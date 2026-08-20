using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class AlertColorConverterTests {

    private static Color Hex(string rgb) => Color.FromRgb(
        System.Convert.ToByte(rgb.Substring(0, 2), 16),
        System.Convert.ToByte(rgb.Substring(2, 2), 16),
        System.Convert.ToByte(rgb.Substring(4, 2), 16));

    [Test]
    public void BadgeText_OnNinaDefaultErrorFill_IsWhite() {
        var converter = new BadgeTextColorConverter();
        var result = converter.Convert(Hex("700000"), typeof(Color), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Colors.White));
    }

    // WPF pushes DependencyProperty.UnsetValue on the first pass of a resource-level binding. Returning
    // Binding.DoNothing would leave SolidColorBrush.Color at Transparent, i.e. invisible text; white is the
    // answer on all 18 built-in schemas anyway.
    [Test]
    public void BadgeText_OnNonColourInput_FallsBackToWhite() {
        var converter = new BadgeTextColorConverter();
        var result = converter.Convert(System.Windows.DependencyProperty.UnsetValue, typeof(Color), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Colors.White));
    }

    [Test]
    public void Accent_LightensTheErrorFillAgainstADarkBackground() {
        var converter = new AccessibleAccentColorConverter();
        var result = (Color)converter.Convert(
            new object[] { Hex("700000"), Hex("02010A") }, typeof(Color), null, CultureInfo.InvariantCulture);
        Assert.That(ContrastMath.ContrastRatio(result, Hex("02010A")),
            Is.GreaterThanOrEqualTo(ContrastMath.MinContrastRatio));
    }

    [Test]
    public void Accent_WithoutAUsableBackground_DegradesToTheRawAlertColour() {
        var converter = new AccessibleAccentColorConverter();
        var result = converter.Convert(
            new object[] { Hex("700000"), System.Windows.DependencyProperty.UnsetValue },
            typeof(Color), null, CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo(Hex("700000")), "no background yet means today's behaviour, not a crash");
    }

    [Test]
    public void Accent_WithNoUsableValuesAtAll_FallsBackToWhite() {
        var converter = new AccessibleAccentColorConverter();
        Assert.Multiple(() => {
            Assert.That(converter.Convert(null, typeof(Color), null, CultureInfo.InvariantCulture), Is.EqualTo(Colors.White));
            Assert.That(converter.Convert(new object[0], typeof(Color), null, CultureInfo.InvariantCulture), Is.EqualTo(Colors.White));
        });
    }
}
