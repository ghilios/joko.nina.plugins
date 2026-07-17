#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter {

    /// <summary>The best-focus surface change produced by a set of screw moves. Units: Gx/Gy are dimensionless
    /// (focuser µm of travel per µm of sensor displacement); piston is µm of focuser travel.</summary>
    public readonly struct AberrationDelta {

        public AberrationDelta(double gx, double gy, double pistonMicrons) {
            Gx = gx;
            Gy = gy;
            PistonMicrons = pistonMicrons;
        }

        public double Gx { get; }
        public double Gy { get; }

        /// <summary>The fitted plane's constant term, in the adapter's response frame. Multiply by
        /// <see cref="SimulatedTiltAdapter.PistonDirectionSign"/> to get the physical best-focus piston.</summary>
        public double PistonMicrons { get; }
    }

    /// <summary>
    /// A virtual tilt adapter. Moving a screw axially by δ moves the sensor there by δ, which moves the
    /// best-focus surface by exactly the plane fitted through the screw displacements. That makes this the
    /// exact inverse of <see cref="TiltScrewGeometry.TiltCorrectionMicrons"/>, which the Aberration Inspector
    /// uses to tell the user how far to turn each screw — so guidance applied here converges to zero.
    /// Pure: no options, no WPF, no NINA types.
    ///
    /// CONVENTIONS (all inherited from <see cref="TiltScrewGeometry"/> — none are re-derived here):
    ///
    /// • <b>Screw angles are the stored "response-convention" angles</b>, i.e. the same quantity
    ///   <c>ITiltAdapterOptions.Screw1..4AngleDegrees</c> holds, not the physical image angle. The wizard
    ///   defines the stored angle as the direction the best-focus gradient moves for a CW turn, and
    ///   <see cref="TiltScrewGeometry.PhysicalToStoredAngle"/> converts (they differ by 180° on rigs where CW
    ///   drives the adapter toward the objective). Storing the same convention as the real adapter is what
    ///   lets the panel's coherence badge and Copy-from/Copy-to-adapter compare the fields directly.
    ///
    /// • <b>The rig direction is NOT re-applied to a turn.</b> The inspector prints tilt turns as
    ///   <c>TiltCorrectionMicrons(...)/unit</c> with no sign factor, because the stored angle already carries
    ///   the direction — pinned by TiltScrewGeometryTests.SignedTotalAdjustment_AppliesSignOnlyToBackfocus
    ///   ("a pure-tilt correction must render the same rotational direction on every rig"). The exact inverse
    ///   must match, so <see cref="AxialMicronsForUnits"/> is a pure scale. Re-applying the curvature sign here
    ///   would drive the simulator backwards on one of the two rig directions and the loop would diverge.
    ///
    /// • <b>The piston is the one place the rig direction survives</b> — see <see cref="PistonDirectionSign"/>.
    ///   This mirrors the guidance exactly, which applies the curvature sign to its backfocus row only.
    /// </summary>
    public sealed class SimulatedTiltAdapter {
        private readonly double[] anglesDegrees;
        private readonly double radiusMicrons;

        public SimulatedTiltAdapter(double[] screwAnglesDegrees, double screwRadiusMicrons,
                double unitMicrons, int inwardCurvatureSign) {
            if (screwAnglesDegrees == null) throw new ArgumentNullException(nameof(screwAnglesDegrees));
            if (screwAnglesDegrees.Length != 3 && screwAnglesDegrees.Length != 4)
                throw new ArgumentOutOfRangeException(nameof(screwAnglesDegrees), "Only 3- or 4-screw adapters exist.");
            if (screwRadiusMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(screwRadiusMicrons));
            if (unitMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(unitMicrons));

            anglesDegrees = (double[])screwAnglesDegrees.Clone();
            radiusMicrons = screwRadiusMicrons;
            UnitMicrons = unitMicrons;
            // Resolve a defensive 0 the same way the inspector's FillNumericGuidance does, so both sides of
            // the loop agree on what an unset direction means.
            InwardCurvatureSign = inwardCurvatureSign == 0
                ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign
                : Math.Sign(inwardCurvatureSign);
        }

        public int ScrewCount => anglesDegrees.Length;

        /// <summary>Axial µm per unit of user input — thread pitch (per turn) or stepper step size (per step).</summary>
        public double UnitMicrons { get; }

        public int InwardCurvatureSign { get; }

        /// <summary>
        /// Converts <see cref="AberrationDelta.PistonMicrons"/> (response frame) to the physical best-focus
        /// piston. On a −1 rig the response frame is a point reflection of the physical frame, so the fitted
        /// GRADIENT is frame-invariant — both δ and p flip, and their product does not — but the piston has no
        /// angle to absorb the flip and keeps the sign. This is the same asymmetry the guidance has, where
        /// <see cref="TiltScrewGeometry.SignedTotalAdjustment"/> applies the curvature sign to the backfocus
        /// component only; the two cancel, so backfocus guidance converges too.
        /// </summary>
        public int PistonDirectionSign => InwardCurvatureSign;

        /// <summary>
        /// Axial displacement (µm) for a signed user amount. Direction comes from the button (the sign of
        /// <paramref name="units"/>: + for ⟳/"+", − for ⟲/"−") and from nothing else — see the class remarks
        /// for why the rig's curvature sign must not appear here.
        /// </summary>
        public double AxialMicronsForUnits(double units) => units * UnitMicrons;

        /// <summary>
        /// Fit the plane through the per-screw axial displacements. Exactly determined for 3 screws,
        /// least-squares for 4 (opposite screws are mechanically coupled, so a 4-screw move set is
        /// generally consistent and the fit is exact there too).
        /// </summary>
        public AberrationDelta ApplyMoves(double[] axialMicronsPerScrew) {
            if (axialMicronsPerScrew == null) throw new ArgumentNullException(nameof(axialMicronsPerScrew));
            if (axialMicronsPerScrew.Length != ScrewCount)
                throw new ArgumentException($"Expected {ScrewCount} displacements.", nameof(axialMicronsPerScrew));

            // Normal equations for z = a·x + b·y + c.
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sz = 0, sxz = 0, syz = 0;
            var n = ScrewCount;
            for (var i = 0; i < n; i++) {
                var (x, y) = TiltScrewGeometry.ScrewPositionMicrons(anglesDegrees[i], radiusMicrons);
                var z = axialMicronsPerScrew[i];
                sxx += x * x; sxy += x * y; syy += y * y;
                sx += x; sy += y; sz += z; sxz += x * z; syz += y * z;
            }

            // Solve the 3x3 system by Cramer's rule.
            var m = new[,] { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, (double)n } };
            var rhs = new[] { sxz, syz, sz };
            var det = Det3(m);
            if (Math.Abs(det) < 1e-12)
                throw new InvalidOperationException("Degenerate screw geometry: the screw angles are collinear.");

            var a = Det3(Replace(m, 0, rhs)) / det;
            var b = Det3(Replace(m, 1, rhs)) / det;
            var c = Det3(Replace(m, 2, rhs)) / det;
            return new AberrationDelta(a, b, c);
        }

        private static double[,] Replace(double[,] m, int col, double[] v) {
            var r = (double[,])m.Clone();
            for (var i = 0; i < 3; i++) r[i, col] = v[i];
            return r;
        }

        private static double Det3(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
          - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
          + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
    }
}
