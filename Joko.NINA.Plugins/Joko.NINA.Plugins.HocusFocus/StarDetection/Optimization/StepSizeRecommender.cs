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
        /// three times the minimum HFR (averaged over the two sides). <see cref="double.NaN"/> for a degenerate fit.
        /// </summary>
        public double HalfWidth { get; set; }

        /// <summary>
        /// True when <see cref="HalfWidth"/> landed outside what this sweep actually sampled and was capped (see
        /// <see cref="StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple"/>). The recommendation is then a
        /// deliberate partial step toward the answer rather than the answer: re-run with it and the next sweep,
        /// being deeper, produces a better-grounded one.
        /// </summary>
        public bool WasCapped { get; set; }

        /// <summary>
        /// The DETECTABILITY half-width actually used to bound <see cref="HalfWidth"/> — <see cref="MaxUsefulHalfSpan"/>
        /// after the "one run may not shrink the sweep by more than half" floor
        /// (<see cref="StepSizeRecommender.MinHalfWidthSampledHalfSpanMultiple"/>). <see cref="double.NaN"/> when no
        /// detectability measurement was supplied or too few frames qualified to make one.
        /// </summary>
        public double DetectHalfWidth { get; set; } = double.NaN;

        /// <summary>
        /// The outermost offset from focus at which this sweep still detected the hard floor of stars — i.e. the
        /// furthest point that MEASURED anything. Reported UNFLOORED, so it is a statement about the sweep rather
        /// than about the recommendation: a point beyond this buys no measurement at any step size, which is what
        /// makes it the right bound for a far-from-focus recovery step. <see cref="double.NaN"/> when unmeasurable.
        ///
        /// <para>Reporting only. Nothing in this class or in the AF engine clamps a recovery step to it today; see
        /// the wave-7 design's §2.2 for why that is engine-side work with its own followup.</para>
        /// </summary>
        public double MaxUsefulHalfSpan { get; set; } = double.NaN;

        /// <summary>
        /// True when detectability — not curve geometry — is what set <see cref="HalfWidth"/>: the sweep stopped
        /// yielding stars before the fitted 3x band ended. This is F18's condition, and a consumer can say
        /// "narrowed to what your rig can still see" rather than presenting a smaller number with no reason.
        /// </summary>
        public bool WasDetectBounded { get; set; }
    }

    /// <summary>
    /// What a sweep actually DETECTED, per frame — the measured half of F18's bound. Supplied by a caller that has
    /// a <c>RunEvaluationMetrics</c> in hand; omitted (null) leaves <see cref="StepSizeRecommender.Recommend"/>
    /// byte-identical to its pre-F18 behaviour.
    /// </summary>
    public sealed class SweepDetectability {

        /// <summary>Accepted star count per frame — <c>RunEvaluationMetrics.FrameStarCounts</c>.</summary>
        public IReadOnlyList<int> FrameStarCounts { get; set; }

        /// <summary>Focuser position per frame, PARALLEL to <see cref="FrameStarCounts"/>.</summary>
        public IReadOnlyList<int> FrameFocuserPositions { get; set; }

        /// <summary>
        /// Recovery flag per frame, PARALLEL to <see cref="FrameStarCounts"/>. Recovery frames are DELIBERATELY far
        /// from focus and are exempt from the objective's own hard floor, so including them would let the rescue
        /// wing vote on how wide the ordinary sweep should be. Null ⇒ no frame is a recovery frame.
        /// </summary>
        public IReadOnlyList<bool> FrameIsRecovery { get; set; }

        /// <summary>
        /// The per-frame star count the objective actually requires — <c>ObjectiveConstants.NHard</c>. A frame below
        /// it is not a usable measurement, which is exactly the sense in which the sweep has run out of detectable
        /// range. Read from the caller's constants rather than hard-coded, for the same reason
        /// <c>ExposureRecommender</c> reads <c>NTarget</c> from them.
        /// </summary>
        public int HardFloorStarCount { get; set; } = 3;

        /// <summary>
        /// Focus-recovery steps per side the executed sweep adds beyond the offset steps. Used ONLY when the caller
        /// asks for the step to be sized for the sweep actually run; see
        /// <see cref="StepSizeRecommender.Recommend"/>'s <c>sizeForExecutedSweep</c>.
        /// </summary>
        public int RecoveryStepsPerSide { get; set; }
    }

    /// <summary>
    /// Recommends an auto-focus step size from a winning hyperbolic fit. The idea: size the step so a focus sweep
    /// lands ~3-4 measurement points on each side of focus inside the "focus-sensitive" region — the band where
    /// HFR climbs from its minimum to roughly three times the minimum, which is where the curve carries the most
    /// slope (and thus the most information). The half-width W of that band is read directly off the fitted model,
    /// and the step is W / 3.5 (so the band holds ~3-4 points per side).
    /// </summary>
    public static class StepSizeRecommender {
        // The focus-sensitive band runs from the minimum HFR up to this multiple of it; its half-width sets the step.
        private const double HfrThresholdMultiple = 3.0;

        // Targeted points per side within the focus-sensitive band [x0, x0 + W]: W / PointsPerSide steps.
        private const double PointsPerSide = 3.5;

        // Default offset steps per side of focus for the recommended sweep.
        private const int DefaultOffsetSteps = 4;

        // Upper bound on the outward search for the half-width, as a multiple of the fit's sampled X span — a
        // guard so a near-flat / degenerate fit cannot loop unboundedly.
        private const double MaxSearchSpanMultiple = 4.0;

        /// <summary>
        /// How far past the sampled sweep the recommended half-width may reach, as a multiple of the sampled
        /// HALF-span.
        ///
        /// <para>The half-width is read off the FITTED model, so on a sweep too shallow to contain the 3x band it
        /// comes from extrapolating the hyperbola well beyond any measured point — and the shallower the sweep,
        /// the further out it goes. Measured on synthetic curves with the exact AF physics, from a +/-2096-step
        /// sweep: ends at 1.2x the minimum HFR put the half-width 4.3x the sampled half-span out (step 524 ->
        /// 2554, a +/-10216 sweep); 1.36x put it 3.1x out; only a sweep that already reaches ~3x lands inside the
        /// data. Recommending a sweep several times wider than anything measured is a claim the data cannot
        /// support, and it can exceed the focuser's travel.</para>
        ///
        /// <para>1.5 caps the per-run correction without changing where it converges: each run widens the sweep,
        /// the next half-width is better grounded, and a shallow rig reaches the same answer in about three runs
        /// instead of one unverifiable jump. The cap binds whenever the sweep's ends reach less than roughly
        /// 2.1x the minimum HFR, and <see cref="StepSizeRecommendation.WasCapped"/> says so.</para>
        /// </summary>
        public const double MaxHalfWidthSampledHalfSpanMultiple = 1.5;

        /// <summary>
        /// How far the DETECTABILITY bound may narrow the half-width in one run, as a multiple of the sampled
        /// HALF-span — the mirror of <see cref="MaxHalfWidthSampledHalfSpanMultiple"/> on the other side.
        ///
        /// <para>The recommender has always bounded how far one run may WIDEN the sweep, on the reasoning that an
        /// extrapolated half-width is a claim the data cannot support. It had no bound on how far one run may
        /// NARROW it, and that asymmetry is what lets a run whose only star-bearing frames are the innermost ones
        /// collapse the sweep in a single step. At 0.5 the worst one-run shrink is
        /// <c>2·step/PointsPerSide = 0.57×</c>, so a pathological run halves the step and converges over runs
        /// instead — the same converge-over-runs philosophy, applied symmetrically.</para>
        /// </summary>
        public const double MinHalfWidthSampledHalfSpanMultiple = 0.5;

        /// <summary>
        /// How many non-recovery frames must clear the hard floor before a detectability half-width is computed at
        /// all. Below this the answer is "unmeasurable" (NaN ⇒ no bound), never "nothing is detectable" — a thin
        /// run tells you the first and not the second, and treating it as the second is exactly how a bound meant
        /// to tighten an over-reach would instead collapse a sweep. Matches
        /// <c>ExposureRecommender.MinFramesForRecommendation</c>'s "too thin to trust" bar, and is a separate
        /// constant for the same reason that one is: the two answer different questions and only coincide by choice.
        /// </summary>
        public const int MinFramesForDetectHalfWidth = 3;

        /// <summary>
        /// Recommends a step size (and offset steps) from <paramref name="bestFit"/>. Returns the
        /// <paramref name="currentStepSize"/> unchanged (with <see cref="StepSizeRecommendation.HalfWidth"/> NaN)
        /// for a degenerate fit (null fit, non-finite minimum, or non-positive minimum HFR). When supplied,
        /// <paramref name="focuserMaxStep"/> clamps the recommendation; the result is never below 1.
        ///
        /// <para><b>F18 — the detectability bound.</b> When <paramref name="detectability"/> is supplied, the
        /// half-width becomes <c>min(W_3x, max(W_detect, floor))</c>, where <c>W_detect</c> is the outermost offset
        /// at which the sweep still detected <c>NHard</c> stars. The 3x band is pure curve GEOMETRY and asks
        /// nothing about whether stars are still visible out there; on a 3800 mm rig it put the half-width 15%
        /// past the last position that measured anything, and the sweep then spent exposures on frames with zero
        /// stars. <c>W_detect</c> is MEASURED rather than extrapolated, so it can only ever report something this
        /// sweep actually sampled — which is what makes it safe to trust more than the fitted band. It only ever
        /// TIGHTENS: a detectable range wider than the 3x band changes nothing.</para>
        ///
        /// <para><paramref name="sizeForExecutedSweep"/> additionally sizes the step for the points the run will
        /// ACTUALLY visit (offset + focus-recovery steps per side) rather than the offset steps alone, holding the
        /// outermost executed point at the same multiple of the half-width today's outermost offset point reaches.
        /// It is separately switchable because it costs lever arm on every successful run to bound a path that only
        /// executes when a run has already failed — a trade the wave-7 design settles by measurement, not argument.</para>
        /// </summary>
        public static StepSizeRecommendation Recommend(AlglibHyperbolicFitting bestFit, int currentStepSize, int? focuserMaxStep = null,
                                                       SweepDetectability detectability = null, bool sizeForExecutedSweep = false) {
            if (bestFit == null || bestFit.Fitting == null) {
                return Degenerate(currentStepSize, focuserMaxStep);
            }

            var x0 = bestFit.Minimum.X;
            var minHfr = bestFit.Minimum.Y;
            if (double.IsNaN(x0) || double.IsInfinity(x0) || !(minHfr > 0.0) || double.IsNaN(minHfr) || double.IsInfinity(minHfr)) {
                return Degenerate(currentStepSize, focuserMaxStep);
            }

            var fitting = bestFit.Fitting;
            var target = HfrThresholdMultiple * minHfr;

            // Search outward from x0 in both directions for the offset where HFR == HfrThresholdMultiple*minHfr.
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

            // Only trust the model a bounded distance past the data it was fitted to.
            var wasCapped = false;
            var maxHalfWidth = MaxHalfWidthSampledHalfSpanMultiple * 0.5 * searchSpan;
            if (maxHalfWidth > 0.0 && halfWidth > maxHalfWidth) {
                halfWidth = maxHalfWidth;
                wasCapped = true;
            }

            // F18: bound the geometric band by what the sweep could still SEE. Only ever tightens.
            var maxUsefulHalfSpan = MeasureMaxUsefulHalfSpan(detectability, x0);
            var detectHalfWidth = double.NaN;
            var wasDetectBounded = false;
            if (double.IsFinite(maxUsefulHalfSpan)) {
                var minHalfWidth = MinHalfWidthSampledHalfSpanMultiple * 0.5 * searchSpan;
                detectHalfWidth = Math.Max(maxUsefulHalfSpan, minHalfWidth);
                if (detectHalfWidth < halfWidth) {
                    halfWidth = detectHalfWidth;
                    wasDetectBounded = true;
                }
            }

            var step = (int)Math.Round(halfWidth / ResolvePointsPerSide(detectability, sizeForExecutedSweep), MidpointRounding.AwayFromZero);
            step = ClampStep(step, focuserMaxStep);

            return new StepSizeRecommendation {
                StepSize = step,
                OffsetSteps = DefaultOffsetSteps,
                HalfWidth = halfWidth,
                WasCapped = wasCapped,
                DetectHalfWidth = detectHalfWidth,
                MaxUsefulHalfSpan = maxUsefulHalfSpan,
                WasDetectBounded = wasDetectBounded
            };
        }

        /// <summary>
        /// The outermost offset from <paramref name="x0"/> at which a NON-RECOVERY frame still detected the hard
        /// floor of stars, or NaN when that is not measurable from <paramref name="detectability"/>.
        ///
        /// <para>NaN is returned — meaning "no bound" — rather than 0 whenever the measurement cannot be made:
        /// absent or mismatched inputs, or fewer than <see cref="MinFramesForDetectHalfWidth"/> qualifying frames.
        /// The distinction matters: a starless run has not shown that nothing is detectable, only that this sweep
        /// did not detect it, and a bound built on the second reading would collapse the very sweep that needs to
        /// stay wide enough to find the curve again.</para>
        /// </summary>
        private static double MeasureMaxUsefulHalfSpan(SweepDetectability detectability, double x0) {
            var counts = detectability?.FrameStarCounts;
            var positions = detectability?.FrameFocuserPositions;
            if (counts == null || positions == null || counts.Count == 0 || positions.Count != counts.Count) {
                return double.NaN;
            }
            var isRecovery = detectability.FrameIsRecovery;
            var hardFloor = detectability.HardFloorStarCount;
            var qualifying = 0;
            var furthest = 0.0;
            for (var i = 0; i < counts.Count; i++) {
                if (isRecovery != null && i < isRecovery.Count && isRecovery[i]) {
                    continue; // deliberately far from focus, and exempt from the objective's own floor
                }
                if (counts[i] < hardFloor) {
                    continue;
                }
                qualifying++;
                var offset = Math.Abs(positions[i] - x0);
                if (offset > furthest) {
                    furthest = offset;
                }
            }
            if (qualifying < MinFramesForDetectHalfWidth || !(furthest > 0.0)) {
                return double.NaN;
            }
            return furthest;
        }

        /// <summary>
        /// The divisor that turns a half-width into a step. <see cref="PointsPerSide"/> by default — the band holds
        /// 3-4 offset points per side, and the outermost of the <see cref="DefaultOffsetSteps"/> offset points lands
        /// at <c>DefaultOffsetSteps / PointsPerSide</c> ≈ 1.14x the half-width.
        ///
        /// <para>When the caller asks for the EXECUTED sweep to be sized, the recovery steps join the count and the
        /// divisor is scaled so the outermost point the run will actually visit lands at that SAME 1.14x multiple,
        /// instead of at <c>(offset + recovery) / PointsPerSide</c> — which at 4 offset + 1 recovery is 1.43x, the
        /// 43% over-reach F18 measured. With no recovery steps the two are identical by construction.</para>
        /// </summary>
        private static double ResolvePointsPerSide(SweepDetectability detectability, bool sizeForExecutedSweep) {
            var recoverySteps = detectability?.RecoveryStepsPerSide ?? 0;
            if (!sizeForExecutedSweep || recoverySteps <= 0) {
                return PointsPerSide;
            }
            return (DefaultOffsetSteps + recoverySteps) * PointsPerSide / DefaultOffsetSteps;
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
            return double.NaN; // never reached 3*min within the budget (near-flat / degenerate)
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
