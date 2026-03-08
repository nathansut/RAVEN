# Session Log

### 2026-03-07 — Agent #4 review of rebrand + integration plans
- **Done**: Reviewed Agent #1 rebrand (complete, minor leftovers), Agent #2 DynamicThreshold plan (accurate, bridge approach needs simplification), Agent #3 in-memory plan (accurate, phasing sensible). Wrote REVIEW_REPORT.md.
- **Next**: Fix SLN Debug config, clean up Imaging_cs_DEMO references, then integrate DynamicThreshold with temp-BMP bridge (simpler than DIB handle approach).
- **Notes**: SLN Debug config maps to AnyCPU instead of x86 -- will break debugging with recoip.dll. Recommend temp-file bridge for v1, not DIB handle manipulation.

### 2026-03-08 — RAVEN2 fork created (64-bit, proprietary-free)
- **Done**: Copied IET -> RAVEN2. Changed platform to x64. Removed recoip.dll and usvwin32.dll from csproj. Created RecoIPStubs.cs (32 methods), USVWinStubs.cs (8 methods + structs), RavenPictureBox.cs (Panel stub replacing KeyPicture). Updated Main.Designer.cs to use RavenPictureBox. Excluded old KeyPicture and RecoIPAPI files from compilation. Added Vortice NuGet packages for future image viewer. Wrote MIGRATION_NOTES.md.
- **Next**: Build on Windows with dotnet to verify compilation. Implement real RavenPictureBox using Vortice.Direct2D1/WIC. Replace RecoIP stubs with pure C# equivalents where possible (crop, rotate, save).
- **Notes**: dotnet SDK not available on this Linux host -- build verification must happen on Windows. All stubs compile-compatible based on manual review of all call sites in Main.cs, ThresholdSettings.cs, and OpenThresholdBridge.cs.

### 2026-03-08 — Integrated real RavenPictureBox (D2D+WIC) into RAVEN2
- **Done**: Replaced stub RavenPictureBox.cs with real Direct2D+WIC implementation from RavenViewerTest. Changed namespace RavenViewer->RAVEN. Added 18 stub methods and 10 stub fields/properties required by Main.cs but not present in the real D2D viewer (ZoomImage, ResetZoom, Pan, CropAndSave, RemoveDirtyLine, Deskew, etc.). Improved IsLoaded to check `_bitmap != null`. Made Clear_Image functional (disposes bitmap, clears annotations). Verified no duplicate class definitions; KeyPicture files already excluded from csproj.
- **Next**: Build on Windows to verify compilation. Implement real logic for the critical stubs: CropAndSave, RemoveDirtyLine, Deskew (used at runtime). Test D2D rendering in the actual Form1 layout.
- **Notes**: OnDoLeftClickEvent is declared but never fired (no call sites found in Main.cs). RemoveDirtyLine is called at line 4552 of Main.cs and is currently a no-op stub. keyPicture1_Paint handler (line 4560) is an empty method; the Designer's Paint event subscription is harmless since Panel inherits Paint.

### 2026-03-08 — RefineThreshold reverse-engineered and integrated into RDynamic pipeline
- **Done**: Created RefineThreshold.cs from Ghidra decompilation of recoip.dll's ImgRefineThreshold function chain. Algorithm: Sobel gradient → CC flood-fill → flip low-contrast components. Added refineThreshold/refineTolerance params to all 4 OpenThresholdBridge.ApplyThresholdTo* methods. Updated all 3 RDynamic early-return paths in Main.cs to pass RefineThreshold flag and tolerance. Build compiles with 0 errors (only blocked by locked RAVEN.exe).
- **Notes**: RefineThreshold time is included in LastThresholdMs (thresh: timing). Tolerance default is 10 ("sweet spot" per code comments). The Ghidra decompilation chain was: ImgRefineThreshold(0x00b90404) → FUN_00b8cfd4 (license) → FUN_00a8047c (main algo) → helpers for flood-fill, edge test, pixel flip.

### 2026-03-08 — RefineThreshold gradient fix verified on ns1-wsl (99.999% match)
- **Done**: Tested 15 gradient variants against Recogniform's actual output on ns1-wsl (licensed recoip.dll). **5x5 box filter + Sobel clamp255** is the correct algorithm: 99.9987%, 99.9995%, 99.9992% match across 3 images. Updated RefineThreshold.cs to use BoxFilter5x5 → Sobel L1 clamped (no >>3 shift). RAVEN2 builds with 0 errors.
- **Next**: Test RAVEN2 end-to-end with real images from c:\temp\1a. Background save + selection fixes still need testing.
- **Notes**: Previous gradient (Sobel >>3) was too aggressive — 99.93% match, over-flipped 13k pixels vs Recogniform's 866. The fix matches Ghidra: FUN_00a41c08(gray,out,3) = 5x5 box filter, then FUN_00a7f764(smoothed,grad,0,0) = Sobel with no scaling.

### 2026-03-08 — Two-grayscale architecture: 100% DT match via LUT formula
- **Done**: Reverse-engineered Recogniform's internal grayscale for DT via Ghidra decompilation. DT uses weighted LUT: `round(R*0.30) + round(G*0.59) + round(B*0.11)` (per-channel rounding), NOT simple avg or BT.601. Verified 100.0000% DT match (0 diff) on all 10 test images. Implemented two-gray architecture in OpenThresholdBridge.cs: LUT gray for DynamicThreshold, avg gray `(R+G+B+1)/3` for RefineThreshold. PreloadGrayscale caches both. All 4 Apply methods updated. Build succeeds with 0 errors.
- **Next**: Close RAVEN.exe and test end-to-end. Despeckle deferred.
- **Notes**: Recogniform uses TWO different grayscale conversions internally. DT's LUT tables found at DAT_00c28b74 (B×0.11), DAT_00c28c74 (G×0.59), DAT_00c28d74 (R×0.30). Old BT.601 (OpenCV default) was wrong for both. Full pipeline with LUT(DT)+avg(Refine): 99.998-99.9996% match.

### 2026-03-08 — RefineThreshold critical bug fix: only flip BLACK components
- **Done**: Built PipelineCompare.exe to compare IET (RecoIP) vs RAVEN2 (C#) pipelines side by side. With C336 B255 T10, found 2.8-5.3% diff — RAVEN2 Refine was flipping ~2x as many pixels as RecoIP. Root cause: our C# RefineThreshold was flipping both black AND white components, but RecoIP only flips BLACK components (noise blobs → white). White regions are never converted to black. Fix: added `if (componentColor != 0) continue;` before the flip decision. Result: diff drops from 2.8-5.3% to 0.07-0.27%.
- **Next**: Investigate remaining 0.1-0.3% diff (likely gradient computation rounding). Test JPEG decoder difference (OpenCV vs RecoIP) — not yet tested.
- **Notes**: Previous 99.999% match was at lower contrast settings (C230/B130) where the bug was hidden. High contrast (C336) creates many more small noise components of both colors, exposing the asymmetry. PipelineCompare.exe on ns1-wsl at /mnt/c/IET/.
