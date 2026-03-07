# Cursor Instructions: Speed Optimizations for RDynamic

## Context

RDynamic currently runs at ~700ms, same as Recogniform. In benchmarks (64-bit, WSL) we got:
- Decode: 100ms (libjpeg-turbo) vs ~450ms (Emgu.CV)
- Threshold: 35ms (64-bit) vs ~35-70ms (32-bit strips)
- Save: ~5ms (native Windows)

Target: **~200ms total** on 32-bit Windows (decode ~100ms + threshold ~70ms + save ~30ms).

## Changes — in priority order

---

### 1. Add TurboJpegDecoder.cs (biggest win: ~350ms saved per image)

Create a new file `TurboJpegDecoder.cs` in the project. This replaces Emgu.CV's slow JPEG decode.

**Copy this file exactly** (adapted from our benchmarks repo for 32-bit):

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RAVEN;

public static class TurboJpegDecoder
{
    private const int TJPF_GRAY = 6;
    private const int TJFLAG_FASTDCT = 2048;
    private const string LibName = "turbojpeg";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tjInitDecompress();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int tjDecompressHeader3(IntPtr handle, byte[] jpegBuf, uint jpegSize,
        out int width, out int height, out int jpegSubsamp, out int jpegColorspace);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int tjDecompress2(IntPtr handle, byte[] jpegBuf, uint jpegSize,
        byte[] dstBuf, int width, int pitch, int height, int pixelFormat, int flags);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int tjDestroy(IntPtr handle);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tjGetErrorStr2(IntPtr handle);

    public static (byte[] gray, int width, int height) DecodeGrayscale(string path)
    {
        byte[] jpegData = File.ReadAllBytes(path);
        return DecodeGrayscale(jpegData);
    }

    public static (byte[] gray, int width, int height) DecodeGrayscale(byte[] jpegData)
    {
        IntPtr handle = tjInitDecompress();
        if (handle == IntPtr.Zero)
            throw new Exception("Failed to init TurboJPEG");

        try
        {
            int ret = tjDecompressHeader3(handle, jpegData, (uint)jpegData.Length,
                out int width, out int height, out int subsamp, out int colorspace);
            if (ret != 0)
                throw new Exception("JPEG header error: " + GetError(handle));

            byte[] gray = new byte[width * height];
            ret = tjDecompress2(handle, jpegData, (uint)jpegData.Length,
                gray, width, width, height, TJPF_GRAY, TJFLAG_FASTDCT);
            if (ret != 0)
                throw new Exception("JPEG decode error: " + GetError(handle));

            return (gray, width, height);
        }
        finally
        {
            tjDestroy(handle);
        }
    }

    private static string GetError(IntPtr handle)
    {
        IntPtr errPtr = tjGetErrorStr2(handle);
        return errPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errPtr) ?? "unknown" : "unknown";
    }
}
```

**IMPORTANT for 32-bit**: The P/Invoke signatures use `uint` (not `ulong`) for `jpegSize`. The benchmarks repo used `ulong` because it was 64-bit. On 32-bit, `ulong` causes marshaling issues. Use `uint`.

**Getting the DLL**: Download the 32-bit libjpeg-turbo installer from https://libjpeg-turbo.org/ (the "GCC 32-bit" or "VC 32-bit" Windows build). After install, find `turbojpeg.dll` (typically in `C:\libjpeg-turbo\bin\` or similar) and copy it next to `RAVEN.exe` in the output directory. Alternatively, add it to the project like `recoip.dll`:

```xml
<!-- Add to RAVEN.csproj -->
<Content Include="turbojpeg.dll">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</Content>
```

---

### 2. Switch OpenThresholdBridge to use TurboJpegDecoder

Replace all Emgu.CV JPEG loading with TurboJpegDecoder. This is the actual hookup.

**In `OpenThresholdBridge.cs`**, make these changes:

#### a) Replace `PreloadGrayscale`:
```csharp
public static void PreloadGrayscale(string jpgPath)
{
    try
    {
        var (gray, w, h) = TurboJpegDecoder.DecodeGrayscale(jpgPath);

        lock (_cacheLock)
        {
            _cachedGray   = gray;
            _cachedWidth  = w;
            _cachedHeight = h;
            _cachedPath   = jpgPath.ToLower();
        }
    }
    catch { }
}
```

#### b) Replace `LoadGrayscaleFromFile`:
```csharp
private static (byte[] gray, int width, int height) LoadGrayscaleFromFile(string path)
{
    string ext = Path.GetExtension(path).ToLowerInvariant();
    if (ext == ".jpg" || ext == ".jpeg")
    {
        return TurboJpegDecoder.DecodeGrayscale(path);
    }

    // Fallback to Emgu.CV for non-JPEG files
    using var mat = CvInvoke.Imread(path, ImreadModes.Grayscale);
    if (mat.IsEmpty) throw new Exception($"Could not load image: {path}");
    return (ExtractGray(mat), mat.Width, mat.Height);
}
```

#### c) Remove `ExtractGray` only if no other code uses it. If `LoadGrayscaleFromHandle` still needs it (for the Emgu.CV TIF fallback), keep it.

---

### 3. Switch integral image from `long[]` to `int[]` (strip-based only)

The strip-based integral uses 512+8=520 row strips. Max integral value = 3489 * 520 * 255 = ~463 million. `int.MaxValue` = 2.147 billion. Fits easily.

On 32-bit x86, `int` arithmetic is native (single instruction) while `long` requires two instructions per operation. This should speed up the threshold by ~30-40%.

**In `DynamicThreshold.cs`**, change:

#### a) `ComputeIntegralStrip` — change `long[]` to `int[]`:
```csharp
internal static int[] ComputeIntegralStrip(byte[] gray, int width, int startRow, int rowCount)
{
    int iw = width + 1;
    int[] integral = new int[iw * (rowCount + 1)];

    for (int ly = 0; ly < rowCount; ly++)
    {
        int gy = startRow + ly;
        int rowSum = 0;
        for (int x = 0; x < width; x++)
        {
            rowSum += gray[gy * width + x];
            integral[(ly + 1) * iw + (x + 1)] = rowSum + integral[ly * iw + (x + 1)];
        }
    }

    return integral;
}
```

#### b) In `Apply`, change the lookup code — `long sum` becomes `int sum`:
```csharp
int[] integral = ComputeIntegralStrip(gray, width, srcY1, srcRows);

// Inside the Parallel.For:
int sum = integral[(ly2 + 1) * iw + (x2 + 1)]
        - integral[ly1        * iw + (x2 + 1)]
        - integral[(ly2 + 1)  * iw + x1]
        + integral[ly1        * iw + x1];

int count = (y2 - y1 + 1) * (x2 - x1 + 1);
int mean  = (sum + count / 2) / count;  // no cast needed now
```

#### c) Update `ComputeIntegral` (the full-image one kept for tests):
```csharp
internal static int[] ComputeIntegral(byte[] gray, int width, int height)
    => ComputeIntegralStrip(gray, width, 0, height);
```

**NOTE**: This is ONLY safe for strip sizes up to ~2400 rows at 3489 width. Our strip size is 520, so we're fine. If someone passes strip sizes above 2400 rows with very wide images, it would overflow. The current code caps at 512 + 2*hh so this won't happen in practice.

---

### 4. Split timing display (decode / threshold / save)

Currently the Stopwatch wraps the entire RDynamic call. Split it so the status bar shows the breakdown. This helps diagnose where time goes.

Find the RDynamic timing code in `Main.cs` (around line 3945, the full non-photostat conversion path). It currently looks something like:

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
OpenThresholdBridge.ApplyThresholdToFile(jpgPath, outputTifPath, windowW, windowH, contrast, brightness);
sw.Stop();
StatusLabel.Text = $"RDynamic: {sw.ElapsedMilliseconds}ms";
```

**Option A (simple)**: Add timing inside `ApplyThresholdToFile` and return it:

Add a result struct to `OpenThresholdBridge`:
```csharp
public struct TimingResult
{
    public long DecodeMs;
    public long ThresholdMs;
    public long SaveMs;
    public long TotalMs => DecodeMs + ThresholdMs + SaveMs;
}
```

Change `ApplyThresholdToFile` to return timing:
```csharp
public static TimingResult ApplyThresholdToFile(string inputJpgPath, string outputTifPath,
    int windowW, int windowH, int contrast, int brightness)
{
    var timing = new TimingResult();
    var sw = System.Diagnostics.Stopwatch.StartNew();

    byte[] gray;
    int width, height;

    if (TryGetCache(inputJpgPath, out gray, out width, out height))
    {
        // cache hit — decode was free
    }
    else
    {
        (gray, width, height) = LoadGrayscaleFromFile(inputJpgPath);
    }
    sw.Stop();
    timing.DecodeMs = sw.ElapsedMilliseconds;

    sw.Restart();
    byte[] binary = DynamicThreshold.Apply(gray, width, height, windowW, windowH, contrast, brightness);
    sw.Stop();
    timing.ThresholdMs = sw.ElapsedMilliseconds;

    sw.Restart();
    RSave(binary, width, height, outputTifPath);
    sw.Stop();
    timing.SaveMs = sw.ElapsedMilliseconds;

    return timing;
}
```

Then in `Main.cs`:
```csharp
var timing = OpenThresholdBridge.ApplyThresholdToFile(jpgPath, outputTifPath, windowW, windowH, contrast, brightness);
StatusLabel.Text = $"RDynamic: {timing.TotalMs}ms (decode:{timing.DecodeMs} thresh:{timing.ThresholdMs} save:{timing.SaveMs})";
```

**Option B (minimal)**: Just change the status text format in Main.cs and keep timing external. Simpler but less informative. Option A is preferred.

---

### 5. Verify background preload is actually hitting the cache

Add a small diagnostic to confirm cache hits. In `ApplyThresholdToFile`, after the cache check:

```csharp
bool cacheHit = TryGetCache(inputJpgPath, out gray, out width, out height);
// The timing struct will show DecodeMs = 0 when cache hits.
// If DecodeMs is always ~100ms, the cache isn't working.
```

Common reasons cache misses:
- Path case mismatch: `TryGetCache` lowercases with `.ToLower()`. Make sure `PreloadGrayscale` and the caller pass the same path format.
- Preload hasn't finished yet: The `Task.Run` background preload might still be running when threshold is triggered. Consider `await`ing or using a `Task` field that `ApplyThresholdToFile` can wait on briefly.
- Preload throws silently: The `catch { }` in `PreloadGrayscale` swallows errors. Temporarily add logging.

---

## Summary of expected gains

| Component | Current | After | Savings |
|-----------|---------|-------|---------|
| JPEG decode (cache miss) | ~450ms (Emgu.CV) | ~100ms (turbojpeg) | **350ms** |
| JPEG decode (cache hit) | 0ms | 0ms | 0ms |
| Threshold (32-bit long[]) | ~70ms | ~45ms (int[]) | **25ms** |
| Save (CvInvoke.Imwrite 8-bit) | ~30ms | ~30ms (RSave 1-bit) | 0ms |
| **Total (cache miss)** | **~550ms** | **~175ms** | **~375ms** |
| **Total (cache hit)** | **~100ms** | **~75ms** | **~25ms** |

With background preload working correctly, most conversions should hit cache and run in ~75ms.

---

## Files to create/modify

| File | Action |
|------|--------|
| `TurboJpegDecoder.cs` | **CREATE** — new file, copy code from section 1 |
| `OpenThresholdBridge.cs` | **MODIFY** — use TurboJpegDecoder, add TimingResult, update ApplyThresholdToFile |
| `DynamicThreshold.cs` | **MODIFY** — change `long[]` to `int[]` in strip integral |
| `Main.cs` | **MODIFY** — update status bar text for split timing |
| `RAVEN.csproj` | **MODIFY** — add turbojpeg.dll content item |
| `turbojpeg.dll` | **ADD** — 32-bit DLL from libjpeg-turbo, copy to project root |

## Prerequisites

1. **RSave must already be implemented** (from CURSOR_INSTRUCTIONS.md). These speed changes build on top of that fix.
2. **turbojpeg.dll (32-bit)** must be obtained and placed next to the exe. Without it, TurboJpegDecoder will throw a DllNotFoundException at runtime. The fallback is Emgu.CV (slow but works).

## Order of implementation

1. Get `turbojpeg.dll` (32-bit) and add to project
2. Create `TurboJpegDecoder.cs`
3. Modify `OpenThresholdBridge.cs` to use it + add TimingResult
4. Modify `DynamicThreshold.cs` for int[] integral
5. Update Main.cs status bar for split timing
6. Test: verify timing shows ~100ms decode (cache miss) or ~0ms (cache hit), ~45ms threshold
