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
/// Unit tests for every word of the optimizer Summary's "Star signal" block — the recommended-exposure ROW, the
/// body PARAGRAPH, and the row's derivation TOOLTIP (<see cref="StarSignalCopy"/>) — plus the summary predicates
/// that decide whether the block appears at all.
///
/// <para>They live in one fixture because the invariant under test spans all three surfaces: the row carries the
/// numbers, the tooltip the derivation, and the paragraph is capped at a diagnosis plus AT MOST ONE instruction.
/// Splitting them by which type happens to host the member would hide exactly the contradictions these tests
/// exist to catch.</para>
/// </summary>
[TestFixture]
public class StarSignalCopyTests {

    // ---- Block visibility and the row ------------------------------------------------------------------------
    //
    // The block fires on the Sensitivity gate ALONE; the recommendation's sub-states decide what it then says.

    private static ExposureRecommendation Advice(
        double current, double recommended, double snr,
        double raw = double.NaN, bool wasCapped = false, bool cappedByAbsoluteLimit = false,
        bool exposureIsNotTheLimit = false, int shortFrameCount = 0, bool starCountIsTheLimit = false) =>
        new ExposureRecommendation {
            HasRecommendation = true,
            CurrentSeconds = current,
            RecommendedSeconds = recommended,
            MeasuredSnr = snr,
            RawSeconds = double.IsNaN(raw) ? recommended : raw,
            WasCapped = wasCapped,
            CappedByAbsoluteLimit = cappedByAbsoluteLimit,
            ExposureIsNotTheLimit = exposureIsNotTheLimit,
            StarCountIsTheLimit = starCountIsTheLimit,
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
        "Stars barely cleared the noise and Brightness Sensitivity is at the bottom of its range (0.125): this focus result rests on low-confidence detections.";

    private const string NeutralOpening =
        "Brightness Sensitivity is at the bottom of its range (0.125): this focus result rests on low-confidence detections.";

    private const string ReplayReassurance =
        " You can still accept these settings; they are the best fit for frames like these.";

    /// <summary>Live's one instruction: it names the capture action sitting directly below the paragraph, and
    /// quotes no seconds (the row above and the editable box below both carry the number).</summary>
    private const string LiveCaptureInstruction =
        " Capture a new sweep at the longer exposure and re-tune these settings on frames with real signal.";

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
            Assert.That(StarSignalCopy.DescribeExposureRow(s), Is.Empty, "no advice => no row, but the block still shows its diagnosis");
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
    public void ExposureRow_Increase_ShowsBeforeAndAfterWithTheMeasurement() {
        var s = Starved(advice: Advice(3, 12, 4.1));
        Assert.That(StarSignalCopy.DescribeExposureRow(s), Is.EqualTo("3 s → 12 s (measured star S/N 4.1; target 10)"));
    }

    [Test]
    public void ExposureRow_AlreadyPastTheCap_ReadsAsUnchangedRatherThanANoOpArrow() {
        // The recommender never returns a SHORTER exposure, so a 40 s narrowband run (past the 30 s absolute cap)
        // gets RecommendedSeconds == CurrentSeconds while HasRecommendation stays true. Branching on
        // HasRecommendation here would print "40 s → 40 s".
        var s = Starved(advice: Advice(40, 40, 6.0, raw: 111.1, wasCapped: true, cappedByAbsoluteLimit: true));
        Assert.Multiple(() => {
            Assert.That(StarSignalCopy.DescribeExposureRow(s), Is.EqualTo("40 s (unchanged; measured star S/N 6; target 10)"));
            Assert.That(StarSignalCopy.DescribeExposureRow(s), Does.Not.Contain("→"));
        });
    }

    [Test]
    public void ExposureRow_NoRecommendation_IsEmpty() {
        var s = Starved(advice: new ExposureRecommendation {
            HasRecommendation = false, CurrentSeconds = 3,
            RecommendedSeconds = double.NaN, RawSeconds = double.NaN, MeasuredSnr = double.NaN
        });
        Assert.That(StarSignalCopy.DescribeExposureRow(s), Is.Empty, "an empty row is hidden rather than rendering a bare label");
    }

    // ---- Star signal body copy -------------------------------------------------------------------------------
    //
    // The body is DIAGNOSIS + AT MOST ONE INSTRUCTION. The states that produce it (Replay, a binding cap, a
    // competing binning recommendation) are independently reachable, so the tests that matter most are the
    // WHOLE-STRING ones on the stacked combinations: clause-level Does.Contain assertions cannot see a paragraph
    // that has accumulated into self-contradiction, which is exactly how the first version shipped six sentences
    // offering three mutually exclusive remedies.

    // useCurrent defaults to false because Optimize is the wizard's default mode and all but one case below runs
    // in it; the use-current cases pass it explicitly so they read as the exception they are.
    private static string Body(OptimizationSummary s, bool live, bool useCurrent = false) =>
        StarSignalCopy.DescribeExposureRecommendation(s, live, useCurrent);

    private static string Detail(OptimizationSummary s) =>
        StarSignalCopy.DescribeExposureDerivation(s);

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
    public void ExposureCopy_Live_UseCurrentMode_NamesTheModeBecauseTheButtonIsHidden() {
        // GOLDEN. ShowCaptureNewSweep HIDES the capture row in use-current mode — there is nothing to re-tune, and
        // unlike a disconnected camera the mode is fixed for the life of the Summary, so a disabled button would
        // sit dead for the whole run with nothing explaining why. The copy must therefore not name that button;
        // it names the mode to switch to, exactly as the binning block's own use-current branch does.
        var text = Body(Starved(advice: Advice(3, 12, 4.1)), live: true, useCurrent: true);
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + " Run this wizard in Optimize mode to capture a new sweep at the longer exposure and re-tune for the new frames."));
        Assert.Multiple(() => {
            Assert.That(text, Does.Not.Contain("Capture a new sweep at the longer exposure to re-tune"),
                "the instruction that names the button must not appear while the button is hidden");
            Assert.That(text, Does.Not.Contain("12 s"), "the number still lives in the row, not the paragraph");
            Assert.That(text, Does.Not.Contain("You can still accept"), "that reassurance is Replay's");
        });
    }

    [Test]
    public void ExposureCopy_Live_UseCurrentMode_AbsoluteCap_StillCarriesTheNarrowbandRider() {
        // The rider belongs to the instruction, not to any one mode's phrasing of it: the cap trimmed the number,
        // so the fix is partial however the user goes about getting the longer frames. One instruction either way.
        var advice = Advice(10, 30, 5.0, raw: 40.0, wasCapped: true, cappedByAbsoluteLimit: true);
        var text = Body(Starved(advice: advice, offsetSteps: 5), live: true, useCurrent: true);
        Assert.That(text, Is.EqualTo(
            LowSignalOpening
            + " Reaching the default S/N target would need more time per frame than a sweep can spend; the recommendation above stops at 30 s."
            + " Run this wizard in Optimize mode to capture a new sweep at the longer exposure and re-tune for the new frames"
            + "; or, if you shoot narrowband, auto-focus through a broadband filter with a filter offset instead."));
    }

    [Test]
    public void ExposureCopy_Replay_UseCurrentMode_IsUnaffected() {
        // Replay's instruction names a NINA setting and a mode to re-run in, not a control on this page, so it
        // stays true in either optimize mode — the use-current branch is Live's alone.
        var optimize = Body(Starved(advice: Advice(3, 12, 4.1)), live: false);
        var useCurrent = Body(Starved(advice: Advice(3, 12, 4.1)), live: false, useCurrent: true);
        Assert.That(useCurrent, Is.EqualTo(optimize));
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
            + " Star brightness is not the problem: your brightest stars measure S/N 22, meeting the default S/N target of 10."
            + " Brightness Sensitivity is low, so far fainter candidates are being admitted below them."
            + " " + GateRemedy));
        Assert.Multiple(() => {
            Assert.That(text, Does.Not.Contain("barely cleared"));
            Assert.That(text, Does.Not.Contain("star-poor"), "nothing measured says the field is thin");
            Assert.That(text, Does.Not.Contain("per frame"), "no exposure figure: exposure is not the problem");
        });
    }

    /// <summary>F49's remedy, spelled out so the golden whole-string assertions stay readable.</summary>
    private const string GateRemedy =
        "The gate was lowered to admit more candidates, not because star brightness was missing. "
        + "Set Brightness Sensitivity by hand in the star detection options and run this wizard again in "
        + "\"use current settings\" mode to compare that landing against this one.";

    [Test]
    public void ExposureCopy_F49_RichWellExposedFieldAtAFlooredGate_EndsOnAnInstruction() {
        // F49, from a FIELD report on the shipped Default profile: Sensitivity 15.667 -> 0.000, StarClip 6.750 ->
        // 0.250, stars per frame 834 -> 5766, and "your brightest stars measure S/N 1438.6" -- followed by NOTHING.
        //
        // THIS IS THE DEFECT, and it is structural rather than a missing string. RemedyFor's three ranked branches
        // are ALL exposure or binning remedies: with ExposureIsNotTheLimit true, IncreasesExposure is false (which
        // kills branches 1 and 3) and branch 2's !ExposureIsNotTheLimit is false, so every branch fell through and
        // the method returned string.Empty -- on a page whose contract is "diagnosis plus exactly one instruction".
        //
        // DISCRIMINATING: delete the F49 branch and this fails, because the body ends after the admission sentence.
        var text = Body(Starved(advice: Advice(2, 2, 1438.6, exposureIsNotTheLimit: true)), live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.EndWith(GateRemedy), "the block must end on exactly one instruction");
            // It names the GATE, which is the lever on this population -- not the exposure, which is not.
            Assert.That(text, Does.Contain("Brightness Sensitivity by hand"));
            Assert.That(text, Does.Not.Contain("longer exposure"),
                "on a field measuring S/N 1438.6 against a target of 10, an exposure remedy is F19's error mirrored");
            Assert.That(text, Does.Not.Contain("broadband filter"),
                "the filter route belongs to the exposure-exhausted state, which this is not");
        });
    }

    [Test]
    public void ExposureCopy_F49_NamesNoControlThatDoesNotExist() {
        // The house rule at ShowOptimizeAgainAtRecommendedBinning: never describe an action whose control is
        // hidden. F32's MinDetectionKeepFraction is the tempting lever here and it has NO XAML binding ANYWHERE --
        // it is --keep-floor on the harness only. (It would not have bound in any case: it rejects landings
        // keeping FEWER stars than the seed, and this landing keeps ~7x MORE. The pathology is admission, not
        // shedding.) BrightnessSensitivity, by contrast, is bound in OptionsDataTemplates.xaml.
        var text = Body(Starved(advice: Advice(2, 2, 1438.6, exposureIsNotTheLimit: true)), live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.Not.Contain("keep"), "MinDetectionKeepFraction has no user-facing control");
            Assert.That(text, Does.Not.Contain("detection keep"));
        });
    }

    [Test]
    public void ExposureCopy_F49_AnUnmeasuredRunGetsNoGateRemedy_BecauseTheSentenceCLAIMSSomething() {
        // The F49 sentence asserts that star brightness was NOT what was missing. That is only knowable from
        // ExposureIsNotTheLimit (S_now >= the default target). On a run with no derivable recommendation at all —
        // too few usable frames, no per-star SNRs, an unknown exposure to scale from — all that is known is that
        // the gate is floored, and claiming anything about brightness there breaks the same "no claim without
        // evidence" rule the star-poor sentence is gated by, in the opposite direction.
        //
        // DISCRIMINATING: drop the `HasRecommendation && ExposureIsNotTheLimit` guard on the F49 branch and this
        // fails, along with the two diagnosis-only golden tests below it.
        var text = Body(Starved(advice: null), live: true);
        Assert.That(text, Does.Not.Contain("Brightness Sensitivity by hand"));
    }

    [Test]
    public void ExposureCopy_F49_TheGateRemedyIsTheCatchAll_NotAReplacementForTheExposureOnes() {
        // The F49 branch is LAST in the ranking, so it must not displace a remedy that is genuinely available.
        // DISCRIMINATING: move the branch above the IncreasesExposure branch and this fails.
        var raisesExposure = Body(Starved(advice: Advice(2, 8, 5.0)), live: true);
        Assert.Multiple(() => {
            Assert.That(raisesExposure, Does.Not.Contain("Brightness Sensitivity by hand"),
                "a run that CAN be fixed with exposure gets the exposure instruction, not the gate one");
            Assert.That(raisesExposure, Does.Contain("Capture a new sweep at the longer exposure"));
        });
    }

    [Test]
    public void ExposureCopy_ExposureIsNotTheLimit_CallsTheFieldStarPoorOnlyOnShortFrames() {
        // ShortFrameCount is the ONLY measured evidence that a field was too thin: those frames had fewer than the
        // star-count target to offer, so the per-frame statistic fell back to their faintest survivor.
        var text = Body(Starved(advice: Advice(5, 5, 22.0, exposureIsNotTheLimit: true, shortFrameCount: 4)), live: true);
        Assert.That(text, Does.Contain("4 of 9 frames found fewer stars than the star-count target; Brightness Sensitivity is low to scrape for count in a star-poor field."));
    }

    [Test]
    public void ExposureCopy_EveryFrameShort_OffersTheProbe_AndClaimsNothingAboutWhetherItWillWork() {
        // The 3800mm case: EVERY frame short of the star-count target while the stars that WERE found comfortably
        // clear the gate. This copy previously asserted that "a longer exposure or a different gate cannot raise
        // that" -- false, and never measured: 2 s -> 5 s on the reported rig took detected stars 76 -> 101. The
        // replacement must offer the experiment WITHOUT promising it works, and must carry its own stopping rule,
        // or a genuinely sparse field gets walked up to the cap for nothing.
        var text = Body(Starved(advice: Advice(2, 4, 24.0, starCountIsTheLimit: true, shortFrameCount: 9)), live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("All 9 frames found fewer stars than the star-count target"));
            Assert.That(text, Does.Contain("try doubling it"));
            Assert.That(text, Does.Contain("if the star count does not rise, the field is the limit"),
                "the stopping rule is what keeps this from walking the user to the cap");
            Assert.That(text, Does.Not.Contain("cannot raise"), "the false claim this test exists to prevent");
            Assert.That(text, Does.Not.Contain("scraping for count in a star-poor field"),
                "that phrasing blames the gate for a shortfall the gate did not cause");
        });
    }

    [Test]
    public void ExposureCopy_EveryFrameShort_NeverSaysExposureIsNotTheLimit() {
        // The regression that started this: the block must not diagnose starvation and then tell the user the one
        // thing that helps cannot help. Asserted on the whole body so no branch can reintroduce it.
        var text = Body(Starved(advice: Advice(2, 4, 24.0, starCountIsTheLimit: true, shortFrameCount: 9)), live: true);
        Assert.That(text, Does.Not.Contain("Exposure is not"));
    }

    [Test]
    public void ExposureCopy_SomeFramesShort_KeepsTheStarPoorWording() {
        // Guard the boundary: a PARTIAL shortfall is still the old situation — some frames did reach the target,
        // so the field is thin rather than incapable, and the existing wording remains the accurate one.
        var text = Body(Starved(advice: Advice(5, 5, 22.0, exposureIsNotTheLimit: true, shortFrameCount: 8)), live: true);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("8 of 9 frames found fewer stars than the star-count target; Brightness Sensitivity is low to scrape for count in a star-poor field."));
            Assert.That(text, Does.Not.Contain("try doubling it"), "a partial shortfall is the star-poor case, not the probe case");
        });
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
            + " The recommendation above is a partial step: one run raises exposure at most 4x. Expect to repeat this."
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
            + " Reaching the default S/N target would need more time per frame than a sweep can spend; the recommendation above stops at 30 s."
            + ReplayReassurance
            + " Raise your auto-focus exposure to about 30 s in NINA's focuser options, then run this wizard again in Live mode;"
            + " or, if you shoot narrowband, auto-focus through a broadband filter with a filter offset instead."));
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
            + " Reaching the default S/N target would need more time per frame than a sweep can spend; the recommendation above stops at 30 s."
            + LiveCaptureInstruction.TrimEnd('.')
            + "; or, if you shoot narrowband, auto-focus through a broadband filter with a filter offset instead."));
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
            + " Reaching the default S/N target would need more time per frame than an auto-focus sweep can spend; no longer exposure is offered."
            + ReplayReassurance
            + " If you shoot narrowband, auto-focus through a broadband filter with a filter offset instead."));
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
            + " Reaching the default S/N target would need more time per frame than an auto-focus sweep can spend; no longer exposure is offered."
            + " If you shoot narrowband, auto-focus through a broadband filter with a filter offset instead."));
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
            + " Raise your auto-focus exposure to about 12 s in NINA's focuser options, then run this wizard again in Live mode."));
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
            + " Reaching the default S/N target would need more time per frame than a sweep can spend; the recommendation above stops at 30 s."
            + ReplayReassurance
            + " The detection binning change recommended below also raises measured star signal. Change the binning factor first and let the next run re-measure the exposure."));
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
            Assert.That(text, Does.Contain("Change the binning factor first"));
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
            + " These frames record no exposure; the numbers assume your profile's 3 s auto-focus exposure."
            + ReplayReassurance
            + " Raise your auto-focus exposure to about 12 s in NINA's focuser options, then run this wizard again in Live mode."));
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
                + " Capped at 8 s: one run raises exposure at most 4x. Run again to refine."));
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
            + " That is past the 30 s this recommendation will suggest, and past what you already use; nothing longer is offered."));
        Assert.That(text, Does.Not.Contain("a sweep can sustain"));
    }

    [Test]
    public void ExposureDetail_ExposureIsNotTheLimit_DoesNotDeriveAShorterExposure() {
        // The scaling would read "5 s × (10 / 22)² = 1 s", i.e. an invitation to SHORTEN — which the recommender
        // refuses to do and the tooltip must not imply.
        var text = Detail(Starved(advice: Advice(5, 5, 22.0, exposureIsNotTheLimit: true)));
        Assert.That(text, Is.EqualTo("Measured S/N 22 already meets the S/N target of 10: the S/N math derives no longer exposure."));
    }

    [Test]
    public void ExposureDetail_ShortFrames_SaysTheDerivationUnderStatesTheNeed() {
        var text = Detail(Starved(advice: Advice(3, 12, 4.1, raw: 17.8, shortFrameCount: 2)));
        Assert.That(text, Does.Contain("2 of 9 frames had fewer stars than the star-count target; the derived exposure under-states what is needed."));
    }

    [Test]
    public void ExposureDetail_IsEmptyExactlyWhenTheRowIsHidden() {
        // The tooltip is bound, so an empty string would render as an empty popup box. The row's own visibility is
        // the row text being non-empty, so the two must agree.
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
            Assert.That(StarSignalCopy.DescribeExposureRow(shown), Is.Not.Empty, "…and the row it annotates really is on screen");
        });
    }
}
