using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Resources;

/// <summary>
/// Guards against setting <c>Padding</c> on a <c>Button</c> that renders with NINA's <c>StandardButton</c>
/// template, where WPF silently discards it.
///
/// <para><b>Why this test exists.</b> NINA's button style (decompiled from <c>NINA.WPF.Base</c>,
/// <c>resources/styles/button.baml</c>) has <b>no <c>Padding</c> TemplateBinding</b> anywhere in its
/// <c>ControlTemplate</c> — the template's <c>ContentPresenter</c> is hard-coded <c>Margin="0,0,0,0"</c> — and the
/// style is applied to <i>every</i> Button implicitly, via
/// <c>&lt;Style x:Key="{x:Type Button}" BasedOn="{StaticResource StandardButton}" /&gt;</c>. So
/// <c>&lt;Button Padding="12,6" Content="Cancel" /&gt;</c> compiles, binds and runs with no error, and the padding
/// simply never appears. The tilt adapter simulator shipped ten buttons that way and rendered its text jammed
/// against the button edges. Put the padding on the <i>content</i> instead —
/// <c>&lt;Button&gt;&lt;TextBlock Margin="12,6" Text="Cancel" /&gt;&lt;/Button&gt;</c> — as ~16 buttons in this
/// plugin already do. (Writing <c>Style="{StaticResource StandardButton}"</c> explicitly is a no-op for the same
/// reason: that style is already the implicit one.)
/// </para>
///
/// <para><b>Same family as <see cref="CameraSimulator.XamlResourceResolutionTests"/>.</b> Both guard a WPF
/// failure that is <i>silent</i>: there, a missing <c>{StaticResource}</c> throws at parse time into a
/// <c>WindowService</c> try/catch, so the only symptom is a window that never opens; here, a property is
/// discarded with no error at all. Neither is caught by the compiler, and neither surfaces as anything more
/// actionable than "it looks wrong". A reader who hits one should know about the other.</para>
///
/// <para><b>Padding is not universally wrong on a Button</b> — only under NINA's template. A style that supplies
/// its own <c>ControlTemplate</c> containing <c>{TemplateBinding Padding}</c> honours it, and this plugin has two
/// such styles (<c>ChoiceButton</c> in the replay prompt, <c>HF_ReviewToolbarButton</c> in the three review
/// controls). Buttons opting into those may legitimately set Padding, so this guard resolves the effective
/// template rather than banning the property outright. Banning it outright would forbid correct code.</para>
///
/// <para><b>Known gaps</b>, both deliberately biased toward a loud failure over a vacuous pass:
/// a style <c>BasedOn</c> a padding-honouring style is not traced through the <c>BasedOn</c> chain, and
/// <c>&lt;Setter Property="Padding"&gt;</c> inside a style that inherits NINA's template is not inspected. The
/// first would report a false offender for a human to adjudicate; the second is not the shape that has ever
/// shipped here. The element-level attribute is what this plugin got wrong eleven times.</para>
/// </summary>
[TestFixture]
public class ButtonPaddingGuardTests {

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    // {StaticResource Key} / {StaticResource ResourceKey=Key}. Deliberately does not match
    // {StaticResource {x:Type Button}} — an implicit-style lookup resolves to StandardButton, which is exactly
    // the template that discards Padding.
    private static readonly Regex StaticResourceKey =
        new Regex(@"\{StaticResource\s+(?:ResourceKey=)?(?<key>[A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);

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

    /// <summary>A style honours Padding iff it supplies a ControlTemplate that template-binds Padding.</summary>
    private static bool SuppliesAPaddingHonouringTemplate(XElement styleOrContainer) =>
        styleOrContainer.Descendants(Presentation + "ControlTemplate")
            .SelectMany(t => t.DescendantsAndSelf())
            .SelectMany(e => e.Attributes())
            .Any(a => a.Value.Replace(" ", string.Empty).Contains("{TemplateBindingPadding}"));

    /// <summary>Keyed styles anywhere in the plugin whose own template honours Padding.</summary>
    private static HashSet<string> PaddingHonouringStyleKeys(IReadOnlyList<XDocument> docs) {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var style in docs.SelectMany(d => d.Descendants(Presentation + "Style"))) {
            var key = (string)style.Attribute(Xaml + "Key");
            if (key != null && SuppliesAPaddingHonouringTemplate(style)) {
                keys.Add(key);
            }
        }
        return keys;
    }

    private static bool PaddingIsHonouredFor(XElement button, ISet<string> honouringKeys) {
        // An inline <Button.Style> that carries its own padding-honouring template.
        var inlineStyle = button.Element(Presentation + "Button.Style");
        if (inlineStyle != null && SuppliesAPaddingHonouringTemplate(inlineStyle)) {
            return true;
        }
        // Style="{StaticResource Foo}" where Foo supplies such a template. No Style attribute at all means the
        // implicit {x:Type Button} style — i.e. StandardButton — which does not.
        var styleAttr = (string)button.Attribute("Style");
        if (styleAttr == null) {
            return false;
        }
        var match = StaticResourceKey.Match(styleAttr);
        return match.Success && honouringKeys.Contains(match.Groups["key"].Value);
    }

    [Test]
    public void NoButton_SetsPaddingUnderNinasStandardButtonTemplate() {
        var docs = PluginXamlFiles()
            .Select(f => (file: f, doc: XDocument.Load(f.FullName, LoadOptions.SetLineInfo)))
            .ToList();
        var honouringKeys = PaddingHonouringStyleKeys(docs.Select(d => d.doc).ToList());

        var buttonsScanned = 0;
        var offenders = new List<string>();
        foreach (var (file, doc) in docs) {
            foreach (var button in doc.Descendants(Presentation + "Button")) {
                buttonsScanned++;
                var padding = (string)button.Attribute("Padding");
                if (padding == null || PaddingIsHonouredFor(button, honouringKeys)) {
                    continue;
                }
                var line = ((IXmlLineInfo)button).LineNumber;
                var content = (string)button.Attribute("Content")
                    ?? button.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.'))?.Name.LocalName
                    ?? "(no content)";
                offenders.Add($"  {file.Name}:{line}  Padding=\"{padding}\"  content: {content}");
            }
        }

        // A path walk that silently matched nothing would make this test vacuously green forever.
        Assert.That(buttonsScanned, Is.GreaterThan(20),
            $"Only {buttonsScanned} Buttons were scanned across {docs.Count} XAML files — the scan is broken, " +
            "not the XAML. This guard is worthless if it inspects nothing.");

        Assert.That(offenders, Is.Empty,
            "Padding is set on a Button that renders with NINA's StandardButton template, where WPF SILENTLY\n" +
            "DISCARDS it — the button will render with no padding at all and no error anywhere:\n" +
            string.Join("\n", offenders) + "\n\n" +
            "NINA's Button ControlTemplate (NINA.WPF.Base, resources/styles/button.baml) has no\n" +
            "'{TemplateBinding Padding}' — its ContentPresenter is hard-coded Margin=\"0,0,0,0\" — and that style is\n" +
            "applied to every Button implicitly via <Style x:Key=\"{x:Type Button}\" BasedOn=\"{StaticResource\n" +
            "StandardButton}\" />. Setting Padding on such a Button therefore does nothing.\n\n" +
            "Fix: move the padding onto the content, which is how ~16 buttons in this plugin already do it:\n" +
            "    <Button Command=\"{Binding FooCommand}\">\n" +
            "        <TextBlock Margin=\"12,6\" Text=\"Foo\" />\n" +
            "    </Button>\n\n" +
            "Padding IS honoured by a style that supplies its own ControlTemplate containing\n" +
            "'{TemplateBinding Padding}' (this plugin has ChoiceButton and HF_ReviewToolbarButton). If that is\n" +
            "what you meant, reference one of those styles and this guard will allow it.");
    }
}
