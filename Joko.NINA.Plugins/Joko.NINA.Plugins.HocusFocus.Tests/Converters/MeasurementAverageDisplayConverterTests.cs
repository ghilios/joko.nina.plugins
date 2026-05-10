using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class MeasurementAverageDisplayConverterTests {
    private MeasurementAverageDisplayConverter converter;

    [SetUp]
    public void SetUp() => converter = new MeasurementAverageDisplayConverter();

    [Test]
    public void Convert_Median_ReturnsHFRMAD() =>
        Assert.That(converter.Convert(MeasurementAverageEnum.Median, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo("HFR MAD"));

    // The non-Median branch routes through Loc.Instance["LblHFRStDev"], which depends on
    // NINA's localization being initialized. We don't assert the exact returned string;
    // we just assert that it does not throw and returns a non-null string.
    [Test]
    public void Convert_NonMedian_ReturnsNonNullString() {
        var nonMedian = MeasurementAverageEnum.MeanOutliers;
        var result = converter.Convert(nonMedian, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
    }

    [Test]
    public void Convert_NonStringTarget_Throws() =>
        Assert.That(() => converter.Convert(MeasurementAverageEnum.Median, typeof(int), null, CultureInfo.InvariantCulture),
            Throws.ArgumentException);
}
