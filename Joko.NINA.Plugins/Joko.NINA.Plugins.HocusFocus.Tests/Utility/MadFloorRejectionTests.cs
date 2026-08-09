#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

/// <summary>
/// F45(b) — the optional floor on the residual scale <see cref="MathUtility.RejectionTest"/> divides by.
///
/// <para>Every assertion here defends one of three claims the wave's whole framework rests on:</para>
/// <list type="number">
/// <item><b>The default is not merely equivalent, it is the same arithmetic.</b> The control rung's entire
/// evidentiary value is that it reproduces the previous wave's numbers exactly.</item>
/// <item><b>The floor is applied AFTER the degenerate-scale guards</b>, so the stdDev fallback still fires and
/// the floor can never make the effective scale <i>smaller</i> than it would have been. Flooring before the
/// guards would let a floor CREATE a rejection.</item>
/// <item><b>A floor can only suppress.</b> One scale divides every z in a round, so the argmax cannot move; the
/// rejected point is the same point or there is none. Everything the wave concludes from a counterfactual over
/// stored reports is downstream of that.</item>
/// </list>
///
/// <para><b>Against the pre-change source every test in this file fails to compile</b>, because the six-argument
/// <c>RejectionTest</c> overload and <see cref="MadFloorSpec"/> do not exist there. Each test therefore also
/// names the narrower mutant it kills — the one-line change to the post-change source that it catches — since
/// that, not the compile error, is what makes it a test rather than a signature check.</para>
/// </summary>
[TestFixture]
public class MadFloorRejectionTests {

    private const double Confidence = 0.95;

    private static readonly Func<double, double> Zero = _ => 0.0;

    // Residuals become the Y values verbatim: fitting is the zero function and no weights are supplied, so
    // errors[i] == Y[i] exactly. That keeps every expected z computable by hand from the numbers below.
    private static List<ScatterErrorPoint> Residuals(params double[] r) =>
        r.Select((v, i) => new ScatterErrorPoint(i, v, 0.0, 1.0)).ToList();

    // ---- the point sets, each chosen for a distinct branch ----

    // A gross outlier over an otherwise exact fit, with a non-degenerate MAD. The ordinary path. median(r) = 0,
    // MAD(r) = 1.483 * 0.01 = 0.01483, so the outlier sits at z = 0.6/0.01483 = 40.5 against a limit of 2.020 at
    // N = 7. The magnitude 0.6 is chosen so that NO rung of the swept ladder below lands within 15% of that
    // limit — a test that turns on the third decimal place of a Student-t quantile is a test of the quantile.
    private static List<ScatterErrorPoint> GrossOutlier() =>
        Residuals(0.0, 0.01, -0.01, 0.02, -0.02, 0.6, 0.0);

    // MAD(r) collapses to EXACTLY zero (five of six residuals identical), so the stdDev fallback is the only
    // reason a rejection is possible at all. This is the branch a floor applied in the wrong place destroys.
    private static List<ScatterErrorPoint> ZeroMadStdDevFallback() =>
        Residuals(0.0, 0.0, 0.0, 0.0, 0.0, 5.0);

    // The `caboose` shape, from the previous wave's own report for that run's round 1 (N=7, Grubbs limit 2.020,
    // r = 0.0012, -0.0238, ~0, ~0, ~0, 0.0082, ~0; recovered scale ~1.48e-5; pos 7275 rejected at z ~1609.6).
    // The "~0" rows print as 0.0000 at four decimals but are not zero — recovering the scale from the printed
    // MAD(r) field is exactly the mistake this reconstruction avoids.
    private static List<ScatterErrorPoint> CabooseRoundOne() {
        var r = new[] { 0.0012, -0.0238, -3e-6, 6e-6, -1e-6, 0.0082, -1.1e-5 };
        var pos = new[] { 7350.0, 7275.0, 7200.0, 7125.0, 7050.0, 6975.0, 6900.0 };
        return pos.Select((x, i) => new ScatterErrorPoint(x, r[i], 0.0, 1.0)).ToList();
    }

    // ---- 1. the control rung ----

    [Test]
    public void TheDefaultFloor_ReproducesTheShippedOverloadExactly() {
        // PRE-CHANGE: does not compile (no six-argument overload). MUTANT KILLED: any implementation whose
        // floored path is a SECOND copy of the statistic rather than the same code reached with a floor of 0 —
        // two copies drift, and the control rung would then be measuring the copy, not the product.
        foreach (var build in new Func<List<ScatterErrorPoint>>[] { GrossOutlier, ZeroMadStdDevFallback, CabooseRoundOne }) {
            var shipped = MathUtility.RejectionTest(build(), Zero, Confidence);
            var floored = MathUtility.RejectionTest(build(), Zero, Confidence, weights: null, scaleFloor: 0.0, scaleUsed: out var scaleUsed);

            Assert.Multiple(() => {
                Assert.That(floored?.X, Is.EqualTo(shipped?.X),
                    "at f = 0 the verdict must be the shipped verdict, not a numerically similar one");
                Assert.That(floored?.Y, Is.EqualTo(shipped?.Y));
                Assert.That(scaleUsed, Is.GreaterThan(0.0),
                    "the reported effective scale must be the real one, so a caller can anchor to it");
            });
        }
    }

    [Test]
    public void TheReportedScale_IsNaN_WhenNoTestWasPerformed() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: initialising scaleUsed to 0.0 instead of NaN. "No test
        // was performed" must not be reportable as "the scale was zero" — a zero would silently become a
        // family-B anchor of zero, i.e. no floor, on a round that never ran.
        var tooFew = Residuals(0.0, 1.0, 2.0); // <= 3 points: the test never runs
        var perfect = Residuals(1.0, 1.0, 1.0, 1.0, 1.0, 1.0); // MAD 0 AND stdDev 0: degenerate after both guards

        var a = MathUtility.RejectionTest(tooFew, Zero, Confidence, null, 0.0, out var scaleTooFew);
        var b = MathUtility.RejectionTest(perfect, Zero, Confidence, null, 0.0, out var scalePerfect);

        Assert.Multiple(() => {
            Assert.That(a, Is.Null);
            Assert.That(b, Is.Null);
            Assert.That(double.IsNaN(scaleTooFew), Is.True, "too few points: no scale was ever computed");
            Assert.That(double.IsNaN(scalePerfect), Is.True, "degenerate after both guards: no scale was ever computed");
        });
    }

    // ---- 2. the floor is applied AFTER the degenerate-scale guards ----

    [Test]
    public void TheStdDevFallbackStillFires_AndTheFloorNeverShrinksTheScale() {
        // Residuals [0,0,0,0,0,5]: the median is 0 and the median absolute deviation is EXACTLY 0, so MAD(r) = 0
        // and only the stdDev fallback can produce a usable scale. Sample stdDev = sqrt(20.8333/5) = 2.0412415.
        // The Grubbs limit at N = 6, confidence 0.95 is 1.887, and 5/2.0412415 = 2.4495 clears it.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED — and this is the one the design singles out: moving the
        // floor ABOVE the fallback, i.e. `scale = mad; if (floor > 0) scale = max(scale, floor); if (scale <= 0)
        // scale = stdDev(...)`. That mutant reports scaleUsed = 1.0 for the floor-1.0 case below (the fallback
        // never runs), which is SMALLER than the 2.0412 the product uses — a floor that shrinks the scale, and
        // therefore a floor that can create a rejection. Monotonicity is the whole basis of the counterfactual,
        // so it is asserted rather than assumed.
        const double sampleStdDev = 2.041241452319315;

        var unfloored = MathUtility.RejectionTest(ZeroMadStdDevFallback(), Zero, Confidence, null, 0.0, out var scaleUnfloored);
        var belowFallback = MathUtility.RejectionTest(ZeroMadStdDevFallback(), Zero, Confidence, null, 1.0, out var scaleBelow);
        var aboveFallback = MathUtility.RejectionTest(ZeroMadStdDevFallback(), Zero, Confidence, null, 10.0, out var scaleAbove);

        Assert.Multiple(() => {
            Assert.That(scaleUnfloored, Is.EqualTo(sampleStdDev).Within(1e-12),
                "with MAD(r) = 0 the effective scale must be the stdDev fallback, not zero");
            Assert.That(unfloored?.Y, Is.EqualTo(5.0), "the lone gross residual is still detectable via the fallback");

            Assert.That(scaleBelow, Is.EqualTo(scaleUnfloored),
                "a floor BELOW the fallback scale must change nothing at all — exactly, not approximately");
            Assert.That(belowFallback?.Y, Is.EqualTo(5.0));

            Assert.That(scaleAbove, Is.EqualTo(10.0),
                "a floor ABOVE the fallback scale replaces it, and z = 5/10 = 0.5 is under the 1.887 limit");
            Assert.That(aboveFallback, Is.Null, "so the floor SUPPRESSES this rejection");
        });
    }

    // ---- 3. a floor can only suppress ----

    [TestCaseSource(nameof(SuppressionOnlyCases))]
    public void AFloorCanOnlySuppress_NeverRedirect_AndTheScaleNeverDecreases(string name, Func<List<ScatterErrorPoint>> build) {
        // Sweeping the floor upward on a fixed point set, the rejected point is either the SAME point or none —
        // never a different point — and once a rung stops firing no larger rung starts again. That prefix
        // property is what lets a counterfactual be computed from a stored report at all: every model's floored
        // rejection set is a prefix of its unfloored set, so the consensus can only shrink.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: applying the floor per point rather than per round (e.g.
        // flooring |errors[i] - median| instead of the scale), which is non-uniform and CAN move the argmax; and
        // any `Math.Min` slip, which would make the scale decrease as the rung rises.
        var floors = new[] { 0.0, 1e-9, 1e-6, 1e-4, 0.001, 0.01, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0, 50.0, 1e6 };
        var baseline = MathUtility.RejectionTest(build(), Zero, Confidence, null, 0.0, out var baselineScale);
        Assume.That(baseline, Is.Not.Null, $"{name} must fire at the control rung or it proves nothing");

        double previousScale = baselineScale;
        bool stoppedFiringAt = false;
        Assert.Multiple(() => {
            foreach (var floor in floors) {
                var rejected = MathUtility.RejectionTest(build(), Zero, Confidence, null, floor, out var scale);

                Assert.That(scale, Is.GreaterThanOrEqualTo(previousScale),
                    $"{name}: the effective scale must be non-decreasing in the floor (f = {floor})");
                Assert.That(scale, Is.GreaterThanOrEqualTo(baselineScale),
                    $"{name}: no floor may take the scale BELOW the unfloored scale (f = {floor})");
                previousScale = scale;

                if (rejected == null) {
                    stoppedFiringAt = true;
                    continue;
                }
                Assert.That(stoppedFiringAt, Is.False,
                    $"{name}: a rung that fires after a lower rung went silent would break the prefix property (f = {floor})");
                Assert.That(rejected.X, Is.EqualTo(baseline.X),
                    $"{name}: a floor may suppress the rejection but must never redirect it to another point (f = {floor})");
            }
        });
    }

    private static IEnumerable<TestCaseData> SuppressionOnlyCases() {
        yield return new TestCaseData("gross outlier", (Func<List<ScatterErrorPoint>>)GrossOutlier);
        yield return new TestCaseData("zero-MAD stdDev fallback", (Func<List<ScatterErrorPoint>>)ZeroMadStdDevFallback);
        yield return new TestCaseData("caboose round 1", (Func<List<ScatterErrorPoint>>)CabooseRoundOne);
    }

    // ---- 4. the named case ----

    [Test]
    public void TheCabooseShape_FiresAtTheControlRung_AndIsSuppressedAtTheQuarterRung() {
        // The run this whole sub-question is named after. Its scale is ~1.48e-5 not because the MAD broke down
        // but because its residuals are genuinely tiny relative to sigma — the rejected point sits at roughly
        // 2.4% of its own error bar from the curve and is still thrown out at z ~1600. Family A at f = 0.25
        // divides that z by ~17000 and the rejection disappears.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: a floor that is applied to the MAD before the 1.483
        // scaling, or one applied to the residuals instead of the scale — either changes the 0.25 arithmetic
        // enough that this run keeps firing, which is the outcome the ladder exists to distinguish.
        var control = MathUtility.RejectionTest(CabooseRoundOne(), Zero, Confidence, null, 0.0, out var controlScale);
        var quarter = MathUtility.RejectionTest(CabooseRoundOne(), Zero, Confidence, null, 0.25, out var quarterScale);

        Assert.Multiple(() => {
            Assert.That(controlScale, Is.EqualTo(1.483e-5).Within(1e-9),
                "the reconstruction must land on the scale recovered from the stored report, ~1.48e-5");
            Assert.That(control, Is.Not.Null);
            Assert.That(control.X, Is.EqualTo(7275.0), "the same focuser position the stored report rejects");
            // |r - median| / scale = 0.023799 / 1.483e-5 against a Grubbs limit of 2.020 at N = 7.
            Assert.That(Math.Abs(control.Y - (-1e-6)) / controlScale, Is.GreaterThan(1000.0),
                "and it is rejected by three orders of magnitude, not marginally");

            Assert.That(quarterScale, Is.EqualTo(0.25),
                "at f = 0.25 the floor is what divides, because the MAD is five orders of magnitude below it");
            Assert.That(quarter, Is.Null,
                "0.0238 / 0.25 = 0.095 against a limit of 2.020 — suppressed by a factor of 21");
        });
    }

    // ---- 5. family B ----

    [Test]
    public void FamilyB_CannotTouchRoundOne_EvenAtAnAbsurdAlpha() {
        // Round 1 has no round-1 anchor yet, so family B's resolved floor is 0 there NO MATTER WHAT alpha is.
        // That is why the family is "bit-identical at round 1 by construction" rather than by tuning, and the
        // contrast below is the proof: the same numeric floor value, expressed as family A, silences round 1.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: seeding the anchor with the current round's own scale
        // (i.e. resolving alpha * scale_k on round 1), which is the natural-looking bug and would make family B
        // a plain multiplicative rung that suppresses everything at alpha >= 1.
        var unflooredRejected = MathUtility.RejectionTest(GrossOutlier(), Zero, Confidence, null, 0.0, out var scaleOne);

        var familyB = MadFloorSpec.RelativeToFirstRound(1e9);
        var roundOneFloor = familyB.ResolveFloor(double.NaN); // NaN = "no anchor recorded yet", i.e. round 1
        var floored = MathUtility.RejectionTest(GrossOutlier(), Zero, Confidence, null, roundOneFloor, out var scaleFlooredOne);

        // The same magnitude, but as family A, which DOES apply on round 1.
        var asFamilyA = MadFloorSpec.Absolute(1e9 * scaleOne);
        var familyARejected = MathUtility.RejectionTest(GrossOutlier(), Zero, Confidence, null, asFamilyA.ResolveFloor(scaleOne), out _);

        Assert.Multiple(() => {
            Assert.That(roundOneFloor, Is.EqualTo(0.0), "no anchor means no floor, never a guessed one");
            Assert.That(scaleFlooredOne, Is.EqualTo(scaleOne), "round 1's effective scale must be bit-identical");
            Assert.That(floored?.X, Is.EqualTo(unflooredRejected?.X), "and so must round 1's verdict");
            Assert.That(familyARejected, Is.Null,
                "the same floor value as family A silences round 1 — so the round-1 invariance above is family B's doing, not a floor too small to matter");
        });
    }

    [Test]
    public void FamilyB_SuppressesACollapsingSecondRound() {
        // The cascade family B targets: round 1's scale is 0.01483 and round 2's has collapsed to 4.449e-5, so
        // round 2 fires on residuals three hundred times smaller than the ones round 1 judged. Anchoring to
        // round 1 at alpha = 0.5 puts the floor at 0.007415 and round 2 goes quiet, while round 1 is untouched.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: re-anchoring every round to the PREVIOUS round's scale
        // instead of round 1's. That mutant follows the collapse down (floor = 0.5 * 4.449e-5 on round 3) and
        // never contains a cascade, which is the entire point of the family.
        var roundOne = GrossOutlier();
        var roundTwo = Residuals(0.0, 0.00002, -0.00002, 0.00004, -0.00004, 0.001);

        var spec = MadFloorSpec.RelativeToFirstRound(0.5);

        // Round 1: no anchor yet.
        var r1 = MathUtility.RejectionTest(roundOne, Zero, Confidence, null, spec.ResolveFloor(double.NaN), out var scale1);
        var r1Unfloored = MathUtility.RejectionTest(roundOne, Zero, Confidence, null, 0.0, out var scale1Unfloored);

        // Round 2, as FitWithOutlierRejection drives it: the anchor is round 1's recorded scale.
        var roundTwoFloor = spec.ResolveFloor(scale1);
        var r2Floored = MathUtility.RejectionTest(roundTwo, Zero, Confidence, null, roundTwoFloor, out var scale2Floored);
        var r2Unfloored = MathUtility.RejectionTest(roundTwo, Zero, Confidence, null, 0.0, out var scale2Unfloored);

        Assert.Multiple(() => {
            Assert.That(scale1, Is.EqualTo(scale1Unfloored), "round 1 is bit-identical");
            Assert.That(r1, Is.Not.Null);
            Assert.That(r1.Y, Is.EqualTo(0.6), "round 1 still rejects the gross residual");
            Assert.That(r1.Y, Is.EqualTo(r1Unfloored.Y), "and it is the same point the unfloored round rejects");
            Assert.That(scale1, Is.EqualTo(1.483 * 0.01).Within(1e-12));

            Assert.That(scale2Unfloored, Is.EqualTo(1.483 * 3e-5).Within(1e-12),
                "round 2's scale has collapsed to ~4.45e-5 — the cascade family B is aimed at");
            Assert.That(r2Unfloored, Is.Not.Null, "and it fires there at the control rung: 0.00099 / 4.45e-5 = 22");

            Assert.That(roundTwoFloor, Is.EqualTo(0.5 * scale1),
                "the floor is alpha times ROUND ONE's scale, not this round's");
            Assert.That(scale2Floored, Is.EqualTo(roundTwoFloor),
                "and it dominates the collapsed scale");
            Assert.That(r2Floored, Is.Null, "0.00099 / 0.0074 = 0.13 against a limit of 1.887 — contained");
        });
    }

    // ---- 6. the parameter object ----

    [Test]
    public void MadFloorSpec_MakesTheTwoFamiliesMutuallyExclusive() {
        // The families are pre-registered as alternatives; a type that can hold both would let an arm run a
        // combination nobody named while its report claimed one of the two.
        //
        // PRE-CHANGE: does not compile (the type does not exist). MUTANT KILLED: replacing the private
        // constructor + factories with a public two-double constructor, which makes "both families at once"
        // representable again.
        var a = MadFloorSpec.Absolute(0.25);
        var b = MadFloorSpec.RelativeToFirstRound(0.5);

        Assert.Multiple(() => {
            Assert.That(a.IsAbsolute, Is.True);
            Assert.That(a.IsRelativeToFirstRound, Is.False);
            Assert.That(a.FirstRoundFactor, Is.EqualTo(0.0));
            Assert.That(b.IsRelativeToFirstRound, Is.True);
            Assert.That(b.IsAbsolute, Is.False);
            Assert.That(b.AbsoluteFloor, Is.EqualTo(0.0));

            // Family A ignores the anchor entirely; family B is nothing but the anchor.
            Assert.That(a.ResolveFloor(double.NaN), Is.EqualTo(0.25));
            Assert.That(a.ResolveFloor(1234.0), Is.EqualTo(0.25));
            Assert.That(b.ResolveFloor(0.02), Is.EqualTo(0.01).Within(1e-15));
            Assert.That(b.ResolveFloor(double.NaN), Is.EqualTo(0.0));
            Assert.That(b.ResolveFloor(0.0), Is.EqualTo(0.0));
        });
    }

    [Test]
    public void MadFloorSpec_ZeroIsTheControlRung_AndNonsenseRungsThrow() {
        // f = 0.00 is a rung on the ladder, and it must be indistinguishable from "no floor" — otherwise the
        // control rung is a fifth measurement rather than a reproduction of the previous wave.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: `Absolute(0.0)` returning a spec whose IsNone is false,
        // which would make the control rung print a floor line and stop being byte-identical.
        Assert.Multiple(() => {
            Assert.That(MadFloorSpec.Absolute(0.0), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorSpec.RelativeToFirstRound(0.0), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorSpec.None.IsNone, Is.True);
            Assert.That(default(MadFloorSpec).IsNone, Is.True, "the C# default must be 'no floor', because that is what every production call site passes");
            Assert.That(MadFloorSpec.None.ResolveFloor(1.0), Is.EqualTo(0.0));

            Assert.That(() => MadFloorSpec.Absolute(-0.1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => MadFloorSpec.Absolute(double.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => MadFloorSpec.Absolute(double.PositiveInfinity), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => MadFloorSpec.RelativeToFirstRound(-1.0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => MadFloorSpec.RelativeToFirstRound(double.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }
}
