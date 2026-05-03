using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class OrdinaryKrigingInterpolatorTests {

    [Test]
    public void Interpolate_ReturnsConstantField() {
        var interpolator = OrdinaryKrigingInterpolator.Create(new[] {
            new KrigingSample(0, 0, 2.5),
            new KrigingSample(1, 0, 2.5),
            new KrigingSample(0, 1, 2.5),
            new KrigingSample(1, 1, 2.5)
        });

        Assert.That(interpolator.Interpolate(0.37, 0.73), Is.EqualTo(2.5).Within(1e-12));
    }

    [Test]
    public void Interpolate_PassesThroughSupportPoints() {
        var interpolator = OrdinaryKrigingInterpolator.Create(new[] {
            new KrigingSample(0, 0, 1.0),
            new KrigingSample(1, 0, 2.0),
            new KrigingSample(0, 1, 3.0),
            new KrigingSample(1, 1, 4.0)
        });

        Assert.Multiple(() => {
            Assert.That(interpolator.Interpolate(0, 0), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(interpolator.Interpolate(1, 0), Is.EqualTo(2.0).Within(1e-12));
            Assert.That(interpolator.Interpolate(0, 1), Is.EqualTo(3.0).Within(1e-12));
            Assert.That(interpolator.Interpolate(1, 1), Is.EqualTo(4.0).Within(1e-12));
        });
    }

    [Test]
    public void Interpolate_UsesSymmetricWeightsAtSquareCenter() {
        var interpolator = OrdinaryKrigingInterpolator.Create(new[] {
            new KrigingSample(0, 0, 1.0),
            new KrigingSample(1, 0, 3.0),
            new KrigingSample(0, 1, 5.0),
            new KrigingSample(1, 1, 7.0)
        });

        Assert.That(interpolator.Interpolate(0.5, 0.5), Is.EqualTo(4.0).Within(1e-12));
    }

    [Test]
    public void Create_CoalescesDuplicateSupportPoints() {
        var interpolator = OrdinaryKrigingInterpolator.Create(new[] {
            new KrigingSample(0, 0, 1.0),
            new KrigingSample(0, 0, 3.0),
            new KrigingSample(1, 0, 5.0),
            new KrigingSample(0, 1, 7.0)
        });

        Assert.That(interpolator.Interpolate(0, 0), Is.EqualTo(2.0).Within(1e-12));
    }
}
