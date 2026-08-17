#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    /// <summary>
    /// What the tilt adapter looked like at the moment an Aberration Inspector run was measured, so the user can
    /// later ask to be put back there.
    ///
    /// <para>Lives beside <see cref="TiltDevicePositions"/> rather than in the device layer so
    /// <c>Inspection.SensorModel</c> — a math class — can carry one without gaining any dependency on the tilt
    /// device.</para>
    ///
    /// <para><see cref="DevicePresetName"/> is load-bearing, not decoration: motor counters are per-device, so a
    /// snapshot taken against one adapter preset must never be used as an absolute target while a different one
    /// is connected.</para>
    /// </summary>
    public sealed class TiltAdapterStateSnapshot {

        public TiltAdapterStateSnapshot(DateTime capturedUtc, IReadOnlyList<int> perMotorSteps, bool positionsKnown, string devicePresetName) {
            CapturedUtc = capturedUtc;
            // Defensive copy: the caller hands us the service's live list, which the next poll replaces.
            PerMotorSteps = perMotorSteps == null ? Array.Empty<int>() : perMotorSteps.ToArray();
            PositionsKnown = positionsKnown;
            DevicePresetName = devicePresetName;
        }

        /// <summary>When the run was measured. The history grid had no timestamp at all before this.</summary>
        public DateTime CapturedUtc { get; }

        /// <summary>Per-motor counters in DEVICE motor order (1..4 = TR, TL, BR, BL). Empty when unknown.</summary>
        public IReadOnlyList<int> PerMotorSteps { get; }

        public bool PositionsKnown { get; }

        /// <summary>The adapter preset that was connected, or null/empty when nothing was.</summary>
        public string DevicePresetName { get; }

        /// <summary>True only when these positions can actually be used as a return target.</summary>
        public bool HasPositions => PositionsKnown && PerMotorSteps.Count == 4;

        /// <summary>A timestamped snapshot with no usable positions — no device connected, or positions unknown.</summary>
        public static TiltAdapterStateSnapshot Unknown(DateTime capturedUtc, string devicePresetName)
            => new TiltAdapterStateSnapshot(capturedUtc, Array.Empty<int>(), positionsKnown: false, devicePresetName);
    }
}
