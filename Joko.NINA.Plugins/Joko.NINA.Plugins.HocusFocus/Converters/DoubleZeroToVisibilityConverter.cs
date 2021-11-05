using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Joko.NINA.Plugins.HocusFocus.Converters {

    public class DoubleZeroToVisibilityConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is double) {
                var d = (double)value;
                if (Math.Abs(d) < 0.00001d) {
                    return System.Windows.Visibility.Collapsed;
                } else {
                    return System.Windows.Visibility.Visible;
                }
            } else if (value is float) {
                var d = (float)value;
                if (Math.Abs(d) < 0.00001f) {
                    return System.Windows.Visibility.Collapsed;
                } else {
                    return System.Windows.Visibility.Visible;
                }
            } else if (value is null) {
                return System.Windows.Visibility.Collapsed;
            }
            throw new ArgumentException("Invalid Type for Converter");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
