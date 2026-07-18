#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter {

    /// <summary>
    /// Software stand-in for the ASG EAT's four motors, over the camera simulator. It builds a
    /// <see cref="SimulatedTiltAdapter"/> from the current <c>Sim*</c> options exactly the way
    /// <c>SimulatedTiltAdapterVM.RebuildAdapter</c> does, feeds each move's WIZARD-order per-screw steps
    /// (× the active step size) into <see cref="SimulatedTiltAdapter.ApplyMoves"/>, and folds the result into
    /// the persisted injected aberration through the shared <see cref="SimulatedTiltInjection.Fold"/> — so the
    /// automated calibration loop and the manual panel drive the identical, sign-verified math.
    ///
    /// <para><b>Two index orders, on purpose.</b> Moves come in WIZARD screw order (screws 1..4), which is the
    /// order <see cref="SimulatedTiltAdapter.ApplyMoves"/> and the sim's own screw geometry use. The per-motor
    /// counters returned for <c>cp</c> are kept in DEVICE motor order (TR, TL, BR, BL) via
    /// <see cref="EatTiltMotionController.PermuteWizardToDeviceMotorOrder"/>, matching a real device's reply and
    /// the controller's shadow tracking. The counters are advanced on EVERY move (a real motor turns regardless
    /// of optical effect); the optical fold is skipped only when the sim geometry can't build a valid plane.</para>
    ///
    /// <para><b>Threading.</b> Called from the connection service's background move execution. The injected-
    /// aberration writes are marshalled onto the UI thread via
    /// <see cref="IApplicationDispatcher.DispatchSynchronizationContext(Action)"/> (synchronous, so the fold is
    /// committed before the move returns and the next exposure observes it); the two <c>SimulatedTiltAdapterVM</c>
    /// instances subscribe to <c>CameraSimulatorOptions.PropertyChanged</c>, so the dockable panel reflects
    /// automated moves live without this class ever touching a VM. The counters are guarded by a lock.</para>
    /// </summary>
    public sealed class SimulatedTiltActuator : ISimulatedTiltActuator {
        private readonly ICameraSimulatorOptions options;
        private readonly IApplicationDispatcher dispatcher;
        private readonly object gate = new object();
        private readonly int[] positions = new int[4]; // DEVICE motor order (TR, TL, BR, BL).

        public SimulatedTiltActuator(ICameraSimulatorOptions options, IApplicationDispatcher dispatcher) {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public int[] GetPerMotorPositions() {
            lock (gate) {
                return (int[])positions.Clone();
            }
        }

        public void ApplyWizardScrewSteps(IReadOnlyList<double> wizardOrderSteps) {
            if (wizardOrderSteps == null) {
                throw new ArgumentNullException(nameof(wizardOrderSteps));
            }
            if (wizardOrderSteps.Count != 4) {
                throw new ArgumentException("Expected 4 wizard-order step values (the EAT is a 4-corner adapter).", nameof(wizardOrderSteps));
            }

            // 1) Advance the per-motor counters (device order) unconditionally -- a real motor moves regardless of
            //    whether the sim geometry can render an optical effect, and cp / excursion tracking must stay
            //    faithful to that. PerCornerSteps entries are integer step counts, so the round is exact.
            var deviceDelta = EatTiltMotionController.PermuteWizardToDeviceMotorOrder(wizardOrderSteps);
            lock (gate) {
                for (int i = 0; i < 4; ++i) {
                    positions[i] += (int)Math.Round(deviceDelta[i], MidpointRounding.AwayFromZero);
                }
            }

            // 2) On the UI thread (synchronous, so the next simulated exposure sees it): accumulate the shared
            //    per-screw net counters (so every SimulatedTiltAdapterVM's "Net" strip reflects the automated
            //    move, exactly as manual clicks do), then fold the optical effect into the injected aberration
            //    (a no-op when the geometry can't build a plane).
            dispatcher.DispatchSynchronizationContext(() => {
                AccumulateNet(wizardOrderSteps);
                FoldIntoInjection(wizardOrderSteps);
            });
        }

        // Accumulate the applied per-screw steps into the shared net counters (axial µm), the same counters the
        // manual panel's clicks update and the "Net" strip displays. Runs unconditionally (a real motor moves
        // even when the geometry can't render an optical effect). sim screw i == wizard screw i, so the wizard-
        // order vector maps straight onto the sim-screw-ordered net array.
        private void AccumulateNet(IReadOnlyList<double> wizardOrderSteps) {
            var unit = options.SimAdjustmentType == TiltAdjustmentType.StepperMotors
                ? options.SimStepperStepSizeMicrons
                : options.SimThreadPitchMicrons;
            if (unit <= 0) {
                return; // Can't scale steps to µm; the Net strip shows 0 in this state anyway.
            }
            var net = options.SimNetAxialMicrons;
            for (int i = 0; i < net.Length && i < wizardOrderSteps.Count; i++) {
                net[i] += wizardOrderSteps[i] * unit;
            }
            options.SimNetAxialMicrons = net;
        }

        private void FoldIntoInjection(IReadOnlyList<double> wizardOrderSteps) {
            var adapter = BuildAdapter();
            if (adapter == null || adapter.ScrewCount != wizardOrderSteps.Count) {
                return; // Unbuildable/incompatible geometry -- counters are already advanced above.
            }

            var unit = adapter.UnitMicrons;
            var axialPerScrew = new double[wizardOrderSteps.Count];
            for (int i = 0; i < axialPerScrew.Length; ++i) {
                axialPerScrew[i] = wizardOrderSteps[i] * unit;
            }

            AberrationDelta delta;
            try {
                delta = adapter.ApplyMoves(axialPerScrew);
            } catch (InvalidOperationException) {
                return; // Collinear screw geometry -- same defensive skip the manual panel makes.
            }

            SimulatedTiltInjection.Fold(options, delta, adapter.PistonDirectionSign);
        }

        // Mirrors SimulatedTiltAdapterVM.RebuildAdapter: same angle set, unit selection, mm->µm radius, and the
        // finite/positive guards. Returns null when the geometry can't build a plane.
        private SimulatedTiltAdapter BuildAdapter() {
            var n = options.SimScrewCount == 4 ? 4 : 3;
            var angles = new[] {
                options.SimScrew1AngleDegrees, options.SimScrew2AngleDegrees,
                options.SimScrew3AngleDegrees, options.SimScrew4AngleDegrees
            }.Take(n).ToArray();
            var unit = options.SimAdjustmentType == TiltAdjustmentType.StepperMotors
                ? options.SimStepperStepSizeMicrons
                : options.SimThreadPitchMicrons;
            var radiusMicrons = options.SimScrewRadiusMillimeters * 1000.0;

            if (angles.Any(a => !double.IsFinite(a)) || unit <= 0 || radiusMicrons <= 0) {
                return null;
            }
            return new SimulatedTiltAdapter(angles, radiusMicrons, unit, options.SimScrewInwardCurvatureSign);
        }
    }
}
