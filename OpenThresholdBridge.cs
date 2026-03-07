using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace RAVEN;

/// <summary>
/// Connects RecoIP image handles to our DynamicThreshold algorithm.
/// Reads pixels from handles in memory, thresholds them, writes 1-bit result back.
/// Also preloads JPEGs in the background so threshold doesn't wait for disk.
/// </summary>
public static class OpenThresholdBridge
{
    // Background preload cache - holds grayscale bytes from the current JPEG
    private static byte[] _cachedGray;
    private static int _cachedWidth;
    private static int _cachedHeight;
    private static string _cachedPath;
    private static readonly object _cacheLock = new object();

    /// <summary>
    /// Load a JPEG into grayscale bytes in memory (call from background thread).
    /// Call this when navigating to a new image so grayscale is ready before threshold.
    /// </summary>
    public static void PreloadGrayscale(string jpgPath)
    {
        try
        {
            using var mat = CvInvoke.Imread(jpgPath, ImreadModes.Grayscale);
            if (mat.IsEmpty) return;

            int w = mat.Width;
            int h = mat.Height;
            byte[] gray = new byte[w * h];

            for (int y = 0; y < h; y++)
                Marshal.Copy(mat.DataPointer + y * mat.Step, gray, y * w, w);

            lock (_cacheLock)
            {
                _cachedGray = gray;
                _cachedWidth = w;
                _cachedHeight = h;
                _cachedPath = jpgPath?.ToLower();
            }
        }
        catch
        {
            // Not critical - we fall back to reading from the RecoIP handle
        }
    }

    /// <summary>
    /// Clear cached data (call when navigating away from an image).
    /// </summary>
    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedGray = null;
            _cachedPath = null;
        }
    }

    /// <summary>
    /// Drop-in replacement for RecoIP.ImgDynamicThresholdAverage.
    /// Pass jpgPath for the full-image case so it can use the preloaded cache.
    /// For partial/cropped images, omit jpgPath and it reads from the handle.
    /// </summary>
    public static void ApplyThreshold(int imageHandle, int windowW, int windowH,
        int contrast, int brightness, string jpgPath = null)
    {
        byte[] gray;
        int width, height;

        // Try the background cache first (only works for full-image threshold)
        if (jpgPath != null && TryGetCache(jpgPath, out gray, out width, out height))
        {
            // Cache hit - no need to touch the RecoIP handle for pixel data
        }
        else
        {
            // Read pixels directly from the RecoIP handle's memory
            (gray, width, height) = ReadGrayscaleFromHandle(imageHandle);
        }

        // Run our open-source threshold
        byte[] binary = DynamicThreshold.Apply(gray, width, height, windowW, windowH, contrast, brightness);

        // Write the 1-bit result back into the RecoIP handle
        Write1BitToHandle(imageHandle, binary, width, height);
    }

    private static bool TryGetCache(string jpgPath, out byte[] gray, out int width, out int height)
    {
        lock (_cacheLock)
        {
            if (_cachedGray != null && _cachedPath == jpgPath.ToLower())
            {
                gray = _cachedGray;
                width = _cachedWidth;
                height = _cachedHeight;
                return true;
            }
        }
        gray = null;
        width = height = 0;
        return false;
    }

    // -----------------------------------------------------------------------
    // DIB = Device Independent Bitmap. It's how Windows stores images in memory.
    // Layout: [BITMAPINFOHEADER (40 bytes)] [Color table] [Pixel data]
    // RecoIP gives us a pointer to this memory via ImgGetDIBHandle.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Read grayscale pixels from a RecoIP image handle.
    /// Works with 8bpp (grayscale) and 24bpp (color, converts to gray).
    /// </summary>
    private static (byte[] gray, int width, int height) ReadGrayscaleFromHandle(int imageHandle)
    {
        int dibHandle = RecoIP.ImgGetDIBHandle(imageHandle);
        IntPtr dib = new IntPtr(dibHandle);

        // Read the header to find image dimensions and format
        int biWidth = Marshal.ReadInt32(dib, 4);
        int biHeight = Marshal.ReadInt32(dib, 8);      // positive = bottom-up (standard)
        short bpp = Marshal.ReadInt16(dib, 14);         // bits per pixel (8 or 24)
        int headerSize = Marshal.ReadInt32(dib, 0);     // usually 40

        int absHeight = Math.Abs(biHeight);
        bool bottomUp = biHeight > 0;

        // Skip past header + color table to get to pixel data
        int colorTableBytes = (bpp <= 8) ? (1 << bpp) * 4 : 0;
        int pixelStart = headerSize + colorTableBytes;

        // Row stride (each row padded to 4-byte boundary)
        int stride = ((biWidth * bpp + 31) / 32) * 4;

        byte[] gray = new byte[biWidth * absHeight];

        for (int y = 0; y < absHeight; y++)
        {
            // Bottom-up DIBs store the last row first
            int srcY = bottomUp ? (absHeight - 1 - y) : y;
            IntPtr rowPtr = dib + pixelStart + srcY * stride;

            if (bpp == 8)
            {
                Marshal.Copy(rowPtr, gray, y * biWidth, biWidth);
            }
            else if (bpp == 24)
            {
                // Convert BGR to grayscale: gray = 0.299*R + 0.587*G + 0.114*B
                byte[] row = new byte[biWidth * 3];
                Marshal.Copy(rowPtr, row, 0, biWidth * 3);
                for (int x = 0; x < biWidth; x++)
                {
                    int b = row[x * 3];
                    int g = row[x * 3 + 1];
                    int r = row[x * 3 + 2];
                    gray[y * biWidth + x] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported bit depth: {bpp}bpp (expected 8 or 24)");
            }
        }

        return (gray, biWidth, absHeight);
    }

    /// <summary>
    /// Build a 1-bit black/white image and set it on the RecoIP handle.
    /// This matches what Recogniform's ImgDynamicThresholdAverage produces.
    /// </summary>
    private static void Write1BitToHandle(int imageHandle, byte[] binary, int width, int height)
    {
        // 1-bit: each row is packed into bits, padded to 4-byte boundary
        int stride = ((width + 31) / 32) * 4;

        int headerSize = 40;
        int colorTableSize = 8;   // 2 colors: black and white
        int totalSize = headerSize + colorTableSize + stride * height;

        byte[] dib = new byte[totalSize];

        // BITMAPINFOHEADER
        BitConverter.GetBytes(40).CopyTo(dib, 0);           // biSize
        BitConverter.GetBytes(width).CopyTo(dib, 4);         // biWidth
        BitConverter.GetBytes(height).CopyTo(dib, 8);        // biHeight (positive = bottom-up)
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);     // biPlanes
        BitConverter.GetBytes((short)1).CopyTo(dib, 14);     // biBitCount

        // Color table: [black, white]
        // Black entry (index 0) is already zeros
        // White entry (index 1):
        dib[headerSize + 4] = 255;  // B
        dib[headerSize + 5] = 255;  // G
        dib[headerSize + 6] = 255;  // R

        // Pack pixels into bits (bottom-up: first row in DIB = last row in image)
        int pixelOffset = headerSize + colorTableSize;
        for (int y = 0; y < height; y++)
        {
            int srcY = height - 1 - y;
            int rowStart = pixelOffset + y * stride;

            for (int x = 0; x < width; x++)
            {
                if (binary[srcY * width + x] == 255)
                    dib[rowStart + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }

        // Copy to unmanaged memory and give to RecoIP
        IntPtr dibPtr = Marshal.AllocHGlobal(totalSize);
        Marshal.Copy(dib, 0, dibPtr, totalSize);
        RecoIP.ImgSetDIBHandle(imageHandle, dibPtr.ToInt32());
    }
}
