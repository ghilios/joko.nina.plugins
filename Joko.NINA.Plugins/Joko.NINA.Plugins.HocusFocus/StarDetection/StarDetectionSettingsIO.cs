#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Logger = NINA.Core.Utility.Logger;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// UI-thread Import/Export of star-detection parameters to a single JSON file, so settings tuned on a powerful
    /// machine can be carried to a lower-power imaging machine. Shared by the options-tab host (HocusFocusPlugin) and
    /// the dockable StarDetectionOptionsVM. All file I/O is wrapped; nothing throws to the UI.
    /// </summary>
    internal static class StarDetectionSettingsIO {
        private const string FileFilter = "HocusFocus Star Detection Settings (*.json)|*.json";

        /// <summary>Prompts for a destination and writes the current star-detection settings to a JSON file. A
        /// cancelled dialog is a silent no-op.</summary>
        public static void Export(StarDetectionOptions options) {
            if (options == null) {
                return;
            }
            try {
                var dialog = new Microsoft.Win32.SaveFileDialog() {
                    Title = "Export Star Detection Settings",
                    Filter = FileFilter,
                    DefaultExt = ".json",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = $"HocusFocusStarDetection_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.json",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };
                if (dialog.ShowDialog() != true) {
                    return;
                }

                var export = StarDetectionSettingsExport.FromOptions(options, GetEditedFilterName());
                File.WriteAllText(dialog.FileName, export.Serialize());
                Logger.Info($"Exported star detection settings to {dialog.FileName}");
                Notification.ShowInformation($"Exported star detection settings to {Path.GetFileName(dialog.FileName)}");
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to export star detection settings");
                Notification.ShowError($"Failed to export star detection settings: {ex.Message}");
            }
        }

        /// <summary>The filter whose set the edit buffer currently holds, or null when per-filter star detection is
        /// off (or the plugin singletons are absent, as under unit tests). Provenance only; never drives an import.</summary>
        private static string GetEditedFilterName() {
            return HocusFocusPlugin.PerFilterStarDetection?.Enabled == true
                ? HocusFocusPlugin.PerFilterStarDetectionEditBinder?.EditedFilterName
                : null;
        }

        /// <summary>Prompts for a file, validates it, shows the diff-confirmation dialog, and — only on Apply — applies
        /// the imported settings to the active profile. Cancel / corrupt / wrong-type / newer-schema files leave the
        /// current settings untouched.</summary>
        public static async Task ImportAsync(StarDetectionOptions options, IWindowServiceFactory windowServiceFactory) {
            if (options == null) {
                return;
            }
            try {
                // Shown before any await, so this stays on the UI thread.
                var dialog = new Microsoft.Win32.OpenFileDialog() {
                    Title = "Import Star Detection Settings",
                    Filter = FileFilter,
                    DefaultExt = ".json",
                    CheckFileExists = true,
                    Multiselect = false,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };
                if (dialog.ShowDialog() != true) {
                    return;
                }

                if (!StarDetectionSettingsExport.TryLoad(dialog.FileName, out var export, out var error)) {
                    Logger.Warning($"Could not import star detection settings from {dialog.FileName}: {error}");
                    Notification.ShowError($"Could not import star detection settings: {error}");
                    return;
                }

                var diff = StarDetectionSettingsDiff.BuildDiff(options, export.StarDetection);
                if (diff.Count == 0) {
                    Notification.ShowInformation("Imported settings match the current settings; nothing to change.");
                    return;
                }

                var vm = new ImportStarDetectionPreviewVM(diff, BuildSourceSummary(export));
                var apply = await ImportStarDetectionPreview.ShowAsync(windowServiceFactory, vm);
                if (!apply) {
                    return;
                }

                options.ApplyImportedSnapshot(export.StarDetection);
                Logger.Info($"Imported star detection settings from {dialog.FileName} ({diff.Count} setting(s) changed)");
                Notification.ShowInformation($"Imported star detection settings from {Path.GetFileName(dialog.FileName)}");
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to import star detection settings");
                Notification.ShowError($"Failed to import star detection settings: {ex.Message}");
            }
        }

        /// <summary>Copies another filter's star-detection settings onto the current edit buffer (per-filter mode).
        /// Reuses the import diff-preview dialog; a cancelled dialog or an identical source is a no-op. The edit
        /// binder's mirror persists the applied values to the edited filter's stored set.</summary>
        public static Task CopyFromFilterAsync(
                string sourceFilterName,
                IPerFilterStarDetectionStore store,
                StarDetectionOptions options,
                IWindowServiceFactory windowServiceFactory,
                PerFilterEditBinder geometryTarget = null) {
            return CopyFromFilterAsync(sourceFilterName, store, options,
                (rows, summary) => ImportStarDetectionPreview.ShowAsync(windowServiceFactory, new ImportStarDetectionPreviewVM(rows, summary)),
                geometryTarget);
        }

        /// <summary>Delegate-injected core of the copy-from-filter flow (unit-test seam — no WPF dialog).
        /// <paramref name="confirmDiff"/> receives the diff rows plus a provenance line and returns the decision.</summary>
        internal static async Task CopyFromFilterAsync(
                string sourceFilterName,
                IPerFilterStarDetectionStore store,
                StarDetectionOptions options,
                Func<IReadOnlyList<StarDetectionSettingDiffRow>, string, Task<bool>> confirmDiff,
                PerFilterEditBinder geometryTarget = null) {
            if (string.IsNullOrEmpty(sourceFilterName) || store == null || options == null) {
                return;
            }
            try {
                var snapshot = store.GetOrSeedSnapshot(sourceFilterName);
                var diff = new List<StarDetectionSettingDiffRow>(StarDetectionSettingsDiff.BuildDiff(options, snapshot));

                // The sweep geometry is part of the filter's set, and the documented workflow is "tune one
                // narrowband filter with the wizard, then copy the result to the others". Since Accept now writes
                // the geometry into the target filter, a copy that carried only detection settings would reproduce
                // HALF of what the wizard produced -- and the dropped half is the one the user notices on the next
                // focus run. So it travels with them, and is listed in the confirmation like everything else.
                var sourceGeometry = store.GetSweepGeometry(sourceFilterName);
                var geometryDiff = geometryTarget == null
                    ? new List<StarDetectionSettingDiffRow>()
                    : StarDetectionSettingsDiff.BuildSweepGeometryDiff(
                        currentStepSize: geometryTarget.SweepStepSizeOverride,
                        currentOffsetSteps: geometryTarget.SweepOffsetStepsOverride,
                        incoming: sourceGeometry,
                        profileStepSize: geometryTarget.ProfileSweepStepSize,
                        profileOffsetSteps: geometryTarget.ProfileSweepOffsetSteps);
                diff.AddRange(geometryDiff);

                if (diff.Count == 0) {
                    Notification.ShowInformation($"'{sourceFilterName}' settings match the current settings; nothing to change.");
                    return;
                }

                var apply = await confirmDiff(diff, $"Copied from filter '{sourceFilterName}'");
                if (!apply) {
                    return;
                }

                options.ApplyImportedSnapshot(snapshot);
                if (geometryDiff.Count > 0) {
                    geometryTarget.MutateFilterSweepGeometry(geometryTarget.EditedFilterName, g => {
                        g.StepSize = sourceGeometry?.StepSize ?? PerFilterSweepGeometry.Inherit;
                        g.InitialOffsetSteps = sourceGeometry?.InitialOffsetSteps ?? PerFilterSweepGeometry.Inherit;
                    });
                }
                Logger.Info($"Copied star detection settings from filter '{sourceFilterName}' ({diff.Count} setting(s) changed)");
                Notification.ShowInformation($"Copied star detection settings from '{sourceFilterName}'");
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to copy star detection settings from filter '{sourceFilterName}'");
                Notification.ShowError($"Failed to copy star detection settings: {ex.Message}");
            }
        }

        /// <summary>The settings handoff a completed optimize pass leaves beside a run's frames. Deliberately NOT
        /// <c>optimized_settings.json</c>: that name is matched exactly by the bank readers, which deserialize it
        /// as a bare <c>OptimizedStarDetectionSettings</c>, and handing them this envelope would bind every
        /// curated knob to its CLR default and score a different detector with no error and no warning.</summary>
        internal const string RunFolderSettingsFileName = "hocusfocus_star_detection.json";

        /// <summary>
        /// Loads the settings handoff sitting beside a loaded run's frames and — after the same diff confirmation
        /// an Import shows — applies it to the current settings.
        ///
        /// <para><b>Explicitly user-initiated, never automatic.</b> Picking this up on load would silently
        /// redefine "Current": the wizard's baseline is the live profile, and <c>baselineJ</c> plus the headline
        /// improvement percentage are both measured against it. A run folder quietly becoming the baseline would
        /// change what that percentage MEANS with nothing on screen to say so — the wave-3 seed-leak shape, where
        /// a value read back from a run folder silently moved results. So the user presses this, and sees the diff
        /// before anything changes.</para>
        /// </summary>
        public static Task ImportFromRunFolderAsync(
                string runFolder, StarDetectionOptions options, IWindowServiceFactory windowServiceFactory) {
            return ImportFromRunFolderAsync(runFolder, options,
                (rows, summary) => ImportStarDetectionPreview.ShowAsync(windowServiceFactory, new ImportStarDetectionPreviewVM(rows, summary)));
        }

        /// <summary>Delegate-injected core of the import-from-run-folder flow (unit-test seam — no WPF dialog).</summary>
        internal static async Task ImportFromRunFolderAsync(
                string runFolder,
                StarDetectionOptions options,
                Func<IReadOnlyList<StarDetectionSettingDiffRow>, string, Task<bool>> confirmDiff) {
            if (string.IsNullOrWhiteSpace(runFolder) || options == null) {
                return;
            }
            try {
                var path = ResolveRunFolderSettings(runFolder);
                if (path == null) {
                    Notification.ShowInformation(
                        $"No saved star detection settings found for this run. They are written by an optimize pass as {RunFolderSettingsFileName}.");
                    return;
                }
                if (!StarDetectionSettingsExport.TryLoad(path, out var export, out var error)) {
                    Logger.Warning($"Could not import star detection settings from {path}: {error}");
                    Notification.ShowError($"Could not import this run's star detection settings: {error}");
                    return;
                }

                var diff = StarDetectionSettingsDiff.BuildDiff(options, export.StarDetection);
                if (diff.Count == 0) {
                    Notification.ShowInformation("This run's settings match the current settings; nothing to change.");
                    return;
                }

                var apply = await confirmDiff(diff, BuildSourceSummary(export) + $"  ·  from {Path.GetFileName(runFolder)}");
                if (!apply) {
                    return;
                }

                options.ApplyImportedSnapshot(export.StarDetection);
                Logger.Info($"Imported star detection settings from run folder {path} ({diff.Count} setting(s) changed)");
                Notification.ShowInformation($"Imported this run's star detection settings ({diff.Count} changed)");
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to import star detection settings from run folder '{runFolder}'");
                Notification.ShowError($"Failed to import this run's star detection settings: {ex.Message}");
            }
        }

        /// <summary>The handoff path for a run folder, or null when there is none. Checks the frame folder first
        /// and then its parent, mirroring the AF-replay metadata convention (folder before run root) — an
        /// <c>optimize --per-run</c> pass writes beside the frames, a joint pass writes at the run root.</summary>
        internal static string ResolveRunFolderSettings(string runFolder) {
            if (string.IsNullOrWhiteSpace(runFolder)) {
                return null;
            }
            var candidates = new List<string> { Path.Combine(runFolder, RunFolderSettingsFileName) };
            var parent = Path.GetDirectoryName(runFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(parent)) {
                candidates.Add(Path.Combine(parent, RunFolderSettingsFileName));
            }
            foreach (var c in candidates) {
                if (File.Exists(c)) {
                    return c;
                }
            }
            return null;
        }

        private static string BuildSourceSummary(StarDetectionSettingsExport export) {
            var when = export.CreatedAtUtc == default(DateTime)
                ? "unknown time"
                : export.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            var version = string.IsNullOrEmpty(export.PluginVersion) ? "unknown" : export.PluginVersion;
            var summary = $"Exported {when}  ·  plugin {version}";
            if (!string.IsNullOrEmpty(export.FilterName)) {
                summary += $"  ·  filter {export.FilterName}";
            }
            return summary;
        }
    }
}
