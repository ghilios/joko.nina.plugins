#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog {

    /// <summary>
    /// One catalog star decoded from an ASTAP (HNSKY) star-database cell file. Immutable.
    ///
    /// The ASTAP databases are Gaia-derived, so coordinates are ICRS ≈ J2000 — the
    /// <see cref="Coordinates.Epoch"/> is always <see cref="Epoch.J2000"/>.
    /// </summary>
    public sealed class CatalogStar {

        /// <param name="coordinates">Star position (J2000).</param>
        /// <param name="magnitude">Visual-ish magnitude (Gaia G, treated as V for the gray-star model).</param>
        /// <param name="colorBV">(B−V) color if the source record carried it (6-byte Gaia records,
        /// database version 2); <c>null</c> for 5-byte records or older 6-byte records without color.</param>
        public CatalogStar(Coordinates coordinates, double magnitude, double? colorBV) {
            Coordinates = coordinates;
            Magnitude = magnitude;
            ColorBV = colorBV;
        }

        /// <summary>Star position, J2000.</summary>
        public Coordinates Coordinates { get; }

        /// <summary>Star magnitude (bright → small).</summary>
        public double Magnitude { get; }

        /// <summary>(B−V) color, or <c>null</c> when the record type carried no color information.</summary>
        public double? ColorBV { get; }

        public override string ToString() =>
            $"CatalogStar(RA={Coordinates.RADegrees:F4}°, Dec={Coordinates.Dec:F4}°, mag={Magnitude:F2}" +
            (ColorBV.HasValue ? $", B-V={ColorBV.Value:F3})" : ")");
    }
}
