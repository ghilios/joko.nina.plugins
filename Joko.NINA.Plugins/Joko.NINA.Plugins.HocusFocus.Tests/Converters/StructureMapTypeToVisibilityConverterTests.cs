using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class ShowStructureMapToVisibilityConverterTests {
    private ShowStructureMapToVisibilityConverter converter;

    [SetUp]
    public void SetUp() => converter = new ShowStructureMapToVisibilityConverter();

    [Test]
    public void Convert_DebugTrueAndNonNoneEnum_ReturnsVisible() {
        ShowStructureMapEnum? nonNone = null;
        foreach (var v in System.Enum.GetValues(typeof(ShowStructureMapEnum))) {
            if ((ShowStructureMapEnum)v != ShowStructureMapEnum.None) {
                nonNone = (ShowStructureMapEnum)v;
                break;
            }
        }
        Assert.That(nonNone, Is.Not.Null);
        var result = converter.Convert(new object[] { true, nonNone.Value }, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_DebugFalse_ReturnsCollapsed() {
        var result = converter.Convert(new object[] { false, ShowStructureMapEnum.None }, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_NoneEnum_ReturnsCollapsed() {
        var result = converter.Convert(new object[] { true, ShowStructureMapEnum.None }, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_WrongValueCount_Throws() =>
        Assert.That(() => converter.Convert(new object[] { true }, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Throws.ArgumentException);
}
