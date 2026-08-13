using NINA.Joko.Plugins.HocusFocus.Controls;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using Brushes = System.Windows.Media.Brushes;
using FrameworkElement = System.Windows.FrameworkElement;
using GridLength = System.Windows.GridLength;
using GridUnitType = System.Windows.GridUnitType;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using Thickness = System.Windows.Thickness;
using UIElement = System.Windows.UIElement;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Visibility = System.Windows.Visibility;
using Visual = System.Windows.Media.Visual;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Controls;

/// <summary>
/// Layout contract for <see cref="EdgeJustifiedWrapPanel"/>, which exists to fix a real rendering bug in the docked
/// AutoFocus pane: "Replay Saved AF" and the "Review Frames" + "Keep frames for review" group used to be a
/// left-aligned and a right-aligned child of ONE Auto grid-row cell, and a Grid cell hands every child the full cell
/// with nobody yielding — so below roughly a 500px pane they drew on top of each other.
///
/// Stub widths 130 / 110 / 195 and heights 25 / 25 / 28 stand in for the three real controls. The pill being TALLER
/// than the buttons is load-bearing: an all-25px fixture cannot tell "arranged at the line height" from "arranged at
/// the child's own height", and would let the wide-case identity claim pass vacuously.
///
/// Geometry is read from RENDERED rects, so a child shorter than its line appears at its VerticalAlignment's offset
/// within that line rather than at the line's top. Fixtures are built from stub Borders, never from the real
/// DataTemplates.xaml — that markup needs NINA theme dictionaries, {ns:Loc}, {StaticResource CancelSVG} and
/// ninactrl:CancellableButton, none of which resolve in the test host.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public class EdgeJustifiedWrapPanelTests {

    private const double ItemGap = 12.0;
    private const double LineGap = 6.0;

    // 130 + 12 + 110 + 12 + 195. The one-line/wrap boundary for the standard fixture.
    private const double PackedWidth = 459.0;

    private static readonly Size Unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);

    private static Border Stub(double width, double height, VerticalAlignment valign = VerticalAlignment.Center) =>
        new Border { Width = width, Height = height, VerticalAlignment = valign };

    /// <summary>The three AutoFocus footer controls, in shipped order and with their shipped alignments.</summary>
    private static EdgeJustifiedWrapPanel StandardPanel() {
        var panel = new EdgeJustifiedWrapPanel { ItemGap = ItemGap, LineGap = LineGap };
        panel.Children.Add(Stub(130, 25, VerticalAlignment.Bottom)); // Replay Saved AF
        panel.Children.Add(Stub(110, 25));                           // Review Frames
        panel.Children.Add(Stub(195, 28));                           // ON pill + "Keep frames for review"
        return panel;
    }

    /// <summary>Measure at the given width and arrange at exactly the height the panel asked for.</summary>
    private static EdgeJustifiedWrapPanel LayOut(EdgeJustifiedWrapPanel panel, double width) {
        // ALWAYS measure with an unbounded height: MeasureCore clamps DesiredSize to the constraint, so
        // Measure(new Size(500, 40)) would report Height 40 even when two lines need 59.
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        return panel;
    }

    private static Rect RectOf(FrameworkElement child, Visual ancestor) =>
        child.TransformToAncestor(ancestor).TransformBounds(new Rect(new Point(0, 0), child.RenderSize));

    private static List<Rect> RectsOf(EdgeJustifiedWrapPanel panel) =>
        panel.Children.Cast<FrameworkElement>().Select(c => RectOf(c, panel)).ToList();

    private static bool ShareALine(Rect a, Rect b) => a.Top < b.Bottom - 0.5 && b.Top < a.Bottom - 0.5;

    [Test]
    public void Wide_LeadingChildFlushLeft_TrailingGroupFlushRightAndCohesive() {
        var panel = LayOut(StandardPanel(), 1150);
        var r = RectsOf(panel);

        Assert.Multiple(() => {
            // The PACKED size, never the constraint — returning the constraint would make an Auto row span the pane.
            Assert.That(panel.DesiredSize.Width, Is.EqualTo(PackedWidth).Within(0.5));
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(28).Within(0.5), "the line is as tall as its tallest item");

            Assert.That(r[0].Left, Is.EqualTo(0).Within(0.5), "leading child flush left");
            Assert.That(r[2].Right, Is.EqualTo(1150).Within(0.5), "trailing child flush right");
            Assert.That(r[1].Left - r[0].Right, Is.EqualTo(ItemGap + (1150 - PackedWidth)).Within(0.5),
                "all slack belongs to the one gap after the leading child");
            Assert.That(r[2].Left - r[1].Right, Is.EqualTo(ItemGap).Within(0.5),
                "the trailing group stays packed at exactly ItemGap — it must not spread across the row");
        });
    }

    [Test]
    public void Wide_ChildrenArrangedAtLineHeightSoVerticalAlignmentStillResolves() {
        var panel = LayOut(StandardPanel(), 1150);
        var r = RectsOf(panel);

        // Arranging children at their OWN height instead of the line height turns VerticalAlignment into a no-op and
        // top-aligns unequal-height items, which is exactly what would silently break the wide-case appearance.
        Assert.Multiple(() => {
            Assert.That(r[0].Bottom, Is.EqualTo(28).Within(0.5), "the Bottom-aligned 25px child sits on the line's floor");
            Assert.That(r[1].Top, Is.EqualTo(1.5).Within(0.5), "the Center-aligned 25px child is centred in the 28px line");
            Assert.That(r[2].Top, Is.EqualTo(0).Within(0.5), "the tallest child defines the line");
        });
    }

    // THE AMENDMENT. A greedy break from index 0 would fill line 1 with [Replay][Review Frames] and orphan the
    // toggle on line 2 — directly beneath, and so reading as belonging to, the one control it has nothing to do with.
    // The toggle is what ENABLES Review Frames, so the two must wrap together.
    [TestCase(340)]
    [TestCase(400)]
    [TestCase(458)]
    public void Narrow_LeadingChildTakesItsOwnLine_TrailingGroupStaysTogether(double width) {
        var panel = LayOut(StandardPanel(), width);
        var r = RectsOf(panel);

        Assert.Multiple(() => {
            Assert.That(r[0].Bottom, Is.LessThanOrEqualTo(r[1].Top + 0.5), "Replay is alone on the first line");
            Assert.That(ShareALine(r[1], r[2]), Is.True, "Review Frames and the toggle that enables it stay together");
            Assert.That(r[2].Left - r[1].Right, Is.EqualTo(ItemGap).Within(0.5));
            Assert.That(r[0].Left, Is.EqualTo(0).Within(0.5), "wrapped lines pack left, with no justification");
            Assert.That(r[1].Left, Is.EqualTo(0).Within(0.5));
        });
    }

    [Test]
    public void Narrow_TwoLineGeometryIsExact() {
        var panel = LayOut(StandardPanel(), 400);
        var r = RectsOf(panel);

        Assert.Multiple(() => {
            Assert.That(panel.DesiredSize.Width, Is.EqualTo(317).Within(0.5), "the widest line: 110 + 12 + 195");
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(59).Within(0.5), "25 + LineGap + 28");

            Assert.That(r[0].Left, Is.EqualTo(0).Within(0.5));
            Assert.That(r[0].Top, Is.EqualTo(0).Within(0.5));
            // Line 2 opens at 25 + LineGap and is 28 tall; the 25px Review Frames stub centres within it at 32.5.
            Assert.That(r[1].Left, Is.EqualTo(0).Within(0.5));
            Assert.That(r[1].Top, Is.EqualTo(32.5).Within(0.5));
            Assert.That(r[2].Left, Is.EqualTo(122).Within(0.5), "110 + ItemGap");
            Assert.That(r[2].Top, Is.EqualTo(31).Within(0.5));
        });
    }

    [Test]
    public void VeryNarrow_BreaksToThreeLinesRatherThanClipping() {
        var panel = LayOut(StandardPanel(), 250);
        var r = RectsOf(panel);

        Assert.Multiple(() => {
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(90).Within(0.5), "25 + 6 + 25 + 6 + 28");
            Assert.That(r.Select(x => x.Left), Is.All.EqualTo(0.0).Within(0.5));
            Assert.That(r[0].Top, Is.EqualTo(0).Within(0.5));
            Assert.That(r[1].Top, Is.EqualTo(31).Within(0.5));
            Assert.That(r[2].Top, Is.EqualTo(62).Within(0.5));
        });
    }

    // The regression pin for the reported bug. Nothing else in this fixture would catch a reintroduction of the
    // collision at some width nobody thought to enumerate.
    [Test]
    public void NoChildEverOverlapsAnother_OrEscapesTheRow_AcrossTheFullWidthRange() {
        for (var w = 60.0; w <= 1400.0; w += 7.0) {
            var panel = LayOut(StandardPanel(), w);
            var r = RectsOf(panel);
            var height = panel.DesiredSize.Height;

            for (var i = 0; i < r.Count; ++i) {
                // A line past the panel's own reported height means the Auto row under-reserved: the footer then
                // overflows the bottom of the pane and is silently layout-clipped.
                Assert.That(r[i].Bottom, Is.LessThanOrEqualTo(height + 0.5), $"child {i} escapes the row at width {w}");
                Assert.That(r[i].Top, Is.GreaterThanOrEqualTo(-0.5), $"child {i} above the row at width {w}");
                Assert.That(r[i].Left, Is.GreaterThanOrEqualTo(-0.5), $"child {i} left of the row at width {w}");
                // The right edge is the direction content actually escapes in, and the only one that catches a
                // child measured against the constraint instead of at its natural width. RenderSize, not w:
                // ArrangeCore legitimately enlarges the panel to its unclipped desired width.
                Assert.That(r[i].Right, Is.LessThanOrEqualTo(panel.RenderSize.Width + 0.5),
                    $"child {i} runs off the right edge at width {w}");

                for (var j = i + 1; j < r.Count; ++j) {
                    var a = r[i];
                    var b = r[j];
                    a.Inflate(-0.5, -0.5); // a shared edge is not an overlap
                    b.Inflate(-0.5, -0.5);
                    Assert.That(a.IntersectsWith(b), Is.False, $"children {i} and {j} overlap at width {w}");
                }
            }
        }
    }

    [Test]
    public void InfiniteConstraint_MeasuresToAFiniteSingleLine() {
        var panel = StandardPanel();
        panel.Measure(Unbounded);

        // SizeToContent windows, ScrollViewers and Auto grid columns all measure at infinity. availableWidth is only
        // ever compared against, never used in arithmetic, so nothing infinite can reach DesiredSize.
        Assert.Multiple(() => {
            Assert.That(panel.DesiredSize.Width, Is.EqualTo(PackedWidth).Within(0.5));
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(28).Within(0.5));
        });
    }

    [Test]
    public void ArrangeWiderThanMeasured_RejustifiesInsteadOfReusingMeasureTimePositions() {
        var panel = StandardPanel();
        panel.Measure(new Size(600, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 1000, panel.DesiredSize.Height));

        // This is the everyday case, not an exotic one: the panel is Stretch inside an Auto row, so it measures
        // against the row width and is then arranged at the full pane width. Caching measure-time x positions
        // would leave the trailing group stranded mid-row.
        Assert.That(RectsOf(panel)[2].Right, Is.EqualTo(1000).Within(0.5));
    }

    [Test]
    public void ArrangeNarrowerThanMeasured_IsEnlargedByArrangeCore_AndStaysConsistent() {
        var panel = StandardPanel();
        panel.Measure(new Size(1200, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 450, panel.DesiredSize.Height));

        // A DOCUMENTED PROBE, not a wish: FrameworkElement.ArrangeCore enlarges the arrange slot to the element's
        // unclipped desired size when the parent offers less, and applies a layout clip instead. ArrangeOverride is
        // therefore handed 459, not 450, and a genuinely narrower pane always reaches the panel through a fresh
        // MEASURE. Keep this here so nobody "fixes" the panel to chase a shrink case WPF never produces.
        Assert.Multiple(() => {
            Assert.That(panel.RenderSize.Width, Is.EqualTo(PackedWidth).Within(0.5));
            Assert.That(RectsOf(panel)[2].Right, Is.EqualTo(PackedWidth).Within(0.5));
        });
    }

    [Test]
    public void CollapsedChild_ContributesNeitherWidthNorGap() {
        var panel = StandardPanel();
        ((UIElement)panel.Children[1]).Visibility = Visibility.Collapsed;
        LayOut(panel, 1150);

        Assert.Multiple(() => {
            // 130 + 12 + 195. Dropping the child but keeping its gap is the classic phantom-gap panel bug.
            Assert.That(panel.DesiredSize.Width, Is.EqualTo(337).Within(0.5));
            Assert.That(RectOf((FrameworkElement)panel.Children[2], panel).Right, Is.EqualTo(1150).Within(0.5),
                "flush right lands on the last VISIBLE child");
        });
    }

    [Test]
    public void HiddenChild_StillOccupiesItsSpace() {
        var panel = StandardPanel();
        ((UIElement)panel.Children[1]).Visibility = Visibility.Hidden;
        panel.Measure(new Size(1150, double.PositiveInfinity));

        // Hidden participates in layout and Collapsed does not; conflating them is a classic panel bug.
        Assert.That(panel.DesiredSize.Width, Is.EqualTo(PackedWidth).Within(0.5));
    }

    [Test]
    public void SingleChild_IsFlushLeftAndNeverStretched() {
        var panel = new EdgeJustifiedWrapPanel { ItemGap = ItemGap, LineGap = LineGap };
        panel.Children.Add(Stub(130, 25));
        LayOut(panel, 1150);

        var r = RectsOf(panel)[0];
        Assert.Multiple(() => {
            Assert.That(r.Left, Is.EqualTo(0).Within(0.5));
            Assert.That(r.Width, Is.EqualTo(130).Within(0.5), "a lone child is not also the flush-right item");
        });
    }

    [Test]
    public void DegenerateInputs_DoNotThrow() {
        var empty = new EdgeJustifiedWrapPanel { ItemGap = ItemGap, LineGap = LineGap };
        empty.Measure(Unbounded);
        Assert.That(empty.DesiredSize, Is.EqualTo(new Size(0, 0)));
        Assert.DoesNotThrow(() => empty.Arrange(new Rect(0, 0, 0, 0)));

        // Rect throws ArgumentException on a negative dimension, and a Grid legitimately passes a zero width.
        var panel = StandardPanel();
        Assert.DoesNotThrow(() => LayOut(panel, 0));
        Assert.That(RectsOf(panel).Select(r => r.Width), Is.All.GreaterThanOrEqualTo(0.0));
    }

    [Test]
    public void ChildWiderThanTheConstraint_GetsALineToItselfRatherThanAnEmptyOne() {
        // The oversized child is at index 1 ON PURPOSE: index 0 is placed by the leading-child special case and
        // never reaches the "a line always accepts at least one item" guard, so an index-0 fixture would pass with
        // the guard deleted. Here the guard is what stops the 5000px child from opening a phantom empty line.
        var panel = new EdgeJustifiedWrapPanel { ItemGap = ItemGap, LineGap = LineGap };
        panel.Children.Add(Stub(130, 25));
        panel.Children.Add(Stub(5000, 25));
        panel.Children.Add(Stub(110, 28));
        panel.Measure(new Size(200, double.PositiveInfinity));

        // Three lines of 25 / 25 / 28, two LineGaps. Do not assert the width: MeasureCore clamps DesiredSize to
        // the 200px constraint even though the packed content is 5000 wide.
        Assert.That(panel.DesiredSize.Height, Is.EqualTo(90).Within(0.5), "25 + 6 + 25 + 6 + 28, with no empty line");
    }

    // Children must be measured at their NATURAL width: measuring them against the constraint makes MeasureCore
    // clamp each DesiredSize to the constraint, every child then "fits", and the panel stops wrapping at all —
    // silently, since a clamped child still renders at its natural size and simply overhangs.
    [Test]
    public void ChildrenAreMeasuredAtTheirNaturalWidth_NotAgainstTheConstraint() {
        var panel = StandardPanel();
        panel.Measure(new Size(150, double.PositiveInfinity));

        Assert.Multiple(() => {
            Assert.That(((FrameworkElement)panel.Children[2]).DesiredSize.Width, Is.EqualTo(195).Within(0.5),
                "the 195px child must not be clamped to the 150px constraint");
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(90).Within(0.5), "so the row still breaks to three lines");
        });
    }

    // Non-monotonicity guard. A line's height is its tallest item, so re-breaking at a different width can shuffle
    // which children share a line and need MORE height than a narrower break did. MeasureOverride therefore
    // reserves height for the width ArrangeOverride will really be handed, which is the unclipped desired width
    // whenever some child is wider than the constraint.
    [Test]
    public void WhenAChildOverflowsTheConstraint_TheReservedHeightMatchesWhatArrangeLaysOut() {
        var panel = new EdgeJustifiedWrapPanel { ItemGap = ItemGap, LineGap = 0 };
        panel.Children.Add(Stub(243, 29));
        panel.Children.Add(Stub(93, 14));
        panel.Children.Add(Stub(163, 3));
        panel.Children.Add(Stub(21, 58));
        panel.Children.Add(Stub(95, 39));

        panel.Measure(new Size(136, double.PositiveInfinity));
        var reserved = panel.DesiredSize.Height;
        panel.Arrange(new Rect(0, 0, 136, reserved));

        foreach (var r in RectsOf(panel)) {
            Assert.That(r.Bottom, Is.LessThanOrEqualTo(reserved + 0.5),
                "arrange laid out taller than measure reserved — the footer overflows the pane");
        }
    }

    [Test]
    public void DesiredHeightNeverDecreasesAsThePaneNarrows() {
        var previous = 0.0;
        foreach (var w in new[] { 1400.0, 900, 500, 460, 400, 300, 230, 150, 60 }) {
            var panel = StandardPanel();
            panel.Measure(new Size(w, double.PositiveInfinity));
            Assert.That(panel.DesiredSize.Height, Is.GreaterThanOrEqualTo(previous - 0.5), $"height went down at width {w}");
            previous = panel.DesiredSize.Height;
        }
    }

    [Test]
    public void ArrangeIsIdempotent() {
        var panel = LayOut(StandardPanel(), 400);
        var first = RectsOf(panel);

        panel.InvalidateArrange();
        panel.Arrange(new Rect(0, 0, 400, panel.DesiredSize.Height));

        // Catches any accumulating cross-pass state, of which this panel has none and should keep none.
        Assert.That(RectsOf(panel), Is.EqualTo(first));
    }

    [Test]
    public void GapProperties_InvalidateMeasure_AndRejectNonsense() {
        var panel = LayOut(StandardPanel(), 1150);
        Assert.That(panel.IsMeasureValid, Is.True);

        panel.ItemGap = 20;
        // Fails if the DPs were registered with plain PropertyMetadata: the change would not repaint until
        // something else happened to dirty layout.
        Assert.That(panel.IsMeasureValid, Is.False);

        Assert.Multiple(() => {
            foreach (var bad in new[] { double.NaN, double.PositiveInfinity, -1.0 }) {
                Assert.Throws<ArgumentException>(() => panel.ItemGap = bad, $"ItemGap accepted {bad}");
                Assert.Throws<ArgumentException>(() => panel.LineGap = bad, $"LineGap accepted {bad}");
            }
        });
    }

    // The panel where it really ships: the Auto row under the "*" focus-chart row. The Auto row must reserve
    // exactly what the footer then lays out — a footer that measures short and arranges tall spills past the pane,
    // and a Panel does not clip its own children.
    [Test]
    public void InsideTheRealTwoRowGrid_TheAutoRowReservesExactlyWhatTheFooterLaysOut() {
        foreach (var w in new[] { 1150.0, 500, 400, 300, 230 }) {
            var chart = new Border { Background = Brushes.Transparent };
            var panel = StandardPanel();
            panel.Margin = new Thickness(10, 15, 10, 5);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(chart, 0);
            Grid.SetRow(panel, 1);
            grid.Children.Add(chart);
            grid.Children.Add(panel);

            grid.Measure(new Size(w, 600));
            grid.Arrange(new Rect(0, 0, w, 600));
            grid.UpdateLayout();

            var footerRow = grid.RowDefinitions[1].ActualHeight;
            Assert.That(grid.RowDefinitions[0].ActualHeight + footerRow, Is.EqualTo(600).Within(0.5),
                $"the two rows do not account for the pane at width {w}");
            // DesiredSize is margin-inclusive, so this is the footer's full 15 + content + 5.
            Assert.That(footerRow, Is.EqualTo(panel.DesiredSize.Height).Within(0.5),
                $"the Auto row does not reserve what the footer asked for at width {w}");

            foreach (var r in panel.Children.Cast<FrameworkElement>().Select(c => RectOf(c, grid))) {
                // 5 is the panel's bottom margin: anything past that is height the Auto row never reserved.
                Assert.That(r.Bottom, Is.LessThanOrEqualTo(600 - 5 + 0.5), $"footer spills past its row at width {w}");
                Assert.That(r.Top, Is.GreaterThanOrEqualTo(600 - footerRow - 0.5), $"footer starts above its row at width {w}");
            }
        }
    }

    [Test]
    public void HorizontalAlignmentOtherThanStretch_DegradesQuietlyButStaysCorrect() {
        var panel = StandardPanel();
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        LayOut(panel, 1150);

        // Documents the single easiest way to kill this feature with no error and no symptom beyond the group
        // drifting off the right edge: a non-Stretch panel is arranged at its DesiredSize, so slack is always 0.
        var r = RectsOf(panel);
        Assert.Multiple(() => {
            Assert.That(r[1].Left - r[0].Right, Is.EqualTo(ItemGap).Within(0.5), "no slack to justify with");
            Assert.That(r[2].Right, Is.EqualTo(PackedWidth).Within(0.5));
        });
    }
}

/// <summary>
/// Pure line-breaking table tests. No visual tree, so these carry the arithmetic cheaply and pin the exact
/// one-line/wrap boundary that the STA fixture can only sample.
/// </summary>
[TestFixture]
public class EdgeJustifiedWrapPanelComputeLinesTests {

    private static readonly IReadOnlyList<Size> Standard = new[] {
        new Size(130, 25), new Size(110, 25), new Size(195, 28),
    };

    private static (int lines, Size size) Break(double availableWidth, IReadOnlyList<Size> sizes = null) {
        var runs = new List<EdgeJustifiedWrapPanel.LineRun>();
        var size = EdgeJustifiedWrapPanel.ComputeLines(sizes ?? Standard, availableWidth, 12.0, 6.0, runs);
        return (runs.Count, size);
    }

    // 459 is the packed width; FitEpsilon (0.5) is what keeps a line that fits to within a rounding error intact.
    [TestCase(double.PositiveInfinity, 1)]
    [TestCase(1150.0, 1)]
    [TestCase(459.0, 1)]
    [TestCase(458.5, 1)]
    [TestCase(458.0, 2)]
    [TestCase(400.0, 2)]
    [TestCase(317.0, 2)]
    [TestCase(316.0, 3)]
    [TestCase(200.0, 3)]
    [TestCase(0.0, 3)]
    public void LineCountAcrossTheWidthRange(double availableWidth, int expected) {
        Assert.That(Break(availableWidth).lines, Is.EqualTo(expected));
    }

    [Test]
    public void OneLine_ReturnsThePackedSizeNotTheConstraint() {
        var (lines, size) = Break(1150);
        Assert.Multiple(() => {
            Assert.That(lines, Is.EqualTo(1));
            Assert.That(size.Width, Is.EqualTo(459.0).Within(0.001));
            Assert.That(size.Height, Is.EqualTo(28.0).Within(0.001), "the tallest item on the line");
        });
    }

    [Test]
    public void TwoLines_CarryExactlyOneLineGap() {
        var (lines, size) = Break(400);
        Assert.Multiple(() => {
            Assert.That(lines, Is.EqualTo(2));
            Assert.That(size.Width, Is.EqualTo(317.0).Within(0.001), "the widest line, not the sum");
            Assert.That(size.Height, Is.EqualTo(59.0).Within(0.001), "25 + 6 + 28 — n lines carry n-1 gaps");
        });
    }

    [Test]
    public void EmptyInput_IsZeroSizedAndProducesNoLines() {
        var (lines, size) = Break(1150, Array.Empty<Size>());
        Assert.Multiple(() => {
            Assert.That(lines, Is.EqualTo(0));
            Assert.That(size, Is.EqualTo(new Size(0, 0)));
        });
    }

    [Test]
    public void SingleOversizedItem_GetsOneLineAndItsOwnFullWidth() {
        var (lines, size) = Break(100, new[] { new Size(5000, 25) });
        Assert.Multiple(() => {
            Assert.That(lines, Is.EqualTo(1), "the lone child must not also produce an empty trailing line");
            Assert.That(size.Width, Is.EqualTo(5000.0).Within(0.001));
            Assert.That(size.Height, Is.EqualTo(25.0).Within(0.001));
        });
    }

    [Test]
    public void Wrapping_PutsTheLeadingChildAloneAndKeepsTheTrailingGroupTogether() {
        var runs = new List<EdgeJustifiedWrapPanel.LineRun>();
        EdgeJustifiedWrapPanel.ComputeLines(Standard, 400, 12.0, 6.0, runs);

        Assert.Multiple(() => {
            Assert.That(runs[0].First, Is.EqualTo(0));
            Assert.That(runs[0].Count, Is.EqualTo(1), "the leading child does not pair up with the trailing group");
            Assert.That(runs[1].First, Is.EqualTo(1));
            Assert.That(runs[1].Count, Is.EqualTo(2));
            Assert.That(runs[1].Width, Is.EqualTo(317.0).Within(0.001), "110 + ItemGap + 195");
        });
    }
}
