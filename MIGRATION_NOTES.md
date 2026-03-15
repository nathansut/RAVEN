# RAVEN2 Migration Notes

## What Is This?

RAVEN2 is a 64-bit, proprietary-DLL-free fork of RAVEN (copied from `/home/ns/IET/`).
The goal is compilation, not functionality. Proprietary functions are stubbed as no-ops.

## Source

- Copied from: `/home/ns/IET/` on 2026-03-08
- Copy method: `cp -r /home/ns/IET/ /home/ns/RAVEN2/`

## What Was Removed

| Item | Type | Notes |
|------|------|-------|
| `recoip.dll` | Recogniform imaging SDK (32-bit) | Content item removed from csproj |
| `usvwin32.dll` | Sutterfield USVWin32 imaging SDK (32-bit) | EmbeddedResource removed from csproj |

## What Was Stubbed

### RecoIPStubs.cs (replaces RecoIPAPI.cs)
32 methods stubbed as no-ops. Original renamed to `RecoIPAPI.cs.bak`.

Stubbed methods: `ImgOpen`, `ImgCreate`, `ImgDelete`, `ImgDuplicate`, `ImgCopy`,
`ImgSaveAsTif`, `ImgSaveAsJpg`, `ImgGetWidth`, `ImgGetHeight`, `ImgGetBitsPixel`,
`ImgCropBorder`, `ImgAddCopy`, `ImgFindBlackBorderLeft/Right/Top/Bottom`,
`ImgDynamicThresholdAverage`, `ImgAdaptiveThresholdAverage`, `ImgAutoThreshold`,
`ImgRefineThreshold`, `ImgBackTrackThresholdAverage`, `ImgInvert`, `ImgDespeckle`,
`ImgRotate`, `ImgResize`, `ImgDeskew`, `ImgCorrectDeformation1`,
`ImgConvertToGrayScale`, `ImgRemoveBlackWires`, `ImgRemoveBleedThrough`,
`ImgRemoveVerticalLines`, `ImgDrawRectangle`, `ImgDrawText`.

### USVWinStubs.cs (replaces USVWin class in KeyPicture.Common.cs)
8 methods that Main.cs calls directly, plus struct definitions for compilation:
`RemoveDirtyLine`, `EraseIn`, `EraseOut`, `SKEWCORRECT`, `COMBINETIFFS`,
`VW_CombineMultiplePageTiffs`, `VW_CombineMultipleTiffPages`, `GetImageInfo`.

Also includes `TiffRedactInfo`, `TiffPrintCropInfo` structs and `USVWinCallback` delegate
for potential future use.

### RavenPictureBox.cs (replaces KeyPicture control)
A `Panel` subclass with the same public API as `KeyPicture`. All methods are no-ops.
The real implementation (using Vortice.Direct2D1/WIC) will be built by another agent.

## What Was Excluded From Compilation

| File | Method |
|------|--------|
| `RecoIPAPI.cs` | Renamed to `.bak` + `<Compile Remove>` |
| `KeyPicture.Common.cs` | `<Compile Remove>` in csproj |
| `KeyPicture.Common.Designer.cs` | `<Compile Remove>` in csproj |
| `callback.cs` | Already excluded in original |
| `Clipbrd.cs` | Already excluded in original |

## Platform Changes

- `PlatformTarget`: `x86` -> `x64`
- `Platform` default: `x86` -> `x64`
- Debug config condition: `Debug|x86` -> `Debug|x64`

## NuGet Packages Added

- `Vortice.Direct2D1` (for future image viewer implementation)
- `Vortice.WIC` (for future image viewer implementation)

## Designer Changes (Main.Designer.cs)

- `KeyPicture` -> `RavenPictureBox` (type declarations and instantiations)
- `keyPicture1.Paint` event handler retained (Panel has Paint event)

## What Still Works

- **DynamicThreshold.cs**: Pure C# threshold — fully functional
- **OpenThresholdBridge.cs**: `ApplyThresholdToFile` path (no RecoIP) — fully functional
- **UI navigation**: Form loads, buttons work, keyboard shortcuts compile
- **Settings/INI loading**: All pure C# — fully functional
- **Emgu.CV operations**: FineRotate, SaveAsTiff — should work in 64-bit

## What Is Stubbed / Non-Functional

- All RecoIP image processing (crop, threshold, deskew, despeckle, etc.)
- All USVWin display/TIFF operations
- Image viewer display (RavenPictureBox is a blank Panel)
- Scanning, printing, redaction features
- `OpenThresholdBridge.ApplyThreshold` handle path (uses RecoIP stubs)
