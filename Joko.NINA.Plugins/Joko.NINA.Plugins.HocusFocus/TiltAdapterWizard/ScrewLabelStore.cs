#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// Reads and writes the opaque blob behind <c>ITiltAdapterOptions.ScrewLabelsJson</c>: a map of
    /// <see cref="ScrewLabelScheme.Id"/> to that scheme's four user-entered screw labels, indexed by
    /// wizard screw number 1..4.
    ///
    /// One blob rather than indexed sibling keys (the shape <c>Screw1AngleDegrees</c>… uses) because the
    /// store is two-dimensional: a profile holds a set of labels per device family, so an EAT's motor
    /// names and a manual adapter's names coexist and each returns when its device is selected.
    ///
    /// <para>Empty string means "unset" — the scheme's default name applies. A scheme whose labels are all
    /// unset is dropped on write, so a user who never touches the feature persists <c>""</c>.</para>
    ///
    /// <para><see cref="Parse"/> is deliberately TOLERANT: malformed, truncated, or foreign JSON logs a
    /// warning and yields an empty map, exactly as <c>PerFilterStarDetectionStore</c> and
    /// <c>StarDetectionOptions.OptimizedSettingsJson</c> do. Labels are cosmetic — corrupt input must
    /// degrade to default names, never throw into a UI binding.</para>
    /// </summary>
    internal static class ScrewLabelStore {

        /// <summary>Labels held per scheme, one per wizard screw number 1..4.</summary>
        public const int ScrewSlots = 4;

        /// <summary>
        /// Trim, collapse blank-to-unset, and clamp to <see cref="ScrewLabelScheme.MaxLabelLength"/>. The
        /// TextBox enforces the cap too, but a label can also arrive from a hand-edited profile.
        /// </summary>
        public static string Normalize(string label) {
            if (string.IsNullOrWhiteSpace(label)) {
                return string.Empty;
            }
            var trimmed = label.Trim();
            return trimmed.Length <= ScrewLabelScheme.MaxLabelLength
                ? trimmed
                : trimmed.Substring(0, ScrewLabelScheme.MaxLabelLength);
        }

        /// <summary>
        /// Parsed labels by scheme id. Every returned array is exactly <see cref="ScrewSlots"/> long and
        /// contains no nulls, so callers can index it without further guarding. Returns an empty map for
        /// null/empty/corrupt input.
        /// </summary>
        public static Dictionary<string, string[]> Parse(string json) {
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) {
                return result;
            }

            Dictionary<string, string[]> raw;
            try {
                raw = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(json);
            } catch (Exception e) {
                Logger.Warning($"Discarding corrupt tilt-adapter ScrewLabelsJson; screws fall back to their default names. {e.Message}");
                return result;
            }
            if (raw == null) {
                return result;
            }

            foreach (var entry in raw) {
                if (string.IsNullOrEmpty(entry.Key)) {
                    continue;
                }
                // A shorter, longer, or null array is not worth discarding the whole blob over -- pad and
                // truncate to the fixed slot count instead.
                var labels = new string[ScrewSlots];
                for (int i = 0; i < ScrewSlots; ++i) {
                    labels[i] = entry.Value != null && i < entry.Value.Length
                        ? Normalize(entry.Value[i])
                        : string.Empty;
                }
                result[entry.Key] = labels;
            }
            return result;
        }

        /// <summary>
        /// The persisted form of <paramref name="bySchemeId"/>, with all-unset schemes omitted. Returns
        /// <see cref="string.Empty"/> when nothing is labeled, matching every other "" = unset string option.
        /// </summary>
        public static string Serialize(IReadOnlyDictionary<string, string[]> bySchemeId) {
            if (bySchemeId == null) {
                return string.Empty;
            }
            var populated = bySchemeId
                .Where(kv => kv.Value != null && kv.Value.Any(l => !string.IsNullOrEmpty(l)))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            return populated.Count == 0 ? string.Empty : JsonConvert.SerializeObject(populated);
        }
    }
}
