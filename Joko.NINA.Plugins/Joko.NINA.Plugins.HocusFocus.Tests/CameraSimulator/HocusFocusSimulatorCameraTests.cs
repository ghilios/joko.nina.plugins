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
        var profileService = Substitute.For<IProfileService>();
        return new CameraSimulatorOptions(profileService, new InMemoryPluginOptionsAccessor(),
            new NINA.Joko.Plugins.HocusFocus.AutoFocus.InspectorOptions(profileService, new InMemoryPluginOptionsAccessor()));
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

    private static IFocuserMediator FocuserAt(int position) {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = true, Position = position });
        return focuser;
    }

    private static ITelescopeMediator ConnectedTelescope() {
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = true });
        return telescope;
    }

    [Test]
    public void DownloadExposure_FocuserDisconnected_ThrowsDescriptiveErrorNamingFocuser() {
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = false });
        var telescope = ConnectedTelescope();

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
        var focuser = FocuserAt(5000);
        var telescope = Substitute.For<ITelescopeMediator>();
        telescope.GetInfo().Returns(new TelescopeInfo { Connected = false });

        var camera = ConnectAndStart(BuildOptions(), focuser, telescope);

        var ex = Assert.ThrowsAsync<CameraExposureFailedException>(
            async () => await camera.DownloadExposure(CancellationToken.None));
        Assert.That(ex.Message, Does.Contain("mount"));
    }

    [Test]
    public async Task DownloadExposure_BothConnected_RendersAndReturnsExposureData() {
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();

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
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();

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
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();

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
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();

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
    public async Task Options_EditsLandOnTheNextRenderSnapshot_AsTheSetupDialogRequires() {
        // The setup dialog's DataTemplate binds Options.* against the camera itself (WindowService.Show(this, ...)),
        // so an edit made in that dialog must reach the very options instance BuildRenderRequest snapshots — with no
        // reconnect. Were Options ever to hand out a copy (or the request to be built from a different instance), the
        // rig controls would silently do nothing and the dialog would be decorative.
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();

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

    /// <summary>
    /// The render must be handed the EFFECTIVE focuser step size, not the raw one. The raw value is the Aberration
    /// Inspector's MicronsPerFocuserStep, whose "uncalibrated" state is the -1 sentinel — and DefocusModel throws
    /// ArgumentOutOfRangeException on a non-positive k, which NINA surfaces as "Unexpected error" plus a spurious
    /// AbortExposure. So an uncalibrated rig must still render, at the 2.0 µm/step fallback.
    /// </summary>
    [TestCase(-1.0, 2.0, TestName = "BuildRenderRequest_UncalibratedStepSize_UsesTheFallback")]
    [TestCase(3.5, 3.5, TestName = "BuildRenderRequest_CalibratedStepSize_UsesTheCalibration")]
    public async Task BuildRenderRequest_UsesTheEffectiveFocuserStepSize(double configured, double expected) {
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();

        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX533; // smallest sensor: keeps the fake render array small
        options.FocuserStepSizeMicrons = configured;

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
        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
        await camera.DownloadExposure(CancellationToken.None);

        Assert.That(captured, Is.Not.Null, "the compositor must have been handed a render snapshot");
        Assert.That(captured.FocuserStepSizeMicrons, Is.EqualTo(expected));
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
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();

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
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();

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

    /// <summary>Runs one full expose→download cycle and returns the RenderRequest the compositor was handed.</summary>
    private static async Task<List<RenderRequest>> CaptureRequests(
            CameraSimulatorOptions options, int focuserPosition, int exposures) {
        var captured = new List<RenderRequest>();
        options.SensorModel = SonySensorModel.IMX533; // smallest sensor: keeps the fake render array small
        var compositor = Substitute.For<IStarFieldCompositor>();
        compositor.Render(Arg.Any<RenderRequest>(), Arg.Any<CancellationToken>())
                  .Returns(call => {
                      captured.Add(call.Arg<RenderRequest>());
                      return new ushort[3008 * 3008];
                  });

        var camera = BuildCameraWithCompositor(
            options, compositor, Substitute.For<IExposureDataFactory>(),
            FocuserAt(focuserPosition), ConnectedTelescope());
        await camera.Connect(CancellationToken.None);

        for (var i = 0; i < exposures; ++i) {
            camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
            await camera.WaitUntilExposureIsReady(CancellationToken.None);
            await camera.DownloadExposure(CancellationToken.None);
        }
        return captured;
    }

    [Test]
    public async Task RepeatedExposures_AtOneFocuserPosition_GetDifferentSeeds() {
        // MeasurementAverageCount > 1 averages several frames per autofocus point. With a constant seed those
        // frames were byte-identical, so the averaging was averaging a frame with itself. The focuser position
        // alone cannot fix this — repeat exposures share a position — so the counter is what separates them.
        var options = BuildOptions();
        options.NoiseSeed = 42;
        var captured = await CaptureRequests(options, focuserPosition: 25000, exposures: 2);

        Assert.That(captured, Has.Count.EqualTo(2));
        Assert.That(captured[1].NoiseSeed, Is.Not.EqualTo(captured[0].NoiseSeed),
            "the exposure counter must advance the seed between exposures at one position");
    }

    [Test]
    public async Task ExposuresAtDifferentFocuserPositions_GetDifferentSeeds() {
        var near = await CaptureRequests(BuildOptions(), focuserPosition: 24979, exposures: 1);
        var far = await CaptureRequests(BuildOptions(), focuserPosition: 25000, exposures: 1);
        Assert.That(near[0].NoiseSeed, Is.Not.EqualTo(far[0].NoiseSeed), "the focuser position must feed the seed");
    }

    [Test]
    public async Task NoiseSeedOption_StillChangesTheFrameSeed() {
        // The option must remain the BASE seed — otherwise a user setting it would have no effect at all.
        var a = BuildOptions(); a.NoiseSeed = 42;
        var b = BuildOptions(); b.NoiseSeed = 43;
        var first = await CaptureRequests(a, focuserPosition: 25000, exposures: 1);
        var second = await CaptureRequests(b, focuserPosition: 25000, exposures: 1);
        Assert.That(first[0].NoiseSeed, Is.Not.EqualTo(second[0].NoiseSeed));
    }

    [Test]
    public async Task ReconnectingResetsTheCounter_SoASessionReplaysIdentically() {
        // The counter resets on Connect, which is what keeps a fixed exposure sequence reproducible from the
        // base seed. This must reconnect ONE camera: a fresh camera starts at 0 from field initialization, so
        // comparing two cameras would pass even if Connect reset nothing.
        var captured = new List<RenderRequest>();
        var options = BuildOptions();
        options.NoiseSeed = 42;
        options.SensorModel = SonySensorModel.IMX533;
        var compositor = Substitute.For<IStarFieldCompositor>();
        compositor.Render(Arg.Any<RenderRequest>(), Arg.Any<CancellationToken>())
                  .Returns(call => { captured.Add(call.Arg<RenderRequest>()); return new ushort[3008 * 3008]; });

        var camera = BuildCameraWithCompositor(
            options, compositor, Substitute.For<IExposureDataFactory>(), FocuserAt(25000), ConnectedTelescope());

        for (var session = 0; session < 2; ++session) {
            await camera.Connect(CancellationToken.None);
            camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
            await camera.WaitUntilExposureIsReady(CancellationToken.None);
            await camera.DownloadExposure(CancellationToken.None);
            camera.Disconnect();
        }

        Assert.That(captured, Has.Count.EqualTo(2));
        Assert.That(captured[1].NoiseSeed, Is.EqualTo(captured[0].NoiseSeed),
            "reconnecting must reset the exposure counter, so the sequence replays identically");
    }

    /// <summary>Records when Render begins and blocks there until cancelled, so a test can observe that the
    /// render is already in flight while the camera is still exposing.</summary>
    private sealed class BlockingCompositor : IStarFieldCompositor {
        private readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim cancelled = new(false);

        public Task Started => started.Task;
        public int RenderCount;

        public bool WaitForCancellation(TimeSpan timeout) => cancelled.Wait(timeout);

        public ushort[] Render(RenderRequest request, CancellationToken token) {
            Interlocked.Increment(ref RenderCount);
            started.TrySetResult(true);
            // Cancellation is observed by waiting on the token's own handle, NOT via token.Register: Cancel()
            // signals that handle BEFORE it runs registered callbacks, so a using-scoped registration gets
            // disposed by this thread the moment the wait returns and its callback then never runs — the fake
            // would report "never cancelled" for a render that was in fact cancelled correctly.
            if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) {
                cancelled.Set();
            }
            token.ThrowIfCancellationRequested();
            return new ushort[1];
        }
    }

    /// <summary>
    /// Waits for the prefetched render to begin. Bounded rather than a bare await: if the prefetch ever
    /// regresses, these tests must FAIL, not hang the suite forever waiting on a render that never starts.
    /// </summary>
    private static async Task AwaitRenderStart(BlockingCompositor compositor) {
        var winner = await Task.WhenAny(compositor.Started, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.That(winner, Is.SameAs(compositor.Started), "the render must begin at StartExposure, not at DownloadExposure");
    }

    [Test]
    public async Task Render_StartsDuringTheExposure_NotAtDownload() {
        var compositor = new BlockingCompositor();
        var camera = BuildCameraWithCompositor(
            BuildOptions(), compositor, Substitute.For<IExposureDataFactory>(),
            FocuserAt(25000), ConnectedTelescope());
        await camera.Connect(CancellationToken.None);

        camera.StartExposure(new CaptureSequence { ExposureTime = 30.0 });

        // The render must already be running while the camera is still nominally exposing. Before this change
        // Render was not called until DownloadExposure, so this waits out the timeout.
        await AwaitRenderStart(compositor);
        camera.AbortExposure();
    }

    [Test]
    public async Task AbortExposure_CancelsTheInFlightRender() {
        var compositor = new BlockingCompositor();
        var camera = BuildCameraWithCompositor(
            BuildOptions(), compositor, Substitute.For<IExposureDataFactory>(),
            FocuserAt(25000), ConnectedTelescope());
        await camera.Connect(CancellationToken.None);

        camera.StartExposure(new CaptureSequence { ExposureTime = 30.0 });
        await AwaitRenderStart(compositor);
        camera.AbortExposure();

        Assert.That(compositor.WaitForCancellation(TimeSpan.FromSeconds(5)), Is.True,
            "aborting must cancel the prefetched render rather than leave every core busy on a dead frame");
    }

    [Test]
    public async Task DownloadWithoutFocuser_StillThrowsTheDescriptiveError_AndNeverRenders() {
        var compositor = new BlockingCompositor();
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = false });
        var camera = BuildCameraWithCompositor(
            BuildOptions(), compositor, Substitute.For<IExposureDataFactory>(),
            focuser, ConnectedTelescope());
        await camera.Connect(CancellationToken.None);

        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });

        var ex = Assert.ThrowsAsync<CameraExposureFailedException>(() => camera.DownloadExposure(CancellationToken.None));
        Assert.That(ex.Message, Does.Contain("no focuser is connected"), "the descriptive guard must survive prefetching");
        Assert.That(compositor.RenderCount, Is.Zero, "a frame nobody can use must never be rendered");
    }
}
