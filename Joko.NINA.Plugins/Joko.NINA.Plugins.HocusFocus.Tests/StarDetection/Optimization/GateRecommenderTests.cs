using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization {

    [TestFixture]
    public class GateRecommenderTests {

        private static AnalyzedRejection R(string gate, double measured, bool recall, double size = 10, bool sr = false, int pos = 5000) =>
            new AnalyzedRejection {
                Gate = gate,
                MeasuredValue = measured,
                ThresholdValue = 2.0,
                CandidateSize = size,
                FocuserPosition = pos,
                IsRecallTarget = recall,
                InShouldRejectBox = sr
            };

        private static LabelGateAnalysis Analysis(IEnumerable<AnalyzedRejection> recs, int noCandidate = 0, int totalRecall = -1) {
            var list = recs.ToList();
            var noCand = Enumerable.Range(0, noCandidate).Select(_ => new NoCandidateTarget()).ToList();
            return new LabelGateAnalysis {
                Rejections = list,
                NoCandidate = noCand,
                NoCandidateCount = noCandidate,
                TotalRecallTargets = totalRecall >= 0 ? totalRecall : list.Count(r => r.IsRecallTarget) + noCandidate
            };
        }

        private static StarDetectorParams CurrentParams() => new StarDetectorParams {
            Sensitivity = 2.0, MinHFR = 1.2, MaxDistortion = 0.5, PeakResponse = 0.75,
            StarCenterTolerance = 0.3, MinimumStarBoundingBoxSize = 5
        };

        [Test]
        public void LowSensitivity_AdmitsAllLabeled_WithinBudget() {
            var a = Analysis(new[] {
                R(RejectionGate.LowSensitivity, 1.1, recall: true),
                R(RejectionGate.LowSensitivity, 1.3, recall: true),
                R(RejectionGate.LowSensitivity, 1.5, recall: true),
                R(RejectionGate.LowSensitivity, 1.4, recall: false),
                R(RejectionGate.LowSensitivity, 1.6, recall: false),
            });
            var rec = GateRecommender.Recommend(a, CurrentParams());
            var row = rec.Rows.Single(r => r.Gate == RejectionGate.LowSensitivity);
            Assert.Multiple(() => {
                Assert.That(row.Applied, Is.True);
                Assert.That(row.Recovered, Is.EqualTo(3));
                Assert.That(row.NewValue.Value, Is.LessThan(1.1), "Sensitivity must drop below the faintest labeled star's ratio to admit it");
                Assert.That(rec.Recommended.Sensitivity, Is.EqualTo(row.NewValue.Value));
                Assert.That(row.WeightedPrecisionCost, Is.EqualTo(2.0), "both unlabeled candidates (1.4, 1.6) are newly admitted");
            });
        }

        [Test]
        public void LowSensitivity_ShouldRejectLabel_VetoesFurtherLoosening() {
            var a = Analysis(new[] {
                R(RejectionGate.LowSensitivity, 1.5, recall: true),
                R(RejectionGate.LowSensitivity, 1.1, recall: true),
                R(RejectionGate.LowSensitivity, 1.3, recall: false, sr: true), // user said: reject this one
            });
            var rec = GateRecommender.Recommend(a, CurrentParams());
            var row = rec.Rows.Single(r => r.Gate == RejectionGate.LowSensitivity);
            Assert.Multiple(() => {
                Assert.That(row.Recovered, Is.EqualTo(1), "only the 1.5 target is recoverable without re-admitting the should-reject at 1.3");
                Assert.That(rec.Recommended.Sensitivity, Is.GreaterThanOrEqualTo(1.3), "must not drop below the should-reject star's ratio");
            });
        }

        [Test]
        public void LowSensitivity_DenseUnlabeled_BacksOffOnCostEffectiveness() {
            var recs = new List<AnalyzedRejection> {
                R(RejectionGate.LowSensitivity, 1.9, recall: true),
                R(RejectionGate.LowSensitivity, 1.5, recall: true),
            };
            recs.AddRange(Enumerable.Range(0, 10).Select(_ => R(RejectionGate.LowSensitivity, 1.6, recall: false)));
            var rec = GateRecommender.Recommend(Analysis(recs), CurrentParams());
            var row = rec.Rows.Single(r => r.Gate == RejectionGate.LowSensitivity);
            Assert.That(row.Recovered, Is.EqualTo(1),
                "admitting the 1.5 target would flood in 10 unlabeled candidates (cost 10 > 2*2), so back off to just the 1.9 target");
        }

        [Test]
        public void TooFlat_RaisesPeakResponse() {
            var a = Analysis(new[] {
                R(RejectionGate.TooFlat, 0.80, recall: true),
                R(RejectionGate.TooFlat, 0.85, recall: true),
            });
            var rec = GateRecommender.Recommend(a, CurrentParams());
            var row = rec.Rows.Single(r => r.Gate == RejectionGate.TooFlat);
            Assert.Multiple(() => {
                Assert.That(row.Recovered, Is.EqualTo(2));
                Assert.That(rec.Recommended.PeakResponse, Is.GreaterThan(0.85), "PeakResponse must rise above the flattest labeled ratio");
                Assert.That(rec.Recommended.PeakResponse, Is.LessThanOrEqualTo(1.0));
            });
        }

        [Test]
        public void TooDistorted_LargeTargets_RecommendsDefocusAwareGates() {
            var a = Analysis(new[] {
                R(RejectionGate.TooDistorted, 0.30, recall: true, size: 50),
                R(RejectionGate.TooDistorted, 0.35, recall: true, size: 45),
            });
            var rec = GateRecommender.Recommend(a, CurrentParams());
            Assert.Multiple(() => {
                Assert.That(rec.RecommendDefocusAwareGates, Is.True);
                Assert.That(rec.Recommended.DefocusAwareDistortion, Is.True);
                Assert.That(rec.Recommended.MaxDistortion, Is.EqualTo(0.5), "the global MaxDistortion is left alone for large/defocused targets");
            });
        }

        [Test]
        public void TooDistorted_SmallTargets_LowersMaxDistortion() {
            var a = Analysis(new[] {
                R(RejectionGate.TooDistorted, 0.30, recall: true, size: 12),
                R(RejectionGate.TooDistorted, 0.35, recall: true, size: 14),
            });
            var rec = GateRecommender.Recommend(a, CurrentParams());
            Assert.Multiple(() => {
                Assert.That(rec.RecommendDefocusAwareGates, Is.False);
                Assert.That(rec.Recommended.MaxDistortion, Is.LessThan(0.30), "lower MaxDistortion to admit the small distorted stars");
            });
        }

        [Test]
        public void NonInvertibleGate_IsReportedNotApplied() {
            var a = Analysis(new[] { R(RejectionGate.Degenerate, double.NaN, recall: true) });
            var rec = GateRecommender.Recommend(a, CurrentParams());
            var row = rec.Rows.Single(r => r.Gate == RejectionGate.Degenerate);
            Assert.Multiple(() => {
                Assert.That(row.Applied, Is.False);
                Assert.That(row.Note, Does.Contain("not recoverable"));
                Assert.That(rec.Recommended.Sensitivity, Is.EqualTo(2.0), "no scalar gate touched");
            });
        }

        [Test]
        public void ManyNoCandidate_RecommendsStructureRecovery() {
            var a = Analysis(new[] { R(RejectionGate.LowSensitivity, 1.5, recall: true) }, noCandidate: 5, totalRecall: 10);
            var rec = GateRecommender.Recommend(a, CurrentParams());
            Assert.That(rec.RecommendStructureRecovery, Is.True);
        }
    }
}
