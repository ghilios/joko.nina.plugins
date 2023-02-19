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
using MediaColor = System.Windows.Media.Color;
using System.IO;
using NINA.Image.ImageAnalysis;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Utility;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    /// <summary>
    /// Interaction logic for ILNSceneControlBase.xaml
    /// </summary>
    public abstract partial class ILNSceneControlBase : UserControl {

        public static readonly DependencyProperty PlotBackgroundColorProperty = DependencyProperty.Register(
            "PlotBackgroundColor",
            typeof(MediaColor),
            typeof(ILNSceneControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnPlotBackgroundColorChangedPropertyChanged));

        public static readonly DependencyProperty TextColorProperty = DependencyProperty.Register(
            "TextColor",
            typeof(MediaColor),
            typeof(ILNSceneControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnTextColorChangedPropertyChanged));

        public static readonly DependencyProperty AxisColorProperty = DependencyProperty.Register(
            "AxisColor",
            typeof(MediaColor),
            typeof(ILNSceneControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAxisColorChangedPropertyChanged));

        public static readonly DependencyProperty PointColorProperty = DependencyProperty.Register(
            "PointColor",
            typeof(MediaColor),
            typeof(ILNSceneControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnPointColorChangedPropertyChanged));

        public static readonly DependencyProperty SurfaceColorProperty = DependencyProperty.Register(
            "SurfaceColor",
            typeof(MediaColor),
            typeof(ILNSceneControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSurfaceColorChangedPropertyChanged));

        public static readonly DependencyProperty RenderingEnabledProperty = DependencyProperty.Register(
            "RenderingEnabled",
            typeof(bool),
            typeof(ILNSceneControlBase),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

        public MediaColor PlotBackgroundColor {
            get { return (MediaColor)GetValue(PlotBackgroundColorProperty); }
            set { SetValue(PlotBackgroundColorProperty, value); }
        }

        public MediaColor TextColor {
            get { return (MediaColor)GetValue(TextColorProperty); }
            set { SetValue(TextColorProperty, value); }
        }

        public MediaColor AxisColor {
            get { return (MediaColor)GetValue(AxisColorProperty); }
            set { SetValue(AxisColorProperty, value); }
        }

        public MediaColor PointColor {
            get { return (MediaColor)GetValue(PointColorProperty); }
            set { SetValue(PointColorProperty, value); }
        }

        public MediaColor SurfaceColor {
            get { return (MediaColor)GetValue(SurfaceColorProperty); }
            set { SetValue(SurfaceColorProperty, value); }
        }

        public bool RenderingEnabled {
            get { return (bool)GetValue(RenderingEnabledProperty); }
            set { SetValue(RenderingEnabledProperty, value); }
        }

        public ILNSceneControlBase() {
            InitializeComponent();

            this.SizeChanged += TiltModelControl_SizeChanged;
        }

        private void TiltModelControl_SizeChanged(object sender, SizeChangedEventArgs e) {
            UpdateSceneImage();
        }

        private static void OnPlotBackgroundColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ILNSceneControlBase)d;
            thisControl.OnPlotBackgroundColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateSceneImage();
        }

        private static void OnTextColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ILNSceneControlBase)d;
            thisControl.OnTextColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateSceneImage();
        }

        private static void OnPointColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ILNSceneControlBase)d;
            thisControl.OnPointColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateSceneImage();
        }

        private static void OnAxisColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ILNSceneControlBase)d;
            thisControl.OnAxisColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateSceneImage();
        }

        private static void OnSurfaceColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (ILNSceneControlBase)d;
            thisControl.OnSurfaceColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateSceneImage();
        }

        protected virtual void OnPlotBackgroundColorChanged(MediaColor newColor) {
        }

        protected virtual void OnTextColorChanged(MediaColor newColor) {
        }

        protected virtual void OnAxisColorChanged(MediaColor newColor) {
        }

        protected virtual void OnPointColorChanged(MediaColor newColor) {
        }

        protected virtual void OnSurfaceColorChanged(MediaColor newColor) {
        }

        public Scene LocalScene { get; private set; }

        protected void ClearScene() {
            OnClearScene();
        }

        protected virtual void OnClearScene() {
            this.SceneImage.Visibility = System.Windows.Visibility.Collapsed;
            LocalScene = null;
        }

        protected void UpdateScene() {
            var scene = GetScene();
            if (scene == null) {
                ClearScene();
            } else {
                LocalScene = scene;
            }
            UpdateSceneImage();
        }

        protected abstract Scene GetScene();

        protected void UpdateSceneImage() {
            this.OnUpdateSceneImage();
        }

        protected virtual void OnUpdateSceneImage() {
            try {
                var scene = this.LocalScene;
                if (scene == null || !RenderingEnabled) {
                    this.SceneImage.Visibility = System.Windows.Visibility.Collapsed;
                    return;
                }
                if (this.SceneImage.Visibility == System.Windows.Visibility.Collapsed) {
                    // Uncollapse the image control so its actual size is set, but keep it hidden until the first render
                    this.SceneImage.Visibility = System.Windows.Visibility.Hidden;
                }

                var width = this.ActualWidth;
                var height = this.ActualHeight;
                if (width == 0 || height == 0) {
                    // No rendering yet, until resize
                    return;
                }
                using (var ms = new MemoryStream()) {
                    var backColor = PlotBackgroundColor.ToDrawingColor();
                    using (var driver = new GDIDriver(width: (int)width, height: (int)height, scene: scene, BackColor: backColor)) {
                        driver.Render();

                        this.SceneImage.Source = ImageUtility.ConvertBitmap(driver.BackBuffer.Bitmap);
                        this.SceneImage.Visibility = System.Windows.Visibility.Visible;
                    }
                }
            } catch (Exception e) {
                Logger.Error(e, "Failed updating scene image");
                throw;
            }
        }
    }
}