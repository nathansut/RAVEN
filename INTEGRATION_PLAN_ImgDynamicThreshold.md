# Integration Plan: ImgDynamicThreshold into RAVEN

## 1. What DynamicThreshold.cs Does (Algorithm Summary)

The `DynamicThreshold.cs` file (retrieved from `ns1-wsl:/mnt/c/temp/OpenThreshold/OpenThreshold/DynamicThreshold.cs`) is an exact open-source reimplementation of Recogniform's proprietary `ImgDynamicThresholdAverage` function from `recoip.dll`. It was reverse-engineered via Ghidra decompilation and verified at 99.9998% match (0 interior pixel mismatches).

### Algorithm (per-pixel two-stage binarization):

1. **Compute integral image** -- a 2D prefix sum over the grayscale pixel values, enabling O(1) window-sum lookups.
2. **Compute half-widths** from window parameters: `hw = (w + 1) >> 1`, `hh = (h + 1) >> 1`. So w=7 yields hw=4, making the actual window 9x9 pixels.
3. **Invert the contrast/brightness parameters**: `effContrast = 255 - contrast`, `effThreshold = 255 - brightness`. This is a key Recogniform quirk -- higher "contrast" parameter means *less* effective contrast threshold.
4. **For each pixel**, using clamped window bounds (variable divisor at borders, rounding division):
   - Compute local mean from integral image
   - If `|mean - pixel| > effContrast`: **local binarization** -- pixel >= mean ? WHITE : BLACK
   - Else: **global threshold** -- pixel > effThreshold ? WHITE : BLACK (strict `>`)

### Parameters (as used in RAVEN):
- `WindowWidth=7, WindowHeight=7` (always 7x7 in RAVEN, producing 9x9 actual window)
- `Contrast` (typically 248-251): higher = less local sensitivity
- `Brightness` (typically 180-220): higher = lighter global threshold

### Performance:
- Recogniform DLL (32-bit, sequential): ~365ms per 19MP image
- OpenThreshold CPU (Parallel.For): ~35ms per 19MP image (10x faster)
- OpenThreshold CPU 8-thread pipeline: ~70ms wall time per image
- OpenThreshold CUDA GPU (RTX 5090): ~7ms per image (52x faster)

---

## 2. Current RAVEN Architecture

### How RecoIP Works Today
RAVEN uses Recogniform's `recoip.dll` (32-bit native DLL) via P/Invoke. The DLL manages images through opaque integer **image handles**. The workflow is:

```
ImgOpen(file) -> handle
ImgDynamicThresholdAverage(handle, 7, 7, contrast, brightness)  // modifies in-place
ImgSaveAsTif(handle, file)
ImgDelete(handle)
```

The image handle system is entirely opaque -- RAVEN never touches raw pixel data for thresholding. It loads a JPEG/TIF, passes the handle to various RecoIP functions, and saves the result.

### Where DynamicThreshold Is Called in RAVEN
Three call sites in `Main.cs`, one in `ThresholdSettings.cs`:

1. **Main.cs:3887** -- Full page photostat conversion (negative image inversion + threshold)
2. **Main.cs:3939** -- Full page non-photostat, non-SBB conversion (the standard path)
3. **Main.cs:4046** -- Partial area (selected region) conversion
4. **ThresholdSettings.cs:663** -- Photostat conversion via ThresholdSettings dialog

All four use the same signature: `RecoIP.ImgDynamicThresholdAverage(handle, 7, 7, contrast, brightness)`

### Key Dependencies in the Threshold Pipeline
Before `ImgDynamicThresholdAverage` is called, the image may have been:
- Loaded via `ImgOpen` (JPEG or TIF)
- Copied/cropped via `ImgCopy` or `ImgDuplicate`
- Inverted via `ImgInvert` (for photostats)
- Converted to grayscale via `ImgConvertToGrayScale` (for color images, before refine threshold)

After thresholding, the image typically goes through:
- `ImgDespeckle` (noise removal)
- `ImgRemoveBlackWires` / `ImgRemoveVerticalLines` (line removal for photostats)
- `ImgRefineThreshold` (optional second-pass refinement using original grayscale)
- `ImgAddCopy` (compositing partial results back into full image)
- `ImgSaveAsTif` (save as 1-bit TIFF)

### Project Configuration
- .NET 6.0 Windows Forms, x86 platform target
- Dependencies: Emgu.CV 4.9, BitMiracle.LibTiff.NET, ImageMagick, ini-parser
- `AllowUnsafeBlocks` is enabled

---

## 3. Integration Plan

### 3.1 Approach: Wrapper Method, Not Full Replacement

The cleanest integration is to add `DynamicThreshold.cs` as a new file in the RAVEN project and create a **wrapper method** that bridges RAVEN's image-handle world with OpenThreshold's raw-byte-array world. This avoids modifying the existing `Main.cs` call pattern beyond changing which function is called.

The wrapper will:
1. Extract grayscale pixel data from a RecoIP image handle
2. Call `DynamicThreshold.Apply()`
3. Write the binary result back into the image handle

### 3.2 Files to Create

#### A. `DynamicThreshold.cs` (NEW)
Copy the retrieved file into the RAVEN project root (alongside Main.cs, RecoIPAPI.cs, etc.).
- Change namespace from `OpenThreshold` to `RAVEN`
- Keep the algorithm identical
- This is a pure static class with no external dependencies

#### B. `OpenThresholdBridge.cs` (NEW)
A bridge class that adapts between RecoIP's image-handle system and DynamicThreshold's byte-array API.

```csharp
namespace RAVEN;

/// <summary>
/// Bridges RecoIP image handles with OpenThreshold's DynamicThreshold.
/// Provides drop-in replacement for RecoIP.ImgDynamicThresholdAverage.
/// </summary>
public static class OpenThresholdBridge
{
    /// <summary>
    /// Drop-in replacement for RecoIP.ImgDynamicThresholdAverage.
    /// Extracts pixels from image handle, applies threshold, writes back.
    /// </summary>
    public static void ImgDynamicThresholdAverage(
        int imageHandle, int windowWidth, int windowHeight,
        int contrast, int brightness)
    {
        // 1. Get image dimensions
        int width = RecoIP.ImgGetWidth(imageHandle);
        int height = RecoIP.ImgGetHeight(imageHandle);
        int bpp = RecoIP.ImgGetBitsPixel(imageHandle);

        // 2. Extract grayscale pixel data from RecoIP handle
        byte[] gray = ExtractGrayscale(imageHandle, width, height, bpp);

        // 3. Apply the open-source threshold
        byte[] binary = DynamicThreshold.Apply(
            gray, width, height,
            windowWidth, windowHeight,
            contrast, brightness);

        // 4. Write binary result back into the image handle
        WriteBinaryToHandle(imageHandle, binary, width, height);
    }

    // Implementation details for pixel extraction/injection
    // depend on which RecoIP functions are available (see section 3.4)
}
```

### 3.3 Files to Modify

#### A. `Main.cs` -- 3 call sites
Replace:
```csharp
RecoIP.ImgDynamicThresholdAverage(handle, 7, 7, contrast, brightness);
```
With:
```csharp
OpenThresholdBridge.ImgDynamicThresholdAverage(handle, 7, 7, contrast, brightness);
```

At lines: 3887, 3939, 4046

#### B. `ThresholdSettings.cs` -- 1 call site
Same replacement at line 663.

#### C. `ThresholdSettings.cs` -- Script regex (lines 501-502)
The regex that modifies `.ips` script files for batch Recogniform processing will need consideration. If batch processing still uses Recogniform's ImageProcessor.exe externally, those scripts should remain unchanged. If batch processing is being moved to use the open-source implementation too, the script generation needs updating.

#### D. `RAVEN.csproj` -- Minimal changes
- No new NuGet packages needed (DynamicThreshold.cs is pure C# with only `System.Threading.Tasks.Parallel`)
- May need to ensure the new .cs files are included in compilation (they should be auto-included by the SDK-style project)

### 3.4 The Key Challenge: Pixel Data Extraction

The critical engineering challenge is bridging RecoIP's opaque image handles with raw byte arrays. Several approaches, in order of preference:

#### Option A: Use ImgGetDIBHandle + Marshal (PREFERRED)
RecoIP exposes `ImgGetDIBHandle(int ImageHandle) -> int` which returns a Windows DIB (Device Independent Bitmap) handle. A DIB is a standard Windows memory structure:
- BITMAPINFOHEADER (40 bytes) followed by pixel data
- Can be read via `Marshal.Copy` from the DIB pointer

```csharp
// Pseudocode
int dibHandle = RecoIP.ImgGetDIBHandle(imageHandle);
IntPtr dibPtr = new IntPtr(dibHandle);
// Read BITMAPINFOHEADER to get stride, bpp, etc.
// Marshal.Copy pixel data to managed byte[]
// After threshold: create new DIB, write pixels, call ImgSetDIBHandle
```

The reverse path uses `ImgSetDIBHandle` to replace the image data.

This is the most efficient approach -- no file I/O, no copies beyond the marshaling.

#### Option B: Save to temp BMP, process, reload
```csharp
string tempIn = Path.GetTempFileName() + ".bmp";
string tempOut = Path.GetTempFileName() + ".bmp";
RecoIP.ImgSaveAsBmp(imageHandle, tempIn);
// Read BMP, extract grayscale, threshold, save as 1-bit BMP
// RecoIP.ImgOpen(tempOut) to get new handle
// Transfer to original handle or replace
```

This is simpler but slower (disk I/O) and messier (temp file cleanup). Use as fallback.

#### Option C: Use ImgGetPixel per pixel
RecoIP has `ImgGetPixel(handle, x, y)` but calling it per-pixel on a 19MP image would be extremely slow (millions of P/Invoke calls). Not viable.

#### Option D: Use Emgu.CV as intermediary
Since RAVEN already depends on Emgu.CV, we could:
1. Save image handle to temp file
2. Load with `CvInvoke.Imread`
3. Extract `Mat.Data` as byte array
4. Threshold
5. Create new `Mat`, save, reload into RecoIP

More complex but avoids DIB manipulation.

### 3.5 Configuration (Optional Enhancement)

Add a setting to `settings.ini` to toggle between Recogniform and OpenThreshold:

```ini
[Special]
UseOpenThreshold = Y
```

This allows easy A/B comparison and fallback. Read it in `Form1`'s initialization and route calls accordingly.

### 3.6 Batch Processing Consideration

The `ThresholdSettings.cs` batch conversion (lines 486-556) launches Recogniform's `ImageProcessor.exe` as an external process with `.ips` script files. These scripts contain `ImgDynamicThresholdAverage` commands interpreted by Recogniform's engine.

For the open-source replacement to apply to batch processing too, one of these approaches is needed:
1. **Replace ImageProcessor.exe invocation** with an in-process loop that calls `OpenThresholdBridge` for each image (recommended -- also eliminates the external dependency)
2. **Keep ImageProcessor.exe** for batch operations (if Recogniform license is still available) and only use OpenThreshold for interactive single-image operations
3. **Write a custom batch runner** (separate project or within RAVEN) that processes the file list from the `.ipb` launch file

### 3.7 Target Framework Consideration

- `DynamicThreshold.cs` uses `Parallel.For` which is available in .NET 6.0 (RAVEN's current target)
- The OpenThreshold project targets .NET 8.0, but nothing in `DynamicThreshold.cs` requires .NET 8+ features
- No changes to `TargetFramework` needed

### 3.8 Platform Consideration

RAVEN targets x86 (32-bit) because `recoip.dll` is 32-bit. `DynamicThreshold.cs` is pure managed code and works on any platform. However, if the goal is to eventually drop `recoip.dll` entirely and move to x64, this is a step in that direction.

---

## 4. Implementation Steps (Ordered)

1. **Add `DynamicThreshold.cs`** to RAVEN project, change namespace to `RAVEN`
2. **Create `OpenThresholdBridge.cs`** with the handle-to-byte-array bridge
3. **Implement pixel extraction** via `ImgGetDIBHandle` + `Marshal` (Option A)
4. **Test on a single image** -- compare output of `RecoIP.ImgDynamicThresholdAverage` vs `OpenThresholdBridge.ImgDynamicThresholdAverage` pixel-by-pixel
5. **Replace the 4 call sites** in Main.cs and ThresholdSettings.cs
6. **Add INI toggle** (`UseOpenThreshold = Y/N`) for easy rollback
7. **Test batch conversion** -- determine if ImageProcessor.exe invocation needs changes
8. **Performance benchmark** -- compare Recogniform vs OpenThreshold in the RAVEN workflow

---

## 5. Related Files in OpenThreshold (for future reference)

The full OpenThreshold project on ns1-wsl has additional capabilities not needed for initial integration but useful for future work:

| File | Purpose | Integration Priority |
|------|---------|---------------------|
| `DynamicThreshold.cs` | CPU implementation (THIS PLAN) | **Immediate** |
| `Pipeline.cs` | Multi-threaded batch processing | Phase 2 (batch conversion) |
| `TurboJpegDecoder.cs` | Fast JPEG decode via libjpeg-turbo | Phase 2 (if replacing ImgOpen) |
| `DynamicThresholdGpu.cs` | GPU via ILGPU (broken on Blackwell) | Future (AMD support) |
| `DynamicThresholdCuda.cs` | GPU via raw CUDA P/Invoke | Future (NVIDIA acceleration) |
| `DynamicThresholdCudaPipeline.cs` | Full GPU pipeline with nvJPEG | Future (full GPU path) |

---

## 6. Risk Assessment

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| DIB handle manipulation incorrect | Medium | Test thoroughly; fall back to Option B (temp files) |
| Subtle pixel differences vs Recogniform | Low | Already verified 99.9998% match; border pixels only |
| 32-bit memory pressure (integral image is 8 bytes * (W+1) * (H+1)) | Low | 19MP image = ~152MB integral. Tight for 32-bit but feasible. |
| Batch processing incompatibility | Medium | Keep Recogniform for batch initially; migrate later |
| Performance regression | Very Low | OpenThreshold is 10x faster than Recogniform on CPU |

---

## 7. File Locations Summary

- Retrieved file saved at: `/home/ns/OpenThreshold_retrieved/DynamicThreshold.cs`
- RAVEN repository: `/home/ns/IET/`
- Key RAVEN files to modify:
  - `/home/ns/IET/Main.cs` (lines 3887, 3939, 4046)
  - `/home/ns/IET/ThresholdSettings.cs` (line 663)
- Key RAVEN files for reference:
  - `/home/ns/IET/RecoIPAPI.cs` (P/Invoke declarations, lines 444-463)
  - `/home/ns/IET/RAVEN.csproj` (project configuration)
  - `/home/ns/IET/settings.ini` (runtime configuration)
- OpenThreshold source on ns1-wsl: `/mnt/c/temp/OpenThreshold/OpenThreshold/` (via Windows SSH: `100.97.152.53`)
