using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class RadiometryCalculatorTests {

    // Design sanity vector optics: D=100 mm, f=800 mm, ε=0, p=3.76 µm, L filter (Δλ=320 nm, QE(540)=0.730),
    // T=0.85, μ_sky=20.5. Exposure varies per test.
    private static RadiometryCalculator Build(
        double apertureMm = 100.0,
        double obstruction = 0.0,
        double focalMm = 800.0,
        double pixelUm = 3.76,
        double bandwidthNm = 320.0,
        double qe = 0.730,
        double throughput = 0.85,
        double exposureS = 60.0,
        double skyMag = 20.5,
        double darkPerSec = 0.011) {
        return new RadiometryCalculator(apertureMm, obstruction, focalMm, pixelUm, bandwidthNm, qe, throughput, exposureS, skyMag, darkPerSec);
    }

    [Test]
    public void StarElectrons_IncreasesWithExposure() {
        var shortExp = Build(exposureS: 10.0).StarElectrons(10.0);
        var longExp = Build(exposureS: 30.0).StarElectrons(10.0);
        Assert.That(longExp, Is.GreaterThan(shortExp));
        // Linear in t: 3× exposure ⇒ 3× electrons.
        Assert.That(longExp / shortExp, Is.EqualTo(3.0).Within(1e-9));
    }

    [Test]
    public void StarElectrons_IncreasesWithApertureArea() {
        var small = Build(apertureMm: 100.0).StarElectrons(10.0);
        var large = Build(apertureMm: 200.0).StarElectrons(10.0);
        Assert.That(large, Is.GreaterThan(small));
        // A ∝ D², so 2× aperture ⇒ 4× electrons.
        Assert.That(large / small, Is.EqualTo(4.0).Within(1e-9));
    }

    [Test]
    public void StarElectrons_IncreasesWithBandwidth() {
        var narrow = Build(bandwidthNm: 5.0).StarElectrons(10.0);
        var wide = Build(bandwidthNm: 320.0).StarElectrons(10.0);
        Assert.That(wide, Is.GreaterThan(narrow));
        Assert.That(wide / narrow, Is.EqualTo(64.0).Within(1e-9));
    }

    [Test]
    public void StarElectrons_IncreasesAsMagnitudeDecreases() {
        var calc = Build();
        var faint = calc.StarElectrons(12.0);
        var bright = calc.StarElectrons(10.0);
        Assert.That(bright, Is.GreaterThan(faint));
        // 2.5 mag brighter ⇒ 10× flux; here 2.0 mag ⇒ 10^0.8 ≈ 6.31×.
        Assert.That(bright / faint, Is.EqualTo(System.Math.Pow(10.0, 0.8)).Within(1e-6));
    }

    [Test]
    public void SkyElectronsPerPixel_ScalesWithPixelSolidAngle() {
        // Ω_px = (206.265·p/f)² ∝ p². Doubling the pixel pitch quadruples the per-pixel sky.
        var smallPixel = Build(pixelUm: 3.76);
        var bigPixel = Build(pixelUm: 7.52);
        var ratio = bigPixel.SkyElectronsPerPixel() / smallPixel.SkyElectronsPerPixel();
        Assert.Multiple(() => {
            Assert.That(bigPixel.PixelSolidAngleArcsec2 / smallPixel.PixelSolidAngleArcsec2, Is.EqualTo(4.0).Within(1e-9));
            Assert.That(ratio, Is.EqualTo(4.0).Within(1e-9));
        });
    }

    [Test]
    public void SkyElectronsPerPixel_ScalesWithBandwidth() {
        // Narrowband sky ∝ Δλ: 5 nm passes 1/64 of L's 320 nm at equal density.
        var narrow = Build(bandwidthNm: 5.0).SkyElectronsPerPixel();
        var wide = Build(bandwidthNm: 320.0).SkyElectronsPerPixel();
        Assert.That(wide / narrow, Is.EqualTo(64.0).Within(1e-9));
    }

    [Test]
    public void DarkElectronsPerPixel_ScalesWithExposure() {
        var shortExp = Build(exposureS: 10.0, darkPerSec: 0.02).DarkElectronsPerPixel();
        var longExp = Build(exposureS: 40.0, darkPerSec: 0.02).DarkElectronsPerPixel();
        Assert.Multiple(() => {
            Assert.That(shortExp, Is.EqualTo(0.2).Within(1e-12));
            Assert.That(longExp, Is.EqualTo(0.8).Within(1e-12));
            Assert.That(longExp / shortExp, Is.EqualTo(4.0).Within(1e-9));
        });
    }

    [Test]
    public void DarkElectronsPerPixel_DoublesPer6Point5Celsius_ViaFromRequest() {
        // IMX455 reference dark 0.011 e⁻/px/s at −10 °C; +6.5 °C doubles it (6.5 °C doubling law).
        var cold = RadiometryCalculator.FromRequest(RequestAtTemp(-10.0), SensorRegistry.Get(SonySensorModel.IMX455), FilterRegistry.Get(SimulatorFilter.L));
        var warm = RadiometryCalculator.FromRequest(RequestAtTemp(-3.5), SensorRegistry.Get(SonySensorModel.IMX455), FilterRegistry.Get(SimulatorFilter.L));
        var ratio = warm.DarkElectronsPerPixel() / cold.DarkElectronsPerPixel();
        Assert.That(ratio, Is.EqualTo(2.0).Within(1e-9));
    }

    [Test]
    public void StarElectrons_LumaVsHa5RatioIsAbout93x() {
        // Same star, aperture, throughput, exposure — only the filter (Δλ·QE(λc)) differs. Design: ≈93.4×.
        var request = RequestAtTemp(-10.0);
        var sensor = SensorRegistry.Get(SonySensorModel.IMX455);
        var luma = RadiometryCalculator.FromRequest(request, sensor, FilterRegistry.Get(SimulatorFilter.L));
        var ha5 = RadiometryCalculator.FromRequest(request, sensor, FilterRegistry.Get(SimulatorFilter.Ha5));
        var ratio = luma.StarElectrons(10.0) / ha5.StarElectrons(10.0);
        Assert.That(ratio, Is.EqualTo(93.4).Within(0.05 * 93.4), $"L/Ha5 electron ratio was {ratio:F1}");
    }

    [Test]
    public void StarElectrons_BrightStarSaturatesButFaintDoesNot() {
        // Design: at D=100 mm f/8, gain 100, a mag-10 star saturates in ~6 s (IMX455 full well ≈ 50 k e⁻).
        // Total star electrons exceeding the well is the necessary condition; a mag-15 star stays well below.
        var calc = Build(exposureS: 6.0);
        var fullWell = SensorRegistry.Get(SonySensorModel.IMX455).FullWellElectrons; // 50000
        Assert.Multiple(() => {
            Assert.That(calc.StarElectrons(10.0), Is.GreaterThan(fullWell), "mag-10 in 6 s should exceed full well");
            Assert.That(calc.StarElectrons(15.0), Is.LessThan(fullWell), "mag-15 in 6 s should stay unsaturated");
            // Cross-check the absolute magnitude against the hand computation (~93.6 k e⁻).
            Assert.That(calc.StarElectrons(10.0), Is.EqualTo(93589.0).Within(0.01 * 93589.0));
        });
    }

    [Test]
    public void ApertureArea_MatchesGeometryWithObstruction() {
        // A = (π/4)·D²·(1−ε²), D in cm. D=100 mm=10 cm, ε=0.3 ⇒ (π/4)·100·(1−0.09) = 71.47 cm².
        var calc = Build(obstruction: 0.3);
        Assert.That(calc.ApertureAreaCm2, Is.EqualTo((System.Math.PI / 4.0) * 100.0 * (1.0 - 0.09)).Within(1e-9));
    }

    private static RenderRequest RequestAtTemp(double celsius) {
        return new RenderRequest {
            ApertureMillimeters = 100.0,
            FocalLengthMillimeters = 800.0,
            CentralObstructionEnabled = false,
            CentralObstructionFraction = 0.3,
            OpticalThroughput = 0.85,
            ExposureSeconds = 60.0,
            SkyBrightnessMagPerArcsec2 = 20.5,
            SensorTemperatureCelsius = celsius,
        };
    }
}
