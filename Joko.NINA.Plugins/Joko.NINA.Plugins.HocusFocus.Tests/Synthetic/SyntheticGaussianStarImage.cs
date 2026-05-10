using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    internal static class SyntheticGaussianStarImage {

        public static Mat Create(
            int width,
            int height,
            double centerX,
            double centerY,
            double sigmaX,
            double sigmaY,
            double peak,
            double background) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            unsafe {
                var data = (float*)mat.DataPointer;
                var inv2sx2 = 1.0 / (2.0 * sigmaX * sigmaX);
                var inv2sy2 = 1.0 / (2.0 * sigmaY * sigmaY);
                for (var y = 0; y < height; ++y) {
                    for (var x = 0; x < width; ++x) {
                        var dx = x - centerX;
                        var dy = y - centerY;
                        var e = dx * dx * inv2sx2 + dy * dy * inv2sy2;
                        data[y * width + x] = (float)(background + peak * Math.Exp(-e));
                    }
                }
            }
            return mat;
        }

        public static Mat CreateFlat(int width, int height, float value) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            mat.SetTo(new Scalar(value));
            return mat;
        }
    }
}
