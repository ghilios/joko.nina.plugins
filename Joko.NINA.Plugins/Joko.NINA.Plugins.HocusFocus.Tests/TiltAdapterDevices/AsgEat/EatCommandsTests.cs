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
}
