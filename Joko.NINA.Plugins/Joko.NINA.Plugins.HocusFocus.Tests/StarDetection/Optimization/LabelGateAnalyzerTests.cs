using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization {

    [TestFixture]
    public class LabelGateAnalyzerTests {

        private static RejectedCandidateRecord Rec(string gate, int x, int y, int w, int h,
            double measured = double.NaN, double thr = double.NaN) =>
            new RejectedCandidateRecord {
                Gate = gate,
                Bounds = new Rect(x, y, w, h),
                MeasuredValue = measured,
                ThresholdValue = thr,
                CandidateSize = Math.Max(w, h),
                CenterX = x + w / 2.0,
                CenterY = y + h / 2.0
            };

        [Test]
        public void Analyze_RecallBoxOverRejected_BucketsByGateAndMarksTarget() {
            var rej = Rec(RejectionGate.LowSensitivity, 100, 100, 20, 20, measured: 1.3, thr: 2.0);
            var frame = new FrameDetectionForAnalysis {
                FocuserPosition = 5000,
                Rejected = new List<RejectedCandidateRecord> { rej },
                RecallBoxes = new List<RectD> { new RectD(100, 100, 20, 20) }
            };

            var a = LabelGateAnalyzer.Analyze(new[] { frame });

            Assert.Multiple(() => {
                Assert.That(a.TotalRecallTargets, Is.EqualTo(1));
                Assert.That(a.AlreadyRecovered, Is.EqualTo(0));
                Assert.That(a.NoCandidateCount, Is.EqualTo(0));
                var targets = a.RecallTargets(RejectionGate.LowSensitivity);
                Assert.That(targets, Has.Count.EqualTo(1));
                Assert.That(targets[0].MeasuredValue, Is.EqualTo(1.3));
                Assert.That(targets[0].FocuserPosition, Is.EqualTo(5000));
                Assert.That(a.RecallTargetCountByGate[RejectionGate.LowSensitivity], Is.EqualTo(1));
            });
        }

        [Test]
        public void Analyze_RecallBoxOverAccepted_CountsAlreadyRecovered() {
            var frame = new FrameDetectionForAnalysis {
                FocuserPosition = 5000,
                AcceptedBounds = new List<Rect> { new Rect(200, 200, 16, 16) },
                RecallBoxes = new List<RectD> { new RectD(200, 200, 16, 16) }
            };
            var a = LabelGateAnalyzer.Analyze(new[] { frame });
            Assert.Multiple(() => {
                Assert.That(a.AlreadyRecovered, Is.EqualTo(1));
                Assert.That(a.NoCandidateCount, Is.EqualTo(0));
                Assert.That(a.Rejections, Is.Empty);
            });
        }

        [Test]
        public void Analyze_RecallBoxOverNothing_IsNoCandidate() {
            var frame = new FrameDetectionForAnalysis {
                FocuserPosition = 5000,
                RecallBoxes = new List<RectD> { new RectD(10, 10, 8, 8) }
            };
            var a = LabelGateAnalyzer.Analyze(new[] { frame });
            Assert.Multiple(() => {
                Assert.That(a.NoCandidateCount, Is.EqualTo(1));
                Assert.That(a.NoCandidate[0].FocuserPosition, Is.EqualTo(5000));
            });
        }

        [Test]
        public void Analyze_UnlabeledRejection_IsNotRecallTarget() {
            // A rejected candidate that no label box overlaps is the precision-proxy "unlabeled" pool.
            var rej = Rec(RejectionGate.LowSensitivity, 800, 800, 10, 10, measured: 1.1, thr: 2.0);
            var frame = new FrameDetectionForAnalysis {
                FocuserPosition = 4000,
                Rejected = new List<RejectedCandidateRecord> { rej },
                RecallBoxes = new List<RectD>() // no labels here
            };
            var a = LabelGateAnalyzer.Analyze(new[] { frame });
            Assert.Multiple(() => {
                Assert.That(a.RecallTargets(RejectionGate.LowSensitivity), Is.Empty);
                Assert.That(a.Unlabeled(RejectionGate.LowSensitivity), Has.Count.EqualTo(1));
                Assert.That(a.Unlabeled(RejectionGate.LowSensitivity)[0].IsRecallTarget, Is.False);
            });
        }

        [Test]
        public void Analyze_ShouldRejectBox_TagsContainmentAndViolation() {
            var rejInside = Rec(RejectionGate.LowSensitivity, 300, 300, 10, 10);     // center 305,305 inside SR box
            var frame = new FrameDetectionForAnalysis {
                FocuserPosition = 4500,
                AcceptedCenters = new List<(double, double)> { (302, 302) },          // accepted inside SR box → violated
                Rejected = new List<RejectedCandidateRecord> { rejInside },
                ShouldRejectBoxes = new List<RectD> { new RectD(290, 290, 30, 30) }
            };
            var a = LabelGateAnalyzer.Analyze(new[] { frame });
            Assert.Multiple(() => {
                Assert.That(a.ShouldReject, Has.Count.EqualTo(1));
                Assert.That(a.ShouldReject[0].CurrentlyViolated, Is.True, "an accepted star inside the should-reject box is a present false positive");
                var unlabeled = a.Rejections.Single();
                Assert.That(unlabeled.InShouldRejectBox, Is.True, "a rejected candidate whose center is inside the should-reject box is flagged so loosening can't re-admit it");
            });
        }
    }
}
