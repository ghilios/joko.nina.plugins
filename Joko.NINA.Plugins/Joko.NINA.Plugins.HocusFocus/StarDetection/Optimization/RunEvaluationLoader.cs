#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Logger = NINA.Core.Utility.Logger;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// A loaded auto-focus run ready for optimization: the <see cref="Data"/> the optimizer evaluates, the
    /// <see cref="Seed"/> params the search starts from (the AF-base params for the first frame, carrying
    /// PixelScale + Region + ModelPSF=false), and the run's <see cref="AfOptions"/> (for the current step size).
    /// </summary>
    public sealed class LoadedRun {
        public RunEvaluationData Data { get; set; }

        /// <summary>The params the optimizer starts its search from. In production this is the fully-DEFAULT
        /// detector params (see <see cref="IHocusFocusStarDetection.GetDefaultStarDetectorParams"/>), carrying
        /// PixelScale + Region + ModelPSF=false.</summary>
        public StarDetectorParams Seed { get; set; }

        private StarDetectorParams baseline;

        /// <summary>The user's CURRENT settings — the displayed "before" baseline (σ, cost J, the "Current" curve,
        /// and the changed-parameters before-column). Defaults to <see cref="Seed"/> when not separately provided,
        /// so a caller/test that sets only <see cref="Seed"/> keeps the legacy "the seed is the baseline" behavior.</summary>
        public StarDetectorParams Baseline {
            get => baseline ?? Seed;
            set => baseline = value;
        }

        public AutoFocusEngineOptions AfOptions { get; set; }
    }

    /// <summary>
    /// Loads a saved auto-focus attempt folder into a <see cref="LoadedRun"/> the optimizer can evaluate.
    /// Extracted so the wizard VM can be unit-tested against a fake loader (the concrete
    /// <see cref="RunEvaluationLoader"/> is heavily NINA-coupled).
    /// </summary>
    public interface IRunEvaluationLoader {

        Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, CancellationToken token);

        /// <summary>
        /// Loads the saved attempt and threads optional ground-truth <paramref name="labels"/> into the resulting
        /// <see cref="RunEvaluationData"/> so the optimizer's recall/precision objective term is active. Passing
        /// <c>null</c> is identical to the no-labels overload. Used by the wizard's re-optimize-with-labels path,
        /// which re-reads the run from disk (the first pass disposed its in-memory data) and feeds the labels the
        /// user just made.
        /// </summary>
        Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, CancellationToken token);

        /// <summary>
        /// As the labels overload, but reports determinate per-frame loading progress (each rendered exposure) via
        /// <paramref name="progress"/> so the wizard's progress bar advances during the long initial disk-render
        /// phase instead of sitting idle. The other overloads delegate here with <c>progress: null</c>; passing
        /// <c>null</c> progress is identical to them.
        /// </summary>
        Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, CancellationToken token);
    }

    /// <summary>
    /// Wizard-side loader: turns a saved auto-focus attempt folder into a <see cref="RunEvaluationData"/> the
    /// optimizer can evaluate. Unlike the pure optimizer (T2) and the image-source-agnostic
    /// <see cref="RunEvaluationData"/> (T3 core), this is intentionally NINA-coupled — it loads exposures via the
    /// image-data factory + imaging mediator (mirroring <c>InspectorVM.LoadSavedFile</c>) and runs the real
    /// HocusFocus star detector. Its end-to-end path is exercised in T6 against a real AF run folder; the unit
    /// tests cover only construction/argument validation.
    /// </summary>
    public sealed class RunEvaluationLoader : IRunEvaluationLoader {
        private readonly IProfileService profileService;
        private readonly IImageDataFactory imageDataFactory;
        private readonly IImagingMediator imagingMediator;
        private readonly IAutoFocusEngine autoFocusEngine;
        private readonly IHocusFocusStarDetection detection;
        private readonly IAlglibAPI alglibAPI;

        public RunEvaluationLoader(
            IProfileService profileService,
            IImageDataFactory imageDataFactory,
            IImagingMediator imagingMediator,
            IAutoFocusEngine autoFocusEngine,
            IHocusFocusStarDetection detection,
            IAlglibAPI alglibAPI = null) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.imageDataFactory = imageDataFactory ?? throw new ArgumentNullException(nameof(imageDataFactory));
            this.imagingMediator = imagingMediator ?? throw new ArgumentNullException(nameof(imagingMediator));
            this.autoFocusEngine = autoFocusEngine ?? throw new ArgumentNullException(nameof(autoFocusEngine));
            this.detection = detection ?? throw new ArgumentNullException(nameof(detection));
            // The plugin singleton is the production source; allow null here only so the dependency is overridable.
            this.alglibAPI = alglibAPI ?? HocusFocusPlugin.AlglibAPI;
        }

        /// <summary>
        /// Loads the saved attempt at <paramref name="attemptFolderPath"/> into a <see cref="LoadedRun"/>:
        /// every saved exposure is rendered once (detectStars: false), the AF-base detector params for the first
        /// image become the optimizer seed, and the detection delegate re-runs the HocusFocus detector with each
        /// candidate params on the already-loaded frames. The fit config is taken from the run's AF options so the
        /// optimizer's curve fit matches the AF engine's.
        /// </summary>
        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, CancellationToken token) =>
            LoadSavedRunAsync(attemptFolderPath, region, labels: null, progress: null, token);

        /// <inheritdoc />
        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, CancellationToken token) =>
            LoadSavedRunAsync(attemptFolderPath, region, labels, progress: null, token);

        /// <inheritdoc />
        public async Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, CancellationToken token) {
            if (string.IsNullOrEmpty(attemptFolderPath)) {
                throw new ArgumentException("attemptFolderPath is required", nameof(attemptFolderPath));
            }
            region = region ?? StarDetectionRegion.Full;

            var attempt = autoFocusEngine.LoadSavedAutoFocusAttempt(attemptFolderPath);
            var afOptions = autoFocusEngine.GetOptions(attempt);

            if (attempt.SavedImages == null || attempt.SavedImages.Count == 0) {
                throw new InvalidOperationException($"No saved images found in {attemptFolderPath}");
            }

            // Render every saved exposure once (detectStars: false, mirroring InspectorVM.LoadSavedFile). Report
            // determinate per-frame progress so the wizard's bar advances during this long disk-render phase.
            var frames = new List<RunFrame>(attempt.SavedImages.Count);
            IRenderedImage firstImage = null;
            var totalImages = attempt.SavedImages.Count;
            var loadedCount = 0;
            foreach (var savedImage in attempt.SavedImages) {
                token.ThrowIfCancellationRequested();
                var rendered = await LoadRenderedImageAsync(savedImage, afOptions, token).ConfigureAwait(false);
                if (firstImage == null) {
                    firstImage = rendered;
                }
                frames.Add(new RunFrame {
                    FrameId = savedImage.Path,
                    FocuserPosition = savedImage.FocuserPosition,
                    Image = rendered
                });
                progress?.Report(new RunLoadProgress(++loadedCount, totalImages));
            }

            // Seed = fully-DEFAULT AF-base detector params for the first image (PixelScale + Region + ModelPSF=false):
            // the optimizer starts its search from a clean, reproducible default regardless of the user's current
            // settings. Baseline = the user's CURRENT settings, kept for the displayed "before" comparison (σ, cost J,
            // the "Current" curve). Both carry the same image context; neither writes to options.
            var seed = detection.GetDefaultStarDetectorParams(firstImage, region, isAutoFocus: true);
            var baseline = detection.GetStarDetectorParams(firstImage, region, isAutoFocus: true);

            // Every detection these params drive is an OPTIMIZER evaluation — the seed (and the candidates the
            // optimizer materializes from it via Clone) and the baseline are each detected across all frames many
            // thousands of times during a single optimization. Suppress the per-region "Average HFR" INFO summary
            // so one optimization run does not flood the NINA log with ~10k+ INFO lines. The flag is output-neutral
            // and excluded from the detection cache key, so detection results stay byte-identical; only the wizard
            // path sets it (TestApp diagnostics build RunEvaluationData directly and are unaffected).
            seed.SuppressInfoLogging = true;
            baseline.SuppressInfoLogging = true;

            // AF detection params: the auto-focus detection contract (sigma rejections + AF flag). NumberOfAFStars
            // is left at 0 so detection keeps every accepted star — the optimizer scores on the full accepted set,
            // not the brightest-N subset the live AF picks for centroiding.
            var hocusParams = new HocusFocusDetectionParams {
                IsAutoFocus = true,
                NumberOfAFStars = 0
            };

            var fitConfig = new RunFitConfig {
                StepSize = afOptions.AutoFocusStepSize,
                UseWeights = afOptions.WeightedHyperbolicFitEnabled,
                MaxOutlierRejections = afOptions.MaxOutlierRejections,
                RejectionConfidence = afOptions.OutlierRejectionConfidence,
                PreferredModel = afOptions.HyperbolicFitModel
            };

            // Split detector: caches the expensive early detection per (frame, early key) and reuses it across the
            // many candidate evaluations that change only late-stage gate params (the ~2× optimizer speedup). The
            // mapping to FrameDetectionResult is identical to the legacy monolithic delegate, so results are
            // unchanged — only the early stage is skipped on a late-only move.
            var splitDetector = new HocusFocusSplitFrameDetector(detection, hocusParams);

            var runId = attempt.FolderPath ?? attemptFolderPath;
            var data = new RunEvaluationData(runId, frames, splitDetector, alglibAPI, fitConfig, labels);

            Logger.Info($"Loaded saved AF run '{runId}' for optimization: {frames.Count} frames, step size {fitConfig.StepSize}");
            return new LoadedRun {
                Data = data,
                Seed = seed,
                Baseline = baseline,
                AfOptions = afOptions
            };
        }

        private async Task<IRenderedImage> LoadRenderedImageAsync(SavedAutoFocusImage savedImage, AutoFocusEngineOptions afOptions, CancellationToken token) {
            var imageData = await imageDataFactory.CreateFromFile(
                savedImage.Path, savedImage.BitDepth, savedImage.IsBayered,
                profileService.ActiveProfile.CameraSettings.RawConverter, token).ConfigureAwait(false);

            // Auto-stretch unless contrast-detection statistics are in play (mirrors InspectorVM.LoadSavedFile).
            var autoStretch = true;
            if (afOptions.AutoFocusMethod == AFMethodEnum.CONTRASTDETECTION
                && profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod == ContrastDetectionMethodEnum.Statistics) {
                autoStretch = false;
            }

            var prepareParameters = new PrepareImageParameters(autoStretch: autoStretch, detectStars: false);
            return await imagingMediator.PrepareImage(imageData, prepareParameters, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Drives the real HocusFocus detector in two phases so the optimizer can cache the expensive early stage.
        /// The frame payload is the loaded <see cref="IRenderedImage"/>; the opaque context is a
        /// <see cref="HocusFocusDetectionContext"/>. The <see cref="FrameDetectionResult"/> mapping is identical to
        /// the legacy monolithic delegate (HFR, σ, accepted-star count + centers), so results are unchanged.
        /// </summary>
        private sealed class HocusFocusSplitFrameDetector : RunEvaluationData.ISplitFrameDetector {
            private readonly IHocusFocusStarDetection detection;
            private readonly HocusFocusDetectionParams hocusParams;

            public HocusFocusSplitFrameDetector(IHocusFocusStarDetection detection, HocusFocusDetectionParams hocusParams) {
                this.detection = detection;
                this.hocusParams = hocusParams;
            }

            public string ComputeEarlyKey(StarDetectorParams p) => detection.ComputeEarlyCacheKey(p);

            public async Task<IDisposable> BuildContextAsync(object frameImage, StarDetectorParams p, CancellationToken token) {
                return await detection.BuildDetectionContext((IRenderedImage)frameImage, hocusParams, p, null, token).ConfigureAwait(false);
            }

            public FrameDetectionResult GateAndMeasure(IDisposable context, StarDetectorParams p) {
                var result = detection.GateAndMeasure((HocusFocusDetectionContext)context, p, CancellationToken.None);
                var centers = result.StarList == null
                    ? (IReadOnlyList<(double X, double Y)>)Array.Empty<(double X, double Y)>()
                    : result.StarList.Select(s => ((double)s.Position.X, (double)s.Position.Y)).ToList();
                // Per-star HFRs, PARALLEL to centers (same StarList), for the extreme-HFR outlier penalty.
                var hfrs = result.StarList == null
                    ? (IReadOnlyList<double>)Array.Empty<double>()
                    : result.StarList.Select(s => s.HFR).ToList();
                var imageSize = (result as HocusFocusStarDetectionResult)?.ImageSize ?? System.Drawing.Size.Empty;
                return new FrameDetectionResult {
                    AverageHFR = result.AverageHFR,
                    HFRStdDev = result.HFRStdDev,
                    StarCount = result.DetectedStars,
                    StarCenters = centers,
                    StarHFRs = hfrs,
                    ImageWidth = imageSize.Width,
                    ImageHeight = imageSize.Height,
                    // Relaxation-admitted accepted-star count for this frame (0 unless a defocus-aware gate is on),
                    // surfaced from the detector metrics so the optimizer can apply its precision penalty.
                    RelaxationAdmittedCount = (result as HocusFocusStarDetectionResult)?.Metrics?.RelaxationAdmittedCount ?? 0
                };
            }
        }
    }
}
