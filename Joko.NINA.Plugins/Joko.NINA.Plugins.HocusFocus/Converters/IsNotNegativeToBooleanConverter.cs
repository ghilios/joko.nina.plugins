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

    public class IsNotNegativeToBooleanConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is double) {
                var d = (double)value;
                return d >= 0.0d;
            } else if (value is decimal) {
                var d = (decimal)value;
                return d >= decimal.Zero;
            } else if (value is float) {
                var d = (float)value;
                return d >= 0.0f;
            } else if (value is int) {
                var d = (int)value;
                return d >= 0;
            }
            throw new ArgumentException("Invalid Type for Converter");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}