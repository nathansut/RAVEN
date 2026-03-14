using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace RAVEN;

/// <summary>
/// Pure C# threshold pipeline for RDynamic.
/// Caching and orchestration layer for RDynamic/Refine paths. All image I/O via RavenImaging (GDI+).
/// </summary>
public static class OpenThresholdBridge
{
    // Grayscale cache: preloaded from the current JPEG so threshold doesn't wait for disk.
    // _cachedGray uses the LUT formula (matches Recogniform DT internally).
    // _cachedGrayAvg uses the avg formula (matches Recogniform ImgConvertToGrayScale for Refine).
    private static byte[]  _cachedGray;
    private static byte[]  _cachedGrayAvg;
    private static int     _cachedWidth;
    private static int     _cachedHeight;
    private static string  _cachedPath;
    private static readonly object _cacheLock = new object();



    // TIF bitmap cache: keeps the current output TIF in memory so partial
    // composites don't re-read from disk on every keystroke.
    private static byte[]  _cachedTif;
    private static int     _cachedTifW;
    private static int     _cachedTifH;
    private static string  _cachedTifPath;
    private static readonly object _tifLock = new object();

    // Background save with skip-if-busy: at most one save runs at a time.
    // If the user scrolls contrast/brightness faster than the save can keep up,
    // intermediate saves are skipped — only the latest pixels get written to disk.
    // The display always updates instantly from the in-memory _cachedTif.
    //
    // How it works:
    //   - QueueSave stores the latest request in _nextSave* fields.
    //   - If no save is running, it starts one immediately.
    //   - If a save IS running, it just returns (the request is remembered).
    //   - When a save finishes, it checks _nextSave*. If a new request came in
    //     while it was saving, it starts that one automatically.
    private static Task _pendingSave;
    private static bool _saveRunning;
    private static byte[] _nextSavePixels;
    private static int _nextSaveWidth, _nextSaveHeight;
    private static string _nextSavePath;
    private static readonly object _saveLock = new object();

    /// <summary>Last operation split timings (ms).</summary>
    public static long LastDecodeMs { get; private set; }
    public static long LastThresholdMs { get; private set; }
    public static long LastWriteMs { get; private set; }

    /// <summary>True while a background save is in progress.</summary>
    public static bool IsSaveInProgress
    {
        get { lock (_saveLock) return _saveRunning; }
    }

    /// <summary>
    /// Callback fired on a background thread when save completes.
    /// Parameter is the write duration in ms. Use BeginInvoke to marshal to UI thread.
    /// </summary>
    public static Action<long> OnSaveCompleted { get; set; }

    /// <summary>
    /// Block until all pending saves finish (including any queued next-save).
    /// Call before navigation or before starting a new threshold on a different image.
    /// </summary>
    public static void WaitForPendingSave()
    {
        // Loop because completing one save may start another (from _nextSave*)
        while (true)
        {
            Task t;
            lock (_saveLock) t = _pendingSave;
            if (t == null || t.IsCompleted) break;
            try { t.Wait(); } catch { }
        }
    }

    /// <summary>
    /// Preload a JPEG into the grayscale cache (call from a background thread when
    /// navigating to a new image so the bytes are ready before threshold is triggered).
    /// </summary>
    public static void PreloadGrayscale(string jpgPath)
    {
        try
        {
            var (gray, grayAvg, w, h) = RavenImaging.LoadImageAsGrayscaleDual(jpgPath);

            lock (_cacheLock)
            {
                _cachedGray    = gray;
                _cachedGrayAvg = grayAvg;
                _cachedWidth   = w;
                _cachedHeight  = h;
                _cachedPath    = jpgPath.ToLower();
            }
        }
        catch { }
    }

    /// <summary>Get the cached TIF pixel data for in-memory display (avoids disk reload).</summary>
    public static bool TryGetDisplayPixels(out byte[] pixels, out int width, out int height)
    {
        lock (_tifLock)
        {
            if (_cachedTif != null)
            {
                pixels = _cachedTif;
                width  = _cachedTifW;
                height = _cachedTifH;
                return true;
            }
        }
        pixels = null; width = height = 0;
        return false;
    }

    /// <summary>Call when navigating away from an image to free the cached bytes.</summary>
    /// <remarks>
    /// Does NOT block on pending save — the save task holds its own byte[] reference,
    /// so clearing the cache pointer is safe. This keeps navigation instant even if
    /// the previous image's TIF save is still writing to disk.
    /// </remarks>
    public static void ClearCache()
    {
        lock (_cacheLock)  { _cachedGray = null; _cachedGrayAvg = null; _cachedPath = null; }
        lock (_tifLock)    { _cachedTif = null; _cachedTifPath = null; }
    }

    /// <summary>
    /// Full-image path: read source JPG, threshold, write TIF directly.
    /// Zero RavenImaging calls.
    /// </summary>
    public static void ApplyThresholdToFile(string inputJpgPath, string outputTifPath,
        int windowW, int windowH, int contrast, int brightness,
        bool refineThreshold = false, int refineTolerance = 10)
    {
        byte[] gray;     // LUT gray for DT
        byte[] grayAvg;  // avg gray for RefineThreshold
        int width, height;

        var sw = Stopwatch.StartNew();
        if (TryGetCache(inputJpgPath, out gray, out grayAvg, out width, out height))
        { /* fastest — already in memory */ }
        else
        { (gray, grayAvg, width, height) = RavenImaging.LoadImageAsGrayscaleDual(inputJpgPath); }
        sw.Stop();
        LastDecodeMs = sw.ElapsedMilliseconds;

        sw.Restart();
        byte[] binary = RavenImaging.DynamicThresholdApply(gray, width, height, windowW, windowH, contrast, brightness);
        if (refineThreshold)
            binary = RavenImaging.RefineThresholdApply(binary, grayAvg, width, height, refineTolerance);
        sw.Stop();
        LastThresholdMs = sw.ElapsedMilliseconds;

        // Cache the TIF bitmap immediately so display-from-memory works
        lock (_tifLock)
        {
            _cachedTif     = binary;
            _cachedTifW    = width;
            _cachedTifH    = height;
            _cachedTifPath = outputTifPath.ToLower();
        }

        // Queue save in background — display updates before disk write finishes
        LastWriteMs = -1; // indicate save is pending
        QueueSave(binary, width, height, outputTifPath);
    }

    /// <summary>
    /// Partial-image path: read source JPG, crop to (x1,y1,x2,y2), threshold the
    /// crop, then composite into the output TIF.
    /// Uses cached TIF bitmap to avoid re-reading the full TIF from disk.
    /// Zero RavenImaging calls.
    /// </summary>
    public static void ApplyThresholdToFilePartial(string inputJpgPath, string outputTifPath,
        int x1, int y1, int x2, int y2,
        int windowW, int windowH, int contrast, int brightness,
        bool refineThreshold = false, int refineTolerance = 10)
    {
        WaitForPendingSave(); // ensure previous save is on disk before we read/composite

        byte[] gray;     // LUT gray for DT
        byte[] grayAvg;  // avg gray for RefineThreshold
        int fullWidth, fullHeight;

        var sw = Stopwatch.StartNew();
        if (TryGetCache(inputJpgPath, out gray, out grayAvg, out fullWidth, out fullHeight))
        { /* fastest — already in memory */ }
        else
        { (gray, grayAvg, fullWidth, fullHeight) = RavenImaging.LoadImageAsGrayscaleDual(inputJpgPath); }
        sw.Stop();
        LastDecodeMs = sw.ElapsedMilliseconds;

        // Clamp coordinates
        x1 = Math.Max(0, Math.Min(x1, fullWidth));
        y1 = Math.Max(0, Math.Min(y1, fullHeight));
        x2 = Math.Max(0, Math.Min(x2, fullWidth));
        y2 = Math.Max(0, Math.Min(y2, fullHeight));
        int cropW = x2 - x1, cropH = y2 - y1;
        if (cropW <= 0 || cropH <= 0) return;

        // Extract crop from LUT grayscale buffer (for DT)
        byte[] cropGray = new byte[cropW * cropH];
        for (int row = 0; row < cropH; row++)
            Buffer.BlockCopy(gray, (y1 + row) * fullWidth + x1, cropGray, row * cropW, cropW);

        // Extract crop from avg grayscale buffer (for RefineThreshold)
        byte[] cropGrayAvg = null;
        if (refineThreshold)
        {
            cropGrayAvg = new byte[cropW * cropH];
            for (int row = 0; row < cropH; row++)
                Buffer.BlockCopy(grayAvg, (y1 + row) * fullWidth + x1, cropGrayAvg, row * cropW, cropW);
        }

        // Threshold the crop
        sw.Restart();
        byte[] binary = RavenImaging.DynamicThresholdApply(cropGray, cropW, cropH, windowW, windowH, contrast, brightness);
        if (refineThreshold)
            binary = RavenImaging.RefineThresholdApply(binary, cropGrayAvg, cropW, cropH, refineTolerance);
        sw.Stop();
        LastThresholdMs = sw.ElapsedMilliseconds;

        // Get or build the full TIF bitmap
        byte[] tifPixels;
        int tifW, tifH;
        if (TryGetTifCache(outputTifPath, out tifPixels, out tifW, out tifH)
            && tifW == fullWidth && tifH == fullHeight)
        {
            // Have cached TIF — no disk read needed
        }
        else if (File.Exists(outputTifPath))
        {
            // Cache miss — load from disk
            (tifPixels, tifW, tifH) = RavenImaging.LoadImageAsGrayscale(outputTifPath);
        }
        else
        {
            // No existing TIF — create a white bitmap
            tifW = fullWidth; tifH = fullHeight;
            tifPixels = new byte[tifW * tifH];
            Array.Fill(tifPixels, (byte)255);
        }

        // Composite: paste thresholded crop into TIF at (x1, y1)
        for (int row = 0; row < cropH; row++)
            Buffer.BlockCopy(binary, row * cropW, tifPixels, (y1 + row) * tifW + x1, cropW);

        // Cache immediately for display
        lock (_tifLock)
        {
            _cachedTif     = tifPixels;
            _cachedTifW    = tifW;
            _cachedTifH    = tifH;
            _cachedTifPath = outputTifPath.ToLower();
        }

        LastWriteMs = -1;
        QueueSave(tifPixels, tifW, tifH, outputTifPath);
    }

    /// <summary>
    /// Drop-in replacement for RavenImaging.ImgDynamicThresholdAverage (handle-based).
    /// Only used for non-RDynamic fallback paths.
    /// </summary>
    public static int ApplyThreshold(int imageHandle, int windowW, int windowH,
        int contrast, int brightness, string jpgPath = null)
    {
        byte[] gray;
        int width, height;

        if (jpgPath != null && TryGetCache(jpgPath, out gray, out _, out width, out height))
        { }
        else if (jpgPath != null)
        { (gray, _, width, height) = RavenImaging.LoadImageAsGrayscaleDual(jpgPath); }
        else
        {
            string tmpGray = Path.ChangeExtension(Path.GetTempFileName(), ".tif");
            try
            {
                RavenImaging.ImgSaveAsTif(imageHandle, tmpGray, 0, 0);
                (gray, width, height) = RavenImaging.LoadImageAsGrayscale(tmpGray);
            }
            finally { try { File.Delete(tmpGray); } catch { } }
        }

        byte[] binary = RavenImaging.DynamicThresholdApply(gray, width, height, windowW, windowH, contrast, brightness);

        string tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".tif");
        try
        {
            RavenImaging.SaveAsCcitt4Tif(binary, width, height, tempFile);

            int newHandle = RavenImaging.ImgOpen(tempFile, 1);
            if (newHandle == 0) throw new Exception($"ImgOpen returned 0 for temp TIF: {tempFile}");

            RavenImaging.ImgDelete(imageHandle);
            return newHandle;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>
    /// Negative/Photostat full-image path: invert grayscale, threshold, save TIF.
    /// </summary>
    public static void ApplyThresholdToFileNegative(string inputJpgPath, string outputTifPath,
        int windowW, int windowH, int contrast, int brightness,
        bool refineThreshold = false, int refineTolerance = 10)
    {
        byte[] gray;     // LUT gray for DT
        byte[] grayAvg;  // avg gray for RefineThreshold
        int width, height;

        var sw = Stopwatch.StartNew();
        byte[] srcGray, srcAvg;
        if (TryGetCache(inputJpgPath, out srcGray, out srcAvg, out width, out height))
        { /* cached */ }
        else
        { (srcGray, srcAvg, width, height) = RavenImaging.LoadImageAsGrayscaleDual(inputJpgPath); }

        // Clone + invert in a single parallel pass (avoids 2x sequential copy + invert)
        int len = width * height;
        gray = new byte[len];
        grayAvg = srcAvg != null ? new byte[len] : null;
        Parallel.For(0, height, y =>
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                gray[row + x] = (byte)(255 - srcGray[row + x]);
                if (grayAvg != null) grayAvg[row + x] = (byte)(255 - srcAvg[row + x]);
            }
        });
        sw.Stop();
        LastDecodeMs = sw.ElapsedMilliseconds;

        sw.Restart();
        byte[] binary = RavenImaging.DynamicThresholdApply(gray, width, height, windowW, windowH, contrast, brightness);
        if (refineThreshold)
            binary = RavenImaging.RefineThresholdApply(binary, grayAvg, width, height, refineTolerance);
        sw.Stop();
        LastThresholdMs = sw.ElapsedMilliseconds;

        lock (_tifLock)
        {
            _cachedTif     = binary;
            _cachedTifW    = width;
            _cachedTifH    = height;
            _cachedTifPath = outputTifPath.ToLower();
        }

        LastWriteMs = -1;
        QueueSave(binary, width, height, outputTifPath);
    }

    /// <summary>
    /// Negative/Photostat partial path: invert crop, threshold, composite into TIF.
    /// </summary>
    public static void ApplyThresholdToFilePartialNegative(string inputJpgPath, string outputTifPath,
        int x1, int y1, int x2, int y2,
        int windowW, int windowH, int contrast, int brightness,
        bool refineThreshold = false, int refineTolerance = 10)
    {
        WaitForPendingSave(); // ensure previous save is on disk before we read/composite

        byte[] gray;     // LUT gray for DT
        byte[] grayAvg;  // avg gray for RefineThreshold
        int fullWidth, fullHeight;

        var sw = Stopwatch.StartNew();
        if (TryGetCache(inputJpgPath, out gray, out grayAvg, out fullWidth, out fullHeight))
        { /* fastest — already in memory */ }
        else
        { (gray, grayAvg, fullWidth, fullHeight) = RavenImaging.LoadImageAsGrayscaleDual(inputJpgPath); }
        sw.Stop();
        LastDecodeMs = sw.ElapsedMilliseconds;

        // Clamp coordinates
        x1 = Math.Max(0, Math.Min(x1, fullWidth));
        y1 = Math.Max(0, Math.Min(y1, fullHeight));
        x2 = Math.Max(0, Math.Min(x2, fullWidth));
        y2 = Math.Max(0, Math.Min(y2, fullHeight));
        int cropW = x2 - x1, cropH = y2 - y1;
        if (cropW <= 0 || cropH <= 0) return;

        // Extract crop from LUT gray and invert for negative/photostat
        byte[] cropGray = new byte[cropW * cropH];
        for (int row = 0; row < cropH; row++)
            Buffer.BlockCopy(gray, (y1 + row) * fullWidth + x1, cropGray, row * cropW, cropW);
        for (int i = 0; i < cropGray.Length; i++)
            cropGray[i] = (byte)(255 - cropGray[i]);

        // Extract crop from avg gray and invert (for RefineThreshold)
        byte[] cropGrayAvg = null;
        if (refineThreshold)
        {
            cropGrayAvg = new byte[cropW * cropH];
            for (int row = 0; row < cropH; row++)
                Buffer.BlockCopy(grayAvg, (y1 + row) * fullWidth + x1, cropGrayAvg, row * cropW, cropW);
            for (int i = 0; i < cropGrayAvg.Length; i++)
                cropGrayAvg[i] = (byte)(255 - cropGrayAvg[i]);
        }

        // Threshold the inverted crop
        sw.Restart();
        byte[] binary = RavenImaging.DynamicThresholdApply(cropGray, cropW, cropH, windowW, windowH, contrast, brightness);
        if (refineThreshold)
            binary = RavenImaging.RefineThresholdApply(binary, cropGrayAvg, cropW, cropH, refineTolerance);
        sw.Stop();
        LastThresholdMs = sw.ElapsedMilliseconds;

        // Get or build the full TIF bitmap
        byte[] tifPixels;
        int tifW, tifH;
        if (TryGetTifCache(outputTifPath, out tifPixels, out tifW, out tifH)
            && tifW == fullWidth && tifH == fullHeight)
        { }
        else if (File.Exists(outputTifPath))
        { (tifPixels, tifW, tifH) = RavenImaging.LoadImageAsGrayscale(outputTifPath); }
        else
        {
            tifW = fullWidth; tifH = fullHeight;
            tifPixels = new byte[tifW * tifH];
            Array.Fill(tifPixels, (byte)255);
        }

        // Composite
        for (int row = 0; row < cropH; row++)
            Buffer.BlockCopy(binary, row * cropW, tifPixels, (y1 + row) * tifW + x1, cropW);

        lock (_tifLock)
        {
            _cachedTif     = tifPixels;
            _cachedTifW    = tifW;
            _cachedTifH    = tifH;
            _cachedTifPath = outputTifPath.ToLower();
        }

        LastWriteMs = -1;
        QueueSave(tifPixels, tifW, tifH, outputTifPath);
    }




    // -------------------------------------------------------------------------
    // Background save
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queue a background save. If a save is already running, the request is
    /// stored and will run automatically when the current save finishes.
    /// If multiple requests arrive while a save is running, only the latest is kept.
    /// </summary>
    private static void QueueSave(byte[] pixels, int width, int height, string outputPath)
    {
        lock (_saveLock)
        {
            // Always store the latest request (overwrites any previous queued request)
            _nextSavePixels = pixels;
            _nextSaveWidth = width;
            _nextSaveHeight = height;
            _nextSavePath = outputPath;

            // If a save is already running, it will pick this up when it finishes
            if (_saveRunning) return;

            // No save running — start one now
            StartNextSave();
        }
    }

    /// <summary>
    /// Start a background save from _nextSave* fields. Must be called under _saveLock.
    /// </summary>
    private static void StartNextSave()
    {
        byte[] pixels = _nextSavePixels;
        int width = _nextSaveWidth;
        int height = _nextSaveHeight;
        string path = _nextSavePath;
        _nextSavePixels = null;
        _saveRunning = true;

        _pendingSave = Task.Run(() =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                RavenImaging.SaveAsCcitt4Tif(pixels, width, height, path);
                sw.Stop();
                LastWriteMs = sw.ElapsedMilliseconds;
                OnSaveCompleted?.Invoke(sw.ElapsedMilliseconds);
            }
            finally
            {
                lock (_saveLock)
                {
                    _saveRunning = false;
                    // If another request arrived while we were saving, start it now
                    if (_nextSavePixels != null)
                        StartNextSave();
                }
            }
        });
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool TryGetCache(string jpgPath, out byte[] gray, out byte[] grayAvg,
        out int width, out int height)
    {
        lock (_cacheLock)
        {
            if (_cachedGray != null && _cachedPath == jpgPath.ToLower())
            {
                gray    = _cachedGray;
                grayAvg = _cachedGrayAvg;
                width   = _cachedWidth;
                height  = _cachedHeight;
                return true;
            }
        }
        gray = null; grayAvg = null; width = height = 0;
        return false;
    }

    private static bool TryGetTifCache(string tifPath, out byte[] pixels, out int width, out int height)
    {
        lock (_tifLock)
        {
            if (_cachedTif != null && _cachedTifPath == tifPath.ToLower())
            {
                pixels = _cachedTif;
                width  = _cachedTifW;
                height = _cachedTifH;
                return true;
            }
        }
        pixels = null; width = height = 0;
        return false;
    }








}
