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
    /// In the 4-step flow (no curvature steps) Baseline/AllInward are unset, HasCurvatureMeasurement is false, and
    /// the baseline reading occupies the ReBaseline1 slot.
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

        /// <summary>False for a 4-step run that skipped the curvature-direction steps: Baseline and
        /// AllInward were never measured (leave them default) and the baseline reading is supplied
        /// in the ReBaseline1 slot, which is the screw-1 reference in both flows.</summary>
        public bool HasCurvatureMeasurement { get; set; } = true;

        /// <summary>Curvature sign carried into the result when HasCurvatureMeasurement is false
        /// (the configured/assumed ScrewInwardCurvatureSign; 0 = unknown).</summary>
        public int FallbackCurvatureSign { get; set; }
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
        public int CurvatureSign { get; set; }               // +1 / -1; 0 = unknown (4-step run whose fallback sign was never configured)
        public double MeasuredHardwareMicrons { get; set; }  // µm/turn (screws) or µm/step (steppers); NaN if uncomputable
        public double Screw1DirectionDegrees { get; set; }   // raw measured direction of screw 1's move (atan2(dA,-dB))
        public double Screw2DirectionDegrees { get; set; }   // raw measured direction of screw 2's move

        /// <summary>Ratio (&gt;= 1) of the larger to the smaller single-screw gradient-change magnitude. For two
        /// clean, equal single-screw moves this is ~1; a large value means the two calibration turns were unequal
        /// (uneven turning / backlash) and the recovered pitch/step size is unreliable. NaN if a magnitude is 0.</summary>
        public double MoveMagnitudeRatio { get; set; }

        /// <summary>Half the absolute difference of the two single-screw recovered pitches (µm/turn or µm/step).
        /// This is the physical-space 1σ on the recovered hardware — it captures screw-to-screw disagreement the
        /// (A,B) <see cref="MoveMagnitudeRatio"/> misses because the plane→physical conversion is anisotropic.
        /// NaN when the hardware is uncomputable.</summary>
        public double PitchUncertaintyMicrons { get; set; }

        /// <summary>Signal-to-noise / reliability of the whole calibration, derived from the per-step tilt vectors.
        /// Populated by <see cref="TiltCalibrationCalculator.Calibrate"/>.</summary>
        public TiltCalibrationConfidence Confidence { get; set; }
    }

    /// <summary>
    /// How trustworthy a calibration is, derived purely from the per-step tilt vectors — no fit covariance
    /// needed. The screw-move magnitudes are the <i>signal</i>; quantities that must be ~0 in a noise-free,
    /// stationary measurement are <i>noise probes</i>: turning all screws inward equally is a pure piston (no net
    /// tilt change vs baseline), and each re-baseline should return to its predecessor (zero drift). A 6-step run
    /// has all three noise probes; a 4-step run (no curvature steps) has only the single re-baseline drift, and
    /// <see cref="AllInwardTiltResidual"/> / <see cref="Rebaseline1Drift"/> are NaN there. When the noise probes
    /// rival the screw-move signal the recovered geometry is dominated by measurement noise / between step drift,
    /// no matter how cleanly the screws were turned.
    /// </summary>
    public sealed class TiltCalibrationConfidence {
        public double ScrewMoveSignal { get; set; }               // mean |single-screw move| magnitude (the signal)
        public double NoiseEstimate { get; set; }                 // RMS of the noise probes below
        public double SignalToNoise { get; set; }                 // ScrewMoveSignal / NoiseEstimate (Inf if noise 0)
        public double PredictedAngleUncertaintyDeg { get; set; }  // ~1σ on each recovered screw direction
        public double AllInwardTiltResidual { get; set; }         // |AllInward − Baseline|, should be ~0 (pure piston); NaN for 4-step runs
        public double Rebaseline1Drift { get; set; }              // |ReBaseline1 − Baseline|, should be ~0; NaN for 4-step runs
        public double Rebaseline2Drift { get; set; }              // |ReBaseline2 − ReBaseline1|, should be ~0
        public bool IsReliable { get; set; }                      // SignalToNoise >= MinReliableSignalToNoise
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

        /// <summary>Below this signal-to-noise the calibration is noise-dominated and should not be trusted/applied.
        /// At SNR = 2 the screw-move signal is twice the noise floor, giving a recovered-direction 1σ of ~27°.</summary>
        public const double MinReliableSignalToNoise = 2.0;

        private static double Magnitude(double a, double b) => Math.Sqrt(a * a + b * b);

        /// <summary>
        /// Estimates how trustworthy the calibration is from the per-step tilt vectors. See
        /// <see cref="TiltCalibrationConfidence"/> for the model: screw moves are the signal; the noise probes are
        /// independent quantities that are each ~0 in an ideal measurement — the all-inward piston residual and both
        /// re-baseline drifts in a 6-step run, only the single re-baseline drift in a 4-step run.
        /// </summary>
        public static TiltCalibrationConfidence ComputeConfidence(TiltCalibrationInputs inputs) {
            double s1 = Magnitude(inputs.Screw1.A - inputs.ReBaseline1.A, inputs.Screw1.B - inputs.ReBaseline1.B);
            double s2 = Magnitude(inputs.Screw2.A - inputs.ReBaseline2.A, inputs.Screw2.B - inputs.ReBaseline2.B);
            double signal = 0.5 * (s1 + s2);

            double drift2 = Magnitude(inputs.ReBaseline2.A - inputs.ReBaseline1.A, inputs.ReBaseline2.B - inputs.ReBaseline1.B);
            double allInward = double.NaN;
            double drift1 = double.NaN;
            double noise;
            if (inputs.HasCurvatureMeasurement) {
                allInward = Magnitude(inputs.AllInward.A - inputs.Baseline.A, inputs.AllInward.B - inputs.Baseline.B);
                drift1 = Magnitude(inputs.ReBaseline1.A - inputs.Baseline.A, inputs.ReBaseline1.B - inputs.Baseline.B);
                noise = Math.Sqrt((allInward * allInward + drift1 * drift1 + drift2 * drift2) / 3.0);
            } else {
                // 4-step run: the only available noise probe is the single re-baseline drift. A one-sample noise
                // estimate makes the IsReliable gate looser than the 6-step RMS-of-3 — an accepted tradeoff of the
                // shorter flow.
                noise = drift2;
            }

            double snr = noise > 0 ? signal / noise : double.PositiveInfinity;
            // A screw direction is atan2 of its move vector; transverse noise of ~noise on a signal of ~signal
            // perturbs that direction by ~atan(noise/signal). Degenerate (no signal) => maximally uncertain.
            double angleUncertainty = signal > 0 ? Math.Atan2(noise, signal) * 180.0 / Math.PI : 90.0;

            return new TiltCalibrationConfidence {
                ScrewMoveSignal = signal,
                NoiseEstimate = noise,
                SignalToNoise = snr,
                PredictedAngleUncertaintyDeg = angleUncertainty,
                AllInwardTiltResidual = allInward,
                Rebaseline1Drift = drift1,
                Rebaseline2Drift = drift2,
                IsReliable = snr >= MinReliableSignalToNoise
            };
        }

        /// <summary>
        /// Curvature (backfocus) sign σ from the all-screws-inward vs baseline mean-focus delta:
        /// <c>σ = −sign(allScrewsMean − baselineMean)</c>. A zero delta yields +1, matching the
        /// long-standing degenerate behavior.
        ///
        /// DERIVATION (docs/focuser-direction-convention-design.md §1). σ is DEFINED — see
        /// <see cref="TiltScrewGeometry"/>'s empirical anchor — as the sign of the curvature-effect
        /// response to a CW/+ turn, where the curvature effect is the FITTED (z-space) quantity. It
        /// therefore factors into the two independent bits it fuses,
        /// <c>σ = sign(m)·sign(k)</c>: the adapter's plate response <c>m</c> to a CW turn
        /// (<c>m &gt; 0</c> ⇔ CW moves the plate toward the camera) and the focuser convention
        /// <c>k</c> (<c>k = +1</c> ⇔ increasing focuser position moves the camera away from the
        /// objective).
        ///
        /// The all-screws step is pure piston (<c>Δb = m·r</c>), so
        /// <c>Δz̄ = −m·r·(1 − q̄)/k</c> and hence <c>sign(Δz̄) = −sign(m)·sign(k) = −σ</c> for the
        /// <c>|q̄| ≪ 1</c> regime every real optical train sits in. <c>m</c> and <c>k</c> enter σ
        /// and the probe ONLY through their product, so the focuser convention CANCELS: this
        /// measurement is correct on standard and inverted focusers alike, with zero user input.
        ///
        /// This returned the negated sign — i.e. <c>−σ</c>, inverted on EVERY rig — until
        /// 2026-08-04. Session 20260803-200647 is the measurement that exposed it: a +150 backfocus
        /// step (plate toward the camera, <c>m = +1</c>) on a standard focuser moved the mean best
        /// focus 5498.4 → 5136.6, so <c>σ = −sign(−361.8) = +1</c> — exactly the value the user had
        /// already had to set by hand to make corrections converge. The paired correction to
        /// <see cref="TiltScrewGeometry.PhysicalToStoredAngle"/>'s 180° offset shipped in the same
        /// commit; the two inversions had been cancelling in the screw diagram (design §7).
        /// </summary>
        public static int ComputeCurvatureSign(double allScrewsMean, double baselineMean) {
            return (allScrewsMean - baselineMean) <= 0 ? 1 : -1;
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
        /// Places all screws from a manually entered screw-1 position angle: equal spacing (120° for
        /// 3 screws; 90° for 4, opposite screws 180° apart), numbered clockwise or counter-clockwise
        /// around the IMAGE (mirrors/diagonals can flip the physical winding). s4 = NaN for 3 screws.
        /// </summary>
        public static (double s1, double s2, double s3, double s4) ComputeManualScrewAngles(
            double screw1Deg, bool clockwise, int screwCount) {
            double step = (screwCount == 3 ? 120.0 : 90.0) * (clockwise ? 1.0 : -1.0);
            double s1 = NormalizeAngle(screw1Deg);
            double s2 = NormalizeAngle(s1 + step);
            double s3 = NormalizeAngle(s1 + 2 * step);
            double s4 = screwCount == 4 ? NormalizeAngle(s1 + 3 * step) : double.NaN;
            return (s1, s2, s3, s4);
        }

        /// <summary>Per-screw recovered hardware: the average (µm/turn or µm/step) plus each screw's own recovered
        /// value (delta_i / applied). Same math as the wizard's CalculateAndSaveHardware. All three are NaN when any
        /// input is non-positive or the average is non-positive.</summary>
        public static (double measured, double delta1PerApplied, double delta2PerApplied) RecoverHardwareDetailed(TiltCalibrationInputs inputs) {
            double pixelSize = inputs.PixelSizeMicrons;
            double fStep = inputs.FocuserStepMicrons;
            double radiusMm = inputs.ScrewRadiusMillimeters;
            double applied = inputs.CalibrationAppliedAmount;
            double sensorW = inputs.ImageWidthPixels * pixelSize;
            double sensorH = inputs.ImageHeightPixels * pixelSize;
            if (radiusMm <= 0 || applied <= 0 || pixelSize <= 0 || fStep <= 0 || sensorW <= 0 || sensorH <= 0) {
                return (double.NaN, double.NaN, double.NaN);
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
                return (double.NaN, double.NaN, double.NaN);
            }
            return (measured, delta1 / applied, delta2 / applied);
        }

        /// <summary>
        /// Recovers the adapter hardware (µm per turn for screws, µm per step for steppers) from the two
        /// single-screw moves' tilt-plane gradient changes and the known applied turns/steps. Mirrors the wizard's
        /// CalculateAndSaveHardware: convert each gradient change to a physical gradient, derive the per-screw axial
        /// move via the adapter lever arm, average the two, and divide by the applied amount. See
        /// <see cref="RecoverHardwareDetailed"/>. Returns NaN when any required input is non-positive or the result
        /// is non-positive.
        /// </summary>
        public static double RecoverHardwareMicrons(TiltCalibrationInputs inputs) => RecoverHardwareDetailed(inputs).measured;

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
            var (measuredHardware, delta1PerApplied, delta2PerApplied) = RecoverHardwareDetailed(inputs);

            return new TiltCalibrationResult {
                Screw1AngleDegrees = s1,
                Screw2AngleDegrees = s2,
                Screw3AngleDegrees = s3,
                Screw4AngleDegrees = s4,
                CalibratedScrewCount = inputs.ScrewCount,
                IsCalibrated = true,
                RawAngleDiffDegrees = rawDiff,
                CurvatureSign = inputs.HasCurvatureMeasurement
                    ? ComputeCurvatureSign(inputs.AllInward.MeanFocuserPosition, inputs.Baseline.MeanFocuserPosition)
                    : inputs.FallbackCurvatureSign,
                MeasuredHardwareMicrons = measuredHardware,
                PitchUncertaintyMicrons = double.IsNaN(delta1PerApplied)
                    ? double.NaN
                    : Math.Abs(delta1PerApplied - delta2PerApplied) / 2.0,
                Screw1DirectionDegrees = NormalizeAngle(Math.Atan2(d1A, -d1B) * 180.0 / Math.PI),
                Screw2DirectionDegrees = NormalizeAngle(Math.Atan2(d2A, -d2B) * 180.0 / Math.PI),
                MoveMagnitudeRatio = MoveMagnitudeRatio(d1A, d1B, d2A, d2B),
                Confidence = ComputeConfidence(inputs)
            };
        }
    }
}
