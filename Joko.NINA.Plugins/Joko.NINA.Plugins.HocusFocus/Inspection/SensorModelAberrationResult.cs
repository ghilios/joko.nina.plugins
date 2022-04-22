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

        private double tiltEffectMicrons = 0.0d;

        public double TiltEffectMicrons {
            get => tiltEffectMicrons;
            private set {
                if (tiltEffectMicrons != value) {
                    tiltEffectMicrons = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double curvatureEffectMicrons = 0.0d;

        public double CurvatureEffectMicrons {
            get => curvatureEffectMicrons;
            private set {
                if (curvatureEffectMicrons != value) {
                    curvatureEffectMicrons = value;
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

        private double curvatureRadiusMillimeters = 0.0d;

        public double CurvatureRadiusMillimeters {
            get => curvatureRadiusMillimeters;
            private set {
                if (curvatureRadiusMillimeters != value) {
                    curvatureRadiusMillimeters = value;
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

        private double sensorMeanPosition;

        public double SensorMeanPosition {
            get => sensorMeanPosition;
            private set {
                if (sensorMeanPosition != value) {
                    sensorMeanPosition = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double autoFocusPosition;

        public double AutoFocusPosition {
            get => autoFocusPosition;
            private set {
                if (autoFocusPosition != value) {
                    autoFocusPosition = value;
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
            CurvatureRadiusMillimeters = 1.0 / (2.0 * sensorModel.C * sensorModel.C * 1000.0);

            var sensorWidthMicrons = imageSize.Width * pixelSizeMicrons;
            var sensorHeightMicrons = imageSize.Height * pixelSizeMicrons;
            CurvatureEffectMicrons = sensorModel.CurvatureAt(sensorWidthMicrons / 2, sensorHeightMicrons / 2);
            var tiltCorners = new double[] {
                sensorModel.TiltAt(-sensorWidthMicrons / 2.0, -sensorWidthMicrons / 2.0),
                sensorModel.TiltAt(sensorWidthMicrons / 2.0, -sensorWidthMicrons / 2.0),
                sensorModel.TiltAt(-sensorWidthMicrons / 2.0, sensorWidthMicrons / 2.0),
                sensorModel.TiltAt(sensorWidthMicrons / 2.0, sensorWidthMicrons / 2.0)
            };
            var sensorVolume = sensorModel.Volume(widthMicrons: sensorWidthMicrons, heightMicrons: sensorHeightMicrons);
            var sensorArea = sensorHeightMicrons * sensorWidthMicrons;
            SensorMeanElevation = sensorVolume / sensorArea;
            SensorMeanPosition = SensorMeanElevation / focuserStepSizeMicrons;
            AutoFocusPosition = finalFocusPosition;
            AutoFocusMeanOffset = SensorMeanPosition - finalFocusPosition;

            TiltEffectMicrons = (tiltCorners.Max() - tiltCorners.Min()) / 2.0;
            Tilt = Angle.ByRadians(sensorModel.Theta);
            var tiltPlaneModel = CreateTiltPlaneModel(imageSize, focuserStepSizeMicrons, sensorModel);
            UpdateTiltModels(tiltPlaneModel);
            TiltPlaneModel = tiltPlaneModel;

            AnalysisResults.Clear();
            AnalyzeCurvature(CurvatureRadiusMillimeters, CurvatureEffectMicrons, criticalFocusMicrons);
            AnalyzeTilt(TiltEffectMicrons, Tilt, criticalFocusMicrons);
            AnalyzeCentering(sensorModel, pixelSizeMicrons);

            Model = sensorModel;
        }

        private void AnalyzeTilt(double tiltEffectMicrons, Angle tilt, double criticalFocus) {
            var result = new SensorModelAnalysisResult() {
                Name = "Tilt",
                Value = $"{tilt.Degree:0.000}°",
                Acceptable = Math.Abs(tiltEffectMicrons) < (0.25 * criticalFocus),
                Details = $"Tilt effect {tiltEffectMicrons:0.0} microns is within 0.25x critical focus"
            };
            if (!result.Acceptable) {
                result.Details = $"Tilt effect of {tiltEffectMicrons:0.0} microns is > 0.25x critical focus. Adjust tilt based on the sensor tilt diagram";
            }
            AnalysisResults.Add(result);
        }

        private void AnalyzeCurvature(double curvatureRadius, double curvatureElevationMicrons, double criticalFocus) {
            var result = new SensorModelAnalysisResult() {
                Name = "Curvature",
                Value = $"{curvatureRadius:0.} mm",
                Acceptable = Math.Abs(curvatureElevationMicrons) < (1.5 * criticalFocus),
                Details = $"Curvature effect of {curvatureElevationMicrons:0.} microns is within 1.5x critical focus"
            };
            if (!result.Acceptable) {
                var direction = curvatureElevationMicrons > 0 ? "REDUCING" : "INCREASING";
                result.Details = $"Curvature effect of {curvatureElevationMicrons:0.} microns is > 1.5x critical focus. Try {direction} backfocus";
            }
            AnalysisResults.Add(result);
        }

        private void AnalyzeCentering(SensorParaboloidModel sensorModel, double pixelSize) {
            var centeringXResult = new SensorModelAnalysisResult() {
                Name = "Left/Right",
                Value = $"{sensorModel.X0 / 1000.0:0.0} mm",
                Acceptable = true,
                Details = $"Offset is less than 10 pixels"
            };
            var centeringYResult = new SensorModelAnalysisResult() {
                Name = "Up/Down",
                Value = $"{sensorModel.Y0 / 1000.0:0.0} mm",
                Acceptable = true,
                Details = $"Offset is less than 10 pixels"
            };

            const string offsetDetected = "Large offset detected. This can be due to flexure, tilt, or other reasons";
            if (Math.Abs(sensorModel.X0) >= 10.0 * pixelSize) {
                centeringXResult.Details = offsetDetected;
            }
            if (Math.Abs(sensorModel.Y0) >= 10.0 * pixelSize) {
                centeringYResult.Details = offsetDetected;
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
            var centerFocuser = sensorModel.ValueAt(x: sensorModel.X0, y: sensorModel.Y0) / focuserStepSizeMicrons;
            var topLeftFocuser = centerFocuser - sensorModel.TiltAt(x: -1.0 / 2.0 * width, y: -1.0 / 2.0 * height) / focuserStepSizeMicrons;
            var topRightFocuser = centerFocuser - sensorModel.TiltAt(x: 1.0 / 2.0 * width, y: -1.0 / 2.0 * height) / focuserStepSizeMicrons;
            var bottomLeftFocuser = centerFocuser - sensorModel.TiltAt(x: -1.0 / 2.0 * width, y: 1.0 / 2.0 * height) / focuserStepSizeMicrons;
            var bottomRightFocuser = centerFocuser - sensorModel.TiltAt(x: 1.0 / 2.0 * width, y: 1.0 / 2.0 * height) / focuserStepSizeMicrons;

            return TiltPlaneModel.Create(imageSize: imageSize, focuserStepSizeMicrons: focuserStepSizeMicrons, centerFocuser: centerFocuser,
                topLeftFocuser: topLeftFocuser, topRightFocuser: topRightFocuser, bottomLeftFocuser: bottomLeftFocuser, bottomRightFocuser: bottomRightFocuser);
        }
    }
}