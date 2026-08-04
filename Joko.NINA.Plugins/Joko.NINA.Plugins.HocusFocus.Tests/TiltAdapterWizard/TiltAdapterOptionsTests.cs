using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

[TestFixture]
public class TiltAdapterOptionsTests {

    private static (TiltAdapterOptions options, InMemoryPluginOptionsAccessor store, IProfileService profile) Build() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new TiltAdapterOptions(profile, store);
        return (options, store, profile);
    }

    [Test]
    public void Defaults_AreLoadedFromAccessor() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.ScrewCount, Is.EqualTo(3));
            Assert.That(options.IsCalibrated, Is.False);
            Assert.That(options.Screw1AngleDegrees, Is.NaN);
            Assert.That(options.Screw2AngleDegrees, Is.NaN);
            Assert.That(options.Screw3AngleDegrees, Is.NaN);
            Assert.That(options.Screw4AngleDegrees, Is.NaN);
            Assert.That(options.CalibratedScrewCount, Is.EqualTo(0));
            Assert.That(options.MeasurementAverageCount, Is.EqualTo(1));
            Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(TiltScrewGeometry.DefaultScrewInwardCurvatureSign));
            Assert.That(options.AdjustmentType, Is.EqualTo(TiltAdjustmentType.Screws));
            Assert.That(options.ThreadPitchMicrons, Is.EqualTo(-1.0));
            Assert.That(options.StepperStepSizeMicrons, Is.EqualTo(-1.0));
            Assert.That(options.ScrewRadiusMillimeters, Is.EqualTo(-1.0));
            Assert.That(options.DeviceName, Is.EqualTo("Manual"));
            Assert.That(options.ScrewInwardCurvatureSignIsMeasured, Is.False);
            Assert.That(options.MeasureCurvatureDuringCalibration, Is.False);
            // Default ON (Task 6) -- unlike MeasureCurvatureDuringCalibration above, drift-symmetric
            // referencing of screw 2 is recommended for every calibration, not an opt-in extra.
            Assert.That(options.MeasureFinalRebaseline, Is.True);
            Assert.That(options.CalibrationIsManual, Is.False);
            Assert.That(options.TiltDeviceSerialPortName, Is.EqualTo(""));
            Assert.That(options.TiltDeviceMaxStepsPerCommand, Is.EqualTo(200));
            Assert.That(options.TiltDeviceMaxExcursionSteps, Is.EqualTo(2000));
            Assert.That(options.TiltDeviceSettleSeconds, Is.EqualTo(3.0));
            Assert.That(options.DeviceLinkedCalibrationDeviceName, Is.EqualTo(""));
            Assert.That(options.CalibrationIsReliable, Is.False);
            Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(""));
            Assert.That(options.CalibrationAppliedAmount, Is.EqualTo(-1.0));
        });
    }

    [Test]
    public void NewOptions_RoundTripThroughAccessor() {
        var (options, store, _) = Build();
        options.ScrewInwardCurvatureSignIsMeasured = true;
        options.MeasureCurvatureDuringCalibration = true;
        options.MeasureFinalRebaseline = false; // default is true, so false is the value that actually persists
        options.CalibrationIsManual = true;
        Assert.Multiple(() => {
            Assert.That(store.GetValueBoolean(nameof(TiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured), false), Is.True);
            Assert.That(store.GetValueBoolean(nameof(TiltAdapterOptions.MeasureCurvatureDuringCalibration), false), Is.True);
            Assert.That(store.GetValueBoolean(nameof(TiltAdapterOptions.MeasureFinalRebaseline), true), Is.False);
            Assert.That(store.GetValueBoolean(nameof(TiltAdapterOptions.CalibrationIsManual), false), Is.True);
        });
    }

    [Test]
    public void PersistedCurvatureSign_WinsOverDefault() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueInt32(nameof(TiltAdapterOptions.ScrewInwardCurvatureSign), -1);
        var options = new TiltAdapterOptions(profile, store);
        Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(-1));
    }

    [Test]
    public void TiltDeviceOptions_PersistAndRoundTripThroughNewInstance() {
        var (options, store, profile) = Build();
        options.TiltDeviceSerialPortName = "COM5";
        options.TiltDeviceMaxStepsPerCommand = 75;
        options.TiltDeviceMaxExcursionSteps = 900;
        options.TiltDeviceSettleSeconds = 4.5;
        options.DeviceLinkedCalibrationDeviceName = "ASG Electronic EAT - 90mm";
        options.CalibrationIsReliable = true;
        options.TiltDeviceShadowPositions = "{\"positions\":[10,20,30,40],\"valid\":true}";
        options.CalibrationAppliedAmount = 150.0;

        // Re-read through a brand-new TiltAdapterOptions over the same backing store, confirming the
        // values actually round-trip through the accessor rather than just being held in memory.
        var reloaded = new TiltAdapterOptions(profile, store);

        Assert.Multiple(() => {
            Assert.That(reloaded.TiltDeviceSerialPortName, Is.EqualTo("COM5"));
            Assert.That(reloaded.TiltDeviceMaxStepsPerCommand, Is.EqualTo(75));
            Assert.That(reloaded.TiltDeviceMaxExcursionSteps, Is.EqualTo(900));
            Assert.That(reloaded.TiltDeviceSettleSeconds, Is.EqualTo(4.5));
            Assert.That(reloaded.DeviceLinkedCalibrationDeviceName, Is.EqualTo("ASG Electronic EAT - 90mm"));
            Assert.That(reloaded.CalibrationIsReliable, Is.True);
            Assert.That(reloaded.TiltDeviceShadowPositions, Is.EqualTo("{\"positions\":[10,20,30,40],\"valid\":true}"));
            Assert.That(reloaded.CalibrationAppliedAmount, Is.EqualTo(150.0));
        });
    }

    [Test]
    public void Setter_PersistsAndRoundTrips() {
        var (options, store, _) = Build();
        options.ScrewCount = 4;
        options.IsCalibrated = true;
        options.Screw1AngleDegrees = 0;
        options.Screw2AngleDegrees = 90;
        options.Screw3AngleDegrees = 180;
        options.Screw4AngleDegrees = 270;
        options.CalibratedScrewCount = 4;
        options.MeasurementAverageCount = 5;
        options.ScrewInwardCurvatureSign = -1;
        options.AdjustmentType = TiltAdjustmentType.StepperMotors;
        options.ThreadPitchMicrons = 500.0;
        options.StepperStepSizeMicrons = 1.25;
        options.ScrewRadiusMillimeters = 21.0;
        options.DeviceName = "Neumann CTU XT48";

        Assert.Multiple(() => {
            Assert.That(store.Snapshot[nameof(options.ScrewCount)], Is.EqualTo(4));
            Assert.That(store.Snapshot[nameof(options.IsCalibrated)], Is.EqualTo(true));
            Assert.That(store.Snapshot[nameof(options.Screw1AngleDegrees)], Is.EqualTo(0d));
            Assert.That(store.Snapshot[nameof(options.Screw2AngleDegrees)], Is.EqualTo(90d));
            Assert.That(store.Snapshot[nameof(options.Screw3AngleDegrees)], Is.EqualTo(180d));
            Assert.That(store.Snapshot[nameof(options.Screw4AngleDegrees)], Is.EqualTo(270d));
            Assert.That(store.Snapshot[nameof(options.CalibratedScrewCount)], Is.EqualTo(4));
            Assert.That(store.Snapshot[nameof(options.MeasurementAverageCount)], Is.EqualTo(5));
            Assert.That(store.Snapshot[nameof(options.ScrewInwardCurvatureSign)], Is.EqualTo(-1));
            Assert.That(store.Snapshot[nameof(options.AdjustmentType)], Is.EqualTo(TiltAdjustmentType.StepperMotors));
            Assert.That(store.Snapshot[nameof(options.ThreadPitchMicrons)], Is.EqualTo(500.0));
            Assert.That(store.Snapshot[nameof(options.StepperStepSizeMicrons)], Is.EqualTo(1.25));
            Assert.That(store.Snapshot[nameof(options.ScrewRadiusMillimeters)], Is.EqualTo(21.0));
            Assert.That(store.Snapshot[nameof(options.DeviceName)], Is.EqualTo("Neumann CTU XT48"));
        });
    }

    [Test]
    public void Setter_DoesNotWriteWhenValueUnchanged() {
        var (options, store, _) = Build();
        var beforeWrites = store.WriteCount;
        options.ScrewCount = options.ScrewCount; // no-op assignment
        Assert.That(store.WriteCount, Is.EqualTo(beforeWrites));
    }

    [TestCase(nameof(TiltAdapterOptions.ScrewCount), 4)]
    [TestCase(nameof(TiltAdapterOptions.IsCalibrated), true)]
    [TestCase(nameof(TiltAdapterOptions.Screw1AngleDegrees), 12.5)]
    [TestCase(nameof(TiltAdapterOptions.Screw2AngleDegrees), 60.0)]
    [TestCase(nameof(TiltAdapterOptions.Screw3AngleDegrees), 270.0)]
    [TestCase(nameof(TiltAdapterOptions.Screw4AngleDegrees), 359.9)]
    [TestCase(nameof(TiltAdapterOptions.CalibratedScrewCount), 3)]
    [TestCase(nameof(TiltAdapterOptions.MeasurementAverageCount), 7)]
    [TestCase(nameof(TiltAdapterOptions.ScrewInwardCurvatureSign), -1)]
    [TestCase(nameof(TiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured), true)]
    [TestCase(nameof(TiltAdapterOptions.MeasureCurvatureDuringCalibration), true)]
    [TestCase(nameof(TiltAdapterOptions.MeasureFinalRebaseline), false)] // default is true, so false is the value that actually changes it
    [TestCase(nameof(TiltAdapterOptions.CalibrationIsManual), true)]
    [TestCase(nameof(TiltAdapterOptions.AdjustmentType), TiltAdjustmentType.StepperMotors)]
    [TestCase(nameof(TiltAdapterOptions.AngleDisplayUnit), TiltGuidanceAngleUnit.Degrees)]
    [TestCase(nameof(TiltAdapterOptions.ThreadPitchMicrons), 500.0)]
    [TestCase(nameof(TiltAdapterOptions.StepperStepSizeMicrons), 1.25)]
    [TestCase(nameof(TiltAdapterOptions.ScrewRadiusMillimeters), 21.0)]
    [TestCase(nameof(TiltAdapterOptions.DeviceName), "Neumann CTU XT48")]
    [TestCase(nameof(TiltAdapterOptions.TiltDeviceSerialPortName), "COM5")]
    [TestCase(nameof(TiltAdapterOptions.TiltDeviceMaxStepsPerCommand), 75)]
    [TestCase(nameof(TiltAdapterOptions.TiltDeviceMaxExcursionSteps), 900)]
    [TestCase(nameof(TiltAdapterOptions.TiltDeviceSettleSeconds), 4.5)]
    [TestCase(nameof(TiltAdapterOptions.DeviceLinkedCalibrationDeviceName), "ASG Electronic EAT - 90mm")]
    [TestCase(nameof(TiltAdapterOptions.CalibrationIsReliable), true)]
    [TestCase(nameof(TiltAdapterOptions.TiltDeviceShadowPositions), "{\"positions\":[1,2,3,4],\"valid\":true}")]
    [TestCase(nameof(TiltAdapterOptions.CalibrationAppliedAmount), 150.0)]
    public void Setter_RaisesPropertyChanged(string propertyName, object newValue) {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var prop = typeof(TiltAdapterOptions).GetProperty(propertyName);
        prop.SetValue(options, Convert.ChangeType(newValue, prop.PropertyType));
        Assert.That(raised, Does.Contain(propertyName));
    }

    // ---- One-time migration of measured curvature signs ------------------------------------------------
    //
    // Every ScrewInwardCurvatureSign a 6-step calibration wrote before 2026-08-04 came from an inverted
    // ComputeCurvatureSign, so measured values are deterministically negated on load — exactly once per
    // profile, guarded by a persisted marker (docs/focuser-direction-convention-design.md §5).

    private const string MigratedKey = "CurvatureSignMeasurementMigrated";

    [Test]
    public void Migration_NegatesAMeasuredSignOnce_AndSetsTheMarker() {
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueInt32(nameof(TiltAdapterOptions.ScrewInwardCurvatureSign), 1);
        store.SetValueBoolean(nameof(TiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured), true);

        var options = new TiltAdapterOptions(Substitute.For<IProfileService>(), store);

        Assert.Multiple(() => {
            Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(-1), "measured +1 loads as −1");
            Assert.That(store.GetValueInt32(nameof(TiltAdapterOptions.ScrewInwardCurvatureSign), 0), Is.EqualTo(-1),
                "and the correction is persisted, not just held in memory");
            Assert.That(store.GetValueBoolean(MigratedKey, false), Is.True);
            Assert.That(options.ScrewInwardCurvatureSignIsMeasured, Is.True, "provenance is preserved");
        });

        // A second construction over the same store must NOT negate again.
        var reloaded = new TiltAdapterOptions(Substitute.For<IProfileService>(), store);
        Assert.That(reloaded.ScrewInwardCurvatureSign, Is.EqualTo(-1), "the marker makes it idempotent");
    }

    [Test]
    public void Migration_RunsOnlyOnceAcrossAProfileChange() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueInt32(nameof(TiltAdapterOptions.ScrewInwardCurvatureSign), -1);
        store.SetValueBoolean(nameof(TiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured), true);

        var options = new TiltAdapterOptions(profile, store);
        Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(1), "precondition: migrated on construction");

        // ProfileChanged re-runs InitializeOptions; the marker in the (same) store must suppress it.
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(1), "a profile change must not negate again");
    }

    [Test]
    public void Migration_LeavesAnUnmeasuredSignAlone_ButStillSetsTheMarker() {
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueInt32(nameof(TiltAdapterOptions.ScrewInwardCurvatureSign), 1);
        store.SetValueBoolean(nameof(TiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured), false);

        var options = new TiltAdapterOptions(Substitute.For<IProfileService>(), store);

        Assert.Multiple(() => {
            // Assumed or hand-set by the wizard's direction combo (which clears IsMeasured) — the user's
            // value, never the buggy formula's.
            Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(1));
            Assert.That(store.GetValueBoolean(MigratedKey, false), Is.True, "the marker is set regardless");
        });
    }

    [Test]
    public void Migration_LeavesAZeroSignAlone() {
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueInt32(nameof(TiltAdapterOptions.ScrewInwardCurvatureSign), 0);
        store.SetValueBoolean(nameof(TiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured), true);

        var options = new TiltAdapterOptions(Substitute.For<IProfileService>(), store);

        Assert.Multiple(() => {
            Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(0), "negating the unset sentinel would be a no-op anyway");
            Assert.That(store.GetValueBoolean(MigratedKey, false), Is.True);
        });
    }

    [Test]
    public void AngleDisplayUnit_DefaultsToTurns() {
        var (options, _, _) = Build();
        Assert.That(options.AngleDisplayUnit, Is.EqualTo(TiltGuidanceAngleUnit.Turns));
    }

    [TestCase(TiltGuidanceAngleUnit.Degrees)]
    [TestCase(TiltGuidanceAngleUnit.Minutes)]
    public void AngleDisplayUnit_PersistsAndRoundTrips(TiltGuidanceAngleUnit unit) {
        var (options, store, _) = Build();
        options.AngleDisplayUnit = unit;
        Assert.Multiple(() => {
            Assert.That(store.GetValueEnum(nameof(TiltAdapterOptions.AngleDisplayUnit), TiltGuidanceAngleUnit.Turns),
                Is.EqualTo(unit));
            var reloaded = new TiltAdapterOptions(Substitute.For<IProfileService>(), store);
            Assert.That(reloaded.AngleDisplayUnit, Is.EqualTo(unit));
        });
    }
}
