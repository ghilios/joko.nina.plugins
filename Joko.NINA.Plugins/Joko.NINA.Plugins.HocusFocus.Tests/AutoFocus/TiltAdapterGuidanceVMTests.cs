using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

[TestFixture]
public class TiltAdapterGuidanceVMTests {

    [Test]
    public void FormatMagnitude_Screws_ShowsTwoDecimalTurns() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatMagnitude(0.75, steps: false), Is.EqualTo("0.75 turns"));
            Assert.That(TiltAdapterGuidanceVM.FormatMagnitude(-0.301, steps: false), Is.EqualTo("0.30 turns"));
            Assert.That(TiltAdapterGuidanceVM.FormatMagnitude(0.0, steps: false), Is.EqualTo("—"));
        });
    }

    [Test]
    public void FormatMagnitude_Steppers_RoundsToWholeSteps() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatMagnitude(119.6, steps: true), Is.EqualTo("120 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatMagnitude(-120.4, steps: true), Is.EqualTo("120 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatMagnitude(0.4, steps: true), Is.EqualTo("—"));
        });
    }

    [Test]
    public void FormatTotal_Screws_UsesClockwiseWording() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(1.25, steps: false, hasDirection: true), Is.EqualTo("1.25 turns CW"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(-0.5, steps: false, hasDirection: true), Is.EqualTo("0.50 turns CCW"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(0.75, steps: false, hasDirection: false), Is.EqualTo("0.75 turns"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(0.001, steps: false, hasDirection: true), Is.EqualTo("—"));
        });
    }

    [Test]
    public void FormatTotal_Steppers_UsesSignedSteps() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(35.2, steps: true, hasDirection: true), Is.EqualTo("+35 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(-35.2, steps: true, hasDirection: true), Is.EqualTo("−35 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(12.0, steps: true, hasDirection: false), Is.EqualTo("12 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(0.2, steps: true, hasDirection: true), Is.EqualTo("—"));
        });
    }

    // Pins the abs-before-sign ordering: the near-zero check and step rounding operate on the
    // magnitude, so negative near-zero values collapse to "—" and 0.5 steps rounds away from zero.
    [TestCase(-0.2, true, "—")]
    [TestCase(-0.004, false, "—")]
    [TestCase(0.5, true, "+1 steps")]
    [TestCase(0.4, true, "—")]
    public void FormatTotal_NearZeroAndBoundary_TakesAbsBeforeSign(double amount, bool steps, string expected) {
        Assert.That(TiltAdapterGuidanceVM.FormatTotal(amount, steps, hasDirection: true), Is.EqualTo(expected));
    }

    [Test]
    public void BuildDirectionLegend_DescribesAdapterMotionAndProvenance() {
        int cwInwardSign = TiltScrewGeometry.CurvatureSignForCwDirection(true);
        int cwOutwardSign = TiltScrewGeometry.CurvatureSignForCwDirection(false);
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, cwOutwardSign, signIsMeasured: true),
                Is.EqualTo("⬆ = clockwise (adapter moves toward the camera)"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, cwInwardSign, signIsMeasured: true),
                Is.EqualTo("⬆ = clockwise (adapter moves toward the objective)"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: true, cwOutwardSign, signIsMeasured: false),
                Is.EqualTo("⬆ = + steps (adapter moves toward the camera) (assumed — set or measure in the Tilt Adapter Wizard)"));
            // The "(assumed …)" suffix is provenance-only — not coupled to the steps unit.
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, cwOutwardSign, signIsMeasured: false),
                Is.EqualTo("⬆ = clockwise (adapter moves toward the camera) (assumed — set or measure in the Tilt Adapter Wizard)"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, 0, signIsMeasured: false), Is.Empty);
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
}
