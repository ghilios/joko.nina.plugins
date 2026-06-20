using NINA.Joko.Plugins.HocusFocus.Interfaces;
using OpenCvSharp;
using System;

namespace TestApp {
    /// <summary>
    /// Offline donut-size oracle. (A) Encircled-flux radius R_e via curve of growth from a ring-fit center
    /// (the production-candidate metric). (B) An annulus⊛Gaussian forward fit whose geometric R_out is
    /// brightness-independent by construction (the ground truth). Used by the A-vs-B agreement report.
    /// </summary>
    internal static class DonutRadiusOracle {
        // ----- (A) encircled-flux radius (validated config: ring center + noise-convergence cap) -----

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

        public static double[] ScanAnnularBins(Mat img, double cx, double cy,
                LocalBackgroundPlane plane, double scalarBg, double maxRadius, double bgDelta) {
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

        public static double EncircledRadius(double[] annular, double noiseSigma, double maxRadius, double frac) {
            int nbins = annular.Length, below = 0; double convR = maxRadius;
            if (noiseSigma > 0)
                for (int b = 5; b < nbins; ++b) {
                    double floor = noiseSigma * Math.Sqrt(2.0 * Math.PI * (b + 0.5));
                    if (annular[b] < floor) { if (++below >= 3) { convR = b - 2; break; } } else below = 0;
                }
            int convBin = Math.Min((int)convR, nbins - 1);
            var cum = new double[nbins]; double acc = 0;
            for (int b = 0; b < nbins; ++b) { acc += annular[b]; cum[b] = acc; }
            double total = convBin >= 0 ? cum[convBin] : double.NaN;
            if (!(total > 0)) return double.NaN;
            double target = frac * total;
            for (int b = 0; b <= convBin; ++b)
                if (cum[b] >= target) {
                    double prev = b > 0 ? cum[b - 1] : 0.0, denom = cum[b] - prev;
                    return b + (denom > 1e-12 ? (target - prev) / denom : 0.0);
                }
            return double.NaN;
        }

        // ----- (B) annulus ⊛ Gaussian forward fit (brightness-independent ground truth) -----
        // Coarse grid search over (Rout, eps, sigmaSeeing), amplitude+background fit by correlation (scale-free).
        // Returns geometric Rout and ring-centroid Rring = Rout*(1+eps)/2 (the comparator for A's R_e).

        public sealed class AnnulusFit { public double Rout, Eps, SigmaSeeing, Rring, RSquared; }

        public static AnnulusFit FitAnnulus(double[] annular, double maxRadius) {
            int nbins = annular.Length;
            var profile = new double[nbins];
            for (int b = 0; b < nbins; ++b) {
                double area = Math.PI * ((b + 1.0) * (b + 1.0) - b * b);
                profile[b] = annular[b] / Math.Max(area, 1e-9);
            }
            double best = double.NegativeInfinity; var bestFit = new AnnulusFit();
            for (double Rout = 6; Rout <= maxRadius; Rout += 0.5)
                for (double eps = 0.0; eps <= 0.7; eps += 0.05)
                    for (double sg = 0.5; sg <= 8.0; sg += 0.5) {
                        double sxy = 0, sxx = 0, syy = 0, mx = 0, my = 0; int n = 0;
                        var model = new double[nbins];
                        for (int b = 0; b < nbins; ++b) {
                            double r = b + 0.5;
                            double rise = 0.5 * Erfc((eps * Rout - r) / (sg * Math.Sqrt(2.0)));
                            double fall = 0.5 * Erfc((r - Rout) / (sg * Math.Sqrt(2.0)));
                            model[b] = rise * fall; mx += model[b]; my += profile[b]; n++;
                        }
                        mx /= n; my /= n;
                        for (int b = 0; b < nbins; ++b) { double a = model[b] - mx, d = profile[b] - my; sxy += a * d; sxx += a * a; syy += d * d; }
                        double r2 = (sxx > 0 && syy > 0) ? (sxy * sxy) / (sxx * syy) : 0.0;
                        if (r2 > best) { best = r2; bestFit = new AnnulusFit { Rout = Rout, Eps = eps, SigmaSeeing = sg, Rring = Rout * (1 + eps) / 2.0, RSquared = r2 }; }
                    }
            return bestFit;
        }

        private static double Erfc(double x) {
            double z = Math.Abs(x), t = 1.0 / (1.0 + 0.5 * z);
            double ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
                t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
                t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0.0 ? ans : 2.0 - ans;
        }
    }
}
