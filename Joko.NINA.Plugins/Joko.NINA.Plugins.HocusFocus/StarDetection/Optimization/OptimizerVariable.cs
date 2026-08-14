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
        /// <summary>No-arg form returns the FULL set (all axes incl. the defocus-aware ones) — for introspection
        /// and tests. Production callers use <see cref="CreateCuratedSet(StarDetectorParams)"/> so the defocus axes
        /// are gated by the master toggle.</summary>
        public static IReadOnlyList<OptimizerVariable> CreateCuratedSet() => CreateCuratedSet(includeDefocusAxes: true);

        /// <summary>
        /// Curated set, including the defocus-aware axes ONLY when <paramref name="seed"/> has
        /// DefocusAwareDonutDetection ON — the MASTER toggle. The defocus-aware axes are the combined gate switch,
        /// the structure boost, the three gate tuning knobs, and the donut morph-close / hole-fill / streak / bloom
        /// knobs. When the master is OFF (or seed is null) the optimizer never touches any defocus param, so its
        /// search stays conservative/bit-identical w.r.t. those axes. (Matches the runtime gating in
        /// BuildStarDetectorParams, which AND-gates every defocus flag with the same master option.)
        /// </summary>
        public static IReadOnlyList<OptimizerVariable> CreateCuratedSet(StarDetectorParams seed)
            => CreateCuratedSet(includeDefocusAxes: seed != null && seed.DefocusAwareDonutDetection);

        /// <summary>
        /// Curated set with an explicit floor on the searchable <see cref="StarDetectorParams.Sensitivity"/> range —
        /// F23 mechanism (b), the CRUDE alternative to the objective's <see cref="OptimizationObjective.SMarginalSnr"/>
        /// term, kept so the two can be measured head-to-head on the synthetic bank.
        ///
        /// <para><paramref name="sensitivityLower"/> null ⇒ <see cref="DefaultSensitivityLower"/> (the shipping
        /// value), so every existing caller is unchanged. A floor at or below ~1.5 is PROVABLY INERT at shipped
        /// defaults: the structure/clip stage guarantees the gate's measured sensitivity is at least
        /// <c>PeakResponse × StarClippingMultiplier</c> (0.75 × 2.0 = 1.5), so no candidate can ever be rejected by
        /// a threshold below that. Do not confuse this with <c>ExposureRecommender.SensitivityFloorThreshold</c>
        /// (1.0), which is a "the optimizer hit its floor" predicate, not a detection floor.</para>
        /// </summary>
        public static IReadOnlyList<OptimizerVariable> CreateCuratedSet(StarDetectorParams seed, double? sensitivityLower)
            => CreateCuratedSet(
                includeDefocusAxes: seed != null && seed.DefocusAwareDonutDetection,
                sensitivityLower: sensitivityLower ?? DefaultSensitivityLower);

        /// <summary>
        /// The shipping lower bound of the searchable Sensitivity range. 0.0 — i.e. the axis is NOT floored, and the
        /// false-positive cost is carried by the objective (<see cref="OptimizationObjective.SMarginalSnr"/>) rather
        /// than by restricting the search domain. See docs/af-recommender-hardening-design.md for why a hard floor
        /// is the cruder of the two mechanisms: it forces every rig upward, including the ones whose low landing was
        /// measurably correct.
        /// </summary>
        public const double DefaultSensitivityLower = 0.0;

        /// <summary>
        /// The searchable ceiling for <see cref="StarDetectorParams.MaxDistortion"/>, and the one bound in the
        /// curated set that is GEOMETRIC rather than heuristic.
        /// <para>
        /// Despite its name, <c>MaxDistortion</c> is a <b>minimum fill ratio</b>: the gate computes
        /// <c>fillRatio = (star pixels) / d²</c> for a bounding-box max dimension <c>d</c> and <b>rejects</b> when
        /// <c>fillRatio &lt; MaxDistortion</c> (<c>StarDetector.cs</c>, the <c>TooDistorted</c> gate). Raising the
        /// value makes the gate <b>stricter</b>, not looser.
        /// </para>
        /// <para>
        /// A perfectly round star therefore tops out at the fill ratio of a disk inscribed in its own bounding
        /// box — <c>π/4 ≈ 0.785</c>. Any threshold above that rejects <b>every</b> round star. The old 1.0
        /// ceiling left roughly the top 21% of the axis reachable by the pattern search while returning zero
        /// detections by construction; wave 29 measured that collapse directly at 0.9
        /// (<c>TP=0 FP=0 FN=110384</c>, recall 0.000). Bounding the axis makes the dead band unreachable.
        /// </para>
        /// <para>
        /// <b>Scope, stated precisely so it is not over-claimed.</b> This axis is NOT coarse-gridded:
        /// <c>StarDetectionOptimizer</c>'s Phase A grids only Sensitivity × StarClippingMultiplier and holds
        /// every other variable at the incumbent. <c>MaxDistortion</c> moves only under the pattern search, whose
        /// proposals are clamped here by <c>Quantize</c>. So the bound is a <b>guard against a pathological
        /// upward excursion</b>, and it is expected to be inert on any run that never walks above π/4 — measured
        /// as bit-identical on all eight gate datasets, whose landings all sit at or below 0.6.
        /// </para>
        /// </summary>
        public const double MaxDistortionSearchUpper = Math.PI / 4.0;

        private static IReadOnlyList<OptimizerVariable> CreateCuratedSet(
            bool includeDefocusAxes, double sensitivityLower = DefaultSensitivityLower) {
            // --- Heuristic bounds (no hard UI validation range; chosen pragmatically). Edit here to retune. ---
            double SensitivityLower = sensitivityLower;  // heuristic; see DefaultSensitivityLower / CreateCuratedSet
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

            var vars = new List<OptimizerVariable> {
                Continuous(nameof(StarDetectorParams.Sensitivity), SensitivityLower, SensitivityUpper, 1.0,
                    p => p.Sensitivity, (p, v) => p.Sensitivity = v),
                Continuous(nameof(StarDetectorParams.StarClippingMultiplier), StarClipLower, StarClipUpper, 0.5,
                    p => p.StarClippingMultiplier, (p, v) => p.StarClippingMultiplier = v),
                Continuous(nameof(StarDetectorParams.NoiseClippingMultiplier), NoiseClipLower, NoiseClipUpper, 0.5,
                    p => p.NoiseClippingMultiplier, (p, v) => p.NoiseClippingMultiplier = v),
                Continuous(nameof(StarDetectorParams.PeakResponse), 0.1, 1.0, 0.05,
                    p => p.PeakResponse, (p, v) => p.PeakResponse = v),
                // Upper is MaxDistortionSearchUpper (π/4), not 1.0: see that constant for why the top of the old
                // range was reachable and useless. The lower bound stays heuristic.
                Continuous(nameof(StarDetectorParams.MaxDistortion), 0.1, MaxDistortionSearchUpper, 0.1,
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
            };

            // Defocus-aware axes — appended ONLY when the master donut toggle is ON (gated by the seed). When the
            // master is OFF the optimizer never explores any defocus param.
            if (includeDefocusAxes) {
                // Combined gate switch (distortion + centering relaxation) — synthetic alias; Write drives both.
                vars.Add(BooleanVar(DefocusAwareGatesName, 1,
                    p => p.DefocusAwareDistortion,
                    (p, en) => { p.DefocusAwareDistortion = en; p.DefocusAwareCentering = en; }));
                // Defocus-aware STRUCTURE boost (EARLY): 0 ⇒ OFF.
                vars.Add(StructureBoostVar());
                // The three gate tuning knobs (previously fixed; now searchable while the master is on).
                vars.Add(Continuous(nameof(StarDetectorParams.DefocusDistortionSizeReference), 10.0, 80.0, 5.0,
                    p => p.DefocusDistortionSizeReference, (p, v) => p.DefocusDistortionSizeReference = v));
                vars.Add(Continuous(nameof(StarDetectorParams.DefocusDistortionMinFactor), 0.05, 1.0, 0.05,
                    p => p.DefocusDistortionMinFactor, (p, v) => p.DefocusDistortionMinFactor = v));
                vars.Add(Continuous(nameof(StarDetectorParams.DefocusCenteringToleranceFactor), 1.0, 4.0, 0.25,
                    p => p.DefocusCenteringToleranceFactor, (p, v) => p.DefocusCenteringToleranceFactor = v));
                // Donut recovery: EARLY morph-close kernel (1 ⇒ off) + LATE annularity hole-fraction.
                vars.Add(IntegerVar(nameof(StarDetectorParams.DonutMorphCloseSize), 1, 15, 2,
                    p => p.DonutMorphCloseSize, (p, v) => p.DonutMorphCloseSize = v));
                vars.Add(Continuous(nameof(StarDetectorParams.DonutMinAnnularityHoleFraction), 0.05, 0.5, 0.05,
                    p => p.DonutMinAnnularityHoleFraction, (p, v) => p.DonutMinAnnularityHoleFraction = v));
                // Spike suppression (LATE): streak eccentricity (1.0 ⇒ off) + saturation bloom radius (0 ⇒ off).
                // Default-off in the seed; the objective's label term enables them when should-reject boxes exist.
                vars.Add(Continuous(nameof(StarDetectorParams.DonutMaxStreakEccentricity), 0.85, 1.0, 0.025,
                    p => p.DonutMaxStreakEccentricity, (p, v) => p.DonutMaxStreakEccentricity = v));
                vars.Add(Continuous(nameof(StarDetectorParams.DonutSaturationBloomRadius), 0.0, 60.0, 5.0,
                    p => p.DonutSaturationBloomRadius, (p, v) => p.DonutSaturationBloomRadius = v));
            }
            return vars;
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
                // Keep the defocus toggles AND the spike-suppression knobs live at full range under feedback so the
                // optimizer can enable them even if the analytic recommender didn't move them (e.g. to suppress a
                // saturated star's spikes/bloom that only the should-reject labels reveal).
                var isDefocusToggle = v.Name == DefocusAwareGatesName || v.Name == DefocusAwareStructureName
                    || v.Name == nameof(StarDetectorParams.DonutMaxStreakEccentricity)
                    || v.Name == nameof(StarDetectorParams.DonutSaturationBloomRadius);
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
