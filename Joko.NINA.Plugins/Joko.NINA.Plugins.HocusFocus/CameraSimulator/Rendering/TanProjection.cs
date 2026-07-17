#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Gnomonic (TAN) tangent-plane projection between J2000 equatorial coordinates and sensor pixels.
    /// The field center projects to the image center (width/2, height/2). Owns its own math — it does
    /// NOT bind NINA's internal projection classes.
    ///
    /// Conventions:
    /// <list type="bullet">
    /// <item>Standard tangent-plane coordinates ξ (East, +RA) and η (North, +Dec) in radians.</item>
    /// <item>Pixel x increases to the right, y increases downward (image-row order); North (+η) is up, so
    /// the y-axis is flipped relative to η. At rotation 0, increasing RA (East) moves a star toward +x and
    /// increasing Dec (North) toward −y.</item>
    /// <item>Rotation is applied in the tangent plane about the image center; positive
    /// <c>rotationDegrees</c> rotates the (East, North) axes counter-clockwise into the (x, up) axes.</item>
    /// </list>
    /// The forward/inverse pair are exact analytic inverses, so a project→deproject→project round-trip is
    /// limited only by floating-point precision.
    /// </summary>
    public sealed class TanProjection {
        private readonly double centerRaRad;
        private readonly double centerDecRad;
        private readonly double sinDec0;
        private readonly double cosDec0;
        private readonly double cosRho;
        private readonly double sinRho;
        private readonly double radiansPerPixel;
        private readonly double centerX;
        private readonly double centerY;

        /// <param name="centerRaDeg">Field-center right ascension (J2000, degrees).</param>
        /// <param name="centerDecDeg">Field-center declination (J2000, degrees).</param>
        /// <param name="focalLengthMm">Telescope focal length (mm).</param>
        /// <param name="pixelSizeMicrons">Sensor pixel pitch (µm).</param>
        /// <param name="rotationDegrees">Field rotation about the image center (degrees).</param>
        /// <param name="width">Image width (px).</param>
        /// <param name="height">Image height (px).</param>
        public TanProjection(
            double centerRaDeg,
            double centerDecDeg,
            double focalLengthMm,
            double pixelSizeMicrons,
            double rotationDegrees,
            int width,
            int height) {
            // `!(x > 0)` and not `x <= 0`: NaN fails BOTH, so the latter would wave NaN through into the plate
            // scale and yield an all-NaN projection with no error anywhere. focalLengthMm arrives from the same
            // request.FocalLengthMillimeters that DefocusModel and RadiometryCalculator guard this way, so the
            // three must agree — a lone `<= 0` here would read as a deliberate claim that this input is known
            // NaN-free, which nothing establishes. width/height are ints and cannot be NaN.
            if (!(focalLengthMm > 0)) throw new ArgumentOutOfRangeException(nameof(focalLengthMm), focalLengthMm, "must be a positive number");
            if (!(pixelSizeMicrons > 0)) throw new ArgumentOutOfRangeException(nameof(pixelSizeMicrons), pixelSizeMicrons, "must be a positive number");
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            centerRaRad = DegToRad(centerRaDeg);
            centerDecRad = DegToRad(centerDecDeg);
            sinDec0 = Math.Sin(centerDecRad);
            cosDec0 = Math.Cos(centerDecRad);

            var rho = DegToRad(rotationDegrees);
            cosRho = Math.Cos(rho);
            sinRho = Math.Sin(rho);

            // radians/px = pixelµm / (1000 · focalMm); arcsec/px is derived from it, so 206.265 appears once.
            radiansPerPixel = pixelSizeMicrons / (1000.0 * focalLengthMm);
            ArcsecPerPixel = radiansPerPixel * AstronomicalConstants.ArcsecPerRadian;

            Width = width;
            Height = height;
            centerX = width / 2.0;
            centerY = height / 2.0;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Plate scale in arcseconds per pixel.</summary>
        public double ArcsecPerPixel { get; }

        /// <summary>Plate scale in radians per pixel.</summary>
        public double RadiansPerPixel => radiansPerPixel;

        /// <summary>
        /// Forward project a J2000 star (degrees) to pixel coordinates. Returns the pixel position even if
        /// it falls off the frame; use <see cref="TryProject"/> for framing/rejection. Returns
        /// <c>(NaN, NaN)</c> when the star is 90° or more from the field center (on or behind the tangent
        /// hemisphere), where the gnomonic projection is undefined, rather than a plausible-but-wrong pixel.
        /// </summary>
        public (double X, double Y) Project(double raDeg, double decDeg) {
            var raRad = DegToRad(raDeg);
            var decRad = DegToRad(decDeg);

            var sinDec = Math.Sin(decRad);
            var cosDec = Math.Cos(decRad);
            var deltaRa = raRad - centerRaRad;
            var cosDeltaRa = Math.Cos(deltaRa);
            var sinDeltaRa = Math.Sin(deltaRa);

            // Cosine of the angular distance from the tangent point. It is ≤ 0 when the star is 90° or more
            // from the field center — on or behind the tangent hemisphere — where the gnomonic projection is
            // undefined (denom == 0 → infinity) or silently mirrors a far star onto a finite wrong pixel
            // (denom < 0). Return NaN so callers (and TryProject) reject it instead of trusting a bad pixel.
            var denom = sinDec * sinDec0 + cosDec * cosDec0 * cosDeltaRa;
            if (denom <= 0.0) {
                return (double.NaN, double.NaN);
            }

            // Standard coordinates (radians): ξ East (+RA), η North (+Dec).
            var xi = cosDec * sinDeltaRa / denom;
            var eta = (sinDec * cosDec0 - cosDec * sinDec0 * cosDeltaRa) / denom;

            // Radians → pixels, then rotate and offset to image center (y flipped: North is up).
            var u = xi / radiansPerPixel;
            var v = eta / radiansPerPixel;
            var uRot = u * cosRho - v * sinRho;
            var vRot = u * sinRho + v * cosRho;

            var x = centerX + uRot;
            var y = centerY - vRot;
            return (x, y);
        }

        /// <summary>
        /// Forward project with off-frame rejection. Returns false (and the projected pixel via the out
        /// params for diagnostics) when the pixel is outside
        /// [−margin, width+margin] × [−margin, height+margin], or when the star is 90° or more from the
        /// field center (<see cref="Project"/> yields NaN). <paramref name="psfMarginPx"/> lets callers
        /// keep stars whose PSF wings spill onto the frame.
        /// </summary>
        public bool TryProject(double raDeg, double decDeg, out double x, out double y, double psfMarginPx = 0.0) {
            (x, y) = Project(raDeg, decDeg);
            if (double.IsNaN(x) || double.IsNaN(y)) {
                return false;
            }
            var margin = Math.Max(0.0, psfMarginPx);
            return x >= -margin && x <= Width + margin
                && y >= -margin && y <= Height + margin;
        }

        /// <summary>
        /// Inverse project a pixel back to J2000 coordinates (degrees). RA is normalized to [0, 360).
        /// </summary>
        public (double RaDeg, double DecDeg) Deproject(double x, double y) {
            var uRot = x - centerX;
            var vRot = centerY - y; // undo the y flip

            // Un-rotate (inverse rotation), then pixels → radians.
            var u = uRot * cosRho + vRot * sinRho;
            var v = -uRot * sinRho + vRot * cosRho;
            var xi = u * radiansPerPixel;
            var eta = v * radiansPerPixel;

            // Standard-coordinate deprojection (gnomonic inverse).
            var denom = cosDec0 - eta * sinDec0;
            var deltaRa = Math.Atan2(xi, denom);
            var raRad = centerRaRad + deltaRa;
            var decRad = Math.Atan((sinDec0 + eta * cosDec0) * Math.Cos(deltaRa) / denom);

            var raDeg = NormalizeRaDeg(RadToDeg(raRad));
            var decDeg = RadToDeg(decRad);
            return (raDeg, decDeg);
        }

        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

        private static double NormalizeRaDeg(double raDeg) {
            var r = raDeg % 360.0;
            if (r < 0) {
                r += 360.0;
            }
            return r;
        }
    }
}
