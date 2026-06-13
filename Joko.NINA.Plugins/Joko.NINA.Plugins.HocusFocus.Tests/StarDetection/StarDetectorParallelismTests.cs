#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Verifies that parallelizing the per-star evaluation (Stage B of <c>ScanStars</c>) is a pure speed
    /// change: the detection output must be byte-identical regardless of the degree of parallelism.
    ///
    /// <list type="bullet">
    ///   <item><term>parallel == sequential</term><description>
    ///     Running <c>Detect</c> with <see cref="StarDetectorParams.MaxStarEvaluationParallelism"/> = 1
    ///     (sequential kill-switch) and = 0 (auto / fully parallel) must yield the identical
    ///     <see cref="StarDetectorEquivalence.Signature"/>. Per-star results do not cross-aggregate, so the
    ///     assembled <c>DetectedStars</c> and the (Y,X)-sorted metrics bounds are independent of thread count.
    ///   </description></item>
    ///   <item><term>concurrent stress</term><description>
    ///     Many parallel <c>Detect</c> calls on independent fields must each match the sequential signature,
    ///     guarding against shared-state regressions (e.g. the instance diagnostics bag, or any accidental
    ///     reuse of mutable buffers) when multiple detections run at once.
    ///   </description></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class StarDetectorParallelismTests {

        private static StarDetectorParams ParamsWithParallelism(int knob) => new StarDetectorParams {
            ModelPSF = false,
            MaxStarEvaluationParallelism = knob,
        };

        [Test]
        public async Task Detect_SmallField_ParallelMatchesSequential() {
            using var fieldSeq = StarDetectorEquivalence.BuildSmallField();
            var seqResult = await StarDetectorEquivalence.RunDetect(fieldSeq, ParamsWithParallelism(1));
            var seqSig = StarDetectorEquivalence.Signature(seqResult);

            using var fieldPar = StarDetectorEquivalence.BuildSmallField();
            var parResult = await StarDetectorEquivalence.RunDetect(fieldPar, ParamsWithParallelism(0));
            var parSig = StarDetectorEquivalence.Signature(parResult);

            TestContext.Progress.WriteLine($"[parallel==seq] sequential detected={seqResult.DetectedStars.Count}, parallel detected={parResult.DetectedStars.Count}");
            Assert.That(parSig, Is.EqualTo(seqSig),
                "Parallel star evaluation (MaxStarEvaluationParallelism=0) must produce a byte-identical " +
                "signature to sequential (=1). A difference means the parallelization altered results.");
        }

        // A few explicit thread counts to exercise the governor's clamping and small/large partitions.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(0)] // auto
        public async Task Detect_LargeField_AllParallelismLevelsMatchSequential(int knob) {
            using var fieldSeq = StarDetectorEquivalence.BuildLargeField();
            var seqResult = await StarDetectorEquivalence.RunDetect(fieldSeq, ParamsWithParallelism(1));
            var seqSig = StarDetectorEquivalence.Signature(seqResult);

            using var field = StarDetectorEquivalence.BuildLargeField();
            var result = await StarDetectorEquivalence.RunDetect(field, ParamsWithParallelism(knob));
            var sig = StarDetectorEquivalence.Signature(result);

            Assert.That(sig, Is.EqualTo(seqSig),
                $"MaxStarEvaluationParallelism={knob} must match the sequential signature on the large field.");
        }

        [Test]
        public async Task Detect_ConcurrentRuns_AllMatchSequentialSignature() {
            // Reference signature, computed sequentially on the large field.
            using var refField = StarDetectorEquivalence.BuildLargeField();
            var refResult = await StarDetectorEquivalence.RunDetect(refField, ParamsWithParallelism(1));
            var expectedSig = StarDetectorEquivalence.Signature(refResult);

            // Run many parallel detections at once, each on its own freshly-built (but identical) field, with
            // auto parallelism. This stresses both the inner Stage-B parallelism and the shared CPU governor
            // (ProcessorCount² potential threads), and surfaces any cross-run shared-state regression.
            const int concurrentRuns = 8;
            var tasks = new List<Task<string>>(concurrentRuns);
            for (int i = 0; i < concurrentRuns; ++i) {
                tasks.Add(Task.Run(async () => {
                    using var field = StarDetectorEquivalence.BuildLargeField();
                    var result = await StarDetectorEquivalence.RunDetect(field, ParamsWithParallelism(0));
                    return StarDetectorEquivalence.Signature(result);
                }));
            }

            var signatures = await Task.WhenAll(tasks);

            Assert.Multiple(() => {
                for (int i = 0; i < signatures.Length; ++i) {
                    Assert.That(signatures[i], Is.EqualTo(expectedSig),
                        $"Concurrent run #{i} signature differs from the sequential reference — shared-state regression.");
                }
            });
        }

        [Test]
        public void StarDetectorMetrics_Merge_SumsScalarsAndConcatenatesBounds() {
            // Direct unit test of the Merge fold used to combine thread-local metrics.
            // Covers all seven *Bounds lists plus representative scalars so that a future
            // eighth bounds list cannot be silently dropped from Merge without a test failure.
            var a = new StarDetectorMetrics {
                StructureCandidates = 3,
                TotalDetected = 2,
                TooSmall = 1,
                OnBorder = 1,
                TooLowHFR = 1,
                HFRAnalysisFailed = 1,
                PSFFitFailed = 1,
                OutsideROI = 1,
                SaturatedPixelCount = 10L,
                HotpixelCount = 100L,
            };
            a.TooDistortedBounds.Add(new Rect(5, 5, 2, 2));
            a.DegenerateBounds.Add(new Rect(6, 6, 2, 2));
            a.SaturatedBounds.Add(new Rect(7, 7, 2, 2));
            a.LowSensitivityBounds.Add(new Rect(8, 8, 2, 2));
            a.NotCenteredBounds.Add(new Rect(9, 9, 2, 2));
            a.TooFlatBounds.Add(new Rect(10, 10, 2, 2));
            a.ContaminatedBounds.Add(new Rect(11, 11, 2, 2));

            var b = new StarDetectorMetrics {
                StructureCandidates = 4,
                TotalDetected = 3,
                TooSmall = 2,
                OnBorder = 0,
                TooLowHFR = 5,
                HFRAnalysisFailed = 0,
                PSFFitFailed = 2,
                OutsideROI = 0,
                SaturatedPixelCount = 1L,
                HotpixelCount = 23L,
            };
            b.TooDistortedBounds.Add(new Rect(1, 1, 3, 3));
            b.DegenerateBounds.Add(new Rect(2, 2, 3, 3));
            b.SaturatedBounds.Add(new Rect(3, 3, 3, 3));
            b.LowSensitivityBounds.Add(new Rect(4, 4, 3, 3));
            b.NotCenteredBounds.Add(new Rect(5, 5, 3, 3));
            b.TooFlatBounds.Add(new Rect(6, 6, 3, 3));
            b.ContaminatedBounds.Add(new Rect(7, 7, 4, 4));

            a.Merge(b);

            Assert.Multiple(() => {
                // Scalars must be summed.
                Assert.That(a.StructureCandidates, Is.EqualTo(7));
                Assert.That(a.TotalDetected, Is.EqualTo(5));
                Assert.That(a.TooSmall, Is.EqualTo(3));
                Assert.That(a.OnBorder, Is.EqualTo(1));
                Assert.That(a.TooLowHFR, Is.EqualTo(6));
                Assert.That(a.HFRAnalysisFailed, Is.EqualTo(1));
                Assert.That(a.PSFFitFailed, Is.EqualTo(3));
                Assert.That(a.OutsideROI, Is.EqualTo(1));
                Assert.That(a.SaturatedPixelCount, Is.EqualTo(11L));
                Assert.That(a.HotpixelCount, Is.EqualTo(123L));

                // All seven *Bounds lists must be concatenated (1 from a + 1 from b = 2 each).
                Assert.That(a.TooDistortedBounds.Count, Is.EqualTo(2), "TooDistortedBounds not concatenated");
                Assert.That(a.DegenerateBounds.Count, Is.EqualTo(2), "DegenerateBounds not concatenated");
                Assert.That(a.SaturatedBounds.Count, Is.EqualTo(2), "SaturatedBounds not concatenated");
                Assert.That(a.LowSensitivityBounds.Count, Is.EqualTo(2), "LowSensitivityBounds not concatenated");
                Assert.That(a.NotCenteredBounds.Count, Is.EqualTo(2), "NotCenteredBounds not concatenated");
                Assert.That(a.TooFlatBounds.Count, Is.EqualTo(2), "TooFlatBounds not concatenated");
                Assert.That(a.ContaminatedBounds.Count, Is.EqualTo(2), "ContaminatedBounds not concatenated");
            });
        }

        [Test]
        public void StarDetectorMetrics_SortBounds_OrdersByYThenX() {
            var m = new StarDetectorMetrics();
            m.TooDistortedBounds.Add(new OpenCvSharp.Rect(10, 5, 1, 1));
            m.TooDistortedBounds.Add(new OpenCvSharp.Rect(2, 5, 1, 1));
            m.TooDistortedBounds.Add(new OpenCvSharp.Rect(8, 1, 1, 1));

            m.SortBounds();

            var ordered = m.TooDistortedBounds.Select(r => (r.Y, r.X)).ToList();
            Assert.That(ordered, Is.EqualTo(new List<(int, int)> { (1, 8), (5, 2), (5, 10) }));
        }
    }
}
