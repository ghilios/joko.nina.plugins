using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Shared oracle helpers for star-detector equivalence and baseline tests. Later parallelization tasks
    /// call <see cref="Signature"/> to assert that parallel changes alter speed only, not results.
    ///
    /// <para>Both field-building methods reuse the project's existing synthetic helpers
    /// (<see cref="SyntheticStarField"/> and <see cref="SyntheticDefocusedStarImage"/>) so the
    /// noise model is bit-for-bit identical to all other tests in the suite.</para>
    /// </summary>
    public static class StarDetectorEquivalence {

        // ── Small field (fast: used by the always-on equivalence / baseline test) ──────────────────────
        public const int SmallFieldWidth = 512;
        public const int SmallFieldHeight = 512;
        public const int SmallFieldSeed = 77777;
        private const float SmallBackground = 0.05f;
        private const double SmallNoiseSigma = 0.02;
        private const double SmallStarSigmaPx = 2.5;

        // 7×7 grid = 49 stars, spaced 64 px, starting at (32, 32) → centres at [32..416] in steps of 64.
        // A few dozen stars is plenty for the equivalence oracle; keeps the non-Explicit test fast (~1 s).
        private static readonly (double x, double y, double peak)[] SmallFieldStars =
            BuildGridStars(originX: 32, originY: 32, stepX: 64, stepY: 64, cols: 7, rows: 7, peak: 0.70);

        // ── Large field (used by the benchmark — bigger, still deterministic) ───────────────────────────
        public const int LargeFieldWidth = 2048;
        public const int LargeFieldHeight = 2048;
        public const int LargeFieldSeed = 99999;
        private const float LargeBackground = 0.05f;
        private const double LargeNoiseSigma = 0.02;
        private const double LargeStarSigmaPx = 2.5;

        // 19×19 grid → 361 stars; representative of a dense multi-star autofocus frame.
        private static readonly (double x, double y, double peak)[] LargeFieldStars =
            BuildGridStars(originX: 56, originY: 56, stepX: 102, stepY: 102, cols: 19, rows: 19, peak: 0.70);

        // ─────────────────────────────────────────────────────────────────────────────────────────────────

        private static (double x, double y, double peak)[] BuildGridStars(
            double originX, double originY,
            double stepX, double stepY,
            int cols, int rows,
            double peak) {
            var list = new List<(double, double, double)>(cols * rows);
            for (int r = 0; r < rows; ++r)
                for (int c = 0; c < cols; ++c)
                    list.Add((originX + c * stepX, originY + r * stepY, peak));
            return list.ToArray();
        }

        /// <summary>
        /// Build the standard small deterministic field used by the equivalence / baseline tests.
        /// Callers must dispose the returned <see cref="Mat"/>.
        /// </summary>
        public static Mat BuildSmallField() {
            var mat = SyntheticStarField.CreateFlat(SmallFieldWidth, SmallFieldHeight, SmallBackground);
            foreach (var (x, y, peak) in SmallFieldStars)
                SyntheticStarField.AddStar(mat, x, y, SmallStarSigmaPx, peak);
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, SmallNoiseSigma, SmallFieldSeed);
            return mat;
        }

        /// <summary>
        /// Build the standard large deterministic field used by the benchmark.
        /// Callers must dispose the returned <see cref="Mat"/>.
        /// </summary>
        public static Mat BuildLargeField() {
            var mat = SyntheticStarField.CreateFlat(LargeFieldWidth, LargeFieldHeight, LargeBackground);
            foreach (var (x, y, peak) in LargeFieldStars)
                SyntheticStarField.AddStar(mat, x, y, LargeStarSigmaPx, peak);
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, LargeNoiseSigma, LargeFieldSeed);
            return mat;
        }

        /// <summary>
        /// Standard <see cref="StarDetectorParams"/> used for the equivalence / baseline tests.
        /// ModelPSF is off (the auto-focus production default) to keep the non-Explicit tests fast.
        /// </summary>
        public static StarDetectorParams StandardParams() => new StarDetectorParams {
            ModelPSF = false,
        };

        /// <summary>
        /// Runs detection on the given pre-built image using a fresh <see cref="StarDetector"/>.
        /// </summary>
        public static Task<HocusFocusStarDetectorResult> RunDetect(Mat image, StarDetectorParams p) {
            var detector = new StarDetector(new AlglibAPI());
            return detector.Detect(image, p, null, CancellationToken.None);
        }

        /// <summary>
        /// Produces a canonical, deterministic string from a detection result.
        /// The signature covers:
        ///   – detected star count
        ///   – per-star tuples (Center.X, Center.Y, HFR, PeakBrightness), sorted by (Y then X), rounded to 4 / 6 d.p.
        ///   – every scalar counter in <see cref="StarDetectorMetrics"/>
        ///   – each *Bounds list, sorted Y-then-X by the Rect's top-left corner
        /// Rounding makes it robust across machines; stable sort order makes it deterministic.
        /// </summary>
        public static string Signature(HocusFocusStarDetectorResult result) {
            var sb = new StringBuilder();
            var stars = result.DetectedStars ?? new List<Star>();

            // Header: detected count
            sb.Append("count=").AppendLine(stars.Count.ToString(CultureInfo.InvariantCulture));

            // Per-star rows, sorted Y-then-X
            var sorted = stars
                .OrderBy(s => Math.Round(s.Center.Y, 4, MidpointRounding.AwayFromZero))
                .ThenBy(s => Math.Round(s.Center.X, 4, MidpointRounding.AwayFromZero))
                .ToList();

            foreach (var s in sorted) {
                sb.Append("star ")
                  .Append(F(s.Center.X, 4)).Append(' ')
                  .Append(F(s.Center.Y, 4)).Append(' ')
                  .Append(F(s.HFR, 6)).Append(' ')
                  .AppendLine(F(s.PeakBrightness, 4));
            }

            // Metrics scalars
            var m = result.Metrics ?? new StarDetectorMetrics();
            sb.Append("StructureCandidates=").AppendLine(m.StructureCandidates.ToString(CultureInfo.InvariantCulture));
            sb.Append("TotalDetected=").AppendLine(m.TotalDetected.ToString(CultureInfo.InvariantCulture));
            sb.Append("TooSmall=").AppendLine(m.TooSmall.ToString(CultureInfo.InvariantCulture));
            sb.Append("OnBorder=").AppendLine(m.OnBorder.ToString(CultureInfo.InvariantCulture));
            sb.Append("TooLowHFR=").AppendLine(m.TooLowHFR.ToString(CultureInfo.InvariantCulture));
            sb.Append("HFRAnalysisFailed=").AppendLine(m.HFRAnalysisFailed.ToString(CultureInfo.InvariantCulture));
            sb.Append("PSFFitFailed=").AppendLine(m.PSFFitFailed.ToString(CultureInfo.InvariantCulture));
            sb.Append("OutsideROI=").AppendLine(m.OutsideROI.ToString(CultureInfo.InvariantCulture));
            sb.Append("SaturatedPixelCount=").AppendLine(m.SaturatedPixelCount.ToString(CultureInfo.InvariantCulture));
            sb.Append("HotpixelCount=").AppendLine(m.HotpixelCount.ToString(CultureInfo.InvariantCulture));
            sb.Append("RelaxationAdmittedCount=").AppendLine(m.RelaxationAdmittedCount.ToString(CultureInfo.InvariantCulture));

            // Bounds lists (sorted Y-then-X by top-left corner)
            AppendBounds(sb, "TooDistortedBounds", m.TooDistortedBounds);
            AppendBounds(sb, "DegenerateBounds", m.DegenerateBounds);
            AppendBounds(sb, "SaturatedBounds", m.SaturatedBounds);
            AppendBounds(sb, "LowSensitivityBounds", m.LowSensitivityBounds);
            AppendBounds(sb, "NotCenteredBounds", m.NotCenteredBounds);
            AppendBounds(sb, "TooFlatBounds", m.TooFlatBounds);
            AppendBounds(sb, "TooElongatedBounds", m.TooElongatedBounds);
            AppendBounds(sb, "BloomSuppressedBounds", m.BloomSuppressedBounds);
            AppendBounds(sb, "ContaminatedBounds", m.ContaminatedBounds);

            return sb.ToString();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

        private static string F(double v, int decimals) =>
            Math.Round(v, decimals, MidpointRounding.AwayFromZero)
                .ToString("F" + decimals, CultureInfo.InvariantCulture);

        private static void AppendBounds(StringBuilder sb, string name, List<Rect> bounds) {
            if (bounds == null || bounds.Count == 0) {
                sb.Append(name).AppendLine("=[]");
                return;
            }
            var sorted = bounds
                .OrderBy(r => r.Y)
                .ThenBy(r => r.X)
                .ToList();
            sb.Append(name).Append("=[");
            for (int i = 0; i < sorted.Count; ++i) {
                if (i > 0) sb.Append(',');
                var r = sorted[i];
                sb.Append('(').Append(r.X).Append(',').Append(r.Y).Append(',')
                  .Append(r.Width).Append(',').Append(r.Height).Append(')');
            }
            sb.AppendLine("]");
        }
    }
}
