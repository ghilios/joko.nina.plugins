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

    public class SensorModel : BaseINPC {
        private readonly IInspectorOptions inspectorOptions;

        public SensorModel(IInspectorOptions inspectorOptions) {
            this.inspectorOptions = inspectorOptions;
            SensorTiltModels = new AsyncObservableCollection<SensorTiltModel>();
            SensorTiltHistoryModels = new AsyncObservableCollection<SensorTiltHistoryModel>();
        }

        public void UpdateModel(
            List<SensorDetectedStars> allDetectedStars,
            System.Drawing.Size imageSize,
            double finalFocusPosition) {
            var dataPoints = RegisterStarsAndFit(allDetectedStars);
            var sensorModelSolver = new SensorParaboloidSolver(dataPoints: dataPoints, sensorSizeX: imageSize.Width, sensorSizeY: imageSize.Height, inFocusPosition: finalFocusPosition);
            var nlSolver = new NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel>();
            var solution = nlSolver.SolveWinsorizedResiduals(sensorModelSolver);
            var goodnessOfFit = nlSolver.GoodnessOfFit(sensorModelSolver, solution);
            var rmsError = nlSolver.RMSError(sensorModelSolver, solution);
            Console.WriteLine($"Solved fit: {solution}. RMS = {rmsError:0.0000}, GoF = {goodnessOfFit:0.0000}");
        }

        private static float DotProduct(float[] x, float[] y) {
            if (x.Length != y.Length) {
                throw new ArgumentException($"x length ({x.Length}) must be equal to y length ({y.Length})");
            }
            float ssd = 0.0f;
            for (int i = 0; i < x.Length; ++i) {
                var diff = y[i] - x[i];
                ssd += diff * diff;
            }
            return ssd;
        }

        private List<SensorParaboloidDataPoint> RegisterStarsAndFit(List<SensorDetectedStars> allDetectedStars) {
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
                    var queue = new PriorityQueue<MatchingPair, double>(new DoubleMath());
                    foreach (var (starNode, starNodeIndex) in nextStarTree.Select((starNode, starNodeIndex) => (starNode, starNodeIndex))) {
                        var sourceStar = starNode.Value.DetectedStar;
                        var sourcePoint = starNode.Point;
                        var sourceIndex = starNode.Value.Index;
                        var globalNeighbors = globalRegistry.RadialSearch(sourcePoint, searchRadius);
                        foreach (var globalNeighbor in globalNeighbors) {
                            var globalNeighborIndex = globalNeighbor.Value.Index;
                            var distance = DotProduct(globalNeighbor.Point, sourcePoint);
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

                var sensorModelDataPoints = new List<SensorParaboloidDataPoint>();
                foreach (var registeredStar in registeredStars) {
                    if (registeredStar.MatchedStars.Count < 5) {
                        continue;
                    }

                    try {
                        var points = registeredStar.MatchedStars.Select(s => new ScatterErrorPoint(s.FocuserPosition, s.Star.HFR, 0.0d, 0.0d)).ToList();
                        var fitting = HyperbolicFittingAlglib.Create(points);
                        var solveResult = fitting.Solve();
                        if (!solveResult) {
                            Logger.Trace($"Failed to fit hyperbolic curve to star matches at ({registeredStar.RegistrationX:0.00}, {registeredStar.RegistrationY:0.00})");
                            continue;
                        }

                        var dataPoint = new SensorParaboloidDataPoint(registeredStar.RegistrationX, registeredStar.RegistrationY, fitting.Minimum.X);
                        sensorModelDataPoints.Add(dataPoint);
                    } catch (Exception e) {
                        Logger.Error(e, $"Failed to calculate hyperbolic at ({registeredStar.RegistrationX}, {registeredStar.RegistrationY}). Error={e.Message}");
                    }
                }
                stopwatch.RecordEntry("fitcurves");
                return sensorModelDataPoints;
            }
        }

        private SensorTiltHistoryModel selectedTiltHistoryModel;

        public SensorTiltHistoryModel SelectedTiltHistoryModel {
            get => selectedTiltHistoryModel;
            set {
                try {
                    if (value != null) {
                        // SetTiltPlaneModel(value.AutoFocusResult, value.TiltPlaneModel);
                    }
                    selectedTiltHistoryModel = value;
                    RaisePropertyChanged();
                } catch (Exception e) {
                    Notification.ShowError($"Failed to set tilt model. {e.Message}");
                    Logger.Error("Failed to set tilt model", e);
                }
            }
        }

        public AsyncObservableCollection<SensorTiltModel> SensorTiltModels { get; private set; }

        public AsyncObservableCollection<SensorTiltHistoryModel> SensorTiltHistoryModels { get; private set; }

        private TiltPlaneModel tiltPlaneModel;

        public TiltPlaneModel TiltPlaneModel {
            get => tiltPlaneModel;
            private set {
                tiltPlaneModel = value;
                RaisePropertyChanged();
            }
        }

        public void Reset() {
            this.SensorTiltModels.Clear();
            this.TiltPlaneModel = null;
        }

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