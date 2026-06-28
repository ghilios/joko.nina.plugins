#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// File envelope for exporting/importing ONLY star-detection parameters between machines (tune on a fast box,
    /// import on the imaging box). Wraps a flat, profile-detached <see cref="StarDetectionSettingsSnapshot"/> with a
    /// schema version + provenance. The JSON conventions and the never-throwing <see cref="TryLoad"/> pattern mirror
    /// <see cref="AutoFocusReplayMetadata"/>; the envelope fields are kept OUT of the snapshot itself because that type
    /// is reused as the AF replay payload and as the engine's in-memory override, where a schema/provenance would be
    /// meaningless.
    /// </summary>
    public sealed class StarDetectionSettingsExport {
        public const int CurrentSchemaVersion = 1;

        /// <summary>Discriminator that distinguishes this file from an AutoFocus <c>metadata.json</c> — the latter also
        /// has a <c>schemaVersion</c> and a <c>starDetection</c> node of the same type, so it would otherwise
        /// deserialize cleanly into this envelope.</summary>
        public const string ExpectedFileType = "HocusFocusStarDetectionSettings";

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings() {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        // NOTE: no default initializer. If this were defaulted to ExpectedFileType, a JSON file lacking the
        // "fileType" key (e.g. an AutoFocus metadata.json, which has the same schemaVersion + starDetection shape)
        // would deserialize with FileType already set to the expected value and wrongly pass Validate. Leaving it null
        // means a file without the discriminator is correctly rejected.
        public string FileType { get; set; }
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public DateTime CreatedAtUtc { get; set; }

        // Informational provenance — not used to drive an import.
        public string PluginVersion { get; set; }

        /// <summary>The full star-detection knob set + optimized-settings layer (see
        /// <see cref="StarDetectionSettingsSnapshot"/>).</summary>
        public StarDetectionSettingsSnapshot StarDetection { get; set; }

        /// <summary>Captures the live options into a portable export. The machine-local intermediate-image path and
        /// save-intermediate flag are blanked: they are never applied on import and the path would otherwise leak a
        /// local user directory into the shared file.</summary>
        public static StarDetectionSettingsExport FromOptions(IStarDetectionOptions options) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            var snapshot = StarDetectionSettingsSnapshot.FromOptions(options);
            snapshot.IntermediateSavePath = "";
            snapshot.SaveIntermediateImages = false;

            return new StarDetectionSettingsExport() {
                FileType = ExpectedFileType,
                SchemaVersion = CurrentSchemaVersion,
                CreatedAtUtc = DateTime.UtcNow,
                PluginVersion = typeof(StarDetectionSettingsExport).Assembly.GetName().Version?.ToString(),
                StarDetection = snapshot
            };
        }

        public string Serialize() => JsonConvert.SerializeObject(this, JsonSettings);

        public static StarDetectionSettingsExport Deserialize(string json) =>
            JsonConvert.DeserializeObject<StarDetectionSettingsExport>(json, JsonSettings);

        public void Validate() {
            if (!string.Equals(FileType, ExpectedFileType, StringComparison.Ordinal)) {
                throw new InvalidOperationException("This file is not a HocusFocus Star Detection settings export.");
            }
            if (SchemaVersion <= 0) {
                throw new InvalidOperationException("schemaVersion must be > 0.");
            }
            if (StarDetection == null) {
                throw new InvalidOperationException("starDetection is required.");
            }
        }

        /// <summary>
        /// Loads + validates an export file chosen by the user. Unlike <see cref="AutoFocusReplayMetadata.TryLoad"/>
        /// (which resolves <c>metadata.json</c> inside a folder), this takes the file path directly. A missing or
        /// unreadable file, malformed JSON, a wrong file type, or a newer schema all return false with
        /// <paramref name="error"/> set. Never throws.
        /// </summary>
        public static bool TryLoad(string filePath, out StarDetectionSettingsExport export, out string error) {
            export = null;
            error = null;
            if (string.IsNullOrWhiteSpace(filePath)) {
                error = "No file specified.";
                return false;
            }
            try {
                if (!File.Exists(filePath)) {
                    throw new FileNotFoundException("File not found.", filePath);
                }
                var loaded = Deserialize(File.ReadAllText(filePath));
                if (loaded == null) {
                    throw new InvalidOperationException("File deserialized to null.");
                }
                loaded.Validate();
                if (loaded.SchemaVersion > CurrentSchemaVersion) {
                    throw new InvalidOperationException(
                        $"settings schema {loaded.SchemaVersion} is newer than the supported {CurrentSchemaVersion}; update the HocusFocus plugin.");
                }
                export = loaded;
                return true;
            } catch (Exception e) {
                error = e.Message;
                return false;
            }
        }
    }
}
