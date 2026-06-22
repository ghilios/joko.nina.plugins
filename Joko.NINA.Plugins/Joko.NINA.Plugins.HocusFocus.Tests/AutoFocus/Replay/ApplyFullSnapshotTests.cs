using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus.Replay {

    [TestFixture]
    public class ApplyFullSnapshotTests {

        private static StarDetectionOptions NewOptions() =>
            new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

        [Test]
        public void ApplyFullSnapshot_FromSimpleMode_ForcesAdvancedAndDoesNotClobber() {
            // Source: a configured advanced options object captured into a snapshot.
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
                // UseAdvanced is forced true first so the Simple-mode recompute cannot clobber the applied values.
                Assert.That(target.UseAdvanced, Is.True);
                Assert.That(target.BrightnessSensitivity, Is.EqualTo(9.1));
                Assert.That(target.StructureLayers, Is.EqualTo(6));
                Assert.That(target.MaxDistortion, Is.EqualTo(0.33));
                Assert.That(target.MinHFR, Is.EqualTo(1.05));
                Assert.That(target.UseOptimizedSettings, Is.False);
            });
        }
    }
}
