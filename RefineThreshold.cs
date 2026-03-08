using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RAVEN;

/// <summary>
/// Reimplementation of Recogniform's ImgRefineThreshold.
///
/// Identifies connected components in a binary image, measures the average
/// gradient magnitude along each component's boundary in the original
/// grayscale image, and flips (removes) components whose average edge
/// contrast falls below a tolerance. This eliminates noise blobs that
/// lack strong edge support in the original grayscale.
///
/// Algorithm (from decompiled Ghidra output, verified against Recogniform DLL):
///   1. Apply 5x5 box filter to grayscale, then compute Sobel gradient magnitude
///      with L1 norm (|gx|+|gy|) clamped to 255.
///   2. For each BLACK connected component in the binary image:
///      a. Identify "edge pixels" — pixels whose 4-connected neighborhood
///         contains at least one pixel of the opposite color.
///      b. Sum the gradient magnitudes at those edge pixels.
///      c. If (edgeCount == 0) or (gradientSum / edgeCount) &lt; tolerance,
///         flip the entire component to the opposite color.
///   3. The result is a cleaned binary image with low-contrast blobs removed.
/// </summary>
public static class RefineThreshold
{
    /// <summary>
    /// Apply refinement threshold to a binary image using grayscale edge evidence.
    /// </summary>
    /// <param name="binary">Binary pixel data (0 or 255), row-major, will not be mutated.</param>
    /// <param name="grayscale">Grayscale pixel data (0-255), row-major.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="tolerance">Edge contrast threshold. Components with average boundary
    /// gradient below this value are flipped. Higher = more aggressive noise removal.
    /// Use 0 to disable. Typical values: 5-20.</param>
    /// <returns>New binary byte array (0 or 255 per pixel) with low-contrast components removed.</returns>
    public static byte[] Apply(byte[] binary, byte[] grayscale, int width, int height,
        int tolerance = 10)
    {
        if (binary == null) throw new ArgumentNullException(nameof(binary));
        if (grayscale == null) throw new ArgumentNullException(nameof(grayscale));
        int pixelCount = width * height;
        if (binary.Length != pixelCount)
            throw new ArgumentException($"binary length {binary.Length} != {width}x{height}");
        if (grayscale.Length != pixelCount)
            throw new ArgumentException($"grayscale length {grayscale.Length} != {width}x{height}");
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width and height must be positive.");

        // Tolerance <= 0 means no refinement
        if (tolerance <= 0)
            return (byte[])binary.Clone();

        // Step 1: Compute gradient magnitude image (Sobel-like edge detector)
        byte[] gradient = ComputeGradient(grayscale, width, height);

        // Step 2: Build output (copy of binary — we'll flip components in-place)
        byte[] result = (byte[])binary.Clone();

        // Step 3: Connected-component labeling + edge analysis + flipping
        // We use a sequential flood-fill with an explicit stack to avoid
        // stack overflow on large components and to collect per-component stats
        // in a single pass.
        //
        // The visited array tracks which pixels have been processed.
        // Only black (0) components are candidates for flipping — Recogniform's
        // RefineThreshold removes low-contrast black noise blobs but never
        // converts white regions to black.
        bool[] visited = new bool[pixelCount];

        // Reusable stack and component pixel list to reduce allocations
        var stack = new Stack<int>(Math.Min(pixelCount, 1024));
        var componentPixels = new List<int>(1024);

        for (int startIdx = 0; startIdx < pixelCount; startIdx++)
        {
            if (visited[startIdx])
                continue;

            byte componentColor = result[startIdx];

            // Flood-fill to find the connected component and compute edge stats
            stack.Clear();
            componentPixels.Clear();
            long edgeGradientSum = 0;
            int edgePixelCount = 0;

            stack.Push(startIdx);
            visited[startIdx] = true;

            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                componentPixels.Add(idx);

                int x = idx % width;
                int y = idx / width;

                // Check if this pixel is an "edge pixel" — on the boundary between
                // black and white regions in the binary image.
                bool isEdge = false;

                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    // Image border pixels are always edge pixels
                    isEdge = true;
                }
                else
                {
                    // Check 4-connected neighbors for different binary value
                    if (result[idx - 1] != componentColor ||      // left
                        result[idx + 1] != componentColor ||      // right
                        result[idx - width] != componentColor ||  // up
                        result[idx + width] != componentColor)    // down
                    {
                        isEdge = true;
                    }
                }

                if (isEdge)
                {
                    edgeGradientSum += gradient[idx];
                    edgePixelCount++;
                }

                // Enqueue 4-connected neighbors with the same binary color
                // Left
                if (x > 0)
                {
                    int ni = idx - 1;
                    if (!visited[ni] && result[ni] == componentColor)
                    {
                        visited[ni] = true;
                        stack.Push(ni);
                    }
                }
                // Right
                if (x < width - 1)
                {
                    int ni = idx + 1;
                    if (!visited[ni] && result[ni] == componentColor)
                    {
                        visited[ni] = true;
                        stack.Push(ni);
                    }
                }
                // Up
                if (y > 0)
                {
                    int ni = idx - width;
                    if (!visited[ni] && result[ni] == componentColor)
                    {
                        visited[ni] = true;
                        stack.Push(ni);
                    }
                }
                // Down
                if (y < height - 1)
                {
                    int ni = idx + width;
                    if (!visited[ni] && result[ni] == componentColor)
                    {
                        visited[ni] = true;
                        stack.Push(ni);
                    }
                }
            }

            // Only black components are candidates for removal.
            // White components are never flipped to black.
            if (componentColor != 0)
                continue;

            // Decision: flip the black component to white if edge contrast is below tolerance
            bool shouldFlip;
            if (edgePixelCount == 0)
            {
                // No edge pixels at all — isolated region, flip it
                shouldFlip = true;
            }
            else
            {
                int avgGradient = (int)(edgeGradientSum / edgePixelCount);
                shouldFlip = avgGradient < tolerance;
            }

            if (shouldFlip)
            {
                for (int i = 0; i < componentPixels.Count; i++)
                    result[componentPixels[i]] = 255;
            }
        }

        return result;
    }

    /// <summary>
    /// Compute gradient magnitude image.
    /// Matches Recogniform's algorithm: 5x5 box filter on grayscale, then Sobel 3x3
    /// with L1 magnitude (|gx|+|gy|) clamped to 255 (no bit-shift scaling).
    /// </summary>
    internal static byte[] ComputeGradient(byte[] gray, int width, int height)
    {
        // Step 1: 5x5 box filter (local mean) on grayscale — matches FUN_00a41c08 with param 3
        byte[] smoothed = BoxFilter5x5(gray, width, height);

        // Step 2: Sobel on the smoothed image, L1 magnitude clamped to 255
        byte[] gradient = new byte[width * height];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int gx, gy;

                if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                {
                    int left  = (x > 0)         ? smoothed[y * width + x - 1]   : smoothed[y * width + x];
                    int right = (x < width - 1)  ? smoothed[y * width + x + 1]   : smoothed[y * width + x];
                    int up    = (y > 0)          ? smoothed[(y - 1) * width + x] : smoothed[y * width + x];
                    int down  = (y < height - 1) ? smoothed[(y + 1) * width + x] : smoothed[y * width + x];
                    gx = right - left;
                    gy = down - up;
                }
                else
                {
                    int ra = (y - 1) * width;
                    int rc = y * width;
                    int rb = (y + 1) * width;

                    gx = -smoothed[ra + x - 1] + smoothed[ra + x + 1]
                       - 2 * smoothed[rc + x - 1] + 2 * smoothed[rc + x + 1]
                       - smoothed[rb + x - 1] + smoothed[rb + x + 1];

                    gy = -smoothed[ra + x - 1] - 2 * smoothed[ra + x] - smoothed[ra + x + 1]
                       + smoothed[rb + x - 1] + 2 * smoothed[rb + x] + smoothed[rb + x + 1];
                }

                int mag = Math.Abs(gx) + Math.Abs(gy);
                gradient[y * width + x] = (byte)Math.Min(255, mag);
            }
        });

        return gradient;
    }

    /// <summary>
    /// 5x5 box filter (local mean) using integral image for O(1) per-pixel lookup.
    /// Each pixel becomes the average of its 5x5 neighborhood.
    /// Border pixels use a smaller window (clamped to image bounds).
    /// </summary>
    internal static byte[] BoxFilter5x5(byte[] gray, int width, int height)
    {
        // Build integral image: integral[(y+1)*iw+(x+1)] = sum of gray[0..y, 0..x]
        int iw = width + 1;
        long[] integral = new long[iw * (height + 1)];
        for (int y = 0; y < height; y++)
        {
            long rowSum = 0;
            int srcRow = y * width;
            for (int x = 0; x < width; x++)
            {
                rowSum += gray[srcRow + x];
                integral[(y + 1) * iw + (x + 1)] = rowSum + integral[y * iw + (x + 1)];
            }
        }

        // Parallel lookup: 4 reads per pixel instead of 25
        byte[] result = new byte[width * height];
        Parallel.For(0, height, y =>
        {
            int y0 = Math.Max(0, y - 2), y1 = Math.Min(height - 1, y + 2);
            int dstRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int x0 = Math.Max(0, x - 2), x1 = Math.Min(width - 1, x + 2);
                long sum = integral[(y1 + 1) * iw + (x1 + 1)]
                         - integral[y0 * iw + (x1 + 1)]
                         - integral[(y1 + 1) * iw + x0]
                         + integral[y0 * iw + x0];
                int count = (y1 - y0 + 1) * (x1 - x0 + 1);
                result[dstRow + x] = (byte)(sum / count);
            }
        });

        return result;
    }
}
