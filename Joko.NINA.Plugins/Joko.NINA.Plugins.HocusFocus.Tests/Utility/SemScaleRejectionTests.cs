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
/// Wave 16 item A — the SEM criterion on <see cref="MathUtility.RejectionTest"/>: the same F45(b) question as
/// the wave-14 MAD floor, asked in the units the plotted error bar actually implies,
/// <c>s = |r|·√N*</c>.
///
/// <para>Every assertion here defends one of the three properties the wave's framework rests on:</para>
/// <list type="number">
/// <item><b><see cref="SemScaleSpec.None"/> is the shipped arithmetic, not merely an equivalent one.</b> Clause
/// W1 is a BYTE comparison against wave 14's control rung, so the default must be inert where the rejection
/// actually executes — including on the degenerate-MAD stdDev-fallback path.</item>
/// <item><b>A SEM spec with no star counts THROWS.</b> A criterion that quietly does nothing when its input is
/// missing is a probe that reports absence as agreement.</item>
/// <item><b>Family V can only suppress; family R can redirect.</b> The first is why the veto rungs are eligible
/// for the subset gate; the second is why family R is excluded from it by pre-registration. Both are measured
/// here rather than argued, because an exclusion nobody can demonstrate is an exclusion nobody can defend.</item>
/// </list>
///
/// <para><b>Against the pre-change source every test in this file fails to compile</b>, because the nine-argument
/// <c>RejectionTest</c> overload and <see cref="SemScaleSpec"/> do not exist there. Each test therefore also
/// names the narrower mutant it kills — the one-line change to the post-change source that it catches — since
/// that, not the compile error, is what makes it a test rather than a signature check.</para>
/// </summary>
[TestFixture]
public class SemScaleRejectionTests {

    private const double Confidence = 0.95;

    private static readonly Func<double, double> Zero = _ => 0.0;

    // Residuals become the Y values verbatim: fitting is the zero function and no weights are supplied, so
    // errors[i] == Y[i] exactly. That keeps every expected z computable by hand from the numbers below.
    private static List<ScatterErrorPoint> Residuals(params double[] r) =>
        r.Select((v, i) => new ScatterErrorPoint(i, v, 0.0, 1.0)).ToList();

    // The side map, in the shape the production weights already travel in: keyed by p.X, never on the point.
    private static Func<double, double> Counts(params double[] n) => x => n[(int)Math.Round(x)];

    private static Func<double, double> ConstantCounts(double n) => _ => n;

    // ---- the point sets, each chosen for a distinct branch ----

    // A gross outlier over an otherwise exact fit, with a non-degenerate MAD. The ordinary path: median(r) = 0,
    // MAD(r) = 0.01483, the outlier at z = 40.5 against a limit of 2.020 at N = 7. With N* = 9 everywhere its
    // s = 0.6 * 3 = 1.8.
    private static List<ScatterErrorPoint> GrossOutlier() =>
        Residuals(0.0, 0.01, -0.01, 0.02, -0.02, 0.6, 0.0);

    // MAD(r) collapses to EXACTLY zero (five of six residuals identical), so the stdDev fallback is the only
    // reason a rejection is possible at all. The default must reproduce that branch too — an inertness claim
    // checked only on the ordinary path is an inertness claim checked on the easy half.
    private static List<ScatterErrorPoint> ZeroMadStdDevFallback() =>
        Residuals(0.0, 0.0, 0.0, 0.0, 0.0, 5.0);

    // The `caboose` shape, reconstructed from that run's own wave-14 report for round 1 (N = 7, Grubbs limit
    // 2.020; pos 7275 rejected at z ~1604.8 on a scale of 1.483e-5). Its 18 detected stars put the rejected
    // point at s = 0.0238 * sqrt(18) = 0.10097 — the 0.1010 the wave-14 SEM audit recorded, and the number the
    // V0.07 and V1.00 rungs are placed either side of.
    private const double CabooseStars = 18.0;

    private const double CabooseSemOfRejected = 0.10097484835343898;

    private static List<ScatterErrorPoint> CabooseRoundOne() {
        var r = new[] { 0.0012, -0.0238, -3e-6, 6e-6, -1e-6, 0.0082, -1.1e-5 };
        var pos = new[] { 7350.0, 7275.0, 7200.0, 7125.0, 7050.0, 6975.0, 6900.0 };
        return pos.Select((x, i) => new ScatterErrorPoint(x, r[i], 0.0, 1.0)).ToList();
    }

    // Two points clear the Grubbs limit and they disagree about which is worse once N* is applied:
    //   index 5: r = 0.60, N* =   1  ->  z = 19.89 (the ARGMAX today),  s =  0.6
    //   index 6: r = 0.50, N* = 400  ->  z = 16.52,                     s = 10.0
    // Under family R the statistic runs on r*sqrt(N*) and index 6 wins at z = 168.2. This one set therefore
    // supports both load-bearing claims: family V must NEVER move the choice to index 6, and family R must.
    private static List<ScatterErrorPoint> RankDisagreement() =>
        Residuals(0.0, 0.01, -0.01, 0.02, -0.02, 0.60, 0.50);

    private static Func<double, double> RankDisagreementCounts() => Counts(4, 4, 4, 4, 4, 1, 400);

    // ---- 1. the control rung ----

    [Test]
    public void TheDefaultSpec_ReproducesTheShippedOverloadsExactly() {
        // PRE-CHANGE: does not compile (no nine-argument overload). MUTANT KILLED — M5: keying the rescale off
        // the SIDE MAP rather than off the family, i.e. `if (starCounts != null) errors[i] *= sqrt(N*)`. That
        // mutant is inert only as long as nobody wires the map up, and clause W1 is the only clause in the wave
        // that could ever catch it. Includes the degenerate-MAD stdDev-fallback set, because an inertness claim
        // checked only where the MAD is healthy is checked on the easy half.
        foreach (var build in new Func<List<ScatterErrorPoint>>[] { GrossOutlier, ZeroMadStdDevFallback, CabooseRoundOne }) {
            var shipped = MathUtility.RejectionTest(build(), Zero, Confidence);
            var wave14 = MathUtility.RejectionTest(build(), Zero, Confidence, weights: null, scaleFloor: 0.0, scaleUsed: out var wave14Scale);
            var defaulted = MathUtility.RejectionTest(build(), Zero, Confidence, null, 0.0,
                SemScaleSpec.None, starCounts: null, out var defaultedScale, out var semNoCounts);
            // The same default, but WITH the side map wired up: the sem is then reported, and nothing else moves.
            var withCounts = MathUtility.RejectionTest(build(), Zero, Confidence, null, 0.0,
                SemScaleSpec.None, ConstantCounts(9.0), out var withCountsScale, out var semWithCounts);

            Assert.Multiple(() => {
                Assert.That(defaulted?.X, Is.EqualTo(shipped?.X),
                    "at SemScaleSpec.None the verdict must be the shipped verdict, not a numerically similar one");
                Assert.That(defaulted?.Y, Is.EqualTo(shipped?.Y));
                Assert.That(defaultedScale, Is.EqualTo(wave14Scale), "and the effective scale must be bit-identical to wave 14's");
                Assert.That(defaulted?.X, Is.EqualTo(wave14?.X));
                Assert.That(double.IsNaN(semNoCounts), Is.True, "with no star counts there is no s to report");

                Assert.That(withCounts?.X, Is.EqualTo(shipped?.X),
                    "supplying the counts at the default rung must change the DECISION not at all — it only adds a number to report");
                Assert.That(withCountsScale, Is.EqualTo(wave14Scale));
                Assert.That(semWithCounts, Is.EqualTo(3.0 * Math.Abs(shipped?.Y ?? double.NaN)).Within(1e-12),
                    "and that number is |r| * sqrt(9) for the selected point");
            });
        }
    }

    [Test]
    public void TheReportedSem_IsNaN_WhenNoTestWasPerformed() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: initialising semOfSelected to 0.0 instead of NaN. "No test
        // was performed" must not be reportable as "s was zero" — zero is the one value that would make every
        // veto rung fire, so the absence would read as the strongest possible measurement.
        var tooFew = Residuals(0.0, 1.0, 2.0);                       // <= 3 points: the test never runs
        var perfect = Residuals(1.0, 1.0, 1.0, 1.0, 1.0, 1.0);       // MAD 0 AND stdDev 0: degenerate after both guards

        var a = MathUtility.RejectionTest(tooFew, Zero, Confidence, null, 0.0,
            SemScaleSpec.Veto(1.0), ConstantCounts(16.0), out var scaleTooFew, out var semTooFew);
        var b = MathUtility.RejectionTest(perfect, Zero, Confidence, null, 0.0,
            SemScaleSpec.Veto(1.0), ConstantCounts(16.0), out var scalePerfect, out var semPerfect);

        Assert.Multiple(() => {
            Assert.That(a, Is.Null);
            Assert.That(b, Is.Null);
            Assert.That(double.IsNaN(scaleTooFew), Is.True);
            Assert.That(double.IsNaN(scalePerfect), Is.True);
            Assert.That(double.IsNaN(semTooFew), Is.True, "too few points: no point was ever selected");
            Assert.That(double.IsNaN(semPerfect), Is.True, "degenerate after both guards: no point was ever selected");
        });
    }

    // ---- 2. a live spec with no star counts is a loud failure ----

    [Test]
    public void ASemSpecWithoutStarCounts_Throws_AndDoesNotQuietlyRunTheControl() {
        // PRE-CHANGE: does not compile. MUTANT KILLED — M3: `if (semScale.IsNone || starCounts == null) { run the
        // control }`, i.e. treating a missing side map as "no criterion". That is F66's shape at the API
        // boundary: an arm would run the control rung, print nothing unusual, and be written up as a SEM
        // measurement that changed nothing.
        Assert.Multiple(() => {
            Assert.That(() => MathUtility.RejectionTest(GrossOutlier(), Zero, Confidence, null, 0.0,
                    SemScaleSpec.Veto(1.0), null, out _, out _),
                Throws.TypeOf<InvalidOperationException>(), "family V with no counts must throw");
            Assert.That(() => MathUtility.RejectionTest(GrossOutlier(), Zero, Confidence, null, 0.0,
                    SemScaleSpec.Rank(), null, out _, out _),
                Throws.TypeOf<InvalidOperationException>(), "family R with no counts must throw");

            // ... and the check must not be a blanket "counts are required", which would break every production
            // call site in the plugin.
            Assert.That(() => MathUtility.RejectionTest(GrossOutlier(), Zero, Confidence, null, 0.0,
                    SemScaleSpec.None, null, out _, out _),
                Throws.Nothing, "the default rung has no use for counts and must not demand them");
        });
    }

    // ---- 3. family V can only suppress ----

    [TestCaseSource(nameof(SuppressionOnlyCases))]
    public void FamilyV_CanOnlySuppress_NeverRedirect(string name, Func<List<ScatterErrorPoint>> build, Func<double, double> counts) {
        // Sweeping the veto threshold upward on a fixed point set, the rejected point is either the SAME point or
        // none — never a different point — and once a rung goes silent no larger rung fires again. That prefix
        // property is what makes the veto rungs eligible for the wave's counterfactual gate at all.
        //
        // PRE-CHANGE: does not compile. MUTANTS KILLED — M1: making the SEM comparison the DECISION rather than a
        // veto (`if (IsVeto) return sem >= t ? points[argmax] : null;` ahead of the Grubbs comparison), which
        // lets a threshold CREATE a rejection on a round the Grubbs test cleared. M-V2: applying the veto per
        // POINT before the argmax (dropping every point with s < t from the competition), which redirects the
        // rejection to the runner-up — on the `rank disagreement` set below, straight to index 6.
        var thresholds = new[] { 1e-9, 1e-6, 1e-3, 0.05, 0.07, 0.1, 0.5, 1.0, 2.0, 4.0, 50.0, 1e6 };
        var baseline = MathUtility.RejectionTest(build(), Zero, Confidence, null, 0.0,
            SemScaleSpec.None, counts, out _, out var baselineSem);
        Assume.That(baseline, Is.Not.Null, $"{name} must fire at the control rung or it proves nothing");

        bool wentSilentAt = false;
        Assert.Multiple(() => {
            foreach (var t in thresholds) {
                var rejected = MathUtility.RejectionTest(build(), Zero, Confidence, null, 0.0,
                    SemScaleSpec.Veto(t), counts, out _, out var sem);

                Assert.That(sem, Is.EqualTo(baselineSem),
                    $"{name}: the reported s is a property of the point, not of the rung (t = {t})");
                if (rejected == null) {
                    Assert.That(t, Is.GreaterThan(baselineSem),
                        $"{name}: a veto below the selected point's s must not suppress anything (t = {t})");
                    wentSilentAt = true;
                    continue;
                }
                Assert.That(wentSilentAt, Is.False,
                    $"{name}: a rung that fires after a lower rung went silent breaks the prefix property (t = {t})");
                Assert.That(rejected.X, Is.EqualTo(baseline.X),
                    $"{name}: a veto may suppress the rejection but must never redirect it to another point (t = {t})");
            }
            Assert.That(wentSilentAt, Is.True, $"{name}: the sweep must reach a threshold that DOES suppress, or it proves nothing");
        });
    }

    [Test]
    public void AVeto_CannotCreateARejection_OnARoundTheGrubbsTestCleared() {
        // The other half of "only suppresses", and the half a sweep over a firing set cannot see: on a round
        // where nothing clears the Grubbs limit, NO veto threshold may produce a rejection — not even one far
        // below the selected point's s, where the veto's own condition is satisfied.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED — M1: making the SEM comparison the decision instead of a
        // veto, `if (IsVeto) return sem >= t ? points[argmax] : null;` placed ahead of the Grubbs comparison.
        // On every set that already fires, M1 is indistinguishable from the correct code — which is exactly why
        // the sweep above cannot catch it and this test exists.
        var quiet = Residuals(0.0, 0.01, -0.01, 0.02, -0.02, 0.015, -0.015); // max z = 0.899 against a limit of 2.020
        var counts = ConstantCounts(9.0);

        var control = MathUtility.RejectionTest(quiet, Zero, Confidence, null, 0.0,
            SemScaleSpec.None, counts, out _, out var controlSem);
        Assume.That(control, Is.Null, "the control must find nothing here, or the test proves nothing");
        Assume.That(controlSem, Is.EqualTo(0.06).Within(1e-12));

        Assert.Multiple(() => {
            foreach (var t in new[] { 1e-9, 1e-3, 0.01, 0.05, 0.06, 0.1, 1.0, 1e6 }) {
                var rejected = MathUtility.RejectionTest(quiet, Zero, Confidence, null, 0.0,
                    SemScaleSpec.Veto(t), counts, out _, out var sem);
                Assert.That(rejected, Is.Null,
                    $"a veto is not a criterion: at t = {t} it may only take a rejection away, never make one");
                Assert.That(sem, Is.EqualTo(0.06).Within(1e-12),
                    $"and the point it looked at is reported either way (t = {t})");
            }
        });
    }

    private static IEnumerable<TestCaseData> SuppressionOnlyCases() {
        yield return new TestCaseData("gross outlier", (Func<List<ScatterErrorPoint>>)GrossOutlier, ConstantCounts(9.0));
        yield return new TestCaseData("zero-MAD stdDev fallback", (Func<List<ScatterErrorPoint>>)ZeroMadStdDevFallback, ConstantCounts(16.0));
        yield return new TestCaseData("caboose round 1", (Func<List<ScatterErrorPoint>>)CabooseRoundOne, ConstantCounts(CabooseStars));
        yield return new TestCaseData("rank disagreement", (Func<List<ScatterErrorPoint>>)RankDisagreement, RankDisagreementCounts());
    }

    // ---- 4. the named case, at the two rungs it sits between ----

    [Test]
    public void TheCabooseShape_SurvivesTheV007Rung_AndIsSuppressedAtV100() {
        // The run this whole sub-question is named after, and the reason the ladder has a rung at 0.07 as well as
        // at 1.00: its rejected point sits 2.4% of its own error bar from the curve, is thrown out at z ~1605,
        // and in SEM units is 0.101 — a tenth of one standard error of the quantity being fitted. The two rungs
        // are placed either side of that number, so this single case decides S16-A(a) and S16-D between them.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: comparing s against t with `<=` instead of `<` would not
        // show up here, but comparing the SCALED residual (or |r| without the sqrt(N*) factor) against t would:
        // |r| alone is 0.0238, which is below BOTH rungs, so the 0.07 rung would silence a run it must not.
        var counts = ConstantCounts(CabooseStars);

        var control = MathUtility.RejectionTest(CabooseRoundOne(), Zero, Confidence, null, 0.0,
            SemScaleSpec.None, counts, out var controlScale, out var controlSem);
        var v007 = MathUtility.RejectionTest(CabooseRoundOne(), Zero, Confidence, null, 0.0,
            SemScaleSpec.Veto(0.07), counts, out _, out _);
        var v100 = MathUtility.RejectionTest(CabooseRoundOne(), Zero, Confidence, null, 0.0,
            SemScaleSpec.Veto(1.00), counts, out _, out _);

        Assert.Multiple(() => {
            Assert.That(controlScale, Is.EqualTo(1.483e-5).Within(1e-9), "the reconstruction must land on the stored report's scale");
            Assert.That(control?.X, Is.EqualTo(7275.0), "the same focuser position the stored report rejects");
            Assert.That(controlSem, Is.EqualTo(CabooseSemOfRejected).Within(1e-12),
                "s = 0.0238 * sqrt(18) = 0.10097, the value the wave-14 SEM audit recorded for this rejection");

            Assert.That(v007?.X, Is.EqualTo(7275.0), "0.101 >= 0.07, so the V0.07 rung leaves this rejection standing");
            Assert.That(v100, Is.Null, "0.101 < 1.00, so the named criterion suppresses it");
        });
    }

    // ---- 5. family R ----

    [Test]
    public void FamilyR_WithAConstantStarCount_IsAnIdentity() {
        // z is scale-invariant: multiplying every residual by the same sqrt(N*) multiplies the median and the MAD
        // by it too. So a constant count MUST leave the verdict exactly where it was — this is a theorem, and a
        // test of it is a test that the rescale went where the arithmetic says it goes.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: rescaling the DEVIATIONS |errors - median| rather than the
        // errors themselves (the median then stays unscaled and the invariance is destroyed), and rescaling the
        // effective scale in the opposite direction.
        foreach (var n in new[] { 1.0, 9.0, 400.0 }) {
            var control = MathUtility.RejectionTest(RankDisagreement(), Zero, Confidence, null, 0.0,
                SemScaleSpec.None, ConstantCounts(n), out var controlScale, out _);
            var ranked = MathUtility.RejectionTest(RankDisagreement(), Zero, Confidence, null, 0.0,
                SemScaleSpec.Rank(), ConstantCounts(n), out var rankedScale, out _);

            Assert.Multiple(() => {
                Assert.That(ranked?.X, Is.EqualTo(control?.X), $"N* = {n} is constant, so the ranking cannot change");
                Assert.That(rankedScale, Is.EqualTo(controlScale * Math.Sqrt(n)).Within(1e-12),
                    $"N* = {n}: the effective scale carries the factor, which is exactly why z does not");
            });
        }
    }

    [Test]
    public void FamilyR_WithAVaryingStarCount_MOVES_TheRejection() {
        // The measurement only family R can produce, and the reason it is EXCLUDED from the subset gate and from
        // RECOMMEND by pre-registration: with N* varying inside a round, re-ranking is not monotone and the test
        // selects a point the shipped statistic never selects. If this could not be demonstrated, the exclusion
        // would be an assumption rather than a finding — so it is demonstrated.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED — M2: applying the sqrt(N*) factor anywhere it cannot
        // change the ranking (to the round's SCALE after MedianMAD, or dropped altogether), which makes family R
        // a uniform no-op. Every other test in this file passes happily under that mutant: R-INERT and
        // "R is broken" are indistinguishable without this one.
        var counts = RankDisagreementCounts();

        var control = MathUtility.RejectionTest(RankDisagreement(), Zero, Confidence, null, 0.0,
            SemScaleSpec.None, counts, out _, out var controlSem);
        var ranked = MathUtility.RejectionTest(RankDisagreement(), Zero, Confidence, null, 0.0,
            SemScaleSpec.Rank(), counts, out _, out var rankedSem);

        Assert.Multiple(() => {
            Assert.That(control?.X, Is.EqualTo(5.0), "unranked, the worst point is index 5: |r| = 0.60 at z = 19.9");
            Assert.That(controlSem, Is.EqualTo(0.6).Within(1e-12), "and in SEM units it is a mere 0.6 (N* = 1)");

            Assert.That(ranked?.X, Is.EqualTo(6.0),
                "ranked in SEM units the worst point is index 6: 0.50 * sqrt(400) = 10, at z = 168");
            Assert.That(rankedSem, Is.EqualTo(10.0).Within(1e-12));
            Assert.That(ranked.X, Is.Not.EqualTo(control.X),
                "family R REDIRECTS — which is the whole reason the subset lemma is not proved for it");
        });
    }

    // ---- 6. the reported s ----

    [Test]
    public void SemOfSelected_IsTheUnscaledSemOfTheSelectedPoint_UnderBothFamilies() {
        // The printed s must mean the same thing at every rung of the ladder, so that wave 14's numbers, the
        // veto rungs' and family R's are all on one axis. Family R is the case that can go wrong: its residuals
        // ALREADY carry sqrt(N*), so a report built from them double-counts.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED — M4: `semOfSelected = |errors[argmax]| * sqrt(N*)`, i.e.
        // measuring the SCALED residual. Identical to the correct value at None and at family V (where the two
        // arrays are the same object) and wrong by a factor of sqrt(N*) = 20 at family R — which is why the R row
        // below is not decoration.
        var counts = RankDisagreementCounts();

        MathUtility.RejectionTest(RankDisagreement(), Zero, Confidence, null, 0.0,
            SemScaleSpec.None, counts, out _, out var semNone);
        MathUtility.RejectionTest(RankDisagreement(), Zero, Confidence, null, 0.0,
            SemScaleSpec.Veto(0.5), counts, out _, out var semVetoKept);
        MathUtility.RejectionTest(RankDisagreement(), Zero, Confidence, null, 0.0,
            SemScaleSpec.Veto(1.0), counts, out _, out var semVetoSuppressed);
        MathUtility.RejectionTest(RankDisagreement(), Zero, Confidence, null, 0.0,
            SemScaleSpec.Rank(), counts, out _, out var semRank);

        // The Grubbs test finds nothing at all: the point it LOOKED at is still reported, so a KEPT-nothing round
        // has a number to print rather than a blank the next wave has to reconstruct.
        var quiet = MathUtility.RejectionTest(Residuals(0.0, 0.01, -0.01, 0.02, -0.02, 0.015, -0.015), Zero,
            Confidence, null, 0.0, SemScaleSpec.Veto(1.0), ConstantCounts(9.0), out _, out var semQuiet);

        Assert.Multiple(() => {
            Assert.That(semNone, Is.EqualTo(0.6).Within(1e-12), "index 5: |r| = 0.6, N* = 1");
            Assert.That(semVetoKept, Is.EqualTo(0.6).Within(1e-12), "a veto that does not fire cannot change the number it judged");
            Assert.That(semVetoSuppressed, Is.EqualTo(0.6).Within(1e-12), "and neither can one that does");
            Assert.That(semRank, Is.EqualTo(10.0).Within(1e-12),
                "index 6 UNSCALED: |r| = 0.5 times sqrt(400) = 10 — not 0.5 * 400 = 200, which is what measuring the ranked residual would report");

            Assert.That(quiet, Is.Null, "nothing clears the Grubbs limit on this set");
            Assert.That(semQuiet, Is.Not.NaN, "and the point the test selected is still reported");
            Assert.That(semQuiet, Is.EqualTo(0.06).Within(1e-12), "|r| = 0.02 at index 3 or 4, times sqrt(9)");
        });
    }

    // ---- 7. the parameter object ----

    [Test]
    public void SemScaleSpec_MakesTheTwoFamiliesMutuallyExclusive() {
        // The families are pre-registered as alternatives — one of them is monotone and one is not, and they are
        // gated differently because of it. A type that could hold both would let an arm run a combination nobody
        // named while its report claimed one of the two.
        //
        // PRE-CHANGE: does not compile (the type does not exist). MUTANT KILLED: replacing the private
        // constructor + factories with a public (double, bool) constructor, which makes "veto AND rank"
        // representable again.
        var v = SemScaleSpec.Veto(1.0);
        var r = SemScaleSpec.Rank();

        Assert.Multiple(() => {
            Assert.That(v.IsVeto, Is.True);
            Assert.That(v.IsRank, Is.False);
            Assert.That(v.IsNone, Is.False);
            Assert.That(v.VetoThreshold, Is.EqualTo(1.0));

            Assert.That(r.IsRank, Is.True);
            Assert.That(r.IsVeto, Is.False);
            Assert.That(r.IsNone, Is.False);
            Assert.That(r.VetoThreshold, Is.EqualTo(0.0), "family R has no threshold, and must not pretend to");

            Assert.That(v, Is.Not.EqualTo(r));
            Assert.That(SemScaleSpec.Veto(1.0), Is.EqualTo(v), "value equality, so a spec can be asserted on");
            Assert.That(v == SemScaleSpec.Veto(1.0), Is.True);
            Assert.That(v != r, Is.True);
        });
    }

    [Test]
    public void SemScaleSpec_ZeroIsTheControlRung_AndNonsenseRungsThrow() {
        // t = 0 is a rung on the ladder, and it must be indistinguishable from "no criterion" — otherwise the
        // control rung is a seventh measurement rather than a reproduction of the previous wave.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: `Veto(0.0)` returning a spec whose IsNone is false, which
        // would make the control rung print a SEM header line and a SEM detail line per round, and break the
        // byte comparison those omissions exist to pass.
        Assert.Multiple(() => {
            Assert.That(SemScaleSpec.Veto(0.0), Is.EqualTo(SemScaleSpec.None));
            Assert.That(SemScaleSpec.None.IsNone, Is.True);
            Assert.That(default(SemScaleSpec).IsNone, Is.True,
                "the C# default must be 'no criterion', because that is what every production call site passes");
            Assert.That(SemScaleSpec.None.IsVeto, Is.False);
            Assert.That(SemScaleSpec.None.IsRank, Is.False);

            Assert.That(() => SemScaleSpec.Veto(-0.1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => SemScaleSpec.Veto(double.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => SemScaleSpec.Veto(double.PositiveInfinity), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void SemScaleSpec_NamesItsOwnRung_InAscii() {
        // A report that does not name its own rung is a report that will be quoted at the wrong one. ASCII is not
        // a style preference here: a Unicode arrow printed elsewhere in this harness reaches a redirected log as
        // the byte 0x1A, and a scorer written against the source string then reports "could not look" on the
        // entire population.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: a description that names the value but not the FAMILY, or
        // one that spells the family with a non-ASCII flourish.
        Assert.Multiple(() => {
            Assert.That(SemScaleSpec.None.ToString(), Does.Contain("none"));
            Assert.That(SemScaleSpec.Veto(1.0).ToString(), Does.Contain("family V").And.Contain("1"));
            Assert.That(SemScaleSpec.Veto(0.07).ToString(), Does.Contain("0.07"));
            Assert.That(SemScaleSpec.Rank().ToString(), Does.Contain("family R"));

            foreach (var s in new[] { SemScaleSpec.None.ToString(), SemScaleSpec.Veto(0.07).ToString(), SemScaleSpec.Rank().ToString() }) {
                Assert.That(s.All(c => c < 128), Is.True, $"'{s}' must be ASCII: a redirected log cannot encode anything else");
            }
        });
    }
}
