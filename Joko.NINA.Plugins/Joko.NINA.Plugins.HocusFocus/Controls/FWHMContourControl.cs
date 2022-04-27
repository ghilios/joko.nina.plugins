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
using System.Drawing;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using System.Collections.Generic;
using ILNumerics.Toolboxes;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class FWHMContourControl : ILNSceneControlBase {

        public static readonly DependencyProperty StarDetectionResultProperty = DependencyProperty.Register(
            "StarDetectionResult",
            typeof(HocusFocusStarDetectionResult),
            typeof(FWHMContourControl),
            new FrameworkPropertyMetadata(
                default(HocusFocusStarDetectionResult),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnStarDetectionResultPropertyChanged));

        public static readonly DependencyProperty NumRegionsWideProperty = DependencyProperty.Register(
            "NumRegionsWide",
            typeof(int),
            typeof(FWHMContourControl),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.None));

        public HocusFocusStarDetectionResult StarDetectionResult {
            get { return (HocusFocusStarDetectionResult)GetValue(StarDetectionResultProperty); }
            set { SetValue(StarDetectionResultProperty, value); }
        }

        public int NumRegionsWide {
            get { return (int)GetValue(NumRegionsWideProperty); }
            set { SetValue(NumRegionsWideProperty, value); }
        }

        private static void OnStarDetectionResultPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (FWHMContourControl)d;
            thisControl.UpdateScene();
        }

        private Colormap GetSceneColorMap(DrawingColor pointColor, DrawingColor surfaceColor) {
            using (Scope.Enter()) {
                var startColor = surfaceColor;
                var endColor = pointColor;
                Array<float> colormapData = new float[2, 5] {
                    { 0.0f, (float)startColor.R / byte.MaxValue, (float)startColor.G / byte.MaxValue, (float)startColor.B / byte.MaxValue, (float)startColor.A / byte.MaxValue },
                    { 1.0f, (float)endColor.R / byte.MaxValue, (float)endColor.G / byte.MaxValue, (float)endColor.B / byte.MaxValue, (float)endColor.A / byte.MaxValue }
                };
                return new Colormap(colormapData);
            }
        }

        protected override void OnTextColorChanged(MediaColor newColor) {
            base.OnTextColorChanged(newColor);

            var plotCube = PlotCube;
            if (plotCube != null) {
                UpdateTextColor(PlotCube, ContourPlot, newColor.ToDrawingColor());
            }
        }

        protected override void OnAxisColorChanged(MediaColor newColor) {
            base.OnAxisColorChanged(newColor);

            var plotCube = PlotCube;
            if (plotCube != null) {
                UpdateAxisColor(plotCube, newColor.ToDrawingColor());
            }
        }

        protected override void OnPlotBackgroundColorChanged(MediaColor newColor) {
            base.OnPlotBackgroundColorChanged(newColor);

            var contourPlot = ContourPlot;
            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlot != null && contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourPlot, contourSurface, pointColor: PointColor.ToDrawingColor(), surfaceColor: SurfaceColor.ToDrawingColor());
            }
        }

        protected override void OnSurfaceColorChanged(MediaColor newColor) {
            base.OnSurfaceColorChanged(newColor);

            var contourPlot = ContourPlot;
            var contourPlotCube = PlotCube;
            var contourSurface = ContourSurface;
            if (contourPlot != null && contourPlotCube != null && contourSurface != null) {
                UpdateColorMap(contourPlotCube, contourPlot, contourSurface, pointColor: PointColor.ToDrawingColor(), surfaceColor: SurfaceColor.ToDrawingColor());
            }
        }

        private void UpdateColorMap(
            PlotCube contourPlotCube,
            ContourPlot countourPlot,
            Surface contourSurface,
            DrawingColor pointColor,
            DrawingColor surfaceColor) {
            var colormap = GetSceneColorMap(pointColor: pointColor, surfaceColor: surfaceColor);
            countourPlot.Colormap = colormap;
            contourSurface.Colormap = colormap;
            contourPlotCube.Configure();
            contourSurface.Configure();
        }

        private void UpdateTextColor(PlotCube plotCube, ContourPlot contourPlot, DrawingColor textColor) {
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
                if (object.ReferenceEquals(lines.Parent, plotCube)) {
                    lines.Color = lineColor;
                }
            }
            plotCube.Configure();
        }

        public PlotCube PlotCube { get; protected set; }
        public Surface ContourSurface { get; protected set; }
        public ContourPlot ContourPlot { get; protected set; }

        protected override Scene GetScene() {
            var starDetectionResult = StarDetectionResult;
            var numRegionsWide = NumRegionsWide;
            if (starDetectionResult == null || numRegionsWide <= 0) {
                return null;
            }

            try {
                var imageSize = starDetectionResult.ImageSize;
                int regionSizePixels = imageSize.Width / numRegionsWide;
                int numRegionsTall = imageSize.Height / regionSizePixels;
                numRegionsTall += numRegionsTall % 2 == 0 ? 1 : 0;
                var regionDetectedStars = new List<HocusFocusDetectedStar>[numRegionsWide, numRegionsTall];
                for (int i = 0; i < numRegionsWide; ++i) {
                    for (int j = 0; j < numRegionsTall; ++j) {
                        regionDetectedStars[i, j] = new List<HocusFocusDetectedStar>();
                    }
                }

                foreach (var detectedStar in starDetectionResult.StarList.Cast<HocusFocusDetectedStar>()) {
                    if (detectedStar.PSF == null) {
                        continue;
                    }

                    var regionRow = (int)Math.Floor(detectedStar.Position.Y / imageSize.Height * numRegionsTall);
                    var regionCol = (int)Math.Floor(detectedStar.Position.X / imageSize.Width * numRegionsWide);
                    regionDetectedStars[regionCol, regionRow].Add(detectedStar);
                }

                using (Scope.Enter()) {
                    var scene = new Scene();

                    var originalPoints = new float[numRegionsTall, numRegionsWide];
                    var originalPointCount = 0;
                    for (int regionRow = 0; regionRow < numRegionsTall; ++regionRow) {
                        for (int regionCol = 0; regionCol < numRegionsWide; ++regionCol) {
                            var detectedStars = regionDetectedStars[regionCol, regionRow];
                            if (detectedStars.Count == 0) {
                                originalPoints[regionRow, regionCol] = float.NaN;
                                continue;
                            }
                            var (fwhmMedian, _) = detectedStars.Select(s => s.PSF.FWHMArcsecs).MedianMAD();
                            originalPoints[regionRow, regionCol] = (float)fwhmMedian;
                            ++originalPointCount;
                        }
                    }

                    Array<float> scatteredPointPositions = zeros<float>(2, originalPointCount);
                    Array<float> scatteredPointValues = zeros<float>(1, originalPointCount);
                    var pointIndex = 0;
                    for (int regionRow = 0; regionRow < numRegionsTall; ++regionRow) {
                        for (int regionCol = 0; regionCol < numRegionsWide; ++regionCol) {
                            if (double.IsNaN(originalPoints[regionRow, regionCol])) {
                                continue;
                            }
                            scatteredPointValues[0, pointIndex] = originalPoints[regionRow, regionCol];
                            scatteredPointPositions[0, pointIndex] = regionCol;
                            scatteredPointPositions[1, pointIndex] = regionRow;
                        }
                    }

                    // interpolated grid positions
                    const int upscalingFactor = 5;
                    const float meshInterval = 1.0f / upscalingFactor;
                    Array<float> interpPositionsY = 1, interpPositions = meshgrid(arange(0.0f, meshInterval, numRegionsTall - 1), arange(0.0f, meshInterval, numRegionsWide - 1), interpPositionsY);
                    Array<float> interpPositionsReshaped = interpPositions[":"].T.Concat(interpPositionsY[":"].T, 0);

                    Array<float> smoothedPoints = Interpolation.kriging(scatteredPointValues, scatteredPointPositions, interpPositionsReshaped);
                    smoothedPoints.a = reshape(smoothedPoints, interpPositions.shape);

                    var colormap = GetSceneColorMap(pointColor: PointColor.ToDrawingColor(), surfaceColor: SurfaceColor.ToDrawingColor());
                    var contourPlot = new ContourPlot(smoothedPoints, colormap: colormap, create3D: true, lineWidth: 1, showLabels: true);
                    var contourSurface = new Surface(smoothedPoints, colormap: colormap) {
                        Wireframe = { Visible = false },
                        UseLighting = true
                    };
                    var contourPlotCube = new PlotCube(twoDMode: false) {
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
                                  Text = "",
                                  Visible = false
                              },
                              Ticks = {
                                  Mode = TickMode.Manual
                              }
                          }
                      },
                        Projection = Projection.Orthographic,
                        Children = {
                      contourPlot,
                      contourSurface
                    }
                    };

                    contourPlotCube.AspectRatioMode = AspectRatioMode.MaintainRatios;
                    contourPlotCube.AllowZoom = false;
                    contourPlotCube.AllowPan = false;
                    contourPlotCube.DataScreenRect = new RectangleF(0, 0, 1.0f, 1.0f);
                    scene.Add(contourPlotCube);
                    scene.Screen.First<Label>().Visible = false;

                    UpdateAxisColor(contourPlotCube, AxisColor.ToDrawingColor());
                    UpdateTextColor(contourPlotCube, contourPlot, TextColor.ToDrawingColor());

                    contourPlotCube.Configure();
                    this.PlotCube = contourPlotCube;
                    this.ContourPlot = contourPlot;
                    this.ContourSurface = contourSurface;
                    return scene;
                }
            } catch (Exception e) {
                Logger.Error(e, "Failed to render scene for FWHM Contour map");
                Notification.ShowError(e.Message);
                return null;
            }
        }
    }
}