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
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace TestApp {

    /// <summary>
    /// The ONE printer both diagnostic runners use to record the <see cref="StarDetectorParams"/> bundle they
    /// hand the detector (wave 16 item B, RULE P16 — closing F67).
    ///
    /// <para><b>F67, restated.</b> <c>af-fit</c>'s per-position star count and <c>optimize</c>'s
    /// <c>optimizedStarCount</c> disagree on exactly 7 of 20 synthetic datasets — {D08, D09, D10, D12, D14, D15,
    /// D17} — at identical settings, with <c>af-fit</c> finding FEWER. Nobody knows whether the two entry points
    /// build different detectors from one settings file.</para>
    ///
    /// <para><b>Printing one side's params is not a diff.</b> <c>optimize</c> already printed five fields
    /// (Sensitivity, StarClippingMultiplier, NoiseClippingMultiplier, StructureLayers, MeasurementAverage)
    /// against a ~45-field object, so the register's "print the detector's own params from af-fit" could not have
    /// closed F67 either. What closes it is the FULL surface from BOTH sides, printed by the SAME code.</para>
    ///
    /// <para><b>Reflection, not a hand-written field list, and that is the entire point.</b> A list would have to
    /// be maintained in step with <see cref="StarDetectorParams"/>, and the defect being measured is precisely
    /// "two printouts of the same object that drifted apart". A field added in a later wave appears on both
    /// sides automatically or the instrument is measuring its own maintenance.</para>
    ///
    /// <para><b>The block shape is pre-registered</b> — <c>score_params_w16.py</c>'s <c>RX_BLOCK</c>/<c>RX_FIELD</c>
    /// were written before this file existed, so the printer and the parser cannot be reconciled after the fact:
    /// <code>
    /// PARAMS-DUMP &lt;source&gt; BEGIN      (column 0; source carries NO whitespace — the parser reads \S+)
    ///   &lt;FieldName&gt;=&lt;value&gt;         (leading whitespace REQUIRED, one field per line, sorted Ordinal)
    /// PARAMS-DUMP &lt;source&gt; END
    /// </code></para>
    ///
    /// <para><b>ASCII-ONLY, and this is a measured trap rather than a style preference.</b>
    /// <c>HarnessSettingsStore</c>'s preset-override warning writes a Unicode right arrow; on this machine the
    /// console code page cannot encode it, so a REDIRECTED log receives the single byte 0x1A. A parser written
    /// against the source string matched 0 of 40 perfectly good wave-15 logs and reported "could not look" on the
    /// whole population. Every character this class prints is printable ASCII, values included — see
    /// <see cref="Ascii"/>.</para>
    ///
    /// <para><b>Where the output goes is the caller's choice, and on the af-fit side it is load-bearing.</b>
    /// <see cref="Write"/> takes the sink, so nothing here can decide to write into a file. Clause W1 of wave 16
    /// is a BYTE comparison of <c>af_fit_summary.txt</c> against wave 14's control rung, so the af-fit call site
    /// passes <c>Console.WriteLine</c> and NOT that runner's <c>Emit</c> helper (which also appends to the
    /// summary). This is provenance in the log, not a row in the report.</para>
    /// </summary>
    internal static class ParamsDump {

        /// <summary>The params <c>af-fit</c> hands <c>StarDetector.Detect</c> for every frame in the run.</summary>
        internal const string AfFitDetector = "af-fit/detector";

        /// <summary>
        /// <c>optimize</c>'s BASELINE bundle — the user's current settings, built by the same
        /// <c>HocusFocusStarDetection.BuildStarDetectorParams(options)</c> call <c>af-fit</c> falls back to. This
        /// is the apples-to-apples side of the P16 diff, and the only one <c>score_params_w16.py</c> reads.
        /// </summary>
        internal const string OptimizeBaseline = "optimize/baseline";

        /// <summary>
        /// <c>optimize</c>'s SEED bundle — a genuinely different object (the fully-default
        /// <c>BuildDefaultStarDetectorParams</c> bundle the search starts from, or the baseline's twin under
        /// <c>--start-from-current</c>). It is dumped because it is also handed to the detector, but it is NOT
        /// the comparison target: seed differing from af-fit is expected by construction, not a finding.
        /// </summary>
        internal const string OptimizeSeed = "optimize/seed";

        /// <summary>Leading whitespace on a field line. The parser REQUIRES it (<c>RX_FIELD</c> is <c>^\s+...</c>).</summary>
        internal const string FieldIndent = "  ";

        /// <summary>
        /// The pre-registered block for <paramref name="detectorParams"/>, one line per element, newline-joined
        /// with no trailing newline.
        /// </summary>
        internal static string Format(string source, StarDetectorParams detectorParams) {
            return string.Join(Environment.NewLine, Lines(source, detectorParams));
        }

        /// <summary>
        /// The pre-registered block as individual lines: the BEGIN marker, one <c>Name=Value</c> line per public
        /// readable instance property sorted Ordinal by name, then the END marker.
        ///
        /// <para>The property set is deliberately UNFILTERED. <see cref="StarDetectorParams.ToString"/> omits a
        /// dozen fields and <c>ToCanonicalCacheString</c> denylists six more as output-neutral; both denylists
        /// are correct for their own purpose and wrong for this one, because a field that cannot change a star
        /// count can still be the fingerprint that identifies WHICH construction path built the bundle.</para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="detectorParams"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="source"/> is empty or carries whitespace. The parser matches the source with
        /// <c>\S+</c> and pairs BEGIN with END by backreference, so a source with a space produces a block that
        /// is silently INVISIBLE to the scorer — which reads as "could not look", i.e. F66's shape. It fails
        /// here instead.
        /// </exception>
        internal static IReadOnlyList<string> Lines(string source, StarDetectorParams detectorParams) {
            ValidateSource(source);
            if (detectorParams == null) {
                throw new ArgumentNullException(nameof(detectorParams));
            }

            var properties = detectorParams.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(prop => prop.CanRead && prop.GetIndexParameters().Length == 0)
                .OrderBy(prop => prop.Name, StringComparer.Ordinal);

            var lines = new List<string> { $"PARAMS-DUMP {source} BEGIN" };
            foreach (var prop in properties) {
                lines.Add($"{FieldIndent}{prop.Name}={Ascii(FormatValue(ReadSafe(prop, detectorParams)))}");
            }
            lines.Add($"PARAMS-DUMP {source} END");
            return lines;
        }

        /// <summary>
        /// Writes the block through <paramref name="writeLine"/> and through nothing else. The sink is a
        /// parameter rather than a hard-coded <c>Console.WriteLine</c> so the destination is decided — and
        /// testable — at each call site; see the class remarks on clause W1.
        /// </summary>
        internal static void Write(Action<string> writeLine, string source, StarDetectorParams detectorParams) {
            if (writeLine == null) {
                throw new ArgumentNullException(nameof(writeLine));
            }
            foreach (var line in Lines(source, detectorParams)) {
                writeLine(line);
            }
        }

        private static void ValidateSource(string source) {
            if (string.IsNullOrEmpty(source)) {
                throw new ArgumentException("A PARAMS-DUMP source tag is required.", nameof(source));
            }
            if (source.Any(char.IsWhiteSpace)) {
                throw new ArgumentException(
                    $"PARAMS-DUMP source '{source}' contains whitespace. RULE P16's parser reads the source as " +
                    "\\S+ and pairs BEGIN with END by backreference, so this block would not be seen at all — " +
                    "and an unseen dump is scored COULD NOT LOOK, not 'the params agree'.", nameof(source));
            }
        }

        private static object ReadSafe(PropertyInfo prop, StarDetectorParams detectorParams) {
            try {
                return prop.GetValue(detectorParams);
            } catch (Exception ex) {
                // A misbehaving getter must not cost the whole diff. Fold the exception type in so the two sides
                // still compare, and so a reader can see that this field was not actually read.
                return $"<err:{ex.GetType().Name}>";
            }
        }

        /// <summary>
        /// Culture-invariant rendering of one property value.
        ///
        /// <para><see cref="StarDetectionRegion"/> is rendered in the shape RULE P16 pre-registered — the
        /// <c>ToString()</c> layout, whose <c>StartX=0, StartY=0, Height=1, Width=1</c> substring is what the
        /// scorer's "both sides Full" condition tests — but built with <see cref="CultureInfo.InvariantCulture"/>
        /// rather than by calling <c>ToString()</c>, which the source documents as NOT locale-safe. The type's
        /// own <c>ToCanonicalString()</c> is invariant but has a different shape and would make the scorer report
        /// the Full region as a BROKEN condition.</para>
        /// </summary>
        private static string FormatValue(object value) {
            switch (value) {
                case null:
                    return "null";

                case StarDetectionRegion region:
                    var inner = region.InnerCropBoundary == null ? string.Empty : FormatRect(region.InnerCropBoundary);
                    return $"{{OuterBoundary={FormatRect(region.OuterBoundary)}, InnerCropBoundary={inner}}}";

                case RatioRect rect:
                    return FormatRect(rect);

                case Enum e:
                    // The NAME, never the underlying number: reordering enum members must not silently make two
                    // different settings compare equal, and the scorer's conditions read names ("Median").
                    return e.ToString();

                case bool b:
                    // "True"/"False" — invariant, and the form RULE P16's pre-registered example uses.
                    return b ? "True" : "False";

                case IFormattable formattable:
                    // Every numeric (double/float/int/...) with a locale-independent separator. Doubles use the
                    // shortest round-trippable form, so an exact-equality diff between the two sides is exact.
                    return formattable.ToString(null, CultureInfo.InvariantCulture);

                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            }
        }

        private static string FormatRect(RatioRect rect) {
            // Field ORDER matches StarDetectionRegion/RatioRect.ToString() exactly, Height before Width included:
            // the scorer's "both sides Full" test is a literal substring match against that layout.
            return $"{{StartX={Inv(rect.StartX)}, StartY={Inv(rect.StartY)}, " +
                $"Height={Inv(rect.Height)}, Width={Inv(rect.Width)}}}";
        }

        private static string Inv(double d) => d.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Forces printable ASCII. Anything outside 0x20..0x7E — a Unicode character the console code page
        /// cannot encode, or a newline that would split one field across two parseable lines — becomes
        /// <c>\uXXXX</c>. The value stays readable and, more importantly, stays one line that survives a
        /// redirected log byte for byte.
        /// </summary>
        private static string Ascii(string s) {
            if (s == null) {
                return string.Empty;
            }
            var needsEscaping = false;
            foreach (var c in s) {
                if (c < ' ' || c > '~') {
                    needsEscaping = true;
                    break;
                }
            }
            if (!needsEscaping) {
                return s;
            }
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s) {
                if (c < ' ' || c > '~') {
                    sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                } else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
