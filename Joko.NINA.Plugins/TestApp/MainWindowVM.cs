#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Controls;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using ScottPlot;
using ScottPlot.Statistics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace TestApp {

    public class MainWindowVM : BaseINPC, IScottPlotController {

        public MainWindowVM() {
            TiltPlaneModel = TiltPlaneModel.Create(
                imageSize: new Size(1600, 1000),
                fRatio: 5.0d,
                focuserStepSizeMicrons: 2.2d,
                centerFocuser: 23264.0d,
                topLeftFocuser: 23277.0d,
                topRightFocuser: 23199.0d,
                bottomLeftFocuser: 23273.0d,
                bottomRightFocuser: 23208.0d);

            NumRegionsWide = 9;
            FwhmStarDetectionResult = CreateSyntheticFwhmResult(NumRegionsWide);
            EccentricityPlot = CreateSyntheticEccentricityPlot();
        }

        public TiltPlaneModel TiltPlaneModel { get; }

        public HocusFocusStarDetectionResult FwhmStarDetectionResult { get; }

        public int NumRegionsWide { get; }

        public Plot EccentricityPlot { get; }

        public event EventHandler PlotRefreshed;

        public void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
        }

        public void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            PlotRefreshed?.Invoke(this, EventArgs.Empty);
        }

        private static HocusFocusStarDetectionResult CreateSyntheticFwhmResult(int numRegionsWide) {
            const int regionSize = 110;
            var numRegionsTall = 7;
            var imageSize = new Size(numRegionsWide * regionSize, numRegionsTall * regionSize);
            var stars = new List<DetectedStar>();

            for (var row = 0; row < numRegionsTall; ++row) {
                for (var col = 0; col < numRegionsWide; ++col) {
                    if ((row == 1 && col == 1) || (row == 5 && col == 7) || (row == 3 && col == 4)) {
                        continue;
                    }

                    var x = col * regionSize + regionSize * 0.5d;
                    var y = row * regionSize + regionSize * 0.5d;
                    var centeredX = (col - (numRegionsWide - 1) / 2.0d) / ((numRegionsWide - 1) / 2.0d);
                    var centeredY = (row - (numRegionsTall - 1) / 2.0d) / ((numRegionsTall - 1) / 2.0d);
                    var fwhmArcseconds =
                        2.15d +
                        0.45d * centeredX +
                        0.30d * centeredY +
                        0.55d * centeredX * centeredX +
                        0.20d * Math.Sin((row + col) * 0.9d);

                    stars.Add(CreateSyntheticStar(x, y, fwhmArcseconds));
                    stars.Add(CreateSyntheticStar(x + 16.0d, y - 13.0d, fwhmArcseconds + 0.04d));
                }
            }

            return new HocusFocusStarDetectionResult() {
                ImageSize = imageSize,
                PixelScale = 1.0d,
                StarList = stars,
                DetectedStars = stars.Count,
                FWHM = stars.Cast<HocusFocusDetectedStar>().Average(s => s.PSF.FWHMArcsecs),
                FWHMMAD = 0.0d
            };
        }

        private static HocusFocusDetectedStar CreateSyntheticStar(double x, double y, double fwhmArcseconds) {
            return new HocusFocusDetectedStar() {
                Position = new Accord.Point((float)x, (float)y),
                BoundingBox = new Rectangle((int)x - 5, (int)y - 5, 10, 10),
                HFR = fwhmArcseconds / 2.0d,
                AverageBrightness = 1200.0d,
                MaxBrightness = 4200.0d,
                Background = 150.0d,
                PSF = new PSFModel(
                    psfType: StarDetectorPSFFitType.Gaussian,
                    offsetX: 0.0d,
                    offsetY: 0.0d,
                    peak: 4200.0d,
                    background: 150.0d,
                    sigmaX: fwhmArcseconds / 2.355d,
                    sigmaY: fwhmArcseconds / 2.355d,
                    fwhmX: fwhmArcseconds,
                    fwhmY: fwhmArcseconds,
                    thetaRadians: 0.0d,
                    rSquared: 0.99d,
                    pixelScale: 1.0d)
            };
        }

        private static Plot CreateSyntheticEccentricityPlot() {
            const int numRegionsWide = 9;
            const int numRegionsTall = 7;
            var xs = DataGen.Range(0, numRegionsWide);
            var ys = DataGen.Range(0, numRegionsTall);
            var vectors = new Vector2[numRegionsWide, numRegionsTall];

            for (var col = 0; col < numRegionsWide; ++col) {
                for (var row = 0; row < numRegionsTall; ++row) {
                    var centeredX = (col - (numRegionsWide - 1) / 2.0d) / ((numRegionsWide - 1) / 2.0d);
                    var centeredY = (row - (numRegionsTall - 1) / 2.0d) / ((numRegionsTall - 1) / 2.0d);
                    var eccentricity = 0.08d + 0.32d * Math.Sqrt(centeredX * centeredX + centeredY * centeredY);
                    var theta = Math.Atan2(centeredY, centeredX) + Math.PI / 7.0d;
                    vectors[col, row] = new Vector2(
                        Math.Cos(theta) * eccentricity,
                        Math.Sin(theta) * eccentricity);
                }
            }

            var plot = new Plot();
            var vectorField = plot.AddVectorField(vectors, xs, ys, color: Color.LimeGreen);
            vectorField.ScaledArrowheads = true;
            vectorField.ScaledArrowheadLength = 0.0d;
            vectorField.ScaledArrowheadWidth = 0.0d;

            plot.Title("Eccentricity");
            plot.XAxis.Ticks(false);
            plot.YAxis.Ticks(false);
            plot.SetAxisLimits(-0.5d, numRegionsWide - 0.5d, -0.5d, numRegionsTall - 0.5d);
            plot.Style(
                figureBackground: Color.FromArgb(30, 33, 37),
                dataBackground: Color.FromArgb(30, 33, 37),
                grid: Color.FromArgb(75, 84, 94),
                tick: Color.White,
                titleLabel: Color.White);
            return plot;
        }
    }
}
