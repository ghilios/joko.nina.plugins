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

    /// <summary>
    /// Acceptance criterion: with Fittable β, fitting a synthetic Moffat with true β=2.5 recovers
    /// β within 5% of 2.5.
    /// </summary>
    [Test]
    public void FittableBeta_RecoversBeta_Within5Percent_ForTrueBeta2_5() {
        const double trueBeta = 2.5;
        const double trueSigma = 2.0;
        const double peak = 0.9;
        const double background = 0.02;
        const int radius = 8;
        var (inputs, outputs) = SampleMoffat(trueSigma, trueSigma, trueBeta, peak, background, radius);
        int side = 2 * radius + 1;

        var model = new FittableMoffatPSFAlglibType(
            alglibAPI: alglibAPI,
            inputs: inputs, outputs: outputs,
            centroidBrightness: peak + background, starDetectionBackground: background,
            starBoundingBox: new Rect(0, 0, side, side), pixelScale: 1.0);

        var sol = model.Solve(maxIterations: 300, tolerance: 1e-10, ct: CancellationToken.None);

        Assert.Multiple(() => {
            // β must be recovered within 5% of 2.5
            Assert.That(model.Beta, Is.EqualTo(trueBeta).Within(0.05 * trueBeta),
                $"Fittable β should recover true β={trueBeta} within 5%; got β={model.Beta:F4}");
            // σ must still be reasonable
            Assert.That(sol.SigmaX, Is.EqualTo(trueSigma).Within(0.20 * trueSigma),
                $"SigmaX should be near true sigma={trueSigma}; got {sol.SigmaX:F4}");
        });
    }

    /// <summary>
    /// Acceptance criterion: with Fixed_1_5, MoffatPSFType uses β=1.5.
    /// </summary>
    [Test]
    public void FixedBeta_1_5_UsesCorrectBeta() {
        var (inputs, outputs) = SampleMoffat(2.0, 2.0, 1.5, 0.9, 0.02);
        var model = new MoffatPSFAlglibType(
            alglibAPI: alglibAPI, beta: 1.5,
            inputs: inputs, outputs: outputs,
            centroidBrightness: 0.92, starDetectionBackground: 0.02,
            starBoundingBox: new Rect(0, 0, 13, 13), pixelScale: 1.0);

        Assert.That(model.Beta, Is.EqualTo(1.5).Within(1e-12),
            "MoffatPSFAlglibType should store β=1.5 when constructed with beta=1.5");

        // Verify Value at centroid uses β=1.5 (D=1 → result = B + A/1^β = B + A regardless of β)
        var p = new[] { 0.88, 0.02, 0.0, 0.0, 2.0, 2.0, 0.0 };
        var v = model.Value(p, new double[] { 0.0, 0.0 });
        Assert.That(v, Is.EqualTo(0.9).Within(1e-12));

        // Verify FWHM uses β=1.5 via SigmaToFWHM
        var fwhm = model.SigmaToFWHM(2.0);
        var expected = MoffatShared.SigmaToFWHM(1.5, 2.0);
        Assert.That(fwhm, Is.EqualTo(expected).Within(1e-12));
    }

    /// <summary>
    /// Acceptance criterion: PSFModeler.Create with MoffatFittable returns a
    /// FittableMoffatPSFAlglibType instance; Moffat_15 returns a MoffatPSFAlglibType with β=1.5.
    /// </summary>
    [Test]
    public void PSFModeler_Create_PSFFitType_ReturnsCorrectType() {
        const double trueSigma = 2.0;
        const int radius = 6;
        int side = 2 * radius + 1;
        var (inputs, outputs) = SampleMoffat(trueSigma, trueSigma, 4.0, 0.9, 0.02, radius);

        var detectedStar = new Star {
            Center = new OpenCvSharp.Point2d(radius, radius),
            Background = 0.02,
            StarBoundingBox = new Rect(0, 0, side, side)
        };

        using (var srcImage = new OpenCvSharp.Mat(side, side, OpenCvSharp.MatType.CV_32F)) {
            unsafe {
                var data = (float*)srcImage.DataPointer;
                for (int i = 0; i < outputs.Length; ++i) {
                    data[i] = (float)outputs[i];
                }
            }

            // MoffatFittable → FittableMoffatPSFAlglibType
            var fittableModel = PSFModeler.Create(
                alglibAPI: alglibAPI,
                fitType: StarDetectorPSFFitType.MoffatFittable,
                psfResolution: side,
                detectedStar: detectedStar,
                srcImage: srcImage,
                pixelScale: 1.0);
            Assert.That(fittableModel, Is.InstanceOf<FittableMoffatPSFAlglibType>(),
                "Create with MoffatFittable should return FittableMoffatPSFAlglibType");

            // Moffat_15 → MoffatPSFAlglibType with β=1.5
            var fixed15Model = PSFModeler.Create(
                alglibAPI: alglibAPI,
                fitType: StarDetectorPSFFitType.Moffat_15,
                psfResolution: side,
                detectedStar: detectedStar,
                srcImage: srcImage,
                pixelScale: 1.0);
            Assert.That(fixed15Model, Is.InstanceOf<MoffatPSFAlglibType>());
            Assert.That(((MoffatPSFAlglibType)fixed15Model).Beta, Is.EqualTo(1.5).Within(1e-12),
                "Create with Moffat_15 should use β=1.5");
        }
    }
}
