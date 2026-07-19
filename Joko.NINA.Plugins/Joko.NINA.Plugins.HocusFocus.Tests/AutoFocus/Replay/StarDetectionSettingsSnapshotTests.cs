using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus.Replay {

    [TestFixture]
    public class StarDetectionSettingsSnapshotTests {

        private static StarDetectionOptions BuildConfiguredOptions() {
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            // Advanced first so Simple-mode auto-config does not overwrite the assigned values.
            options.UseAdvanced = true;
            options.BrightnessSensitivity = 7.5;
            options.StructureLayers = 6;
            options.MaxDistortion = 0.4;
            options.MinHFR = 1.05;
            options.StarPeakResponse = 0.8;
            options.NoiseReductionRadius = 7;
            options.NoiseClippingMultiplier = 5.0;
            options.StarClippingMultiplier = 3.0;
            options.StarCenterTolerance = 0.5;
            options.HotpixelThreshold = 0.01;
            options.SaturationThreshold = 0.95;
            options.MinStarBoundingBoxSize = 6;
            options.PSFFitType = StarDetectorPSFFitType.Gaussian;
            options.DefocusAwareDonutDetection = true;
            options.DonutMorphCloseSize = 7;
            options.MeasurementAverage = MeasurementAverageEnum.MeanOutliers;
            return options;
        }

        [Test]
        public void FromOptions_CopiesEveryGetter() {
            var options = BuildConfiguredOptions();
            var snapshot = StarDetectionSettingsSnapshot.FromOptions(options);

            Assert.Multiple(() => {
                Assert.That(snapshot.UseAdvanced, Is.True);
                Assert.That(snapshot.BrightnessSensitivity, Is.EqualTo(7.5));
                Assert.That(snapshot.StructureLayers, Is.EqualTo(6));
                Assert.That(snapshot.MaxDistortion, Is.EqualTo(0.4));
                Assert.That(snapshot.MinHFR, Is.EqualTo(1.05));
                Assert.That(snapshot.StarPeakResponse, Is.EqualTo(0.8));
                Assert.That(snapshot.NoiseReductionRadius, Is.EqualTo(7));
                Assert.That(snapshot.NoiseClippingMultiplier, Is.EqualTo(5.0));
                Assert.That(snapshot.StarClippingMultiplier, Is.EqualTo(3.0));
                Assert.That(snapshot.StarCenterTolerance, Is.EqualTo(0.5));
                Assert.That(snapshot.HotpixelThreshold, Is.EqualTo(0.01));
                Assert.That(snapshot.SaturationThreshold, Is.EqualTo(0.95));
                Assert.That(snapshot.MinStarBoundingBoxSize, Is.EqualTo(6));
                Assert.That(snapshot.PSFFitType, Is.EqualTo(StarDetectorPSFFitType.Gaussian));
                Assert.That(snapshot.DefocusAwareDonutDetection, Is.True);
                Assert.That(snapshot.DonutMorphCloseSize, Is.EqualTo(7));
                Assert.That(snapshot.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.MeanOutliers));
            });
        }

        [Test]
        public void BuildStarDetectorParams_FromSnapshot_MatchesOriginalOptions() {
            // The core fidelity guarantee: a capture-time snapshot produces detection params identical to the live
            // options it was captured from, so an in-memory replay reproduces detection exactly.
            var options = BuildConfiguredOptions();
            var snapshot = StarDetectionSettingsSnapshot.FromOptions(options);

            var fromOptions = HocusFocusStarDetection.BuildStarDetectorParams(options);
            var fromSnapshot = HocusFocusStarDetection.BuildStarDetectorParams(snapshot);

            Assert.That(fromSnapshot.ToCanonicalCacheString(), Is.EqualTo(fromOptions.ToCanonicalCacheString()));
            Assert.Multiple(() => {
                Assert.That(fromSnapshot.Sensitivity, Is.EqualTo(fromOptions.Sensitivity));
                Assert.That(fromSnapshot.PeakResponse, Is.EqualTo(fromOptions.PeakResponse));
                Assert.That(fromSnapshot.MaxDistortion, Is.EqualTo(fromOptions.MaxDistortion));
                Assert.That(fromSnapshot.StructureLayers, Is.EqualTo(fromOptions.StructureLayers));
                Assert.That(fromSnapshot.MinHFR, Is.EqualTo(fromOptions.MinHFR));
                // The capture-time HFR-aggregation/outlier mode must flow through the override params (it is read at
                // the detect site from detectorParams, not the live options).
                Assert.That(fromSnapshot.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.MeanOutliers));
                Assert.That(fromSnapshot.MeasurementAverage, Is.EqualTo(fromOptions.MeasurementAverage));
            });
        }

        [Test]
        public void Clone_DeepCopiesIncludingOptimizedSettings() {
            var source = new StarDetectionSettingsSnapshot {
                UseAdvanced = true,
                BrightnessSensitivity = 7.5,
                StructureLayers = 6,
                IntermediateSavePath = @"C:\hf\debug",
                OptimizedSettings = new OptimizedStarDetectionSettings { BrightnessSensitivity = 3.3, StructureLayers = 7 }
            };

            var clone = source.Clone();
            // Mutate the source after cloning; the clone must be fully detached (incl. the nested DTO).
            source.BrightnessSensitivity = 99.0;
            source.OptimizedSettings.StructureLayers = 42;

            Assert.Multiple(() => {
                Assert.That(clone, Is.Not.SameAs(source));
                Assert.That(clone.UseAdvanced, Is.True);
                Assert.That(clone.BrightnessSensitivity, Is.EqualTo(7.5));
                Assert.That(clone.StructureLayers, Is.EqualTo(6));
                Assert.That(clone.IntermediateSavePath, Is.EqualTo(@"C:\hf\debug"));
                Assert.That(clone.OptimizedSettings, Is.Not.SameAs(source.OptimizedSettings));
                Assert.That(clone.OptimizedSettings.BrightnessSensitivity, Is.EqualTo(3.3));
                Assert.That(clone.OptimizedSettings.StructureLayers, Is.EqualTo(7));
            });
        }

        [Test]
        public void Clone_NullOptimizedSettings_StaysNull() {
            var clone = new StarDetectionSettingsSnapshot().Clone();
            Assert.That(clone.OptimizedSettings, Is.Null);
        }

        [Test]
        public void CopyMachineLocalFrom_CopiesOnlyMachineLocalFields() {
            var target = new StarDetectionSettingsSnapshot {
                BrightnessSensitivity = 7.5,
                MinHFR = 1.05,
                DebugMode = false,
                IntermediateSavePath = "",
                SaveIntermediateImages = false,
                PSFParallelPartitionSize = 100
            };
            var source = new StarDetectionSettingsSnapshot {
                BrightnessSensitivity = 99.0,
                MinHFR = 9.9,
                DebugMode = true,
                IntermediateSavePath = @"C:\hf\debug",
                SaveIntermediateImages = true,
                PSFParallelPartitionSize = 250
            };

            target.CopyMachineLocalFrom(source);

            Assert.Multiple(() => {
                Assert.That(target.DebugMode, Is.True);
                Assert.That(target.IntermediateSavePath, Is.EqualTo(@"C:\hf\debug"));
                Assert.That(target.SaveIntermediateImages, Is.True);
                Assert.That(target.PSFParallelPartitionSize, Is.EqualTo(250));
                // Detection knobs must be untouched.
                Assert.That(target.BrightnessSensitivity, Is.EqualTo(7.5));
                Assert.That(target.MinHFR, Is.EqualTo(1.05));
            });
        }
    }
}
