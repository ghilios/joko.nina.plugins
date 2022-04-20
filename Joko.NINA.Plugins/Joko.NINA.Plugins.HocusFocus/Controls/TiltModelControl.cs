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
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using ILNumerics.Drawing.Plotting;
using NINA.Astrometry;
using NINA.Core.Utility;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class TiltModelControl : ILNSceneControlBase {

        public static readonly DependencyProperty TiltPlaneModelProperty = DependencyProperty.Register(
            "TiltPlaneModel",
            typeof(TiltPlaneModel),
            typeof(ILNSceneControlBase),
            new FrameworkPropertyMetadata(
                default(TiltPlaneModel),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnTiltPlaneModelPropertyChanged));

        public TiltPlaneModel TiltPlaneModel {
            get { return (TiltPlaneModel)GetValue(TiltPlaneModelProperty); }
            set { SetValue(TiltPlaneModelProperty, value); }
        }

        private static void OnTiltPlaneModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (TiltModelControl)d;
            thisControl.UpdateScene();
        }

        protected override void OnClearScene() {
            base.OnClearScene();

            PlotCube = null;
            PlaneSurface = null;
            PlotPoints = null;
        }

        public PlotCube PlotCube { get; protected set; }
        public TrianglesStrip PlaneSurface { get; protected set; }
        public Points PlotPoints { get; protected set; }

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

            var planeSurface = PlaneSurface;
            if (planeSurface != null) {
                UpdateSurfaceColor(planeSurface, newColor.ToDrawingColor());
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

                    Array<double> points = zeros<double>(3, 4);
                    Array<double> surfacePoints = zeros<double>(3, 4);
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

                        surfacePoints[0, i] = modelX;
                        surfacePoints[1, i] = modelY;
                        surfacePoints[2, i] = modeledFocuserPosition;
                    }
                    for (int i = 0; i < 4; ++i) {
                        points[2, i] = tiltPlaneModel.MeanFocuserPosition - points[2, i];
                        surfacePoints[2, i] = tiltPlaneModel.MeanFocuserPosition - surfacePoints[2, i];
                    }

                    var triStr = new TrianglesStrip();
                    triStr.Color = SurfaceColor.ToDrawingColor();
                    triStr.Positions = tosingle(surfacePoints);
                    var plotPoints = new Points {
                        Positions = tosingle(points),
                        Color = PointColor.ToDrawingColor()
                    };

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
                        Rotation = Matrix4.Rotation(new Vector3(1, 0, 0), AstroUtil.ToRadians(75)) * Matrix4.Rotation(new Vector3(0, 0, 1), AstroUtil.ToRadians(15)),
                        Projection = Projection.Orthographic,
                        Children = { plotPoints, triStr }
                    };
                    plotCube.Axes.XAxis.Ticks.Add(-0.5f, "Left");
                    plotCube.Axes.XAxis.Ticks.Add(0.5f, "Right");
                    plotCube.Axes.YAxis.Ticks.Add(0.5f, "Bottom");
                    plotCube.Axes.YAxis.Ticks.Add(-0.5f, "Top");

                    var dataScreen = plotCube.DataScreenRect;
                    plotCube.AspectRatioMode = AspectRatioMode.StretchToFill;
                    plotCube.AllowZoom = false;
                    plotCube.AllowPan = false;

                    UpdateAxisColor(plotCube, AxisColor.ToDrawingColor());
                    UpdateTextColor(plotCube, TextColor.ToDrawingColor());

                    scene.Add(plotCube);
                    scene.Screen.First<ILNumerics.Drawing.Label>().Visible = false;
                    this.PlotCube = plotCube;
                    this.PlaneSurface = triStr;
                    this.PlotPoints = plotPoints;
                    return scene;
                }
            } catch (Exception e) {
                Logger.Error(e, "Failed updating scene");
                throw;
            }
        }
    }
}