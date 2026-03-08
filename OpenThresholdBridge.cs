using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BitMiracle.LibTiff.Classic;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace RAVEN;

/// <summary>
/// Pure C# threshold pipeline for RDynamic.
/// All image I/O via Emgu.CV — zero RecoIP dependency for RDynamic paths.
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

    // Pre-built LUT tables for Recogniform-compatible grayscale (DynamicThreshold).
    // gray = LUT_R[r] + LUT_G[g] + LUT_B[b]  where LUT_X[i] = (byte)Math.Round(i * weight)
    private static readonly byte[] LUT_R = BuildLut(0.30);
    private static readonly byte[] LUT_G = BuildLut(0.59);
    private static readonly byte[] LUT_B = BuildLut(0.11);

    private static byte[] BuildLut(double weight)
    {
        byte[] lut = new byte[256];
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Round(i * weight);
        return lut;
    }

    // TIF bitmap cache: keeps the current output TIF in memory so partial
    // composites don't re-read from disk on every keystroke.
    private static byte[]  _cachedTif;
    private static int     _cachedTifW;
    private static int     _cachedTifH;
    private static string  _cachedTifPath;
    private static readonly object _tifLock = new object();

    // Background save queue: save runs in background so display updates instantly.
    private static Task _pendingSave;
    private static readonly object _saveLock = new object();

    /// <summary>Last operation split timings (ms).</summary>
    public static long LastDecodeMs { get; private set; }
    public static long LastThresholdMs { get; private set; }
    public static long LastWriteMs { get; private set; }

    /// <summary>True while a background save is in progress.</summary>
    public static bool IsSaveInProgress
    {
        get { lock (_saveLock) return _pendingSave != null && !_pendingSave.IsCompleted; }
    }

    /// <summary>
    /// Callback fired on a background thread when save completes.
    /// Parameter is the write duration in ms. Use BeginInvoke to marshal to UI thread.
    /// </summary>
    public static Action<long> OnSaveCompleted { get; set; }

    /// <summary>
    /// Block until any pending background save finishes.
    /// Call before navigation or before starting a new threshold on a different image.
    /// </summary>
    public static void WaitForPendingSave()
    {
        Task t;
        lock (_saveLock) t = _pendingSave;
        try { t?.Wait(); } catch { }
    }

    /// <summary>
    /// Preload a JPEG into the grayscale cache (call from a background thread when
    /// navigating to a new image so the bytes are ready before threshold is triggered).
    /// </summary>
    public static void PreloadGrayscale(string jpgPath)
    {
        try
        {
            using var mat = CvInvoke.Imread(jpgPath, ImreadModes.Color);
            if (mat.IsEmpty) return;

            (byte[] gray, byte[] grayAvg) = ConvertToGrayscaleDual(mat);

            lock (_cacheLock)
            {
                _cachedGray    = gray;
                _cachedGrayAvg = grayAvg;
                _cachedWidth   = mat.Width;
                _cachedHeight  = mat.Height;
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
    /// Zero RecoIP calls.
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
        { (gray, grayAvg, width, height) = LoadGrayscalesFromFile(inputJpgPath); }
        sw.Stop();
        LastDecodeMs = sw.ElapsedMilliseconds;

        sw.Restart();
        byte[] binary = DynamicThreshold.Apply(gray, width, height, windowW, windowH, contrast, brightness);
        if (refineThreshold)
            binary = RefineThreshold.Apply(binary, grayAvg, width, height, refineTolerance);
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
    /// Zero RecoIP calls.
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
        { (gray, grayAvg, fullWidth, fullHeight) = LoadGrayscalesFromFile(inputJpgPath); }
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
        byte[] binary = DynamicThreshold.Apply(cropGray, cropW, cropH, windowW, windowH, contrast, brightness);
        if (refineThreshold)
            binary = RefineThreshold.Apply(binary, cropGrayAvg, cropW, cropH, refineTolerance);
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
            (tifPixels, tifW, tifH) = LoadGrayscaleFromFile(outputTifPath);
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
    /// Drop-in replacement for RecoIP.ImgDynamicThresholdAverage (handle-based).
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
        { (gray, _, width, height) = LoadGrayscalesFromFile(jpgPath); }
        else
        { (gray, width, height) = LoadGrayscaleFromHandle(imageHandle); }

        byte[] binary = DynamicThreshold.Apply(gray, width, height, windowW, windowH, contrast, brightness);

        string tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".tif");
        try
        {
            using var mat = new Mat(height, width, DepthType.Cv8U, 1);
            Marshal.Copy(binary, 0, mat.DataPointer, binary.Length);
            CvInvoke.Imwrite(tempFile, mat);

            int newHandle = RecoIP.ImgOpen(tempFile, 1);
            if (newHandle == 0) throw new Exception($"ImgOpen returned 0 for temp TIF: {tempFile}");

            RecoIP.ImgDelete(imageHandle);
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
        { (srcGray, srcAvg, width, height) = LoadGrayscalesFromFile(inputJpgPath); }

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
        byte[] binary = DynamicThreshold.Apply(gray, width, height, windowW, windowH, contrast, brightness);
        if (refineThreshold)
            binary = RefineThreshold.Apply(binary, grayAvg, width, height, refineTolerance);
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
        { (gray, grayAvg, fullWidth, fullHeight) = LoadGrayscalesFromFile(inputJpgPath); }
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
        byte[] binary = DynamicThreshold.Apply(cropGray, cropW, cropH, windowW, windowH, contrast, brightness);
        if (refineThreshold)
            binary = RefineThreshold.Apply(binary, cropGrayAvg, cropW, cropH, refineTolerance);
        sw.Stop();
        LastThresholdMs = sw.ElapsedMilliseconds;

        // Get or build the full TIF bitmap
        byte[] tifPixels;
        int tifW, tifH;
        if (TryGetTifCache(outputTifPath, out tifPixels, out tifW, out tifH)
            && tifW == fullWidth && tifH == fullHeight)
        { }
        else if (File.Exists(outputTifPath))
        { (tifPixels, tifW, tifH) = LoadGrayscaleFromFile(outputTifPath); }
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
    /// Save pixel data as 1-bit CCITT Group 4 TIFF.
    /// Parallel 8→1-bit conversion, strip-based encoding, atomic .tmp→move.
    /// Returns true if the file was verified on disk with size > 0.
    /// </summary>
    public static bool SaveTif(byte[] pixels, int width, int height, string outputPath)
    {
        string tmpPath = Path.ChangeExtension(outputPath, ".tmp");
        try
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);

            // Parallel 8-bit → 1-bit packed conversion (MSB first, 1=black 0=white)
            int byteWidth = (width + 7) / 8;
            byte[] packed = new byte[byteWidth * height];

            Parallel.For(0, height, y =>
            {
                int src = y * width;
                int dst = y * byteWidth;
                int xFull = width & ~7; // round down to multiple of 8
                for (int x = 0; x < xFull; x += 8)
                {
                    byte b = 0;
                    if (pixels[src + x]     < 128) b |= 0x80;
                    if (pixels[src + x + 1] < 128) b |= 0x40;
                    if (pixels[src + x + 2] < 128) b |= 0x20;
                    if (pixels[src + x + 3] < 128) b |= 0x10;
                    if (pixels[src + x + 4] < 128) b |= 0x08;
                    if (pixels[src + x + 5] < 128) b |= 0x04;
                    if (pixels[src + x + 6] < 128) b |= 0x02;
                    if (pixels[src + x + 7] < 128) b |= 0x01;
                    packed[dst + (x >> 3)] = b;
                }
                // Remaining pixels
                if (xFull < width)
                {
                    byte b = 0;
                    for (int i = 0; i < width - xFull; i++)
                        if (pixels[src + xFull + i] < 128) b |= (byte)(0x80 >> i);
                    packed[dst + (xFull >> 3)] = b;
                }
            });

            // Write Group 4 TIFF — single strip, one WriteEncodedStrip call
            using (var tif = Tiff.Open(tmpPath, "w"))
            {
                if (tif == null) throw new Exception($"Could not create TIFF: {tmpPath}");

                tif.SetField(TiffTag.IMAGEWIDTH, width);
                tif.SetField(TiffTag.IMAGELENGTH, height);
                tif.SetField(TiffTag.BITSPERSAMPLE, 1);
                tif.SetField(TiffTag.SAMPLESPERPIXEL, 1);
                tif.SetField(TiffTag.ORIENTATION, BitMiracle.LibTiff.Classic.Orientation.TOPLEFT);
                tif.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISWHITE);
                tif.SetField(TiffTag.COMPRESSION, Compression.CCITTFAX4);
                tif.SetField(TiffTag.FILLORDER, FillOrder.MSB2LSB);
                tif.SetField(TiffTag.ROWSPERSTRIP, height);
                tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                tif.SetField(TiffTag.XRESOLUTION, 300.0);
                tif.SetField(TiffTag.YRESOLUTION, 300.0);
                tif.SetField(TiffTag.RESOLUTIONUNIT, ResUnit.INCH);

                tif.WriteEncodedStrip(0, packed, packed.Length);
                tif.WriteDirectory();
            }

            // Atomic replace
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(tmpPath, outputPath);

            var fi = new FileInfo(outputPath);
            return fi.Exists && fi.Length > 0;
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Background save
    // -------------------------------------------------------------------------

    private static void QueueSave(byte[] pixels, int width, int height, string outputPath)
    {
        // Wait for any previous save to complete first (can't overlap writes to same file)
        WaitForPendingSave();

        lock (_saveLock)
        {
            _pendingSave = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                SaveTif(pixels, width, height, outputPath);
                sw.Stop();
                LastWriteMs = sw.ElapsedMilliseconds;
                OnSaveCompleted?.Invoke(sw.ElapsedMilliseconds);
            });
        }
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

    /// <summary>Load a color image and return both grayscale conversions.</summary>
    private static (byte[] gray, byte[] grayAvg, int width, int height) LoadGrayscalesFromFile(string path)
    {
        using var mat = CvInvoke.Imread(path, ImreadModes.Color);
        if (mat.IsEmpty)
        {
            // Fallback for 1-bit/8-bit TIF: load as-is and extract single channel
            using var gmat = CvInvoke.Imread(path, ImreadModes.Grayscale);
            if (gmat.IsEmpty) throw new Exception($"Could not load image: {path}");
            byte[] g = ExtractGray(gmat);
            return (g, g, gmat.Width, gmat.Height); // same for both when already gray
        }
        var (lut, avg) = ConvertToGrayscaleDual(mat);
        return (lut, avg, mat.Width, mat.Height);
    }

    /// <summary>Load a single grayscale from file (for TIF reload in partial paths).</summary>
    private static (byte[] gray, int width, int height) LoadGrayscaleFromFile(string path)
    {
        using var mat = CvInvoke.Imread(path, ImreadModes.Grayscale);
        if (mat.IsEmpty) throw new Exception($"Could not load image: {path}");
        return (ExtractGray(mat), mat.Width, mat.Height);
    }

    private static (byte[] gray, int width, int height) LoadGrayscaleFromHandle(int imageHandle)
    {
        string tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".tif");
        try
        {
            RecoIP.ImgSaveAsTif(imageHandle, tempFile, 0, 0);
            return LoadGrayscaleFromFile(tempFile);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>
    /// Compute both grayscale conversions in a single pass over the pixel data.
    /// Bulk-copies the entire Mat to a managed array (one Marshal.Copy instead of
    /// millions of Marshal.ReadByte), then runs a parallel loop computing both grays
    /// for each pixel simultaneously.
    /// </summary>
    private static (byte[] grayLut, byte[] grayAvg) ConvertToGrayscaleDual(Mat mat)
    {
        int w = mat.Width, h = mat.Height;
        byte[] grayLut = new byte[w * h];
        byte[] grayAvg = new byte[w * h];

        if (mat.NumberOfChannels == 1)
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(mat.DataPointer + y * mat.Step, grayLut, y * w, w);
            Buffer.BlockCopy(grayLut, 0, grayAvg, 0, grayLut.Length);
            return (grayLut, grayAvg);
        }

        // Bulk-copy entire image to managed array (one P/Invoke vs ~57M ReadByte calls)
        int step = (int)mat.Step;
        byte[] raw = new byte[step * h];
        Marshal.Copy(mat.DataPointer, raw, 0, raw.Length);

        // Single parallel pass: read each pixel's BGR once, compute both gray values
        Parallel.For(0, h, y =>
        {
            int srcRow = y * step;
            int dstRow = y * w;
            for (int x = 0; x < w; x++)
            {
                int off = srcRow + x * 3;
                byte b = raw[off];
                byte g = raw[off + 1];
                byte r = raw[off + 2];
                grayLut[dstRow + x] = (byte)(LUT_R[r] + LUT_G[g] + LUT_B[b]);
                grayAvg[dstRow + x] = (byte)((r + g + b + 1) / 3);
            }
        });

        return (grayLut, grayAvg);
    }

    private static byte[] ExtractGray(Mat mat)
    {
        byte[] gray = new byte[mat.Width * mat.Height];
        for (int y = 0; y < mat.Height; y++)
            Marshal.Copy(mat.DataPointer + y * mat.Step, gray, y * mat.Width, mat.Width);
        return gray;
    }
}
