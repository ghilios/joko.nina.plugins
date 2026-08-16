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
            string unavailableReason) {
            Mechanism = mechanism;
            StepsPerScrew = stepsPerScrew ?? Array.Empty<double>();
            UnitMicrons = unitMicrons;
            BackfocusTrustworthy = backfocusTrustworthy;
            TwistSteps = twistSteps;
            UnavailableReason = unavailableReason;
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
            if (positionsTarget != null) {
                return positionsTarget;
            }

            return BuildFromDifferential(target, currentModel, options, calibrationDeviceLinked, calibrationIsReliable);
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
