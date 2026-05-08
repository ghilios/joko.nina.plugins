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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    public enum WizardStep {
        Baseline = 0,       // Verify start position, run measurement → auto-advance
        ScrewNumbering = 1, // Informational: user clicks Continue to advance
        AllScrews = 2,      // Turn all screws inward, run measurement → auto-advance
        Screw1 = 3,         // Adjust screw 1, run measurement → auto-advance
        Screw2 = 4,         // Adjust screw 2, run measurement → auto-advance
        Complete = 5
    }

    [PartCreationPolicy(CreationPolicy.Shared)]
    [Export(typeof(IDockableVM))]
    [Export]
    public class TiltAdapterWizardVM : DockableVM {

        private readonly ITiltAdapterOptions tiltAdapterOptions;
        private readonly InspectorVM inspector;

        private WizardStep currentStep = WizardStep.Baseline;
        private bool isWizardRunning = false;
        private bool isMeasuring = false;
        private bool hasWarning = false;
        private string warningText = string.Empty;
        private string statusText = string.Empty;

        private (double A, double B) baselineReading;
        private (double A, double B) screw1Reading;
        private (double A, double B) screw2Reading;
        private double baselineCurvatureReading;
        private double allScrewsCurvatureReading;
        private CancellationTokenSource measureCts;

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

            StartCommand = new AsyncRelayCommand(StartAsync, () => !IsWizardRunning);
            RunMeasurementCommand = new AsyncRelayCommand(RunMeasurementAsync, () => IsOnMeasurementStep && !IsMeasuring);
            CancelCommand = new RelayCommand(CancelMeasurement, () => IsMeasuring);
            ContinueCommand = new RelayCommand(NextStep, () => currentStep == WizardStep.ScrewNumbering);
            RestartCommand = new RelayCommand(Restart);

            tiltAdapterOptions.PropertyChanged += (s, e) => {
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
            };

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
                RaisePropertyChanged(nameof(IsOnInformationalStep));
                RaisePropertyChanged(nameof(StepInstructions));
                NotifyCommandsCanExecuteChanged();
            }
        }

        public bool IsComplete => currentStep == WizardStep.Complete;

        // Steps that end with running the aberration inspector (measurement auto-advances)
        public bool IsOnMeasurementStep =>
            currentStep == WizardStep.Baseline ||
            currentStep == WizardStep.AllScrews ||
            currentStep == WizardStep.Screw1 ||
            currentStep == WizardStep.Screw2;

        // Steps where the user just reads instructions and clicks Continue
        public bool IsOnInformationalStep => currentStep == WizardStep.ScrewNumbering;

        public bool IsCalibrationValid =>
            tiltAdapterOptions.IsCalibrated &&
            tiltAdapterOptions.ScrewCount == tiltAdapterOptions.CalibratedScrewCount;

        public bool HasCurvatureCalibration => tiltAdapterOptions.ScrewInwardCurvatureSign != 0;

        public string CurvatureSignDescription =>
            tiltAdapterOptions.ScrewInwardCurvatureSign == 1 ? "↑" :
            tiltAdapterOptions.ScrewInwardCurvatureSign == -1 ? "↓" :
            string.Empty;

        public bool IsMeasuring {
            get => isMeasuring;
            private set {
                isMeasuring = value;
                RaisePropertyChanged();
                NotifyCommandsCanExecuteChanged();
            }
        }

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

        public string StepInstructions {
            get {
                int n = tiltAdapterOptions.ScrewCount;
                switch (currentStep) {
                    case WizardStep.Baseline:
                        return "Ensure all screws are at their starting position, then click Run Measurement to take a baseline reading.";
                    case WizardStep.ScrewNumbering:
                        return n == 3
                            ? "Identify your screws. Label them 1, 2, and 3. Screw 1 is at the top of the adapter (12 o'clock); the remaining screws are numbered clockwise: Screw 2 at lower-right, Screw 3 at lower-left."
                            : "Identify your screws. Label them 1, 2, 3, and 4. Screw 1 is at the top-right of the adapter; the remaining screws are numbered clockwise: Screw 2 at lower-right, Screw 3 at lower-left, Screw 4 at upper-left.";
                    case WizardStep.AllScrews:
                        return "Turn ALL screws INWARD exactly 1 full turn each, then click Run Measurement.";
                    case WizardStep.Screw1:
                        return n == 3
                            ? "Turn screws 1, 2, and 3 each back OUT 1 full turn to return to baseline. Then turn screw 1 INWARD exactly 1 full turn, then click Run Measurement."
                            : "Turn screws 1, 2, 3, and 4 each back OUT 1 full turn to return to baseline. Then turn screw 1 INWARD and screw 3 OUTWARD exactly 1 full turn each, then click Run Measurement.";
                    case WizardStep.Screw2:
                        return n == 3
                            ? "Turn screw 1 back OUT 1 full turn to return to baseline. Then turn screw 2 INWARD exactly 1 full turn, then click Run Measurement."
                            : "Turn screw 1 back OUT and screw 3 back IN 1 full turn to return to baseline. Then turn screw 2 INWARD and screw 4 OUTWARD exactly 1 full turn each, then click Run Measurement.";
                    case WizardStep.Complete:
                        return "Restore all screws to their original position.";
                    default:
                        return string.Empty;
                }
            }
        }

        public ICommand StartCommand { get; }
        public ICommand RunMeasurementCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ContinueCommand { get; }
        public ICommand RestartCommand { get; }

        private Task StartAsync() {
            StatusText = string.Empty;
            CurrentStep = WizardStep.Baseline;
            IsWizardRunning = true;
            return Task.CompletedTask;
        }

        private async Task RunMeasurementAsync() {
            measureCts?.Dispose();
            measureCts = new CancellationTokenSource();
            var token = measureCts.Token;

            IsMeasuring = true;
            bool success = false;

            try {
                int count = Math.Max(1, tiltAdapterOptions.MeasurementAverageCount);
                const int simulatedSecondsPerRun = 3;

                for (int i = 0; i < count; i++) {
                    token.ThrowIfCancellationRequested();
                    var runStart = DateTime.UtcNow;

                    while (true) {
                        token.ThrowIfCancellationRequested();
                        double elapsed = (DateTime.UtcNow - runStart).TotalSeconds;
                        if (elapsed >= simulatedSecondsPerRun) break;
                        double remaining = simulatedSecondsPerRun - elapsed;
                        StatusText = $"Running measurement {i + 1}/{count}... ({remaining:F0}s remaining)";
                        await Task.Delay(100, token);
                    }
                }

                InjectSyntheticData();
                success = true;
            } catch (OperationCanceledException) {
                StatusText = "Measurement cancelled.";
            } finally {
                IsMeasuring = false;
            }

            if (success) {
                StatusText = "Measurement complete.";
                await Task.Delay(1500); // let user see the result before advancing
                if (isWizardRunning) {
                    NextStep();
                }
            }
        }

        private void CancelMeasurement() {
            measureCts?.Cancel();
        }

        // Inject synthetic readings for the current step (replaced in Phase 3 with real inspector calls).
        // Screw1 targets 90°, Screw2 targets 210° → produces 90°/210°/330° (evenly 120° apart).
        private void InjectSyntheticData() {
            switch (currentStep) {
                case WizardStep.Baseline:
                    baselineReading = (0.0, 0.0);
                    baselineCurvatureReading = 5000.0;
                    break;
                case WizardStep.AllScrews:
                    allScrewsCurvatureReading = 5100.0;
                    break;
                case WizardStep.Screw1:
                    // dA=sin(90°)=1, dB=-cos(90°)=0 → atan2(0.05, 0) = 90°
                    screw1Reading = (0.05, 0.0);
                    break;
                case WizardStep.Screw2:
                    // dA=sin(210°)=-0.5, dB=-cos(210°)=√3/2 → atan2(-0.025, -0.04330) = -150° → normalized 210°
                    screw2Reading = (-0.025, 0.04330);
                    break;
            }
        }

        private void NextStep() {
            if (currentStep == WizardStep.AllScrews) {
                CalculateAndSaveCurvatureSign();
            }

            WizardStep next = currentStep + 1;

            if (next == WizardStep.Complete) {
                CalculateAndSaveAngles();
                RebuildDiagram();
            }

            StatusText = string.Empty;
            CurrentStep = next;
        }

        private void Restart() {
            measureCts?.Cancel();
            IsWizardRunning = false;
            IsMeasuring = false;
            baselineReading = default;
            screw1Reading = default;
            screw2Reading = default;
            baselineCurvatureReading = 0;
            allScrewsCurvatureReading = 0;
            StatusText = string.Empty;
            CurrentStep = WizardStep.Baseline;
        }

        private void CalculateAndSaveCurvatureSign() {
            double delta = allScrewsCurvatureReading - baselineCurvatureReading;
            tiltAdapterOptions.ScrewInwardCurvatureSign = delta >= 0 ? 1 : -1;
        }

        private void CalculateAndSaveAngles() {
            double d1A = screw1Reading.A - baselineReading.A;
            double d1B = screw1Reading.B - baselineReading.B;
            double d2A = screw2Reading.A - baselineReading.A;
            double d2B = screw2Reading.B - baselineReading.B;

            // atan2(dA, -dB): 0°=top, 90°=right (clockwise from top in image space)
            double angle1 = NormalizeAngle(Math.Atan2(d1A, -d1B) * 180.0 / Math.PI);
            double angle2 = NormalizeAngle(Math.Atan2(d2A, -d2B) * 180.0 / Math.PI);

            // Determine winding direction from measured data. Image mirroring causes screws
            // numbered clockwise on the physical adapter to appear counter-clockwise in the
            // sensor image; the diff tells us which direction is correct.
            double rawDiff = NormalizeAngle(angle2 - angle1);
            bool clockwise = rawDiff < 180.0;
            int n = tiltAdapterOptions.ScrewCount;

            // Constrained least-squares fit: find theta1 that minimises
            //   (theta1 - angle1)^2 + (theta1 + s - angle2)^2
            // subject to equal angular spacing s. Solution: shift angle1 by half the
            // residual from the ideal gap, splitting measurement error evenly.
            if (n == 3) {
                double s = clockwise ? 120.0 : -120.0;
                double expectedDiff = clockwise ? 120.0 : 240.0;
                double theta1 = NormalizeAngle(angle1 + (rawDiff - expectedDiff) / 2.0);
                tiltAdapterOptions.Screw1AngleDegrees = theta1;
                tiltAdapterOptions.Screw2AngleDegrees = NormalizeAngle(theta1 + s);
                tiltAdapterOptions.Screw3AngleDegrees = NormalizeAngle(theta1 + 2 * s);
                tiltAdapterOptions.Screw4AngleDegrees = double.NaN;
            } else {
                double s = clockwise ? 90.0 : -90.0;
                double expectedDiff = clockwise ? 90.0 : 270.0;
                double theta1 = NormalizeAngle(angle1 + (rawDiff - expectedDiff) / 2.0);
                double theta2 = NormalizeAngle(theta1 + s);
                tiltAdapterOptions.Screw1AngleDegrees = theta1;
                tiltAdapterOptions.Screw2AngleDegrees = theta2;
                // Opposite screws are always 180° apart regardless of mirroring.
                tiltAdapterOptions.Screw3AngleDegrees = NormalizeAngle(theta1 + 180.0);
                tiltAdapterOptions.Screw4AngleDegrees = NormalizeAngle(theta2 + 180.0);
            }

            tiltAdapterOptions.CalibratedScrewCount = tiltAdapterOptions.ScrewCount;
            tiltAdapterOptions.IsCalibrated = true;
            ValidateAngleSeparation(rawDiff);
        }

        // Checks raw measured diff (before constrained fit) to catch poor-quality calibrations.
        private void ValidateAngleSeparation(double rawDiff) {
            int n = tiltAdapterOptions.ScrewCount;
            double expected = n == 3 ? 120.0 : 90.0;
            // Fold the diff so it is in [0, 180] — both CW and CCW gaps compare to the same expected value.
            double foldedDiff = rawDiff <= 180.0 ? rawDiff : 360.0 - rawDiff;
            double deviation = Math.Abs(foldedDiff - expected);
            HasWarning = deviation > 30.0;
            WarningText = HasWarning
                ? $"Screw 1→2 measured angle gap is {foldedDiff:F1}° (expected ~{expected}°). Consider recalibrating."
                : string.Empty;
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

        private void NotifyCommandsCanExecuteChanged() {
            ((AsyncRelayCommand)StartCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)RunMeasurementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ContinueCommand).NotifyCanExecuteChanged();
        }

        private static double NormalizeAngle(double deg) => ((deg % 360) + 360) % 360;
    }
}
