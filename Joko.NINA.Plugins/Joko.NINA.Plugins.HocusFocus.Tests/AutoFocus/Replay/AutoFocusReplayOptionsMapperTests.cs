using System;
using System.Collections.Generic;
using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus.Replay {

    [TestFixture]
    public class AutoFocusReplayOptionsMapperTests {

        private static AutoFocusOptionsSnapshot SampleSnapshot() => new AutoFocusOptionsSnapshot() {
            DebayerImage = true,
            NumberOfAFStars = 33,
            TotalNumberOfAttempts = 4,
            ValidateHfrImprovement = true,
            AutoFocusMethod = AFMethodEnum.STARHFR,
            AutoFocusCurveFitting = AFCurveFittingEnum.TRENDHYPERBOLIC,
            AutoFocusInitialOffsetSteps = 7,
            FramesPerPoint = 3,
            HFRImprovementThreshold = 0.2,
            FocuserOffset = 5,
            MaxOutlierRejections = 2,
            OutlierRejectionConfidence = 0.95,
            WeightedHyperbolicFitEnabled = true,
            HyperbolicFitModel = HyperbolicFitModel.SmoothBlend,
            FitRejectionCriterion = FitRejectionCriterion.ReducedChiSquared,
            ReducedChiSquaredRejectionThreshold = 3.3
        };

        private static AutoFocusReplayMetadata SampleMetadata() => new AutoFocusReplayMetadata() {
            StarDetection = new StarDetectionSettingsSnapshot() { BrightnessSensitivity = 4.2 },
            AutoFocus = SampleSnapshot(),
            Regions = new ReplayRegionGeometry() {
                IsInspectorRun = true,
                SensorCurveModelEnabled = true,
                Regions = new List<StarDetectionRegion>() { StarDetectionRegion.Full }
            }
        };

        [Test]
        public void Apply_CopiesFitFields_AndLeavesStepSizeAndLiveCaptureUntouched() {
            var options = new AutoFocusEngineOptions() {
                AutoFocusStepSize = 111,
                Save = true,
                SavePath = "X:/run",
                MaxConcurrent = 9
            };
            AutoFocusReplayOptionsMapper.Apply(options, SampleSnapshot());

            Assert.Multiple(() => {
                Assert.That(options.NumberOfAFStars, Is.EqualTo(33));
                Assert.That(options.AutoFocusCurveFitting, Is.EqualTo(AFCurveFittingEnum.TRENDHYPERBOLIC));
                Assert.That(options.HyperbolicFitModel, Is.EqualTo(HyperbolicFitModel.SmoothBlend));
                Assert.That(options.ReducedChiSquaredRejectionThreshold, Is.EqualTo(3.3));
                // Untouched
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(111));
                Assert.That(options.Save, Is.True);
                Assert.That(options.SavePath, Is.EqualTo("X:/run"));
                Assert.That(options.MaxConcurrent, Is.EqualTo(9));
                Assert.That(options.StarDetectionOptionsOverride, Is.Null);
            });
        }

        [Test]
        public void CaptureThenApply_RoundTripsEveryMappedField() {
            // Distinct, non-default values for every field Capture/Apply enumerate, so a field added to one method but
            // not the other (breaking capture-time replay fidelity) fails this test.
            var source = new AutoFocusEngineOptions() {
                DebayerImage = true,
                NumberOfAFStars = 21,
                TotalNumberOfAttempts = 4,
                ValidateHfrImprovement = true,
                AutoFocusMethod = AFMethodEnum.STARHFR,
                AutoFocusCurveFitting = AFCurveFittingEnum.TRENDHYPERBOLIC,
                AutoFocusInitialOffsetSteps = 9,
                FramesPerPoint = 3,
                HFRImprovementThreshold = 0.17,
                FocuserOffset = -4,
                MaxOutlierRejections = 2,
                OutlierRejectionConfidence = 0.93,
                WeightedHyperbolicFitEnabled = true,
                HyperbolicFitModel = HyperbolicFitModel.UnevenBlend,
                FitRejectionCriterion = FitRejectionCriterion.ReducedChiSquared,
                ReducedChiSquaredRejectionThreshold = 2.7
            };
            var snapshot = AutoFocusReplayOptionsMapper.Capture(source);
            var target = new AutoFocusEngineOptions();
            AutoFocusReplayOptionsMapper.Apply(target, snapshot);

            Assert.Multiple(() => {
                Assert.That(target.DebayerImage, Is.EqualTo(true));
                Assert.That(target.NumberOfAFStars, Is.EqualTo(21));
                Assert.That(target.TotalNumberOfAttempts, Is.EqualTo(4));
                Assert.That(target.ValidateHfrImprovement, Is.EqualTo(true));
                Assert.That(target.AutoFocusMethod, Is.EqualTo(AFMethodEnum.STARHFR));
                Assert.That(target.AutoFocusCurveFitting, Is.EqualTo(AFCurveFittingEnum.TRENDHYPERBOLIC));
                Assert.That(target.AutoFocusInitialOffsetSteps, Is.EqualTo(9));
                Assert.That(target.FramesPerPoint, Is.EqualTo(3));
                Assert.That(target.HFRImprovementThreshold, Is.EqualTo(0.17));
                Assert.That(target.FocuserOffset, Is.EqualTo(-4));
                Assert.That(target.MaxOutlierRejections, Is.EqualTo(2));
                Assert.That(target.OutlierRejectionConfidence, Is.EqualTo(0.93));
                Assert.That(target.WeightedHyperbolicFitEnabled, Is.EqualTo(true));
                Assert.That(target.HyperbolicFitModel, Is.EqualTo(HyperbolicFitModel.UnevenBlend));
                Assert.That(target.FitRejectionCriterion, Is.EqualTo(FitRejectionCriterion.ReducedChiSquared));
                Assert.That(target.ReducedChiSquaredRejectionThreshold, Is.EqualTo(2.7));
            });
        }

        [Test]
        public void BuildReplayOptions_Cancel_ReturnsCancelled() {
            var result = AutoFocusReplayOptionsMapper.BuildReplayOptions(
                ReplaySettingsChoice.Cancel, () => new AutoFocusEngineOptions(), SampleMetadata(), () => { });
            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Options, Is.Null);
        }

        [Test]
        public void BuildReplayOptions_UseCurrent_ReturnsBaseWithoutOverride() {
            var result = AutoFocusReplayOptionsMapper.BuildReplayOptions(
                ReplaySettingsChoice.UseCurrentSettings, () => new AutoFocusEngineOptions() { NumberOfAFStars = 7 }, SampleMetadata(), () => { });
            Assert.Multiple(() => {
                Assert.That(result.Cancelled, Is.False);
                Assert.That(result.Options.NumberOfAFStars, Is.EqualTo(7));
                Assert.That(result.Options.StarDetectionOptionsOverride, Is.Null);
                Assert.That(result.CaptureTimeRegions, Is.Null);
            });
        }

        [Test]
        public void BuildReplayOptions_InMemory_SetsOverride_MapsAf_AndCarriesRegions() {
            var metadata = SampleMetadata();
            var result = AutoFocusReplayOptionsMapper.BuildReplayOptions(
                ReplaySettingsChoice.UseCaptureTimeSettingsInMemory, () => new AutoFocusEngineOptions(), metadata, () => { });

            Assert.Multiple(() => {
                Assert.That(result.Options.StarDetectionOptionsOverride, Is.SameAs(metadata.StarDetection));
                Assert.That(result.Options.NumberOfAFStars, Is.EqualTo(33));
                Assert.That(result.Options.HyperbolicFitModel, Is.EqualTo(HyperbolicFitModel.SmoothBlend));
                Assert.That(result.CaptureTimeRegions, Is.SameAs(metadata.Regions.Regions));
            });
        }

        [Test]
        public void BuildReplayOptions_UpdateProfile_AppliesBeforeBuilding_AndNoOverride() {
            bool applied = false;
            Func<AutoFocusEngineOptions> build = () => new AutoFocusEngineOptions() {
                // Encodes whether applyToProfile already ran when the base options were built.
                MaxConcurrent = applied ? 99 : 1
            };
            var result = AutoFocusReplayOptionsMapper.BuildReplayOptions(
                ReplaySettingsChoice.UpdateProfileToCaptureTime, build, SampleMetadata(), () => applied = true);

            Assert.Multiple(() => {
                Assert.That(applied, Is.True);
                Assert.That(result.Options.MaxConcurrent, Is.EqualTo(99), "base options must be built AFTER the profile is updated");
                Assert.That(result.Options.StarDetectionOptionsOverride, Is.Null);
                Assert.That(result.CaptureTimeRegions, Is.Null);
            });
        }
    }
}
