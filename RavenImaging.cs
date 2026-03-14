// RavenImaging — drop-in replacement for Recogniform recoip.dll.
// GDI+ Bitmap handle management + all Img* functions.
// Threshold and bleed-through functions delegate to recoip_native.dll (x87 extended precision).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
/// <summary>
/// GDI+-backed replacements for Recogniform recoip.dll.
/// Integer handles map to <see cref="Bitmap"/> objects.
/// </summary>
public static class RavenImaging
{
    // ── TurboJPEG P/Invoke declarations ──

    [DllImport("turbojpeg.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tjInitDecompress();

    [DllImport("turbojpeg.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int tjDecompressHeader3(IntPtr handle, byte[] jpegBuf, uint jpegSize,
        out int width, out int height, out int jpegSubsamp, out int jpegColorspace);

    [DllImport("turbojpeg.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int tjDecompress2(IntPtr handle, byte[] jpegBuf, uint jpegSize,
        byte[] dstBuf, int width, int pitch, int height, int pixelFormat, int flags);

    [DllImport("turbojpeg.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void tjDestroy(IntPtr handle);

    private const int TJPF_RGB = 0;
    private const int TJPF_BGR = 1;

    // ── Native DLL P/Invoke declarations ──

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "AdaptiveThresholdAverage")]
    private static extern void NativeAdaptiveThresholdAverage(
        byte[] gray, byte[] result, int w, int h, int blockW, int blockH, int contrast, int brightness);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "DynamicThresholdAverage")]
    private static extern void NativeDynamicThresholdAverage(
        byte[] gray, byte[] result, int w, int h, int blockW, int blockH, int contrast, int brightness);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "RefineThreshold")]
    private static extern void NativeRefineThreshold(
        byte[] binary, byte[] gray, int w, int h, int tolerance);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "RemoveBleedThrough")]
    private static extern void NativeRemoveBleedThrough(
        byte[] bgr, int w, int h, int stride, int tolerance);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "RemoveBleedThroughFast")]
    private static extern void NativeRemoveBleedThroughFast(
        byte[] bgr, int w, int h, int stride, int tolerance);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "RemoveBleedThroughGetBackground")]
    private static extern void NativeRemoveBleedThroughGetBackground(
        byte[] bgr, int w, int h, int stride,
        out int bgH, out int bgS, out int bgL, out int otsu);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "RemoveBleedThroughApplyRows")]
    private static extern void NativeRemoveBleedThroughApplyRows(
        byte[] bgr, int w, int h, int stride, int tolerance,
        int bgH, int bgS, int bgL, int startY, int endY);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "Despeckle")]
    private static extern int NativeDespeckle(
        byte[] buf, int stride, int w, int h, int maxW, int maxH);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "RemoveBlackWires")]
    private static extern void NativeRemoveBlackWires(
        byte[] buf, int stride, int w, int h);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "RemoveVerticalLines")]
    private static extern int NativeRemoveVerticalLines(
        byte[] buf, int stride, int w, int h, int minVLen, int maxVBreaks, int minBlack);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "AutoThreshold")]
    private static extern int NativeAutoThreshold(
        byte[] gray, int w, int h, int stride, int algo);

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "FindBlackBorder")]
    private static extern int NativeFindBlackBorder(
        byte[] buf, int stride, int w, int h, int bpp, int side, double minBlackPct, int maxHoles);

    // FindBlackBorderBatch: run N border calls on one buffer with a single projection build.
    // FBBCall layout (packed): int side(4) + double minBlackPct(8) + int maxHoles(4) = 16 bytes.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FBBCall
    {
        public int side;
        public double minBlackPct;
        public int maxHoles;
    }

    [DllImport("recoip_native.dll", CallingConvention = CallingConvention.StdCall,
        EntryPoint = "FindBlackBorderBatch")]
    private static extern void NativeFindBlackBorderBatch(
        byte[] buf, int stride, int w, int h, int bpp,
        int nCalls, [In] FBBCall[] calls, [Out] int[] results);

    // ── Grayscale LUTs (Recogniform weights: R=0.30, G=0.59, B=0.11) ──

    private static readonly byte[] _lutR = BuildLut(0.30);
    private static readonly byte[] _lutG = BuildLut(0.59);
    private static readonly byte[] _lutB = BuildLut(0.11);
    private static byte[] BuildLut(double weight)
    {
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Round(i * weight);
        return lut;
    }

    public static byte[] LutR => _lutR;
    public static byte[] LutG => _lutG;
    public static byte[] LutB => _lutB;

    // ── Public byte-array native wrappers (handle-free) ────────────────

    public static int NativeAutoThresholdPublic(byte[] gray, int w, int h, int stride, int algo)
        => NativeAutoThreshold(gray, w, h, stride, algo);

    public static int FindBlackBorderDirect(byte[] buf, int stride, int w, int h,
        int bpp, int side, double minBlackPct, int maxHoles)
        => NativeFindBlackBorder(buf, stride, w, h, bpp, side, minBlackPct, maxHoles);

    public static void RemoveBleedThroughDirect(byte[] bgr, int w, int h, int stride, int tolerance)
        => NativeRemoveBleedThroughFast(bgr, w, h, stride, tolerance);

    public static void RemoveBleedThroughGetBackground(byte[] bgr, int w, int h, int stride,
        out int bgH, out int bgS, out int bgL, out int otsu)
        => NativeRemoveBleedThroughGetBackground(bgr, w, h, stride, out bgH, out bgS, out bgL, out otsu);

    public static void RemoveBleedThroughApplyRows(byte[] bgr, int w, int h, int stride,
        int tolerance, int bgH, int bgS, int bgL, int startY, int endY)
        => NativeRemoveBleedThroughApplyRows(bgr, w, h, stride, tolerance, bgH, bgS, bgL, startY, endY);

    public static void RemoveBlackWiresDirect(byte[] buf, int stride, int w, int h)
        => NativeRemoveBlackWires(buf, stride, w, h);

    public static int RemoveVerticalLinesDirect(byte[] buf, int stride, int w, int h,
        int minVLen, int maxVBreaks, int minBlack)
        => NativeRemoveVerticalLines(buf, stride, w, h, minVLen, maxVBreaks, minBlack);

    public static void AdaptiveThresholdApply(byte[] gray, byte[] result, int w, int h,
        int blockW, int blockH, int contrast, int brightness)
        => NativeAdaptiveThresholdAverage(gray, result, w, h, blockW, blockH, contrast, brightness);

    // ── Handle management ──────────────────────────────────────────────

    private static readonly Dictionary<int, Bitmap> _images = new();
    private static int _nextId = 100;
    private static readonly object _lock = new();

    private static int Store(Bitmap bmp)
    {
        lock (_lock)
        {
            int id = _nextId++;
            _images[id] = bmp;
            return id;
        }
    }

    private static Bitmap Get(int handle)
    {
        lock (_lock)
            return _images.TryGetValue(handle, out var b) ? b : null;
    }

    /// <summary>Internal accessor so USVWin can do direct pixel work on loaded images.</summary>
    internal static Bitmap GetBitmap(int handle) => Get(handle);

    /// <summary>Internal: store an externally-created bitmap and return a handle.</summary>
    internal static int StoreBitmap(Bitmap bmp) => Store(bmp);

    /// <summary>Replace the bitmap for an existing handle, disposing the old one.</summary>
    private static void Swap(int handle, Bitmap bmp)
    {
        lock (_lock)
        {
            if (_images.TryGetValue(handle, out var old) && old != bmp)
                old.Dispose();
            _images[handle] = bmp;
        }
    }

    /// <summary>
    /// Pixel-level deep copy. Result is fully independent — no shared
    /// GDI+ resources, no file lock on the source.
    /// </summary>
    private static Bitmap DeepClone(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height, src.PixelFormat);
        dst.SetResolution(src.HorizontalResolution, src.VerticalResolution);
        if ((src.PixelFormat & PixelFormat.Indexed) != 0)
            dst.Palette = src.Palette;

        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var sd = src.LockBits(rect, ImageLockMode.ReadOnly, src.PixelFormat);
        var dd = dst.LockBits(rect, ImageLockMode.WriteOnly, dst.PixelFormat);
        int bytes = Math.Abs(sd.Stride) * src.Height;
        var buf = new byte[bytes];
        Marshal.Copy(sd.Scan0, buf, 0, bytes);
        Marshal.Copy(buf, 0, dd.Scan0, bytes);
        src.UnlockBits(sd);
        dst.UnlockBits(dd);
        return dst;
    }

    // ── Image lifecycle ────────────────────────────────────────────────

    public static int ImgOpen(string FileName, int PageNumber)
    {
        // Load then deep-copy so the file lock is released immediately.
        using var temp = new Bitmap(FileName);
        return Store(DeepClone(temp));
    }

    public static int ImgCreate(int Width, int Height, int BitPerPixel, int Resolution)
    {
        var fmt = BitPerPixel switch
        {
            1 => PixelFormat.Format1bppIndexed,
            8 => PixelFormat.Format8bppIndexed,
            _ => PixelFormat.Format24bppRgb
        };
        var bmp = new Bitmap(Width, Height, fmt);
        bmp.SetResolution(Resolution, Resolution);

        if (BitPerPixel == 8)
        {
            var pal = bmp.Palette;
            for (int i = 0; i < 256; i++)
                pal.Entries[i] = Color.FromArgb(i, i, i);
            bmp.Palette = pal;
        }

        return Store(bmp);
    }

    public static void ImgDelete(int ImageHandle)
    {
        lock (_lock)
        {
            if (_images.Remove(ImageHandle, out var bmp))
                bmp.Dispose();
        }
    }

    public static int ImgDuplicate(int ImageHandle)
    {
        var src = Get(ImageHandle);
        return src == null ? 0 : Store(DeepClone(src));
    }

    /// <summary>Copy a region, or the entire image when all coords are 0.</summary>
    public static int ImgCopy(int ImageHandle, int Left, int Top, int Right, int Bottom)
    {
        var src = Get(ImageHandle);
        if (src == null) return 0;

        // (0,0,0,0) = full copy
        if (Left <= 0 && Top <= 0 && Right <= 0 && Bottom <= 0)
            return Store(DeepClone(src));

        Left   = Math.Clamp(Left,   0, src.Width - 1);
        Top    = Math.Clamp(Top,    0, src.Height - 1);
        Right  = Math.Clamp(Right,  0, src.Width - 1);
        Bottom = Math.Clamp(Bottom, 0, src.Height - 1);
        if (Right < Left || Bottom < Top)
            return Store(DeepClone(src));

        int w = Math.Min(Right - Left + 1, src.Width - Left);
        int h = Math.Min(Bottom - Top + 1, src.Height - Top);
        var rect = new Rectangle(Left, Top, w, h);
        using var region = src.Clone(rect, src.PixelFormat);
        return Store(DeepClone(region));
    }

    // ── Save ───────────────────────────────────────────────────────────

    private static readonly ImageCodecInfo _tiffCodec = FindCodec("image/tiff");
    private static readonly ImageCodecInfo _jpegCodec = FindCodec("image/jpeg");

    private static ImageCodecInfo FindCodec(string mime)
    {
        foreach (var c in ImageCodecInfo.GetImageEncoders())
            if (c.MimeType == mime) return c;
        return null;
    }

    public static void ImgSaveAsTif(int ImageHandle, string FileName, int Compression, int RowsPerStrips)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return;

        var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(Encoder.Compression,
            bmp.PixelFormat == PixelFormat.Format1bppIndexed
                ? (long)EncoderValue.CompressionCCITT4
                : (long)EncoderValue.CompressionLZW);
        bmp.Save(FileName, _tiffCodec, ep);
    }

    public static void ImgSaveAsJpg(int ImageHandle, string FileName, int QFactor)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return;

        // JPEG doesn't support indexed formats — promote to 24bpp.
        Bitmap toSave = bmp;
        bool disposeAfter = false;
        if ((bmp.PixelFormat & PixelFormat.Indexed) != 0)
        {
            toSave = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb);
            toSave.SetResolution(bmp.HorizontalResolution, bmp.VerticalResolution);
            using (var g = Graphics.FromImage(toSave))
                g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
            disposeAfter = true;
        }

        var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)QFactor);
        toSave.Save(FileName, _jpegCodec, ep);
        if (disposeAfter) toSave.Dispose();
    }

    // ── Dimensions ─────────────────────────────────────────────────────

    public static int ImgGetWidth(int ImageHandle) => Get(ImageHandle)?.Width ?? 0;

    public static int ImgGetHeight(int ImageHandle) => Get(ImageHandle)?.Height ?? 0;

    public static int ImgGetBitsPixel(int ImageHandle)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return 0;
        return bmp.PixelFormat switch
        {
            PixelFormat.Format1bppIndexed => 1,
            PixelFormat.Format4bppIndexed => 4,
            PixelFormat.Format8bppIndexed => 8,
            PixelFormat.Format24bppRgb    => 24,
            PixelFormat.Format32bppArgb   => 32,
            _                             => 24
        };
    }

    // ── Crop / Composite ───────────────────────────────────────────────

    /// <summary>Crop in-place to the specified region.</summary>
    public static void ImgCropBorder(int ImageHandle, int Left, int Top, int Right, int Bottom)
    {
        var src = Get(ImageHandle);
        if (src == null) return;

        Left   = Math.Clamp(Left,   0, src.Width - 1);
        Top    = Math.Clamp(Top,    0, src.Height - 1);
        Right  = Math.Clamp(Right,  0, src.Width - 1);
        Bottom = Math.Clamp(Bottom, 0, src.Height - 1);
        if (Right < Left || Bottom < Top) return;

        int w = Math.Min(Right - Left + 1, src.Width - Left);
        int h = Math.Min(Bottom - Top + 1, src.Height - Top);
        var rect = new Rectangle(Left, Top, w, h);
        using var region = src.Clone(rect, src.PixelFormat);
        var cropped = DeepClone(region);
        cropped.SetResolution(src.HorizontalResolution, src.VerticalResolution);
        Swap(ImageHandle, cropped);
    }

    /// <summary>Paste source onto dest at (X, Y).</summary>
    public static void ImgAddCopy(int DestImageHandle, int SourceImageHandle, int X, int Y)
    {
        var dest = Get(DestImageHandle);
        var src  = Get(SourceImageHandle);
        if (dest == null || src == null) return;

        if (dest.PixelFormat == PixelFormat.Format1bppIndexed)
        {
            AddCopyIndexed1bpp(dest, src, X, Y);
            return;
        }

        // 24/32bpp — use Graphics
        using var g = Graphics.FromImage(dest);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(src, X, Y, src.Width, src.Height);
    }

    private static void AddCopyIndexed1bpp(Bitmap dest, Bitmap src, int offX, int offY)
    {
        Bitmap s = src.PixelFormat == PixelFormat.Format1bppIndexed ? src : ThresholdTo1bppFast(src);
        bool disposeS = s != src;

        var sRect = new Rectangle(0, 0, s.Width, s.Height);
        var dRect = new Rectangle(0, 0, dest.Width, dest.Height);
        var sd = s.LockBits(sRect, ImageLockMode.ReadOnly, PixelFormat.Format1bppIndexed);
        var dd = dest.LockBits(dRect, ImageLockMode.ReadWrite, PixelFormat.Format1bppIndexed);

        int sStride = Math.Abs(sd.Stride);
        int dStride = Math.Abs(dd.Stride);
        var sBuf = new byte[sStride * s.Height];
        var dBuf = new byte[dStride * dest.Height];
        Marshal.Copy(sd.Scan0, sBuf, 0, sBuf.Length);
        Marshal.Copy(dd.Scan0, dBuf, 0, dBuf.Length);

        int yStart = Math.Max(0, -offY);
        int xStart = Math.Max(0, -offX);
        int yEnd = Math.Min(s.Height, dest.Height - offY);
        int xEnd = Math.Min(s.Width, dest.Width - offX);

        // Bit offset of source xStart in its byte
        int srcBitOff = xStart & 7;
        // Bit offset of dest (xStart + offX) in its byte
        int dstBitOff = (xStart + offX) & 7;

        if (srcBitOff == 0 && dstBitOff == 0)
        {
            // Both byte-aligned — fast path: bulk copy whole bytes
            int srcByteStart = xStart >> 3;
            int dstByteStart = (xStart + offX) >> 3;
            int pixelSpan = xEnd - xStart;
            int fullBytes = pixelSpan >> 3;
            int remBits = pixelSpan & 7;

            for (int sy = yStart; sy < yEnd; sy++)
            {
                int sRow = sy * sStride + srcByteStart;
                int dRow = (sy + offY) * dStride + dstByteStart;
                Array.Copy(sBuf, sRow, dBuf, dRow, fullBytes);
                if (remBits > 0)
                {
                    // Merge partial last byte
                    byte mask = (byte)(0xFF << (8 - remBits));
                    dBuf[dRow + fullBytes] = (byte)((sBuf[sRow + fullBytes] & mask) | (dBuf[dRow + fullBytes] & ~mask));
                }
            }
        }
        else
        {
            // Unaligned — bit-by-bit (but still faster than before with branch reduction)
            for (int sy = yStart; sy < yEnd; sy++)
            {
                int dy = sy + offY;
                int sRow = sy * sStride;
                int dRow = dy * dStride;

                for (int sx = xStart; sx < xEnd; sx++)
                {
                    int dx = sx + offX;
                    bool bit = (sBuf[sRow + (sx >> 3)] & (0x80 >> (sx & 7))) != 0;
                    int dByteIdx = dRow + (dx >> 3);
                    int dMask = 0x80 >> (dx & 7);
                    if (bit) dBuf[dByteIdx] |= (byte)dMask;
                    else     dBuf[dByteIdx] &= (byte)~dMask;
                }
            }
        }

        Marshal.Copy(dBuf, 0, dd.Scan0, dBuf.Length);
        s.UnlockBits(sd);
        dest.UnlockBits(dd);
        if (disposeS) s.Dispose();
    }

    // ThresholdTo1bpp removed — use ThresholdTo1bppFast instead (LockBits, no GetPixel)

    // ── Image operations ───────────────────────────────────────────────

    /// <summary>Invert all pixels in-place (XOR every byte).</summary>
    public static void ImgInvert(int ImageHandle)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return;

        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);
        int bytes = Math.Abs(bd.Stride) * bmp.Height;
        var buf = new byte[bytes];
        Marshal.Copy(bd.Scan0, buf, 0, bytes);
        for (int i = 0; i < bytes; i++)
            buf[i] = (byte)~buf[i];
        Marshal.Copy(buf, 0, bd.Scan0, bytes);
        bmp.UnlockBits(bd);
    }

    /// <summary>Rotate by 90° increments. Modifies bitmap in-place.</summary>
    public static void ImgRotate(int ImageHandle, int Angle)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return;
        var flip = Angle switch
        {
             90 => RotateFlipType.Rotate90FlipNone,
            180 => RotateFlipType.Rotate180FlipNone,
            270 => RotateFlipType.Rotate270FlipNone,
            -90 => RotateFlipType.Rotate270FlipNone,
            _   => RotateFlipType.RotateNoneFlipNone
        };
        bmp.RotateFlip(flip);
    }

    /// <summary>Resize canvas. Existing pixels stay at top-left, new area filled with background.</summary>
    public static void ImgResize(int ImageHandle, int NewWidth, int NewHeight, int NewBackground)
    {
        var src = Get(ImageHandle);
        if (src == null) return;
        if (src.Width == NewWidth && src.Height == NewHeight) return;

        var dst = new Bitmap(NewWidth, NewHeight, src.PixelFormat);
        dst.SetResolution(src.HorizontalResolution, src.VerticalResolution);
        if ((src.PixelFormat & PixelFormat.Indexed) != 0)
            dst.Palette = src.Palette;

        if ((src.PixelFormat & PixelFormat.Indexed) != 0)
        {
            // Indexed: row-by-row copy via LockBits
            var sRect = new Rectangle(0, 0, src.Width, src.Height);
            var dRect = new Rectangle(0, 0, NewWidth, NewHeight);
            var sd = src.LockBits(sRect, ImageLockMode.ReadOnly, src.PixelFormat);
            var dd = dst.LockBits(dRect, ImageLockMode.WriteOnly, dst.PixelFormat);
            int sStride = Math.Abs(sd.Stride);
            int dStride = Math.Abs(dd.Stride);
            var sBuf = new byte[sStride * src.Height];
            var dBuf = new byte[dStride * NewHeight];
            Marshal.Copy(sd.Scan0, sBuf, 0, sBuf.Length);

            // Fill background (0 = black which is default for new byte[])
            if (NewBackground != 0)
            {
                byte fill = src.PixelFormat == PixelFormat.Format1bppIndexed
                    ? (byte)0xFF   // all-white for 1bpp
                    : (byte)NewBackground;
                Array.Fill(dBuf, fill);
            }

            int copyRows  = Math.Min(src.Height, NewHeight);
            int copyBytes = Math.Min(sStride, dStride);
            for (int y = 0; y < copyRows; y++)
                Array.Copy(sBuf, y * sStride, dBuf, y * dStride, copyBytes);

            Marshal.Copy(dBuf, 0, dd.Scan0, dBuf.Length);
            src.UnlockBits(sd);
            dst.UnlockBits(dd);
        }
        else
        {
            using var g = Graphics.FromImage(dst);
            g.Clear(Color.FromArgb(NewBackground, NewBackground, NewBackground));
            g.DrawImage(src, 0, 0, src.Width, src.Height);
        }

        Swap(ImageHandle, dst);
    }

    /// <summary>Draw or fill a rectangle.</summary>
    public static void ImgDrawRectangle(int ImageHandle, int X, int Y, int W, int H, int ColorVal, bool Filled)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return;

        if ((bmp.PixelFormat & PixelFormat.Indexed) != 0)
        {
            if (!Filled) return; // Outline on indexed formats not needed

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var bd = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);
            int stride = Math.Abs(bd.Stride);
            var buf = new byte[stride * bmp.Height];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

            int x2 = Math.Min(X + W, bmp.Width);
            int y2 = Math.Min(Y + H, bmp.Height);
            int x1 = Math.Max(0, X);
            int y1 = Math.Max(0, Y);

            if (bmp.PixelFormat == PixelFormat.Format1bppIndexed)
            {
                bool white = ColorVal != 0;
                // Fast path: full-width fill
                if (x1 == 0 && x2 >= bmp.Width)
                {
                    byte fill = white ? (byte)0xFF : (byte)0x00;
                    for (int row = y1; row < y2; row++)
                        Array.Fill(buf, fill, row * stride, stride);
                }
                else
                {
                    for (int row = y1; row < y2; row++)
                        for (int col = x1; col < x2; col++)
                        {
                            int idx = row * stride + (col >> 3);
                            int mask = 0x80 >> (col & 7);
                            if (white) buf[idx] |= (byte)mask;
                            else       buf[idx] &= (byte)~mask;
                        }
                }
            }
            else // 8bpp
            {
                byte val = (byte)Math.Clamp(ColorVal, 0, 255);
                for (int row = y1; row < y2; row++)
                    Array.Fill(buf, val, row * stride + x1, x2 - x1);
            }

            Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
            bmp.UnlockBits(bd);
        }
        else
        {
            using var g = Graphics.FromImage(bmp);
            var color = Color.FromArgb(ColorVal, ColorVal, ColorVal);
            if (Filled)
            {
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, X, Y, W, H);
            }
            else
            {
                using var pen = new Pen(color);
                g.DrawRectangle(pen, X, Y, W, H);
            }
        }
    }

    /// <summary>Convert 24bpp color to 8bpp grayscale in-place.</summary>
    public static void ImgConvertToGrayScale(int ImageHandle, int Mode, bool R, bool G, bool B)
    {
        var src = Get(ImageHandle);
        if (src == null) return;
        if (src.PixelFormat != PixelFormat.Format24bppRgb &&
            src.PixelFormat != PixelFormat.Format32bppArgb) return;

        int w = src.Width, h = src.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format8bppIndexed);
        dst.SetResolution(src.HorizontalResolution, src.VerticalResolution);
        var pal = dst.Palette;
        for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
        dst.Palette = pal;

        var sRect = new Rectangle(0, 0, w, h);
        var sd = src.LockBits(sRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dd = dst.LockBits(sRect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
        int sStride = Math.Abs(sd.Stride);
        int dStride = Math.Abs(dd.Stride);
        var sBuf = new byte[sStride * h];
        var dBuf = new byte[dStride * h];
        Marshal.Copy(sd.Scan0, sBuf, 0, sBuf.Length);

        int channels = (R ? 1 : 0) + (G ? 1 : 0) + (B ? 1 : 0);
        if (channels == 0) { R = G = B = true; channels = 3; }

        for (int y = 0; y < h; y++)
        {
            int sOff = y * sStride;
            int dOff = y * dStride;
            for (int x = 0; x < w; x++)
            {
                // GDI+ 24bpp is BGR
                int sum = 0;
                if (B) sum += sBuf[sOff + x * 3];
                if (G) sum += sBuf[sOff + x * 3 + 1];
                if (R) sum += sBuf[sOff + x * 3 + 2];
                dBuf[dOff + x] = (byte)((sum * 2 + channels) / (channels * 2));
            }
        }

        Marshal.Copy(dBuf, 0, dd.Scan0, dBuf.Length);
        src.UnlockBits(sd);
        dst.UnlockBits(dd);
        Swap(ImageHandle, dst);
    }

    // ── Phase 2: ImgAutoThreshold ─────────────────────────────────────

    /// <summary>
    /// Global threshold: converts image to 1bpp using the selected algorithm.
    /// ThresholdAlgo: 0=Otsu, 1=Kittler-Illingworth, 2=Ridler-Calvard isodata.
    /// Returns the threshold value used.
    /// </summary>
    public static int ImgAutoThreshold(int ImageHandle, int ThresholdAlgo)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return 128;
        int w = bmp.Width, h = bmp.Height;

        if (bmp.PixelFormat == PixelFormat.Format1bppIndexed)
            return 128; // already 1bpp, nothing to do

        // Extract grayscale with stride for native call
        byte[] gray;
        int grayStride;
        if (bmp.PixelFormat == PixelFormat.Format8bppIndexed)
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
            grayStride = Math.Abs(bd.Stride);
            gray = new byte[grayStride * h];
            Marshal.Copy(bd.Scan0, gray, 0, gray.Length);
            bmp.UnlockBits(bd);
        }
        else // 24bpp or 32bpp — convert to grayscale (packed, stride=w)
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            int stride24 = Math.Abs(bd.Stride);
            var buf = new byte[stride24 * h];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(bd);
            gray = new byte[w * h];
            grayStride = w;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int off = y * stride24 + x * 3;
                    gray[y * w + x] = (byte)(_lutB[buf[off]] + _lutG[buf[off + 1]] + _lutR[buf[off + 2]]);
                }
        }

        // Native threshold computation
        int T = NativeAutoThreshold(gray, w, h, grayStride, ThresholdAlgo);

        // Build flat grayscale array for packing (handle stride != w case)
        byte[] grayFlat;
        if (grayStride == w)
            grayFlat = gray;
        else
        {
            grayFlat = new byte[w * h];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(gray, y * grayStride, grayFlat, y * w, w);
        }

        // Apply threshold and convert to 1bpp
        var dst = new Bitmap(w, h, PixelFormat.Format1bppIndexed);
        dst.SetResolution(bmp.HorizontalResolution, bmp.VerticalResolution);
        var dd = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format1bppIndexed);
        int dStride = Math.Abs(dd.Stride);
        var dBuf = new byte[dStride * h];
        int atFullBytes = w >> 3;
        int atRemainder = w & 7;
        for (int y = 0; y < h; y++)
        {
            int srcRow = y * w;
            int dstRow = y * dStride;
            int sx = 0;
            for (int bx = 0; bx < atFullBytes; bx++)
            {
                byte packed = 0;
                if (grayFlat[srcRow + sx]     > T) packed |= 0x80;
                if (grayFlat[srcRow + sx + 1] > T) packed |= 0x40;
                if (grayFlat[srcRow + sx + 2] > T) packed |= 0x20;
                if (grayFlat[srcRow + sx + 3] > T) packed |= 0x10;
                if (grayFlat[srcRow + sx + 4] > T) packed |= 0x08;
                if (grayFlat[srcRow + sx + 5] > T) packed |= 0x04;
                if (grayFlat[srcRow + sx + 6] > T) packed |= 0x02;
                if (grayFlat[srcRow + sx + 7] > T) packed |= 0x01;
                dBuf[dstRow + bx] = packed;
                sx += 8;
            }
            if (atRemainder > 0)
            {
                byte packed = 0;
                for (int bit = 0; bit < atRemainder; bit++)
                    if (grayFlat[srcRow + sx + bit] > T)
                        packed |= (byte)(0x80 >> bit);
                dBuf[dstRow + atFullBytes] = packed;
            }
        }
        Marshal.Copy(dBuf, 0, dd.Scan0, dBuf.Length);
        dst.UnlockBits(dd);

        Swap(ImageHandle, dst);
        return T;
    }

    // ── Phase 2: ImgFindBlackBorder ─────────────────────────────────────

    /// <summary>
    /// Find black border from the left edge. Scans columns left→right.
    /// Returns the column index of the first non-border column (0 if no border).
    /// MinBlackPercent: percentage of pixels in a column that must be black for it to be "border".
    /// MaxHoles: allowed non-border columns before stopping.
    /// </summary>
    public static int ImgFindBlackBorderLeft(int ImageHandle, double MinBlackPercent, int MaxHoles)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return 0;
        return FindBlackBorderNative(bmp, 0, MinBlackPercent, MaxHoles);
    }

    public static int ImgFindBlackBorderRight(int ImageHandle, double MinBlackPercent, int MaxHoles)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return 0;
        return FindBlackBorderNative(bmp, 1, MinBlackPercent, MaxHoles);
    }

    public static int ImgFindBlackBorderTop(int ImageHandle, double MinBlackPercent, int MaxHoles)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return 0;
        return FindBlackBorderNative(bmp, 2, MinBlackPercent, MaxHoles);
    }

    public static int ImgFindBlackBorderBottom(int ImageHandle, double MinBlackPercent, int MaxHoles)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return 0;
        return FindBlackBorderNative(bmp, 3, MinBlackPercent, MaxHoles);
    }

    private static int FindBlackBorderNative(Bitmap bmp, int side, double MinBlackPercent, int MaxHoles)
    {
        int w = bmp.Width, h = bmp.Height;
        int bpp;
        PixelFormat lockFmt;
        if (bmp.PixelFormat == PixelFormat.Format1bppIndexed)
            { bpp = 1; lockFmt = PixelFormat.Format1bppIndexed; }
        else if (bmp.PixelFormat == PixelFormat.Format8bppIndexed)
            { bpp = 8; lockFmt = PixelFormat.Format8bppIndexed; }
        else
            { bpp = 24; lockFmt = PixelFormat.Format24bppRgb; }

        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, lockFmt);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * h];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(bd);

        return NativeFindBlackBorder(buf, stride, w, h, bpp, side, MinBlackPercent, MaxHoles);
    }

    /// <summary>
    /// Run multiple FindBlackBorder calls on a single image with a single LockBits/Marshal.Copy.
    /// Each element of <paramref name="calls"/> is (side, minBlackPct, maxHoles).
    /// Returns results in the same order as the input calls array.
    /// For 1bpp images, uses native FindBlackBorderBatch (single projection-build pass over
    /// the image, then O(w+h) lookup per call). For typical photostat images with actual
    /// borders this is ~2.7x faster than 4 individual ImgFindBlackBorder* calls.
    /// </summary>
    public static int[] ImgFindBlackBorderBatch(int ImageHandle,
        (int side, double minBlackPct, int maxHoles)[] calls)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return new int[calls.Length];

        int w = bmp.Width, h = bmp.Height;
        int bpp;
        PixelFormat lockFmt;
        if (bmp.PixelFormat == PixelFormat.Format1bppIndexed)
            { bpp = 1; lockFmt = PixelFormat.Format1bppIndexed; }
        else if (bmp.PixelFormat == PixelFormat.Format8bppIndexed)
            { bpp = 8; lockFmt = PixelFormat.Format8bppIndexed; }
        else
            { bpp = 24; lockFmt = PixelFormat.Format24bppRgb; }

        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, lockFmt);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * h];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(bd);

        var results = new int[calls.Length];

        // For 1bpp, use the native batch function (single projection build = cache-efficient)
        if (bpp == 1)
        {
            var nativeCalls = new FBBCall[calls.Length];
            for (int i = 0; i < calls.Length; i++)
                nativeCalls[i] = new FBBCall
                    { side = calls[i].side, minBlackPct = calls[i].minBlackPct, maxHoles = calls[i].maxHoles };
            NativeFindBlackBorderBatch(buf, stride, w, h, bpp, calls.Length, nativeCalls, results);
        }
        else
        {
            for (int i = 0; i < calls.Length; i++)
            {
                var (side, pct, holes) = calls[i];
                results[i] = NativeFindBlackBorder(buf, stride, w, h, bpp, side, pct, holes);
            }
        }
        return results;
    }

    // ── Phase 2: Threshold (non-RDynamic fallback paths) ──────────────

    /// <summary>
    /// Local dynamic threshold. Handles -1/-2 sentinel values for auto-detection.
    /// Converts image to 1bpp in-place.
    /// Decompiled behavior:
    ///   Contrast=-1  → auto Otsu threshold on image histogram
    ///   Brightness=-1 → histogram mode (most frequent difference value)
    ///   Brightness=-2 → Otsu on difference histogram
    /// </summary>
    public static void ImgDynamicThresholdAverage(int ImageHandle, int WindowWidth, int WindowHeight, int Contrast, int Brightness)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return;
        int w = bmp.Width, h = bmp.Height;
        byte[] gray = ExtractGrayscaleLut(bmp);

        byte[] result = new byte[w * h];
        NativeDynamicThresholdAverage(gray, result, w, h, WindowWidth, WindowHeight, Contrast, Brightness);
        var dst = BinaryTo1bpp(result, w, h, bmp.HorizontalResolution, bmp.VerticalResolution);
        Swap(ImageHandle, dst);
    }

    /// <summary>
    /// Adaptive threshold — decompiled from recoip.dll fcn.00a79fd8.
    /// Algorithm: Sobel edge magnitude → box-averaged gate image → threshold gate → compare gray vs local mean.
    /// Fixed mode (c=40,b=230): 100% pixel-perfect match. Auto mode (c=-1,b=-1): 99.6% match
    /// (gap is GDI+ vs Delphi JPEG decoder difference, not algorithm error).
    /// </summary>
    public static void ImgAdaptiveThresholdAverage(int ImageHandle, int WindowWidth, int WindowHeight, int Contrast, int Brightness)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return;
        int w = bmp.Width, h = bmp.Height;
        byte[] gray = ExtractGrayscaleLut(bmp);

        byte[] result = new byte[w * h];
        NativeAdaptiveThresholdAverage(gray, result, w, h, WindowWidth, WindowHeight, Contrast, Brightness);
        var dst = BinaryTo1bpp(result, w, h, bmp.HorizontalResolution, bmp.VerticalResolution);
        Swap(ImageHandle, dst);
    }

    /// <summary>
    /// Refine threshold — flips low-contrast noise CCs to white.
    /// </summary>
    public static void ImgRefineThreshold(int ImageHandle, int OriginalGrayScaleImageHandle, int Tolerance)
    {
        var bmpBin = Get(ImageHandle);
        var bmpGray = Get(OriginalGrayScaleImageHandle);
        if (bmpBin == null || bmpGray == null) return;
        int w = bmpBin.Width, h = bmpBin.Height;

        // Extract binary as 0/255 array
        byte[] binary;
        if (bmpBin.PixelFormat == PixelFormat.Format1bppIndexed)
        {
            var bd = bmpBin.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format1bppIndexed);
            int stride = Math.Abs(bd.Stride);
            var buf = new byte[stride * h];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bmpBin.UnlockBits(bd);
            binary = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    binary[y * w + x] = (buf[y * stride + (x >> 3)] & (0x80 >> (x & 7))) != 0 ? (byte)255 : (byte)0;
        }
        else
            binary = ExtractGrayscaleLut(bmpBin);

        // Extract grayscale from the original
        byte[] gray;
        if (bmpGray.PixelFormat == PixelFormat.Format8bppIndexed)
        {
            var bd = bmpGray.LockBits(new Rectangle(0, 0, bmpGray.Width, bmpGray.Height), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
            int stride = Math.Abs(bd.Stride);
            var buf = new byte[stride * bmpGray.Height];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bmpGray.UnlockBits(bd);
            gray = new byte[w * h];
            for (int y = 0; y < Math.Min(h, bmpGray.Height); y++)
                for (int x = 0; x < Math.Min(w, bmpGray.Width); x++)
                    gray[y * w + x] = buf[y * stride + x];
        }
        else
            gray = ExtractGrayscaleLut(bmpGray);

        if (Tolerance <= 0)
        {
            // No refinement needed
            var dst0 = BinaryTo1bpp(binary, w, h, bmpBin.HorizontalResolution, bmpBin.VerticalResolution);
            Swap(ImageHandle, dst0);
            return;
        }
        byte[] result = (byte[])binary.Clone();
        NativeRefineThreshold(result, gray, w, h, Tolerance);
        var dst = BinaryTo1bpp(result, w, h, bmpBin.HorizontalResolution, bmpBin.VerticalResolution);
        Swap(ImageHandle, dst);
    }


    /// <summary>Extract grayscale from any Bitmap using Recogniform LUT weights.</summary>
    private static byte[] ExtractGrayscaleLut(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        if (bmp.PixelFormat == PixelFormat.Format8bppIndexed)
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
            int stride = Math.Abs(bd.Stride);
            var buf = new byte[stride * h];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(bd);
            var gray = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    gray[y * w + x] = buf[y * stride + x];
            return gray;
        }
        else // 24bpp
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            int stride = Math.Abs(bd.Stride);
            var buf = new byte[stride * h];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(bd);
            var gray = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int off = y * stride + x * 3;
                    gray[y * w + x] = (byte)(_lutB[buf[off]] + _lutG[buf[off + 1]] + _lutR[buf[off + 2]]);
                }
            return gray;
        }
    }

    /// <summary>Convert 0/255 binary array to 1bpp Bitmap. Packs 8 pixels per byte.</summary>
    private static Bitmap BinaryTo1bpp(byte[] binary, int w, int h, float dpiX, float dpiY)
    {
        var dst = new Bitmap(w, h, PixelFormat.Format1bppIndexed);
        dst.SetResolution(dpiX, dpiY);
        // Default GDI+ 1bpp palette: index 0 = Black, index 1 = White
        // Native DLL outputs 255 for foreground (text), 0 for background
        // So: background (0) -> set bit=1 -> White, text (255) -> leave bit=0 -> Black
        var dd = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format1bppIndexed);
        int dStride = Math.Abs(dd.Stride);
        var dBuf = new byte[dStride * h];
        // Start with all bits = 0 (all black). Set bit=1 for background (white) pixels.
        int fullBytes = w >> 3;
        int remainder = w & 7;

        for (int y = 0; y < h; y++)
        {
            int srcRow = y * w;
            int dstRow = y * dStride;
            int sx = 0;
            for (int bx = 0; bx < fullBytes; bx++)
            {
                byte packed = 0;
                if (binary[srcRow + sx]     != 0) packed |= 0x80;
                if (binary[srcRow + sx + 1] != 0) packed |= 0x40;
                if (binary[srcRow + sx + 2] != 0) packed |= 0x20;
                if (binary[srcRow + sx + 3] != 0) packed |= 0x10;
                if (binary[srcRow + sx + 4] != 0) packed |= 0x08;
                if (binary[srcRow + sx + 5] != 0) packed |= 0x04;
                if (binary[srcRow + sx + 6] != 0) packed |= 0x02;
                if (binary[srcRow + sx + 7] != 0) packed |= 0x01;
                dBuf[dstRow + bx] = packed;
                sx += 8;
            }
            if (remainder > 0)
            {
                byte packed = 0;
                for (int bit = 0; bit < remainder; bit++)
                    if (binary[srcRow + sx + bit] != 0)
                        packed |= (byte)(0x80 >> bit);
                dBuf[dstRow + fullBytes] = packed;
            }
        }

        Marshal.Copy(dBuf, 0, dd.Scan0, dBuf.Length);
        dst.UnlockBits(dd);
        return dst;
    }

    // ── Phase 3: ImgDespeckle ────────────────────────────────────────────

    /// <summary>
    /// Remove speckles (small connected components) from a 1bpp image.
    /// 8-connected flood-fill; removes CCs fitting within MaxWidth × MaxHeight.
    /// Returns the number of removed components.
    /// </summary>
    public static int ImgDespeckle(int ImageHandle, int MaxWidth, int MaxHeight)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null || bmp.PixelFormat != PixelFormat.Format1bppIndexed) return 0;

        int w = bmp.Width, h = bmp.Height;
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format1bppIndexed);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * h];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

        int removed = NativeDespeckle(buf, stride, w, h, MaxWidth, MaxHeight);

        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
        return removed;
    }

    // ── Phase 3: ImgRemoveBlackWires ─────────────────────────────────────

    /// <summary>
    /// Remove thin horizontal black lines (scanner wires) from a 1bpp image.
    /// A pixel is a wire candidate if its vertical black run is ≤ 3px.
    /// Rows where ≥ 25% of pixels are wire candidates have those candidates removed.
    /// Two-pass: first count wire candidates per row (byte[h]), then remove.
    /// Avoids ushort[w*h] allocation.
    /// </summary>
    public static void ImgRemoveBlackWires(int ImageHandle)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null || bmp.PixelFormat != PixelFormat.Format1bppIndexed) return;

        int w = bmp.Width, h = bmp.Height;
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format1bppIndexed);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * h];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

        NativeRemoveBlackWires(buf, stride, w, h);

        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
    }

    // ── Phase 3: ImgRemoveBleedThrough ───────────────────────────────────

    /// <summary>
    /// Remove bleed-through (show-through from page reverse) from a color image.
    /// Uses HSL histogram-based background detection and per-pixel lightness replacement
    /// via recoip_native.dll with x87 extended-precision HSL conversion.
    /// </summary>
    public static void ImgRemoveBleedThrough(int ImageHandle, int Tolerance)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null || bmp.PixelFormat == PixelFormat.Format1bppIndexed) return;

        int w = bmp.Width, h = bmp.Height;
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * h];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

        NativeRemoveBleedThrough(buf, w, h, stride, Tolerance);

        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
    }

    // ── Phase 3: ImgRemoveVerticalLines ──────────────────────────────────

    /// <summary>
    /// Remove vertical lines from a 1bpp image.
    /// Scans each column for long vertical runs; removes pixels whose horizontal
    /// extent is thin (line, not text). Returns the number of columns cleaned.
    /// </summary>
    public static int ImgRemoveVerticalLines(int ImageHandle, int MinVLen, int MaxVBreaks, double MinVRatio, bool CleanBorders, bool ReconnectCharacters)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null || bmp.PixelFormat != PixelFormat.Format1bppIndexed) return 0;

        int w = bmp.Width, h = bmp.Height;
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format1bppIndexed);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * h];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

        int removed = NativeRemoveVerticalLines(buf, stride, w, h, MinVLen, MaxVBreaks, (int)MinVRatio);

        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
        return removed;
    }

    // ── Phase 4: ImgDeskew ───────────────────────────────────────────────

    /// <summary>
    /// Detect and correct skew in-place. Returns the detected angle in degrees.
    /// Uses projection profile variance with coarse-to-fine search.
    /// </summary>
    public static double ImgDeskew(int ImageHandle, int MaxAngle, double Accuracy, int AStep, int FillBlack, int Interpolation)
    {
        var bmp = Get(ImageHandle);
        if (bmp == null) return 0.0;

        double angle = DetectSkewAngle(bmp, MaxAngle, Accuracy);
        if (Math.Abs(angle) < Accuracy / 2.0) return 0.0;

        RotateInPlace(ImageHandle, -(float)angle, FillBlack == 0);
        return angle;
    }

    /// <summary>Apply small-angle rotation (fine deformation correction).</summary>
    public static void ImgCorrectDeformation1(int ImageHandle, double HAngle, double VAngle, bool WhiteBackground)
    {
        RotateInPlace(ImageHandle, (float)HAngle, WhiteBackground);
    }


    // ── Private: Rotation ───────────────────────────────────────────────

    private static void RotateInPlace(int handle, float angleDeg, bool whiteBackground)
    {
        var bmp = Get(handle);
        if (bmp == null || Math.Abs(angleDeg) < 0.001f) return;

        int w = bmp.Width, h = bmp.Height;
        float dpiX = bmp.HorizontalResolution, dpiY = bmp.VerticalResolution;
        bool is1bpp = bmp.PixelFormat == PixelFormat.Format1bppIndexed;

        var src24 = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        src24.SetResolution(dpiX, dpiY);
        using (var g = Graphics.FromImage(src24))
            g.DrawImage(bmp, 0, 0, w, h);

        var dst24 = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        dst24.SetResolution(dpiX, dpiY);
        using (var g = Graphics.FromImage(dst24))
        {
            g.Clear(whiteBackground ? Color.White : Color.Black);
            g.TranslateTransform(w / 2f, h / 2f);
            g.RotateTransform(angleDeg);
            g.TranslateTransform(-w / 2f, -h / 2f);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(src24, 0, 0);
        }
        src24.Dispose();

        if (is1bpp)
        {
            var result = ThresholdTo1bppFast(dst24);
            result.SetResolution(dpiX, dpiY);
            dst24.Dispose();
            Swap(handle, result);
        }
        else
            Swap(handle, dst24);
    }

    /// <summary>Fast 24bpp → 1bpp threshold (gray ≥ 128 = white) using LockBits.</summary>
    private static Bitmap ThresholdTo1bppFast(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format1bppIndexed);
        dst.SetResolution(src.HorizontalResolution, src.VerticalResolution);

        var sRect = new Rectangle(0, 0, w, h);
        var sd = src.LockBits(sRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dd = dst.LockBits(sRect, ImageLockMode.WriteOnly, PixelFormat.Format1bppIndexed);
        int sStride = Math.Abs(sd.Stride);
        int dStride = Math.Abs(dd.Stride);
        var sBuf = new byte[sStride * h];
        var dBuf = new byte[dStride * h];
        Marshal.Copy(sd.Scan0, sBuf, 0, sBuf.Length);

        for (int y = 0; y < h; y++)
        {
            int sOff = y * sStride;
            int dOff = y * dStride;
            for (int x = 0; x < w; x++)
            {
                int idx = sOff + x * 3;
                if ((sBuf[idx] + sBuf[idx + 1] + sBuf[idx + 2]) / 3 >= 128)
                    dBuf[dOff + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }

        Marshal.Copy(dBuf, 0, dd.Scan0, dBuf.Length);
        src.UnlockBits(sd);
        dst.UnlockBits(dd);
        return dst;
    }

    // ── Private: Skew detection ─────────────────────────────────────────

    private static double DetectSkewAngle(Bitmap bmp, int maxAngle, double accuracy)
    {
        int w = bmp.Width, h = bmp.Height;
        int step = Math.Max(1, Math.Min(w, h) / 500);
        var blackPixels = new List<(int x, int y)>();

        if (bmp.PixelFormat == PixelFormat.Format1bppIndexed)
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format1bppIndexed);
            int stride = Math.Abs(bd.Stride);
            var buf = new byte[stride * h];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(bd);
            for (int y = 0; y < h; y += step)
                for (int x = 0; x < w; x += step)
                    if ((buf[y * stride + (x >> 3)] & (0x80 >> (x & 7))) == 0)
                        blackPixels.Add((x, y));
        }
        else
        {
            byte[] gray = ExtractGrayscaleLut(bmp);
            for (int y = 0; y < h; y += step)
                for (int x = 0; x < w; x += step)
                    if (gray[y * w + x] < 128)
                        blackPixels.Add((x, y));
        }

        if (blackPixels.Count < 50) return 0;

        double bestAngle = 0, bestScore = 0;

        // Coarse
        for (double deg = -maxAngle; deg <= maxAngle; deg += 1.0)
        {
            double score = ProjVariance(blackPixels, deg, h);
            if (score > bestScore) { bestScore = score; bestAngle = deg; }
        }
        // Medium
        double mid = bestAngle;
        for (double deg = mid - 1.0; deg <= mid + 1.0; deg += 0.1)
        {
            double score = ProjVariance(blackPixels, deg, h);
            if (score > bestScore) { bestScore = score; bestAngle = deg; }
        }
        // Fine
        double fine = bestAngle;
        double fStep = Math.Max(accuracy / 2.0, 0.01);
        for (double deg = fine - 0.15; deg <= fine + 0.15; deg += fStep)
        {
            double score = ProjVariance(blackPixels, deg, h);
            if (score > bestScore) { bestScore = score; bestAngle = deg; }
        }

        return bestAngle;
    }

    private static double ProjVariance(List<(int x, int y)> pixels, double angleDeg, int maxBucket)
    {
        double rad = angleDeg * Math.PI / 180.0;
        double sinA = Math.Sin(rad), cosA = Math.Cos(rad);
        int offset = maxBucket / 2;
        int histLen = maxBucket * 2;
        var hist = new int[histLen];

        foreach (var (x, y) in pixels)
        {
            int bucket = (int)(y * cosA - x * sinA) + offset;
            if ((uint)bucket < (uint)histLen) hist[bucket]++;
        }

        long sum = 0, sumSq = 0;
        int count = 0;
        for (int i = 0; i < histLen; i++)
        {
            if (hist[i] > 0) { sum += hist[i]; sumSq += (long)hist[i] * hist[i]; count++; }
        }
        if (count == 0) return 0;
        double mean = (double)sum / count;
        return (double)sumSq / count - mean * mean;
    }

    // ── Byte-array overloads (handle-free) ──────────────────────────────────

    /// <summary>
    /// Direct byte-array dynamic threshold. Same native DLL as ImgDynamicThresholdAverage
    /// but without the handle/Bitmap round-trip.
    /// </summary>
    public static byte[] DynamicThresholdApply(byte[] gray, int width, int height,
        int w = 7, int h = 7, int contrast = 248, int brightness = 220)
    {
        byte[] result = new byte[width * height];
        NativeDynamicThresholdAverage(gray, result, width, height, w, h, contrast, brightness);
        return result;
    }

    /// <summary>
    /// Direct byte-array refine threshold. Same native DLL as ImgRefineThreshold
    /// but without the handle/Bitmap round-trip.
    /// </summary>
    public static byte[] RefineThresholdApply(byte[] binary, byte[] grayscale, int width, int height,
        int tolerance = 10)
    {
        if (tolerance <= 0)
            return (byte[])binary.Clone();

        byte[] result = (byte[])binary.Clone();
        NativeRefineThreshold(result, grayscale, width, height, tolerance);
        return result;
    }

    /// <summary>
    /// Direct byte-array despeckle. Same native DLL as ImgDespeckle
    /// but without the handle/Bitmap round-trip.
    /// </summary>
    public static int DespeckleApply(byte[] buf, int stride, int width, int height,
        int maxWidth, int maxHeight)
    {
        return NativeDespeckle(buf, stride, width, height, maxWidth, maxHeight);
    }

    /// <summary>
    /// Despeckle a byte-per-pixel binary image (0=black, ≥128=white).
    /// Packs to 1bpp, calls native despeckle, unpacks back in-place.
    /// </summary>
    public static int DespeckleBytes(byte[] pixels, int width, int height,
        int maxWidth, int maxHeight)
    {
        byte[] packed = PackTo1bpp(pixels, width, height);
        int byteWidth = (width + 7) / 8;

        int removed = NativeDespeckle(packed, byteWidth, width, height, maxWidth, maxHeight);

        UnpackFrom1bpp(packed, pixels, width, height);

        return removed;
    }

    /// <summary>
    /// Despeckle and return packed 1bpp data (avoids double-packing when saving).
    /// The byte-per-pixel array is also updated in-place for display cache.
    /// </summary>
    public static (byte[] packed, int byteWidth, int removed) DespeckleBytesPacked(
        byte[] pixels, int width, int height, int maxWidth, int maxHeight)
    {
        byte[] packed = PackTo1bpp(pixels, width, height);
        int byteWidth = (width + 7) / 8;

        int removed = NativeDespeckle(packed, byteWidth, width, height, maxWidth, maxHeight);

        UnpackFrom1bpp(packed, pixels, width, height);

        return (packed, byteWidth, removed);
    }

    // ── Byte-array helpers for photostat pipeline ─────────────────────────

    /// <summary>
    /// Load a JPEG and return LUT grayscale + raw BGR data in one pass.
    /// Uses TurboJPEG for fast decode (~1.6x faster than GDI+).
    /// </summary>
    public static (byte[] grayLut, byte[] bgr, int bgrStride, int width, int height)
        LoadImageAsGrayscaleAndBgr(string path)
    {
        byte[] jpegData = File.ReadAllBytes(path);
        IntPtr tj = tjInitDecompress();
        try
        {
            tjDecompressHeader3(tj, jpegData, (uint)jpegData.Length,
                out int w, out int h, out _, out _);

            // Decode to BGR (stride = w*3, no padding)
            int bgrStride = w * 3;
            byte[] bgr = new byte[bgrStride * h];
            tjDecompress2(tj, jpegData, (uint)jpegData.Length, bgr, w, 0, h, TJPF_BGR, 0);

            // LUT grayscale from BGR
            byte[] grayLut = new byte[w * h];
            Parallel.For(0, h, y =>
            {
                int srcRow = y * bgrStride;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    int off = srcRow + x * 3;
                    grayLut[dstRow + x] = (byte)(_lutR[bgr[off + 2]] + _lutG[bgr[off + 1]] + _lutB[bgr[off]]);
                }
            });
            return (grayLut, bgr, bgrStride, w, h);
        }
        finally { tjDestroy(tj); }
    }

    /// <summary>
    /// Extract grayscale from a BGR byte array sub-region using LUT weights.
    /// Optionally inverts BGR channels before conversion (for negative/photostat).
    /// </summary>
    public static byte[] ExtractGrayscaleFromBgr(byte[] bgr, int bgrStride,
        int left, int top, int right, int bottom, bool invert = false)
    {
        int w = right - left + 1;
        int h = bottom - top + 1;
        byte[] gray = new byte[w * h];
        Parallel.For(0, h, y =>
        {
            int srcRow = (top + y) * bgrStride + left * 3;
            int dstRow = y * w;
            for (int x = 0; x < w; x++)
            {
                int off = srcRow + x * 3;
                byte b = bgr[off], g = bgr[off + 1], r = bgr[off + 2];
                if (invert) { b = (byte)(255 - b); g = (byte)(255 - g); r = (byte)(255 - r); }
                gray[dstRow + x] = (byte)(_lutR[r] + _lutG[g] + _lutB[b]);
            }
        });
        return gray;
    }

    /// <summary>Extract grayscale sub-region from a full-image grayscale byte array.</summary>
    public static byte[] ExtractGrayscaleSubRegion(byte[] gray, int fullW,
        int left, int top, int right, int bottom)
    {
        int w = right - left + 1;
        int h = bottom - top + 1;
        byte[] sub = new byte[w * h];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(gray, (top + y) * fullW + left, sub, y * w, w);
        return sub;
    }

    /// <summary>
    /// Threshold a grayscale array and pack to 1bpp. Parallel.
    /// bit=1 (white) if gray > threshold, bit=0 (black) if gray &lt;= threshold.
    /// </summary>
    public static byte[] ThresholdAndPack1bpp(byte[] gray, int w, int h, int threshold)
    {
        int byteWidth = (w + 7) / 8;
        byte[] packed = new byte[byteWidth * h];
        Parallel.For(0, h, y =>
        {
            int src = y * w;
            int dst = y * byteWidth;
            int xFull = w & ~7;
            for (int x = 0; x < xFull; x += 8)
            {
                byte b = 0;
                if (gray[src + x]     > threshold) b |= 0x80;
                if (gray[src + x + 1] > threshold) b |= 0x40;
                if (gray[src + x + 2] > threshold) b |= 0x20;
                if (gray[src + x + 3] > threshold) b |= 0x10;
                if (gray[src + x + 4] > threshold) b |= 0x08;
                if (gray[src + x + 5] > threshold) b |= 0x04;
                if (gray[src + x + 6] > threshold) b |= 0x02;
                if (gray[src + x + 7] > threshold) b |= 0x01;
                packed[dst + (x >> 3)] = b;
            }
            if (xFull < w)
            {
                byte b = 0;
                for (int i = 0; i < w - xFull; i++)
                    if (gray[src + xFull + i] > threshold) b |= (byte)(0x80 >> i);
                packed[dst + (xFull >> 3)] = b;
            }
        });
        return packed;
    }

    /// <summary>Composite a byte-per-pixel sub-region into a larger buffer at (dstX, dstY).</summary>
    public static void CompositeBytes(byte[] src, int srcW, int srcH,
        byte[] dst, int dstW, int dstX, int dstY)
    {
        for (int y = 0; y < srcH; y++)
            Buffer.BlockCopy(src, y * srcW, dst, (dstY + y) * dstW + dstX, srcW);
    }

    /// <summary>
    /// Fast scanline connected component labeling + parallel erase.
    /// Single sequential pass with union-find (cache-friendly scanline order),
    /// then parallel erase of small components. No 1bpp packing needed.
    /// </summary>
    private static int DespeckleScanline(byte[] pixels, int width, int height,
        int maxWidth, int maxHeight)
    {
        int size = width * height;
        int[] labels = new int[size];
        // Union-find arrays: parent, rank, bbox (minX, minY, maxX, maxY)
        int capacity = 1024;
        int[] parent = new int[capacity];
        int[] rank = new int[capacity];
        int[] minX = new int[capacity];
        int[] minY = new int[capacity];
        int[] maxX = new int[capacity];
        int[] maxY = new int[capacity];
        int nextLabel = 1;

        // Find with path compression
        int Find(int a)
        {
            while (parent[a] != a)
            {
                parent[a] = parent[parent[a]]; // path halving
                a = parent[a];
            }
            return a;
        }

        // Union by rank, merge bounding boxes
        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a == b) return;
            if (rank[a] < rank[b]) { int t = a; a = b; b = t; }
            parent[b] = a;
            if (rank[a] == rank[b]) rank[a]++;
            // Merge bbox into a
            if (minX[b] < minX[a]) minX[a] = minX[b];
            if (minY[b] < minY[a]) minY[a] = minY[b];
            if (maxX[b] > maxX[a]) maxX[a] = maxX[b];
            if (maxY[b] > maxY[a]) maxY[a] = maxY[b];
        }

        void EnsureCapacity(int needed)
        {
            if (needed < capacity) return;
            int newCap = capacity * 2;
            while (newCap <= needed) newCap *= 2;
            Array.Resize(ref parent, newCap);
            Array.Resize(ref rank, newCap);
            Array.Resize(ref minX, newCap);
            Array.Resize(ref minY, newCap);
            Array.Resize(ref maxX, newCap);
            Array.Resize(ref maxY, newCap);
            capacity = newCap;
        }

        // Pass 1: scanline labeling with 8-connectivity
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            int prevRow = (y - 1) * width;
            for (int x = 0; x < width; x++)
            {
                if (pixels[row + x] >= 128) continue; // white pixel, skip

                int idx = row + x;
                // Check 4 neighbors: W, NW, N, NE (already labeled)
                int w  = (x > 0 && pixels[idx - 1] < 128)                               ? labels[idx - 1] : 0;
                int nw = (y > 0 && x > 0 && pixels[prevRow + x - 1] < 128)              ? labels[prevRow + x - 1] : 0;
                int n  = (y > 0 && pixels[prevRow + x] < 128)                            ? labels[prevRow + x] : 0;
                int ne = (y > 0 && x < width - 1 && pixels[prevRow + x + 1] < 128)      ? labels[prevRow + x + 1] : 0;

                int minLabel = int.MaxValue;
                if (w  > 0) minLabel = Math.Min(minLabel, Find(w));
                if (nw > 0) minLabel = Math.Min(minLabel, Find(nw));
                if (n  > 0) minLabel = Math.Min(minLabel, Find(n));
                if (ne > 0) minLabel = Math.Min(minLabel, Find(ne));

                if (minLabel == int.MaxValue)
                {
                    // New component
                    EnsureCapacity(nextLabel + 1);
                    int lbl = nextLabel++;
                    parent[lbl] = lbl;
                    rank[lbl] = 0;
                    minX[lbl] = x; minY[lbl] = y;
                    maxX[lbl] = x; maxY[lbl] = y;
                    labels[idx] = lbl;
                }
                else
                {
                    labels[idx] = minLabel;
                    // Update bbox
                    if (x < minX[minLabel]) minX[minLabel] = x;
                    if (y < minY[minLabel]) minY[minLabel] = y;
                    if (x > maxX[minLabel]) maxX[minLabel] = x;
                    if (y > maxY[minLabel]) maxY[minLabel] = y;
                    // Union all non-zero neighbors
                    if (w  > 0) Union(minLabel, w);
                    if (nw > 0) Union(minLabel, nw);
                    if (n  > 0) Union(minLabel, n);
                    if (ne > 0) Union(minLabel, ne);
                    // Re-find in case unions changed root
                    labels[idx] = Find(minLabel);
                }
            }
        }

        // Build set of roots to erase
        bool[] eraseRoot = new bool[nextLabel];
        int removed = 0;
        for (int i = 1; i < nextLabel; i++)
        {
            int root = Find(i);
            if (!eraseRoot[root] && (maxX[root] - minX[root] + 1) <= maxWidth
                                 && (maxY[root] - minY[root] + 1) <= maxHeight)
            {
                eraseRoot[root] = true;
                removed++;
            }
        }

        if (removed == 0) return 0;

        // Pass 2: parallel erase
        Parallel.For(0, height, y =>
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int lbl = labels[row + x];
                if (lbl > 0 && eraseRoot[Find(lbl)])
                    pixels[row + x] = 255; // set to white
            }
        });

        return removed;
    }

    /// <summary>Pack byte-per-pixel binary to 1bpp. Parallel, unrolled.</summary>
    public static byte[] PackTo1bpp(byte[] pixels, int width, int height)
    {
        int byteWidth = (width + 7) / 8;
        byte[] packed = new byte[byteWidth * height];

        Parallel.For(0, height, y =>
        {
            int src = y * width;
            int dst = y * byteWidth;
            int xFull = width & ~7;
            for (int x = 0; x < xFull; x += 8)
            {
                byte b = 0;
                if (pixels[src + x]     >= 128) b |= 0x80;
                if (pixels[src + x + 1] >= 128) b |= 0x40;
                if (pixels[src + x + 2] >= 128) b |= 0x20;
                if (pixels[src + x + 3] >= 128) b |= 0x10;
                if (pixels[src + x + 4] >= 128) b |= 0x08;
                if (pixels[src + x + 5] >= 128) b |= 0x04;
                if (pixels[src + x + 6] >= 128) b |= 0x02;
                if (pixels[src + x + 7] >= 128) b |= 0x01;
                packed[dst + (x >> 3)] = b;
            }
            if (xFull < width)
            {
                byte b = 0;
                for (int i = 0; i < width - xFull; i++)
                    if (pixels[src + xFull + i] >= 128) b |= (byte)(0x80 >> i);
                packed[dst + (xFull >> 3)] = b;
            }
        });

        return packed;
    }

    /// <summary>Unpack 1bpp to byte-per-pixel (0 or 255). Parallel.</summary>
    public static void UnpackFrom1bpp(byte[] packed, byte[] pixels, int width, int height)
    {
        int byteWidth = (width + 7) / 8;

        Parallel.For(0, height, y =>
        {
            int srcRow = y * byteWidth;
            int dstRow = y * width;
            int xFull = width & ~7;
            for (int x = 0; x < xFull; x += 8)
            {
                byte b = packed[srcRow + (x >> 3)];
                pixels[dstRow + x]     = (b & 0x80) != 0 ? (byte)255 : (byte)0;
                pixels[dstRow + x + 1] = (b & 0x40) != 0 ? (byte)255 : (byte)0;
                pixels[dstRow + x + 2] = (b & 0x20) != 0 ? (byte)255 : (byte)0;
                pixels[dstRow + x + 3] = (b & 0x10) != 0 ? (byte)255 : (byte)0;
                pixels[dstRow + x + 4] = (b & 0x08) != 0 ? (byte)255 : (byte)0;
                pixels[dstRow + x + 5] = (b & 0x04) != 0 ? (byte)255 : (byte)0;
                pixels[dstRow + x + 6] = (b & 0x02) != 0 ? (byte)255 : (byte)0;
                pixels[dstRow + x + 7] = (b & 0x01) != 0 ? (byte)255 : (byte)0;
            }
            if (xFull < width)
            {
                for (int i = 0; i < width - xFull; i++)
                    pixels[dstRow + xFull + i] = (packed[srcRow + (xFull >> 3)] & (0x80 >> i)) != 0
                        ? (byte)255 : (byte)0;
            }
        });
    }

    // ── Image I/O (byte-array, handle-free) ─────────────────────────────────

    // Reuse the LUT tables defined at the top of this class (_lutR, _lutG, _lutB).

    /// <summary>
    /// Load a JPEG/image file and return two grayscale byte arrays:
    /// grayLut (weighted R/G/B for DynamicThreshold) and grayAvg (simple average for RefineThreshold).
    /// Uses GDI+ for decode (no OpenCV dependency).
    /// </summary>
    public static (byte[] grayLut, byte[] grayAvg, int width, int height) LoadImageAsGrayscaleDual(string path)
    {
        // TIF/indexed images: fall back to GDI+ (TurboJPEG is JPEG-only)
        if (!path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            return LoadImageAsGrayscaleDualGdi(path);

        byte[] jpegData = File.ReadAllBytes(path);
        IntPtr tj = tjInitDecompress();
        try
        {
            tjDecompressHeader3(tj, jpegData, (uint)jpegData.Length,
                out int w, out int h, out _, out _);

            // Decode to RGB (stride = w*3, no padding)
            int rgbStride = w * 3;
            byte[] rgb = new byte[rgbStride * h];
            tjDecompress2(tj, jpegData, (uint)jpegData.Length, rgb, w, 0, h, TJPF_RGB, 0);

            // Dual grayscale conversion from RGB
            byte[] grayLut = new byte[w * h];
            byte[] grayAvg = new byte[w * h];
            Parallel.For(0, h, y =>
            {
                int srcRow = y * rgbStride;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    int off = srcRow + x * 3;
                    byte r = rgb[off], g = rgb[off + 1], b = rgb[off + 2];
                    grayLut[dstRow + x] = (byte)(_lutR[r] + _lutG[g] + _lutB[b]);
                    grayAvg[dstRow + x] = (byte)((r + g + b + 1) / 3);
                }
            });
            return (grayLut, grayAvg, w, h);
        }
        finally { tjDestroy(tj); }
    }

    /// <summary>
    /// GDI+-based grayscale loader. Recogniform-compatible (for comparison testing).
    /// Use LoadImageAsGrayscaleDual for production (uses TurboJPEG).
    /// </summary>
    [Obsolete("Recogniform-compatible. Use LoadImageAsGrayscaleDual for production.")]
    public static (byte[] grayLut, byte[] grayAvg, int width, int height) LoadImageAsGrayscaleDualGdi(string path)
    {
        using var temp = new Bitmap(path);
        int w = temp.Width, h = temp.Height;

        // Fallback for 1-bit/indexed images: clone to 24bpp, extract single channel
        if (temp.PixelFormat == PixelFormat.Format1bppIndexed ||
            temp.PixelFormat == PixelFormat.Format4bppIndexed ||
            temp.PixelFormat == PixelFormat.Format8bppIndexed)
        {
            using var clone8 = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g8 = Graphics.FromImage(clone8))
                g8.DrawImage(temp, 0, 0, w, h);
            var rect8 = new Rectangle(0, 0, w, h);
            var bd8 = clone8.LockBits(rect8, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte[] bgr8 = new byte[Math.Abs(bd8.Stride) * h];
            Marshal.Copy(bd8.Scan0, bgr8, 0, bgr8.Length);
            int stride8 = Math.Abs(bd8.Stride);
            clone8.UnlockBits(bd8);
            byte[] g = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int srcRow = y * stride8;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                    g[dstRow + x] = bgr8[srcRow + x * 3];
            }
            return (g, g, w, h);
        }

        using var clone = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var gr = Graphics.FromImage(clone))
            gr.DrawImage(temp, 0, 0, w, h);
        var rect = new Rectangle(0, 0, w, h);
        var bd = clone.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        byte[] bgr = new byte[Math.Abs(bd.Stride) * h];
        Marshal.Copy(bd.Scan0, bgr, 0, bgr.Length);
        int stride = Math.Abs(bd.Stride);
        clone.UnlockBits(bd);

        var (lut, avg) = ConvertBgrToGrayscaleDual(bgr, w, h, stride);
        return (lut, avg, w, h);
    }

    /// <summary>
    /// Load a file as single-channel grayscale byte array (for TIF reload in partial paths).
    /// Uses GDI+ for decode; applies LUT-weighted grayscale conversion.
    /// </summary>
    public static (byte[] gray, int width, int height) LoadImageAsGrayscale(string path)
    {
        using var temp = new Bitmap(path);
        int w = temp.Width, h = temp.Height;
        using var clone = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(clone))
            g.DrawImage(temp, 0, 0, w, h);
        var rect = new Rectangle(0, 0, w, h);
        var bd = clone.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        byte[] bgr = new byte[Math.Abs(bd.Stride) * h];
        Marshal.Copy(bd.Scan0, bgr, 0, bgr.Length);
        int stride = Math.Abs(bd.Stride);
        clone.UnlockBits(bd);

        byte[] gray = new byte[w * h];
        Parallel.For(0, h, y =>
        {
            int srcRow = y * stride;
            int dstRow = y * w;
            for (int x = 0; x < w; x++)
            {
                int off = srcRow + x * 3;
                byte b = bgr[off];
                byte gv = bgr[off + 1];
                byte r = bgr[off + 2];
                gray[dstRow + x] = (byte)(_lutR[r] + _lutG[gv] + _lutB[b]);
            }
        });
        return (gray, w, h);
    }

    /// <summary>
    /// Compute both grayscale conversions in a single parallel pass over BGR pixel data.
    /// </summary>
    private static (byte[] grayLut, byte[] grayAvg) ConvertBgrToGrayscaleDual(byte[] bgr, int w, int h, int stride)
    {
        byte[] grayLut = new byte[w * h];
        byte[] grayAvg = new byte[w * h];

        Parallel.For(0, h, y =>
        {
            int srcRow = y * stride;
            int dstRow = y * w;
            for (int x = 0; x < w; x++)
            {
                int off = srcRow + x * 3;
                byte b = bgr[off];
                byte g = bgr[off + 1];
                byte r = bgr[off + 2];
                grayLut[dstRow + x] = (byte)(_lutR[r] + _lutG[g] + _lutB[b]);
                grayAvg[dstRow + x] = (byte)((r + g + b + 1) / 3);
            }
        });

        return (grayLut, grayAvg);
    }

    // ── TIFF save (byte-array, handle-free) ─────────────────────────────────

    /// <summary>
    /// Save 8-bit grayscale pixel data as 1-bit CCITT Group 4 TIFF.
    /// Parallel 8-to-1-bit packing, atomic tmp-then-move write.
    /// </summary>
    public static bool SaveAsCcitt4Tif(byte[] pixels, int width, int height, string outputPath)
    {
        byte[] packed = PackTo1bpp(pixels, width, height);
        return SaveAsCcitt4TifPacked(packed, width, height, outputPath);
    }

    /// <summary>
    /// Save pre-packed 1bpp data as CCITT Group 4 TIFF. Skips packing step.
    /// </summary>
    public static bool SaveAsCcitt4TifPacked(byte[] packed, int width, int height, string outputPath)
    {
        int byteWidth = (width + 7) / 8;
        string tmpPath = Path.ChangeExtension(outputPath, ".tmp");
        try
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);

            using (var bmp = new Bitmap(width, height, PixelFormat.Format1bppIndexed))
            {
                var palette = bmp.Palette;
                palette.Entries[0] = Color.Black;
                palette.Entries[1] = Color.White;
                bmp.Palette = palette;

                var bmpData = bmp.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format1bppIndexed);

                for (int y = 0; y < height; y++)
                    Marshal.Copy(packed, y * byteWidth, bmpData.Scan0 + y * bmpData.Stride, byteWidth);

                bmp.UnlockBits(bmpData);

                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Compression,
                    (long)EncoderValue.CompressionCCITT4);

                bmp.Save(tmpPath, _tiffCodec, encoderParams);
            }

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
}


