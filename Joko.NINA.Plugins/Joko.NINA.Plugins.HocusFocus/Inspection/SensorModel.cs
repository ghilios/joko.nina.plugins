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
        private int nextHistoryId = 0;

        public SensorModel(IInspectorOptions inspectorOptions) {
            this.inspectorOptions = inspectorOptions;
            SensorTiltHistoryModels = new AsyncObservableCollection<SensorParaboloidTiltHistoryModel>();
        }

        public Task UpdateModel(
            List<SensorDetectedStars> allDetectedStars,
            double fRatio,
            double focuserSizeMicrons,
            double finalFocusPosition,
            CancellationToken ct) {
            if (allDetectedStars.Count == 0) {
                throw new ArgumentException("Cannot update sensor model. No detected stars provided");
            }

            if (double.IsNaN(fRatio)) {
                Notification.ShowWarning("FRatio not set in Equipment Options");
            }

            return Task.Run(() => {
                ModelLoaded = false;
                var firstStarDetectionResult = allDetectedStars.First().StarDetectionResult;
                var imageSize = firstStarDetectionResult.ImageSize;
                var pixelSize = firstStarDetectionResult.PixelSize;
                Logger.Info($"Building Sensor Model. FRatio ({fRatio}), Focuser Size ({focuserSizeMicrons}), Pixel Size ({pixelSize}), Image size ({imageSize})");
                var dataPoints = RegisterStarsAndFit(allDetectedStars, pixelSize: pixelSize, focuserSizeMicrons: focuserSizeMicrons, imageSize: imageSize);
                var sensorModelSolver = new SensorParaboloidSolver(
                    dataPoints: dataPoints,
                    sensorSizeMicronsX: imageSize.Width * pixelSize,
                    sensorSizeMicronsY: imageSize.Height * pixelSize,
                    inFocusMicrons: finalFocusPosition * focuserSizeMicrons);
                var nlSolver = new NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel>();
                sensorModelSolver.PositiveCurvature = true;
                var positiveCurvatureSolution = nlSolver.SolveWinsorizedResiduals(sensorModelSolver, ct: ct);
                ct.ThrowIfCancellationRequested();
                positiveCurvatureSolution.EvaluateFit(nlSolver, sensorModelSolver);

                sensorModelSolver.PositiveCurvature = false;
                var negativeCurvatureSolution = nlSolver.SolveWinsorizedResiduals(sensorModelSolver, ct: ct);
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

        private List<SensorParaboloidDataPoint> RegisterStarsAndFit(
            List<SensorDetectedStars> allDetectedStars,
            System.Drawing.Size imageSize,
            double focuserSizeMicrons,
            double pixelSize) {
            using (var stopwatch = MultiStopWatch.Measure()) {
                var starRegistry = new StarRegistry();
                foreach (var sensorDetectedStars in allDetectedStars) {
                    starRegistry.AddStarField(sensorDetectedStars.FocuserPosition, sensorDetectedStars.StarDetectionResult);
                }

                int discardedStarCount = 0;
                var sensorModelDataPoints = new List<SensorParaboloidDataPoint>();
                foreach (var registeredStar in starRegistry) {
                    if (registeredStar.MatchedStars.Count < 5) {
                        continue;
                    }

                    try {
                        var points = registeredStar.MatchedStars.Select(s => new ScatterErrorPoint(s.StarField.FocuserPosition, s.Star.HFR, 0.0d, 0.0d)).ToList();
                        // TODO: Figure out if I need to allow rotations here. This is probably an inspection option?
                        var fitting = HyperbolicFittingAlglib.Create(points, false);
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

                if (sensorModelDataPoints.Count < 5) {
                    throw new Exception($"Not enough registered points ({sensorModelDataPoints.Count}) across the field of view to build a model");
                }

                stopwatch.RecordEntry("fitcurves");
                if (discardedStarCount > 0) {
                    Logger.Warning($"Discarded {discardedStarCount} stars during sensor modeling due to poor fits");
                }
                return sensorModelDataPoints;
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