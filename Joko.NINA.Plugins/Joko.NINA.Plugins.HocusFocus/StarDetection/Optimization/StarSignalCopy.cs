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
        internal static string DescribeExposureRecommendation(OptimizationSummary summary, bool lastRunWasLive) {
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
            var gate = $"{summary.VariantSensitivity:F3}, at the bottom of its range";
            var text = starsBarelyCleared
                ? $"Stars in these frames barely cleared the noise, and Brightness Sensitivity sits at {gate}, so this focus result rests on low-confidence detections."
                : $"Brightness Sensitivity sits at {gate}, so this focus result rests on low-confidence detections.";

            // The derivation rests on an exposure nothing recorded — say so BEFORE quoting any of it. Gated on
            // `measured` as well as on the flag: with no derivation there is no row and no tooltip, so "this
            // assumes your profile's 3 s exposure" would have nothing on screen for "this" to refer to.
            if (measured && summary.RunExposureIsAssumed && double.IsFinite(summary.RunExposureSeconds)) {
                text += $" These frames record no exposure, so this assumes your profile's {summary.RunExposureSeconds:0.##} s auto-focus exposure.";
            }

            // Diagnosis continued — facts, no instructions. The magnitudes live in the row and its tooltip.
            if (measured) {
                if (advice.ExposureIsNotTheLimit) {
                    // Name what was actually measured. MeasuredSnr is the NTarget-th BRIGHTEST accepted star
                    // (median across frames), NOT a property of "the stars that were accepted" — at a 0.125 gate
                    // the accepted set runs from 0.125 upward, so claiming they all measure 22 is simply false, and
                    // it reads as refuting the "low-confidence detections" clause before it. The tail clause
                    // reconciles the two facts instead of leaving them looking contradictory.
                    text += $" Exposure is not what is limiting this run: your brightest stars measure S/N {advice.MeasuredSnr:0.#}, which already meets the default gate of {ExposureRecommender.TargetSensitivity:0.#}; the low gate is admitting a long tail of far fainter candidates below them.";
                    if (advice.ShortFrameCount > 0) {
                        text += $" {advice.ShortFrameCount} of {advice.UsableFrameCount} frames found fewer stars than the star-count target, so the low gate is scraping for count in a star-poor field.";
                    }
                } else if (!advice.IncreasesExposure) {
                    text += " Reaching the default gate would take longer per frame than an auto-focus sweep can spend, so there is no longer exposure to offer.";
                } else if (advice.CapLimitsRecommendation) {
                    // "The recommendation above", not "That": the opening sentence quotes no number, so a bare
                    // pronoun would reach past it to the ROW — a different control. The sibling binning block names
                    // its referent explicitly in the same situation.
                    text += advice.CappedByAbsoluteLimit
                        ? $" Reaching the default gate would take longer per frame than an auto-focus sweep can spend, so the recommendation above stops at {advice.RecommendedSeconds:0.##} s."
                        : $" The recommendation above is a partial step: one run should not raise the exposure by more than {ExposureRecommender.MaxExposureFactor:0}x, so expect to repeat this.";
                }
            }

            var remedy = RemedyFor(summary, advice, lastRunWasLive);
            if (!string.IsNullOrEmpty(remedy)) {
                // Replay's reassurance rides with the remedy: a page carrying a warning has to say that Accept is
                // still a legitimate choice, and Replay is the mode with no action of its own to take.
                if (!lastRunWasLive) {
                    text += " You can still accept these settings; they are the best fit for frames like these.";
                }
                text += " " + remedy;
            } else if (!lastRunWasLive) {
                text += " You can still accept these settings; they are the best fit for frames like these.";
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
        /// instruction in both modes — only the way to carry it out differs, so the two share this slot rather than
        /// competing for it. LIVE names the capture action that sits directly below the paragraph: as with the
        /// binning block, the body's only job is to say why that button exists, since the row already carries the
        /// number and the tooltip the derivation. REPLAY has no capture action and no write-back for the exposure
        /// (<see cref="StarDetectionOptimizerWizardVM.CanApplyExposureTime"/> is false), so if its sentence does not say where to set the value by
        /// hand, nothing does. Both carry the narrowband suggestion as a RIDER when the absolute cap trimmed the
        /// number, so that hint survives without becoming a competing instruction.
        ///
        /// <para>Gated on <see cref="ExposureRecommendation.IncreasesExposure"/>, which is exactly the condition
        /// <see cref="StarDetectionOptimizerWizardVM.ShowCaptureNewSweep"/> uses — so the Live sentence and the control it describes appear and
        /// disappear together (the house rule at <see cref="StarDetectionOptimizerWizardVM.ShowOptimizeAgainAtRecommendedBinning"/>). When the
        /// capture is merely UNAVAILABLE (no engine, a disconnected camera, no save folder, use-current mode) the
        /// button renders disabled rather than vanishing, so the sentence still has a referent.</para></item>
        /// </list>
        /// </summary>
        private static string RemedyFor(OptimizationSummary summary, ExposureRecommendation advice, bool lastRunWasLive) {
            var increases = advice != null && advice.IncreasesExposure;
            if (summary.DetectionBinningDiffers && increases) {
                return "The detection binning change recommended below also raises measured star signal, so the two are not additive: change the factor first and let the next run re-measure the exposure.";
            }
            if (advice != null && advice.HasRecommendation && !advice.ExposureIsNotTheLimit && !advice.IncreasesExposure) {
                return "If you are shooting narrowband, consider auto-focusing through a broadband filter with a filter offset instead.";
            }
            if (increases) {
                // Live acts on the rig it is attached to; Replay can only tell the user where the setting lives. The
                // Live sentence quotes no seconds: the editable box beside the button is the authority on the value,
                // and the row above already printed the recommendation (see the button's label comment in the XAML).
                var text = lastRunWasLive
                    ? "Capture a new sweep at the longer exposure to re-tune these settings on frames that have real signal."
                    : $"For a more reliable tune, raise your auto-focus exposure to about {advice.RecommendedSeconds:0.##} s in NINA's focuser options and run this wizard again in Live mode.";
                if (advice.CappedByAbsoluteLimit) {
                    // The cap trimmed the number, so this is a partial fix in either mode. The filter route rides
                    // along as an alternative rather than replacing it — same sentence, so "one instruction" holds.
                    text = text.TrimEnd('.')
                        + "; if you are shooting narrowband, consider auto-focusing through a broadband filter with a filter offset instead.";
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
                return $"The measured S/N of {advice.MeasuredSnr:0.#} already meets the target of {ExposureRecommender.TargetSensitivity:0.#}, so no longer exposure is derived.";
            }
            // Sky-limited scaling: sigma goes as sqrt(t), so an SNR ratio r needs r-squared times the exposure.
            var text = $"Sky-limited scaling: {advice.CurrentSeconds:0.##} s × ({ExposureRecommender.TargetSensitivity:0.#} / {advice.MeasuredSnr:0.#})² = {FormatExposureSeconds(advice.RawSeconds)} s per frame{DescribeSweepCost(advice.RawSeconds, summary.CurrentOffsetSteps)}.";
            if (!advice.IncreasesExposure) {
                // "will suggest", not "a sweep can sustain": this branch is reached by a user whose CURRENT
                // exposure is already longer than the cap, so a flat claim about what a sweep can sustain is
                // contradicted by their own working setup.
                text += $" That is past the {ExposureRecommender.MaxRecommendedExposureSeconds:0} s this recommendation will suggest, and past what you already use, so nothing longer is offered.";
            } else if (advice.CapLimitsRecommendation) {
                text += advice.CappedByAbsoluteLimit
                    ? $" Capped at {advice.RecommendedSeconds:0.##} s: {ExposureRecommender.MaxRecommendedExposureSeconds:0} s per frame is the ceiling a sweep can sustain."
                    : $" Capped at {advice.RecommendedSeconds:0.##} s: one run may not raise the exposure by more than {ExposureRecommender.MaxExposureFactor:0}x, so a second run refines it.";
            }
            if (advice.ShortFrameCount > 0) {
                text += $" {advice.ShortFrameCount} of {advice.UsableFrameCount} frames had fewer stars than the star-count target, so this under-states what is needed.";
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
