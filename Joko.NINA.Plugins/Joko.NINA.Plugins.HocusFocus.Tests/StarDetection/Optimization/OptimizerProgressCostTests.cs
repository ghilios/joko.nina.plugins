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
