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
    }
}
