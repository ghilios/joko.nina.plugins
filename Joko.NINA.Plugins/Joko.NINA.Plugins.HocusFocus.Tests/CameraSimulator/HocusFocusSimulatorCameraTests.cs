using System;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Enum;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class HocusFocusSimulatorCameraTests {

    private static CameraSimulatorOptions BuildOptions() {
        return new CameraSimulatorOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
    }

    private static HocusFocusSimulatorCamera BuildCamera(
        ICameraSimulatorOptions options,
        IFocuserMediator focuser = null,
        ITelescopeMediator telescope = null) {
        return new HocusFocusSimulatorCamera(
            Substitute.For<IProfileService>(),
            Substitute.For<IExposureDataFactory>(),
            Substitute.For<IImageDataFactory>(),
            telescope ?? Substitute.For<ITelescopeMediator>(),
            focuser ?? Substitute.For<IFocuserMediator>(),
            options);
    }

    [Test]
    public void SensorDerivedProps_TrackIMX455() {
        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX455;
        options.Gain = 0;
        var camera = BuildCamera(options);

        Assert.Multiple(() => {
            Assert.That(camera.CameraXSize, Is.EqualTo(9576));
            Assert.That(camera.CameraYSize, Is.EqualTo(6388));
            Assert.That(camera.PixelSizeX, Is.EqualTo(3.76).Within(1e-9));
            Assert.That(camera.PixelSizeY, Is.EqualTo(3.76).Within(1e-9));
            Assert.That(camera.BitDepth, Is.EqualTo(16));
            Assert.That(camera.SensorType, Is.EqualTo(SensorType.Monochrome));
            Assert.That(camera.SensorName, Is.EqualTo("IMX455"));
            Assert.That(camera.ElectronsPerADU, Is.EqualTo(0.763).Within(0.001));
            Assert.That(camera.GainMin, Is.EqualTo(0));
            Assert.That(camera.GainMax, Is.EqualTo(300));
        });
    }

    [Test]
    public void SensorDerivedProps_SwitchToIMX294() {
        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX294;
        options.Gain = 0;
        var camera = BuildCamera(options);

        Assert.Multiple(() => {
            Assert.That(camera.CameraXSize, Is.EqualTo(4144));
            Assert.That(camera.CameraYSize, Is.EqualTo(2822));
            Assert.That(camera.PixelSizeX, Is.EqualTo(4.63).Within(1e-9));
            Assert.That(camera.BitDepth, Is.EqualTo(14));
            Assert.That(camera.SensorType, Is.EqualTo(SensorType.Monochrome));
            Assert.That(camera.SensorName, Is.EqualTo("IMX294"));
            Assert.That(camera.ElectronsPerADU, Is.EqualTo(4.028).Within(0.001));
            Assert.That(camera.GainMax, Is.EqualTo(400));
        });
    }

    [Test]
    public void SensorDerivedProps_ChangeWhenSensorModelChangesLive() {
        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX455;
        var camera = BuildCamera(options);
        Assert.That(camera.CameraXSize, Is.EqualTo(9576));

        options.SensorModel = SonySensorModel.IMX533;
        Assert.Multiple(() => {
            Assert.That(camera.CameraXSize, Is.EqualTo(3008));
            Assert.That(camera.CameraYSize, Is.EqualTo(3008));
            Assert.That(camera.BitDepth, Is.EqualTo(14));
            Assert.That(camera.SensorName, Is.EqualTo("IMX533"));
        });
    }

    [Test]
    public void ElectronsPerADU_TracksGain() {
        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX455;
        var camera = BuildCamera(options);

        camera.Gain = 200; // 20 dB → 10x fewer e-/ADU
        Assert.That(camera.ElectronsPerADU, Is.EqualTo(0.0763).Within(0.001));
        Assert.That(options.Gain, Is.EqualTo(200), "camera.Gain writes through to options");
    }

    private static HocusFocusSimulatorCamera ConnectAndStart(
        ICameraSimulatorOptions options, IFocuserMediator focuser, ITelescopeMediator telescope) {
        var camera = BuildCamera(options, focuser, telescope);
        camera.Connect(CancellationToken.None).GetAwaiter().GetResult();
        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
        return camera;
    }

    [Test]
    public void DownloadExposure_FocuserDisconnected_ThrowsDescriptiveErrorNamingFocuser() {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = false });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var camera = ConnectAndStart(BuildOptions(), focuser, telescope);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await camera.DownloadExposure(CancellationToken.None));
        Assert.That(ex.Message, Does.Contain("focuser"));
    }

    [Test]
    public void DownloadExposure_MountDisconnected_ThrowsDescriptiveErrorNamingMount() {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = false });

        var camera = ConnectAndStart(BuildOptions(), focuser, telescope);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await camera.DownloadExposure(CancellationToken.None));
        Assert.That(ex.Message, Does.Contain("mount"));
    }

    [Test]
    public void DownloadExposure_BothConnected_ReachesCompositorStubAndThrowsPhase4() {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var camera = ConnectAndStart(BuildOptions(), focuser, telescope);

        // Both devices connected: the disconnected guards pass and the call reaches the Phase-0 compositor
        // stub, proving the wiring is correct and the guards run first.
        var ex = Assert.ThrowsAsync<NotImplementedException>(
            async () => await camera.DownloadExposure(CancellationToken.None));
        Assert.That(ex.Message, Does.Contain("Phase 4"));
    }

    [Test]
    public void DownloadExposure_RenderThrows_LeavesCameraStateError() {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var camera = ConnectAndStart(BuildOptions(), focuser, telescope);

        Assert.ThrowsAsync<NotImplementedException>(
            async () => await camera.DownloadExposure(CancellationToken.None));
        // The render threw after CameraState advanced to Download; it must be reset to Error, not left in Download.
        Assert.That(camera.CameraState, Is.EqualTo(CameraStates.Error));
    }

    [Test]
    public void Connect_SetsConnectedAndIdle() {
        var camera = BuildCamera(BuildOptions());
        var connected = camera.Connect(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(connected, Is.True);
            Assert.That(camera.Connected, Is.True);
            Assert.That(camera.CameraState, Is.EqualTo(CameraStates.Idle));
        });
    }

    [Test]
    public void StartExposure_WhenNotConnected_Throws() {
        var camera = BuildCamera(BuildOptions());
        Assert.Throws<InvalidOperationException>(() => camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 }));
    }

    [Test]
    public void StartExposure_SetsExposingState() {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var camera = ConnectAndStart(BuildOptions(), focuser, telescope);
        Assert.That(camera.CameraState, Is.EqualTo(CameraStates.Exposing));
    }

    [Test]
    public void Disconnect_ClearsConnectedAndState() {
        var camera = BuildCamera(BuildOptions());
        camera.Connect(CancellationToken.None).GetAwaiter().GetResult();
        camera.Disconnect();
        Assert.Multiple(() => {
            Assert.That(camera.Connected, Is.False);
            Assert.That(camera.CameraState, Is.EqualTo(CameraStates.NoState));
        });
    }

    [Test]
    public async Task WaitUntilExposureIsReady_ZeroExposure_TransitionsToReading() {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var camera = ConnectAndStart(BuildOptions(), focuser, telescope);
        await camera.WaitUntilExposureIsReady(CancellationToken.None);
        Assert.That(camera.CameraState, Is.EqualTo(CameraStates.Reading));
    }

    [Test]
    public void Identity_IsStable() {
        var camera = BuildCamera(BuildOptions());
        Assert.Multiple(() => {
            Assert.That(camera.Name, Is.EqualTo("Hocus Focus Simulator"));
            Assert.That(camera.Category, Is.EqualTo("Hocus Focus"));
            Assert.That(camera.Id, Is.EqualTo("HocusFocus_SimulatorCamera"));
            Assert.That(camera.CanGetGain, Is.True);
            Assert.That(camera.CanSetGain, Is.True);
            Assert.That(camera.BinningModes, Has.Count.EqualTo(1));
        });
    }
}
