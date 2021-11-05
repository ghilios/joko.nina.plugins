using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Joko.NINA.Plugins.HocusFocus.Converters {
    public class IntNegativeToVisibilityConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is int) {
                var d = (int)value;
                if (d < 0) { 
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
