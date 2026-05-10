using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class DecimalDegreesToArcsecDoubleDashConverterTests {
    private DecimalDegreesToArcsecDoubleDashConverter converter;

    [SetUp]
    public void SetUp() => converter = new DecimalDegreesToArcsecDoubleDashConverter();

    [Test]
    public void Convert_DecimalMin_ReturnsDoubleDash() =>
        Assert.That(converter.Convert(decimal.MinValue, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo("--"));

    [Test]
    public void Convert_OneDegree_ReturnsThirtySixHundredArcsec() =>
        Assert.That(converter.Convert(1m, typeof(decimal), null, CultureInfo.InvariantCulture),
            Is.EqualTo(3600m));

    [Test]
    public void Convert_HalfDegree_ReturnsEighteenHundredArcsec() =>
        Assert.That(converter.Convert(0.5m, typeof(decimal), null, CultureInfo.InvariantCulture),
            Is.EqualTo(1800m));

    [Test]
    public void Convert_NonDecimal_PassesThrough() =>
        Assert.That(converter.Convert("foo", typeof(decimal), null, CultureInfo.InvariantCulture),
            Is.EqualTo("foo"));

    [Test]
    public void ConvertBack_DoubleDash_ReturnsDecimalMin() =>
        Assert.That(converter.ConvertBack("--", typeof(decimal), null, CultureInfo.InvariantCulture),
            Is.EqualTo(decimal.MinValue));

    [Test]
    public void ConvertBack_NumericString_ConvertsArcsecToDegrees() =>
        Assert.That(converter.ConvertBack("3600", typeof(decimal), null, CultureInfo.InvariantCulture),
            Is.EqualTo(1m));
}
