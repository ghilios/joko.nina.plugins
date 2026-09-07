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
using OpenCvSharp;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// Outputs of the EARLY pipeline span (BuildDetectionContextInternal steps 1-5b: hotpixel filter,
    /// noise-reduction Gaussians, K-σ noise estimates, à-trous wavelet residual subtract, post-wavelet
    /// Gaussian, binarization statistics, adaptive-binarization grids). Everything downstream — dilation,
    /// binarize, inner-crop, donut morph-close, candidate collection, the LATE stage — consumes these and
    /// stays on the CPU.
    /// </summary>
    internal sealed class EarlySpanOutput {

        /// <summary>Structure map after PostWaveletConvolution (caller/StarDetector takes ownership).</summary>
        public Mat StructureMap { get; set; }

        /// <summary>
        /// The prepared measurement image when the span mutated it (hotpixel filter and/or measurement
        /// noise reduction applied); null when the input Mat is already the measurement image. When
        /// non-null, StarDetector swaps it in for srcImage and disposes the input.
        /// </summary>
        public Mat MeasurementImage { get; set; }

        public CvImageUtility.KappaSigmaNoiseEstimateResult StructureNoise { get; set; }
        public CvImageUtility.KappaSigmaNoiseEstimateResult MeasurementNoise { get; set; }
        public CvImageUtility.LocalBackgroundGrid SigmaGrid { get; set; }
        public CvImageUtility.LocalBackgroundGrid AdaptiveMedianGrid { get; set; }
        public double StructureMapMedian { get; set; }
        public long HotpixelCount { get; set; }
    }

    /// <summary>
    /// Optional accelerator for the EARLY pipeline span, consulted by StarDetector only when the params
    /// bundle carries <c>AllowGpuAcceleration</c> (production: the process-global
    /// <c>Gpu.GpuAccelerationHost</c>; tests: the <c>StarDetector.EarlyAcceleratorOverride</c> seam).
    /// A null accelerator or a false return runs the unchanged CPU span.
    /// </summary>
    internal interface IEarlyPipelineAccelerator {

        /// <summary>
        /// Attempts to run the EARLY span for <paramref name="srcImage"/> (CV_32F, post-ROI/post-binning;
        /// treated as read-only). Returns false to decline (unsupported input, device failure, ...), in
        /// which case the caller runs the existing CPU span with no behavior change.
        /// <paramref name="token"/> is honored while queued for a device working set; a device build already
        /// in flight is not interrupted.
        /// </summary>
        bool TryRunEarlySpan(Mat srcImage, StarDetectorParams p, int effectiveStructureLayers, bool hotpixelAlreadyApplied, CancellationToken token, out EarlySpanOutput output);
    }
}
