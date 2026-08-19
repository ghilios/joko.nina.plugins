using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

[TestFixture]
public class ScrewLabelStoreTests {

    private static Dictionary<string, string[]> Map(params (string SchemeId, string[] Labels)[] entries) {
        var result = new Dictionary<string, string[]>();
        foreach (var (schemeId, labels) in entries) {
            result[schemeId] = labels;
        }
        return result;
    }

    [Test]
    public void RoundTrip_PreservesEveryScheme() {
        var json = ScrewLabelStore.Serialize(Map(
            ("AsgEat", new[] { "M1", "", "Bob", "" }),
            ("Generic", new[] { "Top Left", "", "", "" })));

        var parsed = ScrewLabelStore.Parse(json);

        Assert.Multiple(() => {
            Assert.That(parsed["AsgEat"], Is.EqualTo(new[] { "M1", "", "Bob", "" }));
            Assert.That(parsed["Generic"], Is.EqualTo(new[] { "Top Left", "", "", "" }));
        });
    }

    [Test]
    public void Serialize_OmitsSchemesWithNothingLabeled() {
        // A user who never touches the feature must persist "", like every other unset string option.
        Assert.Multiple(() => {
            Assert.That(ScrewLabelStore.Serialize(Map(("Generic", new[] { "", "", "", "" }))), Is.Empty);
            Assert.That(ScrewLabelStore.Serialize(Map()), Is.Empty);
            Assert.That(ScrewLabelStore.Serialize(null), Is.Empty);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json at all")]
    [TestCase("{\"AsgEat\": ")]
    [TestCase("[1, 2, 3]")]
    [TestCase("{\"AsgEat\": 7}")]
    public void Parse_DegradesToEmptyRatherThanThrowing(string json) {
        // Labels are cosmetic: corrupt input must fall back to default names, never throw into a binding.
        Assert.That(ScrewLabelStore.Parse(json), Is.Empty);
    }

    [Test]
    public void Parse_PadsAndTruncatesToTheFixedSlotCount() {
        var parsed = ScrewLabelStore.Parse(
            "{\"Short\":[\"a\"],\"Long\":[\"a\",\"b\",\"c\",\"d\",\"e\"],\"Nulls\":[null,\"b\",null,null]}");

        Assert.Multiple(() => {
            Assert.That(parsed["Short"], Is.EqualTo(new[] { "a", "", "", "" }));
            Assert.That(parsed["Long"], Is.EqualTo(new[] { "a", "b", "c", "d" }));
            Assert.That(parsed["Nulls"], Is.EqualTo(new[] { "", "b", "", "" }));
        });
    }

    [TestCase("  M1  ", "M1")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase(null, "")]
    [TestCase("Bottom Right Screw", "Bottom Right")]     // capped at MaxLabelLength = 12
    public void Normalize_TrimsBlanksToUnsetAndCapsLength(string input, string expected) {
        Assert.That(ScrewLabelStore.Normalize(input), Is.EqualTo(expected));
    }

    [Test]
    public void Normalize_IsAppliedOnParseToo() {
        // A hand-edited profile can carry an over-long label that never passed through the TextBox.
        var parsed = ScrewLabelStore.Parse("{\"Generic\":[\"  Bottom Right Screw  \",\"\",\"\",\"\"]}");
        Assert.That(parsed["Generic"][0], Is.EqualTo("Bottom Right"));
    }
}
