using NINA.Joko.Plugins.HocusFocus.Inspection;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection;

[TestFixture]
public class SensorParaboloidDataPointTests {

    [Test]
    public void Constructor_StoresAllValues() {
        var p = new SensorParaboloidDataPoint(x: 10, y: 20, focuserPosition: 1234, rSquared: 0.95);
        Assert.Multiple(() => {
            Assert.That(p.X, Is.EqualTo(10));
            Assert.That(p.Y, Is.EqualTo(20));
            Assert.That(p.FocuserPosition, Is.EqualTo(1234));
            Assert.That(p.RSquared, Is.EqualTo(0.95));
        });
    }

    [Test]
    public void ToInput_ReturnsXY() {
        var p = new SensorParaboloidDataPoint(x: 10, y: 20, focuserPosition: 1234, rSquared: 0.95);
        Assert.That(p.ToInput(), Is.EqualTo(new[] { 10.0, 20.0 }));
    }

    [Test]
    public void ToOutput_ReturnsFocuserPosition() {
        var p = new SensorParaboloidDataPoint(x: 10, y: 20, focuserPosition: 1234, rSquared: 0.95);
        Assert.That(p.ToOutput(), Is.EqualTo(1234));
    }

    [Test]
    public void ToString_IncludesAllFields() {
        var p = new SensorParaboloidDataPoint(x: 10, y: 20, focuserPosition: 1234, rSquared: 0.95);
        var s = p.ToString();
        Assert.Multiple(() => {
            Assert.That(s, Does.Contain("X="));
            Assert.That(s, Does.Contain("Y="));
            Assert.That(s, Does.Contain("FocuserPosition="));
            Assert.That(s, Does.Contain("RSquared="));
        });
    }
}

[TestFixture]
public class SensorParaboloidModelExtraTests {

    [Test]
    public void ParameterizedConstructor_StoresCanonicalValues() {
        var m = new SensorParaboloidModel(x0: 1, y0: 2, z0: 3, gx: 0.1, gy: 0.2, k: 0.05);
        Assert.Multiple(() => {
            Assert.That(m.X0, Is.EqualTo(1));
            Assert.That(m.Y0, Is.EqualTo(2));
            Assert.That(m.Z0, Is.EqualTo(3));
            Assert.That(m.Gx, Is.EqualTo(0.1));
            Assert.That(m.Gy, Is.EqualTo(0.2));
            Assert.That(m.K, Is.EqualTo(0.05));
        });
    }

    [Test]
    public void FromTiltCurvature_RoundTripsThroughDerivedDisplayProperties() {
        var m = SensorParaboloidModel.FromTiltCurvature(x0: 1, y0: 2, z0: 3, theta: 0.1, phi: 0.2, c: 0.05);
        Assert.Multiple(() => {
            Assert.That(m.Theta, Is.EqualTo(0.1).Within(1e-9));
            Assert.That(m.Phi, Is.EqualTo(0.2).Within(1e-9));
            Assert.That(m.C, Is.EqualTo(0.05).Within(1e-9));
        });
    }

    [Test]
    public void DefaultConstructor_HasAllZeros() {
        var m = new SensorParaboloidModel();
        Assert.Multiple(() => {
            Assert.That(m.X0, Is.EqualTo(0));
            Assert.That(m.Y0, Is.EqualTo(0));
            Assert.That(m.Z0, Is.EqualTo(0));
            Assert.That(m.Gx, Is.EqualTo(0));
            Assert.That(m.Gy, Is.EqualTo(0));
            Assert.That(m.K, Is.EqualTo(0));
        });
    }

    [Test]
    public void FromArray_CopiesParameters() {
        var m = new SensorParaboloidModel();
        m.FromArray(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 });
        Assert.Multiple(() => {
            Assert.That(m.X0, Is.EqualTo(1));
            Assert.That(m.Y0, Is.EqualTo(2));
            Assert.That(m.Z0, Is.EqualTo(3));
            Assert.That(m.Gx, Is.EqualTo(4));
            Assert.That(m.Gy, Is.EqualTo(5));
            Assert.That(m.K, Is.EqualTo(6));
        });
    }

    [TestCase(null)]
    [TestCase(new double[] { 1, 2, 3 })]
    [TestCase(new double[] { 1, 2, 3, 4, 5, 6, 7 })]
    public void FromArray_RejectsWrongShape(double[] input) {
        var m = new SensorParaboloidModel();
        Assert.Throws<ArgumentException>(() => m.FromArray(input));
    }

    [Test]
    public void ToArray_ReturnsAllParametersInOrder() {
        var m = new SensorParaboloidModel(x0: 1, y0: 2, z0: 3, gx: 0.4, gy: 0.5, k: 0.6);
        Assert.That(m.ToArray(), Is.EqualTo(new[] { 1.0, 2.0, 3.0, 0.4, 0.5, 0.6 }));
    }

    [Test]
    public void ToString_ContainsAllParameterNames() {
        var s = new SensorParaboloidModel(x0: 1, y0: 2, z0: 3, gx: 0.1, gy: 0.2, k: 0.05).ToString();
        Assert.Multiple(() => {
            Assert.That(s, Does.Contain("X0="));
            Assert.That(s, Does.Contain("Y0="));
            Assert.That(s, Does.Contain("Z0="));
            Assert.That(s, Does.Contain("Gx="));
            Assert.That(s, Does.Contain("Gy="));
            Assert.That(s, Does.Contain("K="));
        });
    }

    [Test]
    public void TiltAt_WithZeroGradients_IsZero() {
        var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: 0, gy: 0, k: 0);
        Assert.That(m.TiltAt(100, 200), Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void CurvatureAt_WithZeroCurvature_IsZero() {
        var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: 0, gy: 0, k: 0);
        Assert.That(m.CurvatureAt(100, 200), Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void CurvatureAt_PositiveK_IsPositive() {
        var m = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 0, theta: 0, phi: 0, c: 0.1);
        // K = +0.01, x²+y² = 100, so result = 1
        Assert.That(m.CurvatureAt(10, 0), Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void CurvatureAt_NegativeK_IsNegative() {
        var m = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 0, theta: 0, phi: 0, c: -0.1);
        Assert.That(m.CurvatureAt(10, 0), Is.EqualTo(-1.0).Within(1e-9));
    }

    [Test]
    public void Volume_ScalesWithSensorArea() {
        var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 5, gx: 0, gy: 0, k: 0);
        // Z0 only contribution: Z0 * w * h = 5 * 1000 * 2000 = 10_000_000
        Assert.That(m.Volume(1000, 2000), Is.EqualTo(10_000_000.0).Within(1e-3));
    }
}

[TestFixture]
public class SensorModelAnalysisResultTests {

    [Test]
    public void Setters_RoundTrip() {
        var r = new SensorModelAnalysisResult {
            Name = "Tilt",
            Value = "0.05°",
            Acceptable = true,
            Details = "OK"
        };
        Assert.Multiple(() => {
            Assert.That(r.Name, Is.EqualTo("Tilt"));
            Assert.That(r.Value, Is.EqualTo("0.05°"));
            Assert.That(r.Acceptable, Is.True);
            Assert.That(r.Details, Is.EqualTo("OK"));
        });
    }
}
