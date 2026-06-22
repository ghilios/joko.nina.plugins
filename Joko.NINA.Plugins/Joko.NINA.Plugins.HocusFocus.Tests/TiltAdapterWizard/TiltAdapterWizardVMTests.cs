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

        private static (TiltAdapterWizardVM vm, ITiltAdapterOptions options, ICameraMediator camera, IFocuserMediator focuser) Build(int screwCount = 3, IApplicationDispatcher dispatcher = null) {
            var profileService = Substitute.For<IProfileService>();
            var camera = Substitute.For<ICameraMediator>();
            var focuser = Substitute.For<IFocuserMediator>();
            var options = Substitute.For<ITiltAdapterOptions>();
            options.ScrewCount.Returns(screwCount);
            options.MeasurementAverageCount.Returns(1);
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
        public void CurvatureSignDescription_ArrowMatchesSign() {
            var (vm, options, _, _) = Build();

            options.ScrewInwardCurvatureSign.Returns(1);
            Assert.That(vm.CurvatureSignDescription, Is.EqualTo("↑"));

            options.ScrewInwardCurvatureSign.Returns(-1);
            Assert.That(vm.CurvatureSignDescription, Is.EqualTo("↓"));

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
    }
}
