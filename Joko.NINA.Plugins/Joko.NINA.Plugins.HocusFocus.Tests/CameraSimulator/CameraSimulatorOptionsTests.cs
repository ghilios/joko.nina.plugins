using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class CameraSimulatorOptionsTests {

    private static (CameraSimulatorOptions options, InMemoryPluginOptionsAccessor store, IProfileService profile) Build() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new CameraSimulatorOptions(profile, store);
        return (options, store, profile);
    }

    [Test]
    public void Defaults_MatchDesignConfigTable() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.OptimalFocuserPosition, Is.EqualTo(5000));
            Assert.That(options.FocuserStepSizeMicrons, Is.EqualTo(2.0));
            Assert.That(options.ApertureMillimeters, Is.EqualTo(100.0));
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(0.0));
            Assert.That(options.CentralObstructionEnabled, Is.True);
            Assert.That(options.CentralObstructionFraction, Is.EqualTo(0.3));
            Assert.That(options.OpticalThroughput, Is.EqualTo(0.85));
            Assert.That(options.SensorModel, Is.EqualTo(SonySensorModel.IMX455));
            Assert.That(options.Gain, Is.EqualTo(100));
            Assert.That(options.BiasPedestalAdu, Is.EqualTo(500));
            Assert.That(options.SensorTemperatureCelsius, Is.EqualTo(-10.0));
            Assert.That(options.Filter, Is.EqualTo(SimulatorFilter.L));
            Assert.That(options.SkyBrightnessMagPerArcsec2, Is.EqualTo(20.5));
            Assert.That(options.SeeingArcsec, Is.EqualTo(2.5));
            Assert.That(options.AstapCatalogPath, Is.EqualTo(CameraSimulatorOptions.DefaultAstapCatalogPath));
            Assert.That(options.LimitingMagnitude, Is.EqualTo(16.0));
            Assert.That(options.RotationDegrees, Is.EqualTo(0.0));
            Assert.That(options.NoiseSeed, Is.EqualTo(42));
            Assert.That(options.EnableAberrations, Is.False);
            Assert.That(options.TiltAngleDegrees, Is.EqualTo(0.0));
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(0.0));
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(0.0));
            Assert.That(options.OpticalAxisOffsetXMicrons, Is.EqualTo(0.0));
            Assert.That(options.OpticalAxisOffsetYMicrons, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void Setters_PersistToAccessorAndReadBack() {
        var (options, store, _) = Build();

        options.OptimalFocuserPosition = 12345;
        options.FocuserStepSizeMicrons = 0.5;
        options.ApertureMillimeters = 200.0;
        options.FocalLengthMillimeters = 800.0;
        options.CentralObstructionEnabled = false;
        options.CentralObstructionFraction = 0.4;
        options.OpticalThroughput = 0.7;
        options.SensorModel = SonySensorModel.IMX294;
        options.Gain = 200;
        options.BiasPedestalAdu = 125;
        options.SensorTemperatureCelsius = -20.0;
        options.Filter = SimulatorFilter.Ha5;
        options.SkyBrightnessMagPerArcsec2 = 21.5;
        options.SeeingArcsec = 1.8;
        options.AstapCatalogPath = @"D:\astap-db";
        options.LimitingMagnitude = 14.0;
        options.RotationDegrees = 33.0;
        options.NoiseSeed = 7;
        options.EnableAberrations = true;
        options.TiltAngleDegrees = 45.0;
        options.TiltAmountMicrons = 15.0;
        options.BackfocusErrorMicrons = 10.0;
        options.OpticalAxisOffsetXMicrons = 100.0;
        options.OpticalAxisOffsetYMicrons = -50.0;

        // Re-read through a fresh options object over the same store to prove persistence round-trips.
        var reread = new CameraSimulatorOptions(Substitute.For<IProfileService>(), store);

        Assert.Multiple(() => {
            Assert.That(store.Snapshot[nameof(CameraSimulatorOptions.OptimalFocuserPosition)], Is.EqualTo(12345));
            Assert.That(store.Snapshot[nameof(CameraSimulatorOptions.SensorModel)], Is.EqualTo(SonySensorModel.IMX294));
            Assert.That(store.Snapshot[nameof(CameraSimulatorOptions.Filter)], Is.EqualTo(SimulatorFilter.Ha5));
            Assert.That(store.Snapshot[nameof(CameraSimulatorOptions.AstapCatalogPath)], Is.EqualTo(@"D:\astap-db"));

            Assert.That(reread.OptimalFocuserPosition, Is.EqualTo(12345));
            Assert.That(reread.FocuserStepSizeMicrons, Is.EqualTo(0.5));
            Assert.That(reread.ApertureMillimeters, Is.EqualTo(200.0));
            Assert.That(reread.FocalLengthMillimeters, Is.EqualTo(800.0));
            Assert.That(reread.CentralObstructionEnabled, Is.False);
            Assert.That(reread.CentralObstructionFraction, Is.EqualTo(0.4));
            Assert.That(reread.OpticalThroughput, Is.EqualTo(0.7));
            Assert.That(reread.SensorModel, Is.EqualTo(SonySensorModel.IMX294));
            Assert.That(reread.Gain, Is.EqualTo(200));
            Assert.That(reread.BiasPedestalAdu, Is.EqualTo(125));
            Assert.That(reread.SensorTemperatureCelsius, Is.EqualTo(-20.0));
            Assert.That(reread.Filter, Is.EqualTo(SimulatorFilter.Ha5));
            Assert.That(reread.SkyBrightnessMagPerArcsec2, Is.EqualTo(21.5));
            Assert.That(reread.SeeingArcsec, Is.EqualTo(1.8));
            Assert.That(reread.AstapCatalogPath, Is.EqualTo(@"D:\astap-db"));
            Assert.That(reread.LimitingMagnitude, Is.EqualTo(14.0));
            Assert.That(reread.RotationDegrees, Is.EqualTo(33.0));
            Assert.That(reread.NoiseSeed, Is.EqualTo(7));
            Assert.That(reread.EnableAberrations, Is.True);
            Assert.That(reread.TiltAngleDegrees, Is.EqualTo(45.0));
            Assert.That(reread.TiltAmountMicrons, Is.EqualTo(15.0));
            Assert.That(reread.BackfocusErrorMicrons, Is.EqualTo(10.0));
            Assert.That(reread.OpticalAxisOffsetXMicrons, Is.EqualTo(100.0));
            Assert.That(reread.OpticalAxisOffsetYMicrons, Is.EqualTo(-50.0));
        });
    }

    [Test]
    public void ResetDefaults_RestoresDocumentedDefaults() {
        var (options, _, _) = Build();
        options.OptimalFocuserPosition = 999;
        options.SensorModel = SonySensorModel.IMX533;
        options.Filter = SimulatorFilter.SII3;
        options.EnableAberrations = true;
        options.TiltAmountMicrons = 42.0;
        options.OpticalThroughput = 0.5;
        options.AstapCatalogPath = @"D:\somewhere";

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.OptimalFocuserPosition, Is.EqualTo(5000));
            Assert.That(options.SensorModel, Is.EqualTo(SonySensorModel.IMX455));
            Assert.That(options.Filter, Is.EqualTo(SimulatorFilter.L));
            Assert.That(options.EnableAberrations, Is.False);
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(0.0));
            Assert.That(options.OpticalThroughput, Is.EqualTo(0.85));
            Assert.That(options.AstapCatalogPath, Is.EqualTo(CameraSimulatorOptions.DefaultAstapCatalogPath));
        });
    }

    [TestCase(nameof(CameraSimulatorOptions.OptimalFocuserPosition), 123)]
    [TestCase(nameof(CameraSimulatorOptions.FocuserStepSizeMicrons), 1.25)]
    [TestCase(nameof(CameraSimulatorOptions.CentralObstructionEnabled), false)]
    [TestCase(nameof(CameraSimulatorOptions.EnableAberrations), true)]
    [TestCase(nameof(CameraSimulatorOptions.AstapCatalogPath), @"C:\astap")]
    public void Setter_RaisesPropertyChanged(string propertyName, object newValue) {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var prop = typeof(CameraSimulatorOptions).GetProperty(propertyName);
        prop.SetValue(options, Convert.ChangeType(newValue, prop.PropertyType));
        Assert.That(raised, Does.Contain(propertyName));
    }

    [Test]
    public void Gain_ClampsToSelectedSensorMaxGain() {
        var (options, _, _) = Build();
        options.SensorModel = SonySensorModel.IMX455; // MaxGain 300
        options.Gain = 9999;
        Assert.That(options.Gain, Is.EqualTo(300));
    }

    [Test]
    public void Gain_ClampsNegativeToZero() {
        var (options, _, _) = Build();
        options.Gain = -50;
        Assert.That(options.Gain, Is.EqualTo(0));
    }

    [Test]
    public void SensorModelChange_ReclampsGainToNewSensorRange() {
        var (options, _, _) = Build();
        options.SensorModel = SonySensorModel.IMX294; // MaxGain 400
        options.Gain = 400;
        Assert.That(options.Gain, Is.EqualTo(400));

        options.SensorModel = SonySensorModel.IMX455; // MaxGain 300 → gain re-clamps
        Assert.That(options.Gain, Is.EqualTo(300));
    }

    [Test]
    public void ProfileChanged_ReinitializesOptions() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new CameraSimulatorOptions(profile, store);
        options.OptimalFocuserPosition = 8888;
        Assert.That(options.OptimalFocuserPosition, Is.EqualTo(8888));

        store.Clear();
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.That(options.OptimalFocuserPosition, Is.EqualTo(5000));
    }

    [Test]
    public void Constructor_ThrowsOnNullAccessor() {
        var profile = Substitute.For<IProfileService>();
        Assert.Throws<ArgumentNullException>(() => new CameraSimulatorOptions(profile, null));
    }

    [Test]
    public void SimTiltAdapterDefaults_MatchDesign() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.SimScrewCount, Is.EqualTo(3));
            Assert.That(options.SimScrew1AngleDegrees, Is.EqualTo(0.0));
            Assert.That(options.SimScrew2AngleDegrees, Is.EqualTo(120.0));
            Assert.That(options.SimScrew3AngleDegrees, Is.EqualTo(240.0));
            Assert.That(double.IsNaN(options.SimScrew4AngleDegrees), Is.True);
            Assert.That(options.SimScrewInwardCurvatureSign,
                Is.EqualTo(TiltScrewGeometry.DefaultScrewInwardCurvatureSign));
            Assert.That(options.SimAdjustmentType, Is.EqualTo(TiltAdjustmentType.Screws));
            Assert.That(options.SimThreadPitchMicrons, Is.EqualTo(500.0));
            Assert.That(options.SimStepperStepSizeMicrons, Is.EqualTo(1.0));
            Assert.That(options.SimScrewRadiusMillimeters, Is.EqualTo(30.0));
            Assert.That(options.ShowSimulatorTiltAdapterPanel, Is.False);
        });
    }

    [Test]
    public void SimTiltAdapterOptions_PersistAndReadBack() {
        var store = new InMemoryPluginOptionsAccessor();
        var a = new CameraSimulatorOptions(Substitute.For<IProfileService>(), store);
        a.SimScrewCount = 4;
        a.SimScrew4AngleDegrees = 270.0;
        a.SimThreadPitchMicrons = 350.0;
        a.ShowSimulatorTiltAdapterPanel = true;

        var b = new CameraSimulatorOptions(Substitute.For<IProfileService>(), store);
        Assert.Multiple(() => {
            Assert.That(b.SimScrewCount, Is.EqualTo(4));
            Assert.That(b.SimScrew4AngleDegrees, Is.EqualTo(270.0));
            Assert.That(b.SimThreadPitchMicrons, Is.EqualTo(350.0));
            Assert.That(b.ShowSimulatorTiltAdapterPanel, Is.True);
        });
    }

    /// <summary>
    /// Screw 4 defaults to <c>double.NaN</c> (the 3-screw convention), and <c>NaN != NaN</c> is always true — a
    /// naive <c>if (field != value)</c> guard would fire on every set, re-persisting and raising forever. Setting
    /// NaN over the NaN default must be a no-op.
    /// </summary>
    [Test]
    public void SimScrew4Angle_SettingNaNOverNaNDefault_DoesNotRaiseOrPersist() {
        var (options, store, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        options.SimScrew4AngleDegrees = double.NaN;

        Assert.Multiple(() => {
            Assert.That(raised, Does.Not.Contain(nameof(CameraSimulatorOptions.SimScrew4AngleDegrees)));
            Assert.That(store.Snapshot.ContainsKey(nameof(CameraSimulatorOptions.SimScrew4AngleDegrees)), Is.False);
            Assert.That(double.IsNaN(options.SimScrew4AngleDegrees), Is.True);
        });
    }
}
