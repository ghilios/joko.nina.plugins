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

                var export = BuildExport(options, GetGeometryTarget());
                File.WriteAllText(dialog.FileName, export.Serialize());
                Logger.Info($"Exported star detection settings to {dialog.FileName}");
                Notification.ShowInformation($"Exported star detection settings to {Path.GetFileName(dialog.FileName)}");
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to export star detection settings");
                Notification.ShowError($"Failed to export star detection settings: {ex.Message}");
            }
        }

        /// <summary>
        /// The file contents an Export writes (unit-test seam — no file dialog). With a per-filter geometry target it
        /// carries that filter's name and its sweep-geometry override; without one the file is exactly what it was
        /// before per-filter mode existed.
        ///
        /// <para>An unset geometry is still written when the target exists, because "this filter inherits the
        /// profile" is a state worth reproducing on the far machine — see
        /// <see cref="StarDetectionSettingsExport.SweepGeometry"/> on why null and unset must not be collapsed.</para>
        /// </summary>
        internal static StarDetectionSettingsExport BuildExport(StarDetectionOptions options, PerFilterEditBinder geometrySource) {
            if (!CanCarryGeometry(geometrySource)) {
                return StarDetectionSettingsExport.FromOptions(options);
            }
            return StarDetectionSettingsExport.FromOptions(options, geometrySource.EditedFilterName, new PerFilterSweepGeometry() {
                StepSize = geometrySource.SweepStepSizeOverride,
                InitialOffsetSteps = geometrySource.SweepOffsetStepsOverride
            });
        }

        /// <summary>The per-filter edit binder to read/write sweep geometry through, or null when there is no
        /// per-filter context: the feature is off, or the plugin singletons are absent (as under unit tests).</summary>
        private static PerFilterEditBinder GetGeometryTarget() {
            return HocusFocusPlugin.PerFilterStarDetection?.Enabled == true
                ? HocusFocusPlugin.PerFilterStarDetectionEditBinder
                : null;
        }

        /// <summary>Whether a geometry target can actually hold a sweep override. A binder with no edited filter (the
        /// feature is on but the profile defines no filters) cannot, and must not be offered rows that a following
        /// Apply would silently drop.</summary>
        private static bool CanCarryGeometry(PerFilterEditBinder target) =>
            target != null && !string.IsNullOrEmpty(target.EditedFilterName);

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

                await ImportAsync(options, dialog.FileName,
                    (rows, summary) => ImportStarDetectionPreview.ShowAsync(windowServiceFactory, new ImportStarDetectionPreviewVM(rows, summary)),
                    GetGeometryTarget());
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to import star detection settings");
                Notification.ShowError($"Failed to import star detection settings: {ex.Message}");
            }
        }

        /// <summary>Delegate-injected core of the import flow (unit-test seam — no WPF dialog). Returns whether the
        /// settings were applied. <paramref name="geometryTarget"/> is the per-filter edit binder the file's sweep
        /// geometry lands on, or null when there is no per-filter context to apply it to.</summary>
        internal static async Task<bool> ImportAsync(
                StarDetectionOptions options,
                string filePath,
                Func<IReadOnlyList<StarDetectionSettingDiffRow>, string, Task<bool>> confirmDiff,
                PerFilterEditBinder geometryTarget) {
            if (options == null) {
                return false;
            }
            try {
                if (!StarDetectionSettingsExport.TryLoad(filePath, out var export, out var error)) {
                    Logger.Warning($"Could not import star detection settings from {filePath}: {error}");
                    Notification.ShowError($"Could not import star detection settings: {error}");
                    return false;
                }

                var diff = new List<StarDetectionSettingDiffRow>(StarDetectionSettingsDiff.BuildDiff(options, export.StarDetection));
                // The sweep geometry belongs to the filter's set, so it travels in the file and is confirmed beside
                // the detection knobs -- the same deal copy-from-filter already makes. Without it, exporting a
                // wizard-tuned filter carried HALF of what the wizard produced, and the dropped half was the one the
                // user notices on the next focus run.
                var geometryDiff = BuildGeometryDiff(geometryTarget, export.SweepGeometry);
                diff.AddRange(geometryDiff);

                var geometryNote = DescribeUnappliedGeometry(geometryTarget, export.SweepGeometry);
                if (diff.Count == 0) {
                    // The note still belongs here: a file whose detection settings match but whose sweep was dropped
                    // did change nothing, and the reason it changed nothing is worth saying.
                    Notification.ShowInformation("Imported settings match the current settings; nothing to change." + geometryNote);
                    return false;
                }

                var apply = await confirmDiff(diff, BuildSourceSummary(export) + geometryNote);
                if (!apply) {
                    return false;
                }

                options.ApplyImportedSnapshot(export.StarDetection);
                if (geometryDiff.Count > 0) {
                    ApplyGeometry(geometryTarget, export.SweepGeometry);
                }
                Logger.Info($"Imported star detection settings from {filePath} ({diff.Count} setting(s) changed)");
                Notification.ShowInformation($"Imported star detection settings from {Path.GetFileName(filePath)}");
                return true;
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to import star detection settings");
                Notification.ShowError($"Failed to import star detection settings: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Diff rows for an incoming sweep geometry, or none when there is nothing to compare it against. A null
        /// <paramref name="incoming"/> means the source said nothing about the sweep (a legacy file, or one exported
        /// with per-filter mode off), which is NOT the same as "inherit" and must leave the target's override alone.
        /// </summary>
        private static IReadOnlyList<StarDetectionSettingDiffRow> BuildGeometryDiff(
                PerFilterEditBinder target, PerFilterSweepGeometry incoming) {
            if (incoming == null || !CanCarryGeometry(target)) {
                return Array.Empty<StarDetectionSettingDiffRow>();
            }
            return StarDetectionSettingsDiff.BuildSweepGeometryDiff(
                currentStepSize: target.SweepStepSizeOverride,
                currentOffsetSteps: target.SweepOffsetStepsOverride,
                incoming: incoming,
                profileStepSize: target.ProfileSweepStepSize,
                profileOffsetSteps: target.ProfileSweepOffsetSteps);
        }

        /// <summary>Writes an incoming sweep geometry into the edited filter's stored set. Normalized on the way in,
        /// so a hand-edited file cannot put a 0 step size in front of the auto-focus engine.</summary>
        private static void ApplyGeometry(PerFilterEditBinder target, PerFilterSweepGeometry incoming) {
            var normalized = (incoming ?? PerFilterSweepGeometry.Unset()).Normalized();
            target.MutateFilterSweepGeometry(target.EditedFilterName, g => {
                g.StepSize = normalized.StepSize;
                g.InitialOffsetSteps = normalized.InitialOffsetSteps;
            });
        }

        /// <summary>A note for the confirmation line when the file carries a sweep override that this machine has
        /// nowhere to put (per-filter star detection is off, and geometry never wrote to the profile). Dropping it is
        /// correct; dropping it silently is how a user loses the wizard's step size without knowing.</summary>
        private static string DescribeUnappliedGeometry(PerFilterEditBinder target, PerFilterSweepGeometry incoming) {
            var carried = (incoming ?? PerFilterSweepGeometry.Unset()).Normalized();
            if (carried.IsUnset || CanCarryGeometry(target)) {
                return "";
            }
            return "  ·  sweep geometry not applied (per-filter star detection is off)";
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
                // Coalesced because the store's contract is never-null and "the source inherits" must still clear
                // an override on the target -- the file-import path distinguishes null (says nothing) from unset.
                var sourceGeometry = store.GetSweepGeometry(sourceFilterName) ?? PerFilterSweepGeometry.Unset();
                var geometryDiff = BuildGeometryDiff(geometryTarget, sourceGeometry);
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
                    ApplyGeometry(geometryTarget, sourceGeometry);
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
