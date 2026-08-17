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
    ///
    /// <para><b>Astigmatism.</b> With <c>astigmatismEnabled</c> this is no longer one surface but a
    /// <i>pair</i> — the tangential and sagittal focal surfaces — straddling the surface above, which remains
    /// their mean:
    /// <code>
    /// e(x, y) = e_c + Gx·(x−X0) + Gy·(y−Y0)      local axial spacing error, µm
    /// A(x, y) = ρ · c_m · e(x, y) · r'²          the T–S half-split, µm
    /// z_T = zBestFocus + A     z_S = zBestFocus − A
    /// </code>
    /// so the per-star defocus becomes a pair, <c>Δ_T = Δ − A</c> and <c>Δ_S = Δ + A</c>, and the blur is an
    /// ellipse whose radial semi-axis is set by <c>Δ_T</c> and tangential semi-axis by <c>Δ_S</c> (the
    /// tangential-ray defocus drives the radial extent — that crossing is what produces the 90° flip).</para>
    ///
    /// <para><b>Why the mean is preserved, and why it matters.</b> At a fixed field point <c>A</c> does not
    /// depend on the focuser, so <c>Δ → −Δ</c> maps the semi-axis pair <c>(|Δ−A|, |Δ+A|)</c> to
    /// <c>(|Δ+A|, |Δ−A|)</c> — the PSF at <c>−Δ</c> is the PSF at <c>+Δ</c> <b>rotated by exactly 90°</b>.
    /// Every rotation-invariant statistic is therefore unchanged: HFR, flux-weighted moments, and the
    /// detector's square bounding-box HFR. So HFR(Δ) stays exactly even about the same per-star best focus,
    /// the HFR² parabola vertex does not move, and the inspector recovers the same (Gx, Gy, K, Z0) it always
    /// did. <see cref="LocalDefocusMicrons"/> deliberately still returns only the mean, so its "this is the
    /// inspector's algebraic inverse" contract stays literally true.</para>
    ///
    /// <para><b>This is first-order theory, exactly.</b> Substituting <c>Δ = −c_m·Δb·r'²</c> and
    /// <c>A = c_a·Δb·r'²</c> gives semi-axes <c>|(c_m + c_a)·Δb·r'²|</c> and <c>|(c_m − c_a)·Δb·r'²|</c> —
    /// the tangential and sagittal focal surfaces themselves, with no approximation. Two consequences follow
    /// and are worth stating, because both surprise people:</para>
    /// <list type="bullet">
    /// <item>The axis ratio is <c>|1 + ρ| / |1 − ρ|</c>, with <b>no Δb in it</b>. Whether stars elongate
    /// radially or tangentially is a property of the <b>corrector</b> — the sign of <c>ρ = c_a/c_m</c> — and
    /// does not change with the sign or size of the spacing error. Hence <c>ρ</c> is signed, and a negative
    /// value models a corrector whose astigmatism opposes its field curvature.</item>
    /// <item>Reversing the spacing error therefore renders an <b>identical</b> frame: it flips both Δ and A,
    /// and the semi-axes are invariant under that pair of flips. Too-much and too-little backfocus are not
    /// distinguishable from star shapes in a single frame — you tell them apart by refocusing, because the
    /// corners come to focus on opposite sides of the centre. That is the optics, not a modelling shortcut.</item>
    /// </list>
    ///
    /// It supplies the per-field-point defocus Δ (and, with astigmatism, the pair); <see cref="DefocusModel"/>
    /// turns a defocus into HFR / W20 / donut radii. Full derivation:
    /// <c>docs/camera-simulator-astigmatism-design.md</c>.
    /// </summary>
    public sealed class AberrationSurface {

        /// <summary>
        /// The nominal corrector's residual field curvature per µm of axial spacing error, per µm² of field
        /// radius (1/µm²). Used as <c>c_m</c> whenever it cannot be derived from the user's own two numbers.
        ///
        /// <para>Pinned to a plausible flattener: <b>1 mm of spacing error produces 50 µm of corner curvature
        /// effect on a full-frame corner</b> (R_c = 21.63 mm for 36 × 24 mm), so
        /// <c>c_m0 = 50 / (1000 · 21633²) = 1.0684e-10</c>. It multiplies r'², so it scales correctly to
        /// smaller sensors — the same spacing error produces less corner curvature on a smaller chip.</para>
        /// </summary>
        public const double NominalCurvaturePerSpacingPerAreaMicrons = 1.0684e-10;

        /// <summary>Sentinel for "the spacing error was not entered"; it is then inferred from K and c_m0.</summary>
        public const double UnsetSpacingErrorMicrons = -1.0;

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
        /// The axial spacing error at the field centre, <c>e_c</c>, in µm — how far the sensor sits from the
        /// corrector's design spacing. <b>Signed</b>: it shares the sign of <see cref="K"/>, because a
        /// corrector's curvature response to spacing has a fixed sign. The option carries only the magnitude
        /// (its negative values are the "not entered" sentinel), so the direction is inherited here.
        ///
        /// <para>When the option is unset this is inferred as <c>K / c_m0</c>, which by construction makes
        /// <see cref="CurvaturePerSpacing"/> come out exactly
        /// <see cref="NominalCurvaturePerSpacingPerAreaMicrons"/>.</para>
        /// </summary>
        public double EffectiveSpacingErrorMicrons { get; }

        /// <summary>
        /// <c>c_m</c>: residual field curvature per µm of spacing error (1/µm²). Derived as <c>K / e_c</c> from
        /// the user's own two numbers when <b>both</b> are nonzero — that is their empirical calibration of the
        /// corrector — and otherwise the nominal constant, since a zero leaves nothing to derive from. Always
        /// positive: <c>e_c</c> carries K's sign, so the quotient does not.
        /// </summary>
        public double CurvaturePerSpacing { get; }

        /// <summary>
        /// <c>c_a = ρ·c_m</c>, the astigmatism coefficient (1/µm²): the T–S half-split per µm of local spacing
        /// error per µm² of field radius. Literally <c>0.0</c> when astigmatism or aberrations are disabled, so
        /// the disabled path is bit-exact rather than merely small.
        /// </summary>
        public double AstigmatismCoefficient { get; }

        /// <summary>
        /// Predicted T–S half-split at the sensor corner from the centre spacing error alone (tilt excluded),
        /// <c>ρ·c_m·e_c·(halfW² + halfH²)</c> µm. Whenever <c>c_m</c> is derived rather than nominal this
        /// reduces to <c>ρ · PredictedCurvatureEffectMicrons</c>.
        /// </summary>
        public double PredictedAstigmatismEffectMicrons { get; }

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
        /// <param name="astigmatismEnabled">When false the surface stays a single surface (A ≡ 0 exactly).</param>
        /// <param name="backfocusSpacingErrorMicrons">Axial spacing error magnitude e_c (µm); negative = unset, inferred from K.</param>
        /// <param name="astigmatismRatio">ρ = c_a/c_m, the astigmatism-to-curvature ratio. Non-negative.</param>
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
            int optimalFocuserPosition,
            bool astigmatismEnabled = false,
            double backfocusSpacingErrorMicrons = UnsetSpacingErrorMicrons,
            double astigmatismRatio = 0.0) {
            if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            if (heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(heightPx));
            if (pixelSizeMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSizeMicrons));
            if (focuserStepSizeMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(focuserStepSizeMicrons));
            // Tilt amount is a non-negative magnitude; direction is carried by the azimuth. A negative value
            // would silently flip Phi by 180° (|G| = amount/den < 0), breaking the inject⇄recover identity.
            if (tiltAmountMicrons < 0) throw new ArgumentOutOfRangeException(nameof(tiltAmountMicrons), "Tilt amount is a non-negative magnitude; direction is given by the azimuth angle.");
            // The ratio is SIGNED — its sign is what selects radial versus tangential elongation, and that is a
            // property of the corrector rather than of the spacing error (see the class remarks). Only NaN is
            // rejected, and `!(x > double.MinValue)` catches it where `x < 0` would wave it through into an
            // all-NaN frame with no error anywhere. Same guard style as DefocusModel's.
            if (!(astigmatismRatio > double.MinValue)) throw new ArgumentOutOfRangeException(nameof(astigmatismRatio), astigmatismRatio, "Astigmatism ratio must be a number.");

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
            var cornerRadiusSquared = halfWidthMicrons * halfWidthMicrons + halfHeightMicrons * halfHeightMicrons;
            PredictedCurvatureEffectMicrons = K * cornerRadiusSquared;

            if (!aberrationsEnabled || !astigmatismEnabled) {
                // Literally zero, not merely small: the whole "astigmatism off renders byte-identically"
                // guarantee rests on A being exactly 0.0 so every star collapses onto one quantized level.
                EffectiveSpacingErrorMicrons = 0.0;
                CurvaturePerSpacing = NominalCurvaturePerSpacingPerAreaMicrons;
                AstigmatismCoefficient = 0.0;
                PredictedAstigmatismEffectMicrons = 0.0;
            } else {
                // The option carries a magnitude (negative means "not entered"), so the direction is inherited
                // from K — a corrector's curvature response to spacing has one fixed sign, so e_c and K share
                // theirs. Unset infers e_c = K / c_m0, which makes CurvaturePerSpacing come out exactly c_m0.
                EffectiveSpacingErrorMicrons = backfocusSpacingErrorMicrons >= 0.0
                    ? (K < 0.0 ? -backfocusSpacingErrorMicrons : backfocusSpacingErrorMicrons)
                    : K / NominalCurvaturePerSpacingPerAreaMicrons;

                // Derive c_m from the user's own two numbers only when both are nonzero. A zero on either side
                // leaves nothing to derive from, and falling back to the nominal constant is what keeps pure
                // tilt (K = 0) producing astigmatism instead of silently producing none.
                CurvaturePerSpacing = (K != 0.0 && EffectiveSpacingErrorMicrons != 0.0)
                    ? K / EffectiveSpacingErrorMicrons
                    : NominalCurvaturePerSpacingPerAreaMicrons;

                AstigmatismCoefficient = astigmatismRatio * CurvaturePerSpacing;
                PredictedAstigmatismEffectMicrons = AstigmatismCoefficient * EffectiveSpacingErrorMicrons * cornerRadiusSquared;
            }
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

        /// <summary>Centers a pixel coordinate to sensor microns: x=(px−W/2)·p, y=(py−H/2)·p.</summary>
        private void ToCenteredMicrons(int px, int py, out double xMicrons, out double yMicrons) {
            xMicrons = (px - widthPx / 2.0) * pixelSizeMicrons;
            yMicrons = (py - heightPx / 2.0) * pixelSizeMicrons;
        }

        /// <summary>
        /// Local sensor defocus Δ (µm) seen by a star at pixel (px, py) with the focuser at
        /// <paramref name="currentFocuserSteps"/>. The pixel is centered to microns
        /// (x=(px−W/2)·p, y=(py−H/2)·p), then Δ = currentSteps·k − zBestFocus(x, y). Feed this to
        /// <see cref="DefocusModel"/> for HFR / W20 / donut sizing.
        ///
        /// <para>This is the <b>mean</b> of the tangential and sagittal defocuses even when astigmatism is on —
        /// deliberately, so it remains exactly the inspector's algebraic inverse. Callers that need the
        /// astigmatic pair use <see cref="AstigmaticDefocusMicrons"/>.</para>
        /// </summary>
        public double LocalDefocusMicrons(int px, int py, int currentFocuserSteps) {
            ToCenteredMicrons(px, py, out var x, out var y);
            var currentFocuserMicrons = currentFocuserSteps * focuserStepSizeMicrons;
            return currentFocuserMicrons - ZBestFocusMicrons(x, y);
        }

        /// <summary>
        /// The <b>local</b> axial spacing error e(x,y) = e_c + Gx·x' + Gy·y', in µm. The tilt plane term is
        /// literally how far that patch of sensor has moved along the optical axis, which is why a tilted
        /// sensor is mis-spaced over most of its area even when its mean spacing is perfect.
        /// </summary>
        public double LocalSpacingErrorMicrons(int px, int py) {
            ToCenteredMicrons(px, py, out var x, out var y);
            return EffectiveSpacingErrorMicrons + Gx * (x - X0) + Gy * (y - Y0);
        }

        /// <summary>
        /// The astigmatism half-split A(x,y) = c_a · e(x,y) · r'², in µm of focuser travel. Exactly 0 when
        /// astigmatism is disabled, and exactly 0 on the optical axis whatever the configuration.
        /// </summary>
        public double AstigmatismSplitMicrons(int px, int py) {
            if (AstigmatismCoefficient == 0.0) {
                return 0.0;
            }
            ToCenteredMicrons(px, py, out var x, out var y);
            var xPrime = x - X0;
            var yPrime = y - Y0;
            return AstigmatismCoefficient * LocalSpacingErrorMicrons(px, py) * (xPrime * xPrime + yPrime * yPrime);
        }

        /// <summary>
        /// Field position angle θ = atan2(y', x'), in radians, measured about the <b>optical axis</b> (X0, Y0)
        /// rather than the sensor centre — the ellipse's principal axes are radial/tangential with respect to
        /// the optical axis, not the chip. Returns 0 exactly on the axis, where the kernel is circular anyway.
        /// </summary>
        public double FieldAngleRadians(int px, int py) {
            ToCenteredMicrons(px, py, out var x, out var y);
            var xPrime = x - X0;
            var yPrime = y - Y0;
            return (xPrime == 0.0 && yPrime == 0.0) ? 0.0 : Math.Atan2(yPrime, xPrime);
        }

        /// <summary>
        /// The astigmatic defocus pair and orientation for a star at pixel (px, py):
        /// <c>Δ_T = Δ − A</c> (tangential rays, which set the <b>radial</b> semi-axis) and
        /// <c>Δ_S = Δ + A</c> (sagittal rays, which set the <b>tangential</b> semi-axis), plus the field angle.
        /// With astigmatism off both outputs equal <see cref="LocalDefocusMicrons"/> exactly.
        /// </summary>
        public void AstigmaticDefocusMicrons(
                int px, int py, int currentFocuserSteps,
                out double tangentialMicrons, out double sagittalMicrons, out double thetaRadians) {
            var delta = LocalDefocusMicrons(px, py, currentFocuserSteps);
            var split = AstigmatismSplitMicrons(px, py);
            tangentialMicrons = delta - split;
            sagittalMicrons = delta + split;
            thetaRadians = split == 0.0 ? 0.0 : FieldAngleRadians(px, py);
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
                optimalFocuserPosition: request.OptimalFocuserPosition,
                astigmatismEnabled: request.AstigmatismEnabled,
                backfocusSpacingErrorMicrons: request.BackfocusSpacingErrorMicrons,
                astigmatismRatio: request.AstigmatismRatio);
        }
    }
}
