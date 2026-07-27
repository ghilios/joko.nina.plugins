using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Resources {

    /// <summary>
    /// Every XML namespace prefix a plugin XAML file USES must also be DECLARED in that file.
    ///
    /// <para>This is not something the build catches. A prefix used only inside a markup-extension argument —
    /// <c>{Binding Source={util:EnumBindingSource {x:Type interfaces:SomeEnum}}}</c> — compiles clean with the
    /// prefix undeclared, and then throws <c>XamlParseException</c> the first time the ResourceDictionary is
    /// loaded, which in NINA means the options page or a wizard simply fails to open. Prefixes do NOT inherit
    /// through <c>MergedDictionaries</c>, so copying a working row from one file into another reproduces this
    /// exactly. Caught here instead.</para>
    /// </summary>
    [TestFixture]
    public class XamlNamespacePrefixGuardTests {

        // Prefixes WPF/XAML supply itself, which no file declares.
        private static readonly HashSet<string> BuiltInPrefixes = new HashSet<string>(StringComparer.Ordinal) {
            "xml", "xmlns"
        };

        // The two places XAML actually resolves a prefix inside an attribute VALUE: the markup extension's own
        // name ({util:EnumBindingSource ...}) and a type/member reference inside one ({x:Type interfaces:Foo}).
        // Deliberately narrow — a blanket "word:word" sweep also matches format strings like StringFormat=HH:mm,
        // which are not prefixed names at all. Element and attribute prefixes are covered structurally by
        // XDocument, so only attribute values need this.
        private static readonly Regex PrefixedNameInValue =
            new Regex(@"\{\s*(?:x:(?:Type|Static)\s+)?(?<prefix>[A-Za-z_][\w.]*):(?<name>[A-Za-z_]\w*)", RegexOptions.Compiled);

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

        [Test]
        public void EveryUsedXmlNamespacePrefixIsDeclaredInItsOwnFile() {
            var files = PluginXamlFiles().ToList();
            var filesScanned = 0;
            var valuesScanned = 0;
            var offenders = new List<string>();

            foreach (var file in files) {
                var doc = XDocument.Load(file.FullName, LoadOptions.SetLineInfo);
                filesScanned++;

                foreach (var element in doc.Descendants()) {
                    foreach (var attribute in element.Attributes()) {
                        if (attribute.IsNamespaceDeclaration) {
                            continue;
                        }
                        valuesScanned++;
                        foreach (Match match in PrefixedNameInValue.Matches(attribute.Value)) {
                            var prefix = match.Groups["prefix"].Value;
                            if (BuiltInPrefixes.Contains(prefix) || element.GetNamespaceOfPrefix(prefix) != null) {
                                continue;
                            }
                            var line = ((IXmlLineInfo)attribute).LineNumber;
                            offenders.Add($"  {file.Name}:{line}  {attribute.Name.LocalName}=\"...{match.Value}...\"  (prefix '{prefix}' is not declared in this file)");
                        }
                    }
                }
            }

            // A path walk that silently matched nothing would make this test vacuously green forever.
            Assert.That(filesScanned, Is.GreaterThan(5), $"Only {filesScanned} XAML files were scanned — the scan is broken, not the XAML.");
            Assert.That(valuesScanned, Is.GreaterThan(500), $"Only {valuesScanned} attribute values were scanned — the scan is broken, not the XAML.");

            Assert.That(offenders.Distinct(), Is.Empty,
                "A XAML file uses an xmlns prefix it does not declare. This COMPILES CLEAN and throws\n" +
                "XamlParseException when the dictionary is loaded at runtime:\n" +
                string.Join("\n", offenders.Distinct()) + "\n\n" +
                "Fix: add the xmlns declaration to the file's root element. Prefixes are NOT inherited through\n" +
                "ResourceDictionary.MergedDictionaries, so every file that uses one must declare it itself.");
        }
    }
}
