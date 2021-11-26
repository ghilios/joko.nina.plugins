#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Globalization;
using System.Windows.Controls;

namespace Joko.NINA.Plugins.Common.ValidationRules {

    public class PositiveIntegerOrInfiniteRule : ValidationRule {

        public override ValidationResult Validate(object value, CultureInfo cultureInfo) {
            if (value is null) {
                return new ValidationResult(false, "Null value");
            }
            var s = value.ToString();
            if (s == "unlimited") {
                return new ValidationResult(true, null);
            }
            if (int.TryParse(s, NumberStyles.Number, cultureInfo, out var _)) {
                return new ValidationResult(true, null);
            } else {
                return new ValidationResult(false, "Value must be an integer or unlimited");
            }
        }
    }
}