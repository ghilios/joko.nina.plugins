#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// Turns a loaded <see cref="IImageData"/> into the image a HocusFocus detection consumes, mirroring
    /// <c>ImageControlVM.PrepareImage</c>'s debayer decision — WITHOUT the mediator (and therefore without the
    /// stretch, which detection never reads). The single source of truth for that decision: the in-NINA
    /// optimization wizard's Review step and every TestApp harness route through here, so neither can drift from
    /// the live app or from each other.
    ///
    /// <para><b>Nothing here CFA hotpixel-filters.</b> The live app applies the CFA filter (and its own debayer)
    /// INSIDE <c>StarDetector.PrepareSrcImageFromRenderedImage</c>, gated on the CALLER'S
    /// <c>HotpixelFiltering &amp;&amp; HotpixelThresholdingEnabled</c> and using the caller's
    /// <c>HotpixelThreshold</c>. The representation a detection runs on is therefore params-dependent, and
    /// filtering at load time would freeze two searched optimizer axes into silent no-ops.</para>
    /// </summary>
    public static class RenderedImageLoading {

        /// <summary>
        /// Renders <paramref name="imageData"/> and debayers it exactly when the live app would: the frame is
        /// bayered AND the profile's <c>ImageSettings.DebayerImage</c> is on
        /// (<c>ImageControlVM.PrepareImage</c>). Mono frames — and a profile with debayering off — return the
        /// plain rendered image, whose detection input is <c>ToOpenCVMat(RawImageData)</c>: byte-identical to
        /// loading the raw frame as a Mat.
        ///
        /// <para><c>saveColorChannels</c> and <c>saveLumChannel</c> are both false. Live computes
        /// <c>saveLumChannel = ImageSettings.DebayeredHFR &amp;&amp; detectStars</c> and every HocusFocus caller
        /// passes <c>detectStars: false</c>, so live <c>SaveLumChannel</c> is always false and
        /// <c>DebayeredData</c> is always null. Passing true would flip <c>CvImageUtility.ToOpenCVMat</c>'s guard
        /// and make the hotpixel-filtering-OFF branch read luminance where live reads the mosaic.
        /// <c>saveColorChannels</c> is live's <c>UnlinkedStretch</c>, which only affects the displayed stretch —
        /// detection never reads the color channels — so false here just avoids allocating three more planes.</para>
        /// </summary>
        /// <param name="cameraSensorType">The CONNECTED camera's reported sensor type, live's last-resort CFA
        /// source. Pass null where there is no camera (headless): resolution then fails loudly instead of
        /// silently debayering at a guessed phase.</param>
        public static IRenderedImage ForDetection(IImageData imageData, IProfileService profileService, SensorType? cameraSensorType = null) {
            if (imageData == null) {
                throw new ArgumentNullException(nameof(imageData));
            }
            if (profileService == null) {
                throw new ArgumentNullException(nameof(profileService));
            }
            var rendered = imageData.RenderImage();
            if (!imageData.Properties.IsBayered || !profileService.ActiveProfile.ImageSettings.DebayerImage) {
                return rendered;
            }
            return rendered.Debayer(saveColorChannels: false, saveLumChannel: false,
                bayerPattern: ResolveBayerPattern(imageData, profileService, cameraSensorType));
        }

        /// <summary>
        /// Resolves the CFA pattern with <c>ImageControlVM.PrepareImage</c>'s precedence: the profile's
        /// <c>CameraSettings.BayerPattern</c> when it is not <c>Auto</c>, else the frame's own
        /// <c>MetaData.Camera.SensorType</c> when it names a real CFA (Mono/Color are placeholder categories, not
        /// patterns), else the connected camera's reported sensor type.
        ///
        /// <para>With no camera to fall back on, this throws rather than substituting RGGB: a wrong phase
        /// debayers the frame incorrectly and quietly biases every number downstream. Live is no more permissive
        /// — it hands <c>cameraInfo.SensorType</c> (Monochrome when disconnected) straight to
        /// <c>ImageUtility.Debayer</c>, which throws <c>InvalidImagePropertiesException</c> on it.</para>
        /// </summary>
        public static SensorType ResolveBayerPattern(IImageData imageData, IProfileService profileService, SensorType? cameraSensorType = null) {
            var profilePattern = profileService.ActiveProfile.CameraSettings.BayerPattern;
            if (profilePattern != BayerPatternEnum.Auto) {
                return (SensorType)profilePattern;
            }
            var metadataSensorType = imageData.MetaData?.Camera?.SensorType;
            if (IsRealCfaPattern(metadataSensorType)) {
                return metadataSensorType.Value;
            }
            if (IsRealCfaPattern(cameraSensorType)) {
                return cameraSensorType.Value;
            }
            throw new InvalidOperationException(
                $"Cannot resolve the CFA pattern for a bayered frame: the profile's Camera > Bayer pattern is 'Auto', the file's " +
                $"BAYERPAT/SensorType metadata is '{metadataSensorType?.ToString() ?? "absent"}', and the camera reports " +
                $"'{cameraSensorType?.ToString() ?? "nothing (disconnected)"}'. Set the profile's Bayer pattern explicitly.");
        }

        private static bool IsRealCfaPattern(SensorType? sensorType) =>
            sensorType.HasValue && sensorType.Value != SensorType.Monochrome && sensorType.Value != SensorType.Color;

        /// <summary>
        /// A CV_32F [0,1] Mat for DISPLAY or for an analysis that must stay INDEPENDENT of the detector: a bayered
        /// image is debayered to luminance (the same <c>ImageUtility.Debayer</c> conversion the detector's OSC path
        /// performs, minus the CFA hotpixel filter); anything else is <c>ToOpenCVMat</c>, i.e. the raw frame.
        ///
        /// <para><b>Never a detection input.</b> Detection must go through <c>Detect(IRenderedImage, …)</c> so the
        /// CFA filter runs at the caller's params. The omission of that filter is deliberate here: the surfaces
        /// this feeds are the ones a human or an LLM looks at (a mosaic renders as a visible checkerboard) plus
        /// the linear export behind the detector-INDEPENDENT golden reference, whose value lies in having
        /// different blind spots from the detector.</para>
        /// </summary>
        public static Mat ToDebayeredLuminanceMat(IRenderedImage image) {
            if (image is IDebayeredImage debayered && !debayered.SaveLumChannel) {
                var props = image.RawImageData.Properties;
                var bitmapSource = ImageUtility.CreateSourceFromArray(image.RawImageData.Data, props, PixelFormats.Gray16);
                var lum = ImageUtility.Debayer(bitmapSource, pf: System.Drawing.Imaging.PixelFormat.Format16bppGrayScale,
                    saveColorChannels: false, saveLumChannel: true, bayerPattern: debayered.BayerPattern);
                return CvImageUtility.ToOpenCVMat(lum.Data.Lum, bpp: props.BitDepth, width: props.Width, height: props.Height);
            }
            return CvImageUtility.ToOpenCVMat(image);
        }
    }
}
