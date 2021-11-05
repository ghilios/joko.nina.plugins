using Joko.NINA.Plugins.HocusFocus.AutoFocus;
using NINA.Core.Enum;
using NINA.WPF.Base.Utility.AutoFocus;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Joko.NINA.Plugins.HocusFocus.Converters {
    public class AutoFocusFittingToVisibilityConverter : IMultiValueConverter {

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            var source = values[0] as string;
            var method = values[1] as AFMethodEnum?;
            var fitting = values[2] as AFCurveFittingEnum?;
            if (source == null || method == null || fitting == null) {
                return Visibility.Collapsed;
            }

            var gaussianFitting = values[3] as GaussianFitting;
            var hyperbolicFitting = values[4] as HyperbolicFitting;
            var quadraticFitting = values[5] as QuadraticFitting;
            var trendlineFitting = values[6] as TrendlineFitting;
            if (method == AFMethodEnum.CONTRASTDETECTION) {
                if (source == "GaussianFitting") {
                    return gaussianFitting != null ? Visibility.Visible : Visibility.Collapsed;
                }
            } else {
                if (source == "HyperbolicFitting" && (fitting == AFCurveFittingEnum.HYPERBOLIC || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC)) {
                    return hyperbolicFitting != null ? Visibility.Visible : Visibility.Collapsed;
                }
                if (source == "QuadraticFitting" && (fitting == AFCurveFittingEnum.PARABOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC)) {
                    return quadraticFitting != null ? Visibility.Visible : Visibility.Collapsed;
                }
                if (source == "Trendline" && (fitting == AFCurveFittingEnum.TRENDLINES || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC)) {
                    return trendlineFitting != null ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            return Visibility.Collapsed;
        }

        object[] IMultiValueConverter.ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
