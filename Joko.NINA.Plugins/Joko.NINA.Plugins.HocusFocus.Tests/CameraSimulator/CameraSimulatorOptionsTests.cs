using System;
using System.Collections.Generic;
using System.ComponentModel;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class CameraSimulatorOptionsTests {

    /// <summary>
    /// A REAL InspectorOptions over an in-memory store, never a substitute. FocuserStepSizeMicrons reads and writes
    /// straight through to it, and its PropertyChanged is what drives the camera-sim notifications — so a mock here
    /// would be testing the mock's auto-property, not the pass-through that is the entire point.
    /// </summary>
    private static InspectorOptions NewInspector(IProfileService profile, IPluginOptionsAccessor store = null) =>
        new InspectorOptions(profile, store ?? new InMemoryPluginOptionsAccessor());

    private static (CameraSimulatorOptions options, InMemoryPluginOptionsAccessor store, IProfileService profile) Build() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new CameraSimulatorOptions(profile, store, NewInspector(profile));
        return (options, store, profile);
    }

    /// <summary>For the tests that need to reach the far side of the pass-through.</summary>
    private static (CameraSimulatorOptions options, InspectorOptions inspector) BuildWithInspector() {
        var profile = Substitute.For<IProfileService>();
        var inspector = NewInspector(profile);
        return (new CameraSimulatorOptions(profile, new InMemoryPluginOptionsAccessor(), inspector), inspector);
    }

    private static CameraSimulatorOptions OptionsFor(double profileFocalLength, double profileFocalRatio) {
        var (options, _, profile) = Build();
        profile.ActiveProfile.TelescopeSettings.FocalLength.Returns(profileFocalLength);
        profile.ActiveProfile.TelescopeSettings.FocalRatio.Returns(profileFocalRatio);
        return options;
    }

    [Test]
    public void UnsetOptics_InferFromTheProfile() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        Assert.Multiple(() => {
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(-1.0), "unset sentinel");
            Assert.That(options.ApertureMillimeters, Is.EqualTo(-1.0), "unset sentinel");
            Assert.That(options.EffectiveFocalLengthMillimeters, Is.EqualTo(430.0));
            Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(86.0).Within(1e-9), "430 / f5");
        });
    }

    // A fresh NINA profile stores NaN — not 0, not -1. NaN passes `<= 0` guards, so it must be handled by
    // a positive test, not by the absence of one.
    [Test]
    public void ProfileWithNaNOptics_FallsBackToTheDefaultRig() {
        var options = OptionsFor(profileFocalLength: double.NaN, profileFocalRatio: double.NaN);
        Assert.Multiple(() => {
            Assert.That(options.EffectiveFocalLengthMillimeters, Is.EqualTo(980.0));
            Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(140.0).Within(1e-9), "980 / f7");
        });
    }

    [Test]
    public void ProfileWithFocalLengthButNoRatio_KeepsTheDefaultRatio() {
        var options = OptionsFor(profileFocalLength: 1400.0, profileFocalRatio: double.NaN);
        Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(200.0).Within(1e-9), "1400 / f7");
    }

    [Test]
    public void ExplicitOptics_WinOverTheProfile() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        options.FocalLengthMillimeters = 1000.0;
        options.ApertureMillimeters = 250.0;
        Assert.Multiple(() => {
            Assert.That(options.EffectiveFocalLengthMillimeters, Is.EqualTo(1000.0));
            Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(250.0));
        });
    }

    [Test]
    public void ExplicitFocalLength_WithUnsetAperture_KeepsTheProfileRatio() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        options.FocalLengthMillimeters = 1000.0;
        Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(200.0).Within(1e-9), "1000 / f5");
    }

    [Test]
    public void NonPositiveAssignment_HealsToTheUnsetSentinel() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        options.FocalLengthMillimeters = 0.0;   // the sentinel older builds stored
        options.ApertureMillimeters = -5.0;
        Assert.Multiple(() => {
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(-1.0));
            Assert.That(options.ApertureMillimeters, Is.EqualTo(-1.0));
            Assert.That(options.EffectiveFocalLengthMillimeters, Is.EqualTo(430.0), "falls back to the profile");
        });
    }

    /// <summary>
    /// The setup dialog's FloatRangeRules had to go (they run on the raw text before the converter, so they
    /// rejected the empty "unset" box), which left the optics unbounded. That is not cosmetic: the outer radius
    /// of a defocused star goes as |Δ| / (2N), so a 999999 mm aperture drives the f-number to ~0.001 and pushes
    /// PsfKernelGenerator past its MaxKernelRadius=512px ceiling within about two focuser steps of best focus.
    /// It throws a bare ArgumentOutOfRangeException, which NINA renders as "Unexpected error" plus a spurious
    /// AbortExposure — a developer-facing message for a user typo. The bounds now live in the setter.
    /// </summary>
    [Test]
    public void OverCapOptics_ClampToTheMaximumOnCommit() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        options.ApertureMillimeters = 999999.0;
        options.FocalLengthMillimeters = 999999.0;
        Assert.Multiple(() => {
            Assert.That(options.ApertureMillimeters, Is.EqualTo(2000.0));
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(20000.0));
        });
    }

    [Test]
    public void UnderMinimumButPositiveOptics_ClampUpToOne() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        options.ApertureMillimeters = 0.5;
        options.FocalLengthMillimeters = 0.25;
        Assert.Multiple(() => {
            Assert.That(options.ApertureMillimeters, Is.EqualTo(1.0));
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(1.0));
        });
    }

    /// <summary>
    /// The clamp must never swallow the sentinel. 0 and negatives mean "unset — infer it" (an empty box), so they
    /// must stay -1 rather than clamping up to the 1 mm minimum: a blank aperture silently becoming a 1 mm
    /// aperture would break inference, which is the entire point of leaving the box empty.
    /// </summary>
    [Test]
    public void ZeroAndNegativeOptics_StayUnsetRatherThanClampingToTheMinimum() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        options.ApertureMillimeters = 0.0;
        options.FocalLengthMillimeters = -5.0;
        Assert.Multiple(() => {
            Assert.That(options.ApertureMillimeters, Is.EqualTo(-1.0), "unset, NOT clamped to 1.0");
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(-1.0), "unset, NOT clamped to 1.0");
            Assert.That(options.EffectiveFocalLengthMillimeters, Is.EqualTo(430.0), "still infers from the profile");
            Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(86.0).Within(1e-9), "still infers 430 / f5");
        });
    }

    /// <summary>The clamp raises PropertyChanged, so the bound box visibly snaps to the cap — the feedback the
    /// validation border used to give.</summary>
    [Test]
    public void ClampedAperture_RaisesPropertyChangedSoTheBoxSnapsToTheCap() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        options.ApertureMillimeters = 999999.0;
        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(CameraSimulatorOptions.ApertureMillimeters)));
            Assert.That(raised, Does.Contain(nameof(CameraSimulatorOptions.EffectiveApertureMillimeters)));
        });
    }

    [TestCase(999999.0, 2000.0)]
    [TestCase(0.5, 1.0)]
    [TestCase(0.0, -1.0)]
    [TestCase(-5.0, -1.0)]
    [TestCase(250.0, 250.0)]
    public void Aperture_HealsOutOfRangeStoredValueOnLoad(double stored, double expected) {
        var store = new InMemoryPluginOptionsAccessor();
        // Seed through the accessor, bypassing the clamping property setter, as a profile written before the
        // bounds moved into the setter would have.
        store.SetValueDouble(nameof(CameraSimulatorOptions.ApertureMillimeters), stored);

        var profileService = Substitute.For<IProfileService>();
        var options = new CameraSimulatorOptions(profileService, store, NewInspector(profileService));

        Assert.That(options.ApertureMillimeters, Is.EqualTo(expected));
    }

    [Test]
    public void ChangingFocalLength_RaisesTheInferredAperture() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        options.FocalLengthMillimeters = 1000.0;
        Assert.That(raised, Does.Contain(nameof(CameraSimulatorOptions.EffectiveApertureMillimeters)),
            "an inferred aperture depends on the focal length, so its hint must refresh too");
    }

    /// <summary>
    /// The user can edit focal length / focal ratio inside the ACTIVE profile (Options → Equipment → Telescope)
    /// without swapping profiles, which raises no ProfileChanged. The render re-reads per exposure and would pick
    /// the edit up, so without this the long-lived rig panel would keep showing the pre-edit number while the
    /// render used the new one — the exact hint-vs-render drift this resolution point exists to prevent.
    /// </summary>
    [TestCase(nameof(ITelescopeSettings.FocalLength))]
    [TestCase(nameof(ITelescopeSettings.FocalRatio))]
    public void InPlaceProfileOpticsEdit_RaisesTheInferredOptics(string editedProperty) {
        var (options, _, profile) = Build();
        var telescopeSettings = profile.ActiveProfile.TelescopeSettings;
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        telescopeSettings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
            telescopeSettings, new PropertyChangedEventArgs(editedProperty));

        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(CameraSimulatorOptions.EffectiveFocalLengthMillimeters)));
            // The inferred aperture is focal length ÷ focal ratio, so either edit moves it.
            Assert.That(raised, Does.Contain(nameof(CameraSimulatorOptions.EffectiveApertureMillimeters)));
        });
    }

    /// <summary>A profile swap replaces the TelescopeSettings instance; the subscription must follow it.</summary>
    [Test]
    public void AfterProfileSwap_InPlaceEditOnTheNewProfileStillRaises() {
        var profile = Substitute.For<IProfileService>();
        var options = new CameraSimulatorOptions(profile, new InMemoryPluginOptionsAccessor(), NewInspector(profile));

        // Swap in a whole new ActiveProfile, so ActiveProfile.TelescopeSettings is a different instance.
        var newProfile = Substitute.For<IProfile>();
        profile.ActiveProfile.Returns(newProfile);
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        newProfile.TelescopeSettings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
            newProfile.TelescopeSettings, new PropertyChangedEventArgs(nameof(ITelescopeSettings.FocalLength)));

        Assert.That(raised, Does.Contain(nameof(CameraSimulatorOptions.EffectiveFocalLengthMillimeters)),
            "the hook must move to the new profile's TelescopeSettings");
    }

    // ---- Focuser step size: ONE variable, shared with the Aberration Inspector. ----

    /// <summary>
    /// The bug this replaced: the camera rendered defocus at its own 2.0 µm/step default while the Aberration
    /// Inspector interpreted the result at the user's calibrated 1.0 µm/step — a silent 2x that broke the
    /// inject⇄recover loop the simulator exists to close. They are the same physical quantity (µm of sensor
    /// defocus per focuser step) and must therefore be the same variable, not two kept in sync.
    /// </summary>
    [Test]
    public void FocuserStepSize_WrittenOnTheCameraSim_IsVisibleOnTheInspector() {
        var (options, inspector) = BuildWithInspector();
        options.FocuserStepSizeMicrons = 3.5;
        Assert.That(inspector.MicronsPerFocuserStep, Is.EqualTo(3.5));
    }

    [Test]
    public void FocuserStepSize_WrittenOnTheInspector_IsVisibleOnTheCameraSim() {
        var (options, inspector) = BuildWithInspector();
        inspector.MicronsPerFocuserStep = 3.5;
        Assert.That(options.FocuserStepSizeMicrons, Is.EqualTo(3.5));
    }

    [Test]
    public void EffectiveFocuserStepSize_UnsetFallsBackToTheDefault() {
        var (options, inspector) = BuildWithInspector();
        Assert.That(inspector.MicronsPerFocuserStep, Is.EqualTo(-1.0), "precondition: uncalibrated");
        Assert.That(options.EffectiveFocuserStepSizeMicrons,
            Is.EqualTo(CameraSimulatorOptions.DefaultFocuserStepSizeMicrons).And.EqualTo(2.0));
    }

    [Test]
    public void EffectiveFocuserStepSize_SetValueWins() {
        var (options, _) = BuildWithInspector();
        options.FocuserStepSizeMicrons = 3.5;
        Assert.That(options.EffectiveFocuserStepSizeMicrons, Is.EqualTo(3.5));
    }

    /// <summary>
    /// Non-positive means "uncalibrated" (the blank box), exactly as it does on the Inspector's own row — it must
    /// NOT clamp up to some positive minimum, or a blank box would silently become a real calibration. NaN is
    /// included because it fails both `&gt;` and `&lt;=`: a `&lt;= 0` guard would wave it through into DefocusModel
    /// and render an all-NaN frame with no error anywhere, which is the bug class this branch has already paid for.
    /// </summary>
    [TestCase(0.0)]
    [TestCase(-1.0)]
    [TestCase(-7.5)]
    [TestCase(double.NaN)]
    public void NonPositiveFocuserStepSize_ResolvesToTheDefaultRatherThanClamping(double assigned) {
        var (options, _) = BuildWithInspector();
        options.FocuserStepSizeMicrons = assigned;
        Assert.Multiple(() => {
            Assert.That(options.EffectiveFocuserStepSizeMicrons, Is.EqualTo(2.0), "unset -> the default, not a clamp");
            Assert.That(options.FocuserStepSizeMicrons, Is.EqualTo(assigned).Or.NaN,
                "the raw value passes through untouched — the camera sim does not heal the Inspector's store");
        });
    }

    /// <summary>
    /// The camera sim's options page binds both of these, and the value can be edited from the Inspector's page.
    /// Without the subscription the page would show a stale number while the render already used the new one.
    /// </summary>
    [Test]
    public void InspectorFocuserStepSizeEdit_RaisesBothCameraSimProperties() {
        var (options, inspector) = BuildWithInspector();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        inspector.MicronsPerFocuserStep = 4.25;

        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(CameraSimulatorOptions.FocuserStepSizeMicrons)));
            Assert.That(raised, Does.Contain(nameof(CameraSimulatorOptions.EffectiveFocuserStepSizeMicrons)),
                "the hint text resolves through the same value and must refresh with it");
        });
    }

    // ---- Migration off the simulator's old, separate FocuserStepSizeMicrons key. ----

    [Test]
    public void LegacyFocuserStepSizeKey_MigratesOntoAnUncalibratedInspector() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        // What an in-development build of this branch left behind.
        store.SetValueDouble(nameof(CameraSimulatorOptions.FocuserStepSizeMicrons), 5.0);
        var inspector = NewInspector(profile);

        var options = new CameraSimulatorOptions(profile, store, inspector);

        Assert.Multiple(() => {
            Assert.That(inspector.MicronsPerFocuserStep, Is.EqualTo(5.0), "a tester's setting must not be dropped");
            Assert.That(options.FocuserStepSizeMicrons, Is.EqualTo(5.0));
        });
    }

    /// <summary>
    /// The asymmetric half of the guard, and the one that matters: MicronsPerFocuserStep is very often a
    /// measurement off a real rig. A leftover simulator value must never overwrite it.
    /// </summary>
    [Test]
    public void LegacyFocuserStepSizeKey_DoesNotClobberACalibratedInspector() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueDouble(nameof(CameraSimulatorOptions.FocuserStepSizeMicrons), 5.0);
        var inspectorStore = new InMemoryPluginOptionsAccessor();
        inspectorStore.SetValueDouble(nameof(InspectorOptions.MicronsPerFocuserStep), 1.0);
        var inspector = NewInspector(profile, inspectorStore);

        var options = new CameraSimulatorOptions(profile, store, inspector);

        Assert.Multiple(() => {
            Assert.That(inspector.MicronsPerFocuserStep, Is.EqualTo(1.0), "the real-rig calibration wins");
            Assert.That(options.FocuserStepSizeMicrons, Is.EqualTo(1.0));
        });
    }

    /// <summary>
    /// The user's actual profile: MicronsPerFocuserStep = 1, no stored simulator key. The migration must not
    /// touch anything at all — this is the common case and the one it would be worst to get wrong.
    /// </summary>
    [Test]
    public void NoLegacyKey_WithCalibratedInspector_MigrationIsANoOp() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var inspectorStore = new InMemoryPluginOptionsAccessor();
        inspectorStore.SetValueDouble(nameof(InspectorOptions.MicronsPerFocuserStep), 1.0);
        var inspector = NewInspector(profile, inspectorStore);

        var options = new CameraSimulatorOptions(profile, store, inspector);

        Assert.Multiple(() => {
            Assert.That(inspector.MicronsPerFocuserStep, Is.EqualTo(1.0));
            Assert.That(options.FocuserStepSizeMicrons, Is.EqualTo(1.0));
            Assert.That(options.EffectiveFocuserStepSizeMicrons, Is.EqualTo(1.0), "the render uses the user's 1, not 2");
            Assert.That(store.Snapshot.ContainsKey(nameof(CameraSimulatorOptions.FocuserStepSizeMicrons)), Is.False,
                "no legacy key existed, so the migration must not create one");
        });
    }

    /// <summary>
    /// Once consumed, the legacy value is cleared. Otherwise deliberately clearing the calibration on the
    /// Inspector's page and restarting NINA would silently resurrect the old simulator number.
    /// </summary>
    [Test]
    public void LegacyFocuserStepSizeKey_IsClearedOnceMigrated_SoItCannotComeBack() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueDouble(nameof(CameraSimulatorOptions.FocuserStepSizeMicrons), 5.0);

        var first = new CameraSimulatorOptions(profile, store, NewInspector(profile));
        Assert.That(first.FocuserStepSizeMicrons, Is.EqualTo(5.0), "precondition: it migrated once");

        // The user later clears the calibration; a fresh load must leave it cleared.
        var second = new CameraSimulatorOptions(profile, store, NewInspector(profile));
        Assert.That(second.FocuserStepSizeMicrons, Is.EqualTo(-1.0),
            "a consumed legacy value must not resurrect after the user clears the calibration");
    }

    /// <summary>
    /// The profile-swap ordering hazard, pinned. Both objects reload on ProfileChanged: InspectorOptions re-reads
    /// MicronsPerFocuserStep, and the camera sim's migration reads that value back. A migration running against a
    /// not-yet-reloaded inspector would see the PREVIOUS profile's value, mistake a calibrated profile for an
    /// uncalibrated one, and copy the legacy simulator value straight over a real calibration — silently, and
    /// persisted, because the setter writes through.
    ///
    /// <para>The order is structurally guaranteed rather than incidental: CameraSimulatorOptions cannot be
    /// constructed without an IInspectorOptions, so InspectorOptions necessarily exists first, and it subscribes to
    /// ProfileChanged inside its own constructor — putting its handler first in the multicast list. This test is
    /// what makes that fail LOUDLY if InspectorOptions is ever refactored to subscribe or reload lazily.</para>
    /// </summary>
    [Test]
    public void ProfileSwap_MigrationSeesTheNewProfilesInspectorValue_NotTheOldOne() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var inspectorStore = new InMemoryPluginOptionsAccessor();
        var inspector = NewInspector(profile, inspectorStore);
        var options = new CameraSimulatorOptions(profile, store, inspector);
        Assert.That(inspector.MicronsPerFocuserStep, Is.EqualTo(-1.0), "precondition: uncalibrated before the swap");

        // The profile being swapped TO: a real calibration, plus a stale legacy simulator key alongside it.
        inspectorStore.SetValueDouble(nameof(InspectorOptions.MicronsPerFocuserStep), 1.0);
        store.SetValueDouble(nameof(CameraSimulatorOptions.FocuserStepSizeMicrons), 5.0);
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.Multiple(() => {
            // Reading a stale inspector (-1, from before the swap) would have copied the legacy 5.0 over this.
            Assert.That(inspector.MicronsPerFocuserStep, Is.EqualTo(1.0),
                "the new profile's calibration must win over the legacy simulator value");
            Assert.That(options.EffectiveFocuserStepSizeMicrons, Is.EqualTo(1.0), "the render must use the new profile's 1.0");
        });
    }

    [Test]
    public void ResetDefaults_DoesNotClobberTheInspectorsCalibration() {
        var (options, inspector) = BuildWithInspector();
        inspector.MicronsPerFocuserStep = 1.0; // a measurement off the user's real rig

        options.ResetDefaults();

        Assert.That(inspector.MicronsPerFocuserStep, Is.EqualTo(1.0),
            "resetting CAMERA SIMULATOR defaults must not destroy a real-rig Aberration Inspector calibration");
    }

    [Test]
    public void Defaults_MatchDesignConfigTable() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.OptimalFocuserPosition, Is.EqualTo(5000));
            // The step size defaults to the Inspector's -1 "uncalibrated" sentinel, not to a number: it is the
            // Inspector's setting now. The old 2.0 default survives as the Effective fallback.
            Assert.That(options.FocuserStepSizeMicrons, Is.EqualTo(-1.0), "unset: the Inspector is uncalibrated");
            Assert.That(options.EffectiveFocuserStepSizeMicrons, Is.EqualTo(2.0));
            Assert.That(options.ApertureMillimeters, Is.EqualTo(-1.0), "unset: inferred from the profile");
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(-1.0), "unset: inferred from the profile");
            Assert.That(options.CentralObstructionEnabled, Is.True);
            Assert.That(options.CentralObstructionFraction, Is.EqualTo(0.3));
            Assert.That(options.OpticalThroughput, Is.EqualTo(0.85));
            Assert.That(options.SensorModel, Is.EqualTo(SonySensorModel.IMX455));
            Assert.That(options.Gain, Is.EqualTo(100));
            Assert.That(options.BiasPedestalAdu, Is.EqualTo(500));
            Assert.That(options.SensorTemperatureCelsius, Is.EqualTo(-10.0));
            Assert.That(options.Filter, Is.EqualTo(SimulatorFilter.L));
            Assert.That(options.SkyBrightnessMagPerArcsec2, Is.EqualTo(20.5));
            Assert.That(options.SeeingArcsec, Is.EqualTo(2.5));
            Assert.That(options.AstapCatalogPath, Is.EqualTo(CameraSimulatorOptions.DefaultAstapCatalogPath));
            Assert.That(options.LimitingMagnitude, Is.EqualTo(16.0));
            Assert.That(options.RotationDegrees, Is.EqualTo(0.0));
            Assert.That(options.NoiseSeed, Is.EqualTo(42));
            Assert.That(options.EnableAberrations, Is.False);
            Assert.That(options.TiltAngleDegrees, Is.EqualTo(0.0));
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(0.0));
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(0.0));
            Assert.That(options.OpticalAxisOffsetXMicrons, Is.EqualTo(0.0));
            Assert.That(options.OpticalAxisOffsetYMicrons, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void Setters_PersistToAccessorAndReadBack() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        // FocuserStepSizeMicrons now persists into the INSPECTOR's store, under the Inspector's key — it IS the
        // Inspector's setting. Holding that store explicitly is what lets the re-read below prove the round-trip
        // goes through the shared variable rather than through a camera-sim copy of it.
        var inspectorStore = new InMemoryPluginOptionsAccessor();
        var options = new CameraSimulatorOptions(profile, store, NewInspector(profile, inspectorStore));

        options.OptimalFocuserPosition = 12345;
        options.FocuserStepSizeMicrons = 0.5;
        options.ApertureMillimeters = 200.0;
        options.FocalLengthMillimeters = 800.0;
        options.CentralObstructionEnabled = false;
        options.CentralObstructionFraction = 0.4;
        options.OpticalThroughput = 0.7;
        options.SensorModel = SonySensorModel.IMX294;
        options.Gain = 200;
        options.BiasPedestalAdu = 125;
        options.SensorTemperatureCelsius = -20.0;
        options.Filter = SimulatorFilter.Ha5;
        options.SkyBrightnessMagPerArcsec2 = 21.5;
        options.SeeingArcsec = 1.8;
        options.AstapCatalogPath = @"D:\astap-db";
        options.LimitingMagnitude = 14.0;
        options.RotationDegrees = 33.0;
        options.NoiseSeed = 7;
        options.EnableAberrations = true;
        options.TiltAngleDegrees = 45.0;
        options.TiltAmountMicrons = 15.0;
        options.BackfocusErrorMicrons = 10.0;
        options.OpticalAxisOffsetXMicrons = 100.0;
        options.OpticalAxisOffsetYMicrons = -50.0;

        // Re-read through a fresh options object over the same stores to prove persistence round-trips.
        var reread = new CameraSimulatorOptions(Substitute.For<IProfileService>(), store, NewInspector(profile, inspectorStore));

        Assert.Multiple(() => {
            Assert.That(store.Snapshot[nameof(CameraSimulatorOptions.OptimalFocuserPosition)], Is.EqualTo(12345));
            Assert.That(store.Snapshot[nameof(CameraSimulatorOptions.SensorModel)], Is.EqualTo(SonySensorModel.IMX294));
            Assert.That(store.Snapshot[nameof(CameraSimulatorOptions.Filter)], Is.EqualTo(SimulatorFilter.Ha5));
            Assert.That(store.Snapshot[nameof(CameraSimulatorOptions.AstapCatalogPath)], Is.EqualTo(@"D:\astap-db"));

            // One variable, one storage location: the step size lands in the Inspector's store under the
            // Inspector's key, and the camera sim keeps no key of its own for it.
            Assert.That(inspectorStore.Snapshot[nameof(InspectorOptions.MicronsPerFocuserStep)], Is.EqualTo(0.5));
            Assert.That(store.Snapshot.ContainsKey(nameof(CameraSimulatorOptions.FocuserStepSizeMicrons)), Is.False,
                "the camera simulator must not keep a second copy of the focuser step size");

            Assert.That(reread.OptimalFocuserPosition, Is.EqualTo(12345));
            Assert.That(reread.FocuserStepSizeMicrons, Is.EqualTo(0.5));
            Assert.That(reread.ApertureMillimeters, Is.EqualTo(200.0));
            Assert.That(reread.FocalLengthMillimeters, Is.EqualTo(800.0));
            Assert.That(reread.CentralObstructionEnabled, Is.False);
            Assert.That(reread.CentralObstructionFraction, Is.EqualTo(0.4));
            Assert.That(reread.OpticalThroughput, Is.EqualTo(0.7));
            Assert.That(reread.SensorModel, Is.EqualTo(SonySensorModel.IMX294));
            Assert.That(reread.Gain, Is.EqualTo(200));
            Assert.That(reread.BiasPedestalAdu, Is.EqualTo(125));
            Assert.That(reread.SensorTemperatureCelsius, Is.EqualTo(-20.0));
            Assert.That(reread.Filter, Is.EqualTo(SimulatorFilter.Ha5));
            Assert.That(reread.SkyBrightnessMagPerArcsec2, Is.EqualTo(21.5));
            Assert.That(reread.SeeingArcsec, Is.EqualTo(1.8));
            Assert.That(reread.AstapCatalogPath, Is.EqualTo(@"D:\astap-db"));
            Assert.That(reread.LimitingMagnitude, Is.EqualTo(14.0));
            Assert.That(reread.RotationDegrees, Is.EqualTo(33.0));
            Assert.That(reread.NoiseSeed, Is.EqualTo(7));
            Assert.That(reread.EnableAberrations, Is.True);
            Assert.That(reread.TiltAngleDegrees, Is.EqualTo(45.0));
            Assert.That(reread.TiltAmountMicrons, Is.EqualTo(15.0));
            Assert.That(reread.BackfocusErrorMicrons, Is.EqualTo(10.0));
            Assert.That(reread.OpticalAxisOffsetXMicrons, Is.EqualTo(100.0));
            Assert.That(reread.OpticalAxisOffsetYMicrons, Is.EqualTo(-50.0));
        });
    }

    [Test]
    public void ResetDefaults_RestoresDocumentedDefaults() {
        var (options, _, _) = Build();
        options.OptimalFocuserPosition = 999;
        options.SensorModel = SonySensorModel.IMX533;
        options.Filter = SimulatorFilter.SII3;
        options.EnableAberrations = true;
        options.TiltAmountMicrons = 42.0;
        options.OpticalThroughput = 0.5;
        options.AstapCatalogPath = @"D:\somewhere";
        // Screw 4 resets to NaN, the one direction the NaN-aware setter guard could plausibly break (value -> NaN).
        options.SimScrew4AngleDegrees = 270.0;

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.OptimalFocuserPosition, Is.EqualTo(5000));
            Assert.That(options.SensorModel, Is.EqualTo(SonySensorModel.IMX455));
            Assert.That(options.Filter, Is.EqualTo(SimulatorFilter.L));
            Assert.That(options.EnableAberrations, Is.False);
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(0.0));
            Assert.That(options.OpticalThroughput, Is.EqualTo(0.85));
            Assert.That(options.AstapCatalogPath, Is.EqualTo(CameraSimulatorOptions.DefaultAstapCatalogPath));
            Assert.That(double.IsNaN(options.SimScrew4AngleDegrees), Is.True);
        });
    }

    [TestCase(nameof(CameraSimulatorOptions.OptimalFocuserPosition), 123)]
    [TestCase(nameof(CameraSimulatorOptions.FocuserStepSizeMicrons), 1.25)]
    [TestCase(nameof(CameraSimulatorOptions.CentralObstructionEnabled), false)]
    [TestCase(nameof(CameraSimulatorOptions.EnableAberrations), true)]
    [TestCase(nameof(CameraSimulatorOptions.AstapCatalogPath), @"C:\astap")]
    public void Setter_RaisesPropertyChanged(string propertyName, object newValue) {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var prop = typeof(CameraSimulatorOptions).GetProperty(propertyName);
        prop.SetValue(options, Convert.ChangeType(newValue, prop.PropertyType));
        Assert.That(raised, Does.Contain(propertyName));
    }

    [Test]
    public void Gain_ClampsToSelectedSensorMaxGain() {
        var (options, _, _) = Build();
        options.SensorModel = SonySensorModel.IMX455; // MaxGain 300
        options.Gain = 9999;
        Assert.That(options.Gain, Is.EqualTo(300));
    }

    [Test]
    public void Gain_ClampsNegativeToZero() {
        var (options, _, _) = Build();
        options.Gain = -50;
        Assert.That(options.Gain, Is.EqualTo(0));
    }

    [Test]
    public void SensorModelChange_ReclampsGainToNewSensorRange() {
        var (options, _, _) = Build();
        options.SensorModel = SonySensorModel.IMX294; // MaxGain 400
        options.Gain = 400;
        Assert.That(options.Gain, Is.EqualTo(400));

        options.SensorModel = SonySensorModel.IMX455; // MaxGain 300 → gain re-clamps
        Assert.That(options.Gain, Is.EqualTo(300));
    }

    [Test]
    public void ProfileChanged_ReinitializesOptions() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new CameraSimulatorOptions(profile, store, NewInspector(profile));
        options.OptimalFocuserPosition = 8888;
        Assert.That(options.OptimalFocuserPosition, Is.EqualTo(8888));

        store.Clear();
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.That(options.OptimalFocuserPosition, Is.EqualTo(5000));
    }

    [Test]
    public void Constructor_ThrowsOnNullAccessor() {
        var profile = Substitute.For<IProfileService>();
        Assert.Throws<ArgumentNullException>(() => new CameraSimulatorOptions(profile, null, NewInspector(profile)));
    }

    [Test]
    public void Constructor_ThrowsOnNullInspectorOptions() {
        var profile = Substitute.For<IProfileService>();
        Assert.Throws<ArgumentNullException>(
            () => new CameraSimulatorOptions(profile, new InMemoryPluginOptionsAccessor(), null));
    }

    [Test]
    public void SimTiltAdapterDefaults_MatchDesign() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.SimScrewCount, Is.EqualTo(3));
            Assert.That(options.SimScrew1AngleDegrees, Is.EqualTo(0.0));
            Assert.That(options.SimScrew2AngleDegrees, Is.EqualTo(120.0));
            Assert.That(options.SimScrew3AngleDegrees, Is.EqualTo(240.0));
            Assert.That(double.IsNaN(options.SimScrew4AngleDegrees), Is.True);
            Assert.That(options.SimScrewInwardCurvatureSign,
                Is.EqualTo(TiltScrewGeometry.DefaultScrewInwardCurvatureSign));
            Assert.That(options.SimAdjustmentType, Is.EqualTo(TiltAdjustmentType.Screws));
            Assert.That(options.SimThreadPitchMicrons, Is.EqualTo(500.0));
            Assert.That(options.SimStepperStepSizeMicrons, Is.EqualTo(1.0));
            Assert.That(options.SimScrewRadiusMillimeters, Is.EqualTo(30.0));
            Assert.That(options.ShowSimulatorTiltAdapterPanel, Is.False);
        });
    }

    [Test]
    public void SimTiltAdapterOptions_PersistAndReadBack() {
        var store = new InMemoryPluginOptionsAccessor();
        var profileA = Substitute.For<IProfileService>();
        var a = new CameraSimulatorOptions(profileA, store, NewInspector(profileA));
        a.SimScrewCount = 4;
        a.SimScrew4AngleDegrees = 270.0;
        a.SimThreadPitchMicrons = 350.0;
        a.ShowSimulatorTiltAdapterPanel = true;

        var profileB = Substitute.For<IProfileService>();
        var b = new CameraSimulatorOptions(profileB, store, NewInspector(profileB));
        Assert.Multiple(() => {
            Assert.That(b.SimScrewCount, Is.EqualTo(4));
            Assert.That(b.SimScrew4AngleDegrees, Is.EqualTo(270.0));
            Assert.That(b.SimThreadPitchMicrons, Is.EqualTo(350.0));
            Assert.That(b.ShowSimulatorTiltAdapterPanel, Is.True);
        });
    }

    /// <summary>
    /// A stored screw count outside 3|4 (hand-edited or legacy profile) must heal to 3 on load. The setter clamps,
    /// but InitializeOptions assigns the backing field directly, so the stored value would otherwise survive and
    /// reach SimulatedTiltAdapter, which rejects anything but 3 or 4.
    /// </summary>
    [TestCase(5, 3)]
    [TestCase(0, 3)]
    [TestCase(-1, 3)]
    [TestCase(3, 3)]
    [TestCase(4, 4)]
    public void SimScrewCount_HealsOutOfRangeStoredValueOnLoad(int stored, int expected) {
        var store = new InMemoryPluginOptionsAccessor();
        // Seed through the accessor, bypassing the clamping property setter, as a hand-edited profile would.
        store.SetValueInt32(nameof(CameraSimulatorOptions.SimScrewCount), stored);

        var profileService = Substitute.For<IProfileService>();
        var options = new CameraSimulatorOptions(profileService, store, NewInspector(profileService));

        Assert.That(options.SimScrewCount, Is.EqualTo(expected));
    }

    /// <summary>
    /// Screw 4 defaults to <c>double.NaN</c> (the 3-screw convention), and <c>NaN != NaN</c> is always true — a
    /// naive <c>if (field != value)</c> guard would fire on every set, re-persisting and raising forever. Setting
    /// NaN over the NaN default must be a no-op.
    /// </summary>
    [Test]
    public void SimScrew4Angle_SettingNaNOverNaNDefault_DoesNotRaiseOrPersist() {
        var (options, store, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        options.SimScrew4AngleDegrees = double.NaN;

        Assert.Multiple(() => {
            Assert.That(raised, Does.Not.Contain(nameof(CameraSimulatorOptions.SimScrew4AngleDegrees)));
            Assert.That(store.Snapshot.ContainsKey(nameof(CameraSimulatorOptions.SimScrew4AngleDegrees)), Is.False);
            Assert.That(double.IsNaN(options.SimScrew4AngleDegrees), Is.True);
        });
    }
}
