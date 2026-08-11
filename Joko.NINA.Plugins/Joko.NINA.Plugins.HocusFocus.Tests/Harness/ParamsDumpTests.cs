#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// RULE P16 (wave 16 item B): the ONE printer both diagnostic runners use for their
/// <see cref="StarDetectorParams"/> bundle, so F67 — <c>af-fit</c> and <c>optimize</c> counting different numbers
/// of stars on 7 of 20 datasets at identical settings — can be answered by a field-by-field DIFF rather than by
/// printing one side and guessing.
///
/// <para><b>Against the pre-change source every test in this file fails to compile</b>: <c>ParamsDump</c> does not
/// exist. So a revert cannot be mistaken for a pass, and each test below names the narrower MUTANT it kills —
/// the ones a revert would not catch.</para>
///
/// <para>The block shape is not this file's invention. <c>score_params_w16.py</c> was written at pre-registration
/// and its <c>RX_BLOCK</c>/<c>RX_FIELD</c> are transcribed here verbatim, so "the printer and the parser agree" is
/// asserted against the parser that will actually read the wave's logs, not against a restatement of it.</para>
/// </summary>
[TestFixture]
public class ParamsDumpTests {

    // ---- score_params_w16.py, transcribed. Changing these to match the code would defeat the purpose. ---------

    //   RX_BLOCK = re.compile(r"^PARAMS-DUMP (\S+) BEGIN\s*$(.*?)^PARAMS-DUMP \1 END\s*$", re.M | re.S)
    private static readonly Regex ScorerBlock = new Regex(
        @"^PARAMS-DUMP (\S+) BEGIN\s*$(.*?)^PARAMS-DUMP \1 END\s*$",
        RegexOptions.Multiline | RegexOptions.Singleline);

    //   RX_FIELD = re.compile(r"^\s+([A-Za-z0-9_]+)=(.*)$")
    private static readonly Regex ScorerField = new Regex(@"^\s+([A-Za-z0-9_]+)=(.*)$");

    //   "Region": ("both sides Full", lambda v: "InnerCropBoundary=}" in v
    //              or v.strip().endswith("InnerCropBoundary=}")
    //              or "StartX=0, StartY=0, Height=1, Width=1" in v),
    private static bool ScorerCallsTheRegionFull(string v) =>
        v.Contains("InnerCropBoundary=}")
        || v.Trim().EndsWith("InnerCropBoundary=}")
        || v.Contains("StartX=0, StartY=0, Height=1, Width=1");

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static IReadOnlyDictionary<string, string> ParseAsTheScorerWould(string text, string expectedSource) {
        var block = ScorerBlock.Match(text);
        Assert.That(block.Success, Is.True, "score_params_w16.py's RX_BLOCK did not match the emitted text");
        Assert.That(block.Groups[1].Value, Is.EqualTo(expectedSource));
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in block.Groups[2].Value.Split('\n')) {
            var m = ScorerField.Match(line.TrimEnd('\r'));
            if (m.Success) {
                fields[m.Groups[1].Value] = m.Groups[2].Value.TrimEnd();
            }
        }
        return fields;
    }

    private static string[] ReflectedPropertyNames(object o) =>
        o.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToArray();

    /// <summary>A later wave adding a knob, simulated. Nothing in <c>ParamsDump</c> knows this type exists.</summary>
    private sealed class ParamsWithALaterWavesKnob : StarDetectorParams {
        public int WaveSeventeenKnob { get; set; } = 7;
    }

    // ---- the block shape RULE P16's parser reads ----------------------------------------------------------------

    [Test]
    public void TheBlock_IsExactlyWhatTheScorersRegexesRead() {
        // MUTANT: indent the BEGIN/END markers (RX_BLOCK anchors them at column 0), or drop the leading spaces
        // from the field lines (RX_FIELD requires ^\s+). Either one makes the dump INVISIBLE to the scorer, which
        // scores as "COULD NOT LOOK" — the failure mode that is indistinguishable from "the params agree" unless
        // something checks it here.
        var p = new StarDetectorParams();
        var lines = ParamsDump.Lines(ParamsDump.AfFitDetector, p);

        Assert.Multiple(() => {
            Assert.That(lines.First(), Is.EqualTo("PARAMS-DUMP af-fit/detector BEGIN"));
            Assert.That(lines.Last(), Is.EqualTo("PARAMS-DUMP af-fit/detector END"));
            foreach (var line in lines.Skip(1).Take(lines.Count - 2)) {
                Assert.That(line, Does.StartWith(ParamsDump.FieldIndent), $"field line not indented: '{line}'");
                Assert.That(line.Trim(), Does.Contain("="), $"field line has no '=': '{line}'");
            }
        });

        var fields = ParseAsTheScorerWould(ParamsDump.Format(ParamsDump.AfFitDetector, p), ParamsDump.AfFitDetector);
        Assert.That(fields.Count, Is.EqualTo(lines.Count - 2),
            "every line between the markers must parse as a field for the scorer");
    }

    [Test]
    public void TheSourceTags_AreTheFourPreRegisteredStrings() {
        // MUTANT M-T1: rename a source ("affit/detector", "optimize-baseline", "optimize/baseline-resolved", ...).
        // The scorer looks the source up by exact key — `collect(af_paths, "af-fit/detector")`, and wave 20's
        // `one_block(blocks, "optimize/detected")` — so a renamed tag is not a differently-named block, it is NO
        // block, on every log in the population. That is COULD-NOT-LOOK, which wave 19 has just demonstrated is
        // indistinguishable from "the code never shipped" unless something asserts the string itself.
        Assert.Multiple(() => {
            Assert.That(ParamsDump.AfFitDetector, Is.EqualTo("af-fit/detector"));
            Assert.That(ParamsDump.OptimizeBaseline, Is.EqualTo("optimize/baseline"));
            Assert.That(ParamsDump.OptimizeSeed, Is.EqualTo("optimize/seed"));
            Assert.That(ParamsDump.OptimizeDetected, Is.EqualTo("optimize/detected"));
            Assert.That(ParamsDump.AllSources, Is.EqualTo(new[] {
                "af-fit/detector", "optimize/baseline", "optimize/seed", "optimize/detected"
            }));
        });
    }

    [Test]
    public void NoSourceTag_ContainsAnyOther_BecauseEveryScorerCountsThemBySubstring() {
        // MUTANT M-T2: name the new tag "optimize/baseline-resolved" or "optimize/seed-detected". Nothing about the
        // dump itself would break — and clause G20-P2e would fail on all eight logs, because every driver this
        // project has saved counts a block with `grep -c "PARAMS-DUMP optimize/baseline BEGIN"`. A tag that
        // CONTAINS another tag double-counts it; a tag CONTAINED BY another is double-counted by it. Both
        // directions are checked, on every ordered pair, over ParamsDump.AllSources rather than over a list this
        // test would have to remember to extend.
        //
        // This is not a hypothetical: G20-P2e's threshold is "optimize/baseline and optimize/seed still appear
        // EXACTLY once per log", and its stated suspect on a failure is this tag.
        var tags = ParamsDump.AllSources;
        Assert.That(tags.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(tags.Count), "two tags are equal");
        Assert.Multiple(() => {
            foreach (var a in tags) {
                foreach (var b in tags) {
                    if (ReferenceEquals(a, b) || string.Equals(a, b, StringComparison.Ordinal)) {
                        continue;
                    }
                    Assert.That(a.Contains(b, StringComparison.Ordinal), Is.False,
                        $"source tag '{a}' contains '{b}' — a saved `grep -c` for '{b}' would double-count it");
                }
            }
        });
        // The one that reads as a near-miss and is not: "detector" is not a substring of "detected".
        Assert.That(ParamsDump.OptimizeDetected.Contains("detector", StringComparison.Ordinal), Is.False);
    }

    [Test]
    public void TheDetectedBlock_HasTheSameFieldSurfaceAsTheBaselineBlock_ThroughTheOneSharedFormatter() {
        // MUTANT M-T3: give the new block a printer of its own, or filter the reflection for it (drop one property
        // from Lines). Clause G20-P2b's threshold is "55 field lines and 55 UNIQUE names" and clause D20-D is a SET
        // EQUALITY over the two blocks' field names — a block with a different surface makes D20-D's difference set
        // include every field only one side prints, so the wave would read a printer discrepancy as a detector one.
        // Asserted BOTH ways: identical to the baseline block line-for-line, AND equal in count to the reflected
        // property set, so a filter applied to Lines (which would shrink both blocks equally) is still red.
        var p = new StarDetectorParams();
        var detected = ParamsDump.Lines(ParamsDump.OptimizeDetected, p);
        var baseline = ParamsDump.Lines(ParamsDump.OptimizeBaseline, p);

        Assert.Multiple(() => {
            Assert.That(detected.First(), Is.EqualTo("PARAMS-DUMP optimize/detected BEGIN"));
            Assert.That(detected.Last(), Is.EqualTo("PARAMS-DUMP optimize/detected END"));
            Assert.That(detected.Skip(1).SkipLast(1), Is.EqualTo(baseline.Skip(1).SkipLast(1)),
                "the two blocks must differ ONLY in their markers when the bundle is the same object");
            Assert.That(detected.Count - 2, Is.EqualTo(ReflectedPropertyNames(p).Length),
                "the detected block must carry every reflected property, not a curated subset");
            var names = detected.Skip(1).SkipLast(1).Select(l => l.Trim().Split('=')[0]).ToList();
            Assert.That(names.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(names.Count),
                "G20-P2b counts UNIQUE names as well as lines");
        });
    }

    [Test]
    public void TheDetectedBlock_IsPrintableAsciiAndInvariant_UnderACommaDecimalCulture() {
        // MUTANT: drop Ascii(), or format with the ambient culture. Demonstrated HERE for the new tag and not only
        // for af-fit/detector, because this is the block whose PixelScale clause G20-P2c reads as a NUMBER and
        // whose "NaN" clause reads as a TOKEN: under de-DE the detected block would print "1,4101", which parses as
        // neither, and the scorer would report COULD-NOT-LOOK on all eight gate logs at once.
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try {
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var p = new StarDetectorParams {
                PixelScale = 1.4101,
                DetectionBinning = 2,
                SaveIntermediateFilesPath = "C:\\caf\u00e9\\\u2192"
            };
            var text = ParamsDump.Format(ParamsDump.OptimizeDetected, p);
            var payload = string.Concat(text.Split(Environment.NewLine));
            var emitted = ParseAsTheScorerWould(text, ParamsDump.OptimizeDetected);

            Assert.Multiple(() => {
                Assert.That(payload.All(c => c >= ' ' && c <= '~'), Is.True,
                    "a non-ASCII or control character reached the detected block");
                Assert.That(emitted["PixelScale"], Is.EqualTo("1.4101"));
                Assert.That(emitted["DetectionBinning"], Is.EqualTo("2"));
                Assert.That(emitted["Region"], Does.Contain("StartX=0, StartY=0, Height=1, Width=1"));
            });
        } finally {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Test]
    public void ADetectedBlock_TakenAfterApplyFactor_DiffersFromTheBaselineOnExactlyDetectionBinningAndPixelScale() {
        // This is clause D20-D's bar, asserted as a unit test, so `score_d20_w20.py` and the product agree about
        // WHICH fields ApplyFactor touches. The scorer's expectation is a SET EQUALITY fixed before the data:
        // {DetectionBinning, PixelScale} on a factor-2 run and {PixelScale} alone on the factor-1 gate. If
        // ApplyFactor ever grew a third field, the scorer would report D-PRESENT-ONLY and name a product change as
        // an instrument failure.
        //
        // MUTANT M-T4: make ApplyFactor write DetectionBinning without rescaling PixelScale. Red here, and in the
        // arm it would leave every pixel-scale-dependent gate evaluated at half the scale the detector analyses at.
        // MUTANT M-T4': make ApplyFactor also stamp, say, MinHFR. Red here; in the arm it is D20-D's "unexpected"
        // set, which is exactly what F71's one-field control failed to notice.
        var p = new StarDetectorParams { PixelScale = 0.31482, DetectionBinning = 1 };
        var before = ParseAsTheScorerWould(
            ParamsDump.Format(ParamsDump.OptimizeBaseline, p), ParamsDump.OptimizeBaseline);
        DetectionBinningResolver.ApplyFactor(p, 2);
        var after = ParseAsTheScorerWould(
            ParamsDump.Format(ParamsDump.OptimizeDetected, p), ParamsDump.OptimizeDetected);

        // `diff_set` in score_d20_w20.py, transcribed: the union of both key sets, kept where the values differ.
        var moved = before.Keys.Union(after.Keys, StringComparer.Ordinal)
            .Where(k => !string.Equals(
                before.TryGetValue(k, out var b) ? b : null,
                after.TryGetValue(k, out var a) ? a : null,
                StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() => {
            Assert.That(moved, Is.EqualTo(new[] { "DetectionBinning", "PixelScale" }),
                "D20-D expects EXACTLY these two to move; an extra name here is a FAIL in the arm, not a note");
            Assert.That(after["DetectionBinning"], Is.EqualTo("2"));
            Assert.That(after["PixelScale"], Is.EqualTo("0.62964"));
        });
    }

    [Test]
    public void ASourceTagWithWhitespace_Throws_RatherThanEmittingABlockNobodyCanSee() {
        // MUTANT: drop the source validation. RX_BLOCK reads the source as \S+ and pairs BEGIN with END by
        // BACKREFERENCE, so "af fit" produces a block the parser cannot match at all — a dump that is silently
        // absent. That is F66's shape: a diagnostic that quietly does nothing is worse than one that crashes.
        Assert.Multiple(() => {
            Assert.That(() => { ParamsDump.Lines("af fit", new StarDetectorParams()); }, Throws.ArgumentException);
            Assert.That(() => { ParamsDump.Lines("af-fit/detector\t", new StarDetectorParams()); }, Throws.ArgumentException);
            Assert.That(() => { ParamsDump.Lines("", new StarDetectorParams()); }, Throws.ArgumentException);
        });
    }

    // ---- sorted, complete, reflective ---------------------------------------------------------------------------

    [Test]
    public void Fields_AreSortedByNameOrdinal_NotOrdinalIgnoreCase() {
        // MUTANT: OrderBy(..., StringComparer.OrdinalIgnoreCase), or no OrderBy at all. This is killable on the
        // REAL object rather than only in principle: Ordinal puts every PSF* field before PeakResponse ('S' 0x53 <
        // 'e' 0x65) while OrdinalIgnoreCase reverses them. An unsorted dump still parses — it just makes a
        // line-by-line read of two logs useless, which is the whole point of the artifact.
        var fields = ParamsDump.Lines(ParamsDump.OptimizeBaseline, new StarDetectorParams())
            .Skip(1).SkipLast(1)
            .Select(l => l.Trim().Split('=')[0])
            .ToList();

        Assert.Multiple(() => {
            Assert.That(fields, Is.EqualTo(fields.OrderBy(f => f, StringComparer.Ordinal).ToList()));
            Assert.That(fields.IndexOf("PSFFitType"), Is.LessThan(fields.IndexOf("PeakResponse")),
                "Ordinal sort places PSFFitType before PeakResponse; OrdinalIgnoreCase would not");
        });
    }

    [Test]
    public void EveryPublicReadableProperty_IsEmitted_IncludingTheOnesToStringAndTheCacheKeyDrop() {
        // MUTANT: filter the reflection through StarDetectorParams.ToString()'s field list, or through
        // ToCanonicalCacheString's `CacheKeyExcludedProperties` denylist. Both denylists are correct for their own
        // purpose and wrong for this one: a field that cannot change a star count can still be the FINGERPRINT
        // that says which construction path built the bundle — SuppressInfoLogging is set only by the optimize
        // path and is exactly such a field.
        var p = new StarDetectorParams();
        var emitted = ParseAsTheScorerWould(ParamsDump.Format(ParamsDump.AfFitDetector, p), ParamsDump.AfFitDetector);

        Assert.That(emitted.Keys.OrderBy(k => k, StringComparer.Ordinal),
            Is.EqualTo(ReflectedPropertyNames(p).OrderBy(k => k, StringComparer.Ordinal)));
        Assert.Multiple(() => {
            // Dropped by ToString() and/or denylisted from the cache key, and all live for provenance.
            foreach (var name in new[] {
                "SuppressInfoLogging", "StoreStructureMap", "CollectContaminationDiagnostics",
                "CollectRejectedCandidateDiagnostics", "MaxStarEvaluationParallelism",
                "LocallyAdaptiveBinarization", "AdaptiveNoiseBlockSize", "DetectionBinning",
                "HfrTauPolicy", "MeasurementAverage", "ExcludeSaturatedStarsFromHFR", "PSFPixelIntegration"
            }) {
                Assert.That(emitted.ContainsKey(name), Is.True, $"{name} is missing from the dump");
            }
        });
    }

    [Test]
    public void AFieldAddedByALaterWave_AppearsWithoutAnyoneEditingTheFormatter() {
        // MUTANT: replace the reflection with a hand-written list of the ~45 names known today. That mutant
        // reproduces the very defect being measured — two printouts of one object that drifted apart — and it
        // passes every other test in this file, because today the hand list and the reflection agree.
        var later = new ParamsWithALaterWavesKnob();
        var emitted = ParseAsTheScorerWould(
            ParamsDump.Format(ParamsDump.OptimizeSeed, later), ParamsDump.OptimizeSeed);

        Assert.Multiple(() => {
            Assert.That(emitted.ContainsKey("WaveSeventeenKnob"), Is.True,
                "a property added to the params object must appear with no change to ParamsDump");
            Assert.That(emitted["WaveSeventeenKnob"], Is.EqualTo("7"));
            Assert.That(emitted.Count, Is.EqualTo(ReflectedPropertyNames(new StarDetectorParams()).Length + 1));
        });
    }

    // ---- values: ASCII, invariant, and in the forms the scorer's conditions test ---------------------------------

    [Test]
    public void EveryCharacterEmitted_IsPrintableAscii_EvenWhenAValueIsNot() {
        // MUTANT: drop the Ascii() escaping. MEASURED, not stylistic: HarnessSettingsStore prints a Unicode arrow
        // and on this machine the console code page turns it into the single byte 0x1A in a REDIRECTED log, which
        // made the first run of score_params_w16.py report "could not look" on all 40 wave-15 logs. A path or a
        // newline inside a value would do the same thing to this block — and a newline would additionally split
        // one field into two lines, one of which parses as a bogus field.
        var p = new StarDetectorParams {
            SaveIntermediateFilesPath = "C:\\caf\u00e9\\\u2192\r\nStarClippingMultiplier=999"
        };
        var text = ParamsDump.Format(ParamsDump.AfFitDetector, p);
        var payload = string.Concat(text.Split(Environment.NewLine));

        Assert.Multiple(() => {
            Assert.That(payload.All(c => c >= ' ' && c <= '~'), Is.True,
                "a non-ASCII or control character reached the dump");
            var emitted = ParseAsTheScorerWould(text, ParamsDump.AfFitDetector);
            Assert.That(emitted["SaveIntermediateFilesPath"],
                Is.EqualTo("C:\\caf\\u00E9\\\\u2192\\u000D\\u000AStarClippingMultiplier=999"));
            Assert.That(emitted["StarClippingMultiplier"], Is.EqualTo("2"),
                "the smuggled newline must not have produced a second, bogus StarClippingMultiplier line");
        });
    }

    [Test]
    public void Values_AreInvariantCulture_UnderACommaDecimalCulture() {
        // MUTANT: format with the ambient culture (plain ToString()). Under de-DE every double prints "1,2", the
        // two sides of the diff stop comparing across machines, and — worse — a comma inside a value is
        // indistinguishable from the separators a downstream reader splits on.
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try {
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var p = new StarDetectorParams { MinHFR = 1.2, PixelScale = 0.75, AnalysisSamplingSize = 1.5f };
            var emitted = ParseAsTheScorerWould(
                ParamsDump.Format(ParamsDump.AfFitDetector, p), ParamsDump.AfFitDetector);
            Assert.Multiple(() => {
                Assert.That(emitted["MinHFR"], Is.EqualTo("1.2"));
                Assert.That(emitted["PixelScale"], Is.EqualTo("0.75"));
                Assert.That(emitted["AnalysisSamplingSize"], Is.EqualTo("1.5"));
            });
        } finally {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Test]
    public void TheRegion_RendersInTheShapeTheScorersFullConditionTests_AndInvariantly() {
        // MUTANT: emit Region via its own ToCanonicalString() (invariant, but shaped
        // "{Index=0,Outer={StartX=0,...}}"), or via ToString() (right shape, but the source documents it as NOT
        // locale-safe). The first makes the scorer report the Full region as a **CONDITION BROKEN** — a field
        // whose inertness could not be verified, on both sides, on every dataset. The second silently prints
        // "StartX=0,1" on a comma-decimal machine.
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try {
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var full = ParseAsTheScorerWould(
                ParamsDump.Format(ParamsDump.AfFitDetector, new StarDetectorParams()),
                ParamsDump.AfFitDetector)["Region"];
            var cropped = ParseAsTheScorerWould(
                ParamsDump.Format(ParamsDump.AfFitDetector,
                    new StarDetectorParams { Region = new StarDetectionRegion(new RatioRect(0.1, 0.1, 0.8, 0.8)) }),
                ParamsDump.AfFitDetector)["Region"];

            Assert.Multiple(() => {
                Assert.That(full, Is.EqualTo("{OuterBoundary={StartX=0, StartY=0, Height=1, Width=1}, InnerCropBoundary=}"));
                Assert.That(ScorerCallsTheRegionFull(full), Is.True,
                    "score_params_w16.py must read this as the Full region, or Region is scored CONDITION BROKEN");
                Assert.That(cropped, Does.Contain("StartX=0.1"), "a comma decimal separator leaked into the region");
            });
        } finally {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Test]
    public void EnumsPrintTheirName_AndBoolsPrintTrueFalse_BecauseTheScorersConditionsReadThem() {
        // MUTANT: print enums by their underlying number (Convert.ToInt32), or bools as "1"/"0". The scorer's
        // conditional partition tests `MeasurementAverage == "Median"` and `ModelPSF in ("false","0")`; a "0" for
        // MeasurementAverage does not read as Median, so the MeanOutliers post-filter's inertness would be scored
        // as UNVERIFIED on both sides at once. And an enum printed as a number lets a member reordering silently
        // make two different settings compare equal.
        var emitted = ParseAsTheScorerWould(
            ParamsDump.Format(ParamsDump.OptimizeBaseline,
                new StarDetectorParams { ModelPSF = false, PSFFitType = StarDetectorPSFFitType.Gaussian }),
            ParamsDump.OptimizeBaseline);

        Assert.Multiple(() => {
            Assert.That(emitted["MeasurementAverage"], Is.EqualTo("Median"));
            Assert.That(emitted["PSFFitType"], Is.EqualTo("Gaussian"));
            Assert.That(emitted["ModelPSF"], Is.EqualTo("False"));
            Assert.That(emitted["HotpixelFiltering"], Is.EqualTo("True"));
            Assert.That(emitted["HfrTauPolicy"], Is.EqualTo("GateOnly"));
        });
    }

    [Test]
    public void ANullParamsBundle_Throws_RatherThanEmittingAnEmptyBlock() {
        // MUTANT: guard with `if (detectorParams == null) return;`. An EMPTY block parses, and `parse_dumps`
        // keeps a source only when it has fields — so the block would vanish and be scored COULD NOT LOOK with
        // nothing in the log to say why.
        Assert.That(() => { ParamsDump.Lines(ParamsDump.AfFitDetector, null); }, Throws.ArgumentNullException);
    }

    // ---- the sink is the caller's, and that is what protects clause W1 -------------------------------------------

    [Test]
    public void Write_SendsEveryLine_ToTheSuppliedSink_AndWritesNothingItself() {
        // MUTANT: hard-code Console.WriteLine inside Write and ignore the sink. Then the af-fit call site cannot
        // choose its destination, and — worse — a future caller that means to route the dump elsewhere gets it in
        // BOTH places. The console assertion is what makes the source-level W1 test below meaningful: the sink
        // parameter genuinely determines where the block lands.
        var collected = new List<string>();
        var previousOut = Console.Out;
        var console = new StringWriter();
        try {
            Console.SetOut(console);
            ParamsDump.Write(collected.Add, ParamsDump.OptimizeSeed, new StarDetectorParams());
        } finally {
            Console.SetOut(previousOut);
        }

        Assert.Multiple(() => {
            Assert.That(collected, Is.EqualTo(ParamsDump.Lines(ParamsDump.OptimizeSeed, new StarDetectorParams())));
            Assert.That(console.ToString(), Is.Empty, "Write must emit through the sink and nowhere else");
        });
    }

    // ---- the wiring: both runners, one formatter, and af-fit's sink ----------------------------------------------
    //
    // These three read the runner SOURCE. AfFitDiagnosticRunner/OptimizationDiagnosticRunner cannot be linked into
    // this project (WPF Application, ProfileService, frames on disk), and their call sites are exactly what the
    // pre-registration is about: "one shared formatter, used by BOTH runners" and "the af-fit dump must not enter
    // af_fit_summary.txt". A property nobody can execute is still a property that can be READ, and reading it is
    // strictly better than leaving the wave's control unguarded.

    private static string RunnerSource(string fileName, [CallerFilePath] string thisFile = null) {
        // .../Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Harness/ParamsDumpTests.cs -> .../TestApp/<file>
        var harness = Path.GetDirectoryName(thisFile);
        var solutionDir = Path.GetDirectoryName(Path.GetDirectoryName(harness));
        var path = Path.Combine(solutionDir ?? string.Empty, "TestApp", fileName);
        // FAIL, never skip: a test that cannot look must not report a pass.
        Assert.That(File.Exists(path), Is.True, $"could not read the runner source at {path}");
        return File.ReadAllText(path);
    }

    [Test]
    public void BothRunners_DumpThroughTheSharedFormatter_AndNeitherHandRollsItsOwn() {
        // MUTANT: give one side its own printout — e.g. leave `optimize`'s five-field line as the whole dump, or
        // hand-roll `Console.WriteLine("PARAMS-DUMP optimize/baseline BEGIN")` plus a literal field list. Two
        // printers is precisely the condition RULE P16 exists to rule out: the diff would then measure the
        // printers' drift instead of the detectors' difference, and it would do so silently.
        var afFit = RunnerSource("AfFitDiagnosticRunner.cs");
        var optimize = RunnerSource("OptimizationDiagnosticRunner.cs");

        Assert.Multiple(() => {
            Assert.That(afFit, Does.Contain("ParamsDump.Write("), "af-fit does not call the shared formatter");
            Assert.That(optimize, Does.Contain("ParamsDump.Write("), "optimize does not call the shared formatter");
            Assert.That(afFit, Does.Not.Contain("\"PARAMS-DUMP"), "af-fit hand-rolls a dump of its own");
            Assert.That(optimize, Does.Not.Contain("\"PARAMS-DUMP"), "optimize hand-rolls a dump of its own");
        });
    }

    [Test]
    public void Optimize_DumpsBothItsBaselineAndItsSeedBundle() {
        // MUTANT: dump only one of them. They are two DIFFERENT objects — baseline is the user's current settings
        // (the bundle af-fit is compared against) and seed is what the search starts from — so dumping one and
        // labelling it "optimize" would make the diff read the wrong bundle half the time.
        var optimize = RunnerSource("OptimizationDiagnosticRunner.cs");
        Assert.Multiple(() => {
            Assert.That(optimize, Does.Match(@"ParamsDump\.Write\([^;]*ParamsDump\.OptimizeBaseline\s*,\s*baseline\s*\)"));
            Assert.That(optimize, Does.Match(@"ParamsDump\.Write\([^;]*ParamsDump\.OptimizeSeed\s*,\s*seed\s*\)"));
        });
    }

    [Test]
    public void AfFit_DumpsToTheConsole_NeverThroughEmit_BecauseClauseW1IsAByteComparison() {
        // MUTANT: change the af-fit sink from `Console.WriteLine` to the runner's `Emit`. Emit appends to the
        // StringBuilder that becomes af_fit_summary.txt, and wave 16's clause W1 is a BYTE comparison of that file
        // against wave 14's control rung. The mutant breaks the wave's control SILENTLY: the dump still appears in
        // the log, the arm still runs, and only the control's identity — the thing that certifies item A changed
        // nothing at its default — quietly stops holding.
        var afFit = RunnerSource("AfFitDiagnosticRunner.cs");
        var calls = Regex.Matches(afFit, @"ParamsDump\.Write\(\s*([A-Za-z0-9_.]+)\s*,");

        Assert.That(calls.Count, Is.GreaterThan(0), "af-fit does not call ParamsDump.Write at all");
        Assert.Multiple(() => {
            foreach (Match call in calls) {
                Assert.That(call.Groups[1].Value, Is.EqualTo("Console.WriteLine"),
                    "the af-fit dump must go to the console; Emit would put it in af_fit_summary.txt");
            }
        });
    }

    // ---- F69(b): the third block, and the only property that makes it worth printing ------------------------------

    //   score_d20_w20.py: RX_NOOP = re.compile(r"--apply-run-detection-binning: ACCEPTED NO-OP")
    private static readonly Regex ScorerNoOpNotice = new Regex(@"--apply-run-detection-binning: ACCEPTED NO-OP");

    /// <summary>The ONE invocation of the F39(b) mutation. The declaration wraps its parameter list, so this
    /// pattern cannot match it — which is what lets the test count call sites.</summary>
    private static readonly Regex BinningMutationCall =
        new Regex(@"(?<![\w.])ApplyRunDetectionBinningIfRequested\(ctx,");

    private static readonly Regex DetectedDump =
        new Regex(@"ParamsDump\.Write\(\s*Console\.WriteLine\s*,\s*ParamsDump\.OptimizeDetected\s*,\s*ctx\.Baseline\s*\)");

    [Test]
    public void Optimize_DumpsTheDetectedBundle_AFTER_TheBinningMutation_AndExactlyOnce() {
        // THE test for item D1. MUTANT M-T5 — move the ParamsDump.Write above ApplyRunDetectionBinningIfRequested
        // (or above the per-run PixelScale assignment): the block still appears on 8 of 8 gate logs, G20-P2a and
        // G20-P2b still pass, and the dump is a byte-for-byte copy of optimize/baseline. That is self-test #3 of
        // score_d20_w20.py, the wrong-site mutant, and F69's own words for it are "a dump identical to the one
        // above it is a dump that has not been demonstrated". Nothing else in this project would catch it: the
        // difference only shows on a dataset whose resolved factor is not 1, and there are none on the gate.
        //
        // MUTANT M-T5' — dump ctx.Seed instead of ctx.Baseline: D20-D and D20-E compare against optimize/baseline,
        // so the difference set would pick up every field on which the search's seed differs from the user's
        // settings, and the wave would read a bundle mix-up as a mutation.
        //
        // "Exactly once" is clause G20-P2a's threshold and not a tidiness preference: a second call site is a block
        // emitted twice per log, `one_block` raises, and a saved `grep -l` reads it as success.
        var optimize = RunnerSource("OptimizationDiagnosticRunner.cs");
        var dump = DetectedDump.Matches(optimize);
        var mutation = BinningMutationCall.Matches(optimize);

        Assert.Multiple(() => {
            Assert.That(dump.Count, Is.EqualTo(1),
                "the detected bundle must be dumped from exactly one call site (G20-P2a is '== 1', not '>= 1')");
            // The design's source finding, asserted rather than recounted by hand: ONE call site, inside RunPerRun.
            // The joint path never calls it, so the joint path carries no optimize/detected block — which is why
            // this test pins the count instead of merely pinning the order.
            Assert.That(mutation.Count, Is.EqualTo(1),
                "ApplyRunDetectionBinningIfRequested must have exactly one call site");
            Assert.That(dump[0].Index, Is.GreaterThan(mutation[0].Index),
                "the detected dump must be taken AFTER the binning mutation, or it is optimize/baseline twice");
            Assert.That(optimize.IndexOf("ParamsDump.Write(Console.WriteLine, ParamsDump.OptimizeBaseline", StringComparison.Ordinal),
                Is.LessThan(mutation[0].Index),
                "optimize/baseline must stay at the CONSTRUCTION site; it is the pre-mutation side of D20-D");
        });
    }

    [Test]
    public void OptimizesThreeParamsDumps_AllGoToTheConsole_AndNoneIntoASummaryFile() {
        // MUTANT M-T6: send the new dump through a summary sink (the runner builds aggregate_summary.txt and
        // optimize_summary.txt from StringBuilders). The block would then be absent from the LOG, which is the only
        // artifact clause G20-P2 reads — an arm that produced the data and no way to see it. Wave 16's clause W1 is
        // the same lesson from the af-fit side, where a summary file was under BYTE comparison.
        var optimize = RunnerSource("OptimizationDiagnosticRunner.cs");
        var calls = Regex.Matches(optimize, @"ParamsDump\.Write\(\s*([A-Za-z0-9_.]+)\s*,\s*ParamsDump\.([A-Za-z]+)\s*,");

        Assert.That(calls.Count, Is.EqualTo(3), "optimize dumps exactly three bundles: baseline, seed, detected");
        Assert.Multiple(() => {
            foreach (Match call in calls) {
                Assert.That(call.Groups[1].Value, Is.EqualTo("Console.WriteLine"),
                    $"the {call.Groups[2].Value} dump must go to the console, never into a summary file");
            }
            Assert.That(calls.Select(c => c.Groups[2].Value),
                Is.EquivalentTo(new[] { "OptimizeBaseline", "OptimizeSeed", "OptimizeDetected" }));
        });
    }

    [Test]
    public void TheF69cNotice_IsEmittedOnlyBehindTheFlagGuard_AndInTheShapeTheScorerParses() {
        // Item D2. The notice says the OPT-IN flag is an accepted no-op and that F39(b) has been the DEFAULT since
        // wave 8. Clause D20-C requires it EXACTLY ONCE in the probe log — the probe is the only arm that passes
        // the flag — and requires the text to name both the flag and the default.
        //
        // NOTE ON WHAT "EXACTLY ONCE" IS KEYED ON, because it is easy to misread: the notice is keyed on the FLAG
        // BEING PASSED, not on the resolved factor being != 1. The resolved factor and the rescaled PixelScale are
        // reported by the pre-existing per-run F39(b) line, which is what clauses D20-V1 and D20-B parse. The gate
        // passes no flag, so the notice is absent from all eight gate logs — which is the "not at all" direction.
        //
        // MUTANT M-T7: drop the `if` guard. The line then appears on every arm this project runs, D20-C still
        // passes on the probe, and the claim the notice exists to make — "this flag changed nothing" — is printed
        // on runs where the flag was never passed. MUTANT M-T7': reword the prefix; RX_NOOP stops matching and
        // D20-C fails as a FAIL rather than a could-not-look, because the flag WAS passed and the probe DID run.
        var optimize = RunnerSource("OptimizationDiagnosticRunner.cs");
        var guarded = Regex.Match(
            optimize,
            @"if \(DiagnosticUtil\.HasFlag\(args, ""--apply-run-detection-binning""\)\) \{\s*"
            + @"Console\.WriteLine\((?<arg>.*?)\);\s*\}",
            RegexOptions.Singleline);
        Assert.That(guarded.Success, Is.True,
            "the F69(c) notice must sit behind a HasFlag(--apply-run-detection-binning) guard");

        var notice = string.Concat(Regex.Matches(guarded.Groups["arg"].Value, "\"([^\"]*)\"")
            .Select(m => m.Groups[1].Value));

        Assert.Multiple(() => {
            Assert.That(ScorerNoOpNotice.IsMatch(notice), Is.True,
                "score_d20_w20.py's RX_NOOP does not match the emitted notice");
            Assert.That(notice, Does.Contain("--apply-run-detection-binning"));
            Assert.That(notice, Does.Contain("--no-run-detection-binning"));
            Assert.That(notice, Does.Contain("adopted as the DEFAULT"));
            Assert.That(notice, Does.Contain("F69(c)"));
            Assert.That(notice.All(c => c >= ' ' && c <= '~'), Is.True,
                "ASCII only: a Unicode character reaches a redirected log as the single byte 0x1A on this machine");
            Assert.That(ScorerNoOpNotice.Matches(optimize).Count, Is.EqualTo(1),
                "the notice must be emitted from one place, or D20-C's 'exactly once' counts a source duplicate");
            Assert.That(ScorerNoOpNotice.Match(optimize).Index, Is.InRange(guarded.Index, guarded.Index + guarded.Length),
                "the only occurrence must be the guarded one");
        });
    }

    // ---- F69(a) wave 21 item A: the polarity of the F39(b) flags, guarded mechanically ----------------------------
    //
    // F39(b) is ON BY DEFAULT (`bool applyRunDetectionBinning = !HasFlag(args, "--no-run-detection-binning")`), and
    // `--apply-run-detection-binning` is an accepted no-op. Two comments in TestApp said the opposite. They were
    // PARAPHRASES sharing only the flag name ("only under …" at :405-419, fixed in wave 18; "No-op unless … was
    // passed" in the XML doc at :591-594, fixed here), so wave 20's lesson "grep for the sentence" would have missed
    // the second exactly as the edit-at-the-address did. The rule that works is: grep for the IDENTIFIER — and then
    // leave a test that does it for you. This is that test.

    private const string BinningOptInFlag = "--apply-run-detection-binning";
    private const string BinningOptOutFlag = "--no-run-detection-binning";

    private static IReadOnlyList<string> TestAppSourceFiles([CallerFilePath] string thisFile = null) {
        var harness = Path.GetDirectoryName(thisFile);
        var solutionDir = Path.GetDirectoryName(Path.GetDirectoryName(harness));
        var testApp = Path.Combine(solutionDir ?? string.Empty, "TestApp");
        // FAIL, never skip: a test that cannot look must not report a pass.
        Assert.That(Directory.Exists(testApp), Is.True, $"could not read the TestApp sources at {testApp}");
        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(testApp, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{sep}obj{sep}") && !p.Contains($"{sep}bin{sep}"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.That(files, Is.Not.Empty, $"no *.cs found under {testApp}");
        return files;
    }

    /// <summary>The comment text on one line — <c>//</c> or <c>///</c>, leading or trailing — or null if the line
    /// carries none. String and char literals are skipped, so `Console.WriteLine("--apply-…")` and a "http://" in a
    /// message are not mistaken for comments; that distinction is the whole point, since the runner PRINTS the
    /// correct polarity on lines whose comments used to deny it.</summary>
    private static string CommentTextOf(string line) {
        bool inString = false, verbatim = false, inChar = false;
        for (var i = 0; i < line.Length; i++) {
            var c = line[i];
            if (inString) {
                if (verbatim) {
                    if (c == '"') {
                        if (i + 1 < line.Length && line[i + 1] == '"') { i++; } else { inString = false; verbatim = false; }
                    }
                } else if (c == '\\') { i++; } else if (c == '"') { inString = false; }
                continue;
            }
            if (inChar) {
                if (c == '\\') { i++; } else if (c == '\'') { inChar = false; }
                continue;
            }
            if (c == '@' && i + 1 < line.Length && line[i + 1] == '"') { inString = true; verbatim = true; i++; continue; }
            if (c == '"') { inString = true; continue; }
            if (c == '\'') { inChar = true; continue; }
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') { return line.Substring(i); }
        }
        return null;
    }

    private sealed class CommentBlock {
        public string File;
        public int StartLine;
        public int EndLine;
        public string Text;
    }

    /// <summary>Maximal runs of adjacent comment-bearing lines. A paragraph is the unit because the polarity is a
    /// property of the paragraph, not of the line the flag name lands on: at :196-215 the opt-in is named on the
    /// first line and the opt-out nineteen lines later, and that comment is true.</summary>
    private static List<CommentBlock> CommentBlocks(string file) {
        var blocks = new List<CommentBlock>();
        var lines = File.ReadAllLines(file);
        CommentBlock current = null;
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Length; i++) {
            var comment = CommentTextOf(lines[i]);
            if (comment == null) {
                if (current != null) { current.Text = text.ToString(); blocks.Add(current); current = null; }
                continue;
            }
            if (current == null) {
                current = new CommentBlock { File = file, StartLine = i + 1 };
                text.Clear();
            }
            current.EndLine = i + 1;
            text.Append(comment).Append('\n');
        }
        if (current != null) { current.Text = text.ToString(); blocks.Add(current); }
        return blocks;
    }

    [Test]
    public void ApplyRunDetectionBinning_EveryCommentNamingTheOptInFlag_AlsoNamesTheOptOut() {
        // THE invariant: the behaviour is decided by the OPT-OUT, so any honest comment about the opt-in must name
        // the flag that actually controls it. Stated one-directionally on purpose — a comment may name only the
        // opt-out (the F69(c) notice's own comment does), because that one is never the backwards direction.
        //
        // MUTANT: restore ":591-594 — No-op unless <c>--apply-run-detection-binning</c> was passed". That block then
        // names the opt-in and not the opt-out, and this test goes red with the file and line. A THIRD block added
        // later that names only the opt-in fails the same way, which is the point: this guards additions, not just
        // the two copies known today.
        var files = TestAppSourceFiles();
        var withOptIn = files
            .SelectMany(CommentBlocks)
            .Where(b => b.Text.Contains(BinningOptInFlag, StringComparison.Ordinal))
            .ToList();

        TestContext.Out.WriteLine($"comment blocks naming {BinningOptInFlag}: {withOptIn.Count} "
            + $"(over {files.Count} *.cs under TestApp) — "
            + string.Join(", ", withOptIn.Select(b => $"{Path.GetFileName(b.File)}:{b.StartLine}-{b.EndLine}")));

        // Could-not-look, and it FAILS. A rename of the flag must break this test loudly rather than empty its
        // population and pass on zero occurrences (F66).
        Assert.That(withOptIn, Is.Not.Empty,
            $"the identifier this test is about ({BinningOptInFlag}) is gone from TestApp's comments; "
            + "it can no longer see its subject");
        Assert.That(withOptIn.Count, Is.GreaterThanOrEqualTo(2),
            $"expected at least the two known truthful sites to name {BinningOptInFlag}; "
            + $"found {withOptIn.Count} comment block(s)");

        var backwards = withOptIn
            .Where(b => !b.Text.Contains(BinningOptOutFlag, StringComparison.Ordinal))
            .Select(b => $"{b.File}:{b.StartLine}-{b.EndLine}\n{b.Text}")
            .ToList();
        Assert.That(backwards, Is.Empty,
            $"every comment naming {BinningOptInFlag} must also name {BinningOptOutFlag} — F39(b) is ON BY DEFAULT "
            + $"and the opt-in is an accepted no-op, so a comment that names only the opt-in reads the polarity "
            + $"backwards (F69(a); it cost RULE P16 its verdict in wave 16). Offending block(s):\n"
            + string.Join("\n\n", backwards));
    }
}
