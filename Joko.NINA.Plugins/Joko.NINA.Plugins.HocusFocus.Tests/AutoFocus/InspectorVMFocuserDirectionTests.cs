using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NSubstitute;
using NUnit.Framework;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

/// <summary>
/// The structural half of docs/focuser-direction-convention-design.md §2.2: the display-only focuser
/// convention k must change LABELS AND ARROWS and never a number, a glyph, a total, or a plan.
///
/// The design's central guarantee is that a wrong k produces wrong labels and never wrong motion. Placement
/// (k lives on IInspectorOptions, and the math layer gains no k parameter) enforces it structurally; these
/// tests are the executable half, so a future change that threads k into a computed quantity fails here
/// rather than shipping.
/// </summary>
[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class InspectorVMFocuserDirectionTests {

    /// <summary>
    /// Reuses the deterministic fixture from InspectorVMBehavioralTests.TiltGuidance_SigmaFlip: a pure +Y
    /// tilt gradient (screw 1 at the top needs the largest CW correction) plus a positive-curvature
    /// paraboloid whose corner curvature effect is +80 µm, above the large-arrow threshold.
    /// </summary>
    private static (MediatorBundle bundle, InspectorVM vm) BuildGuidanceFixture() {
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

        return (bundle, vm);
    }

    private static TiltAdapterGuidanceVM RebuildAtFocuserSetting(MediatorBundle bundle, InspectorVM vm, bool increasesTowardObjective) {
        bundle.InspectorOptions.FocuserIncreasesTowardObjective.Returns(increasesTowardObjective);
        bundle.InspectorOptions.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
            bundle.InspectorOptions, new PropertyChangedEventArgs(nameof(IInspectorOptions.FocuserIncreasesTowardObjective)));
        return vm.TiltGuidance;
    }

    [Test]
    public void TiltGuidance_FocuserDirectionToggle_FlipsMotionArrowsAndNothingElse() {
        var (bundle, vm) = BuildGuidanceFixture();

        var standard = RebuildAtFocuserSetting(bundle, vm, increasesTowardObjective: false);
        var standardTiltArrow = standard.Screw1TiltArrow;
        var standardBackfocusArrow = standard.Screw1BackfocusArrow;
        var standardTiltAmount = standard.Screw1TiltAmount;
        var standardBackfocusAmount = standard.Screw1BackfocusAmount;
        var standardTotal = standard.Screw1TotalAmount;
        var standardLegend = standard.DirectionLegend;

        var reversed = RebuildAtFocuserSetting(bundle, vm, increasesTowardObjective: true);

        Assert.Multiple(() => {
            Assert.That(standard.HasTiltGuidance, Is.True, "precondition: tilt arrow row rendered");
            Assert.That(standard.HasBackfocusRow, Is.True, "precondition: backfocus arrow row rendered");
            Assert.That(standard.HasNumericGuidance, Is.True, "precondition: numeric rows rendered");

            // At the default k = +1 the guidance is bit-identical to what it rendered before the setting
            // existed: screw 1 needs a CW turn, which on a σ = +1 rig moves it toward the camera.
            Assert.That(standardTiltArrow, Is.EqualTo("⬇"));
            Assert.That(standardBackfocusArrow, Is.EqualTo("⬆"));

            // ARROWS FLIP — they are the only part of the panel that claims a physical direction.
            Assert.That(reversed.Screw1TiltArrow, Is.EqualTo("⬆"), "tilt motion arrow flips with k");
            Assert.That(reversed.Screw1BackfocusArrow, Is.EqualTo("⬇"), "backfocus motion arrow flips with k");

            // EVERYTHING COMPUTED IS IDENTICAL. If any of these ever fail, k has leaked into the math.
            Assert.That(reversed.Screw1TiltAmount, Is.EqualTo(standardTiltAmount), "tilt rotation glyph + amount are k-free");
            Assert.That(reversed.Screw2TiltAmount, Is.EqualTo(standard.Screw2TiltAmount));
            Assert.That(reversed.Screw3TiltAmount, Is.EqualTo(standard.Screw3TiltAmount));
            Assert.That(reversed.Screw1BackfocusAmount, Is.EqualTo(standardBackfocusAmount), "backfocus rotation glyph + amount are k-free");
            Assert.That(reversed.Screw1TotalAmount, Is.EqualTo(standardTotal), "signed totals are k-free");
            Assert.That(reversed.Screw2TotalAmount, Is.EqualTo(standard.Screw2TotalAmount));
            Assert.That(reversed.Screw3TotalAmount, Is.EqualTo(standard.Screw3TotalAmount));

            // The legend DEFINES what ⬆ means, so its text is fixed; only the selection of ⬆ vs ⬇ moves.
            Assert.That(reversed.DirectionLegend, Is.EqualTo(standardLegend), "legend text is a definition, not a claim about this rig");
        });
    }

    [Test]
    public void BackfocusDirection_FollowsTheFocuserSetting_WithoutTouchingTheDelta() {
        var bundle = new MediatorBundle();
        bundle.InspectorOptions.MicronsPerFocuserStep.Returns(-1.0);
        var vm = bundle.BuildInspectorVM();

        // Outer regions focusing above the centre. On a standard focuser that means the sensor sits too far
        // from the flattener, so the advice is to move it TOWARDS; on a reversed focuser the same z-space
        // measurement is the opposite physical situation.
        vm.SetBackfocusMeasurementForTest(inner: 5000.0, outer: 5100.0);
        var standardDirection = vm.BackfocusDirection;
        var standardDelta = vm.BackfocusFocuserPositionDelta;

        bundle.InspectorOptions.FocuserIncreasesTowardObjective.Returns(true);
        bundle.InspectorOptions.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
            bundle.InspectorOptions, new PropertyChangedEventArgs(nameof(IInspectorOptions.FocuserIncreasesTowardObjective)));

        Assert.Multiple(() => {
            Assert.That(standardDirection, Is.EqualTo("TOWARDS"), "default k reproduces the previous unconditional test");
            Assert.That(vm.BackfocusDirection, Is.EqualTo("AWAY FROM"), "the word flips with k");
            Assert.That(vm.BackfocusFocuserPositionDelta, Is.EqualTo(standardDelta),
                "the measured delta is z-space and must not move");
        });
    }
}
