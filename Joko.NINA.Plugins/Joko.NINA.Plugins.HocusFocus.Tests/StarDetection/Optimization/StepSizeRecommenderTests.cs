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
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OxyPlot.Series;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

[TestFixture]
public class StepSizeRecommenderTests {

    // Symmetric hyperbola hfr(pos) = sqrt(a^2 + ((pos - p0)/b)^2). The offset W where hfr == 3*minHfr (== 3a)
    // satisfies 9a^2 = a^2 + (W/b)^2 => W = b*sqrt(8)*a. With a=1.5, b=8 => W = 8*sqrt(8)*1.5 ~= 33.94.
    private const double A = 1.5;
    private const double B = 8.0;
    private const int P0 = 10000;
    private static double ExpectedHalfWidth => B * Math.Sqrt(8.0) * A; // ~33.94

    private static AlglibHyperbolicFitting FitCleanHyperbola() {
        var alglib = new AlglibAPI();
        var points = new List<ScatterErrorPoint>();
        for (var i = -6; i <= 6; i++) {
            var pos = P0 + i * 100;
            var dx = (pos - P0) / B;
            var hfr = Math.Sqrt(A * A + dx * dx);
            points.Add(new ScatterErrorPoint(pos, hfr, 0, 0.02));
        }
        AlglibHyperbolicFitting.SelectBestModel(alglib, points, stepSize: 100, useWeights: true,
            maxOutlierRejections: 0, rejectionConfidence: 0.0, out var bestFit, out _);
        return bestFit;
    }

    /// <summary>Fits a SHALLOW sweep: one whose ends only reach <paramref name="edgeHfrRatio"/> times the minimum
    /// HFR, so the 3x band the recommender measures lies outside the sampled range entirely.</summary>
    private static AlglibHyperbolicFitting FitShallowHyperbola(double minHfr, double edgeHfrRatio, int stepSize, int offsetSteps) {
        var halfSpan = stepSize * offsetSteps;
        var b = halfSpan / (Math.Sqrt(edgeHfrRatio * edgeHfrRatio - 1.0) * minHfr);
        var points = new List<ScatterErrorPoint>();
        for (var i = -offsetSteps; i <= offsetSteps; i++) {
            var dx = (i * stepSize) / b;
            points.Add(new ScatterErrorPoint(P0 + i * stepSize, Math.Sqrt(minHfr * minHfr + dx * dx), 0, 0.02));
        }
        AlglibHyperbolicFitting.SelectBestModel(new AlglibAPI(), points, stepSize, useWeights: true,
            maxOutlierRejections: 0, rejectionConfidence: 0.0, out var bestFit, out _);
        return bestFit;
    }

    [Test]
    public void Recommend_ShallowSweep_CapsTheExtrapolationAndSaysSo() {
        // The reported case: HFR ~5.1 px at the vertex and only ~6.9 px at the ends of a +/-2096-step sweep. The
        // 3x band sits ~6400 steps out — 3.1x further than anything measured — and uncapped that recommended a
        // step of ~1838, i.e. a +/-7352 sweep derived almost entirely from extrapolating the model.
        const int stepSize = 524;
        const int offsetSteps = 4;
        var fit = FitShallowHyperbola(minHfr: 5.1, edgeHfrRatio: 1.36, stepSize: stepSize, offsetSteps: offsetSteps);

        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: stepSize);

        var sampledHalfSpan = stepSize * offsetSteps;
        Assert.Multiple(() => {
            Assert.That(rec.WasCapped, Is.True, "the half-width came from outside the sampled sweep");
            Assert.That(rec.HalfWidth, Is.EqualTo(StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple * sampledHalfSpan).Within(1.0));
            Assert.That(rec.StepSize, Is.EqualTo(898).Within(2), "a bounded step toward the answer, not a 3x jump");
            Assert.That(rec.StepSize * rec.OffsetSteps, Is.LessThanOrEqualTo(2 * sampledHalfSpan),
                "the implied sweep must stay near what this run actually measured");
        });
    }

    [Test]
    public void Recommend_SweepThatAlreadyReachesTheBand_IsNotCapped() {
        // A well-shaped sweep whose ends reach 3x the minimum already contains the half-width, so nothing is
        // extrapolated and the recommendation stands on its own data.
        var fit = FitShallowHyperbola(minHfr: 5.1, edgeHfrRatio: 3.0, stepSize: 524, offsetSteps: 4);

        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 524);

        Assert.Multiple(() => {
            Assert.That(rec.WasCapped, Is.False);
            Assert.That(rec.StepSize, Is.EqualTo(599).Within(3));
        });
    }

    [Test]
    public void Recommend_KnownHalfWidth_GivesAboutWidthOver3Point5() {
        var fit = FitCleanHyperbola();
        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 100);

        var expectedStep = (int)Math.Round(ExpectedHalfWidth / 3.5); // ~10
        Assert.Multiple(() => {
            Assert.That(rec.HalfWidth, Is.EqualTo(ExpectedHalfWidth).Within(2.0), "modeled 3x-min-HFR half-width");
            Assert.That(rec.StepSize, Is.EqualTo(expectedStep).Within(1), "step size ~= round(W / 3.5) so the sweep has ~3-4 points/side");
            Assert.That(rec.OffsetSteps, Is.EqualTo(4));
        });
    }

    [Test]
    public void Recommend_ClampsToFocuserMaxStep() {
        var fit = FitCleanHyperbola();
        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 100, focuserMaxStep: 2);
        Assert.That(rec.StepSize, Is.LessThanOrEqualTo(2), "must clamp to the focuser max step");
        Assert.That(rec.StepSize, Is.GreaterThanOrEqualTo(1), "step size never below 1");
    }

    [Test]
    public void Recommend_StepSizeNeverBelowOne() {
        var fit = FitCleanHyperbola();
        // Even with a focuserMaxStep of 0/negative the recommender must keep a legal >= 1 step.
        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 100, focuserMaxStep: 0);
        Assert.That(rec.StepSize, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Recommend_NullFit_ReturnsCurrentStepUnchanged() {
        var rec = StepSizeRecommender.Recommend(null, currentStepSize: 42);
        Assert.Multiple(() => {
            Assert.That(rec.StepSize, Is.EqualTo(42), "degenerate (null) fit => keep current step");
            Assert.That(double.IsNaN(rec.HalfWidth), Is.True);
        });
    }
}
