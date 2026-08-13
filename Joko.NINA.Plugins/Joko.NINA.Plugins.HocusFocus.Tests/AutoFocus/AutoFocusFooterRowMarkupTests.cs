using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

/// <summary>
/// Structural guard on the AutoFocus pane's footer row — the "Replay Saved AF" / "Review Frames" / "Keep frames for
/// review" strip — in the shipped markup. The STA fixture for
/// <see cref="NINA.Joko.Plugins.HocusFocus.Controls.EdgeJustifiedWrapPanel"/> can only exercise stub trees: the real
/// template needs NINA theme dictionaries, {ns:Loc} and ninactrl:CancellableButton and does not load in a test host.
/// So this reads the XAML as XML instead, and pins the handful of properties that, if broken, silently restore the
/// original bug with no build error and no failing layout test.
///
/// The bug: the Replay button and a right-aligned StackPanel were two children of ONE Auto grid-row cell. A Grid cell
/// gives every child the full cell and nobody yields, so below roughly a 500px pane they drew on top of each other.
/// </summary>
[TestFixture]
public class AutoFocusFooterRowMarkupTests {

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    // Anchored on the source file's own path, like OptionsDataTemplatesLayoutTests, rather than walking up from the
    // test binaries — the walk breaks the moment the build output moves.
    private static XElement DockableTemplate([CallerFilePath] string thisFile = null) {
        // thisFile: <repo>/Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/AutoFocusFooterRowMarkupTests.cs
        var testsProjectDir = Directory.GetParent(Path.GetDirectoryName(thisFile)).FullName;
        var xaml = Path.GetFullPath(Path.Combine(
            testsProjectDir, "..", "Joko.NINA.Plugins.HocusFocus", "AutoFocus", "DataTemplates.xaml"));
        Assert.That(File.Exists(xaml), Is.True, $"Could not locate DataTemplates.xaml at '{xaml}'");

        var template = XDocument.Load(xaml).Descendants(Presentation + "DataTemplate")
            .SingleOrDefault(t => ((string)t.Attribute(Xaml + "Key"))?.EndsWith("HocusFocusVM_Dockable",
                StringComparison.Ordinal) == true);
        Assert.That(template, Is.Not.Null, "The AutoFocus pane's _Dockable DataTemplate is gone or was renamed.");
        return template;
    }

    /// <summary>Real child elements. Property elements — <c>&lt;Grid.RowDefinitions&gt;</c> and friends — are not children.</summary>
    private static XElement[] ElementChildren(XElement e) =>
        e.Elements().Where(c => !c.Name.LocalName.Contains('.')).ToArray();

    /// <summary>
    /// The left and right components of an element's Margin, parsed. Thickness accepts 1, 2 and 4 components
    /// (uniform, horizontal/vertical, then left/top/right/bottom), so "0.0", ".0" and "0" must all read as zero.
    /// </summary>
    private static IEnumerable<(string Edge, double Value)> HorizontalMarginsOf(XElement e) {
        var margin = (string)e.Attribute("Margin");
        if (margin == null) {
            yield break;
        }

        var parts = margin.Split(',');
        Assert.That(parts.Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)),
            Is.All.True, $"Margin=\"{margin}\" on <{e.Name.LocalName}> is not a literal Thickness this guard can read.");

        var values = parts.Select(p => double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        yield return ("left", values[0]);
        yield return ("right", values.Length == 4 ? values[2] : values[0]);
    }

    [Test]
    public void FooterRow_IsASingleWrappingPanel_NotTwoSiblingsSharingOneCell() {
        var row1 = DockableTemplate().Descendants()
            .Where(e => (string)e.Attribute("Grid.Row") == "1")
            .ToArray();

        Assert.That(row1.Length, Is.EqualTo(1),
            "Two or more elements share the footer's Auto row cell again — that is the overlap bug itself.");
        Assert.That(row1[0].Name.LocalName, Is.EqualTo("EdgeJustifiedWrapPanel"));
        Assert.That(ElementChildren(row1[0]).Length, Is.EqualTo(3),
            "Replay, Review Frames and the toggle group must be three separate children so the row can break.");
    }

    [Test]
    public void FooterPanel_KeepsTheContractThatMakesJustificationWork() {
        var panel = DockableTemplate().Descendants()
            .Single(e => e.Name.LocalName == "EdgeJustifiedWrapPanel");

        Assert.Multiple(() => {
            // A non-Stretch panel is arranged at its DesiredSize, so there is never any slack: the trailing group
            // silently stops being flush right, with no error and nothing else to notice.
            Assert.That(panel.Attribute("HorizontalAlignment"), Is.Null,
                "The panel must keep the default Stretch to have a full row width to justify against.");

            // The gaps are the ONLY source of spacing left, since child margins are forbidden below.
            Assert.That((string)panel.Attribute("ItemGap"), Is.EqualTo("12"),
                "Without ItemGap the Review Frames button butts straight against the ON/OFF pill.");
            Assert.That((string)panel.Attribute("LineGap"), Is.EqualTo("6"),
                "Without LineGap the wrapped lines touch.");

            // WPF margins do not collapse, so a horizontal child margin ADDS to ItemGap rather than absorbing into it.
            foreach (var child in ElementChildren(panel)) {
                foreach (var (edge, value) in HorizontalMarginsOf(child)) {
                    Assert.That(value, Is.EqualTo(0.0),
                        $"{edge} margin on <{child.Name.LocalName}> adds to ItemGap rather than absorbing into it");
                }
                Assert.That(child.Attribute("HorizontalAlignment"), Is.Null,
                    $"HorizontalAlignment on <{child.Name.LocalName}> does nothing — the panel arranges children at their desired width.");
            }
        });
    }

    [Test]
    public void ToggleAndItsCaption_StayASingleChild() {
        var panel = DockableTemplate().Descendants()
            .Single(e => e.Name.LocalName == "EdgeJustifiedWrapPanel");

        var checkBox = panel.Descendants().Single(e => e.Name.LocalName == "CheckBox");
        var caption = panel.Descendants().Single(e =>
            e.Name.LocalName == "TextBlock" && (string)e.Attribute("Text") == "Keep frames for review");

        // The themed ON/OFF pill must never wrap away from the text that says what it does.
        Assert.That(caption.Parent, Is.EqualTo(checkBox.Parent));
        Assert.That(caption.Parent, Is.Not.EqualTo(panel), "they must be grouped in one child, not two");
    }

    [Test]
    public void FooterRow_HasNoWrappingText() {
        var panel = DockableTemplate().Descendants()
            .Single(e => e.Name.LocalName == "EdgeJustifiedWrapPanel");

        // A wrapping caption inside a Height="Auto" row grows the row without bound as the pane narrows — the text
        // degenerates towards one word per line and the space comes straight out of the focus chart above.
        foreach (var text in panel.Descendants().Where(e => e.Name.LocalName == "TextBlock")) {
            Assert.That(text.Attribute("TextWrapping"), Is.Null,
                $"TextWrapping on \"{(string)text.Attribute("Text")}\" makes the footer's height unbounded.");
        }
    }
}
