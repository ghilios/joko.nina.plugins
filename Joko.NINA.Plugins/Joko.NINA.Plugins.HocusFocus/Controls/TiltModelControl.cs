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
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using System;
using System.Linq;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Utility;
using static ILNumerics.ILMath;
using ILNLines = ILNumerics.Drawing.Lines;
using ILNLabel = ILNumerics.Drawing.Label;
using ILNAxis = ILNumerics.Drawing.Plotting.Axis;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using ILNumerics.Drawing.Plotting;
using NINA.Astrometry;
using NINA.Core.Utility;
using System.Drawing;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class TiltModelControl : ILNSceneControlBase {

        public static readonly DependencyProperty TiltPlaneModelProperty = DependencyProperty.Register(
            "TiltPlaneModel",
            typeof(TiltPlaneModel),
            typeof(ILNSceneControlBase),
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
            typeof(ILNSceneControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSurfaceHighExtremeColorPropertyChanged));

        public MediaColor SurfaceHighExtremeColor {
            get { return (MediaColor)GetValue(SurfaceHighExtremeColorProperty); }
            set { SetValue(SurfaceHighExtremeColorProperty, value); }
        }

        private static void OnSurfaceHighExtremeColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (TiltModelControl)d;
            thisControl.OnSurfaceHighExtremeColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateSceneImage();
        }

        public static readonly DependencyProperty SurfaceLowExtremeColorProperty = DependencyProperty.Register(
            "SurfaceLowExtremeColor",
            typeof(MediaColor),
            typeof(ILNSceneControlBase),
            new FrameworkPropertyMetadata(
                default(MediaColor),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSurfaceLowExtremeColorPropertyChanged));

        public MediaColor SurfaceLowExtremeColor {
            get { return (MediaColor)GetValue(SurfaceLowExtremeColorProperty); }
            set { SetValue(SurfaceLowExtremeColorProperty, value); }
        }

        public DrawingColor TopColor => DrawingColor.PaleVioletRed;
        public DrawingColor BottomColor => DrawingColor.LightGreen;
        public bool ColorCodeCorners => true;

        private static void OnSurfaceLowExtremeColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (TiltModelControl)d;
            thisControl.OnSurfaceLowExtremeColorChanged((MediaColor)e.NewValue);
            thisControl.UpdateSceneImage();
        }

        private static void OnAnyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (TiltModelControl)d;
            thisControl.UpdateScene();
        }

        protected override void OnClearScene() {
            base.OnClearScene();

            PlotCube = null;
            PlotPoints = null;
        }

        protected void OnSurfaceHighExtremeColorChanged(MediaColor newColor) {
            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourSurface, closeColor: SurfaceColor.ToDrawingColor(), lowExtremeColor: SurfaceLowExtremeColor.ToDrawingColor(), highExtremeColor: newColor.ToDrawingColor());
            }
        }

        protected void OnSurfaceLowExtremeColorChanged(MediaColor newColor) {
            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourSurface, closeColor: SurfaceColor.ToDrawingColor(), lowExtremeColor: newColor.ToDrawingColor(), highExtremeColor: SurfaceHighExtremeColor.ToDrawingColor());
            }
        }

        private void UpdateColorMap(
            PlotCube contourPlotCube,
            Surface contourSurface,
            DrawingColor closeColor,
            DrawingColor lowExtremeColor,
            DrawingColor highExtremeColor) {
            var colormap = GetSceneColorMap(closeColor: closeColor, lowExtremeColor: lowExtremeColor, highExtremeColor: highExtremeColor);
            contourSurface.Colormap = colormap;
            contourPlotCube.Configure();
            contourSurface.Configure();
        }

        public PlotCube PlotCube { get; protected set; }
        public Points PlotPoints { get; protected set; }
        public Surface ContourSurface { get; protected set; }
        public ILNLabel TopLabel { get; protected set; }
        public ILNLabel BottomLabel { get; protected set; }

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

        protected override void OnPointColorChanged(MediaColor newColor) {
            base.OnPointColorChanged(newColor);

            var plotPoints = PlotPoints;
            if (plotPoints != null) {
                UpdatePlotPointsColor(plotPoints, newColor.ToDrawingColor());
            }
        }

        protected override void OnSurfaceColorChanged(MediaColor newColor) {
            base.OnSurfaceColorChanged(newColor);

            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourSurface, closeColor: newColor.ToDrawingColor(), lowExtremeColor: SurfaceLowExtremeColor.ToDrawingColor(), highExtremeColor: SurfaceHighExtremeColor.ToDrawingColor());
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
            if (ColorCodeCorners) {
                if (TopLabel != null) {
                    TopLabel.Color = TopColor;
                }
                if (BottomLabel != null) {
                    BottomLabel.Color = BottomColor;
                }
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

        private Colormap GetSceneColorMap(DrawingColor closeColor, DrawingColor lowExtremeColor, DrawingColor highExtremeColor) {
            using (Scope.Enter()) {
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
            // Using lambda/3 / 2 as the saturation limit
            return 2.44 * 0.55 * fRatio * fRatio / 2.0;
        }

        protected override Scene GetScene() {
            var tiltPlaneModel = TiltPlaneModel;
            if (tiltPlaneModel == null) {
                return null;
            }

            try {
                var imageSize = tiltPlaneModel.ImageSize;
                using (Scope.Enter()) {
                    var scene = new Scene();

                    var imageXs = new int[] { 0, imageSize.Width - 1, 0, imageSize.Width - 1 };
                    var imageYs = new int[] { 0, 0, imageSize.Height - 1, imageSize.Height - 1 };

                    var modelXs = imageXs.Select(x => tiltPlaneModel.GetModelX(x)).ToArray();
                    var modelYs = imageYs.Select(y => tiltPlaneModel.GetModelY(y)).ToArray();
                    double xyScaleFactor = 1.4d;

                    Array<double> points = zeros<double>(3, 4);
                    Array<double> topPoints = zeros<double>(3, 2);
                    Array<double> bottomPoints = zeros<double>(3, 2);
                    Array<double> sensorPlanePoints = zeros<double>(3, 4);
                    var estimatedFinalFocuserPositions = new double[] { tiltPlaneModel.TopLeft.FocuserPosition, tiltPlaneModel.TopRight.FocuserPosition, tiltPlaneModel.BottomLeft.FocuserPosition, tiltPlaneModel.BottomRight.FocuserPosition };

                    for (int i = 0; i < 4; ++i) {
                        var imageX = imageXs[i];
                        var imageY = imageYs[i];
                        var modelX = modelXs[i];
                        var modelY = modelYs[i];
                        var modeledFocuserPosition = tiltPlaneModel.EstimateFocusPosition(imageX, imageY);
                        var autoFocusEstimatedFocuserPosition = estimatedFinalFocuserPositions[i];
                        if (double.IsNaN(autoFocusEstimatedFocuserPosition)) {
                            Logger.Error("One or more of the corners failed to produce a focus curve. Not producing a tilt model");
                            return null;
                        }

                        points[0, i] = modelX;
                        points[1, i] = modelY;
                        points[2, i] = autoFocusEstimatedFocuserPosition;
                        if (i < 2) {
                            topPoints[0, i] = modelX;
                            topPoints[1, i] = modelY;
                            topPoints[2, i] = autoFocusEstimatedFocuserPosition;
                        } else {
                            bottomPoints[0, i - 2] = modelX;
                            bottomPoints[1, i - 2] = modelY;
                            bottomPoints[2, i - 2] = autoFocusEstimatedFocuserPosition;
                        }

                        sensorPlanePoints[0, i] = modelX * xyScaleFactor;
                        sensorPlanePoints[1, i] = modelY * xyScaleFactor;
                        sensorPlanePoints[2, i] = 0.0d;
                    }
                    for (int i = 0; i < 4; ++i) {
                        points[2, i] = points[2, i] - tiltPlaneModel.MeanFocuserPosition;
                    }
                    for (int i = 0; i < 2; ++i) {
                        topPoints[2, i] = topPoints[2, i] - tiltPlaneModel.MeanFocuserPosition;
                        bottomPoints[2, i] = bottomPoints[2, i] - tiltPlaneModel.MeanFocuserPosition;
                    }

                    const int SurfaceGranularity = 13;
                    Array<double> XMat = ILMath.zeros<double>(SurfaceGranularity, SurfaceGranularity);
                    Array<double> YMat = ILMath.zeros<double>(SurfaceGranularity, SurfaceGranularity);
                    Array<double> ZMat = ILMath.zeros<double>(SurfaceGranularity, SurfaceGranularity);
                    for (int x = 0; x < SurfaceGranularity; ++x) {
                        var imageX = Math.Min(imageSize.Width - 1, (double)imageSize.Width / (SurfaceGranularity - 1) * x);
                        var modelX = tiltPlaneModel.GetModelX((int)imageX);
                        for (int y = 0; y < SurfaceGranularity; ++y) {
                            var imageY = Math.Min(imageSize.Height - 1, (double)imageSize.Height / (SurfaceGranularity - 1) * y);
                            var modelY = tiltPlaneModel.GetModelY((int)imageY);
                            var modeledFocuserPosition = tiltPlaneModel.EstimateFocusPosition((int)imageX, (int)imageY);
                            XMat[$"{x};{y}"] = modelX;
                            YMat[$"{x};{y}"] = modelY;
                            ZMat[$"{x};{y}"] = modeledFocuserPosition - tiltPlaneModel.MeanFocuserPosition;
                        }
                    }

                    Array<double> tiltPlaneSurfaceZXY = ILMath.zeros<double>(SurfaceGranularity, SurfaceGranularity, 3);
                    tiltPlaneSurfaceZXY[":;:;0"] = ZMat;
                    tiltPlaneSurfaceZXY[":;:;1"] = XMat;
                    tiltPlaneSurfaceZXY[":;:;2"] = YMat;

                    var sensorPlaneSurface = new TrianglesStrip();
                    sensorPlaneSurface.Color = SurfaceColor.ToDrawingColor().SetAlpha(35);
                    sensorPlaneSurface.Positions = tosingle(sensorPlanePoints);

                    var plotPoints = new Points {
                        Positions = tosingle(points),
                        Color = PointColor.ToDrawingColor(),
                        Size = 6
                    };
                    var plotTopPoints = new Points {
                        Positions = tosingle(topPoints),
                        Color = TopColor,
                        Size = 6
                    };
                    var plotBottomPoints = new Points {
                        Positions = tosingle(bottomPoints),
                        Color = BottomColor,
                        Size = 6
                    };

                    var colormapLambdaLimit = (float)GetLambdaColorMapLimit(TiltPlaneModel.FRatio);
                    var colormap = GetSceneColorMap(closeColor: SurfaceColor.ToDrawingColor(), lowExtremeColor: SurfaceLowExtremeColor.ToDrawingColor(), highExtremeColor: SurfaceHighExtremeColor.ToDrawingColor());
                    var contourSurface = new Surface(
                        tosingle(tiltPlaneSurfaceZXY),
                        colormap: colormap) {
                        Wireframe = { Visible = false }
                    };

                    contourSurface.DataRange = new Tuple<float, float>(-colormapLambdaLimit, colormapLambdaLimit);
                    var plotCube = new PlotCube(twoDMode: false) {
                        Axes = {
                          XAxis = {
                              Label = {
                                  Text = ""
                              },
                              Ticks = {
                                  Mode = TickMode.Manual
                              }
                          },
                          YAxis = {
                              Label = {
                                  Text = ""
                              },
                              Ticks = {
                                  Mode = TickMode.Manual
                              }
                          },
                          ZAxis = {
                              Label = {
                                 Text = "Focuser Delta"
                              },
                              Ticks = {
                                  Mode = TickMode.Auto
                              }
                          }
                      },
                        Rotation = Matrix4.Rotation(new Vector3(1, 0, 0), AstroUtil.ToRadians(70)) * Matrix4.Rotation(new Vector3(0, 0, 1), AstroUtil.ToRadians(15)),
                        Projection = Projection.Orthographic,
                        Children = { contourSurface, sensorPlaneSurface }
                    };
                    if (ColorCodeCorners) {
                        plotCube.Children.Add(plotTopPoints);
                        plotCube.Children.Add(plotBottomPoints);
                    } else {
                        plotCube.Children.Add(plotPoints);
                    }

                    var topLabel = new ILNLabel("Top");
                    var bottomLabel = new ILNLabel("Bottom");
                    if (ColorCodeCorners) {
                        topLabel.Color = TopColor;
                        bottomLabel.Color = BottomColor;
                    }

                    plotCube.Axes.XAxis.Ticks.Add(-0.5f, "Left");
                    plotCube.Axes.XAxis.Ticks.Add(0.5f, "Right");
                    plotCube.Axes.YAxis.Ticks.Add(0.5f, bottomLabel);
                    plotCube.Axes.YAxis.Ticks.Add(-0.5f, topLabel);

                    var dataScreen = plotCube.DataScreenRect;
                    plotCube.AspectRatioMode = AspectRatioMode.StretchToFill;
                    plotCube.AllowZoom = false;
                    plotCube.AllowPan = false;
                    scene.Screen.Add(new ILNLabel("Telescope") {
                        Font = new Font(System.Drawing.FontFamily.GenericMonospace, 10),
                        Position = new Vector3(0.5f, 0.1f, 0f),
                        Anchor = new PointF(0.5f, 0f)
                    });
                    scene.Screen.Add(new ILNLabel("Sensor") {
                        Font = new Font(System.Drawing.FontFamily.GenericMonospace, 10),
                        Position = new Vector3(0.5f, 0.9f, 0f),
                        Anchor = new PointF(0.5f, 1.0f)
                    });

                    var fRatio = tiltPlaneModel.FRatio;
                    var criticalFocusMicrons = 2.44 * fRatio * fRatio * 0.55;
                    var zMin = (float)Math.Min(plotCube.Limits.ZMin, -criticalFocusMicrons / 2.0);
                    var zMax = (float)Math.Max(plotCube.Limits.ZMax, criticalFocusMicrons / 2.0);
                    plotCube.Limits.ZMin = zMin;
                    plotCube.Limits.ZMax = zMax;

                    scene.Add(plotCube);
                    scene.Screen.First<ILNumerics.Drawing.Label>().Visible = false;
                    this.PlotCube = plotCube;
                    this.ContourSurface = contourSurface;
                    this.PlotPoints = plotPoints;
                    this.TopLabel = topLabel;
                    this.BottomLabel = bottomLabel;

                    UpdateAxisColor(plotCube, AxisColor.ToDrawingColor());
                    UpdateTextColor(plotCube, TextColor.ToDrawingColor());
                    return scene;
                }
            } catch (Exception e) {
                Logger.Error(e, "Failed updating scene");
                throw;
            }
        }
    }
}