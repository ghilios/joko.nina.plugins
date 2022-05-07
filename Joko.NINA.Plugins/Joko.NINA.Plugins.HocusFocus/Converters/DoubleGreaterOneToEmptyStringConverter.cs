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

    public class DoubleGreaterOneToEmptyStringConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is double) {
                var d = (double)value;
                if (d >= 1.0d) {
                    return string.Empty;
                }
                return $"{d:0.00}";
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value == null) {
                return 1.0d;
            }
            var v = value.ToString();
            if (double.TryParse(value.ToString(), out var d)) {
                return d;
            }
            return 1.0d;
        }
    }
}