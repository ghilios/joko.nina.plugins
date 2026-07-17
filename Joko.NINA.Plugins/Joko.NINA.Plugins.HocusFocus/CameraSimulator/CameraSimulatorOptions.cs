#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator {

    /// <summary>
    /// Persisted options for the synthetic star-field camera, modeled on <see cref="InspectorOptions"/>:
    /// a public constructor for production (backed by a real <see cref="PluginOptionsAccessor"/>) and an
    /// internal constructor for tests (injected accessor). Defaults come from the design's config table.
    /// </summary>
    public class CameraSimulatorOptions : BaseINPC, ICameraSimulatorOptions {
        private readonly IProfileService profileService;
        private readonly IPluginOptionsAccessor optionsAccessor;

        // FocuserStepSizeMicrons is a pass-through onto this, not a copy of it — see the property. Held as the
        // interface so tests can inject a real InspectorOptions over an in-memory store.
        private readonly IInspectorOptions inspectorOptions;

        // EffectiveFocalLengthMillimeters/EffectiveApertureMillimeters read the ACTIVE profile's TelescopeSettings;
        // these track the settings object currently subscribed so in-place edits refresh the inferred values and
        // profile swaps re-hook cleanly. Mirrors InspectorVM/TiltAdapterWizardVM's FocuserSettings hook.
        private readonly PropertyChangedEventHandler telescopeSettingsHandler;
        private ITelescopeSettings hookedTelescopeSettings;

        private void HookActiveProfileTelescopeSettings() {
            if (hookedTelescopeSettings != null) {
                hookedTelescopeSettings.PropertyChanged -= telescopeSettingsHandler;
            }
            hookedTelescopeSettings = profileService?.ActiveProfile?.TelescopeSettings;
            if (hookedTelescopeSettings != null) {
                hookedTelescopeSettings.PropertyChanged += telescopeSettingsHandler;
            }
        }

        public CameraSimulatorOptions(IProfileService profileService, IInspectorOptions inspectorOptions)
            : this(profileService, CreateDefaultAccessor(profileService), inspectorOptions) {
        }

        internal CameraSimulatorOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor, IInspectorOptions inspectorOptions) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
            this.inspectorOptions = inspectorOptions ?? throw new ArgumentNullException(nameof(inspectorOptions));

            // FocuserStepSizeMicrons reads THROUGH to inspectorOptions, so a value edited on the Inspector's own
            // page must re-raise here or this page shows a stale number while the render already uses the new one —
            // the same staleness the TelescopeSettings hook below exists to prevent, for the same reason. The
            // InspectorOptions instance never changes (unlike TelescopeSettings, which a profile swap replaces), so
            // this subscribes once and never re-hooks.
            inspectorOptions.PropertyChanged += InspectorOptions_PropertyChanged;
            // The inferred optics read the active profile's focal length / focal ratio, which the user can edit in
            // place (Options → Equipment → Telescope) without swapping profiles — ProfileChanged alone would leave
            // a bound hint showing the pre-edit number while the render, which re-reads per exposure, already uses
            // the new one. Track the active profile's TelescopeSettings and re-hook on every profile change
            // (unsubscribe old, subscribe new). Assigned before the first hook, which needs the handler.
            telescopeSettingsHandler = (s, e) => {
                if (e.PropertyName == nameof(ITelescopeSettings.FocalLength) ||
                    e.PropertyName == nameof(ITelescopeSettings.FocalRatio)) {
                    RaisePropertyChanged(nameof(EffectiveFocalLengthMillimeters));
                    // The inferred aperture is focal length ÷ focal ratio, so EITHER edit moves it.
                    RaisePropertyChanged(nameof(EffectiveApertureMillimeters));
                }
            };
            HookActiveProfileTelescopeSettings();
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            InitializeOptions();
        }

        private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(CameraSimulatorOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }
            return new PluginOptionsAccessor(profileService, guid.Value);
        }

        private void InspectorOptions_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(IInspectorOptions.MicronsPerFocuserStep)) {
                RaisePropertyChanged(nameof(FocuserStepSizeMicrons));
                // The hint text resolves through the same value, so it moves with it.
                RaisePropertyChanged(nameof(EffectiveFocuserStepSizeMicrons));
            }
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            // The swap replaces the TelescopeSettings instance, so the subscription must move to the new profile's
            // object; the old one would otherwise keep reporting a profile that is no longer active.
            HookActiveProfileTelescopeSettings();
            InitializeOptions();
            RaiseAllPropertiesChanged();
        }

        /// <summary>The default ASTAP database directory: <c>%ProgramFiles%\astap</c>.</summary>
        public static string DefaultAstapCatalogPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "astap");

        /// <summary>
        /// The focal length (mm) used when neither the option nor the active profile supplies one. A fresh NINA
        /// profile stores NaN for both telescope values, so this is the out-of-the-box rig, not a rare edge case.
        /// Paired with <see cref="DefaultFocalRatio"/> it describes a 980 mm f/7 — which at 3.76 µm samples at
        /// 0.79″/px and puts HFR_min near 1.8 px, comfortably above HocusFocus's own minimum-HFR detection gate.
        /// </summary>
        public const double DefaultFocalLengthMillimeters = 980.0;

        /// <summary>
        /// The focal ratio used to infer an aperture when the profile has none. Applied to the <b>effective</b>
        /// focal length rather than yielding a flat aperture, so a long profile focal length cannot silently
        /// produce an absurd f-ratio (the previous flat 100 mm default made a 2000 mm profile an f/20).
        /// </summary>
        public const double DefaultFocalRatio = 7.0;

        /// <summary>
        /// The focuser step size (µm/step) used when the Aberration Inspector's <c>MicronsPerFocuserStep</c> is
        /// uncalibrated. This was the simulator's own default back when it had a second, independent copy of the
        /// value; it survives as the fallback so an uncalibrated rig still renders a sane defocus ramp rather than
        /// throwing out of <see cref="Rendering.DefocusModel"/>, which rejects a non-positive k.
        /// </summary>
        public const double DefaultFocuserStepSizeMicrons = 2.0;

        /// <summary>The stored value meaning "unset — infer it". Matches the plugin's <c>DoubleNegativeToEmptyStringConverter</c> convention.</summary>
        private const double Unset = -1.0;

        /// <summary>Collapses anything non-positive (including NaN, which no ordinary comparison rejects) to <see cref="Unset"/>.</summary>
        private static double NormalizeUnset(double value) => value > 0.0 ? value : Unset;

        /// <summary>
        /// Upper bounds for the optics, previously enforced by the setup dialog's FloatRangeRules and now enforced
        /// on commit (the rules had to go: they run on the raw text before the converter, so they rejected the
        /// empty "unset" box).
        ///
        /// <para>These caps are not cosmetic. A defocused star's outer radius goes as |Δ| / (2N), so an absurd
        /// aperture drives the f-number N toward zero and the radius through
        /// <see cref="Rendering.PsfKernelGenerator"/>'s MaxKernelRadius ceiling — which throws a bare
        /// ArgumentOutOfRangeException that NINA surfaces as "Unexpected error", plus a spurious AbortExposure,
        /// rather than as an actionable message. At 2000 mm / 980 mm (N = 0.49) the ceiling needs ~1900 µm of
        /// defocus, which an AutoFocus sweep never reaches; at an unclamped 999999 mm it needs ~4 µm, about two
        /// focuser steps, so every exposure would throw.</para>
        /// </summary>
        private const double MaxApertureMillimeters = 2000.0;

        /// <inheritdoc cref="MaxApertureMillimeters"/>
        private const double MaxFocalLengthMillimeters = 20000.0;

        /// <summary>
        /// Resolves a user-entered optic to either <see cref="Unset"/> or a usable value in <c>[1, maximum]</c>,
        /// mirroring <see cref="ClampGain"/>'s heal-on-commit precedent.
        ///
        /// <para>The <see cref="NormalizeUnset"/> sentinel check runs FIRST and wins: 0 and negatives mean
        /// "unset — infer it" and must stay <see cref="Unset"/>, never clamp up to 1, or a blank box would
        /// silently become a 1 mm aperture and inference — the whole point of the blank box — would break.
        /// Only a value the user actually meant gets clamped.</para>
        /// </summary>
        private static double NormalizeOptic(double value, double maximum) {
            var normalized = NormalizeUnset(value);
            return normalized > 0.0 ? Math.Clamp(normalized, 1.0, maximum) : normalized;
        }

        /// <summary>Clamp a raw gain into the usable <c>[0, MaxGain]</c> range of the given sensor.</summary>
        private static int ClampGain(int value, SonySensorModel model) {
            return Math.Clamp(value, 0, SensorRegistry.Get(model).MaxGain);
        }

        /// <summary>
        /// Rescues a focuser step size written under the simulator's own (now deleted) <c>FocuserStepSizeMicrons</c>
        /// key, back when it was a second copy of the Inspector's <c>MicronsPerFocuserStep</c>. Only anyone who ran
        /// an in-development build of this branch has one; for everyone else the key is absent and this is a no-op.
        ///
        /// <para>The guard is deliberately asymmetric: it copies ONLY into an uncalibrated Inspector. The Inspector's
        /// value can be a measurement off a real rig, and a simulator leftover must never overwrite that — the
        /// migration exists to avoid silently dropping a tester's setting, which does not justify silently
        /// destroying a real one. `!(x > 0)` rather than `x <= 0` because NaN fails both comparisons.</para>
        ///
        /// <para>The legacy key is cleared once the value is copied, so this cannot resurrect the old number later
        /// (e.g. after the user deliberately clears their calibration). Same heal-the-store-once precedent as
        /// InspectorOptions' StepCount repair.</para>
        ///
        /// <para>Ordering note: on a profile swap this runs from <see cref="ProfileService_ProfileChanged"/> and
        /// reads <c>inspectorOptions</c>, which reloads on the same event. HocusFocusPlugin constructs
        /// InspectorOptions BEFORE CameraSimulatorOptions, so InspectorOptions subscribed first and has already
        /// reloaded from the new profile by the time this runs.</para>
        /// </summary>
        private void MigrateLegacyFocuserStepSize() {
            var legacy = optionsAccessor.GetValueDouble(nameof(FocuserStepSizeMicrons), Unset);
            if (!(legacy > 0.0)) {
                return;
            }
            if (!(inspectorOptions.MicronsPerFocuserStep > 0.0)) {
                Logger.Info($"CameraSimulatorOptions: migrating the simulator's legacy FocuserStepSizeMicrons ({legacy} µm/step) onto the Aberration Inspector's uncalibrated MicronsPerFocuserStep — they are now one setting.");
                inspectorOptions.MicronsPerFocuserStep = legacy;
            } else {
                Logger.Info($"CameraSimulatorOptions: discarding the simulator's legacy FocuserStepSizeMicrons ({legacy} µm/step); the Aberration Inspector is already calibrated at {inspectorOptions.MicronsPerFocuserStep} µm/step, which wins.");
            }
            optionsAccessor.SetValueDouble(nameof(FocuserStepSizeMicrons), Unset);
        }

        private void InitializeOptions() {
            optimalFocuserPosition = optionsAccessor.GetValueInt32(nameof(OptimalFocuserPosition), 5000);
            MigrateLegacyFocuserStepSize();
            // -1 means "unset — infer from the profile". The read default is now Unset rather than the old
            // hard-coded 0 mm focal length / 100 mm aperture, so an unwritten key (the common case — a profile
            // that never touched these) infers from the profile instead of silently rendering at 100 mm,
            // ignoring the profile's focal ratio entirely. NormalizeOptic additionally heals a stored
            // non-positive value, such as the 0 an older build's ResetDefaults wrote for the focal length, and
            // a stored out-of-range one, which became reachable once the setup dialog's FloatRangeRules were
            // dropped — same heal-on-read as ClampGain below, and for the same reason: the render must never be
            // handed a value the current bounds would reject.
            // A value the user actually typed is in range and survives untouched — including a deliberate 100.
            apertureMillimeters = NormalizeOptic(optionsAccessor.GetValueDouble(nameof(ApertureMillimeters), Unset), MaxApertureMillimeters);
            focalLengthMillimeters = NormalizeOptic(optionsAccessor.GetValueDouble(nameof(FocalLengthMillimeters), Unset), MaxFocalLengthMillimeters);
            centralObstructionEnabled = optionsAccessor.GetValueBoolean(nameof(CentralObstructionEnabled), true);
            centralObstructionFraction = optionsAccessor.GetValueDouble(nameof(CentralObstructionFraction), 0.3);
            opticalThroughput = optionsAccessor.GetValueDouble(nameof(OpticalThroughput), 0.85);
            sensorModel = optionsAccessor.GetValueEnum(nameof(SensorModel), SonySensorModel.IMX455);
            // Heal a stored gain that exceeds the loaded sensor's usable range (e.g. after switching sensors in an
            // older build). SensorModel is loaded first so the clamp uses the correct sensor.
            gain = ClampGain(optionsAccessor.GetValueInt32(nameof(Gain), 100), sensorModel);
            biasPedestalAdu = optionsAccessor.GetValueInt32(nameof(BiasPedestalAdu), 500);
            sensorTemperatureCelsius = optionsAccessor.GetValueDouble(nameof(SensorTemperatureCelsius), -10.0);
            filter = optionsAccessor.GetValueEnum(nameof(Filter), SimulatorFilter.L);
            skyBrightnessMagPerArcsec2 = optionsAccessor.GetValueDouble(nameof(SkyBrightnessMagPerArcsec2), 20.5);
            seeingArcsec = optionsAccessor.GetValueDouble(nameof(SeeingArcsec), 2.5);
            astapCatalogPath = optionsAccessor.GetValueString(nameof(AstapCatalogPath), DefaultAstapCatalogPath);
            limitingMagnitude = optionsAccessor.GetValueDouble(nameof(LimitingMagnitude), 16.0);
            rotationDegrees = optionsAccessor.GetValueDouble(nameof(RotationDegrees), 0.0);
            noiseSeed = optionsAccessor.GetValueInt32(nameof(NoiseSeed), 42);
            enableAberrations = optionsAccessor.GetValueBoolean(nameof(EnableAberrations), false);
            tiltAngleDegrees = optionsAccessor.GetValueDouble(nameof(TiltAngleDegrees), 0.0);
            tiltAmountMicrons = optionsAccessor.GetValueDouble(nameof(TiltAmountMicrons), 0.0);
            backfocusErrorMicrons = optionsAccessor.GetValueDouble(nameof(BackfocusErrorMicrons), 0.0);
            opticalAxisOffsetXMicrons = optionsAccessor.GetValueDouble(nameof(OpticalAxisOffsetXMicrons), 0.0);
            opticalAxisOffsetYMicrons = optionsAccessor.GetValueDouble(nameof(OpticalAxisOffsetYMicrons), 0.0);
            // Heal a stored screw count outside 3|4 (a hand-edited or legacy profile). SimulatedTiltAdapter rejects
            // anything else, so an unhealed value would surface as a panel-construction crash rather than a 3.
            simScrewCount = optionsAccessor.GetValueInt32(nameof(SimScrewCount), 3) == 4 ? 4 : 3;
            simScrew1AngleDegrees = optionsAccessor.GetValueDouble(nameof(SimScrew1AngleDegrees), 0.0);
            simScrew2AngleDegrees = optionsAccessor.GetValueDouble(nameof(SimScrew2AngleDegrees), 120.0);
            simScrew3AngleDegrees = optionsAccessor.GetValueDouble(nameof(SimScrew3AngleDegrees), 240.0);
            simScrew4AngleDegrees = optionsAccessor.GetValueDouble(nameof(SimScrew4AngleDegrees), double.NaN);
            simScrewInwardCurvatureSign = optionsAccessor.GetValueInt32(nameof(SimScrewInwardCurvatureSign),
                TiltScrewGeometry.DefaultScrewInwardCurvatureSign);
            simAdjustmentType = optionsAccessor.GetValueEnum(nameof(SimAdjustmentType), TiltAdjustmentType.Screws);
            simThreadPitchMicrons = optionsAccessor.GetValueDouble(nameof(SimThreadPitchMicrons), 500.0);
            simStepperStepSizeMicrons = optionsAccessor.GetValueDouble(nameof(SimStepperStepSizeMicrons), 1.0);
            simScrewRadiusMillimeters = optionsAccessor.GetValueDouble(nameof(SimScrewRadiusMillimeters), 30.0);
            showSimulatorTiltAdapterPanel = optionsAccessor.GetValueBoolean(nameof(ShowSimulatorTiltAdapterPanel), false);
        }

        public void ResetDefaults() {
            OptimalFocuserPosition = 5000;
            // FocuserStepSizeMicrons is deliberately NOT reset. It is no longer the simulator's own setting — it is
            // the Aberration Inspector's MicronsPerFocuserStep, which is very often a measurement off the user's
            // real rig. "Reset camera simulator defaults" must stay inside the camera simulator; silently destroying
            // a physical calibration from here would be a far worse surprise than leaving a shared value alone.
            // Resetting it is still one click away, in the Inspector's own options, where it is labelled as such.
            ApertureMillimeters = Unset;
            FocalLengthMillimeters = Unset;
            CentralObstructionEnabled = true;
            CentralObstructionFraction = 0.3;
            OpticalThroughput = 0.85;
            SensorModel = SonySensorModel.IMX455;
            Gain = 100;
            BiasPedestalAdu = 500;
            SensorTemperatureCelsius = -10.0;
            Filter = SimulatorFilter.L;
            SkyBrightnessMagPerArcsec2 = 20.5;
            SeeingArcsec = 2.5;
            AstapCatalogPath = DefaultAstapCatalogPath;
            LimitingMagnitude = 16.0;
            RotationDegrees = 0.0;
            NoiseSeed = 42;
            EnableAberrations = false;
            TiltAngleDegrees = 0.0;
            TiltAmountMicrons = 0.0;
            BackfocusErrorMicrons = 0.0;
            OpticalAxisOffsetXMicrons = 0.0;
            OpticalAxisOffsetYMicrons = 0.0;
            SimScrewCount = 3;
            SimScrew1AngleDegrees = 0.0;
            SimScrew2AngleDegrees = 120.0;
            SimScrew3AngleDegrees = 240.0;
            SimScrew4AngleDegrees = double.NaN;
            SimScrewInwardCurvatureSign = TiltScrewGeometry.DefaultScrewInwardCurvatureSign;
            SimAdjustmentType = TiltAdjustmentType.Screws;
            SimThreadPitchMicrons = 500.0;
            SimStepperStepSizeMicrons = 1.0;
            SimScrewRadiusMillimeters = 30.0;
            ShowSimulatorTiltAdapterPanel = false;
        }

        private int optimalFocuserPosition;

        public int OptimalFocuserPosition {
            get => optimalFocuserPosition;
            set {
                if (optimalFocuserPosition != value) {
                    optimalFocuserPosition = value;
                    optionsAccessor.SetValueInt32(nameof(OptimalFocuserPosition), optimalFocuserPosition);
                    RaisePropertyChanged();
                }
            }
        }

        /// <inheritdoc cref="ICameraSimulatorOptions.FocuserStepSizeMicrons"/>
        /// <remarks>
        /// No backing field and no accessor key by design: this IS the Inspector's MicronsPerFocuserStep, stored
        /// once, in the Inspector's own option store. A second copy kept "in sync" is the bug this replaced — the
        /// camera rendered at its own default 2.0 µm/step while the Inspector interpreted the result at the user's
        /// calibrated 1.0, so every recovered tilt came back 2x wrong with nothing to show for it.
        ///
        /// <para>The setter raises nothing itself: writing through re-enters via
        /// <see cref="InspectorOptions_PropertyChanged"/>, which raises both this and the Effective value. One
        /// notification path, so an edit here and an edit on the Inspector's page behave identically.</para>
        /// </remarks>
        public double FocuserStepSizeMicrons {
            get => inspectorOptions.MicronsPerFocuserStep;
            set => inspectorOptions.MicronsPerFocuserStep = value;
        }

        /// <inheritdoc cref="ICameraSimulatorOptions.EffectiveFocuserStepSizeMicrons"/>
        public double EffectiveFocuserStepSizeMicrons {
            get {
                // `> 0` and not `!(<= 0)`: NaN fails BOTH comparisons, so the `<=` form would wave it through into
                // DefocusModel and produce an all-NaN frame with no error anywhere (see DefocusModel's ctor guards).
                var configured = inspectorOptions.MicronsPerFocuserStep;
                return configured > 0.0 ? configured : DefaultFocuserStepSizeMicrons;
            }
        }

        private double apertureMillimeters;

        public double ApertureMillimeters {
            get => apertureMillimeters;
            set {
                var normalized = NormalizeOptic(value, MaxApertureMillimeters);
                if (apertureMillimeters != normalized) {
                    apertureMillimeters = normalized;
                    optionsAccessor.SetValueDouble(nameof(ApertureMillimeters), apertureMillimeters);
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(EffectiveApertureMillimeters));
                }
            }
        }

        private double focalLengthMillimeters;

        public double FocalLengthMillimeters {
            get => focalLengthMillimeters;
            set {
                var normalized = NormalizeOptic(value, MaxFocalLengthMillimeters);
                if (focalLengthMillimeters != normalized) {
                    focalLengthMillimeters = normalized;
                    optionsAccessor.SetValueDouble(nameof(FocalLengthMillimeters), focalLengthMillimeters);
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(EffectiveFocalLengthMillimeters));
                    // An unset aperture is inferred FROM the focal length, so its hint moves with this.
                    RaisePropertyChanged(nameof(EffectiveApertureMillimeters));
                }
            }
        }

        /// <summary>
        /// The focal length (mm) the next exposure will actually use: the option when set, else the active
        /// profile's telescope focal length, else <see cref="DefaultFocalLengthMillimeters"/>.
        ///
        /// <para>This is the single resolution point: the render and the setup dialog's hint text both read it,
        /// so a shown value and a rendered value can never be derived by different rules — the failure mode where
        /// a hint lies because it was computed separately. That guarantee holds per <i>read</i>, which is not the
        /// same as per <i>notification</i>: the render re-reads on every exposure, but a bound hint only updates
        /// when told to. Every input this resolves over must therefore raise PropertyChanged for it — the option
        /// setters do, a profile swap does, and in-place TelescopeSettings edits do via
        /// <see cref="HookActiveProfileTelescopeSettings"/>. A new input needs the same treatment or the hint
        /// goes stale while the render moves on.</para>
        /// </summary>
        public double EffectiveFocalLengthMillimeters {
            get {
                if (focalLengthMillimeters > 0.0) {
                    return focalLengthMillimeters;
                }
                // `> 0` and not `!(<= 0)`: a fresh NINA profile stores NaN, which fails BOTH comparisons.
                var profileFocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength;
                return profileFocalLength > 0.0 ? profileFocalLength : DefaultFocalLengthMillimeters;
            }
        }

        /// <summary>
        /// The aperture (mm) the next exposure will actually use: the option when set, else
        /// <see cref="EffectiveFocalLengthMillimeters"/> divided by the profile's focal ratio (so leaving the
        /// aperture blank simply means "my profile's focal ratio"), else by <see cref="DefaultFocalRatio"/>.
        /// Never NaN and never non-positive, so the optics models always receive a usable diameter.
        /// </summary>
        public double EffectiveApertureMillimeters {
            get {
                if (apertureMillimeters > 0.0) {
                    return apertureMillimeters;
                }
                var profileFocalRatio = profileService.ActiveProfile.TelescopeSettings.FocalRatio;
                var focalRatio = profileFocalRatio > 0.0 ? profileFocalRatio : DefaultFocalRatio;
                return EffectiveFocalLengthMillimeters / focalRatio;
            }
        }

        private bool centralObstructionEnabled;

        public bool CentralObstructionEnabled {
            get => centralObstructionEnabled;
            set {
                if (centralObstructionEnabled != value) {
                    centralObstructionEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(CentralObstructionEnabled), centralObstructionEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private double centralObstructionFraction;

        public double CentralObstructionFraction {
            get => centralObstructionFraction;
            set {
                if (centralObstructionFraction != value) {
                    centralObstructionFraction = value;
                    optionsAccessor.SetValueDouble(nameof(CentralObstructionFraction), centralObstructionFraction);
                    RaisePropertyChanged();
                }
            }
        }

        private double opticalThroughput;

        public double OpticalThroughput {
            get => opticalThroughput;
            set {
                if (opticalThroughput != value) {
                    opticalThroughput = value;
                    optionsAccessor.SetValueDouble(nameof(OpticalThroughput), opticalThroughput);
                    RaisePropertyChanged();
                }
            }
        }

        private SonySensorModel sensorModel;

        public SonySensorModel SensorModel {
            get => sensorModel;
            set {
                if (sensorModel != value) {
                    sensorModel = value;
                    optionsAccessor.SetValueEnum(nameof(SensorModel), sensorModel);
                    RaisePropertyChanged();
                    // Different sensors have different MaxGain (e.g. IMX455=300, IMX294=400). Re-clamp the current
                    // gain into the new sensor's range so it can never exceed the selected sensor's usable maximum.
                    var reclampedGain = ClampGain(gain, sensorModel);
                    if (gain != reclampedGain) {
                        gain = reclampedGain;
                        optionsAccessor.SetValueInt32(nameof(Gain), gain);
                        RaisePropertyChanged(nameof(Gain));
                    }
                }
            }
        }

        private int gain;

        public int Gain {
            get => gain;
            set {
                var clamped = ClampGain(value, sensorModel);
                if (gain != clamped) {
                    gain = clamped;
                    optionsAccessor.SetValueInt32(nameof(Gain), gain);
                    RaisePropertyChanged();
                }
            }
        }

        private int biasPedestalAdu;

        public int BiasPedestalAdu {
            get => biasPedestalAdu;
            set {
                if (biasPedestalAdu != value) {
                    biasPedestalAdu = value;
                    optionsAccessor.SetValueInt32(nameof(BiasPedestalAdu), biasPedestalAdu);
                    RaisePropertyChanged();
                }
            }
        }

        private double sensorTemperatureCelsius;

        public double SensorTemperatureCelsius {
            get => sensorTemperatureCelsius;
            set {
                if (sensorTemperatureCelsius != value) {
                    sensorTemperatureCelsius = value;
                    optionsAccessor.SetValueDouble(nameof(SensorTemperatureCelsius), sensorTemperatureCelsius);
                    RaisePropertyChanged();
                }
            }
        }

        private SimulatorFilter filter;

        public SimulatorFilter Filter {
            get => filter;
            set {
                if (filter != value) {
                    filter = value;
                    optionsAccessor.SetValueEnum(nameof(Filter), filter);
                    RaisePropertyChanged();
                }
            }
        }

        private double skyBrightnessMagPerArcsec2;

        public double SkyBrightnessMagPerArcsec2 {
            get => skyBrightnessMagPerArcsec2;
            set {
                if (skyBrightnessMagPerArcsec2 != value) {
                    skyBrightnessMagPerArcsec2 = value;
                    optionsAccessor.SetValueDouble(nameof(SkyBrightnessMagPerArcsec2), skyBrightnessMagPerArcsec2);
                    RaisePropertyChanged();
                }
            }
        }

        private double seeingArcsec;

        public double SeeingArcsec {
            get => seeingArcsec;
            set {
                if (seeingArcsec != value) {
                    seeingArcsec = value;
                    optionsAccessor.SetValueDouble(nameof(SeeingArcsec), seeingArcsec);
                    RaisePropertyChanged();
                }
            }
        }

        private string astapCatalogPath;

        public string AstapCatalogPath {
            get => astapCatalogPath;
            set {
                if (astapCatalogPath != value) {
                    astapCatalogPath = value;
                    optionsAccessor.SetValueString(nameof(AstapCatalogPath), astapCatalogPath);
                    RaisePropertyChanged();
                }
            }
        }

        private double limitingMagnitude;

        public double LimitingMagnitude {
            get => limitingMagnitude;
            set {
                if (limitingMagnitude != value) {
                    limitingMagnitude = value;
                    optionsAccessor.SetValueDouble(nameof(LimitingMagnitude), limitingMagnitude);
                    RaisePropertyChanged();
                }
            }
        }

        private double rotationDegrees;

        public double RotationDegrees {
            get => rotationDegrees;
            set {
                if (rotationDegrees != value) {
                    rotationDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(RotationDegrees), rotationDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private int noiseSeed;

        public int NoiseSeed {
            get => noiseSeed;
            set {
                if (noiseSeed != value) {
                    noiseSeed = value;
                    optionsAccessor.SetValueInt32(nameof(NoiseSeed), noiseSeed);
                    RaisePropertyChanged();
                }
            }
        }

        private bool enableAberrations;

        public bool EnableAberrations {
            get => enableAberrations;
            set {
                if (enableAberrations != value) {
                    enableAberrations = value;
                    optionsAccessor.SetValueBoolean(nameof(EnableAberrations), enableAberrations);
                    RaisePropertyChanged();
                }
            }
        }

        private double tiltAngleDegrees;

        public double TiltAngleDegrees {
            get => tiltAngleDegrees;
            set {
                if (tiltAngleDegrees != value) {
                    tiltAngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(TiltAngleDegrees), tiltAngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private double tiltAmountMicrons;

        public double TiltAmountMicrons {
            get => tiltAmountMicrons;
            set {
                if (tiltAmountMicrons != value) {
                    tiltAmountMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(TiltAmountMicrons), tiltAmountMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double backfocusErrorMicrons;

        public double BackfocusErrorMicrons {
            get => backfocusErrorMicrons;
            set {
                if (backfocusErrorMicrons != value) {
                    backfocusErrorMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(BackfocusErrorMicrons), backfocusErrorMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double opticalAxisOffsetXMicrons;

        public double OpticalAxisOffsetXMicrons {
            get => opticalAxisOffsetXMicrons;
            set {
                if (opticalAxisOffsetXMicrons != value) {
                    opticalAxisOffsetXMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(OpticalAxisOffsetXMicrons), opticalAxisOffsetXMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double opticalAxisOffsetYMicrons;

        public double OpticalAxisOffsetYMicrons {
            get => opticalAxisOffsetYMicrons;
            set {
                if (opticalAxisOffsetYMicrons != value) {
                    opticalAxisOffsetYMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(OpticalAxisOffsetYMicrons), opticalAxisOffsetYMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private int simScrewCount;

        public int SimScrewCount {
            get => simScrewCount;
            set {
                // The adapter model only knows 3- and 4-screw geometries; anything else is a config typo.
                var clamped = value == 4 ? 4 : 3;
                if (simScrewCount != clamped) {
                    simScrewCount = clamped;
                    optionsAccessor.SetValueInt32(nameof(SimScrewCount), simScrewCount);
                    RaisePropertyChanged();
                }
            }
        }

        private double simScrew1AngleDegrees;

        public double SimScrew1AngleDegrees {
            get => simScrew1AngleDegrees;
            set {
                if (simScrew1AngleDegrees != value) {
                    simScrew1AngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(SimScrew1AngleDegrees), simScrew1AngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private double simScrew2AngleDegrees;

        public double SimScrew2AngleDegrees {
            get => simScrew2AngleDegrees;
            set {
                if (simScrew2AngleDegrees != value) {
                    simScrew2AngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(SimScrew2AngleDegrees), simScrew2AngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private double simScrew3AngleDegrees;

        public double SimScrew3AngleDegrees {
            get => simScrew3AngleDegrees;
            set {
                if (simScrew3AngleDegrees != value) {
                    simScrew3AngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(SimScrew3AngleDegrees), simScrew3AngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private double simScrew4AngleDegrees;

        /// <summary>
        /// Angle of the 4th screw, or <c>double.NaN</c> on a 3-screw adapter (the <see cref="Interfaces.ITiltAdapterOptions"/>
        /// convention). The NaN default forces a NaN-aware guard: <c>NaN != NaN</c> is always true, so the usual
        /// <c>if (field != value)</c> shape would re-persist and raise on every set of the default value.
        /// </summary>
        public double SimScrew4AngleDegrees {
            get => simScrew4AngleDegrees;
            set {
                if (!(double.IsNaN(simScrew4AngleDegrees) && double.IsNaN(value)) && simScrew4AngleDegrees != value) {
                    simScrew4AngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(SimScrew4AngleDegrees), simScrew4AngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private int simScrewInwardCurvatureSign;

        public int SimScrewInwardCurvatureSign {
            get => simScrewInwardCurvatureSign;
            set {
                if (simScrewInwardCurvatureSign != value) {
                    simScrewInwardCurvatureSign = value;
                    optionsAccessor.SetValueInt32(nameof(SimScrewInwardCurvatureSign), simScrewInwardCurvatureSign);
                    RaisePropertyChanged();
                }
            }
        }

        private TiltAdjustmentType simAdjustmentType;

        public TiltAdjustmentType SimAdjustmentType {
            get => simAdjustmentType;
            set {
                if (simAdjustmentType != value) {
                    simAdjustmentType = value;
                    optionsAccessor.SetValueEnum(nameof(SimAdjustmentType), simAdjustmentType);
                    RaisePropertyChanged();
                }
            }
        }

        private double simThreadPitchMicrons;

        public double SimThreadPitchMicrons {
            get => simThreadPitchMicrons;
            set {
                if (simThreadPitchMicrons != value) {
                    simThreadPitchMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(SimThreadPitchMicrons), simThreadPitchMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double simStepperStepSizeMicrons;

        public double SimStepperStepSizeMicrons {
            get => simStepperStepSizeMicrons;
            set {
                if (simStepperStepSizeMicrons != value) {
                    simStepperStepSizeMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(SimStepperStepSizeMicrons), simStepperStepSizeMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double simScrewRadiusMillimeters;

        public double SimScrewRadiusMillimeters {
            get => simScrewRadiusMillimeters;
            set {
                if (simScrewRadiusMillimeters != value) {
                    simScrewRadiusMillimeters = value;
                    optionsAccessor.SetValueDouble(nameof(SimScrewRadiusMillimeters), simScrewRadiusMillimeters);
                    RaisePropertyChanged();
                }
            }
        }

        private bool showSimulatorTiltAdapterPanel;

        public bool ShowSimulatorTiltAdapterPanel {
            get => showSimulatorTiltAdapterPanel;
            set {
                if (showSimulatorTiltAdapterPanel != value) {
                    showSimulatorTiltAdapterPanel = value;
                    optionsAccessor.SetValueBoolean(nameof(ShowSimulatorTiltAdapterPanel), showSimulatorTiltAdapterPanel);
                    RaisePropertyChanged();
                }
            }
        }
    }
}
