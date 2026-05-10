using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class EmptyCollectionToVisibilityConverterTests {
    private EmptyCollectionToVisibilityConverter converter;

    [SetUp]
    public void SetUp() => converter = new EmptyCollectionToVisibilityConverter();

    [Test]
    public void Convert_EmptyList_ReturnsCollapsed() {
        Assert.That(converter.Convert(new List<int>(), typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_NonEmptyList_ReturnsVisible() {
        Assert.That(converter.Convert(new List<int> { 1 }, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_EmptyArray_ReturnsCollapsed() {
        Assert.That(converter.Convert(new int[0], typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_Null_ReturnsCollapsed() {
        Assert.That(converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture),
            Is.EqualTo(Visibility.Collapsed));
    }
}
