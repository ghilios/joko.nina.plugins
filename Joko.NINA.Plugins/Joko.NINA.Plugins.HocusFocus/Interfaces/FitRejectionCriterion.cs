#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Converters;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    /// <summary>
    /// Selects which goodness-of-fit metric gates whether an auto-focus hyperbolic fit is accepted. R² is the
    /// classic (default, unchanged) criterion. Reduced χ² compares the residuals against each point's measurement
    /// uncertainty and is only meaningful for weighted fits (see <see cref="HyperbolicFitModel"/> and the weighted
    /// fit option).
    /// </summary>
    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum FitRejectionCriterion {

        [Description("R²")]
        RSquared,

        [Description("Reduced χ²")]
        ReducedChiSquared
    }
}
