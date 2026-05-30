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
    /// and curvature coefficients:
    ///
    ///     z(x, y) = Gx·(x − X0) + Gy·(y − Y0) + Kx·(x − X0)² + Ky·(y − Y0)² + Z0
    ///
    /// In the default <b>isotropic</b> mode Kx == Ky == K (a single curvature coefficient, 6 free
    /// parameters), reproducing the rotationally-symmetric field curvature of a typical refractor or
    /// reflector. In the optional <b>astigmatic</b> mode Kx and Ky are fit independently (7 parameters),
    /// allowing the saddle-shaped field of an astigmatic optical train to be represented.
    ///
    /// This linear-in-(Gx, Gy, Kx, Ky) form replaces the older (θ, φ, c) parameterization, which injected
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
            this.Kx = k;
            this.Ky = k;
            this.Astigmatic = false;
        }

        public SensorParaboloidModel(double x0, double y0, double z0, double gx, double gy, double kx, double ky) {
            this.X0 = x0;
            this.Y0 = y0;
            this.Z0 = z0;
            this.Gx = gx;
            this.Gy = gy;
            this.Kx = kx;
            this.Ky = ky;
            this.Astigmatic = true;
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

        // The parameter-array length is the single source of truth for the mode: 6 elements => isotropic
        // (one curvature coefficient), 7 elements => astigmatic (independent Kx, Ky). The solver creates the
        // model via new() + FromArray, so inferring the mode here keeps the two in sync without extra wiring.
        public void FromArray(double[] parameters) {
            if (parameters == null || (parameters.Length != 6 && parameters.Length != 7)) {
                throw new ArgumentException($"Expected a 6- or 7-element array of parameters");
            }

            this.X0 = parameters[0];
            this.Y0 = parameters[1];
            this.Z0 = parameters[2];
            this.Gx = parameters[3];
            this.Gy = parameters[4];
            if (parameters.Length == 7) {
                this.Kx = parameters[5];
                this.Ky = parameters[6];
                this.Astigmatic = true;
            } else {
                this.Kx = parameters[5];
                this.Ky = parameters[5];
                this.Astigmatic = false;
            }
        }

        public double[] ToArray() {
            if (Astigmatic) {
                return new double[] { this.X0, this.Y0, this.Z0, this.Gx, this.Gy, this.Kx, this.Ky };
            }
            return new double[] { this.X0, this.Y0, this.Z0, this.Gx, this.Gy, this.K };
        }

        public double X0 { get; private set; }
        public double Y0 { get; private set; }
        public double Z0 { get; private set; }

        /// <summary>Tilt gradient along X: change in focuser position per micron of sensor X (dimensionless).</summary>
        public double Gx { get; private set; }

        /// <summary>Tilt gradient along Y: change in focuser position per micron of sensor Y (dimensionless).</summary>
        public double Gy { get; private set; }

        /// <summary>Signed curvature coefficient along X: the X² curvature contribution is Kx·(x−X0)².</summary>
        public double Kx { get; private set; }

        /// <summary>Signed curvature coefficient along Y: the Y² curvature contribution is Ky·(y−Y0)².</summary>
        public double Ky { get; private set; }

        /// <summary>True when Kx and Ky are fit independently (astigmatic field); false for isotropic curvature.</summary>
        public bool Astigmatic { get; private set; }

        /// <summary>
        /// Mean signed curvature coefficient. Equals the single coefficient in isotropic mode and the
        /// average of Kx, Ky in astigmatic mode. Curvature contribution is K·r² only when isotropic.
        /// </summary>
        public double K => 0.5 * (Kx + Ky);

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
            return $"{{{nameof(X0)}={X0.ToString()}, {nameof(Y0)}={Y0.ToString()}, {nameof(Z0)}={Z0.ToString()}, {nameof(Gx)}={Gx.ToString()}, {nameof(Gy)}={Gy.ToString()}, {nameof(Kx)}={Kx.ToString()}, {nameof(Ky)}={Ky.ToString()}, {nameof(Astigmatic)}={Astigmatic.ToString()}}}";
        }

        public double ValueAt(double x, double y) {
            var XPrime = x - X0;
            var YPrime = y - Y0;
            var tilt = Gx * XPrime + Gy * YPrime;
            var curvature = Kx * XPrime * XPrime + Ky * YPrime * YPrime;
            return tilt + curvature + Z0;
        }

        public double TiltAt(double x, double y) {
            return Gx * x + Gy * y;
        }

        public double CurvatureAt(double x, double y) {
            return Kx * x * x + Ky * y * y;
        }

        public double Volume(double widthMicrons, double heightMicrons) {
            var w = widthMicrons;
            var h = heightMicrons;

            // Closed-form double integral of ValueAt over [-w/2,w/2] x [-h/2,h/2]:
            //   tilt:      -w·h·(Gx·X0 + Gy·Y0)
            //   curvature:  w·h·(Kx·X0² + Ky·Y0²) + (1/12)·w·h·(Kx·w² + Ky·h²)
            //   offset:     Z0·w·h
            // (Isotropic Kx = Ky = K collapses the curvature terms to K·w·h·(X0²+Y0²) + (1/12)·K·w·h·(w²+h²).)
            var tiltPart = -w * h * (Gx * X0 + Gy * Y0);
            var curvatureCenterPart = w * h * (Kx * X0 * X0 + Ky * Y0 * Y0);
            var curvatureSpreadPart = 1.0 / 12.0 * w * h * (Kx * w * w + Ky * h * h);
            var offsetPart = Z0 * w * h;
            return tiltPart + curvatureCenterPart + curvatureSpreadPart + offsetPart;
        }
    }

    public class SensorParaboloidSolver : NonLinearLeastSquaresSolverBase<SensorParaboloidDataPoint, SensorParaboloidModel> {
        private readonly double inFocusMicrons;
        private readonly double sensorSizeMicronsX;
        private readonly double sensorSizeMicronsY;
        private readonly bool fixedSensorCenter;
        private readonly bool astigmatic;

        public SensorParaboloidSolver(
            List<SensorParaboloidDataPoint> dataPoints,
            double sensorSizeMicronsX,
            double sensorSizeMicronsY,
            double inFocusMicrons,
            bool fixedSensorCenter,
            bool astigmatic = false) : base(dataPoints, astigmatic ? 7 : 6) {
            this.sensorSizeMicronsX = sensorSizeMicronsX;
            this.sensorSizeMicronsY = sensorSizeMicronsY;
            this.inFocusMicrons = inFocusMicrons;
            this.fixedSensorCenter = fixedSensorCenter;
            this.astigmatic = astigmatic;
        }

        public override bool UseJacobian => true;

        // Isotropic (6 params):  z = Gx·(X-x0) + Gy·(Y-y0) + K·((X-x0)² + (Y-y0)²) + z0
        // Astigmatic (7 params): z = Gx·(X-x0) + Gy·(Y-y0) + Kx·(X-x0)² + Ky·(Y-y0)² + z0
        // The parameter-array length is authoritative so the same Value/Gradient work for either mode.
        public override double Value(double[] parameters, double[] input) {
            var X = input[0];
            var Y = input[1];
            var x0 = parameters[0];
            var y0 = parameters[1];
            var z0 = parameters[2];
            var gx = parameters[3];
            var gy = parameters[4];
            var kx = parameters[5];
            var ky = parameters.Length == 7 ? parameters[6] : parameters[5];

            var XPrime = X - x0;
            var YPrime = Y - y0;
            return gx * XPrime + gy * YPrime + kx * XPrime * XPrime + ky * YPrime * YPrime + z0;
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            var X = input[0];
            var Y = input[1];
            var x0 = parameters[0];
            var y0 = parameters[1];
            var gx = parameters[3];
            var gy = parameters[4];
            var kx = parameters[5];
            var ky = parameters.Length == 7 ? parameters[6] : parameters[5];

            var XPrime = X - x0;
            var YPrime = Y - y0;

            // d/dx0 [Gx·(X-x0) + Kx·(X-x0)²] = -Gx - 2·Kx·(X-x0)
            result[0] = -gx - 2.0 * kx * XPrime;
            // d/dy0
            result[1] = -gy - 2.0 * ky * YPrime;
            // d/dz0
            result[2] = 1.0;
            // d/dGx
            result[3] = XPrime;
            // d/dGy
            result[4] = YPrime;
            if (result.Length == 7) {
                // d/dKx, d/dKy
                result[5] = XPrime * XPrime;
                result[6] = YPrime * YPrime;
            } else {
                // d/dK (isotropic: Kx and Ky are the same parameter)
                result[5] = XPrime * XPrime + YPrime * YPrime;
            }
        }

        public override void SetInitialGuess(double[] initialGuess) {
            initialGuess[0] = 0.0;
            initialGuess[1] = 0.0;
            initialGuess[2] = inFocusMicrons;
            initialGuess[3] = 0.0;
            initialGuess[4] = 0.0;
            initialGuess[5] = 0.0;
            if (astigmatic) {
                initialGuess[6] = 0.0;
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

            // z0, the tilt gradients, and the curvature coefficient(s) are all unbounded. The linear
            // tilt parameterization has no singularity (so no θ ≥ 1e-5 hack is needed), and the curvature
            // can cross zero freely (so a single solve covers both curvature signs — no double solve).
            for (int i = 2; i < lowerBounds.Length; ++i) {
                lowerBounds[i] = double.NegativeInfinity;
                upperBounds[i] = double.PositiveInfinity;
            }
        }

        public override void SetScale(double[] scales) {
            scales[0] = 1.0;
            scales[1] = 1.0;
            scales[2] = 1.0;
            scales[3] = 1E-3;
            scales[4] = 1E-3;
            scales[5] = 1E-6;
            if (astigmatic) {
                scales[6] = 1E-6;
            }
        }
    }
}
