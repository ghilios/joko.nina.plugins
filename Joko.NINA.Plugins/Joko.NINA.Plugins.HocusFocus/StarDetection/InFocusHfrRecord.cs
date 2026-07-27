#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using Logger = NINA.Core.Utility.Logger;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// The last MEASURED in-focus HFR for the active profile, in captured pixels, with when it was measured.
    ///
    /// <para>This exists so the detection-binning recommendation can be built from data instead of an assumption.
    /// The earlier version estimated in-focus star size from pixel scale under an assumed seeing figure, and that
    /// cannot work: plausible seeing spans roughly 1.5" to 4", a factor of 2.7, which is wider than the entire
    /// 1x1-vs-2x2 decision margin. On a 0.28"/px rig the recommendation flips at about 2.3" of seeing — inside
    /// ordinary night-to-night variation — so an assumed figure decides the answer rather than the rig does.</para>
    ///
    /// <para>Two writers, and they do NOT agree on provenance. An auto-focus run writes its FINAL HFR: a real
    /// exposure taken at the focuser position the run settled on. Accepting a live sweep in the optimization
    /// wizard writes that run's FITTED curve minimum instead, gated on R² ≥ 0.9. The gate is the reason the
    /// second source is admissible at all: on a sweep wide enough that the detector loses the defocused donuts,
    /// the outer frames report a couple of compact noise blobs and drag the vertex far below truth (measured on
    /// the simulator: 2.16 px against a 4.87 px optics truth, with R² negative). A real exposure needs no such
    /// gate, so prefer it — never relax the R² gate to make the wizard path fire more often.</para>
    ///
    /// <para>Profile-scoped, like every other plugin option. Detection binning follows the optics, so a single
    /// value per profile is the right granularity.</para>
    /// </summary>
    public class InFocusHfrRecord {
        private const string HfrKey = "LastInFocusHfrPixels";
        private const string MeasuredAtKey = "LastInFocusHfrMeasuredAtUtc";

        private readonly IPluginOptionsAccessor optionsAccessor;

        public InFocusHfrRecord(IProfileService profileService)
            : this(CreateDefaultAccessor(profileService)) {
        }

        internal InFocusHfrRecord(IPluginOptionsAccessor optionsAccessor) {
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
        }

        private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions))
                ?? throw new Exception("Guid not found in assembly metadata");
            return new PluginOptionsAccessor(profileService, guid);
        }

        /// <summary>Raised after a new measurement is stored, so a bound recommendation can refresh. May fire on a
        /// detection/auto-focus thread — subscribers marshal if they touch the UI.</summary>
        public event EventHandler Changed;

        /// <summary>The last measured in-focus HFR in CAPTURED pixels, or NaN when nothing has been measured. It is
        /// always in captured pixels regardless of the detection binning in effect at the time, because the
        /// detector scales its outputs back before reporting them.</summary>
        public double HfrPixels => optionsAccessor.GetValueDouble(HfrKey, double.NaN);

        /// <summary>When <see cref="HfrPixels"/> was measured (UTC), or null when nothing has been measured.</summary>
        public DateTime? MeasuredAtUtc {
            get {
                var ticks = optionsAccessor.GetValueInt64(MeasuredAtKey, 0L);
                return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : (DateTime?)null;
            }
        }

        public bool HasMeasurement => double.IsFinite(HfrPixels) && HfrPixels > 0.0;

        /// <summary>
        /// Stores a measured in-focus HFR. Non-finite or non-positive values are ignored rather than stored as a
        /// bad recommendation: a failed measurement must leave the previous good one in place, not replace it.
        /// </summary>
        public void Record(double hfrPixels, DateTime measuredAtUtc, string source) {
            if (!double.IsFinite(hfrPixels) || hfrPixels <= 0.0) {
                return;
            }
            optionsAccessor.SetValueDouble(HfrKey, hfrPixels);
            optionsAccessor.SetValueInt64(MeasuredAtKey, measuredAtUtc.ToUniversalTime().Ticks);
            Logger.Debug($"Recorded in-focus HFR {hfrPixels:F2} px from {source}; it drives the detection binning recommendation");
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
