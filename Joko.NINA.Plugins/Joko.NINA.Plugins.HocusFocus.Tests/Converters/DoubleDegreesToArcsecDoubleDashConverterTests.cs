using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class DoubleDegreesToArcsecDoubleDashConverterTests {
    private DoubleDegreesToArcsecDoubleDashConverter converter;

    [SetUp]
    public void SetUp() => converter = new DoubleDegreesToArcsecDoubleDashConverter();

    [Test]
    public void Convert_OneDegree_Returns3600Arcsec() =>
        Assert.That(converter.Convert(1.0, typeof(double), null, CultureInfo.InvariantCulture),
            Is.EqualTo(3600.0));

    [Test]
    public void Convert_HalfDegree_Returns1800Arcsec() =>
        Assert.That(converter.Convert(0.5, typeof(double), null, CultureInfo.InvariantCulture),
            Is.EqualTo(1800.0));

    [Test]
    public void Convert_NaN_ReturnsDoubleDash() =>
        Assert.That(converter.Convert(double.NaN, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo("--"));

    [Test]
    public void Convert_NonDouble_PassesThrough() =>
        Assert.That(converter.Convert("foo", typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo("foo"));

    [Test]
    public void ConvertBack_DoubleDash_ReturnsNaN() {
        var result = converter.ConvertBack("--", typeof(double), null, CultureInfo.InvariantCulture);
        Assert.That(double.IsNaN((double)result), Is.True);
    }

    [Test]
    public void ConvertBack_NumericString_ConvertsArcsecToDegrees() =>
        Assert.That(converter.ConvertBack("3600", typeof(double), null, CultureInfo.InvariantCulture),
            Is.EqualTo(1.0));
}
