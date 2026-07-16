using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

/// <summary>
/// Guards against referencing a NINA theme brush that does not exist.
///
/// Why this test exists: <c>{StaticResource Foo}</c> resolves at XAML *parse* time and throws
/// <c>XamlParseException</c> when the key is missing. For a template hosted in a window created by NINA's
/// <c>WindowService</c>, that throw is caught and merely logged (<c>WindowService.Show</c> wraps
/// <c>GenerateWindow</c> + <c>window.Show()</c> in a try/catch), so the only symptom is **the window silently
/// never appears** — no crash, no message, nothing to click. That is exactly how the camera setup dialog failed:
/// it referenced <c>NotificationSuccessBrush</c>, a plausible-looking name (NINA has <c>NotificationWarningBrush</c>
/// and <c>NotificationErrorBrush</c>) that NINA has never defined. The compiler cannot catch it — BAML happily
/// compiles an unresolvable key — and no unit test touched the XAML, so it reached a human running NINA.
///
/// This scans the plugin's XAML for every <c>{StaticResource *Brush}</c> and asserts each key is actually defined,
/// either by NINA (its theme brushes live in NINA.WPF.Base.dll, which NuGet restores next to this test) or by one
/// of our own dictionaries. It is deliberately whole-plugin rather than CameraSimulator-only: the failure mode is
/// invisible wherever it occurs, and the check costs nothing.
/// </summary>
[TestFixture]
public class XamlBrushResourceTests {

    // NINA's theme brushes are declared in BAML inside these assemblies, which sit next to the test binaries
    // via the plugin ProjectReference. A byte-scan for the key name is crude but exact here: BAML stores
    // resource keys as plain strings, and the check is verified to produce zero false positives across every
    // brush the plugin currently references.
    private static readonly string[] NinaAssemblies = {
        "NINA.WPF.Base.dll", "NINA.Core.dll", "NINA.Sequencer.dll", "NINA.Equipment.dll"
    };

    private static readonly Regex StaticResourceBrush =
        new Regex(@"\{StaticResource\s+(?<key>[A-Za-z0-9_]+Brush)\}", RegexOptions.Compiled);

    private static readonly Regex OwnBrushKey =
        new Regex(@"x:Key=""(?<key>[A-Za-z0-9_]+Brush)""", RegexOptions.Compiled);

    private static DirectoryInfo PluginSourceDirectory() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("Joko.NINA.Plugins.HocusFocus").Length == 0) {
            dir = dir.Parent;
        }
        Assert.That(dir, Is.Not.Null, "Could not locate the plugin source directory from the test binaries.");
        return dir.GetDirectories("Joko.NINA.Plugins.HocusFocus").Single();
    }

    [Test]
    public void EveryStaticResourceBrushReferencedByOurXaml_IsActuallyDefined() {
        var pluginDir = PluginSourceDirectory();
        var xamlFiles = pluginDir.GetFiles("*.xaml", SearchOption.AllDirectories);
        Assert.That(xamlFiles, Is.Not.Empty, "Found no XAML to scan — the path walk is wrong, not the XAML.");

        // Brushes our own dictionaries declare.
        var ownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in xamlFiles) {
            foreach (Match m in OwnBrushKey.Matches(File.ReadAllText(file.FullName))) {
                ownKeys.Add(m.Groups["key"].Value);
            }
        }

        // NINA's brushes, read once as latin1 so byte-for-byte substring search is meaningful.
        var ninaBlobs = NinaAssemblies
            .Select(name => Path.Combine(AppContext.BaseDirectory, name))
            .Where(File.Exists)
            .Select(path => Encoding.Latin1.GetString(File.ReadAllBytes(path)))
            .ToList();
        Assert.That(ninaBlobs, Is.Not.Empty, "No NINA assemblies next to the test binaries — the scan would vacuously pass.");

        var unresolved = new List<string>();
        foreach (var file in xamlFiles) {
            var text = File.ReadAllText(file.FullName);
            foreach (Match m in StaticResourceBrush.Matches(text)) {
                var key = m.Groups["key"].Value;
                if (ownKeys.Contains(key)) { continue; }
                if (ninaBlobs.Any(blob => blob.Contains(key, StringComparison.Ordinal))) { continue; }

                var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                unresolved.Add($"{file.Name}:{line} -> {{StaticResource {key}}}");
            }
        }

        Assert.That(unresolved, Is.Empty,
            "These XAML brush keys are not defined by NINA or by us. StaticResource throws at parse time, and "
          + "inside a WindowService-hosted window that throw is swallowed — the window just never opens:"
          + Environment.NewLine + string.Join(Environment.NewLine, unresolved.Distinct()));
    }
}
