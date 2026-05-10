using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class PSFModelTests {

        [Test]
        public void Constructor_Round_StoresCircularEccentricity() {
            var model = new PSFModel(
                psfType: StarDetectorPSFFitType.Gaussian,
                offsetX: 0, offsetY: 0,
                peak: 1.0, background: 0.0,
                sigmaX: 1.0, sigmaY: 1.0,
                fwhmX: 2.355, fwhmY: 2.355,
                thetaRadians: 0.0,
                rSquared: 0.99,
                pixelScale: 1.0);

            Assert.That(model.Eccentricity, Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void Constructor_Elliptical_EccentricityIsBetweenZeroAndOne() {
            var model = new PSFModel(
                psfType: StarDetectorPSFFitType.Gaussian,
                offsetX: 0, offsetY: 0,
                peak: 1.0, background: 0.0,
                sigmaX: 2.0, sigmaY: 1.0,
                fwhmX: 4.0, fwhmY: 2.0,
                thetaRadians: 0.0,
                rSquared: 0.99,
                pixelScale: 1.0);

            // a = 4, b = 2 → ecc = sqrt(1 - 4/16) = sqrt(3)/2
            Assert.That(model.Eccentricity, Is.EqualTo(Math.Sqrt(3.0) / 2.0).Within(1e-12));
        }

        [Test]
        public void Constructor_AsymmetricInputs_EccentricityComputedFromMaxAndMin() {
            // Even when fwhmY > fwhmX, the formula should still give a positive eccentricity.
            var model = new PSFModel(
                psfType: StarDetectorPSFFitType.Moffat_40,
                offsetX: 0, offsetY: 0,
                peak: 1.0, background: 0.0,
                sigmaX: 1.0, sigmaY: 2.0,
                fwhmX: 2.0, fwhmY: 4.0,
                thetaRadians: 0.0,
                rSquared: 0.99,
                pixelScale: 1.0);

            Assert.That(model.Eccentricity, Is.EqualTo(Math.Sqrt(3.0) / 2.0).Within(1e-12));
        }

        [Test]
        public void Constructor_Sigma_IsGeometricMeanOfSigmaXAndSigmaY() {
            var model = new PSFModel(
                psfType: StarDetectorPSFFitType.Gaussian,
                offsetX: 0, offsetY: 0,
                peak: 1.0, background: 0.0,
                sigmaX: 4.0, sigmaY: 9.0,
                fwhmX: 1.0, fwhmY: 1.0,
                thetaRadians: 0.0,
                rSquared: 0.99,
                pixelScale: 1.0);

            Assert.That(model.Sigma, Is.EqualTo(6.0).Within(1e-12)); // sqrt(4*9) = 6
        }

        [Test]
        public void Constructor_FWHMPixels_IsGeometricMeanOfFWHMXAndFWHMY() {
            var model = new PSFModel(
                psfType: StarDetectorPSFFitType.Gaussian,
                offsetX: 0, offsetY: 0,
                peak: 1.0, background: 0.0,
                sigmaX: 1.0, sigmaY: 1.0,
                fwhmX: 4.0, fwhmY: 9.0,
                thetaRadians: 0.0,
                rSquared: 0.99,
                pixelScale: 1.0);

            Assert.That(model.FWHMPixels, Is.EqualTo(6.0).Within(1e-12));
        }

        [Test]
        public void Constructor_FWHMArcsecs_IsFWHMPixelsTimesPixelScale() {
            var model = new PSFModel(
                psfType: StarDetectorPSFFitType.Gaussian,
                offsetX: 0, offsetY: 0,
                peak: 1.0, background: 0.0,
                sigmaX: 1.0, sigmaY: 1.0,
                fwhmX: 4.0, fwhmY: 9.0,
                thetaRadians: 0.0,
                rSquared: 0.99,
                pixelScale: 0.5);

            Assert.That(model.FWHMArcsecs, Is.EqualTo(3.0).Within(1e-12)); // 6 * 0.5
        }

        [Test]
        public void GaussianSigmaToFWHM_FactorIsTwoSqrtTwoLn2() {
            var t = new GaussianPSFAlglibType(
                alglibAPI: null,
                inputs: new double[][] { new double[] { 0, 0 } },
                outputs: new double[] { 0 },
                centroidBrightness: 0.5,
                starDetectionBackground: 0.0,
                starBoundingBox: new OpenCvSharp.Rect(0, 0, 10, 10),
                pixelScale: 1.0);

            var fwhm = t.SigmaToFWHM(1.0);
            Assert.That(fwhm, Is.EqualTo(2.0 * Math.Sqrt(2.0 * Math.Log(2.0))).Within(1e-12));
        }
    }
}
