#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Collections.Generic;
using static NINA.Joko.Plugins.HocusFocus.Inspection.SensorModel;

namespace NINA.Joko.Plugins.HocusFocus.Inspection {

    public class SensorParaboloidDataPoint : INonLinearLeastSquaresDataPoint {

        public SensorParaboloidDataPoint(double x, double y, double focuserPosition, double rSquared, double focuserPositionStdDev = 1.0) {
            this.X = x;
            this.Y = y;
            this.FocuserPosition = focuserPosition;
            this.RSquared = rSquared;
            this.FocuserPositionStdDev = focuserPositionStdDev;
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public double FocuserPosition { get; private set; }
        public double RSquared { get; private set; }

        /// <summary>
        /// 1-sigma uncertainty of <see cref="FocuserPosition"/> (the standard error of this star's
        /// best-focus position, propagated from its hyperbolic fit). Used to weight the paraboloid fit
        /// by 1/σ². Defaults to 1.0 (unweighted) when no uncertainty is available.
        /// </summary>
        public double FocuserPositionStdDev { get; private set; }

        public double[] ToInput() {
            return new double[] { X, Y };
        }

        public double ToOutput() {
            return FocuserPosition;
        }

        public double ToOutputStdDev() {
            return FocuserPositionStdDev;
        }

        public override string ToString() {
            return $"{{{nameof(X)}={X.ToString()}, {nameof(Y)}={Y.ToString()}, {nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(RSquared)}={RSquared.ToString()}, {nameof(FocuserPositionStdDev)}={FocuserPositionStdDev.ToString()}}}";
        }
    }

    public class RegistrationAndFitResult {

        public RegistrationAndFitResult(List<SensorParaboloidDataPoint> points, RegisteredStar[] registeredStars) {
            this.Points = points;
            this.RegisteredStars = registeredStars;
        }

        public List<SensorParaboloidDataPoint> Points { get; private set; }
        public RegisteredStar[] RegisteredStars { get; private set; }
    }

    /// <summary>
    /// Tilted paraboloid sensor model. The surface is parameterized by linear tilt gradients (Gx, Gy)
    /// and a single signed curvature coefficient (K):
    ///
    ///     z(x, y) = Gx·(x − X0) + Gy·(y − Y0) + K·((x − X0)² + (y − Y0)²) + Z0
    ///
    /// This linear-in-(Gx, Gy, K) form replaces the older (θ, φ, c) parameterization, which injected
    /// trigonometric nonlinearity, a φ-unidentifiability/local-minimum trap at θ = 0 (worked around by
    /// forcing θ ≥ 1e-5), and a sign(c)·c² curvature that could not cross zero (forcing two solves).
    /// The legacy tilt/curvature quantities remain available as derived display properties.
    /// </summary>
    public class SensorParaboloidModel : INonLinearLeastSquaresParameters {

        public SensorParaboloidModel(double x0, double y0, double z0, double gx, double gy, double k) {
            this.X0 = x0;
            this.Y0 = y0;
            this.Z0 = z0;
            this.Gx = gx;
            this.Gy = gy;
            this.K = k;
        }

        public SensorParaboloidModel() {
        }

        /// <summary>
        /// Builds a model from the legacy display parameters (tilt angle θ, tilt azimuth φ, curvature c),
        /// converting them to the canonical (Gx, Gy, K) representation. Provided for readability and for
        /// tests; the optimizer always works in canonical parameters via <see cref="ToArray"/>.
        /// </summary>
        public static SensorParaboloidModel FromTiltCurvature(double x0, double y0, double z0, double theta, double phi, double c) {
            var tanTheta = Math.Tan(theta);
            return new SensorParaboloidModel(
                x0: x0, y0: y0, z0: z0,
                gx: tanTheta * Math.Cos(phi),
                gy: tanTheta * Math.Sin(phi),
                k: Math.Sign(c) * c * c);
        }

        public void FromArray(double[] parameters) {
            if (parameters == null || parameters.Length != 6) {
                throw new ArgumentException($"Expected a 6-element array of parameters");
            }

            this.X0 = parameters[0];
            this.Y0 = parameters[1];
            this.Z0 = parameters[2];
            this.Gx = parameters[3];
            this.Gy = parameters[4];
            this.K = parameters[5];
        }

        public double[] ToArray() {
            return new double[] {
                this.X0,
                this.Y0,
                this.Z0,
                this.Gx,
                this.Gy,
                this.K
            };
        }

        public double X0 { get; private set; }
        public double Y0 { get; private set; }
        public double Z0 { get; private set; }

        /// <summary>Tilt gradient along X: change in focuser position per micron of sensor X (dimensionless).</summary>
        public double Gx { get; private set; }

        /// <summary>Tilt gradient along Y: change in focuser position per micron of sensor Y (dimensionless).</summary>
        public double Gy { get; private set; }

        /// <summary>Signed curvature coefficient: curvature contribution is K·r². Equivalent to the old sign(c)·c².</summary>
        public double K { get; private set; }

        // Derived display parameters, kept for back-compat with the (θ, φ, c) form used by the UI/result layer.
        public double Theta => Math.Atan(Math.Sqrt(Gx * Gx + Gy * Gy));
        public double Phi => Math.Atan2(Gy, Gx);
        public double C => Math.Sign(K) * Math.Sqrt(Math.Abs(K));

        public int StarsInModel { get; private set; }
        public double GoodnessOfFit { get; private set; }
        public double RMSErrorMicrons { get; private set; }
        public double ReducedChiSquared { get; private set; }
        public double ChiSquaredPValue { get; private set; }

        public void EvaluateFit(NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel> nlSolver, SensorParaboloidSolver sensorModelSolver) {
            StarsInModel = nlSolver.InputEnabledCount;
            GoodnessOfFit = nlSolver.GoodnessOfFit(sensorModelSolver, this);
            RMSErrorMicrons = nlSolver.RMSError(sensorModelSolver, this);
            ReducedChiSquared = nlSolver.ReducedChiSquared(sensorModelSolver, this);
            ChiSquaredPValue = nlSolver.ChiSquaredPValue(sensorModelSolver, this);
        }

        public override string ToString() {
            return $"{{{nameof(X0)}={X0.ToString()}, {nameof(Y0)}={Y0.ToString()}, {nameof(Z0)}={Z0.ToString()}, {nameof(Gx)}={Gx.ToString()}, {nameof(Gy)}={Gy.ToString()}, {nameof(K)}={K.ToString()}}}";
        }

        public double ValueAt(double x, double y) {
            var XPrime = x - X0;
            var YPrime = y - Y0;
            var tilt = Gx * XPrime + Gy * YPrime;
            var curvature = K * (XPrime * XPrime + YPrime * YPrime);
            return tilt + curvature + Z0;
        }

        public double TiltAt(double x, double y) {
            return Gx * x + Gy * y;
        }

        public double CurvatureAt(double x, double y) {
            return K * (x * x + y * y);
        }

        public double Volume(double widthMicrons, double heightMicrons) {
            var w = widthMicrons;
            var h = heightMicrons;

            // Closed-form double integral of ValueAt over [-w/2,w/2] x [-h/2,h/2]:
            //   tilt:      -w·h·(Gx·X0 + Gy·Y0)
            //   curvature:  K·w·h·(X0² + Y0²) + (1/12)·K·w·h·(w² + h²)
            //   offset:     Z0·w·h
            var tiltPart = -w * h * (Gx * X0 + Gy * Y0);
            var curvatureCenterPart = K * w * h * (X0 * X0 + Y0 * Y0);
            var curvatureSpreadPart = 1.0 / 12.0 * K * w * h * (w * w + h * h);
            var offsetPart = Z0 * w * h;
            return tiltPart + curvatureCenterPart + curvatureSpreadPart + offsetPart;
        }
    }

    public class SensorParaboloidSolver : NonLinearLeastSquaresSolverBase<SensorParaboloidDataPoint, SensorParaboloidModel> {
        private readonly double inFocusMicrons;
        private readonly double sensorSizeMicronsX;
        private readonly double sensorSizeMicronsY;
        private readonly bool fixedSensorCenter;

        public SensorParaboloidSolver(
            List<SensorParaboloidDataPoint> dataPoints,
            double sensorSizeMicronsX,
            double sensorSizeMicronsY,
            double inFocusMicrons,
            bool fixedSensorCenter) : base(dataPoints, 6) {
            this.sensorSizeMicronsX = sensorSizeMicronsX;
            this.sensorSizeMicronsY = sensorSizeMicronsY;
            this.inFocusMicrons = inFocusMicrons;
            this.fixedSensorCenter = fixedSensorCenter;
        }

        public override bool UseJacobian => true;

        // z = Gx·(X-x0) + Gy·(Y-y0) + K·((X-x0)² + (Y-y0)²) + z0
        public override double Value(double[] parameters, double[] input) {
            var X = input[0];
            var Y = input[1];
            var x0 = parameters[0];
            var y0 = parameters[1];
            var z0 = parameters[2];
            var gx = parameters[3];
            var gy = parameters[4];
            var k = parameters[5];

            var XPrime = X - x0;
            var YPrime = Y - y0;
            return gx * XPrime + gy * YPrime + k * (XPrime * XPrime + YPrime * YPrime) + z0;
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            var X = input[0];
            var Y = input[1];
            var x0 = parameters[0];
            var y0 = parameters[1];
            var gx = parameters[3];
            var gy = parameters[4];
            var k = parameters[5];

            var XPrime = X - x0;
            var YPrime = Y - y0;

            // d/dx0 [Gx·(X-x0) + K·(X-x0)²] = -Gx - 2K·(X-x0)
            result[0] = -gx - 2.0 * k * XPrime;
            // d/dy0
            result[1] = -gy - 2.0 * k * YPrime;
            // d/dz0
            result[2] = 1.0;
            // d/dGx
            result[3] = XPrime;
            // d/dGy
            result[4] = YPrime;
            // d/dK
            result[5] = XPrime * XPrime + YPrime * YPrime;
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

            // z0, the tilt gradients, and the signed curvature coefficient are all unbounded. The linear
            // tilt parameterization has no singularity (so no θ ≥ 1e-5 hack is needed), and K can cross
            // zero freely (so a single solve covers both curvature signs — no positive/negative double solve).
            lowerBounds[2] = double.NegativeInfinity;
            lowerBounds[3] = double.NegativeInfinity;
            lowerBounds[4] = double.NegativeInfinity;
            lowerBounds[5] = double.NegativeInfinity;

            upperBounds[2] = double.PositiveInfinity;
            upperBounds[3] = double.PositiveInfinity;
            upperBounds[4] = double.PositiveInfinity;
            upperBounds[5] = double.PositiveInfinity;
        }

        public override void SetScale(double[] scales) {
            scales[0] = 1.0;
            scales[1] = 1.0;
            scales[2] = 1.0;
            scales[3] = 1E-3;
            scales[4] = 1E-3;
            scales[5] = 1E-6;
        }
    }
}
