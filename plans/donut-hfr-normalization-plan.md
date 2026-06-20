# Donut HFR Normalization (R_e) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the brightness-biased flux-weighted-mean HFR with a true encircled-flux radius (`R_e`) for heavily-defocused donut stars, gated to donut scenarios, so per-frame aggregates and the sensor/tilt model become brightness-consistent.

**Architecture:** A pure, unit-tested `DonutEncircledRadius` helper computes a curve-of-growth `R₅₀` from a ring-fit center with a noise-convergence integration cap (the configuration empirically validated in `docs/donut-hfr-normalization-results.md`). `StarDetector` calls it only for large/donut candidates when the defocus-aware donut master toggle is on, storing the result in a new parallel `Star.NormalizedHFR` field (= legacy `HFR` for compact stars or when the toggle is off → bit-identical otherwise). Aggregation, review stats, and the sensor model switch to reading `NormalizedHFR`. Approach B (annulus forward-fit) is built only in `TestApp` as an offline oracle for an A-vs-B agreement report — it never ships in the plugin.

**Tech Stack:** C# / .NET 8 (windows), OpenCvSharp4 (`Mat`, `CV_32F`), NUnit 4 + NUnit3TestAdapter, MathNet/Alglib (only TestApp Phase 1). Tests: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.

**Design refs:** `docs/donut-hfr-normalization-design.md` (§4 algorithm, §6 background risk, §8.1 gating), `docs/donut-hfr-normalization-results.md` (validated config + size-gating finding).

**Branch:** `ghilios/donut-hfr-normalization` (already exists; 3 commits of design + validation).

**Git/commit rule (CLAUDE.md):** every commit MUST set both author and committer to `George Hilios <322725+ghilios@users.noreply.github.com>`:
```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<msg>"
```
(Plan steps below abbreviate this as `git commit -m "<msg>"` — always use the full form.)

---

## Phase 1 — TestApp B-oracle + A-vs-B agreement report (pre-implementation gate)

**Why first:** the design (§9.5) requires validating the production metric against an independent geometric ground truth (the annulus forward fit) before committing production code. This phase is TestApp-only.

### Task 1.1: Port the validated R_e helper into a reusable TestApp form

The R_e algorithm already exists, validated, inside `TestApp/ContaminationDiagnosticRunner.cs`
(`RingCenter`, `ScanRadialBins`, `ComputeCog`, `FractionRadius`). Extract it into a standalone file so
both the agreement report and (later) the production port share one reference implementation.

**Files:**
- Create: `Joko.NINA.Plugins/TestApp/DonutRadiusOracle.cs`
- Modify: `Joko.NINA.Plugins/TestApp/ContaminationDiagnosticRunner.cs` (call the extracted helpers)

- [ ] **Step 1: Create `DonutRadiusOracle.cs` with the encircled-radius helpers (moved verbatim from the runner) plus a forward-model annulus fit.**

```csharp
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using OpenCvSharp;
using System;

namespace TestApp {
    /// <summary>
    /// Offline donut-size oracle. (A) Encircled-flux radius R_e via curve of growth from a ring-fit center
    /// (the production-candidate metric). (B) An annulus⊛Gaussian forward fit whose geometric R_out is
    /// brightness-independent by construction (the ground truth). Used by the A-vs-B agreement report.
    /// </summary>
    internal static class DonutRadiusOracle {
        // ----- (A) encircled-flux radius (validated config: ring center + noise-convergence cap) -----

        public static (double cx, double cy) RingCenter(Mat img, double cx0, double cy0,
                LocalBackgroundPlane plane, double scalarBg, double maxRadius, double noiseSigma) {
            int W = img.Width, H = img.Height;
            int x0 = Math.Max(0, (int)Math.Floor(cx0 - maxRadius)), x1 = Math.Min(W - 1, (int)Math.Ceiling(cx0 + maxRadius));
            int y0 = Math.Max(0, (int)Math.Floor(cy0 - maxRadius)), y1 = Math.Min(H - 1, (int)Math.Ceiling(cy0 + maxRadius));
            double sw = 0, sx = 0, sy = 0, thresh = 3.0 * noiseSigma;
            for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x) {
                    double dx = x - cx0, dy = y - cy0;
                    if (dx * dx + dy * dy > maxRadius * maxRadius) continue;
                    double v = img.At<float>(y, x) - (plane != null ? plane.ValueAt(x, y) : scalarBg);
                    if (v > thresh) { sw += v; sx += v * x; sy += v * y; }
                }
            return sw > 0 ? (sx / sw, sy / sw) : (cx0, cy0);
        }

        public static double[] ScanAnnularBins(Mat img, double cx, double cy,
                LocalBackgroundPlane plane, double scalarBg, double maxRadius, double bgDelta) {
            int nbins = (int)Math.Ceiling(maxRadius) + 1;
            var annular = new double[nbins];
            int W = img.Width, H = img.Height;
            int x0 = Math.Max(0, (int)Math.Floor(cx - maxRadius)), x1 = Math.Min(W - 1, (int)Math.Ceiling(cx + maxRadius));
            int y0 = Math.Max(0, (int)Math.Floor(cy - maxRadius)), y1 = Math.Min(H - 1, (int)Math.Ceiling(cy + maxRadius));
            for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x) {
                    double dx = x - cx, dy = y - cy, r = Math.Sqrt(dx * dx + dy * dy);
                    if (r > maxRadius) continue;
                    int b = (int)r; if (b >= nbins) continue;
                    annular[b] += img.At<float>(y, x) - ((plane != null ? plane.ValueAt(x, y) : scalarBg) + bgDelta);
                }
            return annular;
        }

        public static double EncircledRadius(double[] annular, double noiseSigma, double maxRadius, double frac) {
            int nbins = annular.Length, below = 0; double convR = maxRadius;
            if (noiseSigma > 0)
                for (int b = 5; b < nbins; ++b) {
                    double floor = noiseSigma * Math.Sqrt(2.0 * Math.PI * (b + 0.5));
                    if (annular[b] < floor) { if (++below >= 3) { convR = b - 2; break; } } else below = 0;
                }
            int convBin = Math.Min((int)convR, nbins - 1);
            var cum = new double[nbins]; double acc = 0;
            for (int b = 0; b < nbins; ++b) { acc += annular[b]; cum[b] = acc; }
            double total = convBin >= 0 ? cum[convBin] : double.NaN;
            if (!(total > 0)) return double.NaN;
            double target = frac * total;
            for (int b = 0; b <= convBin; ++b)
                if (cum[b] >= target) {
                    double prev = b > 0 ? cum[b - 1] : 0.0, denom = cum[b] - prev;
                    return b + (denom > 1e-12 ? (target - prev) / denom : 0.0);
                }
            return double.NaN;
        }

        // ----- (B) annulus ⊛ Gaussian forward fit (brightness-independent ground truth) -----
        // Coarse grid search over (Rout, eps, sigmaSeeing), amplitude+background solved by the data scale.
        // Returns geometric Rout and ring-centroid Rring = Rout*(1+eps)/2 (the comparator for A's R_e).

        public sealed class AnnulusFit { public double Rout, Eps, SigmaSeeing, Rring, RSquared; }

        public static AnnulusFit FitAnnulus(double[] annular, double maxRadius) {
            // annular[b] = background-subtracted flux in the ring [b, b+1); convert to mean surface brightness.
            int nbins = annular.Length;
            var profile = new double[nbins];
            for (int b = 0; b < nbins; ++b) {
                double area = Math.PI * ((b + 1.0) * (b + 1.0) - b * b);
                profile[b] = annular[b] / Math.Max(area, 1e-9);
            }
            double best = double.NegativeInfinity; var bestFit = new AnnulusFit();
            for (double Rout = 6; Rout <= maxRadius; Rout += 0.5)
                for (double eps = 0.0; eps <= 0.7; eps += 0.05)
                    for (double sg = 0.5; sg <= 8.0; sg += 0.5) {
                        // model surface brightness ~ annulus(eps*Rout, Rout) ⊛ Gaussian(sg), unit amplitude
                        double sxy = 0, sxx = 0, syy = 0, mx = 0, my = 0; int n = 0;
                        var model = new double[nbins];
                        for (int b = 0; b < nbins; ++b) {
                            double r = b + 0.5;
                            double rise = 0.5 * Erfc((eps * Rout - r) / (sg * Math.Sqrt(2.0)));
                            double fall = 0.5 * Erfc((r - Rout) / (sg * Math.Sqrt(2.0)));
                            model[b] = rise * fall; mx += model[b]; my += profile[b]; n++;
                        }
                        mx /= n; my /= n;
                        for (int b = 0; b < nbins; ++b) { double a = model[b] - mx, d = profile[b] - my; sxy += a * d; sxx += a * a; syy += d * d; }
                        double r2 = (sxx > 0 && syy > 0) ? (sxy * sxy) / (sxx * syy) : 0.0;
                        if (r2 > best) { best = r2; bestFit = new AnnulusFit { Rout = Rout, Eps = eps, SigmaSeeing = sg, Rring = Rout * (1 + eps) / 2.0, RSquared = r2 }; }
                    }
            return bestFit;
        }

        private static double Erfc(double x) {
            double z = Math.Abs(x), t = 1.0 / (1.0 + 0.5 * z);
            double ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
                t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
                t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0.0 ? ans : 2.0 - ans;
        }
    }
}
```

- [ ] **Step 2: Point `ContaminationDiagnosticRunner.WriteCurveOfGrowth` at the shared helpers.** Replace its private `RingCenter`/`ScanRadialBins`/`ComputeCog`/`FractionRadius` bodies with calls to `DonutRadiusOracle.*` (the math is identical — the `cog_radii.csv` output must not change). Run the existing reproduce command and `diff` the new `cog_radii.csv` against the committed numbers in `docs/donut-hfr-normalization-results.md` (R₅₀ medians must match).

- [ ] **Step 3: Build.** Run: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` — Expected: `0 Error(s)`.

- [ ] **Step 4: Commit.** `git add … && git commit -m "refactor(testapp): extract DonutRadiusOracle (R_e + annulus forward fit)"`

### Task 1.2: Add `agreement` subcommand emitting the A-vs-B report

**Files:**
- Create: `Joko.NINA.Plugins/TestApp/AgreementRunner.cs`
- Modify: `Joko.NINA.Plugins/TestApp/Program.cs` (dispatch `agreement`)

- [ ] **Step 1: Write `AgreementRunner`** that, for one image: runs detection exactly like `ContaminationDiagnosticRunner` (build params via `HocusFocusStarDetection.BuildStarDetectorParams`, `--defocus-distortion --defocus-centering`), then per accepted star with `max(bbox) >= DefocusDistortionSizeReference` computes (A) `R₅₀` via `DonutRadiusOracle.EncircledRadius` from the ring center, and (B) `DonutRadiusOracle.FitAnnulus(...).Rring`. Write `agreement.csv` with `CenterX,CenterY,PeakBrightness,R50,Rring,Eps,FitR2` and an `agreement_summary.txt` with: Pearson r and OLS slope/intercept of `R50` vs `Rring`; median |R50−Rring|; slope of each vs `log10(peak)` (both should be ≈0); count with `FitR2 < 0.5` (oracle failures excluded). Mirror `ContaminationDiagnosticRunner.RunImpl` for profile/param/image loading.

- [ ] **Step 2: Dispatch in `Program.cs`** — add `case "agreement": await AgreementRunner.Run(args); return;` alongside the existing `contamination`/`optimize` cases (match the surrounding switch style).

- [ ] **Step 3: Build.** Run: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` — Expected `0 Error(s)`.

- [ ] **Step 4: Run on the validated frames.** For the mufti 2925 frame and `panos_hi`/`toml999_hi` (the strong-donut frames), run:
```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe agreement \
  --image "<frame>" --defocus-distortion --defocus-centering --out "C:\temp\hf-agree\<name>"
```
Expected (acceptance, design §9.5): OLS slope of `R50` vs `Rring` ∈ ~[0.9, 1.1], both brightness slopes < 0.3 px/dex, median |R50−Rring| small relative to median size.

- [ ] **Step 5: Write `docs/donut-hfr-normalization-results.md` "A-vs-B agreement" section** with the numbers, then commit. `git add … && git commit -m "feat(testapp): A-vs-B donut-size agreement report (§9.5)"`

> **GATE:** If A and B disagree materially (slope outside [0.85,1.15] or a brightness slope > 0.5 px/dex on a strong-donut frame), STOP and revisit Phase 2 parameters before writing production code. Record the outcome in the results doc.

---

## Phase 2 — Core `R_e` measurement helper (production, TDD)

### Task 2.1: `DonutEncircledRadius` production helper

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/DonutEncircledRadius.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/DonutEncircledRadiusTests.cs`

- [ ] **Step 1: Write the failing test** (uses the existing `SyntheticDefocusedStarImage.CreateAnnulus`).

```csharp
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class DonutEncircledRadiusTests {
        // A uniform disk's true 50%-enclosed radius is R/√2 (≈0.707 R). MeasureStar's flux-weighted mean
        // would give 2R/3 (≈0.667 R); R_e must land on the cumulative value.
        [Test]
        public void Re50_UniformDisk_MatchesHalfFluxRadius() {
            using var img = SyntheticDefocusedStarImage.CreateDisk(120, 120, 60, 60, radius: 20, peak: 1.0, background: 0.01);
            var plane = LocalBackgroundPlane.Flat(60, 60, 0.01);
            var re = DonutEncircledRadius.Measure(img, 60, 60, plane, 0.01, noiseSigma: 1e-4, maxRadius: 40, frac: 0.5).Radius;
            Assert.That(re, Is.EqualTo(20.0 / System.Math.Sqrt(2.0)).Within(1.0)); // ≈14.14, ±1px (1px binning)
        }

        // The headline property: R_e of the SAME annulus is independent of brightness (peak).
        [Test]
        public void Re50_Annulus_IsBrightnessIndependent() {
            double Re(double peak) {
                using var img = SyntheticDefocusedStarImage.CreateAnnulus(160, 160, 80, 80,
                    innerRadius: 8, outerRadius: 22, peak: peak, background: 0.02, edgeBlurSigma: 3.0);
                var plane = LocalBackgroundPlane.Flat(80, 80, 0.02);
                var c = DonutEncircledRadius.RingCenter(img, 80, 80, plane, 0.02, 50, 1e-4);
                return DonutEncircledRadius.Measure(img, c.cx, c.cy, plane, 0.02, 1e-4, 50, 0.5).Radius;
            }
            var faint = Re(0.05);
            var bright = Re(5.0);   // 100× brighter
            Assert.That(System.Math.Abs(bright - faint), Is.LessThan(0.6)); // px (validated regime ~0.35 px/dex)
        }
    }
}
```

- [ ] **Step 2: Run the test, verify it fails to compile** (`DonutEncircledRadius` does not exist).
Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter DonutEncircledRadiusTests`
Expected: build/compile error.

- [ ] **Step 3: Implement `DonutEncircledRadius`** (production port of the validated oracle; pure + static, mirrors `EffectiveClipMultiplier`'s testable-in-isolation style).

```csharp
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using OpenCvSharp;
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// True encircled-flux radius (R_e) for defocused "donut" stars — brightness-independent, unlike the
    /// flux-weighted-mean HFR (MeasureStar). Curve of growth from a ring-fit center with a noise-convergence
    /// integration cap. Validated config: ring center + adaptive cap + frac=0.5 (docs/donut-hfr-normalization-
    /// results.md). Pure + deterministic ⇒ unit-testable in isolation. NOT used for compact stars (caller gates
    /// on candidate size); 1-px radial bins make it unsuitable below ~6px median size.
    /// </summary>
    public static class DonutEncircledRadius {
        public readonly struct Result {
            public Result(double radius, double stdDev, double total, double convRadius) {
                Radius = radius; StdDev = stdDev; TotalFlux = total; ConvergenceRadius = convRadius;
            }
            public double Radius { get; }       // R_e (px)
            public double StdDev { get; }       // background-σ-propagated uncertainty (px), NaN if unavailable
            public double TotalFlux { get; }
            public double ConvergenceRadius { get; }
        }

        public static (double cx, double cy) RingCenter(Mat img, double cx0, double cy0,
                LocalBackgroundPlane plane, double scalarBg, double maxRadius, double noiseSigma) {
            int W = img.Width, H = img.Height;
            int x0 = Math.Max(0, (int)Math.Floor(cx0 - maxRadius)), x1 = Math.Min(W - 1, (int)Math.Ceiling(cx0 + maxRadius));
            int y0 = Math.Max(0, (int)Math.Floor(cy0 - maxRadius)), y1 = Math.Min(H - 1, (int)Math.Ceiling(cy0 + maxRadius));
            double sw = 0, sx = 0, sy = 0, thresh = 3.0 * noiseSigma;
            for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x) {
                    double dx = x - cx0, dy = y - cy0;
                    if (dx * dx + dy * dy > maxRadius * maxRadius) continue;
                    double v = img.At<float>(y, x) - (plane != null ? plane.ValueAt(x, y) : scalarBg);
                    if (v > thresh) { sw += v; sx += v * x; sy += v * y; }
                }
            return sw > 0 ? (sx / sw, sy / sw) : (cx0, cy0);
        }

        public static Result Measure(Mat img, double cx, double cy, LocalBackgroundPlane plane,
                double scalarBg, double noiseSigma, double maxRadius, double frac) {
            double re = RadiusFromBins(ScanAnnular(img, cx, cy, plane, scalarBg, maxRadius, 0.0), noiseSigma, maxRadius, frac, out double total, out double convR);
            // Background-σ-propagated uncertainty: half the ±0.1σ plane-perturbation spread (design §6).
            double rePlus = RadiusFromBins(ScanAnnular(img, cx, cy, plane, scalarBg, maxRadius, 0.1 * noiseSigma), noiseSigma, maxRadius, frac, out _, out _);
            double reMinus = RadiusFromBins(ScanAnnular(img, cx, cy, plane, scalarBg, maxRadius, -0.1 * noiseSigma), noiseSigma, maxRadius, frac, out _, out _);
            double sd = (double.IsNaN(rePlus) || double.IsNaN(reMinus)) ? double.NaN : Math.Abs(rePlus - reMinus) / 2.0;
            return new Result(re, sd, total, convR);
        }

        private static double[] ScanAnnular(Mat img, double cx, double cy, LocalBackgroundPlane plane,
                double scalarBg, double maxRadius, double bgDelta) {
            int nbins = (int)Math.Ceiling(maxRadius) + 1;
            var annular = new double[nbins];
            int W = img.Width, H = img.Height;
            int x0 = Math.Max(0, (int)Math.Floor(cx - maxRadius)), x1 = Math.Min(W - 1, (int)Math.Ceiling(cx + maxRadius));
            int y0 = Math.Max(0, (int)Math.Floor(cy - maxRadius)), y1 = Math.Min(H - 1, (int)Math.Ceiling(cy + maxRadius));
            for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x) {
                    double dx = x - cx, dy = y - cy, r = Math.Sqrt(dx * dx + dy * dy);
                    if (r > maxRadius) continue;
                    int b = (int)r; if (b >= nbins) continue;
                    annular[b] += img.At<float>(y, x) - ((plane != null ? plane.ValueAt(x, y) : scalarBg) + bgDelta);
                }
            return annular;
        }

        private static double RadiusFromBins(double[] annular, double noiseSigma, double maxRadius, double frac, out double total, out double convR) {
            int nbins = annular.Length, below = 0; convR = maxRadius;
            if (noiseSigma > 0)
                for (int b = 5; b < nbins; ++b) {
                    double floor = noiseSigma * Math.Sqrt(2.0 * Math.PI * (b + 0.5));
                    if (annular[b] < floor) { if (++below >= 3) { convR = b - 2; break; } } else below = 0;
                }
            int convBin = Math.Min((int)convR, nbins - 1);
            var cum = new double[nbins]; double acc = 0;
            for (int b = 0; b < nbins; ++b) { acc += annular[b]; cum[b] = acc; }
            total = convBin >= 0 ? cum[convBin] : double.NaN;
            if (!(total > 0)) return double.NaN;
            double target = frac * total;
            for (int b = 0; b <= convBin; ++b)
                if (cum[b] >= target) {
                    double prev = b > 0 ? cum[b - 1] : 0.0, denom = cum[b] - prev;
                    return b + (denom > 1e-12 ? (target - prev) / denom : 0.0);
                }
            return double.NaN;
        }
    }
}
```

- [ ] **Step 4: Run the tests, verify they pass.**
Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter DonutEncircledRadiusTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit.** `git add … && git commit -m "feat(stardetect): DonutEncircledRadius (brightness-independent R_e)"`

---

## Phase 3 — `Star` field + params + per-star gating

### Task 3.1: Add the parallel size fields to `Star` and `StarDetectorParams.NormalizeDonutSize`

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs`

- [ ] **Step 1: Add fields to `Star`** (after `HFR`, `IStarDetector.cs:617`):

```csharp
public double HFR { get; set; }

/// <summary>
/// Brightness-independent size used by donut-aware aggregation and the sensor model. Equals a true
/// encircled-flux radius (<see cref="DonutEncircledRadius"/>) for large/donut candidates when
/// <see cref="StarDetectorParams.NormalizeDonutSize"/> is on; otherwise equals <see cref="HFR"/>
/// verbatim (so aggregates are bit-identical when the donut master is off or the star is compact).
/// </summary>
public double NormalizedHFR { get; set; }

/// <summary>Background-σ-propagated uncertainty of <see cref="NormalizedHFR"/> (px); NaN when not a donut measurement.</summary>
public double NormalizedHFRStdDev { get; set; } = double.NaN;
```

- [ ] **Step 2: Add the param** to `StarDetectorParams` (near the donut block, after `DefocusAwareDonutDetection`, `IStarDetector.cs:367`):

```csharp
// When true (mapped from StarDetectionOptions.DefocusAwareDonutDetection), compute a brightness-independent
// encircled-flux radius (R_e) for LARGE/donut candidates (bbox max-dim >= DefocusDistortionSizeReference) and
// expose it on Star.NormalizedHFR. Compact stars and the OFF state keep NormalizedHFR == HFR ⇒ bit-identical.
public bool NormalizeDonutSize { get; set; } = false;
```

- [ ] **Step 3: Build.** Run: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — Expected `0 Error(s)`.

- [ ] **Step 4: Commit.** `git add … && git commit -m "feat(stardetect): Star.NormalizedHFR field + NormalizeDonutSize param"`

### Task 3.2: Map the param in `BuildStarDetectorParams`

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs:317`

- [ ] **Step 1: Add the mapping** next to `DefocusAwareDonutDetection = options.DefocusAwareDonutDetection,`:

```csharp
DefocusAwareDonutDetection = options.DefocusAwareDonutDetection,
// Reuse the donut master toggle (design §8.1): no new user option. Per-star SIZE gating happens in the detector.
NormalizeDonutSize = options.DefocusAwareDonutDetection,
```

- [ ] **Step 2: Build.** Run: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — Expected `0 Error(s)`.

- [ ] **Step 3: Commit.** `git add … && git commit -m "feat(stardetect): map NormalizeDonutSize from donut master toggle"`

### Task 3.3: Populate `NormalizedHFR` in the detector accept path (gated)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` (the accept path right after `MeasureStar`, ≈`:1670`)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectorTests.cs` (add cases)

- [ ] **Step 1: Write a failing test** asserting (a) when `NormalizeDonutSize=false`, `NormalizedHFR == HFR` for every star; (b) when true, a large synthetic annulus gets `NormalizedHFR != HFR` and closer to the cumulative half-flux radius. Add to `StarDetectorTests` following its existing detect-an-image pattern (reuse `SyntheticDefocusedStarImage.CreateAnnulus` to build a single big donut, run `StarDetector.Detect`, inspect the returned star). Use the fixture's existing params-builder; set `NormalizeDonutSize` true/false per case and `DefocusAwareDonutDetection=true`, `DefocusDistortionSizeReference=20`.

- [ ] **Step 2: Run, verify it fails.** Run: `dotnet test … --filter StarDetectorTests` — Expected FAIL (NormalizedHFR currently default 0 / equals HFR in both cases).

- [ ] **Step 3: Implement** — immediately after the successful `MeasureStar` call (`StarDetector.cs:1670`, before the `MinHFR` gate), add:

```csharp
// HFR gate still uses the legacy flux-weighted HFR (its threshold is calibrated for it). NormalizedHFR
// defaults to HFR (bit-identical) and is replaced by the brightness-independent R_e only for large/donut
// candidates when enabled (design §8.1; near-focus/compact stars keep HFR — §9 panos_lo).
star.NormalizedHFR = star.HFR;
var candidateSize = Math.Max(star.StarBoundingBox.Width, star.StarBoundingBox.Height);
if (p.NormalizeDonutSize && p.DefocusDistortionSizeReference > 0.0 && candidateSize >= p.DefocusDistortionSizeReference) {
    var plane = star.BackgroundPlane ?? LocalBackgroundPlane.Flat(star.Center.X, star.Center.Y, star.Background);
    var maxRadius = Math.Min(90.0, Math.Max(30.0, candidateSize * 1.5));
    var center = DonutEncircledRadius.RingCenter(srcImage, star.Center.X, star.Center.Y, plane, star.Background, maxRadius, srcImageNoiseSigma);
    var re = DonutEncircledRadius.Measure(srcImage, center.cx, center.cy, plane, star.Background, srcImageNoiseSigma, maxRadius, 0.5);
    if (!double.IsNaN(re.Radius) && re.Radius > 0.0) {
        star.NormalizedHFR = re.Radius;
        star.NormalizedHFRStdDev = re.StdDev;
    }
}
```

- [ ] **Step 4: Run the tests, verify they pass.** Run: `dotnet test … --filter StarDetectorTests` — Expected PASS.

- [ ] **Step 5: Run the FULL suite to confirm bit-identical default behavior.** Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — Expected: all green (equivalence tests confirm OFF-state is unchanged).

- [ ] **Step 6: Commit.** `git add … && git commit -m "feat(stardetect): populate Star.NormalizedHFR for gated donut candidates"`

---

## Phase 4 — Per-frame aggregation uses `NormalizedHFR`

### Task 4.1: Frame reduction + outlier rejection

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs:558,559,615-623`

- [ ] **Step 1: Switch the four `s.HFR` aggregation reads to `s.NormalizedHFR`** (NOT the `MinHFR`/selection reads). Exact edits:
  - `:558` `var (hfrMedian, hfrMAD) = starList.Select(s => s.NormalizedHFR).MedianMAD();`
  - `:559` outlier filter predicate → `s.NormalizedHFR <= hfrMedian + … && s.NormalizedHFR >= hfrMedian - …`
  - `:615` `result.AverageHFR = starList.Average(s => s.NormalizedHFR);`
  - `:616` variance term → `(s.NormalizedHFR - result.AverageHFR)` (both factors)
  - `:621` `var (hfrMedian, hfrMAD) = starList.Select(s => s.NormalizedHFR).MedianMAD();`
  - Leave `:601` selection heuristic (`s.HFR * 0.3 + …`) on legacy `HFR` (it's a brightness-aware *selection*, not a size aggregate).

> Bit-identity note: when the donut master is off, `NormalizedHFR == HFR` for all stars, so `AverageHFR`/`HFRStdDev` are unchanged. No equivalence test should break.

- [ ] **Step 2: Carry the field on the projection** — `ToDetectedStar` (`:633`) add `NormalizedHFR = star.NormalizedHFR,` and `NormalizedHFRStdDev = star.NormalizedHFRStdDev,`; and add the properties to `HocusFocusDetectedStar` (`:207`):

```csharp
public double NormalizedHFR { get; set; }
public double NormalizedHFRStdDev { get; set; } = double.NaN;
```

- [ ] **Step 3: Build + full test suite.** Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — Expected: all green.

- [ ] **Step 4: Commit.** `git add … && git commit -m "feat(stardetect): aggregate frame HFR over NormalizedHFR (donut-gated)"`

### Task 4.2: Review-overlay corner stats

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/StarReviewVM.cs` (the call computing `FrameStatsText` over `f.Accepted.Select(a => a.HFR)`)

- [ ] **Step 1: Switch the corner-stat source to `NormalizedHFR`.** Find the `StarReviewHfrStats.Compute(...)` call (`StarReviewVM.cs` ≈`:438`) and change `a.HFR` → `a.NormalizedHFR` for both the `Compute` call and the per-star `IsOutlier` coloring. (`StarReviewHfrStats` itself is generic over `IEnumerable<double>` — no change needed there.)

- [ ] **Step 2: Build + full suite.** Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — Expected green.

- [ ] **Step 3: Commit.** `git add … && git commit -m "feat(review): corner HFR stats use NormalizedHFR for donut frames"`

---

## Phase 5 — Sensor/tilt model uses `NormalizedHFR` + propagated σ

### Task 5.1: Per-star focus curves + uncertainty

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Inspection/SensorModel.cs:668,589-595`

- [ ] **Step 1: Use `NormalizedHFR` as the curve's y-value** — at `:668`, change `s.Star.HFR` → `s.Star.NormalizedHFR` inside the `ScatterErrorPoint(...)` construction.

- [ ] **Step 2: Prefer the propagated σ when available** — in `EstimateHfrStdDev(HocusFocusDetectedStar star)` (`:589`), prepend:

```csharp
private static double EstimateHfrStdDev(HocusFocusDetectedStar star) {
    // Donut R_e carries a background-σ-propagated uncertainty; prefer it when present (design §6 weighting).
    if (!double.IsNaN(star.NormalizedHFRStdDev) && star.NormalizedHFRStdDev > 0.0) {
        return Math.Max(star.NormalizedHFRStdDev, 1e-3);
    }
    var signal = star.AverageBrightness - star.Background;
    ...
}
```

- [ ] **Step 3: Build + full suite.** Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — Expected green.

- [ ] **Step 4: Commit.** `git add … && git commit -m "feat(inspection): sensor model fits NormalizedHFR with propagated sigma"`

### Task 5.2: End-to-end validation on the bank

- [ ] **Step 1: Re-run the §9 tilt-fit acid test path** via the existing `optimize`/inspector flow (or `TestApp agreement`) on the mufti run and at least one other strong-donut setup; confirm the per-frame `AverageHFR` brightness-spread shrinks and the sensor-model fit (R²/reduced-χ²) is no worse. Record results in `docs/donut-hfr-normalization-results.md` ("Production end-to-end" section).

- [ ] **Step 2: Commit the results update.** `git commit -m "docs(donut-hfr): production end-to-end validation results"`

---

## Out of scope (follow-on plan)

- **Approach C** (empirical log-brightness joint detrend) stays a *diagnostic-only* gate (design §5); only implement if a residual brightness slope survives `R_e` on some setup AND its per-frame VIF<3 / orthogonality / homogeneity gates pass. Not built here.
- **Nearest-neighbor integration cap** (design §4 step 3 (iii)): the production helper caps by bbox only; add an nn-aware cap if a crowded-field setup shows neighbor bleed.
- **Near-focus/compact `frac` retune** (design §4 step 4): R_e is donut-only here; revisit if it is ever extended to compact stars.

---

## Self-Review

- **Spec coverage:** A (§4) → Phase 2 + 3.3; gating (§8.1, toggle + per-star size) → Tasks 3.2/3.3; ring-fit center + adaptive cap + frac=0.5 (§4, §9) → Phase 2; background-σ propagation (§6) → `Measure` StdDev + Task 5.1; aggregation (single-frame target) → Phase 4; sensor model (both-targets) → Phase 5; B oracle + agreement report (§7, §9.5) → Phase 1; C deferred (§5) → Out of scope. All covered.
- **Bit-identity:** every consumer switched to `NormalizedHFR`, which equals `HFR` when the master toggle is off → existing `StarDetectorEquivalenceTests` must stay green (asserted in Tasks 3.3/4.1).
- **Type consistency:** `Star.NormalizedHFR`/`NormalizedHFRStdDev`, `HocusFocusDetectedStar.NormalizedHFR`/`NormalizedHFRStdDev`, `StarDetectorParams.NormalizeDonutSize`, `DonutEncircledRadius.{RingCenter,Measure,Result.Radius,Result.StdDev}` used consistently across Phases 2–5.
- **Placeholders:** none — code shown for every code step; the two TestApp tasks that mirror existing runners reference exact patterns to copy.
