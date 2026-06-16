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
        public IReadOnlyList<ScatterErrorPoint> Points { get; set; }
        public AlglibHyperbolicFitting Fit { get; set; }

        /// <summary>The fitted curve function (focuser position → HFR) for the chart's FunctionAnnotation.</summary>
        public Func<double, double> Fitting => Fit?.Fitting;

        /// <summary>The fitted curve minimum (best-focus position + HFR) for the chart's optimum marker.</summary>
        public DataPoint Minimum => Fit?.Minimum ?? default;

        public double MinimumStdError => Fit?.MinimumStdError ?? double.NaN;
        public double RSquared => Fit?.RSquared ?? double.NaN;
        public double ReducedChiSquared => Fit?.ReducedChiSquared ?? double.NaN;
        public double LeaveOneOutStdError => Fit?.LeaveOneOutStdError ?? double.NaN;

        /// <summary>True when there is a real fit and at least one scatter point to plot.</summary>
        public bool HasFit => Fit != null && Points != null && Points.Count > 0;
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
