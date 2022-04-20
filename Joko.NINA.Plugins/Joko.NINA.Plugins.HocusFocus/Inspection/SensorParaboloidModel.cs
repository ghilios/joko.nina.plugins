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

            var ZPrime = C * (XPrime2 + YPrime2);
            var tanTheta = Math.Tan(Theta);
            var sinPhi = Math.Sin(Phi);
            var cosPhi = Math.Cos(Phi);
            var result = (XPrime * cosPhi + YPrime * sinPhi) * tanTheta + ZPrime + Z0;
            return result;
        }

        public double TiltAt(double x, double y) {
            var tanTheta = Math.Tan(Theta);
            var sinPhi = Math.Sin(Phi);
            var cosPhi = Math.Cos(Phi);
            var result = (x * cosPhi + y * sinPhi) * tanTheta;
            return result;
        }

        public double CurvatureAt(double x, double y) {
            var result = C * (x * x + y * y);
            return result;
        }

        public double Volume(double widthMicrons, double heightMicrons) {
            var w = widthMicrons;
            var w3 = w * w * w;
            var h = heightMicrons;
            var h3 = h * h * h;

            var cosP = Math.Cos(Phi);
            var sinP = Math.Sin(Phi);
            var tanT = Math.Tan(Theta);

            // Double integral from [-w/2,w/2] and [-h/2,h/2]
            var result = -h * w * X0 * cosP * tanT - h * w * Y0 * sinP * tanT + h * C * w3 / 12.0 + w * C * h3 / 12.0 + Z0 * w * h;
            return result;
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
            var tanTheta = Math.Tan(theta);
            var sinPhi = Math.Sin(phi);
            var cosPhi = Math.Cos(phi);
            var result = (XPrime * cosPhi + YPrime * sinPhi) * tanTheta + ZPrime + z0;
            return result;
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            throw new NotImplementedException();
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
            lowerBounds[3] = 0.0;
            lowerBounds[4] = -Math.PI;
            lowerBounds[5] = double.NegativeInfinity;

            upperBounds[0] = sensorSizeMicronsX / 2.0;
            upperBounds[1] = sensorSizeMicronsY / 2.0;
            upperBounds[2] = double.PositiveInfinity;
            upperBounds[3] = Math.PI - double.Epsilon;
            upperBounds[4] = Math.PI - double.Epsilon;
            upperBounds[5] = double.PositiveInfinity;
        }

        public override void SetScale(double[] scales) {
            scales[0] = 1.0;
            scales[1] = 1.0;
            scales[2] = 1.0;
            scales[3] = 1.0;
            scales[4] = 1.0;
            scales[5] = 1E-4;
        }
    }
}