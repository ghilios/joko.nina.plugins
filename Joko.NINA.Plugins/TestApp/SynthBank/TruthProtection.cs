#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;

namespace TestApp.SynthBank {

    /// <summary>
    /// Repairs the synthetic bank's false-positive accounting by protecting detections of stars the golden
    /// policy dropped — F31.
    ///
    /// <para><b>The defect.</b> <see cref="GoldenFromTruth"/> tiers each truth star by its native PEAK-PIXEL SNR.
    /// At or above <see cref="GoldenTierThresholds.UnresolvedSnr"/> (3.5) but below the "low" cut it becomes
    /// <see cref="SyntheticTier.Unresolved"/> — a box that is neither a required find nor a false positive.
    /// BELOW 3.5 it becomes <see cref="SyntheticTier.Omitted"/> and produces no box at all, so a detection at
    /// its location is scored as a FALSE POSITIVE. That is backwards, and the policy's own doc comments say so
    /// in two places that contradict each other: <c>UnresolvedSnr</c> reasons that "below ~3.5σ its own
    /// peak-pixel test cannot vouch for visibility with any confidence; scoring a detection there as a false
    /// positive would be punishing a detector for something the reference itself cannot certify either way",
    /// while <see cref="SyntheticTier.Omitted"/> asserts the opposite. The implementation follows the harsher
    /// one, so the LESS certifiable a star is, the HARSHER the detector is judged for finding it.</para>
    ///
    /// <para><b>Why it matters.</b> Defocus destroys peak-pixel SNR while leaving integrated flux intact, so the
    /// golden evaporates toward the sweep wings (D08_c11_2800mm: 81 golden stars at focus, 9 at the extreme
    /// frame, against 123–126 truth stars per frame throughout). Measured on the F23 wave-1 arms, <b>96% of the
    /// false positives the bank reported were real rendered stars</b> — D09 control arm: 280 scored FP, 269 with
    /// a truth star within the match radius, 11 genuinely spurious. Config A at a floored Sensitivity was
    /// finding MORE REAL STARS than C0 and being charged for every one, which is the whole of F23's evidence.</para>
    ///
    /// <para><b>The repair.</b> The truth sidecar written beside every synthetic frame is COMPLETE by
    /// construction, so the dropped stars are recoverable without regenerating the bank. This builds protection
    /// boxes for them and hands them to <see cref="GoldenMatch.ExcludeUnresolved"/>, which already implements
    /// exactly the right semantics: a detection landing on one is excluded from the false-positive count, and
    /// the box is never counted as a missed star.</para>
    ///
    /// <para><b>Known residual bias, stated rather than hidden.</b> A protection box scales with the star's own
    /// light footprint (2·HFR), so around a heavily-defocused donut it can reach ~40 px — wider than the 12 px
    /// match radius. A genuine noise blob landing inside that footprint is therefore laundered. Precision is
    /// consequently a slight UPPER bound in the presence of large donuts, where before this fix it was a severe
    /// LOWER bound (96% of reported false positives were real stars). The trade is deliberate and heavily net
    /// positive, but "exact" is the wrong word for the result: <c>protectedStars</c> is reported per run so the
    /// size of the correction is always visible.</para>
    ///
    /// <para><b>Recall is deliberately untouched.</b> Only the false-positive side changes. The golden's
    /// <c>stars</c> list still defines what must be found, so recall@high and recall@all stay exactly
    /// comparable to every previously published number — a protected star is not promoted to a required find.
    /// Scoring is a strict improvement in one direction only, which is what makes before/after diffs readable.</para>
    /// </summary>
    public static class TruthProtection {

        /// <summary>The sidecar suffix <c>SynthBankRunner</c> writes beside each rendered frame.</summary>
        public const string TruthSuffix = ".truth.json";

        /// <summary>
        /// Tiers whose stars are REAL but carry no box in the golden, so a detection on them is currently
        /// mis-scored as a false positive.
        ///
        /// <para><see cref="SyntheticTier.Omitted"/> is the population F31 is about. <see cref="SyntheticTier.MergedInto"/>
        /// is included for a different reason: its light was folded into a neighbour's box, so a detector that
        /// resolves the pair reports TWO centres where the golden holds one, and the second is charged as a
        /// false positive for being MORE correct than the reference. Both are cases of the reference, not the
        /// detector, being incomplete.</para>
        /// </summary>
        private static readonly HashSet<string> UnboxedRealTiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            SyntheticTier.Omitted,
            SyntheticTier.MergedInto
        };

        /// <summary>
        /// Builds protection boxes for the real-but-unboxed truth stars. Returns an empty list for a null/empty
        /// input, so a caller with no truth sidecar (the real bank) is a no-op and scores exactly as before.
        ///
        /// <para><paramref name="matchRadius"/> is the SAME radius the scorer uses to decide correspondence. The
        /// box is floored at that radius so protection can never be tighter than matching: if a detection is
        /// close enough to a real star to be called a match, it must also be close enough not to be called a
        /// false positive. Without that floor a heavily-defocused star with a small measured HFR could be
        /// matched-but-not-protected, which is the inconsistency this class exists to remove.</para>
        /// </summary>
        public static List<GoldenStarBox> BuildProtectionBoxes(
                IReadOnlyList<SyntheticStarDisposition> dispositions, double matchRadius) {
            var boxes = new List<GoldenStarBox>();
            if (dispositions == null || dispositions.Count == 0) {
                return boxes;
            }
            var radius = double.IsFinite(matchRadius) && matchRadius > 0.0 ? matchRadius : 0.0;
            foreach (var d in dispositions) {
                if (d == null || d.Tier == null || !UnboxedRealTiers.Contains(d.Tier)) {
                    continue;
                }
                if (!double.IsFinite(d.BinnedCenterX) || !double.IsFinite(d.BinnedCenterY)) {
                    continue; // no usable position — cannot protect what we cannot place
                }
                var hfr = double.IsFinite(d.BinnedHfrPixels) && d.BinnedHfrPixels > 0.0 ? d.BinnedHfrPixels : 0.0;
                // Same shape as GoldenFromTruth.BoxHalfWidthPixels' HFR term and minimum, floored at the match
                // radius (see the parameter note above). The annulus term is deliberately dropped: it needs the
                // native outer radius and the binning factor, neither of which the sidecar carries directly, and
                // for a donut 2*HFR already spans the ring.
                var half = Math.Max(Math.Max(2.0 * hfr, MinProtectionHalfWidthPixels), radius);
                var w = 2.0 * Math.Ceiling(half);
                boxes.Add(new GoldenStarBox {
                    X = d.BinnedCenterX - w / 2.0,
                    Y = d.BinnedCenterY - w / 2.0,
                    W = w,
                    H = w,
                    Confidence = d.Tier
                });
            }
            return boxes;
        }

        /// <summary>Mirrors <see cref="GoldenTierThresholds.MinBoxHalfWidthPixels"/> so a heavily-oversampled
        /// in-focus source still gets a usable multi-pixel protection box.</summary>
        public const double MinProtectionHalfWidthPixels = 4.0;

        /// <summary>
        /// Reads <c>&lt;imagePath&gt;.truth.json</c>. Returns null when the sidecar is absent (every real-bank
        /// run) or unreadable — callers treat null as "no protection available" and score as before, so a
        /// corrupt sidecar degrades to the old behaviour rather than failing the run.
        /// </summary>
        public static IReadOnlyList<SyntheticStarDisposition> LoadForImage(string imagePath) {
            if (string.IsNullOrWhiteSpace(imagePath)) {
                return null;
            }
            var path = imagePath + TruthSuffix;
            if (!File.Exists(path)) {
                return null;
            }
            try {
                return JsonConvert.DeserializeObject<List<SyntheticStarDisposition>>(File.ReadAllText(path));
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>
        /// Convenience for a scoring loop: the golden's own <c>unresolved</c> boxes plus the truth-derived
        /// protection boxes, as the rect list <see cref="GoldenMatch.ExcludeUnresolved"/> expects. Both inputs
        /// may be null.
        /// </summary>
        public static List<RectD> BuildExclusionRects(
                IReadOnlyList<GoldenStarBox> goldenUnresolved,
                IReadOnlyList<SyntheticStarDisposition> dispositions,
                double matchRadius) {
            var rects = new List<RectD>();
            if (goldenUnresolved != null) {
                rects.AddRange(goldenUnresolved.Select(b => new RectD(b.X, b.Y, b.W, b.H)));
            }
            rects.AddRange(BuildProtectionBoxes(dispositions, matchRadius).Select(b => new RectD(b.X, b.Y, b.W, b.H)));
            return rects;
        }
    }
}
