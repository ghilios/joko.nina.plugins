using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Joko.NINA.Plugins.HocusFocus.ValidationRules {
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
