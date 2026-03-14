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
    // _cachedGray uses the NTSC LUT formula (R=0.30, G=0.59, B=0.11) for DynamicThreshold.
    // _cachedGrayAvg uses the avg formula ((R+G+B+1)/3) for RefineThreshold.
    // _cachedBgr + _cachedBgrStride: raw BGR pixel data for photostat pipeline.
    private static byte[]  _cachedGray;
    private static byte[]  _cachedGrayAvg;
    private static byte[]  _cachedBgr;
    private static int     _cachedBgrStride;
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
    private static byte[] _nextSavePacked; // pre-packed 1bpp data (skips packing in save)
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
            // Load grayscale + BGR in one decode (photostat needs BGR for bleed-through).
            var (grayLut, bgr, bgrStride, w, h) = RavenImaging.LoadImageAsGrayscaleAndBgr(jpgPath);

            // Compute avg grayscale for Refine from the BGR data
            byte[] grayAvg = new byte[w * h];
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                int srcRow = y * bgrStride;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    int off = srcRow + x * 3;
                    grayAvg[dstRow + x] = (byte)((bgr[off + 2] + bgr[off + 1] + bgr[off] + 1) / 3);
                }
            });

            lock (_cacheLock)
            {
                _cachedGray      = grayLut;
                _cachedGrayAvg   = grayAvg;
                _cachedBgr       = bgr;
                _cachedBgrStride = bgrStride;
                _cachedWidth     = w;
                _cachedHeight    = h;
                _cachedPath      = jpgPath.ToLower();
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
        lock (_cacheLock)  { _cachedGray = null; _cachedGrayAvg = null; _cachedBgr = null; _cachedPath = null; }
        lock (_tifLock)    { _cachedTif = null; _cachedTifPath = null; }
    }

    /// <summary>
    /// Full-image path: read source JPG, threshold, write TIF directly.
    /// Zero RavenImaging calls.
    /// </summary>
    public static void ApplyThresholdToFile(string inputJpgPath, string outputTifPath,
        int windowW, int windowH, int contrast, int brightness,
        bool refineThreshold = false, int refineTolerance = 10, int despeckle = 0)
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
        byte[] packed = null;
        if (despeckle > 0)
        {
            // Despeckle returns packed 1bpp data — reuse it for save to avoid double-packing
            var result = RavenImaging.DespeckleBytesPacked(binary, width, height, despeckle, despeckle);
            packed = result.packed;
        }
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
        QueueSave(binary, width, height, outputTifPath, packed);
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
        bool refineThreshold = false, int refineTolerance = 10, int despeckle = 0)
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
        if (despeckle > 0)
            RavenImaging.DespeckleBytes(binary, cropW, cropH, despeckle, despeckle);
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
        if (jpgPath == null)
        {
            // In-memory path — no disk I/O needed
            RavenImaging.ImgDynamicThresholdAverage(imageHandle, windowW, windowH, contrast, brightness);
            return imageHandle;
        }

        byte[] gray;
        int width, height;

        if (TryGetCache(jpgPath, out gray, out _, out width, out height))
        { }
        else
        { (gray, _, width, height) = RavenImaging.LoadImageAsGrayscaleDual(jpgPath); }

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
        bool refineThreshold = false, int refineTolerance = 10, int despeckle = 0)
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
        if (despeckle > 0)
            RavenImaging.DespeckleBytes(binary, width, height, despeckle, despeckle);
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
        bool refineThreshold = false, int refineTolerance = 10, int despeckle = 0)
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
        if (despeckle > 0)
            RavenImaging.DespeckleBytes(binary, cropW, cropH, despeckle, despeckle);
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




    /// <summary>
    /// Full photostat (negative) pipeline on byte arrays. No GDI+ handles.
    /// Optimized: cached BGR, fast bleedthrough (7x), overlapped borders + BT background,
    /// parallel BT apply, 4-way parallel AT+DT. Total ~540ms with cache (was ~2000ms).
    /// </summary>
    public static void ApplyThresholdToFilePhotostat(string inputJpgPath, string outputTifPath,
        int windowW, int windowH, int contrast, int brightness, int despeckle)
    {
        byte[] grayLut, bgr;
        int bgrStride, W, H;

        var sw = Stopwatch.StartNew();
        if (TryGetCacheBgr(inputJpgPath, out grayLut, out bgr, out bgrStride, out W, out H))
        { /* fastest — already in memory, BGR cloned */ }
        else
        { (grayLut, bgr, bgrStride, W, H) = RavenImaging.LoadImageAsGrayscaleAndBgr(inputJpgPath); }
        sw.Stop();
        LastDecodeMs = sw.ElapsedMilliseconds;

        sw.Restart();

        // Phase 1: Overlap border detection (on grayLut) with BT background detection (on BGR)
        int T = 0;
        int aLeft = 0, aTop = 0, aRight = 0, aBottom = 0;
        int bLeft = 0, bTop = 0, bRight = 0, bBottom = 0;
        int cLeft = 0, cTop = 0, cRight = 0, cBottom = 0;
        int aW = 0, aH = 0, bW = 0, bH = 0, contentW = 0, contentH = 0;
        byte[] aGray = null;
        int btBgH = 0, btBgS = 0, btBgL = 0, btOtsu = 0;
        bool borderFailed = false;

        Parallel.Invoke(
            () =>
            {
                // Border detection chain (uses grayLut only)
                T = RavenImaging.NativeAutoThresholdPublic(grayLut, W, H, W, 2);
                byte[] borderPacked = RavenImaging.ThresholdAndPack1bpp(grayLut, W, H, T);
                int byteW = (W + 7) / 8;

                aLeft   = RavenImaging.FindBlackBorderDirect(borderPacked, byteW, W, H, 1, 0, 90.0, 1);
                aTop    = RavenImaging.FindBlackBorderDirect(borderPacked, byteW, W, H, 1, 2, 90.0, 1);
                aRight  = RavenImaging.FindBlackBorderDirect(borderPacked, byteW, W, H, 1, 1, 90.0, 1);
                aBottom = RavenImaging.FindBlackBorderDirect(borderPacked, byteW, W, H, 1, 3, 90.0, 1);
                if (aLeft > aRight || aTop > aBottom) { borderFailed = true; return; }

                aW = aRight - aLeft + 1; aH = aBottom - aTop + 1;
                aGray = RavenImaging.ExtractGrayscaleSubRegion(grayLut, W, aLeft, aTop, aRight, aBottom);
                byte[] aCropPacked = RavenImaging.ThresholdAndPack1bpp(aGray, aW, aH, T);
                int aCropByteW = (aW + 7) / 8;
                for (int i = 0; i < aCropPacked.Length; i++) aCropPacked[i] = (byte)~aCropPacked[i];

                bLeft   = RavenImaging.FindBlackBorderDirect(aCropPacked, aCropByteW, aW, aH, 1, 0, 99.0, 1);
                bTop    = RavenImaging.FindBlackBorderDirect(aCropPacked, aCropByteW, aW, aH, 1, 2, 99.0, 1);
                bRight  = RavenImaging.FindBlackBorderDirect(aCropPacked, aCropByteW, aW, aH, 1, 1, 99.0, 1);
                bBottom = RavenImaging.FindBlackBorderDirect(aCropPacked, aCropByteW, aW, aH, 1, 3, 99.0, 1);
                bLeft += 20; bRight -= 20;
                if (bLeft > bRight || bTop > bBottom) { borderFailed = true; return; }

                bW = bRight - bLeft + 1; bH = bBottom - bTop + 1;
                byte[] bGrayForBorder = RavenImaging.ExtractGrayscaleSubRegion(aGray, aW, bLeft, bTop, bRight, bBottom);
                byte[] bCropPacked = RavenImaging.ThresholdAndPack1bpp(bGrayForBorder, bW, bH, T);
                int bCropByteW = (bW + 7) / 8;
                for (int i = 0; i < bCropPacked.Length; i++) bCropPacked[i] = (byte)~bCropPacked[i];

                cLeft   = RavenImaging.FindBlackBorderDirect(bCropPacked, bCropByteW, bW, bH, 1, 0, 80.0, 30);
                cTop    = RavenImaging.FindBlackBorderDirect(bCropPacked, bCropByteW, bW, bH, 1, 2, 80.0, 100);
                cRight  = RavenImaging.FindBlackBorderDirect(bCropPacked, bCropByteW, bW, bH, 1, 1, 80.0, 30);
                cBottom = RavenImaging.FindBlackBorderDirect(bCropPacked, bCropByteW, bW, bH, 1, 3, 80.0, 100);
                contentW = cRight - cLeft + 1; contentH = cBottom - cTop + 1;
            },
            () =>
            {
                // BT background detection (uses BGR only, read-only)
                RavenImaging.RemoveBleedThroughGetBackground(bgr, W, H, bgrStride,
                    out btBgH, out btBgS, out btBgL, out btOtsu);
            }
        );

        if (borderFailed) { sw.Stop(); LastThresholdMs = sw.ElapsedMilliseconds; LastWriteMs = 0; return; }

        // Phase 2: Parallel BT apply (modifies BGR in-place, row-independent)
        int nThreads = Environment.ProcessorCount;
        int rowsPerThread = (H + nThreads - 1) / nThreads;
        Parallel.For(0, nThreads, t =>
        {
            int startY = t * rowsPerThread;
            int endY = Math.Min(startY + rowsPerThread, H);
            if (startY < endY)
                RavenImaging.RemoveBleedThroughApplyRows(bgr, W, H, bgrStride, 1,
                    btBgH, btBgS, btBgL, startY, endY);
        });

        // Phase 3: Post-BT parallel processing
        int absContentLeft  = aLeft + bLeft + cLeft;
        int absContentTop   = aTop  + bTop  + cTop;
        int absContentRight = aLeft + bLeft + cRight;
        int absContentBottom = aTop + bTop + cBottom;
        int absBLeft = aLeft + bLeft, absBTop = aTop + bTop;
        int absBRight = aLeft + bRight, absBBottom = aTop + bBottom;

        // Compute grayPost and contentGray in parallel (both from modified BGR)
        byte[] grayPost = new byte[W * H];
        byte[] contentGray = null;

        Parallel.Invoke(
            () =>
            {
                Parallel.For(0, H, y =>
                {
                    int srcRow = y * bgrStride;
                    int dstRow = y * W;
                    for (int x = 0; x < W; x++)
                    {
                        int off = srcRow + x * 3;
                        grayPost[dstRow + x] = (byte)(RavenImaging.LutR[bgr[off + 2]]
                            + RavenImaging.LutG[bgr[off + 1]] + RavenImaging.LutB[bgr[off]]);
                    }
                });
            },
            () =>
            {
                contentGray = RavenImaging.ExtractGrayscaleFromBgr(bgr, bgrStride,
                    absContentLeft, absContentTop, absContentRight, absContentBottom, invert: true);
            }
        );

        // Extract sub-regions from grayPost (fast memcpy)
        byte[] aGrayPost = RavenImaging.ExtractGrayscaleSubRegion(grayPost, W, aLeft, aTop, aRight, aBottom);
        byte[] bGrayPost = RavenImaging.ExtractGrayscaleSubRegion(grayPost, W, absBLeft, absBTop, absBRight, absBBottom);

        // Phase 4: Run 4 tasks in parallel: 3x AT + content DT+cleanup
        byte[] fullResult = new byte[W * H];
        byte[] aResult = new byte[aW * aH];
        byte[] bResult = new byte[bW * bH];
        byte[] contentBinary = null;

        Parallel.Invoke(
            () => RavenImaging.AdaptiveThresholdApply(grayPost, fullResult, W, H, windowW, windowH, -1, -1),
            () => RavenImaging.AdaptiveThresholdApply(aGrayPost, aResult, aW, aH, windowW, windowH, 40, 230),
            () => RavenImaging.AdaptiveThresholdApply(bGrayPost, bResult, bW, bH, windowW, windowH, 40, 230),
            () =>
            {
                contentBinary = RavenImaging.DynamicThresholdApply(contentGray, contentW, contentH,
                    windowW, windowH, contrast, brightness);
                byte[] contentPacked = RavenImaging.PackTo1bpp(contentBinary, contentW, contentH);
                int cByteW = (contentW + 7) / 8;
                if (despeckle > 0)
                    RavenImaging.DespeckleApply(contentPacked, cByteW, contentW, contentH, despeckle, despeckle);
                RavenImaging.RemoveBlackWiresDirect(contentPacked, cByteW, contentW, contentH);
                int PhHeight = contentH - 10;
                int PhRatio = PhHeight / 5;
                int phBreaks = PhHeight - 15000;
                RavenImaging.RemoveVerticalLinesDirect(contentPacked, cByteW, contentW, contentH, PhHeight, phBreaks, PhRatio);
                RavenImaging.UnpackFrom1bpp(contentPacked, contentBinary, contentW, contentH);
            }
        );

        // Composite: content → page → overscan → full
        RavenImaging.CompositeBytes(contentBinary, contentW, contentH, bResult, bW, cLeft, cTop);
        RavenImaging.CompositeBytes(bResult, bW, bH, aResult, aW, bLeft, bTop);
        RavenImaging.CompositeBytes(aResult, aW, aH, fullResult, W, aLeft, aTop);

        sw.Stop();
        LastThresholdMs = sw.ElapsedMilliseconds;

        lock (_tifLock)
        {
            _cachedTif     = fullResult;
            _cachedTifW    = W;
            _cachedTifH    = H;
            _cachedTifPath = outputTifPath.ToLower();
        }
        LastWriteMs = -1;
        QueueSave(fullResult, W, H, outputTifPath);
    }

    // -------------------------------------------------------------------------
    // Background save
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queue a background save. If a save is already running, the request is
    /// stored and will run automatically when the current save finishes.
    /// If multiple requests arrive while a save is running, only the latest is kept.
    /// </summary>
    private static void QueueSave(byte[] pixels, int width, int height, string outputPath,
        byte[] packed = null)
    {
        lock (_saveLock)
        {
            // Always store the latest request (overwrites any previous queued request)
            _nextSavePixels = pixels;
            _nextSavePacked = packed;
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
        byte[] packed = _nextSavePacked;
        int width = _nextSaveWidth;
        int height = _nextSaveHeight;
        string path = _nextSavePath;
        _nextSavePixels = null;
        _nextSavePacked = null;
        _saveRunning = true;

        _pendingSave = Task.Run(() =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                if (packed != null)
                    RavenImaging.SaveAsCcitt4TifPacked(packed, width, height, path);
                else
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

    private static bool TryGetCacheBgr(string jpgPath, out byte[] grayLut, out byte[] bgr,
        out int bgrStride, out int width, out int height)
    {
        lock (_cacheLock)
        {
            if (_cachedGray != null && _cachedBgr != null && _cachedPath == jpgPath.ToLower())
            {
                grayLut   = _cachedGray;
                bgr       = (byte[])_cachedBgr.Clone(); // clone because bleedthrough modifies it
                bgrStride = _cachedBgrStride;
                width     = _cachedWidth;
                height    = _cachedHeight;
                return true;
            }
        }
        grayLut = null; bgr = null; bgrStride = 0; width = height = 0;
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
