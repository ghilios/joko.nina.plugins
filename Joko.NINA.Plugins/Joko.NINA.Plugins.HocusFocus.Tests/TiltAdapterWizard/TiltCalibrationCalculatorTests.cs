using System;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

[TestFixture]
public class TiltCalibrationCalculatorTests {

    // --- Forward model: the (A,B) tilt-plane reading produced by turning a single screw at angle θ inward by a
    // known axial amount d (microns). G = (2/(n·R²))·d·p(θ); A = Gx·sensorW/fStep, B = Gy·sensorH/fStep.
    // This is the exact inverse of the recovery math, so feeding its output back must round-trip.
    private const double PixelSize = 3.76;
    private const int ImgW = 6000;
    private const int ImgH = 6000;
    private const double SensorW = ImgW * PixelSize;
    private const double SensorH = ImgH * PixelSize;
    private const double FStep = 3.58;
    private const double RadiusMm = 44.0;
    private const double RadiusMicrons = RadiusMm * 1000.0;

    private static TiltGradient SingleScrewReading(double angleDeg, double axialMicrons, int n, double meanFocuser = 0.0) {
        var (px, py) = TiltScrewGeometry.ScrewPositionMicrons(angleDeg, RadiusMicrons);
        double scale = 2.0 / (n * RadiusMicrons * RadiusMicrons) * axialMicrons;
        double gx = scale * px;
        double gy = scale * py;
        double a = gx * SensorW / FStep;
        double b = gy * SensorH / FStep;
        return new TiltGradient(a, b, meanFocuser);
    }

    [Test]
    public void NormalizeAngle_WrapsIntoZeroTo360() {
        Assert.Multiple(() => {
            Assert.That(TiltCalibrationCalculator.NormalizeAngle(-90), Is.EqualTo(270).Within(1e-9));
            Assert.That(TiltCalibrationCalculator.NormalizeAngle(450), Is.EqualTo(90).Within(1e-9));
            Assert.That(TiltCalibrationCalculator.NormalizeAngle(0), Is.EqualTo(0).Within(1e-9));
        });
    }

    [Test]
    public void ComputeScrewAngles_ThreeScrew_Clockwise() {
        // Screw 1 at 0°, screw 2 at 120° (the natural CW order).
        var (s1, s2, s3, s4, rawDiff) = TiltCalibrationCalculator.ComputeScrewAngles(
            Math.Sin(0), -Math.Cos(0),
            Math.Sin(120 * Math.PI / 180), -Math.Cos(120 * Math.PI / 180), 3);
        Assert.Multiple(() => {
            Assert.That(s1, Is.EqualTo(0).Within(1e-6));
            Assert.That(s2, Is.EqualTo(120).Within(1e-6));
            Assert.That(s3, Is.EqualTo(240).Within(1e-6));
            Assert.That(double.IsNaN(s4), Is.True);
            Assert.That(rawDiff, Is.EqualTo(120).Within(1e-6));
        });
    }

    [Test]
    public void ComputeScrewAngles_ThreeScrew_CounterClockwise_Mirrored() {
        // Screw 2 appears at 240° (image mirroring flips winding); the fit recovers 0 / 240 / 120.
        var (s1, s2, s3, _, rawDiff) = TiltCalibrationCalculator.ComputeScrewAngles(
            Math.Sin(0), -Math.Cos(0),
            Math.Sin(240 * Math.PI / 180), -Math.Cos(240 * Math.PI / 180), 3);
        Assert.Multiple(() => {
            Assert.That(s1, Is.EqualTo(0).Within(1e-6));
            Assert.That(s2, Is.EqualTo(240).Within(1e-6));
            Assert.That(s3, Is.EqualTo(120).Within(1e-6));
            Assert.That(rawDiff, Is.EqualTo(240).Within(1e-6));
        });
    }

    [Test]
    public void ComputeScrewAngles_ThreeScrew_SplitsMeasurementErrorEvenly() {
        // Measured screw1=5°, screw2=130° (gap 125° vs ideal 120°): the constrained fit shifts screw1 by half the
        // residual to 7.5°, keeping 120° spacing.
        double a1 = 5 * Math.PI / 180, a2 = 130 * Math.PI / 180;
        var (s1, s2, s3, _, rawDiff) = TiltCalibrationCalculator.ComputeScrewAngles(
            Math.Sin(a1), -Math.Cos(a1), Math.Sin(a2), -Math.Cos(a2), 3);
        Assert.Multiple(() => {
            Assert.That(s1, Is.EqualTo(7.5).Within(1e-6));
            Assert.That(s2, Is.EqualTo(127.5).Within(1e-6));
            Assert.That(s3, Is.EqualTo(247.5).Within(1e-6));
            Assert.That(rawDiff, Is.EqualTo(125).Within(1e-6));
        });
    }

    [Test]
    public void ComputeScrewAngles_FourScrew_OppositeScrewsAre180Apart() {
        double a2 = 90 * Math.PI / 180;
        var (s1, s2, s3, s4, _) = TiltCalibrationCalculator.ComputeScrewAngles(
            Math.Sin(0), -Math.Cos(0), Math.Sin(a2), -Math.Cos(a2), 4);
        Assert.Multiple(() => {
            Assert.That(s1, Is.EqualTo(0).Within(1e-6));
            Assert.That(s2, Is.EqualTo(90).Within(1e-6));
            Assert.That(s3, Is.EqualTo(180).Within(1e-6));
            Assert.That(s4, Is.EqualTo(270).Within(1e-6));
        });
    }

    [Test]
    public void ComputeCurvatureSign_PositiveWhenAllScrewsMeanHigher() {
        Assert.Multiple(() => {
            Assert.That(TiltCalibrationCalculator.ComputeCurvatureSign(1100, 1000), Is.EqualTo(1));
            Assert.That(TiltCalibrationCalculator.ComputeCurvatureSign(900, 1000), Is.EqualTo(-1));
            Assert.That(TiltCalibrationCalculator.ComputeCurvatureSign(1000, 1000), Is.EqualTo(1));
        });
    }

    [Test]
    public void RecoverHardwareMicrons_RoundTripsAppliedPitch() {
        // Both screws turned inward by one full turn = a known thread pitch; recovery must return that pitch.
        const double pitch = 400.0;
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            Baseline = new TiltGradient(0, 0, 1000),
            AllScrews = new TiltGradient(0, 0, 1000),
            Screw1 = SingleScrewReading(0, pitch, 3),
            Screw2 = SingleScrewReading(120, pitch, 3),
            ImageWidthPixels = ImgW,
            ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize,
            FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm,
            CalibrationAppliedAmount = 1.0,
            IsStepperAdjustment = false
        };
        Assert.That(TiltCalibrationCalculator.RecoverHardwareMicrons(inputs), Is.EqualTo(pitch).Within(1e-3));
    }

    [Test]
    public void RecoverHardwareMicrons_HalfTurnDoublesRecoveredPitch() {
        // The same axial move achieved in half a turn implies twice the µm/turn.
        const double axial = 200.0;
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            Baseline = new TiltGradient(0, 0, 0),
            AllScrews = new TiltGradient(0, 0, 0),
            Screw1 = SingleScrewReading(0, axial, 3),
            Screw2 = SingleScrewReading(120, axial, 3),
            ImageWidthPixels = ImgW,
            ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize,
            FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm,
            CalibrationAppliedAmount = 0.5,
            IsStepperAdjustment = false
        };
        Assert.That(TiltCalibrationCalculator.RecoverHardwareMicrons(inputs), Is.EqualTo(2 * axial).Within(1e-3));
    }

    [TestCase(0.0)]
    [TestCase(-5.0)]
    public void RecoverHardwareMicrons_ReturnsNaN_OnNonPositiveInputs(double badApplied) {
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            Baseline = new TiltGradient(0, 0, 0),
            AllScrews = new TiltGradient(0, 0, 0),
            Screw1 = SingleScrewReading(0, 400, 3),
            Screw2 = SingleScrewReading(120, 400, 3),
            ImageWidthPixels = ImgW,
            ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize,
            FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm,
            CalibrationAppliedAmount = badApplied,
            IsStepperAdjustment = false
        };
        Assert.That(double.IsNaN(TiltCalibrationCalculator.RecoverHardwareMicrons(inputs)), Is.True);
    }

    [Test]
    public void MoveMagnitudeRatio_OneForEqualMoves_GrowsWithImbalance() {
        // Two equal-magnitude moves -> ratio 1; a 3x longer second move -> ratio 3.
        Assert.Multiple(() => {
            Assert.That(TiltCalibrationCalculator.MoveMagnitudeRatio(0, -10, 10, 0), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(TiltCalibrationCalculator.MoveMagnitudeRatio(0, -10, 0, -30), Is.EqualTo(3.0).Within(1e-9));
            Assert.That(TiltCalibrationCalculator.MoveMagnitudeRatio(0, -30, 0, -10), Is.EqualTo(3.0).Within(1e-9)); // order-independent
            Assert.That(double.IsNaN(TiltCalibrationCalculator.MoveMagnitudeRatio(0, 0, 1, 1)), Is.True);
        });
    }

    [Test]
    public void Calibrate_PopulatesMoveMagnitudeRatio() {
        // Screw2 turned twice as far as Screw1 -> ratio ~2.
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            Baseline = new TiltGradient(0, 0, 0),
            AllScrews = new TiltGradient(0, 0, 0),
            Screw1 = SingleScrewReading(0, 200, 3),
            Screw2 = SingleScrewReading(120, 400, 3),
            ImageWidthPixels = ImgW,
            ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize,
            FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm,
            CalibrationAppliedAmount = 1.0
        };
        Assert.That(TiltCalibrationCalculator.Calibrate(inputs).MoveMagnitudeRatio, Is.EqualTo(2.0).Within(1e-6));
    }

    [Test]
    public void Calibrate_EndToEnd_ThreeScrew_RecoversAnglesPitchAndSign() {
        const double pitch = 400.0;
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            Baseline = new TiltGradient(0, 0, 1000),
            AllScrews = new TiltGradient(0, 0, 1075), // all-screws-inward raised mean focus -> +1
            Screw1 = SingleScrewReading(0, pitch, 3, 1000),
            Screw2 = SingleScrewReading(120, pitch, 3, 1000),
            ImageWidthPixels = ImgW,
            ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize,
            FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm,
            CalibrationAppliedAmount = 1.0,
            IsStepperAdjustment = false
        };
        var r = TiltCalibrationCalculator.Calibrate(inputs);
        Assert.Multiple(() => {
            Assert.That(r.Screw1AngleDegrees, Is.EqualTo(0).Within(1e-4));
            Assert.That(r.Screw2AngleDegrees, Is.EqualTo(120).Within(1e-4));
            Assert.That(r.Screw3AngleDegrees, Is.EqualTo(240).Within(1e-4));
            Assert.That(r.CalibratedScrewCount, Is.EqualTo(3));
            Assert.That(r.IsCalibrated, Is.True);
            Assert.That(r.CurvatureSign, Is.EqualTo(1));
            Assert.That(r.MeasuredHardwareMicrons, Is.EqualTo(pitch).Within(1e-3));
            Assert.That(r.Screw1DirectionDegrees, Is.EqualTo(0).Within(1e-4));
            Assert.That(r.Screw2DirectionDegrees, Is.EqualTo(120).Within(1e-4));
        });
    }
}
