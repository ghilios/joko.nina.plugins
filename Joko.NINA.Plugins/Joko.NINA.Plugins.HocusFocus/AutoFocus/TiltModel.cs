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
using System.Linq;
using System.ComponentModel;
using NINA.Joko.Plugins.HocusFocus.Converters;
using System.Threading;
using NINA.Core.Utility.Notification;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class TiltPlaneModel {

        public TiltPlaneModel(
            System.Drawing.Size imageSize,
            double fRatio,
            double a,
            double b,
            double c,
            double mean,
            double focuserStepSizeMicrons,
            double centerPosition,
            double topLeftPosition,
            double topRightPosition,
            double bottomLeftPosition,
            double bottomRightPosition) {
            if (imageSize.Width == 0 || imageSize.Height <= 0) {
                throw new ArgumentException($"ImageSize ({imageSize.Width}, {imageSize.Height}) dimensions must be positive");
            }

            ImageSize = imageSize;
            A = a;
            B = b;
            C = c;
            FocuserStepSizeMicrons = focuserStepSizeMicrons;
            MeanFocuserPosition = mean;
            Center = new SensorTiltModel(SensorSide.Center) {
                FocuserPosition = centerPosition,
                AdjustmentRequiredSteps = double.NaN,
                AdjustmentRequiredMicrons = double.NaN
            };
            TopLeft = new SensorTiltModel(SensorSide.TopLeft) {
                FocuserPosition = topLeftPosition,
                AdjustmentRequiredSteps = topLeftPosition - MeanFocuserPosition,
                AdjustmentRequiredMicrons = (topLeftPosition - MeanFocuserPosition) * FocuserStepSizeMicrons
            };
            TopRight = new SensorTiltModel(SensorSide.TopRight) {
                FocuserPosition = topRightPosition,
                AdjustmentRequiredSteps = topRightPosition - MeanFocuserPosition,
                AdjustmentRequiredMicrons = (topRightPosition - MeanFocuserPosition) * FocuserStepSizeMicrons
            };
            BottomLeft = new SensorTiltModel(SensorSide.BottomLeft) {
                FocuserPosition = bottomLeftPosition,
                AdjustmentRequiredSteps = bottomLeftPosition - MeanFocuserPosition,
                AdjustmentRequiredMicrons = (bottomLeftPosition - MeanFocuserPosition) * FocuserStepSizeMicrons
            };
            BottomRight = new SensorTiltModel(SensorSide.BottomRight) {
                FocuserPosition = bottomRightPosition,
                AdjustmentRequiredSteps = bottomRightPosition - MeanFocuserPosition,
                AdjustmentRequiredMicrons = (bottomRightPosition - MeanFocuserPosition) * FocuserStepSizeMicrons
            };
            FRatio = double.IsNaN(fRatio) ? 5.0 : fRatio;
        }

        public System.Drawing.Size ImageSize { get; private set; }
        public double A { get; private set; }
        public double B { get; private set; }
        public double C { get; private set; }
        public double FocuserStepSizeMicrons { get; private set; }
        public SensorTiltModel Center { get; private set; }
        public SensorTiltModel TopLeft { get; private set; }
        public SensorTiltModel TopRight { get; private set; }
        public SensorTiltModel BottomLeft { get; private set; }
        public SensorTiltModel BottomRight { get; private set; }
        public double MeanFocuserPosition { get; private set; }
        public double FRatio { get; private set; }

        public double EstimateFocusPosition(int x, int y) {
            var xRatio = GetModelX(x);
            var yRatio = GetModelY(y);
            return A * xRatio + B * yRatio + C;
        }

        public double GetModelX(int x) {
            if (x < 0 || x >= ImageSize.Width) {
                throw new ArgumentException($"X ({x}) must be within the image size dimensions ({ImageSize.Width}x{ImageSize.Height})");
            }

            return (double)x / ImageSize.Width - 0.5;
        }

        public double GetModelY(int y) {
            if (y < 0 || y >= ImageSize.Height) {
                throw new ArgumentException($"Y ({y}) must be within the image size dimensions ({ImageSize.Width}x{ImageSize.Height})");
            }

            return (double)y / ImageSize.Height - 0.5;
        }

        public static TiltPlaneModel Create(AutoFocusResult result, double fRatio, double focuserStepSizeMicrons) {
            var centerFocuser = result.RegionResults[1].EstimatedFinalFocuserPosition;
            var topLeftFocuser = result.RegionResults[2].EstimatedFinalFocuserPosition;
            var topRightFocuser = result.RegionResults[3].EstimatedFinalFocuserPosition;
            var bottomLeftFocuser = result.RegionResults[4].EstimatedFinalFocuserPosition;
            var bottomRightFocuser = result.RegionResults[5].EstimatedFinalFocuserPosition;
            var tiltPlaneModel = Create(
                imageSize: result.ImageSize, fRatio: fRatio,
                focuserStepSizeMicrons: focuserStepSizeMicrons, centerFocuser: centerFocuser, topLeftFocuser: topLeftFocuser,
                topRightFocuser: topRightFocuser, bottomLeftFocuser: bottomLeftFocuser, bottomRightFocuser: bottomRightFocuser);

            tiltPlaneModel.Center.RSquared = result.RegionResults[1].Fittings.GetRSquared();
            tiltPlaneModel.TopLeft.RSquared = result.RegionResults[2].Fittings.GetRSquared();
            tiltPlaneModel.TopRight.RSquared = result.RegionResults[3].Fittings.GetRSquared();
            tiltPlaneModel.BottomLeft.RSquared = result.RegionResults[4].Fittings.GetRSquared();
            tiltPlaneModel.BottomRight.RSquared = result.RegionResults[5].Fittings.GetRSquared();
            return tiltPlaneModel;
        }

        public static TiltPlaneModel Create(
            System.Drawing.Size imageSize,
            double fRatio,
            double focuserStepSizeMicrons,
            double centerFocuser,
            double topLeftFocuser,
            double topRightFocuser,
            double bottomLeftFocuser,
            double bottomRightFocuser) {
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
            double[] outputs = { topLeftFocuser, topRightFocuser, bottomLeftFocuser, bottomRightFocuser };

            MultipleLinearRegression regression = ols.Learn(inputs, outputs);

            double a = regression.Weights[0];
            double b = regression.Weights[1];
            double c = regression.Intercept;
            double mean = outputs.Average();
            return new TiltPlaneModel(
                imageSize: imageSize, fRatio: fRatio,
                a: a, b: b, c: c, mean: mean, focuserStepSizeMicrons: focuserStepSizeMicrons, centerPosition: centerFocuser,
                topLeftPosition: topLeftFocuser, topRightPosition: topRightFocuser,
                bottomLeftPosition: bottomLeftFocuser, bottomRightPosition: bottomRightFocuser);
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

        private void UpdateTiltModels(SensorTiltHistoryModel historyModel) {
            UpdateTiltModels(historyModel.TiltPlaneModel);
        }

        private void UpdateTiltModels(TiltPlaneModel tiltPlaneModel) {
            SensorTiltModels.Clear();
            SensorTiltModels.Add(tiltPlaneModel.Center);
            SensorTiltModels.Add(tiltPlaneModel.TopLeft);
            SensorTiltModels.Add(tiltPlaneModel.TopRight);
            SensorTiltModels.Add(tiltPlaneModel.BottomLeft);
            SensorTiltModels.Add(tiltPlaneModel.BottomRight);
            TiltPlaneModel = tiltPlaneModel;
        }

        private void UpdateTiltMeasurementsTable(TiltPlaneModel tiltModel, double backfocusFocuserPositionDelta) {
            UpdateTiltModels(tiltModel);
            var historyId = Interlocked.Increment(ref nextHistoryId);
            SensorTiltHistoryModels.Insert(0, new SensorTiltHistoryModel(
                historyId: historyId,
                tiltPlaneModel: tiltPlaneModel,
                backfocusFocuserPositionDelta: backfocusFocuserPositionDelta));
            SelectedTiltHistoryModel = null;
        }

        public void UpdateTiltModel(AutoFocusResult result, double fRatio, double backfocusFocuserPositionDelta) {
            var micronsPerFocuserStep = inspectorOptions.MicronsPerFocuserStep > 0 ? inspectorOptions.MicronsPerFocuserStep : double.NaN;
            var tiltModel = TiltPlaneModel.Create(result, fRatio: fRatio, focuserStepSizeMicrons: micronsPerFocuserStep);
            UpdateTiltMeasurementsTable(tiltModel, backfocusFocuserPositionDelta);
        }

        private SensorTiltHistoryModel selectedTiltHistoryModel;

        public SensorTiltHistoryModel SelectedTiltHistoryModel {
            get => selectedTiltHistoryModel;
            set {
                try {
                    if (value != null) {
                        UpdateTiltModels(value);
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
            TiltPlaneModel tiltPlaneModel,
            double backfocusFocuserPositionDelta) {
            HistoryId = historyId;
            TiltPlaneModel = tiltPlaneModel;
            BackfocusFocuserPositionDelta = backfocusFocuserPositionDelta;
        }

        public int HistoryId { get; private set; }
        public TiltPlaneModel TiltPlaneModel { get; private set; }
        public double BackfocusFocuserPositionDelta { get; private set; }
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