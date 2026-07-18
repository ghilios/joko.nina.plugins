#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Globalization;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices {

    /// <summary>
    /// Topology-specific minimal-move planner. A 4-corner coupled device (like the ASG EAT) can only
    /// reach a 3-D subspace of the 4-D per-screw target space in one move per generator; a future
    /// 3-corner independent device would implement this interface with a trivially different planner
    /// (one move per screw). See the design doc's "Core algorithm" section.
    /// </summary>
    public interface ITiltMovePlanner {

        /// <summary>
        /// Decomposes a per-screw signed step target vector (wizard indices 1..4, real-valued) into a
        /// minimal set of device moves, honoring the Tilt/Backfocus group toggles and an optional
        /// per-command step cap.
        /// </summary>
        /// <param name="sPerScrew">Per-screw signed step targets, wizard indices 1..4 at [0..3].</param>
        /// <param name="includeTilt">When false, no DiagonalA/DiagonalB/EdgeVertical/EdgeHorizontal move is emitted.</param>
        /// <param name="includeBackfocus">When false, no Backfocus move is emitted.</param>
        /// <param name="unitMicrons">Microns represented by one step, for the residual report.</param>
        /// <param name="maxStepsPerCommand">
        /// Largest |Steps| a single emitted move may carry; larger moves are split into same-axis moves
        /// whose signed magnitudes sum to the original. Default (<see cref="int.MaxValue"/>) never splits.
        /// </param>
        /// <param name="perMoveSeconds">Seconds attributed to each emitted move for the time estimate.</param>
        TiltAdapterMovePlan Plan(
            IReadOnlyList<double> sPerScrew,
            bool includeTilt,
            bool includeBackfocus,
            double unitMicrons,
            int maxStepsPerCommand = int.MaxValue,
            double perMoveSeconds = 10);
    }

    /// <summary>
    /// Pure, static core of the minimal-move decomposition described in the design doc's "Core
    /// algorithm — minimal-move decomposition (4-corner coupled)" section. Everything here is a pure
    /// function of its inputs — no device I/O, no NINA services — so it is directly unit-testable and
    /// safe to call from a background thread.
    ///
    /// Orthogonal decomposition of the target vector s = (s1, s2, s3, s4) (wizard screw indices 1..4):
    ///   a = (s1 − s3) / 2   → DiagonalA (D1) component
    ///   b = (s2 − s4) / 2   → DiagonalB (D2) component
    ///   f = (s1 + s2 + s3 + s4) / 4   → Backfocus (BF) component (the mean)
    ///   t = (s1 − s2 + s3 − s4) / 4   → TWIST — physically unreachable by any rigid-plane device;
    ///                                    reported, NEVER planned.
    /// a, b, f, t are an orthogonal basis of R^4: s1 = a+f+t, s2 = b+f−t, s3 = −a+f+t, s4 = −b+f−t.
    ///
    /// The group split happens in COMMAND space: Backfocus = the BF projection f of the raw target s;
    /// Tilt = the D1/D2 projections a, b of the raw target s. Because subtracting the mean f does not
    /// change a or b, any off-center-curvature linear leakage in s automatically stays in the tilt
    /// group without any explicit "subtract f first" step — do not refactor this into physical-origin
    /// space, that would silently drop the leakage term.
    /// </summary>
    public static class TiltMovePlanner {

        /// <summary>
        /// Computes the exact orthogonal decomposition of a 4-corner target vector. See the class
        /// remarks for the formulas; this is real-valued and un-rounded — <see cref="Plan"/> rounds
        /// a, b, f independently after calling this.
        /// </summary>
        internal static (double a, double b, double f, double t) Decompose(IReadOnlyList<double> s) {
            ValidateFourCorners(s, nameof(s));

            double a = (s[0] - s[2]) / 2.0;
            double b = (s[1] - s[3]) / 2.0;
            double f = (s[0] + s[1] + s[2] + s[3]) / 4.0;
            double t = (s[0] - s[1] + s[2] - s[3]) / 4.0;
            return (a, b, f, t);
        }

        /// <summary>
        /// Rounds a real-valued component to the nearest integer step, ties away from zero (matches
        /// the signed-step rounding convention used elsewhere in this feature, e.g. TiltScrewGuidanceRow).
        /// </summary>
        internal static int RoundToStep(double value) {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Builds the minimal tilt-group move list (before cap-splitting) from independently-rounded
        /// diagonal components ra, rb. Never forces a common magnitude to save a move: an edge move is
        /// only emitted when ra and rb are ALREADY equal (or negatives of each other); otherwise up to
        /// two diagonal moves are emitted, one per nonzero component.
        /// </summary>
        internal static IReadOnlyList<TiltAdapterMove> BuildTiltMoves(int ra, int rb) {
            if (ra == 0 && rb == 0) {
                return Array.Empty<TiltAdapterMove>();
            }

            if (ra == rb) {
                // ra == rb and not both zero (handled above), so this magnitude is nonzero.
                return new[] { NewMove(TiltMoveAxis.EdgeVertical, ra, TiltMoveGroup.Tilt) };
            }

            if (ra == -rb) {
                // ra == -rb and not both zero (handled above), so this magnitude is nonzero.
                return new[] { NewMove(TiltMoveAxis.EdgeHorizontal, ra, TiltMoveGroup.Tilt) };
            }

            var moves = new List<TiltAdapterMove>(2);
            if (ra != 0) {
                moves.Add(NewMove(TiltMoveAxis.DiagonalA, ra, TiltMoveGroup.Tilt));
            }
            if (rb != 0) {
                moves.Add(NewMove(TiltMoveAxis.DiagonalB, rb, TiltMoveGroup.Tilt));
            }
            return moves;
        }

        /// <summary>Builds the single backfocus move for a rounded component rf, or null if rf == 0.</summary>
        internal static TiltAdapterMove BuildBackfocusMove(int rf) {
            if (rf == 0) {
                return null;
            }
            return NewMove(TiltMoveAxis.Backfocus, rf, TiltMoveGroup.Backfocus);
        }

        /// <summary>
        /// Splits a signed step magnitude into chunks of at most <paramref name="maxStepsPerCommand"/>,
        /// same sign, summing back to <paramref name="steps"/>. Returns a single-element sequence
        /// (unsplit) when already within the cap. Because the axes this feeds are orthogonal, the total
        /// move count Σ ceil(|mᵢ|/cap) produced by splitting each move independently is provably minimal
        /// under the cap.
        /// </summary>
        internal static IReadOnlyList<int> SplitStepsForCap(int steps, int maxStepsPerCommand) {
            if (maxStepsPerCommand < 1) {
                throw new ArgumentOutOfRangeException(nameof(maxStepsPerCommand), maxStepsPerCommand, "maxStepsPerCommand must be at least 1.");
            }

            int absSteps = Math.Abs(steps);
            if (absSteps <= maxStepsPerCommand) {
                return new[] { steps };
            }

            int sign = Math.Sign(steps);
            var chunks = new List<int>();
            int remaining = absSteps;
            while (remaining > 0) {
                int chunk = Math.Min(maxStepsPerCommand, remaining);
                chunks.Add(sign * chunk);
                remaining -= chunk;
            }
            return chunks;
        }

        /// <summary>
        /// Applies <see cref="SplitStepsForCap"/> to every move in <paramref name="moves"/> whose
        /// |Steps| exceeds the cap, preserving axis and group and replacing the single move with the
        /// split sequence in place. Moves already within the cap pass through unchanged.
        /// </summary>
        internal static IReadOnlyList<TiltAdapterMove> ApplyCap(IReadOnlyList<TiltAdapterMove> moves, int maxStepsPerCommand) {
            if (moves == null || moves.Count == 0) {
                return Array.Empty<TiltAdapterMove>();
            }

            var result = new List<TiltAdapterMove>(moves.Count);
            foreach (var move in moves) {
                if (Math.Abs(move.Steps) <= maxStepsPerCommand) {
                    result.Add(move);
                    continue;
                }

                var parts = SplitStepsForCap(move.Steps, maxStepsPerCommand);
                for (int i = 0; i < parts.Count; ++i) {
                    string description = string.Format(
                        CultureInfo.InvariantCulture, "{0} (part {1} of {2})", DescribeMove(move.Axis, parts[i]), i + 1, parts.Count);
                    result.Add(new TiltAdapterMove(move.Axis, parts[i], move.Group, description));
                }
            }
            return result;
        }

        /// <summary>
        /// Decomposes <paramref name="sPerScrew"/> into a minimal set of device moves per the design
        /// doc's "Core algorithm" section, applying the Tilt/Backfocus group toggles and the optional
        /// per-command step cap, and reports the per-corner residual (which inherently includes both
        /// rounding error and the unreachable twist, since twist is never applied) plus the raw twist.
        /// </summary>
        public static TiltAdapterMovePlan Plan(
            IReadOnlyList<double> sPerScrew,
            bool includeTilt,
            bool includeBackfocus,
            double unitMicrons,
            int maxStepsPerCommand = int.MaxValue,
            double perMoveSeconds = 10) {
            ValidateFourCorners(sPerScrew, nameof(sPerScrew));
            if (maxStepsPerCommand < 1) {
                throw new ArgumentOutOfRangeException(nameof(maxStepsPerCommand), maxStepsPerCommand, "maxStepsPerCommand must be at least 1.");
            }

            var (a, b, f, t) = Decompose(sPerScrew);
            int ra = RoundToStep(a);
            int rb = RoundToStep(b);
            int rf = RoundToStep(f);

            var moves = new List<TiltAdapterMove>();
            if (includeTilt) {
                moves.AddRange(BuildTiltMoves(ra, rb));
            }
            if (includeBackfocus) {
                var bfMove = BuildBackfocusMove(rf);
                if (bfMove != null) {
                    moves.Add(bfMove);
                }
            }

            var cappedMoves = ApplyCap(moves, maxStepsPerCommand);

            var applied = new double[4];
            foreach (var move in cappedMoves) {
                for (int i = 0; i < 4; ++i) {
                    applied[i] += move.PerCornerSteps[i];
                }
            }

            var residual = new double[4];
            for (int i = 0; i < 4; ++i) {
                residual[i] = (applied[i] - sPerScrew[i]) * unitMicrons;
            }

            double estimatedSeconds = cappedMoves.Count * perMoveSeconds;

            return new TiltAdapterMovePlan(cappedMoves, residual, t, estimatedSeconds);
        }

        private static TiltAdapterMove NewMove(TiltMoveAxis axis, int steps, TiltMoveGroup group) {
            return new TiltAdapterMove(axis, steps, group, DescribeMove(axis, steps));
        }

        private static string DescribeMove(TiltMoveAxis axis, int steps) {
            string signed = steps.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            switch (axis) {
                case TiltMoveAxis.DiagonalA:
                    return $"Diagonal A: {signed} steps";

                case TiltMoveAxis.DiagonalB:
                    return $"Diagonal B: {signed} steps";

                case TiltMoveAxis.EdgeVertical:
                    return $"Edge vertical: {signed} steps";

                case TiltMoveAxis.EdgeHorizontal:
                    return $"Edge horizontal: {signed} steps";

                case TiltMoveAxis.Backfocus:
                    return $"Backfocus: {signed} steps";

                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown tilt move axis.");
            }
        }

        private static void ValidateFourCorners(IReadOnlyList<double> s, string paramName) {
            if (s == null || s.Count != 4) {
                throw new ArgumentException("Per-screw target vector must contain exactly 4 elements (wizard screw indices 1..4).", paramName);
            }
        }
    }

    /// <summary>
    /// Thin <see cref="ITiltMovePlanner"/> adapter over <see cref="TiltMovePlanner"/> for the 4-corner
    /// coupled topology (e.g. the ASG EAT) — the instance callers and the registry (T5) resolve. A
    /// future <c>ThreeCornerIndependentPlanner</c> is a sibling of this class, not a modification to it.
    /// </summary>
    public sealed class FourCornerCoupledPlanner : ITiltMovePlanner {

        public TiltAdapterMovePlan Plan(
            IReadOnlyList<double> sPerScrew,
            bool includeTilt,
            bool includeBackfocus,
            double unitMicrons,
            int maxStepsPerCommand = int.MaxValue,
            double perMoveSeconds = 10) {
            return TiltMovePlanner.Plan(sPerScrew, includeTilt, includeBackfocus, unitMicrons, maxStepsPerCommand, perMoveSeconds);
        }
    }
}
