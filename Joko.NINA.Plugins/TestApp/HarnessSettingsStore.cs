#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        /// <summary>Default file name.</summary>
        public const string DefaultFileName = "harness_settings.json";

        /// <summary>Folder under the user's local app data holding the SHARED harness settings — see <see cref="DefaultPath"/>.</summary>
        public const string SharedFolderName = "HocusFocusHarness";

        /// <summary><see cref="HarnessSettingsFile.DetectionBinningSource"/> when the run's own fit produced it.</summary>
        public const string DetectionBinningDerived = "derived-from-in-focus-hfr";

        /// <summary><see cref="HarnessSettingsFile.DetectionBinningSource"/> when it was inherited, not derived (F39).</summary>
        public const string DetectionBinningKeptFromBase = "kept-from-base";

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

            /// <summary>
            /// Where this file's <c>DetectionBinning</c> came from — <c>derived-from-in-focus-hfr</c> or
            /// <c>kept-from-base</c> (F39).
            ///
            /// <para>Every synthetic-bank file records <c>Bin2</c>, and not one of those runs derived it: the
            /// derivation needs a fitted in-focus HFR from an <c>autofocus_report_Region0.json</c> that no bank
            /// folder has, so all of them silently inherited the value from a profile export. A field that LOOKS
            /// derived and is not is worse than an absent one, and <see cref="DerivedNotes"/> saying so in prose is
            /// not something a reader diffs. This makes the provenance a value.</para>
            ///
            /// <para>Note the separate half of F39, deliberately NOT addressed here: the headless runners discard
            /// <see cref="ResolveForRun"/>'s result, so this key is not what the run detected at either way.</para>
            /// </summary>
            public string DetectionBinningSource { get; set; }
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
        /// The ONE place a harness runner builds <see cref="NINA.Joko.Plugins.HocusFocus.AutoFocus.AutoFocusOptions"/>
        /// — bound to the pinned settings FILE, never to the active NINA profile.
        ///
        /// <para><b>F58(d).</b> Every harness runner built <c>StarDetectionOptions</c> on this accessor and then
        /// <c>new AutoFocusOptions(profileService)</c> two lines below it, so the DETECTOR was pinned by
        /// <c>--settings</c> and the FIT was not. Four of <c>AutoFocusOptions</c>' values reach the AF fit, this
        /// machine's nine profiles partition <b>2 / 7</b> on <c>MaxOutlierRejections</c> alone, and because NINA
        /// holds a <c>.profile</c> open while it is loaded (and <c>TryLoad</c> silently skips a locked one for the
        /// next by <c>LastUsed</c>), concurrent harness processes each acquire a DIFFERENT profile. Wave 11 fixed
        /// <c>optimize</c>; the other five call sites kept the defect for a wave because each owed its own
        /// validation.</para>
        ///
        /// <para><b>A helper rather than five copies of one line</b>, for the same reason
        /// <see cref="HarnessFitInputs"/> is one type used both to build the fit and to render the provenance
        /// field: a rule enforced in five places is a rule that comes back. The sixth construction site —
        /// <c>HocusFocusPlugin.cs</c> — deliberately does NOT call this and must not: in the live app the profile
        /// IS the user's settings.</para>
        /// </summary>
        internal static NINA.Joko.Plugins.HocusFocus.AutoFocus.AutoFocusOptions BuildFitOptions(
            IProfileService profileService, Resolved resolved) {
            if (resolved?.Accessor == null) {
                throw new ArgumentNullException(nameof(resolved), "A harness runner must build its fit from RESOLVED settings, not from the active profile (F58(d)).");
            }
            return new NINA.Joko.Plugins.HocusFocus.AutoFocus.AutoFocusOptions(profileService, resolved.Accessor);
        }

        /// <summary>
        /// Stable fingerprint of the settings a run was ACTUALLY driven by (F30): the resolved pixel-scale inputs
        /// plus the settings file's own contents. Hashes the file's SEMANTIC content, not its bytes — a settings
        /// file that is re-exported or re-indented must not make a landing look as though it came from a different
        /// configuration.
        ///
        /// <para>Returns null when there is nothing to fingerprint, which reads as "unknown" downstream rather
        /// than as a match. Never throws: a fingerprint is diagnostic metadata, and failing to compute one must
        /// not abort an optimize run that is otherwise fine.</para>
        /// </summary>
        public static string Fingerprint(Resolved resolved) {
            if (resolved == null) {
                return null;
            }
            try {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var sb = new System.Text.StringBuilder();
                // '#'-prefixed so these can never collide with an option key of the same name.
                sb.Append("#pixelSizeMicrons=").Append(resolved.PixelSizeMicrons.ToString("R", inv)).Append('\n');
                sb.Append("#focalLengthMm=").Append(resolved.FocalLengthMm.ToString("R", inv)).Append('\n');
                if (!string.IsNullOrEmpty(resolved.Path) && File.Exists(resolved.Path)) {
                    // The file's parsed option bag, key-sorted, rather than its raw text: whitespace, key order and
                    // the export-provenance fields (ExportedAtUtc / ExportedFromProfile / DerivedNotes) all move
                    // without changing a single detection.
                    var parsed = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(resolved.Path));
                    var options = parsed["Options"] as Newtonsoft.Json.Linq.JObject;
                    if (options != null) {
                        foreach (var prop in options.Properties().OrderBy(x => x.Name, StringComparer.Ordinal)) {
                            sb.Append(prop.Name).Append('=').Append(prop.Value?.ToString(Newtonsoft.Json.Formatting.None)).Append('\n');
                        }
                    }
                }
                using var sha = System.Security.Cryptography.SHA256.Create();
                var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
                // 12 hex chars: enough to distinguish the handful of configurations a bank session uses, short
                // enough to read in a console line.
                return BitConverter.ToString(hash, 0, 6).Replace("-", string.Empty).ToLowerInvariant();
            } catch (Exception) {
                return null;
            }
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
                file.DetectionBinningSource = DetectionBinningDerived;
                notes.Add($"DetectionBinning={setting} from in-focus HFR {inFocusHfrPixels:0.00}px " +
                    $"(target {NINA.Joko.Plugins.HocusFocus.Utility.DetectionBinningResolver.TargetHfrPixels:0.0}px)");
            } else {
                // F39 — say it as a FIELD, not only in prose. Every synthetic-bank file took this branch and every
                // one of them reads "Bin2" next to sixteen genuinely-exported values, which is indistinguishable
                // from a derivation on inspection.
                file.DetectionBinningSource = DetectionBinningKeptFromBase;
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
        /// F39(b) — the detection-binning FACTOR a run should actually be DETECTED at, and where that answer came
        /// from. Used by <c>optimize --apply-run-detection-binning</c>; the factor itself is applied through
        /// <c>DetectionBinningResolver.ApplyFactor</c> so <c>PixelScale</c> stays consistent with it.
        ///
        /// <para><b>Precedence, and why.</b> The dataset's own <c>synthetic_meta.json</c>
        /// <c>expectedOptimal.detectionBinning</c> wins, because it is PHYSICS-DERIVED — and, for the seven
        /// <c>detectionBinning = 2</c> datasets, the value their EXPOSURES were already derived at
        /// (<c>expectedOptimal.exposureDefinition</c> says so verbatim). The harness settings file is the fallback,
        /// and for all seventeen synthetic datasets its value is INHERITED rather than derived
        /// (<see cref="HarnessSettingsFile.DetectionBinningSource"/> = <see cref="DetectionBinningKeptFromBase"/>,
        /// the field wave 6 added so exactly this is readable). Honouring an inherited guess silently is the defect
        /// F39 is about, so <paramref name="source"/> always says which one was used and the caller prints it.</para>
        ///
        /// <para>Never throws: an unreadable meta file falls through to the settings file, and a run with neither
        /// resolves to 1 — the value every run has used to date.</para>
        /// </summary>
        /// <param name="runDir">The run (attempt) folder; the dataset's meta sits in its PARENT.</param>
        public static int ResolveRunDetectionBinningFactor(string runDir, Resolved resolvedForRun, out string source) {
            var datasetDir = Path.GetDirectoryName(runDir ?? string.Empty);
            var metaPath = Path.Combine(datasetDir ?? string.Empty, "synthetic_meta.json");
            if (File.Exists(metaPath)) {
                try {
                    var meta = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(File.ReadAllText(metaPath));
                    var value = meta?["expectedOptimal"]?["detectionBinning"];
                    if (value != null) {
                        source = "synthetic_meta.json expectedOptimal.detectionBinning (physics-derived)";
                        return NINA.Joko.Plugins.HocusFocus.Utility.DetectionBinningResolver.ToFactor(
                            NINA.Joko.Plugins.HocusFocus.Utility.DetectionBinningResolver.ToSetting((int)value));
                    }
                } catch (Exception ex) {
                    Console.Error.WriteLine($"  detection binning (F39b): could not read {metaPath} ({ex.Message}); "
                        + "falling back to the harness settings file");
                }
            }
            var setting = resolvedForRun?.Accessor?.GetValueEnum("DetectionBinning", DetectionBinningEnum.Bin1)
                ?? DetectionBinningEnum.Bin1;
            source = $"harness_settings.json (may be kept-from-base -- check DetectionBinningSource): {setting}";
            return NINA.Joko.Plugins.HocusFocus.Utility.DetectionBinningResolver.ToFactor(setting);
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

        /// <summary>The settings file beside the TestApp executable — where the default used to live outright.</summary>
        public static string BesideExePath() =>
            Path.Combine(AppContext.BaseDirectory ?? Directory.GetCurrentDirectory(), DefaultFileName);

        /// <summary>The per-user SHARED settings file, which every build directory resolves to alike.</summary>
        public static string SharedDefaultPath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SharedFolderName, DefaultFileName);

        /// <summary>
        /// Default settings path when no <c>--settings</c> is given.
        ///
        /// <para><b>F42.</b> This used to be "next to the exe", unconditionally. The AF-bank run instructions
        /// require each arm to be built to its own <c>-o</c> directory (the exe is file-locked while a run is in
        /// progress), and an absent file is BOOTSTRAPPED from the live NINA profile — so every new build directory
        /// silently got its own detector, and two arms built minutes apart could differ in
        /// <c>UseOptimizedSettings</c>, <c>LocallyAdaptiveBinarization</c> and the pixel-scale inputs. That is a
        /// confound the workflow GUARANTEES rather than merely permits.</para>
        ///
        /// <para>So the per-user file is now the default, and a fresh build directory INHERITS it instead of
        /// bootstrapping a new one. A file deliberately placed beside the exe still wins, so an existing arm
        /// directory keeps behaving exactly as it did.</para>
        /// </summary>
        public static string DefaultPath() => ResolveDefaultPath(BesideExePath(), SharedDefaultPath(), File.Exists);

        /// <summary>The <see cref="DefaultPath"/> decision as a pure function, so it is testable without a filesystem.</summary>
        internal static string ResolveDefaultPath(string besideExe, string shared, Func<string, bool> exists) =>
            exists(besideExe) ? besideExe : shared;

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
                WarnIfSimpleModeOverridesTheFile(loaded, path, profileService);
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
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
            // F42 — this is the exact moment an arm silently stops being comparable to its predecessor: the values
            // come from whatever the ACTIVE profile happens to hold right now, and nothing else records that. Loud
            // on stderr, not an informational line in a log nobody reads until the numbers disagree.
            Console.Error.WriteLine(
                $"WARNING: no harness settings file existed, so one was BOOTSTRAPPED at {path} from the live NINA " +
                $"profile '{activeProfile?.Name}'. These values are whatever that profile holds right now — this run " +
                "is NOT comparable to any earlier arm that used a different file. Pass --settings <path> to pin one " +
                "file across every arm of a comparison.");
            Logger.Warning($"HarnessSettingsStore bootstrapped {path} from profile '{activeProfile?.Name}'; " +
                "cross-arm comparability is not guaranteed unless --settings pins one file.");
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
        /// Warns when a pinned settings file records advanced detector knobs the options class will simply
        /// overwrite.
        ///
        /// <para><b>Why.</b> <c>StarDetectionOptions.InitializeOptions</c> ends in <c>ConfigureSimpleSettings()</c>,
        /// which — when <c>UseAdvanced</c> is false — calls <c>DerivePresetSettings()</c> and recomputes roughly
        /// sixteen advanced knobs (<c>NoiseClippingMultiplier</c>, <c>StarClippingMultiplier</c>,
        /// <c>StructureLayers</c>, <c>BrightnessSensitivity</c>, <c>MinHFR</c>, <c>MaxDistortion</c>, …) from the
        /// three <c>Simple_*</c> presets. So an arm that edits one of those keys in its settings file changes
        /// nothing, and the run looks entirely ordinary — this is the wave-5 probe set where four supposedly
        /// different configurations returned IDENTICAL star counts.</para>
        ///
        /// <para>The overridden keys are MEASURED rather than listed as constants: the file's own bag is handed to a
        /// throwaway options instance and diffed afterwards, so this can never drift from what the class actually
        /// does. Never throws — a diagnostic must not abort a run.</para>
        /// </summary>
        internal static IReadOnlyList<string> SimpleModePresetOverrides(
                IDictionary<string, string> options, IProfileService profileService) {
            var empty = Array.Empty<string>();
            if (options == null || options.Count == 0) {
                return empty;
            }
            if (options.TryGetValue("UseAdvanced", out var ua) && bool.TryParse(ua, out var advanced) && advanced) {
                return empty;   // Advanced mode: the file's values are used verbatim.
            }
            try {
                var probe = new Dictionary<string, string>(options, StringComparer.Ordinal);
                _ = new NINA.Joko.Plugins.HocusFocus.StarDetection.StarDetectionOptions(
                    profileService, new FileOptionsAccessor(probe));
                var changed = new List<string>();
                foreach (var kv in options) {
                    if (probe.TryGetValue(kv.Key, out var after) && !string.Equals(after, kv.Value, StringComparison.Ordinal)) {
                        changed.Add($"{kv.Key} {kv.Value}→{after}");
                    }
                }
                changed.Sort(StringComparer.Ordinal);
                return changed;
            } catch (Exception) {
                return empty;
            }
        }

        private static void WarnIfSimpleModeOverridesTheFile(
                HarnessSettingsFile loaded, string path, IProfileService profileService) {
            if (loaded?.Options == null
                || (loaded.Options.TryGetValue("UseAdvanced", out var ua) && bool.TryParse(ua, out var adv) && adv)) {
                return;
            }
            var overridden = SimpleModePresetOverrides(loaded.Options, profileService);
            var detail = overridden.Count > 0
                ? $" Overwritten by the presets: {string.Join(", ", overridden)}."
                : " (Its recorded values happen to agree with the presets, so nothing changes — but an EDIT to one of them would not take effect.)";
            Console.Error.WriteLine(
                "WARNING: " + path + " has UseAdvanced=False, so Simple-mode presets recompute the advanced detector " +
                "knobs from Simple_NoiseLevel/Simple_PixelScale/Simple_FocusRange and editing them in this file " +
                "has NO effect." + detail + " Set \"UseAdvanced\": \"True\" to make this file's advanced knobs binding.");
            Logger.Warning($"Harness settings {path}: UseAdvanced=False; Simple-mode presets override "
                + $"{overridden.Count} recorded advanced knob(s).");
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
            CopyOptionSurface(
                new NINA.Joko.Plugins.HocusFocus.StarDetection.StarDetectionOptions(
                    profileService, new PluginOptionsAccessor(profileService, guid.Value)),
                new NINA.Joko.Plugins.HocusFocus.StarDetection.StarDetectionOptions(
                    profileService, new FileOptionsAccessor(file.Options)));

            // F58 — THE FIT, not just the detector. `AutoFocusOptions` was the ONE options surface the harness
            // still read straight off the active NINA profile, and four of its values reach the AF fit
            // (WeightedHyperbolicFitEnabled, MaxOutlierRejections, OutlierRejectionConfidence,
            // HyperbolicFitModel). So `--settings` pinned the detector and left the fit floating, and six waves
            // read that as pinning the arm. It is not academic: this machine's nine profiles partition 2/7 on
            // MaxOutlierRejections alone, and because NINA holds a `.profile` open while it is loaded (and
            // TryLoad silently skips a locked one for the next by LastUsed), concurrent `optimize` processes each
            // acquire a DIFFERENT profile -- which is how a single integer produced the two discrete "attractors"
            // F55 spent two waves chasing as a floating-point race.
            CopyOptionSurface(
                new NINA.Joko.Plugins.HocusFocus.AutoFocus.AutoFocusOptions(
                    profileService, new PluginOptionsAccessor(profileService, guid.Value)),
                new NINA.Joko.Plugins.HocusFocus.AutoFocus.AutoFocusOptions(
                    profileService, new FileOptionsAccessor(file.Options)));
            return file;
        }

        /// <summary>
        /// Copies an options class's whole PROPERTY SURFACE from one accessor-backed instance to another.
        ///
        /// <para>Property-by-property rather than by raw key, because the profile stores plugin options as typed
        /// values with no "list my keys" API: a raw read has to guess both the key list and each key's type, and
        /// a miss is SILENT — the loaded options fall back to the option's DEFAULT, which looks like a perfectly
        /// valid run. Assigning through the class makes the class itself emit the right keys with the right
        /// types, so the snapshot cannot drift from what it actually reads, and a newly added option is captured
        /// with no change here.</para>
        /// </summary>
        internal static void CopyOptionSurface<T>(T fromProfile, T toFile) {
            foreach (var prop in typeof(T).GetProperties()) {
                if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length > 0) {
                    continue;
                }
                object value;
                try {
                    value = prop.GetValue(fromProfile);
                } catch {
                    continue;   // a computed or profile-coupled property; not a knob we can carry
                }

                // Write a DIFFERENT value first so the option's change-detecting setter actually fires. Without
                // this the snapshot is SPARSE: a value equal to the shipped default never reaches the file, and
                // the file then inherits whatever the default IS AT LOAD TIME — behaviour-preserving today and
                // quietly wrong the day a default changes, which is precisely the drift a pinned settings file
                // exists to prevent.
                //
                // F59 — AND ONE POISON CANDIDATE IS NOT ENOUGH, WHICH COST FIVE KNOBS FOR SIX WAVES. These
                // setters VALIDATE and THROW: `MaxDistortion` demands [0, 1] and `OutlierRejectionConfidence`
                // demands (0.5, 1.0) exclusive, so the obvious `current + 1` poison raised ArgumentException,
                // a single enclosing catch swallowed it, and the property was skipped ENTIRELY — neither the
                // poison NOR the real value written. `pinned_settings.json`, the file waves 5–11 called "the
                // pinned detector", is missing `MaxDistortion`, `StarCenterTolerance`, `SaturationThreshold`,
                // `HotpixelThreshold` and `Sensitivity` for exactly this reason. So:
                //   * candidates are TRIED IN TURN until one lands inside the property's own valid range, and
                //   * the poison is VERIFIED BY READ-BACK rather than assumed to have taken, and
                //   * the real write has its OWN try, so a poison failure can never cost it again.
                foreach (var poison in PoisonCandidates(value, prop.PropertyType)) {
                    try {
                        prop.SetValue(toFile, poison);
                        if (!Equals(prop.GetValue(toFile), value)) {
                            break;      // it took; the real write below will now fire the change detector
                        }
                    } catch {
                        // Outside THIS property's valid range. Try the next candidate rather than giving up.
                    }
                }

                try {
                    prop.SetValue(toFile, value);
                } catch {
                    // The property genuinely refuses its own current value (computed, or validated against
                    // state this instance does not have). Skip it rather than abort the whole export.
                }
            }
        }

        /// <summary>
        /// Values of <paramref name="type"/> that differ from <paramref name="value"/>, to be tried IN TURN until
        /// one trips the property's change-detecting setter without tripping its VALIDATION.
        ///
        /// <para><b>F59 — why this is a sequence and not a single value.</b> The one-candidate version returned
        /// <c>current + 1</c>, which is outside the valid range of every knob bounded above: <c>MaxDistortion</c>
        /// demands [0, 1] and <c>OutlierRejectionConfidence</c> demands (0.5, 1.0) exclusive, so the poison threw
        /// and the property was dropped from the export entirely. The numeric spread below deliberately includes
        /// candidates on BOTH sides of the current value and INSIDE the unit interval, so a bounded knob still
        /// gets poisoned. Caller verifies by read-back — no candidate is assumed to have taken.</para>
        /// </summary>
        private static IEnumerable<object> PoisonCandidates(object value, Type type) {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t == typeof(bool)) {
                yield return !(bool)(value ?? false);
                yield break;
            }
            if (t == typeof(string)) {
                yield return (value as string) == "~" ? "~~" : "~";
                yield break;
            }
            if (t.IsEnum) {
                foreach (var candidate in Enum.GetValues(t)) {
                    if (!Equals(candidate, value)) {
                        yield return candidate;
                    }
                }
                yield break;
            }
            if (t == typeof(DateTime)) {
                yield return ((DateTime)(value ?? DateTime.UnixEpoch)).AddDays(1);
                yield break;
            }
            if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)
                || t == typeof(int) || t == typeof(long) || t == typeof(short)
                || t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort)) {
                double current;
                try {
                    current = Convert.ToDouble(value ?? 0);
                } catch {
                    yield break;
                }
                if (double.IsNaN(current) || double.IsInfinity(current)) {
                    current = 0.0;
                }
                // Both directions, and inside the unit interval, so a knob bounded to [0,1] or (0.5,1.0) is
                // still reachable. Integer knobs round on conversion, which is why +/-1 come first.
                var spread = new[] {
                    current + 1.0, current - 1.0,
                    (current + 1.0) / 2.0, current / 2.0,
                    current * 1.01, current * 0.99,
                    0.0, 1.0
                };
                foreach (var candidate in spread) {
                    object converted;
                    try {
                        converted = Convert.ChangeType(candidate, t);
                    } catch {
                        continue;   // out of the TYPE's range (e.g. -1 into a byte)
                    }
                    if (!Equals(converted, value)) {
                        yield return converted;
                    }
                }
            }
        }
    }
}
