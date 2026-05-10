using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class ZeroToDoubleDashConverterTests {
    private ZeroToDoubleDashConverter converter;

    [SetUp]
    public void SetUp() => converter = new ZeroToDoubleDashConverter();

    [Test]
    public void Convert_ZeroInt_ReturnsDoubleDash() =>
        Assert.That(converter.Convert(0, typeof(string), null, CultureInfo.InvariantCulture), Is.EqualTo("--"));

    [Test]
    public void Convert_ZeroLong_ReturnsDoubleDash() =>
        Assert.That(converter.Convert(0L, typeof(string), null, CultureInfo.InvariantCulture), Is.EqualTo("--"));

    [Test]
    public void Convert_NonZeroInt_ReturnsValue() =>
        Assert.That(converter.Convert(7, typeof(string), null, CultureInfo.InvariantCulture), Is.EqualTo(7));

    [Test]
    public void Convert_String_PassesThrough() =>
        Assert.That(converter.Convert("foo", typeof(string), null, CultureInfo.InvariantCulture), Is.EqualTo("foo"));

    [Test]
    public void Convert_Null_PassesThroughAsNull() =>
        Assert.That(converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture), Is.Null);

    [Test]
    public void ConvertBack_DoubleDash_ReturnsZero() =>
        Assert.That(converter.ConvertBack("--", typeof(int), null, CultureInfo.InvariantCulture), Is.EqualTo(0));

    [Test]
    public void ConvertBack_OtherString_PassesThrough() =>
        Assert.That(converter.ConvertBack("42", typeof(int), null, CultureInfo.InvariantCulture), Is.EqualTo("42"));
}
