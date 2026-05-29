using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

[TestFixture]
public class InspectorOptionsTests {

    private static (InspectorOptions options, InMemoryPluginOptionsAccessor store, IProfileService profile) Build() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new InspectorOptions(profile, store);
        return (options, store, profile);
    }

    [Test]
    public void Defaults_AreLoadedFromAccessor() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.StepCount, Is.EqualTo(-1));
            Assert.That(options.StepSize, Is.EqualTo(-1));
            Assert.That(options.FramesPerPoint, Is.EqualTo(-1));
            Assert.That(options.TimeoutSeconds, Is.EqualTo(-1));
            Assert.That(options.SimpleExposureSeconds, Is.EqualTo(-1));
            Assert.That(options.DetailedAnalysisExposureSeconds, Is.EqualTo(-1));
            Assert.That(options.NumRegionsWide, Is.EqualTo(7));
            Assert.That(options.LoopingExposureAnalysisEnabled, Is.False);
            Assert.That(options.MicronsPerFocuserStep, Is.EqualTo(-1));
            Assert.That(options.EccentricityColorMapEnabled, Is.True);
            Assert.That(options.MouseOnChartsEnabled, Is.True);
            Assert.That(options.SensorCurveModelEnabled, Is.False);
            Assert.That(options.ShowSensorModel, Is.True);
            Assert.That(options.SensorROI, Is.EqualTo(1.0));
            Assert.That(options.CornersROI, Is.EqualTo(1.0));
            Assert.That(options.InterpolationAlgo, Is.EqualTo(InterpolationAlgoEnum.MultiQuadric));
            Assert.That(options.InterpolationAmount, Is.EqualTo(InterpolationAmountEnum.Medium));
            Assert.That(options.FixedSensorCenter, Is.True);
            Assert.That(options.PreviousRunBrightnessDiff, Is.EqualTo(0.1));
            Assert.That(options.StartingBrightnessDiff, Is.EqualTo(-1));
            Assert.That(options.RejectBadBrightnessMatches, Is.False);
            Assert.That(options.RejectBadlyFittingMatches, Is.True);
            Assert.That(options.UseRANSAC, Is.True);
            Assert.That(options.SaveImagesOnReruns, Is.False);
            Assert.That(options.SaveAlignmentImages, Is.False);
            Assert.That(options.MaxStarsPerRegion, Is.EqualTo(-1));
            Assert.That(options.InterpolationEnabled, Is.False);
        });
    }

    [Test]
    public void Setters_PersistToAccessor() {
        var (options, store, _) = Build();
        options.StepCount = 5;
        options.StepSize = 50;
        options.FramesPerPoint = 3;
        options.NumRegionsWide = 9;
        options.SimpleExposureSeconds = 2.5;
        options.DetailedAnalysisExposureSeconds = 4.0;
        options.LoopingExposureAnalysisEnabled = true;
        options.MicronsPerFocuserStep = 1.5;
        options.EccentricityColorMapEnabled = false;
        options.MouseOnChartsEnabled = false;
        options.SensorCurveModelEnabled = true;
        options.ShowSensorModel = false;
        options.SensorROI = 0.5;
        options.CornersROI = 0.75;
        options.InterpolationAlgo = InterpolationAlgoEnum.ThinPlateSpline;
        options.InterpolationAmount = InterpolationAmountEnum.Large;
        options.FixedSensorCenter = false;
        options.PreviousRunBrightnessDiff = 0.2;
        options.StartingBrightnessDiff = 0.3;
        options.RejectBadBrightnessMatches = true;
        options.RejectBadlyFittingMatches = false;
        options.UseRANSAC = false;
        options.SaveImagesOnReruns = true;
        options.SaveAlignmentImages = true;
        options.MaxStarsPerRegion = 50;

        Assert.Multiple(() => {
            Assert.That(store.Snapshot[nameof(InspectorOptions.StepCount)], Is.EqualTo(5));
            Assert.That(store.Snapshot[nameof(InspectorOptions.StepSize)], Is.EqualTo(50));
            Assert.That(store.Snapshot[nameof(InspectorOptions.FramesPerPoint)], Is.EqualTo(3));
            Assert.That(store.Snapshot[nameof(InspectorOptions.SimpleExposureSeconds)], Is.EqualTo(2.5));
            Assert.That(store.Snapshot[nameof(InspectorOptions.DetailedAnalysisExposureSeconds)], Is.EqualTo(4.0));
            Assert.That(store.Snapshot[nameof(InspectorOptions.NumRegionsWide)], Is.EqualTo(9));
            Assert.That(store.Snapshot[nameof(InspectorOptions.LoopingExposureAnalysisEnabled)], Is.True);
            Assert.That(store.Snapshot[nameof(InspectorOptions.MicronsPerFocuserStep)], Is.EqualTo(1.5));
            Assert.That(store.Snapshot[nameof(InspectorOptions.EccentricityColorMapEnabled)], Is.False);
            Assert.That(store.Snapshot[nameof(InspectorOptions.MouseOnChartsEnabled)], Is.False);
            Assert.That(store.Snapshot[nameof(InspectorOptions.SensorCurveModelEnabled)], Is.True);
            Assert.That(store.Snapshot[nameof(InspectorOptions.ShowSensorModel)], Is.False);
            Assert.That(store.Snapshot[nameof(InspectorOptions.SensorROI)], Is.EqualTo(0.5));
            Assert.That(store.Snapshot[nameof(InspectorOptions.CornersROI)], Is.EqualTo(0.75));
            Assert.That(store.Snapshot[nameof(InspectorOptions.InterpolationAlgo)], Is.EqualTo(InterpolationAlgoEnum.ThinPlateSpline));
            Assert.That(store.Snapshot[nameof(InspectorOptions.InterpolationAmount)], Is.EqualTo(InterpolationAmountEnum.Large));
            Assert.That(store.Snapshot[nameof(InspectorOptions.FixedSensorCenter)], Is.False);
            Assert.That(store.Snapshot[nameof(InspectorOptions.PreviousRunBrightnessDiff)], Is.EqualTo(0.2));
            Assert.That(store.Snapshot[nameof(InspectorOptions.StartingBrightnessDiff)], Is.EqualTo(0.3));
            Assert.That(store.Snapshot[nameof(InspectorOptions.RejectBadBrightnessMatches)], Is.True);
            Assert.That(store.Snapshot[nameof(InspectorOptions.RejectBadlyFittingMatches)], Is.False);
            Assert.That(store.Snapshot[nameof(InspectorOptions.UseRANSAC)], Is.False);
            Assert.That(store.Snapshot[nameof(InspectorOptions.SaveImagesOnReruns)], Is.True);
            Assert.That(store.Snapshot[nameof(InspectorOptions.SaveAlignmentImages)], Is.True);
            Assert.That(store.Snapshot[nameof(InspectorOptions.MaxStarsPerRegion)], Is.EqualTo(50));
        });
    }

    [TestCase(0.0, 0.1)]
    [TestCase(0.05, 0.1)]
    [TestCase(0.5, 0.5)]
    [TestCase(1.0, 1.0)]
    [TestCase(2.0, 1.0)]
    [TestCase(double.NaN, 1.0)]
    public void SensorROI_Clamps(double assigned, double expected) {
        var (options, _, _) = Build();
        options.SensorROI = assigned;
        Assert.That(options.SensorROI, Is.EqualTo(expected));
    }

    [TestCase(0.0, 0.1)]
    [TestCase(2.0, 1.0)]
    [TestCase(double.NaN, 1.0)]
    [TestCase(0.5, 0.5)]
    public void CornersROI_Clamps(double assigned, double expected) {
        var (options, _, _) = Build();
        options.CornersROI = assigned;
        Assert.That(options.CornersROI, Is.EqualTo(expected));
    }

    [Test]
    public void BrightnessToleranceHint_ReflectsPreviousRunBrightnessDiff() {
        var (options, _, _) = Build();
        options.PreviousRunBrightnessDiff = 0.42;
        Assert.That(options.BrightnessToleranceHint, Does.Contain("0.42"));
    }

    [Test]
    public void PreviousRunBrightnessDiff_RaisesBrightnessToleranceHint() {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        options.PreviousRunBrightnessDiff = 0.5;
        Assert.That(raised, Does.Contain("BrightnessToleranceHint"));
    }

    [Test]
    public void MaxStarsPerRegionHint_IsConstant() {
        var (options, _, _) = Build();
        Assert.That(options.MaxStarsPerRegionHint, Does.Contain("unlimited"));
    }

    [Test]
    public void ResetDefaults_RestoresDocumentedDefaults() {
        var (options, _, _) = Build();
        options.StepCount = 7;
        options.NumRegionsWide = 10;
        options.SensorROI = 0.5;
        options.MouseOnChartsEnabled = false;

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.StepCount, Is.EqualTo(-1));
            Assert.That(options.NumRegionsWide, Is.EqualTo(7));
            Assert.That(options.SensorROI, Is.EqualTo(1.0));
            Assert.That(options.MouseOnChartsEnabled, Is.True);
            Assert.That(options.InterpolationAmount, Is.EqualTo(InterpolationAmountEnum.Medium));
        });
    }

    [TestCase(nameof(InspectorOptions.StepCount), 4)]
    [TestCase(nameof(InspectorOptions.LoopingExposureAnalysisEnabled), true)]
    [TestCase(nameof(InspectorOptions.MicronsPerFocuserStep), 2.5)]
    [TestCase(nameof(InspectorOptions.SensorROI), 0.6)]
    [TestCase(nameof(InspectorOptions.UseRANSAC), false)]
    [TestCase(nameof(InspectorOptions.MaxStarsPerRegion), 30)]
    public void Setter_RaisesPropertyChanged(string propertyName, object newValue) {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var prop = typeof(InspectorOptions).GetProperty(propertyName);
        prop.SetValue(options, Convert.ChangeType(newValue, prop.PropertyType));
        Assert.That(raised, Does.Contain(propertyName));
    }

    [Test]
    public void ProfileChanged_ReinitializesOptions() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new InspectorOptions(profile, store);
        options.NumRegionsWide = 11;
        Assert.That(options.NumRegionsWide, Is.EqualTo(11));

        store.Clear();
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.That(options.NumRegionsWide, Is.EqualTo(7));
    }

    [Test]
    public void AcceptableRSquaredMin_HasDocumentedDefault() {
        var (options, _, _) = Build();
        Assert.That(options.AcceptableRSquaredMin, Is.EqualTo(0.05));
    }

    [Test]
    public void AcceptableRSquaredMin_PersistsToAccessor() {
        var (options, store, _) = Build();
        options.AcceptableRSquaredMin = 0.2;
        Assert.That(store.Snapshot[nameof(InspectorOptions.AcceptableRSquaredMin)], Is.EqualTo(0.2));
    }

    [Test]
    public void AcceptableRSquaredMin_ResetsToDefault() {
        var (options, _, _) = Build();
        options.AcceptableRSquaredMin = 0.2;

        options.ResetDefaults();

        Assert.That(options.AcceptableRSquaredMin, Is.EqualTo(0.05));
    }

    [Test]
    public void Constructor_ThrowsOnNullAccessor() {
        var profile = Substitute.For<IProfileService>();
        Assert.Throws<ArgumentNullException>(() => new InspectorOptions(profile, null));
    }
}
