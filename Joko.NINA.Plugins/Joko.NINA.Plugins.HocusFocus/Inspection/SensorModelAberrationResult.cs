#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Utility;
using DrawingSize = System.Drawing.Size;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Inspection {

    public class SensorModelAberrationResult : BaseINPC {
        private Angle tilt = Angle.Zero;

        public Angle Tilt {
            get => tilt;
            private set {
                if (!tilt.Equals(value)) {
                    tilt = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double tiltEffectRangeMicrons = 0.0d;

        public double TiltEffectRangeMicrons {
            get => tiltEffectRangeMicrons;
            private set {
                if (tiltEffectRangeMicrons != value) {
                    tiltEffectRangeMicrons = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double curvatureElevationMicrons = 0.0d;

        public double CurvatureElevationMicrons {
            get => curvatureElevationMicrons;
            private set {
                if (curvatureElevationMicrons != value) {
                    curvatureElevationMicrons = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double criticalFocusMicrons = 0.0d;

        public double CriticalFocusMicrons {
            get => criticalFocusMicrons;
            private set {
                if (criticalFocusMicrons != value) {
                    criticalFocusMicrons = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double centerOffsetXMicrons = 0.0d;

        public double CenterOffsetXMicrons {
            get => centerOffsetXMicrons;
            private set {
                if (centerOffsetXMicrons != value) {
                    centerOffsetXMicrons = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double centerOffsetYMicrons = 0.0d;

        public double CenterOffsetYMicrons {
            get => centerOffsetYMicrons;
            private set {
                if (centerOffsetYMicrons != value) {
                    centerOffsetYMicrons = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double fRatio = 0.0d;

        public double FRatio {
            get => fRatio;
            private set {
                if (fRatio != value) {
                    fRatio = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double curvatureRadiusMeters = 0.0d;

        public double CurvatureRadiusMicrons {
            get => curvatureRadiusMeters;
            private set {
                if (curvatureRadiusMeters != value) {
                    curvatureRadiusMeters = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double pixelSizeMicrons = 0.0d;

        public double PixelSizeMicrons {
            get => pixelSizeMicrons;
            private set {
                if (pixelSizeMicrons != value) {
                    pixelSizeMicrons = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double focuserStepSizeMicrons = 0.0d;

        public double FocuserStepSizeMicrons {
            get => focuserStepSizeMicrons;
            private set {
                if (focuserStepSizeMicrons != value) {
                    focuserStepSizeMicrons = value;
                    RaisePropertyChanged();
                }
            }
        }

        private DrawingSize imageSize = DrawingSize.Empty;

        public DrawingSize ImageSize {
            get => imageSize;
            private set {
                if (!imageSize.Equals(value)) {
                    imageSize = value;
                    RaisePropertyChanged();
                }
            }
        }

        private SensorParaboloidModel model = null;

        public SensorParaboloidModel Model {
            get => model;
            private set {
                model = value;
                RaisePropertyChanged();
            }
        }

        public AsyncObservableCollection<SensorModelAnalysisResult> AnalysisResults { get; private set; } = new AsyncObservableCollection<SensorModelAnalysisResult>();

        public AsyncObservableCollection<SensorTiltModel> SensorTiltModels { get; private set; } = new AsyncObservableCollection<SensorTiltModel>();

        private TiltPlaneModel tiltPlaneModel;

        public TiltPlaneModel TiltPlaneModel {
            get => tiltPlaneModel;
            private set {
                tiltPlaneModel = value;
                RaisePropertyChanged();
            }
        }

        private double autoFocusMeanOffset;

        public double AutoFocusMeanOffset {
            get => autoFocusMeanOffset;
            private set {
                if (autoFocusMeanOffset != value) {
                    autoFocusMeanOffset = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double sensorMeanElevation;

        public double SensorMeanElevation {
            get => sensorMeanElevation;
            private set {
                if (sensorMeanElevation != value) {
                    sensorMeanElevation = value;
                    RaisePropertyChanged();
                }
            }
        }

        public void Update(SensorParaboloidModel sensorModel, DrawingSize imageSize, double pixelSizeMicrons, double fRatio, double focuserStepSizeMicrons, double finalFocusPosition) {
            ImageSize = imageSize;
            FRatio = fRatio;
            PixelSizeMicrons = pixelSizeMicrons;
            FocuserStepSizeMicrons = focuserStepSizeMicrons;
            CriticalFocusMicrons = 2.44 * fRatio * fRatio * 0.55;
            CenterOffsetXMicrons = sensorModel.X0;
            CenterOffsetYMicrons = sensorModel.Y0;
            CurvatureRadiusMicrons = 1.0 / Math.Sqrt(Math.Abs(sensorModel.C));

            var sensorWidthMicrons = imageSize.Width * pixelSizeMicrons;
            var sensorHeightMicrons = imageSize.Height * pixelSizeMicrons;
            CurvatureElevationMicrons = sensorModel.CurvatureAt(sensorWidthMicrons / 2, sensorHeightMicrons / 2);
            var tiltCorners = new double[] {
                sensorModel.TiltAt(-sensorWidthMicrons / 2.0, -sensorWidthMicrons / 2.0),
                sensorModel.TiltAt(sensorWidthMicrons / 2.0, -sensorWidthMicrons / 2.0),
                sensorModel.TiltAt(-sensorWidthMicrons / 2.0, sensorWidthMicrons / 2.0),
                sensorModel.TiltAt(sensorWidthMicrons / 2.0, sensorWidthMicrons / 2.0)
            };
            var sensorVolume = sensorModel.Volume(widthMicrons: sensorWidthMicrons, heightMicrons: sensorHeightMicrons);
            var sensorArea = sensorHeightMicrons * sensorWidthMicrons;
            SensorMeanElevation = sensorVolume / sensorArea;
            var sensorMeanFocuserPosition = SensorMeanElevation / focuserStepSizeMicrons;
            AutoFocusMeanOffset = sensorMeanFocuserPosition - finalFocusPosition;

            TiltEffectRangeMicrons = tiltCorners.Max() - tiltCorners.Min();
            Tilt = Angle.ByRadians(sensorModel.Theta);
            var tiltPlaneModel = CreateTiltPlaneModel(imageSize, focuserStepSizeMicrons, sensorModel);
            UpdateTiltModels(tiltPlaneModel);
            TiltPlaneModel = tiltPlaneModel;

            AnalysisResults.Clear();
            AnalyzeCentering(sensorModel, pixelSizeMicrons);
            AnalyzeCurvature(sensorModel, curvatureElevationMicrons, criticalFocusMicrons);
            AnalyzeTilt(TiltEffectRangeMicrons, Tilt, criticalFocusMicrons);

            Model = sensorModel;
        }

        private void AnalyzeTilt(double tiltEffectRangeMicrons, Angle tilt, double criticalFocus) {
            var result = new SensorModelAnalysisResult() {
                Name = "Tilt",
                Value = $"{tilt.Degree:0.00}°",
                Acceptable = Math.Abs(tiltEffectRangeMicrons) < (0.25 * criticalFocus),
                Details = $"Overall maximum tilt effect is within 0.25x the critical focus of {criticalFocus:0.0} microns"
            };
            if (!result.Acceptable) {
                result.Details = $"Overall maximum tilt effect of {tiltEffectRangeMicrons:0.0} microns is too large. Adjust tilt based on the sensor tilt diagram";
            }
            AnalysisResults.Add(result);
        }

        private void AnalyzeCurvature(SensorParaboloidModel sensorModel, double curvatureElevationMicrons, double criticalFocus) {
            var result = new SensorModelAnalysisResult() {
                Name = "Curvature",
                Value = $"{curvatureElevationMicrons:0.} microns",
                Acceptable = Math.Abs(curvatureElevationMicrons) < (1.5 * criticalFocus),
                Details = $"Curvature elevation is within 1.5x the critical focus of {criticalFocus:0.0} microns"
            };
            if (!result.Acceptable) {
                var direction = curvatureElevationMicrons > 0 ? "REDUCING" : "INCREASING";
                result.Details = $"Large curvature detected, from center to the corners. Try {direction} backfocus";
            }
            AnalysisResults.Add(result);
        }

        private void AnalyzeCentering(SensorParaboloidModel sensorModel, double pixelSize) {
            var centeringXResult = new SensorModelAnalysisResult() {
                Name = "Left/Right Centered",
                Value = $"{sensorModel.X0:0.} microns",
                Acceptable = Math.Abs(sensorModel.X0) < pixelSize,
                Details = $"Offset is less than the size of a pixel, {pixelSize:0.00} microns"
            };
            var centeringYResult = new SensorModelAnalysisResult() {
                Name = "Up/Down Centered",
                Value = $"{sensorModel.Y0:0.} microns",
                Acceptable = Math.Abs(sensorModel.Y0) < pixelSize,
                Details = $"Offset is less than the size of a pixel, {pixelSize:0.00} microns"
            };

            if (!centeringXResult.Acceptable) {
                centeringXResult.Details = $"Offset is greater than the size of a pixel, {pixelSize:0.00} microns. Move {(sensorModel.X0 > 0 ? "LEFT" : "RIGHT")}";
            }
            if (!centeringYResult.Acceptable) {
                centeringYResult.Details = $"Offset is greater than the size of a pixel, {pixelSize:0.00} microns. Move {(sensorModel.Y0 > 0 ? "UP" : "DOWN")}";
            }

            AnalysisResults.Add(centeringXResult);
            AnalysisResults.Add(centeringYResult);
        }

        public void Reset() {
            this.SensorTiltModels.Clear();
            this.TiltPlaneModel = null;
        }

        private void UpdateTiltModels(TiltPlaneModel tiltPlaneModel) {
            SensorTiltModels.Clear();
            SensorTiltModels.Add(tiltPlaneModel.Center);
            SensorTiltModels.Add(tiltPlaneModel.TopLeft);
            SensorTiltModels.Add(tiltPlaneModel.TopRight);
            SensorTiltModels.Add(tiltPlaneModel.BottomLeft);
            SensorTiltModels.Add(tiltPlaneModel.BottomRight);
        }

        private TiltPlaneModel CreateTiltPlaneModel(
            DrawingSize imageSize,
            double focuserStepSizeMicrons,
            SensorParaboloidModel sensorModel) {
            var width = (double)imageSize.Width;
            var height = (double)imageSize.Height;
            var centerFocuser = sensorModel.ValueAt(x: 0.0, y: 0.0) / focuserStepSizeMicrons;
            var topLeftFocuser = sensorModel.ValueAt(x: -1.0 / 2.0 * width, y: -1.0 / 2.0 * height) / focuserStepSizeMicrons;
            var topRightFocuser = sensorModel.ValueAt(x: 1.0 / 2.0 * width, y: -1.0 / 2.0 * height) / focuserStepSizeMicrons;
            var bottomLeftFocuser = sensorModel.ValueAt(x: -1.0 / 2.0 * width, y: 1.0 / 2.0 * height) / focuserStepSizeMicrons;
            var bottomRightFocuser = sensorModel.ValueAt(x: 1.0 / 2.0 * width, y: 1.0 / 2.0 * height) / focuserStepSizeMicrons;

            return TiltPlaneModel.Create(imageSize: imageSize, focuserStepSizeMicrons: focuserStepSizeMicrons, centerFocuser: centerFocuser,
                topLeftFocuser: topLeftFocuser, topRightFocuser: topRightFocuser, bottomLeftFocuser: bottomLeftFocuser, bottomRightFocuser: bottomRightFocuser);
        }
    }
}