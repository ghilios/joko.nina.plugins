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
using System;
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

                var export = StarDetectionSettingsExport.FromOptions(options);
                File.WriteAllText(dialog.FileName, export.Serialize());
                Logger.Info($"Exported star detection settings to {dialog.FileName}");
                Notification.ShowInformation($"Exported star detection settings to {Path.GetFileName(dialog.FileName)}");
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to export star detection settings");
                Notification.ShowError($"Failed to export star detection settings: {ex.Message}");
            }
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

        private static string BuildSourceSummary(StarDetectionSettingsExport export) {
            var when = export.CreatedAtUtc == default(DateTime)
                ? "unknown time"
                : export.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            var version = string.IsNullOrEmpty(export.PluginVersion) ? "unknown" : export.PluginVersion;
            return $"Exported {when}  ·  plugin {version}";
        }
    }
}
