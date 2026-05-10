using System;
using System.Globalization;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Converters;

[TestFixture]
public class InverseBooleanConverterTests {
    private InverseBooleanConverter converter;

    [SetUp]
    public void SetUp() => converter = new InverseBooleanConverter();

    [Test]
    public void Convert_True_ReturnsFalse() =>
        Assert.That(converter.Convert(true, typeof(bool), null, CultureInfo.InvariantCulture), Is.EqualTo(false));

    [Test]
    public void Convert_False_ReturnsTrue() =>
        Assert.That(converter.Convert(false, typeof(bool), null, CultureInfo.InvariantCulture), Is.EqualTo(true));

    [Test]
    public void Convert_NonBool_Throws() =>
        Assert.That(() => converter.Convert("not bool", typeof(bool), null, CultureInfo.InvariantCulture),
            Throws.ArgumentException);
}

[TestFixture]
public class IsNegativeToBooleanConverterTests {
    private IsNegativeToBooleanConverter converter;

    [SetUp]
    public void SetUp() => converter = new IsNegativeToBooleanConverter();

    [TestCase(-1.0)]
    [TestCase(-1e-12)]
    public void Convert_NegativeDouble_ReturnsTrue(double input) =>
        Assert.That(converter.Convert(input, typeof(bool), null, CultureInfo.InvariantCulture), Is.EqualTo(true));

    [TestCase(0.0)]
    [TestCase(1.0)]
    public void Convert_NonNegativeDouble_ReturnsFalse(double input) =>
        Assert.That(converter.Convert(input, typeof(bool), null, CultureInfo.InvariantCulture), Is.EqualTo(false));

    [Test]
    public void Convert_NegativeInt_ReturnsTrue() =>
        Assert.That(converter.Convert(-3, typeof(bool), null, CultureInfo.InvariantCulture), Is.EqualTo(true));

    [Test]
    public void Convert_NonNumber_Throws() =>
        Assert.That(() => converter.Convert("abc", typeof(bool), null, CultureInfo.InvariantCulture),
            Throws.ArgumentException);
}

[TestFixture]
public class IsNotNegativeToBooleanConverterTests {
    private IsNotNegativeToBooleanConverter converter;

    [SetUp]
    public void SetUp() => converter = new IsNotNegativeToBooleanConverter();

    [TestCase(-1.0, false)]
    [TestCase(0.0, true)]
    [TestCase(1.0, true)]
    public void Convert_Double_ReturnsExpected(double input, bool expected) =>
        Assert.That(converter.Convert(input, typeof(bool), null, CultureInfo.InvariantCulture), Is.EqualTo(expected));
}

[TestFixture]
public class PositiveToBooleanConverterTests {
    private PositiveToBooleanConverter converter;

    [SetUp]
    public void SetUp() => converter = new PositiveToBooleanConverter();

    [TestCase(1, true)]
    [TestCase(100, true)]
    [TestCase(0, false)]
    [TestCase(-1, false)]
    public void Convert_Int_ReturnsExpected(int input, bool expected) =>
        Assert.That(converter.Convert(input, typeof(bool), null, CultureInfo.InvariantCulture), Is.EqualTo(expected));

    [Test]
    public void Convert_NonInt_ReturnsFalse() =>
        // Per implementation, all non-int inputs fall through to `false`.
        Assert.That(converter.Convert(1.5, typeof(bool), null, CultureInfo.InvariantCulture), Is.EqualTo(false));
}
