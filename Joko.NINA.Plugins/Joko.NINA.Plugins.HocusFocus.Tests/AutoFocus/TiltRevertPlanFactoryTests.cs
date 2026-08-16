using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    /// <summary>
    /// The two mechanisms for "put the adapter back the way it was at run K", and the precedence between them.
    ///
    /// <para>The differential formulation is the interesting one: each run's per-screw targets describe a STATE
    /// ("signed units from here to flat"), so the motion from now back to run K is T(now) − T(K). It needs only
    /// the two endpoint measurements — no assumption that the user ever applied the intermediate guidance, which
    /// is unknowable.</para>
    /// </summary>
    [TestFixture]
    public class TiltRevertPlanFactoryTests {
        private const string Preset = "ASG Electronic EAT - 90mm";
        private const double StepMicrons = 1.8;
        private const double RadiusMm = 55.0;

        private static ITiltAdapterOptions Options(TiltAdjustmentType type = TiltAdjustmentType.StepperMotors) {
            var options = Substitute.For<ITiltAdapterOptions>();
            options.ScrewCount.Returns(4);
            options.AdjustmentType.Returns(type);
            options.StepperStepSizeMicrons.Returns(StepMicrons);
            options.ThreadPitchMicrons.Returns(500.0);
            options.ScrewRadiusMillimeters.Returns(RadiusMm);
            options.Screw1AngleDegrees.Returns(0.0);
            options.Screw2AngleDegrees.Returns(90.0);
            options.Screw3AngleDegrees.Returns(180.0);
            options.Screw4AngleDegrees.Returns(270.0);
            options.ScrewInwardCurvatureSign.Returns(1);
            options.DeviceName.Returns(Preset);
            return options;
        }

        private static SensorParaboloidModel Model(double gx = 0.0, double gy = 0.0, double k = 0.0)
            => new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: gx, gy: gy, k: k);

        private static SensorParaboloidTiltHistoryModel Run(
                SensorParaboloidModel model, int[] perMotorSteps = null, bool positionsKnown = true, string preset = Preset) {
            var state = perMotorSteps == null
                ? TiltAdapterStateSnapshot.Unknown(DateTime.UtcNow, preset)
                : new TiltAdapterStateSnapshot(DateTime.UtcNow, perMotorSteps, positionsKnown, preset);
            return new SensorParaboloidTiltHistoryModel(
                historyId: 1, imageSize: new System.Drawing.Size(1000, 1000), pixelSizeMicrons: 4.0, fRatio: 5.0,
                focuserSizeMicrons: 1.0, finalFocusPosition: 0.0, tiltEffectMicrons: 0.0,
                curvatureEffectMicrons: 0.0, autoFocusOffset: 0.0, tiltPlaneModel: null,
                sensorModel: model, adapterState: state);
        }

        private static TiltRevertTarget Build(
                SensorParaboloidTiltHistoryModel target,
                SensorParaboloidModel current,
                ITiltAdapterOptions options = null,
                IReadOnlyList<int> currentPositions = null,
                bool currentPositionsKnown = true,
                string connectedPreset = Preset,
                bool deviceLinked = true,
                bool reliable = true)
            => TiltRevertPlanFactory.Build(
                target, current, options ?? Options(), currentPositions, currentPositionsKnown, connectedPreset, deviceLinked, reliable);

        // --- Differential ---------------------------------------------------------------------------------

        [Test]
        public void Differential_EqualsCurrentTargetsMinusHistoricalTargets() {
            var options = Options();
            var current = Model(gx: 0.004, gy: -0.002);
            var historical = Model(gx: 0.001, gy: 0.0005);
            var (currentTargets, _) = InspectorVM.BuildPerScrewTargets(current, options);
            var (historicalTargets, _) = InspectorVM.BuildPerScrewTargets(historical, options);

            var result = Build(Run(historical), current, options);

            Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.MeasurementDifferential));
            for (int i = 0; i < 4; i++) {
                Assert.That(result.StepsPerScrew[i], Is.EqualTo(currentTargets[i] - historicalTargets[i]).Within(1e-9), $"screw {i + 1}");
            }
        }

        [Test]
        public void Differential_SameModel_IsAllZeros() {
            var model = Model(gx: 0.003, gy: 0.002, k: 0.0001);

            var result = Build(Run(model), model);

            Assert.That(result.StepsPerScrew, Is.All.EqualTo(0.0).Within(1e-9));
        }

        // Self-consistency: going A -> B and B -> A must be exact negations, or repeated returns would drift.
        [Test]
        public void Differential_IsAntisymmetric() {
            var options = Options();
            var a = Model(gx: 0.002, gy: -0.001, k: 0.00005);
            var b = Model(gx: -0.003, gy: 0.004, k: -0.00002);

            var aToB = Build(Run(a), b, options);
            var bToA = Build(Run(b), a, options);

            for (int i = 0; i < 4; i++) {
                Assert.That(aToB.StepsPerScrew[i], Is.EqualTo(-bToA.StepsPerScrew[i]).Within(1e-9), $"screw {i + 1}");
            }
        }

        // A pure tilt change must decompose to pure tilt: no piston, so the adapter is not asked to move the
        // sensor bodily toward or away from the objective for a change that never did.
        [Test]
        public void Differential_TiltOnlyChange_HasNoPistonComponent() {
            var current = Model(gx: 0.004, gy: -0.002);
            var historical = Model(gx: 0.001, gy: 0.0005);

            var result = Build(Run(historical), current);

            Assert.That(TiltMovePlannerProbe.Piston(result.StepsPerScrew), Is.EqualTo(0.0).Within(1e-9));
        }

        // The claim that made it safe to keep backfocus in the differential rather than excluding it: a change in
        // curvature alone must come out as PURE piston, with no spurious tilt.
        [Test]
        public void Differential_CurvatureOnlyChange_IsPurePiston() {
            var current = Model(k: 0.00008);
            var historical = Model(k: 0.00002);

            var result = Build(Run(historical), current);

            Assert.Multiple(() => {
                Assert.That(TiltMovePlannerProbe.TiltA(result.StepsPerScrew), Is.EqualTo(0.0).Within(1e-9), "no spurious tilt about one axis");
                Assert.That(TiltMovePlannerProbe.TiltB(result.StepsPerScrew), Is.EqualTo(0.0).Within(1e-9), "nor the other");
                Assert.That(Math.Abs(TiltMovePlannerProbe.Piston(result.StepsPerScrew)), Is.GreaterThan(0.0), "the change shows up as piston");
                Assert.That(result.TwistSteps, Is.EqualTo(0.0).Within(1e-9), "and never as twist");
            });
        }

        // Storing the two MODELS and recomputing beats storing computed targets: a re-calibration between the
        // runs then leaves the differential self-consistent under the calibration we can actually act on.
        [Test]
        public void Differential_UsesTheCurrentOptionsNotTheOnesInEffectAtCaptureTime() {
            var current = Model(gx: 0.004);
            var historical = Model(gx: 0.001);
            var coarse = Options();
            coarse.StepperStepSizeMicrons.Returns(3.6);

            var fine = Build(Run(historical), current, Options());
            var coarser = Build(Run(historical), current, coarse);

            Assert.That(coarser.StepsPerScrew[0], Is.EqualTo(fine.StepsPerScrew[0] / 2.0).Within(1e-6),
                "twice the unit size, half the step count");
        }

        [Test]
        public void Differential_IsNeverTrustedForBackfocus() {
            var result = Build(Run(Model(gx: 0.001)), Model(gx: 0.004));

            Assert.That(result.BackfocusTrustworthy, Is.False);
        }

        [Test]
        public void Differential_NoHistoricalModel_IsUnavailableWithAReason() {
            var result = Build(Run(null), Model(gx: 0.004));

            Assert.Multiple(() => {
                Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.Unavailable));
                Assert.That(result.UnavailableReason, Is.Not.Empty);
            });
        }

        [Test]
        public void Differential_NoCurrentModel_IsUnavailableWithAReason() {
            var result = Build(Run(Model(gx: 0.001)), null);

            Assert.Multiple(() => {
                Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.Unavailable));
                Assert.That(result.UnavailableReason, Does.Contain("current measurement"));
            });
        }

        [Test]
        public void Differential_UnconfiguredAdapter_IsUnavailableAndDoesNotThrow() {
            var broken = Options();
            broken.ScrewRadiusMillimeters.Returns(0.0);

            TiltRevertTarget result = null;
            Assert.DoesNotThrow(() => result = Build(Run(Model(gx: 0.001)), Model(gx: 0.004), broken));
            Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.Unavailable));
        }

        // The differential reads screw angles and the direction sign, so an unlinked or unreliable calibration
        // could send a correction 90 degrees off.
        [Test]
        public void Differential_CalibrationNotDeviceLinked_IsUnavailable() {
            var result = Build(Run(Model(gx: 0.001)), Model(gx: 0.004), deviceLinked: false);

            Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.Unavailable));
        }

        [Test]
        public void Differential_CalibrationNotReliable_IsUnavailable() {
            var result = Build(Run(Model(gx: 0.001)), Model(gx: 0.004), reliable: false);

            Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.Unavailable));
        }

        // Manual screws never have recorded motor positions, so the gate above must not apply to them.
        [Test]
        public void Differential_ManualScrewAdapter_WorksWithoutADeviceLinkedCalibration() {
            var options = Options(TiltAdjustmentType.Screws);

            var result = Build(Run(Model(gx: 0.001)), Model(gx: 0.004), options, deviceLinked: false, reliable: false);

            Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.MeasurementDifferential));
        }

        // --- Device positions -----------------------------------------------------------------------------

        [Test]
        public void DevicePositions_PreferredOverTheDifferentialWhenBothEndpointsAreKnown() {
            var result = Build(
                Run(Model(gx: 0.001), perMotorSteps: new[] { 1000, 1000, 1000, 1000 }),
                Model(gx: 0.004),
                currentPositions: new[] { 1010, 990, 1000, 1000 });

            Assert.Multiple(() => {
                Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.DevicePositions));
                Assert.That(result.BackfocusTrustworthy, Is.True, "counters are ground truth");
            });
        }

        [Test]
        public void DevicePositions_DeltaIsTargetMinusCurrentInWizardScrewOrder() {
            var result = Build(
                Run(Model(), perMotorSteps: new[] { 100, 200, 300, 400 }),
                Model(),
                currentPositions: new[] { 0, 0, 0, 0 });

            // Wizard order TR/TL/BL/BR reads device motors 1/2/4/3.
            Assert.That(result.StepsPerScrew, Is.EqualTo(new[] { 100.0, 200.0, 400.0, 300.0 }));
        }

        [Test]
        public void DevicePositions_PresetMismatch_FallsBackToTheDifferential() {
            var result = Build(
                Run(Model(gx: 0.001), perMotorSteps: new[] { 1000, 1000, 1000, 1000 }, preset: "Some Other Adapter"),
                Model(gx: 0.004),
                currentPositions: new[] { 1000, 1000, 1000, 1000 });

            Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.MeasurementDifferential));
        }

        [Test]
        public void DevicePositions_UnknownAtCaptureTime_FallsBackToTheDifferential() {
            var result = Build(Run(Model(gx: 0.001)), Model(gx: 0.004), currentPositions: new[] { 1000, 1000, 1000, 1000 });

            Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.MeasurementDifferential));
        }

        [Test]
        public void DevicePositions_UnknownNow_FallsBackToTheDifferential() {
            var result = Build(
                Run(Model(gx: 0.001), perMotorSteps: new[] { 1000, 1000, 1000, 1000 }),
                Model(gx: 0.004),
                currentPositions: null,
                currentPositionsKnown: false);

            Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.MeasurementDifferential));
        }

        [Test]
        public void DevicePositions_ManualScrewAdapter_NeverUsesPositions() {
            var result = Build(
                Run(Model(gx: 0.001), perMotorSteps: new[] { 1000, 1000, 1000, 1000 }),
                Model(gx: 0.004),
                Options(TiltAdjustmentType.Screws),
                currentPositions: new[] { 1010, 1000, 1000, 1000 });

            Assert.That(result.Mechanism, Is.EqualTo(TiltRevertMechanism.MeasurementDifferential));
        }

        // Two states the device actually reached differ by a rigid-plane delta, so zero twist is the norm...
        [Test]
        public void DevicePositions_ReachableStates_HaveNoTwist() {
            // A pure tilt about one axis plus a uniform lift: both reachable.
            var result = Build(
                Run(Model(), perMotorSteps: new[] { 1010, 1005, 1005, 1000 }),
                Model(),
                currentPositions: new[] { 1000, 1000, 1000, 1000 });

            Assert.That(result.TwistSteps, Is.EqualTo(0.0).Within(1e-9));
        }

        // ...and a nonzero twist is itself the diagnostic: position tracking drifted since the run.
        [Test]
        public void DevicePositions_HandTweakedCounters_ReportNonZeroTwist() {
            // Twist is (s1 − s2 + s3 − s4)/4 in WIZARD order, so raise the two diagonal corners TR and BL —
            // device motors 1 and 4 — and leave TL/BR alone. No rigid plane can produce that.
            var result = Build(
                Run(Model(), perMotorSteps: new[] { 1004, 1000, 1000, 1004 }),
                Model(),
                currentPositions: new[] { 1000, 1000, 1000, 1000 });

            Assert.That(Math.Abs(result.TwistSteps), Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void NoTarget_IsUnavailable() {
            Assert.That(Build(null, Model()).Mechanism, Is.EqualTo(TiltRevertMechanism.Unavailable));
        }
    }

    /// <summary>
    /// Reads the orthogonal components of a 4-corner motion the way the planner does, so the tests above can talk
    /// about "tilt" and "piston" rather than four raw numbers.
    /// s1 = a+f+t, s2 = b+f−t, s3 = −a+f+t, s4 = −b+f−t.
    /// </summary>
    internal static class TiltMovePlannerProbe {
        public static double TiltA(IReadOnlyList<double> s) => (s[0] - s[2]) / 2.0;

        public static double TiltB(IReadOnlyList<double> s) => (s[1] - s[3]) / 2.0;

        public static double Piston(IReadOnlyList<double> s) => (s[0] + s[1] + s[2] + s[3]) / 4.0;
    }
}
