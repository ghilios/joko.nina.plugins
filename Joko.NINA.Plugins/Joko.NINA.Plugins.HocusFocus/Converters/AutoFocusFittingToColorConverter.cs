#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    public class AutoFocusFittingToColorConverter : IMultiValueConverter {

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            var brushColor = Colors.Transparent;
            if (values.Length > 2) {
                if (values[0] != null && values[1] != null && values[2] != null && values[0] != DependencyProperty.UnsetValue && values[1] != DependencyProperty.UnsetValue && values[2] != DependencyProperty.UnsetValue) {
                    var source = (string)values[0];
                    var method = (AFMethodEnum)values[1];
                    var fitting = (AFCurveFittingEnum)values[2];
                    var autoFocusType = (AutoFocusType)values[3];

                    if (method == AFMethodEnum.CONTRASTDETECTION) {
                        if (source == "GaussianFitting") {
                            brushColor = (Application.Current.TryFindResource("ButtonBackgroundBrush") as SolidColorBrush).Color;
                        }
                    } else {
                        if (source == "HyperbolicFitting" && (fitting == AFCurveFittingEnum.HYPERBOLIC || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC)) {
                            brushColor = (Application.Current.TryFindResource("ButtonBackgroundBrush") as SolidColorBrush).Color;
                        }
                        if (source == "QuadraticFitting" && (fitting == AFCurveFittingEnum.PARABOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC)) {
                            brushColor = (Application.Current.TryFindResource("ButtonBackgroundBrush") as SolidColorBrush).Color;
                        }
                        if (source == "Trendline" && (fitting == AFCurveFittingEnum.TRENDLINES || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC)) {
                            brushColor = (Application.Current.TryFindResource("NotificationWarningBrush") as SolidColorBrush).Color;
                        }
                    }

                    if (autoFocusType == AutoFocusType.Registered && brushColor != Colors.Transparent) {
                        brushColor.A = (byte)(0.6 * byte.MaxValue);
                    }
                }
            }

            return brushColor;
        }

        object[] IMultiValueConverter.ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}