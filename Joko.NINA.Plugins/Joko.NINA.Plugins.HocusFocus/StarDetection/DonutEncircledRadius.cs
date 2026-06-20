using NINA.Joko.Plugins.HocusFocus.Interfaces;
using OpenCvSharp;
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// True encircled-flux radius (R_e) for defocused "donut" stars — brightness-independent, unlike the
    /// flux-weighted-mean HFR (MeasureStar). Curve of growth from a ring-fit center with a noise-convergence
    /// integration cap. Validated config: ring center + adaptive cap + frac=0.5 (docs/donut-hfr-normalization-
    /// results.md). Pure + deterministic ⇒ unit-testable in isolation. NOT used for compact stars (caller gates
    /// on candidate size); 1-px radial bins make it unsuitable below ~6px median size.
    /// </summary>
    public static class DonutEncircledRadius {
        public readonly struct Result {
            public Result(double radius, double stdDev, double total, double convRadius) {
                Radius = radius; StdDev = stdDev; TotalFlux = total; ConvergenceRadius = convRadius;
            }
            public double Radius { get; }       // R_e (px)
            public double StdDev { get; }       // background-σ-propagated uncertainty (px), NaN if unavailable
            public double TotalFlux { get; }
            public double ConvergenceRadius { get; }
        }

        public static (double cx, double cy) RingCenter(Mat img, double cx0, double cy0,
                LocalBackgroundPlane plane, double scalarBg, double maxRadius, double noiseSigma) {
            int W = img.Width, H = img.Height;
            int x0 = Math.Max(0, (int)Math.Floor(cx0 - maxRadius)), x1 = Math.Min(W - 1, (int)Math.Ceiling(cx0 + maxRadius));
            int y0 = Math.Max(0, (int)Math.Floor(cy0 - maxRadius)), y1 = Math.Min(H - 1, (int)Math.Ceiling(cy0 + maxRadius));
            double sw = 0, sx = 0, sy = 0, thresh = 3.0 * noiseSigma;
            for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x) {
                    double dx = x - cx0, dy = y - cy0;
                    if (dx * dx + dy * dy > maxRadius * maxRadius) continue;
                    double v = img.At<float>(y, x) - (plane != null ? plane.ValueAt(x, y) : scalarBg);
                    if (v > thresh) { sw += v; sx += v * x; sy += v * y; }
                }
            return sw > 0 ? (sx / sw, sy / sw) : (cx0, cy0);
        }

        /// <summary>
        /// Preconditions: <paramref name="maxRadius"/> &gt; 0 and <paramref name="frac"/> ∈ (0,1).
        /// </summary>
        public static Result Measure(Mat img, double cx, double cy, LocalBackgroundPlane plane,
                double scalarBg, double noiseSigma, double maxRadius, double frac) {
            if (!(frac > 0.0 && frac < 1.0)) throw new System.ArgumentOutOfRangeException(nameof(frac), "frac must be in (0,1)");
            double re = RadiusFromBins(ScanAnnular(img, cx, cy, plane, scalarBg, maxRadius, 0.0), noiseSigma, maxRadius, frac, out double total, out double convR);
            // Background-σ-propagated uncertainty: half the ±0.1σ plane-perturbation spread (design §6).
            // When noise is unavailable (noiseSigma <= 0) the perturbation is zero, so StdDev is reported as NaN per the Result contract.
            double sd;
            if (noiseSigma > 0) {
                double rePlus = RadiusFromBins(ScanAnnular(img, cx, cy, plane, scalarBg, maxRadius, 0.1 * noiseSigma), noiseSigma, maxRadius, frac, out _, out _);
                double reMinus = RadiusFromBins(ScanAnnular(img, cx, cy, plane, scalarBg, maxRadius, -0.1 * noiseSigma), noiseSigma, maxRadius, frac, out _, out _);
                sd = (double.IsNaN(rePlus) || double.IsNaN(reMinus)) ? double.NaN : Math.Abs(rePlus - reMinus) / 2.0;
            } else {
                sd = double.NaN;
            }
            return new Result(re, sd, total, convR);
        }

        private static double[] ScanAnnular(Mat img, double cx, double cy, LocalBackgroundPlane plane,
                double scalarBg, double maxRadius, double bgDelta) {
            int nbins = (int)Math.Ceiling(maxRadius) + 1;
            var annular = new double[nbins];
            int W = img.Width, H = img.Height;
            int x0 = Math.Max(0, (int)Math.Floor(cx - maxRadius)), x1 = Math.Min(W - 1, (int)Math.Ceiling(cx + maxRadius));
            int y0 = Math.Max(0, (int)Math.Floor(cy - maxRadius)), y1 = Math.Min(H - 1, (int)Math.Ceiling(cy + maxRadius));
            for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x) {
                    double dx = x - cx, dy = y - cy, r = Math.Sqrt(dx * dx + dy * dy);
                    if (r > maxRadius) continue;
                    int b = (int)r; if (b >= nbins) continue;
                    annular[b] += img.At<float>(y, x) - ((plane != null ? plane.ValueAt(x, y) : scalarBg) + bgDelta);
                }
            return annular;
        }

        private static double RadiusFromBins(double[] annular, double noiseSigma, double maxRadius, double frac, out double total, out double convR) {
            int nbins = annular.Length, below = 0; convR = maxRadius;
            if (noiseSigma > 0)
                for (int b = 5; b < nbins; ++b) {
                    double floor = noiseSigma * Math.Sqrt(2.0 * Math.PI * (b + 0.5));
                    if (annular[b] < floor) { if (++below >= 3) { convR = b - 2; break; } } else below = 0;
                }
            int convBin = Math.Min((int)convR, nbins - 1);
            var cum = new double[nbins]; double acc = 0;
            for (int b = 0; b < nbins; ++b) { acc += annular[b]; cum[b] = acc; }
            total = convBin >= 0 ? cum[convBin] : double.NaN;
            if (!(total > 0)) return double.NaN;
            double target = frac * total;
            for (int b = 0; b <= convBin; ++b)
                if (cum[b] >= target) {
                    double prev = b > 0 ? cum[b - 1] : 0.0, denom = cum[b] - prev;
                    return b + (denom > 1e-12 ? (target - prev) / denom : 0.0);
                }
            return double.NaN;
        }
    }
}
