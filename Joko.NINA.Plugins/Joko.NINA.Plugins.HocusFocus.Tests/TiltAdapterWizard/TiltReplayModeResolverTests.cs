using System;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard {

    [TestFixture]
    public class TiltReplayModeResolverTests {

        [Test]
        public void UseCurrentSettings_CurrentGeometry_NoOverride_NoMutation() {
            var mode = TiltReplayModeResolver.Resolve(ReplaySettingsChoice.UseCurrentSettings);
            Assert.Multiple(() => {
                Assert.That(mode.UseMetadataGeometry, Is.False);
                Assert.That(mode.ApplyCaptureTimeOverridePerStep, Is.False);
                Assert.That(mode.UpdateProfileToCaptureTime, Is.False);
            });
        }

        [Test]
        public void UseCaptureTimeInMemory_MetadataGeometry_PerStepOverride_NoMutation() {
            var mode = TiltReplayModeResolver.Resolve(ReplaySettingsChoice.UseCaptureTimeSettingsInMemory);
            Assert.Multiple(() => {
                Assert.That(mode.UseMetadataGeometry, Is.True);
                Assert.That(mode.ApplyCaptureTimeOverridePerStep, Is.True);
                Assert.That(mode.UpdateProfileToCaptureTime, Is.False);
            });
        }

        [Test]
        public void UpdateProfile_MetadataGeometry_NoPerStepOverride_Mutates() {
            var mode = TiltReplayModeResolver.Resolve(ReplaySettingsChoice.UpdateProfileToCaptureTime);
            Assert.Multiple(() => {
                Assert.That(mode.UseMetadataGeometry, Is.True);
                Assert.That(mode.ApplyCaptureTimeOverridePerStep, Is.False);
                Assert.That(mode.UpdateProfileToCaptureTime, Is.True);
            });
        }

        [Test]
        public void Cancel_Throws() {
            Assert.Throws<ArgumentOutOfRangeException>(() => TiltReplayModeResolver.Resolve(ReplaySettingsChoice.Cancel));
        }
    }
}
