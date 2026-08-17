#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    /// <summary>How a return-to-a-past-run target was derived. See <see cref="TiltRevertPlanFactory"/>.</summary>
    public enum TiltRevertMechanism {
        /// <summary>Absolute per-motor counters recorded at that run. Exact, and uses no calibration at all.</summary>
        DevicePositions,

        /// <summary>The difference between two measured models. The only option for manual screws.</summary>
        MeasurementDifferential,

        Unavailable
    }

    /// <summary>The motion that takes the adapter from its current state back to a past run's state.</summary>
    public sealed class TiltRevertTarget {

        public TiltRevertTarget(
            TiltRevertMechanism mechanism,
            IReadOnlyList<double> stepsPerScrew,
            double unitMicrons,
            bool backfocusTrustworthy,
            double twistSteps,
            string unavailableReason,
            bool motorsUnchangedSinceRun = false) {
            Mechanism = mechanism;
            StepsPerScrew = stepsPerScrew ?? Array.Empty<double>();
            UnitMicrons = unitMicrons;
            BackfocusTrustworthy = backfocusTrustworthy;
            TwistSteps = twistSteps;
            UnavailableReason = unavailableReason;
            MotorsUnchangedSinceRun = motorsUnchangedSinceRun;
        }

        public TiltRevertMechanism Mechanism { get; }

        /// <summary>Signed motion per screw in WIZARD screw order, in device units. Clockwise/tighten is positive.</summary>
        public IReadOnlyList<double> StepsPerScrew { get; }

        public double UnitMicrons { get; }

        /// <summary>
        /// True only for <see cref="TiltRevertMechanism.DevicePositions"/>. The differential's backfocus term
        /// comes from the paraboloid's curvature parameters, which are its noisiest, and it silently attributes a
        /// spacer or filter change between the two runs to the adapter — so the caller defaults backfocus OFF on
        /// that path and lets the user opt in.
        /// </summary>
        public bool BackfocusTrustworthy { get; }

        /// <summary>
        /// The component of the requested motion no rigid plane can produce. Two genuinely-reached device states
        /// differ by a rigid-plane delta, so a nonzero twist on the positions path is itself diagnostic: it means
        /// position tracking drifted (lost steps, or a resync) since the run.
        /// </summary>
        public double TwistSteps { get; }

        /// <summary>Non-empty exactly when <see cref="Mechanism"/> is Unavailable; drives the disabled-button text.</summary>
        public string UnavailableReason { get; }

        /// <summary>
        /// True when the motor counters are identical to the ones recorded at that run, yet the two fitted models
        /// disagree — so the adapter was moved by something other than these motors. Hand-turned screws on a
        /// motorized rig, a re-seat, the vendor app, or the camera simulator's own tilt controls all do this.
        ///
        /// <para>Diagnostic, and the reason the differential is being used even though a positions snapshot
        /// exists: driving the motors to a position they are already at would do nothing.</para>
        /// </summary>
        public bool MotorsUnchangedSinceRun { get; }

        public bool IsAvailable => Mechanism != TiltRevertMechanism.Unavailable;

        public static TiltRevertTarget Unavailable(string reason)
            => new TiltRevertTarget(TiltRevertMechanism.Unavailable, Array.Empty<double>(), 0.0, false, 0.0, reason);
    }

    /// <summary>
    /// Computes how to put the adapter back the way it was at a past Aberration Inspector run.
    ///
    /// <para>Two mechanisms, in strict precedence:</para>
    /// <list type="number">
    /// <item><b>Device positions.</b> The counters recorded at that run, driven to directly. Exact, and — this is
    /// the important part — it consumes NO calibration: not the screw angles, not the direction sign, not the
    /// pitch. It is raw hardware control toward a recorded number, so it must NOT be gated on the calibration
    /// being device-linked or reliable.</item>
    /// <item><b>Measurement differential.</b> Each run's per-screw targets are a state description ("signed units
    /// from this state to flat"), so the motion from the current state N back to state K is exactly
    /// <c>T(N) − T(K)</c>. Only the two endpoint MEASUREMENTS matter: no assumption that intermediate guidance was
    /// ever applied, no summing, ~zero when nothing changed, and systematic biases shared by both fits largely
    /// cancel. This is the only option for manual screws — and it DOES consume the calibration, so it carries the
    /// same critical gate Automatic Adjustment does. A rotated camera would otherwise send a correction 90° off.</item>
    /// </list>
    ///
    /// <para>Both models are re-evaluated with TODAY's options rather than storing computed targets, so a
    /// re-calibration between the two runs leaves the differential self-consistent under the only calibration we
    /// can actually act on.</para>
    /// </summary>
    public static class TiltRevertPlanFactory {

        public static TiltRevertTarget Build(
                SensorParaboloidTiltHistoryModel target,
                SensorParaboloidModel currentModel,
                ITiltAdapterOptions options,
                IReadOnlyList<int> currentPerMotorSteps,
                bool currentPositionsKnown,
                string connectedPresetName,
                bool calibrationDeviceLinked,
                bool calibrationIsReliable) {
            if (target == null) {
                return TiltRevertTarget.Unavailable("No run is selected.");
            }
            if (options == null) {
                return TiltRevertTarget.Unavailable("The tilt adapter is not configured.");
            }

            var positionsTarget = TryBuildFromPositions(target, options, currentPerMotorSteps, currentPositionsKnown, connectedPresetName);
            if (positionsTarget != null && !IsNegligible(positionsTarget.StepsPerScrew)) {
                return positionsTarget;
            }

            // Counters unchanged does NOT mean the sensor is unchanged. Anything that moves the adapter without
            // going through these motors leaves them exactly where they were: screws turned by hand on a motorized
            // rig, a re-seat, the vendor app, or the camera simulator's own tilt controls. Whether the SENSOR moved
            // is a question about the two fitted models, and it is answerable without any calibration at all --
            // which matters, because the differential below may be gated off.
            bool sensorMoved = positionsTarget != null && SensorMovedBetween(target.SensorModel, currentModel, options);
            if (positionsTarget != null && !sensorMoved) {
                // Motors and sensor both agree nothing meaningful changed. This is the honest "already there".
                return positionsTarget;
            }

            var differentialTarget = BuildFromDifferential(target, currentModel, options, calibrationDeviceLinked, calibrationIsReliable);
            if (!sensorMoved) {
                return differentialTarget;
            }
            if (!differentialTarget.IsAvailable) {
                // The sensor moved without the motors, and we cannot compute by how much. Saying "already at this
                // run's positions" here would be a claim about the sensor that nothing supports.
                return TiltRevertTarget.Unavailable(
                    "The motor counters are unchanged since this run, but the measured tilt is not, so the adapter was moved by "
                    + "something other than these motors. " + differentialTarget.UnavailableReason);
            }

            return new TiltRevertTarget(
                differentialTarget.Mechanism,
                differentialTarget.StepsPerScrew,
                differentialTarget.UnitMicrons,
                differentialTarget.BackfocusTrustworthy,
                differentialTarget.TwistSteps,
                differentialTarget.UnavailableReason,
                motorsUnchangedSinceRun: true);
        }

        /// <summary>
        /// Whether the fitted sensor plane actually moved between two runs, judged as axial displacement at the
        /// screw radius rather than as a bare slope difference, so the threshold means something physical.
        ///
        /// <para>Deliberately calibration-FREE: it reads the screw radius but not the screw angles, the direction
        /// sign or the pitch. That is what lets it still answer "did the sensor move?" on a rig whose calibration
        /// is too unreliable to compute the corresponding move.</para>
        /// </summary>
        private static bool SensorMovedBetween(SensorParaboloidModel historical, SensorParaboloidModel current, ITiltAdapterOptions options) {
            if (historical == null || current == null) {
                return false;
            }
            var radiusMicrons = options.ScrewRadiusMillimeters * 1000.0;
            if (!(radiusMicrons > 0.0)) {
                radiusMicrons = 1.0;   // unconfigured radius: fall back to comparing the raw slopes
            }
            // Half a micron across the sensor radius. Below any real measurement noise, far above float noise.
            const double MovedThresholdMicrons = 0.5;
            var dGx = Math.Abs(current.Gx - historical.Gx) * radiusMicrons;
            var dGy = Math.Abs(current.Gy - historical.Gy) * radiusMicrons;
            return dGx >= MovedThresholdMicrons || dGy >= MovedThresholdMicrons;
        }

        /// <summary>
        /// Whether a motion rounds away to nothing. Uses the same half-step floor the guidance table's formatter
        /// applies, so "no lines to show" and "negligible" can never disagree.
        /// </summary>
        private static bool IsNegligible(IReadOnlyList<double> stepsPerScrew) {
            if (stepsPerScrew == null || stepsPerScrew.Count == 0) {
                return true;
            }
            foreach (var steps in stepsPerScrew) {
                if (Math.Abs(steps) >= 0.5) {
                    return false;
                }
            }
            return true;
        }

        // Returns null (rather than an Unavailable) when this mechanism simply does not apply, so the caller falls
        // through to the differential instead of reporting a hard failure.
        private static TiltRevertTarget TryBuildFromPositions(
                SensorParaboloidTiltHistoryModel target,
                ITiltAdapterOptions options,
                IReadOnlyList<int> currentPerMotorSteps,
                bool currentPositionsKnown,
                string connectedPresetName) {
            if (options.AdjustmentType != TiltAdjustmentType.StepperMotors) {
                return null;
            }
            if (target.AdapterState?.HasPositions != true || !currentPositionsKnown) {
                return null;
            }
            // Motor counters are per-device: a snapshot taken against another preset is not a target, it is a
            // different machine's numbers.
            if (!string.Equals(target.AdapterState.DevicePresetName, connectedPresetName, StringComparison.Ordinal)) {
                return null;
            }

            var delta = TiltDeviceTargetMath.DeltaPerScrew(target.AdapterState.PerMotorSteps, currentPerMotorSteps);
            if (delta == null) {
                return null;
            }

            return new TiltRevertTarget(
                TiltRevertMechanism.DevicePositions,
                delta,
                options.StepperStepSizeMicrons,
                backfocusTrustworthy: true,
                twistSteps: TiltDeviceTargetMath.TwistSteps(delta),
                unavailableReason: null);
        }

        private static TiltRevertTarget BuildFromDifferential(
                SensorParaboloidTiltHistoryModel target,
                SensorParaboloidModel currentModel,
                ITiltAdapterOptions options,
                bool calibrationDeviceLinked,
                bool calibrationIsReliable) {
            if (target.SensorModel == null) {
                return TiltRevertTarget.Unavailable("This run has no fitted sensor model to compare against.");
            }
            if (currentModel == null) {
                return TiltRevertTarget.Unavailable("There is no current measurement to compare against. Run the Aberration Inspector first.");
            }
            // The differential consumes the screw angles and the direction sign, so it needs the same gate
            // Automatic Adjustment does. The positions path above deliberately skipped it.
            if (options.AdjustmentType == TiltAdjustmentType.StepperMotors && (!calibrationDeviceLinked || !calibrationIsReliable)) {
                return TiltRevertTarget.Unavailable(
                    "No motor positions were recorded for this run, and the calibration is not linked to the connected device (or is not reliable), so the moves cannot be computed.");
            }

            double[] currentTargets;
            double[] historicalTargets;
            double unitMicrons;
            try {
                (currentTargets, unitMicrons) = InspectorVM.BuildPerScrewTargets(currentModel, options);
                (historicalTargets, _) = InspectorVM.BuildPerScrewTargets(target.SensorModel, options);
            } catch (Exception ex) {
                // Never let a configuration problem escape as an exception from a command's canExecute path.
                return TiltRevertTarget.Unavailable(ex.Message);
            }

            var delta = new double[4];
            for (int i = 0; i < 4; i++) {
                delta[i] = currentTargets[i] - historicalTargets[i];
            }

            return new TiltRevertTarget(
                TiltRevertMechanism.MeasurementDifferential,
                delta,
                unitMicrons,
                backfocusTrustworthy: false,
                twistSteps: TiltDeviceTargetMath.TwistSteps(delta),
                unavailableReason: null);
        }
    }
}
