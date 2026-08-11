using System;
using System.Collections.Generic;
using System.Reflection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile;
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
            // Detection binning defaults OFF. It must never turn itself on: an upgrade that started binning
            // frames would silently invalidate detection settings the user had already tuned.
            Assert.That(options.DetectionBinning, Is.EqualTo(DetectionBinningEnum.Bin1));
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
    public void SimpleMode_PixelScaleLongFocalLength_IsStricterThanTypical() {
        // Longer focal length spreads star flux over more pixels; the preset RAISES BrightnessSensitivity to
        // reject faint fragments that would corrupt the HFR median (BrightnessSensitivity is a threshold where
        // SMALLER = more sensitive, so a HIGHER value = stricter). Matches v3.0.0.26 (see design doc §14).
        var (typical, _, _) = Build();
        typical.UseAdvanced = false;
        typical.Simple_PixelScale = PixelScaleEnum.Typical;
        typical.Simple_FocusRange = FocusRangeEnum.Typical;

        var (longFl, _, _) = Build();
        longFl.UseAdvanced = false;
        longFl.Simple_PixelScale = PixelScaleEnum.LongFocalLength;
        longFl.Simple_FocusRange = FocusRangeEnum.Typical;

        Assert.Multiple(() => {
            Assert.That(typical.BrightnessSensitivity, Is.EqualTo(10.0));
            Assert.That(longFl.BrightnessSensitivity, Is.EqualTo(12.0));
            Assert.That(longFl.BrightnessSensitivity, Is.GreaterThan(typical.BrightnessSensitivity));
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
    public void SimpleMode_FocusRangeWideRange_IsStricterThanTypical() {
        // WideRange reaches heavier defocus, where small noise / donut-fragment detections (tiny HFRs) would
        // corrupt the median HFR, so the preset RAISES BrightnessSensitivity to reject them (a threshold where
        // SMALLER = more sensitive, so a HIGHER value = stricter). Matches v3.0.0.26 (see design doc §14).
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
            Assert.That(wide.BrightnessSensitivity, Is.EqualTo(12.0));
            Assert.That(wide.BrightnessSensitivity, Is.GreaterThan(typical.BrightnessSensitivity));
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
    public void BuildDefaultStarDetectorParams_MatchesConstructedOptionsBuild() {
        // The optimizer's "fully-default" seed must equal what BuildStarDetectorParams produces from the options
        // object A REAL LOAD PRODUCES — a plain construction over a blank accessor — for EVERY option-derived
        // field. If a default ever changes in only one place, this test fails loudly.
        //
        // F70: this used to call ResetDefaults() first. That was the ONE path on which the two sides agreed about
        // NoiseReductionRadius, and it was a path no profile load takes: the bare literal, reached only when the
        // preset derivation happened not to re-enter. The path every load DOES take disagreed by one and nothing
        // in the repo looked at it. The reset path is not lost — ResetDefaults_EqualsFreshConstruction_* pins it
        // to this same construction, over every property and from four entry states.
        var (options, _, _) = Build();
        var fromOptions = HocusFocusStarDetection.BuildStarDetectorParams(options);
        var fromDefault = HocusFocusStarDetection.BuildDefaultStarDetectorParams();

        Assert.Multiple(() => {
            Assert.That(fromDefault.ModelPSF, Is.EqualTo(fromOptions.ModelPSF));
            Assert.That(fromDefault.StarMeasurementNoiseReductionEnabled, Is.EqualTo(fromOptions.StarMeasurementNoiseReductionEnabled));
            Assert.That(fromDefault.PSFFitType, Is.EqualTo(fromOptions.PSFFitType));
            Assert.That(fromDefault.HotpixelFiltering, Is.EqualTo(fromOptions.HotpixelFiltering));
            Assert.That(fromDefault.HotpixelThresholdingEnabled, Is.EqualTo(fromOptions.HotpixelThresholdingEnabled));
            // ---- F70: the ONE named exception, asserted in BOTH directions so either side moving fails loudly.
            // BuildDefaultStarDetectorParams carries the Typical preset's PRE-compensation base (3); every
            // constructed options object carries the +1 DerivePresetSettings adds when hotpixel thresholding and
            // filtering are both on (StarDetectionOptions.cs:221-224) — and the assertion two lines up is what
            // says they are both on here, so this exception cannot be read as an unconditional licence.
            // Whether the seed should follow the shipped default is RULE N18's question, not this test's.
            Assert.That(fromDefault.NoiseReductionRadius, Is.EqualTo(3), "the optimizer seed's hardcoded literal");
            Assert.That(fromOptions.NoiseReductionRadius, Is.EqualTo(4), "the value a real load produces");
            // ---- end of the named exception; everything below is plain equality again.
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
            Assert.That(fromDefault.MeasurementAverage, Is.EqualTo(fromOptions.MeasurementAverage));
        });
    }

    // ---- F70: ResetDefaults() must be deterministic and must equal a construction ----

    // Excluded from the "reset == fresh construction" contract: the three DetectionBinning*Hint members are
    // view-only strings/flags computed from the last MEASURED in-focus HFR (one of them embeds a timestamp).
    // They are not defaults, and neither ResetDefaults nor construction owns them.
    private static readonly HashSet<string> NotPartOfDefaultState = new() {
        nameof(StarDetectionOptions.DetectionBinningHint),
        nameof(StarDetectionOptions.DetectionBinningHintDetail),
        nameof(StarDetectionOptions.DetectionBinningRecommendationVisible),
    };

    private static void AssertStateEqualsFreshConstruction(StarDetectionOptions actual, string entryState) {
        var (expected, _, _) = Build();
        var mismatches = new List<string>();
        foreach (var prop in typeof(StarDetectionOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0 || NotPartOfDefaultState.Contains(prop.Name)) {
                continue;
            }
            var a = prop.GetValue(actual);
            var e = prop.GetValue(expected);
            if (!Equals(a, e)) {
                mismatches.Add($"{prop.Name}: after ResetDefaults={a}, fresh construction={e}");
            }
        }
        Assert.That(mismatches, Is.Empty,
            $"ResetDefaults() from entry state '{entryState}' must leave the state a construction produces");
        Assert.That(actual.GetOptimizedSettings(), Is.Null, "ResetDefaults must clear the optimized snapshot");
    }

    // T2 — the contract, from four NAMED entry states. Reset state must equal constructed state for EVERY
    // property, not just for the one field F70 named. Three of these four fail against the pre-change source
    // (NoiseReductionRadius 3 vs 4); the fourth — the snapshot case — passed before the fix and still passes,
    // and the pair of it with the virgin case IS the non-determinism (see the dedicated test below).

    [Test]
    public void ResetDefaults_EqualsFreshConstruction_FromVirginState() {
        var (options, _, _) = Build();
        options.ResetDefaults();
        AssertStateEqualsFreshConstruction(options, "virgin");
    }

    [Test]
    public void ResetDefaults_EqualsFreshConstruction_FromAdvancedWithNonDefaultKnobs() {
        var (options, _, _) = Build();
        options.UseAdvanced = true;
        options.NoiseReductionRadius = 9;
        options.MinHFR = 2.5;
        options.StructureLayers = 7;
        options.ResetDefaults();
        AssertStateEqualsFreshConstruction(options, "Advanced mode with non-default knobs");
    }

    [Test]
    public void ResetDefaults_EqualsFreshConstruction_FromOptimizedSnapshotApplied() {
        var (options, _, _) = Build();
        options.ApplyOptimizedSettings(MakeSnapshot());
        Assert.That(options.UseOptimizedSettings, Is.True, "precondition: this entry state has the flag ON");
        options.ResetDefaults();
        AssertStateEqualsFreshConstruction(options, "UseOptimizedSettings = true with a snapshot applied");
    }

    [Test]
    public void ResetDefaults_EqualsFreshConstruction_FromNonDefaultSimplePresets() {
        var (options, _, _) = Build();
        options.Simple_NoiseLevel = NoiseLevelEnum.High;
        options.Simple_PixelScale = PixelScaleEnum.WideField;
        options.Simple_FocusRange = FocusRangeEnum.WideRange;
        options.ResetDefaults();
        AssertStateEqualsFreshConstruction(options, "non-default Simple_* presets");
    }

    // ---- F70(b'): the preset-owned PARTITION, probed at runtime -------------------------------------------
    //
    // ResetDefaultsImpl assigns 53 public properties. Exactly 20 of them are ALSO assigned by
    // DerivePresetSettings, which ResetDefaultsImpl re-runs unconditionally as its last statement -- so those 20
    // literals are dead and the derivation is the authority. The other 33 are live defaults.
    //
    // The register carried that as a hand-counted "20" with no membership, and the membership is the dangerous
    // half: Simple_NoiseLevel, Simple_PixelScale and Simple_FocusRange are the derivation's own INPUTS, and
    // HotpixelThresholdingEnabled is READ by it (the NoiseReductionRadius += 1 hotpixel compensation) but never
    // assigned. All four look preset-related; deleting them breaks the class. These tests pin the membership so
    // a future edit cannot move a property between the halves unnoticed.
    //
    // The probe needs no source parsing: set a sentinel, fire the derivation by toggling Simple_FocusRange away
    // and back, then see whether the value REVERTS (owned) or SURVIVES (not owned). Every case asserts the
    // sentinel write actually landed first -- a clamping or no-op setter must report could-not-look rather than
    // pass silently (F66).

    private static readonly string[] DerivationOwnedProperties = {
        "BrightnessSensitivity", "HotpixelFiltering", "HotpixelThreshold", "MaxDistortion", "MinHFR",
        "MinStarBoundingBoxSize", "NoiseClippingMultiplier", "NoiseReductionRadius", "PSFFitThreshold",
        "PSFFitType", "PSFResolution", "PixelSampleSize", "StarBackgroundBoxExpansion", "StarCenterTolerance",
        "StarClippingMultiplier", "StarMeasurementNoiseReductionEnabled", "StarPeakResponse",
        "StructureDilationCount", "StructureDilationSize", "StructureLayers",
    };

    // Live defaults the probe can drive safely. Deliberately NOT the whole 33: UseAdvanced would switch the
    // object out of Simple mode and stop the derivation from running at all, UseOptimizedSettings re-enters the
    // derivation from its own setter, IntermediateSavePath creates directories, and Simple_FocusRange is the
    // trigger itself. Those four are excluded BY NAME rather than quietly dropped -- a literal the test cannot
    // cover stays uncovered and says so.
    private static readonly string[] NotDerivationOwnedProperties = {
        "AdaptiveNoiseBlockSize", "ContaminationSensitivity", "DebugMode", "DefocusAwareDonutDetection",
        "DefocusAwareGates", "DefocusAwareStructure", "DefocusCenteringToleranceFactor",
        "DefocusDistortionMinFactor", "DefocusDistortionSizeReference", "DetectionBinning",
        "DonutMaxStreakEccentricity", "DonutMinAnnularityHoleFraction", "DonutMorphCloseSize",
        "DonutSaturationBloomRadius", "ExcludeSaturatedStarsFromHFR", "HotpixelThresholdingEnabled",
        "LocallyAdaptiveBinarization", "MeasurementAverage", "ModelPSF", "PSFParallelPartitionSize",
        "PSFPixelIntegration", "RejectContaminatedStars", "SaturationThreshold", "SaveIntermediateImages",
        "Simple_NoiseLevel", "Simple_PixelScale", "StructureLayerBoost", "UsePSFAbsoluteDeviation",
        // Recovered from the exclusion list: each is drivable once the probe can pick a second trigger.
        // Simple_FocusRange only needed a trigger that is not itself; IntermediateSavePath never reaches the
        // directory-creating code (that lives in ResetDefaultsImpl, which the probe does not call); and
        // UseOptimizedSettings is inert here because ConfigureSimpleSettings requires HasOptimizedSettings too,
        // which is false on a virgin object.
        "Simple_FocusRange", "IntermediateSavePath", "UseOptimizedSettings",
    };

    // One property remains undrivable and the reason is mechanical, not effort: setting UseAdvanced is the one
    // write that stops the derivation from running at all (ConfigureSimpleSettings returns immediately), so the
    // probe cannot distinguish "the derivation left it alone" from "the derivation never ran". It stays
    // uncovered, and PresetOwnedPartition_CountsAndDisjointness_ArePinned asserts it never sneaks into either
    // list -- an uncovered literal must not masquerade as a covered one.
    private static readonly string[] ProbeExcludedByName = { "UseAdvanced" };

    // The trigger cannot be the property under test, so the probe keeps two and picks the one that is not the
    // subject. Both are inputs to DerivePresetSettings, so either fires a complete re-derivation.
    private static void FireDerivation(StarDetectionOptions options, string subject = null) {
        if (subject == "Simple_FocusRange") {
            var wasScale = options.Simple_PixelScale;
            options.Simple_PixelScale = wasScale == PixelScaleEnum.Typical ? PixelScaleEnum.WideField : PixelScaleEnum.Typical;
            options.Simple_PixelScale = wasScale;
            return;
        }
        var was = options.Simple_FocusRange;
        options.Simple_FocusRange = was == FocusRangeEnum.Typical ? FocusRangeEnum.WideRange : FocusRangeEnum.Typical;
        options.Simple_FocusRange = was;
    }

    // Several setters VALIDATE and throw outside a documented range (DonutMaxStreakEccentricity is [0.8, 1.0]),
    // so there is no single generic sentinel. Offer candidates and let the setter choose: the first one it
    // accepts is the probe value. If it rejects all of them the membership is UNMEASURED and the caller says so
    // -- it must never be reported as confirmed.
    private static IEnumerable<object> SentinelCandidates(object current, Type t) {
        if (t == typeof(bool)) {
            yield return !(bool)current;
            yield break;
        }
        if (t.IsEnum) {
            foreach (var v in Enum.GetValues(t)) {
                if (!Equals(v, current)) yield return v;
            }
            yield break;
        }
        if (t == typeof(int)) {
            yield return (int)current + 1;
            yield return (int)current - 1;
            yield break;
        }
        if (t == typeof(double)) {
            var d = (double)current;
            // Nearest first, so a narrow validated range is still reachable; then progressively further away.
            yield return d * 0.95;
            yield return d * 1.05;
            yield return d > 0.0 ? d / 2.0 : 0.5;
            yield return d + 1.0;
            yield break;
        }
        if (t == typeof(string)) {
            yield return (string)current + "_sentinel";
            yield break;
        }
    }

    private static (PropertyInfo prop, object before, object sentinel) WriteSentinel(StarDetectionOptions options, string name) {
        var prop = typeof(StarDetectionOptions).GetProperty(name);
        Assert.That(prop, Is.Not.Null, $"could not look: {name} is not a public property of StarDetectionOptions");
        var before = prop.GetValue(options);
        var rejected = new List<string>();
        foreach (var candidate in SentinelCandidates(before, prop.PropertyType)) {
            try {
                prop.SetValue(options, candidate);
            } catch (TargetInvocationException ex) {
                rejected.Add($"{candidate} -> {ex.InnerException?.GetType().Name}");
                continue;
            }
            // F66: the mutation must be OBSERVED, not assumed. A change-guarded or clamping setter can accept the
            // write and keep the old value, which would make every property look owned for the wrong reason.
            if (Equals(prop.GetValue(options), candidate)) {
                return (prop, before, candidate);
            }
            rejected.Add($"{candidate} -> silently kept {before}");
        }
        Assert.Fail($"could not look: no sentinel was accepted by {name} (tried: {string.Join("; ", rejected)}), " +
                    "so this property's membership is UNMEASURED rather than confirmed");
        return (prop, before, before);
    }

    [Test]
    public void DerivationOwnedProperties_RevertWhenTheDerivationRuns([ValueSource(nameof(DerivationOwnedProperties))] string name) {
        var (options, _, _) = Build();
        var (prop, before, _) = WriteSentinel(options, name);
        FireDerivation(options, name);
        Assert.That(prop.GetValue(options), Is.EqualTo(before),
            $"{name} is listed as preset-owned, so DerivePresetSettings must reassign it and the sentinel must " +
            "not survive. If this fails, the property left the derivation and its ResetDefaultsImpl literal is " +
            "now LIVE -- do not delete it.");
    }

    [Test]
    public void NotDerivationOwnedProperties_SurviveWhenTheDerivationRuns([ValueSource(nameof(NotDerivationOwnedProperties))] string name) {
        var (options, _, _) = Build();
        var (prop, _, sentinel) = WriteSentinel(options, name);
        FireDerivation(options, name);
        Assert.That(prop.GetValue(options), Is.EqualTo(sentinel),
            $"{name} is listed as NOT preset-owned, so the derivation must leave it alone. If this fails, the " +
            "property joined the derivation and its ResetDefaultsImpl literal is now dead.");
    }

    [Test]
    public void PresetOwnedPartition_CountsAndDisjointness_ArePinned() {
        var owned = new HashSet<string>(DerivationOwnedProperties);
        var notOwned = new HashSet<string>(NotDerivationOwnedProperties);
        Assert.Multiple(() => {
            Assert.That(owned, Has.Count.EqualTo(20), "the preset-owned set is 20, derived from source by wave 21");
            Assert.That(owned.Overlaps(notOwned), Is.False, "a property cannot be in both halves");
            // The four names the probe cannot drive are excluded deliberately and must not silently reappear
            // in either list -- that is how an uncovered literal would masquerade as a covered one.
            foreach (var excluded in ProbeExcludedByName) {
                Assert.That(owned.Contains(excluded), Is.False, $"{excluded} is probe-excluded, not owned");
                Assert.That(notOwned.Contains(excluded), Is.False, $"{excluded} is probe-excluded, not not-owned");
            }
        });
    }

    [Test]
    public void ResetDefaults_IsDeterministic_WhateverUseOptimizedSettingsWasBeforehand() {
        // F70's sharpest half, and the half the register did not have. UseOptimizedSettings is a member of
        // SimplePropertyNames, and ResetDefaultsImpl's last statement is `UseOptimizedSettings = false`. Every
        // setter in this class is change-guarded, so before the fix that statement re-entered the preset
        // derivation ONLY when the flag was already on: the same "restore defaults" button left
        // NoiseReductionRadius at 4 for a user who had optimized settings enabled and at 3 for one who did not.
        // A button whose result depends on a checkbox it clears is not a default.
        var (flagOn, _, _) = Build();
        flagOn.UseOptimizedSettings = true;
        var (flagOff, _, _) = Build();
        Assert.That(flagOff.UseOptimizedSettings, Is.False, "precondition: the two entry states differ");

        flagOn.ResetDefaults();
        flagOff.ResetDefaults();
        var (fresh, _, _) = Build();

        Assert.Multiple(() => {
            Assert.That(flagOn.NoiseReductionRadius, Is.EqualTo(flagOff.NoiseReductionRadius),
                "ResetDefaults() must not depend on the state of the flag it clears");
            Assert.That(flagOff.NoiseReductionRadius, Is.EqualTo(fresh.NoiseReductionRadius),
                "and both must equal what constructing the object produces");
            Assert.That(flagOff.NoiseReductionRadius, Is.EqualTo(4),
                "Typical preset base 3 + the hotpixel compensation — the value the product actually runs");
        });
    }

    [Test]
    public void RepeatedDerivation_DoesNotCompoundTheHotpixelCompensation() {
        // The +1 at StarDetectionOptions.cs:221-224 is applied AFTER a switch that assigns NoiseReductionRadius
        // ABSOLUTELY on every branch, so re-entering the derivation cannot stack compensations. That property is
        // precisely what makes an unconditional ConfigureSimpleSettings() safe to add to ResetDefaults, so it is
        // pinned here directly rather than assumed. Driving the derivation is what the test must do: a
        // ResetDefaults-only version of this test does NOT catch the mutant, because ResetDefaults re-asserts the
        // bare literal first and hides the compounding. (Named mutant, VERIFIED to fail this test: make the
        // Typical branch `NoiseReductionRadius = Math.Max(NoiseReductionRadius, 3)`.)
        var (options, _, _) = Build();
        var afterConstruction = options.NoiseReductionRadius;

        for (var i = 0; i < 3; i++) {
            // UseOptimizedSettings is in SimplePropertyNames, so each edge re-enters DerivePresetSettings.
            options.UseOptimizedSettings = true;
            options.UseOptimizedSettings = false;
        }
        var afterRederiving = options.NoiseReductionRadius;

        options.ResetDefaults();
        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(afterConstruction, Is.EqualTo(4));
            Assert.That(afterRederiving, Is.EqualTo(afterConstruction), "six re-derivations must not move the radius");
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(afterConstruction), "nor must repeated resets");
        });
    }

    [Test]
    public void NoiseReductionRadius_AccessorFallbackAndPresetBaseAreTheSameLiteral() {
        // T3, modelled on AutoFocusOptionsTests.TheTwoCodeDefaultSites_ResolveToTheSameBudget. This knob has two
        // code-default sites a reader would expect to agree — InitializeOptions'
        // GetValueInt32("NoiseReductionRadius", 3) fall-back and the Typical branch of DerivePresetSettings — and
        // on the ordinary path NEITHER is observable, because construction always ends in the derivation and the
        // derivation always adds the +1. Two sites that no test can see are two sites that drift.
        var profile = Substitute.For<IProfileService>();

        // The fall-back is observable through a profile already in Advanced mode: InitializeOptions reads the
        // useAdvanced FIELD before the derivation, so ConfigureSimpleSettings returns immediately.
        var advancedStore = new InMemoryPluginOptionsAccessor();
        advancedStore.SetValueBoolean("UseAdvanced", true);
        var advanced = new StarDetectionOptions(profile, advancedStore);

        // The preset base is observable with the compensation's own input turned off.
        var uncompensatedStore = new InMemoryPluginOptionsAccessor();
        uncompensatedStore.SetValueBoolean(nameof(StarDetectionOptions.HotpixelThresholdingEnabled), false);
        var uncompensated = new StarDetectionOptions(profile, uncompensatedStore);

        var (compensated, _, _) = Build();

        Assert.Multiple(() => {
            Assert.That(advanced.UseAdvanced, Is.True, "precondition: the fall-back is only visible in Advanced mode");
            Assert.That(uncompensated.HotpixelThresholdingEnabled, Is.False, "precondition: compensation off");
            Assert.That(advanced.NoiseReductionRadius, Is.EqualTo(uncompensated.NoiseReductionRadius),
                "the accessor fall-back and the Typical preset's base are one default and must move together");
            Assert.That(compensated.NoiseReductionRadius, Is.EqualTo(uncompensated.NoiseReductionRadius + 1),
                "and the shipped default is that base plus the hotpixel compensation, exactly once");
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
            // F70: 4, not 3. ResetDefaults now ends in the Simple-mode derivation unconditionally, so it lands
            // where a construction lands: the Typical preset's base 3 plus the hotpixel compensation
            // (StarDetectionOptions.cs:221-224, both inputs on by default). The 3 this used to assert was the
            // bare literal, a value the next construction overwrote — and only on the branch where the
            // derivation happened not to re-enter.
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(4));
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
        Assert.Throws<ArgumentNullException>(() => new StarDetectionOptions(profile, (IPluginOptionsAccessor)null));
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
