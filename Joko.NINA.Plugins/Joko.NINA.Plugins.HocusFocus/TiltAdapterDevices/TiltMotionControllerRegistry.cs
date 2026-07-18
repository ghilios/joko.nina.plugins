#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices {

    /// <summary>
    /// Maps a <c>TiltAdapterDevicePreset.Name</c> to a factory that builds the
    /// <see cref="ITiltMotionController"/> driving that device. This is the single place that decides
    /// which presets are "motorized" (i.e. can be connected to and automated) versus manual/screw presets,
    /// which have no entry here. To add a new motorized device: append one preset name → factory entry —
    /// no other code in this file changes (mirrors the "append one entry" pattern of
    /// <c>TiltAdapterDevicePreset.All</c>).
    /// </summary>
    public static class TiltMotionControllerRegistry {

        // Exact preset Name strings from TiltAdapterDevicePreset.cs — kept as named constants here (rather
        // than referenced live) so this file's entries are the single, grep-able source of "which presets
        // are motorized"; TiltMotionControllerRegistryTests guards these against drift by iterating
        // TiltAdapterDevicePreset.All and asserting every StepperMotors preset has an entry here.
        private const string AsgEat90mmName = "ASG Electronic EAT - 90mm";
        private const string AsgEatZwo461Name = "ASG Electronic EAT - ZWO 461";

        // Both current EAT presets share a single, identical driver (EatTiltMotionController) -- the two
        // presets differ only in screw radius/geometry (TiltAdapterDevicePreset.cs), not in serial protocol
        // or command vocabulary, so one factory suffices for both entries (T7).
        private static ITiltMotionController CreateEatController(ITiltAdapterOptions options) =>
            new EatTiltMotionController(options);

        private static readonly IReadOnlyDictionary<string, Func<ITiltAdapterOptions, ITiltMotionController>> Factories =
            new Dictionary<string, Func<ITiltAdapterOptions, ITiltMotionController>> {
                [AsgEat90mmName] = CreateEatController,
                [AsgEatZwo461Name] = CreateEatController,
            };

        /// <summary>True iff a motion controller factory is registered for the given device preset name.</summary>
        public static bool IsMotorized(string presetName) =>
            presetName != null && Factories.ContainsKey(presetName);

        /// <summary>
        /// Builds a new <see cref="ITiltMotionController"/> for the given device preset name. The returned
        /// controller is not yet connected — call <see cref="ITiltMotionController.ConnectAsync"/> to open
        /// the transport. <paramref name="options"/> is passed through so the driver can read its limit
        /// configuration (max steps per command, max excursion, settle seconds); the connection port is
        /// supplied separately to <see cref="ITiltMotionController.ConnectAsync"/>.
        /// </summary>
        public static ITiltMotionController Create(string presetName, ITiltAdapterOptions options) {
            if (presetName == null || !Factories.TryGetValue(presetName, out var factory)) {
                throw new ArgumentException($"No motion controller is registered for tilt-adapter device preset \"{presetName}\".", nameof(presetName));
            }
            return factory(options);
        }

        /// <summary>The device preset names this registry has a motion controller factory for.</summary>
        public static IReadOnlyCollection<string> MotorizedPresetNames { get; } =
            new ReadOnlyCollection<string>(Factories.Keys.ToArray());
    }
}
