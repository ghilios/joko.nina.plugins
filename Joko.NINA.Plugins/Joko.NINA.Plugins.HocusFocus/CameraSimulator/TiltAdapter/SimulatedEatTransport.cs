#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter {

    /// <summary>
    /// A drop-in <see cref="IEatTransport"/> that speaks the ASG EAT command vocabulary but drives a
    /// <see cref="ISimulatedTiltActuator"/> (the camera simulator) instead of a serial port. It lets the
    /// entire, UNCHANGED EAT stack — <c>EatTiltMotionController</c> (limits, shadow tracking, wizard↔device
    /// permutation), <c>EatCommands</c>, <c>EatResponses</c> — run against software, closing the calibration
    /// loop with no hardware. Because it produces clean, canonical responses the tolerant parsers already
    /// accept, it needs none of the T15 live-capture wire-format knowledge.
    ///
    /// <para><c>cp</c> is answered with the actuator's four device-order counters as a comma-separated line
    /// (which <see cref="EatResponses.ParseCpPositions"/> reads as the first four integers). Every move command
    /// is decoded with <see cref="EatCommands.TryParse"/> back into its axis + steps, turned into the wizard-
    /// order per-corner effect (<see cref="TiltAdapterMove.UnitEffect"/> × steps — the exact vector the
    /// controller formatted from), applied to the actuator, and acknowledged (an exchange that did not time out,
    /// which <see cref="EatResponses.ParseMoveAck"/> treats as success).</para>
    /// </summary>
    public sealed class SimulatedEatTransport : IEatTransport {
        private readonly ISimulatedTiltActuator actuator;
        private bool isOpen;

        public SimulatedEatTransport(ISimulatedTiltActuator actuator) {
            this.actuator = actuator ?? throw new ArgumentNullException(nameof(actuator));
        }

        public bool IsOpen => isOpen;

        public Task OpenAsync(string portName, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            isOpen = true; // The port string is the "Simulator" sentinel; there is no real port to open.
            return Task.CompletedTask;
        }

        public void Close() => isOpen = false;

        public Task<EatRawExchange> SendAsync(string command, TimeSpan timeout, CancellationToken ct) {
            if (command == null) {
                throw new ArgumentNullException(nameof(command));
            }
            ct.ThrowIfCancellationRequested();

            if (command == EatCommands.PositionQuery()) {
                var positions = actuator.GetPerMotorPositions();
                var line = string.Join(",", positions.Select(p => p.ToString(CultureInfo.InvariantCulture)));
                return Task.FromResult(new EatRawExchange(command, new[] { line }, timedOut: false));
            }

            if (!EatCommands.TryParse(command, out var axis, out var steps)) {
                // The controller only ever sends 'cp' or EatCommands.Format output, so anything else is a bug.
                throw new ArgumentException(
                    $"SimulatedEatTransport received a command it cannot parse: '{command}'. Only 'cp' and EatCommands.Format output are expected.",
                    nameof(command));
            }

            var effect = TiltAdapterMove.UnitEffect(axis);
            var wizardSteps = new double[effect.Count];
            for (int i = 0; i < wizardSteps.Length; ++i) {
                wizardSteps[i] = effect[i] * steps;
            }
            actuator.ApplyWizardScrewSteps(wizardSteps);

            // Empty, non-timed-out exchange = success ack (EatResponses.ParseMoveAck returns !TimedOut).
            return Task.FromResult(new EatRawExchange(command, Array.Empty<string>(), timedOut: false));
        }
    }
}
