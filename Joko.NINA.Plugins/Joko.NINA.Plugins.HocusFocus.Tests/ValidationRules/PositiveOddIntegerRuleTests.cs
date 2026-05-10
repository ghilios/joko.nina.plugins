using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.ValidationRules;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.ValidationRules;

[TestFixture]
public class PositiveOddIntegerRuleTests {
    private PositiveOddIntegerRule rule;

    [SetUp]
    public void SetUp() {
        rule = new PositiveOddIntegerRule();
    }

    [TestCase("1")]
    [TestCase("3")]
    [TestCase("5")]
    [TestCase("99")]
    [TestCase("12345")]
    public void Validate_PositiveOddInteger_ReturnsValid(string input) {
        var result = rule.Validate(input, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.True, $"Expected '{input}' to be valid");
    }

    [TestCase("2")]
    [TestCase("4")]
    [TestCase("100")]
    public void Validate_PositiveEvenInteger_ReturnsInvalid(string input) {
        var result = rule.Validate(input, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.False);
    }

    [TestCase("0")]
    public void Validate_Zero_ReturnsInvalid(string input) {
        var result = rule.Validate(input, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.False);
    }

    [TestCase("-1")]
    [TestCase("-3")]
    [TestCase("-99")]
    public void Validate_NegativeInteger_ReturnsInvalid(string input) {
        var result = rule.Validate(input, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.False);
    }

    [TestCase("abc")]
    [TestCase("")]
    [TestCase("1.5")]
    [TestCase("3.0")]
    public void Validate_NonInteger_ReturnsInvalid(string input) {
        var result = rule.Validate(input, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.False, $"Expected '{input}' to be invalid");
    }

    [Test]
    public void Validate_Null_ReturnsInvalid() {
        var result = rule.Validate(null, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void Validate_BoxedInteger_ReturnsValidWhenPositiveOdd() {
        // ValidationRule receives object; WPF often passes the bound primitive directly.
        var result = rule.Validate(7, CultureInfo.InvariantCulture);
        Assert.That(result.IsValid, Is.True);
    }
}
