using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

/// <summary>
/// Guards against referencing a XAML resource key that does not resolve — any key, not just brushes.
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
/// <para><b>The rule is the merge graph, not a global key union.</b> A <c>{StaticResource K}</c> in file F
/// resolves iff K is in (F's own <c>x:Key</c>s) ∪ (the transitive closure of F's
/// <c>ResourceDictionary.MergedDictionaries</c>) ∪ (NINA's compiled resources). A global union across every
/// plugin dictionary would be wrong, and would miss the load-order bug this codebase has already paid for:
/// <c>SetupDataTemplates.xaml</c> and <c>TiltAdapterDataTemplates.xaml</c> merge *nothing*, so a
/// <c>StaticResource</c> reaching into <c>OptionsDataTemplates.xaml</c> would be a race — plugin
/// ResourceDictionaries are exported separately and merged into <c>Application.Current.Resources</c> in no
/// guaranteed order. Those files therefore declare their own converters locally, and this rule enforces that.</para>
///
/// <para><b><c>DynamicResource</c> is deliberately never checked.</b> It resolves at render time, once every
/// plugin dictionary is merged, and is the sanctioned escape hatch for the intentional cross-dictionary
/// references (<c>HocusFocus_SimTiltAdapter_Panel</c>, <c>HF_TiltScrewDiagram</c>, <c>FontTemplate</c>). That
/// division of labour — <c>StaticResource</c> stays in scope, <c>DynamicResource</c> crosses dictionaries — is
/// the design, not an oversight.</para>
///
/// <para><b>Known gap:</b> <c>{StaticResource {x:Type Button}}</c> (implicit style lookups) cannot be validated
/// without a live WPF application context, so those are skipped by form. Every other <c>{StaticResource</c>
/// occurrence must parse as a key, or <see cref="EveryStaticResourceReference_IsAKnownForm"/> fails — otherwise
/// an unrecognised syntax would silently under-scan and leave this guard vacuously green.</para>
/// </summary>
[TestFixture]
public class XamlResourceResolutionTests {

    // NINA's theme brushes, converters, styles and SVG geometries are declared in BAML inside these assemblies,
    // which sit next to the test binaries via the plugin ProjectReference. CustomControlLibrary is included
    // because ninactrl: controls are used throughout and ship their own generic themes.
    private static readonly string[] NinaAssemblies = {
        "NINA.WPF.Base.dll", "NINA.Core.dll", "NINA.Sequencer.dll", "NINA.Equipment.dll",
        "NINA.CustomControlLibrary.dll"
    };

    // Both the plain and the verbose (ResourceKey=) form. The charset allows dots: keys like
    // {StaticResource NINA.Joko.Plugins.HocusFocus.AutoFocus.HocusFocusVM_Dockable} are real, and were silently
    // skipped by the previous [A-Za-z0-9_]+ pattern.
    private static readonly Regex StaticResourceKey =
        new Regex(@"\{StaticResource\s+(?:ResourceKey=)?(?<key>[A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);

    // Implicit style lookup — unverifiable without a WPF app context. The one whitelisted form.
    private static readonly Regex StaticResourceXType =
        new Regex(@"\{StaticResource\s+\{x:Type\s+[^}]*\}\s*\}", RegexOptions.Compiled);

    private static readonly Regex AnyStaticResource = new Regex(@"\{StaticResource", RegexOptions.Compiled);
    private static readonly Regex OwnKey = new Regex(@"x:Key=""(?<key>[^""]+)""", RegexOptions.Compiled);

    // Matches <ResourceDictionary Source="..."/> and <wpfutil:SharedResourceDictionary Source="..."/>.
    private static readonly Regex MergedSource =
        new Regex(@"<(?:\w+:)?(?:Shared)?ResourceDictionary\s+Source=""(?<src>[^""]+)""", RegexOptions.Compiled);

    // A BAML string is length-prefixed, so requiring the length byte in front of the key makes the blob scan a
    // near-exact test rather than a loose substring one — it rejects "Xy", "Foo" and "Style", which a bare
    // Contains() accepts. It is not sufficient alone: very short keys still collide ("Fg", "Text" and "Brush"
    // survive even the prefixed test, because they are genuine BAML strings elsewhere). Hence the length floor.
    // Together they give zero false positives across all 312 distinct keys the plugin references; the shortest
    // real ones are StarSVG/DotsSVG at 7. A shorter key that misses is a LOUD failure, which is the safe
    // direction — a human then decides, rather than a coincidence silently passing it.
    private const int MinBlobScanKeyLength = 7;

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

    /// <summary>
    /// The resolution model: per-file scope from the merge graph, plus NINA's compiled resources. Exposed as a
    /// type so the discrimination tests can ask it about synthetic XAML attributed to a real file path — a
    /// checker that is never shown to fail is worthless.
    /// </summary>
    private sealed class ResourceIndex {
        private readonly Dictionary<string, string> textByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> ownKeysByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> ninaBlobs;

        public ResourceIndex() {
            foreach (var file in PluginXamlFiles()) {
                var text = File.ReadAllText(file.FullName);
                textByPath[file.FullName] = text;
                ownKeysByPath[file.FullName] =
                    new HashSet<string>(OwnKey.Matches(text).Select(m => m.Groups["key"].Value), StringComparer.Ordinal);
            }

            // Read once as latin1 so a byte-for-byte substring search is meaningful.
            ninaBlobs = NinaAssemblies
                .Select(name => Path.Combine(AppContext.BaseDirectory, name))
                .Where(File.Exists)
                .Select(path => Encoding.Latin1.GetString(File.ReadAllBytes(path)))
                .ToList();
            Assert.That(ninaBlobs, Is.Not.Empty, "No NINA assemblies next to the test binaries — the scan would vacuously pass.");
        }

        public IEnumerable<string> Paths => textByPath.Keys;

        public string TextOf(string path) => textByPath[path];

        /// <summary>Keys visible to <paramref name="path"/>: its own, plus every dictionary it transitively merges.</summary>
        private HashSet<string> ScopeOf(string path) {
            var scope = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Walk(string current) {
                if (!seen.Add(current) || !textByPath.TryGetValue(current, out var text)) { return; }
                scope.UnionWith(ownKeysByPath[current]);
                foreach (Match m in MergedSource.Matches(text)) {
                    var src = m.Groups["src"].Value;
                    // A leading '/' is a pack URI into a NINA assembly (DesignTimeResources.xaml). Not a file we
                    // own; those keys are covered by the blob scan instead.
                    if (src.StartsWith("/", StringComparison.Ordinal)) { continue; }
                    Walk(Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(current)!, src.Replace('/', Path.DirectorySeparatorChar))));
                }
            }

            Walk(path);
            return scope;
        }

        private bool NinaDefines(string key) {
            if (key.Length < MinBlobScanKeyLength) { return false; }
            // BAML length-prefixes strings shorter than 128 chars with a single byte.
            var needle = key.Length < 128 ? (char)key.Length + key : key;
            return ninaBlobs.Any(blob => blob.Contains(needle, StringComparison.Ordinal));
        }

        private static bool IsXTypeFormAt(string text, int index) {
            var m = StaticResourceXType.Match(text, index);
            return m.Success && m.Index == index;
        }

        /// <summary>Every <c>{StaticResource}</c> key in <paramref name="text"/> that would not resolve from <paramref name="path"/>.</summary>
        public List<string> Unresolved(string path, string text) {
            var scope = ScopeOf(path);
            var unresolved = new List<string>();
            foreach (Match m in StaticResourceKey.Matches(text)) {
                if (IsXTypeFormAt(text, m.Index)) { continue; }
                var key = m.Groups["key"].Value;
                if (scope.Contains(key) || NinaDefines(key)) { continue; }
                var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                unresolved.Add($"{Path.GetFileName(path)}:{line} -> {{StaticResource {key}}}");
            }
            return unresolved;
        }

        public bool IsKnownFormAt(string text, int index) {
            var key = StaticResourceKey.Match(text, index);
            return (key.Success && key.Index == index) || IsXTypeFormAt(text, index);
        }
    }

    [Test]
    public void EveryStaticResourceReferencedByOurXaml_ResolvesInItsOwnMergeScope() {
        var index = new ResourceIndex();
        var unresolved = index.Paths.SelectMany(p => index.Unresolved(p, index.TextOf(p))).Distinct().ToList();

        Assert.That(unresolved, Is.Empty,
            "These XAML resource keys do not resolve from the file that references them. StaticResource throws at "
          + "parse time, and inside a WindowService-hosted window that throw is swallowed — the window just never "
          + "opens. A key defined in another plugin dictionary does NOT count unless this file explicitly merges "
          + "it; use DynamicResource to cross dictionaries deliberately:"
          + Environment.NewLine + string.Join(Environment.NewLine, unresolved));
    }

    /// <summary>
    /// Without this, an unrecognised <c>{StaticResource ...}</c> syntax would simply not match the key regex and
    /// be silently skipped, leaving the guard above vacuously green over XAML it never actually checked.
    /// </summary>
    [Test]
    public void EveryStaticResourceReference_IsAKnownForm() {
        var index = new ResourceIndex();
        var unhandled = new List<string>();
        var total = 0;

        foreach (var path in index.Paths) {
            var text = index.TextOf(path);
            foreach (Match m in AnyStaticResource.Matches(text)) {
                total++;
                if (index.IsKnownFormAt(text, m.Index)) { continue; }
                var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                unhandled.Add($"{Path.GetFileName(path)}:{line} -> {text.Substring(m.Index, Math.Min(60, text.Length - m.Index))}");
            }
        }

        Assert.Multiple(() => {
            Assert.That(total, Is.GreaterThan(1000), "Scanned implausibly few StaticResource references — the scan is broken.");
            Assert.That(unhandled, Is.Empty,
                "These StaticResource references match no known form, so the resolution guard skipped them:"
              + Environment.NewLine + string.Join(Environment.NewLine, unhandled));
        });
    }

    // ---- Discrimination: prove the guard actually fails on each thing it claims to catch. ----

    private static string SetupDialogPath() =>
        Path.Combine(PluginSourceDirectory().FullName, "CameraSimulator", "SetupDialog", "SetupDataTemplates.xaml");

    [Test]
    public void Guard_CatchesTheBrushThatActuallyBrokeTheSetupDialog() {
        var unresolved = new ResourceIndex().Unresolved(SetupDialogPath(),
            @"<TextBlock Foreground=""{StaticResource NotificationSuccessBrush}"" />");
        Assert.That(unresolved, Has.Count.EqualTo(1).And.Some.Contains("NotificationSuccessBrush"),
            "NotificationSuccessBrush is the key that silently broke this dialog; the guard must flag it.");
    }

    [Test]
    public void Guard_CatchesAConverterKeyThatDoesNotExist() {
        var unresolved = new ResourceIndex().Unresolved(SetupDialogPath(),
            @"<TextBlock Text=""{Binding X, Converter={StaticResource HF_NoSuchConverter}}"" />");
        Assert.That(unresolved, Has.Count.EqualTo(1).And.Some.Contains("HF_NoSuchConverter"),
            "The old guard only matched *Brush keys, so a bogus converter key went unchecked.");
    }

    [Test]
    public void Guard_FlagsACrossDictionaryReachFromTheSetupDialogIntoOptionsDataTemplates() {
        // HF_DoubleNegativeToVisibilityConverter really is declared — but only in OptionsDataTemplates.xaml,
        // which SetupDataTemplates.xaml does not merge. This is the load-order race the local declarations avoid,
        // and it is precisely what a global key union would fail to catch.
        var unresolved = new ResourceIndex().Unresolved(SetupDialogPath(),
            @"<TextBlock Text=""{Binding X, Converter={StaticResource HF_DoubleNegativeToVisibilityConverter}}"" />");
        Assert.That(unresolved, Has.Count.EqualTo(1).And.Some.Contains("HF_DoubleNegativeToVisibilityConverter"),
            "A key defined only in a dictionary this file does not merge must be flagged — merge graph, not global union.");
    }

    [Test]
    public void Guard_AcceptsAKeyTheFileDeclaresItself() {
        // The converter Task 7 declared locally in SetupDataTemplates.xaml, resolved through the real file.
        var unresolved = new ResourceIndex().Unresolved(SetupDialogPath(),
            @"<TextBlock Text=""{Binding X, Converter={StaticResource HF_DoubleNegativeToEmptyStringConverter}}"" />");
        Assert.That(unresolved, Is.Empty, "A locally declared key must resolve — otherwise the guard is a false alarm.");
    }

    [Test]
    public void Guard_AcceptsAKeyReachedThroughAnExplicitMerge() {
        // AutoFocus/DataTemplates.xaml explicitly merges ../Resources/OptionsDataTemplates.xaml, so the same
        // reference that is illegal from the setup dialog is legal here.
        var path = Path.Combine(PluginSourceDirectory().FullName, "AutoFocus", "DataTemplates.xaml");
        var unresolved = new ResourceIndex().Unresolved(path,
            @"<TextBlock Text=""{Binding X, Converter={StaticResource HF_DoubleNegativeToVisibilityConverter}}"" />");
        Assert.That(unresolved, Is.Empty, "An explicitly merged dictionary's keys must resolve.");
    }

    [Test]
    public void Guard_DoesNotLetAShortKeyBeRescuedByACoincidentalBlobSubstring() {
        // "Fg" is literally present in NINA.Core.dll, so a bare Contains() would "resolve" it. It is not a key.
        var unresolved = new ResourceIndex().Unresolved(SetupDialogPath(), @"<TextBlock Foreground=""{StaticResource Fg}"" />");
        Assert.That(unresolved, Has.Count.EqualTo(1).And.Some.Contains("Fg"),
            "A 2-char key must not be rescued by a coincidental substring hit in a NINA assembly.");
    }

    [Test]
    public void Guard_IgnoresDynamicResource() {
        // The sanctioned cross-dictionary escape hatch. Flagging it would break the tilt adapter panel by design.
        var unresolved = new ResourceIndex().Unresolved(SetupDialogPath(),
            @"<ContentControl ContentTemplate=""{DynamicResource HocusFocus_SimTiltAdapter_Panel}"" />");
        Assert.That(unresolved, Is.Empty, "DynamicResource crosses dictionaries deliberately and must never be flagged.");
    }
}
