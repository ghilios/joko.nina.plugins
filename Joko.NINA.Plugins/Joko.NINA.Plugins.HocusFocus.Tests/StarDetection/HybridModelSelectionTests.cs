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

        // Case 1: on a clean curve where σ(focus) is well-determined, the winner's σ(focus) is the smallest finite
        // σ(focus) across all concrete candidates (Tier 1), and the returned fit is a concrete, solved fit.
        [Test]
        public void SelectBestModel_PicksLowestExpectedError() {
            // A tilted hyperbola: the asymmetric models fit it tighter than Symmetric, so σ(focus) differs by model.
            var points = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.012, xStart: 880, xStep: 15, count: 17, errorY: 0.05);
            var stepSize = InferStep(points);

            // Independently fit every candidate and find the minimum finite σ(focus).
            var perModelSigma = new Dictionary<HyperbolicFitModel, double>();
            foreach (var model in Candidates) {
                var f = AlglibHyperbolicFitting.Create(alglibAPI, model, points, stepSize, useWeights: true);
                if (f.Solve()) {
                    perModelSigma[model] = f.MinimumStdError;
                }
            }
            var finiteSigmas = perModelSigma.Values.Where(s => !double.IsNaN(s) && !double.IsInfinity(s)).ToList();
            Assert.That(finiteSigmas, Is.Not.Empty, "Expected at least one candidate with a finite σ(focus)");
            var minFiniteSigma = finiteSigmas.Min();

            var chosen = AlglibHyperbolicFitting.SelectBestModel(alglibAPI, points, stepSize, useWeights: true, out var bestFit);

            Assert.Multiple(() => {
                Assert.That(Candidates, Does.Contain(chosen), "Chosen model must be a concrete candidate");
                Assert.That(bestFit, Is.Not.Null, "Winning fit must be returned");
                Assert.That(double.IsNaN(bestFit.MinimumStdError), Is.False, "Tier-1 winner must have a finite σ(focus)");
                Assert.That(bestFit.MinimumStdError, Is.EqualTo(minFiniteSigma).Within(1e-9),
                    "Winner's σ(focus) must equal the minimum finite σ(focus) across candidates");
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

        // The rejected points are exactly those the WINNING model's own residuals flag — proving rejection is
        // per-model (against the chosen fit), not shared from some other model's residuals.
        [Test]
        public void SelectBestModel_RejectionMatchesWinningModelResiduals() {
            var clean = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.012, xStart: 880, xStep: 15, count: 17, errorY: 0.1);
            var stepSize = InferStep(clean);
            var outlierX = clean[5].X;
            var withOutlier = clean.Select(p => p.X == outlierX
                ? new ScatterErrorPoint(p.X, p.Y + 3.0, 0, p.ErrorY) : p).ToList();

            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, withOutlier, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
                out var bestFit, out var rejects);

            Assert.That(rejects.Count, Is.EqualTo(1));
            var cleaned = withOutlier.Where(p => p.X != outlierX).ToList();

            var flaggedOnFull = MathUtility.RejectionTest(
                withOutlier, bestFit.Fitting, 0.95, AlglibHyperbolicFitting.BuildResidualWeights(withOutlier, true));
            var flaggedOnCleaned = MathUtility.RejectionTest(
                cleaned, bestFit.Fitting, 0.95, AlglibHyperbolicFitting.BuildResidualWeights(cleaned, true));

            Assert.Multiple(() => {
                Assert.That(flaggedOnFull, Is.Not.Null, "the winning model flags the outlier on the full set");
                Assert.That(flaggedOnFull.X, Is.EqualTo(outlierX));
                Assert.That(flaggedOnCleaned, Is.Null, "the winning model finds no outlier in the set it was cleaned to");
            });
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
