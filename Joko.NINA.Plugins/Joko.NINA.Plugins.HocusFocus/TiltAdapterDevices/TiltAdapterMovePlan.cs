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
using System.Collections.ObjectModel;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices {

    /// <summary>
    /// The immutable result of a minimal-move decomposition: the moves to send, the per-corner residual
    /// (in microns) they leave behind, the unreachable twist component (reported, never planned — see
    /// the design doc's "Core algorithm" section), and an estimated execution time. Pure storage: the
    /// decomposition math itself lives in the planner (a later task). Mirrors the constructor-assigned,
    /// get-only immutability style of <c>TiltAdapterDevicePreset</c>; incoming collections are
    /// defensively copied so the plan can't be mutated out from under an in-flight approval dialog or
    /// executor after construction.
    /// </summary>
    public sealed class TiltAdapterMovePlan {

        public TiltAdapterMovePlan(
            IReadOnlyList<TiltAdapterMove> moves,
            IReadOnlyList<double> residualMicronsPerCorner,
            double twistResidualSteps,
            double estimatedSeconds,
            int biasSteps = 0) {
            Moves = new ReadOnlyCollection<TiltAdapterMove>((moves ?? Array.Empty<TiltAdapterMove>()).ToArray());
            ResidualMicronsPerCorner = new ReadOnlyCollection<double>((residualMicronsPerCorner ?? Array.Empty<double>()).ToArray());
            TwistResidualSteps = twistResidualSteps;
            EstimatedSeconds = estimatedSeconds;
            BiasSteps = biasSteps;
        }

        /// <summary>The ordered, minimal set of device moves to send (≤ 3 before per-move cap splitting).</summary>
        public IReadOnlyList<TiltAdapterMove> Moves { get; }

        /// <summary>
        /// Per-corner residual in microns after the planned moves are applied (applied − target) · 1.8,
        /// wizard screw indices 1..4 at [0..3].
        /// </summary>
        public IReadOnlyList<double> ResidualMicronsPerCorner { get; }

        /// <summary>The twist component (unreachable by any rigid-plane device); reported, never planned.</summary>
        public double TwistResidualSteps { get; }

        /// <summary>Estimated wall-clock execution time for <see cref="Moves"/>, in seconds.</summary>
        public double EstimatedSeconds { get; }

        /// <summary>
        /// Steps of upward backfocus bias prepended to <see cref="Moves"/> so no motor is driven below 0
        /// (0 ⇒ none was needed). A tilt correction is differential — one corner up, the opposite corner
        /// down — so near zero it has to be lifted to fit in the [0, max excursion] travel window. This is
        /// a genuine piston: it shifts backfocus by <c>BiasSteps</c> × step size, which is why it is
        /// reported separately and included in <see cref="ResidualMicronsPerCorner"/> rather than hidden.
        /// </summary>
        public int BiasSteps { get; }
    }
}
