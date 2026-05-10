using System;
using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class DoubleNegativeToVisibilityConverterTests {
    private DoubleNegativeToVisibilityConverter converter;

    [SetUp]
    public void SetUp() => converter = new DoubleNegativeToVisibilityConverter();

    [TestCase(-0.0001)]
    [TestCase(-1.0)]
    [TestCase(-1e9)]
    public void Convert_NegativeDouble_ReturnsCollapsed(double input) {
        Assert.That(converter.Convert(input, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase(0.0)]
    [TestCase(0.0001)]
    [TestCase(1.0)]
    [TestCase(1e9)]
    public void Convert_NonNegativeDouble_ReturnsVisible(double input) {
        Assert.That(converter.Convert(input, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_NegativeDecimal_ReturnsCollapsed() {
        Assert.That(converter.Convert(-1m, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_NegativeFloat_ReturnsCollapsed() {
        Assert.That(converter.Convert(-1f, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_Null_ReturnsCollapsed() {
        Assert.That(converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_UnsupportedType_Throws() {
        Assert.That(
            () => converter.Convert("not a number", typeof(Visibility), null, CultureInfo.InvariantCulture),
            Throws.ArgumentException);
    }

    [Test]
    public void ConvertBack_Throws() {
        Assert.That(
            () => converter.ConvertBack(Visibility.Visible, typeof(double), null, CultureInfo.InvariantCulture),
            Throws.TypeOf<NotImplementedException>());
    }
}
