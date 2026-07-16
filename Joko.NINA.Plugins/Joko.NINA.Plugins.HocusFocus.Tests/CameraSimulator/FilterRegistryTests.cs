using System;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class FilterRegistryTests {

    [Test]
    public void EveryFilterResolvesAndAllContainsThem() {
        var filters = Enum.GetValues(typeof(SimulatorFilter)).Cast<SimulatorFilter>().ToArray();
        Assert.Multiple(() => {
            foreach (var filter in filters) {
                var def = FilterRegistry.Get(filter);
                Assert.That(def, Is.Not.Null, filter.ToString());
                Assert.That(def.Filter, Is.EqualTo(filter));
                Assert.That(FilterRegistry.All, Contains.Item(def));
            }
            Assert.That(FilterRegistry.All, Has.Count.EqualTo(filters.Length));
            Assert.That(FilterRegistry.All, Has.Count.EqualTo(10));
        });
    }

    [TestCase(SimulatorFilter.L, 540.0, 320.0)]
    [TestCase(SimulatorFilter.R, 635.0, 100.0)]
    [TestCase(SimulatorFilter.G, 530.0, 100.0)]
    [TestCase(SimulatorFilter.B, 465.0, 100.0)]
    [TestCase(SimulatorFilter.Ha5, 656.3, 5.0)]
    [TestCase(SimulatorFilter.Ha3, 656.3, 3.0)]
    [TestCase(SimulatorFilter.OIII5, 500.7, 5.0)]
    [TestCase(SimulatorFilter.OIII3, 500.7, 3.0)]
    [TestCase(SimulatorFilter.SII5, 672.4, 5.0)]
    [TestCase(SimulatorFilter.SII3, 672.4, 3.0)]
    public void FilterBandpassMatchesDesignTable(SimulatorFilter filter, double lambdaC, double deltaLambda) {
        var def = FilterRegistry.Get(filter);
        Assert.Multiple(() => {
            Assert.That(def.CentralWavelengthNm, Is.EqualTo(lambdaC).Within(1e-9));
            Assert.That(def.BandwidthNm, Is.EqualTo(deltaLambda).Within(1e-9));
            Assert.That(def.Transmission, Is.EqualTo(0.9).Within(1e-9));
        });
    }

    [Test]
    public void LumaVsHa5_ExposureParityRatioIsAbout93x() {
        // t/t_L to match L's electrons scales as (Δλ_L·QE(λc_L)) / (Δλ·QE(λc)) for equal transmission.
        // Design's parity table lists Hα 5nm at 93.4×. This validates the filter table AND QE interpolation together.
        var luma = FilterRegistry.Get(SimulatorFilter.L);
        var ha5 = FilterRegistry.Get(SimulatorFilter.Ha5);
        var qe = QeCurve.SonyBsiVisible;

        var lumaSignal = luma.BandwidthNm * qe.EvaluateAt(luma.CentralWavelengthNm) * luma.Transmission;
        var ha5Signal = ha5.BandwidthNm * qe.EvaluateAt(ha5.CentralWavelengthNm) * ha5.Transmission;
        var ratio = lumaSignal / ha5Signal;

        Assert.That(ratio, Is.EqualTo(93.4).Within(0.05 * 93.4), $"L/Ha5 parity ratio was {ratio:F1}");
    }

    [Test]
    public void All_IsReadOnlyAndNotCastableToMutableArray() {
        Assert.That(FilterRegistry.All, Is.Not.InstanceOf<FilterDefinition[]>());
    }

    [Test]
    public void Get_UnknownFilter_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => FilterRegistry.Get((SimulatorFilter)999));
    }
}
