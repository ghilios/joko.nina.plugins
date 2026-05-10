using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class IntNegativeToVisibilityConverterTests {
    private IntNegativeToVisibilityConverter converter;

    [SetUp]
    public void SetUp() => converter = new IntNegativeToVisibilityConverter();

    [TestCase(-1)]
    [TestCase(-100)]
    public void Convert_NegativeInt_ReturnsCollapsed(int input) {
        Assert.That(converter.Convert(input, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(100)]
    public void Convert_NonNegativeInt_ReturnsVisible(int input) {
        Assert.That(converter.Convert(input, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_Null_ReturnsCollapsed() {
        Assert.That(converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }
}
