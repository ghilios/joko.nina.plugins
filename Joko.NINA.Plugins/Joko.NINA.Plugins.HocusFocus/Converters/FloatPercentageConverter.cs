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

    public class FloatPercentageConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (string.IsNullOrEmpty(value?.ToString())) return 0;

            int decimals;
            switch (value) {
                case float floatValue when parameter is null:
                    return floatValue * 100f;

                case float floatValue when parameter is string stringValue:
                    return Math.Round(floatValue * 100f, int.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimals) ? decimals : 0);

                default:
                    return value;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            if (string.IsNullOrEmpty(value?.ToString())) return 0;

            var trimmedValue = value.ToString().TrimEnd('%');

            int decimals;
            switch (targetType) {
                case Type floatType when floatType == typeof(float) && parameter is null:
                    return float.TryParse(trimmedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var resultFloat) ? resultFloat / 100f : value;

                case Type floatType when floatType == typeof(float) && parameter is string stringValue:
                    int.TryParse(stringValue, out decimals);
                    return float.TryParse(trimmedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var resultFloatRoundedToDecimals)
                        ? Math.Round(resultFloatRoundedToDecimals / 100f, decimals + 2)
                        : value;

                default:
                    return value;
            }
        }
    }
}