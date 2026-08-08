#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.Interfaces;
using OpenCvSharp;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static NINA.Joko.Plugins.HocusFocus.Utility.CvImageUtility;
using MultiStopWatch = NINA.Joko.Plugins.HocusFocus.Utility.MultiStopWatch;
using System.IO;
using System.Text;
using System.Diagnostics;
using NINA.Image.ImageAnalysis;
using System.Windows.Media;
using NINA.Image.ImageData;
using NINA.Core.Utility.Notification;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class StarDetector : IStarDetector {
        // Bump whenever star-detection logic changes; invalidates the saved-run detection cache so replays
        // re-detect. The version is stamped onto HocusFocusStarDetectionResult.DetectorVersion and folded into
        // HocusFocusStarDetectionResult.CacheKey, so a later reuse-side task can reject any saved
        // _star_detection_result.json that was produced by a different detector version (or different params).
        public const int StarDetectorVersion = 1;

        private readonly IAlglibAPI alglibAPI;

        public StarDetector(IAlglibAPI alglibAPI) {
            this.alglibAPI = alglibAPI;
        }

        /// <summary>
        /// Computes a stable, culture-invariant cache key for a saved star-detection result. The key is the
        /// SHA-256 (lowercase hex) of a canonical string built from
        /// <see cref="StarDetectorParams.ToCanonicalCacheString()"/> (which reflects over every public detection
        /// param — region geometry included — sorted by name, formatted with
        /// <see cref="System.Globalization.CultureInfo.InvariantCulture"/>, and excludes only an explicit
        /// denylist of provably output-neutral fields) plus the <see cref="StarDetectorVersion"/>. Two results
        /// are interchangeable for reuse only when this key matches: identical detection params (including
        /// region) AND identical detector logic version.
        ///
        /// The key is culture-invariant: identical params yield the identical key under any locale (e.g. de-DE
        /// vs en-US). Its safe failure mode is a spurious cache miss, never stale reuse — a param toggle that
        /// affects detection output is guaranteed to change the key (the canonical builder includes a field by
        /// default and drops only the commented denylist).
        ///
        /// This is the single source of truth for the cache-key computation so the later reuse-side task can
        /// recompute the expected key for the current params/version and compare. <paramref name="p"/> must be
        /// the effective <see cref="StarDetectorParams"/> actually used for detection (region included).
        /// </summary>
        public static string ComputeCacheKey(StarDetectorParams p) {
            if (p == null) {
                throw new ArgumentNullException(nameof(p));
            }

            // ToCanonicalCacheString() emits all detection-output-affecting params (region included) in a fixed
            // (name-sorted) order, every value formatted with InvariantCulture. Prefix the version so a version
            // bump alone changes the key even when params are byte-identical.
            var canonical = $"v{StarDetectorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{p.ToCanonicalCacheString()}";
            var bytes = Encoding.UTF8.GetBytes(canonical);
            using (var sha = System.Security.Cryptography.SHA256.Create()) {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) {
                    sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        // The EARLY-stage detection params: everything BuildDetectionContext reads to produce a DetectionContext
        // (hotpixel filtering, noise reduction, the structure map + wavelet, binarization, candidate flood-fill,
        // and the early-only metrics). A cached DetectionContext is safe to reuse across GateAndMeasure calls ONLY
        // when this key matches, so the SAFE failure mode is a spurious recompute, never stale reuse: a param is
        // included here if it changes the prepared measurement image, the candidate region set, the two noise
        // sigmas, OR any early metric (HotpixelCount/StructureCandidates/SaturatedPixelCount). When in doubt a
        // param is INCLUDED (over-keying only costs a recompute). The list is a deliberate ALLOW-list (not a
        // denylist over all params) precisely so a newly-added late-only param can never silently corrupt reuse —
        // it would simply be absent here and force a recompute until explicitly classified.
        //
        // Notes on a few subtle entries:
        //  - NoiseClippingMultiplier: sets the K-σ clip AND the binarize threshold ⇒ changes the candidate set.
        //  - LocallyAdaptiveBinarization / AdaptiveNoiseBlockSize: swap the scalar binarize threshold for a
        //    per-block local-median+NC·local-σ surface (and set its granularity) ⇒ change the candidate set.
        //  - StarMeasurementNoiseReductionEnabled / NoiseReductionRadius / Hotpixel*: drive SrcImagePreparation and
        //    the structure-map source, and which of the two noise estimates is computed (measurement vs structure).
        //  - SaturationThreshold: read by EvaluateGlobalMetrics (early) to tally SaturatedPixelCount, so it is early
        //    even though it is ALSO used by the late saturated-bounds gate (the late use is harmless to over-key).
        //  - Region: the ROI submat (outer) and the inner-crop clearing both change which candidates are collected.
        //  - HotpixelFilterRadius: gates the pipeline (only radius 1 supported) and would change filtering if ever
        //    extended; included for safety.
        // Default extra wavelet layers applied for donut recovery when the master DefocusAwareDonutDetection is on
        // but the explicit DefocusAwareStructure axis is off — enough to keep large defocused donuts from being
        // erased by the structure-removal wavelet (StructureLayers default 4 + 2 ≈ the user's working value of 6).
        private const int DonutDefaultStructureLayerBoost = 2;

        // Donut recovery clip cap. A defocused donut spreads its flux thinly, so each ring pixel sits only a few
        // sigma above background. An aggressive StarClippingMultiplier (calibrated for COMPACT stars, and often
        // pushed high by the optimizer) then clips the ENTIRE thin ring during flux/centroid/HFR measurement,
        // which (a) starves the surviving-pixel count → the Degenerate guard fires, and (b) leaves only the
        // brightest arc → the flux-weighted centroid is pulled off the geometric center → the NotCentered gate
        // fires. For an EXTENDED candidate the effective clip multiplier is capped at this honest default (2.0,
        // the calibrated uniform tau from the sigma-consistency work) so the full ring participates. See
        // EffectiveClipMultiplier; ungated profiles with StarClippingMultiplier <= this cap are unaffected.
        private const double DonutClipMultiplierCap = 2.0;

        // Numerical floor on the per-block local σ used by spatially-adaptive binarization (in normalized image
        // units). Guards a degenerate dead-flat block from a zero/near-zero local σ; small enough not to suppress
        // the genuine low-noise local thresholds in clean regions that drive the recall gain.
        private const float AdaptiveBinarizationSigmaFloor = 1e-6f;

        // Result of the structure-map source noise estimate task: the global kappa-sigma σ used for the scalar
        // binarize threshold, PLUS (only when LocallyAdaptiveBinarization is on) the per-block local-σ grid sampled
        // on that SAME noise-reduced source (F4 σ-consistency), captured inside the task before the source is
        // disposed. SigmaGrid is null on the legacy (flag-off) path.
        private readonly struct StructureNoiseEstimate {
            public StructureNoiseEstimate(CvImageUtility.KappaSigmaNoiseEstimateResult noise, CvImageUtility.LocalBackgroundGrid sigmaGrid) {
                Noise = noise;
                SigmaGrid = sigmaGrid;
            }

            public CvImageUtility.KappaSigmaNoiseEstimateResult Noise { get; }
            public CvImageUtility.LocalBackgroundGrid SigmaGrid { get; }
        }

        private static readonly HashSet<string> EarlyCacheKeyProperties = new HashSet<string>(StringComparer.Ordinal) {
            nameof(StarDetectorParams.HotpixelFiltering),
            nameof(StarDetectorParams.HotpixelThresholdingEnabled),
            nameof(StarDetectorParams.HotpixelThreshold),
            nameof(StarDetectorParams.HotpixelFilterRadius),
            nameof(StarDetectorParams.StarMeasurementNoiseReductionEnabled),
            nameof(StarDetectorParams.NoiseReductionRadius),
            nameof(StarDetectorParams.NoiseClippingMultiplier),
            // Spatially-adaptive binarization: the master flag swaps the scalar threshold for a per-block
            // local-median+NC·local-σ surface, and the block size sets that surface's granularity — both change
            // candidate formation, so both MUST be early-keyed.
            nameof(StarDetectorParams.LocallyAdaptiveBinarization),
            nameof(StarDetectorParams.AdaptiveNoiseBlockSize),
            nameof(StarDetectorParams.StructureLayers),
            // Fast-wavelet implementation swap: float-rounding-level structure-map differences can flip
            // near-threshold pixels, so contexts must not be reused across the two implementations.
            nameof(StarDetectorParams.FastAtrousWavelets),
            nameof(StarDetectorParams.DefocusAwareStructure),
            nameof(StarDetectorParams.StructureLayerBoost),
            nameof(StarDetectorParams.StructureDilationSize),
            nameof(StarDetectorParams.StructureDilationCount),
            nameof(StarDetectorParams.SaturationThreshold),
            // Defocus-aware donut detection: the MASTER flag gates the early morphological-close (and the close
            // kernel size), so both change candidate formation and MUST be early-keyed. The hole-fill / streak /
            // bloom knobs are LATE (gate-only) and are deliberately NOT listed here.
            nameof(StarDetectorParams.DefocusAwareDonutDetection),
            nameof(StarDetectorParams.DonutMorphCloseSize),
            // Software binning resamples the frame before ANY of the above run, so it changes candidate formation
            // outright — a context built at one factor can never be reused at another.
            nameof(StarDetectorParams.DetectionBinning),
            nameof(StarDetectorParams.Region)
        };

        /// <summary>
        /// Computes a deterministic, culture-invariant cache key over ONLY the EARLY-stage detection params (plus
        /// the detector version), for keying a reusable <see cref="DetectionContext"/>. Two parameter bundles that
        /// produce this same key yield a byte-identical early context (prepared measurement image, candidate
        /// regions, both noise sigmas, and the early metrics), so a context built under one bundle may be reused by
        /// <see cref="GateAndMeasure"/> under the other. Built from the same canonical mechanism as
        /// <see cref="ComputeCacheKey"/> but restricted to <see cref="EarlyCacheKeyProperties"/>. Like
        /// <see cref="ComputeCacheKey"/>, the safe failure mode is a spurious miss (recompute), never stale reuse.
        /// </summary>
        // Instance wrapper so IStarDetector can expose the early-key computation (the canonical logic stays static).
        string IStarDetector.ComputeEarlyCacheKey(StarDetectorParams p) => ComputeEarlyCacheKey(p);

        /// <summary>
        /// True when <paramref name="name"/> is an EARLY-stage detection param (a member of
        /// <see cref="EarlyCacheKeyProperties"/>) — i.e. a param that feeds <c>BuildDetectionContext</c> and is
        /// therefore folded into <see cref="ComputeEarlyCacheKey"/>. This is the single, public source of truth
        /// for early-vs-late param classification: callers (e.g. the optimizer's staged search) must consult this
        /// rather than maintaining a second copy of the early-axis list. Synthetic/non-property names (e.g. the
        /// optimizer's combined defocus-aware-gates alias) are not members and are correctly reported LATE.
        /// </summary>
        public static bool IsEarlyCacheKeyParameter(string name) => name != null && EarlyCacheKeyProperties.Contains(name);

        public static string ComputeEarlyCacheKey(StarDetectorParams p) {
            if (p == null) {
                throw new ArgumentNullException(nameof(p));
            }

            var canonical = $"early-v{StarDetectorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{p.ToCanonicalCacheString(EarlyCacheKeyProperties)}";
            var bytes = Encoding.UTF8.GetBytes(canonical);
            using (var sha = System.Security.Cryptography.SHA256.Create()) {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) {
                    sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        private class StarCandidate {
            public Point2d Center;
            public double CenterBrightness;
            public double Background;
            public LocalBackgroundPlane BackgroundPlane;
            public double NormalizedBrightness;
            public double TotalFlux;
            public double Peak;
            public int PixelCount;
            // Clip-survivor count: pixels actually summed into TotalFlux (raw > background + clipMargin). Used by
            // the donut-aware integrated-flux sensitivity path.
            public int UnclippedPixelCount;
            public Rect StarBoundingBox;
            public double StarMedian;
            public bool ContaminationSuspected;

            // Contamination diagnostics carry fields, populated in ComputeStarParameters only when
            // StarDetectorParams.CollectContaminationDiagnostics is true. Null otherwise (zero overhead).
            public ContaminationDiagnosticRecord ContaminationDiagnostics;
        }

        private static void MaybeSaveIntermediateImage(Mat image, StarDetectorParams p, string filename) {
            var saveIntermediate = !string.IsNullOrEmpty(p.SaveIntermediateFilesPath) && Directory.Exists(p.SaveIntermediateFilesPath);
            if (!saveIntermediate) {
                return;
            }

            var targetPath = Path.Combine(p.SaveIntermediateFilesPath, filename);
            image.SaveImage(targetPath);
        }

        private static void MaybeSaveIntermediateText(string text, StarDetectorParams p, string filename) {
            var saveIntermediate = !string.IsNullOrEmpty(p.SaveIntermediateFilesPath) && Directory.Exists(p.SaveIntermediateFilesPath);
            if (!saveIntermediate) {
                return;
            }
            File.WriteAllText(Path.Combine(p.SaveIntermediateFilesPath, filename), text);
        }

        private static void MaybeSaveIntermediateStars(List<Star> stars, StarDetectorParams p, string filename) {
            var saveIntermediate = !string.IsNullOrEmpty(p.SaveIntermediateFilesPath) && Directory.Exists(p.SaveIntermediateFilesPath);
            if (!saveIntermediate) {
                return;
            }

            var sb = new StringBuilder();
            int index = 0;
            foreach (var star in stars) {
                sb.AppendLine($"{index++} => {star.ToString()}");
            }
            File.WriteAllText(Path.Combine(p.SaveIntermediateFilesPath, filename), sb.ToString());
        }

        public async Task<HocusFocusStarDetectorResult> Detect(Mat srcImage, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var resourceTracker = new ResourcesTracker();
            try {
                return await DetectImpl(srcImage, resourceTracker, p, false, progress, token);
            } finally {
                // Cleanup
                resourceTracker.Dispose();
                progress?.Report(new ApplicationStatus() { });
            }
        }

        public async Task<HocusFocusStarDetectorResult> Detect(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var resourceTracker = new ResourcesTracker();
            try {
                var srcImage = resourceTracker.T(PrepareSrcImageFromRenderedImage(image, p, out var hotpixelFilteringApplied, out var numHotpixels));
                var result = await DetectImpl(srcImage, resourceTracker, p, hotpixelFilteringApplied, progress, token);
                if (numHotpixels.HasValue) {
                    result.Metrics.HotpixelCount = numHotpixels.Value;
                }
                return result;
            } catch (TypeInitializationException e) {
                Logger.Error(e, "TypeInitialization exception while performing star detection. This indicates the OpenCV library couldn't be loaded. If you have a Windows N SKU, install the Media Pack");
                Notification.ShowError("Could ont load the OpenCV library. If you have a Windows N SKU, install the Media Pack");
                throw;
            } finally {
                // Cleanup
                resourceTracker.Dispose();
                progress?.Report(new ApplicationStatus() { });
            }
        }

        /// <summary>
        /// Converts an <see cref="IRenderedImage"/> into the float source Mat the pipeline consumes, applying the
        /// CFA hotpixel filter + debayer when the image is bayered and hotpixel thresholding is on (otherwise a
        /// plain <c>ToOpenCVMat</c>). This is the EXACT conversion the monolithic <see cref="Detect(IRenderedImage, StarDetectorParams, IProgress{ApplicationStatus}, CancellationToken)"/>
        /// used to perform inline; it is extracted so the optimizer's split path produces a byte-identical source
        /// image. The returned Mat is NOT tracked — the caller owns it.
        /// </summary>
        private static Mat PrepareSrcImageFromRenderedImage(IRenderedImage image, StarDetectorParams p, out bool hotpixelFilteringApplied, out long? numHotpixels) {
            var debayeredImage = image as IDebayeredImage;
            hotpixelFilteringApplied = false;
            numHotpixels = null;
            if (debayeredImage != null && p.HotpixelFiltering && p.HotpixelThresholdingEnabled) {
                var rawImageDataCopy = new ushort[debayeredImage.RawImageData.Data.FlatArray.Length];
                Buffer.BlockCopy(debayeredImage.RawImageData.Data.FlatArray, 0, rawImageDataCopy, 0, debayeredImage.RawImageData.Data.FlatArray.Length * sizeof(ushort));

                var props = debayeredImage.RawImageData.Properties;
                var rawImageData = new RawImageData(rawImageDataCopy, width: props.Width, height: props.Height);

                var threshold = (ushort)(p.HotpixelThreshold * (1 << props.BitDepth));
                numHotpixels = HotpixelFiltering.CFAHotpixelFilter(rawImageData, debayeredImage.BayerPattern, threshold);
                var bitmapSource = ImageUtility.CreateSourceFromArray(new ImageArray(rawImageDataCopy), props, PixelFormats.Gray16);
                var debayeredImageData = ImageUtility.Debayer(bitmapSource, pf: System.Drawing.Imaging.PixelFormat.Format16bppGrayScale, saveColorChannels: false, saveLumChannel: true, bayerPattern: debayeredImage.BayerPattern);
                hotpixelFilteringApplied = true;

                return CvImageUtility.ToOpenCVMat(debayeredImageData.Data.Lum, bpp: props.BitDepth, width: props.Width, height: props.Height);
            } else {
                return CvImageUtility.ToOpenCVMat(image);
            }
        }

        /// <summary>
        /// EARLY phase for an <see cref="IRenderedImage"/>: performs the same conversion as
        /// <see cref="Detect(IRenderedImage, StarDetectorParams, IProgress{ApplicationStatus}, CancellationToken)"/>
        /// then builds the reusable <see cref="DetectionContext"/> (carrying the CFA hotpixel count when the
        /// debayered-hotpixel path ran). See <see cref="BuildDetectionContext(Mat, StarDetectorParams, IProgress{ApplicationStatus}, CancellationToken)"/>.
        /// </summary>
        public async Task<DetectionContext> BuildDetectionContext(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (p.HotpixelFiltering && p.HotpixelFilterRadius != 1) {
                throw new NotImplementedException("Only hotpixel filter radius of 1 currently supported");
            }
            // PrepareSrcImageFromRenderedImage returns a freshly-allocated, untracked Mat that the context will own;
            // no extra clone needed (unlike the Mat overload, which clones the caller's Mat).
            var srcImage = PrepareSrcImageFromRenderedImage(image, p, out var hotpixelFilteringApplied, out var numHotpixels);
            var ctx = await BuildDetectionContextInternal(srcImage, resourceTracker: null, p, hotpixelFilteringApplied, progress, token);
            ctx.DebayerHotpixelCount = numHotpixels;
            return ctx;
        }

        /// <summary>
        /// A reusable intermediate produced by <see cref="BuildDetectionContext"/> from ONLY the early-stage
        /// params (see <see cref="EarlyCacheKeyProperties"/>): the prepared, read-only measurement image, the full
        /// candidate region set from the flood-fill, both noise sigmas, the ROI offset, the early-only metric
        /// counters, and the debug data. <see cref="GateAndMeasure"/> consumes it to produce the final result and
        /// may be called repeatedly with different LATE-stage params, reusing the same context.
        ///
        /// <para>Ownership: the context owns <see cref="MeasurementImage"/> and disposes it (<see cref="Dispose"/>).
        /// The late stage only READS the image, so the same context is safe to reuse across late-only param
        /// changes. Disposing the context releases the (potentially large, 61 MP ≈ 244 MB) source Mat.</para>
        /// </summary>
        public sealed class DetectionContext : IDisposable {
            internal Mat MeasurementImage;                       // prepared source image, read-only for the late stage
            internal System.Drawing.Size FullImageSize;          // full-frame dimensions BEFORE any ROI crop (star centers are full-frame)
            internal List<StarCandidateRegion> Candidates;      // ALL flood-fill candidates (no late gate applied)
            internal double StructureNoiseSigma;                 // K-σ on the noise-reduced structure-map source
            internal double MeasurementNoiseSigma;              // K-σ on the image actually sampled for measurement
            internal Rect? RoiRect;                              // ROI offset to add back to outputs (null if full frame)
            // The software binning factor MeasurementImage was resampled by (1 = none). Carried here — rather than
            // re-read from the late params — because it is an EARLY property: the late stage must scale its outputs
            // by the factor the context was actually built at.
            internal int Binning = 1;
            internal DebugData DebugData;
            // Early-only metric counters captured here so GateAndMeasure can seed a fresh metrics with them. These
            // are produced by the early pipeline (hotpixel filter) and the candidate flood-fill / global scan.
            internal long HotpixelCount;
            internal int StructureCandidates;
            internal long SaturatedPixelCount;

            // Set on the IRenderedImage debayered-hotpixel path: the CFA hotpixel count to stamp onto the final
            // metrics (mirrors Detect(IRenderedImage), which overwrites Metrics.HotpixelCount with this value). Null
            // when that path did not run, in which case the structure-pipeline HotpixelCount above is authoritative.
            internal long? DebayerHotpixelCount;

            public void Dispose() {
                MeasurementImage?.Dispose();
                MeasurementImage = null;
            }
        }

        private async Task<HocusFocusStarDetectorResult> DetectImpl(Mat srcImage, ResourcesTracker resourceTracker, StarDetectorParams p, bool hotpixelFilterAlreadyApplied, IProgress<ApplicationStatus> progress, CancellationToken token) {
            // The monolithic detect is now exactly: build the early context, then gate + measure. The split is
            // value-preserving — the early stage performs the identical pixel work it always did and the late stage
            // reads it the same way — so output is byte-identical to the pre-split pipeline.
            var ctx = await BuildDetectionContextInternal(srcImage, resourceTracker, p, hotpixelFilterAlreadyApplied, progress, token);
            // BuildDetectionContextInternal tracks the prepared image in resourceTracker (caller disposes it via
            // the tracker in Detect), so do NOT also dispose the context here — that would double-free.
            return await GateAndMeasureInternal(ctx, p, progress, token);
        }

        /// <summary>
        /// EARLY phase: runs the expensive, late-param-independent pipeline (hotpixel filtering, noise reduction,
        /// structure-map + wavelet, binarization, candidate flood-fill, and both noise estimates) and returns a
        /// reusable <see cref="DetectionContext"/>. The returned context OWNS its measurement image; dispose the
        /// context (or call <see cref="GateAndMeasure"/> and dispose afterward) to release it. The result depends
        /// ONLY on the params named in <see cref="EarlyCacheKeyProperties"/> (see <see cref="ComputeEarlyCacheKey"/>),
        /// so it is safe to cache + reuse across <see cref="GateAndMeasure"/> calls whose early key matches.
        /// </summary>
        public async Task<DetectionContext> BuildDetectionContext(Mat srcImage, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (p.HotpixelFiltering && p.HotpixelFilterRadius != 1) {
                throw new NotImplementedException("Only hotpixel filter radius of 1 currently supported");
            }
            // The public split entry points clone the input so the caller's Mat is never mutated and the context's
            // image is independently owned (matching the harness's clone-before-Detect discipline). The monolithic
            // DetectImpl path instead lets BuildDetectionContextInternal mutate the tracker-owned srcImage in place,
            // exactly as before, so behavior on that path is unchanged.
            var ctx = await BuildDetectionContextInternal(srcImage.Clone(), resourceTracker: null, p, hotpixelFilterAlreadyApplied: false, progress, token);
            return ctx;
        }

        /// <summary>
        /// LATE phase: gates + measures the cached candidate regions in <paramref name="ctx"/> using the late-stage
        /// params (Sensitivity, StarClippingMultiplier, PeakResponse, MaxDistortion, MinHFR, StarCenterTolerance,
        /// MinimumStarBoundingBoxSize, BackgroundBoxExpansion, AnalysisSamplingSize, …) and assembles the final
        /// <see cref="HocusFocusStarDetectorResult"/> — identical to what the monolithic <c>Detect</c> would have
        /// produced for the full param bundle. Pure over the context (reads the image, never mutates it), so it may
        /// be called repeatedly on the same context with different late params.
        /// </summary>
        public HocusFocusStarDetectorResult GateAndMeasure(DetectionContext ctx, StarDetectorParams p, CancellationToken token) {
            // The late stage is synchronous except for the optional PSF fit, which is OFF for every cache user
            // (AF/optimize set ModelPSF=false). When PSF IS on this blocks on the PSF tasks (Task.Run-based, no
            // captured sync context), which is acceptable for this non-hot, non-cached usage.
            return GateAndMeasureInternal(ctx, p, progress: null, token).GetAwaiter().GetResult();
        }

        private async Task<DetectionContext> BuildDetectionContextInternal(Mat srcImage, ResourcesTracker resourceTracker, StarDetectorParams p, bool hotpixelFilterAlreadyApplied, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (p.HotpixelFiltering && p.HotpixelFilterRadius != 1) {
                throw new NotImplementedException("Only hotpixel filter radius of 1 currently supported");
            }

            // Full-frame dimensions captured BEFORE any ROI replacement of srcImage below, so downstream consumers
            // (the optimizer's region-coverage metric) can normalize full-frame star centers to ratio coordinates.
            var fullImageSize = new System.Drawing.Size(srcImage.Width, srcImage.Height);

            // When a resourceTracker is supplied (the monolithic DetectImpl path) the prepared srcImage and the
            // scratch Mats are tracked there and freed by the caller. When it is null (the public split path) we
            // own srcImage directly (handed to the context) and use a local tracker for the transient scratch Mats,
            // which is disposed before returning so only the context's image survives.
            var ownsLocalTracker = resourceTracker == null;
            var scratch = resourceTracker ?? new ResourcesTracker();

            // The Mat destined for the context: it is owned here until the context is successfully constructed, after
            // which the context owns it. If the early pipeline throws (cancellation, OOM, CollectStarCandidates, …)
            // before the context exists, this would otherwise be orphaned — it is registered with NEITHER tracker on
            // the split path, and the ROI-replacement clone is untracked on BOTH paths. So we hold it in a local and
            // dispose it in the finally when no context was produced.
            //   - Split path (ownsLocalTracker): we own the incoming srcImage from entry.
            //   - Monolithic path: the caller owns the incoming srcImage (and tracks it), so we must NOT dispose it
            //     here — start null and only adopt the ROI-replacement clone we make below.
            Mat liveOwnedImage = ownsLocalTracker ? srcImage : null;
            var contextProduced = false;
            try {
                MaybeSaveIntermediateText(p.ToString(), p, "00-params.txt");
                var metrics = new StarDetectorMetrics();
                using (var stopWatch = MultiStopWatch.Measure()) {
                    var debugData = new DebugData();

                    Rect? roiRect = null;
                    if (p.Region.OuterBoundary.Height < 1.0d || p.Region.OuterBoundary.Width < 1.0d) {
                        roiRect = new Rect(
                            (int)Math.Floor(srcImage.Cols * p.Region.OuterBoundary.StartX),
                            (int)Math.Floor(srcImage.Rows * p.Region.OuterBoundary.StartY),
                            (int)(srcImage.Cols * p.Region.OuterBoundary.Width),
                            (int)(srcImage.Rows * p.Region.OuterBoundary.Height));
                        debugData.DetectionROI = roiRect.Value.ToDrawingRectangle();

                        var roiImage = srcImage.SubMat(roiRect.Value).Clone();
                        srcImage.Dispose();
                        srcImage = roiImage;
                        // From here srcImage is a clone this method owns (the original was disposed just above). Adopt
                        // it as the live owned Mat on BOTH paths so a later early-stage throw disposes the ROI clone
                        // rather than orphaning it — on the monolithic path the caller's original is already gone.
                        liveOwnedImage = roiImage;
                    } else {
                        debugData.DetectionROI = new System.Drawing.Rectangle(0, 0, srcImage.Width, srcImage.Height);
                    }

                    // Software binning: from here the WHOLE pipeline runs in binned pixels, so every pixel-unit
                    // param (MinHFR, MinimumStarBoundingBoxSize, NoiseReductionRadius, ...) stays in the range it
                    // was calibrated for regardless of the rig's pixel scale. GateAndMeasureInternal scales every
                    // pixel-space output back to source pixels before returning, so callers never see binned units.
                    //
                    // The hotpixel filter is hoisted ABOVE the bin: a hot pixel averaged into its block is no longer
                    // a single-pixel outlier at a recognizable amplitude, so it has to die at native resolution.
                    // Step 1 below is then told it has already run. (Bayered frames already get their CFA hotpixel
                    // pass at native resolution in PrepareSrcImageFromRenderedImage, so both paths agree.)
                    var binning = Math.Max(1, p.DetectionBinning);
                    if (binning > 1) {
                        if (!hotpixelFilterAlreadyApplied && (p.HotpixelFiltering || (p.NoiseReductionRadius > 0 && p.StarMeasurementNoiseReductionEnabled))) {
                            metrics.HotpixelCount = ApplyHotpixelFilter(srcImage, p);
                            hotpixelFilterAlreadyApplied = true;
                        }

                        var binnedImage = CvImageUtility.BinMean(srcImage, binning);
                        srcImage.Dispose();
                        srcImage = binnedImage;
                        // Same ownership handoff as the ROI clone above: this Mat is ours on BOTH paths (the
                        // incoming one was just disposed), so adopt it as the live owned Mat.
                        liveOwnedImage = binnedImage;
                    }

                    MaybeSaveIntermediateImage(srcImage, p, "01-source.tif");

                    if (p.StoreStructureMap) {
                        // Sized from the image actually analyzed. With binning on, the structure map is a BINNED
                        // raster — DebugData.Binning tells the annotator how many display pixels each entry covers.
                        debugData.StructureMap = new byte[srcImage.Width * srcImage.Height];
                    }
                    debugData.Binning = binning;

                    stopWatch.RecordEntry("LoadImage");

                    // Step 1: Perform initial noise reduction and hotpixel filtering
                    progress?.Report(new ApplicationStatus() { Status = "Noise Reduction" });
                    var hotpixelFilteringApplied = hotpixelFilterAlreadyApplied;

                    // Also apply hotpixel filtering if noise reduction will be done to the source image
                    if (p.HotpixelFiltering || (p.NoiseReductionRadius > 0 && p.StarMeasurementNoiseReductionEnabled)) {
                        // Apply a median box filter in place to the starting image
                        if (!hotpixelFilterAlreadyApplied) {
                            metrics.HotpixelCount = ApplyHotpixelFilter(srcImage, p);
                        }
                        hotpixelFilteringApplied = true;
                    }

                    var noiseReductionApplied = false;
                    if (p.NoiseReductionRadius > 0 && p.StarMeasurementNoiseReductionEnabled) {
                        CvImageUtility.ConvolveGaussian(srcImage, srcImage, p.NoiseReductionRadius * 2 + 1);
                        noiseReductionApplied = true;
                    }

                    MaybeSaveIntermediateImage(srcImage, p, "02-src-image-preparation.tif");
                    stopWatch.RecordEntry("SrcImagePreparation");

                    // Step 2: Prepare for structure detection by performing optional noise reduction
                    progress?.Report(new ApplicationStatus() { Status = "Preparing for Structure Detection" });

                    Mat noiseReducedImage = scratch.NewMat();
                    if (hotpixelFilteringApplied || noiseReductionApplied || p.NoiseReductionRadius <= 0) {
                        // In this case, we've already applied hotpixel filtering, so no need to do it again. The structure map can start from here
                        srcImage.CopyTo(noiseReducedImage);
                    } else {
                        srcImage.CopyTo(noiseReducedImage);
                        metrics.HotpixelCount = ApplyHotpixelFilter(noiseReducedImage, p);
                    }

                    // Step 3: If we haven't yet applied noise reduction and it is configured, do so now
                    if (p.NoiseReductionRadius > 0 && !noiseReductionApplied) {
                        CvImageUtility.ConvolveGaussian(noiseReducedImage, noiseReducedImage, p.NoiseReductionRadius * 2 + 1);
                    }

                    Mat structureMap = scratch.NewMat();
                    noiseReducedImage.CopyTo(structureMap);
                    var noiseReducedNoiseEstimateTask = Task.Run(() => {
                        var result = CvImageUtility.KappaSigmaNoiseEstimate(noiseReducedImage, clippingMultipler: p.NoiseClippingMultiplier);
                        var ksigmaTraceStructureMap = $"Structure Map K-Sigma Noise Estimate: {result.Sigma}, Background Mean: {result.BackgroundMean}, NumIterations={result.NumIterations}";
                        Logger.Trace(ksigmaTraceStructureMap);
                        MaybeSaveIntermediateText(ksigmaTraceStructureMap, p, "02-ksigma-estimate-noise-reduced.txt");

                        // Spatially-adaptive binarization (opt-in): sample the per-block local-σ grid on the SAME
                        // noise-reduced source the global σ above is computed from (F4 σ-consistency), HERE — before
                        // the source is disposed below. Null on the legacy path ⇒ bit-identical detection.
                        CvImageUtility.LocalBackgroundGrid sigmaGrid = null;
                        if (p.LocallyAdaptiveBinarization) {
                            sigmaGrid = CvImageUtility.ComputeLocalBackgroundGrid(noiseReducedImage, Math.Max(1, p.AdaptiveNoiseBlockSize), AdaptiveBinarizationSigmaFloor);
                        }

                        noiseReducedImage.Dispose();
                        noiseReducedImage = null;
                        return new StructureNoiseEstimate(result, sigmaGrid);
                    });

                    // F4 (σ consistency): thresholds applied to the image actually sampled must use that image's σ.
                    // The estimate above is computed on the (possibly blurred) structure-map source; when a noise
                    // reduction radius is set but measurement noise reduction is off, srcImage was never blurred and
                    // its white-noise σ is ~4x larger. Measure it directly on srcImage (rather than applying an
                    // analytic kernel factor) so correlated real-camera noise and hotpixel filtering are accounted
                    // for automatically. srcImage is read-only from here until this task is awaited (before binarization), so the concurrent read is safe.
                    var measurementImageDiffers = p.NoiseReductionRadius > 0 && !noiseReductionApplied;
                    var measurementNoiseEstimateTask = measurementImageDiffers
                        ? Task.Run(() => {
                            var result = CvImageUtility.KappaSigmaNoiseEstimate(srcImage, clippingMultipler: p.NoiseClippingMultiplier);
                            var ksigmaTraceMeasurement = $"Measurement Image K-Sigma Noise Estimate: {result.Sigma}, Background Mean: {result.BackgroundMean}, NumIterations={result.NumIterations}";
                            Logger.Trace(ksigmaTraceMeasurement);
                            MaybeSaveIntermediateText(ksigmaTraceMeasurement, p, "02-ksigma-estimate-measurement.txt");
                            return result;
                        })
                        : null;

                    MaybeSaveIntermediateImage(structureMap, p, "03-structure-map-start.tif");
                    stopWatch.RecordEntry("StructureMapPreparation");

                    // Step 4: Compute b-spline wavelets to exclude large structures such as nebulae. If the pixel scale is very small or need a wide range for focus, you may need to increase the number of layers
                    //         to keep stars from being excluded
                    // Defocus-aware structure (opt-in): use a COARSER residual (more layers) so large/donut defocused
                    // stars survive the subtraction. When OFF, effectiveStructureLayers == p.StructureLayers exactly,
                    // so candidate formation is bit-identical.
                    //
                    // Donut recovery needs the LARGE rings to SURVIVE this wavelet subtraction — the downstream
                    // morph-close and hole-fill cannot un-erase a donut the residual already removed. So when the
                    // donut master is on we apply a default structure boost EVEN IF the explicit DefocusAwareStructure
                    // axis is off (the optimizer can raise it further via that axis). Gated by the master ⇒
                    // bit-identical when off.
                    var effectiveStructureLayers = EffectiveStructureLayers(p);
                    // Deliberately NOT passing the cancellation token to the fast wavelet: the two noise-estimate
                    // tasks started above are still running and read srcImage/noiseReducedImage, and a cancellation
                    // unwind from inside the wavelet would dispose those Mats under the tasks (native
                    // use-after-free). Like the legacy SepFilter2D path, this window stays non-cancellable (it is
                    // ~100 ms on the fast path); the token is honored again at the points after the task awaits.
                    if (p.FastAtrousWavelets && string.IsNullOrEmpty(p.SaveIntermediateFilesPath)) {
                        // Fused fast path: residual + subtract + clamp in one final pass, no residual Mat.
                        AtrousWaveletFast.ComputeResidualAndSubtractInPlace(structureMap, effectiveStructureLayers);
                    } else {
                        using (var residualLayer = p.FastAtrousWavelets
                                ? AtrousWaveletFast.ComputeResidual(structureMap, effectiveStructureLayers)
                                : ComputeResidualAtrousB3SplineDyadicWaveletLayer(structureMap, effectiveStructureLayers)) {
                            MaybeSaveIntermediateImage(residualLayer, p, "04-structure-wavelet-residual.tif");
                            CvImageUtility.SubtractInPlace(structureMap, residualLayer);
                        }
                    }

                    MaybeSaveIntermediateImage(structureMap, p, "04-structure-wavelet-subtracted.tif");
                    stopWatch.RecordEntry("WaveletCalculation");

                    // Step 5: Excluding large structures can cut off the outsides of large stars, or leave holes when far out of focus. Blurring smooths this out well for structure detection
                    CvImageUtility.ConvolveGaussian(structureMap, structureMap, p.StructureLayers * 2 + 1);
                    MaybeSaveIntermediateImage(structureMap, p, "05-structure-wavelet-blurred.tif");
                    stopWatch.RecordEntry("PostWaveletConvolution");

                    // Log histograms produce more accurate results due to clustering in very low ADUs, but are substantially more computationally expensive
                    // The difference doesn't seem worth it based on tests done so far
                    var structureMapStats = CalculateStatistics_Histogram(structureMap, useLogHistogram: false, flags: CvImageStatisticsFlags.Median);
                    // Spatially-adaptive binarization (opt-in): sample the per-block local background (median) on the
                    // SAME structure map the global median above is taken from, and BEFORE the dilation below — so it
                    // mirrors the scalar path's pre-dilation median. Null on the legacy path ⇒ bit-identical.
                    CvImageUtility.LocalBackgroundGrid adaptiveMedianGrid = null;
                    if (p.LocallyAdaptiveBinarization) {
                        adaptiveMedianGrid = CvImageUtility.ComputeLocalBackgroundGrid(structureMap, Math.Max(1, p.AdaptiveNoiseBlockSize), AdaptiveBinarizationSigmaFloor);
                    }
                    stopWatch.RecordEntry("BinarizationStatistics");

                    var structureNoiseEstimate = await noiseReducedNoiseEstimateTask;
                    var noiseReducedImageNoise = structureNoiseEstimate.Noise;
                    var measurementImageNoise = measurementNoiseEstimateTask != null
                        ? await measurementNoiseEstimateTask
                        : noiseReducedImageNoise;
                    double binarizeThreshold = structureMapStats.Median + p.NoiseClippingMultiplier * noiseReducedImageNoise.Sigma;
                    var binarizeTrace = $"Structure Map Binarization - Median: {structureMapStats.Median}, Threshold: {binarizeThreshold}, Clipping Multiplier: {p.NoiseClippingMultiplier}, Noise Sigma: {noiseReducedImageNoise.Sigma}";
                    Logger.Trace(binarizeTrace);
                    MaybeSaveIntermediateText(structureMapStats.ToString() + Environment.NewLine + binarizeTrace, p, "05-structure-map-statistics.txt");

                    if (p.StoreStructureMap) {
                        UpdateStructureMapDebugData(structureMap, debugData.StructureMap, binarizeThreshold, 1);
                    }

                    // Step 6: Boost small structures with a dilation box filter
                    if (p.StructureDilationCount > 0) {
                        using (var dilationStructure = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(p.StructureDilationSize, p.StructureDilationSize))) {
                            Cv2.MorphologyEx(structureMap, structureMap, MorphTypes.Dilate, dilationStructure, iterations: p.StructureDilationCount, borderType: BorderTypes.Reflect);
                        }
                        stopWatch.RecordEntry("StructureDilation");
                        MaybeSaveIntermediateImage(structureMap, p, "06-structure-map-dilated.tif");
                    }

                    if (p.StoreStructureMap) {
                        UpdateStructureMapDebugData(structureMap, debugData.StructureMap, binarizeThreshold, 2);
                    }

                    progress?.Report(new ApplicationStatus() { Status = "Structure Detection" });

                    // Step 7: Binarize foreground structures based on noise estimates. Legacy (default): a single
                    // global scalar threshold. Spatially-adaptive (opt-in): a per-pixel threshold surface
                    // (local-median + NC·local-σ) upsampled from the coarse grids. The flag-off path is byte-for-byte
                    // the legacy scalar Binarize.
                    if (p.LocallyAdaptiveBinarization && adaptiveMedianGrid != null && structureNoiseEstimate.SigmaGrid != null) {
                        ApplyAdaptiveBinarization(structureMap, adaptiveMedianGrid, structureNoiseEstimate.SigmaGrid, p.NoiseClippingMultiplier);
                    } else {
                        CvImageUtility.Binarize(structureMap, structureMap, binarizeThreshold);
                    }
                    if (p.Region.InnerCropBoundary != null) {
                        var innerRoiRect = new Rect(
                            (int)Math.Floor(srcImage.Cols * p.Region.InnerCropBoundary.StartX),
                            (int)Math.Floor(srcImage.Rows * p.Region.InnerCropBoundary.StartY),
                            (int)(srcImage.Cols * p.Region.InnerCropBoundary.Width),
                            (int)(srcImage.Rows * p.Region.InnerCropBoundary.Height));
                        structureMap.SubMat(innerRoiRect).SetTo(0.0f);
                        Logger.Info($"Clearing structure map for inner ROI: {p.Region.InnerCropBoundary}");
                    }

                    stopWatch.RecordEntry("Binarization");
                    MaybeSaveIntermediateImage(structureMap, p, "07-structure-binarized.tif");

                    // Step 7b (defocus-aware donut recovery, opt-in): morphologically CLOSE the binarized structure
                    // map so fragmented donut-ring arcs reconnect into ONE candidate region (otherwise each arc is a
                    // tiny region the late gate rejects as TooSmall — the dominant donut loss). Erode-after-dilate
                    // keeps the outer bbox and the central hole intact (kernel diameter << hole), so annularity is
                    // preserved for the late hole-fill gate, and a kernel smaller than the spike-to-spike gap will
                    // not bridge a bright star's diffraction spikes. Gated by the master flag ⇒ bit-identical OFF.
                    if (p.DefocusAwareDonutDetection && p.DonutMorphCloseSize > 1) {
                        using (var donutCloseKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(p.DonutMorphCloseSize, p.DonutMorphCloseSize))) {
                            Cv2.MorphologyEx(structureMap, structureMap, MorphTypes.Close, donutCloseKernel, borderType: BorderTypes.Reflect);
                        }
                        stopWatch.RecordEntry("DonutMorphClose");
                        MaybeSaveIntermediateImage(structureMap, p, "07b-structure-donut-closed.tif");
                    }

                    // Step 8: Scan the structure map and collect ALL candidate regions (the late size/shape/border
                    // gates are NOT applied here — they live in GateAndMeasure, so the candidate set depends only on
                    // the early params). This tallies the early-only metrics: StructureCandidates (per candidate),
                    // HotpixelCount (already set above), and SaturatedPixelCount (via EvaluateGlobalMetrics).
                    progress?.Report(new ApplicationStatus() { Status = "Scan and Analyze Stars" });
                    var candidates = CollectStarCandidates(srcImage, structureMap, p, metrics, token);
                    stopWatch.RecordEntry("CollectStarCandidates");

                    // srcImage (the prepared measurement image) is never registered with the tracker — it is the
                    // method parameter, replaced by a fresh Clone()'d ROI submat when an ROI is in play (the old one
                    // disposed there). So disposing the local tracker in the finally frees only the scratch Mats
                    // (structureMap; noiseReducedImage is disposed inside its task) and the context's image survives.
                    var context = new DetectionContext {
                        MeasurementImage = srcImage,
                        FullImageSize = fullImageSize,
                        Binning = binning,
                        Candidates = candidates,
                        StructureNoiseSigma = noiseReducedImageNoise.Sigma,
                        MeasurementNoiseSigma = measurementImageNoise.Sigma,
                        RoiRect = roiRect,
                        DebugData = debugData,
                        HotpixelCount = metrics.HotpixelCount,
                        StructureCandidates = metrics.StructureCandidates,
                        SaturatedPixelCount = metrics.SaturatedPixelCount
                    };
                    // The context now owns srcImage; clear the failure-cleanup flag so the finally does NOT dispose it.
                    contextProduced = true;
                    return context;
                }
            } finally {
                if (ownsLocalTracker) {
                    // Free the transient scratch Mats (structure map, etc). The prepared srcImage was detached above
                    // and survives on the returned context.
                    scratch.Dispose();
                }
                if (!contextProduced) {
                    // The early pipeline threw before the context was constructed: dispose the in-flight source/ROI
                    // Mat we own so the ~244 MB allocation is not orphaned. On the monolithic no-ROI path
                    // liveOwnedImage is null (the caller still owns + disposes its input), so this is a no-op there —
                    // preserving the established contract that the no-ROI Detect path never disposes the caller's Mat.
                    liveOwnedImage?.Dispose();
                }
            }
        }

        private async Task<HocusFocusStarDetectorResult> GateAndMeasureInternal(DetectionContext ctx, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var srcImage = ctx.MeasurementImage;
            var roiRect = ctx.RoiRect;

            // Seed a fresh metrics with ONLY the early-stage counters carried on the context, then run the late
            // (gate + measure) stage which fills in every other counter. Starting fresh each call is what makes a
            // reused context produce the same metrics as a one-shot detect.
            var metrics = new StarDetectorMetrics {
                // The debayered-hotpixel path (IRenderedImage) overwrites the structure-pipeline HotpixelCount with
                // the CFA count, exactly as the monolithic Detect(IRenderedImage) did; otherwise the early
                // structure-pipeline count is authoritative.
                HotpixelCount = ctx.DebayerHotpixelCount ?? ctx.HotpixelCount,
                StructureCandidates = ctx.StructureCandidates,
                SaturatedPixelCount = ctx.SaturatedPixelCount
            };

            // Per-call diagnostics bags (thread-safe; the late stage evaluates candidates in parallel). Null in
            // normal runs (zero overhead).
            var contaminationDiagnosticsBag = p.CollectContaminationDiagnostics ? new ConcurrentBag<ContaminationDiagnosticRecord>() : null;
            var rejectedCandidatesBag = p.CollectRejectedCandidateDiagnostics ? new ConcurrentBag<RejectedCandidateRecord>() : null;

            using (var stopWatch = MultiStopWatch.Measure()) {
                var stars = EvaluateStarCandidates(srcImage, ctx.Candidates, p, ctx.MeasurementNoiseSigma, metrics, contaminationDiagnosticsBag, rejectedCandidatesBag, token);
                stopWatch.RecordEntry("StarAnalysis");

                // Step 9: Fit PSF models
                if (p.ModelPSF) {
                    // Time only the PSF step, and only when it actually runs. During optimization/AF (ModelPSF=false,
                    // the common case) this whole block is skipped, so the per-detection stopwatch + Console.WriteLine
                    // overhead is eliminated. The remaining timer is TRACE-only (short-circuits unless TRACE is on).
                    var psfStopwatch = Stopwatch.StartNew();
                    progress?.Report(new ApplicationStatus() { Status = "Modeling PSFs" });
                    await ModelPSF(srcImage, ctx.MeasurementNoiseSigma, stars, p, metrics, token);

                    stopWatch.RecordEntry("ModelPSF");
                    psfStopwatch.Stop();
                    Logger.Trace($"PSF time: {psfStopwatch.Elapsed}");
                }
                MaybeSaveIntermediateStars(stars, p, "09-detected-stars.txt");

                var metricsTrace = $"Star Detection Metrics. Total={metrics.TotalDetected}, Candidates={metrics.StructureCandidates}, TooSmall={metrics.TooSmall}, OnBorder={metrics.OnBorder}, TooDistorted={metrics.TooDistorted}, Degenerate={metrics.Degenerate}, SaturatedMasked={metrics.Saturated}, LowSensitivity={metrics.LowSensitivity}, NotCentered={metrics.NotCentered}, TooFlat={metrics.TooFlat}, HFRAnalysisFailed={metrics.HFRAnalysisFailed}";
                MaybeSaveIntermediateText(metricsTrace, p, "10-detection-metrics.txt");
                Logger.Trace(metricsTrace);

                // Log structured rejection breakdown for debugging
                int totalCandidates = metrics.StructureCandidates;
                int accepted = metrics.TotalDetected;
                int rejected = totalCandidates - accepted;
                var rejectionLog = $"Star detection complete: Found={accepted}, Rejected={rejected}, TooSmall={metrics.TooSmall}, OnBorder={metrics.OnBorder}, TooFlat={metrics.TooFlat}, TooDistorted={metrics.TooDistorted}, Saturated(masked)={metrics.Saturated}, LowSensitivity={metrics.LowSensitivity}, OffCenter={metrics.NotCentered}, HFRFailed={metrics.HFRAnalysisFailed}, PSFFailed={metrics.PSFFitFailed}, Degenerate={metrics.Degenerate}, TooLowHFR={metrics.TooLowHFR}, ContaminationSuspected={metrics.ContaminationSuspected}";
                Logger.Debug(rejectionLog);
                // Leave binned pixel space BEFORE the ROI offset is applied: the ROI was cropped at native
                // resolution, so its offset is already in source pixels, while everything measured here is in
                // binned pixels of that crop. Scale first, then translate.
                var binning = Math.Max(1, ctx.Binning);
                if (binning > 1) {
                    stars = stars.Select(s => s.ScaleToSourcePixels(binning)).ToList();
                    metrics.ScaleBounds(binning);
                }

                if (roiRect.HasValue) {
                    // Apply correction for the ROI
                    stars = stars.Select(s => s.AddOffset(xOffset: roiRect.Value.Left, yOffset: roiRect.Value.Top)).ToList();
                    metrics.AddROIOffset(xOffset: roiRect.Value.Left, yOffset: roiRect.Value.Top);
                }

                // Materialize the (parallel-collected) contamination diagnostics, mirroring the ROI offset
                // applied to DetectedStars and ordering deterministically by image position so the CSV is stable.
                List<ContaminationDiagnosticRecord> contaminationDiagnostics = null;
                if (contaminationDiagnosticsBag != null) {
                    contaminationDiagnostics = contaminationDiagnosticsBag.ToList();
                    if (binning > 1) {
                        // Same scale-then-translate order as the stars above. Intensity-valued fields (background,
                        // sigmas, sector residual medians) carry over: mean binning preserves the level.
                        foreach (var rec in contaminationDiagnostics) {
                            var centerShift = (binning - 1) / 2.0;
                            rec.CenterX = rec.CenterX * binning + centerShift;
                            rec.CenterY = rec.CenterY * binning + centerShift;
                            rec.Hfr *= binning;
                            rec.GradientSlope /= binning;
                        }
                    }
                    if (roiRect.HasValue) {
                        foreach (var rec in contaminationDiagnostics) {
                            rec.CenterX += roiRect.Value.Left;
                            rec.CenterY += roiRect.Value.Top;
                        }
                    }
                    contaminationDiagnostics = contaminationDiagnostics.OrderBy(r => r.CenterY).ThenBy(r => r.CenterX).ToList();
                    Logger.Trace($"Collected {contaminationDiagnostics.Count} contamination diagnostic records ({contaminationDiagnostics.Count(r => r.ContaminationSuspected)} suspected)");
                }

                // Materialize the (parallel-collected) rejected-candidate diagnostics with the same ROI offset and
                // deterministic (Y, X) ordering as the metrics *Bounds lists, so the recommender sees a stable set.
                List<RejectedCandidateRecord> rejectedCandidates = null;
                if (rejectedCandidatesBag != null) {
                    rejectedCandidates = rejectedCandidatesBag.ToList();
                    if (binning > 1) {
                        // Geometry moves to source pixels; MeasuredValue/ThresholdValue deliberately do NOT. Those
                        // two are the gate's own comparison pair, and the thresholds are binned-space params — the
                        // recommender inverts one against the other, so they must stay in the same space.
                        foreach (var rec in rejectedCandidates) {
                            var centerShift = (binning - 1) / 2.0;
                            rec.Bounds = new Rect(rec.Bounds.X * binning, rec.Bounds.Y * binning, rec.Bounds.Width * binning, rec.Bounds.Height * binning);
                            rec.CenterX = rec.CenterX * binning + centerShift;
                            rec.CenterY = rec.CenterY * binning + centerShift;
                            rec.CandidateSize *= binning;
                            rec.Hfr *= binning;
                        }
                    }
                    if (roiRect.HasValue) {
                        foreach (var rec in rejectedCandidates) {
                            rec.Bounds = new Rect(rec.Bounds.Location + new Point(roiRect.Value.Left, roiRect.Value.Top), rec.Bounds.Size);
                            rec.CenterX += roiRect.Value.Left;
                            rec.CenterY += roiRect.Value.Top;
                        }
                    }
                    rejectedCandidates = rejectedCandidates.OrderBy(r => r.Bounds.Y).ThenBy(r => r.Bounds.X).ToList();
                    Logger.Trace($"Collected {rejectedCandidates.Count} rejected-candidate diagnostic records");
                }

                return new HocusFocusStarDetectorResult() {
                    DetectedStars = stars,
                    Metrics = metrics,
                    DetectionBinning = binning,
                    DebugData = ctx.DebugData,
                    ContaminationDiagnostics = contaminationDiagnostics,
                    RejectedCandidates = rejectedCandidates,
                    StructureNoiseSigma = ctx.StructureNoiseSigma,
                    MeasurementNoiseSigma = ctx.MeasurementNoiseSigma
                };
            }
        }

        private long ApplyHotpixelFilter(Mat img, StarDetectorParams p) {
            if (p.HotpixelThresholdingEnabled) {
                return HotpixelFiltering.HotpixelFilterWithThresholding(img, p.HotpixelThreshold);
            } else {
                HotpixelFiltering.HotpixelFilter(img);
                return 0L;
            }
        }

        private async Task ModelPSF(Mat srcImage, double noiseSigma, List<Star> stars, StarDetectorParams p, StarDetectorMetrics metrics, CancellationToken ct) {
            var allTasks = new List<Task>();
            Logger.Debug($"Modeling PSFs using a parallel partition size of {p.PSFParallelPartitionSize} and a pixel scale of {p.PixelScale} arcsec/pixel");
            var partitions = p.PSFParallelPartitionSize > 0 ? stars.Partition(p.PSFParallelPartitionSize) : new List<IEnumerable<Star>>() { stars };
            foreach (var detectedStarsPartition in partitions) {
                var psfPartitionTask = Task.Run(() => {
                    foreach (var detectedStar in detectedStarsPartition) {
                        ct.ThrowIfCancellationRequested();

                        var modeler = PSFModeler.Create(
                            alglibAPI: alglibAPI,
                            fitType: p.PSFFitType,
                            psfResolution: p.PSFResolution,
                            detectedStar: detectedStar,
                            srcImage: srcImage,
                            pixelScale: p.PixelScale,
                            saturationThreshold: p.SaturationThreshold,
                            pixelIntegration: p.PSFPixelIntegration);
                        PSFModel psf = null;
                        if (modeler != null) {
                            try {
                                psf = PSFModeler.Solve(modeler, useAbsoluteResiduals: p.UsePSFAbsoluteDeviation, noiseSigma: noiseSigma, ct: ct);
                            } catch (Exception) {
                                // Ignore errors and continue
                            }
                        }

                        bool psfAccepted = false;
                        if (psf != null) {
                            // R² must be at or above the threshold
                            psfAccepted = psf.RSquared >= p.PSFGoodnessOfFitThreshold;
                        }
                        if (psfAccepted) {
                            detectedStar.PSF = psf;
                        } else {
                            // ModelPSF runs across multiple parallel Task.Run partitions, so this counter must
                            // be incremented atomically rather than with a plain ++ (pre-existing data race).
                            metrics.IncrementPsfFitFailed();
                        }
                    }
                }, ct);
                allTasks.Add(psfPartitionTask);
            }

            var tcs = new TaskCompletionSource();
            using (ct.Register(() => tcs.SetCanceled(), useSynchronizationContext: false)) {
                await Task.WhenAny(Task.WhenAll(allTasks), tcs.Task);
            }
        }

        /// <summary>
        /// Builds the spatially-varying binarization threshold surface (local-median + NC·local-σ) from the two
        /// coarse grids and binarizes <paramref name="structureMap"/> against it in place. The median grid is sampled
        /// on the structure map (the image actually thresholded) and the σ grid on the noise-reduced source (F4
        /// σ-consistency); both share the block size, so their grids are congruent. Because bilinear upsampling is
        /// linear, the (median + NC·σ) combination is formed at GRID resolution and the small result upsampled ONCE
        /// (equivalent to upsampling each grid then combining, at a fraction of the cost and memory).
        /// </summary>
        private static void ApplyAdaptiveBinarization(Mat structureMap, CvImageUtility.LocalBackgroundGrid medianGrid, CvImageUtility.LocalBackgroundGrid sigmaGrid, double noiseClippingMultiplier) {
            if (medianGrid.GridRows != sigmaGrid.GridRows || medianGrid.GridCols != sigmaGrid.GridCols) {
                throw new InvalidOperationException(
                    $"Adaptive binarization grid mismatch: median {medianGrid.GridRows}x{medianGrid.GridCols} vs sigma {sigmaGrid.GridRows}x{sigmaGrid.GridCols}");
            }

            int rows = medianGrid.GridRows;
            int cols = medianGrid.GridCols;
            int n = rows * cols;
            using (var thresholdGrid = new Mat(rows, cols, MatType.CV_32F))
            using (var thresholdSurface = new Mat()) {
                unsafe {
                    var dst = (float*)thresholdGrid.DataPointer;
                    for (int i = 0; i < n; ++i) {
                        dst[i] = (float)(medianGrid.Median[i] + noiseClippingMultiplier * sigmaGrid.Sigma[i]);
                    }
                }
                Cv2.Resize(thresholdGrid, thresholdSurface, new Size(structureMap.Cols, structureMap.Rows), 0, 0, InterpolationFlags.Linear);
                CvImageUtility.BinarizeAdaptive(structureMap, structureMap, thresholdSurface);
            }
        }

        /// <summary>
        /// Updates a byte array containing debug information about a structure map. All pixels exceeding the binarization threshold are set to value in the debug data
        /// </summary>
        /// <param name="structureMap">The image representing the structure map</param>
        /// <param name="structureMapDebugData">The debug data to update</param>
        /// <param name="binarizationThreshold">Binarization threshold</param>
        /// <param name="value">The value to set when pixels exceed the binarization threshold</param>
        private void UpdateStructureMapDebugData(Mat structureMap, byte[] structureMapDebugData, double binarizationThreshold, byte value) {
            var numPixels = structureMap.Rows * structureMap.Cols;
            unsafe {
                var structureData = (float*)structureMap.DataPointer;
                for (int i = 0; i < numPixels; ++i) {
                    var pixel = *(structureData++);
                    if (pixel >= binarizationThreshold && structureMapDebugData[i] == 0) {
                        structureMapDebugData[i] = value;
                    }
                }
            }
        }

        /// <summary>
        /// Result of the local-background analysis: a robust background plane fit (always attempted) plus the
        /// gradient-robust contamination decision (only when sensitivity &gt; 0). Scalar fields are always set;
        /// the per-sector residual arrays are populated only when <c>fillSectors</c> is requested (diagnostics).
        /// </summary>
        internal struct GradientContaminationResult {
            public bool PlaneValid;               // true when B0/B1/B2 are a usable robust fit
            public double B0;                     // background at the annulus origin (dx = dy = 0)
            public double B1;                     // d(background)/dx
            public double B2;                     // d(background)/dy
            public bool Suspected;
            public double GradientSlope;
            public double LocalSigmaResidual;
            public double MaxSectorResidualOverSE;
            public int TrippingSector;
            public double[] SectorResidualMedian; // null unless fillSectors
            public int[] SectorResidualCount;     // null unless fillSectors
        }

        /// <summary>
        /// Fits a robust local background plane (b0 + b1*dx + b2*dy) to the annulus pixels via IRLS with Huber
        /// weights to model a smooth one-sided background (galaxy/nebula gradient), and runs the gradient-robust
        /// contamination test on the plane-subtracted residuals: it flags only a one-sided POSITIVE residual
        /// excess in a sector — a contaminant adds light, so this ignores both smooth gradients (removed by the
        /// fit) and edge-clipping deficits (negative residuals). The plane is fit whenever there are enough
        /// non-degenerate points (so it can serve as the local background even when the contamination test is
        /// disabled); the contamination decision additionally requires <paramref name="sensitivity"/> &gt; 0.
        /// </summary>
        internal static GradientContaminationResult ComputeGradientContamination(
                float[] dx, float[] dy, float[] val, int n,
                double fallbackSigma, double sensitivity, int minSectorPixels, bool fillSectors) {
            const int numSectors = 8;
            var result = new GradientContaminationResult {
                PlaneValid = false,
                B0 = double.NaN, B1 = double.NaN, B2 = double.NaN,
                Suspected = false,
                GradientSlope = double.NaN,
                LocalSigmaResidual = double.NaN,
                MaxSectorResidualOverSE = double.NaN,
                TrippingSector = -1
            };
            if (fillSectors) {
                result.SectorResidualMedian = new double[numSectors];
                result.SectorResidualCount = new int[numSectors];
                for (int s = 0; s < numSectors; ++s) result.SectorResidualMedian[s] = double.NaN;
            }

            // Too few points to fit a 3-parameter plane with margin, or null input → no plane, not suspected.
            if (dx == null || n < 12) {
                return result;
            }

            // Robust plane fit via iteratively reweighted least squares (Huber).
            double b0 = 0, b1 = 0, b2 = 0;
            var w = new double[n];
            for (int i = 0; i < n; ++i) w[i] = 1.0;
            const double huberC = 1.345;
            for (int iter = 0; iter < 4; ++iter) {
                if (!SolveWeightedPlane(dx, dy, val, w, n, out b0, out b1, out b2)) {
                    return result; // singular / collinear geometry → no usable plane
                }
                var absr = new double[n];
                for (int i = 0; i < n; ++i) absr[i] = Math.Abs(val[i] - (b0 + b1 * dx[i] + b2 * dy[i]));
                double sigma = 1.4826 * MedianInPlace(absr);
                if (sigma <= 0) break; // residuals essentially zero; fit is exact
                for (int i = 0; i < n; ++i) {
                    double z = Math.Abs(val[i] - (b0 + b1 * dx[i] + b2 * dy[i])) / sigma;
                    w[i] = z <= huberC ? 1.0 : huberC / z;
                }
            }

            result.PlaneValid = true;
            result.B0 = b0;
            result.B1 = b1;
            result.B2 = b2;
            result.GradientSlope = Math.Sqrt(b1 * b1 + b2 * b2);

            var absResid = new double[n];
            for (int i = 0; i < n; ++i) absResid[i] = Math.Abs(val[i] - (b0 + b1 * dx[i] + b2 * dy[i]));
            double localSigma = 1.4826 * MedianInPlace(absResid);
            if (localSigma <= 0) localSigma = fallbackSigma;
            result.LocalSigmaResidual = localSigma;

            // Contamination decision (only when enabled).
            if (sensitivity <= 0 || localSigma <= 0) {
                return result;
            }
            var bySector = new List<double>[numSectors];
            for (int s = 0; s < numSectors; ++s) bySector[s] = new List<double>();
            for (int i = 0; i < n; ++i) {
                bySector[OctantOf(dx[i], dy[i])].Add(val[i] - (b0 + b1 * dx[i] + b2 * dy[i]));
            }
            double maxStat = double.NaN;
            for (int s = 0; s < numSectors; ++s) {
                int count = bySector[s].Count;
                if (fillSectors) result.SectorResidualCount[s] = count;
                if (count == 0) continue;
                bySector[s].Sort();
                double med = bySector[s][count >> 1];
                if (fillSectors) result.SectorResidualMedian[s] = med;
                if (count < minSectorPixels) continue;
                double se = 1.2533 * localSigma / Math.Sqrt(count);
                if (se <= 0) continue;
                double stat = med / se; // one-sided: positive excess over the local plane
                if (double.IsNaN(maxStat) || stat > maxStat) maxStat = stat;
                if (med > sensitivity * se && result.TrippingSector < 0) {
                    result.TrippingSector = s;
                }
            }
            result.MaxSectorResidualOverSE = maxStat;
            result.Suspected = result.TrippingSector >= 0;
            return result;
        }

        // Solves the weighted least-squares plane val ≈ b0 + b1*dx + b2*dy. Returns false if the normal
        // matrix is singular (collinear / degenerate annulus geometry).
        private static bool SolveWeightedPlane(float[] dx, float[] dy, float[] val, double[] w, int n,
                out double b0, out double b1, out double b2) {
            double sw = 0, swx = 0, swy = 0, swxx = 0, swxy = 0, swyy = 0, swv = 0, swxv = 0, swyv = 0;
            for (int i = 0; i < n; ++i) {
                double wi = w[i], x = dx[i], y = dy[i], v = val[i];
                sw += wi; swx += wi * x; swy += wi * y;
                swxx += wi * x * x; swxy += wi * x * y; swyy += wi * y * y;
                swv += wi * v; swxv += wi * x * v; swyv += wi * y * v;
            }
            var a = new double[3, 3] { { sw, swx, swy }, { swx, swxx, swxy }, { swy, swxy, swyy } };
            var g = new double[3] { swv, swxv, swyv };
            b0 = b1 = b2 = 0;
            // Gaussian elimination with partial pivoting.
            for (int col = 0; col < 3; ++col) {
                int piv = col;
                for (int r = col + 1; r < 3; ++r) if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col])) piv = r;
                if (Math.Abs(a[piv, col]) < 1e-12) return false;
                if (piv != col) {
                    for (int c = 0; c < 3; ++c) { var t = a[col, c]; a[col, c] = a[piv, c]; a[piv, c] = t; }
                    var tg = g[col]; g[col] = g[piv]; g[piv] = tg;
                }
                for (int r = 0; r < 3; ++r) {
                    if (r == col) continue;
                    double f = a[r, col] / a[col, col];
                    for (int c = col; c < 3; ++c) a[r, c] -= f * a[col, c];
                    g[r] -= f * g[col];
                }
            }
            b0 = g[0] / a[0, 0];
            b1 = g[1] / a[1, 1];
            b2 = g[2] / a[2, 2];
            return true;
        }

        // Median of an array, sorting it in place (caller passes a scratch array it owns).
        private static double MedianInPlace(double[] a) {
            if (a.Length == 0) return 0;
            Array.Sort(a);
            return a[a.Length >> 1];
        }

        /// <summary>
        /// Assigns a (dx, dy) offset to one of 8 angular octants such that octant s and octant s+4
        /// are geometrically opposite. Uses sign/magnitude branches to avoid a per-pixel atan2.
        /// </summary>
        internal static int OctantOf(double dx, double dy) {
            if (dx >= 0) {
                if (dy >= 0) {
                    return Math.Abs(dx) >= Math.Abs(dy) ? 0 : 1;
                }
                return Math.Abs(dx) >= Math.Abs(dy) ? 7 : 6;
            }
            if (dy >= 0) {
                return Math.Abs(dx) >= Math.Abs(dy) ? 3 : 2;
            }
            return Math.Abs(dx) >= Math.Abs(dy) ? 4 : 5;
        }

        /// <summary>
        /// Computes the star's HFR — the flux-weighted mean radius over a circular aperture, sampled
        /// on an AnalysisSamplingSize grid through the centroid with per-pixel background-plane
        /// subtraction. Saturated pixels are NOT masked here (unlike the PSF fit, which is off during
        /// AF): a flat saturated core under-weights the center, biasing HFR high — most likely near
        /// focus. No correction is attempted because HFR is an empirical flux sum (masking core pixels
        /// would bias it further); median aggregation across stars limits the damage, and the
        /// Saturated metric tracks exposure (accuracy analysis F14, decided document-only).
        /// </summary>
        internal bool MeasureStar(Mat srcImage, Star star, StarDetectorParams p, double noiseSigma) {
            // Subtract the local background plane per pixel so a one-sided gradient does not bias HFR. Fall back
            // to a flat plane at the scalar background if no plane is available.
            var backgroundPlane = star.BackgroundPlane ?? LocalBackgroundPlane.Flat(star.Center.X, star.Center.Y, star.Background);
            double totalBrightness = 0.0;
            double totalWeightedDistance = 0.0;

            // Circular aperture radius — same convention as centroid refinement
            var apertureRadius = Math.Min(star.StarBoundingBox.Width, star.StarBoundingBox.Height) / 2.0;

            // Determine the start position to sample from the star bounding box so that we stay within the box *and* the center point is one of the samples. This ensures
            // we're sampling in a balanced manner around the center
            var startX = star.Center.X - p.AnalysisSamplingSize * Math.Floor((star.Center.X - star.StarBoundingBox.Left) / p.AnalysisSamplingSize);
            var startY = star.Center.Y - p.AnalysisSamplingSize * Math.Floor((star.Center.Y - star.StarBoundingBox.Top) / p.AnalysisSamplingSize);
            var endX = star.StarBoundingBox.Right;
            var endY = star.StarBoundingBox.Bottom;
            // Donut-aware clip (mirrors ComputeStarParameters): cap the per-pixel HFR clip for EXTENDED candidates
            // so the full thin ring — not just its brightest arc — feeds the HFR flux sum, keeping the recovered
            // donuts' HFR consistent with their flux/centroid. Verbatim when the master is off or the candidate is
            // small ⇒ bit-identical.
            var noiseThreshold = EffectiveClipMultiplier(p, Math.Max(star.StarBoundingBox.Width, star.StarBoundingBox.Height)) * noiseSigma;
            for (var y = startY; y <= endY; y += p.AnalysisSamplingSize) {
                for (var x = startX; x <= endX; x += p.AnalysisSamplingSize) {
                    var dx = x - star.Center.X;
                    var dy = y - star.Center.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);

                    // Exclude pixels entirely outside the circular aperture
                    if (distance > apertureRadius + 0.5) {
                        continue;
                    }

                    var background = backgroundPlane.ValueAt(x, y);
                    var flux = CvImageUtility.BilinearSamplePixelValue(srcImage, y: y, x: x) - background;
                    if (flux > noiseThreshold) {
                        // SubtractTau preserves the legacy soft-threshold (flux − τ): identical gating, but wing
                        // pixels lose relative weight, biasing HFR low on radial gradients (F3). GateOnly keeps
                        // the full flux of surviving pixels, matching the centroid's gate-only convention.
                        var value = p.HfrTauPolicy == TauClipPolicy.SubtractTau ? flux - noiseThreshold : flux;
                        // Apply partial-pixel weighting at the aperture boundary (linear interpolation)
                        var apertureWeight = 1.0 - Math.Max(0.0, distance - (apertureRadius - 0.5));
                        totalWeightedDistance += apertureWeight * value * distance;
                        totalBrightness += apertureWeight * value;
                    }
                }
            }

            if (totalBrightness > 0.0d) {
                star.HFR = totalWeightedDistance / totalBrightness;
                return true;
            }
            return false;
        }

        private void EvaluateGlobalMetrics(Mat srcImage, StarDetectorParams p, StarDetectorMetrics metrics) {
            int width = srcImage.Width;
            int height = srcImage.Height;
            long numPixels = (long)width * height;
            unsafe {
                var srcImagePixel = (float*)srcImage.DataPointer;
                while (numPixels-- > 0) {
                    if (*srcImagePixel >= p.SaturationThreshold) {
                        ++metrics.SaturatedPixelCount;
                    }
                    srcImagePixel++;
                }
            }
        }

        // A candidate star collected by the sequential flood-fill (Stage A) and evaluated in parallel (Stage B).
        // Each candidate owns its OWN point list (no sharing/reuse) so the parallel evaluation is data-race free.
        internal readonly struct StarCandidateRegion {
            public readonly Rect Bounds;
            public readonly List<Point> Points;

            public StarCandidateRegion(Rect bounds, List<Point> points) {
                Bounds = bounds;
                Points = points;
            }
        }

        // Stage B: evaluate every collected candidate in parallel and assemble the accepted stars in deterministic
        // raster order. This is the LATE phase — it applies all size/shape/border/sensitivity gates and measures
        // each accepted star. It only READS srcImage + the candidate point lists, so it is safe to run repeatedly
        // against the same cached DetectionContext with different late params.
        private List<Star> EvaluateStarCandidates(Mat srcImage, List<StarCandidateRegion> candidates, StarDetectorParams p, double srcImageNoiseSigma, StarDetectorMetrics metrics, ConcurrentBag<ContaminationDiagnosticRecord> diagnosticsBag, ConcurrentBag<RejectedCandidateRecord> rejectedBag, CancellationToken ct) {
            // Per-star evaluation is independent (only LOCAL arrays + read-only image reads), so the only shared
            // mutable state is the metrics (handled via thread-locals merged afterward) and the (thread-safe,
            // per-call) diagnosticsBag / rejectedBag.
            var results = new Star[candidates.Count];
            using var localMetrics = new ThreadLocal<StarDetectorMetrics>(() => new StarDetectorMetrics(), trackAllValues: true);

            Parallel.For(0, candidates.Count, ParallelExecution.CreateOptions(p.MaxStarEvaluationParallelism, ct), i => {
                ct.ThrowIfCancellationRequested();
                results[i] = EvaluateStarCandidate(srcImage, p, candidates[i].Bounds, candidates[i].Points, srcImageNoiseSigma, localMetrics.Value, diagnosticsBag, rejectedBag);
            });

            // Fold each per-thread metrics instance into the main metrics (additive — see Merge).
            foreach (var threadMetrics in localMetrics.Values) {
                metrics.Merge(threadMetrics);
            }

            // Defocus-aware saturation bloom suppression (opt-in, two-pass). The saturated-source centers are only
            // known after the parallel pass (via the merged SaturatedBounds). Null out accepted NON-saturated
            // candidates whose centroid lies within DonutSaturationBloomRadius px of a saturated source — removing
            // bloom/halo fragments around a bright saturated star while keeping the saturated star itself. Radius 0
            // or master OFF ⇒ no suppression (bit-identical).
            List<Point2d> saturatedCenters = null;
            double bloomR2 = 0.0;
            if (p.DefocusAwareDonutDetection && p.DonutSaturationBloomRadius > 0.0 && metrics.SaturatedBounds.Count > 0) {
                bloomR2 = p.DonutSaturationBloomRadius * p.DonutSaturationBloomRadius;
                saturatedCenters = metrics.SaturatedBounds
                    .Select(b => new Point2d(b.X + b.Width / 2.0, b.Y + b.Height / 2.0)).ToList();
            }

            // Assemble in index order to preserve today's exact top-left raster ordering of DetectedStars, and
            // count detections on the main metrics. RelaxationAdmittedCount is tallied here (main thread) from the
            // accepted-star flags rather than through Merge, so it never double-counts; it is 0 whenever the
            // defocus-aware gates are OFF (no star can be flagged), keeping detection counts bit-identical.
            var stars = new List<Star>(candidates.Count);
            int totalDetected = 0;
            int relaxationAdmitted = 0;
            for (int i = 0; i < results.Length; ++i) {
                var star = results[i];
                if (star == null) {
                    continue;
                }
                if (saturatedCenters != null && (star.Background + star.PeakBrightness) < p.SaturationThreshold) {
                    bool suppressed = false;
                    foreach (var sc in saturatedCenters) {
                        var dx = star.Center.X - sc.X; var dy = star.Center.Y - sc.Y;
                        if (dx * dx + dy * dy <= bloomR2) { suppressed = true; break; }
                    }
                    if (suppressed) {
                        metrics.BloomSuppressedBounds.Add(star.StarBoundingBox);
                        if (rejectedBag != null) {
                            RecordRejection(rejectedBag, star.StarBoundingBox, RejectionGate.BloomSuppressed, double.NaN, p.DonutSaturationBloomRadius, star.Center.X, star.Center.Y, star.HFR);
                        }
                        continue;
                    }
                }
                stars.Add(star);
                ++totalDetected;
                if (star.RelaxationAdmitted) {
                    ++relaxationAdmitted;
                }
            }
            metrics.TotalDetected = totalDetected;
            metrics.RelaxationAdmittedCount = relaxationAdmitted;

            // Sort all bounds lists by (Y, X) so production output is independent of thread scheduling (this also
            // orders the main-thread BloomSuppressedBounds additions above). Bit-identical to the prior single
            // SortBounds when bloom suppression is off.
            metrics.SortBounds();

            return stars;
        }

        private List<StarCandidateRegion> CollectStarCandidates(Mat srcImage, Mat structureMap, StarDetectorParams p, StarDetectorMetrics metrics, CancellationToken ct) {
            const float ZERO_THRESHOLD = 0.001f;

            var candidates = new List<StarCandidateRegion>();
            int width = structureMap.Width;
            int height = structureMap.Height;
            EvaluateGlobalMetrics(srcImage, p, metrics);

            unsafe {
                var structureData = (float*)structureMap.DataPointer;
                for (int yTop = 0, xRight = width - 1, yBottom = height - 1; yTop < yBottom; ++yTop) {
                    ct.ThrowIfCancellationRequested();

                    for (int xLeft = 0; xLeft < xRight; ++xLeft) {
                        // Skip background and pixels already visited
                        if (structureData[yTop * width + xLeft] < ZERO_THRESHOLD) {
                            continue;
                        }

                        // Each candidate gets its OWN point list so the parallel Stage B can read them
                        // concurrently without sharing/reuse.
                        var starPoints = new List<Point>(256);

                        // Grow the star bounding box as we walk around the image, downward and to the right
                        var starBounds = new Rect(xLeft, yTop, 1, 1);

                        for (int y = yTop, x = xLeft; ;) {
                            var rowOffsetStart = y * width;
                            int rowPointsAdded = 0;
                            if (structureData[rowOffsetStart + x] >= ZERO_THRESHOLD) {
                                starPoints.Add(new Point(x, y));
                                ++rowPointsAdded;
                            }

                            int rowStartX = x, rowEndX;
                            // Keep adding pixels to the left until you run into a background pixel, but only if the starting pixel belongs to a star
                            if (rowPointsAdded > 0) {
                                for (rowStartX = x; rowStartX > 0;) {
                                    if (structureData[rowOffsetStart + (rowStartX - 1)] < ZERO_THRESHOLD) {
                                        break;
                                    }
                                    starPoints.Add(new Point(--rowStartX, y));
                                    ++rowPointsAdded;
                                }
                            }

                            // Keep adding pixels to the right until you run into a background pixel
                            for (rowEndX = x; rowEndX < xRight;) {
                                if (structureData[rowOffsetStart + (rowEndX + 1)] < ZERO_THRESHOLD) {
                                    if (rowPointsAdded > 0 || rowEndX >= starBounds.Right) {
                                        // We're expanding the star search area down and to the right. If we haven't encountered any star pixels on
                                        // this row yet, we should keep iterating until we find the first one or we've hit the current right boundary of
                                        // star bounds
                                        break;
                                    }
                                    ++rowEndX;
                                } else {
                                    starPoints.Add(new Point(++rowEndX, y));
                                    ++rowPointsAdded;
                                }
                            }

                            if (rowStartX < starBounds.Left) {
                                starBounds.Width += (starBounds.Left - rowStartX);
                                starBounds.X = rowStartX;
                            }
                            if (rowEndX > (starBounds.Right - 1)) {
                                starBounds.Width += (rowEndX - starBounds.Right + 1);
                            }

                            if (rowPointsAdded == 0) {
                                starBounds.Height = y - yTop;
                                break;
                            }
                            if (y == yBottom) {
                                starBounds.Height = y - yTop + 1;
                                break;
                            }

                            // Move onto the next row
                            ++y;
                        }

                        ++metrics.StructureCandidates;
                        candidates.Add(new StarCandidateRegion(starBounds, starPoints));

                        // Now that we've collected the pixels within the star bounding box, we can zero them all out so we don't look again
                        for (int y = starBounds.Top; y < starBounds.Bottom; ++y) {
                            for (int x = starBounds.Left; x < starBounds.Right; ++x) {
                                structureData[y * width + x] = 0.0f;
                            }
                        }
                    }
                }
            }

            return candidates;
        }

        /// <summary>
        /// Computes the effective fill-ratio threshold the TooDistorted gate compares against for a candidate of
        /// bbox max-dimension <paramref name="candidateSize"/> (= max(bbox.Width, bbox.Height), the same d the
        /// gate divides by).
        ///
        /// When <see cref="StarDetectorParams.DefocusAwareDistortion"/> is FALSE this returns
        /// <see cref="StarDetectorParams.MaxDistortion"/> verbatim, so the gate is byte-for-byte the legacy gate
        /// (default-OFF ⇒ bit-identical detection). When TRUE it returns
        ///   MaxDistortion · clamp(SizeReference / candidateSize, MinFactor, 1.0)
        /// so candidates at or below the size reference keep the strict threshold (factor 1.0) and larger
        /// candidates (defocused donuts, large bbox + low fill-ratio) get a more permissive threshold, floored at
        /// MinFactor · MaxDistortion. Pure + deterministic (no image access) so it is unit-testable in isolation.
        /// </summary>
        public static double ComputeEffectiveMaxDistortion(StarDetectorParams p, double candidateSize) {
            // The relaxation is active when the explicit DefocusAwareDistortion flag is on OR the donut master is on
            // (donut recovery defaults the distortion relaxation on, like the structure boost). It only relaxes for
            // LARGE candidates (candidateSize > SizeReference), so near-focus point sources are unaffected; small
            // fields stay bit-identical, and master-OFF + flag-OFF returns MaxDistortion verbatim.
            if (!p.DefocusAwareDistortion && !p.DefocusAwareDonutDetection) {
                return p.MaxDistortion;
            }

            // Degenerate sizes can't make the gate permissive; fall back to the strict threshold.
            if (candidateSize <= 0.0 || p.DefocusDistortionSizeReference <= 0.0) {
                return p.MaxDistortion;
            }

            var factor = p.DefocusDistortionSizeReference / candidateSize;
            // clamp to [MinFactor, 1.0]: ≤ SizeReference ⇒ ≥ 1 ⇒ strict; larger ⇒ relaxes toward the floor.
            var minFactor = p.DefocusDistortionMinFactor;
            if (factor > 1.0) {
                factor = 1.0;
            } else if (factor < minFactor) {
                factor = minFactor;
            }
            return p.MaxDistortion * factor;
        }

        /// <summary>
        /// Effective per-pixel clip multiplier (× noiseSigma) used for a candidate's flux / centroid / HFR
        /// measurement. When <see cref="StarDetectorParams.DefocusAwareDonutDetection"/> is FALSE this returns
        /// <see cref="StarDetectorParams.StarClippingMultiplier"/> verbatim (default-OFF ⇒ bit-identical). When TRUE
        /// and the candidate is EXTENDED (bbox max-dim <paramref name="candidateSize"/> >=
        /// <see cref="StarDetectorParams.DefocusDistortionSizeReference"/> — the same defocused-star proxy the
        /// distortion / sensitivity relaxations use) it caps the multiplier at <see cref="DonutClipMultiplierCap"/>
        /// so a high (compact-star-calibrated) clip cannot strip a thin defocused ring down to its brightest arc.
        /// Uses Math.Min, so it NEVER increases the clip: profiles with StarClippingMultiplier &lt;= the cap are
        /// unchanged even with the master on, and only near-focus-aggressive profiles get the donut relief. Pure +
        /// deterministic, so it is unit-testable in isolation.
        /// </summary>
        public static double EffectiveClipMultiplier(StarDetectorParams p, double candidateSize) {
            if (p.DefocusAwareDonutDetection && p.DefocusDistortionSizeReference > 0.0
                && candidateSize >= p.DefocusDistortionSizeReference) {
                return Math.Min(p.StarClippingMultiplier, DonutClipMultiplierCap);
            }
            return p.StarClippingMultiplier;
        }

        /// <summary>
        /// The SMALLEST value <see cref="EffectiveClipMultiplier"/> can return for <paramref name="p"/> across every
        /// possible candidate size — the donut-capped value when the master can cap at all, else the multiplier
        /// verbatim. Candidate-size-INDEPENDENT by construction, which is what makes
        /// <see cref="InertSensitivityBound"/> a property of the SETTINGS rather than of one candidate. Keep in
        /// lockstep with <see cref="EffectiveClipMultiplier"/>: same predicate, minus the size test.
        /// </summary>
        public static double MinEffectiveClipMultiplier(StarDetectorParams p) {
            if (p.DefocusAwareDonutDetection && p.DefocusDistortionSizeReference > 0.0) {
                return Math.Min(p.StarClippingMultiplier, DonutClipMultiplierCap);
            }
            return p.StarClippingMultiplier;
        }

        /// <summary>
        /// Strict lower bound on the Sensitivity-gate statistic for every candidate that reaches the gate. A
        /// <see cref="StarDetectorParams.Sensitivity"/> at or below this rejects <b>nothing</b>, so
        /// <see cref="StarDetectorMetrics.LowSensitivity"/> is then identically 0 and its value carries no
        /// information about the frame at all (followup F28). 0.75 × 2.0 = <b>1.5</b> at shipped defaults.
        ///
        /// <para><b>Derivation.</b> Every clip survivor satisfies <c>raw − background &gt; clipMargin =
        /// EffectiveClipMultiplier · σ</c>, so <c>meanFlux</c> — the mean over exactly those survivors — exceeds it
        /// too; <c>peak</c> is the max over the same set, so <c>peak ≥ meanFlux</c>; hence
        /// <c>NormalizedBrightness = peak − (1 − PeakResponse)·meanFlux = (peak − meanFlux) + PeakResponse·meanFlux
        /// ≥ PeakResponse·meanFlux</c>. Dividing by the same σ the gate divides by gives
        /// <c>sensitivity &gt; PeakResponse × EffectiveClipMultiplier</c>. The donut branch takes
        /// <c>max(perPixel, integrated)</c>, so it can only raise the statistic and the bound survives.</para>
        ///
        /// <para><b>Not a constant.</b> <see cref="StarDetectorParams.PeakResponse"/> and
        /// <see cref="StarDetectorParams.StarClippingMultiplier"/> are both searched optimizer axes, so a caller
        /// must compute this from the params the run was actually evaluated with. Hard-coding 1.5 would be wrong
        /// for exactly the landings this matters on — F23 wave 1 measured configurations at StarClip 6.25 and
        /// 6.75, where the bound is over 4×.</para>
        /// </summary>
        public static double InertSensitivityBound(StarDetectorParams p) =>
            p == null ? double.NaN : p.PeakResponse * MinEffectiveClipMultiplier(p);

        /// <summary>
        /// The gate a set of detector settings ACTUALLY enforces:
        /// <c>max(Sensitivity, InertSensitivityBound)</c>. Report this wherever a landing's
        /// <see cref="StarDetectorParams.Sensitivity"/> is quoted (followup F33).
        ///
        /// <para><b>Why the raw axis misleads.</b> Sensitivity below
        /// <see cref="InertSensitivityBound"/> rejects nothing, so the structure/clip stage — not the Sensitivity
        /// knob — is what is culling stars. A landing can therefore read <c>Sensitivity 0.0</c>, which looks like
        /// the synthetic bank's "the optimizer drove the gate to its floor" pathology, while enforcing a gate
        /// several times the shipped default. The real-bank <c>mccomiskey</c> run is the case that motivated this:
        /// Sensitivity 0.0 with PeakResponse 0.98 × StarClip 10.0 ⇒ an effective gate of <b>9.81</b>, and
        /// detections collapsing 3606 → 43. Read as a floor landing it says the opposite of what it does.</para>
        ///
        /// <para>Two more landings in the same arm are misread the same way (<c>caboose</c> at 5.05 on the real
        /// bank; D11 at 2.36 and D12 at 2.10 on the synthetic one), so this is a systematic reading error rather
        /// than one odd run.</para>
        /// </summary>
        public static double EffectiveSensitivityGate(StarDetectorParams p) =>
            p == null ? double.NaN : Math.Max(p.Sensitivity, InertSensitivityBound(p));

        /// <summary>
        /// The wavelet layer count the structure-removal residual is ACTUALLY computed at — <see cref="StarDetectorParams.StructureLayers"/>
        /// plus whichever defocus boost is in force. The single source of truth for that rule: step 4 of
        /// <c>Detect</c> uses it, and so does the optimizer's cost readout, so the two cannot drift.
        ///
        /// <para><b>Why anything outside the detector cares.</b> The residual is recomputed at <c>2^layers</c>, so
        /// this number is the dominant term in a detection's COST, not merely in its behaviour. Measured on the
        /// synthetic bank (<c>D15_cdk20_3454mm_e47</c>, 9 frames, binning 1) the wall time per layer runs
        /// 27 / 46 / 117 / 254 / 526 s for layers 4…8 — <b>roughly a doubling per layer</b>. A parameter search
        /// that wanders two layers deeper is spending ~4× per evaluation for a gain the objective reports in its
        /// fourth decimal, and nothing told the user that (F52).</para>
        /// </summary>
        public static int EffectiveStructureLayers(StarDetectorParams p) {
            if (p == null) {
                return 0;
            }
            if (p.DefocusAwareStructure) {
                return Math.Max(1, p.StructureLayers + p.StructureLayerBoost);
            }
            if (p.DefocusAwareDonutDetection) {
                return Math.Max(1, p.StructureLayers + DonutDefaultStructureLayerBoost);
            }
            return p.StructureLayers;
        }

        /// <summary>
        /// Companion to <see cref="ComputeEffectiveMaxDistortion"/> for the NotCentered gate. Computes the
        /// effective StarCenterTolerance the centering check uses for a candidate of bbox max-dimension
        /// <paramref name="candidateSize"/> (= max(bbox.Width, bbox.Height), the SAME defocus proxy the distortion
        /// gate uses).
        ///
        /// When <see cref="StarDetectorParams.DefocusAwareCentering"/> is FALSE this returns
        /// <see cref="StarDetectorParams.StarCenterTolerance"/> verbatim, so the gate is byte-for-byte the legacy
        /// gate (default-OFF ⇒ bit-identical detection). When TRUE it returns
        ///   StarCenterTolerance · clamp(candidateSize / SizeReference, 1.0, ToleranceFactor)
        /// (additionally clamped so the effective tolerance never exceeds 1.0, where the centered acceptance
        /// sub-box covers the whole bbox). Candidates at or below the size reference keep the strict tolerance
        /// (factor 1.0); larger candidates (defocused donuts, whose hollow ring destabilizes the intensity-weighted
        /// centroid) get a LARGER tolerance → a bigger centered sub-box → the NotCentered gate is MORE permissive,
        /// admitting off-center donut centroids. Pure + deterministic (no image access) so it is unit-testable in
        /// isolation. Mirrors the distortion helper's clamp structure (relaxation direction is inverted because a
        /// LARGER tolerance is more permissive, whereas a SMALLER distortion threshold is more permissive).
        /// </summary>
        public static double ComputeEffectiveStarCenterTolerance(double baseTolerance, double candidateSize, double sizeReference, double maxFactor) {
            // Degenerate inputs can't make the gate permissive; fall back to the strict tolerance. maxFactor < 1
            // would TIGHTEN the gate, which this helper must never do, so treat it as the 1.0 no-op.
            if (candidateSize <= 0.0 || sizeReference <= 0.0 || maxFactor <= 1.0) {
                return baseTolerance;
            }

            var factor = candidateSize / sizeReference;
            // clamp to [1.0, maxFactor]: ≤ SizeReference ⇒ ≤ 1 ⇒ strict; larger ⇒ relaxes toward the ceiling.
            if (factor < 1.0) {
                factor = 1.0;
            } else if (factor > maxFactor) {
                factor = maxFactor;
            }

            var effective = baseTolerance * factor;
            // The tolerance is a ratio of the bbox; 1.0 is the max valid value (sub-box == whole bbox). Never
            // exceed it (a value > 1.0 would place the acceptance band outside the bbox and is meaningless).
            return effective > 1.0 ? 1.0 : effective;
        }

        /// <summary>
        /// DETECTION-ONLY annularity test for defocus-aware donut recovery. Rasterizes the candidate's structure
        /// points into a bbox-sized mask, flood-fills the background INWARD from the bbox border (4-connectivity),
        /// and counts the background cells NOT border-reachable — i.e. an enclosed interior HOLE (the dark center of
        /// a donut ring; classic morphological hole-fill / imfill). Returns the hole pixel count when it is at least
        /// <paramref name="minHoleFraction"/> of the bbox area, else 0. A solid disk, a straight line, or a C-shaped
        /// arc open to the border has no enclosed hole and returns 0. Pure + deterministic; does NOT mutate
        /// <paramref name="starPoints"/>, so flux/HFR/centroid measured from those points are unchanged.
        /// </summary>
        public static int CountEnclosedHole(List<Point> starPoints, Rect bounds, double minHoleFraction) {
            int w = bounds.Width, h = bounds.Height;
            if (w <= 2 || h <= 2 || starPoints == null || starPoints.Count == 0 || minHoleFraction <= 0.0) {
                return 0;
            }
            var fg = new bool[w * h];
            foreach (var pt in starPoints) {
                int lx = pt.X - bounds.X, ly = pt.Y - bounds.Y;
                if (lx >= 0 && lx < w && ly >= 0 && ly < h) {
                    fg[ly * w + lx] = true;
                }
            }
            var outside = new bool[w * h];
            var stack = new Stack<int>();
            void Seed(int x, int y) {
                int idx = y * w + x;
                if (!fg[idx] && !outside[idx]) { outside[idx] = true; stack.Push(idx); }
            }
            for (int x = 0; x < w; ++x) { Seed(x, 0); Seed(x, h - 1); }
            for (int y = 0; y < h; ++y) { Seed(0, y); Seed(w - 1, y); }
            while (stack.Count > 0) {
                int idx = stack.Pop(); int x = idx % w, y = idx / w;
                if (x > 0) Seed(x - 1, y);
                if (x < w - 1) Seed(x + 1, y);
                if (y > 0) Seed(x, y - 1);
                if (y < h - 1) Seed(x, y + 1);
            }
            int hole = 0;
            for (int i = 0; i < fg.Length; ++i) {
                if (!fg[i] && !outside[i]) { ++hole; }
            }
            return hole >= minHoleFraction * w * h ? hole : 0;
        }

        /// <summary>
        /// Second-moment eccentricity of a candidate's point cloud, e = sqrt(1 - λ2/λ1) where λ1 ≥ λ2 are the
        /// eigenvalues of the 2×2 covariance matrix of the points. A line → λ2 ≈ 0 ⇒ e → 1 (diffraction spike /
        /// satellite trail); a round disk/ring → e small. Orientation-independent (unlike a bbox aspect ratio,
        /// which a diagonal spike defeats). Returns 0 for degenerate inputs. Pure + deterministic.
        /// </summary>
        public static double ComputePointCloudEccentricity(List<Point> starPoints) {
            int n = starPoints?.Count ?? 0;
            if (n < 3) {
                return 0.0;
            }
            double mx = 0, my = 0;
            foreach (var p in starPoints) { mx += p.X; my += p.Y; }
            mx /= n; my /= n;
            double sxx = 0, syy = 0, sxy = 0;
            foreach (var p in starPoints) {
                double dx = p.X - mx, dy = p.Y - my;
                sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
            }
            sxx /= n; syy /= n; sxy /= n;
            double tr = sxx + syy;
            double det = sxx * syy - sxy * sxy;
            double disc = Math.Sqrt(Math.Max(0.0, tr * tr / 4.0 - det));
            double l1 = tr / 2.0 + disc;
            double l2 = tr / 2.0 - disc;
            if (l1 <= 0.0) {
                return 0.0;
            }
            double ratio = l2 / l1;
            if (ratio < 0.0) ratio = 0.0;
            return Math.Sqrt(1.0 - ratio);
        }

        // Emits a RejectedCandidateRecord into the (thread-safe) bag, guarded by the caller's null check. centerX/Y
        // are pre-ROI image coords (the ROI offset is applied once during materialization in GateAndMeasureInternal,
        // mirroring the metrics *Bounds lists). measured/threshold are NaN for the non-scalar gates; hfr is NaN when
        // the candidate was rejected before HFR could be measured.
        private static void RecordRejection(ConcurrentBag<RejectedCandidateRecord> bag, Rect bounds, string gate, double measured, double threshold, double centerX, double centerY, double hfr = double.NaN) {
            bag.Add(new RejectedCandidateRecord {
                Bounds = bounds,
                Gate = gate,
                MeasuredValue = measured,
                ThresholdValue = threshold,
                CandidateSize = Math.Max(bounds.Width, bounds.Height),
                CenterX = centerX,
                CenterY = centerY,
                Hfr = hfr
            });
        }

        // Measures the HFR of a candidate that a gate rejected BEFORE the normal MeasureStar step (LowSensitivity/
        // NotCentered/TooFlat), so the review's HFR display can show it. Mirrors the accept-path Star construction +
        // MeasureStar; returns NaN if the measurement fails. Called only on the diagnostics path (rejectedBag != null).
        private double TryMeasureRejectedHfr(Mat srcImage, StarCandidate starCandidate, Rect starBounds, StarDetectorParams p, double srcImageNoiseSigma) {
            try {
                var probe = new Star() {
                    Center = starCandidate.Center,
                    Background = starCandidate.Background,
                    BackgroundPlane = starCandidate.BackgroundPlane,
                    MeanBrightness = starCandidate.PixelCount > 0 ? starCandidate.TotalFlux / starCandidate.PixelCount : 0.0,
                    StarBoundingBox = starBounds,
                    PeakBrightness = starCandidate.Peak
                };
                return MeasureStar(srcImage, probe, p, srcImageNoiseSigma) ? probe.HFR : double.NaN;
            } catch {
                return double.NaN;
            }
        }

        private Star EvaluateStarCandidate(Mat srcImage, StarDetectorParams p, Rect starBounds, List<Point> starPoints, double srcImageNoiseSigma, StarDetectorMetrics metrics, ConcurrentBag<ContaminationDiagnosticRecord> diagnosticsBag, ConcurrentBag<RejectedCandidateRecord> rejectedBag) {
            // Now we have a potential star bounding box as well as the coordinates of every star pixel. If this is a reliable star,
            // we compute its barycenter and include it.
            //
            // Rejection criteria:
            //  1) Touching the border. We assume the star is clipped
            //  2) Elongated stars
            //  3) Star center too far away from the center of the bounding box
            //  4) Too flat
            // Note: partially-saturated stars (Background + Peak >= SaturationThreshold) are no longer rejected here.
            // Instead, they are passed to PSF fitting which masks saturated pixels during the fit.

            // INFORMATIONAL (never affects accept/reject): set true iff this candidate passes a defocus-RELAXED
            // gate (distortion and/or centering) that the verbatim STRICT gate would have rejected. Propagated to
            // the accepted Star.RelaxationAdmitted. When the defocus-aware gates are OFF, effective == strict, so
            // this can never flip — detection stays bit-identical.
            bool relaxationAdmitted = false;

            // Pre-measurement candidate center (bbox center); refined to the measured centroid once available.
            var bboxCenterX = starBounds.X + starBounds.Width / 2.0;
            var bboxCenterY = starBounds.Y + starBounds.Height / 2.0;

            // Too small
            if (starBounds.Width < p.MinimumStarBoundingBoxSize || starBounds.Height < p.MinimumStarBoundingBoxSize) {
                ++metrics.TooSmall;
                if (rejectedBag != null) {
                    RecordRejection(rejectedBag, starBounds, RejectionGate.TooSmall, Math.Min(starBounds.Width, starBounds.Height), p.MinimumStarBoundingBoxSize, bboxCenterX, bboxCenterY);
                }
                return null;
            }

            // Touching the border
            if (starBounds.X == 0 || starBounds.Y == 0 || starBounds.Right == srcImage.Width || starBounds.Bottom == srcImage.Height) {
                ++metrics.OnBorder;
                if (rejectedBag != null) {
                    RecordRejection(rejectedBag, starBounds, RejectionGate.OnBorder, double.NaN, double.NaN, bboxCenterX, bboxCenterY);
                }
                return null;
            }

            // Diffraction-spike / satellite-trail rejection (defocus-aware, opt-in). A spike/trail is extremely
            // LINEAR; a donut (even astigmatic) is roundish. Judge the RAW point cloud's second-moment eccentricity
            // (before any hole-fill). DonutMaxStreakEccentricity == 1.0 ⇒ gate OFF (a perfect line has e → 1.0).
            // Gated by the master flag + a finite point count so faint specks aren't judged on noise ⇒ bit-identical OFF.
            if (p.DefocusAwareDonutDetection && p.DonutMaxStreakEccentricity < 1.0 && starPoints.Count >= 8) {
                var streakEcc = ComputePointCloudEccentricity(starPoints);
                if (streakEcc >= p.DonutMaxStreakEccentricity) {
                    metrics.TooElongatedBounds.Add(starBounds);
                    if (rejectedBag != null) {
                        RecordRejection(rejectedBag, starBounds, RejectionGate.TooElongated, streakEcc, p.DonutMaxStreakEccentricity, bboxCenterX, bboxCenterY);
                    }
                    return null;
                }
            }

            // Too distorted. The fill-ratio metric is (pixel count) / d², where d is the bbox max dimension; a
            // perfect disk fills ~PI/4 ≈ 0.79. When DefocusAwareDistortion is off, the effective threshold is
            // exactly p.MaxDistortion (bit-identical to the legacy gate). When on, it is relaxed for large
            // candidates so large-defocus donuts (big bbox, low fill-ratio) survive — see
            // ComputeEffectiveMaxDistortion. The same effective threshold drives both the decision and the
            // TooDistorted metrics tally.
            double d = Math.Max(starBounds.Width, starBounds.Height);
            // Annularity-aware fill ratio (defocus-aware, opt-in, DETECTION-ONLY). A defocused donut is a hollow
            // ring whose fill-ratio (ring pixels / d²) is far below MaxDistortion ⇒ the strict gate rejects it. When
            // the candidate has an enclosed interior hole (≥ DonutMinAnnularityHoleFraction of the bbox), add the
            // hole's pixel COUNT so the ratio reflects the FILLED disk and the strict threshold accepts it — while a
            // spike/arc (no enclosed hole) is unaffected and still rejected. CountEnclosedHole does NOT mutate
            // starPoints, so flux/HFR/centroid stay byte-identical. 0 when the master is OFF ⇒ bit-identical.
            int donutHoleArea = (p.DefocusAwareDonutDetection && p.DonutMinAnnularityHoleFraction > 0.0)
                ? CountEnclosedHole(starPoints, starBounds, p.DonutMinAnnularityHoleFraction)
                : 0;
            var fillRatio = (starPoints.Count + donutHoleArea) / d / d;
            var effectiveMaxDistortion = ComputeEffectiveMaxDistortion(p, d);
            if (fillRatio < effectiveMaxDistortion) {
                metrics.TooDistortedBounds.Add(starBounds);
                if (rejectedBag != null) {
                    RecordRejection(rejectedBag, starBounds, RejectionGate.TooDistorted, fillRatio, effectiveMaxDistortion, bboxCenterX, bboxCenterY);
                }
                return null;
            }
            // The accept/reject decision above uses the effective threshold ONLY. Separately (informational), note
            // whether the verbatim STRICT threshold would have rejected this candidate — i.e. it survives only
            // because the defocus relaxation lowered the bar. ComputeEffectiveMaxDistortion with the gate OFF
            // returns p.MaxDistortion, so strict == effective ⇒ this never trips when the gate is OFF.
            if (fillRatio < p.MaxDistortion) {
                relaxationAdmitted = true;
            }

            var starCandidate = ComputeStarParameters(srcImage, starBounds, p, srcImageNoiseSigma, starPoints);
            if (starCandidate == null) {
                metrics.DegenerateBounds.Add(starBounds);
                if (rejectedBag != null) {
                    RecordRejection(rejectedBag, starBounds, RejectionGate.Degenerate, double.NaN, double.NaN, bboxCenterX, bboxCenterY);
                }
                return null;
            }

            // Track partially-saturated stars in metrics (clipped pixels will be masked during PSF fit instead of rejecting)
            if ((starCandidate.Background + starCandidate.Peak) >= p.SaturationThreshold) {
                metrics.SaturatedBounds.Add(starBounds);
            }

            // Not bright enough (background already subtracted out) relative to noise level
            var sensitivity = starCandidate.NormalizedBrightness / srcImageNoiseSigma;
            // Donut-aware sensitivity (defocus recovery): a faint defocused donut/disk spreads its flux thinly over
            // a LARGE footprint, so its per-pixel peak — and hence NormalizedBrightness — is low even when the
            // INTEGRATED flux is a strong detection. For an EXTENDED candidate (bbox max-dim >= the defocus size
            // reference — a defocused-star proxy; partially-filled donuts often have no clean enclosed hole, so
            // size is a better discriminator than annularity here) also gauge brightness by the integrated-flux
            // SNR  TotalFlux / (σ·√N), the matched-filter statistic that is √N× more sensitive to an extended
            // source. Small fragments / point noise are not extended, so they keep the strict per-pixel floor; and
            // because the SAME Sensitivity threshold now means "σ of an INTEGRATED detection" on this path, the
            // bar stays high (a real source clears it; noise does not). Gated by the master ⇒ bit-identical OFF.
            if (p.DefocusAwareDonutDetection && srcImageNoiseSigma > 0.0 && starCandidate.UnclippedPixelCount > 0
                && d >= p.DefocusDistortionSizeReference) {
                var integratedSnr = starCandidate.TotalFlux / (srcImageNoiseSigma * Math.Sqrt(starCandidate.UnclippedPixelCount));
                if (integratedSnr > sensitivity) {
                    sensitivity = integratedSnr;
                }
            }
            if (sensitivity <= p.Sensitivity) {
                metrics.LowSensitivityBounds.Add(starBounds);
                if (rejectedBag != null) {
                    RecordRejection(rejectedBag, starBounds, RejectionGate.LowSensitivity, sensitivity, p.Sensitivity, starCandidate.Center.X, starCandidate.Center.Y,
                        TryMeasureRejectedHfr(srcImage, starCandidate, starBounds, p, srcImageNoiseSigma));
                }
                return null;
            }

            // Measured center too far away from being the peak. The accept/reject decision uses the EFFECTIVE
            // (possibly relaxed) tolerance; we also compute the STRICT result so an accepted-by-relaxation centroid
            // is flagged. When DefocusAwareCentering is OFF, effective == strict, so centeredStrict == centered.
            var centered = IsStarCentered(starCandidate, p, out var centeredStrict);
            if (!centered) {
                metrics.NotCenteredBounds.Add(starBounds);
                if (rejectedBag != null) {
                    // Normalized centroid offset: max over axes of |centroid - bbox center| / (bbox half-extent).
                    // The gate accepts iff this is <= the effective tolerance, so it inverts monotonically in
                    // StarCenterTolerance (raise the tolerance to >= the offset to recover the star).
                    var ncBox = starCandidate.StarBoundingBox;
                    var ncEffTol = (p.DefocusAwareCentering || p.DefocusAwareDonutDetection)
                        ? ComputeEffectiveStarCenterTolerance(p.StarCenterTolerance, Math.Max(ncBox.Width, ncBox.Height), p.DefocusDistortionSizeReference, p.DefocusCenteringToleranceFactor)
                        : p.StarCenterTolerance;
                    var ncOffX = ncBox.Width > 0 ? Math.Abs(starCandidate.Center.X - (ncBox.X + ncBox.Width / 2.0)) / (ncBox.Width / 2.0) : 0.0;
                    var ncOffY = ncBox.Height > 0 ? Math.Abs(starCandidate.Center.Y - (ncBox.Y + ncBox.Height / 2.0)) / (ncBox.Height / 2.0) : 0.0;
                    RecordRejection(rejectedBag, starBounds, RejectionGate.NotCentered, Math.Max(ncOffX, ncOffY), ncEffTol, starCandidate.Center.X, starCandidate.Center.Y,
                        TryMeasureRejectedHfr(srcImage, starCandidate, starBounds, p, srcImageNoiseSigma));
                }
                return null;
            }
            if (!centeredStrict) {
                relaxationAdmitted = true;
            }

            // Too flat. Intentionally active during AutoFocus as well — see
            // HocusFocusStarDetection.GetStarDetectorParams (accuracy analysis F1).
            if (starCandidate.StarMedian >= (p.PeakResponse * starCandidate.Peak)) {
                metrics.TooFlatBounds.Add(starBounds);
                if (rejectedBag != null) {
                    var flatRatio = starCandidate.Peak != 0.0 ? starCandidate.StarMedian / starCandidate.Peak : double.NaN;
                    RecordRejection(rejectedBag, starBounds, RejectionGate.TooFlat, flatRatio, p.PeakResponse, starCandidate.Center.X, starCandidate.Center.Y,
                        TryMeasureRejectedHfr(srcImage, starCandidate, starBounds, p, srcImageNoiseSigma));
                }
                return null;
            }

            // Valid candidate!
            var star = new Star() {
                Center = starCandidate.Center,
                Background = starCandidate.Background,
                BackgroundPlane = starCandidate.BackgroundPlane,
                // NOTE (F11): MeanBrightness intentionally divides TotalFlux by the full structure-footprint
                // pixel count (PixelCount = starPoints.Count), unlike NormalizedBrightness's meanFlux which uses
                // the clip-survivor count. This per-footprint surface-brightness value feeds only the optional
                // "brightest N AF stars" selection (analysis F8); it is left as-is by decision (design §1).
                MeanBrightness = starCandidate.TotalFlux / starCandidate.PixelCount,
                StarBoundingBox = starBounds,
                PeakBrightness = starCandidate.Peak,
                RelaxationAdmitted = relaxationAdmitted,
                // Informational only — the exact `sensitivity` scalar the gate above just compared against
                // p.Sensitivity (donut branch already applied, if taken). No new computation.
                MeasuredSensitivity = sensitivity
            };

            // Measure HFR, and discard if we couldn't calculate it
            if (!MeasureStar(srcImage, star, p, srcImageNoiseSigma)) {
                ++metrics.HFRAnalysisFailed;
                if (rejectedBag != null) {
                    RecordRejection(rejectedBag, starBounds, RejectionGate.HFRAnalysisFailed, double.NaN, double.NaN, star.Center.X, star.Center.Y);
                }
                return null;
            }

            // HFR below minimum threshold
            if (star.HFR <= p.MinHFR) {
                ++metrics.TooLowHFR;
                if (rejectedBag != null) {
                    RecordRejection(rejectedBag, starBounds, RejectionGate.TooLowHFR, star.HFR, p.MinHFR, star.Center.X, star.Center.Y, star.HFR);
                }
                return null;
            }

            star.StarContaminationSuspected = starCandidate.ContaminationSuspected;
            if (star.StarContaminationSuspected) {
                // Record the bounds regardless of whether the star is rejected, so the annotator can mark
                // contaminated stars even when RejectContaminatedStars removes them from the detected set.
                metrics.ContaminatedBounds.Add(starBounds);
                // Quality gate: reject contaminated stars so HFR/PSF statistics stay clean of one-sided
                // contaminants. Disabled (flag-only) when RejectContaminatedStars is off (e.g. diagnostics).
                if (p.RejectContaminatedStars) {
                    if (rejectedBag != null) {
                        RecordRejection(rejectedBag, starBounds, RejectionGate.Contaminated, double.NaN, p.ContaminationSensitivity, star.Center.X, star.Center.Y, star.HFR);
                    }
                    return null;
                }
            }

            if (p.CollectContaminationDiagnostics && starCandidate.ContaminationDiagnostics != null && diagnosticsBag != null) {
                var record = starCandidate.ContaminationDiagnostics;
                record.CenterX = star.Center.X;
                record.CenterY = star.Center.Y;
                record.Hfr = star.HFR;
                record.Background = star.Background;
                diagnosticsBag.Add(record);
            }

            return star;
        }

        private static bool IsStarCentered(StarCandidate starCandidate, StarDetectorParams p) =>
            IsStarCentered(starCandidate, p, out _);

        /// <summary>
        /// Centering gate. Returns whether the candidate centroid lies within the EFFECTIVE (possibly defocus-
        /// relaxed) acceptance sub-box — the accept/reject decision. Also reports, via
        /// <paramref name="strictCentered"/>, whether it lies within the STRICT acceptance sub-box (built from the
        /// verbatim <see cref="StarDetectorParams.StarCenterTolerance"/>). When DefocusAwareCentering is OFF the
        /// effective tolerance IS the strict tolerance, so the two results are identical (bit-identical detection);
        /// when ON, a candidate that is centered (effective) but not strictCentered was admitted only by the
        /// relaxation. The single decision call site (in EvaluateStarCandidate) honors the EFFECTIVE return for the
        /// NotCentered tally; strictCentered is informational (feeds Star.RelaxationAdmitted).
        /// </summary>
        private static bool IsStarCentered(StarCandidate starCandidate, StarDetectorParams p, out bool strictCentered) {
            var box = starCandidate.StarBoundingBox;
            // Centering relaxation is active when the explicit DefocusAwareCentering flag is on OR the donut master
            // is on (donut recovery defaults it on). It only grows the tolerance for LARGE candidates, so near-focus
            // point sources are unaffected.
            var centeringRelaxed = p.DefocusAwareCentering || p.DefocusAwareDonutDetection;
            var effectiveTolerance = centeringRelaxed
                ? ComputeEffectiveStarCenterTolerance(p.StarCenterTolerance, Math.Max(box.Width, box.Height), p.DefocusDistortionSizeReference, p.DefocusCenteringToleranceFactor)
                : p.StarCenterTolerance;
            var centered = CenterWithinTolerance(box, starCandidate.Center, effectiveTolerance);
            // The strict tolerance is the verbatim StarCenterTolerance — i.e. exactly the value the effective
            // tolerance equals when the gate is OFF. So with the gate OFF strictCentered == centered always.
            strictCentered = centeringRelaxed
                ? CenterWithinTolerance(box, starCandidate.Center, p.StarCenterTolerance)
                : centered;
            return centered;
        }

        /// <summary>True when <paramref name="center"/> lies within the centered acceptance sub-box of
        /// <paramref name="box"/> at the given <paramref name="tolerance"/> (a fraction of the bbox extents,
        /// concentric with the bbox).</summary>
        private static bool CenterWithinTolerance(Rect box, Point2d center, double tolerance) {
            var centerThresholdBoxWidth = box.Width * tolerance;
            var centerThresholdBoxHeight = box.Height * tolerance;
            var minX = box.X + (box.Width - centerThresholdBoxWidth) / 2.0;
            var maxX = minX + centerThresholdBoxWidth;
            var minY = box.Y + (box.Height - centerThresholdBoxHeight) / 2.0;
            var maxY = minY + centerThresholdBoxHeight;
            return center.X >= minX && center.X <= maxX && center.Y >= minY && center.Y <= maxY;
        }

        /// <summary>
        /// Estimates a robust local background scatter (sigma) from a background-annulus sample, used as the
        /// noise scale for the contamination asymmetry test instead of the global image-noise floor. Near
        /// bright stars and in gradients/nebulosity the local background roughness is several times the global
        /// noise, so a bar built on the global sigma is far too tight and flags ordinary stars.
        ///
        /// Uses the median absolute deviation (MAD), whose 50% breakdown point means a single contaminated
        /// sector (≤ 1/8 of the annulus) cannot inflate it enough to hide itself, while smooth gradients that
        /// lift every sector are correctly absorbed into the bar. <paramref name="sortedPixels"/> must be sorted
        /// ascending over [0, <paramref name="count"/>). Returns the consistency-corrected sigma (1.4826 × MAD),
        /// or 0 if the sample is too small or perfectly flat — signaling the caller to fall back to the global
        /// noise sigma.
        /// </summary>
        internal static double ComputeLocalBackgroundSigma(float[] sortedPixels, int count, double median) {
            const int MinPixelsForLocalSigma = 8;
            if (sortedPixels == null || count < MinPixelsForLocalSigma) {
                return 0.0;
            }
            var deviations = new double[count];
            for (int i = 0; i < count; ++i) {
                deviations[i] = Math.Abs(sortedPixels[i] - median);
            }
            Array.Sort(deviations);
            var mad = ComputeMedian(deviations);
            return 1.4826 * mad;
        }

        /// <summary>
        /// Computes the median of a pre-sorted array of doubles.
        /// For an even-length array, returns the average of the two middle elements.
        /// </summary>
        internal static double ComputeMedian(double[] sortedPixels) {
            if (sortedPixels.Length == 0) {
                throw new ArgumentException("Array must not be empty", nameof(sortedPixels));
            }
            if (sortedPixels.Length % 2 == 1) {
                return sortedPixels[sortedPixels.Length >> 1];
            } else {
                return (sortedPixels[(sortedPixels.Length >> 1) - 1] + sortedPixels[sortedPixels.Length >> 1]) / 2.0;
            }
        }

        /// <summary>
        /// Computes the median of the first <paramref name="count"/> elements of a pre-sorted array of doubles.
        /// For an even count, returns the average of the two middle elements.
        /// Use this overload when the array may be larger than the logical data (e.g. rented from ArrayPool).
        /// </summary>
        internal static double ComputeMedian(double[] sortedPixels, int count) {
            if (count == 0) {
                throw new ArgumentException("Count must be greater than zero", nameof(count));
            }
            if (count % 2 == 1) {
                return sortedPixels[count >> 1];
            } else {
                return (sortedPixels[(count >> 1) - 1] + sortedPixels[count >> 1]) / 2.0;
            }
        }

        /// <summary>
        /// Computes an iterative flux-weighted centroid over a list of candidate star pixels.
        /// Pass 1 includes every pixel above <paramref name="backgroundThreshold"/>.
        /// Each subsequent pass restricts the contributing pixels to those that lie within
        /// a circle of radius <paramref name="apertureRadius"/> centered on the centroid
        /// estimate from the previous pass.  The circular aperture prevents bright
        /// off-axis pixels from biasing the centroid for asymmetric or tilted stars.
        /// </summary>
        /// <param name="imageData">Pointer to the flat float32 pixel array (unsafe).</param>
        /// <param name="imageWidth">Number of pixels per row in the image.</param>
        /// <param name="starPoints">Candidate pixels to consider (from the structure map).</param>
        /// <param name="backgroundPlane">Local background plane subtracted (per pixel) from each value.</param>
        /// <param name="clipMargin">Pixels must exceed plane + this margin to be included (clipping band).</param>
        /// <param name="apertureRadius">Circular aperture radius used from pass 2 onward.</param>
        /// <param name="numPasses">Total number of centroid passes (2 or 3 recommended).</param>
        /// <returns>
        /// Flux-weighted centroid in image-pixel coordinates, or a fallback centre-of-bounding-box
        /// value if no pixel survives the threshold cuts.
        /// </returns>
        internal static unsafe Point2d ComputeIterativeCentroid(
            float* imageData,
            int imageWidth,
            List<Point> starPoints,
            LocalBackgroundPlane backgroundPlane,
            double clipMargin,
            double apertureRadius,
            int numPasses) {

            // Seed the centroid with an unrestricted flux-weighted pass (pass 1).
            double sx = 0, sy = 0, sz = 0;
            foreach (var pt in starPoints) {
                var pixel = imageData[pt.Y * imageWidth + pt.X];
                var background = backgroundPlane.ValueAt(pt.X, pt.Y);
                if (pixel <= background + clipMargin) {
                    continue;
                }
                var flux = pixel - background;
                sx += flux * pt.X;
                sy += flux * pt.Y;
                sz += flux;
            }

            if (sz <= 0) {
                // No pixels above threshold — return the unweighted bounding-box centre
                // (caller can decide to discard this candidate).
                if (starPoints.Count == 0) {
                    return new Point2d(0, 0);
                }
                double sumX = 0, sumY = 0;
                foreach (var pt in starPoints) { sumX += pt.X; sumY += pt.Y; }
                return new Point2d(sumX / starPoints.Count, sumY / starPoints.Count);
            }

            var cx = sx / sz;
            var cy = sy / sz;

            // Passes 2..numPasses: restrict to circular aperture around previous estimate.
            var r2 = apertureRadius * apertureRadius;
            for (int pass = 2; pass <= numPasses; ++pass) {
                sx = 0; sy = 0; sz = 0;
                foreach (var pt in starPoints) {
                    var pixel = imageData[pt.Y * imageWidth + pt.X];
                    var background = backgroundPlane.ValueAt(pt.X, pt.Y);
                    if (pixel <= background + clipMargin) {
                        continue;
                    }
                    var dx = pt.X - cx;
                    var dy = pt.Y - cy;
                    if (dx * dx + dy * dy > r2) {
                        continue;
                    }
                    var flux = pixel - background;
                    sx += flux * pt.X;
                    sy += flux * pt.Y;
                    sz += flux;
                }

                if (sz <= 0) {
                    // Aperture excluded everything — keep the previous estimate
                    break;
                }
                cx = sx / sz;
                cy = sy / sz;
            }

            return new Point2d(cx, cy);
        }

        private StarCandidate ComputeStarParameters(Mat srcImage, Rect starBounds, StarDetectorParams p, double noiseSigma, List<Point> starPoints) {
            var expandedWidth = starBounds.Width + p.BackgroundBoxExpansion * 2;
            var expandedHeight = starBounds.Height + p.BackgroundBoxExpansion * 2;
            // Annulus capacity: expanded box minus star inner box.
            var annulusCapacity = (expandedWidth * expandedHeight) - (starBounds.Width * starBounds.Height);
            int surroundingPixelCount = 0;

            const int MinSectorPixels = 8;
            var cx = starBounds.X + starBounds.Width / 2.0;
            var cy = starBounds.Y + starBounds.Height / 2.0;

            // Rent per-star scratch arrays from the pool to avoid per-star GC pressure in the parallel hot path.
            // ArrayPool.Rent returns an array of length >= requested, with arbitrary stale contents beyond
            // the logical count — we always index within [0, surroundingPixelCount) or [0, numUnclippedPixels).
            var surroundingPixels = ArrayPool<float>.Shared.Rent(annulusCapacity);
            // Retain each background-annulus pixel's (dx, dy, value) relative to the bounding-box center so the
            // robust background plane (used as the local background for centroid/flux/HFR/PSF) and the
            // gradient-robust contamination test can be computed below.
            var annDx = ArrayPool<float>.Shared.Rent(annulusCapacity);
            var annDy = ArrayPool<float>.Shared.Rent(annulusCapacity);
            var annVal = ArrayPool<float>.Shared.Rent(annulusCapacity);
            // starPixels is rented only after the counting pass; null until then so the finally guard works.
            double[] starPixels = null;
            try {
            // Search an expanded box to estimate the median background value
            unsafe {
                var imageData = (float*)srcImage.DataPointer;
                var backgroundStartY = Math.Max(0, starBounds.Y - p.BackgroundBoxExpansion);
                var backgroundStartX = Math.Max(0, starBounds.X - p.BackgroundBoxExpansion);
                var backgroundEndY = Math.Min(srcImage.Height, starBounds.Bottom + p.BackgroundBoxExpansion);
                var backgroundEndX = Math.Min(srcImage.Width, starBounds.Right + p.BackgroundBoxExpansion);
                var pixelPtr = imageData + backgroundStartY * srcImage.Width + backgroundStartX;

                // Pixel array gap between the end of one row and the beginning of the next
                int rowStrideGap = srcImage.Width - expandedWidth;
                // Top part of box
                for (int y = backgroundStartY; y < starBounds.Y; ++y) {
                    for (int x = backgroundStartX; x < backgroundEndX; ++x) {
                        var bgPixel = *pixelPtr;
                        int annIdx = surroundingPixelCount++;
                        surroundingPixels[annIdx] = bgPixel;
                        annDx[annIdx] = (float)(x - cx); annDy[annIdx] = (float)(y - cy); annVal[annIdx] = bgPixel;
                        ++pixelPtr;
                    }

                    // Move to the start of the next row
                    pixelPtr += rowStrideGap;
                }

                // Center part of box
                for (int y = starBounds.Y; y < starBounds.Bottom; ++y) {
                    for (int x = backgroundStartX; x < starBounds.X; ++x) {
                        var bgPixel = *pixelPtr;
                        int annIdx = surroundingPixelCount++;
                        surroundingPixels[annIdx] = bgPixel;
                        annDx[annIdx] = (float)(x - cx); annDy[annIdx] = (float)(y - cy); annVal[annIdx] = bgPixel;
                        ++pixelPtr;
                    }

                    // Skip the inner hole
                    pixelPtr += starBounds.Width;
                    for (int x = starBounds.Right; x < backgroundEndX; ++x) {
                        var bgPixel = *pixelPtr;
                        int annIdx = surroundingPixelCount++;
                        surroundingPixels[annIdx] = bgPixel;
                        annDx[annIdx] = (float)(x - cx); annDy[annIdx] = (float)(y - cy); annVal[annIdx] = bgPixel;
                        ++pixelPtr;
                    }

                    // Move to the start of the next row
                    pixelPtr += rowStrideGap;
                }

                // Bottom part of box
                for (int y = starBounds.Bottom; y < backgroundEndY; ++y) {
                    for (int x = backgroundStartX; x < backgroundEndX; ++x) {
                        var bgPixel = *pixelPtr;
                        int annIdx = surroundingPixelCount++;
                        surroundingPixels[annIdx] = bgPixel;
                        annDx[annIdx] = (float)(x - cx); annDy[annIdx] = (float)(y - cy); annVal[annIdx] = bgPixel;
                        ++pixelPtr;
                    }

                    // Move to the start of the next row
                    pixelPtr += rowStrideGap;
                }
            }

            // Count-bounded sort: the rented array may be larger than annulusCapacity; only [0, surroundingPixelCount) is valid.
            Array.Sort(surroundingPixels, 0, surroundingPixelCount);
            var backgroundMedian = surroundingPixels[surroundingPixelCount >> 1];

            // Robust local background scatter from the annulus pixels themselves, used as the noise scale for
            // the contamination test. The global noiseSigma is the image-noise floor; near bright stars and in
            // gradients/nebulosity the real local roughness is several times larger, so feeding the global value
            // to the standard-error model makes the bar far too tight. Fall back to the global sigma when the
            // sample is too small or perfectly flat (helper returns 0).
            var localBackgroundSigma = ComputeLocalBackgroundSigma(surroundingPixels, surroundingPixelCount, backgroundMedian);
            var contaminationSigma = localBackgroundSigma > 0.0 ? localBackgroundSigma : noiseSigma;

            // Fit the robust local background plane and run the gradient-robust contamination test in one pass.
            var gr = ComputeGradientContamination(annDx, annDy, annVal, surroundingPixelCount,
                contaminationSigma, p.ContaminationSensitivity, MinSectorPixels, fillSectors: p.CollectContaminationDiagnostics);
            var contaminationSuspected = gr.Suspected;

            // Local background model: the fitted plane when usable, else a flat plane at the annulus median.
            // Used as the per-pixel background for clipping, flux, centroid, HFR and PSF so a one-sided gradient
            // (galaxy/nebula) does not bias any measurement; for flat fields the plane equals the median.
            var backgroundPlane = gr.PlaneValid
                ? new LocalBackgroundPlane(cx, cy, gr.B0, gr.B1, gr.B2, isFlat: false)
                : LocalBackgroundPlane.Flat(cx, cy, backgroundMedian);

            // Donut-aware clip: cap the per-pixel clip for EXTENDED candidates so an aggressive
            // StarClippingMultiplier cannot strip a thin defocused ring to its brightest arc (which starves the
            // surviving-pixel count → Degenerate, and biases the centroid off-center → NotCentered). Verbatim
            // StarClippingMultiplier when the master is off or the candidate is small ⇒ bit-identical. See
            // EffectiveClipMultiplier / DonutClipMultiplierCap.
            var clipMargin = EffectiveClipMultiplier(p, Math.Max(starBounds.Width, starBounds.Height)) * noiseSigma;
            double totalFlux = 0d, peak = 0d;
            int numUnclippedPixels = 0;
            unsafe {
                var imageData = (float*)srcImage.DataPointer;
                float minPixel = 1.0f, maxPixel = 0.0f;
                foreach (var starPoint in starPoints) {
                    var raw = imageData[starPoint.Y * srcImage.Width + starPoint.X];
                    var background = backgroundPlane.ValueAt(starPoint.X, starPoint.Y);
                    if (raw <= background + clipMargin) {
                        continue;
                    }

                    var pixel = (float)(raw - background);

                    ++numUnclippedPixels;
                    if (pixel < minPixel) minPixel = pixel;
                    if (pixel > maxPixel) maxPixel = pixel;
                }

                if (numUnclippedPixels == 1 || maxPixel <= minPixel) {
                    // Degenerate case where surrounding pixels are similar to those within the structure. This seems to happen more often near the border
                    return null;
                }

                // Rent starPixels after the counting pass so the finally guard (null-check) correctly
                // skips returning it on the early-return path above.
                starPixels = ArrayPool<double>.Shared.Rent(numUnclippedPixels);
                int pixelCount = 0;
                foreach (var starPoint in starPoints) {
                    var raw = imageData[starPoint.Y * srcImage.Width + starPoint.X];
                    var background = backgroundPlane.ValueAt(starPoint.X, starPoint.Y);
                    if (raw <= background + clipMargin) {
                        continue;
                    }

                    var pixel = raw - background;
                    totalFlux += pixel;
                    peak = pixel > peak ? pixel : peak;
                    starPixels[pixelCount++] = pixel;
                }
            }

            // Count-bounded sort: the rented array may be larger than numUnclippedPixels; only [0, numUnclippedPixels) is valid.
            Array.Sort(starPixels, 0, numUnclippedPixels);
            var starMedian = ComputeMedian(starPixels, numUnclippedPixels);

            // meanFlux is the mean over clip-survivors (the pixels actually summed into totalFlux), NOT the full
            // structure footprint. Dividing by starPoints.Count understated it and inflated NormalizedBrightness
            // for faint/spread stars (low survivor fraction), loosening the sensitivity gate (F11).
            var meanFlux = totalFlux / numUnclippedPixels;

            // Compute an iterative centroid: start with all threshold-clipped pixels, then on
            // subsequent passes restrict to pixels within a circular aperture centered on the
            // previous estimate.  2-3 passes are enough for sub-pixel convergence.
            var apertureRadius = Math.Min(starBounds.Width, starBounds.Height) / 2.0;
            Point2d center;
            unsafe {
                center = ComputeIterativeCentroid(
                    imageData: (float*)srcImage.DataPointer,
                    imageWidth: srcImage.Width,
                    starPoints: starPoints,
                    backgroundPlane: backgroundPlane,
                    clipMargin: clipMargin,
                    apertureRadius: apertureRadius,
                    numPasses: 3);
            }
            var backgroundAtCenter = backgroundPlane.ValueAt(center.X, center.Y);
            var centerBrightness = CvImageUtility.BilinearSamplePixelValue(srcImage, y: center.Y, x: center.X) - backgroundAtCenter;

            ContaminationDiagnosticRecord contaminationDiagnostics = null;
            if (p.CollectContaminationDiagnostics) {
                contaminationDiagnostics = new ContaminationDiagnosticRecord() {
                    NoiseSigma = contaminationSigma,
                    Sensitivity = p.ContaminationSensitivity,
                    MinSectorPixels = MinSectorPixels,
                    ContaminationSuspected = contaminationSuspected,
                    GradientSlope = gr.GradientSlope,
                    LocalSigmaResidual = gr.LocalSigmaResidual,
                    SectorResidualMedian = gr.SectorResidualMedian,
                    SectorResidualCount = gr.SectorResidualCount,
                    MaxSectorResidualOverSE = gr.MaxSectorResidualOverSE,
                    ResidualTrippingSector = gr.TrippingSector
                    // CenterX/CenterY/Hfr/Background are filled later in CreateStar.
                };
            }

            return new StarCandidate() {
                Center = center,
                CenterBrightness = (float)centerBrightness,
                Background = backgroundAtCenter,
                BackgroundPlane = backgroundPlane,
                TotalFlux = (float)totalFlux,
                Peak = (float)peak,
                // Detection level for the star's brightness corrected for the peak response
                NormalizedBrightness = (float)(peak - (1 - p.PeakResponse) * meanFlux),
                StarBoundingBox = starBounds,
                StarMedian = starMedian,
                PixelCount = starPoints.Count,
                UnclippedPixelCount = numUnclippedPixels,
                ContaminationSuspected = contaminationSuspected,
                ContaminationDiagnostics = contaminationDiagnostics
            };
            } finally {
                // Return all rented arrays exactly once on every exit path (normal return, early null return,
                // or exception). starPixels is null when the early-return path (degenerate case) fires before
                // the counting pass allocates it, so guard it before returning.
                ArrayPool<float>.Shared.Return(surroundingPixels);
                ArrayPool<float>.Shared.Return(annDx);
                ArrayPool<float>.Shared.Return(annDy);
                ArrayPool<float>.Shared.Return(annVal);
                if (starPixels != null) {
                    ArrayPool<double>.Shared.Return(starPixels);
                }
            }
        }
    }
}
