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
    /// RECOMMENDS a detection binning factor from a MEASURED in-focus HFR, and converts between the
    /// <see cref="DetectionBinningEnum"/> setting and the integer factor the detector uses. The SINGLE source of
    /// truth for the recommendation rule: the options page, the optimization wizard and the documentation all cite
    /// it, so they cannot drift.
    ///
    /// <para>The rule: every pixel-unit detector knob (MinHFR, MinimumStarBoundingBoxSize, NoiseReductionRadius,
    /// StructureLayers, ...) is calibrated for an in-focus HFR near <see cref="TargetHfrPixels"/>. Pick the integer
    /// factor that brings the measured HFR closest to it.</para>
    ///
    /// <para>It recommends from a MEASUREMENT, never from pixel scale. An earlier version estimated star size as
    /// <c>assumedFwhm / (2 · pixelScale)</c>, and that cannot work: plausible seeing spans roughly 1.5" to 4", a
    /// factor of 2.7, which is wider than the whole 1x1-vs-2x2 decision margin. On a 0.28"/px rig the answer flips
    /// at about 2.3" of seeing, so an assumed figure decided the recommendation rather than the rig did — it told
    /// a user with 3.6 px stars to bin 2x2 when 3.6 px was already in range.</para>
    ///
    /// <para>Nothing here ever CHANGES the setting. The recommendation is text the user acts on, deliberately:
    /// a factor that applied itself would change detection behavior on upgrade and invalidate settings the user
    /// had already tuned.</para>
    /// </summary>
    public static class DetectionBinningResolver {

        /// <summary>The in-focus HFR (in binned pixels) the recommendation aims for: the center of the detector's
        /// calibrated 2-4 px band.</summary>
        public const double TargetHfrPixels = 3.0;

        /// <summary>The largest factor the recommendation will suggest (and the largest the options UI offers).</summary>
        public const int MaxBinningFactor = 4;

        /// <summary>The integer factor for <paramref name="setting"/>, clamped to the supported range.</summary>
        public static int ToFactor(DetectionBinningEnum setting) => Clamp((int)setting);

        /// <summary>
        /// The factor that brings <paramref name="measuredHfrPixels"/> closest to <see cref="TargetHfrPixels"/>.
        /// The HFR is in CAPTURED pixels — detection reports it that way at every factor — so this answer does not
        /// depend on the factor the measurement was taken at. NaN or non-positive input recommends 1.
        /// </summary>
        public static int RecommendFromHfr(double measuredHfrPixels) {
            if (double.IsNaN(measuredHfrPixels) || double.IsInfinity(measuredHfrPixels) || measuredHfrPixels <= 0.0) {
                return 1;
            }
            var raw = measuredHfrPixels / TargetHfrPixels;
            return Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero));
        }

        /// <summary>The <see cref="DetectionBinningEnum"/> member for a factor.</summary>
        public static DetectionBinningEnum ToSetting(int factor) => (DetectionBinningEnum)Clamp(factor);

        /// <summary>
        /// The short recommendation shown beside the setting, built from a MEASURED in-focus HFR. Leads with the
        /// measurement, because that is the fact; the factor follows from it. Short enough to share the dropdown's
        /// row. NaN (nothing measured yet) says how to get one rather than guessing.
        /// </summary>
        public static string DescribeRecommendation(int currentFactor, double measuredHfrPixels) {
            if (!IsUsableHfr(measuredHfrPixels)) {
                return "Run an auto-focus to get a recommendation";
            }
            var current = Clamp(currentFactor);
            var recommended = RecommendFromHfr(measuredHfrPixels);
            var ci = CultureInfo.CurrentCulture;
            return current == recommended
                ? string.Format(ci, "Measured in-focus HFR {0:0.0} px - {1}x{1} is right", measuredHfrPixels, recommended)
                : string.Format(ci, "Measured in-focus HFR {0:0.0} px - {1}x{1} recommended", measuredHfrPixels, recommended);
        }

        /// <summary>The reasoning behind <see cref="DescribeRecommendation"/>, for its tooltip.</summary>
        public static string DescribeRecommendationDetail(int currentFactor, double measuredHfrPixels, DateTime? measuredAtUtc) {
            if (!IsUsableHfr(measuredHfrPixels)) {
                return "Nothing measured yet. Run an auto-focus, or a live sweep in the Star Detection Optimization "
                     + "wizard, and the measured in-focus HFR will appear here with a recommendation. Pixel scale "
                     + "alone cannot predict star size: seeing varies more than the margin between binning factors.";
            }

            var recommended = RecommendFromHfr(measuredHfrPixels);
            var ci = CultureInfo.CurrentCulture;
            // "from your last auto-focus" covers both writers: an AF run's final exposure and an accepted live
            // wizard sweep's fitted minimum (see InFocusHfrRecord). Do not narrow it back to "at the end of a run".
            var when = measuredAtUtc.HasValue
                ? $"Measured {measuredAtUtc.Value.ToLocalTime():g} from your last auto-focus, in captured pixels."
                : "Measured from your last auto-focus, in captured pixels.";
            var reasoning = recommended > 1
                ? string.Format(ci, " Star detection is calibrated for in-focus stars of roughly 2 to 4 px; at {0:0.0} px, binning {1}x{1} for detection brings that to about {2:0.0} px.",
                    measuredHfrPixels, recommended, measuredHfrPixels / recommended)
                : string.Format(ci, " Star detection is calibrated for in-focus stars of roughly 2 to 4 px, and {0:0.0} px is already in that range, so binning is not needed.",
                    measuredHfrPixels);

            return when + reasoning + " Re-run an auto-focus after changing optics, so this reflects the current rig.";
        }

        /// <summary>True when the current factor is not what the measurement calls for. False when there is no
        /// measurement: the UI must not highlight a mismatch it cannot justify.</summary>
        public static bool DiffersFromRecommendation(int currentFactor, double measuredHfrPixels)
            => IsUsableHfr(measuredHfrPixels) && Clamp(currentFactor) != RecommendFromHfr(measuredHfrPixels);

        /// <summary>
        /// Whether the recommendation is worth showing at all: only when it asks for something. That is either
        /// "run an auto-focus so there is something to recommend from", or "the factor you have is not the one
        /// your measurement calls for". A recommendation that merely confirms the current setting is noise —
        /// it occupies a row to say nothing, and trains the eye to skip the line that matters.
        /// </summary>
        public static bool ShouldShowRecommendation(int currentFactor, double measuredHfrPixels)
            => !IsUsableHfr(measuredHfrPixels) || DiffersFromRecommendation(currentFactor, measuredHfrPixels);

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

        private static bool IsUsableHfr(double hfrPixels)
            => !double.IsNaN(hfrPixels) && !double.IsInfinity(hfrPixels) && hfrPixels > 0.0;

        private static int Clamp(int factor) => Math.Max(1, Math.Min(MaxBinningFactor, factor));
    }
}
