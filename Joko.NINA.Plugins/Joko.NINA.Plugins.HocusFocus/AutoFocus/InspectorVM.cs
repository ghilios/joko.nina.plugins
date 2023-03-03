#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

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
using DrawingColor = System.Drawing.Color;
using SPPlot = ScottPlot.Plot;
using SPVector2 = ScottPlot.Statistics.Vector2;
using ScottPlot.Statistics;
using System.Windows.Media;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment;
using Newtonsoft.Json;
using System.IO;
using NINA.Joko.Plugins.HocusFocus.Scottplot;
using NINA.Astrometry;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Image.Interfaces;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    [Export(typeof(IDockableVM))]
    public class InspectorVM : DockableVM, IScottPlotController, ICameraConsumer, IFocuserConsumer, ITelescopeConsumer {
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
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IAutoFocusOptions autoFocusOptions;
        private readonly IAutoFocusEngineFactory autoFocusEngineFactory;
        private readonly IImageDataFactory imageDataFactory;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        private readonly IApplicationDispatcher applicationDispatcher;
        private readonly IProgress<ApplicationStatus> progress;
        private readonly IAlglibAPI alglibAPI;

        [ImportingConstructor]
        public InspectorVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            IImagingMediator imagingMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            IFilterWheelMediator filterWheelMediator,
            ITelescopeMediator telescopeMediator,
            IImageDataFactory imageDataFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector)
            : this(profileService, applicationStatusMediator, imagingMediator, cameraMediator, focuserMediator, filterWheelMediator, telescopeMediator, HocusFocusPlugin.StarDetectionOptions, HocusFocusPlugin.StarAnnotatorOptions, HocusFocusPlugin.InspectorOptions, HocusFocusPlugin.AutoFocusOptions, HocusFocusPlugin.AutoFocusEngineFactory,
                  imageDataFactory, starDetectionSelector, starAnnotatorSelector, HocusFocusPlugin.ApplicationDispatcher, HocusFocusPlugin.AlglibAPI) {
        }

        public InspectorVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            IImagingMediator imagingMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
            IFilterWheelMediator filterWheelMediator,
            ITelescopeMediator telescopeMediator,
            IStarDetectionOptions starDetectionOptions,
            IStarAnnotatorOptions starAnnotatorOptions,
            IInspectorOptions inspectorOptions,
            IAutoFocusOptions autoFocusOptions,
            IAutoFocusEngineFactory autoFocusEngineFactory,
            IImageDataFactory imageDataFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector,
            IApplicationDispatcher applicationDispatcher,
            IAlglibAPI alglibAPI) : base(profileService) {
            this.applicationStatusMediator = applicationStatusMediator;
            this.imagingMediator = imagingMediator;
            this.cameraMediator = cameraMediator;
            this.focuserMediator = focuserMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.starDetectionOptions = starDetectionOptions;
            this.starAnnotatorOptions = starAnnotatorOptions;
            this.telescopeMediator = telescopeMediator;
            this.inspectorOptions = inspectorOptions;
            this.autoFocusOptions = autoFocusOptions;
            this.autoFocusEngineFactory = autoFocusEngineFactory;
            this.imageDataFactory = imageDataFactory;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
            this.applicationDispatcher = applicationDispatcher;
            this.alglibAPI = alglibAPI;
            this.progress = ProgressFactory.Create(applicationStatusMediator, "Aberration Inspector");

            this.cameraMediator.RegisterConsumer(this);
            this.focuserMediator.RegisterConsumer(this);
            this.telescopeMediator.RegisterConsumer(this);

            this.Title = "Aberration Inspector";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);

            RegionFocusPoints = Enumerable.Range(0, 6).Select(i => new AsyncObservableCollection<ScatterErrorPoint>()).ToArray();
            RegionPlotFocusPoints = Enumerable.Range(0, 6).Select(i => new AsyncObservableCollection<DataPoint>()).ToArray();
            RegionFinalFocusPoints = new AsyncObservableCollection<DataPoint>(Enumerable.Range(0, 6).Select(i => new DataPoint(-1.0d, 0.0d)));
            RegionCurveFittings = new AsyncObservableCollection<Func<double, double>>(Enumerable.Range(0, 6).Select(i => (Func<double, double>)null));
            RegionLineFittings = new AsyncObservableCollection<TrendlineFitting>(Enumerable.Range(0, 6).Select(i => (TrendlineFitting)null));
            TiltModel = new TiltModel(inspectorOptions);
            SensorModel = new SensorModel(inspectorOptions, alglibAPI);

            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["InspectorSVG"];
            ImageGeometry.Freeze();

            RunAutoFocusAnalysisCommand = new AsyncCommand<bool>(AnalyzeAutoFocus, canExecute: (o) => !AnalysisRunning() && CameraInfo.Connected && FocuserInfo.Connected);
            RunExposureAnalysisCommand = new AsyncCommand<bool>(AnalyzeExposure, canExecute: (o) => !AnalysisRunning() && CameraInfo.Connected);
            RerunSavedAutoFocusAnalysisCommand = new AsyncCommand<bool>(AnalyzeSavedAutoFocusRun, canExecute: (o) => !AnalysisRunning());
            ClearAnalysesCommand = new RelayCommand(ClearAnalyses, canExecute: (o) => !AnalysisRunning());
            CancelAnalyzeCommand = new RelayCommand(CancelAnalyze);
            SlewToZenithEastCommand = new AsyncCommand<bool>(() => SlewToZenith(false), canExecute: (o) => TelescopeInfo.Connected && (slewToZenithTask == null || slewToZenithTask?.Status >= TaskStatus.RanToCompletion));
            SlewToZenithWestCommand = new AsyncCommand<bool>(() => SlewToZenith(true), canExecute: (o) => TelescopeInfo.Connected && (slewToZenithTask == null || slewToZenithTask?.Status >= TaskStatus.RanToCompletion));
            CancelSlewToZenithCommand = new RelayCommand((o) => slewToZenithCts?.Cancel());
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
            if (localAnalyzeTask != null && !localAnalyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }

            analyzeCts?.Cancel();
            var localAnalyzeCts = new CancellationTokenSource();
            analyzeCts = localAnalyzeCts;

            localAnalyzeTask = Task.Run(async () => {
                try {
                    this.cameraMediator.RegisterCaptureBlock(this);

                    var autoFocusEngine = autoFocusEngineFactory.Create();
                    var options = GetAutoFocusEngineOptions(autoFocusEngine);
                    var sensorCurveModelEnabled = inspectorOptions.SensorCurveModelEnabled;
                    var regions = GetStarDetectionRegions(options, sensorCurveModelEnabled: sensorCurveModelEnabled);
                    var imagingFilter = GetImagingFilter();

                    autoFocusEngine.Started += AutoFocusEngine_Started;
                    autoFocusEngine.IterationFailed += AutoFocusEngine_IterationFailed;
                    autoFocusEngine.Failed += AutoFocusEngine_Failed;
                    autoFocusEngine.Completed += AutoFocusEngine_Completed;
                    autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;
                    autoFocusEngine.SubMeasurementPointCompleted += AutoFocusEngine_SubMeasurementPointCompleted;

                    ActivateAutoFocusChart();
                    ResetErrors();
                    ResetExposureAnalysis();
                    var result = await autoFocusEngine.RunWithRegions(options, imagingFilter, regions, localAnalyzeCts.Token, this.progress);
                    if (result == null) {
                        InspectorErrorText = "AutoFocus Analysis Failed";
                        DeactivateAutoFocusAnalysis();
                        return false;
                    }

                    var autoFocusAnalysisResult = await AnalyzeAutoFocusResult(result, sensorCurveModelEnabled: sensorCurveModelEnabled, ct: localAnalyzeCts.Token);
                    if (!autoFocusAnalysisResult) {
                        InspectorErrorText = "AutoFocus Analysis Failed. View saved AF report in the AutoFocus tab.";
                        Notification.ShowError("AutoFocus Analysis Failed. View saved AF report in the AutoFocus tab.");
                        DeactivateAutoFocusAnalysis();
                        return false;
                    }
                    ActivateTiltMeasurement();
                    var exposureAnalysisResult = await TakeAndAnalyzeExposureImpl(autoFocusEngine, analyzeCts.Token);
                    if (!exposureAnalysisResult) {
                        InspectorErrorText = "Exposure Analysis Failed. View saved AF report in the AutoFocus tab.";
                        Notification.ShowError("Exposure Analysis Failed");
                        DeactivateAutoFocusAnalysis();
                        return false;
                    }
                    ActivateExposureAnalysis();
                    Notification.ShowInformation("Aberration Inspection Complete");
                    return true;
                } finally {
                    this.cameraMediator.ReleaseCaptureBlock(this);
                }
            }, localAnalyzeCts.Token);
            analyzeTask = localAnalyzeTask;

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection analysis cancelled");
                DeactivateAutoFocusAnalysis();
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Inspection analysis failed: {e.Message}");
                Logger.Error("Inspection analysis failed", e);
                DeactivateAutoFocusAnalysis();
                return false;
            } finally {
                analyzeTask = null;
                analyzeCts = null;
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

        private bool focuserStepSizeWarningShowed = false;

        private async Task<bool> AnalyzeAutoFocusResult(AutoFocusResult result, bool sensorCurveModelEnabled, CancellationToken ct) {
            if (result == null || !result.Succeeded) {
                Logger.Error("Inspection analysis failed, due to failed AutoFocus");
                return false;
            }

            var invalidRegionCount = result.RegionResults.Count(r => double.IsNaN(r.EstimatedFinalFocuserPosition));
            if (invalidRegionCount > 0) {
                Notification.ShowWarning($"{invalidRegionCount} regions failed to produce a focus curve");
                Logger.Warning($"{invalidRegionCount} regions failed to produce a focus curve");
            }

            if (sensorCurveModelEnabled) {
                double focuserSizeMicrons = InspectorOptions.MicronsPerFocuserStep;
                if (double.IsNaN(focuserSizeMicrons) || focuserSizeMicrons <= 0.0) {
                    if (!focuserStepSizeWarningShowed) {
                        Notification.ShowWarning("Focuser Step Size not set. Assuming 1 micron per focuser step. This message won't be shown again.");
                        focuserStepSizeWarningShowed = true;
                    }
                    Logger.Warning("Focuser Step Size not set. Assuming 1 micron per focuser step.");
                    focuserSizeMicrons = 1.0d;
                }

                var finalFocuserPosition = result.RegionResults[0].EstimatedFinalFocuserPosition;
                await SensorModel.UpdateModel(
                    FullSensorDetectedStars,
                    fRatio: profileService.ActiveProfile.TelescopeSettings.FocalRatio,
                    focuserSizeMicrons: focuserSizeMicrons,
                    finalFocusPosition: finalFocuserPosition,
                    ct: ct);
            }

            UpdateBackfocusMeasurements(result);
            TiltModel.UpdateTiltModel(result, fRatio: profileService.ActiveProfile.TelescopeSettings.FocalRatio, backfocusFocuserPositionDelta: BackfocusFocuserPositionDelta);
            AutoFocusCompleted = true;
            return true;
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

        private async Task<bool> LoadAndAnalyzeExposure(IAutoFocusEngine autoFocusEngine, AutoFocusEngineOptions autoFocusEngineOptions, SavedAutoFocusImage savedImage, CancellationToken token) {
            var starDetection = starDetectionSelector.GetBehavior() as IHocusFocusStarDetection;
            if (starDetection == null) {
                Notification.ShowError("HocusFocus must be selected as the Star Detector. Change this option in Options -> Image Options");
                Logger.Error("HocusFocus must be selected as the Star Detector");
                return false;
            }

            var imageData = await LoadSavedFile(autoFocusEngineOptions, savedImage, token);
            return await AnalyzeExposureImpl(autoFocusEngine, imageData, token);
        }

        private async Task<IRenderedImage> LoadSavedFile(
            AutoFocusEngineOptions autoFocusEngineOptions,
            SavedAutoFocusImage savedFile,
            CancellationToken token) {
            var isBayered = savedFile.IsBayered;
            var bitDepth = savedFile.BitDepth;

            var imageData = await this.imageDataFactory.CreateFromFile(savedFile.Path, bitDepth, isBayered, profileService.ActiveProfile.CameraSettings.RawConverter, token);
            var autoStretch = true;
            // If using contrast based statistics, no need to stretch
            if (autoFocusEngineOptions.AutoFocusMethod == AFMethodEnum.CONTRASTDETECTION && profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod == ContrastDetectionMethodEnum.Statistics) {
                autoStretch = false;
            }

            var prepareParameters = new PrepareImageParameters(autoStretch: autoStretch, detectStars: false);
            return await imagingMediator.PrepareImage(imageData, prepareParameters, token);
        }

        private async Task<bool> AnalyzeExposureImpl(IAutoFocusEngine autoFocusEngine, IRenderedImage imageData, CancellationToken token) {
            var autoFocusOptions = autoFocusEngine.GetOptions();
            var starDetection = (IHocusFocusStarDetection)starDetectionSelector.GetBehavior();
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

            var analysisResult = (HocusFocusStarDetectionResult)await starDetection.Detect(imageData, hfParams, starDetectorParams, this.progress, token);
            AnalyzeStarDetectionResult(analysisResult);
            this.SnapshotAnalysisStarDetectionResult = analysisResult;
            ActivateExposureAnalysis();
            return true;
        }

        private async Task<bool> TakeAndAnalyzeExposureImpl(IAutoFocusEngine autoFocusEngine, CancellationToken token) {
            var imagingFilter = GetImagingFilter();
            var starDetection = (IHocusFocusStarDetection)starDetectionSelector.GetBehavior();

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
                return await AnalyzeExposureImpl(autoFocusEngine, imageData, token);
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

        private void AnalyzeStarDetectionResult(HocusFocusStarDetectionResult result) {
            var numRegionsWide = inspectorOptions.NumRegionsWide;
            var imageSize = result.ImageSize;
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

                var regionRow = (int)Math.Floor((imageSize.Height - detectedStar.Position.Y - 1) / imageSize.Height * numRegionsTall);
                var regionCol = (int)Math.Floor(detectedStar.Position.X / imageSize.Width * numRegionsWide);
                regionDetectedStars[regionCol, regionRow].Add(detectedStar);
            }

            {
                double[] xs = DataGen.Range(0, numRegionsWide);
                double[] ys = DataGen.Range(0, numRegionsTall);
                var vectors = new SPVector2[numRegionsWide, numRegionsTall];
                var centerXs = new double[numRegionsWide * numRegionsTall];
                var centerYs = new double[numRegionsWide * numRegionsTall];
                var eccentricities = new double[numRegionsWide * numRegionsTall];
                var rotations = new double[numRegionsWide * numRegionsTall];

                double scalingFactor = 2.5;
                int pointIndex = 0;
                double maxMagnitude = 0.0d;
                for (int regionRow = 0; regionRow < numRegionsTall; ++regionRow) {
                    for (int regionCol = 0; regionCol < numRegionsWide; ++regionCol) {
                        var detectedStars = regionDetectedStars[regionCol, regionRow];

                        if (detectedStars.Count > 0) {
                            var (eccentricityMedian, _) = detectedStars.Select(s => s.PSF.Eccentricity).MedianMAD();
                            var sumEccentricity = detectedStars.Select(s => s.PSF.Eccentricity).Sum();
                            var psfRotationWeightedMean = detectedStars.Select(s => s.PSF.ThetaRadians * s.PSF.Eccentricity).Sum() / sumEccentricity;

                            var scaledEccentricity = eccentricityMedian * eccentricityMedian * scalingFactor;
                            double x = Math.Cos(psfRotationWeightedMean) * scaledEccentricity;
                            // Since y is inverted to render top-down, we must also invert the y part of the angle vector
                            double y = -Math.Sin(psfRotationWeightedMean) * scaledEccentricity;
                            vectors[regionCol, regionRow] = new Vector2(x, y);
                            eccentricities[pointIndex] = eccentricityMedian;
                            rotations[pointIndex] = Angle.ByRadians(psfRotationWeightedMean).Degree;
                            maxMagnitude = Math.Max(scaledEccentricity, maxMagnitude);
                        } else {
                            eccentricities[pointIndex] = double.NaN;
                            rotations[pointIndex] = double.NaN;
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

                ScottPlot.Drawing.Colormap colormap = null;
                if (InspectorOptions.EccentricityColorMapEnabled) {
                    colormap = new ScottPlot.Drawing.Colormap(new LinearColormap("G2R", DrawingColor.Green, DrawingColor.GreenYellow, DrawingColor.Red));
                }

                // maxMagnitude * 1.2 is taken from the ScottPlot code to ensure no vector scaling takes place
                var vectorField = new HFVectorField(vectors, xs, ys, colormap: colormap, scaleFactor: maxMagnitude * 1.2 * 1.5, colorScaleMin: 0.3 * 0.3 * scalingFactor, colorScaleMax: 0.6 * 0.6 * scalingFactor, defaultColor: primaryColor);
                plot.Add(vectorField);

                // Scatter points act as anchor points for mouse over events
                var scatterPoints = plot.AddScatterPoints(centerXs, centerYs);
                scatterPoints.IsVisible = false;

                vectorField.ScaledArrowheadLength = 0;
                vectorField.ScaledArrowheadWidth = 0;
                vectorField.ScaledArrowheads = true;
                vectorField.LineWidth = 3;
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
                rotationValues = rotations;
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

        private AutoFocusEngineOptions GetAutoFocusEngineOptions(IAutoFocusEngine autoFocusEngine) {
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
            if (inspectorOptions.DetailedAnalysisExposureSeconds > 0) {
                options.OverrideAutoFocusExposureTime = TimeSpan.FromSeconds(inspectorOptions.DetailedAnalysisExposureSeconds);
            }
            return options;
        }

        private StarDetectionRegion GetAutoFocusRegion(AutoFocusEngineOptions options) {
            var analysisParams = new StarDetectionParams() {
                Sensitivity = profileService.ActiveProfile.ImageSettings.StarSensitivity,
                NoiseReduction = profileService.ActiveProfile.ImageSettings.NoiseReduction,
                NumberOfAFStars = options.NumberOfAFStars,
                IsAutoFocus = true
            };
            if (profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1) {
                analysisParams.UseROI = true;
                analysisParams.InnerCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio;
                analysisParams.OuterCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusOuterCropRatio;
            }
            return StarDetectionRegion.FromStarDetectionParams(analysisParams);
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
                    dialog.RootFolder = Environment.SpecialFolder.MyComputer;
                    if (!String.IsNullOrEmpty(autoFocusOptions.LastSelectedLoadPath) && Directory.Exists(autoFocusOptions.LastSelectedLoadPath)) {
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
                var options = GetAutoFocusEngineOptions(autoFocusEngine);
                var sensorCurveModelEnabled = inspectorOptions.SensorCurveModelEnabled;
                var regions = GetStarDetectionRegions(options, sensorCurveModelEnabled: sensorCurveModelEnabled);

                autoFocusEngine.Started += AutoFocusEngine_Started;
                autoFocusEngine.Failed += AutoFocusEngine_Failed;
                autoFocusEngine.Completed += AutoFocusEngine_CompletedNoReport;
                autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;
                autoFocusEngine.SubMeasurementPointCompleted += AutoFocusEngine_SubMeasurementPointCompleted;

                var imagingFilter = GetImagingFilter();

                ActivateAutoFocusChart();
                ResetErrors();
                ResetExposureAnalysis();
                var result = await autoFocusEngine.RerunWithRegions(options, savedAttempt, imagingFilter, regions, localAnalyzeCts.Token, this.progress);
                if (result == null) {
                    InspectorErrorText = "AutoFocus Analysis Failed";
                    DeactivateAutoFocusAnalysis();
                    return false;
                }

                var autoFocusAnalysisResult = await AnalyzeAutoFocusResult(result, sensorCurveModelEnabled: sensorCurveModelEnabled, ct: localAnalyzeCts.Token);
                if (!autoFocusAnalysisResult) {
                    Notification.ShowError("AutoFocus Analysis Failed");
                    InspectorErrorText = "AutoFocus Analysis Failed";
                    DeactivateAutoFocusAnalysis();
                    return false;
                }
                ActivateTiltMeasurement();

                var finalDirectoryCandidates = new DirectoryInfo(selectedPath).Parent.GetDirectories("final");
                if (finalDirectoryCandidates.Length == 0) {
                    SimpleAnalysisErrorText = "Cannot display FWHM Contour and Eccentricity Vectors.\nSaved AutoFocus doesn't contain a final folder.\nEnable \"Validate HFR Improvement\" in Hocus Focus Options";
                    Logger.Warning($"No final directory found. Continuing without doing exposure analysis.");
                } else {
                    var finalDirectory = finalDirectoryCandidates[0].FullName;
                    try {
                        var savedFinalAttempt = autoFocusEngine.LoadSavedFinalAttempt(finalDirectory);
                        if (savedFinalAttempt.SavedImages.Count == 0) {
                            SimpleAnalysisErrorText = $"Cannot display FWHM Contour and Eccentricity Vectors\n.No saved images in final directory {finalDirectory}.";
                            Logger.Error($"No saved images in final directory {finalDirectory}. Continuing without doing exposure analysis");
                            Notification.ShowError($"No saved images in final directory {finalDirectory}. Continuing without doing exposure analysis");
                        } else if (savedFinalAttempt.SavedImages.Count > 1) {
                            SimpleAnalysisErrorText = $"Cannot display FWHM Contour and Eccentricity Vectors.\nMultiple saved images in final directory {finalDirectory}.";
                            Logger.Error($"Multiple saved images in final directory {finalDirectory}. Continuing without doing exposure analysis");
                            Notification.ShowError($"Multiple saved images in final directory {finalDirectory}. Continuing without doing exposure analysis");
                        } else {
                            var savedFinalImage = savedFinalAttempt.SavedImages[0];
                            var exposureAnalysisResult = await LoadAndAnalyzeExposure(autoFocusEngine, options, savedFinalImage, analyzeCts.Token);
                            if (!exposureAnalysisResult) {
                                SimpleAnalysisErrorText = $"Cannot display FWHM Contour and Eccentricity Vectors.\nAnalyzing final exposure failed.";
                                Logger.Error("Final Exposure Analysis Failed");
                                Notification.ShowError("Final Exposure Analysis Failed");
                                DeactivateExposureAnalysis();
                            } else {
                                ActivateExposureAnalysis();
                            }
                        }
                    } catch (Exception e) {
                        SimpleAnalysisErrorText = $"Cannot display FWHM Contour and Eccentricity Vectors.\nFailed to read image in final directory {finalDirectory}.\n{e.Message}";
                        Logger.Error(e, $"Failed to read image in final directory {finalDirectory}. Continuing without doing exposure analysis");
                        Notification.ShowError($"Failed to read image in final directory {finalDirectory}. {e.Message}");
                    }
                }

                Notification.ShowInformation("Aberration Inspection Complete");
                return true;
            });
            analyzeTask = localAnalyzeTask;

            try {
                return await localAnalyzeTask;
            } catch (OperationCanceledException) {
                Logger.Warning("Inspection auto focus rerun analysis cancelled");
                InspectorErrorText = "Inspection AutoFocus Rerun analysis cancelled";
                DeactivateAutoFocusAnalysis();
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Inspection auto focus rerun analysis failed: {e.Message}");
                InspectorErrorText = $"Inspection AutoFocus Rerun analysis failed\n{e.Message}";
                Logger.Error("Inspection auto focus rerun analysis failed", e);
                DeactivateAutoFocusAnalysis();
                return false;
            }
        }

        private List<StarDetectionRegion> GetStarDetectionRegions(AutoFocusEngineOptions options, bool sensorCurveModelEnabled) {
            var starDetectionRegion = GetAutoFocusRegion(options);
            var one_third = 1.0d / 3.0d;
            var width = one_third * inspectorOptions.CornersROI * inspectorOptions.SensorROI;
            var distanceFromBoundary = (1.0 - inspectorOptions.SensorROI) / 2.0;
            var innerStart = distanceFromBoundary;
            var outerStart = 1.0 - distanceFromBoundary - width;

            var regions = new List<StarDetectionRegion>() {
                    starDetectionRegion,
                    new StarDetectionRegion(new RatioRect(one_third, one_third, one_third, one_third)),
                    new StarDetectionRegion(new RatioRect(innerStart, innerStart, width, width)),
                    new StarDetectionRegion(new RatioRect(outerStart, innerStart, width, width)),
                    new StarDetectionRegion(new RatioRect(innerStart, outerStart, width, width)),
                    new StarDetectionRegion(new RatioRect(outerStart, outerStart, width, width))
                };
            if (sensorCurveModelEnabled) {
                var roiValue = (1.0d - inspectorOptions.SensorROI) / 2.0;
                regions.Add(new StarDetectionRegion(new RatioRect(roiValue, roiValue, 1.0d - roiValue, 1.0d - roiValue)));
            }
            return regions;
        }

        private void AutoFocusEngine_Started(object sender, AutoFocusStartedEventArgs e) {
            this.ClearAnalysis();
        }

        private void AutoFocusEngine_MeasurementPointCompleted(object sender, AutoFocusMeasurementPointCompletedEventArgs e) {
            if (e.RegionIndex >= RegionCurveFittings.Count) {
                return;
            }

            RegionCurveFittings[e.RegionIndex] = GetCurveFitting(e.Fittings);
            RegionLineFittings[e.RegionIndex] = GetLineFitting(e.Fittings);
            RegionPlotFocusPoints[e.RegionIndex].AddSorted(new DataPoint(e.FocuserPosition, e.Measurement.Measure), plotPointComparer);

            var focusPoints = RegionFocusPoints[e.RegionIndex];
            focusPoints.AddSorted(new ScatterErrorPoint(e.FocuserPosition, e.Measurement.Measure, 0, Math.Max(0.001, e.Measurement.Stdev)), focusPointComparer);
        }

        private void AutoFocusEngine_SubMeasurementPointCompleted(object sender, AutoFocusSubMeasurementPointCompletedEventArgs e) {
            if (e.RegionIndex == 6) {
                var hfStarDetectionResult = e.StarDetectionResult as HocusFocusStarDetectionResult;
                if (hfStarDetectionResult != null) {
                    FullSensorDetectedStars.Add(new SensorDetectedStars(e.FocuserPosition, hfStarDetectionResult));
                }
            }
        }

        private void GenerateReport(AutoFocusCompletedEventArgs e) {
            var regionReports = new HocusFocusReport[RegionFocusPoints.Length];
            for (int regionIndex = 0; regionIndex < RegionFocusPoints.Length; ++regionIndex) {
                var region = e.RegionHFRs[regionIndex];
                var finalFocusPoint = new DataPoint(region.EstimatedFinalFocuserPosition, region.EstimatedFinalHFR);
                var lastAutoFocusPoint = new ReportAutoFocusPoint {
                    Focuspoint = finalFocusPoint,
                    Temperature = e.Temperature,
                    Timestamp = DateTime.Now,
                    Filter = e.Filter
                };
                var report = HocusFocusReport.GenerateReport(
                    profileService: this.profileService,
                    starDetector: starDetectionSelector.GetBehavior(),
                    focusPoints: RegionFocusPoints[regionIndex],
                    fittings: region.Fittings,
                    initialFocusPosition: e.InitialFocusPosition,
                    initialHFR: region.InitialHFR ?? 0.0d,
                    finalHFR: region.FinalHFR ?? region.EstimatedFinalHFR,
                    filter: e.Filter,
                    temperature: e.Temperature,
                    focusPoint: finalFocusPoint,
                    lastFocusPoint: lastAutoFocusPoint,
                    region: region.Region,
                    hocusFocusStarDetectionOptions: this.starDetectionOptions,
                    hocusFocusAutoFocusOptions: this.autoFocusOptions,
                    duration: e.Duration);
                regionReports[regionIndex] = report;
            }

            if (!string.IsNullOrEmpty(e.SaveFolder)) {
                for (int regionIndex = 0; regionIndex < RegionFocusPoints.Length; ++regionIndex) {
                    var regionReport = regionReports[regionIndex];
                    var regionReportText = JsonConvert.SerializeObject(regionReport, Formatting.Indented);
                    var targetFilePath = Path.Combine(e.SaveFolder, $"attempt{e.Iteration:00}", $"autofocus_report_Region{regionIndex}.json");
                    File.WriteAllText(targetFilePath, regionReportText);
                }
            }

            var reportText = JsonConvert.SerializeObject(regionReports[0], Formatting.Indented);
            string path = Path.Combine(HocusFocusVM.ReportDirectory, DateTime.Now.ToString("yyyy-MM-dd--HH-mm-ss") + ".json");
            File.WriteAllText(path, reportText);

            var firstRegionReport = regionReports[0];
            var autoFocusInfo = new AutoFocusInfo(firstRegionReport.Temperature, firstRegionReport.CalculatedFocusPoint.Position, firstRegionReport.Filter, firstRegionReport.Timestamp);
            focuserMediator.BroadcastSuccessfulAutoFocusRun(autoFocusInfo);
        }

        private void AutoFocusEngine_Completed(object sender, AutoFocusCompletedEventArgs e) {
            AutoFocusEngine_CompletedNoReport(sender, e);
            GenerateReport(e);
        }

        private void MaybeSaveFailedAutoFocusReports(AutoFocusFailedEventArgs e) {
            if (!string.IsNullOrEmpty(e.SaveFolder)) {
                for (int regionIndex = 0; regionIndex < RegionFocusPoints.Length; ++regionIndex) {
                    var region = e.RegionHFRs[regionIndex];
                    var finalFocusPoint = new DataPoint(-1.0d, 0.0d);
                    var lastAutoFocusPoint = new ReportAutoFocusPoint {
                        Focuspoint = finalFocusPoint,
                        Temperature = e.Temperature,
                        Timestamp = DateTime.Now,
                        Filter = e.Filter
                    };
                    var report = HocusFocusReport.GenerateReport(
                        profileService: this.profileService,
                        starDetector: starDetectionSelector.GetBehavior(),
                        focusPoints: RegionFocusPoints[regionIndex],
                        fittings: region.Fittings,
                        initialFocusPosition: e.InitialFocusPosition,
                        initialHFR: region.InitialHFR ?? 0.0d,
                        finalHFR: region.FinalHFR ?? region.EstimatedFinalHFR,
                        filter: e.Filter,
                        temperature: e.Temperature,
                        focusPoint: finalFocusPoint,
                        lastFocusPoint: lastAutoFocusPoint,
                        region: region.Region,
                        hocusFocusStarDetectionOptions: this.starDetectionOptions,
                        hocusFocusAutoFocusOptions: this.autoFocusOptions,
                        duration: e.Duration);
                    var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
                    var targetFilePath = Path.Combine(e.SaveFolder, $"attempt{e.Iteration:00}", $"autofocus_report_Region{regionIndex}.json");
                    File.WriteAllText(targetFilePath, reportText);
                }
            }
        }

        private void AutoFocusEngine_IterationFailed(object sender, AutoFocusFailedEventArgs e) {
            MaybeSaveFailedAutoFocusReports(e);
            ClearPlots();
        }

        private void AutoFocusEngine_Failed(object sender, AutoFocusFailedEventArgs e) {
            MaybeSaveFailedAutoFocusReports(e);

            var report = GenerateReportForRegion(e, 0);
            var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
            string path = Path.Combine(HocusFocusVM.ReportDirectory, DateTime.Now.ToString("yyyy-MM-dd--HH-mm-ss") + ".json");
            File.WriteAllText(path, reportText);
        }

        private HocusFocusReport GenerateReportForRegion(AutoFocusFinishedEventArgsBase e, int regionIndex) {
            var region = e.RegionHFRs[regionIndex];
            var finalFocusPoint = new DataPoint(-1.0d, 0.0d);
            var lastAutoFocusPoint = new ReportAutoFocusPoint {
                Focuspoint = finalFocusPoint,
                Temperature = e.Temperature,
                Timestamp = DateTime.Now,
                Filter = e.Filter
            };
            return HocusFocusReport.GenerateReport(
                profileService: this.profileService,
                starDetector: starDetectionSelector.GetBehavior(),
                focusPoints: RegionFocusPoints[regionIndex],
                fittings: region.Fittings,
                initialFocusPosition: e.InitialFocusPosition,
                initialHFR: region.InitialHFR ?? 0.0d,
                finalHFR: region.FinalHFR ?? region.EstimatedFinalHFR,
                filter: e.Filter,
                temperature: e.Temperature,
                focusPoint: finalFocusPoint,
                lastFocusPoint: lastAutoFocusPoint,
                region: region.Region,
                hocusFocusStarDetectionOptions: this.starDetectionOptions,
                hocusFocusAutoFocusOptions: this.autoFocusOptions,
                duration: e.Duration);
        }

        private void UpdateBackfocusMeasurements(AutoFocusResult result) {
            var centerRegionResult = result.RegionResults[1];
            var centerHFR = centerRegionResult.EstimatedFinalHFR;
            var centerFocuser = centerRegionResult.EstimatedFinalFocuserPosition;
            var outerHFRSum = 0.0d;
            var outerFocuserPositionSum = 0.0d;
            for (int i = 2; i < 6; ++i) {
                var regionResult = result.RegionResults[i];
                outerHFRSum += regionResult.EstimatedFinalHFR;
                outerFocuserPositionSum += regionResult.EstimatedFinalFocuserPosition;
            }

            InnerFocuserPosition = centerFocuser;
            OuterFocuserPosition = outerFocuserPositionSum / 4;
            BackfocusFocuserPositionDelta = OuterFocuserPosition - InnerFocuserPosition;
            if (BackfocusFocuserPositionDelta > 0) {
                BackfocusDirection = "TOWARDS";
            } else {
                BackfocusDirection = "AWAY FROM";
            }
            if (InspectorOptions.MicronsPerFocuserStep > 0) {
                BackfocusMicronDelta = BackfocusFocuserPositionDelta * InspectorOptions.MicronsPerFocuserStep;
            } else {
                BackfocusMicronDelta = double.NaN;
            }
            InnerHFR = centerHFR;
            OuterHFR = outerHFRSum / 4;
            BackfocusHFR = OuterHFR - InnerHFR;
        }

        private void AutoFocusEngine_CompletedNoReport(object sender, AutoFocusCompletedEventArgs e) {
            var logReportBuilder = new StringBuilder();
            var centerHFR = e.RegionHFRs[1].EstimatedFinalHFR;
            var centerFocuser = e.RegionHFRs[1].EstimatedFinalFocuserPosition;
            RegionFinalFocusPoints[1] = new DataPoint(e.RegionHFRs[1].EstimatedFinalFocuserPosition, e.RegionHFRs[1].EstimatedFinalHFR);
            logReportBuilder.AppendLine($"Center - HFR: {centerHFR}, Focuser: {centerFocuser}");

            for (int i = 2; i < 6; ++i) {
                var regionName = GetRegionName(i);
                var regionHFR = e.RegionHFRs[i];
                logReportBuilder.AppendLine($"{regionName} - HFR Delta: {regionHFR.EstimatedFinalHFR - centerHFR}, Focuser Delta: {regionHFR.EstimatedFinalFocuserPosition - centerFocuser}");

                RegionFinalFocusPoints[i] = new DataPoint(regionHFR.EstimatedFinalFocuserPosition, regionHFR.EstimatedFinalHFR);
            }

            Logger.Info(logReportBuilder.ToString());
        }

        private string GetRegionName(int regionIndex) {
            if (regionIndex == 1) {
                return "Center";
            } else if (regionIndex == 2) {
                return "Top Left";
            } else if (regionIndex == 3) {
                return "Top Right";
            } else if (regionIndex == 4) {
                return "Bottom Left";
            } else if (regionIndex == 5) {
                return "Bottom Right";
            } else if (regionIndex == 6) {
                return "Extra (Full)";
            }
            throw new ArgumentException($"{regionIndex} is not a valid region index", "regionIndex");
        }

        private void ClearAnalysis() {
            FullSensorDetectedStars.Clear();
            ClearPlots();
        }

        private void ClearPlots() {
            for (int i = 0; i < RegionCurveFittings.Count; ++i) {
                RegionCurveFittings[i] = null;
                RegionLineFittings[i] = null;
                RegionPlotFocusPoints[i].Clear();
                RegionFinalFocusPoints[i] = new DataPoint(-1.0d, 0.0d);
                RegionFocusPoints[i].Clear();
            }
            AutoFocusChartPlotModel?.ResetAllAxes();
            SensorModel.Reset();
            AutoFocusCompleted = false;
        }

        private void CancelAnalyze(object o) {
            analyzeCts?.Cancel();
        }

        private Task<bool> slewToZenithTask;
        private CancellationTokenSource slewToZenithCts;

        private async Task<bool> SlewToZenith(bool west) {
            var localTask = slewToZenithTask;
            if (localTask != null && !localTask.IsCompleted) {
                Notification.ShowError("Slew to zenith still in progress");
                return false;
            }

            slewToZenithCts?.Cancel();
            var localCts = new CancellationTokenSource();
            slewToZenithCts = localCts;

            localTask = Task.Run(async () => {
                var latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
                var longitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
                var azimuth = west ? Angle.ByDegree(90) : Angle.ByDegree(270);
                return await telescopeMediator.SlewToCoordinatesAsync(new TopocentricCoordinates(azimuth, Angle.ByDegree(89), latitude, longitude), localCts.Token);
            }, localCts.Token);
            slewToZenithTask = localTask;

            try {
                return await localTask;
            } catch (OperationCanceledException) {
                Logger.Info("SlewToZenith cancelled");
            } catch (Exception e) {
                Notification.ShowError($"Slew to zenith failed. {e.Message}");
                Logger.Error("Slew to zenith failed", e);
            } finally {
                slewToZenithTask = null;
                slewToZenithCts = null;
            }
            return false;
        }

        public ICommand RunAutoFocusAnalysisCommand { get; private set; }
        public ICommand RerunSavedAutoFocusAnalysisCommand { get; private set; }
        public ICommand RunExposureAnalysisCommand { get; private set; }
        public ICommand CancelAnalyzeCommand { get; private set; }
        public ICommand ClearAnalysesCommand { get; private set; }
        public ICommand SlewToZenithEastCommand { get; private set; }
        public ICommand SlewToZenithWestCommand { get; private set; }
        public ICommand CancelSlewToZenithCommand { get; private set; }

        private readonly List<SensorDetectedStars> FullSensorDetectedStars = new List<SensorDetectedStars>();
        public AsyncObservableCollection<ScatterErrorPoint>[] RegionFocusPoints { get; private set; }
        public AsyncObservableCollection<DataPoint>[] RegionPlotFocusPoints { get; private set; }
        public AsyncObservableCollection<DataPoint> RegionFinalFocusPoints { get; private set; }
        public AsyncObservableCollection<Func<double, double>> RegionCurveFittings { get; private set; }
        public AsyncObservableCollection<TrendlineFitting> RegionLineFittings { get; private set; }
        public PlotModel AutoFocusChartPlotModel { get; set; }

        private string backfocusDirection = "";

        public string BackfocusDirection {
            get => backfocusDirection;
            private set {
                backfocusDirection = value;
                RaisePropertyChanged();
            }
        }

        private double innerFocuserPosition = double.NaN;

        public double InnerFocuserPosition {
            get => innerFocuserPosition;
            private set {
                innerFocuserPosition = value;
                RaisePropertyChanged();
            }
        }

        private double outerFocuserPosition = double.NaN;

        public double OuterFocuserPosition {
            get => outerFocuserPosition;
            private set {
                outerFocuserPosition = value;
                RaisePropertyChanged();
            }
        }

        private double backfocusFocuserPositionDelta = double.NaN;

        public double BackfocusFocuserPositionDelta {
            get => backfocusFocuserPositionDelta;
            private set {
                backfocusFocuserPositionDelta = value;
                RaisePropertyChanged();
            }
        }

        private double backfocusMicronDelta = double.NaN;

        public double BackfocusMicronDelta {
            get => backfocusMicronDelta;
            private set {
                backfocusMicronDelta = value;
                RaisePropertyChanged();
            }
        }

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

        private HocusFocusStarDetectionResult snapshotAnalysisStarDetectionResult;

        public HocusFocusStarDetectionResult SnapshotAnalysisStarDetectionResult {
            get => snapshotAnalysisStarDetectionResult;
            private set {
                snapshotAnalysisStarDetectionResult = value;
                RaisePropertyChanged();
            }
        }

        public TiltModel TiltModel { get; private set; }
        public SensorModel SensorModel { get; private set; }
        public IInspectorOptions InspectorOptions => this.inspectorOptions;

        private TrendlineFitting GetLineFitting(AutoFocusFitting fitting) {
            if (fitting.Method == AFMethodEnum.STARHFR) {
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
            highlightedEccentricityPoint.Text = $"{eccentricityValues[pointIndex]:0.00}, {rotationValues[pointIndex]:0.}°";

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

        private TelescopeInfo telescopeInfo = DeviceInfo.CreateDefaultInstance<TelescopeInfo>();

        public TelescopeInfo TelescopeInfo {
            get => telescopeInfo;
            private set {
                this.telescopeInfo = value;
                RaisePropertyChanged();
            }
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            CameraInfo = deviceInfo;
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            TelescopeInfo = deviceInfo;
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

        private bool sensorCurveModelActive = false;

        public bool SensorCurveModelActive {
            get => sensorCurveModelActive;
            set {
                if (sensorCurveModelActive != value) {
                    this.sensorCurveModelActive = value;
                    RaisePropertyChanged();
                }
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

        private bool sensorModel3DEnabled = true;

        public bool SensorModel3DEnabled {
            get => sensorModel3DEnabled;
            set {
                if (sensorModel3DEnabled != value) {
                    this.sensorModel3DEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string inspectorErrorText = string.Empty;

        public string InspectorErrorText {
            get => inspectorErrorText;
            set {
                if (inspectorErrorText != value) {
                    this.inspectorErrorText = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string simpleAnalysisErrorText = string.Empty;

        public string SimpleAnalysisErrorText {
            get => simpleAnalysisErrorText;
            set {
                if (simpleAnalysisErrorText != value) {
                    this.simpleAnalysisErrorText = value;
                    RaisePropertyChanged();
                }
            }
        }

        private void ClearAnalyses(object o) {
            DeactivateAutoFocusAnalysis();
            ResetExposureAnalysis();
            AutoFocusChartActivatedOnce = false;
            TiltMeasurementActivatedOnce = false;
            TiltModel.Reset();
            SensorModel.Clear();
            AutoFocusCompleted = false;
            ResetErrors();
        }

        private void ActivateAutoFocusChart() {
            AutoFocusChartActive = true;
            AutoFocusChartActivatedOnce = true;
        }

        private void ActivateTiltMeasurement() {
            SensorCurveModelActive = true;
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
            SensorCurveModelActive = false;
            TiltMeasurementActive = false;
            TiltMeasurementHistoryActive = false;
        }

        private void DeactivateExposureAnalysis() {
            FWHMContoursActive = false;
            EccentricityVectorsActive = false;
        }

        private void ResetErrors() {
            InspectorErrorText = string.Empty;
            SimpleAnalysisErrorText = string.Empty;
        }

        private void ResetExposureAnalysis() {
            DeactivateExposureAnalysis();
            ExposureAnalysisActivatedOnce = false;
            FWHMContourSceneContainer = null;
        }

        private ScottPlot.Plottable.MarkerPlot highlightedEccentricityPoint;
        private ScottPlot.Plottable.ScatterPlot eccentricityCenterPoints;
        private int lastHighlightedEccentricityPointIndex;
        private double[] eccentricityValues;
        private double[] rotationValues;
    }
}