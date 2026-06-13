using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Tests for the Hybrid "best fit" selection helper <see cref="AlglibHyperbolicFitting.SelectBestModel"/>:
    /// it must pick the concrete model with the least expected best-focus error (σ(focus) primary, leave-one-out
    /// fallback), return an already-solved concrete fit, and never throw or recurse through Hybrid — even on the
    /// degenerate real curves that previously blew up the asymmetric fits.
    /// </summary>
    [TestFixture]
    public class HybridModelSelectionTests {
        private IAlglibAPI alglibAPI;

        private static readonly HyperbolicFitModel[] Candidates = {
            HyperbolicFitModel.Symmetric,
            HyperbolicFitModel.UnevenBlend,
            HyperbolicFitModel.TiltedHyperbola,
            HyperbolicFitModel.SmoothBlend,
        };

        // UC108, n=21: a monotonic ramp (no real vertex in range) — drives every asymmetric model degenerate, so
        // σ(focus) is NaN for all and selection must fall back to LOO (or the Tilted preservation path).
        private static readonly double[][] MonotonicRamp_NoVertexInRange = {
            new[] { 0.0, 7.839050, 0.638013 },
            new[] { 1.0, 7.961625, 0.671539 },
            new[] { 11.0, 8.303164, 0.640615 },
            new[] { 21.0, 8.599752, 0.578047 },
            new[] { 31.0, 8.802762, 0.557605 },
            new[] { 41.0, 8.997434, 0.486232 },
            new[] { 51.0, 9.034380, 0.505478 },
            new[] { 61.0, 9.239328, 0.616678 },
            new[] { 71.0, 9.397387, 0.556208 },
            new[] { 81.0, 9.454321, 0.552180 },
            new[] { 91.0, 9.491712, 0.563458 },
            new[] { 101.0, 9.532192, 0.550557 },
            new[] { 111.0, 9.544934, 0.593257 },
            new[] { 121.0, 9.556212, 0.608118 },
            new[] { 131.0, 9.594925, 0.612019 },
            new[] { 141.0, 9.625054, 0.548355 },
            new[] { 151.0, 9.522130, 0.547106 },
            new[] { 161.0, 9.570066, 0.570344 },
            new[] { 171.0, 9.628579, 0.570173 },
            new[] { 181.0, 9.627635, 0.587291 },
            new[] { 191.0, 9.519182, 0.573350 },
        };

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        // The gate restricts the winner to the asymmetric models when the tilt is significant; among those the
        // winner has the lowest σ(focus). This data is a genuine gate discriminator: a mild tilt with deterministic
        // noise where Symmetric actually has the LOWEST σ(focus) of all candidates — so WITHOUT the gate Symmetric
        // would win on variance; the gate excludes it (the asymmetry is significant) and the lowest-σ asymmetric
        // model wins instead. (The dedicated "is the winner asymmetric" check is Gate_ReportsAsymmetric_* in
        // ModelFairRejectionTests; this test additionally pins the within-asymmetric σ ranking.)
        [Test]
        public void SelectBestModel_PicksLowestExpectedError_AmongJustifiedAsymmetricModels() {
            var points = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.003, xStart: 760, xStep: 16, count: 21, errorY: 0.04,
                noiseSigma: 0.04, seed: 0);
            var stepSize = InferStep(points);

            var asymmetric = new[] { HyperbolicFitModel.UnevenBlend, HyperbolicFitModel.TiltedHyperbola, HyperbolicFitModel.SmoothBlend };
            double symSigma = double.NaN;
            var asymSigmas = new List<double>();
            foreach (var model in Candidates) {
                var f = AlglibHyperbolicFitting.Create(alglibAPI, model, points, stepSize, useWeights: true);
                if (f.Solve() && !double.IsNaN(f.MinimumStdError) && !double.IsInfinity(f.MinimumStdError)) {
                    if (model == HyperbolicFitModel.Symmetric) symSigma = f.MinimumStdError;
                    else asymSigmas.Add(f.MinimumStdError);
                }
            }
            Assert.That(asymSigmas, Is.Not.Empty);
            var minAsymSigma = asymSigmas.Min();
            // Precondition: this data must be a real gate discriminator — Symmetric has the global-min σ, so the
            // gate (not variance ranking) is what excludes it. If this fails, the test no longer proves the gate.
            Assert.That(symSigma, Is.LessThan(minAsymSigma),
                "test data must have Symmetric as the global-min σ(focus) so the gate's exclusion is what flips the winner");

            var chosen = AlglibHyperbolicFitting.SelectBestModel(alglibAPI, points, stepSize, useWeights: true, out var bestFit);

            Assert.Multiple(() => {
                Assert.That(bestFit, Is.Not.Null);
                Assert.That(asymmetric, Does.Contain(chosen),
                    "a significant tilt must yield an asymmetric winner even though Symmetric has the lower σ(focus)");
                Assert.That(bestFit.MinimumStdError, Is.EqualTo(minAsymSigma).Within(1e-6),
                    "winner has the lowest σ(focus) among the justified asymmetric models");
            });
        }

        // Case 2: when every σ(focus)-capable candidate is degenerate (σ NaN), selection falls back without throwing,
        // returns a concrete model (never Hybrid), and the resulting minimum is finite (regression vs the 1e14 blowup).
        [Test]
        public void SelectBestModel_FallsBackWhenSigmaUnavailable() {
            var points = ToPoints(MonotonicRamp_NoVertexInRange);
            var stepSize = InferStep(points);

            HyperbolicFitModel chosen = HyperbolicFitModel.Hybrid;
            AlglibHyperbolicFitting bestFit = null;
            Assert.DoesNotThrow(() => {
                chosen = AlglibHyperbolicFitting.SelectBestModel(alglibAPI, points, stepSize, useWeights: true, out bestFit);
            });

            Assert.Multiple(() => {
                Assert.That(chosen, Is.Not.EqualTo(HyperbolicFitModel.Hybrid), "Must resolve to a concrete model");
                Assert.That(Candidates, Does.Contain(chosen));
                if (bestFit != null) {
                    var x = bestFit.Minimum.X;
                    Assert.That(double.IsNaN(x), Is.False, "Winner minimum must not be NaN");
                    Assert.That(double.IsInfinity(x), Is.False, "Winner minimum must not be Infinity (1e14 regression)");
                }
            });
        }

        // Case 5: with fewer than 5 points, LOO is NaN and σ(focus) is typically unavailable too; selection must still
        // resolve a concrete model via the χ²/R² tiebreak without throwing.
        [Test]
        public void SelectBestModel_FewerThanFivePoints_StillResolves() {
            var points = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, xStart: 970, xStep: 20, count: 4, errorY: 0.1);
            var stepSize = InferStep(points);

            HyperbolicFitModel chosen = HyperbolicFitModel.Hybrid;
            AlglibHyperbolicFitting bestFit = null;
            Assert.DoesNotThrow(() => {
                chosen = AlglibHyperbolicFitting.SelectBestModel(alglibAPI, points, stepSize, useWeights: true, out bestFit);
            });

            Assert.Multiple(() => {
                Assert.That(chosen, Is.Not.EqualTo(HyperbolicFitModel.Hybrid));
                Assert.That(Candidates, Does.Contain(chosen));
            });
        }

        // Per-model rejection: a gross outlier is removed before the model competes, and the winning (cleaned) fit
        // localizes focus closer to truth than the outlier-contaminated fit (max=0).
        [Test]
        public void SelectBestModel_RejectsGrossOutlier_AndExcludesItFromWinningFit() {
            const double trueX0 = 1000;
            var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: trueX0, y0: 2.0, a: 6.0, b: 12.0, xStart: 910, xStep: 15, count: 13, errorY: 0.1);
            var stepSize = InferStep(clean);

            var outlierX = clean[9].X;
            var withOutlier = clean.Select(p => p.X == outlierX
                ? new ScatterErrorPoint(p.X, p.Y + 3.0, 0, p.ErrorY) : p).ToList();

            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, withOutlier, stepSize, useWeights: true, maxOutlierRejections: 0, rejectionConfidence: 0.95,
                out var fitNoReject, out var rejectsNoReject);
            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, withOutlier, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
                out var bestFit, out var rejects);

            Assert.Multiple(() => {
                Assert.That(rejectsNoReject, Is.Empty, "max=0 must reject nothing");
                Assert.That(rejects.Count, Is.EqualTo(1), "the gross outlier must be rejected");
                Assert.That(rejects[0].X, Is.EqualTo(outlierX), "the rejected point must be the injected outlier");
                Assert.That(Math.Abs(bestFit.Minimum.X - trueX0),
                    Is.LessThan(Math.Abs(fitNoReject.Minimum.X - trueX0)),
                    "cleaned fit must localize focus closer to truth than the contaminated fit");
                Assert.That(Math.Abs(bestFit.Minimum.X - trueX0), Is.LessThan(stepSize));
            });
        }

        // The no-rejection overload (and maxOutlierRejections = 0) must reproduce the legacy behavior exactly.
        [Test]
        public void SelectBestModel_MaxRejectionsZero_MatchesLegacyOverload() {
            var points = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.012, xStart: 880, xStep: 15, count: 17, errorY: 0.05);
            var stepSize = InferStep(points);

            var legacy = AlglibHyperbolicFitting.SelectBestModel(alglibAPI, points, stepSize, useWeights: true, out var legacyFit);
            var modern = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 0, rejectionConfidence: 0.95,
                out var modernFit, out var rejects);

            Assert.Multiple(() => {
                Assert.That(rejects, Is.Empty);
                Assert.That(modern, Is.EqualTo(legacy), "max=0 must match the legacy (no-rejection) overload");
                Assert.That(modernFit.Minimum.X, Is.EqualTo(legacyFit.Minimum.X).Within(1e-6));
            });
        }

        // Rejection is capped by maxOutlierRejections.
        [Test]
        public void SelectBestModel_RespectsMaxOutlierRejectionCap() {
            var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, xStart: 880, xStep: 12, count: 21, errorY: 0.1);
            var stepSize = InferStep(clean);
            var x1 = clean[6].X;
            var x2 = clean[14].X;
            var withTwo = clean.Select(p =>
                p.X == x1 || p.X == x2 ? new ScatterErrorPoint(p.X, p.Y + 3.0, 0, p.ErrorY) : p).ToList();

            AlglibHyperbolicFitting.SelectBestModel(alglibAPI, withTwo, stepSize, useWeights: true, 1, 0.95, out _, out var oneReject);
            AlglibHyperbolicFitting.SelectBestModel(alglibAPI, withTwo, stepSize, useWeights: true, 2, 0.95, out _, out var twoReject);

            Assert.Multiple(() => {
                Assert.That(oneReject.Count, Is.EqualTo(1), "cap of 1 must stop after one rejection");
                Assert.That(twoReject.Count, Is.EqualTo(2), "cap of 2 must remove both injected outliers");
                Assert.That(twoReject.Select(p => p.X), Is.EquivalentTo(new[] { x1, x2 }));
            });
        }

        // Proves the rejected point is a genuine CROSS-MODEL unanimous outlier: a single gross bad frame is removed,
        // and it is independently flagged by BOTH a symmetric and an asymmetric fit on the full set. (The
        // complementary half — that a point flagged by only SOME models, e.g. a tilt wing, is NOT removed — is
        // covered by ModelFairRejectionTests.Consensus_KeepsTiltRevealingPoints_NoSpuriousRemoval.)
        [Test]
        public void SelectBestModel_RejectionIsCrossModelConsensus() {
            var clean = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.012, xStart: 880, xStep: 15, count: 17, errorY: 0.1);
            var stepSize = InferStep(clean);
            var outlierX = clean[5].X; // left-wing point, X = 880 + 5*15 = 955
            var withOutlier = clean.Select(p => p.X == outlierX
                ? new ScatterErrorPoint(p.X, p.Y + 3.0, 0, p.ErrorY) : p).ToList();

            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, withOutlier, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
                out _, out var rejects);

            Assert.That(rejects.Count, Is.EqualTo(1));
            Assert.That(rejects[0].X, Is.EqualTo(outlierX), "the unanimous gross outlier is the consensus rejection");

            // It is flagged independently by both a symmetric and an asymmetric fit on the full set (unanimous).
            foreach (var model in new[] { HyperbolicFitModel.Symmetric, HyperbolicFitModel.TiltedHyperbola }) {
                var f = AlglibHyperbolicFitting.Create(alglibAPI, model, withOutlier, stepSize, useWeights: true);
                Assume.That(f.Solve(), Is.True);
                var flagged = MathUtility.RejectionTest(withOutlier, f.Fitting, 0.95,
                    AlglibHyperbolicFitting.BuildResidualWeights(withOutlier, true));
                Assert.That(flagged?.X, Is.EqualTo(outlierX), $"{model} also flags the gross outlier");
            }
        }

        private static List<ScatterErrorPoint> ToPoints(double[][] data) =>
            data.Select(r => new ScatterErrorPoint(r[0], r[1], 0, r[2])).ToList();

        private static int InferStep(List<ScatterErrorPoint> points) {
            var xs = points.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
            var diffs = new List<double>();
            for (int i = 1; i < xs.Count; ++i) {
                diffs.Add(xs[i] - xs[i - 1]);
            }
            diffs.Sort();
            return diffs.Count == 0 ? 25 : Math.Max(1, (int)Math.Round(diffs[diffs.Count / 2]));
        }
    }
}
