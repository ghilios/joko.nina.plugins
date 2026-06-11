using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

[TestFixture]
public class StarDetectionOptionsTests {

    private static (StarDetectionOptions options, InMemoryPluginOptionsAccessor store, IProfileService profile) Build() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new StarDetectionOptions(profile, store);
        return (options, store, profile);
    }

    [Test]
    public void Defaults_AreLoadedFromAccessor() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.DebugMode, Is.False);
            Assert.That(options.ModelPSF, Is.True);
            Assert.That(options.UseAdvanced, Is.False);
            Assert.That(options.PSFFitType, Is.EqualTo(StarDetectorPSFFitType.Moffat_40));
            Assert.That(options.Simple_NoiseLevel, Is.EqualTo(NoiseLevelEnum.Typical));
            Assert.That(options.Simple_PixelScale, Is.EqualTo(PixelScaleEnum.Typical));
            Assert.That(options.Simple_FocusRange, Is.EqualTo(FocusRangeEnum.Typical));
            Assert.That(options.HotpixelFiltering, Is.True);
            Assert.That(options.HotpixelThresholdingEnabled, Is.True);
            Assert.That(options.UseAutoFocusCrop, Is.True);
            Assert.That(options.StarMeasurementNoiseReductionEnabled, Is.False);
            Assert.That(options.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.Median));
        });
    }

    [Test]
    public void Setters_PersistToAccessor() {
        var (options, store, _) = Build();
        // Switch to advanced first so simple-mode auto-config doesn't override our values.
        options.UseAdvanced = true;
        options.DebugMode = true;
        options.ModelPSF = false;
        options.PSFFitType = StarDetectorPSFFitType.Gaussian;
        options.HotpixelFiltering = false;
        options.HotpixelThresholdingEnabled = false;
        options.UseAutoFocusCrop = false;
        options.StarMeasurementNoiseReductionEnabled = true;
        options.NoiseReductionRadius = 7;
        options.NoiseClippingMultiplier = 5.0;
        options.StarClippingMultiplier = 3.0;
        options.StructureLayers = 6;
        options.BrightnessSensitivity = 12.5;
        options.StarCenterTolerance = 0.5;
        options.StarPeakResponse = 0.8;
        options.MaxDistortion = 0.4;
        options.StarBackgroundBoxExpansion = 4;
        options.MinStarBoundingBoxSize = 6;
        options.MinHFR = 1.0;
        options.StructureDilationSize = 5;
        options.StructureDilationCount = 1;
        options.PixelSampleSize = 0.5;
        options.PSFParallelPartitionSize = 200;
        options.PSFResolution = 12;
        options.PSFFitThreshold = 0.85;
        options.UsePSFAbsoluteDeviation = true;
        options.HotpixelThreshold = 0.01;
        options.SaturationThreshold = 0.95;
        options.MeasurementAverage = MeasurementAverageEnum.MeanOutliers;

        Assert.Multiple(() => {
            Assert.That(store.Snapshot["UseAdvanced"], Is.True);
            Assert.That(store.Snapshot["DetectionDebugMode"], Is.True);
            Assert.That(store.Snapshot["ModelPSF"], Is.False);
            Assert.That(store.Snapshot["PSFFitType"], Is.EqualTo(StarDetectorPSFFitType.Gaussian));
            Assert.That(store.Snapshot["HotpixelFiltering"], Is.False);
            Assert.That(store.Snapshot[nameof(StarDetectionOptions.HotpixelThresholdingEnabled)], Is.False);
            Assert.That(store.Snapshot["UseAutoFocusCrop"], Is.False);
            Assert.That(store.Snapshot[nameof(StarDetectionOptions.StarMeasurementNoiseReductionEnabled)], Is.True);
            Assert.That(store.Snapshot["NoiseReductionRadius"], Is.EqualTo(7));
            Assert.That(store.Snapshot["NoiseClippingMultiplier"], Is.EqualTo(5.0));
            Assert.That(store.Snapshot["StarClippingMultiplier"], Is.EqualTo(3.0));
            Assert.That(store.Snapshot["StructureLayers"], Is.EqualTo(6));
            Assert.That(store.Snapshot["BrightnessSensitivity"], Is.EqualTo(12.5));
            Assert.That(store.Snapshot["StarCenterTolerance"], Is.EqualTo(0.5));
            Assert.That(store.Snapshot["StarPeakResponse"], Is.EqualTo(0.8));
            Assert.That(store.Snapshot["MaxDistortion"], Is.EqualTo(0.4));
            Assert.That(store.Snapshot["StarBackgroundBoxExpansion"], Is.EqualTo(4));
            Assert.That(store.Snapshot["MinStarBoundingBoxSize"], Is.EqualTo(6));
            Assert.That(store.Snapshot["MinHFR"], Is.EqualTo(1.0));
            Assert.That(store.Snapshot["StructureDilationSize"], Is.EqualTo(5));
            Assert.That(store.Snapshot["StructureDilationCount"], Is.EqualTo(1));
            Assert.That(store.Snapshot["PixelSampleSize"], Is.EqualTo(0.5));
            Assert.That(store.Snapshot["PSFParallelPartitionSize"], Is.EqualTo(200));
            Assert.That(store.Snapshot["PSFResolution"], Is.EqualTo(12));
            Assert.That(store.Snapshot["PSFFitThreshold"], Is.EqualTo(0.85));
            Assert.That(store.Snapshot[nameof(StarDetectionOptions.UsePSFAbsoluteDeviation)], Is.True);
            Assert.That(store.Snapshot[nameof(StarDetectionOptions.HotpixelThreshold)], Is.EqualTo(0.01));
            Assert.That(store.Snapshot[nameof(StarDetectionOptions.SaturationThreshold)], Is.EqualTo(0.95));
            Assert.That(store.Snapshot[nameof(StarDetectionOptions.MeasurementAverage)], Is.EqualTo(MeasurementAverageEnum.MeanOutliers));
        });
    }

    [Test]
    public void SimpleMode_ChangingNoiseLevelToHigh_TogglesNoiseReduction() {
        var (options, _, _) = Build();
        options.UseAdvanced = false;
        options.Simple_NoiseLevel = NoiseLevelEnum.High;
        Assert.Multiple(() => {
            Assert.That(options.StarMeasurementNoiseReductionEnabled, Is.True);
            Assert.That(options.NoiseReductionRadius, Is.GreaterThanOrEqualTo(5));
            Assert.That(options.HotpixelFiltering, Is.True);
        });
    }

    [Test]
    public void SimpleMode_NoiseLevelNone_DisablesHotpixelFiltering() {
        var (options, _, _) = Build();
        options.UseAdvanced = false;
        options.Simple_NoiseLevel = NoiseLevelEnum.None;
        Assert.Multiple(() => {
            Assert.That(options.HotpixelFiltering, Is.False);
            Assert.That(options.StarMeasurementNoiseReductionEnabled, Is.False);
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(0));
        });
    }

    [Test]
    public void SimpleMode_PixelScaleWideField_AdjustsStructure() {
        var (options, _, _) = Build();
        options.UseAdvanced = false;
        options.Simple_PixelScale = PixelScaleEnum.WideField;
        Assert.That(options.PixelSampleSize, Is.EqualTo(0.5));
    }

    [Test]
    public void SimpleMode_PixelScaleLongFocalLength_BoostsBrightnessSensitivity() {
        var (options, _, _) = Build();
        options.UseAdvanced = false;
        options.Simple_NoiseLevel = NoiseLevelEnum.Typical;
        options.Simple_PixelScale = PixelScaleEnum.LongFocalLength;
        Assert.That(options.BrightnessSensitivity, Is.GreaterThan(10.0));
    }

    [Test]
    public void SimpleMode_FocusRangeWideRange_BoostsStructureLayers() {
        var (options, _, _) = Build();
        options.UseAdvanced = false;
        options.Simple_PixelScale = PixelScaleEnum.Typical;
        options.Simple_FocusRange = FocusRangeEnum.WideRange;
        Assert.That(options.StructureLayers, Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public void SimpleMode_FocusRangeWideRange_IncreasesSensitivity() {
        // WideRange targets faint/defocused stars, so it must make detection MORE sensitive than Typical.
        // BrightnessSensitivity is a threshold where SMALLER = more sensitive (regression guard for the
        // previously-inverted sign: it used to be raised to 12, making WideRange LESS sensitive).
        var (typical, _, _) = Build();
        typical.UseAdvanced = false;
        typical.Simple_PixelScale = PixelScaleEnum.Typical;
        typical.Simple_FocusRange = FocusRangeEnum.Typical;

        var (wide, _, _) = Build();
        wide.UseAdvanced = false;
        wide.Simple_PixelScale = PixelScaleEnum.Typical;
        wide.Simple_FocusRange = FocusRangeEnum.WideRange;

        Assert.Multiple(() => {
            Assert.That(typical.BrightnessSensitivity, Is.EqualTo(10.0));
            Assert.That(wide.BrightnessSensitivity, Is.EqualTo(8.0));
            Assert.That(wide.BrightnessSensitivity, Is.LessThan(typical.BrightnessSensitivity));
        });
    }

    [Test]
    public void AdvancedMode_DoesNotAutoConfigure() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        options.NoiseReductionRadius = 9;
        options.Simple_NoiseLevel = NoiseLevelEnum.High;
        Assert.That(options.NoiseReductionRadius, Is.EqualTo(9));
    }

    [TestCase(-1)]
    public void NoiseReductionRadius_RejectsNegative(int v) {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.NoiseReductionRadius = v);
    }

    [Test]
    public void StructureLayers_RejectsZeroOrNegative() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.StructureLayers = 0);
        Assert.Throws<ArgumentException>(() => options.StructureLayers = -1);
    }

    [Test]
    public void StarCenterTolerance_RejectsOutOfRange() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.StarCenterTolerance = 0);
        Assert.Throws<ArgumentException>(() => options.StarCenterTolerance = 1.5);
    }

    [Test]
    public void MaxDistortion_RejectsOutOfRange() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.MaxDistortion = -0.1);
        Assert.Throws<ArgumentException>(() => options.MaxDistortion = 1.1);
    }

    [Test]
    public void PixelSampleSize_RejectsOutOfRange() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.PixelSampleSize = 0);
        Assert.Throws<ArgumentException>(() => options.PixelSampleSize = 1.1);
    }

    [Test]
    public void StructureDilationSize_RejectsLessThan3() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.StructureDilationSize = 2);
    }

    [Test]
    public void StarBackgroundBoxExpansion_RejectsLessThan1() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.StarBackgroundBoxExpansion = 0);
    }

    [Test]
    public void MinStarBoundingBoxSize_RejectsLessThan1() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.MinStarBoundingBoxSize = 0);
    }

    [Test]
    public void HotpixelThreshold_RejectsOutOfRange() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.HotpixelThreshold = 0);
        Assert.Throws<ArgumentException>(() => options.HotpixelThreshold = 1.5);
    }

    [Test]
    public void SaturationThreshold_RejectsOutOfRange() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.SaturationThreshold = 0);
        Assert.Throws<ArgumentException>(() => options.SaturationThreshold = 1.5);
    }

    [Test]
    public void PSFFitThreshold_RejectsOutOfRange() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.PSFFitThreshold = 0);
        Assert.Throws<ArgumentException>(() => options.PSFFitThreshold = 1.1);
    }

    [TestCase(nameof(StarDetectionOptions.DebugMode), true)]
    [TestCase(nameof(StarDetectionOptions.UseAdvanced), true)]
    [TestCase(nameof(StarDetectionOptions.ModelPSF), false)]
    [TestCase(nameof(StarDetectionOptions.UseAutoFocusCrop), false)]
    [TestCase(nameof(StarDetectionOptions.HotpixelFiltering), false)]
    [TestCase(nameof(StarDetectionOptions.HotpixelThresholdingEnabled), false)]
    [TestCase(nameof(StarDetectionOptions.UsePSFAbsoluteDeviation), true)]
    public void Setter_RaisesPropertyChanged(string propertyName, object newValue) {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var prop = typeof(StarDetectionOptions).GetProperty(propertyName);
        prop.SetValue(options, Convert.ChangeType(newValue, prop.PropertyType));
        Assert.That(raised, Does.Contain(propertyName));
    }

    [Test]
    public void ResetDefaults_RestoresDocumentedDefaults() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        options.NoiseReductionRadius = 9;
        options.PSFFitType = StarDetectorPSFFitType.Gaussian;
        options.MeasurementAverage = MeasurementAverageEnum.MeanOutliers;

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.UseAdvanced, Is.False);
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(3));
            Assert.That(options.PSFFitType, Is.EqualTo(StarDetectorPSFFitType.Moffat_40));
            Assert.That(options.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.Median));
            Assert.That(options.HotpixelFiltering, Is.True);
        });
    }

    [Test]
    public void ProfileChanged_ReinitializesValues() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new StarDetectionOptions(profile, store);
        options.UseAdvanced = true;
        options.MeasurementAverage = MeasurementAverageEnum.MeanOutliers;
        Assert.That(options.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.MeanOutliers));

        store.Clear();
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.That(options.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.Median));
        Assert.That(options.UseAdvanced, Is.False);
    }

    [Test]
    public void Constructor_ThrowsOnNullAccessor() {
        var profile = Substitute.For<IProfileService>();
        Assert.Throws<ArgumentNullException>(() => new StarDetectionOptions(profile, null));
    }
}
