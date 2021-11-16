#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
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

        public override string ToString() {
            return $"RA: {RightAscension}, DEC: {Declination}. Error={ErrorArcseconds:.0} arcseconds";
        }
    }

    public class AlignmentModelInfo {

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

        public decimal RightAscensionAzimuth { get; private set; }
        public decimal RightAscensionAltitude { get; private set; }
        public decimal PolarAlignErrorDegrees { get; private set; }
        public decimal RightAscensionPolarPositionAngleDegrees { get; private set; }
        public decimal OrthogonalityErrorDegrees { get; private set; }
        public decimal AzimuthAdjustmentTurns { get; private set; }
        public decimal AltitudeAdjustmentTurns { get; private set; }
        public int ModelTerms { get; private set; }
        public decimal RMSError { get; private set; }

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