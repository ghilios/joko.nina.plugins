using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Resources {

    // Guards the hand-authored options grids in OptionsDataTemplates.xaml against a whole class of layout bug:
    // every option is one label (Grid.Column="0") plus its editor, and the grids place children by explicit
    // Grid.Row (source order != visual order), so a mistyped/duplicated Grid.Row silently renders two options on
    // top of each other. Regression: "Exclude Saturated Stars From HFR" was added at Grid.Row="35", colliding with
    // "Contamination Sensitivity" already on row 35. This test parses the XAML and fails if any grid has two
    // column-0 children on the same row.
    [TestFixture]
    public class OptionsDataTemplatesLayoutTests {

        private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        private static XDocument LoadOptionsDataTemplates([CallerFilePath] string thisFile = null) {
            // thisFile: <repo>/Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Resources/OptionsDataTemplatesLayoutTests.cs
            var testsProjectDir = Directory.GetParent(Path.GetDirectoryName(thisFile)).FullName;
            var xamlPath = Path.GetFullPath(Path.Combine(
                testsProjectDir, "..", "Joko.NINA.Plugins.HocusFocus", "Resources", "OptionsDataTemplates.xaml"));
            Assert.That(File.Exists(xamlPath), Is.True, $"Could not locate OptionsDataTemplates.xaml at '{xamlPath}'");
            return XDocument.Load(xamlPath);
        }

        [Test]
        public void OptionGrids_NoTwoColumnZeroControlsShareARow() {
            var doc = LoadOptionsDataTemplates();
            var collisions = new List<string>();

            foreach (var grid in doc.Descendants(Presentation + "Grid")) {
                // A Grid that declares neither RowDefinitions nor ColumnDefinitions has exactly ONE cell, so
                // overlapping children are the only thing it can express and are therefore deliberate -- the
                // watermark-over-a-TextBox idiom, for instance. This rule is about the hand-authored OPTION grids,
                // where children are placed by explicit Grid.Row and a duplicated row silently stacks two options.
                if (grid.Element(Presentation + "Grid.RowDefinitions") == null
                    && grid.Element(Presentation + "Grid.ColumnDefinitions") == null) {
                    continue;
                }
                var labelsByRow = new Dictionary<string, List<string>>();
                foreach (var child in grid.Elements()) {
                    // Skip property-element children (e.g. Grid.RowDefinitions, Grid.Resources, Grid.ColumnDefinitions).
                    if (child.Name.LocalName.Contains('.')) {
                        continue;
                    }
                    // Attached properties are unprefixed attributes (no namespace); absent Row/Column default to 0.
                    var column = (string)child.Attribute("Grid.Column") ?? "0";
                    if (column != "0") {
                        continue;
                    }
                    var row = (string)child.Attribute("Grid.Row") ?? "0";
                    var label = (string)child.Attribute("Text") ?? $"<{child.Name.LocalName}>";
                    if (!labelsByRow.TryGetValue(row, out var list)) {
                        list = new List<string>();
                        labelsByRow[row] = list;
                    }
                    list.Add(label);
                }

                collisions.AddRange(labelsByRow
                    .Where(kv => kv.Value.Count > 1)
                    .Select(kv => $"Grid.Row={kv.Key}: {string.Join(" | ", kv.Value)}"));
            }

            Assert.That(collisions, Is.Empty,
                "Two column-0 controls share a grid row, so they render on top of each other:\n" +
                string.Join("\n", collisions));
        }

        // Guards against the UI-thread livelock fixed after the per-filter "active filter" warning was added: a
        // wrapping TextBlock placed as a direct cell of a Grid whose columns use SharedSizeGroup, and spanning more
        // than one column, does not converge its measure when UI Automation forces a re-measure (the shared column
        // widths are negotiated across every grid in the scope). An accessibility/automation client walking the tree
        // then pegs the UI thread at 100% CPU and the window hangs. Keep wrapping messages out of shared-size spanned
        // cells (put them in their own bounded-width element outside the shared-size grid).
        [Test]
        public void OptionGrids_NoWrappingTextBlockSpansSharedSizeColumns() {
            var doc = LoadOptionsDataTemplates();
            var offenders = new List<string>();

            foreach (var grid in doc.Descendants(Presentation + "Grid")) {
                var colDefs = grid.Element(Presentation + "Grid.ColumnDefinitions");
                var hasSharedSizeColumns = colDefs != null && colDefs
                    .Elements(Presentation + "ColumnDefinition")
                    .Any(cd => (string)cd.Attribute("SharedSizeGroup") != null);
                if (!hasSharedSizeColumns) {
                    continue;
                }
                foreach (var textBlock in grid.Elements(Presentation + "TextBlock")) {
                    var wrapping = (string)textBlock.Attribute("TextWrapping");
                    var isWrapping = wrapping != null && wrapping != "NoWrap";
                    var span = (string)textBlock.Attribute("Grid.ColumnSpan");
                    var spansMultipleColumns = span != null && int.TryParse(span, out var n) && n > 1;
                    if (isWrapping && spansMultipleColumns) {
                        var text = (string)textBlock.Attribute("Text") ?? "<TextBlock>";
                        offenders.Add($"TextBlock Text='{text}' TextWrapping={wrapping} Grid.ColumnSpan={span}");
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "A wrapping TextBlock spans SharedSizeGroup columns as a direct grid cell, which livelocks the UI " +
                "thread under UI Automation (the shared column widths never converge). Move the message out of the " +
                "shared-size grid so it wraps within its own bounded width:\n" + string.Join("\n", offenders));
        }
    }
}
