#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using System.Globalization;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// F58 — the four values that reach the AF fit, and the guarantee that <c>--settings</c> now pins them.
///
/// <para><b>What these are protecting.</b> <c>optimize</c> pinned the DETECTOR knobs with <c>--settings</c> and
/// read the fit's four inputs straight off <c>AutoFocusOptions</c>, which binds to whichever NINA profile is
/// ACTIVE. The machine that produced waves 5–11 holds nine profiles that partition <b>2 / 7</b> on
/// <c>MaxOutlierRejections</c>, and NINA hands concurrent processes DIFFERENT profiles (it holds the
/// <c>.profile</c> open, and <c>TryLoad</c> skips a locked one for the next by <c>LastUsed</c>). One integer
/// therefore produced two discrete values of <c>J</c>, which were chased as a floating-point race for two
/// waves.</para>
/// </summary>
[TestFixture]
public class HarnessFitInputsTests {

    private static AutoFocusOptions OptionsOver(params (string Key, string Value)[] pairs) {
        var bag = new Dictionary<string, string>();
        foreach (var (k, v) in pairs) {
            bag[k] = v;
        }
        var profileService = Substitute.For<IProfileService>();
        return new AutoFocusOptions(profileService, new HarnessSettingsStore.FileOptionsAccessor(bag));
    }

    // ---- The fit reads the FILE, not the profile ------------------------------------------------------------

    [Test]
    public void FitInputs_ComeFromTheHarnessFile() {
        var options = OptionsOver(
            ("MaxOutlierRejections", "0"),
            ("OutlierRejectionConfidence", "0.99"),
            ("WeightedHyperbolicFitEnabled", "False"),
            ("HyperbolicFitModel", "TiltedHyperbola"));

        var inputs = HarnessFitInputs.From(options);
        Assert.Multiple(() => {
            Assert.That(inputs.MaxOutlierRejections, Is.EqualTo(0));
            Assert.That(inputs.RejectionConfidence, Is.EqualTo(0.99).Within(1e-12));
            Assert.That(inputs.UseWeights, Is.False);
            Assert.That(inputs.PreferredModel, Is.EqualTo(HyperbolicFitModel.TiltedHyperbola));
        });
    }

    [Test]
    public void FitInputs_FallBackToTheDOCUMENTEDCodeDefaults_WhenTheFileDoesNotCarryThem() {
        // THE BACK-COMPATIBILITY CLAUSE, and it is load-bearing. `pinned_settings.json` -- byte-identical across
        // waves 5-10, md5 df7c7cd1... -- carries NONE of these four keys, so every historical arm run against the
        // fixed binary resolves them here. If these four numbers ever change, every pre-wave-11 pinned file
        // silently starts describing a different fit, and this test is what says so.
        //
        // WAVE 14 MOVED ONE OF THEM: `MaxOutlierRejections` 1 -> 0. This test is the announcement, not a
        // casualty. The consequence it exists to state: re-running a wave 5-10 arm against `pinned_settings.json`
        // on a post-wave-14 binary now resolves the budget to 0 rather than 1. That is a COORDINATE-SYSTEM MOVE
        // for those files -- and it moves them TOWARD what those waves actually measured, because waves 5-10
        // resolved the fit inputs off the ACTIVE PROFILE (astrodet, which stores 0) rather than off the file,
        // which is the whole of F57/F58. Wave 11 onward pins the key EXPLICITLY in `pinned_settings_w11.json`,
        // so the gate's eight values are untouched -- RULE G14 is what proves that rather than assumes it.
        var inputs = HarnessFitInputs.From(OptionsOver());
        Assert.Multiple(() => {
            Assert.That(inputs.MaxOutlierRejections, Is.EqualTo(0));
            Assert.That(inputs.RejectionConfidence, Is.EqualTo(0.95).Within(1e-12));
            Assert.That(inputs.UseWeights, Is.True);
            Assert.That(inputs.PreferredModel, Is.EqualTo(HyperbolicFitModel.Hybrid));
        });
    }

    // ---- Values, not a hash ---------------------------------------------------------------------------------

    [Test]
    public void ToString_RendersTheVALUES_SoAReaderSeesWHICHOneMoved() {
        var inputs = HarnessFitInputs.From(OptionsOver(
            ("MaxOutlierRejections", "1"), ("OutlierRejectionConfidence", "0.99")));

        Assert.That(inputs.ToString(), Is.EqualTo(
            "MaxOutlierRejections=1;OutlierRejectionConfidence=0.99;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid"));
    }

    [Test]
    public void ToString_IsStable_ForIdenticalInputs() {
        var a = HarnessFitInputs.From(OptionsOver(("MaxOutlierRejections", "1"))).ToString();
        var b = HarnessFitInputs.From(OptionsOver(("MaxOutlierRejections", "1"))).ToString();
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void ToString_ChangesWhenANYONEInputChanges() {
        // Discriminating on all four axes: a provenance field that moved on only some of them would report
        // "unchanged" for exactly the kind of drift it exists to catch.
        //
        // The budget's perturbation is DERIVED from the code default rather than written as a literal. It used
        // to be the literal "0", which discriminated only because the default happened to be 1; wave 14 moved
        // the default to 0 and the perturbation silently became the baseline. It failed loudly here, but the
        // same coincidence in a test asserting equality would have passed and proved nothing -- so the fix is
        // to make the perturbation incapable of coinciding, not to write down the next literal that works.
        var baseline = HarnessFitInputs.From(OptionsOver()).ToString();
        var otherBudget = (HarnessFitInputs.From(OptionsOver()).MaxOutlierRejections + 1).ToString(CultureInfo.InvariantCulture);
        Assert.Multiple(() => {
            Assert.That(HarnessFitInputs.From(OptionsOver(("MaxOutlierRejections", otherBudget))).ToString(),
                Is.Not.EqualTo(baseline), "MaxOutlierRejections");
            Assert.That(HarnessFitInputs.From(OptionsOver(("OutlierRejectionConfidence", "0.99"))).ToString(),
                Is.Not.EqualTo(baseline), "OutlierRejectionConfidence");
            Assert.That(HarnessFitInputs.From(OptionsOver(("WeightedHyperbolicFitEnabled", "False"))).ToString(),
                Is.Not.EqualTo(baseline), "WeightedHyperbolicFitEnabled");
            Assert.That(HarnessFitInputs.From(OptionsOver(("HyperbolicFitModel", "TiltedHyperbola"))).ToString(),
                Is.Not.EqualTo(baseline), "HyperbolicFitModel");
        });
    }

    [Test]
    public void ToString_IsCultureInvariant() {
        // The field is diffed across machines and pasted into results documents; a comma decimal separator would
        // also collide with nothing here but would silently break a `;`-and-`=` split downstream.
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.That(HarnessFitInputs.From(OptionsOver(("OutlierRejectionConfidence", "0.95"))).ToString(),
                Does.Contain("OutlierRejectionConfidence=0.95"));
        } finally {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    // ---- The export carries them, densely ---------------------------------------------------------------------

    [Test]
    public void CopyOptionSurface_CarriesTheFitInputs_EvenWhenTheyEqualTheCodeDefault() {
        // DENSITY is the point, not merely presence. `MaxOutlierRejections = 0` IS the code default (wave 14
        // moved it from 1), so a copy that skipped unchanged values would leave the key ABSENT and the file
        // would inherit whatever the default is AT LOAD TIME -- behaviour-preserving today and silently wrong
        // the day a default moves. That is precisely the drift a pinned settings file exists to prevent, and
        // wave 14 is the day a default moved: this test and its partner below carry the CODE DEFAULT and a
        // NON-DEFAULT respectively, so they had to swap values rather than merely be re-baselined.
        var destination = new Dictionary<string, string>();
        HarnessSettingsStore.CopyOptionSurface(
            OptionsOver(("MaxOutlierRejections", "0"), ("OutlierRejectionConfidence", "0.95")),
            new AutoFocusOptions(Substitute.For<IProfileService>(),
                new HarnessSettingsStore.FileOptionsAccessor(destination)));

        Assert.Multiple(() => {
            Assert.That(destination, Does.ContainKey("MaxOutlierRejections"));
            Assert.That(destination["MaxOutlierRejections"], Is.EqualTo("0"));
            Assert.That(destination, Does.ContainKey("OutlierRejectionConfidence"));
            Assert.That(destination, Does.ContainKey("WeightedHyperbolicFitEnabled"));
            Assert.That(destination, Does.ContainKey("HyperbolicFitModel"));
        });
    }

    [Test]
    public void CopyOptionSurface_RoundTripsANonDefaultBudget() {
        // 1 is the NON-default budget from wave 14 onward -- it was the shipped code default through wave 13, and
        // it is the value at which `toml999`'s BaselineJ reads 0.997840 (wave 9's number) against 0.983477 at 0
        // (wave 11's). That 0.0144 gap is the whole of F57, and wave 13 closed it by changing this one integer in
        // one file. If a non-default budget does not survive the export, --settings still does not pin the arm.
        var destination = new Dictionary<string, string>();
        HarnessSettingsStore.CopyOptionSurface(
            OptionsOver(("MaxOutlierRejections", "1")),
            new AutoFocusOptions(Substitute.For<IProfileService>(),
                new HarnessSettingsStore.FileOptionsAccessor(destination)));

        Assert.That(HarnessFitInputs.From(new AutoFocusOptions(Substitute.For<IProfileService>(),
            new HarnessSettingsStore.FileOptionsAccessor(destination))).MaxOutlierRejections, Is.EqualTo(1));
    }

    // ---- F59: a VALIDATING setter must not be dropped from the export -------------------------------------------

    [Test]
    public void CopyOptionSurface_CarriesAKnobWhoseSetterTHROWSOnTheObviousPoison() {
        // F59, and it cost five knobs for six waves. `MaxDistortion` validates to [0, 1] and THROWS outside it,
        // so the old one-shot `current + 1` poison (0.65 -> 1.65) raised ArgumentException, a single enclosing
        // catch swallowed it, and the property was skipped ENTIRELY -- neither poison nor value written.
        // `pinned_settings.json`, the file waves 5-11 called "the pinned detector", is missing MaxDistortion,
        // StarCenterTolerance, SaturationThreshold, HotpixelThreshold and Sensitivity for exactly this reason.
        //
        // Confirmed discriminating by neutralizing: restore the single-candidate poison and this test fails
        // while the rest of the fixture passes.
        // UseAdvanced=True deliberately: in Simple mode `DerivePresetSettings()` overwrites ~16 advanced knobs
        // from the Simple_* presets (F42(3)), so the source would read back the PRESET's MaxDistortion rather
        // than the file's and this test would be measuring that instead of the export.
        var source = new Dictionary<string, string> {
            ["UseAdvanced"] = "True", ["MaxDistortion"] = "0.65", ["StarCenterTolerance"] = "0.325",
        };
        var destination = new Dictionary<string, string>();
        var profileService = Substitute.For<IProfileService>();
        var from = new StarDetectionOptions(profileService, new HarnessSettingsStore.FileOptionsAccessor(source));

        HarnessSettingsStore.CopyOptionSurface(
            from, new StarDetectionOptions(profileService, new HarnessSettingsStore.FileOptionsAccessor(destination)));

        Assert.Multiple(() => {
            Assert.That(destination, Does.ContainKey("MaxDistortion"), "bounded to [0,1]; current+1 throws");
            Assert.That(double.Parse(destination["MaxDistortion"], System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(from.MaxDistortion).Within(1e-12));
            Assert.That(destination, Does.ContainKey("StarCenterTolerance"));
            Assert.That(double.Parse(destination["StarCenterTolerance"], System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(from.StarCenterTolerance).Within(1e-12));
        });
    }

    // ---- Simple mode does not reach them -----------------------------------------------------------------------

    [Test]
    public void SimpleModePresetOverrides_DoesNotTouchTheFitInputs() {
        // F42(3): `UseAdvanced = False` makes DerivePresetSettings() overwrite ~16 advanced DETECTOR knobs from
        // the Simple_* presets, so editing them in a pinned file does nothing, silently. Asserted by MEASUREMENT
        // rather than by a comment claiming the fit inputs are out of scope -- the whole point of that entry is
        // that the list cannot be trusted to a human's reading of the class.
        var file = new Dictionary<string, string> {
            ["UseAdvanced"] = "False",
            ["Simple_NoiseLevel"] = "Typical",
            ["Simple_PixelScale"] = "Typical",
            ["Simple_FocusRange"] = "Typical",
            ["MaxOutlierRejections"] = "0",
            ["OutlierRejectionConfidence"] = "0.99",
            ["WeightedHyperbolicFitEnabled"] = "False",
            ["HyperbolicFitModel"] = "Hyperbolic",
        };
        var overridden = HarnessSettingsStore.SimpleModePresetOverrides(file, Substitute.For<IProfileService>());

        Assert.Multiple(() => {
            Assert.That(overridden, Does.Not.Contain("MaxOutlierRejections"));
            Assert.That(overridden, Does.Not.Contain("OutlierRejectionConfidence"));
            Assert.That(overridden, Does.Not.Contain("WeightedHyperbolicFitEnabled"));
            Assert.That(overridden, Does.Not.Contain("HyperbolicFitModel"));
        });
    }
}
