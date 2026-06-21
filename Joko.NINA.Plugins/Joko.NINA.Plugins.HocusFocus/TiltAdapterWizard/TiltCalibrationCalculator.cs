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
    /// One tilt-plane reading from a single wizard measurement step: the OLS plane gradient (A, B) in focuser
    /// steps per normalized image coordinate (range [-0.5, 0.5]) plus the mean best-focus focuser position (used
    /// for the curvature/backfocus sign). Produced by <c>TiltPlaneModel</c>.
    /// </summary>
    public readonly struct TiltGradient {

        public TiltGradient(double a, double b, double meanFocuserPosition) {
            A = a;
            B = b;
            MeanFocuserPosition = meanFocuserPosition;
        }

        public double A { get; }
        public double B { get; }
        public double MeanFocuserPosition { get; }
    }

    /// <summary>Geometry + per-step readings needed to calibrate a tilt adapter from a sequence of measurements.
    /// The discrete 6-step flow measures: Baseline (a), AllInward (b), ReBaseline1 (c), Screw1 (d), ReBaseline2 (e),
    /// Screw2 (f). Each screw's angle/hardware is derived from the move relative to the re-baseline that immediately
    /// precedes it (c→d for screw 1, e→f for screw 2), so a single physical move is isolated per measurement pair.
    /// </summary>
    public sealed class TiltCalibrationInputs {
        public int ScrewCount { get; set; }                 // 3 or 4
        public TiltGradient Baseline { get; set; }          // a
        public TiltGradient AllInward { get; set; }         // b: all screws inward once; for the curvature (backfocus) sign (a→b)
        public TiltGradient ReBaseline1 { get; set; }       // c: all screws back out once (≈ baseline); reference for screw 1
        public TiltGradient Screw1 { get; set; }            // d: screw 1 inward once (4-screw: + screw 3 outward)
        public TiltGradient ReBaseline2 { get; set; }       // e: undo the screw-1 move (≈ c); reference for screw 2
        public TiltGradient Screw2 { get; set; }            // f: screw 2 inward once (4-screw: + screw 4 outward)
        public double ImageWidthPixels { get; set; }
        public double ImageHeightPixels { get; set; }
        public double PixelSizeMicrons { get; set; }
        public double FocuserStepMicrons { get; set; }
        public double ScrewRadiusMillimeters { get; set; }
        public double CalibrationAppliedAmount { get; set; } // turns (screws) or steps (steppers) applied per screw step
        public bool IsStepperAdjustment { get; set; }
    }

    /// <summary>Result of calibrating a tilt adapter: per-screw position angles, curvature sign, and recovered hardware.</summary>
    public sealed class TiltCalibrationResult {
        public double Screw1AngleDegrees { get; set; }
        public double Screw2AngleDegrees { get; set; }
        public double Screw3AngleDegrees { get; set; }
        public double Screw4AngleDegrees { get; set; }       // NaN for a 3-screw adapter
        public int CalibratedScrewCount { get; set; }
        public bool IsCalibrated { get; set; }
        public double RawAngleDiffDegrees { get; set; }      // measured screw1->screw2 gap before the constrained fit
        public int CurvatureSign { get; set; }               // +1 / -1
        public double MeasuredHardwareMicrons { get; set; }  // µm/turn (screws) or µm/step (steppers); NaN if uncomputable
        public double Screw1DirectionDegrees { get; set; }   // raw measured direction of screw 1's move (atan2(dA,-dB))
        public double Screw2DirectionDegrees { get; set; }   // raw measured direction of screw 2's move

        /// <summary>Ratio (&gt;= 1) of the larger to the smaller single-screw gradient-change magnitude. For two
        /// clean, equal single-screw moves this is ~1; a large value means the two calibration turns were unequal
        /// (uneven turning / backlash) and the recovered pitch/step size is unreliable. NaN if a magnitude is 0.</summary>
        public double MoveMagnitudeRatio { get; set; }
    }

    /// <summary>
    /// Pure calibration math shared by the Tilt Adapter Wizard (live) and headless validation tooling (TestApp).
    /// This is the single source of truth for converting a sequence of tilt-plane readings (baseline, all-screws,
    /// screw 1, screw 2) into per-screw position angles, the curvature/backfocus sign, and the recovered thread
    /// pitch / stepper step size. The wizard VM is a thin adapter that gathers these inputs and applies the
    /// results to its options; keeping the math here guarantees the validator and the wizard never drift.
    ///
    /// Angle convention matches the rest of the plugin: degrees clockwise from straight up (12 o'clock) in image
    /// space (+x right, +y down), via <c>atan2(dA, -dB)</c>.
    /// </summary>
    public static class TiltCalibrationCalculator {

        public static double NormalizeAngle(double deg) => ((deg % 360) + 360) % 360;

        /// <summary>Curvature (backfocus) sign from the all-screws-inward vs baseline mean-focus delta.</summary>
        public static int ComputeCurvatureSign(double allScrewsMean, double baselineMean) {
            return (allScrewsMean - baselineMean) >= 0 ? 1 : -1;
        }

        /// <summary>
        /// Ratio (&gt;= 1) of the larger to the smaller single-screw gradient-change magnitude. Two clean, equal
        /// single-screw calibration turns produce ~equal magnitudes (ratio ~1); a large ratio means the two turns
        /// were unequal, so the recovered hardware (pitch/step size) is unreliable. Returns NaN if either magnitude
        /// is 0.
        /// </summary>
        public static double MoveMagnitudeRatio(double d1A, double d1B, double d2A, double d2B) {
            double m1 = Math.Sqrt(d1A * d1A + d1B * d1B);
            double m2 = Math.Sqrt(d2A * d2A + d2B * d2B);
            if (m1 <= 0 || m2 <= 0) {
                return double.NaN;
            }
            return m1 >= m2 ? m1 / m2 : m2 / m1;
        }

        /// <summary>
        /// Per-screw position angles from the two single-screw gradient changes (screw move minus baseline).
        /// Determines the winding direction from the measured data (image mirroring can flip clockwise/CCW), then
        /// performs a constrained least-squares fit to equal angular spacing (120° for 3 screws; 90° with opposite
        /// screws 180° apart for 4 screws). Returns the four screw angles (s4 = NaN for 3 screws) and the raw
        /// measured screw1->screw2 gap (before the fit) for separation diagnostics.
        /// </summary>
        public static (double s1, double s2, double s3, double s4, double rawDiff) ComputeScrewAngles(
            double d1A, double d1B, double d2A, double d2B, int screwCount) {
            double angle1 = NormalizeAngle(Math.Atan2(d1A, -d1B) * 180.0 / Math.PI);
            double angle2 = NormalizeAngle(Math.Atan2(d2A, -d2B) * 180.0 / Math.PI);

            double rawDiff = NormalizeAngle(angle2 - angle1);
            bool clockwise = rawDiff < 180.0;

            if (screwCount == 3) {
                double s = clockwise ? 120.0 : -120.0;
                double expectedDiff = clockwise ? 120.0 : 240.0;
                double theta1 = NormalizeAngle(angle1 + (rawDiff - expectedDiff) / 2.0);
                return (theta1, NormalizeAngle(theta1 + s), NormalizeAngle(theta1 + 2 * s), double.NaN, rawDiff);
            } else {
                double s = clockwise ? 90.0 : -90.0;
                double expectedDiff = clockwise ? 90.0 : 270.0;
                double theta1 = NormalizeAngle(angle1 + (rawDiff - expectedDiff) / 2.0);
                double theta2 = NormalizeAngle(theta1 + s);
                // Opposite screws are always 180° apart regardless of mirroring.
                return (theta1, theta2, NormalizeAngle(theta1 + 180.0), NormalizeAngle(theta2 + 180.0), rawDiff);
            }
        }

        /// <summary>
        /// Recovers the adapter hardware (µm per turn for screws, µm per step for steppers) from the two
        /// single-screw moves' tilt-plane gradient changes and the known applied turns/steps. Mirrors the wizard's
        /// CalculateAndSaveHardware: convert each gradient change to a physical gradient, derive the per-screw axial
        /// move via the adapter lever arm, average the two, and divide by the applied amount. Returns NaN when any
        /// required input is non-positive or the result is non-positive.
        /// </summary>
        public static double RecoverHardwareMicrons(TiltCalibrationInputs inputs) {
            double pixelSize = inputs.PixelSizeMicrons;
            double fStep = inputs.FocuserStepMicrons;
            double radiusMm = inputs.ScrewRadiusMillimeters;
            double applied = inputs.CalibrationAppliedAmount;
            double sensorW = inputs.ImageWidthPixels * pixelSize;
            double sensorH = inputs.ImageHeightPixels * pixelSize;
            if (radiusMm <= 0 || applied <= 0 || pixelSize <= 0 || fStep <= 0 || sensorW <= 0 || sensorH <= 0) {
                return double.NaN;
            }

            double radiusMicrons = radiusMm * 1000.0;
            int n = inputs.ScrewCount;

            // Each screw move is measured relative to the re-baseline that immediately precedes it (c→d, e→f),
            // isolating a single physical move per pair.
            double d1A = inputs.Screw1.A - inputs.ReBaseline1.A;
            double d1B = inputs.Screw1.B - inputs.ReBaseline1.B;
            double d2A = inputs.Screw2.A - inputs.ReBaseline2.A;
            double d2B = inputs.Screw2.B - inputs.ReBaseline2.B;

            var (g1x, g1y) = TiltScrewGeometry.PlaneGradientToPhysical(d1A, d1B, fStep, sensorW, sensorH);
            var (g2x, g2y) = TiltScrewGeometry.PlaneGradientToPhysical(d2A, d2B, fStep, sensorW, sensorH);
            double delta1 = TiltScrewGeometry.CalibrationAxialMoveMicrons(g1x, g1y, n, radiusMicrons);
            double delta2 = TiltScrewGeometry.CalibrationAxialMoveMicrons(g2x, g2y, n, radiusMicrons);

            double measured = 0.5 * (delta1 + delta2) / applied;
            if (double.IsNaN(measured) || measured <= 0) {
                return double.NaN;
            }
            return measured;
        }

        /// <summary>
        /// Drift of a re-baseline relative to its reference, as a fraction of the subsequent screw-move magnitude.
        /// A good re-baseline returns close to the prior state, so this ratio is near 0; a large value means the
        /// undo/redo left residual tilt (backlash or an uneven turn) comparable to the screw-move signal, so the
        /// recovered angle/hardware for that screw is unreliable. Returns NaN when the screw-move magnitude is 0.
        /// </summary>
        public static double RebaselineDriftRatio(double driftA, double driftB, double moveA, double moveB) {
            double moveMag = Math.Sqrt(moveA * moveA + moveB * moveB);
            if (moveMag <= 0) {
                return double.NaN;
            }
            double driftMag = Math.Sqrt(driftA * driftA + driftB * driftB);
            return driftMag / moveMag;
        }

        /// <summary>Runs the full calibration: screw angles, curvature sign, and recovered hardware.</summary>
        public static TiltCalibrationResult Calibrate(TiltCalibrationInputs inputs) {
            double d1A = inputs.Screw1.A - inputs.ReBaseline1.A;
            double d1B = inputs.Screw1.B - inputs.ReBaseline1.B;
            double d2A = inputs.Screw2.A - inputs.ReBaseline2.A;
            double d2B = inputs.Screw2.B - inputs.ReBaseline2.B;

            var (s1, s2, s3, s4, rawDiff) = ComputeScrewAngles(d1A, d1B, d2A, d2B, inputs.ScrewCount);

            return new TiltCalibrationResult {
                Screw1AngleDegrees = s1,
                Screw2AngleDegrees = s2,
                Screw3AngleDegrees = s3,
                Screw4AngleDegrees = s4,
                CalibratedScrewCount = inputs.ScrewCount,
                IsCalibrated = true,
                RawAngleDiffDegrees = rawDiff,
                CurvatureSign = ComputeCurvatureSign(inputs.AllInward.MeanFocuserPosition, inputs.Baseline.MeanFocuserPosition),
                MeasuredHardwareMicrons = RecoverHardwareMicrons(inputs),
                Screw1DirectionDegrees = NormalizeAngle(Math.Atan2(d1A, -d1B) * 180.0 / Math.PI),
                Screw2DirectionDegrees = NormalizeAngle(Math.Atan2(d2A, -d2B) * 180.0 / Math.PI),
                MoveMagnitudeRatio = MoveMagnitudeRatio(d1A, d1B, d2A, d2B)
            };
        }
    }
}
