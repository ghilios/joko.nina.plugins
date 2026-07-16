#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Additive star stamping into the compositor's electron accumulator, plus the analytic render primitives
    /// (Gaussian star, uniform disk, annular donut, seeded Gaussian noise) that were previously test-only
    /// helpers. <see cref="Stamp"/> is the compositor core: it selects the sub-pixel-phase kernel matching a
    /// star's fractional centre and adds <c>kernel·flux</c> at the integer pixel position, so the total
    /// electrons deposited equal <paramref name="flux"/> (minus any part clipped off the sensor).
    /// </summary>
    public static class StarStamper {
        private const int SuperSample = 4; // NxN subsamples per pixel for the analytic Mat primitives

        /// <summary>
        /// Adds <c>kernel·flux</c> into a row-major <c>float[width*height]</c> electron accumulator at
        /// sub-pixel centre (cx, cy). The phase kernel whose centre offset (a/S, b/S) is nearest the fractional
        /// part of (cx, cy) is chosen; the kernel's centre pixel lands on floor(cx)/floor(cy) (adjusted when a
        /// coordinate rounds up to the next phase). Contributions outside the sensor are clipped.
        /// </summary>
        /// <remarks>
        /// The accumulator write is a non-atomic <c>+=</c>. A caller stamping in parallel must partition stars so
        /// concurrent calls touch <b>disjoint</b> accumulator regions. The <paramref name="rowStart"/>
        /// / <paramref name="rowEnd"/> row clip is exactly that seam: the compositor splits the frame into
        /// horizontal row-stripes and each parallel stripe passes its own <c>[rowStart, rowEnd)</c> so every
        /// stamp writes only that stripe's rows of the one shared accumulator — disjoint rows ⇒ no race. A star
        /// spanning two stripes is stamped by both, each clipping to its rows (flux is conserved additively).
        /// </remarks>
        /// <param name="rowStart">First accumulator row (inclusive) this call may write; clamped to 0.</param>
        /// <param name="rowEnd">One past the last accumulator row this call may write; clamped to <paramref name="height"/>.
        /// The default <c>[0, int.MaxValue)</c> clamps to <c>[0, height)</c>, i.e. the whole frame.</param>
        public static void Stamp(float[] accumulator, int width, int height, double cx, double cy, PsfKernel kernel, double flux, int rowStart = 0, int rowEnd = int.MaxValue) {
            if (accumulator == null) throw new ArgumentNullException(nameof(accumulator));
            if (kernel == null) throw new ArgumentNullException(nameof(kernel));
            if (accumulator.Length != width * height) throw new ArgumentException("Accumulator length must equal width*height.", nameof(accumulator));

            // The row clip subsumes the full-frame vertical bound: the default [0, +∞) clamps to [0, height).
            var clampedRowStart = Math.Max(0, rowStart);
            var clampedRowEnd = Math.Min(height, rowEnd);

            var s = kernel.PhasesPerAxis;
            var radius = kernel.Radius;
            var size = kernel.Size;

            var x0 = (int)Math.Floor(cx);
            var y0 = (int)Math.Floor(cy);
            var a = (int)Math.Round((cx - x0) * s);
            var b = (int)Math.Round((cy - y0) * s);
            if (a == s) { a = 0; ++x0; }
            if (b == s) { b = 0; ++y0; }

            var phase = kernel.GetPhaseKernel(a, b);
            var xStart = x0 - radius;
            var yStart = y0 - radius;

            for (var jj = 0; jj < size; ++jj) {
                var iy = yStart + jj;
                if (iy < clampedRowStart || iy >= clampedRowEnd) continue;
                var kernelRow = jj * size;
                var accRow = iy * width;
                for (var ii = 0; ii < size; ++ii) {
                    var ix = xStart + ii;
                    if (ix < 0 || ix >= width) continue;
                    accumulator[accRow + ix] += (float)(flux * phase[kernelRow + ii]);
                }
            }
        }

        /// <summary>Creates a flat CV_32F image of the given background value.</summary>
        public static Mat CreateFlat(int width, int height, float background) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            mat.SetTo(new Scalar(background));
            return mat;
        }

        /// <summary>Adds an additive Gaussian star (peak above background) at (cx, cy) with σ in pixels, radius ⌈5σ⌉.</summary>
        public static void AddGaussianStar(Mat mat, double cx, double cy, double sigma, double peak) {
            if (mat.Type() != MatType.CV_32F || !mat.IsContinuous()) throw new ArgumentException("AddGaussianStar requires a continuous CV_32F Mat");
            var rad = (int)Math.Ceiling(5.0 * sigma);
            var inv = 1.0 / (2.0 * sigma * sigma);
            int width = mat.Width, height = mat.Height;
            unsafe {
                var data = (float*)mat.DataPointer;
                for (var y = Math.Max(0, (int)cy - rad); y <= Math.Min(height - 1, (int)cy + rad); ++y) {
                    for (var x = Math.Max(0, (int)cx - rad); x <= Math.Min(width - 1, (int)cx + rad); ++x) {
                        double dx = x - cx, dy = y - cy;
                        data[y * width + x] += (float)(peak * Math.Exp(-(dx * dx + dy * dy) * inv));
                    }
                }
            }
        }

        /// <summary>Renders a uniform filled disk of the given radius (supersampled rim), on a flat background.</summary>
        public static Mat CreateDisk(int width, int height, double centerX, double centerY,
                double radius, double peak, double background, double edgeBlurSigma = 0.0) {
            return Render(width, height, centerX, centerY, background,
                r => DiskIntensity(r, radius, peak, edgeBlurSigma));
        }

        /// <summary>Renders a uniform annular donut [inner, outer] (supersampled rim), on a flat background.</summary>
        public static Mat CreateAnnulus(int width, int height, double centerX, double centerY,
                double innerRadius, double outerRadius, double peak, double background, double edgeBlurSigma = 0.0) {
            return Render(width, height, centerX, centerY, background,
                r => AnnulusIntensity(r, innerRadius, outerRadius, peak, edgeBlurSigma));
        }

        /// <summary>Adds zero-mean Gaussian noise of the given σ using a seeded RNG, via <see cref="NoiseGenerator.NextGaussian"/>.</summary>
        public static void AddGaussianNoise(Mat image, double sigma, int seed) {
            var noise = new NoiseGenerator(seed);
            var n = image.Width * image.Height;
            unsafe {
                var data = (float*)image.DataPointer;
                for (var i = 0; i < n; ++i) {
                    data[i] += (float)noise.NextGaussian(0.0, sigma);
                }
            }
        }

        private static Mat Render(int width, int height, double cx, double cy, double background,
                Func<double, double> radialIntensity) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            var inv = 1.0 / SuperSample;
            var sub = SuperSample * SuperSample;
            unsafe {
                var data = (float*)mat.DataPointer;
                for (var y = 0; y < height; ++y) {
                    for (var x = 0; x < width; ++x) {
                        var sum = 0.0;
                        for (var sy = 0; sy < SuperSample; ++sy) {
                            for (var sx = 0; sx < SuperSample; ++sx) {
                                var px = x - 0.5 + (sx + 0.5) * inv;
                                var py = y - 0.5 + (sy + 0.5) * inv;
                                double dx = px - cx, dy = py - cy;
                                sum += radialIntensity(Math.Sqrt(dx * dx + dy * dy));
                            }
                        }
                        data[y * width + x] = (float)(background + sum / sub);
                    }
                }
            }
            return mat;
        }

        private static double DiskIntensity(double r, double radius, double peak, double edgeBlurSigma) {
            if (edgeBlurSigma <= 0.0) return r <= radius ? peak : 0.0;
            return peak * 0.5 * Erfc((r - radius) / (edgeBlurSigma * Math.Sqrt(2.0)));
        }

        private static double AnnulusIntensity(double r, double inner, double outer, double peak, double edgeBlurSigma) {
            if (edgeBlurSigma <= 0.0) return (r >= inner && r <= outer) ? peak : 0.0;
            var rise = 0.5 * Erfc((inner - r) / (edgeBlurSigma * Math.Sqrt(2.0)));
            var fall = 0.5 * Erfc((r - outer) / (edgeBlurSigma * Math.Sqrt(2.0)));
            return peak * rise * fall;
        }

        // Abramowitz & Stegun 7.1.26 erfc approximation (max error ~1.2e-7).
        private static double Erfc(double x) {
            var z = Math.Abs(x);
            var t = 1.0 / (1.0 + 0.5 * z);
            var ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
                t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
                t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0.0 ? ans : 2.0 - ans;
        }
    }
}
