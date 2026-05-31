#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Math;
using KdTree;
using KdTree.Math;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Inspection {

    public class SensorDetectedStars {

        public SensorDetectedStars(
            double focuserPosition,
            HocusFocusStarDetectionResult starDetectionResult,
            IRenderedImage image) {
            this.FocuserPosition = focuserPosition;
            this.StarDetectionResult = starDetectionResult;
            this.Image = image;
        }

        public double FocuserPosition { get; private set; }
        public HocusFocusStarDetectionResult StarDetectionResult { get; private set; }
        public IRenderedImage Image { get; private set; }
        public bool HasBeenAligned { get; set; }
        public Matrix3x2? AlignmentTransform { get; set; }

        public override string ToString() {
            return $"{{{nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(StarDetectionResult)}={StarDetectionResult}}}";
        }
    }

    public class SensorParaboloidTiltHistoryModel {

        public SensorParaboloidTiltHistoryModel(
            int historyId,
            System.Drawing.Size imageSize,
            double pixelSizeMicrons,
            double fRatio,
            double focuserSizeMicrons,
            double finalFocusPosition,
            double tiltEffectMicrons,
            double curvatureEffectMicrons,
            double autoFocusOffset,
            TiltPlaneModel tiltPlaneModel,
            SensorParaboloidModel sensorModel) {
            HistoryId = historyId;
            ImageSize = imageSize;
            PixelSizeMicrons = pixelSizeMicrons;
            FRatio = fRatio;
            FocuserSizeMicrons = focuserSizeMicrons;
            FinalFocusPosition = finalFocusPosition;
            TiltEffectMicrons = tiltEffectMicrons;
            CurvatureEffectMicrons = curvatureEffectMicrons;
            AutoFocusOffset = autoFocusOffset;
            TiltPlaneModel = tiltPlaneModel;
            SensorModel = sensorModel;
        }

        public int HistoryId { get; private set; }
        public System.Drawing.Size ImageSize { get; private set; }
        public double PixelSizeMicrons { get; private set; }
        public double FRatio { get; private set; }
        public double FocuserSizeMicrons { get; private set; }
        public double FinalFocusPosition { get; private set; }
        public double TiltEffectMicrons { get; private set; }
        public double CurvatureEffectMicrons { get; private set; }
        public double AutoFocusOffset { get; private set; }
        public TiltPlaneModel TiltPlaneModel { get; private set; }
        public SensorParaboloidModel SensorModel { get; private set; }
    }

    public class SensorModel : BaseINPC {
        private readonly IProfileService profileService;
        private readonly IInspectorOptions inspectorOptions;
        private readonly IAutoFocusOptions autoFocusOptions;
        private readonly IAlglibAPI alglibAPI;
        private int nextHistoryId = 0;

        public SensorModel(IProfileService profileService, IInspectorOptions inspectorOptions, IAutoFocusOptions autoFocusOptions, IAlglibAPI alglibAPI) {
            this.profileService = profileService;
            this.inspectorOptions = inspectorOptions;
            this.autoFocusOptions = autoFocusOptions;
            this.alglibAPI = alglibAPI;
            SensorTiltHistoryModels = new AsyncObservableCollection<SensorParaboloidTiltHistoryModel>();
            RegistrationAndFitReport.CollectionChanged += RegistrationAndFitReport_CollectionChanged;
        }

        private void RegistrationAndFitReport_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            RaisePropertyChanged("RegistrationAndFitReport");
        }

        public Task UpdateModel(
            List<SensorDetectedStars> allDetectedStars,
            double fRatio,
            double focuserSizeMicrons,
            double finalFocusPosition,
            int stepSize,
            IProgress<ApplicationStatus> progress,
            CancellationToken ct) {
            if (allDetectedStars.Count == 0) {
                throw new ArgumentException("Cannot update sensor model. No detected stars provided");
            }

            return Task.Run(() => {
                ModelLoaded = false;
                var firstStarDetectionResult = allDetectedStars.First().StarDetectionResult;
                var imageSize = firstStarDetectionResult.ImageSize;
                var pixelSize = firstStarDetectionResult.PixelSize;
                Logger.Info($"Building Sensor Model. FRatio ({fRatio}), Focuser Size ({focuserSizeMicrons}), Pixel Size ({pixelSize}), Image size ({imageSize})");

                RegistrationAndFitReport.Clear();

                var (solution, fitResult) = RegisterStarsAndFit(allDetectedStars,
                    pixelSize: pixelSize,
                    focuserSizeMicrons: focuserSizeMicrons,
                    finalFocusPosition: finalFocusPosition,
                    imageSize: imageSize,
                    stepSize: stepSize,
                    progress: progress,
                    ct: ct);

                DisplayedSensorModel = solution;
                SensorModelResult.Update(
                    solution,
                    imageSize,
                    pixelSizeMicrons: pixelSize,
                    fRatio: fRatio,
                    focuserStepSizeMicrons: focuserSizeMicrons,
                    finalFocusPosition: finalFocusPosition,
                    registeredStars: fitResult.RegisteredStars,
                    acceptableRSquaredMin: inspectorOptions.AcceptableRSquaredMin);

                var historyId = Interlocked.Increment(ref nextHistoryId);
                SensorTiltHistoryModels.Insert(0, new SensorParaboloidTiltHistoryModel(
                    historyId: historyId,
                    pixelSizeMicrons: pixelSize,
                    fRatio: fRatio,
                    imageSize: imageSize,
                    focuserSizeMicrons: focuserSizeMicrons,
                    finalFocusPosition: finalFocusPosition,
                    sensorModel: solution,
                    tiltEffectMicrons: SensorModelResult.TiltEffectMicrons,
                    curvatureEffectMicrons: SensorModelResult.CurvatureEffectMicrons,
                    autoFocusOffset: SensorModelResult.AutoFocusMeanOffset,
                    tiltPlaneModel: SensorModelResult.TiltPlaneModel));
                SelectedTiltHistoryModel = null;
                ModelLoaded = true;
            }, ct);
        }

        private SensorParaboloidModel FitParaboloidModel(
            double focuserSizeMicrons,
            double finalFocusPosition,
            System.Drawing.Size imageSize,
            double pixelSize,
            RegistrationAndFitResult fitResult,
            CancellationToken ct,
            IProgress<ApplicationStatus> progress) {
            var dataPoints = fitResult.Points;
            if (dataPoints.Count < 9) {
                throw new Exception($"Need at least 9 registered stars. Found {dataPoints.Count}");
            }

            var sensorModelSolver = new SensorParaboloidSolver(
                dataPoints: dataPoints,
                sensorSizeMicronsX: imageSize.Width * pixelSize,
                sensorSizeMicronsY: imageSize.Height * pixelSize,
                inFocusMicrons: finalFocusPosition * focuserSizeMicrons,
                fixedSensorCenter: inspectorOptions.FixedSensorCenter,
                astigmatic: inspectorOptions.AstigmaticCurvatureEnabled);
            var nlSolver = new NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel>(this.alglibAPI);

            // Single solve: the signed curvature coefficient K can cross zero, so one fit covers both
            // curvature signs (the old code solved once forcing positive curvature and once forcing
            // negative, then kept the lower-RMS result — double the optimizer work and a discontinuity
            // at flat fields).
            var solution = nlSolver.SolveWinsorizedResiduals(sensorModelSolver, ct: ct, progress: progress);
            ct.ThrowIfCancellationRequested();
            solution.EvaluateFit(nlSolver, sensorModelSolver);
            Logger.Info($"Solved surface model: {solution}. RMS = {solution.RMSErrorMicrons:0.0000}, GoD: {solution.GoodnessOfFit:0.0000}, ReducedChiSq: {solution.ReducedChiSquared:0.0000}, Stars: {solution.StarsInModel}");

            return solution;
        }

        private List<SensorParaboloidDataPoint> ToInterpolatedGrid(
            List<SensorParaboloidDataPoint> dataPoints,
            System.Drawing.Size imageSize) {
            var allPointsTree = new KdTree<double, object>(2, new DoubleMath(), AddDuplicateBehavior.Error);
            foreach (var dataPoint in dataPoints) {
                allPointsTree.Add(new[] { dataPoint.X, dataPoint.Y }, null);
            }

            int starCount = 0;
            double totalDistance = 0.0d;
            foreach (var node in allPointsTree) {
                var nearestNeighbors = allPointsTree.GetNearestNeighbours(node.Point, 2);
                if (nearestNeighbors.Length < 2) {
                    continue;
                }

                var nearestPoint = nearestNeighbors[1].Point;
                var distance = Math.Sqrt((node.Point[0] - nearestPoint[0]) * (node.Point[0] - nearestPoint[0]) + (node.Point[1] - nearestPoint[1]) * (node.Point[1] - nearestPoint[1]));
                ++starCount;
                totalDistance += distance;
            }

            var meanDistance = totalDistance / starCount;
            var gridCellSize = meanDistance;
            var gridCellWidthCount = Math.Max(3, (int)(imageSize.Width / gridCellSize));
            if (gridCellWidthCount % 2 == 0) {
                // Ensure odd to cover the center point
                ++gridCellWidthCount;
            }
            var gridCellHeightCount = Math.Max(3, (int)(imageSize.Height / gridCellSize));
            if (gridCellHeightCount % 2 == 0) {
                // Ensure odd to cover the center point
                ++gridCellHeightCount;
            }

            alglib.rbfmodel model = null;
            alglib.rbfreport rep = null;
            try {
                this.alglibAPI.rbfcreate(2, 1, out model);

                double[,] xy = new double[dataPoints.Count, 3];
                for (int i = 0; i < dataPoints.Count; ++i) {
                    var dataPoint = dataPoints[i];
                    xy[i, 0] = dataPoint.X;
                    xy[i, 1] = dataPoint.Y;
                    xy[i, 2] = dataPoint.FocuserPosition;
                }

                alglib.rbfsetpoints(model, xy);
                double lambda, lambdaNS;
                if (this.inspectorOptions.InterpolationAmount == InterpolationAmountEnum.Small) {
                    lambda = 1.0e-6;
                    lambdaNS = 1.0e-6;
                } else if (this.inspectorOptions.InterpolationAmount == InterpolationAmountEnum.Medium) {
                    lambda = 1.0e-3;
                    lambdaNS = 1.0e-4;
                } else if (this.inspectorOptions.InterpolationAmount == InterpolationAmountEnum.Large) {
                    lambda = 1.0;
                    lambdaNS = 1.0e-2;
                } else {
                    throw new ArgumentException($"Interpolation Smoothing Amount {this.inspectorOptions.InterpolationAmount} not expected");
                }

                if (this.inspectorOptions.InterpolationAlgo == InterpolationAlgoEnum.Hierarchical) {
                    alglib.rbfsetalgohierarchical(model, meanDistance * 3.0d, 5, lambdaNS);
                } else if (this.inspectorOptions.InterpolationAlgo == InterpolationAlgoEnum.ThinPlateSpline) {
                    alglib.rbfsetalgothinplatespline(model, lambda);
                } else if (this.inspectorOptions.InterpolationAlgo == InterpolationAlgoEnum.MultiQuadric) {
                    alglib.rbfsetalgomultiquadricauto(model, lambda);
                } else if (this.inspectorOptions.InterpolationAlgo == InterpolationAlgoEnum.BiHarmonic) {
                    alglib.rbfsetalgobiharmonic(model, lambda);
                } else {
                    throw new ArgumentException($"InterpolationAlgo {this.inspectorOptions.InterpolationAlgo} not expected");
                }

                alglib.rbfbuildmodel(model, out rep);
                if (rep.terminationtype != 1) {
                    string reason;
                    if (rep.terminationtype == -5) {
                        reason = "non-distinct basis function centers were detected";
                    } else if (rep.terminationtype == -4) {
                        reason = "non-convergence";
                    } else if (rep.terminationtype == -3) {
                        reason = "incorrect model construction algorithm was chosen";
                    } else {
                        reason = "Unknown";
                    }
                    throw new Exception($"RBF interpolation failed with type {rep.terminationtype} and reason: {reason}");
                }

                double[] gridWidthNodes = new double[gridCellWidthCount];
                var gridWidthInterval = Math.Ceiling((double)imageSize.Width / (gridCellWidthCount - 1));
                var nextPosition = 0.0d;
                for (int i = 0; i < gridCellWidthCount; ++i) {
                    gridWidthNodes[i] = Math.Min(nextPosition, imageSize.Width - 1);
                    nextPosition += gridWidthInterval;
                }

                double[] gridHeightNodes = new double[gridCellHeightCount];
                var gridHeightInterval = Math.Ceiling((double)imageSize.Height / (gridCellHeightCount - 1));
                nextPosition = 0.0d;
                for (int i = 0; i < gridCellHeightCount; ++i) {
                    gridHeightNodes[i] = Math.Min(nextPosition, imageSize.Height - 1);
                    nextPosition += gridHeightInterval;
                }

                double[] outputNodes;
                alglib.rbfgridcalc2v(model, gridWidthNodes, gridWidthNodes.Length, gridHeightNodes, gridHeightNodes.Length, out outputNodes);

                var outputDataPoints = new List<SensorParaboloidDataPoint>();
                int outIdx = 0;
                for (int yIdx = 0; yIdx < gridCellHeightCount; ++yIdx) {
                    var y = gridHeightNodes[yIdx];
                    for (int xIdx = 0; xIdx < gridCellWidthCount; ++xIdx, ++outIdx) {
                        var x = gridWidthNodes[xIdx];
                        if (allPointsTree.RadialSearch(new double[] { x, y }, meanDistance, 1).Length > 0) {
                            outputDataPoints.Add(new SensorParaboloidDataPoint(x: x, y: y, focuserPosition: outputNodes[outIdx], rSquared: 1.0d));
                        }
                    }
                }
                return outputDataPoints;
            } finally {
                if (rep != null) {
                    this.alglibAPI.deallocateimmediately(ref rep);
                }
                if (model != null) {
                    this.alglibAPI.deallocateimmediately(ref model);
                }
            }
        }

        private enum IterationDirection { None, Up, Down };

        private const int searchRadiusRANSAC = 10;
        private const int searchRadiusNonRANSAC = 30;

        // Fixed default starting point for the brightness-tolerance search when StartingBrightnessDiff
        // is set to "auto" (-1). Previously the search seeded from (and wrote back to) the cross-run
        // PreviousRunBrightnessDiff state, which made consecutive runs of the unchanged process diverge.
        private const double DefaultStartingBrightnessDiff = 0.1d;

        // Hard cap on the brightness-tolerance search so acceptance no longer depends on wall-clock
        // time (which previously relaxed the R² target on slow machines) and the loop always terminates.
        private const int MaxBrightnessSearchIterations = 12;

        /// <summary>
        /// Optional sink for human-readable registration/fit messages. When set (e.g. by tests), report
        /// messages bypass the UI-bound <see cref="RegistrationAndFitReport"/> collection, allowing the
        /// registration+fit core to run headless without a dispatcher.
        /// </summary>
        public Action<string> RegistrationReportSink { get; set; }

        private void Report(string message) {
            if (RegistrationReportSink != null) {
                RegistrationReportSink(message);
            } else {
                RegistrationAndFitReport.Add(message);
            }
        }

        // Public testability seam: runs the full registration + per-star + paraboloid fit on the supplied
        // frames and returns the model. Decoupled from UI (via RegistrationReportSink) so it can be driven
        // end-to-end from tests. Production callers go through UpdateModel.
        public (SensorParaboloidModel, RegistrationAndFitResult) RegisterStarsAndFit(
            List<SensorDetectedStars> allDetectedStars,
            System.Drawing.Size imageSize,
            double focuserSizeMicrons,
            double finalFocusPosition,
            double pixelSize,
            IProgress<ApplicationStatus> progress,
            int stepSize,
            CancellationToken ct) {
            using (var stopwatch = MultiStopWatch.Measure()) {
                int maxStarsPerRegion = inspectorOptions.MaxStarsPerRegion;
                int maxStars = 0;
                int refIndex = -1;
                for (int i = 0; i < allDetectedStars.Count; ++i) {
                    if (allDetectedStars[i].StarDetectionResult.StarList.Count > maxStars) {
                        refIndex = i;
                        maxStars = allDetectedStars[i].StarDetectionResult.StarList.Count;
                    }
                }
                ReferenceImage = refIndex;

                ct.ThrowIfCancellationRequested();

                RegisteredStar[] registeredStars = null;
                TrianglesByImage = new Dictionary<int, List<RANSACRegistration.StarTriangle>>();

                // set relative brightness level for each star in each image
                foreach (var detectedStars in allDetectedStars) {
                    double imageMaxBrightness = detectedStars.StarDetectionResult.StarList.Max(s => s.AverageBrightness);
                    double imageMinBrightness = detectedStars.StarDetectionResult.StarList.Min(s => s.AverageBrightness);
                    foreach (var (star, index) in detectedStars.StarDetectionResult.StarList
                                                                    .Select((star, index) => ((HocusFocusDetectedStar)star, index))) {
                        star.NormalisedBrightness = (float)((star.AverageBrightness - imageMinBrightness) / (imageMaxBrightness - imageMinBrightness));
                        star.OriginalPosition = star.Position;
                    }
                }
                stopwatch.RecordEntry("normalise brightness");
                ct.ThrowIfCancellationRequested();

                int ransacAligned = 0;
                if (inspectorOptions.UseRANSAC) {
                    ransacAligned = AlignStarsWithRANSAC(allDetectedStars, imageSize, stopwatch, ReferenceImage, progress);
                    if (ransacAligned < allDetectedStars.Count) {
                        Logger.Info("Ransac failed on at least one image.  Search radius will remain the same as for non-aligned processing");
                        if (ransacAligned == 1) { // ransacAligned starts at 1 for the reference image so if it's still 1 it means no other images aligned
                            Report("All frames failed to align.  An autofocus run where all images align will give more reliable results.");
                        } else {
                            Report($"{allDetectedStars.Count - ransacAligned} frames failed to align.  An autofocus run where all images align will give more reliable results.");
                        }
                    }
                }
                ct.ThrowIfCancellationRequested();

                // Stateless starting point: the configured StartingBrightnessDiff, or a fixed default
                // when set to "auto" (-1). startingBrightnessDiff is a local pivot for the bidirectional
                // up/down search below, so the search no longer depends on (or mutates) cross-run state.
                double startingBrightnessDiff = (inspectorOptions.StartingBrightnessDiff != -1) ? inspectorOptions.StartingBrightnessDiff : DefaultStartingBrightnessDiff;
                double maxNormalisedBrightnessDiff = startingBrightnessDiff;
                SensorParaboloidModel bestPfit = null;
                RegistrationAndFitResult bestReg = null;
                double bestBrightnessDiff = maxNormalisedBrightnessDiff;

                Dictionary<double, (SensorParaboloidModel, RegistrationAndFitResult)> previousRuns = new();
                IterationDirection direction = IterationDirection.Up;
                bool upExhausted = false;
                bool downExhausted = false;
                SensorParaboloidModel prevFit = null;
                RegistrationAndFitResult prevReg = null;

                Logger.Debug($"Starting fit loop with previous MNBD of {maxNormalisedBrightnessDiff:#.##}");

                int iterations = 0;
                for (bool retry = true; retry;) {
                    if (iterations++ >= MaxBrightnessSearchIterations) {
                        Logger.Warning($"Brightness-tolerance search hit the {MaxBrightnessSearchIterations}-iteration cap; stopping further iterations.");
                        break;
                    }
                    retry = false;
                    int rejectionsOnBrightnessDiff;
                    (registeredStars, rejectionsOnBrightnessDiff) = MatchStarsUsingKdTree(allDetectedStars,
                        stopwatch, ReferenceImage,
                        ((inspectorOptions.UseRANSAC) && (ransacAligned == allDetectedStars.Count)) ? searchRadiusRANSAC : searchRadiusNonRANSAC,
                        inspectorOptions.RejectBadBrightnessMatches ? maxNormalisedBrightnessDiff : -1,
                        progress);

                    // registration phase done
                    stopwatch.RecordEntry("registration");
                    ct.ThrowIfCancellationRequested();

                    RegistrationAndFitResult reg = FitImages(imageSize, focuserSizeMicrons, pixelSize, stepSize, stopwatch, registeredStars, progress, inspectorOptions.RejectBadlyFittingMatches);
                    SensorParaboloidModel pfit = null;

                    if (reg.Points.Count >= 9) {  // 9 points is the minimum for fitting the model
                        pfit = FitParaboloidModel(focuserSizeMicrons, finalFocusPosition, imageSize, pixelSize, reg, ct, progress);
                    } else {
                        retry = true;   // try again at a different brightness tolerance
                    }

                    if (inspectorOptions.RejectBadBrightnessMatches) {
                        // Uncertainty-aware, magnitude-independent acceptance: stop iterating once the fit
                        // has enough stars and a reduced χ² within the acceptable band. This replaces the
                        // former R²-vs-target test (and the wall-clock target removed in Phase 1), which
                        // rejected near-flat-but-good sensors and varied with machine load.
                        if ((pfit != null) && ((pfit.StarsInModel < 10) || !SensorAberrationCalculator.IsModelAcceptable(pfit, inspectorOptions.AcceptableRSquaredMin))) {
                            Logger.Debug($"Only have {pfit.StarsInModel} stars and reduced χ² of {pfit.ReducedChiSquared:#.##} (cap {SensorAberrationCalculator.AcceptableReducedChiSquared:#.##} only applies when R² {pfit.GoodnessOfFit:#.##} < min {inspectorOptions.AcceptableRSquaredMin:#.##}) with a brightness tolerance of {maxNormalisedBrightnessDiff:#.##}");
                            retry = true;
                        }

                        if (IsBetterFit(pfit, bestPfit)) {
                            bestPfit = pfit;
                            bestReg = reg;
                            bestBrightnessDiff = maxNormalisedBrightnessDiff;
                        }

                        if (previousRuns.ContainsKey(maxNormalisedBrightnessDiff)) {
                            previousRuns[maxNormalisedBrightnessDiff] = (pfit, reg);
                        } else {
                            previousRuns.Add(maxNormalisedBrightnessDiff, (pfit, reg));
                        }

                        if (retry) {    // keep going in the same direction
                            Logger.Debug($"this brightness tolerance={maxNormalisedBrightnessDiff:#.##}, direction: {direction}");
                            if (direction == IterationDirection.Up) {
                                if ((maxNormalisedBrightnessDiff >= 3) || (rejectionsOnBrightnessDiff == 0)) {    // if there're no rejections at the current level don't increase tolerance
                                    upExhausted = true;
                                }
                                if ((pfit != null && !IsFitTooGood(pfit) && (!IsBetterFit(pfit, prevFit)) || upExhausted)) { // change direction unless already exhausted
                                    if (downExhausted) { // all tried, no retry
                                        retry = false;
                                    } else {
                                        if (maxNormalisedBrightnessDiff != startingBrightnessDiff)
                                            upExhausted = true;
                                        maxNormalisedBrightnessDiff = startingBrightnessDiff / 1.5;
                                        direction = IterationDirection.Down;
                                        Logger.Debug($"Switching direction to {direction}");
                                    }
                                } else {
                                    maxNormalisedBrightnessDiff *= 1.5;
                                    direction = IterationDirection.Up;
                                }
                            } else {
                                if (maxNormalisedBrightnessDiff <= 0.01d)
                                    downExhausted = true;
                                if ((pfit != null && !IsFitTooGood(pfit) && (!IsBetterFit(pfit, prevFit)) || downExhausted)) { // need to switch direction unless already exhausted
                                    if (upExhausted) { // all tried, no retry
                                        retry = false;
                                    } else {
                                        if (maxNormalisedBrightnessDiff != startingBrightnessDiff)
                                            downExhausted = true;
                                        maxNormalisedBrightnessDiff = startingBrightnessDiff * 1.5;
                                        direction = IterationDirection.Up;
                                        Logger.Debug($"Switching direction to {direction}");
                                    }
                                } else {
                                    maxNormalisedBrightnessDiff /= 1.5;
                                    direction = IterationDirection.Down;
                                }
                            }
                            if (retry) {
                                Logger.Debug($"next brightness tolerance={maxNormalisedBrightnessDiff:#.##}, direction: {direction}");
                            } else {
                                Logger.Debug("No more iterations");
                            }
                        }

                        prevFit = pfit;
                        prevReg = reg;

                        Logger.Debug($"End of registerStarsAndFit iteration with R2 of {bestPfit?.GoodnessOfFit:#.##}, retry is {retry}");
                    } else {        // we're not trying to find a better return as we're not rejecting on brightness
                        bestPfit = pfit;
                        bestReg = reg;
                        bestBrightnessDiff = maxNormalisedBrightnessDiff;
                    }
                    Logger.Info($"After {iterations} iterations best fit is {bestPfit?.GoodnessOfFit:#.##}");
                }

                progress.Report(new ApplicationStatus());

                // Display-only: surfaces the winning tolerance in the UI hint. The search no longer
                // reads this value back, so writing it here does not affect the repeatability of future runs.
                inspectorOptions.PreviousRunBrightnessDiff = bestBrightnessDiff;
                previousRuns.Select(r => (r.Key, r.Value.Item1?.GoodnessOfFit, r.Value.Item1?.StarsInModel))
                    .ToList()
                    .ForEach(run => Logger.Debug($"Previous run: brightness tolerance {run.Key:#.##}, R2 {run.GoodnessOfFit:#.##}, Stars {run.StarsInModel} best? {run.Key == bestBrightnessDiff}"));

                if (bestPfit == null) {
                    throw new Exception("Failed to find a good model.");
                }
                // Reduced χ² is only a rejection criterion when R² is very low: fail only when the model
                // explains almost no variance (R² below the minimum) AND cannot fit the best-focus positions
                // within their uncertainties (reduced χ² above the fixed cap). A well-fit model with an
                // adequate R² is accepted even when its reduced χ² is large.
                if (!SensorAberrationCalculator.IsModelAcceptable(bestPfit, inspectorOptions.AcceptableRSquaredMin)) {
                    throw new Exception($"Sensor modeling failed. R² = {bestPfit.GoodnessOfFit:#.00} (min {inspectorOptions.AcceptableRSquaredMin:#.00}) and reduced χ² = {bestPfit.ReducedChiSquared:#.00} exceeds {SensorAberrationCalculator.AcceptableReducedChiSquared:#.00}");
                }

                if (bestPfit.StarsInModel < 10) {
                    if (inspectorOptions.UseRANSAC) {
                        Report($"There are very few stars in the model ({bestPfit.StarsInModel}).  There may be poor transparancy or seeing.  Frames with more stars will give more reliable results.");
                    } else {
                        Report($"There are very few stars in the model ({bestPfit.StarsInModel}).  If there is movement between the frames, it may help to enable the 'align images' option.");
                    }
                }

                return (bestPfit, bestReg);
            }
        }

        // Rough per-detection HFR uncertainty used to weight the per-star hyperbolic fit by 1/σ². HFR is a
        // size measurement whose relative error scales ~1/SNR; the SNR proxy uses the only fields available
        // on a detected star (mean brightness above background, with a shot-noise-like denominator). This
        // makes the previously no-op WeightedHyperbolicFitEnabled meaningful. It does not affect the
        // best-focus standard error, which is self-calibrated from the fit residuals.
        private static double EstimateHfrStdDev(HocusFocusDetectedStar star) {
            var signal = star.AverageBrightness - star.Background;
            var noise = Math.Sqrt(Math.Max(star.AverageBrightness, 1.0));
            var snr = signal > 0 ? signal / noise : 0.0;
            var sigma = star.HFR / Math.Max(snr, 1.0);
            return Math.Max(sigma, 1e-3);
        }

        private static bool IsFitTooGood(SensorParaboloidModel fit) =>
            SensorAberrationCalculator.IsFitTooGood(fit);

        private static bool IsBetterFit(SensorParaboloidModel thisFit, SensorParaboloidModel otherFit) =>
            SensorAberrationCalculator.IsBetterFit(thisFit, otherFit);

        private static List<StarDetectionRegion> CreateFullRegionSet(System.Drawing.Size imageSize, int rows, int cols) =>
            SensorAberrationCalculator.CreateFullRegionSet(imageSize, rows, cols);

        private RegistrationAndFitResult FitImages(
                System.Drawing.Size imageSize,
                double focuserSizeMicrons,
                double pixelSize,
                int stepSize,
                MultiStopWatch stopwatch,
                RegisteredStar[] registeredStars,
                IProgress<ApplicationStatus> progress,
                bool rejectBadlyFittingMatches) {
            int discardedStarCount = 0;
            var sensorModelDataPoints = new List<SensorParaboloidDataPoint>();
            // Best-focus points collected with their per-star σ (NaN when the hyperbolic fit could not
            // estimate a standard error). σ is resolved to a concrete weight in a second pass below.
            var pendingPoints = new List<(double X, double Y, double FocuserMicrons, double RSquared, double StdDevMicrons)>();
            var maxOutlierRejectedPoints = this.autoFocusOptions.MaxOutlierRejections;
            var rejectionConfidence = this.autoFocusOptions.OutlierRejectionConfidence;
            const int minStarCountForFitting = 5;
            int totalRejectedPointCount = 0;
            // Per-star hyperbolic fitting dominates the model build — with the Hybrid model each star fits
            // several candidate curves, so this loop runs tens of seconds for thousands of stars. Report a
            // "Fitting sensor model" status so the UI reflects this phase instead of freezing on the previous
            // ("Matching stars") status. Throttled to ~100 updates to avoid flooding the dispatcher.
            var starCount = registeredStars.Length;
            var reportEvery = Math.Max(1, starCount / 100);
            for (int registeredStarIndex = 0; registeredStarIndex < starCount; ++registeredStarIndex) {
                var registeredStar = registeredStars[registeredStarIndex];
                if (registeredStarIndex % reportEvery == 0) {
                    progress?.Report(new ApplicationStatus() {
                        Status = "Fitting sensor model",
                        Status2 = "Star",
                        ProgressType2 = ApplicationStatus.StatusProgressType.ValueOfMaxValue,
                        MaxProgress2 = starCount,
                        Progress2 = registeredStarIndex + 1
                    });
                }
                if (registeredStar.MatchedStars.Count < minStarCountForFitting) {
                    continue;
                }

                try {
                    var points = registeredStar.MatchedStars.Select(s => new ScatterErrorPoint(s.FocuserPosition, s.Star.HFR, 0.0d, EstimateHfrStdDev(s.Star))).ToList();
                    // When the option is Hybrid, run the full best-fit selection on this star's curve and use the
                    // winning concrete model for the (rejection) fit below — each star self-selects the most
                    // trustworthy model. Non-Hybrid options pass straight through unchanged.
                    var modelForStar = autoFocusOptions.HyperbolicFitModel;
                    if (modelForStar == HyperbolicFitModel.Hybrid) {
                        modelForStar = AlglibHyperbolicFitting.SelectBestModel(this.alglibAPI, points, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled, out _);
                    }
                    var rejectedPoints = new List<ScatterErrorPoint>();
                    bool continueFitting;
                    AlglibHyperbolicFitting fitting;
                    bool solveResult;
                    do {
                        continueFitting = false;
                        fitting = AlglibHyperbolicFitting.Create(this.alglibAPI, modelForStar, points, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled);

                        solveResult = fitting.Solve();
                        if (rejectBadlyFittingMatches) {
                            if (solveResult && rejectedPoints.Count < maxOutlierRejectedPoints && points.Count > minStarCountForFitting) {
                                var rejectedPoint = MathUtility.RejectionTest(points: points, fitting: fitting.Fitting, confidence: rejectionConfidence, weights: AlglibHyperbolicFitting.BuildResidualWeights(points, autoFocusOptions.WeightedHyperbolicFitEnabled));
                                if (rejectedPoint != null) {
                                    rejectedPoints.Add(rejectedPoint);
                                    points.Remove(rejectedPoint);
                                    continueFitting = true;
                                }
                            }
                        }
                    } while (continueFitting);

                    if (!solveResult) {
                        Logger.Trace($"Failed to fit hyperbolic curve to star matches at ({registeredStar.RegistrationX:0.00}, {registeredStar.RegistrationY:0.00})");
                        discardedStarCount++;
                        continue;
                    }

                    if (fitting.RSquared < 0.90) {
                        // Discard bad fitting
                        discardedStarCount++;
                        continue;
                    }

                    totalRejectedPointCount += rejectedPoints.Count;
                    var dataPointX = (registeredStar.RegistrationX - (imageSize.Width / 2.0)) * pixelSize;
                    var dataPointY = (registeredStar.RegistrationY - (imageSize.Height / 2.0)) * pixelSize;
                    var focuserMicrons = fitting.Minimum.X * focuserSizeMicrons;
                    // Propagate the standard error of this star's best-focus position into the paraboloid
                    // fit so it is weighted by 1/σ². NaN marks "no estimate available"; resolved below to the
                    // median of the available σ (rather than a fixed 1 µm, which would give such points a far
                    // larger weight than well-measured stars and distort the χ² gate).
                    var bestFocusStdDevMicrons = (!double.IsNaN(fitting.MinimumStdError) && !double.IsInfinity(fitting.MinimumStdError) && fitting.MinimumStdError > 0.0)
                        ? fitting.MinimumStdError * focuserSizeMicrons
                        : double.NaN;
                    pendingPoints.Add((dataPointX, dataPointY, focuserMicrons, fitting.RSquared, bestFocusStdDevMicrons));
                } catch (Exception e) {
                    Logger.Error(e, $"Failed to calculate hyperbolic at ({registeredStar.RegistrationX}, {registeredStar.RegistrationY}). Error={e.Message}");
                }
            }

            // Resolve missing σ to the median of the available ones (or 1.0 if none could be estimated), so a
            // star whose best-focus standard error is unknown is weighted like a typical star rather than
            // dominating the fit.
            var availableStdDevs = pendingPoints.Where(p => !double.IsNaN(p.StdDevMicrons)).Select(p => p.StdDevMicrons).ToList();
            var fallbackStdDevMicrons = availableStdDevs.Count > 0 ? availableStdDevs.MedianMAD().Item1 : 1.0;
            foreach (var p in pendingPoints) {
                var stdDev = double.IsNaN(p.StdDevMicrons) ? fallbackStdDevMicrons : p.StdDevMicrons;
                sensorModelDataPoints.Add(new SensorParaboloidDataPoint(p.X, p.Y, p.FocuserMicrons, p.RSquared, stdDev));
            }

            stopwatch.RecordEntry("fitcurves");
            if (discardedStarCount > 0) {
                Logger.Warning($"Discarded {discardedStarCount} stars during sensor modeling due to poor fits");
            }
            if (totalRejectedPointCount > 0) {
                Logger.Info($"Rejected {totalRejectedPointCount} points while fitting {sensorModelDataPoints.Count} stars");
            }

            if (this.inspectorOptions.InterpolationEnabled && sensorModelDataPoints.Count > 5) {
                return new RegistrationAndFitResult(ToInterpolatedGrid(sensorModelDataPoints, imageSize), registeredStars);
            } else {
                return new RegistrationAndFitResult(sensorModelDataPoints, registeredStars);
            }
        }

        public int ReferenceImage { get; set; }

        public Dictionary<int, List<RANSACRegistration.StarTriangle>> TrianglesByImage;

        private void ResetRegistration(List<SensorDetectedStars> allDetectedStars) {
            for (int image = 0; image < allDetectedStars.Count; image++) {
                foreach (var star in allDetectedStars[image].StarDetectionResult.StarList) {
                    ((HocusFocusDetectedStar)star).Position = ((HocusFocusDetectedStar)star).OriginalPosition;
                }
            }
        }

        // Max Euclidean distance, in similarity-invariant shape-descriptor space (sorted side-length ratios),
        // for two triangles to be accepted as a putative match. Replaces the former near-1 cosine-similarity
        // thresholds, which were brittle to seeing/centroiding noise. Strict is tried first; if it yields too
        // few matches the relaxed (larger) tolerance is used. Ratios are O(1), so these are small fractions.
        private const double maxShapeDistanceStrict = 0.02;
        private const double maxShapeDistanceRelaxed = 0.05;

        private int AlignStarsWithRANSAC(
            List<SensorDetectedStars> allDetectedStars,
            System.Drawing.Size imageSize,
            MultiStopWatch stopwatch,
            int referenceImage,
            IProgress<ApplicationStatus> progress) {
            int imagesAligned = 1;  // count the reference image as aligned

            var referenceStars = allDetectedStars[referenceImage].StarDetectionResult.StarList
                .Select(s => new Point2D(s.Position.X, s.Position.Y, ((HocusFocusDetectedStar)s).NormalisedBrightness));

            // first pass - build list of triangles in reference image
            int maxTriangleSize;
            List<RANSACRegistration.StarTriangle> refTriangles;

            // aim for ~100 triangles
            int minTri = 100;
            int maxTri = 200;
            double stepSize = 0.005;
            double sizeAsPortion = 0.0055;
            double minSize = 0.001;
            double maxSize = 0.1;

            IterationDirection direction = IterationDirection.None;
            do {
                maxTriangleSize = (int)(sizeAsPortion * Math.Min(imageSize.Width, imageSize.Height));
                refTriangles = RANSACRegistration.BuildStarTriangles(imageSize, referenceStars.ToList(), maxTriangleSize, true, true);
                if ((refTriangles.Count < minTri) && (direction != IterationDirection.Down)) {
                    direction = IterationDirection.Up;
                    sizeAsPortion += stepSize;
                    if (sizeAsPortion > maxSize) {
                        break;
                    }
                } else {
                    if ((refTriangles.Count > maxTri) && (direction != IterationDirection.Up)) {
                        direction = IterationDirection.Down;
                        sizeAsPortion -= stepSize;
                        if (sizeAsPortion < minSize) {
                            break;
                        }
                    } else {
                        break;
                    }
                }
            } while (true);
            TrianglesByImage.Add(referenceImage, refTriangles);
            Logger.Info($"Image {referenceImage}: {refTriangles.Count} triangles (REFERENCE), max size: {maxTriangleSize} ({sizeAsPortion}), stars: {referenceStars.Count()}");

            int TooFewTrianglesImages = 0;
            for (int imageIndex = 0; imageIndex < allDetectedStars.Count; ++imageIndex) {
                if (imageIndex == referenceImage) {
                    allDetectedStars[imageIndex].HasBeenAligned = true;
                    continue;
                }
                ApplicationStatus status = new ApplicationStatus() {
                    Status = "Aligning images",
                    Status2 = "Image",
                    ProgressType2 = ApplicationStatus.StatusProgressType.ValueOfMaxValue,
                    MaxProgress2 = allDetectedStars.Count,
                    Progress2 = imageIndex + 1
                };
                progress.Report(status);
                var theseStars = allDetectedStars[imageIndex].StarDetectionResult.StarList
                    .Select(s => new Point2D(s.Position.X, s.Position.Y, ((HocusFocusDetectedStar)s).NormalisedBrightness));

                List<Point2D> putativeSrc;
                List<Point2D> putativeDst;
                // find triangles in this image
                var theseTriangles = RANSACRegistration.BuildStarTriangles(imageSize, theseStars.ToList(), maxTriangleSize, false, false);
                Logger.Info($"Image {imageIndex}: {theseTriangles.Count} triangles");
                TrianglesByImage.Add(imageIndex, theseTriangles);

                if (theseTriangles.Count < minTri) {
                    TooFewTrianglesImages++;
                }

                // match triangles to reference triangles to get putative matches
                (putativeSrc, putativeDst) = RANSACRegistration.GeneratePutativeMatchesUsingSimilarTriangles(
                    theseTriangles,
                    refTriangles,
                    status,
                    maxShapeDistanceStrict);

                if (putativeDst.Count < 20) {
                    Logger.Debug($"Image {imageIndex}: too few triangles ({putativeDst.Count}) with strict shape tolerance, switching to relaxed mode");
                    // Reset the matched flags set by the strict pass so the relaxed pass can re-match freely.
                    foreach (var t in theseTriangles) {
                        t.ResetMatch();
                    }
                    (putativeSrc, putativeDst) = RANSACRegistration.GeneratePutativeMatchesUsingSimilarTriangles(
                        theseTriangles,
                        refTriangles,
                        status,
                        maxShapeDistanceRelaxed);
                }
                try {
                    Logger.Info($"Image {imageIndex}, putative star matches: {putativeDst.Count} out of {theseStars.Count()} stars");
                    // Calculate the transform needed to register this image. Frames in a single AF run differ
                    // only by small translation/rotation/scale, so a 4-DOF similarity transform is the
                    // physically correct model. The 6-DOF affine adds shear + anisotropic scale that let RANSAC
                    // "explain" mismatches by warping the field — degrading the data the paraboloid then fits.
                    // Affine is retained behind UseAffineAlignment for diagnostics only.
                    Matrix3x2 transform = inspectorOptions.UseAffineAlignment
                        ? RANSACRegistration.EstimateAffineTransform(putativeSrc, putativeDst, status, progress)
                        : RANSACRegistration.EstimateSimilarityTransform(putativeSrc, putativeDst, status, progress).ToMatrix3x2();
                    allDetectedStars[imageIndex].AlignmentTransform = transform;

                    // adjust each star according to the transform
                    for (int starIndex = 0; starIndex < allDetectedStars[imageIndex].StarDetectionResult.StarList.Count; starIndex++) {
                        var oldPoint = allDetectedStars[imageIndex].StarDetectionResult.StarList[starIndex].Position;

                        var transformedPoint = transform.Transform(new Point2D(allDetectedStars[imageIndex].StarDetectionResult.StarList[starIndex].Position));
                        allDetectedStars[imageIndex].StarDetectionResult.StarList[starIndex].Position = new Accord.Point((float)transformedPoint.X, (float)transformedPoint.Y);

                        transformedPoint = transform.Transform(new Point2D(allDetectedStars[imageIndex].StarDetectionResult.StarList[starIndex].BoundingBox.Location));
                        allDetectedStars[imageIndex].StarDetectionResult.StarList[starIndex].BoundingBox
                            = new System.Drawing.Rectangle(
                                        (int)transformedPoint.X,
                                        (int)transformedPoint.Y,
                                        allDetectedStars[imageIndex].StarDetectionResult.StarList[starIndex].BoundingBox.Width,
                                        allDetectedStars[imageIndex].StarDetectionResult.StarList[starIndex].BoundingBox.Height);
                    }
                    imagesAligned++;
                    allDetectedStars[imageIndex].HasBeenAligned = true;
                } catch (Exception ex) {
                    Logger.Info($"Image {imageIndex}: Error: {ex.Message}");
                    allDetectedStars[imageIndex].HasBeenAligned = false;
                }
            }

            if (TooFewTrianglesImages > 0) {
                if (refTriangles.Count < minTri) {
                    TooFewTrianglesImages++;    // include the reference image in this message
                }
                var imageCount = (TooFewTrianglesImages == allDetectedStars.Count) ? "All" : TooFewTrianglesImages.ToString();
                Report($"{imageCount} images had too few star triangles for reliable alignment.  Alignment may have failed for these images.  Check image quality or star detection parameters.");
            } else {
                if (refTriangles.Count < minTri) {
                    Logger.Warning("Too few star triangles found in reference image for reliable alignment.  Alignment may fail.");
                    Report("Too few star triangles found in reference image for reliable alignment.  Check image quality or star detection parameters.");
                }
            }

            Logger.Info($"RANSAC alignment: {imagesAligned} / {allDetectedStars.Count} images were successfully aligned");
            stopwatch.RecordEntry("RANSAC alignment");
            return imagesAligned;
        }

        private static (RegisteredStar[], int) MatchStarsUsingKdTree(
                List<SensorDetectedStars> allDetectedStars,
                MultiStopWatch stopwatch,
                int referenceImage,
                float searchRadius,
                double maxNormalisedBrightnessDiff,
                IProgress<ApplicationStatus> progress) {
            Logger.Debug("MatchStarsUsingKdTree");

            RegisteredStar[] registeredStars;
            var allDetectedStarTrees = allDetectedStars.Select(result => {
                var tree = new KdTree<float, DetectedStarIndex>(2, new FloatMath(), AddDuplicateBehavior.Error);
                foreach (var (star, starIndex) in result.StarDetectionResult.StarList.Select((star, starIndex) => ((HocusFocusDetectedStar)star, starIndex))) {
                    tree.Add(new[] { star.Position.X, star.Position.Y }, new DetectedStarIndex(starIndex, star));
                }
                return tree;
            }).ToArray();
            stopwatch.RecordEntry("build trees");

            var globalRegistry = new KdTree<float, DetectedStarIndex>(2, new FloatMath(), AddDuplicateBehavior.Error);
            var starIndexMap = Enumerable.Range(0, allDetectedStars.Count).Select(i => new Dictionary<int, int>()).ToArray();
            foreach (var starNode in allDetectedStarTrees[referenceImage]) {
                var nextIndex = globalRegistry.Count;
                globalRegistry.Add(starNode.Point, new DetectedStarIndex(nextIndex, starNode.Value.DetectedStar));
                starIndexMap[referenceImage].Add(starNode.Value.Index, nextIndex);
            }

            ApplicationStatus status = new ApplicationStatus() {
                Status = "Matching stars",
                MaxProgress = allDetectedStars.Count,
                ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue
            };
            int totalRejectionsOnBrightness = 0;
            for (int imageIndex = 0; imageIndex < allDetectedStars.Count; ++imageIndex) {
                if (imageIndex == referenceImage) {
                    continue;
                }
                status.Progress = imageIndex + 1; // 1-based count out of MaxProgress, not the 0-based index
                progress.Report(status);

                var nextStarList = allDetectedStars[imageIndex].StarDetectionResult.StarList;
                var nextStarTree = allDetectedStarTrees[imageIndex];
                var nextStarIndexMap = starIndexMap[imageIndex];
                var matchedGlobalStars = new bool[globalRegistry.Count];
                var matchedSourceStars = new bool[nextStarTree.Count];
                // Tracks whether a source star had ANY registry star within the search radius (regardless of
                // brightness or one-to-one contention). Used below to restrict frame-union reinjection to
                // stars that are genuinely absent from the registry's neighborhood.
                var sourceHadGlobalNeighbor = new bool[nextStarTree.Count];
                var queue = new KdTree.PriorityQueue<MatchingPair, double>(new DoubleMath());
                foreach (var (starNode, starNodeIndex) in nextStarTree.Select((starNode, starNodeIndex) => (starNode, starNodeIndex))) {
                    var sourceStar = starNode.Value.DetectedStar;
                    var sourcePoint = starNode.Point;
                    var sourceIndex = starNode.Value.Index;
                    var globalNeighbors = globalRegistry.RadialSearch(sourcePoint, searchRadius);
                    sourceHadGlobalNeighbor[sourceIndex] = globalNeighbors.Length > 0;
                    int queuedCount = 0;
                    foreach (var globalNeighbor in
                        maxNormalisedBrightnessDiff == -1 ? globalNeighbors :
                        // filter out bad matches on brightness
                        globalNeighbors.Where(p => Math.Abs(p.Value.DetectedStar.NormalisedBrightness - sourceStar.NormalisedBrightness) < maxNormalisedBrightnessDiff)
                        ) {
                        var globalNeighborIndex = globalNeighbor.Value.Index;
                        var neighboringStar = globalNeighbor.Value.DetectedStar;
                        var distance = MathUtility.SumOfSquaresOfDifferences(globalNeighbor.Point, sourcePoint);
                        queue.Enqueue(new MatchingPair() { SourceIndex = sourceIndex, GlobalIndex = globalNeighborIndex }, -distance);
                        queuedCount++;
                    }
                    int rejectedOnBrightness = globalNeighbors.Count() - queuedCount;
                    if (rejectedOnBrightness > 0)
                        Logger.Info($"ImageStar: {imageIndex}/{sourceIndex}, {rejectedOnBrightness} matches out of {globalNeighbors.Count()} rejected on brightness difference (>{maxNormalisedBrightnessDiff})");
                    totalRejectionsOnBrightness += rejectedOnBrightness;
                }

                status.Status = "Measuring matched star offset";
                status.MaxProgress = queue.Count;
                status.ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue;
                while (queue.Count > 0) {
                    var nextCandidate = queue.Dequeue();
                    if (matchedGlobalStars[nextCandidate.GlobalIndex] || matchedSourceStars[nextCandidate.SourceIndex]) {
                        continue;
                    }

                    status.Progress = (status.MaxProgress - queue.Count) + 1;
                    progress.Report(status);

                    nextStarIndexMap.Add(nextCandidate.SourceIndex, nextCandidate.GlobalIndex);
                    matchedGlobalStars[nextCandidate.GlobalIndex] = true;
                    matchedSourceStars[nextCandidate.SourceIndex] = true;
                }

                // Frame-union coverage: a source star with NO registry star within the search radius is a
                // physical star not present in (or not detectable from) the earlier frames. Add it as a new
                // registry entry so it still contributes to the model and can be matched by later frames.
                // Only genuinely-absent stars are added: a star that HAD a neighbor but went unmatched (lost
                // the one-to-one contention, or was rejected on brightness) is an ambiguous detection, not a
                // new star — reinjecting it would duplicate a nearby registry star and partially undo the
                // brightness filter. Restricting to no-neighbor stars also guarantees the new point cannot
                // coincide with an existing one, so globalRegistry's AddDuplicateBehavior.Error never trips.
                foreach (var starNode in nextStarTree) {
                    var unmatchedSourceIndex = starNode.Value.Index;
                    if (matchedSourceStars[unmatchedSourceIndex] || sourceHadGlobalNeighbor[unmatchedSourceIndex]) {
                        continue;
                    }
                    var newGlobalIndex = globalRegistry.Count;
                    globalRegistry.Add(starNode.Point, new DetectedStarIndex(newGlobalIndex, starNode.Value.DetectedStar));
                    nextStarIndexMap.Add(unmatchedSourceIndex, newGlobalIndex);
                }
            }

            registeredStars = new RegisteredStar[globalRegistry.Count];
            foreach (var globalNode in globalRegistry) {
                var registeredStar = new RegisteredStar() {
                    RegistrationX = globalNode.Value.DetectedStar.Position.X,
                    RegistrationY = globalNode.Value.DetectedStar.Position.Y
                };
                registeredStars[globalNode.Value.Index] = registeredStar;
            }

            status.Status = "Registering matched stars";
            status.MaxProgress = starIndexMap.Length;

            for (int i = 0; i < starIndexMap.Length; ++i) {
                var nextStarIndexMap = starIndexMap[i];
                var focuserPosition = allDetectedStars[i].FocuserPosition;
                var detectedStars = allDetectedStars[i].StarDetectionResult.StarList;

                status.Progress = i;
                progress.Report(status);

                foreach (var nextKvp in nextStarIndexMap) {
                    var sourceIndex = nextKvp.Key;
                    var globalIndex = nextKvp.Value;
                    var sourceStar = (HocusFocusDetectedStar)detectedStars[sourceIndex];
                    var matchedStar = new MatchedStar() {
                        FocuserPosition = focuserPosition,
                        Star = sourceStar,
                        ImageIndex = i
                    };
                    registeredStars[globalIndex].MatchedStars.Add(matchedStar);
                }
            }

            return (registeredStars, totalRejectionsOnBrightness);
        }

        private void UpdateTiltModels(SensorParaboloidTiltHistoryModel historyModel) {
            SensorModelResult.Update(
                sensorModel: historyModel.SensorModel, imageSize: historyModel.ImageSize, pixelSizeMicrons: historyModel.PixelSizeMicrons,
                fRatio: historyModel.FRatio, focuserStepSizeMicrons: historyModel.FocuserSizeMicrons, finalFocusPosition: historyModel.FinalFocusPosition,
                registeredStars: [],
                acceptableRSquaredMin: inspectorOptions.AcceptableRSquaredMin);
            DisplayedSensorModel = historyModel.SensorModel;
        }

        private SensorParaboloidTiltHistoryModel selectedTiltHistoryModel;

        public SensorParaboloidTiltHistoryModel SelectedTiltHistoryModel {
            get => selectedTiltHistoryModel;
            set {
                try {
                    if (value != null) {
                        UpdateTiltModels(value);
                    }
                    selectedTiltHistoryModel = value;
                    RaisePropertyChanged();
                } catch (Exception e) {
                    Notification.ShowError($"Failed to set selected tilt history model. {e.Message}");
                    Logger.Error("Failed to set selected tilt history model", e);
                }
            }
        }

        public AsyncObservableCollection<SensorParaboloidTiltHistoryModel> SensorTiltHistoryModels { get; private set; }

        public SensorModelAberrationResult SensorModelResult { get; private set; } = new SensorModelAberrationResult();

        public AsyncObservableCollection<String> RegistrationAndFitReport { get; private set; } = new();

        private SensorParaboloidModel displayedSensorModel;

        public SensorParaboloidModel DisplayedSensorModel {
            get => displayedSensorModel;
            private set {
                displayedSensorModel = value;
                RaisePropertyChanged();
            }
        }

        public void Reset() {
            this.ModelLoaded = false;
        }

        public void Clear() {
            SensorModelResult.Reset();
            SensorTiltHistoryModels.Clear();
            this.ModelLoaded = false;
        }

        #region Properties

        private bool modelLoaded = false;

        public bool ModelLoaded {
            get => modelLoaded;
            private set {
                if (modelLoaded != value) {
                    modelLoaded = value;
                    RaisePropertyChanged();
                }
            }
        }

        #endregion

        #region Private Classes

        private class DetectedStarIndex {

            public DetectedStarIndex(int index, HocusFocusDetectedStar star) {
                this.Index = index;
                this.DetectedStar = star;
            }

            public int Index { get; private set; }
            public HocusFocusDetectedStar DetectedStar { get; private set; }
        }

        private class MatchingPair {
            public int SourceIndex { get; set; }
            public int GlobalIndex { get; set; }
        }

        public class MatchedStar {
            public double FocuserPosition { get; set; }
            public HocusFocusDetectedStar Star { get; set; }
            public int ImageIndex { get; set; }

            public override string ToString() {
                return $"{{{nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(Star)}={Star}, {nameof(ImageIndex)}={ImageIndex}}}";
            }
        }

        public class RegisteredStar {
            public double RegistrationX { get; set; } = double.NaN;
            public double RegistrationY { get; set; } = double.NaN;
            public HyperbolicFittingAlglib Fitting { get; set; }
            public List<MatchedStar> MatchedStars { get; private set; } = new List<MatchedStar>();

            public override string ToString() {
                return $"{{{nameof(RegistrationX)}={RegistrationX.ToString()}, {nameof(RegistrationY)}={RegistrationY.ToString()}, {nameof(MatchedStars)}={MatchedStars}}}";
            }
        }

        #endregion
    }
}
