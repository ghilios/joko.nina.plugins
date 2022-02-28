#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Globalization;
using System.Windows.Data;

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    public class InterpolateColorConverter : IMultiValueConverter {

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            var startColor = (System.Windows.Media.Color)values[0];
            var endColor = (System.Windows.Media.Color)values[1];
            var index = (int)values[2];
            var total = (int)values[3];
            if (total <= 0 || index <= 0) {
                return startColor;
            } else if (total < index) {
                return endColor;
            } else {
                var ratio = (float)index / total;
                var a = (byte)(startColor.A + ratio * (endColor.A - startColor.A));
                var r = (byte)(startColor.R + ratio * (endColor.R - startColor.R));
                var g = (byte)(startColor.G + ratio * (endColor.G - startColor.G));
                var b = (byte)(startColor.B + ratio * (endColor.B - startColor.B));
                return System.Windows.Media.Color.FromArgb(a, r, g, b);
            }
        }

        object[] IMultiValueConverter.ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}