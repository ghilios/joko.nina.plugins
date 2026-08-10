#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
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
    public void TheSourceTags_AreTheThreePreRegisteredStrings() {
        // MUTANT: rename a source ("affit/detector", "optimize-baseline", ...). The scorer looks the source up by
        // exact key — `collect(af_paths, "af-fit/detector")` — so a renamed tag is not a differently-named block,
        // it is NO block, on every log in the population.
        Assert.Multiple(() => {
            Assert.That(ParamsDump.AfFitDetector, Is.EqualTo("af-fit/detector"));
            Assert.That(ParamsDump.OptimizeBaseline, Is.EqualTo("optimize/baseline"));
            Assert.That(ParamsDump.OptimizeSeed, Is.EqualTo("optimize/seed"));
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
}
