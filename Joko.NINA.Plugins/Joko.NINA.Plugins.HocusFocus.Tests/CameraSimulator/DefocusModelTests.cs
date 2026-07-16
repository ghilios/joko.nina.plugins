using System;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class DefocusModelTests {

    private const int X0 = 10000;
    private const double K_MicronsPerStep = 0.49;

    // Design sanity vector: D=100 mm, f=800 mm (N=8), ε=0.3, λ=550 nm, p=3.76 µm, seeing=2.5″.
    private static DefocusModel BuildSanityVector() {
        return new DefocusModel(
            apertureMillimeters: 100.0,
            focalLengthMillimeters: 800.0,
            centralObstructionFraction: 0.3,
            pixelSizeMicrons: 3.76,
            seeingArcsec: 2.5,
            wavelengthNm: 550.0,
            focuserStepSizeMicrons: K_MicronsPerStep,
            optimalFocuserPosition: X0);
    }

    [Test]
    public void DesignSanityVector_PlateScaleAndHfrMin() {
        var model = BuildSanityVector();
        Assert.Multiple(() => {
            Assert.That(model.FocalRatio, Is.EqualTo(8.0).Within(1e-9));
            Assert.That(model.ArcsecPerPixel, Is.EqualTo(0.969).Within(0.005), "plate scale ≈ 0.969″/px");
            Assert.That(model.HfrMinPixels, Is.EqualTo(1.50).Within(0.03), "HFR_min ≈ 1.50 px");
        });
    }

    [Test]
    public void HfrAtOptimalFocuserPosition_EqualsHfrMin() {
        var model = BuildSanityVector();
        Assert.That(model.HfrAtFocuserPosition(X0), Is.EqualTo(model.HfrMinPixels).Within(1e-12));
    }

    [TestCase(200)]
    [TestCase(500)]
    [TestCase(1000)]
    public void HfrGrowsAsHyperbola(int deltaSteps) {
        var model = BuildSanityVector();
        var kappa = model.KappaPixelsPerStep;
        var hfrMin = model.HfrMinPixels;
        var expected = Math.Sqrt(hfrMin * hfrMin + (kappa * deltaSteps) * (kappa * deltaSteps));
        Assert.Multiple(() => {
            Assert.That(model.HfrAtFocuserPosition(X0 + deltaSteps), Is.EqualTo(expected).Within(1e-9));
            // Symmetric about x0.
            Assert.That(model.HfrAtFocuserPosition(X0 - deltaSteps), Is.EqualTo(expected).Within(1e-9));
            // The micron entry point must agree with the step entry point at Δ = k·Δsteps.
            Assert.That(model.HfrAtDefocusMicrons(K_MicronsPerStep * deltaSteps), Is.EqualTo(expected).Within(1e-9));
        });
    }

    [Test]
    public void Kappa_MatchesIndependentComputation() {
        // κ = k·(1+ε+ε²)/(3·N·p·(1+ε)) with k=0.49, ε=0.3, N=8, p=3.76 ⇒ 0.00580589 px/step (Python-verified).
        var model = BuildSanityVector();
        Assert.That(model.KappaPixelsPerStep, Is.EqualTo(0.00580589).Within(1e-8));
    }

    [Test]
    public void RangeReadout_GivesExactly4xHfrMin() {
        var model = BuildSanityVector();
        // At Δsteps = RangeReadoutSteps (i.e. Δ = k·RangeReadoutSteps µm), HFR = √(1+15)·HFR_min = 4·HFR_min.
        var defocusMicrons = K_MicronsPerStep * model.RangeReadoutSteps;
        Assert.Multiple(() => {
            Assert.That(model.HfrAtDefocusMicrons(defocusMicrons), Is.EqualTo(4.0 * model.HfrMinPixels).Within(1e-9));
            Assert.That(model.RangeReadoutSteps, Is.EqualTo(1002.0).Within(2.0), "≈ 1002 steps for k=0.49");
        });
    }

    [Test]
    public void DonutAnnulus_InnerOverOuterEqualsObstructionFraction() {
        var model = BuildSanityVector();
        foreach (var defocus in new[] { 100.0, 491.0, -491.0 }) {
            var ratioUm = model.InnerAnnulusRadiusMicrons(defocus) / model.OuterAnnulusRadiusMicrons(defocus);
            var ratioPx = model.InnerAnnulusRadiusPixels(defocus) / model.OuterAnnulusRadiusPixels(defocus);
            Assert.Multiple(() => {
                Assert.That(ratioUm, Is.EqualTo(0.3).Within(1e-12), $"r_in/r_out (µm) at Δ={defocus}");
                Assert.That(ratioPx, Is.EqualTo(0.3).Within(1e-12), $"r_in/r_out (px) at Δ={defocus}");
            });
        }
    }

    [Test]
    public void DonutOuterDiameter_MatchesDesignAt4xHfr() {
        // Design: at 4× HFR_min the donut OD ≈ 16.3 px with Δ ≈ 491 µm, W20 ≈ 1.74λ.
        var model = BuildSanityVector();
        const double delta = 491.0;
        Assert.Multiple(() => {
            Assert.That(2.0 * model.OuterAnnulusRadiusPixels(delta), Is.EqualTo(16.3).Within(0.1), "donut OD ≈ 16.3 px");
            Assert.That(model.W20Waves(delta), Is.EqualTo(1.74).Within(0.02), "W20 ≈ 1.74 waves");
            Assert.That(model.WavelengthNm, Is.EqualTo(550.0).Within(1e-9));
            Assert.That(model.W20Microns(delta), Is.EqualTo(delta / (8.0 * 8.0 * 8.0)).Within(1e-9));
        });
    }

    [Test]
    public void FromRequest_UsesFilterCentralWavelengthAndSensorPixel() {
        // Same optics as the sanity vector but the L filter (λc=540 nm) instead of 550 nm ⇒ slightly smaller
        // diffraction σ. IMX455 pixel pitch (3.76 µm) matches, so ArcsecPerPixel should equal the sanity value.
        var request = new RenderRequest {
            ApertureMillimeters = 100.0,
            FocalLengthMillimeters = 800.0,
            CentralObstructionEnabled = true,
            CentralObstructionFraction = 0.3,
            SeeingArcsec = 2.5,
            FocuserStepSizeMicrons = K_MicronsPerStep,
            OptimalFocuserPosition = X0,
        };
        var model = DefocusModel.FromRequest(request, SensorRegistry.Get(SonySensorModel.IMX455), FilterRegistry.Get(SimulatorFilter.L));
        var sanity = BuildSanityVector();
        Assert.Multiple(() => {
            Assert.That(model.ArcsecPerPixel, Is.EqualTo(sanity.ArcsecPerPixel).Within(1e-12));
            Assert.That(model.CentralObstructionFraction, Is.EqualTo(0.3).Within(1e-12));
            // λ=540 < 550 ⇒ smaller diffraction σ ⇒ slightly smaller HFR_min.
            Assert.That(model.HfrMinPixels, Is.LessThan(sanity.HfrMinPixels));
            Assert.That(model.HfrMinPixels, Is.EqualTo(sanity.HfrMinPixels).Within(0.02));
        });
    }

    [Test]
    public void FromRequest_ObstructionDisabled_UsesZeroEpsilon() {
        var request = new RenderRequest {
            ApertureMillimeters = 100.0,
            FocalLengthMillimeters = 800.0,
            CentralObstructionEnabled = false,
            CentralObstructionFraction = 0.3,
            SeeingArcsec = 2.5,
            FocuserStepSizeMicrons = K_MicronsPerStep,
            OptimalFocuserPosition = X0,
        };
        var model = DefocusModel.FromRequest(request, SensorRegistry.Get(SonySensorModel.IMX455), FilterRegistry.Get(SimulatorFilter.L));
        Assert.Multiple(() => {
            Assert.That(model.CentralObstructionFraction, Is.EqualTo(0.0).Within(1e-12));
            // ε=0 ⇒ no central hole in the donut.
            Assert.That(model.InnerAnnulusRadiusMicrons(300.0), Is.EqualTo(0.0).Within(1e-12));
        });
    }
}
