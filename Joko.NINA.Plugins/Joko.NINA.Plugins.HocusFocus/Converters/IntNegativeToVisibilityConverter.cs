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

namespace Joko.NINA.Plugins.HocusFocus.Converters {

    public class IntNegativeToVisibilityConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is int) {
                var d = (int)value;
                if (d < 0) {
                    return System.Windows.Visibility.Collapsed;
                } else {
                    return System.Windows.Visibility.Visible;
                }
            } else if (value is null) {
                return System.Windows.Visibility.Collapsed;
            }
            throw new ArgumentException("Invalid Type for Converter");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}