#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// F52 — a two-hour optimization logged ONE line and showed only a bar. The search reported progress at PHASE
/// boundaries, and a phase can run for over an hour, so "where did the time go?" was not answerable afterwards by
/// anyone with any tool.
/// </summary>
[TestFixture]
public class OptimizerProgressCostTests {

    private sealed class SyncProgress : IProgress<OptimizationProgress> {
        public List<OptimizationProgress> Reports { get; } = new List<OptimizationProgress>();

        public void Report(OptimizationProgress value) => Reports.Add(value);
    }

    // A landscape whose optimum sits at a DEEPER structure-layer count than the seed, so the search is obliged to
    // walk into the expensive corner -- which is exactly the situation the field session hit.
    private static RunEvaluationMetrics RunFor(StarDetectorParams p) {
        var dSens = (p.Sensitivity - 10.0) / 20.0;
        var dLayers = (p.StructureLayers - 7.0) / 4.0;
        var dist = Math.Sqrt(dSens * dSens + dLayers * dLayers);
        return new RunEvaluationMetrics {
            SigmaFocus = 100.0 * (0.02 + dist),
            LooStdError = double.NaN,
            StepSize = 100.0,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(50, 10).ToList()
        };
    }

    private static Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> Evaluator() =>
        (p, _) => Task.FromResult<IReadOnlyList<RunEvaluationMetrics>>(new[] { RunFor(p) });

    private static async Task<SyncProgress> RunSearch(StarDetectorParams seed = null) {
        var progress = new SyncProgress();
        await new StarDetectionOptimizer().OptimizeAsync(
            seed ?? new StarDetectorParams { Sensitivity = 2.0, StarClippingMultiplier = 2.0, StructureLayers = 4 },
            OptimizerVariable.CreateCuratedSet(), Evaluator(),
            new OptimizerSettings { MaxEvaluations = 400, CoarseGridLevels = 4, StepFloorFraction = 0.125 },
            progress, CancellationToken.None);
        return progress;
    }

    [Test]
    public async Task Progress_ArrivesEveryNEvaluations_NotOnlyAtPhaseBoundaries() {
        var progress = await RunSearch();
        var evaluations = progress.Reports.Max(r => r.Evaluations);

        // The whole defect: a phase can run for over an hour between reports. At one report per
        // ProgressEvaluationInterval evaluations there must be at least evaluations/interval of them, which is far
        // more than the handful of phase boundaries the search has.
        // DISCRIMINATING: deleting the periodic report drops this to the phase-boundary count.
        var expectedFloor = evaluations / 10;
        Assert.That(progress.Reports.Count, Is.GreaterThanOrEqualTo(expectedFloor),
            $"a {evaluations}-evaluation search must report at least {expectedFloor} times, not once per phase");
    }

    // ── F79: which steps are expensive, and saying so before the wait rather than after ──────────────────────
    //
    // An EARLY-axis move rebuilds AND evicts every frame's DetectionContext; a LATE move re-scores the cached
    // one. The two differ by one to two orders of magnitude, the search runs them in blocks, and the panel
    // previously could not tell them apart -- so a block of early probes froze the counter for minutes with
    // nothing on screen to distinguish it from a hang, and the blended per-step mean made the "at most N more"
    // bound an underestimate exactly when it mattered.

    [Test]
    public async Task Progress_ReportsOnEveryCompletedEvaluation_NotOnlyEveryTenth() {
        // DISCRIMINATING: the reason the counter freezes. At one report per ten evaluations a block of 38 s
        // early probes is six minutes of a motionless bar. Restore the every-tenth gate on the UI report and the
        // report count drops to ~1/10 of the evaluation count.
        var progress = await RunSearch();
        var evaluations = progress.Reports.Max(r => r.Evaluations);

        Assert.That(progress.Reports.Count, Is.GreaterThanOrEqualTo(evaluations),
            "every completed evaluation must refresh the readout; the once-per-ten cadence is for the LOG");
    }

    [Test]
    public async Task ExpensiveStep_IsAnnouncedBEFOREItRuns_NotOnlyAfterItFinishes() {
        // The announcement has to precede the wait or it is worthless: the whole complaint is the silence WHILE
        // the step runs. A pre-report is identifiable as one carrying StepIsExpensive with the SAME evaluation
        // count as the previous report (nothing has completed yet).
        // DISCRIMINATING: delete the ReportInFlight call and no such pair exists.
        var progress = await RunSearch();

        var announcedEarly = false;
        for (var i = 1; i < progress.Reports.Count; i++) {
            if (progress.Reports[i].StepIsExpensive &&
                progress.Reports[i].Evaluations == progress.Reports[i - 1].Evaluations) {
                announcedEarly = true;
                break;
            }
        }
        Assert.That(announcedEarly, Is.True,
            "an expensive step must be announced at the START of the wait, not after it");
    }

    [Test]
    public async Task StructureLayerProbes_AreClassifiedExpensive_AndSensitivityProbesAreNot() {
        // The classification is measured at its cause -- the early cache key -- so it must hold across phases.
        // Phase A grids Sensitivity x StarClippingMultiplier, both LATE, so nothing there can be expensive;
        // Phase B's early stage moves StructureLayers, which is EARLY, so something must be.
        var progress = await RunSearch();

        Assert.Multiple(() => {
            Assert.That(progress.Reports.Any(r => r.StepIsExpensive), Is.True,
                "a search that walks the structure-layer axis must report expensive steps");
            Assert.That(progress.Reports.Any(r => !r.StepIsExpensive), Is.True,
                "the coarse grid moves only LATE axes and must stay cheap");
            Assert.That(progress.Reports.All(r => r.ExpensiveStepsPossible), Is.True,
                "the curated set contains early axes, so no projection may be offered for this search");
        });
    }

    [Test]
    public async Task CheapAndExpensiveRates_AreTrackedSeparately_NotAsOneBlendedMean() {
        // DISCRIMINATING: one blended mean is the reason the old bound was not a bound. Both split rates must be
        // real finite numbers by the end of a search that ran both kinds of step.
        var progress = await RunSearch();
        var last = progress.Reports.Last();

        Assert.Multiple(() => {
            Assert.That(double.IsFinite(last.SecondsPerCheapEvaluation), Is.True,
                "the cache-hit rate is what tells the user most steps are fast");
            Assert.That(double.IsFinite(last.SecondsPerExpensiveEvaluation), Is.True,
                "the rebuild rate is what tells the user why it has stopped moving");
            Assert.That(last.SecondsPerCheapEvaluation, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(last.SecondsPerExpensiveEvaluation, Is.GreaterThanOrEqualTo(0.0));
        });
    }

    [Test]
    public async Task ASearchWithNoEarlyAxes_ReportsThatNoExpensiveStepIsPossible() {
        // The narrowed feedback path can hand the search a purely LATE variable set. That search genuinely has a
        // uniform step cost, and the wizard is entitled to keep projecting a duration for it -- so the flag has
        // to distinguish the two rather than being hardcoded on.
        // DISCRIMINATING against reporting ExpensiveStepsPossible unconditionally.
        var progress = new SyncProgress();
        var lateOnly = OptimizerVariable.CreateCuratedSet()
            .Where(v => !StarDetector.IsEarlyCacheKeyParameter(v.Name))
            .ToList();
        Assume.That(lateOnly, Is.Not.Empty);

        await new StarDetectionOptimizer().OptimizeAsync(
            new StarDetectorParams { Sensitivity = 2.0, StarClippingMultiplier = 2.0, StructureLayers = 4 },
            lateOnly, Evaluator(),
            new OptimizerSettings { MaxEvaluations = 120, CoarseGridLevels = 4, StepFloorFraction = 0.125 },
            progress, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(progress.Reports.Any(), Is.True);
            Assert.That(progress.Reports.All(r => !r.ExpensiveStepsPossible), Is.True);
            Assert.That(progress.Reports.All(r => !r.StepIsExpensive), Is.True,
                "with no early axis to move, no candidate can force a context rebuild");
        });
    }

    [Test]
    public async Task TheSeedEvaluation_ContributesToNeitherRate() {
        // The seed's cost depends entirely on the CALLER: the wizard pre-warms every frame's context before the
        // search starts, TestApp does not. Folding it into either class would mis-scale that class by exactly
        // the amount the caller's warm-up did or did not do.
        // DISCRIMINATING: count the seed as expensive and a search with ZERO early moves still reports a finite
        // expensive rate.
        var progress = new SyncProgress();
        var lateOnly = OptimizerVariable.CreateCuratedSet()
            .Where(v => !StarDetector.IsEarlyCacheKeyParameter(v.Name))
            .ToList();

        await new StarDetectionOptimizer().OptimizeAsync(
            new StarDetectorParams { Sensitivity = 2.0, StarClippingMultiplier = 2.0, StructureLayers = 4 },
            lateOnly, Evaluator(),
            new OptimizerSettings { MaxEvaluations = 120, CoarseGridLevels = 4, StepFloorFraction = 0.125 },
            progress, CancellationToken.None);

        Assert.That(double.IsNaN(progress.Reports.Last().SecondsPerExpensiveEvaluation), Is.True,
            "no expensive step ran, so there is no expensive rate to report");
    }

    [Test]
    public async Task Progress_CarriesElapsedAndPerEvaluationCost() {
        var progress = await RunSearch();
        var timed = progress.Reports.Where(r => r.Evaluations > 0).ToList();

        Assert.Multiple(() => {
            Assert.That(timed, Is.Not.Empty);
            // DISCRIMINATING: both fields are new. Elapsed must actually advance (a default TimeSpan would pass a
            // >= 0 check), and the rate must be a real finite number rather than the NaN it starts at.
            Assert.That(timed.Last().Elapsed, Is.GreaterThan(TimeSpan.Zero), "elapsed must be measured, not defaulted");
            Assert.That(double.IsFinite(timed.Last().SecondsPerEvaluation), Is.True,
                "seconds-per-evaluation is what turns elapsed time into a decision; NaN is no better than absent");
            Assert.That(timed.Last().SecondsPerEvaluation, Is.GreaterThanOrEqualTo(0.0));
        });
    }

    // The two CostNote tests ("names the expensive knob when the search goes deeper" / "says nothing when no
    // candidate is deeper") were retired with the note itself: the sparse AtrousWaveletFast swap made per-layer
    // wavelet cost nearly flat, so structure-layer depth stopped being a cost driver worth narrating
    // (docs/atrous-wavelet-fast-design.md).

    [Test]
    public void EffectiveStructureLayers_CountsTheBoostThatIsActuallyInForce() {
        // The cost readout and the detector's step 4 must agree on the depth, or the note describes a computation
        // that is not happening. DISCRIMINATING against returning p.StructureLayers unconditionally.
        Assert.Multiple(() => {
            Assert.That(StarDetector.EffectiveStructureLayers(
                new StarDetectorParams { StructureLayers = 4 }), Is.EqualTo(4), "no defocus path => the raw value");
            Assert.That(StarDetector.EffectiveStructureLayers(
                new StarDetectorParams { StructureLayers = 4, DefocusAwareDonutDetection = true }), Is.EqualTo(6),
                "the donut master applies its default boost even with the explicit structure axis off");
            Assert.That(StarDetector.EffectiveStructureLayers(
                new StarDetectorParams { StructureLayers = 4, DefocusAwareStructure = true, StructureLayerBoost = 3 }),
                Is.EqualTo(7), "the explicit axis wins over the donut default");
        });
    }

    // ── F52(c): the ADVICE, which wave 8 deliberately did not ship ──────────────────────────────────────────
    //
    // Wave 8 shipped (a) and (b) -- the rate, the bound on what is left, which knob made the search expensive, and
    // what Cancel costs -- and withheld (c), because advice derived from the exposure statistic of the day "would
    // tell precisely the users who most need a longer exposure that theirs is already fine": on the reporting
    // user's own rig that statistic read S/N 1438.6 against a target of 10. The separation is kept exactly: facts
    // about COST need no statistic; ADVICE needs one -- and as of wave 10 it does not have one again.

    /// <summary>A 9-frame seed evaluation whose OUTER third sheds <paramref name="wingRejected"/> candidates
    /// against <paramref name="wingAccepted"/> accepted, placed on the wing axis.</summary>
    private static RunEvaluationMetrics SeedMetrics(int wingRejected, int wingAccepted) {
        const int n = 9;
        var rejected = new int[n];
        var accepted = Enumerable.Repeat(500, n).ToArray();
        foreach (var i in new[] { 0, 8, 1 }) { rejected[i] = wingRejected; accepted[i] = wingAccepted; }
        return new RunEvaluationMetrics {
            FrameStarSnrs = Enumerable.Range(0, n).Select(_ => (IReadOnlyList<double>)Enumerable.Repeat(40.0, 20).ToArray()).ToArray(),
            FrameStarCounts = accepted,
            FrameLowSensitivityCounts = rejected,
            FrameTooFlatCounts = new int[n],
            FrameFocuserPositions = Enumerable.Range(0, n).Select(i => 1000 + (i - 4) * 10).ToArray(),
            BestFocusPosition = 1000.0,
            StepSize = 10.0
        };
    }

    // ── F52(c) IS WITHHELD AGAIN (wave 10). These three tests are REWRITTEN, not deleted. ─────────────────
    //
    // Wave 8 refused to ship this advice for want of a statistic; wave 9 shipped it on
    // ExposureRecommendation.WingIsShedding; wave 10's full-bank population check refuted that statistic and
    // withdrew the verdict. This advice tells the user to CANCEL a running two-hour optimization and is computed
    // from the SEED evaluation -- i.e. in the first minute -- so a statistic that fires on the great majority of
    // bank runs meant advising most users to abandon a search before it had done anything.
    //
    // The tests stay because the WITHDRAWAL is the thing that now needs guarding: the successor statistic
    // re-enables this by changing one condition, and these are what will stop it shipping silently.

    [Test]
    public void SearchExposureAdvice_IsWITHHELD_EvenOnTheFixtureThatUsedToTriggerIt() {
        // The exact fixture that produced the advice in wave 9 -- a wing fraction of 0.75.
        //
        // DISCRIMINATING: restore the WingIsShedding condition and this fails.
        var text = StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice(
            new[] { SeedMetrics(wingRejected: 600, wingAccepted: 200) },
            HocusFocusStarDetection.BuildDefaultStarDetectorParams(), currentExposureSeconds: 2.0);

        Assert.That(text, Is.Empty,
            "no advice may be given until it has a statistic that survives a population -- wave 8's position, "
            + "reached again on better evidence");
    }

    [Test]
    public void SearchExposureAdvice_IsWithheldForEVERYWingFraction_NotJustTheHealthyOne() {
        // The withdrawal has to be unconditional on the statistic, not a raised threshold. A higher bar would
        // still be measuring the detector's global reject rate -- the bank's fractions are 0.000 or 0.352-0.879,
        // so any threshold in that gap selects the same runs, and one above it would still not be measuring
        // WINGS. Four real-bank runs reject FEWER candidates in their wings than in their cores.
        Assert.Multiple(() => {
            foreach (var (rejected, accepted) in new[] { (1, 500), (300, 700), (600, 200), (900, 100) }) {
                Assert.That(StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice(
                    new[] { SeedMetrics(rejected, accepted) },
                    HocusFocusStarDetection.BuildDefaultStarDetectorParams(), 2.0),
                    Is.Empty, $"wing fraction {(double)rejected / (rejected + accepted):F2}");
            }
        });
    }

    [Test]
    public void SearchExposureAdvice_MultiRun_IsWithheldToo_SoTheWorstRunAggregationCannotLeakBack() {
        // Wave 9's aggregation took the WORST run rather than an average, on the sound reasoning that one
        // settings bundle lands on all of them. That reasoning is untouched and will be needed again; what is
        // withdrawn is the statistic it aggregated. Pinned so a successor cannot re-enable the aggregation
        // without also re-enabling the condition deliberately.
        var text = StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice(
            new[] { SeedMetrics(wingRejected: 600, wingAccepted: 200),
                    SeedMetrics(wingRejected: 300, wingAccepted: 700) },
            HocusFocusStarDetection.BuildDefaultStarDetectorParams(), currentExposureSeconds: 2.0);

        Assert.That(text, Is.Empty);
    }

    [Test]
    public void SearchExposureAdvice_NoSeedMetrics_SaysNothingRatherThanGuessing() {
        // GUARD. A run with no seed evaluation has not looked at its wings, and the advice tells the user to
        // abandon a sweep -- the one place this must stay silent rather than default to something.
        Assert.Multiple(() => {
            Assert.That(StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice(
                null, HocusFocusStarDetection.BuildDefaultStarDetectorParams(), 2.0), Is.Empty);
            Assert.That(StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice(
                new RunEvaluationMetrics[0], HocusFocusStarDetection.BuildDefaultStarDetectorParams(), 2.0), Is.Empty);
            Assert.That(StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice(
                new[] { SeedMetrics(600, 200) }, HocusFocusStarDetection.BuildDefaultStarDetectorParams(), 0.0),
                Is.Empty, "an unknown exposure has nothing to say about exposure");
        });
    }

    // ── F79: the copy the user reads ────────────────────────────────────────────────────────────────────────

    [Test]
    public void TimingText_WithExpensiveStepsPossible_OffersNoProjectedDuration() {
        // THE DECISION. The old line read "at most N more" off ONE blended mean, which is not a bound: the search
        // opens with a seed and a coarse grid that are all cache hits, so the mean is small right up to the moment
        // the first early stage multiplies the real remaining cost tenfold. The mix is also not knowable in
        // advance. So no duration is offered at all.
        // DISCRIMINATING: reinstate any projection and "at most"/"h"/"min more" reappears.
        var text = StarDetectionOptimizerWizardVM.BuildProgressTimingText(
            expensiveStepsPossible: true, blendedSeconds: 3.4, cheapSeconds: 3.1, expensiveSeconds: 38.0, remaining: 188);

        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("188 steps left"));
            Assert.That(text, Does.Contain("not predictable"));
            Assert.That(text, Does.Contain("fast steps"));
            Assert.That(text, Does.Contain("slow steps"));
            Assert.That(text, Does.Not.Contain("at most"), "the figure that was never a bound must not return");
            Assert.That(text, Does.Not.Contain("usually much less"));
        });
    }

    [Test]
    public void TimingText_WithNoEarlyAxes_KeepsTheBoundedForm() {
        // A purely LATE variable set (the narrowed feedback path) genuinely has a uniform step cost, so a
        // projection there is honest and is the more useful line. DISCRIMINATING against withholding it always.
        var text = StarDetectionOptimizerWizardVM.BuildProgressTimingText(
            expensiveStepsPossible: false, blendedSeconds: 2.0, cheapSeconds: 2.0, expensiveSeconds: double.NaN, remaining: 90);

        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("at most"));
            Assert.That(text, Does.Contain("3 min more"));
            Assert.That(text, Does.Contain("usually much less"));
        });
    }

    [Test]
    public void TimingText_BeforeEitherClassHasBeenMeasured_SaysNothing() {
        // GUARD: the first moments of a search have measured nothing, and inventing a rate there is worse than an
        // empty row.
        Assert.That(StarDetectionOptimizerWizardVM.BuildProgressTimingText(
            expensiveStepsPossible: true, blendedSeconds: double.NaN,
            cheapSeconds: double.NaN, expensiveSeconds: double.NaN, remaining: 250), Is.Empty);
    }

    [Test]
    public void TimingText_WithOnlyOneClassMeasured_ReportsThatOneRatePlainly() {
        // Before the first early stage arrives only the cache-hit rate exists. Labelling it "fast steps" with no
        // "slow steps" to contrast against would be meaningless, so it degrades to the plain form.
        var text = StarDetectionOptimizerWizardVM.BuildProgressTimingText(
            expensiveStepsPossible: true, blendedSeconds: 0.4, cheapSeconds: 0.4, expensiveSeconds: double.NaN, remaining: 200);

        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("400 ms per step"));
            Assert.That(text, Does.Contain("200 steps left"));
            Assert.That(text, Does.Not.Contain("fast steps"));
        });
    }

    [Test]
    public void TimingText_AtTheBudgetCap_DropsTheStepsLeftClause() {
        // GUARD: "0 steps left — remaining time not predictable" is nonsense.
        var text = StarDetectionOptimizerWizardVM.BuildProgressTimingText(
            expensiveStepsPossible: true, blendedSeconds: 3.4, cheapSeconds: 3.1, expensiveSeconds: 38.0, remaining: 0);

        Assert.Multiple(() => {
            Assert.That(text, Does.Not.Contain("steps left"));
            Assert.That(text, Does.Not.Contain("not predictable"));
            Assert.That(text, Does.Contain("slow steps"));
        });
    }

    [Test]
    public void PhaseHeading_NamesTheSlowStepWhileOneIsRunning_AndIsUntouchedOtherwise() {
        // The heading is the first thing read when the bar stops moving; if it still says "Refining settings"
        // there is nothing on screen that distinguishes working from hung.
        Assert.Multiple(() => {
            Assert.That(StarDetectionOptimizerWizardVM.DecorateExpensivePhase("Refining settings", stepIsExpensive: true),
                Is.EqualTo("Refining settings — slow step (re-analyzing every frame)"));
            Assert.That(StarDetectionOptimizerWizardVM.DecorateExpensivePhase("Refining settings", stepIsExpensive: false),
                Is.EqualTo("Refining settings"));
            // GUARD: no bare suffix floating above the bar between phases.
            Assert.That(StarDetectionOptimizerWizardVM.DecorateExpensivePhase(null, stepIsExpensive: true), Is.Null);
            Assert.That(StarDetectionOptimizerWizardVM.DecorateExpensivePhase("", stepIsExpensive: true), Is.Empty);
        });
    }

    [Test]
    public void EffectiveStructureLayers_IsNullSafeAndNeverReturnsZeroLayersForRealParams() {
        // GUARD: the optimizer calls this on every evaluation and the wizard on every report; a null here would
        // take down a two-hour run at the moment it tried to explain itself.
        Assert.Multiple(() => {
            Assert.That(StarDetector.EffectiveStructureLayers(null), Is.EqualTo(0));
            Assert.That(StarDetector.EffectiveStructureLayers(
                new StarDetectorParams { StructureLayers = 1, DefocusAwareStructure = true, StructureLayerBoost = -5 }),
                Is.EqualTo(1), "the boost can never drive the residual below a single layer");
        });
    }
}
