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

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    /// <summary>
    /// Formats a resolved fallback value for a <c>HintTextBox</c>'s greyed hint: the number when it is usable
    /// (finite and &gt; 0), otherwise the ConverterParameter text (default "(not set)").
    ///
    /// <para>Written for the Focuser Step Size boxes, whose hint shows the value an EMPTY box will actually
    /// resolve to — the focuser driver's reported step size, or nothing when no driver supplies one
    /// (docs/focuser-step-size-driver-design.md §5). Without it, a user whose driver reports a step size sees
    /// an empty box and concludes the feature is off.</para>
    ///
    /// <para><c>&gt; 0</c> and not <c>!(&lt;= 0)</c>: NaN fails BOTH comparisons, and only the positive form
    /// routes it to the fallback text instead of rendering "NaN" at the user.</para>
    /// </summary>
    public class PositiveDoubleToHintTextConverter : IValueConverter {
        private const string DefaultHint = "(not set)";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            var fallback = parameter as string;
            if (string.IsNullOrEmpty(fallback)) {
                fallback = DefaultHint;
            }
            if (value == null) {
                return fallback;
            }
            double resolved;
            try {
                resolved = System.Convert.ToDouble(value, culture);
            } catch (Exception) {
                return fallback;
            }
            return resolved > 0.0 ? resolved.ToString("0.###", culture) : fallback;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException("The hint is display-only; bind the editable value separately.");
    }
}
