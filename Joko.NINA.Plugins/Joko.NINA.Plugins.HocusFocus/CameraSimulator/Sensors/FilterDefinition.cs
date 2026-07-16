#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors {

    /// <summary>
    /// Immutable description of a filter: central wavelength, bandwidth (FWHM), and peak transmission.
    /// Radiometry uses <c>Δλ · QE(λc) · Transmission</c>, so the sensor QE is applied separately via the
    /// shared QE curve and is NOT baked into <see cref="Transmission"/>.
    /// </summary>
    public sealed class FilterDefinition {

        public FilterDefinition(SimulatorFilter filter, string filterName, double centralWavelengthNm, double bandwidthNm, double transmission) {
            if (filterName == null) throw new ArgumentNullException(nameof(filterName));
            if (centralWavelengthNm <= 0) throw new ArgumentOutOfRangeException(nameof(centralWavelengthNm));
            if (bandwidthNm <= 0) throw new ArgumentOutOfRangeException(nameof(bandwidthNm));
            if (transmission <= 0 || transmission > 1) throw new ArgumentOutOfRangeException(nameof(transmission));

            Filter = filter;
            FilterName = filterName;
            CentralWavelengthNm = centralWavelengthNm;
            BandwidthNm = bandwidthNm;
            Transmission = transmission;
        }

        public SimulatorFilter Filter { get; }

        /// <summary>Friendly name, e.g. "L" or "Hα 5nm".</summary>
        public string FilterName { get; }

        /// <summary>Central wavelength λc (nm). For narrowband this is the emission line.</summary>
        public double CentralWavelengthNm { get; }

        /// <summary>Bandwidth Δλ (nm, FWHM).</summary>
        public double BandwidthNm { get; }

        /// <summary>Peak transmission (0..1). Uniform 0.9 default for v1; the design does not specify per-filter values.</summary>
        public double Transmission { get; }
    }
}
