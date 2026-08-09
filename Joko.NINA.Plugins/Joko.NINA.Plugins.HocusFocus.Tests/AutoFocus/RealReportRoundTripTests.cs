#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NUnit.Framework;
using System;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

/// <summary>
/// The loaded-report lookup, against a report THE PLUGIN ACTUALLY WROTE.
///
/// <para><b>Why this fixture is a real file and not a built object.</b> Wave 8 found that the loaded-run info
/// rows had never worked in the field while their tests passed, and the reason was the fixture:
/// <c>HocusFocusReport</c> carries <c>HocusFocusStarDetectionOptions</c> (<c>IStarDetectionOptions</c>),
/// <c>HocusFocusAutoFocusOptions</c> (<c>IAutoFocusOptions</c>) and <c>FocuserOptions</c>
/// (<c>IFocuserSettings</c>), and Newtonsoft cannot construct an interface. The test fixtures built reports
/// WITHOUT those three blocks, so their JSON had nothing unreadable in it, and the production writer's output
/// was never round-tripped by anything. **A test written against a hand-built object cannot find a defect that
/// only exists in what the product writes.**</para>
///
/// <para>So the fixture is a genuine report off a real run (paths sanitised, nothing else touched), and the
/// first test below asserts that the fixture STILL BREAKS the naive read — because a fixture that has stopped
/// exercising the defect turns this file into decoration.</para>
/// </summary>
[TestFixture]
public class RealReportRoundTripTests {

    private static string FixturePath() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AutoFocus", "Fixtures", "real-hocusfocus-report.json"))) {
            dir = dir.Parent;
        }
        Assert.That(dir, Is.Not.Null, "real-hocusfocus-report.json fixture not found from the test binaries.");
        return Path.Combine(dir.FullName, "AutoFocus", "Fixtures", "real-hocusfocus-report.json");
    }

    [Test]
    public void TheFixtureStillCarriesTheThreeInterfaceTypedBlocks_OrItProvesNothing() {
        var root = JObject.Parse(File.ReadAllText(FixturePath()));
        Assert.Multiple(() => {
            Assert.That(root["HocusFocusStarDetectionOptions"], Is.Not.Null);
            Assert.That(root["HocusFocusAutoFocusOptions"], Is.Not.Null);
            Assert.That(root["FocuserOptions"], Is.Not.Null);
        });
    }

    [Test]
    public void APlainDeserializeOfARealReportSTILLTHROWS() {
        // The defect, still live and still exactly this: Newtonsoft cannot construct an interface, so the
        // straightforward read of a real report fails every time, for every report this plugin has written.
        var json = File.ReadAllText(FixturePath());
        var ex = Assert.Throws<JsonSerializationException>(
            () => JsonConvert.DeserializeObject<HocusFocusReport>(json));
        Assert.That(ex.Message, Does.Contain("interface").Or.Contain("abstract"),
            "the fixture no longer exercises the defect this file exists for");
    }

    [Test]
    public void TheProductionLookupReadsTheSameFileAndKeepsTheInfoRowFIELDS() {
        var json = File.ReadAllText(FixturePath());
        var timestamp = JObject.Parse(json)["Timestamp"].Value<DateTime>();

        // The lookup keys on the timestamp in the FILE NAME, then requires an exact match on the report's own
        // Timestamp — so the fixture must be copied under the production naming scheme to be found at all.
        var dir = Path.Combine(Path.GetTempPath(), "hf-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var name = timestamp.ToString("yyyy-MM-dd--HH-mm-ss") + "--b10b1d6d-80eb-4dc4-9d89-f1fbf64c2b34.json";
            File.WriteAllText(Path.Combine(dir, name), json);

            var report = new AutoFocusReportDirectorySource(dir).TryFind(timestamp);

            Assert.That(report, Is.Not.Null, "a real report must be findable — this is the field defect");
            Assert.Multiple(() => {
                Assert.That(report.Timestamp, Is.EqualTo(timestamp), "the exact-match clause");
                Assert.That(report.FinalHFR, Is.EqualTo(1.9076325878204998).Within(1e-12),
                    "FinalHFR is not even on the type core deserializes; re-reading the report is the only route to it");
                Assert.That(report.InitialFocusPoint, Is.Not.Null);
                Assert.That(report.InitialFocusPoint.Position, Is.EqualTo(25000.0).Within(1e-9));
            });
        } finally {
            try {
                Directory.Delete(dir, recursive: true);
            } catch (IOException) {
                // a leaked temp dir must never fail a suite
            }
        }
    }
}
