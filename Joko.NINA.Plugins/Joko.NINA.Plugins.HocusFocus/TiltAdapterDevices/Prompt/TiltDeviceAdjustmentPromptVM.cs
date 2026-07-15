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
    /// One row of the approval dialog's move list. <see cref="WireCommand"/> is the exact signed string
    /// sent to the device (e.g. <c>tr,150</c>) — the safety-critical element the dialog displays verbatim.
    /// </summary>
    public sealed class TiltDeviceAdjustmentMoveRow {

        public TiltDeviceAdjustmentMoveRow(string wireCommand, string description, TiltMoveGroup group) {
            WireCommand = wireCommand ?? string.Empty;
            Description = description ?? string.Empty;
            Group = group;
        }

        /// <summary>The exact wire command string, e.g. <c>tr,150</c> or <c>bf,-20</c>.</summary>
        public string WireCommand { get; }

        /// <summary>Human-readable description of the move.</summary>
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
        private readonly RelayCommand proceedCommand;

        private bool applyTilt = true;
        private bool applyBackfocus = true;

        public event EventHandler RequestClose;

        public TiltDeviceAdjustmentPromptVM(
            Func<bool, bool, TiltDevicePlanPreview> replanner,
            bool screwInwardCurvatureSignIsMeasured,
            string pitchMismatchWarning,
            bool positionsUnknown) {
            this.replanner = replanner ?? throw new ArgumentNullException(nameof(replanner));
            this.screwInwardCurvatureSignIsMeasured = screwInwardCurvatureSignIsMeasured;
            this.positionsUnknown = positionsUnknown;
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

        /// <summary>"No commands" / "1 command" / "N commands" for the footer summary.</summary>
        public string MoveCountText =>
            Moves.Count == 0 ? "No commands"
            : Moves.Count == 1 ? "1 command"
            : string.Format(CultureInfo.InvariantCulture, "{0} commands", Moves.Count);

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

        public string TwistWarningText => string.Format(
            CultureInfo.InvariantCulture,
            "This measurement includes a twist component of {0:+0.0;-0.0} steps that NO rigid-plane tilt adapter can correct. " +
            "It is not part of any command below and will remain as residual tilt after the moves.",
            Preview.Plan.TwistResidualSteps);

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
                .Select(m => new TiltDeviceAdjustmentMoveRow(EatCommands.Format(m), m.Description, m.Group))
                .ToArray();
            CornerResiduals = BuildCornerResiduals(preview.Plan.ResidualMicronsPerCorner);

            RaisePropertyChanged(nameof(Preview));
            RaisePropertyChanged(nameof(Moves));
            RaisePropertyChanged(nameof(HasMoves));
            RaisePropertyChanged(nameof(HasNoMoves));
            RaisePropertyChanged(nameof(MoveCountText));
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

        private static IReadOnlyList<TiltDeviceCornerResidualRow> BuildCornerResiduals(IReadOnlyList<double> residualMicrons) {
            var rows = new TiltDeviceCornerResidualRow[residualMicrons.Count];
            for (int i = 0; i < residualMicrons.Count; ++i) {
                rows[i] = new TiltDeviceCornerResidualRow(
                    string.Format(CultureInfo.InvariantCulture, "Screw {0}", i + 1),
                    FormatResidualMicrons(residualMicrons[i]));
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
