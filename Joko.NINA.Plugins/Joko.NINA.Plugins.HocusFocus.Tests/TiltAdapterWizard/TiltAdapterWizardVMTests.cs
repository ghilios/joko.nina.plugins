using CommunityToolkit.Mvvm.Input;
using NINA.Core.Interfaces;
using NINA.Core.Utility.SerialCommunication;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
// Import just the one type — the StarDetection.Optimization namespace also defines a WizardStep that would
// collide with TiltAdapterWizard.WizardStep used elsewhere in this fixture.
using OptimizedStarDetectionSettings = NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.OptimizedStarDetectionSettings;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard {

    [TestFixture]
    [Apartment(System.Threading.ApartmentState.STA)]
    public class TiltAdapterWizardVMTests {

        private static InspectorVM BuildInspector(IInspectorOptions inspectorOptions = null) {
            return new InspectorVM(
                profileService: Substitute.For<IProfileService>(),
                applicationStatusMediator: Substitute.For<IApplicationStatusMediator>(),
                imagingMediator: Substitute.For<IImagingMediator>(),
                cameraMediator: Substitute.For<ICameraMediator>(),
                focuserMediator: Substitute.For<IFocuserMediator>(),
                filterWheelMediator: Substitute.For<IFilterWheelMediator>(),
                telescopeMediator: Substitute.For<ITelescopeMediator>(),
                starDetectionOptions: Substitute.For<IStarDetectionOptions>(),
                starAnnotatorOptions: Substitute.For<IStarAnnotatorOptions>(),
                inspectorOptions: inspectorOptions ?? Substitute.For<IInspectorOptions>(),
                autoFocusOptions: Substitute.For<IAutoFocusOptions>(),
                autoFocusEngineFactory: Substitute.For<IAutoFocusEngineFactory>(),
                imageDataFactory: Substitute.For<IImageDataFactory>(),
                starDetectionSelector: Substitute.For<IPluggableBehaviorSelector<IStarDetection>>(),
                starAnnotatorSelector: Substitute.For<IPluggableBehaviorSelector<IStarAnnotator>>(),
                applicationDispatcher: new SynchronousApplicationDispatcher(),
                alglibAPI: new AlglibAPI(),
                tiltAdapterOptions: Substitute.For<ITiltAdapterOptions>());
        }

        private static (TiltAdapterWizardVM vm, ITiltAdapterOptions options, ICameraMediator camera, IFocuserMediator focuser) Build(
            int screwCount = 3, IApplicationDispatcher dispatcher = null, System.Action<ITiltAdapterOptions> configureOptions = null,
            IProfileService profileService = null, TiltDeviceConnectionService tiltDeviceService = null,
            ISerialPortProvider serialPortProvider = null, Func<Task<bool>> confirmIdleDisconnectAsync = null,
            IInspectorOptions inspectorOptions = null) {
            profileService ??= Substitute.For<IProfileService>();
            var camera = Substitute.For<ICameraMediator>();
            var focuser = Substitute.For<IFocuserMediator>();
            var options = Substitute.For<ITiltAdapterOptions>();
            options.ScrewCount.Returns(screwCount);
            options.MeasurementAverageCount.Returns(1);
            configureOptions?.Invoke(options);
            var inspector = BuildInspector(inspectorOptions);
            var vm = new TiltAdapterWizardVM(
                profileService: profileService,
                applicationStatusMediator: Substitute.For<IApplicationStatusMediator>(),
                cameraMediator: camera,
                focuserMediator: focuser,
                inspector: inspector,
                applicationDispatcher: dispatcher ?? new SynchronousApplicationDispatcher(),
                tiltAdapterOptions: options,
                tiltDeviceConnectionService: tiltDeviceService,
                serialPortProvider: serialPortProvider,
                confirmIdleDisconnectAsync: confirmIdleDisconnectAsync);
            return (vm, options, camera, focuser);
        }

        // --- Motorized device connection pane (T10) test rig -------------------------------------------------

        // Deterministic ITiltDeviceTimeSource test double (mirrors TiltDeviceConnectionServiceTests): UtcNow is
        // a plain settable clock and Advance() moves it forward then fires Tick synchronously once — no real
        // Timer, no wall-clock wait. Advancing 31 minutes drives the service's idle prompt deterministically.
        private sealed class FakeTiltDeviceTimeSource : ITiltDeviceTimeSource {
            public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public event EventHandler Tick;

            public void Advance(TimeSpan delta) {
                UtcNow += delta;
                Tick?.Invoke(this, EventArgs.Empty);
            }

            public void Dispose() {
            }
        }

        // Builds the VM around a REAL TiltDeviceConnectionService driven by a substitute ITiltMotionController
        // (via the service's internal factory ctor) and a fake time source — the least invasive substitution
        // seam: no interface extraction or virtuals on the service, production wiring untouched.
        private static (TiltAdapterWizardVM vm, ITiltAdapterOptions options, TiltDeviceConnectionService service,
            ITiltMotionController controller, FakeTiltDeviceTimeSource time, List<string> requestedPresets)
            BuildMotorized(string deviceName = "ASG Electronic EAT - 90mm",
                Func<Task<bool>> confirmIdleDisconnectAsync = null, TiltDevicePositions polledPositions = null,
                ICameraSimulatorOptions cameraSimulatorOptions = null,
                Func<string, string, Task<bool>> confirmSimConfigChangeAsync = null) {
            var profile = Substitute.For<IProfileService>();
            var options = Substitute.For<ITiltAdapterOptions>();
            options.ScrewCount.Returns(4);
            options.MeasurementAverageCount.Returns(1);
            options.DeviceName.Returns(deviceName);

            var controller = Substitute.For<ITiltMotionController>();
            controller.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            controller.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            controller.QueryPositionsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(polledPositions ?? TiltDevicePositions.Unknown));

            var time = new FakeTiltDeviceTimeSource();
            var requestedPresets = new List<string>();
            var service = new TiltDeviceConnectionService(profile, options,
                (presetName, opts) => { requestedPresets.Add(presetName); return controller; }, time);

            var ports = Substitute.For<ISerialPortProvider>();
            ports.GetPortNames(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(new ReadOnlyCollection<string>(new[] { "COM3", "COM7" }));

            var vm = new TiltAdapterWizardVM(
                profileService: profile,
                applicationStatusMediator: Substitute.For<IApplicationStatusMediator>(),
                cameraMediator: Substitute.For<ICameraMediator>(),
                focuserMediator: Substitute.For<IFocuserMediator>(),
                inspector: BuildInspector(),
                applicationDispatcher: new SynchronousApplicationDispatcher(),
                tiltAdapterOptions: options,
                tiltDeviceConnectionService: service,
                serialPortProvider: ports,
                confirmIdleDisconnectAsync: confirmIdleDisconnectAsync,
                cameraSimulatorOptions: cameraSimulatorOptions,
                confirmSimConfigChangeAsync: confirmSimConfigChangeAsync);
            return (vm, options, service, controller, time, requestedPresets);
        }

        [Test]
        public void Constructor_RegistersAsCameraAndFocuserConsumer() {
            var (vm, _, camera, focuser) = Build();

            Assert.Multiple(() => {
                camera.Received(1).RegisterConsumer(vm);
                focuser.Received(1).RegisterConsumer(vm);
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline));
                Assert.That(vm.IsWizardRunning, Is.False);
                Assert.That(vm.IsMeasuring, Is.False);
                Assert.That(vm.IsComplete, Is.False);
                Assert.That(vm.IsOnMeasurementStep, Is.True);
            });
        }

        [Test]
        public void StartCommand_SetsIsWizardRunningAndResetsToBaseline() {
            var (vm, _, _, _) = Build();
            vm.StartCommand.Execute(null);

            Assert.Multiple(() => {
                Assert.That(vm.IsWizardRunning, Is.True);
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline));
            });
        }

        [Test]
        public void RestartCommand_ResetsWizardState() {
            var (vm, _, _, _) = Build();
            vm.StartCommand.Execute(null);
            Assert.That(vm.IsWizardRunning, Is.True);

            vm.RestartCommand.Execute(null);

            Assert.Multiple(() => {
                Assert.That(vm.IsWizardRunning, Is.False);
                Assert.That(vm.IsMeasuring, Is.False);
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline));
            });
        }

        [Test]
        public void SaveAFRuns_DefaultsOff_AndResetsOnRestart() {
            // Saving must be explicitly enabled for each run, so it defaults off and Restart clears it.
            var (vm, _, _, _) = Build();
            Assert.That(vm.SaveAFRuns, Is.False);
            vm.SaveAFRuns = true;
            vm.RestartCommand.Execute(null);
            Assert.That(vm.SaveAFRuns, Is.False);
        }

        [Test]
        public void SaveAFRunsPath_WritesThroughToOptions() {
            var (vm, options, _, _) = Build();
            vm.SaveAFRunsPath = @"C:\temp\tilt";
            options.Received().SaveAFRunsPath = @"C:\temp\tilt";
        }

        [Test]
        public void BaselineInstructions_DoNotRequireAFixedScrewPosition() {
            // Req 1: screw 1 no longer needs to be at any particular clock position.
            var (vm, _, _, _) = Build();
            Assert.That(vm.StepInstructions, Does.Not.Contain("12 o'clock"));
        }

        [Test]
        public void StepInstructions_BaselineLabelsExactScrewCount() {
            var (vm3, _, _, _) = Build(screwCount: 3);
            var (vm4, _, _, _) = Build(screwCount: 4);

            // The Baseline (first) step names the exact screws to label — the screw configuration
            // is already known here, so it must not offer the "(or 1–4 for a 4-screw adapter)" hedge.
            var b3 = vm3.StepInstructions;
            var b4 = vm4.StepInstructions;
            Assert.Multiple(() => {
                Assert.That(b3, Does.Contain("Label your screws 1, 2, and 3 in a consistent clockwise order"));
                Assert.That(b4, Does.Contain("Label your screws 1, 2, 3, and 4 in a consistent clockwise order"));
                Assert.That(b3, Does.Not.Contain("4-screw"));
                Assert.That(b4, Does.Not.Contain("3-screw"));
                Assert.That(b3, Is.Not.EqualTo(b4));
                Assert.That(b3, Does.Contain("baseline"));
            });
        }

        [Test]
        public void AreDevicesConnected_ReflectsCameraAndFocuserInfo() {
            var (vm, _, _, _) = Build();
            Assert.That(vm.AreDevicesConnected, Is.False);
            Assert.That(vm.ConnectionWarningText, Is.Not.Empty);

            vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
            vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });

            Assert.Multiple(() => {
                Assert.That(vm.AreDevicesConnected, Is.True);
                Assert.That(vm.ConnectionWarningText, Is.Empty);
            });
        }

        [Test]
        public void UpdateDeviceInfo_FromBackgroundThread_MarshalsCommandNotificationsThroughDispatcher() {
            // Reported crash: DeviceMediator.Broadcast pushes device info on a background thread, and the
            // CameraInfo/FocuserInfo setters raised CanExecuteChanged on WPF commands off the UI thread
            // ("The calling thread cannot access this object because a different thread owns it."). The command
            // notifications must now flow through the dispatcher so they are marshaled to the UI thread.
            var dispatcher = new RecordingApplicationDispatcher();
            var (vm, _, _, _) = Build(dispatcher: dispatcher);
            var before = dispatcher.PostCount;

            Task.Run(() => {
                vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
                vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });
            }).GetAwaiter().GetResult();

            Assert.That(dispatcher.PostCount - before, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void UpdateDeviceInfo_Camera_MarshalsExactlyOnceAtBoundary() {
            // F15: device-info broadcasts arrive on a background thread; the whole UpdateDeviceInfo body (state
            // mutation + all command/property notifications) must be marshaled to the UI thread as a single unit,
            // not via a separate dispatch per notify site.
            var dispatcher = new RecordingApplicationDispatcher();
            var (vm, _, _, _) = Build(dispatcher: dispatcher);
            var before = dispatcher.PostCount;

            vm.UpdateDeviceInfo(new CameraInfo { Connected = true });

            Assert.That(dispatcher.PostCount - before, Is.EqualTo(1),
                "UpdateDeviceInfo(CameraInfo) should marshal exactly once at the consumer boundary");
        }

        [Test]
        public void UpdateDeviceInfo_MarshalsWithoutBlockingTheBroadcastThread() {
            // NINA hang on exit: ApplicationDeviceConnectionVM.Shutdown() blocks the UI thread inside
            // AsyncContext.Run (which never pumps the WPF dispatcher) while awaiting CameraVM/FocuserVM to
            // disconnect. Those disconnects await their DeviceUpdateTimer, whose in-flight callback is what
            // broadcasts device info into this consumer. Marshaling with a blocking Invoke therefore parks the
            // timer on a UI thread that is itself waiting for the timer — a deadlock. Post, never block.
            var dispatcher = new RecordingApplicationDispatcher();
            var (vm, _, _, _) = Build(dispatcher: dispatcher);

            vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
            vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });

            Assert.Multiple(() => {
                Assert.That(dispatcher.PostCount, Is.EqualTo(2), "each device-info broadcast marshals by posting");
                Assert.That(dispatcher.DispatchCount, Is.Zero,
                    "the device-broadcast path must never block the calling thread on the UI thread");
                Assert.That(vm.CameraInfo.Connected, Is.True);
                Assert.That(vm.FocuserInfo.Connected, Is.True);
            });
        }

        [Test]
        public void RestartCommand_MarshalsStepMeasurementSummaryMutation() {
            // F16: the StepMeasurementSummary mutation (.Clear here) must itself be marshaled to the UI thread, not
            // just the change-notification, because WPF raises CollectionChanged synchronously at the mutation site.
            var dispatcher = new RecordingApplicationDispatcher();
            var (vm, _, _, _) = Build(dispatcher: dispatcher);
            var before = dispatcher.DispatchCount;

            vm.RestartCommand.Execute(null);

            Assert.That(dispatcher.DispatchCount, Is.GreaterThan(before),
                "Restart must marshal the StepMeasurementSummary mutation through the dispatcher");
        }

        [Test]
        public void IsCalibrationValid_RequiresCalibratedFlagAndMatchingScrewCount() {
            var (vm, options, _, _) = Build(screwCount: 3);

            options.IsCalibrated.Returns(false);
            options.CalibratedScrewCount.Returns(3);
            Assert.That(vm.IsCalibrationValid, Is.False);

            options.IsCalibrated.Returns(true);
            options.CalibratedScrewCount.Returns(3);
            Assert.That(vm.IsCalibrationValid, Is.True);

            options.IsCalibrated.Returns(true);
            options.CalibratedScrewCount.Returns(4);
            Assert.That(vm.IsCalibrationValid, Is.False);
        }

        [Test]
        public void HasCurvatureCalibration_TrueWhenSignNonZero() {
            var (vm, options, _, _) = Build();

            options.ScrewInwardCurvatureSign.Returns(0);
            Assert.That(vm.HasCurvatureCalibration, Is.False);

            options.ScrewInwardCurvatureSign.Returns(1);
            Assert.That(vm.HasCurvatureCalibration, Is.True);

            options.ScrewInwardCurvatureSign.Returns(-1);
            Assert.That(vm.HasCurvatureCalibration, Is.True);
        }

        [Test]
        public void CurvatureSignDescription_ArrowMatchesSign_AndCarriesProvenance() {
            var (vm, options, _, _) = Build();

            options.ScrewInwardCurvatureSign.Returns(1);
            options.ScrewInwardCurvatureSignIsMeasured.Returns(true);
            Assert.That(vm.CurvatureSignDescription, Is.EqualTo("↑ (measured)"));

            options.ScrewInwardCurvatureSign.Returns(-1);
            options.ScrewInwardCurvatureSignIsMeasured.Returns(false);
            Assert.That(vm.CurvatureSignDescription, Is.EqualTo("↓ (assumed)"));

            options.ScrewInwardCurvatureSign.Returns(0);
            Assert.That(vm.CurvatureSignDescription, Is.Empty);
        }

        [Test]
        public void ShowRunColumn_TrueWhenAverageCountAboveOne() {
            var (vm, options, _, _) = Build();

            options.MeasurementAverageCount.Returns(1);
            Assert.That(vm.ShowRunColumn, Is.False);

            options.MeasurementAverageCount.Returns(3);
            Assert.That(vm.ShowRunColumn, Is.True);
        }

        [Test]
        public void HasMeasurementFeedback_TrueWhenMeasuringOrSummaryNonEmpty() {
            var (vm, _, _, _) = Build();
            Assert.That(vm.HasMeasurementFeedback, Is.False);
            Assert.That(vm.HasMeasurementResults, Is.False);
        }

        [Test]
        public void SelectedDevice_Preset_SetsAndLocksHardwareFields() {
            var (vm, options, _, _) = Build();

            vm.SelectedDevice = "Neumann CTU XT48";

            Assert.Multiple(() => {
                options.Received().DeviceName = "Neumann CTU XT48";
                options.Received().ScrewCount = 3;
                options.Received().AdjustmentType = TiltAdjustmentType.Screws;
                options.Received().ThreadPitchMicrons = 400;
                options.Received().ScrewRadiusMillimeters = 44;
                Assert.That(vm.IsManualDevice, Is.False);
            });
        }

        [Test]
        public void SelectedDevice_Manual_LeavesFieldsEditableAndDoesNotWriteHardware() {
            var (vm, options, _, _) = Build();
            options.ClearReceivedCalls();

            vm.SelectedDevice = "Manual";

            Assert.Multiple(() => {
                Assert.That(vm.IsManualDevice, Is.True);
                options.DidNotReceive().ScrewCount = Arg.Any<int>();
                options.DidNotReceive().ThreadPitchMicrons = Arg.Any<double>();
                options.DidNotReceive().ScrewRadiusMillimeters = Arg.Any<double>();
            });
        }

        [Test]
        public void SelectedDevice_EatPreset_SetsCalibrationAppliedAmountTo150Steps() {
            // The ASG Electronic EAT stepper adapters default the per-screw calibration applied amount
            // to 150 steps (TiltAdapterDevicePreset.DefaultCalibrationAmount) so the wizard prompts and
            // any future hands-off automation start from a sane stepper-scale amount, not 1 (a turn-scale
            // default that would be a no-op-sized move for a stepper motor).
            var (vm, options, _, _) = Build();
            // NSubstitute's auto-implemented DeviceName property starts at null; the ctor's own
            // re-lock-on-load ApplyDevice(null) call resolves that to Manual and already writes
            // CalibrationAppliedAmount once (null != "Manual", a "device changed" transition). Clear that
            // call out so Received(1) below proves the write is caused by the user-driven SelectedDevice
            // assignment below (a genuine Manual -> EAT transition), not the constructor.
            options.ClearReceivedCalls();

            vm.SelectedDevice = "ASG Electronic EAT - 90mm";

            options.Received(1).CalibrationAppliedAmount = 150.0;
        }

        [Test]
        public void SelectedDevice_ScrewPreset_SetsCalibrationAppliedAmountTo1Turn() {
            var (vm, options, _, _) = Build();
            // See comment in SelectedDevice_EatPreset_SetsCalibrationAppliedAmountTo150Steps above.
            options.ClearReceivedCalls();

            vm.SelectedDevice = "Neumann CTU XT48";

            options.Received(1).CalibrationAppliedAmount = 1.0;
        }

        [Test]
        public void SelectedDevice_Manual_ResetsCalibrationAppliedAmountTo1Turn() {
            // Manual's own DefaultCalibrationAmount is 1.0 (screw-scale), so re-selecting Manual after an
            // EAT preset must reset the applied amount back to 1, not leave a leftover stepper-scale value
            // (e.g. 150) on screen — unlike ScrewCount/ThreadPitchMicrons/etc., which Manual leaves alone.
            var (vm, options, _, _) = Build();
            // NSubstitute's auto-implemented properties remember the LAST value set through the substitute
            // (ClearReceivedCalls only clears call-verification history, not that remembered state) — and the
            // ctor's own ApplyDevice(null) call already left DeviceName at "Manual". So a genuine "away from
            // Manual" transition has to happen first, or "Manual" -> "Manual" below would be a no-op device
            // change (correctly, per the fix) and never write CalibrationAppliedAmount at all.
            vm.SelectedDevice = "ASG Electronic EAT - 90mm";
            options.ClearReceivedCalls();

            vm.SelectedDevice = "Manual";

            options.Received(1).CalibrationAppliedAmount = 1.0;
        }

        [Test]
        public void Constructor_ReAppliesPersistedDevice_DoesNotClobberCalibrationAppliedAmount() {
            // Regression test for the fix: ApplyDevice is called both from the SelectedDevice setter
            // (genuine user change) AND from the constructor's re-lock-on-load path
            // (ApplyDevice(tiltAdapterOptions.DeviceName)). Simulate an app restart where the options
            // store already has an EAT device persisted from a prior session -- the ctor's call
            // re-asserts that SAME device, so it must NOT be treated as a device change and must not
            // clobber the persisted CalibrationAppliedAmount with the preset default.
            var (vm, options, _, _) = Build(
                screwCount: 4,
                configureOptions: o => o.DeviceName.Returns("ASG Electronic EAT - 90mm"));

            options.DidNotReceive().CalibrationAppliedAmount = Arg.Any<double>();
        }

        [Test]
        public void Constructor_ReAppliesPersistedDevice_PreservesUserEditedCalibrationAppliedAmount_WithRealOptions() {
            // End-to-end proof using a REAL TiltAdapterOptions backed by InMemoryPluginOptionsAccessor
            // (not a substitute): a profile that already has an EAT device selected and a user-customized
            // applied amount (220, not the preset default of 150) must survive VM construction.
            var profile = Substitute.For<IProfileService>();
            var store = new InMemoryPluginOptionsAccessor();
            store.SetValueString(nameof(TiltAdapterOptions.DeviceName), "ASG Electronic EAT - 90mm");
            store.SetValueDouble(nameof(TiltAdapterOptions.CalibrationAppliedAmount), 220.0);
            var options = new TiltAdapterOptions(profile, store);
            var camera = Substitute.For<ICameraMediator>();
            var focuser = Substitute.For<IFocuserMediator>();
            var inspector = BuildInspector();

            var vm = new TiltAdapterWizardVM(
                profileService: profile,
                applicationStatusMediator: Substitute.For<IApplicationStatusMediator>(),
                cameraMediator: camera,
                focuserMediator: focuser,
                inspector: inspector,
                applicationDispatcher: new SynchronousApplicationDispatcher(),
                tiltAdapterOptions: options);

            Assert.Multiple(() => {
                Assert.That(options.CalibrationAppliedAmount, Is.EqualTo(220.0));
                Assert.That(vm.CalibrationAppliedAmount, Is.EqualTo(220.0));
            });
        }

        [Test]
        public void CalibrationAppliedAmount_Getter_ResolvesUnsetToPresetDefault() {
            // -1 (unset) must resolve to the currently-selected preset's DefaultCalibrationAmount.
            var (vm, _, _, _) = Build(configureOptions: o => {
                o.DeviceName.Returns("ASG Electronic EAT - 90mm");
                o.CalibrationAppliedAmount.Returns(-1.0);
            });

            Assert.That(vm.CalibrationAppliedAmount, Is.EqualTo(150.0));
        }

        [Test]
        public void CalibrationAppliedAmount_Getter_ReturnsZeroAsARealValue() {
            // Boundary check on the getter's "raw >= 0" resolution branch: 0 is a real (if unusual)
            // persisted value and must be returned as-is, not treated as "unset" like -1 is.
            var (vm, _, _, _) = Build(configureOptions: o => {
                o.DeviceName.Returns("Manual");
                o.CalibrationAppliedAmount.Returns(0.0);
            });

            Assert.That(vm.CalibrationAppliedAmount, Is.EqualTo(0.0));
        }

        // --- Failure-choice panel (Change 3) ---

        [Test]
        public void StepIsAtBaseline_TrueOnlyForBaselineEquivalentSteps() {
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.StepIsAtBaseline(WizardStep.Baseline), Is.True);
                Assert.That(TiltAdapterWizardVM.StepIsAtBaseline(WizardStep.ReBaseline1), Is.True);
                Assert.That(TiltAdapterWizardVM.StepIsAtBaseline(WizardStep.ReBaseline2), Is.True);
                Assert.That(TiltAdapterWizardVM.StepIsAtBaseline(WizardStep.Complete), Is.True);
                Assert.That(TiltAdapterWizardVM.StepIsAtBaseline(WizardStep.ReBaseline3), Is.True);
                Assert.That(TiltAdapterWizardVM.StepIsAtBaseline(WizardStep.AllInward), Is.False);
                Assert.That(TiltAdapterWizardVM.StepIsAtBaseline(WizardStep.Screw1), Is.False);
                Assert.That(TiltAdapterWizardVM.StepIsAtBaseline(WizardStep.Screw2), Is.False);
            });
        }

        [Test]
        public void BaselineRecoveryText_PerturbedSteps_DescribeUndoingTheMove() {
            // 3-screw: undo only the moved screw (CW/CCW vocabulary — never "inward/outward").
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.AllInward, 3, false, 1.0), Does.Contain("ALL screws back COUNTER-CLOCKWISE"));
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.Screw1, 3, false, 1.0), Does.Contain("screw 1 back COUNTER-CLOCKWISE"));
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.Screw2, 3, false, 1.0), Does.Contain("screw 2 back COUNTER-CLOCKWISE"));
            });
            // 4-screw: the opposing screw is undone too.
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.Screw1, 4, false, 1.0), Does.Contain("screw 3 back CLOCKWISE"));
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.Screw2, 4, false, 1.0), Does.Contain("screw 4 back CLOCKWISE"));
            });
            // Steppers: signed steps (U+2212 minus for undo).
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.AllInward, 3, true, 2.0), Does.Contain("−2 steps to every motor"));
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.Screw1, 4, true, 2.0), Does.Contain("−2 steps to motor 1").And.Contain("+2 steps to motor 3"));
            });
        }

        [Test]
        public void BaselineRecoveryText_BaselineSteps_SayAlreadyAtBaseline() {
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.Baseline, 3, false, 1.0), Does.Contain("already be at the baseline"));
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.ReBaseline1, 3, false, 1.0), Does.Contain("already be at the baseline"));
                Assert.That(TiltAdapterWizardVM.BaselineRecoveryText(WizardStep.ReBaseline2, 4, false, 1.0), Does.Contain("already be at the baseline"));
            });
        }

        // --- 4-step / 6-step sequencing, reworded prompts, sweep summary (Task 8) ---

        [Test]
        public void GetMeasurementSteps_FourStepFlowSkipsCurvatureSteps() {
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.GetMeasurementSteps(measureCurvature: true, measureFinalRebaseline: false), Is.EqualTo(new[] {
                    WizardStep.Baseline, WizardStep.AllInward, WizardStep.ReBaseline1,
                    WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 }));
                Assert.That(TiltAdapterWizardVM.GetMeasurementSteps(measureCurvature: false, measureFinalRebaseline: false), Is.EqualTo(new[] {
                    WizardStep.Baseline, WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 }));
            });
        }

        // --- Task 6: optional measured final re-baseline appended to either base flow ---

        [Test]
        public void GetMeasurementSteps_WithFinalRebaseline_AppendsReBaseline3AsTheLastStep() {
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.GetMeasurementSteps(measureCurvature: true, measureFinalRebaseline: true), Is.EqualTo(new[] {
                    WizardStep.Baseline, WizardStep.AllInward, WizardStep.ReBaseline1,
                    WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2, WizardStep.ReBaseline3 }));
                Assert.That(TiltAdapterWizardVM.GetMeasurementSteps(measureCurvature: false, measureFinalRebaseline: true), Is.EqualTo(new[] {
                    WizardStep.Baseline, WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2, WizardStep.ReBaseline3 }));
            });
        }

        [Test]
        public void StepInstructionsText_Screws_UsesClockwiseWordingAndAmount() {
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.Baseline, 3, isStepper: false, appliedAmount: 1.0),
                    Does.Contain("consistent clockwise order").And.Contain("starting position"));
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.AllInward, 3, false, 1.0),
                    Does.Contain("ALL screws CLOCKWISE (tighten) exactly 1 full turn"));
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.ReBaseline1, 3, false, 1.0),
                    Does.Contain("COUNTER-CLOCKWISE (loosen) exactly 1 full turn"));
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.Screw1, 4, false, 1.0),
                    Does.Contain("screw 1 CLOCKWISE").And.Contain("screw 3 COUNTER-CLOCKWISE"));
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.Screw2, 3, false, 1.5),
                    Does.Contain("screw 2 CLOCKWISE exactly 1.5 turns"));
                // Task 6: the optional measured final re-baseline undoes screw 2's move (motor/screw 2 and,
                // 4-screw, its opposite motor/screw 4) -- same shape as ReBaseline2's undo of screw 1, mirrored.
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.ReBaseline3, 3, false, 1.0),
                    Does.Contain("screw 2 back COUNTER-CLOCKWISE").And.Contain("returning to the baseline position"));
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.ReBaseline3, 4, false, 1.0),
                    Does.Contain("screw 2 back COUNTER-CLOCKWISE").And.Contain("screw 4 back CLOCKWISE"));
            });
        }

        [Test]
        public void StepInstructionsText_Steppers_UsesSignedSteps() {
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.AllInward, 3, isStepper: true, appliedAmount: 2.0),
                    Does.Contain("+2 steps to EVERY motor"));
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.ReBaseline2, 3, true, 2.0),
                    Does.Contain("−2 steps to motor 1"));
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.Screw1, 4, true, 2.0),
                    Does.Contain("+2 steps to motor 1").And.Contain("−2 steps to motor 3"));
                // Task 6: stepper wording for the optional measured final re-baseline (motor 2 / motor 4).
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.ReBaseline3, 4, true, 2.0),
                    Does.Contain("−2 steps to motor 2").And.Contain("+2 steps to motor 4"));
                Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.ReBaseline3, 3, true, 2.0),
                    Does.Contain("−2 steps to motor 2").And.Not.Contain("motor 4"));
            });
        }

        [TestCase(WizardStep.Baseline, "Baseline Measurement")]
        [TestCase(WizardStep.AllInward, "All Screws Inward")]
        [TestCase(WizardStep.ReBaseline1, "Return to Baseline")]
        [TestCase(WizardStep.Screw1, "Move Screw 1")]
        [TestCase(WizardStep.ReBaseline2, "Return to Baseline")]
        [TestCase(WizardStep.Screw2, "Move Screw 2")]
        [TestCase(WizardStep.Complete, "Calibration Complete")]
        [TestCase(WizardStep.ReBaseline3, "Return to Baseline")]
        public void StepTitleText_IsShortPerStepHeader(WizardStep step, string expected) {
            Assert.That(TiltAdapterWizardVM.StepTitleText(step), Is.EqualTo(expected));
        }

        [Test]
        public void DeviceStepInstructionsText_AutoRunning_DropsClickImperatives() {
            // Baseline: manual/connected wording tells the user to click; auto-running does not.
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.DeviceStepInstructionsText(WizardStep.Baseline, 0, autoRunning: false),
                    Does.Contain("click Run Measurement"));
                Assert.That(TiltAdapterWizardVM.DeviceStepInstructionsText(WizardStep.Baseline, 0, autoRunning: true),
                    Does.Contain("Running automatically").And.Not.Contain("click"));

                // A move step: not auto-running tells the user to click; auto-running is pure status.
                Assert.That(TiltAdapterWizardVM.DeviceStepInstructionsText(WizardStep.Screw1, 150, autoRunning: false),
                    Does.Contain("Click Run Measurement"));
                Assert.That(TiltAdapterWizardVM.DeviceStepInstructionsText(WizardStep.Screw1, 150, autoRunning: true),
                    Does.StartWith("Running automatically").And.Not.Contain("Click"));
            });
        }

        [Test]
        public void DeviceStepInstructionsText_Complete_NoMoveOnlyWhenMeasuredFinalRebaseline() {
            // Default (measuredFinalRebaseline: false, the implicit default) -- unchanged from before Task 6:
            // Complete's own restore move is described.
            Assert.Multiple(() => {
                Assert.That(TiltAdapterWizardVM.DeviceStepInstructionsText(WizardStep.Complete, 150, autoRunning: false),
                    Does.Contain("diagonal-B (restore)").And.Contain("Click Run Measurement"));
                Assert.That(TiltAdapterWizardVM.DeviceStepInstructionsText(WizardStep.Complete, 150, autoRunning: true),
                    Does.Contain("diagonal-B (restore)"));

                // measuredFinalRebaseline: true -- ReBaseline3 (an earlier ordinary step in this same run)
                // already sent and measured the restore, so Complete becomes pure status/no-op wording, with
                // no move description and no second restore mentioned.
                Assert.That(TiltAdapterWizardVM.DeviceStepInstructionsText(WizardStep.Complete, 150, autoRunning: false, measuredFinalRebaseline: true),
                    Is.EqualTo("Click Run Measurement to continue.").And.Not.Contain("diagonal-B"));
                Assert.That(TiltAdapterWizardVM.DeviceStepInstructionsText(WizardStep.Complete, 150, autoRunning: true, measuredFinalRebaseline: true),
                    Is.EqualTo("Running automatically — measuring…").And.Not.Contain("diagonal-B"));
            });
        }

        [Test]
        public void StepProgressDisplay_ReflectsActiveStepCount_4vs6() {
            // Default (curvature off) is a 4-step run; the counter reads "Step 1 of 4" at Baseline.
            var (vm4, _, _, _) = Build();
            Assert.That(vm4.StepProgressDisplay, Is.EqualTo("Step 1 of 4"));
            Assert.That(vm4.StepTitle, Is.EqualTo("Baseline Measurement"));

            // Enabling curvature measurement makes it a 6-step run once started.
            var (vm6, _, _, _) = Build(configureOptions: o => o.MeasureCurvatureDuringCalibration.Returns(true));
            vm6.StartCommand.Execute(null);
            Assert.That(vm6.StepProgressDisplay, Is.EqualTo("Step 1 of 6"));
        }

        // Companion to the replay counter test: the LIVE path's counter must advance too. The check above only
        // ever asserted the Baseline value, so a regression in NextStep's advance (or in the array it indexes)
        // would have gone unnoticed. Drives the step advance directly -- the live measurement itself needs a
        // real inspector, but NextStep is the single point every live path funnels through to change the step.
        [Test]
        public void StepProgressDisplay_AdvancesThroughTheLiveRun() {
            var (vm, _, _, _) = Build();
            vm.StartCommand.Execute(null);

            var progress = new List<string> { vm.StepProgressDisplay };
            for (int i = 0; i < 3; i++) {
                vm.NextStep();
                progress.Add(vm.StepProgressDisplay);
            }

            Assert.Multiple(() => {
                Assert.That(progress, Is.EqualTo(new[] { "Step 1 of 4", "Step 2 of 4", "Step 3 of 4", "Step 4 of 4" }));
                // One more advance lands on Complete, whose panel carries its own header instead of a counter.
                vm.NextStep();
                Assert.That(vm.IsComplete, Is.True);
                Assert.That(vm.StepProgressDisplay, Is.Empty);
            });
        }

        [Test]
        public void BuildSweepSummary_CountsSweepsAndImages() {
            // 4 steps × 2 averaged measurements = 8 sweeps; profile: 4 offset steps, 1 frame, amp 2 => 17 images/run.
            var text = TiltAdapterWizardVM.BuildSweepSummary(4, 2, -1, -1, 2, 4, 1);
            Assert.Multiple(() => {
                Assert.That(text, Does.Contain("~17 images"));
                Assert.That(text, Does.Contain("8 sweeps"));
                Assert.That(text, Does.Contain("136 images total"));
            });
            Assert.That(TiltAdapterWizardVM.BuildSweepSummary(4, 1, -1, -1, 2, 0, 0), Is.Empty);
        }

        [Test]
        public void ApplyManualCalibration_WritesCalibrationState_CwTowardObjectiveStoresPhysicalAngles() {
            // σ = −1 at the standard focuser is m = −1 — a CW turn drives the plate toward the
            // objective, RAISING best focus at that screw, so the response direction points along the
            // screw and the stored angles coincide with the physical angles the user typed: 30° places
            // the clockwise-numbered screws at 30/150/270.
            var (vm, options, _, _) = Build(screwCount: 3);
            options.ScrewInwardCurvatureSign.Returns(-1);
            vm.ManualScrew1AngleDegrees = 30;
            vm.ManualNumberingClockwise = true;

            vm.ApplyManualCalibration();

            Assert.Multiple(() => {
                options.Received().Screw1AngleDegrees = 30;
                options.Received().Screw2AngleDegrees = 150;
                options.Received().Screw3AngleDegrees = 270;
                options.Received().Screw4AngleDegrees = double.NaN;
                options.Received().CalibratedScrewCount = 3;
                options.Received().IsCalibrated = true;
                options.Received().ScrewInwardCurvatureSignIsMeasured = false;
                options.Received().CalibrationIsManual = true;
                // A manual entry must invalidate the previous wizard run's measured hardware (the
                // inspector's pitch-mismatch warning compares against these): -1 = the unset sentinel.
                options.Received().LastMeasuredThreadPitchMicrons = -1;
                options.Received().LastMeasuredStepperStepSizeMicrons = -1;
            });
        }

        [Test]
        public void ApplyManualCalibration_CwTowardCamera_ConvertsPhysicalToResponseConvention() {
            // σ = +1 at the standard focuser is m = +1 — a CW turn drives the plate toward the camera,
            // LOWERING best focus at that screw, so the tilt gradient responds opposite the physical
            // screw direction and the stored angles are the typed physical angle + 180°. Wizard runs
            // persist response-convention angles and the guidance math consumes them, so a manual
            // entry must convert or its arrows would invert on these rigs.
            var (vm, options, _, _) = Build(screwCount: 3);
            options.ScrewInwardCurvatureSign.Returns(1);
            vm.ManualScrew1AngleDegrees = 30;
            vm.ManualNumberingClockwise = true;

            vm.ApplyManualCalibration();

            Assert.Multiple(() => {
                options.Received().Screw1AngleDegrees = 210;
                options.Received().Screw2AngleDegrees = 330;
                options.Received().Screw3AngleDegrees = 90;
                options.Received().Screw4AngleDegrees = double.NaN;
                options.Received().CalibratedScrewCount = 3;
                options.Received().IsCalibrated = true;
            });
        }

        [Test]
        public void ApplyManualCalibration_NonFiniteAngle_WritesNothing() {
            // WPF double bindings can push NaN; the guard must refuse to persist any calibration
            // state (IsCalibrated over garbage angles would corrupt guidance until recalibrated).
            var (vm, options, _, _) = Build(screwCount: 3);
            options.ScrewInwardCurvatureSign.Returns(1);
            vm.ManualScrew1AngleDegrees = double.NaN;
            options.ClearReceivedCalls();

            vm.ApplyManualCalibration();

            Assert.Multiple(() => {
                options.DidNotReceive().Screw1AngleDegrees = Arg.Any<double>();
                options.DidNotReceive().Screw2AngleDegrees = Arg.Any<double>();
                options.DidNotReceive().Screw3AngleDegrees = Arg.Any<double>();
                options.DidNotReceive().Screw4AngleDegrees = Arg.Any<double>();
                options.DidNotReceive().CalibratedScrewCount = Arg.Any<int>();
                options.DidNotReceive().IsCalibrated = Arg.Any<bool>();
                options.DidNotReceive().ScrewInwardCurvatureSignIsMeasured = Arg.Any<bool>();
                options.DidNotReceive().CalibrationIsManual = Arg.Any<bool>();
                options.DidNotReceive().LastMeasuredThreadPitchMicrons = Arg.Any<double>();
                options.DidNotReceive().LastMeasuredStepperStepSizeMicrons = Arg.Any<double>();
            });
        }

        // ---- Screw-angle display shows the PHYSICAL image position (readout + wizard diagram) --------------
        //
        // The persisted Screw{N}AngleDegrees are RESPONSE-convention angles (physical + 180° on
        // "CW moves adapter toward the camera" rigs — m = σ·sign(k) = +1). Both the calibration readout and the
        // wizard diagram are labelled image-space ("0° points up; image as shown in NINA"), and must
        // therefore display the PHYSICAL angle — matching the Manual Calibration Entry field — not the
        // raw stored value. Regression guard for docs/tilt-wizard-diagram-orientation-design.md.

        private static (TiltAdapterWizardVM vm, ITiltAdapterOptions options) BuildCalibrated(
            int sign, double s1, double s2, double s3, double s4 = double.NaN, int screwCount = 3) {
            var (vm, options, _, _) = Build(screwCount: screwCount, configureOptions: o => {
                o.IsCalibrated.Returns(true);
                o.CalibratedScrewCount.Returns(screwCount);
                o.ScrewInwardCurvatureSign.Returns(sign);
                o.Screw1AngleDegrees.Returns(s1);
                o.Screw2AngleDegrees.Returns(s2);
                o.Screw3AngleDegrees.Returns(s3);
                o.Screw4AngleDegrees.Returns(s4);
            });
            return (vm, options);
        }

        [Test]
        public void RebuildDiagram_OffsetRig_PlacesPhysicalTopScrewAtCanvasTop() {
            // m = +1 rig (σ = +1 at the standard focuser): screw 1 is physically at the TOP (physical
            // 0°), stored as 0+180 = 180°. The diagram must draw it at canvas-top (cy = 100 − 75·cos0
            // = 25 → Y = 25 − 12 = 13), NOT at the bottom (the raw-stored 180° would give cy = 175).
            // Screws 2/3 are physically 120/240.
            var (vm, _) = BuildCalibrated(sign: 1, s1: 180, s2: 300, s3: 60);

            var screw1 = vm.ScrewDiagramItems.Single(i => i.Number == 1);
            Assert.Multiple(() => {
                Assert.That(screw1.AngleDegrees, Is.EqualTo(0.0).Within(1e-9), "physical angle");
                Assert.That(screw1.Y, Is.EqualTo(13.0).Within(0.5), "canvas-top, not bottom");
                Assert.That(screw1.X, Is.EqualTo(88.0).Within(0.5), "horizontally centred");
            });
        }

        [Test]
        public void RebuildDiagram_NoOffsetRig_PlacesScrewsAtStoredAngles() {
            // m = −1 rig (σ = −1 at the standard focuser): stored == physical, so the diagram plots the
            // stored angles directly. Screw 1 stored 90° → right edge (cx = 175, cy = 100 → X = 163,
            // Y = 88). Guards against a double 180° offset.
            var (vm, _) = BuildCalibrated(sign: -1, s1: 90, s2: 210, s3: 330);

            var screw1 = vm.ScrewDiagramItems.Single(i => i.Number == 1);
            Assert.Multiple(() => {
                Assert.That(screw1.AngleDegrees, Is.EqualTo(90.0).Within(1e-9));
                Assert.That(screw1.X, Is.EqualTo(163.0).Within(0.5));
                Assert.That(screw1.Y, Is.EqualTo(88.0).Within(0.5));
            });
        }

        [Test]
        public void PhysicalScrewAngles_OffsetRig_ConvertStoredResponseAnglesToPhysical() {
            // Readout binds these; on an m = +1 rig they must be the stored angle − 180° (self-inverse).
            var (vm, _) = BuildCalibrated(sign: 1, s1: 180, s2: 300, s3: 60);

            Assert.Multiple(() => {
                Assert.That(vm.PhysicalScrew1AngleDegrees, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(vm.PhysicalScrew2AngleDegrees, Is.EqualTo(120.0).Within(1e-9));
                Assert.That(vm.PhysicalScrew3AngleDegrees, Is.EqualTo(240.0).Within(1e-9));
            });
        }

        [Test]
        public void PhysicalScrewAngles_NoOffsetRig_EqualStoredAngles() {
            var (vm, _) = BuildCalibrated(sign: -1, s1: 90, s2: 210, s3: 330);

            Assert.Multiple(() => {
                Assert.That(vm.PhysicalScrew1AngleDegrees, Is.EqualTo(90.0).Within(1e-9));
                Assert.That(vm.PhysicalScrew2AngleDegrees, Is.EqualTo(210.0).Within(1e-9));
                Assert.That(vm.PhysicalScrew3AngleDegrees, Is.EqualTo(330.0).Within(1e-9));
            });
        }

        [Test]
        public void ScrewInwardCurvatureSignChange_RefreshesPhysicalAnglesAndDiagram() {
            // Changing the adapter-direction setting flips the physical interpretation by 180°, so both
            // the readout properties and the diagram must refresh when ScrewInwardCurvatureSign changes.
            var (vm, options) = BuildCalibrated(sign: -1, s1: 0, s2: 120, s3: 240);
            // Built on m = −1 (no offset): screw 1 (stored 0° = physical top) starts at canvas-top.
            Assert.That(vm.ScrewDiagramItems.Single(i => i.Number == 1).Y, Is.EqualTo(13.0).Within(0.5));

            var raised = new System.Collections.Generic.List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            options.ScrewInwardCurvatureSign.Returns(1);
            options.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                options, new System.ComponentModel.PropertyChangedEventArgs(nameof(ITiltAdapterOptions.ScrewInwardCurvatureSign)));

            Assert.Multiple(() => {
                Assert.That(raised, Does.Contain(nameof(TiltAdapterWizardVM.PhysicalScrew1AngleDegrees)));
                Assert.That(raised, Does.Contain(nameof(TiltAdapterWizardVM.PhysicalScrew2AngleDegrees)));
                // Now interpreted as an m = +1 rig: physical = 0 + 180 = 180° → screw 1 moves to the bottom.
                Assert.That(vm.ScrewDiagramItems.Single(i => i.Number == 1).Y, Is.EqualTo(163.0).Within(0.5));
            });
        }

        [Test]
        public void StepDescription_UsesRotationGlyphs() {
            // Summary-row glyphs must match the guidance legend (⟳ = clockwise / + steps,
            // ⟲ = counter-clockwise / − steps): the perturbation steps are CLOCKWISE moves per
            // StepInstructionsText, so they carry ⟳; the re-baseline undo moves carry ⟲.
            var (vm3, _, _, _) = Build(screwCount: 3);
            var (vm4, _, _, _) = Build(screwCount: 4);
            Assert.Multiple(() => {
                Assert.That(vm3.StepDescription(WizardStep.Baseline), Is.EqualTo("Baseline"));
                Assert.That(vm3.StepDescription(WizardStep.AllInward), Is.EqualTo("All screws ⟳"));
                Assert.That(vm3.StepDescription(WizardStep.ReBaseline1), Is.EqualTo("Re-baseline (all ⟲)"));
                Assert.That(vm3.StepDescription(WizardStep.Screw1), Is.EqualTo("Screw 1 ⟳"));
                Assert.That(vm3.StepDescription(WizardStep.ReBaseline2), Is.EqualTo("Re-baseline (Screw 1 ⟲)"));
                Assert.That(vm3.StepDescription(WizardStep.Screw2), Is.EqualTo("Screw 2 ⟳"));
                Assert.That(vm4.StepDescription(WizardStep.Screw1), Is.EqualTo("Screw 1 ⟳, Screw 3 ⟲"));
                Assert.That(vm4.StepDescription(WizardStep.ReBaseline2), Is.EqualTo("Re-baseline (Screw 1 ⟲, Screw 3 ⟳)"));
                Assert.That(vm4.StepDescription(WizardStep.Screw2), Is.EqualTo("Screw 2 ⟳, Screw 4 ⟲"));
                // Task 6: the optional measured final re-baseline undoes screw 2's move.
                Assert.That(vm3.StepDescription(WizardStep.ReBaseline3), Is.EqualTo("Re-baseline (Screw 2 ⟲)"));
                Assert.That(vm4.StepDescription(WizardStep.ReBaseline3), Is.EqualTo("Re-baseline (Screw 2 ⟲, Screw 4 ⟳)"));
            });
        }

        [Test]
        public void WizardSweepSummary_RefreshesOnInPlaceFocuserSettingEdits() {
            // The summary reads the ACTIVE profile's FocuserSettings; editing those values in place
            // (no profile swap) must re-raise it, filtered to the two properties it consumes.
            var profileService = Substitute.For<IProfileService>();
            var (vm, _, _, _) = Build(profileService: profileService);
            var settings = profileService.ActiveProfile.FocuserSettings;

            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            settings.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                settings, new System.ComponentModel.PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusInitialOffsetSteps)));
            Assert.That(raised, Does.Contain(nameof(vm.WizardSweepSummary)));

            raised.Clear();
            settings.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                settings, new System.ComponentModel.PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusNumberOfFramesPerPoint)));
            Assert.That(raised, Does.Contain(nameof(vm.WizardSweepSummary)));

            raised.Clear();
            settings.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                settings, new System.ComponentModel.PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusExposureTime)));
            Assert.That(raised, Does.Not.Contain(nameof(vm.WizardSweepSummary)), "unrelated focuser settings must not re-raise the summary");
        }

        [Test]
        public void WizardSweepSummary_RehooksFocuserSettingsOnProfileChange() {
            var profileService = Substitute.For<IProfileService>();
            var (vm, _, _, _) = Build(profileService: profileService);
            var oldSettings = profileService.ActiveProfile.FocuserSettings;

            var newProfile = Substitute.For<IProfile>();
            profileService.ActiveProfile.Returns(newProfile);
            profileService.ProfileChanged += Raise.Event<EventHandler>(profileService, EventArgs.Empty);
            var newSettings = newProfile.FocuserSettings;

            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            newSettings.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                newSettings, new System.ComponentModel.PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusInitialOffsetSteps)));
            Assert.That(raised, Does.Contain(nameof(vm.WizardSweepSummary)), "the new profile's settings must be hooked");

            raised.Clear();
            oldSettings.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                oldSettings, new System.ComponentModel.PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusInitialOffsetSteps)));
            Assert.That(raised, Does.Not.Contain(nameof(vm.WizardSweepSummary)), "the previous profile's settings must be unhooked");
        }

        [Test]
        public void Constructor_PrefillsManualAngleAsPhysical() {
            // Stored calibration angles are response-convention; the manual-entry field holds the
            // PHYSICAL image angle, so the pre-fill must convert back (the conversion is self-inverse).
            var (vmOffset, _, _, _) = Build(screwCount: 3, configureOptions: o => {
                o.Screw1AngleDegrees.Returns(210.0);
                o.ScrewInwardCurvatureSign.Returns(1); // m = +1 ⇒ 180° offset
            });
            Assert.That(vmOffset.ManualScrew1AngleDegrees, Is.EqualTo(30).Within(1e-9));

            var (vmNoOffset, _, _, _) = Build(screwCount: 3, configureOptions: o => {
                o.Screw1AngleDegrees.Returns(210.0);
                o.ScrewInwardCurvatureSign.Returns(-1); // m = −1 ⇒ identity
            });
            Assert.That(vmNoOffset.ManualScrew1AngleDegrees, Is.EqualTo(210).Within(1e-9));
        }

        [Test]
        public void CwMovesAdapterTowardObjective_RoundTripsThroughSign() {
            var (vm, options, _, _) = Build();

            options.ScrewInwardCurvatureSign.Returns(0);
            vm.CwMovesAdapterTowardObjective = true;
            Assert.Multiple(() => {
                options.Received().ScrewInwardCurvatureSign = TiltScrewGeometry.CurvatureSignForCwDirection(true);
                options.Received().ScrewInwardCurvatureSignIsMeasured = false;
            });

            vm.CwMovesAdapterTowardObjective = false;
            options.Received().ScrewInwardCurvatureSign = TiltScrewGeometry.CurvatureSignForCwDirection(false);

            // The getter maps a stored sign back through the pinned empirical constant.
            options.ScrewInwardCurvatureSign.Returns(TiltScrewGeometry.CurvatureSignForCwDirection(true));
            Assert.That(vm.CwMovesAdapterTowardObjective, Is.True);
            options.ScrewInwardCurvatureSign.Returns(TiltScrewGeometry.CurvatureSignForCwDirection(false));
            Assert.That(vm.CwMovesAdapterTowardObjective, Is.False);
        }

        [Test]
        public void MeasurementFailureChoice_DefaultsHidden_AndClearsOnRestart() {
            var (vm, _, _, _) = Build();
            Assert.That(vm.HasMeasurementFailureChoice, Is.False);
            // At the Baseline step the recovery guidance reports the user is already at baseline.
            Assert.That(vm.IsCurrentStepAtBaseline, Is.True);
            Assert.That(vm.BaselineRecoveryInstructions, Does.Contain("already be at the baseline"));

            vm.RestartCommand.Execute(null);
            Assert.That(vm.HasMeasurementFailureChoice, Is.False);
        }

        [Test]
        public void RetryMeasurementCommand_CannotExecute_WithoutAFailureChoice() {
            var (vm, _, _, _) = Build();
            // No failure has occurred, so the retry button is disabled even though we're on a measurement step.
            Assert.That(vm.RetryMeasurementCommand.CanExecute(null), Is.False);
        }

        // --- Measurement-consistency warning lifetime ---

        private static readonly List<(double A, double B, double Mean)> InconsistentReadings =
            new List<(double A, double B, double Mean)> { (0.0, 0.0, 1000.0), (0.1, 0.1, 1000.0) };

        [Test]
        public void MeasurementConsistencyWarning_SurvivesStepAdvance() {
            // The warning is raised at the end of a successful averaged measurement, after which the wizard
            // immediately advances to the next step. If NextStep cleared it, it could never be seen — it must
            // survive the advance and show on the next step's screen.
            var (vm, _, _, _) = Build();
            vm.StartCommand.Execute(null);

            // Real warning path: two repeats whose (A, B) deviate from the average by more than the 0.02 threshold.
            vm.AppendSummaryRows(InconsistentReadings, "Baseline", latestModel: null, count: 2, avgA: 0.05, avgB: 0.05);
            Assert.That(vm.HasMeasurementConsistencyWarning, Is.True, "precondition: the averaged measurement raised the warning");

            vm.NextStep();

            Assert.Multiple(() => {
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Screw1));
                Assert.That(vm.HasMeasurementConsistencyWarning, Is.True, "the warning must survive the step advance");
                Assert.That(vm.MeasurementConsistencyWarningText, Does.Contain("max deviation"));
            });
        }

        [Test]
        public void MeasurementConsistencyWarning_ClearedWhenANewRunStarts() {
            // The surviving warning describes the previous step's repeats; starting a fresh wizard run (like
            // starting the next measurement) must clear it.
            var (vm, _, _, _) = Build();
            vm.AppendSummaryRows(InconsistentReadings, "Baseline", latestModel: null, count: 2, avgA: 0.05, avgB: 0.05);
            Assert.That(vm.HasMeasurementConsistencyWarning, Is.True);

            vm.StartCommand.Execute(null);

            Assert.Multiple(() => {
                Assert.That(vm.HasMeasurementConsistencyWarning, Is.False);
                Assert.That(vm.MeasurementConsistencyWarningText, Is.Empty);
            });
        }

        // --- Profile switch refreshes direction controls (real options + broadcast reload) ---

        [Test]
        public void ProfileChanged_RefreshesDirectionControlsAndManualPrefill() {
            // TiltAdapterOptions reloads its values on ProfileChanged and raises a broadcast PropertyChanged that
            // the VM's per-name filters miss. The VM's own ProfileChanged handler must therefore re-raise the
            // direction/provenance/prompt wrappers and re-run the manual-entry pre-fill, or the controls keep
            // showing the previous profile's state (and re-selecting the stale value would overwrite the new
            // profile's measured sign).
            var profileService = Substitute.For<IProfileService>();
            var store = new InMemoryPluginOptionsAccessor();
            var options = new TiltAdapterOptions(profileService, store);
            // Old profile: measured "CW moves the adapter toward the objective" (stored sign −1 per the empirical
            // anchor) with a calibrated screw 1 at stored angle 210°. m = σ·sign(k) = −1 takes no offset, so the
            // physical angle is 210° too.
            options.ScrewInwardCurvatureSign = -1;
            options.ScrewInwardCurvatureSignIsMeasured = true;
            options.Screw1AngleDegrees = 210.0;
            options.IsCalibrated = true;
            options.CalibratedScrewCount = 3;

            var vm = new TiltAdapterWizardVM(
                profileService: profileService,
                applicationStatusMediator: Substitute.For<IApplicationStatusMediator>(),
                cameraMediator: Substitute.For<ICameraMediator>(),
                focuserMediator: Substitute.For<IFocuserMediator>(),
                inspector: BuildInspector(),
                applicationDispatcher: new SynchronousApplicationDispatcher(),
                tiltAdapterOptions: options);
            Assert.Multiple(() => {
                Assert.That(vm.CwMovesAdapterTowardObjective, Is.True, "precondition: old profile's direction");
                Assert.That(vm.ManualScrew1AngleDegrees, Is.EqualTo(210).Within(1e-9), "precondition: ctor pre-fill");
            });

            // The new profile stores the opposite (assumed) direction and a different screw-1 angle.
            store.Clear();
            store.SetValueInt32(nameof(ITiltAdapterOptions.ScrewInwardCurvatureSign), 1);
            store.SetValueBoolean(nameof(ITiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured), false);
            store.SetValueDouble(nameof(ITiltAdapterOptions.Screw1AngleDegrees), 90.0);

            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            profileService.ProfileChanged += Raise.Event<EventHandler>(profileService, EventArgs.Empty);

            Assert.Multiple(() => {
                Assert.That(vm.CwMovesAdapterTowardObjective, Is.False, "the getter reads the new profile's sign");
                Assert.That(raised, Does.Contain(nameof(vm.CwMovesAdapterTowardObjective)));
                Assert.That(raised, Does.Contain(nameof(vm.CurvatureSignDescription)));
                Assert.That(raised, Does.Contain(nameof(vm.CurvatureSignProvenance)));
                Assert.That(raised, Does.Contain(nameof(vm.CwDirectionLabel)));
                Assert.That(raised, Does.Contain(nameof(vm.HasCurvatureCalibration)));
                Assert.That(raised, Does.Contain(nameof(vm.IsCalibrationValid)));
                Assert.That(raised, Does.Contain(nameof(vm.StepInstructions)));
                Assert.That(raised, Does.Contain(nameof(vm.BaselineRecoveryInstructions)));
                // The manual pre-fill re-runs against the new profile: sign +1 is m = +1, so stored 90° is
                // physical 270°.
                Assert.That(vm.ManualScrew1AngleDegrees, Is.EqualTo(270).Within(1e-9));
                Assert.That(raised, Does.Contain(nameof(vm.ManualScrew1AngleDegrees)));
            });
        }

        [Test]
        public void ProfileChanged_RaisesCalibrationAppliedAmountDisplay() {
            // A profile swap can flip AdjustmentType (screws ↔ steppers), changing the units (turns vs
            // steps) in CalibrationAppliedAmountDisplay. This guards that a ProfileChanged refreshes that
            // label; it is currently satisfied transitively by RaiseHardwareSummaryChanged() in the handler,
            // so this test pins the behavior in case a refactor ever drops that path.
            var profileService = Substitute.For<IProfileService>();
            var (vm, _, _, _) = Build(profileService: profileService);
            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            profileService.ProfileChanged += Raise.Event<EventHandler>(profileService, EventArgs.Empty);
            Assert.Multiple(() => {
                // Sanity: proves the ProfileChanged handler actually executed under this fixture's dispatcher.
                Assert.That(raised, Does.Contain(nameof(vm.CalibrationAmountLabel)));
                Assert.That(raised, Does.Contain(nameof(vm.CalibrationAppliedAmountDisplay)));
            });
        }

        // --- 4-step / 6-step VM sequencing through the real NextStep ---

        [Test]
        public void NextStep_FourStepFlow_SkipsCurvatureStepsAndCompletes() {
            var (vm, _, _, _) = Build(configureOptions: o => o.MeasureCurvatureDuringCalibration.Returns(false));
            vm.StartCommand.Execute(null);
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline));

            // Seed a reading per measurement step as a real run would before each advance.
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0);

            vm.NextStep();
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Screw1), "from Baseline the 4-step flow goes straight to Screw1, not AllInward");
            vm.NextStep();
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.ReBaseline2));
            vm.NextStep();
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Screw2));
            vm.NextStep();
            Assert.Multiple(() => {
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Complete), "from Screw2 the 4-step wizard completes");
                Assert.That(vm.IsComplete, Is.True);
            });
        }

        [Test]
        public void NextStep_SixStepFlow_WalksAllCurvatureStepsAndCompletes() {
            var (vm, _, _, _) = Build(configureOptions: o => o.MeasureCurvatureDuringCalibration.Returns(true));
            vm.StartCommand.Execute(null);
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline));

            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.AllInward, 0.0, 0.0, 1010.0);
            vm.SeedStepReading(WizardStep.ReBaseline1, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0);

            var walked = new List<WizardStep> { vm.CurrentStep };
            for (int i = 0; i < 6; i++) {
                vm.NextStep();
                walked.Add(vm.CurrentStep);
            }

            Assert.That(walked, Is.EqualTo(new[] {
                WizardStep.Baseline, WizardStep.AllInward, WizardStep.ReBaseline1,
                WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2, WizardStep.Complete }));
        }

        // --- Curvature-sign persistence semantics at run completion ---

        [Test]
        public void CompletingSixStepRun_WritesMeasuredCurvatureSignFromMeans() {
            var (vm, options, _, _) = Build(configureOptions: o => o.MeasureCurvatureDuringCalibration.Returns(true));
            vm.StartCommand.Execute(null);

            // AllInward mean focus (990) below baseline (1000) => ComputeCurvatureSign(990, 1000) = +1:
            // an all-screws-CW step that LOWERS the mean best focus is σ = +1
            // (docs/focuser-direction-convention-design.md §1(c)).
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.AllInward, 0.0, 0.0, 990.0);
            vm.SeedStepReading(WizardStep.ReBaseline1, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0);
            options.ClearReceivedCalls();

            for (int i = 0; i < 6; i++) {
                vm.NextStep();
            }

            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Complete));
            Assert.Multiple(() => {
                options.Received().ScrewInwardCurvatureSign = 1;
                options.Received().ScrewInwardCurvatureSignIsMeasured = true;
                options.Received().IsCalibrated = true;
            });
        }

        [Test]
        public void CompletingFourStepRun_DoesNotTouchCurvatureSign() {
            var (vm, options, _, _) = Build(configureOptions: o => o.MeasureCurvatureDuringCalibration.Returns(false));
            vm.StartCommand.Execute(null);

            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0);
            options.ClearReceivedCalls();

            for (int i = 0; i < 4; i++) {
                vm.NextStep();
            }

            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Complete));
            Assert.Multiple(() => {
                // Without the AllInward probe the configured/assumed sign must be left untouched.
                options.DidNotReceive().ScrewInwardCurvatureSign = Arg.Any<int>();
                options.DidNotReceive().ScrewInwardCurvatureSignIsMeasured = Arg.Any<bool>();
                options.Received().IsCalibrated = true;
            });
        }

        [Test]
        public void Confidence_DisplayProperties_PopulatedAfterCalibration() {
            var (vm, _, _, _) = Build(screwCount: 3);
            // Seed 6 step readings with a clean, unequal-enough pair so pitch σ > 0 (values in A,B plane units).
            vm.SeedStepReading(WizardStep.Baseline, -9.5, 16.3, 7049);
            vm.SeedStepReading(WizardStep.AllInward, -15.9, 14.5, 6957);
            vm.SeedStepReading(WizardStep.ReBaseline1, -9.5, 16.3, 7049);
            vm.SeedStepReading(WizardStep.Screw1, 11.7, 8.9, 7013);
            vm.SeedStepReading(WizardStep.ReBaseline2, -9.5, 16.3, 7049);
            vm.SeedStepReading(WizardStep.Screw2, -8.6, 40.3, 7016);
            // The seed path bypasses the inspector/profile, so supply the sensor geometry a live run reads + a
            // paraboloid tilt-plane model (RunCalibrationMath reads only its ImageSize) so the hardware-recovery
            // branch runs. These seeds recover a positive per-turn pitch that is clearly unequal per screw
            // (delta1 ≈ 242, delta2 ≈ 363 µm/turn → pitch σ ≈ 61 µm/turn > 0), so PitchUncertaintyDisplay is non-empty.
            var tiltPlane = new TiltPlaneModel(new System.Drawing.Size(6248, 4176), fRatio: 7,
                a: 0, b: 0, c: 0, mean: 7000, focuserStepSizeMicrons: 3.6,
                centerPosition: 7000, topLeftPosition: 7000, topRightPosition: 7000,
                bottomLeftPosition: 7000, bottomRightPosition: 7000);
            vm.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 3.76, focuserStepMicrons: 3.6, tiltPlaneOverride: tiltPlane);
            Assert.Multiple(() => {
                Assert.That(vm.HasConfidenceInfo, Is.True);
                Assert.That(vm.ConfidenceSummaryDisplay, Does.Contain("Signal-to-noise"));
                Assert.That(vm.PitchUncertaintyDisplay, Does.Contain("±"));
            });
        }

        [Test]
        public void CaptureMeasurementContext_ReadsInspectorAndProfileValues() {
            var inspector = Substitute.For<IInspectorOptions>();
            // The EFFECTIVE step size, not the raw override: a run captured while the focuser driver supplied
            // the step size must record what it actually measured with, or a replay reinterprets it wrongly.
            inspector.EffectiveMicronsPerFocuserStep.Returns(3.6);
            inspector.UseRANSAC.Returns(true);
            inspector.AcceptableRSquaredMin.Returns(0.8);
            inspector.SensorROI.Returns(1.0); inspector.CornersROI.Returns(1.0);
            var af = Substitute.For<IAutoFocusOptions>();
            af.WeightedHyperbolicFitEnabled.Returns(true);
            af.MaxOutlierRejections.Returns(3);
            af.OutlierRejectionConfidence.Returns(0.9);
            af.HyperbolicFitModel.Returns(HyperbolicFitModel.Hybrid);

            var ctx = TiltAdapterWizardVM.CaptureMeasurementContext(inspector, af, fRatio: 7, focalLengthMm: 703);

            Assert.Multiple(() => {
                Assert.That(ctx.MicronsPerFocuserStep, Is.EqualTo(3.6));
                Assert.That(ctx.FocalRatio, Is.EqualTo(7));
                Assert.That(ctx.HyperbolicFitModel, Is.EqualTo("Hybrid"));
                Assert.That(ctx.UseRANSAC, Is.True);
            });
        }

        [Test]
        public void MeasurementContextDrift_ListsChangedFields() {
            var captured = new TiltMeasurementContext { MicronsPerFocuserStep = 3.6, FocalRatio = 7, UseRANSAC = true };
            var current  = new TiltMeasurementContext { MicronsPerFocuserStep = 0.26, FocalRatio = 7, UseRANSAC = true };
            var drift = TiltAdapterWizardVM.DescribeMeasurementContextDrift(captured, current);
            Assert.Multiple(() => {
                Assert.That(drift, Does.Contain("MicronsPerFocuserStep"));
                Assert.That(drift, Does.Not.Contain("FocalRatio"));
            });
        }

        [Test]
        public void OverlayOptimizedSettings_AppliesEveryCuratedKnob_SoTheReplayOverlayCannotDriftFromTheDto() {
            // Reproducibility guard: every detection knob a run persists in OptimizedStarDetectionSettings must be
            // reapplied by the tilt replay overlay. A knob present in the DTO but missing from OverlayOptimizedSettings
            // silently leaks the live-profile value on replay — the LocallyAdaptiveBinarization / AdaptiveNoiseBlockSize
            // regression that made a replayed calibration disagree with the run it was captured from.
            var metadataOnly = new HashSet<string> {
                nameof(OptimizedStarDetectionSettings.CreatedAtUtc),
                nameof(OptimizedStarDetectionSettings.RunCount),
                nameof(OptimizedStarDetectionSettings.BaselineJ),
                nameof(OptimizedStarDetectionSettings.FinalJ),
                nameof(OptimizedStarDetectionSettings.RecommendedStepSize),
                nameof(OptimizedStarDetectionSettings.RecommendedOffsetSteps),
                nameof(OptimizedStarDetectionSettings.SchemaVersion),
                // F30 provenance: records WHICH INVOCATION produced the landing. Metadata, not a knob — nothing
                // applies it to StarDetectorParams, and the replay overlay must not try to.
                nameof(OptimizedStarDetectionSettings.Provenance),
            };
            var curatedKnobs = typeof(OptimizedStarDetectionSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && !metadataOnly.Contains(p.Name))
                .ToList();
            Assert.That(curatedKnobs, Is.Not.Empty, "Expected OptimizedStarDetectionSettings to expose curated knobs");

            // Give each knob a sentinel that differs from a fresh snapshot's default (bools default false, ints default
            // 0 except AdaptiveNoiseBlockSize=128, doubles default 0.0), so a copied knob is provably distinguishable
            // from one the overlay left untouched.
            var dto = new OptimizedStarDetectionSettings();
            for (int i = 0; i < curatedKnobs.Count; i++) {
                curatedKnobs[i].SetValue(dto, SentinelFor(curatedKnobs[i].PropertyType, i));
            }

            var snapshot = new StarDetectionSettingsSnapshot();
            TiltAdapterWizardVM.OverlayOptimizedSettings(snapshot, dto);

            var snapshotType = typeof(StarDetectionSettingsSnapshot);
            Assert.Multiple(() => {
                foreach (var knob in curatedKnobs) {
                    var snapProp = snapshotType.GetProperty(knob.Name, BindingFlags.Public | BindingFlags.Instance);
                    Assert.That(snapProp, Is.Not.Null, $"StarDetectionSettingsSnapshot has no '{knob.Name}' to receive the curated knob");
                    if (snapProp != null) {
                        Assert.That(snapProp.GetValue(snapshot), Is.EqualTo(knob.GetValue(dto)),
                            $"OverlayOptimizedSettings did not copy '{knob.Name}' onto the replay snapshot");
                    }
                }
            });
        }

        [Test]
        public void BuildTiltReplayDetectionOverride_PrefersRunLevelFullSnapshot_OverCuratedOverlay() {
            // A run that stored the full snapshot pins detection directly from it, instead of overlaying only the
            // curated subset onto the live profile. (Tilt captures never emit per-step AutoFocusReplayMetadata, so
            // this run-level snapshot is the effective full-pin path for tilt replays.)
            var fullSnapshot = new StarDetectionSettingsSnapshot { BrightnessSensitivity = 42, LocallyAdaptiveBinarization = true };
            var metadata = new TiltCalibrationMetadata { StarDetectionSnapshot = fullSnapshot };
            // A folder with no AutoFocus replay metadata.json, so the per-step branch is skipped.
            var noPerStepMetadata = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hf-tilt-no-replay-metadata");

            var result = TiltAdapterWizardVM.BuildTiltReplayDetectionOverride(noPerStepMetadata, metadata);

            Assert.That(result, Is.SameAs(fullSnapshot));
        }

        // --- Motorized device connection pane (T10) ---

        [Test]
        public void IsMotorizedDevice_TrueForEatPresets_FalseForManualAndScrewPresets() {
            // Drives the "Motorized Device Connection" GroupBox visibility: it must appear only for
            // presets with a registered motion controller (the two EAT presets) and disappear for
            // Manual and every screw preset.
            var (vmEat, _, _, _) = Build(configureOptions: o => o.DeviceName.Returns("ASG Electronic EAT - 90mm"));
            var (vmEat461, _, _, _) = Build(configureOptions: o => o.DeviceName.Returns("ASG Electronic EAT - ZWO 461"));
            var (vmManual, _, _, _) = Build(configureOptions: o => o.DeviceName.Returns("Manual"));
            var (vmScrew, _, _, _) = Build(configureOptions: o => o.DeviceName.Returns("ASG Photon Cage - 90mm"));

            Assert.Multiple(() => {
                Assert.That(vmEat.IsMotorizedDevice, Is.True);
                Assert.That(vmEat461.IsMotorizedDevice, Is.True);
                Assert.That(vmManual.IsMotorizedDevice, Is.False);
                Assert.That(vmScrew.IsMotorizedDevice, Is.False);
            });
        }

        [Test]
        public void SelectedDevice_Change_RaisesIsMotorizedDevice() {
            // The GroupBox visibility binds to IsMotorizedDevice, so switching presets must re-raise it.
            var (vm, _, _, _) = Build();
            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.SelectedDevice = "ASG Electronic EAT - 90mm";

            Assert.Multiple(() => {
                Assert.That(vm.IsMotorizedDevice, Is.True);
                Assert.That(raised, Does.Contain(nameof(vm.IsMotorizedDevice)));
            });
        }

        [Test]
        public void SelectedPortName_PersistsToOptions() {
            var (vm, options, _, _) = Build();

            vm.SelectedPortName = "COM7";

            Assert.Multiple(() => {
                options.Received().TiltDeviceSerialPortName = "COM7";
                Assert.That(vm.SelectedPortName, Is.EqualTo("COM7"));
            });
        }

        // Fix 4 [MINOR]: WPF's ComboBox Selector coerces SelectedItem (and pushes it back through this
        // two-way binding) to null when the bound value isn't present in AvailablePortNames -- there is no
        // "no selection" item in this UI, so a null/empty incoming value can only be that coercion, never a
        // genuine user choice. It must not wipe a remembered, non-empty persisted port.
        [Test]
        public void SelectedPortName_NullFromSelectorCoercion_DoesNotWipePersistedPort() {
            var (vm, options, _, _) = Build(configureOptions: o => o.TiltDeviceSerialPortName.Returns("COM7"));

            vm.SelectedPortName = null;

            Assert.Multiple(() => {
                Assert.That(vm.SelectedPortName, Is.EqualTo("COM7"), "the persisted port must survive the coercion");
                options.DidNotReceive().TiltDeviceSerialPortName = Arg.Any<string>();
            });
        }

        [Test]
        public void SelectedPortName_PersistedPortNotInEnumeratedList_SurvivesAndCanStillConnect() {
            // BuildMotorized's port provider enumerates only COM3/COM7 (see its ctor below); the persisted
            // port ("COM9") is a device that is currently unplugged -- not among them.
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.TiltDeviceSerialPortName.Returns("COM9");
            Assert.That(vm.SelectedPortName, Is.EqualTo("COM9"), "precondition: the persisted port loads normally");

            // Simulates the ComboBox's Selector coercing SelectedItem (and pushing it back through the
            // two-way binding) to null because "COM9" is absent from AvailablePortNames.
            vm.SelectedPortName = null;

            Assert.Multiple(() => {
                Assert.That(vm.SelectedPortName, Is.EqualTo("COM9"), "the persisted port must survive the coercion, not be wiped to empty");
                options.DidNotReceive().TiltDeviceSerialPortName = Arg.Any<string>();
                Assert.That(vm.ConnectDeviceCommand.CanExecute(null), Is.True, "a surviving persisted port must still allow connecting");
            });

            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(service.Connected, Is.True, "connecting must still work against the unlisted persisted port");
                controller.Received(1).ConnectAsync("COM9", Arg.Any<CancellationToken>());
            });
        }

        [Test]
        public void AvailablePortNames_EnumeratesLazily_AndRefreshReEnumerates() {
            var ports = Substitute.For<ISerialPortProvider>();
            ports.GetPortNames(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(
                    new ReadOnlyCollection<string>(new[] { "COM3" }),
                    new ReadOnlyCollection<string>(new[] { "COM3", "COM7" }));
            var (vm, _, _, _) = Build(serialPortProvider: ports);

            // The simulated adapter is always prepended to the enumerated serial ports.
            Assert.That(vm.AvailablePortNames, Is.EqualTo(new[] { "Simulator", "COM3" }));

            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            vm.RefreshPortsCommand.Execute(null);

            Assert.Multiple(() => {
                Assert.That(vm.AvailablePortNames, Is.EqualTo(new[] { "Simulator", "COM3", "COM7" }));
                Assert.That(raised, Does.Contain(nameof(vm.AvailablePortNames)));
            });
        }

        [Test]
        public void AvailablePortNames_ContainsSimulatorSentinelFirst() {
            var (vm, _, _, _, _, _) = BuildMotorized();
            Assert.That(vm.AvailablePortNames, Is.EqualTo(new[] { "Simulator", "COM3", "COM7" }));
        }

        [Test]
        public void ConnectDeviceCommand_CanExecute_RequiresMotorizedAndPortAndNotConnected() {
            var (vm, _, _, _, _, _) = BuildMotorized();

            // Motorized but no port selected yet.
            Assert.Multiple(() => {
                Assert.That(vm.ConnectDeviceCommand.CanExecute(null), Is.False, "no port selected");
                Assert.That(vm.DisconnectDeviceCommand.CanExecute(null), Is.False, "not connected");
            });

            vm.SelectedPortName = "COM3";
            Assert.That(vm.ConnectDeviceCommand.CanExecute(null), Is.True, "motorized + port + not connected");
        }

        [Test]
        public void ConnectDeviceCommand_CanExecute_FalseForNonMotorizedDevice() {
            // Even with a port persisted, Manual/screw presets have no motion controller to connect.
            var (vm, _, _, _) = Build(configureOptions: o => {
                o.DeviceName.Returns("Manual");
                o.TiltDeviceSerialPortName.Returns("COM3");
            });
            Assert.That(vm.ConnectDeviceCommand.CanExecute(null), Is.False);
        }

        // NOTE: the motorized-pane tests below are deliberately synchronous (GetAwaiter().GetResult() over
        // fakes whose tasks complete inline): ProgressFactory.Create — called by the InspectorVM/wizard VM
        // constructors — NREs under the SynchronizationContext NUnit installs for async test methods when
        // Application.Current is null, which is why every other test in this fixture is synchronous too.

        [Test]
        public void ConnectDeviceCommand_ConnectsThroughService_AndPersistsPort() {
            var (vm, options, service, controller, _, requestedPresets) = BuildMotorized();
            vm.SelectedPortName = "COM7";

            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(service.Connected, Is.True);
                Assert.That(vm.IsTiltDeviceConnected, Is.True);
                Assert.That(requestedPresets, Is.EqualTo(new[] { "ASG Electronic EAT - 90mm" }),
                    "the service must be asked for the selected device preset's controller");
                controller.Received(1).ConnectAsync("COM7", Arg.Any<CancellationToken>());
                options.Received().TiltDeviceSerialPortName = "COM7";
                Assert.That(vm.ConnectDeviceCommand.CanExecute(null), Is.False, "already connected");
                Assert.That(vm.DisconnectDeviceCommand.CanExecute(null), Is.True);
                Assert.That(vm.TiltDeviceStatusText, Does.Contain("Connected"));
            });
        }

        // --- Simulator port: block-until-active gate, config-match prompt, aberration auto-enable -------------

        // A substitute sim-options that matches the "ASG Electronic EAT - 90mm" preset (4 corners, steppers,
        // 1.8 µm/step, 55 mm radius), aberrations on.
        private static ICameraSimulatorOptions MatchingSimOptions(bool aberrationsEnabled = true) {
            var sim = Substitute.For<ICameraSimulatorOptions>();
            sim.SimScrewCount.Returns(4);
            sim.SimAdjustmentType.Returns(TiltAdjustmentType.StepperMotors);
            sim.SimStepperStepSizeMicrons.Returns(1.8);
            sim.SimScrewRadiusMillimeters.Returns(55.0);
            sim.EnableAberrations.Returns(aberrationsEnabled);
            return sim;
        }

        private static void ActivateSimulatorCamera(TiltAdapterWizardVM vm) =>
            vm.UpdateDeviceInfo(new CameraInfo { Connected = true, DeviceId = HocusFocusSimulatorCamera.DeviceId });

        private static void ConnectSimulatorPort(TiltAdapterWizardVM vm) {
            vm.SelectedPortName = SimulatedTiltPort.PortName;
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();
        }

        [Test]
        public void Connect_SimulatorPort_CameraSimulatorNotActive_DoesNotConnect() {
            var sim = MatchingSimOptions();
            var (vm, _, service, controller, _, _) = BuildMotorized(cameraSimulatorOptions: sim);
            // No camera activated -> CameraInfo defaults to not-connected.

            ConnectSimulatorPort(vm);

            Assert.That(service.Connected, Is.False, "must not connect the sim adapter without the sim camera active");
            controller.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public void Connect_SimulatorPort_WrongActiveCamera_DoesNotConnect() {
            var sim = MatchingSimOptions();
            var (vm, _, service, controller, _, _) = BuildMotorized(cameraSimulatorOptions: sim);
            vm.UpdateDeviceInfo(new CameraInfo { Connected = true, DeviceId = "Some.Other.Camera" });

            ConnectSimulatorPort(vm);

            Assert.That(service.Connected, Is.False);
            controller.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public void Connect_SimulatorPort_ConfigMatches_ConnectsWithoutPrompt() {
            var sim = MatchingSimOptions();
            var promptCount = 0;
            var (vm, _, service, controller, _, _) = BuildMotorized(
                cameraSimulatorOptions: sim,
                confirmSimConfigChangeAsync: (m, t) => { promptCount++; return Task.FromResult(true); });
            ActivateSimulatorCamera(vm);

            ConnectSimulatorPort(vm);

            Assert.Multiple(() => {
                Assert.That(promptCount, Is.EqualTo(0), "matching config must not prompt");
                Assert.That(service.Connected, Is.True);
                controller.Received(1).ConnectAsync(SimulatedTiltPort.PortName, Arg.Any<CancellationToken>());
            });
        }

        [Test]
        public void Connect_SimulatorPort_ConfigDiffers_Yes_WritesSimConfigFromPreset_ThenConnects() {
            var sim = MatchingSimOptions();
            sim.SimScrewRadiusMillimeters.Returns(40.0); // mismatch vs the 55 mm preset
            var (vm, _, service, controller, _, _) = BuildMotorized(
                cameraSimulatorOptions: sim,
                confirmSimConfigChangeAsync: (m, t) => Task.FromResult(true));
            ActivateSimulatorCamera(vm);

            ConnectSimulatorPort(vm);

            Assert.Multiple(() => {
                sim.Received().SimScrewCount = 4;
                sim.Received().SimAdjustmentType = TiltAdjustmentType.StepperMotors;
                sim.Received().SimStepperStepSizeMicrons = 1.8;
                sim.Received().SimScrewRadiusMillimeters = 55.0;
                Assert.That(service.Connected, Is.True);
                controller.Received(1).ConnectAsync(SimulatedTiltPort.PortName, Arg.Any<CancellationToken>());
            });
        }

        [Test]
        public void Connect_SimulatorPort_ConfigDiffers_No_DoesNotConnect_NoWrites() {
            var sim = MatchingSimOptions();
            sim.SimAdjustmentType.Returns(TiltAdjustmentType.Screws); // mismatch vs the stepper preset
            var (vm, _, service, controller, _, _) = BuildMotorized(
                cameraSimulatorOptions: sim,
                confirmSimConfigChangeAsync: (m, t) => Task.FromResult(false));
            ActivateSimulatorCamera(vm);

            ConnectSimulatorPort(vm);

            Assert.Multiple(() => {
                Assert.That(service.Connected, Is.False, "declining the config change must abort the connect");
                controller.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
                sim.DidNotReceive().SimScrewRadiusMillimeters = Arg.Any<double>();
                sim.DidNotReceive().SimAdjustmentType = Arg.Any<TiltAdjustmentType>();
            });
        }

        [Test]
        public void Connect_SimulatorPort_AberrationsDisabled_AutoEnablesAndConnects() {
            var sim = MatchingSimOptions(aberrationsEnabled: false);
            var (vm, _, service, _, _, _) = BuildMotorized(
                cameraSimulatorOptions: sim,
                confirmSimConfigChangeAsync: (m, t) => Task.FromResult(true));
            ActivateSimulatorCamera(vm);

            ConnectSimulatorPort(vm);

            Assert.Multiple(() => {
                sim.Received().EnableAberrations = true;
                Assert.That(service.Connected, Is.True);
            });
        }

        [Test]
        public void Connect_RealPort_NeverChecksSimConfigOrCameraGate() {
            var sim = MatchingSimOptions();
            sim.SimScrewRadiusMillimeters.Returns(40.0); // would mismatch, but a real port must not check it
            var promptCount = 0;
            var (vm, _, service, controller, _, _) = BuildMotorized(
                cameraSimulatorOptions: sim,
                confirmSimConfigChangeAsync: (m, t) => { promptCount++; return Task.FromResult(true); });
            // Note: no sim camera activated; a real COM port must connect regardless.

            vm.SelectedPortName = "COM7";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(promptCount, Is.EqualTo(0), "a real COM port must not trigger the sim config prompt");
                Assert.That(service.Connected, Is.True);
                controller.Received(1).ConnectAsync("COM7", Arg.Any<CancellationToken>());
                sim.DidNotReceive().SimScrewRadiusMillimeters = Arg.Any<double>();
            });
        }

        [Test]
        public void DeviceMove_UpdatesScrewPositionDisplayWithDeltaFromStart() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            // The counters come from the controller's own latest report, read once when the service connects,
            // once for the run baseline (before the first move), and once per move (what the move itself carried).
            controller.LastKnownPositions.Returns(
                new TiltDevicePositions(new[] { 0, 0, 0, 0 }, known: true),
                new TiltDevicePositions(new[] { 0, 0, 0, 0 }, known: true),
                new TiltDevicePositions(new[] { 150, 150, 150, 150 }, known: true));

            vm.SelectedPortName = "COM3";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            vm.ExecuteDeviceMoveForCurrentStepAsync(WizardStep.AllInward, CancellationToken.None).GetAwaiter().GetResult();

            // TR = motor 1 = index 0: shows the live position plus the delta from the run's baseline.
            Assert.That(vm.ScrewPositionTopRightDisplay, Does.Contain("150").And.Contain("Δ").And.Contain("+150"));
        }

        // The run baseline is captured ONCE (before the run's first move), so every later step's display must
        // keep counting from it -- the delta is "since this calibration run started", not "since the last move".
        [Test]
        public void DeviceMove_LaterStepsInSameRun_KeepUpdatingAgainstTheRunBaseline() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            controller.LastKnownPositions.Returns(
                new TiltDevicePositions(new[] { 0, 0, 0, 0 }, known: true),         // read when the service connects
                new TiltDevicePositions(new[] { 0, 0, 0, 0 }, known: true),         // run baseline
                new TiltDevicePositions(new[] { 150, 150, 150, 150 }, known: true), // after step 1
                new TiltDevicePositions(new[] { 300, 150, 150, 0 }, known: true));  // after step 2

            vm.SelectedPortName = "COM3";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            vm.ExecuteDeviceMoveForCurrentStepAsync(WizardStep.AllInward, CancellationToken.None).GetAwaiter().GetResult();
            vm.ExecuteDeviceMoveForCurrentStepAsync(WizardStep.Screw1, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(vm.ScrewPositionTopRightDisplay, Does.Contain("300").And.Contain("+300"));
        }

        // The device reports its new counters as part of the move it just acknowledged, so a step must never
        // depend on a follow-up position query: that query was a wasted round trip AND an extra failure point
        // that, when it came back unparseable, silently froze the position/Δ display at its pre-move values.
        [Test]
        public void DeviceMove_UpdatesTheDisplayWithoutASeparatePositionQuery() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            controller.LastKnownPositions.Returns(
                new TiltDevicePositions(new[] { 0, 0, 0, 0 }, known: true),
                new TiltDevicePositions(new[] { 0, 0, 0, 0 }, known: true),
                new TiltDevicePositions(new[] { 150, 150, 150, 150 }, known: true));

            vm.SelectedPortName = "COM3";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            vm.ExecuteDeviceMoveForCurrentStepAsync(WizardStep.AllInward, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(vm.ScrewPositionTopRightDisplay, Does.Contain("150").And.Contain("+150"));
                controller.DidNotReceive().QueryPositionsAsync(Arg.Any<CancellationToken>());
            });
        }

        // A run whose counters are not confirmed (nothing has ever parsed a position report) must keep the last
        // snapshot rather than blanking the panel mid-run -- and the service logs it, so a frozen display is
        // diagnosable instead of silent.
        [Test]
        public void DeviceMove_PositionsNeverConfirmed_KeepsTheLastSnapshotInsteadOfBlanking() {
            var (vm, options, service, controller, _, _) = BuildMotorized(
                polledPositions: new TiltDevicePositions(new[] { 7, 7, 7, 7 }, known: true));
            options.CalibrationAppliedAmount.Returns(150.0);
            controller.LastKnownPositions.Returns(TiltDevicePositions.Unknown);

            vm.SelectedPortName = "COM3";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            service.PublishControllerPositions(); // no-op: nothing confirmed
            vm.ExecuteDeviceMoveForCurrentStepAsync(WizardStep.AllInward, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(vm.ScrewPositionTopRightDisplay, Is.EqualTo("unknown"),
                "an unconfirmed position must read 'unknown', never a stale number presented as current");
        }

        [Test]
        public void DisconnectDeviceCommand_DisconnectsThroughService() {
            var (vm, _, service, controller, _, _) = BuildMotorized();
            vm.SelectedPortName = "COM3";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.That(service.Connected, Is.True, "precondition");

            ((AsyncRelayCommand)vm.DisconnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(service.Connected, Is.False);
                Assert.That(vm.IsTiltDeviceConnected, Is.False);
                controller.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
                Assert.That(vm.TiltDeviceStatusText, Does.Contain("Not connected"));
                Assert.That(vm.DisconnectDeviceCommand.CanExecute(null), Is.False);
            });
        }

        [Test]
        public void IdlePrompt_ConfirmYes_DisconnectsThroughService() {
            int promptCount = 0;
            var (vm, _, service, controller, time, _) = BuildMotorized(
                confirmIdleDisconnectAsync: () => { promptCount++; return Task.FromResult(true); });
            vm.SelectedPortName = "COM3";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.That(service.Connected, Is.True, "precondition");

            time.Advance(TimeSpan.FromMinutes(31));
            Assert.That(vm.LastIdlePromptTask, Is.Not.Null, "the idle prompt handler should have run");
            vm.LastIdlePromptTask.GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(promptCount, Is.EqualTo(1));
                Assert.That(service.Connected, Is.False, "Yes must route to ConfirmIdleDisconnectAsync (disconnect)");
                controller.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
            });
        }

        [Test]
        public void IdlePrompt_ConfirmNo_KeepsConnected_AndResetsIdleTimer() {
            int promptCount = 0;
            var (vm, _, service, controller, time, _) = BuildMotorized(
                confirmIdleDisconnectAsync: () => { promptCount++; return Task.FromResult(false); });
            vm.SelectedPortName = "COM3";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            time.Advance(TimeSpan.FromMinutes(31));
            vm.LastIdlePromptTask.GetAwaiter().GetResult();
            Assert.Multiple(() => {
                Assert.That(promptCount, Is.EqualTo(1));
                Assert.That(service.Connected, Is.True, "No must route to KeepConnectedResetIdle (stay connected)");
                controller.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
            });

            // KeepConnectedResetIdle restarted the 30-minute window: 29 more minutes -> no new prompt...
            time.Advance(TimeSpan.FromMinutes(29));
            Assert.That(promptCount, Is.EqualTo(1), "the idle timer must have been reset by the No answer");

            // ...but 31 minutes past the reset the prompt is re-armed and fires again.
            time.Advance(TimeSpan.FromMinutes(2));
            vm.LastIdlePromptTask.GetAwaiter().GetResult();
            Assert.That(promptCount, Is.EqualTo(2));
        }

        [Test]
        public void ScrewPositionDisplays_MapDeviceMotorOrderToCorners_AndShowUnknownOtherwise() {
            // Service positions come in DEVICE motor order: [0]=TR (motor 1), [1]=TL (motor 2),
            // [2]=BR (motor 3), [3]=BL (motor 4).
            var polled = new TiltDevicePositions(new[] { 10, 20, 30, 40 }, known: true);
            var (vm, _, service, _, time, _) = BuildMotorized(polledPositions: polled);

            Assert.That(vm.ScrewPositionTopRightDisplay, Is.EqualTo("unknown"), "positions unknown before connecting");

            vm.SelectedPortName = "COM3";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            time.Advance(TimeSpan.FromSeconds(6)); // > PollInterval; the tick polls and applies positions

            Assert.Multiple(() => {
                Assert.That(service.PositionsKnown, Is.True, "precondition: the poll applied positions");
                Assert.That(vm.TiltDevicePositionsKnown, Is.True);
                Assert.That(vm.ScrewPositionTopRightDisplay, Is.EqualTo("10"));
                Assert.That(vm.ScrewPositionTopLeftDisplay, Is.EqualTo("20"));
                Assert.That(vm.ScrewPositionBottomRightDisplay, Is.EqualTo("30"));
                Assert.That(vm.ScrewPositionBottomLeftDisplay, Is.EqualTo("40"));
            });

            ((AsyncRelayCommand)vm.DisconnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Multiple(() => {
                Assert.That(vm.TiltDevicePositionsKnown, Is.False);
                Assert.That(vm.ScrewPositionTopLeftDisplay, Is.EqualTo("unknown"), "positions reset to unknown on disconnect");
            });
        }

        // --- T11: hands-off device-driven calibration ---------------------------------------------------------
        // All tests below are deliberately synchronous (.GetAwaiter().GetResult()) — see the note above
        // ConnectDeviceCommand_ConnectsThroughService_AndPersistsPort for why (ProgressFactory.Create NREs
        // under NUnit's async SynchronizationContext).

        private static void Connect(TiltAdapterWizardVM vm, string port = "COM3") {
            vm.SelectedPortName = port;
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();
        }

        private static void StubSuccessfulMoves(ITiltMotionController controller) {
            controller.ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
        }

        // Mirrors the seed values the existing 6-step SeedStepReading tests use (e.g. RunCalibrationForTest
        // regression tests below Constructor_Re...): a distinct, non-degenerate reading per step so
        // RunCalibrationMath's angle/hardware-recovery math has real signal instead of an all-zero diff.
        private static Func<WizardStep, CancellationToken, Task<bool>> SeedingMeasurementOverride(TiltAdapterWizardVM vm) {
            var readings = new Dictionary<WizardStep, (double a, double b, double mean)> {
                [WizardStep.Baseline] = (0.0, 0.0, 1000.0),
                [WizardStep.AllInward] = (0.0, 0.0, 1010.0),
                [WizardStep.ReBaseline1] = (0.0, 0.0, 1000.0),
                [WizardStep.Screw1] = (0.5, 0.0, 1000.0),
                [WizardStep.ReBaseline2] = (0.0, 0.0, 1000.0),
                [WizardStep.Screw2] = (-0.25, 0.433, 1000.0),
            };
            return (step, ct) => {
                var (a, b, mean) = readings[step];
                vm.SeedStepReading(step, a, b, mean);
                return Task.FromResult(true);
            };
        }

        [TestCase(WizardStep.AllInward, TiltMoveAxis.Backfocus, 150)]
        [TestCase(WizardStep.ReBaseline1, TiltMoveAxis.Backfocus, -150)]
        [TestCase(WizardStep.Screw1, TiltMoveAxis.DiagonalA, 150)]
        [TestCase(WizardStep.ReBaseline2, TiltMoveAxis.DiagonalA, -150)]
        [TestCase(WizardStep.Screw2, TiltMoveAxis.DiagonalB, 150)]
        [TestCase(WizardStep.Complete, TiltMoveAxis.DiagonalB, -150)]
        public void ExecuteDeviceMoveForCurrentStepAsync_EachStep_SendsExpectedMove(WizardStep step, TiltMoveAxis expectedAxis, int expectedSteps) {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);

            bool result = vm.ExecuteDeviceMoveForCurrentStepAsync(step, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(result, Is.True);
                controller.Received(1).ExecuteMoveAsync(
                    Arg.Is<TiltAdapterMove>(m => m.Axis == expectedAxis && m.Steps == expectedSteps),
                    Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
            });
        }

        [Test]
        public void ExecuteDeviceMoveForCurrentStepAsync_Baseline_IsNoOp_AndControllerUntouched() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            Connect(vm);

            bool result = vm.ExecuteDeviceMoveForCurrentStepAsync(WizardStep.Baseline, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(result, Is.True);
                controller.DidNotReceive().ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
            });
        }

        [Test]
        public void ExecuteDeviceMoveForCurrentStepAsync_ControllerThrows_SendsInverseRecovery_AndSurfacesFailure() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            Connect(vm);
            controller.ExecuteMoveAsync(
                    Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == 150),
                    Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
                .Returns<Task>(x => throw new InvalidOperationException("boom"));
            controller.ExecuteMoveAsync(
                    Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == -150),
                    Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            bool result = vm.ExecuteDeviceMoveForCurrentStepAsync(WizardStep.Screw1, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(result, Is.False);
                controller.Received(1).ExecuteMoveAsync(
                    Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == -150),
                    Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                Assert.That(vm.HasMeasurementFailureChoice, Is.True);
                Assert.That(vm.MeasurementFailureText, Does.Contain("boom"));
            });
        }

        [Test]
        public void ExecuteDeviceMoveForCurrentStepAsync_LimitException_DoesNotSendSpuriousInverse() {
            // TiltDeviceLimitException means NOTHING was sent (every soft-limit check runs before any device
            // I/O) — sending an inverse here would be a spurious, physically real move for a step that never
            // happened. See EatTiltMotionController.ExecuteMoveAsync's documented exception taxonomy.
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            Connect(vm);
            controller.ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
                .Returns<Task>(x => throw new TiltDeviceLimitException("refused"));

            bool result = vm.ExecuteDeviceMoveForCurrentStepAsync(WizardStep.Screw1, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(result, Is.False);
                // Exactly one call total: the failed attempt itself, and no recovery/inverse call.
                controller.Received(1).ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                Assert.That(vm.HasMeasurementFailureChoice, Is.True);
                Assert.That(vm.MeasurementFailureText, Does.Contain("Nothing was sent"));
            });
        }

        [Test]
        public void AutoRunAllCommand_CanExecute_RequiresConnectedMotorizedAndValidAppliedAmount() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            Assert.That(vm.AutoRunAllCommand.CanExecute(null), Is.False, "not connected, and applied amount unset (NSubstitute double default 0)");

            options.CalibrationAppliedAmount.Returns(150.0);
            Connect(vm);
            Assert.That(vm.AutoRunAllCommand.CanExecute(null), Is.True, "connected + motorized + valid applied amount");
        }

        [Test]
        public void AutoRunAll_FourStepFlow_WalksSequence_IncludingCompleteRestoreMove_AndSetsDeviceLinkedMarker() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            // Connecting defaults this ON (T11 item 4); re-assert false AFTER connecting, so StartAsync (read
            // fresh at Start) resolves the 4-step flow this test exercises.
            options.MeasureCurvatureDuringCalibration.Returns(false);
            vm.MeasurementStepOverrideForTest = SeedingMeasurementOverride(vm);

            ((AsyncRelayCommand)vm.AutoRunAllCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(vm.IsComplete, Is.True);
                Received.InOrder(() => {
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == 150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == -150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalB && m.Steps == 150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalB && m.Steps == -150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                });
                options.Received(1).DeviceLinkedCalibrationDeviceName = "ASG Electronic EAT - 90mm";
            });
        }

        [Test]
        public void AutoRunAll_SixStepFlow_WalksFullSequence_IncludingCurvatureSteps() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            options.MeasureCurvatureDuringCalibration.Returns(true); // 6-step flow
            StubSuccessfulMoves(controller);
            Connect(vm);
            vm.MeasurementStepOverrideForTest = SeedingMeasurementOverride(vm);

            ((AsyncRelayCommand)vm.AutoRunAllCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(vm.IsComplete, Is.True);
                Received.InOrder(() => {
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.Backfocus && m.Steps == 150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.Backfocus && m.Steps == -150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == 150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == -150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalB && m.Steps == 150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                    controller.ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalB && m.Steps == -150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                });
            });
        }

        [Test]
        public void AutoRunAll_Cancel_FinishesCurrentStep_ThenRecoversAppliedMovesInReverse() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            options.MeasureCurvatureDuringCalibration.Returns(false); // re-assert AFTER connecting; see the 4-step-flow test above

            // Cancel while Screw1's measurement is in flight: the move+measurement for Screw1 finishes (device
            // has no abort), THEN the loop notices cancellation before ReBaseline2's move and recovers.
            vm.MeasurementStepOverrideForTest = (step, ct) => {
                vm.SeedStepReading(step, 0.1, 0.0, 1000.0);
                if (step == WizardStep.Screw1) {
                    vm.CancelCommand.Execute(null);
                }
                return Task.FromResult(true);
            };

            ((AsyncRelayCommand)vm.AutoRunAllCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(vm.IsComplete, Is.False, "the run must stop before Complete");
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.ReBaseline2), "NextStep already advanced past Screw1 before the cancellation was noticed");
                // Screw1's move was sent, then its inverse was sent back as recovery.
                controller.Received(1).ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == 150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                controller.Received(1).ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == -150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                // ReBaseline2/Screw2/Complete's DiagonalB moves must never have been sent.
                controller.DidNotReceive().ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalB), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                // The lease must be released so the device is available again.
                Assert.That(service.TryBeginOperation("anything"), Is.Not.Null);
            });
        }

        // Once an automated run takes its cancel/failure exit it is NOT resumable: RecoverAppliedMovesAsync has
        // undone this run's device moves (so the readings already in stepReadings no longer describe where the
        // device physically is) and the exclusive operation lease has been released. Re-entering AutoRunAllAsync
        // skips StartAsync -- IsWizardRunning is still true -- so it would resume at the stale CurrentStep,
        // re-send moves computed from a rolled-back position, and run unleased. Same for the failure panel's
        // "Run AutoFocus again". Both must be disabled until the user starts a fresh run.
        [Test]
        public void AutoRunAll_AfterCancelAndRecovery_CannotBeResumed() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            options.MeasureCurvatureDuringCalibration.Returns(false);
            // Every command below also gates on AreDevicesConnected / IsOnMeasurementStep; connect the imaging
            // devices so each assertion fails for the latch's absence rather than passing vacuously.
            vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
            vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });

            vm.MeasurementStepOverrideForTest = (step, ct) => {
                vm.SeedStepReading(step, 0.1, 0.0, 1000.0);
                if (step == WizardStep.Screw1) {
                    vm.CancelCommand.Execute(null);
                }
                return Task.FromResult(true);
            };

            ((AsyncRelayCommand)vm.AutoRunAllCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                // Precondition: this is exactly the state the existing cancel/recovery test leaves behind.
                Assert.That(vm.IsComplete, Is.False);
                Assert.That(vm.IsWizardRunning, Is.True, "the wizard panel stays up so the user can read what happened");
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.ReBaseline2), "the stale step a resume would restart from");

                Assert.That(vm.AutoRunAllCommand.CanExecute(null), Is.False,
                    "resuming would re-send moves computed from a position the rollback already undid");
                Assert.That(vm.RetryMeasurementCommand.CanExecute(null), Is.False,
                    "re-measuring the stale step is invalid for the same reason");
                // EVERY way back into the measurement loop must be closed, not just the two obvious buttons:
                // these two live in the same Panel B row and call the same handlers.
                Assert.That(vm.RunMeasurementCommand.CanExecute(null), Is.False,
                    "\"Run This Step\" calls the same RunMeasurementAsync as the retry button");
                Assert.That(vm.UseSavedAFCommand.CanExecute(null), Is.False,
                    "\"Use Saved AF\" reaches the same Complete from the same stale readings");
                Assert.That(vm.StatusText, Does.Contain("cannot be resumed"),
                    "the user must be told to start over rather than left with dead buttons");
            });
        }

        // The latch must NOT fire when nothing was actually rolled back. A measurement that fails on Baseline
        // has had no device move applied yet (Baseline has none), so RecoverAppliedMovesAsync undoes nothing and
        // the run's readings still describe where the device physically is -- retrying is legitimate, and
        // latching there would regress a working flow into "Abort Wizard and start over".
        [Test]
        public void AutoRunAll_BaselineMeasurementFailure_WithNoMovesApplied_StaysRetryable() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            options.MeasureCurvatureDuringCalibration.Returns(false);
            vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
            vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });

            vm.MeasurementStepOverrideForTest = (step, ct) => Task.FromResult(false); // fails on Baseline

            ((AsyncRelayCommand)vm.AutoRunAllCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline), "precondition: failed before advancing");
                Assert.That(vm.HasMeasurementFailureChoice, Is.True, "precondition: the failure panel is showing");
                controller.DidNotReceive().ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                Assert.That(vm.RetryMeasurementCommand.CanExecute(null), Is.True,
                    "nothing was rolled back, so re-running AutoFocus for Baseline is still valid");
                Assert.That(vm.MeasurementFailureText, Does.Not.Contain("cannot be resumed"));
            });
        }

        // Cancelling before any move has been applied must not claim the device was moved back -- nothing moved.
        [Test]
        public void AutoRunAll_CancelBeforeAnyMoveApplied_DoesNotClaimTheDeviceWasReturned() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            options.MeasureCurvatureDuringCalibration.Returns(false);

            // Cancel during Baseline's measurement: Baseline applies no device move, so the loop notices the
            // cancellation with appliedDeviceMovesThisRun still empty.
            vm.MeasurementStepOverrideForTest = (step, ct) => {
                vm.SeedStepReading(step, 0.1, 0.0, 1000.0);
                if (step == WizardStep.Baseline) {
                    vm.CancelCommand.Execute(null);
                }
                return Task.FromResult(true);
            };

            ((AsyncRelayCommand)vm.AutoRunAllCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                controller.DidNotReceive().ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                Assert.That(vm.StatusText, Does.Contain("cancelled"));
                Assert.That(vm.StatusText, Does.Not.Contain("returned to its original position"),
                    "no move was ever sent, so there was nothing to return");
            });
        }

        [Test]
        public void AutoRunAll_AfterMidRunFailureAndRecovery_CannotBeResumed() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            options.MeasureCurvatureDuringCalibration.Returns(false);
            // RetryMeasurementCommand also gates on AreDevicesConnected, so connect the imaging devices --
            // otherwise the retry assertion below would pass vacuously and prove nothing about the latch.
            vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
            vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });

            // Screw1's measurement fails outright -- MeasureStep sets HasMeasurementFailureChoice, and
            // AutoRunAllAsync rolls the run's applied moves back before returning.
            vm.MeasurementStepOverrideForTest = (step, ct) => {
                if (step == WizardStep.Screw1) {
                    return Task.FromResult(false);
                }
                vm.SeedStepReading(step, 0.1, 0.0, 1000.0);
                return Task.FromResult(true);
            };

            ((AsyncRelayCommand)vm.AutoRunAllCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(vm.HasMeasurementFailureChoice, Is.True, "precondition: the failure panel is showing");
                Assert.That(vm.IsWizardRunning, Is.True, "the failure panel lives inside the run panel; it must stay visible");
                Assert.That(vm.AutoRunAllCommand.CanExecute(null), Is.False);
                Assert.That(vm.RetryMeasurementCommand.CanExecute(null), Is.False);
                Assert.That(vm.MeasurementFailureText, Does.Contain("cannot be resumed"),
                    "the failure panel must explain why its own retry button is dead");
            });
        }

        // The latch is per-run: starting a fresh run must clear it, or the wizard would be permanently bricked
        // for automated use after the first cancellation.
        [Test]
        public void AutoRunAll_AfterCancelAndRecovery_AFreshStartClearsTheLatch() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            options.MeasureCurvatureDuringCalibration.Returns(false);
            vm.MeasurementStepOverrideForTest = (step, ct) => {
                vm.SeedStepReading(step, 0.1, 0.0, 1000.0);
                if (step == WizardStep.Screw1) {
                    vm.CancelCommand.Execute(null);
                }
                return Task.FromResult(true);
            };
            ((AsyncRelayCommand)vm.AutoRunAllCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.That(vm.AutoRunAllCommand.CanExecute(null), Is.False, "precondition: latched");

            // StartAsync must clear the latch on its own. Restart also clears it, so going through Restart first
            // would let this test pass even if StartAsync's clear were deleted -- assert after each separately.
            vm.RestartCommand.Execute(null);
            Assert.That(vm.AutoRunAllCommand.CanExecute(null), Is.True, "Restart clears the latch");

            // Re-latch, then prove StartAsync clears it without Restart's help. StartCommand requires the wizard
            // to be idle, which Restart above already ensured; re-latching directly keeps the two clears independent.
            vm.SetDeviceRunAbandonedForTest(true);
            Assert.That(vm.AutoRunAllCommand.CanExecute(null), Is.False, "precondition: latched again");

            vm.StartCommand.Execute(null);

            Assert.Multiple(() => {
                Assert.That(vm.AutoRunAllCommand.CanExecute(null), Is.True, "StartAsync clears the latch");
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline));
            });
        }

        [Test]
        public void Disconnected_FourStepFlow_NeverTouchesController_AndClearsDeviceLinkedMarker() {
            // BuildMotorized gives a motorized preset + a real controller mock, but the device is NEVER
            // connected — the mandatory regression: the manual flow must be byte-for-byte the old flow.
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            vm.MeasurementStepOverrideForTest = SeedingMeasurementOverride(vm);

            vm.StartCommand.Execute(null);
            while (!vm.IsComplete) {
                ((AsyncRelayCommand)vm.RunMeasurementCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            }

            Assert.Multiple(() => {
                Assert.That(vm.IsComplete, Is.True);
                controller.DidNotReceive().ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                options.Received(1).DeviceLinkedCalibrationDeviceName = string.Empty;
                Assert.That(service.TryBeginOperation("anything"), Is.Not.Null, "a disconnected run must never hold the device operation lease");
            });
        }

        [Test]
        public void Disconnected_StepInstructions_MatchesManualTextExactly_EvenForAMotorizedPreset() {
            var (vm, options, service, controller, _, _) = BuildMotorized(); // motorized preset, never connected
            string expected = TiltAdapterWizardVM.StepInstructionsText(WizardStep.Baseline, 4, true, vm.CalibrationAppliedAmount);
            Assert.That(vm.StepInstructions, Is.EqualTo(expected));
        }

        [Test]
        public void ConnectedMotorized_StepInstructions_ShowsAutomatedTextInsteadOfManual() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            Connect(vm);

            string manualText = TiltAdapterWizardVM.StepInstructionsText(WizardStep.Baseline, 4, true, vm.CalibrationAppliedAmount);
            Assert.Multiple(() => {
                Assert.That(vm.StepInstructions, Is.Not.EqualTo(manualText));
                Assert.That(vm.StepInstructions, Does.Contain("wizard will drive"));
            });
        }

        [Test]
        public void ConnectedRun_ManualStepByStep_ReachingComplete_SetsDeviceLinkedMarker() {
            // Drives the 4-step flow via repeated "Run Measurement" clicks (NOT AutoRunAllCommand) to prove
            // the marker is tied to "connected + device-driven", not specifically to Auto Run All.
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            options.MeasureCurvatureDuringCalibration.Returns(false); // re-assert AFTER connecting; see the 4-step-flow test above
            vm.MeasurementStepOverrideForTest = SeedingMeasurementOverride(vm);

            vm.StartCommand.Execute(null);
            while (!vm.IsComplete) {
                ((AsyncRelayCommand)vm.RunMeasurementCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            }

            Assert.Multiple(() => {
                Assert.That(vm.IsComplete, Is.True);
                controller.Received(1).ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalB && m.Steps == -150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                options.Received(1).DeviceLinkedCalibrationDeviceName = "ASG Electronic EAT - 90mm";
            });
        }

        // Fix 2 [MAJOR]: a "Use Saved AF" measurement mid-run must drop the run out of device-driven mode --
        // the marker must CLEAR (not be set) at completion, and no further device moves (including any later
        // step's move or Complete's restore move) may be sent once this has happened.
        [Test]
        public void MeasureStep_SavedAFStepDuringDeviceDrivenRun_ClearsDeviceLinkedMarker_AndSendsNoFurtherDeviceMoves() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            options.MeasureCurvatureDuringCalibration.Returns(false); // 4-step flow; re-assert AFTER connecting
            vm.MeasurementStepOverrideForTest = SeedingMeasurementOverride(vm);

            vm.StartCommand.Execute(null);
            ((AsyncRelayCommand)vm.RunMeasurementCommand).ExecuteAsync(null).GetAwaiter().GetResult(); // Baseline (no move)
            ((AsyncRelayCommand)vm.RunMeasurementCommand).ExecuteAsync(null).GetAwaiter().GetResult(); // Screw1 (real device move)
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.ReBaseline2), "precondition: past Screw1, mid-run");
            controller.Received(1).ExecuteMoveAsync(Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == 150), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
            controller.ClearReceivedCalls();

            // ReBaseline2 measured via "Use Saved AF" instead of a device move -- the run drops out of
            // device-driven mode from this point on.
            ((AsyncRelayCommand)vm.UseSavedAFCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Multiple(() => {
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Screw2), "precondition: past ReBaseline2");
                Assert.That(vm.HasDeviceLinkDroppedWarning, Is.True);
                controller.DidNotReceive().ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                Assert.That(service.TryBeginOperation("anything"), Is.Not.Null,
                    "the exclusive lease must be released the moment the run drops out of device-driven mode");
            });

            // Screw2 + the Complete transition: even a NORMAL (non-saved) "Run Measurement" click must send no
            // device move for the remainder of this run, and no Complete restore move either.
            ((AsyncRelayCommand)vm.RunMeasurementCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(vm.IsComplete, Is.True);
                controller.DidNotReceive().ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                options.Received(1).DeviceLinkedCalibrationDeviceName = string.Empty;
            });
        }

        [Test]
        public void MeasureStep_RetryAfterMeasurementFailure_DoesNotResendDeviceMove() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            StubSuccessfulMoves(controller);
            Connect(vm);
            // Connecting defaults MeasureCurvatureDuringCalibration ON (T11 item 4); re-assert false here,
            // AFTER connecting, so StartAsync (which reads it fresh) resolves the 4-step flow (Screw1 is the
            // second step) — this test only cares about retry-safety, not the curvature-measuring flow.
            options.MeasureCurvatureDuringCalibration.Returns(false);

            vm.StartCommand.Execute(null);
            // Advance past Baseline (measurement-only; no device move) so the retry-safety check below is
            // exercised for a step that actually has a move (Screw1).
            vm.MeasurementStepOverrideForTest = (step, ct) => { vm.SeedStepReading(step, 0.0, 0.0, 1000.0); return Task.FromResult(true); };
            ((AsyncRelayCommand)vm.RunMeasurementCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Screw1), "precondition: past Baseline");

            int attempt = 0;
            vm.MeasurementStepOverrideForTest = (step, ct) => {
                attempt++;
                if (attempt == 1) {
                    return Task.FromResult(false); // first attempt: the move succeeds, the "measurement" fails
                }
                vm.SeedStepReading(step, 0.5, 0.0, 1000.0);
                return Task.FromResult(true);
            };

            ((AsyncRelayCommand)vm.RunMeasurementCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.That(vm.HasMeasurementFailureChoice, Is.True, "precondition: the first attempt's measurement failed");

            ((AsyncRelayCommand)vm.RetryMeasurementCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(vm.HasMeasurementFailureChoice, Is.False);
                // Sent exactly ONCE despite two measurement attempts -- a retry after a measurement-only
                // failure must not re-apply the same device move a second time.
                controller.Received(1).ExecuteMoveAsync(
                    Arg.Is<TiltAdapterMove>(m => m.Axis == TiltMoveAxis.DiagonalA && m.Steps == 150),
                    Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
            });
        }

        [Test]
        public void StartAsync_DeviceDriven_HoldsOperationTokenForWholeRun_BlockingConcurrentOperations() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            Connect(vm);

            vm.StartCommand.Execute(null);

            Assert.Multiple(() => {
                Assert.That(vm.IsWizardRunning, Is.True);
                Assert.That(service.TryBeginOperation("someone else"), Is.Null, "the wizard's own lease must block a concurrent operation");
            });

            vm.RestartCommand.Execute(null);
            Assert.That(service.TryBeginOperation("someone else"), Is.Not.Null, "abandoning the run (Restart) must release the lease");
        }

        [Test]
        public void StartAsync_DeviceDriven_RefusesWhenAppliedAmountExceedsMaxStepsPerCommand() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(300.0);
            options.TiltDeviceMaxStepsPerCommand.Returns(200);
            Connect(vm);

            vm.StartCommand.Execute(null);

            Assert.Multiple(() => {
                Assert.That(vm.IsWizardRunning, Is.False, "must refuse to start a device-driven run whose applied amount exceeds the configured max steps per command");
                controller.DidNotReceive().ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
                Assert.That(service.TryBeginOperation("anything"), Is.Not.Null, "no lease should have been acquired");
            });
        }

        [Test]
        public void StartAsync_DeviceDriven_RefusesWhenAppliedAmountExceedsMaxExcursion() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.CalibrationAppliedAmount.Returns(150.0);
            options.TiltDeviceMaxExcursionSteps.Returns(100);
            Connect(vm);

            vm.StartCommand.Execute(null);

            Assert.That(vm.IsWizardRunning, Is.False);
        }

        [Test]
        public void RunCalibrationForTest_DeviceDriven_SetsDeviceLinkedMarker() {
            var (vm, options, _, _, _, _) = BuildMotorized();
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0);

            vm.RunCalibrationForTest(deviceDriven: true);

            options.Received(1).DeviceLinkedCalibrationDeviceName = "ASG Electronic EAT - 90mm";
        }

        [Test]
        public void RunCalibrationForTest_NotDeviceDriven_ClearsDeviceLinkedMarker() {
            // deviceDriven defaults to false — this is exactly the parameter value ReplayAsync's two
            // RunCalibrationMath call sites (and a disconnected live run's NextStep call) pass.
            var (vm, options, _, _) = Build(screwCount: 4);
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0);

            vm.RunCalibrationForTest();

            options.Received(1).DeviceLinkedCalibrationDeviceName = string.Empty;
        }

        // End-to-end ReplayAsync, via the three test seams (SelectReplayFolderForTest / SelectReplaySettingsForTest /
        // ReplayStepOverrideForTest + CalibrationTiltPlaneOverrideForTest) that stand in for the real FolderBrowserDialog,
        // the shared WPF replay-settings modal, and the live inspector analysis respectively -- none of which can run
        // headless. Proves ReplayAsync's own RunCalibrationMath call site (deviceDriven: false, always) actually clears a
        // PRE-EXISTING device-linked marker, not just that RunCalibrationMath does so in isolation (already covered by
        // RunCalibrationForTest_NotDeviceDriven_ClearsDeviceLinkedMarker above).
        // A replay must drive CurrentStep exactly like a live run does, so the wizard header ("Step N of M", the
        // step title, and the instruction paragraph -- all derived from currentStep and re-raised only by the
        // CurrentStep setter) tracks the step actually being re-analyzed. Regression: the replay loop used to
        // leave currentStep pinned at Baseline for the whole run, so every step read "Step 1 of 6" under a
        // "Baseline Measurement" header telling the user to click a button that is collapsed during a replay.
        [Test]
        public void ReplayAsync_AdvancesStepHeaderThroughEveryReplayedStep() {
            var (vm, _, _, _) = Build(screwCount: 3,
                configureOptions: o => o.MeasureCurvatureDuringCalibration.Returns(true));

            string runRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hf-tilt-replay-test-" + Guid.NewGuid().ToString("N"));
            // The 6-step shape (curvature steps saved), which is where the reported "Step 1 of 6" was seen.
            var stepFolders = new[] { "01_Baseline", "02_AllInward", "03_ReBaseline1", "04_Screw1", "05_ReBaseline2", "06_Screw2" };
            var steps = new[] { WizardStep.Baseline, WizardStep.AllInward, WizardStep.ReBaseline1, WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 };
            System.IO.Directory.CreateDirectory(runRoot);
            try {
                foreach (var stepFolder in stepFolders) {
                    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(runRoot, stepFolder));
                }
                var metadata = new TiltCalibrationMetadata {
                    NumberOfScrews = 3,
                    PixelSizeMicrons = 3.76,
                    FocuserStepSizeMicrons = 3.6,
                    ScrewRadiusMillimeters = 44,
                    CalibrationAppliedAmount = 1.0,
                    RunStepMapping = steps.Select((s, i) => new TiltRunStepMapping { Step = s.ToString(), Folder = stepFolders[i] }).ToList()
                };
                System.IO.File.WriteAllText(System.IO.Path.Combine(runRoot, "metadata.json"), metadata.Serialize());

                var tiltPlane = new TiltPlaneModel(new System.Drawing.Size(6248, 4176), fRatio: 7,
                    a: 0.1, b: 0.05, c: 0, mean: 7000, focuserStepSizeMicrons: 3.6,
                    centerPosition: 7000, topLeftPosition: 7000, topRightPosition: 7000,
                    bottomLeftPosition: 7000, bottomRightPosition: 7000);
                vm.CalibrationTiltPlaneOverrideForTest = tiltPlane;
                vm.SelectReplayFolderForTest = _ => runRoot;
                vm.SelectReplaySettingsForTest = _ => Task.FromResult(ReplaySettingsChoice.UseCurrentSettings);

                // The per-step seam doubles as the observer: it runs while the VM is mid-step, so it sees exactly
                // what the wizard panel would be showing at that moment.
                var observed = new List<(WizardStep step, string progress, string title, string status, string instructions)>();
                vm.ReplayStepOverrideForTest = (step, ct) => {
                    observed.Add((step, vm.StepProgressDisplay, vm.StepTitle, vm.StatusText, vm.StepInstructions));
                    return Task.FromResult(true);
                };

                ((AsyncRelayCommand)vm.ReplayCommand).ExecuteAsync(null).GetAwaiter().GetResult();

                Assert.Multiple(() => {
                    Assert.That(vm.IsComplete, Is.True, "precondition: the replay actually ran to completion");
                    Assert.That(observed.Select(o => o.step), Is.EqualTo(steps), "every saved step is replayed, in order");
                    Assert.That(observed.Select(o => o.progress), Is.EqualTo(new[] {
                        "Step 1 of 6", "Step 2 of 6", "Step 3 of 6", "Step 4 of 6", "Step 5 of 6", "Step 6 of 6" }));
                    // The title must track the step too -- same root cause, separately visible to the user.
                    Assert.That(observed.Select(o => o.title),
                        Is.EqualTo(steps.Select(TiltAdapterWizardVM.StepTitleText)));
                    // Status text uses the human step title, not the raw enum name.
                    Assert.That(observed.Select(o => o.status),
                        Is.EqualTo(steps.Select(s => $"Replaying {TiltAdapterWizardVM.StepTitleText(s)}...")));
                    // A replay re-analyzes saved frames: the instruction paragraph must not tell the user to turn
                    // screws or click a button that is collapsed for the whole replay.
                    Assert.That(observed.Select(o => o.instructions),
                        Is.EqualTo(steps.Select(TiltAdapterWizardVM.ReplayStepInstructionsText)));
                    Assert.That(observed.Select(o => o.instructions),
                        Has.None.Contains("Run Measurement").And.None.Contains("CLOCKWISE"));
                    // The replay flag is transient: it must be cleared once the replay finishes.
                    Assert.That(vm.IsReplaying, Is.False);
                });
            } finally {
                System.IO.Directory.Delete(runRoot, recursive: true);
            }
        }

        [Test]
        public void ReplayAsync_ClearsAPreExistingDeviceLinkedMarker() {
            var (vm, options, _, _) = Build(screwCount: 3);
            // Simulate the marker was set by an earlier, unrelated device-driven calibration.
            options.DeviceLinkedCalibrationDeviceName = "ASG Electronic EAT - 90mm";

            string runRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hf-tilt-replay-test-" + Guid.NewGuid().ToString("N"));
            var stepFolders = new[] { "01_Baseline", "02_Screw1", "03_ReBaseline2", "04_Screw2" };
            System.IO.Directory.CreateDirectory(runRoot);
            try {
                foreach (var stepFolder in stepFolders) {
                    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(runRoot, stepFolder));
                }
                var metadata = new TiltCalibrationMetadata {
                    NumberOfScrews = 3,
                    PixelSizeMicrons = 3.76,
                    FocuserStepSizeMicrons = 3.6,
                    ScrewRadiusMillimeters = 44,
                    CalibrationAppliedAmount = 1.0,
                    RunStepMapping = new List<TiltRunStepMapping> {
                        new TiltRunStepMapping { Step = "Baseline", Folder = stepFolders[0] },
                        new TiltRunStepMapping { Step = "Screw1", Folder = stepFolders[1] },
                        new TiltRunStepMapping { Step = "ReBaseline2", Folder = stepFolders[2] },
                        new TiltRunStepMapping { Step = "Screw2", Folder = stepFolders[3] },
                    }
                };
                System.IO.File.WriteAllText(System.IO.Path.Combine(runRoot, "metadata.json"), metadata.Serialize());

                var tiltPlane = new TiltPlaneModel(new System.Drawing.Size(6248, 4176), fRatio: 7,
                    a: 0.1, b: 0.05, c: 0, mean: 7000, focuserStepSizeMicrons: 3.6,
                    centerPosition: 7000, topLeftPosition: 7000, topRightPosition: 7000,
                    bottomLeftPosition: 7000, bottomRightPosition: 7000);
                vm.CalibrationTiltPlaneOverrideForTest = tiltPlane;
                vm.SelectReplayFolderForTest = _ => runRoot;
                vm.SelectReplaySettingsForTest = _ => Task.FromResult(ReplaySettingsChoice.UseCurrentSettings);
                vm.ReplayStepOverrideForTest = (step, ct) => Task.FromResult(true);

                ((AsyncRelayCommand)vm.ReplayCommand).ExecuteAsync(null).GetAwaiter().GetResult();

                Assert.Multiple(() => {
                    Assert.That(vm.IsComplete, Is.True, "precondition: the replay actually completed (RunCalibrationMath was reached)");
                    options.Received(1).DeviceLinkedCalibrationDeviceName = string.Empty;
                });
            } finally {
                System.IO.Directory.Delete(runRoot, recursive: true);
            }
        }

        // Task 6: a saved run captured WITH the optional measured final re-baseline replays as a 5-step run
        // (ReBaseline3 detected from RunStepMapping, exactly how AllInward's presence already detects the
        // 6- vs 4-step curvature flow) -- the "think rather than transcribe" backward-compatibility point:
        // a metadata file predating this feature simply has no "ReBaseline3" entry, so byStep.ContainsKey
        // comes back false and this replays as an ordinary 4-step run (already covered by every OTHER
        // ReplayAsync test above, none of which include a ReBaseline3 folder).
        [Test]
        public void ReplayAsync_WithReBaseline3InMetadata_ReplaysFiveStepsAndCompletes() {
            var (vm, _, _, _) = Build(screwCount: 4);

            string runRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hf-tilt-replay-rb3-test-" + Guid.NewGuid().ToString("N"));
            var stepFolders = new[] { "01_Baseline", "02_Screw1", "03_ReBaseline2", "04_Screw2", "07_ReBaseline3" };
            var steps = new[] { WizardStep.Baseline, WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2, WizardStep.ReBaseline3 };
            System.IO.Directory.CreateDirectory(runRoot);
            try {
                foreach (var stepFolder in stepFolders) {
                    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(runRoot, stepFolder));
                }
                var metadata = new TiltCalibrationMetadata {
                    NumberOfScrews = 4,
                    PixelSizeMicrons = 3.76,
                    FocuserStepSizeMicrons = 3.6,
                    ScrewRadiusMillimeters = 44,
                    CalibrationAppliedAmount = 1.0,
                    RunStepMapping = steps.Select((s, i) => new TiltRunStepMapping { Step = s.ToString(), Folder = stepFolders[i] }).ToList()
                };
                System.IO.File.WriteAllText(System.IO.Path.Combine(runRoot, "metadata.json"), metadata.Serialize());

                var tiltPlane = new TiltPlaneModel(new System.Drawing.Size(6248, 4176), fRatio: 7,
                    a: 0.1, b: 0.05, c: 0, mean: 7000, focuserStepSizeMicrons: 3.6,
                    centerPosition: 7000, topLeftPosition: 7000, topRightPosition: 7000,
                    bottomLeftPosition: 7000, bottomRightPosition: 7000);
                vm.CalibrationTiltPlaneOverrideForTest = tiltPlane;
                vm.SelectReplayFolderForTest = _ => runRoot;
                vm.SelectReplaySettingsForTest = _ => Task.FromResult(ReplaySettingsChoice.UseCurrentSettings);

                var observedSteps = new List<WizardStep>();
                vm.ReplayStepOverrideForTest = (step, ct) => {
                    observedSteps.Add(step);
                    return Task.FromResult(true);
                };

                ((AsyncRelayCommand)vm.ReplayCommand).ExecuteAsync(null).GetAwaiter().GetResult();

                Assert.Multiple(() => {
                    Assert.That(vm.IsComplete, Is.True, "precondition: the replay actually ran to completion");
                    Assert.That(observedSteps, Is.EqualTo(steps), "every saved step, including the optional ReBaseline3, is replayed in order");
                });
            } finally {
                System.IO.Directory.Delete(runRoot, recursive: true);
            }
        }

        // Fix 4 (post-review): CornerTiltPlaneOverrideForTest mirrors CalibrationTiltPlaneOverrideForTest but
        // was never exercised by a test -- ReplayAsync's corner-capture line only ever ran its null/NaN
        // fallback. Sets BOTH overrides per replayed step (a fresh TiltPlaneModel each time, since
        // ReplayStepOverrideForTest runs before ReplayAsync reads CalibrationTiltPlane/corner for that step) to
        // the SAME 25%-higher-corner geometry as RunCalibrationForTest_CornerMagnitudes25PercentHigherThan-
        // Paraboloid... above, proving the real capture site (not just the SeedStepReading test shortcut) wires
        // through to the cross-check end to end. UseCaptureTimeSettingsInMemory (rather than UseCurrentSettings,
        // used by the other ReplayAsync tests) so RunCalibrationMath's geometry comes entirely from `metadata`
        // (radius/pixel/focuser-step/applied all under test control), matching the isotropic-sensor convention
        // the seeded-reading tests use -- not from the unconfigured profile/options substitutes.
        [Test]
        public void ReplayAsync_CapturesCornerReadingsAndTheCrossCheckFires() {
            var (vm, _, _, _) = Build(screwCount: 4);

            string runRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hf-tilt-replay-corner-test-" + Guid.NewGuid().ToString("N"));
            var stepFolders = new[] { "01_Baseline", "02_Screw1", "03_ReBaseline2", "04_Screw2" };
            System.IO.Directory.CreateDirectory(runRoot);
            try {
                foreach (var stepFolder in stepFolders) {
                    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(runRoot, stepFolder));
                }
                var metadata = new TiltCalibrationMetadata {
                    NumberOfScrews = 4,
                    PixelSizeMicrons = 1,
                    FocuserStepSizeMicrons = 1,
                    ScrewRadiusMillimeters = 44,
                    CalibrationAppliedAmount = 1.0,
                    RunStepMapping = new List<TiltRunStepMapping> {
                        new TiltRunStepMapping { Step = "Baseline", Folder = stepFolders[0] },
                        new TiltRunStepMapping { Step = "Screw1", Folder = stepFolders[1] },
                        new TiltRunStepMapping { Step = "ReBaseline2", Folder = stepFolders[2] },
                        new TiltRunStepMapping { Step = "Screw2", Folder = stepFolders[3] },
                    }
                };
                System.IO.File.WriteAllText(System.IO.Path.Combine(runRoot, "metadata.json"), metadata.Serialize());

                // Paraboloid: clean (0,-5e-5)/(5e-5,0) deltas (magnitude 5e-5 each). Corner: the SAME deltas
                // scaled 25% higher (magnitude 6.25e-5 each) -- identical numbers to the seeded-reading
                // disagreement test, so the expected outputs below are the same known values (2.75 µm/turn).
                var paraboloidByStep = new Dictionary<WizardStep, (double a, double b)> {
                    [WizardStep.Baseline] = (0.0, 0.0),
                    [WizardStep.Screw1] = (0.0, -0.00005),
                    [WizardStep.ReBaseline2] = (0.0, 0.0),
                    [WizardStep.Screw2] = (0.00005, 0.0),
                };
                var cornerByStep = new Dictionary<WizardStep, (double a, double b)> {
                    [WizardStep.Baseline] = (0.0, 0.0),
                    [WizardStep.Screw1] = (0.0, -0.0000625),
                    [WizardStep.ReBaseline2] = (0.0, 0.0),
                    [WizardStep.Screw2] = (0.0000625, 0.0),
                };
                TiltPlaneModel PlaneFor(double a, double b) => new TiltPlaneModel(new System.Drawing.Size(1, 1), fRatio: 7,
                    a: a, b: b, c: 0, mean: 1000, focuserStepSizeMicrons: 1,
                    centerPosition: 1000, topLeftPosition: 1000, topRightPosition: 1000,
                    bottomLeftPosition: 1000, bottomRightPosition: 1000);

                vm.SelectReplayFolderForTest = _ => runRoot;
                vm.SelectReplaySettingsForTest = _ => Task.FromResult(ReplaySettingsChoice.UseCaptureTimeSettingsInMemory);
                vm.ReplayStepOverrideForTest = (step, ct) => {
                    var (pa, pb) = paraboloidByStep[step];
                    vm.CalibrationTiltPlaneOverrideForTest = PlaneFor(pa, pb);
                    var (ca, cb) = cornerByStep[step];
                    vm.CornerTiltPlaneOverrideForTest = PlaneFor(ca, cb);
                    return Task.FromResult(true);
                };

                ((AsyncRelayCommand)vm.ReplayCommand).ExecuteAsync(null).GetAwaiter().GetResult();

                Assert.Multiple(() => {
                    Assert.That(vm.IsComplete, Is.True, "precondition: the replay actually completed (RunCalibrationMath was reached)");
                    Assert.That(vm.HasCornerCrossCheck, Is.True);
                    Assert.That(vm.CornerCrossCheckDisplay, Is.EqualTo("Corner-AF cross-check: 2.75 µm/turn"));
                    Assert.That(vm.WarningText, Does.Contain("the per-star model and the corner-region AF disagree on the screw moves by"));
                    Assert.That(vm.WarningText, Does.Contain("corner-AF estimate: 2.75 µm"));
                });
            } finally {
                System.IO.Directory.Delete(runRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyManualCalibrationCommand_ClearsDeviceLinkedMarker() {
            var (vm, options, _, _) = Build();
            vm.ManualScrew1AngleDegrees = 30.0;

            vm.ApplyManualCalibrationCommand.Execute(null);

            options.Received(1).DeviceLinkedCalibrationDeviceName = string.Empty;
        }

        // Fix 1 [CRITICAL GATE, MAJOR]: RunCalibrationMath must persist the confidence result's IsReliable to
        // ITiltAdapterOptions.CalibrationIsReliable, independent of (in addition to) DeviceLinkedCalibrationDeviceName.
        [Test]
        public void RunCalibrationForTest_DeviceDriven_ReliableCalibration_SetsCalibrationIsReliableTrue() {
            var (vm, options, _, _, _, _) = BuildMotorized();
            // Clean, equal-magnitude single-screw moves with zero re-baseline drift -> noise probe 0 ->
            // SignalToNoise = +Infinity -> reliable (mirrors the seed data RunCalibrationForTest_DeviceDriven_SetsDeviceLinkedMarker uses).
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0);

            // No tiltPlaneOverride -> RunCalibrationMath's inputs fall back to a 1x1 image size, so real (any
            // positive, equal-for-both-axes) pixel/focuser sizes must be supplied explicitly: PhysicalDelta has
            // no fallback for a zero sensor size (geometry is a precondition -- see its doc comment), and
            // BuildMotorized's mocked profile otherwise leaves PixelSize at its NSubstitute default of 0. A 1x1
            // image size keeps the conversion isotropic, so the SNR ratio (and thus IsReliable) is unchanged
            // from the pre-F2-fix raw-(A,B) computation this test was written against.
            vm.RunCalibrationForTest(deviceDriven: true, pixelSizeMicrons: 3.76, focuserStepMicrons: 3.6);

            options.Received(1).CalibrationIsReliable = true;
        }

        [Test]
        public void RunCalibrationForTest_DeviceDriven_UnreliableCalibration_SetsCalibrationIsReliableFalse_EvenThoughDeviceLinked() {
            var (vm, options, _, _, _, _) = BuildMotorized();
            // ReBaseline2 drifts substantially from Baseline (0.4 of the ~0.5 screw-move signal) instead of
            // returning to it -> noise probe large relative to signal -> SignalToNoise ~1.25, below
            // TiltCalibrationCalculator.MinReliableSignalToNoise (2.0) -> not reliable, even though this is
            // still a completed, connected, device-driven run (device-linked and reliable are independent gates).
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.4, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, 0.9, 0.0, 1000.0);

            // Explicit isotropic geometry -- see RunCalibrationForTest_DeviceDriven_ReliableCalibration_SetsCalibrationIsReliableTrue.
            vm.RunCalibrationForTest(deviceDriven: true, pixelSizeMicrons: 3.76, focuserStepMicrons: 3.6);

            Assert.Multiple(() => {
                options.Received(1).CalibrationIsReliable = false;
                options.Received(1).DeviceLinkedCalibrationDeviceName = "ASG Electronic EAT - 90mm";
            });
        }

        // F2 regression lock at the VM<->calculator seam: RunCalibrationMath must persist the PHYSICAL-space
        // screw angles/ratio TiltCalibrationCalculator.Calibate() computes, not raw (A,B)-tilt-plane-coefficient
        // values. The bug this refactor fixed (RunCalibrationMath hand-mirroring angle/ratio algebra in raw
        // (A,B) space) manifested exactly at this seam, and the calculator-level anisotropic tests alone can't
        // catch a future edit that bypasses Calibrate() again here.
        //
        // Two single-screw moves of EQUAL physical magnitude at physical directions 200 deg and 290 deg (90 deg
        // apart -- this adapter's ideal 4-screw spacing) on a strongly non-square 6000x2000 image. Physically
        // (what Calibrate() now correctly produces): Screw1 = 200 deg exactly, Screw2 = 290 deg exactly,
        // RawAngleDiffDegrees = 90 deg, MoveMagnitudeRatio = 1.0 -- no warning.
        // In raw (A,B) space (what the pre-refactor/pre-F2-fix inline math in RunCalibrationMath produced for
        // these exact same deltas): Screw1 ~= 207.22 deg, Screw2 ~= 297.22 deg, RawAngleDiffDegrees ~= 49.4 deg,
        // MoveMagnitudeRatio ~= 2.04 -- comfortably past the 1.5x "unequal tilt changes" warning threshold.
        [Test]
        public void RunCalibrationForTest_AnisotropicSensor_PersistsPhysicalSpaceScrewAnglesNotRawSpace() {
            var (vm, options, _, _) = Build(screwCount: 4);
            var tiltPlane = new TiltPlaneModel(new System.Drawing.Size(6000, 2000), fRatio: 7,
                a: 0, b: 0, c: 0, mean: 1000, focuserStepSizeMicrons: 0.5,
                centerPosition: 1000, topLeftPosition: 1000, topRightPosition: 1000,
                bottomLeftPosition: 1000, bottomRightPosition: 1000);
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, -30.86389773370834, 28.265954033240128, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, -84.79786209972038, -10.287965911236123, 1000.0);

            vm.RunCalibrationForTest(pixelSizeMicrons: 3.76, focuserStepMicrons: 0.5, tiltPlaneOverride: tiltPlane);

            Assert.Multiple(() => {
                options.Received(1).Screw1AngleDegrees = Arg.Is<double>(v => Math.Abs(v - 200.0) < 0.01);
                options.Received(1).Screw2AngleDegrees = Arg.Is<double>(v => Math.Abs(v - 290.0) < 0.01);
                // MoveMagnitudeRatio has no direct public getter, but it feeds the very same
                // ValidateCalibrationQuality warning check as the angle gap: the physical ratio (1.0) is
                // nowhere near the 1.5x threshold, whereas the raw-(A,B) ratio (~2.04x) would have tripped it
                // -- so a clean (no-warning) WarningText is itself evidence MoveMagnitudeRatio came from
                // Calibrate()'s physical result, not a raw-(A,B) recomputation.
                Assert.That(vm.HasWarning, Is.False);
                Assert.That(vm.WarningText, Is.Empty);
            });
        }

        // --- Piston-implied-pitch warning wiring (Task 4) -------------------------------------------------

        // Both piston tests share the same clean screw geometry: Screw1's delta is (0, -g), Screw2's is
        // (g, 0) with g = 5e-5. A 1x1 isotropic tiltPlaneOverride + unit pixel size/focuser step (see
        // RunCalibrationForTest below) makes PhysicalDelta byte-identical to these raw (A,B) seeds -- no
        // forward/inverse conversion needed to reason about the numbers. That gives:
        //  - equal magnitudes (ratio 1.0, comfortably under the 1.5x MagnitudeRatioWarnThreshold)
        //  - a clean 90 deg gap (screw count 4's exact expected spacing, no angle warning)
        //  - CalibrationAppliedAmount defaults to 1 turn (unconfigured options resolve to the VM's "Manual"
        //    device-preset fallback -- see CalibrationAppliedAmount's getter), and the 4-screw lever arm is
        //    the screw radius itself, so measured (tilt-derived) hardware = g * radiusMicrons / applied
        //    = 0.00005 * 44000 / 1 = 2.2 um/turn.
        // So the only thing that varies between the two tests below is AllInward's mean focuser position,
        // which drives the piston-implied pitch relative to that fixed 2.2 um/turn -- isolating the piston
        // check from the angle/magnitude checks (which never contribute a warning part in either case).
        private static TiltPlaneModel IsotropicPistonWarningTiltPlane() =>
            new TiltPlaneModel(new System.Drawing.Size(1, 1), fRatio: 7,
                a: 0, b: 0, c: 0, mean: 1000, focuserStepSizeMicrons: 1,
                centerPosition: 1000, topLeftPosition: 1000, topRightPosition: 1000,
                bottomLeftPosition: 1000, bottomRightPosition: 1000);

        [Test]
        public void RunCalibrationForTest_PistonDisagreesWithMeasuredHardwareBy30Percent_WarnsAndDisplaysPiston() {
            var (vm, _, _, _) = Build(screwCount: 4);
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            // AllInward's mean sits 2.86 focuser units below the Baseline/ReBaseline1 midpoint (both 1000.0,
            // so there is no drift to correct for): piston = |1000.0 - 997.14| * fStep(1) / applied(1) =
            // 2.86 um/turn. Relative to measured = 2.2 um/turn, that is a 30% disagreement (2.86 / 2.2 =
            // 1.3) -- comfortably over the 20% PistonDisagreementWarnThreshold.
            vm.SeedStepReading(WizardStep.AllInward, 0.0, 0.0, 997.14);
            vm.SeedStepReading(WizardStep.ReBaseline1, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.0, -0.00005, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, 0.00005, 0.0, 1000.0);

            vm.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            Assert.Multiple(() => {
                Assert.That(vm.HasWarning, Is.True);
                Assert.That(vm.WarningText, Does.Contain(
                    "piston-implied hardware (2.86 µm) and tilt-derived (2.2 µm) disagree by more than 20% " +
                    "— the tilt estimate may be unreliable"));
                Assert.That(vm.PistonPitchDisplay, Is.EqualTo("Piston-implied: 2.9 µm/turn"));
            });
        }

        [Test]
        public void RunCalibrationForTest_PistonAgreesWithMeasuredHardwareWithin20Percent_NoWarning() {
            var (vm, _, _, _) = Build(screwCount: 4);
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            // Same fixed measured hardware (2.2 um/turn) as the disagreement test above, but AllInward's mean
            // is only 2.53 focuser units below the Baseline/ReBaseline1 midpoint: piston = 2.53 um/turn, a
            // 15% relative difference from measured (2.53 / 2.2 = 1.15) -- comfortably under the 20%
            // threshold without being close to zero, so this is a meaningful negative case, not a trivial one.
            vm.SeedStepReading(WizardStep.AllInward, 0.0, 0.0, 997.47);
            vm.SeedStepReading(WizardStep.ReBaseline1, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.0, -0.00005, 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, 0.00005, 0.0, 1000.0);

            vm.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            Assert.Multiple(() => {
                Assert.That(vm.HasWarning, Is.False);
                Assert.That(vm.WarningText, Is.Empty);
                Assert.That(vm.PistonPitchDisplay, Is.EqualTo("Piston-implied: 2.5 µm/turn"));
            });
        }

        // --- Corner-region AF cross-check wiring (Task 5) -------------------------------------------------

        // All three tests below share the same clean paraboloid geometry as the piston tests above (Screw1
        // delta (0,-5e-5), Screw2 delta (5e-5,0) — equal magnitudes, a clean 90° gap, no AllInward/ReBaseline1
        // step so no piston check contributes either) and the same isotropic 1x1 pseudo-sensor, so the ONLY
        // thing that varies between them is the corner-region reading — isolating the cross-check from every
        // other warning the same way the piston tests isolate PistonAgreement.
        private static void SeedCleanParaboloidReadings(TiltAdapterWizardVM vm,
            double screw1CornerA, double screw1CornerB, double screw2CornerA, double screw2CornerB) {
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0, cornerA: 0.0, cornerB: 0.0, cornerMean: 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.0, -0.00005, 1000.0, cornerA: screw1CornerA, cornerB: screw1CornerB, cornerMean: 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0, cornerA: 0.0, cornerB: 0.0, cornerMean: 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, 0.00005, 0.0, 1000.0, cornerA: screw2CornerA, cornerB: screw2CornerB, cornerMean: 1000.0);
        }

        [Test]
        public void RunCalibrationForTest_CornerCrossCheckAgreesWithin15Percent_NoNewWarningPart() {
            var (vm, _, _, _) = Build(screwCount: 4);
            // Corner deltas are a uniform 10% higher magnitude than the paraboloid's (0,-5.5e-5) / (5.5e-5,0)
            // vs (0,-5e-5) / (5e-5,0): relative difference |5e-5 - 5.5e-5| / 5.5e-5 = 9.09% -- comfortably
            // under the 15% threshold without being the trivial (identical-values) case.
            SeedCleanParaboloidReadings(vm, screw1CornerA: 0.0, screw1CornerB: -0.000055, screw2CornerA: 0.000055, screw2CornerB: 0.0);

            vm.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            Assert.Multiple(() => {
                Assert.That(vm.HasWarning, Is.False);
                Assert.That(vm.WarningText, Is.Empty);
                // The cross-check estimate itself is still computed and displayed regardless of whether it
                // disagrees enough to warn -- the display and the warning are separate signals (locked
                // decision: flag/display, never silently blend).
                Assert.That(vm.HasCornerCrossCheck, Is.True);
                Assert.That(vm.CornerCrossCheckDisplay, Is.EqualTo("Corner-AF cross-check: 2.42 µm/turn"));
            });
        }

        [Test]
        public void RunCalibrationForTest_CornerMagnitudes25PercentHigherThanParaboloid_WarnsAndDisplaysCornerEstimate() {
            var (vm, _, _, _) = Build(screwCount: 4);
            // Corner deltas are a uniform 25% higher magnitude than the paraboloid's: (0,-6.25e-5) /
            // (6.25e-5,0) vs (0,-5e-5) / (5e-5,0) -- relative difference |5e-5 - 6.25e-5| / 6.25e-5 = 20%,
            // comfortably over the 15% threshold.
            SeedCleanParaboloidReadings(vm, screw1CornerA: 0.0, screw1CornerB: -0.0000625, screw2CornerA: 0.0000625, screw2CornerB: 0.0);

            vm.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            Assert.Multiple(() => {
                Assert.That(vm.HasWarning, Is.True);
                Assert.That(vm.WarningText, Does.Contain("the per-star model and the corner-region AF disagree on the screw moves by"));
                Assert.That(vm.WarningText, Does.Contain("corner-AF estimate: 2.75 µm"));
                Assert.That(vm.HasCornerCrossCheck, Is.True);
                Assert.That(vm.CornerCrossCheckDisplay, Is.EqualTo("Corner-AF cross-check: 2.75 µm/turn"));
            });
        }

        [Test]
        public void RunCalibrationForTest_PartialCornerData_SkipsCrossCheckCleanly() {
            var (vm, _, _, _) = Build(screwCount: 4);
            // Same 25%-higher corner geometry as the disagreement test above -- WOULD warn if the cross-check
            // ran -- but Screw1's corner reading is left at its NaN default (never seeded), simulating a step
            // whose corner regions failed to fit / an older saved run captured before this feature shipped.
            // A partially-available run must degrade to "no cross-check" cleanly: no NaN-contaminated warning
            // text, no display, and — critically — no OTHER warning either (proving the missing corner data
            // doesn't leak NaN into the rest of ValidateCalibrationQuality).
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0, cornerA: 0.0, cornerB: 0.0, cornerMean: 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.0, -0.00005, 1000.0); // corner NaN -- not seeded
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0, cornerA: 0.0, cornerB: 0.0, cornerMean: 1000.0);
            vm.SeedStepReading(WizardStep.Screw2, 0.00005, 0.0, 1000.0, cornerA: 0.0000625, cornerB: 0.0, cornerMean: 1000.0);

            vm.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            Assert.Multiple(() => {
                Assert.That(vm.HasWarning, Is.False);
                Assert.That(vm.WarningText, Is.Empty);
                Assert.That(vm.HasCornerCrossCheck, Is.False);
                Assert.That(vm.CornerCrossCheckDisplay, Is.Empty);
            });
        }

        [Test]
        public void RunCalibrationForTest_StepEntirelyMissingFromReadings_SkipsCrossCheckCleanly() {
            var (vm, _, _, _) = Build(screwCount: 4);
            // Distinct from the partial-corner-data test above (which SEEDS Screw1 but leaves its corner
            // fields at their NaN default): here Screw2 is never seeded AT ALL, so stepReadings has no entry
            // for it. Reading(WizardStep.Screw2) then falls back to default(StepReading), whose CornerA/B/Mean
            // are a struct-default 0.0 -- NOT NaN -- so a completeness check built on the non-NaN test alone
            // would be fooled into treating this as a complete, valid (0,0,0) corner reading for Screw2 (Fix
            // 2, post-review). Screw2's corner delta would then be (0,0) - ReBaseline2's corner (0,0) = (0,0),
            // and 4-screw RecoverHardwareDetailed's own guard only rejects an overall measured <= 0 (not a
            // per-screw zero) -- averaging that zero against Screw1's real corner move still yields a
            // non-NaN, plausible-looking µm/turn, so this specific failure mode would NOT be caught by
            // downstream NaN propagation; only the explicit stepReadings.ContainsKey/TryGetValue check does.
            // (Screw2's paraboloid side is equally absent, which trips the UNRELATED screw-angle-gap warning
            // -- expected, and not asserted against; this test only cares about the corner outputs.)
            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0, cornerA: 0.0, cornerB: 0.0, cornerMean: 1000.0);
            vm.SeedStepReading(WizardStep.Screw1, 0.0, -0.00005, 1000.0, cornerA: 0.0, cornerB: -0.0000625, cornerMean: 1000.0);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0, cornerA: 0.0, cornerB: 0.0, cornerMean: 1000.0);
            // WizardStep.Screw2 intentionally NEVER seeded.

            vm.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            Assert.Multiple(() => {
                Assert.That(vm.HasCornerCrossCheck, Is.False);
                Assert.That(vm.CornerCrossCheckDisplay, Is.Empty);
                Assert.That(vm.WarningText, Does.Not.Contain("corner-region AF disagree"));
            });
        }

        // --- Task 6: optional measured final re-baseline wiring (RunCalibrationMath reads a seeded
        // ReBaseline3 reading and passes it -- and HasFinalRebaseline -- through the SAME shared MakeInputs
        // both the paraboloid and (Task 5) corner-cross-check estimators are built from) -----------------

        [Test]
        public void RunCalibrationForTest_ReBaseline3EqualToReBaseline2_MatchesRunWithoutFinalRebaseline() {
            // When the seeded ReBaseline3 reading is IDENTICAL to ReBaseline2, the drift-cancelling midpoint
            // mid(ReBaseline2, ReBaseline3) collapses to ReBaseline2 itself -- Screw2Delta becomes numerically
            // identical to the no-final-rebaseline reference (Screw2 - ReBaseline2 alone). Proves
            // RunCalibrationMath actually reads a seeded ReBaseline3 rather than ignoring it, without
            // re-deriving the drift-cancelling algebra itself (already pinned exactly by
            // TiltCalibrationCalculatorTests.Calibrate_WithFinalRebaseline_Screw2DeltaIsDriftImmune).
            var (vmWithout, _, _, _) = Build(screwCount: 4);
            vmWithout.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vmWithout.SeedStepReading(WizardStep.Screw1, 0.0, -0.00005, 1000.0);
            vmWithout.SeedStepReading(WizardStep.ReBaseline2, 0.0002, -0.0001, 1000.0);
            vmWithout.SeedStepReading(WizardStep.Screw2, 0.00005, 0.0, 1000.0);
            vmWithout.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            var (vmWith, _, _, _) = Build(screwCount: 4);
            vmWith.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vmWith.SeedStepReading(WizardStep.Screw1, 0.0, -0.00005, 1000.0);
            vmWith.SeedStepReading(WizardStep.ReBaseline2, 0.0002, -0.0001, 1000.0);
            vmWith.SeedStepReading(WizardStep.Screw2, 0.00005, 0.0, 1000.0);
            vmWith.SeedStepReading(WizardStep.ReBaseline3, 0.0002, -0.0001, 1000.0); // identical to ReBaseline2
            vmWith.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            Assert.Multiple(() => {
                Assert.That(vmWith.MeasuredHardwareDisplay, Is.EqualTo(vmWithout.MeasuredHardwareDisplay));
                Assert.That(vmWith.PitchUncertaintyDisplay, Is.EqualTo(vmWithout.PitchUncertaintyDisplay));
            });
        }

        [Test]
        public void RunCalibrationForTest_ReBaseline3DifferentFromReBaseline2_ChangesRecoveredHardware() {
            // Now ReBaseline3 genuinely differs from ReBaseline2 -- Screw2Delta references
            // mid(ReBaseline2, ReBaseline3) instead of ReBaseline2 alone, so the recovered hardware must
            // differ from the no-ReBaseline3 run. Proves the wizard is actually READING and USING the
            // seeded ReBaseline3 value (not silently ignoring it because HasFinalRebaseline never got wired).
            var (vmWithout, _, _, _) = Build(screwCount: 4);
            vmWithout.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vmWithout.SeedStepReading(WizardStep.Screw1, 0.0, -0.00005, 1000.0);
            vmWithout.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vmWithout.SeedStepReading(WizardStep.Screw2, 0.00005, 0.0, 1000.0);
            vmWithout.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            var (vmWith, _, _, _) = Build(screwCount: 4);
            vmWith.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
            vmWith.SeedStepReading(WizardStep.Screw1, 0.0, -0.00005, 1000.0);
            vmWith.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
            vmWith.SeedStepReading(WizardStep.Screw2, 0.00005, 0.0, 1000.0);
            vmWith.SeedStepReading(WizardStep.ReBaseline3, 0.00002, 0.00002, 1000.0); // drifted from ReBaseline2
            vmWith.RunCalibrationForTest(radiusMm: 44, pixelSizeMicrons: 1, focuserStepMicrons: 1,
                tiltPlaneOverride: IsotropicPistonWarningTiltPlane());

            Assert.That(vmWith.MeasuredHardwareDisplay, Is.Not.EqualTo(vmWithout.MeasuredHardwareDisplay));
        }

        [Test]
        public void StartAsync_WithMeasureFinalRebaseline_ExtendsActiveStepsToFive() {
            // StartAsync (the live-run caller of GetMeasurementSteps) must read BOTH options -- proves the
            // wiring reaches the real entry point, not just the static GetMeasurementSteps helper tested
            // directly above.
            var (vm, _, _, _) = Build(configureOptions: o => {
                o.MeasureCurvatureDuringCalibration.Returns(false);
                o.MeasureFinalRebaseline.Returns(true);
            });

            vm.StartCommand.Execute(null);

            Assert.That(vm.StepProgressDisplay, Is.EqualTo("Step 1 of 5"));
        }

        [Test]
        public void ApplyManualCalibrationCommand_SetsCalibrationIsReliableFalse() {
            // A manual entry has no confidence computation to reuse (no per-step tilt vectors) -- conservative
            // default: not automation-trusted until a fresh calibration run demonstrably passes its own check.
            var (vm, options, _, _) = Build();
            vm.ManualScrew1AngleDegrees = 30.0;

            vm.ApplyManualCalibrationCommand.Execute(null);

            options.Received(1).CalibrationIsReliable = false;
        }

        [Test]
        public void ConnectDeviceCommand_Success_DefaultsMeasureCurvatureDuringCalibrationOn_WhenNotAlreadyEnabled() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.MeasureCurvatureDuringCalibration.Returns(false);

            Connect(vm);

            options.Received(1).MeasureCurvatureDuringCalibration = true;
        }

        [Test]
        public void ConnectDeviceCommand_Success_DoesNotResendMeasureCurvature_WhenAlreadyTrue() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.MeasureCurvatureDuringCalibration.Returns(true);

            Connect(vm);

            options.DidNotReceive().MeasureCurvatureDuringCalibration = Arg.Any<bool>();
        }

        // Fix 3 [MINOR]: the on-connect default (T11 item 4, tested above) must never re-force a
        // deliberately disabled option back on across a disconnect -> reconnect cycle.
        [Test]
        public void ConnectDeviceCommand_UserExplicitlyDisabledMeasureCurvature_ReconnectDoesNotReForceOn() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.MeasureCurvatureDuringCalibration.Returns(false);

            Connect(vm); // first (and only) connect that may default it on
            options.Received(1).MeasureCurvatureDuringCalibration = true;

            // The user deliberately unchecks it (the checkbox binds straight to the option's setter, which
            // raises PropertyChanged in production; simulate that on the substitute).
            options.MeasureCurvatureDuringCalibration.Returns(false);
            options.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                options, new System.ComponentModel.PropertyChangedEventArgs(nameof(ITiltAdapterOptions.MeasureCurvatureDuringCalibration)));
            options.ClearReceivedCalls();

            ((AsyncRelayCommand)vm.DisconnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();
            Connect(vm); // reconnect must NOT re-force the user's explicit choice back on

            options.DidNotReceive().MeasureCurvatureDuringCalibration = Arg.Any<bool>();
        }

        // First-connect case where the user disables it BEFORE ever connecting: the very first connect must
        // also respect that (not just reconnects after an earlier default).
        [Test]
        public void ConnectDeviceCommand_UserExplicitlyDisabledMeasureCurvature_BeforeFirstConnect_DoesNotForceOn() {
            var (vm, options, service, controller, _, _) = BuildMotorized();
            options.MeasureCurvatureDuringCalibration.Returns(false);
            options.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                options, new System.ComponentModel.PropertyChangedEventArgs(nameof(ITiltAdapterOptions.MeasureCurvatureDuringCalibration)));

            Connect(vm);

            options.DidNotReceive().MeasureCurvatureDuringCalibration = Arg.Any<bool>();
        }

        // ---- Warning-only curvature-channel cross-check (design §2.4) --------------------------------------
        //
        // σ is measured from the mean best-focus change (geometric channel). The curvature effect responds to
        // the same piston and σ is DEFINED as the sign of that response, so the two are now independent
        // measurements of one quantity. A large, contradicting curvature change is worth surfacing — but only
        // as information: the optical channel is usually drift-buried and must never veto the robust one, nor
        // change the stored sign.

        // Seeds a 6-step run whose mean-focus channel always measures σ = +1 (all-inward mean 990 < baseline
        // 1000), with the curvature effect at screw radius supplied per step so the cross-check has a signal
        // and a drift scale to compare it against.
        private static (TiltAdapterWizardVM vm, ITiltAdapterOptions options) RunSixStepWithCurvature(
                double baselineE, double allInwardE, double reBaseline1E, double reBaseline2E) {
            var (vm, options, _, _) = Build(configureOptions: o => o.MeasureCurvatureDuringCalibration.Returns(true));
            vm.StartCommand.Execute(null);

            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0, baselineE);
            vm.SeedStepReading(WizardStep.AllInward, 0.0, 0.0, 990.0, allInwardE);
            vm.SeedStepReading(WizardStep.ReBaseline1, 0.0, 0.0, 1000.0, reBaseline1E);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0, reBaseline1E);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0, reBaseline2E);
            vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0, reBaseline2E);

            for (int i = 0; i < 6; i++) {
                vm.NextStep();
            }
            return (vm, options);
        }

        [Test]
        public void CurvatureCrossCheck_ChannelsAgree_NoWarning() {
            // σ = +1 from the mean-focus channel; the curvature effect rose by +100 µm on the same step, which
            // is what σ = +1 means by definition. Nothing to report.
            var (vm, _) = RunSixStepWithCurvature(baselineE: -400, allInwardE: -300, reBaseline1E: -395, reBaseline2E: -390);

            Assert.Multiple(() => {
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Complete));
                Assert.That(vm.HasConfidenceWarning, Is.False);
                Assert.That(vm.ConfidenceWarningText, Is.Empty);
            });
        }

        [Test]
        public void CurvatureCrossCheck_ContradictingButWithinDrift_NoWarning() {
            // The curvature effect moved the "wrong" way, but only by 6 µm against re-baseline drifts of 20
            // and 25 µm — exactly the drift-buried regime the reference session showed. Staying quiet here is
            // the point: a noisy optical channel must not cast doubt on the robust one.
            var (vm, _) = RunSixStepWithCurvature(baselineE: -400, allInwardE: -406, reBaseline1E: -380, reBaseline2E: -355);

            Assert.Multiple(() => {
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Complete));
                Assert.That(vm.HasConfidenceWarning, Is.False);
            });
        }

        [Test]
        public void CurvatureCrossCheck_ContradictingAndLarge_WarnsWithoutChangingTheSign() {
            // A 150 µm move the wrong way against ~5 µm of re-baseline drift. Surface it — and keep the
            // measured sign exactly as the mean-focus channel set it.
            var (vm, options) = RunSixStepWithCurvature(baselineE: -400, allInwardE: -550, reBaseline1E: -404, reBaseline2E: -398);

            Assert.Multiple(() => {
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Complete));
                Assert.That(vm.HasConfidenceWarning, Is.True);
                Assert.That(vm.ConfidenceWarningText, Does.Contain("cross-check"));
                Assert.That(vm.ConfidenceWarningText, Does.Contain("informational"));
                // Warning-only: the stored sign is still the mean-focus channel's verdict.
                options.Received().ScrewInwardCurvatureSign = 1;
                options.Received().ScrewInwardCurvatureSignIsMeasured = true;
                options.Received().IsCalibrated = true;
            });
        }

        [Test]
        public void CurvatureCrossCheck_FourStepRun_NeverWarns() {
            // No all-screws step ⇒ no curvature channel to compare against, and the configured sign is left
            // untouched anyway.
            var (vm, _, _, _) = Build(configureOptions: o => o.MeasureCurvatureDuringCalibration.Returns(false));
            vm.StartCommand.Execute(null);

            vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0, -400);
            vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0, -400);
            vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0, -400);
            vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0, -400);

            for (int i = 0; i < 4; i++) {
                vm.NextStep();
            }

            Assert.Multiple(() => {
                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Complete));
                Assert.That(vm.HasConfidenceWarning, Is.False);
            });
        }

        // ---- The focuser-direction setting k is display-only (design §2.2) ---------------------------------

        [Test]
        public void SixStepRun_MeasuredCurvatureSign_IsIdenticalUnderEitherFocuserDirection() {
            // THE INVARIANCE GUARD FOR THE MEASUREMENT. σ is measured from the mean best-focus change, and the
            // focuser convention provably cancels out of that probe (design §1(c)) — m and k enter both σ and
            // the probe only through their product. So a seeded 6-step run must store the SAME sign with k
            // toggled either way. If this ever fails, k has leaked into the calibration math and the design's
            // central guarantee ("a wrong k gives wrong labels, never wrong motion") is broken.
            int MeasureWith(bool focuserIncreasesTowardObjective) {
                var inspectorOptions = Substitute.For<IInspectorOptions>();
                inspectorOptions.FocuserIncreasesTowardObjective.Returns(focuserIncreasesTowardObjective);
                var (vm, options, _, _) = Build(
                    configureOptions: o => o.MeasureCurvatureDuringCalibration.Returns(true),
                    inspectorOptions: inspectorOptions);
                vm.StartCommand.Execute(null);

                vm.SeedStepReading(WizardStep.Baseline, 0.0, 0.0, 1000.0);
                vm.SeedStepReading(WizardStep.AllInward, 0.0, 0.0, 990.0);
                vm.SeedStepReading(WizardStep.ReBaseline1, 0.0, 0.0, 1000.0);
                vm.SeedStepReading(WizardStep.Screw1, 0.5, 0.0, 1000.0);
                vm.SeedStepReading(WizardStep.ReBaseline2, 0.0, 0.0, 1000.0);
                vm.SeedStepReading(WizardStep.Screw2, -0.25, 0.433, 1000.0);

                int stored = 0;
                options.When(o => o.ScrewInwardCurvatureSign = Arg.Any<int>())
                    .Do(ci => stored = ci.Arg<int>());

                for (int i = 0; i < 6; i++) {
                    vm.NextStep();
                }

                Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Complete), "precondition: the run completed");
                return stored;
            }

            var standard = MeasureWith(false);
            var reversed = MeasureWith(true);

            Assert.Multiple(() => {
                Assert.That(standard, Is.EqualTo(1), "the all-screws step lowered mean focus ⇒ σ = +1");
                Assert.That(reversed, Is.EqualTo(standard), "the measured σ must not depend on the focuser setting");
            });
        }

        [Test]
        public void CwMovesAdapterTowardObjective_ReversedFocuser_RoundTripsThroughTheOppositeSign() {
            // The ONE deliberate exception (design §2.3): the combo asks about the adapter mechanics m, but
            // stores σ = m·sign(k). On a reversed focuser the same mechanical answer therefore stores the
            // opposite σ — which is what makes the manual path correct there, where a pinned k = +1
            // conversion would store an inverted sign for an honest answer. It writes only the ASSUMED σ.
            var inspectorOptions = Substitute.For<IInspectorOptions>();
            inspectorOptions.FocuserIncreasesTowardObjective.Returns(true);
            var (vm, options, _, _) = Build(inspectorOptions: inspectorOptions);

            options.ScrewInwardCurvatureSign.Returns(0);
            vm.CwMovesAdapterTowardObjective = true;
            var storedForTowardObjective = TiltScrewGeometry.CurvatureSignForCwDirection(true) * -1;

            Assert.Multiple(() => {
                options.Received().ScrewInwardCurvatureSign = storedForTowardObjective;
                options.Received().ScrewInwardCurvatureSignIsMeasured = false;

                // And the getter reads it back consistently, so the combo never contradicts itself.
                options.ScrewInwardCurvatureSign.Returns(storedForTowardObjective);
                Assert.That(vm.CwMovesAdapterTowardObjective, Is.True);
            });
        }

        [Test]
        public void FocuserDirectionChange_RefreshesTheMechanicalWordingAndPhysicalAngles() {
            // k is display-only, so a change must re-raise the wording and re-run the physical-angle
            // conversion — and touch nothing stored.
            var inspectorOptions = Substitute.For<IInspectorOptions>();
            var (vm, options, _, _) = Build(configureOptions: o => {
                o.IsCalibrated.Returns(true);
                o.CalibratedScrewCount.Returns(3);
                o.ScrewInwardCurvatureSign.Returns(1);
                o.Screw1AngleDegrees.Returns(180.0);
                o.Screw2AngleDegrees.Returns(300.0);
                o.Screw3AngleDegrees.Returns(60.0);
            }, inspectorOptions: inspectorOptions);

            Assert.That(vm.PhysicalScrew1AngleDegrees, Is.EqualTo(0.0).Within(1e-9),
                "precondition: σ = +1 on a standard focuser is m = +1, so stored 180° is physical 0°");
            options.ClearReceivedCalls();

            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            inspectorOptions.FocuserIncreasesTowardObjective.Returns(true);
            inspectorOptions.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(
                inspectorOptions, new System.ComponentModel.PropertyChangedEventArgs(nameof(IInspectorOptions.FocuserIncreasesTowardObjective)));

            Assert.Multiple(() => {
                Assert.That(raised, Does.Contain(nameof(TiltAdapterWizardVM.CwMovesAdapterTowardObjective)));
                Assert.That(raised, Does.Contain(nameof(TiltAdapterWizardVM.PhysicalScrew1AngleDegrees)));
                Assert.That(vm.PhysicalScrew1AngleDegrees, Is.EqualTo(180.0).Within(1e-9),
                    "σ = +1 on a reversed focuser is m = −1, so the offset drops and stored == physical");
                options.DidNotReceive().ScrewInwardCurvatureSign = Arg.Any<int>();
                options.DidNotReceive().Screw1AngleDegrees = Arg.Any<double>();
                options.DidNotReceive().IsCalibrated = Arg.Any<bool>();
            });
        }

        private static object SentinelFor(Type type, int index) {
            if (type == typeof(bool)) return true;              // fresh snapshot bools default to false
            if (type == typeof(int)) return 1000 + index;       // != 0 and != AdaptiveNoiseBlockSize's 128 default
            if (type == typeof(double)) return 100.0 + index;   // != 0.0
            throw new NotSupportedException($"Add a sentinel for curated knob type {type} in the OverlayOptimizedSettings guard test");
        }
    }
}
