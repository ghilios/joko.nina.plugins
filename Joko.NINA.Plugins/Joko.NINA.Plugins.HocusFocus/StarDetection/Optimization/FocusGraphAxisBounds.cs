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
    /// Explicit axis bounds for a focus-graph chart (HFR vs. focuser position) that include the fitted curve's
    /// best-focus minimum, not just the scatter points.
    ///
    /// <para>OxyPlot auto-ranges an axis to its <c>Series</c> only; a <c>FunctionAnnotation</c> (the fitted curve)
    /// and a <c>PointAnnotation</c> (the best-focus diamond) do NOT participate. So a steep curve whose minimum
    /// sits outside the sampled point span — e.g. below the lowest sampled HFR — gets clipped off the plot. This
    /// unions the scatter extent with the (real) fit minimum and pads each axis by <paramref name="pad"/> of its
    /// range, reproducing the templates' former <c>MinimumPadding</c>/<c>MaximumPadding</c> = 0.1 while keeping the
    /// optimum visible. Pure and side-effect free so it is unit-tested directly.</para>
    /// </summary>
    public readonly struct FocusGraphAxisBounds {
        public double XMin { get; }
        public double XMax { get; }
        public double YMin { get; }
        public double YMax { get; }

        public FocusGraphAxisBounds(double xMin, double xMax, double yMin, double yMax) {
            XMin = xMin;
            XMax = xMax;
            YMin = yMin;
            YMax = yMax;
        }

        /// <summary>
        /// Compute padded axis bounds over the scatter <paramref name="points"/> unioned with the fit
        /// <paramref name="minimum"/> (pass <c>null</c> when there is no fit, so the phantom <c>default(DataPoint)</c>
        /// at (0,0) is not folded in). Guards: non-finite point/vertex coordinates are skipped; a zero-width range is
        /// expanded around the value; with nothing finite to show, a benign unit range is returned so the axis still
        /// renders.
        /// </summary>
        public static FocusGraphAxisBounds Compute(IReadOnlyList<ScatterErrorPoint> points, DataPoint? minimum, double pad = 0.1) {
            double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
            double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
            bool any = false;

            if (points != null) {
                foreach (var p in points) {
                    if (!IsFinite(p.X) || !IsFinite(p.Y)) {
                        continue;
                    }
                    xMin = Math.Min(xMin, p.X);
                    xMax = Math.Max(xMax, p.X);
                    yMin = Math.Min(yMin, p.Y);
                    yMax = Math.Max(yMax, p.Y);
                    any = true;
                }
            }

            if (minimum.HasValue && IsFinite(minimum.Value.X) && IsFinite(minimum.Value.Y)) {
                xMin = Math.Min(xMin, minimum.Value.X);
                xMax = Math.Max(xMax, minimum.Value.X);
                yMin = Math.Min(yMin, minimum.Value.Y);
                yMax = Math.Max(yMax, minimum.Value.Y);
                any = true;
            }

            if (!any) {
                return new FocusGraphAxisBounds(0.0, 1.0, 0.0, 1.0);
            }

            var (px0, px1) = Pad(xMin, xMax, pad);
            var (py0, py1) = Pad(yMin, yMax, pad);
            return new FocusGraphAxisBounds(px0, px1, py0, py1);
        }

        private static (double min, double max) Pad(double min, double max, double pad) {
            double range = max - min;
            if (!(range > 0.0)) {
                // Zero-width (single distinct value): expand around it so the axis is not a degenerate line.
                double half = Math.Max(1.0, Math.Abs(max) * pad);
                return (min - half, max + half);
            }
            double delta = range * pad;
            return (min - delta, max + delta);
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
