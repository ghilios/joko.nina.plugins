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
    // about COST need no statistic; ADVICE needs one, and now has ExposureRecommendation.WingIsShedding.

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

    [Test]
    public void SearchExposureAdvice_SheddingWings_TellsTheUserToConsiderStopping_AndWhatCancelCosts() {
        // The user's actual question, asked two hours into a search: "should I abort and try again with a longer
        // exposure?". It is answered from the SEED evaluation, which runs BEFORE the search, so the answer existed
        // in the first minute.
        //
        // DISCRIMINATING: make BuildSearchExposureAdvice return string.Empty and every assertion fails.
        var text = StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice(
            new[] { SeedMetrics(wingRejected: 600, wingAccepted: 200) },
            HocusFocusStarDetection.BuildDefaultStarDetectorParams(), currentExposureSeconds: 2.0);

        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("outer frames"), "it names the population the verdict rests on");
            Assert.That(text, Does.Contain("longer exposure"));
            Assert.That(text, Does.Contain("Cancel"), "the house rule: name a control that exists");
            Assert.That(text, Does.Contain("nothing is written to your profile"),
                "not knowing this is why the user sat through the two hours");
            Assert.That(text, Does.Not.Contain("S/N"),
                "the accepted-star S/N is exactly the number that would have said 'yours is already fine'");
        });
    }

    [Test]
    public void SearchExposureAdvice_HealthyWings_SaysNOTHING() {
        // A note that always fires says nothing -- the same rule the cost note follows ("absent entirely when the
        // search is in a cheap region"). This is also the case wave 8 refused to ship advice for.
        //
        // DISCRIMINATING: drop the WingIsShedding test and this fails.
        var text = StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice(
            new[] { SeedMetrics(wingRejected: 1, wingAccepted: 500) },
            HocusFocusStarDetection.BuildDefaultStarDetectorParams(), currentExposureSeconds: 2.0);

        Assert.That(text, Is.Empty);
    }

    [Test]
    public void SearchExposureAdvice_MultiRun_TakesTheWORSTRun_NotTheAverage() {
        // A multi-run optimization lands ONE settings bundle across all of them, so the run that is worst off is
        // what makes the search's answer untrustworthy. Averaging would understate it.
        //
        // BOTH RUNS MUST BE SHEDDING for this to discriminate, and that correction came from neutralizing it: the
        // aggregation `continue`s past any run whose wings are healthy, so pairing a shedding run with a HEALTHY
        // one leaves the worst and the average identical and the test proves nothing. An earlier version of this
        // test did exactly that and passed under a deliberately-averaged implementation.
        //
        // DISCRIMINATING as written: 0.75 and 0.30 average to 0.525, which renders "53%", not "75%".
        var text = StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice(
            new[] { SeedMetrics(wingRejected: 600, wingAccepted: 200),   // 600/800 = 0.75
                    SeedMetrics(wingRejected: 300, wingAccepted: 700) }, // 300/1000 = 0.30
            HocusFocusStarDetection.BuildDefaultStarDetectorParams(), currentExposureSeconds: 2.0);

        Assert.Multiple(() => {
            Assert.That(text, Is.Not.Empty);
            Assert.That(text, Does.Contain("75%"), "the WORST run's fraction, not a blend");
            Assert.That(text, Does.Not.Contain("53%"));
        });
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
