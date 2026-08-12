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
using System.Linq;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// <c>af-fit</c>'s SEM flags (wave 16 item A). Two pre-registered families, and the command line must be unable
/// to name both, unable to name one by accident, unable to name one and run the other — and unable to name a SEM
/// rung and a MAD-floor rung at the same time, which would be two mechanisms in one arm.
///
/// <para>The prefix-matching half of this is the same lesson <c>MadFloorArgsTests</c> and
/// <c>LandingWritebackTests</c> recorded: a <c>StartsWith</c> match would let a future <c>--sem-veto-never</c>
/// switch the criterion ON, and the resulting arm would report a rung it did not run. That defect is invisible
/// in the output — which is precisely why it is pinned here rather than left to a reading of the parser.</para>
///
/// <para><b>Against the pre-change source every test in this file fails to compile</b>: <c>SemScaleArgs</c> and
/// <see cref="SemScaleSpec"/> do not exist. Each test names the narrower mutant it kills.</para>
/// </summary>
[TestFixture]
public class SemScaleArgsTests {

    private static string[] Cmd(params string[] args) => args;

    [Test]
    public void NoFlags_IsTheControlRung() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: any default other than None — rung N's report must be
        // byte-for-byte wave 14's control-rung report, and that starts here.
        Assert.Multiple(() => {
            Assert.That(SemScaleArgs.Parse(Cmd("af-fit", "--af-run", @"D:\Autofocus Bank\caboose", "--out", @"D:\x")),
                Is.EqualTo(SemScaleSpec.None), "the exact invocation the previous waves used must still mean 'no criterion'");
            Assert.That(SemScaleArgs.Parse(null), Is.EqualTo(SemScaleSpec.None));
            Assert.That(SemScaleArgs.Parse(Array.Empty<string>()), Is.EqualTo(SemScaleSpec.None));
        });
    }

    [Test]
    public void TheFlagsAreMatchedExactly_NotByPrefix() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: StartsWith/Contains matching. Note the opt-out spellings
        // in particular: a prefix match would turn a flag whose author meant "definitely not" into the strongest
        // possible opt-in, and family R's flag takes no value, so a loose match there needs no argument at all to
        // switch a whole ladder rung on.
        Assert.Multiple(() => {
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "1.00")), Is.EqualTo(SemScaleSpec.Veto(1.0)));
            Assert.That(SemScaleArgs.Parse(Cmd("--SEM-VETO", "1.00")), Is.EqualTo(SemScaleSpec.Veto(1.0)),
                "flags elsewhere in this harness are case-insensitive; these must not be the exception");
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-rank")), Is.EqualTo(SemScaleSpec.Rank()));
            Assert.That(SemScaleArgs.Parse(Cmd("--SEM-RANK")), Is.EqualTo(SemScaleSpec.Rank()));

            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto-never", "1.00")), Is.EqualTo(SemScaleSpec.None));
            Assert.That(SemScaleArgs.Parse(Cmd("--no-sem-veto", "1.00")), Is.EqualTo(SemScaleSpec.None));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-vetoes", "1.00")), Is.EqualTo(SemScaleSpec.None));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-rank-never")), Is.EqualTo(SemScaleSpec.None));
            Assert.That(SemScaleArgs.Parse(Cmd("--no-sem-rank")), Is.EqualTo(SemScaleSpec.None));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-ranking")), Is.EqualTo(SemScaleSpec.None));
        });
    }

    [Test]
    public void NamingBothSemFamilies_IsAnError_NotAPrecedenceRule() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: a silent "last one wins" or "rank wins". The families are
        // pre-registered as alternatives and are GATED DIFFERENTLY — V is monotone and eligible for the subset
        // gate, R is not — so an arm that quietly picked one of two named rungs would publish a number under the
        // wrong rung's name, and under the wrong gate.
        Assert.Multiple(() => {
            Assert.That(() => SemScaleArgs.Parse(Cmd("--sem-veto", "1.00", "--sem-rank")),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => SemScaleArgs.Parse(Cmd("--sem-rank", "--sem-veto", "1.00")),
                Throws.TypeOf<ArgumentException>(), "and the error must not depend on the order they were typed");

            Assert.That(SemScaleArgs.Parse(Cmd("--sem-rank", "--sem-veto", "0")), Is.EqualTo(SemScaleSpec.Rank()),
                "a veto pinned to zero names no rung, so this is family R named once");
        });
    }

    [Test]
    public void NamingASemRungAndAMadFloorRung_IsAnError() {
        // Two answers to the same question, pre-registered as alternatives. An arm applying both measures
        // neither, and its report would quote one of the two.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED — and this one is a wave-stopper in the other direction:
        // rejecting ANY mad-floor flag alongside a SEM flag, including the zero form. Wave 14's driver spells
        // every arm with BOTH floor flags present (`--mad-floor 0.00 --round1-floor 0.0`), so a blanket rejection
        // would exit 2 on a command line that names exactly one rung.
        Assert.Multiple(() => {
            Assert.That(() => SemScaleArgs.Parse(Cmd("--sem-veto", "1.00", "--mad-floor", "0.25")),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => SemScaleArgs.Parse(Cmd("--mad-floor", "0.25", "--sem-veto", "1.00")),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => SemScaleArgs.Parse(Cmd("--sem-rank", "--round1-floor", "0.50")),
                Throws.TypeOf<ArgumentException>());

            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "1.00", "--mad-floor", "0.00", "--round1-floor", "0.0")),
                Is.EqualTo(SemScaleSpec.Veto(1.0)), "a floor pinned to zero names no rung, so exactly one rung is named");
            Assert.That(SemScaleArgs.Parse(Cmd("--mad-floor", "0.25")), Is.EqualTo(SemScaleSpec.None),
                "and a MAD-floor arm on its own is simply not a SEM arm");
        });
    }

    [Test]
    public void TheDriversOwnCommandLine_NamesExactlyOneRung() {
        // The six rungs of RULE S16, spelled the way `affit_w16.sh` spells them (`--sem-veto "${RUNG#V}"`, and no
        // SEM flag at all for the control). These are the command lines the wave will actually issue.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: requiring a value for --sem-rank, or requiring the control
        // rung to pass `--sem-veto 0` explicitly. Either makes a whole rung of the ladder exit 2 before it
        // detects a star, and the failure would look like a harness bug rather than a flag-contract disagreement.
        Assert.Multiple(() => {
            Assert.That(SemScaleArgs.Parse(Cmd("--confidence", "0.95", "--weighted", "true")),
                Is.EqualTo(SemScaleSpec.None), "rung N passes no SEM flag at all");
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "0.07")), Is.EqualTo(SemScaleSpec.Veto(0.07)));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "1.00")), Is.EqualTo(SemScaleSpec.Veto(1.0)));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "2.00")), Is.EqualTo(SemScaleSpec.Veto(2.0)));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "4.00")), Is.EqualTo(SemScaleSpec.Veto(4.0)));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-rank")), Is.EqualTo(SemScaleSpec.Rank()));
        });
    }

    [Test]
    public void ZeroIsTheControlRung_HoweverItIsSpelled() {
        // t = 0.00 is a rung on the ladder AND it is the control. An arm invoked as `--sem-veto 0.00` must
        // produce exactly what an arm invoked with no flag produces, or the control is not a control.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: Parse returning a non-None spec for a zero value, which
        // would put a "SEM scale" line into the control rung's report and a SEM detail line into every round —
        // and break the byte comparison those omissions exist to pass.
        Assert.Multiple(() => {
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "0")), Is.EqualTo(SemScaleSpec.None));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "0.00")), Is.EqualTo(SemScaleSpec.None));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "0e0")), Is.EqualTo(SemScaleSpec.None));
        });
    }

    [Test]
    public void ValuesAreParsedInvariantly_AndNonsenseIsRejectedLoudly() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: culture-sensitive double.Parse (which reads "0.07" as 7
        // under a comma-decimal locale — a 100x rung nobody named), and any silent fallback to 0 on a bad value,
        // which would run the control while the command line says otherwise.
        Assert.Multiple(() => {
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "0.07")).VetoThreshold, Is.EqualTo(0.07));
            Assert.That(SemScaleArgs.Parse(Cmd("--sem-veto", "1e-3")).VetoThreshold, Is.EqualTo(0.001));

            Assert.That(() => SemScaleArgs.Parse(Cmd("--sem-veto", "banana")), Throws.TypeOf<ArgumentException>());
            Assert.That(() => SemScaleArgs.Parse(Cmd("--sem-veto", "-1")), Throws.TypeOf<ArgumentException>(),
                "a negative threshold is not a threshold");
            Assert.That(() => SemScaleArgs.Parse(Cmd("--sem-veto", "NaN")), Throws.TypeOf<ArgumentException>());
            Assert.That(() => SemScaleArgs.Parse(Cmd("--af-run", @"D:\x", "--sem-veto")), Throws.TypeOf<ArgumentException>(),
                "a flag typed with no value must fail, not read as absent — 'the flag was typed and the arm ran the control' is the exact silent miss this item is measuring");
        });
    }

    [Test]
    public void TheRungNamesItselfInTheWordsTheReportsQuote_AndInAsciiOnly() {
        // The `SEM detail` line carries the tag, and a scorer reads that line out of a REDIRECTED log. ASCII is
        // therefore a measured requirement, not a style preference: a Unicode arrow printed elsewhere in this
        // harness arrives as the byte 0x1A and takes the whole population's parse with it.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: a tag that names the value but not the family ("1.00"
        // alone is ambiguous between a veto threshold and a MAD floor), a tag containing a space (the scorer's
        // field is \S+, so a space silently truncates it), or one that rounds two rungs to the same name.
        Assert.Multiple(() => {
            Assert.That(SemScaleArgs.Describe(SemScaleSpec.Veto(1.0)), Does.Contain("family V").And.Contain("1"));
            Assert.That(SemScaleArgs.Describe(SemScaleSpec.Rank()), Does.Contain("family R"));
            Assert.That(SemScaleArgs.Describe(SemScaleSpec.None), Does.Contain("none"));

            // The four veto rungs are tagged exactly as the driver names them, so a report cannot be quoted at
            // the wrong rung.
            Assert.That(SemScaleArgs.Tag(SemScaleSpec.None), Is.EqualTo("none"));
            Assert.That(SemScaleArgs.Tag(SemScaleSpec.Rank()), Is.EqualTo("R"));
            Assert.That(SemScaleArgs.Tag(SemScaleSpec.Veto(0.07)), Is.EqualTo("V0.07"));
            Assert.That(SemScaleArgs.Tag(SemScaleSpec.Veto(1.0)), Is.EqualTo("V1.00"));
            Assert.That(SemScaleArgs.Tag(SemScaleSpec.Veto(2.0)), Is.EqualTo("V2.00"));
            Assert.That(SemScaleArgs.Tag(SemScaleSpec.Veto(4.0)), Is.EqualTo("V4.00"));

            // A threshold that two decimals would round away keeps its full precision instead of being tagged as
            // a rung it is not.
            Assert.That(SemScaleArgs.Tag(SemScaleSpec.Veto(0.005)), Is.EqualTo("V0.005"));

            foreach (var spec in new[] { SemScaleSpec.None, SemScaleSpec.Rank(), SemScaleSpec.Veto(0.07), SemScaleSpec.Veto(0.005) }) {
                var tag = SemScaleArgs.Tag(spec);
                Assert.That(tag.All(c => c < 128 && !char.IsWhiteSpace(c)), Is.True,
                    $"the tag '{tag}' must be ASCII with no whitespace: the scorer's field is \\S+ in a redirected log");
                Assert.That(SemScaleArgs.Describe(spec).All(c => c < 128), Is.True);
            }
        });
    }
}
