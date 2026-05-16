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
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        // Diagnostic logging throttle: only emit a [SurfacePlotDiag] line when the computed render
        // configuration actually changes, so resize drags don't spam the log.
        private string lastDiagSignature;

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

                var logicalWidth = this.ActualWidth;
                var logicalHeight = this.ActualHeight;
                if (logicalWidth <= 0 || logicalHeight <= 0) {
                    return;
                }

                var dpi = VisualTreeHelper.GetDpi(this);
                var dpiScaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                var dpiScaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
                var pixelWidth = Math.Max(1, (int)Math.Round(logicalWidth * dpiScaleX));
                var pixelHeight = Math.Max(1, (int)Math.Round(logicalHeight * dpiScaleY));

                var logicalMin = Math.Min(logicalWidth, logicalHeight);
                var chartScale = Math.Clamp(logicalMin / 300.0d, 0.6d, 1.0d);
                var pixelScale = (float)(Math.Min(dpiScaleX, dpiScaleY) * chartScale);

                var diagSignature = string.Format(CultureInfo.InvariantCulture,
                    "{0}|{1:0.#}|{2:0.#}|{3:0.###}|{4:0.###}|{5:0.###}|{6:0.###}|{7:0.###}",
                    this.GetType().Name, logicalWidth, logicalHeight, dpiScaleX, dpiScaleY, chartScale, pixelScale, logicalMin);
                var logDiagnostics = diagSignature != lastDiagSignature;
                if (logDiagnostics) {
                    lastDiagSignature = diagSignature;
                    LogDiagnostics(dpi, logicalWidth, logicalHeight, logicalMin, chartScale, pixelScale, pixelWidth, pixelHeight);
                }

                using (var bitmap = new SurfacePlotRenderer(model, pixelWidth, pixelHeight, pixelScale, logDiagnostics).Render()) {
                    this.SceneImage.Source = ConvertToBitmapSource(bitmap, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
                    this.SceneImage.Visibility = Visibility.Visible;
                }
            } catch (Exception e) {
                Logger.Error(e, "Failed updating surface plot image");
                throw;
            }
        }

        private void LogDiagnostics(DpiScale dpi, double logicalWidth, double logicalHeight, double logicalMin,
            double chartScale, float pixelScale, int pixelWidth, int pixelHeight) {
            try {
                var inv = CultureInfo.InvariantCulture;
                var imageRender = this.SceneImage.RenderSize;

                Logger.Info($"[SurfacePlotDiag] control={this.GetType().Name} " +
                    $"actual={logicalWidth.ToString("0.##", inv)}x{logicalHeight.ToString("0.##", inv)} DIP " +
                    $"imageRender={imageRender.Width.ToString("0.##", inv)}x{imageRender.Height.ToString("0.##", inv)} " +
                    $"imageStretch={this.SceneImage.Stretch} " +
                    $"logicalMin={logicalMin.ToString("0.##", inv)} chartScale={chartScale.ToString("0.####", inv)} " +
                    $"pixelScale={pixelScale.ToString("0.####", inv)} pixelSize={pixelWidth}x{pixelHeight}");

                Logger.Info($"[SurfacePlotDiag] control={this.GetType().Name} GetDpi: " +
                    $"DpiScale={dpi.DpiScaleX.ToString("0.####", inv)}x{dpi.DpiScaleY.ToString("0.####", inv)} " +
                    $"PixelsPerInch={dpi.PixelsPerInchX.ToString("0.##", inv)}x{dpi.PixelsPerInchY.ToString("0.##", inv)}");

                // PresentationSource — true device transform WPF applies, and whether the visual is connected.
                var src = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget != null) {
                    var m = src.CompositionTarget.TransformToDevice;
                    Logger.Info($"[SurfacePlotDiag] control={this.GetType().Name} PresentationSource: connected=true " +
                        $"TransformToDevice M11={m.M11.ToString("0.####", inv)} M22={m.M22.ToString("0.####", inv)}");
                } else {
                    Logger.Info($"[SurfacePlotDiag] control={this.GetType().Name} PresentationSource: connected=false");
                }

                // Cumulative transform from this control up to the window root — detects any global
                // ScaleTransform / LayoutTransform / Viewbox applied by the NINA host.
                if (src?.RootVisual is Visual root && !ReferenceEquals(root, this)) {
                    try {
                        var toRoot = this.TransformToAncestor(root);
                        var probe = toRoot.TransformBounds(new Rect(0, 0, 100, 100));
                        Logger.Info($"[SurfacePlotDiag] control={this.GetType().Name} ancestorTransform: " +
                            $"scaleX={(probe.Width / 100.0).ToString("0.####", inv)} scaleY={(probe.Height / 100.0).ToString("0.####", inv)}");
                    } catch (Exception ancestorEx) {
                        Logger.Info($"[SurfacePlotDiag] control={this.GetType().Name} ancestorTransform: unavailable ({ancestorEx.Message})");
                    }
                }

                // Native-text reference: on-screen size of WPF text at nominal em size 12. The surface-plot
                // fonts are tuned to match this; comparing it across machines isolates the discrepancy.
                var ft = new FormattedText("Sample Mg", inv, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 12.0, System.Windows.Media.Brushes.Black, dpi.PixelsPerDip);
                Logger.Info($"[SurfacePlotDiag] control={this.GetType().Name} nativeTextRef: " +
                    $"\"Sample Mg\" em=12 pixelsPerDip={dpi.PixelsPerDip.ToString("0.####", inv)} " +
                    $"size={ft.Width.ToString("0.##", inv)}x{ft.Height.ToString("0.##", inv)} DIP " +
                    $"baseline={ft.Baseline.ToString("0.##", inv)}");
            } catch (Exception e) {
                Logger.Error(e, "[SurfacePlotDiag] Failed logging control diagnostics");
            }
        }

        private static BitmapSource ConvertToBitmapSource(Bitmap bitmap, double dpiX, double dpiY) {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try {
                var source = BitmapSource.Create(
                    bitmap.Width,
                    bitmap.Height,
                    dpiX > 0 ? dpiX : 96.0d,
                    dpiY > 0 ? dpiY : 96.0d,
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

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) {
            base.OnDpiChanged(oldDpi, newDpi);
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
