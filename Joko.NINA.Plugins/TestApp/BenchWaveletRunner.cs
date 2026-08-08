#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Size = OpenCvSharp.Size;

namespace TestApp {

    /// <summary>
    /// Headless benchmark for the structure-removal à trous wavelet step:
    /// legacy CvImageUtility.ComputeResidualAtrousB3SplineDyadicWaveletLayer (dense zero-padded Cv2.SepFilter2D)
    /// vs AtrousWaveletFast (sparse 5-tap, SIMD, multithreaded), Mat-level and optionally end-to-end through
    /// StarDetector.Detect on a synthetic star field. Needs no NINA profile and no input images.
    ///
    /// Usage: TestApp bench-wavelet [--sizes 6248x4176,3008x3008] [--layers 4,6,8] [--iters 3] [--detect]
    ///                              [--impls legacy,fast,fast1t,fused] [--seed 42]
    /// </summary>
    public static class BenchWaveletRunner {
        private const float Background = 0.05f;
        private const double NoiseSigma = 0.02;
        private const double StarSigmaPx = 2.5;
        private const double StarPeak = 0.7;
        private const int StarSpacingPx = 102;
        private const int StarMarginPx = 56;

        public static async Task Run(string[] args) {
            var sizesArg = DiagnosticUtil.GetArg(args, "--sizes") ?? "6248x4176,3008x3008";
            var layersArg = DiagnosticUtil.GetArg(args, "--layers") ?? "4,6,8";
            var itersArg = DiagnosticUtil.GetArg(args, "--iters") ?? "3";
            var implsArg = DiagnosticUtil.GetArg(args, "--impls") ?? "legacy,fast,fast1t,fused";
            var seedArg = DiagnosticUtil.GetArg(args, "--seed") ?? "42";
            bool detect = DiagnosticUtil.HasFlag(args, "--detect");

            var sizes = new List<(int Width, int Height)>();
            foreach (var token in sizesArg.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
                var parts = token.Split('x');
                if (parts.Length != 2
                    || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
                    || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
                    || w <= 0 || h <= 0) {
                    Console.Error.WriteLine($"Bad --sizes token: {token} (expected WxH)");
                    Environment.ExitCode = 2;
                    return;
                }
                sizes.Add((w, h));
            }
            var layersList = layersArg.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList();
            int iters = int.Parse(itersArg, CultureInfo.InvariantCulture);
            int seed = int.Parse(seedArg, CultureInfo.InvariantCulture);
            var impls = new HashSet<string>(implsArg.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

            Console.WriteLine($"ProcessorCount={Environment.ProcessorCount}, Vector<float>.Count={Vector<float>.Count}, " +
                              $"Vector.IsHardwareAccelerated={Vector.IsHardwareAccelerated}, GC={(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}");

            foreach (var (width, height) in sizes) {
                Console.WriteLine();
                Console.WriteLine($"== {width}x{height} ({width * (long)height / 1_000_000.0:F1} MP) ==");
                using var field = BuildStarField(width, height, seed);

                foreach (var layers in layersList) {
                    var line = $"layers={layers}";
                    double legacyMs = double.NaN;
                    Mat legacyResidual = null;
                    Mat fastResidual = null;
                    try {
                        if (impls.Contains("legacy")) {
                            legacyMs = TimeMedian(() => {
                                legacyResidual?.Dispose();
                                legacyResidual = CvImageUtility.ComputeResidualAtrousB3SplineDyadicWaveletLayer(field, layers);
                            }, iters);
                            line += $"  legacy={legacyMs,9:F1} ms";
                        }
                        if (impls.Contains("fast")) {
                            var fastMs = TimeMedian(() => {
                                fastResidual?.Dispose();
                                fastResidual = AtrousWaveletFast.ComputeResidual(field, layers);
                            }, iters);
                            line += $"  fast={fastMs,7:F1} ms";
                            if (!double.IsNaN(legacyMs)) {
                                line += $" ({legacyMs / fastMs,5:F1}x)";
                            }
                        }
                        if (impls.Contains("fast1t")) {
                            var fast1tMs = TimeMedian(() => {
                                using var r = AtrousWaveletFast.ComputeResidual(field, layers, parallelismKnob: 1);
                            }, iters);
                            line += $"  fast-1T={fast1tMs,8:F1} ms";
                        }
                        if (impls.Contains("fused")) {
                            var cloneMs = TimeMedian(() => {
                                using var c = field.Clone();
                            }, iters);
                            var fusedMs = TimeMedian(() => {
                                using var work = field.Clone();
                                AtrousWaveletFast.ComputeResidualAndSubtractInPlace(work, layers);
                            }, iters) - cloneMs; // net of the per-iteration clone
                            line += $"  fused={fusedMs,8:F1} ms";
                        }
                        if (legacyResidual != null && fastResidual != null) {
                            line += $"  maxAbsDiff={MaxAbsDiff(legacyResidual, fastResidual):E2}";
                        }
                        Console.WriteLine(line);
                    } finally {
                        legacyResidual?.Dispose();
                        fastResidual?.Dispose();
                    }
                }

                if (detect) {
                    foreach (var layers in layersList) {
                        await RunDetectAb(field, layers, iters);
                    }
                }
            }
        }

        private static async Task RunDetectAb(Mat field, int structureLayers, int iters) {
            // Production detection always uses AtrousWaveletFast now; the legacy-vs-fast comparison lives at the
            // Mat level above (the legacy oracle is retained in CvImageUtility for exactly that purpose).
            var p = new StarDetectorParams {
                ModelPSF = false,
                NoiseClippingMultiplier = 4.0,
                StructureLayers = structureLayers,
            };
            var (medianMs, result) = await TimeDetect(field, p, iters);
            var stars = result.DetectedStars ?? new List<Star>();
            Console.WriteLine($"detect layers={structureLayers}: {medianMs:F0} ms ({stars.Count} stars)");
        }

        private static async Task<(double MedianMs, HocusFocusStarDetectorResult Result)> TimeDetect(Mat field, StarDetectorParams p, int iters) {
            var detector = new StarDetector(new AlglibAPI());
            HocusFocusStarDetectorResult result = null;
            var times = new List<double>();
            for (int i = 0; i < iters + 1; ++i) { // first iteration is warmup
                using var work = field.Clone(); // Detect mutates the input in place
                GC.Collect();
                var sw = Stopwatch.StartNew();
                result = await detector.Detect(work, p, null, CancellationToken.None);
                sw.Stop();
                if (i > 0) {
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
            }
            times.Sort();
            return (times[times.Count / 2], result);
        }

        private static double TimeMedian(Action action, int iters) {
            action(); // warmup (JIT, page-in)
            var times = new List<double>();
            for (int i = 0; i < iters; ++i) {
                GC.Collect();
                var sw = Stopwatch.StartNew();
                action();
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            return times[times.Count / 2];
        }

        private static unsafe double MaxAbsDiff(Mat a, Mat b) {
            var pa = (float*)a.DataPointer;
            var pb = (float*)b.DataPointer;
            long stepA = a.Step() / sizeof(float);
            long stepB = b.Step() / sizeof(float);
            double maxDiff = 0.0;
            for (int y = 0; y < a.Height; ++y) {
                for (int x = 0; x < a.Width; ++x) {
                    var diff = Math.Abs((double)pa[y * stepA + x] - pb[y * stepB + x]);
                    if (diff > maxDiff) {
                        maxDiff = diff;
                    }
                }
            }
            return maxDiff;
        }

        /// <summary>Deterministic synthetic star field: flat background + Gaussian star grid + Gaussian noise
        /// (mirrors the test suite's StarDetectorEquivalence field construction).</summary>
        private static unsafe Mat BuildStarField(int width, int height, int seed) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F, new Scalar(Background));
            var p = (float*)mat.DataPointer;
            long step = mat.Step() / sizeof(float);

            int halfExtent = (int)Math.Ceiling(4 * StarSigmaPx);
            for (int cy = StarMarginPx; cy < height - StarMarginPx; cy += StarSpacingPx) {
                for (int cx = StarMarginPx; cx < width - StarMarginPx; cx += StarSpacingPx) {
                    for (int y = Math.Max(0, cy - halfExtent); y <= Math.Min(height - 1, cy + halfExtent); ++y) {
                        for (int x = Math.Max(0, cx - halfExtent); x <= Math.Min(width - 1, cx + halfExtent); ++x) {
                            double dx = x - cx;
                            double dy = y - cy;
                            p[y * step + x] += (float)(StarPeak * Math.Exp(-(dx * dx + dy * dy) / (2 * StarSigmaPx * StarSigmaPx)));
                        }
                    }
                }
            }

            // Box-Muller Gaussian noise, clamped to [0, 1]
            var random = new Random(seed);
            for (int y = 0; y < height; ++y) {
                var row = p + y * step;
                for (int x = 0; x < width; x += 2) {
                    double u1 = 1.0 - random.NextDouble();
                    double u2 = random.NextDouble();
                    double mag = Math.Sqrt(-2.0 * Math.Log(u1));
                    row[x] = Math.Clamp(row[x] + (float)(NoiseSigma * mag * Math.Cos(2.0 * Math.PI * u2)), 0.0f, 1.0f);
                    if (x + 1 < width) {
                        row[x + 1] = Math.Clamp(row[x + 1] + (float)(NoiseSigma * mag * Math.Sin(2.0 * Math.PI * u2)), 0.0f, 1.0f);
                    }
                }
            }
            return mat;
        }
    }
}
