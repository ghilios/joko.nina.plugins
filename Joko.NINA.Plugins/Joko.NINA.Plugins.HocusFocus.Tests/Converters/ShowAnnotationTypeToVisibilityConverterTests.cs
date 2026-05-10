using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class ShowAnnotationTypeToVisibilityConverterTests {
    private ShowAnnotationTypeToVisibilityConverter converter;

    [SetUp]
    public void SetUp() => converter = new ShowAnnotationTypeToVisibilityConverter();

    [Test]
    public void Convert_None_ReturnsCollapsed() =>
        Assert.That(converter.Convert(ShowAnnotationTypeEnum.None, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));

    [Test]
    public void Convert_NonNoneEnumValue_ReturnsVisible() {
        // Pick any enum value that is NOT None. Using IDs is safer than guessing names.
        ShowAnnotationTypeEnum? nonNone = null;
        foreach (var v in System.Enum.GetValues(typeof(ShowAnnotationTypeEnum))) {
            if ((ShowAnnotationTypeEnum)v != ShowAnnotationTypeEnum.None) {
                nonNone = (ShowAnnotationTypeEnum)v;
                break;
            }
        }
        Assert.That(nonNone, Is.Not.Null, "Test assumes ShowAnnotationTypeEnum has at least one value besides None");
        Assert.That(converter.Convert(nonNone.Value, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_WrongType_ReturnsCollapsed() =>
        Assert.That(converter.Convert("string", typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
}
