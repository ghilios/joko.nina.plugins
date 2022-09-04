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
using NINA.Core.Utility;
using System.Collections.Generic;
using ILNumerics.Drawing.Plotting;

using static ILNumerics.Globals;
using static ILNumerics.ILMath;
using DashStyle = ILNumerics.Drawing.DashStyle;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using ILSize = ILNumerics.Size;
using System.Drawing;
using ScottPlot;
using System;
using Accord.Statistics.Models.Regression.Linear;
using Accord.Math.Optimization.Losses;
using static TestApp.Program;
using OpenCvSharp;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System.Threading;
using System.Windows.Media;
using System.Linq;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Size = OpenCvSharp.Size;
using ScottPlot.Statistics;
using NINA.Joko.Plugins.HocusFocus.Controls;
using System.Windows;

namespace TestApp {

    public class MainWindowVM : BaseINPC, IScottPlotController {

        private static (double, double, double) GetPlane() {
            var ols = new OrdinaryLeastSquares() {
                UseIntercept = true
            };

            /*
                Center - HFR: 2.18011990085716, Focuser: 23264
                Top Left - HFR Delta: -0.000118910201427624, Focuser Delta: 13
                Top Right - HFR Delta: 0.0372817463447874, Focuser Delta: -65
                Bottom Left - HFR Delta: 0.0434991788384473, Focuser Delta: 9
                Bottom Right - HFR Delta: 0.00131973679976527, Focuser Delta: -56
                */

            double[][] inputs =
            {
                new double[] { -1, 1 },
                new double[] { 1, 1 },
                new double[] { -1, -1 },
                new double[] { 1, -1 },
            };

            // located in the same Z (z = 1)
            // double[] outputs = { -0.000118910201427624, 0.0372817463447874, 0.0434991788384473, 0.00131973679976527 };
            double[] outputs = { 13, -65, 9, -56 };

            // Use Ordinary Least Squares to estimate a regression model
            MultipleLinearRegression regression = ols.Learn(inputs, outputs);

            // As result, we will be given the following:
            double a = regression.Weights[0]; // a = 0
            double b = regression.Weights[1]; // b = 0
            double c = regression.Intercept;  // c = 1
            // z = ax + by + c

            // We can compute the predicted points using
            double[] predicted = regression.Transform(inputs);

            // And the squared error loss using
            double error = new SquareLoss(outputs).Loss(predicted);

            // We can also compute other measures, such as the coefficient of determination r²
            double r2 = new RSquaredLoss(numberOfInputs: 2, expected: outputs).Loss(predicted); // should be 1

            // We can also compute the adjusted or weighted versions of r² using
            var r2loss = new RSquaredLoss(numberOfInputs: 2, expected: outputs) {
                Adjust = true
            };

            double ar2 = r2loss.Loss(predicted); // should be 1

            // Alternatively, we can also use the less generic, but maybe more user-friendly method directly:
            double ur2 = regression.CoefficientOfDetermination(inputs, outputs, adjust: true); // should be 1

            return (a, b, c);
        }

        private static double ToRadians(double degrees) {
            return (degrees / 180.0d) * Math.PI;
        }

        public MainWindowVM() {
            double[][] inputs =
            {
                new double[] { -1, 1 },
                new double[] { 1, 1 },
                new double[] { -1, -1 },
                new double[] { 1, -1 },
            };
            double[] outputs = { 13, -65, 9, -56 };

            using (Scope.Enter()) {
                var scene = new Scene();

                var (a, b, c) = GetPlane();
                Array<double> points = zeros<double>(3, 4);
                for (int i = 0; i < 4; ++i) {
                    points[0, i] = inputs[i][0];
                    points[1, i] = inputs[i][1];
                    points[2, i] = outputs[i];
                }

                Array<double> surfacePoints = zeros<double>(3, 4);
                for (int i = 0; i < 4; ++i) {
                    var x = inputs[i][0];
                    var y = inputs[i][1];
                    surfacePoints[0, i] = x;
                    surfacePoints[1, i] = y;
                    surfacePoints[2, i] = x * a + y * b + c;
                }

                var triStr = new TrianglesStrip();
                triStr.Positions.Update(tosingle(surfacePoints));
                triStr.Color = DrawingColor.FromArgb((byte)(0.8 * 255), DrawingColor.Gray);

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
                    // rotate plot cube
                    Rotation = Matrix4.Rotation(new Vector3(1, 0, 0), ToRadians(-105 + 180)) * Matrix4.Rotation(new Vector3(0, 0, 1), ToRadians(15)),
                    // perspective projection
                    Projection = Projection.Orthographic,
                    Children = {
	                  // add line plot, provide data as rows
	                  new Points {
                        Positions = tosingle(points),
                        // Colors = tosingle(A),
                        Color = DrawingColor.Black
                      },
                      triStr
                    }
                };
                plotCube.Axes.XAxis.Ticks.Add(-1.0f, "Left");
                plotCube.Axes.XAxis.Ticks.Add(1.0f, "Right");
                plotCube.Axes.YAxis.Ticks.Add(1.0f, "Top");
                plotCube.Axes.YAxis.Ticks.Add(-1.0f, "Bottom");

                // TODO: Add PropertyChanged for Rotation. If Identity, set to starting point
                plotCube.AspectRatioMode = AspectRatioMode.MaintainRatios;
                plotCube.AllowZoom = false;
                plotCube.AllowPan = false;

                scene.Add(plotCube);
                scene.Screen.First<ILNumerics.Drawing.Label>().Visible = false;
                Scene = scene;
            }

            var inputFilePath = @"C:\Users\ghili\Downloads\LIGHT_2022-01-08_20-40-16_H_-10.00_300.00s_0031.tif";
            var (inputSize, detectionResult) = AsyncContext.Run(() => DetectStars(inputFilePath));

            int numRegionsWide = 17;
            int regionSizePixels = inputSize.Width / numRegionsWide;
            int numRegionsTall = inputSize.Height / regionSizePixels;
            numRegionsTall += numRegionsTall % 2 == 0 ? 1 : 0;
            var regionDetectedStars = new List<HocusFocusDetectedStar>[numRegionsWide, numRegionsTall];
            for (int i = 0; i < numRegionsWide; ++i) {
                for (int j = 0; j < numRegionsTall; ++j) {
                    regionDetectedStars[i, j] = new List<HocusFocusDetectedStar>();
                }
            }

            foreach (var detectedStar in detectionResult.StarList.Cast<HocusFocusDetectedStar>()) {
                if (detectedStar.PSF == null) {
                    continue;
                }

                var regionRow = (int)Math.Floor(detectedStar.Position.Y / inputSize.Height * numRegionsTall);
                var regionCol = (int)Math.Floor(detectedStar.Position.X / inputSize.Width * numRegionsWide);
                regionDetectedStars[regionCol, regionRow].Add(detectedStar);
            }

            int numRegionPoints = 0;
            for (int regionRow = 0; regionRow < numRegionsTall; ++regionRow) {
                for (int regionCol = 0; regionCol < numRegionsWide; ++regionCol) {
                    var detectedStars = regionDetectedStars[regionCol, regionRow];
                    if (detectedStars.Count == 0) {
                        continue;
                    }
                    ++numRegionPoints;
                }
            }

            using (Scope.Enter()) {
                var fwhmContourScene = new Scene();

                Array<float> points = zeros<float>(numRegionsTall, numRegionsWide);
                Array<float> points2 = zeros<float>(3, numRegionsTall * numRegionsWide);
                // Array<double> y = zeros<double>(numRegionPoints);
                // Array<double> z = zeros<double>(numRegionPoints);
                float minFWHM = float.PositiveInfinity;
                float maxFWHM = -1.0f;

                int pointIndex = 0;
                for (int regionRow = 0; regionRow < numRegionsTall; ++regionRow) {
                    for (int regionCol = 0; regionCol < numRegionsWide; ++regionCol) {
                        var detectedStars = regionDetectedStars[regionCol, regionRow];
                        if (detectedStars.Count == 0) {
                            points[regionRow, regionCol] = float.NaN;
                            continue;
                        }
                        var (fwhmMedian, _) = detectedStars.Select(s => s.PSF.FWHMArcsecs).MedianMAD();
                        points2[0, pointIndex] = regionRow;
                        points2[1, pointIndex] = regionCol;
                        points2[2, pointIndex++] = (float)fwhmMedian;
                        points[regionRow, regionCol] = (float)fwhmMedian;

                        maxFWHM = Math.Max(maxFWHM, (float)fwhmMedian);
                        minFWHM = Math.Min(minFWHM, (float)fwhmMedian);
                    }
                }

                var contourPlot = new ContourPlot(points, colormap: Colormaps.Gray, create3D: true, lineWidth: 2, showLabels: false);
                minFWHM = (float)Math.Round(minFWHM, 1);
                /*
                for (var v = minFWHM; v < maxFWHM + 0.1f; v += 0.1f) {
                    contourPlot.Levels.Add(new ContourLevel() {
                        Value = v,
                        ShowLabel = false,
                        Text = v.ToString()
                    });
                }
                */

                var contourPlotCube = new PlotCube(twoDMode: false) {
                    Axes = {
                          XAxis = {
                              Label = {
                                  Text = "",
                                  Visible = false,
                                  Position = new Vector3(0.5, 0.1, 0)
                              },
                              Ticks = {
                                  Mode = TickMode.Manual
                              }
                          },
                          YAxis = {
                              Label = {
                                  Text = "",
                                  Visible = false,
                                  Position = new Vector3(0.5, 0.1, 0)
                              },
                              Ticks = {
                                  Mode = TickMode.Manual
                              }
                          },
                          ZAxis = {
                              Label = {
                                  Text = "FWHM",
                                  Position = new Vector3(0.5, 10, 0)
                              },
                              Ticks = {
                                  Mode = TickMode.Manual
                              }
                          }
                      },
                    //Rotation = Matrix4.Rotation(new Vector3(1, 0, 0), ToRadians(-105 + 180)) * Matrix4.Rotation(new Vector3(0, 0, 1), ToRadians(15)),
                    Projection = Projection.Orthographic,
                    Children = {
                      contourPlot,
                      new Surface(points, colormap: Colormaps.Gray) {
                        Wireframe = { Visible = false },
                        UseLighting = true,
                        Children = {
                              new Legend { Location = new PointF(1f,.1f) },
                              new Colorbar {
                                  Location = new PointF(1,.4f),
                                  Anchor = new PointF(1,0)
                              }
                          }
                      }
                      /*
                      new Points {
                        Positions = points2,
                        Color = DrawingColor.Black
                      },
                      */
                    }
                };

                contourPlotCube.ContentFitMode = ContentFitModes.ContentXY;
                contourPlotCube.AspectRatioMode = AspectRatioMode.MaintainRatios;
                contourPlotCube.AllowZoom = false;
                contourPlotCube.AllowPan = false;
                contourPlotCube.DataScreenRect = new RectangleF(0, 0, 1.0f, 1.0f);
                fwhmContourScene.Add(contourPlotCube);
                fwhmContourScene.Screen.First<ILNumerics.Drawing.Label>().Visible = false;
                FWHMContourScene = fwhmContourScene;
            }

            {
                var plot = new Plot();
                double[] xs = DataGen.Range(0, numRegionsWide);
                double[] ys = DataGen.Range(0, numRegionsTall);
                Vector2[,] vectors = new Vector2[numRegionsWide, numRegionsTall];
                double[] centerXs = new double[numRegionsWide * numRegionsTall];
                double[] centerYs = new double[numRegionsWide * numRegionsTall];
                double[] eccentricities = new double[numRegionsWide * numRegionsTall];

                int pointIndex = 0;
                for (int regionRow = 0; regionRow < numRegionsTall; ++regionRow) {
                    for (int regionCol = 0; regionCol < numRegionsWide; ++regionCol) {
                        var detectedStars = regionDetectedStars[regionCol, regionRow];

                        if (detectedStars.Count > 0) {
                            var (eccentricityMedian, _) = detectedStars.Select(s => s.PSF.Eccentricity).MedianMAD();
                            var (psfRotationMedian, _) = detectedStars.Select(s => s.PSF.ThetaRadians).MedianMAD();

                            var scaledEccentricity = eccentricityMedian * eccentricityMedian;
                            double x = Math.Cos(psfRotationMedian) * scaledEccentricity;
                            double y = Math.Sin(psfRotationMedian) * scaledEccentricity;
                            vectors[regionCol, regionRow] = new Vector2(x, y);
                            eccentricities[pointIndex] = eccentricityMedian;
                        } else {
                            eccentricities[pointIndex] = double.NaN;
                        }
                        centerXs[pointIndex] = regionCol;
                        centerYs[pointIndex++] = regionRow;
                    }
                }

                var primaryColor = (Application.Current.TryFindResource("PrimaryBrush") as SolidColorBrush)?.Color ?? Colors.Red;
                var secondaryColor = (Application.Current.TryFindResource("SecondaryBrush") as SolidColorBrush)?.Color ?? Colors.Purple;
                var vectorField = plot.AddVectorField(vectors, xs, ys, color: primaryColor.ToDrawingColor());
                var scatterPoints = plot.AddScatterPoints(centerXs, centerYs, color: secondaryColor.ToDrawingColor());
                scatterPoints.IsVisible = false;

                vectorField.ScaledArrowheads = true;
                vectorField.ScaledArrowheadLength = 0.0;
                vectorField.ScaledArrowheadWidth = 0.0;

                var highlightedPoint = plot.AddPoint(0, 0);
                highlightedPoint.Color = DrawingColor.Red;
                highlightedPoint.MarkerSize = 5;
                highlightedPoint.MarkerShape = ScottPlot.MarkerShape.openCircle;
                highlightedPoint.IsVisible = false;

                plot.Title("Eccentricity");
                plot.XAxis.Ticks(false);
                plot.YAxis.Ticks(false);

                lastHighlightedEccentricityPointIndex = -1;
                highlightedEccentricityPoint = highlightedPoint;
                eccentricityVectorField = vectorField;
                eccentricityCenterPoints = scatterPoints;
                eccentricityValues = eccentricities;
                Plot = plot;
            }

            /*
             var plt = new ScottPlot.Plot(600, 400);

double[] xs = DataGen.Range(-5, 5, .5);
double[] ys = DataGen.Range(-5, 5, .5);
Vector2[,] vectors = new Vector2[xs.Length, ys.Length];
double r = 0.5;

for (int i = 0; i < xs.Length; i++)
{
    for (int j = 0; j < ys.Length; j++)
    {
        double x = ys[j];
        double y = -9.81 / r * Math.Sin(xs[i]);

        vectors[i, j] = new Vector2(x, y);
    }
}

plt.AddVectorField(vectors, xs, ys, colormap: Drawing.Colormap.Turbo);
plt.XLabel("θ");
plt.YLabel("dθ/dt");
             */

            Console.WriteLine();
        }

        private async Task<(Size, HocusFocusStarDetectionResult)> DetectStars(string inputFilePath) {
            var starAnnotatorOptions = StaticStarAnnotatorOptions.CreateDefault();
            using (var t = new ResourcesTracker()) {
                var src = t.T(new Mat(inputFilePath, ImreadModes.Unchanged));
                var srcFloat = t.NewMat();
                ConvertToFloat(src, srcFloat);

                var alglibAPI = new AlglibAPI();
                var detector = new StarDetector(alglibAPI);
                var starDetectionParams = new StarDetectionParams() { };
                var detectorParams = new StarDetectorParams() {
                    NoiseReductionRadius = 3,
                    NoiseClippingMultiplier = 4,
                    StarClippingMultiplier = 2,
                    StructureLayers = 4,
                    MinimumStarBoundingBoxSize = 5,
                    AnalysisSamplingSize = 1.0f,
                    Sensitivity = 10.0,
                    PeakResponse = 0.75,
                    MaxDistortion = 0.5,
                    StarCenterTolerance = 0.3,
                    MinHFR = 1.5,
                    StructureDilationCount = 0,
                    StructureDilationSize = 3,
                    PSFResolution = 10,
                    PSFGoodnessOfFitThreshold = 0.9,
                    PSFParallelPartitionSize = 0,
                    ModelPSF = true,
                    PSFFitType = StarDetectorPSFFitType.Gaussian,
                    UseILNumerics = true,
                };
                var detectorResult = await detector.Detect(srcFloat, detectorParams, null, CancellationToken.None);
                var result = new HocusFocusStarDetectionResult() {
                    StarList = detectorResult.DetectedStars.Select(s => HocusFocusStarDetection.ToDetectedStar(s)).ToList(),
                    DetectedStars = detectorResult.DetectedStars.Count,
                    DetectorParams = detectorParams,
                    Params = starDetectionParams,
                    Metrics = detectorResult.Metrics,
                    DebugData = detectorResult.DebugData,
                };
                return (src.Size(), result);
            }
        }

        public void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
            // determine point nearest the cursor
            var plotControl = (WpfPlot)sender;
            (double mouseCoordX, double mouseCoordY) = plotControl.GetMouseCoordinates();
            double xyRatio = plotControl.Plot.XAxis.Dims.PxPerUnit / plotControl.Plot.YAxis.Dims.PxPerUnit;
            (double pointX, double pointY, int pointIndex) = eccentricityCenterPoints.GetPointNearest(mouseCoordX, mouseCoordY, xyRatio);

            // place the highlight over the point of interest
            highlightedEccentricityPoint.X = pointX;
            highlightedEccentricityPoint.Y = pointY;
            highlightedEccentricityPoint.IsVisible = true;
            highlightedEccentricityPoint.Text = eccentricityValues[pointIndex].ToString("0.00");
            // TODO: Set text color to Foreground from theme

            // render if the highlighted point changed
            if (lastHighlightedEccentricityPointIndex != pointIndex) {
                lastHighlightedEccentricityPointIndex = pointIndex;
                RefreshPlot();
            }
        }

        private void RefreshPlot() {
            this.PlotRefreshed?.Invoke(this, new EventArgs());
        }

        public void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            lastHighlightedEccentricityPointIndex = -1;
            highlightedEccentricityPoint.IsVisible = false;
            RefreshPlot();
        }

        private Scene scene;

        public Scene Scene {
            get => scene;
            set {
                scene = value;
                RaisePropertyChanged();
            }
        }

        private Scene fwhmContourScene;

        public Scene FWHMContourScene {
            get => fwhmContourScene;
            set {
                fwhmContourScene = value;
                RaisePropertyChanged();
            }
        }

        private Plot plot;

        public event EventHandler PlotRefreshed;

        public Plot Plot {
            get => plot;
            set {
                plot = value;
                RaisePropertyChanged();
            }
        }

        private ScottPlot.Plottable.VectorField eccentricityVectorField;
        private ScottPlot.Plottable.MarkerPlot highlightedEccentricityPoint;
        private ScottPlot.Plottable.ScatterPlot eccentricityCenterPoints;
        private int lastHighlightedEccentricityPointIndex;
        private double[] eccentricityValues;
    }
}