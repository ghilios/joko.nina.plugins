using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Enum;
using NINA.Core.Utility.Converters;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Exceptions;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
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

    private static HocusFocusSimulatorCamera BuildCameraWithCompositor(
        ICameraSimulatorOptions options,
        IStarFieldCompositor compositor,
        IExposureDataFactory exposureDataFactory,
        IFocuserMediator focuser,
        ITelescopeMediator telescope) {
        return new HocusFocusSimulatorCamera(
            Substitute.For<IProfileService>(),
            exposureDataFactory,
            Substitute.For<IImageDataFactory>(),
            telescope,
            focuser,
            options,
            compositor);
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

        // MUST be CameraExposureFailedException, not a bare exception: NINA's ImagingVM.CaptureImage routes typed
        // camera exceptions to Notification.ShowError(ex.Message) (our text verbatim), while anything else lands in
        // its generic branch, which prefixes "Unexpected error" and calls AbortExposure(). This pins the contract.
        var ex = Assert.ThrowsAsync<CameraExposureFailedException>(
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

        var ex = Assert.ThrowsAsync<CameraExposureFailedException>(
            async () => await camera.DownloadExposure(CancellationToken.None));
        Assert.That(ex.Message, Does.Contain("mount"));
    }

    [Test]
    public async Task DownloadExposure_BothConnected_RendersAndReturnsExposureData() {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX533;

        // Both devices connected: the disconnected guards pass and the call reaches the (now real) compositor,
        // whose rendered ushort[] is plumbed through IExposureDataFactory. A fake compositor keeps the test fast
        // and independent of any ASTAP database, proving the DownloadExposure wiring end-to-end.
        var pixels = new ushort[3008 * 3008];
        var compositor = Substitute.For<IStarFieldCompositor>();
        compositor.Render(Arg.Any<RenderRequest>(), Arg.Any<CancellationToken>()).Returns(pixels);
        var exposureDataFactory = Substitute.For<IExposureDataFactory>();

        var camera = BuildCameraWithCompositor(options, compositor, exposureDataFactory, focuser, telescope);
        camera.Connect(CancellationToken.None).GetAwaiter().GetResult();
        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });

        await camera.DownloadExposure(CancellationToken.None);

        // The rendered ushort[] is handed to the factory with the selected sensor's geometry (IMX533: 3008², 14-bit,
        // monochrome), and the camera returns to Idle.
        Assert.That(camera.CameraState, Is.EqualTo(CameraStates.Idle));
        exposureDataFactory.Received(1).CreateImageArrayExposureData(
            pixels, 3008, 3008, 14, false, Arg.Any<ImageMetaData>());
    }

    [Test]
    public async Task DownloadExposure_SensorModelChangedAfterStart_UsesSnapshotGeometry() {
        // Regression: the rendered pixels are sized from the render-snapshot's SensorModel, so the width/height/
        // bit-depth handed to the exposure-data factory must come from the SAME snapshot — not the live options,
        // which the user can change between StartExposure and DownloadExposure.
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX533; // 3008², 14-bit at exposure start

        var pixels = new ushort[3008 * 3008];
        var compositor = Substitute.For<IStarFieldCompositor>();
        compositor.Render(Arg.Any<RenderRequest>(), Arg.Any<CancellationToken>()).Returns(pixels);
        var exposureDataFactory = Substitute.For<IExposureDataFactory>();

        var camera = BuildCameraWithCompositor(options, compositor, exposureDataFactory, focuser, telescope);
        camera.Connect(CancellationToken.None).GetAwaiter().GetResult();
        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });

        // User switches to a different sensor (IMX455: 9576×6388, 16-bit) mid-exposure.
        options.SensorModel = SonySensorModel.IMX455;

        await camera.DownloadExposure(CancellationToken.None);

        // Must declare the SNAPSHOT geometry (IMX533), matching the pixel array — not the live IMX455.
        exposureDataFactory.Received(1).CreateImageArrayExposureData(
            pixels, 3008, 3008, 14, false, Arg.Any<ImageMetaData>());
        exposureDataFactory.DidNotReceive().CreateImageArrayExposureData(
            Arg.Any<ushort[]>(), 9576, 6388, 16, Arg.Any<bool>(), Arg.Any<ImageMetaData>());
    }

    [Test]
    public async Task DownloadExposure_AstapCatalogPathChangedBetweenExposures_NextRenderQueriesNewPath() {
        // Regression: the camera built its AstapCatalogReader once, in its constructor, so editing the ASTAP
        // catalog path in options had no effect until the camera was reconnected. The reader is now resolved per
        // exposure from the request snapshot, so an options edit lands on the very next exposure.
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX533; // smallest sensor: keeps the two real renders quick
        options.FocalLengthMillimeters = 910.0;       // must be > 0, else the compositor short-circuits to a dark
                                                      // frame and never reaches the catalog at all
        options.AstapCatalogPath = @"D:\astap-old";

        // The REAL compositor (where the fix lives) over a factory spy. No database exists at either path, so
        // every render is a starless frame — the assertion is purely which path the reader was built for.
        var pathsSeen = new List<string>();
        var compositor = new StarFieldCompositor(path => {
            pathsSeen.Add(path);
            return new FakeCatalogReader(() => throw new DirectoryNotFoundException($"no astap at '{path}'"));
        });

        var exposureDataFactory = Substitute.For<IExposureDataFactory>();
        var camera = BuildCameraWithCompositor(options, compositor, exposureDataFactory, focuser, telescope);
        camera.Connect(CancellationToken.None).GetAwaiter().GetResult();

        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
        await camera.DownloadExposure(CancellationToken.None);

        // The user edits the catalog path in options — WITHOUT disconnecting/reconnecting the camera.
        options.AstapCatalogPath = @"D:\astap-new";

        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
        await camera.DownloadExposure(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(pathsSeen, Is.EqualTo(new[] { @"D:\astap-old", @"D:\astap-new" }).AsCollection,
                "the exposure after an options edit must read the NEW catalog path, with no reconnect");
            // Both exposures still completed: a missing database is a starless frame, never a failed exposure.
            Assert.That(camera.CameraState, Is.EqualTo(CameraStates.Idle));
        });
        exposureDataFactory.Received(2).CreateImageArrayExposureData(
            Arg.Any<ushort[]>(), 3008, 3008, 14, false, Arg.Any<ImageMetaData>());
    }

    [Test]
    public void DownloadExposure_RenderThrows_LeavesCameraStateError() {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var compositor = Substitute.For<IStarFieldCompositor>();
        compositor.Render(Arg.Any<RenderRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));

        var camera = BuildCameraWithCompositor(
            BuildOptions(), compositor, Substitute.For<IExposureDataFactory>(), focuser, telescope);
        camera.Connect(CancellationToken.None).GetAwaiter().GetResult();
        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await camera.DownloadExposure(CancellationToken.None));
        // The render threw after CameraState advanced to Download; it must be reset to Error, not left in Download.
        Assert.That(camera.CameraState, Is.EqualTo(CameraStates.Error));
    }

    [Test]
    public void HasSetupDialog_IsTrue_SoNinaShowsTheGearButton() {
        var camera = BuildCamera(BuildOptions());
        // NINA's DeviceChooserVM.SetupDialog() early-returns unless HasSetupDialog is true, and the connector's
        // gear button binds IsEnabled straight to it — false would hide our only rig-config entry point, since
        // the rig options render read-only in the plugin Options tab.
        Assert.That(camera.HasSetupDialog, Is.True);
    }

    [Test]
    public void TiltAdapterVM_DrivesTheCamerasOwnOptions_SoThePanelAndTheRendererShareOnePlane() {
        var options = BuildOptions();
        var camera = BuildCamera(options);

        // The setup dialog hosts the tilt-adapter panel via {Binding TiltAdapterVM}, and the panel writes the
        // injected plane straight onto the options it was handed. Were it built from any other instance — the
        // plugin's static singleton being the tempting one — every screw click would land on options the camera
        // never snapshots, so the panel would move nothing and the loop would never converge.
        Assert.That(camera.TiltAdapterVM.Options, Is.SameAs(options));
    }

    [Test]
    public async Task Options_EditsLandOnTheNextRenderSnapshot_AsTheSetupDialogRequires() {
        // The setup dialog's DataTemplate binds Options.* against the camera itself (WindowService.Show(this, ...)),
        // so an edit made in that dialog must reach the very options instance BuildRenderRequest snapshots — with no
        // reconnect. Were Options ever to hand out a copy (or the request to be built from a different instance), the
        // rig controls would silently do nothing and the dialog would be decorative.
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = 5000 });
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });

        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX533; // smallest sensor: keeps the fake render array small
        options.ApertureMillimeters = 200.0;

        RenderRequest captured = null;
        var compositor = Substitute.For<IStarFieldCompositor>();
        compositor.Render(Arg.Any<RenderRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                captured = call.Arg<RenderRequest>();
                return new ushort[3008 * 3008];
            });

        var camera = BuildCameraWithCompositor(
            options, compositor, Substitute.For<IExposureDataFactory>(), focuser, telescope);
        camera.Connect(CancellationToken.None).GetAwaiter().GetResult();

        // Stand in for the user editing Aperture in the setup dialog, which reaches the options only via this path.
        camera.Options.ApertureMillimeters = 350.0;

        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
        await camera.DownloadExposure(CancellationToken.None);

        Assert.That(captured, Is.Not.Null, "the compositor must have been handed a render snapshot");
        Assert.Multiple(() => {
            Assert.That(captured.ApertureMillimeters, Is.EqualTo(350.0),
                "an edit through camera.Options must reach the very next render snapshot");
            // Pinpoints which link broke if the assertion above fails: a copy here, or a stale read there.
            Assert.That(camera.Options, Is.SameAs(options),
                "Options must expose the injected instance, not a copy");
        });
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

    /// <summary>
    /// Regression: NINA crashed with an unhandled InvalidCastException on the UI thread when the camera panel
    /// bound Gains. NINA.Core's IntListToTextBlockListConverter does `((List&lt;int&gt;)value).Select(...)` — a hard
    /// cast to the CONCRETE List&lt;int&gt;, even though ICamera declares IList&lt;int&gt;. Backing Gains with an array
    /// (Array.Empty&lt;int&gt;()) satisfied the interface but took NINA down on connect. This drives NINA's real
    /// converter with our real Gains, so reverting the backing type to an array reproduces the original crash.
    /// Convert() is not enumerated: the cast is eager (that's what threw), while Select is lazy and would
    /// construct WPF TextBlocks off the STA thread.
    /// </summary>
    [Test]
    public void Gains_SurvivesNinaIntListToTextBlockListConverter() {
        var camera = BuildCamera(BuildOptions());
        var converter = new IntListToTextBlockListConverter();
        Assert.DoesNotThrow(() => converter.Convert(camera.Gains, typeof(object), null, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Pins the concrete List&lt;T&gt; backing of every IList-typed ICamera/IDevice member. NINA's binding layer
    /// hard-casts these to List&lt;T&gt; (see <see cref="Gains_SurvivesNinaIntListToTextBlockListConverter"/>), so
    /// "optimizing" any of them to an array is a NINA-crashing change, not a harmless one.
    /// </summary>
    [Test]
    public void ListBackedMembers_AreConcreteListsForNinaBinding() {
        var camera = BuildCamera(BuildOptions());
        Assert.Multiple(() => {
            Assert.That(camera.Gains, Is.InstanceOf<List<int>>());
            Assert.That(camera.ReadoutModes, Is.InstanceOf<List<string>>());
            Assert.That(camera.SupportedActions, Is.InstanceOf<List<string>>());
        });
    }
}
