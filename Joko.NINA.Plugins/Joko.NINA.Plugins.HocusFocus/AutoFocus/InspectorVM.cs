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
using DrawingColor = System.Drawing.Color;
using static ILNumerics.ILMath;

using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Controls;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel;
using NINA.WPF.Base.ViewModel.AutoFocus;
using OxyPlot;
using OxyPlot.Series;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using Logger = NINA.Core.Utility.Logger;
using DrawingSize = System.Drawing.Size;
using SPPlot = ScottPlot.Plot;
using SPVector2 = ScottPlot.Statistics.Vector2;
using ILNumerics.Drawing.Plotting;
using System.Drawing;
using Accord.Math.Optimization.Losses;
using Accord.Statistics.Models.Regression.Linear;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    [Export(typeof(IDockableVM))]
    public class InspectorVM : DockableVM, IScottPlotController {
        private static readonly FocusPointComparer focusPointComparer = new FocusPointComparer();
        private static readonly PlotPointComparer plotPointComparer = new PlotPointComparer();

        private readonly IStarDetectionOptions starDetectionOptions;
        private readonly IStarAnnotatorOptions starAnnotatorOptions;
        private readonly IInspectorOptions inspectorOptions;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly ICameraMediator cameraMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IAutoFocusEngineFactory autoFocusEngineFactory;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        private readonly IProgress<ApplicationStatus> progress;

        [ImportingConstructor]
        public InspectorVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            IImagingMediator imagingMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            IFilterWheelMediator filterWheelMediator,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector)
            : this(profileService, applicationStatusMediator, imagingMediator, cameraMediator, focuserMediator, filterWheelMediator, HocusFocusPlugin.StarDetectionOptions, HocusFocusPlugin.StarAnnotatorOptions, HocusFocusPlugin.InspectorOptions, HocusFocusPlugin.AutoFocusEngineFactory, starDetectionSelector, starAnnotatorSelector) {
        }

        public InspectorVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            IImagingMediator imagingMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            IFilterWheelMediator filterWheelMediator,
            IStarDetectionOptions starDetectionOptions,
            IStarAnnotatorOptions starAnnotatorOptions,
            IInspectorOptions inspectorOptions,
            IAutoFocusEngineFactory autoFocusEngineFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector) : base(profileService) {
            this.applicationStatusMediator = applicationStatusMediator;
            this.imagingMediator = imagingMediator;
            this.cameraMediator = cameraMediator;
            this.focuserMediator = focuserMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.starDetectionOptions = starDetectionOptions;
            this.starAnnotatorOptions = starAnnotatorOptions;
            this.inspectorOptions = inspectorOptions;
            this.autoFocusEngineFactory = autoFocusEngineFactory;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
            this.progress = ProgressFactory.Create(applicationStatusMediator, "Aberration Inspector");

            this.Title = "Aberration Inspector";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);

            CenterFocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
            OutsideFocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
            RegionPlotFocusPoints = Enumerable.Range(0, 5).Select(i => new AsyncObservableCollection<DataPoint>()).ToArray();
            RegionFinalFocusPoints = new AsyncObservableCollection<DataPoint>(Enumerable.Range(0, 5).Select(i => new DataPoint(-1.0d, 0.0d)));
            RegionCurveFittings = new AsyncObservableCollection<Func<double, double>>(Enumerable.Range(0, 5).Select(i => (Func<double, double>)null));
            RegionLineFittings = new AsyncObservableCollection<TrendlineFitting>(Enumerable.Range(0, 5).Select(i => (TrendlineFitting)null));
            TiltModel = new TiltModel();

            // TODO: Change logo
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["HocusFocusAnnotateStarsSVG"];
            ImageGeometry.Freeze();

            RunAutoFocusAnalysisCommand = new AsyncCommand<bool>(AnalyzeAutoFocus, canExecute: (o) => !AnalysisRunning() && CameraConnected() && FocuserConnected());
            RunExposureAnalysisCommand = new AsyncCommand<bool>(AnalyzeExposure, canExecute: (o) => !AnalysisRunning() && CameraConnected());
            RerunSavedAutoFocusAnalysisCommand = new AsyncCommand<bool>(AnalyzeSavedAutoFocusRun, canExecute: (o) => !AnalysisRunning());
            CancelAnalyzeCommand = new RelayCommand(CancelAnalyze);
        }

        private bool AnalysisRunning() {
            var localAnalyzeTask = analyzeTask;
            if (localAnalyzeTask == null) {
                return false;
            }

            return analyzeTask.Status < TaskStatus.RanToCompletion;
        }

        private bool CameraConnected() {
            return cameraMediator.GetInfo().Connected;
        }

        private bool FocuserConnected() {
            return focuserMediator.GetInfo().Connected;
        }

        public override bool IsTool { get; } = true;

        private CancellationTokenSource analyzeCts;
        private Task<bool> analyzeTask;

        private async Task<bool> AnalyzeAutoFocus() {
            var localAnalyzeTask = analyzeTask;
            if (analyzeTask != null && !analyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }

            analyzeCts?.Cancel();
            var localAnalyzeCts = new CancellationTokenSource();
            analyzeCts = localAnalyzeCts;

            localAnalyzeTask = Task.Run(async () => {
                var one_third = 1.0d / 3.0d;
                var two_thirds = 2.0d / 3.0d;
                var regions = new List<StarDetectionRegion>() {
                new StarDetectionRegion(new RatioRect(one_third, one_third, one_third, one_third)),
                new StarDetectionRegion(new RatioRect(0, 0, one_third, one_third)),
                new StarDetectionRegion(new RatioRect(two_thirds, one_third, one_third, one_third)),
                new StarDetectionRegion(new RatioRect(0, two_thirds, one_third, one_third)),
                new StarDetectionRegion(new RatioRect(two_thirds, two_thirds, one_third, one_third))
            };
                var autoFocusEngine = autoFocusEngineFactory.Create();
                autoFocusEngine.Started += AutoFocusEngine_Started;
                autoFocusEngine.IterationFailed += AutoFocusEngine_IterationFailed;
                autoFocusEngine.Completed += AutoFocusEngine_Completed;
                autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;

                var imagingFilter = GetImagingFilter();
                var options = autoFocusEngine.GetOptions();
                if (inspectorOptions.FramesPerPoint > 0) {
                    options.FramesPerPoint = inspectorOptions.FramesPerPoint;
                }
                if (inspectorOptions.StepCount > 0) {
                    options.AutoFocusInitialOffsetSteps = inspectorOptions.StepCount;
                }
                if (inspectorOptions.StepSize > 0) {
                    options.AutoFocusStepSize = inspectorOptions.StepSize;
                }
                if (inspectorOptions.TimeoutSeconds > 0) {
                    options.AutoFocusTimeout = TimeSpan.FromSeconds(inspectorOptions.TimeoutSeconds);
                }

                var result = await autoFocusEngine.RunWithRegions(options, imagingFilter, regions, analyzeCts.Token, this.progress);
                return await AnalyzeAutoFocusResult(result);
            });
            analyzeTask = localAnalyzeTask;

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection analysis cancelled");
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Inspection analysis failed: {e.Message}");
                Logger.Error("Inspection analysis failed", e);
                return false;
            }
        }

        private FilterInfo GetImagingFilter() {
            var filterInfo = filterWheelMediator.GetInfo();
            FilterInfo imagingFilter = null;
            if (filterInfo?.SelectedFilter != null) {
                imagingFilter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Where(x => x.Position == filterInfo.SelectedFilter.Position).FirstOrDefault();
            }
            return imagingFilter;
        }

        private async Task<bool> AnalyzeAutoFocusResult(AutoFocusResult result) {
            if (!result.Succeeded) {
                Notification.ShowError($"Inspection analysis failed");
                Logger.Error("Inspection analysis failed, due to failed AutoFocus");
                return false;
            }

            TiltModel.UpdateTiltModel(result);
            return true;
        }

        private async Task<bool> TakeAndAnalyzeExposure(IAutoFocusEngine autoFocusEngine, CancellationToken token) {
            var starDetection = starDetectionSelector.GetBehavior() as IHocusFocusStarDetection;
            if (starDetection == null) {
                Notification.ShowError("HocusFocus must be selected as the Star Detector. Change this option in Options -> Image Options");
                Logger.Error("HocusFocus must be selected as the Star Detector");
                return false;
            }

            var imagingFilter = GetImagingFilter();
            var autoFocusOptions = autoFocusEngine.GetOptions();

            try {
                var autoFocusFilter = await autoFocusEngine.SetAutofocusFilter(imagingFilter, token, this.progress);
                double autoFocusExposureTime = profileService.ActiveProfile.FocuserSettings.AutoFocusExposureTime;
                if (imagingFilter != null && imagingFilter.AutoFocusExposureTime > -1) {
                    autoFocusExposureTime = imagingFilter.AutoFocusExposureTime;
                }

                var exposureTimeSeconds = inspectorOptions.SimpleExposureSeconds >= 0 ? inspectorOptions.SimpleExposureSeconds : autoFocusExposureTime;
                var captureSequence = new CaptureSequence(exposureTimeSeconds, CaptureSequence.ImageTypes.SNAPSHOT, autoFocusFilter, null, 1);
                var exposureData = await imagingMediator.CaptureImage(captureSequence, token, progress);
                var prepareParameters = new PrepareImageParameters(autoStretch: false, detectStars: false);
                var imageData = await imagingMediator.PrepareImage(exposureData, prepareParameters, token);

                var analysisParams = new StarDetectionParams() {
                    Sensitivity = profileService.ActiveProfile.ImageSettings.StarSensitivity,
                    NoiseReduction = profileService.ActiveProfile.ImageSettings.NoiseReduction,
                    NumberOfAFStars = autoFocusOptions.NumberOfAFStars,
                    IsAutoFocus = false
                };
                var hfParams = starDetection.ToHocusFocusParams(analysisParams);
                var starDetectorParams = starDetection.GetStarDetectorParams(imageData, StarDetectionRegion.Full, true);
                if (!starDetectorParams.ModelPSF) {
                    Notification.ShowInformation("Enabling PSF modeling for analysis");
                    starDetectorParams.ModelPSF = true;
                }

                var imageProperties = imageData.RawImageData.Properties;
                var imageSize = new DrawingSize(width: imageProperties.Width, height: imageProperties.Height);
                var analysisResult = await starDetection.Detect(imageData, hfParams, starDetectorParams, this.progress, token);
                AnalyzeStarDetectionResult(imageSize, analysisResult);
                return true;
            } finally {
                var completionOperationTimeout = TimeSpan.FromMinutes(1);
                if (imagingFilter != null) {
                    try {
                        var completionTimeoutCts = new CancellationTokenSource(completionOperationTimeout);
                        await filterWheelMediator.ChangeFilter(imagingFilter, completionTimeoutCts.Token);
                    } catch (Exception e) {
                        Logger.Error("Failed to restore previous filter position after analysis", e);
                        Notification.ShowError($"Failed to restore previous filter position: {e.Message}");
                    }
                }
            }
        }

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

        private void AnalyzeStarDetectionResult(DrawingSize imageSize, StarDetectionResult result) {
            var numRegionsWide = inspectorOptions.NumRegionsWide;
            int regionSizePixels = imageSize.Width / numRegionsWide;
            int numRegionsTall = imageSize.Height / regionSizePixels;
            numRegionsTall += numRegionsTall % 2 == 0 ? 1 : 0;
            var regionDetectedStars = new List<HocusFocusDetectedStar>[numRegionsWide, numRegionsTall];
            for (int i = 0; i < numRegionsWide; ++i) {
                for (int j = 0; j < numRegionsTall; ++j) {
                    regionDetectedStars[i, j] = new List<HocusFocusDetectedStar>();
                }
            }

            foreach (var detectedStar in result.StarList.Cast<HocusFocusDetectedStar>()) {
                if (detectedStar.PSF == null) {
                    continue;
                }

                var regionRow = (int)Math.Floor(detectedStar.Position.Y / imageSize.Height * numRegionsTall);
                var regionCol = (int)Math.Floor(detectedStar.Position.X / imageSize.Width * numRegionsWide);
                regionDetectedStars[regionCol, regionRow].Add(detectedStar);
            }

            using (Scope.Enter()) {
                var scene = new Scene();

                Array<float> points = zeros<float>(numRegionsTall, numRegionsWide);
                for (int regionRow = 0; regionRow < numRegionsTall; ++regionRow) {
                    for (int regionCol = 0; regionCol < numRegionsWide; ++regionCol) {
                        var detectedStars = regionDetectedStars[regionCol, regionRow];
                        if (detectedStars.Count == 0) {
                            points[regionRow, regionCol] = float.NaN;
                            continue;
                        }
                        var (fwhmMedian, _) = detectedStars.Select(s => s.PSF.FWHMArcsecs).MedianMAD();
                        points[regionRow, regionCol] = (float)fwhmMedian;
                    }
                }

                var contourPlot = new ContourPlot(points, colormap: Colormaps.Gray, create3D: true, lineWidth: 2, showLabels: false);
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
                                  Text = "FWHM"
                              },
                              Ticks = {
                                  Mode = TickMode.Manual
                              }
                          }
                      },
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
                    }
                };

                contourPlotCube.AspectRatioMode = AspectRatioMode.MaintainRatios;
                scene.Add(contourPlotCube);
                scene.Screen.First<Label>().Visible = false;
                FWHMContourScene = scene;
            }

            {
                var plot = new SPPlot();
                double[] xs = DataGen.Range(0, numRegionsWide);
                double[] ys = DataGen.Range(0, numRegionsTall);
                var vectors = new SPVector2[numRegionsWide, numRegionsTall];

                for (int regionRow = 0; regionRow < numRegionsTall; ++regionRow) {
                    for (int regionCol = 0; regionCol < numRegionsWide; ++regionCol) {
                        var detectedStars = regionDetectedStars[regionCol, regionRow];

                        if (detectedStars.Count > 0) {
                            var (eccentricityMedian, _) = detectedStars.Select(s => s.PSF.Eccentricity).MedianMAD();
                            var (psfRotationMedian, _) = detectedStars.Select(s => s.PSF.ThetaRadians).MedianMAD();
                            double x = Math.Cos(psfRotationMedian) * eccentricityMedian;
                            double y = Math.Sin(psfRotationMedian) * eccentricityMedian;
                            vectors[regionCol, regionRow] = new SPVector2(x, y);
                        }
                    }
                }

                // TODO: Add toggle for grayscale
                var vectorField = plot.AddVectorField(vectors, xs, ys, colormap: ScottPlot.Drawing.Colormap.Dense);
                vectorField.ScaledArrowheads = true;
                vectorField.ScaledArrowheadLength = 0.0;
                vectorField.ScaledArrowheadWidth = 0.0;

                var colorBar = plot.AddColorbar(ScottPlot.Drawing.Colormap.Dense);
                colorBar.MinValue = 0.0;
                colorBar.MaxValue = 1.0;
                plot.XAxis.Ticks(false);
                plot.YAxis.Ticks(false);

                EccentricityVectorPlot = plot;
            }
        }

        private async Task<bool> AnalyzeExposure() {
            var localAnalyzeTask = analyzeTask;
            if (analyzeTask != null && !analyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }

            analyzeCts?.Cancel();
            var localAnalyzeCts = new CancellationTokenSource();
            analyzeCts = localAnalyzeCts;

            var autoFocusEngine = autoFocusEngineFactory.Create();
            localAnalyzeTask = Task.Run(() => TakeAndAnalyzeExposure(autoFocusEngine, analyzeCts.Token));
            analyzeTask = localAnalyzeTask;

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection exposure analysis cancelled");
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Inspection exposure analysis failed: {e.Message}");
                Logger.Error("Inspection exposure analysis failed", e);
                return false;
            }
        }

        private async Task<bool> AnalyzeSavedAutoFocusRun() {
            var localAnalyzeTask = analyzeTask;
            if (analyzeTask != null && !analyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }

            analyzeCts?.Cancel();
            var localAnalyzeCts = new CancellationTokenSource();
            analyzeCts = localAnalyzeCts;

            localAnalyzeTask = Task.Run(async () => {
                return true;
            });
            analyzeTask = localAnalyzeTask;

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection exposure analysis cancelled");
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Inspection exposure analysis failed: {e.Message}");
                Logger.Error("Inspection exposure analysis failed", e);
                return false;
            }
        }

        private void AutoFocusEngine_Started(object sender, AutoFocusStartedEventArgs e) {
            this.ClearAnalysis();
        }

        private void AutoFocusEngine_MeasurementPointCompleted(object sender, AutoFocusMeasurementPointCompletedEventArgs e) {
            RegionCurveFittings[e.RegionIndex] = GetCurveFitting(e.Fittings);
            RegionLineFittings[e.RegionIndex] = GetLineFitting(e.Fittings);
            RegionPlotFocusPoints[e.RegionIndex].AddSorted(new DataPoint(e.FocuserPosition, e.Measurement.Measure), plotPointComparer);

            var focusPoints = e.RegionIndex == 0 ? CenterFocusPoints : OutsideFocusPoints;
            focusPoints.AddSorted(new ScatterErrorPoint(e.FocuserPosition, e.Measurement.Measure, 0, Math.Max(0.001, e.Measurement.Stdev)), focusPointComparer);
        }

        private void AutoFocusEngine_Completed(object sender, AutoFocusCompletedEventArgs e) {
            Notification.ShowInformation("Aberration Inspection Complete");
            var reportBuilder = new StringBuilder();
            var centerHFR = e.RegionHFRs[0].EstimatedFinalHFR;
            var centerFocuser = e.RegionHFRs[0].EstimatedFinalFocuserPosition;
            RegionFinalFocusPoints[0] = new DataPoint(e.RegionHFRs[0].EstimatedFinalFocuserPosition, e.RegionHFRs[0].EstimatedFinalHFR);
            reportBuilder.AppendLine($"Center - HFR: {centerHFR}, Focuser: {centerFocuser}");

            for (int i = 1; i < e.RegionHFRs.Count; ++i) {
                var regionName = GetRegionName(i);
                var regionHFR = e.RegionHFRs[i];
                reportBuilder.AppendLine($"{regionName} - HFR Delta: {regionHFR.EstimatedFinalHFR - centerHFR}, Focuser Delta: {regionHFR.EstimatedFinalFocuserPosition - centerFocuser}");

                RegionFinalFocusPoints[i] = new DataPoint(e.RegionHFRs[i].EstimatedFinalFocuserPosition, e.RegionHFRs[i].EstimatedFinalHFR);
            }
            Logger.Info(reportBuilder.ToString());
        }

        private string GetRegionName(int regionIndex) {
            if (regionIndex == 0) {
                return "Center";
            } else if (regionIndex == 1) {
                return "Top Left";
            } else if (regionIndex == 2) {
                return "Top Right";
            } else if (regionIndex == 3) {
                return "Bottom Left";
            } else if (regionIndex == 4) {
                return "Bottom Right";
            }
            throw new ArgumentException($"{regionIndex} is not a valid region index", "regionIndex");
        }

        private void AutoFocusEngine_IterationFailed(object sender, AutoFocusIterationFailedEventArgs e) {
            ClearPlots();
        }

        private void ClearAnalysis() {
            ClearPlots();
        }

        private void ClearPlots() {
            for (int i = 0; i < RegionCurveFittings.Count; ++i) {
                RegionCurveFittings[i] = null;
                RegionLineFittings[i] = null;
                RegionPlotFocusPoints[i].Clear();
            }
            CenterFocusPoints.Clear();
            OutsideFocusPoints.Clear();
        }

        private void CancelAnalyze(object o) {
            analyzeCts?.Cancel();
        }

        public ICommand RunAutoFocusAnalysisCommand { get; private set; }
        public ICommand RerunSavedAutoFocusAnalysisCommand { get; private set; }
        public ICommand RunExposureAnalysisCommand { get; private set; }
        public ICommand CancelAnalyzeCommand { get; private set; }

        public AsyncObservableCollection<ScatterErrorPoint> CenterFocusPoints { get; private set; }
        public AsyncObservableCollection<ScatterErrorPoint> OutsideFocusPoints { get; private set; }
        public AsyncObservableCollection<DataPoint>[] RegionPlotFocusPoints { get; private set; }
        public AsyncObservableCollection<DataPoint> RegionFinalFocusPoints { get; private set; }
        public AsyncObservableCollection<Func<double, double>> RegionCurveFittings { get; private set; }
        public AsyncObservableCollection<TrendlineFitting> RegionLineFittings { get; private set; }
        public TiltModel TiltModel { get; private set; }
        public IInspectorOptions InspectorOptions => this.inspectorOptions;

        private TrendlineFitting GetLineFitting(AutoFocusFitting fitting) {
            if (fitting.Method == AFMethodEnum.CONTRASTDETECTION) {
                if (fitting.CurveFittingType == AFCurveFittingEnum.TRENDPARABOLIC || fitting.CurveFittingType == AFCurveFittingEnum.TRENDHYPERBOLIC || fitting.CurveFittingType == AFCurveFittingEnum.TRENDLINES) {
                    return fitting.TrendlineFitting;
                }
            }
            return null;
        }

        private Func<double, double> GetCurveFitting(AutoFocusFitting fitting) {
            if (fitting.Method == AFMethodEnum.CONTRASTDETECTION) {
                return fitting.GaussianFitting?.Fitting ?? null;
            } else if (fitting.CurveFittingType == AFCurveFittingEnum.PARABOLIC || fitting.CurveFittingType == AFCurveFittingEnum.TRENDPARABOLIC) {
                return fitting.QuadraticFitting?.Fitting ?? null;
            } else if (fitting.CurveFittingType == AFCurveFittingEnum.HYPERBOLIC || fitting.CurveFittingType == AFCurveFittingEnum.TRENDHYPERBOLIC) {
                return fitting.HyperbolicFitting?.Fitting ?? null;
            } else {
                return null;
            }
        }

        public void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
            // throw new NotImplementedException();
        }

        public void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            // throw new NotImplementedException();
        }

        private bool autoFocusAnalysisProgressOrResult;

        public bool AutoFocusAnalysisProgressOrResult {
            get => autoFocusAnalysisProgressOrResult;
            private set {
                autoFocusAnalysisProgressOrResult = value;
                RaisePropertyChanged();
            }
        }

        private bool autoFocusAnalysisResult;

        public bool AutoFocusAnalysisResult {
            get => autoFocusAnalysisResult;
            private set {
                autoFocusAnalysisResult = value;
                RaisePropertyChanged();
            }
        }

        private bool exposureAnalysisResult;

        public bool ExposureAnalysisResult {
            get => exposureAnalysisResult;
            private set {
                exposureAnalysisResult = value;
                RaisePropertyChanged();
            }
        }

        private Scene fwhmContourScene = new Scene(true);

        public Scene FWHMContourScene {
            get => fwhmContourScene;
            set {
                fwhmContourScene = value;
                RaisePropertyChanged();
            }
        }

        public event EventHandler PlotRefreshed;

        private SPPlot eccentricityVectorPlot;

        public SPPlot EccentricityVectorPlot {
            get => eccentricityVectorPlot;
            set {
                eccentricityVectorPlot = value;
                RaisePropertyChanged();
            }
        }
    }
}