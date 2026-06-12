#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// Regularizes per-point σ (ErrorY) before any weighted curve fit, bounding the influence a single
    /// point can gain from a degenerate σ. Per-point σ is the star-ensemble scatter — a relative
    /// precision proxy — and a near-zero value (one detected star, or a few stars with near-identical
    /// HFR → MAD ≈ 0) is an artifact of the estimator, not real precision: unregularized it became a
    /// ~1000× weight that pinned the fit (analysis finding F5a), and Huber IRLS could not recover
    /// because LM minimizes the pinned point's residual by construction. NINA core's Trendline and
    /// QuadraticFitting weight by 1/ErrorY² and cannot be modified, so every fitter must receive these
    /// regularized copies; raw measured σ continues to flow to reports and charts.
    /// </summary>
    public static class WeightRegularization {

        /// <summary>
        /// Floor on σ as a fraction of the median positive σ of the points entering the fit. One point
        /// can exceed the median weight by at most 1/0.2 = 5× (25× in squared influence), preserving
        /// legitimate near-focus vs wing weight ratios (~3× in healthy sweeps, weights 2-20) while
        /// removing the degenerate 1000× path.
        /// </summary>
        public const double MinErrorYFractionOfMedian = 0.2;

        /// <summary>
        /// Returns copies of <paramref name="points"/> with ErrorY regularized: non-finite or ≤ 0 σ
        /// (unknown precision) becomes the median positive σ — average weight, not maximum — and
        /// positive σ is floored at <see cref="MinErrorYFractionOfMedian"/>·median. When no point has
        /// a positive finite σ, all σ become 1.0 (the fit degenerates to unweighted). X, Y, and ErrorX
        /// pass through unchanged.
        /// </summary>
        private static bool IsUsableSigma(double sigma) => double.IsFinite(sigma) && sigma > 0.0;

        public static List<ScatterErrorPoint> Regularize(IReadOnlyList<ScatterErrorPoint> points) {
            if (points == null || points.Count == 0) {
                return new List<ScatterErrorPoint>();
            }

            var positiveSigmas = points
                .Select(p => p.ErrorY)
                .Where(IsUsableSigma)
                .ToList();
            if (positiveSigmas.Count == 0) {
                return points.Select(p => new ScatterErrorPoint(p.X, p.Y, p.ErrorX, 1.0)).ToList();
            }

            var (median, _) = positiveSigmas.MedianMAD();
            var floor = MinErrorYFractionOfMedian * median;
            return points.Select(p => {
                var sigma = p.ErrorY;
                var regularized = IsUsableSigma(sigma)
                    ? Math.Max(sigma, floor)
                    : median;
                return new ScatterErrorPoint(p.X, p.Y, p.ErrorX, regularized);
            }).ToList();
        }
    }
}
