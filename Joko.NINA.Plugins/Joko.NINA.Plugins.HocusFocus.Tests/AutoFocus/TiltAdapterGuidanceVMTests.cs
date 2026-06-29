using NINA.Joko.Plugins.HocusFocus.AutoFocus;
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
    public void FormatTotal_AddsInOutDirection() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(0.95, steps: false, hasDirection: true), Is.EqualTo("0.95 turns IN"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(-0.30, steps: false, hasDirection: true), Is.EqualTo("0.30 turns OUT"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(119.6, steps: true, hasDirection: true), Is.EqualTo("120 steps IN"));
        });
    }

    [Test]
    public void FormatTotal_NoDirection_ShowsMagnitudeOnly() {
        Assert.That(TiltAdapterGuidanceVM.FormatTotal(0.95, steps: false, hasDirection: false), Is.EqualTo("0.95 turns"));
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
