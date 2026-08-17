using NINA.Core.Interfaces;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    /// <summary>
    /// The precedence matrix for per-filter auto-focus sweep geometry. Resolution happens inside
    /// <c>AutoFocusEngine.GetOptions</c> rather than later in the run, because callers apply RELATIVE transforms to
    /// these two numbers afterwards (signal amplification divides the step size and multiplies the offset; focus
    /// recovery widens the offset) — resolving after those ran would silently discard them.
    /// </summary>
    [TestFixture]
    public class PerFilterSweepGeometryResolutionTests {
        private const int ProfileStepSize = 100;
        private const int ProfileOffsetSteps = 4;

        private static IProfileService MakeProfileService(bool useFilterWheelOffsets, params FilterInfo[] filters) {
            var profileService = Substitute.For<IProfileService>();
            profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize.Returns(ProfileStepSize);
            profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps.Returns(ProfileOffsetSteps);
            profileService.ActiveProfile.FocuserSettings.UseFilterWheelOffsets.Returns(useFilterWheelOffsets);
            profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Returns(
                new ObserveAllCollection<FilterInfo>(filters));
            return profileService;
        }

        private static IFilterWheelMediator MakeWheel(FilterInfo selected) {
            var mediator = Substitute.For<IFilterWheelMediator>();
            mediator.GetInfo().Returns(new FilterWheelInfo { Connected = selected != null, SelectedFilter = selected });
            return mediator;
        }

        private static IPerFilterStarDetectionStore MakeStore(bool enabled, string filterName = null, PerFilterSweepGeometry geometry = null) {
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            store.Enabled.Returns(enabled);
            store.GetSweepGeometry(Arg.Any<string>()).Returns(PerFilterSweepGeometry.Unset());
            if (filterName != null) {
                store.GetSweepGeometry(filterName).Returns(geometry ?? PerFilterSweepGeometry.Unset());
            }
            return store;
        }

        private static AutoFocusEngine Build(IProfileService profileService, IFilterWheelMediator wheel, IPerFilterStarDetectionStore store) {
            return new AutoFocusEngine(
                profileService: profileService,
                cameraMediator: Substitute.For<ICameraMediator>(),
                filterWheelMediator: wheel,
                focuserMediator: Substitute.For<IFocuserMediator>(),
                guiderMediator: Substitute.For<IGuiderMediator>(),
                imagingMediator: Substitute.For<IImagingMediator>(),
                imageDataFactory: Substitute.For<IImageDataFactory>(),
                starDetectionSelector: Substitute.For<IPluggableBehaviorSelector<IStarDetection>>(),
                starAnnotatorSelector: Substitute.For<IPluggableBehaviorSelector<IStarAnnotator>>(),
                autoFocusOptions: Substitute.For<IAutoFocusOptions>(),
                starAnnotatorOptions: Substitute.For<IStarAnnotatorOptions>(),
                alglibAPI: new AlglibAPI(),
                perFilterStore: store);
        }

        [Test]
        public void FeatureOff_UsesProfileGeometry() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha),
                MakeStore(enabled: false, "Ha", new PerFilterSweepGeometry { StepSize = 25, InitialOffsetSteps = 8 }));

            var options = engine.GetOptions(imagingFilter: ha);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(ProfileStepSize));
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(ProfileOffsetSteps));
                Assert.That(options.SweepGeometryFilterName, Is.Null);
            });
        }

        [Test]
        public void FeatureOn_OverrideUnset_UsesProfileGeometry() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha), MakeStore(enabled: true, "Ha"));

            var options = engine.GetOptions(imagingFilter: ha);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(ProfileStepSize));
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(ProfileOffsetSteps));
                Assert.That(options.SweepGeometryFilterName, Is.EqualTo("Ha"));
            });
        }

        // The two fields resolve independently; only one being set must not drag the other off the profile.
        [Test]
        public void FeatureOn_StepSizeOverrideOnly_UsesOverrideStepAndProfileOffset() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha),
                MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { StepSize = 25 }));

            var options = engine.GetOptions(imagingFilter: ha);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(25));
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(ProfileOffsetSteps));
            });
        }

        [Test]
        public void FeatureOn_BothOverridden_UsesBoth() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha),
                MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { StepSize = 25, InitialOffsetSteps = 8 }));

            var options = engine.GetOptions(imagingFilter: ha);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(25));
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(8));
            });
        }

        // Replay re-derives the step size from the saved frames and takes the offset from the capture-time
        // snapshot. Keying either off whichever filter is in the wheel tonight is the bug that carve-out prevents.
        [Test]
        public void FeatureOn_SavedAttempt_IgnoresPerFilterEntirely() {
            var ha = new FilterInfo("Ha", 0, 0);
            var store = MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { StepSize = 25, InitialOffsetSteps = 8 });
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha), store);

            var options = engine.GetOptions(new SavedAutoFocusAttempt { StepSize = 33 }, imagingFilter: ha);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(33));
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(ProfileOffsetSteps));
                Assert.That(options.SweepGeometryFilterName, Is.Null);
                store.DidNotReceive().GetSweepGeometry(Arg.Any<string>());
            });
        }

        [Test]
        public void FeatureOn_SavedAttemptZeroStepSize_FallsBackToProfileNotToTheFilter() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha),
                MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { StepSize = 25 }));

            var options = engine.GetOptions(new SavedAutoFocusAttempt { StepSize = 0 }, imagingFilter: ha);

            Assert.That(options.AutoFocusStepSize, Is.EqualTo(ProfileStepSize));
        }

        // With filter-wheel offsets on, the frames are exposed through the designated AF filter, which is also the
        // name per-filter DETECTION keys on for the same run -- so geometry must agree with it.
        [Test]
        public void FeatureOn_UseFilterWheelOffsets_KeysOnTheDesignatedAfFilter() {
            var lum = new FilterInfo("Lum", 0, 0) { AutoFocusFilter = true };
            var ha = new FilterInfo("Ha", 0, 1);
            var store = MakeStore(enabled: true, "Lum", new PerFilterSweepGeometry { StepSize = 60, InitialOffsetSteps = 9 });
            store.GetSweepGeometry("Ha").Returns(new PerFilterSweepGeometry { StepSize = 25, InitialOffsetSteps = 3 });
            var engine = Build(MakeProfileService(true, lum, ha), MakeWheel(ha), store);

            var options = engine.GetOptions(imagingFilter: ha);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(60));
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(9));
                Assert.That(options.SweepGeometryFilterName, Is.EqualTo("Lum"));
            });
        }

        [Test]
        public void FeatureOn_UseFilterWheelOffsetsWithNoDesignatedAfFilter_KeysOnTheImagingFilter() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(true, ha), MakeWheel(ha),
                MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { StepSize = 25 }));

            var options = engine.GetOptions(imagingFilter: ha);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(25));
                Assert.That(options.SweepGeometryFilterName, Is.EqualTo("Ha"));
            });
        }

        // The optimizer sweeps one chosen filter on purpose, suppressing the AF-filter substitution.
        [Test]
        public void FeatureOn_UseExactImagingFilter_KeysOnTheImagingFilterNotTheAfFilter() {
            var lum = new FilterInfo("Lum", 0, 0) { AutoFocusFilter = true };
            var ha = new FilterInfo("Ha", 0, 1);
            var store = MakeStore(enabled: true, "Lum", new PerFilterSweepGeometry { StepSize = 60 });
            store.GetSweepGeometry("Ha").Returns(new PerFilterSweepGeometry { StepSize = 25 });
            var engine = Build(MakeProfileService(true, lum, ha), MakeWheel(ha), store);

            var options = engine.GetOptions(imagingFilter: ha, useExactImagingFilter: true);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(25));
                Assert.That(options.SweepGeometryFilterName, Is.EqualTo("Ha"));
            });
        }

        // A caller that passes no filter still gets per-filter behavior, keyed on whatever is loaded -- the same
        // thing per-filter detection does with the capture-time filter in image metadata.
        [Test]
        public void FeatureOn_NoImagingFilter_KeysOnTheWheelsSelectedFilter() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha),
                MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { StepSize = 25 }));

            var options = engine.GetOptions();

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(25));
                Assert.That(options.SweepGeometryFilterName, Is.EqualTo("Ha"));
            });
        }

        // Detection THROWS in this state because it has no fallback. Geometry has one, and failing an auto-focus
        // run over a sweep spacing would be a regression -- so this falls back silently instead.
        [Test]
        public void FeatureOn_WheelDisconnected_FallsBackToProfileAndDoesNotThrow() {
            var engine = Build(MakeProfileService(false), MakeWheel(null), MakeStore(enabled: true));

            AutoFocusEngineOptions options = null;
            Assert.DoesNotThrow(() => options = engine.GetOptions());
            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(ProfileStepSize));
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(ProfileOffsetSteps));
                Assert.That(options.SweepGeometryFilterName, Is.Null);
            });
        }

        [Test]
        public void FeatureOn_WhitespaceFilterName_FallsBackToProfile() {
            var blank = new FilterInfo("   ", 0, 0);
            var engine = Build(MakeProfileService(false, blank), MakeWheel(blank), MakeStore(enabled: true));

            var options = engine.GetOptions(imagingFilter: blank);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(ProfileStepSize));
                Assert.That(options.SweepGeometryFilterName, Is.Null);
            });
        }

        // The engine calls this on its run path, so a seed-and-fan-out would push a SnapshotChanged through the
        // edit binder mid-run. GetSweepGeometry is contractually read-only for exactly this reason.
        [Test]
        public void Resolution_NeverWritesToTheStore() {
            var ha = new FilterInfo("Ha", 0, 0);
            var store = MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { StepSize = 25 });
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha), store);

            engine.GetOptions(imagingFilter: ha);

            store.DidNotReceiveWithAnyArgs().SetSweepGeometry(default, default);
        }

        // --- Composition order --------------------------------------------------------------------------------
        //
        // These are the guards against a future change moving resolution later (into InitializeState or RunImpl).
        // Two of the three caller-side transforms are RELATIVE, so resolving after them would not throw or produce
        // an obviously wrong number -- it would just quietly sweep at the un-transformed geometry.

        [Test]
        public void ApplySignalAmplification_DividesThePerFilterStepNotTheProfileStep() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha),
                MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { StepSize = 240, InitialOffsetSteps = 3 }));

            var options = engine.GetOptions(imagingFilter: ha);
            InspectorVM.ApplySignalAmplification(options, signalAmplification: 3, isLiveCapture: true);

            Assert.Multiple(() => {
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(80), "240 / 3, i.e. the filter's step size was the base");
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(9), "3 * 3, i.e. the filter's offset was the base");
            });
        }

        [Test]
        public void ApplyFocusRecovery_WidensThePerFilterOffsetNotTheProfileOffset() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha),
                MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { InitialOffsetSteps = 7 }));

            var options = engine.GetOptions(imagingFilter: ha);
            StarDetectionOptimizerWizardVM.ApplyFocusRecovery(options, recoverySteps: 2);

            Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(9), "7 + 2, i.e. the filter's offset was the base");
        }

        [Test]
        public void ApplyRecaptureGeometry_WinsOverThePerFilterStepSize() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha),
                MakeStore(enabled: true, "Ha", new PerFilterSweepGeometry { StepSize = 240 }));

            var options = engine.GetOptions(imagingFilter: ha);
            StarDetectionOptimizerWizardVM.ApplyRecaptureGeometry(options, stepSize: 55);

            Assert.That(options.AutoFocusStepSize, Is.EqualTo(55), "a re-capture's own recommendation is absolute");
        }

        [Test]
        public void NullStore_BehavesExactlyLikeTheFeatureBeingOff() {
            var ha = new FilterInfo("Ha", 0, 0);
            var engine = Build(MakeProfileService(false, ha), MakeWheel(ha), store: null);

            var options = engine.GetOptions(imagingFilter: ha);

            Assert.That(options.AutoFocusStepSize, Is.EqualTo(ProfileStepSize));
        }
    }
}
