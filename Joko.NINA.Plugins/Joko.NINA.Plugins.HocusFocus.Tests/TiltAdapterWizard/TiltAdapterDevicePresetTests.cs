using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

[TestFixture]
public class TiltAdapterDevicePresetTests {

    [Test]
    public void Manual_IsTheDefaultSentinel() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterDevicePreset.Manual.IsManual, Is.True);
            Assert.That(TiltAdapterDevicePreset.Manual.Name, Is.EqualTo("Manual"));
            Assert.That(TiltAdapterDevicePreset.All[0], Is.SameAs(TiltAdapterDevicePreset.Manual));
        });
    }

    [Test]
    public void NeumannCtuXt48_HasDocumentedValues() {
        var preset = TiltAdapterDevicePreset.ByName("Neumann CTU XT48");
        Assert.Multiple(() => {
            Assert.That(preset.IsManual, Is.False);
            Assert.That(preset.ScrewCount, Is.EqualTo(3));
            Assert.That(preset.AdjustmentType, Is.EqualTo(TiltAdjustmentType.Screws));
            Assert.That(preset.ThreadPitchMicrons, Is.EqualTo(400));
            Assert.That(preset.ScrewRadiusMillimeters, Is.EqualTo(44));
        });
    }

    [Test]
    public void ByName_UnknownFallsBackToManual() {
        Assert.That(TiltAdapterDevicePreset.ByName("does-not-exist"), Is.SameAs(TiltAdapterDevicePreset.Manual));
        Assert.That(TiltAdapterDevicePreset.ByName(null), Is.SameAs(TiltAdapterDevicePreset.Manual));
    }
}
