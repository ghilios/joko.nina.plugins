using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
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
        options.DefocusAwareGates = true;
        options.DefocusDistortionSizeReference = 25.0;
        options.DefocusDistortionMinFactor = 0.3;
        options.DefocusCenteringToleranceFactor = 2.5;
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
            Assert.That(store.Snapshot["DefocusAwareGates"], Is.True);
            Assert.That(store.Snapshot["DefocusDistortionSizeReference"], Is.EqualTo(25.0));
            Assert.That(store.Snapshot["DefocusDistortionMinFactor"], Is.EqualTo(0.3));
            Assert.That(store.Snapshot["DefocusCenteringToleranceFactor"], Is.EqualTo(2.5));
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
    public void SimpleMode_PixelScaleLongFocalLength_IncreasesSensitivity() {
        // Longer focal length spreads star flux over more pixels, so detection must be MORE sensitive than
        // Typical. BrightnessSensitivity is a threshold where SMALLER = more sensitive (regression guard for
        // the previously-inverted sign: it used to be raised to 12, making LongFocalLength LESS sensitive).
        // Values are honest σ multiples after the F4 recalibration (10→2.0 baseline, deltas ×0.2).
        var (typical, _, _) = Build();
        typical.UseAdvanced = false;
        typical.Simple_PixelScale = PixelScaleEnum.Typical;
        typical.Simple_FocusRange = FocusRangeEnum.Typical;

        var (longFl, _, _) = Build();
        longFl.UseAdvanced = false;
        longFl.Simple_PixelScale = PixelScaleEnum.LongFocalLength;
        longFl.Simple_FocusRange = FocusRangeEnum.Typical;

        Assert.Multiple(() => {
            Assert.That(typical.BrightnessSensitivity, Is.EqualTo(2.0));
            Assert.That(longFl.BrightnessSensitivity, Is.EqualTo(1.6));
            Assert.That(longFl.BrightnessSensitivity, Is.LessThan(typical.BrightnessSensitivity));
        });
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
        // Values are honest σ multiples after the F4 recalibration (10→2.0 baseline, deltas ×0.2).
        var (typical, _, _) = Build();
        typical.UseAdvanced = false;
        typical.Simple_PixelScale = PixelScaleEnum.Typical;
        typical.Simple_FocusRange = FocusRangeEnum.Typical;

        var (wide, _, _) = Build();
        wide.UseAdvanced = false;
        wide.Simple_PixelScale = PixelScaleEnum.Typical;
        wide.Simple_FocusRange = FocusRangeEnum.WideRange;

        Assert.Multiple(() => {
            Assert.That(typical.BrightnessSensitivity, Is.EqualTo(2.0));
            Assert.That(wide.BrightnessSensitivity, Is.EqualTo(1.6));
            Assert.That(wide.BrightnessSensitivity, Is.LessThan(typical.BrightnessSensitivity));
        });
    }

    [Test]
    public void SimpleMode_NoiseLevelNone_KeepsUncompensatedBrightnessSensitivity() {
        // The None preset never blurred the structure copy, so its σ was already honest — BrightnessSensitivity
        // must NOT be compensated (it would become far more permissive than today). StarClippingMultiplier is
        // now a uniform empirical τ level (F3, gate-only at 2.0σ), no longer per-preset compensation.
        var (options, _, _) = Build();
        options.UseAdvanced = false;
        options.Simple_NoiseLevel = NoiseLevelEnum.None;
        options.Simple_PixelScale = PixelScaleEnum.Typical;
        options.Simple_FocusRange = FocusRangeEnum.Typical;
        Assert.Multiple(() => {
            Assert.That(options.BrightnessSensitivity, Is.EqualTo(10.0));
            Assert.That(options.StarClippingMultiplier, Is.EqualTo(2.0));
        });
    }

    [Test]
    public void SimpleMode_NoiseLevelHigh_KeepsUncompensatedBrightnessSensitivity() {
        // High blurs the measured image itself (measurement noise reduction on), so σ was already
        // consistent — BrightnessSensitivity must NOT be compensated. StarClippingMultiplier is now a uniform
        // empirical τ level (F3, gate-only at 2.0σ), no longer per-preset compensation.
        var (options, _, _) = Build();
        options.UseAdvanced = false;
        options.Simple_NoiseLevel = NoiseLevelEnum.High;
        options.Simple_PixelScale = PixelScaleEnum.Typical;
        options.Simple_FocusRange = FocusRangeEnum.Typical;
        Assert.Multiple(() => {
            Assert.That(options.BrightnessSensitivity, Is.EqualTo(10.0));
            Assert.That(options.StarClippingMultiplier, Is.EqualTo(2.0));
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
    public void DefocusAwareGates_DefaultsOff() {
        var (options, _, _) = Build();
        Assert.That(options.DefocusAwareGates, Is.False);
    }

    [Test]
    public void DefocusTunables_DefaultToParamDefaults() {
        // Defaults must equal the StarDetectorParams class defaults so detection stays bit-identical when the
        // gates are off and unchanged when first turned on.
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.DefocusDistortionSizeReference, Is.EqualTo(30.0));
            Assert.That(options.DefocusDistortionMinFactor, Is.EqualTo(0.25));
            Assert.That(options.DefocusCenteringToleranceFactor, Is.EqualTo(2.0));
        });
    }

    [Test]
    public void DefocusDistortionSizeReference_RejectsZeroOrNegative() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.DefocusDistortionSizeReference = 0);
        Assert.Throws<ArgumentException>(() => options.DefocusDistortionSizeReference = -5.0);
        Assert.Throws<ArgumentException>(() => options.DefocusDistortionSizeReference = 0.5);
        Assert.Throws<ArgumentException>(() => options.DefocusDistortionSizeReference = 1001.0);
    }

    [Test]
    public void DefocusDistortionMinFactor_RejectsOutOfRange() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.DefocusDistortionMinFactor = 0);
        Assert.Throws<ArgumentException>(() => options.DefocusDistortionMinFactor = 0.005);
        Assert.Throws<ArgumentException>(() => options.DefocusDistortionMinFactor = 1.5);
    }

    [Test]
    public void DefocusCenteringToleranceFactor_RejectsOutOfRange() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        Assert.Throws<ArgumentException>(() => options.DefocusCenteringToleranceFactor = 0.5);
        Assert.Throws<ArgumentException>(() => options.DefocusCenteringToleranceFactor = 10.5);
    }

    [Test]
    public void ResetDefaults_RestoresDefocusGateDefaults() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        options.DefocusAwareGates = true;
        options.DefocusDistortionSizeReference = 25.0;
        options.DefocusDistortionMinFactor = 0.3;
        options.DefocusCenteringToleranceFactor = 2.5;

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.DefocusAwareGates, Is.False);
            Assert.That(options.DefocusDistortionSizeReference, Is.EqualTo(30.0));
            Assert.That(options.DefocusDistortionMinFactor, Is.EqualTo(0.25));
            Assert.That(options.DefocusCenteringToleranceFactor, Is.EqualTo(2.0));
        });
    }

    [Test]
    public void DefocusGates_RoundTripThroughBuildStarDetectorParams() {
        // The single DefocusAwareGates toggle must drive BOTH detector-param flags, and the three numeric knobs
        // must flow through unchanged.
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        options.DefocusAwareDonutDetection = true; // master gate: required for the gate flags to flow through
        options.DefocusAwareGates = true;
        options.DefocusDistortionSizeReference = 22.0;
        options.DefocusDistortionMinFactor = 0.4;
        options.DefocusCenteringToleranceFactor = 3.0;

        var p = HocusFocusStarDetection.BuildStarDetectorParams(options);

        Assert.Multiple(() => {
            Assert.That(p.DefocusAwareDistortion, Is.True);
            Assert.That(p.DefocusAwareCentering, Is.True);
            Assert.That(p.DefocusDistortionSizeReference, Is.EqualTo(22.0));
            Assert.That(p.DefocusDistortionMinFactor, Is.EqualTo(0.4));
            Assert.That(p.DefocusCenteringToleranceFactor, Is.EqualTo(3.0));
        });
    }

    [Test]
    public void DefocusGatesOff_BothDetectorFlagsOff() {
        var (options, _, _) = Build();
        var p = HocusFocusStarDetection.BuildStarDetectorParams(options);
        Assert.Multiple(() => {
            Assert.That(p.DefocusAwareDistortion, Is.False);
            Assert.That(p.DefocusAwareCentering, Is.False);
        });
    }

    [Test]
    public void BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild() {
        // The optimizer's "fully-default" seed must equal what BuildStarDetectorParams produces from a freshly
        // reset options object — for EVERY option-derived field. If a default ever changes in only one place
        // (ResetDefaults or BuildDefaultStarDetectorParams), this test fails loudly.
        var (options, _, _) = Build();
        options.ResetDefaults();
        var fromOptions = HocusFocusStarDetection.BuildStarDetectorParams(options);
        var fromDefault = HocusFocusStarDetection.BuildDefaultStarDetectorParams();

        Assert.Multiple(() => {
            Assert.That(fromDefault.ModelPSF, Is.EqualTo(fromOptions.ModelPSF));
            Assert.That(fromDefault.StarMeasurementNoiseReductionEnabled, Is.EqualTo(fromOptions.StarMeasurementNoiseReductionEnabled));
            Assert.That(fromDefault.PSFFitType, Is.EqualTo(fromOptions.PSFFitType));
            Assert.That(fromDefault.HotpixelFiltering, Is.EqualTo(fromOptions.HotpixelFiltering));
            Assert.That(fromDefault.HotpixelThresholdingEnabled, Is.EqualTo(fromOptions.HotpixelThresholdingEnabled));
            Assert.That(fromDefault.NoiseReductionRadius, Is.EqualTo(fromOptions.NoiseReductionRadius));
            Assert.That(fromDefault.NoiseClippingMultiplier, Is.EqualTo(fromOptions.NoiseClippingMultiplier));
            Assert.That(fromDefault.StarClippingMultiplier, Is.EqualTo(fromOptions.StarClippingMultiplier));
            Assert.That(fromDefault.ContaminationSensitivity, Is.EqualTo(fromOptions.ContaminationSensitivity));
            Assert.That(fromDefault.RejectContaminatedStars, Is.EqualTo(fromOptions.RejectContaminatedStars));
            Assert.That(fromDefault.StructureLayers, Is.EqualTo(fromOptions.StructureLayers));
            Assert.That(fromDefault.DefocusAwareStructure, Is.EqualTo(fromOptions.DefocusAwareStructure));
            Assert.That(fromDefault.StructureLayerBoost, Is.EqualTo(fromOptions.StructureLayerBoost));
            Assert.That(fromDefault.Sensitivity, Is.EqualTo(fromOptions.Sensitivity));
            Assert.That(fromDefault.PeakResponse, Is.EqualTo(fromOptions.PeakResponse));
            Assert.That(fromDefault.MaxDistortion, Is.EqualTo(fromOptions.MaxDistortion));
            Assert.That(fromDefault.DefocusAwareDistortion, Is.EqualTo(fromOptions.DefocusAwareDistortion));
            Assert.That(fromDefault.DefocusAwareCentering, Is.EqualTo(fromOptions.DefocusAwareCentering));
            Assert.That(fromDefault.DefocusDistortionSizeReference, Is.EqualTo(fromOptions.DefocusDistortionSizeReference));
            Assert.That(fromDefault.DefocusDistortionMinFactor, Is.EqualTo(fromOptions.DefocusDistortionMinFactor));
            Assert.That(fromDefault.DefocusCenteringToleranceFactor, Is.EqualTo(fromOptions.DefocusCenteringToleranceFactor));
            Assert.That(fromDefault.StarCenterTolerance, Is.EqualTo(fromOptions.StarCenterTolerance));
            Assert.That(fromDefault.BackgroundBoxExpansion, Is.EqualTo(fromOptions.BackgroundBoxExpansion));
            Assert.That(fromDefault.MinimumStarBoundingBoxSize, Is.EqualTo(fromOptions.MinimumStarBoundingBoxSize));
            Assert.That(fromDefault.MinHFR, Is.EqualTo(fromOptions.MinHFR));
            Assert.That(fromDefault.StructureDilationSize, Is.EqualTo(fromOptions.StructureDilationSize));
            Assert.That(fromDefault.StructureDilationCount, Is.EqualTo(fromOptions.StructureDilationCount));
            Assert.That(fromDefault.AnalysisSamplingSize, Is.EqualTo(fromOptions.AnalysisSamplingSize));
            Assert.That(fromDefault.StoreStructureMap, Is.EqualTo(fromOptions.StoreStructureMap));
            Assert.That(fromDefault.SaveIntermediateFilesPath, Is.EqualTo(fromOptions.SaveIntermediateFilesPath));
            Assert.That(fromDefault.PSFParallelPartitionSize, Is.EqualTo(fromOptions.PSFParallelPartitionSize));
            Assert.That(fromDefault.PSFResolution, Is.EqualTo(fromOptions.PSFResolution));
            Assert.That(fromDefault.PSFGoodnessOfFitThreshold, Is.EqualTo(fromOptions.PSFGoodnessOfFitThreshold));
            Assert.That(fromDefault.UsePSFAbsoluteDeviation, Is.EqualTo(fromOptions.UsePSFAbsoluteDeviation));
            Assert.That(fromDefault.HotpixelThreshold, Is.EqualTo(fromOptions.HotpixelThreshold));
            Assert.That(fromDefault.SaturationThreshold, Is.EqualTo(fromOptions.SaturationThreshold));
            Assert.That(fromDefault.PSFPixelIntegration, Is.EqualTo(fromOptions.PSFPixelIntegration));
            Assert.That(fromDefault.MaxStarEvaluationParallelism, Is.EqualTo(fromOptions.MaxStarEvaluationParallelism));
        });
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
    [TestCase(nameof(StarDetectionOptions.DefocusAwareGates), true)]
    [TestCase(nameof(StarDetectionOptions.DefocusDistortionSizeReference), 25.0)]
    [TestCase(nameof(StarDetectionOptions.DefocusDistortionMinFactor), 0.3)]
    [TestCase(nameof(StarDetectionOptions.DefocusCenteringToleranceFactor), 2.5)]
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
    public void ResetDefaults_FocusRangePersistedAndNotified_PeakResponseMatchesSimpleMode() {
        var (options, store, _) = Build();
        options.Simple_FocusRange = FocusRangeEnum.WideRange;
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.Simple_FocusRange, Is.EqualTo(FocusRangeEnum.Typical));
            Assert.That(raised, Does.Contain(nameof(StarDetectionOptions.Simple_FocusRange)));
            // Persisted value must match the in-memory value (the old code wrote the backing field only).
            Assert.That(store.GetValueEnum("Simple_FocusRange", FocusRangeEnum.WideRange), Is.EqualTo(FocusRangeEnum.Typical));
            // ResetDefaults and ConfigureSimpleSettings must agree on the canonical default.
            Assert.That(options.StarPeakResponse, Is.EqualTo(0.75));
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

    // ---- Optimized settings snapshot + "Use Optimized Settings" toggle (T1) ----

    private static OptimizedStarDetectionSettings MakeSnapshot() {
        // Distinctive curated values, all within each property's valid range.
        return new OptimizedStarDetectionSettings {
            BrightnessSensitivity = 3.3,
            StarClippingMultiplier = 2.7,
            NoiseClippingMultiplier = 5.5,
            StarPeakResponse = 0.66,
            MaxDistortion = 0.42,
            MinHFR = 1.1,
            StarCenterTolerance = 0.45,
            StructureLayers = 7,
            NoiseReductionRadius = 6,
            MinStarBoundingBoxSize = 8,
            HotpixelThresholdingEnabled = false,
            HotpixelThreshold = 0.02,
            CreatedAtUtc = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc),
            RunCount = 5,
            BaselineJ = 0.9,
            FinalJ = 0.4,
            RecommendedStepSize = 25,
            RecommendedOffsetSteps = 6,
            SchemaVersion = 1
        };
    }

    private static void AssertLiveMatchesSnapshot(IStarDetectionOptions options, OptimizedStarDetectionSettings s) {
        Assert.Multiple(() => {
            Assert.That(options.BrightnessSensitivity, Is.EqualTo(s.BrightnessSensitivity));
            Assert.That(options.StarClippingMultiplier, Is.EqualTo(s.StarClippingMultiplier));
            Assert.That(options.NoiseClippingMultiplier, Is.EqualTo(s.NoiseClippingMultiplier));
            Assert.That(options.StarPeakResponse, Is.EqualTo(s.StarPeakResponse));
            Assert.That(options.MaxDistortion, Is.EqualTo(s.MaxDistortion));
            Assert.That(options.MinHFR, Is.EqualTo(s.MinHFR));
            Assert.That(options.StarCenterTolerance, Is.EqualTo(s.StarCenterTolerance));
            Assert.That(options.StructureLayers, Is.EqualTo(s.StructureLayers));
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(s.NoiseReductionRadius));
            Assert.That(options.MinStarBoundingBoxSize, Is.EqualTo(s.MinStarBoundingBoxSize));
            Assert.That(options.HotpixelThresholdingEnabled, Is.EqualTo(s.HotpixelThresholdingEnabled));
            Assert.That(options.HotpixelThreshold, Is.EqualTo(s.HotpixelThreshold));
        });
    }

    [Test]
    public void OptimizedSettings_DefaultsToNoneAndOff() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.HasOptimizedSettings, Is.False);
            Assert.That(options.UseOptimizedSettings, Is.False);
            Assert.That(options.GetOptimizedSettings(), Is.Null);
        });
    }

    [Test]
    public void ApplyOptimizedSettings_TurnsOnAndDrivesLiveProperties() {
        var (options, _, _) = Build();
        var snapshot = MakeSnapshot();

        options.ApplyOptimizedSettings(snapshot);

        Assert.Multiple(() => {
            Assert.That(options.HasOptimizedSettings, Is.True);
            Assert.That(options.UseOptimizedSettings, Is.True);
            Assert.That(options.UseAdvanced, Is.False);
        });
        AssertLiveMatchesSnapshot(options, snapshot);
    }

    [Test]
    public void ApplyOptimizedSettings_NullThrows() {
        var (options, _, _) = Build();
        Assert.Throws<ArgumentNullException>(() => options.ApplyOptimizedSettings(null));
    }

    [Test]
    public void ApplyOptimizedSettings_ReflectedInBuildStarDetectorParams() {
        var (options, _, _) = Build();
        var snapshot = MakeSnapshot();
        options.ApplyOptimizedSettings(snapshot);

        var p = HocusFocusStarDetection.BuildStarDetectorParams(options);

        Assert.Multiple(() => {
            Assert.That(p.Sensitivity, Is.EqualTo(snapshot.BrightnessSensitivity));
            Assert.That(p.StarClippingMultiplier, Is.EqualTo(snapshot.StarClippingMultiplier));
            Assert.That(p.NoiseClippingMultiplier, Is.EqualTo(snapshot.NoiseClippingMultiplier));
            Assert.That(p.PeakResponse, Is.EqualTo(snapshot.StarPeakResponse));
            Assert.That(p.MaxDistortion, Is.EqualTo(snapshot.MaxDistortion));
            Assert.That(p.MinHFR, Is.EqualTo(snapshot.MinHFR));
            Assert.That(p.StarCenterTolerance, Is.EqualTo(snapshot.StarCenterTolerance));
            Assert.That(p.StructureLayers, Is.EqualTo(snapshot.StructureLayers));
            Assert.That(p.NoiseReductionRadius, Is.EqualTo(snapshot.NoiseReductionRadius));
            Assert.That(p.MinimumStarBoundingBoxSize, Is.EqualTo(snapshot.MinStarBoundingBoxSize));
            Assert.That(p.HotpixelThresholdingEnabled, Is.EqualTo(snapshot.HotpixelThresholdingEnabled));
            Assert.That(p.HotpixelThreshold, Is.EqualTo(snapshot.HotpixelThreshold));
        });
    }

    [Test]
    public void ToggleOptimizedSettingsOff_RestoresPresetDerivedValues() {
        var (options, _, _) = Build();
        // Establish the preset-derived baseline for a fresh Typical/Typical/Typical config.
        var (baseline, _, _) = Build();

        // HotpixelThresholdingEnabled is an input toggle that DerivePresetSettings reads (not re-derives), so
        // use the default (true) here to keep the NoiseReductionRadius derivation aligned with the baseline.
        var snapshot = MakeSnapshot();
        snapshot.HotpixelThresholdingEnabled = true;
        options.ApplyOptimizedSettings(snapshot);
        options.UseOptimizedSettings = false;

        Assert.Multiple(() => {
            Assert.That(options.HasOptimizedSettings, Is.True, "snapshot should still be stored when toggled off");
            Assert.That(options.BrightnessSensitivity, Is.EqualTo(baseline.BrightnessSensitivity));
            Assert.That(options.StarClippingMultiplier, Is.EqualTo(baseline.StarClippingMultiplier));
            Assert.That(options.NoiseClippingMultiplier, Is.EqualTo(baseline.NoiseClippingMultiplier));
            Assert.That(options.StarPeakResponse, Is.EqualTo(baseline.StarPeakResponse));
            Assert.That(options.MaxDistortion, Is.EqualTo(baseline.MaxDistortion));
            Assert.That(options.MinHFR, Is.EqualTo(baseline.MinHFR));
            Assert.That(options.StarCenterTolerance, Is.EqualTo(baseline.StarCenterTolerance));
            Assert.That(options.StructureLayers, Is.EqualTo(baseline.StructureLayers));
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(baseline.NoiseReductionRadius));
            Assert.That(options.MinStarBoundingBoxSize, Is.EqualTo(baseline.MinStarBoundingBoxSize));
            Assert.That(options.HotpixelThresholdingEnabled, Is.EqualTo(baseline.HotpixelThresholdingEnabled));
            Assert.That(options.HotpixelThreshold, Is.EqualTo(baseline.HotpixelThreshold));
        });
    }

    [Test]
    public void OptimizedSettings_PersistAndReloadAcrossNewOptionsInstance() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var snapshot = MakeSnapshot();

        var first = new StarDetectionOptions(profile, store);
        first.ApplyOptimizedSettings(snapshot);

        // A brand new options object over the SAME persisted store must reload the snapshot + toggle.
        var reloaded = new StarDetectionOptions(profile, store);
        Assert.Multiple(() => {
            Assert.That(reloaded.HasOptimizedSettings, Is.True);
            Assert.That(reloaded.UseOptimizedSettings, Is.True);
        });
        AssertLiveMatchesSnapshot(reloaded, snapshot);
    }

    [Test]
    public void ResetDefaults_ClearsOptimizedSettings() {
        var (options, _, _) = Build();
        var (baseline, _, _) = Build();
        options.ApplyOptimizedSettings(MakeSnapshot());

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.HasOptimizedSettings, Is.False);
            Assert.That(options.UseOptimizedSettings, Is.False);
            Assert.That(options.GetOptimizedSettings(), Is.Null);
            // Live values back to preset-derived defaults.
            Assert.That(options.BrightnessSensitivity, Is.EqualTo(baseline.BrightnessSensitivity));
            Assert.That(options.MinHFR, Is.EqualTo(baseline.MinHFR));
            Assert.That(options.StructureLayers, Is.EqualTo(baseline.StructureLayers));
        });
    }

    [Test]
    public void UseOptimizedSettings_DefaultPersistsFalse() {
        var (options, store, _) = Build();
        options.UseAdvanced = true; // avoid simple-mode reconfig recursion noise
        options.UseOptimizedSettings = true;
        Assert.That(store.Snapshot[nameof(StarDetectionOptions.UseOptimizedSettings)], Is.True);
    }

    [Test]
    public void CorruptOptimizedSettingsJson_IsDiscardedAndDoesNotThrow() {
        // A malformed persisted JSON blob must not break options loading: InitializeOptions runs in the
        // constructor (and on every ProfileChanged), so a deserialize failure here would otherwise abort
        // loading entirely. It should be swallowed (logged) and treated as "no snapshot".
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueString("OptimizedSettingsJson", "{ this is not valid json");

        StarDetectionOptions options = null;
        Assert.DoesNotThrow(() => options = new StarDetectionOptions(profile, store));
        Assert.Multiple(() => {
            Assert.That(options.HasOptimizedSettings, Is.False);
            Assert.That(options.GetOptimizedSettings(), Is.Null);
        });
    }

    [Test]
    public void GetOptimizedSettings_ReturnsDefensiveCopy() {
        // Mutating the object returned by GetOptimizedSettings() must not change the stored in-memory snapshot,
        // so a subsequent fetch returns the original curated values.
        var (options, _, _) = Build();
        var snapshot = MakeSnapshot();
        options.ApplyOptimizedSettings(snapshot);

        var fetched = options.GetOptimizedSettings();
        fetched.BrightnessSensitivity += 100.0;
        fetched.StructureLayers += 5;

        var refetched = options.GetOptimizedSettings();
        Assert.Multiple(() => {
            Assert.That(refetched.BrightnessSensitivity, Is.EqualTo(snapshot.BrightnessSensitivity));
            Assert.That(refetched.StructureLayers, Is.EqualTo(snapshot.StructureLayers));
        });
    }
}
