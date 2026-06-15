#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// The domain of a single tunable knob.
    ///   Continuous — any real value within [Lower, Upper].
    ///   Integer    — whole values within [Lower, Upper] (the optimizer rounds away from zero).
    ///   Boolean    — represented as 0/1 with a 0.5 threshold; bounds are always [0, 1].
    /// </summary>
    public enum OptimizerVariableType {
        Continuous,
        Integer,
        Boolean
    }

    /// <summary>
    /// Descriptor for one star-detection parameter that the optimizer is allowed to tune. Every value flows
    /// through this descriptor as a <c>double</c> so the search engine (T2) can stay parameter-agnostic: it
    /// reads the seed value via <see cref="Read"/>, proposes a new double, and writes it back via
    /// <see cref="Write"/>, which always clamps to [<see cref="Lower"/>, <see cref="Upper"/>] and quantizes
    /// per <see cref="Type"/> (round Integers, threshold Booleans). The descriptor is pure data + delegates;
    /// it performs no IO and runs no detection.
    /// </summary>
    public sealed class OptimizerVariable {
        /// <summary>
        /// The matching <see cref="StarDetectorParams"/> property name (e.g. "Sensitivity"), or a synthetic
        /// alias when one variable drives multiple params (see <see cref="DefocusAwareGatesName"/>).
        /// </summary>
        public string Name { get; init; }

        public OptimizerVariableType Type { get; init; }

        /// <summary>Inclusive lower bound (for Boolean, 0).</summary>
        public double Lower { get; init; }

        /// <summary>Inclusive upper bound (for Boolean, 1).</summary>
        public double Upper { get; init; }

        /// <summary>The initial pattern-search step for this axis (in the variable's own units).</summary>
        public double InitialStep { get; init; }

        /// <summary>Reads the current value from a params bundle as a double (Boolean -> 0/1).</summary>
        public Func<StarDetectorParams, double> Read { get; init; }

        /// <summary>
        /// Writes a value into a params bundle. The incoming double is clamped to [Lower, Upper] and quantized
        /// per <see cref="Type"/> before being stored, so callers may pass raw proposals freely.
        /// </summary>
        public Action<StarDetectorParams, double> Write { get; init; }

        /// <summary>Clamps a raw value to the inclusive bounds.</summary>
        public double Clamp(double v) {
            if (v < Lower) {
                return Lower;
            }
            if (v > Upper) {
                return Upper;
            }
            return v;
        }

        /// <summary>
        /// Quantizes a raw value to a legal value of this variable's type, then clamps to bounds:
        ///   Integer  — Math.Round(v, AwayFromZero);
        ///   Boolean  — v >= 0.5 ? 1 : 0;
        ///   Continuous — identity.
        /// </summary>
        public double Quantize(double v) {
            switch (Type) {
                case OptimizerVariableType.Integer:
                    return Clamp(Math.Round(v, MidpointRounding.AwayFromZero));
                case OptimizerVariableType.Boolean:
                    return Clamp(v >= 0.5 ? 1.0 : 0.0);
                default:
                    return Clamp(v);
            }
        }

        /// <summary>
        /// The 13 curated tunable variables. Bounds/initial steps follow the validation ranges in
        /// StarDetectionOptions.cs where a UI range exists; where a range is open-ended the bound is a
        /// HEURISTIC (pragmatic, easily editable) value — see the named constants below.
        ///
        /// Bounds are defined exactly once per variable (in <see cref="Lower"/>/<see cref="Upper"/>); every
        /// <see cref="Write"/> delegate routes its proposal through that same descriptor's own
        /// <see cref="Quantize"/>, so there is a single source of truth and the lambdas can never drift from
        /// the published bounds. The descriptor properties are init-only, so the bounds the lambda quantizes
        /// against are exactly the ones returned by <see cref="Lower"/>/<see cref="Upper"/>.
        /// </summary>
        public static IReadOnlyList<OptimizerVariable> CreateCuratedSet() {
            // --- Heuristic bounds (no hard UI validation range; chosen pragmatically). Edit here to retune. ---
            const double SensitivityLower = 0.0;       // heuristic
            const double SensitivityUpper = 50.0;      // heuristic; widened 20 -> 50 because rich fields pinned the old 20 ceiling
            const double StarClipLower = 0.25;         // heuristic; widened 0.5 -> 0.25 (a setup pinned the old 0.5 floor)
            const double StarClipUpper = 10.0;         // heuristic; widened 5 -> 10 because rich fields pinned the old 5 ceiling
            const double NoiseClipLower = 1.0;         // heuristic
            const double NoiseClipUpper = 10.0;        // heuristic
            const double MinHFRLower = 0.1;            // heuristic
            const double MinHFRUpper = 5.0;            // heuristic
            const double HotpixelThresholdLower = 0.0001; // heuristic
            const double HotpixelThresholdUpper = 0.05;   // heuristic
            const int StructureLayersLower = 1;        // heuristic
            const int StructureLayersUpper = 8;        // heuristic
            const int NoiseReductionRadiusLower = 0;   // heuristic
            const int NoiseReductionRadiusUpper = 10;  // heuristic
            const int MinBoundingBoxLower = 2;         // heuristic
            const int MinBoundingBoxUpper = 20;        // heuristic

            return new List<OptimizerVariable> {
                Continuous(nameof(StarDetectorParams.Sensitivity), SensitivityLower, SensitivityUpper, 1.0,
                    p => p.Sensitivity, (p, v) => p.Sensitivity = v),
                Continuous(nameof(StarDetectorParams.StarClippingMultiplier), StarClipLower, StarClipUpper, 0.5,
                    p => p.StarClippingMultiplier, (p, v) => p.StarClippingMultiplier = v),
                Continuous(nameof(StarDetectorParams.NoiseClippingMultiplier), NoiseClipLower, NoiseClipUpper, 0.5,
                    p => p.NoiseClippingMultiplier, (p, v) => p.NoiseClippingMultiplier = v),
                Continuous(nameof(StarDetectorParams.PeakResponse), 0.1, 1.0, 0.05,
                    p => p.PeakResponse, (p, v) => p.PeakResponse = v),
                Continuous(nameof(StarDetectorParams.MaxDistortion), 0.1, 1.0, 0.1,
                    p => p.MaxDistortion, (p, v) => p.MaxDistortion = v),
                Continuous(nameof(StarDetectorParams.MinHFR), MinHFRLower, MinHFRUpper, 0.25,
                    p => p.MinHFR, (p, v) => p.MinHFR = v),
                Continuous(nameof(StarDetectorParams.StarCenterTolerance), 0.05, 1.0, 0.05,
                    p => p.StarCenterTolerance, (p, v) => p.StarCenterTolerance = v),
                IntegerVar(nameof(StarDetectorParams.StructureLayers), StructureLayersLower, StructureLayersUpper, 1,
                    p => p.StructureLayers, (p, v) => p.StructureLayers = v),
                IntegerVar(nameof(StarDetectorParams.NoiseReductionRadius), NoiseReductionRadiusLower, NoiseReductionRadiusUpper, 1,
                    p => p.NoiseReductionRadius, (p, v) => p.NoiseReductionRadius = v),
                IntegerVar(nameof(StarDetectorParams.MinimumStarBoundingBoxSize), MinBoundingBoxLower, MinBoundingBoxUpper, 1,
                    p => p.MinimumStarBoundingBoxSize, (p, v) => p.MinimumStarBoundingBoxSize = v),
                BooleanVar(nameof(StarDetectorParams.HotpixelThresholdingEnabled), 1,
                    p => p.HotpixelThresholdingEnabled, (p, b) => p.HotpixelThresholdingEnabled = b),
                Continuous(nameof(StarDetectorParams.HotpixelThreshold), HotpixelThresholdLower, HotpixelThresholdUpper, 0.001,
                    p => p.HotpixelThreshold, (p, v) => p.HotpixelThreshold = v),
                // F3: a single combined switch that flips BOTH defocus-aware gates together, so the optimizer can
                // explore the defocus relaxation (recovering large/donut defocused stars) as one knob. The Name is
                // a SYNTHETIC alias (not a StarDetectorParams property): Read reports the distortion flag (the two
                // are written in lockstep), Write sets distortion AND centering to the same value. The seed reads
                // the current params (both OFF by default), so the baseline is unchanged; the search may flip it on.
                // Size-reference tuning is intentionally NOT exposed as a variable for now — only this flag.
                BooleanVar(DefocusAwareGatesName, 1,
                    p => p.DefocusAwareDistortion,
                    (p, en) => { p.DefocusAwareDistortion = en; p.DefocusAwareCentering = en; }),
            };
        }

        /// <summary>Synthetic curated-set variable name for the combined defocus-aware-gates switch. It is NOT a
        /// <see cref="StarDetectorParams"/> property name (the variable drives two properties at once), so it is a
        /// named constant rather than a <c>nameof</c>.</summary>
        public const string DefocusAwareGatesName = "DefocusAwareGates";

        /// <summary>
        /// Builds a Continuous variable whose Write quantizes the proposal through the variable's OWN
        /// <see cref="Quantize"/> (single source of bounds) before storing it via <paramref name="store"/>.
        /// </summary>
        private static OptimizerVariable Continuous(
            string name, double lower, double upper, double step,
            Func<StarDetectorParams, double> read, Action<StarDetectorParams, double> store) {
            OptimizerVariable v = null;
            v = new OptimizerVariable {
                Name = name,
                Type = OptimizerVariableType.Continuous,
                Lower = lower, Upper = upper, InitialStep = step,
                Read = read,
                Write = (p, raw) => store(p, v.Quantize(raw))
            };
            return v;
        }

        /// <summary>
        /// Builds an Integer variable whose Write quantizes (round-away-from-zero + clamp) the proposal through
        /// the variable's OWN <see cref="Quantize"/>, then stores the whole-number result as an int.
        /// </summary>
        private static OptimizerVariable IntegerVar(
            string name, int lower, int upper, double step,
            Func<StarDetectorParams, int> read, Action<StarDetectorParams, int> store) {
            OptimizerVariable v = null;
            v = new OptimizerVariable {
                Name = name,
                Type = OptimizerVariableType.Integer,
                Lower = lower, Upper = upper, InitialStep = step,
                Read = p => read(p),
                Write = (p, raw) => store(p, (int)v.Quantize(raw))
            };
            return v;
        }

        /// <summary>
        /// Builds a Boolean variable (bounds [0, 1]) whose Write thresholds the proposal through the variable's
        /// OWN <see cref="Quantize"/> (>= 0.5) before storing the bool via <paramref name="store"/>.
        /// </summary>
        private static OptimizerVariable BooleanVar(
            string name, double step,
            Func<StarDetectorParams, bool> read, Action<StarDetectorParams, bool> store) {
            OptimizerVariable v = null;
            v = new OptimizerVariable {
                Name = name,
                Type = OptimizerVariableType.Boolean,
                Lower = 0, Upper = 1, InitialStep = step,
                Read = p => read(p) ? 1.0 : 0.0,
                Write = (p, raw) => store(p, v.Quantize(raw) >= 0.5)
            };
            return v;
        }
    }
}
