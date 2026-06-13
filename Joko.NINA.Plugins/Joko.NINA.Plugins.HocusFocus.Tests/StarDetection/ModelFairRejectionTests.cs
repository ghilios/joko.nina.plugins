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
    /// Model-fair rejection: only points flagged as outliers by EVERY solved candidate model (the consensus /
    /// intersection) are removed, so a tilt-revealing wing point (an outlier only under Symmetric) is kept while
    /// a gross bad frame (an outlier under every model) is removed.
    /// </summary>
    [TestFixture]
    public class ModelFairRejectionTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() => alglibAPI = new AlglibAPI();

        // A clean, strongly tilted curve with tight error bars (mirrors the "uneven" run): the Symmetric model
        // would flag wing points, but the asymmetric models flag none → consensus is empty → nothing removed.
        [Test]
        public void Consensus_KeepsTiltRevealingPoints_NoSpuriousRemoval() {
            var points = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.02, xStart: 760, xStep: 32, count: 15, errorY: 0.04);
            var stepSize = InferStep(points);

            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.90,
                out var bestFit, out var rejects);

            Assert.That(rejects, Is.Empty, "consensus must remove no genuine points from a clean tilted curve");
            Assert.That(bestFit, Is.Not.Null);
        }

        // A gross bad frame is an outlier under every model → consensus removes it (capped by maxOutlierRejections).
        [Test]
        public void Consensus_RemovesGrossOutlier_FlaggedByAllModels() {
            var clean = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.02, xStart: 760, xStep: 32, count: 15, errorY: 0.04);
            var stepSize = InferStep(clean);
            var badX = clean[5].X;
            var withOutlier = clean.Select(p => p.X == badX
                ? new ScatterErrorPoint(p.X, p.Y + 4.0, 0, p.ErrorY) : p).ToList();

            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, withOutlier, stepSize, useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.90,
                out var bestFit, out var rejects);

            Assert.That(bestFit, Is.Not.Null);
            Assert.That(rejects.Select(p => p.X), Does.Contain(badX), "the gross bad frame must be the consensus outlier");
            Assert.That(rejects.Count, Is.EqualTo(1), "only the one bad frame is unanimous; tilt wings are kept");
        }

        // maxOutlierRejections = 0 ⇒ no rejection regardless of data.
        [Test]
        public void Consensus_ZeroBudget_RemovesNothing() {
            var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, xStart: 880, xStep: 12, count: 21, errorY: 0.1);
            var withOutlier = clean.Select((p, i) => i == 8 ? new ScatterErrorPoint(p.X, p.Y + 4.0, 0, p.ErrorY) : p).ToList();
            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, withOutlier, InferStep(clean), useWeights: true, maxOutlierRejections: 0, rejectionConfidence: 0.90,
                out _, out var rejects);
            Assert.That(rejects, Is.Empty);
        }

        private static int InferStep(List<ScatterErrorPoint> points) {
            var xs = points.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
            var diffs = new List<double>();
            for (int i = 1; i < xs.Count; ++i) diffs.Add(xs[i] - xs[i - 1]);
            diffs.Sort();
            return diffs.Count == 0 ? 25 : Math.Max(1, (int)Math.Round(diffs[diffs.Count / 2]));
        }
    }
}
