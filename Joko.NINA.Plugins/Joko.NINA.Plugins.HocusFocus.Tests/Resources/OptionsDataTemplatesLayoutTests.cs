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
    }
}
