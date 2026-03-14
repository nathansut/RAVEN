# Cursor Instructions: Fix RDynamic Threshold Pipeline

## The Problem

RDynamic threshold produces **mostly black images**. The root cause is that the current bridge writes 8-bit TIFs (values 0 and 255), but downstream RecoIP operations expect 1-bit CCITT Group 4 TIFs. When `RecogSaveImage` tries to save an 8-bit handle with CCITT Group 4 compression (`ImgSaveAsTif(handle, file, 5, 0)`), it produces corrupt/black output.

## The Fix: Two Separate Pipelines

We want two completely independent save paths. No mixing.

### Pipeline 1 — Recogniform (Type = "Dynamic")
```
JPEG → RecoIP.ImgOpen → ImgDynamicThresholdAverage → ImgDespeckle → RecogSaveImage
```
**Do not change this. Leave it exactly as-is.**

### Pipeline 2 — RAVEN (Type = "RDynamic")
```
JPEG → Emgu.CV or cached grayscale → DynamicThreshold.Apply() → RSave (1-bit CCITT via LibTiff)
```
**No RecoIP involvement at all. No temp files. No handle round-tripping.**

## What to implement

### 1. Create `RSave` — a static method that writes 1-bit CCITT Group 4 TIF from a byte array

Put this on `OpenThresholdBridge` (or a new small static class if you prefer).

Signature:
```csharp
public static void RSave(byte[] binary, int width, int height, string outputPath)
```

- `binary` is the output from `DynamicThreshold.Apply()` — values are 0 (black) and 255 (white)
- Pack into 1-bit: `pixel < 128 ? 1 : 0` (MINISWHITE: 0=white, 1=black)
- Write CCITT Group 4 via BitMiracle.LibTiff.NET (already a project dependency)

**Reference implementation already exists** in `Main.cs` at the `SaveAsTiff` method (around line 2401). It does exactly this but takes a Mat. Just adapt it to take `byte[]` directly instead of a Mat. The key logic:

```csharp
// Pack 8-bit binary into 1-bit (MSB first)
int byteWidth = (width + 7) / 8;
byte[] tiffBytes = new byte[byteWidth * height];
for (int y = 0; y < height; y++)
{
    for (int x = 0; x < width; x++)
    {
        byte pixel = binary[y * width + x] < 128 ? (byte)1 : (byte)0;
        tiffBytes[y * byteWidth + (x / 8)] |= (byte)(pixel << (7 - (x % 8)));
    }
}

// Write with LibTiff
using (Tiff image = Tiff.Open(outputPath, "w"))
{
    image.SetField(TiffTag.IMAGEWIDTH, width);
    image.SetField(TiffTag.IMAGELENGTH, height);
    image.SetField(TiffTag.BITSPERSAMPLE, 1);
    image.SetField(TiffTag.SAMPLESPERPIXEL, 1);
    image.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
    image.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISWHITE);
    image.SetField(TiffTag.COMPRESSION, Compression.CCITTFAX4);
    image.SetField(TiffTag.FILLORDER, FillOrder.MSB2LSB);
    image.SetField(TiffTag.ROWSPERSTRIP, height);
    image.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);

    for (int y = 0; y < height; y++)
    {
        byte[] scanline = new byte[byteWidth];
        Array.Copy(tiffBytes, y * byteWidth, scanline, 0, byteWidth);
        image.WriteScanline(scanline, y);
    }
}
```

### 2. Simplify `ApplyThresholdToFile` to use RSave

Current code writes 8-bit TIF with `CvInvoke.Imwrite`. Change it to:

```csharp
public static void ApplyThresholdToFile(string inputJpgPath, string outputTifPath,
    int windowW, int windowH, int contrast, int brightness)
{
    byte[] gray;
    int width, height;

    if (TryGetCache(inputJpgPath, out gray, out width, out height))
    {
        // cache hit
    }
    else
    {
        (gray, width, height) = LoadGrayscaleFromFile(inputJpgPath);
    }

    byte[] binary = DynamicThreshold.Apply(gray, width, height, windowW, windowH, contrast, brightness);
    RSave(binary, width, height, outputTifPath);
}
```

### 3. Fix the full-image RDynamic path in Main.cs (around line 3945)

The current code calls `ApplyThresholdToFile` and then `return;`. This is correct — it skips all RecoIP. Just make sure `ApplyThresholdToFile` now calls `RSave` internally so the output is a proper 1-bit CCITT TIF.

**No changes needed to Main.cs call site if you fix `ApplyThresholdToFile` internally.**

### 4. Photostat and partial area paths — SKIP FOR NOW

Sites 1, 3, and 4 (Photostat full, partial area, ThresholdSettings photostat) still use `ApplyThreshold` which round-trips through RecoIP handles. **Do not fix these yet.** For now, RDynamic only works for the full non-photostat conversion path (site 2). The other paths can fall back to Dynamic or be addressed later.

Consider disabling RDynamic for those paths with a message:
```csharp
if (conversionSettings?.Type == "RDynamic" && (NegativeImage || X1 > 0 || Y1 > 0))
{
    MessageBox.Show("RDynamic not yet supported for photostat/partial. Using Dynamic.");
    // fall through to Recogniform path
}
```

### 5. Keep the timing display

The `Stopwatch` timing in the status bar is good. Keep it.

## What NOT to change

- Do not modify the Recogniform (Dynamic) pipeline at all
- Do not modify `RecogSaveImage`
- Do not modify `DynamicThreshold.cs` (the strip optimization is mathematically correct)
- Do not remove the background preload (`PreloadGrayscale`) — it's working and makes RDynamic faster

## Files to modify

| File | Change |
|------|--------|
| `OpenThresholdBridge.cs` | Add `RSave` static method. Change `ApplyThresholdToFile` to call `RSave` instead of `CvInvoke.Imwrite`. Add `using BitMiracle.LibTiff.Classic;` |
| `Main.cs` | Optional: guard Photostat/partial paths to not use RDynamic yet |

## How to verify

1. Select RDynamic in the F2 dropdown
2. Do a full-image conversion (not photostat, not partial area)
3. The output TIF should be:
   - 1-bit black and white (not 8-bit grayscale)
   - Small file size (CCITT compressed, typically 50-200KB for a document page)
   - Visually identical to Dynamic threshold output
4. Check the status bar shows timing in ms
5. Subsequent operations on the converted page (whiteout, crop, etc.) should work normally since the TIF is now proper 1-bit
