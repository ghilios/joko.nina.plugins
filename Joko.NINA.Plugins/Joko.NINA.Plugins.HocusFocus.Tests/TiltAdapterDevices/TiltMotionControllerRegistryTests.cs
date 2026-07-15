#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices;

[TestFixture]
public class TiltMotionControllerRegistryTests {

    [TestCase("ASG Electronic EAT - 90mm")]
    [TestCase("ASG Electronic EAT - ZWO 461")]
    public void IsMotorized_TrueForEatPresetNames(string presetName) {
        Assert.That(TiltMotionControllerRegistry.IsMotorized(presetName), Is.True);
    }

    [TestCase("Manual")]
    [TestCase("ASG Photon Cage - 90mm")]
    [TestCase("Some Unknown Device")]
    public void IsMotorized_FalseForNonMotorizedOrUnknownNames(string presetName) {
        Assert.That(TiltMotionControllerRegistry.IsMotorized(presetName), Is.False);
    }

    [Test]
    public void IsMotorized_NullName_ReturnsFalse() {
        Assert.That(TiltMotionControllerRegistry.IsMotorized(null), Is.False);
    }

    // Invariant guard: any future TiltAdapterDevicePreset with AdjustmentType == StepperMotors MUST be
    // registered here, and no non-stepper preset may be. This makes it impossible to add a new motorized
    // preset without also wiring a registry entry (even a placeholder) — forgetting one fails this test.
    [Test]
    public void IsMotorized_MatchesStepperMotorsAdjustmentType_ForEveryKnownPreset() {
        Assert.Multiple(() => {
            foreach (var preset in TiltAdapterDevicePreset.All) {
                bool expectedMotorized = preset.AdjustmentType == TiltAdjustmentType.StepperMotors;
                Assert.That(
                    TiltMotionControllerRegistry.IsMotorized(preset.Name),
                    Is.EqualTo(expectedMotorized),
                    $"Preset \"{preset.Name}\" (AdjustmentType={preset.AdjustmentType}) registry mismatch.");
            }
        });
    }

    [Test]
    public void Create_UnregisteredName_Throws() {
        Assert.Throws<ArgumentException>(() => TiltMotionControllerRegistry.Create("Manual", null));
    }

    [Test]
    public void Create_UnknownName_Throws() {
        Assert.Throws<ArgumentException>(() => TiltMotionControllerRegistry.Create("Not A Real Device", null));
    }

    // T7 flip: the real EatTiltMotionController factory is now wired for both EAT presets (this test used
    // to pin the placeholder NotImplementedException prior to T7 -- that placeholder is gone).
    [TestCase("ASG Electronic EAT - 90mm")]
    [TestCase("ASG Electronic EAT - ZWO 461")]
    public void Create_EatPreset_ReturnsRealEatTiltMotionController(string presetName) {
        var options = Substitute.For<ITiltAdapterOptions>();

        var controller = TiltMotionControllerRegistry.Create(presetName, options);

        Assert.That(controller, Is.InstanceOf<EatTiltMotionController>());
    }

    [Test]
    public void MotorizedPresetNames_ContainsExactlyBothEatPresets() {
        Assert.That(TiltMotionControllerRegistry.MotorizedPresetNames, Is.EquivalentTo(new[] {
            "ASG Electronic EAT - 90mm",
            "ASG Electronic EAT - ZWO 461",
        }));
    }
}

[TestFixture]
public class TiltDeviceCapabilitiesTests {

    [Test]
    public void Constructor_StoresTopologyAndFlags() {
        var caps = new TiltDeviceCapabilities(
            TiltDeviceTopology.FourCornerCoupled,
            new[] { TiltMoveAxis.DiagonalA, TiltMoveAxis.Backfocus },
            supportsPerScrewIndependent: false);

        Assert.Multiple(() => {
            Assert.That(caps.Topology, Is.EqualTo(TiltDeviceTopology.FourCornerCoupled));
            Assert.That(caps.SupportedAxes, Is.EqualTo(new[] { TiltMoveAxis.DiagonalA, TiltMoveAxis.Backfocus }));
            Assert.That(caps.SupportsPerScrewIndependent, Is.False);
        });
    }

    [Test]
    public void Constructor_DefensivelyCopiesAxisList() {
        var axes = new System.Collections.Generic.List<TiltMoveAxis> { TiltMoveAxis.DiagonalA };
        var caps = new TiltDeviceCapabilities(TiltDeviceTopology.FourCornerCoupled, axes, false);

        axes.Add(TiltMoveAxis.Backfocus);

        Assert.That(caps.SupportedAxes, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_NullAxisList_ProducesEmptyCollection() {
        var caps = new TiltDeviceCapabilities(TiltDeviceTopology.FourCornerCoupled, null, false);
        Assert.That(caps.SupportedAxes, Is.Empty);
    }
}

[TestFixture]
public class TiltDevicePositionsTests {

    [Test]
    public void Constructor_StoresPerMotorStepsAndKnown() {
        var positions = new TiltDevicePositions(new[] { 1, 2, 3, 4 }, known: true);
        Assert.Multiple(() => {
            Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(positions.Known, Is.True);
        });
    }

    [Test]
    public void Constructor_DefensivelyCopiesPerMotorSteps() {
        var steps = new System.Collections.Generic.List<int> { 1, 2, 3, 4 };
        var positions = new TiltDevicePositions(steps, known: true);

        steps[0] = 999;

        Assert.That(positions.PerMotorSteps[0], Is.EqualTo(1));
    }

    [Test]
    public void Unknown_HasEmptyStepsAndKnownFalse() {
        Assert.Multiple(() => {
            Assert.That(TiltDevicePositions.Unknown.Known, Is.False);
            Assert.That(TiltDevicePositions.Unknown.PerMotorSteps, Is.Empty);
        });
    }
}
