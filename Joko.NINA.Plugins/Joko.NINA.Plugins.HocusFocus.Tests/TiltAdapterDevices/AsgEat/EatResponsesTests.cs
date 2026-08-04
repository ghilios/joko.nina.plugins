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

    // --- ParseMoveAck: the device's move-completion sentinel is the ONLY evidence of success. ---

    [Test]
    public void ParseMoveAck_ResponseCarriesTheMoveSentinel_ReturnsTrue() {
        var exchange = new EatRawExchange("tr,50",
            new[] { "start_cmd: tr", "moving TR + 50.00", "***Save EEPROM***", "***finished movement***" }, timedOut: false);
        Assert.That(EatResponses.ParseMoveAck(exchange), Is.True);
    }

    [Test]
    public void ParseMoveAck_NotTimedOut_NoLinesAtAll_ReturnsFalse() {
        // A move that produced no output at all is NOT evidence of success. It used to be treated as one
        // ("did not time out" == acked), which is how a move that never ran got journaled as applied.
        var exchange = new EatRawExchange("bf,10", Array.Empty<string>(), timedOut: false);
        Assert.That(EatResponses.ParseMoveAck(exchange), Is.False);
    }

    // The desync case that made this check necessary: with the link running one response behind, a move read
    // back the PREVIOUS 'cp' response, whose "***Action Processed***" is a terminal sentinel too. Accepting any
    // terminal sentinel meant a move whose motors never confirmed anything was recorded as successfully
    // applied -- on a device that persists every move to EEPROM.
    [Test]
    public void ParseMoveAck_ResponseIsAStaleQueryResponse_ReturnsFalse() {
        var exchange = new EatRawExchange("bf,33",
            new[] { "{UI|SET|ready_light.IndicatorColor=Red}", "***Get Current Positions***", "400", "400", "400", "400",
                    "***End Current Positions***", "***Action Processed***" }, timedOut: false);
        Assert.That(EatResponses.ParseMoveAck(exchange), Is.False,
            "a query's sentinel must never acknowledge a move");
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

    // --- ParseCpPositions: the REAL device format (ASG EAT firmware 7.1.0), captured from hardware. ---
    // See docs/asg-eat-serial-protocol-design.md. The four positions are bare integer lines wrapped between
    // "***Get Current Positions***" and "***End Current Positions***", in WIRE order [TL, TR, BL, BR], amid
    // {UI|SET|...} chatter; the parser reorders them into device motor order [TR, TL, BR, BL].

    [Test]
    public void ParseCpPositions_RealMarkedBlock_ReordersWireTLTRBLBR_ToDeviceTRTLBRBL() {
        // Wire block [TL, TR, BL, BR] = [10, 20, 30, 40] -> device order [TR, TL, BR, BL] = [20, 10, 40, 30].
        var exchange = new EatRawExchange("cp", new[] {
            "{UI|SET|ready_light.IndicatorColor=Red}",
            "{UI|SET|TR_curr_position.Text=20}",
            "***Get Current Positions***",
            "10",   // TL
            "20",   // TR
            "30",   // BL
            "40",   // BR
            "***End Current Positions***",
            "{UI|SET|ready_light.IndicatorColor=green}",
            "***Action Processed***",
        }, timedOut: false);

        var positions = EatResponses.ParseCpPositions(exchange);

        Assert.Multiple(() => {
            Assert.That(positions.Known, Is.True);
            Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { 20, 10, 40, 30 }));
        });
    }

    [Test]
    public void ParseCpPositions_RealMarkedBlock_IgnoresUiChatterIntegersBeforeTheBlock() {
        // The {UI|SET|..._input.Text=0} lines contain integers (0) BEFORE the block. A naive "first four
        // integers" scan would return [0,0,0,0]; the marker-aware parser must instead take the block values.
        var exchange = new EatRawExchange("cp", new[] {
            "{UI|SET|TR_input.Text=0}",
            "{UI|SET|TL_input.Text=0}",
            "{UI|SET|BR_input.Text=0}",
            "{UI|SET|BL_input.Text=0}",
            "***Get Current Positions***",
            "600",  // TL
            "605",  // TR
            "595",  // BL
            "600",  // BR
            "***End Current Positions***",
        }, timedOut: false);

        var positions = EatResponses.ParseCpPositions(exchange);

        // Device order [TR, TL, BR, BL] = [605, 600, 600, 595].
        Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { 605, 600, 600, 595 }));
    }

    [Test]
    public void ParseCpPositions_MarkerPresentButFewerThanFourIntegers_Throws() {
        // A real-device marker with a malformed body is a genuine parse failure -- must NOT silently fall back
        // to scanning {UI|SET|...} integers for a bogus answer.
        var exchange = new EatRawExchange("cp", new[] {
            "***Get Current Positions***",
            "600",
            "605",
            "***End Current Positions***",
        }, timedOut: false);

        Assert.Throws<InvalidDeviceResponseException>(() => EatResponses.ParseCpPositions(exchange));
    }

    [Test]
    public void ParseCpPositions_NullExchange_Throws() {
        Assert.Throws<ArgumentNullException>(() => EatResponses.ParseCpPositions(null));
    }
}
