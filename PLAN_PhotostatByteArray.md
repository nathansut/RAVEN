# Photostat Pipeline Byte-Array Rewrite Plan

## Goal

Rewrite the photostat (negative) full-conversion pipeline to eliminate all GDI+ handle operations, using byte-array native calls throughout. This matches the pattern already proven in `OpenThresholdBridge.ApplyThresholdToFile` for non-negative conversions.

## Current Pipeline (27 Steps)

The existing code in Main.cs:3306-3421 uses handle-based operations:

| Step | Operation | Handle Op | Input Format | Output Format |
|------|-----------|-----------|-------------|---------------|
| 1 | `ImgDuplicate(ImageHandle)` → tImageHandle | GDI+ DeepClone | 24bpp | 24bpp |
| 2 | `ImgDuplicate(tImageHandle)` → CopyOfImage | GDI+ DeepClone | 24bpp | 24bpp |
| 3 | `ImgAutoThreshold(CopyOfImage, 2)` | ExtractGray + Pack1bpp | 24bpp → gray → 1bpp | 1bpp Bitmap |
| 4 | `FindBlackBorder(CopyOfImage, 90%, 1)` x4 | LockBits | 1bpp packed | int coords |
| 5 | `ImgCropBorder(CopyOfImage, a*)` | Clone region + DeepClone | 1bpp | 1bpp |
| 6 | `ImgInvert(CopyOfImage)` | LockBits XOR | 1bpp | 1bpp |
| 7 | `FindBlackBorder(CopyOfImage, 99%, 1)` x4 | LockBits | 1bpp | int coords |
| 8 | `bLeft += 20; bRight -= 20` | arithmetic | - | - |
| 9 | `ImgCropBorder(CopyOfImage, b*)` | Clone region + DeepClone | 1bpp | 1bpp |
| 10 | `FindBlackBorder(CopyOfImage, 80%, 30/100)` x4 | LockBits | 1bpp | int coords |
| 11 | `ImgDelete(CopyOfImage)` | Dispose | - | - |
| 12 | `ImgRemoveBleedThrough(tImageHandle, 1)` | LockBits 24bpp R/W | 24bpp BGR | 24bpp BGR |
| 13 | `ImgCopy(tImageHandle, a*)` → Copy1 | Clone region | 24bpp | 24bpp |
| 14 | `ImgCopy(Copy1, b*)` → Copy2 | Clone region | 24bpp | 24bpp |
| 15 | `ImgCopy(Copy2, c*)` → Photostat | Clone region | 24bpp | 24bpp |
| 16 | `ImgInvert(Photostat)` | LockBits XOR | 24bpp | 24bpp |
| 17 | DT or RDynamic on Photostat | ExtractGray + threshold | 24bpp → gray → 1bpp | 1bpp |
| 18 | `ImgDespeckle(Photostat, d, d)` | LockBits 1bpp R/W | 1bpp packed | 1bpp packed |
| 19 | `ImgRemoveBlackWires(Photostat)` | LockBits 1bpp R/W | 1bpp packed | 1bpp packed |
| 20 | `ImgRemoveVerticalLines(Photostat, ...)` | LockBits 1bpp R/W | 1bpp packed | 1bpp packed |
| 21 | `ImgAdaptiveThresholdAverage(tImageHandle, -1,-1)` | ExtractGray + AT | 24bpp → gray → 1bpp | 1bpp |
| 22 | `ImgAdaptiveThresholdAverage(Copy1, 40,230)` | ExtractGray + AT | 24bpp → gray → 1bpp | 1bpp |
| 23 | `ImgAdaptiveThresholdAverage(Copy2, 40,230)` | ExtractGray + AT | 24bpp → gray → 1bpp | 1bpp |
| 24 | `ImgAddCopy(Copy2, Photostat, c*)` | LockBits 1bpp blit | 1bpp | 1bpp |
| 25 | `ImgAddCopy(Copy1, Copy2, b*)` | LockBits 1bpp blit | 1bpp | 1bpp |
| 26 | `ImgAddCopy(tImageHandle, Copy1, a*)` | LockBits 1bpp blit | 1bpp | 1bpp |
| 27 | Save tImageHandle as TIF | GDI+ CCITT4 Save | 1bpp | .tif file |

---

## New Method Signature

```csharp
// In OpenThresholdBridge.cs
public static void ApplyThresholdToFilePhotostat(
    string inputJpgPath, string outputTifPath,
    int windowW, int windowH, int contrast, int brightness,
    int despeckle)
```

No `RefineThreshold` parameter — the existing code in Main.cs already shows a `MessageBox.Show("Refine Threshold not supported...")` for full photostat conversions.

---

## New Helper Method in RavenImaging.cs

We need one new loader that returns both grayscale AND raw BGR data:

```csharp
public static (byte[] grayLut, byte[] bgr, int bgrStride, int width, int height)
    LoadImageAsGrayscaleAndBgr(string path)
```

This is a minor modification of the existing `LoadImageAsGrayscaleDual` — instead of discarding the BGR buffer after computing grayscale, it returns it. The `bgr` array uses GDI+ stride (which is `((w * 3 + 3) & ~3)` — i.e. DWORD-aligned scanlines). The `bgrStride` value is needed by `NativeRemoveBleedThrough` and `NativeFindBlackBorder`.

Alternatively, we could add a cache field for BGR data to OpenThresholdBridge, but since photostat is the only consumer, the simpler approach is a new loader.

---

## Byte-Array Pipeline Design

### Data Buffers

| Buffer | Format | Size | Lifetime |
|--------|--------|------|----------|
| `grayLut` | byte-per-pixel grayscale (LUT weights) | W*H | Full pipeline |
| `bgr` | 24bpp BGR with GDI+ stride | bgrStride*H | Until RemoveBleedThrough completes |
| `binaryFull` | byte-per-pixel 0/255 | W*H | Built from border AT steps; composited at end |
| `packed1bpp` | 1bpp packed | byteWidth*H | Final output for save |

### Key Insight 1: FindBlackBorder on byte arrays

`NativeFindBlackBorder(buf, stride, w, h, bpp, side, minBlackPct, maxHoles)` already supports:
- `bpp=1`: packed 1bpp buffer (1 bit per pixel, stride = (w+7)/8 or DWORD-aligned)
- `bpp=8`: byte-per-pixel grayscale
- `bpp=24`: BGR buffer with stride

For border detection (steps 3-10), the original pipeline:
1. Duplicates the 24bpp image
2. AutoThresholds it to 1bpp
3. Finds borders on the 1bpp result

**In byte-array land**: We can do the same without Bitmap handles:
1. `NativeAutoThreshold(grayLut, w, h, w, 2)` → returns threshold T
2. Pack grayscale to 1bpp using T: `packed[i] = (gray[i] > T) ? set_bit : clear_bit`
3. `NativeFindBlackBorder(packed1bpp, byteWidth, w, h, 1, side, pct, holes)` → border coords
4. For inverted borders: XOR the packed buffer, then find borders again

### Key Insight 2: RemoveBleedThrough on raw BGR

`NativeRemoveBleedThrough(bgr, w, h, stride, tolerance)` works directly on 24bpp BGR byte arrays. No Bitmap needed. We just pass the BGR buffer from the JPEG decode.

**However**: The original pipeline calls RemoveBleedThrough on the FULL image. But it only extracts grayscale from the content sub-region for DT. So we need to either:
- (a) Call RemoveBleedThrough on the full BGR array, then re-extract grayscale from the content region, or
- (b) Call it on the full BGR, convert the content region to grayscale afterward

Option (a) is cleaner. Since we loaded grayLut before RemoveBleedThrough, we'd need to re-derive the content region's grayscale from the modified BGR. This is a small cost (just the content region, not the full image).

### Key Insight 3: "Cropping" is just coordinate arithmetic

In the handle-based pipeline, `ImgCopy` creates a new Bitmap with the cropped region. In byte-array land, we don't need to copy — we just track the coordinates and pass them to functions that support strided access.

**However**: The native functions `NativeDynamicThresholdAverage` and `NativeAdaptiveThresholdAverage` take `(gray, result, w, h, ...)` with an implicit stride=w (no stride parameter). They expect a contiguous WxH buffer. So we DO need to extract sub-regions into contiguous buffers for DT and AT.

For AT specifically: The original pipeline calls AT on Copy1 (the overscan region) and Copy2 (the page region). These are 24bpp handles. AT internally does `ExtractGrayscaleLut(bmp)` to get grayscale, then calls `NativeAdaptiveThresholdAverage`. In byte-array land, we extract the grayscale sub-region into a contiguous buffer, call native AT, and get back a byte-per-pixel binary result.

### Key Insight 4: Despeckle/Wires/VerticalLines need 1bpp packed

All three cleanup functions (`NativeDespeckle`, `NativeRemoveBlackWires`, `NativeRemoveVerticalLines`) operate on packed 1bpp data with a stride parameter. So after DT produces byte-per-pixel binary, we pack to 1bpp, run all three, then unpack or keep packed.

### Key Insight 5: Final composite is 1bpp region copies

The final compositing (steps 24-26) pastes smaller regions into larger ones at (x, y) offsets. In packed 1bpp, this is byte/bit manipulation (already implemented in `AddCopyIndexed1bpp`). We can replicate this in a static helper method operating on packed byte arrays.

---

## Detailed Step-by-Step Implementation

### Phase A: Load and prepare (decode time)

```
A1. Load JPEG → grayLut (byte-per-pixel, W*H) + bgr (24bpp, bgrStride*H)
    - New method: RavenImaging.LoadImageAsGrayscaleAndBgr(inputJpgPath)
    - Try cache first (modify PreloadGrayscale to also cache BGR)
    - Or: just use existing LoadImageAsGrayscaleDual + reload BGR from disk
      (but that's 2x JPEG decode — wasteful)
    - BEST: new method that returns both grayLut AND bgr in one pass
```

### Phase B: Border detection (can all use packed 1bpp from grayscale)

```
B1. AutoThreshold: T = NativeAutoThreshold(grayLut, w, h, w, 2)  // Ridler-Calvard
B2. Pack to 1bpp using T:
      byte[] borderBuf = new byte[byteWidth * H]
      for each pixel: if grayLut[i] > T → set white bit, else → black bit
B3. FindBlackBorder on borderBuf (1bpp):
      aLeft   = NativeFindBlackBorder(borderBuf, byteWidth, w, h, 1, 0, 90.0, 1)
      aTop    = NativeFindBlackBorder(borderBuf, byteWidth, w, h, 1, 2, 90.0, 1)
      aRight  = NativeFindBlackBorder(borderBuf, byteWidth, w, h, 1, 1, 90.0, 1)
      aBottom = NativeFindBlackBorder(borderBuf, byteWidth, w, h, 1, 3, 90.0, 1)
B4. Guard: if aLeft > aRight || aTop > aBottom → skip photostat pipeline
```

### Phase C: Crop + Invert + Find page borders

The original pipeline crops borderBuf to the a-region, inverts it, then finds borders again. In byte-array land, we need to crop the packed 1bpp buffer to a sub-region.

**Option 1**: Repack. Extract grayLut sub-region [aLeft..aRight, aTop..aBottom], apply threshold T, invert (XOR), pack to 1bpp. This is clean and fast.

**Option 2**: Crop the packed 1bpp buffer using bit operations.

Option 1 is simpler and avoids bit-level complexity:

```
C1. Compute cropped dimensions:
      aW = aRight - aLeft + 1  (inclusive coordinates)
      aH = aBottom - aTop + 1
C2. Extract grayscale sub-region and invert+threshold:
      byte[] aCrop1bpp = new byte[aCropByteWidth * aH]
      For each pixel in [aLeft..aRight, aTop..aBottom]:
        gray value = grayLut[y * W + x]
        thresholded = (gray > T) ? white : black
        inverted = !thresholded  (because ImgInvert was called after threshold)
        Pack into aCrop1bpp
C3. FindBlackBorder on aCrop1bpp:
      bLeft   = NativeFindBlackBorder(aCrop1bpp, aCropByteWidth, aW, aH, 1, 0, 99.0, 1)
      bTop    = NativeFindBlackBorder(aCrop1bpp, aCropByteWidth, aW, aH, 1, 2, 99.0, 1)
      bRight  = NativeFindBlackBorder(aCrop1bpp, aCropByteWidth, aW, aH, 1, 1, 99.0, 1)
      bBottom = NativeFindBlackBorder(aCrop1bpp, aCropByteWidth, aW, aH, 1, 3, 99.0, 1)
C4. Inset: bLeft += 20; bRight -= 20
C5. Guard: if bLeft > bRight || bTop > bBottom → skip
```

### Phase D: Find content borders

```
D1. Crop the inverted 1bpp to the b-region:
      bW = bRight - bLeft + 1
      bH = bBottom - bTop + 1
      Extract sub-region of aCrop1bpp at [bLeft..bRight, bTop..bBottom] → bCrop1bpp
      (Or: re-derive from grayLut with threshold+invert, but that recomputes)
D2. FindBlackBorder on bCrop1bpp:
      cLeft   = NativeFindBlackBorder(bCrop1bpp, bCropByteWidth, bW, bH, 1, 0, 80.0, 30)
      cTop    = NativeFindBlackBorder(bCrop1bpp, bCropByteWidth, bW, bH, 1, 2, 80.0, 100)
      cRight  = NativeFindBlackBorder(bCrop1bpp, bCropByteWidth, bW, bH, 1, 1, 80.0, 30)
      cBottom = NativeFindBlackBorder(bCrop1bpp, bCropByteWidth, bW, bH, 1, 3, 80.0, 100)
D3. Free borderBuf, aCrop1bpp, bCrop1bpp — border detection complete.
```

### Phase E: RemoveBleedThrough on full BGR

```
E1. NativeRemoveBleedThrough(bgr, W, H, bgrStride, 1)
    - Modifies bgr in-place
    - This is the FULL image, not cropped
    - After this, we need grayscale for the content region from the modified BGR
```

### Phase F: Extract content region grayscale (inverted for negative)

The content region in absolute coordinates:
```
absContentLeft   = aLeft + bLeft + cLeft
absContentTop    = aTop  + bTop  + cTop
absContentRight  = aLeft + bLeft + cRight
absContentBottom = aTop  + bTop  + cBottom
contentW = cRight - cLeft + 1
contentH = cBottom - cTop + 1
```

```
F1. Extract content grayscale from MODIFIED bgr (post-bleedthrough):
      byte[] contentGray = new byte[contentW * contentH]
      For y in [0..contentH):
        absY = absContentTop + y
        For x in [0..contentW):
          absX = absContentLeft + x
          off = absY * bgrStride + absX * 3
          B = bgr[off], G = bgr[off+1], R = bgr[off+2]
          contentGray[y * contentW + x] = lutR[R] + lutG[G] + lutB[B]
F2. Invert: for each pixel, contentGray[i] = 255 - contentGray[i]
    - This matches ImgInvert(Photostat) on the 24bpp handle before thresholding.
    - The original inverts the 24bpp image (XOR all bytes).
    - For grayscale, XOR 0xFF is the same as 255 - val.
    - HOWEVER: The original inverts the 24bpp Photostat, then DT extracts grayscale.
      Inverting 24bpp then extracting LUT-weighted gray is NOT the same as extracting
      gray then inverting (because the LUT is non-linear: lutR[255-r] != 255-lutR[r]).
    - IMPORTANT: We need to invert the BGR channels FIRST, then compute grayscale.
      Alternative: invert at the BGR level during extraction:
        B' = 255 - bgr[off], G' = 255 - bgr[off+1], R' = 255 - bgr[off+2]
        contentGray[i] = lutR[R'] + lutG[G'] + lutB[B']
```

**CRITICAL CORRECTNESS NOTE**: The original pipeline does:
1. Extract 24bpp region (ImgCopy)
2. Invert 24bpp (ImgInvert — XORs all bytes, so R→255-R, G→255-G, B→255-B)
3. DT internally calls ExtractGrayscaleLut → `lutR[255-R] + lutG[255-G] + lutB[255-B]`

So our grayscale extraction for the content region must be:
```csharp
contentGray[i] = (byte)(_lutR[255 - R] + _lutG[255 - G] + _lutB[255 - B]);
```

This is NOT the same as `255 - (lutR[R] + lutG[G] + lutB[B])`. Example:
- R=200: lutR[200]=60, lutR[55]=16.5→17. So inverted gray uses 17, not 255-60=195.

Wait, actually: for a grayscale image where R=G=B, the LUT weight sum is always ~1.0 (0.30+0.59+0.11=1.00). So `lutR[255-v]+lutG[255-v]+lutB[255-v]` ≈ `255-v` for true gray. But for color images with different R,G,B values, it's different. Since photostats are scanned color images, we must use the correct per-channel inversion.

### Phase G: Threshold content region

```
G1. byte[] contentBinary = DynamicThresholdApply(contentGray, contentW, contentH,
                                                  windowW, windowH, contrast, brightness)
    - Returns byte-per-pixel 0/255 array
```

### Phase H: Post-processing on content (1bpp packed)

```
H1. Pack contentBinary to 1bpp:
      byte[] contentPacked = PackTo1bpp(contentBinary, contentW, contentH)
      int contentByteWidth = (contentW + 7) / 8
H2. if (despeckle > 0):
      NativeDespeckle(contentPacked, contentByteWidth, contentW, contentH, despeckle, despeckle)
H3. NativeRemoveBlackWires(contentPacked, contentByteWidth, contentW, contentH)
H4. Compute RemoveVerticalLines parameters:
      int PhHeight = contentH - 10
      int PhRatio  = PhHeight / 5
      int phBreaks = PhHeight - 15000
      NativeRemoveVerticalLines(contentPacked, contentByteWidth, contentW, contentH,
                                 PhHeight, phBreaks, PhRatio)
```

### Phase I: AdaptiveThreshold on border regions (PARALLELIZABLE)

This is the biggest performance win. The three AT calls are independent:

**Full image AT** (step 21): AT with auto params (-1, -1) on the full image grayscale.
```
I1a. Extract grayscale from MODIFIED bgr (full image):
       Actually, we already have grayLut from step A1, but that was BEFORE RemoveBleedThrough.
       After RemoveBleedThrough modified the BGR buffer, the grayscale is different.
       So we need to re-derive grayscale from the modified BGR.

       byte[] grayFull = new byte[W * H]
       Parallel conversion from bgr → grayFull using LUT weights

I1b. byte[] fullResult = new byte[W * H]
       NativeAdaptiveThresholdAverage(grayFull, fullResult, W, H, 7, 7, -1, -1)
```

**Overscan region AT** (step 22): AT(40, 230) on the overscan (a-region).
```
I2a. Extract grayscale sub-region from grayFull at [aLeft..aRight, aTop..aBottom]:
       byte[] aGray = new byte[aW * aH]
       for y in [0..aH): BlockCopy from grayFull
I2b. byte[] aResult = new byte[aW * aH]
       NativeAdaptiveThresholdAverage(aGray, aResult, aW, aH, 7, 7, 40, 230)
```

**Page region AT** (step 23): AT(40, 230) on the page (b-region, relative to overscan).
```
I3a. Extract grayscale sub-region from grayFull at absolute [aLeft+bLeft..aLeft+bRight, aTop+bTop..aTop+bBottom]:
       byte[] bGray = new byte[bW * bH]
       for y in [0..bH): BlockCopy from grayFull
I3b. byte[] bResult = new byte[bW * bH]
       NativeAdaptiveThresholdAverage(bGray, bResult, bW, bH, 7, 7, 40, 230)
```

**All three can run in parallel** with `Parallel.Invoke()`:

```csharp
Parallel.Invoke(
    () => NativeAdaptiveThresholdAverage(grayFull, fullResult, W, H, 7, 7, -1, -1),
    () => NativeAdaptiveThresholdAverage(aGray, aResult, aW, aH, 7, 7, 40, 230),
    () => NativeAdaptiveThresholdAverage(bGray, bResult, bW, bH, 7, 7, 40, 230)
);
```

### Phase J: Composite all regions

The compositing goes innermost-out:
1. Paste content (1bpp packed) into page region (byte-per-pixel binary)
2. Paste page into overscan
3. Paste overscan into full

Since AT produces byte-per-pixel 0/255 results, and content cleanup produces packed 1bpp, we have a format mismatch. Options:

**Option A**: Convert everything to byte-per-pixel, composite, then pack once at the end.
- Composite is trivial: `Buffer.BlockCopy` for aligned rectangular regions
- Only one PackTo1bpp call at the very end
- Higher memory (full image as byte-per-pixel)

**Option B**: Pack each AT result to 1bpp, composite in 1bpp.
- Needs bit-level compositing code
- Less memory
- More complex

**Option A is simpler and memory cost is acceptable** (18.8M bytes for a 3489x5385 image):

```
J1. Unpack contentPacked back to byte-per-pixel:
      UnpackFrom1bpp(contentPacked, contentBinary, contentW, contentH)
      (or keep the original contentBinary and apply the despeckle/wires/lines changes)

      Actually, since despeckle/wires/lines modify the packed buffer in-place,
      we need to unpack to get the post-cleanup byte-per-pixel data.

J2. Composite content into bResult (page):
      for y in [0..contentH):
        Buffer.BlockCopy(contentBinary, y * contentW,
                         bResult, (cTop + y) * bW + cLeft, contentW)

J3. Composite page (bResult) into aResult (overscan):
      for y in [0..bH):
        Buffer.BlockCopy(bResult, y * bW,
                         aResult, (bTop + y) * aW + bLeft, bW)

J4. Composite overscan (aResult) into fullResult (full image):
      for y in [0..aH):
        Buffer.BlockCopy(aResult, y * aW,
                         fullResult, (aTop + y) * W + aLeft, aW)
```

### Phase K: Save and cache

```
K1. Pack fullResult to 1bpp:
      byte[] packed = PackTo1bpp(fullResult, W, H)
K2. Cache for display:
      _cachedTif = fullResult
      _cachedTifW = W; _cachedTifH = H
      _cachedTifPath = outputTifPath
K3. QueueSave(fullResult, W, H, outputTifPath, packed)
```

---

## Coordinate System Summary

All coordinates use the INCLUSIVE convention from the Recogniform DLL:
- Width  = Right - Left + 1
- Height = Bottom - Top + 1

Absolute coordinates for regions:

| Region | Left | Top | Right | Bottom |
|--------|------|-----|-------|--------|
| Overscan (a) | aLeft | aTop | aRight | aBottom |
| Page (b) — relative to a | bLeft | bTop | bRight | bBottom |
| Content (c) — relative to b | cLeft | cTop | cRight | cBottom |
| Page (b) — absolute | aLeft+bLeft | aTop+bTop | aLeft+bRight | aTop+bBottom |
| Content (c) — absolute | aLeft+bLeft+cLeft | aTop+bTop+cTop | aLeft+bLeft+cRight | aTop+bTop+cBottom |

---

## Memory Budget (3489 x 5385 = 18.8M pixels)

| Buffer | Size | Notes |
|--------|------|-------|
| grayLut | 18.8 MB | Original grayscale, freed after grayFull computed |
| bgr | 56.4 MB (with stride) | 24bpp, freed after AT grayscale extraction |
| borderBuf (1bpp) | 2.4 MB | Border detection, freed after Phase D |
| aCrop1bpp, bCrop1bpp | < 2 MB each | Small 1bpp crops, freed quickly |
| grayFull | 18.8 MB | Post-bleedthrough grayscale for AT |
| contentGray | < 15 MB | Content region, freed after DT |
| contentBinary | < 15 MB | Content result |
| contentPacked | < 2 MB | 1bpp packed content |
| aGray, aResult | < 18 MB each | Overscan AT |
| bGray, bResult | < 15 MB each | Page AT |
| fullResult | 18.8 MB | Final composite |
| packed (1bpp) | 2.4 MB | For CCITT4 save |

Peak memory: ~130 MB (transient), which is comparable to current handle-based pipeline (each GDI+ Bitmap also holds pixel data + GDI+ overhead).

---

## Performance Analysis

### Current handle-based timings (from ThresholdBench, 3489x5385):

| Operation | Time |
|-----------|------|
| JPEG decode | 114 ms |
| ImgDuplicate (DeepClone 24bpp) | 13 ms |
| ExtractGrayscaleLut (from 24bpp Bitmap) | ~30 ms |
| NativeDT | 98 ms |
| NativeAT | 401 ms |
| BinaryTo1bpp packing | 9 ms |
| NativeDespeckle | 10 ms |
| NativeRemoveBlackWires | ~5 ms |
| NativeRemoveVerticalLines | ~5 ms |
| NativeRemoveBleedThrough | ~50 ms |
| GDI+ LockBits/UnlockBits overhead | ~5 ms each |
| ImgCopy (24bpp region) | ~20 ms |
| ImgCropBorder | ~20 ms |
| ImgAddCopy (1bpp blit) | ~5 ms |
| CCITT4 save | 26 ms |

### Estimated handle-based total:

| Phase | Estimated ms |
|-------|-------------|
| Decode + duplicate x2 | 114 + 26 = 140 |
| AutoThreshold + border detection | 30 + 5 + 3*5 = 50 |
| Crop + invert + border detection x2 | 20 + 5 + 20 + 5 + 20 = 70 |
| RemoveBleedThrough | 50 |
| ImgCopy x3 + ImgInvert | 60 + 5 = 65 |
| DT (content) | 30 + 98 = 128 |
| Despeckle + wires + lines | 10 + 5 + 5 + 3*9 = 47 |
| AT x3 (SEQUENTIAL!) | 3 * (30 + 401 + 9) = 1320 |
| AddCopy x3 | 15 |
| Save | 26 |
| **TOTAL** | **~1910 ms** |

### Estimated byte-array total:

| Phase | Estimated ms | Notes |
|-------|-------------|-------|
| Decode (single JPEG load → gray + BGR) | 114 | Same as current |
| AutoThreshold + pack + border detect | 5 + 5 + 3 = 13 | No GDI+ overhead |
| Sub-region crop + invert pack + borders | 5 + 3 + 5 + 3 = 16 | Pure array ops |
| RemoveBleedThrough | 50 | Same native call |
| Re-extract grayscale from modified BGR | 30 | Parallel LUT conversion |
| Content grayscale extract + invert | 10 | Sub-region only |
| DT (content) | 98 | Same native call, no extract overhead |
| Pack + despeckle + wires + lines | 5 + 10 + 5 + 5 = 25 | Same native calls |
| AT x3 (PARALLEL!) | max(401, ~350, ~300) ≈ 401 | 3.3x speedup on AT alone! |
| Composite (3x BlockCopy) | 5 | Trivial memcpy |
| Pack + save | 9 + 26 = 35 | Same |
| **TOTAL** | **~797 ms** | **2.4x speedup** |

### Speedup breakdown:
- **AT parallelism**: 1320ms → 401ms = **919ms saved** (biggest win)
- **Eliminated GDI+ overhead**: ~200ms of LockBits/UnlockBits/DeepClone/region copies
- **Eliminated redundant grayscale extraction**: 3 × 30ms = 90ms

---

## Parallelism Opportunities

### Safe to parallelize:
1. **Three AT calls** (steps 21-23): Each operates on its own independent buffer. No shared state. Use `Parallel.Invoke()`. This is the single biggest win (~920ms saved).
2. **Grayscale sub-region extraction** (for AT): Can use `Parallel.For` per region.
3. **PackTo1bpp**: Already parallel internally.

### NOT safe to parallelize:
1. **DT + cleanup sequence** (steps 17-20): Each step depends on the previous output.
2. **Composite sequence** (steps 24-26): Each composite depends on the previous.
3. **RemoveBleedThrough**: Must complete before AT grayscale extraction (it modifies BGR in-place).

### Advanced parallelism (Phase 2 optimization):
- The content pipeline (DT + cleanup) and AT calls could overlap, since they operate on different data. However:
  - They share the BGR buffer (content needs grayscale from modified BGR, AT needs grayscale from modified BGR)
  - RemoveBleedThrough must finish before either starts
  - After RemoveBleedThrough, content extraction and AT extraction could overlap
  - In practice, the simpler `Parallel.Invoke` for the three ATs is the dominant win

---

## Changes Required

### 1. New method in RavenImaging.cs

```csharp
/// <summary>
/// Load a JPEG and return LUT grayscale + raw BGR data in one pass.
/// The BGR array uses GDI+ stride (DWORD-aligned scanlines).
/// </summary>
public static (byte[] grayLut, byte[] bgr, int bgrStride, int width, int height)
    LoadImageAsGrayscaleAndBgr(string path)
```

Implementation: Same as `LoadImageAsGrayscaleDual` but returns `(grayLut, bgr, stride, w, h)` instead of discarding the BGR buffer. No need for `grayAvg` since RefineThreshold is not used for photostat.

### 2. New static helper in RavenImaging.cs (or OpenThresholdBridge.cs)

```csharp
/// <summary>
/// Threshold grayscale array using a global threshold value, producing packed 1bpp.
/// </summary>
public static byte[] ThresholdAndPack1bpp(byte[] gray, int w, int h, int threshold)
```

And:

```csharp
/// <summary>
/// Threshold + invert and pack to 1bpp.
/// Pixel is black (bit=0) if gray[i] > threshold, white (bit=1) otherwise.
/// Then inverted: black↔white. Net: bit=0 if gray[i] <= threshold, bit=1 if gray[i] > threshold...
/// Actually: just swap the comparison.
/// </summary>
public static byte[] ThresholdInvertAndPack1bpp(byte[] gray, int w, int h, int threshold)
```

Wait — let's think about this more carefully:

Original pipeline:
- ImgAutoThreshold(CopyOfImage, 2) → 1bpp where white=pixel>T, black=pixel<=T
  (In GDI+ 1bpp: bit=1 means white, bit=0 means black)
- ImgInvert(CopyOfImage) → flip all bits. Now bit=0 means what was white (pixel>T), bit=1 means what was black (pixel<=T).

For FindBlackBorder with `bpp=1`: a pixel is "black" when bit=0. So after invert:
- "black" = pixel WAS > T (light pixel in original = film background)
- "white" = pixel WAS <= T (dark pixel in original = content)

In the byte-array path for `NativeFindBlackBorder(buf, stride, w, h, 1, ...)`:
- It checks `(buf[...] & mask) != 0` → white pixel
- So bit=1 means white, bit=0 means black

For the first border detection (step 4, no invert): we want the same as AutoThreshold.
For the second border detection (step 7, after invert): we want the inverted result.

Simple approach: build two packed 1bpp arrays:
```csharp
// Normal threshold (for step 4):
// bit=1 (white) if gray > T, bit=0 (black) if gray <= T
byte[] threshNormal = ThresholdAndPack1bpp(gray, w, h, T);

// Inverted threshold (for step 7):
// bit=1 (white) if gray <= T, bit=0 (black) if gray > T
byte[] threshInverted = new byte[threshNormal.Length];
for (int i = 0; i < threshInverted.Length; i++)
    threshInverted[i] = (byte)~threshNormal[i];
```

Actually, the inverted buffer only needs to be computed for the cropped a-region. So we can crop-then-invert.

### 3. New 1bpp sub-region extraction helper

```csharp
/// <summary>
/// Extract a rectangular sub-region from a packed 1bpp buffer.
/// Returns a new packed 1bpp buffer for the sub-region.
/// </summary>
public static byte[] Extract1bppSubRegion(byte[] src, int srcByteWidth,
    int srcW, int srcH, int left, int top, int right, int bottom)
```

This involves bit-level extraction since the sub-region's left edge is generally not byte-aligned. For a simpler approach, we can:
- Extract the corresponding grayscale sub-region
- Re-threshold it
- Pack to 1bpp

This avoids bit manipulation entirely at the cost of redundant threshold computation (but threshold is just a comparison — much cheaper than the border detection it feeds).

### 4. New method in OpenThresholdBridge.cs

```csharp
public static void ApplyThresholdToFilePhotostat(
    string inputJpgPath, string outputTifPath,
    int windowW, int windowH, int contrast, int brightness,
    int despeckle)
```

This is the main orchestrator, ~150 lines.

### 5. Wiring in Main.cs

Add an early return at the top of the photostat block (before step 1):

```csharp
if (NegativeImage == true && SBB == false)
{
    // Fast byte-array path for RDynamic/Dynamic
    if (conversionSettings?.Type == "RDynamic" || conversionSettings?.Type == "Dynamic")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OpenThresholdBridge.ApplyThresholdToFilePhotostat(
            inputJPG, outputTIF, 7, 7, contrast, brightness, despeckle);
        OpenThresholdBridge.WaitForPendingSave();
        sw.Stop();
        _lastThresholdDetail = $"{sw.ElapsedMilliseconds}ms (photostat fast path)";
        // Clean up handles opened before this block
        if (tImageHandle != 0) { RavenImaging.ImgDelete(tImageHandle); tImageHandle = 0; }
        if (TifHandle != 0) { RavenImaging.ImgDelete(TifHandle); TifHandle = 0; }
        return;
    }
    // ... existing handle-based pipeline for other types ...
```

---

## Correctness Verification Strategy

### Pixel-identical output guarantee

The byte-array pipeline must produce the SAME output as the handle-based pipeline when given identical JPEG input. The key risk areas:

1. **Grayscale extraction**: Must use identical LUT weights. `ExtractGrayscaleLut` uses `_lutR[R] + _lutG[G] + _lutB[B]`. Our byte-array path uses the same LUTs. SAFE.

2. **BGR-to-gray after inversion**: Must invert BGR channels BEFORE grayscale computation (not after). I.e., `lutR[255-R] + lutG[255-G] + lutB[255-B]`, NOT `255 - (lutR[R] + lutG[G] + lutB[B])`. CRITICAL.

3. **RemoveBleedThrough**: Same native function, same BGR buffer. SAFE.

4. **AutoThreshold**: Same native function, same grayscale data. SAFE.

5. **FindBlackBorder**: Same native function, same packed 1bpp data. Must verify that our threshold+pack produces identical 1bpp as the handle-based `ImgAutoThreshold` + GDI+ 1bpp Bitmap. The handle-based code uses `grayFlat[i] > T` for white (bit=1). Our pack must match. SAFE if we use the same comparison.

6. **Coordinate propagation**: The coordinates (a, b, c) are computed from FindBlackBorder on identically-prepared 1bpp data, so they'll be identical. The compositing uses the same coordinates. SAFE.

7. **AT on grayscale from modified BGR**: The handle-based pipeline calls `ImgAdaptiveThresholdAverage(tImageHandle)` which internally does `ExtractGrayscaleLut(bmp)` on the 24bpp Bitmap (which was modified by RemoveBleedThrough). Our byte-array pipeline re-derives grayscale from the modified BGR buffer. As long as we use the same LUT conversion (which we do), the grayscale will be identical. SAFE.

### Test plan

1. Run both pipelines on the same 10 test images (Compact, FF_Pho, Pho sets)
2. Compare output TIFs pixel-by-pixel (should be 0 differences)
3. If any differences found, dump intermediate buffers at each step to find the divergence point

---

## Summary of Code Changes

| File | Change | Lines |
|------|--------|-------|
| `RavenImaging.cs` | Add `LoadImageAsGrayscaleAndBgr()` | ~30 |
| `OpenThresholdBridge.cs` | Add `ApplyThresholdToFilePhotostat()` | ~150 |
| `OpenThresholdBridge.cs` | Add helper: `ThresholdAndPack1bpp()` | ~20 |
| `OpenThresholdBridge.cs` | Add helper: `Crop1bppAndInvert()` or equivalent | ~25 |
| `OpenThresholdBridge.cs` | Add helper: `CompositeBytesInto()` | ~10 |
| `Main.cs` | Add early return for photostat fast path | ~15 |
| **Total** | | **~250 lines** |

---

## Risk Mitigation

1. **Keep old pipeline**: The handle-based pipeline remains as fallback for non-RDynamic/Dynamic types. No code is deleted.

2. **Feature flag**: Could add a config flag to force old pipeline for debugging, but the early-return pattern already provides this (just comment it out).

3. **Correctness first**: Implement without parallelism first. Once pixel-identical output is verified, add `Parallel.Invoke` for the AT calls.

4. **Memory safety**: All byte arrays are allocated on the managed heap. No unmanaged memory. The native P/Invoke functions are already battle-tested.
