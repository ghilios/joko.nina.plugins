#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// F58(d) — the seam every harness runner must build its AF fit through, and the guard that says they all do.
///
/// <para><b>The defect these protect.</b> Each runner built <c>StarDetectionOptions</c> on the harness accessor —
/// the pinned settings FILE — and then <c>new AutoFocusOptions(profileService)</c> two lines below it, binding the
/// FIT to whichever NINA profile was ACTIVE. So <c>--settings</c> pinned the detector and left the fit floating.
/// It is not academic: the machine that produced waves 5–11 holds nine profiles partitioning <b>2 / 7</b> on
/// <c>MaxOutlierRejections</c>, and that one integer produced the two discrete values of <c>J</c> that were chased
/// as a floating-point race for two waves.</para>
///
/// <para><b>Why a source guard and not only a contract test.</b> Wave 11 fixed <c>optimize</c> and left five call
/// sites carrying the same defect, each looking locally correct. A rule enforced by remembering it is a rule that
/// comes back; the ban is on the CONSTRUCTOR, so it holds for a site nobody has written yet.</para>
/// </summary>
[TestFixture]
public class HarnessFitSeamTests {

    /// <summary>
    /// A single-argument <c>new AutoFocusOptions(x)</c>. The two-argument overload — the accessor-bound one — has
    /// a comma, which is what separates the banned form from the required one.
    /// </summary>
    private const string SingleArgAutoFocusOptions = @"new\s+AutoFocusOptions\s*\(\s*[A-Za-z_][A-Za-z0-9_.]*\s*\)";

    private static DirectoryInfo SolutionDirectory() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("TestApp").Length == 0) {
            dir = dir.Parent;
        }
        Assert.That(dir, Is.Not.Null, "Could not locate the solution directory from the test binaries.");
        return dir;
    }

    private static FileInfo[] TestAppSources() {
        var files = SolutionDirectory().GetDirectories("TestApp").Single()
            .GetFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToArray();
        // An ABSENT check is more dangerous than a red one (F37): a path walk that finds nothing must fail here
        // rather than report a clean sweep of zero files.
        Assert.That(files, Is.Not.Empty, "Found no sources under TestApp — the path walk is wrong, not the code.");
        return files;
    }

    private static string StripComments(string source) {
        var noBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(noBlock, @"//[^\r\n]*", string.Empty);
    }

    [Test]
    public void TheGuardsOwnPatternFiresOnTheBannedFormAndNotOnTheRequiredOne() {
        // "What would this test do if the thing it checks were completely broken?" — wave 10 asked that of a trace
        // differ and the answer was "exactly the same thing". A source guard whose regex has rotted reports a clean
        // sweep, which is the most expensive way for a test to pass. So the pattern is exercised in BOTH directions
        // here, on literals, before it is trusted against the tree.
        Assert.Multiple(() => {
            Assert.That(Regex.IsMatch("var o = new AutoFocusOptions(profileService);", SingleArgAutoFocusOptions), Is.True,
                "the guard's pattern no longer matches the banned single-argument form — it would sweep clean while the defect is present");
            Assert.That(Regex.IsMatch("var o = new AutoFocusOptions(profileService, accessor);", SingleArgAutoFocusOptions), Is.False,
                "the guard's pattern matches the REQUIRED accessor-bound form, so it cannot distinguish the fix from the defect");
            Assert.That(Regex.IsMatch("var o = HarnessSettingsStore.BuildFitOptions(profileService, harnessSettings);", SingleArgAutoFocusOptions), Is.False);
        });
    }

    [Test]
    public void NoHarnessRunnerBuildsItsFitFromTheActiveProfile() {
        var offenders = TestAppSources()
            .Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f.FullName)), SingleArgAutoFocusOptions))
            .Select(f => f.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty,
            $"{string.Join(", ", offenders)} build AutoFocusOptions from the ACTIVE NINA profile. A harness runner's fit " +
            "must come from the pinned settings file: call HarnessSettingsStore.BuildFitOptions. (HocusFocusPlugin.cs is " +
            "NOT in this project and is deliberately exempt — in the live app the profile IS the user's settings.)");
    }

    // ---- the seam's own contract ------------------------------------------------------------------------------

    private static HarnessSettingsStore.Resolved ResolvedOver(params (string Key, string Value)[] pairs) {
        var bag = new Dictionary<string, string>();
        foreach (var (k, v) in pairs) {
            bag[k] = v;
        }
        return new HarnessSettingsStore.Resolved { Accessor = new HarnessSettingsStore.FileOptionsAccessor(bag) };
    }

    [Test]
    public void BuildFitOptions_ReadsTheFILE_OnAllFourAxes() {
        var options = HarnessSettingsStore.BuildFitOptions(Substitute.For<IProfileService>(), ResolvedOver(
            ("MaxOutlierRejections", "0"),
            ("OutlierRejectionConfidence", "0.99"),
            ("WeightedHyperbolicFitEnabled", "False"),
            ("HyperbolicFitModel", "TiltedHyperbola")));

        var inputs = HarnessFitInputs.From(options);
        Assert.Multiple(() => {
            Assert.That(inputs.MaxOutlierRejections, Is.EqualTo(0));
            Assert.That(inputs.RejectionConfidence, Is.EqualTo(0.99).Within(1e-12));
            Assert.That(inputs.UseWeights, Is.False);
            Assert.That(inputs.PreferredModel, Is.EqualTo(HyperbolicFitModel.TiltedHyperbola));
        });
    }

    [Test]
    public void BuildFitOptions_RefusesToFallBackToTheProfile() {
        // The failure this forbids is the SILENT one: a caller with no resolved settings getting a profile-backed
        // fit and a run that looks pinned. Throwing is the only reading that cannot be mistaken for a pinned run.
        Assert.Multiple(() => {
            Assert.Throws<ArgumentNullException>(() =>
                HarnessSettingsStore.BuildFitOptions(Substitute.For<IProfileService>(), null));
            Assert.Throws<ArgumentNullException>(() =>
                HarnessSettingsStore.BuildFitOptions(Substitute.For<IProfileService>(), new HarnessSettingsStore.Resolved()));
        });
    }
}
