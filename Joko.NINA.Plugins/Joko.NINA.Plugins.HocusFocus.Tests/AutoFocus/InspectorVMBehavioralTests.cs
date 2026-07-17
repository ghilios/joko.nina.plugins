using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
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
        // (HasTiltGuidance / HasNumericGuidance). This pins the suppression half of the gate: with a
        // valid calibration and a non-zero sign but no measurement, the legend must stay hidden even
        // though BuildDirectionLegend would produce text for that state. The positive half (legend
        // rendered once rows exist) is pinned by TiltGuidance_SigmaFlip_FlipsMotionArrowsNotTiltGlyphs.
        var bundle = new MediatorBundle();
        bundle.TiltAdapterOptions.IsCalibrated.Returns(true);
        bundle.TiltAdapterOptions.ScrewCount.Returns(3);
        bundle.TiltAdapterOptions.CalibratedScrewCount.Returns(3);
        bundle.TiltAdapterOptions.ScrewInwardCurvatureSign.Returns(1);

        var vm = bundle.BuildInspectorVM();

        Assert.Multiple(() => {
            Assert.That(vm.HasTiltAdapterCalibration, Is.True, "precondition: the calibration is valid");
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: false, angleUnit: TiltGuidanceAngleUnit.Turns), Is.Not.Empty,
                "precondition: the legend text itself would be non-empty for this state");
            Assert.That(vm.TiltGuidance.HasTiltGuidance, Is.False, "no measurement -> no arrow rows");
            Assert.That(vm.TiltGuidance.HasNumericGuidance, Is.False, "no sensor model -> no numeric rows");
            Assert.That(vm.TiltGuidance.DirectionLegend, Is.Empty, "the gate must suppress the legend when no rows exist");
            Assert.That(vm.TiltGuidance.HasDirectionLegend, Is.False);
        });
    }

    [Test]
    public void TiltGuidance_SigmaFlip_FlipsMotionArrowsNotTiltGlyphs() {
        // The σ-flip matrix from docs/tilt-guidance-motion-arrows-design.md, run with identical model
        // inputs at σ = +1 then σ = −1:
        //   • tilt MOTION arrows  = −σ·turns            → FLIP with σ
        //   • backfocus MOTION arrow = sign(CurvatureEffectMicrons) — rig-independent physics → IDENTICAL
        //   • tilt rotation glyph = sign(TiltMicrons)   → IDENTICAL
        //   • backfocus rotation glyph = sign(σ·BackfocusMicrons) → FLIPS with σ
        // This is the robustness property: a wrong direction setting can flip an arrow OR a glyph,
        // but never both of the same row, so the two vocabularies cannot contradict each other.
        // Also pinned here: the defensive σ = 0 fallback (renders as the default +1) and the
        // legend's positive path (rows rendered ⇒ the fixed legend text renders).
        var bundle = new MediatorBundle();
        bundle.TiltAdapterOptions.IsCalibrated.Returns(true);
        bundle.TiltAdapterOptions.ScrewCount.Returns(3);
        bundle.TiltAdapterOptions.CalibratedScrewCount.Returns(3);
        bundle.TiltAdapterOptions.ScrewInwardCurvatureSign.Returns(1);
        bundle.TiltAdapterOptions.Screw1AngleDegrees.Returns(0.0);
        bundle.TiltAdapterOptions.Screw2AngleDegrees.Returns(120.0);
        bundle.TiltAdapterOptions.Screw3AngleDegrees.Returns(240.0);
        bundle.TiltAdapterOptions.AdjustmentType.Returns(TiltAdjustmentType.Screws);
        bundle.TiltAdapterOptions.ThreadPitchMicrons.Returns(100.0);
        bundle.TiltAdapterOptions.ScrewRadiusMillimeters.Returns(30.0);

        var vm = bundle.BuildInspectorVM();

        // Tilt plane with a pure +Y gradient (A = 0, B = 10): screw 1 (top, θ = 0°) gets the largest
        // CW-positive correction turns (+6.67; screws 2/3 get −3.33), so it is the observation point.
        var imageSize = new System.Drawing.Size(1000, 1000);
        var tiltPlane = TiltPlaneModel.Create(
            imageSize: imageSize, fRatio: 5.0, focuserStepSizeMicrons: 1.0,
            centerFocuser: 5.0, topLeftFocuser: 0.0, topRightFocuser: 0.0,
            bottomLeftFocuser: 10.0, bottomRightFocuser: 10.0);
        vm.TiltModel.SelectedTiltHistoryModel = new SensorTiltHistoryModel(
            historyId: 1, tiltPlaneModel: tiltPlane, backfocusFocuserPositionDelta: 0.0);

        // Fitted paraboloid with positive curvature (K > 0) and the same +Y tilt gradient: the corner
        // curvature effect is 1e-5·(2000² + 2000²) = +80 µm (≥ the 50 µm large-arrow threshold), and
        // screw 1's corrections are tilt = +30 µm (0.30 turns CW) / backfocus = −9000 µm (−90 turns).
        var paraboloid = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: 0, gy: 1e-3, k: 1e-5);
        vm.SensorModel.SelectedTiltHistoryModel = new SensorParaboloidTiltHistoryModel(
            historyId: 1, imageSize: imageSize, pixelSizeMicrons: 4.0, fRatio: 5.0,
            focuserSizeMicrons: 1.0, finalFocusPosition: 0.0, tiltEffectMicrons: 0.0,
            curvatureEffectMicrons: 0.0, autoFocusOffset: 0.0, tiltPlaneModel: null,
            sensorModel: paraboloid);

        TiltAdapterGuidanceVM Rebuild() {
            bundle.TiltAdapterOptions.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                bundle.TiltAdapterOptions, new PropertyChangedEventArgs(nameof(ITiltAdapterOptions.ScrewInwardCurvatureSign)));
            return vm.TiltGuidance;
        }

        var plusSigma = Rebuild();
        bundle.TiltAdapterOptions.ScrewInwardCurvatureSign.Returns(-1);
        var minusSigma = Rebuild();
        bundle.TiltAdapterOptions.ScrewInwardCurvatureSign.Returns(0);
        var zeroSigma = Rebuild();

        Assert.Multiple(() => {
            Assert.That(vm.SensorModel.DisplayedSensorModel, Is.Not.Null, "precondition: the sensor model injection must stick");
            Assert.That(vm.SensorModel.SensorModelResult.CurvatureEffectMicrons, Is.EqualTo(80.0).Within(1e-6),
                "precondition: positive curvature effect above the large-arrow threshold");
            Assert.That(plusSigma.HasTiltGuidance, Is.True, "precondition: tilt arrow row rendered");
            Assert.That(plusSigma.HasBackfocusRow, Is.True, "precondition: backfocus arrow row rendered");
            Assert.That(plusSigma.HasNumericGuidance, Is.True, "precondition: numeric rows rendered");

            // On a default (σ = +1) rig CW moves the adapter toward the camera; screw 1 needs a CW
            // rotation, so its MOTION is toward the camera: ⬇ under the fixed "⬆ = toward the
            // objective" legend. Flipping σ flips the motion the same rotation produces.
            Assert.That(plusSigma.Screw1TiltArrow, Is.EqualTo("⬇"), "σ=+1: CW-needed screw moves toward the camera");
            Assert.That(minusSigma.Screw1TiltArrow, Is.EqualTo("⬆"), "tilt MOTION arrows flip with σ");

            // Positive curvature effect ⇒ the local best-focus position must decrease ⇒ the adapter
            // moves toward the objective — identical on every rig.
            Assert.That(plusSigma.Screw1BackfocusArrow, Is.EqualTo("⬆"), "effect > 0 ⇒ toward the objective");
            Assert.That(minusSigma.Screw1BackfocusArrow, Is.EqualTo("⬆"), "backfocus MOTION arrow is σ-independent");

            // Rotation glyphs are the inverse matrix: tilt rotation is σ-free, backfocus rotation flips.
            Assert.That(plusSigma.Screw1TiltAmount, Is.EqualTo("0.30 ⟳"));
            Assert.That(minusSigma.Screw1TiltAmount, Is.EqualTo("0.30 ⟳"), "tilt rotation glyph is σ-independent");
            Assert.That(plusSigma.Screw1BackfocusAmount, Is.EqualTo("90.00 ⟲"));
            Assert.That(minusSigma.Screw1BackfocusAmount, Is.EqualTo("90.00 ⟳"), "backfocus rotation glyph flips with σ");

            // Defensive σ = 0 (unreachable from persisted options): resolves to
            // TiltScrewGeometry.DefaultScrewInwardCurvatureSign (+1), so the guidance renders
            // exactly as the σ = +1 run — arrows and glyphs alike — rather than suppressing rows.
            Assert.That(zeroSigma.HasTiltGuidance, Is.True, "σ=0 must not suppress the tilt arrows");
            Assert.That(zeroSigma.HasBackfocusRow, Is.True, "σ=0 must not suppress the backfocus arrows");
            Assert.That(zeroSigma.Screw1TiltArrow, Is.EqualTo(plusSigma.Screw1TiltArrow));
            Assert.That(zeroSigma.Screw1BackfocusArrow, Is.EqualTo(plusSigma.Screw1BackfocusArrow));
            Assert.That(zeroSigma.Screw1TiltAmount, Is.EqualTo(plusSigma.Screw1TiltAmount));
            Assert.That(zeroSigma.Screw1BackfocusAmount, Is.EqualTo(plusSigma.Screw1BackfocusAmount));
            Assert.That(zeroSigma.Screw1TotalAmount, Is.EqualTo(plusSigma.Screw1TotalAmount));

            // Legend positive path: rows exist, so the fixed screws legend renders, with the
            // "(assumed)" suffix because the fixture never marks the direction as wizard-measured.
            Assert.That(plusSigma.HasDirectionLegend, Is.True, "rows rendered ⇒ legend rendered");
            Assert.That(plusSigma.DirectionLegend, Is.EqualTo(
                "⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in turns (assumed — set or measure in the Tilt Adapter Wizard)"));
        });
    }

    [Test]
    public void TiltGuidance_AngleDisplayUnitDegrees_RendersAmountsInDegreesAndFlipsLegend() {
        // End-to-end wiring for the Turns/Degrees selector: the ComboBox binds InspectorVM.TiltGuidanceAngleUnit
        // (a passthrough over ITiltAdapterOptions.AngleDisplayUnit); changing that option must fire the same
        // PropertyChanged -> RebuildTiltGuidance path that regenerates every numeric amount AND the legend in
        // the selected unit. Reuses the deterministic fixture from TiltGuidance_SigmaFlip: screw 1 tilt is
        // 0.30 turns CW = 108 degrees.
        var bundle = new MediatorBundle();
        bundle.TiltAdapterOptions.IsCalibrated.Returns(true);
        bundle.TiltAdapterOptions.ScrewCount.Returns(3);
        bundle.TiltAdapterOptions.CalibratedScrewCount.Returns(3);
        bundle.TiltAdapterOptions.ScrewInwardCurvatureSign.Returns(1);
        bundle.TiltAdapterOptions.Screw1AngleDegrees.Returns(0.0);
        bundle.TiltAdapterOptions.Screw2AngleDegrees.Returns(120.0);
        bundle.TiltAdapterOptions.Screw3AngleDegrees.Returns(240.0);
        bundle.TiltAdapterOptions.AdjustmentType.Returns(TiltAdjustmentType.Screws);
        bundle.TiltAdapterOptions.ThreadPitchMicrons.Returns(100.0);
        bundle.TiltAdapterOptions.ScrewRadiusMillimeters.Returns(30.0);

        var vm = bundle.BuildInspectorVM();

        var imageSize = new System.Drawing.Size(1000, 1000);
        var tiltPlane = TiltPlaneModel.Create(
            imageSize: imageSize, fRatio: 5.0, focuserStepSizeMicrons: 1.0,
            centerFocuser: 5.0, topLeftFocuser: 0.0, topRightFocuser: 0.0,
            bottomLeftFocuser: 10.0, bottomRightFocuser: 10.0);
        vm.TiltModel.SelectedTiltHistoryModel = new SensorTiltHistoryModel(
            historyId: 1, tiltPlaneModel: tiltPlane, backfocusFocuserPositionDelta: 0.0);

        var paraboloid = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: 0, gy: 1e-3, k: 1e-5);
        vm.SensorModel.SelectedTiltHistoryModel = new SensorParaboloidTiltHistoryModel(
            historyId: 1, imageSize: imageSize, pixelSizeMicrons: 4.0, fRatio: 5.0,
            focuserSizeMicrons: 1.0, finalFocusPosition: 0.0, tiltEffectMicrons: 0.0,
            curvatureEffectMicrons: 0.0, autoFocusOffset: 0.0, tiltPlaneModel: null,
            sensorModel: paraboloid);

        // The rebuild handler is subscribed to ANY tilt-option PropertyChanged; raise AngleDisplayUnit's,
        // mirroring what a real option change does, and read the freshly-swapped guidance POCO.
        TiltAdapterGuidanceVM Rebuild() {
            bundle.TiltAdapterOptions.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                bundle.TiltAdapterOptions, new PropertyChangedEventArgs(nameof(ITiltAdapterOptions.AngleDisplayUnit)));
            return vm.TiltGuidance;
        }

        var inTurns = Rebuild(); // mock returns the default (Turns)
        bundle.TiltAdapterOptions.AngleDisplayUnit.Returns(TiltGuidanceAngleUnit.Degrees);
        var inDegrees = Rebuild();

        Assert.Multiple(() => {
            Assert.That(inTurns.HasNumericGuidance, Is.True, "precondition: numeric rows rendered");
            Assert.That(inTurns.ShowAngleUnitSelector, Is.True, "screw adapter with numeric guidance shows the selector");

            // FormatAmount received the unit: 0.30 turns CW -> 108 degrees.
            Assert.That(inTurns.Screw1TiltAmount, Is.EqualTo("0.30 ⟳"));
            Assert.That(inDegrees.Screw1TiltAmount, Is.EqualTo("108° ⟳"));

            // BuildDirectionLegend received the unit: the unit word flips.
            Assert.That(inTurns.DirectionLegend, Does.Contain("amounts in turns"));
            Assert.That(inDegrees.DirectionLegend, Does.Contain("amounts in degrees"));

            // Passthrough property: getter proxies the option; setter writes back through it.
            Assert.That(vm.TiltGuidanceAngleUnit, Is.EqualTo(TiltGuidanceAngleUnit.Degrees), "getter proxies the option");
            vm.TiltGuidanceAngleUnit = TiltGuidanceAngleUnit.Turns;
            bundle.TiltAdapterOptions.Received().AngleDisplayUnit = TiltGuidanceAngleUnit.Turns;
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
