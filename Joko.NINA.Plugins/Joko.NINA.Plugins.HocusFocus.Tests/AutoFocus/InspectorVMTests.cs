using NINA.Core.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    [Apartment(System.Threading.ApartmentState.STA)]
    public class InspectorVMTests {

        private static InspectorVM Build() {
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

        [Test]
        public void Construct_DoesNotThrow_AndRegistersConsumersOnce() {
            var profileService = Substitute.For<IProfileService>();
            var cameraMediator = Substitute.For<ICameraMediator>();
            var focuserMediator = Substitute.For<IFocuserMediator>();
            var telescopeMediator = Substitute.For<ITelescopeMediator>();

            var vm = new InspectorVM(
                profileService: profileService,
                applicationStatusMediator: Substitute.For<IApplicationStatusMediator>(),
                imagingMediator: Substitute.For<IImagingMediator>(),
                cameraMediator: cameraMediator,
                focuserMediator: focuserMediator,
                filterWheelMediator: Substitute.For<IFilterWheelMediator>(),
                telescopeMediator: telescopeMediator,
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

            Assert.Multiple(() => {
                Assert.That(vm, Is.Not.Null);
                cameraMediator.Received(1).RegisterConsumer(vm);
                focuserMediator.Received(1).RegisterConsumer(vm);
                telescopeMediator.Received(1).RegisterConsumer(vm);
            });
        }

        [Test]
        public void Constructor_InitializesCommandsAndModels() {
            var vm = Build();

            Assert.Multiple(() => {
                Assert.That(vm.RunAutoFocusAnalysisCommand, Is.Not.Null);
                Assert.That(vm.RerunSavedAutoFocusAnalysisCommand, Is.Not.Null);
                Assert.That(vm.ClearAnalysesCommand, Is.Not.Null);
                Assert.That(vm.CancelAnalyzeCommand, Is.Not.Null);
                Assert.That(vm.SlewToZenithEastCommand, Is.Not.Null);
                Assert.That(vm.SlewToZenithWestCommand, Is.Not.Null);
                Assert.That(vm.TiltModel, Is.Not.Null);
                Assert.That(vm.SensorModel, Is.Not.Null);
                Assert.That(vm.IsAnalysisRunning, Is.False);
            });
        }

        [Test]
        public void UpdateDeviceInfo_Camera_UpdatesCameraInfoProperty() {
            var vm = Build();
            var info = new global::NINA.Equipment.Equipment.MyCamera.CameraInfo { Connected = true };

            vm.UpdateDeviceInfo(info);

            Assert.That(vm.CameraInfo, Is.SameAs(info));
        }

        [Test]
        public void UpdateDeviceInfo_Focuser_UpdatesFocuserInfoProperty() {
            var vm = Build();
            var info = new global::NINA.Equipment.Equipment.MyFocuser.FocuserInfo { Connected = true };

            vm.UpdateDeviceInfo(info);

            Assert.That(vm.FocuserInfo, Is.SameAs(info));
        }

        [Test]
        public void IsTool_IsTrue() {
            var vm = Build();
            Assert.That(vm.IsTool, Is.True);
        }

        [Test]
        public void ApplySignalAmplification_LiveRun_DividesStepSizeAndMultipliesStepCount() {
            var options = new AutoFocusEngineOptions { AutoFocusInitialOffsetSteps = 4, AutoFocusStepSize = 100 };
            InspectorVM.ApplySignalAmplification(options, signalAmplification: 2, isLiveCapture: true);
            Assert.Multiple(() => {
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(8));
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(50));
            });
        }

        [TestCase(1)]   // factor of 1 disables amplification
        [TestCase(0)]   // clamped to 1 -> no-op
        [TestCase(-3)]  // clamped to 1 -> no-op
        public void ApplySignalAmplification_FactorOfOneOrLess_IsNoOp(int amp) {
            var options = new AutoFocusEngineOptions { AutoFocusInitialOffsetSteps = 4, AutoFocusStepSize = 100 };
            InspectorVM.ApplySignalAmplification(options, signalAmplification: amp, isLiveCapture: true);
            Assert.Multiple(() => {
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(4));
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(100));
            });
        }

        [Test]
        public void ApplySignalAmplification_Replay_IsNoOp() {
            // On replay the saved frames' focuser positions are fixed, so amplification must not change the sweep.
            var options = new AutoFocusEngineOptions { AutoFocusInitialOffsetSteps = 4, AutoFocusStepSize = 100 };
            InspectorVM.ApplySignalAmplification(options, signalAmplification: 3, isLiveCapture: false);
            Assert.Multiple(() => {
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(4));
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(100));
            });
        }

        [Test]
        public void ApplySignalAmplification_StepSizeFloorsAtOne() {
            var options = new AutoFocusEngineOptions { AutoFocusInitialOffsetSteps = 5, AutoFocusStepSize = 1 };
            InspectorVM.ApplySignalAmplification(options, signalAmplification: 4, isLiveCapture: true);
            Assert.Multiple(() => {
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(20));
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(1));
            });
        }

        [Test]
        public void HasInspectorRegionLayout_RequiresFullSixRegionGrid() {
            // The region report indexes RegionHFRs[1..5], so fewer than 6 regions (e.g. a reprocessed single-region
            // regular-AF run) must be rejected — the guard that turns the old IndexOutOfRange crash into a clean log.
            Assert.Multiple(() => {
                Assert.That(InspectorVM.HasInspectorRegionLayout(0), Is.False);
                Assert.That(InspectorVM.HasInspectorRegionLayout(1), Is.False);
                Assert.That(InspectorVM.HasInspectorRegionLayout(5), Is.False);
                Assert.That(InspectorVM.HasInspectorRegionLayout(6), Is.True);
                Assert.That(InspectorVM.HasInspectorRegionLayout(7), Is.True);
            });
        }
    }
}
