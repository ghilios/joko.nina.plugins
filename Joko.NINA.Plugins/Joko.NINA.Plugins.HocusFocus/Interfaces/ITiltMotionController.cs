#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    /// <summary>
    /// The rigid-body topology a motorized tilt device implements. Determines which planner (see the
    /// design doc's "3-corner extensibility" note) can drive it: today only <see cref="FourCornerCoupled"/>
    /// (the ASG EAT) is implemented; <see cref="ThreeCornerIndependent"/> is a placeholder for a future
    /// device/planner pair where each screw can be commanded independently.
    /// </summary>
    public enum TiltDeviceTopology {
        FourCornerCoupled,
        ThreeCornerIndependent
    }

    /// <summary>
    /// Immutable description of what a connected <see cref="ITiltMotionController"/> can do. Constructed
    /// once by the driver (typically from its <c>Capabilities</c> property) and never mutated. Mirrors the
    /// constructor-assigned, get-only immutability style of <c>TiltAdapterDevicePreset</c>; the incoming
    /// axis list is defensively copied so the exposed <see cref="SupportedAxes"/> can't be mutated out from
    /// under the driver after construction.
    /// </summary>
    public sealed class TiltDeviceCapabilities {

        public TiltDeviceCapabilities(TiltDeviceTopology topology, IReadOnlyList<TiltMoveAxis> supportedAxes, bool supportsPerScrewIndependent) {
            Topology = topology;
            SupportedAxes = new ReadOnlyCollection<TiltMoveAxis>((supportedAxes ?? Array.Empty<TiltMoveAxis>()).ToArray());
            SupportsPerScrewIndependent = supportsPerScrewIndependent;
        }

        public TiltDeviceTopology Topology { get; }

        /// <summary>The device move axes (see <see cref="TiltMoveAxis"/>) this device accepts commands on.</summary>
        public IReadOnlyList<TiltMoveAxis> SupportedAxes { get; }

        /// <summary>
        /// True if the device can move a single screw/motor independently of the others (a 3-corner
        /// independent device). The ASG EAT is <c>FourCornerCoupled</c> and declares this <c>false</c> —
        /// every command moves at least a diagonal or edge pair.
        /// </summary>
        public bool SupportsPerScrewIndependent { get; }
    }

    /// <summary>
    /// Immutable snapshot of a motorized tilt device's per-motor position counters, as returned by a
    /// position query (e.g. the ASG EAT's <c>cp</c> command). Device motor order 1..4 = TR, TL, BR, BL
    /// (see the design doc's "Device corner labels" note — this is the DEVICE's own numbering, distinct
    /// from the wizard's screw indices used elsewhere in this feature). Mirrors the constructor-assigned,
    /// get-only immutability style of <c>TiltAdapterDevicePreset</c>.
    /// </summary>
    public sealed class TiltDevicePositions {

        public TiltDevicePositions(IReadOnlyList<int> perMotorSteps, bool known) {
            PerMotorSteps = new ReadOnlyCollection<int>((perMotorSteps ?? Array.Empty<int>()).ToArray());
            Known = known;
        }

        /// <summary>Per-motor absolute step counters in DEVICE motor order 1..4 (TR, TL, BR, BL) at [0..3].</summary>
        public IReadOnlyList<int> PerMotorSteps { get; }

        /// <summary>
        /// False when the device's positions could not be determined (e.g. the response protocol is not
        /// yet understood, or the query failed) — callers must not assume <see cref="PerMotorSteps"/> is
        /// meaningful unless this is true.
        /// </summary>
        public bool Known { get; }

        /// <summary>Convenience singleton for "no position information available": empty list, <see cref="Known"/> false.</summary>
        public static TiltDevicePositions Unknown { get; } = new TiltDevicePositions(Array.Empty<int>(), known: false);
    }

    /// <summary>
    /// Abstraction over a connected motorized tilt-adapter device (e.g. the ASG EAT). Implementations own
    /// the transport (serial or otherwise), command formatting, and any device-specific limits/shadow
    /// position tracking. Deliberately has NO zero-positions member: per the design doc's user decision
    /// #5, zeroing screw/motor position counters is the vendor app's job — the plugin never zeroes.
    /// </summary>
    public interface ITiltMotionController : INotifyPropertyChanged {

        /// <summary>True while connected to the device. Implementations raise <see cref="INotifyPropertyChanged.PropertyChanged"/> for this property.</summary>
        bool Connected { get; }

        /// <summary>What this device can do (topology, supported move axes, per-screw independence).</summary>
        TiltDeviceCapabilities Capabilities { get; }

        /// <summary>Opens the connection to the device over the given port (e.g. "COM3").</summary>
        Task ConnectAsync(string portName, CancellationToken ct);

        /// <summary>Closes the connection to the device.</summary>
        Task DisconnectAsync(CancellationToken ct);

        /// <summary>
        /// Sends the given move to the device and awaits its completion. <paramref name="progress"/>
        /// receives human-readable status text (e.g. "Move 1 of 3: tr,+50") suitable for direct display.
        /// </summary>
        Task ExecuteMoveAsync(TiltAdapterMove move, IProgress<string> progress, CancellationToken ct);

        /// <summary>Queries the device's current per-motor positions.</summary>
        Task<TiltDevicePositions> QueryPositionsAsync(CancellationToken ct);

        /// <summary>
        /// The per-motor counters as of the most recent device interaction that reported them — a parsed
        /// position query, or the position block a move response embeds — with NO further I/O.
        /// <see cref="TiltDevicePositions.Unknown"/> until the device has confirmed positions at least once.
        ///
        /// <para>This is what lets a caller executing a multi-move plan (a calibration run, an Automatic
        /// Adjustment) show live counters between moves. Position polling is suspended for the whole lifetime
        /// of that plan's exclusive operation lease, and a follow-up query per move would be both a wasted
        /// round trip and an extra failure point — the device already reported its new positions as part of
        /// the move it just acknowledged.</para>
        /// </summary>
        TiltDevicePositions LastKnownPositions { get; }

        /// <summary>
        /// Orders <paramref name="moves"/> (a small plan) to minimize the peak per-motor excursion across
        /// every intermediate state, starting from the device's current (shadow-tracked) position.
        /// Pure/side-effect-free: does not touch device state and sends nothing. Callers (e.g. the
        /// Aberration Inspector's Automatic Adjustment) call this once to decide send order via the
        /// interface -- never by downcasting to a concrete driver -- then invoke
        /// <see cref="ExecuteMoveAsync"/> for each move in the returned order (whose own per-move
        /// validation remains the defensive, authoritative check). Throws
        /// <see cref="NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat.TiltDeviceLimitException"/> if
        /// any move exceeds the per-command cap (no ordering can fix that) or if even the best ordering's
        /// peak excursion exceeds the configured max -- in either case nothing is sent.
        /// </summary>
        IReadOnlyList<TiltAdapterMove> OrderForMinimalPeakExcursion(IReadOnlyList<TiltAdapterMove> moves);
    }
}
