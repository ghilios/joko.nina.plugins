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
