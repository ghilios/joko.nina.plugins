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
using DrawingColor = System.Drawing.Color;
using MediaBrush = System.Windows.Media.Brush;
using SPPlot = ScottPlot.Plot;
using SPVector2 = ScottPlot.Statistics.Vector2;
using ILNumerics.Drawing.Plotting;
using ILNLines = ILNumerics.Drawing.Lines;
using ILNLabel = ILNumerics.Drawing.Label;
using System.Drawing;
using ScottPlot.Statistics;
using System.Windows.Media;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    [Export(typeof(IDockableVM))]
    public class InspectorVM : DockableVM, IScottPlotController, ICameraConsumer, IFocuserConsumer {
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
        private readonly IAutoFocusOptions autoFocusOptions;
        private readonly IAutoFocusEngineFactory autoFocusEngineFactory;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        private readonly IApplicationDispatcher applicationDispatcher;
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
            : this(profileService, applicationStatusMediator, imagingMediator, cameraMediator, focuserMediator, filterWheelMediator, HocusFocusPlugin.StarDetectionOptions, HocusFocusPlugin.StarAnnotatorOptions, HocusFocusPlugin.InspectorOptions, HocusFocusPlugin.AutoFocusOptions, HocusFocusPlugin.AutoFocusEngineFactory, starDetectionSelector, starAnnotatorSelector, HocusFocusPlugin.ApplicationDispatcher) {
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
            IAutoFocusOptions autoFocusOptions,
            IAutoFocusEngineFactory autoFocusEngineFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector,
            IApplicationDispatcher applicationDispatcher) : base(profileService) {
            this.applicationStatusMediator = applicationStatusMediator;
            this.imagingMediator = imagingMediator;
            this.cameraMediator = cameraMediator;
            this.focuserMediator = focuserMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.starDetectionOptions = starDetectionOptions;
            this.starAnnotatorOptions = starAnnotatorOptions;
            this.inspectorOptions = inspectorOptions;
            this.autoFocusOptions = autoFocusOptions;
            this.autoFocusEngineFactory = autoFocusEngineFactory;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
            this.applicationDispatcher = applicationDispatcher;
            this.progress = ProgressFactory.Create(applicationStatusMediator, "Aberration Inspector");

            this.cameraMediator.RegisterConsumer(this);
            this.focuserMediator.RegisterConsumer(this);

            this.Title = "Aberration Inspector";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);

            CenterFocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
            OutsideFocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
            RegionPlotFocusPoints = Enumerable.Range(0, 5).Select(i => new AsyncObservableCollection<DataPoint>()).ToArray();
            RegionFinalFocusPoints = new AsyncObservableCollection<DataPoint>(Enumerable.Range(0, 5).Select(i => new DataPoint(-1.0d, 0.0d)));
            RegionCurveFittings = new AsyncObservableCollection<Func<double, double>>(Enumerable.Range(0, 5).Select(i => (Func<double, double>)null));
            RegionLineFittings = new AsyncObservableCollection<TrendlineFitting>(Enumerable.Range(0, 5).Select(i => (TrendlineFitting)null));
            TiltModel = new TiltModel(applicationDispatcher);

            // TODO: Change logo
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["InspectorSVG"];
            ImageGeometry.Freeze();

            RunAutoFocusAnalysisCommand = new AsyncCommand<bool>(AnalyzeAutoFocus, canExecute: (o) => !AnalysisRunning() && CameraInfo.Connected && FocuserInfo.Connected);
            RunExposureAnalysisCommand = new AsyncCommand<bool>(AnalyzeExposure, canExecute: (o) => !AnalysisRunning() && CameraInfo.Connected);
            RerunSavedAutoFocusAnalysisCommand = new AsyncCommand<bool>(AnalyzeSavedAutoFocusRun, canExecute: (o) => !AnalysisRunning());
            ClearAnalysesCommand = new RelayCommand(ClearAnalyses, canExecute: (o) => !AnalysisRunning());
            CancelAnalyzeCommand = new RelayCommand(CancelAnalyze);
        }

        private bool AnalysisRunning() {
            var localAnalyzeTask = analyzeTask;
            if (localAnalyzeTask == null) {
                return false;
            }

            return analyzeTask.Status < TaskStatus.RanToCompletion;
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

                ActivateAutoFocusChart();
                DeactivateExposureAnalysis();
                var result = await autoFocusEngine.RunWithRegions(options, imagingFilter, regions, analyzeCts.Token, this.progress);
                var autoFocusAnalysisResult = await AnalyzeAutoFocusResult(result);
                ActivateTiltMeasurement();
                if (!autoFocusAnalysisResult) {
                    Notification.ShowError("AutoFocus Analysis Failed");
                    return false;
                }
                var exposureAnalysisResult = await TakeAndAnalyzeExposureImpl(autoFocusEngine, analyzeCts.Token);
                if (!exposureAnalysisResult) {
                    Notification.ShowError("Exposure Analysis Failed");
                    return false;
                }
                ActivateExposureAnalysis();
                return true;
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

        private Task<bool> AnalyzeAutoFocusResult(AutoFocusResult result) {
            if (!result.Succeeded) {
                Notification.ShowError($"Inspection analysis failed");
                Logger.Error("Inspection analysis failed, due to failed AutoFocus");
                return Task.FromResult(false);
            }

            TiltModel.UpdateTiltModel(result);
            return Task.FromResult(true);
        }

        private Task<bool> TakeAndAnalyzeExposure(IAutoFocusEngine autoFocusEngine, CancellationToken token) {
            var starDetection = starDetectionSelector.GetBehavior() as IHocusFocusStarDetection;
            if (starDetection == null) {
                Notification.ShowError("HocusFocus must be selected as the Star Detector. Change this option in Options -> Image Options");
                Logger.Error("HocusFocus must be selected as the Star Detector");
                return Task.FromResult(false);
            }

            DeactivateAutoFocusAnalysis();
            return TakeAndAnalyzeExposureImpl(autoFocusEngine, token);
        }

        private async Task<bool> TakeAndAnalyzeExposureImpl(IAutoFocusEngine autoFocusEngine, CancellationToken token) {
            var starDetection = (IHocusFocusStarDetection)starDetectionSelector.GetBehavior();
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
                    starDetectorParams.ModelPSF = true;
                }

                var imageProperties = imageData.RawImageData.Properties;
                var imageSize = new DrawingSize(width: imageProperties.Width, height: imageProperties.Height);
                var analysisResult = await starDetection.Detect(imageData, hfParams, starDetectorParams, this.progress, token);
                AnalyzeStarDetectionResult(imageSize, analysisResult);
                ActivateExposureAnalysis();
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

        private DrawingColor GetColorFromBrushResource(string resourceName, DrawingColor fallback) {
            return applicationDispatcher.DispatchSynchronizationContext(() => {
                var resource = Application.Current.TryFindResource(resourceName);
                if (resource is SolidColorBrush) {
                    return ((SolidColorBrush)resource).Color.ToDrawingColor();
                }
                return fallback;
            });
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

            // TODO: Add FWHM Contour Map back after the graphic can better be supported
            /*
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

                var contourPlot = new ContourPlot(points, colormap: Colormaps.Gray, create3D: true, lineWidth: 1, showLabels: false);
                var contourSurface = new Surface(points, colormap: Colormaps.Gray) {
                    Wireframe = { Visible = false },
                    UseLighting = true,
                    Children = {
                              new Colorbar {
                                  Location = new PointF(1,.4f),
                                  Anchor = new PointF(1,0)
                              }
                          }
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
                                  Text = "FWHM"
                              },
                              Ticks = {
                                  Mode = TickMode.Auto
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
                contourPlotCube.DataScreenRect = new RectangleF(0, 0, 0.9f, 0.9f);
                scene.Add(contourPlotCube);
                scene.Screen.First<Label>().Visible = false;
                var sceneContainer = new ILNSceneContainer(scene);
                sceneContainer.ForegroundChanged += (sender, args) => {
                    var solidColorBrush = args.Brush as SolidColorBrush;
                    if (solidColorBrush != null) {
                        var foregroundColor = solidColorBrush.Color.ToDrawingColor();
                        foreach (var lines in contourPlot.Find<ILNLines>()) {
                            lines.Color = foregroundColor;
                        }
                        foreach (var label in contourPlot.Find<ILNLabel>()) {
                            label.Color = foregroundColor;
                            label.Fringe.Width = 0;
                        }
                    }
                };

                FWHMContourSceneContainer = sceneContainer;
            }
            */

            {
                double[] xs = DataGen.Range(0, numRegionsWide);
                double[] ys = DataGen.Range(0, numRegionsTall);
                var vectors = new SPVector2[numRegionsWide, numRegionsTall];
                var centerXs = new double[numRegionsWide * numRegionsTall];
                var centerYs = new double[numRegionsWide * numRegionsTall];
                var eccentricities = new double[numRegionsWide * numRegionsTall];

                int pointIndex = 0;
                double maxMagnitude = 0.0d;
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
                            maxMagnitude = Math.Max(scaledEccentricity, maxMagnitude);
                        } else {
                            eccentricities[pointIndex] = double.NaN;
                        }
                        centerXs[pointIndex] = regionCol;
                        centerYs[pointIndex++] = regionRow;
                    }
                }

                var backgroundColor = GetColorFromBrushResource("BackgroundBrush", DrawingColor.White);
                var secondaryBackgroundColor = GetColorFromBrushResource("SecondaryBackgroundBrush", DrawingColor.Gray);
                var primaryColor = GetColorFromBrushResource("PrimaryBrush", DrawingColor.Black);
                var secondaryColor = GetColorFromBrushResource("SecondaryBrush", DrawingColor.Red);

                var plot = new SPPlot();
                plot.Style(dataBackground: secondaryBackgroundColor, figureBackground: backgroundColor, tick: secondaryColor, grid: secondaryColor, axisLabel: primaryColor, titleLabel: primaryColor);

                // maxMagnitude * 1.2 is taken from the ScottPlot code to ensure no vector scaling takes place
                var vectorField = plot.AddVectorField(vectors, xs, ys, color: primaryColor, scaleFactor: maxMagnitude * 1.2 * 1.5);

                // Scatter points act as anchor points for mouse over events
                var scatterPoints = plot.AddScatterPoints(centerXs, centerYs);
                scatterPoints.IsVisible = false;

                vectorField.ScaledArrowheadLength = 0;
                vectorField.ScaledArrowheadWidth = 0;
                vectorField.ScaledArrowheads = false;
                vectorField.Anchor = ArrowAnchor.Center;

                var highlightedPoint = plot.AddPoint(0, 0);

                highlightedPoint.Color = secondaryColor;
                highlightedPoint.MarkerSize = 7;
                highlightedPoint.MarkerShape = ScottPlot.MarkerShape.filledCircle;
                highlightedPoint.IsVisible = false;
                highlightedPoint.TextFont.Color = primaryColor;
                highlightedPoint.TextFont.Bold = true;

                plot.XAxis.Ticks(false);
                plot.YAxis.Ticks(false);

                lastHighlightedEccentricityPointIndex = -1;
                highlightedEccentricityPoint = highlightedPoint;
                eccentricityCenterPoints = scatterPoints;
                eccentricityValues = eccentricities;
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
            localAnalyzeTask = Task.Run(async () => {
                bool lastResult = false;
                do {
                    lastResult = await TakeAndAnalyzeExposure(autoFocusEngine, analyzeCts.Token);
                } while (LoopingExposureAnalysis && !analyzeCts.Token.IsCancellationRequested);
                return lastResult;
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

        private async Task<bool> AnalyzeSavedAutoFocusRun() {
            var localAnalyzeTask = analyzeTask;
            if (analyzeTask != null && !analyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }

            analyzeCts?.Cancel();
            var localAnalyzeCts = new CancellationTokenSource();
            analyzeCts = localAnalyzeCts;

            var autoFocusEngine = autoFocusEngineFactory.Create();
            string selectedPath = "";
            SavedAutoFocusAttempt savedAttempt;
            try {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                    if (!String.IsNullOrEmpty(autoFocusOptions.LastSelectedLoadPath)) {
                        dialog.SelectedPath = autoFocusOptions.LastSelectedLoadPath;
                    }
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
                        return false;
                    }

                    selectedPath = dialog.SelectedPath;
                    autoFocusOptions.LastSelectedLoadPath = selectedPath;
                }

                savedAttempt = autoFocusEngine.LoadSavedAutoFocusAttempt(selectedPath);
            } catch (Exception e) {
                Notification.ShowError(e.Message);
                Logger.Error($"Failed to load saved auto focus attempt from {selectedPath}");
                return false;
            }

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

                autoFocusEngine.Started += AutoFocusEngine_Started;
                autoFocusEngine.IterationFailed += AutoFocusEngine_IterationFailed;
                autoFocusEngine.Completed += AutoFocusEngine_Completed;
                autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;

                var imagingFilter = GetImagingFilter();
                var options = autoFocusEngine.GetOptions();
                if (inspectorOptions.TimeoutSeconds > 0) {
                    options.AutoFocusTimeout = TimeSpan.FromSeconds(inspectorOptions.TimeoutSeconds);
                }

                ActivateAutoFocusChart();
                DeactivateExposureAnalysis();
                var result = await autoFocusEngine.RerunWithRegions(options, savedAttempt, imagingFilter, regions, analyzeCts.Token, this.progress);
                var autoFocusAnalysisResult = await AnalyzeAutoFocusResult(result);
                if (!autoFocusAnalysisResult) {
                    Notification.ShowError("AutoFocus Analysis Failed");
                    return false;
                }
                ActivateTiltMeasurement();
                return true;
            });
            analyzeTask = localAnalyzeTask;

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection auto focus rerun analysis cancelled");
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Inspection auto focus rerun analysis failed: {e.Message}");
                Logger.Error("Inspection auto focus rerun analysis failed", e);
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

            var outerHFRSum = 0.0d;
            for (int i = 1; i < e.RegionHFRs.Count; ++i) {
                var regionName = GetRegionName(i);
                var regionHFR = e.RegionHFRs[i];
                outerHFRSum += regionHFR.EstimatedFinalHFR;
                reportBuilder.AppendLine($"{regionName} - HFR Delta: {regionHFR.EstimatedFinalHFR - centerHFR}, Focuser Delta: {regionHFR.EstimatedFinalFocuserPosition - centerFocuser}");

                RegionFinalFocusPoints[i] = new DataPoint(regionHFR.EstimatedFinalFocuserPosition, regionHFR.EstimatedFinalHFR);
            }

            InnerHFR = centerHFR;
            OuterHFR = outerHFRSum / (e.RegionHFRs.Count - 1);
            BackfocusHFR = OuterHFR - InnerHFR;
            AutoFocusCompleted = true;

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
                RegionFinalFocusPoints[i] = new DataPoint(-1.0d, 0.0d);
            }
            CenterFocusPoints.Clear();
            OutsideFocusPoints.Clear();
            AutoFocusCompleted = false;
        }

        private void CancelAnalyze(object o) {
            analyzeCts?.Cancel();
        }

        public ICommand RunAutoFocusAnalysisCommand { get; private set; }
        public ICommand RerunSavedAutoFocusAnalysisCommand { get; private set; }
        public ICommand RunExposureAnalysisCommand { get; private set; }
        public ICommand CancelAnalyzeCommand { get; private set; }
        public ICommand ClearAnalysesCommand { get; private set; }

        public AsyncObservableCollection<ScatterErrorPoint> CenterFocusPoints { get; private set; }
        public AsyncObservableCollection<ScatterErrorPoint> OutsideFocusPoints { get; private set; }
        public AsyncObservableCollection<DataPoint>[] RegionPlotFocusPoints { get; private set; }
        public AsyncObservableCollection<DataPoint> RegionFinalFocusPoints { get; private set; }
        public AsyncObservableCollection<Func<double, double>> RegionCurveFittings { get; private set; }
        public AsyncObservableCollection<TrendlineFitting> RegionLineFittings { get; private set; }
        private double innerHFR = double.NaN;

        public double InnerHFR {
            get => innerHFR;
            private set {
                innerHFR = value;
                RaisePropertyChanged();
            }
        }

        private double outerHFR = double.NaN;

        public double OuterHFR {
            get => outerHFR;
            private set {
                outerHFR = value;
                RaisePropertyChanged();
            }
        }

        private double backfocusHFR = double.NaN;

        public double BackfocusHFR {
            get => backfocusHFR;
            private set {
                backfocusHFR = value;
                RaisePropertyChanged();
            }
        }

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
            var plotControl = (WpfPlot)sender;
            var plotName = plotControl.Name;
            if (eccentricityCenterPoints == null) {
                return;
            }

            (double mouseCoordX, double mouseCoordY) = plotControl.GetMouseCoordinates();
            double xyRatio = plotControl.Plot.XAxis.Dims.PxPerUnit / plotControl.Plot.YAxis.Dims.PxPerUnit;
            (double pointX, double pointY, int pointIndex) = eccentricityCenterPoints.GetPointNearest(mouseCoordX, mouseCoordY, xyRatio);

            // place the highlight over the point of interest
            highlightedEccentricityPoint.X = pointX;
            highlightedEccentricityPoint.Y = pointY;
            highlightedEccentricityPoint.IsVisible = true;
            highlightedEccentricityPoint.Text = eccentricityValues[pointIndex].ToString("0.00");

            // render if the highlighted point changed
            if (lastHighlightedEccentricityPointIndex != pointIndex) {
                lastHighlightedEccentricityPointIndex = pointIndex;
                RefreshScottPlot(plotName);
            }
        }

        public void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            var plotControl = (WpfPlot)sender;
            var plotName = plotControl.Name;
            if (highlightedEccentricityPoint == null) {
                return;
            }

            lastHighlightedEccentricityPointIndex = -1;
            highlightedEccentricityPoint.IsVisible = false;
            RefreshScottPlot(plotName);
        }

        private void RefreshScottPlot(string plotName) {
            // TODO: This should be an event that takes the plot name
            this.PlotRefreshed?.Invoke(this, new EventArgs());
        }

        private CameraInfo cameraInfo = DeviceInfo.CreateDefaultInstance<CameraInfo>();

        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                this.cameraInfo = value;
                RaisePropertyChanged();
            }
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            CameraInfo = deviceInfo;
        }

        public void Dispose() {
            // Do nothing
        }

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            // Do nothing
        }

        public void UpdateUserFocused(FocuserInfo info) {
            // Do nothing
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            FocuserInfo = deviceInfo;
        }

        private FocuserInfo focuserInfo = DeviceInfo.CreateDefaultInstance<FocuserInfo>();

        public FocuserInfo FocuserInfo {
            get => focuserInfo;
            private set {
                this.focuserInfo = value;
                RaisePropertyChanged();
            }
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

        private ILNSceneContainer fwhmContourSceneContainer = null;

        public ILNSceneContainer FWHMContourSceneContainer {
            get => fwhmContourSceneContainer;
            set {
                fwhmContourSceneContainer = value;
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

        public bool LoopingExposureAnalysis {
            get => inspectorOptions.LoopingExposureAnalysisEnabled;
            set {
                inspectorOptions.LoopingExposureAnalysisEnabled = value;
                RaisePropertyChanged();
            }
        }

        private bool tiltMeasurementActive = false;

        public bool TiltMeasurementActive {
            get => tiltMeasurementActive;
            set {
                if (tiltMeasurementActive != value) {
                    this.tiltMeasurementActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool tiltMeasurementHistoryActive = false;

        public bool TiltMeasurementHistoryActive {
            get => tiltMeasurementHistoryActive;
            set {
                if (tiltMeasurementHistoryActive != value) {
                    this.tiltMeasurementHistoryActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool autoFocusChartActive = false;

        public bool AutoFocusChartActive {
            get => autoFocusChartActive;
            set {
                if (autoFocusChartActive != value) {
                    this.autoFocusChartActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool fwhmContoursActive = false;

        public bool FWHMContoursActive {
            get => fwhmContoursActive;
            set {
                if (fwhmContoursActive != value) {
                    this.fwhmContoursActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool eccentricityVectorsActive = false;

        public bool EccentricityVectorsActive {
            get => eccentricityVectorsActive;
            set {
                if (eccentricityVectorsActive != value) {
                    this.eccentricityVectorsActive = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool autoFocusChartActivatedOnce;

        public bool AutoFocusChartActivatedOnce {
            get => autoFocusChartActivatedOnce;
            set {
                if (autoFocusChartActivatedOnce != value) {
                    this.autoFocusChartActivatedOnce = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool tiltMeasurementActivatedOnce;

        public bool TiltMeasurementActivatedOnce {
            get => tiltMeasurementActivatedOnce;
            set {
                if (tiltMeasurementActivatedOnce != value) {
                    this.tiltMeasurementActivatedOnce = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool exposureAnalysisActivatedOnce;

        public bool ExposureAnalysisActivatedOnce {
            get => exposureAnalysisActivatedOnce;
            set {
                if (exposureAnalysisActivatedOnce != value) {
                    this.exposureAnalysisActivatedOnce = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool autoFocusCompleted;

        public bool AutoFocusCompleted {
            get => autoFocusCompleted;
            set {
                if (autoFocusCompleted != value) {
                    this.autoFocusCompleted = value;
                    RaisePropertyChanged();
                }
            }
        }

        private void ClearAnalyses(object o) {
            DeactivateAutoFocusAnalysis();
            DeactivateExposureAnalysis();
            AutoFocusChartActivatedOnce = false;
            TiltMeasurementActivatedOnce = false;
            ExposureAnalysisActivatedOnce = false;
            FWHMContourSceneContainer = null;
            TiltModel.Reset();
            AutoFocusCompleted = false;
        }

        private void ActivateAutoFocusChart() {
            AutoFocusChartActive = true;
            AutoFocusChartActivatedOnce = true;
        }

        private void ActivateTiltMeasurement() {
            TiltMeasurementActive = true;
            TiltMeasurementHistoryActive = true;
            TiltMeasurementActivatedOnce = true;
        }

        private void ActivateExposureAnalysis() {
            FWHMContoursActive = true;
            EccentricityVectorsActive = true;
            ExposureAnalysisActivatedOnce = true;
        }

        private void DeactivateAutoFocusAnalysis() {
            AutoFocusChartActive = false;
            TiltMeasurementActive = false;
            TiltMeasurementHistoryActive = false;
        }

        private void DeactivateExposureAnalysis() {
            FWHMContoursActive = false;
            EccentricityVectorsActive = false;
        }

        private ScottPlot.Plottable.MarkerPlot highlightedEccentricityPoint;
        private ScottPlot.Plottable.ScatterPlot eccentricityCenterPoints;
        private int lastHighlightedEccentricityPointIndex;
        private double[] eccentricityValues;
    }
}