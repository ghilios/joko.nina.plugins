#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using DrawingColor = System.Drawing.Color;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public class FWHMContourControl : SurfacePlotControlBase {

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
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnAnyPropertyChanged));

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

        private static void OnAnyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var thisControl = (FWHMContourControl)d;
            thisControl.UpdateScene();
        }

        protected override SurfacePlotModel GetPlotModel() {
            var starDetectionResult = StarDetectionResult;
            var numRegionsWide = NumRegionsWide;
            if (starDetectionResult == null || numRegionsWide <= 1) {
                return null;
            }

            try {
                var imageSize = starDetectionResult.ImageSize;
                int regionSizePixels = imageSize.Width / numRegionsWide;
                if (regionSizePixels <= 0) {
                    return null;
                }

                int numRegionsTall = imageSize.Height / regionSizePixels;
                numRegionsTall += numRegionsTall % 2 == 0 ? 1 : 0;
                if (numRegionsTall <= 1) {
                    return null;
                }

                var regionDetectedStars = new List<HocusFocusDetectedStar>[numRegionsWide, numRegionsTall];
                for (int col = 0; col < numRegionsWide; ++col) {
                    for (int row = 0; row < numRegionsTall; ++row) {
                        regionDetectedStars[col, row] = new List<HocusFocusDetectedStar>();
                    }
                }

                foreach (var detectedStar in starDetectionResult.StarList.Cast<HocusFocusDetectedStar>()) {
                    if (detectedStar.PSF == null) {
                        continue;
                    }

                    var regionRow = Math.Clamp((int)Math.Floor(detectedStar.Position.Y / imageSize.Height * numRegionsTall), 0, numRegionsTall - 1);
                    var regionCol = Math.Clamp((int)Math.Floor(detectedStar.Position.X / imageSize.Width * numRegionsWide), 0, numRegionsWide - 1);
                    regionDetectedStars[regionCol, regionRow].Add(detectedStar);
                }

                var samples = new List<KrigingSample>();
                for (int row = 0; row < numRegionsTall; ++row) {
                    for (int col = 0; col < numRegionsWide; ++col) {
                        var detectedStars = regionDetectedStars[col, row];
                        if (detectedStars.Count == 0) {
                            continue;
                        }

                        var (fwhmMedian, _) = detectedStars.Select(s => s.PSF.FWHMArcsecs).MedianMAD();
                        samples.Add(new KrigingSample(col, row, fwhmMedian));
                    }
                }

                if (samples.Count == 0) {
                    return null;
                }

                const int upscalingFactor = 10;
                var interpWidth = (numRegionsWide - 1) * upscalingFactor + 1;
                var interpHeight = (numRegionsTall - 1) * upscalingFactor + 1;
                var interpolator = OrdinaryKrigingInterpolator.Create(samples);
                var surface = interpolator.InterpolateGrid(interpWidth, interpHeight, 0.0d, numRegionsWide - 1, 0.0d, numRegionsTall - 1);
                var backgroundColor = PlotBackgroundColor.ToDrawingColor();
                var pointColor = EnsureVisibleColor(PointColor.ToDrawingColor(), DrawingColor.DodgerBlue);
                var model = new SurfacePlotModel() {
                    Z = surface,
                    BackgroundColor = backgroundColor,
                    TextColor = TextColor.ToDrawingColor(),
                    AxisColor = AxisColor.ToDrawingColor(),
                    ColorMap = CreateFwhmColorMap(),
                    ContourColor = DrawingColor.FromArgb(230, 255, 255, 255),
                    ShowContours = true,
                    ShowContourLabels = true,
                    ShowColorBar = true,
                    ContourCount = 9,
                    ContourLabelFormat = "0.0",
                    RotationXDegrees = 0.0d,
                    RotationZDegrees = 0.0d,
                    VerticalScale = 0.0d
                };

                foreach (var sample in samples) {
                    model.Points.Add(new PlotPoint3D(sample.X * upscalingFactor, sample.Y * upscalingFactor, sample.Value, pointColor, size: 5.0f, label: sample.Value.ToString("0.0")));
                }
                return model;
            } catch (Exception e) {
                Logger.Error(e, "Failed to render FWHM contour map");
                Notification.ShowError(e.Message);
                return null;
            }
        }

        private static DrawingColor EnsureVisibleColor(DrawingColor color, DrawingColor fallback) {
            if (color.A == 0) {
                return DrawingColor.FromArgb(byte.MaxValue, fallback.R, fallback.G, fallback.B);
            }
            return DrawingColor.FromArgb(byte.MaxValue, color.R, color.G, color.B);
        }

        private static SurfaceColorMap CreateFwhmColorMap() {
            return new SurfaceColorMap(
                (0.00d, DrawingColor.FromArgb(255, 44, 25, 85)),
                (0.20d, DrawingColor.FromArgb(255, 35, 88, 166)),
                (0.40d, DrawingColor.FromArgb(255, 26, 156, 169)),
                (0.60d, DrawingColor.FromArgb(255, 94, 201, 97)),
                (0.80d, DrawingColor.FromArgb(255, 241, 196, 15)),
                (1.00d, DrawingColor.FromArgb(255, 210, 67, 54)));
        }
    }
}
