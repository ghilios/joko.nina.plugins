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
}
