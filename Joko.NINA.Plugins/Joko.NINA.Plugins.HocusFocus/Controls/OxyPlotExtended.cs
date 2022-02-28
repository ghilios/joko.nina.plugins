#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using OxyPlot;
using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class OxyPlotExtended : OxyPlot.Wpf.Plot {

        public static readonly DependencyProperty PlotModelProperty = DependencyProperty.Register(
            "PlotModel",
            typeof(PlotModel),
            typeof(OxyPlotExtended));

        public PlotModel PlotModel {
            get { return (PlotModel)GetValue(PlotModelProperty); }
            set { SetValue(PlotModelProperty, value); }
        }

        public OxyPlotExtended() : base() {
            SetCurrentValue(PlotModelProperty, ActualModel);
        }
    }
}