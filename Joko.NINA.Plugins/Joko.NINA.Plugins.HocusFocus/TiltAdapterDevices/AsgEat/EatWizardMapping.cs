#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat {

    /// <summary>
    /// Maps the Tilt Adapter Calibration Wizard's <see cref="WizardStep"/> sequence onto device moves, and
    /// defines the wizard-screw &lt;-&gt; device-corner correspondence that a device-linked calibration
    /// establishes.
    ///
    /// CRITICAL: the wizard's per-step instruction text (<c>TiltAdapterWizardVM.StepInstructionsText</c>)
    /// says "motor 1", "motor 3", etc. -- these are WIZARD-SCREW indices (the user's own labeled screws
    /// 1..4), NOT device motor numbers. Connecting the device during calibration DEFINES the
    /// correspondence: wizard-screw 1 == TR, wizard-screw 2 == TL, wizard-screw 3 == BL, wizard-screw 4 ==
    /// BR -- the wizard's opposite pairs (1,3),(2,4) line up with the device's opposite pairs (TR,BL) and
    /// (TL,BR). Every mapping below is expressed purely in <see cref="TiltMoveAxis"/>/
    /// <see cref="TiltAdapterMove"/> terms (wizard-index per-corner space, per <c>TiltAdapterMove</c>'s
    /// (s1,s2,s3,s4) unit-effect table); translating an axis to its ASG EAT wire mnemonic (e.g.
    /// DiagonalA -&gt; "tr") is <c>EatCommands</c>'s job, not this file's -- keep those concerns separate.
    /// </summary>
    public static class EatWizardMapping {

        /// <summary>
        /// The device corner label for a wizard screw index (1-based): 1-&gt;TR, 2-&gt;TL, 3-&gt;BL,
        /// 4-&gt;BR, per the device-linked-calibration correspondence documented on this class. Used to
        /// label the live per-screw positions display while connected.
        /// </summary>
        public static string CornerLabelForWizardScrew(int wizardScrew) {
            switch (wizardScrew) {
                case 1: return "TR";
                case 2: return "TL";
                case 3: return "BL";
                case 4: return "BR";
                default:
                    throw new ArgumentOutOfRangeException(nameof(wizardScrew), wizardScrew, "Wizard screw index must be 1-4.");
            }
        }

        /// <summary>
        /// The device move that realizes a calibration wizard step's instruction, or null for
        /// <see cref="WizardStep.Baseline"/> (a measurement-only step -- no move) and, when <paramref
        /// name="measuredFinalRebaseline"/> is true, for <see cref="WizardStep.Complete"/> (the optional
        /// measured <see cref="WizardStep.ReBaseline3"/> step already restored the device, so Complete has
        /// nothing left to undo). <paramref name="appliedSteps"/> is N, the already-rounded integer step
        /// count (the wizard's persisted per-calibration-step applied amount for a stepper device).
        ///
        /// Each case is annotated with the exact <c>StepInstructionsText</c> wording (four-screw,
        /// isStepper) it realizes. Two variants of the executed sequence both sum to (0,0,0,0) per corner --
        /// the device returns to baseline by the end either way; see EatWizardMappingTests for both
        /// full-sequence regressions that pin this: the standard six-move sequence (AllInward (all motors), ReBaseline1,
        /// Screw1, ReBaseline2, Screw2, Complete, <paramref name="measuredFinalRebaseline"/> false) and the
        /// optional seven-move sequence that substitutes a measured ReBaseline3 restore for Complete's move
        /// (<paramref name="measuredFinalRebaseline"/> true, Complete becomes a no-move).
        /// </summary>
        public static TiltAdapterMove MoveForStep(WizardStep step, int appliedSteps, bool measuredFinalRebaseline = false) {
            switch (step) {
                case WizardStep.Baseline:
                    // "Ensure all screws are at their starting position, then click Run Measurement." --
                    // measurement only, no move.
                    return null;

                case WizardStep.AllInward:
                    // "Apply +N steps to EVERY motor, then click Run Measurement." -> all four wizard
                    // screws +N -> (+N,+N,+N,+N) = Backfocus(+N). Described as "All Motors", never "Inward":
                    // whether +N is physically inward is exactly what this step measures.
                    return new TiltAdapterMove(
                        TiltMoveAxis.Backfocus, appliedSteps, TiltMoveGroup.Backfocus,
                        $"Wizard All Motors: {FormatSigned(appliedSteps)} backfocus");

                case WizardStep.ReBaseline1:
                    // "Apply -N steps to every motor, returning to the baseline position, then click Run
                    // Measurement." -> undoes AllInward -> (-N,-N,-N,-N) = Backfocus(-N).
                    return new TiltAdapterMove(
                        TiltMoveAxis.Backfocus, -appliedSteps, TiltMoveGroup.Backfocus,
                        $"Wizard Re-Baseline 1: {FormatSigned(-appliedSteps)} backfocus");

                case WizardStep.Screw1:
                    // "Apply +N steps to motor 1 and -N steps to motor 3, then click Run Measurement." ->
                    // wizard-screw1 +N, wizard-screw3 -N -> (+N,0,-N,0) = DiagonalA(+N).
                    return new TiltAdapterMove(
                        TiltMoveAxis.DiagonalA, appliedSteps, TiltMoveGroup.Tilt,
                        $"Wizard Screw 1: {FormatSigned(appliedSteps)} diagonal-A");

                case WizardStep.ReBaseline2:
                    // "Apply -N steps to motor 1 and +N steps to motor 3, returning to the baseline
                    // position, then click Run Measurement." -> undoes Screw1 -> (-N,0,+N,0) =
                    // DiagonalA(-N).
                    return new TiltAdapterMove(
                        TiltMoveAxis.DiagonalA, -appliedSteps, TiltMoveGroup.Tilt,
                        $"Wizard Re-Baseline 2: {FormatSigned(-appliedSteps)} diagonal-A");

                case WizardStep.Screw2:
                    // "Apply +N steps to motor 2 and -N steps to motor 4, then click Run Measurement." ->
                    // wizard-screw2 +N, wizard-screw4 -N -> (0,+N,0,-N) = DiagonalB(+N).
                    return new TiltAdapterMove(
                        TiltMoveAxis.DiagonalB, appliedSteps, TiltMoveGroup.Tilt,
                        $"Wizard Screw 2: {FormatSigned(appliedSteps)} diagonal-B");

                case WizardStep.ReBaseline3:
                    // "Apply -N steps to motor 2 and +N steps to motor 4, returning to the baseline
                    // position, then click Run Measurement." -- the optional MEASURED restore after Screw2
                    // (gives screw 2 the same drift-cancelling re-baseline symmetry Screw1 already has) ->
                    // undoes Screw2 -> (0,-N,0,+N) = DiagonalB(-N). Identical move to the un-measured
                    // Complete restore below; the two are mutually exclusive per run (see
                    // measuredFinalRebaseline).
                    return new TiltAdapterMove(
                        TiltMoveAxis.DiagonalB, -appliedSteps, TiltMoveGroup.Tilt,
                        $"Wizard Re-Baseline 3: {FormatSigned(-appliedSteps)} diagonal-B (restore, measured)");

                case WizardStep.Complete:
                    // "Return all motors to their original position." -- the restore move after Screw2 ->
                    // undoes Screw2 -> (0,-N,0,+N) = DiagonalB(-N). Confirmed by the full-sequence trace:
                    // this is exactly what returns the device to baseline (0,0,0,0) after the 6-step run.
                    // When measuredFinalRebaseline is true, ReBaseline3 (above) already sent this exact
                    // move and was itself measured (unlike this un-measured restore) -- Complete becomes a
                    // no-move to avoid sending the restore twice.
                    return measuredFinalRebaseline
                        ? null
                        : new TiltAdapterMove(
                            TiltMoveAxis.DiagonalB, -appliedSteps, TiltMoveGroup.Tilt,
                            $"Wizard Complete: {FormatSigned(-appliedSteps)} diagonal-B (restore)");

                default:
                    throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown wizard step.");
            }
        }

        /// <summary>
        /// The inverse of <paramref name="move"/>: same axis and group, negated
        /// <see cref="TiltAdapterMove.Steps"/>. Used for failure-recovery -- if a wizard-driven move fails
        /// or is cancelled mid-flight, sending the inverse move returns the device to where it was before.
        /// Null-safe: null in, null out.
        /// </summary>
        public static TiltAdapterMove InverseMove(TiltAdapterMove move) {
            if (move == null) {
                return null;
            }
            return new TiltAdapterMove(move.Axis, -move.Steps, move.Group, $"Inverse of: {move.Description}");
        }

        private static string FormatSigned(int steps) => steps >= 0 ? $"+{steps}" : steps.ToString();
    }
}
