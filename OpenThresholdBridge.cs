using System;
using System.IO;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace RAVEN;

/// <summary>
/// Pure C# threshold pipeline for RDynamic.
/// Reads source images via Emgu.CV (never via RecoIP internals).
/// The only RecoIP calls used are the clean public ones:
///   ImgSaveAsTif  - to extract pixels when no JPG cache is available
///   ImgOpen       - to hand the finished result back as a handle
///   ImgDelete     - to release the old handle
/// </summary>
public static class OpenThresholdBridge
{
    // Grayscale cache: preloaded from the current JPEG so threshold doesn't wait for disk.
    private static byte[]       _cachedGray;
    private static int          _cachedWidth;
    private static int          _cachedHeight;
    private static string       _cachedPath;
    private static readonly object _cacheLock = new object();

    /// <summary>
    /// Preload a JPEG into the grayscale cache (call from a background thread when
    /// navigating to a new image so the bytes are ready before threshold is triggered).
    /// </summary>
    public static void PreloadGrayscale(string jpgPath)
    {
        try
        {
            using var mat = CvInvoke.Imread(jpgPath, ImreadModes.Grayscale);
            if (mat.IsEmpty) return;

            byte[] gray = ExtractGray(mat);

            lock (_cacheLock)
            {
                _cachedGray   = gray;
                _cachedWidth  = mat.Width;
                _cachedHeight = mat.Height;
                _cachedPath   = jpgPath.ToLower();
            }
        }
        catch { /* non-critical — threshold falls back to ImgSaveAsTif path */ }
    }

    /// <summary>Call when navigating away from an image to free the cached bytes.</summary>
    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedGray = null;
            _cachedPath = null;
        }
    }

    /// <summary>
    /// Full-image path: read source JPG, threshold, write TIF directly.
    /// Zero RecoIP calls — the output file is written entirely in C#.
    /// </summary>
    public static void ApplyThresholdToFile(string inputJpgPath, string outputTifPath,
        int windowW, int windowH, int contrast, int brightness)
    {
        byte[] gray;
        int    width, height;

        if (TryGetCache(inputJpgPath, out gray, out width, out height))
        {
            // fastest — already in memory from PreloadGrayscale
        }
        else
        {
            (gray, width, height) = LoadGrayscaleFromFile(inputJpgPath);
        }

        byte[] binary = DynamicThreshold.Apply(gray, width, height, windowW, windowH, contrast, brightness);

        using var mat = new Mat(height, width, DepthType.Cv8U, 1);
        Marshal.Copy(binary, 0, mat.DataPointer, binary.Length);
        CvInvoke.Imwrite(outputTifPath, mat);
    }

    /// <summary>
    /// Drop-in replacement for RecoIP.ImgDynamicThresholdAverage.
    ///
    /// Pixel source priority:
    ///   1. Preloaded grayscale cache (fastest — no disk read)
    ///   2. Load jpgPath directly with Emgu.CV
    ///   3. Export the handle to a temp TIF with ImgSaveAsTif, then read with Emgu.CV
    ///      (used for cropped/Photostat handles that have no associated JPG path)
    ///
    /// After thresholding, the result is written to a temp TIF and loaded back via
    /// ImgOpen so the rest of the pipeline gets a normal RecoIP handle.
    /// The old handle is deleted; callers must reassign: handle = ApplyThreshold(handle, …)
    /// </summary>
    public static int ApplyThreshold(int imageHandle, int windowW, int windowH,
        int contrast, int brightness, string jpgPath = null)
    {
        // --- 1. Get source pixels (pure C#, no RecoIP internals) ---
        byte[] gray;
        int    width, height;

        if (jpgPath != null && TryGetCache(jpgPath, out gray, out width, out height))
        {
            // fastest path — already in memory
        }
        else if (jpgPath != null)
        {
            // cache miss — load the JPG directly with Emgu.CV
            (gray, width, height) = LoadGrayscaleFromFile(jpgPath);
        }
        else
        {
            // no JPG path (cropped/Photostat handle) — export via public RecoIP API,
            // then read with Emgu.CV; never touches DIB internals
            (gray, width, height) = LoadGrayscaleFromHandle(imageHandle);
        }

        // --- 2. Threshold (pure C#) ---
        byte[] binary = DynamicThreshold.Apply(gray, width, height, windowW, windowH, contrast, brightness);

        // --- 3. Write result to temp TIF, load back as a clean RecoIP handle ---
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

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool TryGetCache(string jpgPath, out byte[] gray, out int width, out int height)
    {
        lock (_cacheLock)
        {
            if (_cachedGray != null && _cachedPath == jpgPath.ToLower())
            {
                gray   = _cachedGray;
                width  = _cachedWidth;
                height = _cachedHeight;
                return true;
            }
        }
        gray = null; width = height = 0;
        return false;
    }

    private static (byte[] gray, int width, int height) LoadGrayscaleFromFile(string path)
    {
        using var mat = CvInvoke.Imread(path, ImreadModes.Grayscale);
        if (mat.IsEmpty) throw new Exception($"Could not load image: {path}");
        return (ExtractGray(mat), mat.Width, mat.Height);
    }

    /// <summary>
    /// Get pixels from a RecoIP handle without touching DIB internals.
    /// Exports the handle to a temp TIF via the public ImgSaveAsTif API,
    /// then reads it back with Emgu.CV.
    /// </summary>
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

    private static byte[] ExtractGray(Mat mat)
    {
        byte[] gray = new byte[mat.Width * mat.Height];
        for (int y = 0; y < mat.Height; y++)
            Marshal.Copy(mat.DataPointer + y * mat.Step, gray, y * mat.Width, mat.Width);
        return gray;
    }
}
