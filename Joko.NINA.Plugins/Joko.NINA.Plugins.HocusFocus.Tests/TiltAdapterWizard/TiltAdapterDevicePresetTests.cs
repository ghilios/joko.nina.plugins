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
    public void AsgPhotonCage78mm_HasDocumentedValues() {
        var preset = TiltAdapterDevicePreset.ByName("ASG Photon Cage - 78mm");
        Assert.Multiple(() => {
            Assert.That(preset.IsManual, Is.False);
            Assert.That(preset.ScrewCount, Is.EqualTo(4));
            Assert.That(preset.AdjustmentType, Is.EqualTo(TiltAdjustmentType.Screws));
            Assert.That(preset.ThreadPitchMicrons, Is.EqualTo(212));
            Assert.That(preset.StepperStepSizeMicrons, Is.EqualTo(-1));
            Assert.That(preset.ScrewRadiusMillimeters, Is.EqualTo(44));
        });
    }

    [Test]
    public void AsgPhotonCage90mm_HasDocumentedValues() {
        var preset = TiltAdapterDevicePreset.ByName("ASG Photon Cage - 90mm");
        Assert.Multiple(() => {
            Assert.That(preset.IsManual, Is.False);
            Assert.That(preset.ScrewCount, Is.EqualTo(4));
            Assert.That(preset.AdjustmentType, Is.EqualTo(TiltAdjustmentType.Screws));
            Assert.That(preset.ThreadPitchMicrons, Is.EqualTo(212));
            Assert.That(preset.StepperStepSizeMicrons, Is.EqualTo(-1));
            Assert.That(preset.ScrewRadiusMillimeters, Is.EqualTo(50));
        });
    }

    [Test]
    public void AsgElectronicEat90mm_HasDocumentedValues() {
        var preset = TiltAdapterDevicePreset.ByName("ASG Electronic EAT - 90mm");
        Assert.Multiple(() => {
            Assert.That(preset.IsManual, Is.False);
            Assert.That(preset.ScrewCount, Is.EqualTo(4));
            Assert.That(preset.AdjustmentType, Is.EqualTo(TiltAdjustmentType.StepperMotors));
            Assert.That(preset.ThreadPitchMicrons, Is.EqualTo(-1));
            Assert.That(preset.StepperStepSizeMicrons, Is.EqualTo(1.8));
            Assert.That(preset.ScrewRadiusMillimeters, Is.EqualTo(55));
        });
    }

    [Test]
    public void AsgPhotonCageZwo461_HasDocumentedValues() {
        var preset = TiltAdapterDevicePreset.ByName("ASG Photon Cage - ZWO 461");
        Assert.Multiple(() => {
            Assert.That(preset.IsManual, Is.False);
            Assert.That(preset.ScrewCount, Is.EqualTo(4));
            Assert.That(preset.AdjustmentType, Is.EqualTo(TiltAdjustmentType.Screws));
            Assert.That(preset.ThreadPitchMicrons, Is.EqualTo(212));
            Assert.That(preset.StepperStepSizeMicrons, Is.EqualTo(-1));
            Assert.That(preset.ScrewRadiusMillimeters, Is.EqualTo(54));
        });
    }

    [Test]
    public void AsgElectronicEatZwo461_HasDocumentedValues() {
        var preset = TiltAdapterDevicePreset.ByName("ASG Electronic EAT - ZWO 461");
        Assert.Multiple(() => {
            Assert.That(preset.IsManual, Is.False);
            Assert.That(preset.ScrewCount, Is.EqualTo(4));
            Assert.That(preset.AdjustmentType, Is.EqualTo(TiltAdjustmentType.StepperMotors));
            Assert.That(preset.ThreadPitchMicrons, Is.EqualTo(-1));
            Assert.That(preset.StepperStepSizeMicrons, Is.EqualTo(1.8));
            Assert.That(preset.ScrewRadiusMillimeters, Is.EqualTo(62.75));
        });
    }

    [Test]
    public void ByName_UnknownFallsBackToManual() {
        Assert.That(TiltAdapterDevicePreset.ByName("does-not-exist"), Is.SameAs(TiltAdapterDevicePreset.Manual));
        Assert.That(TiltAdapterDevicePreset.ByName(null), Is.SameAs(TiltAdapterDevicePreset.Manual));
    }
}
