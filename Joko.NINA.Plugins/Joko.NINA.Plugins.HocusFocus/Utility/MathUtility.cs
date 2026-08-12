#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public static class MathUtility {
        private const double RadiansToDegreeFactor = 180d / Math.PI;
        private const double ArcSecPerPixConversionFactor = RadiansToDegreeFactor * 60d * 60d / 1000d;

        // Copied to avoid static initializer in AstroUtil from NINA. That should be changed!
        public static double ArcsecPerPixel(double pixelSize, double focalLength) {
            // arcseconds inside one radian and compensated by the difference of microns in pixels and mm in focal length
            return (pixelSize / focalLength) * ArcSecPerPixConversionFactor;
        }

        public static double RadiansToDegrees(double radians) {
            return radians * RadiansToDegreeFactor;
        }

        private static int PartitionFloat(this float[] arr, int start, int end, Random rnd = null) {
            if (rnd != null)
                arr.Swap(end, rnd.Next(start, end + 1));

            var pivot = arr[end];
            var lastLow = start - 1;
            for (var i = start; i < end; i++) {
                if (arr[i] < pivot)
                    arr.Swap(i, ++lastLow);
            }
            arr.Swap(end, ++lastLow);
            return lastLow;
        }

        public static float NthOrderStatisticFloat(this float[] arr, int n, Random rnd = null) {
            return NthOrderStatisticFloat(arr, n, 0, arr.Length - 1, rnd);
        }

        private static float NthOrderStatisticFloat(this float[] arr, int n, int start, int end, Random rnd) {
            while (true) {
                var pivotIndex = arr.PartitionFloat(start, end, rnd);
                if (pivotIndex == n)
                    return arr[pivotIndex];

                if (n < pivotIndex)
                    end = pivotIndex - 1;
                else
                    start = pivotIndex + 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Swap<T>(this T[] list, int i, int j) {
            if (i == j)
                return;
            var temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }

        // https://stackoverflow.com/questions/4140719/calculate-median-in-c-sharp
        public static float MedianFloat(this float[] arr, Random rnd = null) {
            return arr.NthOrderStatisticFloat((arr.Length - 1) / 2, rnd);
        }

        public static (double, double) MedianMAD(this IEnumerable<double> values) {
            var valuesArray = values.ToArray();
            if (valuesArray.Length == 0) {
                return (double.NaN, double.NaN);
            }
            Array.Sort(valuesArray);

            var median = valuesArray.Length % 2 == 0
              ? (valuesArray[valuesArray.Length / 2 - 1] + valuesArray[valuesArray.Length / 2]) / 2.0
              : valuesArray[valuesArray.Length / 2];

            for (int i = 0; i < valuesArray.Length; ++i) {
                valuesArray[i] = Math.Abs(valuesArray[i] - median);
            }
            Array.Sort(valuesArray);

            var medianAbsoluteDeviation = valuesArray.Length % 2 == 0
              ? (valuesArray[valuesArray.Length / 2 - 1] + valuesArray[valuesArray.Length / 2]) / 2.0
              : valuesArray[valuesArray.Length / 2];
            var mad = 1.483 * medianAbsoluteDeviation;
            return (median, mad);
        }

        public static (double, double) MeanVar(this IEnumerable<double> values) {
            var mean = values.Average();
            var count = values.Count();
            var variance = values.Sum(s => (s - mean) * (s - mean)) / (count - 1);
            return (mean, variance);
        }

        public static float DotProduct(float[] x, float[] y) {
            if (x.Length != y.Length) {
                throw new ArgumentException($"x length ({x.Length}) must be equal to y length ({y.Length})");
            }
            float ssd = 0.0f;
            for (int i = 0; i < x.Length; ++i) {
                ssd += x[i] * y[i];
            }
            return ssd;
        }

        public static float SumOfSquaresOfDifferences(float[] x, float[] y) {
            if (x.Length != y.Length) {
                throw new ArgumentException($"x length ({x.Length}) must be equal to y length ({y.Length})");
            }
            float ssd = 0.0f;
            for (int i = 0; i < x.Length; ++i) {
                var diff = y[i] - x[i];
                ssd += diff * diff;
            }
            return ssd;
        }

        /// <summary>
        /// Two-tailed Grubbs outlier test on the residuals of <paramref name="points"/> against
        /// <paramref name="fitting"/>. Returns the single worst outlier, or null if none is significant at
        /// <paramref name="confidence"/>. Intended to be applied iteratively (re-fit after each removal).
        ///
        /// The residual location/scale use the <b>median and MAD</b> rather than the mean and standard
        /// deviation: a gross outlier shifts the mean and inflates the standard deviation, so the classic
        /// mean/stddev statistic can mask the very outlier it should flag (and can flag a good point instead).
        /// The MAD has a 50% breakdown point and is immune to this. When the MAD degenerates to zero (e.g.
        /// noiseless data where more than half the residuals are identical) the scale falls back to the
        /// standard deviation so a lone gross outlier is still detectable.
        ///
        /// When <paramref name="weights"/> is supplied (typically 1/σ at each x), the residuals are weighted
        /// to match the weighting the fit actually minimized, so a point that is far from the curve but has a
        /// large measurement uncertainty (low weight) is not treated as an outlier.
        /// </summary>
        public static ScatterErrorPoint RejectionTest(
                List<ScatterErrorPoint> points,
                Func<double, double> fitting,
                double confidence,
                Func<double, double> weights = null)
            // Delegates with scaleFloor = 0.0, where the floor branch below is not entered at all — so this
            // overload's arithmetic is bit-identical to what it was before the floor existed. That identity is
            // the whole basis of the wave-14 control rung, so it is preserved by having exactly ONE
            // implementation rather than two that must be kept in step.
            => RejectionTest(points, fitting, confidence, weights, scaleFloor: 0.0, scaleUsed: out _);

        /// <summary>
        /// <see cref="RejectionTest(List{ScatterErrorPoint}, Func{double, double}, double, Func{double, double})"/>
        /// with an optional floor on the residual scale, and the scale it actually used reported back.
        ///
        /// <para><paramref name="scaleFloor"/> is applied <b>after</b> both degenerate-scale guards, never
        /// instead of them (see <see cref="MadFloorSpec"/> for why the order is load-bearing): the stdDev
        /// fallback still fires on a collapsed MAD, a still-degenerate scale still returns null, and only then is
        /// <c>scale ← max(scale, floor)</c> applied. Because that is monotone, a floor can only ever
        /// <b>suppress</b> a rejection — it can never add one, and it can never redirect one to a different
        /// point (every z in a round is divided by the same scale, so the argmax cannot move).</para>
        ///
        /// <para><paramref name="scaleUsed"/> is the effective scale the z-scores were computed with — what a
        /// caller needs to anchor family B to round 1, and what the af-fit report prints at full precision
        /// instead of making the next wave recover it from a 4-dp field. It is <see cref="double.NaN"/> on every
        /// early return (too few points, or a degenerate scale), because "no test was performed" must not be
        /// reported as a scale of zero.</para>
        ///
        /// <para><b>This overload takes both extra arguments explicitly, and neither is optional.</b> That is the
        /// shape rather than adding optional parameters to the existing signature, so the three
        /// <c>AutoFocusEngine</c> call sites and the one in <c>SensorModel</c> keep binding to the 4-argument
        /// overload with no change in behaviour and no chance of silently matching a longer one — the floor is
        /// harness-only, and the only way to reach it is to say so in full.</para>
        /// </summary>
        public static ScatterErrorPoint RejectionTest(
                List<ScatterErrorPoint> points,
                Func<double, double> fitting,
                double confidence,
                Func<double, double> weights,
                double scaleFloor,
                out double scaleUsed)
            // Delegates with SemScaleSpec.None and no star counts, where neither SEM branch below is entered at
            // all — so this overload's arithmetic is bit-identical to what it was before the SEM criterion
            // existed, for the same reason and by the same means as the four-argument overload above. ONE
            // implementation, never two that must be kept in step.
            => RejectionTest(points, fitting, confidence, weights, scaleFloor,
                SemScaleSpec.None, starCounts: null, out scaleUsed, out _);

        /// <summary>
        /// <see cref="RejectionTest(List{ScatterErrorPoint}, Func{double, double}, double, Func{double, double}, double, out double)"/>
        /// with an optional SEM criterion (wave 16 item A, F45(b) in the units the error bar actually implies)
        /// and the selected point's SEM-scaled residual reported back.
        ///
        /// <para><paramref name="starCounts"/> supplies <c>N*</c>, the number of detected stars each point's σ
        /// was measured over, keyed by <c>p.X</c> — the same side-map shape
        /// <c>AlglibHyperbolicFitting.BuildResidualWeights</c> uses for the 1/σ weights, so nothing about the
        /// point type or the production path changes. <b>A non-<see cref="SemScaleSpec.None"/> spec with no star
        /// counts THROWS</b> rather than quietly behaving like <see cref="SemScaleSpec.None"/>: a criterion that
        /// silently does nothing when its input is missing is a probe that reports absence as agreement.</para>
        ///
        /// <para><paramref name="semScale"/> family V is applied <b>strictly after</b> the Grubbs comparison, so
        /// it can only ever turn a rejection into no rejection — the argmax cannot move and no rejection can be
        /// created. Family R rescales the residuals <b>before</b> the median/MAD, which is not monotone and
        /// <i>can</i> move the argmax; that is measured and reported separately, never fed to the subset gate.</para>
        ///
        /// <para><paramref name="semOfSelected"/> is always the <b>unscaled</b> <c>|r|·√N*</c> of the point the
        /// test selected, at every rung including family R, so the number a report prints is comparable across
        /// the whole ladder. It is computed for the selected point <b>whether or not the rejection stands</b>, so
        /// a round that rejects nothing still has a number to print, and it is <see cref="double.NaN"/> when no
        /// star counts were supplied or no test was performed.</para>
        /// </summary>
        public static ScatterErrorPoint RejectionTest(
                List<ScatterErrorPoint> points,
                Func<double, double> fitting,
                double confidence,
                Func<double, double> weights,
                double scaleFloor,
                SemScaleSpec semScale,
                Func<double, double> starCounts,
                out double scaleUsed,
                out double semOfSelected) {
            scaleUsed = double.NaN;
            semOfSelected = double.NaN;
            if (!semScale.IsNone && starCounts == null) {
                // Loud, and before anything is measured. The alternative — treating a missing side map as
                // "no criterion" — produces an arm that runs the control while its report names a SEM rung.
                throw new InvalidOperationException(
                    $"A SEM scale spec was supplied ({semScale}) but no star counts were. N* is what the " +
                    "criterion is expressed in; without it this call would silently run the control rung.");
            }
            if (points.Count <= 3) {
                return null;
            }

            var rawErrors = points.Select(p => {
                var residual = p.Y - fitting(p.X);
                return weights != null ? weights(p.X) * residual : residual;
            }).ToArray();

            // Family R only, and guarded so the default path does not even evaluate a Math.Sqrt: the statistic
            // itself is computed on r*sqrt(N*). rawErrors is kept unscaled so semOfSelected below stays
            // comparable with every other rung.
            var errors = rawErrors;
            if (semScale.IsRank) {
                errors = new double[rawErrors.Length];
                for (int i = 0; i < rawErrors.Length; ++i) {
                    errors[i] = rawErrors[i] * Math.Sqrt(Math.Max(starCounts(points[i].X), 1.0));
                }
            }

            var (median, mad) = errors.MedianMAD();
            var scale = mad;
            if (scale <= 0.0 || double.IsNaN(scale)) {
                var (_, stdDev) = MathNet.Numerics.Statistics.Statistics.MeanStandardDeviation(errors);
                scale = stdDev;
            }
            if (scale <= 0.0 || double.IsNaN(scale)) {
                // Perfect fit (or all residuals identical): no outliers possible.
                return null;
            }
            if (scaleFloor > 0.0) {
                // AFTER both guards, and guarded on > 0 so the default path does not even evaluate Math.Max —
                // the f = 0 rung must be bit-identical, not merely numerically equal.
                scale = Math.Max(scale, scaleFloor);
            }
            scaleUsed = scale;

            var N = points.Count;
            var p = (1.0 - confidence) / (2 * N); // Two-tailed test
            var t = MathNet.Numerics.Distributions.StudentT.InvCDF(location: 0.0d, scale: 1.0d, freedom: (double)(N - 2), p: p);
            var t2 = t * t;
            var grubbZLimit = (double)(N - 1) / Math.Sqrt(N) * Math.Sqrt(t2 / (t2 + N - 2));

            var maxError = points.Select((pt, i) => (z: Math.Abs(errors[i] - median) / scale, i)).MaxBy(v => v.z);
            if (starCounts != null) {
                // The UNSCALED |r| times sqrt(N*), for the point the test selected — reported at every rung
                // (including family R, whose statistic already carries the factor) so the printed s means the
                // same thing everywhere, and before the Grubbs comparison so a round that rejects nothing still
                // reports the point it looked at.
                semOfSelected = Math.Abs(rawErrors[maxError.i]) * Math.Sqrt(Math.Max(starCounts(points[maxError.i].X), 1.0));
            }
            if (maxError.z < grubbZLimit) {
                return null;
            }
            if (semScale.IsVeto && semOfSelected < semScale.VetoThreshold) {
                // A veto, and ONLY a veto: its single outcome is `return null`, so it can turn a YES into a NO
                // and can do nothing else. That — not the position of this block — is what makes the prefix
                // property hold. (Moving these four lines above the Grubbs comparison is in fact behaviour-
                // preserving, since both branches return null; the mutant that BREAKS the property is the one
                // that lets the SEM comparison RETURN A POINT, i.e. makes it the decision rather than a veto.
                // SemScaleRejectionTests.AVeto_CannotCreateARejection_OnARoundTheGrubbsTestCleared is the test
                // that catches that, and it is the only one that can.) The order below is kept anyway, because
                // reading it in this order is what makes the claim obvious.
                return null;
            }
            return points[maxError.i];
        }

        public static double CalcSquaredDistance(Point2D p1, Point2D p2) {
            return (p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y);
        }
    }
}
