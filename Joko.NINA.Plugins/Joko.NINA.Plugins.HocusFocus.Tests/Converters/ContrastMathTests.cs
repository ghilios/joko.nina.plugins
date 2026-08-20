using System;
using NINA.Core.Utility.ColorSchema;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class ContrastMathTests {

    private static Color Hex(string rgb) => Color.FromRgb(
        System.Convert.ToByte(rgb.Substring(0, 2), 16),
        System.Convert.ToByte(rgb.Substring(2, 2), 16),
        System.Convert.ToByte(rgb.Substring(4, 2), 16));

    [Test]
    public void ContrastRatio_BlackOnWhite_Is21() {
        Assert.That(ContrastMath.ContrastRatio(Colors.Black, Colors.White), Is.EqualTo(21.0).Within(1e-9));
    }

    [Test]
    public void ContrastRatio_IsSymmetric() {
        var a = Hex("700000");
        var b = Hex("02010A");
        Assert.That(ContrastMath.ContrastRatio(a, b), Is.EqualTo(ContrastMath.ContrastRatio(b, a)).Within(1e-12));
    }

    [Test]
    public void RelativeLuminance_MatchesWcagReferenceValues() {
        Assert.Multiple(() => {
            Assert.That(ContrastMath.RelativeLuminance(Colors.White), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(ContrastMath.RelativeLuminance(Colors.Black), Is.EqualTo(0.0).Within(1e-9));
            // NINA's default error fill; hand-computed from the WCAG formula.
            Assert.That(ContrastMath.RelativeLuminance(Hex("700000")), Is.EqualTo(0.034452).Within(1e-5));
        });
    }

    [Test]
    public void BestContrastText_PicksWhiteOnNinaDefaultErrorFill() {
        Assert.That(ContrastMath.BestContrastText(Hex("700000")), Is.EqualTo(Colors.White));
    }

    [Test]
    public void BestContrastText_PicksBlackOnALightFill() {
        Assert.That(ContrastMath.BestContrastText(Hex("FFD54F")), Is.EqualTo(Colors.Black));
    }

    [Test]
    public void AccessibleAccent_LeavesAnAlreadyReadableColorUntouched() {
        // #700000 on white is 12.4:1 — light themes must stay byte-identical to today.
        var alert = Hex("700000");
        Assert.That(ContrastMath.AccessibleAccent(alert, Colors.White), Is.EqualTo(alert));
    }

    [Test]
    public void AccessibleAccent_PreservesHueAndAlpha() {
        var alert = Color.FromArgb(0xCC, 0x70, 0x00, 0x00);
        var accent = ContrastMath.AccessibleAccent(alert, Hex("02010A"));
        Assert.Multiple(() => {
            Assert.That(accent.A, Is.EqualTo(0xCC), "alpha must be carried through");
            Assert.That(accent.R, Is.GreaterThan(accent.G), "must still read as red");
            Assert.That(accent.G, Is.EqualTo(accent.B), "pure-red hue has equal green and blue");
        });
    }

    [Test]
    public void AccessibleAccent_DarkensAnAchromaticColourAgainstALightBackground() {
        // Forces the goLighter == false branch: white background means only the dark pole can reach the
        // target. No built-in NINA schema exercises this path, so without this test an inverted comparison
        // in the bisection's else-branch would go unnoticed.
        var accent = ContrastMath.AccessibleAccent(Hex("D0D0D0"), Colors.White);
        Assert.Multiple(() => {
            Assert.That(ContrastMath.ContrastRatio(accent, Colors.White),
                Is.GreaterThanOrEqualTo(ContrastMath.MinContrastRatio));
            Assert.That(ContrastMath.RelativeLuminance(accent),
                Is.LessThan(ContrastMath.RelativeLuminance(Hex("D0D0D0"))), "must have moved darker");
        });
    }

    [Test]
    public void AccessibleAccent_DarkeningPreservesHue() {
        var alert = Hex("FFD54F");   // amber, too light to read on white
        var accent = ContrastMath.AccessibleAccent(alert, Colors.White);
        Assert.Multiple(() => {
            Assert.That(ContrastMath.ContrastRatio(accent, Colors.White),
                Is.GreaterThanOrEqualTo(ContrastMath.MinContrastRatio));
            Assert.That(ContrastMath.RelativeLuminance(accent), Is.LessThan(ContrastMath.RelativeLuminance(alert)));
            Assert.That(accent.R, Is.GreaterThan(accent.G), "still amber");
            Assert.That(accent.G, Is.GreaterThan(accent.B), "still amber");
        });
    }

    // The regression that would have caught the original bug: every built-in NINA schema, both roles.
    [Test]
    public void EveryBuiltInNinaSchema_ClearsWcagAaForBadgeTextAndAccent() {
        var schemas = ColorSchemas.ReadColorSchemas().Items;
        Assert.That(schemas, Is.Not.Empty, "NINA must expose its built-in schemas");
        Assert.Multiple(() => {
            foreach (var s in schemas) {
                var bg = s.BackgroundColor;
                foreach (var (role, fill) in new[] {
                    ("error", s.NotificationErrorColor),
                    ("warning", s.NotificationWarningColor)
                }) {
                    var badgeText = ContrastMath.BestContrastText(fill);
                    Assert.That(ContrastMath.ContrastRatio(badgeText, fill),
                        Is.GreaterThanOrEqualTo(ContrastMath.MinContrastRatio),
                        $"{s.Name}: {role} badge text on fill");

                    var accent = ContrastMath.AccessibleAccent(fill, bg);
                    Assert.That(ContrastMath.ContrastRatio(accent, bg),
                        Is.GreaterThanOrEqualTo(ContrastMath.MinContrastRatio),
                        $"{s.Name}: {role} accent on page background");
                }
            }
        });
    }
}
