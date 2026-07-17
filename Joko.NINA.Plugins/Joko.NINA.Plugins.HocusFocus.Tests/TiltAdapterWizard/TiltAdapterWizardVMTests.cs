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

        private static InspectorVM BuildInspector() {
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
                inspectorOptions: Substitute.For<IInspectorOptions>(),
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
            ISerialPortProvider serialPortProvider = null, Func<Task<bool>> confirmIdleDisconnectAsync = null) {
            profileService ??= Substitute.For<IProfileService>();
            var camera = Substitute.For<ICameraMediator>();
            var focuser = Substitute.For<IFocuserMediator>();
            var options = Substitute.For<ITiltAdapterOptions>();
            options.ScrewCount.Returns(screwCount);
            options.MeasurementAverageCount.Returns(1);
            configureOptions?.Invoke(options);
            var inspector = BuildInspector();
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
                Assert.That(TiltAdapterWizardVM.GetMeasurementSteps(measureCurvature: true), Is.EqualTo(new[] {
                    WizardStep.Baseline, WizardStep.AllInward, WizardStep.ReBaseline1,
                    WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 }));
                Assert.That(TiltAdapterWizardVM.GetMeasurementSteps(measureCurvature: false), Is.EqualTo(new[] {
                    WizardStep.Baseline, WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 }));
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
        public void ApplyManualCalibration_WritesCalibrationState_PositiveSignStoresPhysicalAngles() {
            // On +1 rigs the stored response-convention angles coincide with the physical angles the
            // user typed, so 30° places the clockwise-numbered screws at 30/150/270.
            var (vm, options, _, _) = Build(screwCount: 3);
            options.ScrewInwardCurvatureSign.Returns(1);
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
        public void ApplyManualCalibration_NegativeSign_ConvertsPhysicalToResponseConvention() {
            // On −1 rigs a CW turn drives the tilt gradient opposite the physical screw direction, so
            // the stored (response-convention) angles are the typed physical angle + 180°: wizard runs
            // persist response-convention angles and the guidance math consumes them, so a manual
            // entry must convert or its arrows would invert on these rigs.
            var (vm, options, _, _) = Build(screwCount: 3);
            options.ScrewInwardCurvatureSign.Returns(-1);
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
            var (vmNeg, _, _, _) = Build(screwCount: 3, configureOptions: o => {
                o.Screw1AngleDegrees.Returns(210.0);
                o.ScrewInwardCurvatureSign.Returns(-1);
            });
            Assert.That(vmNeg.ManualScrew1AngleDegrees, Is.EqualTo(30).Within(1e-9));

            var (vmPos, _, _, _) = Build(screwCount: 3, configureOptions: o => {
                o.Screw1AngleDegrees.Returns(210.0);
                o.ScrewInwardCurvatureSign.Returns(1);
            });
            Assert.That(vmPos.ManualScrew1AngleDegrees, Is.EqualTo(210).Within(1e-9));
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
            // anchor) with a calibrated screw 1 at stored angle 210° (physical 30° on a −1 rig).
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
                Assert.That(vm.ManualScrew1AngleDegrees, Is.EqualTo(30).Within(1e-9), "precondition: ctor pre-fill");
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
                // The manual pre-fill re-runs against the new profile: sign +1 stores physical angles unchanged.
                Assert.That(vm.ManualScrew1AngleDegrees, Is.EqualTo(90).Within(1e-9));
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

            // AllInward mean focus (990) below baseline (1000) => ComputeCurvatureSign(990, 1000) = −1.
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
                options.Received().ScrewInwardCurvatureSign = -1;
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
            inspector.MicronsPerFocuserStep.Returns(3.6);
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
            // First read is the run baseline (captured before the first move); second is the post-move position.
            controller.QueryPositionsAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult(new TiltDevicePositions(new[] { 0, 0, 0, 0 }, known: true)),
                Task.FromResult(new TiltDevicePositions(new[] { 150, 150, 150, 150 }, known: true)));

            vm.SelectedPortName = "COM3";
            ((AsyncRelayCommand)vm.ConnectDeviceCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            vm.ExecuteDeviceMoveForCurrentStepAsync(WizardStep.AllInward, CancellationToken.None).GetAwaiter().GetResult();

            // TR = motor 1 = index 0: shows the live position plus the delta from the run's baseline.
            Assert.That(vm.ScrewPositionTopRightDisplay, Does.Contain("150").And.Contain("Δ").And.Contain("+150"));
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

            vm.RunCalibrationForTest(deviceDriven: true);

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

            vm.RunCalibrationForTest(deviceDriven: true);

            Assert.Multiple(() => {
                options.Received(1).CalibrationIsReliable = false;
                options.Received(1).DeviceLinkedCalibrationDeviceName = "ASG Electronic EAT - 90mm";
            });
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

        private static object SentinelFor(Type type, int index) {
            if (type == typeof(bool)) return true;              // fresh snapshot bools default to false
            if (type == typeof(int)) return 1000 + index;       // != 0 and != AdaptiveNoiseBlockSize's 128 default
            if (type == typeof(double)) return 100.0 + index;   // != 0.0
            throw new NotSupportedException($"Add a sentinel for curated knob type {type} in the OverlayOptimizedSettings guard test");
        }
    }
}
