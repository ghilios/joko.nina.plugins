#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Statistics.Models.Regression.Linear;
using ILNumerics;
using ILNumerics.Drawing;
using static ILNumerics.ILMath;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using DrawingColor = System.Drawing.Color;
using DrawingSize = System.Drawing.Size;
using ILNumerics.Drawing.Plotting;
using NINA.Astrometry;
using System.Windows.Media;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System.ComponentModel;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NINA.Joko.Plugins.HocusFocus.Controls;
using ILNLines = ILNumerics.Drawing.Lines;
using ILNLabel = ILNumerics.Drawing.Label;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class TiltPlaneModel {

        public TiltPlaneModel(DrawingSize imageSize, double a, double b, double c, double mean) {
            if (imageSize.Width == 0 || imageSize.Height <= 0) {
                throw new ArgumentException($"ImageSize ({ImageSize.Width}, {ImageSize.Height}) dimensions must be positive");
            }

            ImageSize = imageSize;
            A = a;
            B = b;
            C = c;
            MeanFocuserPosition = mean;
        }

        public DrawingSize ImageSize { get; private set; }
        public double A { get; private set; }
        public double B { get; private set; }
        public double C { get; private set; }
        public double MeanFocuserPosition { get; private set; }

        public double EstimateFocusPosition(int x, int y) {
            var xRatio = GetModelX(x);
            var yRatio = GetModelY(y);
            return A * xRatio + B * yRatio + C;
        }

        public double GetModelX(int x) {
            if (x < 0 || x >= ImageSize.Height) {
                throw new ArgumentException($"X ({x}) must be within the image size dimensions ({ImageSize.Width}x{ImageSize.Height})");
            }

            var xyRatio = ImageSize.Width / ImageSize.Height;
            return ((double)x / ImageSize.Width - 0.5) * xyRatio;
        }

        public double GetModelY(int y) {
            if (y < 0 || y >= ImageSize.Height) {
                throw new ArgumentException($"Y ({y}) must be within the image size dimensions ({ImageSize.Width}x{ImageSize.Height})");
            }

            var xyRatio = ImageSize.Width / ImageSize.Height;
            return (double)y / ImageSize.Height - 0.5;
        }

        public static TiltPlaneModel Create(AutoFocusResult result) {
            var ols = new OrdinaryLeastSquares() {
                UseIntercept = true
            };

            var xyRatio = result.ImageSize.Width / result.ImageSize.Height;
            double[][] inputs =
            {
                new double[] { -xyRatio, 1 },
                new double[] { xyRatio, 1 },
                new double[] { -xyRatio, -1 },
                new double[] { xyRatio, -1 },
            };

            var topLeftFocuser = result.RegionResults[2].EstimatedFinalFocuserPosition;
            var topRightFocuser = result.RegionResults[3].EstimatedFinalFocuserPosition;
            var bottomLeftFocuser = result.RegionResults[4].EstimatedFinalFocuserPosition;
            var bottomRightFocuser = result.RegionResults[5].EstimatedFinalFocuserPosition;
            double[] outputs = { topLeftFocuser, topRightFocuser, bottomLeftFocuser, bottomRightFocuser };

            MultipleLinearRegression regression = ols.Learn(inputs, outputs);

            double a = regression.Weights[0];
            double b = regression.Weights[1];
            double c = regression.Intercept;
            double mean = outputs.Average();
            return new TiltPlaneModel(result.ImageSize, a, b, c, mean);
        }
    }

    public class TiltModel : BaseINPC {
        private readonly IApplicationDispatcher applicationDispatcher;
        private int nextHistoryId = 0;

        public TiltModel(IApplicationDispatcher applicationDispatcher) {
            this.applicationDispatcher = applicationDispatcher;
            SensorTiltModels = new AsyncObservableCollection<SensorTiltModel>();
            SensorTiltHistoryModels = new AsyncObservableCollection<SensorTiltHistoryModel>();
        }

        private void UpdateTiltMeasurementsTable(AutoFocusResult result, TiltPlaneModel tiltModel) {
            var centerFocuser = result.RegionResults[1].EstimatedFinalFocuserPosition;
            var topLeftFocuser = result.RegionResults[2].EstimatedFinalFocuserPosition;
            var topRightFocuser = result.RegionResults[3].EstimatedFinalFocuserPosition;
            var bottomLeftFocuser = result.RegionResults[4].EstimatedFinalFocuserPosition;
            var bottomRightFocuser = result.RegionResults[5].EstimatedFinalFocuserPosition;

            var newSideToTiltModels = new List<SensorTiltModel>();
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.Center) {
                FocuserPosition = centerFocuser,
                AdjustmentRequired = double.NaN,
                RSquared = result.RegionResults[1].Fittings.GetRSquared()
            });
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.TopLeft) {
                FocuserPosition = topLeftFocuser,
                AdjustmentRequired = tiltModel.MeanFocuserPosition - topLeftFocuser,
                RSquared = result.RegionResults[2].Fittings.GetRSquared()
            });
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.TopRight) {
                FocuserPosition = topRightFocuser,
                AdjustmentRequired = tiltModel.MeanFocuserPosition - topRightFocuser,
                RSquared = result.RegionResults[3].Fittings.GetRSquared()
            });
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.BottomLeft) {
                FocuserPosition = bottomLeftFocuser,
                AdjustmentRequired = tiltModel.MeanFocuserPosition - bottomLeftFocuser,
                RSquared = result.RegionResults[4].Fittings.GetRSquared()
            });
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.BottomRight) {
                FocuserPosition = bottomRightFocuser,
                AdjustmentRequired = tiltModel.MeanFocuserPosition - bottomRightFocuser,
                RSquared = result.RegionResults[5].Fittings.GetRSquared()
            });

            SensorTiltModels.Clear();
            foreach (var sensorTiltModel in newSideToTiltModels.OrderBy(x => (int)x.SensorSide)) {
                SensorTiltModels.Add(sensorTiltModel);
            }

            var historyId = Interlocked.Increment(ref nextHistoryId);
            SensorTiltHistoryModels.Insert(0, new SensorTiltHistoryModel(
                historyId: historyId,
                center: newSideToTiltModels.First(m => m.SensorSide == SensorSide.Center),
                topLeft: newSideToTiltModels.First(m => m.SensorSide == SensorSide.TopLeft),
                topRight: newSideToTiltModels.First(m => m.SensorSide == SensorSide.TopRight),
                bottomLeft: newSideToTiltModels.First(m => m.SensorSide == SensorSide.BottomLeft),
                bottomRight: newSideToTiltModels.First(m => m.SensorSide == SensorSide.BottomRight)));
        }

        private void UpdateTiltPlot(AutoFocusResult result, TiltPlaneModel tiltModel) {
            using (Scope.Enter()) {
                var scene = new Scene();

                var imageXs = new int[] { 0, tiltModel.ImageSize.Width - 1, 0, tiltModel.ImageSize.Width - 1 };
                var imageYs = new int[] { tiltModel.ImageSize.Height - 1, tiltModel.ImageSize.Height - 1, 0, 0 };

                var modelXs = imageXs.Select(x => tiltModel.GetModelX(x)).ToArray();
                var modelYs = imageYs.Select(y => tiltModel.GetModelY(y)).ToArray();
                var minFocuserPosition = double.PositiveInfinity;

                Array<double> points = zeros<double>(3, 4);
                Array<double> surfacePoints = zeros<double>(3, 4);
                for (int i = 0; i < 4; ++i) {
                    var imageX = imageXs[i];
                    var imageY = imageYs[i];
                    var modelX = modelXs[i];
                    var modelY = modelXs[i];
                    var modeledFocuserPosition = tiltModel.EstimateFocusPosition(imageX, imageY);
                    points[0, i] = modelX;
                    points[1, i] = modelY;
                    points[2, i] = result.RegionResults[i + 2].EstimatedFinalFocuserPosition;

                    surfacePoints[0, i] = modelX;
                    surfacePoints[1, i] = modelY;
                    surfacePoints[2, i] = modeledFocuserPosition;
                    minFocuserPosition = Math.Min(modeledFocuserPosition, minFocuserPosition);
                }
                for (int i = 0; i < 4; ++i) {
                    points[2, i] -= minFocuserPosition;
                    surfacePoints[2, i] -= minFocuserPosition;
                }

                var triStr = new TrianglesStrip();
                triStr.Positions.Update(tosingle(surfacePoints));
                triStr.Color = DrawingColor.FromArgb((byte)(0.8 * 255), DrawingColor.Gray);

                var plotCube = new PlotCube(twoDMode: false) {
                    Axes = {
                          XAxis = {
                              Label = {
                                Text = "",
                                    Visible = false
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
                                  Text = ""
                              },
                              Ticks = {
                                  Mode = TickMode.Manual
                              }
                          }
                      },
                    Rotation = Matrix4.Rotation(new Vector3(1, 0, 0), AstroUtil.ToRadians(75)) * Matrix4.Rotation(new Vector3(0, 0, 1), AstroUtil.ToRadians(15)),
                    Projection = Projection.Orthographic,
                    Children = {
                      new Points {
                        Positions = tosingle(points),
                        // TODO: Set primary brush color via WPF
                        // Color = primaryBrushColor.ToDrawingColor()
                      },
                      triStr
                    }
                };
                plotCube.Axes.XAxis.Ticks.Add(-1.0f, "Left");
                plotCube.Axes.XAxis.Ticks.Add(1.0f, "Right");
                plotCube.Axes.YAxis.Ticks.Add(1.0f, "Top");
                plotCube.Axes.YAxis.Ticks.Add(-1.0f, "Bottom");

                var dataScreen = plotCube.DataScreenRect;

                // TODO: Add PropertyChanged for Rotation. If Identity, set to starting point
                plotCube.AspectRatioMode = AspectRatioMode.StretchToFill;
                plotCube.AllowZoom = false;
                plotCube.AllowPan = false;

                scene.Add(plotCube);
                scene.Screen.First<ILNumerics.Drawing.Label>().Visible = false;
                var sceneContainer = new ILNSceneContainer(scene);
                sceneContainer.ForegroundChanged += (sender, args) => {
                    var solidColorBrush = args.Brush as SolidColorBrush;
                    if (solidColorBrush != null) {
                        var foregroundColor = solidColorBrush.Color.ToDrawingColor();
                        foreach (var lines in plotCube.Find<ILNLines>()) {
                            lines.Color = foregroundColor;
                        }
                        foreach (var label in plotCube.Find<ILNLabel>()) {
                            label.Color = foregroundColor;
                            label.Fringe.Width = 0;
                        }
                    }
                };

                TiltSceneContainer = sceneContainer;
            }
        }

        public void UpdateTiltModel(AutoFocusResult result) {
            var tiltModel = TiltPlaneModel.Create(result);
            UpdateTiltMeasurementsTable(result, tiltModel);
        }

        public AsyncObservableCollection<SensorTiltModel> SensorTiltModels { get; private set; }

        public AsyncObservableCollection<SensorTiltHistoryModel> SensorTiltHistoryModels { get; private set; }

        private ILNSceneContainer tiltSceneContainer;

        public ILNSceneContainer TiltSceneContainer {
            get => tiltSceneContainer;
            set {
                tiltSceneContainer = value;
                RaisePropertyChanged();
            }
        }

        public void Reset() {
            this.TiltSceneContainer = null;
            this.SensorTiltModels.Clear();
        }
    }

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum SensorSide {

        [Description("Center")]
        Center = 0,

        [Description("Top Left")]
        TopLeft = 1,

        [Description("Top Right")]
        TopRight = 2,

        [Description("Bottom Left")]
        BottomLeft = 3,

        [Description("Bottom Right")]
        BottomRight = 4
    }

    public class SensorTiltHistoryModel {

        public SensorTiltHistoryModel(
            int historyId,
            SensorTiltModel center,
            SensorTiltModel topLeft,
            SensorTiltModel topRight,
            SensorTiltModel bottomLeft,
            SensorTiltModel bottomRight) {
            HistoryId = historyId;
            Center = center;
            TopLeft = topLeft;
            TopRight = topRight;
            BottomLeft = bottomLeft;
            BottomRight = bottomRight;
        }

        public int HistoryId { get; private set; }
        public SensorTiltModel Center { get; private set; }
        public SensorTiltModel TopLeft { get; private set; }
        public SensorTiltModel TopRight { get; private set; }
        public SensorTiltModel BottomLeft { get; private set; }
        public SensorTiltModel BottomRight { get; private set; }
    }

    public class SensorTiltModel : BaseINPC {

        public SensorTiltModel(SensorSide sensorPosition) {
            this.SensorSide = sensorPosition;
        }

        public SensorSide SensorSide { get; private set; }

        private double focuserPosition;

        public double FocuserPosition {
            get => focuserPosition;
            set {
                focuserPosition = value;
                RaisePropertyChanged();
            }
        }

        private double adjustmentRequired;

        public double AdjustmentRequired {
            get => adjustmentRequired;
            set {
                adjustmentRequired = value;
                RaisePropertyChanged();
            }
        }

        private double rSquared;

        public double RSquared {
            get => rSquared;
            set {
                rSquared = value;
                RaisePropertyChanged();
            }
        }

        public override string ToString() {
            return $"{{{nameof(SensorSide)}={SensorSide.ToString()}, {nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(AdjustmentRequired)}={AdjustmentRequired.ToString()}, {nameof(RSquared)}={RSquared.ToString()}}}";
        }
    }
}