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
using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay {

    /// <summary>
    /// Round-trippable record of the settings used to capture a saved AutoFocus run, written to
    /// <c>metadata.json</c> in the run's output folder. Lets a replay reproduce the original detection + fitting
    /// (or update the live profile to match) instead of always using current settings. Concrete types only — no
    /// interface-typed fields — so it deserializes without <c>TypeNameHandling</c>. JSON conventions mirror
    /// <see cref="TiltAdapterWizard.TiltCalibrationMetadata"/>.
    /// </summary>
    public sealed class AutoFocusReplayMetadata {
        public const int CurrentSchemaVersion = 1;

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings() {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public DateTime CreatedAtUtc { get; set; }

        // Informational provenance — not used to drive a replay.
        public string PluginVersion { get; set; }

        public int StarDetectorVersion { get; set; }

        /// <summary>Full star-detection knob set captured at the run (see <see cref="StarDetectionSettingsSnapshot"/>).
        /// Doubles as the engine's <c>StarDetectionOptionsOverride</c> for an in-memory capture-time replay.</summary>
        public StarDetectionSettingsSnapshot StarDetection { get; set; }

        /// <summary>The AutoFocus fit/method options that affect re-fitting an already-captured run (step size and
        /// pure live-capture knobs are intentionally excluded).</summary>
        public AutoFocusOptionsSnapshot AutoFocus { get; set; }

        /// <summary>Region/ROI geometry used by the run, sufficient to replay (option b) and to update the profile
        /// (option c) for both the AF-pane single-region and the Aberration Inspector multi-region cases.</summary>
        public ReplayRegionGeometry Regions { get; set; }

        /// <summary>Informational summary of the run's result(s); not used to drive a replay.</summary>
        public List<ReplayRegionResultSummary> Results { get; set; }

        public string Serialize() => JsonConvert.SerializeObject(this, JsonSettings);

        public static AutoFocusReplayMetadata Deserialize(string json) => JsonConvert.DeserializeObject<AutoFocusReplayMetadata>(json, JsonSettings);

        /// <summary>
        /// Attempts to load + validate the <c>metadata.json</c> for a saved run. The caller may pass either the run
        /// root (where metadata.json lives) or a saved ATTEMPT subfolder (LoadSavedAutoFocusAttempt resolves
        /// FolderPath to e.g. <c>AutoFocus_&lt;ts&gt;/attempt01</c>), so the folder and its parent run root are both
        /// checked. Returns false when the file is absent (with <paramref name="error"/> null — the caller should
        /// silently keep current behavior) or when it is unreadable / fails validation / has a newer schema (with
        /// <paramref name="error"/> set — the caller should warn and fall back). Never throws.
        /// </summary>
        public static bool TryLoad(string runFolderPath, out AutoFocusReplayMetadata metadata, out string error) {
            metadata = null;
            error = null;
            if (string.IsNullOrWhiteSpace(runFolderPath)) {
                return false;
            }
            var path = ResolveMetadataPath(runFolderPath);
            if (path == null) {
                return false;
            }
            try {
                var loaded = Deserialize(File.ReadAllText(path));
                if (loaded == null) {
                    throw new InvalidOperationException("metadata.json deserialized to null.");
                }
                loaded.Validate();
                if (loaded.SchemaVersion > CurrentSchemaVersion) {
                    throw new InvalidOperationException($"metadata schema {loaded.SchemaVersion} is newer than the supported {CurrentSchemaVersion}.");
                }
                metadata = loaded;
                return true;
            } catch (Exception e) {
                error = e.Message;
                return false;
            }
        }

        /// <summary>Returns the path to metadata.json in <paramref name="folder"/> or, failing that, its parent run
        /// root (the folder may be a saved attempt subfolder). Null when neither has one.</summary>
        private static string ResolveMetadataPath(string folder) {
            var direct = Path.Combine(folder, "metadata.json");
            if (File.Exists(direct)) {
                return direct;
            }
            try {
                var parent = Directory.GetParent(folder)?.FullName;
                if (!string.IsNullOrEmpty(parent)) {
                    var parentPath = Path.Combine(parent, "metadata.json");
                    if (File.Exists(parentPath)) {
                        return parentPath;
                    }
                }
            } catch {
                // Malformed path — treat as "no metadata".
            }
            return null;
        }

        public void Validate() {
            if (SchemaVersion <= 0) {
                throw new InvalidOperationException("schemaVersion must be > 0.");
            }
            if (StarDetection == null) {
                throw new InvalidOperationException("starDetection is required.");
            }
            if (AutoFocus == null) {
                throw new InvalidOperationException("autoFocus is required.");
            }
        }
    }

    /// <summary>
    /// The <see cref="AutoFocusEngineOptions"/>-derived fields that affect detection/fitting of an ALREADY-captured
    /// run. Excludes <c>AutoFocusStepSize</c> (re-derived from the saved frames) and pure live-capture / scheduling
    /// knobs (Save, SavePath, MaxConcurrent, AutoFocusTimeout, exposure overrides, PreserveExposures, ModelPSF,
    /// SaveExposuresOnly, ReuseSavedDetection).
    /// </summary>
    public sealed class AutoFocusOptionsSnapshot {
        public bool DebayerImage { get; set; }
        public int NumberOfAFStars { get; set; }
        public int TotalNumberOfAttempts { get; set; }
        public bool ValidateHfrImprovement { get; set; }
        public AFMethodEnum AutoFocusMethod { get; set; }
        public AFCurveFittingEnum AutoFocusCurveFitting { get; set; }
        public int AutoFocusInitialOffsetSteps { get; set; }
        public int FramesPerPoint { get; set; }
        public double HFRImprovementThreshold { get; set; }
        public int FocuserOffset { get; set; }
        public int MaxOutlierRejections { get; set; }
        public double OutlierRejectionConfidence { get; set; }
        public bool WeightedHyperbolicFitEnabled { get; set; }
        public HyperbolicFitModel HyperbolicFitModel { get; set; }
        public FitRejectionCriterion FitRejectionCriterion { get; set; }
        public double ReducedChiSquaredRejectionThreshold { get; set; }
    }

    /// <summary>
    /// Region/ROI geometry of a saved run. <see cref="Regions"/> is the fully-resolved list actually used (a single
    /// region for an AF-pane run, several for an Inspector run) and is what an in-memory replay (option b) drives
    /// through the explicit-region path so capture-time ROI is honored without mutating the profile. The scalar
    /// inputs are kept so option (c) can restore the live profile's ROI knobs.
    /// </summary>
    public sealed class ReplayRegionGeometry {
        // AF-pane inputs (FocuserSettings crop ratios).
        public double AutoFocusInnerCropRatio { get; set; }
        public double AutoFocusOuterCropRatio { get; set; }

        // Aberration Inspector inputs (null/zero for an AF-pane run).
        public bool IsInspectorRun { get; set; }
        public double SensorROI { get; set; }
        public double CornersROI { get; set; }
        public bool SensorCurveModelEnabled { get; set; }

        public List<StarDetectionRegion> Regions { get; set; }
    }

    /// <summary>Informational per-region result summary captured at the run. The doubles are nullable so an absent
    /// value serializes as JSON <c>null</c> rather than a non-standard <c>NaN</c> literal (a failed/incomplete fit has
    /// no final point or R²).</summary>
    public sealed class ReplayRegionResultSummary {
        public int RegionIndex { get; set; }
        public double? EstimatedFinalFocuserPosition { get; set; }
        public double? EstimatedFinalHFR { get; set; }
        public double? FinalHFR { get; set; }
        public double? InitialHFR { get; set; }
        public double? RSquared { get; set; }
        public HyperbolicFitModel? SelectedHyperbolicFitModel { get; set; }
    }
}
