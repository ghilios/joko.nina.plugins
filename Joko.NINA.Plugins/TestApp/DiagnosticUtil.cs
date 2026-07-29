#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.FileFormat.FITS;
using NINA.Image.FileFormat.XISF;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp {

    /// <summary>Helpers shared by the headless diagnostic runners (contamination, focus-sweep).</summary>
    internal static class DiagnosticUtil {

        /// <summary>Returns the value following <paramref name="name"/> in <paramref name="args"/>, or null.</summary>
        public static string GetArg(string[] args, string name) {
            for (int i = 0; i < args.Length - 1; ++i) {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) {
                    return args[i + 1];
                }
            }
            return null;
        }

        /// <summary>True when the boolean flag <paramref name="name"/> appears anywhere in <paramref name="args"/>.</summary>
        public static bool HasFlag(string[] args, string name) {
            for (int i = 0; i < args.Length; ++i) {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        private static readonly string[] NinaLoaderExtensions = { ".xisf", ".fits", ".fit" };

        /// <summary>
        /// True when <paramref name="path"/> is an image NINA's XISF/FITS loaders read (and therefore one the
        /// rendered-image path in <see cref="LoadRenderedImage"/> supports). <c>.tif</c> is not: it has no CFA, no
        /// header metadata, and NINA's TIFF decoder normalizes differently, so it stays on the Mat route.
        /// </summary>
        public static bool IsNinaLoaderFormat(string path) {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return Array.IndexOf(NinaLoaderExtensions, ext) >= 0;
        }

        /// <summary>
        /// True when the saved-frame filename carries <c>_Bayered1_</c>. This is exactly what the live app keys off
        /// (<c>AutoFocusEngine.LoadSavedAutoFocusAttempt</c> parses the flag out of the filename and passes it to the
        /// image-data factory), parsed here through the SAME <see cref="SavedAutoFocusImage.TryParseFileName"/> the
        /// engine uses; it only sets <c>ImageProperties.IsBayered</c> — pixel data is untouched either way.
        /// Files that do not match the AF sweep pattern report false, which is the pre-existing behavior for the
        /// arbitrary single images the contamination/annotate tools accept.
        /// </summary>
        public static bool IsBayeredFrameFileName(string path) => SavedAutoFocusImage.TryParseFileName(path)?.IsBayered == true;

        /// <summary>
        /// Loads an image file as a CV_32F Mat normalized to [0,1] — the RAW frame, exactly as stored. .tif/.tiff are
        /// read directly; .xisf/.fits/.fit go through NINA's loaders (which need a profile). profileService may be
        /// null for .tif-only callers.
        ///
        /// <para><b>Not a detection input for a NINA-loader format.</b> For a bayered frame this returns the Bayer
        /// MOSAIC, which is not what the live app feeds star detection. Detecting runners use
        /// <see cref="LoadRenderedImage"/> + <c>StarDetector.Detect(IRenderedImage, …)</c> so the CFA hotpixel filter
        /// and the debayer happen inside <c>Detect</c> at the caller's params exactly as live; the surfaces a human
        /// or the golden reference reads use <see cref="LoadDebayeredFloatMat"/>. What is left for this method is the
        /// <c>.tif</c> carve-out (no CFA, and NINA's TIFF decoder normalizes differently), which is why
        /// <c>DiagnosticUtil</c> is its only caller — <c>HeadlessDetectionParityGuardTests</c> enforces that.</para>
        /// </summary>
        public static async Task<Mat> LoadFloatMat(string path, IProfileService profileService) {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff") {
                using var src = new Mat(path, ImreadModes.Unchanged);
                var dst = new Mat();
                Program.ConvertToFloat(src, dst);
                return dst;
            }
            if (IsNinaLoaderFormat(path)) {
                var imageData = await LoadImageDataAsync(path, profileService, isBayered: false).ConfigureAwait(false);
                return CvImageUtility.ToOpenCVMat(imageData);
            }
            throw new NotSupportedException($"Unsupported image extension '{ext}'. Supported: .tif/.tiff, .xisf, .fits/.fit");
        }

        /// <summary>
        /// Loads a saved frame the SAME way the live app does and returns the <see cref="IRenderedImage"/> the star
        /// detector consumes, so headless detection is predictive of in-app behaviour:
        /// <list type="number">
        /// <item>NINA's XISF/FITS loader with the frame's REAL <c>isBayered</c> flag (the <c>_BayeredN_</c> filename
        ///   token — the same source the live loader uses), rather than a hard-coded false.</item>
        /// <item><c>RenderImage().Debayer(saveColorChannels: false, saveLumChannel: false, bayerPattern: resolved)</c>
        ///   when the frame is bayered AND <c>ImageSettings.DebayerImage</c> is on, mirroring
        ///   <c>ImageControlVM.PrepareImage</c>.</item>
        /// </list>
        ///
        /// <para><b><c>saveLumChannel</c> must stay false.</b> Live computes it as
        /// <c>ImageSettings.DebayeredHFR &amp;&amp; detectStars</c> and EVERY HocusFocus caller passes
        /// <c>detectStars: false</c>, so live <c>SaveLumChannel</c> is always false and <c>DebayeredData</c> is always
        /// null. Passing true here would flip <c>CvImageUtility.ToOpenCVMat</c>'s guard and make the
        /// hotpixel-filtering-OFF branch read luminance where live reads the mosaic.</para>
        ///
        /// <para>Nothing is CFA-filtered here on purpose: <c>StarDetector.PrepareSrcImageFromRenderedImage</c> gates
        /// the CFA hotpixel filter (and its own debayer) on <c>p.HotpixelFiltering &amp;&amp;
        /// p.HotpixelThresholdingEnabled</c> at the CALLER'S params. Filtering at load time would freeze those two
        /// searched optimizer axes into no-ops.</para>
        ///
        /// <para>Mono frames are byte-identical to the legacy Mat route by construction: a non-bayered frame is not
        /// an <c>IDebayeredImage</c>, so detection falls through to <c>ToOpenCVMat(image.RawImageData)</c> — the same
        /// call <see cref="LoadFloatMat"/> makes.</para>
        /// </summary>
        public static async Task<IRenderedImage> LoadRenderedImage(string path, IProfileService profileService) {
            if (!IsNinaLoaderFormat(path)) {
                throw new NotSupportedException(
                    $"'{Path.GetExtension(path)}' has no NINA loader and no CFA; use LoadFloatMat for it. Supported: .xisf, .fits/.fit");
            }
            var imageData = await LoadImageDataAsync(path, profileService, isBayered: IsBayeredFrameFileName(path)).ConfigureAwait(false);
            // The plugin's own seam (the in-NINA wizard's Review step loads through the same call), so the harness
            // mirrors nothing: the debayer decision, the CFA-pattern precedence, and the fail-loud on an
            // unresolvable pattern all live in one place.
            return RenderedImageLoading.ForDetection(imageData, profileService);
        }

        /// <summary>
        /// The DISPLAY/detector-INDEPENDENT counterpart of <see cref="LoadRenderedImage"/>: a CV_32F [0,1] Mat that
        /// is debayered to luminance for a bayered frame (so a mosaic does not render as a visible checkerboard) but
        /// is NEVER CFA hotpixel filtered. Used by the surfaces a human or an LLM looks at (golden tiles, annotated
        /// overlays), by the annotated-frame backgrounds, and by the linear export feeding the golden reference —
        /// none of which detect. Mono frames and .tif/.tiff take exactly the same route as
        /// <see cref="LoadFloatMat"/>, so they are byte-identical to today.
        /// </summary>
        public static async Task<Mat> LoadDebayeredFloatMat(string path, IProfileService profileService) {
            if (!IsNinaLoaderFormat(path)) {
                return await LoadFloatMat(path, profileService).ConfigureAwait(false);
            }
            // Straight off the image data rather than via LoadRenderedImage: this path wants only the luminance,
            // and going through the detection product would build (then discard) the Rgb48 debayered source.
            var imageData = await LoadImageDataAsync(path, profileService, isBayered: IsBayeredFrameFileName(path)).ConfigureAwait(false);
            return RenderedImageLoading.ToDebayeredLuminanceMat(imageData, profileService);
        }

        private static async Task<IImageData> LoadImageDataAsync(string path, IProfileService profileService, bool isBayered) {
            if (profileService == null) {
                throw new InvalidOperationException(
                    $"Loading '{Path.GetExtension(path)}' requires a NINA profile; omit --default-params or use .tif frames.");
            }
            var factory = new ImageDataFactory(profileService, new StubBehaviorSelector<IStarDetection>(new StubStarDetection()), new StubBehaviorSelector<IStarAnnotator>());
            var uri = new Uri(Path.GetFullPath(path));
            // isBayered only sets ImageProperties.IsBayered; the pixel data is identical either way.
            return Path.GetExtension(path).ToLowerInvariant() == ".xisf"
                ? await XISF.Load(uri, isBayered, factory, CancellationToken.None).ConfigureAwait(false)
                : await FITS.Load(uri, isBayered, factory, CancellationToken.None).ConfigureAwait(false);
        }

    }

    /// <summary>
    /// One loaded frame that can be detected on repeatedly, holding the .tif carve-out in a SINGLE place: a
    /// <c>.xisf</c>/<c>.fits</c> frame is carried as the <see cref="IRenderedImage"/> the live app detects on (so
    /// the CFA hotpixel filter and the debayer happen inside <c>Detect</c> at the caller's params), while a
    /// <c>.tif</c> — which has no CFA, no header metadata, and whose NINA decoder normalizes by <c>1&lt;&lt;16</c>
    /// rather than <c>ushort.MaxValue</c> — stays on the legacy Mat route. Used by the tools that accept both
    /// (<c>contamination</c>, and <c>focus-sweep</c>'s synthesized .tif runs).
    /// </summary>
    internal sealed class DetectionSource : IDisposable {
        private readonly IRenderedImage rendered;   // null for .tif
        private readonly Mat mat;                   // null unless .tif

        private DetectionSource(IRenderedImage rendered, Mat mat, int width, int height) {
            this.rendered = rendered;
            this.mat = mat;
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>True when this frame is carried as an <see cref="IRenderedImage"/> (the live-parity route).</summary>
        public bool IsRendered => rendered != null;

        public static async Task<DetectionSource> LoadAsync(string path, IProfileService profileService) {
            if (DiagnosticUtil.IsNinaLoaderFormat(path)) {
                var image = await DiagnosticUtil.LoadRenderedImage(path, profileService).ConfigureAwait(false);
                var props = image.RawImageData.Properties;
                return new DetectionSource(image, null, props.Width, props.Height);
            }
            var loaded = await DiagnosticUtil.LoadFloatMat(path, profileService).ConfigureAwait(false);
            return new DetectionSource(null, loaded, loaded.Width, loaded.Height);
        }

        /// <summary>
        /// Detects this frame at <paramref name="p"/>. The rendered route needs no clone (Detect builds its own
        /// source Mat per call); the .tif route clones because <c>Detect(Mat, …)</c> mutates its input in place.
        /// </summary>
        public async Task<HocusFocusStarDetectorResult> DetectAsync(StarDetector detector, StarDetectorParams p, CancellationToken token) {
            if (rendered != null) {
                return await detector.Detect(rendered, p, null, token).ConfigureAwait(false);
            }
            using var clone = mat.Clone();
            return await detector.Detect(clone, p, null, token).ConfigureAwait(false);
        }

        /// <summary>A fresh CV_32F [0,1] Mat for rendering/annotation backgrounds. The caller owns it.</summary>
        public Mat CreateDisplayMat() => rendered != null ? RenderedImageLoading.ToDebayeredLuminanceMat(rendered) : mat.Clone();

        public void Dispose() => mat?.Dispose();
    }
}
