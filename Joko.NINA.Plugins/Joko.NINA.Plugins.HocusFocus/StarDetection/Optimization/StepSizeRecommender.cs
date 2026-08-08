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

        /// <summary>
        /// The HFR dynamic range this sweep actually MEASURED: max(HFR) / min(HFR) over the fitted points.
        /// <see cref="double.NaN"/> when it cannot be measured (no outputs, or a non-positive minimum).
        ///
        /// <para><b>F49(c) — why this number and not the half-width.</b> The step is sized from the offset at
        /// which HFR reaches <see cref="StepSizeRecommender.HfrThresholdMultiple"/>x its minimum. When a sweep
        /// never gets there, that offset is read off the fitted model well outside the data, and
        /// <see cref="WasCapped"/> is the recommender declining to trust it. This quantity says the same thing
        /// DIRECTLY and from measurement alone: a sweep whose ends reach 1.9x the minimum did not sample the
        /// band, full stop, and no extrapolation is involved in saying so. The field session behind F49/F51 ran
        /// 1.9 -> 3.5 px, i.e. 1.8x, and was told to widen on all four runs.</para>
        /// </summary>
        public double SampledHfrRange { get; set; } = double.NaN;

        /// <summary>
        /// True when the sweep measured HFR out to <see cref="StepSizeRecommender.HfrThresholdMultiple"/>x its
        /// minimum, i.e. the band the step is sized from was actually sampled rather than extrapolated to.
        /// False when <see cref="SampledHfrRange"/> is NaN: an unmeasurable range is not a reached band.
        /// </summary>
        public bool ReachedHfrBand => SampledHfrRange >= StepSizeRecommender.HfrThresholdMultiple;

        /// <summary>
        /// The EXACT factor by which the next capped run will multiply this recommendation, or
        /// <see cref="double.NaN"/> when <see cref="WasCapped"/> is false or it cannot be derived.
        ///
        /// <para><b>It is a property of the ALGORITHM, not of the fit.</b> While the cap binds,
        /// <c>halfWidth = 1.5 · ½ · span</c> and <c>span = 2·P·step</c> for P points per side, so
        /// <c>step' = (1.5·P / PointsPerSide) · step</c> — 1.714x at 4 offset steps, 2.143x at 4 offset + 1
        /// recovery. Nothing in it depends on the extrapolated half-width the cap exists to distrust, which is
        /// precisely why this is quotable to a user and a projected run count is not.</para>
        ///
        /// <para><b>Measured, not only derived.</b> Wave 7's F18 control arm has 22 capped rounds across six
        /// datasets on disk, and every one of them lands at 1.667–1.750 (the scatter is integer rounding of the
        /// step). The field session behind F49/F51 ran 100 → 214 → 459 at P = 5: 100 × 2.143 = 214.3, and
        /// 214 × 2.143 = 458.6.</para>
        /// </summary>
        public double CappedGrowthRatio { get; set; } = double.NaN;
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
        /// <summary>
        /// The focus-sensitive band runs from the minimum HFR up to this multiple of it; its half-width sets the
        /// step. PUBLIC because it is the number a capped recommendation is converging TOWARD, and F49(c) asks
        /// for that to be said out loud rather than left as an internal constant.
        /// </summary>
        public const double HfrThresholdMultiple = 3.0;

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

            var pointsPerSide = ResolvePointsPerSide(detectability, sizeForExecutedSweep);
            var step = (int)Math.Round(halfWidth / pointsPerSide, MidpointRounding.AwayFromZero);
            step = ClampStep(step, focuserMaxStep);

            return new StepSizeRecommendation {
                StepSize = step,
                OffsetSteps = DefaultOffsetSteps,
                HalfWidth = halfWidth,
                WasCapped = wasCapped,
                DetectHalfWidth = detectHalfWidth,
                MaxUsefulHalfSpan = maxUsefulHalfSpan,
                WasDetectBounded = wasDetectBounded,
                SampledHfrRange = MeasureSampledHfrRange(bestFit),
                CappedGrowthRatio = wasCapped ? CappedGrowthRatioOf(searchSpan, currentStepSize, pointsPerSide) : double.NaN
            };
        }

        /// <summary>
        /// max(HFR) / min(HFR) over the points the fit was given, or NaN when that cannot be formed.
        /// See <see cref="StepSizeRecommendation.SampledHfrRange"/> for why this is the honest companion to
        /// <see cref="StepSizeRecommendation.WasCapped"/>.
        /// </summary>
        private static double MeasureSampledHfrRange(AlglibHyperbolicFitting bestFit) {
            var outputs = bestFit.Outputs;
            if (outputs == null || outputs.Length == 0) {
                return double.NaN;
            }
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            foreach (var y in outputs) {
                if (double.IsNaN(y) || double.IsInfinity(y) || y <= 0.0) {
                    return double.NaN; // a non-positive or non-finite HFR makes the ratio meaningless, not zero
                }
                if (y < lo) lo = y;
                if (y > hi) hi = y;
            }
            return lo > 0.0 ? hi / lo : double.NaN;
        }

        /// <summary>
        /// The exact multiplier the NEXT capped run will apply — <c>MaxHalfWidthSampledHalfSpanMultiple · P /
        /// pointsPerSide</c>, with P (points per side actually executed) read back off the sweep as
        /// <c>½·span / currentStepSize</c> rather than assumed.
        ///
        /// <para>Reading P back off the sweep is what makes this correct for the run in hand instead of for a
        /// default configuration: the field session behind F49/F51 executed 4 offset + 1 recovery step per side,
        /// so its P was 5 and its ratio 2.143 — not the 1.714 a 4-offset sweep gives. Both appear in the
        /// evidence, and a single hard-coded constant would have been wrong on one of them.</para>
        /// </summary>
        private static double CappedGrowthRatioOf(double searchSpan, int currentStepSize, double pointsPerSide) {
            if (!(searchSpan > 0.0) || currentStepSize < 1 || !(pointsPerSide > 0.0)) {
                return double.NaN;
            }
            var executedPointsPerSide = 0.5 * searchSpan / currentStepSize;
            if (!(executedPointsPerSide > 0.0)) {
                return double.NaN;
            }
            return MaxHalfWidthSampledHalfSpanMultiple * executedPointsPerSide / pointsPerSide;
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
        ///
        /// <para><b>And NaN when EVERY sampled frame cleared the floor</b>, which is the same principle at the
        /// other end and is easy to get wrong. This quantity is bounded above by the sampled half-span by
        /// construction — it is the outermost SAMPLED position, so it can never report a distance the sweep did not
        /// visit. If no frame failed, the sweep never observed detectability ending, and returning the sweep's own
        /// edge would turn <c>min(W_3x, W_detect)</c> into "never recommend a sweep wider than the one you just
        /// took" on every healthy run — a cap on widening, which is a completely different rule from F18's, and one
        /// the recommender already has in <see cref="MaxHalfWidthSampledHalfSpanMultiple"/>. The bound exists to
        /// report an OBSERVED limit; an unobserved one is absent.</para>
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
            var starved = 0;
            var furthest = 0.0;
            for (var i = 0; i < counts.Count; i++) {
                if (isRecovery != null && i < isRecovery.Count && isRecovery[i]) {
                    continue; // deliberately far from focus, and exempt from the objective's own floor
                }
                if (counts[i] < hardFloor) {
                    starved++;
                    continue;
                }
                qualifying++;
                var offset = Math.Abs(positions[i] - x0);
                if (offset > furthest) {
                    furthest = offset;
                }
            }
            if (starved == 0 || qualifying < MinFramesForDetectHalfWidth || !(furthest > 0.0)) {
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
