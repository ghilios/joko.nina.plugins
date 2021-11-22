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
using System;

namespace Joko.NINA.Plugins.TenMicron.ModelBuilder {

    public class ResponseBase {

        public ResponseBase(string rawResponse) {
            this.RawResponse = rawResponse;
        }

        public string RawResponse { get; private set; }
    }

    public class Response<T> : ResponseBase {

        public Response(T value, string rawResponse) : base(rawResponse) {
            this.Value = value;
        }

        public T Value { get; private set; }

        public static implicit operator T(Response<T> r) => r.Value;

        public override string ToString() {
            return Value.ToString();
        }
    }

    public class CoordinateAngle {
        public static CoordinateAngle ZERO = new CoordinateAngle(true, 0, 0, 0, 0);

        public CoordinateAngle(bool positive, int degrees, int minutes, int seconds, int hundredthSeconds) {
            this.Positive = positive;
            this.Degrees = degrees;
            this.Minutes = minutes;
            this.Seconds = seconds;
            this.HundredthSeconds = hundredthSeconds;
        }

        public bool Positive { get; private set; }
        public int Degrees { get; private set; }
        public int Minutes { get; private set; }
        public int Seconds { get; private set; }
        public int HundredthSeconds { get; private set; }

        public override string ToString() {
            return $"{(Positive ? '+' : '-')}{Degrees:00}*{Minutes:00}:{Seconds:00}.{HundredthSeconds:00}";
        }

        public Angle ToAngle() {
            var degrees = (double)Degrees + (Minutes * 6000 + Seconds * 100 + HundredthSeconds) / (6000d * 60d);
            if (!Positive) {
                degrees *= -1;
            }
            return Angle.ByDegree(degrees);
        }

        public CoordinateAngle RoundSeconds() {
            if (HundredthSeconds < 50) {
                return this;
            }
            var degrees = Degrees;
            var minutes = Minutes;
            var seconds = Seconds + 1;
            if (seconds == 60) {
                seconds = 0;
                ++minutes;
            }
            if (minutes == 60) {
                minutes = 0;
                ++degrees;
            }
            return new CoordinateAngle(Positive, degrees, minutes, seconds, 0);
        }
    }

    public class AstrometricTime {
        public static AstrometricTime ZERO = new AstrometricTime(0, 0, 0, 0);

        public AstrometricTime(int hours, int minutes, int seconds, int hundredthSeconds) {
            this.Hours = hours;
            this.Minutes = minutes;
            this.Seconds = seconds;
            this.HundredthSeconds = hundredthSeconds;
        }

        public int Hours { get; private set; }
        public int Minutes { get; private set; }
        public int Seconds { get; private set; }
        public int HundredthSeconds { get; private set; }

        public override string ToString() {
            return $"{Hours:00}:{Minutes:00}:{Seconds:00}.{HundredthSeconds:00}";
        }

        public Angle ToAngle() {
            var hours = (double)Hours + (Minutes * 6000 + Seconds * 100 + HundredthSeconds) / (6000d * 60d);
            return Angle.ByHours(hours);
        }

        public AstrometricTime RoundTenthSecond() {
            if (HundredthSeconds % 10 == 0) {
                return this;
            }

            var hours = Hours;
            var minutes = Minutes;
            var seconds = Seconds;
            var tenthSeconds = HundredthSeconds / 10;
            if (HundredthSeconds % 10 >= 5) {
                ++tenthSeconds;
            }
            if (tenthSeconds == 10) {
                tenthSeconds = 0;
                ++seconds;
            }
            if (seconds == 60) {
                seconds = 0;
                ++minutes;
            }
            if (minutes == 60) {
                minutes = 0;
                ++hours;
            }
            return new AstrometricTime(hours, minutes, seconds, tenthSeconds * 10);
        }
    }

    public class AlignmentStarInfo {

        public AlignmentStarInfo(AstrometricTime rightAscension, CoordinateAngle declination, decimal errorArcseconds) {
            this.RightAscension = rightAscension;
            this.Declination = declination;
            this.ErrorArcseconds = errorArcseconds;
        }

        public AstrometricTime RightAscension { get; private set; }
        public CoordinateAngle Declination { get; private set; }
        public decimal ErrorArcseconds { get; private set; }

        // TODO: Use a converter for this? Hack for now
        public double Altitude { get; private set; }

        public double Azimuth { get; private set; }

        public override string ToString() {
            return $"RA: {RightAscension}, DEC: {Declination}. Error={ErrorArcseconds:.0} arcseconds";
        }
    }

    public class AlignmentModelInfo : BaseINPC {

        public AlignmentModelInfo(
            decimal rightAscensionAzimuth,
            decimal rightAscensionAltitude,
            decimal polarAlignErrorDegrees,
            decimal rightAscensionPolarPositionAngleDegrees,
            decimal orthogonalityErrorDegrees,
            decimal azimuthAdjustmentTurns,
            decimal altitudeAdjustmentTurns,
            int modelTerms,
            decimal rmsError) {
            this.RightAscensionAzimuth = rightAscensionAzimuth;
            this.RightAscensionAltitude = rightAscensionAltitude;
            this.PolarAlignErrorDegrees = polarAlignErrorDegrees;
            this.RightAscensionPolarPositionAngleDegrees = rightAscensionPolarPositionAngleDegrees;
            this.OrthogonalityErrorDegrees = orthogonalityErrorDegrees;
            this.AzimuthAdjustmentTurns = azimuthAdjustmentTurns;
            this.AltitudeAdjustmentTurns = altitudeAdjustmentTurns;
            this.ModelTerms = modelTerms;
            this.RMSError = rmsError;
        }

        private decimal rightAscensionAzimuth;

        public decimal RightAscensionAzimuth {
            get => rightAscensionAzimuth;
            private set {
                if (rightAscensionAzimuth != value) {
                    rightAscensionAzimuth = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal rightAscensionAltitude;

        public decimal RightAscensionAltitude {
            get => rightAscensionAltitude;
            private set {
                if (rightAscensionAltitude != value) {
                    rightAscensionAltitude = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal polarAlignErrorDegrees;

        public decimal PolarAlignErrorDegrees {
            get => polarAlignErrorDegrees;
            private set {
                if (polarAlignErrorDegrees != value) {
                    polarAlignErrorDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal rightAscensionPolarPositionAngleDegrees;

        public decimal RightAscensionPolarPositionAngleDegrees {
            get => rightAscensionPolarPositionAngleDegrees;
            private set {
                if (rightAscensionPolarPositionAngleDegrees != value) {
                    rightAscensionPolarPositionAngleDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal orthogonalityErrorDegrees;

        public decimal OrthogonalityErrorDegrees {
            get => orthogonalityErrorDegrees;
            private set {
                if (orthogonalityErrorDegrees != value) {
                    orthogonalityErrorDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal azimuthAdjustmentTurns;

        public decimal AzimuthAdjustmentTurns {
            get => azimuthAdjustmentTurns;
            private set {
                if (azimuthAdjustmentTurns != value) {
                    azimuthAdjustmentTurns = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal altitudeAdjustmentTurns;

        public decimal AltitudeAdjustmentTurns {
            get => altitudeAdjustmentTurns;
            private set {
                if (altitudeAdjustmentTurns != value) {
                    altitudeAdjustmentTurns = value;
                    RaisePropertyChanged();
                }
            }
        }

        private int modelTerms;

        public int ModelTerms {
            get => modelTerms;
            private set {
                if (modelTerms != value) {
                    modelTerms = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal rmsError;

        public decimal RMSError {
            get => rmsError;
            private set {
                if (rmsError != value) {
                    rmsError = value;
                    RaisePropertyChanged();
                }
            }
        }

        public static AlignmentModelInfo Empty() {
            return new AlignmentModelInfo(
                rightAscensionAzimuth: decimal.MinValue,
                rightAscensionAltitude: decimal.MinValue,
                polarAlignErrorDegrees: decimal.MinValue,
                rightAscensionPolarPositionAngleDegrees: decimal.MinValue,
                orthogonalityErrorDegrees: decimal.MinValue,
                azimuthAdjustmentTurns: decimal.MinValue,
                altitudeAdjustmentTurns: decimal.MinValue,
                modelTerms: -1,
                rmsError: decimal.MinValue);
        }

        public void CopyFrom(AlignmentModelInfo other) {
            this.RightAscensionAzimuth = other.RightAscensionAzimuth;
            this.RightAscensionAltitude = other.RightAscensionAltitude;
            this.PolarAlignErrorDegrees = other.PolarAlignErrorDegrees;
            this.RightAscensionPolarPositionAngleDegrees = other.RightAscensionPolarPositionAngleDegrees;
            this.OrthogonalityErrorDegrees = other.OrthogonalityErrorDegrees;
            this.AzimuthAdjustmentTurns = other.AzimuthAdjustmentTurns;
            this.AltitudeAdjustmentTurns = other.AltitudeAdjustmentTurns;
            this.ModelTerms = other.ModelTerms;
            this.RMSError = other.RMSError;
        }

        public override string ToString() {
            return $"RA Azimuth={RightAscensionAzimuth}, RA Altitude={RightAscensionAltitude}, PA Error={PolarAlignErrorDegrees}°, RA Polar Angle={RightAscensionPolarPositionAngleDegrees}°, Azimuth Knob Turns={Math.Abs(AzimuthAdjustmentTurns)} {(AzimuthAdjustmentTurns > 0 ? "left" : "right")}, Altitude Knob Turns={Math.Abs(AltitudeAdjustmentTurns)} {(AltitudeAdjustmentTurns > 0 ? "left" : "right")}, Model Terms={ModelTerms}, RMS Error={RMSError}";
        }
    }

    public class ProductFirmware {

        public ProductFirmware(string productName, DateTime timestamp, Version version) {
            this.ProductName = productName;
            this.Timestamp = timestamp;
            this.Version = version;
        }

        public string ProductName { get; private set; }
        public DateTime Timestamp { get; private set; }
        public Version Version { get; private set; }

        public override string ToString() {
            return $"{ProductName} v{Version} {Timestamp}";
        }
    }
}