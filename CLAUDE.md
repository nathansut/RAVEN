# RAVEN — Claude Code Project Guide

## Project Overview
RAVEN is a Windows Forms (.NET 8, x64) document imaging application for scanning, thresholding, and processing document images (JPG to TIF conversion). It replaces proprietary Recogniform/USVWin SDKs with open-source C# + native C implementations.
- GitHub: https://github.com/nathansut/RAVEN.git

## Build & Run
```bash
cd /mnt/c/dev/raven && "/mnt/c/Program Files/dotnet/dotnet.exe" build -c Release
```
- Output: `bin/Release/net8.0-windows/RAVEN2.exe`
- **IMPORTANT**: Close RAVEN2.exe before building (locks output files)
- Windows Forms app — must run on Windows, but we build/edit from WSL2
- If `dotnet.exe` gives "Exec format error", run `wsl --shutdown` from PowerShell and reopen WSL

## Architecture — Key Source Files

| File | Role |
|------|------|
| `Main.cs` | WinForms main form — all UI event handlers, threshold dispatch, image operations |
| `Main.Designer.cs` | Auto-generated WinForms layout |
| `RavenImaging.cs` | Drop-in replacement for Recogniform's recoip.dll — all image I/O and processing |
| `OpenThresholdBridge.cs` | Caching/orchestration layer for RDynamic/Refine threshold paths |
| `RavenPictureBox.cs` | WIC/Direct2D image viewer (Vortice bindings), prefetches +/-3 adjacent images |
| `ThresholdSettings.cs` | F-key preset management, threshold settings UI |
| `Settings.cs` | App settings and configuration |
| `USVWinStubs.cs` | Stubs for removed USVWin SDK (EraseIn, EraseOut, etc.) |
| `recoip_native.dll` | Native C library — threshold, despeckle, refine, deskew, bleed removal |
| `recoip_native.c` | Source for the native DLL (x87 FPU for Delphi compatibility) |
| `settings.ini` | Per-preset threshold settings (F2-F12) |

## Core Libraries

### RavenImaging.cs (`public static class RavenImaging`)
- Drop-in replacement for Recogniform's recoip.dll
- **Handle-based API** (legacy GDI+ Bitmap, `int` handles): `ImgOpen`, `ImgDelete`, `ImgDuplicate`, `ImgCopy`, `ImgSaveAsTif`, `ImgDynamicThresholdAverage`, `ImgRefineThreshold`, `ImgDespeckle`, `ImgDeskew`, `ImgInvert`, `ImgRotate`, `ImgResize`, `ImgCropBorder`, `ImgAddCopy`, `ImgConvertToGrayScale`, `ImgRemoveBlackWires`, `ImgRemoveBleedThrough`, `ImgRemoveVerticalLines`, `ImgFindBlackBorder*`, `ImgAdaptiveThresholdAverage`, `ImgAutoThreshold`, `ImgDrawRectangle`
- **Byte-array API** (modern, no handles): `DynamicThresholdApply`, `RefineThresholdApply`, `DespeckleApply`, `LoadImageAsGrayscaleDual`, `LoadImageAsGrayscale`, `SaveAsCcitt4Tif`
- All threshold/despeckle/deskew P/Invoke into `recoip_native.dll` (native C, x87 FPU)
- GDI+ decode with `Parallel.For` grayscale conversion

### OpenThresholdBridge.cs
- Caching/orchestration for RDynamic and Refine threshold paths
- Preloads grayscale bytes in background on page navigation
- Caches thresholded TIF pixels in memory for instant display
- Background TIF save (display updates before save completes)
- All I/O delegates to RavenImaging — no direct image processing
- Key methods: `PreloadGrayscale`, `ApplyThresholdToFile`, `ApplyThresholdToFilePartial`, `ApplyThreshold` (handle-based), `TryGetDisplayPixels`, `WaitForPendingSave`
- Exposes timing: `LastDecodeMs`, `LastThresholdMs`, `LastWriteMs`

### RavenPictureBox.cs
- WIC/Direct2D hardware-accelerated image viewer (Vortice bindings)
- Prefetches +/-3 adjacent images for instant paging
- Scales images to panel size

### recoip_native.dll
- Native C library: threshold, despeckle, refine, deskew, bleed removal, line removal
- Source: `recoip_native.c` in project root
- Uses x87 FPU instructions for pixel-perfect Delphi/Recogniform compatibility
- Key export: `NativeDynamicThresholdAverage` — outputs 255=bright/background, 0=dark/foreground

## Threshold Paths (Critical)

Three threshold types selectable in the GUI:

| Type | Path | Flow |
|------|------|------|
| **Dynamic** | Handle-based | GDI+ `ImgOpen` -> `ImgDynamicThresholdAverage` -> `BinaryTo1bpp` -> `ImgSaveAsTif`. Uses CachedJPG handle for repeated adjustments. |
| **RDynamic** | Byte-array via Bridge | GDI+ decode -> `Parallel.For` dual grayscale -> `DynamicThresholdApply` -> `SaveAsCcitt4Tif`. Preloads grayscale on page nav, caches result. |
| **Refine** | Same as RDynamic + refine | Same as RDynamic + `RefineThresholdApply` pass for noise reduction. |

All three call the same native DLL and produce identical pixel output.

**Partial regions** (user draws a box):
- RDynamic/Refine: `OpenThresholdBridge.ApplyThresholdToFilePartial` — crops from cached grayscale, thresholds crop, composites into existing TIF
- Dynamic: `ImgCopy` crop from CachedJPG handle -> threshold -> `ImgAddCopy` into TifHandle

## Image Decode Pipeline

| Path | Decoder | Purpose |
|------|---------|---------|
| Display | WIC -> Direct2D GPU | Screen rendering (RavenPictureBox) |
| Dynamic threshold | GDI+ `new Bitmap()` | Handle-based processing |
| RDynamic threshold | GDI+ -> `Parallel.For` grayscale | Byte-array processing with preload cache |

GDI+ decode is 1.6x faster than OpenCV on this hardware. All decoders produce identical pixels.

## Polarity Conventions (CRITICAL — has caused bugs)

- `recoip_native.dll` output: **255 = bright/background (white paper), 0 = dark/foreground (black text)**
- GDI+ default 1bpp palette: **index 0 = Black, index 1 = White**
- `BinaryTo1bpp` packing: `binary != 0` (bright/255) -> set bit=1 -> White. Leave bit=0 -> Black (text). **DO NOT change this.**
- `SaveAsCcitt4Tif` packing: `pixels >= 128` -> bit=1. Same convention.
- `ImgRefineThreshold` unpack (1bpp->byte array): `bit != 0` -> 255 (bright), `bit == 0` -> 0 (dark)

## Display After Threshold

In `ThresholdMe` (Main.cs), after `threshold()` returns:
- **RDynamic/Refine**: Display from `OpenThresholdBridge.TryGetDisplayPixels` (in-memory cache, instant)
- **Dynamic**: Falls through to disk reload via `DisplayImages` (Bridge cache check is gated to RDynamic/Refine only)
- Do NOT let Dynamic use the Bridge's cached pixels — they'd be stale from a previous RDynamic run

## Known Issues / Gotchas

- **Handle leak in Dynamic full-image path**: TifHandle opened at ~line 3838 is never freed before reassignment at ~line 4048. Fix: add `if (TifHandle != 0) { RavenImaging.ImgDelete(TifHandle); TifHandle = 0; }` before the reassignment.
- **Despeckle missing in RDynamic/Refine paths**: OpenThresholdBridge returns early, skipping `ImgDespeckle`
- **RecoIP is NOT thread-safe**: Never call RavenImaging handle-based methods from multiple threads
- **USVWin operations are stubbed**: `EraseIn`, `EraseOut`, `SKEWCORRECT`, etc. are no-ops in `USVWinStubs.cs`

## Dependencies (NuGet)

| Package | Version | Purpose |
|---------|---------|---------|
| BitMiracle.LibTiff.NET | 2.4.649 | CCITT Group 4 TIF read/write |
| ini-parser | 2.5.2 | settings.ini parsing |
| Magick.NET-Q8-AnyCPU | 14.0.0 | ImageMagick for misc image ops |
| Magick.NET.Core | 14.0.0 | ImageMagick core |
| Vortice.Direct2D1 | 3.6.2 | WIC/Direct2D for RavenPictureBox |
| Microsoft.CSharp | 4.7.0 | Runtime support |
| System.ComponentModel.Composition | 8.0.0 | MEF |
| System.Data.DataSetExtensions | 4.5.0 | DataSet extensions |

**NOTE**: Emgu.CV was fully removed (March 2026). Do NOT re-add it.

## Native Dependencies

| File | Purpose |
|------|---------|
| `recoip_native.dll` | C library for threshold/despeckle/refine/deskew (copied to output) |
| `sutterfield.lic` | License file (copied to output) |

## Conventions

- Class name: `RavenImaging` (not RecoIP — fully renamed)
- Method naming: `Img*` for handle-based API, `*Apply` for byte-array API
- All imaging work goes through `RavenImaging.cs` — no direct GDI+/OpenCV calls elsewhere
- `OpenThresholdBridge` handles only caching/orchestration, delegates all I/O to RavenImaging
- Threshold settings stored in `settings.ini` per F-key preset (F2-F12)
- Despeckle dispatch: Dynamic uses `ConversionSettings.Despeckle` (1st GUI box), Refine uses `ConversionSettings.DespeckleFilter` (2nd GUI box)
- Platform target: x64 (was x86 when using proprietary 32-bit DLLs)
- Namespace: `RAVEN`

## Development Environment

- Dev machine: t14s-wsl (Tailscale SSH: `ssh ns@t14s-wsl`)
- Claude Code host: NS1-WSL
- Project path: `/mnt/c/dev/raven/` (Windows: `C:\dev\raven`)
- dotnet: `"/mnt/c/Program Files/dotnet/dotnet.exe"`
- Full env docs: https://github.com/nathansut/Env

## Files Excluded From Compilation

These are in the repo but excluded via `<Compile Remove>` in csproj:
- `RecoIPAPI.cs` — old P/Invoke wrappers for proprietary recoip.dll
- `KeyPicture.Common.cs` / `KeyPicture.Common.Designer.cs` — old USVWin image viewer
- `callback.cs`, `Clipbrd.cs` — legacy code
