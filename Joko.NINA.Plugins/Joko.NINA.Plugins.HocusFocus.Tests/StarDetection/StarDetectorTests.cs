using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class StarDetectorTests {

        [Test]
        public void ComputeMedian_OddLength_ReturnsCenterElement() {
            // 5 elements: sorted [1, 2, 3, 4, 5] — median is index 2 = 3.0
            var pixels = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(3.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EvenLength_AveragesTwoMiddleElements() {
            // 10 elements: [1,2,3,4,5,6,7,8,9,10]
            // Correct middle indices: 4 and 5 (values 5 and 6), median = 5.5
            // Buggy formula used Length >> (1+1) = Length / 4 = index 2,
            // producing (pixels[3] + pixels[2]) / 2 = (4 + 3) / 2 = 3.5 — a different result.
            var pixels = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(5.5).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EvenLength_TwoElements_AveragesBoth() {
            var pixels = new double[] { 3.0, 7.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(5.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EvenLength_FourElements_AveragesTwoMiddleElements() {
            // [10, 20, 30, 40] — middle indices 1 and 2 → (20 + 30) / 2 = 25.0
            var pixels = new double[] { 10.0, 20.0, 30.0, 40.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(25.0).Within(1e-12));
        }
    }
}
