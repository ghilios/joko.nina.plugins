using CommunityToolkit.Mvvm.Input;
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

        [Test]
        public void ApplySignalAmplification_LiveRun_MultipliesTimeoutByFactor() {
            // Amplification multiplies the exposure count (more, finer-spaced points), so the autofocus timeout must
            // grow by the same factor or the longer sweep would time out. Scaling the per-run options override keeps
            // this off the persisted timeout — no mutate-then-restore.
            var options = new AutoFocusEngineOptions { AutoFocusTimeout = System.TimeSpan.FromSeconds(120) };
            InspectorVM.ApplySignalAmplification(options, signalAmplification: 3, isLiveCapture: true);
            Assert.That(options.AutoFocusTimeout, Is.EqualTo(System.TimeSpan.FromSeconds(360)));
        }

        [TestCase(1)]   // factor of 1 disables amplification
        [TestCase(0)]   // clamped to 1 -> no-op
        [TestCase(-3)]  // clamped to 1 -> no-op
        public void ApplySignalAmplification_FactorOfOneOrLess_IsNoOp(int amp) {
            var options = new AutoFocusEngineOptions { AutoFocusInitialOffsetSteps = 4, AutoFocusStepSize = 100, AutoFocusTimeout = System.TimeSpan.FromSeconds(120) };
            InspectorVM.ApplySignalAmplification(options, signalAmplification: amp, isLiveCapture: true);
            Assert.Multiple(() => {
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(4));
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(100));
                Assert.That(options.AutoFocusTimeout, Is.EqualTo(System.TimeSpan.FromSeconds(120)));
            });
        }

        [Test]
        public void ApplySignalAmplification_Replay_IsNoOp() {
            // On replay the saved frames' focuser positions are fixed, so amplification must not change the sweep — and
            // the fixed exposure count means the timeout must not grow either.
            var options = new AutoFocusEngineOptions { AutoFocusInitialOffsetSteps = 4, AutoFocusStepSize = 100, AutoFocusTimeout = System.TimeSpan.FromSeconds(120) };
            InspectorVM.ApplySignalAmplification(options, signalAmplification: 3, isLiveCapture: false);
            Assert.Multiple(() => {
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(4));
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(100));
                Assert.That(options.AutoFocusTimeout, Is.EqualTo(System.TimeSpan.FromSeconds(120)));
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
        public void EstimateImagesPerRun_UsesOverridesThenProfileFallbacks() {
            Assert.Multiple(() => {
                // StepCount/FramesPerPoint unset (-1) => profile values; amp 2 doubles offset steps.
                Assert.That(InspectorVM.EstimateImagesPerRun(-1, -1, 2, 4, 1), Is.EqualTo((17, 17)));
                // Explicit overrides win over profile values.
                Assert.That(InspectorVM.EstimateImagesPerRun(3, 2, 1, 4, 1), Is.EqualTo((7, 14)));
                // Unknown when neither source is positive.
                Assert.That(InspectorVM.EstimateImagesPerRun(-1, 1, 2, 0, 1), Is.EqualTo((0, 0)));
            });
        }

        [Test]
        public void BuildSignalAmplificationSummary_DescribesImageCount() {
            var text = InspectorVM.BuildSignalAmplificationSummary(-1, -1, 2, 4, 1);
            Assert.Multiple(() => {
                Assert.That(text, Does.Contain("~17 images"));
                Assert.That(text, Does.Contain("17 focus positions"));
                Assert.That(text, Does.Contain("1 runs a regular autofocus"));
            });
            Assert.That(InspectorVM.BuildSignalAmplificationSummary(-1, -1, 2, 0, 0), Is.Empty);
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

        [Test]
        public void AnalyzeAutoFocus_PerFilterEnabledAndWheelDisconnected_RefusesWithoutStartingEngine() {
            // Sync test body + GetAwaiter().GetResult() (not `async Task`/`await`), matching the
            // established pattern in InspectorVMAutomaticAdjustmentTests: this fixture is
            // [Apartment(STA)], and NUnit's async-test SynchronizationContext for STA fixtures makes
            // ProgressFactory.Create (called from the InspectorVM ctor) NRE when a genuinely async
            // test method awaits before/around construction.
            var bundle = new MediatorBundle().WithFilterWheelConnected(false).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildInspectorVM();

            var result = vm.AnalyzeAutoFocus(System.Threading.CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result, Is.False);
            bundle.AutoFocusEngineFactory.DidNotReceive().Create();
        }

        [Test]
        public void AnalyzeAutoFocus_PerFilterEnabledAndWheelConnected_ProceedsToEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(true).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildInspectorVM();

            vm.AnalyzeAutoFocus(System.Threading.CancellationToken.None).GetAwaiter().GetResult();

            bundle.AutoFocusEngineFactory.Received(1).Create();
        }

        [Test]
        public void RunExposureAnalysis_PerFilterEnabledAndWheelDisconnected_RefusesWithoutStartingEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(false).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildInspectorVM();

            ((IAsyncRelayCommand)vm.RunExposureAnalysisCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            bundle.AutoFocusEngineFactory.DidNotReceive().Create();
        }

        [Test]
        public void RunExposureAnalysis_PerFilterDisabled_WheelDisconnected_ProceedsToEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(false);
            var vm = bundle.BuildInspectorVM();

            ((IAsyncRelayCommand)vm.RunExposureAnalysisCommand).ExecuteAsync(null).GetAwaiter().GetResult();

            bundle.AutoFocusEngineFactory.Received(1).Create();
        }
    }
}
