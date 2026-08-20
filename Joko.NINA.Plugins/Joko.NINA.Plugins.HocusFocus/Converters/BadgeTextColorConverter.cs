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
    /// Text color for a FILLED alert badge: black or white, whichever is readable on the badge's own fill.
    /// Deliberately not NINA's NotificationErrorTextColor — see the note on <see cref="ContrastMath"/>.
    /// </summary>
    public class BadgeTextColorConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            // The first pass of a resource-level binding arrives as DependencyProperty.UnsetValue. White rather
            // than Binding.DoNothing: DoNothing would leave SolidColorBrush.Color at Transparent (invisible
            // text), and white is the computed answer on all 18 built-in schemas.
            return value is Color fill ? ContrastMath.BestContrastText(fill) : Colors.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
