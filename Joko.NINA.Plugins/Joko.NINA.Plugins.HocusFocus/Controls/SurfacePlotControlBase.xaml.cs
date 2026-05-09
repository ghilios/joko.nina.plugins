#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using MediaColor = System.Windows.Media.Color;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using WpfPixelFormats = System.Windows.Media.PixelFormats;
using BitmapSource = System.Windows.Media.Imaging.BitmapSource;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public abstract partial class SurfacePlotControlBase : UserControl {

        public static readonly DependencyProperty PlotBackgroundColorProperty = DependencyProperty.Register(
            "PlotBackgroundColor",
            typeof(MediaColor),
            typeof(SurfacePlotControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnPlotBackgroundColorChangedPropertyChanged));

        public static readonly DependencyProperty TextColorProperty = DependencyProperty.Register(
            "TextColor",
            typeof(MediaColor),
            typeof(SurfacePlotControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnTextColorChangedPropertyChanged));

        public static readonly DependencyProperty AxisColorProperty = DependencyProperty.Register(
            "AxisColor",
            typeof(MediaColor),
            typeof(SurfacePlotControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAxisColorChangedPropertyChanged));

        public static readonly DependencyProperty PointColorProperty = DependencyProperty.Register(
            "PointColor",
            typeof(MediaColor),
            typeof(SurfacePlotControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnPointColorChangedPropertyChanged));

        public static readonly DependencyProperty SurfaceColorProperty = DependencyProperty.Register(
            "SurfaceColor",
            typeof(MediaColor),
            typeof(SurfacePlotControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSurfaceColorChangedPropertyChanged));

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

        public SurfacePlotControlBase() {
            InitializeComponent();
            this.SizeChanged += SurfacePlotControlBase_SizeChanged;
        }

        public SurfacePlotModel LocalPlotModel { get; private set; }

        protected void ClearScene() {
            OnClearScene();
        }

        protected virtual void OnClearScene() {
            this.SceneImage.Visibility = Visibility.Collapsed;
            LocalPlotModel = null;
        }

        protected void UpdateScene() {
            var model = GetPlotModel();
            if (model == null) {
                ClearScene();
            } else {
                LocalPlotModel = model;
            }
            UpdateSceneImage();
        }

        protected abstract SurfacePlotModel GetPlotModel();

        protected void UpdateSceneImage() {
            this.OnUpdateSceneImage();
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

        protected virtual void OnUpdateSceneImage() {
            try {
                var model = this.LocalPlotModel;
                if (model == null) {
                    this.SceneImage.Visibility = Visibility.Collapsed;
                    return;
                }
                if (this.SceneImage.Visibility == Visibility.Collapsed) {
                    this.SceneImage.Visibility = Visibility.Hidden;
                }

                var width = this.ActualWidth;
                var height = this.ActualHeight;
                if (width <= 0 || height <= 0) {
                    return;
                }

                using (var bitmap = new SurfacePlotRenderer(model, (int)width, (int)height).Render()) {
                    this.SceneImage.Source = ConvertToBitmapSource(bitmap);
                    this.SceneImage.Visibility = Visibility.Visible;
                }
            } catch (Exception e) {
                Logger.Error(e, "Failed updating surface plot image");
                throw;
            }
        }

        private static BitmapSource ConvertToBitmapSource(Bitmap bitmap) {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try {
                var source = BitmapSource.Create(
                    bitmap.Width,
                    bitmap.Height,
                    96.0d,
                    96.0d,
                    WpfPixelFormats.Pbgra32,
                    null,
                    data.Scan0,
                    Math.Abs(data.Stride) * bitmap.Height,
                    data.Stride);
                source.Freeze();
                return source;
            } finally {
                bitmap.UnlockBits(data);
            }
        }

        private void SurfacePlotControlBase_SizeChanged(object sender, SizeChangedEventArgs e) {
            UpdateSceneImage();
        }

        private static void OnPlotBackgroundColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (SurfacePlotControlBase)d;
            thisControl.OnPlotBackgroundColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateScene();
        }

        private static void OnTextColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (SurfacePlotControlBase)d;
            thisControl.OnTextColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateScene();
        }

        private static void OnPointColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (SurfacePlotControlBase)d;
            thisControl.OnPointColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateScene();
        }

        private static void OnAxisColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (SurfacePlotControlBase)d;
            thisControl.OnAxisColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateScene();
        }

        private static void OnSurfaceColorChangedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (SurfacePlotControlBase)d;
            thisControl.OnSurfaceColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateScene();
        }
    }
}
