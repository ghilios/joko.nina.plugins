using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class DoubleZeroToVisibilityConverterTests {
    private DoubleZeroToVisibilityConverter converter;

    [SetUp]
    public void SetUp() => converter = new DoubleZeroToVisibilityConverter();

    [TestCase(0.0)]
    [TestCase(0.000001)]
    [TestCase(-0.000001)]
    public void Convert_NearZeroDouble_ReturnsCollapsed(double input) {
        Assert.That(converter.Convert(input, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase(1.0)]
    [TestCase(-1.0)]
    [TestCase(0.001)]
    public void Convert_NonZeroDouble_ReturnsVisible(double input) {
        Assert.That(converter.Convert(input, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_ZeroDecimal_ReturnsCollapsed() {
        Assert.That(converter.Convert(0m, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_ZeroFloat_ReturnsCollapsed() {
        Assert.That(converter.Convert(0f, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_Null_ReturnsCollapsed() {
        Assert.That(converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }
}
