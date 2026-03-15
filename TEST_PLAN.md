# RAVEN2 vs IET End-to-End Testing Plan

## Overview

Two standalone CLI test harnesses that mirror the GUI operations of IET and RAVEN2, plus a test runner that compares outputs and tracks feature status. All headless, all callable by Claude from the command line.

**Architecture:**
```
C:\dev\RavenTest\
├── IETHarness\          # x86, .NET 6.0 — calls recoip.dll directly
│   ├── IETHarness.csproj
│   ├── Program.cs       # CLI entry point
│   ├── RecoIPAPI.cs     # Copied from IET (DllImport wrappers)
│   ├── Operations.cs    # Each GUI operation as a CLI command
│   └── recoip.dll       # Copied from IET (32-bit)
│
├── RAVENHarness\        # x64, .NET 8.0 — uses RAVEN2 open-source code
│   ├── RAVENHarness.csproj
│   ├── Program.cs       # CLI entry point
│   ├── Operations.cs    # Same operations using RAVEN2 code
│   ├── RecoIPStubs.cs   # Harvested from RAVEN2
│   ├── DynamicThreshold.cs
│   ├── RefineThreshold.cs
│   └── OpenThresholdBridge.cs
│
├── TestRunner\          # Orchestrator — runs both, compares, reports
│   ├── TestRunner.csproj
│   ├── Program.cs
│   ├── ImageComparer.cs # Pixel-level comparison with tolerance
│   └── ReportGenerator.cs
│
├── TestImages\          # Symlink or copy from C:\temp\1a
├── TestOutput\          # Generated outputs go here
│   ├── iet\
│   └── raven\
│
├── harvest.ps1          # Script to pull latest code from RAVEN2
├── test_status.txt      # Machine-readable feature status
├── test_report.txt      # Human-readable last run report
└── README.md            # Instructions for Claude
```

---

## Part 1: The Two CLI Harnesses

### IETHarness (x86 / .NET 6.0)

Wraps IET's Recogniform SDK calls into CLI commands. This is the **reference/golden implementation**.

**Why x86:** `recoip.dll` is 32-bit only. Must target x86.

**Project file:** `IETHarness.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net6.0-windows</TargetFramework>
    <PlatformTarget>x86</PlatformTarget>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <PackageReference Include="BitMiracle.LibTiff.NET" Version="2.4.649" />
</Project>
```

**Files to copy from IET:**
- `RecoIPAPI.cs` — the full DllImport wrapper (all ~200 functions)
- `recoip.dll` + `sutterfield.lic` — runtime dependencies

**CLI Commands (each maps to a GUI action):**

```
IETHarness.exe threshold <input.jpg> <output.tif> [--contrast 248] [--brightness 180] [--despeckle 3] [--negative]
IETHarness.exe threshold-partial <input.jpg> <existing.tif> <output.tif> --region X1,Y1,X2,Y2 [--contrast 248] [--brightness 180] [--negative]
IETHarness.exe refine <input.jpg> <output.tif> [--contrast 248] [--brightness 180] [--tolerance 10] [--despeckle-filter 8]
IETHarness.exe whiteout <input.tif> <output.tif> --region X1,Y1,X2,Y2
IETHarness.exe whiteout-outside <input.tif> <output.tif> --region X1,Y1,X2,Y2
IETHarness.exe invert-region <input.tif> <output.tif> --region X1,Y1,X2,Y2
IETHarness.exe deskew <input.tif> <output.tif>
IETHarness.exe despeckle <input.tif> <output.tif> --level 3
IETHarness.exe crop <input.tif> <output.tif> --region X1,Y1,X2,Y2
IETHarness.exe line-remove-h <input.tif> <output.tif> [--min-len 500] [--max-breaks 5]
IETHarness.exe line-remove-v <input.tif> <output.tif> [--min-len 500] [--max-breaks 5]
IETHarness.exe invert <input.tif> <output.tif>
IETHarness.exe rotate <input.tif> <output.tif> --angle 90|180|270
IETHarness.exe border-detect <input.tif> --threshold 90.0 --min-gap 1
IETHarness.exe photostat <input.jpg> <output.tif> [--contrast 251] [--brightness 200] [--despeckle 8]
IETHarness.exe mousewheel-sim <input.jpg> <output.tif> --field contrast --delta +1 [--start-contrast 248] [--brightness 180]
IETHarness.exe ml-threshold <input.ml1|.ml2> <output.tif> [--negative]
```

Each command:
1. Loads the input image via `RecoIP.ImgOpen()`
2. Performs the exact same sequence of operations as the GUI
3. Saves via `RecoIP.ImgSaveAsTif()`
4. Prints timing + dimensions to stdout as JSON
5. Returns exit code 0 on success

### RAVENHarness (x64 / .NET 8.0)

Mirrors the exact same CLI interface, but uses RAVEN2's open-source implementations.

**Project file:** `RAVENHarness.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <PackageReference Include="Emgu.CV" Version="4.9.0.5494" />
  <PackageReference Include="Emgu.CV.runtime.windows" Version="4.9.0.5494" />
  <PackageReference Include="BitMiracle.LibTiff.NET" Version="2.4.649" />
  <PackageReference Include="Magick.NET-Q8-AnyCPU" Version="14.0.0" />
</Project>
```

**Files harvested from RAVEN2:**
- `RecoIPStubs.cs` — open-source implementations
- `DynamicThreshold.cs` — threshold algorithm
- `RefineThreshold.cs` — refine filter
- `OpenThresholdBridge.cs` — threshold pipeline bridge (stripped of UI dependencies)

**Key:** Same CLI interface as IETHarness. Same command names, same parameters. The test runner doesn't need to know which is which — it just runs both with the same args and compares outputs.

---

## Part 2: Mousewheel / Wiring Verification

The mousewheel test simulates what happens when a user scrolls the mousewheel while a threshold field is highlighted. In the GUI this calls `ThresholdSettings.Settings_MouseWheel()` which adjusts contrast or brightness by ±1, then re-applies the threshold.

**CLI simulation:**
```
# Simulate: open image, press F2 (contrast=248, brightness=180), then scroll mousewheel UP 3 times on "Contrast" field
IETHarness.exe mousewheel-sim 0001.jpg output.tif --preset F2 --field contrast --delta +3

# This internally does:
#   1. Load with contrast=248, brightness=180 (F2 defaults)
#   2. Apply threshold with contrast=248 → save intermediate
#   3. Apply threshold with contrast=249 → save intermediate
#   4. Apply threshold with contrast=250 → save intermediate
#   5. Apply threshold with contrast=251 → save final output
#   6. Output JSON with all intermediate checksums
```

**What this verifies:**
- The mousewheel increments the correct field by ±1
- The settings are correctly passed to the threshold function
- Each intermediate result matches between IET and RAVEN2
- The wiring from UI control → parameter → processing function is correct

**Fields to test mousewheel on:**
| Field | Threshold Type | Expected behavior |
|-------|---------------|-------------------|
| Contrast | Dynamic/RDynamic | ±1 per scroll, range 0-255 |
| Brightness | Dynamic/RDynamic | ±1 per scroll, range 0-255 |
| Despeckle | Dynamic/RDynamic | ±1 per scroll, range 0-10 |
| Tolerance | Refine | ±1 per scroll |
| FilterThresholdStepup | Refine | ±1 per scroll |
| DespeckleFilter | Refine | ±1 per scroll |

---

## Part 3: Test Runner & Comparison

### ImageComparer

Pixel-level comparison of two TIF files:

```csharp
// Pseudocode
CompareResult Compare(string ietOutput, string ravenOutput, double tolerancePercent = 99.97)
{
    // Load both as bitmaps
    // Count total pixels, matching pixels, mismatched pixels
    // Calculate match percentage
    // Return PASS if matchPercent >= tolerancePercent
    // Also return: dimensions match, bit depth match, mismatch heatmap coords
}
```

**Output format (JSON):**
```json
{
  "test": "threshold",
  "image": "0001.jpg",
  "params": {"contrast": 248, "brightness": 180},
  "iet_dimensions": "3000x4000",
  "raven_dimensions": "3000x4000",
  "match_percent": 99.9998,
  "mismatched_pixels": 24,
  "total_pixels": 12000000,
  "status": "PASS",
  "iet_time_ms": 450,
  "raven_time_ms": 320
}
```

### TestRunner

Orchestrates the full test suite:

```
TestRunner.exe run-all               # Run every test
TestRunner.exe run-feature threshold  # Run just threshold tests
TestRunner.exe run-feature whiteout   # Run just whiteout tests
TestRunner.exe scan-features          # Scan RAVEN2 for stub vs implemented, update test_status.txt
TestRunner.exe report                 # Generate human-readable report from last run
TestRunner.exe add-images <dir>       # Register new test images
```

### Test Matrix

For each test image in `TestImages\`, the runner executes:

```
[For each .jpg file]:
  threshold         (F2 defaults: contrast=248, brightness=180, despeckle=3)
  threshold         (F3 defaults: contrast=251, brightness=200, despeckle=8, negative=true)
  threshold         (F4 defaults: contrast=249, brightness=200, despeckle=3)
  threshold-partial (region=200,200,800,800 with F2 settings)
  refine            (F2 + tolerance=10, despeckle-filter=8)
  photostat         (if negative preset exists)
  mousewheel-sim    (contrast ±3 from F2 baseline)
  mousewheel-sim    (brightness ±3 from F2 baseline)

[For each .tif file that exists]:
  whiteout          (region=200,300,500,600)
  whiteout-outside  (region=200,300,500,600)
  invert-region     (region=100,100,400,400)
  invert            (full image)
  deskew
  despeckle         (level=3)
  crop              (region=50,50,2900,3900)
  line-remove-h     (default params)
  line-remove-v     (default params)
  rotate            (90, 180, 270)
  border-detect     (threshold=90.0)

[For each .ml1/.ml2 file]:
  ml-threshold
  ml-threshold --negative
```

---

## Part 4: Feature Status Tracking

### test_status.txt (machine-readable)

Generated by `TestRunner.exe scan-features` which reads RAVEN2 source code and checks each operation.

```
# Format: FEATURE | IET_STATUS | RAVEN_STATUS | LAST_TEST_RESULT | LAST_TEST_DATE | MATCH%
threshold-dynamic | IMPLEMENTED | IMPLEMENTED | PASS | 2026-03-09 | 99.9998%
threshold-rdynamic | N/A | IMPLEMENTED | PASS | 2026-03-09 | 99.9998%
refine-threshold | IMPLEMENTED | IMPLEMENTED | PASS | 2026-03-09 | 99.999%
threshold-partial | IMPLEMENTED | IMPLEMENTED | PASS | 2026-03-09 | 99.998%
photostat-full | IMPLEMENTED | IMPLEMENTED | UNTESTED | - | -
photostat-partial | IMPLEMENTED | IMPLEMENTED | UNTESTED | - | -
whiteout | IMPLEMENTED | IMPLEMENTED | PASS | 2026-03-09 | 100.0%
whiteout-outside | IMPLEMENTED | STUB | SKIP | - | -
invert-region | IMPLEMENTED | IMPLEMENTED | PASS | 2026-03-09 | 100.0%
invert-full | IMPLEMENTED | IMPLEMENTED | PASS | 2026-03-09 | 100.0%
crop | IMPLEMENTED | IMPLEMENTED | PASS | 2026-03-09 | 100.0%
deskew | IMPLEMENTED | STUB | SKIP | - | -
despeckle | IMPLEMENTED | STUB | SKIP | - | -
line-remove-h | IMPLEMENTED | STUB | SKIP | - | -
line-remove-v | IMPLEMENTED | STUB | SKIP | - | -
border-detect | IMPLEMENTED | STUB | SKIP | - | -
rotate | IMPLEMENTED | IMPLEMENTED | PASS | 2026-03-09 | 100.0%
fine-rotation | IMPLEMENTED | PARTIAL | UNTESTED | - | -
ml1-threshold | IMPLEMENTED | STUB | SKIP | - | -
ml2-threshold | IMPLEMENTED | STUB | SKIP | - | -
mousewheel-contrast | WIRING | WIRING | PASS | 2026-03-09 | -
mousewheel-brightness | WIRING | WIRING | PASS | 2026-03-09 | -
batch-whiteout | IMPLEMENTED | IMPLEMENTED | UNTESTED | - | -
external-launch-2 | IMPLEMENTED | IMPLEMENTED | UNTESTED | - | -
external-launch-3 | IMPLEMENTED | IMPLEMENTED | UNTESTED | - | -
```

**Statuses:**
- `IMPLEMENTED` — real code exists
- `STUB` — method exists but is a no-op / returns dummy value
- `PARTIAL` — some code exists, not complete
- `WIRING` — tests the connection between UI and processing (not pixel comparison)

---

## Part 5: Harvest Script

### harvest.ps1

Pulls the latest code from RAVEN2 into the RAVENHarness project. Run this whenever you implement a new feature in RAVEN2.

```powershell
# harvest.ps1 — Pull latest RAVEN2 code into RAVENHarness
param(
    [string]$RavenPath = "C:\dev\RAVEN2",
    [string]$HarnessPath = "C:\dev\RavenTest\RAVENHarness"
)

$files = @(
    "RecoIPStubs.cs",
    "DynamicThreshold.cs",
    "RefineThreshold.cs",
    "OpenThresholdBridge.cs",
    "USVWinStubs.cs"
)

Write-Host "Harvesting from $RavenPath..."

foreach ($file in $files) {
    $src = Join-Path $RavenPath $file
    $dst = Join-Path $HarnessPath $file
    if (Test-Path $src) {
        Copy-Item $src $dst -Force
        Write-Host "  Copied: $file"
    } else {
        Write-Host "  MISSING: $file (not found in RAVEN2)"
    }
}

# Strip UI dependencies from OpenThresholdBridge.cs
$bridge = Get-Content (Join-Path $HarnessPath "OpenThresholdBridge.cs") -Raw
$bridge = $bridge -replace 'using System\.Windows\.Forms;', '// using System.Windows.Forms; // stripped for CLI'
# Comment out any MessageBox.Show calls
$bridge = $bridge -replace 'MessageBox\.Show\(', '// MessageBox.Show('
Set-Content (Join-Path $HarnessPath "OpenThresholdBridge.cs") $bridge

Write-Host ""
Write-Host "Harvest complete. Now run:"
Write-Host "  cd $HarnessPath"
Write-Host "  dotnet build"
Write-Host "  cd ..\TestRunner"
Write-Host "  TestRunner.exe scan-features"
Write-Host "  TestRunner.exe run-all"
```

### Adding New Features to Test

When you implement a new feature in RAVEN2 (e.g., deskew):

1. **Implement the feature** in RAVEN2's `RecoIPStubs.cs` (or new file)
2. **Run harvest:** `powershell .\harvest.ps1`
3. **Add the operation** to `RAVENHarness\Operations.cs` if it's a new command
4. **Run tests:** `TestRunner.exe run-feature deskew`
5. **Check status:** `TestRunner.exe report`

### Adding New Test Images

```
TestRunner.exe add-images C:\path\to\new\images
# This copies JPG/TIF pairs to TestImages\ and runs the full suite
```

---

## Part 6: Claude Instructions (README.md for the test project)

```markdown
# RavenTest — RAVEN2 vs IET Comparison Harness

## Quick Start (for Claude)

### Build everything:
```cmd
cd C:\dev\RavenTest
dotnet build IETHarness\IETHarness.csproj -c Release
dotnet build RAVENHarness\RAVENHarness.csproj -c Release
dotnet build TestRunner\TestRunner.csproj -c Release
```

### Run all tests:
```cmd
cd C:\dev\RavenTest
TestRunner\bin\Release\net8.0\TestRunner.exe run-all
```

### After implementing a RAVEN2 feature:
```cmd
cd C:\dev\RavenTest
powershell .\harvest.ps1
dotnet build RAVENHarness\RAVENHarness.csproj -c Release
TestRunner\bin\Release\net8.0\TestRunner.exe scan-features
TestRunner\bin\Release\net8.0\TestRunner.exe run-all
```

### Run specific feature tests:
```cmd
TestRunner.exe run-feature threshold
TestRunner.exe run-feature whiteout
TestRunner.exe run-feature deskew
```

### Check what's implemented vs stubbed:
```cmd
TestRunner.exe scan-features
type test_status.txt
```

### Add new test images:
```cmd
copy C:\path\to\new\*.jpg TestImages\
copy C:\path\to\new\*.tif TestImages\
TestRunner.exe run-all
```

### Read the report:
```cmd
type test_report.txt
```
```

---

## Part 7: Wiring Verification Tests

These tests don't compare pixel output between IET and RAVEN2. Instead, they verify that the RAVEN2 GUI correctly wires user actions to processing functions.

### What "wiring" means

When a user presses F2 in the GUI:
1. `ThresholdSettings` loads the F2 preset from `settings.ini`
2. Settings are passed to `ConversionSettings` object
3. `Form1.ProcessThreshold()` reads contrast/brightness/despeckle from settings
4. Those values are passed to `OpenThresholdBridge.ApplyThresholdToFile()` (for RDynamic)
5. Or to `RecoIP.ImgDynamicThresholdAverage()` (for Dynamic)

The wiring test verifies each step by:
1. Reading `settings.ini` directly and parsing F2-F6 presets
2. Calling the CLI harness with those exact parameters
3. Verifying the output matches what the GUI would produce

### Mousewheel wiring test

```
# Test: user has F2 active (contrast=248), scrolls mousewheel UP once
# Expected: contrast becomes 249, threshold re-applied with 249

RAVENHarness.exe threshold 0001.jpg out_248.tif --contrast 248 --brightness 180
RAVENHarness.exe threshold 0001.jpg out_249.tif --contrast 249 --brightness 180

# Verify out_249.tif differs from out_248.tif (the scroll had an effect)
# Verify the difference is exactly what changing contrast by 1 produces
```

### Settings wiring test

```
# Read settings.ini, parse F2 section
# Call harness with those exact values
# Verify the output matches what pressing F2 in the GUI would produce
```

---

## Part 8: Feature Implementation Checklist

### Currently Testable (RAVEN2 has real implementations)

| # | Feature | CLI Command | Expected Match |
|---|---------|-------------|---------------|
| 1 | Dynamic Threshold (full) | `threshold` | 99.97%+ |
| 2 | RDynamic Threshold (full) | `threshold` (RAVEN-only) | N/A (RAVEN-only) |
| 3 | Refine Threshold | `refine` | 99.97%+ |
| 4 | Partial Threshold | `threshold-partial` | 99.97%+ |
| 5 | Photostat (full, RDynamic) | `photostat` | 99.97%+ |
| 6 | Photostat (partial, RDynamic) | `threshold-partial --negative` | 99.97%+ |
| 7 | Invert (full) | `invert` | 100% |
| 8 | Invert (region) | `invert-region` | 100% |
| 9 | Crop | `crop` | 100% |
| 10 | Rotate (90/180/270) | `rotate` | 100% |
| 11 | Whiteout (EraseIn) | `whiteout` | 100% |
| 12 | Image I/O (load JPG, save TIF CCITT4) | implicit in all | 100% |
| 13 | Grayscale conversion | implicit in threshold | 100% |

### Stubbed — Will Become Testable

| # | Feature | CLI Command | Blocked By |
|---|---------|-------------|-----------|
| 14 | Deskew | `deskew` | RecoIPStubs.ImgDeskew is stub |
| 15 | Despeckle | `despeckle` | RecoIPStubs.ImgDespeckle is stub |
| 16 | Line Removal (H) | `line-remove-h` | Not in RecoIPStubs |
| 17 | Line Removal (V) | `line-remove-v` | RecoIPStubs.ImgRemoveVerticalLines is stub |
| 18 | Border Detection | `border-detect` | ImgFindBlackBorder* all stubbed |
| 19 | Whiteout Outside (EraseOut) | `whiteout-outside` | USVWinStubs.EraseOut is stub |
| 20 | Bleed-through removal | `debleed` | RecoIPStubs.ImgRemoveBleedThrough is stub |
| 21 | Black wire removal | `remove-wires` | RecoIPStubs.ImgRemoveBlackWires is stub |
| 22 | ML1 threshold | `ml-threshold` | ML logic not implemented |
| 23 | ML2 threshold | `ml-threshold` | ML logic not implemented |

### Wiring-Only Tests (no pixel comparison)

| # | Test | What It Verifies |
|---|------|-----------------|
| 24 | Mousewheel contrast ±N | Settings_MouseWheel adjusts contrast correctly |
| 25 | Mousewheel brightness ±N | Settings_MouseWheel adjusts brightness correctly |
| 26 | Mousewheel despeckle ±N | Settings_MouseWheel adjusts despeckle correctly |
| 27 | F2-F6 preset loading | settings.ini values reach the threshold function |
| 28 | NegativeImage flag | Photostat path triggers when NegativeImage=Y |
| 29 | Type=RDynamic routing | RDynamic type uses OpenThresholdBridge, not RecoIP |
| 30 | Type=Dynamic routing | Dynamic type uses RecoIP path |
| 31 | External launch (key 2) | PowerShell script 2.ps1 executes |
| 32 | External launch (key 3) | PowerShell script 3.ps1 executes |

---

## Part 9: Excluded Features

| Feature | Reason |
|---------|--------|
| CanadianSetLine | Per user request |
| Scanning/Printing | Not in RAVEN2 scope |
| IPS script execution | Proprietary, being replaced |
| Redaction | Not implemented in RAVEN2 |
| ConvertRestOfBook (bulk enhance) | Will be reimplemented later with current settings approach |

---

## Part 10: Build & Execution Environment

### Build (Windows)
Both harnesses and the test runner build on Windows with `dotnet build`:
```cmd
cd C:\dev\RavenTest
dotnet build --configuration Release
```

### Remote Testing via ns1-wsl
For remote execution via Tailscale SSH:
```bash
# From ai1-wsl (this machine)
ssh test@<ns1-wsl-tailscale-ip>

# On ns1-wsl, the Windows filesystem is at /mnt/c/
cd /mnt/c/dev/RavenTest

# Build (needs Windows dotnet in PATH)
/mnt/c/Program\ Files/dotnet/dotnet.exe build --configuration Release

# Run tests
./TestRunner/bin/Release/net8.0/TestRunner.exe run-all
```

### Refresh Workflow (for Claude)

When the user says "I implemented deskew in RAVEN2, test it":

```bash
# 1. Harvest latest code
powershell.exe -File C:\dev\RavenTest\harvest.ps1

# 2. Rebuild RAVENHarness
dotnet.exe build C:\dev\RavenTest\RAVENHarness\RAVENHarness.csproj -c Release

# 3. Scan for newly implemented features
C:\dev\RavenTest\TestRunner\bin\Release\net8.0\TestRunner.exe scan-features

# 4. Run tests for the new feature
C:\dev\RavenTest\TestRunner\bin\Release\net8.0\TestRunner.exe run-feature deskew

# 5. Run full suite to check for regressions
C:\dev\RavenTest\TestRunner\bin\Release\net8.0\TestRunner.exe run-all

# 6. Show results
type C:\dev\RavenTest\test_report.txt
type C:\dev\RavenTest\test_status.txt
```

When the user says "add these test images":
```bash
# Copy new images to TestImages folder
copy C:\path\to\new\images\*.jpg C:\dev\RavenTest\TestImages\
copy C:\path\to\new\images\*.tif C:\dev\RavenTest\TestImages\

# Re-run all tests with new images
C:\dev\RavenTest\TestRunner\bin\Release\net8.0\TestRunner.exe run-all
```

---

## Implementation Order

### Phase 1: Scaffolding
1. Create `C:\dev\RavenTest\` directory structure
2. Create all three .csproj files
3. Copy `RecoIPAPI.cs` + `recoip.dll` + license into IETHarness
4. Run `harvest.ps1` to pull RAVEN2 code into RAVENHarness
5. Implement `ImageComparer` in TestRunner
6. Verify both projects build

### Phase 2: Core Threshold Testing
7. Implement `threshold` command in both harnesses
8. Implement `threshold-partial` command in both harnesses
9. Implement `refine` command in both harnesses
10. Run comparisons on C:\temp\1a images
11. Verify 99.97%+ match

### Phase 3: Manipulation Testing
12. Implement `whiteout`, `invert`, `invert-region`, `crop`, `rotate` in both harnesses
13. Run comparisons — these should be 100% match (exact operations)

### Phase 4: Wiring Tests
14. Implement mousewheel simulation
15. Implement settings.ini parsing verification
16. Implement preset loading tests (F2-F6)

### Phase 5: Stub Feature Placeholders
17. Add all stubbed commands (deskew, despeckle, line-remove, etc.)
18. They return "STUB — not yet implemented" and update test_status.txt as SKIP
19. As features are implemented in RAVEN2, harvest + rebuild + re-test

### Phase 6: Advanced Features
20. Photostat full pipeline testing
21. ML1/ML2 threshold testing (when implemented)
22. External tool launch verification
23. Batch whiteout testing
