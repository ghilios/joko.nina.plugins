#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.ImageData;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// The harness settings store. These cover the two properties the store exists to guarantee — that a run's
/// settings come from a file rather than mutable profile state, and that PixelScale comes from the DATA rather
/// than from whichever rig the local profile happens to describe.
/// </summary>
[TestFixture]
public class HarnessSettingsStoreTests {

    private static HarnessSettingsStore.FileOptionsAccessor Accessor(params (string Key, string Value)[] pairs) {
        var bag = new Dictionary<string, string>();
        foreach (var (k, v) in pairs) {
            bag[k] = v;
        }
        return new HarnessSettingsStore.FileOptionsAccessor(bag);
    }

    // ---- The accessor -------------------------------------------------------------------------------------

    [Test]
    public void Accessor_ReadsTypedValuesBackFromTheFileBag() {
        var a = Accessor(("BrightnessSensitivity", "33.3333"), ("StructureLayers", "5"),
                         ("DefocusAwareDonutDetection", "True"), ("MeasurementAverage", "Median"));
        Assert.Multiple(() => {
            Assert.That(a.GetValueDouble("BrightnessSensitivity", 10.0), Is.EqualTo(33.3333).Within(1e-9));
            Assert.That(a.GetValueInt32("StructureLayers", 4), Is.EqualTo(5));
            Assert.That(a.GetValueBoolean("DefocusAwareDonutDetection", false), Is.True);
        });
    }

    [Test]
    public void Accessor_FallsBackToTheDefault_ForAMissingOrUnparseableKey() {
        // A harness must never throw on a key it does not care about, and a corrupt value must not be silently
        // read as some other number — both degrade to the caller's stated default.
        var a = Accessor(("BrightnessSensitivity", "not-a-number"));
        Assert.Multiple(() => {
            Assert.That(a.GetValueDouble("BrightnessSensitivity", 10.0), Is.EqualTo(10.0));
            Assert.That(a.GetValueDouble("NeverWritten", 7.5), Is.EqualTo(7.5));
        });
    }

    [Test]
    public void Accessor_WritesStayInTheBag_AndSuppressWritesBlocksThem() {
        // Writes must never reach the user's profile as a side effect of a harness run.
        var bag = new Dictionary<string, string>();
        var a = new HarnessSettingsStore.FileOptionsAccessor(bag);
        a.SetValueDouble("BrightnessSensitivity", 12.5);
        Assert.That(bag["BrightnessSensitivity"], Is.EqualTo("12.5"));

        a.SuppressWrites = true;
        a.SetValueDouble("BrightnessSensitivity", 99.0);
        Assert.That(bag["BrightnessSensitivity"], Is.EqualTo("12.5"), "SuppressWrites must block the write");
    }

    [Test]
    public void Accessor_UsesInvariantCulture() {
        // A machine with a comma decimal separator must not read 33.3333 as 333333.
        var a = Accessor(("BrightnessSensitivity", "33.3333"));
        Assert.That(a.GetValueDouble("BrightnessSensitivity", 0.0), Is.EqualTo(33.3333).Within(1e-9));
    }

    // ---- PixelScale from the frame, not the profile -------------------------------------------------------

    private static ImageMetaData Meta(double pixelSize, double focalLength, int binX = 1) {
        var m = new ImageMetaData();
        m.Camera.PixelSize = pixelSize;
        m.Camera.BinX = binX;
        m.Telescope.FocalLength = focalLength;
        return m;
    }

    [Test]
    public void PixelScale_ComesFromTheFrameHeader_NotTheSettingsFile() {
        // The measured 3800mm case: the frame says 3.76um / 3800mm = 0.204 arcsec/px, while the local profile
        // described a 703mm rig at 1.10. Using the profile value under-reported the scale by 5x on this run, and
        // across the bank the true scales span 0.74-5.97 arcsec/px.
        var settings = new HarnessSettingsStore.Resolved { PixelSizeMicrons = 3.76, FocalLengthMm = 703.0, Path = "f.json" };
        var scale = HarnessSettingsStore.PixelScaleForFrame(Meta(3.76, 3800.0), settings, out var source);
        Assert.Multiple(() => {
            Assert.That(scale, Is.EqualTo(0.2041).Within(0.001));
            Assert.That(source, Is.EqualTo("frame header"));
        });
    }

    [Test]
    public void PixelScale_ScalesWithBinning() {
        var scale = HarnessSettingsStore.PixelScaleForFrame(Meta(3.76, 3800.0, binX: 2), null, out _);
        Assert.That(scale, Is.EqualTo(0.4082).Within(0.001));
    }

    [Test]
    public void PixelScale_FallsBackToTheSettingsFile_WhenTheHeaderHasNone() {
        var settings = new HarnessSettingsStore.Resolved { PixelSizeMicrons = 3.76, FocalLengthMm = 585.0, Path = "f.json" };
        var scale = HarnessSettingsStore.PixelScaleForFrame(Meta(double.NaN, double.NaN), settings, out var source);
        Assert.Multiple(() => {
            Assert.That(scale, Is.EqualTo(1.3257).Within(0.001));
            Assert.That(source, Does.Contain("settings file"));
        });
    }

    [Test]
    public void PixelScale_IsNaN_WhenNeitherSourceHasIt_RatherThanFabricated() {
        // A frame with no scale information must not be handed an invented one: NaN is what the previous
        // profile path produced too, and PixelScale-dependent gates already handle it.
        var scale = HarnessSettingsStore.PixelScaleForFrame(Meta(double.NaN, double.NaN), null, out var source);
        Assert.Multiple(() => {
            Assert.That(double.IsNaN(scale), Is.True);
            Assert.That(source, Does.Contain("unavailable"));
        });
    }

    // ---- F42: a new build directory must INHERIT settings, not bootstrap its own ---------------------------

    [Test]
    public void DefaultPath_UsesAFileDeliberatelyPlacedBesideTheExe() {
        // Back-compat, and the escape hatch: an arm directory that already carries its own file keeps using it.
        var chosen = HarnessSettingsStore.ResolveDefaultPath(
            besideExe: @"D:\arm3\harness_settings.json",
            shared: @"C:\Users\x\AppData\Local\HocusFocusHarness\harness_settings.json",
            exists: p => p == @"D:\arm3\harness_settings.json");
        Assert.That(chosen, Is.EqualTo(@"D:\arm3\harness_settings.json"));
    }

    [Test]
    public void DefaultPath_FallsBackToThePerUserFile_SoANewBuildDirectoryInherits() {
        // F42's actual defect: the bank run instructions require a fresh -o directory per arm (the exe is
        // file-locked during a run), and the old "always beside the exe" default meant every one of them
        // bootstrapped its OWN detector settings from whatever the live profile held at that moment. Two arms
        // built minutes apart then differed in UseOptimizedSettings / LocallyAdaptiveBinarization and no
        // comparison between them meant anything.
        var chosen = HarnessSettingsStore.ResolveDefaultPath(
            besideExe: @"D:\arm4\harness_settings.json",
            shared: @"C:\Users\x\AppData\Local\HocusFocusHarness\harness_settings.json",
            exists: _ => false);
        Assert.That(chosen, Is.EqualTo(@"C:\Users\x\AppData\Local\HocusFocusHarness\harness_settings.json"));
    }

    // ---- F42/F43: a Simple-mode settings file silently ignores its own advanced knobs ----------------------

    [Test]
    public void SimpleModePresetOverrides_NamesTheKnobsThePresetsOverwrite() {
        // With UseAdvanced=False, StarDetectionOptions recomputes the advanced knobs from the Simple_* presets,
        // so these recorded values never reach the detector. Wave 5 lost four probe runs to exactly this: the
        // configurations differed on paper and returned identical star counts.
        var overrides = HarnessSettingsStore.SimpleModePresetOverrides(
            new Dictionary<string, string> {
                ["UseAdvanced"] = "False",
                ["NoiseClippingMultiplier"] = "1",
                ["StarClippingMultiplier"] = "9.5",
                ["MinHFR"] = "0.1",
            },
            Substitute.For<IProfileService>());
        Assert.Multiple(() => {
            Assert.That(overrides.Count, Is.EqualTo(3));
            Assert.That(string.Join("|", overrides), Does.Contain("MinHFR"));
            Assert.That(string.Join("|", overrides), Does.Contain("NoiseClippingMultiplier"));
            Assert.That(string.Join("|", overrides), Does.Contain("StarClippingMultiplier"));
        });
    }

    [Test]
    public void SimpleModePresetOverrides_IsSilentInAdvancedMode() {
        // The mirror image, and the reason the warning is conditional: in Advanced mode the file IS the detector.
        var overrides = HarnessSettingsStore.SimpleModePresetOverrides(
            new Dictionary<string, string> {
                ["UseAdvanced"] = "True",
                ["NoiseClippingMultiplier"] = "1",
                ["MinHFR"] = "0.1",
            },
            Substitute.For<IProfileService>());
        Assert.That(overrides, Is.Empty);
    }

    // ---- F39: a per-run file must not present an inherited binning as a derived one -----------------------

    private static (string Path, string Json) ResolveForRunInTempDir(double inFocusHfr) {
        var dir = Path.Combine(Path.GetTempPath(), "hf_harness_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var baseSettings = new HarnessSettingsStore.Resolved {
                Accessor = new HarnessSettingsStore.FileOptionsAccessor(
                    new Dictionary<string, string> { ["DetectionBinning"] = "Bin2" }),
                PixelSizeMicrons = 3.76,
                FocalLengthMm = 585.0,
            };
            var resolved = HarnessSettingsStore.ResolveForRun(dir, baseSettings, Meta(3.76, 585.0), inFocusHfr);
            return (resolved.Path, File.ReadAllText(resolved.Path));
        } finally {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Test]
    public void ResolveForRun_MarksAnInheritedDetectionBinningAsKeptFromBase() {
        // F39's defect in one line: EVERY synthetic-bank file says "DetectionBinning": "Bin2" and not one of them
        // derived it — no bank folder has the autofocus_report_Region0.json the derivation needs, so all of them
        // inherited it from a profile export while looking exactly like the derived case.
        var (_, json) = ResolveForRunInTempDir(inFocusHfr: double.NaN);
        Assert.Multiple(() => {
            Assert.That(json, Does.Contain(HarnessSettingsStore.DetectionBinningKeptFromBase));
            Assert.That(json, Does.Not.Contain(HarnessSettingsStore.DetectionBinningDerived));
        });
    }

    [Test]
    public void ResolveForRun_MarksAMeasuredDetectionBinningAsDerived() {
        var (_, json) = ResolveForRunInTempDir(inFocusHfr: 8.0);
        Assert.That(json, Does.Contain(HarnessSettingsStore.DetectionBinningDerived));
    }

    // ---- F39(b): the factor a run should actually be DETECTED at ------------------------------------------

    // A dataset folder holding synthetic_meta.json, with an attempt folder inside it — the real bank layout, since
    // the resolver has to look in the run folder's PARENT.
    private static (string DatasetDir, string RunDir) BankLayout(int? expectedDetectionBinning) {
        var datasetDir = Path.Combine(Path.GetTempPath(), "hf_bank_" + Guid.NewGuid().ToString("N"));
        var runDir = Path.Combine(datasetDir, "attempt01");
        Directory.CreateDirectory(runDir);
        if (expectedDetectionBinning.HasValue) {
            File.WriteAllText(Path.Combine(datasetDir, "synthetic_meta.json"),
                "{\"expectedOptimal\":{\"detectionBinning\":" + expectedDetectionBinning.Value + ",\"stepSizeSteps\":82}}");
        }
        return (datasetDir, runDir);
    }

    [Test]
    public void ResolveRunDetectionBinningFactor_PrefersTheDatasetsPhysicsDerivedExpectation() {
        // The seven detectionBinning=2 datasets: their settings file says Bin2 too, but it INHERITED it. The
        // authoritative value is the dataset's own derivation — and the one its exposure was derived at.
        var (datasetDir, runDir) = BankLayout(expectedDetectionBinning: 2);
        try {
            var settingsSayBin1 = new HarnessSettingsStore.Resolved { Accessor = Accessor(("DetectionBinning", "Bin1")) };
            var factor = HarnessSettingsStore.ResolveRunDetectionBinningFactor(runDir, settingsSayBin1, out var source);
            Assert.Multiple(() => {
                Assert.That(factor, Is.EqualTo(2));
                Assert.That(source, Does.Contain("synthetic_meta.json"),
                    "the run log must say WHICH source decided, or an inherited guess is honoured silently again");
            });
        } finally {
            try { Directory.Delete(datasetDir, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public void ResolveRunDetectionBinningFactor_FallsBackToTheSettingsFileAndSaysSo() {
        // The real bank has no synthetic_meta.json, so its runs fall through here — and the message has to warn
        // that the value may be kept-from-base rather than derived (F39 part (a)'s field).
        var (datasetDir, runDir) = BankLayout(expectedDetectionBinning: null);
        try {
            var settings = new HarnessSettingsStore.Resolved { Accessor = Accessor(("DetectionBinning", "Bin2")) };
            var factor = HarnessSettingsStore.ResolveRunDetectionBinningFactor(runDir, settings, out var source);
            Assert.Multiple(() => {
                Assert.That(factor, Is.EqualTo(2));
                Assert.That(source, Does.Contain("kept-from-base"));
            });
        } finally {
            try { Directory.Delete(datasetDir, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public void ResolveRunDetectionBinningFactor_AnswersFromTheDatasetWithNoResolvedSettingsAtAll() {
        // Wave 8: `golden eval` calls this with a NULL Resolved, purely to report when the factor it is scoring at
        // disagrees with the run's own physics -- it has no settings bundle in hand at that point and must not need
        // one. The dataset's own derivation is sufficient, and passing null must not throw.
        var (datasetDir, runDir) = BankLayout(expectedDetectionBinning: 2);
        try {
            var factor = HarnessSettingsStore.ResolveRunDetectionBinningFactor(runDir, null, out var source);
            Assert.Multiple(() => {
                Assert.That(factor, Is.EqualTo(2));
                Assert.That(source, Does.Contain("synthetic_meta.json"));
            });
        } finally {
            try { Directory.Delete(datasetDir, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public void ResolveRunDetectionBinningFactor_ResolvesTo1WithNeitherSource() {
        // The value every run to date has actually used. A missing answer must not become a fabricated factor.
        var (datasetDir, runDir) = BankLayout(expectedDetectionBinning: null);
        try {
            Assert.That(HarnessSettingsStore.ResolveRunDetectionBinningFactor(runDir, null, out _), Is.EqualTo(1));
        } finally {
            try { Directory.Delete(datasetDir, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public void ApplyFactor_CarriesTheFactorIntoPixelScale() {
        // Why the factor is applied through DetectionBinningResolver rather than written to the field: PixelScale
        // carries it (HocusFocusStarDetection.ApplyDetectionImageContext = pixelScale * softwareBinning). A raw
        // field write would evaluate every pixel-scale-dependent gate at half the scale being analyzed.
        var p = new StarDetectorParams { PixelScale = 0.277, DetectionBinning = 1 };
        DetectionBinningResolver.ApplyFactor(p, 2);
        Assert.Multiple(() => {
            Assert.That(p.DetectionBinning, Is.EqualTo(2));
            Assert.That(p.PixelScale, Is.EqualTo(0.554).Within(1e-12));
        });
        // ...and it is idempotent, which is what lets the per-run loop normalize back to 1 between datasets.
        DetectionBinningResolver.ApplyFactor(p, 1);
        Assert.Multiple(() => {
            Assert.That(p.DetectionBinning, Is.EqualTo(1));
            Assert.That(p.PixelScale, Is.EqualTo(0.277).Within(1e-12));
        });
    }

    [Test]
    public void SimpleModePresetOverrides_IsEmptyWhenTheFileAgreesWithThePresets() {
        // The wave-5 pinned file's case: its advanced values coincide with the Typical presets, so no wave-5 arm
        // was invalidated. The hazard is real but did not fire, and the diff has to be able to say so.
        var overrides = HarnessSettingsStore.SimpleModePresetOverrides(
            new Dictionary<string, string> {
                ["UseAdvanced"] = "False",
                ["Simple_NoiseLevel"] = "Typical",
                ["Simple_PixelScale"] = "Typical",
                ["Simple_FocusRange"] = "Typical",
                ["NoiseClippingMultiplier"] = "4",
                ["StarClippingMultiplier"] = "2",
                ["StructureLayers"] = "4",
                ["MinHFR"] = "1.2",
            },
            Substitute.For<IProfileService>());
        Assert.That(overrides, Is.Empty);
    }
}
