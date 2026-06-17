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
        /// The curated tunable variables. Bounds/initial steps follow the validation ranges in
        /// StarDetectionOptions.cs where a UI range exists; where a range is open-ended the bound is a
        /// HEURISTIC (pragmatic, easily editable) value — see the named constants below.
        ///
        /// Bounds are defined exactly once per variable (in <see cref="Lower"/>/<see cref="Upper"/>); every
        /// <see cref="Write"/> delegate routes its proposal through that same descriptor's own
        /// <see cref="Quantize"/>, so there is a single source of truth and the lambdas can never drift from
        /// the published bounds. The descriptor properties are init-only, so the bounds the lambda quantizes
        /// against are exactly the ones returned by <see cref="Lower"/>/<see cref="Upper"/>.
        /// </summary>
        public static IReadOnlyList<OptimizerVariable> CreateCuratedSet(bool defocusRecovery = false) {
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
            const double DefocusSizeRefLower = 15.0;   // heuristic; bbox max-dim (px) above which the defocus gates relax
            const double DefocusSizeRefUpper = 60.0;   // heuristic
            const double DefocusMaxElongationLower = 1.0; // heuristic; 1.0 = admit only perfectly-round donuts
            const double DefocusMaxElongationUpper = 4.0; // heuristic; up to ~4:1 elongation admitted
            const double DefocusMinFactorLower = 0.1;  // heuristic; floor multiplier on MaxDistortion (most permissive)
            const double DefocusMinFactorUpper = 1.0;  // heuristic; 1.0 = no distortion relaxation
            const double DefocusCenterFactorLower = 1.0; // heuristic; 1.0 = no centering relaxation
            const double DefocusCenterFactorUpper = 4.0; // heuristic; max centering-tolerance multiplier

            // The defocus-aware gates switch. In the BASELINE (default) set it drives distortion + centering only —
            // exactly the pre-existing behavior. Under the defocus-recovery OPT-IN it ALSO drives the roundness
            // rescue, and the 4 numeric defocus knobs below are added. Keeping the default set == baseline is what
            // guarantees no AF-curve fit regression for users who don't opt into donut recovery.
            Action<StarDetectorParams, bool> gatesWrite = defocusRecovery
                ? (p, en) => { p.DefocusAwareDistortion = en; p.DefocusAwareCentering = en; p.DefocusRoundnessAdmission = en; }
                : (p, en) => { p.DefocusAwareDistortion = en; p.DefocusAwareCentering = en; };

            var set = new List<OptimizerVariable> {
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
                // Combined defocus-aware-gates switch (a SYNTHETIC alias; Read reports the distortion flag, Write
                // drives the flags in lockstep). Present in BOTH sets — it was curated pre-change, so keeping it is
                // part of the baseline. The roundness coupling differs by mode (see gatesWrite above).
                BooleanVar(DefocusAwareGatesName, 1, p => p.DefocusAwareDistortion, gatesWrite),
                // Defocus-aware STRUCTURE integer knob (EARLY): 0 ⇒ OFF (bit-identical), >0 ⇒ extra wavelet layers.
                // Also pre-change / baseline, so kept in both sets.
                StructureBoostVar(),
            };

            if (defocusRecovery) {
                // OPT-IN ONLY: the LATE gate-tuning knobs. They are excluded from the default set because adding 4
                // extra search dimensions on a fixed eval budget measurably dilutes convergence and regresses the
                // AF-curve fit across the bank. They only take effect while the gates are ON. SizeReference = the
                // bbox max-dim (px) above which a candidate gets the relaxed gates + roundness rescue; MaxElongation
                // = the roundness cap; MinFactor = the floor multiplier on MaxDistortion; CenteringToleranceFactor =
                // how far the NotCentered tolerance grows. All LATE (not in the early cache key) so they reuse context.
                set.Add(Continuous(nameof(StarDetectorParams.DefocusDistortionSizeReference), DefocusSizeRefLower, DefocusSizeRefUpper, 5.0,
                    p => p.DefocusDistortionSizeReference, (p, v) => p.DefocusDistortionSizeReference = v));
                set.Add(Continuous(nameof(StarDetectorParams.DefocusMaxElongation), DefocusMaxElongationLower, DefocusMaxElongationUpper, 0.25,
                    p => p.DefocusMaxElongation, (p, v) => p.DefocusMaxElongation = v));
                set.Add(Continuous(nameof(StarDetectorParams.DefocusDistortionMinFactor), DefocusMinFactorLower, DefocusMinFactorUpper, 0.05,
                    p => p.DefocusDistortionMinFactor, (p, v) => p.DefocusDistortionMinFactor = v));
                set.Add(Continuous(nameof(StarDetectorParams.DefocusCenteringToleranceFactor), DefocusCenterFactorLower, DefocusCenterFactorUpper, 0.25,
                    p => p.DefocusCenteringToleranceFactor, (p, v) => p.DefocusCenteringToleranceFactor = v));
            }
            return set;
        }

        /// <summary>Synthetic curated-set variable name for the combined defocus-aware-gates switch. It is NOT a
        /// <see cref="StarDetectorParams"/> property name (the variable drives two properties at once), so it is a
        /// named constant rather than a <c>nameof</c>.</summary>
        public const string DefocusAwareGatesName = "DefocusAwareGates";

        /// <summary>Synthetic curated-set variable name for the defocus-aware-structure integer knob. Matches the
        /// EARLY cache-key property <c>DefocusAwareStructure</c> so the optimizer stages it as an early axis, but
        /// the variable's Write drives BOTH <see cref="StarDetectorParams.DefocusAwareStructure"/> and
        /// <see cref="StarDetectorParams.StructureLayerBoost"/>.</summary>
        public const string DefocusAwareStructureName = nameof(StarDetectorParams.DefocusAwareStructure);

        /// <summary>
        /// Builds a WARM-START variable set from the curated set and a recommendation (before → after params): for
        /// each curated axis the recommender MOVED, a copy with a tight band ([recommended ± bandSteps·step] clamped
        /// to the original bounds) so the optimizer refines near the analytic point without wandering; axes the
        /// recommender did NOT move are OMITTED (pinned by omission — the warm-start SEED carries their value),
        /// EXCEPT the defocus-aware toggles (<see cref="DefocusAwareGatesName"/> / <see cref="DefocusAwareStructureName"/>),
        /// which are always kept live at full range so the optimizer can enable them if needed (guarded by the
        /// objective's near-focus precision penalty). The caller passes <paramref name="after"/> as the optimizer seed.
        /// </summary>
        public static IReadOnlyList<OptimizerVariable> CreateWarmStartSet(
            IReadOnlyList<OptimizerVariable> baseSet, StarDetectorParams before, StarDetectorParams after, int bandSteps = 3) {
            var result = new List<OptimizerVariable>();
            foreach (var v in baseSet) {
                var beforeVal = v.Read(before);
                var afterVal = v.Read(after);
                var moved = Math.Abs(beforeVal - afterVal) > 1e-9;
                var isDefocusToggle = v.Name == DefocusAwareGatesName || v.Name == DefocusAwareStructureName;
                if (moved) {
                    var half = bandSteps * v.InitialStep;
                    var lo = Math.Max(v.Lower, afterVal - half);
                    var hi = Math.Min(v.Upper, afterVal + half);
                    if (hi <= lo) {
                        hi = Math.Min(v.Upper, lo + Math.Max(v.InitialStep, 1e-9));
                    }
                    result.Add(WithBounds(v, lo, hi));
                } else if (isDefocusToggle) {
                    result.Add(v); // always explorable, full range
                }
                // else: omit — the seed (after) carries the unchanged value.
            }
            return result;
        }

        /// <summary>Copies <paramref name="baseVar"/> with new [<paramref name="lower"/>, <paramref name="upper"/>]
        /// bounds. The new Write re-quantizes through the NEW bounds (so the optimizer cannot escape the band),
        /// then delegates to the original Write (a no-op clamp + the real store, since the new band ⊂ original).</summary>
        private static OptimizerVariable WithBounds(OptimizerVariable baseVar, double lower, double upper) {
            OptimizerVariable v = null;
            v = new OptimizerVariable {
                Name = baseVar.Name,
                Type = baseVar.Type,
                Lower = lower, Upper = upper, InitialStep = baseVar.InitialStep,
                Read = baseVar.Read,
                Write = (p, raw) => baseVar.Write(p, v.Quantize(raw))
            };
            return v;
        }

        /// <summary>Builds the synthetic defocus-aware-structure variable: an Integer boost in [0, 4] that drives
        /// the flag (boost &gt; 0) and the layer count together. 0 reproduces the bit-identical baseline.</summary>
        private static OptimizerVariable StructureBoostVar() {
            OptimizerVariable v = null;
            v = new OptimizerVariable {
                Name = DefocusAwareStructureName,
                Type = OptimizerVariableType.Integer,
                Lower = 0, Upper = 4, InitialStep = 1,
                Read = p => p.DefocusAwareStructure ? p.StructureLayerBoost : 0,
                Write = (p, raw) => {
                    var boost = (int)v.Quantize(raw);
                    p.StructureLayerBoost = boost;
                    p.DefocusAwareStructure = boost > 0;
                }
            };
            return v;
        }

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
