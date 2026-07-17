#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Imaging.Filters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.SerialCommunication;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Review;
using NINA.Joko.Plugins.HocusFocus.Controls;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Scottplot;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel;
using NINA.WPF.Base.ViewModel.AutoFocus;
using OxyPlot;
using OxyPlot.Series;
using ScottPlot;
using ScottPlot.Statistics;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static NINA.Joko.Plugins.HocusFocus.Inspection.SensorModel;
using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;
using DrawingColor = System.Drawing.Color;
using Logger = NINA.Core.Utility.Logger;
using MyMessageBox = NINA.Core.MyMessageBox.MyMessageBox;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using SPPlot = ScottPlot.Plot;
using SPVector2 = ScottPlot.Statistics.Vector2;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    [PartCreationPolicy(CreationPolicy.Shared)]
    [Export(typeof(IDockableVM))]
    [Export]
    public class InspectorVM : DockableVM, IScottPlotController, ICameraConsumer, IFocuserConsumer, ITelescopeConsumer {
        private static readonly FocusPointComparer focusPointComparer = new FocusPointComparer();
        private static readonly PlotPointComparer plotPointComparer = new PlotPointComparer();

        private readonly IStarDetectionOptions starDetectionOptions;
        private readonly IStarAnnotatorOptions starAnnotatorOptions;
        private readonly IInspectorOptions inspectorOptions;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly ICameraMediator cameraMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IAutoFocusOptions autoFocusOptions;
        private readonly IAutoFocusEngineFactory autoFocusEngineFactory;
        private readonly IImageDataFactory imageDataFactory;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        private readonly IApplicationDispatcher applicationDispatcher;
        private readonly IProgress<ApplicationStatus> progress;
        private readonly ITiltAdapterOptions tiltAdapterOptions;

        // Shared motorized-device connection singleton (HocusFocusPlugin.TiltDeviceConnectionService in
        // production, injected in tests; null-tolerant so device-less test rigs stay valid — Automatic
        // Adjustment then degrades to "not connected"/disabled, mirroring TiltAdapterWizardVM's pattern).
        private readonly TiltDeviceConnectionService tiltDeviceConnectionService;

        // Yes/No confirmation prompt for the Automatic Adjustment flow (re-run to confirm; revert on
        // mid-plan failure; revert on post-adjustment worsening). Injectable for tests; production default
        // shows NINA's message box (ShowYesNoPromptAsync), mirroring TiltAdapterWizardVM's confirmIdleDisconnectAsync.
        private readonly Func<string, string, Task<bool>> confirmPromptAsync;

        // Shows the tilt-device adjustment approval dialog and returns the user's choice. Injectable so tests
        // can drive the Automatic Adjustment flow (cancel / approve with a specific plan) WITHOUT opening a
        // real WPF window — TiltDeviceAdjustmentPrompt.ShowAsync's ShowDialog call requires a live UI
        // dispatcher that unit tests don't have. Production default delegates to the real dialog
        // (DefaultShowAdjustmentPromptAsync). Signature mirrors TiltDeviceAdjustmentPrompt.ShowAsync minus the
        // windowServiceFactory parameter (fixed to this VM's own windowServiceFactory field in the default).
        private readonly Func<Func<bool, bool, TiltDevicePlanPreview>, bool, string, bool, Task<TiltDeviceAdjustmentChoice>> showAdjustmentPromptAsync;

        // The confirming re-run after a successful adjustment. Injectable so tests can verify "re-run
        // invoked on accept" without exercising the full AutoFocus engine pipeline (AnalyzeAutoFocus requires
        // a working IAutoFocusEngine/camera/focuser and is exercised on its own elsewhere). Production default
        // is the real AnalyzeAutoFocus(ct, captureCameraBlock: true).
        private readonly Func<CancellationToken, Task<bool>> reRunAnalysisAsync;

        // WindowServiceFactory is not a MEF export (NINA exposes the concrete type with a default ctor only), so it
        // is instantiated directly here, mirroring HocusFocusPlugin — used to show the modal Review Frames dialog.
        private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();

        // SignalAmplificationSummary reads the ACTIVE profile's FocuserSettings; these track the settings
        // object currently subscribed so in-place edits refresh the summary and profile swaps re-hook cleanly.
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

        [ImportingConstructor]
        public InspectorVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            IImagingMediator imagingMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            IFilterWheelMediator filterWheelMediator,
            ITelescopeMediator telescopeMediator,
            IImageDataFactory imageDataFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector)
            : this(profileService, applicationStatusMediator, imagingMediator, cameraMediator, focuserMediator, filterWheelMediator, telescopeMediator, HocusFocusPlugin.StarDetectionOptions, HocusFocusPlugin.StarAnnotatorOptions, HocusFocusPlugin.InspectorOptions, HocusFocusPlugin.AutoFocusOptions, HocusFocusPlugin.AutoFocusEngineFactory,
                  imageDataFactory, starDetectionSelector, starAnnotatorSelector, HocusFocusPlugin.ApplicationDispatcher, HocusFocusPlugin.AlglibAPI, HocusFocusPlugin.TiltAdapterOptions, HocusFocusPlugin.TiltDeviceConnectionService) {
        }

        public InspectorVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            IImagingMediator imagingMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            IFilterWheelMediator filterWheelMediator,
            ITelescopeMediator telescopeMediator,
            IStarDetectionOptions starDetectionOptions,
            IStarAnnotatorOptions starAnnotatorOptions,
            IInspectorOptions inspectorOptions,
            IAutoFocusOptions autoFocusOptions,
            IAutoFocusEngineFactory autoFocusEngineFactory,
            IImageDataFactory imageDataFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector,
            IApplicationDispatcher applicationDispatcher,
            IAlglibAPI alglibAPI,
            ITiltAdapterOptions tiltAdapterOptions = null,
            TiltDeviceConnectionService tiltDeviceConnectionService = null,
            Func<string, string, Task<bool>> confirmPromptAsync = null,
            Func<Func<bool, bool, TiltDevicePlanPreview>, bool, string, bool, Task<TiltDeviceAdjustmentChoice>> showAdjustmentPromptAsync = null,
            Func<CancellationToken, Task<bool>> reRunAnalysisAsync = null) : base(profileService) {
            this.applicationStatusMediator = applicationStatusMediator;
            this.imagingMediator = imagingMediator;
            this.cameraMediator = cameraMediator;
            this.focuserMediator = focuserMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.starDetectionOptions = starDetectionOptions;
            this.starAnnotatorOptions = starAnnotatorOptions;
            this.telescopeMediator = telescopeMediator;
            this.inspectorOptions = inspectorOptions;
            this.autoFocusOptions = autoFocusOptions;
            this.autoFocusEngineFactory = autoFocusEngineFactory;
            this.imageDataFactory = imageDataFactory;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
            this.applicationDispatcher = applicationDispatcher;
            this.progress = ProgressFactory.Create(applicationStatusMediator, "Aberration Inspector");

            this.cameraMediator.RegisterConsumer(this);
            this.focuserMediator.RegisterConsumer(this);
            this.telescopeMediator.RegisterConsumer(this);

            this.Title = "Aberration Inspector";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);

            RegionFocusPoints = Enumerable.Range(0, 6).Select(i => new AsyncObservableCollection<ScatterErrorPoint>()).ToArray();
            RegionPlotFocusPoints = Enumerable.Range(0, 6).Select(i => new AsyncObservableCollection<DataPoint>()).ToArray();
            RegionFinalFocusPoints = new AsyncObservableCollection<DataPoint>(Enumerable.Range(0, 6).Select(i => new DataPoint(-1.0d, 0.0d)));
            RegionCurveFittings = new AsyncObservableCollection<Func<double, double>>(Enumerable.Range(0, 6).Select(i => (Func<double, double>)null));
            RegionLineFittings = new AsyncObservableCollection<TrendlineFitting>(Enumerable.Range(0, 6).Select(i => (TrendlineFitting)null));
            TiltModel = new TiltModel(inspectorOptions);
            SensorModel = new SensorModel(profileService, inspectorOptions, autoFocusOptions, alglibAPI);

            inspectorOptions.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(IInspectorOptions.SignalAmplification) ||
                    e.PropertyName == nameof(IInspectorOptions.StepCount) ||
                    e.PropertyName == nameof(IInspectorOptions.FramesPerPoint)) {
                    RaisePropertyChanged(nameof(SignalAmplificationSummary));
                }
            };
            // SignalAmplificationSummary also reads the active profile's FocuserSettings (offset steps /
            // frames per point), which the user can edit in place without swapping profiles —
            // ProfileChanged alone would leave the summary stale. Track the active profile's
            // FocuserSettings and re-hook on every profile change (unsubscribe old, subscribe new).
            focuserSettingsHandler = (s, e) => {
                if (e.PropertyName == nameof(IFocuserSettings.AutoFocusInitialOffsetSteps) ||
                    e.PropertyName == nameof(IFocuserSettings.AutoFocusNumberOfFramesPerPoint)) {
                    RaisePropertyChanged(nameof(SignalAmplificationSummary));
                }
            };
            HookActiveProfileFocuserSettings();
            profileService.ProfileChanged += (s, e) => {
                HookActiveProfileFocuserSettings();
                RaisePropertyChanged(nameof(SignalAmplificationSummary));
            };

            this.tiltAdapterOptions = tiltAdapterOptions;
            this.tiltDeviceConnectionService = tiltDeviceConnectionService;
            this.confirmPromptAsync = confirmPromptAsync ?? ShowYesNoPromptAsync;
            this.showAdjustmentPromptAsync = showAdjustmentPromptAsync ?? DefaultShowAdjustmentPromptAsync;
            this.reRunAnalysisAsync = reRunAnalysisAsync ?? (ct => AnalyzeAutoFocus(ct, captureCameraBlock: true));
            TiltGuidance = new TiltAdapterGuidanceVM();
            if (tiltAdapterOptions != null) {
                tiltAdapterOptions.PropertyChanged += (s, e) => RebuildTiltGuidance();
                RebuildTiltGuidance();
            }
            if (this.tiltDeviceConnectionService != null) {
                // This VM is a Shared MEF singleton, so this ctor-time subscription intentionally lives for
                // the whole app run (mirrors TiltAdapterWizardVM's identical subscription).
                this.tiltDeviceConnectionService.PropertyChanged += TiltDeviceConnectionService_PropertyChanged;
            }

            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["InspectorSVG"];
            ImageGeometry.Freeze();

            RunAutoFocusAnalysisCommand = new AsyncRelayCommand(() => AnalyzeAutoFocusImpl(true), canExecute: () => !AnalysisRunning() && CameraInfo.Connected && FocuserInfo.Connected);
            RunExposureAnalysisCommand = new AsyncRelayCommand(AnalyzeExposure, canExecute: () => !AnalysisRunning() && CameraInfo.Connected);
            RerunSavedAutoFocusAnalysisCommand = new AsyncRelayCommand(AnalyzeSavedAutoFocusRun, canExecute: () => !AnalysisRunning());
            ClearAnalysesCommand = new RelayCommand(ClearAnalyses, canExecute: () => !AnalysisRunning());
            CancelAnalyzeCommand = new RelayCommand(CancelAnalyze);
            SlewToZenithEastCommand = new AsyncRelayCommand(() => SlewToZenith(false), canExecute: () => TelescopeInfo.Connected && (slewToZenithTask == null || slewToZenithTask?.Status >= TaskStatus.RanToCompletion));
            SlewToZenithWestCommand = new AsyncRelayCommand(() => SlewToZenith(true), canExecute: () => TelescopeInfo.Connected && (slewToZenithTask == null || slewToZenithTask?.Status >= TaskStatus.RanToCompletion));
            CancelSlewToZenithCommand = new RelayCommand(() => slewToZenithCts?.Cancel());
            ReviewFramesCommand = new RelayCommand(ShowFrameReview, canExecute: () => ReviewFramesAvailable);
            AutomaticAdjustmentCommand = new AsyncRelayCommand(RunAutomaticAdjustmentAsync, CanExecuteAutomaticAdjustmentNow);
        }

        private bool AnalysisRunning() {
            var localAnalyzeTask = analyzeTask;
            if (localAnalyzeTask == null) {
                return false;
            }

            return localAnalyzeTask.Status < TaskStatus.RanToCompletion;
        }

        public override bool IsTool { get; } = true;

        private CancellationTokenSource analyzeCts;
        private Task<bool> analyzeTask;

        // Folder of the most recent AutoFocus run (the engine's timestamped attempt root, == result.SaveFolder).
        // Set after a successful run that saved frames; null/empty when the last run did not save. The Tilt
        // Adapter Wizard reads this to record each calibration step's saved location for later replay.
        public string LastSaveFolder { get; private set; }

        public async Task<bool> AnalyzeAutoFocus(CancellationToken token, bool captureCameraBlock = false, AutoFocusSaveOverride saveOverride = null) {
            var task = AnalyzeAutoFocusImpl(captureCameraBlock, saveOverride);
            token.Register(() => analyzeCts?.Cancel());
            return await task;
        }

        /// <summary>
        /// Re-analyzes a saved AutoFocus attempt from an explicit folder path (no folder-picker dialog), for
        /// replaying a saved calibration step. The folder should be the engine's attempt root (the level that
        /// contains a single <c>attempt*</c> subfolder), i.e. <see cref="LastSaveFolder"/> from the original run.
        /// </summary>
        public async Task<bool> AnalyzeAutoFocusFromSavedPath(string folderPath, CancellationToken token, IStarDetectionOptions starDetectionOptionsOverride = null) {
            if (string.IsNullOrEmpty(folderPath)) {
                return false;
            }
            var task = AnalyzeAutoFocusFromSavedImpl(folderPath, starDetectionOptionsOverride: starDetectionOptionsOverride);
            token.Register(() => analyzeCts?.Cancel());
            return await task;
        }

        public async Task<bool> AnalyzeAutoFocusFromSaved(CancellationToken token, Action onFolderSelected = null, AutoFocusSaveOverride saveOverride = null) {
            string folderPath;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                if (!String.IsNullOrEmpty(autoFocusOptions.LastSelectedLoadPath)) {
                    dialog.SelectedPath = autoFocusOptions.LastSelectedLoadPath;
                }
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
                    return false;
                }
                folderPath = dialog.SelectedPath;
                autoFocusOptions.LastSelectedLoadPath = folderPath;
            }
            onFolderSelected?.Invoke();
            var task = AnalyzeAutoFocusFromSavedImpl(folderPath, saveOverride);
            token.Register(() => analyzeCts?.Cancel());
            return await task;
        }

        private async Task<bool> AnalyzeAutoFocusFromSavedImpl(string folderPath, AutoFocusSaveOverride saveOverride = null, IStarDetectionOptions starDetectionOptionsOverride = null) {
            var localAnalyzeTask = analyzeTask;
            if (localAnalyzeTask != null && !localAnalyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }

            analyzeCts?.Cancel();
            var localAnalyzeCts = new CancellationTokenSource();
            analyzeCts = localAnalyzeCts;

            var autoFocusEngine = autoFocusEngineFactory.Create();
            SavedAutoFocusAttempt savedAttempt;
            try {
                savedAttempt = autoFocusEngine.LoadSavedAutoFocusAttempt(folderPath);
                folderPath = savedAttempt.FolderPath;
            } catch (Exception e) {
                Notification.ShowError(e.Message);
                Logger.Error($"Failed to load saved auto focus attempt from {folderPath}: {e.Message}");
                return false;
            }

            Logger.Info($"Rerunning auto focus attempt from {folderPath}");
            bool suppressAuxiliaryFiles = saveOverride?.SuppressAuxiliaryFiles == true;
            localAnalyzeTask = Task.Run(async () => {
                // A rerun re-analyzes existing frames and never captures, so the engine cannot write raw frames to a
                // new location. Leave the engine save OFF (no annotated/JSON artifacts) and, on success, copy the
                // source raw frames into the requested per-step folder so the calibration run stays replayable.
                var options = GetAutoFocusEngineOptions(autoFocusEngine, savedAttempt);
                // Headless (Tilt Adapter Wizard) replay: when a capture-time detection snapshot is supplied, replay
                // uses it without mutating the profile; null = current settings. Never prompts from this path.
                options.StarDetectionOptionsOverride = starDetectionOptionsOverride;
                // This path re-analyzes existing frames and manages its own replayable copy via CopySavedFramesForReplay;
                // keep the engine save OFF so it neither writes auxiliary artifacts nor a replay metadata.json into the
                // global save path during tilt calibration replay.
                options.Save = false;
                var sensorCurveModelEnabled = ResolveSensorCurveModelEnabled();
                var regions = GetStarDetectionRegions(options, sensorCurveModelEnabled: sensorCurveModelEnabled);

                autoFocusEngine.Started += AutoFocusEngine_Started;
                autoFocusEngine.Failed += AutoFocusEngine_Failed;
                autoFocusEngine.Completed += AutoFocusEngine_CompletedNoReport;
                autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;
                autoFocusEngine.SubMeasurementPointCompleted += AutoFocusEngine_SubMeasurementPointCompleted;

                var imagingFilter = GetImagingFilter();
                ActivateAutoFocusChart();
                ResetErrors();
                ResetExposureAnalysis();

                LastSaveFolder = null;
                var result = await autoFocusEngine.RerunWithRegions(options, savedAttempt, imagingFilter, regions, localAnalyzeCts.Token, this.progress);
                if (result == null) {
                    InspectorErrorText = "AutoFocus Analysis Failed";
                    DeactivateAutoFocusAnalysis();
                    return false;
                }

                var analysisResult = await AnalyzeAutoFocusResult(options, result, sensorCurveModelEnabled: sensorCurveModelEnabled, ct: localAnalyzeCts.Token, forRerun: true, suppressRegisteredImages: suppressAuxiliaryFiles);
                if (!analysisResult) {
                    Notification.ShowError("AutoFocus Analysis Failed");
                    InspectorErrorText = "AutoFocus Analysis Failed";
                    DeactivateAutoFocusAnalysis();
                    return false;
                }
                if (saveOverride?.Save == true && !string.IsNullOrEmpty(saveOverride.SavePath)) {
                    LastSaveFolder = CopySavedFramesForReplay(savedAttempt, saveOverride.SavePath);
                }
                ActivateTiltMeasurement();
                return true;
            });
            analyzeTask = localAnalyzeTask;
            OnAnalysisRunningChanged();

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection auto focus rerun analysis cancelled");
                InspectorErrorText = "Inspection AutoFocus Rerun analysis cancelled";
                DeactivateAutoFocusAnalysis();
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Inspection auto focus rerun analysis failed: {e.Message}");
                InspectorErrorText = $"Inspection AutoFocus Rerun analysis failed\n{e.Message}";
                Logger.Error("Inspection auto focus rerun analysis failed", e);
                DeactivateAutoFocusAnalysis();
                return false;
            } finally {
                OnAnalysisRunningChanged();
            }
        }

        // Copies a saved attempt's raw exposure frames into a fresh AutoFocus_<ts>/attempt01 folder under
        // <savePathRoot>, returning that AutoFocus_<ts> folder (the level a replay points at). Used by the Tilt
        // Adapter Wizard so re-analyzing a saved run still produces a self-contained, replayable per-step folder.
        // Best-effort: the tilt measurement has already succeeded by the time this runs, so any copy failure is
        // logged and yields a null result (this step simply won't be replayable) rather than failing the step.
        private static string CopySavedFramesForReplay(SavedAutoFocusAttempt savedAttempt, string savePathRoot) {
            try {
                var runFolder = Path.Combine(savePathRoot, $"AutoFocus_{DateTime.Now:yyyyMMdd_HHmmss}");
                var attemptFolder = Path.Combine(runFolder, "attempt01");
                Directory.CreateDirectory(attemptFolder);
                int copied = 0;
                foreach (var img in savedAttempt.SavedImages) {
                    if (string.IsNullOrEmpty(img.Path) || !File.Exists(img.Path)) {
                        continue;
                    }
                    var dest = Path.Combine(attemptFolder, Path.GetFileName(img.Path));
                    File.Copy(img.Path, dest, overwrite: true);
                    copied++;
                }
                if (copied == 0) {
                    Logger.Warning($"No source frames could be copied to {runFolder}; this calibration step will not be replayable.");
                    return null;
                }
                return runFolder;
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to copy saved frames for replay; this calibration step will not be replayable");
                return null;
            }
        }

        private async Task<bool> AnalyzeAutoFocusImpl(bool captureCameraBlock, AutoFocusSaveOverride saveOverride = null) {
            var localAnalyzeTask = analyzeTask;
            if (localAnalyzeTask != null && !localAnalyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }

            analyzeCts?.Cancel();
            var localAnalyzeCts = new CancellationTokenSource();
            analyzeCts = localAnalyzeCts;

            bool suppressAuxiliaryFiles = saveOverride?.SuppressAuxiliaryFiles == true;
            localAnalyzeTask = Task.Run(async () => {
                try {
                    if (captureCameraBlock) {
                        this.cameraMediator.RegisterCaptureBlock(this);
                    }

                    var autoFocusEngine = autoFocusEngineFactory.Create();
                    var options = GetAutoFocusEngineOptions(autoFocusEngine);
                    if (saveOverride != null) {
                        options.Save = saveOverride.Save;
                        options.SavePath = saveOverride.SavePath;
                        if (options.Save) {
                            // Mirror GetAutoFocusEngineOptions: keep exposures so the run can be re-analyzed later.
                            options.PreserveExposures = true;
                            // A saved calibration run keeps only the raw frames — no per-region annotated TIFFs /
                            // detection-result JSONs (and, below, no registered/alignment images).
                            options.SaveExposuresOnly = suppressAuxiliaryFiles;
                        }
                    }
                    var sensorCurveModelEnabled = ResolveSensorCurveModelEnabled();
                    var regions = GetStarDetectionRegions(options, sensorCurveModelEnabled: sensorCurveModelEnabled);
                    var imagingFilter = GetImagingFilter();

                    autoFocusEngine.Started += AutoFocusEngine_Started;
                    autoFocusEngine.IterationFailed += AutoFocusEngine_IterationFailed;
                    autoFocusEngine.Failed += AutoFocusEngine_Failed;
                    autoFocusEngine.Completed += AutoFocusEngine_Completed;
                    autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;
                    autoFocusEngine.SubMeasurementPointCompleted += AutoFocusEngine_SubMeasurementPointCompleted;

                    ActivateAutoFocusChart();
                    ResetErrors();
                    ResetExposureAnalysis();
                    LastSaveFolder = null;

                    // Pre-center the focuser at best focus before the detailed multi-region sweep, so the sweep
                    // brackets focus symmetrically. This reduces extreme one-sided defocus frames that fail RANSAC
                    // alignment and improves the per-star paraboloid fit the tilt is read from. A plain (single-region)
                    // AutoFocus is run on a SEPARATE engine — no inspector chart wiring. It uses GetOptions() (profile
                    // step size/count), so it is deliberately NOT signal-amplified: the finer steps are only needed for
                    // the sensor-model run, a quick coarse centering pass is sufficient. The centering run is NEVER
                    // saved (Save forced off, independent of the global AutoFocus save toggle and of any saveOverride):
                    // only the actual sensor-model / per-tilt-step run that follows is persisted. Failure is non-fatal:
                    // the detailed run proceeds from the current focuser position.
                    if (inspectorOptions.CenterFocuserBeforeRun) {
                        try {
                            var centeringEngine = autoFocusEngineFactory.Create();
                            var centeringOptions = centeringEngine.GetOptions();
                            centeringOptions.Save = false;
                            centeringOptions.PreserveExposures = false;
                            this.progress.Report(new ApplicationStatus() { Status = "Centering focuser before sensor model run" });
                            var centeringResult = await centeringEngine.Run(centeringOptions, imagingFilter, localAnalyzeCts.Token, this.progress);
                            if (centeringResult == null || !centeringResult.Succeeded) {
                                Logger.Warning("Centering AutoFocus did not succeed; continuing the sensor-model run from the current focuser position.");
                            }
                        } catch (OperationCanceledException) {
                            throw;
                        } catch (Exception ex) {
                            Logger.Warning($"Centering AutoFocus failed: {ex.Message}; continuing the sensor-model run from the current focuser position.");
                        }
                    }

                    var result = await autoFocusEngine.RunWithRegions(options, imagingFilter, regions, localAnalyzeCts.Token, this.progress);
                    if (result == null) {
                        InspectorErrorText = "AutoFocus Analysis Failed";
                        DeactivateAutoFocusAnalysis();
                        return false;
                    }
                    LastSaveFolder = result.SaveFolder;

                    var autoFocusAnalysisResult = await AnalyzeAutoFocusResult(options, result, sensorCurveModelEnabled: sensorCurveModelEnabled, ct: localAnalyzeCts.Token, suppressRegisteredImages: suppressAuxiliaryFiles);
                    if (!autoFocusAnalysisResult) {
                        InspectorErrorText = "AutoFocus Analysis Failed. View saved AF report in the AutoFocus tab.";
                        Notification.ShowError("AutoFocus Analysis Failed. View saved AF report in the AutoFocus tab.");
                        DeactivateAutoFocusAnalysis();
                        return false;
                    }
                    ActivateTiltMeasurement();
                    var exposureAnalysisResult = await TakeAndAnalyzeExposureImpl(autoFocusEngine, analyzeCts.Token);
                    if (!exposureAnalysisResult) {
                        InspectorErrorText = "Exposure Analysis Failed. View saved AF report in the AutoFocus tab.";
                        Notification.ShowError("Exposure Analysis Failed");
                        DeactivateAutoFocusAnalysis();
                        return false;
                    }
                    ActivateExposureAnalysis();
                    Notification.ShowInformation("Aberration Inspection Complete");
                    return true;
                } finally {
                    if (captureCameraBlock) {
                        this.cameraMediator.ReleaseCaptureBlock(this);
                    }
                }
            }, localAnalyzeCts.Token);
            analyzeTask = localAnalyzeTask;
            OnAnalysisRunningChanged();

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection analysis cancelled");
                DeactivateAutoFocusAnalysis();
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Inspection analysis failed: {e.Message}");
                Logger.Error("Inspection analysis failed", e);
                DeactivateAutoFocusAnalysis();
                return false;
            } finally {
                analyzeTask = null;
                analyzeCts = null;
                OnAnalysisRunningChanged();
            }
        }

        private FilterInfo GetImagingFilter() {
            var filterInfo = filterWheelMediator.GetInfo();
            FilterInfo imagingFilter = null;
            if (filterInfo?.SelectedFilter != null) {
                imagingFilter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Where(x => x.Position == filterInfo.SelectedFilter.Position).FirstOrDefault();
            }
            return imagingFilter;
        }

        private bool focuserStepSizeWarningShowed = false;

        // When set (by the Tilt Adapter Wizard around its own calibration analyses), the per-star sensor-curve model
        // (paraboloid) is generated even if the SensorCurveModelEnabled display option is off, so calibration can read
        // its robust tilt. Transient and not persisted; the wizard sets it only for the duration of each analysis call.
        public bool ForceSensorCurveModelGeneration { get; set; }

        // Transient replay overrides for the sensor-model inputs otherwise read live (null = use profile/inspector).
        public double? SensorModelFocuserSizeOverrideMicrons { get; set; }
        public double? SensorModelFRatioOverride { get; set; }

        private bool ResolveSensorCurveModelEnabled() =>
            inspectorOptions.SensorCurveModelEnabled || ForceSensorCurveModelGeneration;

        private async Task<bool> AnalyzeAutoFocusResult(
            AutoFocusEngineOptions options,
            AutoFocusResult result,
            bool sensorCurveModelEnabled,
            CancellationToken ct,
            bool forRerun = false,
            bool suppressRegisteredImages = false) {
            if (result == null || !result.Succeeded) {
                Logger.Error("Inspection analysis failed, due to failed AutoFocus");
                return false;
            }

            var invalidRegionCount = result.RegionResults.Count(r => double.IsNaN(r.EstimatedFinalFocuserPosition));
            if (invalidRegionCount > 0) {
                Notification.ShowWarning($"{invalidRegionCount} regions failed to produce a focus curve");
                Logger.Warning($"{invalidRegionCount} regions failed to produce a focus curve");
            }

            // Resolve the definitive review request from the SAME flag that gates this block (capture-time flag on replay).
            frameReviewRequestedForRun = IsFrameReviewRequested(inspectorOptions.FrameReviewEnabled, sensorCurveModelEnabled);
            if (sensorCurveModelEnabled) {
                double focuserSizeMicrons = SensorModelFocuserSizeOverrideMicrons ?? InspectorOptions.MicronsPerFocuserStep;
                if (double.IsNaN(focuserSizeMicrons) || focuserSizeMicrons <= 0.0) {
                    if (!focuserStepSizeWarningShowed) {
                        Notification.ShowWarning("Focuser Step Size not set. Assuming 1 micron per focuser step. This message won't be shown again.");
                        focuserStepSizeWarningShowed = true;
                    }
                    Logger.Warning("Focuser Step Size not set. Assuming 1 micron per focuser step.");
                    focuserSizeMicrons = 1.0d;
                }

                var finalFocuserPosition = result.RegionResults[0].EstimatedFinalFocuserPosition;
                try {
                    await SensorModel.UpdateModel(
                        FullSensorDetectedStars,
                        fRatio: SensorModelFRatioOverride ?? profileService.ActiveProfile.TelescopeSettings.FocalRatio,
                        focuserSizeMicrons: focuserSizeMicrons,
                        finalFocusPosition: finalFocuserPosition,
                        stepSize: result.StepSize,
                        progress,
                        ct: ct);

                    if (!suppressRegisteredImages && ((!forRerun) || (inspectorOptions.SaveImagesOnReruns))) {
                        if (!String.IsNullOrEmpty(result.SaveFolder)) {
                            await SaveRegisteredImages(result.SaveFolder,
                                SensorModel.SensorModelResult.RegisteredStars,
                                SensorModel.TrianglesByImage,
                                SensorModel.ReferenceImage,
                                inspectorOptions.SaveAlignmentImages);
                        }
                    }
                } finally {
                    // Build the Review Frames snapshot even if UpdateModel threw (failed/poor fit): the per-frame
                    // detections + bitmaps are exactly what the user needs to "see why the fit looks wrong". The
                    // builder handles a null/partial registration result gracefully. Don't let snapshot-build errors
                    // mask the original UpdateModel exception. Skip on cancellation.
                    if (frameReviewRequestedForRun && !ct.IsCancellationRequested) {
                        try {
                            List<SensorDetectedStars> framesForReview;
                            lock (fullSensorDetectedStarsLock) {
                                framesForReview = FullSensorDetectedStars.ToList();
                            }
                            reviewSnapshot = FrameReviewSnapshotBuilder.Build(
                                framesForReview,
                                SensorModel.SensorModelResult?.RegisteredStars,
                                SensorModel.ReferenceImage,
                                inspectorOptions.UseRANSAC);
                            NotifyReviewFramesAvailabilityChanged();
                        } catch (Exception snapEx) {
                            Logger.Warning($"Failed to build Review Frames snapshot after model fit: {snapEx.Message}");
                        }
                    }
                }
            }

            UpdateBackfocusMeasurements(result);
            TiltModel.UpdateTiltModel(result, fRatio: profileService.ActiveProfile.TelescopeSettings.FocalRatio, backfocusFocuserPositionDelta: BackfocusFocuserPositionDelta);
            RebuildTiltGuidance();
            // One-adjustment-per-measurement (design doc user decision #9): every COMPLETED analysis that
            // reaches this point (wizard calibration steps included) is a fresh measurement. Automatic
            // Adjustment's canExecute requires this counter to be strictly newer than the generation stamped
            // on the last EXECUTED plan (see AutomaticAdjustmentCommand's canExecute) — so the button stays
            // disabled after an adjustment until a brand-new analysis completes, and the same measurement can
            // never drive two plans. Deliberately NOT incremented from RebuildTiltGuidance's other call sites
            // (ctor, tiltAdapterOptions.PropertyChanged, ClearAnalyses) — only a genuinely completed analysis
            // counts as a new measurement.
            measurementGeneration++;
            // Marshal the requery to the UI thread (NotifyCanExecuteChanged raises through
            // CanExecuteChangedEventManager, which requires it). Non-blocking Post, for the same
            // deadlock-avoidance reason as RebuildTiltGuidance's publish above.
            applicationDispatcher.PostSynchronizationContext(() => AutomaticAdjustmentCommand?.NotifyCanExecuteChanged());
            AutoFocusCompleted = true;
            return true;
        }

        private async Task SaveRegisteredImages(
            String saveFolder,
            SensorModel.RegisteredStar[] registeredStars,
            Dictionary<int, List<RANSACRegistration.StarTriangle>> trianglesByImage,
            int referenceImage,
            bool saveAlignmentImages) {
            if (string.IsNullOrWhiteSpace(saveFolder)) {
                Logger.Error("SavePath empty. Not saving registered images");
                return;
            }
            if (!Directory.Exists(saveFolder)) {
                Logger.Error($"SavePath {saveFolder} does not exist. Not saving registered images");
                return;
            }

            Logger.Info($"Saving registered images to {saveFolder}");
            var colors = new System.Windows.Media.Color[] { Colors.Blue, Colors.Red, Colors.Purple, Colors.Green, Colors.Yellow, Colors.Orange };
            Dictionary<int, int> outputIndexMap = FullSensorDetectedStars
                .Select((stars, sourceIdx) => (stars, sourceIdx))
                .OrderBy(x => x.stars.FocuserPosition)
                .Select((kv, idx) => (kv.sourceIdx, idx))
                .ToDictionary();

            await Task.WhenAll(Enumerable.Range(0, FullSensorDetectedStars.Count).Select(imageIndex => Task.Run(() => {
                var detectedStars = FullSensorDetectedStars[imageIndex];
                var imageToAnnotate = detectedStars.Image.Image;
                if (imageToAnnotate.Format == PixelFormats.Rgb48) {
                    using (var source = ImageUtility.BitmapFromSource(imageToAnnotate, System.Drawing.Imaging.PixelFormat.Format48bppRgb)) {
                        using (var img = new Grayscale(0.2125, 0.7154, 0.0721).Apply(source)) {
                            imageToAnnotate = ImageUtility.ConvertBitmap(img, PixelFormats.Gray16);
                            imageToAnnotate.Freeze();
                        }
                    }
                }

                var brushes = colors.Select(c => new SolidBrush(c.ToDrawingColor())).ToArray();
                var pens = brushes.Select(b => new System.Drawing.Pen(b)).ToArray();
                var annotationFont = new Font(
                    starAnnotatorOptions.AnnotationFontFamily.ToDrawingFontFamily(),
                    starAnnotatorOptions.AnnotationFontSizePoints,
                    System.Drawing.FontStyle.Regular,
                    GraphicsUnit.Point);

                var infoBrush = new SolidBrush(Colors.White.ToDrawingColor());
                var triPenUnmatched = new System.Drawing.Pen(Colors.DarkCyan.ToDrawingColor());
                var triPenMatched = new System.Drawing.Pen(Colors.Yellow.ToDrawingColor());
                var triPenReferenceColor = Colors.White.ToDrawingColor();
                var triPenReferenceBrush = new SolidBrush(triPenReferenceColor);
                var triPenReference = new System.Drawing.Pen(triPenReferenceBrush);
                try {
                    using (var bmp = ImageUtility.Convert16BppTo8Bpp(imageToAnnotate)) {
                        if (saveAlignmentImages) {
                            SaveAlignmentImage(saveFolder, referenceImage, imageIndex, outputIndexMap, detectedStars, bmp);
                        }
                        SaveRegisteredImage(saveFolder, registeredStars, trianglesByImage, referenceImage, imageIndex, outputIndexMap, detectedStars, brushes, pens, annotationFont, infoBrush, triPenMatched, triPenReferenceBrush, triPenReference, bmp);
                    }
                } finally {
                    foreach (var p in pens) {
                        p.Dispose();
                    }
                    foreach (var b in brushes) {
                        b.Dispose();
                    }
                    annotationFont.Dispose();
                }
            })));
        }

        private static void SaveRegisteredImage(
            string saveFolder,
            RegisteredStar[] registeredStars,
            Dictionary<int, List<RANSACRegistration.StarTriangle>> trianglesByImage,
            int referenceImage,
            int imageIndex,
            Dictionary<int, int> outputIndexMap,
            SensorDetectedStars detectedStars,
            SolidBrush[] brushes,
            System.Drawing.Pen[] pens,
            Font annotationFont,
            SolidBrush infoBrush,
            System.Drawing.Pen triPenMatched,
            SolidBrush triPenReferenceBrush,
            System.Drawing.Pen triPenReference,
            Bitmap bmp) {
            using (var newBitmap = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb)) {
                Graphics graphics = Graphics.FromImage(newBitmap);
                graphics.DrawImage(bmp, 0, 0);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                // indicate RANSAC output by showing the transformed points
                foreach (var star in detectedStars.StarDetectionResult.StarList) {
                    float starX = star.Position.X, starY = star.Position.Y;
                    var xLength = 20;
                    var yLength = 20;

                    graphics.DrawLine(triPenReference, starX - xLength, starY - yLength, starX + xLength, starY + yLength);
                    graphics.DrawLine(triPenReference, starX + xLength, starY - yLength, starX - xLength, starY + yLength);
                }

                for (int starIdx = 0; starIdx < registeredStars.Length; starIdx++) {
                    var star = registeredStars[starIdx];
                    var starCenterPen = pens[starIdx % pens.Length];
                    var annotationBrush = brushes[starIdx % pens.Length];
                    bool done = false;
                    foreach (var matchedStar in star.MatchedStars) {
                        if (matchedStar.ImageIndex == imageIndex) {
                            var boundingBox = matchedStar.Star.BoundingBox;
                            float starX = matchedStar.Star.Position.X, starY = matchedStar.Star.Position.Y;
                            var xLength = Math.Max(1.0f, Math.Min(starX - boundingBox.Left, boundingBox.Right - starX)) / 2.0f;
                            var yLength = Math.Max(1.0f, Math.Min(starY - boundingBox.Top, boundingBox.Bottom - starY)) / 2.0f;

                            graphics.DrawLine(starCenterPen, starX - xLength, starY, starX + xLength, starY);
                            graphics.DrawLine(starCenterPen, starX, starY - yLength, starX, starY + yLength);
                            graphics.DrawString(starIdx.ToString(), annotationFont, annotationBrush, new PointF(matchedStar.Star.Position.X, matchedStar.Star.Position.Y - yLength));

                            if (matchedStar.Star.OriginalPosition.X > 0 || matchedStar.Star.OriginalPosition.Y > 0) {
                                if (referenceImage != matchedStar.ImageIndex) {
                                    // image has been aligned with reference - draw line
                                    Rectangle rectOriginal = new Rectangle(
                                        (int)(matchedStar.Star.OriginalPosition.X - matchedStar.Star.BoundingBox.Width / 2),
                                        (int)(matchedStar.Star.OriginalPosition.Y - matchedStar.Star.BoundingBox.Height / 2),
                                        matchedStar.Star.BoundingBox.Width,
                                        matchedStar.Star.BoundingBox.Height);

                                    graphics.DrawEllipse(starCenterPen, rectOriginal);
                                    graphics.DrawLine(starCenterPen, starX, starY, matchedStar.Star.OriginalPosition.X, matchedStar.Star.OriginalPosition.Y);
                                }
                            }
                            break;
                        }
                    }
                    if (done) break;
                }
                if (!detectedStars.HasBeenAligned) {
                    graphics.DrawString($"Image: {imageIndex}, Not aligned", annotationFont, infoBrush, new PointF(0, 0));
                } else {
                    if (referenceImage == imageIndex) {
                        graphics.DrawString($"Image: {imageIndex}, Reference image", annotationFont, infoBrush, new PointF(0, 0));
                    } else {
                        graphics.DrawString($"Image: {imageIndex}, Ref: {referenceImage}", annotationFont, infoBrush, new PointF(0, 0));
                        graphics.DrawString($"Transform: {detectedStars.AlignmentTransform.ToFullString()}", new Font("Arial", 12), new SolidBrush(DrawingColor.White), new PointF(0, 20));
                    }
                }

                if ((trianglesByImage != null) && (trianglesByImage.Count > imageIndex)) {
                    foreach (var tri in trianglesByImage[imageIndex].Where(t => !t.IsReference && t.Matched)) {
                        graphics.DrawLine(triPenMatched, tri.P1.AsPointF(), tri.P2.AsPointF());
                        graphics.DrawLine(triPenMatched, tri.P2.AsPointF(), tri.P3.AsPointF());
                        graphics.DrawLine(triPenMatched, tri.P3.AsPointF(), tri.P1.AsPointF());
                        graphics.DrawString(tri.MatchString, annotationFont, triPenReferenceBrush, tri.P1.AsPointF());
                        graphics.DrawString(tri.MatchString, annotationFont, triPenReferenceBrush, tri.P2.AsPointF());
                        graphics.DrawString(tri.MatchString, annotationFont, triPenReferenceBrush, tri.P3.AsPointF());
                    }
                    foreach (var tri in trianglesByImage[imageIndex].Where(t => t.IsReference)) {
                        graphics.DrawLine(triPenReference, tri.P1.AsPointF(), tri.P2.AsPointF());
                        graphics.DrawLine(triPenReference, tri.P2.AsPointF(), tri.P3.AsPointF());
                        graphics.DrawLine(triPenReference, tri.P3.AsPointF(), tri.P1.AsPointF());
                        graphics.DrawString(tri.MatchString, annotationFont, triPenReferenceBrush, tri.P1.AsPointF());
                        graphics.DrawString(tri.MatchString, annotationFont, triPenReferenceBrush, tri.P2.AsPointF());
                        graphics.DrawString(tri.MatchString, annotationFont, triPenReferenceBrush, tri.P3.AsPointF());
                    }
                }

                var img = ImageUtility.ConvertBitmap(newBitmap, PixelFormats.Bgr24);
                img.Freeze();

                var suffix = imageIndex == referenceImage ? "_ref" : "";
                var filename = $"Registered_Index{outputIndexMap[imageIndex]:00}_Focuser{detectedStars.FocuserPosition}{suffix}.tiff";
                var targetPath = Path.Combine(saveFolder, filename);
                using (var fileStream = new FileStream(targetPath, FileMode.Create)) {
                    BitmapEncoder encoder = new TiffBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(img));
                    encoder.Save(fileStream);
                }
                Logger.Info($"Image {imageIndex}: Saved registered image {filename}");
            }
        }

        private static void SaveAlignmentImage(string saveFolder, int referenceImage, int imageIndex, Dictionary<int, int> outputIndexMap, SensorDetectedStars detectedStars, Bitmap bmp) {
            using (var newBitmap = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb)) {
                Graphics graphics = Graphics.FromImage(newBitmap);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                // indicate RANSAC output by showing the transformed points
                foreach (var star in detectedStars.StarDetectionResult.StarList) {
                    float starX = star.Position.X, starY = star.Position.Y;

                    Point2D ptCenter = new Point2D(star.Position);

                    graphics.FillEllipse(new SolidBrush(DrawingColor.FromArgb((int)(Math.Min(64 + (star.AverageBrightness * 50 * 191), 255)), DrawingColor.White)), star.BoundingBox);
                }

                graphics.DrawString($"Image: {imageIndex}, {(detectedStars.HasBeenAligned ? $"Aligned to Ref: {referenceImage}" : "Not aligned")}", new Font("Arial", 12), new SolidBrush(DrawingColor.White), new PointF(0, 0));
                if (detectedStars.HasBeenAligned) {
                    if (imageIndex == referenceImage) {
                        graphics.DrawString($"Reference image", new Font("Arial", 12), new SolidBrush(DrawingColor.White), new PointF(0, 20));
                    } else {
                        graphics.DrawString($"Transform: {detectedStars.AlignmentTransform.ToFullString()}", new Font("Arial", 12), new SolidBrush(DrawingColor.White), new PointF(0, 20));
                    }
                }

                var img = ImageUtility.ConvertBitmap(newBitmap, PixelFormats.Bgr24);
                img.Freeze();

                var suffix = imageIndex == referenceImage ? "_ref" : "";
                var filename = $"StarAlignment_Index{outputIndexMap[imageIndex]:00}_Focuser{detectedStars.FocuserPosition}{suffix}.tiff";
                var targetPath = Path.Combine(saveFolder, filename);
                using (var fileStream = new FileStream(targetPath, FileMode.Create)) {
                    BitmapEncoder encoder = new TiffBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(img));
                    encoder.Save(fileStream);
                }
            }
        }

        private Task<bool> TakeAndAnalyzeExposure(IAutoFocusEngine autoFocusEngine, CancellationToken token) {
            var starDetection = starDetectionSelector.GetBehavior() as IHocusFocusStarDetection;
            if (starDetection == null) {
                Notification.ShowError("HocusFocus must be selected as the Star Detector. Change this option in Options -> Image Options");
                Logger.Error("HocusFocus must be selected as the Star Detector");
                return Task.FromResult(false);
            }

            DeactivateAutoFocusAnalysis();
            return TakeAndAnalyzeExposureImpl(autoFocusEngine, token);
        }

        private async Task<bool> LoadAndAnalyzeExposure(IAutoFocusEngine autoFocusEngine, AutoFocusEngineOptions autoFocusEngineOptions, SavedAutoFocusImage savedImage, CancellationToken token) {
            var starDetection = starDetectionSelector.GetBehavior() as IHocusFocusStarDetection;
            if (starDetection == null) {
                Notification.ShowError("HocusFocus must be selected as the Star Detector. Change this option in Options -> Image Options");
                Logger.Error("HocusFocus must be selected as the Star Detector");
                return false;
            }

            var imageData = await LoadSavedFile(autoFocusEngineOptions, savedImage, token);
            return await AnalyzeExposureImpl(autoFocusEngine, imageData, token);
        }

        private async Task<IRenderedImage> LoadSavedFile(
            AutoFocusEngineOptions autoFocusEngineOptions,
            SavedAutoFocusImage savedFile,
            CancellationToken token) {
            var isBayered = savedFile.IsBayered;
            var bitDepth = savedFile.BitDepth;

            var imageData = await this.imageDataFactory.CreateFromFile(savedFile.Path, bitDepth, isBayered, profileService.ActiveProfile.CameraSettings.RawConverter, token);
            var autoStretch = true;
            // If using contrast based statistics, no need to stretch
            if (autoFocusEngineOptions.AutoFocusMethod == AFMethodEnum.CONTRASTDETECTION && profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod == ContrastDetectionMethodEnum.Statistics) {
                autoStretch = false;
            }

            var prepareParameters = new PrepareImageParameters(autoStretch: autoStretch, detectStars: false);
            return await imagingMediator.PrepareImage(imageData, prepareParameters, token);
        }

        private async Task<bool> AnalyzeExposureImpl(IAutoFocusEngine autoFocusEngine, IRenderedImage imageData, CancellationToken token) {
            var autoFocusOptions = autoFocusEngine.GetOptions();
            var starDetection = (IHocusFocusStarDetection)starDetectionSelector.GetBehavior();
            var analysisParams = new StarDetectionParams() {
                Sensitivity = profileService.ActiveProfile.ImageSettings.StarSensitivity,
                NoiseReduction = profileService.ActiveProfile.ImageSettings.NoiseReduction,
                NumberOfAFStars = autoFocusOptions.NumberOfAFStars,
                IsAutoFocus = false
            };
            var hfParams = starDetection.ToHocusFocusParams(analysisParams);
            var starDetectorParams = starDetection.GetStarDetectorParams(imageData, StarDetectionRegion.Full, true);
            if (!starDetectorParams.ModelPSF) {
                starDetectorParams.ModelPSF = true;
            }

            var analysisResult = (HocusFocusStarDetectionResult)await starDetection.Detect(imageData, hfParams, starDetectorParams, this.progress, token);
            AnalyzeStarDetectionResult(analysisResult);
            this.SnapshotAnalysisStarDetectionResult = analysisResult;
            ActivateExposureAnalysis();
            return true;
        }

        private async Task<bool> TakeAndAnalyzeExposureImpl(IAutoFocusEngine autoFocusEngine, CancellationToken token) {
            var imagingFilter = GetImagingFilter();
            var starDetection = (IHocusFocusStarDetection)starDetectionSelector.GetBehavior();

            try {
                var autoFocusFilter = await autoFocusEngine.SetAutofocusFilter(imagingFilter, token, this.progress);
                double autoFocusExposureTime = profileService.ActiveProfile.FocuserSettings.AutoFocusExposureTime;
                if (imagingFilter != null && imagingFilter.AutoFocusExposureTime > -1) {
                    autoFocusExposureTime = imagingFilter.AutoFocusExposureTime;
                }

                var exposureTimeSeconds = inspectorOptions.SimpleExposureSeconds >= 0 ? inspectorOptions.SimpleExposureSeconds : autoFocusExposureTime;
                var captureSequence = new CaptureSequence(exposureTimeSeconds, CaptureSequence.ImageTypes.SNAPSHOT, autoFocusFilter, null, 1);
                var exposureData = await imagingMediator.CaptureImage(captureSequence, token, progress);
                var prepareParameters = new PrepareImageParameters(autoStretch: false, detectStars: false);
                var imageData = await imagingMediator.PrepareImage(exposureData, prepareParameters, token);
                return await AnalyzeExposureImpl(autoFocusEngine, imageData, token);
            } finally {
                var completionOperationTimeout = TimeSpan.FromMinutes(1);
                if (imagingFilter != null) {
                    try {
                        var completionTimeoutCts = new CancellationTokenSource(completionOperationTimeout);
                        await filterWheelMediator.ChangeFilter(imagingFilter, completionTimeoutCts.Token);
                    } catch (Exception e) {
                        Logger.Error("Failed to restore previous filter position after analysis", e);
                        Notification.ShowError($"Failed to restore previous filter position: {e.Message}");
                    }
                }
            }
        }

        private DrawingColor GetColorFromBrushResource(string resourceName, DrawingColor fallback) {
            return applicationDispatcher.DispatchSynchronizationContext(() => {
                var resource = Application.Current.TryFindResource(resourceName);
                if (resource is SolidColorBrush) {
                    return ((SolidColorBrush)resource).Color.ToDrawingColor();
                }
                return fallback;
            });
        }

        private void AnalyzeStarDetectionResult(HocusFocusStarDetectionResult result) {
            var numRegionsWide = inspectorOptions.NumRegionsWide;
            var imageSize = result.ImageSize;
            int regionSizePixels = imageSize.Width / numRegionsWide;
            int numRegionsTall = imageSize.Height / regionSizePixels;
            numRegionsTall += numRegionsTall % 2 == 0 ? 1 : 0;
            var regionDetectedStars = new List<HocusFocusDetectedStar>[numRegionsWide, numRegionsTall];
            for (int i = 0; i < numRegionsWide; ++i) {
                for (int j = 0; j < numRegionsTall; ++j) {
                    regionDetectedStars[i, j] = new List<HocusFocusDetectedStar>();
                }
            }

            foreach (var detectedStar in result.StarList.Cast<HocusFocusDetectedStar>()) {
                if (detectedStar.PSF == null) {
                    continue;
                }

                var regionRow = (int)Math.Floor((imageSize.Height - detectedStar.Position.Y - 1) / imageSize.Height * numRegionsTall);
                var regionCol = (int)Math.Floor(detectedStar.Position.X / imageSize.Width * numRegionsWide);
                regionDetectedStars[regionCol, regionRow].Add(detectedStar);
            }

            {
                double[] xs = DataGen.Range(0, numRegionsWide);
                double[] ys = DataGen.Range(0, numRegionsTall);
                var vectors = new SPVector2[numRegionsWide, numRegionsTall];
                var centerXs = new double[numRegionsWide * numRegionsTall];
                var centerYs = new double[numRegionsWide * numRegionsTall];
                var eccentricities = new double[numRegionsWide * numRegionsTall];
                var rotations = new double[numRegionsWide * numRegionsTall];

                double scalingFactor = 2.5;
                int pointIndex = 0;
                double maxMagnitude = 0.0d;
                for (int regionRow = 0; regionRow < numRegionsTall; ++regionRow) {
                    for (int regionCol = 0; regionCol < numRegionsWide; ++regionCol) {
                        var detectedStars = regionDetectedStars[regionCol, regionRow];

                        if (detectedStars.Count > 0) {
                            var (eccentricityMedian, _) = detectedStars.Select(s => s.PSF.Eccentricity).MedianMAD();
                            var sumEccentricity = detectedStars.Select(s => s.PSF.Eccentricity).Sum();
                            var psfRotationWeightedMean = detectedStars.Select(s => s.PSF.ThetaRadians * s.PSF.Eccentricity).Sum() / sumEccentricity;

                            var scaledEccentricity = eccentricityMedian * eccentricityMedian * scalingFactor;
                            double x = Math.Cos(psfRotationWeightedMean) * scaledEccentricity;
                            // Since y is inverted to render top-down, we must also invert the y part of the angle vector
                            double y = -Math.Sin(psfRotationWeightedMean) * scaledEccentricity;
                            vectors[regionCol, regionRow] = new Vector2(x, y);
                            eccentricities[pointIndex] = eccentricityMedian;
                            rotations[pointIndex] = Angle.ByRadians(psfRotationWeightedMean).Degree;
                            maxMagnitude = Math.Max(scaledEccentricity, maxMagnitude);
                        } else {
                            eccentricities[pointIndex] = double.NaN;
                            rotations[pointIndex] = double.NaN;
                        }
                        centerXs[pointIndex] = regionCol;
                        centerYs[pointIndex++] = regionRow;
                    }
                }

                var backgroundColor = GetColorFromBrushResource("BackgroundBrush", DrawingColor.White);
                var secondaryBackgroundColor = GetColorFromBrushResource("SecondaryBackgroundBrush", DrawingColor.Gray);
                var primaryColor = GetColorFromBrushResource("PrimaryBrush", DrawingColor.Black);
                var secondaryColor = GetColorFromBrushResource("SecondaryBrush", DrawingColor.Red);

                var plot = new SPPlot();
                plot.Style(dataBackground: secondaryBackgroundColor, figureBackground: backgroundColor, tick: secondaryColor, grid: secondaryColor, axisLabel: primaryColor, titleLabel: primaryColor);

                ScottPlot.Drawing.Colormap colormap = null;
                if (InspectorOptions.EccentricityColorMapEnabled) {
                    colormap = new ScottPlot.Drawing.Colormap(new LinearColormap("G2R", DrawingColor.Green, DrawingColor.GreenYellow, DrawingColor.Red));
                }

                // maxMagnitude * 1.2 is taken from the ScottPlot code to ensure no vector scaling takes place
                var vectorField = new HFVectorField(vectors, xs, ys, colormap: colormap, scaleFactor: maxMagnitude * 1.2 * 1.5, colorScaleMin: 0.3 * 0.3 * scalingFactor, colorScaleMax: 0.6 * 0.6 * scalingFactor, defaultColor: primaryColor);
                plot.Add(vectorField);

                // Scatter points act as anchor points for mouse over events
                var scatterPoints = plot.AddScatterPoints(centerXs, centerYs);
                scatterPoints.IsVisible = false;

                vectorField.ScaledArrowheadLength = 0;
                vectorField.ScaledArrowheadWidth = 0;
                vectorField.ScaledArrowheads = true;
                vectorField.LineWidth = 3;
                vectorField.Anchor = ArrowAnchor.Center;

                var highlightedPoint = plot.AddPoint(0, 0);

                highlightedPoint.Color = secondaryColor;
                highlightedPoint.MarkerSize = 7;
                highlightedPoint.MarkerShape = ScottPlot.MarkerShape.filledCircle;
                highlightedPoint.IsVisible = false;
                highlightedPoint.TextFont.Color = primaryColor;
                highlightedPoint.TextFont.Bold = true;

                plot.XAxis.Ticks(false);
                plot.YAxis.Ticks(false);

                lastHighlightedEccentricityPointIndex = -1;
                highlightedEccentricityPoint = highlightedPoint;
                eccentricityCenterPoints = scatterPoints;
                eccentricityValues = eccentricities;
                rotationValues = rotations;
                EccentricityVectorPlot = plot;
            }
        }

        private async Task<bool> AnalyzeExposure() {
            var localAnalyzeTask = analyzeTask;
            if (analyzeTask != null && !analyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }

            analyzeCts?.Cancel();
            var localAnalyzeCts = new CancellationTokenSource();
            analyzeCts = localAnalyzeCts;

            var autoFocusEngine = autoFocusEngineFactory.Create();
            localAnalyzeTask = Task.Run(async () => {
                bool lastResult = false;
                do {
                    lastResult = await TakeAndAnalyzeExposure(autoFocusEngine, analyzeCts.Token);
                } while (LoopingExposureAnalysis && !analyzeCts.Token.IsCancellationRequested);
                return lastResult;
            });
            analyzeTask = localAnalyzeTask;
            OnAnalysisRunningChanged();

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection exposure analysis cancelled");
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Inspection exposure analysis failed: {e.Message}");
                Logger.Error("Inspection exposure analysis failed", e);
                return false;
            } finally {
                OnAnalysisRunningChanged();
            }
        }

        // Pure retention decision: Review Frames needs both the user toggle on AND the resolved (capture-time on
        // replay) sensor-curve-model flag that actually gates the snapshot build, so the two never disagree.
        internal static bool IsFrameReviewRequested(bool frameReviewEnabled, bool resolvedSensorCurveModelEnabled) {
            return frameReviewEnabled && resolvedSensorCurveModelEnabled;
        }

        private AutoFocusEngineOptions GetAutoFocusEngineOptions(IAutoFocusEngine autoFocusEngine, SavedAutoFocusAttempt savedAutoFocusAttempt = null) {
            var options = autoFocusEngine.GetOptions(savedAutoFocusAttempt);
            if (inspectorOptions.FramesPerPoint > 0) {
                options.FramesPerPoint = inspectorOptions.FramesPerPoint;
            }
            if (inspectorOptions.StepCount > 0) {
                options.AutoFocusInitialOffsetSteps = inspectorOptions.StepCount;
            }
            if (inspectorOptions.StepSize > 0 && savedAutoFocusAttempt != null) {
                options.AutoFocusStepSize = inspectorOptions.StepSize;
            }
            // Resolve the base autofocus timeout (inspector override, else the profile default GetOptions already put
            // on the options) BEFORE amplification, so signal amplification scales the resolved value by its factor.
            if (inspectorOptions.TimeoutSeconds > 0) {
                options.AutoFocusTimeout = TimeSpan.FromSeconds(inspectorOptions.TimeoutSeconds);
            }
            // Signal amplification: capture more, finer-spaced points over the same sweep range by dividing the step
            // size and multiplying the step count by the factor, and scale the autofocus timeout by that same factor so
            // the longer sweep does not time out. Only meaningful for LIVE captures — a replay re-uses the saved frames'
            // fixed focuser positions (savedAutoFocusAttempt != null), so those stay untouched.
            ApplySignalAmplification(options, inspectorOptions.SignalAmplification, isLiveCapture: savedAutoFocusAttempt == null);
            if (inspectorOptions.DetailedAnalysisExposureSeconds > 0) {
                options.OverrideAutoFocusExposureTime = TimeSpan.FromSeconds(inspectorOptions.DetailedAnalysisExposureSeconds);
            }
            // Freeze the frame-review decision at run start (atomically with the retention decision). Review needs the
            // per-frame images retained, which the engine only does when PreserveExposures is on; force it on for review
            // even on non-saving runs. Applies to all run/rerun paths since they all build options through here.
            // Force exposure retention whenever review is enabled; the definitive frameReviewRequestedForRun
            // (which gates the snapshot build) is set in AnalyzeAutoFocusResult from the RESOLVED sensor-curve
            // flag, because option (b) replay runs with the captured flag, not the live inspectorOptions one.
            if (options.Save || inspectorOptions.FrameReviewEnabled) {
                options.PreserveExposures = true;
            }
            return options;
        }

        // Applies the Signal Amplification factor to a live sensor-model / tilt sweep: divides the focuser step size
        // and multiplies the step count by the factor, so the same sweep range is covered with more, finer-spaced
        // points (more signal, smaller defocus jumps between adjacent frames). Those extra points multiply the sweep's
        // exposure count, so the autofocus timeout is scaled by the same factor to keep the longer sweep from timing
        // out. The timeout is scaled on the per-run options override (never the persisted AutoFocusTimeoutSeconds), so
        // there is nothing to restore afterward. No-op at factor <= 1 or on replay (isLiveCapture == false), since
        // replay re-uses the saved frames' fixed focuser positions. Internal for tests.
        internal static void ApplySignalAmplification(AutoFocusEngineOptions options, int signalAmplification, bool isLiveCapture) {
            var amp = Math.Max(1, signalAmplification);
            if (amp > 1 && isLiveCapture) {
                options.AutoFocusInitialOffsetSteps *= amp;
                options.AutoFocusStepSize = Math.Max(1, (int)Math.Round(options.AutoFocusStepSize / (double)amp));
                options.AutoFocusTimeout *= amp;
            }
        }

        // Estimated sweep size for one live sensor-model autofocus run at the given settings.
        // points ≈ 2·offsetSteps·amp + 1 (the engine can extend a sweep, so callers label it "~");
        // images = points × framesPerPoint. Returns (0, 0) when the inputs cannot be resolved.
        internal static (int points, int images) EstimateImagesPerRun(
            int stepCount, int framesPerPoint, int signalAmplification, int profileOffsetSteps, int profileFramesPerPoint) {
            int offsetSteps = stepCount > 0 ? stepCount : profileOffsetSteps;
            int frames = framesPerPoint > 0 ? framesPerPoint : profileFramesPerPoint;
            if (offsetSteps <= 0 || frames <= 0) return (0, 0);
            int amp = Math.Max(1, signalAmplification);
            int points = 2 * offsetSteps * amp + 1;
            return (points, points * frames);
        }

        // Prose shown beside the Signal Amplification control. Internal for tests.
        internal static string BuildSignalAmplificationSummary(
            int stepCount, int framesPerPoint, int signalAmplification, int profileOffsetSteps, int profileFramesPerPoint) {
            var (points, images) = EstimateImagesPerRun(stepCount, framesPerPoint, signalAmplification, profileOffsetSteps, profileFramesPerPoint);
            if (images <= 0) return string.Empty;
            int frames = framesPerPoint > 0 ? framesPerPoint : profileFramesPerPoint;
            return $"Each autofocus run will capture ~{images} images ({points} focus positions × {frames} exposure{(frames == 1 ? "" : "s")} each). " +
                "Higher values collect more, finer-spaced points for a steadier fit on weak signal; a value of 1 runs a regular autofocus (fastest).";
        }

        public string SignalAmplificationSummary =>
            BuildSignalAmplificationSummary(
                inspectorOptions.StepCount,
                inspectorOptions.FramesPerPoint,
                inspectorOptions.SignalAmplification,
                profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps,
                profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint);

        private StarDetectionRegion GetAutoFocusRegion(AutoFocusEngineOptions options) {
            var analysisParams = new StarDetectionParams() {
                Sensitivity = profileService.ActiveProfile.ImageSettings.StarSensitivity,
                NoiseReduction = profileService.ActiveProfile.ImageSettings.NoiseReduction,
                NumberOfAFStars = options.NumberOfAFStars,
                IsAutoFocus = true
            };
            if (profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1) {
                analysisParams.UseROI = true;
                analysisParams.InnerCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio;
                analysisParams.OuterCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusOuterCropRatio;
            }
            return StarDetectionRegion.FromStarDetectionParams(analysisParams);
        }

        private async Task<bool> AnalyzeSavedAutoFocusRun() {
            var localAnalyzeTask = analyzeTask;
            if (analyzeTask != null && !analyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }

            analyzeCts?.Cancel();
            var localAnalyzeCts = new CancellationTokenSource();
            analyzeCts = localAnalyzeCts;

            var autoFocusEngine = autoFocusEngineFactory.Create();
            string selectedPath = "";
            SavedAutoFocusAttempt savedAttempt;
            try {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                    if (!String.IsNullOrEmpty(autoFocusOptions.LastSelectedLoadPath)) {
                        dialog.SelectedPath = autoFocusOptions.LastSelectedLoadPath;
                    }
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
                        return false;
                    }

                    selectedPath = dialog.SelectedPath;
                    autoFocusOptions.LastSelectedLoadPath = selectedPath;
                }

                savedAttempt = autoFocusEngine.LoadSavedAutoFocusAttempt(selectedPath);
                selectedPath = savedAttempt.FolderPath;
            } catch (Exception e) {
                Notification.ShowError(e.Message);
                Logger.Error($"Failed to load saved auto focus attempt from {selectedPath}");
                return false;
            }

            Logger.Info($"Rerunning auto focus attempt from {selectedPath}");

            // If the saved run has a metadata.json, prompt for how to replay (current settings / the run's
            // capture-time settings in memory / update the profile to the captured settings). Resolved on the UI
            // thread (the modal and any profile update raise INPC). No metadata ⇒ current behavior; cancel ⇒ abort.
            var resolution = await AutoFocusReplayCoordinator.ResolveAsync(
                windowServiceFactory,
                applicationDispatcher,
                profileService,
                savedAttempt.FolderPath,
                isInteractive: true,
                () => GetAutoFocusEngineOptions(autoFocusEngine, savedAttempt),
                texts: ReplaySettingsPromptTexts.Inspector);
            if (resolution.Cancelled) {
                return false;
            }

            string outputFolder = null;
            localAnalyzeTask = Task.Run(async () => {
                var options = resolution.Options;
                // The regions to analyze (and the sensor-curve-model flag that shapes them) are an Inspector
                // (application) concern, not a captured one — always use the current Inspector grid so ANY saved run,
                // including a single-region regular AutoFocus run, is analyzed across the Inspector's regions (a
                // captured single region would otherwise leave RegionHFRs with one entry and crash the inspector
                // report). Only the capture-time DETECTION settings (option b) are replayed, via
                // options.StarDetectionOptionsOverride, which the explicit-region detection path applies regardless of
                // which regions are used. ROI follows the regions: the Inspector grid uses the app's SensorROI/CornersROI
                // and the AF region uses the app's crop (see GetAutoFocusRegion).
                var sensorCurveModelEnabled = ResolveSensorCurveModelEnabled();
                var regions = GetStarDetectionRegions(options, sensorCurveModelEnabled: sensorCurveModelEnabled);

                autoFocusEngine.Started += AutoFocusEngine_Started;
                autoFocusEngine.Failed += AutoFocusEngine_Failed;
                autoFocusEngine.Completed += AutoFocusEngine_CompletedNoReport;
                autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;
                autoFocusEngine.SubMeasurementPointCompleted += AutoFocusEngine_SubMeasurementPointCompleted;

                var imagingFilter = GetImagingFilter();

                ActivateAutoFocusChart();
                ResetErrors();
                ResetExposureAnalysis();
                var result = await autoFocusEngine.RerunWithRegions(options, savedAttempt, imagingFilter, regions, localAnalyzeCts.Token, this.progress);
                if (result == null) {
                    InspectorErrorText = "AutoFocus Analysis Failed";
                    DeactivateAutoFocusAnalysis();
                    return false;
                }

                outputFolder = result.SaveFolder;

                var autoFocusAnalysisResult = await AnalyzeAutoFocusResult(
                    options,
                    result,
                    sensorCurveModelEnabled: sensorCurveModelEnabled,
                    ct: localAnalyzeCts.Token,
                    true);
                if (!autoFocusAnalysisResult) {
                    Notification.ShowError("AutoFocus Analysis Failed");
                    InspectorErrorText = "AutoFocus Analysis Failed";
                    DeactivateAutoFocusAnalysis();
                    return false;
                }
                ActivateTiltMeasurement();

                var finalDirectoryCandidates = new DirectoryInfo(selectedPath).Parent.GetDirectories("final");
                if (finalDirectoryCandidates.Length == 0) {
                    SimpleAnalysisErrorText = "Cannot display FWHM Contour and Eccentricity Vectors.\nSaved AutoFocus doesn't contain a final folder.\nEnable \"Validate HFR Improvement\" in Hocus Focus Options";
                    Logger.Warning($"No final directory found. Continuing without doing exposure analysis.");
                } else {
                    var finalDirectory = finalDirectoryCandidates[0].FullName;
                    try {
                        var savedFinalAttempt = autoFocusEngine.LoadSavedFinalAttempt(finalDirectory);
                        if (savedFinalAttempt.SavedImages.Count == 0) {
                            SimpleAnalysisErrorText = $"Cannot display FWHM Contour and Eccentricity Vectors\n.No saved images in final directory {finalDirectory}.";
                            Logger.Error($"No saved images in final directory {finalDirectory}. Continuing without doing exposure analysis");
                            Notification.ShowError($"No saved images in final directory {finalDirectory}. Continuing without doing exposure analysis");
                            /*                        } else if (savedFinalAttempt.SavedImages.Count > 1) {
                                                        SimpleAnalysisErrorText = $"Cannot display FWHM Contour and Eccentricity Vectors.\nMultiple saved images in final directory {finalDirectory}.";
                                                        Logger.Error($"Multiple saved images in final directory {finalDirectory}. Continuing without doing exposure analysis");
                                                        Notification.ShowError($"Multiple saved images in final directory {finalDirectory}. Continuing without doing exposure analysis");
                            */
                        } else {
                            var savedFinalImage = savedFinalAttempt.SavedImages[0];
                            var exposureAnalysisResult = await LoadAndAnalyzeExposure(autoFocusEngine, options, savedFinalImage, analyzeCts.Token);
                            if (!exposureAnalysisResult) {
                                SimpleAnalysisErrorText = $"Cannot display FWHM Contour and Eccentricity Vectors.\nAnalyzing final exposure failed.";
                                Logger.Error("Final Exposure Analysis Failed");
                                Notification.ShowError("Final Exposure Analysis Failed");
                                DeactivateExposureAnalysis();
                            } else {
                                ActivateExposureAnalysis();
                            }
                        }
                    } catch (Exception e) {
                        SimpleAnalysisErrorText = $"Cannot display FWHM Contour and Eccentricity Vectors.\nFailed to read image in final directory {finalDirectory}.\n{e.Message}";
                        Logger.Error(e, $"Failed to read image in final directory {finalDirectory}. Continuing without doing exposure analysis");
                        Notification.ShowError($"Failed to read image in final directory {finalDirectory}. {e.Message}");
                    }
                }

                Notification.ShowInformation("Aberration Inspection Complete");
                return true;
            });
            analyzeTask = localAnalyzeTask;
            OnAnalysisRunningChanged();

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection auto focus rerun analysis cancelled");
                InspectorErrorText = "Inspection AutoFocus Rerun analysis cancelled";
                DeactivateAutoFocusAnalysis();
                return false;
            } catch (Exception e) {
                if ((inspectorOptions.SaveImagesOnReruns) && (outputFolder != null)) {
                    await SaveRegisteredImages(outputFolder,
                        SensorModel.SensorModelResult.RegisteredStars,
                        SensorModel.TrianglesByImage,
                        SensorModel.ReferenceImage,
                        inspectorOptions.SaveAlignmentImages);
                }

                Notification.ShowError($"Inspection auto focus rerun analysis failed: {e.Message}");
                InspectorErrorText = $"Inspection AutoFocus Rerun analysis failed\n{e.Message}";
                Logger.Error("Inspection auto focus rerun analysis failed", e);
                DeactivateAutoFocusAnalysis();
                return false;
            } finally {
                OnAnalysisRunningChanged();
            }
        }

        private List<StarDetectionRegion> GetStarDetectionRegions(AutoFocusEngineOptions options, bool sensorCurveModelEnabled) {
            var starDetectionRegion = GetAutoFocusRegion(options);
            var one_third = 1.0d / 3.0d;
            var width = one_third * inspectorOptions.CornersROI * inspectorOptions.SensorROI;
            var distanceFromBoundary = (1.0 - inspectorOptions.SensorROI) / 2.0;
            var innerStart = distanceFromBoundary;
            var outerStart = 1.0 - distanceFromBoundary - width;

            var regions = new List<StarDetectionRegion>() {
                    starDetectionRegion,
                    new StarDetectionRegion(new RatioRect(one_third, one_third, one_third, one_third), index: 1),
                    new StarDetectionRegion(new RatioRect(innerStart, innerStart, width, width), index: 2),
                    new StarDetectionRegion(new RatioRect(outerStart, innerStart, width, width), index: 3),
                    new StarDetectionRegion(new RatioRect(innerStart, outerStart, width, width), index: 4),
                    new StarDetectionRegion(new RatioRect(outerStart, outerStart, width, width), index: 5)
                };
            if (sensorCurveModelEnabled) {
                var roiValue = (1.0d - inspectorOptions.SensorROI) / 2.0;
                regions.Add(new StarDetectionRegion(new RatioRect(roiValue, roiValue, 1.0d - roiValue, 1.0d - roiValue), index: 6));
            }
            return regions;
        }

        private void AutoFocusEngine_Started(object sender, AutoFocusStartedEventArgs e) {
            this.ClearAnalysis();
        }

        private void AutoFocusEngine_MeasurementPointCompleted(object sender, AutoFocusMeasurementPointCompletedEventArgs e) {
            if (e.RegionIndex >= RegionCurveFittings.Count) {
                return;
            }

            RegionCurveFittings[e.RegionIndex] = GetCurveFitting(e.Fittings);
            RegionLineFittings[e.RegionIndex] = GetLineFitting(e.Fittings);
            RegionPlotFocusPoints[e.RegionIndex].AddSorted(new DataPoint(e.FocuserPosition, e.Measurement.Measure), plotPointComparer);

            var focusPoints = RegionFocusPoints[e.RegionIndex];
            focusPoints.AddSorted(new ScatterErrorPoint(e.FocuserPosition, e.Measurement.Measure, 0, AutoFocusEngine.SafeDisplayError(e.Measurement.Stdev)), focusPointComparer);
        }

        private void AutoFocusEngine_SubMeasurementPointCompleted(object sender, AutoFocusSubMeasurementPointCompletedEventArgs e) {
            if (e.RegionIndex == 6) {
                var hfStarDetectionResult = e.StarDetectionResult as HocusFocusStarDetectionResult;
                if (hfStarDetectionResult != null) {
                    // SubMeasurementPointCompleted fires concurrently for different focuser positions, so guard the
                    // List.Add against concurrent mutation. Order is irrelevant (consumers OrderBy(FocuserPosition)).
                    lock (fullSensorDetectedStarsLock) {
                        FullSensorDetectedStars.Add(new SensorDetectedStars(e.FocuserPosition, hfStarDetectionResult, e.Image));
                    }
                }
            }
        }

        private void GenerateReport(AutoFocusCompletedEventArgs e) {
            var regionReports = new HocusFocusReport[RegionFocusPoints.Length];
            for (int regionIndex = 0; regionIndex < RegionFocusPoints.Length; ++regionIndex) {
                var region = e.RegionHFRs[regionIndex];
                var finalFocusPoint = new DataPoint(region.EstimatedFinalFocuserPosition, region.EstimatedFinalHFR);
                var lastAutoFocusPoint = new ReportAutoFocusPoint {
                    Focuspoint = finalFocusPoint,
                    Temperature = e.Temperature,
                    Timestamp = DateTime.Now,
                    Filter = e.Filter
                };
                var report = HocusFocusReport.GenerateReport(
                    profileService: this.profileService,
                    starDetector: starDetectionSelector.GetBehavior(),
                    focusPoints: RegionFocusPoints[regionIndex],
                    fittings: region.Fittings,
                    initialFocusPosition: e.InitialFocusPosition,
                    initialHFR: region.InitialHFR ?? 0.0d,
                    finalHFR: region.FinalHFR ?? region.EstimatedFinalHFR,
                    filter: e.Filter,
                    temperature: e.Temperature,
                    focusPoint: finalFocusPoint,
                    lastFocusPoint: lastAutoFocusPoint,
                    region: region.Region,
                    hocusFocusStarDetectionOptions: this.starDetectionOptions,
                    hocusFocusAutoFocusOptions: this.autoFocusOptions,
                    duration: e.Duration);
                regionReports[regionIndex] = report;
            }

            if (!string.IsNullOrEmpty(e.SaveFolder)) {
                for (int regionIndex = 0; regionIndex < RegionFocusPoints.Length; ++regionIndex) {
                    var regionReport = regionReports[regionIndex];
                    var regionReportText = JsonConvert.SerializeObject(regionReport, Formatting.Indented);
                    var targetFilePath = Path.Combine(e.SaveFolder, $"attempt{e.Iteration:00}", $"autofocus_report_Region{regionIndex}.json");
                    File.WriteAllText(targetFilePath, regionReportText);
                }
            }

            var reportText = JsonConvert.SerializeObject(regionReports[0], Formatting.Indented);
            string path = Path.Combine(HocusFocusVM.ReportDirectory, DateTime.Now.ToString("yyyy-MM-dd--HH-mm-ss") + ".json");
            // Atomic write: NINA core watches ReportDirectory and File.OpenText's new reports; a plain
            // File.WriteAllText's open write handle races that reader into a sharing-violation IOException.
            PathUtility.WriteAllTextAtomic(path, reportText);

            var firstRegionReport = regionReports[0];
            var autoFocusInfo = new AutoFocusInfo(firstRegionReport.Temperature, firstRegionReport.CalculatedFocusPoint.Position, firstRegionReport.Filter, firstRegionReport.Timestamp);
            focuserMediator.BroadcastSuccessfulAutoFocusRun(autoFocusInfo);
        }

        private void AutoFocusEngine_Completed(object sender, AutoFocusCompletedEventArgs e) {
            AutoFocusEngine_CompletedNoReport(sender, e);
            GenerateReport(e);
        }

        private void MaybeSaveFailedAutoFocusReports(AutoFocusFailedEventArgs e) {
            if (!string.IsNullOrEmpty(e.SaveFolder)) {
                for (int regionIndex = 0; regionIndex < RegionFocusPoints.Length; ++regionIndex) {
                    var region = e.RegionHFRs[regionIndex];
                    var finalFocusPoint = new DataPoint(-1.0d, 0.0d);
                    var lastAutoFocusPoint = new ReportAutoFocusPoint {
                        Focuspoint = finalFocusPoint,
                        Temperature = e.Temperature,
                        Timestamp = DateTime.Now,
                        Filter = e.Filter
                    };
                    var report = HocusFocusReport.GenerateReport(
                        profileService: this.profileService,
                        starDetector: starDetectionSelector.GetBehavior(),
                        focusPoints: RegionFocusPoints[regionIndex],
                        fittings: region.Fittings,
                        initialFocusPosition: e.InitialFocusPosition,
                        initialHFR: region.InitialHFR ?? 0.0d,
                        finalHFR: region.FinalHFR ?? region.EstimatedFinalHFR,
                        filter: e.Filter,
                        temperature: e.Temperature,
                        focusPoint: finalFocusPoint,
                        lastFocusPoint: lastAutoFocusPoint,
                        region: region.Region,
                        hocusFocusStarDetectionOptions: this.starDetectionOptions,
                        hocusFocusAutoFocusOptions: this.autoFocusOptions,
                        duration: e.Duration);
                    var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
                    var targetFilePath = Path.Combine(e.SaveFolder, $"attempt{e.Iteration:00}", $"autofocus_report_Region{regionIndex}.json");
                    File.WriteAllText(targetFilePath, reportText);
                }
            }
        }

        private void AutoFocusEngine_IterationFailed(object sender, AutoFocusFailedEventArgs e) {
            MaybeSaveFailedAutoFocusReports(e);
            ClearPlots();
        }

        private void AutoFocusEngine_Failed(object sender, AutoFocusFailedEventArgs e) {
            MaybeSaveFailedAutoFocusReports(e);

            var report = GenerateReportForRegion(e, 0);
            var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
            string path = Path.Combine(HocusFocusVM.ReportDirectory, DateTime.Now.ToString("yyyy-MM-dd--HH-mm-ss") + ".json");
            // Atomic write (see the success-path write above): avoids the ReportDirectory read-while-write race
            // with NINA core's AutoFocusToolVM.LoadChart. This is the exact site that produced the reported
            // IOException on the AF-failed path.
            PathUtility.WriteAllTextAtomic(path, reportText);
        }

        private HocusFocusReport GenerateReportForRegion(AutoFocusFinishedEventArgsBase e, int regionIndex) {
            var region = e.RegionHFRs[regionIndex];
            var finalFocusPoint = new DataPoint(-1.0d, 0.0d);
            var lastAutoFocusPoint = new ReportAutoFocusPoint {
                Focuspoint = finalFocusPoint,
                Temperature = e.Temperature,
                Timestamp = DateTime.Now,
                Filter = e.Filter
            };
            return HocusFocusReport.GenerateReport(
                profileService: this.profileService,
                starDetector: starDetectionSelector.GetBehavior(),
                focusPoints: RegionFocusPoints[regionIndex],
                fittings: region.Fittings,
                initialFocusPosition: e.InitialFocusPosition,
                initialHFR: region.InitialHFR ?? 0.0d,
                finalHFR: region.FinalHFR ?? region.EstimatedFinalHFR,
                filter: e.Filter,
                temperature: e.Temperature,
                focusPoint: finalFocusPoint,
                lastFocusPoint: lastAutoFocusPoint,
                region: region.Region,
                hocusFocusStarDetectionOptions: this.starDetectionOptions,
                hocusFocusAutoFocusOptions: this.autoFocusOptions,
                duration: e.Duration);
        }

        private void UpdateBackfocusMeasurements(AutoFocusResult result) {
            var centerRegionResult = result.RegionResults[1];
            var centerHFR = centerRegionResult.EstimatedFinalHFR;
            var centerFocuser = centerRegionResult.EstimatedFinalFocuserPosition;
            var outerHFRSum = 0.0d;
            var outerFocuserPositionSum = 0.0d;
            for (int i = 2; i < 6; ++i) {
                var regionResult = result.RegionResults[i];
                outerHFRSum += regionResult.EstimatedFinalHFR;
                outerFocuserPositionSum += regionResult.EstimatedFinalFocuserPosition;
            }

            var fRatio = profileService.ActiveProfile.TelescopeSettings.FocalRatio;
            CriticalFocusMicrons = 2.44 * fRatio * fRatio * 0.55;
            InnerFocuserPosition = centerFocuser;
            OuterFocuserPosition = outerFocuserPositionSum / 4;
            BackfocusFocuserPositionDelta = OuterFocuserPosition - InnerFocuserPosition;
            if (BackfocusFocuserPositionDelta > 0) {
                BackfocusDirection = "TOWARDS";
            } else {
                BackfocusDirection = "AWAY FROM";
            }
            if (InspectorOptions.MicronsPerFocuserStep > 0) {
                BackfocusMicronDelta = BackfocusFocuserPositionDelta * InspectorOptions.MicronsPerFocuserStep;
            } else {
                BackfocusMicronDelta = double.NaN;
            }

            BackfocusWithinCFZ = Math.Abs(BackfocusMicronDelta) < criticalFocusMicrons;
            InnerHFR = centerHFR;
            OuterHFR = outerHFRSum / 4;
            BackfocusHFR = OuterHFR - InnerHFR;
        }

        // The Inspector region report indexes RegionHFRs[1..5] (center = 1, corners = 2..5), so it needs the full
        // Inspector grid of at least 6 regions. Extracted + internal so the precondition is unit-testable.
        internal static bool HasInspectorRegionLayout(int regionCount) => regionCount >= 6;

        private void AutoFocusEngine_CompletedNoReport(object sender, AutoFocusCompletedEventArgs e) {
            // The reprocess path now always uses the Inspector grid (>= 6 regions), but guard defensively so a run with
            // fewer regions logs cleanly instead of throwing an unhandled IndexOutOfRange.
            if (e.RegionHFRs == null || !HasInspectorRegionLayout(e.RegionHFRs.Count)) {
                Logger.Warning($"Skipping inspector region report: expected the Inspector's >= 6 region grid but got {e.RegionHFRs?.Count ?? 0}. This run is not an Aberration Inspector run.");
                return;
            }
            var logReportBuilder = new StringBuilder();
            var centerHFR = e.RegionHFRs[1].EstimatedFinalHFR;
            var centerFocuser = e.RegionHFRs[1].EstimatedFinalFocuserPosition;
            RegionFinalFocusPoints[1] = new DataPoint(e.RegionHFRs[1].EstimatedFinalFocuserPosition, e.RegionHFRs[1].EstimatedFinalHFR);
            logReportBuilder.AppendLine($"Center - HFR: {centerHFR}, Focuser: {centerFocuser}");

            for (int i = 2; i < 6; ++i) {
                var regionName = GetRegionName(i);
                var regionHFR = e.RegionHFRs[i];
                logReportBuilder.AppendLine($"{regionName} - HFR Delta: {regionHFR.EstimatedFinalHFR - centerHFR}, Focuser Delta: {regionHFR.EstimatedFinalFocuserPosition - centerFocuser}");

                RegionFinalFocusPoints[i] = new DataPoint(regionHFR.EstimatedFinalFocuserPosition, regionHFR.EstimatedFinalHFR);
            }

            Logger.Info(logReportBuilder.ToString());
        }

        private string GetRegionName(int regionIndex) {
            if (regionIndex == 1) {
                return "Center";
            } else if (regionIndex == 2) {
                return "Top Left";
            } else if (regionIndex == 3) {
                return "Top Right";
            } else if (regionIndex == 4) {
                return "Bottom Left";
            } else if (regionIndex == 5) {
                return "Bottom Right";
            } else if (regionIndex == 6) {
                return "Extra (Full)";
            }
            throw new ArgumentException($"{regionIndex} is not a valid region index", "regionIndex");
        }

        private void ClearAnalysis() {
            lock (fullSensorDetectedStarsLock) {
                FullSensorDetectedStars.Clear();
            }
            ClearReviewSnapshot();
            ClearPlots();
        }

        private void ClearPlots() {
            for (int i = 0; i < RegionCurveFittings.Count; ++i) {
                RegionCurveFittings[i] = null;
                RegionLineFittings[i] = null;
                RegionPlotFocusPoints[i].Clear();
                RegionFinalFocusPoints[i] = new DataPoint(-1.0d, 0.0d);
                RegionFocusPoints[i].Clear();
            }
            AutoFocusChartPlotModel?.ResetAllAxes();
            SensorModel.Reset();
            AutoFocusCompleted = false;
        }

        private void CancelAnalyze() {
            analyzeCts?.Cancel();
        }

        private Task<bool> slewToZenithTask;
        private CancellationTokenSource slewToZenithCts;

        private async Task<bool> SlewToZenith(bool west) {
            var localTask = slewToZenithTask;
            if (localTask != null && !localTask.IsCompleted) {
                Notification.ShowError("Slew to zenith still in progress");
                return false;
            }

            slewToZenithCts?.Cancel();
            var localCts = new CancellationTokenSource();
            slewToZenithCts = localCts;

            localTask = Task.Run(async () => {
                var astrometry = profileService.ActiveProfile.AstrometrySettings;
                var latitude = Angle.ByDegree(astrometry.Latitude);
                var longitude = Angle.ByDegree(astrometry.Longitude);
                var azimuth = west ? Angle.ByDegree(90) : Angle.ByDegree(270);
                var coordinates = new TopocentricCoordinates(azimuth, Angle.ByDegree(89), latitude, longitude, astrometry.Elevation);
                return await telescopeMediator.SlewToTopocentricCoordinates(coordinates, localCts.Token);
            }, localCts.Token);
            slewToZenithTask = localTask;
            RefreshCommandStates();

            try {
                return await localTask;
            } catch (OperationCanceledException) {
                Logger.Info("SlewToZenith cancelled");
            } catch (Exception e) {
                Notification.ShowError($"Slew to zenith failed. {e.Message}");
                Logger.Error("Slew to zenith failed", e);
            } finally {
                slewToZenithTask = null;
                slewToZenithCts = null;
                RefreshCommandStates();
            }
            return false;
        }

        // These use CommunityToolkit AsyncRelayCommand/RelayCommand, whose CanExecute is only re-evaluated when we
        // call NotifyCanExecuteChanged() (they do not hook CommandManager.RequerySuggested like NINA's MVVMLight
        // RelayCommand did), so they are typed as IRelayCommand and refreshed via RefreshCommandStates().
        public IRelayCommand RunAutoFocusAnalysisCommand { get; private set; }
        public IRelayCommand RerunSavedAutoFocusAnalysisCommand { get; private set; }
        public IRelayCommand RunExposureAnalysisCommand { get; private set; }
        public ICommand CancelAnalyzeCommand { get; private set; }
        public IRelayCommand ClearAnalysesCommand { get; private set; }
        public IRelayCommand SlewToZenithEastCommand { get; private set; }
        public IRelayCommand SlewToZenithWestCommand { get; private set; }
        public ICommand CancelSlewToZenithCommand { get; private set; }

        // RelayCommand (not ICommand) so we can call NotifyCanExecuteChanged when the snapshot becomes (un)available.
        public RelayCommand ReviewFramesCommand { get; private set; }

        // AsyncRelayCommand (not ICommand) so we can call NotifyCanExecuteChanged from the tilt device
        // service's INPC, RebuildTiltGuidance, and the measurement-generation counter. See the "Tilt Adapter
        // Automatic Adjustment" region below for the full flow.
        public AsyncRelayCommand AutomaticAdjustmentCommand { get; private set; }

        private readonly object fullSensorDetectedStarsLock = new object();
        private readonly List<SensorDetectedStars> FullSensorDetectedStars = new List<SensorDetectedStars>();

        // The immutable per-frame review data built at the end of a sensor-model run when "Keep frames for Review" was
        // on. Null until a qualifying run completes; cleared at run start and on Clear Analyses. frameReviewRequestedForRun
        // freezes the toggle's value at run start (when PreserveExposures is decided) so a mid-run toggle change can't
        // desync image retention from the snapshot build.
        private FrameReviewSnapshot reviewSnapshot;
        private bool frameReviewRequestedForRun;

        /// <summary>Whether a completed sensor-model run produced reviewable frames (drives the "Review Frames" button).</summary>
        public bool ReviewFramesAvailable => reviewSnapshot?.Frames.Count > 0;

        // Raise the availability binding + re-evaluate the command, marshaled to the UI thread (the snapshot is built /
        // cleared on the analysis background task). DispatchSynchronizationContext is a synchronous Send with a
        // same-context fast path, so it is safe to call from either thread.
        private void NotifyReviewFramesAvailabilityChanged() {
            applicationDispatcher.DispatchSynchronizationContext(() => {
                RaisePropertyChanged(nameof(ReviewFramesAvailable));
                ReviewFramesCommand.NotifyCanExecuteChanged();
            });
        }

        // Re-evaluate CanExecute for the connection-/analysis-/slew-gated buttons. Their commands are CommunityToolkit
        // AsyncRelayCommand/RelayCommand, which (unlike NINA's MVVMLight RelayCommand) do not hook
        // CommandManager.RequerySuggested, so their CanExecute is only re-checked when we raise it here. Call whenever a
        // CanExecute input changes: a device connects/disconnects, or an analysis/slew task starts or finishes.
        // POST, not the blocking DispatchSynchronizationContext: device-info updates arrive on the DeviceUpdateTimer's
        // broadcast, which shutdown awaits — a blocking Invoke back onto the tearing-down UI thread would deadlock the
        // close (exactly the hazard ApplicationDispatcher.PostSynchronizationContext is documented to avoid). BeginInvoke
        // queues the requery and returns; NotifyCanExecuteChanged only needs to update button IsEnabled eventually, not
        // synchronously. Null-conditional so it is a no-op if a device snapshot arrives before the commands are built.
        private void RefreshCommandStates() {
            applicationDispatcher.PostSynchronizationContext(() => {
                RunAutoFocusAnalysisCommand?.NotifyCanExecuteChanged();
                RunExposureAnalysisCommand?.NotifyCanExecuteChanged();
                RerunSavedAutoFocusAnalysisCommand?.NotifyCanExecuteChanged();
                ClearAnalysesCommand?.NotifyCanExecuteChanged();
                SlewToZenithEastCommand?.NotifyCanExecuteChanged();
                SlewToZenithWestCommand?.NotifyCanExecuteChanged();
            });
        }

        // Analysis start/stop changes both the IsAnalysisRunning binding and the CanExecute of the analysis-gated
        // commands; update both together.
        private void OnAnalysisRunningChanged() {
            RaisePropertyChanged(nameof(IsAnalysisRunning));
            RefreshCommandStates();
        }

        private void ClearReviewSnapshot() {
            reviewSnapshot = null;
            NotifyReviewFramesAvailabilityChanged();
        }

        // Shows the modal Review Frames dialog, mirroring HocusFocusPlugin.OptimizeStarDetection: the VM is presented in
        // a ContentPresenter resolved by the implicit DataType DataTemplate for FrameReviewVM, and is disposed when the
        // window closes (releasing the retained frame bitmaps).
        private void ShowFrameReview() {
            var snapshot = reviewSnapshot;
            if (snapshot == null || snapshot.Frames.Count == 0) {
                return;
            }

            // Release this VM's frozen snapshot bitmaps on close (F36) via the shared host's afterClosed callback.
            var vm = new FrameReviewVM(snapshot);
            ReviewDialogHost.Show(windowServiceFactory, vm, "Review Frames", ClearReviewSnapshot);
        }
        public AsyncObservableCollection<ScatterErrorPoint>[] RegionFocusPoints { get; private set; }
        public AsyncObservableCollection<DataPoint>[] RegionPlotFocusPoints { get; private set; }
        public AsyncObservableCollection<DataPoint> RegionFinalFocusPoints { get; private set; }
        public AsyncObservableCollection<Func<double, double>> RegionCurveFittings { get; private set; }
        public AsyncObservableCollection<TrendlineFitting> RegionLineFittings { get; private set; }
        public PlotModel AutoFocusChartPlotModel { get; set; }

        private string backfocusDirection = "";

        public string BackfocusDirection {
            get => backfocusDirection;
            private set {
                backfocusDirection = value;
                RaisePropertyChanged();
            }
        }

        private double innerFocuserPosition = double.NaN;

        public double InnerFocuserPosition {
            get => innerFocuserPosition;
            private set {
                innerFocuserPosition = value;
                RaisePropertyChanged();
            }
        }

        private double outerFocuserPosition = double.NaN;

        public double OuterFocuserPosition {
            get => outerFocuserPosition;
            private set {
                outerFocuserPosition = value;
                RaisePropertyChanged();
            }
        }

        private double backfocusFocuserPositionDelta = double.NaN;

        public double BackfocusFocuserPositionDelta {
            get => backfocusFocuserPositionDelta;
            private set {
                backfocusFocuserPositionDelta = value;
                RaisePropertyChanged();
            }
        }

        private double backfocusMicronDelta = double.NaN;

        public double BackfocusMicronDelta {
            get => backfocusMicronDelta;
            private set {
                backfocusMicronDelta = value;
                RaisePropertyChanged();
            }
        }

        private double criticalFocusMicrons = double.NaN;

        public double CriticalFocusMicrons {
            get => criticalFocusMicrons;
            private set {
                criticalFocusMicrons = value;
                RaisePropertyChanged();
            }
        }

        private bool backfocusWithinCFZ = false;

        public bool BackfocusWithinCFZ {
            get => backfocusWithinCFZ;
            private set {
                backfocusWithinCFZ = value;
                RaisePropertyChanged();
            }
        }

        private double innerHFR = double.NaN;

        public double InnerHFR {
            get => innerHFR;
            private set {
                innerHFR = value;
                RaisePropertyChanged();
            }
        }

        private double outerHFR = double.NaN;

        public double OuterHFR {
            get => outerHFR;
            private set {
                outerHFR = value;
                RaisePropertyChanged();
            }
        }

        private double backfocusHFR = double.NaN;

        public double BackfocusHFR {
            get => backfocusHFR;
            private set {
                backfocusHFR = value;
                RaisePropertyChanged();
            }
        }

        private HocusFocusStarDetectionResult snapshotAnalysisStarDetectionResult;

        public HocusFocusStarDetectionResult SnapshotAnalysisStarDetectionResult {
            get => snapshotAnalysisStarDetectionResult;
            private set {
                snapshotAnalysisStarDetectionResult = value;
                RaisePropertyChanged();
            }
        }

        public TiltModel TiltModel { get; private set; }
        public SensorModel SensorModel { get; private set; }
        public IInspectorOptions InspectorOptions => this.inspectorOptions;

        public TiltAdapterGuidanceVM TiltGuidance { get; private set; }

        // Two-way bound by the Tilt Adapter Guidance dropdown. Writing it flips the persisted option,
        // whose PropertyChanged is already subscribed to RebuildTiltGuidance() (see the constructor),
        // so the numeric strings + legend regenerate in the new unit automatically.
        public TiltGuidanceAngleUnit TiltGuidanceAngleUnit {
            get => tiltAdapterOptions?.AngleDisplayUnit ?? TiltGuidanceAngleUnit.Turns;
            set {
                if (tiltAdapterOptions != null && tiltAdapterOptions.AngleDisplayUnit != value) {
                    tiltAdapterOptions.AngleDisplayUnit = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool IsAnalysisRunning => AnalysisRunning();

        public bool HasTiltAdapterCalibration =>
            tiltAdapterOptions != null &&
            tiltAdapterOptions.IsCalibrated &&
            tiltAdapterOptions.ScrewCount == tiltAdapterOptions.CalibratedScrewCount;

        private const double GuidanceNoiseThreshold = 0.005;
        private const double GuidanceMinArrowThreshold = 0.1;
        private const double GuidanceLargeArrowThreshold = 0.5;

        // Backfocus arrow thresholds in focuser-µm (CurvatureEffectMicrons units)
        private const double BackfocusNoiseThresholdMicrons = 10.0;
        private const double BackfocusLargeArrowThresholdMicrons = 50.0;

        private void RebuildTiltGuidance() {
            RaisePropertyChanged(nameof(HasTiltAdapterCalibration));

            int n = HasTiltAdapterCalibration ? tiltAdapterOptions.CalibratedScrewCount : 3;
            var guidance = new TiltAdapterGuidanceVM { ScrewCount = n };

            if (HasTiltAdapterCalibration) {
                // σ resolved for the arrow rows; FillNumericGuidance resolves the same 0→default
                // rule for the numeric rows.
                int curvatureSign = tiltAdapterOptions.ScrewInwardCurvatureSign;
                int resolvedSign = curvatureSign == 0 ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign : curvatureSign;

                var tiltPlane = TiltModel?.TiltPlaneModel;
                if (tiltPlane != null) {
                    double a = tiltPlane.A;
                    double b = tiltPlane.B;
                    if (!double.IsNaN(a) && !double.IsNaN(b)) {
                        var angles = new double[n];
                        angles[0] = tiltAdapterOptions.Screw1AngleDegrees;
                        angles[1] = tiltAdapterOptions.Screw2AngleDegrees;
                        angles[2] = tiltAdapterOptions.Screw3AngleDegrees;
                        if (n == 4) angles[3] = tiltAdapterOptions.Screw4AngleDegrees;

                        // Per-screw CW-positive correction turns from the tilt plane. The arrows grid
                        // shows the adapter MOTION those turns produce (⬆ = toward the objective), not
                        // the rotation itself — the rotation glyphs on the numeric rows carry that.
                        var turns = new double[n];
                        for (int i = 0; i < n; i++) {
                            double theta = angles[i] * Math.PI / 180.0;
                            turns[i] = (2.0 / n) * (-a * Math.Sin(theta) + b * Math.Cos(theta));
                        }

                        double maxAbs = turns.Max(t => Math.Abs(t));
                        var tiltArrows = new string[n];
                        for (int i = 0; i < n; i++) {
                            if (maxAbs < GuidanceNoiseThreshold) {
                                tiltArrows[i] = "—";
                            } else {
                                // turns[i] is CW-positive (the stored response-convention angles encode
                                // the rig direction), so adapter MOTION toward the objective = −σ·turns.
                                double ratio = (-resolvedSign * turns[i]) / maxAbs;
                                if (ratio >= GuidanceLargeArrowThreshold) tiltArrows[i] = "⬆";
                                else if (ratio >= GuidanceMinArrowThreshold) tiltArrows[i] = "↑";
                                else if (ratio <= -GuidanceLargeArrowThreshold) tiltArrows[i] = "⬇";
                                else if (ratio <= -GuidanceMinArrowThreshold) tiltArrows[i] = "↓";
                                else tiltArrows[i] = "—";
                            }
                        }
                        guidance.Screw1TiltArrow = tiltArrows[0];
                        guidance.Screw2TiltArrow = tiltArrows[1];
                        guidance.Screw3TiltArrow = tiltArrows[2];
                        if (n == 4) guidance.Screw4TiltArrow = tiltArrows[3];
                        guidance.HasTiltGuidance = true;
                    }
                }

                // Backfocus row: adapter MOTION needed to null the curvature effect. Toward the
                // objective ⇔ the local best-focus position must decrease; the σ in "which rotation
                // is needed" and the σ in "what a rotation does" cancel, so the motion arrow is
                // sign(CurvatureEffectMicrons) — rig-independent physics (see
                // docs/tilt-guidance-motion-arrows-design.md).
                if (SensorModel?.DisplayedSensorModel != null) {
                    double curvatureEffectMicrons = SensorModel.SensorModelResult.CurvatureEffectMicrons;
                    double absMicrons = Math.Abs(curvatureEffectMicrons);

                    string backfocusArrow;
                    if (absMicrons < BackfocusNoiseThresholdMicrons) {
                        backfocusArrow = "—";
                    } else {
                        bool towardObjective = curvatureEffectMicrons > 0;
                        string bigArrow = towardObjective ? "⬆" : "⬇";
                        string smallArrow = towardObjective ? "↑" : "↓";
                        backfocusArrow = absMicrons >= BackfocusLargeArrowThresholdMicrons ? bigArrow : smallArrow;
                    }

                    guidance.Screw1BackfocusArrow = backfocusArrow;
                    guidance.Screw2BackfocusArrow = backfocusArrow;
                    guidance.Screw3BackfocusArrow = backfocusArrow;
                    if (n == 4) guidance.Screw4BackfocusArrow = backfocusArrow;
                    guidance.HasBackfocusRow = true;
                }
            }

            FillNumericGuidance(guidance, n);

            // Direction legend only when the arrows/totals it annotates are actually on screen
            // (the arrows grid is gated by HasTiltGuidance, the numeric totals by HasNumericGuidance).
            // Otherwise the calibrated-but-unmeasured state would show a legend right under
            // "Run a measurement to see guidance." with nothing to explain.
            if (guidance.HasTiltGuidance || guidance.HasNumericGuidance) {
                guidance.DirectionLegend = TiltAdapterGuidanceVM.BuildDirectionLegend(
                    steps: tiltAdapterOptions.AdjustmentType == TiltAdjustmentType.StepperMotors,
                    signIsMeasured: tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured,
                    angleUnit: tiltAdapterOptions.AngleDisplayUnit);
            }

            // Publish to the UI on the UI thread. RebuildTiltGuidance runs on the UI thread (ctor) but ALSO on
            // background threads: the analysis task (AnalyzeAutoFocusResult) and — via tiltAdapterOptions
            // .PropertyChanged (subscribed in the ctor) — the connection service's 'cp' poll thread, because a
            // poll persists shadow positions back into tiltAdapterOptions. AutomaticAdjustmentCommand
            // .NotifyCanExecuteChanged() raises through CanExecuteChangedEventManager, which throws off the UI
            // thread, so this must be marshaled. Use the NON-BLOCKING PostSynchronizationContext (BeginInvoke),
            // NOT a blocking DispatchSynchronizationContext (Invoke): a blocking Invoke from the poll thread onto
            // a busy UI thread (e.g. while it lays out the Imaging tab) deadlocks — the same hazard, and the same
            // fix, as RefreshCommandStates. It still runs inline for UI-thread callers; the requery/publish only
            // needs to reach the UI eventually, not synchronously.
            applicationDispatcher.PostSynchronizationContext(() => {
                TiltGuidance = guidance;
                RaisePropertyChanged(nameof(TiltGuidance));

                // Numeric guidance availability and the device-linked calibration marker (both read from
                // tiltAdapterOptions, whose PropertyChanged is what drives every call to this method) both feed
                // Automatic Adjustment's canExecute gate — re-raise its remediation text/visibility and
                // canExecute here so they never go stale.
                RaisePropertyChanged(nameof(AutomaticAdjustmentRemediationVisible));
                RaisePropertyChanged(nameof(AutomaticAdjustmentRemediationText));
                AutomaticAdjustmentCommand?.NotifyCanExecuteChanged();
            });
        }

        private const double PitchMismatchFraction = 0.15;

        // Populate the precise per-screw turn/step amounts from the fitted paraboloid model and the
        // configured adapter hardware. All three rows carry their own rotation direction (⟳/⟲ glyphs
        // for screws, signed steps for steppers); the arrows grid above describes adapter MOTION
        // (⬆ = toward the objective), not rotation. Everything is computed in axial best-focus microns
        // (tilt = -TiltAt, backfocus = -CurvatureAt) then divided by the saved pitch/step size — there
        // is no square root, the curvature term is already a length.
        private void FillNumericGuidance(TiltAdapterGuidanceVM guidance, int n) {
            if (!HasTiltAdapterCalibration) return;
            var model = SensorModel?.DisplayedSensorModel;
            if (model == null) return;

            bool steps = tiltAdapterOptions.AdjustmentType == TiltAdjustmentType.StepperMotors;
            var angleUnit = tiltAdapterOptions.AngleDisplayUnit;
            double unitMicrons = steps ? tiltAdapterOptions.StepperStepSizeMicrons : tiltAdapterOptions.ThreadPitchMicrons;
            double radiusMm = tiltAdapterOptions.ScrewRadiusMillimeters;
            if (unitMicrons <= 0 || radiusMm <= 0) return;

            double radiusMicrons = radiusMm * 1000.0;
            // σ is never 0 from persisted options (defaulted since the direction-setting feature);
            // resolve defensively to the assumed default so every row can carry a direction.
            int curvatureSign = tiltAdapterOptions.ScrewInwardCurvatureSign;
            int resolvedSign = curvatureSign == 0 ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign : curvatureSign;

            var angles = new double[n];
            angles[0] = tiltAdapterOptions.Screw1AngleDegrees;
            angles[1] = tiltAdapterOptions.Screw2AngleDegrees;
            angles[2] = tiltAdapterOptions.Screw3AngleDegrees;
            if (n == 4) angles[3] = tiltAdapterOptions.Screw4AngleDegrees;
            if (angles.Any(double.IsNaN)) return;

            // Per-screw signed targets come from the shared helper (also consumed by the motorized
            // move planner, T14) so the display and the planner can never diverge. The curvature sign
            // applies ONLY to the backfocus component within the helper: the tilt component's
            // direction is already encoded by the stored response-convention screw angle (the same
            // convention the tilt arrows invert), so multiplying the whole total by the sign would
            // double-apply the rig direction to the tilt part on sign = -1 rigs.
            var targets = TiltScrewTargets.ComputePerScrewTargets(
                model.Gx, model.Gy, model.Kx, model.Ky, model.X0, model.Y0, angles, radiusMicrons, unitMicrons, resolvedSign);

            var tiltText = new string[n];
            var backText = new string[n];
            var totalText = new string[n];
            for (int i = 0; i < n; i++) {
                tiltText[i] = TiltAdapterGuidanceVM.FormatAmount(targets[i].TiltSteps, steps, angleUnit);
                backText[i] = TiltAdapterGuidanceVM.FormatAmount(targets[i].BackfocusSteps, steps, angleUnit);
                totalText[i] = TiltAdapterGuidanceVM.FormatAmount(targets[i].TotalSteps, steps, angleUnit);
            }

            guidance.Screw1TiltAmount = tiltText[0];
            guidance.Screw2TiltAmount = tiltText[1];
            guidance.Screw3TiltAmount = tiltText[2];
            if (n == 4) guidance.Screw4TiltAmount = tiltText[3];
            guidance.Screw1BackfocusAmount = backText[0];
            guidance.Screw2BackfocusAmount = backText[1];
            guidance.Screw3BackfocusAmount = backText[2];
            if (n == 4) guidance.Screw4BackfocusAmount = backText[3];
            guidance.Screw1TotalAmount = totalText[0];
            guidance.Screw2TotalAmount = totalText[1];
            guidance.Screw3TotalAmount = totalText[2];
            if (n == 4) guidance.Screw4TotalAmount = totalText[3];

            guidance.UnitsAreSteps = steps;
            guidance.HasNumericGuidance = true;

            double measured = steps
                ? tiltAdapterOptions.LastMeasuredStepperStepSizeMicrons
                : tiltAdapterOptions.LastMeasuredThreadPitchMicrons;
            if (TiltScrewGeometry.PitchMismatchExceeds(unitMicrons, measured, PitchMismatchFraction)) {
                string label = steps ? "step size" : "thread pitch";
                string units = steps ? "µm/step" : "µm/turn";
                guidance.PitchMismatchWarning =
                    $"Saved {label} ({unitMicrons:0.###} {units}) differs from the wizard's last measured value " +
                    $"({measured:0.###} {units}). Re-run the Tilt Adapter Wizard or update the saved value.";
            }
        }

        // ==================================================================================================
        // Tilt Adapter Automatic Adjustment (T14): computes a move plan from the fitted sensor model, gets
        // user approval via the modal approval dialog, executes it on the connected device with a revert
        // journal, then offers to re-run the inspector to confirm. SAFETY-CRITICAL — see the design doc's
        // "[CRITICAL GATE]" note and user decision #9 (one adjustment per measurement).
        // ==================================================================================================

        // Measurement-generation counter (design doc user decision #9): incremented once per COMPLETED
        // analysis (see AnalyzeAutoFocusResult, the only place this is incremented). lastExecutedMeasurementGeneration
        // is stamped with the generation captured at the start of a plan's execution, but only once execution
        // has actually STARTED (>= 1 move successfully sent) — a dialog Cancel or an approved empty plan never
        // touch it, so they don't consume the measurement.
        private int measurementGeneration;
        private int lastExecutedMeasurementGeneration;

        // Test seams: AnalyzeAutoFocusResult (the only production incrementer of measurementGeneration)
        // requires a full AutoFocusResult that is impractical to construct in a unit test, so tests drive
        // and observe the measurement-generation counters directly here instead of running the whole
        // analysis pipeline.
        internal int MeasurementGenerationForTest {
            get => measurementGeneration;
            set {
                measurementGeneration = value;
                AutomaticAdjustmentCommand?.NotifyCanExecuteChanged();
            }
        }

        internal int LastExecutedMeasurementGenerationForTest => lastExecutedMeasurementGeneration;

        public bool IsTiltDeviceConnected => tiltDeviceConnectionService?.Connected ?? false;

        /// <summary>
        /// [CRITICAL GATE] Visible remediation hint when connected but the calibration is not linked to the
        /// connected device preset (see <see cref="IsCalibrationDeviceLinked"/>) — OR is linked but did not
        /// pass its own confidence/quality check (<see cref="ITiltAdapterOptions.CalibrationIsReliable"/>).
        /// </summary>
        public bool AutomaticAdjustmentRemediationVisible =>
            IsTiltDeviceConnected &&
            (!IsCalibrationDeviceLinked(tiltAdapterOptions) || !(tiltAdapterOptions?.CalibrationIsReliable ?? false));

        public string AutomaticAdjustmentRemediationText =>
            !IsCalibrationDeviceLinked(tiltAdapterOptions)
                ? "This calibration is not linked to the connected device. Re-run calibration with the device connected."
                : "This calibration is low-confidence (it did not pass quality validation). Re-run calibration to enable Automatic Adjustment.";

        /// <summary>
        /// [CRITICAL GATE] True only when the stored calibration was produced by a completed, connected
        /// hands-off run against the SAME device preset that is now selected — see the design doc's
        /// "[CRITICAL GATE]" note. A pre-existing manual/replay calibration could have "screw 1" mapped to a
        /// different physical corner than the connected device's wiring, which would apply corrections
        /// rotated 90°/180° and worsen tilt unattended. Connecting to the device always uses
        /// tiltAdapterOptions.DeviceName as the preset name (TiltAdapterWizardVM.ConnectTiltDeviceAsync), so
        /// DeviceName IS "the connected preset name" whenever the service reports Connected.
        /// </summary>
        internal static bool IsCalibrationDeviceLinked(ITiltAdapterOptions options) {
            if (options == null) return false;
            string linked = options.DeviceLinkedCalibrationDeviceName;
            return !string.IsNullOrEmpty(linked) && linked == options.DeviceName;
        }

        /// <summary>
        /// Pure canExecute gate for <see cref="AutomaticAdjustmentCommand"/> — every condition the design doc
        /// and plan require, expressed as plain values so it's directly unit-testable without the VM. See
        /// <see cref="CanExecuteAutomaticAdjustmentNow"/> for how the live VM/service state feeds this.
        ///
        /// <paramref name="calibrationIsReliable"/> is the second half of the "[CRITICAL GATE]" automation
        /// gate: <see cref="ITiltAdapterOptions.CalibrationIsReliable"/>, persisted by TiltAdapterWizardVM's
        /// RunCalibrationMath/ApplyManualCalibration from TiltCalibrationCalculator.ComputeConfidence's
        /// IsReliable. A calibration can be device-linked (the correct wizard-screw &lt;-&gt; device-corner
        /// correspondence) yet still be noise-dominated (measurement noise rivaling the screw-move signal) —
        /// both must hold before automation may run unattended.
        /// </summary>
        internal static bool CanExecuteAutomaticAdjustment(
            bool serviceConnected,
            bool controllerAvailable,
            bool deviceLinked,
            bool calibrationIsReliable,
            bool hasNumericGuidance,
            bool isOperationActive,
            int currentGeneration,
            int lastExecutedGeneration) {
            return serviceConnected
                && controllerAvailable
                && deviceLinked
                && calibrationIsReliable
                && hasNumericGuidance
                && !isOperationActive
                && currentGeneration > lastExecutedGeneration;
        }

        private bool CanExecuteAutomaticAdjustmentNow() {
            var service = tiltDeviceConnectionService;
            return CanExecuteAutomaticAdjustment(
                serviceConnected: service?.Connected ?? false,
                controllerAvailable: service?.Controller != null,
                deviceLinked: IsCalibrationDeviceLinked(tiltAdapterOptions),
                calibrationIsReliable: tiltAdapterOptions?.CalibrationIsReliable ?? false,
                hasNumericGuidance: TiltGuidance?.HasNumericGuidance ?? false,
                isOperationActive: service?.IsOperationActive ?? false,
                currentGeneration: measurementGeneration,
                lastExecutedGeneration: lastExecutedMeasurementGeneration);
        }

        /// <summary>Dimensionless tilt magnitude sqrt(Gx² + Gy²) — used for the before/after worsening check.</summary>
        internal static double TiltMagnitude(SensorParaboloidModel model) =>
            model == null ? double.NaN : Math.Sqrt(model.Gx * model.Gx + model.Gy * model.Gy);

        // A tiny relative uptick is noise, not a real regression: require BOTH a fractional margin (relative
        // to the "before" magnitude) and an absolute floor (so a near-zero before-magnitude can't make the
        // relative test trivially satisfied by noise) before calling it "worse".
        internal const double TiltWorseningRelativeMargin = 0.15;
        internal const double TiltWorseningAbsoluteFloor = 1e-4;

        /// <summary>True when <paramref name="afterMagnitude"/> is meaningfully larger than <paramref name="beforeMagnitude"/> — see TiltWorseningRelativeMargin/TiltWorseningAbsoluteFloor.</summary>
        internal static bool TiltWorsened(double beforeMagnitude, double afterMagnitude) {
            if (double.IsNaN(beforeMagnitude) || double.IsNaN(afterMagnitude)) return false;
            if (!(afterMagnitude > beforeMagnitude)) return false;
            double delta = afterMagnitude - beforeMagnitude;
            double threshold = Math.Max(TiltWorseningAbsoluteFloor, beforeMagnitude * TiltWorseningRelativeMargin);
            return delta >= threshold;
        }

        /// <summary>
        /// Per-screw signed step/turn targets (TiltScrewTargets.ComputePerScrewTargets's TotalSteps) plus the
        /// resolved unit size, computed directly from the fitted model + adapter options — the exact same
        /// inputs FillNumericGuidance formats for display, so the planner's targets can never diverge from
        /// what the guidance table shows. Throws InvalidOperationException for a not-fully-configured/not-4-screw
        /// adapter (should be unreachable once CanExecuteAutomaticAdjustment has gated on hasNumericGuidance
        /// and the only device-linkable presets are 4-screw EAT presets, but this guards defensively rather
        /// than silently computing against a NaN Screw4AngleDegrees).
        /// </summary>
        internal static (double[] sPerScrew, double unitMicrons) BuildPerScrewTargets(SensorParaboloidModel model, ITiltAdapterOptions options) {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.ScrewCount != 4) {
                throw new InvalidOperationException("Automatic Adjustment requires a 4-corner coupled device (screw count must be 4).");
            }

            bool steps = options.AdjustmentType == TiltAdjustmentType.StepperMotors;
            double unitMicrons = steps ? options.StepperStepSizeMicrons : options.ThreadPitchMicrons;
            double radiusMm = options.ScrewRadiusMillimeters;
            if (unitMicrons <= 0 || radiusMm <= 0) {
                throw new InvalidOperationException("Adapter hardware (unit size / screw radius) is not configured.");
            }

            var angles = new[] { options.Screw1AngleDegrees, options.Screw2AngleDegrees, options.Screw3AngleDegrees, options.Screw4AngleDegrees };
            if (angles.Any(double.IsNaN)) {
                throw new InvalidOperationException("Screw position angles are not fully calibrated.");
            }

            // Resolve the sign exactly like FillNumericGuidance does (σ is never persisted as 0 since the
            // direction-setting feature, but resolve defensively to the assumed default here too) — using
            // the raw options.ScrewInwardCurvatureSign here instead would let the planner's targets diverge
            // from the guidance table the user actually approved if the sign were ever 0.
            int curvatureSign = options.ScrewInwardCurvatureSign;
            int resolvedSign = curvatureSign == 0 ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign : curvatureSign;

            var targets = TiltScrewTargets.ComputePerScrewTargets(
                model.Gx, model.Gy, model.Kx, model.Ky, model.X0, model.Y0,
                angles, radiusMm * 1000.0, unitMicrons, resolvedSign);
            return (targets.Select(t => t.TotalSteps).ToArray(), unitMicrons);
        }

        /// <summary>
        /// Builds one replanner invocation's preview: plans the moves for the given group toggles, then asks
        /// the controller (via the interface — never a downcast) to order them for minimal peak excursion so
        /// the approval dialog shows the EXACT execution order (WYSIWYG). A TiltDeviceLimitException from the
        /// ordering call means either a single move exceeds the per-command cap or every ordering would
        /// exceed the max excursion — surfaced as a blocking preview (unordered moves shown, matching the
        /// design doc) rather than propagated, so the dialog can display it instead of crashing. Residuals,
        /// twist, and the time estimate are unaffected by ordering, so they carry over unchanged from the
        /// original plan.
        /// </summary>
        internal static TiltDevicePlanPreview BuildPlanPreview(
            IReadOnlyList<double> sPerScrew,
            bool includeTilt,
            bool includeBackfocus,
            double unitMicrons,
            int maxStepsPerCommand,
            ITiltMotionController controller) {
            var plan = TiltMovePlanner.Plan(sPerScrew, includeTilt, includeBackfocus, unitMicrons, maxStepsPerCommand);
            try {
                var ordered = controller.OrderForMinimalPeakExcursion(plan.Moves);
                var orderedPlan = new TiltAdapterMovePlan(ordered, plan.ResidualMicronsPerCorner, plan.TwistResidualSteps, plan.EstimatedSeconds);
                return new TiltDevicePlanPreview(orderedPlan, hardLimitViolated: false, limitWarning: string.Empty);
            } catch (TiltDeviceLimitException ex) {
                return new TiltDevicePlanPreview(plan, hardLimitViolated: true, limitWarning: ex.Message);
            }
        }

        private async Task RunAutomaticAdjustmentAsync() {
            // Defensive re-check: canExecute already gates the UI button, but this makes the
            // one-adjustment-per-measurement invariant hold even if this method is somehow invoked directly
            // (e.g. programmatically, or a CanExecute/Execute race) — a second execution off the same
            // measurement must be impossible, not just discouraged by a disabled button.
            if (!CanExecuteAutomaticAdjustmentNow()) {
                Logger.Warning("Automatic Adjustment invoked while its gate conditions were not satisfied; ignoring.");
                return;
            }

            var service = tiltDeviceConnectionService;
            var options = tiltAdapterOptions;
            var controller = service?.Controller;
            if (service == null || options == null || controller == null) {
                return;
            }

            var model = SensorModel?.DisplayedSensorModel;
            if (model == null) {
                Notification.ShowError("No fitted sensor model is available for Automatic Adjustment.");
                return;
            }

            // Captured up front: this is the measurement the plan below is computed from. It is only marked
            // "consumed" (see below) once a move belonging to THIS plan has actually been sent — a later
            // analysis completing while the dialog is open must not let this capture consume a newer
            // measurement it was never computed from.
            int capturedGeneration = measurementGeneration;

            double[] sPerScrew;
            double unitMicrons;
            try {
                (sPerScrew, unitMicrons) = BuildPerScrewTargets(model, options);
            } catch (Exception ex) {
                Logger.Error(ex, "Automatic Adjustment: failed to compute per-screw targets");
                Notification.ShowError($"Could not compute the Automatic Adjustment plan: {ex.Message}");
                return;
            }

            double beforeTiltMagnitude = TiltMagnitude(model);
            int maxStepsPerCommand = options.TiltDeviceMaxStepsPerCommand;

            TiltDevicePlanPreview Replanner(bool includeTilt, bool includeBackfocus) =>
                BuildPlanPreview(sPerScrew, includeTilt, includeBackfocus, unitMicrons, maxStepsPerCommand, controller);

            var choice = await showAdjustmentPromptAsync(
                Replanner,
                options.ScrewInwardCurvatureSignIsMeasured,
                TiltGuidance?.PitchMismatchWarning ?? string.Empty,
                !service.PositionsKnown);

            if (!choice.Proceed) {
                // Cancel (or window close): no moves may be sent, and — per design doc user decision #9 — the
                // measurement is NOT consumed. The button stays enabled for another attempt off this same run.
                return;
            }

            var moves = choice.FinalPlan?.Moves ?? Array.Empty<TiltAdapterMove>();
            if (moves.Count == 0) {
                // Approved but nothing to do (e.g. every residual rounded to 0 steps): a no-op does not
                // consume the measurement either — nothing physical happened.
                Notification.ShowInformation("Automatic Adjustment: no moves were needed for the approved groups.");
                return;
            }

            using var operationToken = service.TryBeginOperation("Automatic Adjustment");
            if (operationToken == null) {
                Notification.ShowWarning("The tilt adapter device is busy with another operation; try Automatic Adjustment again once it finishes.");
                return;
            }

            // Re-check after acquiring the lease: the dialog was open (holding no lease) while the user
            // reviewed it, so the device could have disconnected, or reconnected to a different controller
            // instance, in the meantime.
            if (!service.Connected || !ReferenceEquals(service.Controller, controller)) {
                Notification.ShowError("The tilt adapter device disconnected before Automatic Adjustment could execute; nothing was sent.");
                return;
            }

            // Same re-check window, different hazard: closes a TOCTOU where two Automatic Adjustment
            // invocations both captured this same generation (e.g. two dialogs opened off the same
            // measurement) before either had executed. TryBeginOperation above serializes the two, but only
            // by time, not by generation — if the OTHER invocation already ran to completion and stamped
            // lastExecutedMeasurementGeneration while this one was waiting on the dialog/lease, this
            // invocation must not go on to send a second, redundant/over-correcting plan against the same
            // already-consumed measurement.
            if (lastExecutedMeasurementGeneration >= capturedGeneration) {
                Logger.Warning("Automatic Adjustment: this measurement was already consumed by a concurrent invocation; ignoring.");
                return;
            }

            var journal = new List<TiltAdapterMove>();
            Exception failure = null;
            var moveProgress = new Progress<string>(text => this.progress.Report(new ApplicationStatus { Status = $"Automatic Adjustment: {text}" }));

            for (int i = 0; i < moves.Count; i++) {
                var move = moves[i];
                this.progress.Report(new ApplicationStatus { Status = $"Automatic Adjustment: move {i + 1} of {moves.Count} — {move.Description}" });
                try {
                    await controller.ExecuteMoveAsync(move, moveProgress, CancellationToken.None);
                    journal.Add(move);
                } catch (Exception ex) {
                    failure = ex;
                    break;
                }
            }

            // Consumed once execution has STARTED (>= 1 move sent), regardless of what happens next (success,
            // failure, or a later revert) — the same measurement can never drive a second plan.
            if (journal.Count > 0) {
                lastExecutedMeasurementGeneration = capturedGeneration;
                AutomaticAdjustmentCommand?.NotifyCanExecuteChanged();
            }

            if (failure != null) {
                Logger.Error(failure, "Automatic Adjustment: move execution failed");
                await HandleExecutionFailureAsync(controller, journal, failure);
                return;
            }

            this.progress.Report(new ApplicationStatus { Status = "Automatic Adjustment: complete" });
            Notification.ShowInformation($"Automatic Adjustment complete: {journal.Count} move(s) sent.");

            bool confirmRerun = await confirmPromptAsync(
                "Automatic Adjustment finished sending the approved moves. Re-run the Aberration Inspector to confirm the improvement?",
                "Confirm Adjustment");
            if (!confirmRerun) {
                return;
            }

            bool analyzed = await reRunAnalysisAsync(CancellationToken.None);
            if (!analyzed) {
                Notification.ShowWarning("The confirming Aberration Inspector run did not complete; verify the result manually before adjusting again.");
                return;
            }

            var afterModel = SensorModel?.DisplayedSensorModel;
            double afterTiltMagnitude = TiltMagnitude(afterModel);
            if (TiltWorsened(beforeTiltMagnitude, afterTiltMagnitude)) {
                Notification.ShowError(
                    "Tilt got WORSE after this Automatic Adjustment. This can indicate a stale calibration, a rotated camera/adapter, " +
                    "or an incorrect curvature-sign setting — investigate before adjusting again.");
                bool confirmRevert = await confirmPromptAsync(
                    "Tilt appears WORSE after the moves just applied. Revert them now (send the inverse of each move, in reverse order)?",
                    "Tilt Worsened — Revert?");
                if (confirmRevert) {
                    await RevertJournalAsync(controller, journal, "post-adjustment worsening");

                    // The confirming re-run above incremented measurementGeneration (a new completed
                    // analysis), but lastExecutedMeasurementGeneration is still stamped with the PRE-revert
                    // generation this plan was computed from. Left unbumped, the canExecute gate
                    // (currentGeneration > lastExecutedGeneration) would immediately re-enable the button
                    // against the STALE, now-reverted DisplayedSensorModel — a second plan computed before
                    // the user has looked at fresh (post-revert) numbers could over-correct an already-reverted
                    // device. Consume the confirming measurement so a genuinely NEW analysis is required.
                    lastExecutedMeasurementGeneration = measurementGeneration;
                    AutomaticAdjustmentCommand?.NotifyCanExecuteChanged();
                }
            }
        }

        private async Task HandleExecutionFailureAsync(ITiltMotionController controller, List<TiltAdapterMove> journal, Exception failure) {
            string failureText = DescribeFailure(failure);
            if (journal.Count == 0) {
                if (!FirstMoveFailureMeansDeviceUntouched(failure)) {
                    // Per EatTiltMotionController's exception taxonomy, anything other than
                    // TiltDeviceLimitException on the FIRST move (TiltDeviceCommandFailedException, a
                    // propagated SerialPortClosedException, or something unexpected) means a command WAS
                    // actually sent to the device before the failure — the outcome is ambiguous (an EEPROM
                    // move may have executed) and the shadow was deliberately NOT advanced. The journal being
                    // empty here reflects only that no move was confirmed successful, NOT that nothing
                    // physical happened.
                    Logger.Error(failure, "Automatic Adjustment: first move failed after a command may have been sent; device state is ambiguous.");
                }
                Notification.ShowError(DescribeFirstMoveFailureNotification(failure, failureText));
                return;
            }

            Notification.ShowError($"Automatic Adjustment failed after {journal.Count} of its move(s) were sent: {failureText}");
            bool confirmRevert = await confirmPromptAsync(
                $"Automatic Adjustment failed after {journal.Count} move(s) were sent: {failureText}\n\nRevert the applied move(s) now (send the inverse of each, in reverse order)?",
                "Automatic Adjustment Failed");
            if (confirmRevert) {
                await RevertJournalAsync(controller, journal, "mid-plan failure");
            } else {
                Notification.ShowWarning("The applied move(s) were left in place. Verify the device's position in the vendor app before adjusting again.");
            }
        }

        private async Task RevertJournalAsync(ITiltMotionController controller, List<TiltAdapterMove> journal, string context) {
            for (int i = journal.Count - 1; i >= 0; i--) {
                var inverse = EatWizardMapping.InverseMove(journal[i]);
                try {
                    this.progress.Report(new ApplicationStatus { Status = $"Automatic Adjustment: reverting move {journal.Count - i} of {journal.Count} — {inverse.Description}" });
                    await controller.ExecuteMoveAsync(inverse, null, CancellationToken.None);
                } catch (Exception ex) {
                    // Revert itself failed partway: the device's tracked position can no longer be trusted.
                    // There is no interface member to force-invalidate a controller's shadow position, so the
                    // actionable recovery path is the one the controller already implements: ConnectAsync
                    // always re-queries the device's absolute positions and overwrites the shadow with ground
                    // truth on a successful parse. Tell the user to use it.
                    Logger.Error(ex, $"Automatic Adjustment revert ({context}) failed partway through; device position may be inaccurate.");
                    Notification.ShowError(
                        $"Revert failed: {ex.Message}. The device's tracked position may now be inaccurate — " +
                        "disconnect and reconnect the tilt adapter device to resync its position from the hardware before doing anything else.");
                    return;
                }
            }
            Notification.ShowWarning($"Automatic Adjustment: {journal.Count} move(s) were reverted ({context}).");
        }

        private static string DescribeFailure(Exception failure) {
            switch (failure) {
                case OperationCanceledException _:
                    return "the operation was cancelled";
                case TiltDeviceLimitException limitEx:
                    return $"a device travel limit was violated ({limitEx.Message})";
                case TiltDeviceCommandFailedException cmdEx:
                    return cmdEx.Message;
                case SerialPortClosedException portEx:
                    return $"the serial port closed unexpectedly ({portEx.Message})";
                default:
                    return failure.Message;
            }
        }

        /// <summary>
        /// True only for <see cref="TiltDeviceLimitException"/> -- per EatTiltMotionController's exception
        /// taxonomy (see its <c>ExecuteMoveAsync</c> XML doc's "Exception taxonomy" section), this is the ONLY
        /// exception thrown strictly BEFORE any device I/O, so a limit violation on the very first move
        /// guarantees the device was never touched. Any other exception (<see cref="TiltDeviceCommandFailedException"/>,
        /// a propagated <see cref="SerialPortClosedException"/>, or something unexpected) means a command may
        /// already have been sent, even though the journal is empty (nothing was CONFIRMED successful).
        /// </summary>
        internal static bool FirstMoveFailureMeansDeviceUntouched(Exception failure) => failure is TiltDeviceLimitException;

        /// <summary>
        /// The notification text for a failure on the FIRST move of a plan (empty journal) -- see
        /// <see cref="FirstMoveFailureMeansDeviceUntouched"/> for which branch applies. Extracted as a pure
        /// helper (mirrors <see cref="DescribeFailure"/>) specifically so the two outcomes are directly
        /// unit-testable without a live VM/Notification pipeline (Notification is a no-op in headless tests).
        /// </summary>
        internal static string DescribeFirstMoveFailureNotification(Exception failure, string failureText) {
            if (FirstMoveFailureMeansDeviceUntouched(failure)) {
                return $"Automatic Adjustment failed before any move was sent: {failureText}. Nothing was sent to the device.";
            }
            return $"Automatic Adjustment failed on its first move: {failureText}\n\nA command may have already been sent to the device, so its position is now UNCERTAIN — " +
                "verify the device's position in the vendor app before doing anything else, or disconnect and reconnect the tilt adapter device to force a fresh resync of its position from the hardware.";
        }

        // Production Yes/No confirmation: NINA's message box (mirrors TiltAdapterWizardVM.ShowIdleDisconnectPromptAsync).
        // Default answer is No for every caller of confirmPromptAsync in this file — never proceed with an
        // irreversible hardware action (re-run, revert) just because a dialog was dismissed.
        private static Task<bool> ShowYesNoPromptAsync(string message, string title) {
            var result = MyMessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxResult.No);
            return Task.FromResult(result == MessageBoxResult.Yes);
        }

        // Production adjustment-approval dialog: the real modal (TiltDeviceAdjustmentPrompt.ShowAsync), using
        // this VM's own windowServiceFactory. See showAdjustmentPromptAsync's field doc for why this is
        // injectable at all (unit tests cannot open a real WPF window).
        private Task<TiltDeviceAdjustmentChoice> DefaultShowAdjustmentPromptAsync(
            Func<bool, bool, TiltDevicePlanPreview> replanner,
            bool screwInwardCurvatureSignIsMeasured,
            string pitchMismatchWarning,
            bool positionsUnknown) {
            return TiltDeviceAdjustmentPrompt.ShowAsync(windowServiceFactory, replanner, screwInwardCurvatureSignIsMeasured, pitchMismatchWarning, positionsUnknown);
        }

        private void TiltDeviceConnectionService_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            // The service raises INPC from its polling/idle timer threads; marshal without blocking them
            // (mirrors TiltAdapterWizardVM.TiltDeviceConnectionService_PropertyChanged).
            applicationDispatcher.PostSynchronizationContext(() => {
                if (e.PropertyName == nameof(TiltDeviceConnectionService.Connected) ||
                    e.PropertyName == nameof(TiltDeviceConnectionService.Controller)) {
                    RaisePropertyChanged(nameof(IsTiltDeviceConnected));
                    RaisePropertyChanged(nameof(AutomaticAdjustmentRemediationVisible));
                    AutomaticAdjustmentCommand?.NotifyCanExecuteChanged();
                }
                if (e.PropertyName == nameof(TiltDeviceConnectionService.IsOperationActive)) {
                    AutomaticAdjustmentCommand?.NotifyCanExecuteChanged();
                }
            });
        }

        private TrendlineFitting GetLineFitting(AutoFocusFitting fitting) {
            if (fitting.Method == AFMethodEnum.STARHFR) {
                if (fitting.CurveFittingType == AFCurveFittingEnum.TRENDPARABOLIC || fitting.CurveFittingType == AFCurveFittingEnum.TRENDHYPERBOLIC || fitting.CurveFittingType == AFCurveFittingEnum.TRENDLINES) {
                    return fitting.TrendlineFitting;
                }
            }
            return null;
        }

        private Func<double, double> GetCurveFitting(AutoFocusFitting fitting) {
            if (fitting.Method == AFMethodEnum.CONTRASTDETECTION) {
                return fitting.GaussianFitting?.Fitting ?? null;
            } else if (fitting.CurveFittingType == AFCurveFittingEnum.PARABOLIC || fitting.CurveFittingType == AFCurveFittingEnum.TRENDPARABOLIC) {
                return fitting.QuadraticFitting?.Fitting ?? null;
            } else if (fitting.CurveFittingType == AFCurveFittingEnum.HYPERBOLIC || fitting.CurveFittingType == AFCurveFittingEnum.TRENDHYPERBOLIC) {
                return fitting.HyperbolicFitting?.Fitting ?? null;
            } else {
                return null;
            }
        }

        public void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
            var plotControl = (WpfPlot)sender;
            var plotName = plotControl.Name;
            if (eccentricityCenterPoints == null) {
                return;
            }

            (double mouseCoordX, double mouseCoordY) = plotControl.GetMouseCoordinates();
            double xyRatio = plotControl.Plot.XAxis.Dims.PxPerUnit / plotControl.Plot.YAxis.Dims.PxPerUnit;
            (double pointX, double pointY, int pointIndex) = eccentricityCenterPoints.GetPointNearest(mouseCoordX, mouseCoordY, xyRatio);

            // place the highlight over the point of interest
            highlightedEccentricityPoint.X = pointX;
            highlightedEccentricityPoint.Y = pointY;
            highlightedEccentricityPoint.IsVisible = true;
            highlightedEccentricityPoint.Text = $"{eccentricityValues[pointIndex]:0.00}, {rotationValues[pointIndex]:0.}°";

            // render if the highlighted point changed
            if (lastHighlightedEccentricityPointIndex != pointIndex) {
                lastHighlightedEccentricityPointIndex = pointIndex;
                RefreshScottPlot(plotName);
            }
        }

        public void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            var plotControl = (WpfPlot)sender;
            var plotName = plotControl.Name;
            if (highlightedEccentricityPoint == null) {
                return;
            }

            lastHighlightedEccentricityPointIndex = -1;
            highlightedEccentricityPoint.IsVisible = false;
            RefreshScottPlot(plotName);
        }

        private void RefreshScottPlot(string plotName) {
            // TODO: This should be an event that takes the plot name
            this.PlotRefreshed?.Invoke(this, new EventArgs());
        }

        private CameraInfo cameraInfo = DeviceInfo.CreateDefaultInstance<CameraInfo>();

        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                this.cameraInfo = value;
                RaisePropertyChanged();
                RefreshCommandStates();
            }
        }

        private TelescopeInfo telescopeInfo = DeviceInfo.CreateDefaultInstance<TelescopeInfo>();

        public TelescopeInfo TelescopeInfo {
            get => telescopeInfo;
            private set {
                this.telescopeInfo = value;
                RaisePropertyChanged();
                RefreshCommandStates();
            }
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            CameraInfo = deviceInfo;
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            TelescopeInfo = deviceInfo;
        }

        public void Dispose() {
            // Do nothing
        }

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            // Do nothing
        }

        public void UpdateUserFocused(FocuserInfo info) {
            // Do nothing
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            FocuserInfo = deviceInfo;
        }

        private FocuserInfo focuserInfo = DeviceInfo.CreateDefaultInstance<FocuserInfo>();

        public FocuserInfo FocuserInfo {
            get => focuserInfo;
            private set {
                this.focuserInfo = value;
                RaisePropertyChanged();
                RefreshCommandStates();
            }
        }

        private bool autoFocusAnalysisProgressOrResult;

        public bool AutoFocusAnalysisProgressOrResult {
            get => autoFocusAnalysisProgressOrResult;
            private set {
                autoFocusAnalysisProgressOrResult = value;
                RaisePropertyChanged();
            }
        }

        private bool autoFocusAnalysisResult;

        public bool AutoFocusAnalysisResult {
            get => autoFocusAnalysisResult;
            private set {
                autoFocusAnalysisResult = value;
                RaisePropertyChanged();
            }
        }

        private bool exposureAnalysisResult;

        public bool ExposureAnalysisResult {
            get => exposureAnalysisResult;
            private set {
                exposureAnalysisResult = value;
                RaisePropertyChanged();
            }
        }

        public event EventHandler PlotRefreshed;

        private SPPlot eccentricityVectorPlot;

        public SPPlot EccentricityVectorPlot {
            get => eccentricityVectorPlot;
            set {
                eccentricityVectorPlot = value;
                RaisePropertyChanged();
            }
        }

        public bool LoopingExposureAnalysis {
            get => inspectorOptions.LoopingExposureAnalysisEnabled;
            set {
                inspectorOptions.LoopingExposureAnalysisEnabled = value;
                RaisePropertyChanged();
            }
        }

        private bool sensorCurveModelActive = false;

        public bool SensorCurveModelActive {
            get => sensorCurveModelActive;
            set {
                if (sensorCurveModelActive != value) {
                    this.sensorCurveModelActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool tiltMeasurementActive = false;

        public bool TiltMeasurementActive {
            get => tiltMeasurementActive;
            set {
                if (tiltMeasurementActive != value) {
                    this.tiltMeasurementActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool tiltMeasurementHistoryActive = false;

        public bool TiltMeasurementHistoryActive {
            get => tiltMeasurementHistoryActive;
            set {
                if (tiltMeasurementHistoryActive != value) {
                    this.tiltMeasurementHistoryActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool autoFocusChartActive = false;

        public bool AutoFocusChartActive {
            get => autoFocusChartActive;
            set {
                if (autoFocusChartActive != value) {
                    this.autoFocusChartActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool fwhmContoursActive = false;

        public bool FWHMContoursActive {
            get => fwhmContoursActive;
            set {
                if (fwhmContoursActive != value) {
                    this.fwhmContoursActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool eccentricityVectorsActive = false;

        public bool EccentricityVectorsActive {
            get => eccentricityVectorsActive;
            set {
                if (eccentricityVectorsActive != value) {
                    this.eccentricityVectorsActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool autoFocusChartActivatedOnce;

        public bool AutoFocusChartActivatedOnce {
            get => autoFocusChartActivatedOnce;
            set {
                if (autoFocusChartActivatedOnce != value) {
                    this.autoFocusChartActivatedOnce = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool tiltMeasurementActivatedOnce;

        public bool TiltMeasurementActivatedOnce {
            get => tiltMeasurementActivatedOnce;
            set {
                if (tiltMeasurementActivatedOnce != value) {
                    this.tiltMeasurementActivatedOnce = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool exposureAnalysisActivatedOnce;

        public bool ExposureAnalysisActivatedOnce {
            get => exposureAnalysisActivatedOnce;
            set {
                if (exposureAnalysisActivatedOnce != value) {
                    this.exposureAnalysisActivatedOnce = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool autoFocusCompleted;

        public bool AutoFocusCompleted {
            get => autoFocusCompleted;
            set {
                if (autoFocusCompleted != value) {
                    this.autoFocusCompleted = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool sensorModel3DEnabled = true;

        public bool SensorModel3DEnabled {
            get => sensorModel3DEnabled;
            set {
                if (sensorModel3DEnabled != value) {
                    this.sensorModel3DEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string inspectorErrorText = string.Empty;

        public string InspectorErrorText {
            get => inspectorErrorText;
            set {
                if (inspectorErrorText != value) {
                    this.inspectorErrorText = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string simpleAnalysisErrorText = string.Empty;

        public string SimpleAnalysisErrorText {
            get => simpleAnalysisErrorText;
            set {
                if (simpleAnalysisErrorText != value) {
                    this.simpleAnalysisErrorText = value;
                    RaisePropertyChanged();
                }
            }
        }

        private void ClearAnalyses() {
            DeactivateAutoFocusAnalysis();
            ResetExposureAnalysis();
            AutoFocusChartActivatedOnce = false;
            TiltMeasurementActivatedOnce = false;
            TiltModel.Reset();
            SensorModel.Clear();
            AutoFocusCompleted = false;
            ResetErrors();
            RebuildTiltGuidance();
            ClearReviewSnapshot();
        }

        private void ActivateAutoFocusChart() {
            AutoFocusChartActive = true;
            AutoFocusChartActivatedOnce = true;
        }

        private void ActivateTiltMeasurement() {
            SensorCurveModelActive = true;
            TiltMeasurementActive = true;
            TiltMeasurementHistoryActive = true;
            TiltMeasurementActivatedOnce = true;
        }

        private void ActivateExposureAnalysis() {
            // Only surface the FWHM contour map and eccentricity vectors when the analyzed validation image
            // produced stars with a fitted PSF. Both plots skip stars whose PSF could not be modeled
            // (FWHMContourControl and the eccentricity vector field both ignore PSF == null), so a result
            // with detections but no usable PSFs would render empty panels. Treat that as "no final
            // validation data" and stay hidden (the expanders' visibility is bound to
            // ExposureAnalysisActivatedOnce).
            var hasValidationData = SnapshotAnalysisStarDetectionResult?.StarList?
                .OfType<HocusFocusDetectedStar>().Any(s => s.PSF != null) ?? false;
            FWHMContoursActive = hasValidationData;
            EccentricityVectorsActive = hasValidationData;
            ExposureAnalysisActivatedOnce = hasValidationData;
            if (!hasValidationData) {
                SimpleAnalysisErrorText = "Cannot display FWHM Contour and Eccentricity Vectors.\nThe final validation image produced no usable star measurements (no fitted PSFs).";
                Logger.Warning("Final validation image produced no PSF-modeled stars; hiding FWHM contour and eccentricity vectors.");
            }
        }

        private void DeactivateAutoFocusAnalysis() {
            AutoFocusChartActive = false;
            SensorCurveModelActive = false;
            TiltMeasurementActive = false;
            TiltMeasurementHistoryActive = false;
        }

        private void DeactivateExposureAnalysis() {
            FWHMContoursActive = false;
            EccentricityVectorsActive = false;
        }

        private void ResetErrors() {
            InspectorErrorText = string.Empty;
            SimpleAnalysisErrorText = string.Empty;
        }

        private void ResetExposureAnalysis() {
            DeactivateExposureAnalysis();
            ExposureAnalysisActivatedOnce = false;
        }

        private ScottPlot.Plottable.MarkerPlot highlightedEccentricityPoint;
        private ScottPlot.Plottable.ScatterPlot eccentricityCenterPoints;
        private int lastHighlightedEccentricityPointIndex;
        private double[] eccentricityValues;
        private double[] rotationValues;
    }
}
