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
    /// a₂      = K/2 + a_c/r_c²                  the astigmatism coefficient, 1/µm
    /// A(x, y) = a₂ · r'²                        the T–S half-split, µm
    /// z_T = zBestFocus + A     z_S = zBestFocus − A
    /// </code>
    /// so the per-star defocus becomes a pair, <c>Δ_T = Δ − A</c> and <c>Δ_S = Δ + A</c>, and the blur is an
    /// ellipse whose radial semi-axis is set by <c>Δ_T</c> and tangential semi-axis by <c>Δ_S</c> (the
    /// tangential-ray defocus drives the radial extent — that crossing is what produces the 90° flip).</para>
    ///
    /// <para><b>There is no tilt term, and that is the point.</b> <c>Gx</c>/<c>Gy</c> appear in the mean
    /// surface and nowhere else. A sensor is a passive sampling plane: tilting it changes which plane of the
    /// converging beam is sampled, not the beam's aberration content, so it cannot manufacture astigmatism.
    /// What makes a tilted rig show eccentric stars is that tilt drives <c>Δ</c> positive on one edge and
    /// negative on the other against a split that is the same on both — so the two edges land on opposite
    /// sides of the astigmatic pair and elongate perpendicular to each other. An earlier version of this
    /// class drove the split from the local spacing error, which tilt moves; that makes <c>A</c> flip in
    /// lockstep with <c>Δ</c>, leaving <c>Δ·A &lt; 0</c> everywhere and every edge elongated the same way.
    /// It rendered pure tilt as plain defocus. Do not reintroduce it.</para>
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
    /// <para><b>Why the induced term is exactly K/2.</b> For a Seidel system the tangential surface departs
    /// from the Petzval surface three times as far as the sagittal one, so the medial surface a focus run
    /// finds moves at <c>2s·r²</c> while the half-split moves at <c>s·r²</c>. Petzval curvature depends only
    /// on element powers and indices, not separations, so a spacing change lands entirely in the astigmatism
    /// term: a mis-spacing that shifts the medial surface by <c>K·r'²</c> splits the pair by exactly half
    /// that. It is fixed by the optics, not a tunable fraction.</para>
    ///
    /// <para><b>What the spacing sign does.</b> Reversing a spacer flips <c>Δ</c> and flips the <c>K/2</c>
    /// half of the split with it, but cannot touch the corrector's residual <c>a_c</c>. So the familiar
    /// "swap a spacer and the corners rotate 90°" holds only while the residual still outvotes the induced
    /// term — <c>|BackfocusErrorMicrons| &lt; 2·a_c</c> — and past that both spacing directions read radial.
    /// That window is the honest form of a widely repeated piece of field lore, and it is why published
    /// reports of the flip and flat denials of it can both be true.</para>
    ///
    /// It supplies the per-field-point defocus Δ (and, with astigmatism, the pair); <see cref="DefocusModel"/>
    /// turns a defocus into HFR / W20 / donut radii. Full derivation:
    /// <c>docs/camera-simulator-astigmatism-design.md</c>.
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
        /// <c>a₂</c>, the astigmatism coefficient (1/µm): the T–S half-split per µm² of field radius, so
        /// <c>A = a₂·r'²</c>. Literally <c>0.0</c> when astigmatism or aberrations are disabled, so the
        /// disabled path is bit-exact rather than merely small.
        ///
        /// <para><c>a₂ = K/2 + a_c/r_c²</c>. The first term is the spacing-induced split, which Seidel pins
        /// to exactly <b>half</b> the induced mean curvature — the tangential surface sits three times as far
        /// from the Petzval surface as the sagittal one, so the medial surface moves 2s·r² while the half
        /// split moves s·r². It is not a free ratio. The second term is the corrector's own residual, which
        /// is what survives at perfect spacing and what tilt reveals.</para>
        /// </summary>
        public double AstigmatismCoefficient { get; }

        /// <summary>
        /// Predicted T–S half-split at the sensor corner, <c>a₂·(halfW² + halfH²)</c> µm — i.e.
        /// <c>BackfocusErrorMicrons/2 + CornerAstigmatismMicrons</c>.
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
        /// <param name="cornerAstigmatismMicrons">The corrector's design-residual T–S half-split at the sensor corner (µm, signed).</param>
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
            double cornerAstigmatismMicrons = 0.0) {
            if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            if (heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(heightPx));
            if (pixelSizeMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSizeMicrons));
            if (focuserStepSizeMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(focuserStepSizeMicrons));
            // Tilt amount is a non-negative magnitude; direction is carried by the azimuth. A negative value
            // would silently flip Phi by 180° (|G| = amount/den < 0), breaking the inject⇄recover identity.
            if (tiltAmountMicrons < 0) throw new ArgumentOutOfRangeException(nameof(tiltAmountMicrons), "Tilt amount is a non-negative magnitude; direction is given by the azimuth angle.");
            // Signed, but it must be finite: NaN fails every ordering comparison and an infinity would sail
            // through a naive `< 0` guard, and either renders an all-NaN frame with no error anywhere.
            if (!double.IsFinite(cornerAstigmatismMicrons)) throw new ArgumentOutOfRangeException(nameof(cornerAstigmatismMicrons), cornerAstigmatismMicrons, "Corner astigmatism must be a finite number.");

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
                AstigmatismCoefficient = 0.0;
                PredictedAstigmatismEffectMicrons = 0.0;
            } else {
                // Spacing-induced half-split is exactly K/2 (Seidel 3:1), plus the corrector's own residual
                // expressed at the corner. NO tilt term: a tilted sensor cannot change the beam's aberrations,
                // only where along each beam it samples -- so tilt belongs solely to the mean surface above.
                AstigmatismCoefficient = 0.5 * K + (cornerRadiusSquared > 0.0 ? cornerAstigmatismMicrons / cornerRadiusSquared : 0.0);
                PredictedAstigmatismEffectMicrons = AstigmatismCoefficient * cornerRadiusSquared;
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
        /// The astigmatism half-split <c>A(x,y) = a₂·r'²</c>, in µm of focuser travel. Exactly 0 when
        /// astigmatism is disabled, and exactly 0 on the optical axis whatever the configuration.
        ///
        /// <para><b>Rotationally symmetric, and independent of tilt.</b> A tilted sensor cannot change the
        /// astigmatism anywhere: the wavefront converging on a field point is fixed by the telescope and
        /// corrector, and the sensor only chooses where along that beam it samples. Mis-spacing induces
        /// astigmatism because it forces a compensating refocus that moves the corrector relative to the
        /// telescope's image, changing its working conjugates; a tilted sensor at correct mean spacing needs
        /// no such refocus. Schechter &amp; Levinson (2011) put it directly — a tilted detector produces a
        /// field pattern identical to misalignment curvature of field, i.e. pure defocus, with no
        /// astigmatism term at third order.</para>
        ///
        /// <para><b>So how does a tilted rig show eccentric corners?</b> Through the residual the optic
        /// already has. Every real corrector leaves some T–S split at the field edge even at design spacing
        /// — invisible at best focus, because the circle of least confusion there is round and small, and
        /// <i>revealed</i> by defocus. Tilt is a defocus injector: it drags each corner to a different Δ, and
        /// the semi-axes <c>|Δ−A|</c> and <c>|Δ+A|</c> separate. The corner where Δ and A disagree in sign
        /// elongates radially, the opposite corner tangentially, with a high-eccentricity band where
        /// <c>|Δ| ≈ A</c>. That is the tilt signature, and it needs no tilt term here to produce it — only a
        /// non-zero <c>CornerAstigmatismMicrons</c>.</para>
        ///
        /// <para>An earlier version of this model routed tilt into A through a "local spacing error", on the
        /// reasoning that a tilted sensor is mis-spaced across its area. That is not how it works, and it
        /// could not produce the effect anyway: it made A flip sign across the field in lockstep with Δ, so
        /// <c>Δ·A &lt; 0</c> everywhere and every corner elongated radially. The measured axis ratio was 1.07
        /// regardless of tilt magnitude.</para>
        /// </summary>
        public double AstigmatismSplitMicrons(int px, int py) {
            if (AstigmatismCoefficient == 0.0) {
                return 0.0;
            }
            ToCenteredMicrons(px, py, out var x, out var y);
            var xPrime = x - X0;
            var yPrime = y - Y0;
            return AstigmatismCoefficient * (xPrime * xPrime + yPrime * yPrime);
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
                cornerAstigmatismMicrons: request.CornerAstigmatismMicrons);
        }
    }
}
