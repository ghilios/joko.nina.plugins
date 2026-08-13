#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    /// <summary>
    /// A horizontal panel that justifies its content to both edges while everything fits on one line, and wraps
    /// to left-packed lines when it does not. No WPF built-in does both: DockPanel and Grid justify but then
    /// shrink-and-clip instead of breaking, and WrapPanel breaks but always packs left, so the wide case loses
    /// its flush-right group. There is no flexbox "space-between + wrap" and no adaptive trigger in WPF.
    ///
    /// Single-line layout: the FIRST visible child is flush left, and every child after it is packed together,
    /// <see cref="ItemGap"/> apart, flush right. All slack lands in the one gap after the leading child, so a
    /// trailing group of related controls stays cohesive instead of being spread evenly across the row (CSS
    /// space-between), which is what a footer row of "one primary action, left | related actions, right" wants.
    /// One visible child is simply flush left and never stretched; zero children measure to 0x0.
    ///
    /// Multi-line layout: the leading child keeps a line to itself and the trailing group wraps among itself,
    /// every line packed left with <see cref="ItemGap"/> between items and <see cref="LineGap"/> between lines.
    /// Nothing is justified once the row has broken — there is no single row left to justify against, and
    /// flinging each line's items to opposite margins reads as an alignment bug. Wrapping this way rather than
    /// greedily from the first child preserves the grouping the single-line case shows: the leading child is the
    /// odd one out at both widths, instead of pairing up with the head of the trailing group and orphaning the
    /// rest of that group beneath it.
    ///
    /// Group integrity is structural: anything that must never be split across lines (a themed ON/OFF toggle and
    /// its label, say) is passed as ONE child, typically a StackPanel, and this panel never looks inside it.
    /// No attached "keep together" property is needed, or offered.
    ///
    /// EVERY CHILD MUST BE CONTENT-SIZED. Children are measured at unbounded width and arranged at exactly that
    /// desired width, so nothing inside one can reflow: a <c>TextWrapping="Wrap"</c> TextBlock never wraps and is
    /// layout-clipped at its natural width, and a child with <c>HorizontalAlignment="Stretch"</c> and no width of
    /// its own collapses to nothing. Both fail silently. That is the deliberate trade — the panel breaks BETWEEN
    /// children so their text does not have to break inside them — but it means a nested WrapPanel is not a way to
    /// subdivide a group further; give the panel more children instead.
    /// </summary>
    public class EdgeJustifiedWrapPanel : Panel {

        // Fit-test tolerance, in device-independent pixels. UseLayoutRounding is an inherited property that a
        // theme can switch on; when it is on, WPF rounds both DesiredSize and the arranged rect to whole device
        // pixels AFTER this code runs, so an exact comparison can break a line that visually fits by a fraction
        // of a pixel. Erring on the "still fits" side cannot produce an overlap: the slack clamp in
        // ArrangeOverride floors the justified gap at ItemGap no matter how the comparison went.
        private const double FitEpsilon = 0.5d;

        public static readonly DependencyProperty ItemGapProperty = DependencyProperty.Register(
            "ItemGap",
            typeof(double),
            typeof(EdgeJustifiedWrapPanel),
            new FrameworkPropertyMetadata(
                0.0d,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
            IsFiniteAndNonNegative);

        public static readonly DependencyProperty LineGapProperty = DependencyProperty.Register(
            "LineGap",
            typeof(double),
            typeof(EdgeJustifiedWrapPanel),
            new FrameworkPropertyMetadata(
                0.0d,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
            IsFiniteAndNonNegative);

        /// <summary>
        /// Minimum horizontal space between two items on the same line, and the floor on the justified gap.
        /// Defaults to 0 so a caller can keep spacing items with their own margins instead — use one or the
        /// other, never both: WPF margins do not collapse, so a 12px margin plus a 12px gap is 24px.
        /// </summary>
        public double ItemGap {
            get { return (double)GetValue(ItemGapProperty); }
            set { SetValue(ItemGapProperty, value); }
        }

        /// <summary>Vertical space between wrapped lines. Has no effect until the content wraps.</summary>
        public double LineGap {
            get { return (double)GetValue(LineGapProperty); }
            set { SetValue(LineGapProperty, value); }
        }

        // Rejects NaN/Infinity/negative at the assignment site, where the stack trace names the culprit, rather
        // than letting it surface as an opaque "should not return PositiveInfinity or NaN as its DesiredSize"
        // failure inside the layout pass.
        private static bool IsFiniteAndNonNegative(object value) {
            return value is double d && !double.IsNaN(d) && !double.IsInfinity(d) && d >= 0.0d;
        }

        protected override Size MeasureOverride(Size availableSize) {
            var children = InternalChildren;

            // Measure at UNBOUNDED width. MeasureCore clamps a child's DesiredSize to whatever size it was
            // handed, so measuring at the constraint reports an over-wide child as exactly available-wide and
            // the fit test below can never fail. Nothing in this panel is allowed to reflow horizontally (the
            // "Keep frames for review" label must not break mid-phrase), so natural width is the correct input —
            // and it stays readable from DesiredSize during arrange, which is why this panel needs no cross-pass
            // state whatsoever. availableSize.Height is passed through unchanged; under a Height="Auto" grid row
            // that is PositiveInfinity, which is fine and expected.
            var childConstraint = new Size(double.PositiveInfinity, availableSize.Height);
            for (int i = 0; i < children.Count; ++i) {
                children[i].Measure(childConstraint); // collapsed children included; Measure short-circuits them to 0x0
            }

            var visible = CollectVisible(children);
            var sizes = DesiredSizesOf(children, visible);

            // availableSize.Width is only ever COMPARED against inside ComputeLines, never used in arithmetic,
            // so an infinite constraint (SizeToContent window, ScrollViewer, Auto grid column) just means "one
            // line" and can never leak Infinity or NaN into the DesiredSize we return.
            var lines = new List<LineRun>(sizes.Count);
            var packed = ComputeLines(sizes, availableSize.Width, ItemGap, LineGap, lines);

            // Measure at the width ARRANGE will really see, so the height we reserve is the height we then use.
            // A packed width above the constraint means some child is wider than the constraint on its own, and
            // ArrangeCore responds by raising the arrange slot to this unclipped desired width — at which point
            // the greedy break repacks, and because a line's height is its tallest item, repacking can need MORE
            // height than the narrower break did. Recomputing here is a fixed point rather than the first step of
            // a loop: the packed width can only exceed the constraint by being exactly the widest child's width,
            // and re-breaking at that width cannot produce a line wider still.
            if (packed.Width > availableSize.Width) {
                packed = ComputeLines(sizes, packed.Width, ItemGap, LineGap, lines);
            }

            return packed;
        }

        protected override Size ArrangeOverride(Size finalSize) {
            var children = InternalChildren;
            var visible = CollectVisible(children);
            var sizes = DesiredSizesOf(children, visible);
            var itemGap = ItemGap;
            var lineGap = LineGap;

            // Re-break from finalSize.Width. finalSize is NOT the measure constraint: this panel's
            // HorizontalAlignment is Stretch, so an Auto grid row arranges it at the FULL row width — which is
            // precisely what makes the trailing group land flush right. Caching measure-time breaks or x positions
            // would leave the trailing group stranded wherever the narrower measure pass put it, which is the very
            // misplacement this panel exists to fix. Note that WPF never arranges us narrower than we measured:
            // ArrangeCore raises the slot to our unclipped desired size and applies a layout clip instead.
            var lines = new List<LineRun>(sizes.Count);
            ComputeLines(sizes, finalSize.Width, itemGap, lineGap, lines);

            double y = 0.0d;
            for (int l = 0; l < lines.Count; ++l) {
                var line = lines[l];

                // Justify only while the whole row is on one line and there is something to justify against.
                // Math.Max keeps the gap at >= ItemGap when the parent arranges us narrower than we measured;
                // a negative slack is the overlap bug, and Rect throws ArgumentException on a negative width.
                double slack = lines.Count == 1 && line.Count > 1
                    ? Math.Max(0.0d, finalSize.Width - line.Width)
                    : 0.0d;

                double x = 0.0d;
                for (int k = 0; k < line.Count; ++k) {
                    if (k > 0) {
                        x += itemGap + (k == 1 ? slack : 0.0d); // the entire slack goes into the first gap
                    }

                    var size = sizes[line.First + k];

                    // Arrange at the LINE height, not the child's own, so each child's VerticalAlignment still
                    // positions it within the line (Bottom for the taller-row case, Center for the rest).
                    children[visible[line.First + k]].Arrange(
                        new Rect(x, y, Math.Max(0.0d, size.Width), Math.Max(0.0d, line.Height)));
                    x += size.Width;
                }

                // Top-down only; never compute y backwards from finalSize.Height. MeasureOverride reserves height
                // for the break at this same width, so the lines fit — and if a future change ever breaks that
                // agreement, the surplus overflows the BOTTOM of the pane (this row is the last one) rather than
                // reaching the chart above. Calling InvalidateMeasure from inside arrange risks a layout cycle.
                y += line.Height + lineGap;
            }

            // Every child must be arranged exactly once. A measured-but-never-arranged child keeps
            // IsArrangeValid false and renders at its stale position.
            for (int i = 0; i < children.Count; ++i) {
                if (children[i].Visibility == Visibility.Collapsed) {
                    children[i].Arrange(new Rect(0.0d, 0.0d, 0.0d, 0.0d));
                }
            }

            return finalSize;
        }

        /// <summary>
        /// Line breaking: everything on one line while it fits, otherwise the leading child alone on the first
        /// line and the trailing group wrapped greedily among itself, at least one item per line. Fills
        /// <paramref name="lines"/> and returns the PACKED content size — never the constraint. Pure, so both
        /// layout overrides can call it and the interesting cases are unit testable with no visual tree.
        /// </summary>
        internal static Size ComputeLines(IReadOnlyList<Size> sizes, double availableWidth, double itemGap, double lineGap, List<LineRun> lines) {
            lines.Clear();
            if (sizes.Count == 0) {
                return new Size(0.0d, 0.0d);
            }

            double packedWidth = 0.0d;
            double packedHeight = 0.0d;
            for (int k = 0; k < sizes.Count; ++k) {
                packedWidth += (k > 0 ? itemGap : 0.0d) + sizes[k].Width;
                packedHeight = Math.Max(packedHeight, sizes[k].Height);
            }

            // availableWidth is only ever COMPARED against, never used in arithmetic, so an infinite constraint
            // takes this branch and no Infinity or NaN can reach the size we return.
            if (packedWidth <= availableWidth + FitEpsilon) {
                lines.Add(new LineRun(0, sizes.Count, packedWidth, packedHeight));
                return new Size(packedWidth, packedHeight);
            }

            // Break at the leading/trailing boundary FIRST. Greedy breaking from index 0 would instead fill line 1
            // with the leading child plus as much of the trailing group as fits, orphaning the remainder of that
            // group on line 2 — directly beneath, and so read as belonging to, the one child it is unrelated to.
            lines.Add(new LineRun(0, 1, sizes[0].Width, sizes[0].Height));
            double totalWidth = sizes[0].Width;
            double totalHeight = sizes[0].Height;

            int first = 1;
            int count = 0;
            double lineWidth = 0.0d;
            double lineHeight = 0.0d;

            for (int k = 1; k < sizes.Count; ++k) {
                var size = sizes[k];

                // The count > 0 guard is the termination guarantee: a line ALWAYS accepts at least one item, so
                // a child wider than the constraint — or a zero-width constraint, which a Grid legitimately
                // passes — gets a line to itself instead of spinning here forever.
                if (count > 0 && lineWidth + itemGap + size.Width > availableWidth + FitEpsilon) {
                    lines.Add(new LineRun(first, count, lineWidth, lineHeight));
                    totalWidth = Math.Max(totalWidth, lineWidth);
                    totalHeight += lineGap + lineHeight;
                    first = k;
                    count = 0;
                    lineWidth = 0.0d;
                    lineHeight = 0.0d;
                }

                lineWidth += (count > 0 ? itemGap : 0.0d) + size.Width;
                lineHeight = Math.Max(lineHeight, size.Height);
                ++count;
            }

            // Empty only when a lone child did not fit, and that child already has its line above.
            if (count > 0) {
                lines.Add(new LineRun(first, count, lineWidth, lineHeight));
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineGap + lineHeight; // n lines carry n-1 gaps: this one precedes the line it adds
            }

            return new Size(totalWidth, totalHeight);
        }

        // Indices of the non-collapsed children, in order. A collapsed child must contribute neither width nor a
        // gap; walking a compacted list is what keeps the phantom-gap bug structurally impossible.
        private static List<int> CollectVisible(UIElementCollection children) {
            var visible = new List<int>(children.Count);
            for (int i = 0; i < children.Count; ++i) {
                if (children[i].Visibility != Visibility.Collapsed) {
                    visible.Add(i);
                }
            }
            return visible;
        }

        private static List<Size> DesiredSizesOf(UIElementCollection children, List<int> visible) {
            var sizes = new List<Size>(visible.Count);
            for (int k = 0; k < visible.Count; ++k) {
                sizes.Add(children[visible[k]].DesiredSize);
            }
            return sizes;
        }

        /// <summary>One laid-out line: a run over the VISIBLE children, its packed width and its tallest item.</summary>
        internal readonly struct LineRun {

            public LineRun(int first, int count, double width, double height) {
                First = first;
                Count = count;
                Width = width;
                Height = height;
            }

            /// <summary>Index of the line's first item within the compacted list of visible children.</summary>
            public int First { get; }

            public int Count { get; }

            /// <summary>Sum of the items' desired widths plus (Count - 1) ItemGaps.</summary>
            public double Width { get; }

            public double Height { get; }
        }
    }
}
