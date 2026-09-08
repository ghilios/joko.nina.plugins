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

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

/// <summary>
/// Source-level guard that GPU acceleration stays OPTIMIZATION-ONLY (PR #209 review: "I want to be
/// convinced this isn't used in the regular path outside of optimization"). The GPU can only run when a
/// params bundle carries <c>AllowGpuAcceleration = true</c>, so the invariant reduces to: the ONLY
/// production sites allowed to set that flag are the two optimization stamping points — the wizard's run
/// loader and the TestApp optimize harness. Autofocus, sensor modeling, and single-frame detection build
/// their params through <c>BuildStarDetectorParams</c>/<c>BuildDefaultStarDetectorParams</c>, which never
/// touch the flag, so it stays at its <c>false</c> default there. A new assignment anywhere else fails
/// this test and must justify itself.
/// </summary>
[TestFixture]
public class GpuAccelerationScopeGuardTests {

    // The ONLY production files allowed to enable GPU acceleration on a params bundle:
    //  - RunEvaluationLoader.cs: the wizard's seed/baseline stamping (after GpuAccelerationPolicy).
    //  - OptimizationDiagnosticRunner.cs: the TestApp optimize harness (same policy; --gpu/--no-gpu force).
    private static readonly string[] AllowedAssignmentFiles = {
        "RunEvaluationLoader.cs",
        "OptimizationDiagnosticRunner.cs",
    };

    private static DirectoryInfo SolutionDirectory() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("TestApp").Length == 0) {
            dir = dir.Parent;
        }
        Assert.That(dir, Is.Not.Null, "Could not locate the solution directory from the test binaries.");
        return dir;
    }

    private static (string File, string Source)[] ProductionSources() =>
        new[] { "Joko.NINA.Plugins.HocusFocus", "TestApp" }
            .SelectMany(project => SolutionDirectory().GetDirectories(project).Single()
                .GetFiles("*.cs", SearchOption.AllDirectories))
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (f.Name, StripComments(File.ReadAllText(f.FullName))))
            .ToArray();

    private static string StripComments(string source) {
        var noBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(noBlock, @"//[^\r\n]*", string.Empty);
    }

    [Test]
    public void AllowGpuAcceleration_IsOnlyAssignedByTheOptimizationStampingSites() {
        // Any assignment counts (including "= false" and "= someBool"): a new site toggling the flag is a
        // scope change that must be reviewed, wherever it lands.
        var assignment = new Regex(@"\.AllowGpuAcceleration\s*=");
        var offenders = ProductionSources()
            .Where(s => assignment.IsMatch(s.Source) && !AllowedAssignmentFiles.Contains(s.File))
            .Select(s => s.File)
            .Distinct()
            .ToArray();
        Assert.That(offenders, Is.Empty,
            "AllowGpuAcceleration may only be set by the optimization stamping sites (wizard loader + " +
            "TestApp optimize harness). A new assignment site widens GPU scope beyond optimization — " +
            "autofocus, sensor modeling, and single-frame detection must never set it.");
    }

    [Test]
    public void OptionsToParamsMapping_NeverReferencesTheGpuFlag() {
        // The regular detection path builds params in HocusFocusStarDetection (BuildStarDetectorParams /
        // BuildDefaultStarDetectorParams). If the flag ever appears there, the regular path could enable
        // the GPU from persisted options — exactly what the scope decision forbids.
        var source = ProductionSources().Single(s => s.File == "HocusFocusStarDetection.cs").Source;
        Assert.That(source, Does.Not.Contain("AllowGpuAcceleration"),
            "HocusFocusStarDetection (the options→params mapping every regular detection uses) must not " +
            "reference AllowGpuAcceleration.");
    }

    [Test]
    public void GpuHost_IsOnlyConsultedFromTheSingleGate_InStarDetector() {
        // StarDetector consults GpuAccelerationHost.TryGetForBuild at exactly one site, behind the
        // p.AllowGpuAcceleration gate. More consult sites = more paths to audit.
        var sources = ProductionSources();
        var consulters = sources
            .Where(s => s.Source.Contains("TryGetForBuild"))
            .Select(s => s.File)
            .Distinct()
            .Where(f => f != "GpuAccelerationHost.cs")
            .ToArray();
        Assert.That(consulters, Is.EqualTo(new[] { "StarDetector.cs" }),
            "GpuAccelerationHost.TryGetForBuild must be consulted only by StarDetector's single gated site.");
        var starDetector = sources.Single(s => s.File == "StarDetector.cs").Source;
        Assert.That(Regex.Matches(starDetector, "TryGetForBuild").Count, Is.EqualTo(1),
            "StarDetector must consult the GPU host at exactly one (AllowGpuAcceleration-gated) site.");
    }
}
