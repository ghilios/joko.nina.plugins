#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Statistics.Models.Regression.Linear;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using NINA.Joko.Plugins.HocusFocus.Converters;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class TiltPlaneModel {

        public TiltPlaneModel(AutoFocusResult autoFocusResult, double a, double b, double c, double mean) {
            if (autoFocusResult.ImageSize.Width == 0 || autoFocusResult.ImageSize.Height <= 0) {
                throw new ArgumentException($"ImageSize ({autoFocusResult.ImageSize.Width}, {autoFocusResult.ImageSize.Height}) dimensions must be positive");
            }

            AutoFocusResult = autoFocusResult;
            A = a;
            B = b;
            C = c;
            MeanFocuserPosition = mean;
        }

        public AutoFocusResult AutoFocusResult { get; private set; }
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
            if (x < 0 || x >= AutoFocusResult.ImageSize.Width) {
                throw new ArgumentException($"X ({x}) must be within the image size dimensions ({AutoFocusResult.ImageSize.Width}x{AutoFocusResult.ImageSize.Height})");
            }

            return (double)x / AutoFocusResult.ImageSize.Width - 0.5;
        }

        public double GetModelY(int y) {
            if (y < 0 || y >= AutoFocusResult.ImageSize.Height) {
                throw new ArgumentException($"Y ({y}) must be within the image size dimensions ({AutoFocusResult.ImageSize.Width}x{AutoFocusResult.ImageSize.Height})");
            }

            return (double)y / AutoFocusResult.ImageSize.Height - 0.5;
        }

        public static TiltPlaneModel Create(AutoFocusResult result) {
            var ols = new OrdinaryLeastSquares() {
                UseIntercept = true
            };

            double[][] inputs =
            {
                new double[] { -0.5, -0.5 },
                new double[] { 0.5, -0.5 },
                new double[] { -0.5, 0.5 },
                new double[] { 0.5, 0.5 },
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
            return new TiltPlaneModel(result, a, b, c, mean);
        }
    }

    public class TiltModel : BaseINPC {
        private readonly IInspectorOptions inspectorOptions;
        private int nextHistoryId = 0;

        public TiltModel(IInspectorOptions inspectorOptions) {
            this.inspectorOptions = inspectorOptions;
            SensorTiltModels = new AsyncObservableCollection<SensorTiltModel>();
            SensorTiltHistoryModels = new AsyncObservableCollection<SensorTiltHistoryModel>();
        }

        private void UpdateTiltMeasurementsTable(AutoFocusResult result, TiltPlaneModel tiltModel) {
            var centerFocuser = result.RegionResults[1].EstimatedFinalFocuserPosition;
            var topLeftFocuser = result.RegionResults[2].EstimatedFinalFocuserPosition;
            var topRightFocuser = result.RegionResults[3].EstimatedFinalFocuserPosition;
            var bottomLeftFocuser = result.RegionResults[4].EstimatedFinalFocuserPosition;
            var bottomRightFocuser = result.RegionResults[5].EstimatedFinalFocuserPosition;
            var micronsPerFocuserStep = inspectorOptions.MicronsPerFocuserStep > 0 ? inspectorOptions.MicronsPerFocuserStep : double.NaN;

            var newSideToTiltModels = new List<SensorTiltModel>();
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.Center) {
                FocuserPosition = centerFocuser,
                AdjustmentRequiredSteps = double.NaN,
                AdjustmentRequiredMicrons = double.NaN,
                RSquared = result.RegionResults[1].Fittings.GetRSquared()
            });
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.TopLeft) {
                FocuserPosition = topLeftFocuser,
                AdjustmentRequiredSteps = tiltModel.MeanFocuserPosition - topLeftFocuser,
                AdjustmentRequiredMicrons = (tiltModel.MeanFocuserPosition - topLeftFocuser) * micronsPerFocuserStep,
                RSquared = result.RegionResults[2].Fittings.GetRSquared()
            });
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.TopRight) {
                FocuserPosition = topRightFocuser,
                AdjustmentRequiredSteps = tiltModel.MeanFocuserPosition - topRightFocuser,
                AdjustmentRequiredMicrons = (tiltModel.MeanFocuserPosition - topRightFocuser) * micronsPerFocuserStep,
                RSquared = result.RegionResults[3].Fittings.GetRSquared()
            });
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.BottomLeft) {
                FocuserPosition = bottomLeftFocuser,
                AdjustmentRequiredSteps = tiltModel.MeanFocuserPosition - bottomLeftFocuser,
                AdjustmentRequiredMicrons = (tiltModel.MeanFocuserPosition - bottomLeftFocuser) * micronsPerFocuserStep,
                RSquared = result.RegionResults[4].Fittings.GetRSquared()
            });
            newSideToTiltModels.Add(new SensorTiltModel(SensorSide.BottomRight) {
                FocuserPosition = bottomRightFocuser,
                AdjustmentRequiredSteps = tiltModel.MeanFocuserPosition - bottomRightFocuser,
                AdjustmentRequiredMicrons = (tiltModel.MeanFocuserPosition - bottomRightFocuser) * micronsPerFocuserStep,
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

        public void UpdateTiltModel(AutoFocusResult result) {
            var tiltModel = TiltPlaneModel.Create(result);
            TiltPlaneModel = tiltModel;
            UpdateTiltMeasurementsTable(result, tiltModel);
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

        private double adjustmentRequiredSteps;

        public double AdjustmentRequiredSteps {
            get => adjustmentRequiredSteps;
            set {
                adjustmentRequiredSteps = value;
                RaisePropertyChanged();
            }
        }

        private double adjustmentRequiredMicrons;

        public double AdjustmentRequiredMicrons {
            get => adjustmentRequiredMicrons;
            set {
                adjustmentRequiredMicrons = value;
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
            return $"{{{nameof(SensorSide)}={SensorSide.ToString()}, {nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(AdjustmentRequiredSteps)}={AdjustmentRequiredSteps.ToString()}, {nameof(RSquared)}={RSquared.ToString()}}}";
        }
    }
}