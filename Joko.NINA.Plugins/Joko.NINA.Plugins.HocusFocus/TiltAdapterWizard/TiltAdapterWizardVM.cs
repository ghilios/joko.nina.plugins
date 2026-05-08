#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.Input;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    public enum WizardStep {
        BaselineMeasurement = 0,
        ScrewNumbering = 1,
        Screw1Adjustment = 2,
        Screw1Measurement = 3,
        Screw2Adjustment = 4,
        Screw2Measurement = 5,
        Complete = 6
    }

    [PartCreationPolicy(CreationPolicy.Shared)]
    [Export(typeof(IDockableVM))]
    [Export]
    public class TiltAdapterWizardVM : DockableVM {
        private static readonly Random rng = new Random();

        private readonly ITiltAdapterOptions tiltAdapterOptions;
        private readonly InspectorVM inspector;

        private WizardStep currentStep = WizardStep.BaselineMeasurement;
        private bool isWizardRunning = false;
        private bool hasWarning = false;
        private string warningText = string.Empty;

        [ImportingConstructor]
        public TiltAdapterWizardVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            InspectorVM inspector)
            : this(profileService, applicationStatusMediator, inspector,
                   HocusFocusPlugin.TiltAdapterOptions) { }

        public TiltAdapterWizardVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            InspectorVM inspector,
            ITiltAdapterOptions tiltAdapterOptions)
            : base(profileService) {
            this.inspector = inspector;
            this.tiltAdapterOptions = tiltAdapterOptions;

            this.Title = "Tilt Adapter Wizard";

            ScrewDiagramItems = new ObservableCollection<TiltScrewDiagramItem>();
            ScrewConnectionLines = new ObservableCollection<TiltScrewConnectionLine>();

            StartCommand = new AsyncRelayCommand(StartAsync);

            // DEV
            DevSetMock3ScrewCommand = new RelayCommand(DevSetMock3Screw);
            DevSetMock4ScrewCommand = new RelayCommand(DevSetMock4Screw);
            DevClearCalibrationCommand = new RelayCommand(DevClearCalibration);

            RebuildDiagram();
        }

        public ITiltAdapterOptions TiltAdapterOptions => tiltAdapterOptions;

        public ObservableCollection<TiltScrewDiagramItem> ScrewDiagramItems { get; }

        public ObservableCollection<TiltScrewConnectionLine> ScrewConnectionLines { get; }

        public bool IsWizardRunning {
            get => isWizardRunning;
            private set {
                isWizardRunning = value;
                RaisePropertyChanged();
            }
        }

        public WizardStep CurrentStep {
            get => currentStep;
            private set {
                currentStep = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsComplete));
                RaisePropertyChanged(nameof(StepInstructions));
            }
        }

        public bool IsComplete => currentStep == WizardStep.Complete;

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

        public string StepInstructions {
            get {
                if (currentStep == WizardStep.Complete) {
                    return "Restore all screws to their original position.";
                }
                return string.Empty;
            }
        }

        public ICommand StartCommand { get; }

        // DEV
        public ICommand DevSetMock3ScrewCommand { get; }
        public ICommand DevSetMock4ScrewCommand { get; }
        public ICommand DevClearCalibrationCommand { get; }

        private void RebuildDiagram() {
            ScrewDiagramItems.Clear();
            ScrewConnectionLines.Clear();

            int n = tiltAdapterOptions.ScrewCount;
            if (n < 3 || !tiltAdapterOptions.IsCalibrated) return;

            var angles = new[] {
                tiltAdapterOptions.Screw1AngleDegrees,
                tiltAdapterOptions.Screw2AngleDegrees,
                tiltAdapterOptions.Screw3AngleDegrees,
                n == 4 ? tiltAdapterOptions.Screw4AngleDegrees : double.NaN
            };

            // Canvas 200×200, center (100,100), radius 75, circle half-size 12
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

            // Connection lines forming the screw polygon (closing back to first screw)
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

        private Task StartAsync() => throw new NotImplementedException();

        // DEV — random base angle preserving 120° spacing
        private void DevSetMock3Screw() {
            double baseAngle = rng.NextDouble() * 360.0;
            tiltAdapterOptions.ScrewCount = 3;
            tiltAdapterOptions.Screw1AngleDegrees = NormalizeAngle(baseAngle);
            tiltAdapterOptions.Screw2AngleDegrees = NormalizeAngle(baseAngle + 120.0);
            tiltAdapterOptions.Screw3AngleDegrees = NormalizeAngle(baseAngle + 240.0);
            tiltAdapterOptions.Screw4AngleDegrees = double.NaN;
            tiltAdapterOptions.IsCalibrated = true;
            RebuildDiagram();
        }

        // DEV — random base angle preserving 90° spacing
        private void DevSetMock4Screw() {
            double baseAngle = rng.NextDouble() * 360.0;
            tiltAdapterOptions.ScrewCount = 4;
            tiltAdapterOptions.Screw1AngleDegrees = NormalizeAngle(baseAngle);
            tiltAdapterOptions.Screw2AngleDegrees = NormalizeAngle(baseAngle + 90.0);
            tiltAdapterOptions.Screw3AngleDegrees = NormalizeAngle(baseAngle + 180.0);
            tiltAdapterOptions.Screw4AngleDegrees = NormalizeAngle(baseAngle + 270.0);
            tiltAdapterOptions.IsCalibrated = true;
            RebuildDiagram();
        }

        // DEV
        private void DevClearCalibration() {
            tiltAdapterOptions.IsCalibrated = false;
            tiltAdapterOptions.Screw1AngleDegrees = double.NaN;
            tiltAdapterOptions.Screw2AngleDegrees = double.NaN;
            tiltAdapterOptions.Screw3AngleDegrees = double.NaN;
            tiltAdapterOptions.Screw4AngleDegrees = double.NaN;
            RebuildDiagram();
        }

        private static double NormalizeAngle(double deg) => ((deg % 360) + 360) % 360;
    }
}
