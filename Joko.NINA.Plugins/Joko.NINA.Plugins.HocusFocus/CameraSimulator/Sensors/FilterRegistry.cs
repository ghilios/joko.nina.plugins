#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors {

    /// <summary>
    /// Hard-coded, immutable registry of the ten simulator filters (λc / Δλ from the design). Peak
    /// transmission is a uniform 0.9 default — the design does not specify per-filter transmission (its
    /// parity-table QE column is <em>sensor</em> QE, applied separately via <see cref="QeCurve"/>).
    /// </summary>
    public static class FilterRegistry {
        private static readonly IReadOnlyDictionary<SimulatorFilter, FilterDefinition> Definitions = BuildDefinitions();

        private const double DefaultTransmission = 0.9;

        /// <summary>All filter definitions, in enum order.</summary>
        public static IReadOnlyList<FilterDefinition> All { get; } = Array.AsReadOnly(
            Enum.GetValues(typeof(SimulatorFilter)).Cast<SimulatorFilter>().Select(f => Definitions[f]).ToArray());

        /// <summary>Resolve the immutable definition for a filter.</summary>
        public static FilterDefinition Get(SimulatorFilter filter) {
            if (!Definitions.TryGetValue(filter, out var def)) {
                throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown filter.");
            }
            return def;
        }

        private static IReadOnlyDictionary<SimulatorFilter, FilterDefinition> BuildDefinitions() {
            var list = new[] {
                // Filter, name, λc (nm), Δλ (nm), transmission
                new FilterDefinition(SimulatorFilter.L,     "L",       540.0, 320.0, DefaultTransmission),
                new FilterDefinition(SimulatorFilter.R,     "R",       635.0, 100.0, DefaultTransmission),
                new FilterDefinition(SimulatorFilter.G,     "G",       530.0, 100.0, DefaultTransmission),
                new FilterDefinition(SimulatorFilter.B,     "B",       465.0, 100.0, DefaultTransmission),
                new FilterDefinition(SimulatorFilter.Ha5,   "Hα 5nm",  656.3,   5.0, DefaultTransmission),
                new FilterDefinition(SimulatorFilter.Ha3,   "Hα 3nm",  656.3,   3.0, DefaultTransmission),
                new FilterDefinition(SimulatorFilter.OIII5, "OIII 5nm", 500.7,  5.0, DefaultTransmission),
                new FilterDefinition(SimulatorFilter.OIII3, "OIII 3nm", 500.7,  3.0, DefaultTransmission),
                new FilterDefinition(SimulatorFilter.SII5,  "SII 5nm", 672.4,   5.0, DefaultTransmission),
                new FilterDefinition(SimulatorFilter.SII3,  "SII 3nm", 672.4,   3.0, DefaultTransmission),
            };
            return list.ToDictionary(d => d.Filter);
        }
    }
}
