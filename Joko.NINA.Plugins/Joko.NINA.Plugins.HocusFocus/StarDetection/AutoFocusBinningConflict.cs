#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model.Equipment;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// The set of NINA Auto Focus Binning settings that are above 1x1 at the moment the user raises Hocus Focus's
    /// detection binning above 1. The two settings answer different questions and MULTIPLY, so stacking them is
    /// almost never intended — this type finds the conflict, phrases it, and (on the user's say-so) clears it.
    ///
    /// <para>Deliberately free of any UI: detection, the message text, and the reset are pure operations over the
    /// profile, so they can be tested without a dialog. The prompt itself lives in HocusFocusPlugin.</para>
    /// </summary>
    public sealed class AutoFocusBinningConflict {

        private AutoFocusBinningConflict(short globalBinning, IReadOnlyList<string> filterNames) {
            GlobalBinning = globalBinning;
            FilterNames = filterNames;
        }

        /// <summary>The profile-wide <c>FocuserSettings.AutoFocusBinning</c>, or 1 when it is not a conflict.</summary>
        public short GlobalBinning { get; }

        /// <summary>Filters whose per-filter <c>AutoFocusBinning</c> override is above 1x1, in profile order.</summary>
        public IReadOnlyList<string> FilterNames { get; }

        public bool HasConflict => GlobalBinning > 1 || FilterNames.Count > 0;

        public static readonly AutoFocusBinningConflict None = new AutoFocusBinningConflict(1, Array.Empty<string>());

        /// <summary>
        /// Enumerates every Auto Focus Binning setting above 1x1 in the active profile. A per-filter override wins
        /// over the global value for the filters that carry one (that is how <c>AutoFocusEngine.TakeExposure</c>
        /// resolves it), so both are reported: clearing only one would leave the other still capturing binned.
        /// </summary>
        public static AutoFocusBinningConflict Detect(IProfileService profileService) {
            var profile = profileService?.ActiveProfile;
            if (profile == null) {
                return None;
            }

            var global = profile.FocuserSettings.AutoFocusBinning;
            var filterNames = (profile.FilterWheelSettings?.FilterWheelFilters ?? Enumerable.Empty<FilterInfo>().ToList() as IEnumerable<FilterInfo>)
                .Where(f => f?.AutoFocusBinning != null && (f.AutoFocusBinning.X > 1 || f.AutoFocusBinning.Y > 1))
                .Select(f => f.Name)
                .ToList();

            if (global <= 1 && filterNames.Count == 0) {
                return None;
            }
            return new AutoFocusBinningConflict(global, filterNames);
        }

        /// <summary>
        /// The prompt text. Explains what each setting is for, states the combined factor, lists exactly what
        /// would be reset, and ends with the question.
        ///
        /// <para>Every line break here is deliberate typesetting, not paragraphing. This goes to NINA's
        /// <c>MyMessageBox</c>, whose TextBlock has no <c>TextWrapping</c> and no <c>MaxWidth</c> inside a window
        /// that sizes to content — so the window is exactly as wide as the longest line, and its buttons split
        /// that width. As one paragraph per idea this rendered ~1800px across. Keep lines under ~60 characters.
        /// The one line that cannot be bounded is the per-filter bullet, which carries a user-chosen name.</para>
        /// </summary>
        public string Describe(int detectionBinning) {
            var sb = new StringBuilder();
            sb.AppendLine("NINA's Auto Focus Binning is set above 1x1, and Hocus Focus");
            sb.AppendLine($"detection binning is now {detectionBinning}x{detectionBinning}.");
            sb.AppendLine();
            sb.AppendLine("These are different settings. Auto Focus Binning changes");
            sb.AppendLine("how the camera captures the auto-focus frames; it should");
            sb.AppendLine("match the binning you image at. Detection binning resamples");
            sb.AppendLine("the captured frame for star detection only.");
            sb.AppendLine();
            sb.AppendLine("The two multiply, so detection would currently run at");
            sb.AppendLine($"{EffectiveFactorDescription(detectionBinning)}.");
            sb.AppendLine();
            sb.AppendLine("Currently set above 1x1:");
            if (GlobalBinning > 1) {
                sb.AppendLine($"  - Auto Focus Binning (all filters): {GlobalBinning}x{GlobalBinning}");
            }
            foreach (var filterName in FilterNames) {
                sb.AppendLine($"  - Filter \"{filterName}\" Auto Focus Binning override");
            }
            sb.AppendLine();
            sb.Append("Set these back to 1x1?");
            return sb.ToString();
        }

        /// <summary>Sets the global Auto Focus Binning, and every per-filter override this conflict named, back to
        /// 1x1. Only touches the rows <see cref="Describe"/> listed.</summary>
        public void ResetToUnbinned(IProfileService profileService) {
            var profile = profileService?.ActiveProfile;
            if (profile == null) {
                return;
            }

            if (GlobalBinning > 1) {
                profile.FocuserSettings.AutoFocusBinning = 1;
            }

            if (FilterNames.Count == 0) {
                return;
            }
            var filters = profile.FilterWheelSettings?.FilterWheelFilters;
            if (filters == null) {
                return;
            }
            foreach (var filter in filters) {
                if (filter != null && FilterNames.Contains(filter.Name)) {
                    filter.AutoFocusBinning = new BinningMode(1, 1);
                }
            }
        }

        private string EffectiveFactorDescription(int detectionBinning) {
            var cameraFactor = Math.Max((short)1, GlobalBinning);
            if (FilterNames.Count > 0 && GlobalBinning <= 1) {
                return $"{detectionBinning}x on top of each filter's own capture binning";
            }
            // "at detection" rather than "again for detection" purely for width: it parallels "at capture" and
            // keeps the whole clause on ONE line under Describe's budget, so the sentence sits on two balanced
            // lines instead of three with a 24-character orphan. The combined factor stays contiguous.
            return $"{cameraFactor * detectionBinning}x the native pixel size ({cameraFactor}x at capture, {detectionBinning}x at detection)";
        }
    }
}
