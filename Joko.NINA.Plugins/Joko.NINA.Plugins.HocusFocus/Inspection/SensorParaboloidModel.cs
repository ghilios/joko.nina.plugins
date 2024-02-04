#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Inspection {

    public class SensorParaboloidDataPoint : INonLinearLeastSquaresDataPoint {

        public SensorParaboloidDataPoint(double x, double y, double focuserPosition, double rSquared) {
            this.X = x;
            this.Y = y;
            this.FocuserPosition = focuserPosition;
            this.RSquared = rSquared;
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public double FocuserPosition { get; private set; }
        public double RSquared { get; private set; }

        public double[] ToInput() {
            return new double[] { X, Y };
        }

        public double ToOutput() {
            return FocuserPosition;
        }

        public override string ToString() {
            return $"{{{nameof(X)}={X.ToString()}, {nameof(Y)}={Y.ToString()}, {nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(RSquared)}={RSquared.ToString()}}}";
        }
    }

    public class SensorParaboloidModel : INonLinearLeastSquaresParameters {

        public SensorParaboloidModel(double x0, double y0, double z0, double theta, double phi, double c) {
            this.X0 = x0;
            this.Y0 = y0;
            this.Z0 = z0;
            this.Theta = theta;
            this.Phi = phi;
            this.C = c;
        }

        public SensorParaboloidModel() {
        }

        public void FromArray(double[] parameters) {
            if (parameters == null || parameters.Length != 6) {
                throw new ArgumentException($"Expected a 6-element array of parameters");
            }

            this.X0 = parameters[0];
            this.Y0 = parameters[1];
            this.Z0 = parameters[2];
            this.Theta = parameters[3];
            this.Phi = parameters[4];
            this.C = parameters[5];
        }

        public double[] ToArray() {
            return new double[] {
                this.X0,
                this.Y0,
                this.Z0,
                this.Theta,
                this.Phi,
                this.C
            };
        }

        public double X0 { get; private set; }
        public double Y0 { get; private set; }
        public double Z0 { get; private set; }
        public double Theta { get; private set; }
        public double Phi { get; private set; }
        public double C { get; private set; }
        public int StarsInModel { get; private set; }
        public double GoodnessOfFit { get; private set; }
        public double RMSErrorMicrons { get; private set; }

        public void EvaluateFit(NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel> nlSolver, SensorParaboloidSolver sensorModelSolver) {
            StarsInModel = nlSolver.InputEnabledCount;
            GoodnessOfFit = nlSolver.GoodnessOfFit(sensorModelSolver, this);
            RMSErrorMicrons = nlSolver.RMSError(sensorModelSolver, this);
        }

        public override string ToString() {
            return $"{{{nameof(X0)}={X0.ToString()}, {nameof(Y0)}={Y0.ToString()}, {nameof(Z0)}={Z0.ToString()}, {nameof(Theta)}={Theta.ToString()}, {nameof(Phi)}={Phi.ToString()}, {nameof(C)}={C.ToString()}}}";
        }

        public double ValueAt(double x, double y) {
            var XPrime = x - X0;
            var YPrime = y - Y0;
            var XPrime2 = XPrime * XPrime;
            var YPrime2 = YPrime * YPrime;
            var C2 = Math.Sign(C) * C * C;

            var ZPrime = C2 * (XPrime2 + YPrime2);
            var tanTheta = Math.Tan(Theta);
            var sinPhi = Math.Sin(Phi);
            var cosPhi = Math.Cos(Phi);
            var result = (XPrime * cosPhi + YPrime * sinPhi) * tanTheta + ZPrime + Z0;
            return result;
        }

        public double TiltAt(double x, double y) {
            var XPrime = x;
            var YPrime = y;
            var tanTheta = Math.Tan(Theta);
            var sinPhi = Math.Sin(Phi);
            var cosPhi = Math.Cos(Phi);
            var result = (XPrime * cosPhi + YPrime * sinPhi) * tanTheta;
            return result;
        }

        public double CurvatureAt(double x, double y) {
            var XPrime = x;
            var YPrime = y;
            var C2 = Math.Sign(C) * C * C;
            var result = C2 * (XPrime * XPrime + YPrime * YPrime);
            return result;
        }

        public double Volume(double widthMicrons, double heightMicrons) {
            var w = widthMicrons;
            var h = heightMicrons;
            var C2 = Math.Sign(C) * C * C;

            var cosP = Math.Cos(Phi);
            var sinP = Math.Sin(Phi);
            var tanT = Math.Tan(Theta);

            // Double integral from [-w/2,w/2] and [-h/2,h/2]
            var part1 = C2 * w * h * (X0 * X0 + Y0 + Y0);
            var part2 = 1.0 / 12.0 * C2 * w * h * (w * w + h * h);
            var part3 = -w * h * tanT * (X0 * cosP + Y0 * sinP);
            var part4 = Z0 * w * h;
            var result = part1 + part2 + part3 + part4;
            return result;
        }
    }

    public class SensorParaboloidSolver : NonLinearLeastSquaresSolverBase<SensorParaboloidDataPoint, SensorParaboloidModel> {
        private readonly double inFocusMicrons;
        private readonly double sensorSizeMicronsX;
        private readonly double sensorSizeMicronsY;
        private readonly bool fixedSensorCenter;
        private readonly SensorParaboloidModel modelStart;

        public SensorParaboloidSolver(
            List<SensorParaboloidDataPoint> dataPoints,
            double sensorSizeMicronsX,
            double sensorSizeMicronsY,
            double inFocusMicrons,
            bool fixedSensorCenter,
            SensorParaboloidModel modelStart = null) : base(dataPoints, 6) {
            this.sensorSizeMicronsX = sensorSizeMicronsX;
            this.sensorSizeMicronsY = sensorSizeMicronsY;
            this.inFocusMicrons = inFocusMicrons;
            this.fixedSensorCenter = fixedSensorCenter;
            this.modelStart = modelStart;
        }

        public override bool UseJacobian => true;

        public bool PositiveCurvature { get; set; } = true;

        public override double Value(double[] parameters, double[] input) {
            var X = input[0];
            var Y = input[1];
            var x0 = parameters[0]; // u
            var y0 = parameters[1]; // v
            var z0 = parameters[2]; // w
            var theta = parameters[3]; // t
            var phi = parameters[4]; // p
            var c = parameters[5]; // c

            var XPrime = X - x0;
            var YPrime = Y - y0;
            var XPrime2 = XPrime * XPrime;
            var YPrime2 = YPrime * YPrime;
            var C2 = Math.Sign(c) * c * c;

            var ZPrime = C2 * (XPrime2 + YPrime2);
            var tanTheta = Math.Tan(theta);
            var sinPhi = Math.Sin(phi);
            var cosPhi = Math.Cos(phi);
            var result = (XPrime * cosPhi + YPrime * sinPhi) * tanTheta + ZPrime + z0;
            return result;
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            var X = input[0];
            var Y = input[1];
            var x0 = parameters[0]; // u
            var y0 = parameters[1]; // v
            var z0 = parameters[2]; // w
            var theta = parameters[3]; // t
            var phi = parameters[4]; // p
            var c = parameters[5]; // c

            var C2 = c * c;
            var SignedC2 = Math.Sign(c) * C2;
            var XPrime = X - x0;
            var XPrime2 = XPrime * XPrime;
            var YPrime = Y - y0;
            var YPrime2 = YPrime * YPrime;
            var cosPhi = Math.Cos(phi);
            var sinPhi = Math.Sin(phi);
            var tanTheta = Math.Tan(theta);
            var cosTheta = Math.Cos(theta);
            var cosTheta2 = cosTheta * cosTheta;
            var secTheta2 = 1.0 / cosTheta2;

            // d/du = https://www.wolframalpha.com/input?i2d=true&i=differentiate+%5C%2840%29%5C%2840%29x+-+u%5C%2841%29*Cos%5Bp%5D%2B%5C%2840%29y-v%5C%2841%29*Sin%5Bp%5D%5C%2841%29*Tan%5Bt%5D%2Bsign%5C%2840%29c%5C%2841%29*Power%5Bc%2C2%5D*%5C%2840%29Power%5B%5C%2840%29x-u%5C%2841%29%2C2%5D%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29%2Bw+with+respect+to+u
            var d_du = -2.0 * SignedC2 * XPrime - cosPhi * tanTheta;

            // d/dv = https://www.wolframalpha.com/input?i2d=true&i=differentiate+%5C%2840%29%5C%2840%29x+-+u%5C%2841%29*Cos%5Bp%5D%2B%5C%2840%29y-v%5C%2841%29*Sin%5Bp%5D%5C%2841%29*Tan%5Bt%5D%2Bsign%5C%2840%29c%5C%2841%29*Power%5Bc%2C2%5D*%5C%2840%29Power%5B%5C%2840%29x-u%5C%2841%29%2C2%5D%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29%2Bw+with+respect+to+v
            var d_dv = -2.0 * SignedC2 * YPrime - sinPhi * tanTheta;

            // d/dw = https://www.wolframalpha.com/input?i2d=true&i=differentiate+%5C%2840%29%5C%2840%29x+-+u%5C%2841%29*Cos%5Bp%5D%2B%5C%2840%29y-v%5C%2841%29*Sin%5Bp%5D%5C%2841%29*Tan%5Bt%5D%2Bsign%5C%2840%29c%5C%2841%29*Power%5Bc%2C2%5D*%5C%2840%29Power%5B%5C%2840%29x-u%5C%2841%29%2C2%5D%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29%2Bw+with+respect+to+w
            var d_dw = 1.0;

            // d/dt = https://www.wolframalpha.com/input?i2d=true&i=differentiate+%5C%2840%29%5C%2840%29x+-+u%5C%2841%29*Cos%5Bp%5D%2B%5C%2840%29y-v%5C%2841%29*Sin%5Bp%5D%5C%2841%29*Tan%5Bt%5D%2Bsign%5C%2840%29c%5C%2841%29*Power%5Bc%2C2%5D*%5C%2840%29Power%5B%5C%2840%29x-u%5C%2841%29%2C2%5D%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29%2Bw+with+respect+to+t
            var d_dt = secTheta2 * (cosPhi * XPrime + sinPhi * YPrime);

            // d/dp = https://www.wolframalpha.com/input?i2d=true&i=differentiate+%5C%2840%29%5C%2840%29x+-+u%5C%2841%29*Cos%5Bp%5D%2B%5C%2840%29y-v%5C%2841%29*Sin%5Bp%5D%5C%2841%29*Tan%5Bt%5D%2Bsign%5C%2840%29c%5C%2841%29*Power%5Bc%2C2%5D*%5C%2840%29Power%5B%5C%2840%29x-u%5C%2841%29%2C2%5D%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29%2Bw+with+respect+to+p
            var d_dp = tanTheta * (cosPhi * YPrime - sinPhi * XPrime);

            // d/dc = https://www.wolframalpha.com/input?i2d=true&i=differentiate+%5C%2840%29%5C%2840%29x+-+u%5C%2841%29*Cos%5Bp%5D%2B%5C%2840%29y-v%5C%2841%29*Sin%5Bp%5D%5C%2841%29*Tan%5Bt%5D%2Bsign%5C%2840%29c%5C%2841%29*Power%5Bc%2C2%5D*%5C%2840%29Power%5B%5C%2840%29x-u%5C%2841%29%2C2%5D%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29%2Bw+with+respect+to+c
            var d_dc = 2.0 * c * Math.Sign(c) * (XPrime2 + YPrime2);

            result[0] = d_du;
            result[1] = d_dv;
            result[2] = d_dw;
            result[3] = d_dt;
            result[4] = d_dp;
            result[5] = d_dc;
        }

        public override void SetInitialGuess(double[] initialGuess) {
            if (modelStart != null) {
                initialGuess[0] = modelStart.X0;
                initialGuess[1] = modelStart.Y0;
                initialGuess[2] = modelStart.Z0;
                initialGuess[3] = modelStart.Theta;
                initialGuess[4] = modelStart.Phi;
                initialGuess[5] = modelStart.C;
            } else {
                initialGuess[0] = 0.0;
                initialGuess[1] = 0.0;
                initialGuess[2] = inFocusMicrons;
                initialGuess[3] = 1E-5;
                initialGuess[4] = 0.0;
                initialGuess[5] = PositiveCurvature ? 1E-4 : -1E-4;
            }
        }

        public override void SetBounds(double[] lowerBounds, double[] upperBounds) {
            if (fixedSensorCenter) {
                lowerBounds[0] = 0.0;
                lowerBounds[1] = 0.0;
                upperBounds[0] = 0.0;
                upperBounds[1] = 0.0;
            } else {
                lowerBounds[0] = -sensorSizeMicronsX / 2.0;
                lowerBounds[1] = -sensorSizeMicronsY / 2.0;
                upperBounds[0] = sensorSizeMicronsX / 2.0;
                upperBounds[1] = sensorSizeMicronsY / 2.0;
            }

            lowerBounds[2] = double.NegativeInfinity;
            lowerBounds[3] = 1E-5; // Ensure tilt never goes to 0, since perfection is impossible. This forces calculation of phi, and avoids situations where we're stuck at a local minima
            lowerBounds[4] = -Math.PI;
            lowerBounds[5] = PositiveCurvature ? 1E-4 : double.NegativeInfinity;

            upperBounds[2] = double.PositiveInfinity;
            upperBounds[3] = Math.PI - double.Epsilon;
            upperBounds[4] = Math.PI - double.Epsilon;
            upperBounds[5] = PositiveCurvature ? double.PositiveInfinity : -1E-4;
        }

        public override void SetScale(double[] scales) {
            scales[0] = 1.0;
            scales[1] = 1.0;
            scales[2] = 1.0;
            scales[3] = 1E-2;
            scales[4] = 1.0;
            scales[5] = 1E-2;
        }
    }
}