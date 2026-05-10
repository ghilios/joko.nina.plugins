using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;
using Color = System.Windows.Media.Color;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class InterpolateColorConverterTests {
    private InterpolateColorConverter converter;
    private Color start;
    private Color end;

    [SetUp]
    public void SetUp() {
        converter = new InterpolateColorConverter();
        start = Color.FromArgb(255, 0, 0, 0);
        end = Color.FromArgb(255, 100, 200, 50);
    }

    [Test]
    public void Convert_IndexZero_ReturnsStartColor() {
        var result = (Color)converter.Convert(new object[] { start, end, 0, 10 }, typeof(Color), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(start));
    }

    [Test]
    public void Convert_TotalZero_ReturnsStartColor() {
        var result = (Color)converter.Convert(new object[] { start, end, 5, 0 }, typeof(Color), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(start));
    }

    [Test]
    public void Convert_IndexExceedsTotal_ReturnsEndColor() {
        var result = (Color)converter.Convert(new object[] { start, end, 11, 10 }, typeof(Color), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(end));
    }

    [Test]
    public void Convert_Midpoint_ReturnsHalfwayInterpolation() {
        var result = (Color)converter.Convert(new object[] { start, end, 5, 10 }, typeof(Color), null, CultureInfo.InvariantCulture);
        Assert.Multiple(() => {
            Assert.That(result.R, Is.EqualTo(50));
            Assert.That(result.G, Is.EqualTo(100));
            Assert.That(result.B, Is.EqualTo(25));
        });
    }

    [Test]
    public void Convert_FullIndex_ReturnsEndColor() {
        var result = (Color)converter.Convert(new object[] { start, end, 10, 10 }, typeof(Color), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(end));
    }
}
