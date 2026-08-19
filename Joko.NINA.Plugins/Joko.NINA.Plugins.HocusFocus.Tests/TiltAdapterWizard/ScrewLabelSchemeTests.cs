using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

[TestFixture]
public class ScrewLabelSchemeTests {

    [TestCase(1, "Screw 1")]
    [TestCase(2, "Screw 2")]
    [TestCase(3, "Screw 3")]
    [TestCase(4, "Screw 4")]
    public void Generic_ReproducesTheWordingUsedBeforeLabelsExisted(int wizardScrew, string expected) {
        Assert.That(ScrewLabelScheme.Generic.DefaultLabel(wizardScrew), Is.EqualTo(expected));
    }

    // The whole point of the feature: wizard screws 3 and 4 are the EAT's motors 4 and 3.
    [TestCase(1, "M1")]
    [TestCase(2, "M2")]
    [TestCase(3, "M4")]
    [TestCase(4, "M3")]
    public void AsgEat_NamesTheMotorAndSwapsScrews3And4(int wizardScrew, string expected) {
        Assert.That(ScrewLabelScheme.AsgEat.DefaultLabel(wizardScrew), Is.EqualTo(expected));
    }

    // Guards against someone "simplifying" AsgEat into a literal {M1,M2,M4,M3} array, which would then
    // silently disagree with TiltAdapterCorner if the corner table were ever corrected.
    [Test]
    public void AsgEat_IsDerivedFromTheCornerTableRatherThanALiteral() {
        for (int wizardScrew = 1; wizardScrew <= 4; ++wizardScrew) {
            var corner = TiltAdapterCorner.ForWizardScrew(wizardScrew);
            Assert.That(
                ScrewLabelScheme.AsgEat.DefaultLabel(wizardScrew),
                Is.EqualTo($"M{corner.DeviceMotorNumber}"),
                $"wizard screw {wizardScrew} ({corner.Label})");
        }
    }

    [TestCase(0)]
    [TestCase(5)]
    public void DefaultLabel_RejectsOutOfRangeScrewNumbers(int wizardScrew) {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScrewLabelScheme.Generic.DefaultLabel(wizardScrew));
    }

    [Test]
    public void ById_RoundTripsEverySchemeAndFallsBackToGeneric() {
        Assert.Multiple(() => {
            foreach (var scheme in ScrewLabelScheme.All) {
                Assert.That(ScrewLabelScheme.ById(scheme.Id), Is.SameAs(scheme), scheme.Id);
            }
            Assert.That(ScrewLabelScheme.ById("no-such-scheme"), Is.SameAs(ScrewLabelScheme.Generic));
            Assert.That(ScrewLabelScheme.ById(null), Is.SameAs(ScrewLabelScheme.Generic));
        });
    }

    [Test]
    public void SchemeIds_AreUniqueAndStable() {
        // Ids are persistence keys inside ScrewLabelsJson; changing one orphans a user's stored labels.
        Assert.Multiple(() => {
            Assert.That(ScrewLabelScheme.All.Select(s => s.Id), Is.Unique);
            Assert.That(ScrewLabelScheme.Generic.Id, Is.EqualTo("Generic"));
            Assert.That(ScrewLabelScheme.AsgEat.Id, Is.EqualTo("AsgEat"));
        });
    }

    [Test]
    public void EveryPresetDeclaresAScheme_AndOnlyTheEatsUseAsgEat() {
        Assert.Multiple(() => {
            foreach (var preset in TiltAdapterDevicePreset.All) {
                Assert.That(preset.ScrewLabels, Is.Not.Null, preset.Name);
                var expected = preset.Name.StartsWith("ASG Electronic EAT", StringComparison.Ordinal)
                    ? ScrewLabelScheme.AsgEat
                    : ScrewLabelScheme.Generic;
                Assert.That(preset.ScrewLabels, Is.SameAs(expected), preset.Name);
            }
            Assert.That(TiltAdapterDevicePreset.Manual.ScrewLabels, Is.SameAs(ScrewLabelScheme.Generic));
        });
    }

    // Both EAT presets share one scheme, so labels entered against the 90mm are the same labels the
    // ZWO 461 shows -- they are the same motors either way.
    [Test]
    public void BothEatPresets_ShareOneLabelSlot() {
        Assert.That(
            TiltAdapterDevicePreset.ByName("ASG Electronic EAT - 90mm").ScrewLabels.Id,
            Is.EqualTo(TiltAdapterDevicePreset.ByName("ASG Electronic EAT - ZWO 461").ScrewLabels.Id));
    }
}
