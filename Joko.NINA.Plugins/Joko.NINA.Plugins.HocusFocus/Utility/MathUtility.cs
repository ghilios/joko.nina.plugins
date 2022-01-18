﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Runtime.CompilerServices;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public static class MathUtility {

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
    }
}