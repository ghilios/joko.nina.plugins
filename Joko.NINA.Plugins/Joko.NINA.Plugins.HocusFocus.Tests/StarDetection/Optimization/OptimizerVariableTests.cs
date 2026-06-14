#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Linq;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

[TestFixture]
public class OptimizerVariableTests {

    // Mirrors the curated-set contract: Write clamps to bounds before storing.
    private static OptimizerVariable Continuous(double lo, double hi, double step) {
        OptimizerVariable v = null;
        v = new OptimizerVariable {
            Name = "Sensitivity",
            Type = OptimizerVariableType.Continuous,
            Lower = lo,
            Upper = hi,
            InitialStep = step,
            Read = p => p.Sensitivity,
            Write = (p, value) => p.Sensitivity = v.Quantize(value)
        };
        return v;
    }

    [Test]
    public void Clamp_ClampsToBounds() {
        var v = Continuous(0, 20, 1.0);
        Assert.Multiple(() => {
            Assert.That(v.Clamp(-5.0), Is.EqualTo(0.0));
            Assert.That(v.Clamp(10.0), Is.EqualTo(10.0));
            Assert.That(v.Clamp(99.0), Is.EqualTo(20.0));
        });
    }

    [Test]
    public void Quantize_Continuous_IsIdentityThenClamp() {
        var v = Continuous(0, 20, 1.0);
        Assert.Multiple(() => {
            Assert.That(v.Quantize(3.14159), Is.EqualTo(3.14159).Within(1e-12));
            Assert.That(v.Quantize(25.0), Is.EqualTo(20.0));
        });
    }

    [Test]
    public void Quantize_Integer_RoundsAwayFromZeroThenClamps() {
        var v = new OptimizerVariable {
            Name = "StructureLayers",
            Type = OptimizerVariableType.Integer,
            Lower = 1,
            Upper = 8,
            InitialStep = 1,
            Read = p => p.StructureLayers,
            Write = (p, x) => p.StructureLayers = (int)x
        };
        Assert.Multiple(() => {
            Assert.That(v.Quantize(3.5), Is.EqualTo(4.0));     // away from zero
            Assert.That(v.Quantize(2.5), Is.EqualTo(3.0));     // away from zero (not banker's)
            Assert.That(v.Quantize(0.4), Is.EqualTo(1.0));     // clamp to lower
            Assert.That(v.Quantize(100.0), Is.EqualTo(8.0));   // clamp to upper
        });
    }

    [Test]
    public void Quantize_Boolean_ThresholdsAtHalf() {
        var v = new OptimizerVariable {
            Name = "HotpixelThresholdingEnabled",
            Type = OptimizerVariableType.Boolean,
            Lower = 0,
            Upper = 1,
            InitialStep = 1,
            Read = p => p.HotpixelThresholdingEnabled ? 1.0 : 0.0,
            Write = (p, x) => p.HotpixelThresholdingEnabled = x >= 0.5
        };
        Assert.Multiple(() => {
            Assert.That(v.Quantize(0.49), Is.EqualTo(0.0));
            Assert.That(v.Quantize(0.5), Is.EqualTo(1.0));
            Assert.That(v.Quantize(0.7), Is.EqualTo(1.0));
        });
    }

    [Test]
    public void Write_Continuous_ClampsAndWrites() {
        var v = Continuous(0, 20, 1.0);
        var p = new StarDetectorParams();
        v.Write(p, 99.0);
        Assert.That(p.Sensitivity, Is.EqualTo(20.0));
        v.Write(p, -5.0);
        Assert.That(p.Sensitivity, Is.EqualTo(0.0));
        v.Write(p, 7.5);
        Assert.That(p.Sensitivity, Is.EqualTo(7.5));
    }

    [Test]
    public void Write_Integer_RoundsAndWritesWholeNumber() {
        var p = new StarDetectorParams();
        var v = OptimizerVariable.CreateCuratedSet().Single(x => x.Name == nameof(StarDetectorParams.StructureLayers));
        v.Write(p, 3.5);
        Assert.That(p.StructureLayers, Is.EqualTo(4));
        v.Write(p, 100.0);
        Assert.That(p.StructureLayers, Is.EqualTo(8)); // upper bound
        v.Write(p, -10.0);
        Assert.That(p.StructureLayers, Is.EqualTo(1)); // lower bound
    }

    [Test]
    public void Write_Boolean_Thresholds() {
        var p = new StarDetectorParams();
        var v = OptimizerVariable.CreateCuratedSet().Single(x => x.Name == nameof(StarDetectorParams.HotpixelThresholdingEnabled));
        v.Write(p, 0.9);
        Assert.That(p.HotpixelThresholdingEnabled, Is.True);
        v.Write(p, 0.1);
        Assert.That(p.HotpixelThresholdingEnabled, Is.False);
    }

    [Test]
    public void Read_Boolean_ReturnsZeroOrOne() {
        var p = new StarDetectorParams { HotpixelThresholdingEnabled = true };
        var v = OptimizerVariable.CreateCuratedSet().Single(x => x.Name == nameof(StarDetectorParams.HotpixelThresholdingEnabled));
        Assert.That(v.Read(p), Is.EqualTo(1.0));
        p.HotpixelThresholdingEnabled = false;
        Assert.That(v.Read(p), Is.EqualTo(0.0));
    }

    // ---- CreateCuratedSet ----

    [Test]
    public void CreateCuratedSet_HasTwelveDistinctVariables() {
        var set = OptimizerVariable.CreateCuratedSet();
        Assert.That(set.Count, Is.EqualTo(12));
        Assert.That(set.Select(x => x.Name).Distinct().Count(), Is.EqualTo(12));
    }

    [Test]
    public void CreateCuratedSet_NamesMatchStarDetectorParamsProperties() {
        var set = OptimizerVariable.CreateCuratedSet();
        var expected = new[] {
            nameof(StarDetectorParams.Sensitivity),
            nameof(StarDetectorParams.StarClippingMultiplier),
            nameof(StarDetectorParams.NoiseClippingMultiplier),
            nameof(StarDetectorParams.PeakResponse),
            nameof(StarDetectorParams.MaxDistortion),
            nameof(StarDetectorParams.MinHFR),
            nameof(StarDetectorParams.StarCenterTolerance),
            nameof(StarDetectorParams.StructureLayers),
            nameof(StarDetectorParams.NoiseReductionRadius),
            nameof(StarDetectorParams.MinimumStarBoundingBoxSize),
            nameof(StarDetectorParams.HotpixelThresholdingEnabled),
            nameof(StarDetectorParams.HotpixelThreshold),
        };
        Assert.That(set.Select(x => x.Name), Is.EquivalentTo(expected));
    }

    [Test]
    public void CreateCuratedSet_BoundsAndStepsMatchSpec() {
        var set = OptimizerVariable.CreateCuratedSet().ToDictionary(x => x.Name);
        void Check(string name, OptimizerVariableType type, double lo, double hi, double step) {
            var v = set[name];
            Assert.Multiple(() => {
                Assert.That(v.Type, Is.EqualTo(type), $"{name} type");
                Assert.That(v.Lower, Is.EqualTo(lo), $"{name} lower");
                Assert.That(v.Upper, Is.EqualTo(hi), $"{name} upper");
                Assert.That(v.InitialStep, Is.EqualTo(step), $"{name} step");
            });
        }
        Check(nameof(StarDetectorParams.Sensitivity), OptimizerVariableType.Continuous, 0, 50, 1.0);
        Check(nameof(StarDetectorParams.StarClippingMultiplier), OptimizerVariableType.Continuous, 0.25, 10, 0.5);
        Check(nameof(StarDetectorParams.NoiseClippingMultiplier), OptimizerVariableType.Continuous, 1, 10, 0.5);
        Check(nameof(StarDetectorParams.PeakResponse), OptimizerVariableType.Continuous, 0.1, 1.0, 0.05);
        Check(nameof(StarDetectorParams.MaxDistortion), OptimizerVariableType.Continuous, 0.1, 1.0, 0.1);
        Check(nameof(StarDetectorParams.MinHFR), OptimizerVariableType.Continuous, 0.1, 5.0, 0.25);
        Check(nameof(StarDetectorParams.StarCenterTolerance), OptimizerVariableType.Continuous, 0.05, 1.0, 0.05);
        Check(nameof(StarDetectorParams.StructureLayers), OptimizerVariableType.Integer, 1, 8, 1);
        Check(nameof(StarDetectorParams.NoiseReductionRadius), OptimizerVariableType.Integer, 0, 10, 1);
        Check(nameof(StarDetectorParams.MinimumStarBoundingBoxSize), OptimizerVariableType.Integer, 2, 20, 1);
        Check(nameof(StarDetectorParams.HotpixelThresholdingEnabled), OptimizerVariableType.Boolean, 0, 1, 1);
        Check(nameof(StarDetectorParams.HotpixelThreshold), OptimizerVariableType.Continuous, 0.0001, 0.05, 0.001);
    }

    [Test]
    public void CreateCuratedSet_ReadWriteRoundTripsThroughParams() {
        var set = OptimizerVariable.CreateCuratedSet();
        var p = new StarDetectorParams();
        foreach (var v in set) {
            // Write the midpoint, read it back (quantized) and confirm consistency.
            var mid = (v.Lower + v.Upper) / 2.0;
            v.Write(p, mid);
            var read = v.Read(p);
            Assert.That(read, Is.EqualTo(v.Quantize(mid)).Within(1e-9), $"{v.Name} round-trip");
        }
    }
}
