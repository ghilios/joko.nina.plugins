using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Interfaces;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    /// <summary>
    /// The substitution rule ("which filter will this auto-focus run actually expose through") used to exist as
    /// two hand-copied implementations — the engine's SetAutofocusFilter and the wizard's ResolveSweepFilterName.
    /// Per-filter sweep geometry adds a third consumer, so the rule was extracted here. The last fixture in this
    /// file is the guard that keeps the extracted rule and the wheel-moving code in agreement.
    /// </summary>
    [TestFixture]
    public class AutoFocusFilterResolverTests {
        private static FilterInfo Lum => new FilterInfo("Lum", 0, 0) { AutoFocusFilter = true };

        private static IProfile MakeProfile(bool useFilterWheelOffsets, params FilterInfo[] filters) {
            var profile = Substitute.For<IProfile>();
            profile.FocuserSettings.UseFilterWheelOffsets.Returns(useFilterWheelOffsets);
            profile.FilterWheelSettings.FilterWheelFilters.Returns(new ObserveAllCollection<FilterInfo>(filters));
            return profile;
        }

        [Test]
        public void UsesDesignatedAutoFocusFilter_OffsetsEnabledAndFilterFlagged_ReturnsTrueWithThatFilter() {
            var af = Lum;
            var profile = MakeProfile(true, af, new FilterInfo("Ha", 0, 1));

            Assert.Multiple(() => {
                Assert.That(AutoFocusFilterResolver.UsesDesignatedAutoFocusFilter(profile, out var designated), Is.True);
                Assert.That(designated, Is.SameAs(af));
            });
        }

        [Test]
        public void UsesDesignatedAutoFocusFilter_OffsetsDisabled_ReturnsFalseEvenWithAFlaggedFilter() {
            var profile = MakeProfile(false, Lum);

            Assert.Multiple(() => {
                Assert.That(AutoFocusFilterResolver.UsesDesignatedAutoFocusFilter(profile, out var designated), Is.False);
                Assert.That(designated, Is.Null);
            });
        }

        [Test]
        public void UsesDesignatedAutoFocusFilter_OffsetsEnabledButNoFilterFlagged_ReturnsFalse() {
            var profile = MakeProfile(true, new FilterInfo("Ha", 0, 0), new FilterInfo("Oiii", 0, 1));

            Assert.That(AutoFocusFilterResolver.UsesDesignatedAutoFocusFilter(profile, out _), Is.False);
        }

        [Test]
        public void UsesDesignatedAutoFocusFilter_NullProfile_ReturnsFalseAndDoesNotThrow() {
            Assert.That(AutoFocusFilterResolver.UsesDesignatedAutoFocusFilter(null, out var designated), Is.False);
            Assert.That(designated, Is.Null);
        }

        [Test]
        public void Resolve_UseExactImagingFilter_ReturnsTheImagingFilter() {
            var imaging = new FilterInfo("Ha", 0, 1);
            var profile = MakeProfile(true, Lum, imaging);

            Assert.That(AutoFocusFilterResolver.Resolve(profile, imaging, useExactImagingFilter: true), Is.SameAs(imaging));
        }

        [Test]
        public void Resolve_UseExactImagingFilterButNoImagingFilter_StillSubstitutes() {
            // "Exact" cannot mean anything when there is no filter to be exact about, so the designated-AF rule
            // still applies — this mirrors the engine overload, which falls through to the substituting path.
            var af = Lum;
            var profile = MakeProfile(true, af);

            Assert.That(AutoFocusFilterResolver.Resolve(profile, null, useExactImagingFilter: true), Is.SameAs(af));
        }

        [Test]
        public void Resolve_OffsetsDisabled_ReturnsTheImagingFilter() {
            var imaging = new FilterInfo("Ha", 0, 1);
            var profile = MakeProfile(false, Lum, imaging);

            Assert.That(AutoFocusFilterResolver.Resolve(profile, imaging, useExactImagingFilter: false), Is.SameAs(imaging));
        }

        [Test]
        public void Resolve_OffsetsEnabled_ReturnsTheDesignatedAfFilter() {
            var af = Lum;
            var imaging = new FilterInfo("Ha", 0, 1);
            var profile = MakeProfile(true, af, imaging);

            Assert.That(AutoFocusFilterResolver.Resolve(profile, imaging, useExactImagingFilter: false), Is.SameAs(af));
        }

        [Test]
        public void Resolve_OffsetsEnabledWithNoDesignatedAfFilter_ReturnsTheImagingFilter() {
            var imaging = new FilterInfo("Ha", 0, 1);
            var profile = MakeProfile(true, imaging);

            Assert.That(AutoFocusFilterResolver.Resolve(profile, imaging, useExactImagingFilter: false), Is.SameAs(imaging));
        }

        [Test]
        public void ResolveName_NoFilterAnywhere_ReturnsNull() {
            var profile = MakeProfile(true);

            Assert.That(AutoFocusFilterResolver.ResolveName(profile, null, useExactImagingFilter: false), Is.Null);
        }

        [Test]
        public void ResolveName_ReturnsTheResolvedFiltersName() {
            var profile = MakeProfile(true, Lum, new FilterInfo("Ha", 0, 1));

            Assert.That(
                AutoFocusFilterResolver.ResolveName(profile, new FilterInfo("Ha", 0, 1), useExactImagingFilter: false),
                Is.EqualTo("Lum"));
        }
    }

    /// <summary>
    /// The anti-divergence guard. Per-filter sweep geometry is keyed on AutoFocusFilterResolver's answer, but the
    /// frames are taken through whichever filter SetAutofocusFilter actually moves the wheel to. If those two ever
    /// disagree, a run silently sweeps one filter's geometry while exposing through another — so this fixture
    /// asserts they agree across the whole input matrix rather than trusting the extraction.
    /// </summary>
    [TestFixture]
    public class AutoFocusFilterResolverEngineAgreementTests {

        [SetUp]
        public void ResetStaticGuardBefore() => AutoFocusEngine.ResetAutoFocusInProgressForTests();

        [TearDown]
        public void ResetStaticGuardAfter() => AutoFocusEngine.ResetAutoFocusInProgressForTests();

        private static AutoFocusEngine Build(IProfileService profileService, IFilterWheelMediator filterWheelMediator) {
            return new AutoFocusEngine(
                profileService: profileService,
                cameraMediator: Substitute.For<ICameraMediator>(),
                filterWheelMediator: filterWheelMediator,
                focuserMediator: Substitute.For<IFocuserMediator>(),
                guiderMediator: Substitute.For<IGuiderMediator>(),
                imagingMediator: Substitute.For<IImagingMediator>(),
                imageDataFactory: Substitute.For<IImageDataFactory>(),
                starDetectionSelector: Substitute.For<IPluggableBehaviorSelector<IStarDetection>>(),
                starAnnotatorSelector: Substitute.For<IPluggableBehaviorSelector<IStarAnnotator>>(),
                autoFocusOptions: Substitute.For<IAutoFocusOptions>(),
                starAnnotatorOptions: Substitute.For<IStarAnnotatorOptions>(),
                alglibAPI: new AlglibAPI());
        }

        [TestCase(true, true, true, true)]
        [TestCase(true, true, true, false)]
        [TestCase(true, true, false, true)]
        [TestCase(true, true, false, false)]
        [TestCase(true, false, true, true)]
        [TestCase(true, false, true, false)]
        [TestCase(true, false, false, true)]
        [TestCase(true, false, false, false)]
        [TestCase(false, true, true, true)]
        [TestCase(false, true, true, false)]
        [TestCase(false, true, false, true)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, true)]
        [TestCase(false, false, true, false)]
        [TestCase(false, false, false, true)]
        [TestCase(false, false, false, false)]
        public async Task Resolve_MatchesSetAutofocusFilter_ForEveryCombination(
            bool useFilterWheelOffsets, bool hasDesignatedAfFilter, bool useExactImagingFilter, bool hasImagingFilter) {
            var profileService = Substitute.For<IProfileService>();
            var af = new FilterInfo("Lum", 0, 0) { AutoFocusFilter = hasDesignatedAfFilter };
            var imaging = hasImagingFilter ? new FilterInfo("Ha", 0, 1) : null;
            profileService.ActiveProfile.FocuserSettings.UseFilterWheelOffsets.Returns(useFilterWheelOffsets);
            profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Returns(
                new ObserveAllCollection<FilterInfo>(new[] { af }));

            // Echo the requested filter back, so the engine's return value reflects the filter it chose to move to
            // rather than any mediator behavior of its own.
            var filterWheelMediator = Substitute.For<IFilterWheelMediator>();
            filterWheelMediator.ChangeFilter(Arg.Any<FilterInfo>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<ApplicationStatus>>())
                .Returns(ci => Task.FromResult(ci.Arg<FilterInfo>()));

            var engine = Build(profileService, filterWheelMediator);
            var options = new AutoFocusEngineOptions { UseExactImagingFilter = useExactImagingFilter };

            var engineChoice = await engine.SetAutofocusFilter(options, imaging, CancellationToken.None, null);
            var resolverChoice = AutoFocusFilterResolver.Resolve(profileService.ActiveProfile, imaging, useExactImagingFilter);

            Assert.That(engineChoice, Is.SameAs(resolverChoice),
                $"offsets={useFilterWheelOffsets}, designated={hasDesignatedAfFilter}, exact={useExactImagingFilter}, imaging={hasImagingFilter}");
        }
    }
}
