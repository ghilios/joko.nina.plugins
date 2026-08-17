#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter {

    /// <summary>
    /// The single, sign-critical implementation of folding a virtual-adapter <see cref="AberrationDelta"/> into
    /// the camera simulator's persisted injected aberration. Extracted verbatim from
    /// <see cref="SimulatedTiltAdapterVM.ApplyDelta"/> so that BOTH ways of driving the virtual adapter -- the
    /// manual dockable panel (<see cref="SimulatedTiltAdapterVM"/>) and the automated
    /// <c>SimulatedEatTransport</c>/<c>SimulatedTiltActuator</c> path -- run the exact same gradient-compose +
    /// piston arithmetic. The closed calibration loop only converges if the injection here is the exact inverse
    /// of the inspector's guidance; keeping ONE implementation (rather than two that could silently drift) is
    /// what guarantees that. Pure: reads/writes only <see cref="ICameraSimulatorOptions"/>.
    /// </summary>
    public static class SimulatedTiltInjection {

        /// <summary>The persisted bounds of the aberration boxes (Resources/OptionsDataTemplates.xaml).</summary>
        internal const double AberrationBoundMicrons = 10_000.0;

        /// <summary>Half the physical sensor extents (µm), from the simulator's selected sensor.</summary>
        internal static (double halfWidth, double halfHeight) SensorHalfDimensionsMicrons(ICameraSimulatorOptions options) {
            var sensor = SensorRegistry.Get(options.SensorModel);
            return (sensor.Width * sensor.PixelSizeMicrons / 2.0, sensor.Height * sensor.PixelSizeMicrons / 2.0);
        }

        /// <summary>
        /// Fold a screw-move delta into the simulator's injected aberration (design §3.2). Returns true when a
        /// value had to be clamped to the persisted bounds.
        ///
        /// THE SIGN, stated once: <see cref="AberrationDelta"/> is an honest plane fit in the adapter's RESPONSE
        /// frame. On a σ=−1 rig that frame is a point reflection of the physical one, so the fitted GRADIENT is
        /// frame-invariant (both the screw position and its displacement flip, and their product does not) and
        /// Gx/Gy apply raw — but the constant term has no angle to absorb the reflection, so the physical piston
        /// is <c>PistonDirectionSign · PistonMicrons</c>. This is the same asymmetry the guidance has, where
        /// <see cref="TiltScrewGeometry.SignedTotalAdjustment"/> applies the curvature sign to its backfocus
        /// component only; the two cancel, so backfocus guidance converges on both rig directions. Feeding the
        /// raw fitted constant in here is correct only on σ=+1 rigs and silently backwards on the other half —
        /// pinned by SimulatedTiltAdapterVMTests.BackfocusMove_OnOppositeRigs_MovesBackfocusInOppositeDirections.
        ///
        /// That physical piston then drives TWO responses with OPPOSITE signs, and the asymmetry is real physics,
        /// not bookkeeping (docs/focuser-direction-convention-design.md §1 and §4):
        ///   • the geometric mean-focus shift is <c>−pistonMicrons</c> — a plate move toward the camera increases
        ///     the sensor's objective-distance, so best focus is reached at a LOWER focuser position (§1(a));
        ///   • the curvature-effect (backfocus) response is <c>+pistonMicrons</c>, because σ is DEFINED as the
        ///     sign of that response to a CW turn.
        /// The simulator is pure z-space and never reads the display-only focuser-direction setting k: the
        /// focuser convention cancels out of both responses (§1(c)), so no knob belongs here.
        /// </summary>
        public static bool Fold(ICameraSimulatorOptions options, AberrationDelta delta, int pistonDirectionSign) {
            var (halfW, halfH) = SensorHalfDimensionsMicrons(options);

            // Current gradient from the options' (azimuth, amount) form — the same inversion AberrationSurface uses.
            var phi = options.TiltAngleDegrees * Math.PI / 180.0;
            var den = Math.Abs(Math.Cos(phi)) * halfW + Math.Abs(Math.Sin(phi)) * halfH;
            var g = den > 0 ? options.TiltAmountMicrons / den : 0.0;
            var gx = g * Math.Cos(phi) + delta.Gx;
            var gy = g * Math.Sin(phi) + delta.Gy;

            // Back to (azimuth, amount). The amount is a non-negative magnitude by construction — AberrationSurface
            // rejects a negative one, because direction belongs to the azimuth.
            var amount = Math.Abs(gx) * halfW + Math.Abs(gy) * halfH;
            var clamped = amount > AberrationBoundMicrons;
            options.TiltAmountMicrons = Math.Min(amount, AberrationBoundMicrons);
            options.TiltAngleDegrees = TiltCalibrationCalculator.NormalizeAngle(Math.Atan2(gy, gx) * 180.0 / Math.PI);

            var pistonMicrons = pistonDirectionSign * delta.PistonMicrons;
            if (pistonMicrons != 0.0) {
                // The sensor moving axially both shifts best focus — and the shift OPPOSES the piston. A positive
                // piston drives the plate toward the camera, which increases the sensor's objective-distance, so
                // the focus point is reached at a LOWER focuser position: Δz̄ = −piston/step (design §1(a)/(c)).
                // This line carried a + until 2026-08-04, built to satisfy the then-inverted ComputeCurvatureSign;
                // both were corrected together, or a simulated 6-step run would measure −σ_config (design §4).
                // Effective, not raw: the raw value is the -1 "unset" sentinel on an uncalibrated Inspector, and
                // the render uses Effective, so converting the piston with anything else would move best focus
                // somewhere the star field is not defocused about.
                options.OptimalFocuserPosition -= (int)Math.Round(pistonMicrons / options.EffectiveFocuserStepSizeMicrons);

                // ...and violates the optics' backfocus spacing — this one WITH the piston, which is why the two
                // signs differ. The curvature responds to the PISTON, not to any individual screw move, so a
                // corner move (piston 0 by symmetry) correctly leaves it untouched.
                // The proportionality is fixed by being the exact inverse of the inspector's backfocus row: it asks
                // for an axial ΔZ0_phys = -CurvatureAt(R) = -K·R² per screw, which must null K exactly, so
                // ΔK = ΔZ0_phys / R². Equivalently — and this is the independent check that fixes the sign —
                // ScrewInwardCurvatureSign is DEFINED as the sign of the curvature-effect response to a CW turn,
                // and a CW turn gives ΔZ0_phys = σ·(+δ), so ΔBackfocusError must carry the sign of σ.
                var radiusMicrons = options.SimScrewRadiusMillimeters * 1000.0;
                if (radiusMicrons > 0) {
                    var backfocus = options.BackfocusErrorMicrons +
                        pistonMicrons * (halfW * halfW + halfH * halfH) / (radiusMicrons * radiusMicrons);
                    clamped |= Math.Abs(backfocus) > AberrationBoundMicrons;
                    options.BackfocusErrorMicrons = Math.Clamp(backfocus, -AberrationBoundMicrons, AberrationBoundMicrons);
                }

                // ...and moves the sensor bodily along the axis, so the spacing error changes by exactly the
                // piston. Only when the user entered a spacing explicitly: left blank it is inferred from the
                // backfocus error just updated, so it already followed. Blank is never silently made explicit.
                // The option stores a magnitude and takes its sign from the backfocus error, so a piston that
                // would drive the two to disagree in sign lands at the magnitude and defers to that sign.
                if (options.BackfocusSpacingErrorMicrons >= 0.0) {
                    var signedSpacing = options.BackfocusErrorMicrons < 0.0
                        ? -options.BackfocusSpacingErrorMicrons
                        : options.BackfocusSpacingErrorMicrons;
                    var moved = Math.Abs(signedSpacing + pistonMicrons);
                    clamped |= moved > AberrationBoundMicrons;
                    options.BackfocusSpacingErrorMicrons = Math.Min(moved, AberrationBoundMicrons);
                }
            }

            return clamped;
        }
    }
}
