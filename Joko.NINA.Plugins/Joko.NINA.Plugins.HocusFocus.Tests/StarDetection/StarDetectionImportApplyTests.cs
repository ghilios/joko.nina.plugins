using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class StarDetectionImportApplyTests {

        private static StarDetectionOptions NewOptions() =>
            new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

        private static OptimizedStarDetectionSettings SampleOptimized() => new OptimizedStarDetectionSettings() {
            BrightnessSensitivity = 3.5,
            StarClippingMultiplier = 2.0,
            NoiseClippingMultiplier = 4.0,
            StarPeakResponse = 0.7,
            MaxDistortion = 0.55,
            MinHFR = 1.3,
            StarCenterTolerance = 0.4,
            StructureLayers = 5,
            NoiseReductionRadius = 3,
            MinStarBoundingBoxSize = 5,
            HotpixelThresholdingEnabled = true,
            HotpixelThreshold = 0.002
        };

        [Test]
        public void ApplyImportedSnapshot_RestoresAdvancedKnobs_ThroughFileRoundTrip() {
            var source = NewOptions();
            source.UseAdvanced = true;
            source.BrightnessSensitivity = 8.4;
            source.StructureLayers = 6;
            source.MaxDistortion = 0.31;
            source.MinHFR = 1.05;

            // Full export → JSON → import, exercising the file format end to end.
            var json = StarDetectionSettingsExport.FromOptions(source).Serialize();
            var export = StarDetectionSettingsExport.Deserialize(json);

            var target = NewOptions(); // starts in Simple mode
            Assume.That(target.UseAdvanced, Is.False);

            target.ApplyImportedSnapshot(export.StarDetection);

            Assert.Multiple(() => {
                Assert.That(target.UseAdvanced, Is.True);
                Assert.That(target.BrightnessSensitivity, Is.EqualTo(8.4));
                Assert.That(target.StructureLayers, Is.EqualTo(6));
                Assert.That(target.MaxDistortion, Is.EqualTo(0.31));
                Assert.That(target.MinHFR, Is.EqualTo(1.05));
            });
        }

        [Test]
        public void ApplyImportedSnapshot_RestoresSimpleOptimizedMode() {
            var source = NewOptions();
            source.ApplyOptimizedSettings(SampleOptimized());
            var snapshot = StarDetectionSettingsSnapshot.FromOptions(source);

            var target = NewOptions();
            target.UseAdvanced = true;

            target.ApplyImportedSnapshot(snapshot);

            Assert.Multiple(() => {
                Assert.That(target.UseAdvanced, Is.False);
                Assert.That(target.UseOptimizedSettings, Is.True);
                Assert.That(target.HasOptimizedSettings, Is.True);
                Assert.That(target.BrightnessSensitivity, Is.EqualTo(3.5));
                Assert.That(target.MaxDistortion, Is.EqualTo(0.55));
            });
        }

        [Test]
        public void ApplyImportedSnapshot_LeavesMachineLocalPerfAndDebugUntouched() {
            var source = NewOptions();
            source.UseAdvanced = true;
            source.BrightnessSensitivity = 9.1;
            source.PSFParallelPartitionSize = 999;
            source.DebugMode = true;
            var snapshot = StarDetectionSettingsSnapshot.FromOptions(source);

            var target = NewOptions();
            target.UseAdvanced = true;
            target.PSFParallelPartitionSize = 50;
            target.DebugMode = false;
            target.BrightnessSensitivity = 2.0;

            target.ApplyImportedSnapshot(snapshot);

            Assert.Multiple(() => {
                // A normal algorithm knob IS imported.
                Assert.That(target.BrightnessSensitivity, Is.EqualTo(9.1));
                // The machine-local parallelism + diagnostics knobs are preserved on the imaging machine.
                Assert.That(target.PSFParallelPartitionSize, Is.EqualTo(50));
                Assert.That(target.DebugMode, Is.False);
            });
        }

        [Test]
        public void ApplyFullSnapshot_StillCopiesMachineLocalPerfAndDebug() {
            // Guards the behavioral split: the same-machine replay path (option c) must keep copying these two.
            var source = NewOptions();
            source.UseAdvanced = true;
            source.PSFParallelPartitionSize = 999;
            source.DebugMode = true;
            var snapshot = StarDetectionSettingsSnapshot.FromOptions(source);

            var target = NewOptions();
            target.UseAdvanced = true;
            target.PSFParallelPartitionSize = 50;
            target.DebugMode = false;

            target.ApplyFullSnapshot(snapshot);

            Assert.Multiple(() => {
                Assert.That(target.PSFParallelPartitionSize, Is.EqualTo(999));
                Assert.That(target.DebugMode, Is.True);
            });
        }
    }
}
