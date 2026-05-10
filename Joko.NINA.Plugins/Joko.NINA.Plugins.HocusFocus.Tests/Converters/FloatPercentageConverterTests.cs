using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class FloatPercentageConverterTests {
    private FloatPercentageConverter converter;

    [SetUp]
    public void SetUp() => converter = new FloatPercentageConverter();

    [Test]
    public void Convert_FloatNoParameter_ScalesByOneHundred() =>
        Assert.That(converter.Convert(0.25f, typeof(float), null, CultureInfo.InvariantCulture),
            Is.EqualTo(25f));

    [Test]
    public void Convert_FloatWithDecimalsParameter_RoundsToThatManyDecimals() {
        var result = converter.Convert(0.12345f, typeof(float), "2", CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(12.35).Within(1e-6));
    }

    [Test]
    public void Convert_NullValue_ReturnsZero() =>
        Assert.That(converter.Convert(null, typeof(float), null, CultureInfo.InvariantCulture),
            Is.EqualTo(0));

    [Test]
    public void Convert_EmptyString_ReturnsZero() =>
        Assert.That(converter.Convert(string.Empty, typeof(float), null, CultureInfo.InvariantCulture),
            Is.EqualTo(0));

    [Test]
    public void ConvertBack_PercentString_ScalesDownByOneHundred() {
        var result = converter.ConvertBack("25%", typeof(float), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(0.25f).Within(1e-6f));
    }

    [Test]
    public void ConvertBack_PercentStringWithDecimals_RoundsToDecimalsPlusTwo() {
        var result = converter.ConvertBack("12.35", typeof(float), "2", CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(0.1235).Within(1e-6));
    }

    [Test]
    public void ConvertBack_EmptyString_ReturnsZero() =>
        Assert.That(converter.ConvertBack(string.Empty, typeof(float), null, CultureInfo.InvariantCulture),
            Is.EqualTo(0));
}
