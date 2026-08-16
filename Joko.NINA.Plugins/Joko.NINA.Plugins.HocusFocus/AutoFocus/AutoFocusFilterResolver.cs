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
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    /// <summary>
    /// The single source of truth for "which filter will an auto-focus run started with a given imaging filter
    /// actually expose through". Pure and static so its consumers cannot drift apart:
    ///
    /// <list type="bullet">
    /// <item><see cref="AutoFocusEngine.SetAutofocusFilter(FilterInfo, System.Threading.CancellationToken, System.IProgress{NINA.Core.Model.ApplicationStatus})"/>,
    /// which then MOVES the wheel to the resolved filter.</item>
    /// <item><see cref="AutoFocusEngine.GetOptions"/>, which keys the per-filter sweep geometry on it.</item>
    /// <item>The Optimization Wizard's sweep readouts, which display it.</item>
    /// </list>
    ///
    /// <para>Two hand-copied implementations of this rule already existed before it was extracted (the engine's
    /// <c>SetAutofocusFilter</c> and the wizard's <c>ResolveSweepFilterName</c>), which is exactly the divergence
    /// this type exists to prevent. Anything that needs to answer "which filter" must call in here.</para>
    /// </summary>
    public static class AutoFocusFilterResolver {

        /// <summary>
        /// Whether this run will be re-pointed at the profile's designated auto-focus filter, and which one.
        /// True only when filter-wheel offsets are enabled AND a filter is actually flagged as the AF filter —
        /// the exact condition under which the engine moves the wheel away from the imaging filter.
        /// </summary>
        public static bool UsesDesignatedAutoFocusFilter(IProfile profile, out FilterInfo designatedFilter) {
            designatedFilter = null;
            if (profile?.FocuserSettings?.UseFilterWheelOffsets != true) {
                return false;
            }

            designatedFilter = profile.FilterWheelSettings?.FilterWheelFilters?.FirstOrDefault(f => f.AutoFocusFilter);
            return designatedFilter != null;
        }

        /// <summary>
        /// The filter the run will expose through. <paramref name="useExactImagingFilter"/> suppresses the
        /// designated-AF-filter substitution (the Optimization Wizard sweeps one specific filter on purpose).
        /// Returns <paramref name="imagingFilter"/> — which may itself be null — when no substitution applies.
        /// </summary>
        public static FilterInfo Resolve(IProfile profile, FilterInfo imagingFilter, bool useExactImagingFilter) {
            if (useExactImagingFilter && imagingFilter != null) {
                return imagingFilter;
            }

            return UsesDesignatedAutoFocusFilter(profile, out var designatedFilter) ? designatedFilter : imagingFilter;
        }

        /// <inheritdoc cref="Resolve"/>
        public static string ResolveName(IProfile profile, FilterInfo imagingFilter, bool useExactImagingFilter)
            => Resolve(profile, imagingFilter, useExactImagingFilter)?.Name;
    }
}
