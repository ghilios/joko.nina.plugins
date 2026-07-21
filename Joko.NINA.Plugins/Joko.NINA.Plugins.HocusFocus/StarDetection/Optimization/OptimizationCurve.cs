#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// Which settings variant the results page is currently showing / would Accept:
    /// <list type="bullet">
    /// <item><see cref="Current"/> — the user's current (seed) settings; shown for comparison, never accepted.</item>
    /// <item><see cref="Optimized"/> — the first optimization pass (preserved across re-optimizes).</item>
    /// <item><see cref="Feedback"/> — the latest re-optimization that used the user's review labels (replaced each round).</item>
    /// </list>
    /// </summary>
    public enum OptimizationVariant {
        Current,
        Optimized,
        Feedback
    }

    /// <summary>
    /// One auto-focus curve to plot on the results page: the pooled scatter points (with error bars), the winning
    /// hyperbolic fit (which carries the smooth curve function + the optimum point), and a short label. Holds ONLY
    /// Mat-free POCOs (<see cref="ScatterErrorPoint"/> structs + an <see cref="AlglibHyperbolicFitting"/> whose
    /// <c>Fitting</c> closure captures only a <c>double[]</c> solution), so it safely survives disposal of the
    /// in-memory run data. The pass-through members let the XAML chart bind without null-walking into <see cref="Fit"/>.
    /// </summary>
    public sealed class OptimizationCurve {
        public string Label { get; set; }

        /// <summary>The FULL pooled scatter set (core ∪ recovery). Remains the set backing <see cref="Bounds"/> and
        /// <see cref="HasFit"/>, so axis extents and fit-presence are unaffected by the core/recovery split. The chart
        /// draws the split subsets (<see cref="CorePoints"/> + <see cref="RecoveryPoints"/>), not this list directly.</summary>
        public IReadOnlyList<ScatterErrorPoint> Points { get; set; }

        /// <summary>The NON-recovery subset of <see cref="Points"/> — the near-focus points the MAIN scatter series draws.
        /// <c>Points == CorePoints ∪ RecoveryPoints</c>. Equal in content to <see cref="Points"/> when recovery is off.</summary>
        public IReadOnlyList<ScatterErrorPoint> CorePoints { get; set; }

        /// <summary>The far-from-focus recovery subset of <see cref="Points"/> — down-weighted in the fit, drawn as the
        /// hollow overlay series. Never null (callers set an empty list when there is no recovery). <c>Points ==
        /// CorePoints ∪ RecoveryPoints</c>.</summary>
        public IReadOnlyList<ScatterErrorPoint> RecoveryPoints { get; set; }

        public AlglibHyperbolicFitting Fit { get; set; }

        /// <summary>Accepted star count per frame for this variant's params (parallel to
        /// <see cref="FrameFocuserPositions"/>), from the representative run's evaluation. Used to show the
        /// per-frame star-count change between the optimized and feedback variants.</summary>
        public IReadOnlyList<int> FrameStarCounts { get; set; }

        /// <summary>Focuser position per frame, parallel to <see cref="FrameStarCounts"/>.</summary>
        public IReadOnlyList<int> FrameFocuserPositions { get; set; }

        /// <summary>The fitted curve function (focuser position → HFR) for the chart's FunctionAnnotation.</summary>
        public Func<double, double> Fitting => Fit?.Fitting;

        /// <summary>The fitted curve minimum (best-focus position + HFR) for the chart's optimum marker.</summary>
        public DataPoint Minimum => Fit?.Minimum ?? default;

        /// <summary>Explicit chart axis bounds that include the best-focus minimum (not just the scatter points), so a
        /// steep curve's optimum is never clipped off the plot. Null vertex when there is no fit, so points-only.</summary>
        public FocusGraphAxisBounds Bounds => FocusGraphAxisBounds.Compute(Points, Fit != null ? Fit.Minimum : (DataPoint?)null);

        public double MinimumStdError => Fit?.MinimumStdError ?? double.NaN;
        public double RSquared => Fit?.RSquared ?? double.NaN;
        public double ReducedChiSquared => Fit?.ReducedChiSquared ?? double.NaN;
        public double LeaveOneOutStdError => Fit?.LeaveOneOutStdError ?? double.NaN;

        /// <summary>True when there is a real fit and at least one scatter point to plot.</summary>
        public bool HasFit => Fit != null && Points != null && Points.Count > 0;

        /// <summary>True when there is at least one far-from-focus recovery point to draw as the hollow overlay (and to
        /// show the "recovery point" caption). False when recovery is off or every recovery position was excluded upstream.</summary>
        public bool HasRecoveryPoints => RecoveryPoints != null && RecoveryPoints.Count > 0;
    }

    /// <summary>
    /// One frame's accepted-star-count change between a baseline variant (optimized, or current when there was no
    /// optimization pass) and the feedback-optimized variant, at a given focuser position. Shown as a compact
    /// horizontal list when the user is comparing the feedback variant.
    /// </summary>
    public sealed class FrameStarCountChange {
        public int FocuserPosition { get; set; }
        public int BaselineCount { get; set; }
        public int FeedbackCount { get; set; }

        /// <summary>Feedback minus baseline (positive = more stars accepted with feedback).</summary>
        public int Delta => FeedbackCount - BaselineCount;

        /// <summary>"{baseline}→{feedback}" accepted-star counts.</summary>
        public string CountsText => $"{BaselineCount}→{FeedbackCount}";

        /// <summary>Signed delta, e.g. "+3" / "-2" / "0".</summary>
        public string DeltaText => Delta > 0 ? $"+{Delta}" : Delta.ToString();
    }

    /// <summary>
    /// One frame's accepted-star count and how it moved across the optimization passes, at a given focuser
    /// position. <see cref="Stages"/> is the summed accepted-star count per stage: [current, round1, round2, …]
    /// for the Optimized variant (after "Continue optimizing"), or a single [current] entry for the Current
    /// variant. Shown as a compact wrapping list of per-position cells on the summary, mirroring
    /// <see cref="FrameStarCountChange"/> but carrying the full per-round path like <c>ChangedParameterRow.Stages</c>.
    /// </summary>
    public sealed class FrameStarCountTrajectory {
        public int FocuserPosition { get; set; }
        public IReadOnlyList<int> Stages { get; set; }

        /// <summary>Arrow-joined counts across the stages, e.g. "52→61→78" (or just "52" for a single stage).</summary>
        public string CountsText => Stages == null ? string.Empty : string.Join("→", Stages);

        /// <summary>Net change from the first stage (current) to the last (latest optimization). 0 for a single stage.</summary>
        public int Delta => Stages != null && Stages.Count > 1 ? Stages[Stages.Count - 1] - Stages[0] : 0;

        /// <summary>True when there is more than one stage (i.e. an optimization change to show a delta for).</summary>
        public bool HasDelta => Stages != null && Stages.Count > 1;

        /// <summary>Signed net delta ("+26" / "-4"); empty when there is no change to show (single stage).</summary>
        public string DeltaText => !HasDelta ? string.Empty : (Delta > 0 ? $"+{Delta}" : Delta.ToString());
    }

    /// <summary>
    /// Determinate progress for the wizard's long-running per-item phases (loading frames from disk, building the
    /// per-frame early-detection contexts during the seed-guard, and detecting frames for review). Both fields are
    /// 1-based running counts: <see cref="Current"/> items done out of <see cref="Total"/>.
    /// </summary>
    public readonly struct RunLoadProgress {
        public int Current { get; }
        public int Total { get; }

        public RunLoadProgress(int current, int total) {
            Current = current;
            Total = total;
        }
    }
}
