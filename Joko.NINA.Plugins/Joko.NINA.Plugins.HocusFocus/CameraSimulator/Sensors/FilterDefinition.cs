#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors {

    /// <summary>
    /// The filters the synthetic camera can place in front of the sensor: broadband L/R/G/B and
    /// narrowband Hα/OIII/SII at 3 nm and 5 nm. Wider bandpass passes more light → shorter exposure for
    /// equal signal.
    /// </summary>
    /// <remarks>
    /// Placed here for Phase 1 (options don't exist yet). A later phase moves this to
    /// <c>Interfaces/ICameraSimulatorOptions.cs</c> and decorates it with
    /// <c>[Description]</c> / <c>[TypeConverter]</c> for the options UI.
    /// </remarks>
    public enum SimulatorFilter {
        L,
        R,
        G,
        B,
        Ha5,
        Ha3,
        OIII5,
        OIII3,
        SII5,
        SII3
    }

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
