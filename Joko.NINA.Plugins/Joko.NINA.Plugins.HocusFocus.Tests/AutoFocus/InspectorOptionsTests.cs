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
            Assert.That(options.SignalAmplification, Is.EqualTo(1));
            Assert.That(options.CenterFocuserBeforeRun, Is.False);
            Assert.That(options.FramesPerPoint, Is.EqualTo(-1));
            Assert.That(options.TimeoutSeconds, Is.EqualTo(-1));
            Assert.That(options.SimpleExposureSeconds, Is.EqualTo(-1));
            Assert.That(options.DetailedAnalysisExposureSeconds, Is.EqualTo(-1));
            Assert.That(options.NumRegionsWide, Is.EqualTo(7));
            Assert.That(options.LoopingExposureAnalysisEnabled, Is.False);
            Assert.That(options.MicronsPerFocuserStep, Is.EqualTo(-1));
            // Default k: standard focuser (increasing position moves the camera AWAY from the objective),
            // which reproduces every caption exactly as it rendered before the setting existed.
            Assert.That(options.FocuserIncreasesTowardObjective, Is.False);
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
            Assert.That(options.FrameReviewEnabled, Is.False);
            Assert.That(options.MaxStarsPerRegion, Is.EqualTo(-1));
            Assert.That(options.InterpolationEnabled, Is.False);
        });
    }

    [Test]
    public void Setters_PersistToAccessor() {
        var (options, store, _) = Build();
        options.StepCount = 5;
        options.StepSize = 50;
        options.SignalAmplification = 3;
        options.CenterFocuserBeforeRun = true;
        options.FramesPerPoint = 3;
        options.NumRegionsWide = 9;
        options.SimpleExposureSeconds = 2.5;
        options.DetailedAnalysisExposureSeconds = 4.0;
        options.LoopingExposureAnalysisEnabled = true;
        options.MicronsPerFocuserStep = 1.5;
        options.FocuserIncreasesTowardObjective = true;
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
        options.FrameReviewEnabled = true;
        options.MaxStarsPerRegion = 50;

        Assert.Multiple(() => {
            Assert.That(store.Snapshot[nameof(InspectorOptions.StepCount)], Is.EqualTo(5));
            Assert.That(store.Snapshot[nameof(InspectorOptions.StepSize)], Is.EqualTo(50));
            Assert.That(store.Snapshot[nameof(InspectorOptions.SignalAmplification)], Is.EqualTo(3));
            Assert.That(store.Snapshot[nameof(InspectorOptions.CenterFocuserBeforeRun)], Is.True);
            Assert.That(store.Snapshot[nameof(InspectorOptions.FramesPerPoint)], Is.EqualTo(3));
            Assert.That(store.Snapshot[nameof(InspectorOptions.SimpleExposureSeconds)], Is.EqualTo(2.5));
            Assert.That(store.Snapshot[nameof(InspectorOptions.DetailedAnalysisExposureSeconds)], Is.EqualTo(4.0));
            Assert.That(store.Snapshot[nameof(InspectorOptions.NumRegionsWide)], Is.EqualTo(9));
            Assert.That(store.Snapshot[nameof(InspectorOptions.LoopingExposureAnalysisEnabled)], Is.True);
            Assert.That(store.Snapshot[nameof(InspectorOptions.MicronsPerFocuserStep)], Is.EqualTo(1.5));
            Assert.That(store.Snapshot[nameof(InspectorOptions.FocuserIncreasesTowardObjective)], Is.True);
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
            Assert.That(store.Snapshot[nameof(InspectorOptions.FrameReviewEnabled)], Is.True);
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

    [TestCase(-5, 1)]
    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(4, 4)]
    public void SignalAmplification_ClampsToAtLeastOne(int assigned, int expected) {
        var (options, _, _) = Build();
        options.SignalAmplification = assigned;
        Assert.That(options.SignalAmplification, Is.EqualTo(expected));
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
        options.SignalAmplification = 4;
        options.CenterFocuserBeforeRun = true;

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.StepCount, Is.EqualTo(-1));
            Assert.That(options.NumRegionsWide, Is.EqualTo(7));
            Assert.That(options.SensorROI, Is.EqualTo(1.0));
            Assert.That(options.MouseOnChartsEnabled, Is.True);
            Assert.That(options.InterpolationAmount, Is.EqualTo(InterpolationAmountEnum.Medium));
            Assert.That(options.SignalAmplification, Is.EqualTo(1));
            Assert.That(options.CenterFocuserBeforeRun, Is.False);
        });
    }

    [TestCase(nameof(InspectorOptions.StepCount), 4)]
    [TestCase(nameof(InspectorOptions.SignalAmplification), 3)]
    [TestCase(nameof(InspectorOptions.CenterFocuserBeforeRun), true)]
    [TestCase(nameof(InspectorOptions.LoopingExposureAnalysisEnabled), true)]
    [TestCase(nameof(InspectorOptions.MicronsPerFocuserStep), 2.5)]
    [TestCase(nameof(InspectorOptions.FocuserIncreasesTowardObjective), true)]
    [TestCase(nameof(InspectorOptions.SensorROI), 0.6)]
    [TestCase(nameof(InspectorOptions.UseRANSAC), false)]
    [TestCase(nameof(InspectorOptions.FrameReviewEnabled), true)]
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
    public void StepCount_ContaminatedByOldTimeoutBug_ResetsToDefaultAndHealsStore() {
        // Profiles written by builds with the old TimeoutSeconds bug can hold a timeout value (e.g. 300)
        // under the StepCount key. Load must reset it to -1 (use profile default) AND write the
        // correction back through the accessor so the store is healed.
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueInt32(nameof(InspectorOptions.StepCount), 300);

        var options = new InspectorOptions(profile, store);

        Assert.Multiple(() => {
            Assert.That(options.StepCount, Is.EqualTo(-1));
            Assert.That(store.GetValueInt32(nameof(InspectorOptions.StepCount), -999), Is.EqualTo(-1));
        });
    }

    [TestCase(-1)]
    [TestCase(4)]
    [TestCase(20)]
    public void StepCount_PlausibleStoredValues_SurviveLoadUnchanged(int stored) {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueInt32(nameof(InspectorOptions.StepCount), stored);

        var options = new InspectorOptions(profile, store);

        Assert.That(options.StepCount, Is.EqualTo(stored));
    }

    [Test]
    public void TimeoutSeconds_PersistsUnderItsOwnKey() {
        var (options, store, _) = Build();
        options.TimeoutSeconds = 120;
        Assert.Multiple(() => {
            Assert.That(store.GetValueInt32(nameof(InspectorOptions.TimeoutSeconds), -999), Is.EqualTo(120));
            Assert.That(store.GetValueInt32(nameof(InspectorOptions.StepCount), -999), Is.EqualTo(-999),
                "TimeoutSeconds must not clobber the StepCount key");
        });
    }

    [Test]
    public void CenterFocuserBeforeRun_DefaultsToOff() {
        var (options, _, _) = Build();
        Assert.That(options.CenterFocuserBeforeRun, Is.False);
    }

    // ---- Focuser step size: driver value, override, and the mismatch flag -----------------------------
    //
    // docs/focuser-step-size-driver-design.md. The driver's reported StepSize is the default source; the
    // persisted MicronsPerFocuserStep is an override. The governing constraint is that "unset" must never
    // become silently WRONG — only ever missing — so the driver value is accepted on exactly one test
    // (finite and > 0) and is never persisted.

    [Test]
    public void EffectiveMicronsPerFocuserStep_ResolvesOverrideThenDriverThenUnset() {
        var (options, _, _) = Build();

        Assert.Multiple(() => {
            Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(-1.0), "neither set");

            options.DriverMicronsPerFocuserStep = 2.5;
            Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(2.5), "driver alone");

            options.MicronsPerFocuserStep = 1.0;
            Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(1.0), "the override wins");

            options.MicronsPerFocuserStep = -1;
            Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(2.5), "clearing the box returns to the driver");
        });
    }

    [TestCase(0.0)]
    [TestCase(-5.0)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void DriverMicronsPerFocuserStep_RejectsUnusableValues(double reported) {
        // The three ways a driver declines to answer, plus infinity. NaN is the one that matters: it fails
        // BOTH `> 0` and `<= 0`, so a `!(value <= 0)` guard would wave it through into the sensor model and
        // produce an all-NaN fit with no error anywhere.
        var (options, _, _) = Build();

        options.DriverMicronsPerFocuserStep = reported;

        Assert.Multiple(() => {
            Assert.That(options.DriverMicronsPerFocuserStep, Is.EqualTo(-1.0));
            Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(-1.0));
        });
    }

    [Test]
    public void DriverMicronsPerFocuserStep_IsStickyAcrossADisconnect() {
        // A disconnected focuser reports StepSize = 0. Dropping that write is the whole stickiness mechanism:
        // an in-flight analysis must not be rescaled because the focuser dropped off the bus.
        var (options, _, _) = Build();
        options.DriverMicronsPerFocuserStep = 3.5;

        options.DriverMicronsPerFocuserStep = 0.0;

        Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(3.5));
    }

    [Test]
    public void DriverMicronsPerFocuserStep_IsNeverPersisted() {
        var (options, store, _) = Build();

        options.DriverMicronsPerFocuserStep = 3.5;

        Assert.That(store.Snapshot.ContainsKey(nameof(IInspectorOptions.DriverMicronsPerFocuserStep)), Is.False,
            "it is live device state — a persisted copy would go stale against a swapped focuser");
    }

    [Test]
    public void ProfileChange_ClearsTheDriverValue_ButNotTheOverride() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new InspectorOptions(profile, store);
        options.MicronsPerFocuserStep = 1.25;
        options.DriverMicronsPerFocuserStep = 3.5;

        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.Multiple(() => {
            Assert.That(options.DriverMicronsPerFocuserStep, Is.EqualTo(-1.0), "a profile swap means a different rig");
            Assert.That(options.MicronsPerFocuserStep, Is.EqualTo(1.25), "the override is persisted and survives");
        });
    }

    [Test]
    public void ResetDefaults_LeavesTheDriverValueAlone() {
        var (options, _, _) = Build();
        options.DriverMicronsPerFocuserStep = 3.5;

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.MicronsPerFocuserStep, Is.EqualTo(-1.0), "the override resets");
            Assert.That(options.DriverMicronsPerFocuserStep, Is.EqualTo(3.5),
                "the driver value is device state, not a default");
        });
    }

    [TestCase(-1.0, 1.0, false, TestName = "Mismatch_NoOverride_NeverFlags")]
    [TestCase(1.0, 0.0, false, TestName = "Mismatch_NoDriverValue_NeverFlags")]
    [TestCase(1.0, double.NaN, false, TestName = "Mismatch_NaNDriverValue_NeverFlags")]
    [TestCase(1.0, 1.005, false, TestName = "Mismatch_WithinOnePercent_Quiet")]
    [TestCase(1.0, 2.0, true, TestName = "Mismatch_DoubleTheDriverValue_Flags")]
    [TestCase(1.0, 0.5, true, TestName = "Mismatch_HalfTheDriverValue_Flags")]
    public void HasFocuserStepSizeMismatch_Matrix(double overrideValue, double driverValue, bool expected) {
        var (options, _, _) = Build();
        options.MicronsPerFocuserStep = overrideValue;
        options.DriverMicronsPerFocuserStep = driverValue;

        Assert.That(options.HasFocuserStepSizeMismatch, Is.EqualTo(expected));
    }

    [Test]
    public void FocuserStepSize_DerivedProperties_RaiseFromBothInputs() {
        // The hint text and the mismatch flag bind to these, and both derived values read BOTH inputs — so
        // either setter must re-raise both, or a driver update leaves a stale hint on screen.
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        options.MicronsPerFocuserStep = 1.0;
        options.DriverMicronsPerFocuserStep = 3.5;

        Assert.Multiple(() => {
            Assert.That(raised.FindAll(n => n == nameof(InspectorOptions.EffectiveMicronsPerFocuserStep)),
                Has.Count.EqualTo(2), "once per input");
            Assert.That(raised.FindAll(n => n == nameof(InspectorOptions.HasFocuserStepSizeMismatch)),
                Has.Count.EqualTo(2), "once per input");
        });
    }

    [Test]
    public void Constructor_ThrowsOnNullAccessor() {
        var profile = Substitute.For<IProfileService>();
        Assert.Throws<ArgumentNullException>(() => new InspectorOptions(profile, null));
    }
}
