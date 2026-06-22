using System;
using System.Collections.Generic;
using System.IO;
using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus.Replay {

    [TestFixture]
    public class AutoFocusReplayMetadataTests {

        private static AutoFocusReplayMetadata BuildPopulated() {
            return new AutoFocusReplayMetadata() {
                SchemaVersion = AutoFocusReplayMetadata.CurrentSchemaVersion,
                CreatedAtUtc = new DateTime(2026, 6, 22, 1, 2, 3, DateTimeKind.Utc),
                PluginVersion = "3.0.0.99",
                StarDetectorVersion = 1,
                StarDetection = new StarDetectionSettingsSnapshot() {
                    UseAdvanced = true,
                    BrightnessSensitivity = 7.25,
                    StructureLayers = 5,
                    MaxDistortion = 0.42,
                    MinHFR = 1.1,
                    StarPeakResponse = 0.8,
                    NoiseReductionRadius = 4,
                    PSFFitType = StarDetectorPSFFitType.Gaussian,
                    MeasurementAverage = MeasurementAverageEnum.MeanOutliers,
                    DefocusAwareDonutDetection = true,
                    DonutMorphCloseSize = 7,
                    SaturationThreshold = 0.95
                },
                AutoFocus = new AutoFocusOptionsSnapshot() {
                    DebayerImage = true,
                    NumberOfAFStars = 42,
                    TotalNumberOfAttempts = 3,
                    ValidateHfrImprovement = true,
                    AutoFocusMethod = AFMethodEnum.STARHFR,
                    AutoFocusCurveFitting = AFCurveFittingEnum.HYPERBOLIC,
                    AutoFocusInitialOffsetSteps = 6,
                    FramesPerPoint = 2,
                    HFRImprovementThreshold = 0.15,
                    FocuserOffset = -3,
                    MaxOutlierRejections = 1,
                    OutlierRejectionConfidence = 0.9,
                    WeightedHyperbolicFitEnabled = true,
                    HyperbolicFitModel = HyperbolicFitModel.UnevenBlend,
                    FitRejectionCriterion = FitRejectionCriterion.ReducedChiSquared,
                    ReducedChiSquaredRejectionThreshold = 4.5
                },
                Regions = new ReplayRegionGeometry() {
                    AutoFocusInnerCropRatio = 0.7,
                    AutoFocusOuterCropRatio = 1.0,
                    IsInspectorRun = true,
                    SensorROI = 0.9,
                    CornersROI = 0.8,
                    SensorCurveModelEnabled = true,
                    Regions = new List<StarDetectionRegion>() {
                        StarDetectionRegion.Full,
                        new StarDetectionRegion(new RatioRect(0.1, 0.15, 0.5, 0.6), index: 3)
                    }
                },
                Results = new List<ReplayRegionResultSummary>() {
                    new ReplayRegionResultSummary() {
                        RegionIndex = 0,
                        EstimatedFinalFocuserPosition = 12345.6,
                        EstimatedFinalHFR = 2.34,
                        FinalHFR = 2.4,
                        InitialHFR = 5.6,
                        RSquared = 0.987,
                        SelectedHyperbolicFitModel = HyperbolicFitModel.Symmetric
                    }
                }
            };
        }

        [Test]
        public void RoundTrips_WithoutTypeNameHandling() {
            var original = BuildPopulated();
            var json = original.Serialize();

            // No polymorphic $type tokens — proves the DTO round-trips with concrete types only.
            Assert.That(json, Does.Not.Contain("$type"));

            var restored = AutoFocusReplayMetadata.Deserialize(json);

            Assert.Multiple(() => {
                Assert.That(restored.SchemaVersion, Is.EqualTo(original.SchemaVersion));
                Assert.That(restored.CreatedAtUtc, Is.EqualTo(original.CreatedAtUtc));
                Assert.That(restored.PluginVersion, Is.EqualTo("3.0.0.99"));
                Assert.That(restored.StarDetectorVersion, Is.EqualTo(1));

                // Star detection snapshot
                Assert.That(restored.StarDetection.BrightnessSensitivity, Is.EqualTo(7.25));
                Assert.That(restored.StarDetection.StructureLayers, Is.EqualTo(5));
                Assert.That(restored.StarDetection.MaxDistortion, Is.EqualTo(0.42));
                Assert.That(restored.StarDetection.PSFFitType, Is.EqualTo(StarDetectorPSFFitType.Gaussian));
                Assert.That(restored.StarDetection.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.MeanOutliers));
                Assert.That(restored.StarDetection.DefocusAwareDonutDetection, Is.True);
                Assert.That(restored.StarDetection.DonutMorphCloseSize, Is.EqualTo(7));

                // AutoFocus snapshot
                Assert.That(restored.AutoFocus.NumberOfAFStars, Is.EqualTo(42));
                Assert.That(restored.AutoFocus.AutoFocusMethod, Is.EqualTo(AFMethodEnum.STARHFR));
                Assert.That(restored.AutoFocus.AutoFocusCurveFitting, Is.EqualTo(AFCurveFittingEnum.HYPERBOLIC));
                Assert.That(restored.AutoFocus.HyperbolicFitModel, Is.EqualTo(HyperbolicFitModel.UnevenBlend));
                Assert.That(restored.AutoFocus.FitRejectionCriterion, Is.EqualTo(FitRejectionCriterion.ReducedChiSquared));
                Assert.That(restored.AutoFocus.ReducedChiSquaredRejectionThreshold, Is.EqualTo(4.5));

                // Region geometry (incl. StarDetectionRegion / RatioRect)
                Assert.That(restored.Regions.IsInspectorRun, Is.True);
                Assert.That(restored.Regions.SensorROI, Is.EqualTo(0.9));
                Assert.That(restored.Regions.SensorCurveModelEnabled, Is.True);
                Assert.That(restored.Regions.Regions, Has.Count.EqualTo(2));
                Assert.That(restored.Regions.Regions[0].IsFull(), Is.True);
                Assert.That(restored.Regions.Regions[1].Index, Is.EqualTo(3));
                Assert.That(restored.Regions.Regions[1].OuterBoundary.StartX, Is.EqualTo(0.1));
                Assert.That(restored.Regions.Regions[1].OuterBoundary.Width, Is.EqualTo(0.5));

                // Result summary
                Assert.That(restored.Results, Has.Count.EqualTo(1));
                Assert.That(restored.Results[0].RSquared, Is.EqualTo(0.987));
                Assert.That(restored.Results[0].SelectedHyperbolicFitModel, Is.EqualTo(HyperbolicFitModel.Symmetric));
            });
        }

        [Test]
        public void Validate_Throws_WhenStarDetectionMissing() {
            var metadata = BuildPopulated();
            metadata.StarDetection = null;
            Assert.Throws<InvalidOperationException>(() => metadata.Validate());
        }

        [Test]
        public void Validate_Throws_WhenAutoFocusMissing() {
            var metadata = BuildPopulated();
            metadata.AutoFocus = null;
            Assert.Throws<InvalidOperationException>(() => metadata.Validate());
        }

        [Test]
        public void TryLoad_FindsMetadataInParentRunRoot_WhenGivenAttemptFolder() {
            // metadata.json lives at the run root, but LoadSavedAutoFocusAttempt hands callers the attempt subfolder.
            var runRoot = Path.Combine(Path.GetTempPath(), "HFReplayTest_" + Guid.NewGuid().ToString("N"));
            var attemptFolder = Path.Combine(runRoot, "attempt01");
            Directory.CreateDirectory(attemptFolder);
            try {
                File.WriteAllText(Path.Combine(runRoot, "metadata.json"), BuildPopulated().Serialize());

                var ok = AutoFocusReplayMetadata.TryLoad(attemptFolder, out var metadata, out var error);

                Assert.Multiple(() => {
                    Assert.That(ok, Is.True);
                    Assert.That(error, Is.Null);
                    Assert.That(metadata, Is.Not.Null);
                    Assert.That(metadata.AutoFocus.NumberOfAFStars, Is.EqualTo(42));
                });
            } finally {
                Directory.Delete(runRoot, recursive: true);
            }
        }

        [Test]
        public void TryLoad_ReturnsFalseWithoutError_WhenMetadataAbsent() {
            var runRoot = Path.Combine(Path.GetTempPath(), "HFReplayTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runRoot);
            try {
                var ok = AutoFocusReplayMetadata.TryLoad(runRoot, out var metadata, out var error);
                Assert.Multiple(() => {
                    Assert.That(ok, Is.False);
                    Assert.That(error, Is.Null);
                    Assert.That(metadata, Is.Null);
                });
            } finally {
                Directory.Delete(runRoot, recursive: true);
            }
        }

        [Test]
        public void TryLoad_ReturnsFalseWithError_WhenMetadataCorrupt() {
            var runRoot = Path.Combine(Path.GetTempPath(), "HFReplayTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runRoot);
            try {
                File.WriteAllText(Path.Combine(runRoot, "metadata.json"), "{ not valid json ");
                var ok = AutoFocusReplayMetadata.TryLoad(runRoot, out var metadata, out var error);
                Assert.Multiple(() => {
                    Assert.That(ok, Is.False);
                    Assert.That(error, Is.Not.Null);
                    Assert.That(metadata, Is.Null);
                });
            } finally {
                Directory.Delete(runRoot, recursive: true);
            }
        }
    }
}
