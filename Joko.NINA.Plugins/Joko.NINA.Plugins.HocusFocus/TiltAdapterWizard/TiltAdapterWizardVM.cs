#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.Input;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.Composition;
using System.Linq;
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
        private readonly IProgress<ApplicationStatus> progress;

        private WizardStep currentStep = WizardStep.Baseline;
        private bool isWizardRunning = false;
        private bool isMeasuring = false;
        private bool hasWarning = false;
        private string warningText = string.Empty;
        private string statusText = string.Empty;
        private bool hasMeasurementConsistencyWarning = false;
        private string measurementConsistencyWarningText = string.Empty;
        private bool measurementDoneForCurrentStep = false;

        private (double A, double B) baselineReading;
        private (double A, double B) screw1Reading;
        private (double A, double B) screw2Reading;
        private double baselineCurvatureReading;
        private double allScrewsCurvatureReading;
        private CancellationTokenSource measureCts;

        private const double MeasurementConsistencyWarningThreshold = 0.02;

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
            this.progress = ProgressFactory.Create(applicationStatusMediator, "Tilt Adapter Wizard");

            this.Title = "Tilt Adapter Wizard";

            ScrewDiagramItems = new ObservableCollection<TiltScrewDiagramItem>();
            ScrewConnectionLines = new ObservableCollection<TiltScrewConnectionLine>();
            StepMeasurementSummary = new ObservableCollection<TiltMeasurementSummaryRow>();
            StepMeasurementSummary.CollectionChanged += OnSummaryCollectionChanged;

            StartCommand = new AsyncRelayCommand(StartAsync, () => !IsWizardRunning);
            RunMeasurementCommand = new AsyncRelayCommand(RunMeasurementAsync, () => IsOnMeasurementStep && !IsMeasuring);
            CancelCommand = new RelayCommand(CancelMeasurement, () => IsMeasuring);
            ContinueCommand = new RelayCommand(NextStep, () => CanAdvance && !IsMeasuring);
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

        private void OnSummaryCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            RaisePropertyChanged(nameof(HasMeasurementFeedback));
        }

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
                measurementDoneForCurrentStep = false;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsComplete));
                RaisePropertyChanged(nameof(IsOnMeasurementStep));
                RaisePropertyChanged(nameof(IsOnInformationalStep));
                RaisePropertyChanged(nameof(StepInstructions));
                RaisePropertyChanged(nameof(CanAdvance));
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

        public bool CanAdvance => IsOnInformationalStep || measurementDoneForCurrentStep;

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
                RaisePropertyChanged(nameof(HasMeasurementFeedback));
                NotifyCommandsCanExecuteChanged();
            }
        }

        public bool HasMeasurementFeedback => isMeasuring || StepMeasurementSummary.Count > 0;

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
            StepMeasurementSummary.Clear();
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;

            measureCts?.Dispose();
            measureCts = new CancellationTokenSource();
            var token = measureCts.Token;
            IsMeasuring = true;

            try {
                bool success = false;

                switch (currentStep) {
                    case WizardStep.Baseline: {
                        var result = await RunAveragedTiltMeasurement(token);
                        if (result != null) {
                            baselineReading = result.Value;
                            baselineCurvatureReading = inspector.TiltModel?.TiltPlaneModel?.MeanFocuserPosition ?? 0.0;
                            success = true;
                        }
                        break;
                    }
                    case WizardStep.AllScrews: {
                        var result = await RunAveragedCurvatureMeasurement(token);
                        if (result != null) {
                            allScrewsCurvatureReading = result.Value;
                            success = true;
                        }
                        break;
                    }
                    case WizardStep.Screw1: {
                        var result = await RunAveragedTiltMeasurement(token);
                        if (result != null) {
                            screw1Reading = result.Value;
                            success = true;
                        }
                        break;
                    }
                    case WizardStep.Screw2: {
                        var result = await RunAveragedTiltMeasurement(token);
                        if (result != null) {
                            screw2Reading = result.Value;
                            success = true;
                        }
                        break;
                    }
                }

                if (success) {
                    measurementDoneForCurrentStep = true;
                    StatusText = "Measurement complete.";
                    RaisePropertyChanged(nameof(CanAdvance));
                    NotifyCommandsCanExecuteChanged();
                } else {
                    StatusText = "Measurement failed.";
                }
            } catch (OperationCanceledException) {
                StatusText = "Measurement cancelled.";
            } finally {
                IsMeasuring = false;
            }
        }

        private async Task<(double A, double B)?> RunAveragedTiltMeasurement(CancellationToken token) {
            int count = Math.Max(1, tiltAdapterOptions.MeasurementAverageCount);
            var readings = new List<(double A, double B)>(count);

            for (int i = 0; i < count; i++) {
                token.ThrowIfCancellationRequested();
                StatusText = $"Run {i + 1}/{count}...";
                bool ok = await inspector.AnalyzeAutoFocus(token, captureCameraBlock: true);
                if (!ok) return null;
                var m = inspector.TiltModel?.TiltPlaneModel;
                if (m == null) return null;
                readings.Add((m.A, m.B));
            }

            double avgA = readings.Average(r => r.A);
            double avgB = readings.Average(r => r.B);

            for (int i = 0; i < readings.Count; i++) {
                var (a, b) = readings[i];
                StepMeasurementSummary.Add(new TiltMeasurementSummaryRow {
                    RunNumber = i + 1,
                    Direction = NormalizeAngle(Math.Atan2(a, -b) * 180.0 / Math.PI),
                    Magnitude = Math.Sqrt(a * a + b * b),
                    IsAverage = false
                });
            }

            if (count > 1) {
                StepMeasurementSummary.Add(new TiltMeasurementSummaryRow {
                    RunNumber = 0,
                    Direction = NormalizeAngle(Math.Atan2(avgA, -avgB) * 180.0 / Math.PI),
                    Magnitude = Math.Sqrt(avgA * avgA + avgB * avgB),
                    IsAverage = true
                });

                double maxDev = readings.Max(r =>
                    Math.Sqrt(Math.Pow(r.A - avgA, 2) + Math.Pow(r.B - avgB, 2)));
                if (maxDev > MeasurementConsistencyWarningThreshold) {
                    HasMeasurementConsistencyWarning = true;
                    MeasurementConsistencyWarningText =
                        $"Measurements inconsistent: max deviation {maxDev:F4} exceeds {MeasurementConsistencyWarningThreshold:F4}. Consider re-running.";
                }
            }

            return (avgA, avgB);
        }

        private async Task<double?> RunAveragedCurvatureMeasurement(CancellationToken token) {
            int count = Math.Max(1, tiltAdapterOptions.MeasurementAverageCount);
            double sum = 0;
            for (int i = 0; i < count; i++) {
                token.ThrowIfCancellationRequested();
                StatusText = $"Run {i + 1}/{count}...";
                bool ok = await inspector.AnalyzeAutoFocus(token, captureCameraBlock: true);
                if (!ok) return null;
                var pos = inspector.TiltModel?.TiltPlaneModel?.MeanFocuserPosition;
                if (pos == null) return null;
                sum += pos.Value;
            }
            return sum / count;
        }

        private void CancelMeasurement() {
            measureCts?.Cancel();
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

            StepMeasurementSummary.Clear();
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
            StatusText = string.Empty;
            CurrentStep = next;
        }

        private void Restart() {
            measureCts?.Cancel();
            IsWizardRunning = false;
            IsMeasuring = false;
            measurementDoneForCurrentStep = false;
            baselineReading = default;
            screw1Reading = default;
            screw2Reading = default;
            baselineCurvatureReading = 0;
            allScrewsCurvatureReading = 0;
            StepMeasurementSummary.Clear();
            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
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
