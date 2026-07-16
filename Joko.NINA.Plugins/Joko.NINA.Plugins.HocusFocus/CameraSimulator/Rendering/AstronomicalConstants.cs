#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Constants shared across the render pipeline so that every stage computes the same physical quantity
    /// from the same value. In particular, the plate scale (arcsec/px) is used by three stages —
    /// <see cref="TanProjection"/> (star positions), <see cref="RadiometryCalculator"/> (pixel solid angle),
    /// and <see cref="DefocusModel"/> (seeing σ) — which the compositor combines, so they must all
    /// derive it from the one full-precision <see cref="ArcsecPerRadian"/> here rather than local literals.
    /// </summary>
    internal static class AstronomicalConstants {

        /// <summary>Arcseconds per radian = 3600·180/π.</summary>
        public const double ArcsecPerRadian = 206264.806247096355;
    }
}
