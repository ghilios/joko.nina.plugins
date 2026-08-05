#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
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

    /// <summary>
    /// F20 part 1 — the entry's actual title. An undersampled rig (D01_ultrawide_40mm: fit vertex 0.762 px against
    /// the 1.2 px default gate) must report that its stars are smaller than the minimum HFR.
    /// </summary>
    [Test]
    public void HasUndersampledStars_FiresWhenTheVertexIsAtOrBelowTheGate() {
        var s = new OptimizationSummary { MeasuredInFocusHfr = 0.762, VariantMinHfr = 1.2, RunDetectionBinning = 1 };
        Assert.That(s.HasUndersampledStars, Is.True);
    }

    /// <summary>D05_tec140_1000mm, the control: vertex 1.804 px, well clear of the gate. Must stay silent.</summary>
    [Test]
    public void HasUndersampledStars_StaysSilentOnAWellSampledRig() {
        var s = new OptimizationSummary { MeasuredInFocusHfr = 1.804, VariantMinHfr = 1.2, RunDetectionBinning = 1 };
        Assert.That(s.HasUndersampledStars, Is.False);
    }

    /// <summary>
    /// F38 — the summary reports in CAPTURED pixels while the gate is BINNED, so the flag has to convert. A 2.2 px
    /// captured vertex clears a 1.2 px gate at 1x1 and does NOT at 2x2, where it is really 1.1 binned px. Shares
    /// MinHfrSeed.IsBelowGate with the seed so the two can never disagree.
    /// </summary>
    [Test]
    public void HasUndersampledStars_ConvertsCapturedPixelsIntoTheGatesBinnedSpace() {
        var unbinned = new OptimizationSummary { MeasuredInFocusHfr = 2.2, VariantMinHfr = 1.2, RunDetectionBinning = 1 };
        var binned = new OptimizationSummary { MeasuredInFocusHfr = 2.2, VariantMinHfr = 1.2, RunDetectionBinning = 2 };
        Assert.Multiple(() => {
            Assert.That(unbinned.HasUndersampledStars, Is.False, "2.2 px clears a 1.2 px gate at 1x1");
            Assert.That(binned.HasUndersampledStars, Is.True, "the same 2.2 px is 1.1 binned px at 2x2");
        });
    }

    /// <summary>
    /// A summary built before this feature (or by a test that does not exercise it) carries NaN for both operands
    /// and must not fire — the note is an assertion about a measurement, so no measurement means no note.
    /// </summary>
    [Test]
    public void HasUndersampledStars_StaysSilentWithoutAMeasurement() {
        Assert.Multiple(() => {
            Assert.That(new OptimizationSummary().HasUndersampledStars, Is.False);
            Assert.That(new OptimizationSummary { MeasuredInFocusHfr = 0.5 }.HasUndersampledStars, Is.False);
            Assert.That(new OptimizationSummary { VariantMinHfr = 1.2 }.HasUndersampledStars, Is.False);
        });
    }

    /// <summary>
    /// The flag follows the VARIANT, which is the whole reason it is named that way: on the Current view the gate
    /// is the user's own hand-set value. A user who raised MinHFR themselves must be told their own gate is what
    /// emptied the curve, not the optimizer's.
    /// </summary>
    [Test]
    public void HasUndersampledStars_FollowsTheVariantsOwnGate() {
        var optimizedClears = new OptimizationSummary {
            MeasuredInFocusHfr = 1.5, VariantMinHfr = 1.2, BaselineMinHfr = 3.0, RunDetectionBinning = 1
        };
        var currentDoesNot = new OptimizationSummary {
            MeasuredInFocusHfr = 1.5, VariantMinHfr = 3.0, BaselineMinHfr = 3.0, RunDetectionBinning = 1
        };
        Assert.Multiple(() => {
            Assert.That(optimizedClears.HasUndersampledStars, Is.False);
            Assert.That(currentDoesNot.HasUndersampledStars, Is.True);
        });
    }
}

/// <summary>
/// The bank's NINA-loadable settings handoff (<c>hocusfocus_star_detection.json</c>): a run folder must be able to
/// carry its optimizer landing in a form the app's Import and the AF-replay override can both apply correctly.
/// </summary>
[TestFixture]
public class OptimizedLandingExportTests {

    private static OptimizedStarDetectionSettings Landing() => new OptimizedStarDetectionSettings {
        BrightnessSensitivity = 17.67,
        MinHFR = 0.7,
        StarClippingMultiplier = 2.0,
        StructureLayers = 6,
        StarPeakResponse = 0.68
    };

    /// <summary>
    /// THE defect this guards. If the landing is written only into the nested optimizedSettings block, both
    /// consumers silently apply the BASELINE instead: StarDetectionOptions' import copies the flat knobs last (so
    /// flat wins), and the AF-replay in-memory override reads the flat knobs only and never looks at the nested DTO.
    /// Neither errors. So the flat layer must carry the landing too.
    /// </summary>
    [Test]
    public void FromOptimizedLanding_WritesTheLandingIntoTheFLATKnobs_NotOnlyTheNestedBlock() {
        var baseOptions = new StarDetectionSettingsSnapshot {
            BrightnessSensitivity = 10.0, MinHFR = 1.2, StarClippingMultiplier = 5.0, StructureLayers = 4
        };

        var export = StarDetectionSettingsExport.FromOptimizedLanding(baseOptions, Landing());

        Assert.Multiple(() => {
            Assert.That(export.StarDetection.BrightnessSensitivity, Is.EqualTo(17.67), "flat Sensitivity must be the landing");
            Assert.That(export.StarDetection.MinHFR, Is.EqualTo(0.7), "flat MinHFR must be the landing");
            Assert.That(export.StarDetection.StarClippingMultiplier, Is.EqualTo(2.0));
            Assert.That(export.StarDetection.StructureLayers, Is.EqualTo(6));
            Assert.That(export.StarDetection.OptimizedSettings?.BrightnessSensitivity, Is.EqualTo(17.67), "and the nested block too");
        });
    }

    /// <summary>Importing it must land the user where a wizard Accept would.</summary>
    [Test]
    public void FromOptimizedLanding_LandsInTheOptimizedMode() {
        var export = StarDetectionSettingsExport.FromOptimizedLanding(
            new StarDetectionSettingsSnapshot(), Landing());
        Assert.Multiple(() => {
            Assert.That(export.StarDetection.UseOptimizedSettings, Is.True);
            Assert.That(export.StarDetection.UseAdvanced, Is.False);
            Assert.That(export.FileType, Is.EqualTo(StarDetectionSettingsExport.ExpectedFileType));
        });
    }

    /// <summary>
    /// Knobs the curated axes do NOT cover must come from the options the optimize actually ran with — a landing
    /// replayed against a different detection binning or PSF model is not the configuration that was measured.
    /// </summary>
    [Test]
    public void FromOptimizedLanding_KeepsTheNonAxisKnobsFromTheRun() {
        var baseOptions = new StarDetectionSettingsSnapshot {
            DetectionBinning = DetectionBinningEnum.Bin2, ContaminationSensitivity = 7.5
        };
        var export = StarDetectionSettingsExport.FromOptimizedLanding(baseOptions, Landing());
        Assert.Multiple(() => {
            Assert.That(export.StarDetection.DetectionBinning, Is.EqualTo(DetectionBinningEnum.Bin2));
            Assert.That(export.StarDetection.ContaminationSensitivity, Is.EqualTo(7.5));
        });
    }

    /// <summary>
    /// THE INVARIANT. Every curated axis must have a same-named, same-typed, settable property on the flat snapshot,
    /// so adding an axis to the optimizer cannot silently produce a handoff file that drops it. If this fails, the
    /// new axis needs a matching option property (or an entry in the DTO's NonKnobFields if it describes the run
    /// rather than the detector).
    /// </summary>
    [Test]
    public void EveryCuratedAxisRoundTripsIntoASettingsSnapshot() {
        var unmapped = OptimizedStarDetectionSettings.UnmappedKnobs(typeof(StarDetectionSettingsSnapshot));
        Assert.That(unmapped, Is.Empty, "curated axes with no matching option property: " + string.Join(", ", unmapped));
    }

    /// <summary>The envelope must survive a round trip through disk, or the bank stores something unreadable.</summary>
    [Test]
    public void FromOptimizedLanding_RoundTripsThroughJson() {
        var export = StarDetectionSettingsExport.FromOptimizedLanding(
            new StarDetectionSettingsSnapshot(), Landing());
        var reloaded = StarDetectionSettingsExport.Deserialize(export.Serialize());
        Assert.Multiple(() => {
            Assert.That(reloaded.StarDetection.BrightnessSensitivity, Is.EqualTo(17.67));
            Assert.That(reloaded.StarDetection.MinHFR, Is.EqualTo(0.7));
            Assert.That(reloaded.StarDetection.UseOptimizedSettings, Is.True);
        });
    }

    /// <summary>
    /// The check <c>bank-export-settings</c> runs before it writes anything, exercised here on a landing with
    /// EVERY curated axis moved off its default. <see cref="OptimizedStarDetectionSettings.UnmappedKnobs"/> proves
    /// each axis has somewhere to land; this proves each axis's VALUE actually arrives — a property that would
    /// pass the mapping check and still be wrong if a knob were written to the nested block only.
    /// </summary>
    [Test]
    public void DiffKnobs_IsEmptyAfterAFullRoundTrip_WithEveryAxisMovedOffItsDefault() {
        var landing = new OptimizedStarDetectionSettings {
            BrightnessSensitivity = 17.67, StarClippingMultiplier = 2.0, NoiseClippingMultiplier = 3.875,
            StarPeakResponse = 0.68, MaxDistortion = 0.26, MinHFR = 0.7, StarCenterTolerance = 0.275,
            StructureLayers = 6, NoiseReductionRadius = 4, MinStarBoundingBoxSize = 7,
            HotpixelThresholdingEnabled = true, HotpixelThreshold = 0.002,
            DefocusAwareGates = true, DefocusDistortionSizeReference = 28.75, DefocusDistortionMinFactor = 0.3,
            DefocusCenteringToleranceFactor = 2.5, DefocusAwareStructure = true, StructureLayerBoost = 3,
            DefocusAwareDonutDetection = true, DonutMorphCloseSize = 7, LocallyAdaptiveBinarization = true,
            AdaptiveNoiseBlockSize = 64, DonutMinAnnularityHoleFraction = 0.2, DonutMaxStreakEccentricity = 1.5,
            DonutSaturationBloomRadius = 3.0
        };

        var reloaded = StarDetectionSettingsExport.Deserialize(
            StarDetectionSettingsExport.FromOptimizedLanding(new StarDetectionSettingsSnapshot(), landing).Serialize());

        var diffs = landing.DiffKnobs(reloaded.StarDetection);
        Assert.That(diffs, Is.Empty, "curated axes that did not survive the round trip: " + string.Join(", ", diffs));
    }

    /// <summary>
    /// The F32 bookkeeping fields describe the SEARCH (which candidates were eligible), not the detector, so they
    /// must be registered as non-knobs. An unregistered one would be hunted for as a live option property, and
    /// <see cref="OptimizedStarDetectionSettings.UnmappedKnobs"/> would start failing for a field that has no
    /// business being on the options object at all.
    /// </summary>
    [Test]
    public void KeepFloorBookkeepingIsNotTreatedAsADetectorKnob() {
        var landing = Landing();
        landing.MinDetectionKeepFraction = 0.5;
        landing.LandingDetectionKeepFraction = 0.63;

        var snapshot = new StarDetectionSettingsSnapshot();
        landing.ApplyToFlatOptions(snapshot);

        Assert.Multiple(() => {
            Assert.That(OptimizedStarDetectionSettings.UnmappedKnobs(typeof(StarDetectionSettingsSnapshot)), Is.Empty);
            Assert.That(landing.DiffKnobs(snapshot), Is.Empty,
                "the keep-floor fields must not participate in the knob mapping at all");
        });
    }

    /// <summary>
    /// An unconstrained landing records NO floor — there was none — but DOES record what it kept.
    ///
    /// <para>The asymmetry is deliberate and was corrected mid-wave. Gating both fields on the floor made the
    /// control arm unable to report its own keep fraction, which is the exact number that decides whether a floor
    /// would have bound on that run: the control could not be classified without re-running it. F32 spent two
    /// waves reconstructing this quantity by hand from stored landings, which is the argument for storing it.</para>
    /// </summary>
    [Test]
    public void UnconstrainedLanding_RecordsWhatItKeptButNoFloor() {
        var dto = OptimizedStarDetectionSettings.FromParams(
            new StarDetectorParams(), runCount: 1, baselineJ: 0.9, finalJ: 0.95,
            recommendedStepSize: 50, recommendedOffsetSteps: 4,
            provenance: null, minDetectionKeepFraction: null, landingDetectionKeepFraction: 0.62);

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(dto);

        Assert.Multiple(() => {
            Assert.That(json, Does.Not.Contain("MinDetectionKeepFraction"), "no floor was in force");
            Assert.That(json, Does.Contain("LandingDetectionKeepFraction"), "what it kept is measurable either way");
            Assert.That(dto.LandingDetectionKeepFraction, Is.EqualTo(0.62));
        });
    }

    /// <summary>An unmeasurable keep fraction is omitted rather than written as NaN — no JSON reader should have
    /// to handle a NaN, and an absent field already reads correctly as "there was nothing to measure".</summary>
    [Test]
    public void UnmeasurableKeepFraction_IsOmittedRatherThanWrittenAsNaN() {
        var dto = OptimizedStarDetectionSettings.FromParams(
            new StarDetectorParams(), runCount: 1, baselineJ: 0.9, finalJ: 0.95,
            recommendedStepSize: 50, recommendedOffsetSteps: 4,
            provenance: null, minDetectionKeepFraction: 0.5, landingDetectionKeepFraction: double.NaN);

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(dto);

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("MinDetectionKeepFraction"), "the floor WAS in force and must be recorded");
            Assert.That(json, Does.Not.Contain("LandingDetectionKeepFraction"));
            Assert.That(json, Does.Not.Contain("NaN"));
        });
    }
}
