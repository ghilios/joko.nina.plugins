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
using System.Linq;
using System.Windows;
using static ILNumerics.ILMath;
using DrawingSize = System.Drawing.Size;
using DrawingColor = System.Drawing.Color;
using ILNLabel = ILNumerics.Drawing.Label;
using ILNLines = ILNumerics.Drawing.Lines;
using MediaColor = System.Windows.Media.Color;
using NINA.Astrometry;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class SensorModelContourMapControl : ILNSceneControlBase {

        public static readonly DependencyProperty SurfaceExtremeColorProperty = DependencyProperty.Register(
            "SurfaceExtremeColor",
            typeof(MediaColor),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSurfaceExtremeColorPropertyChanged));

        public MediaColor SurfaceExtremeColor {
            get { return (MediaColor)GetValue(SurfaceExtremeColorProperty); }
            set { SetValue(SurfaceExtremeColorProperty, value); }
        }

        private static void OnSurfaceExtremeColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (SensorModelContourMapControl)d;
            thisControl.OnSurfaceExtremeColorChanged((MediaColor)e.NewValue);
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

        protected void OnSurfaceExtremeColorChanged(MediaColor newColor) {
            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourSurface, FRatio, closeColor: SurfaceColor.ToDrawingColor(), extremeColor: newColor.ToDrawingColor());
            }
        }

        protected override void OnSurfaceColorChanged(MediaColor newColor) {
            base.OnSurfaceColorChanged(newColor);

            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourSurface, FRatio, closeColor: newColor.ToDrawingColor(), extremeColor: SurfaceExtremeColor.ToDrawingColor());
            }
        }

        private void UpdateTextColor(PlotCube plotCube, DrawingColor textColor) {
            foreach (var label in plotCube.Find<ILNLabel>()) {
                label.Color = textColor;
                label.Fringe.Width = 0;
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

        private Colormap GetSceneColorMap(double fRatio, DrawingColor closeColor, DrawingColor extremeColor) {
            using (Scope.Enter()) {
                var lambdaLimitMicrons = GetLambdaColorMapLimit(fRatio);

                Array<float> colormapData = new float[3, 5] {
                    { 0.0f, (float)extremeColor.R / byte.MaxValue, (float)extremeColor.G / byte.MaxValue, (float)extremeColor.B / byte.MaxValue, (float)extremeColor.A / byte.MaxValue },
                    { 0.5f, (float)closeColor.R / byte.MaxValue, (float)closeColor.G / byte.MaxValue, (float)closeColor.B / byte.MaxValue, (float)closeColor.A / byte.MaxValue },
                    { 1.0f, (float)extremeColor.R / byte.MaxValue, (float)extremeColor.G / byte.MaxValue, (float)extremeColor.B / byte.MaxValue, (float)extremeColor.A / byte.MaxValue }
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
            DrawingColor extremeColor) {
            var colormap = GetSceneColorMap(fRatio, closeColor: closeColor, extremeColor: extremeColor);
            contourSurface.Colormap = colormap;
            contourPlotCube.Configure();
            contourSurface.Configure();
        }

        protected override Scene GetScene() {
            var sensorModel = SensorModel;
            if (sensorModel == null) {
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
                    var colormap = GetSceneColorMap(fRatio, closeColor: SurfaceColor.ToDrawingColor(), extremeColor: SurfaceExtremeColor.ToDrawingColor());
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
                                Anchor = new PointF(1.0f, 0.5f),
                                Location = new PointF(0.9f, 0.5f)
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
                                    Text = "Focuser Offset"
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
                    contourPlotCube.Axes.XAxis.Ticks.Add(0f, "Left");
                    contourPlotCube.Axes.XAxis.Ticks.Add(imageWidth, "Right");
                    contourPlotCube.Axes.YAxis.Ticks.Add(imageHeight, "Bottom");
                    contourPlotCube.Axes.YAxis.Ticks.Add(0f, "Top");

                    contourPlotCube.AspectRatioMode = AspectRatioMode.MaintainRatios;
                    contourPlotCube.AllowZoom = false;
                    contourPlotCube.AllowPan = false;
                    contourPlotCube = scene.Add(contourPlotCube);
                    scene.Screen.First<Label>().Visible = false;

                    UpdateAxisColor(contourPlotCube, AxisColor.ToDrawingColor());
                    UpdateTextColor(contourPlotCube, TextColor.ToDrawingColor());

                    contourPlotCube.Configure();
                    contourPlotCube.Reset();
                    scene.First<PlotCube>().Rotation = Matrix4.Rotation(new Vector3(1, 0, 0), AstroUtil.ToRadians(45)) * Matrix4.Rotation(new Vector3(0, 0, 1), AstroUtil.ToRadians(25));
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