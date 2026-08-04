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
using NINA.Core.Utility.SerialCommunication;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Logger = NINA.Core.Utility.Logger;
using MyMessageBox = NINA.Core.MyMessageBox.MyMessageBox;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// The discrete calibration measurement steps. Each step is a single physical screw move followed by one
    /// measurement, so two changes are never compounded between measurements: the all-inward and per-screw moves
    /// are each bracketed by an explicit re-baseline. Screw 1's angle is derived from the move relative to
    /// mid(c, e) — the midpoint of the re-baselines that bracket it — which cancels a linear tilt drift across
    /// the sequence exactly. Screw 2's angle is derived relative to e alone unless a measured final re-baseline
    /// is present, in which case it gets the same midpoint symmetry (see
    /// <see cref="TiltCalibrationCalculator.Screw2Delta"/>).
    /// </summary>
    public enum WizardStep {
        Baseline = 0,     // a
        AllInward = 1,    // b: all screws inward once (curvature/backfocus sign via a→b)
        ReBaseline1 = 2,  // c: all screws back out once (≈ baseline)
        Screw1 = 3,       // d: screw 1 inward once (4-screw: + screw 3 outward)
        ReBaseline2 = 4,  // e: undo the screw-1 move (≈ c)
        Screw2 = 5,       // f: screw 2 inward once (4-screw: + screw 4 outward)
        Complete = 6,
        // ReBaseline3's ordinal (7) deliberately does NOT match its position in the flow: it runs BEFORE
        // Complete (immediately after Screw2, when measured), not after it. Complete's ordinal must never be
        // renumbered -- StepFolderName ((int)step + 1) persists it into saved-run folder names on disk
        // ("06_Complete" would become e.g. "07_Complete") -- so this optional step was appended numerically
        // last instead. StepFolderName special-cases ReBaseline3 -> "07_ReBaseline3" to sort correctly
        // alongside it. The actual step ORDER everywhere else (NextStep, GetMeasurementSteps,
        // activeMeasurementSteps) comes from an explicit array, never from this numeric value -- do not "fix"
        // this ordinal to look sequential.
        ReBaseline3 = 7,  // g (optional): undo the screw-2 move, measured — screw 2's drift-symmetric reference
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

        // Shared motorized-device connection singleton (HocusFocusPlugin.TiltDeviceConnectionService in
        // production, injected in tests; null-tolerant so device-less test rigs stay valid — the pane's
        // commands/properties then degrade to "not connected"/disabled).
        private readonly TiltDeviceConnectionService tiltDeviceConnectionService;
        // Lazily created: SerialPortProvider's own constructor runs a WMI scan, so the real provider is only
        // built on first port enumeration (when the pane is actually shown), never during VM construction.
        private ISerialPortProvider serialPortProvider;
        // The idle-disconnect confirmation, injectable for tests. Production default shows the NINA
        // message box (ShowIdleDisconnectPromptAsync); tests inject a fake returning true/false and assert
        // the right TiltDeviceConnectionService method is called.
        private readonly Func<Task<bool>> confirmIdleDisconnectAsync;
        // The simulator config the "Simulator" port checks against the selected EAT preset. Null-tolerant: a
        // device-less test rig may not wire it, in which case the Simulator-port config check is skipped.
        private readonly ICameraSimulatorOptions cameraSimulatorOptions;
        // The "change the simulator config to match?" confirmation, injectable for tests (mirrors
        // confirmIdleDisconnectAsync). Production default shows the NINA Yes/No message box (default No).
        private readonly Func<string, string, Task<bool>> confirmSimConfigChangeAsync;
        private IReadOnlyList<string> availablePortNames;
        // Live per-motor device positions for the connection pane (device order TR/TL/BR/BL). During a device-
        // driven run the service's cp poll is paused (the run holds the operation lease), so these are refreshed
        // per move from the controller; when idle they mirror the service's polled CurrentPositions.
        private IReadOnlyList<int> deviceDisplayPositions;
        // Per-motor positions captured at the start of the current calibration run, for the "Δ since start" display.
        private int[] calibrationBaselinePositions;
        private string connectedPortName;

        // Fix 3: ConnectTiltDeviceAsync defaults MeasureCurvatureDuringCalibration ON, but only once per VM
        // lifetime (this is a [PartCreationPolicy(CreationPolicy.Shared)] singleton, so "lifetime" == "session")
        // and never once the user has explicitly turned it off — otherwise a disconnect -> reconnect cycle
        // would silently re-force a deliberately disabled option back on. See the options PropertyChanged
        // handler (ctor) and ConnectTiltDeviceAsync below.
        private bool hasDefaultedMeasureCurvatureOnConnect;
        private bool userExplicitlyDisabledMeasureCurvatureThisSession;

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
        private bool hasConfidenceWarning = false;
        private string confidenceWarningText = string.Empty;
        private bool hasMeasurementFailureChoice = false;
        private string measurementFailureText = string.Empty;
        private bool hasDeviceLinkDroppedWarning = false;
        private string deviceLinkDroppedWarningText = string.Empty;

        // ---- T11: hands-off device-driven calibration state ------------------------------------------------
        // Captured at StartAsync (and, when it bootstraps a not-yet-running wizard, AutoRunAllAsync) time:
        // true when the run began connected to a motorized device, so every step's move is sent by the wizard
        // itself instead of instructing the user. Read at NextStep's Complete transition to decide whether the
        // device-linked calibration marker (ITiltAdapterOptions.DeviceLinkedCalibrationDeviceName) is set or
        // cleared -- see RunCalibrationMath.
        private bool currentRunIsDeviceDriven;
        // The exclusive T9 operation lease held for the WHOLE duration of a device-driven run: StartAsync
        // acquires it (refusing to start if the device is busy elsewhere); it is released once the run reaches
        // Complete (right after the restore move is attempted, successful or not) or the run is abandoned via
        // Restart. Null for a disconnected/manual run.
        private IDisposable tiltDeviceOperationToken;
        // Guards against re-sending a step's device move on a measurement retry: set to the step whose move
        // has already been applied to the (real) device. A retry after a measurement-only failure (the move
        // itself succeeded; AutoFocus/sensor-modeling failed) must not re-apply the same move a second time.
        private WizardStep? deviceMoveAppliedForStep;
        // Every device move successfully applied during the current device-driven run, in send order -- used
        // to walk backwards (inverse, reverse order) to return the device to baseline on an Auto Run All
        // cancellation or a hard failure partway through the sequence.
        private readonly List<TiltAdapterMove> appliedDeviceMovesThisRun = new List<TiltAdapterMove>();
        // AutoRunAllCommand re-entrancy guard.
        private bool isAutoRunningAll;
        // Latched when an automated run takes its cancel/failure exit. That exit rolls this run's device moves
        // back (RecoverAppliedMovesAsync) and releases the exclusive operation lease, which together make the
        // run unresumable: the readings already in stepReadings describe a device position that no longer
        // exists, and re-entering AutoRunAllAsync skips StartAsync (IsWizardRunning is still true) so it would
        // resume at the stale CurrentStep, unleased, sending moves computed from the rolled-back position.
        // Gates AutoRunAllCommand and the failure panel's RetryMeasurementCommand; cleared by StartAsync and
        // Restart. Deliberately does NOT tear the run panel down -- the failure text explaining what happened
        // lives inside it.
        private bool deviceRunAbandoned;
        // IProgress<string> adapter over the ApplicationStatus progress reporter, mirroring
        // InspectorVM.RunAutomaticAdjustmentAsync's moveProgress -- the same status-bar wording convention for
        // the other automated tilt-device consumer (T14).
        private readonly IProgress<string> moveProgress;
        // Test seam: when set, MeasureStep uses this instead of invoking the live inspector for a step's
        // measurement. The delegate is responsible for seeding stepReadings for the step (via
        // SeedStepReading) and returns whether the "measurement" succeeded. Lets Auto Run All's full-sequence
        // orchestration (device move -> measurement -> NextStep, repeated to Complete) be exercised without a
        // live inspector analysis, mirroring how RunCalibrationForTest bypasses RunCalibrationMath's live
        // sensor-model input. Always null in production.
        internal Func<WizardStep, CancellationToken, Task<bool>> MeasurementStepOverrideForTest { get; set; }

        // One (A, B, mean) tilt-plane reading plus the per-step field-curvature characterization, keyed by step.
        private readonly Dictionary<WizardStep, StepReading> stepReadings = new Dictionary<WizardStep, StepReading>();
        private CancellationTokenSource measureCts;

        // WizardSweepSummary reads the ACTIVE profile's FocuserSettings; these track the settings object
        // currently subscribed so in-place edits refresh the summary and profile swaps re-hook cleanly.
        private readonly System.ComponentModel.PropertyChangedEventHandler focuserSettingsHandler;
        private IFocuserSettings hookedFocuserSettings;

        private void HookActiveProfileFocuserSettings() {
            if (hookedFocuserSettings != null) {
                hookedFocuserSettings.PropertyChanged -= focuserSettingsHandler;
            }
            hookedFocuserSettings = profileService?.ActiveProfile?.FocuserSettings;
            if (hookedFocuserSettings != null) {
                hookedFocuserSettings.PropertyChanged += focuserSettingsHandler;
            }
        }

        private double measuredHardwareMicrons = double.NaN;
        private TiltCalibrationConfidence lastConfidence;
        private double pitchUncertaintyMicrons = double.NaN;
        private double pistonImpliedMicronsPerStep = double.NaN;
        private double calibrationPixelSizeMicrons;
        private double calibrationFocuserStepMicrons;
        private double calibrationScrewRadiusMm;
        private double lastRawAngleDiff = double.NaN;
        private double lastMoveMagnitudeRatio = double.NaN;
        // Task 5: corner-region AF cross-check of the paraboloid calibration. See RunCalibrationMath.
        private double cornerMeasuredHardwareMicrons = double.NaN;
        private double estimatorRelativeDifference = double.NaN;

        // Per-run save state (transient; only populated while saving AF runs for the current calibration run).
        private bool saveAFRuns;
        private string runRootFolder;
        private string metadataPath;
        private TiltCalibrationMetadata currentMetadata;

        private const double MeasurementConsistencyWarningThreshold = 0.02;
        // A re-baseline that returns close to the prior state drifts ~0; warn once the residual reaches half the
        // screw-move signal (backlash / uneven undo would corrupt the recovered angle/hardware for that screw).
        private const double RebaselineDriftWarnThreshold = 0.5;

        // The measurement steps in capture order. The 4-step flow (default) skips the two
        // curvature-direction steps; the Baseline reading then serves as the screw-1 reference
        // (the ReBaseline1 role of the 6-step flow). measureFinalRebaseline (Task 6) appends the optional
        // ReBaseline3 step to either flow: a MEASURED restore of the Screw2 move, giving screw 2 the same
        // drift-cancelling re-baseline symmetry screw 1 always gets from mid(ReBaseline1, ReBaseline2). It
        // is always the LAST measurement step (immediately before Complete) regardless of which base flow
        // it is appended to.
        internal static WizardStep[] GetMeasurementSteps(bool measureCurvature, bool measureFinalRebaseline) {
            var baseSteps = measureCurvature
                ? new[] { WizardStep.Baseline, WizardStep.AllInward, WizardStep.ReBaseline1,
                          WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 }
                : new[] { WizardStep.Baseline, WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 };
            if (!measureFinalRebaseline) {
                return baseSteps;
            }
            var withFinalRebaseline = new WizardStep[baseSteps.Length + 1];
            Array.Copy(baseSteps, withFinalRebaseline, baseSteps.Length);
            withFinalRebaseline[baseSteps.Length] = WizardStep.ReBaseline3;
            return withFinalRebaseline;
        }

        // Captured at run start (StartAsync/ReplayAsync) so toggling the option mid-run is inert.
        private WizardStep[] activeMeasurementSteps = GetMeasurementSteps(measureCurvature: false, measureFinalRebaseline: false);

        // Prose for the wizard's Signal Amplification row: per-sweep image estimate plus the total
        // for the whole calibration at current settings. Internal for tests.
        internal static string BuildSweepSummary(int stepsInRun, int measurementAverage,
            int stepCount, int framesPerPoint, int signalAmplification, int profileOffsetSteps, int profileFramesPerPoint) {
            var (points, imagesPerRun) = InspectorVM.EstimateImagesPerRun(
                stepCount, framesPerPoint, signalAmplification, profileOffsetSteps, profileFramesPerPoint);
            if (imagesPerRun <= 0) return string.Empty;
            int frames = framesPerPoint > 0 ? framesPerPoint : profileFramesPerPoint;
            int sweeps = stepsInRun * Math.Max(1, measurementAverage);
            return $"Every calibration step runs a full autofocus sweep of ~{imagesPerRun} images ({points} focus positions × {frames} exposure{(frames == 1 ? "" : "s")}). " +
                $"At the current settings this calibration will take {sweeps} sweeps ≈ {sweeps * imagesPerRun} images total. " +
                "Increase for more signal on faint stars; decrease to run faster (1 = a regular autofocus).";
        }

        private struct StepReading {
            public double A;
            public double B;
            public double Mean;
            public double TiltAngleDeg;
            public double DirectionDeg;
            public double CurvatureRadiusMm;
            public double CurvatureEffectAtScrewRadiusMicrons;
            public string SaveFolder;

            // Task 5: the inspector's 4-corner region plane for this same step (honest per-corner estimator
            // since Task 1), captured alongside the paraboloid reading above for the corner-region AF
            // cross-check (see RunCalibrationMath). NaN when the corner plane could not be fit for this step
            // (or any of the averaged sub-measurements that make it up) -- a struct default of 0.0 would look
            // like a valid zero-tilt corner reading and silently corrupt the cross-check, so every WRITER of
            // this struct (SeedStepReading, RunAveragedMeasurement, ReplayAsync) sets these explicitly rather
            // than relying on the field default. That covers every step actually present in stepReadings, but
            // NOT a step missing from it entirely: Reading(step) falls back to default(StepReading) for an
            // absent key, which zero-inits CornerA/B/Mean to 0.0 too (C# struct defaults, not this comment's
            // "writer" guarantee). RunCalibrationMath's completeness check therefore also requires
            // stepReadings.ContainsKey(step) (via TryGetValue), not just the non-NaN test alone, so an
            // entirely-unmeasured step can never be mistaken for a valid zero-tilt corner reading.
            public double CornerA;
            public double CornerB;
            public double CornerMean;
        }

        [ImportingConstructor]
        public TiltAdapterWizardVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            InspectorVM inspector)
            : this(profileService, applicationStatusMediator, cameraMediator, focuserMediator, inspector,
                   HocusFocusPlugin.ApplicationDispatcher, HocusFocusPlugin.TiltAdapterOptions,
                   HocusFocusPlugin.TiltDeviceConnectionService,
                   cameraSimulatorOptions: HocusFocusPlugin.CameraSimulatorOptions) { }

        public TiltAdapterWizardVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            InspectorVM inspector,
            IApplicationDispatcher applicationDispatcher,
            ITiltAdapterOptions tiltAdapterOptions,
            TiltDeviceConnectionService tiltDeviceConnectionService = null,
            ISerialPortProvider serialPortProvider = null,
            Func<Task<bool>> confirmIdleDisconnectAsync = null,
            ICameraSimulatorOptions cameraSimulatorOptions = null,
            Func<string, string, Task<bool>> confirmSimConfigChangeAsync = null)
            : base(profileService) {
            this.inspector = inspector;
            this.tiltAdapterOptions = tiltAdapterOptions;
            this.applicationDispatcher = applicationDispatcher;
            this.tiltDeviceConnectionService = tiltDeviceConnectionService;
            this.serialPortProvider = serialPortProvider; // null => created lazily on first enumeration
            this.confirmIdleDisconnectAsync = confirmIdleDisconnectAsync ?? ShowIdleDisconnectPromptAsync;
            this.cameraSimulatorOptions = cameraSimulatorOptions;
            this.confirmSimConfigChangeAsync = confirmSimConfigChangeAsync ?? ShowSimConfigChangePromptAsync;
            this.progress = ProgressFactory.Create(applicationStatusMediator, "Tilt Adapter Wizard");
            this.moveProgress = new Progress<string>(text => this.progress.Report(new ApplicationStatus { Status = $"Tilt Adapter Wizard: {text}" }));

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
            // deviceRunAbandoned gates EVERY re-entry into the measurement loop, not just Auto Run All: these two
            // buttons sit in the same Panel B row, stay visible in exactly the post-rollback state, and reach the
            // same handlers (RunMeasurementCommand shares RunMeasurementAsync with RetryMeasurementCommand below).
            // Leaving either open would let one click resume a run whose device moves had just been undone.
            RunMeasurementCommand = new AsyncRelayCommand(RunMeasurementAsync, () => IsOnMeasurementStep && !IsMeasuring && AreDevicesConnected && !deviceRunAbandoned);
            UseSavedAFCommand = new AsyncRelayCommand(RunSavedMeasurementAsync, () => IsOnMeasurementStep && !IsMeasuring && !deviceRunAbandoned);
            CancelCommand = new RelayCommand(CancelMeasurement, () => IsMeasuring);
            RestartCommand = new RelayCommand(Restart);
            UseMeasuredHardwareCommand = new RelayCommand(UseMeasuredHardware, () => HasMeasuredHardware);
            BrowseSaveFolderCommand = new RelayCommand(BrowseSaveFolder);
            ReplayCommand = new AsyncRelayCommand(ReplayAsync, () => !IsWizardRunning && !IsMeasuring);
            RetryMeasurementCommand = new AsyncRelayCommand(RunMeasurementAsync, () => HasMeasurementFailureChoice && IsOnMeasurementStep && !IsMeasuring && AreDevicesConnected && !deviceRunAbandoned);
            ApplyManualCalibrationCommand = new RelayCommand(ApplyManualCalibration);
            ClearCalibrationCommand = new RelayCommand(ClearCalibration);
            RefreshPortsCommand = new RelayCommand(RefreshPorts);
            ConnectDeviceCommand = new AsyncRelayCommand(ConnectTiltDeviceAsync, () =>
                IsMotorizedDevice && this.tiltDeviceConnectionService != null && !IsTiltDeviceConnected && !string.IsNullOrEmpty(SelectedPortName));
            DisconnectDeviceCommand = new AsyncRelayCommand(DisconnectTiltDeviceAsync, () => IsTiltDeviceConnected);
            AutoRunAllCommand = new AsyncRelayCommand(AutoRunAllAsync, () =>
                IsMotorizedDevice && this.tiltDeviceConnectionService != null && IsTiltDeviceConnected &&
                !isAutoRunningAll && !IsMeasuring && !deviceRunAbandoned && HasValidDeviceAppliedAmount());

            // This VM is a Shared MEF singleton, so these ctor-time subscriptions intentionally live for the
            // whole app run (like every other subscription in this ctor).
            if (this.tiltDeviceConnectionService != null) {
                this.tiltDeviceConnectionService.PropertyChanged += TiltDeviceConnectionService_PropertyChanged;
                this.tiltDeviceConnectionService.IdlePromptRequested += TiltDeviceConnectionService_IdlePromptRequested;
            }

            // The display-only focuser convention k changes what the mechanical wording and the physical
            // screw angles READ, never what is stored. Refresh exactly those (design §3, site 6).
            if (this.inspector?.InspectorOptions != null) {
                this.inspector.InspectorOptions.PropertyChanged += (s, e) => OnUIThread(() => {
                    if (e.PropertyName == nameof(IInspectorOptions.FocuserIncreasesTowardObjective)) {
                        RaisePropertyChanged(nameof(CwMovesAdapterTowardObjective));
                        RebuildDiagram();
                    }
                });
            }

            tiltAdapterOptions.PropertyChanged += (s, e) => OnUIThread(() => {
                if (e.PropertyName == nameof(ITiltAdapterOptions.ScrewInwardCurvatureSign)) {
                    RaisePropertyChanged(nameof(HasCurvatureCalibration));
                    RaisePropertyChanged(nameof(CurvatureSignDescription));
                    RaisePropertyChanged(nameof(CwMovesAdapterTowardObjective));
                    RaisePropertyChanged(nameof(CurvatureSignProvenance));
                    // The readout + diagram show physical image angles, which are stored ± 180° keyed on
                    // this sign — refresh both (RebuildDiagram also re-raises the PhysicalScrew* props).
                    RebuildDiagram();
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured)) {
                    RaisePropertyChanged(nameof(CurvatureSignDescription));
                    RaisePropertyChanged(nameof(CurvatureSignProvenance));
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.MeasureCurvatureDuringCalibration) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.MeasureFinalRebaseline) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.MeasurementAverageCount)) {
                    RaisePropertyChanged(nameof(WizardSweepSummary));
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.MeasureCurvatureDuringCalibration)) {
                    RaisePropertyChanged(nameof(CurvatureSignProvenance));
                    // Fix 3: the only two writers of this option are the checkbox (XAML binds it directly to
                    // TiltAdapterOptions.MeasureCurvatureDuringCalibration, a user action) and
                    // ConnectTiltDeviceAsync's own first-connect default (which only ever sets it to TRUE, never
                    // false — see below). So a change to FALSE observed here can only be the user explicitly
                    // unchecking it; remember that for the rest of this VM's lifetime so a later reconnect never
                    // silently re-forces it back on.
                    if (!tiltAdapterOptions.MeasureCurvatureDuringCalibration) {
                        userExplicitlyDisabledMeasureCurvatureThisSession = true;
                    }
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.CalibrationIsManual)) {
                    RaisePropertyChanged(nameof(IsCalibrationValid));
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
                    RaisePropertyChanged(nameof(IsMotorizedDevice));
                    NotifyCommandsCanExecuteChangedCore(); // ConnectDeviceCommand gates on IsMotorizedDevice
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.TiltDeviceSerialPortName)) {
                    RaisePropertyChanged(nameof(SelectedPortName));
                    NotifyCommandsCanExecuteChangedCore(); // ConnectDeviceCommand gates on a selected port
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
                    // Adjustment type changes the prompt vocabulary (screw turns vs signed steps).
                    RaisePropertyChanged(nameof(CwDirectionLabel));
                    RaisePropertyChanged(nameof(StepInstructions));
                    RaisePropertyChanged(nameof(BaselineRecoveryInstructions));
                    RaiseHardwareSummaryChanged();
                }
            });

            if (inspector.InspectorOptions != null) {
                inspector.InspectorOptions.PropertyChanged += (s, e) => OnUIThread(() => {
                    if (e.PropertyName == nameof(IInspectorOptions.SignalAmplification) ||
                        e.PropertyName == nameof(IInspectorOptions.StepCount) ||
                        e.PropertyName == nameof(IInspectorOptions.FramesPerPoint)) {
                        RaisePropertyChanged(nameof(WizardSweepSummary));
                    }
                });
            }

            // WizardSweepSummary also reads the active profile's FocuserSettings (offset steps / frames
            // per point), which the user can edit in place without swapping profiles — ProfileChanged
            // alone would leave the summary stale. Track the active profile's FocuserSettings and
            // re-hook on every profile change (unsubscribe old, subscribe new).
            focuserSettingsHandler = (s, e) => OnUIThread(() => {
                if (e.PropertyName == nameof(IFocuserSettings.AutoFocusInitialOffsetSteps) ||
                    e.PropertyName == nameof(IFocuserSettings.AutoFocusNumberOfFramesPerPoint)) {
                    RaisePropertyChanged(nameof(WizardSweepSummary));
                }
            });
            HookActiveProfileFocuserSettings();

            profileService.ProfileChanged += (s, e) => OnUIThread(() => {
                HookActiveProfileFocuserSettings();
                RaisePropertyChanged(nameof(PixelSizeMicronsValue));
                RaisePropertyChanged(nameof(FocuserStepSizeMicronsValue));
                RaisePropertyChanged(nameof(SelectedDevice));
                RaisePropertyChanged(nameof(IsManualDevice));
                // TiltAdapterOptions' ProfileChanged reload raises one broadcast PropertyChanged (null name)
                // that the per-name filters above never match, so the device-connection wrappers must be
                // re-raised here like every other wrapper property (see the comment below). The connection
                // service force-disconnects on profile change on its own; its Connected INPC refreshes the
                // connected-state wrappers separately.
                RaisePropertyChanged(nameof(IsMotorizedDevice));
                RaisePropertyChanged(nameof(SelectedPortName));
                RaisePropertyChanged(nameof(TiltDeviceStatusText));
                NotifyCommandsCanExecuteChangedCore();
                RaisePropertyChanged(nameof(SaveAFRunsPath));
                RaisePropertyChanged(nameof(WizardSweepSummary));
                // TiltAdapterOptions reloads its values from the new profile in its own ProfileChanged handler
                // (subscribed before this VM exists, so it runs first), but that reload raises one broadcast
                // PropertyChanged (null name) that the per-name filters in the options handler above never match.
                // Every wrapper/derived property must therefore be re-raised here, or the direction controls,
                // provenance text, prompts, and diagram keep showing the previous profile's state — and re-selecting
                // the stale direction value would silently overwrite the new profile's measured sign.
                RaisePropertyChanged(nameof(CwMovesAdapterTowardObjective));
                RaisePropertyChanged(nameof(CurvatureSignDescription));
                RaisePropertyChanged(nameof(CurvatureSignProvenance));
                RaisePropertyChanged(nameof(CwDirectionLabel));
                RaisePropertyChanged(nameof(HasCurvatureCalibration));
                RaisePropertyChanged(nameof(IsCalibrationValid));
                RaisePropertyChanged(nameof(StepInstructions));
                RaisePropertyChanged(nameof(BaselineRecoveryInstructions));
                RaisePropertyChanged(nameof(AdjustmentType));
                RaisePropertyChanged(nameof(IsStepperAdjustment));
                RaisePropertyChanged(nameof(CalibrationAmountLabel));
                // CalibrationAppliedAmount is now a wrapper over the persisted option (per-profile), so a
                // profile swap can genuinely change its resolved value — re-raise it here for the same reason
                // as the other wrapper properties above.
                RaisePropertyChanged(nameof(CalibrationAppliedAmount));
                // CalibrationAppliedAmountDisplay (turns/steps units) is intentionally not re-raised in this
                // list: RaiseHardwareSummaryChanged() below already raises it on every ProfileChanged.
                RaisePropertyChanged(nameof(ThreadPitchMicronsValue));
                RaisePropertyChanged(nameof(StepperStepSizeMicronsValue));
                RaisePropertyChanged(nameof(ScrewRadiusMillimetersValue));
                RaisePropertyChanged(nameof(ShowRunColumn));
                RaiseHardwareSummaryChanged();
                RebuildDiagram();
                // Re-run the manual-entry pre-fill for the new profile's calibration (it was constructor-only):
                // stored angles are response-convention, the manual field holds the physical image angle, and the
                // conversion uses the new profile's direction sign. When the new profile has no calibration angle
                // the previous value is kept, matching the constructor's NaN guard.
                if (!double.IsNaN(tiltAdapterOptions.Screw1AngleDegrees)) {
                    ManualScrew1AngleDegrees = TiltScrewGeometry.PhysicalToStoredAngle(
                        tiltAdapterOptions.Screw1AngleDegrees, tiltAdapterOptions.ScrewInwardCurvatureSign, FocuserSign);
                }
            });

            // Re-assert and lock a persisted device preset on load.
            ApplyDevice(tiltAdapterOptions.DeviceName);

            // Pre-fill the Manual Calibration Entry angle from an existing calibration. Stored angles
            // are response-convention; the manual field holds the PHYSICAL image angle, so convert
            // back (PhysicalToStoredAngle is self-inverse) with the current direction sign.
            if (!double.IsNaN(tiltAdapterOptions.Screw1AngleDegrees)) {
                manualScrew1AngleDegrees = TiltScrewGeometry.PhysicalToStoredAngle(
                    tiltAdapterOptions.Screw1AngleDegrees, tiltAdapterOptions.ScrewInwardCurvatureSign, FocuserSign);
            }

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

        // The calibration readout and the wizard diagram both display the PHYSICAL image position of
        // each screw (0° = straight up, increasing clockwise) — matching the "Manual Calibration Entry"
        // field and the diagram's "image as shown in NINA" caption. The persisted Screw{N}AngleDegrees
        // are RESPONSE-convention angles (the direction a CW turn drives the tilt gradient), which sit
        // 180° from the physical position on "CW moves adapter toward the objective" (−1) rigs and
        // coincide with it on the default +1 rig. TiltScrewGeometry.PhysicalToStoredAngle is
        // self-inverse, so the same call converts stored→physical with the adapter-direction sign in
        // effect. RebuildDiagram() re-raises these (and the sign-change handler calls it) so the
        // readout tracks both a re-calibration and a direction change.
        public double PhysicalScrew1AngleDegrees => TiltScrewGeometry.PhysicalToStoredAngle(tiltAdapterOptions.Screw1AngleDegrees, tiltAdapterOptions.ScrewInwardCurvatureSign, FocuserSign);
        public double PhysicalScrew2AngleDegrees => TiltScrewGeometry.PhysicalToStoredAngle(tiltAdapterOptions.Screw2AngleDegrees, tiltAdapterOptions.ScrewInwardCurvatureSign, FocuserSign);
        public double PhysicalScrew3AngleDegrees => TiltScrewGeometry.PhysicalToStoredAngle(tiltAdapterOptions.Screw3AngleDegrees, tiltAdapterOptions.ScrewInwardCurvatureSign, FocuserSign);
        public double PhysicalScrew4AngleDegrees => TiltScrewGeometry.PhysicalToStoredAngle(tiltAdapterOptions.Screw4AngleDegrees, tiltAdapterOptions.ScrewInwardCurvatureSign, FocuserSign);

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
                RaisePropertyChanged(nameof(StepProgressDisplay));
                RaisePropertyChanged(nameof(StepTitle));
                RaisePropertyChanged(nameof(IsCurrentStepAtBaseline));
                RaisePropertyChanged(nameof(BaselineRecoveryInstructions));
                NotifyCommandsCanExecuteChanged();
            }
        }

        public bool IsComplete => currentStep == WizardStep.Complete;

        // Steps that end with running the aberration inspector (measurement auto-advances)
        public bool IsOnMeasurementStep => activeMeasurementSteps.Contains(currentStep);

        // Task 6: whether THIS run's active sequence includes the optional measured final re-baseline --
        // captured into activeMeasurementSteps at run start (StartAsync/ReplayAsync), not read live off the
        // option, so it stays correct for the whole run even if the option is toggled mid-run. Single named
        // source for the two call sites that need to know whether Complete's own restore move should be
        // suppressed (ReBaseline3 already sent and measured it): the device-instructions text (what the user
        // is told will happen) and ExecuteDeviceMoveForCurrentStepAsync (what actually gets sent) -- both must
        // agree, so both read this one property rather than re-deriving the Contains check separately.
        private bool ActiveRunHasFinalRebaseline => activeMeasurementSteps.Contains(WizardStep.ReBaseline3);

        // "Step N of M" over the active measurement-step set (4, 5, 6, or 7 steps depending on
        // MeasureCurvatureDuringCalibration/MeasureFinalRebaseline, captured at run start). Empty on the
        // terminal Complete panel, which shows its own "Calibration Complete!" header instead.
        public string StepProgressDisplay {
            get {
                if (currentStep == WizardStep.Complete) {
                    return string.Empty;
                }
                int idx = Array.IndexOf(activeMeasurementSteps, currentStep);
                if (idx < 0) {
                    return string.Empty;
                }
                return string.Format(CultureInfo.InvariantCulture, "Step {0} of {1}", idx + 1, activeMeasurementSteps.Length);
            }
        }

        // Short, scannable title shown above the longer StepInstructions paragraph so the user can tell where
        // they are without re-reading the instructions.
        public string StepTitle => StepTitleText(currentStep);

        internal static string StepTitleText(WizardStep step) {
            switch (step) {
                case WizardStep.Baseline: return "Baseline Measurement";
                case WizardStep.AllInward: return "All Screws Inward";
                case WizardStep.ReBaseline1: return "Return to Baseline";
                case WizardStep.Screw1: return "Move Screw 1";
                case WizardStep.ReBaseline2: return "Return to Baseline";
                case WizardStep.Screw2: return "Move Screw 2";
                case WizardStep.Complete: return "Calibration Complete";
                case WizardStep.ReBaseline3: return "Return to Baseline";
                default: return string.Empty;
            }
        }

        // True while ReplayAsync is re-analyzing a saved run. Exposed (INPC) for the same reason as
        // IsAutoRunningAll below: the step copy must drop its "turn the screws / click Run Measurement"
        // imperatives, since a replay touches no hardware and those buttons are collapsed for its duration.
        public bool IsReplaying {
            get => isReplaying;
            private set {
                if (isReplaying != value) {
                    isReplaying = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(StepInstructions));
                }
            }
        }

        // T11: true while Auto Run All is driving the calibration hands-off. Exposed (INPC) so the step copy
        // can drop its "click Run Measurement" imperatives — those buttons are hidden during an automated run.
        public bool IsAutoRunningAll {
            get => isAutoRunningAll;
            private set {
                if (isAutoRunningAll != value) {
                    isAutoRunningAll = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(StepInstructions));
                    RaisePropertyChanged(nameof(StepTitle));
                }
            }
        }

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
            PostToUIThread(() => CameraInfo = deviceInfo);
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            PostToUIThread(() => FocuserInfo = deviceInfo);
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

        public string CurvatureSignDescription {
            get {
                int sign = tiltAdapterOptions.ScrewInwardCurvatureSign;
                if (sign == 0) return string.Empty;
                string arrow = sign == 1 ? "↑" : "↓";
                return tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured ? $"{arrow} (measured)" : $"{arrow} (assumed)";
            }
        }

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

        // Shown after an AutoFocus / sensor-model failure: lets the user re-run AutoFocus or read how to get back to
        // baseline, instead of the bare "Measurement failed." that left the screw-adjustment instruction on screen.
        public bool HasMeasurementFailureChoice {
            get => hasMeasurementFailureChoice;
            private set {
                hasMeasurementFailureChoice = value;
                RaisePropertyChanged();
                NotifyCommandsCanExecuteChanged();
            }
        }

        public string MeasurementFailureText {
            get => measurementFailureText;
            private set {
                measurementFailureText = value;
                RaisePropertyChanged();
            }
        }

        // True when the current step's intended physical state IS the baseline (all screws at their starting
        // position), so the failure panel can say "already at baseline" rather than how to undo a screw move.
        public bool IsCurrentStepAtBaseline => StepIsAtBaseline(currentStep);

        // Per-step guidance for returning to baseline before retrying, shown in the failure panel.
        public string BaselineRecoveryInstructions =>
            BaselineRecoveryText(currentStep, tiltAdapterOptions.ScrewCount, IsStepperAdjustment, CalibrationAppliedAmount);

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

        // Overall signal-to-noise of the calibration (screw-move signal vs the all-inward/re-baseline noise probes).
        // When low, the recovered screw geometry is dominated by measurement noise / drift and should not be applied.
        public bool HasConfidenceWarning {
            get => hasConfidenceWarning;
            private set {
                hasConfidenceWarning = value;
                RaisePropertyChanged();
            }
        }

        public string ConfidenceWarningText {
            get => confidenceWarningText;
            private set {
                confidenceWarningText = value;
                RaisePropertyChanged();
            }
        }

        // Fix 2 / [CRITICAL GATE]: set the moment a "Use Saved AF" measurement occurs mid-run during what
        // started as a device-driven calibration (see MeasureStep) — the run has silently dropped out of
        // device-driven mode, so DeviceLinkedCalibrationDeviceName will NOT be set at Complete and no further
        // device moves will be sent. Survives the step advance to Complete (like HasMeasurementConsistencyWarning),
        // so it is rendered on both the step panel and the Complete panel.
        public bool HasDeviceLinkDroppedWarning {
            get => hasDeviceLinkDroppedWarning;
            private set {
                hasDeviceLinkDroppedWarning = value;
                RaisePropertyChanged();
            }
        }

        public string DeviceLinkDroppedWarningText {
            get => deviceLinkDroppedWarningText;
            private set {
                deviceLinkDroppedWarningText = value;
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

        // T11 item 6: when a motorized device is connected, the wizard drives the moves itself, so the
        // instruction becomes automated status (what the wizard is about to send) instead of the manual
        // "turn screw" wording. Disconnected: EXACTLY the prior expression, unchanged (mandatory regression —
        // IsTiltDeviceConnected is false whenever the service is null or not connected).
        public string StepInstructions =>
            IsReplaying
                ? ReplayStepInstructionsText(currentStep)
                : (IsTiltDeviceConnected && IsMotorizedDevice)
                    ? DeviceStepInstructionsText(currentStep, (int)Math.Round(CalibrationAppliedAmount), IsAutoRunningAll,
                        ActiveRunHasFinalRebaseline)
                    : StepInstructionsText(currentStep, tiltAdapterOptions.ScrewCount, IsStepperAdjustment, CalibrationAppliedAmount);

        // Replay wording: a replay re-analyzes already-captured frames, so every imperative in the live copy
        // ("turn ALL screws CLOCKWISE", "click Run Measurement") is wrong — nothing is captured, no hardware
        // moves, and the measurement buttons are collapsed by the IsMeasuring trigger for the whole replay.
        internal static string ReplayStepInstructionsText(WizardStep step) =>
            $"Replaying — re-analyzing the saved frames for {StepTitleText(step)}. No action needed: nothing is " +
            "captured and the tilt adapter is not moved during a replay.";

        // Automated-status wording for a connected, device-driven run: describes what the wizard will send
        // (Move.Description) rather than what the user must do by hand. Baseline has no move (measurement
        // only); every other step maps 1:1 via EatWizardMapping.MoveForStep -- including Complete, which
        // becomes a no-move ("measuring…" / "Click Run Measurement to continue.") once <paramref
        // name="measuredFinalRebaseline"/> is true, matching the move ExecuteDeviceMoveForCurrentStepAsync
        // will actually (not) send. When <paramref name="autoRunning"/> (Auto Run All is active) the "click
        // Run Measurement" imperatives are dropped — those buttons are hidden during an automated run, so
        // telling the user to click them reads as a stalled manual run.
        internal static string DeviceStepInstructionsText(WizardStep step, int appliedSteps, bool autoRunning, bool measuredFinalRebaseline = false) {
            if (step == WizardStep.Baseline) {
                return autoRunning
                    ? "Running automatically — the wizard is driving the tilt adapter through the calibration. " +
                        "No action needed; it will apply each move, measure, and advance on its own."
                    : "Connected: the wizard will drive the tilt adapter through each calibration step automatically. " +
                        "Ensure the device is at its starting position (as zeroed in the vendor app), then click Run Measurement " +
                        "or Auto Run All to begin.";
            }
            var move = EatWizardMapping.MoveForStep(step, appliedSteps, measuredFinalRebaseline);
            if (autoRunning) {
                return move == null
                    ? "Running automatically — measuring…"
                    : $"Running automatically — {move.Description}. The wizard applies this move, measures, and advances without further input.";
            }
            return move == null
                ? "Click Run Measurement to continue."
                : $"Automated: {move.Description}. Click Run Measurement (or Auto Run All) to apply this move and measure.";
        }

        // Formats the calibration move amount: "1 full turn" / "1.5 turns" for screws, whole "+N"
        // magnitude for steppers (sign is added by the caller's wording).
        internal static string FormatAppliedAmount(bool isStepper, double amount) =>
            isStepper ? $"{amount:0.##}" : (amount == 1.0 ? "1 full turn" : $"{amount:0.##} turns");

        // All wizard prompts. Screw motion is worded as CLOCKWISE/COUNTER-CLOCKWISE (tighten/loosen)
        // — never "inward/outward", which this plugin reserves for adapter-plate motion. Stepper
        // prompts use signed steps; "+" is the direction the guidance later reports as positive.
        internal static string StepInstructionsText(WizardStep step, int screwCount, bool isStepper, double appliedAmount) {
            string amt = FormatAppliedAmount(isStepper, appliedAmount);
            bool four = screwCount == 4;
            switch (step) {
                case WizardStep.Baseline:
                    return (four
                            ? "Label your screws 1, 2, 3, and 4 in a consistent clockwise order. "
                            : "Label your screws 1, 2, and 3 in a consistent clockwise order. ") +
                        "Screw 1 does NOT need to be at any particular clock position — the wizard determines each screw's actual " +
                        "position from the measurements.\n\nEnsure all screws are at their starting position, then click Run Measurement to take a baseline reading.";
                case WizardStep.AllInward:
                    return isStepper
                        ? $"Apply +{amt} steps to EVERY motor, then click Run Measurement."
                        : $"Turn ALL screws CLOCKWISE (tighten) exactly {amt} each, then click Run Measurement.";
                case WizardStep.ReBaseline1:
                    return isStepper
                        ? $"Apply −{amt} steps to every motor, returning to the baseline position, then click Run Measurement."
                        : $"Turn ALL screws back COUNTER-CLOCKWISE (loosen) exactly {amt} each, returning to the baseline position, then click Run Measurement.";
                case WizardStep.Screw1:
                    if (isStepper) {
                        return four
                            ? $"Apply +{amt} steps to motor 1 and −{amt} steps to motor 3, then click Run Measurement."
                            : $"Apply +{amt} steps to motor 1, then click Run Measurement.";
                    }
                    return four
                        ? $"Turn screw 1 CLOCKWISE and screw 3 COUNTER-CLOCKWISE exactly {amt} each, then click Run Measurement."
                        : $"Turn screw 1 CLOCKWISE exactly {amt}, then click Run Measurement.";
                case WizardStep.ReBaseline2:
                    if (isStepper) {
                        return four
                            ? $"Apply −{amt} steps to motor 1 and +{amt} steps to motor 3, returning to the baseline position, then click Run Measurement."
                            : $"Apply −{amt} steps to motor 1, returning to the baseline position, then click Run Measurement.";
                    }
                    return four
                        ? $"Turn screw 1 back COUNTER-CLOCKWISE and screw 3 back CLOCKWISE exactly {amt} each, returning to the baseline position, then click Run Measurement."
                        : $"Turn screw 1 back COUNTER-CLOCKWISE exactly {amt}, returning to the baseline position, then click Run Measurement.";
                case WizardStep.Screw2:
                    if (isStepper) {
                        return four
                            ? $"Apply +{amt} steps to motor 2 and −{amt} steps to motor 4, then click Run Measurement."
                            : $"Apply +{amt} steps to motor 2, then click Run Measurement.";
                    }
                    return four
                        ? $"Turn screw 2 CLOCKWISE and screw 4 COUNTER-CLOCKWISE exactly {amt} each, then click Run Measurement."
                        : $"Turn screw 2 CLOCKWISE exactly {amt}, then click Run Measurement.";
                case WizardStep.ReBaseline3:
                    // Optional (Task 6): a MEASURED restore of the screw-2 move, giving screw 2 the same
                    // drift-cancelling re-baseline symmetry screw 1 already gets from ReBaseline1/ReBaseline2.
                    if (isStepper) {
                        return four
                            ? $"Apply −{amt} steps to motor 2 and +{amt} steps to motor 4, returning to the baseline position, then click Run Measurement."
                            : $"Apply −{amt} steps to motor 2, returning to the baseline position, then click Run Measurement.";
                    }
                    return four
                        ? $"Turn screw 2 back COUNTER-CLOCKWISE and screw 4 back CLOCKWISE exactly {amt} each, returning to the baseline position, then click Run Measurement."
                        : $"Turn screw 2 back COUNTER-CLOCKWISE exactly {amt}, returning to the baseline position, then click Run Measurement.";
                case WizardStep.Complete:
                    return isStepper
                        ? "Return all motors to their original position."
                        : "Restore all screws to their original position.";
                default:
                    return string.Empty;
            }
        }

        // True for steps whose intended physical state is the baseline (all screws at the starting position).
        internal static bool StepIsAtBaseline(WizardStep step) {
            switch (step) {
                case WizardStep.Baseline:
                case WizardStep.ReBaseline1:
                case WizardStep.ReBaseline2:
                case WizardStep.Complete:
                case WizardStep.ReBaseline3:
                    return true;
                default:
                    return false;
            }
        }

        // Instructions for undoing the current step's screw move to return to baseline (mirrors the ReBaseline /
        // Complete wording in StepInstructions). For a step already at baseline, says so instead.
        internal static string BaselineRecoveryText(WizardStep step, int screwCount, bool isStepper, double appliedAmount) {
            string amt = FormatAppliedAmount(isStepper, appliedAmount);
            bool four = screwCount == 4;
            switch (step) {
                case WizardStep.AllInward:
                    return isStepper
                        ? $"Apply −{amt} steps to every motor, returning to the baseline position."
                        : $"Turn ALL screws back COUNTER-CLOCKWISE exactly {amt} each, returning to the baseline position.";
                case WizardStep.Screw1:
                    if (isStepper) {
                        return four
                            ? $"Apply −{amt} steps to motor 1 and +{amt} steps to motor 3, returning to the baseline position."
                            : $"Apply −{amt} steps to motor 1, returning to the baseline position.";
                    }
                    return four
                        ? $"Turn screw 1 back COUNTER-CLOCKWISE and screw 3 back CLOCKWISE exactly {amt} each, returning to the baseline position."
                        : $"Turn screw 1 back COUNTER-CLOCKWISE exactly {amt}, returning to the baseline position.";
                case WizardStep.Screw2:
                    if (isStepper) {
                        return four
                            ? $"Apply −{amt} steps to motor 2 and +{amt} steps to motor 4, returning to the baseline position."
                            : $"Apply −{amt} steps to motor 2, returning to the baseline position.";
                    }
                    return four
                        ? $"Turn screw 2 back COUNTER-CLOCKWISE and screw 4 back CLOCKWISE exactly {amt} each, returning to the baseline position."
                        : $"Turn screw 2 back COUNTER-CLOCKWISE exactly {amt}, returning to the baseline position.";
                default:
                    return "All screws should already be at the baseline (starting) position.";
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
        public ICommand RetryMeasurementCommand { get; }
        public ICommand ApplyManualCalibrationCommand { get; }
        public ICommand ClearCalibrationCommand { get; }
        public ICommand RefreshPortsCommand { get; }
        public ICommand ConnectDeviceCommand { get; }
        public ICommand DisconnectDeviceCommand { get; }
        public ICommand AutoRunAllCommand { get; }

        // Amount the user moves each screw during the per-screw calibration steps (full turns for
        // screws, steps for steppers). Persisted per profile via tiltAdapterOptions; -1 (unset) resolves
        // to the selected device preset's default (TiltAdapterDevicePreset.DefaultCalibrationAmount) so
        // it still matches the "1 full turn" / "150 steps" instructions before the user ever edits it.
        public double CalibrationAppliedAmount {
            get {
                var raw = tiltAdapterOptions.CalibrationAppliedAmount;
                return raw >= 0 ? raw : TiltAdapterDevicePreset.ByName(tiltAdapterOptions.DeviceName).DefaultCalibrationAmount;
            }
            set {
                if (value != tiltAdapterOptions.CalibrationAppliedAmount) {
                    tiltAdapterOptions.CalibrationAppliedAmount = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));
                    // The prompts embed the applied amount, so editing it must refresh them.
                    RaisePropertyChanged(nameof(StepInstructions));
                    RaisePropertyChanged(nameof(BaselineRecoveryInstructions));
                }
            }
        }

        public bool IsStepperAdjustment => tiltAdapterOptions.AdjustmentType == TiltAdjustmentType.StepperMotors;

        /// <summary>
        /// The display-only focuser convention k as a sign: +1 standard, −1 reversed. Read only by the
        /// mechanical wording below and by the physical⇄stored angle conversions; it never reaches the
        /// calibration math (docs/focuser-direction-convention-design.md §2.2).
        /// </summary>
        private int FocuserSign =>
            inspector?.InspectorOptions != null && inspector.InspectorOptions.FocuserIncreasesTowardObjective ? -1 : 1;

        // Mechanical framing of ScrewInwardCurvatureSign: does a CW screw turn (or +steps) move the
        // adapter plate toward the objective? Editing writes the sign (and marks it assumed); a
        // 6-step wizard measurement overwrites the sign and this re-reads it.
        //
        // The stored σ fuses the adapter mechanics and the focuser convention, σ = m·sign(k), so translating
        // it into this mechanical question needs k on BOTH sides: the getter shows m = σ·sign(k), and for the
        // combo to round-trip the setter stores σ = m·sign(k).
        //
        // THE ONE DELIBERATE EXCEPTION to "k never influences motion" (design §2.3, accepted by the user
        // 2026-08-04). Its exposure is narrow: it writes only the ASSUMED σ, which every guidance surface
        // flags "(assumed)" and any 6-step measurement overwrites; at the default k = +1 it is bit-identical
        // to the previous behavior; and on a genuinely reversed rig it makes the manual path CORRECT where a
        // pinned k = +1 conversion would silently store an inverted σ for an honest answer about m. Without
        // it the UI contradicts itself on reversed rigs — the user picks "toward the objective" and the
        // readback immediately says "toward the camera". Accepted residual risk: a user who sets k wrong AND
        // sets the direction by hand instead of running the 6-step wizard gets inverted motion. Do NOT widen
        // this; the only other sanctioned path is TiltScrewGeometry.PhysicalToStoredAngle (§7.4).
        public bool CwMovesAdapterTowardObjective {
            get {
                int sign = tiltAdapterOptions.ScrewInwardCurvatureSign;
                if (sign == 0) sign = TiltScrewGeometry.DefaultScrewInwardCurvatureSign;
                return TiltScrewGeometry.CwMovesAdapterTowardObjectiveForSign(sign * FocuserSign);
            }
            set {
                int sign = TiltScrewGeometry.CurvatureSignForCwDirection(value) * FocuserSign;
                if (tiltAdapterOptions.ScrewInwardCurvatureSign != sign) {
                    tiltAdapterOptions.ScrewInwardCurvatureSign = sign;
                    tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured = false;
                    RaisePropertyChanged();
                }
            }
        }

        // The visible direction-row label is now composed in XAML (a "Screw ⟳ moves adapter" /
        // "+ steps move adapter" icon+text row). This property is retained because a profile swap
        // must still raise it (TiltAdapterWizardVMTests asserts the notification); the strings below
        // are kept aligned with the XAML wording for any future textual use.
        public string CwDirectionLabel => IsStepperAdjustment
            ? "+ steps move adapter"
            : "Screw turn moves adapter";

        public string CurvatureSignProvenance =>
            tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured
                ? "Direction was measured by a calibration run."
                : tiltAdapterOptions.MeasureCurvatureDuringCalibration
                    ? "Direction will be measured on the next calibration run."
                    : "Direction is assumed — enable the measurement below (it adds the all-screws steps to the run) to verify it.";

        private double manualScrew1AngleDegrees;

        // Manual Calibration Entry: screw 1 PHYSICAL position angle in degrees, image space, 0° =
        // straight up (12 o'clock), increasing clockwise — the plugin-wide convention. Converted to
        // the wizard's stored response convention on Apply (TiltScrewGeometry.PhysicalToStoredAngle),
        // using the adapter-direction sign at Apply time.
        public double ManualScrew1AngleDegrees {
            get => manualScrew1AngleDegrees;
            set {
                if (manualScrew1AngleDegrees != value) {
                    manualScrew1AngleDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool manualNumberingClockwise = true;

        // Whether screws 2..n proceed clockwise from screw 1 in the IMAGE (mirrors can flip it).
        public bool ManualNumberingClockwise {
            get => manualNumberingClockwise;
            set {
                if (manualNumberingClockwise != value) {
                    manualNumberingClockwise = value;
                    RaisePropertyChanged();
                }
            }
        }

        // Applies a manually entered calibration: same persisted state a wizard run writes, tagged
        // manual. The curvature sign is whatever the direction setting above holds (assumed).
        internal void ApplyManualCalibration() {
            // WPF double bindings can push NaN/Infinity; never persist IsCalibrated over all-NaN angles.
            if (!double.IsFinite(manualScrew1AngleDegrees)) {
                Notification.ShowWarning("Enter a valid screw 1 position angle (in degrees) before applying a manual calibration.");
                return;
            }
            int n = tiltAdapterOptions.ScrewCount;
            // The user types the PHYSICAL image angle, but wizard runs persist RESPONSE-convention
            // angles (the direction a CW turn drives the tilt gradient — 180° from physical on
            // sign = -1 rigs), and the guidance math consumes stored angles as response-convention.
            // Convert with the adapter-direction sign in effect now; if the user changes that setting
            // later they must click Apply again (the conversion is not retroactive).
            double stored1 = TiltScrewGeometry.PhysicalToStoredAngle(manualScrew1AngleDegrees, tiltAdapterOptions.ScrewInwardCurvatureSign, FocuserSign);
            var (s1, s2, s3, s4) = TiltCalibrationCalculator.ComputeManualScrewAngles(stored1, manualNumberingClockwise, n);
            tiltAdapterOptions.Screw1AngleDegrees = s1;
            tiltAdapterOptions.Screw2AngleDegrees = s2;
            tiltAdapterOptions.Screw3AngleDegrees = s3;
            tiltAdapterOptions.Screw4AngleDegrees = s4;
            tiltAdapterOptions.CalibratedScrewCount = n;
            tiltAdapterOptions.IsCalibrated = true;
            tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured = false;
            tiltAdapterOptions.CalibrationIsManual = true;
            // [CRITICAL GATE] A manual entry's screw numbering/orientation is not guaranteed to match how the
            // device's motors are wired — clear any device-linked marker so automation (the wizard's
            // hands-off calibration and the inspector's Automatic Adjustment, T14) stays blocked until a
            // fresh connected, device-driven calibration re-establishes the correspondence.
            tiltAdapterOptions.DeviceLinkedCalibrationDeviceName = string.Empty;
            // [CRITICAL GATE] A manual entry has no confidence computation to reuse (there are no per-step
            // tilt vectors — the user typed a single angle) — conservative default: not automation-trusted
            // until a fresh calibration run demonstrably passes its own confidence check.
            tiltAdapterOptions.CalibrationIsReliable = false;
            // A manual entry supersedes whatever wizard run last measured the hardware — reset to the
            // unset sentinel (-1, what the options initialize to; PitchMismatchExceeds ignores <= 0) so
            // the inspector's pitch-mismatch warning can't compare the new adapter's configured pitch
            // against a stale measurement from a previous adapter.
            tiltAdapterOptions.LastMeasuredThreadPitchMicrons = -1;
            tiltAdapterOptions.LastMeasuredStepperStepSizeMicrons = -1;
            // Clear stale wizard-run display state (measured-hardware panel, per-run warnings, summary
            // rows — the same state Restart clears) that would otherwise describe the previous run next
            // to a manually entered calibration.
            ResetDerivedCalibrationReadouts();
            HasWarning = false;
            WarningText = string.Empty;
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
            HasRebaselineDriftWarning = false;
            RebaselineDriftWarningText = string.Empty;
            HasConfidenceWarning = false;
            ConfidenceWarningText = string.Empty;
            curvatureChannelDisagreement = null;
            ClearSummaryRows();
            RaiseHardwareSummaryChanged();
            RebuildDiagram();
        }

        /// <summary>
        /// Clears the saved calibration RESULTS — screw angles, the calibrated/manual flags, the calibrated screw
        /// count, the measured-curvature-sign flag, and the measured-hardware sentinels — leaving the device
        /// selection and its configured hardware/direction intact. Reverses <see cref="ApplyManualCalibration"/>'s
        /// field writes and runs the same display-state cleanup, so the pane returns to the "No calibration saved"
        /// state without a wizard run.
        /// </summary>
        private void ClearCalibration() {
            tiltAdapterOptions.Screw1AngleDegrees = double.NaN;
            tiltAdapterOptions.Screw2AngleDegrees = double.NaN;
            tiltAdapterOptions.Screw3AngleDegrees = double.NaN;
            tiltAdapterOptions.Screw4AngleDegrees = double.NaN;
            tiltAdapterOptions.CalibratedScrewCount = 0;
            tiltAdapterOptions.IsCalibrated = false;
            tiltAdapterOptions.CalibrationIsManual = false;
            tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured = false;
            tiltAdapterOptions.LastMeasuredThreadPitchMicrons = -1;
            tiltAdapterOptions.LastMeasuredStepperStepSizeMicrons = -1;

            ResetDerivedCalibrationReadouts();
            HasWarning = false;
            WarningText = string.Empty;
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
            HasRebaselineDriftWarning = false;
            RebaselineDriftWarningText = string.Empty;
            HasConfidenceWarning = false;
            ConfidenceWarningText = string.Empty;
            curvatureChannelDisagreement = null;
            ClearSummaryRows();
            RaiseHardwareSummaryChanged();
            RebuildDiagram();
        }

        // Shared by ApplyManualCalibration, ClearCalibration, and Restart: every path that invalidates the
        // previous run's results without immediately running a new one. Resets exactly the derived-readout
        // state RunCalibrationMath populates from a Calibrate() result -- NOT the warning-text/flag
        // properties around it, which differ slightly per caller (e.g. Restart also clears
        // HasDeviceLinkDroppedWarning, which the other two callers don't touch). Extracted so a field this
        // task (or a future one, e.g. Task 5's corner-AF readouts) adds to the set can't be missed at one of
        // the three call sites and leave a stale readout on screen after Clear/Restart/a manual entry.
        private void ResetDerivedCalibrationReadouts() {
            measuredHardwareMicrons = double.NaN;
            lastConfidence = null;
            pitchUncertaintyMicrons = double.NaN;
            pistonImpliedMicronsPerStep = double.NaN;
            lastRawAngleDiff = double.NaN;
            lastMoveMagnitudeRatio = double.NaN;
            cornerMeasuredHardwareMicrons = double.NaN;
            estimatorRelativeDifference = double.NaN;
        }

        public string WizardSweepSummary {
            get {
                var inspectorOptions = inspector.InspectorOptions;
                if (inspectorOptions == null) return string.Empty;
                return BuildSweepSummary(
                    GetMeasurementSteps(tiltAdapterOptions.MeasureCurvatureDuringCalibration, tiltAdapterOptions.MeasureFinalRebaseline).Length,
                    tiltAdapterOptions.MeasurementAverageCount,
                    inspectorOptions.StepCount,
                    inspectorOptions.FramesPerPoint,
                    inspectorOptions.SignalAmplification,
                    profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps,
                    profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint);
            }
        }

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
        public string CalibrationAppliedAmountDisplay => IsStepperAdjustment ? $"{CalibrationAppliedAmount:0.##} steps" : $"{CalibrationAppliedAmount:0.##} turns";

        public bool HasConfidenceInfo => lastConfidence != null;

        public bool ConfidenceIsReliable => lastConfidence?.IsReliable ?? false;

        public string ConfidenceSummaryDisplay =>
            lastConfidence == null ? string.Empty :
            $"Signal-to-noise {lastConfidence.SignalToNoise:F1} · screw-direction ±{lastConfidence.PredictedAngleUncertaintyDeg:F0}°";

        // Precision follows MeasuredHardwareDisplay's convention rather than a fixed "F0". On a stepper the
        // whole quantity lives near 1 µm/step, so "F0" rounded the screw-to-screw spread to a flat "± 0" --
        // and that spread is the single most diagnostic number the calibration produces: it is what separates
        // "both screws agree" from "one screw's move was measured badly and the mean is hiding it".
        public string PitchUncertaintyDisplay =>
            double.IsNaN(pitchUncertaintyMicrons) ? string.Empty :
            IsStepperAdjustment ? $"± {pitchUncertaintyMicrons:0.###} µm/step"
            : $"± {pitchUncertaintyMicrons:0.#} µm/turn";

        // Piston-implied pitch (frame-factor probe): a free, tilt-fit-independent hardware estimate from the
        // AllInward piston alone (see TiltCalibrationCalculator.PistonImpliedMicronsPerStep). NaN for 4-step
        // runs (no piston measured). The XAML row's visibility is gated on HasPistonPitch below (a
        // standalone, self-labeling row -- not a shared label/value UniformGrid cell like the "Measured"/
        // "Saved" rows above it), not on this string being empty.
        public string PistonPitchDisplay =>
            double.IsNaN(pistonImpliedMicronsPerStep) ? string.Empty
            : IsStepperAdjustment ? $"Piston-implied: {pistonImpliedMicronsPerStep:0.###} µm/step"
            : $"Piston-implied: {pistonImpliedMicronsPerStep:0.#} µm/turn";

        // Gates the piston-pitch row's visibility in DataTemplates.xaml (this file uses DataTrigger-driven
        // Visibility throughout, never converters) — true once a 6-step run has measured a piston.
        public bool HasPistonPitch => !double.IsNaN(pistonImpliedMicronsPerStep);

        // Corner-region AF cross-check (Task 5): the adapter hardware recovered from the inspector's 4-corner
        // region plane instead of the per-star paraboloid, a second independent estimator of the same screw
        // moves (see RunCalibrationMath). Displayed unconditionally whenever it was computable, regardless of
        // whether it agrees with the paraboloid reading — the disagreement warning (ValidateCalibrationQuality)
        // is a separate, additional signal; this row never replaces MeasuredHardwareDisplay above. NaN when
        // the run didn't capture a corner reading for every active step (see StepReading.CornerA).
        public string CornerCrossCheckDisplay =>
            double.IsNaN(cornerMeasuredHardwareMicrons) ? string.Empty
            : $"Corner-AF cross-check: {cornerMeasuredHardwareMicrons:0.###} µm/{(IsStepperAdjustment ? "step" : "turn")}";

        // Gates the corner-cross-check row's visibility in DataTemplates.xaml, same pattern as HasPistonPitch.
        public bool HasCornerCrossCheck => !double.IsNaN(cornerMeasuredHardwareMicrons);

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

        // ---- Motorized device connection (T10) --------------------------------------------------------------
        // Everything below drives the "Motorized Device Connection" GroupBox in Panel A. The pane is only
        // visible when the selected preset has a registered motion controller (the ASG EAT presets); all
        // device access flows through the shared TiltDeviceConnectionService singleton. Deliberately NO
        // zero-positions command anywhere — zeroing the counters is the vendor app's job (user decision #5).

        /// <summary>True when the selected device preset has a registered motion controller (can be connected and automated).</summary>
        public bool IsMotorizedDevice => TiltMotionControllerRegistry.IsMotorized(tiltAdapterOptions.DeviceName);

        public bool IsTiltDeviceConnected => tiltDeviceConnectionService?.Connected ?? false;

        public bool TiltDevicePositionsKnown => (deviceDisplayPositions != null && deviceDisplayPositions.Count >= 4)
            || (tiltDeviceConnectionService?.PositionsKnown ?? false);

        public string TiltDeviceStatusText {
            get {
                var svc = tiltDeviceConnectionService;
                if (svc == null || !svc.Connected) return "Not connected";
                string port = string.IsNullOrEmpty(connectedPortName) ? string.Empty : $" on {connectedPortName}";
                // Surfaces the exclusive-operation name so T11's hands-off calibration (and the inspector's
                // plan execution) get a live status line for free.
                return svc.IsOperationActive && !string.IsNullOrEmpty(svc.CurrentOperationName)
                    ? $"Connected{port} — {svc.CurrentOperationName}"
                    : $"Connected{port}";
            }
        }

        /// <summary>COM ports available for the device connection. Enumerated lazily on first access (the provider's WMI scan only runs when the pane is shown); RefreshPortsCommand re-enumerates.</summary>
        public IReadOnlyList<string> AvailablePortNames {
            get {
                if (availablePortNames == null) {
                    availablePortNames = EnumeratePortNames();
                }
                return availablePortNames;
            }
        }

        /// <summary>The selected COM port, persisted per profile (ITiltAdapterOptions.TiltDeviceSerialPortName).</summary>
        public string SelectedPortName {
            get => tiltAdapterOptions.TiltDeviceSerialPortName;
            set {
                // Fix 4: WPF's Selector coerces ComboBox.SelectedItem — and pushes the coercion back through
                // this two-way binding — to null/empty whenever the bound value isn't present in the CURRENT
                // AvailablePortNames (e.g. the persisted port's device is unplugged when the pane loads, or
                // simply hasn't been enumerated yet). There is no "no selection" item in this ComboBox, so a
                // null/empty incoming value can only ever be that coercion, never a genuine user choice —
                // ignore it rather than wiping a remembered, non-empty persisted port.
                if (string.IsNullOrEmpty(value)) {
                    return;
                }
                if (tiltAdapterOptions.TiltDeviceSerialPortName != value) {
                    tiltAdapterOptions.TiltDeviceSerialPortName = value;
                    RaisePropertyChanged();
                    NotifyCommandsCanExecuteChanged(); // ConnectDeviceCommand gates on a selected port
                }
            }
        }

        // Per-corner position counters in the 2x2 spatial layout of the physical adapter. The service's
        // CurrentPositions list is in DEVICE motor order: [0]=TR (motor 1), [1]=TL (motor 2), [2]=BR (motor 3),
        // [3]=BL (motor 4). "unknown" whenever PositionsKnown is false (e.g. the cp response is not parseable
        // yet) or the index is missing.
        public string ScrewPositionTopRightDisplay => TiltDevicePositionDisplay(0);
        public string ScrewPositionTopLeftDisplay => TiltDevicePositionDisplay(1);
        public string ScrewPositionBottomRightDisplay => TiltDevicePositionDisplay(2);
        public string ScrewPositionBottomLeftDisplay => TiltDevicePositionDisplay(3);

        private string TiltDevicePositionDisplay(int deviceMotorIndex) {
            // Prefer the wizard's per-move snapshot (kept fresh during a run, when the service poll is paused);
            // fall back to the service's polled positions when idle.
            IReadOnlyList<int> positions = deviceDisplayPositions;
            if (positions == null || positions.Count <= deviceMotorIndex) {
                var svc = tiltDeviceConnectionService;
                positions = (svc != null && svc.PositionsKnown) ? svc.CurrentPositions : null;
            }
            if (positions == null || positions.Count <= deviceMotorIndex) return "unknown";

            var pos = positions[deviceMotorIndex];
            var text = pos.ToString(CultureInfo.InvariantCulture);
            // Delta from the position captured when this calibration run started.
            if (calibrationBaselinePositions != null && calibrationBaselinePositions.Length > deviceMotorIndex) {
                var delta = pos - calibrationBaselinePositions[deviceMotorIndex];
                text += $"  (Δ {delta.ToString("+0;-0;0", CultureInfo.InvariantCulture)})";
            }
            return text;
        }

        private void RaiseScrewPositionDisplays() {
            RaisePropertyChanged(nameof(TiltDevicePositionsKnown));
            RaisePropertyChanged(nameof(ScrewPositionTopRightDisplay));
            RaisePropertyChanged(nameof(ScrewPositionTopLeftDisplay));
            RaisePropertyChanged(nameof(ScrewPositionBottomRightDisplay));
            RaisePropertyChanged(nameof(ScrewPositionBottomLeftDisplay));
        }

        // Refresh the wizard's live device-position snapshot from the controller's own latest counters, and
        // publish them through the connection service so every other panel (e.g. the inspector's) updates too.
        // Used during a run, when the service's cp poll is paused (the run holds the operation lease).
        // Optionally (re)captures the "delta since start" baseline.
        //
        // Deliberately NO device I/O: a move response already carries the device's fresh counters (see
        // EatTiltMotionController.ExecuteMoveAsync), so a follow-up 'cp' per move was both a wasted ~1 s round
        // trip and an extra failure point — one that failed silently, freezing the position and Δ display at
        // its pre-move values with nothing in the log to say so.
        private void RefreshRunDevicePositions(bool captureBaseline) {
            var positions = tiltDeviceConnectionService?.PublishControllerPositions();
            if (positions == null || positions.Count < 4) {
                return; // Already logged by the service; keep the previous snapshot rather than blanking the display.
            }
            deviceDisplayPositions = positions;
            if (captureBaseline || calibrationBaselinePositions == null) {
                calibrationBaselinePositions = positions.ToArray();
            }
            RaiseScrewPositionDisplays();
        }

        private IReadOnlyList<string> EnumeratePortNames() {
            // The simulated adapter is always offered first (and even if port enumeration fails), so a user with
            // no hardware can still connect it. It's only ever visible when an EAT preset is selected -- the whole
            // connection pane is gated on IsMotorizedDevice -- which is exactly when connecting the sim EAT applies.
            var result = new List<string> { SimulatedTiltPort.PortName };
            try {
                // Created here (not in the ctor): SerialPortProvider's constructor runs a WMI scan.
                serialPortProvider ??= new SerialPortProvider();
                result.AddRange(serialPortProvider.GetPortNames(deviceQuery: null, addDivider: false, addGenericPorts: true));
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to enumerate serial ports for the tilt device connection");
            }
            return result;
        }

        private void RefreshPorts() {
            availablePortNames = EnumeratePortNames();
            RaisePropertyChanged(nameof(AvailablePortNames));
            // Fix 4: a persisted port not previously enumerated (its device was unplugged) survives in the
            // options (SelectedPortName's setter guard above never wipes it) but the ComboBox may still be
            // showing no selection from that earlier mismatch — re-raise so it retries to visually select the
            // persisted port now that a refresh may have brought it back into AvailablePortNames.
            RaisePropertyChanged(nameof(SelectedPortName));
        }

        private async Task ConnectTiltDeviceAsync() {
            var svc = tiltDeviceConnectionService;
            var port = SelectedPortName;
            if (svc == null || string.IsNullOrEmpty(port)) return;

            // Connecting the SIMULATOR port has extra preconditions (active sim camera, matching sim config,
            // aberrations on); if any is unmet and the user declines to fix it, abort before touching the service.
            if (SimulatedTiltPort.IsSimulator(port) && !await PrepareSimulatorConnectionAsync()) {
                return;
            }

            try {
                await svc.ConnectAsync(tiltAdapterOptions.DeviceName, port, CancellationToken.None);
                connectedPortName = port;
                // The SelectedPortName setter already persisted the port; re-assert here so a port that
                // actually connected is always the one saved, whatever path selected it.
                tiltAdapterOptions.TiltDeviceSerialPortName = port;
                // T11 item 4: default the 6-step (curvature-measuring) flow ON the moment a device connects,
                // so a hands-off calibration measures sigma via the real `bf` command by default (the exact
                // command automation later replays) instead of relying on the assumed/manual direction.
                // Fix 3: this is a DEFAULT applied at most ONCE per VM lifetime (this VM is a
                // [PartCreationPolicy(CreationPolicy.Shared)] singleton, so "lifetime" == this session), and
                // never when the user has explicitly disabled it (before OR after a prior connect) — without
                // both guards, a disconnect -> reconnect cycle would silently re-force a deliberately disabled
                // option back on every time (the original bug: this ran on EVERY connect).
                if (!hasDefaultedMeasureCurvatureOnConnect) {
                    hasDefaultedMeasureCurvatureOnConnect = true;
                    if (!tiltAdapterOptions.MeasureCurvatureDuringCalibration && !userExplicitlyDisabledMeasureCurvatureThisSession) {
                        tiltAdapterOptions.MeasureCurvatureDuringCalibration = true;
                        // Surface the silent lengthening of the run so the extra "all screws" steps aren't confusing.
                        // Don't quote a step count here: the run length also depends on MeasureFinalRebaseline.
                        Notification.ShowInformation("Enabled direction measurement for the connected device — it adds the " +
                            "all-screws steps to the run, and is recommended for hands-off runs. You can turn it off under " +
                            "the measurement settings.");
                    }
                }
                RaisePropertyChanged(nameof(TiltDeviceStatusText));
                RaisePropertyChanged(nameof(StepInstructions));
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to connect to the tilt adapter device on {port}");
                Notification.ShowError($"Failed to connect to the tilt adapter device on {port}: {ex.Message}");
            }
        }

        private async Task DisconnectTiltDeviceAsync() {
            var svc = tiltDeviceConnectionService;
            if (svc == null) return;
            try {
                await svc.DisconnectAsync();
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to disconnect the tilt adapter device");
                Notification.ShowError($"Failed to disconnect the tilt adapter device: {ex.Message}");
            }
        }

        private void TiltDeviceConnectionService_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            // The service raises INPC from its polling/idle timer threads; marshal without blocking them
            // (same rationale as the device-info broadcasts, see PostToUIThread).
            PostToUIThread(() => {
                if (e.PropertyName == nameof(TiltDeviceConnectionService.Connected)) {
                    // Fix 4: clear the remembered connected-port display field on EVERY disconnect (user-
                    // initiated, an idle-timeout forced disconnect, or a connection loss) -- not just the
                    // explicit Disconnect button -- so a later reconnect (possibly to a different port) never
                    // has a stale value to leak into TiltDeviceStatusText.
                    if (!IsTiltDeviceConnected) {
                        connectedPortName = string.Empty;
                        // A disconnect invalidates the live positions and the delta baseline.
                        deviceDisplayPositions = null;
                        calibrationBaselinePositions = null;
                        RaiseScrewPositionDisplays();
                    }
                    RaisePropertyChanged(nameof(IsTiltDeviceConnected));
                    RaisePropertyChanged(nameof(TiltDeviceStatusText));
                    RaisePropertyChanged(nameof(StepInstructions));
                    NotifyCommandsCanExecuteChangedCore();
                }
                if (e.PropertyName == nameof(TiltDeviceConnectionService.CurrentPositions) ||
                    e.PropertyName == nameof(TiltDeviceConnectionService.PositionsKnown)) {
                    // Idle poll update (paused during a run — then per-move RefreshRunDevicePositionsAsync drives this).
                    var svc = tiltDeviceConnectionService;
                    if (svc != null && svc.PositionsKnown && svc.CurrentPositions?.Count >= 4) {
                        deviceDisplayPositions = svc.CurrentPositions.ToArray();
                    }
                    RaiseScrewPositionDisplays();
                }
                if (e.PropertyName == nameof(TiltDeviceConnectionService.IsOperationActive) ||
                    e.PropertyName == nameof(TiltDeviceConnectionService.CurrentOperationName)) {
                    RaisePropertyChanged(nameof(TiltDeviceStatusText));
                }
            });
        }

        // Test-observability hook for the fire-and-forget idle-prompt handling (mirrors the service's
        // LastForcedDisconnectTask): tests await it for deterministic completion.
        internal Task LastIdlePromptTask { get; private set; }

        private void TiltDeviceConnectionService_IdlePromptRequested(object sender, EventArgs e) {
            // Fires on the service's timer thread; the modal must be shown from the UI thread. Post (never
            // block the timer thread) and let the async handler route the answer back to the service.
            PostToUIThread(() => LastIdlePromptTask = HandleIdlePromptRequestedAsync());
        }

        private async Task HandleIdlePromptRequestedAsync() {
            var svc = tiltDeviceConnectionService;
            if (svc == null) return;
            bool disconnect;
            try {
                disconnect = await confirmIdleDisconnectAsync();
            } catch (Exception ex) {
                // Treat a failed prompt as "keep connected" but still resolve it, so the service re-arms
                // rather than suppressing every future idle prompt behind a permanently-outstanding one.
                Logger.Error(ex, "Tilt device idle-disconnect prompt failed; keeping the device connected");
                svc.KeepConnectedResetIdle();
                return;
            }
            try {
                if (disconnect) {
                    await svc.ConfirmIdleDisconnectAsync();
                } else {
                    svc.KeepConnectedResetIdle();
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Tilt device idle disconnect failed");
            }
        }

        // Production idle prompt: NINA's message box (it marshals onto the application dispatcher itself,
        // and the caller is already posted to the UI thread). Default answer is No — never disconnect the
        // hardware because a dialog was dismissed.
        private Task<bool> ShowIdleDisconnectPromptAsync() {
            var result = MyMessageBox.Show(
                "The tilt adapter device has been connected but idle for 30 minutes. Disconnect it?",
                "Tilt Adapter Device Idle",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxResult.No);
            return Task.FromResult(result == System.Windows.MessageBoxResult.Yes);
        }

        // Pre-connect checks for the "Simulator" port. Returns false to ABORT the connect. The simulated adapter
        // only affects the HocusFocus camera simulator's images, its geometry must match the selected EAT preset
        // for the calibration loop to converge, and aberration rendering must be on or the loop is inert.
        private async Task<bool> PrepareSimulatorConnectionAsync() {
            // 1) Block unless the HocusFocus camera simulator is the active, connected camera.
            var camInfo = CameraInfo;
            if (camInfo == null || !camInfo.Connected ||
                !string.Equals(camInfo.DeviceId, HocusFocusSimulatorCamera.DeviceId, StringComparison.Ordinal)) {
                Notification.ShowError(
                    "Connect the HocusFocus camera simulator before connecting the simulated tilt adapter — " +
                    "the simulated adapter only changes the HocusFocus simulator's images.");
                return false;
            }

            // No simulator options wired (device-less test rig): nothing to check or fix, allow the connect.
            if (cameraSimulatorOptions == null) {
                return true;
            }

            // 2) The simulator's tilt-adapter geometry must match the selected EAT preset or the loop can't
            //    converge. If it differs, offer to change the simulator config to match; if declined, don't connect.
            var preset = TiltAdapterDevicePreset.ByName(tiltAdapterOptions.DeviceName);
            if (SimConfigDiffersFromPreset(preset)) {
                var apply = await confirmSimConfigChangeAsync(
                    "The camera simulator's tilt-adapter configuration (screw count, motor/screw type, step size, " +
                    "screw radius) differs from the selected EAT preset, so the automated calibration loop can't " +
                    "converge. Change the simulator configuration to match and connect?",
                    "Simulated Tilt Adapter Configuration");
                if (!apply) {
                    return false;
                }
                // Mirror ApplyDevice's preset -> options copy, into the Sim* fields. Screw angles and adapter
                // direction are deliberately NOT synced: the device-linked calibration measures them, and forcing
                // them would both be discarded and remove the coverage the simulator exists to provide.
                cameraSimulatorOptions.SimScrewCount = preset.ScrewCount;
                cameraSimulatorOptions.SimAdjustmentType = preset.AdjustmentType;
                cameraSimulatorOptions.SimStepperStepSizeMicrons = preset.StepperStepSizeMicrons;
                cameraSimulatorOptions.SimScrewRadiusMillimeters = preset.ScrewRadiusMillimeters;
            }

            // 3) Aberration rendering must be on or every simulated exposure is flat and the loop measures nothing.
            if (!cameraSimulatorOptions.EnableAberrations) {
                cameraSimulatorOptions.EnableAberrations = true;
                Notification.ShowInformation(
                    "Enabled camera-simulator aberrations so the tilt calibration loop can measure changes.");
            }

            return true;
        }

        // Compares only the four hardware fields the user cares about (screw count, motor/screw type, step size,
        // screw radius) between the simulator config and the selected EAT preset.
        private bool SimConfigDiffersFromPreset(TiltAdapterDevicePreset preset) =>
            cameraSimulatorOptions.SimScrewCount != preset.ScrewCount ||
            cameraSimulatorOptions.SimAdjustmentType != preset.AdjustmentType ||
            !HardwareValuesMatch(cameraSimulatorOptions.SimStepperStepSizeMicrons, preset.StepperStepSizeMicrons) ||
            !HardwareValuesMatch(cameraSimulatorOptions.SimScrewRadiusMillimeters, preset.ScrewRadiusMillimeters);

        // Tolerant compare for the hardware doubles (step size, radius): a benign rounding difference must not force
        // the prompt. After a "Yes" the fields are set from the preset verbatim, so a later connect matches exactly.
        private static bool HardwareValuesMatch(double a, double b) {
            if (a <= 0 || b <= 0) {
                return a <= 0 && b <= 0;
            }
            return Math.Abs(a - b) / b <= 0.001;
        }

        // Production "change the simulator config?" prompt: NINA's Yes/No message box, default No (a dismissed
        // dialog must never silently rewrite the simulator's configuration).
        private static Task<bool> ShowSimConfigChangePromptAsync(string message, string title) {
            // Wrapped: this message enumerates every mismatched simulator setting on one line, and MyMessageBox
            // does not wrap — an unwrapped long line stretches the modal past the screen edge. See DialogText.
            var result = MyMessageBox.Show(
                DialogText.Wrap(message), title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxResult.No);
            return Task.FromResult(result == System.Windows.MessageBoxResult.Yes);
        }

        // ---- End motorized device connection ----------------------------------------------------------------

        // ---- T11: hands-off device-driven calibration --------------------------------------------------------

        /// <summary>
        /// Sends the device move that realizes <paramref name="step"/>'s instruction (see
        /// <see cref="EatWizardMapping.MoveForStep"/>) and awaits its completion. No-op (returns true
        /// immediately) for <see cref="WizardStep.Baseline"/>, which is measurement-only — no move. On
        /// failure or cancellation, surfaces the error through the existing measurement-failure panel
        /// (<see cref="HasMeasurementFailureChoice"/> / <see cref="MeasurementFailureText"/>) and returns
        /// false; callers must not proceed to a measurement for this step when this returns false.
        ///
        /// Recovery (sending <see cref="EatWizardMapping.InverseMove"/>) is attempted only when the device
        /// may actually have moved: per <c>EatTiltMotionController.ExecuteMoveAsync</c>'s documented
        /// exception taxonomy, a <c>TiltDeviceLimitException</c> or an <see cref="OperationCanceledException"/>
        /// both mean NOTHING was sent (every soft-limit check and the single cancellation boundary run
        /// strictly before any device I/O) — inverting in that case would send a spurious, physically real
        /// move for a step that never happened. Any other exception (a command-failed/ambiguous-ack timeout,
        /// or the serial port closing) means a command WAS sent and its outcome is ambiguous ("state-dirty"),
        /// so a best-effort inverse is worth attempting. This mirrors the same taxonomy
        /// InspectorVM's Automatic Adjustment (T14) journal-revert path already relies on.
        /// </summary>
        internal async Task<bool> ExecuteDeviceMoveForCurrentStepAsync(WizardStep step, CancellationToken ct) {
            if (step == WizardStep.Baseline) {
                return true;
            }
            var controller = tiltDeviceConnectionService?.Controller;
            if (controller == null) {
                StatusText = string.Empty;
                MeasurementFailureText = "The tilt adapter device is not connected. Reconnect it before continuing this automated calibration.";
                HasMeasurementFailureChoice = true;
                return false;
            }

            int appliedSteps = (int)Math.Round(CalibrationAppliedAmount);
            // When ActiveRunHasFinalRebaseline, Complete becomes a no-move — ReBaseline3 (an ordinary step
            // earlier in this same sequence) already sent and measured the restore, so sending it again here
            // would be a second, spurious, physically real move.
            var move = EatWizardMapping.MoveForStep(step, appliedSteps, ActiveRunHasFinalRebaseline);
            if (move == null) {
                return true; // Baseline, or Complete after a measured ReBaseline3 already restored the device.
            }

            try {
                if (calibrationBaselinePositions == null) {
                    // Capture the "delta since start" baseline before this run's first move.
                    RefreshRunDevicePositions(captureBaseline: true);
                }
                progress.Report(new ApplicationStatus { Status = $"Tilt Adapter Wizard: {move.Description}" });
                StatusText = move.Description;
                await controller.ExecuteMoveAsync(move, moveProgress, ct).ConfigureAwait(true);
                appliedDeviceMovesThisRun.Add(move);
                // Refresh the live position display + delta after the move (the poll is paused during the run).
                RefreshRunDevicePositions(captureBaseline: false);
                return true;
            } catch (Exception ex) {
                Logger.Error(ex, $"Tilt adapter device move failed for wizard step {step}.");
                bool deviceMayHaveMoved = !(ex is TiltDeviceLimitException) && !(ex is OperationCanceledException);
                if (deviceMayHaveMoved) {
                    await TryRecoverFailedMoveAsync(controller, move).ConfigureAwait(true);
                }
                StatusText = string.Empty;
                MeasurementFailureText = BuildDeviceMoveFailureText(move, ex, deviceMayHaveMoved);
                HasMeasurementFailureChoice = true;
                return false;
            } finally {
                // Clear the transient bottom-left status. moveProgress leaves the last move step (e.g.
                // "bf,150 complete") showing, which otherwise lingers in NINA's status bar after the command
                // finishes; the next step (or its AutoFocus run) reports its own status afresh.
                progress.Report(new ApplicationStatus { Status = string.Empty });
            }
        }

        // Best-effort single-move recovery after a move that may have reached the device: sends the inverse
        // of the move that just failed so the device returns to its position before this step. Always uses
        // CancellationToken.None — a recovery attempt must never itself be skipped due to cancellation.
        private async Task TryRecoverFailedMoveAsync(ITiltMotionController controller, TiltAdapterMove move) {
            var inverse = EatWizardMapping.InverseMove(move);
            if (inverse == null) {
                return;
            }
            try {
                await controller.ExecuteMoveAsync(inverse, moveProgress, CancellationToken.None).ConfigureAwait(true);
                RefreshRunDevicePositions(captureBaseline: false);
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to recover tilt adapter device position after a failed move; the device may not be at its expected position.");
            }
        }

        private static string BuildDeviceMoveFailureText(TiltAdapterMove move, Exception ex, bool recoveryAttempted) {
            string reason = ex is OperationCanceledException ? "was cancelled before it was sent"
                : ex is TiltDeviceLimitException ? $"was refused: {ex.Message}"
                : $"failed: {ex.Message}";
            string recovery = recoveryAttempted
                ? " The device was sent the inverse move to return it to its position before this step; verify positions before continuing."
                : " Nothing was sent to the device for this step.";
            return $"Automated device move ({move.Description}) {reason}.{recovery}";
        }

        // Auto Run All cancellation/failure recovery: walks every device move successfully applied during the
        // current run, in reverse, sending each one's inverse — returning the device to baseline. Best-effort;
        // stops and surfaces a warning if a recovery move itself fails (device position becomes uncertain).
        // Returns what actually happened, which the caller needs in order to decide whether the run is still
        // resumable: anyRolledBack is true once at least one inverse move has been sent (the device no longer
        // matches this run's readings), recoveryFailed is true if a recovery move threw (position uncertain).
        // A run that had applied nothing yields (false, false) and is untouched.
        private async Task<(bool anyRolledBack, bool recoveryFailed)> RecoverAppliedMovesAsync() {
            var controller = tiltDeviceConnectionService?.Controller;
            var moves = appliedDeviceMovesThisRun.ToArray();
            appliedDeviceMovesThisRun.Clear();
            deviceMoveAppliedForStep = null;
            if (controller == null || moves.Length == 0) {
                return (false, false);
            }
            bool anyRolledBack = false;
            try {
                for (int i = moves.Length - 1; i >= 0; i--) {
                    var inverse = EatWizardMapping.InverseMove(moves[i]);
                    if (inverse == null) {
                        continue;
                    }
                    try {
                        progress.Report(new ApplicationStatus { Status = $"Tilt Adapter Wizard: recovering — {inverse.Description}" });
                        await controller.ExecuteMoveAsync(inverse, moveProgress, CancellationToken.None).ConfigureAwait(true);
                        anyRolledBack = true;
                        RefreshRunDevicePositions(captureBaseline: false);
                    } catch (Exception ex) {
                        Logger.Error(ex, "Failed to fully recover tilt adapter device position after an automated-run cancellation/failure.");
                        MeasurementFailureText = $"Recovery failed while returning the device to its original position: {ex.Message}. Verify screw/motor positions before continuing.";
                        HasMeasurementFailureChoice = true;
                        return (anyRolledBack, true);
                    }
                }
            } finally {
                // Clear the transient "recovering — …" bottom-left status so it doesn't linger after recovery.
                progress.Report(new ApplicationStatus { Status = string.Empty });
            }
            return (anyRolledBack, false);
        }

        // The single cancel/failure exit for an automated run: roll this run's device moves back, release the
        // exclusive lease, and — when the rollback actually changed the device — latch the run as unresumable
        // (see deviceRunAbandoned). Every caller previously did the first two by hand and none did the third,
        // which left the measurement buttons live on a run whose device state had been undone underneath them.
        private async Task AbandonDeviceRunAsync(bool cancelled) {
            var (anyRolledBack, recoveryFailed) = await RecoverAppliedMovesAsync();
            ReleaseTiltDeviceOperationToken();

            if (cancelled) {
                StatusText = anyRolledBack
                    ? "Auto Run All cancelled; the tilt adapter was returned to its original position."
                    : "Auto Run All cancelled.";
            }

            // Only a run whose moves were actually undone — or whose rollback failed, leaving the position
            // uncertain — is unresumable: its stored readings no longer describe where the device is. A run
            // that had applied nothing (e.g. an AutoFocus failure on Baseline, which has no move) is physically
            // untouched, so leave it retryable exactly as it was before this latch existed.
            if (!anyRolledBack && !recoveryFailed) {
                return;
            }
            deviceRunAbandoned = true;

            string reason = recoveryFailed
                ? "This calibration run cannot be resumed: the tilt adapter could not be fully returned to its original position."
                : "This calibration run cannot be resumed because the tilt adapter was returned to its original position.";
            string cannotResume = $"{reason} Click Abort Wizard, then Calibrate, to start a new run.";
            // Say so wherever the user is already looking: the failure panel when there is one (it owns the
            // retry button this just disabled), otherwise the status line used by the cancel path.
            if (HasMeasurementFailureChoice) {
                MeasurementFailureText = string.IsNullOrEmpty(MeasurementFailureText)
                    ? cannotResume
                    : $"{MeasurementFailureText} {cannotResume}";
            } else {
                StatusText = string.IsNullOrEmpty(StatusText) ? cannotResume : $"{StatusText} {cannotResume}";
            }
        }

        private void ReleaseTiltDeviceOperationToken() {
            var t = tiltDeviceOperationToken;
            tiltDeviceOperationToken = null;
            t?.Dispose();
        }

        // Whether the currently configured CalibrationAppliedAmount is safe to send to a connected motorized
        // device: positive, and within both the per-command and cumulative-excursion safety caps (T8
        // options). Used for AutoRunAllCommand's canExecute (fast/sync, no side effects).
        private bool HasValidDeviceAppliedAmount() => TryValidateDeviceAppliedAmount(out _);

        // Same check as HasValidDeviceAppliedAmount, with a human-readable reason for Notification.ShowError
        // — called before StartAsync/AutoRunAllAsync actually proceeds with a device-driven run (T11 item 4).
        private bool TryValidateDeviceAppliedAmount(out string error) {
            int appliedSteps = (int)Math.Round(CalibrationAppliedAmount);
            if (appliedSteps <= 0) {
                error = "The applied amount per calibration step must be greater than zero to run a hands-off calibration.";
                return false;
            }
            int maxSteps = tiltAdapterOptions.TiltDeviceMaxStepsPerCommand;
            if (maxSteps > 0 && appliedSteps > maxSteps) {
                error = $"The applied amount ({appliedSteps} steps) exceeds the configured maximum steps per command ({maxSteps}). Reduce the applied amount or increase the limit before running a hands-off calibration.";
                return false;
            }
            int maxExcursion = tiltAdapterOptions.TiltDeviceMaxExcursionSteps;
            if (maxExcursion > 0 && appliedSteps > maxExcursion) {
                error = $"The applied amount ({appliedSteps} steps) exceeds the configured maximum excursion ({maxExcursion} steps). Reduce the applied amount or increase the limit before running a hands-off calibration.";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Drives every remaining calibration step from <see cref="CurrentStep"/> through
        /// <see cref="WizardStep.Complete"/> automatically: device move, live measurement, advance — repeated
        /// without further clicks — finishing with Complete's restore move (returns the device to baseline;
        /// handled by <see cref="MeasureStep"/>, which this reuses for every step so the manual "Run
        /// Measurement" flow and this loop share one code path). If the wizard is not yet running, starts it
        /// first (mirrors clicking Start, including the applied-amount validation and exclusive
        /// operation-token acquisition in <see cref="StartAsync"/>).
        ///
        /// Cancelling (the existing <see cref="CancelCommand"/> / measureCts) lets the CURRENT move/measurement
        /// finish — the device has no abort command — then recovers toward baseline by sending the inverse of
        /// every move applied so far, in reverse order (<see cref="RecoverAppliedMovesAsync"/>), and stops. A
        /// hard failure partway through (a move or a measurement failing) is recovered the same way, UNLESS
        /// the failure was Complete's own restore move — by then the calibration has already succeeded (and
        /// the device-linked marker is already set), and ExecuteDeviceMoveForCurrentStepAsync already
        /// attempted its own single-move recovery; layering a second whole-run rollback on top of that would
        /// risk compounding an already-ambiguous device state.
        /// </summary>
        private async Task AutoRunAllAsync() {
            if (!IsMotorizedDevice || !IsTiltDeviceConnected) {
                Notification.ShowWarning("Connect the tilt adapter device before running the automated calibration.");
                return;
            }
            if (!TryValidateDeviceAppliedAmount(out var validationError)) {
                Notification.ShowError(validationError);
                return;
            }

            if (!IsWizardRunning) {
                await StartAsync();
                if (!IsWizardRunning) {
                    return; // StartAsync already surfaced why (validation failure or device busy).
                }
            } else if (!currentRunIsDeviceDriven) {
                Notification.ShowWarning("Restart the wizard (Abort Wizard, then Calibrate) while the tilt adapter device is connected to use Auto Run All.");
                return;
            }

            IsAutoRunningAll = true;
            NotifyCommandsCanExecuteChanged();

            measureCts?.Dispose();
            measureCts = new CancellationTokenSource();
            var token = measureCts.Token;
            IsMeasuring = true;
            try {
                while (CurrentStep != WizardStep.Complete) {
                    if (token.IsCancellationRequested) {
                        await AbandonDeviceRunAsync(cancelled: true);
                        return;
                    }
                    var stepBefore = CurrentStep;
                    await MeasureStep(stepBefore, token, fromSaved: false);
                    if (HasMeasurementFailureChoice) {
                        if (CurrentStep != WizardStep.Complete) {
                            // A move or measurement failed before the run finished — undo everything applied so far,
                            // which also latches the run as unresumable if that rollback moved the device.
                            await AbandonDeviceRunAsync(cancelled: false);
                        } else {
                            // Only Complete's own restore move failed: the calibration itself already succeeded, so
                            // there is nothing to abandon (and MeasureStep already released the lease at Complete).
                            ReleaseTiltDeviceOperationToken();
                        }
                        return;
                    }
                    if (CurrentStep == stepBefore) {
                        // Defensive: NextStep() did not advance (unreachable when HasMeasurementFailureChoice
                        // is false); never spin forever.
                        break;
                    }
                }
            } catch (OperationCanceledException) {
                await AbandonDeviceRunAsync(cancelled: true);
            } finally {
                IsMeasuring = false;
                IsAutoRunningAll = false;
                NotifyCommandsCanExecuteChanged();
            }
        }

        // ---- End T11: hands-off device-driven calibration ------------------------------------------------

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
            // T11 item 4/5: a connected, motorized run drives its own moves and must hold the exclusive T9
            // operation lease for the whole run — acquire (or refuse) it BEFORE touching any other state, so
            // a validation/busy failure leaves everything exactly as it was (no partial reset).
            bool deviceDriven = IsTiltDeviceConnected && IsMotorizedDevice;
            if (deviceDriven) {
                if (!TryValidateDeviceAppliedAmount(out var validationError)) {
                    Notification.ShowError(validationError);
                    return Task.CompletedTask;
                }
                var opToken = tiltDeviceConnectionService.TryBeginOperation("Tilt Adapter Wizard Calibration");
                if (opToken == null) {
                    Notification.ShowError("The tilt adapter device is busy with another operation.");
                    return Task.CompletedTask;
                }
                ReleaseTiltDeviceOperationToken(); // defensive; should already be null at this point
                tiltDeviceOperationToken = opToken;
            }
            currentRunIsDeviceDriven = deviceDriven;
            deviceMoveAppliedForStep = null;
            appliedDeviceMovesThisRun.Clear();
            // Re-capture the position-delta baseline for this run on the first device move below (the cp poll is
            // paused for the whole run, so live positions come from the controller per move).
            calibrationBaselinePositions = null;

            StatusText = string.Empty;
            ClearMeasurementFailureChoice();
            deviceRunAbandoned = false; // a fresh run starts from Baseline and re-acquires the exclusive lease
            stepReadings.Clear();
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
            HasRebaselineDriftWarning = false;
            RebaselineDriftWarningText = string.Empty;
            HasConfidenceWarning = false;
            ConfidenceWarningText = string.Empty;
            curvatureChannelDisagreement = null;
            HasWarning = false;
            WarningText = string.Empty;
            HasDeviceLinkDroppedWarning = false;
            DeviceLinkDroppedWarningText = string.Empty;
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

            activeMeasurementSteps = GetMeasurementSteps(tiltAdapterOptions.MeasureCurvatureDuringCalibration, tiltAdapterOptions.MeasureFinalRebaseline);
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
                CalibrationAppliedAmount = CalibrationAppliedAmount,
                MeasurementAverageCount = Math.Max(1, tiltAdapterOptions.MeasurementAverageCount),
                OptimizedStarDetectionSettings = CaptureDetectionSettings(),
                MeasurementContext = CaptureMeasurementContext(
                    inspector.InspectorOptions, HocusFocusPlugin.AutoFocusOptions,
                    profileService.ActiveProfile.TelescopeSettings.FocalRatio,
                    profileService.ActiveProfile.TelescopeSettings.FocalLength),
                // Full detection config so a replay pins every knob, not just the curated OptimizedStarDetectionSettings.
                StarDetectionSnapshot = StarDetectionSettingsSnapshot.FromOptions(HocusFocusPlugin.StarDetectionOptions),
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

        // Snapshot the live inputs the sensor-model measurement reads, so a replay can restore or warn on drift.
        internal static TiltMeasurementContext CaptureMeasurementContext(
            IInspectorOptions inspector, IAutoFocusOptions af, double fRatio, double focalLengthMm) {
            return new TiltMeasurementContext {
                // The EFFECTIVE value, so a run captured under a driver-supplied step size records what it
                // actually measured with. A replay restores this as SensorModelFocuserSizeOverrideMicrons,
                // which sits above both the override and the driver in the resolver.
                MicronsPerFocuserStep = inspector.EffectiveMicronsPerFocuserStep,
                FocalRatio = fRatio,
                FocalLengthMm = focalLengthMm,
                UseRANSAC = inspector.UseRANSAC,
                FixedSensorCenter = inspector.FixedSensorCenter,
                AstigmaticCurvatureEnabled = inspector.AstigmaticCurvatureEnabled,
                AcceptableRSquaredMin = inspector.AcceptableRSquaredMin,
                WeightedHyperbolicFitEnabled = af.WeightedHyperbolicFitEnabled,
                MaxOutlierRejections = af.MaxOutlierRejections,
                OutlierRejectionConfidence = af.OutlierRejectionConfidence,
                HyperbolicFitModel = af.HyperbolicFitModel.ToString(),
                SensorROI = inspector.SensorROI,
                CornersROI = inspector.CornersROI
            };
        }

        // Human-readable list of context fields that differ between capture and the current profile (empty if none).
        internal static string DescribeMeasurementContextDrift(TiltMeasurementContext captured, TiltMeasurementContext current) {
            if (captured == null || current == null) return string.Empty;
            var diffs = new List<string>();
            void D(string name, double a, double b) { if (!(double.IsNaN(a) && double.IsNaN(b)) && Math.Abs(a - b) > 1e-9) diffs.Add($"{name} ({a:0.###} → {b:0.###})"); }
            void B(string name, bool a, bool b) { if (a != b) diffs.Add($"{name} ({a} → {b})"); }
            D("MicronsPerFocuserStep", captured.MicronsPerFocuserStep, current.MicronsPerFocuserStep);
            D("FocalRatio", captured.FocalRatio, current.FocalRatio);
            D("AcceptableRSquaredMin", captured.AcceptableRSquaredMin, current.AcceptableRSquaredMin);
            B("UseRANSAC", captured.UseRANSAC, current.UseRANSAC);
            B("FixedSensorCenter", captured.FixedSensorCenter, current.FixedSensorCenter);
            B("WeightedHyperbolicFit", captured.WeightedHyperbolicFitEnabled, current.WeightedHyperbolicFitEnabled);
            if (!string.Equals(captured.HyperbolicFitModel, current.HyperbolicFitModel, StringComparison.Ordinal))
                diffs.Add($"HyperbolicFitModel ({captured.HyperbolicFitModel} → {current.HyperbolicFitModel})");
            return string.Join(", ", diffs);
        }

        // Delegates to the shared resolver (docs/focuser-step-size-driver-design.md §2). This used to
        // duplicate the override→driver fallback locally against the wizard's own live focuserInfo — the
        // pattern that design generalized. The shared one differs in being STICKY: a focuser that drops off
        // the bus mid-run no longer silently changes the step size a measurement is interpreted at.
        private double EffectiveFocuserStepMicrons() =>
            inspector.InspectorOptions?.EffectiveMicronsPerFocuserStep ?? -1;

        private async Task RunMeasurementAsync() {
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
            ClearMeasurementFailureChoice();

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
            ClearMeasurementFailureChoice();

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

            // Fix 2 / [CRITICAL GATE]: a "Use Saved AF" measurement (fromSaved) never drives the device — if
            // this happens during a run that started device-driven, THIS step's real move (which the
            // wizard-screw <-> device-corner correspondence depends on having actually happened) was skipped,
            // so the run is no longer purely device-driven. Err safe the instant it happens: drop out of
            // device-driven mode so (a) RunCalibrationMath — which reads currentRunIsDeviceDriven fresh at
            // Complete — CLEARS DeviceLinkedCalibrationDeviceName instead of wrongly setting it, and (b) no
            // further device moves — including Complete's restore move, which would otherwise "restore" a
            // forward move that, for this step, never actually happened — are sent for the rest of this run.
            // Release the exclusive operation lease too: this run will never touch the device again, so
            // holding it would needlessly block other automation (a future run, the inspector's Automatic
            // Adjustment) until Restart.
            if (fromSaved && currentRunIsDeviceDriven) {
                currentRunIsDeviceDriven = false;
                ReleaseTiltDeviceOperationToken();
                HasDeviceLinkDroppedWarning = true;
                DeviceLinkDroppedWarningText =
                    $"This run is no longer device-linked: \"Use Saved AF\" skipped the device move for step '{step}'. " +
                    "The remaining steps will not move the device, and this calibration will not be marked as device-linked when it completes.";
            }

            // T11: a connected, motorized run drives this step's move itself, BEFORE the measurement. Never
            // for a "Use Saved AF" re-analysis (fromSaved) — that re-analyzes already-captured frames; the
            // physical device must not be moved again for it. deviceMoveAppliedForStep guards a measurement
            // RETRY (the move already succeeded; only AutoFocus/sensor-modeling failed) from re-sending the
            // same move a second time.
            if (currentRunIsDeviceDriven && !fromSaved && deviceMoveAppliedForStep != step) {
                bool moved = await ExecuteDeviceMoveForCurrentStepAsync(step, token);
                if (!moved) {
                    StatusText = string.Empty;
                    return; // Failure text/HasMeasurementFailureChoice already set by ExecuteDeviceMoveForCurrentStepAsync.
                }
                deviceMoveAppliedForStep = step;
            }

            bool ok;
            StepReading? reading;
            if (MeasurementStepOverrideForTest != null) {
                ok = await MeasurementStepOverrideForTest(step, token);
                reading = ok ? Reading(step) : (StepReading?)null;
            } else {
                reading = await RunAveragedMeasurement(token, step, StepDescription(step), fromSaved);
                ok = reading != null;
            }
            if (!ok || reading == null) {
                // Show the inline failure panel (below) instead of the bare "Measurement failed." status — that left the
                // screw-adjustment instruction on screen (reads as "re-adjust the screw") and duplicated the panel text
                // right above the panel. Clear StatusText so the visible post-measurement status line doesn't leak the
                // stale "Run N/count..." progress text. The focuser was left where the last run ended, so re-running
                // AutoFocus often succeeds; otherwise the user can return to baseline (see BaselineRecoveryInstructions).
                StatusText = string.Empty;
                MeasurementFailureText = "AutoFocus or sensor modeling failed for this step. You can run AutoFocus again, or return the screws to baseline before retrying.";
                HasMeasurementFailureChoice = true;
                return;
            }
            stepReadings[step] = reading.Value;
            RecordStepIntoMetadata(step, reading.Value);
            NextStep();

            // T11: Complete's restore move (EatWizardMapping.MoveForStep(Complete, N) = DiagonalB(-N), or null
            // when this run's measuredFinalRebaseline is true — see ExecuteDeviceMoveForCurrentStepAsync)
            // returns the device to baseline — sent automatically for ANY device-driven run, whether stepped
            // manually (repeated "Run Measurement" clicks) or via AutoRunAllAsync's loop (which just calls
            // this method repeatedly). The operation lease is released here, AFTER the restore move is
            // attempted (or skipped), so it still protects that final move — NOT in NextStep(), which runs
            // synchronously and cannot await it.
            if (CurrentStep == WizardStep.Complete && currentRunIsDeviceDriven) {
                bool restored = await ExecuteDeviceMoveForCurrentStepAsync(WizardStep.Complete, CancellationToken.None);
                ReleaseTiltDeviceOperationToken();
                if (!restored) {
                    return; // Failure text/HasMeasurementFailureChoice already set; calibration itself already succeeded.
                }
                StatusText = "Automated calibration complete; device returned to its original position.";
            }
        }

        // Description shown on each summary row, reflecting the single move performed for the step.
        // Rotation glyphs match the guidance legend (⟳ = clockwise / + steps, ⟲ = counter-clockwise
        // / − steps) — screw rotation, never adapter-plate motion (⬆/⬇ are reserved for motion in
        // the guidance table). Perturbation steps (all CW per StepInstructionsText) carry ⟳ and the
        // re-baseline undo moves carry ⟲. Internal for tests.
        internal string StepDescription(WizardStep step) {
            bool four = tiltAdapterOptions.ScrewCount == 4;
            switch (step) {
                case WizardStep.Baseline: return "Baseline";
                case WizardStep.AllInward: return "All screws ⟳";
                case WizardStep.ReBaseline1: return "Re-baseline (all ⟲)";
                case WizardStep.Screw1: return four ? "Screw 1 ⟳, Screw 3 ⟲" : "Screw 1 ⟳";
                case WizardStep.ReBaseline2: return four ? "Re-baseline (Screw 1 ⟲, Screw 3 ⟳)" : "Re-baseline (Screw 1 ⟲)";
                case WizardStep.Screw2: return four ? "Screw 2 ⟳, Screw 4 ⟲" : "Screw 2 ⟳";
                case WizardStep.ReBaseline3: return four ? "Re-baseline (Screw 2 ⟲, Screw 4 ⟳)" : "Re-baseline (Screw 2 ⟲)";
                default: return step.ToString();
            }
        }

        // ReBaseline3 = 7 sits numerically AFTER Complete = 6 (so Complete's ordinal never renumbers), but it
        // is the 5th/7th step IN THE SEQUENCE (right before Complete) — special-cased here so its folder
        // sorts and reads correctly ("07_ReBaseline3") rather than the generic "08_ReBaseline3" the enum's
        // raw ordinal would produce.
        private static string StepFolderName(WizardStep step) {
            if (step == WizardStep.ReBaseline3) {
                return "07_ReBaseline3";
            }
            return $"{(int)step + 1:00}_{step}";
        }

        // Test seam: RunCalibrationForTest injects the tilt plane a live run would get from the inspector so the
        // hardware-recovery path is exercisable without running the paraboloid fit. Always null in production, so
        // CalibrationTiltPlane below is byte-for-byte the live inspector chain.
        private TiltPlaneModel calibrationTiltPlaneOverrideForTest;

        // Test seam: same override as above, settable directly (not just via RunCalibrationForTest's local
        // try/finally) so ReplayAsync's per-step loop — which reads CalibrationTiltPlane directly, without a
        // live inspector analysis — can be exercised under test too. Always null in production.
        internal TiltPlaneModel CalibrationTiltPlaneOverrideForTest {
            get => calibrationTiltPlaneOverrideForTest;
            set => calibrationTiltPlaneOverrideForTest = value;
        }

        // The wizard calibrates from the per-star sensor-curve model's tilt (the paraboloid Gx/Gy, expressed as a
        // TiltPlaneModel by SensorModelAberrationResult.CreateTiltPlaneModel) — NEVER the 4-corner region plane. The
        // sensor model is force-generated around each measurement (inspector.ForceSensorCurveModelGeneration); this is
        // null when the paraboloid could not be fit (too few stars), in which case the measurement fails.
        private TiltPlaneModel CalibrationTiltPlane =>
            calibrationTiltPlaneOverrideForTest ?? inspector.SensorModel?.SensorModelResult?.TiltPlaneModel;

        // Task 5: same override pattern as calibrationTiltPlaneOverrideForTest above, but for the inspector's
        // 4-corner region plane (inspector.TiltModel.TiltPlaneModel) — captured alongside the paraboloid
        // reading purely as a second, independent cross-check estimator (see RunCalibrationMath); it never
        // feeds the production calibration itself. Always null in production.
        private TiltPlaneModel cornerTiltPlaneOverrideForTest;
        internal TiltPlaneModel CornerTiltPlaneOverrideForTest {
            get => cornerTiltPlaneOverrideForTest;
            set => cornerTiltPlaneOverrideForTest = value;
        }

        // Runs the aberration inspector MeasurementAverageCount times, averages the tilt plane, appends summary
        // rows + the consistency warning, and captures the per-step field-curvature characterization. When saving
        // (live only), each step's run is redirected into its own folder and the saved location is recorded.
        private async Task<StepReading?> RunAveragedMeasurement(CancellationToken token, WizardStep step, string stepDescription, bool fromSaved) {
            // Re-analyzing the same saved frames repeatedly yields identical readings (and would pop the folder
            // dialog once per run), so the saved path runs a single pass regardless of the averaging count.
            int count = fromSaved ? 1 : Math.Max(1, tiltAdapterOptions.MeasurementAverageCount);
            var readings = new List<(double A, double B, double Mean)>(count);
            // Task 5: parallel corner-region-plane readings, averaged the same way as the paraboloid readings
            // above. NaN entries (the corner plane could not be fit for that sub-measurement) propagate through
            // Average() into a NaN step average — deliberately: a partially-available corner reading for this
            // step is not a valid basis for the cross-check, so the whole step degrades to "unavailable"
            // rather than averaging over a biased subset of the sub-measurements.
            var cornerReadings = new List<(double A, double B, double Mean)>(count);

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
                // Force the per-star sensor-curve model so this analysis produces the paraboloid tilt the calibration
                // reads (CalibrationTiltPlane); restore the flag immediately so it never leaks to other inspector uses.
                var prevForce = inspector.ForceSensorCurveModelGeneration;
                inspector.ForceSensorCurveModelGeneration = true;
                try {
                    if (fromSaved) {
                        // IsMeasuring is set via callback after the folder dialog closes so the chart doesn't appear
                        // until the user has confirmed a selection.
                        ok = await inspector.AnalyzeAutoFocusFromSaved(token, onFolderSelected: () => IsMeasuring = true, saveOverride: saveOverride);
                    } else {
                        // includeExposureAnalysis: false — a calibration step consumes ONLY the sensor model fitted
                        // from the sweep (CalibrationTiltPlane, below). The inspector's closing validation exposure
                        // feeds the FWHM-contour/eccentricity panels, which this wizard never reads, and it costs a
                        // further SimpleExposureSeconds plus a full-frame PSF detection on every one of the six
                        // steps — ~18 s per step on the rig this was measured on.
                        ok = await inspector.AnalyzeAutoFocus(token, captureCameraBlock: true, saveOverride, includeExposureAnalysis: false);
                    }
                } finally {
                    inspector.ForceSensorCurveModelGeneration = prevForce;
                }
                if (!ok) return null;
                var m = CalibrationTiltPlane;
                if (m == null) {
                    Logger.Error("Tilt calibration: the per-star sensor-curve model could not be fit for this step; cannot derive tilt.");
                    return null;
                }
                readings.Add((m.A, m.B, m.MeanFocuserPosition));
                var corner = cornerTiltPlaneOverrideForTest ?? inspector.TiltModel?.TiltPlaneModel;
                cornerReadings.Add((corner?.A ?? double.NaN, corner?.B ?? double.NaN, corner?.MeanFocuserPosition ?? double.NaN));
            }

            double avgA = readings.Average(r => r.A);
            double avgB = readings.Average(r => r.B);
            double avgMean = readings.Average(r => r.Mean);
            double avgCornerA = cornerReadings.Average(r => r.A);
            double avgCornerB = cornerReadings.Average(r => r.B);
            double avgCornerMean = cornerReadings.Average(r => r.Mean);

            var latestModel = CalibrationTiltPlane;
            AppendSummaryRows(readings, stepDescription, latestModel, count, avgA, avgB);

            var (avgGx, avgGy) = StateGradient(avgA, avgB, latestModel);
            var reading = new StepReading {
                A = avgA,
                B = avgB,
                Mean = avgMean,
                TiltAngleDeg = ComputeTiltAngleDeg(avgA, avgB, latestModel),
                DirectionDeg = NormalizeAngle(Math.Atan2(avgGx, -avgGy) * 180.0 / Math.PI),
                SaveFolder = saveOverride != null ? inspector.LastSaveFolder : null,
                CornerA = avgCornerA,
                CornerB = avgCornerB,
                CornerMean = avgCornerMean
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

        // Internal for tests (the consistency-warning path is otherwise only reachable through a live inspector run).
        internal void AppendSummaryRows(List<(double A, double B, double Mean)> readings, string stepDescription,
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

        // Converts a step's (A, B) tilt-plane reading (focuser steps per normalized image coordinate) into the
        // physical best-focus gradient (gx, gy): microns of focuser travel per micron of sensor displacement.
        // Shared by ComputeTiltAngleDeg (magnitude) and the per-state DirectionDeg (atan2(gx,-gy)) so both read
        // the same physical space instead of the anisotropic (A,B) coefficients (the F2 bug) — mirrors
        // TiltScrewGeometry.PlaneGradientToPhysical / TiltCalibrationCalculator.PhysicalDelta. (NaN, NaN) when the
        // model or the focuser/pixel size inputs are unavailable.
        private (double gx, double gy) StateGradient(double a, double b, TiltPlaneModel model) {
            if (model == null) return (double.NaN, double.NaN);
            var pixelSizeMicrons = profileService.ActiveProfile.CameraSettings.PixelSize;
            var fStepMicrons = model.FocuserStepSizeMicrons;
            // Fall back to the connected focuser's reported step size when the inspector
            // option (MicronsPerFocuserStep) hasn't been configured.
            if (double.IsNaN(fStepMicrons) || fStepMicrons <= 0) {
                fStepMicrons = focuserInfo.StepSize;
            }
            if (double.IsNaN(fStepMicrons) || fStepMicrons <= 0 ||
                double.IsNaN(pixelSizeMicrons) || pixelSizeMicrons <= 0) return (double.NaN, double.NaN);
            // A and B are in focuser steps per normalized image coordinate (range [-0.5, 0.5]).
            // Convert to gradient in physical units: (steps * microns/step) / (pixels * microns/pixel).
            var gx = a * fStepMicrons / (model.ImageSize.Width * pixelSizeMicrons);
            var gy = b * fStepMicrons / (model.ImageSize.Height * pixelSizeMicrons);
            return (gx, gy);
        }

        private double ComputeTiltAngleDeg(double a, double b, TiltPlaneModel model) {
            var (gx, gy) = StateGradient(a, b, model);
            if (double.IsNaN(gx) || double.IsNaN(gy)) return double.NaN;
            return Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI;
        }

        private void CancelMeasurement() {
            measureCts?.Cancel();
        }

        private void ClearMeasurementFailureChoice() {
            HasMeasurementFailureChoice = false;
            MeasurementFailureText = string.Empty;
        }

        // Internal for tests (the step-advance rule and the end-of-run calibration math are otherwise only
        // reachable through a live inspector measurement).
        internal void NextStep() {
            int idx = Array.IndexOf(activeMeasurementSteps, currentStep);
            if (idx < 0) {
                // Unreachable via the measurement commands (they gate on IsOnMeasurementStep), but never
                // fabricate a Complete: that would run the calibration math on default-zero readings and
                // persist IsCalibrated from garbage. Log loudly and leave the state untouched.
                Logger.Error($"Tilt calibration: current step {currentStep} is not in the active measurement sequence; ignoring step advance.");
                return;
            }
            WizardStep next = idx == activeMeasurementSteps.Length - 1
                ? WizardStep.Complete
                : activeMeasurementSteps[idx + 1];

            if (next == WizardStep.Complete) {
                RunCalibrationMath(
                    tiltAdapterOptions.ScrewCount,
                    tiltAdapterOptions.ScrewRadiusMillimeters,
                    profileService.ActiveProfile.CameraSettings.PixelSize,
                    EffectiveFocuserStepMicrons(),
                    CalibrationAppliedAmount,
                    IsStepperAdjustment,
                    deviceDriven: currentRunIsDeviceDriven);
                RebuildDiagram();
                FinalizeMetadata();
                // NOTE: the device operation token (if any) is released by MeasureStep's caller AFTER the
                // Complete restore move is attempted -- NOT here -- so the lease still protects that final
                // move (NextStep runs synchronously and cannot await it).
            }

            // The measurement-consistency warning is intentionally NOT cleared here: it is set at the end of a
            // successful averaged measurement, and NextStep runs immediately afterwards — clearing it here would
            // hide it before the user ever saw it. It survives onto the next step's screen and is cleared when the
            // next measurement run starts (RunMeasurementAsync / RunSavedMeasurementAsync) and on Start/Restart/Replay.
            StatusText = string.Empty;
            ClearMeasurementFailureChoice();
            CurrentStep = next;
        }

        private void Restart() {
            measureCts?.Cancel();
            IsWizardRunning = false;
            IsMeasuring = false;
            IsReplaying = false;
            IsAutoRunningAll = false;
            // Abandoning a device-driven run releases the exclusive lease so other automation (a future run,
            // the inspector's Automatic Adjustment) isn't blocked. Deliberately does NOT attempt to drive the
            // device back to baseline — an abandoned run's physical recovery is left to the user (mirrors the
            // existing disconnected-run philosophy of BaselineRecoveryInstructions); only a controlled
            // Auto Run All cancellation/failure attempts an automatic device recovery (RecoverAppliedMovesAsync).
            ReleaseTiltDeviceOperationToken();
            currentRunIsDeviceDriven = false;
            deviceRunAbandoned = false;
            deviceMoveAppliedForStep = null;
            appliedDeviceMovesThisRun.Clear();
            SaveAFRuns = false; // saving must be re-enabled explicitly for each run
            stepReadings.Clear();
            ResetDerivedCalibrationReadouts();
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
            HasDeviceLinkDroppedWarning = false;
            DeviceLinkDroppedWarningText = string.Empty;
            StatusText = string.Empty;
            ClearMeasurementFailureChoice();
            CurrentStep = WizardStep.Baseline;
        }

        private void ApplyDevice(string name) {
            var preset = TiltAdapterDevicePreset.ByName(name);
            // Captured BEFORE the DeviceName assignment below so the CalibrationAppliedAmount reset (further
            // down) can tell a genuine user-driven device change apart from ApplyDevice's other call site --
            // the constructor's re-lock-on-load path, which re-asserts the ALREADY-persisted device on every
            // app start. Without this guard the ctor call would unconditionally overwrite a persisted, possibly
            // user-edited CalibrationAppliedAmount with the preset default on every launch (this VM is a
            // [PartCreationPolicy(CreationPolicy.Shared)] singleton, so the ctor runs exactly once per app run).
            var previousName = tiltAdapterOptions.DeviceName;
            tiltAdapterOptions.DeviceName = preset.Name;
            if (!preset.IsManual) {
                tiltAdapterOptions.ScrewCount = preset.ScrewCount;
                tiltAdapterOptions.AdjustmentType = preset.AdjustmentType;
                tiltAdapterOptions.ThreadPitchMicrons = preset.ThreadPitchMicrons;
                tiltAdapterOptions.StepperStepSizeMicrons = preset.StepperStepSizeMicrons;
                tiltAdapterOptions.ScrewRadiusMillimeters = preset.ScrewRadiusMillimeters;
            }
            // Reset the applied amount to the newly selected preset's default (still user-editable
            // afterward) ONLY when the device actually changed. Applies to Manual too, so switching back to
            // Manual resets to 1 full turn rather than leaving a leftover stepper value (e.g. 150) on screen --
            // but re-asserting the SAME already-persisted device (the ctor path) must never clobber a value the
            // user already customized for that device.
            if (!string.Equals(preset.Name, previousName, System.StringComparison.Ordinal)) {
                tiltAdapterOptions.CalibrationAppliedAmount = preset.DefaultCalibrationAmount;
            }
            RaisePropertyChanged(nameof(SelectedDevice));
            RaisePropertyChanged(nameof(IsManualDevice));
            RaisePropertyChanged(nameof(IsMotorizedDevice));
            RaisePropertyChanged(nameof(AdjustmentType));
            RaisePropertyChanged(nameof(ThreadPitchMicronsValue));
            RaisePropertyChanged(nameof(StepperStepSizeMicronsValue));
            RaisePropertyChanged(nameof(ScrewRadiusMillimetersValue));
            RaisePropertyChanged(nameof(IsStepperAdjustment));
            RaisePropertyChanged(nameof(CalibrationAmountLabel));
            RaisePropertyChanged(nameof(CalibrationAppliedAmount));
            RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));
            RaisePropertyChanged(nameof(StepInstructions));
            // ApplyDevice only runs on the UI thread (SelectedDevice setter) or during construction, so the
            // commands are notified without marshaling (a Dispatch here would break the "device broadcasts
            // never block on the UI thread" accounting the F15 tests pin).
            NotifyCommandsCanExecuteChangedCore(); // ConnectDeviceCommand gates on IsMotorizedDevice
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

        // Runs the full calibration math from the step readings (six, or seven with the optional measured
        // final re-baseline) and writes the results to the options. Shared by a live run (geometry from the
        // current options/profile) and a replay (geometry from the metadata).
        // deviceDriven: true only for a completed CONNECTED, device-driven (hands-off) run — the wizard sent
        // every step's move itself, so the wizard-screw <-> device-corner correspondence (EatWizardMapping) is
        // known to be correct. False for a disconnected/manual-clicking run, a replay, or the RunCalibrationForTest
        // seed path (its default) — see the [CRITICAL GATE] note on ITiltAdapterOptions.DeviceLinkedCalibrationDeviceName.
        // Shared by RunCalibrationMath's paraboloid `inputs` and its Task-5 corner-AF cross-check `cornerInputs`:
        // every geometry/flag field is identical between the two estimators -- only the up-to-seven gradient
        // readings (baseline/all-inward/re-baselines/screw moves, plus the Task-6 optional measured final
        // re-baseline) differ. Factored so the two TiltCalibrationInputs objects cannot drift apart (a field
        // added to one and forgotten on the other would silently bias the cross-check).
        private static TiltCalibrationInputs BuildCalibrationInputs(
            int screwCount, bool hasCurvatureMeasurement, int fallbackCurvatureSign,
            double imageWidthPixels, double imageHeightPixels, double pixelSize, double fStep,
            double radiusMm, double appliedAmount, bool isStepper,
            TiltGradient baseline, TiltGradient allInward, TiltGradient reBaseline1,
            TiltGradient screw1, TiltGradient reBaseline2, TiltGradient screw2,
            TiltGradient reBaseline3, bool hasFinalRebaseline) {
            return new TiltCalibrationInputs {
                ScrewCount = screwCount,
                // 4-step runs never measured Baseline/AllInward as curvature probes: leave them default —
                // the calculator ignores them when HasCurvatureMeasurement is false (reBaseline1 carries the
                // baseline reading instead).
                Baseline = hasCurvatureMeasurement ? baseline : default,
                AllInward = hasCurvatureMeasurement ? allInward : default,
                ReBaseline1 = reBaseline1,
                Screw1 = screw1,
                ReBaseline2 = reBaseline2,
                Screw2 = screw2,
                HasCurvatureMeasurement = hasCurvatureMeasurement,
                FallbackCurvatureSign = fallbackCurvatureSign,
                ImageWidthPixels = imageWidthPixels,
                ImageHeightPixels = imageHeightPixels,
                PixelSizeMicrons = pixelSize,
                FocuserStepMicrons = fStep,
                ScrewRadiusMillimeters = radiusMm,
                CalibrationAppliedAmount = appliedAmount,
                IsStepperAdjustment = isStepper,
                // Same "leave default when not measured" discipline as Baseline/AllInward above -- the
                // calculator ignores ReBaseline3 whenever HasFinalRebaseline is false.
                ReBaseline3 = hasFinalRebaseline ? reBaseline3 : default,
                HasFinalRebaseline = hasFinalRebaseline
            };
        }

        // Fix 5 (post-review): the EstimatorRelativeDifference guard goes silent whenever either alternate
        // (corner) screw magnitude is ~0 -- correctly, when the primary (paraboloid) estimator ALSO saw ~no
        // move for that screw (nothing to compare). But when the corner estimator saw ~no move for a screw the
        // paraboloid says was clearly turned, that is itself a real, diagnostic disagreement the guard would
        // otherwise hide entirely. The plan specifies exactly one user-facing warning string
        // (CheckCornerCrossCheck's, documented in Task 8) -- this does NOT grow a second one; it logs to the
        // NINA log only, where it is discoverable without inventing new UI text.
        private static void LogDegenerateCornerScrewIfNeeded(int screwNumber, double paraboloidMagnitude, double cornerMagnitude) {
            if (cornerMagnitude <= 0 && paraboloidMagnitude > 0) {
                Logger.Warning(
                    $"Tilt calibration: the corner-region AF plane saw essentially no move for screw {screwNumber} " +
                    $"(magnitude {cornerMagnitude:0.###e+0}) while the per-star model measured {paraboloidMagnitude:0.###e+0} " +
                    "-- the corner-AF cross-check cannot form a ratio for this screw and is being skipped.");
            }
        }

        private void RunCalibrationMath(int screwCount, double radiusMm, double pixelSize, double fStep,
            double appliedAmount, bool isStepper, bool deviceDriven) {
            bool measuredCurvature = stepReadings.ContainsKey(WizardStep.AllInward);
            // Task 6: the optional measured final re-baseline. Presence in stepReadings (NOT
            // activeMeasurementSteps -- deliberately, for two independent reasons) is the same "was it
            // actually measured" test measuredCurvature above uses for AllInward:
            //  1. Replay: a saved run predating this feature (or captured with the option off) simply never
            //     has a ReBaseline3 entry, so this comes back false and Screw2Delta keeps its old
            //     ReBaseline2-only semantics (backward compatible) -- exactly like AllInward's replay case.
            //  2. RunCalibrationForTest (the SeedStepReading test seam): it calls RunCalibrationMath directly
            //     and never calls StartAsync/ReplayAsync, so activeMeasurementSteps is still sitting at its
            //     ctor-time field-initializer default (the 4-step flow, no ReBaseline3) regardless of what
            //     was actually seeded into stepReadings. Gating on activeMeasurementSteps here would make
            //     every RunCalibrationForTest-based test seeding a ReBaseline3 reading silently ignore it.
            bool hasFinalRebaseline = stepReadings.ContainsKey(WizardStep.ReBaseline3);
            var a = Reading(WizardStep.Baseline);
            var b = Reading(WizardStep.AllInward);
            var c = measuredCurvature ? Reading(WizardStep.ReBaseline1) : Reading(WizardStep.Baseline);
            var d = Reading(WizardStep.Screw1);
            var e = Reading(WizardStep.ReBaseline2);
            var f = Reading(WizardStep.Screw2);
            var g = Reading(WizardStep.ReBaseline3);

            // Curvature (backfocus) sign from baseline (a) → all-inward (b) mean focus, only when
            // those steps ran; otherwise the configured/assumed sign is left untouched.
            if (measuredCurvature) {
                int curvatureSign = TiltCalibrationCalculator.ComputeCurvatureSign(b.Mean, a.Mean);
                // This one number decides which way EVERY later automated correction turns, and it is derived
                // from a single comparison — so log the comparison itself, not just the verdict. Without it,
                // checking a suspect direction against a night's log means reconstructing mean-focus positions
                // from the raw model solves. The curvature effects are logged alongside because they are the
                // quantity the stored sign is DEFINED in terms of (see TiltScrewGeometry's empirical anchor),
                // and a run where the two disagree is exactly the evidence needed to settle the convention.
                // The raw σ is printed as-is (it is the stored, k-free quantity); only the mechanical
                // OBJECTIVE/CAMERA word needs the focuser convention, because m = σ·sign(k) — hence the
                // explicit "per the focuser direction setting" caveat (design §3, site 6).
                Logger.Info(
                    $"Tilt calibration: adapter direction measured from the all-screws step. Mean best-focus position {a.Mean:F1} → {b.Mean:F1} " +
                    $"(Δ {b.Mean - a.Mean:+0.0;-0.0} focuser steps); curvature effect at screw radius {a.CurvatureEffectAtScrewRadiusMicrons:F1} → {b.CurvatureEffectAtScrewRadiusMicrons:F1} µm. " +
                    $"ScrewInwardCurvatureSign = {curvatureSign:+0;-0} (per the focuser direction setting, clockwise/+steps moves the adapter toward the " +
                    $"{(TiltScrewGeometry.CwMovesAdapterTowardObjectiveForSign(curvatureSign * FocuserSign) ? "OBJECTIVE" : "CAMERA")}).");
                tiltAdapterOptions.ScrewInwardCurvatureSign = curvatureSign;
                tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured = true;
                curvatureChannelDisagreement = EvaluateCurvatureChannelCrossCheck(a, b, c, e, curvatureSign);
            } else {
                curvatureChannelDisagreement = null;
            }

            calibrationScrewRadiusMm = radiusMm;
            calibrationPixelSizeMicrons = pixelSize;
            calibrationFocuserStepMicrons = fStep;

            // RunCalibrationMath is the SINGLE production consumer of TiltCalibrationCalculator.Calibrate: one
            // TiltCalibrationInputs, one Calibrate() call, feeding the screw angles, raw gap/ratio, recovered
            // hardware, and confidence alike. Do not hand-mirror any of that math back into this method — it
            // used to (separate inline (A,B) angle/ratio algebra plus a separate RecoverHardwareDetailed call),
            // which is exactly why the F2 physical-gradient-space fix didn't reach the wizard's persisted
            // Screw1..4AngleDegrees or the "unequal tilt changes" warning until this refactor. Tasks 3-6 add
            // fields to Calibrate's inputs/result; wiring them here (not duplicating them) is what makes them
            // reach production automatically.
            var model = CalibrationTiltPlane;
            // A model-less run (e.g. a seeded/test-only run with no live/replayed tilt plane) has no known
            // sensor size; fall back to a square 1x1 pseudo-sensor so the angle/ratio/confidence conversion
            // stays isotropic instead of aspect-distorted -- a uniform scale of the raw (A,B) delta reproduces
            // the exact pre-refactor direction/ratio/SNR-ratio behavior for those outputs. Every real (live or
            // replayed) run has a model by the time this method is reached, so production calibrations always
            // get their true geometry here; the fallback exists for test/degenerate inputs, not as a supported
            // production mode. This fallback is NOT safe for hardware recovery (see the model != null gate
            // below): a fake 1-pixel-wide sensor carries no real lever arm, so µm/turn recovered against it
            // would be non-NaN but physically meaningless.
            double imageWidthPixels = model?.ImageSize.Width ?? 1;
            double imageHeightPixels = model?.ImageSize.Height ?? 1;
            // Local function closing over this run's shared geometry/flag locals (Fix 6, post-review; extended
            // for Task 6's hasFinalRebaseline): both the paraboloid `inputs` below and the Task-5
            // corner-cross-check `cornerInputs` further down call THIS one function, so only the up-to-seven
            // gradient readings can ever differ between the two TiltCalibrationInputs objects — nothing short
            // of editing this one signature could let the shared leading arguments (including
            // hasFinalRebaseline) drift apart between the call sites.
            TiltCalibrationInputs MakeInputs(TiltGradient baseline, TiltGradient allInward, TiltGradient reBaseline1,
                TiltGradient screw1, TiltGradient reBaseline2, TiltGradient screw2, TiltGradient reBaseline3) =>
                BuildCalibrationInputs(
                    screwCount, measuredCurvature, tiltAdapterOptions.ScrewInwardCurvatureSign,
                    imageWidthPixels, imageHeightPixels, pixelSize, fStep, radiusMm, appliedAmount, isStepper,
                    baseline, allInward, reBaseline1, screw1, reBaseline2, screw2, reBaseline3, hasFinalRebaseline);
            var inputs = MakeInputs(
                new TiltGradient(a.A, a.B, a.Mean), new TiltGradient(b.A, b.B, b.Mean),
                new TiltGradient(c.A, c.B, c.Mean), new TiltGradient(d.A, d.B, d.Mean),
                new TiltGradient(e.A, e.B, e.Mean), new TiltGradient(f.A, f.B, f.Mean),
                new TiltGradient(g.A, g.B, g.Mean));
            var result = TiltCalibrationCalculator.Calibrate(inputs);

            tiltAdapterOptions.Screw1AngleDegrees = result.Screw1AngleDegrees;
            tiltAdapterOptions.Screw2AngleDegrees = result.Screw2AngleDegrees;
            tiltAdapterOptions.Screw3AngleDegrees = result.Screw3AngleDegrees;
            tiltAdapterOptions.Screw4AngleDegrees = result.Screw4AngleDegrees;
            if (tiltAdapterOptions.ScrewCount != screwCount) {
                tiltAdapterOptions.ScrewCount = screwCount; // replaying a run captured with a different screw count
            }
            tiltAdapterOptions.CalibratedScrewCount = screwCount;
            tiltAdapterOptions.IsCalibrated = true;
            tiltAdapterOptions.CalibrationIsManual = false;
            // [CRITICAL GATE] Set ONLY on a successful, connected, device-driven run's completion; cleared for
            // every other completion (disconnected live run, replay). A stale marker would let automation
            // (T14's Automatic Adjustment / a future wizard auto-apply) apply corrections against a
            // wizard-screw <-> device-corner correspondence that was never actually established by this
            // calibration — err toward clearing whenever the run wasn't a fresh connected hands-off run.
            tiltAdapterOptions.DeviceLinkedCalibrationDeviceName = deviceDriven ? tiltAdapterOptions.DeviceName : string.Empty;

            lastRawAngleDiff = result.RawAngleDiffDegrees;
            lastMoveMagnitudeRatio = result.MoveMagnitudeRatio;

            // Recover the adapter hardware (µm/turn or µm/step) — Calibrate computes this via the same
            // RecoverHardwareDetailed math this method used to call directly, off the SAME inputs/result as
            // the angles/confidence above. Consumption is re-gated on model != null (as it was pre-refactor):
            // the 1x1 image-size fallback used to build `inputs` is isotropic and therefore safe for
            // angles/ratio/confidence (direction- and ratio-preserving, so it reproduces the pre-refactor
            // behavior exactly), but it carries no real lever arm for hardware -- an ungated 1x1 fake sensor
            // with genuinely-real pixelSize/fStep would pass RecoverHardwareDetailed's >0 guard and yield a
            // non-NaN, physically-meaningless µm/turn that could get persisted into LastMeasured*Microns. Kept
            // local (not the distant CalibrationTiltPlane == null bail-outs in RunAveragedMeasurement/
            // ReplayAsync) because Task 5 adds a second TiltCalibrationInputs to this same method.
            measuredHardwareMicrons = double.NaN;
            pitchUncertaintyMicrons = double.NaN;
            if (model != null) {
                pitchUncertaintyMicrons = result.PitchUncertaintyMicrons;
                if (!double.IsNaN(result.MeasuredHardwareMicrons)) {
                    measuredHardwareMicrons = result.MeasuredHardwareMicrons;
                    if (isStepper) {
                        tiltAdapterOptions.LastMeasuredStepperStepSizeMicrons = result.MeasuredHardwareMicrons;
                    } else {
                        tiltAdapterOptions.LastMeasuredThreadPitchMicrons = result.MeasuredHardwareMicrons;
                    }
                }
            }

            // Piston-implied pitch (frame-factor probe): unlike the hardware recovery above, this needs NO
            // sensor geometry at all -- TiltCalibrationCalculator.PistonImpliedMicronsPerStep consumes only
            // mean focuser positions, the focuser step size, and the applied amount, never the per-screw
            // lever arm the model != null gate above exists to protect. So it is deliberately NOT gated on
            // model != null. This does NOT unlock a model-less UI/warning benefit -- the only display surface
            // (the StackPanel this feeds, gated on HasMeasuredHardware) and the disagreement warning below
            // (gated on measuredHardware > 0) both already require a real model indirectly, since
            // measuredHardwareMicrons stays NaN without one. The actual benefit is narrower: the value still
            // lands in FinalizeMetadata's persisted TiltCalibrationResultRecord (Task 7's offline consumer)
            // on a model-less replay/test run, instead of silently dropping to NaN alongside the
            // geometry-dependent fields.
            pistonImpliedMicronsPerStep = result.PistonImpliedMicronsPerStep;

            // ---- Task 5: corner-region AF cross-check ---------------------------------------------------
            // The inspector's 4-corner region plane (an honest per-corner estimator since Task 1) is a second,
            // independent measurement of the same screw moves. Only cross-check when EVERY active step is
            // BOTH present in stepReadings AND carries a non-NaN corner reading -- Reading(step) falls back to
            // default(StepReading) for a step missing from stepReadings entirely, which zero-inits CornerA/B/
            // Mean to 0.0 (a struct default), NOT NaN, so the non-NaN test alone would be fooled into treating
            // an unmeasured step as a valid zero-tilt corner reading. A partially-available run (an older saved
            // run captured before this feature, a step whose corner regions failed to fit, or -- the reason for
            // the explicit key check -- a step never measured at all) degrades cleanly to "no cross-check"
            // rather than NaN-contaminated arithmetic or (worse) a spurious warning built from a biased subset.
            cornerMeasuredHardwareMicrons = double.NaN;
            estimatorRelativeDifference = double.NaN;
            bool cornerDataComplete = activeMeasurementSteps.All(step =>
                stepReadings.TryGetValue(step, out var r) &&
                !double.IsNaN(r.CornerA) && !double.IsNaN(r.CornerB) && !double.IsNaN(r.CornerMean));
            if (cornerDataComplete) {
                // Reuses the SAME MakeInputs local function the paraboloid `inputs` above was built with (Fix
                // 6, post-review; extended for Task 6) — only the up-to-seven gradient readings differ here,
                // and hasFinalRebaseline (closed over, not passed here) is identical for both estimators, so
                // the corner cross-check stays apples-to-apples with the paraboloid even when the optional
                // measured final re-baseline is present.
                var cornerInputs = MakeInputs(
                    new TiltGradient(a.CornerA, a.CornerB, a.CornerMean), new TiltGradient(b.CornerA, b.CornerB, b.CornerMean),
                    new TiltGradient(c.CornerA, c.CornerB, c.CornerMean), new TiltGradient(d.CornerA, d.CornerB, d.CornerMean),
                    new TiltGradient(e.CornerA, e.CornerB, e.CornerMean), new TiltGradient(f.CornerA, f.CornerB, f.CornerMean),
                    new TiltGradient(g.CornerA, g.CornerB, g.CornerMean));
                var cornerResult = TiltCalibrationCalculator.Calibrate(cornerInputs);

                // Fix 5 (post-review) diagnostic: TiltCalibrationCalculator.EstimatorRelativeDifference's own
                // zero-magnitude guard (below) goes silent whenever either corner screw magnitude is ~0 --
                // correctly, when the paraboloid ALSO saw ~no move for that screw. But when the corner
                // estimator saw ~no move for a screw the paraboloid says was clearly turned, that is itself
                // the most diagnostic disagreement possible; log it (not a new user-facing warning -- see
                // LogDegenerateCornerScrewIfNeeded) rather than letting the guard hide it entirely.
                LogDegenerateCornerScrewIfNeeded(1,
                    TiltCalibrationCalculator.ScrewMoveMagnitude(1, inputs), TiltCalibrationCalculator.ScrewMoveMagnitude(1, cornerInputs));
                LogDegenerateCornerScrewIfNeeded(2,
                    TiltCalibrationCalculator.ScrewMoveMagnitude(2, inputs), TiltCalibrationCalculator.ScrewMoveMagnitude(2, cornerInputs));

                // The per-screw move-magnitude comparison itself is centralized in TiltCalibrationCalculator
                // (Fix 1, post-review) so the wizard and TestApp's headless validator (Task 7) never
                // hand-duplicate — and cannot drift on — this formula. It is dimensionless (a ratio-like
                // relative difference), so — like MoveMagnitudeRatio/angles/SNR above — it is safe under the
                // 1x1 pseudo-sensor fallback (a uniform scale of both estimators' deltas) and is deliberately
                // NOT gated on model != null; only the µm hardware number below needs a real lever arm.
                estimatorRelativeDifference = TiltCalibrationCalculator.EstimatorRelativeDifference(inputs, cornerInputs);

                // The recovered corner-AF hardware number needs the SAME model != null gate as the
                // paraboloid's measuredHardwareMicrons above: a model-less run's 1x1 pseudo-sensor carries no
                // real lever arm, so a µm/turn recovered against it (paraboloid OR corner) would be non-NaN
                // but physically meaningless.
                if (model != null && !double.IsNaN(cornerResult.MeasuredHardwareMicrons)) {
                    cornerMeasuredHardwareMicrons = cornerResult.MeasuredHardwareMicrons;
                }
            }

            ValidateCalibrationQuality(lastRawAngleDiff, lastMoveMagnitudeRatio, screwCount,
                measuredHardwareMicrons, pistonImpliedMicronsPerStep, estimatorRelativeDifference, cornerMeasuredHardwareMicrons);

            EvaluateRebaselineDrift();
            lastConfidence = result.Confidence;
            EvaluateCalibrationConfidence(lastConfidence);
            // [CRITICAL GATE] Persist the confidence result as the automation-trust marker — the second half
            // of the automation gate alongside DeviceLinkedCalibrationDeviceName above. Unlike that marker,
            // this one is NOT conditioned on deviceDriven: a disconnected/manual-clicking live run and a
            // replay both recompute this exact confidence from their own step readings, so their reliability
            // is just as real (and just as gating) as a device-driven run's. Reused, never invented: this is
            // the same lastConfidence.IsReliable the wizard already surfaces as the (previously non-blocking)
            // HasConfidenceWarning banner above.
            tiltAdapterOptions.CalibrationIsReliable = lastConfidence?.IsReliable ?? false;
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
            RaisePropertyChanged(nameof(HasConfidenceInfo));
            RaisePropertyChanged(nameof(ConfidenceIsReliable));
            RaisePropertyChanged(nameof(ConfidenceSummaryDisplay));
            RaisePropertyChanged(nameof(PitchUncertaintyDisplay));
            RaisePropertyChanged(nameof(PistonPitchDisplay));
            RaisePropertyChanged(nameof(HasPistonPitch));
            RaisePropertyChanged(nameof(CornerCrossCheckDisplay));
            RaisePropertyChanged(nameof(HasCornerCrossCheck));
            OnUIThread(() => ((RelayCommand)UseMeasuredHardwareCommand).NotifyCanExecuteChanged());
        }

        // Each check below is independent and returns null when it has nothing to say, so ValidateCalibrationQuality
        // stays a simple "collect the non-null messages" join no matter how many checks it ends up with (Task 5
        // adds a 4th, the corner-AF estimator disagreement, without growing this method's own logic). Behavior,
        // wording, and thresholds are unchanged from the flat-boolean version this replaced.

        // Two single-screw turns of the same amount should land at the adapter's exact expected angular spacing;
        // a bad angle gap means the recovered screw geometry is unreliable.
        private static string CheckScrewAngleGap(double rawDiff, int screwCount) {
            double expected = screwCount == 3 ? 120.0 : 90.0;
            // Fold the diff so it is in [0, 180] — both CW and CCW gaps compare to the same expected value.
            double foldedDiff = rawDiff <= 180.0 ? rawDiff : 360.0 - rawDiff;
            double deviation = Math.Abs(foldedDiff - expected);
            return deviation > 30.0
                ? $"Screw 1 and screw 2 measured {foldedDiff:F1}° apart, but {expected:F0}° was expected."
                : null;
        }

        // Two single-screw turns of the same amount should also produce ~equal gradient-change magnitudes; very
        // unequal magnitudes (uneven turning / backlash) mean the recovered hardware is unreliable.
        private const double MagnitudeRatioWarnThreshold = 1.5; // larger move > 1.5x smaller => suspect

        private static string CheckMagnitudeRatio(double magnitudeRatio) =>
            !double.IsNaN(magnitudeRatio) && magnitudeRatio > MagnitudeRatioWarnThreshold
                ? $"The two screw moves changed the tilt by very unequal amounts ({magnitudeRatio:F1}× apart)."
                : null;

        // The piston-implied pitch and the tilt-derived measured hardware are both focuser-frame quantities
        // (see TiltCalibrationCalculator.PistonImpliedMicronsPerStep), so honest agreement is expected; a gap
        // this large means the tilt estimator (or the mechanics on pull-side moves) is off.
        private const double PistonDisagreementWarnThreshold = 0.20;

        private static string CheckPistonAgreement(double measuredHardware, double pistonImplied) {
            if (measuredHardware <= 0 || pistonImplied <= 0) {
                return null;
            }
            // Denominator is the tilt-derived measuredHardware, not a symmetric mean of the two: the question
            // this check answers is "does the tilt estimate look unreliable" (framed against the piston's free,
            // tilt-fit-independent value as the reference), not "how far apart are these two numbers" in the
            // abstract.
            double relativeDiff = Math.Abs(pistonImplied - measuredHardware) / measuredHardware;
            // Percent is formatted by hand rather than with ":P0": the "P" specifier inserts a space before
            // the sign in most cultures ("17 %"), which reads as a typo in running prose.
            return relativeDiff > PistonDisagreementWarnThreshold
                ? $"The piston-implied pitch ({pistonImplied:0.##} µm) and the tilt-derived pitch ({measuredHardware:0.##} µm) differ by {relativeDiff * 100.0:F0}%."
                : null;
        }

        // The corner-region AF plane is a second, independent estimator of the same screw moves (see the
        // design doc's §2 investigation): on a real run the per-star paraboloid model was found to shrink one
        // screw's gradient by a uniform factor relative to the corner estimator, producing a spurious "unequal
        // screw turns" warning the corner estimator did not share. A large disagreement between the two
        // doesn't say which estimator is right, but it does say the paraboloid-derived pitch reading may not
        // be trustworthy — flag it for the user rather than silently trusting (or blending) either one.
        private const double EstimatorDisagreementWarnThreshold = 0.15;

        private static string CheckCornerCrossCheck(double estimatorRelDiff, double cornerMeasuredHardwareMicrons) {
            if (double.IsNaN(estimatorRelDiff) || estimatorRelDiff <= EstimatorDisagreementWarnThreshold) {
                return null;
            }
            return $"The per-star model and the corner-region AF differ by {estimatorRelDiff * 100.0:F0}% on the screw " +
                $"moves, and the corner-region AF puts the pitch at {cornerMeasuredHardwareMicrons:0.###} µm.";
        }

        private void ValidateCalibrationQuality(double rawDiff, double magnitudeRatio, int screwCount,
            double measuredHardware, double pistonImplied, double estimatorRelDiff, double cornerMeasuredHardwareMicrons) {
            string angleGap = CheckScrewAngleGap(rawDiff, screwCount);
            string ratio = CheckMagnitudeRatio(magnitudeRatio);
            string piston = CheckPistonAgreement(measuredHardware, pistonImplied);
            string corner = CheckCornerCrossCheck(estimatorRelDiff, cornerMeasuredHardwareMicrons);
            var parts = new[] { angleGap, ratio, piston, corner }.Where(p => p != null).ToList();

            HasWarning = parts.Count > 0;
            if (!HasWarning) {
                WarningText = string.Empty;
                return;
            }

            // The closing advice depends on WHICH checks fired. An unequal-magnitude reading on its own is
            // most likely what it looks like: the two turns really were different sizes. But when a second,
            // independent estimator also disagrees with the per-star model, the screw moves are no longer the
            // most likely culprit -- telling the user to re-turn the screws would send them after the wrong
            // thing, and on a motorized run it is advice they cannot even act on.
            string closing = piston != null || corner != null
                ? "An independent estimator disagrees with the per-star model, so suspect the measurement before the hardware. Re-run on a star-rich field, or raise Measurements to average."
                : "Turn each screw by the same amount and recalibrate.";
            WarningText = string.Join(" ", parts) + " " + closing;
        }

        // Re-baseline drift: each re-baseline (c, e) should return close to the prior state. A large residual
        // relative to the subsequent screw move means backlash / an uneven undo contaminated the calibration.
        // Deliberately does NOT evaluate ReBaseline3 (Task 6, optional): RebaselineDriftRatio needs a
        // SUBSEQUENT screw move to form its ratio against (drift1 vs Screw1's move, drift2 vs Screw2's move),
        // and ReBaseline3 has none -- it is immediately followed by Complete, which is itself a no-move once
        // ReBaseline3 has run. Its magnitude is not lost, just measured differently: it feeds
        // TiltCalibrationConfidence.Rebaseline3Drift, an absolute noise-probe term in ComputeConfidence's SNR
        // RMS, rather than a relative-to-the-next-move ratio here. Do not "fix" this by inventing a ratio for
        // it against some other reference -- there isn't a subsequent move to be honest about.
        private void EvaluateRebaselineDrift() {
            bool measuredCurvature = stepReadings.ContainsKey(WizardStep.AllInward);
            var a = Reading(WizardStep.Baseline);
            var c = measuredCurvature ? Reading(WizardStep.ReBaseline1) : Reading(WizardStep.Baseline);
            var d = Reading(WizardStep.Screw1);
            var e = Reading(WizardStep.ReBaseline2);
            var f = Reading(WizardStep.Screw2);

            // In the 4-step flow c IS a (drift1 would be a meaningless 0); the explicit NaN keeps the
            // warning semantics clean — only the 6-step flow has a re-baseline-1 to evaluate.
            double drift1 = measuredCurvature
                ? TiltCalibrationCalculator.RebaselineDriftRatio(c.A - a.A, c.B - a.B, d.A - c.A, d.B - c.B)
                : double.NaN;
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

        // Overall calibration signal-to-noise from the per-step tilt vectors (six, or seven with the optional
        // measured final re-baseline). A low SNR means the recovered screw geometry is dominated by
        // measurement noise / between-step drift (typically too few stars or a too-coarse focus step),
        // regardless of how cleanly the screws were turned — warn the user not to apply it.
        private void EvaluateCalibrationConfidence(TiltCalibrationConfidence confidence) {
            var disagreement = curvatureChannelDisagreement;
            if (confidence == null || confidence.IsReliable) {
                HasConfidenceWarning = disagreement != null;
                ConfidenceWarningText = disagreement ?? string.Empty;
                return;
            }
            HasConfidenceWarning = true;
            ConfidenceWarningText =
                $"Low calibration confidence: signal-to-noise {confidence.SignalToNoise:F1} (need ≥ {TiltCalibrationCalculator.MinReliableSignalToNoise:F0}), " +
                $"predicted screw-direction error ±{confidence.PredictedAngleUncertaintyDeg:F0}°. The tilt-measurement noise rivals the " +
                "screw-move signal — usually too few stars or a too-coarse focus step (calibrate on a star-rich field with a finer step), " +
                "or drift between steps. Re-capture before applying these screw angles." +
                (disagreement == null ? string.Empty : " " + disagreement);
        }

        // The disagreement text from the last 6-step run's cross-check, or null. Held between
        // EvaluateCurvatureChannelCrossCheck (which runs early in RunCalibrationMath, where the step readings
        // are in hand) and EvaluateCalibrationConfidence (which owns the banner).
        private string curvatureChannelDisagreement;

        /// <summary>
        /// How many times larger than the drift scale the a→b curvature-effect change must be before a
        /// disagreement is worth surfacing. The optical channel is usually drift-buried, so this is a coarse
        /// "obviously bigger than the noise" test, not a statistical one.
        /// </summary>
        private const double CurvatureCrossCheckDriftMultiple = 2.0;

        /// <summary>
        /// WARNING-ONLY cross-check of the measured σ against the OTHER channel (design §2.4, Q3 resolved
        /// "adopt" by the user 2026-08-04).
        ///
        /// σ is measured from the mean best-focus change of the all-screws step — the geometric channel. The
        /// curvature effect responds to the same piston, and σ is DEFINED as the sign of that response, so the
        /// two are independent measurements of one quantity: one geometric, one optical. Before the sign fix
        /// they agreed by construction; corrected, a disagreement is real information.
        ///
        /// It NEVER blocks, never prompts, and never changes the stored sign. The optical channel is usually
        /// buried in secular drift (in the reference session Kx slid monotonically across all six steps,
        /// including pure-tilt moves that changed no spacing), so it must not be allowed to veto the robust
        /// channel — which also means a quiet panel is weak evidence of agreement, not proof of it. Hence the
        /// scale test: the change must clear <see cref="CurvatureCrossCheckDriftMultiple"/>× the drift seen
        /// across the two re-baselines, which is the only per-run estimate of that drift available.
        ///
        /// Reads no focuser convention: both channels are z-space, so k is irrelevant here by construction.
        /// </summary>
        /// <returns>The warning text, or null when the channels agree or the change is within the noise.</returns>
        private static string EvaluateCurvatureChannelCrossCheck(
                StepReading baseline, StepReading allInward, StepReading reBaseline1, StepReading reBaseline2, int measuredSign) {
            double deltaE = allInward.CurvatureEffectAtScrewRadiusMicrons - baseline.CurvatureEffectAtScrewRadiusMicrons;
            if (!double.IsFinite(deltaE) || deltaE == 0.0 || measuredSign == 0) {
                return null;
            }

            // The re-baselines return the adapter to a previously-held state, so any curvature-effect change
            // across them is drift, not signal. RMS of the two, mirroring ComputeConfidence's noise estimate.
            double drift1 = reBaseline1.CurvatureEffectAtScrewRadiusMicrons - baseline.CurvatureEffectAtScrewRadiusMicrons;
            double drift2 = reBaseline2.CurvatureEffectAtScrewRadiusMicrons - reBaseline1.CurvatureEffectAtScrewRadiusMicrons;
            if (!double.IsFinite(drift1) || !double.IsFinite(drift2)) {
                return null;
            }
            double driftScale = Math.Sqrt((drift1 * drift1 + drift2 * drift2) / 2.0);

            bool agrees = Math.Sign(deltaE) == Math.Sign(measuredSign);
            bool clearsNoise = Math.Abs(deltaE) > CurvatureCrossCheckDriftMultiple * driftScale;
            if (agrees || !clearsNoise) {
                return null;
            }

            string text =
                $"Direction cross-check disagrees: the all-screws step moved the curvature effect at screw radius by " +
                $"{deltaE:+0.0;-0.0} µm (re-baseline drift ≈ {driftScale:F1} µm), which implies the opposite adapter direction " +
                $"from the mean best-focus measurement that set ScrewInwardCurvatureSign = {measuredSign:+0;-0}. The mean-focus " +
                "channel is the more robust of the two and has been kept; this is informational. If corrections turn out to " +
                "make aberrations worse, re-run the 6-step calibration on a star-rich field.";
            Logger.Warning(
                $"Tilt calibration: curvature-effect cross-check disagrees with the measured direction. " +
                $"ΔE(a→b) = {deltaE:+0.0;-0.0} µm at screw radius, re-baseline drifts {drift1:+0.0;-0.0} / {drift2:+0.0;-0.0} µm " +
                $"(RMS {driftScale:F1}); measured ScrewInwardCurvatureSign = {measuredSign:+0;-0}. The stored sign is unchanged — " +
                "the mean-focus channel is the robust one (see docs/focuser-direction-convention-design.md §2.4).");
            return text;
        }

        private StepReading Reading(WizardStep step) =>
            stepReadings.TryGetValue(step, out var r) ? r : default;

        // Test seam: seeds a step reading with the fields the calibration math consumes, so unit tests can
        // exercise NextStep/RunCalibrationMath without running the inspector. StepReading and stepReadings
        // stay private — this is the only external write path. The corner fields default to NaN (unavailable),
        // matching production: a step whose corner plane wasn't captured never contributes a real reading.
        internal void SeedStepReading(WizardStep step, double a, double b, double mean,
                double curvatureEffectAtScrewRadiusMicrons = 0.0,
                double cornerA = double.NaN, double cornerB = double.NaN, double cornerMean = double.NaN) {
            stepReadings[step] = new StepReading {
                A = a,
                B = b,
                Mean = mean,
                CurvatureEffectAtScrewRadiusMicrons = curvatureEffectAtScrewRadiusMicrons,
                CornerA = cornerA,
                CornerB = cornerB,
                CornerMean = cornerMean
            };
        }

        // Test seam: set the abandoned-run latch directly, so a test can prove StartAsync clears it WITHOUT
        // going through Restart first (Restart clears it too, which would otherwise mask a missing clear).
        internal void SetDeviceRunAbandonedForTest(bool value) {
            deviceRunAbandoned = value;
            NotifyCommandsCanExecuteChanged();
        }

        // Test seam: run the calibration math over the seeded step readings (mirrors NextStep -> Complete). The
        // optional overrides supply the sensor geometry a live run reads from the inspector/profile + the paraboloid
        // tilt-plane model (all of which the seed path bypasses), so RunCalibrationMath's hardware-recovery + pitch
        // branch can run under test.
        internal void RunCalibrationForTest(
            double? radiusMm = null, double? pixelSizeMicrons = null, double? focuserStepMicrons = null,
            TiltPlaneModel tiltPlaneOverride = null, bool deviceDriven = false) {
            calibrationTiltPlaneOverrideForTest = tiltPlaneOverride;
            try {
                RunCalibrationMath(
                    tiltAdapterOptions.ScrewCount,
                    radiusMm ?? tiltAdapterOptions.ScrewRadiusMillimeters,
                    pixelSizeMicrons ?? profileService.ActiveProfile.CameraSettings.PixelSize,
                    focuserStepMicrons ?? EffectiveFocuserStepMicrons(),
                    CalibrationAppliedAmount,
                    IsStepperAdjustment,
                    deviceDriven);
            } finally {
                calibrationTiltPlaneOverrideForTest = null;
            }
        }

        // ---- Replay -----------------------------------------------------------------------------------------

        // Replays a saved calibration run from a folder: reads metadata.json, re-analyzes each step from its saved
        // frames, and recomputes the calibration. A single "Replay" button opens the shared replay-settings modal,
        // which offers three modes (see TiltReplayModeResolver):
        // - Use current settings: current profile star-detection + current tilt geometry (metadata does not override).
        // - Use capture-time settings in memory: the run's stored star-detection as a transient per-step override
        //   (profile untouched) + the run's stored geometry — reproduces the original calibration exactly.
        // - Update profile to capture-time: persist the run's star-detection settings to the live profile, then replay
        //   against it + the run's stored geometry.
        // Test seam: when set, ReplayAsync uses this instead of opening a real (modal, WinForms)
        // FolderBrowserDialog. Called with the current SaveAFRunsPath (the dialog's initial selection in
        // production); returns the folder to replay, or null/empty to simulate the dialog being cancelled.
        // Always null in production.
        internal Func<string, string> SelectReplayFolderForTest { get; set; }

        // Test seam: when set, ReplayAsync uses this instead of showing the real (WPF-window) shared
        // replay-settings modal (ReplaySettingsPrompt.ShowAsync). Always null in production.
        internal Func<AutoFocusReplayMetadata, Task<ReplaySettingsChoice>> SelectReplaySettingsForTest { get; set; }

        // Test seam: mirrors MeasurementStepOverrideForTest for the live wizard flow — when set, ReplayAsync
        // uses this instead of invoking the live inspector's AnalyzeAutoFocusFromSavedPath for each step (the
        // reading itself still comes from CalibrationTiltPlane, i.e. CalibrationTiltPlaneOverrideForTest
        // above). Always null in production.
        internal Func<WizardStep, CancellationToken, Task<bool>> ReplayStepOverrideForTest { get; set; }

        private async Task ReplayAsync() {
            string folder;
            if (SelectReplayFolderForTest != null) {
                folder = SelectReplayFolderForTest(SaveAFRunsPath);
                if (string.IsNullOrEmpty(folder)) {
                    return;
                }
            } else {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                    if (!string.IsNullOrEmpty(SaveAFRunsPath)) {
                        dialog.SelectedPath = SaveAFRunsPath;
                    }
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
                        return;
                    }
                    folder = dialog.SelectedPath;
                }
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

            // Resolve each step folder against the run directory the user selected, so a run that was moved or copied
            // (its stored paths now pointing at the original capture location) still replays. Relative entries (the
            // current format) rebase onto the selected folder; legacy absolute entries are honored as-is.
            var byStep = (metadata.RunStepMapping ?? new List<TiltRunStepMapping>())
                .GroupBy(m => m.Step, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => TiltCalibrationMetadata.ResolveStepFolder(folder, g.Last().Folder), StringComparer.OrdinalIgnoreCase);
            // A run saved without the curvature steps replays as a 4-step run; a run saved without the
            // optional measured final re-baseline (older runs, or one captured with the option off) replays
            // without ReBaseline3 -- so HasFinalRebaseline (read fresh from stepReadings in RunCalibrationMath)
            // correctly comes back false for those, keeping Screw2Delta's old ReBaseline2-only semantics.
            bool replayHasCurvatureSteps = byStep.ContainsKey(WizardStep.AllInward.ToString());
            bool replayHasFinalRebaseline = byStep.ContainsKey(WizardStep.ReBaseline3.ToString());
            var replaySteps = GetMeasurementSteps(replayHasCurvatureSteps, replayHasFinalRebaseline);
            activeMeasurementSteps = replaySteps;
            RaisePropertyChanged(nameof(StepProgressDisplay)); // step count (4 vs 6) may differ from the prior run
            foreach (var step in replaySteps) {
                if (!byStep.ContainsKey(step.ToString())) {
                    Notification.ShowError($"metadata.json has no saved folder for step '{step}'. Cannot replay.");
                    return;
                }
            }

            // Consolidated replay: one button, three modes resolved by the shared replay-settings modal (seeded from a
            // representative per-step AutoFocus replay snapshot). Cancelling / closing the modal aborts.
            var representativeStepFolder = byStep[replaySteps.First().ToString()];
            AutoFocusReplayMetadata.TryLoad(representativeStepFolder, out var replayMetadata, out _);
            var choice = SelectReplaySettingsForTest != null
                ? await SelectReplaySettingsForTest(replayMetadata)
                : await ReplaySettingsPrompt.ShowAsync(windowServiceFactory, replayMetadata, ReplaySettingsPromptTexts.TiltCalibration);
            if (choice == ReplaySettingsChoice.Cancel) {
                return;
            }
            var mode = TiltReplayModeResolver.Resolve(choice);

            // The run's representative capture-time star-detection snapshot: used to warn when the run stored none, and
            // — for "update profile" — to persist to the live profile AFTER a successful replay. Reuse the metadata
            // already loaded above when present; otherwise build it (which also overlays legacy optimized settings).
            var captureTimeSnapshot = mode.ApplyCaptureTimeOverridePerStep
                ? (replayMetadata?.StarDetection ?? BuildTiltReplayDetectionOverride(representativeStepFolder, metadata))
                : null;
            if (mode.ApplyCaptureTimeOverridePerStep && captureTimeSnapshot == null) {
                Notification.ShowWarning(mode.UpdateProfileToCaptureTime
                    ? "This saved run has no stored star-detection settings, so it will replay with your current settings and your profile will not be changed."
                    : "This saved run has no stored star-detection settings, so it will replay with your current settings.");
            }
            // Failure/cancel happens before the profile write below, so the profile is never left half-changed.
            var profileNotChangedNote = mode.UpdateProfileToCaptureTime
                ? " Your profile was not changed (it is only updated after a successful replay)."
                : string.Empty;

            // Replaying with current geometry applies the current screw count to the saved deltas; if the saved run
            // used a different screw count, the angle fit is meaningless. Warn rather than silently corrupt.
            if (!mode.UseMetadataGeometry && metadata.NumberOfScrews != tiltAdapterOptions.ScrewCount) {
                Notification.ShowWarning($"The saved run used {metadata.NumberOfScrews} screws but the current setting is " +
                    $"{tiltAdapterOptions.ScrewCount}. Replaying with current settings may produce incorrect angles.");
            }

            measureCts?.Dispose();
            measureCts = new CancellationTokenSource();
            var token = measureCts.Token;

            IsReplaying = true;
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
            var ctx = metadata.MeasurementContext;
            var prevFocuserOverride = inspector.SensorModelFocuserSizeOverrideMicrons;
            var prevFRatioOverride = inspector.SensorModelFRatioOverride;
            if (mode.ApplyCaptureTimeOverridePerStep && ctx != null) {
                if (!double.IsNaN(ctx.MicronsPerFocuserStep) && ctx.MicronsPerFocuserStep > 0)
                    inspector.SensorModelFocuserSizeOverrideMicrons = ctx.MicronsPerFocuserStep;
                if (!double.IsNaN(ctx.FocalRatio) && ctx.FocalRatio > 0)
                    inspector.SensorModelFRatioOverride = ctx.FocalRatio;
                var currentCtx = CaptureMeasurementContext(inspector.InspectorOptions, HocusFocusPlugin.AutoFocusOptions,
                    profileService.ActiveProfile.TelescopeSettings.FocalRatio,
                    profileService.ActiveProfile.TelescopeSettings.FocalLength);
                var drift = DescribeMeasurementContextDrift(ctx, currentCtx);
                if (!string.IsNullOrEmpty(drift))
                    Notification.ShowWarning($"Replaying with captured measurement settings; your current profile differs: {drift}.");
            }
            try {
                foreach (var step in replaySteps) {
                    token.ThrowIfCancellationRequested();
                    // Mirror the live path's step advance (NextStep -> CurrentStep). The wizard header is entirely
                    // derived from currentStep -- StepProgressDisplay ("Step N of M"), StepTitle and StepInstructions
                    // are re-raised ONLY by this setter -- and Panel B is on screen for the whole replay, so without
                    // this the header stays frozen on the pre-replay step (every step reading "Step 1 of 6" under a
                    // "Baseline Measurement" heading). Assigned through the property, not the field, for that reason.
                    // Safe mid-replay despite the setter's NotifyCommandsCanExecuteChanged: IsMeasuring is true for
                    // the whole run, which is what actually gates every measurement command's canExecute.
                    CurrentStep = step;
                    StatusText = $"Replaying {StepTitleText(step)}...";
                    // Capture-time modes ("use captured in memory" and "update profile") replay each step with its own
                    // detached capture-time star-detection snapshot as an override, so the live profile is untouched
                    // during the replay. "Use current settings" passes null and uses the current profile.
                    var detectionOverride = mode.ApplyCaptureTimeOverridePerStep
                        ? BuildTiltReplayDetectionOverride(byStep[step.ToString()], metadata)
                        : null;
                    bool ok;
                    if (ReplayStepOverrideForTest != null) {
                        ok = await ReplayStepOverrideForTest(step, token);
                    } else {
                        // Force the per-star sensor-curve model so replay derives tilt from the paraboloid, like a live run.
                        var prevForce = inspector.ForceSensorCurveModelGeneration;
                        inspector.ForceSensorCurveModelGeneration = true;
                        try {
                            ok = await inspector.AnalyzeAutoFocusFromSavedPath(byStep[step.ToString()], token, detectionOverride);
                        } finally {
                            inspector.ForceSensorCurveModelGeneration = prevForce;
                        }
                    }
                    // Failure messages keep the raw WizardStep name: it matches the saved step folder on disk
                    // (StepFolderName -> "03_ReBaseline1" / "05_ReBaseline2"), whereas StepTitleText maps BOTH
                    // re-baseline steps to "Return to Baseline". The toast outlives the panel (a failed replay
                    // collapses it), so it is the only thing telling the user which folder to look at.
                    if (!ok) {
                        StatusText = $"Replay failed at {step}.";
                        Notification.ShowError($"Replay failed at step '{step}'. The saved frames could not be analyzed.{profileNotChangedNote}");
                        return;
                    }
                    var m = CalibrationTiltPlane;
                    if (m == null) {
                        StatusText = $"Replay produced no tilt model at {step}.";
                        Notification.ShowError($"Replay produced no sensor-curve-model tilt at step '{step}'. The per-star paraboloid could not be fit.{profileNotChangedNote}");
                        return;
                    }
                    var (mGx, mGy) = StateGradient(m.A, m.B, m);
                    // Task 5: a replay re-analyzes saved frames through the same inspector chain as a live run,
                    // so the corner-region plane is captured the same way (single reading — replay has no
                    // measurement-averaging loop to accumulate across). NaN when unavailable, same as the live
                    // capture path in RunAveragedMeasurement.
                    var corner = cornerTiltPlaneOverrideForTest ?? inspector.TiltModel?.TiltPlaneModel;
                    var reading = new StepReading {
                        A = m.A,
                        B = m.B,
                        Mean = m.MeanFocuserPosition,
                        TiltAngleDeg = ComputeTiltAngleDeg(m.A, m.B, m),
                        DirectionDeg = NormalizeAngle(Math.Atan2(mGx, -mGy) * 180.0 / Math.PI),
                        CornerA = corner?.A ?? double.NaN,
                        CornerB = corner?.B ?? double.NaN,
                        CornerMean = corner?.MeanFocuserPosition ?? double.NaN
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
                    // Written directly (not via the CalibrationAppliedAmount property setter) so the two
                    // dependent properties below are ALWAYS re-raised, even if the replayed value happens
                    // to match what's already persisted (the property setter's no-op guard would otherwise
                    // skip the raise, leaving a stale value on screen if the user has a pending edit).
                    tiltAdapterOptions.CalibrationAppliedAmount = metadata.CalibrationAppliedAmount;
                    RaisePropertyChanged(nameof(CalibrationAppliedAmount));
                    RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));
                    // [CRITICAL GATE] deviceDriven: false — a replay never sends live device moves (it
                    // re-analyzes already-captured frames), so it can never re-establish the wizard-screw <->
                    // device-corner correspondence, regardless of whether a device happens to be connected
                    // right now. Always clears any prior device-linked marker.
                    RunCalibrationMath(
                        metadata.NumberOfScrews,
                        metadata.ScrewRadiusMillimeters,
                        metadata.PixelSizeMicrons,
                        metadata.FocuserStepSizeMicrons,
                        metadata.CalibrationAppliedAmount,
                        metadata.IsStepperAdjustment,
                        deviceDriven: false);
                } else {
                    // Use the current profile / tilt-adapter settings, exactly as a live calibration would.
                    // [CRITICAL GATE] deviceDriven: false for the same reason as the branch above.
                    RunCalibrationMath(
                        tiltAdapterOptions.ScrewCount,
                        tiltAdapterOptions.ScrewRadiusMillimeters,
                        profileService.ActiveProfile.CameraSettings.PixelSize,
                        EffectiveFocuserStepMicrons(),
                        CalibrationAppliedAmount,
                        IsStepperAdjustment,
                        deviceDriven: false);
                }
                RebuildDiagram();
                // Update-profile choice: the replay succeeded, so now persist the run's capture-time star-detection
                // settings to the live profile (no-op if the run stored none — the user was warned above).
                if (mode.UpdateProfileToCaptureTime && captureTimeSnapshot != null) {
                    OnUIThread(() => HocusFocusPlugin.StarDetectionOptions?.ApplyFullSnapshot(captureTimeSnapshot));
                }
                StatusText = "Replay complete.";
                CurrentStep = WizardStep.Complete;
                completed = true;
            } catch (OperationCanceledException) {
                StatusText = "Replay cancelled.";
            } catch (Exception ex) {
                Notification.ShowError($"Replay failed: {ex.Message}{profileNotChangedNote}");
                Logger.Error(ex, "Tilt calibration replay failed");
            } finally {
                // The live profile is only persisted on a successful "update profile to capture-time" replay (above), so
                // a cancelled/failed replay leaves it untouched — nothing to restore. The in-memory per-step override
                // never touches the profile either.
                IsReplaying = false;
                IsMeasuring = false;
                inspector.SensorModelFocuserSizeOverrideMicrons = prevFocuserOverride;
                inspector.SensorModelFRatioOverride = prevFRatioOverride;
                // A successful replay ends on the Complete panel (like a live run); a failed/cancelled replay
                // returns to the idle config panel so the user can retry instead of being stuck mid-run.
                if (!completed) {
                    IsWizardRunning = false;
                }
            }
        }

        /// <summary>
        /// Builds a detached, in-memory capture-time star-detection override for the no-mutation tilt replay.
        /// Resolution order: a per-step replay <c>metadata.json</c> if present (tilt captures don't currently emit
        /// one); then the run-level full <see cref="TiltCalibrationMetadata.StarDetectionSnapshot"/>, which pins every
        /// knob; then, for older runs, overlaying the curated <see cref="OptimizedStarDetectionSettings"/> onto a
        /// snapshot of the current options (legacy "apply optimized settings" behavior, without mutating the profile).
        /// Returns null when there is nothing to override (the replay then uses current settings).
        /// </summary>
        internal static IStarDetectionOptions BuildTiltReplayDetectionOverride(string stepFolder, TiltCalibrationMetadata tiltMetadata) {
            if (AutoFocusReplayMetadata.TryLoad(stepFolder, out var replayMetadata, out _) && replayMetadata.StarDetection != null) {
                return replayMetadata.StarDetection;
            }
            // Run-level full snapshot (all 53 knobs) — the effective full-pin path, since tilt captures don't emit
            // per-step replay metadata. Preferred over the curated overlay so no non-curated knob leaks from the profile.
            if (tiltMetadata?.StarDetectionSnapshot != null) {
                return tiltMetadata.StarDetectionSnapshot;
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
        // snapshot's current-options values). Internal so OverlayOptimizedSettings_AppliesEveryCuratedKnob can guard
        // it against silently dropping a knob that OptimizedStarDetectionSettings persists.
        internal static void OverlayOptimizedSettings(StarDetectionSettingsSnapshot snapshot, OptimizedStarDetectionSettings s) {
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
            snapshot.LocallyAdaptiveBinarization = s.LocallyAdaptiveBinarization;
            snapshot.AdaptiveNoiseBlockSize = s.AdaptiveNoiseBlockSize;
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
                // Store the folder relative to the run root so the saved run replays after being moved/copied.
                currentMetadata.RunStepMapping.Add(new TiltRunStepMapping {
                    Step = stepName,
                    Folder = TiltCalibrationMetadata.ToRelativeStepFolder(runRootFolder, reading.SaveFolder)
                });
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
                CurvatureEffectMicronsAtScrewRadius = reading.CurvatureEffectAtScrewRadiusMicrons,
                CornerTiltPlaneA = reading.CornerA,
                CornerTiltPlaneB = reading.CornerB,
                CornerMeanFocuserPosition = reading.CornerMean
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
                MoveMagnitudeRatio = lastMoveMagnitudeRatio,
                SignalToNoise = lastConfidence?.SignalToNoise ?? double.NaN,
                PredictedAngleUncertaintyDeg = lastConfidence?.PredictedAngleUncertaintyDeg ?? double.NaN,
                PitchUncertaintyMicrons = pitchUncertaintyMicrons,
                ConfidenceIsReliable = lastConfidence?.IsReliable ?? false,
                PistonImpliedMicronsPerStep = pistonImpliedMicronsPerStep,
                CornerMeasuredHardwareMicrons = cornerMeasuredHardwareMicrons,
                EstimatorRelativeDifference = estimatorRelativeDifference,
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
            // RebuildDiagram is the single choke point reached on every calibration, direction, and
            // profile change — refresh the readout's physical-angle properties here too (before the
            // early-return guard, so a Clear also updates them).
            RaisePropertyChanged(nameof(PhysicalScrew1AngleDegrees));
            RaisePropertyChanged(nameof(PhysicalScrew2AngleDegrees));
            RaisePropertyChanged(nameof(PhysicalScrew3AngleDegrees));
            RaisePropertyChanged(nameof(PhysicalScrew4AngleDegrees));

            ScrewDiagramItems.Clear();
            ScrewConnectionLines.Clear();

            int n = tiltAdapterOptions.ScrewCount;
            if (n < 3 || !IsCalibrationValid) return;

            // Stored angles are RESPONSE-convention; the diagram depicts image space ("0° points up;
            // image as shown in NINA"), so plot the PHYSICAL position — 180° from stored on −1 rigs,
            // identical on +1. Self-inverse PhysicalToStoredAngle converts stored→physical.
            int sign = tiltAdapterOptions.ScrewInwardCurvatureSign;
            int focuserSign = FocuserSign;
            var angles = new[] {
                TiltScrewGeometry.PhysicalToStoredAngle(tiltAdapterOptions.Screw1AngleDegrees, sign, focuserSign),
                TiltScrewGeometry.PhysicalToStoredAngle(tiltAdapterOptions.Screw2AngleDegrees, sign, focuserSign),
                TiltScrewGeometry.PhysicalToStoredAngle(tiltAdapterOptions.Screw3AngleDegrees, sign, focuserSign),
                n == 4 ? TiltScrewGeometry.PhysicalToStoredAngle(tiltAdapterOptions.Screw4AngleDegrees, sign, focuserSign) : double.NaN
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

        // Fire-and-forget marshaling for NINA's device-info broadcasts. Those arrive on the CameraVM/FocuserVM
        // DeviceUpdateTimer, and ApplicationDeviceConnectionVM.Shutdown() awaits that timer to stop while the UI
        // thread is blocked inside AsyncContext.Run — which pumps its own queue, never the WPF dispatcher. A
        // blocking OnUIThread here would wait on a UI thread that is waiting on us: NINA hangs on exit. Only the
        // UI cares about the result (bindings + CanExecute), so queueing it is sufficient.
        private void PostToUIThread(Action action) => applicationDispatcher.PostSynchronizationContext(action);

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
            ((AsyncRelayCommand)RetryMeasurementCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ConnectDeviceCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)DisconnectDeviceCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)AutoRunAllCommand).NotifyCanExecuteChanged();
        }

        private static double NormalizeAngle(double deg) => ((deg % 360) + 360) % 360;
    }
}
