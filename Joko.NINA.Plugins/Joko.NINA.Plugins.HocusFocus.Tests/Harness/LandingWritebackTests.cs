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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// F15 — <c>optimize --per-run</c> wrote its landing back into every discovered run's OWN source folder as well
/// as into <c>--out</c>, with no way to suppress it.
///
/// <para><b>What it cost.</b> It destroyed <c>bobp_m101</c>'s historical <c>sens 50 / clip 9.5</c> settings
/// mid-investigation, silently re-baselined BOTH banks in wave 3, and was the last thing forcing whole passes to
/// be serialized against one another — two passes over the same bank collide IN THE BANK however carefully their
/// <c>--out</c> directories are kept apart.</para>
///
/// <para><b>Why the capability is kept.</b> <c>bank-verify --opt-a/--opt-b</c> and
/// <c>golden eval --params optimized</c> read the RUN FOLDER copy by default, and <c>review --runs &lt;same&gt;</c>
/// auto-discovers it. The fix is not to delete the write — it is to make it something a command line SAYS.</para>
/// </summary>
[TestFixture]
public class LandingWritebackTests {

    private string dir;

    [SetUp]
    public void SetUp() {
        dir = Path.Combine(Path.GetTempPath(), "hf-f15-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown() {
        try {
            Directory.Delete(dir, recursive: true);
        } catch (IOException) {
            // A leaked temp dir must never fail a suite.
        }
    }

    private string Landing(string content) {
        var p = Path.Combine(dir, "optimized_settings.json");
        File.WriteAllText(p, content);
        return p;
    }

    // ---- the flag ----

    [Test]
    public void TheRunFolderWriteIsOFF_UnlessTheFlagIsGiven() {
        Assert.Multiple(() => {
            Assert.That(LandingWriteback.ShouldUpdateRunFolder(
                new[] { "optimize", "--per-run", "--runs", @"D:\Autofocus Bank", "--out", @"D:\x" }), Is.False,
                "the exact invocation thirteen waves used must NOT touch the bank any more");
            Assert.That(LandingWriteback.ShouldUpdateRunFolder(null), Is.False);
            Assert.That(LandingWriteback.ShouldUpdateRunFolder(Array.Empty<string>()), Is.False);
        });
    }

    [Test]
    public void TheFlagOptsIn_AndIsMatchedExactlyRatherThanByPrefix() {
        Assert.Multiple(() => {
            Assert.That(LandingWriteback.ShouldUpdateRunFolder(new[] { "optimize", "--update-run-folder" }), Is.True);
            Assert.That(LandingWriteback.ShouldUpdateRunFolder(new[] { "--UPDATE-RUN-FOLDER" }), Is.True,
                "flags elsewhere in this harness are case-insensitive; this one must not be the exception");
            // A prefix/substring match would make an unrelated future flag silently re-enable the write — which
            // is the shape of the defect, not a nuisance.
            Assert.That(LandingWriteback.ShouldUpdateRunFolder(new[] { "--update-run-folder-never" }), Is.False);
            Assert.That(LandingWriteback.ShouldUpdateRunFolder(new[] { "--no-update-run-folder" }), Is.False);
        });
    }

    // ---- the backup ----

    [Test]
    public void SnapshotPreservesTheFileThatWasThere() {
        var p = Landing("{\"historical\":\"sens 50 / clip 9.5\"}");
        var backup = LandingWriteback.SnapshotExistingLanding(p);

        Assert.That(backup, Is.Not.Null, "an existing landing must be preserved before it is overwritten");
        Assert.That(Path.GetFileName(backup), Is.EqualTo(LandingWriteback.DisplacedLandingFileName));
        Assert.That(File.ReadAllText(backup), Is.EqualTo("{\"historical\":\"sens 50 / clip 9.5\"}"));
    }

    [Test]
    public void TheBackupNameIsNotOneTheBankReadersMatch() {
        // Every bank reader locates a landing by the EXACT name "optimized_settings.json"
        // (BankVerifyRunner, BankExportSettingsRunner, golden eval --params optimized). A backup that shared it
        // would be read back AS a landing — the distinct name is load-bearing, not cosmetic.
        Assert.That(LandingWriteback.DisplacedLandingFileName, Is.Not.EqualTo("optimized_settings.json"));
        Assert.That(LandingWriteback.DisplacedLandingFileName, Does.EndWith(".json"));
    }

    [Test]
    public void TheBackupKeepsTheOLDESTDisplacedLanding_NotTheMostRecent() {
        // The file worth keeping is the one nobody can reproduce. Every landing written since is reproducible
        // from a recorded command line and survives in its own --out directory, so a ROLLING backup would lose
        // the irreplaceable file on the second pass and keep a reproducible one in its place.
        var p = Landing("HISTORICAL");
        var first = LandingWriteback.SnapshotExistingLanding(p);
        File.WriteAllText(p, "WAVE 13's LANDING");

        var second = LandingWriteback.SnapshotExistingLanding(p);

        Assert.Multiple(() => {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Null, "a second pass must NOT roll the backup forward");
            Assert.That(File.ReadAllText(Path.Combine(dir, LandingWriteback.DisplacedLandingFileName)),
                Is.EqualTo("HISTORICAL"), "the irreplaceable file is the one that must survive");
        });
    }

    [Test]
    public void SnapshotIsSilentAndHarmlessWhenThereIsNothingToPreserve() {
        Assert.Multiple(() => {
            Assert.That(LandingWriteback.SnapshotExistingLanding(Path.Combine(dir, "optimized_settings.json")), Is.Null);
            Assert.That(LandingWriteback.SnapshotExistingLanding(null), Is.Null);
            Assert.That(File.Exists(Path.Combine(dir, LandingWriteback.DisplacedLandingFileName)), Is.False,
                "no landing, no backup — a backup of nothing would be a file that lies about what was there");
        });
    }

    // ---- the source guard, and it checks itself first ----

    private const string GuardedLoop =
        @"for\s*\(\s*int\s+i\s*=\s*0\s*;\s*updateRunFolder\s*&&\s*i\s*<\s*loadedRuns\.Count\s*;";

    /// <summary>
    /// "What would this test do if the thing it checks were completely broken?" has the answer "sweep clean" for
    /// every source guard whose pattern has rotted, so the pattern is asserted in BOTH directions on literals
    /// before it is ever run against the tree.
    /// </summary>
    [Test]
    public void TheGuardsOwnPatternMatchesTheGatedLoopAndNotTheUngatedOne() {
        var gated = "for (int i = 0; updateRunFolder && i < loadedRuns.Count; i++) {";
        var ungated = "for (int i = 0; i < loadedRuns.Count; i++) {";
        Assert.Multiple(() => {
            Assert.That(Regex.IsMatch(gated, GuardedLoop), Is.True, "the guard cannot see its own required form");
            Assert.That(Regex.IsMatch(ungated, GuardedLoop), Is.False, "the guard would pass the banned form");
        });
    }

    [Test]
    public void TheRunFolderWriteLoopIsGatedOnTheOptIn() {
        var dirInfo = new DirectoryInfo(AppContext.BaseDirectory);
        while (dirInfo != null && dirInfo.GetDirectories("TestApp").Length == 0) {
            dirInfo = dirInfo.Parent;
        }
        Assert.That(dirInfo, Is.Not.Null, "Could not locate the solution directory from the test binaries.");

        var runner = dirInfo.GetDirectories("TestApp").Single()
            .GetFiles("OptimizationDiagnosticRunner.cs", SearchOption.AllDirectories).SingleOrDefault();
        Assert.That(runner, Is.Not.Null, "OptimizationDiagnosticRunner.cs not found — the guard cannot run.");

        var source = File.ReadAllText(runner.FullName);
        Assert.That(Regex.IsMatch(source, GuardedLoop), Is.True,
            "The per-run source-folder write loop must stay gated on updateRunFolder (F15). Removing the gate " +
            "silently re-baselines every run folder in both banks on every pass.");
    }
}
