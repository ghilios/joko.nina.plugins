# Weight-Chain Hygiene (F5a/F5b/F6) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bound the influence any single focus point can gain from a degenerate σ, fix the multi-frame σ pooling bugs, and put the reduced-χ² gate on an honest, documented footing.

**Architecture:** A new `WeightRegularization` static class produces regularized *copies* of `ScatterErrorPoint` lists at every fit entry point (our four hyperbolic models, Grubbs, and NINA core's 1/ErrorY²-weighted `Trendline`/`QuadraticFitting`), while raw measured σ keeps flowing to reports and charts. `CvImageUtility.AverageMeasurement` is rewritten for valid-σ-only SEM pooling. The χ² gate gets corrected documentation plus an empirical calibration pass using the existing `TestApp fit-quality` harness.

**Tech Stack:** C# / .NET 8.0-windows, NUnit 4.4 (Tests project references the plugin project; `InternalsVisibleTo` is already in place), alglib LM fitting, OxyPlot `ScatterErrorPoint`.

**Design doc:** `plans/weight-chain-hygiene-design.md` (approved 2026-06-12). Read it first.

---

## Context for the implementer

- **Branch:** work on `ghilios/weight-chain-hygiene` (already created off `develop`; the design doc is committed on it). Never push to `develop` directly — finish via PR.
- **Build/test from the repo root (WSL → Windows interop):**
  - Full suite: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (set Bash timeout 600000; trust `errors=0`/exit code, not rtk's `fail` header word).
  - Single fixture (clearer per-test output): `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~<FixtureName>"` (timeout 600000).
- **Commit author (required — GitHub email privacy):**
  ```bash
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
  ```
- **Key fact:** NINA core's `Trendline` and `QuadraticFitting` weight by `1/ErrorY²` (verified in NINA source) and cannot be modified — they must receive regularized point copies. `GaussianFitting` is unweighted (regularization is a harmless no-op for it).
- **`MeasureAndError`** is a NINA struct from `NINA.WPF.Base.ViewModel.AutoFocus` (`{ double Measure; double Stdev; }`); `AverageMeasurement` is our extension method on `List<MeasureAndError>` in `CvImageUtility.cs`.
- **`ScatterErrorPoint`** is OxyPlot's; constructor `(x, y, errorX, errorY)`.

---

### Task 1: Roadmap §10 update (rows 3 and 4)

**Files:**
- Modify: `plans/star-detection-hfr-autofocus-accuracy-analysis.md` (§10 table, lines ~284-285)

- [ ] **Step 1: Update row 3 to Done and row 4 to In progress**

Row 3: change the status cell `🟡 In review` to `✅ Done (PR #48, merged)`. Keep the Notes cell unchanged.

Row 4: replace the entire row

```markdown
| 4. Weight-chain hygiene (F5/F6) | ⬜ Not started | ErrorY floor/cap, invalid-σ accumulation fix, √FramesPerPoint, χ² gate. |
```

with

```markdown
| 4. Weight-chain hygiene (F5/F6) | 🟡 In progress | ErrorY regularization (median floor at fit entry, protecting NINA-core 1/σ² fitters too), invalid-σ accumulation fix, √FramesPerPoint SEM pooling, χ² gate calibration. Plans: `weight-chain-hygiene-{design,plan}.md`. |
```

- [ ] **Step 2: Commit**

```bash
git add plans/star-detection-hfr-autofocus-accuracy-analysis.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Update roadmap: step 3 done (PR #48), step 4 in progress

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `WeightRegularization` (F5a core)

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/WeightRegularization.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/WeightRegularizationTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;
using OxyPlot.Series;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class WeightRegularizationTests {

        private static List<ScatterErrorPoint> Points(params double[] sigmas) {
            return sigmas.Select((s, i) => new ScatterErrorPoint(100 * i, 2.0 + i, 0, s)).ToList();
        }

        [Test]
        public void Regularize_HealthySigmas_Unchanged() {
            var result = WeightRegularization.Regularize(Points(0.1, 0.2, 0.3, 0.4, 0.5));
            Assert.That(result.Select(p => p.ErrorY), Is.EqualTo(new[] { 0.1, 0.2, 0.3, 0.4, 0.5 }).Within(1e-12));
        }

        [Test]
        public void Regularize_DegenerateSigma_FlooredAtFractionOfMedian() {
            // Median of the positive σs is 0.3 → floor = 0.2 · 0.3 = 0.06. The old chain gave this
            // point weight 1/0.001 = 1000; now it is capped at 1/0.06 ≈ 16.7 (5× the median weight).
            var result = WeightRegularization.Regularize(Points(0.001, 0.2, 0.3, 0.4, 0.5));
            Assert.That(result[0].ErrorY, Is.EqualTo(0.06).Within(1e-12));
        }

        [Test]
        public void Regularize_ZeroAndNaN_GetMedianSigma() {
            // Unknown precision gets *average* weight (median σ), not maximum weight.
            var result = WeightRegularization.Regularize(Points(0.0, double.NaN, 0.2, 0.3, 0.4));
            Assert.Multiple(() => {
                Assert.That(result[0].ErrorY, Is.EqualTo(0.3).Within(1e-12));
                Assert.That(result[1].ErrorY, Is.EqualTo(0.3).Within(1e-12));
            });
        }

        [Test]
        public void Regularize_AllDegenerate_FallsBackToUnweighted() {
            var result = WeightRegularization.Regularize(Points(0.0, double.NaN, -1.0));
            Assert.That(result.Select(p => p.ErrorY), Is.All.EqualTo(1.0));
        }

        [Test]
        public void Regularize_EmptyAndNull_ReturnsEmpty() {
            Assert.Multiple(() => {
                Assert.That(WeightRegularization.Regularize(new List<ScatterErrorPoint>()), Is.Empty);
                Assert.That(WeightRegularization.Regularize(null), Is.Empty);
            });
        }

        [Test]
        public void Regularize_PreservesXYAndErrorX() {
            var input = new List<ScatterErrorPoint> { new ScatterErrorPoint(123, 4.5, 6.7, 0.0) };
            var result = WeightRegularization.Regularize(input);
            Assert.Multiple(() => {
                Assert.That(result[0].X, Is.EqualTo(123));
                Assert.That(result[0].Y, Is.EqualTo(4.5));
                Assert.That(result[0].ErrorX, Is.EqualTo(6.7));
            });
        }

        [Test]
        public void Regularize_SinglePoint_KeepsOwnSigma() {
            // Median of one value is itself; the floor (0.2·σ) is below σ, so it passes through.
            var result = WeightRegularization.Regularize(Points(0.25));
            Assert.That(result[0].ErrorY, Is.EqualTo(0.25).Within(1e-12));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~WeightRegularizationTests"` (timeout 600000)
Expected: build FAILURE — `WeightRegularization` does not exist yet.

- [ ] **Step 3: Implement `WeightRegularization`**

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// Regularizes per-point σ (ErrorY) before any weighted curve fit, bounding the influence a single
    /// point can gain from a degenerate σ. Per-point σ is the star-ensemble scatter — a relative
    /// precision proxy — and a near-zero value (one detected star, or a few stars with near-identical
    /// HFR → MAD ≈ 0) is an artifact of the estimator, not real precision: unregularized it became a
    /// ~1000× weight that pinned the fit (analysis finding F5a), and Huber IRLS could not recover
    /// because LM minimizes the pinned point's residual by construction. NINA core's Trendline and
    /// QuadraticFitting weight by 1/ErrorY² and cannot be modified, so every fitter must receive these
    /// regularized copies; raw measured σ continues to flow to reports and charts.
    /// </summary>
    public static class WeightRegularization {

        /// <summary>
        /// Floor on σ as a fraction of the median positive σ of the points entering the fit. One point
        /// can exceed the median weight by at most 1/0.2 = 5× (25× in squared influence), preserving
        /// legitimate near-focus vs wing weight ratios (~3× in healthy sweeps, weights 2-20) while
        /// removing the degenerate 1000× path.
        /// </summary>
        public const double MinErrorYFractionOfMedian = 0.2;

        /// <summary>
        /// Returns copies of <paramref name="points"/> with ErrorY regularized: non-finite or ≤ 0 σ
        /// (unknown precision) becomes the median positive σ — average weight, not maximum — and
        /// positive σ is floored at <see cref="MinErrorYFractionOfMedian"/>·median. When no point has
        /// a positive finite σ, all σ become 1.0 (the fit degenerates to unweighted). X, Y, and ErrorX
        /// pass through unchanged.
        /// </summary>
        public static List<ScatterErrorPoint> Regularize(IReadOnlyList<ScatterErrorPoint> points) {
            if (points == null || points.Count == 0) {
                return new List<ScatterErrorPoint>();
            }

            var positiveSigmas = points
                .Select(p => p.ErrorY)
                .Where(s => !double.IsNaN(s) && !double.IsInfinity(s) && s > 0.0)
                .ToList();
            if (positiveSigmas.Count == 0) {
                return points.Select(p => new ScatterErrorPoint(p.X, p.Y, p.ErrorX, 1.0)).ToList();
            }

            var (median, _) = positiveSigmas.MedianMAD();
            var floor = MinErrorYFractionOfMedian * median;
            return points.Select(p => {
                var sigma = p.ErrorY;
                var regularized = !double.IsNaN(sigma) && !double.IsInfinity(sigma) && sigma > 0.0
                    ? Math.Max(sigma, floor)
                    : median;
                return new ScatterErrorPoint(p.X, p.Y, p.ErrorX, regularized);
            }).ToList();
        }
    }
}
```

(`MedianMAD` is the existing tested extension in `NINA.Joko.Plugins.HocusFocus.Utility.MathUtility`; it returns the σ-consistent scaled MAD as the second element, unused here.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~WeightRegularizationTests"` (timeout 600000)
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/WeightRegularization.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/WeightRegularizationTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Add WeightRegularization: median-relative sigma floor for weighted fits (F5a)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: `AverageMeasurement` rewrite (F5b latch + F6 SEM pooling)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/CvImageUtility.cs:607-637`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/AverageMeasurementTests.cs` (new file — keep the large existing `CvImageUtilityTests.cs` untouched)

- [ ] **Step 1: Write the failing tests**

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.WPF.Base.ViewModel.AutoFocus;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class AverageMeasurementTests {

        private static MeasureAndError M(double measure, double stdev) =>
            new MeasureAndError() { Measure = measure, Stdev = stdev };

        [Test]
        public void AverageMeasurement_SingleFrame_PassesThrough() {
            var result = new List<MeasureAndError> { M(2.0, 0.5) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(2.0));
                Assert.That(result.Stdev, Is.EqualTo(0.5).Within(1e-12));
            });
        }

        [Test]
        public void AverageMeasurement_MultiFrame_StdevIsSemNotRms() {
            // The mean of n frames has SEM = σ/√n; the old code returned the RMS σ (no ÷√n),
            // so FramesPerPoint never reduced the reported σ (F6).
            var result = new List<MeasureAndError> { M(2.0, 0.4), M(3.0, 0.4) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(2.5));
                Assert.That(result.Stdev, Is.EqualTo(0.4 / Math.Sqrt(2)).Within(1e-12));
            });
        }

        [Test]
        public void AverageMeasurement_InvalidSigmaFrameFirst_LaterFramesStillPooled() {
            // Regression for the F5b latch: an invalid-σ frame previously stopped variance
            // accumulation for ALL later frames while the divisor still counted them.
            var result = new List<MeasureAndError> { M(2.0, 0.0), M(3.0, 0.3), M(4.0, 0.3) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(3.0));
                // Valid-σ pool RMS = 0.3; SEM over the 3 averaged frames = 0.3/√3.
                Assert.That(result.Stdev, Is.EqualTo(0.3 / Math.Sqrt(3)).Within(1e-12));
            });
        }

        [Test]
        public void AverageMeasurement_AllSigmasInvalid_StdevNaNMeasureIntact() {
            var result = new List<MeasureAndError> { M(2.0, 0.0), M(3.0, double.NaN) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(2.5));
                Assert.That(result.Stdev, Is.NaN);
            });
        }

        [Test]
        public void AverageMeasurement_NoPositiveMeasures_ZeroMeasureNaNStdev() {
            var result = new List<MeasureAndError> { M(0.0, 0.5) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(0.0));
                Assert.That(result.Stdev, Is.NaN);
            });
        }

        [Test]
        public void AverageMeasurement_ZeroMeasureFrame_ExcludedFromMeanAndPool() {
            var result = new List<MeasureAndError> { M(0.0, 0.5), M(2.0, 0.3) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(2.0));
                Assert.That(result.Stdev, Is.EqualTo(0.3).Within(1e-12));
            });
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~AverageMeasurementTests"` (timeout 600000)
Expected: `AverageMeasurement_MultiFrame_StdevIsSemNotRms`, `AverageMeasurement_InvalidSigmaFrameFirst_LaterFramesStillPooled`, and `AverageMeasurement_AllSigmasInvalid_StdevNaNMeasureIntact` FAIL against the old implementation (RMS instead of SEM; latch bug; Stdev 0 instead of NaN). The others pass — confirming what is preserved.

- [ ] **Step 3: Replace the implementation**

In `CvImageUtility.cs`, replace the entire `AverageMeasurement` method (currently lines 607-637) with:

```csharp
        /// <summary>
        /// Pools multi-frame sub-measurements into one measurement: the mean of the positive measures,
        /// with the standard error of that mean derived from the per-frame σs. Per-frame σ is the
        /// star-ensemble scatter (1.483·MAD across stars) — a relative precision proxy, not the
        /// median's absolute uncertainty — and the pooled value keeps those units: RMS of the valid
        /// per-frame σs divided by √(number of averaged frames). Frames with an invalid σ (≤ 0 or NaN,
        /// e.g. a single detected star) still contribute their measure to the mean but are excluded
        /// from the σ pool; when no frame has a valid σ the pooled Stdev is NaN ("unknown"), which the
        /// fit layer maps to the sweep's median σ (see WeightRegularization) and displays render as no
        /// error bar.
        /// </summary>
        public static MeasureAndError AverageMeasurement(this List<MeasureAndError> measurement) {
            int total = 0;
            int validVarianceCount = 0;
            double sum = 0.0d;
            double sumVariance = 0.0d;
            foreach (var measure in measurement) {
                if (measure.Measure > 0) {
                    sum += measure.Measure;
                    ++total;
                    if (measure.Stdev > 0.0) { // false for NaN as well
                        sumVariance += measure.Stdev * measure.Stdev;
                        ++validVarianceCount;
                    }
                }
            }

            if (total == 0) {
                return new MeasureAndError() {
                    Measure = 0.0,
                    Stdev = double.NaN
                };
            }
            var stdev = validVarianceCount > 0
                ? Math.Sqrt(sumVariance / validVarianceCount) / Math.Sqrt(total)
                : double.NaN;
            return new MeasureAndError() {
                Measure = sum / total,
                Stdev = stdev
            };
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~AverageMeasurementTests"` (timeout 600000)
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/CvImageUtility.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/AverageMeasurementTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Fix AverageMeasurement: valid-sigma-only SEM pooling, surface unknown sigma as NaN (F5b/F6)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Honest point construction (drop the 0.001 fabrication)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs:695` (+ new helper near `TryCompleteFocuserPoint`, ~line 708)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/AutoFocusEngineTests.cs` (append)

- [ ] **Step 1: Write the failing test**

Append to the existing fixture in `AutoFocusEngineTests.cs`:

```csharp
        [Test]
        public void SafeDisplayError_MapsNaNAndNegativeToZero_KeepsMeasuredValues() {
            Assert.Multiple(() => {
                Assert.That(AutoFocusEngine.SafeDisplayError(double.NaN), Is.EqualTo(0.0));
                Assert.That(AutoFocusEngine.SafeDisplayError(-0.5), Is.EqualTo(0.0));
                Assert.That(AutoFocusEngine.SafeDisplayError(0.0), Is.EqualTo(0.0));
                // Below the old 0.001 fabrication threshold: must now pass through unmodified.
                Assert.That(AutoFocusEngine.SafeDisplayError(0.0004), Is.EqualTo(0.0004));
                Assert.That(AutoFocusEngine.SafeDisplayError(0.25), Is.EqualTo(0.25));
            });
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~SafeDisplayError"` (timeout 600000)
Expected: build FAILURE — `SafeDisplayError` does not exist.

- [ ] **Step 3: Implement**

In `AutoFocusEngine.cs`, change line 695 from

```csharp
                var focusPoints = regionState.MeasurementsByFocuserPoint.Select(fp => new ScatterErrorPoint(fp.Key, fp.Value.Measure, 0, Math.Max(0.001, fp.Value.Stdev))).ToList();
```

to

```csharp
                var focusPoints = regionState.MeasurementsByFocuserPoint.Select(fp => new ScatterErrorPoint(fp.Key, fp.Value.Measure, 0, SafeDisplayError(fp.Value.Stdev))).ToList();
```

and add next to `TryCompleteFocuserPoint` (after line ~714):

```csharp
        /// <summary>
        /// σ for the display/report layer: keep the measured value; NaN (no valid per-frame σ) and
        /// negatives render as 0 = "no error bar". The old code fabricated a 0.001 floor here, which
        /// downstream 1/σ weighting turned into a 1000× weight (F5a). Fitters never consume this raw
        /// value directly — every weighted fit receives WeightRegularization copies, which map 0 or
        /// unknown σ to the sweep's median σ.
        /// </summary>
        internal static double SafeDisplayError(double stdev) {
            return double.IsNaN(stdev) ? 0.0 : Math.Max(0.0, stdev);
        }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~SafeDisplayError"` (timeout 600000)
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/AutoFocusEngineTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Replace fabricated 0.001 ErrorY floor with honest NaN-guarded display sigma

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

**Note:** between this task and Task 5 the weighted fits would see raw (possibly 0) σ — the models' internal `1/max(|σ|, 1e-6)` backstops prevent division by zero, but NINA's `Trendline` would compute `1/0²` = Infinity. That is why Task 5 lands in the same PR and the suite is only required fully green at Task 5's end; do not insert a release between these tasks.

---

### Task 5: Wire regularization into every fit entry point

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs` (3 sites: `CurveFittingResult.Calculate` line ~102; `SelectBestHyperbolicModel` line ~269; `ComputeLeaveOneOutStability` line ~320)
- Modify: `Joko.NINA.Plugins/TestApp/FitQualityRunner.cs` (`LoadMeasurePoints`, lines ~244-267)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/WeightChainIntegrationTests.cs` (new)

- [ ] **Step 1: Write the failing pin-release integration test**

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// End-to-end check that WeightRegularization releases the F5a pin: a single point with a
    /// degenerate σ must no longer dominate a weighted hyperbolic fit.
    /// </summary>
    [TestFixture]
    public class WeightChainIntegrationTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void Setup() {
            alglibAPI = new AlglibAPI();
        }

        // y = a/b·√((x−x0)² + b²) + y0 — the Symmetric hyperbola, sampled at 11 positions.
        private static List<ScatterErrorPoint> SyntheticSweep(double x0) {
            const double a = 1.0, b = 200.0, y0 = 1.0;
            var points = new List<ScatterErrorPoint>();
            for (int i = 0; i <= 10; ++i) {
                double x = x0 - 500 + i * 100;
                double y = a / b * Math.Sqrt((x - x0) * (x - x0) + b * b) + y0;
                points.Add(new ScatterErrorPoint(x, y, 0, 0.3));
            }
            return points;
        }

        [Test]
        public void Regularize_ZeroSigmaOutlier_NoLongerPinsWeightedHyperbolicFit() {
            const double trueX0 = 5000.0;
            var points = SyntheticSweep(trueX0);
            // A wing point displaced upward with a degenerate σ — the F5a scenario (e.g. a frame
            // where 2 near-identical-HFR stars gave MAD ≈ 0). Old chain: weight 1/0.001 = 1000.
            points[0] = new ScatterErrorPoint(points[0].X, points[0].Y + 1.5, 0, 0.001);

            // Huber IRLS off on both fits to isolate pure weighting behavior (the LM solver pins the
            // curve to a 1000×-weighted point, so IRLS cannot save the unregularized fit anyway).
            var pinned = HyperbolicFittingAlglib.Create(alglibAPI, points, useWeights: true);
            pinned.HuberIrlsEnabled = false;
            Assert.That(pinned.Solve(), Is.True);

            var regularized = HyperbolicFittingAlglib.Create(alglibAPI, WeightRegularization.Regularize(points), useWeights: true);
            regularized.HuberIrlsEnabled = false;
            Assert.That(regularized.Solve(), Is.True);

            var pinnedShift = Math.Abs(pinned.Minimum.X - trueX0);
            var regularizedShift = Math.Abs(regularized.Minimum.X - trueX0);
            Assert.Multiple(() => {
                Assert.That(regularizedShift, Is.LessThan(50.0), "regularized fit should stay near the true minimum");
                Assert.That(regularizedShift, Is.LessThan(pinnedShift), "regularization must reduce the degenerate point's pull");
            });
        }

        [Test]
        public void Regularize_ZeroSigmaOutlier_WithHuberIrls_LandsNearTruth() {
            // With the pin released, Huber IRLS can now also engage on the displaced point.
            const double trueX0 = 5000.0;
            var points = SyntheticSweep(trueX0);
            points[0] = new ScatterErrorPoint(points[0].X, points[0].Y + 1.5, 0, 0.001);

            var fit = HyperbolicFittingAlglib.Create(alglibAPI, WeightRegularization.Regularize(points), useWeights: true);
            Assert.That(fit.Solve(), Is.True);
            Assert.That(Math.Abs(fit.Minimum.X - trueX0), Is.LessThan(25.0));
        }
    }
}
```

- [ ] **Step 2: Run the tests — they must pass already (they exercise Task 2's class directly)**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~WeightChainIntegrationTests"` (timeout 600000)
Expected: 2 passed. (These tests pin the *behavior*; the remaining steps wire the same call into production paths. If the `LessThan(50)`/`LessThan(25)` tolerances fail, investigate the fit — do not widen them blindly; the synthetic data is noise-free.)

- [ ] **Step 3: Wire into `CurveFittingResult.Calculate`**

In `AutoFocusEngine.cs` line ~102, change

```csharp
                var validFocusPoints = focusPoints.Where(p => p.Y > 0.0).ToList();
```

to

```csharp
                // Weighted fitters — ours and NINA core's Trendline/QuadraticFitting, which weight by
                // 1/ErrorY² — must never see a degenerate σ: fit on regularized copies. Raw points
                // still feed reports/charts upstream; rejected points recorded from this path carry
                // the regularized σ.
                var validFocusPoints = WeightRegularization.Regularize(focusPoints.Where(p => p.Y > 0.0).ToList());
```

`AutoFocusEngine.cs` already has `using NINA.Joko.Plugins.HocusFocus.StarDetection;` (it references `AlglibHyperbolicFitting`); no using changes needed.

- [ ] **Step 4: Wire into `SelectBestHyperbolicModel`**

Line ~269, change

```csharp
                var validPoints = lastValidFocusPoints.Where(p => p.Y > 0.0).ToList();
```

to

```csharp
                var validPoints = WeightRegularization.Regularize(lastValidFocusPoints.Where(p => p.Y > 0.0).ToList());
```

- [ ] **Step 5: Wire into `ComputeLeaveOneOutStability`**

Line ~320, change

```csharp
                var validPoints = lastValidFocusPoints.Where(p => p.Y > 0.0).ToList();
```

to

```csharp
                var validPoints = WeightRegularization.Regularize(lastValidFocusPoints.Where(p => p.Y > 0.0).ToList());
```

- [ ] **Step 6: Wire into `FitQualityRunner.LoadMeasurePoints` (TestApp)**

Replace the method body (lines ~249-267) so invalid stored errors become the *unknown* sentinel (0) instead of a fabricated 1.0, then regularize — old reports containing the historical fabricated `0.001` floor are regularized identically to live runs:

```csharp
        private static List<ScatterErrorPoint> LoadMeasurePoints(JObject json) {
            var measurePoints = json["MeasurePoints"] as JArray;
            if (measurePoints == null) {
                return null;
            }
            var pts = new List<ScatterErrorPoint>();
            foreach (var mp in measurePoints) {
                if (mp is not JObject mpo) {
                    continue;
                }
                var x = ReadDouble(mpo["Position"]);
                var y = ReadDouble(mpo["Value"]);
                var e = ReadDouble(mpo["Error"]);
                if (!double.IsNaN(x) && !double.IsNaN(y) && y > 0) {
                    pts.Add(new ScatterErrorPoint(x, y, 0, double.IsNaN(e) ? 0.0 : Math.Max(0.0, e)));
                }
            }
            return WeightRegularization.Regularize(pts);
        }
```

Also update the doc comment above it (lines ~244-248) to:

```csharp
        /// <summary>
        /// Builds the weighted focus points from <c>MeasurePoints</c>: keep points with finite Position
        /// and positive Value, then regularize σ exactly like the production fit path
        /// (<see cref="WeightRegularization"/>) — invalid/absent errors become the unknown sentinel (0)
        /// and map to the median σ; historical fabricated 0.001 floors are floored to 0.2·median.
        /// </summary>
```

Add `using NINA.Joko.Plugins.HocusFocus.StarDetection;` to the file's usings if not present (it is — `HocusFocusStarDetection` is already imported from that namespace).

- [ ] **Step 7: Run the full test suite**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: errors=0, all tests pass. Pre-existing fitting tests (`HyperbolicFittingAlglibTests`, `HybridModelSelectionTests`, `AsymmetricFitRobustnessTests`, `AlglibHyperbolicFittingChiSquaredTests`, `HocusFocusReportTests`, …) must stay green — they construct fits directly with healthy σ, which regularization passes through unchanged. If a fixture fails, read its data: a test relying on a degenerate σ dominating a fit is asserting the F5a bug and should be updated deliberately, not patched around.

- [ ] **Step 8: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs \
        Joko.NINA.Plugins/TestApp/FitQualityRunner.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/WeightChainIntegrationTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Apply WeightRegularization at every fit entry point (engine + fit-quality harness)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Documentation updates (χ² semantics, weighting tooltips)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs:58-73` (XML docs)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml:418-420` (tooltips)

- [ ] **Step 1: Update the `ChiSquared` XML doc (lines 58-62)**

Replace with:

```csharp
        /// <summary>
        /// Weighted χ² of the fit: Σ (Weights[i]·(model(xᵢ) − yᵢ))². When <see cref="WeightedHyperbolicFitEnabled"/>
        /// is on (Weights = 1/σ from each point's regularized ErrorY) this is a χ² in <b>scatter units</b>:
        /// per-point σ is the star-ensemble scatter (1.483·MAD), which overstates the uncertainty of the
        /// plotted median HFR by roughly √(detected stars) — so values ≪ 1 are normal for star-rich fields.
        /// Unweighted (Weights = 1) it degenerates to the plain residual sum of squares, which is
        /// scale-dependent. Computed for every model.
        /// </summary>
```

- [ ] **Step 2: Update the `ReducedChiSquared` XML doc (lines 68-73)**

Replace with:

```csharp
        /// <summary>
        /// Reduced χ² = <see cref="ChiSquared"/> / <see cref="DegreesOfFreedom"/>. Comparable across runs
        /// only as a <b>relative</b> goodness measure: per-point σ is ensemble scatter, so this runs ≪ 1
        /// for star-rich fields (≈ 1/N* scaling) and grows ×FramesPerPoint now that multi-frame σ is
        /// SEM-pooled. Meaningful only for weighted fits; see <see cref="ChiSquared"/>.
        /// <see cref="double.NaN"/> when not computed.
        /// </summary>
```

- [ ] **Step 3: Update the tooltips in `OptionsDataTemplates.xaml`**

Replace the `ReducedChiSquaredRejectionThreshold_Tooltip` text (line ~420) — the current claim that "reduced χ² routinely runs above 1 even for good fits" is wrong — with:

```xaml
    <TextBlock x:Key="ReducedChiSquaredRejectionThreshold_Tooltip" Text="Upper bound on the hyperbolic fit's reduced χ² (χ² per degree of freedom) above which the run is rejected, when the rejection criterion is set to Reduced χ². The per-point σ behind the weights is the star-ensemble scatter, which overstates the uncertainty of the per-point median HFR — so reduced χ² typically runs well below 1 and shrinks as more stars are detected (and as Frames Per Point increases). Treat this threshold as a coarse sanity bound rather than a calibrated statistical test. Set to 0 to disable. Meaningful only with weighted fits." />
```

Extend `WeightedHyperbolicFit_Tooltip` (line ~418) to:

```xaml
    <TextBlock x:Key="WeightedHyperbolicFit_Tooltip" Text="Weights each point in the hyperbolic fit based on measurement error. σ values are regularized before fitting: a degenerate near-zero σ (e.g. from a frame with very few detected stars) is floored at 20% of the sweep's median σ, and an unknown σ gets the median, so no single point can dominate the fit." />
```

- [ ] **Step 4: Build to validate the XAML**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: errors=0.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Document chi-squared scatter-unit semantics and weight regularization

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: χ² gate empirical calibration (needs the user's saved-report corpus)

**Files:**
- Create: `plans/weight-chain-hygiene-chi2-results.md` (findings write-up; same pattern as `sigma-consistency-f3-results.md`)
- Conditionally modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusOptions.cs:71,94`, `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/AutoFocusOptionsTests.cs:239`, and the Task 6 tooltip number

- [ ] **Step 1: Build TestApp**

Run: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` (timeout 600000)
Expected: errors=0.

- [ ] **Step 2: Ask the user for the corpus location**

The harness takes `--dir <folder>` or `--zip <archive>` of saved `autofocus_report*.json` files. **Stop and ask the user** which corpus to use (they have real saved AF runs) — do not guess a path.

- [ ] **Step 3: Run the harness**

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  fit-quality --dir "C:\path\from\user" --out "C:\temp\fitquality-weightchain"
```

(Optional flags: `--models` to restrict models, `--min-points`, `--no-weights` for an unweighted control run.)

- [ ] **Step 4: Analyze the χ²_red distribution**

From the output CSV, per model and overall, compute median / P90 / P99 / max of `ReducedChiSquared` for runs that solved (`SolveOk`). Compare `StoredReducedChiSquared` (old chain) vs the re-fit values (new chain) to quantify the regularization's effect. Note: historical reports are almost all FramesPerPoint = 1, so the ×n SEM shift does not contaminate this corpus; state that in the write-up.

- [ ] **Step 5: Apply the pre-registered decision rule (from the design doc)**

- **Tight distribution** (good-run χ²_red spread ≲ 2 orders of magnitude): pick the new default `ReducedChiSquaredRejectionThreshold` as a robust upper fence (median × 10, or ≈ P99 with margin, rounded to one significant figure). Then update: `AutoFocusOptions.cs` line 71 (the `GetValueDouble(nameof(ReducedChiSquaredRejectionThreshold), <default>)` literal) and line 94 (`ResetDefaults`), the pinned expectation at `AutoFocusOptionsTests.cs:239`, and the threshold guidance sentence in the Task 6 tooltip.
- **Sprawling distribution**: keep 5.0 everywhere; the Task 6 docs already describe the gate as a coarse sanity bound.

- [ ] **Step 6: Write `plans/weight-chain-hygiene-chi2-results.md`**

Record: corpus size, per-model χ²_red percentiles (old vs new chain), the decision taken and why, and any runs whose fitted minimum moved materially under regularization (these are the F5a victims — list file + shift in steps).

- [ ] **Step 7: Run the full suite (required if any default changed)**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: errors=0.

- [ ] **Step 8: Commit**

```bash
git add -A plans/weight-chain-hygiene-chi2-results.md \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusOptions.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/AutoFocusOptionsTests.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Calibrate reduced chi-squared gate from saved-run corpus

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

(If the decision was docs-only with no file changes beyond the results doc, commit just that file with message "Record chi-squared calibration results: threshold kept at 5.0".)

---

### Task 8: Final verification and PR

- [ ] **Step 1: Full suite green**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: errors=0, 0 failed. Use the superpowers:verification-before-completion skill — paste the actual summary line, no success claims without output.

- [ ] **Step 2: Review the diff against the design doc**

`git diff develop --stat` — every changed file must map to a design section (§1 regularizer, §2 construction, §3 pooling, §4 χ², roadmap). Anything else is scope creep; remove it.

- [ ] **Step 3: Finish the branch**

Use the superpowers:finishing-a-development-branch skill. Target: PR from `ghilios/weight-chain-hygiene` into `develop`. Suggested PR title: "Weight-chain hygiene: sigma regularization, SEM pooling, chi-squared calibration (F5/F6)". PR body should cite the design doc, the four findings addressed (F5a, F5b, F6 + the NINA-core 1/σ² discovery), and the calibration results doc.

---

## Self-review notes (already applied)

- Spec coverage: design §1 → Tasks 2+5; §2 → Task 4; §3 → Task 3; §4 → Tasks 6+7; roadmap scope → Task 1. Out-of-scope items (F7, F12, star-count threading, UI knob) appear in no task.
- Type consistency: `WeightRegularization.Regularize(IReadOnlyList<ScatterErrorPoint>) → List<ScatterErrorPoint>` matches every call site (`List` implements `IReadOnlyList`); `SafeDisplayError` is `internal static` (Tests project has `InternalsVisibleTo`; TestApp does too).
- Task 4/5 ordering note: raw-σ exposure between the tasks is intentional and confined to one commit boundary; both land in the same PR.
