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
using System;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// <c>af-fit</c>'s MAD-floor flags (F45(b)). Two pre-registered families, and the command line must be unable to
/// name both, unable to name one by accident, and unable to name one and run the other.
///
/// <para>The prefix-matching half of this is the same lesson <c>LandingWritebackTests</c> recorded for
/// <c>--update-run-folder</c>: a <c>StartsWith</c> match would let a future <c>--mad-floor-never</c> switch the
/// floor ON, and the resulting arm would report a rung it did not run. That defect is invisible in the output —
/// which is precisely why it is pinned here rather than left to a reading of the parser.</para>
///
/// <para><b>Against the pre-change source every test in this file fails to compile</b>: <c>MadFloorArgs</c> and
/// <see cref="MadFloorSpec"/> do not exist. Each test names the narrower mutant it kills.</para>
/// </summary>
[TestFixture]
public class MadFloorArgsTests {

    private static string[] Cmd(params string[] args) => args;

    [Test]
    public void NoFlags_IsTheControlRung() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: any default other than None — the ordinary invocation
        // must produce a report byte-for-byte identical to the previous wave's, and that starts here.
        Assert.Multiple(() => {
            Assert.That(MadFloorArgs.Parse(Cmd("af-fit", "--af-run", @"D:\Autofocus Bank\caboose", "--out", @"D:\x")),
                Is.EqualTo(MadFloorSpec.None), "the exact invocation the previous wave used must still mean 'no floor'");
            Assert.That(MadFloorArgs.Parse(null), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorArgs.Parse(Array.Empty<string>()), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorArgs.CaptionTag(MadFloorSpec.None), Is.EqualTo(string.Empty),
                "and it must add nothing at all to a caption");
        });
    }

    [Test]
    public void TheFlagsAreMatchedExactly_NotByPrefix() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: StartsWith/Contains matching in TryFindValue. Note the
        // last two cases in particular: they read like OPT-OUTS, so a prefix match would turn a flag whose author
        // meant "definitely not" into the strongest possible opt-in.
        Assert.Multiple(() => {
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "0.25")), Is.EqualTo(MadFloorSpec.Absolute(0.25)));
            Assert.That(MadFloorArgs.Parse(Cmd("--MAD-FLOOR", "0.25")), Is.EqualTo(MadFloorSpec.Absolute(0.25)),
                "flags elsewhere in this harness are case-insensitive; these must not be the exception");

            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor-never", "0.25")), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorArgs.Parse(Cmd("--no-mad-floor", "0.25")), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor-relative", "0.5")), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorArgs.Parse(Cmd("--round1-floor-disabled", "0.5")), Is.EqualTo(MadFloorSpec.None));
        });
    }

    [Test]
    public void NamingTwoNonZeroRungs_IsAnError_NotAPrecedenceRule() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: a silent "last one wins" or "absolute wins". The families
        // are pre-registered as alternatives; an arm that quietly picked one of two named rungs would publish a
        // number under the wrong rung's name, and nothing in the artifact would show it.
        Assert.Multiple(() => {
            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor", "0.25", "--mad-floor-rel", "0.5")),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor-rel", "0.5", "--mad-floor", "0.25")),
                Throws.TypeOf<ArgumentException>(), "and the error must not depend on the order they were typed");
            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor", "0.25", "--round1-floor", "0.5")),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor-rel", "0.5", "--round1-floor", "0.5")),
                Throws.TypeOf<ArgumentException>(), "naming the same family twice is still two names for one rung");
        });
    }

    [Test]
    public void TheDriversOwnCommandLine_NamesExactlyOneRung() {
        // The wave's driver (affit_w14.sh) passes BOTH flags on every arm, with the inactive family pinned to
        // zero — including on the control rung, which it spells `--mad-floor 0.00 --round1-floor 0.0`. A zero is
        // "this family contributes no floor", so exactly one rung is named and there is nothing to choose
        // between: accepting it is not a precedence rule. These are the four command lines the wave will
        // actually issue, quoted verbatim from the driver.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED — and this one is a wave-stopper: rejecting ANY two floor
        // flags outright. That mutant makes every single arm of Stage B exit 2 before it detects a star, and the
        // failure would look like a harness bug rather than a flag-contract disagreement.
        Assert.Multiple(() => {
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "0.00", "--round1-floor", "0.0")),
                Is.EqualTo(MadFloorSpec.None), "family A rung 0.00 — the control, and it must be indistinguishable from no flags");
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "0.25", "--round1-floor", "0.0")),
                Is.EqualTo(MadFloorSpec.Absolute(0.25)), "family A rung 0.25");
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "1.00", "--round1-floor", "0.0")),
                Is.EqualTo(MadFloorSpec.Absolute(1.0)), "family A rung 1.00");
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "0.0", "--round1-floor", "0.50")),
                Is.EqualTo(MadFloorSpec.RelativeToFirstRound(0.5)), "family B rung 0.50");
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "0.0", "--round1-floor", "1.00")),
                Is.EqualTo(MadFloorSpec.RelativeToFirstRound(1.0)), "family B rung 1.00");
        });
    }

    [Test]
    public void AFlagPinnedToZero_IsStillSyntaxChecked() {
        // The inactive family's value is still parsed, so a typo on the switched-off flag fails loudly instead
        // of being skipped as "it was zero anyway" — which it demonstrably was not.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: short-circuiting on the first non-zero flag and never
        // looking at the rest, which would let `--mad-floor 0.25 --round1-floor banana` run happily.
        Assert.Multiple(() => {
            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor", "banana", "--round1-floor", "0.5")),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor", "0.25", "--round1-floor", "banana")),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor", "0.0", "--round1-floor", "-0.5")),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void FamilyB_AnswersToBothSpellingsTheWaveDocumentsUsed() {
        // The design (§2.8) and the plan both spell family B's flag --round1-floor; the Stage-B build
        // instruction spells it --mad-floor-rel. A driver script written against either must reach the SAME
        // rung: the failure mode of getting this wrong is not an error message, it is an arm that silently runs
        // the control and is then reported as a family-B measurement.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: supporting only one spelling.
        Assert.Multiple(() => {
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor-rel", "0.5")), Is.EqualTo(MadFloorSpec.RelativeToFirstRound(0.5)));
            Assert.That(MadFloorArgs.Parse(Cmd("--round1-floor", "0.5")), Is.EqualTo(MadFloorSpec.RelativeToFirstRound(0.5)));
            Assert.That(MadFloorArgs.Parse(Cmd("--round1-floor", "1.0")), Is.EqualTo(MadFloorSpec.RelativeToFirstRound(1.0)));
        });
    }

    [Test]
    public void ZeroIsTheControlRung_HoweverItIsSpelled() {
        // f = 0.00 is a rung on the ladder AND it is the control. An arm invoked as `--mad-floor 0.00` must
        // produce exactly what an arm invoked with no flag produces, or the control is not a control.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: Parse returning a non-None spec for a zero value, which
        // would put a "MAD floor" line into the control rung's report and break the byte comparison it exists
        // to pass.
        Assert.Multiple(() => {
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "0")), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "0.00")), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor-rel", "0.0")), Is.EqualTo(MadFloorSpec.None));
            Assert.That(MadFloorArgs.CaptionTag(MadFloorArgs.Parse(Cmd("--mad-floor", "0.00"))), Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void ValuesAreParsedInvariantly_AndNonsenseIsRejectedLoudly() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: culture-sensitive double.Parse (which reads "0.25" as 25
        // under a comma-decimal locale — a 100x rung nobody named), and any silent fallback to 0 on a bad value,
        // which would run the control while the command line says otherwise.
        Assert.Multiple(() => {
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "0.25")).AbsoluteFloor, Is.EqualTo(0.25));
            Assert.That(MadFloorArgs.Parse(Cmd("--mad-floor", "1e-3")).AbsoluteFloor, Is.EqualTo(0.001));

            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor", "banana")), Throws.TypeOf<ArgumentException>());
            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor", "-0.25")), Throws.TypeOf<ArgumentException>(),
                "a negative floor is not a floor");
            Assert.That(() => MadFloorArgs.Parse(Cmd("--mad-floor-rel", "-1")), Throws.TypeOf<ArgumentException>());
            Assert.That(() => MadFloorArgs.Parse(Cmd("--af-run", @"D:\x", "--mad-floor")), Throws.TypeOf<ArgumentException>(),
                "a flag typed with no value must fail, not read as absent — 'the flag was typed and the arm ran the control' is the exact silent miss this item is measuring");
        });
    }

    [Test]
    public void TheRungNamesItselfInTheWordsTheReportsQuote() {
        // A report that does not name its own rung is a report that will be quoted at the wrong one. The caption
        // tag is what puts the rung on the table that carries the numbers, not just in a header a reader scrolls
        // past.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: a description that names the value but not the FAMILY —
        // "0.5" alone is ambiguous between f = 0.5 and alpha = 0.5, which are different rungs on different
        // ladders.
        Assert.Multiple(() => {
            Assert.That(MadFloorArgs.Describe(MadFloorSpec.Absolute(0.25)), Does.Contain("family A").And.Contain("0.25"));
            Assert.That(MadFloorArgs.Describe(MadFloorSpec.RelativeToFirstRound(0.5)), Does.Contain("family B").And.Contain("0.5"));
            Assert.That(MadFloorArgs.Describe(MadFloorSpec.None), Does.Contain("none"));

            Assert.That(MadFloorArgs.CaptionTag(MadFloorSpec.Absolute(0.25)), Does.StartWith(" [MAD floor:").And.Contain("family A"));
            Assert.That(MadFloorArgs.CaptionTag(MadFloorSpec.RelativeToFirstRound(1.0)), Does.Contain("family B"));
        });
    }
}
