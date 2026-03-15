# RAVEN — Claude Code Project Guide

## Project Overview
RAVEN is a Windows Forms (.NET 8, x64) document imaging application for scanning, thresholding, and processing document images (JPG to TIF conversion). All imaging is native C# + a custom native C DLL (`raven_native.dll`).
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
| `RavenImaging.cs` | All image I/O, processing, and document operations |
| `OpenThresholdBridge.cs` | Caching/orchestration layer for Dynamic/Refine threshold paths |
| `RavenPictureBox.cs` | WIC/Direct2D image viewer (Vortice bindings), prefetches +/-3 adjacent images |
| `ThresholdSettings.cs` | F-key preset management, threshold settings UI |
| `Settings.cs` | App settings and configuration |
| `raven_native.dll` | Native C library — threshold, despeckle, refine, deskew, bleed removal |
| `raven_native.c` | Source for the native DLL (x87 FPU for precision compatibility) |
| `settings.ini` | Per-preset threshold settings (F2-F12) |

## Core Libraries

### RavenImaging.cs (`public static class RavenImaging`)
- All image I/O and processing — handles, byte-array ops, document operations (erase, deskew, line removal)
- **Handle-based API** (legacy GDI+ Bitmap, `int` handles): `ImgOpen`, `ImgDelete`, `ImgDuplicate`, `ImgCopy`, `ImgSaveAsTif`, `ImgDynamicThresholdAverage`, `ImgRefineThreshold`, `ImgDespeckle`, `ImgDeskew`, `ImgInvert`, `ImgRotate`, `ImgResize`, `ImgCropBorder`, `ImgAddCopy`, `ImgConvertToGrayScale`, `ImgRemoveBlackWires`, `ImgRemoveBleedThrough`, `ImgRemoveVerticalLines`, `ImgFindBlackBorder*`, `ImgAdaptiveThresholdAverage`, `ImgAutoThreshold`, `ImgDrawRectangle`
- **Byte-array API** (modern, no handles): `DynamicThresholdApply`, `RefineThresholdApply`, `DespeckleApply`, `LoadImageAsGrayscaleDual`, `LoadImageAsGrayscale`, `SaveAsCcitt4Tif`
- **Document operations**: `EraseIn`, `EraseOut`, `RemoveDirtyLine`, `SKEWCORRECT`, `GetImageInfo`
- All threshold/despeckle/deskew P/Invoke into `raven_native.dll` (native C, x87 FPU)
- GDI+ decode with `Parallel.For` grayscale conversion

### OpenThresholdBridge.cs
- Caching/orchestration for Dynamic and Refine threshold paths
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

### raven_native.dll
- Native C library: threshold, despeckle, refine, deskew, bleed removal, line removal
- Source: `raven_native.c` in project root
- Uses x87 FPU instructions for extended-precision math
- Key export: `NativeDynamicThresholdAverage` — outputs 255=bright/background, 0=dark/foreground

## Threshold Paths (Critical)

Threshold types selectable in the GUI:

| Type | Flow |
|------|------|
| **Dynamic** | GDI+ decode → `Parallel.For` dual grayscale → `DynamicThresholdApply` → `SaveAsCcitt4Tif`. Preloads grayscale on page nav, caches result for instant display. |
| **Refine** | Same as Dynamic + `RefineThresholdApply` pass for noise reduction. |
| **ML1/ML2** | Pre-computed ML binarization files. |

**Partial regions** (user draws a box):
- Dynamic/Refine: `OpenThresholdBridge.ApplyThresholdToFilePartial` — crops from cached grayscale, thresholds crop, composites into existing TIF

**Batch Mode** (`[`/`]` keys or Batch button):
- Multi-threaded (`ProcessorCount - 2`), byte-array pipeline only (thread-safe)
- Supports full-page and area-based conversion for all types
- Full photostat pipeline with 3-level border detection + bleed-through removal

## Image Decode Pipeline

| Path | Decoder | Purpose |
|------|---------|---------|
| Display | WIC → Direct2D GPU | Screen rendering (RavenPictureBox) |
| Threshold | GDI+ → `Parallel.For` grayscale | Byte-array processing with preload cache |

## Polarity Conventions (CRITICAL — has caused bugs)

- `raven_native.dll` output: **255 = bright/background (white paper), 0 = dark/foreground (black text)**
- GDI+ default 1bpp palette: **index 0 = Black, index 1 = White**
- `BinaryTo1bpp` packing: `binary != 0` (bright/255) → set bit=1 → White. Leave bit=0 → Black (text). **DO NOT change this.**
- `SaveAsCcitt4Tif` packing: `pixels >= 128` → bit=1. Same convention.

## Display After Threshold

In `ThresholdMe` (Main.cs), after `threshold()` returns:
- **Dynamic/Refine**: Display from `OpenThresholdBridge.TryGetDisplayPixels` (in-memory cache, instant)
- **Other types**: Falls through to disk reload via `DisplayImages`

## Known Issues / Gotchas

- **Handle leak in Dynamic full-image path**: TifHandle opened at ~line 3838 is never freed before reassignment at ~line 4048. Fix: add `if (TifHandle != 0) { RavenImaging.ImgDelete(TifHandle); TifHandle = 0; }` before the reassignment.
- **Despeckle missing in Dynamic/Refine paths**: OpenThresholdBridge returns early, skipping `ImgDespeckle`
- **RavenImaging handle API is NOT thread-safe**: Never call handle-based methods from multiple threads
- **Document operations consolidated**: `EraseIn`, `EraseOut`, `SKEWCORRECT`, `RemoveDirtyLine`, `GetImageInfo` are all in `RavenImaging.cs`

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
| `raven_native.dll` | C library for threshold/despeckle/refine/deskew (copied to output) |

## Conventions

- Class name: `RavenImaging`
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

- **Batch conversion is multi-threaded**: uses byte-array API only (thread-safe), `ProcessorCount - 2` parallelism
- All TIF/JPG saves use atomic tmp-then-move (`File.Move(overwrite: true)`) to prevent file-in-use errors
