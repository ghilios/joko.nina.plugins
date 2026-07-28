#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// Unit tests for the pure, de-jargoned summary formatting + improvement math on <see cref="OptimizationSummary"/>
/// (starry-hopper items 5/8): the before→after / "(unchanged)" recommendation text, the σ "tighter" readout, and
/// the higher-is-better improvement percentage.
/// </summary>
[TestFixture]
public class OptimizationSummaryTests {

    [Test]
    public void FormatRecommendation_Changed_UsesArrow() {
        Assert.That(OptimizationSummary.FormatRecommendation(150, 105), Is.EqualTo("150 → 105"));
    }

    [Test]
    public void FormatRecommendation_Unchanged_SaysSo() {
        Assert.That(OptimizationSummary.FormatRecommendation(105, 105), Is.EqualTo("105 (unchanged)"));
    }

    [Test]
    public void StepSizeText_AndOffsetStepsText_FollowRecommendationFormat() {
        var changed = new OptimizationSummary { CurrentStepSize = 150, RecommendedStepSize = 105, CurrentOffsetSteps = 4, RecommendedOffsetSteps = 4 };
        Assert.Multiple(() => {
            Assert.That(changed.StepSizeText, Is.EqualTo("150 → 105"));
            Assert.That(changed.OffsetStepsText, Is.EqualTo("4 (unchanged)"));
        });
    }

    [Test]
    public void StepSizeOrOffsetChanged_TrueWhenEitherDiffers() {
        Assert.Multiple(() => {
            Assert.That(new OptimizationSummary { CurrentStepSize = 150, RecommendedStepSize = 105, CurrentOffsetSteps = 4, RecommendedOffsetSteps = 4 }.StepSizeOrOffsetChanged, Is.True);
            Assert.That(new OptimizationSummary { CurrentStepSize = 100, RecommendedStepSize = 100, CurrentOffsetSteps = 4, RecommendedOffsetSteps = 5 }.StepSizeOrOffsetChanged, Is.True);
            Assert.That(new OptimizationSummary { CurrentStepSize = 100, RecommendedStepSize = 100, CurrentOffsetSteps = 4, RecommendedOffsetSteps = 4 }.StepSizeOrOffsetChanged, Is.False);
        });
    }

    [Test]
    public void SigmaText_ShowsTighterPercent_WhenImproved() {
        var s = new OptimizationSummary { SeedSigmaFocus = 2.0, BestSigmaFocus = 1.0 };
        Assert.That(s.SigmaText, Does.Contain("2.00 → 1.00").And.Contain("50% tighter"));
    }

    [Test]
    public void SigmaText_Unchanged_SaysUnchanged() {
        // σ effectively the same before/after (at the F2 precision shown) reads "2.20 (unchanged)", not "2.20 → 2.20".
        Assert.Multiple(() => {
            Assert.That(new OptimizationSummary { SeedSigmaFocus = 2.20, BestSigmaFocus = 2.20 }.SigmaText, Is.EqualTo("2.20 (unchanged)"));
            Assert.That(new OptimizationSummary { SeedSigmaFocus = 2.201, BestSigmaFocus = 2.203 }.SigmaText, Is.EqualTo("2.20 (unchanged)"));
        });
    }

    // ---- Live progress readout ---------------------------------------------------------------------------
    // It must never contradict the summary. The summary headlines σ (FocusPrecisionText → SigmaText), so the
    // live line reports σ too — never a percent of the objective J, whose relative deltas are ~1e-5 on a
    // saturated run (e.g. 0.999939 → 0.999919) and therefore always round to "0%".

    [Test]
    public void LiveImprovementText_StillSearching_UntilBestBeatsTheBaselineJ() {
        // gateBaselineJ is the stricter of the pass's own baseline and the user's current settings (see
        // StarDetectionOptimizerWizardVM.ProgressGateJ), so the live line can never promise an improvement the
        // results page — which judges against the current settings — then retracts.
        Assert.Multiple(() => {
            Assert.That(OptimizationSummary.LiveImprovementText(gateBaselineJ: 0.9, bestJ: 0.0, baselineSigma: 2.20, bestSigma: double.NaN),
                Is.EqualTo("Searching for a better fit…"), "no incumbent yet");
            Assert.That(OptimizationSummary.LiveImprovementText(gateBaselineJ: 0.9, bestJ: 0.9, baselineSigma: 2.20, bestSigma: 1.50),
                Is.EqualTo("Searching for a better fit…"), "a tie is not an improvement");
            Assert.That(OptimizationSummary.LiveImprovementText(gateBaselineJ: 0.99994, bestJ: 0.99992, baselineSigma: 2.20, bestSigma: 1.50),
                Is.EqualTo("Searching for a better fit…"),
                "the saturated-plateau case: a tighter σ must NOT be advertised while J is below the current settings");
            // The Continue pass: the incumbent beats the prior round (0.999919) but not the current settings
            // (0.999939). ProgressGateJ hands us the stricter of the two, so nothing is claimed — matching the
            // results page, which keeps Current and shows "could not improve on your current settings".
            Assert.That(OptimizationSummary.LiveImprovementText(gateBaselineJ: 0.999939, bestJ: 0.999925, baselineSigma: 2.20, bestSigma: 2.05),
                Is.EqualTo("Searching for a better fit…"));
        });
    }

    [Test]
    public void LiveImprovementText_ShowsSigmaBeforeAfter_WhenTightened() {
        // Same F2 σ pair the summary's SigmaText shows, so the two agree digit for digit.
        Assert.That(OptimizationSummary.LiveImprovementText(gateBaselineJ: 0.90, bestJ: 0.91, baselineSigma: 2.20, bestSigma: 1.85),
            Is.EqualTo("Best so far: σ 2.20 → 1.85"));
    }

    [Test]
    public void LiveImprovementText_FoundBetterSettings_WhenJImprovesWithoutTighteningSigma() {
        // J can improve on star count alone (the aberration-inspection objective trades σ for stars), and σ may
        // be missing entirely on a degenerate fit. Neither may render as a σ pair — that would read as a σ win.
        Assert.Multiple(() => {
            Assert.That(OptimizationSummary.LiveImprovementText(gateBaselineJ: 0.90, bestJ: 0.91, baselineSigma: 2.20, bestSigma: 2.40),
                Is.EqualTo("Found better settings so far"), "σ got worse");
            Assert.That(OptimizationSummary.LiveImprovementText(gateBaselineJ: 0.90, bestJ: 0.91, baselineSigma: 2.201, bestSigma: 2.199),
                Is.EqualTo("Found better settings so far"), "σ inside SigmaText's 'unchanged' band");
            Assert.That(OptimizationSummary.LiveImprovementText(gateBaselineJ: 0.90, bestJ: 0.91, baselineSigma: double.NaN, bestSigma: 1.50),
                Is.EqualTo("Found better settings so far"), "no baseline σ");
            Assert.That(OptimizationSummary.LiveImprovementText(gateBaselineJ: 0.90, bestJ: 0.91, baselineSigma: 2.20, bestSigma: double.NaN),
                Is.EqualTo("Found better settings so far"), "no incumbent σ");
        });
    }

    [Test]
    public void SigmaText_NonFinite_FallsBackToBaseText_NoTighterOrUnchanged() {
        var s = new OptimizationSummary { SeedSigmaFocus = double.NaN, BestSigmaFocus = 1.0 }.SigmaText;
        Assert.Multiple(() => {
            Assert.That(s, Does.Not.Contain("tighter"));
            Assert.That(s, Does.Not.Contain("unchanged"));
        });
    }

    [Test]
    public void FormatSigmaTrajectory_MultiRound_JoinsPathAndShowsTighter() {
        // baseline → R1 → R2 σ path with a "(~N% tighter)" suffix from first vs last ((2.20-1.52)/2.20 ≈ 31%).
        var s = OptimizationSummary.FormatSigmaTrajectory(new[] { 2.20, 1.85, 1.52 });
        Assert.Multiple(() => {
            Assert.That(s, Does.Contain("2.20 → 1.85 → 1.52"));
            Assert.That(s, Does.Contain("31% tighter"));
            Assert.That(s.Split('→').Length, Is.EqualTo(3), "two arrows across three stages");
        });
    }

    [Test]
    public void FormatSigmaTrajectory_NonImproving_NoTighterSuffix() {
        // σ that holds or worsens (last >= first) renders the path only — no false "tighter" claim.
        Assert.Multiple(() => {
            Assert.That(OptimizationSummary.FormatSigmaTrajectory(new[] { 1.50, 1.80, 2.00 }), Is.EqualTo("1.50 → 1.80 → 2.00"));
            Assert.That(OptimizationSummary.FormatSigmaTrajectory(new[] { 1.50, 1.50 }), Is.EqualTo("1.50 → 1.50"));
        });
    }

    [Test]
    public void FormatSigmaTrajectory_DropsNonFinite_AndHandlesEmpty() {
        Assert.Multiple(() => {
            // Non-finite stages are dropped; the suffix compares the surviving first/last (2.00 → 1.50 ≈ 25%).
            Assert.That(OptimizationSummary.FormatSigmaTrajectory(new[] { double.NaN, 2.00, double.NaN, 1.50 }),
                Does.Contain("2.00 → 1.50").And.Contain("25% tighter"));
            Assert.That(OptimizationSummary.FormatSigmaTrajectory(new[] { double.NaN, double.NaN }), Is.Empty);
            Assert.That(OptimizationSummary.FormatSigmaTrajectory(new double[0]), Is.Empty);
            Assert.That(OptimizationSummary.FormatSigmaTrajectory(null), Is.Empty);
        });
    }

    [Test]
    public void PercentBetter_HigherIsBetter_ClampedAndGuarded() {
        Assert.Multiple(() => {
            Assert.That(OptimizationSummary.PercentBetter(1.0, 1.5), Is.EqualTo(50.0).Within(1e-9), "higher best => positive %");
            Assert.That(OptimizationSummary.PercentBetter(2.0, 1.0), Is.EqualTo(0.0), "a lower 'best' clamps to 0 (no improvement)");
            Assert.That(OptimizationSummary.PercentBetter(1.0, 1.0), Is.EqualTo(0.0));
            Assert.That(OptimizationSummary.PercentBetter(0.0, 5.0), Is.EqualTo(0.0), "near-zero baseline guarded");
            Assert.That(OptimizationSummary.PercentBetter(double.NaN, 1.0), Is.EqualTo(0.0));
            Assert.That(OptimizationSummary.PercentBetter(1.0, double.PositiveInfinity), Is.EqualTo(0.0));
        });
    }

    [Test]
    public void ImprovementPercent_UsesObjectiveScore() {
        var s = new OptimizationSummary { SeedJ = 1.0, BestJ = 1.2 };
        Assert.That(s.ImprovementPercent, Is.EqualTo(20.0).Within(1e-9));
    }

    // ---- Detection binning states -------------------------------------------------------------------------
    //
    // F = the factor this run analyzed at, P = the persisted setting, R = what the measured HFR calls for.

    private static OptimizationSummary Binning(int run, int persisted, int recommended, double hfr, double rSquared = 0.99) =>
        new OptimizationSummary {
            RunDetectionBinning = run, PersistedDetectionBinning = persisted,
            RecommendedDetectionBinning = recommended, MeasuredInFocusHfr = hfr, FitRSquared = rSquared
        };

    [Test]
    public void DetectionBinning_NoMeasuredHfr_HidesTheWholeBlock() {
        // A degenerate baseline fit has nothing to say about binning, and the bad curve is already on the chart.
        var s = Binning(1, 1, 1, double.NaN);
        Assert.Multiple(() => {
            Assert.That(s.HasDetectionBinningMeasurement, Is.False);
            Assert.That(s.DetectionBinningDiffers, Is.False);
            Assert.That(s.DetectionBinningPendingApply, Is.False);
        });
    }

    [Test]
    public void DetectionBinning_FollowsTheVariantsOwnCurve_NotTheBaseline() {
        // Regression: a run whose OPTIMIZED curve bottomed at 5.1 px reported "measured in-focus HFR 4.3 px, 1x1
        // unchanged", because the number came from the current-settings curve while the chart showed the optimized
        // one. The current-settings curve is exactly the one not to trust here — that run's focus precision went
        // 45.88 -> 6.64 by switching off it.
        var optimized = new OptimizationSummary {
            RunDetectionBinning = 1, PersistedDetectionBinning = 1,
            MeasuredInFocusHfr = 5.1, FitRSquared = 0.99,
            BaselineMeasuredInFocusHfr = 4.3, BaselineFitRSquared = 0.98,
            RecommendedDetectionBinning = DetectionBinningResolver.RecommendFromHfr(5.1)
        };
        var current = StarDetectionOptimizerWizardVM.BuildCurrentSummary(optimized);

        Assert.Multiple(() => {
            Assert.That(optimized.MeasuredInFocusHfr, Is.EqualTo(5.1), "the Optimized view reports the optimized curve");
            Assert.That(optimized.RecommendedDetectionBinning, Is.EqualTo(2), "5.1 px calls for 2x2");
            Assert.That(optimized.DetectionBinningDiffers, Is.True);

            // The Current view keeps the current settings, so it reports what THOSE measure.
            Assert.That(current.MeasuredInFocusHfr, Is.EqualTo(4.3));
            Assert.That(current.RecommendedDetectionBinning, Is.EqualTo(1), "4.3 px is still in range");
            Assert.That(current.DetectionBinningDiffers, Is.False);
        });
    }

    [Test]
    public void DetectionBinning_PoorFit_SuppressesTheRecommendationEntirely() {
        // The in-focus HFR is read off the fitted focus curve. When a sweep runs past the point where the
        // detector can still measure the defocused donuts, those frames report a couple of compact noise blobs
        // instead, the fit is dragged wildly off, and R2 goes negative. Measured on the simulator at 2800mm: a
        // +/-320-step sweep put the fitted minimum at 2.16 px against an optics truth of 4.87 px, with
        // R2 = -0.34. No estimator recovers from that - the underlying curve is junk - so the only honest answer
        // is to not offer a recommendation at all.
        var junk = Binning(run: 1, persisted: 1, recommended: 1, hfr: 2.16, rSquared: -0.34);
        Assert.Multiple(() => {
            Assert.That(junk.HasDetectionBinningMeasurement, Is.False, "a curve this bad cannot support a recommendation");
            Assert.That(junk.DetectionBinningDiffers, Is.False);
            Assert.That(junk.DetectionBinningPendingApply, Is.False);
        });
    }

    [Test]
    public void DetectionBinning_MissingFitQuality_SuppressesTheRecommendation() {
        // No fit at all (too few points, degenerate curve) leaves R2 NaN. Same rule: say nothing.
        var noFit = Binning(run: 1, persisted: 1, recommended: 2, hfr: 6.1, rSquared: double.NaN);
        Assert.That(noFit.HasDetectionBinningMeasurement, Is.False);
    }

    [Test]
    public void DetectionBinning_GoodFit_IsAccepted() {
        // The sound regime the same experiment measured: R2 >= 0.998 at every sweep width where the detector
        // still tracked the donuts, and the fitted minimum agreed with the optics truth to within 5%.
        var sound = Binning(run: 1, persisted: 1, recommended: 2, hfr: 4.71, rSquared: 0.9984);
        Assert.Multiple(() => {
            Assert.That(sound.HasDetectionBinningMeasurement, Is.True);
            Assert.That(sound.DetectionBinningDiffers, Is.True);
        });
    }

    [Test]
    public void DetectionBinning_RecommendationMatchesTheRun_ReadsAsConfirmation() {
        var s = Binning(run: 2, persisted: 2, recommended: 2, hfr: 5.3);
        Assert.Multiple(() => {
            Assert.That(s.DetectionBinningDiffers, Is.False);
            Assert.That(s.DetectionBinningPendingApply, Is.False);
            Assert.That(s.DetectionBinningText, Is.EqualTo("2x2 (unchanged; measured in-focus HFR 5.3 px)"));
        });
    }

    [Test]
    public void DetectionBinning_RecommendationDiffers_OffersTheChange() {
        var s = Binning(run: 1, persisted: 1, recommended: 2, hfr: 6.1);
        Assert.Multiple(() => {
            Assert.That(s.DetectionBinningDiffers, Is.True);
            Assert.That(s.DetectionBinningPendingApply, Is.False, "nothing has been re-run yet, so Accept writes no factor");
            Assert.That(s.DetectionBinningText, Is.EqualTo("1x1 -> 2x2 (measured in-focus HFR 6.1 px)"));
        });
    }

    [Test]
    public void DetectionBinning_AfterOptimizeAgain_IsPendingAndNamesBothFactors() {
        // The run analyzed at 2x2 while the profile still says 1x1: Accept writes the factor WITH the settings.
        var s = Binning(run: 2, persisted: 1, recommended: 2, hfr: 6.2);
        Assert.Multiple(() => {
            Assert.That(s.DetectionBinningPendingApply, Is.True);
            Assert.That(s.DetectionBinningDiffers, Is.False, "the run already used the recommended factor");
            Assert.That(s.DetectionBinningText, Is.EqualTo("1x1 -> 2x2 (applied on Accept; measured in-focus HFR 6.2 px)"));
        });
    }

    [Test]
    public void DetectionBinning_PendingApplyWinsOverAFurtherRecommendation() {
        // Seeing shifted and the re-run now implies 3x3. The pending 2x2 is still what Accept would write, so the
        // row must describe that rather than silently advertising a factor nothing was measured at.
        var s = Binning(run: 2, persisted: 1, recommended: 3, hfr: 8.4);
        Assert.Multiple(() => {
            Assert.That(s.DetectionBinningPendingApply, Is.True);
            Assert.That(s.DetectionBinningDiffers, Is.True);
            Assert.That(s.DetectionBinningText, Is.EqualTo("1x1 -> 2x2 (applied on Accept; measured in-focus HFR 8.4 px)"));
        });
    }

    // ---- Star signal (exposure recommendation) ---------------------------------------------------------------
    //
    // The block fires on the Sensitivity gate ALONE; the recommendation's sub-states decide what it then says.

    private static ExposureRecommendation Advice(
        double current, double recommended, double snr,
        double raw = double.NaN, bool wasCapped = false, bool cappedByAbsoluteLimit = false,
        bool exposureIsNotTheLimit = false, int shortFrameCount = 0) =>
        new ExposureRecommendation {
            HasRecommendation = true,
            CurrentSeconds = current,
            RecommendedSeconds = recommended,
            MeasuredSnr = snr,
            RawSeconds = double.IsNaN(raw) ? recommended : raw,
            WasCapped = wasCapped,
            CappedByAbsoluteLimit = cappedByAbsoluteLimit,
            ExposureIsNotTheLimit = exposureIsNotTheLimit,
            ShortFrameCount = shortFrameCount,
            UsableFrameCount = 9
        };

    private static OptimizationSummary Starved(
        double sensitivity = 0.125, ExposureRecommendation advice = null, int offsetSteps = 4,
        int runBinning = 1, int recommendedBinning = 1,
        double runExposureSeconds = double.NaN, bool runExposureIsAssumed = false) =>
        new OptimizationSummary {
            VariantSensitivity = sensitivity,
            ExposureAdvice = advice,
            CurrentOffsetSteps = offsetSteps,
            RunExposureSeconds = runExposureSeconds,
            RunExposureIsAssumed = runExposureIsAssumed,
            // Only matters for the "both recommendations at once" caveat; a finite HFR + good R2 is what makes the
            // detection-binning verdict meaningful at all.
            MeasuredInFocusHfr = 6.1,
            FitRSquared = 0.99,
            RunDetectionBinning = runBinning,
            RecommendedDetectionBinning = recommendedBinning,
            PersistedDetectionBinning = runBinning
        };

    /// <summary>The unconditional first sentence, in its two shapes — spelled out here so the golden whole-string
    /// assertions below stay readable while still being whole-string.</summary>
    private const string LowSignalOpening =
        "Stars in these frames barely cleared the noise, and Brightness Sensitivity sits at 0.125, at the bottom of its range, so this focus result rests on low-confidence detections.";

    private const string NeutralOpening =
        "Brightness Sensitivity sits at 0.125, at the bottom of its range, so this focus result rests on low-confidence detections.";

    private const string ReplayReassurance =
        " You can still accept these settings; they are the best fit for frames like these.";

    /// <summary>Live's one instruction: it names the capture action sitting directly below the paragraph, and
    /// quotes no seconds (the row above and the editable box below both carry the number).</summary>
    private const string LiveCaptureInstruction =
        " Capture a new sweep at the longer exposure to re-tune these settings on frames that have real signal.";

    [TestCase(0.0, true)]
    [TestCase(0.125, true)]
    [TestCase(0.5, true)]
    [TestCase(1.0, true)]
    [TestCase(1.0001, false)]
    [TestCase(2.0, false)]
    [TestCase(10.0, false)]
    [TestCase(double.NaN, false)]
    public void Exposure_BlockVisibility_FollowsTheGateAlone(double sensitivity, bool expected) {
        // A search that lands in the floor band admitted anything above the noise to find stars at all. NaN (a
        // summary built before this feature, or by a test that does not set it) must read as "no", not "unknown".
        Assert.That(Starved(sensitivity).HasLowStarSignal, Is.EqualTo(expected));
    }

    [Test]
    public void Exposure_BlockShows_EvenWithNothingToRecommend() {
        // Deliberate: the block is NOT additionally gated on there being a derived number. A floored gate means the
        // focus result rests on low-confidence detections, which is worth saying even when nothing can be done.
        var s = Starved(sensitivity: 0.1, advice: null);
        Assert.Multiple(() => {
            Assert.That(s.HasLowStarSignal, Is.True);
            Assert.That(s.ExposureText, Is.Empty, "no advice => no row, but the block still shows its diagnosis");
        });
    }

    [Test]
    public void Exposure_FollowsTheVariantsOwnGate_NotTheBaseline() {
        // Same rule as MeasuredInFocusHfr: the variant is what Accept applies, so the gate reported must be the
        // one on screen. An optimizer that floored the gate must not make the user's healthy current settings look
        // starved, and a user who hand-set their OWN gate to 0 must be told so on the Current view.
        var optimizedFloored = new OptimizationSummary {
            VariantSensitivity = 0.1, BaselineSensitivity = 10.0,
            ExposureAdvice = Advice(3, 12, 4.1), BaselineExposureAdvice = Advice(3, 3, 22.0, exposureIsNotTheLimit: true)
        };
        var currentOfFloored = StarDetectionOptimizerWizardVM.BuildCurrentSummary(optimizedFloored);

        var optimizedHealthy = new OptimizationSummary {
            VariantSensitivity = 12.0, BaselineSensitivity = 0.2,
            ExposureAdvice = Advice(3, 3, 30.0, exposureIsNotTheLimit: true), BaselineExposureAdvice = Advice(3, 12, 4.1)
        };
        var currentOfHealthy = StarDetectionOptimizerWizardVM.BuildCurrentSummary(optimizedHealthy);

        Assert.Multiple(() => {
            Assert.That(optimizedFloored.HasLowStarSignal, Is.True, "the optimized gate is floored");
            Assert.That(currentOfFloored.HasLowStarSignal, Is.False, "the user's own gate is healthy");
            Assert.That(optimizedHealthy.HasLowStarSignal, Is.False);
            Assert.That(currentOfHealthy.HasLowStarSignal, Is.True, "the user hand-set their gate to 0.2");
        });
    }

    [Test]
    public void Exposure_BuildCurrentSummary_CarriesTheBaselineAdvice() {
        var optimized = new OptimizationSummary {
            RunExposureSeconds = 3.0, RunExposureIsAssumed = true,
            VariantSensitivity = 0.1, BaselineSensitivity = 0.2,
            ExposureAdvice = Advice(3, 12, 4.1), BaselineExposureAdvice = Advice(3, 9, 5.4)
        };
        var current = StarDetectionOptimizerWizardVM.BuildCurrentSummary(optimized);

        Assert.Multiple(() => {
            Assert.That(current.VariantSensitivity, Is.EqualTo(0.2), "the Current view reports the current gate");
            Assert.That(current.BaselineSensitivity, Is.EqualTo(0.2));
            Assert.That(current.ExposureAdvice, Is.SameAs(optimized.BaselineExposureAdvice),
                "the Current view's advice is the one derived from the CURRENT settings' accepted stars");
            Assert.That(current.BaselineExposureAdvice, Is.SameAs(optimized.BaselineExposureAdvice));
            Assert.That(current.RunExposureSeconds, Is.EqualTo(3.0), "both variants ran on the same frames");
            Assert.That(current.RunExposureIsAssumed, Is.True, "and on the same assumption about how they were captured");
        });
    }

    [Test]
    public void ExposureText_Increase_ShowsBeforeAndAfterWithTheMeasurement() {
        var s = Starved(advice: Advice(3, 12, 4.1));
        Assert.That(s.ExposureText, Is.EqualTo("3 s → 12 s (measured star S/N 4.1; target 10)"));
    }

    [Test]
    public void ExposureText_AlreadyPastTheCap_ReadsAsUnchangedRatherThanANoOpArrow() {
        // The recommender never returns a SHORTER exposure, so a 40 s narrowband run (past the 30 s absolute cap)
        // gets RecommendedSeconds == CurrentSeconds while HasRecommendation stays true. Branching on
        // HasRecommendation here would print "40 s → 40 s".
        var s = Starved(advice: Advice(40, 40, 6.0, raw: 111.1, wasCapped: true, cappedByAbsoluteLimit: true));
        Assert.Multiple(() => {
            Assert.That(s.ExposureText, Is.EqualTo("40 s (unchanged; measured star S/N 6; target 10)"));
            Assert.That(s.ExposureText, Does.Not.Contain("→"));
        });
    }

    [Test]
    public void ExposureText_NoRecommendation_IsEmpty() {
        var s = Starved(advice: new ExposureRecommendation {
            HasRecommendation = false, CurrentSeconds = 3,
            RecommendedSeconds = double.NaN, RawSeconds = double.NaN, MeasuredSnr = double.NaN
        });
        Assert.That(s.ExposureText, Is.Empty, "an empty row is hidden rather than rendering a bare label");
    }

    // ---- Star signal body copy -------------------------------------------------------------------------------
    //
    // The body is DIAGNOSIS + AT MOST ONE INSTRUCTION. The states that produce it (Replay, a binding cap, a
    // competing binning recommendation) are independently reachable, so the tests that matter most are the
    // WHOLE-STRING ones on the stacked combinations: clause-level Does.Contain assertions cannot see a paragraph
    // that has accumulated into self-contradiction, which is exactly how the first version shipped six sentences
    // offering three mutually exclusive remedies.

    private static string Body(OptimizationSummary s, bool live) =>
        StarDetectionOptimizerWizardVM.DescribeExposureRecommendation(s, live);

    private static string Detail(OptimizationSummary s) =>
        StarDetectionOptimizerWizardVM.DescribeExposureDerivation(s);

    [Test]
    public void ExposureCopy_HealthyGate_SaysNothing() {
        Assert.Multiple(() => {
            Assert.That(Body(Starved(sensitivity: 10.0, advice: Advice(3, 12, 4.1)), live: true), Is.Empty);
            Assert.That(Detail(Starved(sensitivity: 10.0, advice: Advice(3, 12, 4.1))), Is.Empty);
        });
    }

    [Test]
    public void ExposureCopy_Live_PlainIncrease_NamesTheCaptureActionAndQuotesNoNumber() {
        // GOLDEN. The row already reads "3 s → 12 s (measured star S/N 4.1; target 10)" and the editable box beside
        // the button repeats it, so a body sentence quoting the seconds a THIRD time would go stale the moment the
        // user edits that box. The sibling detection-binning block sets the rule: the body's only job is to say why
        // the button exists.
        var text = Body(Starved(advice: Advice(3, 12, 4.1)), live: true);
        Assert.That(text, Is.EqualTo(LowSignalOpening + LiveCaptureInstruction));
        Assert.Multiple(() => {
            Assert.That(text, Does.Not.Contain("12 s"), "the number lives in the row and the box, not the paragraph");
            // The block follows the SELECTED variant, and on the Current view the gate is the user's own hand-set
            // value, so the copy must never attribute it to the optimizer.
            Assert.That(text, Does.Not.Contain("optimizer"));
            Assert.That(text, Does.Not.Contain("NINA's focuser options"), "that is Replay's instruction");
        });
    }

    [Test]
    public void ExposureCopy_ExposureIsNotTheLimit_DoesNotClaimTheStarsBarelyClearedTheNoise() {
        // GOLDEN, and the regression this state exists to prevent: ExposureIsNotTheLimit means S_now >= the target,
        // so an unconditional "stars barely cleared the noise" opening is FALSE here and is contradicted by the very
        // next sentence. MeasuredSnr is the NTarget-th BRIGHTEST accepted star, so this state actually describes a
        // rich field — hence no "star-poor" claim either (see the ShortFrameCount case below). It is also NOT a
        // property of "the stars that were accepted": at a 0.125 gate the accepted set runs from 0.125 upward, so
        // the sentence has to name the brightest stars and then reconcile that with the low-confidence clause,
        // rather than leaving the two looking like a contradiction.
        var text = Body(Starved(advice: Advice(5, 5, 22.0, exposureIsNotTheLimit: true)), live: true);
        Assert.That(text, Is.EqualTo(
            NeutralOpening
            + " Exposure is not what is limiting this run: your brightest stars measure S/N 22, which already meets the default gate of 10;"
            + " the low gate is admitting a long tail of far fainter candidates below them."));
        Assert.Multiple(() => {
            Assert.That(text, Does.Not.Contain("barely cleared"));
            Assert.That(text, Does.Not.Contain("star-poor"), "nothing measured says the field is thin");
            Assert.That(text, Does.Not.Contain("per frame"), "no exposure figure: exposure is not the problem");
        });
    }

    [Test]
    public void ExposureCopy_ExposureIsNotTheLimit_CallsTheFieldStarPoorOnlyOnShortFrames() {
        // ShortFrameCount is the ONLY measured evidence that a field was too thin: those frames had fewer than the
        // star-count target to offer, so the per-frame statistic fell back to their faintest survivor.
        var text = Body(Starved(advice: Advice(5, 5, 22.0, exposureIsNotTheLimit: true, shortFrameCount: 4)), live: true);
        Assert.That(text, Does.Contain("4 of 9 frames found fewer stars than the star-count target, so the low gate is scraping for count in a star-poor field."));
    }

    [Test]
    public void ExposureCopy_RelativeCapOnly_SaysPartialStep_AndNeverMentionsFilters() {
        // The reviewer's case: current 2 s, S_now 4.5 => raw 9.88 s, trimmed to 8 s by the 4x RUN-RELATIVE cap.
        // 9.9 s is an ordinary auto-focus exposure nowhere near the 30 s ceiling, so filter-swap advice would be
        // nonsense — and MaxExposureFactor's own doc says the relative cap exists so a second run refines the
        // answer. CapLimitsRecommendation alone cannot tell these apart; CappedByAbsoluteLimit can.
        var advice = Advice(2, 8, 4.5, raw: 9.88, wasCapped: true, cappedByAbsoluteLimit: false);
        Assert.That(advice.CapLimitsRecommendation, Is.True, "fixture guard: a cap really did shape the offer");
        var text = Body(Starved(advice: advice), live: true);
        // The relative cap is still a plain increase, so the capture instruction stands — and "expect to repeat
        // this" is advice about the very action the button takes. NO narrowband rider: that rides only with the
        // ABSOLUTE cap, and 8 s is nowhere near the ceiling.
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + " The recommendation above is a partial step: one run should not raise the exposure by more than 4x, so expect to repeat this."
            + LiveCaptureInstruction));
        Assert.Multiple(() => {
            Assert.That(text, Does.Not.Contain("filter"), "the exposure route is nowhere near exhausted");
            Assert.That(text, Does.Not.Contain("broadband"));
            Assert.That(text, Does.Not.Contain(" That is a partial step"),
                "the opening quotes no number, so a bare pronoun would reach past it to the ROW");
        });
    }

    [Test]
    public void ExposureCopy_AbsoluteCap_ButStillAnIncrease_Replay_SaysWhereToSetIt() {
        // The absolute cap binding does NOT mean the exposure route is exhausted: 10 s at S/N 5 wants 40 s and is
        // trimmed to 30 s — a real 3x improvement, sitting in the row directly above. Replay has no write-back for
        // the exposure, so if the body does not say where to set it, nothing does; and offering the filter route
        // "instead" here would advise against the very offer above it. The narrowband hint therefore rides along
        // in the same sentence rather than replacing the instruction.
        var advice = Advice(10, 30, 5.0, raw: 40.0, wasCapped: true, cappedByAbsoluteLimit: true);
        Assert.Multiple(() => {
            Assert.That(advice.IncreasesExposure, Is.True, "fixture guard: there IS a longer exposure on offer");
            Assert.That(advice.CapLimitsRecommendation, Is.True, "fixture guard");
        });
        var text = Body(Starved(advice: advice, offsetSteps: 5), live: false);
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + " Reaching the default gate would take longer per frame than an auto-focus sweep can spend, so the recommendation above stops at 30 s."
            + ReplayReassurance
            + " For a more reliable tune, raise your auto-focus exposure to about 30 s in NINA's focuser options and run this wizard again in Live mode;"
            + " if you are shooting narrowband, consider auto-focusing through a broadband filter with a filter offset instead."));
    }

    [Test]
    public void ExposureCopy_AbsoluteCap_ButStillAnIncrease_Live_CapturesAndCarriesTheNarrowbandRider() {
        // GOLDEN, and the Live twin of the Replay case above. The absolute cap binding does NOT exhaust the
        // exposure route — 10 s at S/N 5 wants 40 s and is trimmed to 30 s, a real 3x improvement the capture
        // action can actually go and take — so the capture instruction stands and the narrowband hint rides along
        // in the SAME sentence rather than replacing it. One instruction either way.
        var advice = Advice(10, 30, 5.0, raw: 40.0, wasCapped: true, cappedByAbsoluteLimit: true);
        var text = Body(Starved(advice: advice, offsetSteps: 5), live: true);
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + " Reaching the default gate would take longer per frame than an auto-focus sweep can spend, so the recommendation above stops at 30 s."
            + LiveCaptureInstruction.TrimEnd('.')
            + "; if you are shooting narrowband, consider auto-focusing through a broadband filter with a filter offset instead."));
        Assert.That(text, Does.Not.Contain("You can still accept"), "that reassurance is Replay's, not Live's");
    }

    [Test]
    public void ExposureCopy_AlreadyPastTheCap_Replay_OffersTheFilterRouteNotALongerExposure() {
        // GOLDEN. WasCapped is true but CapLimitsRecommendation is FALSE — nothing delivered for the cap to limit,
        // because the current exposure is already past it. There is no longer exposure to offer, so the Replay
        // "raise it to X" instruction must not appear.
        var advice = Advice(40, 40, 6.0, raw: 111.1, wasCapped: true, cappedByAbsoluteLimit: true);
        Assert.Multiple(() => {
            Assert.That(advice.CapLimitsRecommendation, Is.False, "fixture guard");
            Assert.That(advice.IncreasesExposure, Is.False, "fixture guard");
        });
        var text = Body(Starved(advice: advice), live: false);
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + " Reaching the default gate would take longer per frame than an auto-focus sweep can spend, so there is no longer exposure to offer."
            + ReplayReassurance
            + " If you are shooting narrowband, consider auto-focusing through a broadband filter with a filter offset instead."));
        Assert.That(text, Does.Not.Contain("40 s"), "the row carries the numbers; the body must not re-offer them");
    }

    [Test]
    public void ExposureCopy_AlreadyPastTheCap_Live_DoesNotOfferACaptureThatChangesNothing() {
        // The mirror image of the case above, and the reason the capture instruction branches on IncreasesExposure
        // rather than HasRecommendation: at 40 s the recommendation collapses onto the current exposure, so a new
        // sweep would spend minutes of sky time to re-capture at the exposure that just failed. Live therefore
        // takes the SAME filter-route remedy Replay does here.
        var advice = Advice(40, 40, 6.0, raw: 111.1, wasCapped: true, cappedByAbsoluteLimit: true);
        Assert.That(advice.IncreasesExposure, Is.False, "fixture guard");
        var text = Body(Starved(advice: advice), live: true);
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + " Reaching the default gate would take longer per frame than an auto-focus sweep can spend, so there is no longer exposure to offer."
            + " If you are shooting narrowband, consider auto-focusing through a broadband filter with a filter offset instead."));
        Assert.That(text, Does.Not.Contain("Capture a new sweep"), "there is nothing longer to capture at");
    }

    [Test]
    public void ExposureCopy_NoDerivableNumber_GivesTheDiagnosisOnly() {
        // Fewer than MinFramesForRecommendation usable frames, no per-star SNRs, or an unknown exposure to scale
        // from. Never scale a factor off an unknown base — and with nothing measured we cannot claim the stars
        // barely cleared the noise either, so this takes the neutral opening.
        var text = Body(Starved(advice: null), live: true);
        Assert.That(text, Is.EqualTo(NeutralOpening));
    }

    [Test]
    public void ExposureCopy_Replay_PlainIncrease_PointsAtTheProfile() {
        // GOLDEN. Replay's one instruction, with the reassurance that Accept is still legitimate.
        var text = Body(Starved(advice: Advice(3, 12, 4.1)), live: false);
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + ReplayReassurance
            + " For a more reliable tune, raise your auto-focus exposure to about 12 s in NINA's focuser options and run this wizard again in Live mode."));
    }

    [Test]
    public void ExposureCopy_Replay_WithNoNumber_StillKeepsAcceptButNamesNoExposure() {
        var text = Body(Starved(advice: null), live: false);
        Assert.That(text, Is.EqualTo(NeutralOpening + ReplayReassurance));
    }

    [Test]
    public void ExposureCopy_Replay_Capped_AndBinningDiffers_GivesOnlyTheOrderingInstruction() {
        // GOLDEN on the worst stack: Replay + absolute cap + a competing binning recommendation. All three states
        // want to say something, and the earlier version said all three — "use a different filter INSTEAD", then
        // "raise the exposure anyway", then "do not raise it yet, change the binning first". Changing the factor
        // re-measures the per-binned-pixel SNRs every other remedy is derived from, so it outranks both.
        var advice = Advice(10, 30, 1.9, raw: 75.0, wasCapped: true, cappedByAbsoluteLimit: true);
        var s = Starved(advice: advice, offsetSteps: 5, runBinning: 1, recommendedBinning: 2);
        Assert.That(s.DetectionBinningDiffers, Is.True, "fixture guard");
        var text = Body(s, live: false);
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + " Reaching the default gate would take longer per frame than an auto-focus sweep can spend, so the recommendation above stops at 30 s."
            + ReplayReassurance
            + " The detection binning change recommended below also raises measured star signal, so the two are not additive: change the factor first and let the next run re-measure the exposure."));
        Assert.Multiple(() => {
            Assert.That(text, Does.Not.Contain("broadband"), "the ordering instruction is the only instruction");
            Assert.That(text, Does.Not.Contain("NINA's focuser options"));
        });
    }

    [Test]
    public void ExposureCopy_Live_Capped_AndBinningDiffers_AlsoGivesOnlyTheOrderingInstruction() {
        var advice = Advice(10, 30, 1.9, raw: 75.0, wasCapped: true, cappedByAbsoluteLimit: true);
        var s = Starved(advice: advice, offsetSteps: 5, runBinning: 1, recommendedBinning: 2);
        var text = Body(s, live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("not additive"));
            Assert.That(text, Does.Not.Contain("broadband"));
            Assert.That(text, Does.Not.Contain("You can still accept"), "that reassurance is Replay's, not Live's");
        });
    }

    [Test]
    public void ExposureCopy_BinningCaveat_NeedsADerivedIncreaseToBeWorthGiving() {
        // Nothing to over-apply when there is no exposure figure on offer, so the caveat would only add noise —
        // and would displace the instruction that IS useful.
        var notTheLimit = Starved(advice: Advice(5, 5, 22.0, exposureIsNotTheLimit: true), runBinning: 1, recommendedBinning: 2);
        var noAdvice = Starved(advice: null, runBinning: 1, recommendedBinning: 2);
        Assert.That(notTheLimit.DetectionBinningDiffers, Is.True, "fixture guard");
        Assert.Multiple(() => {
            Assert.That(Body(notTheLimit, live: true), Does.Not.Contain("not additive"));
            Assert.That(Body(noAdvice, live: true), Does.Not.Contain("not additive"));
        });
    }

    [Test]
    public void ExposureCopy_AssumedBase_SaysSoBeforeQuotingAnyDerivedNumber() {
        // The derivation rests on an exposure NOTHING recorded, so the paragraph must not read as a measurement of
        // the frames. This is the only reader of RunExposureSeconds.
        var text = Body(Starved(advice: Advice(3, 12, 4.1), runExposureSeconds: 3.0, runExposureIsAssumed: true), live: false);
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + " These frames record no exposure, so this assumes your profile's 3 s auto-focus exposure."
            + ReplayReassurance
            + " For a more reliable tune, raise your auto-focus exposure to about 12 s in NINA's focuser options and run this wizard again in Live mode."));
    }

    // ---- Star signal derivation tooltip ----------------------------------------------------------------------
    //
    // The raw derivation and the sweep cost live HERE rather than in the paragraph — background belongs in a
    // tooltip, which is the rule the sibling detection-binning block already follows.

    [Test]
    public void ExposureDetail_ShowsTheScalingAndWhichCapTrimmedIt() {
        var absolute = Detail(Starved(advice: Advice(10, 30, 1.9, raw: 75.0, wasCapped: true, cappedByAbsoluteLimit: true), offsetSteps: 5));
        var relative = Detail(Starved(advice: Advice(2, 8, 4.5, raw: 9.88, wasCapped: true, cappedByAbsoluteLimit: false)));
        Assert.Multiple(() => {
            Assert.That(absolute, Is.EqualTo(
                "Sky-limited scaling: 10 s × (10 / 1.9)² = 75 s per frame, roughly 14 minutes per auto-focus run."
                + " Capped at 30 s: 30 s per frame is the ceiling a sweep can sustain."));
            Assert.That(relative, Is.EqualTo(
                "Sky-limited scaling: 2 s × (10 / 4.5)² = 9.9 s per frame."
                + " Capped at 8 s: one run may not raise the exposure by more than 4x, so a second run refines it."));
        });
    }

    [Test]
    public void ExposureDetail_SweepCost_IsOmittedRatherThanRenderedAsRoughlyOneMinutes() {
        // "{minutes:0}" rounds, so anything under 1.5 renders as "0" or an ungrammatical "1" — reachable exactly
        // via the relative-cap case above (9.88 s x 9 frames = 1.48 minutes).
        var justUnder = Detail(Starved(advice: Advice(2, 8, 4.5, raw: 9.88, wasCapped: true), offsetSteps: 4));
        var noOffset = Detail(Starved(advice: Advice(3, 12, 4.1, raw: 17.8), offsetSteps: 0));
        var justOver = Detail(Starved(advice: Advice(2, 8, 4.4, raw: 10.4, wasCapped: true), offsetSteps: 4));
        Assert.Multiple(() => {
            Assert.That(justUnder, Does.Not.Contain("minutes"), "1.48 minutes must not render as 'roughly 1 minutes'");
            Assert.That(noOffset, Does.Not.Contain("minutes"), "no known sweep width, no sweep cost");
            Assert.That(justOver, Does.Contain("roughly 2 minutes per auto-focus run"), "10.4 s x 9 frames = 1.56 min");
        });
    }

    [Test]
    public void ExposureDetail_PastTheCap_DoesNotTellAUserTheirOwnExposureIsImpossible() {
        // This branch is reached only when the CURRENT exposure already exceeds the cap, so a flat "past the 30 s a
        // sweep can sustain" would be contradicted by the 40 s setup the user is successfully running.
        var text = Detail(Starved(advice: Advice(40, 40, 6.0, raw: 111.1, wasCapped: true, cappedByAbsoluteLimit: true), offsetSteps: 5));
        Assert.That(text, Is.EqualTo(
            "Sky-limited scaling: 40 s × (10 / 6)² = 111 s per frame, roughly 20 minutes per auto-focus run."
            + " That is past the 30 s this recommendation will suggest, and past what you already use, so nothing longer is offered."));
        Assert.That(text, Does.Not.Contain("a sweep can sustain"));
    }

    [Test]
    public void ExposureDetail_ExposureIsNotTheLimit_DoesNotDeriveAShorterExposure() {
        // The scaling would read "5 s × (10 / 22)² = 1 s", i.e. an invitation to SHORTEN — which the recommender
        // refuses to do and the tooltip must not imply.
        var text = Detail(Starved(advice: Advice(5, 5, 22.0, exposureIsNotTheLimit: true)));
        Assert.That(text, Is.EqualTo("The measured S/N of 22 already meets the target of 10, so no longer exposure is derived."));
    }

    [Test]
    public void ExposureDetail_ShortFrames_SaysTheDerivationUnderStatesTheNeed() {
        var text = Detail(Starved(advice: Advice(3, 12, 4.1, raw: 17.8, shortFrameCount: 2)));
        Assert.That(text, Does.Contain("2 of 9 frames had fewer stars than the star-count target, so this under-states what is needed."));
    }

    [Test]
    public void ExposureDetail_IsEmptyExactlyWhenTheRowIsHidden() {
        // The tooltip is bound, so an empty string would render as an empty popup box. The row's own visibility is
        // ExposureText being non-empty, so the two must agree.
        var hidden = new[] {
            Starved(advice: null),
            Starved(advice: new ExposureRecommendation { HasRecommendation = false, MeasuredSnr = double.NaN }),
            Starved(sensitivity: 10.0, advice: Advice(3, 12, 4.1))   // block itself hidden
        };
        var shown = Starved(advice: Advice(3, 12, 4.1));
        Assert.Multiple(() => {
            foreach (var s in hidden) {
                Assert.That(Detail(s), Is.Empty, "the row is hidden here, so a bound tooltip would be an empty popup");
            }
            Assert.That(Detail(shown), Is.Not.Empty);
            Assert.That(shown.ExposureText, Is.Not.Empty, "…and the row it annotates really is on screen");
        });
    }
}
