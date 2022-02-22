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

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class TiltModel : BaseINPC {
        private Dictionary<SensorSide> sideToTiltModelMap;
        private readonly AsyncObservableCollection<SensorTiltModel> sensorTiltModels;
        private readonly IApplicationDispatcher applicationDispatcher;

        public TiltModel(IApplicationDispatcher applicationDispatcher) {
            this.applicationDispatcher = applicationDispatcher;
            this.sensorTiltModels = new AsyncObservableCollection<SensorTiltModel>();
        }

        private (double, double, double) CalculateTiltPlane(AutoFocusResult result) {
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

            var topLeftFocuser = result.RegionResults[1].EstimatedFinalFocuserPosition;
            var topRightFocuser = result.RegionResults[2].EstimatedFinalFocuserPosition;
            var bottomLeftFocuser = result.RegionResults[3].EstimatedFinalFocuserPosition;
            var bottomRightFocuser = result.RegionResults[4].EstimatedFinalFocuserPosition;
            double[] outputs = { topLeftFocuser, topRightFocuser, bottomLeftFocuser, bottomRightFocuser };

            MultipleLinearRegression regression = ols.Learn(inputs, outputs);

            double a = regression.Weights[0];
            double b = regression.Weights[1];
            double c = regression.Intercept;
            return (a, b, c);
        }

        public void UpdateTiltModel(AutoFocusResult result) {
            var primaryBrushColor = applicationDispatcher.GetResource("PrimaryBrush", Colors.Black);

            var (a, b, c) = CalculateTiltPlane(result);
            var xyRatio = result.ImageSize.Width / result.ImageSize.Height;
            var topLeftPosition = a * (-xyRatio) + b * (1) + c;
            var topRightPosition = a * (xyRatio) + b * (1) + c;
            var bottomLeftPosition = a * (-xyRatio) + b * (-1) + c;
            var bottomRightPosition = a * (xyRatio) + b * (-1) + c;

            var leftPosition = (topLeftPosition + bottomLeftPosition) / 2;
            var rightPosition = (topRightPosition + bottomRightPosition) / 2;
            var topPosition = (topLeftPosition + topRightPosition) / 2;
            var bottomPosition = (bottomLeftPosition + bottomRightPosition) / 2;

            var leftAdjustment = (rightPosition + leftPosition) / 2 - leftPosition;
            var rightAdjustment = -leftAdjustment;
            var topAdjustment = (topPosition + bottomPosition) / 2 - topPosition;
            var bottomAdjustment = -topAdjustment;

            var newSideToTiltModelMap = new Dictionary<SensorSide, SensorTiltModel>();
            newSideToTiltModelMap.Add(SensorSide.Left, new SensorTiltModel(SensorSide.Left) {
                FocuserPosition = leftPosition,
                AdjustmentRequired = leftAdjustment,
                ImprovementDelta = double.NaN
            });
            newSideToTiltModelMap.Add(SensorSide.Right, new SensorTiltModel(SensorSide.Right) {
                FocuserPosition = rightPosition,
                AdjustmentRequired = rightAdjustment,
                ImprovementDelta = double.NaN
            });
            newSideToTiltModelMap.Add(SensorSide.Top, new SensorTiltModel(SensorSide.Top) {
                FocuserPosition = topPosition,
                AdjustmentRequired = topAdjustment,
                ImprovementDelta = double.NaN
            });
            newSideToTiltModelMap.Add(SensorSide.Bottom, new SensorTiltModel(SensorSide.Bottom) {
                FocuserPosition = bottomPosition,
                AdjustmentRequired = bottomAdjustment,
                ImprovementDelta = double.NaN
            });

            sensorTiltModels.Clear();
            foreach (var sensorTiltModel in newSideToTiltModelMap.OrderBy(kv => (int)kv.Key).Select(kv => kv.Value)) {
                sensorTiltModels.Add(sensorTiltModel);
            }

            using (Scope.Enter()) {
                var scene = new Scene();

                var xs = new double[] { -xyRatio, +xyRatio, -xyRatio, +xyRatio };
                var ys = new double[] { 1, 1, -1, -1 };
                var minFocuserPosition = double.PositiveInfinity;

                Array<double> points = zeros<double>(3, 4);
                Array<double> surfacePoints = zeros<double>(3, 4);
                for (int i = 0; i < 4; ++i) {
                    var x = xs[i];
                    var y = ys[i];
                    var modeledFocuserPosition = x * a + y * b + c;
                    points[0, i] = x;
                    points[1, i] = y;
                    points[2, i] = result.RegionResults[i + 1].EstimatedFinalFocuserPosition;

                    surfacePoints[0, i] = x;
                    surfacePoints[1, i] = y;
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
                        Color = primaryBrushColor.ToDrawingColor()
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

        public AsyncObservableCollection<SensorTiltModel> SensorTiltModels => sensorTiltModels;

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

        [Description("Left")]
        Left = 0,

        [Description("Right")]
        Right = 1,

        [Description("Top")]
        Top = 2,

        [Description("Bottom")]
        Bottom = 3
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

        private double improvementDelta;

        public double ImprovementDelta {
            get => improvementDelta;
            set {
                improvementDelta = value;
                RaisePropertyChanged();
            }
        }

        public override string ToString() {
            return $"{{{nameof(SensorSide)}={SensorSide.ToString()}, {nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(AdjustmentRequired)}={AdjustmentRequired.ToString()}, {nameof(ImprovementDelta)}={ImprovementDelta.ToString()}}}";
        }
    }
}