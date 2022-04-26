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
using System.Windows;
using System.Windows.Data;

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    public class MultiBooleanToZeroConverter : IMultiValueConverter {

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            var trueValue = (double)values[0];
            if (values.Length <= 1) {
                return 0.0d;
            }

            foreach (var value in values) {
                if (value == null) {
                    return 0.0d;
                }
                if (value is bool && !(bool)value) {
                    return 0.0d;
                }
            }
            return trueValue;
        }

        object[] IMultiValueConverter.ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}