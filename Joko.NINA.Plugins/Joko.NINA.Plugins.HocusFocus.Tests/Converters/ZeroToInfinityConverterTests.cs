using System;
using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class ZeroToInfinityConverterTests {
    private ZeroToInfinityConverter converter;

    [SetUp]
    public void SetUp() => converter = new ZeroToInfinityConverter();

    [Test]
    public void Convert_Zero_ReturnsUnlimited() =>
        Assert.That(converter.Convert(0, typeof(string), null, CultureInfo.InvariantCulture), Is.EqualTo("unlimited"));

    [Test]
    public void Convert_Negative_ReturnsUnlimited() =>
        Assert.That(converter.Convert(-1, typeof(string), null, CultureInfo.InvariantCulture), Is.EqualTo("unlimited"));

    [Test]
    public void Convert_Positive_ReturnsString() =>
        Assert.That(converter.Convert(7, typeof(string), null, CultureInfo.InvariantCulture), Is.EqualTo("7"));

    [Test]
    public void Convert_NonInt_Throws() =>
        Assert.That(() => converter.Convert("abc", typeof(string), null, CultureInfo.InvariantCulture),
            Throws.ArgumentException);

    [Test]
    public void ConvertBack_Unlimited_ReturnsZero() =>
        Assert.That(converter.ConvertBack("unlimited", typeof(int), null, CultureInfo.InvariantCulture), Is.EqualTo(0));

    [Test]
    public void ConvertBack_NumericString_ReturnsParsedInt() =>
        Assert.That(converter.ConvertBack("42", typeof(int), null, CultureInfo.InvariantCulture), Is.EqualTo(42));

    [Test]
    public void ConvertBack_NonString_Throws() =>
        Assert.That(() => converter.ConvertBack(42, typeof(int), null, CultureInfo.InvariantCulture),
            Throws.ArgumentException);

    [Test]
    public void ConvertBack_Unparseable_Throws() =>
        Assert.That(() => converter.ConvertBack("abc", typeof(int), null, CultureInfo.InvariantCulture),
            Throws.TypeOf<FormatException>());
}
