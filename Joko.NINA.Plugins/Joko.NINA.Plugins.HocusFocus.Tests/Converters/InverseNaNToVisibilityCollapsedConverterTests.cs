using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class InverseNaNToVisibilityCollapsedConverterTests {
    private InverseNaNToVisibilityCollapsedConverter converter;

    [SetUp]
    public void SetUp() => converter = new InverseNaNToVisibilityCollapsedConverter();

    [Test]
    public void Convert_NaN_ReturnsVisible() {
        Assert.That(converter.Convert(double.NaN, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_FiniteDouble_ReturnsCollapsed() {
        Assert.That(converter.Convert(3.14, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_Infinity_ReturnsCollapsed() {
        Assert.That(converter.Convert(double.PositiveInfinity, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }
}
