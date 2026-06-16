#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.IO;
using NUnit.Framework;
using NINA.Joko.Plugins.HocusFocus.Utility;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class BundledAssemblyResolverTests {

    private const string PluginDir = @"C:\Users\me\AppData\Local\NINA\Plugins\3.0.0\Hocus Focus";

    [Test]
    public void ResolveBundledAssemblyPath_BundledAssembly_ReturnsDllPathInPluginDir() {
        // The exact failing request from the NINA log: ScottPlot.WPF with culture + public key token, no Version.
        const string request = "ScottPlot.WPF, Culture=neutral, PublicKeyToken=e53b06131e34a3aa";
        var expected = Path.Combine(PluginDir, "ScottPlot.WPF.dll");

        var path = BundledAssemblyResolver.ResolveBundledAssemblyPath(PluginDir, request, p => p == expected);

        Assert.That(path, Is.EqualTo(expected));
    }

    [Test]
    public void ResolveBundledAssemblyPath_FullySpecifiedName_UsesSimpleNamePlusDll() {
        const string request = "ScottPlot, Version=4.1.59.0, Culture=neutral, PublicKeyToken=e53b06131e34a3aa";
        var expected = Path.Combine(PluginDir, "ScottPlot.dll");

        var path = BundledAssemblyResolver.ResolveBundledAssemblyPath(PluginDir, request, p => p == expected);

        Assert.That(path, Is.EqualTo(expected));
    }

    [Test]
    public void ResolveBundledAssemblyPath_NotBundled_ReturnsNull() {
        // A framework/NINA-provided assembly that is NOT in the plugin folder must defer (null) to default resolution.
        var path = BundledAssemblyResolver.ResolveBundledAssemblyPath(
            PluginDir, "System.Drawing.Common, Version=8.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51", _ => false);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void ResolveBundledAssemblyPath_ResourceSatelliteAssembly_DefersWhenAbsent() {
        // Satellite resource probes (e.g. "ScottPlot.WPF.resources") must not resolve to the main DLL.
        var probed = (string)null;
        var path = BundledAssemblyResolver.ResolveBundledAssemblyPath(
            PluginDir, "ScottPlot.WPF.resources, Culture=de, PublicKeyToken=e53b06131e34a3aa",
            p => { probed = p; return false; });

        Assert.Multiple(() => {
            Assert.That(path, Is.Null);
            Assert.That(probed, Is.EqualTo(Path.Combine(PluginDir, "ScottPlot.WPF.resources.dll")));
        });
    }

    [Test]
    public void ResolveBundledAssemblyPath_GarbledOrEmptyInputs_ReturnNull() {
        Assert.Multiple(() => {
            Assert.That(BundledAssemblyResolver.ResolveBundledAssemblyPath(PluginDir, "", p => true), Is.Null);
            Assert.That(BundledAssemblyResolver.ResolveBundledAssemblyPath(PluginDir, null, p => true), Is.Null);
            Assert.That(BundledAssemblyResolver.ResolveBundledAssemblyPath("", "ScottPlot.WPF", p => true), Is.Null);
            Assert.That(BundledAssemblyResolver.ResolveBundledAssemblyPath(PluginDir, "ScottPlot.WPF", null), Is.Null);
        });
    }
}
