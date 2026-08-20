using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Resources {

    /// <summary>
    /// A <c>Button</c>'s label must bring its own foreground, or it renders invisible on some NINA themes.
    ///
    /// <para>NINA.WPF.Base ships a KEYLESS <c>TextBlock</c> style (Resources/Styles/TextBlock.xaml, BasedOn
    /// StandardTextBlock ⇒ <c>Foreground = PrimaryBrush</c>) in <c>Application.Resources</c>, and an implicit
    /// style setter outranks an inherited value. Its <c>StandardButton</c> style then sets
    /// <c>Foreground = ButtonBackgroundBrush</c> on purpose — NINA's own buttons carry <c>Path</c> icons that
    /// supply <c>ButtonForegroundBrush</c> themselves. So any label that does not name a foreground paints
    /// <c>PrimaryBrush</c> on <c>ButtonBackgroundBrush</c>, and on the "Dark" and "Alternative Custom" schemas
    /// those two are the SAME colour (#FF550C18): the label renders at 1.00:1, literally invisible.</para>
    ///
    /// <para>It bites in two forms, and this fixture guards both:</para>
    /// <list type="number">
    ///   <item>an explicit <c>TextBlock</c> child, which needs its own <c>Foreground</c>;</item>
    ///   <item><c>Button Content="…"</c> with no TextBlock in the markup at all — <c>ContentPresenter</c>
    ///   generates one at runtime and it hits the identical rule. Setting <c>Foreground</c> on the Button does
    ///   NOT fix that form (a style setter still outranks the inherited value); it takes a Style whose template
    ///   puts an empty <c>&lt;Style TargetType="TextBlock" /&gt;</c> in <c>ContentPresenter.Resources</c>, which
    ///   wins the implicit-style lookup and, carrying no Foreground of its own, lets the inherited value through
    ///   again.</item>
    /// </list>
    ///
    /// <para>Neither the compiler nor <c>XamlResourceResolutionTests</c> can see this: every resource key
    /// involved resolves perfectly. It is a foreground-vs-background contrast bug, so only a rule about the
    /// markup itself catches it — which is what this fixture is. See <c>.claude/docs/wpf-xaml.md</c>.</para>
    /// </summary>
    [TestFixture]
    public class ButtonContentForegroundGuardTests {

        private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        /// <summary>
        /// Styles whose template is known to give a string <c>Content</c> a readable foreground, each via the
        /// <c>ContentPresenter.Resources</c> empty-Style pattern described above. Adding a key here is a claim
        /// about that style's template — check it before you add one.
        /// </summary>
        private static readonly HashSet<string> ContentSafeButtonStyles = new HashSet<string>(StringComparer.Ordinal) {
            // Theme-following: NINA's StandardButton look with Foreground pinned to ButtonForegroundBrush in
            // every visual state (StarDetection/Optimization/DataTemplates.xaml).
            "HF_TextButton",
            // Self-contained fixed light toolbar button on the review panels' fixed dark chrome
            // (StarReviewControl / FrameReviewControl / AutoFocusFrameReviewControl, one copy each).
            "HF_ReviewToolbarButton",
        };

        // A Content attribute that starts with '{' is a markup extension (a Binding, a StaticResource, a
        // template) — not a string, so no TextBlock is generated from it and the trap does not apply.
        private static bool IsLiteralString(string content) =>
            content != null && !content.TrimStart().StartsWith("{", StringComparison.Ordinal);

        private static readonly Regex StaticResourceKey =
            new Regex(@"\{StaticResource\s+(?:ResourceKey=)?(?<key>[A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);

        private static IEnumerable<FileInfo> PluginXamlFiles() {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Joko.NINA.Plugins.HocusFocus"))) {
                dir = dir.Parent;
            }
            Assert.That(dir, Is.Not.Null, "Could not locate the plugin project directory from the test output path");
            return new DirectoryInfo(Path.Combine(dir.FullName, "Joko.NINA.Plugins.HocusFocus"))
                .EnumerateFiles("*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        }

        /// <summary>The style keys a Button element names: the Style attribute, plus the BasedOn of an inline
        /// &lt;Button.Style&gt; (the form the optimizer wizard's Close button uses to add visibility triggers).</summary>
        private static IEnumerable<string> StyleKeysOf(XElement button) {
            var attribute = (string)button.Attribute("Style");
            if (attribute != null) {
                foreach (Match m in StaticResourceKey.Matches(attribute)) yield return m.Groups["key"].Value;
            }
            foreach (var inline in button.Elements(Wpf + "Button.Style").Elements(Wpf + "Style")) {
                var basedOn = (string)inline.Attribute("BasedOn");
                if (basedOn == null) continue;
                foreach (Match m in StaticResourceKey.Matches(basedOn)) yield return m.Groups["key"].Value;
            }
        }

        [Test]
        public void EveryStringContentButton_UsesAStyleThatGivesItsLabelAForeground() {
            var offenders = new List<string>();
            var buttonsChecked = 0;

            foreach (var file in PluginXamlFiles()) {
                var doc = XDocument.Load(file.FullName, LoadOptions.SetLineInfo);
                foreach (var button in doc.Descendants(Wpf + "Button")) {
                    var content = (string)button.Attribute("Content");
                    if (!IsLiteralString(content)) continue;
                    buttonsChecked++;
                    if (StyleKeysOf(button).Any(ContentSafeButtonStyles.Contains)) continue;
                    var line = ((System.Xml.IXmlLineInfo)button).LineNumber;
                    offenders.Add($"{file.Name}:{line}  Content=\"{content}\"");
                }
            }

            Assert.That(buttonsChecked, Is.GreaterThan(0),
                "Found no string-Content buttons at all — the scan is broken, not the XAML.");
            Assert.That(offenders, Is.Empty,
                "A Button with a string Content renders its label through a ContentPresenter-generated TextBlock, "
                + "which picks up NINA's keyless TextBlock style (Foreground = PrimaryBrush) over the button's own "
                + "foreground — invisible on the Dark and Alternative Custom schemas, where PrimaryBrush and "
                + "ButtonBackgroundBrush are the same colour. Give it one of "
                + string.Join(" / ", ContentSafeButtonStyles) + ", or use an explicit <TextBlock Foreground=\"...\"> "
                + "child instead of Content. Offenders:" + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
        }

        [Test]
        public void EveryTextBlockUsedAsButtonContent_NamesItsOwnForeground() {
            var offenders = new List<string>();
            var labelsChecked = 0;

            foreach (var file in PluginXamlFiles()) {
                var doc = XDocument.Load(file.FullName, LoadOptions.SetLineInfo);
                foreach (var button in doc.Descendants(Wpf + "Button")) {
                    // Both spellings of "this TextBlock is the button's content": a direct child, and the
                    // explicit <Button.Content> property element.
                    var labels = button.Elements(Wpf + "TextBlock")
                        .Concat(button.Elements(Wpf + "Button.Content").Elements(Wpf + "TextBlock"));
                    foreach (var label in labels) {
                        labelsChecked++;
                        if (label.Attribute("Foreground") != null || label.Attribute("Style") != null) continue;
                        var line = ((System.Xml.IXmlLineInfo)label).LineNumber;
                        offenders.Add($"{file.Name}:{line}  Text=\"{(string)label.Attribute("Text")}\"");
                    }
                }
            }

            Assert.That(labelsChecked, Is.GreaterThan(0),
                "Found no button-content TextBlocks at all — the scan is broken, not the XAML.");
            Assert.That(offenders, Is.Empty,
                "A TextBlock used as a Button's content does NOT inherit the button's foreground: NINA's keyless "
                + "TextBlock style (Foreground = PrimaryBrush) outranks the inherited value, and on the Dark and "
                + "Alternative Custom schemas PrimaryBrush equals ButtonBackgroundBrush. Set "
                + "Foreground=\"{StaticResource ButtonForegroundBrush}\" (or the control's own local foreground key, "
                + "as TiltDeviceAdjustmentPromptControl.xaml does). Offenders:" + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
        }
    }
}
