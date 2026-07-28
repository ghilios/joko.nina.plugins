#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
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
        private readonly IProfileService profileService;

        public InFocusHfrRecord(IProfileService profileService)
            : this(CreateDefaultAccessor(profileService), profileService) {
        }

        internal InFocusHfrRecord(IPluginOptionsAccessor optionsAccessor, IProfileService profileService = null) {
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
            this.profileService = profileService;
            if (profileService != null) {
                HookActiveProfile();
                profileService.ProfileChanged += (s, e) => HookActiveProfile();
            }
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
        /// detector scales its outputs back before reporting them.
        ///
        /// <para>Normalizes any non-positive stored value to NaN: <see cref="Clear"/> writes 0 rather than NaN,
        /// because 0 round-trips through profile serialization unambiguously and NaN does not.</para></summary>
        public double HfrPixels {
            get {
                var stored = optionsAccessor.GetValueDouble(HfrKey, double.NaN);
                return stored > 0.0 ? stored : double.NaN;
            }
        }

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
            lastPixelScale = CurrentPixelScale();
            Logger.Debug($"Recorded in-focus HFR {hfrPixels:F2} px from {source}; it drives the detection binning recommendation");
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Discards the stored measurement, so the recommendation goes back to asking for an auto-focus rather than
        /// answering from a value that no longer describes the rig. A no-op when nothing is stored.
        /// </summary>
        public void Clear(string reason) {
            if (!HasMeasurement && !MeasuredAtUtc.HasValue) {
                return;
            }
            // 0, not NaN: NaN does not survive profile serialization cleanly, and HfrPixels normalizes any
            // non-positive value back to NaN on read.
            optionsAccessor.SetValueDouble(HfrKey, 0.0);
            optionsAccessor.SetValueInt64(MeasuredAtKey, 0L);
            Logger.Info($"Cleared the measured in-focus HFR ({reason}); the detection binning recommendation will ask for a new auto-focus");
            Changed?.Invoke(this, EventArgs.Empty);
        }

        #region Pixel-scale invalidation

        // The stored HFR is a star size in CAPTURED PIXELS, so it only means anything at the pixel scale it was
        // measured at. Change the focal length, the pixel size, or the capture binning and the very same sky makes
        // a different number of pixels — a 5.8 px measurement at 2800mm is 2.9 px at 1400mm, which flips the
        // recommendation from 2x2 to 1x1. Rather than silently recommending from a stale rig, drop the measurement
        // and say so; the next auto-focus refills it.
        private ITelescopeSettings watchedTelescope;
        private ICameraSettings watchedCamera;
        private IFocuserSettings watchedFocuser;
        private double lastPixelScale = double.NaN;

        /// <summary>
        /// Re-points the watch at the ACTIVE profile's settings objects. Called at construction and on every
        /// profile change, because a profile switch hands out different setting instances and the old ones would
        /// go on raising events nobody should act on.
        ///
        /// <para>Deliberately re-baselines WITHOUT clearing. The record is profile-scoped, so switching profiles
        /// already switches to that profile's own stored measurement — the pixel scale differing across two
        /// profiles is not a change to either one's optics.</para>
        /// </summary>
        private void HookActiveProfile() {
            if (watchedTelescope != null) { watchedTelescope.PropertyChanged -= OnPixelScaleInputChanged; }
            if (watchedCamera != null) { watchedCamera.PropertyChanged -= OnPixelScaleInputChanged; }
            if (watchedFocuser != null) { watchedFocuser.PropertyChanged -= OnPixelScaleInputChanged; }

            var profile = profileService?.ActiveProfile;
            watchedTelescope = profile?.TelescopeSettings;
            watchedCamera = profile?.CameraSettings;
            watchedFocuser = profile?.FocuserSettings;

            if (watchedTelescope != null) { watchedTelescope.PropertyChanged += OnPixelScaleInputChanged; }
            if (watchedCamera != null) { watchedCamera.PropertyChanged += OnPixelScaleInputChanged; }
            if (watchedFocuser != null) { watchedFocuser.PropertyChanged += OnPixelScaleInputChanged; }

            lastPixelScale = CurrentPixelScale();
        }

        /// <summary>
        /// The active profile's effective arcsec/pixel, or NaN when the rig is not described yet. Compared as a
        /// SCALE rather than as its three inputs on purpose: doubling both focal length and binning leaves star
        /// size in captured pixels exactly where it was, and there is no reason to make the user re-measure.
        /// </summary>
        private double CurrentPixelScale() {
            var profile = profileService?.ActiveProfile;
            if (profile == null) {
                return double.NaN;
            }
            var pixelSize = profile.CameraSettings?.PixelSize ?? double.NaN;
            var focalLength = profile.TelescopeSettings?.FocalLength ?? double.NaN;
            if (!(pixelSize > 0.0) || !(focalLength > 0.0)) {
                return double.NaN;
            }
            var captureBinning = Math.Max((short)1, profile.FocuserSettings?.AutoFocusBinning ?? (short)1);
            return MathUtility.ArcsecPerPixel(pixelSize, focalLength) * captureBinning;
        }

        private void OnPixelScaleInputChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            // Recompute rather than match property names: the comparison below is what decides, and this stays
            // correct if NINA renames a property. Setting a value to what it already was raises PropertyChanged
            // but does not move the scale, and must not cost the user their measurement.
            var current = CurrentPixelScale();
            if (SamePixelScale(current, lastPixelScale)) {
                return;
            }
            var previous = lastPixelScale;
            lastPixelScale = current;
            Clear($"pixel scale changed from {Describe(previous)} to {Describe(current)}");
        }

        private static string Describe(double pixelScale) =>
            double.IsNaN(pixelScale) ? "unset" : $"{pixelScale:F3}\"/px";

        private static bool SamePixelScale(double a, double b) {
            if (double.IsNaN(a) && double.IsNaN(b)) {
                return true;
            }
            if (double.IsNaN(a) || double.IsNaN(b)) {
                return false;
            }
            return Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Abs(b));
        }

        #endregion Pixel-scale invalidation
    }
}
