#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// Pure mapping from the in-memory review labels (<see cref="StarReviewRunLabels"/>) to the optimizer's
    /// objective-side <see cref="FrameLabels"/>/<see cref="LabelBox"/> form, so the wizard's re-optimize-with-labels
    /// path can feed the recall/precision objective term without going through disk. Mirrors the box rule the offline
    /// <c>optimize --labels</c> path uses (<c>OptimizationDiagnosticRunner.ToBoxes</c>):
    /// <list type="bullet">
    /// <item>A box that already carries explicit dimensions (<see cref="StarReviewLabelBox.HasSize"/>) is taken as-is
    /// (top-left <c>X,Y</c> + <c>W,H</c>).</item>
    /// <item>A legacy point-only box (no <c>W/H</c>) is widened to a <c>2·radius</c> box CENTERED on its <c>(X,Y)</c>,
    /// where <c>radius = position.RadiusPx ?? run.RadiusPx ?? DefaultRadiusPx</c>.</item>
    /// </list>
    /// Recall targets are <see cref="StarReviewPositionLabels.Missed"/> + <see cref="StarReviewPositionLabels.WronglyRejected"/>
    /// (kept in their own lists here — <see cref="RunEvaluationData.ApplyLabelScores"/> unions them); precision targets
    /// are <see cref="StarReviewPositionLabels.ShouldReject"/>.
    /// </summary>
    public static class LabelConverter {

        /// <summary>Default fallback radius for legacy point-only labels — matches
        /// <see cref="StarReviewLabelStore.DefaultRadiusPx"/> and the offline harness default.</summary>
        public const double DefaultRadiusPx = StarReviewLabelStore.DefaultRadiusPx;

        /// <summary>
        /// Maps each <see cref="StarReviewPositionLabels"/> to a <see cref="FrameLabels"/> (same focuser position),
        /// converting every <see cref="StarReviewLabelBox"/> into a <see cref="LabelBox"/> via the box rule above.
        /// Returns null for null/empty input (no positions ⇒ no labels to score).
        /// </summary>
        public static IReadOnlyList<FrameLabels> ToFrameLabels(StarReviewRunLabels runLabels) {
            if (runLabels?.Positions == null || runLabels.Positions.Count == 0) {
                return null;
            }

            var runRadius = runLabels.RadiusPx ?? DefaultRadiusPx;
            var result = new List<FrameLabels>(runLabels.Positions.Count);
            foreach (var pos in runLabels.Positions) {
                var radius = pos.RadiusPx ?? runRadius;
                result.Add(new FrameLabels {
                    FocuserPosition = pos.FocuserPosition,
                    RadiusPx = radius,
                    Missed = ToBoxes(pos.Missed, radius),
                    ShouldReject = ToBoxes(pos.ShouldReject, radius),
                    WronglyRejected = ToBoxes(pos.WronglyRejected, radius)
                });
            }
            return result;
        }

        /// <summary>
        /// Maps a list of review label boxes to objective <see cref="LabelBox"/>es: an explicit-size box is taken
        /// as-is; a legacy point-only box (no W/H) is widened to a <c>2·radiusPx</c> box centered on its (X,Y).
        /// Never null (returns an empty list for null/empty input).
        /// </summary>
        private static IReadOnlyList<LabelBox> ToBoxes(List<StarReviewLabelBox> boxes, double radiusPx) {
            if (boxes == null || boxes.Count == 0) {
                return Array.Empty<LabelBox>();
            }
            var side = Math.Max(1e-9, 2.0 * radiusPx);
            var result = new List<LabelBox>(boxes.Count);
            foreach (var b in boxes) {
                if (b.HasSize) {
                    result.Add(new LabelBox(b.X, b.Y, b.W.Value, b.H.Value));
                } else {
                    result.Add(new LabelBox(b.X - side / 2.0, b.Y - side / 2.0, side, side));
                }
            }
            return result;
        }
    }
}
