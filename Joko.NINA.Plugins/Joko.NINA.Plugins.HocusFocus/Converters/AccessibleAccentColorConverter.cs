#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    /// <summary>
    /// Foreground for INLINE alert text drawn straight on the page: the schema's alert color, hue preserved,
    /// lightened or darkened only as far as WCAG AA needs against the page background.
    /// values[0] = alert color, values[1] = page background color.
    ///
    /// Assumes the binding target is a Color dependency property, e.g. SolidColorBrush.Color — targetType is
    /// otherwise ignored. Do not bind this converter directly to a Brush property.
    /// </summary>
    public class AccessibleAccentColorConverter : IMultiValueConverter {

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            // The first pass of a resource-level binding arrives as an UnsetValue entry (or, for a null/empty
            // array, no entries at all). White rather than Binding.DoNothing: DoNothing would leave
            // SolidColorBrush.Color at Transparent (invisible text).
            if (values == null || values.Length < 1 || !(values[0] is Color alert)) {
                return Colors.White;
            }
            // Background not resolved yet: degrade to the raw alert color (exactly today's behaviour) rather
            // than guessing. The real value arrives on the next pass.
            if (values.Length < 2 || !(values[1] is Color background)) {
                return alert;
            }
            return ContrastMath.AccessibleAccent(alert, background);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException("One-way only: bind with Mode=OneWay.");
        }
    }
}
