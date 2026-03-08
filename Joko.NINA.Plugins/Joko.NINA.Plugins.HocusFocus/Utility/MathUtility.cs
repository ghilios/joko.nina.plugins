#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

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

            var mad = 1.483 * valuesArray.Length % 2 == 0
              ? (valuesArray[valuesArray.Length / 2 - 1] + valuesArray[valuesArray.Length / 2]) / 2.0
              : valuesArray[valuesArray.Length / 2];
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

        public static ScatterErrorPoint RejectionTest(
                List<ScatterErrorPoint> points,
                Func<double, double> fitting,
                double confidence) {
            if (points.Count <= 3) {
                return null;
            }

            var errors = points.Select(p => p.Y - fitting(p.X)).ToArray();
            var (errorsMean, errorsStdDev) = MathNet.Numerics.Statistics.Statistics.MeanStandardDeviation(errors);
            var N = points.Count;
            var p = (1.0 - confidence) / (2 * N); // Two-tailed test
            var t = MathNet.Numerics.Distributions.StudentT.InvCDF(location: 0.0d, scale: 1.0d, freedom: (double)(N - 2), p: p);
            var t2 = t * t;
            var grubbZLimit = (double)(N - 1) / Math.Sqrt(N) * Math.Sqrt(t2 / (t2 + N - 2));

            var maxError = errors.Select((e, i) => (e, i)).MaxBy(v => Math.Abs(v.e));
            var maxErrorZScore = Math.Abs(maxError.e) / errorsStdDev;
            if (maxErrorZScore < grubbZLimit) {
                return null;
            }
            return points[maxError.i];
        }

        public static double CalcDistance(Point2D p1, Point2D p2) {
            return (p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y);
        }
    }
}