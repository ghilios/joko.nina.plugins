#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay {

    /// <summary>What a replay should run with after the user's <see cref="ReplaySettingsChoice"/> is resolved.</summary>
    public sealed class ReplayOptionsResolution {

        /// <summary>True when the user cancelled — the caller should abort without replaying.</summary>
        public bool Cancelled { get; set; }

        /// <summary>The engine options to replay with (null when cancelled).</summary>
        public AutoFocusEngineOptions Options { get; set; }

        /// <summary>
        /// When non-null, replay MUST be driven through the explicit-region path using exactly these regions so the
        /// run's capture-time ROI is honored without mutating the profile (option b). Null means the caller uses its
        /// normal region source (AF-pane single ROI / Inspector <c>GetStarDetectionRegions</c>).
        /// </summary>
        public List<StarDetectionRegion> CaptureTimeRegions { get; set; }

        /// <summary>
        /// The capture-time Aberration Inspector "sensor curve model" flag, set only for the in-memory capture-time
        /// replay (option b) so the Inspector analyzes the result the way the run was captured. Null means the caller
        /// uses its current <c>InspectorOptions.SensorCurveModelEnabled</c>. Ignored by the AF pane.
        /// </summary>
        public bool? SensorCurveModelEnabled { get; set; }

        public static ReplayOptionsResolution Cancel() => new ReplayOptionsResolution() { Cancelled = true };
    }

    /// <summary>
    /// Pure choice→options logic for replay, factored out of the view models so it is unit-testable without the
    /// modal, the engine, or the profile singletons.
    /// </summary>
    public static class AutoFocusReplayOptionsMapper {

        /// <summary>
        /// Copies the fit/method fields from a captured snapshot onto an engine options bundle. Intentionally leaves
        /// <c>AutoFocusStepSize</c> (re-derived from the saved frames), the live-capture/scheduling knobs, and
        /// <c>StarDetectionOptionsOverride</c> untouched.
        /// </summary>
        public static void Apply(AutoFocusEngineOptions options, AutoFocusOptionsSnapshot snapshot) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }
            if (snapshot == null) {
                throw new ArgumentNullException(nameof(snapshot));
            }
            options.DebayerImage = snapshot.DebayerImage;
            options.NumberOfAFStars = snapshot.NumberOfAFStars;
            options.TotalNumberOfAttempts = snapshot.TotalNumberOfAttempts;
            options.ValidateHfrImprovement = snapshot.ValidateHfrImprovement;
            options.AutoFocusMethod = snapshot.AutoFocusMethod;
            options.AutoFocusCurveFitting = snapshot.AutoFocusCurveFitting;
            options.AutoFocusInitialOffsetSteps = snapshot.AutoFocusInitialOffsetSteps;
            options.FramesPerPoint = snapshot.FramesPerPoint;
            options.HFRImprovementThreshold = snapshot.HFRImprovementThreshold;
            options.FocuserOffset = snapshot.FocuserOffset;
            options.MaxOutlierRejections = snapshot.MaxOutlierRejections;
            options.OutlierRejectionConfidence = snapshot.OutlierRejectionConfidence;
            options.WeightedHyperbolicFitEnabled = snapshot.WeightedHyperbolicFitEnabled;
            options.HyperbolicFitModel = snapshot.HyperbolicFitModel;
            options.FitRejectionCriterion = snapshot.FitRejectionCriterion;
            options.ReducedChiSquaredRejectionThreshold = snapshot.ReducedChiSquaredRejectionThreshold;
        }

        /// <summary>Captures the fit/method fields of an engine options bundle into a serializable snapshot.</summary>
        public static AutoFocusOptionsSnapshot Capture(AutoFocusEngineOptions options) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }
            return new AutoFocusOptionsSnapshot() {
                DebayerImage = options.DebayerImage,
                NumberOfAFStars = options.NumberOfAFStars,
                TotalNumberOfAttempts = options.TotalNumberOfAttempts,
                ValidateHfrImprovement = options.ValidateHfrImprovement,
                AutoFocusMethod = options.AutoFocusMethod,
                AutoFocusCurveFitting = options.AutoFocusCurveFitting,
                AutoFocusInitialOffsetSteps = options.AutoFocusInitialOffsetSteps,
                FramesPerPoint = options.FramesPerPoint,
                HFRImprovementThreshold = options.HFRImprovementThreshold,
                FocuserOffset = options.FocuserOffset,
                MaxOutlierRejections = options.MaxOutlierRejections,
                OutlierRejectionConfidence = options.OutlierRejectionConfidence,
                WeightedHyperbolicFitEnabled = options.WeightedHyperbolicFitEnabled,
                HyperbolicFitModel = options.HyperbolicFitModel,
                FitRejectionCriterion = options.FitRejectionCriterion,
                ReducedChiSquaredRejectionThreshold = options.ReducedChiSquaredRejectionThreshold
            };
        }

        /// <summary>
        /// Resolves the engine options + regions to replay with, given the user's choice.
        /// <paramref name="buildBaseOptions"/> produces a fresh options bundle from the CURRENT profile — for
        /// <see cref="ReplaySettingsChoice.UpdateProfileToCaptureTime"/> it is invoked AFTER
        /// <paramref name="applyToProfile"/> mutates the profile, so the returned bundle reflects the just-applied
        /// capture-time settings.
        /// </summary>
        public static ReplayOptionsResolution BuildReplayOptions(
            ReplaySettingsChoice choice,
            Func<AutoFocusEngineOptions> buildBaseOptions,
            AutoFocusReplayMetadata metadata,
            Action applyToProfile) {
            if (buildBaseOptions == null) {
                throw new ArgumentNullException(nameof(buildBaseOptions));
            }

            switch (choice) {
                case ReplaySettingsChoice.Cancel:
                    return ReplayOptionsResolution.Cancel();

                case ReplaySettingsChoice.UseCurrentSettings:
                    return new ReplayOptionsResolution() { Options = buildBaseOptions() };

                case ReplaySettingsChoice.UseCaptureTimeSettingsInMemory: {
                    if (metadata == null) {
                        throw new ArgumentNullException(nameof(metadata));
                    }
                    var options = buildBaseOptions();
                    Apply(options, metadata.AutoFocus);
                    options.StarDetectionOptionsOverride = metadata.StarDetection;
                    return new ReplayOptionsResolution() {
                        Options = options,
                        CaptureTimeRegions = metadata.Regions?.Regions,
                        SensorCurveModelEnabled = metadata.Regions?.SensorCurveModelEnabled
                    };
                }

                case ReplaySettingsChoice.UpdateProfileToCaptureTime: {
                    applyToProfile?.Invoke();
                    // Built after the profile mutation, so it already reflects the capture-time settings; no override.
                    return new ReplayOptionsResolution() { Options = buildBaseOptions() };
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(choice), choice, "Unhandled replay settings choice");
            }
        }
    }
}
