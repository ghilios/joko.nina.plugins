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

        // Determinism regression (replay race): SelectBestModel must be independent of the ORDER in which
        // measurement points are supplied. During replay points complete concurrently, so the engine can hand
        // them to the fit in any order; before the input was canonicalized, the order-sensitive alglib fit
        // (summation roundoff) + Grubbs rejection (first-of-ties MaxBy) could flip a borderline outlier and shift
        // the fitted minimum between otherwise-identical replays — observed as a sweep point intermittently
        // rejected and rendered at a neighbor's HFR on the AF replay chart.
        [Test]
        public void SelectBestModel_IsIndependentOfPointOrder() {
            var clean = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.02, xStart: 760, xStep: 32, count: 15, errorY: 0.04);
            // A borderline wing perturbation near the rejection threshold — the case most sensitive to fit order.
            var points = clean.Select((p, i) => i == 14 ? new ScatterErrorPoint(p.X, p.Y + 0.5, 0, p.ErrorY) : p).ToList();
            var stepSize = InferStep(points);

            HyperbolicFitModel SelectOn(IList<ScatterErrorPoint> pts, out double minX, out double minY, out List<int> rejX) {
                var model = AlglibHyperbolicFitting.SelectBestModel(
                    alglibAPI, pts, stepSize, useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.95,
                    out var fit, out var rejects);
                minX = fit?.Minimum.X ?? double.NaN;
                minY = fit?.Minimum.Y ?? double.NaN;
                rejX = rejects.Select(p => (int)Math.Round(p.X)).OrderBy(x => x).ToList();
                return model;
            }

            var baseModel = SelectOn(points, out var baseMinX, out var baseMinY, out var baseRej);

            // Several deterministic re-orderings (reverse + rotations) must all yield the identical decision and fit.
            var permutations = new[] {
                Enumerable.Reverse(points).ToList(),
                points.Skip(7).Concat(points.Take(7)).ToList(),
                points.Skip(3).Concat(points.Take(3)).ToList(),
            };
            foreach (var perm in permutations) {
                var model = SelectOn(perm, out var minX, out var minY, out var rejX);
                Assert.Multiple(() => {
                    Assert.That(model, Is.EqualTo(baseModel), "selected model must not depend on point order");
                    Assert.That(rejX, Is.EqualTo(baseRej), "rejected set must not depend on point order");
                    Assert.That(minX, Is.EqualTo(baseMinX), "fitted minimum X must not depend on point order");
                    Assert.That(minY, Is.EqualTo(baseMinY), "fitted minimum Y must not depend on point order");
                });
            }
        }

        private static int InferStep(List<ScatterErrorPoint> points) {
            var xs = points.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
            var diffs = new List<double>();
            for (int i = 1; i < xs.Count; ++i) diffs.Add(xs[i] - xs[i - 1]);
            diffs.Sort();
            return diffs.Count == 0 ? 25 : Math.Max(1, (int)Math.Round(diffs[diffs.Count / 2]));
        }

        private static readonly HyperbolicFitModel[] Asymmetric = {
            HyperbolicFitModel.UnevenBlend, HyperbolicFitModel.TiltedHyperbola, HyperbolicFitModel.SmoothBlend,
        };

        // Genuinely tilted curve (small s=0.003) with realistic deterministic noise (noiseSigma = errorY, seed=0).
        // This is a genuine gate discriminator: even though sigma(focus)_sym=0.058676 < sigma_min_asym=0.063878
        // (Symmetric would win WITHOUT the gate on sigma), the F-test fires strongly (F_tilt=43.85,
        // F_uneven=42.26, F_smooth=41.91 >> F_crit(1,16,0.95)=4.49) so the gate unlocks the asymmetric models
        // and TiltedHyperbola wins. Confirmed: without the gate (force allowed=allIndices) Symmetric wins —
        // verified by a sweep of 100 seeds showing sigma_sym < sigma_min_asym for ~75% of seeds.
        // Audited chi-squared: chi2_sym=92.27, chi2_tilt=24.67, chi2_uneven=25.34, chi2_smooth=25.49.
        [Test]
        public void Gate_ReportsAsymmetric_WhenAsymmetryIsSignificant() {
            var points = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.003,
                xStart: 760, xStep: 16, count: 21, errorY: 0.04,
                noiseSigma: 0.04, seed: 0);
            var chosen = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, InferStep(points), useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.90,
                out _, out _);
            Assert.That(Asymmetric, Does.Contain(chosen), "a significant tilt must be reported as an asymmetric model");
        }

        // Clean symmetric curve with realistic deterministic noise (noiseSigma = errorY, seed=42, 21 points).
        // No real asymmetry exists, so no asymmetric model achieves a significant F-test improvement over
        // Symmetric — gate stays closed — Symmetric wins by parsimony.
        // Comfortable margin verified over seeds 0–5:
        //   seed=0: F_tilt=0.58, F_uneven=0.65, F_smooth=0.61  (max F = 0.65; gap = 4.49 - 0.65 = 3.84)
        //   seed=1: F_tilt=0.00, F_uneven=0.00, F_smooth=0.00
        //   seed=2: F_tilt=0.10, F_uneven=0.10, F_smooth=0.10
        //   seed=3: F_tilt=0.00, F_uneven=-0.01, F_smooth=0.00
        //   seed=4: F_tilt=-0.10, F_uneven=-0.11, F_smooth=-0.11
        //   seed=5: F_tilt=2.84, F_uneven=2.73, F_smooth=2.77  (worst; gap = 4.49 - 2.84 = 1.65)
        //   F_crit(1,16,0.95) = 4.49; chosen=Symmetric for all seeds.
        [Test]
        public void Gate_ReportsSymmetric_WhenNoSignificantAsymmetry() {
            var points = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0,
                xStart: 760, xStep: 32, count: 21, errorY: 0.1,
                noiseSigma: 0.1, seed: 42);
            var chosen = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, InferStep(points), useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.90,
                out _, out _);
            Assert.That(chosen, Is.EqualTo(HyperbolicFitModel.Symmetric));
        }

    }
}
