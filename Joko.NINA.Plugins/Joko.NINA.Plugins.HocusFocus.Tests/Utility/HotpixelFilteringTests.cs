using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class HotpixelFilteringTests {

        [Test]
        public void RawImageData_LengthMismatch_Throws() {
            Assert.Throws<System.ArgumentException>(() =>
                new RawImageData(new ushort[10], width: 4, height: 4));
        }

        [Test]
        public void RawImageData_GetSetPixel_RoundTrip() {
            var data = new RawImageData(new ushort[16], 4, 4);
            data.SetPixel(2, 1, 1234);
            Assert.That(data.GetPixel(2, 1), Is.EqualTo(1234));
        }

        [Test]
        public void HotpixelFilter_ReplacesIsolatedSpike_WithMedianBlur() {
            using var mat = new Mat(new Size(5, 5), MatType.CV_16UC1, new Scalar(100));
            mat.Set(2, 2, (ushort)10000);

            HotpixelFiltering.HotpixelFilter(mat);

            // Median of a 3x3 region of mostly 100s with one spike → 100
            Assert.That((ushort)mat.At<ushort>(2, 2), Is.EqualTo(100));
        }

        [Test]
        public void HotpixelFilterWithThresholding_CountsAndFixesSpike() {
            using var mat = new Mat(new Size(5, 5), MatType.CV_16UC1, new Scalar(100));
            mat.Set(2, 2, (ushort)10000);

            var count = HotpixelFiltering.HotpixelFilterWithThresholding(mat, threshold: 1000);

            Assert.Multiple(() => {
                Assert.That(count, Is.EqualTo(1));
                Assert.That((ushort)mat.At<ushort>(2, 2), Is.EqualTo(100));
            });
        }

        [Test]
        public void HotpixelFilterWithThresholding_BelowThreshold_DoesNotReplace() {
            using var mat = new Mat(new Size(5, 5), MatType.CV_16UC1, new Scalar(100));
            mat.Set(2, 2, (ushort)200);

            var count = HotpixelFiltering.HotpixelFilterWithThresholding(mat, threshold: 1000);

            Assert.Multiple(() => {
                Assert.That(count, Is.EqualTo(0));
                Assert.That((ushort)mat.At<ushort>(2, 2), Is.EqualTo(200));
            });
        }

        [Test]
        public void CFAHotpixelFilter_DetectsSpike_RGGB() {
            // 8x8 RGGB with a single hot pixel (R) at (4,4)
            const int W = 8, H = 8;
            var arr = new ushort[W * H];
            for (var i = 0; i < arr.Length; ++i) arr[i] = 100;
            var raw = new RawImageData(arr, W, H);
            raw.SetPixel(4, 4, 5000);

            var count = HotpixelFiltering.CFAHotpixelFilter(raw, SensorType.RGGB, threshold: 1000);

            Assert.Multiple(() => {
                Assert.That(count, Is.GreaterThanOrEqualTo(1));
                Assert.That(raw.GetPixel(4, 4), Is.LessThan((ushort)5000));
            });
        }

        [Test]
        public void CFAHotpixelFilter_FlatImage_FindsNoHotpixels() {
            const int W = 16, H = 16;
            var arr = new ushort[W * H];
            for (var i = 0; i < arr.Length; ++i) arr[i] = 200;
            var raw = new RawImageData(arr, W, H);

            var count = HotpixelFiltering.CFAHotpixelFilter(raw, SensorType.RGGB, threshold: 50);

            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void CFAHotpixelFilter_UnsupportedPattern_Throws() {
            const int W = 8, H = 8;
            var arr = new ushort[W * H];
            var raw = new RawImageData(arr, W, H);

            // SensorType.Monochrome is not handled by the filter
            Assert.Throws<Accord.Imaging.InvalidImagePropertiesException>(() =>
                HotpixelFiltering.CFAHotpixelFilter(raw, SensorType.Monochrome, threshold: 100));
        }
    }
}
