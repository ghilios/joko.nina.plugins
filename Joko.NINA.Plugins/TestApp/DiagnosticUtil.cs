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

        /// <summary>
        /// Loads an image file as a CV_32F Mat normalized to [0,1]. .tif/.tiff are read directly; .xisf/.fits/.fit
        /// go through NINA's loaders (which need a profile). profileService may be null for .tif-only callers.
        /// </summary>
        public static async Task<Mat> LoadFloatMat(string path, IProfileService profileService) {
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
                IImageData imageData = ext == ".xisf"
                    ? await XISF.Load(uri, false, factory, CancellationToken.None)
                    : await FITS.Load(uri, false, factory, CancellationToken.None);
                return CvImageUtility.ToOpenCVMat(imageData);
            }
            throw new NotSupportedException($"Unsupported image extension '{ext}'. Supported: .tif/.tiff, .xisf, .fits/.fit");
        }
    }
}
