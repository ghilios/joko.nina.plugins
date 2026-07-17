#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog {

    /// <summary>
    /// Reads real catalog stars from an ASTAP/HNSKY star database (<c>.1476</c> or <c>.290</c> cell files)
    /// for a given pointing and field of view, down to a limiting magnitude.
    /// </summary>
    public interface IAstapCatalogReader {

        /// <summary>
        /// Query stars intersecting a square field of view.
        /// </summary>
        /// <param name="centerRaDeg">Field-center RA (J2000, degrees, [0, 360)).</param>
        /// <param name="centerDecDeg">Field-center Dec (J2000, degrees, [−90, 90]).</param>
        /// <param name="fovDeg">Field-of-view side length (degrees). Used both to pick the intersecting cells
        /// and to box-filter stars around the pointing; pass a value large enough to bound the sensor (e.g.
        /// the diagonal FOV). Precise rectangular framing happens downstream in the compositor.</param>
        /// <param name="limitingMagnitude">Faintest magnitude to return. Records are magnitude-sorted, so the
        /// reader stops reading a cell once it passes this limit.</param>
        /// <returns>Lazily-evaluated catalog stars. Eager validation (folder/database presence) happens when
        /// <see cref="Query"/> is called; per-cell errors surface during enumeration.</returns>
        IEnumerable<CatalogStar> Query(double centerRaDeg, double centerDecDeg, double fovDeg, double limitingMagnitude);
    }
}
