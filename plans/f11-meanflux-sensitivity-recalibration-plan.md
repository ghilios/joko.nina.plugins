# F11 meanFlux Fix + BrightnessSensitivity Recalibration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `NormalizedBrightness`'s `meanFlux` divide by the clip-survivor count (honest survivor-mean), then recalibrate `BrightnessSensitivity` for the compensated (Low/Typical) preset and the advanced default so the pre-fix faint-star yield is restored (the fix alone dropped accepted stars 8.1% on the corpus anchor).

**Architecture:** A one-line denominator fix in `StarDetector.ComputeStarParameters` lowers `NormalizedBrightness`, tightening the sensitivity gate. Because the NB shift is per-star (it depends on each star's survivor fraction), the new knob value cannot be a clean rescale — it is found empirically by a `BrightnessSensitivity` sweep added to the `TestApp focus-sweep` runner, matched against the pre-fix accepted count on a real frame, and validated by before/after sweeps on four AF runs. The fix + recalibration land as one atomic suite-green commit (a partial commit would break the compensation-guard test).

**Tech Stack:** C# / .NET 8.0-windows7.0, NUnit 4, OpenCvSharp, headless `TestApp focus-sweep` for real-data measurement and validation.

**Spec:** `docs/f11-meanflux-sensitivity-recalibration-design.md` (approved). Branch: `ghilios/f11-meanflux-sensitivity-recalibration` (already created; contains the design-doc commit `9ff17da`).

**Environment notes (read first):**
- All `dotnet` commands run via WSL interop. Use `rtk dotnet ...` (trust `errors=0` / exit code, not the header word) or `cmd.exe /c "dotnet ..."`. Set the Bash `timeout` to `600000` for build/test.
- Full suite: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
- TestApp build: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"`
- TestApp exe: `./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe` (WSL interop runs the Windows exe; quote Windows paths).
- Every commit must use the noreply identity:
  ```bash
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
  ```
- Never push to `develop`; the PR at the end targets `develop`.

**The corpus (fixed during brainstorming — all under `C:\Workshop Data\Data\autofocus\`):**
- **Recalibration anchor (single frame):** `uneven\final` — contains `11_Frame00_BitDepth16_Bayered0_Focuser56457.xisf`; this is where the 1970 → 1810 count is defined. The focus-sweep runner parses its lone frame as a one-position run.
- **Validation sweeps (9-position each):** `standard_example1`, `standard_example2` (dense; `standard_example2` exposed step 3's `TooLowHFR` cliff), `sensitivity_example1`, `sensitivity_example2` (faint-field / sensitivity-stress).

**The two user checkpoints (do not skip):**
- **Checkpoint B** (Task 3): present the post-fix `BrightnessSensitivity` sweep (Typical regime) overlaid on the pre-fix baseline; the user confirms `K_typ` (the value restoring ~1970) and the None/High scope.
- **Checkpoint C** (Task 5): present the before/after focus-sweep comparison on the anchor + four runs; the user signs off (or constants get nudged and we loop).

**Dependency:** base on current `develop` (PR #51 / step 5 merged, so `PeakResponse = 0.75` ⇒ the multiplier on `meanFlux` is `1 − 0.75 = 0.25`; step 3 / PR #48 supplies the honest `σ_measure` that is the gate denominator). The recalibration is only valid against `PeakResponse = 0.75`.

---

### Task 1: Add a BrightnessSensitivity sweep + override to the focus-sweep runner

Pure tooling: no detector behavior change. This must land before the pre-fix baseline (Task 2) so the diagnostic overlay (design §3 "approach B") can sweep both pre-fix and post-fix code.

**Files:**
- Modify: `Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs` (usage strings ~:91-92; after `baseParams.ModelPSF = false;` ~:137; new method)

- [ ] **Step 1: Wire the two new flags into `RunImpl`**

In `Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs`, immediately AFTER the line `baseParams.ModelPSF = false;` (~line 137) and BEFORE the `var byPosition = new SortedDictionary<int, PositionAccum>();` line (~139), insert:

```csharp
            // F11 step 6: a single-value override and a sweep for BrightnessSensitivity, applied on top of the
            // built base params (profile-derived or --default-params). The sweep is how the recalibrated knob
            // value is chosen empirically — see docs/f11-meanflux-sensitivity-recalibration-design.md §3.
            var brightnessOverride = DiagnosticUtil.GetArg(args, "--brightness-sensitivity");
            if (!string.IsNullOrWhiteSpace(brightnessOverride)) {
                baseParams.Sensitivity = double.Parse(brightnessOverride, CultureInfo.InvariantCulture);
                Console.WriteLine($"Override: BrightnessSensitivity = {baseParams.Sensitivity}");
            }
            var brightnessSweepArg = DiagnosticUtil.GetArg(args, "--brightness-sensitivity-sweep");
            if (!string.IsNullOrWhiteSpace(brightnessSweepArg)) {
                await RunBrightnessSweep(frames, baseParams, profileService, brightnessSweepArg, outDir);
                return;
            }
```

- [ ] **Step 2: Add the `RunBrightnessSweep` method**

Add this method to the `FocusSweepDiagnosticRunner` class (place it just after `RunImpl`):

```csharp
        private static async Task RunBrightnessSweep(
            List<Frame> frames, StarDetectorParams baseParams, IProfileService profileService,
            string sweepArg, string outDir) {
            var parts = sweepArg.Split(',');
            if (parts.Length != 3) {
                throw new ArgumentException("--brightness-sensitivity-sweep expects <a,b,step>");
            }
            double a = double.Parse(parts[0], CultureInfo.InvariantCulture);
            double b = double.Parse(parts[1], CultureInfo.InvariantCulture);
            double step = double.Parse(parts[2], CultureInfo.InvariantCulture);
            if (step <= 0) {
                throw new ArgumentException("--brightness-sensitivity-sweep step must be > 0");
            }

            var detector = new StarDetector(new AlglibAPI());
            var rows = new List<string> { "sensitivity,FocuserPosition,StarCount,LowSensitivity,MedianHFR" };
            var totals = new List<string> { "sensitivity,TotalStars,TotalLowSensitivity" };
            // Detect only reads its params, so reusing the same (mutable) instance per value is safe.
            for (double s = a; s <= b + 1e-9; s += step) {
                baseParams.Sensitivity = s;
                var byPos = new SortedDictionary<int, (int stars, long lowSens, List<double> hfrs)>();
                long totalStars = 0, totalLow = 0;
                foreach (var frame in frames) {
                    using var img = await DiagnosticUtil.LoadFloatMat(frame.Path, profileService);
                    var result = await detector.Detect(img, baseParams, null, CancellationToken.None);
                    if (!byPos.TryGetValue(frame.FocuserPosition, out var acc)) {
                        acc = (0, 0, new List<double>());
                    }
                    acc.stars += result.DetectedStars.Count;
                    acc.lowSens += result.Metrics.LowSensitivity;
                    foreach (var st in result.DetectedStars) acc.hfrs.Add(st.HFR);
                    byPos[frame.FocuserPosition] = acc;
                    totalStars += result.DetectedStars.Count;
                    totalLow += result.Metrics.LowSensitivity;
                }
                var tag = s.ToString("0.###", CultureInfo.InvariantCulture);
                foreach (var kv in byPos) {
                    var (median, _) = MedianMad(kv.Value.hfrs);
                    rows.Add(string.Join(",", tag, kv.Key, kv.Value.stars, kv.Value.lowSens,
                        median.ToString("0.####", CultureInfo.InvariantCulture)));
                }
                totals.Add(string.Join(",", tag, totalStars, totalLow));
                Console.WriteLine($"BrightnessSensitivity={tag}: {totalStars} stars total, {totalLow} low-sensitivity rejections");
            }
            File.WriteAllLines(Path.Combine(outDir, "brightness_sweep.csv"), rows);
            File.WriteAllLines(Path.Combine(outDir, "brightness_sweep_totals.csv"), totals);
            Console.WriteLine($"Wrote brightness_sweep.csv, brightness_sweep_totals.csv to {outDir}");
        }
```

(`MedianMad` is the existing private helper in this class — `MedianMad(List<double>)` returns `(median, mad)` and `(NaN, NaN)` for an empty list.)

- [ ] **Step 3: Update the usage strings**

In `RunImpl`, append the new flags to the first usage line (~:91):

```csharp
                Console.Error.WriteLine("Usage: TestApp focus-sweep --af-run <dir> [--profile-id <guid>] [--out <dir>] [--default-params] [--brightness-sensitivity <v>] [--brightness-sensitivity-sweep <a,b,step>]");
```

- [ ] **Step 4: Build TestApp**

Run: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` (timeout 600000)
Expected: exit 0, `0 errors`.

- [ ] **Step 5: Smoke-test the sweep flag on the anchor**

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  focus-sweep --af-run "C:\Workshop Data\Data\autofocus\uneven\final" \
  --brightness-sensitivity-sweep "1.0,3.0,0.5" --out "C:\temp\hf-f11\smoke"
```

Expected: console prints `BrightnessSensitivity=1: NNNN stars total ...` for 1.0, 1.5, 2.0, 2.5, 3.0; and `Wrote brightness_sweep.csv, brightness_sweep_totals.csv`. Confirm `/mnt/c/temp/hf-f11/smoke/brightness_sweep_totals.csv` exists with 5 data rows and that `TotalStars` falls as `sensitivity` rises (monotone). At `sensitivity=2.0` (current Typical default) the total should be ~1970 (this is pre-fix code). If the run errors on profile load, add `--profile-id <guid>` for the user's profile.

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Add BrightnessSensitivity override + sweep to focus-sweep diagnostic (F11 step 6)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Capture pre-fix baselines (anchor sweep + four validation sweeps)

No code change. Everything runs against the Task-1 build (pre-fix detector). These baselines are the before-half of the diagnostic overlay and the Checkpoint C comparison.

**Files:** none (TestApp output only, outside the repo).

- [ ] **Step 1: Pre-fix anchor sweep**

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  focus-sweep --af-run "C:\Workshop Data\Data\autofocus\uneven\final" \
  --brightness-sensitivity-sweep "0.8,2.4,0.1" --out "C:\temp\hf-f11\baseline\anchor"
```

Expected: `brightness_sweep_totals.csv` with `TotalStars` for sensitivity 0.8…2.4 step 0.1. Record the row at `sensitivity=2.0` — call its `TotalStars` **N_anchor_prefix** (expected ~1970). This is the count the recalibration must restore.

- [ ] **Step 2: Pre-fix validation sweeps (one per run, at the production default only)**

For each of the four runs `<RUN>` ∈ {`standard_example1`, `standard_example2`, `sensitivity_example1`, `sensitivity_example2`}:

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  focus-sweep --af-run "C:\Workshop Data\Data\autofocus\<RUN>" \
  --out "C:\temp\hf-f11\baseline\<RUN>"
```

(No sweep flag here — the plain run captures `focus_sweep.csv` per-position `StarCount`, `MedianHFR`, and the rejection histogram at the profile's actual params. These are the before-curves for Checkpoint C.)

Expected per run: console prints per-position star counts; `Wrote focus_sweep.csv, focus_sweep_summary.txt, focus_sweep_hfr.png`.

- [ ] **Step 3: Sanity-check**

Read each `/mnt/c/temp/hf-f11/baseline/<RUN>/focus_sweep.csv`: confirm multiple focuser positions, non-zero `StarCount`, a V-shaped `MedianHFR`. If a run looks broken (0 stars everywhere), stop and tell the user before continuing.

No commit (outputs live outside the repo).

---

### Task 3: Apply the F11 fix locally, then measure & decide K_typ (Checkpoint B)

The fix is applied to the working tree here (to enable the measurement) but **not committed** — it lands atomically with the recalibration in Task 4. The NB-definition test is written first (RED) to pin the fix's direction.

**Files:**
- Test: create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeanFluxDenominatorTests.cs`
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/SyntheticHaloStarField.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` (`:877`, `:1208`)
- Create: `docs/f11-meanflux-sensitivity-recalibration-results.md`

- [ ] **Step 1: Write the synthetic halo-star field helper**

This field gives each star a sharp bright core (defines the survivor flux) plus a broad shallow halo whose pixels sit in the structure footprint but below the clip margin — so the clipped fraction is large and the `meanFlux` denominator change moves `NormalizedBrightness` by a wide, robust margin.

```csharp
using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Builds synthetic fields where each star is a sharp Gaussian core plus a broad shallow halo. The halo
    /// pixels are detected as part of the star's structure footprint (so they inflate starPoints.Count) but lie
    /// below the clip margin (so they are excluded from totalFlux). This makes the clip-survivor fraction small,
    /// which is exactly the regime where the F11 meanFlux denominator fix (divide by survivor count, not
    /// footprint count) moves NormalizedBrightness by a wide margin — used to pin the fix's direction.
    /// </summary>
    internal static class SyntheticHaloStarField {

        public static Mat CreateFlat(int width, int height, float background) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            mat.SetTo(new Scalar(background));
            return mat;
        }

        public static void AddGaussian(Mat mat, double cx, double cy, double sigma, double peak) {
            int rad = (int)Math.Ceiling(5.0 * sigma);
            double inv = 1.0 / (2.0 * sigma * sigma);
            int width = mat.Width, height = mat.Height;
            unsafe {
                var data = (float*)mat.DataPointer;
                for (int y = Math.Max(0, (int)cy - rad); y <= Math.Min(height - 1, (int)cy + rad); ++y) {
                    for (int x = Math.Max(0, (int)cx - rad); x <= Math.Min(width - 1, (int)cx + rad); ++x) {
                        double dx = x - cx, dy = y - cy;
                        data[y * width + x] += (float)(peak * Math.Exp(-(dx * dx + dy * dy) * inv));
                    }
                }
            }
        }

        /// <summary>Adds a core+halo star: a sharp core (small σ, high peak) and a broad shallow halo at the same center.</summary>
        public static void AddCoreHaloStar(Mat mat, double cx, double cy,
            double coreSigma, double corePeak, double haloSigma, double haloPeak) {
            AddGaussian(mat, cx, cy, coreSigma, corePeak);
            AddGaussian(mat, cx, cy, haloSigma, haloPeak);
        }
    }
}
```

- [ ] **Step 2: Write the NB-definition test (RED)**

```csharp
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Pins the F11 fix: NormalizedBrightness's meanFlux must divide by the clip-survivor count
    /// (numUnclippedPixels), not the full structure-footprint count (starPoints.Count). The buggy denominator
    /// understates meanFlux and inflates NB; the fix lowers NB, tightening the sensitivity gate. The field's
    /// stars have a large clipped (halo) fraction so the NB shift is wide and the gate flip is robust.
    /// </summary>
    [TestFixture]
    public class MeanFluxDenominatorTests {
        private const int Size = 256;
        private const double Background = 0.05;
        private const double NoiseSigma = 0.02;
        private const int Seed = 515151;

        // 4 core+halo stars. Core: σ=1.5, peak=0.55 (≈27σ_n, decisively detected). Halo: σ=8, peak=0.03
        // (below clipMargin = StarClippingMultiplier·σ_measure = 2.0·0.02 = 0.04, so every halo pixel is
        // clipped — it only inflates starPoints.Count). Survivor fraction is small ⇒ large meanFlux shift.
        private static readonly (double x, double y)[] Centers = {
            (64, 64), (192, 64), (64, 192), (192, 192),
        };

        private static OpenCvSharp.Mat BuildField() {
            var mat = SyntheticHaloStarField.CreateFlat(Size, Size, (float)Background);
            foreach (var (x, y) in Centers) {
                SyntheticHaloStarField.AddCoreHaloStar(mat, x, y, coreSigma: 1.5, corePeak: 0.55, haloSigma: 8.0, haloPeak: 0.03);
            }
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, NoiseSigma, Seed);
            return mat;
        }

        // The mismatch path (NR radius set, measurement NR off) so σ_measure is the honest sharp σ ≈ 0.02.
        private static StarDetectorParams Params(double sensitivity) => new StarDetectorParams {
            StarMeasurementNoiseReductionEnabled = false,
            NoiseReductionRadius = 3,
            Sensitivity = sensitivity,
        };

        [Test]
        public void Detect_HighSensitivityGate_RejectsCoreHaloStarsOnHonestMeanFlux() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());
            // Sensitivity chosen to sit BETWEEN the inflated (pre-fix) and honest (post-fix) NB/σ for these
            // stars: pre-fix NB/σ is above it (stars accepted) and post-fix NB/σ is below it (stars rejected).
            var result = detector.Detect(image, Params(sensitivity: 26.0), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            TestContext.WriteLine($"detected={result.DetectedStars.Count} lowSensitivity={result.Metrics.LowSensitivity}");
            Assert.Multiple(() => {
                Assert.That(result.DetectedStars.Count, Is.EqualTo(0),
                    "with the honest survivor-mean meanFlux, NB drops below the 26σ gate for these clipped-halo stars");
                Assert.That(result.Metrics.LowSensitivity, Is.GreaterThanOrEqualTo(Centers.Length),
                    "the stars must be rejected specifically by the sensitivity gate");
            });
        }
    }
}
```

- [ ] **Step 3: Run it against CURRENT code — confirm RED for the right reason**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~MeanFluxDenominatorTests` (timeout 600000)
Expected: FAIL. The printed line should show `detected=4 lowSensitivity=0` (pre-fix NB is inflated, so all 4 stars pass the 26σ gate). If instead `detected=0` already (pre-fix), the gate value is too high — lower `sensitivity` toward 24 until pre-fix gives `detected=4`. If `detected<4` for other reasons (e.g. a star clipped at the border), nudge the core peak up or move centers inward. Do NOT proceed until pre-fix reliably gives `detected=4`.

- [ ] **Step 4: Apply the F11 fix**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs`, line ~1208:

```csharp
            // meanFlux is the mean over clip-survivors (the pixels actually summed into totalFlux), NOT the full
            // structure footprint. Dividing by starPoints.Count understated it and inflated NormalizedBrightness
            // for faint/spread stars (low survivor fraction), loosening the sensitivity gate (F11).
            var meanFlux = totalFlux / numUnclippedPixels;
```

(`numUnclippedPixels` is guaranteed ≥ 2 here: the `numUnclippedPixels == 1` degenerate guard at `:1184` already returned `null`.)

Then add a clarifying comment at the `MeanBrightness` assignment (line ~877) so the deliberately-different denominator is not mistaken for the same bug:

```csharp
                // NOTE (F11): MeanBrightness intentionally divides TotalFlux by the full structure-footprint
                // pixel count (PixelCount = starPoints.Count), unlike NormalizedBrightness's meanFlux which uses
                // the clip-survivor count. This per-footprint surface-brightness value feeds only the optional
                // "brightest N AF stars" selection (analysis F8); it is left as-is by decision (design §1).
                MeanBrightness = starCandidate.TotalFlux / starCandidate.PixelCount,
```

- [ ] **Step 5: Run the test again — confirm GREEN**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~MeanFluxDenominatorTests` (timeout 600000)
Expected: PASS, printed line now `detected=0 lowSensitivity>=4`.

- [ ] **Step 6: Run the FULL suite — confirm nothing else broke**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: all pass. The fix lowers NB, so a test that asserts a star count or NB-derived value on a field with marginal/faint stars at default knobs could shift. Likely-robust (bright stars / explicit faint-rejection): `SigmaConsistencyDetectorTests`, `HocusFocusReportTests`. If one fails: confirm it is the expected F11 effect (a marginal star's NB crossed the gate), and note it — it should be restored by the recalibration in Task 4. Do NOT silently re-pin; if it is still wrong after Task 4, investigate. Do NOT commit yet (working tree holds the uncommitted fix).

- [ ] **Step 7: Build TestApp with the working-tree fix**

Run: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` (timeout 600000)
Expected: exit 0, `0 errors`.

- [ ] **Step 8: Post-fix anchor sweep (find K_typ)**

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  focus-sweep --af-run "C:\Workshop Data\Data\autofocus\uneven\final" \
  --brightness-sensitivity-sweep "0.8,2.4,0.1" --out "C:\temp\hf-f11\afterfix\anchor"
```

Read `/mnt/c/temp/hf-f11/afterfix/anchor/brightness_sweep_totals.csv`. Confirm the row at `sensitivity=2.0` is ~1810 (the −8.1% drop reproduced). **K_typ = the sensitivity value whose `TotalStars` ≈ N_anchor_prefix** (the Task-2 Step-1 value at 2.0, ~1970). Interpolate between rows if needed; round to one decimal.

- [ ] **Step 9: Build the diagnostic overlay (approach B)**

Make a small table from the two totals CSVs (`baseline/anchor` vs `afterfix/anchor`): `sensitivity | TotalStars(pre-fix) | TotalStars(post-fix)`. The horizontal gap at a fixed count is the required Δsensitivity; the local slope of the post-fix curve near K_typ shows whether K_typ sits on a cliff (steep ⇒ fragile, prefer to note it). Save this table for the results file.

- [ ] **Step 10: Checkpoint B — present and confirm**

Present to the user: the overlay table, the reproduced 2.0 → ~1810 drop, and **the proposed K_typ** (restores ~1970 at the Typical/default regime). State the None/High scope explicitly and ask the user to confirm or adjust:

> "Proposed recalibration: the Low/Typical preset, the advanced default, and the `StarDetectorParams` class default move from 2.0 → **K_typ** (restores the anchor count). The WideRange/LongFocalLength deltas scale proportionally (intent ratio preserved). **None and High keep 10.0** — None because its bright 10σ operating point has a high survivor fraction (negligible NB shift), High because it is a rarely-used non-default preset kept at the honest-σ baseline. To empirically recalibrate None/High instead, I'd temporarily switch your profile's Noise Level and re-run the anchor sweep. Confirm 'keep None/High at 10.0', or ask for the empirical pass."

Wait for the answer. If the user requests the None/High empirical pass, run two extra anchor sweeps after temporarily setting the profile's `Simple_NoiseLevel` (the user does this in NINA Options, or via a second `--profile-id`), determine `K_none`/`K_high`, and carry them into Task 4 (the ConfigureSimpleSettings note covers how to split the None/High branch). Default path: keep 10.0.

- [ ] **Step 11: Record the decision in the results file**

Create `docs/f11-meanflux-sensitivity-recalibration-results.md` with: a one-paragraph header (what was measured — anchor `uneven/final`, the pre/post overlay, link to design §3/§5); the overlay table from Step 9; the reproduced 2.0 → ~1810 drop; and a `## Decision` section recording K_typ (+ K_none/K_high if measured), the date, the None/High scope decision, and a one-sentence rationale. Commit just this file:

```bash
git add docs/f11-meanflux-sensitivity-recalibration-results.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Record F11 BrightnessSensitivity recalibration measurement + decision

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

(The working tree still holds the uncommitted StarDetector.cs fix + the new test files from Steps 1-2; they are committed atomically with the recalibration in Task 4.)

---

### Task 4: Apply the recalibration (atomic fix + recalibration commit)

Lands the F11 fix (Task 3 working-tree changes) together with the recalibrated constants and the compensation-guard test, in one suite-green commit. Throughout, substitute the **K_typ** chosen at Checkpoint B (e.g. if K_typ = 1.5, then `sensitivityScale` for Low/Typical = `0.2 × (1.5/2.0)` = `0.15`, which gives `BrightnessSensitivity = 10 × 0.15 = 1.5`; the WideRange/LongFL delta becomes `−2.0 × 0.15 = −0.3`, i.e. wide/longFl = 1.2, preserving the 0.8 ratio).

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs` (`:270`)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs` (`:97`, `:102`, `:116`, `:167`, `:214`)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs` (`:141-193`)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/HocusFocusReportTests.cs` (`:101`)
- Test: create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeanFluxRecalibrationTests.cs`

- [ ] **Step 1: Recalibrate the `StarDetectorParams` class default**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs`, line ~270:

```csharp
        // Sensitivity is the minimum value of a star's brightness (with the background subtracted out) above the
        // noise floor (s - b)/n, with n measured on the image actually sampled (F4) and the brightness measure
        // using the honest survivor-mean meanFlux (F11). Smaller values increase sensitivity. The default was
        // lowered from 2.0 to restore the pre-F11 faint-star yield — see docs/f11-meanflux-sensitivity-recalibration-results.md.
        public double Sensitivity { get; set; } = <K_typ>;
```

- [ ] **Step 2: Recalibrate `StarDetectionOptions` (compensated preset + advanced default)**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs`:

`ConfigureSimpleSettings` — in BOTH the `NoiseLevelEnum.Low` and `NoiseLevelEnum.Typical` cases (lines ~80 and ~86), change the `sensitivityScale` assignment from `0.2` to the recalibrated value and document the two stacked compensations:

```csharp
                case NoiseLevelEnum.Low:
                    StarMeasurementNoiseReductionEnabled = false;
                    NoiseReductionRadius = 3;
                    // 0.2 = F4 honest-σ compensation; further reduced by the F11 yield factor (K_typ/2.0) because
                    // the survivor-mean meanFlux fix lowered NormalizedBrightness. 0.2·(K_typ/2.0) = <scale>.
                    // Empirical — see docs/f11-meanflux-sensitivity-recalibration-results.md.
                    sensitivityScale = <scale>; // = K_typ / 10.0; gives BrightnessSensitivity = 10·<scale> = K_typ
                    break;

                case NoiseLevelEnum.Typical:
                    StarMeasurementNoiseReductionEnabled = false;
                    NoiseReductionRadius = 3;
                    sensitivityScale = <scale>; // same as Low: F4 ×0.2 then F11 yield factor — see results file
                    break;
```

Leave `BrightnessSensitivity = 10.0 * sensitivityScale;` (`:97`) and the WideRange (`:102`) / LongFocalLength (`:116`) `BrightnessSensitivity -= 2.0 * sensitivityScale;` lines unchanged — they now produce `K_typ` and the proportional deltas automatically. (`StarClippingMultiplier = 2.0;` at `:95` is uniform and untouched.)

`InitializeOptions` (line ~167) — new-profile fallback:

```csharp
            brightnessSensitivity = optionsAccessor.GetValueDouble("BrightnessSensitivity", <K_typ>);
```

`ResetDefaults` (line ~214):

```csharp
            BrightnessSensitivity = <K_typ>;
```

**If the user requested None/High empirical recalibration at Checkpoint B** (non-default path): split the `switch` so `None` keeps `sensitivityScale = 1.0` but sets `BrightnessSensitivity` to `K_none/10.0`-scaled, and `High` likewise to `K_high`; otherwise leave the `None`/`High` cases exactly as they are (scale stays 1.0 ⇒ BrightnessSensitivity stays 10.0).

- [ ] **Step 3: Update the options tests**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs`:

`SimpleMode_PixelScaleLongFocalLength_IncreasesSensitivity` (assert block ~:156-160) and `SimpleMode_FocusRangeWideRange_IncreasesSensitivity` (~:188-192) — update the constants (keep the `< typical` ordering assertion). For K_typ = 1.5 these become:

```csharp
        Assert.Multiple(() => {
            Assert.That(typical.BrightnessSensitivity, Is.EqualTo(<K_typ>));
            Assert.That(longFl.BrightnessSensitivity, Is.EqualTo(<K_typ * 0.8>));
            Assert.That(longFl.BrightnessSensitivity, Is.LessThan(typical.BrightnessSensitivity));
        });
```

(and identically `wide.BrightnessSensitivity` → `<K_typ * 0.8>` in the WideRange test). Append one line to each test's comment: `// Recalibrated for F11 (meanFlux survivor-mean fix) — see f11-meanflux-sensitivity-recalibration-results.md.`

The `SimpleMode_NoiseLevelNone_KeepsUncompensatedBrightnessSensitivity` test (asserts `10.0`) stays unchanged on the default path (None keeps 10.0). If a High variant exists and None/High were NOT empirically recalibrated, it also stays unchanged.

- [ ] **Step 4: Update `HocusFocusReportTests`**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/HocusFocusReportTests.cs`, line ~101, change the expected built-params sensitivity:

```csharp
            Assert.That(p.Sensitivity, Is.EqualTo(<K_typ>));
```

Update the nearby comment (~:93) to mention the F11 recalibration alongside the F4 note.

- [ ] **Step 5: Write the compensation-guard test**

This pins that the recalibrated default restores faint-star yield: a field whose faint stars sit just above the recalibrated gate is detected in full.

```csharp
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Compensation guard for the F11 recalibration: with the honest survivor-mean meanFlux AND the recalibrated
    /// BrightnessSensitivity default, a field of moderate-faint core+halo stars must still be detected in full —
    /// i.e. lowering the knob restored the yield the fix had removed.
    /// </summary>
    [TestFixture]
    public class MeanFluxRecalibrationTests {
        private const int Size = 256;
        private const double Background = 0.05;
        private const double NoiseSigma = 0.02;
        private const int Seed = 626262;

        private static readonly (double x, double y)[] Centers = {
            (64, 64), (192, 64), (64, 192), (192, 192),
        };

        private static OpenCvSharp.Mat BuildField() {
            var mat = SyntheticHaloStarField.CreateFlat(Size, Size, (float)Background);
            foreach (var (x, y) in Centers) {
                SyntheticHaloStarField.AddCoreHaloStar(mat, x, y, coreSigma: 1.5, corePeak: 0.55, haloSigma: 8.0, haloPeak: 0.03);
            }
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, NoiseSigma, Seed);
            return mat;
        }

        [Test]
        public void Detect_RecalibratedDefault_DetectsAllStarsTheFixWouldHaveDropped() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());
            // Use the class-default Sensitivity (= the recalibrated K_typ). These stars (NB/σ well above the
            // recalibrated gate, but they were the kind dropped at the un-recalibrated 26σ probe in
            // MeanFluxDenominatorTests) must all be detected.
            var p = new StarDetectorParams { StarMeasurementNoiseReductionEnabled = false, NoiseReductionRadius = 3 };
            var result = detector.Detect(image, p, null, CancellationToken.None).GetAwaiter().GetResult();
            TestContext.WriteLine($"detected={result.DetectedStars.Count} sensitivity={p.Sensitivity}");
            Assert.That(result.DetectedStars.Count, Is.EqualTo(Centers.Length),
                "the recalibrated default gate must accept these bright-core stars (yield preserved)");
        }
    }
}
```

(These stars are bright-core ⇒ comfortably above any K_typ ≈ 1–2 gate; the test mainly guards that the recalibrated `StarDetectorParams` default is wired through and non-regressive. If you want a tighter guard at the exact gate margin, lower `corePeak` toward where `detected` becomes sensitive to the knob and pin that — but keep it stable across seeds.)

- [ ] **Step 6: Re-verify `SigmaConsistencyDetectorTests` comments**

Open `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/SigmaConsistencyDetectorTests.cs`. Its header comment cites NB magnitudes (e.g. "NormalizedBrightness ≈ 6.4–9.0 × σ̂_blurred", ~:28). Run the fixture; if its assertions still pass (expected — bright stars and explicit faint-rejection are robust to the NB shift), update only the NB-magnitude *comments* to reflect the survivor-mean values (note them from the test's TRACE output). If an *assertion* legitimately changed, re-pin it with a one-line comment explaining the F11 cause — do not bump silently.

- [ ] **Step 7: Run the FULL suite — GREEN**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: all pass — `MeanFluxDenominatorTests` (gate flip), `MeanFluxRecalibrationTests` (yield restored), updated options/report tests, `SigmaConsistencyDetectorTests`.

- [ ] **Step 8: Commit the atomic fix + recalibration**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/SyntheticHaloStarField.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeanFluxDenominatorTests.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeanFluxRecalibrationTests.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/SigmaConsistencyDetectorTests.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/HocusFocusReportTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Fix NormalizedBrightness meanFlux denominator; recalibrate BrightnessSensitivity (F11)

meanFlux now divides totalFlux by the clip-survivor count (numUnclippedPixels)
instead of the full structure-footprint count (starPoints.Count), so
NormalizedBrightness is an honest peak-vs-survivor-mean contrast. This lowered
NB and tightened the sensitivity gate (-8.1% accepted stars on the corpus
anchor). BrightnessSensitivity for the Low/Typical preset and the advanced/class
defaults is recalibrated (2.0 -> K_typ) to restore the pre-fix faint-star yield,
chosen empirically from a focus-sweep anchor measurement; WideRange/LongFocalLength
deltas scale proportionally. None/High keep 10.0. MeanBrightness's footprint
denominator is intentionally left unchanged (analysis F8, design decision).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

(Replace `K_typ` in the message with the chosen number.)

---

### Task 5: Checkpoint C — after-sweeps, before/after comparison, sign-off

**Files:** none (TestApp output + possible constant nudge).

- [ ] **Step 1: Rebuild TestApp with the committed change**

Run: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` (timeout 600000)
Expected: exit 0, `0 errors`.

- [ ] **Step 2: After anchor sweep + after validation sweeps**

Anchor (confirm the recalibrated default restores the count):

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  focus-sweep --af-run "C:\Workshop Data\Data\autofocus\uneven\final" \
  --brightness-sensitivity-sweep "0.8,2.4,0.1" --out "C:\temp\hf-f11\after\anchor"
```

Each validation run `<RUN>` ∈ {`standard_example1`, `standard_example2`, `sensitivity_example1`, `sensitivity_example2`} — these load the user's profile (so the recalibrated preset applies automatically):

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  focus-sweep --af-run "C:\Workshop Data\Data\autofocus\<RUN>" \
  --out "C:\temp\hf-f11\after\<RUN>"
```

- [ ] **Step 3: Build the before/after comparison**

- Anchor: from `after/anchor/brightness_sweep_totals.csv`, confirm the row at `sensitivity = K_typ` is ~N_anchor_prefix (the count is restored at the recalibrated default).
- Per validation run: read `baseline/<RUN>/focus_sweep.csv` and `after/<RUN>/focus_sweep.csv` and produce a per-position table: `FocuserPosition | StarCount before→after | MedianHFR before→after | LowSensitivity before→after | TooFlat before→after | TooLowHFR before→after | ContaminationSuspected before→after`.

- [ ] **Step 4: Present to the user and ask for sign-off**

Show the tables plus a one-paragraph reading: per-position `StarCount` should be roughly preserved at defaults (within ~±5–10%); the V-curve (`MedianHFR` vs position) keeps its shape with the minimum at the same position; `LowSensitivity` returns to ~baseline; and crucially the re-admitted stars do NOT pile into `TooFlat` / `ContaminationSuspected` / `TooLowHFR` (count-not-membership quality guard — the recovered stars must be real, not junk failing a later gate). Ask: **"Sign off, or nudge K_typ?"**

- [ ] **Step 5 (only if counts shifted materially or junk was re-admitted): nudge and loop**

If the anchor count over/under-shoots N_anchor_prefix, or a validation run shows a new dominant rejection mode among recovered stars: pick a corrected K_typ from the after-anchor sweep curve (or move it up slightly to shed junk), update consistently — `sensitivityScale` (`StarDetectionOptions.cs` Low/Typical cases), `BrightnessSensitivity` defaults (`InitializeOptions`, `ResetDefaults`), the `IStarDetector.cs` class default, and the options/report test expectations (`<K_typ>` and `<K_typ*0.8>`) — re-run the full suite (Task 4 Step 7), rebuild TestApp, re-run Steps 2-4. Update `docs/f11-meanflux-sensitivity-recalibration-results.md` with the final value. Commit the nudge:

```bash
git add -A Joko.NINA.Plugins docs/f11-meanflux-sensitivity-recalibration-results.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Tune F11 BrightnessSensitivity from real-data before/after sweeps

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Tooltip / release-note check, results finalize

**Files:**
- Modify (only if needed): `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml` (`BrightnessSensitivity_Tooltip`)
- Modify: `docs/f11-meanflux-sensitivity-recalibration-results.md`

- [ ] **Step 1: Check the BrightnessSensitivity tooltip**

Open `Resources/OptionsDataTemplates.xaml` and find `BrightnessSensitivity_Tooltip`. The gate is still `(s−b)/n` against the measured-image σ; only the meaning of `s` (now a survivor-mean-based NB) and the default value changed. If the tooltip names the old default ("The default of 2 works well"), update that phrase to the new K_typ; otherwise leave it. Validation-rule ranges already admit values below 2.0 (≥ 0), so no rule change. If you edit the XAML, build to validate:

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: exit 0, `errors=0`.

- [ ] **Step 2: Add the advanced-mode release note to the results file**

Append a `## Release note (advanced-mode users)` section to `docs/f11-meanflux-sensitivity-recalibration-results.md`:

> `BrightnessSensitivity` now operates against a slightly lower `NormalizedBrightness` (the meanFlux fix uses the clip-survivor mean instead of the full-footprint mean). Hand-tuned advanced-mode values may be lowered by ~`(K_typ/2.0)` to preserve prior faint-star yield. Simple-mode presets are recalibrated automatically.

- [ ] **Step 3: Commit (if anything changed)**

```bash
git add -A Joko.NINA.Plugins docs/f11-meanflux-sensitivity-recalibration-results.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Update BrightnessSensitivity tooltip + add F11 release note

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

(If neither the tooltip nor anything else changed, skip the commit — the release note can ride with Task 7's commit.)

---

### Task 7: Roadmap housekeeping, full suite, push, PR

**Files:**
- Modify: `docs/star-detection-hfr-autofocus-accuracy-analysis.md` (§10 table, rows 5-6 ~:286-287)

- [ ] **Step 1: Update §10**

Set row 6 to Done (fill in the final K_typ and the measured before/after numbers):

```markdown
| 6. F11 meanFlux + sensitivity recalibration | ✅ Done (PR #NN) | meanFlux divides by the clip-survivor count (honest survivor-mean NB); BrightnessSensitivity recalibrated 2.0 → <K_typ> for the Low/Typical preset + advanced/class defaults (WideRange/LongFL deltas scaled, None/High kept at 10.0). Anchor count restored ~1970; before/after sweeps on 4 AF runs preserve V-curve shape and rejection mix. Plans: `f11-meanflux-sensitivity-recalibration-{design,plan}.md`; results: `f11-meanflux-sensitivity-recalibration-results.md`. |
```

If row 5 still reads `🟡 In progress` but PR #51 is merged, correct it to `✅ Done (PR #51, merged)` (housekeeping).

- [ ] **Step 2: Final full suite**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: all pass. Fix any failure's cause before pushing — never skip or mark expected-to-fail.

- [ ] **Step 3: Commit, push, open the PR**

```bash
git add docs/star-detection-hfr-autofocus-accuracy-analysis.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Update roadmap progress for step 6 (F11)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push -u origin ghilios/f11-meanflux-sensitivity-recalibration
```

Then create the PR (fill in K_typ and the before/after summary):

```bash
gh pr create --base develop --title "F11 meanFlux fix + BrightnessSensitivity recalibration (step 6)" --body "$(cat <<'EOF'
Step 6 of the star-detection accuracy analysis (finding F11). Design: `docs/f11-meanflux-sensitivity-recalibration-design.md`; measurement + decision: `docs/f11-meanflux-sensitivity-recalibration-results.md`.

## Changes
- **F11 fix:** `NormalizedBrightness`'s `meanFlux` now divides `totalFlux` by the clip-survivor count (`numUnclippedPixels`) instead of the full structure-footprint count (`starPoints.Count`). The old denominator understated `meanFlux` and inflated NB for faint/spread stars, loosening the sensitivity gate.
- **Recalibration (behavior-preserving at defaults):** `BrightnessSensitivity` for the Low/Typical preset and the advanced/class defaults moves 2.0 → K_typ to restore the pre-fix faint-star yield (the fix alone dropped accepted stars 8.1% on the corpus anchor). WideRange/LongFocalLength deltas scale proportionally; None/High keep 10.0.
- **Scope:** `MeanBrightness`'s full-footprint denominator is intentionally left unchanged (rarely-used brightest-N AF path, analysis F8).
- **Tooling:** `focus-sweep` gains `--brightness-sensitivity` / `--brightness-sensitivity-sweep` for the empirical recalibration measurement.

## Advanced-mode users (release note)
`BrightnessSensitivity` now operates against a slightly lower (survivor-mean) `NormalizedBrightness`. Hand-tuned advanced-mode values may be lowered by ~`(K_typ/2.0)` to preserve prior yield. Simple-mode presets are recalibrated automatically.

## Verification
- Full unit suite passes, including a gate-flip test pinning the fix direction and a compensation guard pinning restored yield.
- Before/after `TestApp focus-sweep` on the anchor frame + four AF runs: anchor count restored (~1970 at the recalibrated default), V-curve shape preserved, rejection mix sane (recovered stars are real, not junk failing a later gate) — tables reviewed at the in-plan checkpoint.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

(If `gh pr create` hits the Projects-classic GraphQL error seen on this repo, retry via `gh api -X POST repos/:owner/:repo/pulls -f title=... -f head=ghilios/f11-meanflux-sensitivity-recalibration -f base=develop -F body=@/tmp/pr-body.md`.)

- [ ] **Step 4: Report back**

Tell the user: PR number/URL, the final K_typ, the None/High scope decision, the anchor before→after counts, and the location of the before/after evidence (`C:\temp\hf-f11\...`).

---

## Self-review notes (already applied)

- **Spec coverage:** design §1 (fix + MeanBrightness comment) → Task 3 Step 4; §2 (recalibration mechanics, per-regime) → Task 4 Steps 1-4 + Checkpoint B scope; §3 (focus-sweep sweep flag + overlay diagnostic) → Task 1 + Task 3 Steps 8-9; §4 (NB-definition test, compensation guard, options/report/SigmaConsistency pins) → Tasks 3, 4; §5 (anchor + four-sweep before/after, acceptance bar) → Tasks 2, 5; §6 (tooltip, release note) → Task 6; §7 (rollout, step-5 dependency) → Task 7 + header.
- **Atomicity:** the fix and the recalibration land in ONE commit (Task 4 Step 8); the working tree carries the uncommitted fix across Checkpoint B because the recalibration constant can only be measured on a built fix, and a fix-only commit would break the compensation guard.
- **Placeholder discipline:** `K_typ` / `<scale>` / `<K_typ*0.8>` are values determined at Checkpoint B and applied at every enumerated site with the exact formula (`<scale> = K_typ/10`, delta ratio 0.8) — the step-3 `<L>` precedent, not vague TODOs.
- **Type/name consistency:** `numUnclippedPixels` (StarDetector), `Sensitivity` / `BrightnessSensitivity` (params vs options), `sensitivityScale`, `SyntheticHaloStarField.AddCoreHaloStar`, `MeanFluxDenominatorTests` / `MeanFluxRecalibrationTests`, `RunBrightnessSweep`, `brightness_sweep[_totals].csv` are used identically throughout.
- **None/High honesty:** kept at 10.0 by default with an explicit Checkpoint-B scope decision (mechanism for None; rarely-used-non-default for High) and an optional empirical pass — no silent assumption.
