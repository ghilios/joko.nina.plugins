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
                        CurvatureRadiusMillimeters = 1234.5, CurvatureEffectMicronsAtScrewRadius = 5.5
                    }
                },
                Calibration = new TiltCalibrationResultRecord {
                    Screw1AngleDegrees = 10, Screw2AngleDegrees = 100, CurvatureSign = -1, MeasuredHardwareMicrons = 400
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
                Assert.That(back.Calibration.MeasuredHardwareMicrons, Is.EqualTo(400).Within(1e-9));
                Assert.That(back.Calibration.CurvatureSign, Is.EqualTo(-1));
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
    }
}
