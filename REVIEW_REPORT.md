# Review Report: Agents #1, #2, #3

Reviewed 2026-03-07 by Agent #4.

---

## 1. Agent #1 -- Rebrand (IET -> RAVEN)

### Issues Found

**[LOW] "Imaging_cs_DEMO" temp folder name not renamed**
`Main.cs` lines 4128, 4185, 4220, 4273 all use `Path.Combine(Path.GetTempPath(), "Imaging_cs_DEMO")` as the checkpoint directory. This is a runtime folder name, not a build-breaking issue, but it leaks the old project name to users' temp directories. Should be renamed to `"RAVEN"` or similar.

**[LOW] RAVEN.csproj references missing PFX file**
Line 38: `<ManifestKeyFile>Imaging_cs_DEMO_TemporaryKey.pfx</ManifestKeyFile>`. The file does not exist in the repo. This won't break the build because `<SignManifests>false</SignManifests>` (line 46) disables signing. But it's a stale reference that should be removed or renamed.

**[LOW] Clipbrd.cs and callback.cs still use old namespaces**
`Clipbrd.cs` uses `namespace ImagingDemo`, `callback.cs` references `ImagingDemo.RecoIO`. Both are excluded from compilation via `<Compile Remove>` in the csproj, so this is harmless. No action needed unless these files are ever re-included.

**[MEDIUM] SLN Debug config maps to "Any CPU" instead of x86**
`RAVEN.sln` line 13-14: `Debug|x86.ActiveCfg = Debug|Any CPU`. The Release config correctly maps to x86. When debugging, the app may build as AnyCPU, which on a 64-bit OS will run as 64-bit and **fail to load `recoip.dll` (32-bit)**. This is likely a pre-existing issue, not introduced by the rebrand, but it should be fixed:
```
{D94584A9-69E7-4F1B-95A4-D95A10386691}.Debug|x86.ActiveCfg = Debug|x86
{D94584A9-69E7-4F1B-95A4-D95A10386691}.Debug|x86.Build.0 = Debug|x86
```
The csproj also has `Debug|AnyCPU` and `Release|AnyCPU` property groups (lines 49-53) that do nothing useful since the sln only defines x86 configs. These can be removed for cleanliness.

### Rebrand Completeness Verdict

The rebrand is **functionally complete**. All `.cs` files that are compiled use `namespace RAVEN`. The `.sln` and `.csproj` reference `RAVEN` everywhere that matters. Version is correctly set to `0.0`. No remaining "IET" string found in any source file. The items above are cosmetic or pre-existing.

---

## 2. Agent #2 -- DynamicThreshold Integration Plan

### Algorithm Understanding: Correct

The plan accurately describes the two-stage algorithm (local mean vs global threshold), the parameter inversion quirk, and the integral image approach. Verified against the actual `DynamicThreshold.cs` source.

### Line Numbers: Verified Correct

- Main.cs: 3887, 3939, 4046 -- all confirmed as `RecoIP.ImgDynamicThresholdAverage` calls
- ThresholdSettings.cs: 663 -- confirmed
- ThresholdSettings.cs: 501-502 -- confirmed (regex for .ips script modification)
- RecoIPAPI.cs: 57-58, 63-64 -- `ImgGetDIBHandle` / `ImgSetDIBHandle` confirmed

### DIB Bridge Approach (Option A): Feasible With Caveats

**[MEDIUM] `ImgGetDIBHandle` returns `int`, not `IntPtr`**
The P/Invoke declaration returns `int`. On x86 (32-bit), this works fine because pointers are 32-bit. But the plan should document that this approach is x86-only. If the project ever moves to x64 (which the plan mentions as a long-term goal), the P/Invoke signature would need to change to `IntPtr`. Not a blocker for now.

**[MEDIUM] DIB handle may be a GDI HBITMAP, not a raw memory pointer**
The plan assumes `ImgGetDIBHandle` returns a pointer you can `Marshal.Copy` from directly. But Windows DIB handles from GDI can be HBITMAP handles that require `GlobalLock()` to get the actual memory pointer, or they could be pointers to BITMAPINFO + pixel data. This needs testing. The plan correctly identifies "needs testing" as a risk but underestimates the complexity.

Simpler fallback: **Option B (temp BMP file) is fine for v1**. The threshold operation itself takes ~35ms. Adding a BMP write + read (~10ms for a 19MP image on SSD) is negligible. Don't over-engineer the bridge on the first pass. Get it working with temp files, optimize later if needed.

**[LOW] Missing detail: writing binary result back**
`DynamicThreshold.Apply()` returns a `byte[]` of 0/255 values (8-bit). But `ImgDynamicThresholdAverage` modifies the image handle in-place and produces a 1-bit image. The bridge needs to either:
1. Create a new 1-bit RecoIP image via `ImgCreate(width, height, 1, resolution)` and set pixels, or
2. Keep the result as 8-bit and let downstream operations handle it, or
3. Use the temp BMP approach where you can control the output format.

This is solvable but the plan should be explicit about it.

### Simplification Suggestion

**Skip the bridge entirely for v1.** Instead of bridging RecoIP handles to byte arrays, do this:
1. Load the JPEG with Emgu.CV (`CvInvoke.Imread`)
2. Get the grayscale byte array from the Mat
3. Call `DynamicThreshold.Apply()`
4. Save the result as a 1-bit TIFF using the existing `SaveAsTiff` helper in Main.cs
5. Load the TIFF into a RecoIP handle for downstream operations (despeckle, etc.)

This avoids DIB manipulation entirely. The cost is one temp file write, which is the same as Option B but simpler because you don't need to convert between RecoIP handles and byte arrays at all. The JPEG is already on disk (or cached in `CachedJPG`).

### Edge Cases

**[MEDIUM] 32-bit memory for integral image**
The plan notes this but may underestimate it. The integral image uses `long[]` (8 bytes each). For a 19MP image (e.g., 4400x4400): `(4401 * 4401) * 8 = ~155MB`. The input gray array is another ~19MB, output another ~19MB. That's ~193MB just for the threshold operation. In a 32-bit process with ~1.5GB usable, this is tight if other images are loaded simultaneously. `Parallel.For` also adds thread stack overhead.

Mitigation: This is still better than Recogniform's DLL (which presumably has similar memory needs). But worth noting that very large images (e.g., 30MP) could OOM in 32-bit.

---

## 3. Agent #3 -- In-Memory Enhancements Plan

### Disk-Heavy Operations: Spot-Checked and Accurate

Verified against actual code:
- **whiteout() at 3573**: Confirmed -- uses `SaveTemp` + `USVWin.EraseIn/EraseOut` + `File.Copy`. Accurately described.
- **toggle() at 3663**: Confirmed -- opens from disk, processes in-memory, saves via `RecogSaveImage`. Plan correctly notes this is "mostly okay."
- **FineRotate() at 2092**: Confirmed -- `ImgOpen` + `ImgCorrectDeformation1` + `ImgResize` + `RecogSaveImage` + `ImgDelete` on every wheel tick. This is indeed the worst offender for user-visible latency.
- **RecogSaveImage() at 2192**: Confirmed -- temp-file-then-rename pattern. Plan correctly says to keep this pattern but call it less.
- **CreateCheckpoint() at 4124**: Confirmed -- `File.Copy` to temp dir.
- **ImageProcessor.exe at Main.cs:1659 and ThresholdSettings.cs:547**: Confirmed -- shells out to external process.

### Library Choice (Emgu.CV): Correct

Already a dependency (version 4.9 in csproj). Already used for rotation (`RotateWithSameCanvasSizeEmgu` at line 2357). No reason to add another library.

### Simplification Suggestions

**[LOW] Phase 1c (FineRotate accumulation) is more complex than described**
The plan says "track accumulated angle, apply on navigate away." But the current code adds a column of white pixels on every rotation (`ImgResize` width+1), which is a physical resize, not just a rotation angle. This means you can't simply accumulate angles -- the image dimensions change each time. Need to either: (a) redesign FineRotate to use Emgu.CV `WarpAffine` which doesn't change dimensions, or (b) keep the current approach but cache the RecoIP handle between ticks instead of saving/reloading.

Option (b) is simpler: keep the handle alive in a class field, only `RecogSaveImage` when the user navigates to a new page. This is essentially what `CachedJPG` already does.

**[LOW] Phase 3b (in-memory checkpoints) underestimates complexity**
The plan says "40 checkpoints = 2-8MB." This is correct for compressed 1-bit TIFs. But if stored as uncompressed bitmaps in memory, a 4400x6000 1-bit image is ~3.3MB uncompressed. 40 of those is ~132MB. Still fine for RAM, but the plan should specify storing them as compressed byte arrays (just read the file bytes into a `byte[]` rather than decompressing into a bitmap).

### Phasing/Priority: Sensible

The tier ordering (user-visible latency first, batch ops second, infrastructure third) is correct. Phase 1a (EraseIn/EraseOut replacement) is a clean, isolated win. Phase 1b (ImageProcessor.exe replacement) has the most risk but the most payoff.

---

## 4. Cross-Cutting Concerns

### Conflicts Between Plans

**[MEDIUM] DynamicThreshold plan and In-Memory plan overlap on the bridge**
Both plans discuss the `ImgGetDIBHandle + Marshal.Copy` approach. Agent #2 needs the bridge for DynamicThreshold. Agent #3 needs it for broader in-memory operations. They should share the same bridge code, not build two separate implementations. The In-Memory plan (Agent #3) correctly references the DynamicThreshold plan as "already analyzed" and proposes the same bridge, so there's no actual conflict -- just make sure the bridge is built generically enough to serve both purposes.

**[LOW] Both plans touch ThresholdSettings.cs batch conversion**
Agent #2 mentions the ImageProcessor.exe batch path needs consideration. Agent #3 (Phase 1b) explicitly plans to replace it. These are complementary, not conflicting, but whoever does Phase 1b should coordinate with the DynamicThreshold integration.

### No Conflicts Found

The three plans are well-separated:
- Agent #1 (rebrand) is done and doesn't affect the other two
- Agent #2 (DynamicThreshold) adds new files and modifies 4 call sites
- Agent #3 (in-memory) modifies different code paths (whiteout, FineRotate, batch) except for the shared batch conversion area

### Shared Prerequisites

1. **Fix the SLN Debug config** (Agent #1 leftover) -- do this first so debugging works correctly for all subsequent work
2. **Build and test the current state** -- confirm the RAVEN rebrand builds and runs before making any changes
3. **Decide on the DIB bridge vs temp-file approach** -- this affects both Agent #2 and Agent #3's plans

---

## 5. Recommended Execution Order

1. **Fix SLN Debug config** (5 min) -- prevents debugging headaches
2. **Clean up "Imaging_cs_DEMO" references** (10 min) -- while touching Main.cs anyway
3. **Agent #2: Add DynamicThreshold.cs + simple bridge using temp BMP** (1-2 hours)
   - Start with Option B (temp file). It's simpler, guaranteed to work, and the perf difference is negligible.
   - Replace the 4 call sites
   - Add INI toggle for A/B testing
4. **Agent #3 Phase 1a: Replace USVWin.EraseIn/EraseOut** (1-2 hours)
   - Independent of DynamicThreshold work
5. **Agent #3 Phase 1b: Replace ImageProcessor.exe** (2-3 hours)
   - Depends on step 3 (DynamicThreshold must be integrated first so the in-process loop can use it)
6. **Agent #3 Phase 1c: FineRotate caching** (1-2 hours)
   - Use the simpler "keep handle alive" approach, not angle accumulation
7. **Agent #2 optimization: Upgrade bridge to DIB handle** (optional, if perf matters)
   - Only after everything else works
8. **Agent #3 Phases 2-4**: Later, as needed

---

## 6. Gotchas to Watch For

1. **recoip.dll thread safety**: The code has a commented-out parallel attempt (`AutosetCropbox_Cache`) that confirms RecoIP is not thread-safe. Never call RecoIP from multiple threads.

2. **x86 memory pressure**: Keep an eye on memory during testing with large images. The integral image in DynamicThreshold alone uses ~155MB for a 19MP image. Combined with RecoIP's internal allocations, multiple image handles, and Emgu.CV Mats, you could hit the ~1.5GB limit.

3. **TIFF format mismatch**: DynamicThreshold outputs 8-bit (0/255 values). Downstream operations like `ImgDespeckle` may expect 1-bit images. The bridge must convert 8-bit binary to 1-bit. If using the temp BMP approach, Emgu.CV's `Imwrite` for BMP will write 8-bit. RecoIP's `ImgOpen` on an 8-bit BMP may or may not auto-convert to 1-bit. Test this.

4. **The `Imaging_cs_DEMO_TemporaryKey.pfx` reference in csproj**: Harmless today (signing disabled) but will cause a build warning. Clean it up.

5. **`callback.cs` references `ImagingDemo.RecoIO`**: This file is excluded from compilation. If someone re-includes it, the build will break. Consider deleting it if it's truly dead code.

6. **Emgu.CV x86 compatibility**: The csproj includes `Emgu.CV.runtime.windows` (version 4.9). Confirm this NuGet package includes x86 native binaries. The `PlatformTarget` is x86, so AnyCPU native libs won't work.
