using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.ValidationRules;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.ValidationRules;

[TestFixture]
public class PositiveIntegerOrInfiniteRuleTests {
    private PositiveIntegerOrInfiniteRule rule;

    [SetUp]
    public void SetUp() {
        rule = new PositiveIntegerOrInfiniteRule();
    }

    [TestCase("1")]
    [TestCase("2")]
    [TestCase("100")]
    [TestCase("999999")]
    public void Validate_PositiveInteger_ReturnsValid(string input) {
        var result = rule.Validate(input, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.True, $"Expected '{input}' to be valid");
    }

    [Test]
    public void Validate_UnlimitedLiteral_ReturnsValid() {
        var result = rule.Validate("unlimited", CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_NegativeOneSentinel_ReturnsValid() {
        // CLAUDE.md: "-1 means auto/infinite"
        var result = rule.Validate("-1", CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.True);
    }

    [TestCase("0")]
    [TestCase("-2")]
    [TestCase("-100")]
    public void Validate_NonPositiveOtherThanNegativeOne_ReturnsInvalid(string input) {
        var result = rule.Validate(input, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.False, $"Expected '{input}' to be invalid for a 'positive integer or infinite' rule");
    }

    [TestCase("abc")]
    [TestCase("1.5")]
    [TestCase("")]
    [TestCase("infinite")]
    public void Validate_NonNumeric_ReturnsInvalid(string input) {
        var result = rule.Validate(input, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void Validate_Null_ReturnsInvalid() {
        var result = rule.Validate(null, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.False);
    }
}
