using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization {

    [TestFixture]
    public class BoxMatcherTests {

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
        public void IoU_IdenticalRects_IsOne() {
            Assert.That(BoxMatcher.IoU(new RectD(10, 10, 20, 20), new RectD(10, 10, 20, 20)), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void IoU_Disjoint_IsZero() {
            Assert.That(BoxMatcher.IoU(new RectD(0, 0, 10, 10), new RectD(100, 100, 10, 10)), Is.EqualTo(0.0));
        }

        [Test]
        public void Classify_OverlapsRejectedOnly_ReportsGateAndMeasuredValue() {
            var label = new RectD(100, 100, 20, 20);
            var rejected = new List<RejectedCandidateRecord> {
                Rec(RejectionGate.LowSensitivity, 100, 100, 20, 20, measured: 1.3, thr: 2.0)
            };
            var m = BoxMatcher.Classify(label, new List<Rect>(), rejected);
            Assert.Multiple(() => {
                Assert.That(m.Kind, Is.EqualTo(BoxClassification.Rejected));
                Assert.That(m.Gate, Is.EqualTo(RejectionGate.LowSensitivity));
                Assert.That(m.Rejected.MeasuredValue, Is.EqualTo(1.3));
            });
        }

        [Test]
        public void Classify_TooLowHFR_AttributedNotNoCandidate() {
            // The regression this whole side channel fixes: a TooLowHFR rejection has no metrics *Bounds list, but
            // it IS in the records pool, so it must classify as REJECTED:TooLowHFR rather than NO CANDIDATE.
            var label = new RectD(50, 50, 12, 12);
            var rejected = new List<RejectedCandidateRecord> {
                Rec(RejectionGate.TooLowHFR, 50, 50, 12, 12, measured: 0.9, thr: 1.2)
            };
            var m = BoxMatcher.Classify(label, new List<Rect>(), rejected);
            Assert.Multiple(() => {
                Assert.That(m.Kind, Is.EqualTo(BoxClassification.Rejected));
                Assert.That(m.Gate, Is.EqualTo(RejectionGate.TooLowHFR));
            });
        }

        [Test]
        public void Classify_OverlapsAcceptedOnly_IsAccepted() {
            var label = new RectD(200, 200, 16, 16);
            var accepted = new List<Rect> { new Rect(200, 200, 16, 16) };
            var m = BoxMatcher.Classify(label, accepted, new List<RejectedCandidateRecord>());
            Assert.That(m.Kind, Is.EqualTo(BoxClassification.Accepted));
        }

        [Test]
        public void Classify_OverlapsNeither_IsNoCandidate() {
            var label = new RectD(10, 10, 8, 8);
            var accepted = new List<Rect> { new Rect(500, 500, 16, 16) };
            var rejected = new List<RejectedCandidateRecord> { Rec(RejectionGate.LowSensitivity, 600, 600, 10, 10) };
            var m = BoxMatcher.Classify(label, accepted, rejected);
            Assert.That(m.Kind, Is.EqualTo(BoxClassification.NoCandidate));
        }

        [Test]
        public void Classify_AcceptedWinsTie() {
            // Same box overlaps an accepted star and a rejected candidate at equal IoU → accepted wins (detector's
            // actual outcome), but the rejected candidate is still reported for the tie note.
            var label = new RectD(300, 300, 20, 20);
            var accepted = new List<Rect> { new Rect(300, 300, 20, 20) };
            var rejected = new List<RejectedCandidateRecord> { Rec(RejectionGate.TooDistorted, 300, 300, 20, 20) };
            var m = BoxMatcher.Classify(label, accepted, rejected);
            Assert.Multiple(() => {
                Assert.That(m.Kind, Is.EqualTo(BoxClassification.Accepted));
                Assert.That(m.Rejected, Is.Not.Null, "the tied rejected candidate is still surfaced for the note");
                Assert.That(m.AcceptedIoU, Is.EqualTo(m.RejectedIoU).Within(1e-9));
            });
        }
    }
}
