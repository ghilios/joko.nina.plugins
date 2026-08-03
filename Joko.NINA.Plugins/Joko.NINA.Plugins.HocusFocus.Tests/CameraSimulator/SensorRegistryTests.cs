using System;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class SensorRegistryTests {

    [Test]
    public void EverySensorModelResolvesAndAllContainsThem() {
        var models = Enum.GetValues(typeof(SonySensorModel)).Cast<SonySensorModel>().ToArray();
        Assert.Multiple(() => {
            foreach (var model in models) {
                var def = SensorRegistry.Get(model);
                Assert.That(def, Is.Not.Null, model.ToString());
                Assert.That(def.Model, Is.EqualTo(model));
                Assert.That(def.SensorName, Is.EqualTo(model.ToString()));
                Assert.That(SensorRegistry.All, Contains.Item(def));
            }
            Assert.That(SensorRegistry.All, Has.Count.EqualTo(models.Length));
        });
    }

    [TestCase(SonySensorModel.IMX455, 9576, 6388, 3.76, 16, 50000.0)]
    [TestCase(SonySensorModel.IMX571, 6248, 4176, 3.76, 16, 50000.0)]
    [TestCase(SonySensorModel.IMX533, 3008, 3008, 3.76, 14, 50000.0)]
    [TestCase(SonySensorModel.IMX294, 4144, 2822, 4.63, 14, 66000.0)]
    [TestCase(SonySensorModel.IMX585, 3840, 2160, 2.90, 12, 40000.0)]
    public void SensorGeometryMatchesDatasheet(SonySensorModel model, int w, int h, double pixel, int bits, double fullWell) {
        var def = SensorRegistry.Get(model);
        Assert.Multiple(() => {
            Assert.That(def.Width, Is.EqualTo(w));
            Assert.That(def.Height, Is.EqualTo(h));
            Assert.That(def.PixelSizeMicrons, Is.EqualTo(pixel).Within(1e-9));
            Assert.That(def.BitDepth, Is.EqualTo(bits));
            Assert.That(def.FullWellElectrons, Is.EqualTo(fullWell).Within(1e-6));
            Assert.That(def.MaxAdu, Is.EqualTo((1 << bits) - 1));
        });
    }

    [TestCase(SonySensorModel.IMX455, 0.763)]
    [TestCase(SonySensorModel.IMX571, 0.763)]
    [TestCase(SonySensorModel.IMX533, 3.052)]
    [TestCase(SonySensorModel.IMX294, 4.028)]
    [TestCase(SonySensorModel.IMX585, 9.766)]
    public void ElectronsPerAduAtGain0_MatchesGain0Column_Within1Percent(SonySensorModel model, double gain0Column) {
        var def = SensorRegistry.Get(model);
        var ge0 = def.ElectronsPerAduAtGain(0);
        Assert.That(ge0, Is.EqualTo(gain0Column).Within(0.01 * gain0Column));
    }

    [Test]
    public void GainLaw_HalvesEvery60Units_And20DbGivesTenthOfGain0() {
        var def = SensorRegistry.Get(SonySensorModel.IMX455);
        var ge0 = def.ElectronsPerAduAtGain(0);
        Assert.Multiple(() => {
            // 200 0.1dB-units = 20 dB = factor of 10 reduction.
            Assert.That(def.ElectronsPerAduAtGain(200), Is.EqualTo(ge0 / 10.0).Within(1e-9));
            // 60 0.1dB-units = 6 dB ≈ factor of ~2 (10^0.3).
            Assert.That(def.ElectronsPerAduAtGain(60), Is.EqualTo(ge0 * Math.Pow(10.0, -0.3)).Within(1e-9));
            Assert.That(def.ElectronsPerAduAtGain(100), Is.LessThan(ge0), "gain increases → fewer e-/ADU");
        });
    }

    [Test]
    public void DigitalSaturation_NeverExceedsFullWell_AndTightensAtHighGain() {
        foreach (var def in SensorRegistry.All) {
            var satGain0 = def.DigitalSaturationElectronsAtGain(0);
            var satHighGain = def.DigitalSaturationElectronsAtGain(200);
            Assert.Multiple(() => {
                Assert.That(satGain0, Is.LessThanOrEqualTo(def.FullWellElectrons), $"{def.SensorName} gain0");
                // At gain 0, digital clip = fullWell·(2^bits−1)/2^bits, just under the well.
                Assert.That(satGain0, Is.EqualTo(def.FullWellElectrons * def.MaxAdu / def.DigitalLevels).Within(1e-6));
                Assert.That(satHighGain, Is.LessThan(satGain0), $"{def.SensorName} high gain tighter");
            });
        }
    }

    [Test]
    public void ReadNoise_PositiveDecreasingWithHcgStepDown() {
        foreach (var def in SensorRegistry.All) {
            var thr = def.HighConversionGainThreshold;
            var rn0 = def.ReadNoiseElectronsAtGain(0);
            var rnJustBelow = def.ReadNoiseElectronsAtGain(thr - 1);
            var rnAtThreshold = def.ReadNoiseElectronsAtGain(thr);
            var rnMax = def.ReadNoiseElectronsAtGain(def.MaxGain);

            Assert.Multiple(() => {
                Assert.That(rn0, Is.EqualTo(def.ReadNoiseGain0Electrons).Within(1e-9), $"{def.SensorName} RN@0");
                Assert.That(rnAtThreshold, Is.EqualTo(def.ReadNoiseHcgElectrons).Within(1e-9), $"{def.SensorName} RN@threshold");
                Assert.That(rnMax, Is.EqualTo(def.ReadNoiseMinElectrons).Within(1e-9), $"{def.SensorName} RN@max");

                // LCG segment decreases toward the threshold, then a discontinuous step DOWN to HCG.
                Assert.That(rnJustBelow, Is.LessThan(rn0), $"{def.SensorName} LCG decreases");
                Assert.That(rnAtThreshold, Is.LessThan(rnJustBelow), $"{def.SensorName} HCG step down");

                // Positive and monotone non-increasing when sampled densely.
                var prev = double.PositiveInfinity;
                for (int g = 0; g <= def.MaxGain; g += 5) {
                    var rn = def.ReadNoiseElectronsAtGain(g);
                    Assert.That(rn, Is.GreaterThan(0.0), $"{def.SensorName} RN>0 @{g}");
                    Assert.That(rn, Is.LessThanOrEqualTo(prev + 1e-12), $"{def.SensorName} RN non-increasing @{g}");
                    prev = rn;
                }
            });
        }
    }

    [Test]
    public void ReadNoise_ClampsGainToUsableRange() {
        var def = SensorRegistry.Get(SonySensorModel.IMX455);
        Assert.Multiple(() => {
            Assert.That(def.ReadNoiseElectronsAtGain(-50), Is.EqualTo(def.ReadNoiseGain0Electrons).Within(1e-9));
            Assert.That(def.ReadNoiseElectronsAtGain(def.MaxGain + 100), Is.EqualTo(def.ReadNoiseMinElectrons).Within(1e-9));
        });
    }

    [Test]
    public void DarkCurrent_EqualsRefAtTRef_AndDoublesEvery6Point5Celsius() {
        foreach (var def in SensorRegistry.All) {
            var tRef = def.DarkReferenceTemperatureCelsius;
            var iRef = def.DarkCurrentRefElectronsPerPixelPerSecond;
            Assert.Multiple(() => {
                Assert.That(def.DarkElectronsPerPixelPerSecondAtTemperature(tRef), Is.EqualTo(iRef).Within(1e-12),
                    $"{def.SensorName} @T_ref");
                Assert.That(def.DarkElectronsPerPixelPerSecondAtTemperature(tRef + 6.5), Is.EqualTo(2.0 * iRef).Within(1e-12),
                    $"{def.SensorName} +6.5C doubles");
                Assert.That(def.DarkElectronsPerPixelPerSecondAtTemperature(tRef - 6.5), Is.EqualTo(0.5 * iRef).Within(1e-12),
                    $"{def.SensorName} -6.5C halves");
                Assert.That(def.DarkElectronsPerPixelPerSecondAtTemperature(tRef + 13.0), Is.EqualTo(4.0 * iRef).Within(1e-12),
                    $"{def.SensorName} +13C quadruples");
            });
        }
    }

    [Test]
    public void AllSensorsShareTheVisibleQeCurve() {
        Assert.Multiple(() => {
            foreach (var def in SensorRegistry.All) {
                Assert.That(def.QeCurve, Is.SameAs(QeCurve.SonyBsiVisible), def.SensorName);
            }
        });
    }

    [Test]
    public void All_IsReadOnlyAndNotCastableToMutableArray() {
        Assert.That(SensorRegistry.All, Is.Not.InstanceOf<SensorDefinition[]>());
    }

    [Test]
    public void Get_UnknownModel_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => SensorRegistry.Get((SonySensorModel)999));
    }
}
