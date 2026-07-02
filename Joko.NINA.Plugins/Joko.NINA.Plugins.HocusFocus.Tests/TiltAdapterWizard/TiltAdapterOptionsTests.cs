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
            Assert.That(options.CalibrationIsManual, Is.False);
        });
    }

    [Test]
    public void NewOptions_RoundTripThroughAccessor() {
        var (options, store, _) = Build();
        options.ScrewInwardCurvatureSignIsMeasured = true;
        options.MeasureCurvatureDuringCalibration = true;
        options.CalibrationIsManual = true;
        Assert.Multiple(() => {
            Assert.That(store.GetValueBoolean(nameof(TiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured), false), Is.True);
            Assert.That(store.GetValueBoolean(nameof(TiltAdapterOptions.MeasureCurvatureDuringCalibration), false), Is.True);
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
    [TestCase(nameof(TiltAdapterOptions.CalibrationIsManual), true)]
    [TestCase(nameof(TiltAdapterOptions.AdjustmentType), TiltAdjustmentType.StepperMotors)]
    [TestCase(nameof(TiltAdapterOptions.ThreadPitchMicrons), 500.0)]
    [TestCase(nameof(TiltAdapterOptions.StepperStepSizeMicrons), 1.25)]
    [TestCase(nameof(TiltAdapterOptions.ScrewRadiusMillimeters), 21.0)]
    [TestCase(nameof(TiltAdapterOptions.DeviceName), "Neumann CTU XT48")]
    public void Setter_RaisesPropertyChanged(string propertyName, object newValue) {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var prop = typeof(TiltAdapterOptions).GetProperty(propertyName);
        prop.SetValue(options, Convert.ChangeType(newValue, prop.PropertyType));
        Assert.That(raised, Does.Contain(propertyName));
    }
}
