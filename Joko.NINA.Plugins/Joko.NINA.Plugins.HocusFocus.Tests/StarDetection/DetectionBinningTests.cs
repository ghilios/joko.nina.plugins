using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// The unit contract for software detection binning: detection ANALYZES binned pixels, but everything it
    /// RETURNS is back in source pixels. These tests detect the same synthetic field at several factors and
    /// assert the outputs agree — which is the whole promise of the feature (nothing downstream can tell).
    /// </summary>
    [TestFixture]
    public class DetectionBinningTests {

        // Sigma 6 px: an oversampled star, the case binning exists for. HFR = sigma*sqrt(pi/2) ~ 7.5 px, so a 2x
        // or 3x bin lands it inside the detector's calibrated band.
        private const double StarSigmaPx = 6.0;

        private const double Peak = 0.5;
        private const double Background = 0.05;

        private static readonly (double x, double y)[] Placements = {
            (120.0, 130.0), (360.0, 140.0), (130.0, 370.0), (370.0, 380.0), (250.0, 250.0)
        };

        private static Mat BuildField(int size = 480) {
            var mat = SyntheticGaussianStarImage.CreateFlat(size, size, (float)Background);
            foreach (var (x, y) in Placements) {
                SyntheticStarField.AddStar(mat, x, y, StarSigmaPx, Peak);
            }
            return mat;
        }

        /// <summary>Detector params tuned for this synthetic field. Deliberately NOT the production defaults:
        /// the field is noiseless and the stars are large, so the knobs below just keep every placement
        /// detectable at every binning factor rather than exercising the gates.</summary>
        private static StarDetectorParams Params(int binning) => new StarDetectorParams {
            DetectionBinning = binning,
            HotpixelFiltering = false,
            ModelPSF = false,
            // The star spans ~36 px unbinned; the structure layers have to be able to hold it at 1x too.
            StructureLayers = 6,
            MinimumStarBoundingBoxSize = 3,
            MinHFR = 0.0,
            PixelScale = 1.0 * binning
        };

        private static async Task<HocusFocusStarDetectorResult> DetectAsync(Mat image, StarDetectorParams p) {
            var detector = new StarDetector(new AlglibAPI());
            using var clone = image.Clone();
            return await detector.Detect(clone, p, null, CancellationToken.None);
        }

        private static Star Nearest(IEnumerable<Star> stars, double x, double y) =>
            stars.OrderBy(s => (s.Center.X - x) * (s.Center.X - x) + (s.Center.Y - y) * (s.Center.Y - y)).FirstOrDefault();

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task Detect_Binned_RecoversTheSameStarCentersInSourcePixels(int binning) {
            using var image = BuildField();
            var unbinned = await DetectAsync(image, Params(1));
            var binned = await DetectAsync(image, Params(binning));

            TestContext.WriteLine($"1x: {unbinned.DetectedStars.Count} stars; {binning}x: {binned.DetectedStars.Count} stars");
            Assert.That(binned.DetectedStars, Has.Count.EqualTo(Placements.Length), "every injected star must survive binning");
            Assert.That(binned.DetectionBinning, Is.EqualTo(binning), "the result reports the factor it ran at");

            Assert.Multiple(() => {
                foreach (var (x, y) in Placements) {
                    var star = Nearest(binned.DetectedStars, x, y);
                    // Centroid precision in SOURCE pixels degrades roughly with the factor — that is the documented
                    // trade for the SNR gain — but the position must still be the injected one, not a scaled copy
                    // of a binned coordinate.
                    Assert.That(star.Center.X, Is.EqualTo(x).Within(binning), $"star at ({x},{y}) X");
                    Assert.That(star.Center.Y, Is.EqualTo(y).Within(binning), $"star at ({x},{y}) Y");
                }
            });
        }

        [TestCase(2)]
        [TestCase(3)]
        public async Task Detect_Binned_ReportsHfrInSourcePixels(int binning) {
            using var image = BuildField();
            var truth = HfrGroundTruth.Gaussian(StarSigmaPx);
            var unbinned = await DetectAsync(image, Params(1));
            var binned = await DetectAsync(image, Params(binning));

            var unbinnedHfr = unbinned.DetectedStars.Select(s => s.HFR).Average();
            var binnedHfr = binned.DetectedStars.Select(s => s.HFR).Average();
            TestContext.WriteLine($"truth={truth:F3} 1x={unbinnedHfr:F3} {binning}x={binnedHfr:F3}");

            Assert.Multiple(() => {
                Assert.That(binnedHfr, Is.EqualTo(truth).Within(0.15 * truth),
                    "the reported HFR must be the star's real size in source pixels, not its binned size");
                Assert.That(binnedHfr, Is.EqualTo(unbinnedHfr).Within(0.15 * unbinnedHfr),
                    "binning must not shift the reported HFR — this is what keeps auto-focus curves comparable");
            });
        }

        [TestCase(2)]
        [TestCase(3)]
        public async Task Detect_Binned_LeavesTheHfrDispersionInSourcePixels(int binning) {
            // The auto-focus curve's ERROR BARS are HFRStdDev, and the hyperbolic fit weights each point by
            // 1/ErrorY^2 - so a dispersion left in binned pixels would draw error bars a factor of `binning` too
            // small AND mis-weight the fit against any frame detected at a different factor.
            //
            // Nothing scales the dispersion explicitly, and nothing needs to: HocusFocusStarDetection computes it
            // from Star.HFR, which StarDetector has already scaled, and both sd and MAD are scale-equivariant
            // (sd(b*x) = b*sd(x)). This test exists because that correctness is INDIRECT - it would be quietly lost
            // by moving the aggregation into the detector, or by "fixing" it with a scale-back of its own, which
            // would double-count. Computed here exactly the two ways production does (MeanOutliers vs the
            // MedianMAD default).
            // BuildField's stars are all one size, so its HFR dispersion is ~0 at every factor and would make this
            // vacuously green. Spread the sigmas to give the statistic something real to measure.
            using var image = SyntheticGaussianStarImage.CreateFlat(480, 480, (float)Background);
            var sigmas = new[] { 4.5, 5.25, 6.0, 6.75, 7.5 };
            for (var i = 0; i < Placements.Length; i++) {
                SyntheticStarField.AddStar(image, Placements[i].x, Placements[i].y, sigmas[i], Peak);
            }

            var unbinnedHfrs = (await DetectAsync(image, Params(1))).DetectedStars.Select(s => s.HFR).ToList();
            var binnedHfrs = (await DetectAsync(image, Params(binning))).DetectedStars.Select(s => s.HFR).ToList();
            Assume.That(unbinnedHfrs.Count, Is.GreaterThan(1).And.EqualTo(binnedHfrs.Count),
                "both runs must find the same stars, or the dispersions are not comparable");

            static double Sd(IReadOnlyCollection<double> hfrs) {
                var mean = hfrs.Average();
                return Math.Sqrt(hfrs.Sum(h => (h - mean) * (h - mean)) / (hfrs.Count - 1));
            }

            var (_, unbinnedMad) = unbinnedHfrs.MedianMAD();
            var (_, binnedMad) = binnedHfrs.MedianMAD();
            TestContext.WriteLine($"1x: sd={Sd(unbinnedHfrs):F3} mad={unbinnedMad:F3}; {binning}x: sd={Sd(binnedHfrs):F3} mad={binnedMad:F3}");

            // The discriminating check: a dispersion still in binned pixels would be ~1/binning of the 1x value,
            // which these tolerances (well under a factor of 2) exclude.
            Assert.Multiple(() => {
                Assert.That(Sd(binnedHfrs), Is.EqualTo(Sd(unbinnedHfrs)).Within(0.35 * Sd(unbinnedHfrs)),
                    "HFR sigma must be in source pixels - it is drawn as the curve's error bars and weights the fit");
                Assert.That(binnedMad, Is.EqualTo(unbinnedMad).Within(0.35 * Math.Max(unbinnedMad, 1e-6)),
                    "HFR MAD (the default dispersion) must be in source pixels for the same reason");
            });
        }

        [Test]
        public async Task Detect_Binned_ScalesBoundingBoxesAndTheDebugRoiToSourcePixels() {
            using var image = BuildField();
            var unbinned = await DetectAsync(image, Params(1));

            var p = Params(3);
            p.StoreStructureMap = true;
            var binned = await DetectAsync(image, p);

            var unbinnedBox = Nearest(unbinned.DetectedStars, 250, 250).StarBoundingBox;
            var binnedBox = Nearest(binned.DetectedStars, 250, 250).StarBoundingBox;
            TestContext.WriteLine($"1x box={unbinnedBox}, 3x box={binnedBox}");

            Assert.Multiple(() => {
                // Scaled up from a binned raster, so every edge lands on a block boundary. (The binned box covers
                // MORE of the star than the 1x box: mean binning lifts the faint wings above the structure-map
                // threshold. That is the recall gain the feature exists for, not a scaling error — so this asserts
                // the box is a correctly-scaled, star-bracketing box rather than pinning it to the 1x extent.)
                Assert.That(binnedBox.Width % 3, Is.Zero, "a scaled binned box is a whole number of blocks wide");
                Assert.That(binnedBox.Height % 3, Is.Zero);
                Assert.That(binnedBox.X % 3, Is.Zero);
                Assert.That(binnedBox.Width, Is.GreaterThanOrEqualTo(unbinnedBox.Width).And.LessThan(2.5 * unbinnedBox.Width),
                    "the box must describe the star's extent in source pixels, at the same order of magnitude as 1x");
                Assert.That(binnedBox.X + binnedBox.Width / 2.0, Is.EqualTo(250.0).Within(4.0), "and stay centered on the star");
                // The debug ROI stays in source coordinates so the annotator can draw it over the full-size frame;
                // the structure map itself is a binned raster, which DebugData.Binning declares.
                Assert.That(binned.DebugData.DetectionROI.Width, Is.EqualTo(image.Width));
                Assert.That(binned.DebugData.Binning, Is.EqualTo(3));
                Assert.That(binned.DebugData.StructureMap, Has.Length.EqualTo((image.Width / 3) * (image.Height / 3)));
            });
        }

        [Test]
        public async Task Detect_BinnedWithRoi_AppliesTheRoiOffsetInSourcePixels() {
            using var image = BuildField();
            // A centered half-frame ROI: only the (250, 250) star is inside it.
            var region = new StarDetectionRegion(new RatioRect(0.25, 0.25, 0.5, 0.5));
            var p = Params(2);
            p.Region = region;

            var binned = await DetectAsync(image, p);
            TestContext.WriteLine($"ROI detections: {string.Join(", ", binned.DetectedStars.Select(s => s.Center))}");

            Assert.That(binned.DetectedStars, Is.Not.Empty);
            var star = Nearest(binned.DetectedStars, 250, 250);
            Assert.Multiple(() => {
                // Scale-then-translate: a bug that scaled AFTER adding the ROI offset would put this near 370.
                Assert.That(star.Center.X, Is.EqualTo(250.0).Within(2.0));
                Assert.That(star.Center.Y, Is.EqualTo(250.0).Within(2.0));
            });
        }

        [Test]
        public async Task Detect_BinningOfOne_IsBitIdenticalToNoBinning() {
            using var image = BuildField();
            var a = await DetectAsync(image, Params(1));

            var p = Params(1);
            p.DetectionBinning = 1;
            var b = await DetectAsync(image, p);

            Assert.That(b.DetectedStars, Has.Count.EqualTo(a.DetectedStars.Count));
            for (var i = 0; i < a.DetectedStars.Count; i++) {
                Assert.That(b.DetectedStars[i].Center.X, Is.EqualTo(a.DetectedStars[i].Center.X).Within(1e-12));
                Assert.That(b.DetectedStars[i].HFR, Is.EqualTo(a.DetectedStars[i].HFR).Within(1e-12));
            }
        }

        [Test]
        public void EarlyCacheKey_ChangesWithTheBinningFactor() {
            // Binning resamples the frame before candidate formation, so an early context built at one factor must
            // never be reused at another.
            Assert.That(StarDetector.IsEarlyCacheKeyParameter(nameof(StarDetectorParams.DetectionBinning)), Is.True);
            var one = StarDetector.ComputeEarlyCacheKey(Params(1));
            var two = StarDetector.ComputeEarlyCacheKey(Params(2));
            Assert.That(two, Is.Not.EqualTo(one));
        }

        [Test]
        public async Task Detect_Binned_PreservesIntensityLevelsAndPsfArcsecScale() {
            using var image = BuildField();
            var pOne = Params(1);
            pOne.ModelPSF = true;
            var pTwo = Params(2);
            pTwo.ModelPSF = true;

            var unbinned = await DetectAsync(image, pOne);
            var binned = await DetectAsync(image, pTwo);

            var a = Nearest(unbinned.DetectedStars, 250, 250);
            var b = Nearest(binned.DetectedStars, 250, 250);
            Assert.That(a.PSF, Is.Not.Null);
            Assert.That(b.PSF, Is.Not.Null);
            TestContext.WriteLine($"1x: bg={a.Background:F4} peak={a.PeakBrightness:F4} sigma={a.PSF.Sigma:F3} fwhm\"={a.PSF.FWHMArcsecs:F3}");
            TestContext.WriteLine($"2x: bg={b.Background:F4} peak={b.PeakBrightness:F4} sigma={b.PSF.Sigma:F3} fwhm\"={b.PSF.FWHMArcsecs:F3}");

            Assert.Multiple(() => {
                // Mean binning preserves the level, so brightness-valued fields carry over unscaled.
                Assert.That(b.Background, Is.EqualTo(a.Background).Within(0.02));
                Assert.That(b.PeakBrightness, Is.EqualTo(a.PeakBrightness).Within(0.1 * a.PeakBrightness));
                // Sigma is a length: scaled back to source pixels.
                Assert.That(b.PSF.Sigma, Is.EqualTo(a.PSF.Sigma).Within(0.2 * a.PSF.Sigma));
                // FWHM in arcsec was already physical (PixelScale carried the factor), so it must NOT be scaled twice.
                Assert.That(b.PSF.FWHMArcsecs, Is.EqualTo(a.PSF.FWHMArcsecs).Within(0.2 * a.PSF.FWHMArcsecs));
            });
        }

        [Test]
        public void PsfModel_ScaledToSourcePixels_GrowsLengthsAndPreservesArcsec() {
            var model = new PSFModel(
                StarDetectorPSFFitType.Gaussian,
                offsetX: 0.5, offsetY: -0.25, peak: 0.8, background: 0.1,
                sigmaX: 2.0, sigmaY: 3.0, fwhmX: 4.0, fwhmY: 6.0,
                thetaRadians: 0.3, rSquared: 0.99, pixelScale: 1.5);

            var scaled = model.ScaledToSourcePixels(2);

            Assert.Multiple(() => {
                Assert.That(scaled.SigmaX, Is.EqualTo(4.0).Within(1e-9));
                Assert.That(scaled.FWHMy, Is.EqualTo(12.0).Within(1e-9));
                Assert.That(scaled.OffsetX, Is.EqualTo(1.0).Within(1e-9));
                Assert.That(scaled.FWHMPixels, Is.EqualTo(2.0 * model.FWHMPixels).Within(1e-9));
                Assert.That(scaled.FWHMArcsecs, Is.EqualTo(model.FWHMArcsecs).Within(1e-9), "arcsec was already physical");
                Assert.That(scaled.Eccentricity, Is.EqualTo(model.Eccentricity).Within(1e-9), "shape is scale-invariant");
                Assert.That(scaled.Peak, Is.EqualTo(model.Peak).Within(1e-12));
                Assert.That(model.ScaledToSourcePixels(1), Is.SameAs(model));
            });
        }

        [Test]
        public void Star_ScaleToSourcePixels_MapsGeometryAndLeavesIntensitiesAlone() {
            var star = new Star {
                Center = new Point2d(10.0, 20.0),
                StarBoundingBox = new Rect(8, 18, 5, 7),
                Background = 0.03,
                BackgroundPlane = new LocalBackgroundPlane(10.0, 20.0, 0.03, 0.001, -0.002, isFlat: false),
                MeanBrightness = 0.4,
                PeakBrightness = 0.7,
                HFR = 3.0,
                StarContaminationSuspected = true,
                RelaxationAdmitted = true,
                MeasuredSensitivity = 5.5
            };

            var scaled = star.ScaleToSourcePixels(3);

            Assert.Multiple(() => {
                // A block mean is sampled at the block center, hence the (factor-1)/2 shift.
                Assert.That(scaled.Center.X, Is.EqualTo(31.0).Within(1e-9));
                Assert.That(scaled.Center.Y, Is.EqualTo(61.0).Within(1e-9));
                Assert.That(scaled.StarBoundingBox, Is.EqualTo(new Rect(24, 54, 15, 21)));
                Assert.That(scaled.HFR, Is.EqualTo(9.0).Within(1e-9));
                Assert.That(scaled.Background, Is.EqualTo(0.03).Within(1e-12));
                Assert.That(scaled.MeanBrightness, Is.EqualTo(0.4).Within(1e-12));
                Assert.That(scaled.PeakBrightness, Is.EqualTo(0.7).Within(1e-12));
                // The plane must evaluate the same at corresponding points: slope is per source pixel now.
                Assert.That(scaled.BackgroundPlane.B1, Is.EqualTo(0.001 / 3.0).Within(1e-12));
                Assert.That(scaled.BackgroundPlane.ValueAt(scaled.Center.X + 3.0, scaled.Center.Y),
                    Is.EqualTo(star.BackgroundPlane.ValueAt(star.Center.X + 1.0, star.Center.Y)).Within(1e-12));
                Assert.That(scaled.StarContaminationSuspected, Is.True);
                Assert.That(scaled.RelaxationAdmitted, Is.True);
                // A ratio of two intensities — mean binning preserves level for both, so it is carried UNSCALED.
                Assert.That(scaled.MeasuredSensitivity, Is.EqualTo(5.5).Within(1e-12));
                Assert.That(star.ScaleToSourcePixels(1), Is.SameAs(star));
            });
        }

        [Test]
        public void Metrics_ScaleBounds_MovesEveryRejectionBoxToSourcePixels() {
            var metrics = new StarDetectorMetrics();
            metrics.TooDistortedBounds.Add(new Rect(2, 3, 4, 5));
            metrics.ContaminatedBounds.Add(new Rect(10, 20, 6, 8));

            metrics.ScaleBounds(2);

            Assert.Multiple(() => {
                Assert.That(metrics.TooDistortedBounds[0], Is.EqualTo(new Rect(4, 6, 8, 10)));
                Assert.That(metrics.ContaminatedBounds[0], Is.EqualTo(new Rect(20, 40, 12, 16)));
            });

            metrics.ScaleBounds(1);
            Assert.That(metrics.TooDistortedBounds[0], Is.EqualTo(new Rect(4, 6, 8, 10)), "factor 1 is a no-op");
        }
    }
}
