using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NSubstitute;
using NUnit.Framework;
using OxyPlot;
using OxyPlot.Series;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class HocusFocusVMTests {

        private static HocusFocusVM Build(IAlglibAPI alglibAPI = null, IAutoFocusOptions autoFocusOptions = null) {
            var profileService = Substitute.For<IProfileService>();
            var focuserMediator = Substitute.For<IFocuserMediator>();
            focuserMediator.GetInfo().Returns(new FocuserInfo());

            return new HocusFocusVM(
                profileService: profileService,
                focuserMediator: focuserMediator,
                autoFocusEngineFactory: Substitute.For<IAutoFocusEngineFactory>(),
                autoFocusOptions: autoFocusOptions ?? Substitute.For<IAutoFocusOptions>(),
                starDetectionOptions: Substitute.For<IStarDetectionOptions>(),
                filterWheelMediator: Substitute.For<IFilterWheelMediator>(),
                applicationStatusMediator: Substitute.For<IApplicationStatusMediator>(),
                starDetectionSelector: Substitute.For<IPluggableBehaviorSelector<IStarDetection>>(),
                alglibAPI: alglibAPI ?? new AlglibAPI(),
                applicationDispatcher: new NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles.SynchronousApplicationDispatcher());
        }

        [Test]
        public void Constructor_InitializesObservableCollectionsAndDefaults() {
            var vm = Build();

            Assert.Multiple(() => {
                Assert.That(vm.FocusPoints, Is.Not.Null);
                Assert.That(vm.FocusPoints, Is.Empty);
                Assert.That(vm.PlotFocusPoints, Is.Not.Null);
                Assert.That(vm.PlotRejectedFocusPoints, Is.Not.Null);
                Assert.That(vm.InitialFocuserPosition, Is.EqualTo(-1));
                Assert.That(vm.FinalFocuserPosition, Is.EqualTo(-1));
                Assert.That(vm.AutoFocusInProgress, Is.False);
                Assert.That(vm.LoadSavedAutoFocusRunCommand, Is.Not.Null);
                Assert.That(vm.CancelLoadSavedAutoFocusRunCommand, Is.Not.Null);
                Assert.That(vm.LastReport, Is.Null);
            });
        }

        [Test]
        public void InitialFocuserPosition_Setter_RaisesPropertyChangedOnlyOnChange() {
            var vm = Build();
            int changes = CountChanges(vm, nameof(vm.InitialFocuserPosition), () => {
                vm.InitialFocuserPosition = 1234;
                vm.InitialFocuserPosition = 1234; // unchanged
                vm.InitialFocuserPosition = 5678;
            });
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test]
        public void FinalFocusPoint_Setter_AlsoUpdatesFinalFocuserPosition() {
            var vm = Build();

            vm.FinalFocusPoint = new DataPoint(4321.4, 1.0);
            Assert.That(vm.FinalFocuserPosition, Is.EqualTo(4321));

            vm.FinalFocusPoint = new DataPoint(4321.6, 1.0);
            Assert.That(vm.FinalFocuserPosition, Is.EqualTo(4322));
        }

        [Test]
        public void InitialHFR_Setter_RaisesPropertyChangedOnlyOnChange() {
            var vm = Build();
            int changes = CountChanges(vm, nameof(vm.InitialHFR), () => {
                vm.InitialHFR = 1.0;
                vm.InitialHFR = 1.0;
                vm.InitialHFR = 2.5;
            });
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test]
        public void SetCurveFittings_FewerThanThreePoints_LeavesFittingsNull() {
            var vm = Build();
            vm.FocusPoints.Add(new ScatterErrorPoint(100, 1.5, 0, 1.0));
            vm.FocusPoints.Add(new ScatterErrorPoint(200, 1.4, 0, 1.0));

            vm.SetCurveFittings(method: AFMethodEnum.STARHFR.ToString(), fitting: AFCurveFittingEnum.HYPERBOLIC.ToString());

            Assert.Multiple(() => {
                Assert.That(vm.HyperbolicFitting, Is.Null);
                Assert.That(vm.QuadraticFitting, Is.Null);
                Assert.That(vm.GaussianFitting, Is.Null);
            });
        }

        [Test]
        public void SetCurveFittings_StarHFR_Hyperbolic_PopulatesHyperbolicFit() {
            var autoFocusOptions = Substitute.For<IAutoFocusOptions>();
            autoFocusOptions.WeightedHyperbolicFitEnabled.Returns(false);
            var vm = Build(alglibAPI: new AlglibAPI(), autoFocusOptions: autoFocusOptions);

            // Synthetic noiseless symmetric hyperbola
            var pts = Synthetic.SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 2.0, b: 80.0, xStart: 4800, xStep: 25, count: 17);
            foreach (var p in pts) vm.FocusPoints.Add(p);

            vm.SetCurveFittings(method: AFMethodEnum.STARHFR.ToString(), fitting: AFCurveFittingEnum.HYPERBOLIC.ToString());

            Assert.Multiple(() => {
                Assert.That(vm.HyperbolicFitting, Is.Not.Null);
                Assert.That(vm.HyperbolicFitting.Minimum.X, Is.EqualTo(5000.0).Within(1.0));
                Assert.That(vm.QuadraticFitting, Is.Null);
            });
        }

        [Test]
        public void SetCurveFittings_StarHFR_Parabolic_PopulatesQuadraticFit() {
            var vm = Build();
            // y = (x-100)^2 / 1000 + 1
            for (int x = 50; x <= 150; x += 10) {
                var y = (x - 100.0) * (x - 100.0) / 1000.0 + 1.0;
                vm.FocusPoints.Add(new ScatterErrorPoint(x, y, 0, 0.1));
            }

            vm.SetCurveFittings(method: AFMethodEnum.STARHFR.ToString(), fitting: AFCurveFittingEnum.PARABOLIC.ToString());

            Assert.Multiple(() => {
                Assert.That(vm.QuadraticFitting, Is.Not.Null);
                Assert.That(vm.HyperbolicFitting, Is.Null);
                Assert.That(vm.QuadraticFitting.Minimum.X, Is.EqualTo(100.0).Within(2.0));
            });
        }

        [Test]
        public void SetCurveFittings_ContrastDetection_PopulatesGaussianAndTrendline() {
            var vm = Build();
            // Concave curve so Gaussian/trendline can fit
            for (int x = 50; x <= 150; x += 10) {
                var y = 10.0 - (x - 100.0) * (x - 100.0) / 1000.0;
                vm.FocusPoints.Add(new ScatterErrorPoint(x, y, 0, 0.1));
            }

            vm.SetCurveFittings(method: AFMethodEnum.CONTRASTDETECTION.ToString(), fitting: AFCurveFittingEnum.PARABOLIC.ToString());

            Assert.Multiple(() => {
                Assert.That(vm.GaussianFitting, Is.Not.Null);
                Assert.That(vm.TrendlineFitting, Is.Not.Null);
            });
        }

        [Test]
        public void CancelLoadSavedAutoFocusRunCommand_DoesNotThrowWhenIdle() {
            var vm = Build();
            Assert.DoesNotThrow(() => vm.CancelLoadSavedAutoFocusRunCommand.Execute(null));
        }

        [Test]
        public async Task StartAutoFocus_PerFilterEnabledAndWheelDisconnected_RefusesWithoutStartingEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(false).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildHocusFocusVM();

            var report = await vm.StartAutoFocus(imagingFilter: null, token: CancellationToken.None, progress: null);

            Assert.That(report, Is.Null);
            bundle.AutoFocusEngineFactory.DidNotReceive().Create();
            Assert.That(vm.AutoFocusInProgress, Is.False);
        }

        [Test]
        public async Task StartAutoFocus_PerFilterEnabledAndWheelConnected_ProceedsToEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(true).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildHocusFocusVM();

            await vm.StartAutoFocus(imagingFilter: null, token: CancellationToken.None, progress: null);

            bundle.AutoFocusEngineFactory.Received(1).Create();
        }

        [Test]
        public async Task StartAutoFocus_PerFilterDisabled_WheelDisconnected_ProceedsToEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(false);
            var vm = bundle.BuildHocusFocusVM();

            await vm.StartAutoFocus(imagingFilter: null, token: CancellationToken.None, progress: null);

            bundle.AutoFocusEngineFactory.Received(1).Create();
        }

        private static int CountChanges(INotifyPropertyChanged source, string propertyName, System.Action act) {
            int n = 0;
            PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == propertyName) n++; };
            source.PropertyChanged += handler;
            try {
                act();
            } finally {
                source.PropertyChanged -= handler;
            }
            return n;
        }
    }
}
