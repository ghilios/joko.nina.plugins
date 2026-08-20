# Tilt Calibration UX Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix three reported problems with the Tilt Adapter Calibration wizard — alert text that is unreadable on dark NINA themes, a step title that claims "Inward" while the device moves `+N`, and a hand-entered calibration that silently and permanently disables Automatic Adjustment.

**Architecture:** Part 1 adds a small pure contrast library plus two WPF converters, exposes four derived brushes in a new merged `ResourceDictionary`, and re-points 27 XAML sites at them. Part 2 is a wording-only change to two files. Part 3 adds a three-command in-pane confirmation to `TiltAdapterWizardVM` that lets the user deliberately re-arm the two existing automation markers, plus honest messaging everywhere those markers get cleared.

**Tech Stack:** C# / .NET 8.0-windows, WPF, NUnit 4.4.0 + NSubstitute, NINA plugin (MEF). Spec: `docs/tilt-calibration-ux-feedback-design.md`.

---

## Background you need before starting

**Read `docs/tilt-calibration-ux-feedback-design.md` first.** It contains the measured contrast numbers this plan implements against.

Key facts an engineer new to this codebase will otherwise get wrong:

1. **NINA's `NotificationErrorBrush` / `NotificationWarningBrush` are FILL colors, not text colors.** 13 of NINA's 18 built-in schemas use the near-black pair `#FF700000` / `#FF5E330B`, and 15 of the 18 render error text below 4.5:1 today. NINA only ever uses them as `Background`. This plugin used them as `Foreground`, which is the bug.
2. **`NotificationErrorTextBrush` is not a safe fix either.** On the "Dark" schema it is `#FF02010A` — near-black text on near-black-red fill, 1.67:1. That is why this plan computes the badge text color instead of reading it.
3. **`WizardStep.AllInward` keeps its enum name.** It is persisted in saved replay runs. Only user-facing strings change.
4. **The wizard's motion is correct.** Do not "fix" any sign, axis, or move. Part 2 is wording only.
5. **Test command** (there is no `dotnet` in WSL; use the Windows host binary):
   ```bash
   dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
   ```
   Allow up to 600 s. A single flaky test, `SendAsync_WritesOnABackgroundThread`, is known-unrelated — if it is the only failure, re-run it alone to confirm and move on.
6. **Building deploys the plugin** into NINA's plugin folder via a PostBuild `xcopy`. Close NINA before running tests.
7. **Commit identity** — every commit in this plan must use:
   ```bash
   GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
     git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
   ```
8. **Branch:** `ghilios/tilt-calibration-ux-feedback` (already created, spec already committed). Never push to `develop`.

**Paths are relative to the repo root** `/home/ghilios/src/hocus-focus`. The plugin lives at `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/`, abbreviated **`PLUGIN/`** below. Tests live at `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/`, abbreviated **`TESTS/`**.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `PLUGIN/Converters/ContrastMath.cs` | Pure WCAG contrast math + the two color decisions. No WPF binding concerns. |
| `PLUGIN/Converters/BadgeTextColorConverter.cs` | `IValueConverter`: badge fill color → black or white text. |
| `PLUGIN/Converters/AccessibleAccentColorConverter.cs` | `IMultiValueConverter`: (alert color, page background) → readable accent. |
| `PLUGIN/Resources/AlertBrushes.xaml` | The four derived brushes + two badge `Border` styles. The single place alert chrome is defined. |
| `TESTS/Converters/ContrastMathTests.cs` | Contrast math + the all-schemas regression sweep. |
| `TESTS/Converters/AlertColorConverterTests.cs` | The two converters' binding-edge behavior. |

**Modified:**

| File | Change |
|---|---|
| `PLUGIN/Resources/OptionsDataTemplates.xaml` | Merge `AlertBrushes.xaml`; 2 accent swaps. |
| `PLUGIN/TiltAdapterWizard/DataTemplates.xaml` | 15 alert sites; the new Part 3 banner. |
| `PLUGIN/AutoFocus/DataTemplates.xaml` | 4 alert sites + 2 existing badge texts re-pointed. |
| `PLUGIN/StarDetection/Optimization/DataTemplates.xaml` | 5 alert sites. |
| `PLUGIN/CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | Merge `AlertBrushes.xaml`; 1 alert site + 3 existing badge texts re-pointed. |
| `PLUGIN/TiltAdapterWizard/TiltAdapterWizardVM.cs` | Part 2 titles; Part 3 commands, banner state, notification, `ClearCalibration` fix. |
| `PLUGIN/TiltAdapterDevices/AsgEat/EatWizardMapping.cs` | Part 2 move description. |
| `PLUGIN/AutoFocus/InspectorVM.cs` | Part 3 remediation text branch. |
| `PLUGIN/CameraSimulator/TiltAdapter/SimulatedTiltAdapterVM.cs` | Part 3 marker hygiene. |
| `TESTS/TiltAdapterWizard/TiltAdapterWizardVMTests.cs` | Part 2 title cases + naming guard; Part 3 command tests. |
| `TESTS/TiltAdapterDevices/AsgEat/EatWizardMappingTests.cs` | Part 2 description assertion. |
| `TESTS/AutoFocus/` (inspector tests) | Part 3 remediation-text test. |
| `docs/tilt-adapter-ui-review-design.md` | Stale "All Screws Inward" reference. |
| `.claude/docs/tilt-domain.md` | Record the alert-brush rule so it is not re-broken. |

---

## Task 1: Contrast math

**Files:**
- Create: `PLUGIN/Converters/ContrastMath.cs`
- Test: `TESTS/Converters/ContrastMathTests.cs`

- [ ] **Step 1: Write the failing test**

Create `TESTS/Converters/ContrastMathTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test and verify it fails**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~ContrastMathTests"
```
Expected: build FAILS with `CS0103`/`CS0246` — the name `ContrastMath` does not exist.

- [ ] **Step 3: Write the implementation**

Create `PLUGIN/Converters/ContrastMath.cs`:

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    /// <summary>
    /// WCAG 2.x contrast math, and the two color decisions the alert brushes are built from.
    ///
    /// WHY THIS EXISTS: NINA's ColorSchema exposes NotificationErrorColor / NotificationWarningColor as FILL
    /// colors (it only ever uses them as a Background), and 13 of its 18 built-in schemas set them to near-black
    /// #FF700000 / #FF5E330B. Used as a Foreground — which this plugin did in 27 places — they land at 1.06:1
    /// against a dark page background. Its paired NotificationErrorTextColor is not a way out either: the "Dark"
    /// schema sets it to #FF02010A, i.e. near-black text on near-black-red fill, 1.67:1. So both alert text
    /// colors are COMPUTED here rather than read from the schema.
    ///
    /// Pure and WPF-binding-free on purpose: the all-schemas regression sweep in ContrastMathTests runs directly
    /// against this, with no converter or dispatcher in the way.
    /// </summary>
    internal static class ContrastMath {

        /// <summary>WCAG AA for body text. What the tests assert.</summary>
        internal const double MinContrastRatio = 4.5;

        /// <summary>
        /// What <see cref="AccessibleAccent"/> actually searches for. Deliberately above
        /// <see cref="MinContrastRatio"/>: the search evaluates byte-rounded colors, and landing exactly on 4.5
        /// leaves no headroom for that rounding.
        /// </summary>
        private const double TargetContrastRatio = 4.6;

        private static double Linearize(byte channel) {
            double c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        internal static double RelativeLuminance(Color c) =>
            0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);

        internal static double ContrastRatio(Color a, Color b) {
            double la = RelativeLuminance(a);
            double lb = RelativeLuminance(b);
            double hi = Math.Max(la, lb);
            double lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>Black or white — whichever is more readable ON <paramref name="fill"/>.</summary>
        internal static Color BestContrastText(Color fill) =>
            ContrastRatio(Colors.Black, fill) >= ContrastRatio(Colors.White, fill) ? Colors.Black : Colors.White;

        /// <summary>
        /// <paramref name="alert"/> made readable against <paramref name="pageBackground"/>, keeping its HUE and
        /// SATURATION and moving only HSL lightness.
        ///
        /// Moving in HSL rather than lerping toward white is the point: #700000 lerped toward white only reaches
        /// 4.5:1 at roughly #A96666, a washed-out rose that no longer reads as an error, while raising its HSL
        /// lightness reaches the same ratio at #EF0000 — still unmistakably red. Returned unchanged when it
        /// already passes, so light-background schemas stay byte-identical to their previous appearance.
        /// </summary>
        internal static Color AccessibleAccent(Color alert, Color pageBackground) {
            if (ContrastRatio(alert, pageBackground) >= TargetContrastRatio) {
                return alert;
            }

            RgbToHsl(alert, out double h, out double s, out double l);

            bool lighterReaches = ContrastRatio(FromHsl(h, s, 1.0, alert.A), pageBackground) >= TargetContrastRatio;
            bool darkerReaches = ContrastRatio(FromHsl(h, s, 0.0, alert.A), pageBackground) >= TargetContrastRatio;

            bool goLighter;
            if (lighterReaches && darkerReaches) {
                // Both poles work, so head for the one the background is furthest from.
                goLighter = ContrastRatio(Colors.White, pageBackground) >= ContrastRatio(Colors.Black, pageBackground);
            } else if (lighterReaches) {
                goLighter = true;
            } else if (darkerReaches) {
                goLighter = false;
            } else {
                // A fully saturated hue cannot reach the target against this background (only possible for an
                // exotic custom schema). Legibility beats hue: fall back to plain black or white.
                var fallback = BestContrastText(pageBackground);
                return Color.FromArgb(alert.A, fallback.R, fallback.G, fallback.B);
            }

            // Contrast is monotonic in lightness once we are moving away from the background, so bisect.
            double lo = goLighter ? l : 0.0;
            double hi = goLighter ? 1.0 : l;
            for (int i = 0; i < 24; i++) {
                double mid = (lo + hi) / 2.0;
                bool reaches = ContrastRatio(FromHsl(h, s, mid, alert.A), pageBackground) >= TargetContrastRatio;
                if (goLighter) {
                    if (reaches) { hi = mid; } else { lo = mid; }
                } else {
                    if (reaches) { lo = mid; } else { hi = mid; }
                }
            }
            return FromHsl(h, s, goLighter ? hi : lo, alert.A);
        }

        private static void RgbToHsl(Color c, out double h, out double s, out double l) {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            double delta = max - min;
            if (delta <= double.Epsilon) {
                h = 0.0;
                s = 0.0;
                return;
            }
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);
            if (max == r) {
                h = ((g - b) / delta + (g < b ? 6.0 : 0.0)) / 6.0;
            } else if (max == g) {
                h = ((b - r) / delta + 2.0) / 6.0;
            } else {
                h = ((r - g) / delta + 4.0) / 6.0;
            }
        }

        private static Color FromHsl(double h, double s, double l, byte alpha) {
            l = Math.Max(0.0, Math.Min(1.0, l));
            if (s <= double.Epsilon) {
                byte v = (byte)Math.Round(l * 255.0);
                return Color.FromArgb(alpha, v, v, v);
            }
            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;
            return Color.FromArgb(
                alpha,
                (byte)Math.Round(HueToChannel(p, q, h + 1.0 / 3.0) * 255.0),
                (byte)Math.Round(HueToChannel(p, q, h) * 255.0),
                (byte)Math.Round(HueToChannel(p, q, h - 1.0 / 3.0) * 255.0));
        }

        private static double HueToChannel(double p, double q, double t) {
            if (t < 0.0) { t += 1.0; }
            if (t > 1.0) { t -= 1.0; }
            if (t < 1.0 / 6.0) { return p + (q - p) * 6.0 * t; }
            if (t < 1.0 / 2.0) { return q; }
            if (t < 2.0 / 3.0) { return p + (q - p) * (2.0 / 3.0 - t) * 6.0; }
            return p;
        }
    }
}
```

- [ ] **Step 4: Run the test and verify it passes**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~ContrastMathTests"
```
Expected: PASS, 8 tests. If `EveryBuiltInNinaSchema_...` fails, the failure message names the schema and role — do not lower `MinContrastRatio` to make it pass; fix `AccessibleAccent`.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Converters/ContrastMath.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Converters/ContrastMathTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Add WCAG contrast math for accessible alert colors"
```

---

## Task 2: The two color converters

**Files:**
- Create: `PLUGIN/Converters/BadgeTextColorConverter.cs`
- Create: `PLUGIN/Converters/AccessibleAccentColorConverter.cs`
- Test: `TESTS/Converters/AlertColorConverterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `TESTS/Converters/AlertColorConverterTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test and verify it fails**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~AlertColorConverterTests"
```
Expected: build FAILS — `BadgeTextColorConverter` / `AccessibleAccentColorConverter` do not exist.

- [ ] **Step 3: Write the implementation**

Create `PLUGIN/Converters/BadgeTextColorConverter.cs`:

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    /// <summary>
    /// Text color for a FILLED alert badge: black or white, whichever is readable on the badge's own fill.
    /// Deliberately not NINA's NotificationErrorTextColor — see the note on <see cref="ContrastMath"/>.
    /// </summary>
    public class BadgeTextColorConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            // The first pass of a resource-level binding arrives as DependencyProperty.UnsetValue. White rather
            // than Binding.DoNothing: DoNothing would leave SolidColorBrush.Color at Transparent (invisible
            // text), and white is the computed answer on all 18 built-in schemas.
            return value is Color fill ? ContrastMath.BestContrastText(fill) : Colors.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
```

Create `PLUGIN/Converters/AccessibleAccentColorConverter.cs`:

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Converters {

    /// <summary>
    /// Foreground for INLINE alert text drawn straight on the page: the schema's alert color, hue preserved,
    /// lightened or darkened only as far as WCAG AA needs against the page background.
    /// values[0] = alert color, values[1] = page background color.
    /// </summary>
    public class AccessibleAccentColorConverter : IMultiValueConverter {

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values == null || values.Length < 1 || !(values[0] is Color alert)) {
                return Colors.White;
            }
            // Background not resolved yet: degrade to the raw alert color (exactly today's behaviour) rather
            // than guessing. The real value arrives on the next pass.
            if (values.Length < 2 || !(values[1] is Color background)) {
                return alert;
            }
            return ContrastMath.AccessibleAccent(alert, background);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
```

- [ ] **Step 4: Run the test and verify it passes**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~AlertColorConverterTests"
```
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Converters/BadgeTextColorConverter.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Converters/AccessibleAccentColorConverter.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Converters/AlertColorConverterTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Add badge-text and accessible-accent colour converters"
```

---

## Task 3: The alert brush dictionary

**Files:**
- Create: `PLUGIN/Resources/AlertBrushes.xaml`
- Modify: `PLUGIN/Resources/OptionsDataTemplates.xaml` (add `MergedDictionaries`)
- Modify: `PLUGIN/CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` (add `MergedDictionaries`)

WPF resource resolution has no dedicated unit test, but it is **not** unguarded. `AlertBrushes.xaml` references
`NotificationErrorBrush`, `NotificationWarningBrush` and `BackgroundBrush`, which live only in NINA's
`Application.Resources`. A `StaticResource` that resolves nowhere throws at dictionary-load time — it does not
fail the build, it breaks a whole panel at runtime. Two things cover that:

- `Tests/CameraSimulator/XamlResourceResolutionTests.cs` scans the plugin's XAML for unresolvable `StaticResource`
  keys and picks up this new file automatically. It exists because of a real past incident: `NotificationSuccessBrush`,
  a key NINA never defines, silently broke the camera setup dialog, because `StaticResource` throws at parse time
  and `WindowService.Show` swallowed the exception. **Run it in Step 4.**
- WPF's `StaticResourceExtension` falls back to `FindResourceInAppOrSystem` (i.e. `Application.Current.Resources`)
  when a key is not in the ambient chain, and `NINA.Plugin/PluginLoader.cs:477` composes plugin dictionaries only
  after `App.xaml` has merged NINA's brushes — so the keys are present when this file parses.

Note this is a *different* mechanism from the plugin's existing alert usages, which all sit inside a `Setter.Value`
or a `DataTemplate` visual tree and are resolved lazily at apply time via `FindResource`. Do not reason from those
as precedent; they are deferred content, these brushes are eager.

- [ ] **Step 1: Create the dictionary**

Create `PLUGIN/Resources/AlertBrushes.xaml`:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:hfconverters="clr-namespace:NINA.Joko.Plugins.HocusFocus.Converters">

    <!--
        Accessible alert chrome. NINA's NotificationError/WarningBrush are FILL colours (it only ever uses them
        as a Background) and are near-black in 13 of its 18 built-in schemas, so using them as a Foreground
        renders at 1.06-1.9:1 on every dark theme. Its paired NotificationErrorTextBrush is no better — the
        "Dark" schema sets it to #FF02010A, near-black on near-black-red, 1.67:1. Both text colours are
        therefore COMPUTED (see Converters/ContrastMath.cs), which holds >= 4.5:1 on all 18 built-in schemas and
        on custom ones.

        The Color properties are BOUND rather than fixed so a live colour-schema change flows through, the same
        shape NINA uses in NINA.WPF.Base/Resources/StaticResources/Brushes.xaml.

        USAGE:
          HF_Alert*TextBrush   -> text INSIDE a filled badge
          HF_Alert*AccentBrush -> alert text drawn straight on the page background
          HF_Alert*Badge       -> the filled Border itself (BasedOn-able, so a local Style can add triggers)

        When a Border becomes a filled badge, EVERY TextBlock inside it needs the badge text brush — children
        with no explicit Foreground otherwise inherit PrimaryBrush onto the fill.

        Mode=OneWay is stated EXPLICITLY on every binding below, and is not decoration. Both converters throw
        NotSupportedException from ConvertBack, and WPF does NOT catch exceptions thrown out of a converter's
        ConvertBack — unlike a failed Convert, which it logs and leaves the target unset, a throwing ConvertBack
        propagates and can take the application down. These bindings resolve to OneWay implicitly today only
        because SolidColorBrush.ColorProperty is registered through Animatable.RegisterProperty, which carries no
        BindsTwoWayByDefault. Saying OneWay outright makes the guarantee structural instead of incidental, so it
        survives someone re-pointing a converter at a target property whose metadata does default to two-way.
    -->

    <hfconverters:BadgeTextColorConverter x:Key="HF_BadgeTextColorConverter" />
    <hfconverters:AccessibleAccentColorConverter x:Key="HF_AccessibleAccentColorConverter" />

    <SolidColorBrush
        x:Key="HF_AlertErrorTextBrush"
        Color="{Binding Source={StaticResource NotificationErrorBrush},
                        Path=Color,
                        Mode=OneWay,
                        Converter={StaticResource HF_BadgeTextColorConverter}}" />

    <SolidColorBrush
        x:Key="HF_AlertWarningTextBrush"
        Color="{Binding Source={StaticResource NotificationWarningBrush},
                        Path=Color,
                        Mode=OneWay,
                        Converter={StaticResource HF_BadgeTextColorConverter}}" />

    <SolidColorBrush x:Key="HF_AlertErrorAccentBrush">
        <SolidColorBrush.Color>
            <MultiBinding Converter="{StaticResource HF_AccessibleAccentColorConverter}" Mode="OneWay">
                <Binding Path="Color" Source="{StaticResource NotificationErrorBrush}" Mode="OneWay" />
                <Binding Path="Color" Source="{StaticResource BackgroundBrush}" Mode="OneWay" />
            </MultiBinding>
        </SolidColorBrush.Color>
    </SolidColorBrush>

    <SolidColorBrush x:Key="HF_AlertWarningAccentBrush">
        <SolidColorBrush.Color>
            <MultiBinding Converter="{StaticResource HF_AccessibleAccentColorConverter}" Mode="OneWay">
                <Binding Path="Color" Source="{StaticResource NotificationWarningBrush}" Mode="OneWay" />
                <Binding Path="Color" Source="{StaticResource BackgroundBrush}" Mode="OneWay" />
            </MultiBinding>
        </SolidColorBrush.Color>
    </SolidColorBrush>

    <Style x:Key="HF_AlertErrorBadge" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource NotificationErrorBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource NotificationErrorBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="3" />
        <Setter Property="Padding" Value="6,4" />
    </Style>

    <Style x:Key="HF_AlertWarningBadge" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource NotificationWarningBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource NotificationWarningBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="3" />
        <Setter Property="Padding" Value="6,4" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 2: Merge it into `OptionsDataTemplates.xaml`**

`PLUGIN/Resources/OptionsDataTemplates.xaml` currently has no `MergedDictionaries`. Insert one immediately after the root element's closing `>` (after the `mc:Ignorable="d">` line, before the first `<hfconverters:...>` resource).

Find:
```xml
    mc:Ignorable="d">
    <hfconverters:ShowStructureMapToVisibilityConverter x:Key="HF_ShowStructureMapToVisibilityConverter" />
```
Replace with:
```xml
    mc:Ignorable="d">
    <!--  Alert chrome. Merged (not cross-dictionary StaticResource) so the keys resolve at parse time in every
          dictionary that merges this one: AutoFocus, TiltAdapterWizard and StarDetection/Optimization all merge
          OptionsDataTemplates already, so they inherit it for free.  -->
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="AlertBrushes.xaml" />
    </ResourceDictionary.MergedDictionaries>
    <hfconverters:ShowStructureMapToVisibilityConverter x:Key="HF_ShowStructureMapToVisibilityConverter" />
```

- [ ] **Step 3: Merge it into the camera-simulator dictionary**

`PLUGIN/CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` deliberately has no merges and declares its resources locally — its header comment warns that reaching across *separately-exported plugin dictionaries* with `StaticResource` is a load-order race. Merging is not that: merged keys resolve inside this dictionary's own scope at parse time. Merge only `AlertBrushes.xaml`, not the large `OptionsDataTemplates.xaml`.

Find:
```xml
    xmlns:rules="clr-namespace:NINA.Core.Utility.ValidationRules;assembly=NINA.Core">

    <!--  Declared locally rather than reached for across dictionaries: StaticResource resolves at parse time and
```
Replace with:
```xml
    xmlns:rules="clr-namespace:NINA.Core.Utility.ValidationRules;assembly=NINA.Core">

    <!--  MERGED, not reached for: a merged dictionary's keys resolve inside this dictionary's own parse-time
          scope, so it is not the cross-dictionary load-order race the note below is about. Only AlertBrushes is
          merged, not the large OptionsDataTemplates.  -->
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="../../Resources/AlertBrushes.xaml" />
    </ResourceDictionary.MergedDictionaries>

    <!--  Declared locally rather than reached for across dictionaries: StaticResource resolves at parse time and
```

- [ ] **Step 4: Build and verify the XAML compiles**

```bash
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```
Expected: `Build succeeded`, 0 errors. A `MC3074` (unknown type) means the `hfconverters` namespace is wrong; an `MC3000` means malformed XAML. `AlertBrushes.xaml` needs no csproj entry — the WPF SDK auto-includes `**/*.xaml` as `Page`; confirm it really was picked up rather than assuming (`obj/Debug/net8.0-windows7.0/Resources/AlertBrushes.baml` should exist).

Then run the resolution guard and the converter tests:

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~XamlResourceResolutionTests"
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~Converters"
```
Expected: both green. A failure in the first means a key in `AlertBrushes.xaml` resolves nowhere — fix the key, do not suppress the test.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/AlertBrushes.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Add accessible alert brush dictionary and merge it where needed"
```

---

## Task 4: Wizard XAML — filled badges

**Files:**
- Modify: `PLUGIN/TiltAdapterWizard/DataTemplates.xaml`

**Line numbers below are from the pre-edit file.** Each edit shifts later lines, so work **bottom-up** (highest line number first) or re-locate each site by its `Text="{Binding …}"` binding, which is unique.

### 4a. The six single-child alert Borders

These all have the identical shape: a `Border` with `BorderBrush="{StaticResource NotificationErrorBrush}"` and a `<Border.Style>` carrying only the visibility trigger, whose sole child is the alert `TextBlock`.

| Border | Text | Binding |
|---|---|---|
| 2045 | 2058 | `DeviceLinkDroppedWarningText` |
| 2067 | 2080 | `MeasurementConsistencyWarningText` |
| 2221 | 2234 | `DeviceLinkDroppedWarningText` |
| 2244 | 2257 | `MeasurementConsistencyWarningText` |
| 2266 | 2279 | `RebaselineDriftWarningText` |
| 2288 | 2301 | `ConfidenceWarningText` |

- [ ] **Step 1: Apply the badge transformation to all six**

For each, make exactly two changes. Add `BasedOn` to the `<Style TargetType="Border">`:

```xml
<Style TargetType="Border" BasedOn="{StaticResource HF_AlertErrorBadge}">
```

and swap the child `TextBlock`'s foreground:

```xml
<TextBlock
    Foreground="{StaticResource HF_AlertErrorTextBrush}"
    Text="{Binding DeviceLinkDroppedWarningText}"
    TextWrapping="Wrap" />
```

Worked example — the block at 2244–2260 becomes:

```xml
                    <Border
                        Margin="0,5"
                        Padding="5">
                        <Border.Style>
                            <Style TargetType="Border" BasedOn="{StaticResource HF_AlertErrorBadge}">
                                <Setter Property="Visibility" Value="Collapsed" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding HasMeasurementConsistencyWarning}" Value="True">
                                        <Setter Property="Visibility" Value="Visible" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Border.Style>
                        <TextBlock
                            Foreground="{StaticResource HF_AlertErrorTextBrush}"
                            Text="{Binding MeasurementConsistencyWarningText}"
                            TextWrapping="Wrap" />
                    </Border>
```

**Delete the local `BorderBrush` and `BorderThickness` attributes; keep `Margin` and `Padding`.** WPF's
dependency-property precedence puts a local value (rank 3) above a style setter (rank 8), so anything left on the
element pins itself and stops following the style. `Padding="5"` is a genuine override worth keeping — the style
says `6,4` and these blocks should keep their existing spacing. But `BorderThickness="1"` and
`BorderBrush="{StaticResource NotificationErrorBrush}"` are byte-identical to what `HF_AlertErrorBadge` already
sets, so leaving them changes nothing today while silently pinning those six Borders against any future central
change to the badge style — a footgun that would not show up in a diff. Remove them.

**Invariant, and it is counter-intuitive — do not break it.** Local value also outranks *style triggers* (rank 6),
not just plain setters. So an element must **never** carry a local `Visibility` attribute when its `Style` sets
`Visibility` from a `DataTrigger`: the local value wins permanently and the trigger becomes dead code, with no
error and no warning. That is why every badge in this task keeps `Visibility` inside `<Border.Style>` and sets
none on the element, while Tasks 7 and 10 — which apply `Style="{StaticResource HF_Alert*Badge}"` directly and set
`Visibility` locally — are safe only because those shared styles set no `Visibility` at all.

### 4b. Wrap the bare connection warning (line 1875)

`ConnectionWarningText` is a bare `TextBlock` with its own visibility `Style`. Wrapping it means the **`Border`** must own the visibility, or an empty red pill shows whenever the text is blank.

- [ ] **Step 2: Replace the block at 1872–1893**

Find:
```xml
                    <!--  Device connection warning: shown when Run Measurement is visible but devices are not connected  -->
                    <TextBlock
                        Margin="0,0,0,4"
                        Foreground="{StaticResource NotificationErrorBrush}"
                        Text="{Binding ConnectionWarningText}"
                        TextWrapping="Wrap">
                        <TextBlock.Style>
                            <Style BasedOn="{StaticResource StandardTextBlock}" TargetType="TextBlock">
                                <Setter Property="Visibility" Value="Collapsed" />
                                <Style.Triggers>
                                    <MultiDataTrigger>
                                        <MultiDataTrigger.Conditions>
                                            <Condition Binding="{Binding IsOnMeasurementStep}" Value="True" />
                                            <Condition Binding="{Binding IsMeasuring}" Value="False" />
                                            <Condition Binding="{Binding AreDevicesConnected}" Value="False" />
                                        </MultiDataTrigger.Conditions>
                                        <Setter Property="Visibility" Value="Visible" />
                                    </MultiDataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
```

Replace with:
```xml
                    <!--  Device connection warning: shown when Run Measurement is visible but devices are not
                          connected. The BORDER owns the visibility trigger, not the TextBlock — otherwise the
                          filled badge would render as an empty red pill whenever the text is blank.  -->
                    <Border Margin="0,0,0,4">
                        <Border.Style>
                            <Style TargetType="Border" BasedOn="{StaticResource HF_AlertErrorBadge}">
                                <Setter Property="Visibility" Value="Collapsed" />
                                <Style.Triggers>
                                    <MultiDataTrigger>
                                        <MultiDataTrigger.Conditions>
                                            <Condition Binding="{Binding IsOnMeasurementStep}" Value="True" />
                                            <Condition Binding="{Binding IsMeasuring}" Value="False" />
                                            <Condition Binding="{Binding AreDevicesConnected}" Value="False" />
                                        </MultiDataTrigger.Conditions>
                                        <Setter Property="Visibility" Value="Visible" />
                                    </MultiDataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Border.Style>
                        <TextBlock
                            Foreground="{StaticResource HF_AlertErrorTextBrush}"
                            Text="{Binding ConnectionWarningText}"
                            TextWrapping="Wrap" />
                    </Border>
```

- [ ] **Step 3: Build**

```bash
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Verify all seven badge sites landed**

```bash
grep -c 'BasedOn="{StaticResource HF_AlertErrorBadge}"' \
  Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml
```
Expected: `7`

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Render wizard alert banners as filled badges"
```

---

## Task 5: Wizard XAML — inline accents

**Files:**
- Modify: `PLUGIN/TiltAdapterWizard/DataTemplates.xaml`

Eight sites keep being plain text and only change brush. Seven were alert-coloured; the eighth (`:1999`) is the long measurement-failure banner, which stays **outlined** per the house rule at `AutoFocus/DataTemplates.xaml:2967` — it wraps a paragraph, two sibling plain `TextBlock`s and a `Button`, so filling it would force every child onto the badge brush for no legibility gain.

| Line | Binding / context | New brush |
|---|---|---|
| 372 | `LimitTag` default setter | `HF_AlertWarningAccentBrush` |
| 378 | `LimitTag` `IsBlocking` trigger | `HF_AlertErrorAccentBrush` |
| 403 | `PositionsUnknownAdvisory` | `HF_AlertWarningAccentBrush` |
| 420 | `SendDisabledReason` | `HF_AlertWarningAccentBrush` |
| 510 | `WillReachText` | `HF_AlertWarningAccentBrush` |
| 537 | `TargetHintIsError` trigger | `HF_AlertErrorAccentBrush` |
| 569 | `ReviewDisabledReason` | `HF_AlertWarningAccentBrush` |
| 1999 | `MeasurementFailureText` | `HF_AlertErrorAccentBrush` |

- [ ] **Step 1: Swap the eight brushes**

Each is a single-token substitution on that line only:

```xml
<!-- line 372, inside <Style TargetType="TextBlock" BasedOn="{StaticResource StandardTextBlock}"> -->
<Setter Property="Foreground" Value="{StaticResource HF_AlertWarningAccentBrush}" />

<!-- line 378, inside the IsBlocking DataTrigger -->
<Setter Property="Foreground" Value="{StaticResource HF_AlertErrorAccentBrush}" />

<!-- lines 403, 420, 510, 569 — element attributes -->
Foreground="{StaticResource HF_AlertWarningAccentBrush}"

<!-- line 537, inside the TargetHintIsError DataTrigger -->
<Setter Property="Foreground" Value="{StaticResource HF_AlertErrorAccentBrush}" />

<!-- line 1999 — element attribute -->
Foreground="{StaticResource HF_AlertErrorAccentBrush}"
```

Do **not** touch the `BorderBrush` at 488, 548, 643, 649, 1984, 2201 — those are outlines and text-box error states, both already readable.

- [ ] **Step 2: Verify no alert `Foreground` remains in this file**

```bash
grep -n 'Foreground.*StaticResource Notification\(Error\|Warning\)Brush' \
  Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml
```
Expected: no output (exit status 1). Any hit is a missed site.

- [ ] **Step 3: Build**

```bash
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Use accessible accent brushes for wizard advisory text"
```

---

## Task 6: AutoFocus / Inspector XAML

**Files:**
- Modify: `PLUGIN/AutoFocus/DataTemplates.xaml`

- [ ] **Step 1: Re-point the two existing badge texts (lines 135, 143)**

The idle-countdown badge already fills correctly but reads its text colour from the schema, which is 1.93:1 on the Dark schema. Both lines currently say `Foreground="{StaticResource NotificationWarningTextBrush}"`; change both to:

```xml
Foreground="{StaticResource HF_AlertWarningTextBrush}"
```

Leave line 127 (`Background="{StaticResource NotificationWarningBrush}"`) exactly as it is — the fill is correct.

- [ ] **Step 2: Swap the four alert foregrounds to accents**

| Line | Context | New brush |
|---|---|---|
| 1845 | `⚠ differs from the focuser driver` | `HF_AlertWarningAccentBrush` |
| 2981 | `Tilt got worse after the last adjustment` | `HF_AlertErrorAccentBrush` |
| 3037 | `ViewingPastRunNotice` | `HF_AlertWarningAccentBrush` |
| 3373 | `AutomaticAdjustmentRemediationText` | `HF_AlertWarningAccentBrush` |

Line 2981 stays inside its **outlined** border — the comment directly above it at 2967 documents that `BorderThickness 1` with `NotificationErrorBrush` is the house style for a red box that carries an action, and this one wraps a paragraph plus two buttons.

Do **not** touch:
- lines 229/242/265/338/340 — chart `ErrorBarColor` / `Fill` / `Stroke`, which match NINA's own `AutoFocusChart.xaml`;
- lines 2252/3773/4269 — literal `BorderBrush="Red"`, already ~5:1 and not text;
- lines 3300/4236 — warning-outlined borders whose children are plain, already readable.

- [ ] **Step 3: Verify**

```bash
grep -n 'Foreground.*StaticResource Notification\(Error\|Warning\)\(Text\)\?Brush' \
  Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml
```
Expected: no output (exit status 1).

- [ ] **Step 4: Build**

```bash
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Make Inspector alert text readable on dark themes"
```

---

## Task 7: Optimizer, options and camera-simulator XAML

**Files:**
- Modify: `PLUGIN/StarDetection/Optimization/DataTemplates.xaml`
- Modify: `PLUGIN/Resources/OptionsDataTemplates.xaml`
- Modify: `PLUGIN/CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml`

- [ ] **Step 1: Optimizer — wrap `ErrorMessage` (line 1218) as a badge**

This is a real failure banner and is short, so it earns the fill. It is a bare `TextBlock` with a `Visibility` **attribute** (no `Style`), so the wrapping `Border` takes the same converter binding.

Find:
```xml
                <TextBlock
                    Margin="0,8,0,0"
                    Foreground="{StaticResource NotificationErrorBrush}"
                    Text="{Binding ErrorMessage}"
                    TextWrapping="Wrap"
                    Visibility="{Binding HasError, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}" />
```
Replace with:
```xml
                <!--  The BORDER carries the visibility, not the TextBlock: a filled badge whose text is blank
                      would otherwise render as an empty red pill.  -->
                <Border
                    Margin="0,8,0,0"
                    Style="{StaticResource HF_AlertErrorBadge}"
                    Visibility="{Binding HasError, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}">
                    <TextBlock
                        Foreground="{StaticResource HF_AlertErrorTextBrush}"
                        Text="{Binding ErrorMessage}"
                        TextWrapping="Wrap" />
                </Border>
```

- [ ] **Step 2: Optimizer — swap the four advisory notes to accents**

Lines 782 (`LowSignalChartNote`), 795 (`UndersampledStarsChartNote`), 1227 (`RecoveryWidenedNotice`), 1238 (`MinHfrRescueNotice`) all become:

```xml
Foreground="{StaticResource HF_AlertWarningAccentBrush}"
```

Keep these as text. The comment above line 782 states it is deliberately *"the page's ONLY colored element"* and deliberately the warning rather than the error brush — a pill would break that restraint.

Do **not** touch lines 97/108/121/123/187/189 — chart colours.

- [ ] **Step 3: Options — swap two accents**

`PLUGIN/Resources/OptionsDataTemplates.xaml` lines 2616 (`PerFilterEditBinder.ActiveFilterWarning`) and 3359 (`⚠ differs from the focuser driver`):

```xml
Foreground="{StaticResource HF_AlertWarningAccentBrush}"
```

- [ ] **Step 4: Camera simulator — one accent, three badge texts**

`PLUGIN/CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml`:

- Line 327 (`ExtremeTiltText`) → `Foreground="{StaticResource HF_AlertWarningAccentBrush}"`
- Lines 227, 275, 290 — currently `Foreground="{StaticResource NotificationWarningTextBrush}"` inside already-filled badges → `Foreground="{StaticResource HF_AlertWarningTextBrush}"`
- Leave lines 220, 269, 284 (`Background="{StaticResource NotificationWarningBrush}"`) unchanged.

- [ ] **Step 5: Verify every file is clean**

```bash
grep -rn 'Foreground.*StaticResource Notification\(Error\|Warning\)\(Text\)\?Brush' \
  Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus --include=*.xaml
```
Expected: no output (exit status 1). This is the completion check for the whole of Part 1 — every alert foreground in the plugin now goes through the computed brushes.

- [ ] **Step 6: Build**

```bash
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/DataTemplates.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Make optimizer, options and simulator alert text readable on dark themes"
```

---

## Task 8: Stop calling a `+N` move "Inward"

**Files:**
- Modify: `PLUGIN/TiltAdapterWizard/TiltAdapterWizardVM.cs` (lines 67, 756, 1040-1047, 1051-1053)
- Modify: `PLUGIN/TiltAdapterDevices/AsgEat/EatWizardMapping.cs`
- Modify: `PLUGIN/TiltAdapterWizard/DataTemplates.xaml` (line 1757)
- Test: `TESTS/TiltAdapterWizard/TiltAdapterWizardVMTests.cs`, `TESTS/TiltAdapterDevices/AsgEat/EatWizardMappingTests.cs`

**Behaviour must not change.** No sign, axis, amount or sequence is touched.

- [ ] **Step 1: Write the failing tests**

In `TESTS/TiltAdapterWizard/TiltAdapterWizardVMTests.cs`, replace the existing case at line 689:

```csharp
    [TestCase(WizardStep.AllInward, "All Screws Inward")]
```

with the two adjustment-type variants (keep every other `[TestCase]` on that test as-is, and add `isStepper` to the test signature and the `StepTitleText` call — the other cases pass `false`):

```csharp
    [TestCase(WizardStep.AllInward, false, "All Screws Clockwise")]
    [TestCase(WizardStep.AllInward, true, "All Motors Positive Steps")]
```

Then add a new guard test to the same fixture:

```csharp
    // The wizard reserves "inward"/"outward" for ADAPTER-PLATE motion; screw and motor moves are worded
    // clockwise/counter-clockwise or as signed steps. A step titled "All Screws Inward" while the device
    // applies +N to every motor is what made a correct move look like a bug — and "+N is inward" is not
    // something the wizard knows, it is what this very step measures.
    [Test]
    public void NoUserFacingMoveWording_ClaimsInwardOrOutward() {
        var offenders = new List<string>();
        foreach (var step in Enum.GetValues<WizardStep>()) {
            foreach (var isStepper in new[] { false, true }) {
                foreach (var screwCount in new[] { 3, 4 }) {
                    offenders.AddRange(new[] {
                        TiltAdapterWizardVM.StepTitleText(step, null, isStepper),
                        TiltAdapterWizardVM.StepInstructionsText(step, screwCount, isStepper, 1.0),
                        TiltAdapterWizardVM.BaselineRecoveryText(step, screwCount, isStepper, 1.0),
                    }.Where(t => t != null &&
                        (t.IndexOf("inward", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         t.IndexOf("outward", StringComparison.OrdinalIgnoreCase) >= 0)));
                }
            }
            var move = EatWizardMapping.MoveForStep(step, 150);
            if (move?.Description != null &&
                (move.Description.IndexOf("inward", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 move.Description.IndexOf("outward", StringComparison.OrdinalIgnoreCase) >= 0)) {
                offenders.Add(move.Description);
            }
        }
        Assert.That(offenders, Is.Empty, "user-facing move wording must not claim a direction the wizard has not measured");
    }
```

Add `using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;`, `using System.Linq;` and `using System.Collections.Generic;` to the file's usings if absent.

In `TESTS/TiltAdapterDevices/AsgEat/EatWizardMappingTests.cs`, add to the fixture:

```csharp
    [Test]
    public void AllInward_DescriptionDoesNotClaimAPhysicalDirection() {
        var move = EatWizardMapping.MoveForStep(WizardStep.AllInward, 150);
        Assert.Multiple(() => {
            Assert.That(move.Description, Does.Contain("All Motors"));
            Assert.That(move.Description, Does.Not.Contain("Inward"));
            Assert.That(move.Steps, Is.EqualTo(150), "wording only — the move itself is unchanged");
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.Backfocus), "wording only — the axis is unchanged");
        });
    }
```

- [ ] **Step 2: Run the tests and verify they fail**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~EatWizardMappingTests|FullyQualifiedName~TiltAdapterWizardVMTests"
```
Expected: FAIL — `StepTitleText` has no `isStepper` parameter; `Expected: String containing "All Motors"` but was `"Wizard All Inward: +150 backfocus"`.

- [ ] **Step 3: Change the wording**

In `PLUGIN/TiltAdapterWizard/TiltAdapterWizardVM.cs`:

Line 67 — the enum comment:
```csharp
        AllInward = 1,    // b: all screws clockwise / all motors +N once (curvature/backfocus sign via a→b;
                          // the NAME is historical and stays — it is persisted in saved replay runs — but no
                          // user-facing string may claim "+N" is physically inward: measuring that is the
                          // whole purpose of this step.
```

Line 756 — inside `StepTitleText`, whose signature gains a parameter:
```csharp
        internal static string StepTitleText(WizardStep step, IScrewLabelProvider labels, bool isStepper) {
            switch (step) {
                case WizardStep.Baseline: return "Baseline Measurement";
                // NOT "All Screws Inward": the wizard applies +N to every motor and MEASURES which way the
                // adapter plate goes. Naming the direction here contradicts the step and misreads as a bug.
                // "Positive Steps", not "+ Steps": the latter parses as two nouns ("motors AND steps") rather
                // than naming the sign the instruction body actually uses ("Apply +N steps to EVERY motor").
                case WizardStep.AllInward: return isStepper ? "All Motors Positive Steps" : "All Screws Clockwise";
```

Line 752 — the `StepTitle` property:
```csharp
        public string StepTitle => StepTitleText(currentStep, ScrewLabels, IsStepperAdjustment);
```

Line 1051 — `ReplayStepInstructionsText` threads the flag through:
```csharp
        internal static string ReplayStepInstructionsText(WizardStep step, IScrewLabelProvider labels, bool isStepper) =>
            $"Replaying — re-analyzing the saved frames for {StepTitleText(step, labels, isStepper)}. No action needed: nothing is " +
            "captured and the tilt adapter is not moved during a replay.";
```

Line 1042 — its call site inside `StepInstructions`:
```csharp
                ? ReplayStepInstructionsText(currentStep, ScrewLabels, IsStepperAdjustment)
```

**`isStepper` must NOT have a default on either method.** `StepInstructionsText` and `BaselineRecoveryText` in the
same file already make it required, and that stricter convention is what forces every call site to make an explicit
choice. A defaulted bool here silently gives a stepper rig screw wording at any call site that forgets it, and the
suite stays green. Update the few test call sites that relied on the default.

**`StepTitle` now depends on `IsStepperAdjustment`, so every place that invalidates its siblings must invalidate it
too.** There are TWO such places, not one:

Line ~484 — the adjustment-type change handler, which already raises `StepInstructions`:
```csharp
                    RaisePropertyChanged(nameof(StepInstructions));
                    RaisePropertyChanged(nameof(StepTitle));
                    RaisePropertyChanged(nameof(BaselineRecoveryInstructions));
```

Line ~543 — the `profileService.ProfileChanged` handler, which separately re-raises the same siblings. A profile
switch can change the adjustment type, and until the next `CurrentStep` transition the bold title would otherwise go
stale while the instruction paragraph directly below it updates — title and body visibly disagreeing, which is the
exact failure this task exists to remove:

```csharp
                    RaisePropertyChanged(nameof(StepInstructions));
                    RaisePropertyChanged(nameof(StepTitle));
                    RaisePropertyChanged(nameof(BaselineRecoveryInstructions));
```

In `PLUGIN/TiltAdapterDevices/AsgEat/EatWizardMapping.cs`, in the `WizardStep.AllInward` case:
```csharp
                case WizardStep.AllInward:
                    // "Apply +N steps to EVERY motor, then click Run Measurement." -> all four wizard
                    // screws +N -> (+N,+N,+N,+N) = Backfocus(+N). Described as "All Motors", never "Inward":
                    // whether +N is physically inward is exactly what this step measures.
                    return new TiltAdapterMove(
                        TiltMoveAxis.Backfocus, appliedSteps, TiltMoveGroup.Backfocus,
                        $"Wizard All Motors: {FormatSigned(appliedSteps)} backfocus");
```

Also update the class-level doc comment on `EatWizardMapping` where it lists `AllInward` in the sequence prose: change the words "AllInward" to "AllInward (all motors)" so the enum reference stays greppable while the prose no longer asserts a direction.

- [ ] **Step 4: Add the explanatory tooltip**

In `PLUGIN/TiltAdapterWizard/DataTemplates.xaml` at line 1755-1757, add a `ToolTip` to the step-title `TextBlock`:

```xml
                    <TextBlock
                        Margin="0,1,0,6"
                        FontWeight="Bold"
                        Text="{Binding StepTitle}"
                        Text="{Binding StepTitle}">
                        <!--  The tooltip is about the all-motors step SPECIFICALLY, and this one TextBlock renders
                              every step's title, so it must be scoped. Unscoped it would tell a user hovering
                              "Move Screw 1" about a different step's mechanics — a smaller version of the
                              text-does-not-match-the-hardware confusion this task exists to remove.  -->
                        <TextBlock.Style>
                            <Style BasedOn="{StaticResource StandardTextBlock}" TargetType="TextBlock">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding CurrentStep}" Value="{x:Static local:WizardStep.AllInward}">
                                        <Setter Property="ToolTip" Value="Moving every screw or motor together changes backfocus, and this step measures which way the adapter plate actually travels. A '+' step is not assumed to be inward — determining that direction is the point of the step." />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~EatWizardMappingTests|FullyQualifiedName~TiltAdapterWizardVMTests"
```
Expected: PASS. `EatWizardMappingTests`' existing full-sequence regressions must still pass untouched — they pin that the moves sum to `(0,0,0,0)`, which proves this change was wording-only.

- [ ] **Step 6: Update the stale doc reference**

In `docs/tilt-adapter-ui-review-design.md` line 83, change `AllInward → "All Screws Inward"` to `AllInward → "All Screws Clockwise" / "All Motors Positive Steps"`.

- [ ] **Step 7: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterDevices/AsgEat/EatWizardMapping.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterDevices/AsgEat/EatWizardMappingTests.cs \
        docs/tilt-adapter-ui-review-design.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Stop calling the all-motors calibration step 'Inward'"
```

---

## Task 9: Opt-in re-link — view model

**Files:**
- Modify: `PLUGIN/TiltAdapterWizard/TiltAdapterWizardVM.cs`
- Modify: `PLUGIN/CameraSimulator/TiltAdapter/SimulatedTiltAdapterVM.cs`
- Test: `TESTS/TiltAdapterWizard/TiltAdapterWizardVMTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `TESTS/TiltAdapterWizard/TiltAdapterWizardVMTests.cs`, using the fixture's **existing** `Build` helper (line 62) — do not invent a new one:

First add one private helper beside the fixture's other helpers — the only new setup this task needs:

```csharp
        // A saved, valid calibration on a motorized preset — the state the Trust banner is about.
        // "ASG Electronic EAT - 90mm" is a real registered preset name; TiltMotionControllerRegistry.IsMotorized
        // does an exact-name lookup, so an invented name would silently make every banner test pass vacuously.
        private const string MotorizedPreset = "ASG Electronic EAT - 90mm";

        private static (TiltAdapterWizardVM vm, ITiltAdapterOptions options) BuildCalibrated(
            string deviceName = MotorizedPreset, string linkedDevice = "", bool reliable = false) {
            var (vm, options, _, _) = Build(screwCount: 3, configureOptions: o => {
                o.DeviceName.Returns(deviceName);
                o.IsCalibrated.Returns(true);
                o.CalibratedScrewCount.Returns(3);
                o.DeviceLinkedCalibrationDeviceName.Returns(linkedDevice);
                o.CalibrationIsReliable.Returns(reliable);
            });
            return (vm, options);
        }
```

then the tests:

```csharp
        [Test]
        public void TrustCalibration_AloneWritesNothing() {
            var (vm, options) = BuildCalibrated();
            options.ClearReceivedCalls();

            vm.TrustCalibrationCommand.Execute(null);

            Assert.Multiple(() => {
                Assert.That(vm.IsTrustCalibrationPending, Is.True, "the button only arms the confirmation");
                options.DidNotReceive().DeviceLinkedCalibrationDeviceName = Arg.Any<string>();
                options.DidNotReceive().CalibrationIsReliable = Arg.Any<bool>();
            });
        }

        [Test]
        public void ConfirmTrustCalibration_ArmsBothAutomationMarkersForTheCurrentDevice() {
            var (vm, options) = BuildCalibrated();
            options.ClearReceivedCalls();

            vm.TrustCalibrationCommand.Execute(null);
            vm.ConfirmTrustCalibrationCommand.Execute(null);

            Assert.Multiple(() => {
                options.Received().DeviceLinkedCalibrationDeviceName = MotorizedPreset;
                options.Received().CalibrationIsReliable = true;
                Assert.That(vm.IsTrustCalibrationPending, Is.False);
            });
        }

        [Test]
        public void CancelTrustCalibration_WritesNothing() {
            var (vm, options) = BuildCalibrated();
            options.ClearReceivedCalls();

            vm.TrustCalibrationCommand.Execute(null);
            vm.CancelTrustCalibrationCommand.Execute(null);

            Assert.Multiple(() => {
                Assert.That(vm.IsTrustCalibrationPending, Is.False);
                options.DidNotReceive().DeviceLinkedCalibrationDeviceName = Arg.Any<string>();
                options.DidNotReceive().CalibrationIsReliable = Arg.Any<bool>();
            });
        }

        [Test]
        public void ApplyManualCalibration_DisarmsAPendingTrustConfirmation() {
            // A stale confirmation must not survive the state change that invalidated it.
            var (vm, options) = BuildCalibrated(linkedDevice: MotorizedPreset, reliable: true);
            options.ScrewInwardCurvatureSign.Returns(1);
            vm.TrustCalibrationCommand.Execute(null);
            vm.ManualScrew1AngleDegrees = 30;

            vm.ApplyManualCalibration();

            Assert.Multiple(() => {
                Assert.That(vm.IsTrustCalibrationPending, Is.False);
                options.Received().DeviceLinkedCalibrationDeviceName = string.Empty;
                options.Received().CalibrationIsReliable = false;
            });
        }

        [Test]
        public void ClearCalibration_AlsoClearsTheAutomationMarkers() {
            var (vm, options) = BuildCalibrated(linkedDevice: MotorizedPreset, reliable: true);
            options.ClearReceivedCalls();

            vm.ClearCalibrationCommand.Execute(null);

            Assert.Multiple(() => {
                options.Received().DeviceLinkedCalibrationDeviceName = string.Empty;
                options.Received().CalibrationIsReliable = false;
            });
        }

        [Test]
        public void AutomationTrustBanner_IsHiddenForADeviceLinkedReliableCalibration() {
            var (vm, _) = BuildCalibrated(linkedDevice: MotorizedPreset, reliable: true);
            Assert.That(vm.AutomationTrustBannerVisible, Is.False);
        }

        [Test]
        public void AutomationTrustBanner_IsShownForAManualCalibrationOnAMotorizedPreset() {
            var (vm, _) = BuildCalibrated();
            Assert.That(vm.AutomationTrustBannerVisible, Is.True);
        }

        [Test]
        public void AutomationTrustBanner_IsHiddenOnANonMotorizedPreset() {
            var (vm, _) = BuildCalibrated(deviceName: TiltAdapterDevicePreset.ManualName);
            Assert.That(vm.AutomationTrustBannerVisible, Is.False,
                "there is nothing to automate without a motorized device");
        }

        [Test]
        public void AutomationTrustBanner_IsHiddenWithNoSavedCalibration() {
            var (vm, _, _, _) = Build(screwCount: 3, configureOptions: o => {
                o.DeviceName.Returns(MotorizedPreset);
                o.IsCalibrated.Returns(false);
            });
            Assert.That(vm.AutomationTrustBannerVisible, Is.False);
        }

        [Test]
        public void ConfirmedTrust_SatisfiesTheAutomaticAdjustmentGate() {
            // The point of the whole feature: the Inspector's gate reads exactly these two markers.
            var (vm, options) = BuildCalibrated();
            vm.TrustCalibrationCommand.Execute(null);
            vm.ConfirmTrustCalibrationCommand.Execute(null);
            // NSubstitute property setters do not feed the getters back, so mirror what was written.
            options.DeviceLinkedCalibrationDeviceName.Returns(MotorizedPreset);
            options.CalibrationIsReliable.Returns(true);

            Assert.Multiple(() => {
                Assert.That(InspectorVM.IsCalibrationDeviceLinked(options), Is.True);
                Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
                    serviceConnected: true, controllerAvailable: true,
                    deviceLinked: InspectorVM.IsCalibrationDeviceLinked(options),
                    calibrationIsReliable: options.CalibrationIsReliable,
                    hasNumericGuidance: true, isOperationActive: false,
                    currentGeneration: 2, lastExecutedGeneration: 1), Is.True);
                Assert.That(vm.AutomationTrustBannerVisible, Is.False, "the banner retires once trust is granted");
            });
        }

        [Test]
        public void SelectingADifferentDevicePreset_RevokesTheTrust() {
            // No explicit revoke needed: IsCalibrationDeviceLinked compares against the CURRENT DeviceName.
            var (vm, options) = BuildCalibrated(linkedDevice: MotorizedPreset, reliable: true);
            Assert.That(vm.AutomationTrustBannerVisible, Is.False, "precondition: trusted");

            options.DeviceName.Returns("ASG Electronic EAT - ZWO 461");

            Assert.Multiple(() => {
                Assert.That(InspectorVM.IsCalibrationDeviceLinked(options), Is.False);
                Assert.That(vm.AutomationTrustBannerVisible, Is.True);
            });
        }
```

Add `using NINA.Joko.Plugins.HocusFocus.AutoFocus;` to the file's usings if absent (for `InspectorVM`).

- [ ] **Step 2: Run the tests and verify they fail**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~TiltAdapterWizardVMTests"
```
Expected: build FAILS — `TrustCalibrationCommand`, `IsTrustCalibrationPending`, `AutomationTrustBannerVisible` do not exist.

- [ ] **Step 3: Add the state, banner and commands**

In `PLUGIN/TiltAdapterWizard/TiltAdapterWizardVM.cs`, beside the other `ICommand` declarations (around line 1230), add:

```csharp
        public ICommand TrustCalibrationCommand { get; }
        public ICommand ConfirmTrustCalibrationCommand { get; }
        public ICommand CancelTrustCalibrationCommand { get; }
```

and in the constructor, beside the other command wiring:

```csharp
            // No canExecute predicate on purpose: the whole banner that hosts these buttons is collapsed
            // unless AutomationTrustBannerVisible, so gating them again would only add a second source of
            // truth that NotifyCommandsCanExecuteChangedCore does not know to refresh.
            TrustCalibrationCommand = new RelayCommand(() => IsTrustCalibrationPending = true);
            ConfirmTrustCalibrationCommand = new RelayCommand(ConfirmTrustCalibration);
            CancelTrustCalibrationCommand = new RelayCommand(() => IsTrustCalibrationPending = false);
```

Add the state and the gate near `IsCalibrationValid` (around line 797):

```csharp
        private bool isTrustCalibrationPending;

        /// <summary>
        /// True once the user has asked to trust a hand-entered calibration and the in-pane confirmation is
        /// showing. Two-step rather than a modal, mirroring SimulatedTiltAdapterVM's CopyToAdapter trio, which
        /// guards the structurally identical decision — and unlike a MyMessageBox it stays unit-testable.
        /// </summary>
        public bool IsTrustCalibrationPending {
            get => isTrustCalibrationPending;
            private set {
                if (isTrustCalibrationPending != value) {
                    isTrustCalibrationPending = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// [CRITICAL GATE] Shown when a saved calibration cannot drive Automatic Adjustment because it is not
        /// linked to the selected device preset, or did not pass its own confidence check — the exact pair
        /// InspectorVM.CanExecuteAutomaticAdjustment requires. Gated on a motorized preset because there is
        /// nothing to automate otherwise.
        ///
        /// This exists because clearing those markers used to be completely silent: a user who corrected one
        /// angle by hand after a device-driven run lost Automatic Adjustment with no message, no cause named,
        /// and no remedy short of repeating the whole calibration.
        /// </summary>
        public bool AutomationTrustBannerVisible =>
            IsCalibrationValid
            && IsMotorizedDevice
            && !(InspectorVM.IsCalibrationDeviceLinked(tiltAdapterOptions) && tiltAdapterOptions.CalibrationIsReliable);

        private void ConfirmTrustCalibration() {
            IsTrustCalibrationPending = false;
            // Deliberately re-arming the two markers ApplyManualCalibration cleared. The user has been told,
            // in the confirmation copy, exactly what goes wrong if the screw numbering does not match the
            // device's wiring. Both markers are re-cleared by every path that invalidates the correspondence:
            // a fresh manual entry, ClearCalibration, or selecting a different device preset (which
            // IsCalibrationDeviceLinked invalidates for free by comparing against the CURRENT DeviceName).
            tiltAdapterOptions.DeviceLinkedCalibrationDeviceName = tiltAdapterOptions.DeviceName;
            tiltAdapterOptions.CalibrationIsReliable = true;
            RaisePropertyChanged(nameof(AutomationTrustBannerVisible));
        }
```

- [ ] **Step 4: Warn when a manual entry revokes automation**

In `ApplyManualCalibration()`, capture the prior state before the `[CRITICAL GATE]` clears at line 1375, and warn after them. Insert immediately before `tiltAdapterOptions.DeviceLinkedCalibrationDeviceName = string.Empty;`:

```csharp
            // Whether this entry actually TAKES automation away, so a first-ever manual entry (which never had
            // it) does not warn about losing something the user never had.
            bool revokedAutomation =
                InspectorVM.IsCalibrationDeviceLinked(tiltAdapterOptions) || tiltAdapterOptions.CalibrationIsReliable;
```

and immediately after `tiltAdapterOptions.CalibrationIsReliable = false;`:

```csharp
            // A stale confirmation must not survive the state change that invalidated it.
            IsTrustCalibrationPending = false;
            if (revokedAutomation) {
                Notification.ShowWarning(
                    "Automatic Adjustment is now disabled: a hand-entered calibration can't be checked against the " +
                    "device's motor wiring. Use \"Trust This Calibration for Automation\" in the wizard to re-enable it, " +
                    "or re-run calibration with the device connected.");
            }
```

At the end of the method, beside the existing `RaiseHardwareSummaryChanged(); RebuildDiagram();`, add:

```csharp
            RaisePropertyChanged(nameof(AutomationTrustBannerVisible));
```

- [ ] **Step 5: Fix `ClearCalibration`'s missing marker clears**

`ClearCalibration()` clears the angles and flags but leaves `DeviceLinkedCalibrationDeviceName` and `CalibrationIsReliable` set. Harmless before this task only because clearing the angles also removes the numeric guidance the gate needs — but wrong now that Trust can set them deliberately. After `tiltAdapterOptions.CalibrationIsManual = false;` add:

```csharp
            // [CRITICAL GATE] Clearing the calibration must also clear what made it automation-trusted; leaving
            // these set would let a later Trust-free calibration inherit a stale link.
            tiltAdapterOptions.DeviceLinkedCalibrationDeviceName = string.Empty;
            tiltAdapterOptions.CalibrationIsReliable = false;
            IsTrustCalibrationPending = false;
```

and at the end, beside `RebuildDiagram();`:

```csharp
            RaisePropertyChanged(nameof(AutomationTrustBannerVisible));
```

- [ ] **Step 6: Raise the banner on the options that gate it**

In the `tiltAdapterOptions.PropertyChanged` handler (around line 438-460), extend the two existing blocks:

```csharp
                if (e.PropertyName == nameof(ITiltAdapterOptions.CalibrationIsManual)) {
                    RaisePropertyChanged(nameof(IsCalibrationValid));
                    RaisePropertyChanged(nameof(AutomationTrustBannerVisible));
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.ScrewCount) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.IsCalibrated) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.CalibratedScrewCount)) {
                    RaisePropertyChanged(nameof(IsCalibrationValid));
                    RaisePropertyChanged(nameof(AutomationTrustBannerVisible));
                    RebuildDiagram();
                }
```

and add the banner to the `DeviceName` block, which already raises `IsMotorizedDevice`:

```csharp
                    RaisePropertyChanged(nameof(IsMotorizedDevice));
                    RaisePropertyChanged(nameof(AutomationTrustBannerVisible));
```

- [ ] **Step 7: Marker hygiene in the camera simulator**

`SimulatedTiltAdapterVM.ConfirmCopyToAdapter()` writes a manual calibration over the real one and relies on setting `DeviceName = ManualName` to break the device link implicitly. Make it explicit, matching the other two paths. After `realAdapter.CalibrationIsManual = true;` add:

```csharp
            // [CRITICAL GATE] Same clears the wizard's manual-entry path makes. Setting DeviceName to Manual
            // above already breaks IsCalibrationDeviceLinked implicitly, but leaving a stale device name in the
            // marker is exactly the kind of thing that survives a later preset change.
            realAdapter.DeviceLinkedCalibrationDeviceName = string.Empty;
            realAdapter.CalibrationIsReliable = false;
```

- [ ] **Step 8: Run the tests and verify they pass**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~TiltAdapterWizardVMTests|FullyQualifiedName~SimulatedTiltAdapterVMTests"
```
Expected: PASS. The pre-existing test at line 807 asserting `options.Received().CalibrationIsManual = true` must still pass.

- [ ] **Step 9: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/SimulatedTiltAdapterVM.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Let a hand-entered calibration be trusted for automation, explicitly"
```

---

## Task 10: Opt-in re-link — wizard UI

**Files:**
- Modify: `PLUGIN/TiltAdapterWizard/DataTemplates.xaml`

- [ ] **Step 1: Add the banner to the saved-calibration sub-panel**

Insert immediately **before** the `Clear Calibration` `Button` (originally at line 1656, inside the `IsCalibrationValid` `StackPanel`):

```xml
                        <!--  [CRITICAL GATE] Automation is blocked for a calibration that is not device-linked or
                              not confidence-checked. Clearing those markers used to be entirely silent — a user
                              who corrected one angle by hand after a device-driven run lost Automatic Adjustment
                              with no message and no way back short of repeating the calibration. Filled badge
                              rather than the quiet outlined style because this CARRIES AN ACTION and explains a
                              capability that has disappeared (same reasoning as the idle-countdown banner in
                              AutoFocus/DataTemplates.xaml).  -->
                        <Border Margin="0,10,0,0" Visibility="{Binding AutomationTrustBannerVisible, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}"
                            Style="{StaticResource HF_AlertWarningBadge}">
                            <StackPanel>
                                <TextBlock
                                    MaxWidth="520"
                                    HorizontalAlignment="Left"
                                    FontWeight="Bold"
                                    Foreground="{StaticResource HF_AlertWarningTextBrush}"
                                    Text="Automatic Adjustment is disabled"
                                    TextWrapping="Wrap" />
                                <TextBlock
                                    MaxWidth="520"
                                    Margin="0,3,0,0"
                                    HorizontalAlignment="Left"
                                    Foreground="{StaticResource HF_AlertWarningTextBrush}"
                                    Text="This calibration was entered or edited by hand, so HocusFocus cannot verify that your screw numbering matches the device's motor wiring. Re-run the wizard with the device connected, or trust this calibration explicitly."
                                    TextWrapping="Wrap" />

                                <!--  Step 1: arm.  -->
                                <Button
                                    Margin="0,6,0,0"
                                    HorizontalAlignment="Left"
                                    Command="{Binding TrustCalibrationCommand}"
                                    Visibility="{Binding IsTrustCalibrationPending, Converter={StaticResource InverseBooleanToVisibilityCollapsedConverter}}">
                                    <TextBlock
                                        Margin="10,5,10,5"
                                        Foreground="{StaticResource ButtonForegroundBrush}"
                                        Text="Trust This Calibration for Automation" />
                                </Button>

                                <!--  Step 2: the risk, then confirm or back out.  -->
                                <StackPanel Visibility="{Binding IsTrustCalibrationPending, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}">
                                    <TextBlock
                                        MaxWidth="520"
                                        Margin="0,6,0,0"
                                        HorizontalAlignment="Left"
                                        FontWeight="Bold"
                                        Foreground="{StaticResource HF_AlertWarningTextBrush}"
                                        Text="If your screw numbering does not match the device's motor wiring, Automatic Adjustment will drive the adapter unattended in the wrong direction and make tilt worse. Verify with a single manual adjustment before leaving it unattended."
                                        TextWrapping="Wrap" />
                                    <WrapPanel Margin="0,6,0,0">
                                        <Button Command="{Binding ConfirmTrustCalibrationCommand}">
                                            <TextBlock
                                                Margin="10,5,10,5"
                                                Foreground="{StaticResource ButtonForegroundBrush}"
                                                Text="Yes, trust it" />
                                        </Button>
                                        <Button Margin="6,0,0,0" Command="{Binding CancelTrustCalibrationCommand}">
                                            <TextBlock
                                                Margin="10,5,10,5"
                                                Foreground="{StaticResource ButtonForegroundBrush}"
                                                Text="Cancel" />
                                        </Button>
                                    </WrapPanel>
                                </StackPanel>
                            </StackPanel>
                        </Border>
```

- [ ] **Step 2: Confirm both visibility converters resolve here**

```bash
grep -n 'InverseBooleanToVisibilityCollapsedConverter\|x:Key="BooleanToVisibilityCollapsedConverter"' \
  Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml | head
```
Expected: both keys appear. The wizard dictionary merges `OptionsDataTemplates.xaml`, so they resolve. If `InverseBooleanToVisibilityCollapsedConverter` is not defined there, use NINA's `InverseBooleanToVisibilityCollapsedConverter` from `util:` instead, or swap the two `Visibility` bindings to use a single `DataTrigger`-based `Style` — do not add a duplicate converter key.

- [ ] **Step 3: Build**

```bash
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Add the trust-this-calibration banner to the wizard"
```

---

## Task 11: Name the real cause in the Inspector

**Files:**
- Modify: `PLUGIN/AutoFocus/InspectorVM.cs` (line 2502-2505)
- Test: `TESTS/AutoFocus/InspectorVMAutomaticAdjustmentTests.cs`

- [ ] **Step 1: Write the failing test**

Add to the `#region IsCalibrationDeviceLinked` block of `TESTS/AutoFocus/InspectorVMAutomaticAdjustmentTests.cs` — the fixture that already covers `InspectorVM`'s pure gate helpers. It uses `"ASG Electronic EAT - 90mm"` as its preset name; keep that.

```csharp
    [Test]
    public void RemediationText_ForAHandEnteredCalibration_NamesManualEntryAsTheCause() {
        var options = Substitute.For<ITiltAdapterOptions>();
        options.DeviceName.Returns("ASG Electronic EAT - 90mm");
        options.DeviceLinkedCalibrationDeviceName.Returns(string.Empty);
        options.CalibrationIsManual.Returns(true);

        var text = InspectorVM.AutomaticAdjustmentRemediationTextFor(options);

        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("by hand"));
            Assert.That(text, Does.Contain("Tilt Adapter Wizard"), "the remedy must point at where the Trust button is");
        });
    }

    [Test]
    public void RemediationText_ForANonManualUnlinkedCalibration_KeepsTheExistingWording() {
        var options = Substitute.For<ITiltAdapterOptions>();
        options.DeviceName.Returns("ASG Electronic EAT - 90mm");
        options.DeviceLinkedCalibrationDeviceName.Returns(string.Empty);
        options.CalibrationIsManual.Returns(false);

        Assert.That(InspectorVM.AutomaticAdjustmentRemediationTextFor(options),
            Is.EqualTo("This calibration is not linked to the connected device. Re-run calibration with the device connected."));
    }

    [Test]
    public void RemediationText_ForALinkedButLowConfidenceCalibration_KeepsTheExistingWording() {
        var options = Substitute.For<ITiltAdapterOptions>();
        options.DeviceName.Returns("ASG Electronic EAT - 90mm");
        options.DeviceLinkedCalibrationDeviceName.Returns("ASG Electronic EAT - 90mm");
        options.CalibrationIsReliable.Returns(false);
        options.CalibrationIsManual.Returns(true);   // manual must NOT hijack the low-confidence branch

        Assert.That(InspectorVM.AutomaticAdjustmentRemediationTextFor(options),
            Is.EqualTo("This calibration is low-confidence (it did not pass quality validation). Re-run calibration to enable Automatic Adjustment."));
    }
```

- [ ] **Step 2: Run the test and verify it fails**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~RemediationText"
```
Expected: build FAILS — `AutomaticAdjustmentRemediationTextFor` does not exist.

- [ ] **Step 3: Extract the pure helper and add the branch**

In `PLUGIN/AutoFocus/InspectorVM.cs`, replace the property at line 2502-2505:

```csharp
        public string AutomaticAdjustmentRemediationText =>
            !IsCalibrationDeviceLinked(tiltAdapterOptions)
                ? "This calibration is not linked to the connected device. Re-run calibration with the device connected."
                : "This calibration is low-confidence (it did not pass quality validation). Re-run calibration to enable Automatic Adjustment.";
```

with:

```csharp
        public string AutomaticAdjustmentRemediationText => AutomaticAdjustmentRemediationTextFor(tiltAdapterOptions);

        /// <summary>
        /// Pure form of <see cref="AutomaticAdjustmentRemediationText"/>, so the wording is unit-testable
        /// without a live VM. Names MANUAL ENTRY explicitly when that is the cause: the previous copy said only
        /// "not linked to the connected device", which is true but gave a user who had just corrected an angle
        /// by hand no way to connect the message to what they did, and no remedy but a full re-run.
        /// </summary>
        internal static string AutomaticAdjustmentRemediationTextFor(ITiltAdapterOptions options) {
            if (!IsCalibrationDeviceLinked(options)) {
                return (options?.CalibrationIsManual ?? false)
                    ? "This calibration was entered by hand, so Automatic Adjustment is disabled — HocusFocus can't " +
                      "confirm your screw numbering matches the device's motor wiring. Re-run calibration with the " +
                      "device connected, or trust it explicitly in the Tilt Adapter Wizard."
                    : "This calibration is not linked to the connected device. Re-run calibration with the device connected.";
            }
            return "This calibration is low-confidence (it did not pass quality validation). Re-run calibration to enable Automatic Adjustment.";
        }
```

- [ ] **Step 4: Run the test and verify it passes**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  --filter "FullyQualifiedName~RemediationText"
```
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Name manual entry as the cause when automation is blocked"
```

---

## Task 11b: Button text that renders invisible on the Dark schema

**Found during Task 6's review, not in the original spec.** Scoped in because it is the same defect class the whole
plan exists to fix, and one instance sits *inside* the idle-countdown badge Task 6 just made readable.

**Files:**
- Modify: 16 sites across `PLUGIN/AutoFocus/DataTemplates.xaml`, `PLUGIN/AutoFocus/Replay/ReplaySettingsPromptControl.xaml`, `PLUGIN/CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml`, `PLUGIN/StarDetection/Optimization/DataTemplates.xaml`, `PLUGIN/TiltAdapterDevices/Prompt/TiltDeviceAdjustmentPromptControl.xaml`, `PLUGIN/TiltAdapterWizard/DataTemplates.xaml`

### The defect

A `TextBlock` used as `Button` content with **no explicit `Foreground` and no explicit `Style`** does not inherit the
button's foreground. NINA.WPF.Base ships a **keyless** `TextBlock` style (`Resources/Styles/TextBlock.xaml`,
`Foreground = PrimaryBrush`) in `Application.Resources`, and an implicit style beats an inherited value. So the text
renders in `PrimaryBrush` on `ButtonBackgroundBrush`.

On NINA's **Dark** and **Alternative Custom** schemas those two colours are the *same value* — `#FF550C18` — so the
button label is **1.00:1: literally invisible**. `TiltAdapterWizard/DataTemplates.xaml` already documents this
implicit-style hazard in its header comment; these 16 sites simply predate or missed it.

The rest of the plugin already follows the correct convention, e.g. every button in the wizard sets
`Foreground="{StaticResource ButtonForegroundBrush}"` on its content `TextBlock`.

### What this task does and does not fix

Adding `ButtonForegroundBrush` takes those two schemas from **1.00:1 to 1.44:1** and restores consistency with every
other button in the plugin and in NINA. It does **not** make button text WCAG-legible, because NINA's own button
palette is the limit: `ButtonForegroundColor` on `ButtonBackgroundColor` clears 4.5:1 on only **12 of 18** built-in
schemas (Dark and Alternative Custom 1.44, Dark Nebula and Custom 4.01, High Contrast 4.00, Vivid Malachite 4.45).

**Do not "fix" that by computing a contrast-safe button foreground.** Every button in this plugin would then look
different from every other button in NINA, which is a product decision for the maintainer, not a bug fix. This task
only removes the fully-invisible case.

- [ ] **Step 1: Add the missing foreground at all 16 sites**

Each is a one-attribute addition to a `TextBlock` that is the direct content of a `Button`:

```xml
<TextBlock Margin="6,2" Foreground="{StaticResource ButtonForegroundBrush}" Text="Stay connected" />
```

The 16 sites (line numbers are pre-edit; locate by button text, and note some files shift as you edit):

| File | Line | Button text |
|---|---|---|
| `AutoFocus/DataTemplates.xaml` | 153 | Stay connected |
| `AutoFocus/DataTemplates.xaml` | 724 | Review Frames |
| `AutoFocus/DataTemplates.xaml` | 3367 | Automatic Adjustment |
| `AutoFocus/Replay/ReplaySettingsPromptControl.xaml` | 135 | Cancel |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | 211 | Copy from adapter settings |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | 218 | Copy to adapter settings… |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | 239 | Overwrite |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | 245 | Cancel |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | 306 | Enable |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | 356 | Undo |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | 491 | Re-zero |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | 568 | Zero all aberrations |
| `StarDetection/Optimization/DataTemplates.xaml` | 1002 | Capture a new sweep and optimize |
| `StarDetection/Optimization/DataTemplates.xaml` | 1037 | (bound content) |
| `TiltAdapterDevices/Prompt/TiltDeviceAdjustmentPromptControl.xaml` | 373 | Cancel |
| `TiltAdapterWizard/DataTemplates.xaml` | 1660 | Clear Calibration |

**Check `TiltDeviceAdjustmentPromptControl.xaml:373` before editing it.** That control deliberately carries its own
self-contained, theme-independent palette (`Fg`, `FgDim`, `WarnFg`, `DangerFg`, fixed hex values) so it stays legible
regardless of schema. If its buttons are styled from that local palette, use the local foreground key that matches its
siblings rather than `ButtonForegroundBrush` — match the file, not this table.

- [ ] **Step 2: Build**

```bash
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Verify no Button-content TextBlock is left without a foreground**

```bash
python3 - <<'SCAN'
import re, glob
root='Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus'
def spans(src):
    out=[]; depth=0; start=None
    for m in re.finditer(r'<(/?)Button\b([^>]*?)(/?)>', src, re.S):
        closing, _, selfclose = m.group(1), m.group(2), m.group(3)
        if closing:
            depth-=1
            if depth==0 and start is not None: out.append((start,m.end())); start=None
        elif selfclose: continue
        else:
            if depth==0: start=m.start()
            depth+=1
    return out
bad=[]
for f in sorted(glob.glob(root+'/**/*.xaml', recursive=True)):
    if '/obj/' in f or '/bin/' in f: continue
    src=open(f,encoding='utf-8-sig',errors='replace').read()
    for s,e in spans(src):
        for tb in re.finditer(r'<TextBlock\b[^>]*?/>|<TextBlock\b[^>]*?>.*?</TextBlock>', src[s:e], re.S):
            t=tb.group(0)
            if 'Foreground' in t or 'Style=' in t: continue
            bad.append(f"{f}:{src[:s+tb.start()].count(chr(10))+1}")
print("remaining:", len(bad))
for b in bad: print("  ", b)
SCAN
```
Expected: `remaining: 0`.

- [ ] **Step 4: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Stop button labels rendering invisible on the Dark colour schema"
```

---

## Task 12: Documentation, full suite, live verification

**Files:**
- Modify: `.claude/docs/tilt-domain.md`
- Modify: `.claude/docs/wpf-xaml.md`

- [ ] **Step 1: Record the alert-brush rule so it is not re-broken**

Append to `.claude/docs/wpf-xaml.md`:

```markdown
## Alert colours — never use the NINA notification brushes as a Foreground

`NotificationErrorBrush` / `NotificationWarningBrush` are **fill** colours. NINA only ever uses them as a
`Background`, and 13 of its 18 built-in schemas set them to near-black (`#FF700000`, `#FF5E330B`). Used as a
`Foreground` they render at 1.06–1.9:1 against a dark page background. Their paired
`NotificationErrorTextBrush` is not a fix either — the "Dark" schema sets it to `#FF02010A`, i.e. near-black
text on near-black-red fill, 1.67:1.

Use `Resources/AlertBrushes.xaml` instead:

| Need | Brush |
|---|---|
| Text inside a filled alert badge | `HF_AlertErrorTextBrush` / `HF_AlertWarningTextBrush` |
| Alert text drawn on the page background | `HF_AlertErrorAccentBrush` / `HF_AlertWarningAccentBrush` |
| The filled badge `Border` itself | `HF_AlertErrorBadge` / `HF_AlertWarningBadge` (both `BasedOn`-able) |

Both text colours are computed by `Converters/ContrastMath.cs` and hold ≥ 4.5:1 on all 18 built-in schemas;
`ContrastMathTests.EveryBuiltInNinaSchema_ClearsWcagAaForBadgeTextAndAccent` is the regression.

Filled vs outlined follows the existing house rule (`AutoFocus/DataTemplates.xaml:120` and `:2967`): **filled**
for a short alert that carries an action or announces a lost capability, **outlined with accent text** for
advisory copy and for long banners that wrap paragraphs and buttons. When a `Border` does become filled, every
`TextBlock` inside it needs the badge text brush — children with no explicit `Foreground` otherwise inherit
`PrimaryBrush` onto the fill.
```

- [ ] **Step 2: Record the wording rule and the Trust path**

Append to `.claude/docs/tilt-domain.md`:

```markdown
## Wording: "inward/outward" is adapter-plate motion only

Screw and motor moves are worded CLOCKWISE / COUNTER-CLOCKWISE or as signed steps — never "inward/outward".
The wizard's all-motors step is titled "All Screws Clockwise" / "All Motors Positive Steps", never "All Screws
Inward": it applies `+N` to every motor and **measures** which way the plate travels (that is what sets
`ScrewInwardCurvatureSign`). `WizardStep.AllInward` keeps its historical enum name because it is persisted in
saved replay runs. `TiltAdapterWizardVMTests.NoUserFacingMoveWording_ClaimsInwardOrOutward` is the guard.

## Trusting a hand-entered calibration for automation

`ApplyManualCalibration` clears `DeviceLinkedCalibrationDeviceName` and `CalibrationIsReliable`, which blocks
Automatic Adjustment — a hand-entered screw numbering is not guaranteed to match the device's motor wiring.
The wizard now warns when that revokes something, and the saved-calibration pane offers **Trust This
Calibration for Automation** (a two-step in-pane confirmation, `TrustCalibrationCommand` →
`ConfirmTrustCalibrationCommand`) which re-arms both markers deliberately. There is no new persisted option:
`CalibrationIsManual == true` together with a device link already means "manually trusted". The trust is
revoked by a fresh manual entry, by `ClearCalibration`, and for free by a device-preset change.
```

- [ ] **Step 3: Run the full suite**

```bash
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```
Expected: `Passed!` with 0 failures. Do **not** pipe to `tail` — that masks the exit code. If `SendAsync_WritesOnABackgroundThread` is the only failure, re-run it alone to confirm it is the known flake; any other failure must be fixed, never skipped or marked expected-to-fail.

- [ ] **Step 4: Live verification in NINA**

Close NINA, rebuild (the PostBuild `xcopy` deploys the plugin), then launch NINA via `Start-Process` — not the MCP `launch_executable`.

Switch the colour schema to **Dark** (Options → General → Colors), then confirm:

1. Tilt Adapter Wizard → the alert badges are legible: white text on the red/amber fills, no near-black text.
2. Options and the Aberration Inspector → the `⚠ differs from the focuser driver` markers and the guidance notices are legible red/amber on the dark background.
3. Select an EAT preset → the wizard's all-motors step reads **"All Motors Positive Steps"**, and its tooltip explains that `+` is not assumed to be inward. Select a screw preset → **"All Screws Clockwise"**.
4. Apply a Manual Calibration Entry over a device-linked calibration → a warning notification appears, and the "Automatic Adjustment is disabled" badge shows in the saved-calibration pane.
5. Click **Trust This Calibration for Automation** → the risk copy and Yes/Cancel appear; Cancel changes nothing; Yes hides the badge and re-enables the Inspector's Automatic Adjustment button.
6. Switch to the **Light** schema and confirm nothing regressed — accent colours should be unchanged from before this work, because `AccessibleAccent` returns light-theme colours untouched.

- [ ] **Step 5: Commit and open the PR**

```bash
git add .claude/docs/tilt-domain.md .claude/docs/wpf-xaml.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Document the alert-brush, wording and calibration-trust rules"
git push -u origin ghilios/tilt-calibration-ux-feedback
gh pr create --base develop --title "Tilt calibration UX feedback: readable alerts, honest step wording, recoverable automation gate"
```

---

## Notes for whoever executes this

- **Do not weaken the automation gate.** Task 9 adds a way for the user to satisfy it deliberately. It does not change `CanExecuteAutomaticAdjustment`'s conditions.
- **Do not touch calibration math.** Part 2 is wording; the `EatWizardMappingTests` full-sequence regressions are the proof and must pass unmodified.
- **Do not lower `MinContrastRatio`** to make a test pass. If the sweep fails, the algorithm is wrong.
- **Do not add a persisted option.** If you find yourself adding one, re-read the spec's Part 3 — it is deliberately avoided, and a new persisted option would also require a control in `Resources/OptionsDataTemplates.xaml` per the project invariants.
