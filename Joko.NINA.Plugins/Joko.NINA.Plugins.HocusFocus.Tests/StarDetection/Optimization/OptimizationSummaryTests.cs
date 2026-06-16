#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// Unit tests for the pure, de-jargoned summary formatting + improvement math on <see cref="OptimizationSummary"/>
/// (starry-hopper items 5/8): the before→after / "(unchanged)" recommendation text, the σ "tighter" readout, and
/// the higher-is-better improvement percentage.
/// </summary>
[TestFixture]
public class OptimizationSummaryTests {

    [Test]
    public void FormatRecommendation_Changed_UsesArrow() {
        Assert.That(OptimizationSummary.FormatRecommendation(150, 105), Is.EqualTo("150 → 105"));
    }

    [Test]
    public void FormatRecommendation_Unchanged_SaysSo() {
        Assert.That(OptimizationSummary.FormatRecommendation(105, 105), Is.EqualTo("105 (unchanged)"));
    }

    [Test]
    public void StepSizeText_AndOffsetStepsText_FollowRecommendationFormat() {
        var changed = new OptimizationSummary { CurrentStepSize = 150, RecommendedStepSize = 105, CurrentOffsetSteps = 4, RecommendedOffsetSteps = 4 };
        Assert.Multiple(() => {
            Assert.That(changed.StepSizeText, Is.EqualTo("150 → 105"));
            Assert.That(changed.OffsetStepsText, Is.EqualTo("4 (unchanged)"));
        });
    }

    [Test]
    public void StepSizeOrOffsetChanged_TrueWhenEitherDiffers() {
        Assert.Multiple(() => {
            Assert.That(new OptimizationSummary { CurrentStepSize = 150, RecommendedStepSize = 105, CurrentOffsetSteps = 4, RecommendedOffsetSteps = 4 }.StepSizeOrOffsetChanged, Is.True);
            Assert.That(new OptimizationSummary { CurrentStepSize = 100, RecommendedStepSize = 100, CurrentOffsetSteps = 4, RecommendedOffsetSteps = 5 }.StepSizeOrOffsetChanged, Is.True);
            Assert.That(new OptimizationSummary { CurrentStepSize = 100, RecommendedStepSize = 100, CurrentOffsetSteps = 4, RecommendedOffsetSteps = 4 }.StepSizeOrOffsetChanged, Is.False);
        });
    }

    [Test]
    public void SigmaText_ShowsTighterPercent_WhenImproved() {
        var s = new OptimizationSummary { SeedSigmaFocus = 2.0, BestSigmaFocus = 1.0 };
        Assert.That(s.SigmaText, Does.Contain("2.00 → 1.00").And.Contain("50% tighter"));
    }

    [Test]
    public void SigmaText_Unchanged_SaysUnchanged() {
        // σ effectively the same before/after (at the F2 precision shown) reads "2.20 (unchanged)", not "2.20 → 2.20".
        Assert.Multiple(() => {
            Assert.That(new OptimizationSummary { SeedSigmaFocus = 2.20, BestSigmaFocus = 2.20 }.SigmaText, Is.EqualTo("2.20 (unchanged)"));
            Assert.That(new OptimizationSummary { SeedSigmaFocus = 2.201, BestSigmaFocus = 2.203 }.SigmaText, Is.EqualTo("2.20 (unchanged)"));
        });
    }

    [Test]
    public void SigmaText_NonFinite_FallsBackToBaseText_NoTighterOrUnchanged() {
        var s = new OptimizationSummary { SeedSigmaFocus = double.NaN, BestSigmaFocus = 1.0 }.SigmaText;
        Assert.Multiple(() => {
            Assert.That(s, Does.Not.Contain("tighter"));
            Assert.That(s, Does.Not.Contain("unchanged"));
        });
    }

    [Test]
    public void PercentBetter_HigherIsBetter_ClampedAndGuarded() {
        Assert.Multiple(() => {
            Assert.That(OptimizationSummary.PercentBetter(1.0, 1.5), Is.EqualTo(50.0).Within(1e-9), "higher best => positive %");
            Assert.That(OptimizationSummary.PercentBetter(2.0, 1.0), Is.EqualTo(0.0), "a lower 'best' clamps to 0 (no improvement)");
            Assert.That(OptimizationSummary.PercentBetter(1.0, 1.0), Is.EqualTo(0.0));
            Assert.That(OptimizationSummary.PercentBetter(0.0, 5.0), Is.EqualTo(0.0), "near-zero baseline guarded");
            Assert.That(OptimizationSummary.PercentBetter(double.NaN, 1.0), Is.EqualTo(0.0));
            Assert.That(OptimizationSummary.PercentBetter(1.0, double.PositiveInfinity), Is.EqualTo(0.0));
        });
    }

    [Test]
    public void ImprovementPercent_UsesObjectiveScore() {
        var s = new OptimizationSummary { SeedJ = 1.0, BestJ = 1.2 };
        Assert.That(s.ImprovementPercent, Is.EqualTo(20.0).Within(1e-9));
    }
}
