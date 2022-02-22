#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILNumerics.Drawing;
using System.Windows;
using System.Windows.Controls;
using ILNPanel = ILNumerics.Drawing.Panel;
using ILNLabel = ILNumerics.Drawing.Label;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System.Windows.Media;
using System;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using System.IO;

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

        public static readonly DependencyProperty SceneContainerProperty = DependencyProperty.Register(
            "SceneContainer",
            typeof(ILNSceneContainer),
            typeof(ILNCanvasControl),
            new FrameworkPropertyMetadata(
                default(ILNSceneContainer),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnScenePropertyChanged));

        public ScrollViewer ParentScrollViewer {
            get { return (ScrollViewer)GetValue(ParentScrollViewerProperty); }
            set { SetValue(ParentScrollViewerProperty, value); }
        }

        public ILNSceneContainer SceneContainer {
            get { return (ILNSceneContainer)GetValue(SceneContainerProperty); }
            set { SetValue(SceneContainerProperty, value); }
        }

        private static void OnParentScrollViewerPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        }

        private static void OnScenePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ILNCanvasControl)d;
            var panel = thisControl.ILNPanel;
            if (panel == null && e.NewValue != null) {
                panel = thisControl.InitializePanel();
            }

            var oldSceneContainer = (ILNSceneContainer)e.OldValue;
            var sceneContainer = (ILNSceneContainer)e.NewValue;
            var newScene = sceneContainer?.Scene;
            if (newScene != null) {
                using (MemoryStream ms = new MemoryStream()) {
                    var width = thisControl.ActualWidth;
                    var height = thisControl.ActualHeight;
                    new SVGDriver(ms, width: (int)width, height: (int)height, scene: newScene).Render();
                    ms.Position = 0;

                    thisControl.SVG.Load(ms);
                }
            } else {
                thisControl.SVG.Unload();
            }

            /*
            panel.MouseWheel += Panel_MouseWheel;
            if (e.NewValue == null) {
                thisControl.ILNumericsContainer.Child = null;
                thisControl.ILNPanel?.Dispose();
                thisControl.ILNPanel = null;
            } else {
                panel.Scene = newScene;
                var solidColorBackgroundBrush = thisControl.Background as SolidColorBrush;
                if (solidColorBackgroundBrush != null) {
                    var backgroundColor = solidColorBackgroundBrush.Color.ToDrawingColor();
                    panel.BackColor = backgroundColor;
                }

                var solidColorForegroundBrush = thisControl.Foreground as SolidColorBrush;
                if (solidColorForegroundBrush != null) {
                    var secondaryColor = solidColorForegroundBrush.Color.ToDrawingColor();
                    panel.ForeColor = secondaryColor;
                }

                sceneContainer.UpdateForegroundBrush(thisControl.Foreground);
                sceneContainer.UpdateBackgroundBrush(thisControl.Background);
                panel.Configure();
                panel.Invalidate(true);
                panel.Update();

                oldSceneContainer?.Scene?.Dispose();
            }
            */
        }

        private static void Panel_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e) {
            Console.WriteLine();
        }

        public ILNCanvasControl() {
            InitializeComponent();

            this.MouseWheel += ILNCanvasControl_MouseWheel;
        }

        private void ILNCanvasControl_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) {
            Console.WriteLine();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) {
            base.OnPropertyChanged(e);
            var panel = ILNPanel;
            if (panel != null) {
                if (e.Property == Control.BackgroundProperty) {
                    var brush = e.NewValue as Brush;
                    var solidColorBackgroundBrush = brush as SolidColorBrush;
                    if (solidColorBackgroundBrush != null) {
                        panel.BackColor = solidColorBackgroundBrush.Color.ToDrawingColor();
                    }

                    var sceneContainer = SceneContainer;
                    sceneContainer?.UpdateBackgroundBrush(brush);
                    panel.Configure();
                    panel.Invalidate(true);
                    panel.Update();
                } else if (e.Property == Control.ForegroundProperty) {
                    var brush = e.NewValue as Brush;
                    var solidColorForegroundBrush = brush as SolidColorBrush;
                    if (solidColorForegroundBrush != null) {
                        var secondaryColor = solidColorForegroundBrush.Color.ToDrawingColor();
                        panel.ForeColor = secondaryColor;
                    }

                    var sceneContainer = SceneContainer;
                    sceneContainer?.UpdateForegroundBrush(brush);
                    panel.Configure();
                    panel.Invalidate(true);
                    panel.Update();
                }
            }
        }

        private ILNPanel InitializePanel() {
            ILNPanel = new ILNPanel();
            ILNPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            ILNPanel.Name = "ILNumericsPanel";
            ILNPanel.RendererType = ILNumerics.Drawing.RendererTypes.OpenGL;
            ILNPanel.ShowUIControls = false;

            /*
            ILNumericsContainer.Child = ILNPanel;
            ILNumericsContainer.Background = Background;
            */
            return ILNPanel;
        }

        public ILNPanel ILNPanel { get; set; }
    }
}