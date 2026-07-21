#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    [Export(typeof(IDockableVM))]
    public class StarDetectionOptionsVM : DockableVM {

        // WindowServiceFactory is not a MEF export (NINA exposes the concrete type with a default ctor only), so it is
        // instantiated directly here, mirroring HocusFocusPlugin. Used to show the import-confirmation dialog.
        private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();

        [ImportingConstructor]
        public StarDetectionOptionsVM(IProfileService profileService)
            : base(profileService) {
            this.StarDetectionOptions = HocusFocusPlugin.StarDetectionOptions;
            this.PerFilterStore = HocusFocusPlugin.PerFilterStarDetection;
            this.PerFilterEditBinder = HocusFocusPlugin.PerFilterStarDetectionEditBinder;
            this.Title = "Star Detection Options";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["HocusFocusDetectStarsSVG"];
            ImageGeometry.Freeze();

            ChooseIntermediatePathDiagCommand = new RelayCommand(ChooseIntermediatePathDiag);
            OptimizeStarDetectionCommand = new RelayCommand(OptimizeStarDetection);
            ExportStarDetectionSettingsCommand = new RelayCommand(() => StarDetectionSettingsIO.Export(StarDetectionOptions));
            ImportStarDetectionSettingsCommand = new AsyncRelayCommand(() => StarDetectionSettingsIO.ImportAsync(StarDetectionOptions, windowServiceFactory));
            // Enablement is driven by PerFilterEditBinder.CanCopyFromFilter (IsEnabled binding), not a canExecute
            // predicate — see the note on the same command in HocusFocusPlugin.
            CopyStarDetectionFromFilterCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<string>(CopyStarDetectionFromFilter);
        }

        private void OptimizeStarDetection() {
            // Delegates to the shared launcher set by HocusFocusPlugin. Null-safe so the command is a no-op
            // when no plugin instance has been constructed (e.g. a unit test constructing this VM directly).
            HocusFocusPlugin.LaunchStarDetectionOptimizer?.Invoke();
        }

        private Task CopyStarDetectionFromFilter(string sourceFilterName) {
            // Same static-delegation pattern as OptimizeStarDetection: the plugin owns the store/buffer/dialog wiring.
            return HocusFocusPlugin.LaunchCopyStarDetectionFromFilter?.Invoke(sourceFilterName) ?? Task.CompletedTask;
        }

        private void ChooseIntermediatePathDiag() {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                dialog.SelectedPath = StarDetectionOptions.IntermediateSavePath;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    StarDetectionOptions.IntermediateSavePath = dialog.SelectedPath;
                }
            }
        }

        public override bool IsTool { get; } = true;

        public StarDetectionOptions StarDetectionOptions { get; private set; }

        // Same property names as HocusFocusPlugin so the shared HocusFocus_StarDetection_Options template's
        // binding paths resolve on both hosts. Null when no plugin instance exists (unit tests) — bindings no-op.
        public IPerFilterStarDetectionStore PerFilterStore { get; private set; }

        public PerFilterEditBinder PerFilterEditBinder { get; private set; }

        public ICommand ChooseIntermediatePathDiagCommand { get; private set; }

        public ICommand OptimizeStarDetectionCommand { get; private set; }

        public ICommand ExportStarDetectionSettingsCommand { get; private set; }

        public ICommand ImportStarDetectionSettingsCommand { get; private set; }

        public ICommand CopyStarDetectionFromFilterCommand { get; private set; }
    }
}
