using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Resources;

/// <summary>
/// Guards against a range <see cref="System.Windows.Controls.ValidationRule"/> written in model units behind a
/// <c>PercentageConverter</c>, which makes the option impossible to edit.
///
/// <para><b>Why this test exists.</b> A percent option is a text box showing <c>value × 100</c>
/// (<c>Unit="%"</c>, <c>Converter="{StaticResource PercentageConverter}"</c>). WPF runs a binding's
/// <c>ValidationRules</c> at <c>ValidationStep.RawProposedValue</c> — on the <i>string the user typed</i>, before
/// <c>ConvertBack</c> divides it by 100 — so those ranges are in <b>percent</b> units, not model units.
/// "Outlier Rejection Confidence" shipped with <c>DoubleRangeChecker Minimum="0.5001" Maximum="0.9999"</c>, the
/// model-space bound of <c>AutoFocusOptions.OutlierRejectionConfidence</c>. Typing the intended 90 was rejected as
/// out of range, and the only accepted entries (under 1) came back through <c>ConvertBack</c> as ~0.009, which the
/// option setter threw on — an exception the binding engine swallows because the binding does not set
/// <c>ValidatesOnExceptions</c>. Net effect: the field could not be changed at all, with no error beyond a red
/// border on the values that were in fact valid.</para>
///
/// <para><b>The discriminator is the maximum.</b> Every percent range in this plugin caps at 100; a maximum of
/// 1.0 or less is the fingerprint of a fraction-space bound that escaped the ×100. A field whose legitimate cap is
/// genuinely ≤ 1 % would be a false offender here — adjust this guard then, and do so knowing it is the shape of
/// the only bug it exists to catch.</para>
/// </summary>
[TestFixture]
public class PercentBindingRangeTests {

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static DirectoryInfo PluginSourceDirectory() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("Joko.NINA.Plugins.HocusFocus").Length == 0) {
            dir = dir.Parent;
        }
        Assert.That(dir, Is.Not.Null, "Could not locate the plugin source directory from the test binaries.");
        return dir.GetDirectories("Joko.NINA.Plugins.HocusFocus").Single();
    }

    private static FileInfo[] PluginXamlFiles() {
        var files = PluginSourceDirectory().GetFiles("*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToArray();
        Assert.That(files, Is.Not.Empty, "Found no XAML to scan — the path walk is wrong, not the XAML.");
        return files;
    }

    /// <summary>PercentageConverter and FloatPercentageConverter both scale by 100 for display.</summary>
    private static bool ScalesByOneHundred(XElement binding) =>
        ((string)binding.Attribute("Converter"))?.Contains("PercentageConverter", StringComparison.Ordinal) == true;

    [Test]
    public void PercentBindings_ValidateInPercentUnitsNotModelUnits() {
        var checkersScanned = 0;
        var offenders = new List<string>();

        foreach (var file in PluginXamlFiles()) {
            var doc = XDocument.Load(file.FullName, LoadOptions.SetLineInfo);
            foreach (var binding in doc.Descendants(Presentation + "Binding").Where(ScalesByOneHundred)) {
                // DoubleRangeChecker / FloatRangeChecker / IntRangeChecker, whatever the rule wraps them in.
                foreach (var checker in binding.Descendants().Where(e => e.Name.LocalName.EndsWith("RangeChecker", StringComparison.Ordinal))) {
                    var maximum = (string)checker.Attribute("Maximum");
                    if (maximum == null || !double.TryParse(maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var max)) {
                        continue;
                    }
                    checkersScanned++;
                    if (max > 1.0) {
                        continue;
                    }
                    var line = ((IXmlLineInfo)checker).LineNumber;
                    var path = (string)binding.Attribute("Path") ?? "(no Path)";
                    var minimum = (string)checker.Attribute("Minimum") ?? "(none)";
                    offenders.Add($"  {file.Name}:{line}  {path}  Minimum={minimum} Maximum={maximum}");
                }
            }
        }

        // A scan that silently matched nothing would make this guard vacuously green forever.
        Assert.That(checkersScanned, Is.GreaterThan(3),
            $"Only {checkersScanned} range checkers behind a PercentageConverter were scanned — the scan is " +
            "broken, not the XAML. This guard is worthless if it inspects nothing.");

        Assert.That(offenders, Is.Empty,
            "A range ValidationRule behind a PercentageConverter is written in model units, so the option CANNOT\n" +
            "BE EDITED — the text box shows value × 100, but the rule rejects anything outside the fraction range:\n" +
            string.Join("\n", offenders) + "\n\n" +
            "WPF runs ValidationRules at ValidationStep.RawProposedValue — on the typed string, BEFORE\n" +
            "PercentageConverter.ConvertBack divides by 100 — so the bound belongs in percent units.\n\n" +
            "Fix: multiply the bound by 100, e.g. a model range of (0.5, 1.0) exclusive becomes\n" +
            "    <rules:DoubleRangeChecker Maximum=\"99.99\" Minimum=\"50.01\" />\n" +
            "matching every other percent option in OptionsDataTemplates.xaml, which all cap at 100.");
    }
}
