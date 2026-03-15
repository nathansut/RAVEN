# PLAN: In-Memory Image Enhancements for RAVEN

## Goal
Eliminate unnecessary disk I/O during image processing operations. Currently, many operations save files to disk mid-pipeline, read them back, or shell out to external processes. This plan identifies every such case and describes how to do the work in-memory using C# libraries.

---

## Library Choice: Emgu.CV (OpenCvSharp is close second)

**Recommendation: Stick with Emgu.CV.**

Why:
- Already a project dependency (Emgu.CV 4.9, referenced in RAVEN.csproj)
- Already proven in the codebase (RotateWithSameCanvasSizeEmgu works)
- Provides Mat-based in-memory image manipulation, thresholding, morphology, rotation
- No new NuGet packages needed for initial work
- System.Drawing is too limited (no TIFF writing control, no morphology ops)
- ImageSharp/SkiaSharp would add new dependencies for no gain
- OpenCvSharp is equivalent but would be a swap-out, not an add

For TIFF I/O specifically, keep using **BitMiracle.LibTiff.NET** (already a dependency, already used in SaveAsTiff). It gives you 1-bit CCITT Group 4 compression which Emgu.CV cannot produce natively.

---

## Current Architecture Summary

The app has **three separate image systems** that don't talk to each other:

| System | Purpose | Data Model |
|--------|---------|------------|
| **RecoIP** (recoip.dll) | Image processing (threshold, despeckle, crop, etc.) | Opaque `int` handles. Load from disk, process, save to disk. |
| **USVWin** (usvwin32.dll) | Image display in WinForms panels | File-path-based. `Load_Image(path)` reads from disk. |
| **Emgu.CV** | Fine rotation (deskew) | `Mat` objects loaded via `CvInvoke.Imread(path)` |

The fundamental problem: **all three systems are file-based**. They communicate through the filesystem. To process an image, you save it as a TIF, open it in another system, process it, save it again.

---

## Operations That Hit Disk Unnecessarily

### TIER 1 — High-frequency, user-visible latency (fix first)

#### 1. `whiteout()` — Main.cs:3573
**What it does:** Erases pixels inside or outside a rectangle on the TIF.
**Current flow:**
```
File.Copy(tifimage -> tempfile)        // SaveTemp()
USVWin.EraseIn(tifimage, tempfile, ...)  // reads file, writes file
File.Copy(tempfile -> tifimage)        // overwrite original
```
Three disk writes + two disk reads for a rectangle fill operation.

**Fix:** Use Emgu.CV `Mat` + `Rectangle` + `SetTo(white)` in memory. Or use RecoIP: load handle, use `ImgCropBorder` or pixel manipulation, save once. Best: load with `CvInvoke.Imread`, draw white rect, save with `SaveAsTiff` (already exists).

#### 2. `toggle()` — Main.cs:3663
**What it does:** Inverts pixels in a selected rectangle.
**Current flow:**
```
RecoIP.ImgOpen(tifimage)    // disk read
RecoIP.ImgCopy(...)         // in-memory (good)
RecoIP.ImgInvert(...)       // in-memory (good)
RecoIP.ImgAddCopy(...)      // in-memory (good)
RecogSaveImage(TifHandle, tifimage)  // disk write via temp file
RecoIP.ImgDelete(...)
```
This is actually mostly okay — one read, one write. The `RecogSaveImage` adds an extra temp file dance (write .tmp, delete original, rename). Could be simplified but is not the worst offender.

#### 3. `FineRotate()` — Main.cs:2092
**What it does:** Small-angle rotation of TIF for deskewing.
**Current flow:**
```
RecoIP.ImgOpen(image)           // disk read
RecoIP.ImgCorrectDeformation1(...)  // in-memory
RecoIP.ImgResize(...)           // in-memory
RecogSaveImage(handle, image)   // disk write via temp file
RecoIP.ImgDelete(...)
```
Called on every mouse wheel tick in FineRotation mode. The read-from-disk + write-to-disk on each wheel tick is the bottleneck.

**Fix:** Keep the image handle alive between wheel ticks (like CachedJPG pattern). Only save to disk when the user moves to the next page. Accumulate rotation angle, apply once.

#### 4. `Rotate90()` — Main.cs:2042
**Current flow:** Open from disk, rotate, save to disk. Same pattern as FineRotate. Less frequent (user presses a key) but still disk-bound.

#### 5. `RecogSaveImage()` — Main.cs:2192
**What it does:** Saves a RecoIP image handle to disk using temp-file-then-rename.
**Current flow:**
```
Delete existing .tmp
Save to .tmp via RecoIP.ImgSaveAsTif/ImgSaveAsJpg
Delete original file
File.Move(.tmp -> original)
```
This is called by ~6 different operations. The temp-file pattern is correct for crash safety but adds latency. Not removable directly, but can be avoided if we stop saving after every operation.

### TIER 2 — Batch operations, worth fixing but less urgent

#### 6. `threshold()` — Main.cs:3762 (the main conversion pipeline)
**Current flow for partial-area conversion:**
```
RecoIP.ImgOpen(inputJPG)       // disk read (but cached via CachedJPG)
RecoIP.ImgOpen(outputTIF)      // disk read
[in-memory processing: copy, threshold, despeckle, addcopy]
RecogSaveImage(TifHandle, outputTIF)  // disk write via temp
```
The JPG read is already cached (CachedJPG). The TIF read + write is unavoidable for the final save (it IS the output). This is already reasonably optimized for single operations.

**But for batch operations** (Convert Rest of Book, MultipageModifyAlt_SBB), each image goes through the full open-process-save cycle sequentially. No pipeline parallelism.

**Fix for batch:** Process images in parallel using `Parallel.ForEach` with Emgu.CV (not RecoIP — RecoIP is not thread-safe as noted in the commented-out `AutosetCropbox_Cache`).

#### 7. Batch conversion via ImageProcessor.exe — ThresholdSettings.cs:547, Main.cs:1659
**What it does:** Shells out to Recogniform's external `ImageProcessor.exe` with .ips script files.
**Current flow:**
```
Write .ipb launch file to disk
Write modified .ips script to temp dir
Launch ImageProcessor.exe -auto "launch.ipb"
Wait for process to finish
```
This is the external process invocation that should be completely replaced.

**Fix:** Replace with an in-process loop. The .ips scripts just call `ImgDynamicThresholdAverage` + `ImgDespeckle` (loose) or the full photostat pipeline. We already have all of this logic in C# in `threshold()` and `ConvertPhotostat()`. Just loop through the file list and call `ThresholdMe()` for each — which is exactly what `MultipageModifyAlt_SBB()` already does. The `ImageProcessor.exe` path is dead code waiting to be removed.

#### 8. `crop()` — Main.cs:2832
**Current flow:**
```
RecoIP.ImgOpen(tifimage)       // disk read
RecoIP.ImgCropBorder(...)      // in-memory
RecogSaveImage(TifHandle, tifimage)  // disk write
RecoIP.ImgDelete(...)
```
Same open-process-save pattern. Not terrible for a single operation.

#### 9. `SaveTemp()` + `USVWin.EraseIn/EraseOut` — Main.cs:3629, 3554, 3601
The `SaveTemp` function copies the entire TIF file to a .tmp file just so USVWin can have two different filenames (input and output). USVWin.EraseIn/EraseOut are file-to-file operations — they cannot work in memory.

**Fix:** Replace USVWin.EraseIn/EraseOut entirely with Emgu.CV rectangle operations. This eliminates SaveTemp, the file copy, and the USVWin dependency for these operations.

### TIER 3 — Infrastructure improvements

#### 10. `CreateCheckpoint()` — Main.cs:4124
**What it does:** Copies the TIF file to a temp directory as an undo checkpoint. Up to 40 checkpoints.
**Current flow:** `File.Copy(tifImage, checkpointPath)` — a full file copy for every enhancement operation.

**Fix (later):** Keep checkpoints as in-memory byte arrays or Emgu.CV Mat objects. For 1-bit CCITT TIFs, a typical page is 50-200KB. 40 checkpoints = 2-8MB. Trivial for RAM. But this is a bigger refactor since it changes the undo architecture.

#### 11. `KeyPicture.Image` setter — KeyPicture.Common.cs:167
**What it does:** When you set the byte array, it writes to a file and then calls `Load_Image(file)`.
**Why:** USVWin can only display from files, not from memory.
**Fix (later):** This is a fundamental USVWin limitation. Would require replacing USVWin with a PictureBox/custom control that can display from a Bitmap object. Big change, do last.

#### 12. `deskewrecog()` — Main.cs:~2290
**Current flow:**
```
RecoIP.ImgOpen(tif) -> get deskew angle -> ImgDelete
RotateWithSameCanvasSizeEmgu(tif, angle, tempTif)  // reads from disk, writes to disk
RotateWithSameCanvasSizeEmgu(jpg, angle, tempJpg)  // reads from disk, writes to disk
```
Two full disk-to-disk rotations.

**Fix:** If we keep Emgu.CV for rotation, at least chain the operations: load once, rotate, save once. Don't save intermediate files.

---

## Bridging RecoIP Handles and Byte Arrays

This is the key technical challenge, already analyzed in INTEGRATION_PLAN_ImgDynamicThreshold.md. Summary:

### Option A: ImgGetDIBHandle + Marshal (Best)
```csharp
int dibHandle = RecoIP.ImgGetDIBHandle(imageHandle);
IntPtr dibPtr = new IntPtr(dibHandle);
// Read BITMAPINFOHEADER (40 bytes) to get width, height, stride, bpp
// Marshal.Copy pixel data from dibPtr + headerSize into byte[]
// Process byte[]
// Create new DIB, write result, call ImgSetDIBHandle
```
Zero disk I/O. Needs testing to confirm DIB handle is a real memory pointer.

### Option B: Save to temp BMP, load with Emgu.CV (Fallback)
```csharp
string tmp = Path.GetTempFileName() + ".bmp";
RecoIP.ImgSaveAsBmp(imageHandle, tmp);
Mat mat = CvInvoke.Imread(tmp, ImreadModes.Grayscale);
byte[] pixels = new byte[mat.Width * mat.Height];
Marshal.Copy(mat.DataPointer, pixels, 0, pixels.Length);
// Process pixels
// Write back: create Mat from result, save, reload into RecoIP
```
One temp file but avoids the per-pixel P/Invoke of ImgGetPixel.

### Option C: Direct byte[] via DynamicThreshold pattern
For operations we reimplement ourselves (like DynamicThreshold), we can bypass RecoIP entirely:
```csharp
Mat mat = CvInvoke.Imread(jpgPath, ImreadModes.Grayscale);
byte[] gray = new byte[mat.Width * mat.Height];
Marshal.Copy(mat.DataPointer, gray, 0, gray.Length);
byte[] binary = DynamicThreshold.Apply(gray, mat.Width, mat.Height, 7, 7, contrast, brightness);
// Write binary result as 1-bit TIFF using SaveAsTiff (already exists in Main.cs)
```
No RecoIP needed at all for the threshold step.

---

## Specific Refactoring Plan (Priority Order)

### Phase 1: Quick Wins (no architectural changes)

**1a. Replace `USVWin.EraseIn/EraseOut` with Emgu.CV**
- Files: Main.cs (`whiteout()` at line 3573, bulk whiteout at line 3554)
- Change: Load TIF with `CvInvoke.Imread`, use `Mat.SetTo(white, mask)` or `CvInvoke.Rectangle`, save with existing `SaveAsTiff`
- Eliminates: `SaveTemp()`, `File.Copy` round-trip, USVWin dependency for erase ops
- Risk: Low. Pure replacement of file-based erase with in-memory erase.

**1b. Replace `ImageProcessor.exe` invocation with in-process loop**
- Files: ThresholdSettings.cs (lines 481-556), Main.cs (lines 1544-1670 MultipageModifyAlt_Dynamic)
- Change: Instead of writing .ipb/.ips files and launching ImageProcessor.exe, loop through file list and call `ThresholdMe()` per image (exactly like MultipageModifyAlt_SBB already does)
- Eliminates: External process, temp script files, temp launch files
- Risk: Low. The SBB path already does this correctly.

**1c. Accumulate FineRotate instead of save-per-tick**
- Files: Main.cs (`FineRotate()` at line 2092, mouse wheel handler at line 620)
- Change: Track accumulated angle in a variable. On each wheel tick, just update the angle and display a preview. Only apply rotation + save to disk when user presses a key or navigates away.
- Eliminates: Disk read + write on every mouse wheel tick
- Risk: Medium. Need to handle the "what if they navigate away without confirming" case.

### Phase 2: DynamicThreshold Integration (already planned)

**2a. Add DynamicThreshold.cs to project**
- Copy from `/home/ns/OpenThreshold_retrieved/DynamicThreshold.cs`
- Change namespace to match project

**2b. Create OpenThresholdBridge.cs**
- Bridge between RecoIP handles and byte arrays (Option A or B from above)
- Drop-in replacement for `RecoIP.ImgDynamicThresholdAverage`

**2c. Replace 4 call sites**
- Main.cs: lines 3887, 3939, 4046
- ThresholdSettings.cs: line 663

### Phase 3: Deeper In-Memory Pipeline

**3a. Replace `RecogSaveImage` pattern with deferred saves**
- Instead of saving after every operation, keep the RecoIP handle alive
- Only save when navigating to a different page or closing
- This is similar to the existing `CachedJPG` / `CachedTIF` pattern but extended

**3b. In-memory checkpoints**
- Replace `File.Copy` checkpoint system with in-memory byte array ring buffer
- 1-bit TIFs are small enough (50-200KB) that 40 checkpoints fit in ~8MB RAM

**3c. Parallel batch conversion with Emgu.CV**
- For "Convert Rest of Book", process images in parallel
- Use Emgu.CV + DynamicThreshold (both thread-safe) instead of RecoIP (not thread-safe)
- Each thread: imread -> grayscale -> threshold -> despeckle -> save

### Phase 4: Long-term (optional)

**4a. Replace USVWin display with native WinForms**
- Use PictureBox or custom double-buffered Panel with System.Drawing.Bitmap
- Load from byte[] instead of file path
- This eliminates the biggest remaining disk dependency but is a large refactor

**4b. Drop RecoIP for all remaining operations**
- Reimplement despeckle, line removal, border detection, invert, crop using Emgu.CV
- The DynamicThreshold reimplementation proves the pattern works

---

## Operations Inventory: What Uses Disk and What Doesn't

| Operation | Current Method | Disk I/O | In-Memory Alternative |
|-----------|---------------|----------|----------------------|
| Dynamic threshold (single) | RecoIP.ImgDynamicThresholdAverage | Open + Save | DynamicThreshold.Apply (Phase 2) |
| Dynamic threshold (batch) | ImageProcessor.exe | External process | In-process loop (Phase 1b) |
| Whiteout (EraseIn/EraseOut) | USVWin.EraseIn/EraseOut | File -> File | Emgu.CV Mat.SetTo (Phase 1a) |
| Toggle (invert region) | RecoIP open/invert/save | Open + Save | Keep RecoIP, defer save (Phase 3a) |
| Crop | RecoIP open/crop/save | Open + Save | Keep RecoIP, defer save (Phase 3a) |
| Rotate 90 | RecoIP open/rotate/save | Open + Save | Keep RecoIP, defer save (Phase 3a) |
| Fine rotate | RecoIP open/deform/save per tick | Open + Save per tick | Accumulate angle (Phase 1c) |
| Deskew | RecoIP angle + Emgu.CV rotate | 2x disk read/write | Chain operations (Phase 3) |
| Despeckle | RecoIP.ImgDespeckle | In-memory (via handle) | Keep as-is |
| Checkpoint/undo | File.Copy to temp dir | Full file copy | In-memory ring buffer (Phase 3b) |
| Display image | USVWin.Load_Image(path) | Disk read | Replace USVWin (Phase 4a) |
| Autoset cropbox | RecoIP open/threshold/border detect | Open + close (no save) | Keep as-is |

---

## Files That Will Be Modified

| File | Phase | Changes |
|------|-------|---------|
| Main.cs | 1a | Replace `whiteout()` EraseIn/EraseOut with Emgu.CV |
| Main.cs | 1c | Add angle accumulation to FineRotate |
| Main.cs | 2c | Replace 3 ImgDynamicThresholdAverage calls |
| Main.cs | 3a | Add deferred-save logic |
| ThresholdSettings.cs | 1b | Replace ImageProcessor.exe with in-process loop |
| ThresholdSettings.cs | 2c | Replace 1 ImgDynamicThresholdAverage call |
| RAVEN.csproj | 2a | No new packages needed (Emgu.CV already present) |

## New Files

| File | Phase | Purpose |
|------|-------|---------|
| DynamicThreshold.cs | 2a | Open-source threshold algorithm |
| OpenThresholdBridge.cs | 2b | Bridge RecoIP handles to byte arrays |

---

## Key Constraint Reminders

- **x86 (32-bit)**: recoip.dll is 32-bit. Emgu.CV must be x86 too. Memory limited to ~1.5GB usable.
- **RecoIP is not thread-safe**: The commented-out `AutosetCropbox_Cache` confirms this. Don't parallelize RecoIP calls. Parallelize only with Emgu.CV / DynamicThreshold.
- **USVWin is file-based**: Cannot display from memory. Until Phase 4, the final "display" step always hits disk.
- **Keep RecogSaveImage temp-file pattern**: The write-tmp-then-rename approach is correct for crash safety. Don't remove it; just call it less often.
