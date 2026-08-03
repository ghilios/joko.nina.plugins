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
    /// <para><b>Protection is exactly as generous as matching.</b> The box half-width is the match radius —
    /// a detection is protected iff it would have been MATCHED had this star carried a golden box. An earlier
    /// cut used the star's light footprint (2·HFR, ~40 px on a wing donut) and SATURATED the metric: precision
    /// read 1.000 on all 17 datasets for all three F23 arms, because a 40 px box launders genuine noise blobs
    /// along with the real stars. A metric that cannot separate configurations is as useless as a biased one.
    /// At the match radius the same runs give 0.946–1.000. <c>protectedStars</c> is reported per run so the size
    /// of the correction stays visible.</para>
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
                // Protection is EXACTLY as generous as matching, and no more: the half-width is the match radius.
                //
                // The first cut of this used max(2*HFR, 4, radius) — the star's light footprint, ~40 px on a wing
                // donut. That saturated the metric: precision read 1.000 on all 17 datasets for all three F23 arms,
                // because a 40 px box around every faint truth star launders genuine noise blobs too. A metric that
                // cannot separate configurations is as useless as one that is biased, just in the other direction.
                // Scored at the match radius the same runs give 0.946–1.000, which discriminates.
                //
                // The rule to hold onto: a detection is protected iff it would have been MATCHED had this star
                // carried a golden box. Anything wider is inventing true positives; anything narrower re-creates
                // the F31 bug.
                var half = radius > 0.0 ? radius : MinProtectionHalfWidthPixels;
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

        /// <summary>Fallback half-width for the degenerate case of a non-positive match radius, so a protection
        /// box is never zero-sized. Mirrors <see cref="GoldenTierThresholds.MinBoxHalfWidthPixels"/>.</summary>
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
        ///
        /// <para><b>Prefer <see cref="ExcludeProtected"/> for the truth half.</b> <see cref="GoldenMatch.ExcludeUnresolved"/>
        /// applies <c>centre-in-box OR IoU(box, detection bbox) &gt; 0</c>, so the DETECTION'S OWN bounding box
        /// dilates every rect — a wide donut detection is excluded well past the match radius, which is not what
        /// "protection is exactly as generous as matching" says. Measured on the saved wave-1 detection dumps,
        /// that reads up to 0.011 high (D09 s0: 1.0000 against 0.9909 symmetric; D17 s0: 0.9895 against 0.9793) —
        /// small, but systematic and always flattering. This overload is kept for the golden's own
        /// <c>unresolved</c> boxes, whose <c>Covers</c> semantics are the REAL bank's scoring path and must not
        /// move.</para>
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

        /// <summary>Binned centres of the real-but-unboxed truth stars (<see cref="UnboxedRealTiers"/>) — the
        /// population <see cref="ExcludeProtected"/> protects. Empty for a null/absent sidecar.</summary>
        public static List<(double X, double Y)> ProtectionCenters(IReadOnlyList<SyntheticStarDisposition> dispositions) =>
            Centers(dispositions, d => UnboxedRealTiers.Contains(d.Tier ?? string.Empty));

        /// <summary>Binned centres of EVERY star in the sidecar, whatever tier the golden policy gave it. This is
        /// the complete rendered population, so "is there a real star here?" has an exact answer — which is what
        /// <see cref="CountWithinRadius"/> turns into the F31 regression guard.</summary>
        public static List<(double X, double Y)> AllCenters(IReadOnlyList<SyntheticStarDisposition> dispositions) =>
            Centers(dispositions, _ => true);

        private static List<(double X, double Y)> Centers(
                IReadOnlyList<SyntheticStarDisposition> dispositions, Func<SyntheticStarDisposition, bool> predicate) {
            var pts = new List<(double X, double Y)>();
            if (dispositions == null) {
                return pts;
            }
            foreach (var d in dispositions) {
                if (d == null || !predicate(d)) {
                    continue;
                }
                if (!double.IsFinite(d.BinnedCenterX) || !double.IsFinite(d.BinnedCenterY)) {
                    continue; // cannot protect what we cannot place
                }
                pts.Add((d.BinnedCenterX, d.BinnedCenterY));
            }
            return pts;
        }

        /// <summary>
        /// Drops false positives whose CENTROID is within <paramref name="radius"/> of a protected truth star —
        /// exactly the predicate <see cref="GoldenMatch.Match"/> uses in <see cref="GoldenMatchMode.Centroid"/>
        /// mode, so a detection is excluded iff it would have been MATCHED had this star carried a golden box.
        /// Anything wider invents true positives; anything narrower re-creates F31.
        /// </summary>
        public static List<int> ExcludeProtected(
                IReadOnlyList<int> falsePositives, IReadOnlyList<DetBox> detected,
                IReadOnlyList<(double X, double Y)> centers, double radius) {
            if (falsePositives == null) {
                return new List<int>();
            }
            if (centers == null || centers.Count == 0 || !(radius > 0.0)) {
                return falsePositives.ToList();
            }
            return falsePositives.Where(di => !WithinRadius(detected[di], centers, radius)).ToList();
        }

        /// <summary>How many of <paramref name="indices"/> sit within <paramref name="radius"/> of one of
        /// <paramref name="centers"/>. Used with <see cref="AllCenters"/> to count scored false positives that
        /// are in fact real rendered stars — the F31 signature, which must be 0.</summary>
        public static int CountWithinRadius(
                IReadOnlyList<int> indices, IReadOnlyList<DetBox> detected,
                IReadOnlyList<(double X, double Y)> centers, double radius) {
            if (indices == null || centers == null || centers.Count == 0 || !(radius > 0.0)) {
                return 0;
            }
            return indices.Count(di => WithinRadius(detected[di], centers, radius));
        }

        private static bool WithinRadius(DetBox det, IReadOnlyList<(double X, double Y)> centers, double radius) {
            var r2 = radius * radius;
            foreach (var (x, y) in centers) {
                var dx = det.Cx - x;
                var dy = det.Cy - y;
                if (dx * dx + dy * dy <= r2) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The NULL CONTROL: every detection translated by (<see cref="NullShiftX"/>, <see cref="NullShiftY"/>)
        /// with wraparound. Detection count and spatial clustering survive; correspondence with the frame does
        /// not. Re-scoring these against the same reference gives the precision a detector would earn by
        /// CHANCE — the floor the metric can never read below, and therefore the only honest way to tell a real
        /// 1.000 from a saturated one.
        ///
        /// <para>This exists because the F31 repair failed twice in opposite directions before it worked: first
        /// biased (the golden omitted real stars), then saturated (protection sized by 2·HFR read 1.000 on all
        /// 17 datasets and measured nothing). Both look like a clean result from the precision column alone. A
        /// reported null makes the difference checkable instead of something a reader has to think to ask.</para>
        /// </summary>
        public static List<DetBox> ShiftForNullControl(IReadOnlyList<DetBox> detected, int frameWidth, int frameHeight) {
            var shifted = new List<DetBox>();
            if (detected == null || frameWidth <= 0 || frameHeight <= 0) {
                return shifted;
            }
            foreach (var d in detected) {
                var cx = Wrap(d.Cx + NullShiftX, frameWidth);
                var cy = Wrap(d.Cy + NullShiftY, frameHeight);
                var bx = Wrap(d.Box.X + NullShiftX, frameWidth);
                var by = Wrap(d.Box.Y + NullShiftY, frameHeight);
                shifted.Add(new DetBox(new RectD(bx, by, d.Box.W, d.Box.H), cx, cy));
            }
            return shifted;
        }

        /// <summary>Null-control translation, x (px). Far larger than any PSF or match radius in the bank, and
        /// not a round number, so it cannot line up with a rendered grid.</summary>
        public const int NullShiftX = 317;

        /// <summary>Null-control translation, y (px) — see <see cref="NullShiftX"/>.</summary>
        public const int NullShiftY = 211;

        private static double Wrap(double v, int extent) {
            var m = v % extent;
            return m < 0 ? m + extent : m;
        }
    }
}
