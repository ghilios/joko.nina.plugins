using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
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

    private static XElement DockableTemplate() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("Joko.NINA.Plugins.HocusFocus").Length == 0) {
            dir = dir.Parent;
        }
        Assert.That(dir, Is.Not.Null, "Could not locate the plugin source directory from the test binaries.");

        var xaml = Path.Combine(dir.GetDirectories("Joko.NINA.Plugins.HocusFocus").Single().FullName,
            "AutoFocus", "DataTemplates.xaml");
        Assert.That(File.Exists(xaml), Is.True, $"{xaml} not found");

        var template = XDocument.Load(xaml).Descendants(Presentation + "DataTemplate")
            .SingleOrDefault(t => ((string)t.Attribute(Xaml + "Key"))?.EndsWith("HocusFocusVM_Dockable",
                StringComparison.Ordinal) == true);
        Assert.That(template, Is.Not.Null, "The AutoFocus pane's _Dockable DataTemplate is gone or was renamed.");
        return template;
    }

    /// <summary>Real child elements. Property elements — <c>&lt;Grid.RowDefinitions&gt;</c> and friends — are not children.</summary>
    private static XElement[] ElementChildren(XElement e) =>
        e.Elements().Where(c => !c.Name.LocalName.Contains('.')).ToArray();

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

            // WPF margins do not collapse, so a horizontal child margin ADDS to ItemGap rather than absorbing into it.
            foreach (var child in ElementChildren(panel)) {
                var margin = (string)child.Attribute("Margin");
                if (margin != null) {
                    var parts = margin.Split(',');
                    Assert.That(parts[0].Trim(), Is.EqualTo("0"), $"left margin on <{child.Name.LocalName}> doubles ItemGap");
                    if (parts.Length == 4) {
                        Assert.That(parts[2].Trim(), Is.EqualTo("0"), $"right margin on <{child.Name.LocalName}> doubles ItemGap");
                    }
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
