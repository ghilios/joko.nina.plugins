#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;

namespace TestApp {

    /// <summary>
    /// The headless harness's own settings store: a LOCAL JSON FILE, not the NINA profile.
    ///
    /// <para><b>Why this exists.</b> Every runner used to build its detector settings from the active NINA
    /// profile via <c>PluginOptionsAccessor</c>. That made a harness invocation depend on mutable, machine-local
    /// state nothing recorded — and it is not even stable within one session: two <c>optimize</c> runs launched
    /// seconds apart over the SAME saved run were seeded at Sensitivity 33.3333 and 15.6667 respectively, which
    /// silently invalidated a before/after comparison. A settings file makes a run reproducible, diffable, and
    /// safe to run concurrently.</para>
    ///
    /// <para><b>Bootstrap.</b> When no settings file exists, the profile's current values are exported to one
    /// ONCE and that file is used from then on. This keeps existing invocations working while making the values
    /// they depend on explicit and version-controllable — after the first run the profile is never read for
    /// settings again, so later profile edits cannot silently change harness results.</para>
    ///
    /// <para>The profile is still loaded, because NINA's image loaders need one to read XISF/FITS. It is no
    /// longer the source of DETECTOR SETTINGS or of the pixel-scale inputs.</para>
    /// </summary>
    internal static class HarnessSettingsStore {

        /// <summary>Default file name, resolved next to the TestApp executable so it travels with the harness.</summary>
        public const string DefaultFileName = "harness_settings.json";

        /// <summary>Serialized form: the raw plugin option key/value bag plus the pixel-scale inputs.</summary>
        internal sealed class HarnessSettingsFile {
            public int SchemaVersion { get; set; } = 1;

            /// <summary>Provenance only — which profile the values were first exported from, and when.</summary>
            public string ExportedFromProfile { get; set; }

            public DateTime ExportedAtUtc { get; set; }

            /// <summary>Pixel size in microns; feeds <c>PixelScale</c>. Previously <c>CameraSettings.PixelSize</c>.</summary>
            public double PixelSizeMicrons { get; set; }

            /// <summary>Focal length in mm; feeds <c>PixelScale</c>. Previously <c>TelescopeSettings.FocalLength</c>.</summary>
            public double FocalLengthMm { get; set; }

            /// <summary>Every plugin option key the detector reads, stored as strings exactly as the profile
            /// stores them, so this file is a faithful snapshot rather than a curated subset that could drift
            /// from whatever <c>StarDetectionOptions</c> happens to read next release.</summary>
            public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>How a per-dataset file's derived values were arrived at, so the file explains itself.</summary>
            public string DerivedNotes { get; set; }
        }

        /// <summary>
        /// An <see cref="IPluginOptionsAccessor"/> backed by an in-memory bag loaded from the settings file.
        /// Writes stay in memory (and are persisted only when the caller asks), so a harness run can never
        /// mutate the user's profile as a side effect — something the profile-backed accessor could do.
        /// </summary>
        internal sealed class FileOptionsAccessor : IPluginOptionsAccessor {
            private readonly Dictionary<string, string> values;

            public FileOptionsAccessor(Dictionary<string, string> values) {
                this.values = values ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }

            public bool SuppressWrites { get; set; }

            /// <summary>A copy of the underlying bag, for seeding a per-dataset file from a shared base.</summary>
            public Dictionary<string, string> Snapshot() => new Dictionary<string, string>(values, StringComparer.Ordinal);

            private T Get<T>(string key, T defaultValue, Func<string, T> parse) {
                if (key == null || !values.TryGetValue(key, out var raw) || string.IsNullOrEmpty(raw)) {
                    return defaultValue;
                }
                try {
                    return parse(raw);
                } catch {
                    return defaultValue;
                }
            }

            private void Set(string key, string raw) {
                if (SuppressWrites || key == null) {
                    return;
                }
                values[key] = raw;
            }

            public bool GetValueBoolean(string key, bool defaultValue) => Get(key, defaultValue, bool.Parse);

            public double GetValueDouble(string key, double defaultValue) =>
                Get(key, defaultValue, s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));

            public float GetValueSingle(string key, float defaultValue) =>
                Get(key, defaultValue, s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));

            public int GetValueInt32(string key, int defaultValue) =>
                Get(key, defaultValue, s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture));

            public int GetValueInt(string key, int defaultValue) => GetValueInt32(key, defaultValue);

            public string GetValueString(string key, string defaultValue) => Get(key, defaultValue, s => s);

            public DateTime GetValueDateTime(string key, DateTime defaultValue) =>
                Get(key, defaultValue, s => DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture));

            public Guid GetValueGuid(string key, Guid defaultValue) => Get(key, defaultValue, Guid.Parse);

            public T GetValueEnum<T>(string key, T defaultValue) where T : struct, Enum =>
                Get(key, defaultValue, s => (T)Enum.Parse(typeof(T), s, ignoreCase: true));

            public void SetValueBoolean(string key, bool value) => Set(key, value.ToString());

            public void SetValueDouble(string key, double value) =>
                Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            public void SetValueSingle(string key, float value) =>
                Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            public void SetValueInt32(string key, int value) =>
                Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            public void SetValueInt(string key, int value) => SetValueInt32(key, value);

            public void SetValueString(string key, string value) => Set(key, value);

            public void SetValueDateTime(string key, DateTime value) =>
                Set(key, value.ToString("o", System.Globalization.CultureInfo.InvariantCulture));

            public void SetValueGuid(string key, Guid value) => Set(key, value.ToString());

            public void SetValueEnum<T>(string key, T value) where T : struct, Enum => Set(key, value.ToString());

            public void RemoveValue(string key) {
                if (!SuppressWrites && key != null) {
                    values.Remove(key);
                }
            }

            // The remaining IPluginOptionsAccessor surface. The detector reads none of these, but the interface
            // requires them and a harness must never throw on a key it simply does not care about — so they round
            // trip through the same string bag as everything else rather than being NotImplementedException stubs.
            private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

            public byte GetValueByte(string key, byte defaultValue) => Get(key, defaultValue, s => byte.Parse(s, Inv));

            public void SetValueByte(string key, byte value) => Set(key, value.ToString(Inv));

            public sbyte GetValueSByte(string key, sbyte defaultValue) => Get(key, defaultValue, s => sbyte.Parse(s, Inv));

            public void SetValueSByte(string key, sbyte value) => Set(key, value.ToString(Inv));

            public char GetValueChar(string key, char defaultValue) => Get(key, defaultValue, char.Parse);

            public void SetValueChar(string key, char value) => Set(key, value.ToString());

            public decimal GetValueDecimal(string key, decimal defaultValue) => Get(key, defaultValue, s => decimal.Parse(s, Inv));

            public void SetValueDecimal(string key, decimal value) => Set(key, value.ToString(Inv));

            public System.Windows.Media.Color GetValueColor(string key, System.Windows.Media.Color defaultValue) =>
                Get(key, defaultValue, s => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s));

            public void SetValueColor(string key, System.Windows.Media.Color value) => Set(key, value.ToString());

            public uint GetValueUInt32(string key, uint defaultValue) => Get(key, defaultValue, s => uint.Parse(s, Inv));

            public void SetValueUInt32(string key, uint value) => Set(key, value.ToString(Inv));

            public short GetValueInt16(string key, short defaultValue) => Get(key, defaultValue, s => short.Parse(s, Inv));

            public void SetValueInt16(string key, short value) => Set(key, value.ToString(Inv));

            public ushort GetValueUInt16(string key, ushort defaultValue) => Get(key, defaultValue, s => ushort.Parse(s, Inv));

            public void SetValueUInt16(string key, ushort value) => Set(key, value.ToString(Inv));

            public long GetValueInt64(string key, long defaultValue) => Get(key, defaultValue, s => long.Parse(s, Inv));

            public void SetValueInt64(string key, long value) => Set(key, value.ToString(Inv));

            public ulong GetValueUInt64(string key, ulong defaultValue) => Get(key, defaultValue, s => ulong.Parse(s, Inv));

            public void SetValueUInt64(string key, ulong value) => Set(key, value.ToString(Inv));
        }

        /// <summary>The resolved settings for a harness run.</summary>
        internal sealed class Resolved {
            public IPluginOptionsAccessor Accessor { get; set; }
            public double PixelSizeMicrons { get; set; }
            public double FocalLengthMm { get; set; }
            public string Path { get; set; }
            public bool WasBootstrapped { get; set; }
        }

        /// <summary>
        /// PixelScale (arcsec/binned-pixel) for a frame, taken FROM THE FRAME'S OWN HEADER wherever possible.
        ///
        /// <para><b>Why not the profile.</b> The AF bank is other people's data. Their pixel size and focal
        /// length are recorded in their frames (FITS <c>XPIXSZ</c>/<c>FOCALLEN</c>, and the XISF equivalents,
        /// both of which NINA's loaders populate onto <c>MetaData</c>); the local profile describes a completely
        /// different rig. Measured across the bank the true scales span <b>0.74 to 5.97 arcsec/px</b> — timmer at
        /// 130 mm against toml999 at 1060 mm — while a profile-sourced value applied ONE number to all of them,
        /// so every PixelScale-dependent gate was evaluated at the wrong scale on nearly every run.</para>
        ///
        /// <para>Falls back to the settings file only when a frame carries no usable header, and returns NaN when
        /// neither has one — the same NaN the profile path produced, so a frame with no scale information is not
        /// silently given a fabricated one. <paramref name="source"/> reports which of the three applied, so a
        /// runner can say so rather than leaving the provenance implicit.</para>
        /// </summary>
        public static double PixelScaleForFrame(NINA.Image.ImageData.ImageMetaData meta, Resolved settings, out string source) {
            var metaPixelSize = meta?.Camera?.PixelSize ?? double.NaN;
            var metaFocalLength = meta?.Telescope?.FocalLength ?? double.NaN;
            var binX = meta?.Camera?.BinX ?? 1;
            var binning = Math.Max(double.IsNaN(binX) ? 1 : binX, 1);

            if (metaPixelSize > 0.0 && metaFocalLength > 0.0) {
                source = "frame header";
                return NINA.Joko.Plugins.HocusFocus.Utility.MathUtility.ArcsecPerPixel(metaPixelSize, metaFocalLength) * binning;
            }
            if (settings != null && settings.PixelSizeMicrons > 0.0 && settings.FocalLengthMm > 0.0) {
                source = $"settings file ({settings.Path}) — frame header carried no pixel size / focal length";
                return NINA.Joko.Plugins.HocusFocus.Utility.MathUtility.ArcsecPerPixel(
                    settings.PixelSizeMicrons, settings.FocalLengthMm) * binning;
            }
            source = "unavailable (neither the frame header nor the settings file has pixel size + focal length)";
            return double.NaN;
        }

        /// <summary>
        /// Per-dataset settings, written beside the run as <see cref="DefaultFileName"/>.
        ///
        /// <para><b>Why per dataset.</b> The bank is other people's data and a single shared bundle cannot
        /// describe it: pixel scale alone spans <b>0.66–5.97 arcsec/px</b> (FlyData at 2350 mm against timmer at
        /// 130 mm), and the detection-binning factor that puts a run inside the detector's calibrated 2–4 px HFR
        /// band is a property of that run's optics and seeing, not of whoever ran the harness. Deriving both from
        /// the dataset — and RECORDING the result next to it — makes a run reproducible and lets a human correct
        /// a bad derivation by editing one file rather than by changing global state.</para>
        ///
        /// <para>An existing per-run file is used AS-IS and never re-derived: once a human has looked at a
        /// dataset and set its values, a later harness run must not silently overwrite that judgement.</para>
        /// </summary>
        /// <param name="runDir">Folder holding the run's frames; the settings file is written here.</param>
        /// <param name="baseSettings">Shared base — everything not derived per run comes from this.</param>
        /// <param name="firstFrameMeta">First frame's metadata, for the pixel-scale inputs.</param>
        /// <param name="inFocusHfrPixels">The run's fitted in-focus HFR, for the binning derivation. NaN ⇒ the
        /// base setting's binning is kept, since a binning guess with no HFR behind it is not a derivation.</param>
        public static Resolved ResolveForRun(
            string runDir, Resolved baseSettings, NINA.Image.ImageData.ImageMetaData firstFrameMeta, double inFocusHfrPixels) {
            var path = Path.Combine(runDir, DefaultFileName);
            if (File.Exists(path)) {
                var existing = JsonConvert.DeserializeObject<HarnessSettingsFile>(File.ReadAllText(path))
                    ?? new HarnessSettingsFile();
                Console.WriteLine($"  settings: {path} (existing, not re-derived)");
                return new Resolved {
                    Accessor = new FileOptionsAccessor(existing.Options),
                    PixelSizeMicrons = existing.PixelSizeMicrons,
                    FocalLengthMm = existing.FocalLengthMm,
                    Path = path,
                    WasBootstrapped = false
                };
            }

            // Start from the shared base so only the genuinely per-dataset knobs differ from it.
            var options = new Dictionary<string, string>(
                (baseSettings?.Accessor as FileOptionsAccessor)?.Snapshot() ?? new Dictionary<string, string>(),
                StringComparer.Ordinal);
            var file = new HarnessSettingsFile {
                ExportedFromProfile = $"derived from dataset ({Path.GetFileName(Path.GetDirectoryName(runDir)) ?? runDir})",
                ExportedAtUtc = DateTime.UtcNow,
                PixelSizeMicrons = firstFrameMeta?.Camera?.PixelSize ?? (baseSettings?.PixelSizeMicrons ?? 0.0),
                FocalLengthMm = firstFrameMeta?.Telescope?.FocalLength ?? (baseSettings?.FocalLengthMm ?? 0.0),
                Options = options
            };

            var notes = new List<string>();
            if (double.IsFinite(inFocusHfrPixels) && inFocusHfrPixels > 0.0) {
                var factor = NINA.Joko.Plugins.HocusFocus.Utility.DetectionBinningResolver.RecommendFromHfr(inFocusHfrPixels);
                var setting = NINA.Joko.Plugins.HocusFocus.Utility.DetectionBinningResolver.ToSetting(factor);
                options["DetectionBinning"] = setting.ToString();
                notes.Add($"DetectionBinning={setting} from in-focus HFR {inFocusHfrPixels:0.00}px " +
                    $"(target {NINA.Joko.Plugins.HocusFocus.Utility.DetectionBinningResolver.TargetHfrPixels:0.0}px)");
            } else {
                notes.Add("DetectionBinning kept from base (no fitted in-focus HFR for this dataset)");
            }
            notes.Add(file.PixelSizeMicrons > 0 && file.FocalLengthMm > 0
                ? $"PixelScale inputs {file.PixelSizeMicrons:0.00}um / {file.FocalLengthMm:0.#}mm from the frame header"
                : "PixelScale inputs unavailable from the frame header; base values kept");
            file.DerivedNotes = string.Join("; ", notes);

            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
            Console.WriteLine($"  settings: derived {path} — {file.DerivedNotes}");
            return new Resolved {
                Accessor = new FileOptionsAccessor(file.Options),
                PixelSizeMicrons = file.PixelSizeMicrons,
                FocalLengthMm = file.FocalLengthMm,
                Path = path,
                WasBootstrapped = true
            };
        }

        /// <summary>
        /// The fitted in-focus HFR (px) from a run's <c>autofocus_report_Region0.json</c>, or NaN. This is the
        /// SAME quantity DetectionBinningResolver expects: the AF curve's minimum, in the pixel space the run was
        /// actually detected in.
        /// </summary>
        public static double ReadInFocusHfr(string runFolder) {
            var path = Path.Combine(runFolder ?? string.Empty, "autofocus_report_Region0.json");
            if (!File.Exists(path)) {
                return double.NaN;
            }
            try {
                var o = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(File.ReadAllText(path));
                var v = o?["CalculatedFocusPoint"]?["Value"];
                return v == null ? double.NaN : (double)v;
            } catch {
                return double.NaN;
            }
        }

        /// <summary>Default settings path: next to the TestApp executable.</summary>
        public static string DefaultPath() =>
            Path.Combine(AppContext.BaseDirectory ?? Directory.GetCurrentDirectory(), DefaultFileName);

        /// <summary>
        /// Resolves the harness settings for this run. Order: <c>--settings &lt;path&gt;</c>, else the default
        /// path, else bootstrap one from <paramref name="activeProfile"/> and use that. Prints which file was
        /// used, so a run's provenance is visible in its own log rather than implied.
        /// </summary>
        public static Resolved Resolve(string[] args, IProfileService profileService, IProfile activeProfile) =>
            ResolveAt(SettingsPathArg(args), profileService, activeProfile);

        /// <summary>The <c>--settings</c> value, read inline rather than through the harness's CLI helper so this
        /// store stays dependency-free and unit-testable on its own.</summary>
        private static string SettingsPathArg(string[] args) {
            for (var i = 0; args != null && i < args.Length - 1; i++) {
                if (string.Equals(args[i], "--settings", StringComparison.OrdinalIgnoreCase)) {
                    return args[i + 1];
                }
            }
            return null;
        }

        /// <summary>
        /// <see cref="Resolve"/> with the path already extracted — the arg-free core, so the store carries no
        /// dependency on the harness's CLI helpers and can be unit tested on its own.
        /// </summary>
        public static Resolved ResolveAt(string explicitPath, IProfileService profileService, IProfile activeProfile) {
            var path = string.IsNullOrWhiteSpace(explicitPath) ? DefaultPath() : explicitPath;

            if (File.Exists(path)) {
                var loaded = JsonConvert.DeserializeObject<HarnessSettingsFile>(File.ReadAllText(path))
                    ?? new HarnessSettingsFile();
                Console.WriteLine($"Settings: {path} (exported {loaded.ExportedAtUtc:u} from profile '{loaded.ExportedFromProfile}')");
                return new Resolved {
                    Accessor = new FileOptionsAccessor(loaded.Options),
                    PixelSizeMicrons = loaded.PixelSizeMicrons,
                    FocalLengthMm = loaded.FocalLengthMm,
                    Path = path,
                    WasBootstrapped = false
                };
            }

            if (!string.IsNullOrWhiteSpace(explicitPath)) {
                throw new FileNotFoundException($"Harness settings file not found: {path}", path);
            }

            var file = ExportFromProfile(profileService, activeProfile);
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
            Console.WriteLine($"Settings: bootstrapped {path} from profile '{activeProfile?.Name}'. " +
                "Later runs read this file; the profile is not consulted for settings again.");
            return new Resolved {
                Accessor = new FileOptionsAccessor(file.Options),
                PixelSizeMicrons = file.PixelSizeMicrons,
                FocalLengthMm = file.FocalLengthMm,
                Path = path,
                WasBootstrapped = true
            };
        }

        /// <summary>
        /// Snapshots the profile's plugin options + pixel-scale inputs. Reads through the REAL accessor so the
        /// exported values are exactly what the profile-backed path would have produced — the bootstrap must be
        /// behaviour-preserving, or the first run after this change would silently differ from the last one
        /// before it.
        /// </summary>
        internal static HarnessSettingsFile ExportFromProfile(IProfileService profileService, IProfile activeProfile) {
            var file = new HarnessSettingsFile {
                ExportedFromProfile = activeProfile?.Name,
                ExportedAtUtc = DateTime.UtcNow,
                PixelSizeMicrons = activeProfile?.CameraSettings?.PixelSize ?? 0.0,
                FocalLengthMm = activeProfile?.TelescopeSettings?.FocalLength ?? 0.0
            };

            var guid = PluginOptionsAccessor.GetAssemblyGuid(
                typeof(NINA.Joko.Plugins.HocusFocus.StarDetection.StarDetectionOptions));
            if (guid == null) {
                return file;
            }

            // Copy through StarDetectionOptions' OWN PROPERTY SURFACE rather than reading raw keys. The profile
            // stores plugin options as typed values with no "list my keys" API, so a raw read has to guess both
            // the key list and each key's type -- and a miss is silent: the loaded options fall back to the
            // option's DEFAULT, which looks like a perfectly valid run. Assigning property-by-property makes the
            // options class itself emit the right keys with the right types, so the snapshot cannot drift from
            // what the class actually reads, and a newly added option is captured with no change here.
            var fromProfile = new NINA.Joko.Plugins.HocusFocus.StarDetection.StarDetectionOptions(
                profileService, new PluginOptionsAccessor(profileService, guid.Value));
            var toFile = new NINA.Joko.Plugins.HocusFocus.StarDetection.StarDetectionOptions(
                profileService, new FileOptionsAccessor(file.Options));

            foreach (var prop in typeof(NINA.Joko.Plugins.HocusFocus.StarDetection.StarDetectionOptions).GetProperties()) {
                if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length > 0) {
                    continue;
                }
                try {
                    var value = prop.GetValue(fromProfile);
                    // Write a DIFFERENT value first so the option's change-detecting setter actually fires.
                    // Without this the snapshot is SPARSE: a value equal to the shipped default never reaches the
                    // file, and the file then inherits whatever the default IS AT LOAD TIME. That is
                    // behaviour-preserving today and quietly wrong the day a default changes — precisely the
                    // silent drift a settings file exists to prevent. Density costs one extra write per property.
                    var poison = Poison(value, prop.PropertyType);
                    if (poison != null) {
                        prop.SetValue(toFile, poison);
                    }
                    prop.SetValue(toFile, value);
                } catch {
                    // A property that refuses a round trip (computed, validated, or profile-coupled) is not a
                    // detector knob we can carry; skip it rather than abort the whole export.
                }
            }
            return file;
        }

        /// <summary>
        /// A value of <paramref name="type"/> guaranteed to differ from <paramref name="value"/>, used only to
        /// trip a change-detecting setter. Null when no such value can be produced, in which case the property is
        /// written once and may stay absent from the file (see <see cref="ExportFromProfile"/>).
        /// </summary>
        private static object Poison(object value, Type type) {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t == typeof(bool)) {
                return !(bool)(value ?? false);
            }
            if (t == typeof(string)) {
                return (value as string) == "~" ? "~~" : "~";
            }
            if (t.IsEnum) {
                foreach (var candidate in Enum.GetValues(t)) {
                    if (!Equals(candidate, value)) {
                        return candidate;
                    }
                }
                return null;
            }
            if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)
                || t == typeof(int) || t == typeof(long) || t == typeof(short)
                || t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort)) {
                try {
                    // +1 in the property's own type. Doubles that are NaN compare unequal to everything, so any
                    // finite value already differs; 0 is a safe, in-range choice for every numeric knob here.
                    var current = Convert.ToDouble(value ?? 0);
                    return Convert.ChangeType(double.IsNaN(current) ? 0.0 : current + 1.0, t);
                } catch {
                    return null;
                }
            }
            if (t == typeof(DateTime)) {
                return ((DateTime)(value ?? DateTime.UnixEpoch)).AddDays(1);
            }
            return null;
        }
    }
}
