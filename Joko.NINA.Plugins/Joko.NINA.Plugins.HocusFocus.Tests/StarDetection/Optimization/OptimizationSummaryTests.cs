#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
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
            Assert.That(s.DetectionBinningText, Is.EqualTo("1x1 -> 2x2 (this run; measured in-focus HFR 6.2 px)"));
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
            Assert.That(s.DetectionBinningText, Is.EqualTo("1x1 -> 2x2 (this run; measured in-focus HFR 8.4 px)"));
        });
    }
}
