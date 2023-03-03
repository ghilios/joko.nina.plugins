#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILNumerics;
using ILNumerics.Drawing;
using ILNumerics.Drawing.Plotting;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Drawing;
using System.Windows;
using DrawingSize = System.Drawing.Size;
using DrawingColor = System.Drawing.Color;
using ILNLabel = ILNumerics.Drawing.Label;
using ILNLines = ILNumerics.Drawing.Lines;
using ILNAxis = ILNumerics.Drawing.Plotting.Axis;
using MediaColor = System.Windows.Media.Color;
using NINA.Astrometry;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class SensorModelContourMapControl : ILNSceneControlBase {

        public static readonly DependencyProperty SurfaceHighExtremeColorProperty = DependencyProperty.Register(
            "SurfaceHighExtremeColor",
            typeof(MediaColor),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSurfaceHighExtremeColorPropertyChanged));

        public MediaColor SurfaceHighExtremeColor {
            get { return (MediaColor)GetValue(SurfaceHighExtremeColorProperty); }
            set { SetValue(SurfaceHighExtremeColorProperty, value); }
        }

        private static void OnSurfaceHighExtremeColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (SensorModelContourMapControl)d;
            thisControl.OnSurfaceHighExtremeColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateSceneImage();
        }

        public static readonly DependencyProperty SurfaceLowExtremeColorProperty = DependencyProperty.Register(
            "SurfaceLowExtremeColor",
            typeof(MediaColor),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSurfaceLowExtremeColorPropertyChanged));

        public MediaColor SurfaceLowExtremeColor {
            get { return (MediaColor)GetValue(SurfaceLowExtremeColorProperty); }
            set { SetValue(SurfaceLowExtremeColorProperty, value); }
        }

        private static void OnSurfaceLowExtremeColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (SensorModelContourMapControl)d;
            thisControl.OnSurfaceLowExtremeColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateSceneImage();
        }

        public static readonly DependencyProperty SensorModelProperty = DependencyProperty.Register(
            "SensorModel",
            typeof(SensorParaboloidModel),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(SensorParaboloidModel),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public SensorParaboloidModel SensorModel {
            get { return (SensorParaboloidModel)GetValue(SensorModelProperty); }
            set { SetValue(SensorModelProperty, value); }
        }

        public static readonly DependencyProperty FRatioProperty = DependencyProperty.Register(
            "FRatio",
            typeof(double),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(double),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public double FRatio {
            get { return (double)GetValue(FRatioProperty); }
            set { SetValue(FRatioProperty, value); }
        }

        public static readonly DependencyProperty Show3DProperty = DependencyProperty.Register(
            "Show3D",
            typeof(bool),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                true,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public bool Show3D {
            get { return (bool)GetValue(Show3DProperty); }
            set { SetValue(Show3DProperty, value); }
        }

        public static readonly DependencyProperty ImageSizeProperty = DependencyProperty.Register(
            "ImageSize",
            typeof(DrawingSize),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(DrawingSize),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public DrawingSize ImageSize {
            get { return (DrawingSize)GetValue(ImageSizeProperty); }
            set { SetValue(ImageSizeProperty, value); }
        }

        public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(
            "PixelSize",
            typeof(double),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(double),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public double PixelSize {
            get { return (double)GetValue(PixelSizeProperty); }
            set { SetValue(PixelSizeProperty, value); }
        }

        public static readonly DependencyProperty SensorMeanElevationProperty = DependencyProperty.Register(
            "SensorMeanElevation",
            typeof(double),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(double),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public double SensorMeanElevation {
            get { return (double)GetValue(SensorMeanElevationProperty); }
            set { SetValue(SensorMeanElevationProperty, value); }
        }

        private static void OnAnyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (SensorModelContourMapControl)d;
            thisControl.UpdateScene();
        }

        protected override void OnClearScene() {
            base.OnClearScene();

            PlotCube = null;
            ContourSurface = null;
        }

        public PlotCube PlotCube { get; protected set; }
        public Surface ContourSurface { get; protected set; }

        protected override void OnTextColorChanged(MediaColor newColor) {
            base.OnTextColorChanged(newColor);

            var plotCube = PlotCube;
            if (plotCube != null) {
                UpdateTextColor(PlotCube, newColor.ToDrawingColor());
            }
        }

        protected override void OnAxisColorChanged(MediaColor newColor) {
            base.OnAxisColorChanged(newColor);

            var plotCube = PlotCube;
            if (plotCube != null) {
                UpdateAxisColor(PlotCube, newColor.ToDrawingColor());
            }
        }

        protected void OnSurfaceHighExtremeColorChanged(MediaColor newColor) {
            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourSurface, FRatio, closeColor: SurfaceColor.ToDrawingColor(), lowExtremeColor: SurfaceLowExtremeColor.ToDrawingColor(), highExtremeColor: newColor.ToDrawingColor());
            }
        }

        protected void OnSurfaceLowExtremeColorChanged(MediaColor newColor) {
            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourSurface, FRatio, closeColor: SurfaceColor.ToDrawingColor(), lowExtremeColor: newColor.ToDrawingColor(), highExtremeColor: SurfaceHighExtremeColor.ToDrawingColor());
            }
        }

        protected override void OnSurfaceColorChanged(MediaColor newColor) {
            base.OnSurfaceColorChanged(newColor);

            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourSurface, FRatio, closeColor: newColor.ToDrawingColor(), lowExtremeColor: SurfaceLowExtremeColor.ToDrawingColor(), highExtremeColor: SurfaceHighExtremeColor.ToDrawingColor());
            }
        }

        private void UpdateTextColor(PlotCube plotCube, DrawingColor textColor) {
            foreach (var label in plotCube.Find<ILNLabel>()) {
                label.Color = textColor;
                label.Fringe.Width = 0;
            }
            foreach (var axis in plotCube.Find<ILNAxis>()) {
                axis.Ticks.DefaultLabel.Color = textColor;
                axis.Ticks.DefaultLabel.Fringe.Width = 0;
            }
            plotCube.Configure();
        }

        private void UpdateAxisColor(PlotCube plotCube, DrawingColor lineColor) {
            foreach (var lines in plotCube.Find<ILNLines>()) {
                lines.Color = lineColor;
            }
            plotCube.Configure();
        }

        private void UpdatePlotPointsColor(Points plotPoints, DrawingColor pointColor) {
            plotPoints.Color = pointColor;
            plotPoints.Configure();
        }

        private void UpdateSurfaceColor(TrianglesStrip surfaceStrip, DrawingColor surfaceColor) {
            surfaceStrip.Color = surfaceColor;
            surfaceStrip.Configure();
        }

        private Colormap GetSceneColorMap(double fRatio, DrawingColor closeColor, DrawingColor lowExtremeColor, DrawingColor highExtremeColor) {
            using (Scope.Enter()) {
                var lambdaLimitMicrons = GetLambdaColorMapLimit(fRatio);

                Array<float> colormapData = new float[3, 5] {
                    { 0.0f, (float)lowExtremeColor.R / byte.MaxValue, (float)lowExtremeColor.G / byte.MaxValue, (float)lowExtremeColor.B / byte.MaxValue, (float)lowExtremeColor.A / byte.MaxValue },
                    { 0.5f, (float)closeColor.R / byte.MaxValue, (float)closeColor.G / byte.MaxValue, (float)closeColor.B / byte.MaxValue, (float)closeColor.A / byte.MaxValue },
                    { 1.0f, (float)highExtremeColor.R / byte.MaxValue, (float)highExtremeColor.G / byte.MaxValue, (float)highExtremeColor.B / byte.MaxValue, (float)highExtremeColor.A / byte.MaxValue }
                };
                return new Colormap(colormapData);
            }
        }

        private double GetLambdaColorMapLimit(double fRatio) {
            // https://www.innovationsforesight.com/education/how-much-focus-error-is-too-much/
            // Using lambda/3 * 2 as the saturation limit
            return 2.44 * 0.55 * fRatio * fRatio * 2.0;
        }

        private void UpdateColorMap(
            PlotCube contourPlotCube,
            Surface contourSurface,
            double fRatio,
            DrawingColor closeColor,
            DrawingColor lowExtremeColor,
            DrawingColor highExtremeColor) {
            var colormap = GetSceneColorMap(fRatio, closeColor: closeColor, lowExtremeColor: lowExtremeColor, highExtremeColor: highExtremeColor);
            contourSurface.Colormap = colormap;
            contourPlotCube.Configure();
            contourSurface.Configure();
        }

        protected override Scene GetScene() {
            var sensorModel = SensorModel;
            if (sensorModel == null || !RenderingEnabled) {
                return null;
            }

            try {
                using (Scope.Enter()) {
                    var scene = new Scene();
                    var imageSize = ImageSize;
                    var pixelSize = PixelSize;
                    var fRatio = FRatio;
                    var imageWidth = (float)(imageSize.Width * pixelSize);
                    var imageCenterX = imageWidth / 2.0f;
                    var imageHeight = (float)(imageSize.Height * pixelSize);
                    var imageCenterY = imageHeight / 2.0f;
                    var imageCenterValue = (float)SensorMeanElevation;

                    var colormapLambdaLimit = (float)GetLambdaColorMapLimit(fRatio);
                    var colormap = GetSceneColorMap(fRatio, closeColor: SurfaceColor.ToDrawingColor(), lowExtremeColor: SurfaceLowExtremeColor.ToDrawingColor(), highExtremeColor: SurfaceHighExtremeColor.ToDrawingColor());
                    var contourSurface = new Surface(
                        (x, y) => (float)sensorModel.ValueAt(x - imageCenterX, y - imageCenterY) - imageCenterValue,
                        xmin: 0.0f,
                        xmax: imageWidth,
                        ymin: 0.0f,
                        ymax: imageHeight,
                        colormap: colormap) {
                        Wireframe = { Visible = false },
                        Children = {
                            new Colorbar() {
                                Anchor = new PointF(1.0f, 1.0f),
                                Location = new PointF(0.99f, 0.99f),
                                Background = {
                                    Color = PlotBackgroundColor.ToDrawingColor()
                                }
                            }
                        }
                    };
                    contourSurface.DataRange = new Tuple<float, float>(-colormapLambdaLimit, colormapLambdaLimit);
                    var contourPlotCube = new PlotCube(twoDMode: false) {
                        Axes = {
                            XAxis = {
                                Label = {
                                    Text = "X",
                                    Visible = false
                                },
                                Ticks = {
                                    Mode = TickMode.Manual
                                }
                            },
                            YAxis = {
                                Label = {
                                    Text = "Y",
                                    Visible = false
                                },
                                Ticks = {
                                    Mode = TickMode.Manual
                                }
                            },
                            ZAxis = {
                                Label = {
                                    Text = "Offset (microns)"
                                },
                                Ticks = {
                                    Mode = TickMode.Auto
                                }
                            }
                        },
                        Projection = Projection.Orthographic,
                        Children = {
                            contourSurface
                        }
                    };
                    if (Show3D) {
                        contourPlotCube.Axes.XAxis.Ticks.Add(0f, "Left");
                        contourPlotCube.Axes.XAxis.Ticks.Add(imageWidth, "Right");
                        contourPlotCube.Axes.YAxis.Ticks.Add(imageHeight, "Bottom");
                        contourPlotCube.Axes.YAxis.Ticks.Add(0f, "Top");
                    }

                    contourPlotCube.AspectRatioMode = AspectRatioMode.MaintainRatios;
                    contourPlotCube.AllowZoom = false;
                    contourPlotCube.AllowPan = false;
                    contourPlotCube.DataScreenRect = new RectangleF(0.1f, 0.1f, 0.9f, 0.9f);
                    contourPlotCube = scene.Add(contourPlotCube);
                    scene.Screen.First<Label>().Visible = false;

                    UpdateAxisColor(contourPlotCube, AxisColor.ToDrawingColor());
                    UpdateTextColor(contourPlotCube, TextColor.ToDrawingColor());

                    contourPlotCube.Configure();
                    contourPlotCube.Reset();
                    if (Show3D) {
                        scene.First<PlotCube>().Rotation = Matrix4.Rotation(new Vector3(1, 0, 0), AstroUtil.ToRadians(45)) * Matrix4.Rotation(new Vector3(0, 0, 1), AstroUtil.ToRadians(25));
                    }

                    this.PlotCube = contourPlotCube;
                    this.ContourSurface = contourSurface;
                    return scene;
                }
            } catch (Exception e) {
                Logger.Error(e, "Failed updating scene");
                throw;
            }
        }
    }
}