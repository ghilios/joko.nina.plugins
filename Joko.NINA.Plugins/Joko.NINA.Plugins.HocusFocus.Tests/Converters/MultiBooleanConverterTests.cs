using System.Globalization;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class MultiBooleanToVisibilityCollapsedConverterTests {
    private MultiBooleanToVisibilityCollapsedConverter converter;

    [SetUp]
    public void SetUp() => converter = new MultiBooleanToVisibilityCollapsedConverter();

    [Test]
    public void Convert_AllTrue_ReturnsVisible() {
        var result = converter.Convert(new object[] { true, true, true }, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void Convert_AnyFalse_ReturnsCollapsed() {
        var result = converter.Convert(new object[] { true, false, true }, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_AnyNull_ReturnsCollapsed() {
        var result = converter.Convert(new object[] { true, null, true }, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_EmptyArray_ReturnsCollapsed() {
        var result = converter.Convert(new object[0], typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_NonBoolValuesAreIgnored_AllTrue() {
        // Per implementation, only `false` booleans collapse; other types pass through.
        var result = converter.Convert(new object[] { true, "string", 42 }, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Visible));
    }
}

[TestFixture]
public class MultiBooleanToZeroConverterTests {
    private MultiBooleanToZeroConverter converter;

    [SetUp]
    public void SetUp() => converter = new MultiBooleanToZeroConverter();

    [Test]
    public void Convert_AllTrue_ReturnsFirstValueAsTrueResult() {
        // values[0] is the "trueValue" double; remaining values are booleans.
        var result = converter.Convert(new object[] { 5.5, true, true }, typeof(double), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(5.5));
    }

    [Test]
    public void Convert_AnyFalse_ReturnsZero() {
        var result = converter.Convert(new object[] { 5.5, true, false }, typeof(double), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(0.0));
    }

    [Test]
    public void Convert_SingleTrueValueOnly_ReturnsZero() {
        // Length <=1 short-circuits to 0.
        var result = converter.Convert(new object[] { 5.5 }, typeof(double), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(0.0));
    }

    [Test]
    public void Convert_AnyNull_ReturnsZero() {
        var result = converter.Convert(new object[] { 5.5, true, null }, typeof(double), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(0.0));
    }
}
