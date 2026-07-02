using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

[TestFixture]
public class TiltAdapterGuidanceVMTests {

    [Test]
    public void FormatAmount_Screws_ShowsMagnitudeWithRotationGlyph() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(1.25, steps: false), Is.EqualTo("1.25 ⟳"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.5, steps: false), Is.EqualTo("0.50 ⟲"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.001, steps: false), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.004, steps: false), Is.EqualTo("—"));
            // Floor boundary: the dash threshold equals the smallest value that renders, so a
            // misleading "0.00 ⟳" can never be displayed.
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.005, steps: false), Is.EqualTo("0.01 ⟳"));
        });
    }

    [Test]
    public void FormatAmount_Steppers_ShowsSignedSteps() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(35.2, steps: true), Is.EqualTo("+35 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-35.2, steps: true), Is.EqualTo("−35 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.5, steps: true), Is.EqualTo("+1 steps"));
            // Rounding is away-from-zero on BOTH sides of zero (|amount| is rounded, then signed).
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.5, steps: true), Is.EqualTo("−1 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.4, steps: true), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.2, steps: true), Is.EqualTo("—"));
        });
    }

    [Test]
    public void BuildDirectionLegend_IsFixedTextWithProvenanceSuffix() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: true),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in turns"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: false),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in turns (assumed — set or measure in the Tilt Adapter Wizard)"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: true, signIsMeasured: true),
                Is.EqualTo("⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: true, signIsMeasured: false),
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
}
