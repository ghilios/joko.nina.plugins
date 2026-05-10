using System;
using System.Collections.Generic;
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
            Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(0));
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
    [TestCase(nameof(TiltAdapterOptions.ScrewInwardCurvatureSign), 1)]
    public void Setter_RaisesPropertyChanged(string propertyName, object newValue) {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var prop = typeof(TiltAdapterOptions).GetProperty(propertyName);
        prop.SetValue(options, Convert.ChangeType(newValue, prop.PropertyType));
        Assert.That(raised, Does.Contain(propertyName));
    }
}
