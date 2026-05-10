using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class DoubleGreaterOneToEmptyStringConverterTests {
    private DoubleGreaterOneToEmptyStringConverter converter;

    [SetUp]
    public void SetUp() => converter = new DoubleGreaterOneToEmptyStringConverter();

    [TestCase(1.0)]
    [TestCase(1.5)]
    [TestCase(100.0)]
    public void Convert_GreaterOrEqualOne_ReturnsEmptyString(double input) =>
        Assert.That(converter.Convert(input, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo(string.Empty));

    [Test]
    public void Convert_LessThanOne_ReturnsFormatted() =>
        Assert.That(converter.Convert(0.5, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo("0.50"));

    [Test]
    public void Convert_NonDouble_ReturnsEmptyString() =>
        Assert.That(converter.Convert("foo", typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo(string.Empty));

    [Test]
    public void ConvertBack_NumericString_ReturnsParsedDouble() =>
        Assert.That(converter.ConvertBack("0.5", typeof(double), null, CultureInfo.InvariantCulture),
            Is.EqualTo(0.5));

    [Test]
    public void ConvertBack_Null_ReturnsOne() =>
        Assert.That(converter.ConvertBack(null, typeof(double), null, CultureInfo.InvariantCulture),
            Is.EqualTo(1.0));

    [Test]
    public void ConvertBack_Unparseable_ReturnsOne() =>
        Assert.That(converter.ConvertBack("abc", typeof(double), null, CultureInfo.InvariantCulture),
            Is.EqualTo(1.0));
}
