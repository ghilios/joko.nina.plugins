using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard {

    [TestFixture]
    public class TiltCalibrationMetadataTests {

        [Test]
        public void StepOrder_HasSixDiscreteSteps() {
            Assert.That(TiltCalibrationMetadata.StepOrder,
                Is.EqualTo(new[] { "Baseline", "AllInward", "ReBaseline1", "Screw1", "ReBaseline2", "Screw2" }));
        }

        [Test]
        public void Serialize_RoundTripsAllFields() {
            var md = new TiltCalibrationMetadata {
                NumberOfScrews = 4,
                AdjustmentType = "StepperMotors",
                StepperStepSizeMicrons = 1.25,
                ScrewRadiusMillimeters = 44.0,
                PixelSizeMicrons = 3.76,
                FocuserStepSizeMicrons = 3.58,
                CalibrationAppliedAmount = 2.0,
                MeasurementAverageCount = 3,
                RunStepMapping = new List<TiltRunStepMapping> {
                    new TiltRunStepMapping { Step = "Baseline", Folder = @"C:\runs\01_Baseline\AutoFocus_x" }
                },
                PerStep = new List<TiltPerStepResult> {
                    new TiltPerStepResult {
                        Step = "Baseline", TiltPlaneA = 1.1, TiltPlaneB = -2.2, MeanFocuserPosition = 1000,
                        TiltAngleDeg = 0.5, DirectionDeg = 153.4,
                        CurvatureRadiusMillimeters = 1234.5, CurvatureEffectMicronsAtScrewRadius = 5.5,
                        CornerTiltPlaneA = 1.05, CornerTiltPlaneB = -2.35, CornerMeanFocuserPosition = 999.4
                    }
                },
                Calibration = new TiltCalibrationResultRecord {
                    Screw1AngleDegrees = 10, Screw2AngleDegrees = 100, CurvatureSign = -1, MeasuredHardwareMicrons = 400,
                    CornerMeasuredHardwareMicrons = 410.5, EstimatorRelativeDifference = 0.0909
                }
            };

            var back = TiltCalibrationMetadata.Deserialize(md.Serialize());

            Assert.Multiple(() => {
                Assert.That(back.SchemaVersion, Is.EqualTo(TiltCalibrationMetadata.CurrentSchemaVersion));
                Assert.That(back.NumberOfScrews, Is.EqualTo(4));
                Assert.That(back.IsStepperAdjustment, Is.True);
                Assert.That(back.StepperStepSizeMicrons, Is.EqualTo(1.25).Within(1e-9));
                Assert.That(back.CalibrationAppliedAmount, Is.EqualTo(2.0).Within(1e-9));
                Assert.That(back.MeasurementAverageCount, Is.EqualTo(3));
                Assert.That(back.RunStepMapping[0].Folder, Is.EqualTo(@"C:\runs\01_Baseline\AutoFocus_x"));
                Assert.That(back.PerStep[0].TiltPlaneB, Is.EqualTo(-2.2).Within(1e-9));
                Assert.That(back.PerStep[0].CurvatureRadiusMillimeters, Is.EqualTo(1234.5).Within(1e-9));
                Assert.That(back.PerStep[0].CurvatureEffectMicronsAtScrewRadius, Is.EqualTo(5.5).Within(1e-9));
                Assert.That(back.PerStep[0].CornerTiltPlaneA, Is.EqualTo(1.05).Within(1e-9));
                Assert.That(back.PerStep[0].CornerTiltPlaneB, Is.EqualTo(-2.35).Within(1e-9));
                Assert.That(back.PerStep[0].CornerMeanFocuserPosition, Is.EqualTo(999.4).Within(1e-9));
                Assert.That(back.Calibration.MeasuredHardwareMicrons, Is.EqualTo(400).Within(1e-9));
                Assert.That(back.Calibration.CurvatureSign, Is.EqualTo(-1));
                Assert.That(back.Calibration.CornerMeasuredHardwareMicrons, Is.EqualTo(410.5).Within(1e-9));
                Assert.That(back.Calibration.EstimatorRelativeDifference, Is.EqualTo(0.0909).Within(1e-9));
            });
        }

        [Test]
        public void ResultRecord_RoundTripsConfidenceFields() {
            var meta = new TiltCalibrationMetadata {
                NumberOfScrews = 3, ScrewRadiusMillimeters = 44, PixelSizeMicrons = 3.76,
                FocuserStepSizeMicrons = 3.6, CalibrationAppliedAmount = 1.0,
                Calibration = new TiltCalibrationResultRecord {
                    Screw1AngleDegrees = 67.1, SignalToNoise = 2.16,
                    PredictedAngleUncertaintyDeg = 24.9, PitchUncertaintyMicrons = 43.0, ConfidenceIsReliable = true,
                    PistonImpliedMicronsPerStep = 2.233258
                }
            };
            var back = TiltCalibrationMetadata.Deserialize(meta.Serialize());
            Assert.Multiple(() => {
                Assert.That(back.Calibration.SignalToNoise, Is.EqualTo(2.16).Within(1e-9));
                Assert.That(back.Calibration.PredictedAngleUncertaintyDeg, Is.EqualTo(24.9).Within(1e-9));
                Assert.That(back.Calibration.PitchUncertaintyMicrons, Is.EqualTo(43.0).Within(1e-9));
                Assert.That(back.Calibration.ConfidenceIsReliable, Is.True);
                Assert.That(back.Calibration.PistonImpliedMicronsPerStep, Is.EqualTo(2.233258).Within(1e-9));
            });
        }

        [Test]
        public void MeasurementContext_RoundTrips() {
            var meta = new TiltCalibrationMetadata {
                NumberOfScrews = 3, ScrewRadiusMillimeters = 44, PixelSizeMicrons = 3.76,
                FocuserStepSizeMicrons = 3.6, CalibrationAppliedAmount = 1.0,
                MeasurementContext = new TiltMeasurementContext {
                    MicronsPerFocuserStep = 3.6, FocalRatio = 7, FocalLengthMm = 703,
                    UseRANSAC = true, FixedSensorCenter = false, AstigmaticCurvatureEnabled = false,
                    AcceptableRSquaredMin = 0.8, WeightedHyperbolicFitEnabled = true,
                    MaxOutlierRejections = 3, OutlierRejectionConfidence = 0.9,
                    HyperbolicFitModel = "Hybrid", SensorROI = 1.0, CornersROI = 1.0
                }
            };
            var back = TiltCalibrationMetadata.Deserialize(meta.Serialize());
            Assert.Multiple(() => {
                Assert.That(back.MeasurementContext.MicronsPerFocuserStep, Is.EqualTo(3.6).Within(1e-9));
                Assert.That(back.MeasurementContext.FocalRatio, Is.EqualTo(7).Within(1e-9));
                Assert.That(back.MeasurementContext.HyperbolicFitModel, Is.EqualTo("Hybrid"));
                Assert.That(back.MeasurementContext.UseRANSAC, Is.True);
            });
        }

        [Test]
        public void IsStepperAdjustment_FalseForScrews() {
            var md = new TiltCalibrationMetadata { AdjustmentType = "Screws" };
            Assert.That(md.IsStepperAdjustment, Is.False);
        }

        [Test]
        public void ExpectedPositionAngleScrew1Deg_DefaultsToNaN_AndRoundTrips() {
            // NaN signals "not provided" (a wizard-written file) so the headless validator reports the angle as
            // n/a instead of failing it against a bogus 0° expectation.
            var md = new TiltCalibrationMetadata();
            Assert.That(double.IsNaN(md.ExpectedPositionAngleScrew1Deg), Is.True);
            var back = TiltCalibrationMetadata.Deserialize(md.Serialize());
            Assert.That(double.IsNaN(back.ExpectedPositionAngleScrew1Deg), Is.True);
        }

        [TestCase(5)]
        [TestCase(2)]
        public void Validate_RejectsBadScrewCount(int screwCount) {
            var md = new TiltCalibrationMetadata {
                NumberOfScrews = screwCount, PixelSizeMicrons = 1, FocuserStepSizeMicrons = 1, ScrewRadiusMillimeters = 1
            };
            Assert.That(() => md.Validate(), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Validate_PassesForWellFormedMetadata() {
            var md = new TiltCalibrationMetadata {
                NumberOfScrews = 3, PixelSizeMicrons = 3.76, FocuserStepSizeMicrons = 3.58,
                ScrewRadiusMillimeters = 44.0, CalibrationAppliedAmount = 1.0
            };
            Assert.That(() => md.Validate(), Throws.Nothing);
        }

        [Test]
        public void StepFolder_RelativizesAndResolvesRoundTrip() {
            // A step folder under the run root is stored relative, then resolves back to the same absolute path.
            var runRoot = @"C:\TiltCalibration\TiltCalibration_20260621_214304";
            var absolute = @"C:\TiltCalibration\TiltCalibration_20260621_214304\01_Baseline\AutoFocus_20260621_214312";

            var relative = TiltCalibrationMetadata.ToRelativeStepFolder(runRoot, absolute);
            Assert.That(relative, Is.EqualTo(@"01_Baseline\AutoFocus_20260621_214312"));
            Assert.That(TiltCalibrationMetadata.ResolveStepFolder(runRoot, relative), Is.EqualTo(absolute));
        }

        [Test]
        public void ResolveStepFolder_RebasesRelativeOntoSelectedRunRoot() {
            // The whole point: a run captured under one path replays from another — the relative folder follows
            // the run directory the user actually selected.
            var resolved = TiltCalibrationMetadata.ResolveStepFolder(
                @"D:\Moved\TiltCalibration_20260621_214304", @"01_Baseline\AutoFocus_20260621_214312");
            Assert.That(resolved,
                Is.EqualTo(@"D:\Moved\TiltCalibration_20260621_214304\01_Baseline\AutoFocus_20260621_214312"));
        }

        [Test]
        public void ResolveStepFolder_HonorsLegacyAbsolutePaths() {
            // Pre-relative files stored absolute capture-machine paths; honor them as-is (no rebasing).
            var absolute = @"C:\Users\someone\Desktop\runs\01_Baseline\AutoFocus_x";
            Assert.That(TiltCalibrationMetadata.ResolveStepFolder(@"C:\Elsewhere", absolute), Is.EqualTo(absolute));
        }

        [Test]
        public void ToRelativeStepFolder_KeepsAbsoluteWhenNotUnderRunRoot() {
            // A folder on a different volume can't be made relative; keep it absolute rather than emit "..\..".
            var other = @"D:\external\01_Baseline\AutoFocus_x";
            Assert.That(TiltCalibrationMetadata.ToRelativeStepFolder(@"C:\runs\run1", other), Is.EqualTo(other));
        }

        [Test]
        public void StarDetectionSnapshot_RoundTrips() {
            var meta = new TiltCalibrationMetadata {
                NumberOfScrews = 3, ScrewRadiusMillimeters = 44, PixelSizeMicrons = 3.76,
                FocuserStepSizeMicrons = 3.6, CalibrationAppliedAmount = 1.0,
                StarDetectionSnapshot = new StarDetectionSettingsSnapshot {
                    BrightnessSensitivity = 42.5, LocallyAdaptiveBinarization = true, AdaptiveNoiseBlockSize = 256
                }
            };
            var back = TiltCalibrationMetadata.Deserialize(meta.Serialize());
            Assert.That(back.StarDetectionSnapshot, Is.Not.Null);
            Assert.Multiple(() => {
                Assert.That(back.StarDetectionSnapshot.BrightnessSensitivity, Is.EqualTo(42.5).Within(1e-9));
                Assert.That(back.StarDetectionSnapshot.LocallyAdaptiveBinarization, Is.True);
                Assert.That(back.StarDetectionSnapshot.AdaptiveNoiseBlockSize, Is.EqualTo(256));
            });
        }
    }
}
