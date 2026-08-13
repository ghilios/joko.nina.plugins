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
    ///
    /// <para>The band is stated on BOTH sides. ABOVE it binning is the remedy, so the recommendation names a
    /// factor. BELOW <see cref="MinCalibratedHfrPixels"/> there is no remedy here at all - binning only DIVIDES,
    /// so no factor makes a star larger - and the recommendation therefore stays at 1x1 while the text says the
    /// measurement is outside the calibrated range and points at MinHFR, the axis that can move. It must never
    /// tell a user below the band that they are "already in that range": the clamp floors the factor at 1, which
    /// is why the two cases look identical to <see cref="RecommendFromHfr"/> and must not look identical to the
    /// reader.</para>
    /// </summary>
    public static class DetectionBinningResolver {

        /// <summary>The in-focus HFR (in binned pixels) the recommendation aims for: the center of the detector's
        /// calibrated 2-4 px band.</summary>
        public const double TargetHfrPixels = 3.0;

        /// <summary>The LOWER edge of that band, in the same captured pixels: <see cref="TargetHfrPixels"/> less the
        /// band's 1 px half-width, so the two constants cannot say different things about where the band sits.
        /// Below this the pixel-unit knobs are outside the regime they were calibrated in. Named here rather than
        /// written as a literal at each site so the tooltip's wording and the visibility rule cannot drift apart.
        /// </summary>
        public const double MinCalibratedHfrPixels = TargetHfrPixels - 1.0;

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
        ///
        /// <para>This is the line the user READS; the tooltip is the line they have to hover for. So below the
        /// band it must not settle for "{n}x{n} is right", which reads as all-clear on the one surface that is
        /// always visible. It states the position and stops there — the remedy belongs in
        /// <see cref="DescribeRecommendationDetail"/>, which has room for it.</para>
        /// </summary>
        public static string DescribeRecommendation(int currentFactor, double measuredHfrPixels) {
            if (!IsUsableHfr(measuredHfrPixels)) {
                return "Run an auto-focus to get a recommendation";
            }
            var current = Clamp(currentFactor);
            var recommended = RecommendFromHfr(measuredHfrPixels);
            var ci = CultureInfo.CurrentCulture;
            if (current != recommended) {
                // A factor to change is the actionable fact on either side of the band, so it keeps the row.
                return string.Format(ci, "Measured in-focus HFR {0:0.0} px - {1}x{1} recommended", measuredHfrPixels, recommended);
            }
            if (IsBelowCalibratedBand(measuredHfrPixels)) {
                // The factor agrees and is still not the story: below the band no factor helps. Reached through the
                // SAME helper as the tooltip, so the two surfaces cannot disagree about where the band starts.
                return string.Format(ci, "Measured in-focus HFR {0:0.0} px - below the 2-4 px range", measuredHfrPixels);
            }
            return string.Format(ci, "Measured in-focus HFR {0:0.0} px - {1}x{1} is right", measuredHfrPixels, recommended);
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
            // Three cases, not two. RecommendFromHfr clamps to 1 both when the measurement is IN the band and when
            // it is below it, so an else-branch keyed on the factor alone tells an under-sampled rig it is in a
            // range it is nowhere near. Split on the measurement instead.
            string reasoning;
            if (recommended > 1) {
                reasoning = string.Format(ci, " Star detection is calibrated for in-focus stars of roughly 2 to 4 px; at {0:0.0} px, binning {1}x{1} for detection brings that to about {2:0.0} px.",
                    measuredHfrPixels, recommended, measuredHfrPixels / recommended);
            } else if (IsBelowCalibratedBand(measuredHfrPixels)) {
                reasoning = string.Format(ci, " Star detection is calibrated for in-focus stars of roughly 2 to 4 px, and {0:0.0} px is below that range. Detection binning cannot help here: it only divides, so no factor makes a star larger. The setting that can is MinHFR - lower it so stars this small are not rejected, or run the Star Detection Optimization wizard, which seeds it from the sweep.",
                    measuredHfrPixels);
            } else {
                reasoning = string.Format(ci, " Star detection is calibrated for in-focus stars of roughly 2 to 4 px, and {0:0.0} px is already in that range, so binning is not needed.",
                    measuredHfrPixels);
            }

            return when + reasoning + " Changing focal length, pixel size or capture binning discards this measurement, so it always describes the current rig.";
        }

        /// <summary>True when the current factor is not what the measurement calls for. False when there is no
        /// measurement: the UI must not highlight a mismatch it cannot justify.</summary>
        public static bool DiffersFromRecommendation(int currentFactor, double measuredHfrPixels)
            => IsUsableHfr(measuredHfrPixels) && Clamp(currentFactor) != RecommendFromHfr(measuredHfrPixels);

        /// <summary>
        /// Whether the recommendation is worth showing at all: only when it asks for something. That is "run an
        /// auto-focus so there is something to recommend from", "your stars are below the band the detector is
        /// calibrated for, and binning is not the fix", or "the factor you have is not the one your measurement
        /// calls for". A recommendation that merely confirms the current setting is noise — it occupies a row to
        /// say nothing, and trains the eye to skip the line that matters.
        ///
        /// <para>The below-band case is here because the clamp makes it INDISTINGUISHABLE from agreement: the
        /// recommendation is 1x1 and the setting is 1x1, so a rule written on the factor alone hides the row from
        /// exactly the users whose rig is outside the calibrated range.</para>
        /// </summary>
        public static bool ShouldShowRecommendation(int currentFactor, double measuredHfrPixels)
            => !IsUsableHfr(measuredHfrPixels)
                || IsBelowCalibratedBand(measuredHfrPixels)
                || DiffersFromRecommendation(currentFactor, measuredHfrPixels);

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

        /// <summary>
        /// Whether a usable measurement falls BELOW <see cref="MinCalibratedHfrPixels"/>. The ONE expression behind
        /// both the tooltip's below-band wording and the visibility rule, so the row can never be hidden from a
        /// user the wording was written for. It deliberately does not reach <see cref="RecommendFromHfr"/>: this
        /// changes what the user is TOLD, never what is recommended.
        /// </summary>
        private static bool IsBelowCalibratedBand(double hfrPixels)
            => IsUsableHfr(hfrPixels) && hfrPixels < MinCalibratedHfrPixels;

        private static int Clamp(int factor) => Math.Max(1, Math.Min(MaxBinningFactor, factor));
    }
}
