using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Proves the early/late detection split is bit-identical to the monolithic <c>Detect</c> and that the
    /// early-context cache is safe to reuse across late-only param changes. These tests guard the optimizer
    /// speedup refactor: the cache may reuse a <see cref="StarDetector.DetectionContext"/> ONLY when the early
    /// params match, and reusing one across different LATE params must produce exactly the monolithic result.
    /// </summary>
    [TestFixture]
    public class StarDetectorEarlyLateSplitTests {

        private static StarDetector NewDetector() => new StarDetector(new AlglibAPI());

        // Runs the split path (BuildDetectionContext + GateAndMeasure) on a fresh field.
        private static async Task<HocusFocusStarDetectorResult> RunSplit(StarDetectorParams p) {
            var detector = NewDetector();
            using var field = StarDetectorEquivalence.BuildSmallField();
            var ctx = await detector.BuildDetectionContext(field, p, null, CancellationToken.None);
            using (ctx) {
                return detector.GateAndMeasure(ctx, p, CancellationToken.None);
            }
        }

        [Test]
        public async Task SplitPath_MatchesMonolithicDetect_DefaultParams() {
            var p = StarDetectorEquivalence.StandardParams();

            using var monoField = StarDetectorEquivalence.BuildSmallField();
            var monoResult = await NewDetector().Detect(monoField, p, null, CancellationToken.None);
            var monoSig = StarDetectorEquivalence.Signature(monoResult);

            var splitResult = await RunSplit(p);
            var splitSig = StarDetectorEquivalence.Signature(splitResult);

            Assert.Multiple(() => {
                Assert.That(splitSig, Is.EqualTo(monoSig),
                    "BuildDetectionContext + GateAndMeasure must produce a byte-identical signature to monolithic Detect");
                Assert.That(splitResult.StructureNoiseSigma, Is.EqualTo(monoResult.StructureNoiseSigma),
                    "structure-map noise sigma must be identical");
                Assert.That(splitResult.MeasurementNoiseSigma, Is.EqualTo(monoResult.MeasurementNoiseSigma),
                    "measurement-image noise sigma must be identical");
            });
        }

        [Test]
        public async Task SplitPath_MatchesMonolithicDetect_WithNoiseReductionAndRoi() {
            // Exercise the branch where the measurement image differs from the structure-map source (two distinct
            // noise estimates) AND a non-trivial ROI is in play.
            var p = StarDetectorEquivalence.StandardParams();
            p.NoiseReductionRadius = 2;
            p.StarMeasurementNoiseReductionEnabled = false;
            p.Region = new StarDetectionRegion(new RatioRect(0.1, 0.1, 0.8, 0.8));

            using var monoField = StarDetectorEquivalence.BuildSmallField();
            var monoResult = await NewDetector().Detect(monoField, p, null, CancellationToken.None);
            var monoSig = StarDetectorEquivalence.Signature(monoResult);

            var splitResult = await RunSplit(p);
            var splitSig = StarDetectorEquivalence.Signature(splitResult);

            Assert.That(splitSig, Is.EqualTo(monoSig),
                "split path must match monolithic Detect even with noise reduction + ROI offset");
        }

        [Test]
        public async Task ReusedContext_AcrossLateParamChanges_MatchesFreshFullDetect() {
            // Build the early context ONCE from default params, then gate-and-measure with several different LATE
            // param sets. Each must equal a fresh full Detect with those same params — proving the cached early
            // context is exact to reuse across late-only changes.
            var earlyParams = StarDetectorEquivalence.StandardParams();
            var detector = NewDetector();
            using var ctxField = StarDetectorEquivalence.BuildSmallField();
            using var ctx = await detector.BuildDetectionContext(ctxField, earlyParams, null, CancellationToken.None);

            // Each variant changes ONLY late-stage gate params (so the early key is unchanged).
            var lateVariants = new[] {
                Mutate(earlyParams, q => q.Sensitivity = 5.0),
                Mutate(earlyParams, q => q.MinimumStarBoundingBoxSize = 8),
                Mutate(earlyParams, q => q.MaxDistortion = 0.7),
                Mutate(earlyParams, q => q.PeakResponse = 0.5),
                Mutate(earlyParams, q => q.StarCenterTolerance = 0.15),
                Mutate(earlyParams, q => q.MinHFR = 2.5),
                Mutate(earlyParams, q => q.StarClippingMultiplier = 3.0),
            };

            foreach (var lateParams in lateVariants) {
                var reusedSig = StarDetectorEquivalence.Signature(detector.GateAndMeasure(ctx, lateParams, CancellationToken.None));

                using var freshField = StarDetectorEquivalence.BuildSmallField();
                var freshSig = StarDetectorEquivalence.Signature(await NewDetector().Detect(freshField, lateParams, null, CancellationToken.None));

                Assert.That(reusedSig, Is.EqualTo(freshSig),
                    $"reusing the early context with late params {lateParams} must equal a fresh full Detect");
            }
        }

        [Test]
        public void EarlyCacheKey_SameForLateOnlyChanges_DifferentForEarlyChanges() {
            var baseP = StarDetectorEquivalence.StandardParams();
            var baseKey = StarDetector.ComputeEarlyCacheKey(baseP);

            // LATE-only changes must NOT change the early key.
            Assert.Multiple(() => {
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.Sensitivity = 9.0)), Is.EqualTo(baseKey), "Sensitivity is late-only");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.StarClippingMultiplier = 4.0)), Is.EqualTo(baseKey), "StarClippingMultiplier is late-only");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.PeakResponse = 0.4)), Is.EqualTo(baseKey), "PeakResponse is late-only");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.MaxDistortion = 0.9)), Is.EqualTo(baseKey), "MaxDistortion is late-only");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.MinHFR = 3.0)), Is.EqualTo(baseKey), "MinHFR is late-only");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.StarCenterTolerance = 0.1)), Is.EqualTo(baseKey), "StarCenterTolerance is late-only");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.MinimumStarBoundingBoxSize = 9)), Is.EqualTo(baseKey), "MinimumStarBoundingBoxSize is late-only");
            });

            // EARLY changes MUST change the early key.
            Assert.Multiple(() => {
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.NoiseClippingMultiplier = 5.0)), Is.Not.EqualTo(baseKey), "NoiseClippingMultiplier is early");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.StructureLayers = 5)), Is.Not.EqualTo(baseKey), "StructureLayers is early");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.NoiseReductionRadius = 5)), Is.Not.EqualTo(baseKey), "NoiseReductionRadius is early");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.HotpixelThresholdingEnabled = false)), Is.Not.EqualTo(baseKey), "HotpixelThresholdingEnabled is early");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.HotpixelThreshold = 0.5)), Is.Not.EqualTo(baseKey), "HotpixelThreshold is early");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.HotpixelFiltering = false)), Is.Not.EqualTo(baseKey), "HotpixelFiltering is early");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.StarMeasurementNoiseReductionEnabled = true)), Is.Not.EqualTo(baseKey), "StarMeasurementNoiseReductionEnabled is early");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.StructureDilationCount = 1)), Is.Not.EqualTo(baseKey), "StructureDilationCount is early");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.StructureDilationSize = 5)), Is.Not.EqualTo(baseKey), "StructureDilationSize is early");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.SaturationThreshold = 0.5)), Is.Not.EqualTo(baseKey), "SaturationThreshold affects an early metric (over-keyed for safety)");
                Assert.That(StarDetector.ComputeEarlyCacheKey(Mutate(baseP, q => q.Region = new StarDetectionRegion(new RatioRect(0.1, 0.1, 0.5, 0.5)))), Is.Not.EqualTo(baseKey), "Region is early");
            });
        }

        private static StarDetectorParams Mutate(StarDetectorParams p, System.Action<StarDetectorParams> mutate) {
            var clone = p.Clone();
            mutate(clone);
            return clone;
        }
    }
}
