#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ScottPlot;
using System;
using System.Windows;
using System.Windows.Controls;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public interface IScottPlotController {

        event EventHandler PlotRefreshed;

        void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e);

        void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e);
    }

    /// <summary>
    /// Interaction logic for ScottPlotControl.xaml
    /// </summary>
    public partial class ScottPlotControl : UserControl {

        public static readonly DependencyProperty PlotProperty = DependencyProperty.Register(
            "Plot",
            typeof(Plot),
            typeof(ScottPlotControl),
            new FrameworkPropertyMetadata(
                default(Plot),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPlotPropertyChanged));

        public static readonly DependencyProperty WpfPlotNameProperty = DependencyProperty.Register(
            "WpfPlotName",
            typeof(string),
            typeof(ScottPlotControl),
            new FrameworkPropertyMetadata(
                "ScottPlot",
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnNamePropertyChanged));

        public Plot Plot {
            get { return (Plot)GetValue(PlotProperty); }
            set { SetValue(PlotProperty, value); }
        }

        public string WpfPlotName {
            get { return (string)GetValue(WpfPlotNameProperty); }
            set { SetValue(WpfPlotNameProperty, value); }
        }

        private static void OnPlotPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ScottPlotControl)d;
            var scottPlot = thisControl.ScottPlot;
            var newPlot = (Plot)e.NewValue;
            scottPlot.Reset(newPlot);
            scottPlot.Refresh();
        }

        private static void OnNamePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ScottPlotControl)d;
            var scottPlot = thisControl.ScottPlot;
            scottPlot.Name = (string)e.NewValue;
        }

        private void Plot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
            try {
                (this.DataContext as IScottPlotController)?.OnMouseMove(sender, e);
            } catch (Exception) { }
        }

        private void Plot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            try {
                (this.DataContext as IScottPlotController)?.OnMouseLeave(sender, e);
            } catch (Exception) { }
        }

        public ScottPlotControl() {
            InitializeComponent();

            ScottPlot.Configuration.Zoom = false;
            ScottPlot.Configuration.Pan = false;
            ScottPlot.Plot.XAxis.Ticks(false);
            ScottPlot.Plot.YAxis.Ticks(false);

            ScottPlot.MouseMove += Plot_MouseMove;
            ScottPlot.MouseLeave += Plot_MouseLeave;
            this.DataContextChanged += ScottPlotControl_DataContextChanged;
        }

        private void ScottPlotControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is IScottPlotController) {
                ((IScottPlotController)e.OldValue).PlotRefreshed -= ScottPlotControl_Refreshed;
            }
            if (e.NewValue is IScottPlotController) {
                var controller = (IScottPlotController)e.NewValue;
                controller.PlotRefreshed += ScottPlotControl_Refreshed;
            }
        }

        private void ScottPlotControl_Refreshed(object sender, EventArgs e) {
            this.ScottPlot.Refresh();
        }
    }
}