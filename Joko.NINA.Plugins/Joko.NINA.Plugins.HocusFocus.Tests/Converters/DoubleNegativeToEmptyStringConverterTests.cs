using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class DoubleNegativeToEmptyStringConverterTests {
    private DoubleNegativeToEmptyStringConverter converter;

    [SetUp]
    public void SetUp() => converter = new DoubleNegativeToEmptyStringConverter();

    [Test]
    public void Convert_NegativeDouble_ReturnsEmptyString() =>
        Assert.That(converter.Convert(-1.0, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo(string.Empty));

    [Test]
    public void Convert_NonNegativeDouble_PassesThrough() =>
        Assert.That(converter.Convert(2.5, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo(2.5));

    [Test]
    public void Convert_StringInput_ReturnsEmptyString() =>
        Assert.That(converter.Convert("anything", typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo(string.Empty));

    [Test]
    public void ConvertBack_EmptyString_ReturnsNegativeOne() =>
        Assert.That(converter.ConvertBack(string.Empty, typeof(double), null, CultureInfo.InvariantCulture),
            Is.EqualTo(-1.0));

    [Test]
    public void ConvertBack_NonEmptyString_PassesThrough() =>
        Assert.That(converter.ConvertBack("foo", typeof(double), null, CultureInfo.InvariantCulture),
            Is.EqualTo("foo"));
}
