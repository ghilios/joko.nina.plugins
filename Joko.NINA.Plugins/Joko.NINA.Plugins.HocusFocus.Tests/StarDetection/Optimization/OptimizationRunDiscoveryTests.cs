#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.IO;
using System.Linq;
using NUnit.Framework;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// Unit tests for the pure-logic run discovery of the T6 optimize harness (linked from TestApp), built purely
/// on directory structure + AF-frame filenames. The key behavior: the single-frame final/initial validation
/// captures must be EXCLUDED by the >=3-distinct-focuser-positions guard, frameless attempt* folders must be
/// skipped, and only true sweep folders count as runs.
/// </summary>
[TestFixture]
public class OptimizationRunDiscoveryTests {

    private string tempRoot;

    [SetUp]
    public void SetUp() {
        tempRoot = Path.Combine(Path.GetTempPath(), "hf-run-discovery-" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
    }

    [TearDown]
    public void TearDown() {
        if (tempRoot != null && Directory.Exists(tempRoot)) {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static string FrameName(int index, int focuser) =>
        $"{index}_Frame{index}_BitDepth16_Bayered0_Focuser{focuser}.fits";

    private static void WriteFrame(string dir, int index, int focuser) {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, FrameName(index, focuser)), "dummy");
    }

    [Test]
    public void Discover_ExcludesSingleFrameFinalInitialAndFramelessAttempt() {
        // setup/attempt01: a real sweep (3 distinct focuser positions) -> the ONLY usable run.
        var attempt01 = Path.Combine(tempRoot, "setup", "attempt01");
        WriteFrame(attempt01, 0, 5000);
        WriteFrame(attempt01, 1, 5100);
        WriteFrame(attempt01, 2, 5200);

        // setup/final: a single-frame validation capture (1 position) -> excluded by the >=3 guard.
        WriteFrame(Path.Combine(tempRoot, "setup", "final"), 0, 5050);

        // setup/initial: a single-frame validation capture (1 position) -> excluded by the >=3 guard.
        WriteFrame(Path.Combine(tempRoot, "setup", "initial"), 0, 5060);

        // setup/attempt02: an attempt folder with NO sweep frames (only artifacts) -> skipped.
        var attempt02 = Path.Combine(tempRoot, "setup", "attempt02");
        Directory.CreateDirectory(attempt02);
        File.WriteAllText(Path.Combine(attempt02, "0_Frame0_star_detection_result.json"), "{}");
        File.WriteAllText(Path.Combine(attempt02, "0_Frame0_annotated.png"), "img");

        var result = OptimizationRunDiscovery.Discover(tempRoot);

        Assert.That(result.Runs.Select(r => r.RunId), Is.EqualTo(new[] { "setup/attempt01" }),
            "Only the 3-position attempt01 sweep should be discovered as a run");
        Assert.That(result.Runs.Single().DistinctPositions, Is.EqualTo(3));

        // attempt02 has no frames -> skipped with the "no sweep frames" reason. final/initial are NOT attempt*
        // folders, so under the attempt-anchored branch they are simply not candidates (and therefore not even
        // listed as skipped) — the guarantee under test is that they are NOT discovered as runs.
        Assert.That(result.Runs.Any(r => r.RunId.Contains("final")), Is.False, "final must not be a run");
        Assert.That(result.Runs.Any(r => r.RunId.Contains("initial")), Is.False, "initial must not be a run");
        Assert.That(result.Skipped.Any(s => s.RelativePath == "setup/attempt02" && s.Reason.Contains("no sweep frames")),
            Is.True, "the frameless attempt02 must be skipped with a 'no sweep frames' reason");
    }

    [Test]
    public void Discover_BackCompat_RootItselfIsAnAttemptFolderWithEnoughPositions() {
        // Pointing --runs directly at an attempt folder (>=3 positions) treats the root as a single run.
        WriteFrame(tempRoot, 0, 4000);
        WriteFrame(tempRoot, 1, 4100);
        WriteFrame(tempRoot, 2, 4200);

        var result = OptimizationRunDiscovery.Discover(tempRoot);

        Assert.That(result.Runs.Count, Is.EqualTo(1));
        Assert.That(result.Runs.Single().DistinctPositions, Is.EqualTo(3));
    }

    [Test]
    public void Discover_FindsAttemptFoldersNestedSeveralLevelsDeep() {
        // astrodet/AutoFocus_x/attempt01 -> nested 2 levels under root.
        var nested = Path.Combine(tempRoot, "astrodet", "AutoFocus_x", "attempt01");
        WriteFrame(nested, 0, 6000);
        WriteFrame(nested, 1, 6100);
        WriteFrame(nested, 2, 6200);

        var result = OptimizationRunDiscovery.Discover(tempRoot);

        Assert.That(result.Runs.Select(r => r.RunId), Is.EqualTo(new[] { "astrodet/AutoFocus_x/attempt01" }));
    }

    [Test]
    public void Discover_Fallback_NoAttemptFolders_UsesSubfoldersWithEnoughPositions() {
        // No attempt* folders anywhere; an immediate subfolder with >=3 positions becomes a run, and one with
        // too few positions is skipped.
        var good = Path.Combine(tempRoot, "weird-layout");
        WriteFrame(good, 0, 7000);
        WriteFrame(good, 1, 7100);
        WriteFrame(good, 2, 7200);

        var tooFew = Path.Combine(tempRoot, "just-two");
        WriteFrame(tooFew, 0, 8000);
        WriteFrame(tooFew, 1, 8100);

        var result = OptimizationRunDiscovery.Discover(tempRoot);

        Assert.That(result.Runs.Select(r => r.RunId), Is.EqualTo(new[] { "weird-layout" }));
        Assert.That(result.Skipped.Any(s => s.RelativePath == "just-two" && s.Reason.Contains("need >=3")), Is.True);
    }
}
