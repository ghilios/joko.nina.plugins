#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter {

    /// <summary>
    /// The seam <c>SimulatedEatTransport</c> drives to make the camera simulator behave like a connected ASG
    /// EAT. It is the software stand-in for the four physical motors: apply a move (in the wizard's screw-index
    /// order, the order <see cref="TiltAdapterDevices.TiltAdapterMove.PerCornerSteps"/> uses) and read back the
    /// per-motor step counters (in the device's motor order, the order a real <c>cp</c> reply uses).
    /// </summary>
    public interface ISimulatedTiltActuator {

        /// <summary>
        /// Applies a move expressed as per-screw signed step counts in WIZARD screw-index order (screws 1..4 at
        /// [0..3]) — exactly a <see cref="TiltAdapterDevices.TiltAdapterMove.PerCornerSteps"/> vector. Advances
        /// the per-motor counters and folds the resulting best-focus-surface change into the simulator's injected
        /// aberration so the next simulated exposure reflects it.
        /// </summary>
        void ApplyWizardScrewSteps(IReadOnlyList<double> wizardOrderSteps);

        /// <summary>
        /// The cumulative per-motor step counters in DEVICE motor order (motor 1..4 = TR, TL, BR, BL at [0..3]),
        /// i.e. the order a real EAT <c>cp</c> reply reports and the order the controller's shadow tracking and
        /// the wizard's position display both expect. Always a 4-element array.
        /// </summary>
        int[] GetPerMotorPositions();
    }
}
