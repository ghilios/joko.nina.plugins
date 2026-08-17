#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.Properties;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Plugin;
using MyMessageBox = NINA.Core.MyMessageBox.MyMessageBox;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Core.Interfaces;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using System.Reflection;
using System.IO;
using System;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System.Threading.Tasks;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.Core.Model;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;

namespace NINA.Joko.Plugins.HocusFocus {

    [Export(typeof(IPluginManifest))]
    public class HocusFocusPlugin : PluginBase {

        // Collaborators captured for the on-demand Star Detection Optimization Wizard (T5). The wizard is created
        // lazily when the user clicks "Optimize Star Detection…", well after MEF composition, so these are safe to
        // reuse for building its RunEvaluationLoader + detector.
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageDataFactory imageDataFactory;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;

        // WindowServiceFactory is not a MEF export (NINA exposes the concrete type with a default ctor only), so it
        // is instantiated directly here, mirroring RunAberrationInspector.
        private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();

        static HocusFocusPlugin() {
            // NINA does not ship ScottPlot, so the plugin's bundled ScottPlot(.WPF) DLLs live only in the plugin
            // folder — which is not on the default assembly probing path. WPF/BAML loads a view's xmlns assemblies on
            // demand, and without this resolver that load fails (FileNotFoundException -> XamlParseException), so views
            // with ScottPlot charts (e.g. the Star Detection Optimization Wizard) fail to render — the window just
            // flashes. Registering here (static ctor: runs the moment the plugin type is first touched at MEF
            // discovery, before any view is shown) makes those bundled-dependency loads resolve from the plugin folder.
            BundledAssemblyResolver.Register(Path.GetDirectoryName(Assembly.GetAssembly(typeof(HocusFocusPlugin))?.Location));
        }

        [ImportingConstructor]
        public HocusFocusPlugin(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IFilterWheelMediator filterWheelMediator,
            IFocuserMediator focuserMediator,
            IGuiderMediator guiderMediator,
            IImagingMediator imagingMediator,
            IImageDataFactory imageDataFactory,
            IImageSaveMediator imageSaveMediator,
            IOptionsVM options,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;
            this.imagingMediator = imagingMediator;
            this.imageDataFactory = imageDataFactory;
            this.starDetectionSelector = starDetectionSelector;
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                Settings.Default.Save();
            }

            if (InFocusHfr == null) {
                // The measured in-focus HFR that drives the detection-binning recommendation. MUST be constructed
                // before StarDetectionOptions, which subscribes to it for the recommendation refresh.
                InFocusHfr = new InFocusHfrRecord(profileService);
            }
            if (StarDetectionOptions == null) {
                StarDetectionOptions = new StarDetectionOptions(profileService, InFocusHfr);
            }
            if (StarAnnotatorOptions == null) {
                StarAnnotatorOptions = new StarAnnotatorOptions(profileService);
            }
            if (AutoFocusOptions == null) {
                AutoFocusOptions = new AutoFocusOptions(profileService);
            }
            if (InspectorOptions == null) {
                InspectorOptions = new InspectorOptions(profileService);
            }
            if (TiltAdapterOptions == null) {
                TiltAdapterOptions = new TiltAdapterOptions(profileService);
            }
            if (CameraSimulatorOptions == null) {
                // Takes InspectorOptions: the simulator's FocuserStepSizeMicrons is a pass-through onto the
                // Inspector's MicronsPerFocuserStep, not a copy. Constructed above, which this relies on — both for
                // the non-null argument here and for ProfileChanged ordering (see MigrateLegacyFocuserStepSize).
                CameraSimulatorOptions = new CameraSimulatorOptions(profileService, InspectorOptions);
            }
            var thisAssembly = Assembly.GetAssembly(typeof(HocusFocusPlugin));
            var thisAssemblyFileInfo = new FileInfo(thisAssembly.Location);
            if (ApplicationDispatcher == null) {
                // Constructed here rather than further down: PerFilterStarDetectionEditBinder below takes it, to
                // marshal store-driven snapshot reloads (which a detection thread can trigger) onto the UI thread.
                ApplicationDispatcher = new ApplicationDispatcher();

                var archFolder = Environment.Is64BitProcess ? "x64" : "x86";
                var dllPath = Path.Combine(thisAssemblyFileInfo.Directory.FullName, "dll", archFolder);
                OpenCvSharp.Internal.WindowsLibraryLoader.Instance.AdditionalPaths.Add(dllPath);
            }
            // Wired after ApplicationDispatcher exists. The options object raises the conflict; the dialog lives
            // here so the options stay UI-free (and silent in tests/headless tooling, where the handler is null).
            // Posted rather than dispatched synchronously: this fires from inside a property setter driven by a
            // WPF binding, so the binding must complete before a modal dialog takes over the thread.
            StarDetectionOptions.AutoFocusBinningConflictHandler = (conflict, detectionBinning) => {
                ApplicationDispatcher.PostSynchronizationContext(() => {
                    var answer = MyMessageBox.Show(
                        conflict.Describe(detectionBinning),
                        "Auto Focus Binning",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxResult.No);
                    if (answer == System.Windows.MessageBoxResult.Yes) {
                        conflict.ResetToUnbinned(profileService);
                        Notification.ShowInformation("Auto Focus Binning set back to 1x1. Hocus Focus detection binning is unchanged.");
                    }
                });
            };

            if (PerFilterStarDetection == null) {
                // Constructed after the options singletons: ProfileChanged handlers run in subscription order,
                // so the store (and the binder below) re-read a new profile only after StarDetectionOptions has
                // re-read the legacy keys.
                PerFilterStarDetection = new PerFilterStarDetectionStore(
                    profileService,
                    () => StarDetectionSettingsSnapshot.FromOptions(StarDetectionOptions));
            }
            if (PerFilterStarDetectionEditBinder == null) {
                PerFilterStarDetectionEditBinder = new PerFilterEditBinder(
                    PerFilterStarDetection,
                    StarDetectionOptions,
                    profileService,
                    () => filterWheelMediator.GetInfo()?.SelectedFilter?.Name,
                    ApplicationDispatcher,
                    // Connectivity is passed separately from the filter name: the name alone cannot tell a
                    // disconnected wheel from a connected one mid-move, and the options-page warning needs to say
                    // different things about those two states.
                    () => filterWheelMediator.GetInfo()?.Connected == true);
                // The warning is computed, so it only reaches the UI when something raises PropertyChanged for it.
                // Held in a static so the registration lives as long as the binder it drives.
                ActiveFilterWheelWatcher = new ActiveFilterWheelWatcher(
                    filterWheelMediator, () => PerFilterStarDetectionEditBinder.RefreshActiveFilter());
            }
            // Must follow CameraSimulatorOptions: the VM reads it and subscribes to its PropertyChanged.
            SimTiltAdapterVM = new SimulatedTiltAdapterVM(CameraSimulatorOptions);
            if (TiltDeviceConnectionService == null) {
                // The "Simulator" port connects the EAT automation to the camera simulator instead of real
                // hardware, so the whole calibration/adjustment loop can run with nothing plugged in. A single
                // shared actuator (created lazily on first Connect) keeps the per-motor counters and injected
                // aberration coherent across reconnects; the closure runs only at Connect time, by which point
                // the CameraSimulatorOptions and ApplicationDispatcher statics are both set.
                SimulatedTiltActuator sharedSimActuator = null;
                TiltDeviceConnectionService = new TiltDeviceConnectionService(
                    profileService, TiltAdapterOptions,
                    simulatedControllerFactory: () => {
                        sharedSimActuator ??= new SimulatedTiltActuator(CameraSimulatorOptions, ApplicationDispatcher);
                        return new EatTiltMotionController(new SimulatedEatTransport(sharedSimActuator), TiltAdapterOptions);
                    });
            }
            if (TiltDeviceIdleCountdown == null) {
                TiltDeviceIdleCountdown = new TiltDeviceIdleCountdownVM(TiltDeviceConnectionService, ApplicationDispatcher);
            }
            if (AlglibAPI == null) {
                AlglibAPI = new AlglibAPI();
            }
            if (AutoFocusEngineFactory == null) {
                AutoFocusEngineFactory = new AutoFocusEngineFactory(
                    profileService,
                    cameraMediator,
                    filterWheelMediator,
                    focuserMediator,
                    guiderMediator,
                    imagingMediator,
                    imageDataFactory,
                    starDetectionSelector,
                    starAnnotatorSelector,
                    AutoFocusOptions,
                    StarAnnotatorOptions,
                    AlglibAPI,
                    // Lets the engine resolve a per-filter sweep-geometry override for the filter a run will
                    // actually expose through. Constructed above, so it is non-null by the time this runs.
                    PerFilterStarDetection);
            }

            options.AddImagePattern(fwhmImagePattern);
            options.AddImagePattern(eccentricityImagePattern);
            imageSaveMediator.BeforeFinalizeImageSaved += ImageSaveMediator_BeforeFinalizeImageSaved;
            ResetStarDetectionDefaultsCommand = new RelayCommand(StarDetectionOptions.ResetDefaults);
            ResetStarAnnotatorDefaultsCommand = new RelayCommand(StarAnnotatorOptions.ResetDefaults);
            ResetAutoFocusDefaultsCommand = new RelayCommand(AutoFocusOptions.ResetDefaults);
            ResetCameraSimulatorDefaultsCommand = new RelayCommand(CameraSimulatorOptions.ResetDefaults);
            ChooseIntermediatePathDiagCommand = new RelayCommand(ChooseIntermediatePathDiag);
            ChooseSavePathDiagCommand = new RelayCommand(ChooseSavePathDiag);
            ChooseAstapPathDiagCommand = new RelayCommand(ChooseAstapPathDiag);
            OptimizeStarDetectionCommand = new RelayCommand(OptimizeStarDetection);
            LaunchStarDetectionOptimizer = OptimizeStarDetection;
            ExportStarDetectionSettingsCommand = new RelayCommand(() => StarDetectionSettingsIO.Export(StarDetectionOptions));
            ImportStarDetectionSettingsCommand = new AsyncRelayCommand(() => StarDetectionSettingsIO.ImportAsync(StarDetectionOptions, windowServiceFactory));
            // No canExecute predicate: CommunityToolkit commands do not requery on CommandManager.RequerySuggested,
            // and ButtonBase does not requery when CommandParameter changes, so a predicate here would latch the
            // button disabled forever. The button's enablement is driven reactively by PerFilterEditBinder
            // .CanCopyFromFilter via an IsEnabled binding instead.
            CopyStarDetectionFromFilterCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<string>(CopyStarDetectionFromFilter);
            LaunchCopyStarDetectionFromFilter = CopyStarDetectionFromFilter;
        }

        /// <summary>
        /// Builds the Star Detection Optimization Wizard with its real collaborators and shows it as a modal dialog.
        /// The window resolves the wizard's content from the keyed DataTemplate (exported as a ResourceDictionary by
        /// StarDetection/Optimization/DataTemplates.xaml). The VM is disposed when the window closes.
        /// </summary>
        private void OptimizeStarDetection() {
            var autoFocusEngine = AutoFocusEngineFactory.Create();

            // Reuse the MEF-composed HocusFocus detector when present (no new manifest import needed). Fall back to a
            // fresh instance — the loader's detection path (GetStarDetectorParams + Detect) does not use
            // ImageStatisticsVM, so a null is safe here.
            var detection = starDetectionSelector?.Behaviors?.OfType<IHocusFocusStarDetection>().FirstOrDefault()
                ?? new HocusFocusStarDetection(
                    null,
                    profileService,
                    focuserMediator,
                    StarDetectionOptions,
                    AlglibAPI,
                    PerFilterStarDetection);

            var vm = new StarDetectionOptimizerWizardVM(
                profileService,
                imageDataFactory,
                imagingMediator,
                cameraMediator,
                filterWheelMediator,
                focuserMediator,
                autoFocusEngine,
                detection);

            var windowService = windowServiceFactory.Create();

            // The VM's Close button asks the host to dismiss the dialog.
            void onRequestClose(object s, EventArgs e) {
                _ = windowService.Close();
            }

            // Tear down the VM (cancellation-token source) once the window is dismissed. OnClosed fires whether the
            // user closes the window or the VM's Close command closes it.
            EventHandler onClosed = null;
            onClosed = (s, e) => {
                windowService.OnClosed -= onClosed;
                vm.RequestClose -= onRequestClose;
                vm.Dispose();
            };
            windowService.OnClosed += onClosed;
            vm.RequestClose += onRequestClose;

            // NINA's WindowService marshals window creation onto the application dispatcher internally; this is the
            // standard way NINA plugins show a modal dialog (the VM is presented in a ContentPresenter and its visual
            // is resolved by the implicit DataType DataTemplate for StarDetectionOptimizerWizardVM).
            windowService.ShowDialog(vm, "Optimize Star Detection", ResizeMode.CanResize, WindowStyle.SingleBorderWindow);
        }

        private async Task CopyStarDetectionFromFilter(string sourceFilterName) {
            try {
                await StarDetectionSettingsIO.CopyFromFilterAsync(sourceFilterName, PerFilterStarDetection, StarDetectionOptions, windowServiceFactory, PerFilterStarDetectionEditBinder);
            } finally {
                // Reset the "Copy Settings From" dropdown to no selection once the flow finishes (applied or not), so
                // the Copy button disables again and the next copy is a deliberate re-selection. Shared binder, so
                // this clears the dropdown on whichever host raised the command.
                if (PerFilterStarDetectionEditBinder != null) {
                    PerFilterStarDetectionEditBinder.CopySourceFilterName = null;
                }
            }
        }

        private void ChooseIntermediatePathDiag() {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                dialog.SelectedPath = StarDetectionOptions.IntermediateSavePath;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    StarDetectionOptions.IntermediateSavePath = dialog.SelectedPath;
                }
            }
        }

        private void ChooseSavePathDiag() {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                dialog.SelectedPath = AutoFocusOptions.SavePath;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    AutoFocusOptions.SavePath = dialog.SelectedPath;
                }
            }
        }

        private void ChooseAstapPathDiag() {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                dialog.SelectedPath = CameraSimulatorOptions.AstapCatalogPath;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    CameraSimulatorOptions.AstapCatalogPath = dialog.SelectedPath;
                }
            }
        }

        private Task ImageSaveMediator_BeforeFinalizeImageSaved(object sender, BeforeFinalizeImageSavedEventArgs e) {
            var hfAnalysis = (e.Image?.RawImageData?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis);
            if (hfAnalysis != null) {
                e.AddImagePattern(new ImagePattern(fwhmImagePattern.Key, fwhmImagePattern.Description, fwhmImagePattern.Category) {
                    Value = $"{hfAnalysis.FWHM:0.00}"
                });
                e.AddImagePattern(new ImagePattern(eccentricityImagePattern.Key, eccentricityImagePattern.Description, eccentricityImagePattern.Category) {
                    Value = $"{hfAnalysis.Eccentricity:0.00}"
                });
            }
            return Task.CompletedTask;
        }

        private readonly ImagePattern fwhmImagePattern = new ImagePattern("$$FWHM$$", "Full Width Half Maximum", "Hocus Focus") { Value = "4.23" };

        private readonly ImagePattern eccentricityImagePattern = new ImagePattern("$$ECCENTRICITY$$", "Eccentricity", "Hocus Focus") { Value = "0.66" };

        public static StarDetectionOptions StarDetectionOptions { get; private set; }

        public static StarAnnotatorOptions StarAnnotatorOptions { get; private set; }

        public static AutoFocusOptions AutoFocusOptions { get; private set; }

        public static InspectorOptions InspectorOptions { get; private set; }

        public static TiltAdapterOptions TiltAdapterOptions { get; private set; }

        public static CameraSimulatorOptions CameraSimulatorOptions { get; private set; }

        public static PerFilterStarDetectionStore PerFilterStarDetection { get; private set; }

        public static PerFilterEditBinder PerFilterStarDetectionEditBinder { get; private set; }

        /// <summary>
        /// Keeps <see cref="PerFilterEditBinder.ActiveFilterWarning"/> live: the binder cannot observe the filter
        /// wheel itself (it takes plain delegates, not mediators, so it stays testable without equipment), so this
        /// consumer re-raises the warning whenever the wheel connects, disconnects, or changes filter.
        /// </summary>
        public static ActiveFilterWheelWatcher ActiveFilterWheelWatcher { get; private set; }

        /// <summary>
        /// Backs the simulated tilt adapter's configuration on the Camera Simulator options page, whose DataContext
        /// is this plugin. An instance property, unlike the options singletons above: those predate this and are
        /// static so non-VM code can reach them, whereas nothing but the options page needs this one.
        /// </summary>
        /// <remarks>
        /// The Imaging dockable builds its own <see cref="SimulatedTiltAdapterVM"/> over the same
        /// <see cref="CameraSimulatorOptions"/> singleton, and each VM subscribes to that singleton's
        /// PropertyChanged, so the two views stay in lockstep without talking to each other. A second instance is
        /// therefore the established design here, not duplicated state — the injected plane and the adapter
        /// geometry live in the options, never in the VM.
        /// </remarks>
        public SimulatedTiltAdapterVM SimTiltAdapterVM { get; private set; }

        public static TiltDeviceConnectionService TiltDeviceConnectionService { get; private set; }

        /// <summary>
        /// One shared idle-countdown banner VM, so every panel that shows tilt-device state renders the same
        /// countdown from one source of truth — whichever panel the user happens to be looking at.
        /// </summary>
        public static TiltDeviceIdleCountdownVM TiltDeviceIdleCountdown { get; private set; }

        public static AutoFocusEngineFactory AutoFocusEngineFactory { get; private set; }

        public static ApplicationDispatcher ApplicationDispatcher { get; private set; }

        /// <summary>The last measured in-focus HFR for the active profile. Written when an auto-focus run finishes;
        /// read by the detection-binning recommendation on the Star Detector options page and in the wizard.</summary>
        public static InFocusHfrRecord InFocusHfr { get; private set; }

        public static IAlglibAPI AlglibAPI { get; private set; }

        /// <summary>
        /// Shared launcher for the Star Detection Optimization Wizard. Set by the plugin constructor so other
        /// ViewModels (e.g. the Imaging-pane StarDetectionOptionsVM) can open the same wizard without re-wiring
        /// the wizard's dependencies. May be null when no plugin instance has been constructed (e.g. unit tests).
        /// </summary>
        public static Action LaunchStarDetectionOptimizer { get; private set; }

        /// <summary>
        /// Shared launcher for the per-filter "Copy Settings From" flow, mirroring
        /// <see cref="LaunchStarDetectionOptimizer"/>: set by the plugin constructor so the Imaging-pane
        /// StarDetectionOptionsVM can run the same copy dialog without re-wiring its dependencies. May be null
        /// when no plugin instance has been constructed (e.g. unit tests).
        /// </summary>
        public static Func<string, Task> LaunchCopyStarDetectionFromFilter { get; private set; }

        // Instance wrappers over the per-filter statics: the shared HocusFocus_StarDetection_Options template
        // binds DataContext-relative paths (PerFilterStore.* / PerFilterEditBinder.*), and a WPF Binding Path
        // cannot resolve static properties, so both hosts expose the same instance property names.
        public IPerFilterStarDetectionStore PerFilterStore => PerFilterStarDetection;

        public PerFilterEditBinder PerFilterEditBinder => PerFilterStarDetectionEditBinder;

        public ICommand ResetStarDetectionDefaultsCommand { get; private set; }

        public ICommand ResetStarAnnotatorDefaultsCommand { get; private set; }

        public ICommand ResetAutoFocusDefaultsCommand { get; private set; }

        public ICommand ResetCameraSimulatorDefaultsCommand { get; private set; }

        public ICommand ChooseIntermediatePathDiagCommand { get; private set; }

        public ICommand ChooseSavePathDiagCommand { get; private set; }

        public ICommand ChooseAstapPathDiagCommand { get; private set; }

        public ICommand OptimizeStarDetectionCommand { get; private set; }

        public ICommand ExportStarDetectionSettingsCommand { get; private set; }

        public ICommand ImportStarDetectionSettingsCommand { get; private set; }

        public ICommand CopyStarDetectionFromFilterCommand { get; private set; }
    }
}
