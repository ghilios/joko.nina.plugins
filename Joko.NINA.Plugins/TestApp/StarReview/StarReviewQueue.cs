#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;

namespace TestApp.StarReview {

    /// <summary>Which frames the review window should surface.</summary>
    public enum StarReviewMode {
        /// <summary>Frames whose accepted-star count is below the review threshold (the ones worth labeling).</summary>
        Low,

        /// <summary>Frames the optimizer can least trust: the low ones PLUS each run's most-defocused extremes.</summary>
        Uncertain,

        /// <summary>Every frame.</summary>
        All
    }

    /// <summary>
    /// A single reviewable frame in the queue: its run, focuser position, the on-disk frame path, and the
    /// accepted-star count under the chosen params (used for the Low/Uncertain selection and shown in the UI).
    /// </summary>
    public sealed class StarReviewFrame {
        public string RunId { get; set; }
        public int FocuserPosition { get; set; }
        public string FramePath { get; set; }
        public int AcceptedCount { get; set; }

        /// <summary>True when this frame is one of its run's min/max focuser-position extremes.</summary>
        public bool IsExtreme { get; set; }
    }

    /// <summary>
    /// Pure queue-selection logic (no IO/UI): given every frame with its accepted-star count, picks which frames
    /// the reviewer should see for a given <see cref="StarReviewMode"/>. Kept separate so it is unit-testable.
    /// </summary>
    public static class StarReviewQueue {

        /// <summary>
        /// N_review — the accepted-star count at/under which a frame is "low" and worth a human look. Tied to the
        /// objective's <c>NFloor</c> star-count knee (8): frames with fewer than the floor are exactly the ones the
        /// star-count sub-score penalizes, so they are the ones a reviewer most wants to correct. A frame AT or
        /// ABOVE the floor is already scoring well on star count and rarely needs labels.
        /// </summary>
        public const int NReview = 8;

        /// <summary>
        /// Builds the review queue from <paramref name="allFrames"/> (which must already carry per-frame
        /// AcceptedCount and IsExtreme) for the given <paramref name="mode"/>:
        /// <list type="bullet">
        ///   <item><b>Low</b>: AcceptedCount &lt; <see cref="NReview"/>.</item>
        ///   <item><b>Uncertain</b>: the Low set UNION each run's most-defocused extremes (min/max focuser
        ///     position). There is no cheap per-frame optimizer "uncertainty" signal available offline, so this is
        ///     the documented stand-in: starved frames plus the defocused extremes are exactly where the optimizer
        ///     is most likely to be wrong (the hard floor lives at the extremes).</item>
        ///   <item><b>All</b>: every frame.</item>
        /// </list>
        /// Output order is deterministic: by run id (ordinal), then by focuser position.
        /// </summary>
        public static List<StarReviewFrame> Build(IReadOnlyList<StarReviewFrame> allFrames, StarReviewMode mode) {
            if (allFrames == null) {
                throw new ArgumentNullException(nameof(allFrames));
            }

            IEnumerable<StarReviewFrame> selected;
            switch (mode) {
                case StarReviewMode.All:
                    selected = allFrames;
                    break;

                case StarReviewMode.Low:
                    selected = allFrames.Where(f => f.AcceptedCount < NReview);
                    break;

                case StarReviewMode.Uncertain:
                    selected = allFrames.Where(f => f.AcceptedCount < NReview || f.IsExtreme);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown review mode");
            }

            return selected
                .OrderBy(f => f.RunId, StringComparer.Ordinal)
                .ThenBy(f => f.FocuserPosition)
                .ToList();
        }

        /// <summary>
        /// Marks the min/max focuser-position frame of each run as an extreme (in place), so
        /// <see cref="StarReviewMode.Uncertain"/> can include them. A run with a single distinct position has that
        /// one frame as its (sole) extreme.
        /// </summary>
        public static void MarkExtremes(IReadOnlyList<StarReviewFrame> allFrames) {
            if (allFrames == null) {
                return;
            }
            foreach (var byRun in allFrames.GroupBy(f => f.RunId, StringComparer.Ordinal)) {
                var positions = byRun.Select(f => f.FocuserPosition).ToList();
                if (positions.Count == 0) {
                    continue;
                }
                var min = positions.Min();
                var max = positions.Max();
                foreach (var f in byRun) {
                    f.IsExtreme = f.FocuserPosition == min || f.FocuserPosition == max;
                }
            }
        }

        public static StarReviewMode ParseMode(string s) {
            if (string.IsNullOrWhiteSpace(s)) {
                return StarReviewMode.Low;
            }
            switch (s.Trim().ToLowerInvariant()) {
                case "low": return StarReviewMode.Low;
                case "uncertain": return StarReviewMode.Uncertain;
                case "all": return StarReviewMode.All;
                default:
                    throw new ArgumentException($"--review: '{s}' must be 'low', 'uncertain', or 'all'");
            }
        }
    }
}
