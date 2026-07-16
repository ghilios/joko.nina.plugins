#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Input;
// NINA.Core.Utility ships its own (deprecated) RelayCommand, so both toolkit commands are aliased rather than
// imported — the same disambiguation TiltAdapterWizardVM uses.
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using ScrewTurnCommand = CommunityToolkit.Mvvm.Input.RelayCommand<NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter.ScrewTurn>;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter {

    /// <summary>Which screws a click moves on a 4-screw (coupled) adapter. See design §3.3.</summary>
    public enum SimTiltMovementMode {

        /// <summary>The named screw and its diagonal opposite, turned opposite ways — pure tilt toward a corner.</summary>
        Corner = 0,

        /// <summary>The named adjacent pair together, the opposing pair counter-turned — tilt about an edge axis.</summary>
        Side = 1,

        /// <summary>All four the same way — pure piston: backfocus/curvature changes, tilt untouched.</summary>
        Backfocus = 2
    }

    /// <summary>
    /// One click on a turn button: which row, and which way it was turned. RotationSign is +1 for ⟳ / "+" and
    /// −1 for ⟲ / "−" — direction comes from the button, never from a signed amount, so the two cannot
    /// contradict each other (UX design §2).
    /// </summary>
    public readonly struct ScrewTurn {

        public ScrewTurn(int screwIndex, int rotationSign) {
            ScrewIndex = screwIndex;
            RotationSign = Math.Sign(rotationSign) == 0 ? 1 : Math.Sign(rotationSign);
        }

        /// <summary>0-based row index. In Backfocus mode there is only one row, so this is always 0.</summary>
        public int ScrewIndex { get; }

        /// <summary>+1 (⟳ / "+") or −1 (⟲ / "−").</summary>
        public int RotationSign { get; }
    }

    /// <summary>
    /// One Operate row. Immutable: the VM replaces the whole <see cref="SimulatedTiltAdapterVM.Rows"/>
    /// collection whenever anything a row renders (mode, angles, amount, rig direction) changes.
    /// </summary>
    public sealed class SimTiltAdapterRow {

        public SimTiltAdapterRow(int index, string label, string couplingText,
                string positiveTooltip, string negativeTooltip) {
            Index = index;
            Label = label;
            CouplingText = couplingText ?? string.Empty;
            PositiveTooltip = positiveTooltip;
            NegativeTooltip = negativeTooltip;
            PositiveTurn = new ScrewTurn(index, +1);
            NegativeTurn = new ScrewTurn(index, -1);
        }

        public int Index { get; }

        /// <summary>"Screw 2 · 135°", "Side 1+2 · right", or "All screws 1–4".</summary>
        public string Label { get; }

        /// <summary>"4 opposes" / "3+4 oppose" — names the coupled partner the click counter-turns. Empty when none.</summary>
        public string CouplingText { get; }

        public ScrewTurn PositiveTurn { get; }
        public ScrewTurn NegativeTurn { get; }

        /// <summary>Rotation in, MOTION out: these state where the plate goes (⬆/⬇), per the glyph contract.</summary>
        public string PositiveTooltip { get; }

        public string NegativeTooltip { get; }
    }

    /// <summary>
    /// Backs the virtual tilt-adapter panel (`HocusFocus_SimTiltAdapter_Panel`), hosted by both the simulator
    /// camera's setup dialog and the Imaging dockable. It closes the loop the synthetic camera exists for:
    /// inject a tilt → the Aberration Inspector says "screw 2: 0.75 ⟳" → click exactly that here → re-run → flat.
    ///
    /// Everything geometric is delegated to <see cref="SimulatedTiltAdapter"/> and <see cref="TiltScrewGeometry"/>
    /// so the virtual adapter and the inspector can never disagree about conventions. The VM's own job is only to
    /// fold the model's <see cref="AberrationDelta"/> into the persisted injected aberration — which is exactly
    /// where the rig-direction sign is easiest to lose; see <see cref="ApplyDelta"/>.
    /// </summary>
    public class SimulatedTiltAdapterVM : BaseINPC {

        /// <summary>Below this the state strip calls the plane flat. Display-only; deliberately not configurable.</summary>
        private const double FlatThresholdMicrons = 1.0;

        /// <summary>Passive "expect heavy donuts" warning above this. Extreme states stay legal — Undo is free.</summary>
        private const double ExtremeTiltMicrons = 500.0;

        /// <summary>The persisted bounds of the aberration boxes (Resources/OptionsDataTemplates.xaml).</summary>
        private const double AberrationBoundMicrons = 10_000.0;

        private const double DefaultTurnsPerClick = 0.25;
        private const double DefaultStepsPerClick = 10.0;

        /// <summary>Coherence tolerances (UX design §6). Radius follows pitch — both are hardware measurements.</summary>
        private const double AngleToleranceDegrees = 2.0;

        private const double HardwareToleranceFraction = 0.05;

        private readonly ICameraSimulatorOptions options;
        private readonly ITiltAdapterOptions realAdapter;
        private readonly double[] netAxialMicrons = new double[4];

        private SimulatedTiltAdapter adapter;
        private bool derivingAngles;
        private PanelSnapshot? undoSnapshot;

        /// <summary>Production: the badge compares against the user's real, hand-calibrated adapter.</summary>
        public SimulatedTiltAdapterVM(ICameraSimulatorOptions options)
            : this(options, HocusFocusPlugin.TiltAdapterOptions) {
        }

        /// <summary>
        /// Test seam. A null <paramref name="realAdapterOptions"/> is a legitimate production state too (the panel
        /// can be built before the plugin's static options exist), and the badge reports it honestly rather than
        /// claiming a match it cannot verify.
        /// </summary>
        internal SimulatedTiltAdapterVM(ICameraSimulatorOptions options, ITiltAdapterOptions realAdapterOptions) {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            realAdapter = realAdapterOptions;

            amountPerClick = DefaultAmountPerClick;
            ScrewDiagramItems = new ObservableCollection<TiltScrewDiagramItem>();
            ScrewConnectionLines = new ObservableCollection<TiltScrewConnectionLine>();

            TurnCommand = new ScrewTurnCommand(Turn, CanTurn);
            UndoCommand = new RelayCommand(Undo, () => CanUndo);
            RezeroCommand = new RelayCommand(Rezero);
            ZeroAberrationsCommand = new RelayCommand(ZeroAberrations);
            EnableAberrationsCommand = new RelayCommand(() => options.EnableAberrations = true);
            AutoFillAnglesCommand = new RelayCommand(AutoFillAngles);
            CopyFromAdapterCommand = new RelayCommand(CopyFromAdapter, () => CanCopyAdapterSettings);
            CopyToAdapterCommand = new RelayCommand(() => IsCopyToAdapterPending = true, () => CanCopyAdapterSettings);
            ConfirmCopyToAdapterCommand = new RelayCommand(ConfirmCopyToAdapter);
            CancelCopyToAdapterCommand = new RelayCommand(() => IsCopyToAdapterPending = false);

            options.PropertyChanged += OnOptionsChanged;
            if (realAdapter != null) {
                realAdapter.PropertyChanged += OnRealAdapterChanged;
            }

            RebuildAll();
        }

        // ---- Commands -------------------------------------------------------------------------------

        /// <summary>Takes a <see cref="ScrewTurn"/> — one click, one row, one direction.</summary>
        public ICommand TurnCommand { get; }

        public ICommand UndoCommand { get; }
        public ICommand RezeroCommand { get; }
        public ICommand ZeroAberrationsCommand { get; }
        public ICommand EnableAberrationsCommand { get; }
        public ICommand AutoFillAnglesCommand { get; }
        public ICommand CopyFromAdapterCommand { get; }
        public ICommand CopyToAdapterCommand { get; }
        public ICommand ConfirmCopyToAdapterCommand { get; }
        public ICommand CancelCopyToAdapterCommand { get; }

        /// <summary>Bound directly by the Injected-aberration and Adapter-configuration expanders.</summary>
        public ICameraSimulatorOptions Options => options;

        // ---- Operate: amount + rows -----------------------------------------------------------------

        private double amountPerClick;

        /// <summary>
        /// The one shared amount every row's buttons apply. Strictly positive by contract — direction comes from
        /// the button alone, so a signed amount here could only contradict it.
        /// </summary>
        public double AmountPerClick {
            get => amountPerClick;
            set {
                if (amountPerClick != value) {
                    amountPerClick = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(AxialHintText));
                    RebuildRows();
                    (TurnCommand as ScrewTurnCommand)?.NotifyCanExecuteChanged();
                }
            }
        }

        private double DefaultAmountPerClick =>
            options.SimAdjustmentType == TiltAdjustmentType.StepperMotors ? DefaultStepsPerClick : DefaultTurnsPerClick;

        private bool IsStepper => options.SimAdjustmentType == TiltAdjustmentType.StepperMotors;

        /// <summary>"turns" or "steps" — the unit suffix on the amount box.</summary>
        public string AmountUnit => IsStepper ? "steps" : "turns";

        public string NetUnitLabel => AmountUnit;

        /// <summary>⟳ for screws, "+" for steppers. Buttons speak ROTATION (never ⬆/⬇).</summary>
        public string PositiveGlyph => IsStepper ? "+" : "⟳";

        public string NegativeGlyph => IsStepper ? "−" : "⟲";

        /// <summary>"≈ 262 µm axial" — magnitude feel without arithmetic.</summary>
        public string AxialHintText =>
            UnitMicrons > 0 ? $"≈ {(AmountPerClick * UnitMicrons).ToString("0", CultureInfo.CurrentCulture)} µm axial" : string.Empty;

        private double UnitMicrons => IsStepper ? options.SimStepperStepSizeMicrons : options.SimThreadPitchMicrons;

        private int ScrewCount => options.SimScrewCount == 4 ? 4 : 3;

        public bool IsFourScrew => ScrewCount == 4;

        public bool IsThreeScrew => !IsFourScrew;

        /// <summary>Only the active hardware scale is shown — the other one is inert and invites the wrong edit.</summary>
        public bool ShowThreadPitch => !IsStepper;

        public bool ShowStepperStepSize => IsStepper;

        /// <summary>3-screw adapters get three independent rows and no selector, full stop.</summary>
        public bool ShowMovementModeSelector => IsFourScrew;

        private SimTiltMovementMode movementMode = SimTiltMovementMode.Corner;

        /// <summary>Remembered per session; switching modes never applies anything.</summary>
        public SimTiltMovementMode MovementMode {
            get => movementMode;
            set {
                if (movementMode != value) {
                    movementMode = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsCornerMode));
                    RaisePropertyChanged(nameof(IsSideMode));
                    RaisePropertyChanged(nameof(IsBackfocusMode));
                    RebuildRows();
                }
            }
        }

        // Bound by the segmented radio: a three-way enum needs three two-way bools, since RadioButton.IsChecked
        // cannot bind an enum without a converter and the setter must ignore the uncheck half of the pair.
        public bool IsCornerMode {
            get => MovementMode == SimTiltMovementMode.Corner;
            set { if (value) MovementMode = SimTiltMovementMode.Corner; }
        }

        public bool IsSideMode {
            get => MovementMode == SimTiltMovementMode.Side;
            set { if (value) MovementMode = SimTiltMovementMode.Side; }
        }

        public bool IsBackfocusMode {
            get => MovementMode == SimTiltMovementMode.Backfocus;
            set { if (value) MovementMode = SimTiltMovementMode.Backfocus; }
        }

        public ObservableCollection<SimTiltAdapterRow> Rows { get; } = new ObservableCollection<SimTiltAdapterRow>();

        /// <summary>The one rule that governs every mode — the selector's legend.</summary>
        public string MovementModeLegend =>
            "The screws named in the row turn in the clicked direction; their coupled opposites automatically counter-turn. " +
            "(Backfocus: all four turn the same way — no counter-turn.)";

        // ---- Operate: state strip -------------------------------------------------------------------

        public double TiltAmountMicrons => options.TiltAmountMicrons;
        public double TiltAngleDegrees => options.TiltAngleDegrees;
        public double BackfocusErrorMicrons => options.BackfocusErrorMicrons;

        /// <summary>The loop's finish line: tilt AND backfocus both under 1 µm (UX design §4).</summary>
        public bool IsFlat =>
            Math.Abs(options.TiltAmountMicrons) < FlatThresholdMicrons &&
            Math.Abs(options.BackfocusErrorMicrons) < FlatThresholdMicrons;

        public string FlatText => IsFlat ? "✓ ≈ flat" : string.Empty;

        public string TiltDisplay =>
            $"{options.TiltAmountMicrons.ToString("0.0", CultureInfo.CurrentCulture)} µm @ " +
            $"{TiltCalibrationCalculator.NormalizeAngle(options.TiltAngleDegrees).ToString("0", CultureInfo.CurrentCulture)}°";

        public string BackfocusDisplay => FormatSignedMicrons(options.BackfocusErrorMicrons);

        public bool HasExtremeTilt => options.TiltAmountMicrons > ExtremeTiltMicrons;

        public string ExtremeTiltText => "extreme tilt — expect heavy donuts";

        // ---- Operate: last action + undo ------------------------------------------------------------

        private string lastActionText = string.Empty;

        /// <summary>
        /// "Screw 2 ⟳ 0.75 (S2 ⬇, S4 ⬆) — tilt 12.4 → 3.1 µm". Names the ROTATION applied (input vocabulary)
        /// and the resulting plate MOTION (output vocabulary), which is continuous education about what the
        /// rig-direction setting actually means on this rig.
        /// </summary>
        public string LastActionText {
            get => lastActionText;
            private set {
                lastActionText = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasLastAction));
            }
        }

        public bool HasLastAction => !string.IsNullOrEmpty(LastActionText);

        public bool CanUndo => undoSnapshot.HasValue;

        // ---- Operate: net counters ------------------------------------------------------------------

        /// <summary>
        /// Per-screw accumulated position since the last re-zero, in axial µm. Stored in µm — not turns — so a
        /// mid-session pitch edit re-scales the display instead of corrupting it.
        /// </summary>
        public IReadOnlyList<double> NetAxialMicrons => netAxialMicrons.Take(ScrewCount).ToArray();

        public string NetPositionText {
            get {
                var unit = UnitMicrons;
                var sb = new StringBuilder();
                for (var i = 0; i < ScrewCount; i++) {
                    if (i > 0) sb.Append("  ·  ");
                    var value = unit > 0 ? netAxialMicrons[i] / unit : 0.0;
                    sb.Append(CultureInfo.CurrentCulture, $"{i + 1}: {FormatNet(value)}");
                }
                return sb.ToString();
            }
        }

        private string FormatNet(double value) => IsStepper
            ? value.ToString("+0;−0;0", CultureInfo.CurrentCulture)
            : value.ToString("+0.00;−0.00;0.00", CultureInfo.CurrentCulture);

        public string RezeroTooltip =>
            "Re-bases this counter display only — it does not move screws or change the simulated plane. " +
            "Counters are stored in axial µm, so editing the thread pitch or step size re-scales them rather than corrupting them.";

        // ---- Configuration ---------------------------------------------------------------------------

        public bool IsAdapterConfigured => adapter != null;

        public string ConfigurationBannerText {
            get {
                if (IsAdapterConfigured) return string.Empty;
                if (UnitMicrons <= 0 || options.SimScrewRadiusMillimeters <= 0) {
                    return IsStepper
                        ? "Set stepper step size and screw radius to enable the adapter"
                        : "Set thread pitch and screw radius to enable the adapter";
                }
                return "Set valid screw angles to enable the adapter";
            }
        }

        public bool HasConfigurationBanner => !IsAdapterConfigured;

        /// <summary>True when the banner's one-click fix (Auto-fill evenly) is the right offer.</summary>
        public bool CanAutoFillFromBanner => !IsAdapterConfigured && UnitMicrons > 0 && options.SimScrewRadiusMillimeters > 0;

        public bool ShowAberrationsDisabledBanner => !options.EnableAberrations;

        private bool configurationExpanded;

        /// <summary>Collapsed by default; forced open while the configuration is invalid.</summary>
        public bool IsConfigurationExpanded {
            get => configurationExpanded || !IsAdapterConfigured;
            set {
                if (configurationExpanded != value) {
                    configurationExpanded = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool injectionExpanded;

        public bool IsInjectionExpanded {
            get => injectionExpanded;
            set {
                if (injectionExpanded != value) {
                    injectionExpanded = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>Screws 3 and 4 are derived (+180°) on a coupled 4-screw adapter — rendered dimmed, never edited.</summary>
        public string Screw3AngleDisplay => FormatAngle(options.SimScrew3AngleDegrees);

        public string Screw4AngleDisplay => FormatAngle(options.SimScrew4AngleDegrees);

        public IReadOnlyList<int> ScrewCountOptions { get; } = new[] { 3, 4 };

        /// <summary>
        /// The mechanical reading of <c>SimScrewInwardCurvatureSign</c>. Exposed as words, never as ±1 — the
        /// words are the meaning (UX design §6).
        /// </summary>
        public bool CwMovesAdapterTowardObjective {
            get => TiltScrewGeometry.CwMovesAdapterTowardObjectiveForSign(ResolvedCurvatureSign);
            set => options.SimScrewInwardCurvatureSign = TiltScrewGeometry.CurvatureSignForCwDirection(value);
        }

        private const string TowardObjective = "objective";
        private const string TowardCamera = "camera";

        public IReadOnlyList<string> AdapterDirectionOptions { get; } = new[] { TowardCamera, TowardObjective };

        /// <summary>Bound by the "⟳ tighten moves adapter toward: [camera | objective]" ComboBox.</summary>
        public string SelectedAdapterDirection {
            get => CwMovesAdapterTowardObjective ? TowardObjective : TowardCamera;
            set => CwMovesAdapterTowardObjective = string.Equals(value, TowardObjective, StringComparison.Ordinal);
        }

        private int ResolvedCurvatureSign => options.SimScrewInwardCurvatureSign == 0
            ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign
            : Math.Sign(options.SimScrewInwardCurvatureSign);

        public ObservableCollection<TiltScrewDiagramItem> ScrewDiagramItems { get; }
        public ObservableCollection<TiltScrewConnectionLine> ScrewConnectionLines { get; }

        public string InjectionSummary =>
            $"tilt {TiltDisplay} · BF {BackfocusDisplay}";

        public string ConfigurationSummary {
            get {
                var hardware = UnitMicrons > 0
                    ? $"{UnitMicrons.ToString("0.##", CultureInfo.CurrentCulture)} µm/{(IsStepper ? "step" : "turn")}"
                    : "hardware unset";
                var direction = CwMovesAdapterTowardObjective ? "⟳ → objective" : "⟳ → camera";
                var radius = options.SimScrewRadiusMillimeters > 0
                    ? $"R {options.SimScrewRadiusMillimeters.ToString("0.#", CultureInfo.CurrentCulture)} mm"
                    : "R unset";
                return $"{ScrewCount} screws · {(IsStepper ? "Steppers" : "Screws")} · {hardware} · {radius} · {direction} · {AdapterCoherenceText}";
            }
        }

        // ---- Coherence with the real adapter ---------------------------------------------------------

        public bool CanCopyAdapterSettings => realAdapter != null;

        private IReadOnlyList<string> DifferingAdapterFields() {
            if (realAdapter == null) return Array.Empty<string>();
            var differences = new List<string>();

            if (realAdapter.ScrewCount != ScrewCount) differences.Add("Screw count");
            if (realAdapter.AdjustmentType != options.SimAdjustmentType) differences.Add("Adjustment type");
            if (ResolvedCurvatureSign != ResolveSign(realAdapter.ScrewInwardCurvatureSign)) differences.Add("Adapter direction");

            var simAngles = SimAngles();
            var realAngles = new[] {
                realAdapter.Screw1AngleDegrees, realAdapter.Screw2AngleDegrees,
                realAdapter.Screw3AngleDegrees, realAdapter.Screw4AngleDegrees
            };
            // Only the active screws are compared: a 3-screw rig's stale 4th angle is NaN by convention and must
            // never make the badge lie in either direction.
            for (var i = 0; i < ScrewCount && realAdapter.ScrewCount == ScrewCount; i++) {
                if (!AnglesAgree(simAngles[i], realAngles[i])) differences.Add($"Screw {i + 1} angle");
            }

            var realUnit = realAdapter.AdjustmentType == TiltAdjustmentType.StepperMotors
                ? realAdapter.StepperStepSizeMicrons
                : realAdapter.ThreadPitchMicrons;
            if (!HardwareAgrees(UnitMicrons, realUnit)) differences.Add(IsStepper ? "Stepper step size" : "Thread pitch");
            if (!HardwareAgrees(options.SimScrewRadiusMillimeters, realAdapter.ScrewRadiusMillimeters)) differences.Add("Screw radius");

            return differences;
        }

        private static int ResolveSign(int sign) =>
            sign == 0 ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign : Math.Sign(sign);

        private static bool AnglesAgree(double a, double b) {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.IsNaN(a) && double.IsNaN(b);
            var diff = TiltCalibrationCalculator.NormalizeAngle(a - b);
            if (diff > 180.0) diff = 360.0 - diff;
            return diff <= AngleToleranceDegrees;
        }

        private static bool HardwareAgrees(double sim, double real) {
            if (sim <= 0 || real <= 0) return sim <= 0 && real <= 0;
            return Math.Abs(sim - real) / real <= HardwareToleranceFraction;
        }

        /// <summary>
        /// The inspector guides from the user's REAL calibration while the simulator obeys these Sim* fields, so
        /// the loop only converges when the two agree. Mismatch stays possible (flipping the sim's direction to
        /// test the inspector's glyph robustness is itself a scenario) — but never silent.
        /// </summary>
        public bool AdapterMatchesReal => realAdapter != null && DifferingAdapterFields().Count == 0;

        public string AdapterCoherenceText {
            get {
                if (realAdapter == null) return "adapter settings unavailable";
                return AdapterMatchesReal ? "matches adapter ✓" : "⚠ differs from adapter settings";
            }
        }

        public string AdapterCoherenceTooltip {
            get {
                if (realAdapter == null) {
                    return "The real tilt-adapter settings are not loaded, so this configuration cannot be compared against them.";
                }
                if (AdapterMatchesReal) {
                    return "The simulated adapter's geometry matches the real tilt-adapter settings the Aberration Inspector " +
                           "guides from, so its guidance applies here unchanged.";
                }
                return "These fields differ from the real tilt-adapter settings the Aberration Inspector guides from, so its " +
                       "guidance will not converge here: " + string.Join(", ", DifferingAdapterFields()) + ".";
            }
        }

        private bool copyToAdapterPending;

        public bool IsCopyToAdapterPending {
            get => copyToAdapterPending;
            private set {
                if (copyToAdapterPending != value) {
                    copyToAdapterPending = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>Names exactly what the copy overwrites — this is real, hand-measured calibration.</summary>
        public string CopyToAdapterConfirmText =>
            "Overwrite the real tilt adapter settings — screw count, screw angles, adapter direction, adjustment type, " +
            $"{(IsStepper ? "stepper step size" : "thread pitch")} and screw radius — with this simulated adapter's values? " +
            "The result is marked as a manual calibration.";

        // ---- Turning ---------------------------------------------------------------------------------

        private bool CanTurn(ScrewTurn turn) => IsAdapterConfigured && AmountPerClick > 0;

        private void Turn(ScrewTurn turn) {
            if (!CanTurn(turn)) return;

            var moves = BuildMoves(turn);
            if (moves == null) return;

            var snapshot = Capture();
            AberrationDelta delta;
            try {
                delta = adapter.ApplyMoves(moves);
            } catch (InvalidOperationException ex) {
                // Degenerate (collinear) screw angles. Non-blocking: say so and leave the plane untouched.
                Logger.Warning($"Simulated tilt adapter rejected a move: {ex.Message}");
                LastActionText = "Last: no move — the screw angles are collinear; fix them in Adapter configuration";
                return;
            }

            var tiltBefore = options.TiltAmountMicrons;
            var backfocusBefore = options.BackfocusErrorMicrons;
            var clamped = ApplyDelta(delta);

            for (var i = 0; i < moves.Length; i++) {
                netAxialMicrons[i] += moves[i];
            }

            undoSnapshot = snapshot;
            LastActionText = BuildLastActionText(turn, moves, tiltBefore, backfocusBefore, clamped);
            AfterStateChanged();
        }

        /// <summary>
        /// The per-screw axial move set for the active movement mode (design §3.3). δ is signed by the clicked
        /// button and by nothing else — <see cref="SimulatedTiltAdapter.AxialMicronsForUnits"/> is a pure scale,
        /// deliberately carrying no rig-direction factor (the stored screw angle already carries it).
        /// </summary>
        private double[] BuildMoves(ScrewTurn turn) {
            var n = ScrewCount;
            var delta = adapter.AxialMicronsForUnits(AmountPerClick * turn.RotationSign);
            var moves = new double[n];

            if (n == 3) {
                if (turn.ScrewIndex < 0 || turn.ScrewIndex > 2) return null;
                moves[turn.ScrewIndex] = delta;
                return moves;
            }

            switch (MovementMode) {
                case SimTiltMovementMode.Backfocus:
                    for (var i = 0; i < 4; i++) moves[i] = delta;
                    return moves;

                case SimTiltMovementMode.Side: {
                        if (turn.ScrewIndex < 0 || turn.ScrewIndex > 3) return null;
                        var i = turn.ScrewIndex;
                        moves[i] = delta;
                        moves[(i + 1) % 4] = delta;
                        moves[(i + 2) % 4] = -delta;
                        moves[(i + 3) % 4] = -delta;
                        return moves;
                    }

                default: {
                        if (turn.ScrewIndex < 0 || turn.ScrewIndex > 3) return null;
                        moves[turn.ScrewIndex] = delta;
                        moves[(turn.ScrewIndex + 2) % 4] = -delta;
                        return moves;
                    }
            }
        }

        /// <summary>
        /// Fold a screw-move delta into the simulator's injected aberration (design §3.2). Returns true when a
        /// value had to be clamped to the persisted bounds.
        ///
        /// THE SIGN, stated once: <see cref="AberrationDelta"/> is an honest plane fit in the adapter's RESPONSE
        /// frame. On a σ=−1 rig that frame is a point reflection of the physical one, so the fitted GRADIENT is
        /// frame-invariant (both the screw position and its displacement flip, and their product does not) and
        /// Gx/Gy apply raw — but the constant term has no angle to absorb the reflection, so the physical piston
        /// is <c>PistonDirectionSign · PistonMicrons</c>. This is the same asymmetry the guidance has, where
        /// <see cref="TiltScrewGeometry.SignedTotalAdjustment"/> applies the curvature sign to its backfocus
        /// component only; the two cancel, so backfocus guidance converges on both rig directions. Feeding the
        /// raw fitted constant in here is correct only on σ=+1 rigs and silently backwards on the other half —
        /// pinned by SimulatedTiltAdapterVMTests.BackfocusMove_OnOppositeRigs_MovesBackfocusInOppositeDirections.
        /// </summary>
        private bool ApplyDelta(AberrationDelta delta) {
            var (halfW, halfH) = SensorHalfDimensionsMicrons();

            // Current gradient from the options' (azimuth, amount) form — the same inversion AberrationSurface uses.
            var phi = options.TiltAngleDegrees * Math.PI / 180.0;
            var den = Math.Abs(Math.Cos(phi)) * halfW + Math.Abs(Math.Sin(phi)) * halfH;
            var g = den > 0 ? options.TiltAmountMicrons / den : 0.0;
            var gx = g * Math.Cos(phi) + delta.Gx;
            var gy = g * Math.Sin(phi) + delta.Gy;

            // Back to (azimuth, amount). The amount is a non-negative magnitude by construction — AberrationSurface
            // rejects a negative one, because direction belongs to the azimuth.
            var amount = Math.Abs(gx) * halfW + Math.Abs(gy) * halfH;
            var clamped = amount > AberrationBoundMicrons;
            options.TiltAmountMicrons = Math.Min(amount, AberrationBoundMicrons);
            options.TiltAngleDegrees = TiltCalibrationCalculator.NormalizeAngle(Math.Atan2(gy, gx) * 180.0 / Math.PI);

            var pistonMicrons = adapter.PistonDirectionSign * delta.PistonMicrons;
            if (pistonMicrons != 0.0) {
                // The sensor moving axially both shifts best focus...
                if (options.FocuserStepSizeMicrons > 0) {
                    options.OptimalFocuserPosition += (int)Math.Round(pistonMicrons / options.FocuserStepSizeMicrons);
                }

                // ...and violates the optics' backfocus spacing. The curvature responds to the PISTON, not to any
                // individual screw move, so a corner move (piston 0 by symmetry) correctly leaves it untouched.
                // The proportionality is fixed by being the exact inverse of the inspector's backfocus row: it asks
                // for an axial ΔZ0_phys = -CurvatureAt(R) = -K·R² per screw, which must null K exactly, so
                // ΔK = ΔZ0_phys / R². Equivalently — and this is the independent check that fixes the sign —
                // ScrewInwardCurvatureSign is DEFINED as the sign of the curvature-effect response to a CW turn,
                // and a CW turn gives ΔZ0_phys = σ·(+δ), so ΔBackfocusError must carry the sign of σ.
                var radiusMicrons = options.SimScrewRadiusMillimeters * 1000.0;
                if (radiusMicrons > 0) {
                    var backfocus = options.BackfocusErrorMicrons +
                        pistonMicrons * (halfW * halfW + halfH * halfH) / (radiusMicrons * radiusMicrons);
                    clamped |= Math.Abs(backfocus) > AberrationBoundMicrons;
                    options.BackfocusErrorMicrons = Math.Clamp(backfocus, -AberrationBoundMicrons, AberrationBoundMicrons);
                }
            }

            return clamped;
        }

        private (double halfWidth, double halfHeight) SensorHalfDimensionsMicrons() {
            var sensor = SensorRegistry.Get(options.SensorModel);
            return (sensor.Width * sensor.PixelSizeMicrons / 2.0, sensor.Height * sensor.PixelSizeMicrons / 2.0);
        }

        // ---- Feedback --------------------------------------------------------------------------------

        private string BuildLastActionText(ScrewTurn turn, double[] moves, double tiltBefore, double backfocusBefore, bool clamped) {
            var sb = new StringBuilder("Last: ");
            sb.Append(RowNameFor(turn.ScrewIndex));
            sb.Append(' ').Append(FormatRotation(turn.RotationSign, AmountPerClick));

            var motions = MotionSummary(moves);
            if (!string.IsNullOrEmpty(motions)) {
                sb.Append(" (").Append(motions).Append(')');
            }

            var changes = new List<string>();
            if (Math.Abs(options.TiltAmountMicrons - tiltBefore) >= 0.05) {
                changes.Add($"tilt {tiltBefore.ToString("0.0", CultureInfo.CurrentCulture)} → " +
                            $"{options.TiltAmountMicrons.ToString("0.0", CultureInfo.CurrentCulture)} µm");
            }
            if (Math.Abs(options.BackfocusErrorMicrons - backfocusBefore) >= 0.05) {
                changes.Add($"BF {FormatSignedMicrons(backfocusBefore)} → {FormatSignedMicrons(options.BackfocusErrorMicrons)}");
            }
            sb.Append(" — ").Append(changes.Count > 0 ? string.Join(" · ", changes) : "no visible change");
            if (clamped) sb.Append(" (clamped)");
            return sb.ToString();
        }

        /// <summary>The row's name without its angle — the last-action line is a sentence, not a row label.</summary>
        private string RowNameFor(int index) {
            if (ScrewCount == 3) return $"Screw {index + 1}";
            return MovementMode switch {
                SimTiltMovementMode.Backfocus => "All screws 1–4",
                SimTiltMovementMode.Side => $"Side {index + 1}+{(index + 1) % 4 + 1}",
                _ => $"Screw {index + 1}"
            };
        }

        /// <summary>"S2 ⬇, S4 ⬆" — where the plate actually goes. Output vocabulary only; never a rotation.</summary>
        private string MotionSummary(double[] moves) {
            var moved = Enumerable.Range(0, moves.Length).Where(i => moves[i] != 0.0).ToArray();
            if (moved.Length == 0) return string.Empty;
            if (moved.Length == ScrewCount && moves.All(m => m == moves[0])) {
                return "all " + MotionArrow(moves[0]);
            }
            return string.Join(", ", moved.Select(i => $"S{i + 1} {MotionArrow(moves[i])}"));
        }

        /// <summary>
        /// ⬆ = that corner moves toward the objective. Same rule the inspector's arrows use: a CW-positive
        /// rotation produces motion toward the objective exactly when the rig direction says it does.
        /// </summary>
        private string MotionArrow(double move) =>
            TiltScrewGeometry.CwMovesAdapterTowardObjectiveForSign(ResolvedCurvatureSign) == (move > 0) ? "⬆" : "⬇";

        private string FormatRotation(int rotationSign, double amount) => IsStepper
            ? $"{(rotationSign > 0 ? "+" : "−")}{amount.ToString("0.##", CultureInfo.CurrentCulture)} steps"
            : $"{(rotationSign > 0 ? "⟳" : "⟲")} {amount.ToString("0.##", CultureInfo.CurrentCulture)}";

        private static string FormatSignedMicrons(double value) =>
            value.ToString("+0.0;−0.0;0.0", CultureInfo.CurrentCulture) + " µm";

        private static string FormatAngle(double value) =>
            double.IsNaN(value) ? "—" : value.ToString("0.0", CultureInfo.CurrentCulture) + "°";

        // ---- Undo / re-zero --------------------------------------------------------------------------

        private readonly struct PanelSnapshot {

            public PanelSnapshot(double tiltAmount, double tiltAngle, double backfocus, int focuserPosition,
                    double[] net, string lastAction) {
                TiltAmountMicrons = tiltAmount;
                TiltAngleDegrees = tiltAngle;
                BackfocusErrorMicrons = backfocus;
                OptimalFocuserPosition = focuserPosition;
                NetAxialMicrons = net;
                LastActionText = lastAction;
            }

            public double TiltAmountMicrons { get; }
            public double TiltAngleDegrees { get; }
            public double BackfocusErrorMicrons { get; }
            public int OptimalFocuserPosition { get; }
            public double[] NetAxialMicrons { get; }
            public string LastActionText { get; }
        }

        private PanelSnapshot Capture() => new PanelSnapshot(
            options.TiltAmountMicrons, options.TiltAngleDegrees, options.BackfocusErrorMicrons,
            options.OptimalFocuserPosition, (double[])netAxialMicrons.Clone(), LastActionText);

        /// <summary>Single level: misclicks in a rapid loop must be free, but this is not an edit history.</summary>
        private void Undo() {
            if (!undoSnapshot.HasValue) return;
            var s = undoSnapshot.Value;
            options.TiltAmountMicrons = s.TiltAmountMicrons;
            options.TiltAngleDegrees = s.TiltAngleDegrees;
            options.BackfocusErrorMicrons = s.BackfocusErrorMicrons;
            options.OptimalFocuserPosition = s.OptimalFocuserPosition;
            Array.Copy(s.NetAxialMicrons, netAxialMicrons, netAxialMicrons.Length);
            undoSnapshot = null;
            LastActionText = s.LastActionText;
            AfterStateChanged();
        }

        /// <summary>Re-bases the counter display only — never the plane. Hence "Re-zero", not "Reset".</summary>
        private void Rezero() {
            Array.Clear(netAxialMicrons, 0, netAxialMicrons.Length);
            RaisePropertyChanged(nameof(NetAxialMicrons));
            RaisePropertyChanged(nameof(NetPositionText));
        }

        private void ZeroAberrations() {
            options.TiltAmountMicrons = 0.0;
            options.TiltAngleDegrees = 0.0;
            options.BackfocusErrorMicrons = 0.0;
        }

        private void AutoFillAngles() {
            derivingAngles = true;
            try {
                if (ScrewCount == 3) {
                    options.SimScrew1AngleDegrees = 0.0;
                    options.SimScrew2AngleDegrees = 120.0;
                    options.SimScrew3AngleDegrees = 240.0;
                    options.SimScrew4AngleDegrees = double.NaN;
                } else {
                    options.SimScrew1AngleDegrees = 45.0;
                    options.SimScrew2AngleDegrees = 135.0;
                    options.SimScrew3AngleDegrees = 225.0;
                    options.SimScrew4AngleDegrees = 315.0;
                }
            } finally {
                derivingAngles = false;
            }
            RebuildAll();
        }

        // ---- Copy from / to the real adapter ---------------------------------------------------------

        private void CopyFromAdapter() {
            if (realAdapter == null) return;
            derivingAngles = true;
            try {
                options.SimScrewCount = realAdapter.ScrewCount == 4 ? 4 : 3;
                options.SimScrew1AngleDegrees = realAdapter.Screw1AngleDegrees;
                options.SimScrew2AngleDegrees = realAdapter.Screw2AngleDegrees;
                options.SimScrew3AngleDegrees = realAdapter.Screw3AngleDegrees;
                options.SimScrew4AngleDegrees = options.SimScrewCount == 4 ? realAdapter.Screw4AngleDegrees : double.NaN;
                options.SimScrewInwardCurvatureSign = ResolveSign(realAdapter.ScrewInwardCurvatureSign);
                options.SimAdjustmentType = realAdapter.AdjustmentType;
                if (realAdapter.ThreadPitchMicrons > 0) options.SimThreadPitchMicrons = realAdapter.ThreadPitchMicrons;
                if (realAdapter.StepperStepSizeMicrons > 0) options.SimStepperStepSizeMicrons = realAdapter.StepperStepSizeMicrons;
                if (realAdapter.ScrewRadiusMillimeters > 0) options.SimScrewRadiusMillimeters = realAdapter.ScrewRadiusMillimeters;
            } finally {
                derivingAngles = false;
            }
            RebuildAll();
        }

        /// <summary>
        /// Writes the sim geometry over the user's real calibration. Confirmed, because that calibration may be a
        /// wizard run the user cannot cheaply repeat. Mirrors the wizard's manual-entry path: same persisted
        /// state, tagged manual, with the stale wizard measurements retired — no angle conversion is applied
        /// because the Sim* angles are already stored in the response convention ITiltAdapterOptions holds.
        /// </summary>
        private void ConfirmCopyToAdapter() {
            IsCopyToAdapterPending = false;
            if (realAdapter == null) return;

            realAdapter.ScrewCount = ScrewCount;
            realAdapter.Screw1AngleDegrees = options.SimScrew1AngleDegrees;
            realAdapter.Screw2AngleDegrees = options.SimScrew2AngleDegrees;
            realAdapter.Screw3AngleDegrees = options.SimScrew3AngleDegrees;
            realAdapter.Screw4AngleDegrees = ScrewCount == 4 ? options.SimScrew4AngleDegrees : double.NaN;
            realAdapter.ScrewInwardCurvatureSign = ResolvedCurvatureSign;
            realAdapter.AdjustmentType = options.SimAdjustmentType;
            realAdapter.ThreadPitchMicrons = options.SimThreadPitchMicrons;
            realAdapter.StepperStepSizeMicrons = options.SimStepperStepSizeMicrons;
            realAdapter.ScrewRadiusMillimeters = options.SimScrewRadiusMillimeters;

            // A device preset locks the hardware fields to the preset's values; we just wrote our own, so the
            // adapter is "Manual" by definition now.
            realAdapter.DeviceName = TiltAdapterDevicePreset.ManualName;
            realAdapter.CalibratedScrewCount = ScrewCount;
            realAdapter.IsCalibrated = true;
            realAdapter.ScrewInwardCurvatureSignIsMeasured = false;
            realAdapter.CalibrationIsManual = true;
            // Retire the previous adapter's wizard measurements, so the inspector's pitch-mismatch warning cannot
            // compare the values we just wrote against a stale measurement (the manual-entry path does the same).
            realAdapter.LastMeasuredThreadPitchMicrons = -1;
            realAdapter.LastMeasuredStepperStepSizeMicrons = -1;

            RaiseCoherenceChanged();
        }

        // ---- Rebuilds --------------------------------------------------------------------------------

        private void OnOptionsChanged(object sender, PropertyChangedEventArgs e) {
            // A profile switch broadcasts an empty name (BaseINPC.RaiseAllPropertiesChanged, from
            // CameraSimulatorOptions.ProfileService_ProfileChanged) — every field below may have changed at once,
            // and none of the cases would match. Rebuild rather than silently keep the old profile's panel.
            if (string.IsNullOrEmpty(e.PropertyName)) {
                RebuildAll();
                return;
            }

            switch (e.PropertyName) {
                case nameof(ICameraSimulatorOptions.SimScrewCount):
                    DeriveOppositeAngles();
                    RebuildAll();
                    break;

                case nameof(ICameraSimulatorOptions.SimScrew1AngleDegrees):
                case nameof(ICameraSimulatorOptions.SimScrew2AngleDegrees):
                    DeriveOppositeAngles();
                    RebuildAll();
                    break;

                case nameof(ICameraSimulatorOptions.SimScrew3AngleDegrees):
                case nameof(ICameraSimulatorOptions.SimScrew4AngleDegrees):
                case nameof(ICameraSimulatorOptions.SimScrewInwardCurvatureSign):
                case nameof(ICameraSimulatorOptions.SimScrewRadiusMillimeters):
                    RebuildAll();
                    break;

                case nameof(ICameraSimulatorOptions.SimAdjustmentType):
                    // The unit changed meaning entirely; 0.25 steps is not a sane granularity.
                    amountPerClick = DefaultAmountPerClick;
                    RaisePropertyChanged(nameof(AmountPerClick));
                    RebuildAll();
                    break;

                case nameof(ICameraSimulatorOptions.SimThreadPitchMicrons):
                case nameof(ICameraSimulatorOptions.SimStepperStepSizeMicrons):
                    RebuildAll();
                    break;

                case nameof(ICameraSimulatorOptions.TiltAmountMicrons):
                case nameof(ICameraSimulatorOptions.TiltAngleDegrees):
                case nameof(ICameraSimulatorOptions.BackfocusErrorMicrons):
                    AfterStateChanged();
                    break;

                case nameof(ICameraSimulatorOptions.EnableAberrations):
                    RaisePropertyChanged(nameof(ShowAberrationsDisabledBanner));
                    break;
            }
        }

        private void OnRealAdapterChanged(object sender, PropertyChangedEventArgs e) => RaiseCoherenceChanged();

        /// <summary>
        /// Opposite screws on a coupled 4-screw adapter are 180° apart by construction, so screws 3 and 4 are
        /// derived rather than entered — that kills an entire class of configuration typos.
        /// </summary>
        private void DeriveOppositeAngles() {
            if (derivingAngles) return;
            derivingAngles = true;
            try {
                if (ScrewCount == 4) {
                    options.SimScrew3AngleDegrees = Opposite(options.SimScrew1AngleDegrees);
                    options.SimScrew4AngleDegrees = Opposite(options.SimScrew2AngleDegrees);
                } else {
                    // Mirrors the existing convention: a 3-screw rig's 4th angle is NaN, never a stale value.
                    options.SimScrew4AngleDegrees = double.NaN;
                }
            } finally {
                derivingAngles = false;
            }
        }

        private static double Opposite(double angle) =>
            double.IsNaN(angle) ? double.NaN : TiltCalibrationCalculator.NormalizeAngle(angle + 180.0);

        private double[] SimAngles() => new[] {
            options.SimScrew1AngleDegrees, options.SimScrew2AngleDegrees,
            options.SimScrew3AngleDegrees, options.SimScrew4AngleDegrees
        };

        private void RebuildAll() {
            RebuildAdapter();
            RebuildRows();
            RebuildDiagram();
            RaiseAllPropertiesChanged();
            (TurnCommand as ScrewTurnCommand)?.NotifyCanExecuteChanged();
            (CopyFromAdapterCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (CopyToAdapterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }

        private void RebuildAdapter() {
            var n = ScrewCount;
            var angles = SimAngles().Take(n).ToArray();
            var unit = UnitMicrons;
            var radiusMicrons = options.SimScrewRadiusMillimeters * 1000.0;

            if (angles.Any(a => !double.IsFinite(a)) || unit <= 0 || radiusMicrons <= 0) {
                adapter = null;
                return;
            }
            adapter = new SimulatedTiltAdapter(angles, radiusMicrons, unit, options.SimScrewInwardCurvatureSign);
        }

        private void AfterStateChanged() {
            RaisePropertyChanged(nameof(TiltAmountMicrons));
            RaisePropertyChanged(nameof(TiltAngleDegrees));
            RaisePropertyChanged(nameof(BackfocusErrorMicrons));
            RaisePropertyChanged(nameof(TiltDisplay));
            RaisePropertyChanged(nameof(BackfocusDisplay));
            RaisePropertyChanged(nameof(IsFlat));
            RaisePropertyChanged(nameof(FlatText));
            RaisePropertyChanged(nameof(HasExtremeTilt));
            RaisePropertyChanged(nameof(InjectionSummary));
            RaisePropertyChanged(nameof(NetAxialMicrons));
            RaisePropertyChanged(nameof(NetPositionText));
            RaisePropertyChanged(nameof(CanUndo));
            (UndoCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }

        private void RaiseCoherenceChanged() {
            RaisePropertyChanged(nameof(AdapterMatchesReal));
            RaisePropertyChanged(nameof(AdapterCoherenceText));
            RaisePropertyChanged(nameof(AdapterCoherenceTooltip));
            RaisePropertyChanged(nameof(ConfigurationSummary));
        }

        private void RebuildRows() {
            Rows.Clear();
            var n = ScrewCount;
            var angles = SimAngles();

            if (n == 3) {
                for (var i = 0; i < 3; i++) {
                    Rows.Add(BuildRow(i, $"Screw {i + 1} · {FormatAngle(angles[i])}", string.Empty));
                }
                return;
            }

            switch (MovementMode) {
                case SimTiltMovementMode.Backfocus:
                    Rows.Add(BuildRow(0, "All screws 1–4", string.Empty));
                    break;

                case SimTiltMovementMode.Side:
                    for (var i = 0; i < 4; i++) {
                        var partner = (i + 1) % 4;
                        var opposing = $"{(i + 2) % 4 + 1}+{(i + 3) % 4 + 1} oppose";
                        Rows.Add(BuildRow(i, $"Side {i + 1}+{partner + 1} · {SideName(angles[i], angles[partner])}", opposing));
                    }
                    break;

                default:
                    for (var i = 0; i < 4; i++) {
                        Rows.Add(BuildRow(i, $"Screw {i + 1} · {FormatAngle(angles[i])}", $"{(i + 2) % 4 + 1} opposes"));
                    }
                    break;
            }
        }

        private SimTiltAdapterRow BuildRow(int index, string label, string couplingText) =>
            new SimTiltAdapterRow(index, label, couplingText, Tooltip(index, +1), Tooltip(index, -1));

        /// <summary>
        /// The button's tooltip: it opens with the ROTATION the button applies and then states the MOTION that
        /// produces on this rig, computed live from the rig-direction setting. Every click therefore teaches the
        /// σ mapping, which is the whole mitigation for direction confusion (UX design §8).
        /// </summary>
        private string Tooltip(int index, int rotationSign) {
            var rotation = FormatRotation(rotationSign, AmountPerClick) + (IsStepper ? string.Empty : " turns");
            var named = MotionWords(rotationSign);
            var opposed = MotionWords(-rotationSign);

            if (ScrewCount == 3) {
                return $"{rotation} — screw {index + 1} moves {named}; tilt tips accordingly.";
            }

            switch (MovementMode) {
                case SimTiltMovementMode.Backfocus:
                    return $"{rotation} — all four screws move {named}: a pure backfocus/curvature change, tilt untouched.";

                case SimTiltMovementMode.Side: {
                        var partner = (index + 1) % 4;
                        return $"{rotation} — screws {index + 1}+{partner + 1} move {named}; " +
                               $"screws {(index + 2) % 4 + 1}+{(index + 3) % 4 + 1} counter-turn and move {opposed}. Backfocus untouched.";
                    }

                default:
                    return $"{rotation} — screw {index + 1} moves {named}; " +
                           $"screw {(index + 2) % 4 + 1} counter-turns and moves {opposed}. Backfocus untouched.";
            }
        }

        private string MotionWords(int rotationSign) {
            var arrow = MotionArrow(rotationSign);
            return arrow == "⬆" ? "toward the objective (⬆)" : "toward the camera (⬇)";
        }

        /// <summary>
        /// Derived from the pair's mean angle in image space, so the hint stays correct under the mirrored
        /// configurations a diagonal or a flip in the optical train produces.
        /// </summary>
        private static string SideName(double a, double b) {
            if (!double.IsFinite(a) || !double.IsFinite(b)) return "—";
            var ra = a * Math.PI / 180.0;
            var rb = b * Math.PI / 180.0;
            var mean = TiltCalibrationCalculator.NormalizeAngle(
                Math.Atan2(Math.Sin(ra) + Math.Sin(rb), Math.Cos(ra) + Math.Cos(rb)) * 180.0 / Math.PI);
            if (mean < 45.0 || mean >= 315.0) return "top";
            if (mean < 135.0) return "right";
            if (mean < 225.0) return "bottom";
            return "left";
        }

        /// <summary>Renders live from the entered angles — the user sees screw 2 land where they typed it.</summary>
        private void RebuildDiagram() {
            ScrewDiagramItems.Clear();
            ScrewConnectionLines.Clear();

            var n = ScrewCount;
            var angles = SimAngles();
            if (angles.Take(n).Any(a => !double.IsFinite(a))) return;

            var centers = new (double cx, double cy)[n];
            for (var i = 0; i < n; i++) {
                var theta = angles[i] * Math.PI / 180.0;
                var cx = 100 + 75 * Math.Sin(theta);
                var cy = 100 - 75 * Math.Cos(theta);
                centers[i] = (cx, cy);
                ScrewDiagramItems.Add(new TiltScrewDiagramItem { X = cx - 12, Y = cy - 12, Number = i + 1, AngleDegrees = angles[i] });
            }
            for (var i = 0; i < n; i++) {
                var j = (i + 1) % n;
                ScrewConnectionLines.Add(new TiltScrewConnectionLine {
                    X1 = centers[i].cx,
                    Y1 = centers[i].cy,
                    X2 = centers[j].cx,
                    Y2 = centers[j].cy
                });
            }
        }
    }
}
