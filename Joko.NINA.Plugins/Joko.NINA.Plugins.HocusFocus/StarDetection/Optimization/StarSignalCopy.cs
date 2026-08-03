#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// Every word of the optimizer Summary's "Star signal" block: the recommended-exposure ROW, the body PARAGRAPH
    /// under it, and the row's derivation TOOLTIP. Pure and static — it reads an
    /// <see cref="OptimizationSummary"/> (plus the one fact the VM knows that the summary does not: whether the
    /// run was a Live sweep) and returns strings, so every copy state is unit-testable without a VM.
    ///
    /// <para><b>Why these live together, away from the VM.</b> The block's whole difficulty is that its three
    /// surfaces must not repeat or contradict one another: the row carries the numbers, the tooltip carries the
    /// derivation, and the paragraph is capped at a diagnosis plus AT MOST ONE instruction (see
    /// <see cref="DescribeExposureRecommendation"/> for the version that shipped six sentences offering three
    /// mutually exclusive remedies). That invariant spans all of them, so it can only be reviewed if they are read
    /// together — which, scattered across a 4,000-line ViewModel and a summary model, they were not. Nothing here
    /// touches VM state; <c>StarDetectionOptimizerWizardVM</c> keeps only the bound properties that select the
    /// variant and hand it over.</para>
    ///
    /// <para>The <c>MyMessageBox</c> confirmation bodies deliberately stay on the VM beside
    /// <c>DescribeReoptimizeAtBinning</c>: those are typeset to a hard line budget for a dialog that does not wrap,
    /// which is a different constraint from anything here, and they belong with their sibling prompt.</para>
    /// </summary>
    public static class StarSignalCopy {

        /// <summary>
        /// The recommended-exposure row's value, e.g. "3 s → 12 s (measured star S/N 4.1; target 10)". Empty when
        /// there is no derived number at all, which hides the row (the block still shows its diagnosis body).
        ///
        /// <para>Branches on <see cref="ExposureRecommendation.IncreasesExposure"/>, NOT on
        /// <see cref="ExposureRecommendation.HasRecommendation"/>: the recommender never returns an exposure
        /// SHORTER than the current one, so when the current exposure already exceeds
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/> (realistic for narrowband) the
        /// recommendation collapses onto the current value while <c>HasRecommendation</c> stays true on purpose.
        /// Rendering that as "{current} → {recommended}" would print a no-op "40 s → 40 s" row, so that case takes
        /// the "(unchanged; …)" form the detection-binning row already uses for the same situation.</para>
        /// </summary>
        public static string DescribeExposureRow(OptimizationSummary summary) {
            var advice = summary?.ExposureAdvice;
            if (advice == null || !advice.HasRecommendation) {
                return string.Empty;
            }
            var measured = $"measured star S/N {advice.MeasuredSnr:0.#}; target {ExposureRecommender.TargetSensitivity:0.#}";
            return advice.IncreasesExposure
                ? $"{advice.CurrentSeconds:0.##} s → {advice.RecommendedSeconds:0.##} s ({measured})"
                : $"{advice.CurrentSeconds:0.##} s (unchanged; {measured})";
        }

        /// <summary>
        /// The Star signal block's body copy. Pure and static so every copy state is unit-testable without a VM,
        /// following <see cref="StarDetectionOptimizerWizardVM.DescribeReoptimizeAtBinning"/>.
        ///
        /// <para><b>Shape: diagnosis, then AT MOST ONE remedy.</b> Replay, a binding cap, and a competing
        /// detection-binning recommendation are independently reachable, and an earlier version simply concatenated
        /// a sentence per condition — six sentences offering three remedies that contradicted each other ("use a
        /// different filter instead", "raise the exposure anyway", "do not raise it yet, change the binning
        /// factor first"). The sibling binning block's rule applies here too: the row gives the numbers, the
        /// tooltip carries the derivation (see <see cref="DescribeExposureDerivation"/>), and the paragraph adds
        /// only what to DO. So the remedies are ranked and exactly one is emitted — see <c>RemedyFor</c>.</para>
        ///
        /// <para><b>The opening is state-dependent, and leads with the gate.</b> The gate being floored is the one
        /// fact true in every state; "stars barely cleared the noise" is NOT — under
        /// <see cref="ExposureRecommendation.ExposureIsNotTheLimit"/> the measured S/N is at or above the default
        /// gate, and an unconditional low-signal opening contradicted the very next sentence. It also never blames
        /// the optimizer: this block follows the SELECTED VARIANT, and on the Current view the gate on screen is
        /// the user's own hand-set value.</para>
        ///
        /// <para><b>No "star-poor" claim without evidence.</b> <see cref="ExposureRecommendation.MeasuredSnr"/> is
        /// the NTarget-th BRIGHTEST accepted star, so <c>ExposureIsNotTheLimit</c> (S_now ≥ target) actually
        /// describes a RICH field — the median frame had NTarget stars at or above the default gate. The signal
        /// that a field was too thin is <see cref="ExposureRecommendation.ShortFrameCount"/>, so that is what the
        /// star-poor sentence is gated on.</para>
        ///
        /// <para><b>Two traps, both carried forward from the recommender's own review.</b> (1) Any "raise it to X"
        /// text branches on <see cref="ExposureRecommendation.IncreasesExposure"/>, never on
        /// <see cref="ExposureRecommendation.HasRecommendation"/>: the recommender has a never-shorter-than-current
        /// floor, so an already-long exposure (past the absolute cap — realistic for narrowband) collapses the
        /// recommendation onto the current value while <c>HasRecommendation</c> stays true, and a naive branch
        /// would offer the user the exposure they already use. (2) The capped copy is selected by
        /// <see cref="ExposureRecommendation.CapLimitsRecommendation"/>, not
        /// <see cref="ExposureRecommendation.WasCapped"/>: <c>WasCapped</c> only says the cap reduced
        /// <see cref="ExposureRecommendation.RawSeconds"/>, which can be true while nothing delivered was shaped by
        /// it. And WHICH cap bound matters just as much: the run-relative
        /// <see cref="ExposureRecommender.MaxExposureFactor"/> binding means "this is a partial step, re-run and
        /// refine" (its own doc says so), while only the absolute
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/> means "an auto-focus sweep cannot spend
        /// this much". A 2 s run wanting 9.9 s is capped at 8 s by the RELATIVE bound and is nowhere near the
        /// ceiling — telling that user to change filters would be nonsense, so
        /// <see cref="ExposureRecommendation.CappedByAbsoluteLimit"/> discriminates.</para>
        ///
        /// <para><b>Detection-binning interaction.</b> <c>FrameStarSnrs</c> are per-BINNED-pixel, so the derived
        /// exposure is self-consistent within the run but assumes the run's own binning factor; a higher factor
        /// raises measured SNR on its own. When this same page ALSO offers a binning change
        /// (<see cref="OptimizationSummary.DetectionBinningDiffers"/>), accepting both would over-lengthen the
        /// exposure. The block is deliberately NOT suppressed in that case — the diagnosis ("this result rests on
        /// low-confidence detections") is exactly what the user must be told, and it is true regardless of binning
        /// — so the copy is QUALIFIED instead, and because the ordering instruction is a remedy it OUTRANKS the
        /// others: changing the factor re-measures the very SNRs every other remedy is derived from. Note this does
        /// NOT apply to <see cref="OptimizationSummary.DetectionBinningPendingApply"/>: an optimize-again pass
        /// already ran, and was measured, at the factor Accept will write, so its SNRs are already in the space the
        /// user is about to commit to.</para>
        /// </summary>
        internal static string DescribeExposureRecommendation(OptimizationSummary summary, bool lastRunWasLive, bool isUseCurrentMode) {
            if (summary == null || !summary.HasLowStarSignal) {
                return string.Empty;
            }
            var advice = summary.ExposureAdvice;
            var measured = advice != null && advice.HasRecommendation;
            // A run is "low signal" for the purposes of the opening only when we actually MEASURED stars sitting
            // below the default gate. With no derivation at all we know only that the gate is floored, and under
            // ExposureIsNotTheLimit we know the opposite — so both take the neutral opening.
            var starsBarelyCleared = measured && !advice.ExposureIsNotTheLimit;

            // Gate value at the same F3 precision the Changed parameters table below prints it at, so the two
            // cannot show the same number differently ("0.13" vs "0.125"). The range is described rather than
            // quoted — its upper bound has already been widened once (20 -> 50), and the tooltip is where the
            // actual numbers belong.
            var gate = $"the bottom of its range ({summary.VariantSensitivity:F3})";
            var text = starsBarelyCleared
                ? $"Stars barely cleared the noise and Brightness Sensitivity is at {gate}: this focus result rests on low-confidence detections."
                : $"Brightness Sensitivity is at {gate}: this focus result rests on low-confidence detections.";

            // The derivation rests on an exposure nothing recorded — say so BEFORE quoting any of it. Gated on
            // `measured` as well as on the flag: with no derivation there is no row and no tooltip, so "this
            // assumes your profile's 3 s exposure" would have nothing on screen for "this" to refer to.
            if (measured && summary.RunExposureIsAssumed && double.IsFinite(summary.RunExposureSeconds)) {
                text += $" These frames record no exposure; the numbers assume your profile's {summary.RunExposureSeconds:0.##} s auto-focus exposure.";
            }

            // Diagnosis continued — facts, no instructions. The magnitudes live in the row and its tooltip.
            if (measured) {
                if (advice.ExposureIsNotTheLimit) {
                    // Scoped to BRIGHTNESS, not to exposure in general: this lead is shared with the star-poor
                    // sub-branch below, and "exposure is not what is limiting this run" is a claim about a knob
                    // this statistic cannot see. MeasuredSnr is the NTarget-th BRIGHTEST accepted star (median
                    // across frames), NOT a property of "the stars that were accepted" — at a 0.125 gate the
                    // accepted set runs from 0.125 upward, so claiming they all measure 22 is simply false. The
                    // tail clause reconciles the two facts instead of leaving them looking contradictory.
                    text += $" Star brightness is not the problem: your brightest stars measure S/N {advice.MeasuredSnr:0.#}, meeting the default S/N target of {ExposureRecommender.TargetSensitivity:0.#}. Brightness Sensitivity is low, so far fainter candidates are being admitted below them.";
                    // PARTIAL-short only. ExposureIsNotTheLimit already excludes all-short by construction (see
                    // ExposureRecommendation.StarCountIsTheLimit), so the upper bound is belt-and-braces against a
                    // hand-built advice object — without it an all-short run would read "9 of 9 frames ... the low
                    // gate is scraping for count", which blames the gate for a shortfall it did not cause.
                    if (advice.ShortFrameCount > 0 && advice.ShortFrameCount < advice.UsableFrameCount) {
                        text += $" {advice.ShortFrameCount} of {advice.UsableFrameCount} frames found fewer stars than the star-count target; Brightness Sensitivity is low to scrape for count in a star-poor field.";
                    }
                } else if (advice.StarFieldIsExhausted) {
                    // The end of the probe. Every frame short, stars bright, and the gate rejecting NOTHING: there
                    // is no candidate waiting for more signal and none forming. Offering another doubling here is
                    // what walked the reported rig from 2 s to 14 s for six extra stars, so this state offers no
                    // exposure at all and names the levers that can actually move a small field.
                    text += $" All {advice.UsableFrameCount} frames found fewer stars than the star-count target, and no candidate was rejected for being too faint — these frames contain every star the detector can find. A longer exposure will not add more. Long focal lengths see few stars per frame; detection binning, a wider field, or accepting that this field supports fewer stars are the options.";
                } else if (advice.StarCountIsTheLimit && advice.IncreasesExposure) {
                    // Bright enough, too few. This branch previously asserted that a longer exposure "cannot raise
                    // that" — false, and the measurement that was cited only ever tested the GATE. Exposure acts
                    // upstream of the gate, on candidate formation: 2 s → 5 s on the reported rig took structure
                    // candidates 181 → 201 and detected stars 76 → 101. But that is one field, and whether more
                    // exposure reveals more stars depends on the luminosity function this run cannot measure — so
                    // the copy prescribes an EXPERIMENT with its own stopping rule, not a verdict either way.
                    text += $" All {advice.UsableFrameCount} frames found fewer stars than the star-count target: the stars found are bright enough (S/N {advice.MeasuredSnr:0.#}), there are just too few. A longer exposure may or may not find more; try doubling it, and if the star count does not rise, the field is the limit and no exposure will fix it.";
                } else if (!advice.IncreasesExposure) {
                    text += " Reaching the default S/N target would need more time per frame than an auto-focus sweep can spend; no longer exposure is offered.";
                } else if (advice.CapLimitsRecommendation) {
                    // "The recommendation above", not "That": the opening sentence quotes no number, so a bare
                    // pronoun would reach past it to the ROW — a different control. The sibling binning block names
                    // its referent explicitly in the same situation.
                    text += advice.CappedByAbsoluteLimit
                        ? $" Reaching the default S/N target would need more time per frame than a sweep can spend; the recommendation above stops at {advice.RecommendedSeconds:0.##} s."
                        : $" The recommendation above is a partial step: one run raises exposure at most {ExposureRecommender.MaxExposureFactor:0}x. Expect to repeat this.";
                }

                // Sweep GEOMETRY, not exposure or the gate — a different knob, so it rides only on the two
                // star-count states where the user is already being told what limits the count. Flat-topped
                // rejections mean the sweep reaches focuser positions where stars have spread into featureless
                // discs; no exposure recovers those, and the gate that rejects them has no defocus relaxation.
                if (advice.FlatRejectedCount > 0 && (advice.StarFieldIsExhausted || advice.StarCountIsTheLimit)) {
                    text += $" Separately, {advice.FlatRejectedCount} candidate(s) were discarded for being flat and featureless, which happens on frames far enough from focus that stars spread into plain discs. A smaller step size keeps more of the sweep close enough to focus to be measurable.";
                }
            }

            // Replay's reassurance: a page carrying a warning has to say that Accept is still a legitimate choice,
            // and Replay is the mode with no action of its own to take. It comes BEFORE the remedy when there is
            // one, and stands alone when there is not — which is the whole of its placement rule, so it is emitted
            // once here rather than once per branch of the remedy test.
            if (!lastRunWasLive) {
                text += " You can still accept these settings; they are the best fit for frames like these.";
            }
            var remedy = RemedyFor(summary, advice, lastRunWasLive, isUseCurrentMode);
            if (!string.IsNullOrEmpty(remedy)) {
                text += " " + remedy;
            }
            return text;
        }

        /// <summary>
        /// The single instruction the body ends on, or empty when there is nothing useful to tell the user yet.
        /// Ranked, because more than one can apply at once and they contradict each other:
        ///
        /// <list type="number">
        /// <item><b>Change the binning factor first.</b> It re-measures the per-binned-pixel SNRs every other
        /// remedy here is derived from, so any exposure instruction given alongside it would be computed against a
        /// factor the user is about to change. This fires in "use current settings" mode too, where the
        /// "Optimize again at NxN" button is hidden — a DELIBERATE exception to the house rule at
        /// <see cref="StarDetectionOptimizerWizardVM.ShowOptimizeAgainAtRecommendedBinning"/> about never describing an action whose control is
        /// hidden, because this sentence names a SETTING to change, not that button: it stays true and actionable
        /// from the options page in either mode, and the binning block's own body already spells out the mode
        /// requirement right below it.</item>
        /// <item><b>Use a different filter.</b> ONLY when the exposure route is genuinely exhausted — i.e.
        /// <c>!IncreasesExposure</c>, where the sentence before it has just said there is no longer exposure to
        /// offer, which is what gives "instead" its antecedent. NOT merely because
        /// <see cref="ExposureRecommendation.CappedByAbsoluteLimit"/> is set: the absolute cap can bind while the
        /// recommendation STILL raises the exposure (10 s at S/N 5 wants 40 s, capped to 30 s — a real 3x
        /// improvement sitting in the row), and offering the filter route "instead" there advises against the
        /// offer directly above it while suppressing the only sentence that says where to apply it.</item>
        /// <item><b>Get frames at the longer exposure.</b> The one remaining instruction, and it is the SAME
        /// instruction in all three of its shapes — only the way to carry it out differs, so they share this slot
        /// rather than competing for it. Each carries the narrowband suggestion as a RIDER when the absolute cap
        /// trimmed the number, so that hint survives without becoming a competing instruction.
        ///
        /// <list type="bullet">
        /// <item>LIVE + Optimize names the capture action that sits directly below the paragraph: as with the
        /// binning block, the body's only job is to say why that button exists, since the row already carries the
        /// number and the tooltip the derivation. It quotes no seconds — the editable box beside the button is the
        /// authority on the value.</item>
        /// <item>LIVE + "use current settings" names the MODE to switch to, because
        /// <see cref="StarDetectionOptimizerWizardVM.ShowCaptureNewSweep"/> HIDES the button there (a use-current
        /// run has nothing to re-tune, and the mode is fixed for the life of the Summary, so a disabled button
        /// would sit dead for the whole run with no explanation). This mirrors the binning block's own
        /// use-current branch exactly.</item>
        /// <item>REPLAY has no capture action and no write-back for the exposure
        /// (<see cref="StarDetectionOptimizerWizardVM.CanApplyExposureTime"/> is false), so if its sentence does
        /// not say where to set the value by hand, nothing does.</item>
        /// </list>
        ///
        /// <para>The whole slot is gated on <see cref="ExposureRecommendation.IncreasesExposure"/>, and the Live
        /// branches split on exactly the remaining term of
        /// <see cref="StarDetectionOptimizerWizardVM.ShowCaptureNewSweep"/> — so the sentence that names the button
        /// and the button itself appear and disappear together (the house rule at
        /// <see cref="StarDetectionOptimizerWizardVM.ShowOptimizeAgainAtRecommendedBinning"/>). What remains in
        /// <see cref="StarDetectionOptimizerWizardVM.CanCaptureNewSweep"/> is only the TRANSIENT unavailability (no
        /// engine, a disconnected camera or focuser, no save folder), which renders the button disabled rather than
        /// hidden — so the sentence still has a referent, and the referent can come back.</para></item>
        /// </list>
        /// </summary>
        private static string RemedyFor(
            OptimizationSummary summary, ExposureRecommendation advice, bool lastRunWasLive, bool isUseCurrentMode) {
            var increases = advice != null && advice.IncreasesExposure;
            if (summary.DetectionBinningDiffers && increases) {
                return "The detection binning change recommended below also raises measured star signal. Change the binning factor first and let the next run re-measure the exposure.";
            }
            if (advice != null && advice.HasRecommendation && !advice.ExposureIsNotTheLimit && !advice.IncreasesExposure) {
                return "If you shoot narrowband, auto-focus through a broadband filter with a filter offset instead.";
            }
            if (increases) {
                // Live acts on the rig it is attached to; Replay can only tell the user where the setting lives; a
                // use-current run can do neither until it is re-run in Optimize mode, which is where its button went.
                string text;
                if (!lastRunWasLive) {
                    text = $"Raise your auto-focus exposure to about {advice.RecommendedSeconds:0.##} s in NINA's focuser options, then run this wizard again in Live mode.";
                } else if (isUseCurrentMode) {
                    text = "Run this wizard in Optimize mode to capture a new sweep at the longer exposure and re-tune for the new frames.";
                } else {
                    text = "Capture a new sweep at the longer exposure and re-tune these settings on frames with real signal.";
                }
                if (advice.CappedByAbsoluteLimit) {
                    // The cap trimmed the number, so this is a partial fix in either mode. The filter route rides
                    // along as an alternative rather than replacing it — same sentence, so "one instruction" holds.
                    text = text.TrimEnd('.')
                        + "; or, if you shoot narrowband, auto-focus through a broadband filter with a filter offset instead.";
                }
                return text;
            }
            return string.Empty;
        }

        /// <summary>
        /// The recommended-exposure row's tooltip: the arithmetic the number came from, what a whole sweep would
        /// cost at the un-capped figure, and which cap trimmed it. This is BACKGROUND, which is why it is here and
        /// not in the paragraph — the body is capped at diagnosis plus one instruction (see
        /// <see cref="DescribeExposureRecommendation"/>), and the raw derivation is the first thing that has to
        /// move out when a state stacks. Empty exactly when the row itself is hidden, so the bound tooltip never
        /// renders as an empty box.
        /// </summary>
        internal static string DescribeExposureDerivation(OptimizationSummary summary) {
            var advice = summary?.ExposureAdvice;
            if (summary == null || !summary.HasLowStarSignal || advice == null || !advice.HasRecommendation) {
                return string.Empty;
            }
            if (advice.ExposureIsNotTheLimit) {
                return $"Measured S/N {advice.MeasuredSnr:0.#} already meets the S/N target of {ExposureRecommender.TargetSensitivity:0.#}: the S/N math derives no longer exposure.";
            }
            // The probe state has no derivation to show — its number is a fixed factor, not a result — so the
            // tooltip has to say that outright. Printing the sky-limited formula here would show a ratio ≤ 1
            // arguing for a SHORTER exposure directly beneath a row proposing a longer one.
            if (advice.StarCountIsTheLimit) {
                var probe = $"S/N {advice.MeasuredSnr:0.#} already meets the S/N target of {ExposureRecommender.TargetSensitivity:0.#}, so there is no S/N shortfall to derive from. The frames are short of stars, not of signal: this is a {ExposureRecommender.StarCountProbeFactor:0}× probe, {advice.CurrentSeconds:0.##} s → {FormatExposureSeconds(advice.RawSeconds)} s per frame{DescribeSweepCost(advice.RawSeconds, summary.CurrentOffsetSteps)}. Whether more exposure finds more stars depends on the field, so check the star count afterwards.";
                if (advice.GateIsProvablyInert) {
                    // Say why the probe is being offered with no rejection evidence behind it. This belongs in the
                    // derivation tooltip rather than the body: the body is capped at diagnosis plus one
                    // instruction, and this is background on where the number came from.
                    probe += $" No candidate was rejected for being too faint, but at a Brightness Sensitivity of {summary.VariantSensitivity:0.###} none could be: the detector's own clipping guarantees every candidate measures at least {advice.InertGateBound:0.##} S/N, so that count is empty by construction and is not evidence either way.";
                }
                if (advice.CapLimitsRecommendation) {
                    probe += advice.CappedByAbsoluteLimit
                        ? $" Capped at {advice.RecommendedSeconds:0.##} s: {ExposureRecommender.MaxRecommendedExposureSeconds:0} s per frame is the ceiling a sweep can sustain."
                        : $" Capped at {advice.RecommendedSeconds:0.##} s: one run raises exposure at most {ExposureRecommender.MaxExposureFactor:0}×.";
                }
                return probe;
            }
            // Sky-limited scaling: sigma goes as sqrt(t), so an SNR ratio r needs r-squared times the exposure.
            var text = $"Sky-limited scaling: {advice.CurrentSeconds:0.##} s × ({ExposureRecommender.TargetSensitivity:0.#} / {advice.MeasuredSnr:0.#})² = {FormatExposureSeconds(advice.RawSeconds)} s per frame{DescribeSweepCost(advice.RawSeconds, summary.CurrentOffsetSteps)}.";
            if (!advice.IncreasesExposure) {
                // "will suggest", not "a sweep can sustain": this branch is reached by a user whose CURRENT
                // exposure is already longer than the cap, so a flat claim about what a sweep can sustain is
                // contradicted by their own working setup.
                text += $" That is past the {ExposureRecommender.MaxRecommendedExposureSeconds:0} s this recommendation will suggest, and past what you already use; nothing longer is offered.";
            } else if (advice.CapLimitsRecommendation) {
                text += advice.CappedByAbsoluteLimit
                    ? $" Capped at {advice.RecommendedSeconds:0.##} s: {ExposureRecommender.MaxRecommendedExposureSeconds:0} s per frame is the ceiling a sweep can sustain."
                    : $" Capped at {advice.RecommendedSeconds:0.##} s: one run raises exposure at most {ExposureRecommender.MaxExposureFactor:0}x. Run again to refine.";
            }
            if (advice.ShortFrameCount > 0) {
                text += $" {advice.ShortFrameCount} of {advice.UsableFrameCount} frames had fewer stars than the star-count target; the derived exposure under-states what is needed.";
            }
            return text;
        }

        /// <summary>Seconds for prose: one decimal below 10 s (where half-seconds matter), whole seconds above —
        /// a four-digit exposure printed to a tenth reads as false precision on a figure that is an extrapolation.</summary>
        private static string FormatExposureSeconds(double seconds) =>
            seconds < 10.0 ? seconds.ToString("0.#") : seconds.ToString("0");

        /// <summary>The ", roughly N minutes per auto-focus run" clause: what a whole sweep would cost at
        /// <paramref name="perFrameSeconds"/>, over the <c>2·offset+1</c> points an auto-focus run samples. Empty
        /// when the offset is unknown or the total would not round to at least "2 minutes" — the clause exists to
        /// make an impractical number FEEL impractical, and both "roughly 0 minutes" and the ungrammatical
        /// "roughly 1 minutes" do the opposite. (1.5 is the cutoff rather than 1.0 precisely because
        /// <c>{minutes:0}</c> rounds, so anything below it renders as "0" or "1".)</summary>
        private static string DescribeSweepCost(double perFrameSeconds, int offsetSteps) {
            if (!double.IsFinite(perFrameSeconds) || perFrameSeconds <= 0.0 || offsetSteps <= 0) {
                return string.Empty;
            }
            var minutes = perFrameSeconds * (2 * offsetSteps + 1) / 60.0;
            return minutes < 1.5 ? string.Empty : $", roughly {minutes:0} minutes per auto-focus run";
        }
    }
}
