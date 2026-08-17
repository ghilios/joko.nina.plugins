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
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using System;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// File envelope for exporting/importing one filter's star-detection parameters — plus its auto-focus sweep
    /// geometry — between machines (tune on a fast box, import on the imaging box). Wraps a flat, profile-detached
    /// <see cref="StarDetectionSettingsSnapshot"/> with a schema version + provenance. The JSON conventions and the
    /// never-throwing <see cref="TryLoad"/> pattern mirror <see cref="AutoFocusReplayMetadata"/>; the envelope fields
    /// are kept OUT of the snapshot itself because that type is reused as the AF replay payload and as the engine's
    /// in-memory override, where a schema/provenance would be meaningless.
    /// </summary>
    public sealed class StarDetectionSettingsExport {

        /// <summary>
        /// Deliberately still 1 after the <see cref="FilterName"/> and <see cref="SweepGeometry"/> nodes were added.
        /// Both are additive and optional in BOTH directions — a new plugin reading an old file gets null, and an old
        /// plugin reading a new file ignores the extra key — so bumping this would only make an older plugin refuse a
        /// file it can read perfectly well (<see cref="TryLoad"/> rejects a newer schema). Bump it when a change
        /// actually breaks one of those directions.
        /// </summary>
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

        /// <summary>Name of the filter whose per-filter settings set was exported. Stamped only while per-filter
        /// star detection is enabled; omitted from the JSON when null so files written with the feature off (and
        /// every pre-existing export) keep the legacy schema byte-for-byte. Provenance only — an import applies to
        /// whichever filter is being edited, regardless of this value.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string FilterName { get; set; }

        /// <summary>The full star-detection knob set + optimized-settings layer (see
        /// <see cref="StarDetectionSettingsSnapshot"/>).</summary>
        public StarDetectionSettingsSnapshot StarDetection { get; set; }

        /// <summary>
        /// The exported filter's auto-focus sweep-geometry override, or null when the file carries none (per-filter
        /// star detection was off, and every export written before this node existed). Omitted from the JSON when
        /// null so those files keep the legacy schema byte-for-byte.
        ///
        /// <para>A SIBLING of <see cref="StarDetection"/> rather than a member of it, because that type is also the
        /// AF replay metadata payload and the engine's in-memory detector override — a focuser sweep inside a node
        /// named <c>starDetection</c> would be meaningless in both roles (see
        /// <see cref="PerFilterSweepGeometry"/>'s own remarks).</para>
        ///
        /// <para>Null and "unset" are deliberately DIFFERENT states here. Null means the file says nothing about the
        /// sweep, so an import leaves the receiving filter's override alone; an unset (both-inherit) instance means
        /// the exported filter explicitly inherited the profile, so an import clears a stale override on the
        /// receiving side. Collapsing the two would make a legacy file silently wipe a filter's sweep.</para>
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public PerFilterSweepGeometry SweepGeometry { get; set; }

        /// <summary>Captures the live options into a portable export. The machine-local intermediate-image path and
        /// save-intermediate flag are blanked: they are never applied on import and the path would otherwise leak a
        /// local user directory into the shared file. <paramref name="filterName"/> is the edited filter when
        /// per-filter star detection is on; null/empty leaves the provenance field absent.
        /// <paramref name="sweepGeometry"/> is that filter's sweep override (pass null when there is no per-filter
        /// context at all); it is normalized and copied, so a later edit to the caller's instance cannot reach the
        /// file.</summary>
        public static StarDetectionSettingsExport FromOptions(
                IStarDetectionOptions options, string filterName = null, PerFilterSweepGeometry sweepGeometry = null) {
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
                FilterName = string.IsNullOrEmpty(filterName) ? null : filterName,
                StarDetection = snapshot,
                SweepGeometry = sweepGeometry?.Normalized()
            };
        }

        /// <summary>
        /// The same envelope, but describing an OPTIMIZER LANDING rather than a live profile — the form the AF bank
        /// stores beside each run's frames so the landing can be replayed in the app.
        /// </summary>
        ///
        /// <para><b>Why the flat knobs are overwritten and not just the nested DTO.</b> Handing back a snapshot whose
        /// <see cref="StarDetectionSettingsSnapshot.OptimizedSettings"/> holds the landing while its flat properties
        /// still hold the harness baseline produces a file that is silently WRONG on both consumers:
        /// <see cref="StarDetectionOptions"/>'s import restores the optimized layer first and then copies every flat
        /// knob verbatim (so the flat baseline wins), and the AF-replay in-memory override
        /// (<c>BuildStarDetectorParams(IStarDetectionOptions)</c>) reads the flat knobs ONLY and never looks at the
        /// nested DTO. Neither path errors; the user simply gets settings that are not the landing. So the landing is
        /// written into both layers, and <see cref="OptimizedStarDetectionSettings.ApplyToFlatOptions"/> owns the
        /// mapping so the two cannot drift.</para>
        ///
        /// <param name="baseOptions">
        /// The options the optimize actually RAN with. Everything the curated axes do not cover — detection binning,
        /// contamination, PSF/Moffat, saturation, dilation, measurement average — comes from here, because a landing
        /// replayed against different values for those is not the configuration that was measured.
        /// </param>
        /// <param name="landing">The winning axis values, already post-AND for the defocus gates.</param>
        public static StarDetectionSettingsExport FromOptimizedLanding(
            IStarDetectionOptions baseOptions, OptimizedStarDetectionSettings landing) {
            if (baseOptions == null) {
                throw new ArgumentNullException(nameof(baseOptions));
            }
            if (landing == null) {
                throw new ArgumentNullException(nameof(landing));
            }

            var export = FromOptions(baseOptions);
            landing.ApplyToFlatOptions(export.StarDetection);
            export.StarDetection.ApplyOptimizedSettings(landing);
            // The state a wizard Accept leaves behind, so importing this file lands the user exactly there.
            export.StarDetection.UseOptimizedSettings = true;
            export.StarDetection.UseAdvanced = false;
            return export;
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
