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

namespace Joko.NINA.Plugins.Common.Converters {

    public class InverseDoubleZeroToVisibilityConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is double) {
                var d = (double)value;
                if (Math.Abs(d) < 0.00001d) {
                    return System.Windows.Visibility.Visible;
                } else {
                    return System.Windows.Visibility.Collapsed;
                }
            } else if (value is float) {
                var d = (float)value;
                if (Math.Abs(d) < 0.00001f) {
                    return System.Windows.Visibility.Visible;
                } else {
                    return System.Windows.Visibility.Collapsed;
                }
            } else if (value is null) {
                return System.Windows.Visibility.Visible;
            }
            throw new ArgumentException("Invalid Type for Converter");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}