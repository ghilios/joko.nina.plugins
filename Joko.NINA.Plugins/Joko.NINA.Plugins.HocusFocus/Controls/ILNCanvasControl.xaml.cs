#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILNumerics.Drawing;
using System;
using System.Windows;
using System.Windows.Controls;
using ILNPanel = ILNumerics.Drawing.Panel;
using DrawingColor = System.Drawing.Color;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    /// <summary>
    /// Interaction logic for ILNCanvasControl.xaml
    /// </summary>
    public partial class ILNCanvasControl : UserControl {

        public static readonly DependencyProperty ParentScrollViewerProperty = DependencyProperty.Register(
            "ParentScrollViewer",
            typeof(ScrollViewer),
            typeof(ILNCanvasControl),
            new FrameworkPropertyMetadata(
                default(ScrollViewer),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnParentScrollViewerPropertyChanged));

        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
            "Scene",
            typeof(Scene),
            typeof(ILNCanvasControl),
            new FrameworkPropertyMetadata(
                default(Scene),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnScenePropertyChanged));

        public static readonly DependencyProperty BackgroundColorProperty = DependencyProperty.Register(
            "BackgroundColor",
            typeof(DrawingColor),
            typeof(ILNCanvasControl),
            new FrameworkPropertyMetadata(
                default(DrawingColor),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBackgroundColorPropertyChanged));

        public ScrollViewer ParentScrollViewer {
            get { return (ScrollViewer)GetValue(ParentScrollViewerProperty); }
            set { SetValue(ParentScrollViewerProperty, value); }
        }

        public Scene Scene {
            get { return (Scene)GetValue(SceneProperty); }
            set { SetValue(SceneProperty, value); }
        }

        public DrawingColor BackgroundColor {
            get { return (DrawingColor)GetValue(BackgroundColorProperty); }
            set { SetValue(BackgroundColorProperty, value); }
        }

        private static void OnParentScrollViewerPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        }

        private static void OnScenePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ILNCanvasControl)d;
            var panel = thisControl.ILNPanel;
            if (panel == null && e.NewValue != null) {
                panel = thisControl.InitializePanel();
            }

            if (e.NewValue == null) {
                thisControl.ILNumericsContainer.Child = null;
                thisControl.ILNPanel?.Dispose();
                thisControl.ILNPanel = null;
            } else {
                var oldValue = (Scene)e.OldValue;
                panel.Scene = (Scene)e.NewValue;
                panel.Configure();
                panel.Invalidate(true);
                panel.Update();

                oldValue?.Dispose();
            }
        }

        private static void OnBackgroundColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ILNCanvasControl)d;
            var panel = thisControl.ILNPanel;
            panel.BackColor = (DrawingColor)e.NewValue;
            panel.Configure();
            panel.Invalidate(true);
            panel.Update();
        }

        public ILNCanvasControl() {
            InitializeComponent();
        }

        private ILNPanel InitializePanel() {
            ILNPanel = new ILNPanel();
            ILNPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            ILNPanel.Name = "ILNumericsPanel";
            ILNPanel.RendererType = ILNumerics.Drawing.RendererTypes.OpenGL;
            ILNPanel.ShowUIControls = false;
            ILNumericsContainer.Child = ILNPanel;
            return ILNPanel;
        }

        public ILNPanel ILNPanel;
    }
}