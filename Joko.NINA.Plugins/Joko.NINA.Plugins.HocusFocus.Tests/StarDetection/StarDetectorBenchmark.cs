using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Explicit (non-suite) wall-clock benchmark for the star detector on a large deterministic
    /// synthetic field (~2048×2048, ~361 stars). Does NOT run in the normal test suite — invoke with:
    ///
    ///   dotnet test ... --filter "FullyQualifiedName~StarDetectorBenchmark"
    ///
    /// Two variants are timed:
    ///   – <b>ModelPSF=false</b>: the auto-focus production default (PRIMARY number).
    ///   – <b>ModelPSF=true</b>: PSF-modeling path (SECONDARY number).
    ///
    /// One untimed warm-up run is performed before each variant to cover JIT and OpenCV/native
    /// initialization so the reported numbers reflect steady-state throughput.
    ///
    /// If the environment variable <c>FOCUS_BENCH_OUT</c> is set, a CSV row is appended to
    /// <c>{FOCUS_BENCH_OUT}/star_detector_benchmark.csv</c> (following the convention in
    /// <see cref="FocusCurveBenchmark"/>).
    /// </summary>
    [TestFixture]
    public class StarDetectorBenchmark {

        private const int K = 5;  // timed iterations per variant

        [Test, Explicit("Wall-clock benchmark — run explicitly; not part of the normal suite")]
        public async Task Benchmark_ModelPsfOff() {
            var p = StarDetectorEquivalence.StandardParams();   // ModelPSF = false
            await RunBenchmark("ModelPSF=false", p);
        }

        [Test, Explicit("Wall-clock benchmark — run explicitly; not part of the normal suite")]
        public async Task Benchmark_ModelPsfOn() {
            var p = StarDetectorEquivalence.StandardParams();
            p.ModelPSF = true;
            await RunBenchmark("ModelPSF=true", p);
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────

        private static async Task RunBenchmark(string variantLabel, NINA.Joko.Plugins.HocusFocus.Interfaces.StarDetectorParams p) {
            TestContext.Progress.WriteLine($"[benchmark] variant={variantLabel}  field={StarDetectorEquivalence.LargeFieldWidth}×{StarDetectorEquivalence.LargeFieldHeight}");

            // Build the field once; each iteration clones it so the Mat is not mutated between runs.
            using var baseField = StarDetectorEquivalence.BuildLargeField();

            // ── Warm-up (one untimed run: covers JIT + any OpenCV/native initialization) ──────────────
            TestContext.Progress.WriteLine("[benchmark] warming up...");
            {
                using var warmupImage = baseField.Clone();
                var warmupResult = await StarDetectorEquivalence.RunDetect(warmupImage, p);
                TestContext.Progress.WriteLine($"[benchmark] warmup: detected={warmupResult.DetectedStars.Count}");
            }

            // ── Timed iterations ──────────────────────────────────────────────────────────────────────
            var wallMs = new long[K];
            int lastDetectedCount = 0;

            for (int k = 0; k < K; ++k) {
                // Clone so each run starts from an unmodified image.
                using var image = baseField.Clone();
                var sw = Stopwatch.StartNew();
                var result = await StarDetectorEquivalence.RunDetect(image, p);
                sw.Stop();
                wallMs[k] = sw.ElapsedMilliseconds;
                lastDetectedCount = result.DetectedStars.Count;

                // The production StarDetector wraps DetectImpl in a MultiStopWatch, but the per-phase
                // timing is stored in a local variable inside DetectImpl and is not exposed on the result
                // type. We therefore report total wall-clock time only and note per-phase timing is not
                // accessible without a production-code change.
                TestContext.Progress.WriteLine($"[benchmark] k={k + 1}/{K}  total={wallMs[k]} ms  detected={result.DetectedStars.Count}");
            }

            // ── Summary ───────────────────────────────────────────────────────────────────────────────
            Array.Sort(wallMs);
            long min = wallMs[0];
            long median = Percentile(wallMs, 50);
            long p90 = Percentile(wallMs, 90);

            var summary = new StringBuilder();
            summary.AppendLine($"[benchmark] variant={variantLabel}  K={K}  detected={lastDetectedCount}");
            summary.AppendLine($"[benchmark]   min={min} ms  median={median} ms  p90={p90} ms");
            summary.AppendLine($"[benchmark]   raw_ms=[{string.Join(", ", wallMs)}]");
            TestContext.Progress.Write(summary.ToString());

            // ── Optional CSV output (mirrors FocusCurveBenchmark.WriteCsvAndSummary convention) ──────
            var outDir = Environment.GetEnvironmentVariable("FOCUS_BENCH_OUT");
            if (!string.IsNullOrEmpty(outDir)) {
                Directory.CreateDirectory(outDir);
                var csvPath = Path.Combine(outDir, "star_detector_benchmark.csv");
                bool writeHeader = !File.Exists(csvPath);
                using var writer = new StreamWriter(csvPath, append: true);
                if (writeHeader)
                    writer.WriteLine("variant,k,detected,min_ms,median_ms,p90_ms,raw_ms");
                writer.WriteLine(string.Join(",",
                    QuoteCsv(variantLabel),
                    K.ToString(CultureInfo.InvariantCulture),
                    lastDetectedCount.ToString(CultureInfo.InvariantCulture),
                    min.ToString(CultureInfo.InvariantCulture),
                    median.ToString(CultureInfo.InvariantCulture),
                    p90.ToString(CultureInfo.InvariantCulture),
                    QuoteCsv(string.Join(";", wallMs))));
                TestContext.Progress.WriteLine($"[benchmark] CSV appended to {csvPath}");
            }
        }

        private static long Percentile(long[] sorted, int pct) {
            if (sorted.Length == 0) return 0;
            double idx = (sorted.Length - 1) * pct / 100.0;
            int lo = (int)Math.Floor(idx);
            int hi = (int)Math.Ceiling(idx);
            if (lo == hi) return sorted[lo];
            return (long)(sorted[lo] * (hi - idx) + sorted[hi] * (idx - lo));
        }

        private static string QuoteCsv(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
    }
}
