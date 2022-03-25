#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

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
    }
}