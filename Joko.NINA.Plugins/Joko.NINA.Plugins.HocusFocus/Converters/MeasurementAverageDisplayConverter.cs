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

    public class MeasurementAverageDisplayConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (targetType == typeof(string)) {
                var measurementAverage = value as MeasurementAverageEnum?;
                if (measurementAverage != null) {
                    if (measurementAverage.Value == MeasurementAverageEnum.Median) {
                        return "HFR MAD";
                    }
                }
                return global::NINA.Core.Locale.Loc.Instance["LblHFRStDev"];
            }
            throw new ArgumentException("Invalid Type for Converter");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}