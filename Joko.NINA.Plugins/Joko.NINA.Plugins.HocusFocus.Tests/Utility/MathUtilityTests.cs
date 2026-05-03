using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class MathUtilityTests {

    [Test]
    public void MedianMAD_ScalesOddLengthMedianAbsoluteDeviation() {
        var (median, mad) = new[] { 1.0, 2.0, 3.0 }.MedianMAD();

        Assert.Multiple(() => {
            Assert.That(median, Is.EqualTo(2.0));
            Assert.That(mad, Is.EqualTo(1.483).Within(1e-12));
        });
    }

    [Test]
    public void MedianMAD_ScalesEvenLengthMedianAbsoluteDeviation() {
        var (median, mad) = new[] { 1.0, 2.0, 4.0, 100.0 }.MedianMAD();

        Assert.Multiple(() => {
            Assert.That(median, Is.EqualTo(3.0));
            Assert.That(mad, Is.EqualTo(2.2245).Within(1e-12));
        });
    }
}
