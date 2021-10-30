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
                throw new ArgumentException("First parameter should be bool (DebugMode) and the second should be ShowStructureMapEnum");
            }

            var debugMode = (bool)values[0];
            var showStructureMap = (ShowStructureMapEnum)values[1];
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
