#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

/// <summary>
/// The transport is the seam that lets the entire UNCHANGED EAT controller stack run against the simulator. The
/// unit tests pin its contract against a substitute actuator (cp reply parses, move commands decode to the right
/// wizard-order effect, acks succeed); the integration test drives a real actuator through a real, unmodified
/// <see cref="EatTiltMotionController"/> to prove the whole chain works end to end.
/// </summary>
[TestFixture]
public class SimulatedEatTransportTests {

    private static readonly TimeSpan AnyTimeout = TimeSpan.FromSeconds(5);

    private static ISimulatedTiltActuator Actuator(int[] positions = null) {
        var actuator = Substitute.For<ISimulatedTiltActuator>();
        actuator.GetPerMotorPositions().Returns(positions ?? new[] { 0, 0, 0, 0 });
        return actuator;
    }

    [Test]
    public async Task OpenAsync_SetsIsOpen_Close_ClearsIt() {
        var transport = new SimulatedEatTransport(Actuator());
        Assert.That(transport.IsOpen, Is.False);
        await transport.OpenAsync("Simulator", CancellationToken.None);
        Assert.That(transport.IsOpen, Is.True);
        transport.Close();
        Assert.That(transport.IsOpen, Is.False);
    }

    [Test]
    public async Task SendAsync_Cp_ReturnsPositionsParseableByEatResponses() {
        var transport = new SimulatedEatTransport(Actuator(new[] { 10, 20, 30, 40 }));

        var exchange = await transport.SendAsync("cp", AnyTimeout, CancellationToken.None);
        var positions = EatResponses.ParseCpPositions(exchange);

        Assert.Multiple(() => {
            Assert.That(exchange.TimedOut, Is.False);
            Assert.That(positions.Known, Is.True);
            Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { 10, 20, 30, 40 }));
        });
    }

    // "tr,5" (DiagonalA, +5) -> wizard-order per-corner effect (+5, 0, -5, 0).
    // "tl,5" (DiagonalB, +5) -> (0, +5, 0, -5). "bf,150" (Backfocus) -> (150,150,150,150).
    [TestCase("tr,5", 5.0, 0.0, -5.0, 0.0)]
    [TestCase("tl,5", 0.0, 5.0, 0.0, -5.0)]
    [TestCase("bf,150", 150.0, 150.0, 150.0, 150.0)]
    [TestCase("tr,-5", -5.0, 0.0, 5.0, 0.0)]
    public async Task SendAsync_MoveWire_AppliesWizardOrderEffect(string wire, double s0, double s1, double s2, double s3) {
        var actuator = Actuator();
        var transport = new SimulatedEatTransport(actuator);

        await transport.SendAsync(wire, AnyTimeout, CancellationToken.None);

        actuator.Received(1).ApplyWizardScrewSteps(Arg.Is<IReadOnlyList<double>>(v =>
            v != null && v.Count == 4 && v[0] == s0 && v[1] == s1 && v[2] == s2 && v[3] == s3));
    }

    [Test]
    public async Task SendAsync_Move_ReturnsSuccessAck() {
        var transport = new SimulatedEatTransport(Actuator());
        var exchange = await transport.SendAsync("tr,5", AnyTimeout, CancellationToken.None);
        Assert.That(EatResponses.ParseMoveAck(exchange), Is.True);
    }

    [Test]
    public void SendAsync_UnparseableCommand_Throws() {
        var transport = new SimulatedEatTransport(Actuator());
        Assert.ThrowsAsync<ArgumentException>(() => transport.SendAsync("zz,5", AnyTimeout, CancellationToken.None));
    }

    [Test]
    public void SendAsync_NullCommand_Throws() {
        var transport = new SimulatedEatTransport(Actuator());
        Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(null, AnyTimeout, CancellationToken.None));
    }

    [Test]
    public void Constructor_NullActuator_Throws() {
        Assert.Throws<ArgumentNullException>(() => new SimulatedEatTransport(null));
    }

    // ---- Integration: real actuator through the REAL, UNCHANGED EatTiltMotionController ----------------

    private static CameraSimulatorOptions BuildSimOptions() {
        var profileService = Substitute.For<IProfileService>();
        var options = new CameraSimulatorOptions(profileService, new InMemoryPluginOptionsAccessor(),
            new InspectorOptions(profileService, new InMemoryPluginOptionsAccessor()));
        options.SimScrewCount = 4;
        options.SimScrew1AngleDegrees = 45.0;
        options.SimScrew2AngleDegrees = 135.0;
        options.SimScrew3AngleDegrees = 225.0;
        options.SimScrew4AngleDegrees = 315.0;
        options.SimScrewInwardCurvatureSign = 1;
        options.SimAdjustmentType = TiltAdjustmentType.StepperMotors;
        options.SimStepperStepSizeMicrons = 1.8;
        options.SimScrewRadiusMillimeters = 55.0;
        options.EnableAberrations = true;
        return options;
    }

    private static ITiltAdapterOptions BuildTiltOptions() {
        var options = Substitute.For<ITiltAdapterOptions>();
        options.TiltDeviceMaxStepsPerCommand.Returns(200);
        options.TiltDeviceMaxExcursionSteps.Returns(2000); // the shipped default for absolute counters
        options.TiltDeviceSettleSeconds.Returns(0.0);
        return options;
    }

    [Test]
    public async Task Integration_RealActuator_ThroughUnchangedController() {
        var simOptions = BuildSimOptions();
        var actuator = new SimulatedTiltActuator(simOptions, new RecordingApplicationDispatcher());
        var controller = new EatTiltMotionController(new SimulatedEatTransport(actuator), BuildTiltOptions());

        await controller.ConnectAsync("Simulator", CancellationToken.None);
        Assert.That(controller.Connected, Is.True);
        Assert.That(controller.AbsolutePositionsKnown, Is.True, "the simulated cp reply always parses, so positions are known");

        var tiltBefore = simOptions.TiltAmountMicrons;

        // The simulated actuator homes mid-travel, so a differential tilt move from a fresh simulator has the
        // downward headroom the travel-below-0 floor demands — the exact move the wizard opens with, refused
        // when the motors homed at 0.
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 50, TiltMoveGroup.Tilt, "tr,50");
        await controller.ExecuteMoveAsync(move, null, CancellationToken.None);

        var positions = await controller.QueryPositionsAsync(CancellationToken.None);

        Assert.Multiple(() => {
            // DiagonalA +50 adds [+50, 0, 0, -50] in device order (TR, TL, BR, BL) on top of the mid-travel home.
            const int home = SimulatedTiltActuator.InitialPositionSteps;
            Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { home + 50, home, home, home - 50 }));
            Assert.That(simOptions.TiltAmountMicrons, Is.Not.EqualTo(tiltBefore), "the move must fold into the simulator's injected tilt");
        });
    }
}
