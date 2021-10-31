using Joko.NINA.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Joko.NINA.Plugins.HocusFocus.Converters {
    public class ShowStructureMapToVisibilityConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            // The first is the enum value being tested, and the rest are values that result in True
            if (values.Length != 2) {
                throw new ArgumentException("Exactly 2 values required for StructureMapToVisibilityConverter");
            }
            if (!(values[0] is bool) || !(values[1] is ShowStructureMapEnum)) {
                return System.Windows.Visibility.Collapsed;
            }

            var debugMode = (bool)values[0];
            var showStructureMap = values[1] != null ? (ShowStructureMapEnum)values[1] : ShowStructureMapEnum.None;
            if (debugMode && showStructureMap != ShowStructureMapEnum.None) {
                return System.Windows.Visibility.Visible;
            } else {
                return System.Windows.Visibility.Collapsed;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
