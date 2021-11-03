using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Joko.NINA.Plugins.HocusFocus.Converters {

    public class TimeSpanToStringConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is TimeSpan) {
                var ts = (TimeSpan)value;
                var sb = new StringBuilder();
                if (ts.Days > 0) {
                    sb.Append($"{ts.Days} days ");
                }
                if (ts.Days > 0 || ts.Hours > 0) {
                    sb.Append($"{ts.Hours:00}:");
                }
                sb.Append($"{ts.Minutes:00}:{ts.Seconds:00.00}");
                return sb.ToString();
            }
            throw new ArgumentException("Invalid Type for Converter");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
