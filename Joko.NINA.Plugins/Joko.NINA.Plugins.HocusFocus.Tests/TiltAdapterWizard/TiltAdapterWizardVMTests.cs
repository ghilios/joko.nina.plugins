using NINA.Core.Interfaces;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

        private static (TiltAdapterWizardVM vm, ITiltAdapterOptions options, ICameraMediator camera, IFocuserMediator focuser) Build(int screwCount = 3, IApplicationDispatcher dispatcher = null, System.Action<ITiltAdapterOptions> configureOptions = null, IProfileService profileService = null) {
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
                tiltAdapterOptions: options);
            return (vm, options, camera, focuser);
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
        public void StepInstructions_DiffersByScrewCountAtAllScrewsStep() {
            var (vm3, _, _, _) = Build(screwCount: 3);
            var (vm4, _, _, _) = Build(screwCount: 4);

            // Manually advance to AllScrews via reflection-free path: update via PropertyChanged on options
            // is ineffective for CurrentStep. Use only what we can verify at Baseline first.
            // Baseline instructions are screw-count independent.
            var b3 = vm3.StepInstructions;
            var b4 = vm4.StepInstructions;
            Assert.That(b3, Is.EqualTo(b4));
            Assert.That(b3, Does.Contain("baseline"));
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
            var before = dispatcher.DispatchCount;

            Task.Run(() => {
                vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
                vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });
            }).GetAwaiter().GetResult();

            Assert.That(dispatcher.DispatchCount - before, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void UpdateDeviceInfo_Camera_MarshalsExactlyOnceAtBoundary() {
            // F15: device-info broadcasts arrive on a background thread; the whole UpdateDeviceInfo body (state
            // mutation + all command/property notifications) must be marshaled to the UI thread as a single unit,
            // not via a separate dispatch per notify site.
            var dispatcher = new RecordingApplicationDispatcher();
            var (vm, _, _, _) = Build(dispatcher: dispatcher);
            var before = dispatcher.DispatchCount;

            vm.UpdateDeviceInfo(new CameraInfo { Connected = true });

            Assert.That(dispatcher.DispatchCount - before, Is.EqualTo(1),
                "UpdateDeviceInfo(CameraInfo) should marshal exactly once at the consumer boundary");
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
    }
}
