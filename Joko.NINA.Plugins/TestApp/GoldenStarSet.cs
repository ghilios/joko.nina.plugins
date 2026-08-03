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
using System.IO;
using System.Linq;

namespace TestApp {

    /// <summary>
    /// A GOLDEN star set: every star a human/LLM visually identified in each frame of a saved AF focus sweep,
    /// keyed by focuser position. This is the DETECTOR-INDEPENDENT ground truth the <c>golden eval</c> harness
    /// scores precision/recall against; it is built by visual inspection of rendered tiles (see <c>golden tiles</c>),
    /// NEVER by running any star-finding code.
    ///
    /// <para>Storage convention: ONE golden file PER IMAGE, named <c>&lt;imageFileName&gt;.golden.json</c> and placed
    /// beside the image (e.g. <c>05_Frame00_..._Focuser2701.fits.golden.json</c> next to that FITS). Each file holds a
    /// single <see cref="GoldenFrame"/> for that image, so the golden set travels per-frame and the same method
    /// applies to any saved run. <see cref="GoldenFrame.ImageFile"/> records provenance.</para>
    ///
    /// <para>Box semantics are top-left <c>(x,y)</c> + size <c>(w,h)</c> in FULL-FRAME pixels — IDENTICAL to the
    /// review label boxes (<c>StarReviewLabelBox</c> / <c>RunEvaluationData.LabelBox</c>) so the box-containment and
    /// IoU matching primitives are shared by construction. Do NOT fork the field names — this is the third schema
    /// after <c>StarReviewLabels</c> and the <c>OptimizationDiagnosticRunner</c> label POCOs and must agree with them
    /// on box geometry.</para>
    /// </summary>
    public sealed class GoldenStarBox {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("w")] public double W { get; set; }
        [JsonProperty("h")] public double H { get; set; }

        /// <summary>Tiered visual confidence: "high" | "medium" | "low" (see <see cref="GoldenConfidence"/>).
        /// Lets recall/precision be reported at multiple confidence cuts. Null/empty ⇒ treated as "high".</summary>
        [JsonProperty("confidence", NullValueHandling = NullValueHandling.Ignore)]
        public string Confidence { get; set; }

        [JsonIgnore] public double CenterX => X + W / 2.0;
        [JsonIgnore] public double CenterY => Y + H / 2.0;
    }

    /// <summary>How much of one SNR tier was actually examined. Lets a consumer tell a precision
    /// MEASUREMENT from a lower bound: with a bounded montage budget, most of the faint tier on a deep
    /// field is never looked at.</summary>
    public sealed class GoldenTierCoverage {
        [JsonProperty("examined")] public int Examined { get; set; }
        [JsonProperty("total")] public int Total { get; set; }

        [JsonIgnore] public double Fraction => Total > 0 ? Examined / (double)Total : 0.0;
    }

    /// <summary>Per-image golden stars (the unit stored in one <c>&lt;image&gt;.golden.json</c>). <see cref="CoveredTiles"/>
    /// records which tiles were actually inspected so false-positive scoring can be restricted to covered area
    /// (partial coverage is honest).</summary>
    public sealed class GoldenFrame {
        /// <summary>The image file name this golden frame describes (provenance), e.g. the FITS filename.</summary>
        [JsonProperty("imageFile", NullValueHandling = NullValueHandling.Ignore)] public string ImageFile { get; set; }
        [JsonProperty("focuserPosition")] public int FocuserPosition { get; set; }
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;

        /// <summary>How this golden frame was produced, e.g. <c>"SNR-ref+montageQA"</c> (the real-bank
        /// pipeline, <c>tools/golden/build_goldens.py</c>) or <c>"synthetic"</c> (<c>GoldenFromTruth</c>,
        /// built from exact render truth rather than any detector/QA pass). The Python writer has emitted
        /// this since schema v2; this C# record had no equivalent field until now, so every C#-authored
        /// golden was silently missing provenance that the JSON schema already expected elsewhere.</summary>
        [JsonProperty("method", NullValueHandling = NullValueHandling.Ignore)] public string Method { get; set; }

        [JsonProperty("stars")] public List<GoldenStarBox> Stars { get; set; } = new List<GoldenStarBox>();

        [JsonProperty("coveredTiles", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> CoveredTiles { get; set; }

        /// <summary>Candidates the QA budget never reached — NEITHER stars nor confirmed non-stars.
        /// A detection landing on one of these is excluded from the false-positive count, and these
        /// boxes are never counted as missed stars. Absent (schema v1) ⇒ empty, so every pre-v2 golden
        /// scores exactly as it did before this field existed.</summary>
        [JsonProperty("unresolved", NullValueHandling = NullValueHandling.Ignore)]
        public List<GoldenStarBox> Unresolved { get; set; }

        /// <summary>Per-tier ("high"|"medium"|"low") examined/total counts.</summary>
        [JsonProperty("coverage", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, GoldenTierCoverage> Coverage { get; set; }

        /// <summary>Independent LLM votes per candidate (majority-confirmed). 1 or absent ⇒ single vote,
        /// which is NOT reproducible: two passes over the same defocused frames agreed on 62% of
        /// confirmed stars.</summary>
        [JsonProperty("qaVotes", NullValueHandling = NullValueHandling.Ignore)]
        public int? QaVotes { get; set; }

        /// <summary>Model + prompt revision that produced the QA verdicts, so a reproducibility
        /// regression is attributable.</summary>
        [JsonProperty("qaVersion", NullValueHandling = NullValueHandling.Ignore)]
        public string QaVersion { get; set; }
    }

    /// <summary>An in-memory aggregate of per-image golden frames for a run (NOT the on-disk unit — storage is one
    /// file per image). Handy when scoring a whole sweep.</summary>
    public sealed class GoldenStarSet {
        [JsonProperty("runId")] public string RunId { get; set; }
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("frames")] public List<GoldenFrame> Frames { get; set; } = new List<GoldenFrame>();
    }

    /// <summary>Canonical tiered-confidence labels + ranking (high &gt; medium &gt; low).</summary>
    public static class GoldenConfidence {
        public const string High = "high";
        public const string Medium = "medium";
        public const string Low = "low";

        /// <summary>3 = high, 2 = medium, 1 = low, 0 = unknown. Null/empty ⇒ treated as high (3), matching the
        /// JSON-default semantics where an unlabeled box is a confident star.</summary>
        public static int Rank(string confidence) {
            if (string.IsNullOrWhiteSpace(confidence)) {
                return 3;
            }
            switch (confidence.Trim().ToLowerInvariant()) {
                case High: return 3;
                case Medium: return 2;
                case Low: return 1;
                default: return 0;
            }
        }
    }

    /// <summary>
    /// Load/save helper for the PER-IMAGE golden file <c>&lt;imageFileName&gt;.golden.json</c> (one <see cref="GoldenFrame"/>
    /// per image, stored beside the image). Pure (no NINA / OpenCV coupling) so it is unit-testable; linked into the
    /// test project like <c>OptimizationRunDiscovery</c>.
    /// </summary>
    public static class GoldenStarSetStore {
        /// <summary>Suffix appended to an image file name to get its golden sidecar, e.g.
        /// <c>05_..._Focuser2701.fits</c> → <c>05_..._Focuser2701.fits.golden.json</c>.</summary>
        public const string Suffix = ".golden.json";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string Serialize(GoldenFrame frame) => JsonConvert.SerializeObject(frame, Settings);

        public static GoldenFrame Deserialize(string json) =>
            string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<GoldenFrame>(json);

        /// <summary>The golden sidecar path for an image path, e.g. <c>…\05_…_Focuser2701.fits.golden.json</c>.</summary>
        public static string PathForImage(string imagePath) => imagePath + Suffix;

        /// <summary>Loads the <see cref="GoldenFrame"/> sidecar for <paramref name="imagePath"/>, or null when
        /// absent/unparseable (partial coverage — that frame is skipped in scoring rather than counted all-missed).</summary>
        public static GoldenFrame LoadForImage(string imagePath) {
            var path = PathForImage(imagePath);
            if (!File.Exists(path)) {
                return null;
            }
            return Deserialize(File.ReadAllText(path));
        }

        /// <summary>Writes a per-image golden sidecar beside <paramref name="imagePath"/>; returns the sidecar path.</summary>
        public static string SaveForImage(string imagePath, GoldenFrame frame) {
            var path = PathForImage(imagePath);
            File.WriteAllText(path, Serialize(frame));
            return path;
        }
    }
}
