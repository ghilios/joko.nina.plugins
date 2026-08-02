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
        /// <param name="token">Cancellation for the render.</param>
        public static (double ExposureSeconds, double BandLowSeconds, double BandHighSeconds, string Definition) DeriveExposureBand(
                SynthDatasetSpec dataset, SynthBankDefaults defaults, DefocusModel model,
                IAstapCatalogReader catalogReader, int captureBinning, int detectionBinning, CancellationToken token = default) {
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
                    "No catalog stars landed on-frame at the in-focus position; the exposure band cannot be derived from truth (starless pointing, or the ASTAP catalog is missing/unreadable).");
            }

            // t = ReferenceExposureSeconds = 1 s, so FluxElectrons/SkyElectronsPerPixel/DarkElectronsPerPixel
            // ARE the per-second rates directly (all three are exactly linear in exposure time).
            var fluxPerSecond = star.FluxElectrons;
            var radiometry = RadiometryCalculator.FromRequest(request, sensor, filter);
            var skyPlusDarkRatePerSecond = radiometry.SkyElectronsPerPixel() + radiometry.DarkElectronsPerPixel();
            var readNoise = sensor.ReadNoiseElectronsAtGain(gain);

            var effectiveBinning = captureBinning * detectionBinning;
            // GoldenFromTruth.PeakFractionBinned (not a reimplementation of its min(1, ...) cap here): the exposure
            // derivation and the golden tiering must agree exactly on how a star's peak fraction saturates toward
            // 1.0 as binning grows, or the two could silently drift apart on a future retune of the cap.
            var peakFractionBinned = GoldenFromTruth.PeakFractionBinned(star.KernelPeakFraction, effectiveBinning);

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
                $"Exposure t solving snr(t) = {GateSnrPeakCoefficient:0.00}·peakElectrons(t)/σ_bg(t) = {ExposureRecommender.TargetSensitivity:0} for the {starDescription} " +
                $"on the in-focus (median) sweep frame, at binning={effectiveBinning} (captureBinning={captureBinning}×detectionBinning={detectionBinning}), " +
                $"clamped to [{MinExposureSeconds:0.0}, {ExposureRecommender.MaxRecommendedExposureSeconds:0}] s and rounded on ExposureRecommender.RoundExposureSeconds' ladder. " +
                $"Band = t at snr(t) = {GateSnrBandFloor:0} (low) and snr(t) = {GateSnrBandCeiling:0} (high).";

            return (exposureSeconds, bandLow, bandHigh, definition);
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
                rationale = $"unobstructed optic (ε={dataset.CentralObstructionFraction:0.00}) — no donut is geometrically possible";
            } else if (!innerRadiusClears) {
                rationale = $"inner radius {innerRadiusCapturedPixels:0.00} px ≤ {DonutInnerRadiusFloorPixels:0.0} px at extreme defocus — hole too small to register as a donut";
            } else if (!hfrClears) {
                rationale = $"HFR {hfrCapturedPixels:0.0} px < DonutHeuristic.HeavyDefocusHFR ({DonutHeuristic.HeavyDefocusHFR:0.0} px) at extreme defocus — not heavily defocused enough";
            } else {
                rationale = $"ε={dataset.CentralObstructionFraction:0.00} > 0, inner radius {innerRadiusCapturedPixels:0.0} px > {DonutInnerRadiusFloorPixels:0.0} px, " +
                             $"HFR {hfrCapturedPixels:0.0} px ≥ DonutHeuristic.HeavyDefocusHFR ({DonutHeuristic.HeavyDefocusHFR:0.0} px) at extreme defocus";
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
        public static SynthExpectedOptimal DeriveExpectedOptimal(
                SynthDatasetSpec dataset, SynthBankDefaults defaults, IAstapCatalogReader catalogReader,
                out SynthKernelCapGuard kernelCapGuard, out SynthTruthModel truthModel, CancellationToken token = default) {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));

            var model = BuildDefocusModel(dataset, defaults);
            var offsetSteps = defaults.OffsetSteps;
            var stepSize = DeriveStepSize(model);
            var detectionBinning = DeriveDetectionBinning(model, dataset.CaptureBinning);
            var (donutExpected, donutRationale) = DeriveDonutExpectation(dataset, model, offsetSteps, stepSize, dataset.CaptureBinning);

            kernelCapGuard = DeriveKernelCapGuard(model, offsetSteps, stepSize);
            truthModel = kernelCapGuard.WithinCap ? BuildTruthModel(model, dataset, offsetSteps, stepSize) : null;

            double exposureSeconds, bandLow, bandHigh;
            string exposureDefinition;
            if (catalogReader == null) {
                exposureSeconds = bandLow = bandHigh = double.NaN;
                exposureDefinition = "No catalog reader supplied; the exposure band was not derived (catalog-free fields only).";
            } else {
                (exposureSeconds, bandLow, bandHigh, exposureDefinition) =
                    DeriveExposureBand(dataset, defaults, model, catalogReader, dataset.CaptureBinning, detectionBinning, token);
            }

            return new SynthExpectedOptimal {
                ExposureSeconds = exposureSeconds,
                ExposureBandLowSeconds = bandLow,
                ExposureBandHighSeconds = bandHigh,
                ExposureDefinition = exposureDefinition,
                StepSizeSteps = stepSize,
                StepSizeTolerance = 0.4,
                OffsetSteps = offsetSteps,
                AutofocusBinning = dataset.CaptureBinning,
                DetectionBinning = detectionBinning,
                DonutDetection = donutExpected,
                DonutRationale = donutRationale
            };
        }
    }
}
