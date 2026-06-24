#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.Input;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Logger = NINA.Core.Utility.Logger;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// The discrete calibration measurement steps. Each step is a single physical screw move followed by one
    /// measurement, so two changes are never compounded between measurements: the all-inward and per-screw moves
    /// are each bracketed by an explicit re-baseline. Screw angles are derived from the move relative to the
    /// re-baseline that immediately precedes it (c→d for screw 1, e→f for screw 2).
    /// </summary>
    public enum WizardStep {
        Baseline = 0,     // a
        AllInward = 1,    // b: all screws inward once (curvature/backfocus sign via a→b)
        ReBaseline1 = 2,  // c: all screws back out once (≈ baseline)
        Screw1 = 3,       // d: screw 1 inward once (4-screw: + screw 3 outward)
        ReBaseline2 = 4,  // e: undo the screw-1 move (≈ c)
        Screw2 = 5,       // f: screw 2 inward once (4-screw: + screw 4 outward)
        Complete = 6
    }

    [PartCreationPolicy(CreationPolicy.Shared)]
    [Export(typeof(IDockableVM))]
    [Export]
    public class TiltAdapterWizardVM : DockableVM, ICameraConsumer, IFocuserConsumer {

        private readonly ITiltAdapterOptions tiltAdapterOptions;
        private readonly InspectorVM inspector;
        private readonly IApplicationDispatcher applicationDispatcher;
        // WindowServiceFactory is not a MEF export (NINA exposes the concrete type with a default ctor only), so it is
        // constructed directly, matching InspectorVM / HocusFocusVM. Used to show the shared replay-settings modal.
        private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();
        private readonly IProgress<ApplicationStatus> progress;

        private CameraInfo cameraInfo = DeviceInfo.CreateDefaultInstance<CameraInfo>();
        private FocuserInfo focuserInfo = DeviceInfo.CreateDefaultInstance<FocuserInfo>();

        private WizardStep currentStep = WizardStep.Baseline;
        private bool isWizardRunning = false;
        private bool isMeasuring = false;
        private bool isReplaying = false;
        private bool hasWarning = false;
        private string warningText = string.Empty;
        private string statusText = string.Empty;
        private bool hasMeasurementConsistencyWarning = false;
        private string measurementConsistencyWarningText = string.Empty;
        private bool hasRebaselineDriftWarning = false;
        private string rebaselineDriftWarningText = string.Empty;

        // One (A, B, mean) tilt-plane reading plus the per-step field-curvature characterization, keyed by step.
        private readonly Dictionary<WizardStep, StepReading> stepReadings = new Dictionary<WizardStep, StepReading>();
        private CancellationTokenSource measureCts;

        private double calibrationAppliedAmount = 1.0;
        private double measuredHardwareMicrons = double.NaN;
        private double calibrationPixelSizeMicrons;
        private double calibrationFocuserStepMicrons;
        private double calibrationScrewRadiusMm;
        private double lastRawAngleDiff = double.NaN;
        private double lastMoveMagnitudeRatio = double.NaN;

        // Per-run save state (transient; only populated while saving AF runs for the current calibration run).
        private bool saveAFRuns;
        private string runRootFolder;
        private string metadataPath;
        private TiltCalibrationMetadata currentMetadata;

        private const double MeasurementConsistencyWarningThreshold = 0.02;
        // A re-baseline that returns close to the prior state drifts ~0; warn once the residual reaches half the
        // screw-move signal (backlash / uneven undo would corrupt the recovered angle/hardware for that screw).
        private const double RebaselineDriftWarnThreshold = 0.5;

        // The six measurement steps in capture order.
        private static readonly WizardStep[] MeasurementSteps = {
            WizardStep.Baseline, WizardStep.AllInward, WizardStep.ReBaseline1,
            WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2
        };

        private struct StepReading {
            public double A;
            public double B;
            public double Mean;
            public double TiltAngleDeg;
            public double DirectionDeg;
            public double CurvatureRadiusMm;
            public double CurvatureEffectAtScrewRadiusMicrons;
            public string SaveFolder;
        }

        [ImportingConstructor]
        public TiltAdapterWizardVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            InspectorVM inspector)
            : this(profileService, applicationStatusMediator, cameraMediator, focuserMediator, inspector,
                   HocusFocusPlugin.ApplicationDispatcher, HocusFocusPlugin.TiltAdapterOptions) { }

        public TiltAdapterWizardVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            InspectorVM inspector,
            IApplicationDispatcher applicationDispatcher,
            ITiltAdapterOptions tiltAdapterOptions)
            : base(profileService) {
            this.inspector = inspector;
            this.tiltAdapterOptions = tiltAdapterOptions;
            this.applicationDispatcher = applicationDispatcher;
            this.progress = ProgressFactory.Create(applicationStatusMediator, "Tilt Adapter Wizard");

            this.Title = "Tilt Adapter Wizard";

            var dict = new System.Windows.ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/TiltAdapterWizard/DataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["TiltAdapterWizardSVG"];
            ImageGeometry.Freeze();

            ScrewDiagramItems = new ObservableCollection<TiltScrewDiagramItem>();
            ScrewConnectionLines = new ObservableCollection<TiltScrewConnectionLine>();
            StepMeasurementSummary = new ObservableCollection<TiltMeasurementSummaryRow>();
            StepMeasurementSummary.CollectionChanged += OnSummaryCollectionChanged;

            StartCommand = new AsyncRelayCommand(StartAsync, () => !IsWizardRunning);
            RunMeasurementCommand = new AsyncRelayCommand(RunMeasurementAsync, () => IsOnMeasurementStep && !IsMeasuring && AreDevicesConnected);
            UseSavedAFCommand = new AsyncRelayCommand(RunSavedMeasurementAsync, () => IsOnMeasurementStep && !IsMeasuring);
            CancelCommand = new RelayCommand(CancelMeasurement, () => IsMeasuring);
            RestartCommand = new RelayCommand(Restart);
            UseMeasuredHardwareCommand = new RelayCommand(UseMeasuredHardware, () => HasMeasuredHardware);
            BrowseSaveFolderCommand = new RelayCommand(BrowseSaveFolder);
            ReplayCommand = new AsyncRelayCommand(ReplayAsync, () => !IsWizardRunning && !IsMeasuring);

            tiltAdapterOptions.PropertyChanged += (s, e) => OnUIThread(() => {
                if (e.PropertyName == nameof(ITiltAdapterOptions.ScrewInwardCurvatureSign)) {
                    RaisePropertyChanged(nameof(HasCurvatureCalibration));
                    RaisePropertyChanged(nameof(CurvatureSignDescription));
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.ScrewCount) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.IsCalibrated) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.CalibratedScrewCount)) {
                    RaisePropertyChanged(nameof(IsCalibrationValid));
                    RebuildDiagram();
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.MeasurementAverageCount)) {
                    RaisePropertyChanged(nameof(ShowRunColumn));
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.DeviceName)) {
                    RaisePropertyChanged(nameof(SelectedDevice));
                    RaisePropertyChanged(nameof(IsManualDevice));
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.SaveAFRunsPath)) {
                    RaisePropertyChanged(nameof(SaveAFRunsPath));
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.AdjustmentType) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.ThreadPitchMicrons) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.StepperStepSizeMicrons) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.ScrewRadiusMillimeters)) {
                    RaisePropertyChanged(nameof(IsStepperAdjustment));
                    RaisePropertyChanged(nameof(CalibrationAmountLabel));
                    RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));
                    RaisePropertyChanged(nameof(AdjustmentType));
                    RaisePropertyChanged(nameof(ThreadPitchMicronsValue));
                    RaisePropertyChanged(nameof(StepperStepSizeMicronsValue));
                    RaisePropertyChanged(nameof(ScrewRadiusMillimetersValue));
                    RaiseHardwareSummaryChanged();
                }
            });

            profileService.ProfileChanged += (s, e) => OnUIThread(() => {
                RaisePropertyChanged(nameof(PixelSizeMicronsValue));
                RaisePropertyChanged(nameof(FocuserStepSizeMicronsValue));
                RaisePropertyChanged(nameof(SelectedDevice));
                RaisePropertyChanged(nameof(IsManualDevice));
                RaisePropertyChanged(nameof(SaveAFRunsPath));
            });

            // Re-assert and lock a persisted device preset on load.
            ApplyDevice(tiltAdapterOptions.DeviceName);

            RebuildDiagram();

            cameraMediator.RegisterConsumer(this);
            focuserMediator.RegisterConsumer(this);
        }

        private void OnSummaryCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            // Mutations are marshaled via AddSummaryRow/ClearSummaryRows, so this handler already runs on the UI
            // thread; the wrap stays as a defensive no-op fast path (F16).
            OnUIThread(() => {
                RaisePropertyChanged(nameof(HasMeasurementFeedback));
                RaisePropertyChanged(nameof(HasMeasurementResults));
            });
        }

        // F16: WPF raises CollectionChanged synchronously at the mutation site, so the .Add/.Clear themselves — not
        // just the resulting notification — must run on the UI thread. All StepMeasurementSummary mutations go
        // through these helpers so off-thread callers (e.g. a future ConfigureAwait(false) resume) stay safe.
        private void AddSummaryRow(TiltMeasurementSummaryRow row) => OnUIThread(() => StepMeasurementSummary.Add(row));

        private void ClearSummaryRows() => OnUIThread(StepMeasurementSummary.Clear);

        public ITiltAdapterOptions TiltAdapterOptions => tiltAdapterOptions;
        public InspectorVM Inspector => inspector;

        public ObservableCollection<TiltScrewDiagramItem> ScrewDiagramItems { get; }
        public ObservableCollection<TiltScrewConnectionLine> ScrewConnectionLines { get; }
        public ObservableCollection<TiltMeasurementSummaryRow> StepMeasurementSummary { get; }

        public bool IsWizardRunning {
            get => isWizardRunning;
            private set {
                isWizardRunning = value;
                RaisePropertyChanged();
                NotifyCommandsCanExecuteChanged();
            }
        }

        public WizardStep CurrentStep {
            get => currentStep;
            private set {
                currentStep = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsComplete));
                RaisePropertyChanged(nameof(IsOnMeasurementStep));
                RaisePropertyChanged(nameof(StepInstructions));
                NotifyCommandsCanExecuteChanged();
            }
        }

        public bool IsComplete => currentStep == WizardStep.Complete;

        // Steps that end with running the aberration inspector (measurement auto-advances)
        public bool IsOnMeasurementStep => MeasurementSteps.Contains(currentStep);

        public bool IsCalibrationValid =>
            tiltAdapterOptions.IsCalibrated &&
            tiltAdapterOptions.ScrewCount == tiltAdapterOptions.CalibratedScrewCount;

        public bool HasCurvatureCalibration => tiltAdapterOptions.ScrewInwardCurvatureSign != 0;

        public bool AreDevicesConnected => cameraInfo.Connected && focuserInfo.Connected;

        public string ConnectionWarningText {
            get {
                if (!cameraInfo.Connected && !focuserInfo.Connected) return "Camera and focuser are not connected.";
                if (!cameraInfo.Connected) return "Camera is not connected.";
                if (!focuserInfo.Connected) return "Focuser is not connected.";
                return string.Empty;
            }
        }

        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                cameraInfo = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AreDevicesConnected));
                RaisePropertyChanged(nameof(ConnectionWarningText));
                RaisePropertyChanged(nameof(PixelSizeMicronsValue));
                NotifyCommandsCanExecuteChangedCore();
            }
        }

        public FocuserInfo FocuserInfo {
            get => focuserInfo;
            private set {
                focuserInfo = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AreDevicesConnected));
                RaisePropertyChanged(nameof(ConnectionWarningText));
                NotifyCommandsCanExecuteChangedCore();
            }
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            // Marshal the whole update (state mutation + notifications) once at the consumer boundary so individual
            // setters need not each remember to wrap, and the high-frequency background broadcast is not blocked (F15).
            OnUIThread(() => CameraInfo = deviceInfo);
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            OnUIThread(() => FocuserInfo = deviceInfo);
        }

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            // Do nothing
        }

        public void UpdateUserFocused(FocuserInfo info) {
            // Do nothing
        }

        public void Dispose() {
            // Do nothing
        }

        public string CurvatureSignDescription =>
            tiltAdapterOptions.ScrewInwardCurvatureSign == 1 ? "↑" :
            tiltAdapterOptions.ScrewInwardCurvatureSign == -1 ? "↓" :
            string.Empty;

        public bool IsMeasuring {
            get => isMeasuring;
            private set {
                isMeasuring = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasMeasurementFeedback));
                RaisePropertyChanged(nameof(HasMeasurementResults));
                NotifyCommandsCanExecuteChanged();
            }
        }

        public bool HasMeasurementFeedback => isMeasuring || StepMeasurementSummary.Count > 0;

        public bool HasMeasurementResults => !isMeasuring && StepMeasurementSummary.Count > 0;

        public bool ShowRunColumn => tiltAdapterOptions.MeasurementAverageCount > 1;

        public bool HasWarning {
            get => hasWarning;
            private set {
                hasWarning = value;
                RaisePropertyChanged();
            }
        }

        public string WarningText {
            get => warningText;
            private set {
                warningText = value;
                RaisePropertyChanged();
            }
        }

        public string StatusText {
            get => statusText;
            private set {
                statusText = value;
                RaisePropertyChanged();
            }
        }

        public bool HasMeasurementConsistencyWarning {
            get => hasMeasurementConsistencyWarning;
            private set {
                hasMeasurementConsistencyWarning = value;
                RaisePropertyChanged();
            }
        }

        public string MeasurementConsistencyWarningText {
            get => measurementConsistencyWarningText;
            private set {
                measurementConsistencyWarningText = value;
                RaisePropertyChanged();
            }
        }

        public bool HasRebaselineDriftWarning {
            get => hasRebaselineDriftWarning;
            private set {
                hasRebaselineDriftWarning = value;
                RaisePropertyChanged();
            }
        }

        public string RebaselineDriftWarningText {
            get => rebaselineDriftWarningText;
            private set {
                rebaselineDriftWarningText = value;
                RaisePropertyChanged();
            }
        }

        // Transient per-run toggle: save each calibration step's AutoFocus sweep so the run can be replayed.
        // Always starts OFF and must be explicitly enabled before each run (not persisted). The folder is
        // persisted (SaveAFRunsPath) so the location is reused.
        public bool SaveAFRuns {
            get => saveAFRuns;
            set {
                if (saveAFRuns != value) {
                    saveAFRuns = value;
                    RaisePropertyChanged();
                }
            }
        }

        // Enabling saving immediately prompts for the folder (one click). Invoked from the view's CheckBox.Checked
        // so the property setter stays free of UI side effects (unit-testable). No-op if a folder is already set.
        public void PromptForSaveFolderIfNeeded() {
            if (saveAFRuns && string.IsNullOrWhiteSpace(SaveAFRunsPath)) {
                BrowseSaveFolder();
            }
        }

        public string SaveAFRunsPath {
            get => tiltAdapterOptions.SaveAFRunsPath;
            set {
                if (tiltAdapterOptions.SaveAFRunsPath != value) {
                    tiltAdapterOptions.SaveAFRunsPath = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string StepInstructions {
            get {
                int n = tiltAdapterOptions.ScrewCount;
                switch (currentStep) {
                    case WizardStep.Baseline:
                        return "Ensure all screws are at their starting position, then click Run Measurement to take a baseline reading.";
                    case WizardStep.AllInward:
                        return "Label your screws 1, 2, and 3 (or 1–4 for a 4-screw adapter) in a consistent clockwise order. " +
                            "Screw 1 does NOT need to be at any particular clock position — the wizard determines each screw's actual " +
                            "position from the measurements.\n\nTurn ALL screws INWARD exactly 1 full turn each, then click Run Measurement.";
                    case WizardStep.ReBaseline1:
                        return "Turn ALL screws back OUT exactly 1 full turn each, returning to the baseline position, then click Run Measurement.";
                    case WizardStep.Screw1:
                        return n == 3
                            ? "Turn screw 1 INWARD exactly 1 full turn, then click Run Measurement."
                            : "Turn screw 1 INWARD and screw 3 OUTWARD exactly 1 full turn each, then click Run Measurement.";
                    case WizardStep.ReBaseline2:
                        return n == 3
                            ? "Turn screw 1 back OUT exactly 1 full turn, returning to the baseline position, then click Run Measurement."
                            : "Turn screw 1 back OUT and screw 3 back IN exactly 1 full turn each, returning to the baseline position, then click Run Measurement.";
                    case WizardStep.Screw2:
                        return n == 3
                            ? "Turn screw 2 INWARD exactly 1 full turn, then click Run Measurement."
                            : "Turn screw 2 INWARD and screw 4 OUTWARD exactly 1 full turn each, then click Run Measurement.";
                    case WizardStep.Complete:
                        return "Restore all screws to their original position.";
                    default:
                        return string.Empty;
                }
            }
        }

        public ICommand StartCommand { get; }
        public ICommand RunMeasurementCommand { get; }
        public ICommand UseSavedAFCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RestartCommand { get; }
        public ICommand UseMeasuredHardwareCommand { get; }
        public ICommand BrowseSaveFolderCommand { get; }
        public ICommand ReplayCommand { get; }

        // Known amount the user moves each screw during the per-screw calibration steps (full turns
        // for screws, steps for steppers). Defaults to 1.0 to match the "1 full turn" instructions.
        public double CalibrationAppliedAmount {
            get => calibrationAppliedAmount;
            set {
                if (calibrationAppliedAmount != value) {
                    calibrationAppliedAmount = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));
                }
            }
        }

        public bool IsStepperAdjustment => tiltAdapterOptions.AdjustmentType == TiltAdjustmentType.StepperMotors;

        public string CalibrationAmountLabel => IsStepperAdjustment ? "Steps applied per screw" : "Turns applied per screw";

        public bool HasMeasuredHardware => !double.IsNaN(measuredHardwareMicrons) && measuredHardwareMicrons > 0;

        public string MeasuredHardwareDisplay =>
            !HasMeasuredHardware ? "—"
            : IsStepperAdjustment ? $"{measuredHardwareMicrons:0.###} µm/step"
            : $"{measuredHardwareMicrons:0.#} µm/turn";

        public string SavedHardwareDisplay {
            get {
                double saved = IsStepperAdjustment ? tiltAdapterOptions.StepperStepSizeMicrons : tiltAdapterOptions.ThreadPitchMicrons;
                if (saved <= 0) return "not set";
                return IsStepperAdjustment ? $"{saved:0.###} µm/step" : $"{saved:0.#} µm/turn";
            }
        }

        public string HardwareDeltaDisplay {
            get {
                double saved = IsStepperAdjustment ? tiltAdapterOptions.StepperStepSizeMicrons : tiltAdapterOptions.ThreadPitchMicrons;
                if (!HasMeasuredHardware || saved <= 0) return string.Empty;
                double pct = (measuredHardwareMicrons - saved) / saved * 100.0;
                return $"{pct:+0.#;-0.#;0}% vs saved";
            }
        }

        public string CalibrationPixelSizeDisplay => calibrationPixelSizeMicrons > 0 ? $"{calibrationPixelSizeMicrons:0.##} µm" : "—";
        public string CalibrationFocuserStepDisplay => calibrationFocuserStepMicrons > 0 ? $"{calibrationFocuserStepMicrons:0.###} µm" : "—";
        public string CalibrationScrewRadiusDisplay => calibrationScrewRadiusMm > 0 ? $"{calibrationScrewRadiusMm:0.##} mm" : "not set";
        public string CalibrationAppliedAmountDisplay => IsStepperAdjustment ? $"{calibrationAppliedAmount:0.##} steps" : $"{calibrationAppliedAmount:0.##} turns";

        // Config-panel bindings. They wrap the persisted options, presenting thread pitch in mm and
        // showing 0 for the unset (-1) sentinel so the textboxes read cleanly.
        public TiltAdjustmentType AdjustmentType {
            get => tiltAdapterOptions.AdjustmentType;
            set {
                if (tiltAdapterOptions.AdjustmentType != value) {
                    tiltAdapterOptions.AdjustmentType = value;
                    RaisePropertyChanged();
                }
            }
        }

        public double ThreadPitchMicronsValue {
            get { var um = tiltAdapterOptions.ThreadPitchMicrons; return um > 0 ? um : 0; }
            set {
                tiltAdapterOptions.ThreadPitchMicrons = value > 0 ? value : -1;
                RaisePropertyChanged();
            }
        }

        public double StepperStepSizeMicronsValue {
            get { var v = tiltAdapterOptions.StepperStepSizeMicrons; return v > 0 ? v : 0; }
            set {
                tiltAdapterOptions.StepperStepSizeMicrons = value > 0 ? value : -1;
                RaisePropertyChanged();
            }
        }

        public double ScrewRadiusMillimetersValue {
            get { var v = tiltAdapterOptions.ScrewRadiusMillimeters; return v > 0 ? v : 0; }
            set {
                tiltAdapterOptions.ScrewRadiusMillimeters = value > 0 ? value : -1;
                RaisePropertyChanged();
            }
        }

        // Device presets. Selecting a non-Manual device fills and locks the hardware fields.
        public IReadOnlyList<string> DeviceNames => TiltAdapterDevicePreset.All.Select(p => p.Name).ToList();

        public string SelectedDevice {
            get => tiltAdapterOptions.DeviceName;
            set => ApplyDevice(value);
        }

        public bool IsManualDevice => TiltAdapterDevicePreset.ByName(tiltAdapterOptions.DeviceName).IsManual;

        // Inputs that feed the screw-turn calculation, wired to their source of truth: pixel size to
        // the active NINA camera profile, focuser step size to the Inspector's MicronsPerFocuserStep.
        public double PixelSizeMicronsValue {
            get => profileService.ActiveProfile.CameraSettings.PixelSize;
            set {
                if (profileService.ActiveProfile.CameraSettings.PixelSize != value) {
                    profileService.ActiveProfile.CameraSettings.PixelSize = value;
                    RaisePropertyChanged();
                }
            }
        }

        public double FocuserStepSizeMicronsValue {
            get {
                var v = inspector.InspectorOptions?.MicronsPerFocuserStep ?? -1;
                return v > 0 ? v : 0;
            }
            set {
                if (inspector.InspectorOptions != null) {
                    inspector.InspectorOptions.MicronsPerFocuserStep = value > 0 ? value : -1;
                    RaisePropertyChanged();
                }
            }
        }

        private Task StartAsync() {
            StatusText = string.Empty;
            stepReadings.Clear();
            HasRebaselineDriftWarning = false;
            RebaselineDriftWarningText = string.Empty;
            HasWarning = false;
            WarningText = string.Empty;
            ClearSummaryRows();
            runRootFolder = null;
            metadataPath = null;
            currentMetadata = null;

            if (saveAFRuns) {
                if (!SetUpSaveRun()) {
                    // Could not set up the save folder; continue without saving.
                    SaveAFRuns = false;
                }
            }

            CurrentStep = WizardStep.Baseline;
            IsWizardRunning = true;
            return Task.CompletedTask;
        }

        // Creates the per-run root folder and captures the live star-detection settings into the metadata so the
        // run can be replayed later. Returns false (and warns) if the save folder is unset / cannot be created.
        private bool SetUpSaveRun() {
            if (string.IsNullOrWhiteSpace(SaveAFRunsPath)) {
                Notification.ShowWarning("Choose a folder to save AutoFocus runs, or turn off saving.");
                return false;
            }
            try {
                runRootFolder = Path.Combine(SaveAFRunsPath, "TiltCalibration_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(runRootFolder);
            } catch (Exception ex) {
                Notification.ShowError($"Could not create the AutoFocus save folder: {ex.Message}");
                Logger.Error(ex, "Failed to create tilt calibration save folder");
                return false;
            }
            metadataPath = Path.Combine(runRootFolder, "metadata.json");
            currentMetadata = BuildInitialMetadata();
            WriteMetadata();
            return true;
        }

        private TiltCalibrationMetadata BuildInitialMetadata() {
            return new TiltCalibrationMetadata {
                NumberOfScrews = tiltAdapterOptions.ScrewCount,
                AdjustmentType = IsStepperAdjustment ? "StepperMotors" : "Screws",
                ScrewThreadPitchMicrons = tiltAdapterOptions.ThreadPitchMicrons,
                StepperStepSizeMicrons = tiltAdapterOptions.StepperStepSizeMicrons,
                ScrewRadiusMillimeters = tiltAdapterOptions.ScrewRadiusMillimeters,
                PixelSizeMicrons = profileService.ActiveProfile.CameraSettings.PixelSize,
                FocuserStepSizeMicrons = EffectiveFocuserStepMicrons(),
                CalibrationAppliedAmount = calibrationAppliedAmount,
                MeasurementAverageCount = Math.Max(1, tiltAdapterOptions.MeasurementAverageCount),
                OptimizedStarDetectionSettings = CaptureDetectionSettings(),
                RunStepMapping = new List<TiltRunStepMapping>(),
                PerStep = new List<TiltPerStepResult>()
            };
        }

        // Capture the effective live star-detection params as a snapshot DTO. BuildStarDetectorParams is the same
        // source the runtime AF detection path uses; the optimization-only J/step args are inert metadata.
        private static OptimizedStarDetectionSettings CaptureDetectionSettings() {
            var p = HocusFocusStarDetection.BuildStarDetectorParams(HocusFocusPlugin.StarDetectionOptions);
            return OptimizedStarDetectionSettings.FromParams(p, runCount: 0, baselineJ: 0.0, finalJ: 0.0,
                recommendedStepSize: 0, recommendedOffsetSteps: 0);
        }

        private double EffectiveFocuserStepMicrons() {
            var v = inspector.InspectorOptions?.MicronsPerFocuserStep ?? -1;
            if (v > 0) return v;
            return focuserInfo.StepSize > 0 ? focuserInfo.StepSize : -1;
        }

        private async Task RunMeasurementAsync() {
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;

            measureCts?.Dispose();
            measureCts = new CancellationTokenSource();
            var token = measureCts.Token;
            IsMeasuring = true;

            try {
                await MeasureStep(currentStep, token, fromSaved: false);
            } catch (OperationCanceledException) {
                StatusText = "Measurement cancelled.";
            } finally {
                IsMeasuring = false;
            }
        }

        private async Task RunSavedMeasurementAsync() {
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;

            measureCts?.Dispose();
            measureCts = new CancellationTokenSource();
            var token = measureCts.Token;

            try {
                await MeasureStep(currentStep, token, fromSaved: true);
            } catch (OperationCanceledException) {
                StatusText = "Measurement cancelled.";
            } finally {
                IsMeasuring = false;
            }
        }

        private async Task MeasureStep(WizardStep step, CancellationToken token, bool fromSaved) {
            if (step == WizardStep.Baseline) {
                ClearSummaryRows();
            }
            var reading = await RunAveragedMeasurement(token, step, StepDescription(step), fromSaved);
            if (reading == null) {
                StatusText = "Measurement failed.";
                return;
            }
            stepReadings[step] = reading.Value;
            RecordStepIntoMetadata(step, reading.Value);
            NextStep();
        }

        // Description shown on each summary row, reflecting the single move performed for the step.
        private string StepDescription(WizardStep step) {
            bool four = tiltAdapterOptions.ScrewCount == 4;
            switch (step) {
                case WizardStep.Baseline: return "Baseline";
                case WizardStep.AllInward: return "All screws ↓";
                case WizardStep.ReBaseline1: return "Re-baseline (all ↑)";
                case WizardStep.Screw1: return four ? "Screw 1 ↓, Screw 3 ↑" : "Screw 1 ↓";
                case WizardStep.ReBaseline2: return four ? "Re-baseline (Screw 1 ↑, Screw 3 ↓)" : "Re-baseline (Screw 1 ↑)";
                case WizardStep.Screw2: return four ? "Screw 2 ↓, Screw 4 ↑" : "Screw 2 ↓";
                default: return step.ToString();
            }
        }

        private static string StepFolderName(WizardStep step) => $"{(int)step + 1:00}_{step}";

        // Runs the aberration inspector MeasurementAverageCount times, averages the tilt plane, appends summary
        // rows + the consistency warning, and captures the per-step field-curvature characterization. When saving
        // (live only), each step's run is redirected into its own folder and the saved location is recorded.
        private async Task<StepReading?> RunAveragedMeasurement(CancellationToken token, WizardStep step, string stepDescription, bool fromSaved) {
            // Re-analyzing the same saved frames repeatedly yields identical readings (and would pop the folder
            // dialog once per run), so the saved path runs a single pass regardless of the averaging count.
            int count = fromSaved ? 1 : Math.Max(1, tiltAdapterOptions.MeasurementAverageCount);
            var readings = new List<(double A, double B, double Mean)>(count);

            // Redirect this step's run into its own folder when saving — for both live capture and the
            // "Use Saved AF" path (re-analyzing a previously captured run still writes a replayable per-step run).
            AutoFocusSaveOverride saveOverride = null;
            if (saveAFRuns && !string.IsNullOrEmpty(runRootFolder)) {
                try {
                    var perStepDir = Path.Combine(runRootFolder, StepFolderName(step));
                    Directory.CreateDirectory(perStepDir);
                    // Re-running a step (e.g. after a failed run) must not leave the prior attempt behind — clear
                    // any earlier runs so the folder holds only this attempt's run(s).
                    PruneStepFolderExcept(perStepDir, keepFolder: null);
                    // Keep only the raw frames needed for replay — no annotated/alignment or intermediate files.
                    saveOverride = new AutoFocusSaveOverride { Save = true, SavePath = perStepDir, SuppressAuxiliaryFiles = true };
                } catch (Exception ex) {
                    Logger.Error(ex, "Failed to create per-step save folder; continuing without saving this step");
                }
            }

            for (int i = 0; i < count; i++) {
                token.ThrowIfCancellationRequested();
                StatusText = $"Run {i + 1}/{count}...";
                bool ok;
                if (fromSaved) {
                    // IsMeasuring is set via callback after the folder dialog closes so the chart doesn't appear
                    // until the user has confirmed a selection.
                    ok = await inspector.AnalyzeAutoFocusFromSaved(token, onFolderSelected: () => IsMeasuring = true, saveOverride: saveOverride);
                } else {
                    ok = await inspector.AnalyzeAutoFocus(token, captureCameraBlock: true, saveOverride);
                }
                if (!ok) return null;
                var m = inspector.TiltModel?.TiltPlaneModel;
                if (m == null) return null;
                readings.Add((m.A, m.B, m.MeanFocuserPosition));
            }

            double avgA = readings.Average(r => r.A);
            double avgB = readings.Average(r => r.B);
            double avgMean = readings.Average(r => r.Mean);

            var latestModel = inspector.TiltModel?.TiltPlaneModel;
            AppendSummaryRows(readings, stepDescription, latestModel, count, avgA, avgB);

            var reading = new StepReading {
                A = avgA,
                B = avgB,
                Mean = avgMean,
                TiltAngleDeg = ComputeTiltAngleDeg(avgA, avgB, latestModel),
                DirectionDeg = NormalizeAngle(Math.Atan2(avgA, -avgB) * 180.0 / Math.PI),
                SaveFolder = saveOverride != null ? inspector.LastSaveFolder : null
            };
            PopulateCurvature(ref reading);

            // Keep only the run the metadata references (when averaging > 1 the earlier runs are not replayed).
            if (saveOverride != null && !string.IsNullOrEmpty(reading.SaveFolder)) {
                PruneStepFolderExcept(saveOverride.SavePath, reading.SaveFolder);
            }
            return reading;
        }

        // Deletes every AutoFocus run subfolder under a per-step folder except the one to keep (null = delete all),
        // so a step folder never accumulates stale/failed runs. Best-effort; logs and continues on any failure.
        private static void PruneStepFolderExcept(string perStepDir, string keepFolder) {
            if (string.IsNullOrEmpty(perStepDir) || !Directory.Exists(perStepDir)) {
                return;
            }
            var keepFull = string.IsNullOrEmpty(keepFolder) ? null : Path.GetFullPath(keepFolder);
            foreach (var dir in Directory.GetDirectories(perStepDir)) {
                if (keepFull != null && string.Equals(Path.GetFullPath(dir), keepFull, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                try {
                    Directory.Delete(dir, recursive: true);
                } catch (Exception ex) {
                    Logger.Warning($"Failed to delete stale calibration run folder {dir}: {ex.Message}");
                }
            }
        }

        private void AppendSummaryRows(List<(double A, double B, double Mean)> readings, string stepDescription,
            TiltPlaneModel latestModel, int count, double avgA, double avgB) {
            for (int i = 0; i < readings.Count; i++) {
                var (a, b, _) = readings[i];
                AddSummaryRow(new TiltMeasurementSummaryRow {
                    RunNumber = i + 1,
                    Direction = NormalizeAngle(Math.Atan2(a, -b) * 180.0 / Math.PI),
                    TiltAngleDeg = ComputeTiltAngleDeg(a, b, latestModel),
                    IsAverage = false,
                    StepDescription = stepDescription
                });
            }

            if (count > 1) {
                AddSummaryRow(new TiltMeasurementSummaryRow {
                    RunNumber = 0,
                    Direction = NormalizeAngle(Math.Atan2(avgA, -avgB) * 180.0 / Math.PI),
                    TiltAngleDeg = ComputeTiltAngleDeg(avgA, avgB, latestModel),
                    IsAverage = true,
                    StepDescription = stepDescription
                });

                double maxDev = readings.Max(r =>
                    Math.Sqrt(Math.Pow(r.A - avgA, 2) + Math.Pow(r.B - avgB, 2)));
                if (maxDev > MeasurementConsistencyWarningThreshold) {
                    HasMeasurementConsistencyWarning = true;
                    MeasurementConsistencyWarningText =
                        $"Measurements inconsistent: max deviation {maxDev:F4} exceeds {MeasurementConsistencyWarningThreshold:F4}. Consider re-running.";
                }
            }
        }

        // Field-curvature characterization for the metadata (req 9), read from the inspector's sensor model when
        // the sensor curve model is enabled. CurvatureAt takes microns; the screw-radius evaluation point is the
        // screw distance from the optical center. NaN when the curve model is unavailable.
        private void PopulateCurvature(ref StepReading reading) {
            reading.CurvatureRadiusMm = double.NaN;
            reading.CurvatureEffectAtScrewRadiusMicrons = double.NaN;
            var aberration = inspector.SensorModel?.SensorModelResult;
            if (aberration?.Model == null) return;
            reading.CurvatureRadiusMm = aberration.CurvatureRadiusMillimeters;
            double radiusMicrons = tiltAdapterOptions.ScrewRadiusMillimeters * 1000.0;
            if (radiusMicrons > 0) {
                reading.CurvatureEffectAtScrewRadiusMicrons = aberration.Model.CurvatureAt(radiusMicrons, 0);
            }
        }

        private double ComputeTiltAngleDeg(double a, double b, TiltPlaneModel model) {
            if (model == null) return double.NaN;
            var pixelSizeMicrons = profileService.ActiveProfile.CameraSettings.PixelSize;
            var fStepMicrons = model.FocuserStepSizeMicrons;
            // Fall back to the connected focuser's reported step size when the inspector
            // option (MicronsPerFocuserStep) hasn't been configured.
            if (double.IsNaN(fStepMicrons) || fStepMicrons <= 0) {
                fStepMicrons = focuserInfo.StepSize;
            }
            if (double.IsNaN(fStepMicrons) || fStepMicrons <= 0 ||
                double.IsNaN(pixelSizeMicrons) || pixelSizeMicrons <= 0) return double.NaN;
            // A and B are in focuser steps per normalized image coordinate (range [-0.5, 0.5]).
            // Convert to gradient in physical units: (steps * microns/step) / (pixels * microns/pixel).
            var gx = a * fStepMicrons / (model.ImageSize.Width * pixelSizeMicrons);
            var gy = b * fStepMicrons / (model.ImageSize.Height * pixelSizeMicrons);
            return Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI;
        }

        private void CancelMeasurement() {
            measureCts?.Cancel();
        }

        private void NextStep() {
            WizardStep next = currentStep + 1;

            if (next == WizardStep.Complete) {
                RunCalibrationMath(
                    tiltAdapterOptions.ScrewCount,
                    tiltAdapterOptions.ScrewRadiusMillimeters,
                    profileService.ActiveProfile.CameraSettings.PixelSize,
                    EffectiveFocuserStepMicrons(),
                    calibrationAppliedAmount,
                    IsStepperAdjustment);
                RebuildDiagram();
                FinalizeMetadata();
            }

            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
            StatusText = string.Empty;
            CurrentStep = next;
        }

        private void Restart() {
            measureCts?.Cancel();
            IsWizardRunning = false;
            IsMeasuring = false;
            isReplaying = false;
            SaveAFRuns = false; // saving must be re-enabled explicitly for each run
            stepReadings.Clear();
            measuredHardwareMicrons = double.NaN;
            lastRawAngleDiff = double.NaN;
            lastMoveMagnitudeRatio = double.NaN;
            runRootFolder = null;
            metadataPath = null;
            currentMetadata = null;
            RaiseHardwareSummaryChanged();
            ClearSummaryRows();
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
            HasRebaselineDriftWarning = false;
            RebaselineDriftWarningText = string.Empty;
            HasWarning = false;
            WarningText = string.Empty;
            StatusText = string.Empty;
            CurrentStep = WizardStep.Baseline;
        }

        private void ApplyDevice(string name) {
            var preset = TiltAdapterDevicePreset.ByName(name);
            tiltAdapterOptions.DeviceName = preset.Name;
            if (!preset.IsManual) {
                tiltAdapterOptions.ScrewCount = preset.ScrewCount;
                tiltAdapterOptions.AdjustmentType = preset.AdjustmentType;
                tiltAdapterOptions.ThreadPitchMicrons = preset.ThreadPitchMicrons;
                tiltAdapterOptions.StepperStepSizeMicrons = preset.StepperStepSizeMicrons;
                tiltAdapterOptions.ScrewRadiusMillimeters = preset.ScrewRadiusMillimeters;
            }
            RaisePropertyChanged(nameof(SelectedDevice));
            RaisePropertyChanged(nameof(IsManualDevice));
            RaisePropertyChanged(nameof(AdjustmentType));
            RaisePropertyChanged(nameof(ThreadPitchMicronsValue));
            RaisePropertyChanged(nameof(StepperStepSizeMicronsValue));
            RaisePropertyChanged(nameof(ScrewRadiusMillimetersValue));
            RaisePropertyChanged(nameof(IsStepperAdjustment));
            RaisePropertyChanged(nameof(CalibrationAmountLabel));
            RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));
        }

        private void UseMeasuredHardware() {
            if (!HasMeasuredHardware) return;
            if (IsStepperAdjustment) {
                tiltAdapterOptions.StepperStepSizeMicrons = measuredHardwareMicrons;
            } else {
                tiltAdapterOptions.ThreadPitchMicrons = measuredHardwareMicrons;
            }
            RaiseHardwareSummaryChanged();
        }

        private void BrowseSaveFolder() {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                if (!string.IsNullOrEmpty(SaveAFRunsPath)) {
                    dialog.SelectedPath = SaveAFRunsPath;
                }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    SaveAFRunsPath = dialog.SelectedPath;
                }
            }
        }

        // Runs the full calibration math from the six step readings and writes the results to the options. Shared
        // by a live run (geometry from the current options/profile) and a replay (geometry from the metadata).
        private void RunCalibrationMath(int screwCount, double radiusMm, double pixelSize, double fStep,
            double appliedAmount, bool isStepper) {
            var a = Reading(WizardStep.Baseline);
            var b = Reading(WizardStep.AllInward);
            var c = Reading(WizardStep.ReBaseline1);
            var d = Reading(WizardStep.Screw1);
            var e = Reading(WizardStep.ReBaseline2);
            var f = Reading(WizardStep.Screw2);

            // Curvature (backfocus) sign from baseline (a) → all-inward (b) mean focus.
            tiltAdapterOptions.ScrewInwardCurvatureSign = TiltCalibrationCalculator.ComputeCurvatureSign(b.Mean, a.Mean);

            // Screw angles from each move relative to its preceding re-baseline (c→d, e→f).
            double d1A = d.A - c.A, d1B = d.B - c.B;
            double d2A = f.A - e.A, d2B = f.B - e.B;
            var (s1, s2, s3, s4, rawDiff) = TiltCalibrationCalculator.ComputeScrewAngles(d1A, d1B, d2A, d2B, screwCount);

            tiltAdapterOptions.Screw1AngleDegrees = s1;
            tiltAdapterOptions.Screw2AngleDegrees = s2;
            tiltAdapterOptions.Screw3AngleDegrees = s3;
            tiltAdapterOptions.Screw4AngleDegrees = s4;
            if (tiltAdapterOptions.ScrewCount != screwCount) {
                tiltAdapterOptions.ScrewCount = screwCount; // replaying a run captured with a different screw count
            }
            tiltAdapterOptions.CalibratedScrewCount = screwCount;
            tiltAdapterOptions.IsCalibrated = true;

            lastRawAngleDiff = rawDiff;
            lastMoveMagnitudeRatio = TiltCalibrationCalculator.MoveMagnitudeRatio(d1A, d1B, d2A, d2B);
            ValidateCalibrationQuality(rawDiff, lastMoveMagnitudeRatio, screwCount);

            // Recover the adapter hardware (µm/turn or µm/step).
            measuredHardwareMicrons = double.NaN;
            calibrationScrewRadiusMm = radiusMm;
            calibrationPixelSizeMicrons = pixelSize;
            calibrationFocuserStepMicrons = fStep;

            var model = inspector.TiltModel?.TiltPlaneModel;
            if (model != null) {
                var inputs = new TiltCalibrationInputs {
                    ScrewCount = screwCount,
                    Baseline = new TiltGradient(a.A, a.B, a.Mean),
                    AllInward = new TiltGradient(b.A, b.B, b.Mean),
                    ReBaseline1 = new TiltGradient(c.A, c.B, c.Mean),
                    Screw1 = new TiltGradient(d.A, d.B, d.Mean),
                    ReBaseline2 = new TiltGradient(e.A, e.B, e.Mean),
                    Screw2 = new TiltGradient(f.A, f.B, f.Mean),
                    ImageWidthPixels = model.ImageSize.Width,
                    ImageHeightPixels = model.ImageSize.Height,
                    PixelSizeMicrons = pixelSize,
                    FocuserStepMicrons = fStep,
                    ScrewRadiusMillimeters = radiusMm,
                    CalibrationAppliedAmount = appliedAmount,
                    IsStepperAdjustment = isStepper
                };
                double measured = TiltCalibrationCalculator.RecoverHardwareMicrons(inputs);
                if (!double.IsNaN(measured)) {
                    measuredHardwareMicrons = measured;
                    if (isStepper) {
                        tiltAdapterOptions.LastMeasuredStepperStepSizeMicrons = measured;
                    } else {
                        tiltAdapterOptions.LastMeasuredThreadPitchMicrons = measured;
                    }
                }
            }

            EvaluateRebaselineDrift();
            RaiseHardwareSummaryChanged();
        }

        private void RaiseHardwareSummaryChanged() {
            RaisePropertyChanged(nameof(HasMeasuredHardware));
            RaisePropertyChanged(nameof(MeasuredHardwareDisplay));
            RaisePropertyChanged(nameof(SavedHardwareDisplay));
            RaisePropertyChanged(nameof(HardwareDeltaDisplay));
            RaisePropertyChanged(nameof(CalibrationPixelSizeDisplay));
            RaisePropertyChanged(nameof(CalibrationFocuserStepDisplay));
            RaisePropertyChanged(nameof(CalibrationScrewRadiusDisplay));
            RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));
            OnUIThread(() => ((RelayCommand)UseMeasuredHardwareCommand).NotifyCanExecuteChanged());
        }

        // Two single-screw turns of the same amount should produce gradient changes that are ~equal in magnitude
        // and the correct angular distance apart. A bad angle gap OR very unequal magnitudes (uneven turning /
        // backlash) means the recovered geometry/hardware is unreliable — warn so the user recalibrates.
        private const double MagnitudeRatioWarnThreshold = 1.5; // larger move > 1.5x smaller => suspect

        private void ValidateCalibrationQuality(double rawDiff, double magnitudeRatio, int screwCount) {
            double expected = screwCount == 3 ? 120.0 : 90.0;
            // Fold the diff so it is in [0, 180] — both CW and CCW gaps compare to the same expected value.
            double foldedDiff = rawDiff <= 180.0 ? rawDiff : 360.0 - rawDiff;
            double deviation = Math.Abs(foldedDiff - expected);
            bool angleBad = deviation > 30.0;
            bool magnitudeBad = !double.IsNaN(magnitudeRatio) && magnitudeRatio > MagnitudeRatioWarnThreshold;

            HasWarning = angleBad || magnitudeBad;
            if (!HasWarning) {
                WarningText = string.Empty;
                return;
            }
            var parts = new List<string>(2);
            if (angleBad) {
                parts.Add($"Screw 1→2 measured angle gap is {foldedDiff:F1}° (expected ~{expected}°)");
            }
            if (magnitudeBad) {
                parts.Add($"the two screw turns produced very unequal tilt changes ({magnitudeRatio:F1}× apart) — turn each screw the same amount");
            }
            WarningText = string.Join("; ", parts) + ". Consider recalibrating.";
        }

        // Re-baseline drift: each re-baseline (c, e) should return close to the prior state. A large residual
        // relative to the subsequent screw move means backlash / an uneven undo contaminated the calibration.
        private void EvaluateRebaselineDrift() {
            var a = Reading(WizardStep.Baseline);
            var c = Reading(WizardStep.ReBaseline1);
            var d = Reading(WizardStep.Screw1);
            var e = Reading(WizardStep.ReBaseline2);
            var f = Reading(WizardStep.Screw2);

            double drift1 = TiltCalibrationCalculator.RebaselineDriftRatio(c.A - a.A, c.B - a.B, d.A - c.A, d.B - c.B);
            double drift2 = TiltCalibrationCalculator.RebaselineDriftRatio(e.A - c.A, e.B - c.B, f.A - e.A, f.B - e.B);

            var parts = new List<string>(2);
            if (!double.IsNaN(drift1) && drift1 > RebaselineDriftWarnThreshold) {
                parts.Add($"re-baseline 1 drifted {drift1 * 100.0:F0}% of the screw-1 move");
            }
            if (!double.IsNaN(drift2) && drift2 > RebaselineDriftWarnThreshold) {
                parts.Add($"re-baseline 2 drifted {drift2 * 100.0:F0}% of the screw-2 move");
            }
            HasRebaselineDriftWarning = parts.Count > 0;
            RebaselineDriftWarningText = parts.Count == 0
                ? string.Empty
                : "Re-baseline drift detected: " + string.Join("; ", parts) +
                    ". The undo between moves left residual tilt (backlash or an uneven turn); consider recalibrating.";
        }

        private StepReading Reading(WizardStep step) =>
            stepReadings.TryGetValue(step, out var r) ? r : default;

        // ---- Replay -----------------------------------------------------------------------------------------

        // Replays a saved calibration run from a folder: reads metadata.json, re-analyzes each step from its saved
        // frames, and recomputes the calibration. A single "Replay" button opens the shared replay-settings modal,
        // which offers three modes (see TiltReplayModeResolver):
        // - Use current settings: current profile star-detection + current tilt geometry (metadata does not override).
        // - Use capture-time settings in memory: the run's stored star-detection as a transient per-step override
        //   (profile untouched) + the run's stored geometry — reproduces the original calibration exactly.
        // - Update profile to capture-time: persist the run's star-detection settings to the live profile, then replay
        //   against it + the run's stored geometry.
        private async Task ReplayAsync() {
            string folder;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                if (!string.IsNullOrEmpty(SaveAFRunsPath)) {
                    dialog.SelectedPath = SaveAFRunsPath;
                }
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
                    return;
                }
                folder = dialog.SelectedPath;
            }

            var metadataFile = Path.Combine(folder, "metadata.json");
            if (!File.Exists(metadataFile)) {
                Notification.ShowError("No metadata.json found in the selected folder.");
                return;
            }

            TiltCalibrationMetadata metadata;
            try {
                metadata = TiltCalibrationMetadata.Deserialize(File.ReadAllText(metadataFile));
                metadata.Validate();
            } catch (Exception ex) {
                Notification.ShowError($"Could not read metadata.json: {ex.Message}");
                Logger.Error(ex, "Failed to read tilt calibration metadata for replay");
                return;
            }

            var byStep = (metadata.RunStepMapping ?? new List<TiltRunStepMapping>())
                .GroupBy(m => m.Step, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Folder, StringComparer.OrdinalIgnoreCase);
            foreach (var step in MeasurementSteps) {
                if (!byStep.ContainsKey(step.ToString())) {
                    Notification.ShowError($"metadata.json has no saved folder for step '{step}'. Cannot replay.");
                    return;
                }
            }

            // Consolidated replay: one button, three modes resolved by the shared replay-settings modal (seeded from a
            // representative per-step AutoFocus replay snapshot). Cancelling / closing the modal aborts.
            var representativeStepFolder = byStep[MeasurementSteps.First().ToString()];
            AutoFocusReplayMetadata.TryLoad(representativeStepFolder, out var replayMetadata, out _);
            var choice = await ReplaySettingsPrompt.ShowAsync(windowServiceFactory, replayMetadata);
            if (choice == ReplaySettingsChoice.Cancel) {
                return;
            }
            var mode = TiltReplayModeResolver.Resolve(choice);

            // "Update profile to capture-time": persist the run's capture-time star-detection settings to the live
            // profile, then replay against it (no per-step override needed). ApplyFullSnapshot raises INPC -> UI thread.
            if (mode.UpdateProfileToCaptureTime) {
                var captureSnapshot = BuildTiltReplayDetectionOverride(representativeStepFolder, metadata);
                if (captureSnapshot != null) {
                    OnUIThread(() => HocusFocusPlugin.StarDetectionOptions?.ApplyFullSnapshot(captureSnapshot));
                }
            }

            // Replaying with current geometry applies the current screw count to the saved deltas; if the saved run
            // used a different screw count, the angle fit is meaningless. Warn rather than silently corrupt.
            if (!mode.UseMetadataGeometry && metadata.NumberOfScrews != tiltAdapterOptions.ScrewCount) {
                Notification.ShowWarning($"The saved run used {metadata.NumberOfScrews} screws but the current setting is " +
                    $"{tiltAdapterOptions.ScrewCount}. Replaying with current settings may produce incorrect angles.");
            }

            measureCts?.Dispose();
            measureCts = new CancellationTokenSource();
            var token = measureCts.Token;

            isReplaying = true;
            IsWizardRunning = true;
            IsMeasuring = true;
            stepReadings.Clear();
            ClearSummaryRows();
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
            HasRebaselineDriftWarning = false;
            RebaselineDriftWarningText = string.Empty;
            HasWarning = false;
            WarningText = string.Empty;
            StatusText = string.Empty;

            bool completed = false;
            try {
                foreach (var step in MeasurementSteps) {
                    token.ThrowIfCancellationRequested();
                    StatusText = $"Replaying {step}...";
                    // Capture-time-in-memory replay passes a detached per-step star-detection snapshot as an override so
                    // the profile is never touched. "Use current settings" and "update profile" both pass null (the
                    // latter has already mutated the live profile to the capture-time settings).
                    var detectionOverride = mode.ApplyCaptureTimeOverridePerStep
                        ? BuildTiltReplayDetectionOverride(byStep[step.ToString()], metadata)
                        : null;
                    bool ok = await inspector.AnalyzeAutoFocusFromSavedPath(byStep[step.ToString()], token, detectionOverride);
                    if (!ok) {
                        StatusText = $"Replay failed at {step}.";
                        Notification.ShowError($"Replay failed at step '{step}'. The saved frames could not be analyzed.");
                        return;
                    }
                    var m = inspector.TiltModel?.TiltPlaneModel;
                    if (m == null) {
                        StatusText = $"Replay produced no tilt model at {step}.";
                        Notification.ShowError($"Replay produced no tilt model at step '{step}'.");
                        return;
                    }
                    var reading = new StepReading {
                        A = m.A,
                        B = m.B,
                        Mean = m.MeanFocuserPosition,
                        TiltAngleDeg = ComputeTiltAngleDeg(m.A, m.B, m),
                        DirectionDeg = NormalizeAngle(Math.Atan2(m.A, -m.B) * 180.0 / Math.PI)
                    };
                    PopulateCurvature(ref reading);
                    stepReadings[step] = reading;
                    AddSummaryRow(new TiltMeasurementSummaryRow {
                        RunNumber = 0,
                        Direction = reading.DirectionDeg,
                        TiltAngleDeg = reading.TiltAngleDeg,
                        IsAverage = false,
                        StepDescription = StepDescription(step)
                    });
                }

                if (mode.UseMetadataGeometry) {
                    calibrationAppliedAmount = metadata.CalibrationAppliedAmount;
                    RaisePropertyChanged(nameof(CalibrationAppliedAmount));
                    RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));
                    RunCalibrationMath(
                        metadata.NumberOfScrews,
                        metadata.ScrewRadiusMillimeters,
                        metadata.PixelSizeMicrons,
                        metadata.FocuserStepSizeMicrons,
                        metadata.CalibrationAppliedAmount,
                        metadata.IsStepperAdjustment);
                } else {
                    // Use the current profile / tilt-adapter settings, exactly as a live calibration would.
                    RunCalibrationMath(
                        tiltAdapterOptions.ScrewCount,
                        tiltAdapterOptions.ScrewRadiusMillimeters,
                        profileService.ActiveProfile.CameraSettings.PixelSize,
                        EffectiveFocuserStepMicrons(),
                        calibrationAppliedAmount,
                        IsStepperAdjustment);
                }
                RebuildDiagram();
                StatusText = "Replay complete.";
                CurrentStep = WizardStep.Complete;
                completed = true;
            } catch (OperationCanceledException) {
                StatusText = "Replay cancelled.";
            } catch (Exception ex) {
                Notification.ShowError($"Replay failed: {ex.Message}");
                Logger.Error(ex, "Tilt calibration replay failed");
            } finally {
                // "Use capture-time in memory" passes detection settings as an in-memory override, so nothing is undone.
                // "Update profile to capture-time" intentionally persisted the capture-time settings to the profile (the
                // user opted in via the modal), so it is likewise not restored.
                isReplaying = false;
                IsMeasuring = false;
                // A successful replay ends on the Complete panel (like a live run); a failed/cancelled replay
                // returns to the idle config panel so the user can retry instead of being stuck mid-run.
                if (!completed) {
                    IsWizardRunning = false;
                }
            }
        }

        /// <summary>
        /// Builds a detached, in-memory capture-time star-detection snapshot for the no-mutation tilt replay. Prefers
        /// the full per-step replay <c>metadata.json</c> (runs captured after that feature shipped); falls back, for
        /// older tilt runs, to overlaying the curated <see cref="OptimizedStarDetectionSettings"/> onto a snapshot of
        /// the current options — reproducing the legacy "apply optimized settings" behavior without mutating the
        /// profile. Returns null when there is nothing to override (the replay then uses current settings).
        /// </summary>
        private static IStarDetectionOptions BuildTiltReplayDetectionOverride(string stepFolder, TiltCalibrationMetadata tiltMetadata) {
            if (AutoFocusReplayMetadata.TryLoad(stepFolder, out var replayMetadata, out _) && replayMetadata.StarDetection != null) {
                return replayMetadata.StarDetection;
            }
            var optimized = tiltMetadata?.OptimizedStarDetectionSettings;
            if (optimized == null) {
                return null;
            }
            var snapshot = StarDetectionSettingsSnapshot.FromOptions(HocusFocusPlugin.StarDetectionOptions);
            OverlayOptimizedSettings(snapshot, optimized);
            return snapshot;
        }

        // Overlays the curated optimized-settings subset onto a snapshot, mirroring
        // StarDetectionOptions.ApplyOptimizedSnapshotToLiveProperties (the curated knobs win; the rest keep the
        // snapshot's current-options values).
        private static void OverlayOptimizedSettings(StarDetectionSettingsSnapshot snapshot, OptimizedStarDetectionSettings s) {
            snapshot.BrightnessSensitivity = s.BrightnessSensitivity;
            snapshot.StarClippingMultiplier = s.StarClippingMultiplier;
            snapshot.NoiseClippingMultiplier = s.NoiseClippingMultiplier;
            snapshot.StarPeakResponse = s.StarPeakResponse;
            snapshot.MaxDistortion = s.MaxDistortion;
            snapshot.MinHFR = s.MinHFR;
            snapshot.StarCenterTolerance = s.StarCenterTolerance;
            snapshot.StructureLayers = s.StructureLayers;
            snapshot.NoiseReductionRadius = s.NoiseReductionRadius;
            snapshot.MinStarBoundingBoxSize = s.MinStarBoundingBoxSize;
            snapshot.HotpixelThresholdingEnabled = s.HotpixelThresholdingEnabled;
            snapshot.HotpixelThreshold = s.HotpixelThreshold;
            snapshot.DefocusAwareGates = s.DefocusAwareGates;
            snapshot.DefocusDistortionSizeReference = s.DefocusDistortionSizeReference;
            snapshot.DefocusDistortionMinFactor = s.DefocusDistortionMinFactor;
            snapshot.DefocusCenteringToleranceFactor = s.DefocusCenteringToleranceFactor;
            snapshot.DefocusAwareStructure = s.DefocusAwareStructure;
            snapshot.StructureLayerBoost = s.StructureLayerBoost;
            snapshot.DefocusAwareDonutDetection = s.DefocusAwareDonutDetection;
            snapshot.DonutMorphCloseSize = s.DonutMorphCloseSize;
            snapshot.DonutMinAnnularityHoleFraction = s.DonutMinAnnularityHoleFraction;
            snapshot.DonutMaxStreakEccentricity = s.DonutMaxStreakEccentricity;
            snapshot.DonutSaturationBloomRadius = s.DonutSaturationBloomRadius;
        }

        // ---- Metadata ---------------------------------------------------------------------------------------

        private void RecordStepIntoMetadata(WizardStep step, StepReading reading) {
            if (!saveAFRuns || currentMetadata == null) return;
            string stepName = step.ToString();

            currentMetadata.RunStepMapping.RemoveAll(m => string.Equals(m.Step, stepName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(reading.SaveFolder)) {
                currentMetadata.RunStepMapping.Add(new TiltRunStepMapping { Step = stepName, Folder = reading.SaveFolder });
            }

            currentMetadata.PerStep.RemoveAll(p => string.Equals(p.Step, stepName, StringComparison.OrdinalIgnoreCase));
            currentMetadata.PerStep.Add(new TiltPerStepResult {
                Step = stepName,
                TiltPlaneA = reading.A,
                TiltPlaneB = reading.B,
                MeanFocuserPosition = reading.Mean,
                TiltAngleDeg = reading.TiltAngleDeg,
                DirectionDeg = reading.DirectionDeg,
                CurvatureRadiusMillimeters = reading.CurvatureRadiusMm,
                CurvatureEffectMicronsAtScrewRadius = reading.CurvatureEffectAtScrewRadiusMicrons
            });
            WriteMetadata();
        }

        private void FinalizeMetadata() {
            if (!saveAFRuns || currentMetadata == null) return;
            currentMetadata.Calibration = new TiltCalibrationResultRecord {
                Screw1AngleDegrees = tiltAdapterOptions.Screw1AngleDegrees,
                Screw2AngleDegrees = tiltAdapterOptions.Screw2AngleDegrees,
                Screw3AngleDegrees = tiltAdapterOptions.Screw3AngleDegrees,
                Screw4AngleDegrees = tiltAdapterOptions.Screw4AngleDegrees,
                CurvatureSign = tiltAdapterOptions.ScrewInwardCurvatureSign,
                MeasuredHardwareMicrons = measuredHardwareMicrons,
                RawAngleDiffDegrees = lastRawAngleDiff,
                MoveMagnitudeRatio = lastMoveMagnitudeRatio
            };
            WriteMetadata();
        }

        private void WriteMetadata() {
            if (currentMetadata == null || string.IsNullOrEmpty(metadataPath)) return;
            try {
                File.WriteAllText(metadataPath, currentMetadata.Serialize());
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to write tilt calibration metadata");
            }
        }

        private void RebuildDiagram() {
            ScrewDiagramItems.Clear();
            ScrewConnectionLines.Clear();

            int n = tiltAdapterOptions.ScrewCount;
            if (n < 3 || !IsCalibrationValid) return;

            var angles = new[] {
                tiltAdapterOptions.Screw1AngleDegrees,
                tiltAdapterOptions.Screw2AngleDegrees,
                tiltAdapterOptions.Screw3AngleDegrees,
                n == 4 ? tiltAdapterOptions.Screw4AngleDegrees : double.NaN
            };

            var centers = new (double cx, double cy)[n];
            for (int i = 0; i < n; i++) {
                double theta = angles[i] * Math.PI / 180.0;
                double cx = 100 + 75 * Math.Sin(theta);
                double cy = 100 - 75 * Math.Cos(theta);
                centers[i] = (cx, cy);
                ScrewDiagramItems.Add(new TiltScrewDiagramItem {
                    X = cx - 12,
                    Y = cy - 12,
                    Number = i + 1,
                    AngleDegrees = angles[i]
                });
            }

            for (int i = 0; i < n; i++) {
                int j = (i + 1) % n;
                ScrewConnectionLines.Add(new TiltScrewConnectionLine {
                    X1 = centers[i].cx,
                    Y1 = centers[i].cy,
                    X2 = centers[j].cx,
                    Y2 = centers[j].cy
                });
            }
        }

        // CanExecuteChanged on WPF commands and ObservableCollection mutations must touch UI-owned objects on the
        // UI thread. DispatchSynchronizationContext is a synchronous Send with a same-context fast path, so calling
        // this from the UI thread is free and calling it from a DeviceMediator background broadcast marshals safely.
        private void OnUIThread(Action action) => applicationDispatcher.DispatchSynchronizationContext(action);

        private void NotifyCommandsCanExecuteChanged() => OnUIThread(NotifyCommandsCanExecuteChangedCore);

        // Raises CanExecuteChanged on every command WITHOUT marshaling. Only call this when already on the UI thread
        // (e.g. from a setter whose caller already marshaled via OnUIThread, such as UpdateDeviceInfo — F15).
        private void NotifyCommandsCanExecuteChangedCore() {
            ((AsyncRelayCommand)StartCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)RunMeasurementCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)UseSavedAFCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UseMeasuredHardwareCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ReplayCommand).NotifyCanExecuteChanged();
        }

        private static double NormalizeAngle(double deg) => ((deg % 360) + 360) % 360;
    }
}
