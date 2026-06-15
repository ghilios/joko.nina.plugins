#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Properties;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Core.Utility;
using NINA.Core.Utility.WindowService;
using NINA.Plugin;
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

namespace NINA.Joko.Plugins.HocusFocus {

    [Export(typeof(IPluginManifest))]
    public class HocusFocusPlugin : PluginBase {

        // Collaborators captured for the on-demand Star Detection Optimization Wizard (T5). The wizard is created
        // lazily when the user clicks "Optimize Star Detection…", well after MEF composition, so these are safe to
        // reuse for building its RunEvaluationLoader + detector.
        private readonly IProfileService profileService;
        private readonly IFocuserMediator focuserMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageDataFactory imageDataFactory;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;

        // WindowServiceFactory is not a MEF export (NINA exposes the concrete type with a default ctor only), so it
        // is instantiated directly here, mirroring RunAberrationInspector.
        private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();

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
            this.focuserMediator = focuserMediator;
            this.imagingMediator = imagingMediator;
            this.imageDataFactory = imageDataFactory;
            this.starDetectionSelector = starDetectionSelector;
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                Settings.Default.Save();
            }

            if (StarDetectionOptions == null) {
                StarDetectionOptions = new StarDetectionOptions(profileService);
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
                    AlglibAPI);
            }

            var thisAssembly = Assembly.GetAssembly(typeof(HocusFocusPlugin));
            var thisAssemblyFileInfo = new FileInfo(thisAssembly.Location);
            if (ApplicationDispatcher == null) {
                ApplicationDispatcher = new ApplicationDispatcher();

                var archFolder = Environment.Is64BitProcess ? "x64" : "x86";
                var dllPath = Path.Combine(thisAssemblyFileInfo.Directory.FullName, "dll", archFolder);
                OpenCvSharp.Internal.WindowsLibraryLoader.Instance.AdditionalPaths.Add(dllPath);
            }

            options.AddImagePattern(fwhmImagePattern);
            options.AddImagePattern(eccentricityImagePattern);
            imageSaveMediator.BeforeFinalizeImageSaved += ImageSaveMediator_BeforeFinalizeImageSaved;
            ResetStarDetectionDefaultsCommand = new RelayCommand(StarDetectionOptions.ResetDefaults);
            ResetStarAnnotatorDefaultsCommand = new RelayCommand(StarAnnotatorOptions.ResetDefaults);
            ResetAutoFocusDefaultsCommand = new RelayCommand(AutoFocusOptions.ResetDefaults);
            ChooseIntermediatePathDiagCommand = new RelayCommand(ChooseIntermediatePathDiag);
            ChooseSavePathDiagCommand = new RelayCommand(ChooseSavePathDiag);
            OptimizeStarDetectionCommand = new RelayCommand(OptimizeStarDetection);
            LaunchStarDetectionOptimizer = OptimizeStarDetection;
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
                    AlglibAPI);

            var vm = new StarDetectionOptimizerWizardVM(
                profileService,
                imageDataFactory,
                imagingMediator,
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

        public static AutoFocusEngineFactory AutoFocusEngineFactory { get; private set; }

        public static ApplicationDispatcher ApplicationDispatcher { get; private set; }

        public static IAlglibAPI AlglibAPI { get; private set; }

        /// <summary>
        /// Shared launcher for the Star Detection Optimization Wizard. Set by the plugin constructor so other
        /// ViewModels (e.g. the Imaging-pane StarDetectionOptionsVM) can open the same wizard without re-wiring
        /// the wizard's dependencies. May be null when no plugin instance has been constructed (e.g. unit tests).
        /// </summary>
        public static Action LaunchStarDetectionOptimizer { get; private set; }

        public ICommand ResetStarDetectionDefaultsCommand { get; private set; }

        public ICommand ResetStarAnnotatorDefaultsCommand { get; private set; }

        public ICommand ResetAutoFocusDefaultsCommand { get; private set; }

        public ICommand ChooseIntermediatePathDiagCommand { get; private set; }

        public ICommand ChooseSavePathDiagCommand { get; private set; }

        public ICommand OptimizeStarDetectionCommand { get; private set; }
    }
}
