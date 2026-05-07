#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Windows;
using DrawingSize = System.Drawing.Size;
using MediaColor = System.Windows.Media.Color;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class SensorModelContourMapControl : SurfacePlotControlBase {

        public static readonly DependencyProperty SurfaceHighExtremeColorProperty = DependencyProperty.Register(
            "SurfaceHighExtremeColor",
            typeof(MediaColor),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public MediaColor SurfaceHighExtremeColor {
            get { return (MediaColor)GetValue(SurfaceHighExtremeColorProperty); }
            set { SetValue(SurfaceHighExtremeColorProperty, value); }
        }

        public static readonly DependencyProperty SurfaceLowExtremeColorProperty = DependencyProperty.Register(
            "SurfaceLowExtremeColor",
            typeof(MediaColor),
            typeof(SensorModelContourMapControl),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public MediaColor SurfaceLowExtremeColor {
            get { return (MediaColor)GetValue(SurfaceLowExtremeColorProperty); }
            set { SetValue(SurfaceLowExtremeColorProperty, value); }
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

        protected override SurfacePlotModel GetPlotModel() {
            var sensorModel = SensorModel;
            if (sensorModel == null || ImageSize.Width <= 0 || ImageSize.Height <= 0 || PixelSize <= 0.0d) {
                return null;
            }

            try {
                const int gridWidth = 450;
                const int gridHeight = 310;
                var imageWidth = ImageSize.Width * PixelSize;
                var imageHeight = ImageSize.Height * PixelSize;
                var imageCenterX = imageWidth / 2.0d;
                var imageCenterY = imageHeight / 2.0d;
                var imageCenterValue = SensorMeanElevation;
                var xs = new double[gridHeight, gridWidth];
                var ys = new double[gridHeight, gridWidth];
                var zs = new double[gridHeight, gridWidth];

                for (int row = 0; row < gridHeight; ++row) {
                    var y = imageHeight * row / (gridHeight - 1);
                    for (int col = 0; col < gridWidth; ++col) {
                        var x = imageWidth * col / (gridWidth - 1);
                        xs[row, col] = x;
                        ys[row, col] = y;
                        zs[row, col] = sensorModel.ValueAt(x - imageCenterX, y - imageCenterY) - imageCenterValue;
                    }
                }

                var colormapLambdaLimit = 3.0 * GetLambdaColorMapLimit(FRatio);
                var hasValidColorRange = double.IsFinite(colormapLambdaLimit) && colormapLambdaLimit > 0.0d;
                var model = new SurfacePlotModel() {
                    X = xs,
                    Y = ys,
                    Z = zs,
                    BackgroundColor = PlotBackgroundColor.ToDrawingColor(),
                    TextColor = TextColor.ToDrawingColor(),
                    AxisColor = AxisColor.ToDrawingColor(),
                    ColorMap = new SurfaceColorMap(
                        (0.00d, System.Drawing.Color.FromArgb(255, 103,   0,  31)),
                        (0.25d, System.Drawing.Color.FromArgb(255, 239, 138,  98)),
                        (0.50d, System.Drawing.Color.FromArgb(255, 255, 255, 255)),
                        (0.75d, System.Drawing.Color.FromArgb(255, 103, 169, 207)),
                        (1.00d, System.Drawing.Color.FromArgb(255,   5,  48,  97))),
                    ColorRangeMin = hasValidColorRange ? -colormapLambdaLimit : (double?)null,
                    ColorRangeMax = hasValidColorRange ? colormapLambdaLimit : (double?)null,
                    Projection = Show3D ? ProjectionType.Oblique : ProjectionType.RotationBased,
                    ObliqueYAngleDegrees = 30.0d,
                    ObliqueYScale = 0.5d,
                    RotationXDegrees = 0.0d,
                    RotationYDegrees = 0.0d,
                    RotationZDegrees = 0.0d,
                    VerticalScale = Show3D ? 0.8d : 0.0d,
                    ShowAxes = Show3D,
                    ZAxisLabel = Show3D ? "Offset (microns)" : null,
                    AutoZTickCount = Show3D ? 4 : 0,
                    AutoZTickFormat = "0.0",
                    ShowColorBar = true
                };

                if (Show3D) {
                    model.XTicks.Add(new PlotTick(0.0d, "Left"));
                    model.XTicks.Add(new PlotTick(imageWidth, "Right"));
                    model.YTicks.Add(new PlotTick(imageHeight, "Bottom"));
                    model.YTicks.Add(new PlotTick(0.0d, "Top"));
                }

                return model;
            } catch (Exception e) {
                Logger.Error(e, "Failed updating sensor model contour map");
                throw;
            }
        }

        private static double GetLambdaColorMapLimit(double fRatio) {
            return 2.44 * 0.55 * fRatio * fRatio * 2.0;
        }
    }
}
