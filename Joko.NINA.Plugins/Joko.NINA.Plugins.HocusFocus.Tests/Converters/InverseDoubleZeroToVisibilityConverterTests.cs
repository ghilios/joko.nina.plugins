using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class InverseDoubleZeroToVisibilityConverterTests {
    private InverseDoubleZeroToVisibilityConverter converter;

    [SetUp]
    public void SetUp() => converter = new InverseDoubleZeroToVisibilityConverter();

    [Test]
    public void Convert_ZeroDouble_ReturnsVisible() {
        Assert.That(converter.Convert(0.0, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_NonZeroDouble_ReturnsCollapsed() {
        Assert.That(converter.Convert(1.0, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_ZeroInt_ReturnsVisible() {
        Assert.That(converter.Convert(0, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_NonZeroInt_ReturnsCollapsed() {
        Assert.That(converter.Convert(7, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_Null_ReturnsVisible() {
        // Null is treated as "zero/empty" -> Visible (this is the inverse zero converter)
        Assert.That(converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }
}
