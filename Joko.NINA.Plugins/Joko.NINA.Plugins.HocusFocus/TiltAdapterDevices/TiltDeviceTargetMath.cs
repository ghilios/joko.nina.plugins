#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices {

    /// <summary>
    /// Shared arithmetic for driving the adapter toward a set of ABSOLUTE per-motor positions.
    ///
    /// <para>The only interesting thing this does is convert between index spaces: device positions arrive in
    /// DEVICE MOTOR order (<see cref="ITiltMotionController.LastKnownPositions"/>,
    /// <c>TiltDeviceConnectionService.CurrentPositions</c>) while the planner and
    /// <c>TiltAdapterMove.PerCornerSteps</c> work in WIZARD SCREW order. Hand-converting between them is called
    /// out in <see cref="TiltAdapterCorner"/> as "the single most repeated bug in this feature", so both the
    /// manual Target-Positions panel and the Inspector's return-to-a-past-run path resolve it here rather than
    /// each writing the loop out again.</para>
    /// </summary>
    public static class TiltDeviceTargetMath {

        /// <summary>
        /// <paramref name="targetPerMotor"/> − <paramref name="currentPerMotor"/>, converted from DEVICE motor
        /// order into WIZARD screw order — the planner's <c>sPerScrew</c> space. Returns null when either input
        /// is null or does not hold exactly four motors, so callers can treat "unknown positions" and "invalid
        /// targets" identically.
        /// </summary>
        public static double[] DeltaPerScrew(IReadOnlyList<int> targetPerMotor, IReadOnlyList<int> currentPerMotor) {
            if (targetPerMotor == null || currentPerMotor == null || targetPerMotor.Count != 4 || currentPerMotor.Count != 4) {
                return null;
            }

            var delta = new double[4];
            for (int wizardIndex = 0; wizardIndex < 4; ++wizardIndex) {
                int deviceIndex = TiltAdapterCorner.InWizardScrewOrder[wizardIndex].DeviceMotorNumber - 1;
                delta[wizardIndex] = targetPerMotor[deviceIndex] - currentPerMotor[deviceIndex];
            }
            return delta;
        }

        /// <summary>
        /// The component of a wizard-order delta that no rigid plane can produce, in steps. The adapter can tilt
        /// the sensor and change its spacing but cannot twist it, so a nonzero value means the four requested
        /// positions are not simultaneously reachable and the device will land on the plane projection instead.
        /// Returns 0 for a malformed delta — the caller has already failed a null check by then.
        /// </summary>
        public static double TwistSteps(IReadOnlyList<double> deltaPerScrew) {
            if (deltaPerScrew == null || deltaPerScrew.Count != 4) {
                return 0.0;
            }

            return TiltMovePlanner.Decompose(deltaPerScrew).t;
        }
    }
}
