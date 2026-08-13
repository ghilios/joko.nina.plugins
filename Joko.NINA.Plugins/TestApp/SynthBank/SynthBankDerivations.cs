#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TestApp; // DonutHeuristic (BankVerification.cs) — internal, same assembly, explicit for clarity

namespace TestApp.SynthBank {

    /// <summary>
    /// Pure derivations of a synthetic dataset's expected-optimal bootstrap parameters from physics
    /// (<c>docs/synthetic-af-bank-design.md</c> §"G4. Spec and derivations"). "Pure" here means deterministic
    /// given its inputs and free of writes/global state — <see cref="DeriveExposureBand"/> is the one method
    /// that performs a READ (an ASTAP catalog query), and it does so only through an injected
    /// <see cref="IAstapCatalogReader"/>, the same seam <see cref="StarFieldCompositor"/> itself uses for
    /// testability (see <c>Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/FakeCatalogReader.cs</c>).
    ///
    /// <para><b>Deliberately override-free.</b> None of these methods read <see cref="SynthDatasetSpec"/>'s
    /// <c>*Override</c> fields (<c>StepSizeOverride</c>, <c>DetectionBinningOverride</c>, <c>DonutOverride</c>).
    /// Applying an override here would silently absorb any disagreement between the checked-in spec and what
    /// physics actually says; leaving it to the (separate, not-this-task) runner keeps that disagreement
    /// visible, which is the whole point of pinning a value rather than just accepting whatever the formula
    /// produces (see R2 below).</para>
    /// </summary>
    public static class SynthBankDerivations {

        // ── Step size ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Floor (px) under the in-focus HFR used to compute the step size, ESTIMATED (design risk R2): a
        /// sub-pixel PSF cannot actually be MEASURED below roughly this HFR (pixelization dominates over the
        /// optical minimum), so the recommender that will eventually run against the RENDERED/measured curve
        /// sees a shallower minimum than <see cref="DefocusModel.HfrMinPixels"/> alone predicts. This only bites
        /// the very-oversampled short-focal-length datasets (D01–D03 in the design's matrix, native HFR_min
        /// ≈0.7 px) — everything else has HFR_min well above this floor and the max() is a no-op. Both the raw
        /// <see cref="DefocusModel.HfrMinPixels"/> and the floored value are carried in
        /// <see cref="SynthTruthModel.HfrMin"/> / <see cref="SynthTruthModel.HfrMinEffective"/> specifically so
        /// validation can calibrate this number against what the renderer's measured minimum actually turns out
        /// to be — flagged as an estimate, not fixed here.
        /// </summary>
        public const double PixelizationFloorPixels = 0.70;

        /// <summary>
        /// Targeted points per side within the focus-sensitive band, mirroring
        /// <c>StepSizeRecommender</c>'s private constant of the same name and value (3.5). Not referenced
        /// directly — that constant is private — so if it is ever retuned this must be updated to match, or the
        /// two derivations (this file's physics estimate and the recommender's own fixed point) will disagree
        /// for a reason that has nothing to do with the fixed point itself.
        /// </summary>
        private const double PointsPerSide = 3.5;

        /// <summary>
        /// Derives the step size step* (focuser steps, floored at 1) a sweep should use so the focus-sensitive
        /// band (HFR from its minimum up to 3× the minimum) holds <see cref="PointsPerSide"/> points per side.
        ///
        /// <para><b>Derivation.</b> <c>StepSizeRecommender</c> finds the offset Δ where the fitted hyperbola
        /// <c>HFR(Δ) = √(HFR_min² + (κΔ)²)</c> reaches <c>3·HFR_min</c> (its <c>HfrThresholdMultiple</c>):
        /// <c>√(HFR_min² + (κΔ)²) = 3·HFR_min ⇒ (κΔ)² = 9·HFR_min² − HFR_min² = 8·HFR_min² ⇒ κΔ = √8·HFR_min</c>.
        /// The recommender then divides that half-width Δ by <c>PointsPerSide</c> to get the step. Substituting
        /// the pixelization-floored <see cref="PixelizationFloorPixels"/> for HFR_min (see that constant's
        /// remarks) gives <c>step* = √8 · HFR_eff / (κ · PointsPerSide)</c> exactly as stated in the design.</para>
        /// </summary>
        /// <param name="model">The dataset's defocus model (<see cref="BuildDefocusModel"/>).</param>
        public static int DeriveStepSize(DefocusModel model) {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var hfrEffective = Math.Max(model.HfrMinPixels, PixelizationFloorPixels);
            var halfWidthSteps = Math.Sqrt(8.0) * hfrEffective / model.KappaPixelsPerStep;
            var step = (int)Math.Round(halfWidthSteps / PointsPerSide, MidpointRounding.AwayFromZero);
            return Math.Max(1, step);
        }

        /// <summary>
        /// The per-frame star count the objective requires (<c>ObjectiveConstants.NHard</c>). A sweep position
        /// below it is not a usable measurement, which is exactly the sense in which the sweep has run out of
        /// DETECTABLE range. Mirrored here rather than referenced because <c>ObjectiveConstants</c> is a plugin
        /// type the derivation deliberately does not depend on — same arrangement as <see cref="NTarget"/>.
        /// </summary>
        public const int NHardStars = 3;

        /// <summary>
        /// How far the detectability bound may narrow <c>step*</c> in one derivation, as a multiple of the sampled
        /// HALF-span — the analytic twin of <c>StepSizeRecommender.MinHalfWidthSampledHalfSpanMultiple</c>. Kept in
        /// lockstep with it for the same reason <see cref="PointsPerSide"/> is kept in lockstep with the
        /// recommender's: the two derivations must differ only where the physics does.
        /// </summary>
        public const double MinHalfWidthSampledHalfSpanMultiple = 0.5;

        /// <summary>
        /// F18's analytic twin: the largest offset from focus (focuser steps) at which at least
        /// <see cref="NHardStars"/> on-frame truth stars still clear the detector's default Sensitivity gate at
        /// <paramref name="exposureSeconds"/>. <see cref="double.NaN"/> when fewer than
        /// <see cref="MinFramesForDetectableHalfWidth"/> sweep positions qualify — "unmeasurable", never "nothing
        /// is detectable", the same distinction the runtime rule makes.
        ///
        /// <para><b>Why the bank needs this at all.</b> <c>step*</c> is the target the bank's A1/A3 assertions walk
        /// the recommender toward. If the shipped recommender gains a detectability bound and this derivation does
        /// not, the bank asserts the recommender toward a target F18 says is wrong. So the rule is applied to BOTH
        /// or to NEITHER — which is why it rides the same arm flag rather than shipping unconditionally.</para>
        ///
        /// <para><b>The SNR model is <see cref="DeriveExposureBand"/>'s, term for term</b>
        /// (<c>snr = GateSnrPeakCoefficient · peakElectrons / σ_bg</c>), evaluated per STAR rather than for the
        /// NTarget-th one, and compared against <see cref="ExposureRecommender.TargetSensitivity"/> — which is
        /// documented there as the shipped default gate, and is what the effective gate
        /// <c>max(Sensitivity, InertSensitivityBound) = max(10, 1.5)</c> resolves to at defaults.</para>
        ///
        /// <para><b>One render, like the exposure band.</b> Aberrations are off across the bank, so every star on a
        /// frame shares one kernel; a star's flux does not change with focus, and only the peak FRACTION varies per
        /// frame — analytically, via <c>PsfKernelGenerator</c>. So the star field is rendered once at focus and
        /// every sweep position is evaluated from it.</para>
        /// </summary>
        public static (double HalfWidthSteps, int QualifyingFrames, string Definition) DeriveDetectableHalfWidth(
                SynthDatasetSpec dataset, SynthBankDefaults defaults, DefocusModel model,
                IAstapCatalogReader catalogReader, int captureBinning, int detectionBinning,
                int stepSize, int offsetSteps, double exposureSeconds, CancellationToken token = default) {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (catalogReader == null || !double.IsFinite(exposureSeconds) || !(exposureSeconds > 0.0) || stepSize < 1) {
                return (double.NaN, 0, "detectable half-width not derived (no catalog reader, or no usable exposure/step)");
            }

            var sensor = SensorRegistry.Get(SynthRenderRequestFactory.ParseSensorModel(dataset.SensorModel));
            var filter = FilterRegistry.Get(SynthRenderRequestFactory.ParseFilter(dataset.Filter ?? defaults.Filter));
            var gain = dataset.Gain ?? defaults.Gain;

            var request = SynthRenderRequestFactory.Build(
                dataset, defaults, focuserPosition: model.OptimalFocuserPosition, exposureSeconds: ReferenceExposureSeconds, noiseSeed: 0);
            var compositor = new StarFieldCompositor(catalogReader);
            var truth = new List<StarTruth>();
            compositor.Render(request, truth, token);

            // On-frame centres only — the gate question is about a star the detector centroids, matching
            // FindNthBrightestOnFrameStar's own exclusion of wing-spill truth.
            var onFrameFluxPerSecond = truth
                .Where(t => t.CxPixels >= 0.0 && t.CxPixels < sensor.Width && t.CyPixels >= 0.0 && t.CyPixels < sensor.Height)
                .Select(t => t.FluxElectrons)
                .Where(f => f > 0.0)
                .ToList();
            if (onFrameFluxPerSecond.Count == 0) {
                return (double.NaN, 0, "detectable half-width not derived (no on-frame catalog stars at the in-focus position)");
            }

            var radiometry = RadiometryCalculator.FromRequest(request, sensor, filter);
            var skyPlusDarkRatePerSecond = radiometry.SkyElectronsPerPixel() + radiometry.DarkElectronsPerPixel();
            var readNoise = sensor.ReadNoiseElectronsAtGain(gain);
            var effectiveBinning = captureBinning * detectionBinning;
            // σ_bg at this exposure is the same for every star on every frame (background, not signal), so it is
            // computed once rather than per star.
            var sigmaBackground = effectiveBinning * Math.Sqrt(skyPlusDarkRatePerSecond * exposureSeconds + readNoise * readNoise);
            if (!(sigmaBackground > 0.0)) {
                return (double.NaN, 0, "detectable half-width not derived (non-positive background sigma)");
            }

            var qualifying = 0;
            var starvedPositions = 0;
            var furthestSteps = 0.0;
            var edgeFrameCount = 0;
            for (var k = -offsetSteps; k <= offsetSteps; k++) {
                var defocusMicrons = k * stepSize * dataset.FocuserStepSizeMicrons;
                var frameKernel = PsfKernelGenerator.Generate(model, defocusMicrons);
                var peakFractionBinned = GoldenFromTruth.PeakFractionBinned(frameKernel.MaxPeak, effectiveBinning);
                // The faintest flux that still clears the gate on THIS frame, solved from
                // snr = c · flux · t · peakFraction / σ_bg >= gate. Counting stars above a flux threshold is the
                // same test as evaluating every star's SNR, and it is O(N) instead of O(N) per frame with a divide.
                var snrPerFluxUnit = GateSnrPeakCoefficient * exposureSeconds * peakFractionBinned / sigmaBackground;
                if (!(snrPerFluxUnit > 0.0)) {
                    continue;
                }
                var fluxThreshold = ExposureRecommender.TargetSensitivity / snrPerFluxUnit;
                var count = 0;
                for (var i = 0; i < onFrameFluxPerSecond.Count; i++) {
                    if (onFrameFluxPerSecond[i] >= fluxThreshold) {
                        count++;
                    }
                }
                // The OUTERMOST frame's count is the headroom this bound has. Reported, because "W_detect never
                // binds" is only believable alongside the margin by which it did not: a count of 4 at the edge and
                // a count of 400 mean very different things about whether the bank can exercise F18 at all.
                if (Math.Abs(k) == offsetSteps) {
                    edgeFrameCount = Math.Max(edgeFrameCount, count);
                }
                if (count < NHardStars) {
                    starvedPositions++;
                    continue;
                }
                qualifying++;
                var offsetSteps_ = Math.Abs((double)k * stepSize);
                if (offsetSteps_ > furthestSteps) {
                    furthestSteps = offsetSteps_;
                }
            }

            if (starvedPositions == 0) {
                // The sweep never observed detectability ending. Reporting its own edge would turn
                // min(W_3x, W_detect) into a cap on WIDENING, which is a different rule the recommender already
                // has -- see StepSizeRecommender.MeasureMaxUsefulHalfSpan's remarks. An unobserved limit is absent.
                return (double.NaN, qualifying,
                    $"detectable half-width NOT OBSERVED: all {2 * offsetSteps + 1} sweep positions carry >= {NHardStars} " +
                    $"stars above the gate at {exposureSeconds:0.###}s (edge frame carries {edgeFrameCount}), so this sweep " +
                    "never reached the detectability limit and no bound is applied");
            }
            if (qualifying < MinFramesForDetectableHalfWidth || !(furthestSteps > 0.0)) {
                return (double.NaN, qualifying,
                    $"detectable half-width UNMEASURABLE: only {qualifying} of {2 * offsetSteps + 1} sweep positions carry " +
                    $">= {NHardStars} stars above the gate at {exposureSeconds:0.###}s (need {MinFramesForDetectableHalfWidth}); " +
                    "no bound is applied, which is deliberately different from a bound of zero");
            }
            return (furthestSteps, qualifying,
                $"detectable half-width {furthestSteps:0} steps (edge frame carries {edgeFrameCount} stars above the gate, " +
                $"floor {NHardStars}): the outermost of {2 * offsetSteps + 1} sweep positions " +
                $"(at step {stepSize}) still carrying >= {NHardStars} of {onFrameFluxPerSecond.Count} on-frame stars above " +
                $"the default gate ({ExposureRecommender.TargetSensitivity:0}) at {exposureSeconds:0.###}s, binning {effectiveBinning}");
        }

        /// <summary>How many sweep positions must clear the floor before a detectable half-width is measurable at all. Twin of <c>StepSizeRecommender.MinFramesForDetectHalfWidth</c>.</summary>
        public const int MinFramesForDetectableHalfWidth = 3;

        /// <summary>
        /// F18's step*, bounded by detectability: <c>min(W_3x, max(W_detect, floor)) / PointsPerSide</c>, with the
        /// floor at <see cref="MinHalfWidthSampledHalfSpanMultiple"/> × the sampled half-span of the GEOMETRIC
        /// sweep. A non-finite <paramref name="detectableHalfWidthSteps"/> returns the geometric step unchanged —
        /// unmeasurable means no bound.
        /// </summary>
        public static int DeriveStepSizeDetectBounded(DefocusModel model, int geometricStep, int offsetSteps, double detectableHalfWidthSteps) {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (!double.IsFinite(detectableHalfWidthSteps) || !(detectableHalfWidthSteps > 0.0)) {
                return Math.Max(1, geometricStep);
            }
            var hfrEffective = Math.Max(model.HfrMinPixels, PixelizationFloorPixels);
            var geometricHalfWidth = Math.Sqrt(8.0) * hfrEffective / model.KappaPixelsPerStep;
            var floor = MinHalfWidthSampledHalfSpanMultiple * offsetSteps * Math.Max(1, geometricStep);
            var halfWidth = Math.Min(geometricHalfWidth, Math.Max(detectableHalfWidthSteps, floor));
            return Math.Max(1, (int)Math.Round(halfWidth / PointsPerSide, MidpointRounding.AwayFromZero));
        }

        // ── Detection binning ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Derives the recommended DETECTION binning factor from the dataset's in-focus HFR, expressed in
        /// CAPTURED pixels (native HFR_min ÷ <paramref name="captureBinning"/> — capture binning already
        /// happened physically at readout before detection ever sees the frame). Delegates to the real
        /// <see cref="DetectionBinningResolver.RecommendFromHfr"/> rather than reimplementing its
        /// <c>clamp(round(hfr/3), 1, 4)</c> rule, so a future retune of that rule is picked up automatically
        /// instead of silently diverging.
        ///
        /// <para>Per the design: the R² ≥ 0.9 gate that guards this recommendation in production lives in
        /// <c>StarDetectionOptimizerWizardVM</c>, not in the resolver itself, so this derivation (and any
        /// offline caller) applies the rule unconditionally — there is no gate to reproduce here.</para>
        /// </summary>
        public static int DeriveDetectionBinning(DefocusModel model, int captureBinning) {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (captureBinning < 1) throw new ArgumentOutOfRangeException(nameof(captureBinning), captureBinning, "must be >= 1");

            var inFocusHfrCapturedPixels = model.HfrMinPixels / captureBinning;
            return DetectionBinningResolver.RecommendFromHfr(inFocusHfrCapturedPixels);
        }

        // ── Exposure band ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The order statistic used for the "brightest N" star (<c>ExposureRecommender</c>'s own
        /// <c>NTarget</c>-th-brightest gate statistic). Hard-coded to <c>ObjectiveConstants.NTarget</c>'s
        /// shipped default (20) rather than threaded through as a parameter, matching
        /// <c>ExposureRecommender</c>'s own remarks on why it is the <c>NTarget</c>-th star and not the median:
        /// the median over-recommends on a rich field, while the <c>NTarget</c>-th star asks the question this
        /// exposure derivation exists to answer — "what exposure would put <c>NTarget</c> stars per frame above
        /// the healthy default gate".
        /// </summary>
        private const int NTarget = 20;

        /// <summary>
        /// Reference exposure (s) for the ONE render <see cref="DeriveExposureBand"/> performs. Both
        /// <c>RadiometryCalculator.StarElectrons</c> and <c>SkyElectronsPerPixel</c>/<c>DarkElectronsPerPixel</c>
        /// are exactly linear in exposure time, so a 1 s reference makes the rendered
        /// <c>StarTruth.FluxElectrons</c> and the sky/dark rates read directly AS their per-second values — no
        /// separate scaling step is needed before solving for the target exposure.
        /// </summary>
        private const double ReferenceExposureSeconds = 1.0;

        /// <summary>
        /// Stand-in for the ratio <c>NormalizedBrightness / peakElectrons</c> in the REAL detector gate. The
        /// production statistic (<c>StarDetector.ComputeStarParameters</c>, consumed as
        /// <c>Star.MeasuredSensitivity</c>) is <c>sensitivity = NormalizedBrightness / σ</c> with
        /// <c>NormalizedBrightness = peak − (1 − PeakResponse) · meanFlux</c>
        /// (<c>PeakResponse</c> defaults to 0.75) — where <c>meanFlux</c> is the mean background-subtracted flux
        /// over the clip-surviving FOOTPRINT pixels, a segmentation-dependent quantity this truth-only
        /// derivation has no access to (<c>StarTruth</c> carries total flux and the phase-kernel peak fraction,
        /// not a per-pixel footprint the detector would have segmented). For a compact, well-sampled PSF
        /// <c>meanFlux</c> runs well below <c>peak</c>, so <c>NormalizedBrightness</c> sits closer to <c>peak</c>
        /// than to <c>0.75·peak</c>; <c>0.85</c> is the design's chosen stand-in for that gap — a documented
        /// APPROXIMATION of <c>ComputeStarParameters</c>, not a re-derivation of it. This is why the design
        /// calls the exposure band "the softest derivation" and runs scenario S0 (bootstrap = expected optimal)
        /// first on every dataset: if 0.85 is off, S0 will show the exposure recommendation moving even when
        /// seeded at the "right" answer, and the coefficient gets recalibrated from that evidence.
        ///
        /// <para>Evaluated at the in-focus (median) sweep frame only, so the detector's DONUT matched-filter
        /// branch of <c>MeasuredSensitivity</c> (<c>TotalFlux / (σ·√N)</c>, gated on an extended bounding box)
        /// never applies here — only the per-pixel <c>NormalizedBrightness/σ</c> path this coefficient
        /// approximates, which is the branch an in-focus star always takes.</para>
        /// </summary>
        private const double GateSnrPeakCoefficient = 0.85;

        /// <summary>Low end of the acceptable gate-SNR band the design brackets around <c>ExposureRecommender.TargetSensitivity</c> (10).</summary>
        private const double GateSnrBandFloor = 7.0;

        /// <summary>High end of the acceptable gate-SNR band. Not coincidentally 2× <c>TargetSensitivity</c> — the same "2× the gate" margin G5's golden `high` tier uses.</summary>
        private const double GateSnrBandCeiling = 20.0;

        /// <summary>Lower exposure clamp (s) — also the exposure-time ladder's finest granularity (<see cref="ExposureRecommender.RoundExposureSeconds"/> uses 0.5 s below 10 s).</summary>
        private const double MinExposureSeconds = 0.5;

        /// <summary>
        /// Derives the expected-optimal exposure and its acceptable band from the SAME gate-SNR arithmetic the
        /// product's <see cref="ExposureRecommender"/> targets, evaluated against a REAL rendered star field
        /// rather than a placeholder brightness distribution.
        ///
        /// <para><b>What "gate SNR" means here</b> (reconciled against <see cref="ExposureRecommender"/> — see
        /// <see cref="GateSnrPeakCoefficient"/> for why an exact reproduction of
        /// <c>StarDetector.ComputeStarParameters</c> is not possible from truth alone):
        /// <code>
        /// snr(t) = GateSnrPeakCoefficient · peakElectrons(t) / σ_bg(t)
        /// peakElectrons(t) = flux(t) · min(1, kernelPeakFraction · binning²)
        /// σ_bg(t) = binning · √((skyRate + darkRate)·t + readNoise²)
        /// </code>
        /// <c>flux(t)</c> is linear in exposure time <c>t</c>; the peak-fraction cap at 1.0 is required because
        /// no more than 100% of a star's flux can land in one binned pixel — without it, the formula would
        /// (wrongly) keep growing the peak past total flux for a large binning factor on an already-concentrated
        /// (well-oversampled) kernel. <c>binning</c> is <c>captureBinning · detectionBinning</c> (see the
        /// <paramref name="captureBinning"/>/<paramref name="detectionBinning"/> parameters): capture binning
        /// physically sums native pixels at readout before the frame ever reaches the detector, and detection
        /// binning sums again on top of that — <c>RunEvaluationMetrics.FrameStarSnrs</c>' own doc comment notes
        /// its values are "in binned-pixel space (not comparable across DetectionBinning factors)", i.e. the
        /// space AFTER both binnings have been applied, which is exactly what this formula's <c>binning</c>
        /// must represent for <c>snr(t)</c> to land in the same space <see cref="ExposureRecommender.TargetSensitivity"/>
        /// was calibrated against.</para>
        ///
        /// <para><b>How the star is found.</b> Renders <paramref name="dataset"/> ONCE, at the sweep's median
        /// (in-focus) frame and <see cref="ReferenceExposureSeconds"/> (1 s — see that constant), through the
        /// REAL <see cref="StarFieldCompositor"/> with <paramref name="catalogReader"/>, and reads
        /// <see cref="StarTruth.FluxElectrons"/> for the <see cref="NTarget"/>-th brightest star whose PROJECTED
        /// CENTRE actually lands on the sensor (wing-spill truth is excluded — the gate SNR question is about a
        /// star the detector could centroid, not one merely spilling a faint arc onto the edge). Fewer than
        /// <see cref="NTarget"/> on-frame stars falls back to the faintest one found, mirroring
        /// <c>ExposureRecommender.PerFrameNthBrightest</c>'s own short-frame fallback (a bounded UNDER-estimate,
        /// never an invented number) — <see cref="SynthExpectedOptimal.ExposureDefinition"/> says so explicitly
        /// when it happens.</para>
        ///
        /// <para><b>Solving for t.</b> Since <c>flux(t)</c> is linear and the peak fraction/σ terms are
        /// otherwise t-independent, <c>snr(t) = A·t / √(B·t + C)</c> for constants A (signal), B (shot-noise
        /// rate) and C (read-noise floor) — closed-form invertible (<see cref="SolveExposureForTargetGateSnr"/>)
        /// rather than iterated, avoiding the re-render-per-candidate-exposure cost the design explicitly says
        /// to avoid.</para>
        /// </summary>
        /// <param name="dataset">The dataset spec.</param>
        /// <param name="defaults">Bank-wide defaults.</param>
        /// <param name="model">The dataset's defocus model (<see cref="BuildDefocusModel"/>) — reused rather than rebuilt so caller and this method agree on it.</param>
        /// <param name="catalogReader">The ASTAP catalog reader (real in production, fake in tests).</param>
        /// <param name="captureBinning">Physical/AF capture binning (<see cref="SynthDatasetSpec.CaptureBinning"/>).</param>
        /// <param name="detectionBinning">Recommended detection binning (<see cref="DeriveDetectionBinning"/>).</param>
        /// <param name="stepSize">Derived sweep step (<see cref="DeriveStepSize"/>) — fixes where the sweep's frames sit, and so the per-frame defocus the median peak fraction is taken over.</param>
        /// <param name="offsetSteps">Frames per side of focus; the sweep is <c>2·offsetSteps+1</c> frames.</param>
        /// <param name="token">Cancellation for the render.</param>
        public static (double ExposureSeconds, double BandLowSeconds, double BandHighSeconds, string Definition,
                double RawSeconds, string Clamp) DeriveExposureBand(
                SynthDatasetSpec dataset, SynthBankDefaults defaults, DefocusModel model,
                IAstapCatalogReader catalogReader, int captureBinning, int detectionBinning,
                int stepSize, int offsetSteps, CancellationToken token = default) {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (catalogReader == null) throw new ArgumentNullException(nameof(catalogReader));
            if (captureBinning < 1) throw new ArgumentOutOfRangeException(nameof(captureBinning));
            if (detectionBinning < 1) throw new ArgumentOutOfRangeException(nameof(detectionBinning));

            var sensor = SensorRegistry.Get(SynthRenderRequestFactory.ParseSensorModel(dataset.SensorModel));
            var filter = FilterRegistry.Get(SynthRenderRequestFactory.ParseFilter(dataset.Filter ?? defaults.Filter));
            var gain = dataset.Gain ?? defaults.Gain;

            // model.OptimalFocuserPosition (not a second, independent read of dataset.OptimalFocuserPosition) —
            // the model is the single source of truth for "which focuser position counts as in-focus" for this
            // derivation, so it and the caller's model (BuildDefocusModel) can never accidentally diverge.
            var request = SynthRenderRequestFactory.Build(
                dataset, defaults, focuserPosition: model.OptimalFocuserPosition, exposureSeconds: ReferenceExposureSeconds, noiseSeed: 0);

            var compositor = new StarFieldCompositor(catalogReader);
            var truth = new List<StarTruth>();
            compositor.Render(request, truth, token);

            var (star, onFrameCount, usedFallback) = FindNthBrightestOnFrameStar(truth, sensor.Width, sensor.Height, NTarget);
            if (star == null) {
                return (double.NaN, double.NaN, double.NaN,
                    "No catalog stars landed on-frame at the in-focus position; the exposure band cannot be derived from truth (starless pointing, or the ASTAP catalog is missing/unreadable).",
                    double.NaN, "not-derived");
            }

            // t = ReferenceExposureSeconds = 1 s, so FluxElectrons/SkyElectronsPerPixel/DarkElectronsPerPixel
            // ARE the per-second rates directly (all three are exactly linear in exposure time).
            var fluxPerSecond = star.FluxElectrons;
            var radiometry = RadiometryCalculator.FromRequest(request, sensor, filter);
            var skyPlusDarkRatePerSecond = radiometry.SkyElectronsPerPixel() + radiometry.DarkElectronsPerPixel();
            var readNoise = sensor.ReadNoiseElectronsAtGain(gain);

            var effectiveBinning = captureBinning * detectionBinning;

            // MEDIAN ACROSS THE SWEEP, not the in-focus frame alone. ExposureRecommender's S_now is the median over
            // non-recovery frames of each frame's NTarget-th-brightest SNR, so scoring only the in-focus frame --
            // the sharpest and highest-SNR frame there is -- systematically overstates SNR and understates the
            // exposure the sweep needs. Generating the bank made that concrete: at the in-focus-only exposure of
            // 0.5s, the extreme frames of D08/D09 carried 3-7 golden stars out of ~470, which is not a sweep an
            // autofocus fit can use.
            //
            // No additional rendering is needed for this. Aberrations are off across the whole bank, so every star
            // on a given frame shares one defocus and therefore one kernel; a star's flux does not change with
            // focus, so the NTarget-th brightest star is the SAME star on every frame. Only the peak FRACTION
            // varies, and that is analytic per frame via PsfKernelGenerator.
            //
            // GoldenFromTruth.PeakFractionBinned (rather than a local copy of its min(1, ...) cap): the exposure
            // derivation and the golden tiering must agree exactly on how a peak fraction saturates toward 1.0 as
            // binning grows, or the two could silently drift apart on a future retune of the cap.
            var perFramePeakFractions = new List<double>(2 * offsetSteps + 1);
            for (var k = -offsetSteps; k <= offsetSteps; k++) {
                var defocusMicrons = k * stepSize * dataset.FocuserStepSizeMicrons;
                var frameKernel = PsfKernelGenerator.Generate(model, defocusMicrons);
                perFramePeakFractions.Add(GoldenFromTruth.PeakFractionBinned(frameKernel.MaxPeak, effectiveBinning));
            }
            perFramePeakFractions.Sort();
            var peakFractionBinned = perFramePeakFractions[perFramePeakFractions.Count / 2];

            var rawTarget = SolveExposureForTargetGateSnr(ExposureRecommender.TargetSensitivity, fluxPerSecond, peakFractionBinned, effectiveBinning, skyPlusDarkRatePerSecond, readNoise);
            var rawLow = SolveExposureForTargetGateSnr(GateSnrBandFloor, fluxPerSecond, peakFractionBinned, effectiveBinning, skyPlusDarkRatePerSecond, readNoise);
            var rawHigh = SolveExposureForTargetGateSnr(GateSnrBandCeiling, fluxPerSecond, peakFractionBinned, effectiveBinning, skyPlusDarkRatePerSecond, readNoise);

            var exposureSeconds = double.IsNaN(rawTarget)
                ? double.NaN
                : ExposureRecommender.RoundExposureSeconds(Math.Clamp(rawTarget, MinExposureSeconds, ExposureRecommender.MaxRecommendedExposureSeconds));
            var bandLow = double.IsNaN(rawLow) ? double.NaN : Math.Clamp(rawLow, MinExposureSeconds, ExposureRecommender.MaxRecommendedExposureSeconds);
            var bandHigh = double.IsNaN(rawHigh) ? double.NaN : Math.Clamp(rawHigh, MinExposureSeconds, ExposureRecommender.MaxRecommendedExposureSeconds);

            var starDescription = usedFallback
                ? $"faintest of only {onFrameCount} on-frame stars (fewer than NTarget={NTarget} found; under-estimating fallback, mirrors ExposureRecommender.PerFrameNthBrightest)"
                : $"{NTarget}th-brightest of {onFrameCount} on-frame stars";
            var definition =
                $"Exposure t solving snr(t) = {GateSnrPeakCoefficient:0.00}*peakElectrons(t)/sigma_bg(t) = {ExposureRecommender.TargetSensitivity:0} for the {starDescription} " +
                $"taken as the MEDIAN over the {2 * offsetSteps + 1} sweep frames at step {stepSize} (matching ExposureRecommender's " +
                $"median-across-non-recovery-frames statistic, not the in-focus frame alone), at binning={effectiveBinning} " +
                $"(captureBinning={captureBinning} x detectionBinning={detectionBinning}), " +
                $"clamped to [{MinExposureSeconds:0.0}, {ExposureRecommender.MaxRecommendedExposureSeconds:0}] s and rounded on ExposureRecommender.RoundExposureSeconds' ladder. " +
                $"Band = t at snr(t) = {GateSnrBandFloor:0} (low) and snr(t) = {GateSnrBandCeiling:0} (high).";

            // F19(b): a value that IS the clamp is not an answer, it is the arithmetic saying it had nothing to
            // say -- and reporting it identically to a free solution inside the band hides exactly that. On
            // D02_rich_135mm the solve asks for far less than the 0.5 s floor and the reported band collapses to
            // [0.5, 0.5]; the zero width is the tell that nothing was measured, not that everything agreed. Wave 7
            // arm E then measured D02 gaining 44% of sigma_focus at 8x this "derived" exposure. Say it in the
            // definition and expose the pre-clamp value, so the saturated set is readable rather than inferred --
            // that set is the work list for re-checking MinExposureSeconds itself.
            var clamp = ExposureClamp.Classify(rawTarget, MinExposureSeconds, ExposureRecommender.MaxRecommendedExposureSeconds);
            if (ExposureClamp.Saturated(clamp)) {
                definition += $" SATURATED at the {clamp}: the solve asked for {rawTarget:0.###} s, so {exposureSeconds:0.###} s is the clamp rather than a derived value.";
            }

            return (exposureSeconds, bandLow, bandHigh, definition, rawTarget, clamp);
        }

        /// <summary>
        /// Closed-form solve of <c>s = A·t / √(B·t + C)</c> for <c>t</c>, where
        /// <c>A = GateSnrPeakCoefficient · fluxPerSecond · peakFractionBinned / binning</c>,
        /// <c>B = skyPlusDarkRatePerSecond</c>, <c>C = readNoise²</c>. Squaring and rearranging gives the
        /// quadratic <c>A²t² − s²Bt − s²C = 0</c>; since <c>C ≥ 0</c> the two roots have opposite sign (or one is
        /// zero), so the positive root is unambiguous:
        /// <c>t = [s²B + s·√(s²B² + 4A²C)] / (2A²)</c>. Returns <see cref="double.NaN"/> when there is no signal
        /// to solve against (non-positive flux/peak fraction/binning ⇒ A ≤ 0).
        /// </summary>
        private static double SolveExposureForTargetGateSnr(
                double targetSnr, double fluxPerSecondElectrons, double peakFractionBinned, int binning,
                double skyPlusDarkRatePerSecond, double readNoiseElectrons) {
            var a = GateSnrPeakCoefficient * fluxPerSecondElectrons * peakFractionBinned / binning;
            if (!(a > 0.0)) {
                return double.NaN;
            }
            var b = skyPlusDarkRatePerSecond;
            var c = readNoiseElectrons * readNoiseElectrons;
            var s = targetSnr;
            var discriminant = s * s * b * b + 4.0 * a * a * c;
            var t = (s * s * b + s * Math.Sqrt(discriminant)) / (2.0 * a * a);
            return t;
        }

        /// <summary>
        /// The <paramref name="nTarget"/>-th brightest (by <see cref="StarTruth.FluxElectrons"/>) star whose
        /// PROJECTED CENTRE lands within the sensor's [0, width) × [0, height) — wing-spill truth (accepted only
        /// because its donut spills onto the frame) is excluded, since the gate-SNR question is specifically
        /// about a star the detector centroids. Falls back to the faintest on-frame star when fewer than
        /// <paramref name="nTarget"/> exist, same convention as <c>ExposureRecommender.PerFrameNthBrightest</c>.
        /// </summary>
        private static (StarTruth Star, int OnFrameCount, bool UsedFallback) FindNthBrightestOnFrameStar(
                IReadOnlyList<StarTruth> truth, int width, int height, int nTarget) {
            var onFrame = truth
                .Where(t => t.CxPixels >= 0.0 && t.CxPixels < width && t.CyPixels >= 0.0 && t.CyPixels < height)
                .OrderByDescending(t => t.FluxElectrons)
                .ToList();
            if (onFrame.Count == 0) {
                return (null, 0, false);
            }
            if (onFrame.Count >= nTarget) {
                return (onFrame[nTarget - 1], onFrame.Count, false);
            }
            return (onFrame[onFrame.Count - 1], onFrame.Count, true);
        }

        // ── Donut expectation ────────────────────────────────────────────────────────────────────────────────

        /// <summary>Inner-radius floor (captured px) below which the donut hole is too small to register as a donut, per the design.</summary>
        private const double DonutInnerRadiusFloorPixels = 2.0;

        /// <summary>
        /// Derives whether donut-aware detection is expected to be warranted on this dataset:
        /// <c>ε &gt; 0 AND r_inner@extreme &gt; 2 px AND HFR@extreme ≥ DonutHeuristic.HeavyDefocusHFR (9 px)</c>,
        /// all evaluated at the sweep's most-defocused frame and in CAPTURED pixels (native ÷
        /// <paramref name="captureBinning"/>). The HFR term deliberately reuses
        /// <c>TestApp.DonutHeuristic.HeavyDefocusHFR</c> — the same constant <c>bank-verify</c>'s MEASURED
        /// donut heuristic uses — so the apriori truth flag and the measured signal are on the same scale and
        /// comparable rather than two independently-chosen thresholds that happen to look similar.
        /// </summary>
        /// <param name="dataset">The dataset spec (for <see cref="SynthDatasetSpec.CentralObstructionFraction"/>).</param>
        /// <param name="model">The dataset's defocus model.</param>
        /// <param name="offsetSteps">Offset steps per side of focus (<see cref="SynthBankDefaults.OffsetSteps"/>).</param>
        /// <param name="stepSize">The (derived) step size (<see cref="DeriveStepSize"/>).</param>
        /// <param name="captureBinning">Physical/AF capture binning.</param>
        public static (bool DonutExpected, string Rationale) DeriveDonutExpectation(
                SynthDatasetSpec dataset, DefocusModel model, int offsetSteps, int stepSize, int captureBinning) {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (captureBinning < 1) throw new ArgumentOutOfRangeException(nameof(captureBinning));

            var extremeDefocusMicrons = SweepExtremeDefocusMicrons(offsetSteps, stepSize, model.FocuserStepSizeMicrons);
            var innerRadiusCapturedPixels = model.InnerAnnulusRadiusPixels(extremeDefocusMicrons) / captureBinning;
            var hfrCapturedPixels = model.HfrAtDefocusMicrons(extremeDefocusMicrons) / captureBinning;

            var hasObstruction = dataset.CentralObstructionFraction > 0.0;
            var innerRadiusClears = innerRadiusCapturedPixels > DonutInnerRadiusFloorPixels;
            var hfrClears = hfrCapturedPixels >= DonutHeuristic.HeavyDefocusHFR;
            var donutExpected = hasObstruction && innerRadiusClears && hfrClears;

            string rationale;
            if (!hasObstruction) {
                rationale = $"unobstructed optic (eps={dataset.CentralObstructionFraction:0.00}) -- no donut is geometrically possible";
            } else if (!innerRadiusClears) {
                rationale = $"inner radius {innerRadiusCapturedPixels:0.00} px <= {DonutInnerRadiusFloorPixels:0.0} px at extreme defocus -- hole too small to register as a donut";
            } else if (!hfrClears) {
                rationale = $"HFR {hfrCapturedPixels:0.0} px < DonutHeuristic.HeavyDefocusHFR ({DonutHeuristic.HeavyDefocusHFR:0.0} px) at extreme defocus -- not heavily defocused enough";
            } else {
                rationale = $"eps={dataset.CentralObstructionFraction:0.00} > 0, inner radius {innerRadiusCapturedPixels:0.0} px > {DonutInnerRadiusFloorPixels:0.0} px, " +
                             $"HFR {hfrCapturedPixels:0.0} px >= DonutHeuristic.HeavyDefocusHFR ({DonutHeuristic.HeavyDefocusHFR:0.0} px) at extreme defocus";
            }
            return (donutExpected, rationale);
        }

        // ── Kernel-cap guard ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>Safety margin under <c>StarFieldCompositor.MaxAbsDefocusMicrons</c> the sweep's extreme frame must stay within.</summary>
        public const double KernelCapGuardUtilizationLimit = 0.9;

        /// <summary>
        /// Derives the kernel-cap guard: the sweep's most-defocused frame's |Δ| must stay at or under
        /// <see cref="KernelCapGuardUtilizationLimit"/> (90%) of <c>StarFieldCompositor.MaxAbsDefocusMicrons</c>
        /// — the largest defocus whose kernel support stays within the compositor's own safe render clamp.
        /// Never throws; returns the guard so a caller (the runner's <c>--dry-run</c>) can print the whole
        /// dataset table and decide, rather than aborting mid-table on the first dataset that fails it.
        /// </summary>
        public static SynthKernelCapGuard DeriveKernelCapGuard(DefocusModel model, int offsetSteps, int stepSize) {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var maxAbs = StarFieldCompositor.MaxAbsDefocusMicrons(model);
            var extreme = SweepExtremeDefocusMicrons(offsetSteps, stepSize, model.FocuserStepSizeMicrons);
            var utilization = maxAbs > 0.0 ? extreme / maxAbs : double.PositiveInfinity;
            var withinCap = maxAbs > 0.0 && extreme <= KernelCapGuardUtilizationLimit * maxAbs;

            return new SynthKernelCapGuard {
                MaxAbsDefocusMicrons = maxAbs,
                SweepExtremeDefocusMicrons = extreme,
                Utilization = utilization,
                WithinCap = withinCap
            };
        }

        /// <summary>The sweep's most-defocused frame's |Δ| (µm): <c>offsetSteps · stepSize · focuserStepSizeMicrons</c>. Shared by the donut and kernel-cap-guard derivations so they can never disagree on what "extreme" means.</summary>
        private static double SweepExtremeDefocusMicrons(int offsetSteps, int stepSize, double focuserStepSizeMicrons) {
            return offsetSteps * stepSize * focuserStepSizeMicrons;
        }

        // ── Truth model ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the dataset's geometric truth model: the sweep's <c>2·offsetSteps + 1</c> frame positions,
        /// each frame's defocus, and its kernel's analytic/measured HFR and outer/inner radii
        /// (<see cref="PsfKernelGenerator.Generate"/> + <paramref name="model"/> — the same construction the
        /// real renderer uses, so this truth is exact rather than a separate approximation of it). Native
        /// (pre-binning) pixels throughout, matching <see cref="StarTruth"/>'s convention.
        ///
        /// <para>Requires the sweep to already be within the kernel-cap guard
        /// (<see cref="DeriveKernelCapGuard"/>): at an out-of-cap defocus,
        /// <see cref="PsfKernelGenerator.Generate"/> throws (<c>PsfKernelGenerator.MaxKernelRadius</c>). Check
        /// the guard first — <see cref="DeriveExpectedOptimal"/> does exactly that.</para>
        /// </summary>
        public static SynthTruthModel BuildTruthModel(DefocusModel model, SynthDatasetSpec dataset, int offsetSteps, int stepSize) {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));

            var perFrame = new List<SynthPerFrameTruth>(2 * offsetSteps + 1);
            for (var k = -offsetSteps; k <= offsetSteps; k++) {
                var position = dataset.OptimalFocuserPosition + k * stepSize;
                var defocusMicrons = k * stepSize * model.FocuserStepSizeMicrons; // == (position - x0) * focuserStepSizeMicrons
                var kernel = PsfKernelGenerator.Generate(model, defocusMicrons);
                perFrame.Add(new SynthPerFrameTruth {
                    FocuserPosition = position,
                    DefocusMicrons = defocusMicrons,
                    AnalyticKernelHfrPixels = kernel.AnalyticHfrPixels,
                    MeasuredKernelHfrPixels = kernel.MeasuredHfrPixels,
                    OuterRadiusPixels = kernel.OuterRadiusPixels,
                    InnerRadiusPixels = kernel.InnerRadiusPixels
                });
            }

            return new SynthTruthModel {
                HfrMin = model.HfrMinPixels,
                HfrMinEffective = Math.Max(model.HfrMinPixels, PixelizationFloorPixels),
                Kappa = model.KappaPixelsPerStep,
                ArcsecPerPixel = model.ArcsecPerPixel,
                CaptureBinning = dataset.CaptureBinning,
                ReadNoiseNote = "Read noise is gain- and sensor-dependent (SensorDefinition.ReadNoiseElectronsAtGain); it enters the exposure-band derivation's sigma term (DeriveExposureBand), not this geometric truth model.",
                PerFrame = perFrame
            };
        }

        // ── Composition ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the dataset's <see cref="DefocusModel"/> from its optics/sensor/filter — the single owner of
        /// that mapping (plain-number constructor, "unit-test friendly") so every derivation below constructs
        /// an identical model for the same dataset.
        /// </summary>
        public static DefocusModel BuildDefocusModel(SynthDatasetSpec dataset, SynthBankDefaults defaults) {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));

            var sensor = SensorRegistry.Get(SynthRenderRequestFactory.ParseSensorModel(dataset.SensorModel));
            var filter = FilterRegistry.Get(SynthRenderRequestFactory.ParseFilter(dataset.Filter ?? defaults.Filter));

            return new DefocusModel(
                apertureMillimeters: dataset.ApertureMillimeters,
                focalLengthMillimeters: dataset.FocalLengthMm,
                centralObstructionFraction: dataset.CentralObstructionFraction,
                pixelSizeMicrons: sensor.PixelSizeMicrons,
                seeingArcsec: dataset.SeeingArcsec,
                wavelengthNm: filter.CentralWavelengthNm,
                focuserStepSizeMicrons: dataset.FocuserStepSizeMicrons,
                optimalFocuserPosition: dataset.OptimalFocuserPosition);
        }

        /// <summary>
        /// Composes every physics-only derivation above into one dataset's expected-optimal bootstrap
        /// parameters. Deliberately override-free (see the class remarks) — a caller wanting the checked-in
        /// spec's PINNED value instead must apply <see cref="SynthDatasetSpec"/>'s <c>*Override</c> fields
        /// itself, on top of this method's physics answer, so any disagreement between the two stays visible.
        ///
        /// <para><paramref name="catalogReader"/> may be null, in which case the exposure fields are left NaN
        /// with an explanatory <see cref="SynthExpectedOptimal.ExposureDefinition"/> and every catalog-free
        /// field (step size, detection binning, donut expectation, kernel-cap guard, truth model) is still
        /// computed — this is what lets the kernel-cap guard be checked, and the whole per-dataset table be
        /// printed, BEFORE any catalog access, exactly as the design's <c>--dry-run</c> requires.</para>
        ///
        /// <para><paramref name="truthModel"/> is null when <paramref name="kernelCapGuard"/> fails
        /// (<c>WithinCap</c> = false): building it would call <see cref="PsfKernelGenerator.Generate"/> at an
        /// out-of-cap defocus, which throws rather than degrading gracefully. The guard is the caller's signal
        /// to stop before that happens, matching the design's harness self-verification order (kernel-cap guard
        /// is checked before any render).</para>
        /// </summary>
        /// <param name="detectBoundedStep">
        /// F18 arm switch. False (the default) derives <c>step*</c> from curve geometry alone, byte-identical to
        /// every prior wave. True additionally bounds it by <see cref="DeriveDetectableHalfWidth"/>.
        ///
        /// <para><b>Why it is a switch and not a fix.</b> The rule has to be applied to the bank's TARGET and to
        /// the shipped recommender together or not at all: an arm that changed only the recommender would be
        /// scored against a target that still says curve geometry is right, and an arm that changed only the
        /// target would score today's recommender as newly broken. One flag, both places, so each arm is
        /// internally consistent and arm C reproduces `develop` exactly.</para>
        /// </param>
        public static SynthExpectedOptimal DeriveExpectedOptimal(
                SynthDatasetSpec dataset, SynthBankDefaults defaults, IAstapCatalogReader catalogReader,
                out SynthKernelCapGuard kernelCapGuard, out SynthTruthModel truthModel, CancellationToken token = default,
                bool detectBoundedStep = false) {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));

            var model = BuildDefocusModel(dataset, defaults);
            var offsetSteps = defaults.OffsetSteps;
            var geometricStep = DeriveStepSize(model);
            var stepSize = geometricStep;
            var detectionBinning = DeriveDetectionBinning(model, dataset.CaptureBinning);

            double exposureSeconds, bandLow, bandHigh;
            string exposureDefinition;
            var exposureRaw = double.NaN;
            var exposureClamp = "not-derived";
            if (catalogReader == null) {
                exposureSeconds = bandLow = bandHigh = double.NaN;
                exposureDefinition = "No catalog reader supplied; the exposure band was not derived (catalog-free fields only).";
            } else {
                // Derived at the GEOMETRIC step, and deliberately NOT re-derived after the detectability bound
                // narrows the sweep. Re-deriving would couple the exposure axis to the step axis, and this wave
                // holds the exposure axis still on purpose (F19 decided no change there). A narrower sweep has a
                // slightly higher median peak fraction, so keeping the geometric-step exposure is the CONSERVATIVE
                // direction — marginally more exposure than the narrowed sweep needs, never less.
                (exposureSeconds, bandLow, bandHigh, exposureDefinition, exposureRaw, exposureClamp) =
                    DeriveExposureBand(dataset, defaults, model, catalogReader, dataset.CaptureBinning, detectionBinning,
                        geometricStep, offsetSteps, token);
            }

            var detectableHalfWidth = double.NaN;
            var stepSizeDefinition = $"step* = sqrt(8)*max(HFR_min, {PixelizationFloorPixels:0.00})/(kappa*{PointsPerSide}) " +
                "= the fitted 3x-min-HFR half-width over PointsPerSide (curve geometry alone)";
            if (detectBoundedStep) {
                var (halfWidth, _, detectDefinition) = DeriveDetectableHalfWidth(
                    dataset, defaults, model, catalogReader, dataset.CaptureBinning, detectionBinning,
                    geometricStep, offsetSteps, exposureSeconds, token);
                detectableHalfWidth = halfWidth;
                stepSize = DeriveStepSizeDetectBounded(model, geometricStep, offsetSteps, halfWidth);
                stepSizeDefinition = $"F18 detectability-bounded: step* = min(W_3x, max(W_detect, {MinHalfWidthSampledHalfSpanMultiple:0.0}*sampled half-span))/{PointsPerSide}. " +
                    $"Geometry alone gives {geometricStep}; {detectDefinition}. Result: {stepSize}.";
            }

            var (donutExpected, donutRationale) = DeriveDonutExpectation(dataset, model, offsetSteps, stepSize, dataset.CaptureBinning);
            kernelCapGuard = DeriveKernelCapGuard(model, offsetSteps, stepSize);
            truthModel = kernelCapGuard.WithinCap ? BuildTruthModel(model, dataset, offsetSteps, stepSize) : null;

            return new SynthExpectedOptimal {
                ExposureSeconds = exposureSeconds,
                ExposureBandLowSeconds = bandLow,
                ExposureBandHighSeconds = bandHigh,
                ExposureDefinition = exposureDefinition,
                ExposureRawSeconds = exposureRaw,
                ExposureClamp = exposureClamp,
                StepSizeSteps = stepSize,
                StepSizeTolerance = 0.4,
                DetectableHalfWidthSteps = detectableHalfWidth,
                StepSizeDefinition = stepSizeDefinition,
                OffsetSteps = offsetSteps,
                AutofocusBinning = dataset.CaptureBinning,
                DetectionBinning = detectionBinning,
                DonutDetection = donutExpected,
                DonutRationale = donutRationale
            };
        }
    }
}
