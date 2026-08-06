#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    /// <summary>
    /// Finds the saved report behind a chart NINA core just loaded, identified by its timestamp.
    ///
    /// <para><b>Why this has to exist.</b> Core's <c>AutoFocusToolVM.LoadChart</c> deserializes the report as the
    /// BASE <c>AutoFocusReport</c> and writes only <c>FocusPoints</c> / <c>PlotFocusPoints</c> /
    /// <c>FinalFocusPoint</c> / <c>LastAutoFocusPoint</c> / method / fitting onto <c>IAutoFocusVM</c> before calling
    /// <c>SetCurveFittings</c>. The plugin-only info rows (initial focuser position, Start HFR, HFR change) are not
    /// among them, and <see cref="HocusFocusReport.FinalHFR"/> is not even on the type core deserializes — so the
    /// only way to render the LOADED run's own values is to re-read its report. The single handle core leaves
    /// behind is <c>LastAutoFocusPoint.Timestamp</c>, which it copies verbatim from <c>report.Timestamp</c>.</para>
    /// </summary>
    internal interface ILoadedAutoFocusReportSource {

        /// <summary>
        /// The report whose <c>Timestamp</c> equals <paramref name="timestamp"/> EXACTLY, or null when there is no
        /// such report (deleted, unreadable, written by something with a different naming scheme, or mid-write).
        /// Never throws — a chart must still render when its report cannot be read.
        /// </summary>
        HocusFocusReport TryFind(DateTime timestamp);
    }

    /// <summary>
    /// The shipping <see cref="ILoadedAutoFocusReportSource"/>: a lookup over the report directory keyed on the
    /// timestamp embedded in each report's FILE NAME.
    ///
    /// <para><b>Why a filename window plus an exact content match, rather than either alone.</b> Report file names
    /// are <c>yyyy-MM-dd--HH-mm-ss--{profileId}.json</c> (<c>HocusFocusVM.cs:516</c>) — but that stamp and the
    /// report's own <c>Timestamp</c> (<c>HocusFocusReport.cs:91</c>) come from two SEPARATE <c>DateTime.Now</c>
    /// calls with a JSON serialization between them, so a second boundary can split them. Matching on the file name
    /// alone would therefore miss; parsing every report in a directory that holds months of them would be wasteful.
    /// So the file name narrows the candidates (a cheap directory listing, no parsing) and the report's own
    /// <c>Timestamp</c> — compared for EXACT equality — decides. A near-miss is not a match: adopting a report from
    /// a neighbouring second is the stale-value bug this whole path exists to prevent.</para>
    /// </summary>
    internal sealed class AutoFocusReportDirectorySource : ILoadedAutoFocusReportSource {

        /// <summary>
        /// How far the file name's second-resolution stamp may sit from the report's own <c>Timestamp</c> before a
        /// file stops being a candidate. Generous relative to the millisecond-scale gap the two <c>DateTime.Now</c>
        /// calls can actually open, because the cost of a wider window is one extra small JSON parse while the cost
        /// of a narrow one is silently failing to find the report at all.
        /// </summary>
        internal const double FileNameMatchWindowSeconds = 5.0;

        /// <summary>The <c>yyyy-MM-dd--HH-mm-ss</c> prefix every report file name starts with — 20 characters.</summary>
        private const string FileNameTimestampFormat = "yyyy-MM-dd--HH-mm-ss";

        private readonly string directory;

        public AutoFocusReportDirectorySource(string directory) {
            this.directory = directory;
        }

        public HocusFocusReport TryFind(DateTime timestamp) {
            try {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
                    return null;
                }

                var candidates = new List<(double Delta, string Path)>();
                foreach (var path in Directory.EnumerateFiles(directory, "*.json")) {
                    var name = Path.GetFileName(path);
                    if (name.Length < FileNameTimestampFormat.Length) {
                        continue;
                    }
                    if (!DateTime.TryParseExact(name.Substring(0, FileNameTimestampFormat.Length), FileNameTimestampFormat,
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamped)) {
                        continue;
                    }
                    var delta = Math.Abs((stamped - timestamp).TotalSeconds);
                    if (delta <= FileNameMatchWindowSeconds) {
                        candidates.Add((delta, path));
                    }
                }

                // Nearest first, then by path, so the scan is deterministic when two reports share a second.
                candidates.Sort((a, b) => a.Delta != b.Delta
                    ? a.Delta.CompareTo(b.Delta)
                    : string.CompareOrdinal(a.Path, b.Path));

                foreach (var candidate in candidates) {
                    var report = Deserialize(candidate.Path);
                    // Exact equality, deliberately: the file name only narrowed the search, it did not identify the
                    // run. Ticks round-trip through Newtonsoft the same way core's own deserialization does.
                    if (report != null && report.Timestamp == timestamp) {
                        return report;
                    }
                }
                return null;
            } catch (Exception) {
                // A report that cannot be listed or read is "not found". The caller collapses the rows, which is
                // exactly the pre-existing behaviour, so this path can never render a value from another run.
                return null;
            }
        }

        /// <summary>
        /// <see cref="HocusFocusReport"/> is WRITE-ONLY as a whole: it carries <c>HocusFocusStarDetectionOptions</c>
        /// (<c>IStarDetectionOptions</c>), <c>HocusFocusAutoFocusOptions</c> (<c>IAutoFocusOptions</c>) and
        /// <c>FocuserOptions</c> (<c>IFocuserSettings</c>), and Newtonsoft cannot construct an interface. A plain
        /// <c>DeserializeObject&lt;HocusFocusReport&gt;</c> of a REAL report therefore throws
        /// <c>"Could not create an instance of type ... Type is an interface or abstract class"</c> — every time,
        /// for every report this plugin has ever written.
        ///
        /// <para><b>This is why the loaded-run info rows never worked in the field</b> while their tests passed:
        /// the test fixture built reports without those three option blocks, so its JSON had nothing unreadable in
        /// it and the production writer's output was never round-tripped by anything.</para>
        ///
        /// <para>The handler skips a member it cannot construct and carries on, which is exactly right HERE: this
        /// read wants <c>Timestamp</c>, <c>InitialFocusPoint</c> and <c>FinalHFR</c>, and the option blocks are
        /// diagnostics for a human reading the file. A partially-populated object is safe by construction because
        /// the caller still requires an EXACT <c>Timestamp</c> match before using it, and every value resolver
        /// treats missing or non-finite as "not recorded" and collapses the row.</para>
        ///
        /// <para>Deliberately NOT fixed by changing what is WRITTEN: the option blocks are the record of how a run
        /// was configured, and dropping or retyping them would rewrite the on-disk format and lose that for every
        /// future report to fix a reader.</para>
        /// </summary>
        private static readonly JsonSerializerSettings ReadSettings = new JsonSerializerSettings {
            Error = (_, args) => args.ErrorContext.Handled = true
        };

        private static HocusFocusReport Deserialize(string path) {
            try {
                return JsonConvert.DeserializeObject<HocusFocusReport>(File.ReadAllText(path), ReadSettings);
            } catch (Exception) {
                // One unreadable/half-written report must not stop the scan reaching the right one.
                return null;
            }
        }
    }
}
