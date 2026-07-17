using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class HocusFocusSimulatorCameraProviderTests {

    private static HocusFocusSimulatorCameraProvider BuildProvider() {
        return new HocusFocusSimulatorCameraProvider(
            Substitute.For<IProfileService>(),
            Substitute.For<IExposureDataFactory>(),
            Substitute.For<IImageDataFactory>(),
            Substitute.For<ITelescopeMediator>(),
            Substitute.For<IFocuserMediator>());
    }

    [Test]
    public void Name_IsStable() {
        Assert.That(BuildProvider().Name, Is.EqualTo("Hocus Focus"));
    }

    [Test]
    public void GetEquipment_ReturnsExactlyOneCameraWithStableIdentity() {
        var equipment = BuildProvider().GetEquipment();
        Assert.Multiple(() => {
            Assert.That(equipment, Has.Count.EqualTo(1));
            Assert.That(equipment[0], Is.InstanceOf<ICamera>());
            Assert.That(equipment[0].Name, Is.EqualTo("Hocus Focus Simulator"));
            Assert.That(equipment[0].Category, Is.EqualTo("Hocus Focus"));
        });
    }
}
