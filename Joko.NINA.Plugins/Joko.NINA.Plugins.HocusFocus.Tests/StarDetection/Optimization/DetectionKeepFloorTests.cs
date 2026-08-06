#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// F32 — the detection-keep floor: an ACCEPTANCE CONSTRAINT on the optimizer's search, not a term in J and not
/// a rescale of it.
///
/// <para>The landscape below reproduces the defect in miniature. Raising <c>Sensitivity</c> monotonically
/// TIGHTENS the fit (σ_focus falls) and monotonically SHEDS stars, which is exactly what the real bank does:
/// σ_focus improves on 10 of 10 shedding runs while a median 0.243 of recall is given up for a median ΔJ of
/// +0.0125. An unconstrained search therefore climbs to a high gate and a small star list, and the floor's job
/// is to stop it — without touching a single J value.</para>
/// </summary>
[TestFixture]
public class DetectionKeepFloorTests {

    private const int FrameCount = 10;
    private const int SeedStarsPerFrame = 100;

    /// <summary>Stars per frame as a function of the gate: 100 at Sensitivity 0, decaying with a 15-unit scale.
    /// Floored at 10 so the run never trips the NHard starvation floor — the point of this fixture is the
    /// optimizer CHOOSING to shed, not J collapsing to zero, which is a different failure (F20).</summary>
    private static int StarsFor(double sensitivity) =>
        Math.Max(10, (int)Math.Round(SeedStarsPerFrame * Math.Exp(-sensitivity / 15.0)));

    private static RunEvaluationMetrics ShedderRun(StarDetectorParams p, double countScale = 1.0) {
        const double stepSize = 100.0;
        // σ falls as the gate rises: shedding buys a genuinely better fit, exactly as measured on the real bank.
        var sigma = stepSize * Math.Max(0.02, 0.30 - 0.005 * p.Sensitivity);
        var stars = Math.Max(10, (int)Math.Round(StarsFor(p.Sensitivity) * countScale));
        return new RunEvaluationMetrics {
            SigmaFocus = sigma,
            LooStdError = double.NaN,
            StepSize = stepSize,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(stars, FrameCount).ToList()
        };
    }

    private static Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> ShedderEvaluator() =>
        (p, token) => Task.FromResult<IReadOnlyList<RunEvaluationMetrics>>(new[] { ShedderRun(p) });

    private static StarDetectorParams Seed() => new StarDetectorParams {
        Sensitivity = 0.0,
        StarClippingMultiplier = 2.0,
    };

    private static OptimizerSettings Settings(double? keepFloor = null) => new OptimizerSettings {
        MaxEvaluations = 400,
        CoarseGridLevels = 4,
        StepFloorFraction = 0.125,
        MinDetectionKeepFraction = keepFloor
    };

    private static Task<OptimizationResult> RunAsync(OptimizerSettings settings,
        Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> evaluator = null,
        StarDetectorParams seed = null) =>
        new StarDetectionOptimizer().OptimizeAsync(
            seed ?? Seed(), OptimizerVariable.CreateCuratedSet(), evaluator ?? ShedderEvaluator(),
            settings, null, CancellationToken.None);

    /// <summary>
    /// GUARD, NOT A DISCRIMINATOR — this passes with and without the fix, and is kept deliberately because
    /// "null leaves every caller bit-identical" is the property that lets the same binary be its own control
    /// arm. It is excluded from the discriminating-test count on purpose (wave-4 lesson 2: count the tests that
    /// fail when the fix is reverted, not the tests you wrote).
    /// </summary>
    [Test]
    public async Task NoFloor_IsBitIdenticalToTheUnconstrainedSearch() {
        var a = await RunAsync(Settings(keepFloor: null));
        var b = await RunAsync(Settings(keepFloor: null));

        Assert.Multiple(() => {
            Assert.That(a.BestJ, Is.EqualTo(b.BestJ).Within(0.0));
            Assert.That(a.BestParams.Sensitivity, Is.EqualTo(b.BestParams.Sensitivity).Within(0.0));
            Assert.That(a.CandidatesRejectedByKeepFloor, Is.Zero, "no floor => nothing can be rejected by one");
            // The keep fraction is MEASURED unconditionally — it costs nothing (the counts are already in hand)
            // and it is precisely the "keep%" F32 computes by hand from stored landings. Only the JSON omits it
            // without a floor, so an unconstrained landing file stays byte-identical to before this existed.
            Assert.That(a.LandingKeepFraction, Is.EqualTo(b.LandingKeepFraction).Within(0.0));
            Assert.That(a.LandingKeepFraction, Is.LessThan(0.5),
                "fixture precondition: the unconstrained search sheds, which is the defect under test");
        });
    }

    /// <summary>The headline: the unconstrained search sheds, and the floor stops it at the floor.</summary>
    [Test]
    public async Task Floor_RejectsALandingThatShedsBelowIt() {
        var unconstrained = await RunAsync(Settings(keepFloor: null));
        var constrained = await RunAsync(Settings(keepFloor: 0.5));

        var unconstrainedKeep = (double)StarsFor(unconstrained.BestParams.Sensitivity) / SeedStarsPerFrame;

        Assert.Multiple(() => {
            // Precondition: the fixture actually reproduces the defect, else the test proves nothing.
            Assert.That(unconstrainedKeep, Is.LessThan(0.5),
                "fixture precondition: the unconstrained search must shed past the floor being tested");
            Assert.That(constrained.LandingKeepFraction, Is.GreaterThanOrEqualTo(0.5),
                "the landing must satisfy the floor it was searched under");
            Assert.That(constrained.BestParams.Sensitivity, Is.LessThan(unconstrained.BestParams.Sensitivity),
                "the constrained landing must sit at a looser gate than the unconstrained one");
            Assert.That(constrained.CandidatesRejectedByKeepFloor, Is.GreaterThan(0),
                "the constraint must have actually bound; zero rejections would mean this proves nothing");
        });
    }

    /// <summary>The same candidates become admissible once the floor drops beneath their keep fraction, which is
    /// what makes the previous test a statement about the FLOOR rather than about the plumbing.</summary>
    [Test]
    public async Task Floor_AcceptsTheSameCandidatesWhenLoweredBeneathThem() {
        var unconstrained = await RunAsync(Settings(keepFloor: null));
        var loose = await RunAsync(Settings(keepFloor: 0.01));

        Assert.Multiple(() => {
            Assert.That(loose.BestParams.Sensitivity, Is.EqualTo(unconstrained.BestParams.Sensitivity).Within(0.0));
            Assert.That(loose.BestJ, Is.EqualTo(unconstrained.BestJ).Within(0.0),
                "a floor beneath everything visited must not change J or the landing");
            Assert.That(loose.CandidatesRejectedByKeepFloor, Is.Zero);
        });
    }

    /// <summary>J is carried alongside feasibility, never folded into it: the constrained landing's J must be
    /// exactly the J that same θ scores with no floor in force. This is what keeps every wave-1..4 number
    /// comparable, and it is the property a multiplier-based implementation would silently destroy.</summary>
    [Test]
    public async Task Floor_DoesNotAlterJ() {
        var constrained = await RunAsync(Settings(keepFloor: 0.5));

        // Re-score the constrained landing with NO floor by seeding an unconstrained search there: its seed J is
        // that θ's objective value, computed by the same code path with the constraint absent.
        var reseeded = await RunAsync(Settings(keepFloor: null), seed: constrained.BestParams);

        Assert.That(reseeded.SeedJ, Is.EqualTo(constrained.BestJ).Within(0.0),
            "the constrained landing's J must be bit-identical to the same params' unconstrained J");
    }

    /// <summary>
    /// A run whose seed detected NOTHING is exempt rather than infeasible. Rejecting on an undefined ratio would
    /// re-gate exactly the F20/F35 population — a rig whose seed legitimately detects nothing is the case the
    /// MinHFR seeding exists to rescue, and it has to be free to climb from zero.
    /// </summary>
    [Test]
    public async Task Floor_ExemptsARunWhoseSeedDetectedNothing() {
        // Zero stars at the seed gate, real stars once the gate moves off it.
        Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> evaluator =
            (p, token) => {
                var m = ShedderRun(p);
                if (p.Sensitivity <= 0.0) {
                    m.FrameStarCounts = Enumerable.Repeat(0, FrameCount).ToList();
                }
                return Task.FromResult<IReadOnlyList<RunEvaluationMetrics>>(new[] { m });
            };

        var result = await RunAsync(Settings(keepFloor: 0.9), evaluator);

        Assert.Multiple(() => {
            Assert.That(result.SeedRunDetectionTotals, Is.Not.Null);
            Assert.That(result.SeedRunDetectionTotals[0], Is.Zero, "fixture precondition: the seed detects nothing");
            Assert.That(result.BestParams.Sensitivity, Is.GreaterThan(0.0),
                "a zero-baseline run must not be frozen at its seed by a floor it cannot be measured against");
        });
    }

    /// <summary>
    /// The floor is the MIN over runs, not the pooled total. One run collapsing must reject the candidate even
    /// when a second run grows enough to raise the sum — pooling would let a dense run's gains mask another
    /// being gutted.
    /// </summary>
    [Test]
    public async Task Floor_IsMinOverRuns_NotThePooledTotal() {
        // Run 0 collapses hard with the gate; run 1 is ten times denser and barely moves, so the POOLED count
        // stays far above the floor while run 0 falls through it.
        Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> evaluator =
            (p, token) => Task.FromResult<IReadOnlyList<RunEvaluationMetrics>>(new[] {
                ShedderRun(p),
                new RunEvaluationMetrics {
                    SigmaFocus = ShedderRun(p).SigmaFocus,
                    LooStdError = double.NaN,
                    StepSize = 100.0,
                    RSquared = 0.99,
                    ReducedChiSquared = 1.0,
                    FrameStarCounts = Enumerable.Repeat(1000, FrameCount).ToList()
                }
            });

        var result = await RunAsync(Settings(keepFloor: 0.5), evaluator);

        var run0Keep = (double)StarsFor(result.BestParams.Sensitivity) / SeedStarsPerFrame;
        Assert.Multiple(() => {
            Assert.That(run0Keep, Is.GreaterThanOrEqualTo(0.5),
                "the collapsing run must satisfy the floor on its own, not be carried by the dense one");
            Assert.That(result.LandingKeepFraction, Is.GreaterThanOrEqualTo(0.5));
        });
    }

    /// <summary>
    /// The multi-pass ratchet. Both multi-pass callers re-invoke the optimizer with <c>seed = the previous
    /// pass's best</c> (TestApp <c>--continue-rounds</c>, the wizard's Continue button). Measured against each
    /// round's OWN seed, a floor of 0.5 permits 0.25 after two rounds and 0.125 after three — the constraint
    /// paid in installments. Pinning round 0's totals is what makes the floor mean "of where the user started".
    /// </summary>
    [Test]
    public async Task Floor_DoesNotRatchetAcrossContinueRounds() {
        var settings = Settings(keepFloor: 0.5);
        var result = await RunAsync(settings);
        var originalSeedTotals = result.SeedRunDetectionTotals;
        Assert.That(originalSeedTotals, Is.Not.Null);

        // What the multi-pass callers do: pin round 0's seed totals for every later round.
        settings.DetectionKeepBaselineTotals = originalSeedTotals;

        for (var round = 0; round < 3; round++) {
            result = await new StarDetectionOptimizer().OptimizeAsync(
                result.BestParams, OptimizerVariable.CreateCuratedSet(result.BestParams),
                ShedderEvaluator(), settings, null, CancellationToken.None);
        }

        // Measured against the ORIGINAL seed, not the last round's.
        var keepVsOriginalSeed = (double)StarsFor(result.BestParams.Sensitivity) / SeedStarsPerFrame;
        Assert.That(keepVsOriginalSeed, Is.GreaterThanOrEqualTo(0.5),
            "three continue rounds must not walk through a floor anchored at the first pass's seed");
    }

    /// <summary>Every accepted incumbent is feasible, at every floor — the invariant all three accept sites
    /// (CoarseGrid, CompassStage, RevertNeutralAxes) exist to preserve.</summary>
    [TestCase(0.25)]
    [TestCase(0.50)]
    [TestCase(0.75)]
    [TestCase(0.95)]
    public async Task Floor_LandingAlwaysSatisfiesTheFloor(double floor) {
        var result = await RunAsync(Settings(keepFloor: floor));

        Assert.Multiple(() => {
            Assert.That(result.LandingKeepFraction, Is.GreaterThanOrEqualTo(floor),
                $"landing must satisfy the floor {floor}");
            Assert.That(result.BestJ, Is.GreaterThanOrEqualTo(result.SeedJ),
                "the never-regress floor survives the constraint: the seed is always feasible");
        });
    }

    /// <summary>The seed is feasible by construction even when it cannot possibly satisfy the floor against
    /// itself-as-baseline — otherwise the feasible set would be empty and the search would have nowhere to
    /// stand.</summary>
    [Test]
    public async Task Floor_SeedIsAlwaysFeasible() {
        var result = await RunAsync(Settings(keepFloor: 1.0));

        Assert.Multiple(() => {
            Assert.That(result.BestJ, Is.GreaterThanOrEqualTo(result.SeedJ));
            Assert.That(result.LandingKeepFraction, Is.GreaterThanOrEqualTo(1.0),
                "at a floor of 1.0 nothing that sheds a single star may win");
        });
    }
}
