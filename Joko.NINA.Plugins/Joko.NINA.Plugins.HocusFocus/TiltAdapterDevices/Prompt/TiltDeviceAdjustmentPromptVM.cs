#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt {

    /// <summary>
    /// The immutable result of one replanner invocation: the recomputed plan plus the controller's limit
    /// check against it. <see cref="HardLimitViolated"/> blocks Proceed; <see cref="LimitWarning"/> is the
    /// user-facing explanation (which may also be a non-blocking advisory when the hard flag is false).
    /// </summary>
    public sealed class TiltDevicePlanPreview {

        public TiltDevicePlanPreview(TiltAdapterMovePlan plan, bool hardLimitViolated, string limitWarning) {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            HardLimitViolated = hardLimitViolated;
            LimitWarning = limitWarning ?? string.Empty;
        }

        /// <summary>The recomputed move plan for the current Tilt/Backfocus toggles.</summary>
        public TiltAdapterMovePlan Plan { get; }

        /// <summary>True when executing <see cref="Plan"/> would violate a device travel limit; blocks Proceed.</summary>
        public bool HardLimitViolated { get; }

        /// <summary>User-facing limit text (blocking when <see cref="HardLimitViolated"/>, advisory otherwise). Never null.</summary>
        public string LimitWarning { get; }
    }

    /// <summary>
    /// One row of the approval dialog's move list. <see cref="SemanticText"/> is the human-meaningful
    /// primary line (which numbered screws move, and by how many signed steps); <see cref="MoveKindLabel"/>
    /// tags the move as Corner / Side / Backfocus. <see cref="WireCommand"/> is the exact string sent to the
    /// device (e.g. <c>tr,150</c>), demoted to a small auditability chip rather than the row's anchor.
    /// </summary>
    public sealed class TiltDeviceAdjustmentMoveRow {

        public TiltDeviceAdjustmentMoveRow(string wireCommand, string semanticText, string moveKindLabel, string description, TiltMoveGroup group, bool assumedDirection = false) {
            WireCommand = wireCommand ?? string.Empty;
            SemanticText = semanticText ?? string.Empty;
            MoveKindLabel = moveKindLabel ?? string.Empty;
            Description = description ?? string.Empty;
            Group = group;
            AssumedDirection = assumedDirection;
        }

        /// <summary>
        /// True when this row's move could physically go the wrong way because the backfocus direction was
        /// never measured — the anchor for the "(assumed direction)" warning, shown inline on the row.
        /// </summary>
        public bool AssumedDirection { get; }

        /// <summary>The exact wire command string, e.g. <c>tr,150</c> or <c>bf,-20</c>.</summary>
        public string WireCommand { get; }

        /// <summary>
        /// Human-readable, screw-oriented description of the move (e.g. "Corner move — Screw 1 +142, Screw 3
        /// −142 steps"). The primary text shown for each row.
        /// </summary>
        public string SemanticText { get; }

        /// <summary>Short kind tag for the row badge: "Corner", "Side", or "Backfocus".</summary>
        public string MoveKindLabel { get; }

        /// <summary>The planner's internal axis description (kept for status text / diagnostics).</summary>
        public string Description { get; }

        public TiltMoveGroup Group { get; }

        /// <summary>Display label for the group badge.</summary>
        public string GroupLabel => Group == TiltMoveGroup.Backfocus ? "Backfocus" : "Tilt";
    }

    /// <summary>One labeled per-corner residual cell (wizard screw indices 1..4).</summary>
    public sealed class TiltDeviceCornerResidualRow {

        public TiltDeviceCornerResidualRow(string label, string valueText) {
            Label = label ?? string.Empty;
            ValueText = valueText ?? string.Empty;
        }

        public string Label { get; }

        public string ValueText { get; }
    }

    /// <summary>
    /// View model for the modal approval dialog shown before motorized tilt-adapter moves are executed.
    /// The moves are physical and EEPROM-persisted, so the dialog's job is to make the exact signed wire
    /// commands, the residual they leave behind, and every applicable warning unmistakable before the user
    /// clicks Proceed. Toggling the Tilt/Backfocus group checkboxes re-invokes the injected replanner so
    /// the displayed plan is ALWAYS the plan that would execute. Mirrors the
    /// <c>ReplaySettingsPromptVM</c> TaskCompletionSource + RequestClose modal pattern.
    /// </summary>
    public sealed class TiltDeviceAdjustmentPromptVM : BaseINPC, IDisposable {
        private readonly TaskCompletionSource<TiltDeviceAdjustmentChoice> tcs =
            new TaskCompletionSource<TiltDeviceAdjustmentChoice>(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Func<bool, bool, TiltDevicePlanPreview> replanner;
        private readonly bool screwInwardCurvatureSignIsMeasured;
        private readonly bool positionsUnknown;
        private readonly double unitMicrons;
        private readonly RelayCommand proceedCommand;

        private bool applyTilt = true;
        private bool applyBackfocus = true;

        public event EventHandler RequestClose;

        public TiltDeviceAdjustmentPromptVM(
            Func<bool, bool, TiltDevicePlanPreview> replanner,
            bool screwInwardCurvatureSignIsMeasured,
            string pitchMismatchWarning,
            bool positionsUnknown,
            double unitMicrons = 0.0) {
            this.replanner = replanner ?? throw new ArgumentNullException(nameof(replanner));
            this.screwInwardCurvatureSignIsMeasured = screwInwardCurvatureSignIsMeasured;
            this.positionsUnknown = positionsUnknown;
            this.unitMicrons = unitMicrons;
            PitchMismatchWarning = pitchMismatchWarning ?? string.Empty;

            proceedCommand = new RelayCommand(Proceed, CanProceed);
            CancelCommand = new RelayCommand(Cancel);

            Replan();
        }

        /// <summary>Completes when the user clicks Proceed or Cancel (or closes the window ⇒ Cancel).</summary>
        public Task<TiltDeviceAdjustmentChoice> Choice => tcs.Task;

        public ICommand ProceedCommand => proceedCommand;

        public ICommand CancelCommand { get; }

        /// <summary>Tilt group toggle; changing it re-invokes the replanner and refreshes the whole preview.</summary>
        public bool ApplyTilt {
            get => applyTilt;
            set {
                if (applyTilt != value) {
                    applyTilt = value;
                    RaisePropertyChanged();
                    Replan();
                }
            }
        }

        /// <summary>Backfocus group toggle; changing it re-invokes the replanner and refreshes the whole preview.</summary>
        public bool ApplyBackfocus {
            get => applyBackfocus;
            set {
                if (applyBackfocus != value) {
                    applyBackfocus = value;
                    RaisePropertyChanged();
                    Replan();
                }
            }
        }

        /// <summary>The latest replanner result — the plan that will execute if the user proceeds now.</summary>
        public TiltDevicePlanPreview Preview { get; private set; }

        /// <summary>Display rows for <see cref="TiltAdapterMovePlan.Moves"/> (wire command + description + group).</summary>
        public IReadOnlyList<TiltDeviceAdjustmentMoveRow> Moves { get; private set; }

        public bool HasMoves => Moves.Count > 0;

        public bool HasNoMoves => Moves.Count == 0;

        /// <summary>"No moves" / "1 move" / "N moves" for the footer summary.</summary>
        public string MoveCountText =>
            Moves.Count == 0 ? "No moves"
            : Moves.Count == 1 ? "1 move"
            : string.Format(CultureInfo.InvariantCulture, "{0} moves", Moves.Count);

        /// <summary>Dynamic Proceed-button label: "Send N moves" when there is something to send, else "Proceed".</summary>
        public string ProceedButtonText =>
            Moves.Count == 0 ? "Proceed"
            : Moves.Count == 1 ? "Send 1 move"
            : string.Format(CultureInfo.InvariantCulture, "Send {0} moves", Moves.Count);

        /// <summary>
        /// Explains why Proceed is disabled, for the case not already covered by a red panel (both groups off).
        /// Empty when Proceed is enabled, or when the hard-limit panel is already carrying the explanation.
        /// </summary>
        public string ProceedDisabledReason {
            get {
                if (Preview != null && Preview.HardLimitViolated) {
                    return string.Empty; // the blocking red panel already explains this.
                }
                if (!applyTilt && !applyBackfocus) {
                    return "Select at least one correction to apply.";
                }
                return string.Empty;
            }
        }

        public bool ProceedDisabledReasonVisible => !string.IsNullOrEmpty(ProceedDisabledReason);

        /// <summary>Labeled per-corner residual cells (Screw 1..4, µm).</summary>
        public IReadOnlyList<TiltDeviceCornerResidualRow> CornerResiduals { get; private set; }

        /// <summary>Human-friendly execution time estimate, e.g. "~30 s".</summary>
        public string EstimatedDurationText => FormatDuration(Preview.Plan.EstimatedSeconds);

        public bool HardLimitViolated => Preview.HardLimitViolated;

        /// <summary>Blocking limit warning (red, disables Proceed).</summary>
        public bool HardLimitWarningVisible => Preview.HardLimitViolated;

        /// <summary>Advisory limit warning: text present but the hard flag is off (does not disable Proceed).</summary>
        public bool SoftLimitWarningVisible => !Preview.HardLimitViolated && !string.IsNullOrWhiteSpace(Preview.LimitWarning);

        public string LimitWarningText {
            get {
                if (!string.IsNullOrWhiteSpace(Preview.LimitWarning)) {
                    return Preview.LimitWarning;
                }
                return Preview.HardLimitViolated ? "The planned moves would exceed a device travel limit." : string.Empty;
            }
        }

        /// <summary>Visible when the plan carries a twist component of at least one whole step.</summary>
        public bool TwistWarningVisible => Math.Abs(Preview.Plan.TwistResidualSteps) >= 1.0;

        public string TwistWarningText {
            get {
                double steps = Preview.Plan.TwistResidualSteps;
                // Translate to µm when the step size is known, so the twist is on the same scale as the residual
                // table above (which is in µm) instead of a bare step count the user must interpret.
                string microns = unitMicrons > 0
                    ? string.Format(CultureInfo.InvariantCulture, " (about {0:0.0} µm across the sensor)", Math.Abs(steps * unitMicrons))
                    : string.Empty;
                return string.Format(CultureInfo.InvariantCulture,
                    "This measurement includes a twist component of {0:+0.0;-0.0} steps{1} that no rigid-plane tilt adapter can correct " +
                    "(the sensor is warped, not merely tilted). It is not part of any move below and remains as residual tilt afterwards.",
                    steps, microns);
            }
        }

        /// <summary>
        /// Visible when the backfocus direction has never been measured by the wizard AND the current plan
        /// contains a backfocus move — the one case where a command could physically go the wrong way.
        /// </summary>
        public bool AssumedDirectionWarningVisible =>
            !screwInwardCurvatureSignIsMeasured && Preview.Plan.Moves.Any(m => m.Group == TiltMoveGroup.Backfocus);

        public string AssumedDirectionWarningText =>
            "The backfocus direction is assumed, not measured: the Tilt Adapter Wizard has never measured this adapter's " +
            "screw inward-curvature direction. If the assumption is wrong, the backfocus command will move the wrong way. " +
            "Re-run the wizard with curvature measurement enabled, or uncheck Backfocus.";

        /// <summary>Pitch-mismatch advisory passed in by the caller (empty ⇒ hidden).</summary>
        public string PitchMismatchWarning { get; }

        public bool PitchMismatchWarningVisible => !string.IsNullOrWhiteSpace(PitchMismatchWarning);

        /// <summary>Visible when the device's current motor positions are unknown (excursion enforcement degraded).</summary>
        public bool PositionsUnknownWarningVisible => positionsUnknown;

        public string PositionsUnknownWarningText =>
            "The device's current motor positions are unknown, so soft travel-limit (max excursion) enforcement is degraded — " +
            "the adapter could be driven farther from center than the configured limits allow. Verify positions in the vendor " +
            "app before approving large moves.";

        private void Replan() {
            var preview = replanner(applyTilt, applyBackfocus)
                ?? throw new InvalidOperationException("The tilt-device replanner returned a null preview.");
            Preview = preview;
            Moves = preview.Plan.Moves
                .Select(m => new TiltDeviceAdjustmentMoveRow(
                    EatCommands.Format(m), BuildSemanticText(m), BuildMoveKindLabel(m.Axis), m.Description, m.Group,
                    assumedDirection: !screwInwardCurvatureSignIsMeasured && m.Group == TiltMoveGroup.Backfocus))
                .ToArray();
            CornerResiduals = BuildCornerResiduals(preview.Plan.ResidualMicronsPerCorner);

            RaisePropertyChanged(nameof(Preview));
            RaisePropertyChanged(nameof(Moves));
            RaisePropertyChanged(nameof(HasMoves));
            RaisePropertyChanged(nameof(HasNoMoves));
            RaisePropertyChanged(nameof(MoveCountText));
            RaisePropertyChanged(nameof(ProceedButtonText));
            RaisePropertyChanged(nameof(ProceedDisabledReason));
            RaisePropertyChanged(nameof(ProceedDisabledReasonVisible));
            RaisePropertyChanged(nameof(CornerResiduals));
            RaisePropertyChanged(nameof(EstimatedDurationText));
            RaisePropertyChanged(nameof(HardLimitViolated));
            RaisePropertyChanged(nameof(HardLimitWarningVisible));
            RaisePropertyChanged(nameof(SoftLimitWarningVisible));
            RaisePropertyChanged(nameof(LimitWarningText));
            RaisePropertyChanged(nameof(TwistWarningVisible));
            RaisePropertyChanged(nameof(TwistWarningText));
            RaisePropertyChanged(nameof(AssumedDirectionWarningVisible));
            proceedCommand.NotifyCanExecuteChanged();
        }

        private bool CanProceed() {
            // Both groups off ⇒ nothing to approve; hard limit ⇒ physically unsafe. Cancel is always available.
            return Preview != null && (applyTilt || applyBackfocus) && !Preview.HardLimitViolated;
        }

        private void Proceed() {
            tcs.TrySetResult(TiltDeviceAdjustmentChoice.Proceeded(applyTilt, applyBackfocus, Preview.Plan));
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel() {
            tcs.TrySetResult(TiltDeviceAdjustmentChoice.Cancelled);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Short kind tag for the row badge, derived from the move's axis.</summary>
        internal static string BuildMoveKindLabel(TiltMoveAxis axis) {
            switch (axis) {
                case TiltMoveAxis.DiagonalA:
                case TiltMoveAxis.DiagonalB:
                    return "Corner";

                case TiltMoveAxis.EdgeVertical:
                case TiltMoveAxis.EdgeHorizontal:
                    return "Side";

                case TiltMoveAxis.Backfocus:
                    return "Backfocus";

                default:
                    return "Move";
            }
        }

        /// <summary>
        /// Builds the human-readable, screw-oriented description of a move from its per-corner step effect
        /// (wizard screw indices 1..4). Deliberately uses SIGNED STEPS (+ = the wizard's positive/clockwise
        /// step direction, − = the opposite) rather than a physical "up/down": whether a positive step raises
        /// or lowers a corner is rig-dependent (the ScrewInwardCurvatureSign the assumed-direction warning is
        /// about), so asserting up/down here could be wrong. The one-line legend in the dialog explains the sign.
        /// </summary>
        internal static string BuildSemanticText(TiltAdapterMove move) {
            if (move == null) {
                return string.Empty;
            }

            if (move.Axis == TiltMoveAxis.Backfocus) {
                string signed = move.Steps.ToString("+0;-0;0", CultureInfo.InvariantCulture);
                return string.Format(CultureInfo.InvariantCulture, "All four screws {0} steps together", signed);
            }

            var perCorner = move.PerCornerSteps;
            var positives = new List<int>();
            var negatives = new List<int>();
            int magnitude = 0;
            for (int i = 0; i < perCorner.Count; ++i) {
                int s = (int)Math.Round(perCorner[i], MidpointRounding.AwayFromZero);
                if (s > 0) {
                    positives.Add(i + 1);
                    magnitude = Math.Abs(s);
                } else if (s < 0) {
                    negatives.Add(i + 1);
                    magnitude = Math.Abs(s);
                }
            }

            // The move kind ("Corner"/"Side") is already shown in the row's badge, so it is not repeated here.
            var parts = new List<string>(2);
            if (positives.Count > 0) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} +{1}", FormatScrews(positives), magnitude));
            }
            if (negatives.Count > 0) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} -{1}", FormatScrews(negatives), magnitude));
            }
            return string.Format(CultureInfo.InvariantCulture, "{0} steps", string.Join(", ", parts));
        }

        /// <summary>"Screw 1" / "Screws 1 &amp; 2" / "Screws 1, 2 &amp; 3" for a list of wizard screw numbers.</summary>
        private static string FormatScrews(IReadOnlyList<int> screws) {
            if (screws.Count == 0) {
                return string.Empty;
            }
            if (screws.Count == 1) {
                return string.Format(CultureInfo.InvariantCulture, "Screw {0}", screws[0]);
            }
            var head = string.Join(", ", screws.Take(screws.Count - 1));
            return string.Format(CultureInfo.InvariantCulture, "Screws {0} & {1}", head, screws[screws.Count - 1]);
        }

        // Wizard screw index (1..4) → physical corner, per the device-connected calibration convention the
        // wizard's motor-position grid also uses (screw 1 = TR, 2 = TL, 3 = BL, 4 = BR). This dialog only ever
        // runs against a connected device, so the mapping is fixed.
        private static readonly string[] ScrewCornerLabels = { "TR", "TL", "BL", "BR" };

        private static IReadOnlyList<TiltDeviceCornerResidualRow> BuildCornerResiduals(IReadOnlyList<double> residualMicrons) {
            var rows = new TiltDeviceCornerResidualRow[residualMicrons.Count];
            for (int i = 0; i < residualMicrons.Count; ++i) {
                string label = i < ScrewCornerLabels.Length
                    ? string.Format(CultureInfo.InvariantCulture, "Screw {0} ({1})", i + 1, ScrewCornerLabels[i])
                    : string.Format(CultureInfo.InvariantCulture, "Screw {0}", i + 1);
                rows[i] = new TiltDeviceCornerResidualRow(label, FormatResidualMicrons(residualMicrons[i]));
            }
            return rows;
        }

        /// <summary>Signed one-decimal residual, e.g. "+0.9 µm" / "-1.2 µm" / "0.0 µm".</summary>
        internal static string FormatResidualMicrons(double microns) {
            return string.Format(CultureInfo.InvariantCulture, "{0:+0.0;-0.0;0.0} µm", microns);
        }

        /// <summary>Human-friendly duration: "0 s", "~30 s", "~1 min", "~1 min 35 s". Rounds up (never under-promises).</summary>
        internal static string FormatDuration(double estimatedSeconds) {
            if (!(estimatedSeconds > 0)) {
                return "0 s";
            }
            int total = (int)Math.Ceiling(estimatedSeconds);
            if (total < 60) {
                return string.Format(CultureInfo.InvariantCulture, "~{0} s", total);
            }
            int minutes = total / 60;
            int seconds = total % 60;
            return seconds == 0
                ? string.Format(CultureInfo.InvariantCulture, "~{0} min", minutes)
                : string.Format(CultureInfo.InvariantCulture, "~{0} min {1} s", minutes, seconds);
        }

        public void Dispose() {
            // Window dismissed without an explicit pick (e.g. the X button) ⇒ treat as Cancel. TrySetResult is a
            // no-op if a button already set the result.
            tcs.TrySetResult(TiltDeviceAdjustmentChoice.Cancelled);
        }
    }
}
