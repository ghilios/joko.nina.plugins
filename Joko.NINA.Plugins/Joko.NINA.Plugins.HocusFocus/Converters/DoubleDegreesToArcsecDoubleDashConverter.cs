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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    public class DoubleDegreesToArcsecDoubleDashConverter : IValueConverter {
        private static readonly double ArcsecPerDegree = 3600d;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            switch (value) {
                case double i when i == double.NaN:
                    return "--";

                case double i:
                    return i * ArcsecPerDegree;

                default:
                    return value;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            switch (value) {
                case string s when s == "--":
                    return double.NaN;

                case string s:
                    return double.Parse(s) / ArcsecPerDegree;

                default:
                    return value;
            }
        }
    }
}