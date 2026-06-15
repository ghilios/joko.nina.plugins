#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

[TestFixture]
public class RunEvaluationDataTests {

    private static AlglibAPI NewAlglib() => new AlglibAPI();

    private static RunFitConfig DefaultFitConfig() => new RunFitConfig {
        StepSize = 100,
        UseWeights = true,
        MaxOutlierRejections = 0,
        RejectionConfidence = 0.0,
        PreferredModel = null
    };

    // A clean symmetric hyperbola hfr(pos) = sqrt(a^2 + ((pos - p0)/b)^2).
    private const double HyperbolaA = 1.5;     // minimum HFR at focus
    private const double HyperbolaB = 8.0;     // focuser-steps-per-unit; larger b => flatter curve
    private const int HyperbolaP0 = 10000;     // best-focus position

    // A small label box CENTERED on (cx,cy) (half-extent 3, matching the prior point-radius intent), so an accepted
    // star whose center is at (or very near) (cx,cy) lies INSIDE the box (recovered), while a far one does not.
    private static LabelBox Box(double cx, double cy, double half = 3.0) =>
        new LabelBox(cx - half, cy - half, 2 * half, 2 * half);

    private static double Hfr(int pos) {
        var dx = (pos - HyperbolaP0) / HyperbolaB;
        return Math.Sqrt(HyperbolaA * HyperbolaA + dx * dx);
    }

    // Detection delegate that returns a clean hyperbola HFR, a fixed small stdev, and a fixed star count.
    private static Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> HyperbolaDetect(
        int starCount = 50, double hfrStdDev = 0.05, IReadOnlyList<(double X, double Y)> centers = null) {
        return (image, p, token) => {
            var pos = (int)image; // synthetic frames carry their focuser position as the payload
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = Hfr(pos),
                HFRStdDev = hfrStdDev,
                StarCount = starCount,
                StarCenters = centers ?? Array.Empty<(double X, double Y)>()
            });
        };
    }

    private static List<RunFrame> NineFrames() {
        // Nine evenly-spaced focuser positions centered on best focus.
        var frames = new List<RunFrame>();
        for (var i = -4; i <= 4; i++) {
            var pos = HyperbolaP0 + i * 100;
            frames.Add(new RunFrame { FrameId = $"pos_{pos}", FocuserPosition = pos, Image = pos });
        }
        return frames;
    }

    [Test]
    public async Task EvaluateAsync_FramesAtSamePosition_PoolIntoOneScatterPoint() {
        // Two frames at the SAME focuser position with different HFRs must pool to one point at the average HFR.
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect = (image, p, token) => {
            // Frame A reports HFR=2.0, frame B reports HFR=4.0 (both at the same position) => pooled mean 3.0.
            var hfr = ((string)((object[])image)[1]) == "A" ? 2.0 : 4.0;
            return Task.FromResult(new FrameDetectionResult { AverageHFR = hfr, HFRStdDev = 0.1, StarCount = 30, StarCenters = Array.Empty<(double X, double Y)>() });
        };

        // Three positions so the fit has enough points; the middle position is sampled twice.
        var frames = new List<RunFrame> {
            new RunFrame { FrameId = "p0a", FocuserPosition = 9800, Image = new object[] { 9800, "A" } },
            new RunFrame { FrameId = "p1a", FocuserPosition = 10000, Image = new object[] { 10000, "A" } },
            new RunFrame { FrameId = "p1b", FocuserPosition = 10000, Image = new object[] { 10000, "B" } },
            new RunFrame { FrameId = "p2a", FocuserPosition = 10200, Image = new object[] { 10200, "A" } },
        };

        var data = new RunEvaluationData("pool", frames, detect, NewAlglib(), DefaultFitConfig());
        var result = await data.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);

        Assert.Multiple(() => {
            // Four frames in, but only THREE distinct focuser positions feeding the fit.
            Assert.That(result.PooledPointCount, Is.EqualTo(3), "two frames at one position must pool to a single fit point");
            Assert.That(result.Metrics.FrameStarCounts.Count, Is.EqualTo(4), "FrameStarCounts is per-frame, not per-position");
            Assert.That(result.PooledHfrAt(10000), Is.EqualTo(3.0).Within(1e-9), "pooled HFR is the mean of the per-frame HFRs at that position");
        });
    }

    [Test]
    public async Task EvaluateAsync_CleanHyperbola_FitsWithHighRSquaredAndFiniteSigma() {
        var data = new RunEvaluationData("clean", NineFrames(), HyperbolaDetect(), NewAlglib(), DefaultFitConfig());
        var metrics = await data.EvaluateAsync(new StarDetectorParams(), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(metrics.RSquared, Is.GreaterThan(0.99), "clean hyperbola fits near-perfectly");
            Assert.That(double.IsNaN(metrics.SigmaFocus), Is.False, "focus sigma must be finite for a clean fit");
            Assert.That(metrics.SigmaFocus, Is.GreaterThan(0.0));
            Assert.That(metrics.StepSize, Is.EqualTo(100.0), "StepSize is taken from the fit config");
            Assert.That(metrics.FrameStarCounts.Count, Is.EqualTo(9), "one star count per frame");
            Assert.That(metrics.FrameStarCounts.All(c => c == 50), Is.True);
            Assert.That(metrics.Recall, Is.Null, "no labels => null label scores");
            Assert.That(metrics.Precision, Is.Null);

            // The whole point of T3: these metrics score well in T2's objective.
            var j = OptimizationObjective.JRun(metrics, new ObjectiveConstants());
            Assert.That(j, Is.GreaterThan(0.7), "a clean star-rich run should score highly");
        });
    }

    [Test]
    public async Task EvaluateAsync_StarvedFrames_HardFailsViaJRun() {
        // Some frames return fewer than NHard stars => the objective's hard constraint maps J to 0.
        var c = new ObjectiveConstants();
        var frames = NineFrames();
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect = (image, p, token) => {
            var pos = (int)image;
            // Starve the two wings (below NHard); the rest are healthy.
            var starved = pos <= HyperbolaP0 - 300 || pos >= HyperbolaP0 + 300;
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = Hfr(pos),
                HFRStdDev = 0.05,
                StarCount = starved ? 1 : 50,
                StarCenters = Array.Empty<(double X, double Y)>()
            });
        };

        var data = new RunEvaluationData("starved", frames, detect, NewAlglib(), DefaultFitConfig());
        var metrics = await data.EvaluateAsync(new StarDetectorParams(), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(metrics.FrameStarCounts.Count(n => n < c.NHard), Is.GreaterThan(c.MaxFramesBelowHardFloor),
                "the run must contain more starved frames than the allowed floor");
            Assert.That(OptimizationObjective.JRun(metrics, c), Is.EqualTo(0.0), "starved frames must hard-fail the objective");
        });
    }

    [Test]
    public async Task EvaluateAsync_TooFewPositions_ReturnsNaNSigmaWithoutThrowing() {
        // Only two distinct positions: too few to fit a hyperbola. Must degrade gracefully (NaN sigma, J=0).
        var frames = new List<RunFrame> {
            new RunFrame { FrameId = "a", FocuserPosition = 9900, Image = 9900 },
            new RunFrame { FrameId = "b", FocuserPosition = 10100, Image = 10100 },
        };
        var data = new RunEvaluationData("few", frames, HyperbolaDetect(), NewAlglib(), DefaultFitConfig());

        RunEvaluationMetrics metrics = null;
        Assert.DoesNotThrowAsync(async () => metrics = await data.EvaluateAsync(new StarDetectorParams(), CancellationToken.None));
        Assert.Multiple(() => {
            Assert.That(double.IsNaN(metrics.SigmaFocus), Is.True, "too few points => no fitted sigma");
            Assert.That(double.IsNaN(metrics.LooStdError), Is.True, "too few points => no LOO fallback either");
            Assert.That(OptimizationObjective.JRun(metrics, new ObjectiveConstants()), Is.EqualTo(0.0), "unusable focus sigma => hard fail");
        });
    }

    [Test]
    public async Task EvaluateAsync_Labels_ProduceRecallAndPrecision() {
        // One accepted star at (100,100). Labels: a missed star FAR from any accepted center (not recovered =>
        // recall 0); a should-reject star also far from accepted centers (correctly excluded => precision 1).
        var accepted = new List<(double X, double Y)> { (100.0, 100.0) };
        var detect = HyperbolaDetect(centers: accepted);

        var labels = new List<FrameLabels> {
            new FrameLabels {
                FocuserPosition = HyperbolaP0,
                Missed = new List<LabelBox> { Box(5000.0, 5000.0) },        // no accepted center inside => recall 0
                ShouldReject = new List<LabelBox> { Box(6000.0, 6000.0) },  // no accepted center inside => precision 1
                RadiusPx = 3.0
            }
        };

        var data = new RunEvaluationData("labeled", NineFrames(), detect, NewAlglib(), DefaultFitConfig(), labels);
        var metrics = await data.EvaluateAsync(new StarDetectorParams(), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(metrics.Recall, Is.Not.Null);
            Assert.That(metrics.Precision, Is.Not.Null);
            Assert.That(metrics.Recall.Value, Is.EqualTo(0.0).Within(1e-9), "missed star not recovered => recall 0");
            Assert.That(metrics.Precision.Value, Is.EqualTo(1.0).Within(1e-9), "should-reject star correctly excluded => precision 1");
        });
    }

    [Test]
    public async Task EvaluateAsync_WronglyRejected_FoldsIntoRecall() {
        // Two accepted stars. One WronglyRejected target lands ON an accepted center (recovered) and one does NOT
        // (not recovered) => recall 0.5. Precision stays a function of ShouldReject only (none => 1.0). This proves
        // the union of Missed ∪ WronglyRejected drives recall.
        var accepted = new List<(double X, double Y)> { (100.0, 100.0), (200.0, 200.0) };
        var detect = HyperbolaDetect(centers: accepted);

        var labels = new List<FrameLabels> {
            new FrameLabels {
                FocuserPosition = HyperbolaP0,
                Missed = new List<LabelBox>(),                                   // no false negatives
                ShouldReject = new List<LabelBox>(),                            // no false positives
                WronglyRejected = new List<LabelBox> {
                    Box(100.0, 100.0),                                          // contains an accepted center => recovered
                    Box(9000.0, 9000.0)                                         // no accepted center inside => not recovered
                },
                RadiusPx = 3.0
            }
        };

        var data = new RunEvaluationData("wrongly", NineFrames(), detect, NewAlglib(), DefaultFitConfig(), labels);
        var metrics = await data.EvaluateAsync(new StarDetectorParams(), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(metrics.Recall, Is.Not.Null);
            Assert.That(metrics.Recall.Value, Is.EqualTo(0.5).Within(1e-9), "1 of 2 wrongly-rejected targets recovered => recall 0.5");
            Assert.That(metrics.Precision, Is.Not.Null);
            Assert.That(metrics.Precision.Value, Is.EqualTo(1.0).Within(1e-9), "no should-reject targets => precision 1");
        });
    }

    [Test]
    public async Task EvaluateAsync_MissedAndWronglyRejected_UnionDrivesRecall() {
        // Missed (recovered) ∪ WronglyRejected (one recovered, one not) => 2 of 3 recall targets recovered => 2/3.
        var accepted = new List<(double X, double Y)> { (100.0, 100.0), (300.0, 300.0) };
        var detect = HyperbolaDetect(centers: accepted);

        var labels = new List<FrameLabels> {
            new FrameLabels {
                FocuserPosition = HyperbolaP0,
                Missed = new List<LabelBox> { Box(100.0, 100.0) },          // recovered
                ShouldReject = new List<LabelBox>(),
                WronglyRejected = new List<LabelBox> {
                    Box(300.0, 300.0),                                      // recovered
                    Box(9000.0, 9000.0)                                     // not recovered
                },
                RadiusPx = 3.0
            }
        };

        var data = new RunEvaluationData("union", NineFrames(), detect, NewAlglib(), DefaultFitConfig(), labels);
        var metrics = await data.EvaluateAsync(new StarDetectorParams(), CancellationToken.None);

        Assert.That(metrics.Recall, Is.Not.Null);
        Assert.That(metrics.Recall.Value, Is.EqualTo(2.0 / 3.0).Within(1e-9), "2 of 3 (missed ∪ wrongly-rejected) recovered");
    }

    [Test]
    public async Task EvaluateAsync_MissedOnly_WronglyRejectedNullOrEmpty_RecallUnchanged() {
        // Bit-identical-behavior guard: with WronglyRejected null AND with it empty, recall must match the
        // missed-only result exactly (the union fold must not perturb the prior single-list path).
        var accepted = new List<(double X, double Y)> { (100.0, 100.0) };
        var detect = HyperbolaDetect(centers: accepted);

        FrameLabels MakeLabel(IReadOnlyList<LabelBox> wrongly) => new FrameLabels {
            FocuserPosition = HyperbolaP0,
            Missed = new List<LabelBox> { Box(100.0, 100.0), Box(9000.0, 9000.0) }, // 1 of 2 recovered => 0.5
            ShouldReject = new List<LabelBox>(),
            WronglyRejected = wrongly,
            RadiusPx = 3.0
        };

        var dataNull = new RunEvaluationData("null", NineFrames(), detect, NewAlglib(), DefaultFitConfig(),
            new List<FrameLabels> { MakeLabel(null) });
        var dataEmpty = new RunEvaluationData("empty", NineFrames(), detect, NewAlglib(), DefaultFitConfig(),
            new List<FrameLabels> { MakeLabel(new List<LabelBox>()) });

        var mNull = await dataNull.EvaluateAsync(new StarDetectorParams(), CancellationToken.None);
        var mEmpty = await dataEmpty.EvaluateAsync(new StarDetectorParams(), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(mNull.Recall.Value, Is.EqualTo(0.5).Within(1e-12), "null WronglyRejected => missed-only recall");
            Assert.That(mEmpty.Recall.Value, Is.EqualTo(0.5).Within(1e-12), "empty WronglyRejected => missed-only recall");
        });
    }

    [Test]
    public async Task EvaluateAsync_NoLabels_LeavesRecallPrecisionNull() {
        var data = new RunEvaluationData("nolabels", NineFrames(), HyperbolaDetect(), NewAlglib(), DefaultFitConfig());
        var metrics = await data.EvaluateAsync(new StarDetectorParams(), CancellationToken.None);
        Assert.Multiple(() => {
            Assert.That(metrics.Recall, Is.Null);
            Assert.That(metrics.Precision, Is.Null);
        });
    }

    [Test]
    public async Task CreateEvaluator_OverTwoRuns_ReturnsOnePerRunInOrder() {
        var runA = new RunEvaluationData("A", NineFrames(), HyperbolaDetect(starCount: 40), NewAlglib(), DefaultFitConfig());
        var runB = new RunEvaluationData("B", NineFrames(), HyperbolaDetect(starCount: 60), NewAlglib(), DefaultFitConfig());

        var evaluator = RunEvaluationData.CreateEvaluator(new[] { runA, runB });
        var metrics = await evaluator(new StarDetectorParams(), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(metrics.Count, Is.EqualTo(2), "one metrics per run");
            Assert.That(metrics[0].FrameStarCounts.All(c => c == 40), Is.True, "first run's frames in order");
            Assert.That(metrics[1].FrameStarCounts.All(c => c == 60), Is.True, "second run's frames in order");
        });
    }

    [Test]
    public async Task CreateEvaluator_PluggedIntoOptimizer_CompletesAndNeverRegresses() {
        // The real integration proof: T3's evaluator drives T2's optimizer end-to-end with a real fitter.
        // Make J depend on Sensitivity so there is something to optimize: star count grows toward an optimum
        // Sensitivity, which raises S_stars (and thus J) without ever hard-failing.
        const double optSensitivity = 10.0;
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect = (image, p, token) => {
            var pos = (int)image;
            // Star count peaks at optSensitivity. The range is chosen to sit in the SENSITIVE band of S_stars
            // (NFloor=8, NTarget=20): ~22 at the optimum (S_stars ~ 1) sliding toward ~10 at the seed (median
            // term unsaturated), so J responds monotonically to Sensitivity — and always stays above the hard
            // floor (NHard=3).
            var dist = Math.Abs(p.Sensitivity - optSensitivity);
            var starCount = (int)Math.Max(10, Math.Round(22 - 1.5 * dist));
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = Hfr(pos),
                HFRStdDev = 0.05,
                StarCount = starCount,
                StarCenters = Array.Empty<(double X, double Y)>()
            });
        };

        var run = new RunEvaluationData("opt", NineFrames(), detect, NewAlglib(), DefaultFitConfig());
        var evaluator = RunEvaluationData.CreateEvaluator(new[] { run });

        var seed = new StarDetectorParams { Sensitivity = 2.0, StarClippingMultiplier = 2.0 };
        var variables = OptimizerVariable.CreateCuratedSet();
        var settings = new OptimizerSettings { MaxEvaluations = 200, CoarseGridLevels = 4, StepFloorFraction = 0.125 };

        var result = await new StarDetectionOptimizer().OptimizeAsync(seed, variables, evaluator, settings, null, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.BestJ, Is.GreaterThanOrEqualTo(result.SeedJ), "must never regress below seed");
            Assert.That(result.Evaluations, Is.LessThanOrEqualTo(settings.MaxEvaluations), "must respect the eval budget");
            Assert.That(result.BestJ, Is.GreaterThan(0.0), "a star-rich, well-fit run must score above zero");
            // It should have moved Sensitivity toward the star-count optimum.
            Assert.That(Math.Abs(result.BestParams.Sensitivity - optSensitivity),
                Is.LessThan(Math.Abs(seed.Sensitivity - optSensitivity)), "Sensitivity should approach the star-count optimum");
        });
    }
}
