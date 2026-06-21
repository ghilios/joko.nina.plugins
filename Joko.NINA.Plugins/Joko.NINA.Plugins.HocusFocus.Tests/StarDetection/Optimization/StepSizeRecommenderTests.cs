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
