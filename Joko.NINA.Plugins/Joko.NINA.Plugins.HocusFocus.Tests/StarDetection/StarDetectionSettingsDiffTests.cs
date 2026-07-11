using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class StarDetectionSettingsDiffTests {

        private static StarDetectionOptions NewOptions() =>
            new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

        // A snapshot that initially mirrors the current options, so a single mutation produces a single diff row.
        private static StarDetectionSettingsSnapshot Mirror(StarDetectionOptions current) =>
            StarDetectionSettingsSnapshot.FromOptions(current);

        [Test]
        public void BuildDiff_IsEmpty_ForIdenticalSettings() {
            var current = NewOptions();
            var imported = Mirror(current);

            var diff = StarDetectionSettingsDiff.BuildDiff(current, imported);

            Assert.That(diff, Is.Empty);
        }

        [Test]
        public void BuildDiff_ReportsChangedDouble_WithFormatting() {
            var current = NewOptions(); // BrightnessSensitivity default 10.0 (interim v3 revert)
            var imported = Mirror(current);
            imported.BrightnessSensitivity = 9.1;

            var diff = StarDetectionSettingsDiff.BuildDiff(current, imported);

            Assert.That(diff, Has.Count.EqualTo(1));
            var row = diff[0];
            Assert.Multiple(() => {
                Assert.That(row.Name, Is.EqualTo("Brightness Sensitivity"));
                Assert.That(row.CurrentValue, Is.EqualTo("10"));
                Assert.That(row.NewValue, Is.EqualTo("9.1"));
            });
        }

        [Test]
        public void BuildDiff_FormatsEnumsAndBooleans() {
            var current = NewOptions();
            var imported = Mirror(current);
            imported.Simple_NoiseLevel = NoiseLevelEnum.High;       // enum, default Typical
            imported.RejectContaminatedStars = false;              // bool, default true

            var diff = StarDetectionSettingsDiff.BuildDiff(current, imported);

            var byName = diff.ToDictionary(r => r.Name);
            Assert.Multiple(() => {
                Assert.That(diff, Has.Count.EqualTo(2));
                Assert.That(byName["Noise Level"].CurrentValue, Is.EqualTo("Typical"));
                Assert.That(byName["Noise Level"].NewValue, Is.EqualTo("High"));
                Assert.That(byName["Reject Contaminated Stars"].CurrentValue, Is.EqualTo("On"));
                Assert.That(byName["Reject Contaminated Stars"].NewValue, Is.EqualTo("Off"));
            });
        }

        [Test]
        public void BuildDiff_IgnoresMachineLocalKnobs() {
            var current = NewOptions();
            var imported = Mirror(current);
            imported.PSFParallelPartitionSize = 12345;
            imported.DebugMode = true;
            imported.IntermediateSavePath = @"C:\somewhere\else";
            imported.SaveIntermediateImages = true;

            var diff = StarDetectionSettingsDiff.BuildDiff(current, imported);

            Assert.That(diff, Is.Empty);
        }

        [Test]
        public void BuildDiff_ReportsOptimizerResultChange() {
            var current = NewOptions(); // no optimizer result
            var imported = Mirror(current);
            imported.OptimizedSettings = new OptimizedStarDetectionSettings() {
                FinalJ = 0.98,
                RunCount = 2,
                CreatedAtUtc = new DateTime(2026, 6, 28, 12, 0, 0, DateTimeKind.Utc)
            };

            var diff = StarDetectionSettingsDiff.BuildDiff(current, imported);

            Assert.That(diff, Has.Count.EqualTo(1));
            Assert.Multiple(() => {
                Assert.That(diff[0].Name, Is.EqualTo("Optimizer result"));
                Assert.That(diff[0].CurrentValue, Is.EqualTo("none"));
                Assert.That(diff[0].NewValue, Does.StartWith("present"));
            });
        }

        [Test]
        public void ImportableSettings_CoverExactlyTheImportableInterfaceProperties() {
            // Drift guard: every read/write IStarDetectionOptions property must be either importable (with a label) or
            // explicitly excluded as machine-local. A newly added knob that is wired into neither fails here.
            var readWrite = typeof(IStarDetectionOptions).GetProperties()
                .Where(p => p.CanRead && p.CanWrite)
                .Select(p => p.Name)
                .ToHashSet();

            var importable = StarDetectionSettingsDiff.ImportableSettings.Select(x => x.Property).ToList();
            var excluded = StarDetectionSettingsDiff.ExcludedFromImport.ToList();

            var union = new HashSet<string>(importable);
            union.UnionWith(excluded);

            Assert.Multiple(() => {
                Assert.That(importable, Is.Unique, "ImportableSettings must not contain duplicate properties.");
                Assert.That(importable.Intersect(excluded), Is.Empty, "A property cannot be both importable and excluded.");
                Assert.That(union, Is.EquivalentTo(readWrite),
                    "ImportableSettings ∪ ExcludedFromImport must cover exactly the read/write IStarDetectionOptions properties.");
            });
        }
    }
}
