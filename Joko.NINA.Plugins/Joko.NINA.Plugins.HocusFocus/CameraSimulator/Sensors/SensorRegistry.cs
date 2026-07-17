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
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors {

    /// <summary>
    /// Hard-coded, immutable registry of the four datasheet-derived Sony sensor definitions.
    /// All sensors share <see cref="QeCurve.SonyBsiVisible"/> for v1.
    /// </summary>
    public static class SensorRegistry {
        private static readonly IReadOnlyDictionary<SonySensorModel, SensorDefinition> Definitions = BuildDefinitions();

        /// <summary>All sensor definitions, in enum order.</summary>
        public static IReadOnlyList<SensorDefinition> All { get; } = Array.AsReadOnly(
            Enum.GetValues(typeof(SonySensorModel)).Cast<SonySensorModel>().Select(m => Definitions[m]).ToArray());

        /// <summary>Resolve the immutable definition for a sensor model.</summary>
        public static SensorDefinition Get(SonySensorModel model) {
            if (!Definitions.TryGetValue(model, out var def)) {
                throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown sensor model.");
            }
            return def;
        }

        private static IReadOnlyDictionary<SonySensorModel, SensorDefinition> BuildDefinitions() {
            var qe = QeCurve.SonyBsiVisible;
            var list = new[] {
                // Model, Name, W, H, pixel µm, bits, full well e⁻,
                //   QE, RN@gain0, HCG threshold, RN@HCG, RN@min, maxGain, dark I_ref e⁻/px/s, dark T_ref °C
                new SensorDefinition(SonySensorModel.IMX455, "IMX455", 9576, 6388, 3.76, 16, 50000.0,
                    qe, 3.5, 100, 1.5, 0.9, 300, 0.011, -10.0),
                new SensorDefinition(SonySensorModel.IMX571, "IMX571", 6248, 4176, 3.76, 16, 50000.0,
                    qe, 2.8, 100, 1.5, 1.0, 300, 0.0022, 0.0),
                new SensorDefinition(SonySensorModel.IMX533, "IMX533", 3008, 3008, 3.76, 14, 50000.0,
                    qe, 3.8, 100, 1.5, 1.0, 300, 0.002, 0.0),
                new SensorDefinition(SonySensorModel.IMX294, "IMX294", 4144, 2822, 4.63, 14, 66000.0,
                    qe, 7.0, 120, 1.3, 1.2, 400, 0.0022, -20.0),
            };
            return list.ToDictionary(d => d.Model);
        }
    }
}
