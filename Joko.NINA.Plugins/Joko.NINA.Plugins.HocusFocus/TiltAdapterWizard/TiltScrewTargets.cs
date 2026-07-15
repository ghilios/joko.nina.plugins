#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// Per-screw signed adjustment in unit-counts: "steps" for stepper motors, "turns" for screw
    /// adapters. Mirrors <see cref="ScrewAxialCorrection"/> (axial best-focus microns) but already
    /// divided by the adapter's unit size and signed for the curvature/backfocus direction, i.e.
    /// the exact numbers the Aberration Inspector's numeric guidance table displays and the move
    /// planner (T14) consumes.
    /// </summary>
    public readonly struct ScrewTarget {
        public ScrewTarget(double tiltMicrons, double backfocusMicrons, double tiltSteps, double backfocusSteps, double totalSteps) {
            TiltMicrons = tiltMicrons;
            BackfocusMicrons = backfocusMicrons;
            TiltSteps = tiltSteps;
            BackfocusSteps = backfocusSteps;
            TotalSteps = totalSteps;
        }

        /// <summary>Raw tilt correction in axial best-focus microns (unsigned by curvature sign).</summary>
        public double TiltMicrons { get; }

        /// <summary>Raw backfocus/curvature correction in axial best-focus microns (unsigned by curvature sign).</summary>
        public double BackfocusMicrons { get; }

        /// <summary>Tilt component, in unit-counts. No curvature-sign factor — the tilt direction is
        /// already encoded by the stored response-convention screw angle.</summary>
        public double TiltSteps { get; }

        /// <summary>Backfocus component, in unit-counts, WITH the resolved curvature sign applied.</summary>
        public double BackfocusSteps { get; }

        /// <summary>Signed total adjustment, in unit-counts: TiltSteps + BackfocusSteps. CW/+ positive
        /// (matches <see cref="TiltScrewGeometry.SignedTotalAdjustment"/> and the wizard's "+" prompts).</summary>
        public double TotalSteps { get; }
    }

    /// <summary>
    /// Shared per-screw target computation, pure and static, sitting on top of the existing
    /// <see cref="TiltScrewGeometry"/> primitives. Single source of truth for the numeric values
    /// the Aberration Inspector's guidance table displays (<c>InspectorVM.FillNumericGuidance</c>)
    /// and the future motorized-adapter move planner (T14) consumes directly from
    /// <c>SensorModel.DisplayedSensorModel</c> + <c>ITiltAdapterOptions</c> — deliberately independent
    /// of both.
    /// </summary>
    public static class TiltScrewTargets {

        /// <summary>
        /// Per-screw signed targets from a fitted paraboloid model and adapter geometry. Per screw i:
        ///   corr         = TiltScrewGeometry.ScrewCorrectionMicrons(gx, gy, kx, ky, x0, y0, angles[i], radiusMicrons)
        ///   TiltSteps     = corr.TiltMicrons / unitMicrons
        ///   BackfocusSteps = resolvedSign * corr.BackfocusMicrons / unitMicrons
        ///   TotalSteps    = TiltScrewGeometry.SignedTotalAdjustment(corr.TiltMicrons, corr.BackfocusMicrons, unitMicrons, resolvedSign)
        /// where resolvedSign is curvatureSign, defaulted to TiltScrewGeometry.DefaultScrewInwardCurvatureSign
        /// when curvatureSign == 0 (mirrors InspectorVM.FillNumericGuidance's defensive resolve). The
        /// curvature sign multiplies the BACKFOCUS component ONLY; the tilt component's direction is
        /// already baked into the stored response-convention screw angle.
        /// </summary>
        public static IReadOnlyList<ScrewTarget> ComputePerScrewTargets(
            double gx, double gy, double kx, double ky, double x0, double y0,
            IReadOnlyList<double> anglesDegrees, double radiusMicrons, double unitMicrons, int curvatureSign) {
            int resolvedSign = curvatureSign == 0 ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign : curvatureSign;

            var results = new ScrewTarget[anglesDegrees.Count];
            for (int i = 0; i < anglesDegrees.Count; i++) {
                var corr = TiltScrewGeometry.ScrewCorrectionMicrons(gx, gy, kx, ky, x0, y0, anglesDegrees[i], radiusMicrons);
                double tiltSteps = corr.TiltMicrons / unitMicrons;
                double backfocusSteps = resolvedSign * corr.BackfocusMicrons / unitMicrons;
                double totalSteps = TiltScrewGeometry.SignedTotalAdjustment(corr.TiltMicrons, corr.BackfocusMicrons, unitMicrons, resolvedSign);
                results[i] = new ScrewTarget(corr.TiltMicrons, corr.BackfocusMicrons, tiltSteps, backfocusSteps, totalSteps);
            }
            return results;
        }
    }
}
