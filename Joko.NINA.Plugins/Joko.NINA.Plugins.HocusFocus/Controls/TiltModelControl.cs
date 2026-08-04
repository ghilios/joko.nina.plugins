#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Linq;
using System.Windows;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class TiltModelControl : SurfacePlotControlBase {

        public static readonly DependencyProperty TiltPlaneModelProperty = DependencyProperty.Register(
            "TiltPlaneModel",
            typeof(TiltPlaneModel),
            typeof(TiltModelControl),
            new FrameworkPropertyMetadata(
                default(TiltPlaneModel),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public TiltPlaneModel TiltPlaneModel {
            get { return (TiltPlaneModel)GetValue(TiltPlaneModelProperty); }
            set { SetValue(TiltPlaneModelProperty, value); }
        }

        public static readonly DependencyProperty SurfaceHighExtremeColorProperty = DependencyProperty.Register(
            "SurfaceHighExtremeColor",
            typeof(MediaColor),
            typeof(TiltModelControl),
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
            typeof(TiltModelControl),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public MediaColor SurfaceLowExtremeColor {
            get { return (MediaColor)GetValue(SurfaceLowExtremeColorProperty); }
            set { SetValue(SurfaceLowExtremeColorProperty, value); }
        }

        /// <summary>
        /// The display-only focuser-direction setting k, bound from IInspectorOptions. The plot's z axis is a
        /// focuser delta, so which end of it faces the telescope depends on the focuser convention: a higher
        /// best-focus position is closer to the objective only when increasing focuser position moves the
        /// camera away from it. False (default) = standard, which renders exactly as before this property
        /// existed. Labels only — no plotted value reads it
        /// (docs/focuser-direction-convention-design.md §3, site 3).
        /// </summary>
        public static readonly DependencyProperty FocuserIncreasesTowardObjectiveProperty = DependencyProperty.Register(
            "FocuserIncreasesTowardObjective",
            typeof(bool),
            typeof(TiltModelControl),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

        public bool FocuserIncreasesTowardObjective {
            get { return (bool)GetValue(FocuserIncreasesTowardObjectiveProperty); }
            set { SetValue(FocuserIncreasesTowardObjectiveProperty, value); }
        }

        public DrawingColor TopColor => DrawingColor.PaleVioletRed;
        public DrawingColor BottomColor => DrawingColor.LightGreen;
        public bool ColorCodeCorners => true;

        private static void OnAnyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (TiltModelControl)d;
            thisControl.UpdateScene();
        }

        protected override SurfacePlotModel GetPlotModel() {
            var tiltPlaneModel = TiltPlaneModel;
            if (tiltPlaneModel == null) {
                return null;
            }

            try {
                var imageSize = tiltPlaneModel.ImageSize;
                const int surfaceGranularity = 130;
                var xs = new double[surfaceGranularity, surfaceGranularity];
                var ys = new double[surfaceGranularity, surfaceGranularity];
                var zs = new double[surfaceGranularity, surfaceGranularity];
                for (int xIndex = 0; xIndex < surfaceGranularity; ++xIndex) {
                    var imageX = Math.Min(imageSize.Width - 1, (double)imageSize.Width / (surfaceGranularity - 1) * xIndex);
                    var modelX = tiltPlaneModel.GetModelX((int)imageX);
                    for (int yIndex = 0; yIndex < surfaceGranularity; ++yIndex) {
                        var imageY = Math.Min(imageSize.Height - 1, (double)imageSize.Height / (surfaceGranularity - 1) * yIndex);
                        var modelY = tiltPlaneModel.GetModelY((int)imageY);
                        xs[yIndex, xIndex] = modelX;
                        ys[yIndex, xIndex] = modelY;
                        zs[yIndex, xIndex] = tiltPlaneModel.EstimateFocusPosition((int)imageX, (int)imageY) - tiltPlaneModel.MeanFocuserPosition;
                    }
                }

                var surfaceColor = SurfaceColor.ToDrawingColor();
                var model = new SurfacePlotModel() {
                    X = xs,
                    Y = ys,
                    Z = zs,
                    BackgroundColor = PlotBackgroundColor.ToDrawingColor(),
                    TextColor = TextColor.ToDrawingColor(),
                    AxisColor = AxisColor.ToDrawingColor(),
                    ColorMap = new SurfaceColorMap(
                        (0.0d, SurfaceLowExtremeColor.ToDrawingColor()),
                        (0.5d, surfaceColor),
                        (1.0d, SurfaceHighExtremeColor.ToDrawingColor())),
                    Projection = ProjectionType.Oblique,
                    ObliqueYAngleDegrees = 30.0d,
                    ObliqueYScale = 0.5d,
                    ZAxisLabel = "Focuser Delta",
                    ReferencePlaneZ = 0.0d,
                    ReferencePlaneScale = 1.1d,
                    ReferencePlaneColor = DrawingColor.FromArgb(35, surfaceColor.R, surfaceColor.G, surfaceColor.B)
                };

                var colormapLimit = 3.0 * GetLambdaColorMapLimit(tiltPlaneModel.FRatio, tiltPlaneModel.FocuserStepSizeMicrons);
                if (double.IsFinite(colormapLimit) && colormapLimit > 0.0d) {
                    model.ColorRangeMin = -colormapLimit;
                    model.ColorRangeMax = colormapLimit;
                }

                AddCornerPoints(model, tiltPlaneModel);
                model.XTicks.Add(new PlotTick(-0.5d, "Left"));
                model.XTicks.Add(new PlotTick(0.5d, "Right"));
                model.YTicks.Add(new PlotTick(0.5d, "Bottom", BottomColor));
                model.YTicks.Add(new PlotTick(-0.5d, "Top", TopColor));
                model.ZTicks.Add(new PlotTick(0.0d, "0"));
                var actualZMin = zs.Cast<double>().Min();
                var actualZMax = zs.Cast<double>().Max();
                if (Math.Abs(actualZMin) > 0.5d) {
                    model.ZTicks.Add(new PlotTick(actualZMin, actualZMin.ToString("0")));
                }
                if (Math.Abs(actualZMax) > 0.5d) {
                    model.ZTicks.Add(new PlotTick(actualZMax, actualZMax.ToString("0")));
                }
                // Top of the plot is the higher focuser delta. On a standard focuser that is nearer the
                // telescope; on a reversed one the two labels swap. Nothing plotted above changes.
                var topLabel = FocuserIncreasesTowardObjective ? "Sensor" : "Telescope";
                var bottomLabel = FocuserIncreasesTowardObjective ? "Telescope" : "Sensor";
                model.ScreenLabels.Add(new ScreenLabel(topLabel, 0.5f, 0.05f, TextColor.ToDrawingColor()));
                model.ScreenLabels.Add(new ScreenLabel(bottomLabel, 0.5f, 0.95f, TextColor.ToDrawingColor()));
                return model;
            } catch (Exception e) {
                Logger.Error(e, "Failed updating tilt model visualization");
                throw;
            }
        }

        private void AddCornerPoints(SurfacePlotModel model, TiltPlaneModel tiltPlaneModel) {
            var imageSize = tiltPlaneModel.ImageSize;
            var cornerXs = new[] { 0, imageSize.Width - 1, 0, imageSize.Width - 1 };
            var cornerYs = new[] { 0, 0, imageSize.Height - 1, imageSize.Height - 1 };
            var focuserPositions = new[] {
                tiltPlaneModel.TopLeft.FocuserPosition,
                tiltPlaneModel.TopRight.FocuserPosition,
                tiltPlaneModel.BottomLeft.FocuserPosition,
                tiltPlaneModel.BottomRight.FocuserPosition
            };

            for (int i = 0; i < cornerXs.Length; ++i) {
                var focuserPosition = focuserPositions[i];
                if (double.IsNaN(focuserPosition)) {
                    Logger.Error("One or more of the corners failed to produce a focus curve. Not producing a tilt model");
                    model.Points.Clear();
                    return;
                }

                var pointColor = i < 2 ? TopColor : BottomColor;
                model.Points.Add(new PlotPoint3D(
                    tiltPlaneModel.GetModelX(cornerXs[i]),
                    tiltPlaneModel.GetModelY(cornerYs[i]),
                    focuserPosition - tiltPlaneModel.MeanFocuserPosition,
                    pointColor,
                    size: 6.0f));
            }
        }

        private static double GetLambdaColorMapLimit(double fRatio, double focuserStepSizeMicrons) {
            var criticalFocusMicrons = 2.44 * 0.55 * fRatio * fRatio / 2.0d;
            return criticalFocusMicrons / focuserStepSizeMicrons;
        }
    }
}
