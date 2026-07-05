#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    /// <summary>
    /// Attached property that renders a bound string into a <see cref="TextBlock"/>'s inline run,
    /// substituting the screw-rotation sentinels ⟳ (U+27F3) / ⟲ (U+27F2) with the crisp vector
    /// icons HF_RotationCwSVG / HF_RotationCcwSVG. Every other character — including the adapter
    /// MOTION arrows ⬆⬇↑↓ and the em-dash "—" — passes through unchanged as text.
    ///
    /// The VMs keep emitting the glyphs (so their strings stay unit-testable); this control is the
    /// single place that turns those sentinels into legible icons. Use it in place of Text=, e.g.
    /// <c>controls:IconizedText.Text="{Binding SomeGlyphBearingString}"</c>. The icon inherits the
    /// TextBlock's Foreground and tracks its FontSize (sampled when Text changes).
    /// </summary>
    public static class IconizedText {

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text", typeof(string), typeof(IconizedText),
                new PropertyMetadata(null, OnTextChanged));

        public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);

        public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);

        private static void OnTextChanged(DependencyObject o, DependencyPropertyChangedEventArgs e) {
            if (o is not TextBlock tb) {
                return;
            }

            tb.Inlines.Clear();
            var s = e.NewValue as string;
            if (string.IsNullOrEmpty(s)) {
                return;
            }

            var buffer = new StringBuilder();
            foreach (var ch in s) {
                if (ch == '⟳' || ch == '⟲') { // ⟳ / ⟲ only
                    if (buffer.Length > 0) {
                        tb.Inlines.Add(new Run(buffer.ToString()));
                        buffer.Clear();
                    }
                    tb.Inlines.Add(BuildIcon(tb, ch == '⟳' ? "HF_RotationCwSVG" : "HF_RotationCcwSVG"));
                } else {
                    buffer.Append(ch); // ⬆⬇↑↓ and everything else pass through as text
                }
            }
            if (buffer.Length > 0) {
                tb.Inlines.Add(new Run(buffer.ToString()));
            }
        }

        private static InlineUIContainer BuildIcon(TextBlock tb, string resourceKey) {
            // Render a touch larger than the surrounding text: the rotation direction has to be
            // readable at inline sizes, and a glyph-sized icon loses the arrowhead.
            double size = (tb.FontSize > 0 ? tb.FontSize : 12) + 2;
            var path = new System.Windows.Shapes.Path {
                Data = tb.TryFindResource(resourceKey) as Geometry,
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
                Margin = new Thickness(1, 0, 1, 0),
                SnapsToDevicePixels = true
            };
            // Bind the fill to the TextBlock's Foreground so the icon always tracks the resolved theme
            // brush. A one-time snapshot (Fill = tb.Foreground) can freeze to the near-black default if
            // it runs before the theme brush resolves; the Run text inherits Foreground dynamically, but
            // a Shape's Fill does not — so without this binding the icon goes dim/invisible in dark theme.
            path.SetBinding(System.Windows.Shapes.Shape.FillProperty,
                new System.Windows.Data.Binding { Source = tb, Path = new PropertyPath(TextBlock.ForegroundProperty) });
            return new InlineUIContainer(path) { BaselineAlignment = BaselineAlignment.Center };
        }
    }
}
