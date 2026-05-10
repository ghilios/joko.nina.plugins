using System.Globalization;
using System.Windows;
using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class AutoFocusFittingToVisibilityConverterTests {
    private AutoFocusFittingToVisibilityConverter converter;

    [SetUp]
    public void SetUp() => converter = new AutoFocusFittingToVisibilityConverter();

    [Test]
    public void Convert_NullSource_ReturnsCollapsed() {
        var result = converter.Convert(
            new object[] { null, AFMethodEnum.STARHFR, AFCurveFittingEnum.HYPERBOLIC, null, null, null, null },
            typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_NullMethod_ReturnsCollapsed() {
        var result = converter.Convert(
            new object[] { "HyperbolicFitting", null, AFCurveFittingEnum.HYPERBOLIC, null, null, null, null },
            typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_NullFitting_ReturnsCollapsed() {
        var result = converter.Convert(
            new object[] { "HyperbolicFitting", AFMethodEnum.STARHFR, null, null, null, null, null },
            typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_HyperbolicSourceWithoutFittingObject_ReturnsCollapsed() {
        // source = "HyperbolicFitting", fitting algo = HYPERBOLIC, but no HyperbolicFitting object
        var result = converter.Convert(
            new object[] { "HyperbolicFitting", AFMethodEnum.STARHFR, AFCurveFittingEnum.HYPERBOLIC, null, null, null, null },
            typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_HyperbolicSourceWithUnrelatedFittingMode_ReturnsCollapsed() {
        // Source asks for HyperbolicFitting but fitting mode is PARABOLIC -> not visible
        var result = converter.Convert(
            new object[] { "HyperbolicFitting", AFMethodEnum.STARHFR, AFCurveFittingEnum.PARABOLIC, null, null, null, null },
            typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Convert_ContrastDetectionWithoutGaussian_ReturnsCollapsed() {
        var result = converter.Convert(
            new object[] { "GaussianFitting", AFMethodEnum.CONTRASTDETECTION, AFCurveFittingEnum.HYPERBOLIC, null, null, null, null },
            typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Visibility.Collapsed));
    }
}
