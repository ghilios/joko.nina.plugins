#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using KdTree;
using KdTree.Math;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Inspection {

    public class SensorDetectedStars {

        public SensorDetectedStars(double focuserPosition, HocusFocusStarDetectionResult starDetectionResult) {
            this.FocuserPosition = focuserPosition;
            this.StarDetectionResult = starDetectionResult;
        }

        public double FocuserPosition { get; private set; }
        public HocusFocusStarDetectionResult StarDetectionResult { get; private set; }

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
        private readonly IInspectorOptions inspectorOptions;
        private readonly IAutoFocusOptions autoFocusOptions;
        private readonly IAlglibAPI alglibAPI;
        private int nextHistoryId = 0;

        public SensorModel(IInspectorOptions inspectorOptions, IAutoFocusOptions autoFocusOptions, IAlglibAPI alglibAPI) {
            this.inspectorOptions = inspectorOptions;
            this.autoFocusOptions = autoFocusOptions;
            this.alglibAPI = alglibAPI;
            SensorTiltHistoryModels = new AsyncObservableCollection<SensorParaboloidTiltHistoryModel>();
        }

        public Task UpdateModel(
            List<SensorDetectedStars> allDetectedStars,
            double fRatio,
            double focuserSizeMicrons,
            double finalFocusPosition,
            int stepSize,
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
                var dataPoints = RegisterStarsAndFit(allDetectedStars, pixelSize: pixelSize, focuserSizeMicrons: focuserSizeMicrons, imageSize: imageSize, stepSize: stepSize);
                if (dataPoints.Count < 9) {
                    throw new Exception($"Need at least 9 registered stars. Found {dataPoints.Count}");
                }

                SensorParaboloidModel modelStart = null;
                if (inspectorOptions.SensorModelStartFromPrior) {
                    SensorParaboloidTiltHistoryModel lastModel = SensorTiltHistoryModels.LastOrDefault();
                    if (lastModel != null) {
                        modelStart = lastModel.SensorModel;
                    }
                }

                if (autoFocusOptions.DeveloperSettingsEnabled) {
                    Logger.Warning("DEVELOPER SETTINGS ENABLED. You will not be helped if you report issues, unless jokogeo specifically asked you to turn this on");
                }
                var sensorModelSolver = new SensorParaboloidSolver(
                    dataPoints: dataPoints,
                    sensorSizeMicronsX: imageSize.Width * pixelSize,
                    sensorSizeMicronsY: imageSize.Height * pixelSize,
                    inFocusMicrons: finalFocusPosition * focuserSizeMicrons,
                    fixedSensorCenter: inspectorOptions.FixedSensorCenter,
                    modelStart: modelStart);
                var nlSolver = new NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel>(this.alglibAPI);
                sensorModelSolver.PositiveCurvature = true;

                var positiveCurvatureSolution = nlSolver.SolveWinsorizedResiduals(
                    sensorModelSolver,
                    maxWinsorizedIterations: inspectorOptions.SensorModelMaxWinsorizationIterations,
                    winsorizationSigma: inspectorOptions.SensorModelWinsorizationSigma,
                    maxIterationsLS: inspectorOptions.SensorModelMaxSolverIterations,
                    toleranceLS: inspectorOptions.SensorModelSolverErrorTolerance,
                    ct: ct);
                ct.ThrowIfCancellationRequested();
                positiveCurvatureSolution.EvaluateFit(nlSolver, sensorModelSolver);

                sensorModelSolver.PositiveCurvature = false;
                var negativeCurvatureSolution = nlSolver.SolveWinsorizedResiduals(
                    sensorModelSolver,
                    maxWinsorizedIterations: inspectorOptions.SensorModelMaxWinsorizationIterations,
                    winsorizationSigma: inspectorOptions.SensorModelWinsorizationSigma,
                    maxIterationsLS: inspectorOptions.SensorModelMaxSolverIterations,
                    toleranceLS: inspectorOptions.SensorModelSolverErrorTolerance,
                    ct: ct);
                ct.ThrowIfCancellationRequested();
                negativeCurvatureSolution.EvaluateFit(nlSolver, sensorModelSolver);

                var solution = positiveCurvatureSolution.RMSErrorMicrons < negativeCurvatureSolution.RMSErrorMicrons ? positiveCurvatureSolution : negativeCurvatureSolution;
                Logger.Info($"Solved surface model: {solution}. RMS = {solution.RMSErrorMicrons:0.0000}, GoD: {solution.GoodnessOfFit:0.0000}, Stars: {solution.StarsInModel}");

                if (solution.GoodnessOfFit < 0.05) {
                    throw new Exception($"Sensor modeling failed. R² = {solution.GoodnessOfFit:#.00}");
                }

                DisplayedSensorModel = solution;
                SensorModelResult.Update(solution, imageSize, pixelSizeMicrons: pixelSize, fRatio: fRatio, focuserStepSizeMicrons: focuserSizeMicrons, finalFocusPosition: finalFocusPosition);

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

        private List<SensorParaboloidDataPoint> RegisterStarsAndFit(
            List<SensorDetectedStars> allDetectedStars,
            System.Drawing.Size imageSize,
            double focuserSizeMicrons,
            double pixelSize,
            int stepSize) {
            using (var stopwatch = MultiStopWatch.Measure()) {
                var allDetectedStarTrees = allDetectedStars.Select(result => {
                    var tree = new KdTree<float, DetectedStarIndex>(2, new FloatMath(), AddDuplicateBehavior.Error);
                    foreach (var (star, starIndex) in result.StarDetectionResult.StarList.Select((star, starIndex) => (star, starIndex))) {
                        tree.Add(new[] { star.Position.X, star.Position.Y }, new DetectedStarIndex(starIndex, (HocusFocusDetectedStar)star));
                    }
                    return tree;
                }).ToArray();
                stopwatch.RecordEntry("build trees");

                const float searchRadius = 30;
                var globalRegistry = new KdTree<float, DetectedStarIndex>(2, new FloatMath(), AddDuplicateBehavior.Error);
                var starIndexMap = Enumerable.Range(0, allDetectedStars.Count).Select(i => new Dictionary<int, int>()).ToArray();
                foreach (var starNode in allDetectedStarTrees[0]) {
                    var nextIndex = globalRegistry.Count;
                    globalRegistry.Add(starNode.Point, new DetectedStarIndex(nextIndex, starNode.Value.DetectedStar));
                    starIndexMap[0].Add(starNode.Value.Index, nextIndex);
                }

                for (int i = 1; i < allDetectedStars.Count; ++i) {
                    var nextStarList = allDetectedStars[i].StarDetectionResult.StarList;
                    var nextStarTree = allDetectedStarTrees[i];
                    var nextStarIndexMap = starIndexMap[i];
                    var matchedGlobalStars = new bool[globalRegistry.Count];
                    var matchedSourceStars = new bool[nextStarTree.Count];
                    var queue = new KdTree.PriorityQueue<MatchingPair, double>(new DoubleMath());
                    foreach (var (starNode, starNodeIndex) in nextStarTree.Select((starNode, starNodeIndex) => (starNode, starNodeIndex))) {
                        var sourceStar = starNode.Value.DetectedStar;
                        var sourcePoint = starNode.Point;
                        var sourceIndex = starNode.Value.Index;
                        var globalNeighbors = globalRegistry.RadialSearch(sourcePoint, searchRadius);
                        foreach (var globalNeighbor in globalNeighbors) {
                            var globalNeighborIndex = globalNeighbor.Value.Index;
                            var distance = MathUtility.DotProduct(globalNeighbor.Point, sourcePoint);
                            queue.Enqueue(new MatchingPair() { SourceIndex = sourceIndex, GlobalIndex = globalNeighborIndex }, distance);
                        }
                    }

                    while (queue.Count > 0) {
                        var nextCandidate = queue.Dequeue();
                        if (matchedGlobalStars[nextCandidate.GlobalIndex] || matchedSourceStars[nextCandidate.SourceIndex]) {
                            continue;
                        }

                        nextStarIndexMap.Add(nextCandidate.SourceIndex, nextCandidate.GlobalIndex);
                        matchedGlobalStars[nextCandidate.GlobalIndex] = true;
                        matchedSourceStars[nextCandidate.SourceIndex] = true;
                    }

                    for (int j = 0; j < matchedSourceStars.Length; ++j) {
                        if (matchedSourceStars[j]) {
                            continue;
                        }

                        // Now we've found a star that didn't match in the global registry. Add it to the registry for future matches
                        var star = nextStarList[j];
                        var nextGlobalIndex = globalRegistry.Count;
                        globalRegistry.Add(new[] { star.Position.X, star.Position.Y }, new DetectedStarIndex(nextGlobalIndex, (HocusFocusDetectedStar)star));
                        nextStarIndexMap.Add(j, nextGlobalIndex);
                    }
                }

                var registeredStars = new RegisteredStar[globalRegistry.Count];
                foreach (var globalNode in globalRegistry) {
                    var registeredStar = new RegisteredStar() {
                        RegistrationX = globalNode.Value.DetectedStar.Position.X,
                        RegistrationY = globalNode.Value.DetectedStar.Position.Y
                    };
                    registeredStars[globalNode.Value.Index] = registeredStar;
                }

                for (int i = 0; i < starIndexMap.Length; ++i) {
                    var nextStarIndexMap = starIndexMap[i];
                    var focuserPosition = allDetectedStars[i].FocuserPosition;
                    var detectedStars = allDetectedStars[i].StarDetectionResult.StarList;
                    foreach (var nextKvp in nextStarIndexMap) {
                        var sourceIndex = nextKvp.Key;
                        var globalIndex = nextKvp.Value;
                        var sourceStar = (HocusFocusDetectedStar)detectedStars[sourceIndex];
                        var matchedStar = new MatchedStar() {
                            FocuserPosition = focuserPosition,
                            Star = sourceStar
                        };
                        registeredStars[globalIndex].MatchedStars.Add(matchedStar);
                    }
                }
                stopwatch.RecordEntry("registration");

                int discardedStarCount = 0;
                var sensorModelDataPoints = new List<SensorParaboloidDataPoint>();
                foreach (var registeredStar in registeredStars) {
                    if (registeredStar.MatchedStars.Count < 5) {
                        continue;
                    }

                    try {
                        var points = registeredStar.MatchedStars.Select(s => new ScatterErrorPoint(s.FocuserPosition, s.Star.HFR, 0.0d, 0.0d)).ToList();
                        AlglibHyperbolicFitting fitting;
                        if (autoFocusOptions.UnevenHyperbolicFitEnabled) {
                            fitting = HyperbolicUnevenFittingAlglib.Create(this.alglibAPI, points, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled);
                        } else {
                            fitting = HyperbolicFittingAlglib.Create(this.alglibAPI, points, autoFocusOptions.WeightedHyperbolicFitEnabled);
                        }

                        var solveResult = fitting.Solve();
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

                        var dataPointX = (registeredStar.RegistrationX - (imageSize.Width / 2.0)) * pixelSize;
                        var dataPointY = (registeredStar.RegistrationY - (imageSize.Height / 2.0)) * pixelSize;
                        var focuserMicrons = fitting.Minimum.X * focuserSizeMicrons;
                        var dataPoint = new SensorParaboloidDataPoint(dataPointX, dataPointY, focuserMicrons, fitting.RSquared);
                        sensorModelDataPoints.Add(dataPoint);
                    } catch (Exception e) {
                        Logger.Error(e, $"Failed to calculate hyperbolic at ({registeredStar.RegistrationX}, {registeredStar.RegistrationY}). Error={e.Message}");
                    }
                }

                stopwatch.RecordEntry("fitcurves");
                if (discardedStarCount > 0) {
                    Logger.Warning($"Discarded {discardedStarCount} stars during sensor modeling due to poor fits");
                }

                if (this.inspectorOptions.InterpolationEnabled && sensorModelDataPoints.Count > 5) {
                    return ToInterpolatedGrid(sensorModelDataPoints, imageSize);
                } else {
                    return sensorModelDataPoints;
                }
            }
        }

        private void UpdateTiltModels(SensorParaboloidTiltHistoryModel historyModel) {
            SensorModelResult.Update(
                sensorModel: historyModel.SensorModel, imageSize: historyModel.ImageSize, pixelSizeMicrons: historyModel.PixelSizeMicrons,
                fRatio: historyModel.FRatio, focuserStepSizeMicrons: historyModel.FocuserSizeMicrons, finalFocusPosition: historyModel.FinalFocusPosition);
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

        private class MatchedStar {
            public double FocuserPosition { get; set; }
            public HocusFocusDetectedStar Star { get; set; }

            public override string ToString() {
                return $"{{{nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(Star)}={Star}}}";
            }
        }

        private class RegisteredStar {
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