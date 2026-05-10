using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class InverseDoubleNegativeToVisibilityConverterTests {
    private InverseDoubleNegativeToVisibilityConverter converter;

    [SetUp]
    public void SetUp() => converter = new InverseDoubleNegativeToVisibilityConverter();

    [Test]
    public void Convert_NegativeDouble_ReturnsVisible() {
        Assert.That(converter.Convert(-1.0, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_ZeroDouble_ReturnsCollapsed() {
        Assert.That(converter.Convert(0.0, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_PositiveDouble_ReturnsCollapsed() {
        Assert.That(converter.Convert(1.0, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_Null_ReturnsCollapsed() {
        Assert.That(converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }
}
