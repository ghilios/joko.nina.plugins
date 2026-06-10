using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    [TestFixture]
    public class HfrGroundTruthTests {

        [Test]
        public void Disk_ClosedFormMatchesNumericalIntegration() {
            const double a = 12.0;
            double numerical = HfrGroundTruth.Numerical(r => r <= a ? 1.0 : 0.0, apertureRadius: 40.0);
            Assert.That(HfrGroundTruth.Disk(a), Is.EqualTo(numerical).Within(0.01));
            Assert.That(HfrGroundTruth.Disk(a), Is.EqualTo(8.0).Within(1e-9));
        }

        [Test]
        public void Annulus_ClosedFormMatchesNumericalIntegration() {
            const double b = 6.0, a = 14.0;
            double numerical = HfrGroundTruth.Numerical(r => (r >= b && r <= a) ? 1.0 : 0.0, apertureRadius: 40.0);
            Assert.That(HfrGroundTruth.Annulus(b, a), Is.EqualTo(numerical).Within(0.01));
        }

        [Test]
        public void Gaussian_ClosedFormMatchesNumericalIntegration() {
            const double sigma = 4.0;
            double numerical = HfrGroundTruth.Numerical(r => Math.Exp(-r * r / (2 * sigma * sigma)), apertureRadius: 60.0);
            Assert.That(HfrGroundTruth.Gaussian(sigma), Is.EqualTo(numerical).Within(0.01));
        }
    }
}
