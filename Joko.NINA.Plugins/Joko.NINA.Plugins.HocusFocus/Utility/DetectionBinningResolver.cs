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
using System.Globalization;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// Turns the user's <see cref="DetectionBinningEnum"/> setting into the concrete integer factor star
    /// detection resamples by. This is the SINGLE source of truth for the Auto rule: the options hint, the
    /// detection path, the optimization wizard's recommendation and the documentation all cite it, so they
    /// cannot drift.
    ///
    /// <para>The rule: every pixel-unit detector knob (MinHFR, MinimumStarBoundingBoxSize, NoiseReductionRadius,
    /// StructureLayers, ...) is calibrated for an in-focus HFR near <see cref="TargetHfrPixels"/>. Estimate the
    /// in-focus HFR from the pixel scale under an assumed <see cref="AssumedFwhmArcsec"/> total FWHM (seeing +
    /// optics + guiding), then pick the integer factor that lands closest to the target.</para>
    /// </summary>
    public static class DetectionBinningResolver {

        /// <summary>The assumed total in-focus FWHM (seeing + optics + guiding) used to estimate star size from
        /// pixel scale alone. A stand-in for a typical night, not a measurement — the optimization wizard
        /// recommends from measured HFR when real sweep data is available.</summary>
        public const double AssumedFwhmArcsec = 3.0;

        /// <summary>The in-focus HFR (in binned pixels) Auto aims for: the center of the detector's calibrated
        /// 2-4 px band.</summary>
        public const double TargetHfrPixels = 3.0;

        /// <summary>The largest factor Auto will choose (and the largest the options UI offers).</summary>
        public const int MaxBinningFactor = 4;

        /// <summary>
        /// The in-focus HFR, in pixels, an <see cref="AssumedFwhmArcsec"/> star would have at
        /// <paramref name="pixelScaleArcsecPerPixel"/>. HFR is half the FWHM, which is exact for a Gaussian.
        /// NaN when the pixel scale is unusable.
        /// </summary>
        public static double EstimateInFocusHfrPixels(double pixelScaleArcsecPerPixel) {
            if (!IsUsablePixelScale(pixelScaleArcsecPerPixel)) {
                return double.NaN;
            }
            return AssumedFwhmArcsec / (2.0 * pixelScaleArcsecPerPixel);
        }

        /// <summary>
        /// The integer factor for <paramref name="setting"/>. An explicit setting passes straight through
        /// (clamped); <see cref="DetectionBinningEnum.Auto"/> derives it from
        /// <paramref name="pixelScaleArcsecPerPixel"/>, which is expected to ALREADY include any camera
        /// (hardware) binning — so Auto backs off on its own when the camera is already binning. An unusable
        /// pixel scale (focal length or pixel size unset) resolves to 1 rather than guessing.
        /// </summary>
        public static int Resolve(DetectionBinningEnum setting, double pixelScaleArcsecPerPixel) {
            if (setting != DetectionBinningEnum.Auto) {
                return Clamp((int)setting);
            }
            return RecommendFromHfr(EstimateInFocusHfrPixels(pixelScaleArcsecPerPixel));
        }

        /// <summary>
        /// The factor that brings <paramref name="unbinnedHfrPixels"/> closest to <see cref="TargetHfrPixels"/>.
        /// Shared by the pixel-scale estimate above and the optimization wizard's measured recommendation, so
        /// both answer to the same target. NaN or non-positive input resolves to 1.
        /// </summary>
        public static int RecommendFromHfr(double unbinnedHfrPixels) {
            if (double.IsNaN(unbinnedHfrPixels) || double.IsInfinity(unbinnedHfrPixels) || unbinnedHfrPixels <= 0.0) {
                return 1;
            }
            var raw = unbinnedHfrPixels / TargetHfrPixels;
            return Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero));
        }

        /// <summary>The <see cref="DetectionBinningEnum"/> member for an already-resolved factor.</summary>
        public static DetectionBinningEnum ToSetting(int factor) => (DetectionBinningEnum)Clamp(factor);

        /// <summary>
        /// The one-line hint shown under the option, e.g.
        /// <c>"Resolved: 2x - 0.28"/px, est. in-focus HFR ~5.4 px -> ~2.7 px binned"</c>. Explains what Auto
        /// picked and why, or (for an explicit setting) what the current pixel scale implies.
        /// </summary>
        public static string DescribeResolution(DetectionBinningEnum setting, double pixelScaleArcsecPerPixel) {
            var factor = Resolve(setting, pixelScaleArcsecPerPixel);
            var prefix = setting == DetectionBinningEnum.Auto ? "Resolved" : "Using";
            if (!IsUsablePixelScale(pixelScaleArcsecPerPixel)) {
                return $"{prefix}: {factor}x - pixel scale unknown, set pixel size and focal length in Options";
            }

            var hfr = EstimateInFocusHfrPixels(pixelScaleArcsecPerPixel);
            var ci = CultureInfo.CurrentCulture;
            return string.Format(ci,
                "{0}: {1}x - {2:0.00}\"/px, est. in-focus HFR ~{3:0.0} px -> ~{4:0.0} px binned",
                prefix, factor, pixelScaleArcsecPerPixel, hfr, hfr / factor);
        }

        private static bool IsUsablePixelScale(double pixelScaleArcsecPerPixel)
            => !double.IsNaN(pixelScaleArcsecPerPixel) && !double.IsInfinity(pixelScaleArcsecPerPixel) && pixelScaleArcsecPerPixel > 0.0;

        private static int Clamp(int factor) => Math.Max(1, Math.Min(MaxBinningFactor, factor));
    }
}
