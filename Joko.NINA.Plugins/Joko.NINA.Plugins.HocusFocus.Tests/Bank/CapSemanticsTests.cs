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
using TestApp.SynthBank;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Bank;

/// <summary>
/// A4's cap-semantics rule. The two tests that matter are the two defects the rule carried: the boundary was
/// rebuilt from the REQUESTED sweep while the product used the FITTED span, and only <c>WasCapped</c> was read
/// when the band floor clamps to the same bound.
/// </summary>
[TestFixture]
public class CapSemanticsTests {

    private const double Mult = StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple;   // 1.5

    // ---- the boundary comes from the FITTED span, not the requested sweep ----

    /// <summary>
    /// D01 r1's shape, in the numbers the bank published: the sweep REQUESTED 2×4×3 = 24.0 but only 12.0 survived
    /// into the fit, so the product's bound was 1.5 × 0.5 × 12.0 = 9.0 while A4 was asserting against
    /// 1.5 × 0.5 × 24.0 = 18.0 — twice the bound the product ever applied.
    /// </summary>
    [Test]
    public void Evaluate_UsesTheFittedSpan_NotTheRequestedSweep() {
        var r = CapSemantics.Evaluate(
            truthHalfWidth: 12.0, fittedSearchSpan: 12.0,
            requestedOffsetSteps: 4, requestedStepSize: 3.0,
            wasCapped: true, wasBandFloored: false);

        Assert.Multiple(() => {
            Assert.That(r.SpanSource, Is.EqualTo("fitted"));
            Assert.That(r.CapBoundary, Is.EqualTo(Mult * 0.5 * 12.0).Within(1e-12), "boundary must come from the fitted span");
            // truth 12.0 is above the real bound 9.0, and the round WAS capped -> the rule agrees.
            Assert.That(r.PredictedBounded, Is.True);
            Assert.That(r.Verdict, Is.EqualTo(CapSemanticsVerdict.Pass));
        });
    }

    /// <summary>
    /// The same round scored the old way: against the requested sweep the boundary is 18.0, truth 12.0 sits
    /// below it, so the rule would have predicted "not bounded" while the product plainly capped — a FAIL
    /// invented by the assertion. Pinned here so the regression is visible rather than argued.
    /// </summary>
    [Test]
    public void Evaluate_AgainstTheRequestedSweep_WouldContradictACappedRound() {
        var legacy = CapSemantics.Evaluate(
            truthHalfWidth: 12.0, fittedSearchSpan: double.NaN,   // NaN => legacy fallback to the requested sweep
            requestedOffsetSteps: 4, requestedStepSize: 3.0,
            wasCapped: true, wasBandFloored: false);

        Assert.Multiple(() => {
            Assert.That(legacy.SpanSource, Is.EqualTo("requested"));
            Assert.That(legacy.CapBoundary, Is.EqualTo(Mult * 4 * 3.0).Within(1e-12));
            Assert.That(legacy.PredictedBounded, Is.False, "the requested-sweep boundary is 18.0, above truth 12.0");
            Assert.That(legacy.ActualBounded, Is.True, "but the recommender did cap");
            Assert.That(legacy.Verdict, Is.EqualTo(CapSemanticsVerdict.Fail));
        });
    }

    /// <summary>
    /// The compatibility guarantee: on the 36 of 37 bank rounds where nothing was dropped, fitted == requested
    /// and the boundary is unchanged, so re-scoring an old artifact cannot move a verdict.
    /// </summary>
    [Test]
    public void Evaluate_WhenFittedEqualsRequested_MatchesTheLegacyBoundaryExactly() {
        // requested span = 2 × offsetSteps × stepSize = 2 × 4 × 2 = 16.0
        var fitted = CapSemantics.Evaluate(20.0, 16.0, 4, 2.0, wasCapped: true, wasBandFloored: false);
        var legacy = CapSemantics.Evaluate(20.0, double.NaN, 4, 2.0, wasCapped: true, wasBandFloored: false);

        Assert.Multiple(() => {
            Assert.That(fitted.CapBoundary, Is.EqualTo(legacy.CapBoundary).Within(1e-12));
            Assert.That(fitted.Verdict, Is.EqualTo(legacy.Verdict));
        });
    }

    [Test]
    public void Evaluate_NonPositiveOrNonFiniteFittedSpan_FallsBackToTheRequestedSweep() {
        foreach (var bad in new[] { double.NaN, 0.0, -3.0, double.PositiveInfinity }) {
            var r = CapSemantics.Evaluate(20.0, bad, 4, 2.0, wasCapped: true, wasBandFloored: false);
            Assert.That(r.SpanSource, Is.EqualTo("requested"), $"fittedSearchSpan={bad}");
            Assert.That(r.CapBoundary, Is.EqualTo(Mult * 4 * 2.0).Within(1e-12), $"fittedSearchSpan={bad}");
        }
    }

    // ---- a band-floored round is EXEMPT, because the cap predicate does not describe it ----

    /// <summary>
    /// D03 r0's shape: the floor fired and truth (46.1) sits well ABOVE the boundary (24.0). The old rule read
    /// that as "should have been capped, wasn't" and FAILED a round the floor had handled exactly as F81
    /// designed. The cap clamps DOWN, the floor raises UP; "truth is above the boundary" predicts the cap and
    /// says nothing about the floor.
    /// </summary>
    [Test]
    public void Evaluate_ABandFlooredRound_TruthAboveTheBoundary_IsNotApplicable() {
        var r = CapSemantics.Evaluate(
            truthHalfWidth: 46.1, fittedSearchSpan: 32.0,
            requestedOffsetSteps: 4, requestedStepSize: 4.0,
            wasCapped: false, wasBandFloored: true);

        Assert.Multiple(() => {
            Assert.That(r.CapBoundary, Is.EqualTo(Mult * 0.5 * 32.0).Within(1e-12));   // 24.0
            Assert.That(r.PredictedBounded, Is.True, "truth 46.1 is above the 24.0 boundary");
            Assert.That(r.Branch, Is.EqualTo("band-floored"));
            Assert.That(r.Verdict, Is.EqualTo(CapSemanticsVerdict.NotApplicable));
        });
    }

    /// <summary>
    /// The mirror, and the reason "floored counts as bounded" was rejected: D01 r0 (ratio 0.87) and D02 r0
    /// (ratio 0.70) are floored rounds whose truth sits BELOW the boundary. Under that alternative they would
    /// have dropped PASS to FLAG and PASS to FAIL respectively, on rounds where the recommender did nothing
    /// wrong. Exemption is the verdict that is right in BOTH directions.
    /// </summary>
    [Test]
    public void Evaluate_ABandFlooredRound_TruthBelowTheBoundary_IsAlsoNotApplicable() {
        // D01 r0: truth 10.4, fitted span 16.0 -> boundary 12.0, ratio 0.87
        var d01 = CapSemantics.Evaluate(10.4, 16.0, 4, 2.0, wasCapped: false, wasBandFloored: true);
        // D02 r0: truth 8.4, fitted span 16.0 -> boundary 12.0, ratio 0.70
        var d02 = CapSemantics.Evaluate(8.4, 16.0, 4, 2.0, wasCapped: false, wasBandFloored: true);

        Assert.Multiple(() => {
            Assert.That(d01.PredictedBounded, Is.False);
            Assert.That(d01.Verdict, Is.EqualTo(CapSemanticsVerdict.NotApplicable));
            Assert.That(d02.PredictedBounded, Is.False);
            Assert.That(d02.Verdict, Is.EqualTo(CapSemanticsVerdict.NotApplicable));
        });
    }

    [Test]
    public void Evaluate_AnUnboundedRoundBelowTheBoundary_Passes() {
        var r = CapSemantics.Evaluate(5.0, 16.0, 4, 2.0, wasCapped: false, wasBandFloored: false);
        Assert.Multiple(() => {
            Assert.That(r.PredictedBounded, Is.False);
            Assert.That(r.ActualBounded, Is.False);
            Assert.That(r.Branch, Is.EqualTo("unbounded"));
            Assert.That(r.Verdict, Is.EqualTo(CapSemanticsVerdict.Pass));
        });
    }

    /// <summary>An unbounded round whose truth is far above the boundary is still a genuine FAIL — the fix must
    /// not turn A4 into a rule that cannot fail.</summary>
    [Test]
    public void Evaluate_AnUnboundedRoundFarAboveTheBoundary_StillFails() {
        var r = CapSemantics.Evaluate(30.0, 16.0, 4, 2.0, wasCapped: false, wasBandFloored: false);
        Assert.Multiple(() => {
            Assert.That(r.PredictedBounded, Is.True);
            Assert.That(r.ActualBounded, Is.False);
            Assert.That(r.Verdict, Is.EqualTo(CapSemanticsVerdict.Fail));
        });
    }

    // ---- the near-boundary FLAG band survives the change ----

    [Test]
    public void Evaluate_JustAboveTheBoundary_IsAFlagNotAFail() {
        // boundary = 1.5 × 0.5 × 16 = 12.0; truth 12.6 => ratio 1.05, inside the 0.15 band.
        var r = CapSemantics.Evaluate(12.6, 16.0, 4, 2.0, wasCapped: false, wasBandFloored: false);
        Assert.Multiple(() => {
            Assert.That(r.Ratio, Is.EqualTo(1.05).Within(1e-12));
            Assert.That(r.Verdict, Is.EqualTo(CapSemanticsVerdict.Flag));
        });
    }

    [Test]
    public void Evaluate_OutsideTheFlagBand_IsAFail() {
        // ratio 1.20 is outside the 0.15 band.
        var r = CapSemantics.Evaluate(14.4, 16.0, 4, 2.0, wasCapped: false, wasBandFloored: false);
        Assert.Multiple(() => {
            Assert.That(r.Ratio, Is.EqualTo(1.20).Within(1e-12));
            Assert.That(r.Verdict, Is.EqualTo(CapSemanticsVerdict.Fail));
        });
    }
}
