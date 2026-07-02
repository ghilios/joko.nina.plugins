using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class InspectorVMBehavioralTests {

    private static int CountChanges(INotifyPropertyChanged source, string propertyName, System.Action act) {
        int n = 0;
        PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == propertyName) n++; };
        source.PropertyChanged += handler;
        try { act(); } finally { source.PropertyChanged -= handler; }
        return n;
    }

    [Test]
    public void Construct_WithBundle_DoesNotThrow() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildInspectorVM();
        Assert.That(vm, Is.Not.Null);
    }

    [Test]
    public void HasTiltAdapterCalibration_RequiresCalibratedAndMatchingScrewCount() {
        var bundle = new MediatorBundle();
        bundle.TiltAdapterOptions.IsCalibrated.Returns(false);
        bundle.TiltAdapterOptions.ScrewCount.Returns(3);
        bundle.TiltAdapterOptions.CalibratedScrewCount.Returns(3);
        var vm = bundle.BuildInspectorVM();
        Assert.That(vm.HasTiltAdapterCalibration, Is.False);

        bundle.TiltAdapterOptions.IsCalibrated.Returns(true);
        Assert.That(vm.HasTiltAdapterCalibration, Is.True);

        bundle.TiltAdapterOptions.CalibratedScrewCount.Returns(4);
        Assert.That(vm.HasTiltAdapterCalibration, Is.False);
    }

    [Test]
    public void UpdateDeviceInfo_Camera_FlipsCameraInfo() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildInspectorVM();
        var info = new CameraInfo { Connected = true };
        vm.UpdateDeviceInfo(info);
        Assert.That(vm.CameraInfo, Is.SameAs(info));
    }

    [Test]
    public void UpdateDeviceInfo_Focuser_FlipsFocuserInfo() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildInspectorVM();
        var info = new FocuserInfo { Connected = true, Position = 5000 };
        vm.UpdateDeviceInfo(info);
        Assert.That(vm.FocuserInfo, Is.SameAs(info));
    }

    [Test]
    public void UpdateDeviceInfo_Telescope_FlipsTelescopeInfo() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildInspectorVM();
        var info = new TelescopeInfo { Connected = true };
        vm.UpdateDeviceInfo(info);
        Assert.That(vm.TelescopeInfo, Is.SameAs(info));
    }

    [TestCase(nameof(InspectorVM.SensorCurveModelActive))]
    [TestCase(nameof(InspectorVM.TiltMeasurementActive))]
    [TestCase(nameof(InspectorVM.TiltMeasurementHistoryActive))]
    [TestCase(nameof(InspectorVM.AutoFocusChartActive))]
    [TestCase(nameof(InspectorVM.FWHMContoursActive))]
    [TestCase(nameof(InspectorVM.EccentricityVectorsActive))]
    [TestCase(nameof(InspectorVM.AutoFocusChartActivatedOnce))]
    [TestCase(nameof(InspectorVM.TiltMeasurementActivatedOnce))]
    [TestCase(nameof(InspectorVM.ExposureAnalysisActivatedOnce))]
    [TestCase(nameof(InspectorVM.AutoFocusCompleted))]
    [TestCase(nameof(InspectorVM.SensorModel3DEnabled))]
    public void BoolToggleProperty_RaisesPropertyChangedOnceOnFlip(string propertyName) {
        var vm = new MediatorBundle().BuildInspectorVM();
        var prop = typeof(InspectorVM).GetProperty(propertyName);
        var initial = (bool)prop.GetValue(vm);
        var changes = CountChanges(vm, propertyName, () => {
            prop.SetValue(vm, !initial);
            prop.SetValue(vm, !initial); // unchanged
            prop.SetValue(vm, initial);
        });
        Assert.That(changes, Is.EqualTo(2));
    }

    [TestCase(nameof(InspectorVM.InspectorErrorText))]
    [TestCase(nameof(InspectorVM.SimpleAnalysisErrorText))]
    public void StringProperty_RaisesPropertyChangedOnlyOnChange(string propertyName) {
        var vm = new MediatorBundle().BuildInspectorVM();
        var prop = typeof(InspectorVM).GetProperty(propertyName);
        var changes = CountChanges(vm, propertyName, () => {
            prop.SetValue(vm, "hello");
            prop.SetValue(vm, "hello"); // unchanged
            prop.SetValue(vm, "world");
            prop.SetValue(vm, string.Empty);
        });
        Assert.That(changes, Is.EqualTo(3));
    }

    [Test]
    public void LoopingExposureAnalysis_DelegatesToInspectorOptions() {
        var bundle = new MediatorBundle();
        bundle.InspectorOptions.LoopingExposureAnalysisEnabled.Returns(false);
        var vm = bundle.BuildInspectorVM();
        Assert.That(vm.LoopingExposureAnalysis, Is.False);

        var raisedNames = new List<string>();
        vm.PropertyChanged += (_, e) => raisedNames.Add(e.PropertyName);
        vm.LoopingExposureAnalysis = true;

        bundle.InspectorOptions.Received().LoopingExposureAnalysisEnabled = true;
        Assert.That(raisedNames, Does.Contain(nameof(vm.LoopingExposureAnalysis)));
    }

    [Test]
    public void IsTool_IsTrue() {
        var vm = new MediatorBundle().BuildInspectorVM();
        Assert.That(vm.IsTool, Is.True);
    }

    [Test]
    public void Models_AreInitializedNonNull() {
        var vm = new MediatorBundle().BuildInspectorVM();
        Assert.Multiple(() => {
            Assert.That(vm.TiltModel, Is.Not.Null);
            Assert.That(vm.SensorModel, Is.Not.Null);
            Assert.That(vm.TiltGuidance, Is.Not.Null);
            Assert.That(vm.InspectorOptions, Is.Not.Null);
        });
    }

    [Test]
    public void TiltGuidance_CalibratedButUnmeasured_ShowsNoDirectionLegend() {
        // The legend is gated in RebuildTiltGuidance: it renders only when the rows it annotates exist
        // (HasTiltGuidance / HasNumericGuidance). Driving those true needs a fitted tilt/sensor model,
        // which would take heavy scaffolding — so this pins the reachable half of the gate: with a
        // valid calibration and a non-zero sign but no measurement, the legend must stay hidden even
        // though BuildDirectionLegend would produce text for that sign.
        var bundle = new MediatorBundle();
        bundle.TiltAdapterOptions.IsCalibrated.Returns(true);
        bundle.TiltAdapterOptions.ScrewCount.Returns(3);
        bundle.TiltAdapterOptions.CalibratedScrewCount.Returns(3);
        bundle.TiltAdapterOptions.ScrewInwardCurvatureSign.Returns(1);

        var vm = bundle.BuildInspectorVM();

        Assert.Multiple(() => {
            Assert.That(vm.HasTiltAdapterCalibration, Is.True, "precondition: the calibration is valid");
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, curvatureSign: 1, signIsMeasured: false), Is.Not.Empty,
                "precondition: the legend text itself would be non-empty for this sign");
            Assert.That(vm.TiltGuidance.HasTiltGuidance, Is.False, "no measurement -> no arrow rows");
            Assert.That(vm.TiltGuidance.HasNumericGuidance, Is.False, "no sensor model -> no numeric rows");
            Assert.That(vm.TiltGuidance.DirectionLegend, Is.Empty, "the gate must suppress the legend when no rows exist");
            Assert.That(vm.TiltGuidance.HasDirectionLegend, Is.False);
        });
    }

    [Test]
    public void SignalAmplificationSummary_RefreshesOnInPlaceFocuserSettingEdits() {
        // The summary reads the ACTIVE profile's FocuserSettings; editing those values in place
        // (no profile swap) must re-raise it, filtered to the two properties it consumes.
        var bundle = new MediatorBundle();
        var vm = bundle.BuildInspectorVM();
        var settings = bundle.ProfileService.ActiveProfile.FocuserSettings;

        int relevant = CountChanges(vm, nameof(InspectorVM.SignalAmplificationSummary), () => {
            settings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                settings, new PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusInitialOffsetSteps)));
            settings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                settings, new PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusNumberOfFramesPerPoint)));
            settings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                settings, new PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusExposureTime))); // filtered out
        });
        Assert.That(relevant, Is.EqualTo(2));
    }

    [Test]
    public void SignalAmplificationSummary_RehooksFocuserSettingsOnProfileChange() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildInspectorVM();
        var oldSettings = bundle.ProfileService.ActiveProfile.FocuserSettings;

        var newProfile = Substitute.For<IProfile>();
        bundle.ProfileService.ActiveProfile.Returns(newProfile);
        bundle.ProfileService.ProfileChanged += Raise.Event<EventHandler>(bundle.ProfileService, EventArgs.Empty);
        var newSettings = newProfile.FocuserSettings;

        int fromNew = CountChanges(vm, nameof(InspectorVM.SignalAmplificationSummary), () => {
            newSettings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                newSettings, new PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusInitialOffsetSteps)));
        });
        int fromOld = CountChanges(vm, nameof(InspectorVM.SignalAmplificationSummary), () => {
            oldSettings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                oldSettings, new PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusInitialOffsetSteps)));
        });

        Assert.Multiple(() => {
            Assert.That(fromNew, Is.EqualTo(1), "the new profile's settings must be hooked");
            Assert.That(fromOld, Is.Zero, "the previous profile's settings must be unhooked");
        });
    }
}
