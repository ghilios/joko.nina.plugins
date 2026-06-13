# Focus-Sweep Evidence Base Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the measurement evidence base for the star-detection accuracy analysis — a synthetic defocused/annular star generator with `MeasureStar` bias tests (quantifying findings F3/F4), and a headless `focus-sweep` diagnostic in TestApp that reports detection metrics vs focuser position over a saved AutoFocus run.

**Architecture:** Two independent pieces. **Piece A** (Tests project): pure in-memory synthetic generators + ground-truth HFR helpers + NUnit tests that pin `MeasureStar` against known truth and measure the F3 (soft-threshold) and F4 (σ-mismatch) bias magnitudes. **Piece B** (TestApp): a new `focus-sweep` subcommand mirroring the existing `contamination` diagnostic — loads a saved AF-run folder, runs detection per frame, and writes per-focuser-position CSV + summary + a ScottPlot V-curve PNG. No production plugin code changes. CI stays 100% synthetic (TestApp is not in CI).

**Tech Stack:** C# / .NET 8 (`net8.0-windows7.0`), NUnit 4.4.0, OpenCvSharp4 (`Mat`, `CV_32F`), ScottPlot.WPF 4.1.59, alglib.net. Build via `rtk dotnet` (or `cmd.exe /c "dotnet …"`).

**Reference spec:** [`docs/focus-sweep-evidence-base-design.md`](../docs/focus-sweep-evidence-base-design.md). **Branch:** `ghilios/focus-sweep-evidence-base` (already created).

---

## Conventions for every commit

Use the required identity (project CLAUDE.md). Each commit command in this plan is written as:

```bash
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "<subject>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

Build/test (10-min timeout; rtk build mislabels success header as `fail` — trust `errors=0` and exit code):

```bash
rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo
rtk dotnet test  Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~MeasureStarBiasTests"
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
```

---

## File Structure

| File | New/Mod | Responsibility |
|---|---|---|
| `Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/SyntheticDefocusedStarImage.cs` | New | Render uniform disk / annulus (supersampled, optional edge blur) + seeded Gaussian noise. |
| `Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/HfrGroundTruth.cs` | New | Closed-form + numerical flux-weighted-mean-radius ground truth. |
| `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeasureStarBiasTests.cs` | New | Pin `MeasureStar` vs truth; measure F3/F4 magnitudes; donut sanity. |
| `TestApp/DiagnosticUtil.cs` | New | Shared diagnostic helpers `GetArg`, `LoadFloatMat` (relocated from `ContaminationDiagnosticRunner`). |
| `TestApp/ContaminationDiagnosticRunner.cs` | Mod | Delegate `GetArg`/`LoadFloatMat` to `DiagnosticUtil` (verbatim relocation; behavior unchanged). |
| `TestApp/FocusSweepDiagnosticRunner.cs` | New | The `focus-sweep` diagnostic: parse AF folder, detect per frame, aggregate per position, write CSV/summary/PNG, `--synthesize`. |
| `TestApp/Program.cs` | Mod | Route `focus-sweep` subcommand. |

---

# PIECE A — Synthetic generator + MeasureStar bias tests

### Task A1: Synthetic generators + ground-truth helpers

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/SyntheticDefocusedStarImage.cs`
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/HfrGroundTruth.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/HfrGroundTruthTests.cs`

- [ ] **Step 1: Write the generator**

Create `SyntheticDefocusedStarImage.cs`:

```csharp
using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Renders defocused-star test images (uniform disk, annular donut) whose continuous radial profile is
    /// known, so MeasureStar's flux-weighted-mean-radius output can be compared to analytic ground truth.
    /// Each pixel is supersampled so the rendered value equals the analytic mean over the pixel area, keeping
    /// the rim faithful (otherwise discretization at the edge dominates the error budget).
    /// </summary>
    internal static class SyntheticDefocusedStarImage {
        private const int SuperSample = 4; // NxN subsamples per pixel

        public static Mat CreateDisk(int width, int height, double centerX, double centerY,
                double radius, double peak, double background, double edgeBlurSigma = 0.0) {
            return Render(width, height, centerX, centerY, background,
                r => DiskIntensity(r, radius, peak, edgeBlurSigma));
        }

        public static Mat CreateAnnulus(int width, int height, double centerX, double centerY,
                double innerRadius, double outerRadius, double peak, double background, double edgeBlurSigma = 0.0) {
            return Render(width, height, centerX, centerY, background,
                r => AnnulusIntensity(r, innerRadius, outerRadius, peak, edgeBlurSigma));
        }

        /// <summary>Adds zero-mean Gaussian noise of the given standard deviation using a seeded RNG (reproducible).</summary>
        public static void AddGaussianNoise(Mat image, double sigma, int seed) {
            var rng = new Random(seed);
            int n = image.Width * image.Height;
            unsafe {
                var data = (float*)image.DataPointer;
                for (int i = 0; i < n; ++i) {
                    double u1 = 1.0 - rng.NextDouble();
                    double u2 = 1.0 - rng.NextDouble();
                    double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2); // Box-Muller
                    data[i] += (float)(sigma * z);
                }
            }
        }

        private static Mat Render(int width, int height, double cx, double cy, double background,
                Func<double, double> radialIntensity) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            double inv = 1.0 / SuperSample;
            int sub = SuperSample * SuperSample;
            unsafe {
                var data = (float*)mat.DataPointer;
                for (int y = 0; y < height; ++y) {
                    for (int x = 0; x < width; ++x) {
                        double sum = 0.0;
                        for (int sy = 0; sy < SuperSample; ++sy) {
                            for (int sx = 0; sx < SuperSample; ++sx) {
                                double px = x - 0.5 + (sx + 0.5) * inv;
                                double py = y - 0.5 + (sy + 0.5) * inv;
                                double dx = px - cx, dy = py - cy;
                                sum += radialIntensity(Math.Sqrt(dx * dx + dy * dy));
                            }
                        }
                        data[y * width + x] = (float)(background + sum / sub);
                    }
                }
            }
            return mat;
        }

        private static double DiskIntensity(double r, double radius, double peak, double edgeBlurSigma) {
            if (edgeBlurSigma <= 0.0) return r <= radius ? peak : 0.0;
            return peak * 0.5 * Erfc((r - radius) / (edgeBlurSigma * Math.Sqrt(2.0)));
        }

        private static double AnnulusIntensity(double r, double inner, double outer, double peak, double edgeBlurSigma) {
            if (edgeBlurSigma <= 0.0) return (r >= inner && r <= outer) ? peak : 0.0;
            double rise = 0.5 * Erfc((inner - r) / (edgeBlurSigma * Math.Sqrt(2.0)));
            double fall = 0.5 * Erfc((r - outer) / (edgeBlurSigma * Math.Sqrt(2.0)));
            return peak * rise * fall;
        }

        // Abramowitz & Stegun 7.1.26 erfc approximation (max error ~1.2e-7).
        private static double Erfc(double x) {
            double z = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.5 * z);
            double ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
                t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
                t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0.0 ? ans : 2.0 - ans;
        }
    }
}
```

- [ ] **Step 2: Write the ground-truth helper**

Create `HfrGroundTruth.cs`:

```csharp
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Ground-truth flux-weighted mean radius (the quantity MeasureStar approximates) for the synthetic
    /// shapes, with aperture R >= the star's support:  HFR = ∫0^R I(r) r^2 dr / ∫0^R I(r) r dr.
    /// </summary>
    internal static class HfrGroundTruth {
        /// <summary>Uniform filled disk of radius a:  HFR = 2a/3.</summary>
        public static double Disk(double radius) => 2.0 * radius / 3.0;

        /// <summary>Uniform annulus [inner, outer]:  HFR = (2/3)(a^3 - b^3)/(a^2 - b^2).</summary>
        public static double Annulus(double inner, double outer) =>
            (2.0 / 3.0) * (Math.Pow(outer, 3) - Math.Pow(inner, 3)) / (Math.Pow(outer, 2) - Math.Pow(inner, 2));

        /// <summary>Gaussian exp(-r^2/2σ^2), large aperture:  HFR = σ·sqrt(π/2) ≈ 1.2533σ.</summary>
        public static double Gaussian(double sigma) => sigma * Math.Sqrt(Math.PI / 2.0);

        /// <summary>Numerical flux-weighted mean radius of a radial profile over [0, R] (validates the closed forms).</summary>
        public static double Numerical(Func<double, double> intensity, double apertureRadius, double step = 0.001) {
            double num = 0.0, den = 0.0;
            for (double r = 0.0; r <= apertureRadius; r += step) {
                double i = intensity(r);
                num += i * r * r * step;
                den += i * r * step;
            }
            return den > 0 ? num / den : 0.0;
        }
    }
}
```

- [ ] **Step 3: Write a self-test pinning the ground-truth math**

Create `HfrGroundTruthTests.cs`:

```csharp
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    [TestFixture]
    public class HfrGroundTruthTests {

        [Test]
        public void Disk_ClosedFormMatchesNumericalIntegration() {
            const double a = 12.0;
            double numerical = HfrGroundTruth.Numerical(r => r <= a ? 1.0 : 0.0, apertureRadius: 40.0);
            Assert.That(HfrGroundTruth.Disk(a), Is.EqualTo(numerical).Within(0.01));
            Assert.That(HfrGroundTruth.Disk(a), Is.EqualTo(8.0).Within(1e-9));
        }

        [Test]
        public void Annulus_ClosedFormMatchesNumericalIntegration() {
            const double b = 6.0, a = 14.0;
            double numerical = HfrGroundTruth.Numerical(r => (r >= b && r <= a) ? 1.0 : 0.0, apertureRadius: 40.0);
            Assert.That(HfrGroundTruth.Annulus(b, a), Is.EqualTo(numerical).Within(0.01));
        }

        [Test]
        public void Gaussian_ClosedFormMatchesNumericalIntegration() {
            const double sigma = 4.0;
            double numerical = HfrGroundTruth.Numerical(r => Math.Exp(-r * r / (2 * sigma * sigma)), apertureRadius: 60.0);
            Assert.That(HfrGroundTruth.Gaussian(sigma), Is.EqualTo(numerical).Within(0.01));
        }
    }
}
```

- [ ] **Step 4: Build and run**

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~HfrGroundTruthTests"
```
Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/SyntheticDefocusedStarImage.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/HfrGroundTruth.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/HfrGroundTruthTests.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Add synthetic defocused-star generator and HFR ground truth" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task A2: Clean unbiasedness tests (estimator correctness)

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeasureStarBiasTests.cs`

- [ ] **Step 1: Write the test fixture with the three clean tests**

Create `MeasureStarBiasTests.cs`:

```csharp
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Pins MeasureStar's HFR against analytic ground truth on controlled synthetic shapes, and measures the
    /// magnitude of two confirmed biases: F3 (the soft-threshold τ is subtracted from each pixel, pulling HFR
    /// down on stars with a radial gradient) and F4 (an understated σ lets large-radius noise inflate HFR).
    /// All inputs are deterministic (seeded noise); bias tests assert direction + a bounded magnitude and log
    /// the measured numbers via TestContext so the magnitudes are visible without being brittle.
    /// </summary>
    [TestFixture]
    public class MeasureStarBiasTests {
        private const int Size = 81;       // R = min(bbox)/2 = 40.5, large vs the shapes below (low truncation)
        private const double Cx = 40.0;
        private const double Cy = 40.0;

        private static Star NewStar(double background) => new Star {
            Center = new Point2d(Cx, Cy),
            StarBoundingBox = new Rect(0, 0, Size, Size),
            Background = background
        };

        [Test]
        public void MeasureStar_CleanDisk_MatchesTwoThirdsRadius() {
            const double a = 12.0, peak = 1.0, bg = 0.0;
            using var image = SyntheticDefocusedStarImage.CreateDisk(Size, Size, Cx, Cy, a, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            var star = NewStar(bg);
            Assert.That(detector.MeasureStar(image, star, p, 0.0), Is.True);
            double truth = HfrGroundTruth.Disk(a); // 8.0
            TestContext.WriteLine($"clean disk: measured={star.HFR:F4} truth={truth:F4}");
            Assert.That(star.HFR, Is.EqualTo(truth).Within(0.3));
        }

        [Test]
        public void MeasureStar_CleanAnnulus_MatchesClosedForm() {
            const double inner = 6.0, outer = 14.0, peak = 1.0, bg = 0.0;
            using var image = SyntheticDefocusedStarImage.CreateAnnulus(Size, Size, Cx, Cy, inner, outer, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            var star = NewStar(bg);
            Assert.That(detector.MeasureStar(image, star, p, 0.0), Is.True);
            double truth = HfrGroundTruth.Annulus(inner, outer); // ~10.53
            TestContext.WriteLine($"clean annulus: measured={star.HFR:F4} truth={truth:F4}");
            Assert.That(star.HFR, Is.EqualTo(truth).Within(0.3));
        }

        [Test]
        public void MeasureStar_CleanGaussian_MatchesSigmaRootPiOverTwo() {
            const double sigma = 4.0, peak = 1.0, bg = 0.0; // R = 40.5 = 10σ → truncation negligible
            using var image = SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            var star = NewStar(bg);
            Assert.That(detector.MeasureStar(image, star, p, 0.0), Is.True);
            double truth = HfrGroundTruth.Gaussian(sigma); // ~5.013
            TestContext.WriteLine($"clean gaussian: measured={star.HFR:F4} truth={truth:F4}");
            Assert.That(star.HFR, Is.EqualTo(truth).Within(0.3));
        }
    }
}
```

- [ ] **Step 2: Build and run**

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~MeasureStarBiasTests"
```
Expected: 3 tests PASS. If a clean test is off by >0.3, read the logged measured-vs-truth values and adjust the tolerance to a documented value (the supersampled rim should keep it within a few percent).

- [ ] **Step 3: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeasureStarBiasTests.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Pin MeasureStar HFR against analytic ground truth" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task A3: F3 soft-threshold bias tests

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeasureStarBiasTests.cs`

- [ ] **Step 1: Add the three F3 tests inside the fixture**

Insert before the closing brace of the `MeasureStarBiasTests` class (τ = `StarClippingMultiplier · noiseSigma`; the tests fix the multiplier and sweep `noiseSigma`):

```csharp
        [Test]
        public void MeasureStar_UniformDisk_SoftThresholdBarelyChangesHfr() {
            // A uniform disk has no radial gradient, so subtracting a constant τ scales every surviving pixel
            // equally and the flux-weighted radius is (almost) unchanged — only the partially-covered rim
            // shifts. This isolates WHY the F3 bias requires a gradient (contrast with the Gaussian below).
            const double a = 12.0, peak = 1.0, bg = 0.0;
            using var image = SyntheticDefocusedStarImage.CreateDisk(Size, Size, Cx, Cy, a, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var pNoClip = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            var pClip = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 1.0 };
            var s0 = NewStar(bg);
            var sT = NewStar(bg);
            detector.MeasureStar(image, s0, pNoClip, 0.0);
            detector.MeasureStar(image, sT, pClip, 0.3); // τ = 1.0 * 0.3
            TestContext.WriteLine($"uniform disk: hfr(τ=0)={s0.HFR:F4} hfr(τ=0.3)={sT.HFR:F4} Δ={sT.HFR - s0.HFR:F4}");
            Assert.That(sT.HFR, Is.EqualTo(s0.HFR).Within(0.15));
        }

        [Test]
        public void MeasureStar_GaussianSoftThreshold_BiasesHfrDownward() {
            const double sigma = 5.0, peak = 1.0, bg = 0.0;
            using var image = SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 1.0 }; // τ == noiseSigma
            double[] taus = { 0.0, 0.02, 0.05, 0.10, 0.20 };
            var hfrs = new double[taus.Length];
            for (int i = 0; i < taus.Length; ++i) {
                var star = NewStar(bg);
                detector.MeasureStar(image, star, p, taus[i]);
                hfrs[i] = star.HFR;
                TestContext.WriteLine($"τ={taus[i]:F3} → HFR={hfrs[i]:F4} (bias {hfrs[i] - hfrs[0]:F4})");
            }
            for (int i = 1; i < hfrs.Length; ++i)
                Assert.That(hfrs[i], Is.LessThan(hfrs[i - 1]), $"HFR should decrease monotonically as τ grows (i={i})");
            Assert.That(hfrs[0] - hfrs[^1], Is.GreaterThan(0.1), "soft-threshold should bias HFR downward measurably at τ=0.2");
        }

        [Test]
        public void MeasureStar_GaussianSoftThreshold_RelativeBiasGrowsForFainterStars() {
            const double sigma = 5.0, bg = 0.0, tau = 0.05;
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 1.0 };
            double[] peaks = { 1.0, 0.5, 0.25, 0.125 };
            double prevRel = -1.0;
            foreach (var peak in peaks) {
                using var image = SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, bg);
                var s0 = NewStar(bg);
                var sT = NewStar(bg);
                detector.MeasureStar(image, s0, p, 0.0);
                detector.MeasureStar(image, sT, p, tau);
                double rel = (s0.HFR - sT.HFR) / s0.HFR;
                TestContext.WriteLine($"peak={peak:F3} τ/peak={tau / peak:F3} relBias={rel:P2}");
                if (prevRel >= 0)
                    Assert.That(rel, Is.GreaterThan(prevRel), "relative HFR bias should grow as the star gets fainter");
                prevRel = rel;
            }
        }
```

- [ ] **Step 2: Build and run**

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~MeasureStarBiasTests"
```
Expected: 6 tests PASS (3 from A2 + 3 new). The logged τ→HFR curve is the F3 magnitude evidence.

- [ ] **Step 3: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeasureStarBiasTests.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Measure F3 soft-threshold HFR bias on synthetic stars" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task A4: F4 σ-mismatch test + donut sanity test

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeasureStarBiasTests.cs`

- [ ] **Step 1: Add the F4 and donut tests inside the fixture**

Insert before the closing brace of the class:

```csharp
        [Test]
        public void MeasureStar_UnderstatedSigma_InflatesHfrUnderNoise() {
            // A small star in a large aperture surrounded by noise. With correctly-scaled τ the noise is
            // clipped; with an understated σ (production measures σ on a ~4-5× smoothed copy but samples the
            // sharp image) positive noise at large radius leaks into the flux sum and inflates HFR (F4).
            const double sigma = 3.0, peak = 1.0, bg = 0.0;
            const double sigmaSharp = 0.02; // true white-noise σ of the measurement image
            const int seed = 12345;
            const int bigSize = 121;        // R = 60.5 = 20σ → a large pure-noise annulus inside the aperture
            const double bigC = 60.0;
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 2.0 };

            double MeasureWith(double noiseSigmaArg) {
                using var image = SyntheticGaussianStarImage.Create(bigSize, bigSize, bigC, bigC, sigma, sigma, peak, bg);
                SyntheticDefocusedStarImage.AddGaussianNoise(image, sigmaSharp, seed);
                var star = new Star {
                    Center = new Point2d(bigC, bigC),
                    StarBoundingBox = new Rect(0, 0, bigSize, bigSize),
                    Background = bg
                };
                detector.MeasureStar(image, star, p, noiseSigmaArg);
                return star.HFR;
            }

            double hfrTrue = MeasureWith(sigmaSharp);          // τ = 2·σ_sharp     → noise clipped
            double hfrUnderstated = MeasureWith(sigmaSharp / 5.0); // τ = 2·σ_sharp/5 → noise leaks in
            TestContext.WriteLine($"HFR(trueσ)={hfrTrue:F4}  HFR(understatedσ)={hfrUnderstated:F4}  inflation={hfrUnderstated - hfrTrue:F4}");
            Assert.That(hfrUnderstated, Is.GreaterThan(hfrTrue + 0.5), "understated σ should inflate HFR by leaking large-radius noise");
        }

        [Test]
        public void MeasureStar_Annulus_HfrExceedsFilledDisk_SameOuterRadius() {
            const double outer = 14.0, inner = 8.0, peak = 1.0, bg = 0.0;
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            using var disk = SyntheticDefocusedStarImage.CreateDisk(Size, Size, Cx, Cy, outer, peak, bg);
            using var annulus = SyntheticDefocusedStarImage.CreateAnnulus(Size, Size, Cx, Cy, inner, outer, peak, bg);
            var sd = NewStar(bg);
            var sa = NewStar(bg);
            detector.MeasureStar(disk, sd, p, 0.0);
            detector.MeasureStar(annulus, sa, p, 0.0);
            TestContext.WriteLine($"disk HFR={sd.HFR:F4}  annulus HFR={sa.HFR:F4}");
            Assert.That(sa.HFR, Is.GreaterThan(sd.HFR), "annulus pushes flux outward → larger HFR than a filled disk of the same outer radius");
        }
```

- [ ] **Step 2: Build and run**

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~MeasureStarBiasTests"
```
Expected: 8 tests PASS. The logged inflation value is the F4 magnitude evidence.

- [ ] **Step 3: Run the full suite to confirm nothing else broke**

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo
```
Expected: all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeasureStarBiasTests.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Measure F4 noise-inflation HFR bias and pin donut HFR ordering" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

# PIECE B — focus-sweep diagnostic (TestApp)

### Task B1: Extract shared diagnostic helpers

**Files:**
- Create: `Joko.NINA.Plugins/TestApp/DiagnosticUtil.cs`
- Modify: `Joko.NINA.Plugins/TestApp/ContaminationDiagnosticRunner.cs`

`GetArg` and `LoadFloatMat` are needed verbatim by the new runner. Relocate them to a shared static class (compiler-verified, pure relocation — runtime behavior of the contamination tool is unchanged).

- [ ] **Step 1: Create `DiagnosticUtil.cs`**

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.FileFormat.FITS;
using NINA.Image.FileFormat.XISF;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp {

    /// <summary>Helpers shared by the headless diagnostic runners (contamination, focus-sweep).</summary>
    internal static class DiagnosticUtil {

        /// <summary>Returns the value following <paramref name="name"/> in <paramref name="args"/>, or null.</summary>
        public static string GetArg(string[] args, string name) {
            for (int i = 0; i < args.Length - 1; ++i) {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) {
                    return args[i + 1];
                }
            }
            return null;
        }

        /// <summary>
        /// Loads an image file as a CV_32F Mat normalized to [0,1]. .tif/.tiff are read directly; .xisf/.fits/.fit
        /// go through NINA's loaders (which need a profile). profileService may be null for .tif-only callers.
        /// </summary>
        public static async Task<Mat> LoadFloatMat(string path, IProfileService profileService) {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff") {
                using var src = new Mat(path, ImreadModes.Unchanged);
                var dst = new Mat();
                Program.ConvertToFloat(src, dst);
                return dst;
            }
            if (ext == ".xisf" || ext == ".fits" || ext == ".fit") {
                if (profileService == null) {
                    throw new InvalidOperationException($"Loading '{ext}' requires a NINA profile; omit --default-params or use .tif frames.");
                }
                var factory = new ImageDataFactory(profileService, new StubBehaviorSelector<IStarDetection>(new StubStarDetection()), new StubBehaviorSelector<IStarAnnotator>());
                var uri = new Uri(Path.GetFullPath(path));
                IImageData imageData = ext == ".xisf"
                    ? await XISF.Load(uri, false, factory, CancellationToken.None)
                    : await FITS.Load(uri, false, factory, CancellationToken.None);
                return CvImageUtility.ToOpenCVMat(imageData);
            }
            throw new NotSupportedException($"Unsupported image extension '{ext}'. Supported: .tif/.tiff, .xisf, .fits/.fit");
        }
    }
}
```

- [ ] **Step 2: Delete the two private methods from `ContaminationDiagnosticRunner.cs`**

Remove the `private static async Task<Mat> LoadFloatMat(...) { ... }` block (currently lines ~198-215) and the `private static string GetArg(...) { ... }` block (currently lines ~505-512) entirely.

- [ ] **Step 3: Update the two call sites in `ContaminationDiagnosticRunner.cs`**

`RunImpl` calls `GetArg(args, "--image")`, `GetArg(args, "--profile-id")`, `GetArg(args, "--out")`, `GetArg(args, "--sensitivity")`, `GetArg(args, "--sensitivity-sweep")`, and `await LoadFloatMat(imagePath, profileService)`. Prefix each with `DiagnosticUtil.`:

```csharp
            var imagePath = DiagnosticUtil.GetArg(args, "--image");
            // ...
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var outDir = DiagnosticUtil.GetArg(args, "--out");
            // ...
            var sensitivityArg = DiagnosticUtil.GetArg(args, "--sensitivity");
            // ...
            using var srcFloat = await DiagnosticUtil.LoadFloatMat(imagePath, profileService);
            // ...
            var sweepArg = DiagnosticUtil.GetArg(args, "--sensitivity-sweep");
```

(There are no other references; `using` lines for the FITS/XISF/ImageData/Profile imports that only `LoadFloatMat` used may now be unused in `ContaminationDiagnosticRunner.cs` — leave them, they are harmless, or remove if the build warns and you want it clean.)

- [ ] **Step 4: Build TestApp**

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
```
Expected: build succeeds, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/TestApp/DiagnosticUtil.cs Joko.NINA.Plugins/TestApp/ContaminationDiagnosticRunner.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Extract shared diagnostic helpers into DiagnosticUtil" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task B2: focus-sweep runner core (parse → detect → aggregate → CSV/summary) + dispatch

**Files:**
- Create: `Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs`
- Modify: `Joko.NINA.Plugins/TestApp/Program.cs`

- [ ] **Step 1: Create `FocusSweepDiagnosticRunner.cs` (core, no PNG/synthesize yet)**

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Logger = NINA.Core.Utility.Logger;

namespace TestApp {

    /// <summary>
    /// Headless diagnostic: replays a saved AutoFocus run through HocusFocus star detection and reports star
    /// count, HFR (median + robust spread), and per-reason rejection counts as a function of focuser position.
    /// Turns the analysis's "detection degrades at defocus" mechanisms (F1/F2) into measured numbers. Mirrors
    /// ContaminationDiagnosticRunner; TestApp is not run in CI, so this consumes real data on disk only.
    /// </summary>
    internal static class FocusSweepDiagnosticRunner {

        // Mirrors AutoFocusEngine.IMAGE_FILE_REGEX (private) so the diagnostic stays decoupled from the engine.
        private static readonly Regex ImageFileRegex = new Regex(
            @"^(?<IMAGE_INDEX>\d+)_Frame(?<FRAME_NUMBER>\d+)_BitDepth(?<BITDEPTH>\d+)_Bayered(?<BAYERED>\d)_Focuser(?<FOCUSER>\d+)(_HFR(?<HFR>(\d+)(\.\d+)?))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private sealed class Frame {
            public string Path;
            public int FocuserPosition;
        }

        private sealed class PositionAccum {
            public int FocuserPosition;
            public int Frames;
            public readonly List<double> Hfrs = new List<double>();
            public long StructureCandidates, TotalDetected, TooSmall, OnBorder, TooDistorted, Degenerate,
                Saturated, LowSensitivity, NotCentered, TooFlat, TooLowHFR, HFRAnalysisFailed, PSFFitFailed,
                ContaminationSuspected, OutsideROI;
        }

        public static async Task Run(string[] args) {
            try {
                await RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "Focus-sweep diagnostic run failed");
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunImpl(string[] args) {
            var afRun = DiagnosticUtil.GetArg(args, "--af-run");
            var synthesizeDir = DiagnosticUtil.GetArg(args, "--synthesize");
            bool defaultParams = args.Any(a => a.Equals("--default-params", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(synthesizeDir)) {
                int count = 9;
                var countArg = DiagnosticUtil.GetArg(args, "--synthesize-count");
                if (!string.IsNullOrWhiteSpace(countArg)) count = int.Parse(countArg, CultureInfo.InvariantCulture);
                SynthesizeAfRun(synthesizeDir, count);
                Console.WriteLine($"Synthesized {count} frames into {synthesizeDir}");
                afRun = synthesizeDir; // proceed to sweep the freshly-synthesized folder
            }

            if (string.IsNullOrWhiteSpace(afRun)) {
                Console.Error.WriteLine("Usage: TestApp focus-sweep --af-run <dir> [--profile-id <guid>] [--out <dir>] [--default-params]");
                Console.Error.WriteLine("       TestApp focus-sweep --synthesize <dir> [--synthesize-count <n>] --default-params [--out <dir>]");
                Environment.ExitCode = 2;
                return;
            }
            if (!Directory.Exists(afRun)) {
                throw new DirectoryNotFoundException($"AF-run folder not found: {afRun}");
            }

            var outDir = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-diag", "focus-sweep");
            }
            Directory.CreateDirectory(outDir);
            Logger.SetLogLevel(LogLevelEnum.TRACE);

            var frames = ParseAfRun(afRun);
            if (frames.Count == 0) {
                throw new InvalidOperationException($"No AF frames matched the filename pattern in {afRun}");
            }
            Console.WriteLine($"AF run: {afRun} ({frames.Count} frames, {frames.Select(f => f.FocuserPosition).Distinct().Count()} positions)");

            // Build detector params: from the real profile (default) or library defaults (--default-params).
            IProfileService profileService = null;
            StarDetectorParams baseParams;
            if (defaultParams) {
                baseParams = new StarDetectorParams();
                Console.WriteLine("Using default StarDetectorParams (--default-params; no profile loaded)");
            } else {
                if (Application.Current == null) {
                    new Application(); // ProfileService.ActiveProfile setter touches Application.Current.Resources
                }
                profileService = new ProfileService();
                profileService.TryLoad(DiagnosticUtil.GetArg(args, "--profile-id") ?? string.Empty);
                if (profileService.ActiveProfile == null) {
                    throw new InvalidOperationException("No active NINA profile. Pass --profile-id, run NINA once, or use --default-params.");
                }
                Console.WriteLine($"Profile: {profileService.ActiveProfile.Name} ({profileService.ActiveProfile.Id})");
                var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
                var accessor = new PluginOptionsAccessor(profileService, guid.Value);
                var options = new StarDetectionOptions(profileService, accessor);
                baseParams = HocusFocusStarDetection.BuildStarDetectorParams(options);
            }
            // Mirror real AF: PSF modeling is disabled during AutoFocus, so HFR is the measured quantity.
            baseParams.ModelPSF = false;

            var byPosition = new SortedDictionary<int, PositionAccum>();
            var detector = new StarDetector(new AlglibAPI());
            foreach (var frame in frames) {
                using var img = await DiagnosticUtil.LoadFloatMat(frame.Path, profileService);
                var result = await detector.Detect(img, baseParams, null, CancellationToken.None);
                if (!byPosition.TryGetValue(frame.FocuserPosition, out var accum)) {
                    accum = new PositionAccum { FocuserPosition = frame.FocuserPosition };
                    byPosition[frame.FocuserPosition] = accum;
                }
                Accumulate(accum, result);
                Console.WriteLine($"  pos {frame.FocuserPosition}: {result.DetectedStars.Count} stars");
            }

            var rows = byPosition.Values.ToList();
            WriteCsv(Path.Combine(outDir, "focus_sweep.csv"), rows);
            WriteSummary(Path.Combine(outDir, "focus_sweep_summary.txt"), afRun, baseParams, rows);
            Console.WriteLine($"Wrote focus_sweep.csv, focus_sweep_summary.txt to {outDir}");
        }

        private static List<Frame> ParseAfRun(string dir) {
            var root = new DirectoryInfo(dir);
            var search = root;
            // Accept either an attempt folder directly or a parent with a single attempt* subfolder.
            if (!root.GetFiles().Any(f => ImageFileRegex.IsMatch(Path.GetFileNameWithoutExtension(f.Name)))) {
                var attempt = root.EnumerateDirectories("attempt*").FirstOrDefault();
                if (attempt != null) search = attempt;
            }
            var frames = new List<Frame>();
            foreach (var file in search.GetFiles()) {
                var m = ImageFileRegex.Match(Path.GetFileNameWithoutExtension(file.Name));
                if (!m.Success) continue;
                if (!int.TryParse(m.Groups["FOCUSER"].Value, out var pos)) continue;
                frames.Add(new Frame { Path = file.FullName, FocuserPosition = pos });
            }
            return frames;
        }

        private static void Accumulate(PositionAccum a, HocusFocusStarDetectorResult result) {
            a.Frames++;
            var m = result.Metrics;
            a.StructureCandidates += m.StructureCandidates;
            a.TotalDetected += m.TotalDetected;
            a.TooSmall += m.TooSmall;
            a.OnBorder += m.OnBorder;
            a.TooDistorted += m.TooDistorted;
            a.Degenerate += m.Degenerate;
            a.Saturated += m.Saturated;
            a.LowSensitivity += m.LowSensitivity;
            a.NotCentered += m.NotCentered;
            a.TooFlat += m.TooFlat;
            a.TooLowHFR += m.TooLowHFR;
            a.HFRAnalysisFailed += m.HFRAnalysisFailed;
            a.PSFFitFailed += m.PSFFitFailed;
            a.ContaminationSuspected += m.ContaminationSuspected;
            a.OutsideROI += m.OutsideROI;
            foreach (var s in result.DetectedStars) a.Hfrs.Add(s.HFR);
        }

        private static (double median, double mad) MedianMad(List<double> values) {
            if (values.Count == 0) return (double.NaN, double.NaN);
            return values.MedianMAD();
        }

        private static void WriteCsv(string path, List<PositionAccum> rows) {
            var sb = new StringBuilder();
            sb.AppendLine("FocuserPosition,Frames,StarCount,MedianHFR,MAD_HFR,StructureCandidates,TotalDetected," +
                "TooSmall,OnBorder,TooDistorted,Degenerate,Saturated,LowSensitivity,NotCentered,TooFlat," +
                "TooLowHFR,HFRAnalysisFailed,PSFFitFailed,ContaminationSuspected,OutsideROI");
            foreach (var r in rows) {
                var (median, mad) = MedianMad(r.Hfrs);
                sb.AppendLine(string.Join(",",
                    r.FocuserPosition, r.Frames, r.Hfrs.Count, F(median), F(mad),
                    r.StructureCandidates, r.TotalDetected, r.TooSmall, r.OnBorder, r.TooDistorted, r.Degenerate,
                    r.Saturated, r.LowSensitivity, r.NotCentered, r.TooFlat, r.TooLowHFR, r.HFRAnalysisFailed,
                    r.PSFFitFailed, r.ContaminationSuspected, r.OutsideROI));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSummary(string path, string afRun, StarDetectorParams p, List<PositionAccum> rows) {
            var sb = new StringBuilder();
            sb.AppendLine($"AF run: {afRun}");
            sb.AppendLine($"Params: {p}");
            sb.AppendLine($"Positions: {rows.Count}");
            sb.AppendLine($"Total frames: {rows.Sum(r => r.Frames)}");
            var withStars = rows.Where(r => r.Hfrs.Count > 0).ToList();
            if (withStars.Count > 0) {
                var best = withStars.OrderBy(r => MedianMad(r.Hfrs).median).First();
                sb.AppendLine($"Min median-HFR position (detector's-eye focus estimate): {best.FocuserPosition} " +
                    $"(median HFR {F(MedianMad(best.Hfrs).median)}, {best.Hfrs.Count} stars)");
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static string F(double v) => double.IsNaN(v) ? "NaN" : v.ToString("G9", CultureInfo.InvariantCulture);

        // --- Synthesize: replaced with the real body in Task B4. Placeholder keeps the file compiling. ---
        private static void SynthesizeAfRun(string dir, int count) {
            throw new NotImplementedException("Implemented in Task B4");
        }
    }
}
```

- [ ] **Step 2: Route the subcommand in `Program.cs`**

In `Program.Main`, add this block immediately after the `fit-quality` block and before the `contamination` block:

```csharp
            // Headless focus-sweep diagnostic mode: `TestApp focus-sweep --af-run <dir> ...`
            if (args.Length > 0 && args[0].Equals("focus-sweep", StringComparison.OrdinalIgnoreCase)) {
                await FocusSweepDiagnosticRunner.Run(args);
                return;
            }
```

- [ ] **Step 3: Build TestApp**

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
```
Expected: build succeeds, 0 errors (the `SynthesizeAfRun` placeholder throws at runtime only).

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs Joko.NINA.Plugins/TestApp/Program.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Add focus-sweep diagnostic core (CSV + summary)" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task B3: V-curve PNG (ScottPlot)

**Files:**
- Modify: `Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs`

- [ ] **Step 1: Add the PNG writer**

Add this method to the class (after `WriteSummary`):

```csharp
        private static void WriteVCurvePng(string path, List<PositionAccum> rows) {
            try {
                var data = rows.Where(r => r.Hfrs.Count > 0)
                    .Select(r => (x: (double)r.FocuserPosition, y: MedianMad(r.Hfrs)))
                    .OrderBy(t => t.x).ToList();
                if (data.Count == 0) {
                    Logger.Warning("V-curve PNG skipped: no positions had detected stars");
                    return;
                }
                var xs = data.Select(d => d.x).ToArray();
                var ys = data.Select(d => d.y.median).ToArray();
                var err = data.Select(d => double.IsNaN(d.y.mad) ? 0.0 : d.y.mad).ToArray();
                var plt = new ScottPlot.Plot(900, 600);
                plt.AddScatter(xs, ys, markerSize: 6);
                plt.AddErrorBars(xs, ys, null, err);
                plt.XLabel("Focuser Position");
                plt.YLabel("Median HFR (px)");
                plt.Title("Focus Sweep — detector HFR vs position");
                plt.SaveFig(path);
            } catch (Exception ex) {
                Logger.Warning($"V-curve PNG render failed ({ex.Message}); CSV/summary still written");
            }
        }
```

- [ ] **Step 2: Call it from `RunImpl`**

Replace the two output lines in `RunImpl`:

```csharp
            WriteCsv(Path.Combine(outDir, "focus_sweep.csv"), rows);
            WriteSummary(Path.Combine(outDir, "focus_sweep_summary.txt"), afRun, baseParams, rows);
            Console.WriteLine($"Wrote focus_sweep.csv, focus_sweep_summary.txt to {outDir}");
```

with:

```csharp
            WriteCsv(Path.Combine(outDir, "focus_sweep.csv"), rows);
            WriteSummary(Path.Combine(outDir, "focus_sweep_summary.txt"), afRun, baseParams, rows);
            WriteVCurvePng(Path.Combine(outDir, "focus_sweep_hfr.png"), rows);
            Console.WriteLine($"Wrote focus_sweep.csv, focus_sweep_summary.txt, focus_sweep_hfr.png to {outDir}");
```

- [ ] **Step 3: Build TestApp**

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
```
Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Add focus-sweep V-curve PNG output" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task B4: `--synthesize` (self-contained smoke fixture)

**Files:**
- Modify: `Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs`

- [ ] **Step 1: Replace the placeholder `SynthesizeAfRun` with the real implementation**

Replace the placeholder method with:

```csharp
        // Writes `count` synthetic 16-bit TIFF frames named with the saved-AF convention, with a V-shaped HFR
        // (sharp in the middle, defocused at the ends). Lets the diagnostic be exercised end-to-end with no
        // real data. Self-contained (cannot reference the Tests project's generators).
        private static void SynthesizeAfRun(string dir, int count) {
            Directory.CreateDirectory(dir);
            const int frameSize = 128;
            const int basePos = 10000, stepPos = 100;
            int center = count / 2;
            for (int i = 0; i < count; ++i) {
                int pos = basePos + i * stepPos;
                double sigma = 2.0 + 1.5 * Math.Abs(i - center); // V-shaped width
                using var f = SynthFrame(frameSize, frameSize, sigma, peak: 0.6, background: 0.02);
                using var u16 = new Mat();
                f.ConvertTo(u16, MatType.CV_16U, ushort.MaxValue);
                var name = $"{i:00}_Frame00_BitDepth16_Bayered0_Focuser{pos}.tif";
                Cv2.ImWrite(Path.Combine(dir, name), u16);
            }
        }

        // A 3x3 grid of Gaussian stars on a flat background.
        private static Mat SynthFrame(int w, int h, double sigma, double peak, double background) {
            var mat = new Mat(new Size(w, h), MatType.CV_32F, new Scalar(background));
            for (int gy = 1; gy <= 3; ++gy) {
                for (int gx = 1; gx <= 3; ++gx) {
                    AddGaussian(mat, w * gx / 4.0, h * gy / 4.0, sigma, peak);
                }
            }
            return mat;
        }

        private static void AddGaussian(Mat mat, double cx, double cy, double sigma, double peak) {
            int w = mat.Width, h = mat.Height;
            double inv = 1.0 / (2.0 * sigma * sigma);
            int rad = (int)Math.Ceiling(5.0 * sigma);
            unsafe {
                var data = (float*)mat.DataPointer;
                for (int y = Math.Max(0, (int)(cy - rad)); y < Math.Min(h, (int)(cy + rad)); ++y) {
                    for (int x = Math.Max(0, (int)(cx - rad)); x < Math.Min(w, (int)(cx + rad)); ++x) {
                        double dx = x - cx, dy = y - cy;
                        data[y * w + x] += (float)(peak * Math.Exp(-(dx * dx + dy * dy) * inv));
                    }
                }
            }
        }
```

Note: `Cv2`, `Mat`, `Size`, `Scalar`, `MatType` are already imported via `using OpenCvSharp;`. `Size` may need disambiguation — if the build reports an ambiguous `Size`, add `using Size = OpenCvSharp.Size;` to the file's usings.

- [ ] **Step 2: Build TestApp**

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
```
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Add --synthesize fixture to focus-sweep diagnostic" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task B5: End-to-end smoke test + full verification

**Files:** none (verification only).

- [ ] **Step 1: Locate the built TestApp.exe**

```bash
ls Joko.NINA.Plugins/TestApp/bin/*/Debug/net8.0-windows7.0/TestApp.exe Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe 2>/dev/null
```
Use whichever path exists (depends on the platform the build produced; `bin\x64\Debug\...` for x64).

- [ ] **Step 2: Run synthesize + sweep with default params (no NINA profile needed)**

```bash
"<TESTAPP_EXE_PATH>" focus-sweep --synthesize "C:\temp\hf-sweep-synth" --synthesize-count 9 --default-params --out "C:\temp\hf-sweep-out"
```
Expected console: `Synthesized 9 frames…`, then `pos 10000: N stars` … lines, then `Wrote focus_sweep.csv, focus_sweep_summary.txt, focus_sweep_hfr.png`.

- [ ] **Step 3: Verify the outputs**

```bash
cat "/mnt/c/temp/hf-sweep-out/focus_sweep.csv"
cat "/mnt/c/temp/hf-sweep-out/focus_sweep_summary.txt"
ls -la "/mnt/c/temp/hf-sweep-out/focus_sweep_hfr.png"
```
Expected: CSV has 9 position rows with non-zero `StarCount` and a `MedianHFR` column that is **smallest near the middle position (10400)** and larger at the ends (the V). Summary's "Min median-HFR position" should be near 10400. PNG exists and is non-empty. If the PNG is missing, check the logged warning — the CSV/summary are the primary evidence and the run still succeeds.

- [ ] **Step 4: Run the full unit-test suite (Piece A + everything)**

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo
```
Expected: all tests PASS.

- [ ] **Step 5: Push the branch and open the PR**

```bash
git push -u origin ghilios/focus-sweep-evidence-base
gh pr create --base develop --head ghilios/focus-sweep-evidence-base \
  --title "Focus-sweep evidence base: synthetic HFR bias tests + TestApp diagnostic" \
  --body "$(cat <<'EOF'
Implements step 1 of the star-detection accuracy analysis (build the evidence base).

## Piece A — synthetic MeasureStar bias tests (Tests project, CI)
- `SyntheticDefocusedStarImage` (disk/annulus, supersampled, seeded noise) + `HfrGroundTruth`.
- `MeasureStarBiasTests` pins `MeasureStar` against analytic ground truth and measures the F3
  (soft-threshold) and F4 (σ-mismatch noise) HFR bias magnitudes; donut HFR ordering. All synthetic, deterministic.

## Piece B — focus-sweep diagnostic (TestApp, local only; not in CI)
- New `TestApp focus-sweep --af-run <dir>` reports star count, median HFR (+MAD), and per-reason rejection
  counts vs focuser position (CSV + summary + ScottPlot V-curve PNG). `--default-params` and `--synthesize`
  allow a data-free smoke run. Shared `LoadFloatMat`/`GetArg` extracted into `DiagnosticUtil`.

No production plugin behavior changes. CI stays synthetic; no large data added.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review (completed during planning)

- **Spec coverage:** Piece A generator (Task A1) ✓, ground truth (A1) ✓, clean pins (A2) ✓, F3 magnitude (A3) ✓, F4 magnitude (A4) ✓, donut sanity (A4) ✓. Piece B runner consuming a saved-AF folder (B2) ✓, CSV/summary (B2) ✓, V-curve PNG (B3) ✓, synthetic validation with no real data (B4/B5) ✓, dispatch (B2) ✓. Branch/PR/verification (B5) ✓. No-CI-data decision honored (TestApp not in CI; `--default-params`+`--synthesize` keep the smoke test data-free).
- **Placeholder scan:** The only `NotImplementedException` is the B2 `SynthesizeAfRun` stub, explicitly replaced in B4 (and it builds in between). No TBDs.
- **Type consistency:** `MeasureStar(Mat, Star, StarDetectorParams, double)`, `Detect(Mat, StarDetectorParams, IProgress, CancellationToken)`, `Star { Center=Point2d, StarBoundingBox=Rect, Background }`, `StarDetectorParams { AnalysisSamplingSize, StarClippingMultiplier, ModelPSF }`, `StarDetectorMetrics` field names, `HocusFocusStarDetection.BuildStarDetectorParams`, `MathUtility.MedianMAD()→(median,mad)`, `PluginOptionsAccessor.GetAssemblyGuid` — all verified against source. `DiagnosticUtil.GetArg/LoadFloatMat` names used consistently in both runners.

## Risks

- **Clean-test tolerances** (`Within(0.3)`): supersampling should keep the estimator within a few percent; if a value exceeds it, the logged measured/truth lines make re-tuning trivial (adjust to a documented tolerance — do not loosen blindly).
- **ScottPlot `SaveFig` headless:** wrapped in try/catch; CSV/summary remain the primary evidence if PNG rendering fails.
- **`--default-params` only supports `.tif`:** `LoadFloatMat` throws a clear message for XISF/FITS without a profile (real runs omit `--default-params`).
