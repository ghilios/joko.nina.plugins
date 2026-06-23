#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public class HocusFocusDetectionParams {
        public double HighSigmaOutlierRejection { get; set; } = 4.0d;
        public double LowSigmaOutlierRejection { get; set; } = 3.0d;
        public List<Accord.Point> MatchStarPositions { get; set; } = null;
        public int NumberOfAFStars { get; set; } = 0;
        public bool IsAutoFocus { get; set; } = false;
    }

    public interface IHocusFocusStarDetection : IStarDetection {

        HocusFocusDetectionParams ToHocusFocusParams(StarDetectionParams p);

        /// <summary>
        /// Same as <see cref="IStarDetection.Detect(IRenderedImage, PixelFormat, StarDetectionParams, IProgress{ApplicationStatus}, CancellationToken)"/>,
        /// but when <paramref name="modelPSFForAutoFocus"/> is true the auto-focus PSF-off override is lifted so PSFs
        /// are modeled (per the star-detection options) during an auto-focus run — used by the manual AF "Review Frames"
        /// feature so the review can show PSF-derived per-star properties. Detection is otherwise identical, so the AF
        /// HFR curve is unaffected.
        /// </summary>
        Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token, bool modelPSFForAutoFocus);

        StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus);

        /// <summary>
        /// Like <see cref="GetStarDetectorParams(IRenderedImage, StarDetectionRegion, bool)"/>, but builds the
        /// option-derived params from <paramref name="optionsOverride"/> instead of the detector's injected options
        /// (and never touches them). Used by auto-focus replay to reproduce a saved run's capture-time detection
        /// settings without mutating the live profile.
        /// </summary>
        StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus, IStarDetectionOptions optionsOverride);

        /// <summary>The detector's injected star-detection options (read-only). Used to snapshot the settings in
        /// effect when an auto-focus run is captured.</summary>
        IStarDetectionOptions StarDetectionOptions { get; }

        /// <summary>The Optimization Wizard's seed: fully-default detector params with the same image-context +
        /// auto-focus overrides as <see cref="GetStarDetectorParams"/>. Read-only with respect to options.</summary>
        StarDetectorParams GetDefaultStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus);

        Task<StarDetectionResult> Detect(IRenderedImage image, HocusFocusDetectionParams hocusFocusParams, StarDetectorParams detectorParams, IProgress<ApplicationStatus> progress, CancellationToken token);

        /// <summary>
        /// EARLY phase of <see cref="Detect(IRenderedImage, HocusFocusDetectionParams, StarDetectorParams, IProgress{ApplicationStatus}, CancellationToken)"/>:
        /// converts the image + builds the reusable early-stage detection context. Pair with
        /// <see cref="GateAndMeasure"/>. Lets the optimizer cache the expensive early stage across candidate
        /// evaluations that change only late-stage params. The returned context owns native resources; dispose it.
        /// </summary>
        Task<HocusFocusDetectionContext> BuildDetectionContext(IRenderedImage image, HocusFocusDetectionParams hocusFocusParams, StarDetectorParams detectorParams, IProgress<ApplicationStatus> progress, CancellationToken token);

        /// <summary>
        /// LATE phase: gates + measures a context built by <see cref="BuildDetectionContext"/> and runs the same
        /// post-processing (HFR aggregation, etc.) as the monolithic Detect, producing an identical result.
        /// </summary>
        StarDetectionResult GateAndMeasure(HocusFocusDetectionContext context, StarDetectorParams detectorParams, CancellationToken token);

        /// <summary>Early cache key over only the early-affecting detection params.</summary>
        string ComputeEarlyCacheKey(StarDetectorParams detectorParams);
    }

    /// <summary>
    /// Opaque, disposable carrier for the early-stage detection of one image: the raw-detector early context plus
    /// the header data the late stage needs to assemble the final <c>StarDetectionResult</c>. Built by
    /// <see cref="IHocusFocusStarDetection.BuildDetectionContext"/>, consumed by
    /// <see cref="IHocusFocusStarDetection.GateAndMeasure"/>, and reused across late-only candidate changes.
    /// </summary>
    public sealed class HocusFocusDetectionContext : IDisposable {
        public StarDetection.StarDetector.DetectionContext DetectorContext { get; set; }
        public HocusFocusDetectionParams HocusFocusParams { get; set; }
        public System.Drawing.Size ImageSize { get; set; }
        public double PixelSize { get; set; }
        public int FocuserPosition { get; set; }

        public void Dispose() {
            DetectorContext?.Dispose();
            DetectorContext = null;
        }
    }
}