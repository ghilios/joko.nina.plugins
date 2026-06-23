#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay {

    /// <summary>
    /// Shared replay wiring used by both the AutoFocus pane and the Aberration Inspector: reads a saved run's
    /// <c>metadata.json</c>, prompts the user (when interactive) for how to replay, and resolves the engine options +
    /// regions. Also applies a run's capture-time settings to the live profile for option (c).
    /// </summary>
    public static class AutoFocusReplayCoordinator {

        /// <summary>
        /// Resolves how to replay a saved run. Headless callers (e.g. the Tilt Adapter Wizard) pass
        /// <paramref name="isInteractive"/> = false and never prompt — supplying an optional
        /// <paramref name="headlessStarDetectionOverride"/> to replay with capture-time detection settings without
        /// mutating the profile. Interactive callers prompt only when a valid <c>metadata.json</c> exists; otherwise
        /// (missing/corrupt/newer-schema) they fall back to current settings.
        /// </summary>
        public static async Task<ReplayOptionsResolution> ResolveAsync(
            IWindowServiceFactory windowServiceFactory,
            IApplicationDispatcher applicationDispatcher,
            IProfileService profileService,
            string runFolderPath,
            bool isInteractive,
            Func<AutoFocusEngineOptions> buildBaseOptions,
            IStarDetectionOptions headlessStarDetectionOverride = null) {
            if (buildBaseOptions == null) {
                throw new ArgumentNullException(nameof(buildBaseOptions));
            }

            if (!isInteractive) {
                var options = buildBaseOptions();
                options.StarDetectionOptionsOverride = headlessStarDetectionOverride;
                return new ReplayOptionsResolution() { Options = options };
            }

            if (!AutoFocusReplayMetadata.TryLoad(runFolderPath, out var metadata, out var error)) {
                if (error != null) {
                    Logger.Warning($"Ignoring unreadable/incompatible replay metadata.json in {runFolderPath}: {error}");
                    Notification.ShowWarning("Saved run settings could not be read; replaying with current settings.");
                }
                return new ReplayOptionsResolution() { Options = buildBaseOptions() };
            }

            var choice = await ReplaySettingsPrompt.ShowAsync(windowServiceFactory, metadata);
            // ApplyToProfile (option c) raises INPC for the bound Options UI and must run on the UI thread. The AF-pane
            // replay runs on a background Task.Run, so marshal the mutation via the dispatcher (a synchronous Send with
            // a same-context fast path, so it is free when already on the UI thread, e.g. the Inspector path).
            void applyToProfile() {
                if (applicationDispatcher != null) {
                    applicationDispatcher.DispatchSynchronizationContext(() => ApplyToProfile(profileService, metadata));
                } else {
                    ApplyToProfile(profileService, metadata);
                }
            }
            return AutoFocusReplayOptionsMapper.BuildReplayOptions(choice, buildBaseOptions, metadata, applyToProfile);
        }

        /// <summary>
        /// Overwrites the live profile with a run's capture-time settings (option c): star detection, AutoFocus
        /// fit/method options, and ROI/region geometry. Mutates the plugin option singletons + profile settings, which
        /// persist with the profile. Must be called on the UI thread (raises INPC for bound settings UI).
        /// </summary>
        public static void ApplyToProfile(IProfileService profileService, AutoFocusReplayMetadata metadata) {
            if (profileService == null) {
                throw new ArgumentNullException(nameof(profileService));
            }
            if (metadata == null) {
                throw new ArgumentNullException(nameof(metadata));
            }

            // Star detection (full snapshot supersedes any curated optimized layer; forces advanced mode internally).
            if (metadata.StarDetection != null) {
                HocusFocusPlugin.StarDetectionOptions?.ApplyFullSnapshot(metadata.StarDetection);
            }

            // AutoFocus options: plugin-owned subset.
            var af = metadata.AutoFocus;
            var afOptions = HocusFocusPlugin.AutoFocusOptions;
            if (af != null && afOptions != null) {
                afOptions.ValidateHfrImprovement = af.ValidateHfrImprovement;
                afOptions.HFRImprovementThreshold = af.HFRImprovementThreshold;
                afOptions.FocuserOffset = af.FocuserOffset;
                afOptions.MaxOutlierRejections = af.MaxOutlierRejections;
                afOptions.OutlierRejectionConfidence = af.OutlierRejectionConfidence;
                afOptions.WeightedHyperbolicFitEnabled = af.WeightedHyperbolicFitEnabled;
                afOptions.HyperbolicFitModel = af.HyperbolicFitModel;
                afOptions.FitRejectionCriterion = af.FitRejectionCriterion;
                afOptions.ReducedChiSquaredRejectionThreshold = af.ReducedChiSquaredRejectionThreshold;
            }

            // AutoFocus options: profile-owned subset (step size deliberately not written — replay re-derives it).
            if (af != null) {
                var focuserSettings = profileService.ActiveProfile.FocuserSettings;
                focuserSettings.AutoFocusMethod = af.AutoFocusMethod;
                focuserSettings.AutoFocusCurveFitting = af.AutoFocusCurveFitting;
                focuserSettings.AutoFocusInitialOffsetSteps = af.AutoFocusInitialOffsetSteps;
                focuserSettings.AutoFocusNumberOfFramesPerPoint = af.FramesPerPoint;
                focuserSettings.AutoFocusTotalNumberOfAttempts = af.TotalNumberOfAttempts;
                focuserSettings.AutoFocusUseBrightestStars = af.NumberOfAFStars;
                profileService.ActiveProfile.ImageSettings.DebayerImage = af.DebayerImage;
            }

            // ROI / region geometry.
            var regions = metadata.Regions;
            if (regions != null) {
                var focuserSettings = profileService.ActiveProfile.FocuserSettings;
                focuserSettings.AutoFocusInnerCropRatio = regions.AutoFocusInnerCropRatio;
                focuserSettings.AutoFocusOuterCropRatio = regions.AutoFocusOuterCropRatio;
                if (regions.IsInspectorRun) {
                    var inspectorOptions = HocusFocusPlugin.InspectorOptions;
                    if (inspectorOptions != null) {
                        inspectorOptions.SensorROI = regions.SensorROI;
                        inspectorOptions.CornersROI = regions.CornersROI;
                        inspectorOptions.SensorCurveModelEnabled = regions.SensorCurveModelEnabled;
                    }
                }
            }
        }
    }
}
