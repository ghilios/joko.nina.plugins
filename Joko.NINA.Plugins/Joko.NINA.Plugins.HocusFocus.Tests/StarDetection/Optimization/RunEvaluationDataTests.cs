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

    [Test]
    public async Task EvaluateAndFitAsync_ExposesPooledScatterPoints_WithHfrAndErrorBars() {
        // The wizard plots the curve from these points; one per distinct focuser position, Y = pooled HFR, ErrorY =
        // the (display-safe) per-frame stdev. Surfaced from the same pooling that feeds the fit (zero extra cost).
        const double stdev = 0.07;
        var data = new RunEvaluationData("curve", NineFrames(), HyperbolaDetect(hfrStdDev: stdev), NewAlglib(), DefaultFitConfig());

        var result = await data.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);

        Assert.That(result.Points, Is.Not.Null);
        Assert.That(result.Points.Count, Is.EqualTo(9), "one scatter point per distinct focuser position");
        var ordered = result.Points.OrderBy(p => p.X).ToList();
        Assert.Multiple(() => {
            Assert.That(ordered[0].X, Is.EqualTo(HyperbolaP0 - 400).Within(1e-9));
            Assert.That(ordered.Select(p => p.Y), Is.EqualTo(ordered.Select(p => Hfr((int)p.X))).Within(1e-9).AsCollection,
                "each point's Y is the pooled HFR for its position");
            Assert.That(ordered.All(p => Math.Abs(p.ErrorY - stdev) < 1e-9), Is.True, "ErrorY carries the per-position stdev");
        });
    }

    [Test]
    public async Task EvaluateAndFitAsync_FrameProgress_ReportsEveryFrame_AndMetricsAreIdentical() {
        // The optional per-frame progress (used by the seed-guard) must report once per frame, AND the metrics must be
        // byte-identical to the no-progress overload (proving the optimizer hot path, which passes no progress, is
        // unaffected). Force the sequential path so the report order is deterministic.
        var data = new RunEvaluationData("clean", NineFrames(), HyperbolaDetect(), NewAlglib(), DefaultFitConfig()) {
            FrameParallelismOverride = 1
        };
        var p = new StarDetectorParams();

        var reports = new List<RunLoadProgress>();
        var progress = new ImmediateProgress<RunLoadProgress>(reports.Add);
        var withProgress = await data.EvaluateAndFitAsync(p, progress, CancellationToken.None);
        var withoutProgress = await data.EvaluateAndFitAsync(p, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(reports.Count, Is.EqualTo(9), "one progress report per frame");
            Assert.That(reports[^1].Current, Is.EqualTo(9));
            Assert.That(reports[^1].Total, Is.EqualTo(9));
            Assert.That(withProgress.Metrics.SigmaFocus, Is.EqualTo(withoutProgress.Metrics.SigmaFocus).Within(1e-12),
                "progress must not change the computed metrics");
            Assert.That(withProgress.Metrics.RSquared, Is.EqualTo(withoutProgress.Metrics.RSquared).Within(1e-12));
            Assert.That(withProgress.PooledPointCount, Is.EqualTo(withoutProgress.PooledPointCount));
        });
    }

    [Test]
    public async Task EvaluateAsync_PopulatesFrameStarHFRs_AndRegionOccupancy() {
        // The evaluator must surface the per-frame accepted-star HFRs (parallel to FrameStarCounts) and a finite
        // per-frame region occupancy in [0,1] from the FrameDetectionResult, so the objective's new terms can read
        // them. 3 stars in 3 distinct cells of a 3×3 grid over a 900×900 frame => occupancy 3/9.
        const int width = 900, height = 900;
        var centers = new (double X, double Y)[] { (150, 150), (450, 450), (750, 750) };
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect = (image, p, token) => {
            var pos = (int)image;
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = Hfr(pos),
                HFRStdDev = 0.05,
                StarCount = 3,
                StarCenters = centers,
                StarHFRs = new double[] { 2.0, 2.1, 1.9 },
                ImageWidth = width,
                ImageHeight = height
            });
        };
        var data = new RunEvaluationData("plumb", NineFrames(), detect, NewAlglib(), DefaultFitConfig());
        var metrics = await data.EvaluateAsync(new StarDetectorParams(), CancellationToken.None);
        Assert.Multiple(() => {
            Assert.That(metrics.FrameStarHFRs, Is.Not.Null);
            Assert.That(metrics.FrameStarHFRs.Count, Is.EqualTo(metrics.FrameStarCounts.Count));
            for (var i = 0; i < metrics.FrameStarHFRs.Count; i++) {
                Assert.That(metrics.FrameStarHFRs[i].Count, Is.EqualTo(metrics.FrameStarCounts[i]),
                    "per-frame HFR list must be parallel to the accepted star count");
            }
            Assert.That(metrics.FrameRegionOccupancy, Is.Not.Null);
            Assert.That(metrics.FrameRegionOccupancy.Count, Is.EqualTo(metrics.FrameStarCounts.Count));
            foreach (var occ in metrics.FrameRegionOccupancy) {
                Assert.That(occ, Is.EqualTo(3.0 / 9).Within(1e-12), "3 stars in 3 distinct cells of a 3×3 grid => 3/9");
            }
        });
    }

    // ── Focus recovery ────────────────────────────────────────────────────────────────────────────────────────

    private static RunFitConfig UnweightedFitConfig() => new RunFitConfig {
        StepSize = 100,
        UseWeights = false,
        MaxOutlierRejections = 0,
        RejectionConfidence = 0.0,
        PreferredModel = null
    };

    private static List<RunFrame> FramesAt(IEnumerable<int> positions) =>
        positions.Select(pos => new RunFrame { FrameId = $"pos_{pos}", FocuserPosition = pos, Image = pos }).ToList();

    // Detection delegate returning a clean hyperbola HFR with a per-position stdev (so a test can make a chosen
    // position zero-scatter), a fixed star count, and no centers.
    private static Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> HyperbolaDetectWithStdev(
        Func<int, double> stdevFor, int starCount = 50) {
        return (image, p, token) => {
            var pos = (int)image;
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = Hfr(pos),
                HFRStdDev = stdevFor(pos),
                StarCount = starCount,
                StarCenters = Array.Empty<(double X, double Y)>()
            });
        };
    }

    private static double ErrorYAt(RunEvaluationResult r, int x) =>
        r.Points.First(pt => Math.Abs(pt.X - x) < 0.5).ErrorY;

    // A GENTLE symmetric hyperbola about a chosen center (HFR ~2 at focus, ~6 at ±400 for the default sweep). Used by
    // the down-weighting test to place the recovery-position HFRs on a DIFFERENT (false) center than the interior.
    private static double GentleHfr(int pos, int center) {
        const double a = 2.0, b = 70.0;
        var dx = (pos - center) / b;
        return Math.Sqrt(a * a + dx * dx);
    }

    [Test]
    public async Task EvaluateAndFitAsync_RecoverySteps_MarksOutermostNPerSideAsRecovery() {
        // 9 distinct positions (p0-400..p0+400), N=2 => the 2 smallest AND 2 largest positions are recovery; the
        // interior 5 are not. Recovery is defined by distinct position.
        var data = new RunEvaluationData("recovery", NineFrames(), HyperbolaDetect(), NewAlglib(), DefaultFitConfig()) {
            RecoveryStepsPerSide = 2
        };
        var result = await data.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);
        var flags = result.Metrics.FrameIsRecovery;
        var positions = result.Metrics.FrameFocuserPositions;

        Assert.That(flags, Is.Not.Null, "N>0 with enough positions => a recovery flag list");
        Assert.That(flags.Count, Is.EqualTo(9));
        var recovery = new HashSet<int> { HyperbolaP0 - 400, HyperbolaP0 - 300, HyperbolaP0 + 300, HyperbolaP0 + 400 };
        Assert.Multiple(() => {
            for (var i = 0; i < flags.Count; i++) {
                Assert.That(flags[i], Is.EqualTo(recovery.Contains(positions[i])),
                    $"frame at position {positions[i]} recovery flag");
            }
            Assert.That(flags.Count(f => f), Is.EqualTo(4), "exactly 2-per-side extreme positions are recovery");
        });
    }

    [Test]
    public async Task EvaluateAndFitAsync_RecoverySteps_InflatesRecoveryErrorY_IncludingZeroScatterPosition() {
        // Interior (non-recovery) positions carry per-frame stdev 0.05; the smallest recovery position (p0-400) is a
        // SINGLE frame with per-frame HFRStdDev 0. AverageMeasurement pools that lone frame with no VALID variance
        // contribution (validVarianceCount == 0 since HFRStdDev is not > 0), so its pooled Stdev is NaN, which
        // SafeDisplayError maps to display error 0. refErr = median(non-recovery display errors) = 0.05, so even the
        // zero-scatter recovery position is down-weighted: ErrorY = inflation * max(0, refErr) = inflation * refErr.
        var zeroScatterPos = HyperbolaP0 - 400;
        var detect = HyperbolaDetectWithStdev(pos => pos == zeroScatterPos ? 0.0 : 0.05);
        var data = new RunEvaluationData("recovery-inflate", NineFrames(), detect, NewAlglib(), DefaultFitConfig()) {
            RecoveryStepsPerSide = 2
        };
        var result = await data.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);

        const double refErr = 0.05;      // median of the interior positions' display errors
        const double interiorErrorY = 0.05;
        var recoveryPositions = new[] { HyperbolaP0 - 400, HyperbolaP0 - 300, HyperbolaP0 + 300, HyperbolaP0 + 400 };
        var interiorPositions = new[] { HyperbolaP0 - 200, HyperbolaP0 - 100, HyperbolaP0, HyperbolaP0 + 100, HyperbolaP0 + 200 };

        Assert.Multiple(() => {
            Assert.That(result.Points.Count, Is.EqualTo(9), "weighted fit keeps recovery points (down-weighted)");
            foreach (var pos in interiorPositions) {
                Assert.That(ErrorYAt(result, pos), Is.EqualTo(interiorErrorY).Within(1e-9),
                    "non-recovery point ErrorY is unchanged from the baseline scatter");
            }
            foreach (var pos in recoveryPositions) {
                Assert.That(ErrorYAt(result, pos), Is.EqualTo(RunEvaluationData.RecoveryErrorInflation * refErr).Within(1e-9),
                    $"recovery point at {pos} is down-weighted (ErrorY = inflation * max(ownStdev, refErr))");
                Assert.That(ErrorYAt(result, pos), Is.GreaterThan(interiorErrorY), "recovery ErrorY exceeds the reference");
            }
            // The zero-scatter recovery position is STILL down-weighted (0 * inflation would be inert => must use refErr).
            Assert.That(ErrorYAt(result, zeroScatterPos), Is.EqualTo(RunEvaluationData.RecoveryErrorInflation * refErr).Within(1e-9));
            Assert.That(ErrorYAt(result, zeroScatterPos), Is.GreaterThan(0.0), "zero-scatter recovery point is not weightless");
        });
    }

    [Test]
    public async Task EvaluateAndFitAsync_TooFewPositions_CollapsesToNoRecovery() {
        // 4 distinct positions with a large N: perSide = min(N, (4-3)/2) = 0 => no recovery (>=3 anchors preserved).
        var frames = FramesAt(new[] { HyperbolaP0 - 300, HyperbolaP0 - 100, HyperbolaP0 + 100, HyperbolaP0 + 300 });
        var data = new RunEvaluationData("too-few", frames, HyperbolaDetect(), NewAlglib(), DefaultFitConfig()) {
            RecoveryStepsPerSide = 5
        };
        var result = await data.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.Metrics.FrameIsRecovery, Is.Null, "cap collapses perSide to 0 => no recovery");
            Assert.That(result.NonRecoveryPooledPointCount, Is.EqualTo(result.PooledPointCount),
                "no recovery => non-recovery count equals the pooled count");
            Assert.That(result.PooledPointCount, Is.EqualTo(4), "all 4 positions still feed the fit");
        });
    }

    [Test]
    public async Task EvaluateAndFitAsync_CapLimitsPerSideToPositiveValue() {
        // 6 distinct positions with a large N: perSide = min(5, (6-3)/2) = min(5, 1) = 1 => exactly the outermost ONE
        // position per side is recovery (2 recovery positions), leaving 4 interior anchors. Exercises the cap between
        // its collapse-to-0 boundary and the requested N.
        var positions = new[] { HyperbolaP0 - 250, HyperbolaP0 - 150, HyperbolaP0 - 50, HyperbolaP0 + 50, HyperbolaP0 + 150, HyperbolaP0 + 250 };
        var data = new RunEvaluationData("cap", FramesAt(positions), HyperbolaDetect(), NewAlglib(), DefaultFitConfig()) {
            RecoveryStepsPerSide = 5
        };
        var result = await data.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);
        var flags = result.Metrics.FrameIsRecovery;
        var framePositions = result.Metrics.FrameFocuserPositions;

        var recovery = new HashSet<int> { HyperbolaP0 - 250, HyperbolaP0 + 250 }; // only the outermost 1 per side
        Assert.Multiple(() => {
            Assert.That(flags, Is.Not.Null);
            Assert.That(flags.Count, Is.EqualTo(6));
            for (var i = 0; i < flags.Count; i++) {
                Assert.That(flags[i], Is.EqualTo(recovery.Contains(framePositions[i])),
                    $"frame at position {framePositions[i]} recovery flag");
            }
            Assert.That(flags.Count(f => f), Is.EqualTo(2), "perSide capped to 1 => exactly 2 recovery positions");
            Assert.That(result.PooledPointCount, Is.EqualTo(6), "weighted fit keeps all positions (recovery down-weighted)");
            Assert.That(result.NonRecoveryPooledPointCount, Is.EqualTo(4), "4 interior anchors are non-recovery");
        });
    }

    [Test]
    public async Task EvaluateAndFitAsync_DownWeightingKeepsMinimumNearTruth_DespiteCorruptedWings() {
        // The feature's raison d'être: the interior positions lie on a hyperbola centered at the TRUE minimum
        // (HyperbolaP0), but the outermost (recovery) positions are CORRUPTED onto a hyperbola with a false center —
        // off-curve points that, at full weight, drag the fitted minimum away from truth. With recovery ON the
        // corrupted wings are down-weighted 10×, so the clean interior anchors keep the minimum near truth; with
        // recovery OFF (same corrupted points, full weight) the minimum is dragged noticeably away.
        const int falseCenter = HyperbolaP0 + 600;
        var recovery = new HashSet<int> { HyperbolaP0 - 400, HyperbolaP0 - 300, HyperbolaP0 + 300, HyperbolaP0 + 400 };
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect = (image, p, token) => {
            var pos = (int)image;
            var hfr = recovery.Contains(pos) ? GentleHfr(pos, falseCenter) : GentleHfr(pos, HyperbolaP0);
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = hfr,
                HFRStdDev = 0.05,
                StarCount = 50,
                StarCenters = Array.Empty<(double X, double Y)>()
            });
        };

        var runOn = new RunEvaluationData("down-on", NineFrames(), detect, NewAlglib(), DefaultFitConfig()) {
            RecoveryStepsPerSide = 2
        };
        var runOff = new RunEvaluationData("down-off", NineFrames(), detect, NewAlglib(), DefaultFitConfig()) {
            RecoveryStepsPerSide = 0
        };
        var onResult = await runOn.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);
        var offResult = await runOff.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);

        var onBest = onResult.Metrics.BestFocusPosition;
        var offBest = offResult.Metrics.BestFocusPosition;
        Assert.Multiple(() => {
            Assert.That(double.IsFinite(onBest), Is.True, "recovery-ON fit must produce a finite minimum");
            Assert.That(double.IsFinite(offBest), Is.True, "recovery-OFF fit must produce a finite minimum");
            // Recovery ON keeps the minimum near the true center despite the corrupted wings.
            Assert.That(Math.Abs(onBest - HyperbolaP0), Is.LessThan(100.0),
                "down-weighted wings must not drag the minimum far from truth");
            // Recovery OFF drags the minimum noticeably away from truth (direction depends on the corruption geometry).
            Assert.That(Math.Abs(offBest - HyperbolaP0), Is.GreaterThan(100.0),
                "full-weight corrupted wings drag the minimum away from truth");
            Assert.That(Math.Abs(onBest - HyperbolaP0), Is.LessThan(Math.Abs(offBest - HyperbolaP0)),
                "recovery keeps the minimum strictly closer to truth than the full-weight fit");
        });
    }

    [Test]
    public async Task EvaluateAndFitAsync_DuplicateFramesAtRecoveryPosition_AllFlagged() {
        // Two frames at the smallest position (a recovery position) => BOTH frames are flagged (recovery is by
        // distinct position). 10 frames, 9 distinct positions.
        var frames = NineFrames();
        frames.Add(new RunFrame { FrameId = "pos_dup", FocuserPosition = HyperbolaP0 - 400, Image = HyperbolaP0 - 400 });
        var data = new RunEvaluationData("dup", frames, HyperbolaDetect(), NewAlglib(), DefaultFitConfig()) {
            RecoveryStepsPerSide = 2
        };
        var result = await data.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);
        var flags = result.Metrics.FrameIsRecovery;
        var positions = result.Metrics.FrameFocuserPositions;

        Assert.Multiple(() => {
            Assert.That(flags, Is.Not.Null);
            Assert.That(flags.Count, Is.EqualTo(10), "per-frame flags include the duplicate frame");
            var atSmallest = Enumerable.Range(0, flags.Count).Where(i => positions[i] == HyperbolaP0 - 400).ToList();
            Assert.That(atSmallest.Count, Is.EqualTo(2), "two frames sit at the smallest position");
            Assert.That(atSmallest.All(i => flags[i]), Is.True, "all frames at a recovery position are flagged");
        });
    }

    [Test]
    public async Task EvaluateAndFitAsync_UnweightedFit_ExcludesRecoveryFromPoints_ButKeepsFlags() {
        // With UseWeights=false the fitter ignores ErrorY, so inflation cannot down-weight — recovery positions are
        // EXCLUDED from the fit points entirely. They still populate the per-frame FrameIsRecovery flags, and
        // NonRecoveryPooledPointCount stays consistent with the excluded set.
        var data = new RunEvaluationData("unweighted", NineFrames(), HyperbolaDetect(), NewAlglib(), UnweightedFitConfig()) {
            RecoveryStepsPerSide = 2
        };
        var result = await data.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);

        var recovery = new HashSet<int> { HyperbolaP0 - 400, HyperbolaP0 - 300, HyperbolaP0 + 300, HyperbolaP0 + 400 };
        Assert.Multiple(() => {
            Assert.That(result.PooledPointCount, Is.EqualTo(5), "the 4 recovery positions are excluded from the fit");
            Assert.That(result.NonRecoveryPooledPointCount, Is.EqualTo(5), "all fitted points are non-recovery");
            Assert.That(result.Points.Any(pt => recovery.Contains((int)Math.Round(pt.X))), Is.False,
                "no recovery position appears in the fit points");
            // Metrics still tag every frame (only the FIT omits the recovery positions).
            Assert.That(result.Metrics.FrameIsRecovery, Is.Not.Null);
            Assert.That(result.Metrics.FrameIsRecovery.Count(f => f), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task EvaluateAndFitAsync_RecoveryOff_IsByteIdenticalBaseline() {
        // Regression guard: RecoveryStepsPerSide == 0 (default) => FrameIsRecovery null, NonRecoveryPooledPointCount ==
        // PooledPointCount, and the interior (non-recovery) points' ErrorY are IDENTICAL to a run with N=2 (the
        // recovery feature only perturbs the extremes; it never touches the baseline points).
        var detect = HyperbolaDetectWithStdev(_ => 0.05);
        var baseline = new RunEvaluationData("baseline", NineFrames(), detect, NewAlglib(), DefaultFitConfig());
        var withRecovery = new RunEvaluationData("with-recovery", NineFrames(), detect, NewAlglib(), DefaultFitConfig()) {
            RecoveryStepsPerSide = 2
        };
        var baseResult = await baseline.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);
        var recResult = await withRecovery.EvaluateAndFitAsync(new StarDetectorParams(), CancellationToken.None);

        var interiorPositions = new[] { HyperbolaP0 - 200, HyperbolaP0 - 100, HyperbolaP0, HyperbolaP0 + 100, HyperbolaP0 + 200 };
        Assert.Multiple(() => {
            Assert.That(baseResult.Metrics.FrameIsRecovery, Is.Null, "feature off => FrameIsRecovery is null (not an all-false list)");
            Assert.That(baseResult.NonRecoveryPooledPointCount, Is.EqualTo(baseResult.PooledPointCount),
                "feature off => non-recovery count equals the pooled count");
            Assert.That(baseResult.Points.Count, Is.EqualTo(9));
            Assert.That(baseResult.Points.All(pt => Math.Abs(pt.ErrorY - 0.05) < 1e-9), Is.True,
                "baseline points carry the raw display scatter");
            // The interior points are byte-identical between the baseline and the recovery run.
            foreach (var pos in interiorPositions) {
                Assert.That(ErrorYAt(recResult, pos), Is.EqualTo(ErrorYAt(baseResult, pos)).Within(1e-12),
                    "recovery must not perturb the interior (non-recovery) fit points");
            }
        });
    }

    // Synchronous IProgress so reports are captured on the calling thread (no SynchronizationContext marshaling).
    private sealed class ImmediateProgress<T> : IProgress<T> {
        private readonly Action<T> onReport;
        public ImmediateProgress(Action<T> onReport) => this.onReport = onReport;
        public void Report(T value) => onReport(value);
    }
}
