using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class EnumStaticDescriptionValueConverterTests {
    private EnumStaticDescriptionValueConverter converter;

    [SetUp]
    public void SetUp() => converter = new EnumStaticDescriptionValueConverter();

    private enum SampleEnum {

        [System.ComponentModel.Description("Pretty Apple")]
        Apple,

        [System.ComponentModel.Description("Pretty Banana")]
        Banana,

        Cherry,
    }

    [Test]
    public void Convert_DescribedEnumValue_ReturnsDescription() =>
        Assert.That(converter.Convert(SampleEnum.Apple, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo("Pretty Apple"));

    [Test]
    public void Convert_UndescribedEnumValue_ReturnsValueName() =>
        Assert.That(converter.Convert(SampleEnum.Cherry, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo("Cherry"));

    [Test]
    public void Convert_NullValue_ReturnsEmptyString() =>
        Assert.That(converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture),
            Is.EqualTo(string.Empty));

    [Test]
    public void Convert_NonStringTarget_Throws() =>
        Assert.That(() => converter.Convert(SampleEnum.Apple, typeof(int), null, CultureInfo.InvariantCulture),
            Throws.ArgumentException);
}
