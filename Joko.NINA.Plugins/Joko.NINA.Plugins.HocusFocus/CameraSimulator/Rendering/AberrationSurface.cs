#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// The injected best-focus surface, expressed as the tilted paraboloid the aberration inspector fits
    /// (<see cref="NINA.Joko.Plugins.HocusFocus.Inspection.SensorParaboloidModel"/>), so that inject ⇄ recover
    /// is an algebraic identity. Coordinates are <b>sensor microns, centered</b> at the sensor middle; the
    /// surface value is in <b>µm of focuser travel</b>:
    ///
    ///     zBestFocus(x, y) = Gx·(x − X0) + Gy·(y − Y0) + K·((x − X0)² + (y − Y0)²) + Z0
    ///
    /// The config knobs are inverted onto (Gx, Gy, K) so that the inspector recovers exactly what was
    /// injected (spec §"The inverse"):
    /// <list type="bullet">
    /// <item>azimuth <c>atan2(Gy, Gx) == TiltAngleDegrees</c></item>
    /// <item><c>|Gx|·halfW + |Gy|·halfH == TiltAmountMicrons</c> (the inspector's <c>TiltEffectMicrons</c>)</item>
    /// <item><c>K·(halfW² + halfH²) == BackfocusErrorMicrons</c> (the inspector's <c>CurvatureEffectMicrons</c>)</item>
    /// </list>
    /// This is a pure local-defocus surface (no coma/astigmatism), which is precisely what makes it the
    /// inspector's inverse. It supplies the per-field-point defocus Δ; <see cref="DefocusModel"/> turns Δ into
    /// HFR / W20 / donut radii.
    /// </summary>
    public sealed class AberrationSurface {

        private readonly int widthPx;
        private readonly int heightPx;
        private readonly double pixelSizeMicrons;
        private readonly double focuserStepSizeMicrons; // k
        private readonly int optimalFocuserPosition;    // x0
        private readonly double halfWidthMicrons;
        private readonly double halfHeightMicrons;

        /// <summary>Optical-axis X offset X0, in sensor µm (0 when aberrations are disabled).</summary>
        public double X0 { get; }

        /// <summary>Optical-axis Y offset Y0, in sensor µm (0 when aberrations are disabled).</summary>
        public double Y0 { get; }

        /// <summary>Center best-focus Z0 = x0·k, in µm of focuser travel.</summary>
        public double Z0 { get; }

        /// <summary>Tilt gradient along X (change in focuser µm per sensor µm, dimensionless). 0 when disabled.</summary>
        public double Gx { get; }

        /// <summary>Tilt gradient along Y (change in focuser µm per sensor µm, dimensionless). 0 when disabled.</summary>
        public double Gy { get; }

        /// <summary>Isotropic curvature coefficient K = Kx = Ky (1/µm). 0 when disabled.</summary>
        public double K { get; }

        /// <summary>Tilt magnitude angle θ = atan(√(Gx²+Gy²)), in radians (the inspector's <c>Theta</c>).</summary>
        public double Theta => Math.Atan(Math.Sqrt(Gx * Gx + Gy * Gy));

        /// <summary>Tilt azimuth φ = atan2(Gy, Gx), in radians (the inspector's <c>Phi</c>).</summary>
        public double Phi => Math.Atan2(Gy, Gx);

        /// <summary>
        /// Predicted recovered tilt effect = <c>|Gx|·halfW + |Gy|·halfH</c>, in µm. Matches the inspector's
        /// <c>TiltEffectMicrons</c> (half the corner focus swing); equals the injected TiltAmountMicrons.
        /// </summary>
        public double PredictedTiltEffectMicrons { get; }

        /// <summary>
        /// Predicted recovered curvature effect = <c>K·(halfW² + halfH²)</c>, in µm. Matches the inspector's
        /// <c>CurvatureEffectMicrons</c> (corner-vs-center); equals the injected BackfocusErrorMicrons.
        /// </summary>
        public double PredictedCurvatureEffectMicrons { get; }

        /// <summary>
        /// Plain-number constructor (unit-test friendly). Inverts the aberration knobs onto (Gx, Gy, K). When
        /// <paramref name="aberrationsEnabled"/> is false the surface is flat (Gx=Gy=K=0, X0=Y0=0) and every
        /// field point sees the uniform defocus k·(currentSteps − x0).
        /// </summary>
        /// <param name="aberrationsEnabled">When false, the surface is flat regardless of the other knobs.</param>
        /// <param name="tiltAngleDegrees">Tilt azimuth φ (deg), the inspector's Phi.</param>
        /// <param name="tiltAmountMicrons">Center → corner focus swing (µm), the inspector's TiltEffectMicrons.</param>
        /// <param name="backfocusErrorMicrons">Corner-vs-center curvature offset (µm), the inspector's CurvatureEffectMicrons.</param>
        /// <param name="opticalAxisOffsetXMicrons">Optical-axis X offset X0 (sensor µm).</param>
        /// <param name="opticalAxisOffsetYMicrons">Optical-axis Y offset Y0 (sensor µm).</param>
        /// <param name="widthPx">Sensor width, in pixels.</param>
        /// <param name="heightPx">Sensor height, in pixels.</param>
        /// <param name="pixelSizeMicrons">Pixel pitch p, in µm.</param>
        /// <param name="focuserStepSizeMicrons">Focuser step size k, in µm of sensor defocus per step.</param>
        /// <param name="optimalFocuserPosition">Best-focus focuser step position x0.</param>
        public AberrationSurface(
            bool aberrationsEnabled,
            double tiltAngleDegrees,
            double tiltAmountMicrons,
            double backfocusErrorMicrons,
            double opticalAxisOffsetXMicrons,
            double opticalAxisOffsetYMicrons,
            int widthPx,
            int heightPx,
            double pixelSizeMicrons,
            double focuserStepSizeMicrons,
            int optimalFocuserPosition) {
            if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            if (heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(heightPx));
            if (pixelSizeMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSizeMicrons));
            if (focuserStepSizeMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(focuserStepSizeMicrons));
            // Tilt amount is a non-negative magnitude; direction is carried by the azimuth. A negative value
            // would silently flip Phi by 180° (|G| = amount/den < 0), breaking the inject⇄recover identity.
            if (tiltAmountMicrons < 0) throw new ArgumentOutOfRangeException(nameof(tiltAmountMicrons), "Tilt amount is a non-negative magnitude; direction is given by the azimuth angle.");

            this.widthPx = widthPx;
            this.heightPx = heightPx;
            this.pixelSizeMicrons = pixelSizeMicrons;
            this.focuserStepSizeMicrons = focuserStepSizeMicrons;
            this.optimalFocuserPosition = optimalFocuserPosition;

            halfWidthMicrons = widthPx * pixelSizeMicrons / 2.0;
            halfHeightMicrons = heightPx * pixelSizeMicrons / 2.0;

            // Center best-focus in µm of focuser travel — always the configured optimal position.
            Z0 = optimalFocuserPosition * focuserStepSizeMicrons;

            if (!aberrationsEnabled) {
                X0 = 0.0;
                Y0 = 0.0;
                Gx = 0.0;
                Gy = 0.0;
                K = 0.0;
            } else {
                X0 = opticalAxisOffsetXMicrons;
                Y0 = opticalAxisOffsetYMicrons;

                // Tilt: choose |G| so that |Gx|·halfW + |Gy|·halfH == TiltAmountMicrons, at azimuth φ.
                var phi = tiltAngleDegrees * Math.PI / 180.0;
                var cosPhi = Math.Cos(phi);
                var sinPhi = Math.Sin(phi);
                var den = Math.Abs(cosPhi) * halfWidthMicrons + Math.Abs(sinPhi) * halfHeightMicrons;
                var gMagnitude = (tiltAmountMicrons == 0.0 || den == 0.0) ? 0.0 : tiltAmountMicrons / den;
                Gx = gMagnitude * cosPhi;
                Gy = gMagnitude * sinPhi;

                // Curvature (isotropic): K·(halfW²+halfH²) == BackfocusErrorMicrons.
                var r2 = halfWidthMicrons * halfWidthMicrons + halfHeightMicrons * halfHeightMicrons;
                K = r2 > 0.0 ? backfocusErrorMicrons / r2 : 0.0;
            }

            PredictedTiltEffectMicrons = Math.Abs(Gx) * halfWidthMicrons + Math.Abs(Gy) * halfHeightMicrons;
            PredictedCurvatureEffectMicrons = K * (halfWidthMicrons * halfWidthMicrons + halfHeightMicrons * halfHeightMicrons);
        }

        /// <summary>
        /// Best-focus surface height at a centered sensor position, in µm of focuser travel:
        /// <c>Gx·(x−X0) + Gy·(y−Y0) + K·((x−X0)² + (y−Y0)²) + Z0</c>.
        /// </summary>
        private double ZBestFocusMicrons(double xMicrons, double yMicrons) {
            var xPrime = xMicrons - X0;
            var yPrime = yMicrons - Y0;
            var tilt = Gx * xPrime + Gy * yPrime;
            var curvature = K * (xPrime * xPrime + yPrime * yPrime);
            return tilt + curvature + Z0;
        }

        /// <summary>
        /// Local sensor defocus Δ (µm) seen by a star at pixel (px, py) with the focuser at
        /// <paramref name="currentFocuserSteps"/>. The pixel is centered to microns
        /// (x=(px−W/2)·p, y=(py−H/2)·p), then Δ = currentSteps·k − zBestFocus(x, y). Feed this to
        /// <see cref="DefocusModel"/> for HFR / W20 / donut sizing.
        /// </summary>
        public double LocalDefocusMicrons(int px, int py, int currentFocuserSteps) {
            var x = (px - widthPx / 2.0) * pixelSizeMicrons;
            var y = (py - heightPx / 2.0) * pixelSizeMicrons;
            var currentFocuserMicrons = currentFocuserSteps * focuserStepSizeMicrons;
            return currentFocuserMicrons - ZBestFocusMicrons(x, y);
        }

        /// <summary>
        /// Builds the surface from a render request and its resolved sensor. When aberrations are disabled the
        /// surface is flat and every field point gets the uniform defocus k·(currentSteps − x0).
        /// </summary>
        public static AberrationSurface FromRequest(RenderRequest request, SensorDefinition sensor) {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (sensor == null) throw new ArgumentNullException(nameof(sensor));

            return new AberrationSurface(
                aberrationsEnabled: request.AberrationsEnabled,
                tiltAngleDegrees: request.TiltAngleDegrees,
                tiltAmountMicrons: request.TiltAmountMicrons,
                backfocusErrorMicrons: request.BackfocusErrorMicrons,
                opticalAxisOffsetXMicrons: request.OpticalAxisOffsetXMicrons,
                opticalAxisOffsetYMicrons: request.OpticalAxisOffsetYMicrons,
                widthPx: sensor.Width,
                heightPx: sensor.Height,
                pixelSizeMicrons: sensor.PixelSizeMicrons,
                focuserStepSizeMicrons: request.FocuserStepSizeMicrons,
                optimalFocuserPosition: request.OptimalFocuserPosition);
        }
    }
}
