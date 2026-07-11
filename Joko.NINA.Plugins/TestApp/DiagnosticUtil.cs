#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Image.FileFormat.FITS;
using NINA.Image.FileFormat.XISF;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

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

        /// <summary>
        /// Loads an image file as a CV_32F Mat normalized to [0,1]. .tif/.tiff are read directly; .xisf/.fits/.fit
        /// go through NINA's loaders (which need a profile). profileService may be null for .tif-only callers.
        ///
        /// When <paramref name="debayerToLuminance"/> is true and the loaded frame is bayered, it is debayered to a
        /// luminance Mat — the SAME representation the live app feeds star detection when ImageSettings.DebayerImage
        /// is on (see <c>StarDetector.PrepareSrcImageFromRenderedImage</c>). Headless runners otherwise detect on the
        /// raw Bayer mosaic, which biases the per-star sensor-model tilt the Tilt Adapter Wizard calibrates from.
        /// </summary>
        public static async Task<Mat> LoadFloatMat(string path, IProfileService profileService, bool debayerToLuminance = false, bool applyCfaHotpixel = false, double hotpixelThreshold = 0.001) {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff") {
                using var src = new Mat(path, ImreadModes.Unchanged);
                var dst = new Mat();
                Program.ConvertToFloat(src, dst);
                return dst;
            }
            if (ext == ".xisf" || ext == ".fits" || ext == ".fit") {
                if (profileService == null) {
                    throw new InvalidOperationException($"Loading '{ext}' requires a NINA profile; omit --default-params or use .tif frames.");
                }
                var factory = new ImageDataFactory(profileService, new StubBehaviorSelector<IStarDetection>(new StubStarDetection()), new StubBehaviorSelector<IStarAnnotator>());
                var uri = new Uri(Path.GetFullPath(path));
                // The second arg is isBayered. Loading with false (the raw-mosaic default the other runners use)
                // clears IsBayered and drops the CFA pattern, which would silently skip the debayer path below.
                // When the caller wants luminance, load AS bayered so the CFA pattern is populated for the debayer.
                IImageData imageData = ext == ".xisf"
                    ? await XISF.Load(uri, debayerToLuminance, factory, CancellationToken.None)
                    : await FITS.Load(uri, debayerToLuminance, factory, CancellationToken.None);
                if (debayerToLuminance && imageData.Properties.IsBayered) {
                    return DebayerToLuminanceMat(imageData, applyCfaHotpixel, hotpixelThreshold);
                }
                return CvImageUtility.ToOpenCVMat(imageData);
            }
            throw new NotSupportedException($"Unsupported image extension '{ext}'. Supported: .tif/.tiff, .xisf, .fits/.fit");
        }

        /// <summary>
        /// Debayers a bayered <see cref="IImageData"/> to a luminance Mat, mirroring the detector's own
        /// <c>StarDetector.PrepareSrcImageFromRenderedImage</c> conversion (CFA → 16-bit luminance) so the headless
        /// representation matches the live app. The concrete CFA pattern comes from the frame's own metadata (the
        /// FITS BAYERPAT), defaulting to RGGB when the metadata reports none but the properties say bayered.
        /// </summary>
        private static Mat DebayerToLuminanceMat(IImageData imageData, bool applyCfaHotpixel = false, double hotpixelThreshold = 0.001) {
            var props = imageData.Properties;
            var sensorType = imageData.MetaData?.Camera?.SensorType ?? SensorType.RGGB;
            if (sensorType == SensorType.Monochrome) {
                sensorType = SensorType.RGGB;
            }
            var dataArray = imageData.Data;
            if (applyCfaHotpixel) {
                // Mirror the live StarDetector.PrepareSrcImageFromRenderedImage OSC path: CFA hotpixel filter on the
                // raw mosaic BEFORE debayer, so noise is rejected exactly as the live AF detector does.
                var copy = new ushort[imageData.Data.FlatArray.Length];
                Buffer.BlockCopy(imageData.Data.FlatArray, 0, copy, 0, copy.Length * sizeof(ushort));
                var raw = new RawImageData(copy, width: props.Width, height: props.Height);
                var threshold = (ushort)(hotpixelThreshold * (1 << props.BitDepth));
                HotpixelFiltering.CFAHotpixelFilter(raw, sensorType, threshold);
                dataArray = new ImageArray(copy);
            }
            var bitmapSource = ImageUtility.CreateSourceFromArray(dataArray, props, PixelFormats.Gray16);
            var debayered = ImageUtility.Debayer(bitmapSource, pf: System.Drawing.Imaging.PixelFormat.Format16bppGrayScale,
                saveColorChannels: false, saveLumChannel: true, bayerPattern: sensorType);
            return CvImageUtility.ToOpenCVMat(debayered.Data.Lum, bpp: props.BitDepth, width: props.Width, height: props.Height);
        }
    }
}
