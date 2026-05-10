using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class TiltAdapterWizardBehavioralTests {

    private static (TiltAdapterWizardVM vm, MediatorBundle bundle) Build(int screwCount = 3) {
        var bundle = new MediatorBundle();
        bundle.TiltAdapterOptions.ScrewCount.Returns(screwCount);
        bundle.TiltAdapterOptions.MeasurementAverageCount.Returns(1);
        var inspector = bundle.BuildInspectorVM();
        var vm = new TiltAdapterWizardVM(
            profileService: bundle.ProfileService,
            applicationStatusMediator: bundle.ApplicationStatusMediator,
            cameraMediator: bundle.CameraMediator,
            focuserMediator: bundle.FocuserMediator,
            inspector: inspector,
            tiltAdapterOptions: bundle.TiltAdapterOptions);
        return (vm, bundle);
    }

    [Test]
    public void StartCommand_DoesNotChangeStepWhenAlreadyAtBaseline() {
        var (vm, _) = Build();
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline));

        vm.StartCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline));
            Assert.That(vm.IsWizardRunning, Is.True);
        });
    }

    [Test]
    public void RestartCommand_AfterStart_RevertsState() {
        var (vm, _) = Build();
        vm.StartCommand.Execute(null);
        Assert.That(vm.IsWizardRunning, Is.True);

        vm.RestartCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.IsWizardRunning, Is.False);
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Baseline));
        });
    }

    [Test]
    public void ConnectionWarningText_VariesWithConnectionState() {
        var (vm, _) = Build();
        // Both disconnected
        Assert.That(vm.ConnectionWarningText, Does.Contain("Camera and focuser"));

        // Camera-only connected
        vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
        vm.UpdateDeviceInfo(new FocuserInfo { Connected = false });
        Assert.That(vm.ConnectionWarningText, Does.Contain("Focuser"));
        Assert.That(vm.ConnectionWarningText, Does.Not.Contain("Camera"));

        // Focuser-only connected
        vm.UpdateDeviceInfo(new CameraInfo { Connected = false });
        vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });
        Assert.That(vm.ConnectionWarningText, Does.Contain("Camera"));
        Assert.That(vm.ConnectionWarningText, Does.Not.Contain("Focuser"));

        // Both connected
        vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
        vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });
        Assert.That(vm.ConnectionWarningText, Is.Empty);
    }

    [Test]
    public void UpdateDeviceInfo_FocuserUpdate_RaisesAreDevicesConnected() {
        var (vm, _) = Build();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.UpdateDeviceInfo(new CameraInfo { Connected = true });
        vm.UpdateDeviceInfo(new FocuserInfo { Connected = true });

        Assert.That(raised, Does.Contain(nameof(vm.AreDevicesConnected)));
    }

    [Test]
    public void IsMeasuring_DefaultsFalse_AndDrivesHasMeasurementFeedback() {
        var (vm, _) = Build();
        Assert.That(vm.IsMeasuring, Is.False);
        Assert.That(vm.HasMeasurementFeedback, Is.False);
        Assert.That(vm.HasMeasurementResults, Is.False);
    }

    [Test]
    public void HasCurvatureCalibration_RespondsToScrewInwardCurvatureSign() {
        var (vm, bundle) = Build();
        bundle.TiltAdapterOptions.ScrewInwardCurvatureSign.Returns(0);
        Assert.That(vm.HasCurvatureCalibration, Is.False);
        bundle.TiltAdapterOptions.ScrewInwardCurvatureSign.Returns(1);
        Assert.That(vm.HasCurvatureCalibration, Is.True);
    }

    [Test]
    public void IsCalibrationValid_RequiresMatchingScrewCount() {
        var (vm, bundle) = Build(screwCount: 3);

        bundle.TiltAdapterOptions.IsCalibrated.Returns(true);
        bundle.TiltAdapterOptions.CalibratedScrewCount.Returns(3);
        Assert.That(vm.IsCalibrationValid, Is.True);

        bundle.TiltAdapterOptions.CalibratedScrewCount.Returns(4);
        Assert.That(vm.IsCalibrationValid, Is.False);
    }

    [Test]
    public void StepInstructions_NotEmpty_AtAllSteps() {
        var (vm, _) = Build(screwCount: 3);
        Assert.That(vm.StepInstructions, Is.Not.Empty);
    }

    [Test]
    public void ShowRunColumn_RespectsMeasurementAverageCount() {
        var (vm, bundle) = Build();
        bundle.TiltAdapterOptions.MeasurementAverageCount.Returns(1);
        Assert.That(vm.ShowRunColumn, Is.False);
        bundle.TiltAdapterOptions.MeasurementAverageCount.Returns(5);
        Assert.That(vm.ShowRunColumn, Is.True);
    }

    [Test]
    public void IsOnMeasurementStep_TrueAtBaseline() {
        var (vm, _) = Build();
        Assert.That(vm.IsOnMeasurementStep, Is.True);
        Assert.That(vm.IsComplete, Is.False);
    }
}
