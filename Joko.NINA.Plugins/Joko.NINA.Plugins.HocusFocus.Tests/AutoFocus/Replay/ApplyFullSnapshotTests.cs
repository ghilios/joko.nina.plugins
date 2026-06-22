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
    public class ApplyFullSnapshotTests {

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
        public void ApplyFullSnapshot_RestoresAdvancedModeVerbatim() {
            // Source: a configured ADVANCED options object captured into a snapshot.
            var source = NewOptions();
            source.UseAdvanced = true;
            source.BrightnessSensitivity = 9.1;
            source.StructureLayers = 6;
            source.MaxDistortion = 0.33;
            source.MinHFR = 1.05;
            var snapshot = StarDetectionSettingsSnapshot.FromOptions(source);

            // Target starts in Simple mode, where ConfigureSimpleSettings would recompute the advanced knobs.
            var target = NewOptions();
            Assert.That(target.UseAdvanced, Is.False);

            target.ApplyFullSnapshot(snapshot);

            Assert.Multiple(() => {
                Assert.That(target.UseAdvanced, Is.True);
                Assert.That(target.BrightnessSensitivity, Is.EqualTo(9.1));
                Assert.That(target.StructureLayers, Is.EqualTo(6));
                Assert.That(target.MaxDistortion, Is.EqualTo(0.33));
                Assert.That(target.MinHFR, Is.EqualTo(1.05));
                Assert.That(target.UseOptimizedSettings, Is.False);
            });
        }

        [Test]
        public void ApplyFullSnapshot_RestoresSimpleOptimizedMode() {
            // Source: Simple mode with an optimized snapshot applied (UseAdvanced off, Use Optimized on).
            var source = NewOptions();
            source.ApplyOptimizedSettings(SampleOptimized());
            Assert.Multiple(() => {
                Assert.That(source.UseAdvanced, Is.False);
                Assert.That(source.UseOptimizedSettings, Is.True);
                Assert.That(source.HasOptimizedSettings, Is.True);
            });

            var snapshot = StarDetectionSettingsSnapshot.FromOptions(source);
            Assert.Multiple(() => {
                Assert.That(snapshot.UseAdvanced, Is.False);
                Assert.That(snapshot.UseOptimizedSettings, Is.True);
                Assert.That(snapshot.HasOptimizedSettings, Is.True);
            });

            // Target starts in Advanced mode; option (c) must put it back to Simple + Optimized.
            var target = NewOptions();
            target.UseAdvanced = true;

            target.ApplyFullSnapshot(snapshot);

            Assert.Multiple(() => {
                Assert.That(target.UseAdvanced, Is.False);
                Assert.That(target.UseOptimizedSettings, Is.True);
                Assert.That(target.HasOptimizedSettings, Is.True);
                // The optimized values are reflected in the effective knobs.
                Assert.That(target.BrightnessSensitivity, Is.EqualTo(3.5));
                Assert.That(target.MaxDistortion, Is.EqualTo(0.55));
            });
        }

        [Test]
        public void ApplyFullSnapshot_RestoresSimplePresetMode() {
            // Source: Simple mode, no optimized layer, with non-default presets so the derived knobs differ from
            // defaults. (In Simple mode the advanced knobs are recomputed from the presets, so we capture the
            // effective derived values to compare against.)
            var source = NewOptions();
            source.Simple_NoiseLevel = NoiseLevelEnum.High;
            source.Simple_PixelScale = PixelScaleEnum.WideField;
            source.Simple_FocusRange = FocusRangeEnum.WideRange;
            var expectedStructureLayers = source.StructureLayers;
            var expectedBrightness = source.BrightnessSensitivity;

            var snapshot = StarDetectionSettingsSnapshot.FromOptions(source);
            Assert.Multiple(() => {
                Assert.That(snapshot.UseAdvanced, Is.False);
                Assert.That(snapshot.UseOptimizedSettings, Is.False);
                Assert.That(snapshot.HasOptimizedSettings, Is.False);
            });

            // Target starts in Advanced mode with a deliberately different knob; option (c) must put it back to Simple.
            var target = NewOptions();
            target.UseAdvanced = true;
            target.StructureLayers = 9;

            target.ApplyFullSnapshot(snapshot);

            Assert.Multiple(() => {
                Assert.That(target.UseAdvanced, Is.False);
                Assert.That(target.UseOptimizedSettings, Is.False);
                Assert.That(target.HasOptimizedSettings, Is.False);
                Assert.That(target.Simple_NoiseLevel, Is.EqualTo(NoiseLevelEnum.High));
                Assert.That(target.Simple_PixelScale, Is.EqualTo(PixelScaleEnum.WideField));
                Assert.That(target.Simple_FocusRange, Is.EqualTo(FocusRangeEnum.WideRange));
                // The captured (preset-derived) knob values win over both the fresh preset baseline and the target's
                // prior Advanced value.
                Assert.That(target.StructureLayers, Is.EqualTo(expectedStructureLayers));
                Assert.That(target.BrightnessSensitivity, Is.EqualTo(expectedBrightness));
            });
        }
    }
}
