using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Guards the ownership contract for Mats adopted from the EARLY-span accelerator
    /// (StarDetector's AdoptPreparedImage): on the monolithic Detect path every accelerator-produced Mat
    /// must be freed by the caller's ResourcesTracker (the context is never disposed there), and on the
    /// split path the context must keep owning its measurement image until the context itself is disposed
    /// (RunEvaluationData disposes contexts).
    /// </summary>
    [TestFixture]
    [NonParallelizable] // uses the static StarDetector.EarlyAcceleratorOverride seam
    public class GpuEarlySpanOwnershipTests {

        private sealed class FakeAccelerator : IEarlyPipelineAccelerator {
            public Mat LastStructureMap;
            public Mat LastMeasurementImage;
            public int RunCount;

            public bool TryRunEarlySpan(Mat srcImage, StarDetectorParams p, int effectiveStructureLayers, bool hotpixelAlreadyApplied, CancellationToken token, out EarlySpanOutput output) {
                RunCount++;
                // An all-zero structure map yields no candidates, so the late stage completes trivially; the
                // measurement clone stands in for the GPU-prepared (hotpixel-filtered) image.
                LastStructureMap = new Mat(srcImage.Rows, srcImage.Cols, MatType.CV_32F, Scalar.All(0));
                LastMeasurementImage = srcImage.Clone();
                output = new EarlySpanOutput {
                    StructureMap = LastStructureMap,
                    MeasurementImage = LastMeasurementImage,
                    StructureNoise = new CvImageUtility.KappaSigmaNoiseEstimateResult { Sigma = 0.01, BackgroundMean = 0.05, NumIterations = 1 },
                    MeasurementNoise = new CvImageUtility.KappaSigmaNoiseEstimateResult { Sigma = 0.01, BackgroundMean = 0.05, NumIterations = 1 },
                    StructureMapMedian = 0.0,
                    HotpixelCount = 0,
                };
                return true;
            }
        }

        [TearDown]
        public void TearDown() {
            StarDetector.EarlyAcceleratorOverride = null;
        }

        private static StarDetectorParams GpuParams() {
            var p = StarDetectorEquivalence.StandardParams();
            p.AllowGpuAcceleration = true;
            return p;
        }

        [Test]
        public async Task MonolithicDetect_FreesAdoptedAcceleratorMats() {
            var fake = new FakeAccelerator();
            StarDetector.EarlyAcceleratorOverride = fake;
            var p = GpuParams();

            using var field = StarDetectorEquivalence.BuildSmallField();
            await new StarDetector(new AlglibAPI()).Detect(field, p, null, CancellationToken.None);

            Assert.Multiple(() => {
                Assert.That(fake.RunCount, Is.EqualTo(1), "the override accelerator must have been consulted");
                Assert.That(fake.LastMeasurementImage.IsDisposed, Is.True,
                    "the adopted measurement Mat must be registered with the monolithic caller's tracker — DetectImpl never disposes the context, so an untracked adoption leaks a full-frame CV_32F Mat");
                Assert.That(fake.LastStructureMap.IsDisposed, Is.True,
                    "the structure map is tracked scratch and must be freed with the tracker");
            });
        }

        [Test]
        public async Task SplitBuildContext_ContextOwnsMeasurementImage_UntilDisposed() {
            var fake = new FakeAccelerator();
            StarDetector.EarlyAcceleratorOverride = fake;
            var p = GpuParams();

            var detector = new StarDetector(new AlglibAPI());
            using var field = StarDetectorEquivalence.BuildSmallField();
            var ctx = await detector.BuildDetectionContext(field, p, null, CancellationToken.None);

            Assert.That(fake.LastMeasurementImage.IsDisposed, Is.False,
                "on the split path the context owns the adopted Mat — tracking it in the local scratch tracker would be a use-after-free for GateAndMeasure");
            Assert.That(fake.LastStructureMap.IsDisposed, Is.True,
                "the structure map is local scratch on the split path, freed before BuildDetectionContext returns");

            detector.GateAndMeasure(ctx, p, CancellationToken.None);
            Assert.That(fake.LastMeasurementImage.IsDisposed, Is.False,
                "GateAndMeasure only reads the context image; the context must remain reusable");

            ctx.Dispose();
            Assert.That(fake.LastMeasurementImage.IsDisposed, Is.True,
                "disposing the context releases its measurement image");
        }
    }
}
