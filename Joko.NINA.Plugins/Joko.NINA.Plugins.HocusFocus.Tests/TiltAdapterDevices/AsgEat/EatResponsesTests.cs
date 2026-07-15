#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using NINA.Core.Utility.SerialCommunication;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices.AsgEat;

// These tests pin EatResponses' PARSING CONTRACT (tolerant success/failure rules, the four-integer `cp`
// extraction shape, the throw-with-raw-lines-included behavior) -- NOT the real ASG EAT wire format, which
// is unknown until plan task T15's live hardware capture. Every fixture string below is an ASSUMED
// stand-in (// LIVE-CAPTURE:), not a real captured device response; T15 replaces them with real transcripts
// without needing to change ParseMoveAck's or ParseCpPositions' signatures.
[TestFixture]
public class EatResponsesTests {

    // --- ParseMoveAck: tolerant on TimedOut only (no ack/error text recognized yet). ---

    [Test]
    public void ParseMoveAck_NotTimedOut_ReturnsTrue() {
        // LIVE-CAPTURE: "OK" is an assumed placeholder ack line -- the real ack text (if any) is unknown
        // until T15. ParseMoveAck does not currently inspect line content at all; this fixture exists only
        // to show a plausible non-timed-out exchange.
        var exchange = new EatRawExchange("tr,50", new[] { "OK" }, timedOut: false);
        Assert.That(EatResponses.ParseMoveAck(exchange), Is.True);
    }

    [Test]
    public void ParseMoveAck_NotTimedOut_NoLinesAtAll_StillReturnsTrue() {
        // A quiet-period completion with zero lines (the device produced no output at all before going
        // quiet) is still tolerated as success -- this is exactly the "move produces no terminating
        // response" case the plan calls out; EatResponses treats it as success by default pre-T15.
        var exchange = new EatRawExchange("bf,10", Array.Empty<string>(), timedOut: false);
        Assert.That(EatResponses.ParseMoveAck(exchange), Is.True);
    }

    [Test]
    public void ParseMoveAck_TimedOut_ReturnsFalse() {
        var exchange = new EatRawExchange("tr,50", Array.Empty<string>(), timedOut: true);
        Assert.That(EatResponses.ParseMoveAck(exchange), Is.False);
    }

    [Test]
    public void ParseMoveAck_TimedOut_WithPartialLines_StillReturnsFalse() {
        // Even if some lines trickled in before the overall timeout fired, TimedOut still wins -- partial
        // data is not evidence of success under the tolerant policy.
        var exchange = new EatRawExchange("tr,50", new[] { "partial" }, timedOut: true);
        Assert.That(EatResponses.ParseMoveAck(exchange), Is.False);
    }

    [Test]
    public void ParseMoveAck_NullExchange_Throws() {
        Assert.Throws<ArgumentNullException>(() => EatResponses.ParseMoveAck(null));
    }

    // --- ParseCpPositions: assumed layout is "first four integers found, in order, across the raw lines". ---

    [Test]
    public void ParseCpPositions_FourIntegers_OnePerLine_ParsesInDeviceMotorOrder() {
        // LIVE-CAPTURE: assumes one integer per line, no other content -- real framing (single line vs
        // multiple, delimiter) unconfirmed until T15.
        var exchange = new EatRawExchange("cp", new[] { "100", "200", "300", "400" }, timedOut: false);

        var positions = EatResponses.ParseCpPositions(exchange);

        Assert.Multiple(() => {
            Assert.That(positions.Known, Is.True);
            // Device motor order 1..4 = TR, TL, BR, BL (TiltDevicePositions' own contract).
            Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { 100, 200, 300, 400 }));
        });
    }

    [Test]
    public void ParseCpPositions_FourIntegers_SingleCommaDelimitedLine_Parses() {
        // LIVE-CAPTURE: assumes a single comma-delimited reply line is also a plausible layout -- the
        // extraction is delimiter-agnostic (any non-digit run separates tokens) so both plausible layouts
        // parse the same way without EatResponses needing to guess which one the real device uses.
        var exchange = new EatRawExchange("cp", new[] { "cp,100,200,-300,400" }, timedOut: false);

        var positions = EatResponses.ParseCpPositions(exchange);

        Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { 100, 200, -300, 400 }));
    }

    [Test]
    public void ParseCpPositions_NegativeValues_ParsedWithSign() {
        var exchange = new EatRawExchange("cp", new[] { "-5", "0", "10", "-20" }, timedOut: false);

        var positions = EatResponses.ParseCpPositions(exchange);

        Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { -5, 0, 10, -20 }));
    }

    [Test]
    public void ParseCpPositions_MoreThanFourIntegers_TakesFirstFour() {
        // Tolerant: extra numeric noise (e.g. a leading echo of the command, or a trailing checksum) does
        // not prevent extraction -- only the first four found are used.
        var exchange = new EatRawExchange("cp", new[] { "cp", "100,200,300,400,999" }, timedOut: false);

        var positions = EatResponses.ParseCpPositions(exchange);

        Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { 100, 200, 300, 400 }));
    }

    [Test]
    public void ParseCpPositions_FewerThanFourIntegers_ThrowsInvalidDeviceResponseException_WithRawLinesInMessage() {
        var exchange = new EatRawExchange("cp", new[] { "100", "200" }, timedOut: false);

        var ex = Assert.Throws<InvalidDeviceResponseException>(() => EatResponses.ParseCpPositions(exchange));

        Assert.Multiple(() => {
            Assert.That(ex.Message, Does.Contain("100"));
            Assert.That(ex.Message, Does.Contain("200"));
        });
    }

    [Test]
    public void ParseCpPositions_NoLinesAtAll_ThrowsInvalidDeviceResponseException() {
        // Expected to happen ROUTINELY pre-T15 (e.g. a TimedOut exchange with zero lines) -- callers (T7's
        // EatTiltMotionController) treat this as "positions unknown", not a crash; this test just pins that
        // the parser itself throws rather than silently returning a bogus TiltDevicePositions.
        var exchange = new EatRawExchange("cp", Array.Empty<string>(), timedOut: true);

        Assert.Throws<InvalidDeviceResponseException>(() => EatResponses.ParseCpPositions(exchange));
    }

    [Test]
    public void ParseCpPositions_NonNumericLines_ThrowsInvalidDeviceResponseException() {
        var exchange = new EatRawExchange("cp", new[] { "ERR", "unexpected" }, timedOut: false);

        Assert.Throws<InvalidDeviceResponseException>(() => EatResponses.ParseCpPositions(exchange));
    }

    [Test]
    public void ParseCpPositions_NullExchange_Throws() {
        Assert.Throws<ArgumentNullException>(() => EatResponses.ParseCpPositions(null));
    }
}
