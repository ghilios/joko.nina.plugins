#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Globalization;
using System.Windows.Data;

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    public class ShowStructureMapToVisibilityConverter : IMultiValueConverter {

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            // The first is the enum value being tested, and the rest are values that result in True
            if (values.Length != 2) {
                throw new ArgumentException("Exactly 2 values required for StructureMapToVisibilityConverter");
            }
            if (!(values[0] is bool) || !(values[1] is ShowStructureMapEnum)) {
                return System.Windows.Visibility.Collapsed;
            }

            var debugMode = (bool)values[0];
            var showStructureMap = values[1] != null ? (ShowStructureMapEnum)values[1] : ShowStructureMapEnum.None;
            if (debugMode && showStructureMap != ShowStructureMapEnum.None) {
                return System.Windows.Visibility.Visible;
            } else {
                return System.Windows.Visibility.Collapsed;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}