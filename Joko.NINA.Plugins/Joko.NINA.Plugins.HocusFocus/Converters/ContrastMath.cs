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
    /// colors (it only ever uses them as a Background), and 15 of its 18 built-in schemas set them to near-black
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
                // Unreachable at TargetContrastRatio = 4.6, and deliberately kept anyway: reaching white
                // needs background luminance <= 1.05/T - 0.05, reaching black needs >= (T-1)/20, and those
                // ranges are disjoint for any T above sqrt(21) ~= 4.583. Lower the threshold and this
                // becomes live, so the tie-break stays. Do not try to unit-test it at the current T.
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
            // Exact-zero test, deliberately: r/g/b are byte/255.0, so channel maths is exact and an
            // achromatic color lands on delta == 0.0 precisely rather than merely near it.
            if (delta == 0.0) {
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
            // Exact-zero test, deliberately: s is only ever 0.0 here because RgbToHsl set it that way for an
            // exact-zero delta (see above), never as a result of accumulated floating-point error.
            if (s == 0.0) {
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
