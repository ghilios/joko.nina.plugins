using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

[TestFixture]
public class HocusFocusReportTests {

    [Test]
    public void Defaults_AreInitialized() {
        var r = new HocusFocusReport();
        Assert.Multiple(() => {
            Assert.That(r.FinalHFR, Is.EqualTo(0.0));
            Assert.That(r.Region, Is.SameAs(StarDetectionRegion.Full));
            Assert.That(r.HocusFocusStarDetectionOptions, Is.Null);
            Assert.That(r.HocusFocusAutoFocusOptions, Is.Null);
            Assert.That(r.FocuserOptions, Is.Null);
        });
    }

    [Test]
    public void Setters_RoundTrip() {
        var r = new HocusFocusReport {
            FinalHFR = 1.25,
            Region = new StarDetectionRegion(new RatioRect(0.0, 0.0, 0.5, 0.5))
        };
        Assert.Multiple(() => {
            Assert.That(r.FinalHFR, Is.EqualTo(1.25));
            Assert.That(r.Region.OuterBoundary.Width, Is.EqualTo(0.5));
        });
    }
}

[TestFixture]
public class StarDetectionRegionTests {

    [Test]
    public void Full_IsTheFullSentinel() {
        Assert.That(StarDetectionRegion.Full.IsFull(), Is.True);
        Assert.That(StarDetectionRegion.Full.OuterBoundary, Is.SameAs(RatioRect.Full));
        Assert.That(StarDetectionRegion.Full.InnerCropBoundary, Is.Null);
    }

    [Test]
    public void Constructor_RejectsNullOuterBoundary() {
        Assert.Throws<System.ArgumentException>(() => new StarDetectionRegion(null));
    }

    [Test]
    public void Constructor_RejectsInnerCropOutsideOuterBoundary() {
        var outer = new RatioRect(0.25, 0.25, 0.5, 0.5);
        var inner = new RatioRect(0.0, 0.0, 0.9, 0.9);
        Assert.Throws<System.ArgumentException>(() => new StarDetectionRegion(outer, inner));
    }

    [Test]
    public void Constructor_AcceptsInnerCropFullyContained() {
        var outer = RatioRect.Full;
        var inner = new RatioRect(0.4, 0.4, 0.2, 0.2);
        var region = new StarDetectionRegion(outer, inner, index: 7);
        Assert.Multiple(() => {
            Assert.That(region.OuterBoundary, Is.SameAs(outer));
            Assert.That(region.InnerCropBoundary, Is.SameAs(inner));
            Assert.That(region.Index, Is.EqualTo(7));
            Assert.That(region.IsFull(), Is.False);
        });
    }

    [Test]
    public void ToString_IncludesBoundaries() {
        var s = StarDetectionRegion.Full.ToString();
        Assert.Multiple(() => {
            Assert.That(s, Does.Contain("OuterBoundary"));
            Assert.That(s, Does.Contain("InnerCropBoundary"));
        });
    }
}

[TestFixture]
public class StarDetectorParamsTests {

    [Test]
    public void Defaults_AreReasonable() {
        var p = new StarDetectorParams();
        Assert.Multiple(() => {
            Assert.That(p.HotpixelFiltering, Is.True);
            Assert.That(p.HotpixelThresholdingEnabled, Is.True);
            Assert.That(p.HotpixelThreshold, Is.EqualTo(0.001));
            Assert.That(p.NoiseReductionRadius, Is.EqualTo(3));
            Assert.That(p.NoiseClippingMultiplier, Is.EqualTo(4.0));
            // F4 recalibration: σ is now measured on the image actually sampled; the σ-multiple knob 10.0→2.0
            // (BrightnessSensitivity) remains the F4 recalibration. StarClippingMultiplier is 2.0 as the uniform
            // empirical τ level (F3, gate-only) — see plans/sigma-consistency-f3-results.md.
            Assert.That(p.StarClippingMultiplier, Is.EqualTo(2.0));
            Assert.That(p.HfrTauPolicy, Is.EqualTo(TauClipPolicy.GateOnly), "empirical F3 default — see plans/sigma-consistency-f3-results.md");
            Assert.That(p.HotpixelFilterRadius, Is.EqualTo(1));
            Assert.That(p.StructureLayers, Is.EqualTo(4));
            Assert.That(p.StructureDilationSize, Is.EqualTo(3));
            Assert.That(p.StructureDilationCount, Is.EqualTo(0));
            Assert.That(p.Sensitivity, Is.EqualTo(2.0));
            Assert.That(p.StarMeasurementNoiseReductionEnabled, Is.False,
                "mirrors the StarDetectionOptions default; the recalibrated 0.4/2.0 σ-knob defaults assume the sharp-measurement path");
            Assert.That(p.PeakResponse, Is.EqualTo(0.75));
            Assert.That(p.MaxDistortion, Is.EqualTo(0.5));
            Assert.That(p.StarCenterTolerance, Is.EqualTo(0.3));
            Assert.That(p.BackgroundBoxExpansion, Is.EqualTo(3));
            Assert.That(p.MinimumStarBoundingBoxSize, Is.EqualTo(5));
            Assert.That(p.MinHFR, Is.EqualTo(1.5));
            Assert.That(p.Region, Is.SameAs(StarDetectionRegion.Full));
            Assert.That(p.AnalysisSamplingSize, Is.EqualTo(1.0f));
            Assert.That(p.StoreStructureMap, Is.False);
            Assert.That(p.SaveIntermediateFilesPath, Is.EqualTo(string.Empty));
            Assert.That(p.SaturationThreshold, Is.EqualTo(0.99).Within(1e-6));
            Assert.That(p.ModelPSF, Is.True);
            Assert.That(p.PSFFitType, Is.EqualTo(StarDetectorPSFFitType.Moffat_40));
            Assert.That(p.UsePSFAbsoluteDeviation, Is.False);
            Assert.That(p.PSFGoodnessOfFitThreshold, Is.EqualTo(0.9));
            Assert.That(p.PSFResolution, Is.EqualTo(10));
            Assert.That(p.PSFParallelPartitionSize, Is.EqualTo(100));
            Assert.That(p.PixelScale, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void ToString_IncludesKeyFields() {
        var s = new StarDetectorParams().ToString();
        Assert.Multiple(() => {
            Assert.That(s, Does.Contain("HotpixelFiltering"));
            Assert.That(s, Does.Contain("Sensitivity"));
            Assert.That(s, Does.Contain("PSFResolution"));
        });
    }
}

[TestFixture]
public class StarDetectorMetricsTests {

    [Test]
    public void Counts_ReflectListLengths() {
        var m = new StarDetectorMetrics();
        m.TooDistortedBounds.Add(new OpenCvSharp.Rect(0, 0, 1, 1));
        m.DegenerateBounds.Add(new OpenCvSharp.Rect(0, 0, 1, 1));
        m.DegenerateBounds.Add(new OpenCvSharp.Rect(0, 0, 1, 1));
        Assert.Multiple(() => {
            Assert.That(m.TooDistorted, Is.EqualTo(1));
            Assert.That(m.Degenerate, Is.EqualTo(2));
            Assert.That(m.Saturated, Is.EqualTo(0));
        });
    }

    [Test]
    public void DirectSetters_OnComputedCounts_Throw() {
        var m = new StarDetectorMetrics();
        Assert.Throws<System.NotSupportedException>(() => m.TooDistorted = 5);
        Assert.Throws<System.NotSupportedException>(() => m.Degenerate = 5);
        Assert.Throws<System.NotSupportedException>(() => m.Saturated = 5);
        Assert.Throws<System.NotSupportedException>(() => m.LowSensitivity = 5);
        Assert.Throws<System.NotSupportedException>(() => m.NotCentered = 5);
        Assert.Throws<System.NotSupportedException>(() => m.TooFlat = 5);
    }

    [Test]
    public void AddROIOffset_ShiftsAllRectBounds() {
        var m = new StarDetectorMetrics();
        m.TooDistortedBounds.Add(new OpenCvSharp.Rect(10, 20, 5, 5));
        m.SaturatedBounds.Add(new OpenCvSharp.Rect(0, 0, 1, 1));
        m.AddROIOffset(100, 200);
        Assert.Multiple(() => {
            Assert.That(m.TooDistortedBounds[0].X, Is.EqualTo(110));
            Assert.That(m.TooDistortedBounds[0].Y, Is.EqualTo(220));
            Assert.That(m.SaturatedBounds[0].X, Is.EqualTo(100));
            Assert.That(m.SaturatedBounds[0].Y, Is.EqualTo(200));
        });
    }
}
