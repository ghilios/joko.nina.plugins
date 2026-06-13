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
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Regression guard for Task 5 (parallelizing the validation-phase hyperbolic fitting): the new
    /// <c>maxDegreeOfParallelism</c> parameter on <see cref="AlglibHyperbolicFitting.SelectBestModel"/> and
    /// <see cref="AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError"/> is a pure speed change — the
    /// parallel path (knob 0 ⇒ auto ⇒ ProcessorCount) must produce results <b>bit-identical</b> to the
    /// sequential path (knob 1). Same winning model, same best-focus position, same rejected set, same
    /// leave-one-out value. Preserved by index-aligned result slots + an ordered post-loop reduction + a
    /// sequential tie-break that iterates in fixed candidate-model index order.
    /// </summary>
    [TestFixture]
    public class AlglibHyperbolicFittingParallelismTests {
        private IAlglibAPI alglibAPI;

        private static readonly HyperbolicFitModel[] Candidates = {
            HyperbolicFitModel.Symmetric,
            HyperbolicFitModel.UnevenBlend,
            HyperbolicFitModel.TiltedHyperbola,
            HyperbolicFitModel.SmoothBlend,
        };

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        // A clean tilted hyperbola — Tier 1 (finite σ(focus)) path. Reuses the shape from HybridModelSelectionTests.
        private List<ScatterErrorPoint> TiltedCurve() => SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
            x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.012, xStart: 880, xStep: 15, count: 17, errorY: 0.05);

        // The parallel (auto) selection must produce the identical winning model, the identical best-focus point,
        // and the identical rejected set as the sequential (degree 1) selection — including outlier rejection.
        [Test]
        public void SelectBestModel_ParallelMatchesSequential_WinningModelAndFitAndRejects() {
            var clean = TiltedCurve();
            var stepSize = InferStep(clean);
            // Inject a gross outlier so the per-model outlier rejection loop is exercised on every candidate.
            var outlierX = clean[5].X;
            var points = clean.Select(p => p.X == outlierX
                ? new ScatterErrorPoint(p.X, p.Y + 3.0, 0, p.ErrorY) : p).ToList();

            var sequentialModel = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true,
                maxOutlierRejections: 2, rejectionConfidence: 0.95,
                out var sequentialFit, out var sequentialRejects, maxDegreeOfParallelism: 1);
            var parallelModel = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true,
                maxOutlierRejections: 2, rejectionConfidence: 0.95,
                out var parallelFit, out var parallelRejects, maxDegreeOfParallelism: 0);

            Assert.Multiple(() => {
                Assert.That(parallelModel, Is.EqualTo(sequentialModel), "winning model must be identical");
                Assert.That(parallelFit, Is.Not.Null);
                // Bit-identical best-focus position (X and Y) — no tolerance.
                Assert.That(parallelFit.Minimum.X, Is.EqualTo(sequentialFit.Minimum.X), "best-focus X must be bit-identical");
                Assert.That(parallelFit.Minimum.Y, Is.EqualTo(sequentialFit.Minimum.Y), "best-focus Y must be bit-identical");
                Assert.That(parallelFit.MinimumStdError, Is.EqualTo(sequentialFit.MinimumStdError), "σ(focus) must be bit-identical");
                Assert.That(parallelFit.ReducedChiSquared, Is.EqualTo(sequentialFit.ReducedChiSquared));
                Assert.That(parallelFit.RSquared, Is.EqualTo(sequentialFit.RSquared));
                Assert.That(parallelRejects.Select(p => p.X), Is.EqualTo(sequentialRejects.Select(p => p.X)),
                    "rejected set must be identical (same points, same order)");
            });
        }

        // Tier-2 fallback (no candidate has a finite σ(focus)) routes through the parallelized LOO; it too must match.
        [Test]
        public void SelectBestModel_ParallelMatchesSequential_OnMonotonicRamp_Tier2() {
            var points = ToPoints(MonotonicRamp_NoVertexInRange);
            var stepSize = InferStep(points);

            var sequentialModel = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true,
                maxOutlierRejections: 0, rejectionConfidence: 0.95,
                out var sequentialFit, out _, maxDegreeOfParallelism: 1);
            var parallelModel = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true,
                maxOutlierRejections: 0, rejectionConfidence: 0.95,
                out var parallelFit, out _, maxDegreeOfParallelism: 0);

            Assert.Multiple(() => {
                Assert.That(parallelModel, Is.EqualTo(sequentialModel));
                if (sequentialFit != null) {
                    Assert.That(parallelFit, Is.Not.Null);
                    Assert.That(parallelFit.Minimum.X, Is.EqualTo(sequentialFit.Minimum.X));
                    Assert.That(parallelFit.Minimum.Y, Is.EqualTo(sequentialFit.Minimum.Y));
                }
            });
        }

        // The leave-one-out best-focus std error must be bit-identical between the sequential and parallel paths:
        // the predictions are written to index-aligned slots and reduced in ascending skip-index order, so the
        // floating-point reduction order is the same regardless of completion order.
        [Test]
        public void ComputeLeaveOneOutBestFocusStdError_ParallelMatchesSequential_BitIdentical() {
            var points = TiltedCurve();
            var stepSize = InferStep(points);

            foreach (var model in Candidates) {
                var sequential = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(
                    alglibAPI, model, points, stepSize, useWeights: true, maxDegreeOfParallelism: 1);
                var parallel = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(
                    alglibAPI, model, points, stepSize, useWeights: true, maxDegreeOfParallelism: 0);

                // Both NaN (guard) or both bit-identical.
                if (double.IsNaN(sequential)) {
                    Assert.That(double.IsNaN(parallel), Is.True, $"model {model}: NaN guard must agree");
                } else {
                    Assert.That(parallel, Is.EqualTo(sequential),
                        $"model {model}: LOO std error must be bit-identical between parallel and sequential");
                }
            }
        }

        // Default (no degree argument) and explicit auto (0) are the same auto path — sanity that the default param
        // does not change the answer vs the sequential reference.
        [Test]
        public void SelectBestModel_DefaultDegree_MatchesSequential() {
            var points = TiltedCurve();
            var stepSize = InferStep(points);

            var sequentialModel = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true,
                maxOutlierRejections: 1, rejectionConfidence: 0.95,
                out var sequentialFit, out _, maxDegreeOfParallelism: 1);
            // Default param ⇒ auto (parallel).
            var defaultModel = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true,
                maxOutlierRejections: 1, rejectionConfidence: 0.95,
                out var defaultFit, out _);

            Assert.Multiple(() => {
                Assert.That(defaultModel, Is.EqualTo(sequentialModel));
                Assert.That(defaultFit.Minimum.X, Is.EqualTo(sequentialFit.Minimum.X));
                Assert.That(defaultFit.Minimum.Y, Is.EqualTo(sequentialFit.Minimum.Y));
            });
        }

        // The 5-parameter SelectBestModel overload (no rejectedPoints out, no rejection params) is the one
        // used by HocusFocusVM. It delegates to the full overload with maxOutlierRejections=0, so the
        // winning model and best-focus position must be identical to the full overload with the same settings.
        [Test]
        public void SelectBestModel_FiveParamOverload_MatchesFullOverload() {
            var points = TiltedCurve();
            var stepSize = InferStep(points);

            // Full overload with no rejection (maxOutlierRejections=0 is the sentinel the 5-param overload passes).
            var fullModel = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true,
                maxOutlierRejections: 0, rejectionConfidence: 0.0,
                out var fullFit, out _);

            // 5-param overload — the one used by HocusFocusVM.
            var shortModel = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true,
                out var shortFit);

            Assert.Multiple(() => {
                Assert.That(shortModel, Is.EqualTo(fullModel), "5-param overload must pick the same winning model");
                Assert.That(shortFit, Is.Not.Null);
                Assert.That(shortFit.Minimum.X, Is.EqualTo(fullFit.Minimum.X), "best-focus X must match");
                Assert.That(shortFit.Minimum.Y, Is.EqualTo(fullFit.Minimum.Y), "best-focus Y must match");
            });
        }

        // UC108, n=21: a monotonic ramp (no real vertex in range) — drives every asymmetric model degenerate, so
        // σ(focus) is NaN for all and selection falls back to LOO. Mirrors HybridModelSelectionTests.
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
