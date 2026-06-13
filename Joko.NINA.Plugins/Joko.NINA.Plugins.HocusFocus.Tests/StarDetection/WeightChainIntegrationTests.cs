#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// End-to-end checks that WeightRegularization releases the F5a pin at the weight layer, plus
    /// measured damage bounds for the fit layer. Investigation on this noise-free sweep showed the
    /// fitted-minimum damage from one max-leverage wing outlier saturates (~56-61 steps here) almost
    /// independently of its weight (5× capped or 1000× raw): a four-parameter hyperbola that
    /// interpolates one wing point lands in nearly the same place either way. Regularization's
    /// guarantee is therefore the weight-ratio contract itself, not curve rescue — and Huber IRLS
    /// self-masks on any point whose capped weight ratio exceeds ~1.25× (its residual is near zero
    /// at the fit it dominated), so no recovery assertion is made for the degenerate-σ case.
    /// </summary>
    [TestFixture]
    public class WeightChainIntegrationTests {
        private const double TrueX0 = 5000.0;

        private IAlglibAPI alglibAPI;

        [SetUp]
        public void Setup() {
            alglibAPI = new AlglibAPI();
        }

        // y = a/b·√((x−x0)² + b²) + y0 — the Symmetric hyperbola, sampled at 11 positions.
        private static List<ScatterErrorPoint> SyntheticSweep(double x0) {
            const double a = 1.0, b = 200.0, y0 = 1.0;
            var points = new List<ScatterErrorPoint>();
            for (int i = 0; i <= 10; ++i) {
                double x = x0 - 500 + i * 100;
                double y = a / b * Math.Sqrt((x - x0) * (x - x0) + b * b) + y0;
                points.Add(new ScatterErrorPoint(x, y, 0, 0.3));
            }
            return points;
        }

        [Test]
        public void Regularize_DegenerateSigmaOutlier_WeightRatioCappedAtFive() {
            // The F5a scenario: a wing point displaced upward whose σ collapsed (e.g. 2 near-identical
            // HFR stars → MAD ≈ 0). The old chain gave it weight 1/0.001 = 1000 vs typical 3.3; the
            // regularized copy floors σ at 0.2·median, capping the weight ratio at exactly 5×.
            var points = SyntheticSweep(TrueX0);
            points[0] = new ScatterErrorPoint(points[0].X, points[0].Y + 1.5, 0, 0.001);

            var regularized = WeightRegularization.Regularize(points);

            var maxWeightRatio = regularized.Max(p => 1.0 / p.ErrorY) / (1.0 / 0.3);
            Assert.Multiple(() => {
                Assert.That(regularized[0].ErrorY, Is.EqualTo(0.06).Within(1e-12), "degenerate σ must be floored at 0.2·median");
                Assert.That(maxWeightRatio, Is.EqualTo(5.0).Within(1e-12), "no point may exceed 5× the median weight");
            });
        }

        [Test]
        public void WeightedFit_HealthyNoiseFreeSweep_RecoversTrueMinimum() {
            var fit = HyperbolicFittingAlglib.Create(alglibAPI, WeightRegularization.Regularize(SyntheticSweep(TrueX0)), useWeights: true);
            Assert.That(fit.Solve(), Is.True);
            Assert.That(Math.Abs(fit.Minimum.X - TrueX0), Is.LessThan(0.5), "noise-free healthy sweep must recover x0 essentially exactly");
        }

        [Test]
        public void WeightedFit_DisplacedPointWithHealthySigma_HuberIrlsRecovers() {
            // Same displaced wing point but with a healthy σ (no weight advantage): Huber IRLS detects
            // and crushes it (measured shift ~1e-4 vs ~36 steps without IRLS). This pins the IRLS
            // machinery itself, isolating the self-masking failure below to the weight ratio.
            var points = SyntheticSweep(TrueX0);
            points[0] = new ScatterErrorPoint(points[0].X, points[0].Y + 1.5, 0, 0.3);

            var fit = HyperbolicFittingAlglib.Create(alglibAPI, WeightRegularization.Regularize(points), useWeights: true);
            fit.HuberIrlsEnabled = true;
            Assert.That(fit.Solve(), Is.True);
            Assert.That(Math.Abs(fit.Minimum.X - TrueX0), Is.LessThan(5.0));
        }

        [Test]
        public void WeightedFit_RegularizedDegenerateOutlier_DamageBounded() {
            // Damage bound, not a recovery claim: with the weight capped at 5×, a +5σ displaced
            // max-leverage wing point still drags the noise-free minimum ~61 steps (it self-masks from
            // Huber IRLS — its residual is ≈0 at the fit it dominates, so IRLS downweights the clean
            // neighbors instead). The bound has ~23% headroom over the measured 60.7; the unregularized
            // 1000× path measures ~62 with IRLS — the weight cap, not curve rescue, is the contract.
            var points = SyntheticSweep(TrueX0);
            points[0] = new ScatterErrorPoint(points[0].X, points[0].Y + 1.5, 0, 0.001);

            var fit = HyperbolicFittingAlglib.Create(alglibAPI, WeightRegularization.Regularize(points), useWeights: true);
            fit.HuberIrlsEnabled = true;
            Assert.That(fit.Solve(), Is.True);
            Assert.That(Math.Abs(fit.Minimum.X - TrueX0), Is.LessThan(75.0));
        }
    }
}
