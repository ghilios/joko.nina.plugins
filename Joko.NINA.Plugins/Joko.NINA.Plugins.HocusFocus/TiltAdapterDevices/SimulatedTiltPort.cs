#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices {

    /// <summary>
    /// The synthetic "port" name that stands in for a serial COM port when connecting to the SIMULATED tilt
    /// adapter (the camera simulator) instead of real hardware. It is offered as an entry in the wizard's
    /// COM-port dropdown; <see cref="TiltDeviceConnectionService.ConnectAsync"/> routes it to the simulated
    /// controller factory rather than the serial registry. Kept in one place so the dropdown, the routing, and
    /// the plugin's factory wiring all agree on the exact string.
    /// </summary>
    public static class SimulatedTiltPort {

        /// <summary>The port-dropdown entry that selects the simulated tilt adapter.</summary>
        public const string PortName = "Simulator";

        /// <summary>True iff <paramref name="portName"/> is the simulated-adapter sentinel (exact, case-sensitive).</summary>
        public static bool IsSimulator(string portName) => string.Equals(portName, PortName, StringComparison.Ordinal);
    }
}
