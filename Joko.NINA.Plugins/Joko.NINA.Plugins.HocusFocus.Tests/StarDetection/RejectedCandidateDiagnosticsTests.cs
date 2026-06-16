using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Phase 1 of the "Optimize with feedback" work: the per-rejected-candidate diagnostics side channel
    /// (<see cref="StarDetectorParams.CollectRejectedCandidateDiagnostics"/> →
    /// <see cref="HocusFocusStarDetectorResult.RejectedCandidates"/>).
    ///
    /// Guards three properties:
    ///  1. <b>Bit-identical when OFF/ON</b> — turning the flag on must not change ANY detected star or metric
    ///     (the records are emitted after the reject decision is already made), and the flag must be excluded
    ///     from the detection cache key.
    ///  2. <b>Complete</b> — every rejected candidate produces exactly one record (records.Count == rejected),
    ///     including the counter-only gates (TooSmall/TooLowHFR) that have no metrics *Bounds list.
    ///  3. <b>Invertible</b> — each scalar gate's recorded MeasuredValue lies on the reject side of its
    ///     ThresholdValue, so the recommender can solve for the threshold that recovers the star.
    /// </summary>
    [TestFixture]
    public class RejectedCandidateDiagnosticsTests {

        [Test]
        public async Task Diagnostics_OffVsOn_DetectionBitIdentical() {
            var pOff = StarDetectorEquivalence.StandardParams();
            var pOn = StarDetectorEquivalence.StandardParams();
            pOn.CollectRejectedCandidateDiagnostics = true;

            using var field1 = StarDetectorEquivalence.BuildSmallField();
            var resultOff = await StarDetectorEquivalence.RunDetect(field1, pOff);

            using var field2 = StarDetectorEquivalence.BuildSmallField();
            var resultOn = await StarDetectorEquivalence.RunDetect(field2, pOn);

            Assert.Multiple(() => {
                Assert.That(StarDetectorEquivalence.Signature(resultOn), Is.EqualTo(StarDetectorEquivalence.Signature(resultOff)),
                    "Enabling rejected-candidate diagnostics must not change any detected star or metric.");
                Assert.That(resultOff.RejectedCandidates, Is.Null, "OFF ⇒ the side-channel list is null (zero overhead).");
                Assert.That(resultOn.RejectedCandidates, Is.Not.Null, "ON ⇒ the side-channel list is populated.");
            });
        }

        [Test]
        public void Diagnostics_FlagExcludedFromCacheKey() {
            var pOff = StarDetectorEquivalence.StandardParams();
            var pOn = StarDetectorEquivalence.StandardParams();
            pOn.CollectRejectedCandidateDiagnostics = true;

            // The flag is a pure side channel — it must NOT change the canonical cache string, else the
            // optimizer's early-context cache could treat diagnostics-on and -off as different detections.
            Assert.That(pOn.ToCanonicalCacheString(), Is.EqualTo(pOff.ToCanonicalCacheString()),
                "CollectRejectedCandidateDiagnostics must be excluded from the detection cache key.");
        }

        [Test]
        public async Task Diagnostics_RecordsAccountForEveryRejection() {
            var p = StarDetectorEquivalence.StandardParams();
            p.CollectRejectedCandidateDiagnostics = true;

            using var field = StarDetectorEquivalence.BuildSmallField();
            var result = await StarDetectorEquivalence.RunDetect(field, p);

            var m = result.Metrics;
            var rejected = m.StructureCandidates - m.TotalDetected;
            var records = result.RejectedCandidates;

            Assert.Multiple(() => {
                // One record per rejected candidate (ModelPSF is off in StandardParams, so the only ways a
                // candidate leaves the accepted set are the gates, each of which now emits exactly one record).
                Assert.That(records.Count, Is.EqualTo(rejected),
                    "Every rejected candidate must produce exactly one diagnostics record.");

                // Per-gate record counts agree with the metrics tallies (including counter-only gates).
                int RecordCount(string gate) => records.Count(r => r.Gate == gate);
                Assert.That(RecordCount(RejectionGate.TooSmall), Is.EqualTo(m.TooSmall), "TooSmall");
                Assert.That(RecordCount(RejectionGate.OnBorder), Is.EqualTo(m.OnBorder), "OnBorder");
                Assert.That(RecordCount(RejectionGate.TooDistorted), Is.EqualTo(m.TooDistorted), "TooDistorted");
                Assert.That(RecordCount(RejectionGate.Degenerate), Is.EqualTo(m.Degenerate), "Degenerate");
                Assert.That(RecordCount(RejectionGate.LowSensitivity), Is.EqualTo(m.LowSensitivity), "LowSensitivity");
                Assert.That(RecordCount(RejectionGate.NotCentered), Is.EqualTo(m.NotCentered), "NotCentered");
                Assert.That(RecordCount(RejectionGate.TooFlat), Is.EqualTo(m.TooFlat), "TooFlat");
                Assert.That(RecordCount(RejectionGate.TooLowHFR), Is.EqualTo(m.TooLowHFR), "TooLowHFR");
                Assert.That(RecordCount(RejectionGate.HFRAnalysisFailed), Is.EqualTo(m.HFRAnalysisFailed), "HFRAnalysisFailed");
                // ContaminatedBounds counts all flagged; with RejectContaminatedStars=true (the default) all are rejected.
                Assert.That(RecordCount(RejectionGate.Contaminated), Is.EqualTo(m.ContaminationSuspected), "Contaminated");
            });
        }

        [Test]
        public async Task Diagnostics_MeasuresHfr_ForRejectedCandidates() {
            // Reject every (real, bright) candidate via an extreme Sensitivity. Because these are rejected AFTER
            // ComputeStarParameters, the detector measures their HFR on-demand for the review's HFR display — so the
            // LowSensitivity records must carry a finite, positive HFR (not the NaN of pre-parameter gates).
            var p = StarDetectorEquivalence.StandardParams();
            p.CollectRejectedCandidateDiagnostics = true;
            p.Sensitivity = 1000.0;

            using var field = StarDetectorEquivalence.BuildSmallField();
            var result = await StarDetectorEquivalence.RunDetect(field, p);

            var lowSens = result.RejectedCandidates.Where(r => r.Gate == RejectionGate.LowSensitivity).ToList();
            Assert.That(lowSens, Is.Not.Empty);
            Assert.That(lowSens.Count(r => !double.IsNaN(r.Hfr) && r.Hfr > 0.0), Is.GreaterThan(0),
                "rejected candidates with a centroid must get an on-demand HFR measurement for the review display");
        }

        [Test]
        public async Task Diagnostics_InvertsLowSensitivity() {
            // Crank the sensitivity threshold so every otherwise-valid bright candidate fails the LowSensitivity
            // gate. Each such record must carry the candidate's measured sensitivity ON the reject side of the
            // threshold (measured <= threshold), which is exactly what the recommender inverts.
            var p = StarDetectorEquivalence.StandardParams();
            p.CollectRejectedCandidateDiagnostics = true;
            p.Sensitivity = 1000.0;

            using var field = StarDetectorEquivalence.BuildSmallField();
            var result = await StarDetectorEquivalence.RunDetect(field, p);

            var lowSens = result.RejectedCandidates.Where(r => r.Gate == RejectionGate.LowSensitivity).ToList();
            Assert.That(lowSens, Is.Not.Empty, "An extreme Sensitivity threshold must produce LowSensitivity rejections.");
            Assert.Multiple(() => {
                foreach (var r in lowSens) {
                    Assert.That(r.ThresholdValue, Is.EqualTo(1000.0), "threshold echoes the effective gate value");
                    Assert.That(r.MeasuredValue, Is.LessThanOrEqualTo(r.ThresholdValue),
                        "the measured sensitivity must be on the reject side of the threshold");
                    Assert.That(r.CandidateSize, Is.GreaterThan(0.0), "candidate size (defocus proxy) is recorded");
                }
            });
        }
    }
}
