#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
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
    public void CreateCuratedSet_HasTwentyOneDistinctVariables() {
        // 12 base axes + 9 defocus-aware axes (combined gate switch, structure boost, 3 gate knobs, 4 donut knobs).
        // The no-arg form returns the FULL set (master gating is applied via the StarDetectorParams overload).
        var set = OptimizerVariable.CreateCuratedSet();
        Assert.That(set.Count, Is.EqualTo(21));
        Assert.That(set.Select(x => x.Name).Distinct().Count(), Is.EqualTo(21));
    }

    [Test]
    public void CreateCuratedSet_Seed_GatesDefocusAxesByMaster() {
        var masterOff = new StarDetectorParams { DefocusAwareDonutDetection = false };
        var masterOn = new StarDetectorParams { DefocusAwareDonutDetection = true };
        Assert.Multiple(() => {
            // Master OFF ⇒ only the 12 base axes; no defocus axis is searchable.
            Assert.That(OptimizerVariable.CreateCuratedSet(masterOff).Count, Is.EqualTo(12));
            Assert.That(OptimizerVariable.CreateCuratedSet(masterOff).Any(v => v.Name == OptimizerVariable.DefocusAwareGatesName), Is.False);
            Assert.That(OptimizerVariable.CreateCuratedSet((StarDetectorParams)null).Count, Is.EqualTo(12));
            // Master ON ⇒ the full set incl. all defocus axes.
            Assert.That(OptimizerVariable.CreateCuratedSet(masterOn).Count, Is.EqualTo(21));
            Assert.That(OptimizerVariable.CreateCuratedSet(masterOn).Any(v => v.Name == nameof(StarDetectorParams.DonutMorphCloseSize)), Is.True);
        });
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
            // Synthetic combined switch (drives DefocusAwareDistortion + DefocusAwareCentering together).
            OptimizerVariable.DefocusAwareGatesName,
            // Synthetic integer knob (drives DefocusAwareStructure + StructureLayerBoost together).
            OptimizerVariable.DefocusAwareStructureName,
            // Defocus-aware gate tuning knobs + donut/spike knobs (master-gated; present in the full no-arg set).
            nameof(StarDetectorParams.DefocusDistortionSizeReference),
            nameof(StarDetectorParams.DefocusDistortionMinFactor),
            nameof(StarDetectorParams.DefocusCenteringToleranceFactor),
            nameof(StarDetectorParams.DonutMorphCloseSize),
            nameof(StarDetectorParams.DonutMinAnnularityHoleFraction),
            nameof(StarDetectorParams.DonutMaxStreakEccentricity),
            nameof(StarDetectorParams.DonutSaturationBloomRadius),
        };
        Assert.That(set.Select(x => x.Name), Is.EquivalentTo(expected));
    }

    // ---- MaxDistortion's searchable ceiling is GEOMETRY, not a tuning choice (F98) ----

    /// <summary>
    /// The axis ceiling has to be the fill ratio the detector's own metric assigns to a perfectly round star,
    /// because the gate rejects when <c>(star pixels)/d² &lt; MaxDistortion</c>. Rasterise disks and confirm the
    /// constant is that ratio rather than a number someone liked — the sequence converges to π/4 from below.
    /// </summary>
    [Test]
    public void MaxDistortionSearchUpper_IsTheFillRatioOfARasterisedPerfectDisk() {
        static double DiskFillRatio(int d) {
            var r = d / 2.0;
            var count = 0;
            for (var y = 0; y < d; y++) {
                for (var x = 0; x < d; x++) {
                    var dx = x + 0.5 - r;
                    var dy = y + 0.5 - r;
                    if ((dx * dx) + (dy * dy) <= r * r) {
                        count++;
                    }
                }
            }
            return count / (double)(d * d);
        }

        Assert.Multiple(() => {
            Assert.That(DiskFillRatio(64), Is.EqualTo(OptimizerVariable.MaxDistortionSearchUpper).Within(0.01));
            Assert.That(DiskFillRatio(512), Is.EqualTo(OptimizerVariable.MaxDistortionSearchUpper).Within(0.002));
            // And it is genuinely below the old 1.0 ceiling, which is the whole point of the bound.
            Assert.That(OptimizerVariable.MaxDistortionSearchUpper, Is.LessThan(1.0));
        });
    }

    /// <summary>
    /// The coarse grid samples <c>Lower + t·(Upper − Lower)</c> inclusive of BOTH bounds, so the ceiling is
    /// itself evaluated. Under the old 1.0 upper the top level rejected every round star — one of this axis's
    /// four levels spent on a setting that returns zero detections by construction.
    /// </summary>
    [Test]
    public void MaxDistortionAxis_EveryCoarseGridLevel_IsSatisfiableByARoundStar() {
        var v = OptimizerVariable.CreateCuratedSet().Single(x => x.Name == nameof(StarDetectorParams.MaxDistortion));
        const int Levels = 4;   // StarDetectionOptimizer.CoarseGridLevels default
        for (var i = 0; i < Levels; i++) {
            var t = i / (double)(Levels - 1);
            var value = v.Lower + (t * (v.Upper - v.Lower));
            Assert.That(value, Is.LessThanOrEqualTo(Math.PI / 4.0),
                $"coarse-grid level {i} of {Levels} sits above a perfect disk's fill ratio, so it rejects every round star");
        }
    }

    [Test]
    public void MaxDistortionAxis_WriteClampsAboveTheGeometricCeiling() {
        var v = OptimizerVariable.CreateCuratedSet().Single(x => x.Name == nameof(StarDetectorParams.MaxDistortion));
        var p = new StarDetectorParams();
        v.Write(p, 1.0);
        Assert.That(p.MaxDistortion, Is.EqualTo(Math.PI / 4.0).Within(1e-12));
    }

    // ---- DefocusAwareGates combined switch (F3) ----

    [Test]
    public void CreateCuratedSet_IncludesDefocusAwareGates_AsBoolean() {
        var v = OptimizerVariable.CreateCuratedSet().Single(x => x.Name == OptimizerVariable.DefocusAwareGatesName);
        Assert.Multiple(() => {
            Assert.That(v.Type, Is.EqualTo(OptimizerVariableType.Boolean));
            Assert.That(v.Lower, Is.EqualTo(0));
            Assert.That(v.Upper, Is.EqualTo(1));
        });
    }

    [Test]
    public void DefocusAwareGates_WritingTrue_SetsBothFlags() {
        var v = OptimizerVariable.CreateCuratedSet().Single(x => x.Name == OptimizerVariable.DefocusAwareGatesName);
        var p = new StarDetectorParams { DefocusAwareDistortion = false, DefocusAwareCentering = false };
        v.Write(p, 1.0);
        Assert.Multiple(() => {
            Assert.That(p.DefocusAwareDistortion, Is.True);
            Assert.That(p.DefocusAwareCentering, Is.True);
        });
    }

    [Test]
    public void DefocusAwareGates_WritingFalse_ClearsBothFlags() {
        var v = OptimizerVariable.CreateCuratedSet().Single(x => x.Name == OptimizerVariable.DefocusAwareGatesName);
        var p = new StarDetectorParams { DefocusAwareDistortion = true, DefocusAwareCentering = true };
        v.Write(p, 0.0);
        Assert.Multiple(() => {
            Assert.That(p.DefocusAwareDistortion, Is.False);
            Assert.That(p.DefocusAwareCentering, Is.False);
        });
    }

    [Test]
    public void DefocusAwareGates_Read_ReflectsTheDistortionFlag() {
        var v = OptimizerVariable.CreateCuratedSet().Single(x => x.Name == OptimizerVariable.DefocusAwareGatesName);
        var on = new StarDetectorParams { DefocusAwareDistortion = true, DefocusAwareCentering = true };
        var off = new StarDetectorParams { DefocusAwareDistortion = false, DefocusAwareCentering = false };
        Assert.Multiple(() => {
            Assert.That(v.Read(on), Is.EqualTo(1.0));
            Assert.That(v.Read(off), Is.EqualTo(0.0));
        });
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
        Check(nameof(StarDetectorParams.MaxDistortion), OptimizerVariableType.Continuous, 0.1, Math.PI / 4.0, 0.1);
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

    // ---- Warm-start set (Phase 4) ------------------------------------------------------------------------

    [Test]
    public void WarmStartSet_MovedAxis_GetsNarrowBandAroundRecommended() {
        var before = new StarDetectorParams { Sensitivity = 16.0 };
        var after = new StarDetectorParams { Sensitivity = 3.5 };
        var ws = OptimizerVariable.CreateWarmStartSet(OptimizerVariable.CreateCuratedSet(), before, after);

        var sens = ws.SingleOrDefault(v => v.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.That(sens, Is.Not.Null, "a moved axis must be present in the warm-start set");
        Assert.Multiple(() => {
            // Sensitivity step is 1.0; default bandSteps = 3 ⇒ [3.5-3, 3.5+3] = [0.5, 6.5], clamped to [0,50].
            Assert.That(sens.Lower, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(sens.Upper, Is.EqualTo(6.5).Within(1e-9));
            // The new Write re-quantizes through the band so the optimizer cannot escape it.
            var p = new StarDetectorParams();
            sens.Write(p, 100.0);
            Assert.That(p.Sensitivity, Is.EqualTo(6.5).Within(1e-9));
        });
    }

    [Test]
    public void WarmStartSet_UnmovedNonDefocusAxis_IsOmitted() {
        var before = new StarDetectorParams { Sensitivity = 16.0, MaxDistortion = 0.5 };
        var after = new StarDetectorParams { Sensitivity = 3.5, MaxDistortion = 0.5 }; // MaxDistortion unchanged
        var ws = OptimizerVariable.CreateWarmStartSet(OptimizerVariable.CreateCuratedSet(), before, after);
        Assert.That(ws.Any(v => v.Name == nameof(StarDetectorParams.MaxDistortion)), Is.False,
            "an unmoved, non-defocus axis is pinned by omission (the seed carries its value)");
    }

    [Test]
    public void WarmStartSet_DefocusToggles_AlwaysLiveAtFullRange() {
        // Nothing moved — but the defocus-aware toggles must still be explorable.
        var before = new StarDetectorParams();
        var after = new StarDetectorParams();
        var ws = OptimizerVariable.CreateWarmStartSet(OptimizerVariable.CreateCuratedSet(), before, after);

        var gates = ws.SingleOrDefault(v => v.Name == OptimizerVariable.DefocusAwareGatesName);
        var structure = ws.SingleOrDefault(v => v.Name == OptimizerVariable.DefocusAwareStructureName);
        Assert.Multiple(() => {
            Assert.That(gates, Is.Not.Null, "DefocusAwareGates must always stay live");
            Assert.That(structure, Is.Not.Null, "DefocusAwareStructure must always stay live");
            Assert.That(gates.Lower, Is.EqualTo(0));
            Assert.That(gates.Upper, Is.EqualTo(1));
            Assert.That(structure.Upper, Is.EqualTo(4), "structure boost keeps its full [0,4] range");
        });
    }

    // ---- F23 mechanism (b): a floor on the searchable Sensitivity range ----

    [Test]
    public void CreateCuratedSet_ShippingSensitivityBounds_AreUnflooredZeroToFifty() {
        // Pins the SHIPPING domain. DefaultSensitivityLower is 0.0 — the false-positive cost is carried by the
        // objective's SMarginalSnr term, not by restricting the search domain. If this assertion is ever changed,
        // it means mechanism (b) shipped instead of (a), which is a documented decision, not an incidental edit.
        var sens = OptimizerVariable.CreateCuratedSet().Single(v => v.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.Multiple(() => {
            Assert.That(OptimizerVariable.DefaultSensitivityLower, Is.EqualTo(0.0));
            Assert.That(sens.Lower, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(sens.Upper, Is.EqualTo(50.0).Within(1e-9));
        });
    }

    [Test]
    public void CreateCuratedSet_ExplicitSensitivityFloor_RaisesOnlyThatAxisLowerBound() {
        var seed = new StarDetectorParams { DefocusAwareDonutDetection = false };
        var floored = OptimizerVariable.CreateCuratedSet(seed, 2.5);
        var sens = floored.Single(v => v.Name == nameof(StarDetectorParams.Sensitivity));
        var clip = floored.Single(v => v.Name == nameof(StarDetectorParams.StarClippingMultiplier));
        Assert.Multiple(() => {
            Assert.That(sens.Lower, Is.EqualTo(2.5).Within(1e-9));
            Assert.That(sens.Upper, Is.EqualTo(50.0).Within(1e-9), "the ceiling is untouched");
            Assert.That(clip.Lower, Is.EqualTo(0.25).Within(1e-9), "no other axis moves");
            // The floor is enforced through the variable's own Quantize, so the optimizer cannot propose beneath it.
            var p = new StarDetectorParams();
            sens.Write(p, 0.0);
            Assert.That(p.Sensitivity, Is.EqualTo(2.5).Within(1e-9));
        });
    }

    [Test]
    public void CreateCuratedSet_NullSensitivityFloor_IsTheShippingDefault() {
        var seed = new StarDetectorParams { DefocusAwareDonutDetection = false };
        var explicitDefault = OptimizerVariable.CreateCuratedSet(seed, null)
            .Single(v => v.Name == nameof(StarDetectorParams.Sensitivity));
        var shipping = OptimizerVariable.CreateCuratedSet(seed)
            .Single(v => v.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.That(explicitDefault.Lower, Is.EqualTo(shipping.Lower));
    }

    [Test]
    public void CreateCuratedSet_SensitivityFloor_PropagatesIntoTheWarmStartBand() {
        // CreateWarmStartSet clamps with Math.Max(v.Lower, …), so a floored base set must floor the warm-start band
        // too — otherwise the second optimize pass would silently escape back below the floor.
        var seed = new StarDetectorParams { DefocusAwareDonutDetection = false };
        var before = new StarDetectorParams { Sensitivity = 16.0 };
        var after = new StarDetectorParams { Sensitivity = 3.5 };
        var ws = OptimizerVariable.CreateWarmStartSet(OptimizerVariable.CreateCuratedSet(seed, 2.5), before, after);
        var sens = ws.Single(v => v.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.Multiple(() => {
            // Unfloored this band is [0.5, 6.5]; the 2.5 floor dominates the lower edge.
            Assert.That(sens.Lower, Is.EqualTo(2.5).Within(1e-9));
            Assert.That(sens.Upper, Is.EqualTo(6.5).Within(1e-9));
        });
    }
}
