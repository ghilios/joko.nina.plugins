using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

[TestFixture]
public class TiltScrewLabelsTests {

    private const string Eat90 = "ASG Electronic EAT - 90mm";
    private const string EatZwo = "ASG Electronic EAT - ZWO 461";

    private static TiltAdapterOptions BuildOptions(string deviceName = null) {
        var options = new TiltAdapterOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
        if (deviceName != null) {
            options.DeviceName = deviceName;
        }
        return options;
    }

    [Test]
    public void ManualAdapter_ReadsExactlyAsItDidBeforeLabelsExisted() {
        var labels = TiltScrewLabels.For(BuildOptions());
        Assert.Multiple(() => {
            for (int screw = 1; screw <= 4; ++screw) {
                Assert.That(labels.Label(screw), Is.EqualTo($"Screw {screw}"));
            }
        });
    }

    [TestCase(1, "M1")]
    [TestCase(2, "M2")]
    [TestCase(3, "M4")]
    [TestCase(4, "M3")]
    public void SelectingTheEat_NamesTheMotorsWithNoUserInput(int wizardScrew, string expected) {
        var labels = TiltScrewLabels.For(BuildOptions(Eat90));
        Assert.That(labels.Label(wizardScrew), Is.EqualTo(expected));
    }

    [Test]
    public void AUserLabelWinsOverTheDefault() {
        var options = BuildOptions(Eat90);
        options.SetScrewLabelOverride(2, "Top Left");

        var labels = TiltScrewLabels.For(options);
        Assert.Multiple(() => {
            Assert.That(labels.Label(2), Is.EqualTo("Top Left"));
            Assert.That(labels.Label(1), Is.EqualTo("M1"), "unlabeled screws keep the device default");
        });
    }

    [Test]
    public void ClearingALabelRestoresTheDefault() {
        var options = BuildOptions(Eat90);
        options.SetScrewLabelOverride(3, "Bob");
        Assert.That(TiltScrewLabels.For(options).Label(3), Is.EqualTo("Bob"));

        options.SetScrewLabelOverride(3, "   ");
        Assert.Multiple(() => {
            Assert.That(TiltScrewLabels.For(options).Label(3), Is.EqualTo("M4"));
            Assert.That(options.ScrewLabelsJson, Is.Empty, "the last label going away should clear the blob");
        });
    }

    // The heart of the per-device store: each device family keeps its own names, and both come back.
    [Test]
    public void EachDeviceKeepsItsOwnNames() {
        var options = BuildOptions();
        options.SetScrewLabelOverride(1, "Front");        // against Manual / Generic
        options.DeviceName = Eat90;
        options.SetScrewLabelOverride(1, "Motor A");      // against the EAT

        var labels = TiltScrewLabels.For(options);
        Assert.Multiple(() => {
            Assert.That(labels.Label(1), Is.EqualTo("Motor A"));
            options.DeviceName = "Manual";
            Assert.That(labels.Label(1), Is.EqualTo("Front"), "the manual adapter's name survived the round trip");
            options.DeviceName = Eat90;
            Assert.That(labels.Label(1), Is.EqualTo("Motor A"), "and so did the EAT's");
        });
    }

    [Test]
    public void BothEatPresetsShareOneSetOfNames() {
        var options = BuildOptions(Eat90);
        options.SetScrewLabelOverride(4, "Rear");
        options.DeviceName = EatZwo;

        // Same motors, same engraving -- switching EAT models must not orphan the labels.
        Assert.That(TiltScrewLabels.For(options).Label(4), Is.EqualTo("Rear"));
    }

    [Test]
    public void LabelsSurviveAScrewCountDropTo3AndBack() {
        var options = BuildOptions();
        options.ScrewCount = 4;
        options.SetScrewLabelOverride(4, "Spare");

        options.ScrewCount = 3;   // screw 4 is parked, exactly like Screw4AngleDegrees
        options.ScrewCount = 4;

        Assert.That(TiltScrewLabels.For(options).Label(4), Is.EqualTo("Spare"));
    }

    [Test]
    public void LabelsPersistAcrossAnOptionsReload() {
        var store = new InMemoryPluginOptionsAccessor();
        var profile = Substitute.For<IProfileService>();
        var first = new TiltAdapterOptions(profile, store);
        first.DeviceName = Eat90;
        first.SetScrewLabelOverride(3, "Bob");

        var reloaded = new TiltAdapterOptions(profile, store);
        Assert.That(TiltScrewLabels.For(reloaded).Label(3), Is.EqualTo("Bob"));
    }

    [Test]
    public void CorruptStoredJson_FallsBackToDefaultNames() {
        var options = BuildOptions(Eat90);
        options.ScrewLabelsJson = "{ this is not json";

        Assert.Multiple(() => {
            Assert.That(TiltScrewLabels.For(options).Label(3), Is.EqualTo("M4"));
            Assert.That(options.GetScrewLabelOverride(3), Is.Empty);
        });
    }

    [TestCase(0)]
    [TestCase(5)]
    public void ScrewNumbersOutsideOneToFourAreRejected(int wizardScrew) {
        var options = BuildOptions();
        Assert.Multiple(() => {
            Assert.Throws<ArgumentOutOfRangeException>(() => options.GetScrewLabelOverride(wizardScrew));
            Assert.Throws<ArgumentOutOfRangeException>(() => options.SetScrewLabelOverride(wizardScrew, "x"));
        });
    }

    [Test]
    public void WithScrewNumber_AddsThePositionOnlyWhenTheNameHidesIt() {
        var eat = TiltScrewLabels.For(BuildOptions(Eat90));
        Assert.Multiple(() => {
            Assert.That(TiltScrewLabels.WithScrewNumber(eat, 3), Is.EqualTo("M4 (screw 3)"));
            // "Screw 3 (screw 3)" would be a stutter.
            Assert.That(TiltScrewLabels.WithScrewNumber(TiltScrewLabels.Default, 3), Is.EqualTo("Screw 3"));
        });
    }

    [Test]
    public void DescribeScrew_SpellsOutTheIdentityBehindALabel() {
        Assert.Multiple(() => {
            Assert.That(TiltScrewLabels.DescribeScrew(3, screwCount: 4), Is.EqualTo("Screw 3 · BL · Motor 4"));
            Assert.That(TiltScrewLabels.DescribeScrew(1, screwCount: 4), Is.EqualTo("Screw 1 · TR · Motor 1"));
            // A 3-screw adapter has no corners or motors to name.
            Assert.That(TiltScrewLabels.DescribeScrew(3, screwCount: 3), Is.EqualTo("Screw 3"));
        });
    }

    [Test]
    public void ForScheme_GivesDefaultNamesWithoutAnOptionsObject() {
        Assert.Multiple(() => {
            Assert.That(TiltScrewLabels.ForScheme(ScrewLabelScheme.AsgEat).Label(3), Is.EqualTo("M4"));
            Assert.That(TiltScrewLabels.Default.Label(3), Is.EqualTo("Screw 3"));
        });
    }

    [Test]
    public void For_RejectsNullOptions() {
        Assert.Throws<ArgumentNullException>(() => TiltScrewLabels.For(null));
    }
}
