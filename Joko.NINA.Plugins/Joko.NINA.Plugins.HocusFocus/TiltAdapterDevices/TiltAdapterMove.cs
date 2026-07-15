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

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices {

    /// <summary>
    /// The five independent single-command move axes a 4-corner coupled tilt device (e.g. the ASG EAT)
    /// can execute. Each axis corresponds to one of the orthogonal generators in the minimal-move
    /// decomposition (see the design doc's "Core algorithm" section): DiagonalA/DiagonalB are the two
    /// corner-pair generators (D1, D2), EdgeVertical/EdgeHorizontal are their sum/difference (E+, E-),
    /// and Backfocus is the uniform generator (BF).
    /// </summary>
    public enum TiltMoveAxis {
        DiagonalA,
        DiagonalB,
        EdgeVertical,
        EdgeHorizontal,
        Backfocus
    }

    /// <summary>
    /// Which approval-dialog checkbox group a move belongs to (user decision #2: Tilt / Backfocus are
    /// independently toggleable). Backfocus is exactly the <see cref="TiltMoveAxis.Backfocus"/> axis;
    /// every other axis is Tilt.
    /// </summary>
    public enum TiltMoveGroup {
        Tilt,
        Backfocus
    }

    /// <summary>
    /// A single, immutable, ready-to-send device move along one <see cref="TiltMoveAxis"/>. The
    /// per-corner effect (in wizard screw indices 1..4, at array positions [0..3]) is computed from the
    /// fixed unit effect-vector table in <see cref="UnitEffect"/> — it is impossible to construct a
    /// <see cref="TiltAdapterMove"/> whose <see cref="PerCornerSteps"/> disagrees with its
    /// <see cref="Axis"/>/<see cref="Steps"/>. Mirrors the constructor-assigned, get-only immutability
    /// style of <c>TiltAdapterDevicePreset</c>.
    /// </summary>
    public sealed class TiltAdapterMove {

        // Unit effect vectors in (s1, s2, s3, s4) wizard-screw-index space — THE correctness anchor for
        // every device move this feature ever sends. Do not edit without re-verifying against the design
        // doc's generator table; a sign error here is an EEPROM-persisted wrong-way motor move.
        //   DiagonalA      : (+1,  0, -1,  0)
        //   DiagonalB      : ( 0, +1,  0, -1)
        //   EdgeVertical   : (+1, +1, -1, -1)
        //   EdgeHorizontal : (+1, -1, -1, +1)
        //   Backfocus      : (+1, +1, +1, +1)
        // Wrapped via Array.AsReadOnly (not copied) so every call to UnitEffect returns a view over the
        // same immutable table; ReadOnlyCollection<T> throws on any mutating IList<T> member, so external
        // code cannot corrupt the table even by downcasting the returned IReadOnlyList<double>.
        private static readonly IReadOnlyDictionary<TiltMoveAxis, ReadOnlyCollection<double>> UnitEffectVectors =
            new Dictionary<TiltMoveAxis, ReadOnlyCollection<double>> {
                [TiltMoveAxis.DiagonalA] = Array.AsReadOnly(new double[] { 1, 0, -1, 0 }),
                [TiltMoveAxis.DiagonalB] = Array.AsReadOnly(new double[] { 0, 1, 0, -1 }),
                [TiltMoveAxis.EdgeVertical] = Array.AsReadOnly(new double[] { 1, 1, -1, -1 }),
                [TiltMoveAxis.EdgeHorizontal] = Array.AsReadOnly(new double[] { 1, -1, -1, 1 }),
                [TiltMoveAxis.Backfocus] = Array.AsReadOnly(new double[] { 1, 1, 1, 1 }),
            };

        /// <summary>
        /// The unit per-corner effect vector for one step of the given axis, in wizard screw indices
        /// 1..4 at array positions [0..3]. See the class-level table for the exact values.
        /// </summary>
        public static IReadOnlyList<double> UnitEffect(TiltMoveAxis axis) {
            if (!UnitEffectVectors.TryGetValue(axis, out var vector)) {
                throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown tilt move axis.");
            }
            return vector;
        }

        public TiltAdapterMove(TiltMoveAxis axis, int steps, TiltMoveGroup group, string description) {
            Axis = axis;
            Steps = steps;
            Group = group;
            Description = description ?? string.Empty;

            var effect = UnitEffect(axis);
            var perCornerSteps = new double[effect.Count];
            for (int i = 0; i < effect.Count; ++i) {
                perCornerSteps[i] = effect[i] * steps;
            }
            PerCornerSteps = Array.AsReadOnly(perCornerSteps);
        }

        public TiltMoveAxis Axis { get; }

        /// <summary>Signed magnitude of the move along <see cref="Axis"/> (e.g. the "N" in <c>tr,N</c>).</summary>
        public int Steps { get; }

        public TiltMoveGroup Group { get; }

        /// <summary>Human-readable description of the move, for the approval dialog / status text.</summary>
        public string Description { get; }

        /// <summary>
        /// Per-corner signed step effect of this move, wizard screw indices 1..4 at [0..3]. Always equal
        /// to <c>UnitEffect(Axis)[i] * Steps</c> — computed once in the constructor, never settable.
        /// </summary>
        public IReadOnlyList<double> PerCornerSteps { get; }
    }
}
