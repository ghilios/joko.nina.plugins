using System;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class QeCurveTests {
    private static readonly double[] AnchorWavelengths = { 450, 475, 500, 530, 656, 672 };
    private static readonly double[] AnchorQe = { 0.82, 0.80, 0.78, 0.75, 0.50, 0.46 };

    [Test]
    public void EvaluateAt_ReturnsExactValueAtEachAnchor() {
        var curve = QeCurve.SonyBsiVisible;
        Assert.Multiple(() => {
            for (int i = 0; i < AnchorWavelengths.Length; i++) {
                Assert.That(curve.EvaluateAt(AnchorWavelengths[i]), Is.EqualTo(AnchorQe[i]).Within(1e-12),
                    $"anchor {AnchorWavelengths[i]} nm");
            }
        });
    }

    [Test]
    public void EvaluateAt_InterpolatesLinearlyBetweenAnchors() {
        var curve = QeCurve.SonyBsiVisible;
        Assert.Multiple(() => {
            // Filter-center wavelengths used by the design's exposure-parity table.
            Assert.That(curve.EvaluateAt(540.0), Is.EqualTo(0.7302).Within(1e-3), "L center");
            Assert.That(curve.EvaluateAt(465.0), Is.EqualTo(0.808).Within(1e-3), "B center");
            Assert.That(curve.EvaluateAt(635.0), Is.EqualTo(0.5417).Within(1e-3), "R center");
            Assert.That(curve.EvaluateAt(500.7), Is.EqualTo(0.7793).Within(1e-3), "OIII center");
            Assert.That(curve.EvaluateAt(656.3), Is.EqualTo(0.4993).Within(1e-3), "Ha center");
        });
    }

    [Test]
    public void EvaluateAt_ClampsOutsideAnchorRange() {
        var curve = QeCurve.SonyBsiVisible;
        Assert.Multiple(() => {
            Assert.That(curve.EvaluateAt(300.0), Is.EqualTo(0.82).Within(1e-12), "below range clamps to first");
            Assert.That(curve.EvaluateAt(449.0), Is.EqualTo(0.82).Within(1e-12), "just below range");
            Assert.That(curve.EvaluateAt(900.0), Is.EqualTo(0.46).Within(1e-12), "above range clamps to last");
            Assert.That(curve.EvaluateAt(672.4), Is.EqualTo(0.46).Within(1e-12), "just above range");
        });
    }

    [Test]
    public void EvaluateAt_IsMonotoneDecreasingAcrossVisibleAnchors() {
        var curve = QeCurve.SonyBsiVisible;
        var prev = double.PositiveInfinity;
        Assert.Multiple(() => {
            for (double wl = 450; wl <= 672; wl += 1.0) {
                var q = curve.EvaluateAt(wl);
                Assert.That(q, Is.LessThanOrEqualTo(prev + 1e-12), $"QE should not increase at {wl} nm");
                Assert.That(q, Is.GreaterThan(0.0));
                prev = q;
            }
        });
    }

    [Test]
    public void ExposedAnchors_AreReadOnlyAndNotCastableToMutableArray() {
        var curve = QeCurve.SonyBsiVisible;
        Assert.Multiple(() => {
            // A caller cannot cast the view back to double[] and mutate the shared static curve.
            Assert.That(curve.WavelengthsNm, Is.Not.InstanceOf<double[]>());
            Assert.That(curve.QeValues, Is.Not.InstanceOf<double[]>());
            // The shared static instance is unperturbed and still evaluates its anchors.
            Assert.That(curve.EvaluateAt(450.0), Is.EqualTo(0.82).Within(1e-12));
        });
    }

    [Test]
    public void Constructor_RejectsMismatchedOrUnsortedAnchors() {
        Assert.Multiple(() => {
            Assert.Throws<ArgumentException>(() => new QeCurve(new double[] { 400, 500 }, new double[] { 0.8 }));
            Assert.Throws<ArgumentException>(() => new QeCurve(new double[] { 500, 400 }, new double[] { 0.8, 0.7 }));
            Assert.Throws<ArgumentException>(() => new QeCurve(new double[] { 400 }, new double[] { 0.8 }));
        });
    }
}
