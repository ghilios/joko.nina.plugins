#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Inspection {

    public class SensorParaboloidDataPoint : INonLinearLeastSquaresDataPoint {

        public SensorParaboloidDataPoint(double x, double y, double focuserPosition) {
            this.X = x;
            this.Y = y;
            this.FocuserPosition = focuserPosition;
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public double FocuserPosition { get; private set; }

        public double[] ToInput() {
            return new double[] { X, Y };
        }

        public double ToOutput() {
            return FocuserPosition;
        }

        public override string ToString() {
            return $"{{{nameof(X)}={X.ToString()}, {nameof(Y)}={Y.ToString()}, {nameof(FocuserPosition)}={FocuserPosition.ToString()}}}";
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

        public override string ToString() {
            return $"{{{nameof(X0)}={X0.ToString()}, {nameof(Y0)}={Y0.ToString()}, {nameof(Z0)}={Z0.ToString()}, {nameof(Theta)}={Theta.ToString()}, {nameof(Phi)}={Phi.ToString()}, {nameof(C)}={C.ToString()}}}";
        }
    }

    public class SensorParaboloidSolver : NonLinearLeastSquaresSolverBase<SensorParaboloidDataPoint, SensorParaboloidModel> {
        private readonly double inFocusMicrons;
        private readonly double sensorSizeMicronsX;
        private readonly double sensorSizeMicronsY;

        public SensorParaboloidSolver(
            List<SensorParaboloidDataPoint> dataPoints,
            double sensorSizeMicronsX,
            double sensorSizeMicronsY,
            double inFocusMicrons) : base(dataPoints, 6) {
            this.sensorSizeMicronsX = sensorSizeMicronsX;
            this.sensorSizeMicronsY = sensorSizeMicronsY;
            this.inFocusMicrons = inFocusMicrons;
        }

        public override bool UseJacobian => false;

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

            var ZPrime = c * (XPrime2 + YPrime2);
            var sinTheta = Math.Sin(theta);
            var cosTheta = Math.Cos(theta);
            var sinPhi = Math.Sin(phi);
            var cosPhi = Math.Cos(phi);
            var result = XPrime * sinPhi + (-YPrime * sinTheta + ZPrime * cosTheta) * cosPhi + z0;
            return result;
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            var X = input[0];
            var Y = input[1];
            var x0 = parameters[0]; // u
            var y0 = parameters[1]; // v
            var z0 = parameters[2]; // w - Not used
            var theta = parameters[3]; // t
            var phi = parameters[4]; // p
            var c = parameters[5]; // c

            var XPrime = X - x0;
            var YPrime = Y - y0;
            var XPrime2 = XPrime * XPrime;
            var YPrime2 = YPrime * YPrime;
            var sinP = Math.Sin(phi);
            var cosP = Math.Cos(phi);
            var cosT = Math.Cos(theta);
            var tanT = Math.Tan(theta);
            var cosT2 = cosT * cosT;
            var secT2 = 1.0 / cosT2;

            // u
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+u
            // -2 c (-u + x) - Cos[p] Tan[t]
            var dz_du = -2 * c * XPrime - cosP * tanT;

            // v
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+v
            //  -2 c (-v + y) - Sin[p] Tan[t]
            var dz_dv = -2 * c * YPrime - sinP * tanT;

            // w
            var dz_dw = 1;

            // t
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+t
            //  Sec[t]^2 ((-u + x) Cos[p] + (-v + y) Sin[p])
            var dz_dt = secT2 * (XPrime * cosP + YPrime * sinP);

            // p
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+p
            //  ((-v + y) Cos[p] - (-u + x) Sin[p]) Tan[t]
            var dz_dp = tanT * (YPrime * cosP - XPrime * sinP);

            // c
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+c
            //  (-u + x)^2 + (-v + y)^2
            var dz_dc = XPrime2 + YPrime2;

            result[0] = dz_du;
            result[1] = dz_dv;
            result[2] = dz_dw;
            result[3] = dz_dt;
            result[4] = dz_dp;
            result[5] = dz_dc;
        }

        public override void SetInitialGuess(double[] initialGuess) {
            initialGuess[0] = 0.0;
            initialGuess[1] = 0.0;
            initialGuess[2] = inFocusMicrons;
            initialGuess[3] = 0.0;
            initialGuess[4] = 0.0;
            initialGuess[5] = 0.0;
        }

        public override void SetBounds(double[] lowerBounds, double[] upperBounds) {
            lowerBounds[0] = -sensorSizeMicronsX / 2.0;
            lowerBounds[1] = -sensorSizeMicronsY / 2.0;
            lowerBounds[2] = double.NegativeInfinity;
            lowerBounds[3] = -Math.PI / 2.0;
            lowerBounds[4] = -Math.PI / 2.0;
            lowerBounds[5] = double.NegativeInfinity;

            upperBounds[0] = sensorSizeMicronsX / 2.0;
            upperBounds[1] = sensorSizeMicronsY / 2.0;
            upperBounds[2] = double.PositiveInfinity;
            upperBounds[3] = Math.PI / 2.0;
            upperBounds[4] = Math.PI / 2.0;
            upperBounds[5] = double.PositiveInfinity;
        }

        public override void SetScale(double[] scales) {
            scales[0] = 1.0;
            scales[1] = 1.0;
            scales[2] = 1.0;
            scales[3] = 1.0;
            scales[4] = 1.0;
            scales[5] = 1E-3;
        }
    }
}