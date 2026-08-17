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
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

/// <summary>
/// The actuator is the software stand-in for the EAT's four motors. These tests pin the two index orders it
/// bridges (wizard-order moves in, device-order counters out), that it folds through the shared, sign-verified
/// <see cref="SimulatedTiltInjection"/> (so tilt vs backfocus stay separated), and that it stays faithful even
/// when the geometry can't render (a real motor still turns). The convergence physics itself is pinned by the
/// VM/capstone tests through the same Fold; here we assert the actuator drives that path correctly.
/// </summary>
[TestFixture]
public class SimulatedTiltActuatorTests {

    private const double StepMicrons = 1.8;
    private const double RadiusMillimeters = 55.0;
    private const int Home = SimulatedTiltActuator.InitialPositionSteps;

    private static CameraSimulatorOptions BuildOptions() {
        var profileService = Substitute.For<IProfileService>();
        return new CameraSimulatorOptions(profileService, new InMemoryPluginOptionsAccessor(),
            new InspectorOptions(profileService, new InMemoryPluginOptionsAccessor()));
    }

    /// <summary>A valid 4-corner stepper EAT-like config: square screw angles, matching radius/step size.</summary>
    private static CameraSimulatorOptions ConfiguredEat(int curvatureSign = 1) {
        var options = BuildOptions();
        options.SimScrewCount = 4;
        options.SimScrew1AngleDegrees = 45.0;
        options.SimScrew2AngleDegrees = 135.0;
        options.SimScrew3AngleDegrees = 225.0;
        options.SimScrew4AngleDegrees = 315.0;
        options.SimScrewInwardCurvatureSign = curvatureSign;
        options.SimAdjustmentType = TiltAdjustmentType.StepperMotors;
        options.SimStepperStepSizeMicrons = StepMicrons;
        options.SimScrewRadiusMillimeters = RadiusMillimeters;
        options.EnableAberrations = true;
        return options;
    }

    private static SimulatedTiltActuator NewActuator(CameraSimulatorOptions options, out RecordingApplicationDispatcher dispatcher) {
        dispatcher = new RecordingApplicationDispatcher();
        return new SimulatedTiltActuator(options, dispatcher);
    }

    [Test]
    public void GetPerMotorPositions_StartsMidTravel() {
        // A fresh simulator homes its motors mid-travel (not at 0) so the wizard's first diagonal move —
        // which drives one corner down — has downward headroom under the controller's travel-below-0 floor.
        var actuator = NewActuator(ConfiguredEat(), out _);
        Assert.That(actuator.GetPerMotorPositions(), Is.EqualTo(new[] { Home, Home, Home, Home }));
        Assert.That(Home, Is.EqualTo(1000), "mid-travel of the 2000-step default max excursion");
    }

    [Test]
    public void ApplyWizardScrewSteps_AdvancesCountersInDeviceOrder() {
        var actuator = NewActuator(ConfiguredEat(), out _);

        // Wizard-order diagonal-A move "tr,5": PerCornerSteps = (+5, 0, -5, 0). Device order (TR,TL,BR,BL) is the
        // permutation [w0, w1, w3, w2] = [5, 0, 0, -5].
        actuator.ApplyWizardScrewSteps(new[] { 5.0, 0.0, -5.0, 0.0 });
        Assert.That(actuator.GetPerMotorPositions(), Is.EqualTo(new[] { Home + 5, Home, Home, Home - 5 }));

        // Backfocus "bf,3": (+3,+3,+3,+3) -> device [3,3,3,3], accumulating onto the previous state.
        actuator.ApplyWizardScrewSteps(new[] { 3.0, 3.0, 3.0, 3.0 });
        Assert.That(actuator.GetPerMotorPositions(), Is.EqualTo(new[] { Home + 8, Home + 3, Home + 3, Home - 2 }));
    }

    [Test]
    public void ApplyWizardScrewSteps_MarshalsFoldThroughDispatcher() {
        var actuator = NewActuator(ConfiguredEat(), out var dispatcher);
        actuator.ApplyWizardScrewSteps(new[] { 5.0, 0.0, -5.0, 0.0 });
        Assert.That(dispatcher.DispatchCount, Is.EqualTo(1), "the optical fold must be marshalled onto the UI thread");
    }

    [Test]
    public void ApplyWizardScrewSteps_DiagonalPair_ChangesTiltNotBackfocus() {
        var options = ConfiguredEat();
        var actuator = new SimulatedTiltActuator(options, new RecordingApplicationDispatcher());
        // Against the value the options START at, not a literal zero: the shipped backfocus default is
        // nonzero so the astigmatism model has something to work from, and what this test is about is that a
        // symmetric move does not MOVE it.
        var baselineBackfocus = options.BackfocusErrorMicrons;

        actuator.ApplyWizardScrewSteps(new[] { 50.0, 0.0, -50.0, 0.0 }); // pure diagonal -> pure tilt, piston 0

        Assert.Multiple(() => {
            Assert.That(options.TiltAmountMicrons, Is.GreaterThan(0.0), "a diagonal move must inject tilt");
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(baselineBackfocus).Within(1e-9), "a symmetric diagonal move has zero piston -> no backfocus change");
        });
    }

    [Test]
    public void ApplyWizardScrewSteps_BackfocusAllMotors_ChangesBackfocusNotTilt() {
        var options = ConfiguredEat();
        var actuator = new SimulatedTiltActuator(options, new RecordingApplicationDispatcher());

        actuator.ApplyWizardScrewSteps(new[] { 50.0, 50.0, 50.0, 50.0 }); // uniform -> pure piston, zero gradient

        Assert.Multiple(() => {
            Assert.That(options.BackfocusErrorMicrons, Is.Not.EqualTo(0.0), "a backfocus move must inject a backfocus error");
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(0.0).Within(1e-9), "a uniform move has zero gradient -> no tilt change");
        });
    }

    [Test]
    public void ApplyWizardScrewSteps_ApplyThenInverse_ReturnsToBaseline() {
        var options = ConfiguredEat();
        var actuator = new SimulatedTiltActuator(options, new RecordingApplicationDispatcher());
        var baselineTilt = options.TiltAmountMicrons;
        var baselineBackfocus = options.BackfocusErrorMicrons;

        actuator.ApplyWizardScrewSteps(new[] { 50.0, 0.0, -50.0, 0.0 });
        actuator.ApplyWizardScrewSteps(new[] { -50.0, 0.0, 50.0, 0.0 }); // exact inverse

        Assert.Multiple(() => {
            Assert.That(actuator.GetPerMotorPositions(), Is.EqualTo(new[] { Home, Home, Home, Home }), "counters must return to baseline");
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(baselineTilt).Within(1e-6), "injected tilt must return to baseline");
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(baselineBackfocus).Within(1e-6), "injected backfocus must return to baseline");
        });
    }

    [Test]
    public void ApplyWizardScrewSteps_UnbuildableGeometry_StillTracksPositions_SkipsFold() {
        var options = ConfiguredEat();
        options.SimScrewRadiusMillimeters = 0.0; // radius <= 0 -> BuildAdapter returns null, fold is skipped
        var actuator = new SimulatedTiltActuator(options, new RecordingApplicationDispatcher());

        actuator.ApplyWizardScrewSteps(new[] { 50.0, 0.0, -50.0, 0.0 });

        Assert.Multiple(() => {
            Assert.That(actuator.GetPerMotorPositions(), Is.EqualTo(new[] { Home + 50, Home, Home, Home - 50 }), "counters advance even when geometry can't render");
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(0.0).Within(1e-9), "no optical effect when the plane can't be built");
        });
    }

    [Test]
    public void ApplyWizardScrewSteps_AccumulatesSharedNetCounters() {
        var options = ConfiguredEat();
        var actuator = new SimulatedTiltActuator(options, new RecordingApplicationDispatcher());

        actuator.ApplyWizardScrewSteps(new[] { 150.0, 150.0, 150.0, 150.0 }); // bf,150 -> +150 steps per screw

        // Net is stored in axial µm (steps x step size), the same convention the manual panel's "Net" strip uses.
        Assert.That(options.SimNetAxialMicrons,
            Is.EqualTo(new[] { 150 * StepMicrons, 150 * StepMicrons, 150 * StepMicrons, 150 * StepMicrons }).Within(1e-9),
            "the shared net counters must reflect the automated move");
    }

    [Test]
    public void ApplyWizardScrewSteps_Net_InverseReturnsToZero() {
        var options = ConfiguredEat();
        var actuator = new SimulatedTiltActuator(options, new RecordingApplicationDispatcher());

        actuator.ApplyWizardScrewSteps(new[] { 50.0, 0.0, -50.0, 0.0 });
        actuator.ApplyWizardScrewSteps(new[] { -50.0, 0.0, 50.0, 0.0 });

        Assert.That(options.SimNetAxialMicrons, Is.EqualTo(new[] { 0.0, 0.0, 0.0, 0.0 }).Within(1e-9));
    }

    [Test]
    public void ApplyWizardScrewSteps_WrongLength_Throws() {
        var actuator = NewActuator(ConfiguredEat(), out _);
        Assert.Throws<ArgumentException>(() => actuator.ApplyWizardScrewSteps(new[] { 1.0, 2.0, 3.0 }));
    }

    [Test]
    public void ApplyWizardScrewSteps_Null_Throws() {
        var actuator = NewActuator(ConfiguredEat(), out _);
        Assert.Throws<ArgumentNullException>(() => actuator.ApplyWizardScrewSteps(null));
    }

    [Test]
    public void Constructor_NullOptions_Throws() {
        Assert.Throws<ArgumentNullException>(() => new SimulatedTiltActuator(null, new RecordingApplicationDispatcher()));
    }

    [Test]
    public void Constructor_NullDispatcher_Throws() {
        Assert.Throws<ArgumentNullException>(() => new SimulatedTiltActuator(ConfiguredEat(), null));
    }
}
