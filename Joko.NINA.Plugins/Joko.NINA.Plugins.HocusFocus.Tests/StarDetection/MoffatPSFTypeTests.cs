using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

[TestFixture]
public class MoffatPSFTypeTests {
    private IAlglibAPI alglibAPI;

    [SetUp]
    public void SetUp() {
        alglibAPI = new AlglibAPI();
    }

    private static (double[][] inputs, double[] outputs) SampleMoffat(
        double sigmaX, double sigmaY, double beta, double peak, double background,
        int radius = 6) {
        int side = 2 * radius + 1;
        var n = side * side;
        var inputs = new double[n][];
        var outputs = new double[n];
        int idx = 0;
        for (var y = -radius; y <= radius; ++y) {
            for (var x = -radius; x <= radius; ++x) {
                var d = 1.0 + (x * x) / (sigmaX * sigmaX) + (y * y) / (sigmaY * sigmaY);
                inputs[idx] = new double[] { x, y };
                outputs[idx] = background + peak / Math.Pow(d, beta);
                idx++;
            }
        }
        return (inputs, outputs);
    }

    [Test]
    public void Constructor_StoresBeta() {
        var (inputs, outputs) = SampleMoffat(1.0, 1.0, 4.0, 1.0, 0.0);
        var model = new MoffatPSFAlglibType(
            alglibAPI: alglibAPI, beta: 4.0,
            inputs: inputs, outputs: outputs,
            centroidBrightness: 1.0, starDetectionBackground: 0.0,
            starBoundingBox: new Rect(0, 0, 13, 13), pixelScale: 1.0);
        Assert.That(model.Beta, Is.EqualTo(4.0));
        Assert.That(model.PSFType, Is.EqualTo(StarDetectorPSFFitType.Moffat_40));
        Assert.That(model.UseJacobian, Is.True);
    }

    [Test]
    public void Solve_RecoversSigmasOnNoiselessSymmetricStar() {
        const double trueSigma = 2.0;
        const double beta = 4.0;
        const double peak = 1.0;
        const double background = 0.0;
        var (inputs, outputs) = SampleMoffat(trueSigma, trueSigma, beta, peak, background);

        var model = new MoffatPSFAlglibType(
            alglibAPI: alglibAPI, beta: beta,
            inputs: inputs, outputs: outputs,
            centroidBrightness: peak, starDetectionBackground: background,
            starBoundingBox: new Rect(0, 0, 13, 13), pixelScale: 1.0);

        var sol = model.Solve(maxIterations: 200, tolerance: 1e-10, ct: CancellationToken.None);
        var rSquared = model.GoodnessOfFit(sol.A, sol.B, sol.X0, sol.Y0, sol.SigmaX, sol.SigmaY, sol.Theta);

        Assert.Multiple(() => {
            Assert.That(sol.SigmaX, Is.EqualTo(trueSigma).Within(0.15));
            Assert.That(sol.SigmaY, Is.EqualTo(trueSigma).Within(0.15));
            Assert.That(sol.X0, Is.EqualTo(0.0).Within(0.05));
            Assert.That(sol.Y0, Is.EqualTo(0.0).Within(0.05));
            Assert.That(rSquared, Is.GreaterThan(0.99));
        });
    }

    [Test]
    public void Solve_CancellationToken_PropagatesCancellation() {
        var (inputs, outputs) = SampleMoffat(2.0, 2.0, 4.0, 1.0, 0.0);
        var model = new MoffatPSFAlglibType(
            alglibAPI: alglibAPI, beta: 4.0,
            inputs: inputs, outputs: outputs,
            centroidBrightness: 1.0, starDetectionBackground: 0.0,
            starBoundingBox: new Rect(0, 0, 13, 13), pixelScale: 1.0);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            model.Solve(maxIterations: 200, tolerance: 1e-10, ct: cts.Token));
    }

    [Test]
    public void SigmaToFWHM_MatchesShared() {
        var (inputs, outputs) = SampleMoffat(2.0, 2.0, 4.0, 1.0, 0.0);
        var model = new MoffatPSFAlglibType(
            alglibAPI: alglibAPI, beta: 4.0,
            inputs: inputs, outputs: outputs,
            centroidBrightness: 1.0, starDetectionBackground: 0.0,
            starBoundingBox: new Rect(0, 0, 13, 13), pixelScale: 1.0);

        var fwhm = model.SigmaToFWHM(2.0);
        var expected = MoffatShared.SigmaToFWHM(4.0, 2.0);
        Assert.That(fwhm, Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void MoffatShared_SigmaToFWHM_RecoversKnownValueForBeta1() {
        // For beta=1: FWHM = 2*sigma*sqrt(2-1) = 2*sigma
        Assert.That(MoffatShared.SigmaToFWHM(1.0, 3.5), Is.EqualTo(7.0).Within(1e-12));
    }

    [Test]
    public void Value_AtCentroid_EqualsBackgroundPlusA() {
        var (inputs, outputs) = SampleMoffat(2.0, 2.0, 4.0, 1.0, 0.0);
        var model = new MoffatPSFAlglibType(
            alglibAPI: alglibAPI, beta: 4.0,
            inputs: inputs, outputs: outputs,
            centroidBrightness: 1.0, starDetectionBackground: 0.0,
            starBoundingBox: new Rect(0, 0, 13, 13), pixelScale: 1.0);

        // Parameters: A, B, x0, y0, sigmaX, sigmaY, theta
        // At input = (x0, y0) the denominator D = 1, so value = B + A/1^beta = A + B
        var p = new[] { 0.7, 0.05, 0.0, 0.0, 2.0, 2.0, 0.0 };
        var v = model.Value(p, new double[] { 0.0, 0.0 });
        Assert.That(v, Is.EqualTo(0.75).Within(1e-12));
    }
}
