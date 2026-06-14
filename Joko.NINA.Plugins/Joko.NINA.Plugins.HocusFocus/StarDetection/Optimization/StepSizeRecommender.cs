#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// An auto-focus step-size recommendation derived from a fitted focus curve.
    /// </summary>
    public sealed class StepSizeRecommendation {
        /// <summary>Recommended AF step size (focuser steps), always &gt;= 1.</summary>
        public int StepSize { get; set; }

        /// <summary>Recommended number of offset steps per side of focus (points per side).</summary>
        public int OffsetSteps { get; set; }

        /// <summary>
        /// The modeled focus-sensitive half-width: the offset from best focus at which the fitted HFR reaches
        /// twice the minimum HFR (averaged over the two sides). <see cref="double.NaN"/> for a degenerate fit.
        /// </summary>
        public double HalfWidth { get; set; }
    }

    /// <summary>
    /// Recommends an auto-focus step size from a winning hyperbolic fit. The idea: size the step so a focus sweep
    /// lands ~3-4 measurement points on each side of focus inside the "focus-sensitive" region — the band where
    /// HFR climbs from its minimum to roughly twice the minimum, which is where the curve carries the most slope
    /// (and thus the most information). The half-width W of that band is read directly off the fitted model, and
    /// the step is W / 3.5 (so the band holds ~3-4 points per side).
    /// </summary>
    public static class StepSizeRecommender {
        // Targeted points per side within the focus-sensitive band [x0, x0 + W]: W / PointsPerSide steps.
        private const double PointsPerSide = 3.5;

        // Default offset steps per side of focus for the recommended sweep.
        private const int DefaultOffsetSteps = 4;

        // Upper bound on the outward search for the half-width, as a multiple of the fit's sampled X span — a
        // guard so a near-flat / degenerate fit cannot loop unboundedly.
        private const double MaxSearchSpanMultiple = 4.0;

        /// <summary>
        /// Recommends a step size (and offset steps) from <paramref name="bestFit"/>. Returns the
        /// <paramref name="currentStepSize"/> unchanged (with <see cref="StepSizeRecommendation.HalfWidth"/> NaN)
        /// for a degenerate fit (null fit, non-finite minimum, or non-positive minimum HFR). When supplied,
        /// <paramref name="focuserMaxStep"/> clamps the recommendation; the result is never below 1.
        /// </summary>
        public static StepSizeRecommendation Recommend(AlglibHyperbolicFitting bestFit, int currentStepSize, int? focuserMaxStep = null) {
            if (bestFit == null || bestFit.Fitting == null) {
                return Degenerate(currentStepSize, focuserMaxStep);
            }

            var x0 = bestFit.Minimum.X;
            var minHfr = bestFit.Minimum.Y;
            if (double.IsNaN(x0) || double.IsInfinity(x0) || !(minHfr > 0.0) || double.IsNaN(minHfr) || double.IsInfinity(minHfr)) {
                return Degenerate(currentStepSize, focuserMaxStep);
            }

            var fitting = bestFit.Fitting;
            var target = 2.0 * minHfr;

            // Search outward from x0 in both directions for the offset where HFR == 2*minHfr.
            var searchSpan = SearchSpan(bestFit);
            var rightW = FindHalfWidth(fitting, x0, +1.0, target, searchSpan);
            var leftW = FindHalfWidth(fitting, x0, -1.0, target, searchSpan);

            double halfWidth;
            if (double.IsNaN(rightW) && double.IsNaN(leftW)) {
                return Degenerate(currentStepSize, focuserMaxStep);
            } else if (double.IsNaN(rightW)) {
                halfWidth = leftW;
            } else if (double.IsNaN(leftW)) {
                halfWidth = rightW;
            } else {
                halfWidth = 0.5 * (rightW + leftW); // average the two sides (handles asymmetric models)
            }

            var step = (int)Math.Round(halfWidth / PointsPerSide, MidpointRounding.AwayFromZero);
            step = ClampStep(step, focuserMaxStep);

            return new StepSizeRecommendation {
                StepSize = step,
                OffsetSteps = DefaultOffsetSteps,
                HalfWidth = halfWidth
            };
        }

        private static StepSizeRecommendation Degenerate(int currentStepSize, int? focuserMaxStep) {
            return new StepSizeRecommendation {
                StepSize = ClampStep(currentStepSize, focuserMaxStep),
                OffsetSteps = DefaultOffsetSteps,
                HalfWidth = double.NaN
            };
        }

        private static int ClampStep(int step, int? focuserMaxStep) {
            if (step < 1) {
                step = 1;
            }
            if (focuserMaxStep.HasValue && focuserMaxStep.Value >= 1 && step > focuserMaxStep.Value) {
                step = focuserMaxStep.Value;
            }
            return step;
        }

        /// <summary>The sampled-X span of the fit, used to bound the outward half-width search.</summary>
        private static double SearchSpan(AlglibHyperbolicFitting bestFit) {
            var span = double.NaN;
            var inputs = bestFit.Inputs;
            if (inputs != null && inputs.Length > 0) {
                double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
                foreach (var row in inputs) {
                    var x = row[0];
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
                span = maxX - minX;
            }
            if (!(span > 0.0)) {
                // Fall back to a generous absolute span keyed off the step size so the search still terminates.
                span = Math.Max(1.0, Math.Abs(bestFit.StepSize)) * 100.0;
            }
            return span;
        }

        /// <summary>
        /// Walks outward from <paramref name="x0"/> in <paramref name="direction"/> until the modeled HFR reaches
        /// <paramref name="target"/>, then refines by bisection. Returns the offset (a positive magnitude) or NaN
        /// if the target is not reached within <see cref="MaxSearchSpanMultiple"/> × <paramref name="searchSpan"/>.
        /// </summary>
        private static double FindHalfWidth(Func<double, double> fitting, double x0, double direction, double target, double searchSpan) {
            var maxOffset = MaxSearchSpanMultiple * searchSpan;
            // Coarse step: 1/256 of the search budget — fine enough to bracket, cheap enough to terminate.
            var coarse = Math.Max(maxOffset / 256.0, 1e-6);

            double prevOffset = 0.0;
            for (var offset = coarse; offset <= maxOffset; offset += coarse) {
                var value = fitting(x0 + direction * offset);
                if (double.IsNaN(value) || double.IsInfinity(value)) {
                    return double.NaN;
                }
                if (value >= target) {
                    // Bracketed between prevOffset (below target) and offset (>= target): bisection refine.
                    return Bisect(fitting, x0, direction, target, prevOffset, offset);
                }
                prevOffset = offset;
            }
            return double.NaN; // never reached 2*min within the budget (near-flat / degenerate)
        }

        private static double Bisect(Func<double, double> fitting, double x0, double direction, double target, double lo, double hi) {
            for (var i = 0; i < 60; i++) {
                var mid = 0.5 * (lo + hi);
                var value = fitting(x0 + direction * mid);
                if (value >= target) {
                    hi = mid;
                } else {
                    lo = mid;
                }
                if (hi - lo < 1e-6) {
                    break;
                }
            }
            return 0.5 * (lo + hi);
        }
    }
}
