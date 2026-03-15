# Q-Key Autocrop: IET vs RAVEN2 Analysis & Implementation Plan

## 1. Side-by-Side Comparison

### Q-Key Handler (identical in both)

```
IET   Main.cs:1200  →  AutosetCropbox(Shift ? false : true)
RAVEN Main.cs:1118  →  AutosetCropbox(Shift ? false : true)
```

- **Q** alone: `LocationOnly = true` — uses existing CropWidth/CropHeight, just repositions.
- **Shift+Q**: `LocationOnly = false` — detects AND sets new CropWidth/CropHeight.

### AutosetCropbox Algorithm (identical in both)

Both follow the same 3-layer border detection for photostat negatives:

| Step | IET (RecoIP.dll) | RAVEN2 (RavenImaging.cs) | Parameters |
|------|-------------------|--------------------------|------------|
| Guard | `F2Settings.NegativeImage != true → return` | Same | Only works for photostats |
| Cache shortcut | If BX1/BY1/BX2/BY2 cached, use them directly | Same | Adds ±80/+160 margins |
| Open | `RecoIP.ImgOpen(JPG, 0)` + `RecoIP.ImgOpen(TIF, 0)` | `RavenImaging.ImgOpen(JPG, 0)` + `RavenImaging.ImgOpen(TIF, 0)` | — |
| Size check | jpgWidth vs tifWidth, jpgHeight vs tifHeight | Same | **BUG**: vacuous, see below |
| Duplicate | `RecoIP.ImgDuplicate(ImageHandle)` | `RavenImaging.ImgDuplicate(ImageHandle)` | Copies JPG |
| AutoThreshold | `RecoIP.ImgAutoThreshold(CopyOfImage, 1)` | `RavenImaging.ImgAutoThreshold(CopyOfImage, 1)` | Algo=1 (KI) |
| A borders | FindBlackBorder L/T/R/B at 90.0%, holes=1 | Same | Overscan removal |
| Crop to A | CropBorder(aLeft, aTop, aRight, aBottom) | Same | — |
| Invert | ImgInvert(CopyOfImage) | Same | Black border→white interior |
| B borders | FindBlackBorder L/T/R/B at 99.0%, holes=1 | Same | Page border detection |
| Adjust B | bLeft += 20, bRight -= 20 | Same | 20px inward margin |
| Crop to B | CropBorder(bLeft, bTop, bRight, bBottom) | Same | — |
| C borders | FindBlackBorder L/R at 80.0%/30holes, T/B at 80.0%/100holes | Same | Photostat content |
| Composite coords | left = aLeft + (bLeft+20) + cLeft | Same | — |
| | top = aTop + bTop + cTop | Same | — |
| | right = cRight + aLeft + bLeft | Same | — |
| | bottom = cBottom + aTop + bTop | Same | — |
| Cleanup | ImgDelete × 3 | Same | — |

### SetCropbox (identical in both)

```csharp
// IET:   Main.cs:2523
// RAVEN: Main.cs:2071
private void SetCropbox(CropCordinates CropDim, bool force_active = false)
{
    keyPicture2.RemoveAnnotation(1, 1, 1);
    if (activecropbox == false || force_active == true)
    {
        activecropbox = true;
        keyPicture2.SetTransparentRect(X1, Y1, X2, Y2, 2);
        workingCropCordinates = CropDim;
    }
    else { activecropbox = false; } // toggle off
}
```

## 2. Bugs Found (present in BOTH IET and RAVEN2)

### Bug 1: Vacuous size check

```csharp
int jpgWidth = ImgGetWidth(ImageHandle);   // from JPG handle
int jpgHeight = ImgGetHeight(TifHandle);   // from TIF handle
int tifWidth = ImgGetWidth(ImageHandle);   // from JPG handle (WRONG! should be TifHandle)
int tifHeight = ImgGetHeight(TifHandle);   // from TIF handle

if (jpgWidth != tifWidth || jpgHeight != tifHeight) { ... }
// jpgWidth == tifWidth is ALWAYS true (both read from same ImageHandle)
// jpgHeight == tifHeight is ALWAYS true (both read from same TifHandle)
```

This size check never fires. It should be:
```csharp
int jpgWidth = ImgGetWidth(ImageHandle);
int jpgHeight = ImgGetHeight(ImageHandle);
int tifWidth = ImgGetWidth(TifHandle);
int tifHeight = ImgGetHeight(TifHandle);
```

### Bug 2: Shift+Q never shows the cropbox

When `LocationOnly == false` (Shift+Q), the code sets `CropLeftEven/CropTopEven/CropWidth/CropHeight` but never calls `SetCropbox()`. The rectangle dimensions are stored but the visual cropbox is not displayed.

The `LocationOnly == true` (plain Q) path DOES call `SetCropbox()` and shows the rectangle. So the workflow is: Shift+Q first to calibrate size, then Q to position it. This is by design (the Shift+Q path just records the dimensions for later use).

### Bug 3: Composite coordinate math inconsistency

```csharp
int left  = aLeft + (bLeft + 20) + cLeft;  // bLeft+20 is DOUBLE the adjustment (bLeft already += 20)
int right = cRight + aLeft + bLeft;          // uses bLeft (already adjusted by +20)
```

The `left` calculation adds 20 again to bLeft (which already had 20 added), so there's an extra +20 on the left side vs the right side. This is the same in both IET and RAVEN2, so it's a pre-existing asymmetry, not a regression.

### Not a bug: JPEG decoder divergence

The only source of differing RESULTS between IET and RAVEN2 would be the JPEG decoder:
- IET uses Recogniform's built-in Delphi JPEG decoder
- RAVEN2 uses GDI+ (via `new Bitmap(path)`)

This ~1.7% pixel-level difference (measured in the photostat pipeline) can shift the Otsu threshold by ±1, which can shift border detection by a few pixels. This is the same known variance documented in the session log and is unavoidable without byte-identical JPEG decoding.

## 3. Conclusion: What's Different?

**Functionally, nothing.** The code is line-for-line identical between IET and RAVEN2 (modulo `RecoIP` → `RavenImaging` API rename). The algorithm, parameters, coordinate math, and control flow all match exactly.

Any perceived behavioral difference would come from:
1. JPEG decoder divergence (±1.7% pixel variance → occasional ±1 border shift)
2. The vacuous size check (never triggers in either, so not observable)

## 4. Implementation Plan: Byte-Array Fast Path

### Goal
Replace the handle-based pipeline (ImgOpen → ImgDuplicate → ImgAutoThreshold → FindBlackBorder × 12 → ImgCropBorder × 3 → ImgInvert → ImgDelete × 3) with a direct byte-array pipeline that skips GDI+ Bitmap allocations and can reuse the cached grayscale from `PreloadGrayscale`.

### Estimated speedup
Current handle-based path on 3489×5385:
- ImgOpen(JPG): ~114ms (JPEG decode)
- ImgDuplicate: ~13ms
- ImgAutoThreshold: ~40ms (grayscale conversion + Otsu + 1bpp packing)
- FindBlackBorder × 12: ~5ms
- ImgCropBorder × 3: ~3ms
- ImgInvert: ~1ms
- Total: ~176ms per Q-key press

Byte-array fast path:
- Skip JPG decode if grayscale cached: 0ms (vs 114ms)
- Skip duplicate: 0ms (work on byte arrays, no Bitmap)
- AutoThreshold on byte array: ~25ms (skip grayscale conversion)
- FindBlackBorder on packed 1bpp: ~5ms
- Crop as index math: ~0ms
- Invert as byte flip: ~0ms
- Total: ~30ms per Q-key press (5.8× speedup)

### Implementation steps

**Step 1: Add `AutosetCropboxFast()` to Main.cs**

```csharp
private void AutosetCropboxFast(bool LocationOnly = false)
{
    if (F2Settings.NegativeImage != true) return;

    // Cache shortcut (same as existing)
    if (LocationOnly && ImagePairs[currentImageIndex].BX1 > 0 && ...)
    {
        // ... existing cache shortcut code (unchanged) ...
        return;
    }

    // Try to get cached grayscale from OpenThresholdBridge
    string jpgPath = ImagePairs[currentImageIndex].JPG;
    byte[] gray;
    int w, h;
    if (!OpenThresholdBridge.TryGetCachedGrayscale(out gray, out w, out h, jpgPath))
    {
        // Cache miss — load from disk (still faster than handle-based)
        (gray, w, h) = RavenImaging.LoadImageAsGrayscale(jpgPath);
    }

    // Verify TIF dimensions match (fix the vacuous check bug)
    string tifPath = ImagePairs[currentImageIndex].TIF;
    var (tifW, tifH) = RavenImaging.GetImageDimensions(tifPath);
    if (w != tifW || h != tifH)
    {
        StatusUpdate("TIF & JPG Size Don't Match!");
        return;
    }

    // AutoThreshold (KI=1) on grayscale → returns threshold T
    int T = RavenImaging.NativeAutoThresholdPublic(gray, w, h, w, 1);

    // Pack to 1bpp byte array (not a Bitmap — just raw bytes)
    int stride1 = (w + 7) / 8;  // round up to byte boundary
    // Pad stride to 4-byte alignment to match GDI+ expectations for FindBlackBorder
    stride1 = (stride1 + 3) & ~3;
    byte[] packed = new byte[stride1 * h];
    Pack1bpp(gray, packed, w, h, stride1, T);

    // A borders: overscan detection (90%, holes=1)
    int aLeft   = RavenImaging.FindBlackBorderDirect(packed, stride1, w, h, 1, 0, 90.0, 1);
    int aTop    = RavenImaging.FindBlackBorderDirect(packed, stride1, w, h, 1, 2, 90.0, 1);
    int aRight  = RavenImaging.FindBlackBorderDirect(packed, stride1, w, h, 1, 1, 90.0, 1);
    int aBottom = RavenImaging.FindBlackBorderDirect(packed, stride1, w, h, 1, 3, 90.0, 1);

    if (aLeft > aRight || aTop > aBottom) return;

    // Crop packed array to A borders
    byte[] cropped1 = CropPacked1bpp(packed, stride1, w, h, aLeft, aTop, aRight, aBottom);
    int w1 = aRight - aLeft + 1;
    int h1 = aBottom - aTop + 1;
    int stride1a = (w1 + 7) / 8;
    stride1a = (stride1a + 3) & ~3;

    // Invert
    for (int i = 0; i < cropped1.Length; i++) cropped1[i] = (byte)~cropped1[i];

    // B borders: page border (99%, holes=1)
    int bLeft   = RavenImaging.FindBlackBorderDirect(cropped1, stride1a, w1, h1, 1, 0, 99.0, 1);
    int bTop    = RavenImaging.FindBlackBorderDirect(cropped1, stride1a, w1, h1, 1, 2, 99.0, 1);
    int bRight  = RavenImaging.FindBlackBorderDirect(cropped1, stride1a, w1, h1, 1, 1, 99.0, 1);
    int bBottom = RavenImaging.FindBlackBorderDirect(cropped1, stride1a, w1, h1, 1, 3, 99.0, 1);

    bLeft += 20;
    bRight -= 20;

    if (bLeft > bRight || bTop > bBottom) return;

    // Crop to B borders
    byte[] cropped2 = CropPacked1bpp(cropped1, stride1a, w1, h1, bLeft, bTop, bRight, bBottom);
    int w2 = bRight - bLeft + 1;
    int h2 = bBottom - bTop + 1;
    int stride2 = (w2 + 7) / 8;
    stride2 = (stride2 + 3) & ~3;

    // C borders: photostat content (80%, 30/100 holes)
    int cLeft   = RavenImaging.FindBlackBorderDirect(cropped2, stride2, w2, h2, 1, 0, 80.0, 30);
    int cTop    = RavenImaging.FindBlackBorderDirect(cropped2, stride2, w2, h2, 1, 2, 80.0, 100);
    int cRight  = RavenImaging.FindBlackBorderDirect(cropped2, stride2, w2, h2, 1, 1, 80.0, 30);
    int cBottom = RavenImaging.FindBlackBorderDirect(cropped2, stride2, w2, h2, 1, 3, 80.0, 100);

    // Composite coordinates (same math as original)
    int left   = aLeft + (bLeft + 20) + cLeft;
    int top    = aTop + bTop + cTop;
    int right  = cRight + aLeft + bLeft;
    int bottom = cBottom + aTop + bTop;

    // ... rest of SetCropbox logic (identical to existing) ...
}
```

**Step 2: Add helper methods**

```csharp
// Pack grayscale to 1bpp (white = pixel > T)
private static void Pack1bpp(byte[] gray, byte[] packed, int w, int h, int stride, int T)
{
    int fullBytes = w >> 3;
    int remainder = w & 7;
    for (int y = 0; y < h; y++)
    {
        int srcRow = y * w;
        int dstRow = y * stride;
        int sx = 0;
        for (int bx = 0; bx < fullBytes; bx++)
        {
            byte b = 0;
            if (gray[srcRow + sx]     > T) b |= 0x80;
            if (gray[srcRow + sx + 1] > T) b |= 0x40;
            if (gray[srcRow + sx + 2] > T) b |= 0x20;
            if (gray[srcRow + sx + 3] > T) b |= 0x10;
            if (gray[srcRow + sx + 4] > T) b |= 0x08;
            if (gray[srcRow + sx + 5] > T) b |= 0x04;
            if (gray[srcRow + sx + 6] > T) b |= 0x02;
            if (gray[srcRow + sx + 7] > T) b |= 0x01;
            packed[dstRow + bx] = b;
            sx += 8;
        }
        if (remainder > 0)
        {
            byte b = 0;
            for (int bit = 0; bit < remainder; bit++)
                if (gray[srcRow + sx + bit] > T) b |= (byte)(0x80 >> bit);
            packed[dstRow + fullBytes] = b;
        }
    }
}

// Crop a 1bpp packed array to [left, top, right, bottom] inclusive
private static byte[] CropPacked1bpp(byte[] src, int srcStride, int srcW, int srcH,
    int left, int top, int right, int bottom)
{
    int w = right - left + 1;
    int h = bottom - top + 1;
    int dstStride = ((w + 7) / 8 + 3) & ~3;
    byte[] dst = new byte[dstStride * h];

    for (int y = 0; y < h; y++)
    {
        int srcY = top + y;
        for (int x = 0; x < w; x++)
        {
            int srcX = left + x;
            int srcByte = srcY * srcStride + (srcX >> 3);
            int srcBit = 7 - (srcX & 7);
            bool isWhite = (src[srcByte] & (1 << srcBit)) != 0;
            if (isWhite)
            {
                int dstByte = y * dstStride + (x >> 3);
                int dstBit = 7 - (x & 7);
                dst[dstByte] |= (byte)(1 << dstBit);
            }
        }
    }
    return dst;
}
```

**Step 3: Add `TryGetCachedGrayscale` to OpenThresholdBridge.cs**

```csharp
public static bool TryGetCachedGrayscale(out byte[] gray, out int width, out int height, string jpgPath)
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
    gray = null; width = height = 0;
    return false;
}
```

**Step 4: Wire into Q-key handler**

Replace `AutosetCropbox(...)` call with `AutosetCropboxFast(...)`.

**Step 5: Fix the vacuous size check**

```csharp
// OLD (both IET and RAVEN2):
int jpgWidth = ImgGetWidth(ImageHandle);   // JPG
int jpgHeight = ImgGetHeight(TifHandle);   // TIF  ← wrong
int tifWidth = ImgGetWidth(ImageHandle);   // JPG  ← wrong
int tifHeight = ImgGetHeight(TifHandle);   // TIF

// NEW:
int jpgWidth = ImgGetWidth(ImageHandle);    // JPG
int jpgHeight = ImgGetHeight(ImageHandle);  // JPG
int tifWidth = ImgGetWidth(TifHandle);      // TIF
int tifHeight = ImgGetHeight(TifHandle);    // TIF
```

In the byte-array path, this becomes direct dimension comparison via `RavenImaging.GetImageDimensions()` or loading both and comparing w/h.

## 5. Design: Background Border Pre-Computation Toggle

### Concept
When the user navigates to a new image, pre-compute the autocrop borders in the background using the already-cached grayscale bytes. When Q is pressed, if borders are available, use them instantly (0ms). If not, compute on demand (~30ms with byte-array path).

### Data structures

```csharp
// In OpenThresholdBridge.cs (or a new BorderCache.cs)
public static class BorderCache
{
    private static readonly Dictionary<string, BorderResult> _cache
        = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    // Settings that affect border detection (if these change, cache is invalid)
    private static bool _enabled = false; // toggle

    public record BorderResult(int Left, int Top, int Right, int Bottom, bool Valid);

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public static bool TryGet(string imagePath, out BorderResult result)
    {
        lock (_lock) return _cache.TryGetValue(imagePath, out result);
    }

    public static void Set(string imagePath, BorderResult result)
    {
        lock (_lock) _cache[imagePath] = result;
    }

    public static void Invalidate(string imagePath)
    {
        lock (_lock) _cache.Remove(imagePath);
    }

    public static void Clear()
    {
        lock (_lock) _cache.Clear();
    }
}
```

### Integration points

1. **On navigation** (`PrevImage_Click`, `NextImage_Click`, `JumpTo`):
   - After `PreloadForOpenThreshold()` (which caches grayscale), if `BorderCache.Enabled`:
   ```csharp
   Task.Run(() =>
   {
       if (!BorderCache.TryGet(jpg, out _))
       {
           var borders = ComputeBorders(jpg);
           BorderCache.Set(jpg, borders);
       }
   });
   ```

2. **On Q-key press** (`AutosetCropboxFast`):
   - Check `BorderCache.TryGet(jpgPath, out result)` first
   - If hit: use `result.Left/Top/Right/Bottom` directly (skip all computation)
   - If miss: compute on demand, then cache the result

3. **On image edit** (whiteout, crop, threshold, deskew, line removal):
   - Call `BorderCache.Invalidate(currentImagePath)` since the TIF changed
   - The JPG doesn't change (source image), but the TIF (thresholded output) might affect how autocrop interprets the layout — currently AutosetCropbox works on the JPG, so edits to TIF don't invalidate. However, if the workflow changes to use the TIF for border detection, invalidation would be needed.

4. **Toggle mechanism**:
   - Add a checkbox in `ThresholdSettings.cs` or a settings.ini entry: `PrecomputeBorders=true/false`
   - Default: disabled (opt-in)
   - When disabled: `BorderCache.Enabled = false`, no background computation happens

### Cache invalidation strategy

| Event | Action |
|-------|--------|
| Navigate to new image | Compute borders in background (if enabled) |
| Edit TIF (whiteout, crop, etc.) | No invalidation needed (autocrop uses JPG) |
| Change NegativeImage setting | `BorderCache.Clear()` (border detection only works for negatives) |
| Change F2Settings | `BorderCache.Clear()` (settings change could affect workflow) |
| Toggle pre-computation off | `BorderCache.Clear()` |
| Close folder / open new folder | `BorderCache.Clear()` |

### Memory considerations
- Each `BorderResult` is ~24 bytes (4 ints + bool + overhead)
- 10,000 images: ~240KB — negligible
- The grayscale arrays are NOT stored in BorderCache — only the computed border coordinates
- The grayscale arrays are already managed by `OpenThresholdBridge._cachedGray` (only 1 image at a time)

### Pre-computation flow

```
User navigates to image N
  → PreloadForOpenThreshold() starts background grayscale cache of image N
  → If BorderCache.Enabled:
      → Wait for grayscale to be ready (or compute independently)
      → Run border detection pipeline on grayscale bytes
      → Store (left, top, right, bottom) in BorderCache

User presses Q on image N
  → AutosetCropboxFast() checks BorderCache
  → Hit: instant (0ms)
  → Miss: compute on demand (~30ms), cache for next time
```

### Future enhancement: pre-compute ahead
Could extend to pre-compute borders for adjacent images (±3) during prefetch, similar to how `PrefetchAdjacentImages()` pre-decodes images for display. This would make Q-key instant even on first press after navigation.
