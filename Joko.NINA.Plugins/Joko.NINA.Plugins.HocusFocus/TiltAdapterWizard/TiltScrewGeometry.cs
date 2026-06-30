#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// Pure geometry shared by the tilt-adapter wizard (calibration) and the Aberration
    /// Inspector (application). The two directions are exact inverses under a single
    /// least-squares plane model of n equally-spaced screws on a circle of radius R:
    ///
    ///   forward (moves -> gradient): G = (2 / (n·R²)) · Σ dᵢ·pᵢ
    ///   inverse (gradient -> moves): δᵢ = -(G·pᵢ)   (the per-screw axial move that cancels G)
    ///
    /// All positions and displacements are in microns; the screw angle convention matches
    /// the rest of the plugin — degrees clockwise from straight up (12 o'clock) in image
    /// space, where image coordinates are +x right / +y down (so "up" is -y). A screw at
    /// angle θ therefore sits at (R·sinθ, -R·cosθ).
    /// </summary>
    public static class TiltScrewGeometry {

        /// <summary>Screw position in sensor microns from its position angle and radius.</summary>
        public static (double x, double y) ScrewPositionMicrons(double angleDegrees, double radiusMicrons) {
            var t = angleDegrees * Math.PI / 180.0;
            return (radiusMicrons * Math.Sin(t), -radiusMicrons * Math.Cos(t));
        }

        /// <summary>
        /// Convert tilt-plane coefficients (A, B) — focuser steps per normalized image
        /// coordinate over the range [-0.5, 0.5] — into a physical best-focus gradient in
        /// microns of focuser travel per micron of sensor displacement (dimensionless).
        /// Mirrors InspectorVM/TiltAdapterWizardVM's ComputeTiltAngleDeg conversion.
        /// </summary>
        public static (double gx, double gy) PlaneGradientToPhysical(
            double a, double b, double focuserStepMicrons, double sensorWidthMicrons, double sensorHeightMicrons) {
            var gx = a * focuserStepMicrons / sensorWidthMicrons;
            var gy = b * focuserStepMicrons / sensorHeightMicrons;
            return (gx, gy);
        }

        /// <summary>
        /// Exact inverse of <see cref="PlaneGradientToPhysical"/>: convert a physical best-focus tilt gradient
        /// (focuser microns of travel per micron of sensor displacement — e.g. the per-star paraboloid's Gx/Gy)
        /// into tilt-plane coefficients (A, B) in focuser steps per normalized image coordinate over [-0.5, 0.5].
        /// This lets the robust per-star sensor-model tilt feed the same screw-calibration math the 4-corner plane
        /// uses, so the wizard can consume the better estimator without changing downstream geometry.
        /// </summary>
        public static (double a, double b) PhysicalGradientToPlane(
            double gx, double gy, double focuserStepMicrons, double sensorWidthMicrons, double sensorHeightMicrons) {
            if (focuserStepMicrons <= 0) {
                return (double.NaN, double.NaN);
            }
            var a = gx * sensorWidthMicrons / focuserStepMicrons;
            var b = gy * sensorHeightMicrons / focuserStepMicrons;
            return (a, b);
        }

        /// <summary>
        /// Per-screw axial move (microns) that cancels the given best-focus tilt gradient,
        /// evaluated at the screw location: δ = -(gx·x + gy·y). Positive = the direction
        /// returned here is the sensor-axial displacement to apply at that screw.
        /// </summary>
        public static double TiltCorrectionMicrons(double gx, double gy, double angleDegrees, double radiusMicrons) {
            var (x, y) = ScrewPositionMicrons(angleDegrees, radiusMicrons);
            return -(gx * x + gy * y);
        }

        /// <summary>
        /// Axial move (microns) of a single screw implied by the least-squares tilt-plane
        /// gradient change it produced: δ = |ΔG| · n · R / 2. This is the inverse of
        /// <see cref="TiltCorrectionMicrons"/> for an isolated single-screw move and is used by
        /// the wizard to recover thread pitch / step size from a known applied move.
        /// </summary>
        public static double SingleScrewAxialMoveMicrons(double dGx, double dGy, int screwCount, double radiusMicrons) {
            return Math.Sqrt(dGx * dGx + dGy * dGy) * screwCount * radiusMicrons / 2.0;
        }

        /// <summary>
        /// Per-screw axial corrections (microns) needed at a screw location to flatten tilt and to
        /// neutralize field curvature ("backfocus"), evaluated against the fitted paraboloid model.
        /// Both are in axial best-focus microns (the same unit as physical screw travel), so the
        /// total is simply their sum and no square root is involved — the curvature term
        /// kx·(x-x0)² + ky·(y-y0)² is already a length.
        /// </summary>
        public static ScrewAxialCorrection ScrewCorrectionMicrons(
            double gx, double gy, double kx, double ky, double x0, double y0,
            double angleDegrees, double radiusMicrons) {
            var (px, py) = ScrewPositionMicrons(angleDegrees, radiusMicrons);
            double tiltCorrection = -(gx * px + gy * py);
            double cx = px - x0;
            double cy = py - y0;
            double backfocusCorrection = -(kx * cx * cx + ky * cy * cy);
            return new ScrewAxialCorrection(tiltCorrection, backfocusCorrection);
        }

        /// <summary>
        /// Convert an axial correction (microns) into a signed adjustment in turns (screws) or
        /// steps (steppers), where positive = turn the screw inward. Inward turning moves the
        /// sensor axially in the direction given by <paramref name="curvatureSign"/> (= the wizard's
        /// ScrewInwardCurvatureSign), so the inward amount is curvatureSign·correction / unitMicrons.
        /// </summary>
        public static double InwardAdjustment(double correctionMicrons, double unitMicrons, int curvatureSign) {
            if (unitMicrons <= 0) return double.NaN;
            return curvatureSign * correctionMicrons / unitMicrons;
        }

        /// <summary>
        /// Axial move (microns) applied per screw during a wizard calibration step, recovered from
        /// the tilt-plane gradient change it produced. The wizard's move pattern differs by adapter:
        ///   • 3-screw: a single screw is turned inward — lever arm 1.5·R, so δ = 1.5·R·|ΔG|.
        ///   • 4-screw: opposite screws are push-pulled by the same amount — lever arm R, so δ = R·|ΔG|.
        /// Dividing by the known applied turns/steps yields thread pitch / step size.
        /// </summary>
        public static double CalibrationAxialMoveMicrons(double dGx, double dGy, int screwCount, double radiusMicrons) {
            double magnitude = Math.Sqrt(dGx * dGx + dGy * dGy);
            double leverArm = screwCount == 4 ? radiusMicrons : 1.5 * radiusMicrons;
            return magnitude * leverArm;
        }

        /// <summary>True when |active − measured| / |measured| exceeds the given fraction.</summary>
        public static bool PitchMismatchExceeds(double activeMicrons, double measuredMicrons, double fraction) {
            if (activeMicrons <= 0 || measuredMicrons <= 0) return false;
            return Math.Abs(activeMicrons - measuredMicrons) / measuredMicrons > fraction;
        }
    }

    public readonly struct ScrewAxialCorrection {
        public ScrewAxialCorrection(double tiltMicrons, double backfocusMicrons) {
            TiltMicrons = tiltMicrons;
            BackfocusMicrons = backfocusMicrons;
        }

        public double TiltMicrons { get; }
        public double BackfocusMicrons { get; }
        public double TotalMicrons => TiltMicrons + BackfocusMicrons;
    }
}
