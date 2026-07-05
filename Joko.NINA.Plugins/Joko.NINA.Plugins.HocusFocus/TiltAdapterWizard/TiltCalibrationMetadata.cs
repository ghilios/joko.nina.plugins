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
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>One entry of the run → wizard-step folder map. The folder is the AutoFocus attempt root
    /// (== <c>InspectorVM.LastSaveFolder</c>) that a replay/validation run loads. It is stored <b>relative to the
    /// run root</b> (the directory holding <c>metadata.json</c>) so a saved run stays replayable after it is moved
    /// or copied to another machine; legacy files that stored an absolute path are still honored on read. Use
    /// <see cref="TiltCalibrationMetadata.ToRelativeStepFolder"/> when writing and
    /// <see cref="TiltCalibrationMetadata.ResolveStepFolder"/> when reading.</summary>
    public sealed class TiltRunStepMapping {
        public string Step { get; set; }
        public string Folder { get; set; }
    }

    /// <summary>
    /// Per-step measured tilt-plane and field-curvature characterization (req 9), recorded for every measured
    /// step. The tilt plane is the focus surface gradient: <c>plane = A·x + B·y</c> in focuser steps per
    /// normalized image coordinate (range [-0.5, 0.5]). Curvature fields require the sensor curve model to be
    /// enabled; they carry NaN otherwise.
    /// </summary>
    public sealed class TiltPerStepResult {
        public string Step { get; set; }
        public double TiltPlaneA { get; set; }
        public double TiltPlaneB { get; set; }
        public double MeanFocuserPosition { get; set; }
        public double TiltAngleDeg { get; set; }
        public double DirectionDeg { get; set; }
        public double CurvatureRadiusMillimeters { get; set; } = double.NaN;
        public double CurvatureEffectMicronsAtScrewRadius { get; set; } = double.NaN;
    }

    /// <summary>The computed calibration result stored for reference (the wizard/validator re-derive this from
    /// the per-step readings via the shared <see cref="TiltCalibrationCalculator"/>).</summary>
    public sealed class TiltCalibrationResultRecord {
        public double Screw1AngleDegrees { get; set; }
        public double Screw2AngleDegrees { get; set; }
        public double Screw3AngleDegrees { get; set; }
        public double Screw4AngleDegrees { get; set; }
        public int CurvatureSign { get; set; }
        public double MeasuredHardwareMicrons { get; set; }
        public double RawAngleDiffDegrees { get; set; }
        public double MoveMagnitudeRatio { get; set; }

        // ---- Confidence (persisted so a saved/replayed run carries its reliability) ----
        public double SignalToNoise { get; set; } = double.NaN;
        public double PredictedAngleUncertaintyDeg { get; set; } = double.NaN;
        public double PitchUncertaintyMicrons { get; set; } = double.NaN;
        public bool ConfidenceIsReliable { get; set; }
    }

    /// <summary>
    /// Serialization unit for a saved Tilt Adapter calibration run, shared by the live wizard (writer + replay)
    /// and the headless <c>TestApp tilt</c> validator so a wizard-saved run replays both in-app and offline.
    /// Holds the calibration settings, the star-detection settings used (so replay reproduces detection without
    /// mutating the profile), the run → step folder map, and the per-step / final results. The validator-only
    /// ground-truth fields are optional and omitted by the wizard.
    /// </summary>
    public sealed class TiltCalibrationMetadata {

        public const int CurrentSchemaVersion = 2;

        /// <summary>The six discrete measurement steps, in capture order. Same for 3- and 4-screw adapters.</summary>
        public static readonly string[] StepOrder = {
            "Baseline", "AllInward", "ReBaseline1", "Screw1", "ReBaseline2", "Screw2"
        };

        /// <summary>The 4 measurement steps of a run captured without the curvature-direction steps.</summary>
        public static readonly string[] StepOrderWithoutCurvature = {
            "Baseline", "Screw1", "ReBaseline2", "Screw2"
        };

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        // ---- Calibration settings (req 3) ----
        public int NumberOfScrews { get; set; }
        public string AdjustmentType { get; set; } = "Screws";       // "Screws" | "StepperMotors"
        public double ScrewThreadPitchMicrons { get; set; } = -1;     // µm/turn (screws)
        public double StepperStepSizeMicrons { get; set; } = -1;      // µm/step (steppers)
        public double ScrewRadiusMillimeters { get; set; }
        public double PixelSizeMicrons { get; set; }
        public double FocuserStepSizeMicrons { get; set; }
        public double CalibrationAppliedAmount { get; set; } = 1.0;   // turns/steps applied per screw step
        public int MeasurementAverageCount { get; set; } = 1;

        // ---- Replay payload ----
        public List<TiltRunStepMapping> RunStepMapping { get; set; }
        public OptimizedStarDetectionSettings OptimizedStarDetectionSettings { get; set; }

        // ---- Results ----
        public List<TiltPerStepResult> PerStep { get; set; }
        public TiltCalibrationResultRecord Calibration { get; set; }

        // ---- Optional validator-only ground truth (omitted by the wizard) ----
        // NaN = not provided (a wizard-written file): the headless validator then reports the screw-1 angle as
        // "n/a" instead of failing it against a bogus 0° expectation.
        public double ExpectedPositionAngleScrew1Deg { get; set; } = double.NaN;
        public bool DefocusAwareDetectionNeeded { get; set; }

        [JsonIgnore]
        public bool IsStepperAdjustment =>
            string.Equals(AdjustmentType, "StepperMotors", StringComparison.OrdinalIgnoreCase);

        public string Serialize() => JsonConvert.SerializeObject(this, JsonSettings);

        public static TiltCalibrationMetadata Deserialize(string json) =>
            JsonConvert.DeserializeObject<TiltCalibrationMetadata>(json, JsonSettings);

        /// <summary>
        /// Converts a step folder to one stored relative to <paramref name="runRootFolder"/> (the directory holding
        /// <c>metadata.json</c>) so a saved run stays replayable after it is moved or copied. A folder that is not
        /// under the run root (e.g. a different volume) is kept absolute rather than emitting a brittle
        /// <c>..\..</c> chain. Inverse of <see cref="ResolveStepFolder"/>.
        /// </summary>
        public static string ToRelativeStepFolder(string runRootFolder, string stepFolder) {
            if (string.IsNullOrEmpty(stepFolder) || string.IsNullOrEmpty(runRootFolder)) {
                return stepFolder;
            }
            var relative = Path.GetRelativePath(runRootFolder, stepFolder);
            return Path.IsPathRooted(relative) ? stepFolder : relative;
        }

        /// <summary>
        /// Resolves a stored step folder against the run root the caller actually selected. Relative entries (the
        /// current format) are rebased onto <paramref name="runRootFolder"/>. Absolute entries (legacy files, or a
        /// run captured under a different volume/path than where <c>metadata.json</c> now lives) are honored as-is
        /// when they still exist; otherwise their tail is rebased onto the selected run root so a moved/copied run
        /// stays replayable. Mirrors the headless <c>TestApp</c> resolver so in-app and offline replay agree.
        /// </summary>
        public static string ResolveStepFolder(string runRootFolder, string storedFolder) {
            if (string.IsNullOrEmpty(storedFolder) || string.IsNullOrEmpty(runRootFolder)) {
                return storedFolder;
            }
            if (!Path.IsPathRooted(storedFolder)) {
                return Path.GetFullPath(Path.Combine(runRootFolder, storedFolder));
            }
            // Absolute, and present on this machine: honor it (the common live-replay case).
            if (Directory.Exists(storedFolder)) {
                return storedFolder;
            }
            // Absolute but missing — the run was moved/copied to a different drive than the captured path. Rebase
            // the portion after the run-root folder (e.g. TiltCalibration_<id>) onto the selected run root; fall
            // back to the last two segments (<NN_Step>\AutoFocus_<timestamp>), the wizard's save structure.
            return RebaseAbsoluteStepFolder(runRootFolder, storedFolder) ?? storedFolder;
        }

        private static string RebaseAbsoluteStepFolder(string runRootFolder, string storedFolder) {
            var segments = storedFolder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string TryTail(IEnumerable<string> tailSegments) {
                var tail = tailSegments.ToArray();
                if (tail.Length == 0) {
                    return null;
                }
                var candidate = Path.GetFullPath(Path.Combine(runRootFolder, Path.Combine(tail)));
                return Directory.Exists(candidate) ? candidate : null;
            }

            var rootLeaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(runRootFolder));
            if (!string.IsNullOrEmpty(rootLeaf)) {
                int idx = Array.FindLastIndex(segments, s => string.Equals(s, rootLeaf, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0 && idx < segments.Length - 1) {
                    var afterRoot = TryTail(segments.Skip(idx + 1));
                    if (afterRoot != null) {
                        return afterRoot;
                    }
                }
            }
            return segments.Length >= 2 ? TryTail(segments.Skip(segments.Length - 2)) : null;
        }

        public void Validate() {
            if (NumberOfScrews != 3 && NumberOfScrews != 4) {
                throw new InvalidOperationException($"numberOfScrews must be 3 or 4 (was {NumberOfScrews}).");
            }
            if (PixelSizeMicrons <= 0) throw new InvalidOperationException("pixelSizeMicrons must be > 0.");
            if (FocuserStepSizeMicrons <= 0) throw new InvalidOperationException("focuserStepSizeMicrons must be > 0.");
            if (ScrewRadiusMillimeters <= 0) throw new InvalidOperationException("screwRadiusMillimeters must be > 0.");
            if (CalibrationAppliedAmount <= 0) throw new InvalidOperationException("calibrationAppliedAmount must be > 0.");
        }
    }
}
