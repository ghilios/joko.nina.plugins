using MathNet.Numerics.Distributions;
using OxyPlot.Series;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    internal static class SyntheticFocusCurveSamples {

        // Noise convention: when a *Points generator is given noiseSigma > 0, it perturbs each Y by a Gaussian
        // (std = noiseSigma) drawn from a fresh System.Random(seed), so the data is fully deterministic per seed.
        // Each call owns its RNG, so two calls with the SAME seed yield independent but identically-sequenced noise
        // — use different seeds if a test needs uncorrelated noise across two curves. noiseSigma = 0 (the default)
        // is byte-for-byte identical to the legacy noise-free output.

        // y = a / b * sqrt((x - x0)^2 + b^2) + y0
        public static double Hyperbola(double x, double x0, double y0, double a, double b) {
            var dx = x - x0;
            return a / b * Math.Sqrt(dx * dx + b * b) + y0;
        }

        public static List<ScatterErrorPoint> SymmetricHyperbolaPoints(
            double x0,
            double y0,
            double a,
            double b,
            double xStart,
            double xStep,
            int count,
            double errorY = 1.0,
            double noiseSigma = 0.0,
            int seed = 0) {
            var points = new List<ScatterErrorPoint>(count);
            Normal dist = noiseSigma > 0.0 ? new Normal(0.0, noiseSigma, new System.Random(seed)) : null;
            for (var i = 0; i < count; ++i) {
                var x = xStart + i * xStep;
                var y = Hyperbola(x, x0, y0, a, b) + (dist != null ? dist.Sample() : 0.0);
                points.Add(new ScatterErrorPoint(x, y, 0, errorY));
            }
            return points;
        }

        // Tilted hyperbola: y = y0 + a/b * sqrt((x - x0)^2 + b^2) + s * (x - x0)
        public static double TiltedHyperbola(double x, double x0, double y0, double a, double b, double s) {
            var u = x - x0;
            return y0 + a / b * Math.Sqrt(u * u + b * b) + s * u;
        }

        // Closed-form minimum (best focus) x of the tilted hyperbola. Requires |s| < a/b.
        public static double TiltedHyperbolaMinimumX(double x0, double a, double b, double s) {
            var k = a / b;
            var d = Math.Sqrt(k * k - s * s);
            return x0 - s * b / d;
        }

        public static List<ScatterErrorPoint> TiltedHyperbolaPoints(
            double x0,
            double y0,
            double a,
            double b,
            double s,
            double xStart,
            double xStep,
            int count,
            double errorY = 1.0,
            double noiseSigma = 0.0,
            int seed = 0) {
            var points = new List<ScatterErrorPoint>(count);
            Normal dist = noiseSigma > 0.0 ? new Normal(0.0, noiseSigma, new System.Random(seed)) : null;
            for (var i = 0; i < count; ++i) {
                var x = xStart + i * xStep;
                var y = TiltedHyperbola(x, x0, y0, a, b, s) + (dist != null ? dist.Sample() : 0.0);
                points.Add(new ScatterErrorPoint(x, y, 0, errorY));
            }
            return points;
        }

        // Smooth logistic blend: y = y0 + t*(a/b)*sqrt(u^2+b^2) + (1-t)*(a/c)*sqrt(u^2+c^2), t = 1/(1+exp(u/w))
        public static double SmoothBlend(double x, double x0, double y0, double a, double b, double c, double w) {
            var u = x - x0;
            var t = 1.0 / (1.0 + Math.Exp(u / w));
            var left = t * a / b * Math.Sqrt(u * u + b * b);
            var right = (1.0 - t) * a / c * Math.Sqrt(u * u + c * c);
            return y0 + left + right;
        }

        public static List<ScatterErrorPoint> SmoothBlendPoints(
            double x0,
            double y0,
            double a,
            double b,
            double c,
            double w,
            double xStart,
            double xStep,
            int count,
            double errorY = 1.0) {
            var points = new List<ScatterErrorPoint>(count);
            for (var i = 0; i < count; ++i) {
                var x = xStart + i * xStep;
                var y = SmoothBlend(x, x0, y0, a, b, c, w);
                points.Add(new ScatterErrorPoint(x, y, 0, errorY));
            }
            return points;
        }

        // Legacy "uneven" blend: y = t*(a/b)*sqrt(u^2+b^2) + (1-t)*(a/c)*sqrt(u^2+c^2) + y0,
        // t = clamp((x0 - x)/stepSize, 0, 1). Matches HyperbolicUnevenFittingAlglib.ModelValue.
        public static double UnevenBlend(double x, double x0, double y0, double a, double b, double c, double stepSize) {
            var u = x - x0;
            var t = Math.Clamp((x0 - x) / stepSize, 0.0, 1.0);
            var left = t * a / b * Math.Sqrt(u * u + b * b);
            var right = (1.0 - t) * a / c * Math.Sqrt(u * u + c * c);
            return y0 + left + right;
        }

        public static List<ScatterErrorPoint> UnevenBlendPoints(
            double x0,
            double y0,
            double a,
            double b,
            double c,
            double stepSize,
            double xStart,
            double xStep,
            int count,
            double errorY = 1.0) {
            var points = new List<ScatterErrorPoint>(count);
            for (var i = 0; i < count; ++i) {
                var x = xStart + i * xStep;
                var y = UnevenBlend(x, x0, y0, a, b, c, stepSize);
                points.Add(new ScatterErrorPoint(x, y, 0, errorY));
            }
            return points;
        }
    }
}
