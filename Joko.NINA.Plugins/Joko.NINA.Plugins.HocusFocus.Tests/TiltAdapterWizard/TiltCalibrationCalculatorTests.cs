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
    public void ComputeCurvatureSign_PositiveWhenAllScrewsMeanLower() {
        // σ = −sign(allScrewsMean − baselineMean): an all-screws-CW step that LOWERS the mean
        // best-focus position is σ = +1 (docs/focuser-direction-convention-design.md §1(c)).
        Assert.Multiple(() => {
            Assert.That(TiltCalibrationCalculator.ComputeCurvatureSign(1100, 1000), Is.EqualTo(-1));
            Assert.That(TiltCalibrationCalculator.ComputeCurvatureSign(900, 1000), Is.EqualTo(1));
            // Degenerate zero delta still resolves to +1, as it always has.
            Assert.That(TiltCalibrationCalculator.ComputeCurvatureSign(1000, 1000), Is.EqualTo(1));
        });
    }

    [Test]
    public void ComputeCurvatureSign_Session20260803_BaselineHigherMeansTowardCamera() {
        // Session 20260803-200647: a +150 backfocus step (vendor mnemonic — plate toward the
        // CAMERA, so m = +1) on a standard focuser (k = +1) moved the mean best-focus position
        // 5498.4 → 5136.6. σ = −sign(−361.8) = +1, which reads "clockwise moves the adapter toward
        // the CAMERA" via m = σ·k — exactly the value the user had to set by hand before this fix
        // to make corrections converge. This is the regression pin for that incident.
        Assert.That(TiltCalibrationCalculator.ComputeCurvatureSign(5136.6, 5498.4), Is.EqualTo(1));
    }

    [Test]
    public void RecoverHardwareMicrons_RoundTripsAppliedPitch() {
        // Both screws turned inward by one full turn = a known thread pitch; recovery must return that pitch.
        const double pitch = 400.0;
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            Baseline = new TiltGradient(0, 0, 1000),
            AllInward = new TiltGradient(0, 0, 1000),
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
            AllInward = new TiltGradient(0, 0, 0),
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
            AllInward = new TiltGradient(0, 0, 0),
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
    public void PitchUncertainty_IsHalfTheDifferenceOfPerScrewMoves() {
        // Two single-screw moves of clearly unequal magnitude along different axes.
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            ReBaseline1 = new TiltGradient(0, 0, 0),
            Screw1 = new TiltGradient(20, 0, 0),      // move along A
            ReBaseline2 = new TiltGradient(0, 0, 0),
            Screw2 = new TiltGradient(0, 10, 0),      // move along B
            ImageWidthPixels = 6248, ImageHeightPixels = 4176,
            PixelSizeMicrons = 3.76, FocuserStepMicrons = 3.6,
            ScrewRadiusMillimeters = 44, CalibrationAppliedAmount = 1.0, IsStepperAdjustment = false
        };
        var (measured, d1, d2) = TiltCalibrationCalculator.RecoverHardwareDetailed(inputs);
        Assert.Multiple(() => {
            Assert.That(measured, Is.EqualTo(0.5 * (d1 + d2)).Within(1e-9));
            var result = TiltCalibrationCalculator.Calibrate(inputs);
            Assert.That(result.PitchUncertaintyMicrons, Is.EqualTo(Math.Abs(d1 - d2) / 2.0).Within(1e-9));
            Assert.That(result.PitchUncertaintyMicrons, Is.GreaterThan(0));
        });
    }

    [Test]
    public void PitchUncertainty_IsNaN_WhenHardwareUncomputable() {
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3, ScrewRadiusMillimeters = 0 /* invalid */,
            CalibrationAppliedAmount = 1.0, PixelSizeMicrons = 3.76, FocuserStepMicrons = 3.6,
            ImageWidthPixels = 6248, ImageHeightPixels = 4176
        };
        Assert.That(TiltCalibrationCalculator.Calibrate(inputs).PitchUncertaintyMicrons, Is.NaN);
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
            AllInward = new TiltGradient(0, 0, 0),
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
    public void Calibrate_AnisotropicSensor_ReadsPhysicalGapAndEqualMagnitudes() {
        // On a 3:2 sensor, equal-magnitude physical moves at 200° and 290° produce these (A,B) deltas.
        // In raw (A,B) space they would read: gap 75.01°, magnitude ratio 1.354 — the F2 bug.
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 4,
            ReBaseline1 = new TiltGradient(0, 0, 0),
            Screw1 = new TiltGradient(-77.1597, 141.3298, 0),
            ReBaseline2 = new TiltGradient(0, 0, 0),
            Screw2 = new TiltGradient(-211.9947, -51.4398, 0),
            ImageWidthPixels = 6000, ImageHeightPixels = 4000,
            PixelSizeMicrons = 3.76, FocuserStepMicrons = 0.5,
            ScrewRadiusMillimeters = 44.0, CalibrationAppliedAmount = 1.0,
            IsStepperAdjustment = false, HasCurvatureMeasurement = false, FallbackCurvatureSign = 1,
        };
        var result = TiltCalibrationCalculator.Calibrate(inputs);
        Assert.Multiple(() => {
            Assert.That(result.RawAngleDiffDegrees, Is.EqualTo(90.0).Within(0.01));
            Assert.That(result.MoveMagnitudeRatio, Is.EqualTo(1.0).Within(0.001));
            Assert.That(result.Screw1DirectionDegrees, Is.EqualTo(200.0).Within(0.01));
            Assert.That(result.Screw2DirectionDegrees, Is.EqualTo(290.0).Within(0.01));
            Assert.That(result.Screw1AngleDegrees, Is.EqualTo(200.0).Within(0.01));
        });
    }

    private static TiltGradient Plus(TiltGradient a, TiltGradient b) =>
        new TiltGradient(a.A + b.A, a.B + b.B, a.MeanFocuserPosition + b.MeanFocuserPosition);

    [Test]
    public void Calibrate_DerivesScrewDeltasFromReBaselineNotBaseline() {
        // The screw moves are measured against the re-baseline(s), not the original baseline, so a drifted
        // re-baseline (offset from the original baseline) must NOT contaminate the recovered angles: the single
        // screw move is added on top of the re-baseline reading and the delta isolates it. Screw 1's reference is
        // now mid(ReBaseline1, ReBaseline2) (drift-cancelling); constructing ReBaseline1 == ReBaseline2 here keeps
        // that midpoint exactly equal to the drifted re-baseline, so the expectations stay exact and obviously
        // correct (this is not testing drift-cancellation itself -- see Calibrate_LinearTiltDrift_CancelsExactlyForScrew1
        // for that).
        const double pitch = 400.0;
        var reBaseline = new TiltGradient(12.0, -7.0, 1000);  // c and e both drifted identically from baseline a
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            Baseline = new TiltGradient(0, 0, 1000),
            AllInward = new TiltGradient(0, 0, 1075),
            ReBaseline1 = reBaseline,
            Screw1 = Plus(reBaseline, SingleScrewReading(0, pitch, 3)),
            ReBaseline2 = reBaseline,
            Screw2 = Plus(reBaseline, SingleScrewReading(120, pitch, 3)),
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
            Assert.That(r.MeasuredHardwareMicrons, Is.EqualTo(pitch).Within(1e-3));
            // AllInward mean (1075) above baseline (1000) ⇒ σ = −1.
            Assert.That(r.CurvatureSign, Is.EqualTo(-1));
        });
    }

    [Test]
    public void Calibrate_LinearTiltDrift_CancelsExactlyForScrew1() {
        // A constant drift vector v is added per measurement interval. The screw-1 move D is bracketed
        // by ReBaseline1 (2 intervals in) and ReBaseline2 (4 intervals in), with Screw1 at 3 intervals:
        // Screw1 − mid(RB1, RB2) recovers D exactly. The old delta (Screw1 − RB1) is off by |v|.
        var (vA, vB) = (9.0, -5.0);
        var move1 = SingleScrewReading(0, 400.0, 4);      // D
        var move2 = SingleScrewReading(90, 400.0, 4);     // E
        TiltGradient Drift(TiltGradient g, int k) => new TiltGradient(g.A + k * vA, g.B + k * vB, g.MeanFocuserPosition);

        var drifted = new TiltCalibrationInputs {
            ScrewCount = 4,
            Baseline = Drift(new TiltGradient(0, 0, 0), 0),
            AllInward = Drift(new TiltGradient(0, 0, 100), 1),
            ReBaseline1 = Drift(new TiltGradient(0, 0, 0), 2),
            Screw1 = Drift(move1, 3),
            ReBaseline2 = Drift(new TiltGradient(0, 0, 0), 4),
            Screw2 = Drift(move2, 5),
            ImageWidthPixels = ImgW, ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize, FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm, CalibrationAppliedAmount = 1.0,
            IsStepperAdjustment = false,
        };
        // Same shape, but with zero drift applied at every step (k=0 everywhere) -- the drift-free reference.
        var cleanInputs = new TiltCalibrationInputs {
            ScrewCount = 4,
            Baseline = Drift(new TiltGradient(0, 0, 0), 0),
            AllInward = Drift(new TiltGradient(0, 0, 100), 0),
            ReBaseline1 = Drift(new TiltGradient(0, 0, 0), 0),
            Screw1 = Drift(move1, 0),
            ReBaseline2 = Drift(new TiltGradient(0, 0, 0), 0),
            Screw2 = Drift(move2, 0),
            ImageWidthPixels = ImgW, ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize, FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm, CalibrationAppliedAmount = 1.0,
            IsStepperAdjustment = false,
        };
        var clean = TiltCalibrationCalculator.Calibrate(cleanInputs);
        var driftedResult = TiltCalibrationCalculator.Calibrate(drifted);
        Assert.Multiple(() => {
            // Screw 1's move is bracketed by ReBaseline1/ReBaseline2, so its recovered direction is drift-immune.
            Assert.That(driftedResult.Screw1DirectionDegrees, Is.EqualTo(clean.Screw1DirectionDegrees).Within(1e-9));
            // Screw 2 has no measured final re-baseline (HasFinalRebaseline defaults false), so it keeps the old
            // ReBaseline2-only reference and is NOT drift-immune -- this is an intentional lock on that asymmetry,
            // not a caveat: it fails if a future change accidentally gives Screw2Delta symmetric behavior before
            // Task 6 wires HasFinalRebaseline. The measured direction shift here is ~7.57 degrees (82.43 vs
            // 90.00), comfortably beyond any floating-point tolerance.
            Assert.That(driftedResult.Screw2DirectionDegrees, Is.Not.EqualTo(clean.Screw2DirectionDegrees).Within(1e-6));
        });
    }

    [Test]
    public void RebaselineDriftRatio_ZeroWhenNoDrift_GrowsWithDrift() {
        Assert.Multiple(() => {
            // No drift relative to a move of magnitude 10 -> 0.
            Assert.That(TiltCalibrationCalculator.RebaselineDriftRatio(0, 0, 0, 10), Is.EqualTo(0.0).Within(1e-9));
            // Drift magnitude 5 vs move magnitude 10 -> 0.5.
            Assert.That(TiltCalibrationCalculator.RebaselineDriftRatio(3, 4, 0, 10), Is.EqualTo(0.5).Within(1e-9));
            // Drift equal to the move -> 1.
            Assert.That(TiltCalibrationCalculator.RebaselineDriftRatio(0, 10, 10, 0), Is.EqualTo(1.0).Within(1e-9));
            // Zero move magnitude -> NaN.
            Assert.That(double.IsNaN(TiltCalibrationCalculator.RebaselineDriftRatio(1, 1, 0, 0)), Is.True);
        });
    }

    [Test]
    public void Calibrate_EndToEnd_ThreeScrew_RecoversAnglesPitchAndSign() {
        const double pitch = 400.0;
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            Baseline = new TiltGradient(0, 0, 1000),
            AllInward = new TiltGradient(0, 0, 1075), // all-screws-inward raised mean focus -> −1
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
            Assert.That(r.CurvatureSign, Is.EqualTo(-1));
            Assert.That(r.MeasuredHardwareMicrons, Is.EqualTo(pitch).Within(1e-3));
            Assert.That(r.Screw1DirectionDegrees, Is.EqualTo(0).Within(1e-4));
            Assert.That(r.Screw2DirectionDegrees, Is.EqualTo(120).Within(1e-4));
        });
    }

    // --- Calibration confidence (signal-to-noise) ---

    [Test]
    public void ComputeConfidence_CleanMeasurement_HighSnrAndReliable() {
        // Baselines all identical (zero noise probes) and two clean equal screw moves -> infinite SNR, reliable.
        // Deliberately isotropic unit geometry (1x1 sensor, 1 µm pixel, 1 µm focuser step): PhysicalDelta's
        // gx = A*fStep/(W*pixelSize) = A*1/(1*1) = A (and same for B/gy), so this is byte-identical to raw
        // (A,B) units — this pure noise-model algebra test is unaffected by the physical-gradient-space
        // conversion (F2 fix). PhysicalDelta itself has no geometry fallback (see its doc comment): real
        // geometry is a precondition everywhere, including here.
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            ImageWidthPixels = 1, ImageHeightPixels = 1, PixelSizeMicrons = 1, FocuserStepMicrons = 1,
            Baseline = new TiltGradient(0, 0, 1000),
            AllInward = new TiltGradient(0, 0, 1075),   // piston only: no tilt change vs baseline
            ReBaseline1 = new TiltGradient(0, 0, 1000),
            Screw1 = new TiltGradient(10, 0, 1000),
            ReBaseline2 = new TiltGradient(0, 0, 1000),
            Screw2 = new TiltGradient(0, 10, 1000),
        };
        var c = TiltCalibrationCalculator.ComputeConfidence(inputs);
        Assert.Multiple(() => {
            Assert.That(c.ScrewMoveSignal, Is.EqualTo(10).Within(1e-9));
            Assert.That(c.NoiseEstimate, Is.EqualTo(0).Within(1e-9));
            Assert.That(double.IsPositiveInfinity(c.SignalToNoise), Is.True);
            Assert.That(c.PredictedAngleUncertaintyDeg, Is.EqualTo(0).Within(1e-9));
            Assert.That(c.IsReliable, Is.True);
        });
    }

    [Test]
    public void ComputeConfidence_NoiseRivalsSignal_FlaggedUnreliable() {
        // A large all-inward tilt residual (a pure piston should give ~0) drives the noise floor near the signal.
        // Deliberately isotropic unit geometry (see ComputeConfidence_CleanMeasurement_HighSnrAndReliable) so
        // PhysicalDelta is byte-identical to the raw (A,B) delta.
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            ImageWidthPixels = 1, ImageHeightPixels = 1, PixelSizeMicrons = 1, FocuserStepMicrons = 1,
            Baseline = new TiltGradient(0, 0, 1000),
            AllInward = new TiltGradient(20, 0, 1075),  // 20-unit spurious tilt change on a piston move
            ReBaseline1 = new TiltGradient(0, 0, 1000),
            Screw1 = new TiltGradient(10, 0, 1000),
            ReBaseline2 = new TiltGradient(0, 0, 1000),
            Screw2 = new TiltGradient(0, 10, 1000),
        };
        var c = TiltCalibrationCalculator.ComputeConfidence(inputs);
        Assert.Multiple(() => {
            Assert.That(c.AllInwardTiltResidual, Is.EqualTo(20).Within(1e-9));
            Assert.That(c.NoiseEstimate, Is.EqualTo(Math.Sqrt(400.0 / 3.0)).Within(1e-9));
            Assert.That(c.SignalToNoise, Is.LessThan(TiltCalibrationCalculator.MinReliableSignalToNoise));
            Assert.That(c.IsReliable, Is.False);
        });
    }

    [Test]
    public void ComputeConfidence_RealAstrodet6Run_IsNoiseDominated() {
        // Regression lock on the real astrodet_6 calibration (D:\Tilt Calibration Bank\astrodet_6\...115023):
        // the per-step tilt vectors give SNR ~0.81 and ~51° predicted screw-direction error -> not reliable.
        // Geometry: the astrodet rig's real camera/focuser (ASI2600 6248x4176 @ 3.76 µm, 3.6 µm focuser step —
        // see docs/tilt-calibration-error-bounds-design.md's astrodet_6_2 companion run and the same values used
        // elsewhere in this fixture for this rig family).
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            ImageWidthPixels = 6248, ImageHeightPixels = 4176, PixelSizeMicrons = 3.76, FocuserStepMicrons = 3.6,
            Baseline = new TiltGradient(-2.1105835080420547, 24.121199286798387, 591.0128980765176),
            AllInward = new TiltGradient(8.482652443992663, 6.478563541214388, 498.59261745105937),
            ReBaseline1 = new TiltGradient(3.575182052031437, 6.397250940705116, 599.5753346049264),
            Screw1 = new TiltGradient(7.026796200619742, -7.090700037203773, 566.0698145625257),
            ReBaseline2 = new TiltGradient(6.01584663828578, 16.457907779687257, 609.7793117669113),
            Screw2 = new TiltGradient(19.615438422209536, 13.583617294890002, 591.3416298243283),
        };
        var c = TiltCalibrationCalculator.ComputeConfidence(inputs);
        Assert.Multiple(() => {
            // physical-space values (F2 fix): the old (A,B)-space pins (13.91 / 17.10 / 0.81 / 50.9°) were
            // computed on an implicitly-unit sensor; the real 6248x4176 anisotropy shifts SNR from 0.81 to
            // ~0.71 and the angle uncertainty from 50.9° to ~54.5° — still solidly noise-dominated/unreliable.
            // Drift-cancelling screw-1 reference (F3, this test): ScrewMoveSignal is the mean of the two
            // single-screw move magnitudes, and screw 1's move is now referenced to mid(ReBaseline1,
            // ReBaseline2) instead of ReBaseline1 alone; since RB1 != RB2 in this real run, the signal (and
            // everything derived from it) shifts from 0.0026615 -> ~0.0032226. NoiseEstimate is untouched (the
            // noise probes -- all-inward residual and both re-baseline drifts -- don't involve the screw-move
            // reference). Still solidly noise-dominated/unreliable either way.
            Assert.That(c.ScrewMoveSignal, Is.EqualTo(0.0032226).Within(0.00005));
            Assert.That(c.NoiseEstimate, Is.EqualTo(0.0037296).Within(0.00005));
            Assert.That(c.SignalToNoise, Is.EqualTo(0.8641).Within(0.01));
            Assert.That(c.PredictedAngleUncertaintyDeg, Is.EqualTo(49.17).Within(0.5));
            Assert.That(c.IsReliable, Is.False);
        });
    }

    [Test]
    public void Calibrate_PopulatesConfidence() {
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            Baseline = new TiltGradient(0, 0, 1000),
            AllInward = new TiltGradient(0, 0, 1075),
            ReBaseline1 = new TiltGradient(0, 0, 1000),
            Screw1 = SingleScrewReading(0, 400, 3, 1000),
            ReBaseline2 = new TiltGradient(0, 0, 1000),
            Screw2 = SingleScrewReading(120, 400, 3, 1000),
            ImageWidthPixels = ImgW,
            ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize,
            FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm,
            CalibrationAppliedAmount = 1.0,
        };
        var r = TiltCalibrationCalculator.Calibrate(inputs);
        Assert.That(r.Confidence, Is.Not.Null);
        Assert.That(r.Confidence.IsReliable, Is.True); // clean synthetic moves, zero drift
    }

    [Test]
    public void Calibrate_WithoutCurvatureMeasurement_PassesFallbackSignThrough() {
        // 4-step run: Baseline/AllInward were never measured. The baseline reading is supplied in
        // the ReBaseline1 slot (it is the screw-1 reference); Baseline/AllInward stay default.
        var baseline = new TiltGradient(0, 0, 1000);
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            HasCurvatureMeasurement = false,
            FallbackCurvatureSign = -1,
            ReBaseline1 = baseline,
            Screw1 = SingleScrewReading(0, 400, 3),
            ReBaseline2 = baseline,
            Screw2 = SingleScrewReading(120, 400, 3),
            ImageWidthPixels = ImgW,
            ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize,
            FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm,
            CalibrationAppliedAmount = 1.0,
            IsStepperAdjustment = false
        };
        var result = TiltCalibrationCalculator.Calibrate(inputs);
        Assert.Multiple(() => {
            Assert.That(result.CurvatureSign, Is.EqualTo(-1));
            Assert.That(result.Screw1AngleDegrees, Is.EqualTo(0).Within(1e-6));
            Assert.That(result.Screw2AngleDegrees, Is.EqualTo(120).Within(1e-6));
            Assert.That(result.MeasuredHardwareMicrons, Is.EqualTo(400).Within(1e-6));
        });
    }

    [Test]
    public void ComputeConfidence_WithoutCurvatureMeasurement_UsesOnlyRebaseline2Drift() {
        var baseline = new TiltGradient(0, 0, 1000);
        var drifted = new TiltGradient(0.01, 0, 1000); // ReBaseline2 drifts by 0.01 from ReBaseline1
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            // Deliberately isotropic unit geometry (see ComputeConfidence_CleanMeasurement_HighSnrAndReliable)
            // so PhysicalDelta is byte-identical to the raw (A,B) delta.
            ImageWidthPixels = 1, ImageHeightPixels = 1, PixelSizeMicrons = 1, FocuserStepMicrons = 1,
            HasCurvatureMeasurement = false,
            ReBaseline1 = baseline,
            Screw1 = new TiltGradient(0.10, 0, 1000),
            ReBaseline2 = drifted,
            Screw2 = new TiltGradient(0.01, 0.10, 1000)
        };
        var confidence = TiltCalibrationCalculator.ComputeConfidence(inputs);
        Assert.Multiple(() => {
            // Screw 1's move is referenced to mid(ReBaseline1, ReBaseline2) = mid((0,0), (0.01,0)) = (0.005,0):
            // |Screw1 - mid| = |(0.095,0)| = 0.095. Screw 2 has no HasFinalRebaseline, so it's still referenced
            // to ReBaseline2 alone: |Screw2 - ReBaseline2| = |(0,0.10)| = 0.10. signal = mean(0.095, 0.10) =
            // 0.0975; noise = |ReBaseline2 - ReBaseline1| = 0.01 (unaffected -- it's a noise probe, not a
            // screw-move delta).
            Assert.That(confidence.ScrewMoveSignal, Is.EqualTo(0.0975).Within(1e-9));
            Assert.That(confidence.NoiseEstimate, Is.EqualTo(0.01).Within(1e-9));
            Assert.That(confidence.SignalToNoise, Is.EqualTo(9.75).Within(1e-9));
            Assert.That(confidence.AllInwardTiltResidual, Is.NaN);
            Assert.That(confidence.Rebaseline1Drift, Is.NaN);
            Assert.That(confidence.Rebaseline2Drift, Is.EqualTo(0.01).Within(1e-9));
            Assert.That(confidence.IsReliable, Is.True);
        });
    }

    [Test]
    public void PistonImpliedMicronsPerStep_DriftCorrectedFromBracketingBaselines() {
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 4, HasCurvatureMeasurement = true,
            Baseline = new TiltGradient(0, 0, 11283.868653107538),
            AllInward = new TiltGradient(0, 0, 9995.476157148303),
            ReBaseline1 = new TiltGradient(0, 0, 11197.706101225354),
            FocuserStepMicrons = 0.269, CalibrationAppliedAmount = 150.0,
            ImageWidthPixels = 9576, ImageHeightPixels = 6388, PixelSizeMicrons = 3.76,
            ScrewRadiusMillimeters = 55.0,
        };
        Assert.That(TiltCalibrationCalculator.PistonImpliedMicronsPerStep(inputs),
            Is.EqualTo(2.233258).Within(1e-5));
    }

    [Test]
    public void PistonImpliedMicronsPerStep_NaNWithoutCurvatureSteps() {
        var inputs = new TiltCalibrationInputs { HasCurvatureMeasurement = false,
            FocuserStepMicrons = 0.269, CalibrationAppliedAmount = 150.0 };
        Assert.That(TiltCalibrationCalculator.PistonImpliedMicronsPerStep(inputs), Is.NaN);
    }

    // Mirrors RecoverHardwareMicrons_ReturnsNaN_OnNonPositiveInputs' TestCase shape for this method's other
    // two guard branches (CalibrationAppliedAmount and FocuserStepMicrons); HasCurvatureMeasurement's guard
    // is already covered above.
    [TestCase(0.0, 0.269)]     // CalibrationAppliedAmount <= 0
    [TestCase(-150.0, 0.269)]
    [TestCase(150.0, 0.0)]     // FocuserStepMicrons <= 0
    [TestCase(150.0, -0.269)]
    public void PistonImpliedMicronsPerStep_NaN_OnNonPositiveApplicationOrStepSize(
        double calibrationAppliedAmount, double focuserStepMicrons) {
        var inputs = new TiltCalibrationInputs {
            HasCurvatureMeasurement = true,
            Baseline = new TiltGradient(0, 0, 11283.868653107538),
            AllInward = new TiltGradient(0, 0, 9995.476157148303),
            ReBaseline1 = new TiltGradient(0, 0, 11197.706101225354),
            FocuserStepMicrons = focuserStepMicrons, CalibrationAppliedAmount = calibrationAppliedAmount,
        };
        Assert.That(TiltCalibrationCalculator.PistonImpliedMicronsPerStep(inputs), Is.NaN);
    }

    [TestCase(30.0, true, 3, 30.0, 150.0, 270.0, double.NaN)]
    [TestCase(30.0, false, 3, 30.0, 270.0, 150.0, double.NaN)]
    [TestCase(350.0, true, 4, 350.0, 80.0, 170.0, 260.0)]
    [TestCase(10.0, false, 4, 10.0, 280.0, 190.0, 100.0)]
    [TestCase(-30.0, true, 3, 330.0, 90.0, 210.0, double.NaN)]  // out-of-range input normalized into [0, 360)
    [TestCase(370.0, true, 3, 10.0, 130.0, 250.0, double.NaN)]
    public void ComputeManualScrewAngles_PlacesEqualSpacingWithWinding(
        double screw1, bool clockwise, int screwCount, double e1, double e2, double e3, double e4) {
        var (s1, s2, s3, s4) = TiltCalibrationCalculator.ComputeManualScrewAngles(screw1, clockwise, screwCount);
        Assert.Multiple(() => {
            Assert.That(s1, Is.EqualTo(e1).Within(1e-9));
            Assert.That(s2, Is.EqualTo(e2).Within(1e-9));
            Assert.That(s3, Is.EqualTo(e3).Within(1e-9));
            if (double.IsNaN(e4)) Assert.That(s4, Is.NaN);
            else Assert.That(s4, Is.EqualTo(e4).Within(1e-9));
        });
    }
}
