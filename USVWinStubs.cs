// Native C# replacements for USVWin32.dll functions.
// EraseIn, EraseOut, GetImageInfo, RemoveDirtyLine, SKEWCORRECT: implemented using GDI+ / RavenImaging handles.
// COMBINETIFFS, VW_Combine*: stubs (not actively used).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace RAVEN;

/// <summary>
/// Native C# replacements for USVWin32.dll.
/// </summary>
public class USVWin
{
    // --- Structs needed by KeyPicture code that may still be referenced ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public unsafe struct TiffRedactInfo
    {
        public int iCount;
        public fixed uint iLeft[50];
        public fixed uint iTop[50];
        public fixed uint iRight[50];
        public fixed uint iBottom[50];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct TiffPrintCropInfo
    {
        public int iLeft;
        public int iTop;
        public int iRight;
        public int iBottom;
        public int unknown_1;
        public int unknown_2;
        public int unknown_3;
        public int unknown_4;
    }

    // Callback delegate (kept for compatibility)
    public delegate bool USVWinCallback(int Hwnd, uint iMessage, uint wParam, int lParam);

    // ── Implemented ────────────────────────────────────────────────────

    /// <summary>Read image dimensions from file header without loading pixel data.</summary>
    public static int GetImageInfo(string InputFile, int Page, ref int Width, ref int Height)
    {
        try
        {
            using var fs = new FileStream(InputFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var img = Image.FromStream(fs, false, false);
            Width = img.Width;
            Height = img.Height;
        }
        catch
        {
            Width = 0;
            Height = 0;
        }
        return 0;
    }

    /// <summary>Fill a rectangle with white (erase inside the box).</summary>
    public static int EraseIn(string FileNameIn, string FileNameOut, short Left, short Top, short Right, short Bottom)
    {
        int h = RavenImaging.ImgOpen(FileNameIn, 0);
        int color = RavenImaging.ImgGetBitsPixel(h) == 1 ? 1 : 255;
        RavenImaging.ImgDrawRectangle(h, Left, Top, Right - Left, Bottom - Top, color, true);
        RavenImaging.ImgSaveAsTif(h, FileNameOut, 5, 0);
        RavenImaging.ImgDelete(h);
        return 0;
    }

    /// <summary>Fill everything outside a rectangle with white (erase outside the box).</summary>
    public static int EraseOut(string FileNameIn, string FileNameOut, short Left, short Top, short Right, short Bottom)
    {
        int h = RavenImaging.ImgOpen(FileNameIn, 0);
        int w = RavenImaging.ImgGetWidth(h);
        int ht = RavenImaging.ImgGetHeight(h);
        int color = RavenImaging.ImgGetBitsPixel(h) == 1 ? 1 : 255;

        // Top strip
        if (Top > 0)
            RavenImaging.ImgDrawRectangle(h, 0, 0, w, Top, color, true);
        // Bottom strip
        if (Bottom < ht)
            RavenImaging.ImgDrawRectangle(h, 0, Bottom, w, ht - Bottom, color, true);
        // Left strip (between top and bottom)
        if (Left > 0)
            RavenImaging.ImgDrawRectangle(h, 0, Top, Left, Bottom - Top, color, true);
        // Right strip (between top and bottom)
        if (Right < w)
            RavenImaging.ImgDrawRectangle(h, Right, Top, w - Right, Bottom - Top, color, true);

        RavenImaging.ImgSaveAsTif(h, FileNameOut, 5, 0);
        RavenImaging.ImgDelete(h);
        return 0;
    }

    // ── Implemented — line removal ──────────────────────────────────────

    /// <summary>
    /// Remove a vertical scanner-artifact line at the given column.
    /// For each row, checks 3 pixels on each side of the band for black neighbors.
    /// If none found (pure line, no text), the band is erased to white.
    /// If neighbors exist (text crosses the line), the band is left untouched.
    /// Algorithm reverse-engineered from usvwin32.dll VW_RemoveDirtLine.
    /// </summary>
    public static int RemoveDirtyLine(string ImageFileName, int iPage, string OutputFileName, int MaxLineThickness, int Column)
    {
        int h = RavenImaging.ImgOpen(ImageFileName, 0);
        var bmp = RavenImaging.GetBitmap(h);
        if (bmp == null) { RavenImaging.ImgDelete(h); return 1; }

        int width = bmp.Width;
        int height = bmp.Height;
        if (Column < 0 || Column >= width) { RavenImaging.ImgDelete(h); return 1; }

        int leftBound = Math.Max(0, Column - MaxLineThickness);
        int rightBound = Math.Min(width - 1, Column + MaxLineThickness);

        if (bmp.PixelFormat == PixelFormat.Format1bppIndexed)
            RemoveDirtyLine1bpp(bmp, width, height, leftBound, rightBound);
        else
            RemoveDirtyLine24bpp(bmp, width, height, leftBound, rightBound);

        RavenImaging.ImgSaveAsTif(h, OutputFileName, 5, 0);
        RavenImaging.ImgDelete(h);
        return 0;
    }

    /// <summary>1bpp fast path using LockBits. Bit clear = black, bit set = white.</summary>
    private static void RemoveDirtyLine1bpp(Bitmap bmp, int width, int height, int left, int right)
    {
        var rect = new Rectangle(0, 0, width, height);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format1bppIndexed);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * height];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            int hits = 0;

            // Check 3 pixels left of band
            for (int x = left - 3; x < left; x++)
            {
                if (x < 0) continue;
                if ((buf[row + (x >> 3)] & (0x80 >> (x & 7))) == 0)
                    hits++;
            }

            // Check 3 pixels right of band
            for (int x = right + 1; x <= right + 3; x++)
            {
                if (x >= width) break;
                if ((buf[row + (x >> 3)] & (0x80 >> (x & 7))) == 0)
                    hits++;
            }

            // No adjacent black pixels → erase band (set bits = white)
            if (hits == 0)
                for (int x = left; x <= right; x++)
                    buf[row + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }

        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
    }

    /// <summary>24bpp fallback. Brightness &lt; 128 = black.</summary>
    private static void RemoveDirtyLine24bpp(Bitmap bmp, int width, int height, int left, int right)
    {
        var rect = new Rectangle(0, 0, width, height);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * height];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            int hits = 0;

            for (int x = left - 3; x < left; x++)
            {
                if (x < 0) continue;
                int off = row + x * 3;
                if ((buf[off] + buf[off + 1] + buf[off + 2]) / 3 < 128)
                    hits++;
            }

            for (int x = right + 1; x <= right + 3; x++)
            {
                if (x >= width) break;
                int off = row + x * 3;
                if ((buf[off] + buf[off + 1] + buf[off + 2]) / 3 < 128)
                    hits++;
            }

            if (hits == 0)
                for (int x = left; x <= right; x++)
                {
                    int off = row + x * 3;
                    buf[off] = buf[off + 1] = buf[off + 2] = 255;
                }
        }

        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
    }

    // ── Implemented — skew correction ──────────────────────────────────

    /// <summary>
    /// Detect and correct document skew using projection profile analysis.
    /// For each candidate angle, computes the variance of the horizontal projection
    /// (row-wise black pixel counts). The angle maximizing variance aligns text rows.
    /// Coarse-to-fine search: ±5° → ±0.5° → ±0.1° for sub-0.02° precision.
    /// Algorithm based on decompilation of usvwin32.dll SKEWCORRECT (projection profile
    /// with DPI-dependent sampling, followed by per-line rotation).
    /// </summary>
    public static int SKEWCORRECT(string InputFile, string OutputFile)
    {
        try
        {
            int h = RavenImaging.ImgOpen(InputFile, 0);
            var bmp = RavenImaging.GetBitmap(h);
            if (bmp == null) { RavenImaging.ImgDelete(h); return 1; }

            int w = bmp.Width, ht = bmp.Height;
            float dpiX = bmp.HorizontalResolution, dpiY = bmp.VerticalResolution;
            bool is1bpp = bmp.PixelFormat == PixelFormat.Format1bppIndexed;

            // Detect skew angle from 1bpp data
            double angle;
            if (is1bpp)
                angle = DetectSkewAngle1bpp(bmp, w, ht);
            else
                angle = DetectSkewAngle24bpp(bmp, w, ht);

            if (Math.Abs(angle) < 0.05)
            {
                // No meaningful skew — save unchanged (or skip if same file)
                if (!string.Equals(InputFile, OutputFile, StringComparison.OrdinalIgnoreCase))
                    RavenImaging.ImgSaveAsTif(h, OutputFile, 5, 0);
                RavenImaging.ImgDelete(h);
                return 0;
            }

            // Rotate via 24bpp intermediate (GDI+ can't draw onto indexed formats)
            var src24 = new Bitmap(w, ht, PixelFormat.Format24bppRgb);
            src24.SetResolution(dpiX, dpiY);
            using (var g = Graphics.FromImage(src24))
                g.DrawImage(bmp, 0, 0, w, ht);

            RavenImaging.ImgDelete(h); // release original

            var dst24 = new Bitmap(w, ht, PixelFormat.Format24bppRgb);
            dst24.SetResolution(dpiX, dpiY);
            using (var g = Graphics.FromImage(dst24))
            {
                g.Clear(Color.White);
                g.TranslateTransform(w / 2f, ht / 2f);
                g.RotateTransform(-(float)angle);
                g.TranslateTransform(-w / 2f, -ht / 2f);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(src24, 0, 0);
            }
            src24.Dispose();

            // Convert back to original format and save via RavenImaging
            Bitmap result;
            if (is1bpp)
            {
                result = SkewThresholdTo1bpp(dst24);
                result.SetResolution(dpiX, dpiY);
                dst24.Dispose();
            }
            else
            {
                result = dst24;
            }

            int hResult = RavenImaging.StoreBitmap(result);
            RavenImaging.ImgSaveAsTif(hResult, OutputFile, 5, 0);
            RavenImaging.ImgDelete(hResult);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>Detect skew angle from 1bpp bitmap using projection profile variance.</summary>
    private static double DetectSkewAngle1bpp(Bitmap bmp, int width, int height)
    {
        var rect = new Rectangle(0, 0, width, height);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format1bppIndexed);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * height];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(bd);

        // Collect sampled black pixel positions (bit clear = black in GDI+ 1bpp)
        int step = Math.Max(1, Math.Min(width, height) / 500); // ~500 samples per axis
        var blackPixels = new List<(int x, int y)>();
        for (int y = 0; y < height; y += step)
        {
            int rowOff = y * stride;
            for (int x = 0; x < width; x += step)
            {
                if ((buf[rowOff + (x >> 3)] & (0x80 >> (x & 7))) == 0)
                    blackPixels.Add((x, y));
            }
        }

        return FindBestAngle(blackPixels, height);
    }

    /// <summary>Detect skew angle from 24bpp bitmap using projection profile variance.</summary>
    private static double DetectSkewAngle24bpp(Bitmap bmp, int width, int height)
    {
        var rect = new Rectangle(0, 0, width, height);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        int stride = Math.Abs(bd.Stride);
        var buf = new byte[stride * height];
        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(bd);

        int step = Math.Max(1, Math.Min(width, height) / 500);
        var blackPixels = new List<(int x, int y)>();
        for (int y = 0; y < height; y += step)
        {
            int rowOff = y * stride;
            for (int x = 0; x < width; x += step)
            {
                int off = rowOff + x * 3;
                if ((buf[off] + buf[off + 1] + buf[off + 2]) / 3 < 128)
                    blackPixels.Add((x, y));
            }
        }

        return FindBestAngle(blackPixels, height);
    }

    /// <summary>Coarse-to-fine search for the angle that maximizes projection profile variance.</summary>
    private static double FindBestAngle(List<(int x, int y)> blackPixels, int height)
    {
        if (blackPixels.Count < 50) return 0;

        double bestAngle = 0, bestScore = 0;

        // Coarse: -5° to +5° in 0.5° steps
        for (double deg = -5.0; deg <= 5.0; deg += 0.5)
        {
            double score = ProjectionVariance(blackPixels, deg, height);
            if (score > bestScore) { bestScore = score; bestAngle = deg; }
        }

        // Medium: ±0.5° around best in 0.1° steps
        double mid = bestAngle;
        for (double deg = mid - 0.5; deg <= mid + 0.5; deg += 0.1)
        {
            double score = ProjectionVariance(blackPixels, deg, height);
            if (score > bestScore) { bestScore = score; bestAngle = deg; }
        }

        // Fine: ±0.1° around best in 0.02° steps
        double fine = bestAngle;
        for (double deg = fine - 0.1; deg <= fine + 0.1; deg += 0.02)
        {
            double score = ProjectionVariance(blackPixels, deg, height);
            if (score > bestScore) { bestScore = score; bestAngle = deg; }
        }

        return bestAngle;
    }

    /// <summary>
    /// Compute variance of the horizontal projection profile at a given angle.
    /// Higher variance = text rows are better aligned (sharper peaks/valleys).
    /// </summary>
    private static double ProjectionVariance(List<(int x, int y)> pixels, double angleDeg, int maxBucket)
    {
        double rad = angleDeg * Math.PI / 180.0;
        double sinA = Math.Sin(rad);
        double cosA = Math.Cos(rad);

        // Use a fixed-size histogram; offset so negative buckets map into range
        int offset = maxBucket / 2;
        int histLen = maxBucket * 2;
        var hist = new int[histLen];

        foreach (var (x, y) in pixels)
        {
            int bucket = (int)(y * cosA - x * sinA) + offset;
            if (bucket >= 0 && bucket < histLen)
                hist[bucket]++;
        }

        // Variance of non-zero bins
        long sum = 0, sumSq = 0;
        int count = 0;
        for (int i = 0; i < histLen; i++)
        {
            if (hist[i] > 0)
            {
                sum += hist[i];
                sumSq += (long)hist[i] * hist[i];
                count++;
            }
        }
        if (count == 0) return 0;
        double mean = (double)sum / count;
        return (double)sumSq / count - mean * mean;
    }

    /// <summary>Threshold 24bpp to 1bpp (gray &gt;= 128 = white).</summary>
    private static Bitmap SkewThresholdTo1bpp(Bitmap src)
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

    // ── Stubs — not actively used ──────────────────────────────────────

    public static int COMBINETIFFS(string OutputFile, string AppendFile) => 0;
    public static int VW_CombineMultiplePageTiffs(string File1, string File2, string Outputfile) => 0;
    public static int VW_CombineMultipleTiffPages(string OutputFile, string AppendFile, int Mode) => 0;
}
