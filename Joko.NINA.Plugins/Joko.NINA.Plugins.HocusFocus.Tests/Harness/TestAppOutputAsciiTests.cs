#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// TestApp's output must be ASCII, and this is a measured trap rather than a style preference.
///
/// <para><b>The failure it prevents fails CLOSED TO ZERO.</b> A single non-ASCII byte anywhere in a redirected log
/// makes GNU <c>grep</c> classify the WHOLE FILE as binary, at which point it reports no matches for perfectly
/// ordinary ASCII strings elsewhere in that file. The reader is told "the feature is absent", not "I could not
/// look". Wave 15 hit the first half of this with <c>HarnessSettingsStore</c>'s Unicode right arrow, which the
/// console code page encoded as the single byte 0x1A and which made a parser report could-not-look on 40 good
/// logs. Wave 23 hit the second and worse half: one <c>σ</c> (byte 0xE5) at ~offset 8000 of every gate log, from
/// the optimizer's <c>marginalSnr</c> objective line, made a gate driver read <c>0 of 8</c> PARAMS-DUMP blocks off
/// logs that held all eight — the Python scorer read 8 of 8 from the same files in the same minute. The byte was
/// present in every gate log of waves 18 through 23. It was latent for six waves because nothing checked.</para>
///
/// <para><b>Why "outside a comment" and not "on a Console.Write line".</b> The obvious guard —
/// <c>grep -P "[^\x00-\x7F]" *.cs | grep Console.Write</c> — was tried this wave and reported 35 emitting lines.
/// The real count is 120, and the miss is not incidental: the wave's OWN offending byte, at
/// <c>OptimizationDiagnosticRunner.cs:866</c>, sits on the second line of a multi-line
/// <c>Console.WriteLine(... + ...)</c> and carries no <c>Console.Write</c> text of its own, so the line-keyed grep
/// scored it absent. Seven further violations hid inside interpolation holes (<c>{"tilt°",8}</c>), where a naive
/// quote-parity scan flips polarity and stops seeing the literal at all. A guard against a fails-closed-to-zero
/// defect must not itself be able to fail closed to zero, so this one keys on the only property that is cheap to
/// decide correctly: OUTSIDE A COMMENT, every character of TestApp source is ASCII. Comments are exempt, which is
/// deliberate — the <c>Copyright ©</c> header and the em dashes in the doc comments are not output, and a guard
/// that failed on them would be deleted by the next person and would deserve to be.</para>
///
/// <para><b>Against the pre-change source this fixture fails</b> with 158 violating characters on 120 lines of 20
/// files, naming each one's file, line, column, character and code point.</para>
/// </summary>
[TestFixture]
public class TestAppOutputAsciiTests {

    /// <summary>Assembly metadata, never printed and never redirected into a log; its
    /// <c>AssemblyCopyright("Copyright © 2022")</c> is the one string literal under TestApp that is exempt.</summary>
    private static readonly string[] ExemptRelativePaths = { Path.Combine("Properties", "AssemblyInfo.cs") };

    /// <summary>Files that must be in the scan. A rename or a moved folder would otherwise shrink the scan
    /// silently, and a guard that stopped looking would keep reporting a pass.</summary>
    private static readonly string[] SentinelRelativePaths = {
        "OptimizationDiagnosticRunner.cs",
        "ParamsDump.cs",
        "TiltCalibrationRunner.cs",
        "BankVerifyRunner.cs",
    };

    // ---- the property -------------------------------------------------------------------------------------------

    [Test]
    public void EveryCharacterOfTestAppOutsideAComment_IsAscii() {
        var testApp = TestAppDirectory();
        var files = TestAppSourceFiles(testApp);

        var violations = new List<Violation>();
        var codeCharactersScanned = 0L;
        foreach (var path in files) {
            var source = ReadSourceWithoutBom(path);
            var isComment = CommentMask(source);
            codeCharactersScanned += isComment.Count(c => !c);
            violations.AddRange(ScanForNonAscii(Path.GetRelativePath(testApp, path), source, isComment));
        }

        // COULD-NOT-LOOK GETS ITS OWN FAILURE, AND IT IS ASSERTED BEFORE THE SCAN RESULT IS READ. A scan that found
        // nothing because it looked at nothing is the exact defect this fixture exists to prevent, so "no
        // violations" is only allowed to mean "clean" once we have proved the scan reached the source. Assert.That
        // throws, so nothing below runs until each of these holds.
        Assert.That(files, Has.Count.GreaterThanOrEqualTo(40),
            $"could not look: only {files.Count} *.cs found under {testApp}; TestApp has ~49");
        foreach (var sentinel in SentinelRelativePaths) {
            Assert.That(files.Any(p => Path.GetRelativePath(testApp, p) == sentinel), Is.True,
                $"could not look: {sentinel} is not in the scan, so the guard has stopped covering it");
        }
        Assert.That(codeCharactersScanned, Is.GreaterThan(200_000),
            $"could not look: only {codeCharactersScanned} non-comment characters were scanned across {files.Count} "
            + "files, so the comment mask has swallowed the source and every file would score clean");

        Assert.That(violations, Is.Empty, Describe(violations));
    }

    /// <summary>
    /// The scanner has to be able to FIND a violation, or the property above is vacuous. This runs it over a
    /// snippet holding one of each shape that matters — including the two the wave's own grep missed — and pins
    /// exactly which ones are violations and which are exempt.
    /// </summary>
    [Test]
    public void TheScanner_FindsEveryShapeOfViolation_AndExemptsComments() {
        const string snippet =
            "// Copyright © 2026 — a comment, exempt\n"                        // 1: exempt
            + "/// <summary>An XML doc comment — exempt.</summary>\n"          // 2: exempt
            + "/* a block comment ± exempt */\n"                              // 3: exempt
            + "Console.WriteLine(\"plain σ\");\n"                             // 4: violation
            + "Console.WriteLine(\n    $\"continued {x} floor=6σ\");\n"        // 6: violation, no Console.Write text
            + "Line($\"  {\"tilt°\",8} {\"mean\",10}\");\n"                    // 7: violation inside a hole
            + "var s = @\"verbatim µm\";\n"                                    // 8: violation
            + "var t = \"url http://x — not a comment\";\n"                    // 9: violation (// is inside a string)
            + "var u = \"clean\"; // trailing comment ©\n";                    // 10: exempt

        var found = ScanForNonAscii("Snippet.cs", snippet, CommentMask(snippet)).ToList();

        Assert.Multiple(() => {
            Assert.That(found.Select(v => v.Line), Is.EqualTo(new[] { 4, 6, 7, 8, 9 }),
                "the scanner must flag exactly the non-comment lines: 6 is the multi-line-call case the wave's grep "
                + "missed, 7 is the interpolation-hole case a quote-parity scan flips polarity on, and 9 proves a "
                + "'//' inside a string is not mistaken for a comment");
            Assert.That(found.Select(v => v.Character), Is.EqualTo(new[] { 'σ', 'σ', '°', 'µ', '—' }));
            Assert.That(found[0].Column, Is.EqualTo(26), "the column must point at the character itself");
            Assert.That(found[0].Describe(), Does.Contain("Snippet.cs:4"));
            Assert.That(found[0].Describe(), Does.Contain("U+03C3"));
        });
    }

    // ---- locating the source: FAIL, never skip ---------------------------------------------------------------------

    /// <summary>
    /// TestApp is a WPF Exe and is not referenced as an assembly, and only a handful of its pure-logic helpers are
    /// source-linked into this project (see the csproj), so the runners that print most of TestApp's output cannot
    /// be loaded and reflected over. Reading the source is therefore the only mechanism that covers all of it. The
    /// path is anchored on <see cref="CallerFilePathAttribute"/> rather than on the working directory or
    /// <c>AppContext.BaseDirectory</c>, so it is the same whether the run comes from <c>dotnet test</c>, an IDE, or
    /// a different drive — and it FAILS if it cannot find the directory. A test that cannot look must not pass.
    /// </summary>
    private static string TestAppDirectory([CallerFilePath] string thisFile = null) {
        // .../Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Harness/TestAppOutputAsciiTests.cs -> .../TestApp
        var harness = Path.GetDirectoryName(thisFile);
        var solutionDir = Path.GetDirectoryName(Path.GetDirectoryName(harness));
        var testApp = Path.Combine(solutionDir ?? string.Empty, "TestApp");
        Assert.That(Directory.Exists(testApp), Is.True,
            $"could not look: the TestApp sources are not at {testApp} (resolved from {thisFile})");
        return testApp;
    }

    private static IReadOnlyList<string> TestAppSourceFiles(string testApp) {
        var sep = Path.DirectorySeparatorChar;
        var exempt = new HashSet<string>(ExemptRelativePaths, StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(testApp, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{sep}obj{sep}") && !p.Contains($"{sep}bin{sep}"))
            .Where(p => !exempt.Contains(Path.GetRelativePath(testApp, p)))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.That(files, Is.Not.Empty, $"could not look: no *.cs found under {testApp}");
        return files;
    }

    private static string ReadSourceWithoutBom(string path) {
        var source = File.ReadAllText(path);
        // File.ReadAllText strips a UTF-8 BOM already; belt and braces, because a surviving U+FEFF is neither
        // output nor a defect, and flagging it would make this guard the boy who cried wolf.
        return source.Length > 0 && source[0] == '\uFEFF' ? source.Substring(1) : source;
    }

    // ---- the scan -----------------------------------------------------------------------------------------------

    private sealed class Violation {
        public string RelativePath;
        public int Line;
        public int Column;
        public char Character;
        public string SourceLine;

        /// <summary>Names the file, the line and the character, so a future violation is diagnosable from the
        /// failure alone — without re-deriving the scan or knowing this fixture exists.</summary>
        public string Describe() =>
            $"TestApp/{RelativePath.Replace('\\', '/')}:{Line} col {Column}: "
            + $"'{Character}' (U+{(int)Character:X4}) in: {SourceLine.Trim()}";
    }

    private static string Describe(IReadOnlyList<Violation> violations) {
        var sb = new StringBuilder();
        sb.Append(violations.Count).Append(" non-ASCII character(s) reach TestApp's output. A single one of these ")
          .Append("makes GNU grep call a whole redirected log binary, so it reports ZERO matches for ASCII strings ")
          .Append("elsewhere in that file -- 'the feature is absent' rather than 'I could not look'. Replace each ")
          .AppendLine("with its ASCII spelling (-- for an em dash, sigma, R^2, >=, ->, um, deg, x).");
        foreach (var v in violations) {
            sb.Append("  ").AppendLine(v.Describe());
        }
        return sb.ToString();
    }

    private static IEnumerable<Violation> ScanForNonAscii(string relativePath, string source, bool[] isComment) {
        var lineStart = 0;
        var line = 1;
        for (var i = 0; i < source.Length; i++) {
            if (source[i] == '\n') {
                line++;
                lineStart = i + 1;
                continue;
            }
            if (source[i] < 128 || isComment[i]) {
                continue;
            }
            var lineEnd = source.IndexOf('\n', i);
            yield return new Violation {
                RelativePath = relativePath,
                Line = line,
                Column = i - lineStart + 1,
                Character = source[i],
                SourceLine = source.Substring(lineStart, (lineEnd < 0 ? source.Length : lineEnd) - lineStart)
            };
        }
    }

    // ---- the lexer: find the comments, and be right about strings ------------------------------------------------
    //
    // Only comments have to be identified, because everything else is either printed or an identifier and both must
    // be ASCII. The work is in NOT mistaking a string for a comment (a "http://" in a message) and in NOT losing
    // track of where a string ends -- the failure that let `{"tilt°",8}` through the hand-rolled grep this wave.

    /// <summary>True at every index that is inside a <c>//</c> or <c>/* */</c> comment.</summary>
    private static bool[] CommentMask(string source) {
        var n = source.Length;
        var mask = new bool[n];
        var i = 0;
        while (i < n) {
            var c = source[i];
            if (c == '/' && i + 1 < n && source[i + 1] == '/') {
                while (i < n && source[i] != '\n') {
                    mask[i] = true;
                    i++;
                }
                continue;
            }
            if (c == '/' && i + 1 < n && source[i + 1] == '*') {
                var start = i;
                i += 2;
                while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/')) {
                    i++;
                }
                i = Math.Min(i + 2, n);
                for (var k = start; k < i; k++) {
                    mask[k] = true;
                }
                continue;
            }
            if (c == '"' && i + 2 < n && source[i + 1] == '"' && source[i + 2] == '"') {
                i = SkipRawString(source, i);
                continue;
            }
            if (c == '@' || c == '$') {
                var j = i;
                var verbatim = false;
                var interpolated = false;
                while (j < n && (source[j] == '@' || source[j] == '$')) {
                    if (source[j] == '@') { verbatim = true; } else { interpolated = true; }
                    j++;
                }
                i = j < n && source[j] == '"' ? SkipString(source, j + 1, verbatim, interpolated) : j;
                continue;
            }
            if (c == '"') {
                i = SkipString(source, i + 1, verbatim: false, interpolated: false);
                continue;
            }
            if (c == '\'') {
                i = SkipCharLiteral(source, i + 1);
                continue;
            }
            i++;
        }
        return mask;
    }

    private static int SkipRawString(string source, int i) {
        var n = source.Length;
        var quotes = 0;
        while (i + quotes < n && source[i + quotes] == '"') {
            quotes++;
        }
        var delimiter = new string('"', quotes);
        i += quotes;
        while (i < n && string.CompareOrdinal(source, i, delimiter, 0, quotes) != 0) {
            i++;
        }
        return Math.Min(i + quotes, n);
    }

    /// <summary>Index just past the closing quote. <paramref name="interpolated"/> makes it track <c>{}</c> depth so
    /// a quote inside an interpolation hole opens a NESTED literal instead of closing this one — the case
    /// <c>Line($"  {"tilt°",8} ...")</c> turns on, and the one a quote-parity scan gets backwards.</summary>
    private static int SkipString(string source, int i, bool verbatim, bool interpolated) {
        var n = source.Length;
        var depth = 0;
        while (i < n) {
            var c = source[i];
            if (interpolated && c == '{') {
                if (i + 1 < n && source[i + 1] == '{') { i += 2; continue; }
                depth++;
                i++;
                continue;
            }
            if (interpolated && c == '}') {
                if (i + 1 < n && source[i + 1] == '}') { i += 2; continue; }
                if (depth > 0) { depth--; }
                i++;
                continue;
            }
            if (!verbatim && c == '\\') { i += 2; continue; }
            if (c == '\'' && depth > 0) { i = SkipCharLiteral(source, i + 1); continue; }
            if (c == '"') {
                if (verbatim && i + 1 < n && source[i + 1] == '"') { i += 2; continue; }
                if (depth > 0) {
                    i = SkipString(source, i + 1, verbatim: false, interpolated: false);
                    continue;
                }
                return i + 1;
            }
            if (!verbatim && c == '\n' && depth == 0) { return i; }
            i++;
        }
        return n;
    }

    private static int SkipCharLiteral(string source, int i) {
        var n = source.Length;
        while (i < n) {
            if (source[i] == '\\') { i += 2; continue; }
            if (source[i] == '\'') { return i + 1; }
            if (source[i] == '\n') { return i; }
            i++;
        }
        return n;
    }
}
