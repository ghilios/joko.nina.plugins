#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices {

    /// <summary>
    /// Turns a per-screw step target into the exact <see cref="TiltDevicePlanPreview"/> the approval dialog
    /// shows and the executor sends. Shared by every surface that drives the device from a target vector —
    /// the Aberration Inspector's Automatic Adjustment and the wizard's Manual Adjustment panel — so the two
    /// can never diverge in what "the plan" means.
    /// </summary>
    public static class TiltDevicePlanPreviewBuilder {

        /// <summary>
        /// Builds one replanner invocation's preview: plans the moves for the given group toggles, then asks
        /// the controller (via the interface — never a downcast) to order them for minimal peak excursion so
        /// the approval dialog shows the EXACT execution order (WYSIWYG). A TiltDeviceLimitException from the
        /// ordering call means either a single move exceeds the per-command cap or every ordering would
        /// exceed the max excursion — surfaced as a blocking preview (unordered moves shown, matching the
        /// design doc) rather than propagated, so the dialog can display it instead of crashing. Residuals,
        /// twist, and the time estimate are unaffected by ordering, so they carry over unchanged from the
        /// original plan.
        /// </summary>
        public static TiltDevicePlanPreview Build(
            IReadOnlyList<double> sPerScrew,
            bool includeTilt,
            bool includeBackfocus,
            double unitMicrons,
            int maxStepsPerCommand,
            ITiltMotionController controller) {
            var plan = TiltMovePlanner.Plan(sPerScrew, includeTilt, includeBackfocus, unitMicrons, maxStepsPerCommand);
            try {
                var ordered = controller.OrderForMinimalPeakExcursion(plan.Moves);
                // The controller may PREPEND a backfocus bias so a differential tilt correction never drives a
                // motor below 0. That bias is a genuine piston -- it shifts backfocus -- so the residual has to
                // be recomputed from what will actually be sent. Carrying the unbiased plan's residual here
                // would silently under-report the very backfocus error the bias introduces.
                var applied = new double[4];
                foreach (var move in ordered) {
                    for (int i = 0; i < 4; ++i) {
                        applied[i] += move.PerCornerSteps[i];
                    }
                }
                var residual = new double[4];
                for (int i = 0; i < 4; ++i) {
                    residual[i] = (applied[i] - sPerScrew[i]) * unitMicrons;
                }

                // The bias is the uniform surplus the controller added on top of what was planned; it lands
                // equally on all four corners (it is a piston), so the smallest per-corner surplus is it.
                var planned = new double[4];
                foreach (var move in plan.Moves) {
                    for (int i = 0; i < 4; ++i) {
                        planned[i] += move.PerCornerSteps[i];
                    }
                }
                int biasSteps = (int)Math.Round(Enumerable.Range(0, 4).Min(i => applied[i] - planned[i]));
                // A prepended bias also adds real execution time; scale the estimate by the per-move rate the
                // planner used rather than carrying a move count that no longer matches.
                double perMoveSeconds = plan.Moves.Count > 0 ? plan.EstimatedSeconds / plan.Moves.Count : 0.0;
                var orderedPlan = new TiltAdapterMovePlan(ordered, residual, plan.TwistResidualSteps, ordered.Count * perMoveSeconds, Math.Max(0, biasSteps));
                return new TiltDevicePlanPreview(orderedPlan, hardLimitViolated: false, limitWarning: string.Empty);
            } catch (TiltDeviceLimitException ex) {
                return new TiltDevicePlanPreview(plan, hardLimitViolated: true, limitWarning: ex.Message);
            }
        }
    }
}
