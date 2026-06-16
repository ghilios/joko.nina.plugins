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
using System.Linq;
using System.Reflection;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// Resolves the plugin's BUNDLED dependency assemblies (e.g. ScottPlot.WPF, ScottPlot) from the plugin's own
    /// directory.
    ///
    /// <para>NINA does not ship ScottPlot (it ships OxyPlot), so those DLLs only exist in the plugin folder — which
    /// is NOT on the default assembly probing path. WPF/BAML resolves a XAML view's xmlns assemblies on demand via
    /// <see cref="Assembly.Load(AssemblyName)"/>; for the bundled DLLs that load fails with a
    /// <see cref="FileNotFoundException"/>, surfacing as a <c>XamlParseException</c> that prevents the view from
    /// rendering (observed on NINA 3.3.0: the Star Detection Optimization Wizard window "just flashes" because its
    /// ScottPlot chart view cannot be parsed). Registering this <see cref="AppDomain.AssemblyResolve"/> handler makes
    /// those on-demand loads succeed by loading the requested assembly from the plugin directory.</para>
    /// </summary>
    public static class BundledAssemblyResolver {
        private static int registered;

        /// <summary>
        /// Registers the <see cref="AppDomain.AssemblyResolve"/> handler exactly once for the given plugin directory.
        /// Safe to call multiple times (subsequent calls are no-ops). Call this from plugin bootstrap, before any
        /// view that references a bundled assembly is shown.
        /// </summary>
        public static void Register(string pluginDirectory) {
            if (string.IsNullOrEmpty(pluginDirectory)) {
                return;
            }
            if (Interlocked.Exchange(ref registered, 1) != 0) {
                return;
            }
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => Resolve(pluginDirectory, args.Name);
        }

        private static Assembly Resolve(string pluginDirectory, string assemblyFullName) {
            var simpleName = SafeSimpleName(assemblyFullName);
            if (simpleName == null) {
                return null;
            }
            // Reuse an already-loaded assembly of the same simple name (avoids duplicate loads and recursion).
            var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => !a.IsDynamic && string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
            if (alreadyLoaded != null) {
                return alreadyLoaded;
            }
            var path = ResolveBundledAssemblyPath(pluginDirectory, assemblyFullName, File.Exists);
            return path != null ? Assembly.LoadFrom(path) : null;
        }

        /// <summary>
        /// Pure mapping from an assembly request to a bundled DLL path under <paramref name="pluginDirectory"/>, or
        /// <c>null</c> when the assembly is not bundled there (so resolution defers to NINA / the runtime). Split out
        /// from <see cref="Resolve"/> so the name-parsing + path logic is unit-testable without touching the AppDomain.
        /// </summary>
        public static string ResolveBundledAssemblyPath(string pluginDirectory, string assemblyFullName, Func<string, bool> fileExists) {
            if (string.IsNullOrEmpty(pluginDirectory) || fileExists == null) {
                return null;
            }
            var simpleName = SafeSimpleName(assemblyFullName);
            if (simpleName == null) {
                return null;
            }
            var candidate = Path.Combine(pluginDirectory, simpleName + ".dll");
            return fileExists(candidate) ? candidate : null;
        }

        /// <summary>Extracts the simple name from an assembly full name, or null if it is empty/unparseable.</summary>
        private static string SafeSimpleName(string assemblyFullName) {
            if (string.IsNullOrEmpty(assemblyFullName)) {
                return null;
            }
            try {
                var name = new AssemblyName(assemblyFullName).Name;
                return string.IsNullOrEmpty(name) ? null : name;
            } catch {
                return null;
            }
        }
    }
}
