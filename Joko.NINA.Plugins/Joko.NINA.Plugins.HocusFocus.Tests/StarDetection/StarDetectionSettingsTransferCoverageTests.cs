using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// End-to-end drift guards over the settings-transfer chain, driven by reflection over
    /// <see cref="IStarDetectionOptions"/> so a newly added knob cannot quietly fall out of it.
    ///
    /// <para><see cref="StarDetectionSettingsDiffTests.ImportableSettings_CoverExactlyTheImportableInterfaceProperties"/>
    /// already guards the LIST of what is supposed to transfer. These guard the three hand-written copy steps that
    /// list drives — <see cref="StarDetectionSettingsSnapshot.FromOptions"/> (capture), the JSON round trip, and
    /// <see cref="StarDetectionOptions.ApplyImportedSnapshot"/> (apply) — because a knob missing from any one of
    /// them is present in the UI, present in the diff table, and still does not survive an export/import.</para>
    /// </summary>
    [TestFixture]
    public class StarDetectionSettingsTransferCoverageTests {

        private static StarDetectionOptions NewOptions() =>
            new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

        private static IReadOnlyList<PropertyInfo> ReadWriteOptionProperties() =>
            typeof(IStarDetectionOptions).GetProperties().Where(p => p.CanRead && p.CanWrite).ToList();

        /// <summary>
        /// A second valid value for every numeric knob, chosen inside the range its <see cref="StarDetectionOptions"/>
        /// setter validates (those setters THROW rather than clamp, so an out-of-range probe would fail the test for
        /// the wrong reason). Booleans, enums and strings need no entry — they get a generic rule below. A new numeric
        /// knob with no entry here fails <see cref="AlternateValues_CoverEverySettingThatTransfers"/>.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, object> NumericAlternates = new Dictionary<string, object>() {
            { nameof(IStarDetectionOptions.NoiseReductionRadius), 4 },                        // >= 0
            { nameof(IStarDetectionOptions.NoiseClippingMultiplier), 6.5 },                   // >= 0
            { nameof(IStarDetectionOptions.StarClippingMultiplier), 3.5 },                    // >= 0
            { nameof(IStarDetectionOptions.ContaminationSensitivity), 0.42 },
            { nameof(IStarDetectionOptions.StructureLayers), 4 },                             // > 0
            { nameof(IStarDetectionOptions.StructureLayerBoost), 2 },                         // [0, 6]
            { nameof(IStarDetectionOptions.BrightnessSensitivity), 7.25 },                    // >= 0
            { nameof(IStarDetectionOptions.StarPeakResponse), 0.55 },                         // > 0
            { nameof(IStarDetectionOptions.MaxDistortion), 0.42 },                            // [0, 1]
            { nameof(IStarDetectionOptions.DefocusDistortionSizeReference), 42.0 },           // [1, 1000]
            { nameof(IStarDetectionOptions.DefocusDistortionMinFactor), 0.33 },               // [0.01, 1]
            { nameof(IStarDetectionOptions.DefocusCenteringToleranceFactor), 3.5 },           // [1, 10]
            { nameof(IStarDetectionOptions.DonutMorphCloseSize), 9 },                         // [1, 25]
            { nameof(IStarDetectionOptions.DonutMinAnnularityHoleFraction), 0.22 },           // [0.02, 0.6]
            { nameof(IStarDetectionOptions.DonutMaxStreakEccentricity), 0.93 },               // [0.8, 1.0]
            { nameof(IStarDetectionOptions.DonutSaturationBloomRadius), 12.0 },               // [0, 100]
            { nameof(IStarDetectionOptions.AdaptiveNoiseBlockSize), 256 },                    // [16, 1024]
            { nameof(IStarDetectionOptions.StarCenterTolerance), 0.45 },                      // (0, 1]
            { nameof(IStarDetectionOptions.StarBackgroundBoxExpansion), 5 },                  // >= 1
            { nameof(IStarDetectionOptions.MinStarBoundingBoxSize), 7 },                      // >= 1
            { nameof(IStarDetectionOptions.MinHFR), 1.15 },                                   // >= 0
            { nameof(IStarDetectionOptions.StructureDilationSize), 5 },                       // >= 3
            { nameof(IStarDetectionOptions.StructureDilationCount), 2 },                      // >= 0
            { nameof(IStarDetectionOptions.PixelSampleSize), 0.75 },                          // (0, 1]
            { nameof(IStarDetectionOptions.PSFResolution), 12 },                              // >= 0
            { nameof(IStarDetectionOptions.PSFFitThreshold), 0.55 },                          // (0, 1]
            { nameof(IStarDetectionOptions.HotpixelThreshold), 0.004 },                       // (0, 1]
            { nameof(IStarDetectionOptions.SaturationThreshold), 0.88 },                      // (0, 1]
            { nameof(IStarDetectionOptions.PSFParallelPartitionSize), 24 },                   // >= 0 (machine-local)
        };

        /// <summary>A value that differs from <paramref name="current"/>, or null when this property's type has no
        /// generic rule and no <see cref="NumericAlternates"/> entry.</summary>
        private static object AlternateValue(PropertyInfo property, object current) {
            if (NumericAlternates.TryGetValue(property.Name, out var numeric)) {
                return numeric;
            }
            if (property.PropertyType == typeof(bool)) {
                return !(bool)current;
            }
            if (property.PropertyType.IsEnum) {
                return Enum.GetValues(property.PropertyType).Cast<object>().First(v => !Equals(v, current));
            }
            if (property.PropertyType == typeof(string)) {
                return (current as string ?? "") + "-alternate";
            }
            return null;
        }

        [Test]
        public void AlternateValues_CoverEverySettingThatTransfers() {
            // Keeps the two guards below honest: without this, a knob with no alternate value would be silently
            // skipped by them and its missing copy step would go unnoticed.
            var unsupported = ReadWriteOptionProperties()
                .Where(p => AlternateValue(p, DefaultOf(p)) == null)
                .Select(p => $"{p.Name} ({p.PropertyType.Name})")
                .ToList();

            Assert.That(unsupported, Is.Empty,
                "every read/write IStarDetectionOptions property needs a second valid value so the transfer guards can move it");
        }

        private static object DefaultOf(PropertyInfo property) => property.GetValue(new StarDetectionSettingsSnapshot());

        [Test]
        public void FromOptions_CapturesEverySetting() {
            // The snapshot is a plain POCO with no validating setters, so it can hold the mutated source directly —
            // this isolates the capture step from the live options' range checks.
            var source = new StarDetectionSettingsSnapshot();
            var properties = ReadWriteOptionProperties();
            foreach (var property in properties) {
                property.SetValue(source, AlternateValue(property, property.GetValue(source)));
            }

            var captured = StarDetectionSettingsSnapshot.FromOptions(source);

            Assert.Multiple(() => {
                foreach (var property in properties) {
                    Assert.That(property.GetValue(captured), Is.EqualTo(property.GetValue(source)),
                        $"{property.Name} is not captured by StarDetectionSettingsSnapshot.FromOptions");
                }
            });
        }

        [Test]
        public void EveryImportableSetting_SurvivesAFileRoundTripAndApply() {
            var target = NewOptions();
            var importable = StarDetectionSettingsDiff.ImportableSettings
                .Select(x => typeof(IStarDetectionOptions).GetProperty(x.Property))
                .ToList();

            // Start from the target's own values so each mutation is a real change, then move every importable knob.
            var source = StarDetectionSettingsSnapshot.FromOptions(target);
            foreach (var property in importable) {
                property.SetValue(source, AlternateValue(property, property.GetValue(source)));
            }

            // Through the real file format, so a knob the JSON drops fails here too.
            var restored = StarDetectionSettingsExport.Deserialize(
                StarDetectionSettingsExport.FromOptions(source).Serialize()).StarDetection;
            target.ApplyImportedSnapshot(restored);

            Assert.Multiple(() => {
                foreach (var property in importable) {
                    Assert.That(property.GetValue(target), Is.EqualTo(property.GetValue(source)),
                        $"{property.Name} does not survive export -> import");
                }
            });
        }
    }
}
