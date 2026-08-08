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
