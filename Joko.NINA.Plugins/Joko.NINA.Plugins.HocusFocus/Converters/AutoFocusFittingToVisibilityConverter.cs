#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.WPF.Base.Utility.AutoFocus;
using System;
using System.Globalization;
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