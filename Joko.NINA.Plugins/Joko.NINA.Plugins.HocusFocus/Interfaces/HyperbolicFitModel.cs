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
    /// Selects which hyperbolic model is fit to the auto-focus HFR/FWHM curve. Symmetric is the classic
    /// 4-parameter hyperbola; the other three are 5-parameter asymmetric variants for optical trains whose
    /// focus curve is steeper on one side of focus than the other.
    /// </summary>
    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum HyperbolicFitModel {

        [Description("Symmetric")]
        Symmetric,

        [Description("Uneven Blend (Legacy)")]
        UnevenBlendLegacy,

        [Description("Tilted Hyperbola")]
        TiltedHyperbola,

        [Description("Smooth Blend")]
        SmoothBlend,

        [Description("Hybrid (Best Fit)")]
        Hybrid
    }
}
