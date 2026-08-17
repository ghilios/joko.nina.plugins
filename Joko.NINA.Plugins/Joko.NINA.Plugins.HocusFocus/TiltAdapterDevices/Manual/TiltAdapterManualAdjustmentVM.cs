#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual {

    /// <summary>Which of the panel's two entry modes is showing.</summary>
    public enum ManualAdjustmentMode {

        /// <summary>Pick a pad cell + direction + amount; one click sends exactly one generator move.</summary>
        SingleMove,

        /// <summary>Type absolute per-motor end positions; the delta is decomposed and reviewed in the approval dialog.</summary>
        TargetPositions
    }

    /// <summary>How close a predicted position sits to the ends of the travel window.</summary>
    public enum ManualLimitSeverity {
        None,
        Near,
        Blocking
    }

    /// <summary>
    /// One cell of the single-move preview's 2×2 predicted-position grid. Read-only projection rebuilt on
    /// every input or position change.
    /// </summary>
    public sealed class ManualPreviewCell {

        public ManualPreviewCell(TiltAdapterCorner corner, string valueText, bool isMoving, string limitTag, ManualLimitSeverity severity, string tooltip) {
            Corner = corner;
            ValueText = valueText ?? string.Empty;
            IsMoving = isMoving;
            LimitTag = limitTag ?? string.Empty;
            Severity = severity;
            Tooltip = tooltip ?? string.Empty;
        }

        public TiltAdapterCorner Corner { get; }

        public string Heading => Corner.CellHeading;

        /// <summary>"512 → 512" when unchanged, "480 → 500 (+20)" when this motor moves, "unknown → unknown" when positions are unknown.</summary>
        public string ValueText { get; }

        public bool IsMoving { get; }

        /// <summary>"near max" / "near 0" / "below 0" / "over max", or empty. Text, never colour alone.</summary>
        public string LimitTag { get; }

        public bool HasLimitTag => !string.IsNullOrEmpty(LimitTag);

        public ManualLimitSeverity Severity { get; }

        public bool IsBlocking => Severity == ManualLimitSeverity.Blocking;

        public string Tooltip { get; }
    }

    /// <summary>
    /// One cell of the 3×3 pad, bound by the UI. A per-VM wrapper rather than a flag on
    /// <see cref="ManualAdjustmentTarget"/>, because that table is a shared static — selection is view state
    /// and must not be written into it.
    /// </summary>
    public sealed class ManualPadItem : BaseINPC {
        private readonly Action<ManualPadItem> onSelected;
        private bool isSelected;

        internal ManualPadItem(ManualAdjustmentTarget target, Action<ManualPadItem> onSelected) {
            Target = target;
            this.onSelected = onSelected;
        }

        public ManualAdjustmentTarget Target { get; }

        public bool IsSelected {
            get => isSelected;
            set {
                if (isSelected != value) {
                    isSelected = value;
                    RaisePropertyChanged();
                    if (value) {
                        onSelected?.Invoke(this);
                    }
                }
            }
        }

        /// <summary>Sets selection without re-entering the VM's selection handler (used when another cell wins).</summary>
        internal void SetSelectedSilently(bool value) {
            if (isSelected != value) {
                isSelected = value;
                RaisePropertyChanged(nameof(IsSelected));
            }
        }
    }

    /// <summary>
    /// One editable cell of the target-positions 2×2 grid: the live "now" value, the user's absolute target,
    /// the resulting delta, and — when the request contains twist the adapter cannot make — the position this
    /// motor will actually reach.
    /// </summary>
    public sealed class ManualTargetCell : BaseINPC {
        private readonly Action onTargetChanged;
        private string targetText = string.Empty;
        private string nowText = "unknown";
        private string deltaText = string.Empty;
        private string willReachText = string.Empty;
        private bool hasError;

        internal ManualTargetCell(TiltAdapterCorner corner, Action onTargetChanged) {
            Corner = corner;
            this.onTargetChanged = onTargetChanged;
        }

        public TiltAdapterCorner Corner { get; }

        public string Heading => Corner.CellHeading;

        /// <summary>"Wizard screw 3" — the third index space, kept visible so this grid joins up with the approval dialog's rows.</summary>
        public string WizardScrewCaption => string.Format(CultureInfo.InvariantCulture, "Wizard screw {0}", Corner.WizardScrewNumber);

        /// <summary>The user's absolute target, as typed. Parsed by the VM so it can own the error copy.</summary>
        public string TargetText {
            get => targetText;
            set {
                if (targetText != value) {
                    targetText = value ?? string.Empty;
                    RaisePropertyChanged();
                    onTargetChanged?.Invoke();
                }
            }
        }

        /// <summary>Sets the target without re-entering the VM's recompute (used by prefill/reset, which recompute once at the end).</summary>
        internal void SetTargetSilently(string value) {
            targetText = value ?? string.Empty;
            RaisePropertyChanged(nameof(TargetText));
        }

        /// <summary>"now 512" — live, refreshed by the position poll; never overwrites <see cref="TargetText"/>.</summary>
        public string NowText {
            get => nowText;
            internal set { if (nowText != value) { nowText = value; RaisePropertyChanged(); } }
        }

        public string DeltaText {
            get => deltaText;
            internal set { if (deltaText != value) { deltaText = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasDelta)); } }
        }

        public bool HasDelta => !string.IsNullOrEmpty(deltaText);

        /// <summary>"will reach 546" — only populated when the request contains unreachable twist.</summary>
        public string WillReachText {
            get => willReachText;
            internal set { if (willReachText != value) { willReachText = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasWillReach)); } }
        }

        public bool HasWillReach => !string.IsNullOrEmpty(willReachText);

        public bool HasError {
            get => hasError;
            internal set { if (hasError != value) { hasError = value; RaisePropertyChanged(); } }
        }
    }

    /// <summary>
    /// The Tilt Adapter Wizard's "Manual adjustment" panel: hand control of a connected 4-corner coupled
    /// motorized adapter, in two modes.
    ///
    /// <para><b>Single move</b> sends exactly one generator command — one pad cell is one axis plus a sign (see
    /// <see cref="ManualAdjustmentTarget"/>). It deliberately does NOT run the planner and never lets the
    /// controller prepend a backfocus bias: "a single adjustment" must mean precisely the one move previewed.
    /// A move that would leave the travel window is blocked with a reason instead, and the user lifts with an
    /// explicit All move.</para>
    ///
    /// <para><b>Target positions</b> takes absolute per-motor end positions, subtracts the live positions, and
    /// runs the result through the same <see cref="TiltDevicePlanPreviewBuilder"/> and approval dialog the
    /// Aberration Inspector's Automatic Adjustment uses — so the decomposition, ordering, residual grid, and
    /// warnings are literally the same code and the same UI.</para>
    ///
    /// <para>Neither mode requires a fitted sensor model, a device-linked calibration, or
    /// <c>CalibrationIsReliable</c>: this is raw hardware control, not a correction derived from a
    /// measurement, so Automatic Adjustment's gates do not apply.</para>
    ///
    /// <para>All state here is session-scoped on purpose — nothing this panel remembers is worth a persisted
    /// per-profile option (and would then owe a control in the global options screen).</para>
    /// </summary>
    public sealed class TiltAdapterManualAdjustmentVM : BaseINPC, IDisposable {

        /// <summary>Operation-lease name; surfaces in the connection status line as "Connected on COM7 — Manual Adjustment".</summary>
        internal const string OperationName = "Manual Adjustment";

        /// <summary>Seconds attributed to one command for the duration estimate — the same rate <see cref="TiltMovePlanner"/> defaults to.</summary>
        internal const double PerMoveSeconds = 10.0;

        /// <summary>Fraction of the travel window within which a predicted position earns a "near 0" / "near max" tag.</summary>
        internal const double NearLimitFraction = 0.05;

        internal const string CautionCopy =
            "Moves are physical and the device remembers every move (stored in EEPROM). There is no automatic undo.";

        internal const string NotConnectedCopy = "Connect the device above to make adjustments.";

        internal const string NothingToMoveCopy = "Targets match the current positions — nothing to move.";

        private readonly ITiltAdapterOptions options;
        private readonly TiltDeviceConnectionService connectionService;
        private readonly IApplicationDispatcher applicationDispatcher;
        private readonly Func<Func<bool, bool, TiltDevicePlanPreview>, double, Task<TiltDeviceAdjustmentChoice>> showAdjustmentPromptAsync;

        private readonly RelayCommand sendCommand;
        private readonly RelayCommand reviewCommand;
        private readonly RelayCommand stopCommand;

        private bool userExpanded;
        private ManualAdjustmentMode mode = ManualAdjustmentMode.SingleMove;

        private ManualAdjustmentTarget selectedTarget;
        private bool positiveDirection = true;
        private string amountText = "10";

        private bool isSending;
        private string progressText = string.Empty;
        private int remainingMoveCount;
        private string successText = string.Empty;
        private string lastSentSummary = string.Empty;
        private string resultTitle = string.Empty;
        private string resultBody = string.Empty;
        private bool resultIsError;
        private CancellationTokenSource sendCts;

        private IReadOnlyList<int> lastKnownPositions;

        public TiltAdapterManualAdjustmentVM(
            ITiltAdapterOptions options,
            TiltDeviceConnectionService connectionService,
            IApplicationDispatcher applicationDispatcher,
            Func<Func<bool, bool, TiltDevicePlanPreview>, double, Task<TiltDeviceAdjustmentChoice>> showAdjustmentPromptAsync) {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.connectionService = connectionService;
            this.applicationDispatcher = applicationDispatcher;
            this.showAdjustmentPromptAsync = showAdjustmentPromptAsync ?? throw new ArgumentNullException(nameof(showAdjustmentPromptAsync));

            TargetCells = TiltAdapterCorner.InDisplayOrder
                .Select(c => new ManualTargetCell(c, OnTargetCellChanged))
                .ToList();
            // Row-major over the 3×3 pad, which is the order ManualAdjustmentTarget.All is declared in.
            PadItems = ManualAdjustmentTarget.All
                .Select(t => new ManualPadItem(t, OnPadItemSelected))
                .ToList();

            sendCommand = new RelayCommand(() => RunGuarded(SendSingleMoveAsync, "send"), () => CanSend);
            reviewCommand = new RelayCommand(() => RunGuarded(ReviewTargetsAsync, "review"), () => CanReview);
            stopCommand = new RelayCommand(RequestStop, () => CanStop);
            ResetToCurrentCommand = new RelayCommand(ResetTargetsToCurrent);
            DismissResultCommand = new RelayCommand(DismissResult);

            if (this.connectionService != null) {
                this.connectionService.PropertyChanged += OnConnectionServiceChanged;
            }
            this.options.PropertyChanged += OnOptionsChanged;

            CaptureCurrentPositions();
            PrefillTargetsFromCurrent();
            RefreshAll();
        }

        #region Wiring

        private void OnConnectionServiceChanged(object sender, PropertyChangedEventArgs e) {
            // Fires from the 5 s poll thread and from move completions. Post (never blocking-dispatch): the
            // poll path is one the UI thread can transitively wait on.
            OnUIThread(() => {
                CaptureCurrentPositions();
                if (!IsDeviceConnected) {
                    // Targets are meaningless against a device that is gone; the next connect re-prefills them.
                    ClearTargets();
                }
                RefreshAll();
            });
        }

        private void OnOptionsChanged(object sender, PropertyChangedEventArgs e) {
            // Travel window / per-command cap / step size all feed the preview, the split count and the limit
            // checks, so any of them changing has to re-derive the whole panel.
            OnUIThread(RefreshAll);
        }

        private void OnUIThread(Action action) {
            if (applicationDispatcher == null) {
                action();
                return;
            }
            applicationDispatcher.PostSynchronizationContext(action);
        }

        public void Dispose() {
            if (connectionService != null) {
                connectionService.PropertyChanged -= OnConnectionServiceChanged;
            }
            options.PropertyChanged -= OnOptionsChanged;
            sendCts?.Dispose();
            sendCts = null;
        }

        #endregion Wiring

        #region Device / options state

        public bool IsDeviceConnected => connectionService?.Connected ?? false;

        private ITiltMotionController Controller => connectionService?.Controller;

        /// <summary>True only while another surface (a wizard run, Automatic Adjustment) holds the device lease.</summary>
        public bool IsDeviceBusyElsewhere => !isSending && (connectionService?.IsOperationActive ?? false);

        public bool PositionsKnown => lastKnownPositions != null && lastKnownPositions.Count >= 4;

        private int MaxExcursionSteps => options.TiltDeviceMaxExcursionSteps;

        private int MaxStepsPerCommand => Math.Max(1, options.TiltDeviceMaxStepsPerCommand);

        private double UnitMicrons {
            get {
                double unit = options.AdjustmentType == TiltAdjustmentType.StepperMotors
                    ? options.StepperStepSizeMicrons
                    : options.ThreadPitchMicrons;
                return unit > 0 ? unit : 0.0;
            }
        }

        private bool HasUnitMicrons => UnitMicrons > 0;

        private void CaptureCurrentPositions() {
            var svc = connectionService;
            if (svc == null || !svc.Connected || !svc.PositionsKnown) {
                lastKnownPositions = null;
                return;
            }
            var positions = svc.CurrentPositions;
            lastKnownPositions = (positions != null && positions.Count >= 4) ? positions : null;
        }

        #endregion Device / options state

        #region Expander + summary

        /// <summary>
        /// Collapsed by default and two-way bound so a user's toggle sticks for the session — but force-open
        /// while a send is running or an undismissed failure is on screen, so the panel can never hide the
        /// consequence of something it started.
        /// </summary>
        public bool IsExpanded {
            get => userExpanded || isSending || ResultVisible;
            set {
                if (userExpanded != value) {
                    userExpanded = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>Dim one-liner beside the header, so the collapsed state still says something useful.</summary>
        public string SummaryText {
            get {
                if (isSending) {
                    return progressText;
                }
                if (ResultVisible && resultIsError) {
                    return "Last move failed — see details";
                }
                if (IsDeviceBusyElsewhere) {
                    string op = connectionService?.CurrentOperationName;
                    return string.IsNullOrEmpty(op) ? "Device busy" : string.Format(CultureInfo.InvariantCulture, "Device busy — {0}", op);
                }
                if (!IsDeviceConnected) {
                    return "Connect to enable";
                }
                // Deliberately shorter than the body's success line: the header is what a COLLAPSED panel shows,
                // and when the panel is open the two sit inches apart, where the full sentence twice reads as a
                // stutter.
                if (!string.IsNullOrEmpty(lastSentSummary)) {
                    return lastSentSummary;
                }
                return "Nudge a corner or set target positions";
            }
        }

        public string CautionText => CautionCopy;

        public string NotConnectedText => NotConnectedCopy;

        #endregion Expander + summary

        #region Mode

        public ManualAdjustmentMode Mode {
            get => mode;
            set {
                if (mode != value) {
                    mode = value;
                    if (mode == ManualAdjustmentMode.TargetPositions) {
                        PrefillTargetsFromCurrent();
                    }
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsSingleMoveMode));
                    RaisePropertyChanged(nameof(IsTargetPositionsMode));
                    RefreshAll();
                }
            }
        }

        // Two settable bools rather than an enum converter: RadioButton.IsChecked binds to these directly, and
        // WPF pushes false to the losing option before true to the winning one, so ignore the false edge.
        public bool IsSingleMoveMode {
            get => mode == ManualAdjustmentMode.SingleMove;
            set { if (value) Mode = ManualAdjustmentMode.SingleMove; }
        }

        public bool IsTargetPositionsMode {
            get => mode == ManualAdjustmentMode.TargetPositions;
            set { if (value) Mode = ManualAdjustmentMode.TargetPositions; }
        }

        #endregion Mode

        #region Single move — inputs

        public IReadOnlyList<ManualAdjustmentTarget> PadTargets => ManualAdjustmentTarget.All;

        /// <summary>The nine pad cells with their selection state, row-major over the 3×3 grid.</summary>
        public IReadOnlyList<ManualPadItem> PadItems { get; }

        private void OnPadItemSelected(ManualPadItem winner) {
            foreach (var item in PadItems) {
                if (!ReferenceEquals(item, winner)) {
                    item.SetSelectedSilently(false);
                }
            }
            SelectedTarget = winner.Target;
        }

        /// <summary>The selected pad cell, or null until the user picks one (there is deliberately no default).</summary>
        public ManualAdjustmentTarget SelectedTarget {
            get => selectedTarget;
            set {
                if (!ReferenceEquals(selectedTarget, value)) {
                    selectedTarget = value;
                    foreach (var item in PadItems) {
                        item.SetSelectedSilently(ReferenceEquals(item.Target, value));
                    }
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(SelectedTargetKey));
                    RefreshAll();
                }
            }
        }

        /// <summary>Pad selection as a key, for the ToggleButton bindings and for tests.</summary>
        public string SelectedTargetKey {
            get => selectedTarget?.Key ?? string.Empty;
            set => SelectedTarget = string.IsNullOrEmpty(value) ? null : ManualAdjustmentTarget.ByKey(value);
        }

        public bool HasSelection => selectedTarget != null;

        public bool PositiveDirection {
            get => positiveDirection;
            set {
                if (positiveDirection != value) {
                    positiveDirection = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(NegativeDirection));
                    RefreshAll();
                }
            }
        }

        public bool NegativeDirection {
            get => !positiveDirection;
            set { if (value) PositiveDirection = false; }
        }

        public string AmountText {
            get => amountText;
            set {
                if (amountText != value) {
                    amountText = value ?? string.Empty;
                    RaisePropertyChanged();
                    RefreshAll();
                }
            }
        }

        /// <summary>The parsed amount, or null when the box does not hold a whole number of at least 1.</summary>
        internal int? Amount {
            get {
                if (int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= 1) {
                    return parsed;
                }
                return null;
            }
        }

        /// <summary>"= 18.0 µm of screw travel", or empty when the preset's step size is unknown.</summary>
        public string AmountMicronsHint {
            get {
                var amount = Amount;
                if (amount == null || !HasUnitMicrons) {
                    return string.Empty;
                }
                return string.Format(CultureInfo.InvariantCulture, "= {0:0.0} µm of screw travel", amount.Value * UnitMicrons);
            }
        }

        public string AmountTooltip =>
            HasUnitMicrons
                ? string.Format(CultureInfo.InvariantCulture,
                    "How far each affected motor moves, in device steps. 1 step = {0:0.#} µm of screw travel. Amounts above the per-command cap ({1}) are sent as several same-direction commands.",
                    UnitMicrons, MaxStepsPerCommand)
                : string.Format(CultureInfo.InvariantCulture,
                    "How far each affected motor moves, in device steps. Amounts above the per-command cap ({0}) are sent as several same-direction commands.",
                    MaxStepsPerCommand);

        #endregion Single move — inputs

        #region Single move — preview

        /// <summary>The commands a Send would issue right now, in order. Empty when the inputs are incomplete.</summary>
        internal IReadOnlyList<TiltAdapterMove> PlannedSingleMoves {
            get {
                var target = selectedTarget;
                var amount = Amount;
                if (target == null || amount == null) {
                    return Array.Empty<TiltAdapterMove>();
                }

                int signedSteps = target.SignedAxisSteps(positiveDirection, amount.Value);
                var chunks = TiltMovePlanner.SplitStepsForCap(signedSteps, MaxStepsPerCommand);
                var group = target.Axis == TiltMoveAxis.Backfocus ? TiltMoveGroup.Backfocus : TiltMoveGroup.Tilt;
                var moves = new List<TiltAdapterMove>(chunks.Count);
                for (int i = 0; i < chunks.Count; ++i) {
                    string description = chunks.Count == 1
                        ? target.DescribeMove(chunks[i])
                        : string.Format(CultureInfo.InvariantCulture, "{0} (part {1} of {2})", target.DescribeMove(chunks[i]), i + 1, chunks.Count);
                    moves.Add(new TiltAdapterMove(target.Axis, chunks[i], group, description));
                }
                return moves;
            }
        }

        public bool PreviewVisible => HasSelection && Amount != null;

        public string NoSelectionText => "Pick a corner, side, or All to move.";

        public string PreviewKindLabel => selectedTarget?.KindLabel ?? string.Empty;

        /// <summary>"TR (Motor 1) +20 · BL (Motor 4) −20 steps".</summary>
        public string PreviewSemanticText {
            get {
                var target = selectedTarget;
                var amount = Amount;
                if (target == null || amount == null) {
                    return string.Empty;
                }
                return target.DescribeMove(target.SignedAxisSteps(positiveDirection, amount.Value));
            }
        }

        /// <summary>
        /// The exact wire string, straight from <see cref="EatCommands.Format(TiltAdapterMove, EatSignEncoding)"/>.
        /// Never hand-built: the default encoding signs the argument rather than switching mnemonic, so a "BL +20"
        /// pad click really sends <c>tr,-20</c>, and a chip assembled from the pad label would name a command
        /// that is never issued. Shows the first command when a large amount is split.
        /// </summary>
        public string PreviewWireCommand {
            get {
                var moves = PlannedSingleMoves;
                return moves.Count == 0 ? string.Empty : EatCommands.Format(moves[0]);
            }
        }

        public string PreviewWireCommandTooltip => "The exact command string sent to the device.";

        /// <summary>Predicted end positions for all four motors, in the 2×2 display order the live grid uses.</summary>
        public IReadOnlyList<ManualPreviewCell> PreviewCells { get; private set; } = Array.Empty<ManualPreviewCell>();

        /// <summary>Per-motor predicted end positions in DEVICE motor order, or null when positions are unknown.</summary>
        internal int[] PredictedPositions {
            get {
                if (!PositionsKnown) {
                    return null;
                }
                var target = selectedTarget;
                var amount = Amount;
                if (target == null || amount == null) {
                    return null;
                }
                var effectWizard = target.PerScrewEffect(positiveDirection, amount.Value);
                var effectDevice = EatTiltMotionController.PermuteWizardToDeviceMotorOrder(effectWizard);
                var predicted = new int[4];
                for (int i = 0; i < 4; ++i) {
                    predicted[i] = lastKnownPositions[i] + (int)Math.Round(effectDevice[i], MidpointRounding.AwayFromZero);
                }
                return predicted;
            }
        }

        private IReadOnlyList<ManualPreviewCell> BuildPreviewCells() {
            var target = selectedTarget;
            var amount = Amount;
            if (target == null || amount == null) {
                return Array.Empty<ManualPreviewCell>();
            }

            var effectWizard = target.PerScrewEffect(positiveDirection, amount.Value);
            var effectDevice = EatTiltMotionController.PermuteWizardToDeviceMotorOrder(effectWizard);
            var predicted = PredictedPositions;
            int max = MaxExcursionSteps;
            double nearBand = max * NearLimitFraction;

            var cells = new List<ManualPreviewCell>(4);
            foreach (var corner in TiltAdapterCorner.InDisplayOrder) {
                int deviceIndex = corner.DeviceMotorNumber - 1;
                int delta = (int)Math.Round(effectDevice[deviceIndex], MidpointRounding.AwayFromZero);
                bool moving = delta != 0;

                if (predicted == null) {
                    cells.Add(new ManualPreviewCell(
                        corner,
                        valueText: "unknown → unknown",
                        isMoving: moving,
                        limitTag: string.Empty,
                        severity: ManualLimitSeverity.None,
                        tooltip: "Motor positions are unknown, so the travel window cannot be checked for this move."));
                    continue;
                }

                int from = lastKnownPositions[deviceIndex];
                int to = predicted[deviceIndex];
                string valueText = moving
                    ? string.Format(CultureInfo.InvariantCulture, "{0} → {1} ({2})", from, to, delta.ToString("+0;-0;0", CultureInfo.InvariantCulture))
                    : string.Format(CultureInfo.InvariantCulture, "{0} → {1}", from, to);

                string tag = string.Empty;
                var severity = ManualLimitSeverity.None;
                if (to < 0) {
                    tag = "below 0";
                    severity = ManualLimitSeverity.Blocking;
                } else if (to > max) {
                    tag = "over max";
                    severity = ManualLimitSeverity.Blocking;
                } else if (to <= nearBand) {
                    tag = "near 0";
                    severity = ManualLimitSeverity.Near;
                } else if (to >= max - nearBand) {
                    tag = "near max";
                    severity = ManualLimitSeverity.Near;
                }

                string tooltip = string.Format(
                    CultureInfo.InvariantCulture,
                    "Travel window 0 to {0}. This move ends at {1}, leaving {2} to the max and {3} to zero.",
                    max, to, max - to, to);

                cells.Add(new ManualPreviewCell(corner, valueText, moving, tag, severity, tooltip));
            }
            return cells;
        }

        /// <summary>Plain-physics summary of what the move changes. Never claims a "toward/away" direction — that is rig-dependent.</summary>
        public string ConsequenceText {
            get {
                var target = selectedTarget;
                var amount = Amount;
                if (target == null || amount == null) {
                    return string.Empty;
                }

                if (target.Kind == ManualMoveKind.Backfocus) {
                    return HasUnitMicrons
                        ? string.Format(CultureInfo.InvariantCulture, "Changes sensor spacing by {0:0.0} µm; tilt unchanged.", amount.Value * UnitMicrons)
                        : string.Format(CultureInfo.InvariantCulture, "Changes sensor spacing by {0} steps; tilt unchanged.", amount.Value);
                }

                // A tilt move is differential: the named element goes one way by `amount` and its opposite goes
                // the other way by the same amount, so the spacing between them changes by twice the amount.
                int differential = 2 * amount.Value;
                return HasUnitMicrons
                    ? string.Format(CultureInfo.InvariantCulture, "Changes {0}-vs-{1} spacing by {2:0.0} µm of screw travel.", target.MovedLabel, target.OppositeLabel, differential * UnitMicrons)
                    : string.Format(CultureInfo.InvariantCulture, "Changes {0}-vs-{1} spacing by {2} steps of screw travel.", target.MovedLabel, target.OppositeLabel, differential);
            }
        }

        public string LegendText =>
            HasUnitMicrons
                ? string.Format(CultureInfo.InvariantCulture, "+ = clockwise / tighten (the wizard's positive direction); − = the opposite. 1 step = {0:0.#} µm.", UnitMicrons)
                : "+ = clockwise / tighten (the wizard's positive direction); − = the opposite.";

        public string MoveCountAndDurationText {
            get {
                int count = PlannedSingleMoves.Count;
                if (count == 0) {
                    return string.Empty;
                }
                string moves = count == 1 ? "1 move" : string.Format(CultureInfo.InvariantCulture, "{0} moves", count);
                return string.Format(CultureInfo.InvariantCulture, "{0} · {1}", moves, TiltDeviceAdjustmentPromptVM.FormatDuration(count * PerMoveSeconds));
            }
        }

        public string SendButtonText {
            get {
                int count = PlannedSingleMoves.Count;
                return count <= 1
                    ? "Send 1 move"
                    : string.Format(CultureInfo.InvariantCulture, "Send {0} moves", count);
            }
        }

        public bool PositionsUnknownAdvisoryVisible => IsDeviceConnected && !PositionsKnown && mode == ManualAdjustmentMode.SingleMove;

        public string PositionsUnknownAdvisory =>
            "Motor positions are unknown — travel limits can't be checked for this move. Verify positions in the vendor app before sending.";

        #endregion Single move — preview

        #region Target positions

        public IReadOnlyList<ManualTargetCell> TargetCells { get; }

        public string TargetModeCaption => "Type where each motor should end up, in device steps. Current positions are filled in to start.";

        private void OnTargetCellChanged() => RefreshAll();

        private void PrefillTargetsFromCurrent() {
            if (!PositionsKnown) {
                return;
            }
            foreach (var cell in TargetCells) {
                cell.SetTargetSilently(lastKnownPositions[cell.Corner.DeviceMotorNumber - 1].ToString(CultureInfo.InvariantCulture));
            }
        }

        private void ClearTargets() {
            foreach (var cell in TargetCells) {
                cell.SetTargetSilently(string.Empty);
            }
        }

        private void ResetTargetsToCurrent() {
            PrefillTargetsFromCurrent();
            RefreshAll();
        }

        public ICommand ResetToCurrentCommand { get; }

        /// <summary>Parsed absolute targets in DEVICE motor order, or null when any cell is empty/invalid/out of range.</summary>
        internal int[] ParsedTargets {
            get {
                var result = new int[4];
                foreach (var cell in TargetCells) {
                    if (!int.TryParse(cell.TargetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) {
                        return null;
                    }
                    if (value < 0 || value > MaxExcursionSteps) {
                        return null;
                    }
                    result[cell.Corner.DeviceMotorNumber - 1] = value;
                }
                return result;
            }
        }

        /// <summary>
        /// The requested per-screw step delta in WIZARD screw order [0..3] — the planner's <c>sPerScrew</c>
        /// space. Null when positions are unknown or any target is invalid.
        /// </summary>
        internal double[] TargetDeltaPerScrew {
            get {
                var targets = ParsedTargets;
                if (targets == null || !PositionsKnown) {
                    return null;
                }
                return TiltDeviceTargetMath.DeltaPerScrew(targets, lastKnownPositions);
            }
        }

        /// <summary>Preview of the full (tilt + backfocus) plan for the current targets, or null when it cannot be built.</summary>
        internal TiltDevicePlanPreview TargetPlanPreview {
            get {
                var delta = TargetDeltaPerScrew;
                var controller = Controller;
                if (delta == null || controller == null || delta.All(d => Math.Abs(d) < 0.5)) {
                    return null;
                }
                return TiltDevicePlanPreviewBuilder.Build(delta, includeTilt: true, includeBackfocus: true, unitMicrons: UnitMicrons, maxStepsPerCommand: MaxStepsPerCommand, controller: controller);
            }
        }

        public string TargetHintText { get; private set; } = string.Empty;

        public bool TargetHintVisible => !string.IsNullOrEmpty(TargetHintText);

        public bool TargetHintIsError { get; private set; }

        public bool TwistWarningVisible { get; private set; }

        public string TwistWarningTitle => "Twist can't be made";

        public string TwistWarningBody { get; private set; } = string.Empty;

        #endregion Target positions

        #region Commands + gating

        public ICommand SendCommand => sendCommand;

        public ICommand ReviewMovesCommand => reviewCommand;

        public ICommand StopCommand => stopCommand;

        public ICommand DismissResultCommand { get; }

        public string ReviewButtonText => "Review moves…";

        public bool CanSend => mode == ManualAdjustmentMode.SingleMove && string.IsNullOrEmpty(SendDisabledReason) && !isSending && PlannedSingleMoves.Count > 0;

        public bool CanReview => mode == ManualAdjustmentMode.TargetPositions && string.IsNullOrEmpty(ReviewBlockReason) && !isSending;

        public bool CanStop => isSending && remainingMoveCount > 1 && !(sendCts?.IsCancellationRequested ?? true);

        /// <summary>
        /// The single reason line shown above Send, or empty when Send is available. Order is the panel's
        /// documented precedence: device state first, then inputs, then the travel window.
        /// </summary>
        public string SendDisabledReason {
            get {
                if (!IsDeviceConnected || isSending) {
                    return string.Empty; // the replaced body / the progress line already explains these.
                }
                if (IsDeviceBusyElsewhere) {
                    return BusyReason;
                }
                if (selectedTarget == null) {
                    return "Pick a corner, side, or All to move.";
                }
                if (Amount == null) {
                    return "Enter an amount of at least 1 step.";
                }

                var predicted = PredictedPositions;
                if (predicted != null) {
                    int max = MaxExcursionSteps;
                    for (int i = 0; i < 4; ++i) {
                        var corner = TiltAdapterCorner.ForDeviceMotor(i + 1);
                        if (predicted[i] < 0) {
                            return string.Format(
                                CultureInfo.InvariantCulture,
                                "This move would drive Motor {0} ({1}) below 0. Reduce the amount, or send an All + move first to lift all motors.",
                                corner.DeviceMotorNumber, corner.Label);
                        }
                        if (predicted[i] > max) {
                            return string.Format(
                                CultureInfo.InvariantCulture,
                                "This move would drive Motor {0} ({1}) past the max excursion ({2}). Reduce the amount, or lower all motors with an All − move first.",
                                corner.DeviceMotorNumber, corner.Label, max);
                        }
                    }
                }
                return string.Empty;
            }
        }

        public bool SendDisabledReasonVisible => !string.IsNullOrEmpty(SendDisabledReason);

        /// <summary>
        /// Why Review is unavailable, for the gate. Not all of these are worth displaying — see
        /// <see cref="ReviewDisabledReason"/>.
        /// </summary>
        private string ReviewBlockReason {
            get {
                if (!IsDeviceConnected || isSending) {
                    return string.Empty;
                }
                if (IsDeviceBusyElsewhere) {
                    return BusyReason;
                }
                if (!PositionsKnown) {
                    return "Motor positions are unknown — targets need a known starting point. They update after a successful poll or move.";
                }
                if (ParsedTargets == null) {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "Targets must be whole numbers between 0 and {0} steps.",
                        MaxExcursionSteps);
                }
                var delta = TargetDeltaPerScrew;
                if (delta != null && delta.All(d => Math.Abs(d) < 0.5)) {
                    return NothingToMoveCopy;
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// The single reason line shown above Review, or empty when Review is available — or when the reason is
        /// already on screen. "Nothing to move" is the hint line's job (it describes what the targets amount to,
        /// which is where the eye already is); repeating it as a disabled-reason directly underneath just
        /// stutters the same sentence twice.
        /// </summary>
        public string ReviewDisabledReason {
            get {
                string reason = ReviewBlockReason;
                return string.Equals(reason, NothingToMoveCopy, StringComparison.Ordinal) ? string.Empty : reason;
            }
        }

        public bool ReviewDisabledReasonVisible => !string.IsNullOrEmpty(ReviewDisabledReason);

        private string BusyReason {
            get {
                string op = connectionService?.CurrentOperationName;
                return string.IsNullOrEmpty(op)
                    ? "Device busy. Wait for it to finish."
                    : string.Format(CultureInfo.InvariantCulture, "Device busy — {0}. Wait for it to finish.", op);
            }
        }

        #endregion Commands + gating

        #region Status / result

        public bool IsSending => isSending;

        public string ProgressText => progressText;

        public bool ProgressVisible => isSending;

        public string StopButtonText => "Stop after this move";

        public string StopButtonTooltip => "The move already sent can't be recalled. Nothing further will be sent.";

        public string SuccessText => successText;

        public bool SuccessVisible => !isSending && !ResultVisible && !string.IsNullOrEmpty(successText);

        public bool ResultVisible => !string.IsNullOrEmpty(resultTitle);

        public string ResultTitle => resultTitle;

        public string ResultBody => resultBody;

        public bool ResultIsError => resultIsError;

        private void SetResult(string title, string body, bool isError) {
            resultTitle = title ?? string.Empty;
            resultBody = body ?? string.Empty;
            resultIsError = isError;
        }

        private void DismissResult() {
            SetResult(string.Empty, string.Empty, false);
            RefreshAll();
        }

        #endregion Status / result

        #region Execution

        private void RequestStop() {
            try {
                sendCts?.Cancel();
            } catch (ObjectDisposedException) {
                // The send finished between the button being enabled and the click landing — nothing left to stop.
            }
            RefreshAll();
        }

        /// <summary>
        /// Runs a send from a synchronous command without leaving an unobserved faulted task behind. Everything
        /// inside <see cref="ExecuteMovesAsync"/> is already handled; this catches the paths around it (the
        /// approval dialog, the lease) so a surprise there surfaces as a dismissible panel rather than a
        /// silently swallowed crash.
        /// </summary>
        private void RunGuarded(Func<Task> work, string context) {
            _ = Task.Run(async () => {
                try {
                    await work().ConfigureAwait(false);
                } catch (Exception ex) {
                    Logger.Error(ex, $"Manual Adjustment: {context} failed");
                    isSending = false;
                    SetResult("Manual adjustment failed", ex.Message, isError: true);
                    OnUIThread(RefreshAll);
                }
            });
        }

        /// <summary>Internal so tests can await the send instead of racing the fire-and-forget command.</summary>
        internal async Task SendSingleMoveAsync() {
            var target = selectedTarget;
            var amount = Amount;
            var moves = PlannedSingleMoves;
            if (!CanSend || target == null || amount == null || moves.Count == 0) {
                return;
            }

            var now = DateTime.Now;
            string summaryOnSuccess = string.Format(
                CultureInfo.InvariantCulture, "Sent {0} · {1} · finished at {2:HH:mm}.",
                target.DescribeMove(target.SignedAxisSteps(positiveDirection, amount.Value)),
                moves.Count == 1 ? "1 move" : string.Format(CultureInfo.InvariantCulture, "{0} moves", moves.Count),
                now);
            string headerSummary = string.Format(
                CultureInfo.InvariantCulture, "Last sent: {0} {1} at {2:HH:mm}",
                target.Label, target.SignedAxisSteps(positiveDirection, amount.Value).ToString("+0;-0;0", CultureInfo.InvariantCulture), now);

            await ExecuteMovesAsync(moves, summaryOnSuccess, headerSummary, splitAmount: moves.Count > 1 ? amount : null).ConfigureAwait(false);
        }

        /// <summary>Internal so tests can await the review-and-send round trip.</summary>
        internal async Task ReviewTargetsAsync() {
            var delta = TargetDeltaPerScrew;
            var controller = Controller;
            if (!CanReview || delta == null || controller == null) {
                return;
            }

            TiltDevicePlanPreview Replanner(bool includeTilt, bool includeBackfocus) =>
                TiltDevicePlanPreviewBuilder.Build(delta, includeTilt, includeBackfocus, UnitMicrons, MaxStepsPerCommand, controller);

            // The assumed-direction and pitch-mismatch warnings exist because Automatic Adjustment INFERS screw
            // motion from a measured curvature. Here the user commanded absolute steps, so nothing is inferred
            // and firing those warnings would be a lie; positions are known by the CanReview gate.
            var choice = await showAdjustmentPromptAsync(Replanner, UnitMicrons).ConfigureAwait(false);
            if (choice == null || !choice.Proceed || choice.FinalPlan == null || choice.FinalPlan.Moves.Count == 0) {
                return;
            }

            var now = DateTime.Now;
            int moveCount = choice.FinalPlan.Moves.Count;
            string summaryOnSuccess = string.Format(
                CultureInfo.InvariantCulture, "Adjustment complete — {0} sent · finished at {1:HH:mm}.",
                moveCount == 1 ? "1 move" : string.Format(CultureInfo.InvariantCulture, "{0} moves", moveCount), now);
            string headerSummary = string.Format(
                CultureInfo.InvariantCulture, "Last sent: {0} at {1:HH:mm}",
                moveCount == 1 ? "1 move" : string.Format(CultureInfo.InvariantCulture, "{0} moves", moveCount), now);

            await ExecuteMovesAsync(choice.FinalPlan.Moves, summaryOnSuccess, headerSummary, splitAmount: null).ConfigureAwait(false);

            OnUIThread(() => {
                // Re-prefill from where the motors actually landed, so the cells read Δ 0 (± the residual the
                // dialog predicted) rather than still showing the request.
                PrefillTargetsFromCurrent();
                RefreshAll();
            });
        }

        /// <summary>
        /// Sends <paramref name="moves"/> sequentially under the device lease. There is no device abort, so
        /// cancellation only ever skips moves that have not been sent — <c>ExecuteMoveAsync</c> honours the
        /// token at entry and not after. Every move republishes the controller's counters so the live grid,
        /// the preview and the target cells all track the device while the 5 s poll is suspended by our lease.
        /// </summary>
        private async Task ExecuteMovesAsync(IReadOnlyList<TiltAdapterMove> moves, string summaryOnSuccess, string headerSummary, int? splitAmount) {
            var svc = connectionService;
            var controller = Controller;
            if (svc == null || controller == null) {
                return;
            }

            using var operationToken = svc.TryBeginOperation(OperationName);
            if (operationToken == null) {
                SetResult("Device busy", BusyReason, isError: false);
                OnUIThread(RefreshAll);
                return;
            }

            // State is written on whatever thread we are called from and only the NOTIFICATION is marshalled.
            // Posting the state change instead would let the loop below start — and the first move go out —
            // before isSending/sendCts were even set, which would leave Stop pointing at a disposed token and
            // the panel claiming to be idle mid-send.
            var cts = new CancellationTokenSource();
            var previousCts = sendCts;
            sendCts = cts;
            previousCts?.Dispose();
            isSending = true;
            successText = string.Empty;
            lastSentSummary = string.Empty;
            SetResult(string.Empty, string.Empty, false);
            remainingMoveCount = moves.Count;
            progressText = string.Format(CultureInfo.InvariantCulture, "Move 1 of {0} — starting…", moves.Count);
            OnUIThread(RefreshAll);

            int sent = 0;
            Exception failure = null;
            bool stopped = false;
            try {
                for (int i = 0; i < moves.Count; ++i) {
                    int moveNumber = i + 1;
                    var progress = new Progress<string>(text => {
                        progressText = string.Format(CultureInfo.InvariantCulture, "Move {0} of {1} — {2}", moveNumber, moves.Count, text);
                        OnUIThread(() => {
                            RaisePropertyChanged(nameof(ProgressText));
                            RaisePropertyChanged(nameof(SummaryText));
                        });
                    });

                    remainingMoveCount = moves.Count - i;
                    OnUIThread(RefreshAll);

                    try {
                        await controller.ExecuteMoveAsync(moves[i], progress, cts.Token).ConfigureAwait(false);
                    } catch (OperationCanceledException) {
                        // The device has no abort: cancellation is only ever honoured at the next move's entry,
                        // so this always means "nothing further was sent", never "the last move was interrupted".
                        stopped = true;
                        break;
                    }
                    ++sent;
                    // No follow-up 'cp': the move response already carried fresh counters.
                    svc.PublishControllerPositions();
                }
            } catch (Exception ex) {
                failure = ex;
                Logger.Error(ex, $"Manual Adjustment: move {sent + 1} of {moves.Count} failed");
            } finally {
                isSending = false;
                remainingMoveCount = 0;
                progressText = string.Empty;
                if (failure != null) {
                    SetResult(
                        BuildFailureTitle(sent, moves.Count),
                        BuildFailureBody(moves, sent, failure, splitAmount),
                        isError: true);
                } else if (stopped) {
                    SetResult(
                        string.Format(CultureInfo.InvariantCulture, "Stopped after {0} of {1} moves", sent, moves.Count),
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "The moves already sent are applied — there is no automatic undo. Nothing after move {0} was sent. Positions above show where the motors are now.",
                            sent),
                        isError: false);
                } else {
                    successText = summaryOnSuccess;
                    lastSentSummary = headerSummary;
                }
                if (ReferenceEquals(sendCts, cts)) {
                    sendCts = null;
                }
                cts.Dispose();
                OnUIThread(RefreshAll);
            }
        }

        private static string BuildFailureTitle(int sent, int total) {
            if (total == 1) {
                return "Move failed";
            }
            return sent == 0
                ? "Adjustment failed before any move was sent"
                : string.Format(CultureInfo.InvariantCulture, "Adjustment stopped after {0} of {1} moves", sent, total);
        }

        private string BuildFailureBody(IReadOnlyList<TiltAdapterMove> moves, int sent, Exception failure, int? splitAmount) {
            string failedWire = sent < moves.Count ? EatCommands.Format(moves[sent]) : string.Empty;

            if (moves.Count == 1) {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "The command '{0}' got no valid response. It may or may not have reached the device, so the position counters above were not advanced — they re-sync on the next successful poll. If in doubt, check the vendor app before sending more moves.\n\n{1}",
                    failedWire, failure.Message);
            }

            if (splitAmount != null) {
                // A split nudge: every command is the same axis and sign, so the remainder is expressible as a
                // smaller amount the user can simply re-send.
                int sentSteps = moves.Take(sent).Sum(m => Math.Abs(m.Steps));
                int remainingSteps = splitAmount.Value - sentSteps;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Sent {0} of {1} moves ({2} of {3} steps). The steps already sent are applied — there is no automatic undo. The remaining {4} steps were not sent; set Amount to {4} and press Send to finish.\n\n{5}",
                    sent, moves.Count, sentSteps, splitAmount.Value, remainingSteps, failure.Message);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "The first {0} moves were sent and are applied — there is no automatic undo. Move {1} ('{2}') failed and nothing after it was sent. Once the positions above re-sync, press Review moves — the plan recomputes from wherever the motors actually are, so it will finish the remainder.\n\n{3}",
                sent, sent + 1, failedWire, failure.Message);
        }

        #endregion Execution

        #region Refresh

        /// <summary>
        /// Re-derives every projection and re-raises the lot. The panel is small and each readout is a pure
        /// function of (device state, options, inputs), so a single blanket refresh is both simpler and
        /// impossible to get subtly out of sync — which matters when a wrong number here becomes a physical
        /// move that cannot be undone.
        /// </summary>
        private void RefreshAll() {
            PreviewCells = BuildPreviewCells();
            RefreshTargetProjections();

            RaisePropertyChanged(nameof(IsExpanded));
            RaisePropertyChanged(nameof(SummaryText));
            RaisePropertyChanged(nameof(IsDeviceConnected));
            RaisePropertyChanged(nameof(IsDeviceBusyElsewhere));
            RaisePropertyChanged(nameof(PositionsKnown));

            RaisePropertyChanged(nameof(HasSelection));
            RaisePropertyChanged(nameof(AmountMicronsHint));
            RaisePropertyChanged(nameof(AmountTooltip));
            RaisePropertyChanged(nameof(PreviewVisible));
            RaisePropertyChanged(nameof(PreviewKindLabel));
            RaisePropertyChanged(nameof(PreviewSemanticText));
            RaisePropertyChanged(nameof(PreviewWireCommand));
            RaisePropertyChanged(nameof(PreviewCells));
            RaisePropertyChanged(nameof(ConsequenceText));
            RaisePropertyChanged(nameof(LegendText));
            RaisePropertyChanged(nameof(MoveCountAndDurationText));
            RaisePropertyChanged(nameof(SendButtonText));
            RaisePropertyChanged(nameof(PositionsUnknownAdvisoryVisible));

            RaisePropertyChanged(nameof(TargetHintText));
            RaisePropertyChanged(nameof(TargetHintVisible));
            RaisePropertyChanged(nameof(TargetHintIsError));
            RaisePropertyChanged(nameof(TwistWarningVisible));
            RaisePropertyChanged(nameof(TwistWarningBody));

            RaisePropertyChanged(nameof(CanSend));
            RaisePropertyChanged(nameof(CanReview));
            RaisePropertyChanged(nameof(CanStop));
            RaisePropertyChanged(nameof(SendDisabledReason));
            RaisePropertyChanged(nameof(SendDisabledReasonVisible));
            RaisePropertyChanged(nameof(ReviewDisabledReason));
            RaisePropertyChanged(nameof(ReviewDisabledReasonVisible));

            RaisePropertyChanged(nameof(IsSending));
            RaisePropertyChanged(nameof(ProgressText));
            RaisePropertyChanged(nameof(ProgressVisible));
            RaisePropertyChanged(nameof(SuccessText));
            RaisePropertyChanged(nameof(SuccessVisible));
            RaisePropertyChanged(nameof(ResultVisible));
            RaisePropertyChanged(nameof(ResultTitle));
            RaisePropertyChanged(nameof(ResultBody));
            RaisePropertyChanged(nameof(ResultIsError));

            // CanExecuteChanged must be raised on the UI thread; every caller of RefreshAll already arrives via
            // OnUIThread (or is the constructor, before the VM is bound).
            sendCommand.NotifyCanExecuteChanged();
            reviewCommand.NotifyCanExecuteChanged();
            stopCommand.NotifyCanExecuteChanged();
        }

        private void RefreshTargetProjections() {
            // Only while the mode is showing: building the hint runs the planner and asks the controller to
            // order the result, and doing that on every keystroke of a single-move amount would be both wasted
            // work and a device call nothing on screen is asking for.
            if (mode != ManualAdjustmentMode.TargetPositions) {
                return;
            }

            int max = MaxExcursionSteps;
            foreach (var cell in TargetCells) {
                int deviceIndex = cell.Corner.DeviceMotorNumber - 1;
                cell.NowText = PositionsKnown
                    ? string.Format(CultureInfo.InvariantCulture, "now {0}", lastKnownPositions[deviceIndex])
                    : "now unknown";

                bool parsed = int.TryParse(cell.TargetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value);
                cell.HasError = !string.IsNullOrEmpty(cell.TargetText) && (!parsed || value < 0 || value > max);

                if (parsed && !cell.HasError && PositionsKnown) {
                    int delta = value - lastKnownPositions[deviceIndex];
                    cell.DeltaText = HasUnitMicrons
                        ? string.Format(CultureInfo.InvariantCulture, "Δ {0} ({1:0.0} µm)", delta.ToString("+0;-0;0", CultureInfo.InvariantCulture), Math.Abs(delta) * UnitMicrons)
                        : string.Format(CultureInfo.InvariantCulture, "Δ {0}", delta.ToString("+0;-0;0", CultureInfo.InvariantCulture));
                } else {
                    cell.DeltaText = string.Empty;
                }
                cell.WillReachText = string.Empty;
            }

            TargetHintText = string.Empty;
            TargetHintIsError = false;
            TwistWarningVisible = false;
            TwistWarningBody = string.Empty;

            var delta4 = TargetDeltaPerScrew;
            if (delta4 == null) {
                return;
            }
            if (delta4.All(d => Math.Abs(d) < 0.5)) {
                TargetHintText = NothingToMoveCopy;
                return;
            }

            var preview = TargetPlanPreview;
            if (preview == null) {
                return;
            }

            if (preview.HardLimitViolated) {
                TargetHintText = string.Format(
                    CultureInfo.InvariantCulture,
                    "These targets can't be reached without leaving the travel window (0 to {0}).",
                    max);
                TargetHintIsError = true;
            } else {
                int count = preview.Plan.Moves.Count;
                string moves = count == 1 ? "1 move" : string.Format(CultureInfo.InvariantCulture, "{0} moves", count);
                TargetHintText = preview.Plan.BiasSteps > 0
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "Becomes {0} · {1}, including a +{2}-step lift to keep all motors above 0.",
                        moves, TiltDeviceAdjustmentPromptVM.FormatDuration(preview.Plan.EstimatedSeconds), preview.Plan.BiasSteps)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "Becomes {0} · {1}.",
                        moves, TiltDeviceAdjustmentPromptVM.FormatDuration(preview.Plan.EstimatedSeconds));
            }

            // Twist: the component of the requested delta that no rigid plane can produce. Reported before the
            // user ever opens the dialog, because they typed four numbers and only three of the four degrees of
            // freedom are physically available.
            double twist = TiltDeviceTargetMath.TwistSteps(delta4);
            if (Math.Abs(twist) < 1.0) {
                return;
            }

            TwistWarningVisible = true;
            TwistWarningBody = HasUnitMicrons
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "These four targets differ from a rigid plane by ±{0:0.#} steps (about {1:0.0} µm across the sensor). The adapter can tilt the sensor and change its spacing, but it cannot twist it. Each corner above shows the position it will actually reach.",
                    Math.Abs(twist), Math.Abs(twist) * UnitMicrons)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "These four targets differ from a rigid plane by ±{0:0.#} steps. The adapter can tilt the sensor and change its spacing, but it cannot twist it. Each corner above shows the position it will actually reach.",
                    Math.Abs(twist));

            // "Will reach" is taken from the plan's own moves rather than re-deriving the twist share, so it
            // folds in step rounding and any prepended bias exactly as executed.
            var applied = new double[4];
            foreach (var move in preview.Plan.Moves) {
                for (int i = 0; i < 4; ++i) {
                    applied[i] += move.PerCornerSteps[i];
                }
            }
            foreach (var cell in TargetCells) {
                int deviceIndex = cell.Corner.DeviceMotorNumber - 1;
                int wizardIndex = cell.Corner.WizardScrewNumber - 1;
                int reached = lastKnownPositions[deviceIndex] + (int)Math.Round(applied[wizardIndex], MidpointRounding.AwayFromZero);
                cell.WillReachText = string.Format(CultureInfo.InvariantCulture, "will reach {0}", reached);
            }
        }

        #endregion Refresh
    }
}
