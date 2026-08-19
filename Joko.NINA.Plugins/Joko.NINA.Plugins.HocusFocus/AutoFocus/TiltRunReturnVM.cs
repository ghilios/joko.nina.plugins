#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    /// <summary>
    /// The "Return to this run" panel under the sensor-model history grid: everything the user needs to decide,
    /// shown BEFORE they click, so the click itself is already informed.
    ///
    /// <para>Built whole and swapped in, like <see cref="TiltAdapterGuidanceVM"/> — no per-property INPC.</para>
    ///
    /// <para>Selecting a history row only ever populates this. Nothing here moves hardware except the explicit
    /// button, and the panel self-dismisses when a new analysis lands (which clears the selection).</para>
    /// </summary>
    public sealed class TiltRunReturnVM {
        private static readonly TiltRunReturnVM HiddenInstance = new TiltRunReturnVM();

        public static TiltRunReturnVM Hidden => HiddenInstance;

        private TiltRunReturnVM() {
        }

        public bool IsVisible { get; private set; }

        public string Header { get; private set; } = string.Empty;

        public string BodyText { get; private set; } = string.Empty;

        /// <summary>Per-corner or per-screw lines describing the motion. Empty when there is nothing to show.</summary>
        public IReadOnlyList<string> MotionLines { get; private set; } = Array.Empty<string>();

        public bool HasMotionLines => MotionLines.Count > 0;

        public string WarningText { get; private set; } = string.Empty;

        public bool HasWarning => !string.IsNullOrEmpty(WarningText);

        /// <summary>Always-visible italic note about what the numbers do and do not mean. Not an anomaly, a property of the method.</summary>
        public string CaveatText { get; private set; } = string.Empty;

        public bool HasCaveat => !string.IsNullOrEmpty(CaveatText);

        public bool ShowDriveButton { get; private set; }

        public string DriveButtonText { get; private set; } = string.Empty;

        /// <summary>The run this panel is about; the command re-reads it rather than trusting a stale selection.</summary>
        public SensorParaboloidTiltHistoryModel TargetRun { get; private set; }

        public TiltRevertTarget Target { get; private set; }

        /// <summary>
        /// Builds the panel for a selected history row. <paramref name="isNewestRun"/> suppresses the action: the
        /// adapter should already be in that state, and offering to "return" to it invites a pointless move.
        /// </summary>
        public static TiltRunReturnVM Build(
                SensorParaboloidTiltHistoryModel target,
                TiltRevertTarget revertTarget,
                bool isNewestRun,
                bool isMotorized,
                bool deviceConnected,
                bool deviceBusy,
                TiltGuidanceAngleUnit angleUnit,
                IScrewLabelProvider labels = null) {
            if (target == null) {
                return Hidden;
            }

            var vm = new TiltRunReturnVM {
                IsVisible = true,
                TargetRun = target,
                Target = revertTarget,
                Header = string.Format(CultureInfo.CurrentCulture, "Return to run #{0}{1}", target.HistoryId, DescribeTime(target))
            };

            if (isNewestRun) {
                vm.BodyText = "This is the most recent run — the adapter should already be in this state. If you have adjusted anything since, re-run the Inspector first.";
                return vm;
            }
            if (revertTarget == null || !revertTarget.IsAvailable) {
                vm.BodyText = revertTarget?.UnavailableReason ?? "This run cannot be returned to.";
                return vm;
            }

            vm.MotionLines = DescribeMotion(revertTarget, isMotorized, angleUnit, labels);
            if (vm.MotionLines.Count == 0) {
                vm.BodyText = revertTarget.Mechanism == TiltRevertMechanism.DevicePositions
                    ? "The adapter is already at this run's recorded positions — nothing to send."
                    : "This run and the current measurement are within measurement noise of each other — no meaningful adjustment to make.";
                return vm;
            }

            if (revertTarget.Mechanism == TiltRevertMechanism.DevicePositions) {
                vm.BodyText = "Drive each motor back to the position recorded at this run:";
                if (Math.Abs(revertTarget.TwistSteps) >= 1.0) {
                    // Two states the device actually reached differ by a rigid plane, so this should be ~0.
                    vm.WarningText = string.Format(
                        CultureInfo.CurrentCulture,
                        "These recorded positions differ from the current positions by a twist of ±{0:0.#} steps, which the adapter cannot make — position tracking may have drifted since this run (lost steps, or a resync). The adapter will land on the closest reachable plane.",
                        Math.Abs(revertTarget.TwistSteps));
                }
            } else {
                vm.BodyText = isMotorized
                    ? (revertTarget.MotorsUnchangedSinceRun
                        ? "The motor counters are unchanged since this run, so the adapter was moved by something other than these motors (screws turned by hand, a re-seat, the vendor app, or the simulator's own tilt controls). Driving the motors back to their recorded positions would therefore do nothing. The moves below come from the two fitted models instead:"
                        : "No motor positions were recorded at this run, so the moves below are estimated from the two fitted models — guidance, not ground truth:")
                    : "Turn each screw as shown to bring the adapter back to the state measured at this run:";
                vm.CaveatText =
                    "Computed as the difference between this run's fitted model and the current one, so it is only as trustworthy as those two fits. " +
                    "It assumes nothing but the tilt adjustment changed between them: if the camera was rotated, the adapter re-seated, or the screws were never touched, this difference is measurement drift rather than adjustment.";
            }

            // The drive button is offered for BOTH mechanisms on a motorized rig. When the counters are unchanged
            // but the sensor moved, the differential is the only actionable answer -- and it is the same kind of
            // model-derived plan Automatic Adjustment already sends.
            if (isMotorized) {
                if (!deviceConnected) {
                    var recorded = DescribeRecordedPositions(target);
                    vm.BodyText = recorded.Count > 0
                        ? "Connect the tilt adapter device to drive it back to this run's state. Its recorded positions were: " + string.Join(" · ", recorded)
                        : "Connect the tilt adapter device to drive it back to this run's state.";
                } else if (deviceBusy) {
                    vm.BodyText += " (The tilt adapter device is busy with another operation; try again when it finishes.)";
                } else {
                    vm.ShowDriveButton = true;
                    vm.DriveButtonText = string.Format(
                        CultureInfo.CurrentCulture,
                        revertTarget.Mechanism == TiltRevertMechanism.DevicePositions
                            ? "Drive Adapter to Run #{0} Positions"
                            : "Drive Adapter Back to Run #{0}",
                        target.HistoryId);
                }
            }

            return vm;
        }

        private static string DescribeTime(SensorParaboloidTiltHistoryModel target) {
            var display = target.CapturedAtDisplay;
            return string.IsNullOrEmpty(display) ? string.Empty : $" ({display})";
        }

        private static IReadOnlyList<string> DescribeRecordedPositions(SensorParaboloidTiltHistoryModel target) {
            var state = target.AdapterState;
            if (state?.HasPositions != true) {
                return Array.Empty<string>();
            }
            return TiltAdapterCorner.InDeviceMotorOrder
                .Select(corner => string.Format(CultureInfo.CurrentCulture, "{0} {1}", corner.Label, state.PerMotorSteps[corner.DeviceMotorNumber - 1]))
                .ToList();
        }

        private static IReadOnlyList<string> DescribeMotion(TiltRevertTarget target, bool isMotorized, TiltGuidanceAngleUnit angleUnit, IScrewLabelProvider labels = null) {
            var lines = new List<string>();
            for (int wizardIndex = 0; wizardIndex < target.StepsPerScrew.Count; wizardIndex++) {
                // Reuses the guidance table's formatter, so the glyph vocabulary and the noise floor are the same
                // ones the user already reads elsewhere: rotation glyphs only, never the motion arrows.
                var amount = TiltAdapterGuidanceVM.FormatAmount(target.StepsPerScrew[wizardIndex], isMotorized, angleUnit);
                if (amount == TiltAdapterGuidanceVM.NoAdjustmentGlyph) {
                    continue;
                }
                var corner = TiltAdapterCorner.InWizardScrewOrder[wizardIndex];
                var name = TiltScrewLabels.Resolve(labels, corner.WizardScrewNumber);
                // A motorized rig keeps the corner tag alongside the name: these lines describe moves about to
                // be sent to hardware, and the corner is how the vendor app and the device's own reports
                // identify the motor.
                lines.Add(isMotorized
                    ? string.Format(CultureInfo.CurrentCulture, "{0} ({1}): {2}", name, corner.Label, amount)
                    : string.Format(CultureInfo.CurrentCulture, "{0}: {1}", name, amount));
            }
            return lines;
        }
    }
}
