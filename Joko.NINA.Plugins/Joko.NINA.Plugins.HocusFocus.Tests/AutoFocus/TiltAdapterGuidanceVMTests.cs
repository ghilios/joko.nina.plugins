using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

[TestFixture]
public class TiltAdapterGuidanceVMTests {

    [Test]
    public void FormatAmount_ScrewsTurns_ShowsMagnitudeWithRotationGlyph() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(1.25, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("1.25 ⟳"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.5, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("0.50 ⟲"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.001, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.004, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.005, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("0.01 ⟳"));
        });
    }

    [Test]
    public void FormatAmount_ScrewsDegrees_RoundsToNearestDegreeWithGlyph() {
        Assert.Multiple(() => {
            // 1 turn = 360°, 0.5 turn = 180°, 0.125 turn = 45°.
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(1.0, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("360° ⟳"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.5, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("180° ⟲"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.125, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("45° ⟳"));
            // Rounds to the nearest whole degree: 0.126 turn = 45.36° → 45°; 0.1264 turn = 45.504° → 46°.
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.126, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("45° ⟳"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.1264, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("46° ⟳"));
            // Same physical noise floor as turns: |turns| < 0.005 (= 1.8°) renders as the dash.
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.004, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.004, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("—"));
            // At the floor, 0.005 turn = 1.8° rounds up to 2°.
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.005, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("2° ⟳"));
        });
    }

    [Test]
    public void FormatAmount_Steppers_IgnoreUnitAndShowSignedSteps() {
        // The unit selector never applies to steppers; assert both units render identical whole steps.
        foreach (var unit in new[] { TiltGuidanceAngleUnit.Turns, TiltGuidanceAngleUnit.Degrees }) {
            Assert.Multiple(() => {
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(35.2, steps: true, unit), Is.EqualTo("+35 steps"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(-35.2, steps: true, unit), Is.EqualTo("−35 steps"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.5, steps: true, unit), Is.EqualTo("+1 steps"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.5, steps: true, unit), Is.EqualTo("−1 steps"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.4, steps: true, unit), Is.EqualTo("—"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.2, steps: true, unit), Is.EqualTo("—"));
            });
        }
    }

    [Test]
    public void BuildDirectionLegend_UsesUnitWordAndProvenanceSuffix() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: true, TiltGuidanceAngleUnit.Turns),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in turns"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: true, TiltGuidanceAngleUnit.Degrees),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in degrees"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: false, TiltGuidanceAngleUnit.Degrees),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in degrees (assumed — set or measure in the Tilt Adapter Wizard)"));
            // Steppers ignore the unit word entirely.
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: true, signIsMeasured: true, TiltGuidanceAngleUnit.Degrees),
                Is.EqualTo("⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: true, signIsMeasured: false, TiltGuidanceAngleUnit.Turns),
                Is.EqualTo("⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts (assumed — set or measure in the Tilt Adapter Wizard)"));
        });
    }

    // A fresh guidance object must carry no legend: the legend gate in InspectorVM.RebuildTiltGuidance
    // relies on this default — DirectionLegend is only assigned when guidance rows exist, so the
    // unassigned state must render nothing (HasDirectionLegend gates the XAML row).
    [Test]
    public void FreshGuidance_HasNoDirectionLegend() {
        var vm = new TiltAdapterGuidanceVM();
        Assert.Multiple(() => {
            Assert.That(vm.DirectionLegend, Is.Empty);
            Assert.That(vm.HasDirectionLegend, Is.False);
        });
    }

    // The Screw 4 backfocus arrow is shown only when the adapter has four screws AND the backfocus row
    // is active. This gating bool lets the XAML use a single plain Visibility binding (like every other
    // cell) instead of a MultiDataTrigger that failed to re-apply on the whole-object guidance swap.
    [Test]
    public void HasFourScrewBackfocus_RequiresFourScrewsAndBackfocusRow() {
        Assert.Multiple(() => {
            Assert.That(new TiltAdapterGuidanceVM { ScrewCount = 4, HasBackfocusRow = true }.HasFourScrewBackfocus, Is.True);
            Assert.That(new TiltAdapterGuidanceVM { ScrewCount = 3, HasBackfocusRow = true }.HasFourScrewBackfocus, Is.False);
            Assert.That(new TiltAdapterGuidanceVM { ScrewCount = 4, HasBackfocusRow = false }.HasFourScrewBackfocus, Is.False);
            Assert.That(new TiltAdapterGuidanceVM { ScrewCount = 3, HasBackfocusRow = false }.HasFourScrewBackfocus, Is.False);
        });
    }

    [Test]
    public void ShowAngleUnitSelector_RequiresNumericGuidanceAndScrews() {
        Assert.Multiple(() => {
            Assert.That(new TiltAdapterGuidanceVM { HasNumericGuidance = true, UnitsAreSteps = false }.ShowAngleUnitSelector, Is.True);
            Assert.That(new TiltAdapterGuidanceVM { HasNumericGuidance = true, UnitsAreSteps = true }.ShowAngleUnitSelector, Is.False);
            Assert.That(new TiltAdapterGuidanceVM { HasNumericGuidance = false, UnitsAreSteps = false }.ShowAngleUnitSelector, Is.False);
        });
    }
}
