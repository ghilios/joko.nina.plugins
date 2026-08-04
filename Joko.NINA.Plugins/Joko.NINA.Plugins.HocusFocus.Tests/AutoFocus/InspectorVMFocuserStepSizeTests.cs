#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Equipment.Equipment.MyFocuser;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

/// <summary>
/// InspectorVM is the single writer of the driver-reported focuser step size — it is the plugin's registered
/// focuser consumer, and a Shared MEF singleton. See docs/focuser-step-size-driver-design.md §2.
/// </summary>
[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class InspectorVMFocuserStepSizeTests {

    // A real InspectorOptions rather than a substitute: the whole point of these tests is the setter's
    // accept-only-valid guard, which a substitute would replace with a plain auto-property.
    private static (MediatorBundle bundle, InspectorVM vm, InspectorOptions options) Build() {
        var bundle = new MediatorBundle();
        var options = new InspectorOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
        var vm = bundle.BuildInspectorVM(inspectorOptions: options);
        return (bundle, vm, options);
    }

    [Test]
    public void UpdateDeviceInfo_PublishesAUsableDriverStepSize() {
        var (_, vm, options) = Build();

        vm.UpdateDeviceInfo(new FocuserInfo { Connected = true, Position = 5000, StepSize = 3.5 });

        Assert.Multiple(() => {
            Assert.That(options.DriverMicronsPerFocuserStep, Is.EqualTo(3.5));
            Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(3.5),
                "with no override, the driver's value is what everything computes with");
        });
    }

    [Test]
    public void UpdateDeviceInfo_DisconnectDoesNotClearTheStepSize() {
        // A disconnected focuser reports StepSize = 0. Dropping that write is what keeps an in-flight
        // analysis from being silently rescaled when the focuser drops off the bus.
        var (_, vm, options) = Build();
        vm.UpdateDeviceInfo(new FocuserInfo { Connected = true, StepSize = 3.5 });

        vm.UpdateDeviceInfo(new FocuserInfo { Connected = false, StepSize = 0.0 });

        Assert.Multiple(() => {
            Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(3.5));
            Assert.That(vm.FocuserInfo.Connected, Is.False, "the disconnect itself still lands");
        });
    }

    [Test]
    public void UpdateDeviceInfo_DriverThatDoesNotImplementStepSize_LeavesTheInspectorUncalibrated() {
        var (_, vm, options) = Build();

        vm.UpdateDeviceInfo(new FocuserInfo { Connected = true, StepSize = 0.0 });

        Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(-1.0),
            "unset must stay unset — never a fabricated number");
    }

    [Test]
    public void BackfocusDelta_UsesTheDriverStepSize_WhenThereIsNoOverride() {
        // The end-to-end statement: a driver-supplied step size reaches a reported micron value with no user
        // input at all, which is the point of the whole change.
        var (_, vm, options) = Build();
        vm.UpdateDeviceInfo(new FocuserInfo { Connected = true, StepSize = 2.0 });

        vm.SetBackfocusMeasurementForTest(inner: 5000.0, outer: 5100.0);

        Assert.Multiple(() => {
            Assert.That(vm.BackfocusFocuserPositionDelta, Is.EqualTo(100.0));
            Assert.That(vm.BackfocusMicronDelta, Is.EqualTo(200.0), "100 steps x 2 µm/step");

            // And the override still wins over it.
            options.MicronsPerFocuserStep = 1.0;
            vm.SetBackfocusMeasurementForTest(inner: 5000.0, outer: 5100.0);
            Assert.That(vm.BackfocusMicronDelta, Is.EqualTo(100.0), "the override supersedes the driver");
        });
    }

    [Test]
    public void MismatchFlag_FiresOnlyWhenAnOverrideContradictsAValidDriverValue() {
        var (_, vm, options) = Build();

        Assert.Multiple(() => {
            vm.UpdateDeviceInfo(new FocuserInfo { Connected = true, StepSize = 1.0 });
            Assert.That(options.HasFocuserStepSizeMismatch, Is.False, "no override yet");

            options.MicronsPerFocuserStep = 1.005;
            Assert.That(options.HasFocuserStepSizeMismatch, Is.False,
                "a calibration landing near the driver's round number must not nag");

            // The failure this exists to catch: a driver reporting steps rather than microns.
            options.MicronsPerFocuserStep = 3.6;
            Assert.That(options.HasFocuserStepSizeMismatch, Is.True);
            Assert.That(options.EffectiveMicronsPerFocuserStep, Is.EqualTo(3.6),
                "advisory only — the override still wins");
        });
    }
}
