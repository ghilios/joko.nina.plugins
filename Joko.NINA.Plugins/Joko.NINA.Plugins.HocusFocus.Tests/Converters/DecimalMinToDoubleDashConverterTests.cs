using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class DecimalMinToDoubleDashConverterTests {
    private DecimalMinToDoubleDashConverter converter;

    [SetUp]
    public void SetUp() => converter = new DecimalMinToDoubleDashConverter();

    [Test]
    public void Convert_DecimalMinValue_ReturnsDoubleDash() =>
        Assert.That(converter.Convert(decimal.MinValue, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo("--"));

    [Test]
    public void Convert_OtherDecimal_PassesThrough() =>
        Assert.That(converter.Convert(1.5m, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo(1.5m));

    [Test]
    public void Convert_NonDecimal_PassesThrough() =>
        Assert.That(converter.Convert("foo", typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo("foo"));

    [Test]
    public void ConvertBack_DoubleDash_ReturnsDecimalMin() =>
        Assert.That(converter.ConvertBack("--", typeof(decimal), null, CultureInfo.InvariantCulture),
            Is.EqualTo(decimal.MinValue));

    [Test]
    public void ConvertBack_OtherString_PassesThrough() =>
        Assert.That(converter.ConvertBack("foo", typeof(decimal), null, CultureInfo.InvariantCulture),
            Is.EqualTo("foo"));
}
