#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using MathNet.Numerics.Distributions;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus.Harness {

    /// <summary>Which side of best focus a knob applies to. "Left" is the low-position side, "Right" the high side.</summary>
    internal enum FocusSide {
        Left,
        Right
    }

    /// <summary>
    /// Injects a spurious LOW-HFR "false minimum" far from focus — the case where the detector imagines a small cluster
    /// of stars in background noise, producing a dip in the HFR curve that a lowest-raw-point center would chase.
    /// Behavior A defends against exactly this by centering the window on the fitted vertex, not the lowest sample.
    /// </summary>
    /// <param name="OnsetSteps">|pos − focus| ≥ OnsetSteps·stepSize on <paramref name="Side"/> triggers the false minimum.</param>
    /// <param name="Side">Which side the false minimum sits on.</param>
    /// <param name="FalseHfr">The spuriously LOW reported HFR (typically ≈ HfrFloor × 1.1 — near the true minimum).</param>
    /// <param name="FalseStarCount">Stars the false detection reports (≥ 2, so it survives the ≥2-usable-stars floor and forms a real fit point).</param>
    /// <param name="FalseSigma">HFR σ reported with the false cluster.</param>
    internal sealed record SpuriousSpec(
        int OnsetSteps,
        FocusSide Side,
        double FalseHfr,
        int FalseStarCount,
        double FalseSigma);

    /// <summary>
    /// A fully deterministic, seeded description of what the (scripted) star detector "sees" at any focuser position
    /// during a driven AutoFocus sweep. <see cref="Evaluate(int)"/> maps a position to <c>(hfr, sigma, starCount)</c>
    /// from the distance to focus, with three composable degradation knobs the battery sweeps over:
    /// <list type="bullet">
    ///   <item><b>Cutoff</b> — <see cref="LeftCutoffSteps"/> / <see cref="RightCutoffSteps"/>: beyond that many steps on a
    ///   side the field goes starless (<c>hfr = 0</c>, the engine's failure signal).</item>
    ///   <item><b>Spurious</b> — <see cref="Spurious"/>: a false low-HFR minimum far out on one side.</item>
    ///   <item><b>Asymmetry</b> — any left/right imbalance in the cutoff/spurious knobs (a lopsided run).</item>
    /// </list>
    /// Precedence at a position: cutoff (hard starless at the extreme) → spurious (false minimum in the near-far band) →
    /// baseline V-curve. All randomness is seeded per position (a fresh RNG keyed on <see cref="Seed"/> + position), so
    /// the same position always yields the same values regardless of the order detections complete — essential because
    /// detection runs on pooled tasks.
    /// </summary>
    internal sealed class DegradationScenario {

        public DegradationScenario(int focusPosition, int stepSize) {
            if (stepSize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(stepSize), stepSize, "stepSize must be positive");
            }
            FocusPosition = focusPosition;
            StepSize = stepSize;
            BaselineHfr = DefaultBaselineHfr(focusPosition);
        }

        /// <summary>True best-focus position (the V-curve vertex).</summary>
        public int FocusPosition { get; }

        /// <summary>Focuser step size, used to convert the step-based knobs to position offsets.</summary>
        public int StepSize { get; }

        /// <summary>
        /// Baseline HFR as a function of focuser position. Defaults to a <see cref="DefocusModel"/> hyperbola centered on
        /// <see cref="FocusPosition"/> (the same model the camera simulator uses — the single source of truth), but is
        /// settable so a scenario can substitute any curve.
        /// </summary>
        public Func<int, double> BaselineHfr { get; set; }

        /// <summary>Beyond this many steps LEFT of focus, detection returns 0 stars. Null ⇒ never cuts off on the left.</summary>
        public int? LeftCutoffSteps { get; set; }

        /// <summary>Beyond this many steps RIGHT of focus, detection returns 0 stars. Null ⇒ never cuts off on the right.</summary>
        public int? RightCutoffSteps { get; set; }

        /// <summary>Optional spurious low-HFR false-minimum injector. Null ⇒ no false minimum.</summary>
        public SpuriousSpec Spurious { get; set; }

        /// <summary>Stars reported at best focus, before any distance falloff.</summary>
        public int BaselineStarCount { get; set; } = 60;

        /// <summary>Floor on the baseline star count (never falls below this while stars are still detected).</summary>
        public int MinStarCount { get; set; } = 5;

        /// <summary>Stars lost per step of |defocus| (0 ⇒ constant star count everywhere in-band).</summary>
        public double StarCountFalloffPerStep { get; set; } = 0.0;

        /// <summary>Baseline HFR σ reported for a normal detection.</summary>
        public double SigmaBase { get; set; } = 0.1;

        /// <summary>Extra HFR σ added per step of |defocus| (0 ⇒ uniform σ).</summary>
        public double SigmaInflationPerStep { get; set; } = 0.0;

        /// <summary>Std-dev of seeded Gaussian noise added to the baseline (and spurious) HFR. 0 ⇒ noise-free.</summary>
        public double NoiseSigma { get; set; } = 0.0;

        /// <summary>Master seed for the per-position noise RNG.</summary>
        public int Seed { get; set; } = 1;

        /// <summary>The baseline HFR at best focus (the V-curve floor). Handy for sizing <see cref="SpuriousSpec.FalseHfr"/>.</summary>
        public double HfrFloor => BaselineHfr(FocusPosition);

        /// <summary>
        /// Deterministically maps a focuser position to the detector's <c>(hfr, sigma, starCount)</c>. A zero-star result
        /// always carries <c>hfr = 0</c> and a finite σ (the "found no usable stars" signal, distinct from an analysis
        /// error's NaN σ). See the precedence note on the class.
        /// </summary>
        public (double hfr, double sigma, int starCount) Evaluate(int position) {
            var delta = position - FocusPosition;
            var absDelta = Math.Abs(delta);
            var absSteps = absDelta / (double)StepSize;
            var side = delta < 0 ? FocusSide.Left : FocusSide.Right;

            // 1) Cutoff: hard starless at the far extreme. hfr = 0, σ = 0 (finite) ⇒ engine reads "no usable stars".
            var cutoffSteps = side == FocusSide.Left ? LeftCutoffSteps : RightCutoffSteps;
            if (cutoffSteps.HasValue && absDelta > cutoffSteps.Value * StepSize) {
                return (0.0, 0.0, 0);
            }

            // 2) Spurious: a false low-HFR minimum in the near-far band on its side.
            if (Spurious != null && side == Spurious.Side && absDelta >= Spurious.OnsetSteps * StepSize) {
                var falseHfr = Math.Max(1e-3, Spurious.FalseHfr + Noise(position));
                return (falseHfr, Spurious.FalseSigma, Spurious.FalseStarCount);
            }

            // 3) Baseline V-curve. Clamp to a small positive floor so a normal detection never reads as a failure (0).
            var hfr = Math.Max(1e-3, BaselineHfr(position) + Noise(position));
            var starCount = (int)Math.Round(Math.Max(MinStarCount, BaselineStarCount - StarCountFalloffPerStep * absSteps));
            var sigma = SigmaBase + SigmaInflationPerStep * absSteps;
            return (hfr, sigma, starCount);
        }

        /// <summary>
        /// A clean, symmetric baseline V-curve with every degradation knob off — no cutoffs, no spurious minimum, no
        /// falloff/inflation, no noise. Used by the C1 smoke test to prove the harness drives a full successful sweep.
        /// </summary>
        public static DegradationScenario CleanSymmetric(int focusPosition, int stepSize) {
            return new DegradationScenario(focusPosition, stepSize);
        }

        private double Noise(int position) {
            if (NoiseSigma <= 0.0) {
                return 0.0;
            }
            // Fresh RNG per position ⇒ the noise at a position is independent of the order detections complete.
            var rng = new Random(unchecked(Seed * 397 ^ position));
            return new Normal(0.0, NoiseSigma, rng).Sample();
        }

        // A DefocusModel hyperbola centered on best focus: HFR(pos) = √(HfrMin² + (κ·(pos − focus))²). Optics chosen to
        // give a well-conditioned V (HfrMin ≈ 0.9 px, a clear rise over the ±(offsetSteps+1)·stepSize window). Because
        // this IS a pure hyperbola, the HYPERBOLIC fit recovers the vertex exactly from noise-free samples.
        private static Func<int, double> DefaultBaselineHfr(int focusPosition) {
            var model = new DefocusModel(
                apertureMillimeters: 100.0,
                focalLengthMillimeters: 550.0,
                centralObstructionFraction: 0.0,
                pixelSizeMicrons: 3.76,
                seeingArcsec: 2.0,
                wavelengthNm: 550.0,
                focuserStepSizeMicrons: 5.0,
                optimalFocuserPosition: focusPosition);
            return position => model.HfrAtFocuserPosition(position);
        }
    }
}
