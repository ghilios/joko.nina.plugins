#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Globalization;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// RECOMMENDS a detection binning factor, and converts between the <see cref="DetectionBinningEnum"/>
    /// setting and the integer factor the detector uses. The SINGLE source of truth for the recommendation
    /// rule: the options page, the optimization wizard and the documentation all cite it, so they cannot drift.
    ///
    /// <para>The rule: every pixel-unit detector knob (MinHFR, MinimumStarBoundingBoxSize, NoiseReductionRadius,
    /// StructureLayers, ...) is calibrated for an in-focus HFR near <see cref="TargetHfrPixels"/>. Estimate the
    /// in-focus HFR from the pixel scale under an assumed <see cref="AssumedFwhmArcsec"/> total FWHM (seeing +
    /// optics + guiding), then pick the integer factor that lands closest to the target.</para>
    ///
    /// <para>Nothing here ever CHANGES the setting. The recommendation is text the user acts on, deliberately:
    /// a factor that applied itself would change detection behavior on upgrade and invalidate settings the user
    /// had already tuned.</para>
    /// </summary>
    public static class DetectionBinningResolver {

        /// <summary>The assumed total in-focus FWHM (seeing + optics + guiding) used to estimate star size from
        /// pixel scale alone. A stand-in for a typical night, not a measurement — the optimization wizard
        /// recommends from measured HFR when real sweep data is available.</summary>
        public const double AssumedFwhmArcsec = 3.0;

        /// <summary>The in-focus HFR (in binned pixels) the recommendation aims for: the center of the detector's
        /// calibrated 2-4 px band.</summary>
        public const double TargetHfrPixels = 3.0;

        /// <summary>The largest factor the recommendation will suggest (and the largest the options UI offers).</summary>
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

        /// <summary>The integer factor for <paramref name="setting"/>, clamped to the supported range.</summary>
        public static int ToFactor(DetectionBinningEnum setting) => Clamp((int)setting);

        /// <summary>
        /// The factor RECOMMENDED for <paramref name="pixelScaleArcsecPerPixel"/>, which is expected to ALREADY
        /// include any camera (hardware) binning — so the recommendation backs off on its own when the camera is
        /// already binning. An unusable pixel scale (focal length or pixel size unset) recommends 1 rather than
        /// guessing.
        /// </summary>
        public static int RecommendFromPixelScale(double pixelScaleArcsecPerPixel)
            => RecommendFromHfr(EstimateInFocusHfrPixels(pixelScaleArcsecPerPixel));

        /// <summary>
        /// The factor that brings <paramref name="unbinnedHfrPixels"/> closest to <see cref="TargetHfrPixels"/>.
        /// Shared by the pixel-scale estimate above and the optimization wizard's measured recommendation, so
        /// both answer to the same target. NaN or non-positive input recommends 1.
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
        /// The pixel scale the RECOMMENDATION reasons about: the profile's optics scaled by the auto-focus capture
        /// binning NINA is configured for. Detection itself uses the ACTUAL captured frame's BinX (see
        /// <c>HocusFocusStarDetection.ApplyDetectionImageContext</c>); this is the best estimate available before a
        /// frame exists, and it is shared by the options page and the optimization wizard so their recommendations
        /// cannot disagree. NaN when focal length or pixel size is unset.
        /// </summary>
        public static double PixelScaleFromProfile(IProfileService profileService) {
            var profile = profileService?.ActiveProfile;
            if (profile == null) {
                return double.NaN;
            }
            var captureBinning = Math.Max((short)1, profile.FocuserSettings.AutoFocusBinning);
            return MathUtility.ArcsecPerPixel(profile.CameraSettings.PixelSize, profile.TelescopeSettings.FocalLength) * captureBinning;
        }

        /// <summary>
        /// The one-line recommendation shown under the setting on the options page AND in the optimization
        /// wizard's confirmation panel, e.g.
        /// <c>Recommended: 2x2 (currently 1x1) - 0.28"/px, est. in-focus HFR ~5.4 px -&gt; ~2.7 px at 2x2</c>.
        /// It always names the RECOMMENDED factor and the numbers behind it; the current factor appears in
        /// parentheses so the user can see at a glance whether they already match. Purely advisory — see the
        /// class remarks for why nothing here applies itself.
        /// </summary>
        public static string DescribeRecommendation(int currentFactor, double pixelScaleArcsecPerPixel) {
            if (!IsUsablePixelScale(pixelScaleArcsecPerPixel)) {
                return "No recommendation - pixel scale unknown, set pixel size and focal length in NINA's Options";
            }

            var current = Clamp(currentFactor);
            var recommended = RecommendFromPixelScale(pixelScaleArcsecPerPixel);
            var hfr = EstimateInFocusHfrPixels(pixelScaleArcsecPerPixel);
            var ci = CultureInfo.CurrentCulture;
            var state = current == recommended ? "current" : string.Format(ci, "currently {0}x{0}", current);

            // At 1x1 the tail explains why no binning is called for; above it, the tail justifies the factor by
            // showing where the star size lands once the frame is binned.
            var tail = recommended > 1
                ? string.Format(ci, "est. in-focus HFR ~{0:0.0} px -> ~{1:0.0} px at {2}x{2}", hfr, hfr / recommended, recommended)
                : string.Format(ci, "est. in-focus HFR ~{0:0.0} px, {1}", hfr,
                    current == 1 ? "already in the detector's range" : "binning is not needed");

            return string.Format(ci, "Recommended: {0}x{0} ({1}) - {2:0.00}\"/px, {3}", recommended, state, pixelScaleArcsecPerPixel, tail);
        }

        /// <summary>True when <paramref name="currentFactor"/> is not what this pixel scale calls for, so the UI
        /// should present the recommendation as something to act on rather than as a confirmation.</summary>
        public static bool DiffersFromRecommendation(int currentFactor, double pixelScaleArcsecPerPixel)
            => IsUsablePixelScale(pixelScaleArcsecPerPixel) && Clamp(currentFactor) != RecommendFromPixelScale(pixelScaleArcsecPerPixel);

        /// <summary>
        /// Re-stamps an already-built parameter bundle onto a different binning factor, keeping
        /// <see cref="StarDetectorParams.PixelScale"/> consistent (it carries the factor, see
        /// <c>HocusFocusStarDetection.ApplyDetectionImageContext</c>). Used by the optimization wizard to run a
        /// search at a factor the user has not committed to yet, WITHOUT writing it to the profile first.
        /// </summary>
        public static void ApplyFactor(StarDetectorParams p, int factor) {
            if (p == null) {
                return;
            }
            var target = Clamp(factor);
            var unbinnedPixelScale = p.PixelScale / Math.Max(1, p.DetectionBinning);
            p.DetectionBinning = target;
            p.PixelScale = unbinnedPixelScale * target;
        }

        private static bool IsUsablePixelScale(double pixelScaleArcsecPerPixel)
            => !double.IsNaN(pixelScaleArcsecPerPixel) && !double.IsInfinity(pixelScaleArcsecPerPixel) && pixelScaleArcsecPerPixel > 0.0;

        private static int Clamp(int factor) => Math.Max(1, Math.Min(MaxBinningFactor, factor));
    }
}
