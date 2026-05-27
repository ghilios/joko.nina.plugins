using MathNet.Numerics;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class PSFModelerTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        // Sample a synthetic Gaussian on a regular grid centered on (0,0). The PSF model takes
        // inputs as offsets from the centroid (dx, dy) and the corresponding pixel value.
        private static (double[][] inputs, double[] outputs) SampleGaussian(
            double sigmaX, double sigmaY, double peak, double background,
            int radius = 5) {
            int side = 2 * radius + 1;
            var n = side * side;
            var inputs = new double[n][];
            var outputs = new double[n];
            int idx = 0;
            for (var y = -radius; y <= radius; ++y) {
                for (var x = -radius; x <= radius; ++x) {
                    var e = (x * x) / (2.0 * sigmaX * sigmaX) + (y * y) / (2.0 * sigmaY * sigmaY);
                    inputs[idx] = new double[] { x, y };
                    outputs[idx] = background + peak * Math.Exp(-e);
                    idx++;
                }
            }
            return (inputs, outputs);
        }

        [Test]
        public void GaussianPSF_Solve_RecoversSigmasOnNoiselessSymmetricStar() {
            const double trueSigma = 1.5;
            const double peak = 0.8;
            const double background = 0.05;
            var (inputs, outputs) = SampleGaussian(trueSigma, trueSigma, peak, background);

            var model = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: new Rect(0, 0, 11, 11),
                pixelScale: 1.0);

            var sol = model.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);

            Assert.Multiple(() => {
                Assert.That(sol.SigmaX, Is.EqualTo(trueSigma).Within(0.1));
                Assert.That(sol.SigmaY, Is.EqualTo(trueSigma).Within(0.1));
                Assert.That(sol.X0, Is.EqualTo(0.0).Within(0.05));
                Assert.That(sol.Y0, Is.EqualTo(0.0).Within(0.05));
                Assert.That(sol.A, Is.EqualTo(peak).Within(0.05));
            });
        }

        [Test]
        public void GaussianPSF_Solve_GoodnessOfFit_NearOneOnNoiselessData() {
            const double trueSigma = 2.0;
            const double peak = 1.0;
            const double background = 0.0;
            var (inputs, outputs) = SampleGaussian(trueSigma, trueSigma, peak, background);

            var model = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak,
                starDetectionBackground: background,
                starBoundingBox: new Rect(0, 0, 11, 11),
                pixelScale: 1.0);

            var sol = model.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);
            var rSq = model.GoodnessOfFit(sol.A, sol.B, sol.X0, sol.Y0, sol.SigmaX, sol.SigmaY, sol.Theta);

            Assert.That(rSq, Is.GreaterThan(0.99));
        }

        [Test]
        public void GaussianPSF_Solve_CancellationToken_PropagatesCancellation() {
            const double trueSigma = 1.5;
            var (inputs, outputs) = SampleGaussian(trueSigma, trueSigma, 1.0, 0.0);

            var model = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: 1.0,
                starDetectionBackground: 0.0,
                starBoundingBox: new Rect(0, 0, 11, 11),
                pixelScale: 1.0);

            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                model.Solve(maxIterations: 200, tolerance: 1e-10, ct: cts.Token));
        }

        // Elongated Gaussian: σx=3, σy=6.  The star extends ~±12 pixels in the short axis
        // and ~±18 in the long axis, so use radius=18 and a bounding box of 37×37.
        [Test]
        public void GaussianPSF_Solve_MomentSeed_ConvergesOnElongatedStar_SigmaX3_SigmaY6() {
            const double trueSigmaX = 3.0;
            const double trueSigmaY = 6.0;
            const double peak = 0.9;
            const double background = 0.02;
            const int radius = 18;
            var (inputs, outputs) = SampleGaussian(trueSigmaX, trueSigmaY, peak, background, radius);

            int side = 2 * radius + 1; // 37
            var model = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: new Rect(0, 0, side, side),
                pixelScale: 1.0);

            var sol = model.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);

            // The solver may swap sigmaX/sigmaY so that sigmaX >= sigmaY.
            var recoveredSigmaMin = Math.Min(sol.SigmaX, sol.SigmaY);
            var recoveredSigmaMax = Math.Max(sol.SigmaX, sol.SigmaY);

            Assert.Multiple(() => {
                // Verify both axes are recovered within 5%
                Assert.That(recoveredSigmaMin, Is.EqualTo(trueSigmaX).Within(0.05 * trueSigmaX),
                    "Short-axis sigma (σ=3) not recovered within 5%");
                Assert.That(recoveredSigmaMax, Is.EqualTo(trueSigmaY).Within(0.05 * trueSigmaY),
                    "Long-axis sigma (σ=6) not recovered within 5%");
                Assert.That(sol.X0, Is.EqualTo(0.0).Within(0.1), "Centroid x offset should be near zero");
                Assert.That(sol.Y0, Is.EqualTo(0.0).Within(0.1), "Centroid y offset should be near zero");
                Assert.That(sol.A, Is.EqualTo(peak).Within(0.05 * peak), "Peak amplitude not recovered within 5%");
            });
        }

        [Test]
        public void GaussianPSF_Solve_MomentSeed_GoodnessOfFitNearOneOnElongatedStar() {
            const double trueSigmaX = 3.0;
            const double trueSigmaY = 6.0;
            const double peak = 0.9;
            const double background = 0.02;
            const int radius = 18;
            var (inputs, outputs) = SampleGaussian(trueSigmaX, trueSigmaY, peak, background, radius);

            int side = 2 * radius + 1;
            var model = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: new Rect(0, 0, side, side),
                pixelScale: 1.0);

            var sol = model.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);
            var rSq = model.GoodnessOfFit(sol.A, sol.B, sol.X0, sol.Y0, sol.SigmaX, sol.SigmaY, sol.Theta);

            Assert.That(rSq, Is.GreaterThan(0.999),
                "Goodness-of-fit R² should be > 0.999 on noiseless elongated Gaussian data");
        }

        // Returns inputs shifted so that the detected centroid is offset by (seedOffsetX, seedOffsetY)
        // from the true Gaussian center. The solver receives inputs where the true peak is at
        // (-seedOffsetX, -seedOffsetY) in input-space, and must recover x0 ≈ -seedOffsetX.
        private static (double[][] inputs, double[] outputs) SampleGaussianWithCentroidOffset(
            double sigmaX, double sigmaY, double peak, double background,
            double seedOffsetX, double seedOffsetY,
            int radius = 5) {
            int side = 2 * radius + 1;
            var n = side * side;
            var inputs = new double[n][];
            var outputs = new double[n];
            int idx = 0;
            for (var y = -radius; y <= radius; ++y) {
                for (var x = -radius; x <= radius; ++x) {
                    // Pixel at true offset (x, y) from true center.
                    // Detected centroid is shifted by (seedOffsetX, seedOffsetY) from true center,
                    // so the input seen by the solver is (x - seedOffsetX, y - seedOffsetY).
                    var inputX = x - seedOffsetX;
                    var inputY = y - seedOffsetY;
                    var e = (x * x) / (2.0 * sigmaX * sigmaX) + (y * y) / (2.0 * sigmaY * sigmaY);
                    inputs[idx] = new double[] { inputX, inputY };
                    outputs[idx] = background + peak * Math.Exp(-e);
                    idx++;
                }
            }
            return (inputs, outputs);
        }

        /// <summary>
        /// Creates a Gaussian sample grid identical to <see cref="SampleGaussian"/> except that
        /// one pixel (at the given <paramref name="hotPixelX"/>, <paramref name="hotPixelY"/>
        /// grid position, measured from the centre) is replaced with <paramref name="hotValue"/>.
        /// </summary>
        private static (double[][] inputs, double[] outputs) SampleGaussianWithHotPixel(
            double sigmaX, double sigmaY, double peak, double background,
            int hotPixelX, int hotPixelY, double hotValue,
            int radius = 5) {
            var (inputs, outputs) = SampleGaussian(sigmaX, sigmaY, peak, background, radius);
            // Find and replace the hot pixel
            for (int i = 0; i < inputs.Length; ++i) {
                if ((int)inputs[i][0] == hotPixelX && (int)inputs[i][1] == hotPixelY) {
                    outputs[i] = hotValue;
                    break;
                }
            }
            return (inputs, outputs);
        }

        /// <summary>
        /// Verifies that Huber IRLS recovers the true sigma within 5% when a single hot pixel
        /// is present at 3× the peak value, while an unweighted (plain Levenberg-Marquardt)
        /// solve is pulled off by the outlier.
        /// </summary>
        [Test]
        public void GaussianPSF_SolveIRLS_HuberWeights_RobustToHotPixel() {
            const double trueSigma = 2.0;
            const double peak = 0.8;
            const double background = 0.02;
            // Hot pixel is placed away from the core (at offset (4,0)) to maximise the disruption
            // on the unweighted fit while still being inside the bounding box.
            const int hotX = 4;
            const int hotY = 0;
            const double hotValue = 3.0 * peak; // 3× peak = strong outlier

            var (inputs, outputs) = SampleGaussianWithHotPixel(trueSigma, trueSigma, peak, background, hotX, hotY, hotValue);

            int side = 2 * 5 + 1; // radius = 5 → 11×11

            // --- IRLS with Huber weights ---
            var irlsModel = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: new Rect(0, 0, side, side),
                pixelScale: 1.0);

            // noiseSigma chosen as a small fraction of the clean peak so the hot pixel residual
            // (≈ 0.8 * peak) is well above δ = 1.5 * noiseSigma and gets down-weighted.
            const double noiseSigma = 0.005;
            var irlsSol = irlsModel.SolveIRLS(
                maxIterationsIRLS: 10, toleranceIRLS: 1e-6,
                maxIterationsLM: 100, toleranceLM: 1e-10,
                noiseSigma: noiseSigma,
                ct: CancellationToken.None);

            // --- Plain LM (no robust weighting) with hot pixel to demonstrate sensitivity ---
            var lmModel = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: new Rect(0, 0, side, side),
                pixelScale: 1.0);

            var lmSol = lmModel.Solve(maxIterations: 100, tolerance: 1e-10, ct: CancellationToken.None);

            // Huber IRLS must recover sigma within 5%
            Assert.Multiple(() => {
                Assert.That(irlsSol.SigmaX, Is.EqualTo(trueSigma).Within(0.05 * trueSigma),
                    "Huber IRLS SigmaX should be within 5% of the true sigma despite the hot pixel");
                Assert.That(irlsSol.SigmaY, Is.EqualTo(trueSigma).Within(0.05 * trueSigma),
                    "Huber IRLS SigmaY should be within 5% of the true sigma despite the hot pixel");

                // Plain LM should be noticeably pulled off (error > 5% on at least one sigma)
                var lmSigmaXErr = Math.Abs(lmSol.SigmaX - trueSigma) / trueSigma;
                var lmSigmaYErr = Math.Abs(lmSol.SigmaY - trueSigma) / trueSigma;
                Assert.That(Math.Max(lmSigmaXErr, lmSigmaYErr), Is.GreaterThan(0.05),
                    "Unweighted LM should be pulled off by the hot pixel (error > 5%)");
            });
        }

        // Creates a Gaussian sample where all pixels whose true Gaussian value >= saturationThreshold
        // are clipped to exactly saturationThreshold, simulating partial saturation at the star core.
        private static (double[][] inputs, double[] outputs) SampleGaussianWithSaturation(
            double sigmaX, double sigmaY, double peak, double background,
            double saturationThreshold,
            int radius = 5) {
            int side = 2 * radius + 1;
            var n = side * side;
            var inputs = new double[n][];
            var outputs = new double[n];
            int idx = 0;
            for (var y = -radius; y <= radius; ++y) {
                for (var x = -radius; x <= radius; ++x) {
                    var e = (x * x) / (2.0 * sigmaX * sigmaX) + (y * y) / (2.0 * sigmaY * sigmaY);
                    var trueValue = background + peak * Math.Exp(-e);
                    inputs[idx] = new double[] { x, y };
                    // Clip to saturation threshold
                    outputs[idx] = Math.Min(trueValue, saturationThreshold);
                    idx++;
                }
            }
            return (inputs, outputs);
        }

        /// <summary>
        /// Verifies that PSF fitting with saturation masking recovers sigma within 10% for a
        /// partially-saturated star where the peak few pixels are clipped to 0.99.
        /// Unsaturated wing pixels carry sufficient profile information to constrain the fit.
        /// </summary>
        [Test]
        public void PSFModeler_Create_PartiallySaturatedStar_MasksSaturatedPixels_RecoversSigmaWithin10Percent() {
            const double trueSigma = 2.0;
            const double peak = 1.2;        // peak > saturationThreshold so core pixels get clipped
            const double background = 0.05;
            const double saturationThreshold = 0.99;
            const int radius = 5;

            var (inputs, outputs) = SampleGaussianWithSaturation(trueSigma, trueSigma, peak, background, saturationThreshold, radius);

            // Count how many pixels are clipped (raw value >= saturationThreshold) — these will be masked by Create
            int clippedCount = 0;
            for (int i = 0; i < outputs.Length; ++i) {
                if (outputs[i] >= saturationThreshold) ++clippedCount;
            }
            // Sanity: at least one pixel should be saturated for this to be a meaningful test
            Assert.That(clippedCount, Is.GreaterThan(0), "Expected at least one saturated pixel");

            // Build a synthetic Star and Mat to use with PSFModeler.Create
            int side = 2 * radius + 1;
            var starBoundingBox = new Rect(0, 0, side, side);
            // Center is at (radius, radius) in image coords
            var detectedStar = new Star {
                Center = new Point2d(radius, radius),
                Background = background,
                StarBoundingBox = starBoundingBox
            };

            // Build a float32 Mat from the outputs array (pre-saturation-clip values from SampleGaussianWithSaturation)
            using (var srcImage = new Mat(side, side, MatType.CV_32F)) {
                unsafe {
                    var data = (float*)srcImage.DataPointer;
                    for (int i = 0; i < outputs.Length; ++i) {
                        data[i] = (float)outputs[i];
                    }
                }

                // Create modeler WITH saturation masking — saturated pixels should be excluded
                var modelerMasked = PSFModeler.Create(
                    alglibAPI: alglibAPI,
                    fitType: StarDetectorPSFFitType.Gaussian,
                    psfResolution: side,   // 1 sample per pixel (samplingSize = sqrt(side*side)/side = 1)
                    detectedStar: detectedStar,
                    srcImage: srcImage,
                    pixelScale: 1.0,
                    saturationThreshold: saturationThreshold);

                Assert.That(modelerMasked, Is.Not.Null, "Modeler should not be null — enough unsaturated pixels remain");

                // Verify that saturated pixels were excluded from the fit inputs
                var maskedAlglib = (PSFModelTypeAlglibBase)modelerMasked;
                var maskedInputCount = maskedAlglib.Inputs.Length;
                Assert.That(maskedInputCount, Is.EqualTo(outputs.Length - clippedCount),
                    "Masked modeler should have exactly (total - clipped) inputs");

                var solMasked = modelerMasked.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);

                // Create modeler WITHOUT saturation masking for comparison (threshold = MaxValue → no masking)
                var modelerUnmasked = PSFModeler.Create(
                    alglibAPI: alglibAPI,
                    fitType: StarDetectorPSFFitType.Gaussian,
                    psfResolution: side,
                    detectedStar: detectedStar,
                    srcImage: srcImage,
                    pixelScale: 1.0,
                    saturationThreshold: double.MaxValue);

                var solUnmasked = modelerUnmasked.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);

                Assert.Multiple(() => {
                    // Masked fit should recover sigma within 10% of the true sigma
                    Assert.That(solMasked.SigmaX, Is.EqualTo(trueSigma).Within(0.10 * trueSigma),
                        "Masked PSF fit: SigmaX should be recovered within 10% despite partial saturation");
                    Assert.That(solMasked.SigmaY, Is.EqualTo(trueSigma).Within(0.10 * trueSigma),
                        "Masked PSF fit: SigmaY should be recovered within 10% despite partial saturation");

                    // Unmasked fit (using clipped values as real observations) should be noticeably worse
                    var maskedSigmaErr = Math.Abs(solMasked.SigmaX - trueSigma) / trueSigma;
                    var unmaskedSigmaErr = Math.Abs(solUnmasked.SigmaX - trueSigma) / trueSigma;
                    Assert.That(maskedSigmaErr, Is.LessThan(unmaskedSigmaErr),
                        "Masked fit should recover sigma more accurately than unmasked fit on clipped data");
                });
            }
        }

        /// <summary>
        /// Verifies that PSFModeler.Create returns null when the saturation threshold is so low
        /// that fewer than MinUnsaturatedPixels unsaturated pixels remain.
        /// </summary>
        [Test]
        public void PSFModeler_Create_TooFewUnsaturatedPixels_ReturnsNull() {
            const double trueSigma = 2.0;
            const double peak = 1.2;
            const double background = 0.05;
            // Set threshold low enough that only the 4 extreme corner pixels (r ≈ 7.07) survive
            // (r > 6.41 → only 4 corners survive out of 121, which is < MinUnsaturatedPixels=10).
            // With sigma=2: value at (5,4) ≈ 0.0571 > threshold, so it gets clipped.
            // value at (5,5) ≈ 0.0523 < threshold, so it is kept (but only 4 such corners exist).
            const double veryLowThreshold = 0.057;
            const int radius = 5;

            var (inputs, outputs) = SampleGaussianWithSaturation(trueSigma, trueSigma, peak, background, veryLowThreshold, radius);

            int side = 2 * radius + 1;
            var detectedStar = new Star {
                Center = new Point2d(radius, radius),
                Background = background,
                StarBoundingBox = new Rect(0, 0, side, side)
            };

            using (var srcImage = new Mat(side, side, MatType.CV_32F)) {
                unsafe {
                    var data = (float*)srcImage.DataPointer;
                    for (int i = 0; i < outputs.Length; ++i) {
                        data[i] = (float)outputs[i];
                    }
                }

                var modeler = PSFModeler.Create(
                    alglibAPI: alglibAPI,
                    fitType: StarDetectorPSFFitType.Gaussian,
                    psfResolution: side,
                    detectedStar: detectedStar,
                    srcImage: srcImage,
                    pixelScale: 1.0,
                    saturationThreshold: veryLowThreshold);

                Assert.That(modeler, Is.Null,
                    "Create should return null when fewer than MinUnsaturatedPixels pixels survive the saturation mask");
            }
        }

        /// <summary>
        /// Verifies that ReducedChiSquared is computed and stored on the PSFModel returned by
        /// PSFModeler.Solve when a non-zero noiseSigma is supplied.
        /// </summary>
        [Test]
        public void PSFModeler_Solve_ComputesReducedChiSquared_WhenNoiseSigmaProvided() {
            const double trueSigma = 2.0;
            const double peak = 0.8;
            const double background = 0.02;
            const double noiseSigma = 0.01;

            var (inputs, outputs) = SampleGaussian(trueSigma, trueSigma, peak, background);
            var model = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: new Rect(0, 0, 11, 11),
                pixelScale: 1.0);

            var psf = PSFModeler.Solve(model, noiseSigma: noiseSigma, useAbsoluteResiduals: false, ct: CancellationToken.None);

            Assert.That(psf, Is.Not.Null);
            Assert.That(double.IsNaN(psf.ReducedChiSquared), Is.False, "ReducedChiSquared should not be NaN when noiseSigma > 0");
            Assert.That(psf.ReducedChiSquared, Is.GreaterThan(0.0), "ReducedChiSquared should be positive");
        }

        /// <summary>
        /// Generates a Gaussian PSF sample with additive Gaussian noise.
        /// </summary>
        private static (double[][] inputs, double[] outputs) SampleGaussianWithNoise(
            double sigmaX, double sigmaY, double peak, double background,
            double noiseSigma, int seed = 42,
            int radius = 5) {
            var rng = new Random(seed);
            int side = 2 * radius + 1;
            var n = side * side;
            var inputs = new double[n][];
            var outputs = new double[n];
            int idx = 0;
            for (var y = -radius; y <= radius; ++y) {
                for (var x = -radius; x <= radius; ++x) {
                    var e = (x * x) / (2.0 * sigmaX * sigmaX) + (y * y) / (2.0 * sigmaY * sigmaY);
                    // Box-Muller for Gaussian noise
                    var u1 = 1.0 - rng.NextDouble();
                    var u2 = 1.0 - rng.NextDouble();
                    var noise = noiseSigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                    inputs[idx] = new double[] { x, y };
                    outputs[idx] = background + peak * Math.Exp(-e) + noise;
                    idx++;
                }
            }
            return (inputs, outputs);
        }

        /// <summary>
        /// Verifies the brightness-bias scenario: for a faint star (tiny signal above a large
        /// background), noise inflates rss/tss so R² drops below 0.9 even though the fit is
        /// structurally correct and reduced chi² remains below 2.0.
        ///
        /// The old R² gate would reject this fit; the new reduced-chi² gate should accept it.
        /// </summary>
        [Test]
        public void PSFModeler_Solve_FaintStar_LowRSquared_But_GoodReducedChiSquared() {
            // Faint star: peak is very small relative to background.
            // tss = sum((output - yBar)²) is dominated by background noise, not the Gaussian shape.
            // With noise present, rss/tss is large → R² is pulled below the 0.9 threshold.
            // But the absolute residuals are consistent with the noise level → reducedChiSq < 2.
            const double trueSigma = 2.0;
            const double peak = 0.003;        // extremely faint star — signal << background
            const double background = 0.50;   // high sky background
            // Noise level comparable to the faint signal.  The Gaussian peak rises only
            // ~0.003 above background; noise at 0.002 makes the star nearly invisible in tss terms.
            const double noiseSigma = 0.002;

            var (inputs, outputs) = SampleGaussianWithNoise(trueSigma, trueSigma, peak, background, noiseSigma, seed: 42);
            var model = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: new Rect(0, 0, 11, 11),
                pixelScale: 1.0);

            var psf = PSFModeler.Solve(model, noiseSigma: noiseSigma, useAbsoluteResiduals: false, ct: CancellationToken.None);

            Assert.That(psf, Is.Not.Null);

            // R² should be low — the brightness-bias effect pushes R² down when peak << noise level
            Assert.That(psf.RSquared, Is.LessThan(0.9),
                "Faint star R² should be below 0.9 due to brightness bias (noise dominates tss when peak << noiseSigma)");

            // Reduced chi² should be good — the absolute residuals are consistent with the noise
            Assert.That(psf.ReducedChiSquared, Is.LessThan(2.0),
                "Reduced chi² should be < 2.0 (fit residuals within the noise level)");

            // Verify gating: the new chi² gate would accept this fit while the old R² gate rejects it
            const double chiSqThreshold = 2.0;
            const double rSquaredThreshold = 0.9;
            bool acceptedByChiSq = psf.ReducedChiSquared <= chiSqThreshold;
            bool acceptedByRSquared = psf.RSquared >= rSquaredThreshold;

            Assert.Multiple(() => {
                Assert.That(acceptedByChiSq, Is.True,
                    "Reduced chi² gate should ACCEPT the faint-star fit");
                Assert.That(acceptedByRSquared, Is.False,
                    "Old R² gate would REJECT the faint-star fit (brightness-bias)");
            });
        }

        /// <summary>
        /// Two-part test for the PSF canonical-form invariant (SigmaX ≥ SigmaY) and the ±π/2
        /// discontinuity fix for near-circular stars.
        ///
        /// Part 1 — Canonical-form invariant: runs PSFModeler.Solve 10 times on near-circular
        /// noisy data (σx = σy = 2) and asserts SigmaX ≥ SigmaY on every run.
        ///
        /// Part 2 — No ±π/2 flip for slightly elongated star: uses a mildly elongated star
        /// (σx = 2.2, σy = 2.0, axis-aligned) with 10 different noise seeds.  The reported
        /// θ values must all stay within π/4 of zero — a ±π/2 flip would take them
        /// outside that window.
        ///
        /// The fix seeds the LM optimizer with sigmaX ≥ sigmaY and applies a small tie-breaking
        /// nudge (sigmaX *= 1.001) so the optimizer starts unambiguously in the sigmaX > sigmaY
        /// basin, preventing the solver from returning sigmaY > sigmaX on some frames.
        /// </summary>
        [Test]
        public void PSFModeler_Solve_CanonicalFormAndNoThetaFlip() {
            const double peak = 0.8;
            const double background = 0.02;
            const double noiseSigma = 0.005;
            const int radius = 8;
            const int numRuns = 10;
            int side = 2 * radius + 1;

            // --- Part 1: canonical form SigmaX >= SigmaY for near-circular stars ---
            for (int run = 0; run < numRuns; ++run) {
                var (inputs, outputs) = SampleGaussianWithNoise(2.0, 2.0, peak, background, noiseSigma, seed: run, radius: radius);
                var model = new GaussianPSFAlglibType(
                    alglibAPI: alglibAPI,
                    inputs: inputs, outputs: outputs,
                    centroidBrightness: peak + background,
                    starDetectionBackground: background,
                    starBoundingBox: new Rect(0, 0, side, side),
                    pixelScale: 1.0);

                var psf = PSFModeler.Solve(model, noiseSigma: noiseSigma, useAbsoluteResiduals: false, ct: CancellationToken.None);
                Assert.That(psf, Is.Not.Null, $"Part 1 Run {run}: PSFModeler.Solve returned null");

                // After PSFModeler.Solve, SigmaX is always the major axis (canonical form).
                Assert.That(psf.SigmaX, Is.GreaterThanOrEqualTo(psf.SigmaY),
                    $"Part 1 Run {run}: canonical form violated — SigmaX={psf.SigmaX:F4} must be ≥ SigmaY={psf.SigmaY:F4}");
            }

            // --- Part 2: no ±π/2 flip for a slightly elongated star (σx=2.2, σy=2.0, axis-aligned) ---
            // The star has a clear preferred axis, so the reported θ should cluster near the true angle
            // regardless of which noise seed is used.  Previously the optimizer returned sigmaY > sigmaX
            // on some frames, triggering the post-hoc swap and shifting θ by ±π/2 (~±1.57 rad).
            var thetas = new double[numRuns];
            for (int run = 0; run < numRuns; ++run) {
                // Sample a rotated elongated Gaussian at the true angle.
                // For simplicity, sample axis-aligned (theta=0) and rely on the small elongation
                // (2.2 vs 2.0) to check the canonical form; the exact angle matters less than the flip.
                var (inputs, outputs) = SampleGaussianWithNoise(2.2, 2.0, peak, background, noiseSigma, seed: run + 100, radius: radius);
                var model = new GaussianPSFAlglibType(
                    alglibAPI: alglibAPI,
                    inputs: inputs, outputs: outputs,
                    centroidBrightness: peak + background,
                    starDetectionBackground: background,
                    starBoundingBox: new Rect(0, 0, side, side),
                    pixelScale: 1.0);

                var psf = PSFModeler.Solve(model, noiseSigma: noiseSigma, useAbsoluteResiduals: false, ct: CancellationToken.None);
                Assert.That(psf, Is.Not.Null, $"Part 2 Run {run}: PSFModeler.Solve returned null");

                // Canonical form must hold for every run
                Assert.That(psf.SigmaX, Is.GreaterThanOrEqualTo(psf.SigmaY),
                    $"Part 2 Run {run}: canonical form violated — SigmaX={psf.SigmaX:F4} must be ≥ SigmaY={psf.SigmaY:F4}");

                thetas[run] = psf.ThetaRadians;
            }

            // For the axis-aligned elongated star (true θ ≈ 0), all reported angles must be within
            // π/4 (45°) of zero.  A ±π/2 flip would push them to ≈ ±1.57, far outside this window.
            foreach (var theta in thetas) {
                // Wrap to [-π/2, π/2] range
                var wrapped = theta;
                while (wrapped > Math.PI / 2) wrapped -= Math.PI;
                while (wrapped < -Math.PI / 2) wrapped += Math.PI;
                Assert.That(Math.Abs(wrapped), Is.LessThan(Math.PI / 4),
                    $"Part 2: θ={theta:F4} rad is more than π/4 from zero — a ±π/2 flip may be present. " +
                    $"All angles: [{string.Join(", ", Array.ConvertAll(thetas, t => t.ToString("F4")))}]");
            }
        }

        /// <summary>
        /// Generates pixel values as the true area-integrated Gaussian over each pixel cell.
        /// This simulates what a real detector measures: the integral of the PSF over each pixel.
        /// The integral is computed via the error function so it is exact.
        /// </summary>
        private static (double[][] inputs, double[] outputs) SampleGaussianPixelIntegrated(
            double sigmaX, double sigmaY, double peak, double background,
            int radius = 5) {
            int side = 2 * radius + 1;
            var n = side * side;
            var inputs = new double[n][];
            var outputs = new double[n];
            int idx = 0;
            var sqrt2 = Math.Sqrt(2.0);
            for (var y = -radius; y <= radius; ++y) {
                for (var x = -radius; x <= radius; ++x) {
                    // Pixel spans [x-0.5, x+0.5] × [y-0.5, y+0.5]
                    // Integral of exp(-t²/(2σ²)) from a to b = σ√(2π) * [Φ(b/σ) − Φ(a/σ)]
                    // where Φ(z) = (1 + erf(z/√2)) / 2
                    // Pixel integral = peak * σx√(2π) * ΔΦx * σy√(2π) * ΔΦy  / (σx√(2π) * σy√(2π))
                    //                = peak * ΔΦx * ΔΦy   (after normalisation)
                    var loX = (x - 0.5) / (sigmaX * sqrt2);
                    var hiX = (x + 0.5) / (sigmaX * sqrt2);
                    var deltaPhiX = (SpecialFunctions.Erf(hiX) - SpecialFunctions.Erf(loX)) * 0.5;

                    var loY = (y - 0.5) / (sigmaY * sqrt2);
                    var hiY = (y + 0.5) / (sigmaY * sqrt2);
                    var deltaPhiY = (SpecialFunctions.Erf(hiY) - SpecialFunctions.Erf(loY)) * 0.5;

                    // Normalise by the CDF diff at the centroid pixel (x=0, y=0) so that
                    // the centroid pixel value equals peak (same convention as point-sampling).
                    var centrePhiX = SpecialFunctions.Erf(0.5 / (sigmaX * sqrt2));
                    var centrePhiY = SpecialFunctions.Erf(0.5 / (sigmaY * sqrt2));

                    var pixelValue = background + peak * (deltaPhiX / centrePhiX) * (deltaPhiY / centrePhiY);
                    inputs[idx] = new double[] { x, y };
                    outputs[idx] = pixelValue;
                    idx++;
                }
            }
            return (inputs, outputs);
        }

        /// <summary>
        /// Verifies that for an undersampled Gaussian (FWHM ≈ 1.5px, σ ≈ 0.638px):
        ///   - Point-sampling model gives > 5% error on σ when fitted to pixel-integrated data.
        ///   - Pixel-integration model gives ≤ 5% error on σ when fitted to the same data.
        ///
        /// This is the acceptance criterion for Task 13 (PSFPixelIntegration feature).
        /// </summary>
        [Test]
        public void GaussianPSF_PixelIntegration_UndersampledStar_ReducesSigmaError() {
            // FWHM = 1.5px → σ = FWHM / (2√(2 ln 2)) ≈ 0.6375 px
            const double fwhm = 1.5;
            var trueSigma = fwhm / GaussianPSFConstants.SIGMA_TO_FWHM_FACTOR;
            const double peak = 0.9;
            const double background = 0.02;

            // Generate outputs as true pixel-area integrals
            var (inputs, outputs) = SampleGaussianPixelIntegrated(trueSigma, trueSigma, peak, background, radius: 5);

            var bbox = new Rect(0, 0, 11, 11);

            // --- Point-sampling model (pixelIntegration = false) ---
            var pointModel = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: bbox,
                pixelScale: 1.0,
                pixelIntegration: false);

            var pointSol = pointModel.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);

            // --- Pixel-integration model (pixelIntegration = true) ---
            var intModel = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: bbox,
                pixelScale: 1.0,
                pixelIntegration: true);

            var intSol = intModel.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);

            var pointErrX = Math.Abs(pointSol.SigmaX - trueSigma) / trueSigma;
            var pointErrY = Math.Abs(pointSol.SigmaY - trueSigma) / trueSigma;
            var intErrX   = Math.Abs(intSol.SigmaX - trueSigma) / trueSigma;
            var intErrY   = Math.Abs(intSol.SigmaY - trueSigma) / trueSigma;

            Assert.Multiple(() => {
                // Pixel-integration model must recover sigma within 5%
                Assert.That(intErrX, Is.LessThanOrEqualTo(0.05),
                    $"Pixel-integration SigmaX error {intErrX:P1} should be ≤ 5% for FWHM=1.5px undersampled star. " +
                    $"trueSigma={trueSigma:F4}, fitted={intSol.SigmaX:F4}");
                Assert.That(intErrY, Is.LessThanOrEqualTo(0.05),
                    $"Pixel-integration SigmaY error {intErrY:P1} should be ≤ 5% for FWHM=1.5px undersampled star. " +
                    $"trueSigma={trueSigma:F4}, fitted={intSol.SigmaY:F4}");

                // Point-sampling model must have > 5% error on at least one sigma axis
                Assert.That(Math.Max(pointErrX, pointErrY), Is.GreaterThan(0.05),
                    $"Point-sampling max sigma error {Math.Max(pointErrX, pointErrY):P1} should be > 5% for " +
                    $"FWHM=1.5px undersampled star. trueSigma={trueSigma:F4}, " +
                    $"fittedX={pointSol.SigmaX:F4}, fittedY={pointSol.SigmaY:F4}");
            });
        }

        // Verify that with the loosened centroid bounds (box/2) the solver converges when the
        // seed centroid is 1.5 px away from the true star center. With the old box/8 bounds
        // (±1.375 px for an 11px box) the true offset of 1.5 px was outside the allowed range
        // and the fit would be clamped, yielding poor R². With box/2 (±5.5 px) it converges.
        [Test]
        public void GaussianPSF_Solve_ConvergesWithSeedCentroidOffset1_5px() {
            const double trueSigma = 1.5;
            const double peak = 0.8;
            const double background = 0.05;
            const double seedOffsetX = 1.5;  // detected centroid is 1.5 px to the right of the true center
            const double seedOffsetY = 0.0;
            var (inputs, outputs) = SampleGaussianWithCentroidOffset(trueSigma, trueSigma, peak, background, seedOffsetX, seedOffsetY);

            // 11×11 bounding box: box/8 gives ±1.375 px (too tight for 1.5 px offset),
            // box/2 gives ±5.5 px (sufficient).
            var model = new GaussianPSFAlglibType(
                alglibAPI: alglibAPI,
                inputs: inputs, outputs: outputs,
                centroidBrightness: peak + background,
                starDetectionBackground: background,
                starBoundingBox: new Rect(0, 0, 11, 11),
                pixelScale: 1.0);

            var sol = model.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);
            var rSq = model.GoodnessOfFit(sol.A, sol.B, sol.X0, sol.Y0, sol.SigmaX, sol.SigmaY, sol.Theta);

            Assert.Multiple(() => {
                // The solver should recover the true centroid offset (x0 ≈ -1.5, y0 ≈ 0)
                Assert.That(sol.X0, Is.EqualTo(-seedOffsetX).Within(0.1),
                    "Solver should recover x0 ≈ -1.5 when seed centroid is 1.5 px off");
                Assert.That(sol.Y0, Is.EqualTo(-seedOffsetY).Within(0.1),
                    "Solver should recover y0 ≈ 0 when seed centroid is not offset in y");
                Assert.That(sol.SigmaX, Is.EqualTo(trueSigma).Within(0.1),
                    "SigmaX should be recovered correctly");
                Assert.That(sol.SigmaY, Is.EqualTo(trueSigma).Within(0.1),
                    "SigmaY should be recovered correctly");
                // R² should be high — fit converged correctly
                Assert.That(rSq, Is.GreaterThan(0.95),
                    "R² should be high (>0.95) when box/2 bounds allow recovery from 1.5 px seed offset");
            });
        }
    }
}
