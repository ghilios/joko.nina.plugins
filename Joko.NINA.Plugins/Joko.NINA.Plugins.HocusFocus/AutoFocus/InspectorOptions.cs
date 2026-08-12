#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class InspectorOptions : BaseINPC, IInspectorOptions {
        private readonly IPluginOptionsAccessor optionsAccessor;

        public InspectorOptions(IProfileService profileService)
            : this(profileService, CreateDefaultAccessor(profileService)) {
        }

        internal InspectorOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor) {
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            InitializeOptions();
        }

        private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(AutoFocusOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }
            return new PluginOptionsAccessor(profileService, guid.Value);
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            InitializeOptions();
            RaiseAllPropertiesChanged();
        }

        // StepCount is a per-side AF offset-step count: -1 = use the profile's AutoFocus value, real
        // values are small (single/double digits). Neither the setter nor the UI (a plain HintTextBox)
        // constrains it, so 50 is a generous sanity bound used only to catch corrupted stores.
        private const int MaxPlausibleStepCount = 50;

        private void InitializeOptions() {
            stepCount = optionsAccessor.GetValueInt32(nameof(StepCount), -1);
            // Profiles written by builds with the old TimeoutSeconds bug (it persisted under the
            // StepCount key) can hold a timeout value (e.g. 300) here. Reset implausible values to -1
            // and write the correction back so the store is healed once instead of on every load.
            if (stepCount < -1 || stepCount > MaxPlausibleStepCount) {
                Logger.Warning($"InspectorOptions.StepCount loaded an implausible value ({stepCount}) — likely a TimeoutSeconds value written under the StepCount key by an older build. Resetting to -1 (use the profile's AutoFocus value).");
                stepCount = -1;
                optionsAccessor.SetValueInt32(nameof(StepCount), stepCount);
            }
            stepSize = optionsAccessor.GetValueInt32(nameof(StepSize), -1);
            signalAmplification = Math.Max(1, optionsAccessor.GetValueInt32(nameof(SignalAmplification), 2));
            centerFocuserBeforeRun = optionsAccessor.GetValueBoolean(nameof(CenterFocuserBeforeRun), false);
            framesPerPoint = optionsAccessor.GetValueInt32(nameof(FramesPerPoint), -1);
            timeoutSeconds = optionsAccessor.GetValueInt32(nameof(TimeoutSeconds), -1);
            simpleExposureSeconds = optionsAccessor.GetValueDouble(nameof(SimpleExposureSeconds), -1);
            detailedAnalysisExposureSeconds = optionsAccessor.GetValueDouble(nameof(DetailedAnalysisExposureSeconds), -1);
            numRegionsWide = optionsAccessor.GetValueInt32(nameof(NumRegionsWide), 7);
            loopingExposureAnalysisEnabled = optionsAccessor.GetValueBoolean(nameof(LoopingExposureAnalysisEnabled), false);
            micronsPerFocuserStep = optionsAccessor.GetValueDouble(nameof(MicronsPerFocuserStep), -1);
            // Not loaded — it has no accessor key. Assigned to the FIELD, not the property, because the
            // property's setter rejects non-positive values by design. This runs on construction and on every
            // ProfileChanged, which is exactly where "a different rig, so forget the last focuser" belongs.
            driverMicronsPerFocuserStep = -1;
            focuserIncreasesTowardObjective = optionsAccessor.GetValueBoolean(nameof(FocuserIncreasesTowardObjective), false);
            eccentricityColorMapEnabled = optionsAccessor.GetValueBoolean(nameof(EccentricityColorMapEnabled), true);
            mouseOnChartsEnabled = optionsAccessor.GetValueBoolean(nameof(MouseOnChartsEnabled), true);
            sensorCurveModelEnabled = optionsAccessor.GetValueBoolean(nameof(SensorCurveModelEnabled), false);
            showSensorModel = optionsAccessor.GetValueBoolean(nameof(ShowSensorModel), true);
            sensorROI = optionsAccessor.GetValueDouble(nameof(SensorROI), 1.0);
            cornersROI = optionsAccessor.GetValueDouble(nameof(CornersROI), 1.0);
            interpolationAlgo = optionsAccessor.GetValueEnum(nameof(InterpolationAlgo), InterpolationAlgoEnum.MultiQuadric);
            interpolationAmount = optionsAccessor.GetValueEnum(nameof(InterpolationAmount), InterpolationAmountEnum.Medium);
            fixedSensorCenter = optionsAccessor.GetValueBoolean(nameof(FixedSensorCenter), true);
            previousRunBrightnessDiff = optionsAccessor.GetValueDouble(nameof(PreviousRunBrightnessDiff), 0.1d);
            startingBrightnessDiff = optionsAccessor.GetValueDouble(nameof(StartingBrightnessDiff), -1);
            rejectBadBrightnessMatches = optionsAccessor.GetValueBoolean(nameof(RejectBadBrightnessMatches), false);
            rejectBadlyFittingMatches = optionsAccessor.GetValueBoolean(nameof(RejectBadlyFittingMatches), true);
            useRANSAC = optionsAccessor.GetValueBoolean(nameof(UseRANSAC), true);
            useAffineAlignment = optionsAccessor.GetValueBoolean(nameof(UseAffineAlignment), false);
            astigmaticCurvatureEnabled = optionsAccessor.GetValueBoolean(nameof(AstigmaticCurvatureEnabled), false);
            frameReviewEnabled = optionsAccessor.GetValueBoolean(nameof(FrameReviewEnabled), false);
            maxStarsPerRegion = optionsAccessor.GetValueInt32(nameof(MaxStarsPerRegion), -1);
            acceptableRSquaredMin = optionsAccessor.GetValueDouble(nameof(AcceptableRSquaredMin), SensorAberrationCalculator.DefaultAcceptableRSquaredMin);
        }

        public void ResetDefaults() {
            StepCount = -1;
            StepSize = -1;
            SignalAmplification = 2;
            CenterFocuserBeforeRun = false;
            FramesPerPoint = -1;
            TimeoutSeconds = -1;
            SimpleExposureSeconds = -1;
            NumRegionsWide = 7;
            LoopingExposureAnalysisEnabled = false;
            MicronsPerFocuserStep = -1;
            FocuserIncreasesTowardObjective = false;
            EccentricityColorMapEnabled = true;
            MouseOnChartsEnabled = true;
            SensorCurveModelEnabled = false;
            ShowSensorModel = true;
            SensorROI = 1.0;
            CornersROI = 1.0;
            InterpolationAlgo = InterpolationAlgoEnum.MultiQuadric;
            InterpolationAmount = InterpolationAmountEnum.Medium;
            FixedSensorCenter = true;
            previousRunBrightnessDiff = -1;
            startingBrightnessDiff = -1;
            rejectBadBrightnessMatches = false;
            rejectBadlyFittingMatches = true;
            useRANSAC = true;
            useAffineAlignment = false;
            astigmaticCurvatureEnabled = false;
            FrameReviewEnabled = false;
            AcceptableRSquaredMin = SensorAberrationCalculator.DefaultAcceptableRSquaredMin;
        }

        private int stepCount;

        public int StepCount {
            get => stepCount;
            set {
                if (stepCount != value) {
                    stepCount = value;
                    optionsAccessor.SetValueInt32(nameof(StepCount), stepCount);
                    RaisePropertyChanged();
                }
            }
        }

        private int stepSize;

        public int StepSize {
            get => stepSize;
            set {
                if (stepSize != value) {
                    stepSize = value;
                    optionsAccessor.SetValueInt32(nameof(StepSize), stepSize);
                    RaisePropertyChanged();
                }
            }
        }

        // Signal amplification factor for sensor-model / tilt calibration sweeps: divides the focuser step size and
        // multiplies the step count by this factor, so a live run captures more, finer-spaced points over the same
        // range. More points => more signal and smaller defocus jumps between adjacent frames (easier RANSAC
        // alignment). Clamped to >= 1; 1 disables amplification. Applied only to live captures, never on replay.
        private int signalAmplification = 2;

        public int SignalAmplification {
            get => signalAmplification;
            set {
                var clamped = Math.Max(1, value);
                if (signalAmplification != clamped) {
                    signalAmplification = clamped;
                    optionsAccessor.SetValueInt32(nameof(SignalAmplification), signalAmplification);
                    RaisePropertyChanged();
                }
            }
        }

        // When on, a quick standard autofocus is run before each live sensor-model / tilt sweep to center
        // the focuser at best focus, so the sweep brackets focus symmetrically (fewer extreme one-sided defocus
        // frames that fail to align). Off by default — it adds a full AF run to every sweep. No effect on replay.
        private bool centerFocuserBeforeRun = false;

        public bool CenterFocuserBeforeRun {
            get => centerFocuserBeforeRun;
            set {
                if (centerFocuserBeforeRun != value) {
                    centerFocuserBeforeRun = value;
                    optionsAccessor.SetValueBoolean(nameof(CenterFocuserBeforeRun), centerFocuserBeforeRun);
                    RaisePropertyChanged();
                }
            }
        }

        private int framesPerPoint;

        public int FramesPerPoint {
            get => framesPerPoint;
            set {
                if (framesPerPoint != value) {
                    framesPerPoint = value;
                    optionsAccessor.SetValueInt32(nameof(FramesPerPoint), framesPerPoint);
                    RaisePropertyChanged();
                }
            }
        }

        private int timeoutSeconds;

        public int TimeoutSeconds {
            get => timeoutSeconds;
            set {
                if (timeoutSeconds != value) {
                    timeoutSeconds = value;
                    optionsAccessor.SetValueInt32(nameof(TimeoutSeconds), timeoutSeconds);
                    RaisePropertyChanged();
                }
            }
        }

        private double simpleExposureSeconds;

        public double SimpleExposureSeconds {
            get => simpleExposureSeconds;
            set {
                if (simpleExposureSeconds != value) {
                    simpleExposureSeconds = value;
                    optionsAccessor.SetValueDouble(nameof(SimpleExposureSeconds), simpleExposureSeconds);
                    RaisePropertyChanged();
                }
            }
        }

        private double detailedAnalysisExposureSeconds;

        public double DetailedAnalysisExposureSeconds {
            get => detailedAnalysisExposureSeconds;
            set {
                if (detailedAnalysisExposureSeconds != value) {
                    detailedAnalysisExposureSeconds = value;
                    optionsAccessor.SetValueDouble(nameof(DetailedAnalysisExposureSeconds), detailedAnalysisExposureSeconds);
                    RaisePropertyChanged();
                }
            }
        }

        private int numRegionsWide;

        public int NumRegionsWide {
            get => numRegionsWide;
            set {
                if (numRegionsWide != value) {
                    numRegionsWide = value;
                    optionsAccessor.SetValueInt32(nameof(NumRegionsWide), numRegionsWide);
                    RaisePropertyChanged();
                }
            }
        }

        private bool loopingExposureAnalysisEnabled;

        public bool LoopingExposureAnalysisEnabled {
            get => loopingExposureAnalysisEnabled;
            set {
                if (loopingExposureAnalysisEnabled != value) {
                    loopingExposureAnalysisEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(LoopingExposureAnalysisEnabled), loopingExposureAnalysisEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private double micronsPerFocuserStep;

        /// <inheritdoc cref="IInspectorOptions.MicronsPerFocuserStep"/>
        public double MicronsPerFocuserStep {
            get => micronsPerFocuserStep;
            set {
                if (micronsPerFocuserStep != value) {
                    micronsPerFocuserStep = value;
                    optionsAccessor.SetValueDouble(nameof(MicronsPerFocuserStep), micronsPerFocuserStep);
                    RaisePropertyChanged();
                    RaiseFocuserStepSizeDerivedChanged();
                }
            }
        }

        private double driverMicronsPerFocuserStep = -1;

        /// <inheritdoc cref="IInspectorOptions.DriverMicronsPerFocuserStep"/>
        /// <remarks>
        /// No accessor key, by design — see the interface doc. The setter's guard IS the stickiness mechanism:
        /// a disconnected focuser reports 0, the write is dropped, and the last known value stands. Keep the
        /// filtering here and nowhere else, so the single writer stays a plain unconditional assignment and the
        /// two cannot drift apart.
        ///
        /// <para><c>&gt; 0</c> and not <c>!(&lt;= 0)</c>: NaN fails BOTH comparisons, so only the positive form
        /// rejects it. A NaN step size waved through here yields an all-NaN sensor model with no error
        /// anywhere — the same trap documented on
        /// <c>CameraSimulatorOptions.EffectiveFocuserStepSizeMicrons</c>.</para>
        /// </remarks>
        public double DriverMicronsPerFocuserStep {
            get => driverMicronsPerFocuserStep;
            set {
                if (!(value > 0.0) || double.IsInfinity(value)) {
                    return;
                }
                if (driverMicronsPerFocuserStep != value) {
                    driverMicronsPerFocuserStep = value;
                    RaisePropertyChanged();
                    RaiseFocuserStepSizeDerivedChanged();
                }
            }
        }

        /// <inheritdoc cref="IInspectorOptions.EffectiveMicronsPerFocuserStep"/>
        public double EffectiveMicronsPerFocuserStep =>
            micronsPerFocuserStep > 0.0 ? micronsPerFocuserStep
            : driverMicronsPerFocuserStep > 0.0 ? driverMicronsPerFocuserStep
            : -1.0;

        /// <summary>
        /// How far the override may sit from the driver's reported step size before
        /// <see cref="HasFocuserStepSizeMismatch"/> flags it, as a fraction of the driver's value.
        ///
        /// <para>Relative rather than absolute because real rigs span roughly 0.1–10 µm/step. 1% is loose
        /// enough that a genuine calibration landing near the driver's round number stays quiet, and tight
        /// enough to catch what this check exists for: a driver reporting steps rather than microns, or a 2×
        /// error.</para>
        /// </summary>
        public const double FocuserStepSizeMismatchFraction = 0.01;

        /// <inheritdoc cref="IInspectorOptions.HasFocuserStepSizeMismatch"/>
        public bool HasFocuserStepSizeMismatch =>
            micronsPerFocuserStep > 0.0 && driverMicronsPerFocuserStep > 0.0 &&
            Math.Abs(micronsPerFocuserStep - driverMicronsPerFocuserStep) / driverMicronsPerFocuserStep
                > FocuserStepSizeMismatchFraction;

        // Both derived values read the override AND the driver value, so either input changing must re-raise
        // both. Bindings (the hint text, the mismatch flag) depend on this.
        private void RaiseFocuserStepSizeDerivedChanged() {
            RaisePropertyChanged(nameof(EffectiveMicronsPerFocuserStep));
            RaisePropertyChanged(nameof(HasFocuserStepSizeMismatch));
        }

        private bool focuserIncreasesTowardObjective;

        /// <summary>
        /// The focuser direction convention k. DISPLAY-ONLY — see
        /// <see cref="IInspectorOptions.FocuserIncreasesTowardObjective"/> for the full contract and the two
        /// sanctioned exceptions.
        /// </summary>
        public bool FocuserIncreasesTowardObjective {
            get => focuserIncreasesTowardObjective;
            set {
                if (focuserIncreasesTowardObjective != value) {
                    focuserIncreasesTowardObjective = value;
                    optionsAccessor.SetValueBoolean(nameof(FocuserIncreasesTowardObjective), focuserIncreasesTowardObjective);
                    RaisePropertyChanged();
                }
            }
        }

        private bool eccentricityColorMapEnabled;

        public bool EccentricityColorMapEnabled {
            get => eccentricityColorMapEnabled;
            set {
                if (eccentricityColorMapEnabled != value) {
                    eccentricityColorMapEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(EccentricityColorMapEnabled), eccentricityColorMapEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private bool mouseOnChartsEnabled;

        public bool MouseOnChartsEnabled {
            get => mouseOnChartsEnabled;
            set {
                if (mouseOnChartsEnabled != value) {
                    mouseOnChartsEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(MouseOnChartsEnabled), mouseOnChartsEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private bool sensorCurveModelEnabled;

        public bool SensorCurveModelEnabled {
            get => sensorCurveModelEnabled;
            set {
                if (sensorCurveModelEnabled != value) {
                    sensorCurveModelEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(SensorCurveModelEnabled), sensorCurveModelEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private bool showSensorModel;

        public bool ShowSensorModel {
            get => showSensorModel;
            set {
                if (showSensorModel != value) {
                    showSensorModel = value;
                    optionsAccessor.SetValueBoolean(nameof(ShowSensorModel), showSensorModel);
                    RaisePropertyChanged();
                }
            }
        }

        private double sensorROI;

        public double SensorROI {
            get => sensorROI;
            set {
                if (sensorROI != value) {
                    if (value < 0.1) {
                        sensorROI = 0.1;
                    } else if (value > 1.0 || double.IsNaN(value)) {
                        sensorROI = 1.0;
                    } else {
                        sensorROI = value;
                    }

                    optionsAccessor.SetValueDouble(nameof(SensorROI), sensorROI);
                    RaisePropertyChanged();
                }
            }
        }

        private double cornersROI;

        public double CornersROI {
            get => cornersROI;
            set {
                if (cornersROI != value) {
                    if (value < 0.1) {
                        cornersROI = 0.1;
                    } else if (value > 1.0 || double.IsNaN(value)) {
                        cornersROI = 1.0;
                    } else {
                        cornersROI = value;
                    }

                    optionsAccessor.SetValueDouble(nameof(CornersROI), cornersROI);
                    RaisePropertyChanged();
                }
            }
        }

        public bool InterpolationEnabled { get; private set; } = false;

        /*
        private bool interpolationEnabled;

        public bool InterpolationEnabled {
            get => interpolationEnabled;
            set {
                if (interpolationEnabled != value) {
                    interpolationEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(InterpolationEnabled), interpolationEnabled);
                    RaisePropertyChanged();
                }
            }
        }
        */

        private InterpolationAlgoEnum interpolationAlgo;

        public InterpolationAlgoEnum InterpolationAlgo {
            get => interpolationAlgo;
            set {
                if (interpolationAlgo != value) {
                    interpolationAlgo = value;
                    optionsAccessor.SetValueEnum(nameof(InterpolationAlgo), value);
                    RaisePropertyChanged();
                }
            }
        }

        private InterpolationAmountEnum interpolationAmount;

        public InterpolationAmountEnum InterpolationAmount {
            get => interpolationAmount;
            set {
                if (interpolationAmount != value) {
                    interpolationAmount = value;
                    optionsAccessor.SetValueEnum(nameof(InterpolationAmount), value);
                    RaisePropertyChanged();
                }
            }
        }

        private bool fixedSensorCenter;

        public bool FixedSensorCenter {
            get => fixedSensorCenter;
            set {
                if (fixedSensorCenter != value) {
                    fixedSensorCenter = value;
                    optionsAccessor.SetValueBoolean(nameof(FixedSensorCenter), fixedSensorCenter);
                    RaisePropertyChanged();
                }
            }
        }

        private bool useRANSAC = true;

        public bool UseRANSAC {
            get => useRANSAC;
            set {
                if (useRANSAC != value) {
                    useRANSAC = value;
                    optionsAccessor.SetValueBoolean(nameof(UseRANSAC), useRANSAC);
                    RaisePropertyChanged();
                }
            }
        }

        private bool useAffineAlignment = false;

        public bool UseAffineAlignment {
            get => useAffineAlignment;
            set {
                if (useAffineAlignment != value) {
                    useAffineAlignment = value;
                    optionsAccessor.SetValueBoolean(nameof(UseAffineAlignment), useAffineAlignment);
                    RaisePropertyChanged();
                }
            }
        }

        private bool astigmaticCurvatureEnabled = false;

        public bool AstigmaticCurvatureEnabled {
            get => astigmaticCurvatureEnabled;
            set {
                if (astigmaticCurvatureEnabled != value) {
                    astigmaticCurvatureEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(AstigmaticCurvatureEnabled), astigmaticCurvatureEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private bool rejectBadBrightnessMatches = false;

        public bool RejectBadBrightnessMatches {
            get => rejectBadBrightnessMatches;
            set {
                if (rejectBadBrightnessMatches != value) {
                    rejectBadBrightnessMatches = value;
                    optionsAccessor.SetValueBoolean(nameof(RejectBadBrightnessMatches), rejectBadBrightnessMatches);
                    RaisePropertyChanged();
                }
            }
        }

        private bool rejectBadlyFittingMatches = true;

        public bool RejectBadlyFittingMatches {
            get => rejectBadlyFittingMatches;
            set {
                if (rejectBadlyFittingMatches != value) {
                    rejectBadlyFittingMatches = value;
                    optionsAccessor.SetValueBoolean(nameof(RejectBadlyFittingMatches), rejectBadlyFittingMatches);
                    RaisePropertyChanged();
                }
            }
        }

        private double previousRunBrightnessDiff;

        public double PreviousRunBrightnessDiff {
            get => previousRunBrightnessDiff;
            set {
                if (previousRunBrightnessDiff != value) {
                    previousRunBrightnessDiff = value;
                    optionsAccessor.SetValueDouble(nameof(PreviousRunBrightnessDiff), previousRunBrightnessDiff);
                    RaisePropertyChanged();
                    RaisePropertyChanged("BrightnessToleranceHint");
                }
            }
        }

        private double startingBrightnessDiff;  // if this is -1, it means "auto" and previous run brightness diff is used as the starting point

        public double StartingBrightnessDiff {
            get => startingBrightnessDiff;
            set {
                if (startingBrightnessDiff != value) {
                    startingBrightnessDiff = value;
                    optionsAccessor.SetValueDouble(nameof(StartingBrightnessDiff), startingBrightnessDiff);
                    RaisePropertyChanged();
                }
            }
        }

        public string BrightnessToleranceHint { get { return $"(auto: {PreviousRunBrightnessDiff:0.##})"; } }

        private bool frameReviewEnabled = false;

        public bool FrameReviewEnabled {
            get => frameReviewEnabled;
            set {
                if (frameReviewEnabled != value) {
                    frameReviewEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(FrameReviewEnabled), frameReviewEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        public string MaxStarsPerRegionHint { get { return $"(unlimited)"; } }
        private int maxStarsPerRegion = -1;

        public int MaxStarsPerRegion {
            get => maxStarsPerRegion;
            set {
                if (maxStarsPerRegion != value) {
                    maxStarsPerRegion = value;
                    optionsAccessor.SetValueInt32(nameof(MaxStarsPerRegion), maxStarsPerRegion);
                    RaisePropertyChanged();
                }
            }
        }

        private double acceptableRSquaredMin;

        public double AcceptableRSquaredMin {
            get => acceptableRSquaredMin;
            set {
                if (acceptableRSquaredMin != value) {
                    acceptableRSquaredMin = value;
                    optionsAccessor.SetValueDouble(nameof(AcceptableRSquaredMin), acceptableRSquaredMin);
                    RaisePropertyChanged();
                }
            }
        }
    }
}