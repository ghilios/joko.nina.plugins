#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices.AsgEat;

[TestFixture]
public class EatCommandsTests {

    // Expected strings below are hard-coded literals copied from the plan/design mnemonic table, NOT
    // computed from the same lookup tables EatCommands.cs uses internally -- that would be tautological
    // and could not catch a wrong mnemonic baked into the implementation.

    // --- SignedArgument encoding: always the positive mnemonic, sign rides on the value. ---

    [TestCase(TiltMoveAxis.DiagonalA, 5, "tr,5")]
    [TestCase(TiltMoveAxis.DiagonalA, -5, "tr,-5")]
    [TestCase(TiltMoveAxis.DiagonalB, 5, "tl,5")]
    [TestCase(TiltMoveAxis.DiagonalB, -5, "tl,-5")]
    [TestCase(TiltMoveAxis.EdgeVertical, 7, "tp,7")]
    [TestCase(TiltMoveAxis.EdgeVertical, -7, "tp,-7")]
    [TestCase(TiltMoveAxis.EdgeHorizontal, 2, "rt,2")]
    [TestCase(TiltMoveAxis.EdgeHorizontal, -2, "rt,-2")]
    [TestCase(TiltMoveAxis.Backfocus, 4, "bf,4")]
    [TestCase(TiltMoveAxis.Backfocus, -4, "bf,-4")]
    public void Format_SignedArgument_ProducesExactWireString(TiltMoveAxis axis, int steps, string expected) {
        var actual = EatCommands.Format(axis, steps, EatSignEncoding.SignedArgument);
        Assert.That(actual, Is.EqualTo(expected));
    }

    // --- OppositeMnemonic encoding: positive -> positive mnemonic + magnitude; negative -> opposite
    // mnemonic + magnitude. Backfocus stays signed even here (no opposite mnemonic exists). ---

    [TestCase(TiltMoveAxis.DiagonalA, 5, "tr,5")]
    [TestCase(TiltMoveAxis.DiagonalA, -5, "bl,5")]
    [TestCase(TiltMoveAxis.DiagonalB, 5, "tl,5")]
    [TestCase(TiltMoveAxis.DiagonalB, -5, "br,5")]
    [TestCase(TiltMoveAxis.EdgeVertical, 7, "tp,7")]
    [TestCase(TiltMoveAxis.EdgeVertical, -7, "bt,7")]
    [TestCase(TiltMoveAxis.EdgeHorizontal, 2, "rt,2")]
    [TestCase(TiltMoveAxis.EdgeHorizontal, -2, "lt,2")]
    [TestCase(TiltMoveAxis.Backfocus, 4, "bf,4")]
    [TestCase(TiltMoveAxis.Backfocus, -4, "bf,-4")]
    public void Format_OppositeMnemonic_ProducesExactWireString(TiltMoveAxis axis, int steps, string expected) {
        var actual = EatCommands.Format(axis, steps, EatSignEncoding.OppositeMnemonic);
        Assert.That(actual, Is.EqualTo(expected));
    }

    // --- The blocking-sign invariant: negative Backfocus is ALWAYS "bf,-N", under BOTH encodings. This is
    // the one sign unknown that is truly blocking (no opposite mnemonic exists for bf on the real device);
    // a future encoding flip (e.g. during T15) must not be able to silently break it. ---

    [TestCase(EatSignEncoding.SignedArgument)]
    [TestCase(EatSignEncoding.OppositeMnemonic)]
    public void Format_NegativeBackfocus_AlwaysUsesSignedArgument_UnderBothEncodings(EatSignEncoding encoding) {
        var actual = EatCommands.Format(TiltMoveAxis.Backfocus, -3, encoding);
        Assert.That(actual, Is.EqualTo("bf,-3"));
    }

    [TestCase(EatSignEncoding.SignedArgument)]
    [TestCase(EatSignEncoding.OppositeMnemonic)]
    public void Format_PositiveBackfocus_UsesBfMnemonic_UnderBothEncodings(EatSignEncoding encoding) {
        var actual = EatCommands.Format(TiltMoveAxis.Backfocus, 3, encoding);
        Assert.That(actual, Is.EqualTo("bf,3"));
    }

    // --- TiltAdapterMove overload delegates to (axis, steps). ---

    [Test]
    public void Format_TiltAdapterMoveOverload_DelegatesToAxisSteps() {
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 5, TiltMoveGroup.Tilt, "tr,5");
        var actual = EatCommands.Format(move, EatSignEncoding.SignedArgument);
        Assert.That(actual, Is.EqualTo("tr,5"));
    }

    [Test]
    public void Format_TiltAdapterMoveOverload_NegativeDiagonalUnderOppositeMnemonic() {
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, -5, TiltMoveGroup.Tilt, "bl,5");
        var actual = EatCommands.Format(move, EatSignEncoding.OppositeMnemonic);
        Assert.That(actual, Is.EqualTo("bl,5"));
    }

    [Test]
    public void Format_TiltAdapterMoveOverload_NullMove_Throws() {
        Assert.Throws<ArgumentNullException>(() => EatCommands.Format(null, EatSignEncoding.SignedArgument));
    }

    [Test]
    public void Format_TiltAdapterMoveOverload_DefaultEncoding_UsesDefaultSignEncoding() {
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, -5, TiltMoveGroup.Tilt, "tr,-5");
        var actual = EatCommands.Format(move);
        Assert.That(actual, Is.EqualTo(EatCommands.DefaultSignEncoding == EatSignEncoding.SignedArgument ? "tr,-5" : "bl,5"));
    }

    // --- Default encoding constant: pinned to SignedArgument per the plan (LIVE-CAPTURE: T15 confirms/flips). ---

    [Test]
    public void DefaultSignEncoding_IsSignedArgument() {
        Assert.That(EatCommands.DefaultSignEncoding, Is.EqualTo(EatSignEncoding.SignedArgument));
    }

    // --- PositionQuery. ---

    [Test]
    public void PositionQuery_ReturnsCp() {
        Assert.That(EatCommands.PositionQuery(), Is.EqualTo("cp"));
    }

    // --- steps == 0: documented behavior is to throw, since the planner never emits a zero-magnitude move
    // and a silent "no-op" wire command would be an ambiguous, untested code path on real hardware. ---

    [TestCase(EatSignEncoding.SignedArgument)]
    [TestCase(EatSignEncoding.OppositeMnemonic)]
    public void Format_ZeroSteps_Throws(EatSignEncoding encoding) {
        Assert.Throws<ArgumentException>(() => EatCommands.Format(TiltMoveAxis.DiagonalA, 0, encoding));
    }

    [Test]
    public void Format_ZeroStepsBackfocus_AlsoThrows() {
        Assert.Throws<ArgumentException>(() => EatCommands.Format(TiltMoveAxis.Backfocus, 0, EatSignEncoding.SignedArgument));
    }

    // --- Culture-invariant wire formatting: negative values must always use ASCII '-' (U+002D), never a
    // culture's alternate negative sign glyph. Under .NET 8's ICU-backed globalization, some cultures format
    // the minus sign as U+2212 (MINUS SIGN) rather than ASCII '-'; if that leaked into a plain culture-
    // sensitive interpolation, the ASG EAT device -- which parses raw wire bytes -- would receive a corrupted
    // command. This test does not rely on any particular CI machine's installed ICU/locale data: it builds an
    // explicit CultureInfo whose NegativeSign is forced to U+2212 and installs it as CurrentCulture for the
    // duration, so the assertion is deterministic everywhere. Against the old ($"{positiveMnemonic},{steps}")
    // formatting this fails (the command would contain U+2212 instead of '-'); it passes once every
    // value-formatting return site uses FormattableString.Invariant. ---

    [Test]
    public void Format_NegativeValues_UseAsciiMinusSign_RegardlessOfCurrentCulture() {
        const string nonAsciiMinusSign = "−"; // U+2212 MINUS SIGN
        var originalCulture = CultureInfo.CurrentCulture;
        try {
            var hostileCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            hostileCulture.NumberFormat.NegativeSign = nonAsciiMinusSign;
            CultureInfo.CurrentCulture = hostileCulture;

            var signedArgument = EatCommands.Format(TiltMoveAxis.DiagonalA, -5, EatSignEncoding.SignedArgument);
            var backfocus = EatCommands.Format(TiltMoveAxis.Backfocus, -3, EatSignEncoding.SignedArgument);

            Assert.Multiple(() => {
                Assert.That(signedArgument, Is.EqualTo("tr,-5"));
                Assert.That(signedArgument, Does.Not.Contain(nonAsciiMinusSign));
                Assert.That(backfocus, Is.EqualTo("bf,-3"));
                Assert.That(backfocus, Does.Not.Contain(nonAsciiMinusSign));
            });
        } finally {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // --- Unknown axis / encoding guard. ---

    [Test]
    public void Format_UnknownAxis_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => EatCommands.Format((TiltMoveAxis)999, 5, EatSignEncoding.SignedArgument));
    }

    [Test]
    public void Format_UnknownEncoding_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => EatCommands.Format(TiltMoveAxis.DiagonalA, 5, (EatSignEncoding)999));
    }

    // --- TryParse: the exact inverse of Format. The strongest guarantee we want is a round-trip, but round-
    // tripping ALONE (parse(format(x)) == x) can't catch a table that is wrong in the SAME way in both
    // directions, so the wire strings fed to TryParse below are the same hard-coded literals used in the
    // Format tests (copied from the mnemonic table), not values produced by Format at test time. ---

    [TestCase("tr,5", TiltMoveAxis.DiagonalA, 5)]
    [TestCase("tr,-5", TiltMoveAxis.DiagonalA, -5)]
    [TestCase("tl,5", TiltMoveAxis.DiagonalB, 5)]
    [TestCase("tl,-5", TiltMoveAxis.DiagonalB, -5)]
    [TestCase("tp,7", TiltMoveAxis.EdgeVertical, 7)]
    [TestCase("tp,-7", TiltMoveAxis.EdgeVertical, -7)]
    [TestCase("rt,2", TiltMoveAxis.EdgeHorizontal, 2)]
    [TestCase("rt,-2", TiltMoveAxis.EdgeHorizontal, -2)]
    [TestCase("bf,4", TiltMoveAxis.Backfocus, 4)]
    [TestCase("bf,-4", TiltMoveAxis.Backfocus, -4)]
    public void TryParse_SignedArgument_DecodesExactMove(string wire, TiltMoveAxis expectedAxis, int expectedSteps) {
        var ok = EatCommands.TryParse(wire, out var axis, out var steps, EatSignEncoding.SignedArgument);
        Assert.Multiple(() => {
            Assert.That(ok, Is.True);
            Assert.That(axis, Is.EqualTo(expectedAxis));
            Assert.That(steps, Is.EqualTo(expectedSteps));
        });
    }

    [TestCase("tr,5", TiltMoveAxis.DiagonalA, 5)]
    [TestCase("bl,5", TiltMoveAxis.DiagonalA, -5)]
    [TestCase("tl,5", TiltMoveAxis.DiagonalB, 5)]
    [TestCase("br,5", TiltMoveAxis.DiagonalB, -5)]
    [TestCase("tp,7", TiltMoveAxis.EdgeVertical, 7)]
    [TestCase("bt,7", TiltMoveAxis.EdgeVertical, -7)]
    [TestCase("rt,2", TiltMoveAxis.EdgeHorizontal, 2)]
    [TestCase("lt,2", TiltMoveAxis.EdgeHorizontal, -2)]
    [TestCase("bf,4", TiltMoveAxis.Backfocus, 4)]
    [TestCase("bf,-4", TiltMoveAxis.Backfocus, -4)]
    public void TryParse_OppositeMnemonic_DecodesExactMove(string wire, TiltMoveAxis expectedAxis, int expectedSteps) {
        var ok = EatCommands.TryParse(wire, out var axis, out var steps, EatSignEncoding.OppositeMnemonic);
        Assert.Multiple(() => {
            Assert.That(ok, Is.True);
            Assert.That(axis, Is.EqualTo(expectedAxis));
            Assert.That(steps, Is.EqualTo(expectedSteps));
        });
    }

    // Round-trip closure over the whole vocabulary under both encodings: TryParse(Format(axis,steps,enc),enc)
    // recovers (axis, steps) exactly. Backfocus is included precisely because it is the one axis whose negative
    // is signed under BOTH encodings.
    [TestCase(TiltMoveAxis.DiagonalA, 5, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.DiagonalA, -5, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.DiagonalB, 12, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.DiagonalB, -12, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.EdgeVertical, 3, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.EdgeVertical, -3, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.EdgeHorizontal, 8, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.EdgeHorizontal, -8, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.Backfocus, 150, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.Backfocus, -150, EatSignEncoding.SignedArgument)]
    [TestCase(TiltMoveAxis.DiagonalA, 5, EatSignEncoding.OppositeMnemonic)]
    [TestCase(TiltMoveAxis.DiagonalA, -5, EatSignEncoding.OppositeMnemonic)]
    [TestCase(TiltMoveAxis.DiagonalB, 12, EatSignEncoding.OppositeMnemonic)]
    [TestCase(TiltMoveAxis.DiagonalB, -12, EatSignEncoding.OppositeMnemonic)]
    [TestCase(TiltMoveAxis.EdgeVertical, 3, EatSignEncoding.OppositeMnemonic)]
    [TestCase(TiltMoveAxis.EdgeVertical, -3, EatSignEncoding.OppositeMnemonic)]
    [TestCase(TiltMoveAxis.EdgeHorizontal, 8, EatSignEncoding.OppositeMnemonic)]
    [TestCase(TiltMoveAxis.EdgeHorizontal, -8, EatSignEncoding.OppositeMnemonic)]
    [TestCase(TiltMoveAxis.Backfocus, 150, EatSignEncoding.OppositeMnemonic)]
    [TestCase(TiltMoveAxis.Backfocus, -150, EatSignEncoding.OppositeMnemonic)]
    public void TryParse_RoundTripsFormat(TiltMoveAxis axis, int steps, EatSignEncoding encoding) {
        var wire = EatCommands.Format(axis, steps, encoding);
        var ok = EatCommands.TryParse(wire, out var parsedAxis, out var parsedSteps, encoding);
        Assert.Multiple(() => {
            Assert.That(ok, Is.True, $"'{wire}' should parse");
            Assert.That(parsedAxis, Is.EqualTo(axis));
            Assert.That(parsedSteps, Is.EqualTo(steps));
        });
    }

    // Default encoding overload parses SignedArgument (pinned to the same DefaultSignEncoding as Format).
    [Test]
    public void TryParse_DefaultEncoding_ParsesSignedArgument() {
        var ok = EatCommands.TryParse("tr,-5", out var axis, out var steps);
        Assert.Multiple(() => {
            Assert.That(ok, Is.True);
            Assert.That(axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
            Assert.That(steps, Is.EqualTo(-5));
        });
    }

    // An opposite-corner mnemonic is not a legal command under SignedArgument (Format never emits one there),
    // so TryParse must reject it rather than silently guessing the axis.
    [Test]
    public void TryParse_OppositeMnemonicUnderSignedArgument_ReturnsFalse() {
        var ok = EatCommands.TryParse("bl,5", out _, out _, EatSignEncoding.SignedArgument);
        Assert.That(ok, Is.False);
    }

    [TestCase("bf,-3", EatSignEncoding.SignedArgument)]
    [TestCase("bf,-3", EatSignEncoding.OppositeMnemonic)]
    public void TryParse_NegativeBackfocus_DecodesSigned_UnderBothEncodings(string wire, EatSignEncoding encoding) {
        var ok = EatCommands.TryParse(wire, out var axis, out var steps, encoding);
        Assert.Multiple(() => {
            Assert.That(ok, Is.True);
            Assert.That(axis, Is.EqualTo(TiltMoveAxis.Backfocus));
            Assert.That(steps, Is.EqualTo(-3));
        });
    }

    [Test]
    public void TryParse_PositionQuery_ReturnsFalse() {
        Assert.That(EatCommands.TryParse("cp", out _, out _), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("tr")]
    [TestCase("tr,")]
    [TestCase("tr,x")]
    [TestCase("zz,5")]
    [TestCase("tr,5,6")]
    [TestCase(",5")]
    public void TryParse_Malformed_ReturnsFalse(string wire) {
        Assert.That(EatCommands.TryParse(wire, out _, out _), Is.False);
    }

    // Whitespace tolerance: mnemonic/value are trimmed, so a padded command still decodes.
    [Test]
    public void TryParse_WithSurroundingWhitespace_Decodes() {
        var ok = EatCommands.TryParse(" tr , 5 ", out var axis, out var steps);
        Assert.Multiple(() => {
            Assert.That(ok, Is.True);
            Assert.That(axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
            Assert.That(steps, Is.EqualTo(5));
        });
    }
}
