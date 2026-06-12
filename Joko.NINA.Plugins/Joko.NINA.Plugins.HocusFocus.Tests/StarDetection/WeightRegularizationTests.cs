#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;
using OxyPlot.Series;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class WeightRegularizationTests {

        private static List<ScatterErrorPoint> Points(params double[] sigmas) {
            return sigmas.Select((s, i) => new ScatterErrorPoint(100 * i, 2.0 + i, 0, s)).ToList();
        }

        [Test]
        public void Regularize_HealthySigmas_Unchanged() {
            var result = WeightRegularization.Regularize(Points(0.1, 0.2, 0.3, 0.4, 0.5));
            Assert.That(result.Select(p => p.ErrorY), Is.EqualTo(new[] { 0.1, 0.2, 0.3, 0.4, 0.5 }).Within(1e-12));
        }

        [Test]
        public void Regularize_DegenerateSigma_FlooredAtFractionOfMedian() {
            // Median of the positive σs is 0.3 → floor = 0.2 · 0.3 = 0.06. The old chain gave this
            // point weight 1/0.001 = 1000; now it is capped at 1/0.06 ≈ 16.7 (5× the median weight).
            var result = WeightRegularization.Regularize(Points(0.001, 0.2, 0.3, 0.4, 0.5));
            Assert.That(result[0].ErrorY, Is.EqualTo(0.06).Within(1e-12));
        }

        [Test]
        public void Regularize_ZeroNaNAndInfinity_GetMedianSigma() {
            // Unknown precision gets *average* weight (median σ), not maximum weight. Infinity must
            // not enter the median pool either: the pool here is {0.2, 0.3, 0.4, 0.5} (even count,
            // median 0.35) and all three degenerate σs map to it.
            var result = WeightRegularization.Regularize(Points(0.0, double.NaN, double.PositiveInfinity, 0.2, 0.3, 0.4, 0.5));
            Assert.Multiple(() => {
                Assert.That(result[0].ErrorY, Is.EqualTo(0.35).Within(1e-12));
                Assert.That(result[1].ErrorY, Is.EqualTo(0.35).Within(1e-12));
                Assert.That(result[2].ErrorY, Is.EqualTo(0.35).Within(1e-12));
            });
        }

        [Test]
        public void Regularize_AllDegenerate_FallsBackToUnweighted() {
            var result = WeightRegularization.Regularize(Points(0.0, double.NaN, -1.0));
            Assert.That(result.Select(p => p.ErrorY), Is.All.EqualTo(1.0));
        }

        [Test]
        public void Regularize_EmptyAndNull_ReturnsEmpty() {
            Assert.Multiple(() => {
                Assert.That(WeightRegularization.Regularize(new List<ScatterErrorPoint>()), Is.Empty);
                Assert.That(WeightRegularization.Regularize(null), Is.Empty);
            });
        }

        [Test]
        public void Regularize_PreservesXYAndErrorX() {
            var input = new List<ScatterErrorPoint> { new ScatterErrorPoint(123, 4.5, 6.7, 0.0) };
            var result = WeightRegularization.Regularize(input);
            Assert.Multiple(() => {
                Assert.That(result[0].X, Is.EqualTo(123));
                Assert.That(result[0].Y, Is.EqualTo(4.5));
                Assert.That(result[0].ErrorX, Is.EqualTo(6.7));
            });
        }

        [Test]
        public void Regularize_SinglePoint_KeepsOwnSigma() {
            // Median of one value is itself; the floor (0.2·σ) is below σ, so it passes through.
            var result = WeightRegularization.Regularize(Points(0.25));
            Assert.That(result[0].ErrorY, Is.EqualTo(0.25).Within(1e-12));
        }

        [Test]
        public void Regularize_AppliedTwice_IsIdempotent() {
            // Floored values land strictly below the median and unknown σ lands on it, so a second
            // pass recomputes the same median and changes nothing. Five production call sites make
            // accidental chaining plausible; idempotency is what makes that safe.
            var once = WeightRegularization.Regularize(Points(0.001, 0.0, double.NaN, 0.2, 0.3, 0.4, 0.5));
            var twice = WeightRegularization.Regularize(once);
            Assert.That(twice.Select(p => p.ErrorY), Is.EqualTo(once.Select(p => p.ErrorY)).Within(1e-15));
        }
    }
}
