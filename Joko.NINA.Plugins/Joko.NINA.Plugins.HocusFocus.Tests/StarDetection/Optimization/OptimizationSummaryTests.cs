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
        double raw = double.NaN, bool wasCapped = false, bool exposureIsNotTheLimit = false) =>
        new ExposureRecommendation {
            HasRecommendation = true,
            CurrentSeconds = current,
            RecommendedSeconds = recommended,
            MeasuredSnr = snr,
            RawSeconds = double.IsNaN(raw) ? recommended : raw,
            WasCapped = wasCapped,
            ExposureIsNotTheLimit = exposureIsNotTheLimit,
            UsableFrameCount = 9
        };

    private static OptimizationSummary Starved(
        double sensitivity = 0.1, ExposureRecommendation advice = null, int offsetSteps = 4,
        int runBinning = 1, int recommendedBinning = 1) =>
        new OptimizationSummary {
            OptimizedSensitivity = sensitivity,
            ExposureAdvice = advice,
            CurrentOffsetSteps = offsetSteps,
            // Only matters for the "both recommendations at once" caveat; a finite HFR + good R2 is what makes the
            // detection-binning verdict meaningful at all.
            MeasuredInFocusHfr = 6.1,
            FitRSquared = 0.99,
            RunDetectionBinning = runBinning,
            RecommendedDetectionBinning = recommendedBinning,
            PersistedDetectionBinning = runBinning
        };

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
        Assert.That(Starved(sensitivity).HasExposureRecommendation, Is.EqualTo(expected));
    }

    [Test]
    public void Exposure_BlockShows_EvenWithNothingToRecommend() {
        // Deliberate: the block is NOT additionally gated on there being a derived number. A floored gate means the
        // focus result rests on low-confidence detections, which is worth saying even when nothing can be done.
        var s = Starved(sensitivity: 0.1, advice: null);
        Assert.Multiple(() => {
            Assert.That(s.HasExposureRecommendation, Is.True);
            Assert.That(s.ExposureText, Is.Empty, "no advice => no row, but the block still shows its diagnosis");
        });
    }

    [Test]
    public void Exposure_FollowsTheVariantsOwnGate_NotTheBaseline() {
        // Same rule as MeasuredInFocusHfr: the variant is what Accept applies, so the gate reported must be the
        // one on screen. An optimizer that floored the gate must not make the user's healthy current settings look
        // starved, and a user who hand-set their OWN gate to 0 must be told so on the Current view.
        var optimizedFloored = new OptimizationSummary {
            OptimizedSensitivity = 0.1, BaselineSensitivity = 10.0,
            ExposureAdvice = Advice(3, 12, 4.1), BaselineExposureAdvice = Advice(3, 3, 22.0, exposureIsNotTheLimit: true)
        };
        var currentOfFloored = StarDetectionOptimizerWizardVM.BuildCurrentSummary(optimizedFloored);

        var optimizedHealthy = new OptimizationSummary {
            OptimizedSensitivity = 12.0, BaselineSensitivity = 0.2,
            ExposureAdvice = Advice(3, 3, 30.0, exposureIsNotTheLimit: true), BaselineExposureAdvice = Advice(3, 12, 4.1)
        };
        var currentOfHealthy = StarDetectionOptimizerWizardVM.BuildCurrentSummary(optimizedHealthy);

        Assert.Multiple(() => {
            Assert.That(optimizedFloored.HasExposureRecommendation, Is.True, "the optimized gate is floored");
            Assert.That(currentOfFloored.HasExposureRecommendation, Is.False, "the user's own gate is healthy");
            Assert.That(optimizedHealthy.HasExposureRecommendation, Is.False);
            Assert.That(currentOfHealthy.HasExposureRecommendation, Is.True, "the user hand-set their gate to 0.2");
        });
    }

    [Test]
    public void Exposure_BuildCurrentSummary_CarriesTheBaselineAdvice() {
        var optimized = new OptimizationSummary {
            RunExposureSeconds = 3.0,
            OptimizedSensitivity = 0.1, BaselineSensitivity = 0.2,
            ExposureAdvice = Advice(3, 12, 4.1), BaselineExposureAdvice = Advice(3, 9, 5.4)
        };
        var current = StarDetectionOptimizerWizardVM.BuildCurrentSummary(optimized);

        Assert.Multiple(() => {
            Assert.That(current.OptimizedSensitivity, Is.EqualTo(0.2), "the Current view reports the current gate");
            Assert.That(current.BaselineSensitivity, Is.EqualTo(0.2));
            Assert.That(current.ExposureAdvice, Is.SameAs(optimized.BaselineExposureAdvice),
                "the Current view's advice is the one derived from the CURRENT settings' accepted stars");
            Assert.That(current.BaselineExposureAdvice, Is.SameAs(optimized.BaselineExposureAdvice));
            Assert.That(current.RunExposureSeconds, Is.EqualTo(3.0), "both variants ran on the same frames");
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
        var s = Starved(advice: Advice(40, 40, 6.0, raw: 111.1, wasCapped: true));
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

    // ---- Star signal body copy (one case per state) ----------------------------------------------------------

    private static string Body(OptimizationSummary s, bool live) =>
        StarDetectionOptimizerWizardVM.DescribeExposureRecommendation(s, live);

    [Test]
    public void ExposureCopy_HealthyGate_SaysNothing() {
        Assert.That(Body(Starved(sensitivity: 10.0, advice: Advice(3, 12, 4.1)), live: true), Is.Empty);
    }

    [Test]
    public void ExposureCopy_Live_ActionableIncrease_NamesTheGateAndTheExposure() {
        var text = Body(Starved(sensitivity: 0.1, advice: Advice(3, 12, 4.1)), live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("Brightness Sensitivity sits at 0.1"));
            Assert.That(text, Does.Contain("low-confidence detections"));
            Assert.That(text, Does.Contain("At about 12 s the same stars would reach the detector's normal acceptance level."));
            // The block follows the SELECTED variant, and on the Current view the gate is the user's own value —
            // so the copy must never attribute it to the optimizer.
            Assert.That(text, Does.Not.Contain("optimizer"));
            Assert.That(text, Does.Not.Contain("NINA's focuser options"), "that is the Replay-only instruction");
        });
    }

    [Test]
    public void ExposureCopy_Capped_ReportsWhatTheRawDerivationAsksFor() {
        // CapLimitsRecommendation (not WasCapped): the cap actually shaped what is being offered, so the row's
        // 12 s is a partial step and the body has to say what the data really wants — and what it would cost.
        var advice = Advice(3, 12, 1.9, raw: 75.0, wasCapped: true);
        Assume.That(advice.CapLimitsRecommendation, Is.True, "fixture guard: this state is what the branch selects on");
        var text = Body(Starved(advice: advice, offsetSteps: 5), live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("about 75 s per frame"));
            Assert.That(text, Does.Contain("roughly 14 minutes per auto-focus run"), "75 s x (2*5+1) frames = 13.75 min");
            Assert.That(text, Does.Contain("this filter is the limit"));
            Assert.That(text, Does.Contain("filter offset"));
        });
    }

    [Test]
    public void ExposureCopy_AlreadyPastTheCap_StillReportsWhatTheDataAsksFor() {
        // WasCapped is true but CapLimitsRecommendation is FALSE (nothing was delivered for the cap to limit), and
        // yet this is exactly the case where the user most needs to hear that the data wants more than is on offer.
        var advice = Advice(40, 40, 6.0, raw: 111.1, wasCapped: true);
        Assume.That(advice.CapLimitsRecommendation, Is.False);
        Assume.That(advice.IncreasesExposure, Is.False);
        var text = Body(Starved(advice: advice), live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("about 111 s per frame"));
            Assert.That(text, Does.Not.Contain("At about"), "there is no longer exposure to offer");
        });
    }

    [Test]
    public void ExposureCopy_ExposureIsNotTheLimit_SaysStarPoorNotUnderExposed() {
        var text = Body(Starved(advice: Advice(5, 5, 22.0, exposureIsNotTheLimit: true)), live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("star-poor, not under-exposed"));
            Assert.That(text, Does.Not.Contain("per frame"), "no exposure figure: exposure is not the problem");
        });
    }

    [Test]
    public void ExposureCopy_NoDerivableNumber_GivesTheDiagnosisOnly() {
        // Fewer than MinFramesForRecommendation usable frames, no per-star SNRs, or an unknown exposure to scale
        // from. Never scale a factor off an unknown base — say what is wrong and stop.
        var text = Body(Starved(advice: null), live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("low-confidence detections"));
            Assert.That(text, Does.Not.Contain("acceptance level"));
            Assert.That(text, Does.Not.Contain("per frame"));
        });
    }

    [Test]
    public void ExposureCopy_Replay_KeepsAcceptAndPointsAtTheProfile() {
        var text = Body(Starved(advice: Advice(3, 12, 4.1)), live: false);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("You can still accept these settings"));
            Assert.That(text, Does.Contain("about 12 s in NINA's focuser options"));
            Assert.That(text, Does.Contain("Live mode"));
        });
    }

    [Test]
    public void ExposureCopy_Replay_WithNoNumber_StillKeepsAcceptButNamesNoExposure() {
        var text = Body(Starved(advice: null), live: false);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("You can still accept these settings"));
            Assert.That(text, Does.Not.Contain("NINA's focuser options"), "there is no figure to tell them to set");
        });
    }

    [Test]
    public void ExposureCopy_WhenBinningAlsoChanges_WarnsTheyAreNotAdditive() {
        // Per-star SNRs are measured in the run's BINNED pixel space, so a binning change raises measured signal on
        // its own. Accepting both recommendations from one run over-lengthens the exposure. The block is qualified
        // rather than suppressed: the diagnosis is true regardless of binning.
        var both = Starved(advice: Advice(3, 12, 4.1), runBinning: 1, recommendedBinning: 2);
        Assume.That(both.DetectionBinningDiffers, Is.True, "fixture guard");
        var onlyExposure = Starved(advice: Advice(3, 12, 4.1), runBinning: 1, recommendedBinning: 1);
        Assume.That(onlyExposure.DetectionBinningDiffers, Is.False, "fixture guard");

        Assert.Multiple(() => {
            Assert.That(Body(both, live: true), Does.Contain("not additive"));
            Assert.That(Body(onlyExposure, live: true), Does.Not.Contain("not additive"));
        });
    }

    [Test]
    public void ExposureCopy_NoDerivedIncrease_SkipsTheBinningCaveat() {
        // Nothing to over-apply when there is no exposure figure on offer, so the caveat would only add noise.
        var s = Starved(advice: Advice(5, 5, 22.0, exposureIsNotTheLimit: true), runBinning: 1, recommendedBinning: 2);
        Assume.That(s.DetectionBinningDiffers, Is.True, "fixture guard");
        Assert.That(Body(s, live: true), Does.Not.Contain("not additive"));
    }
}
