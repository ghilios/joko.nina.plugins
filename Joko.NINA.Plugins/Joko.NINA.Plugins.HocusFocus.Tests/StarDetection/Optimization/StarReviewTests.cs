#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using TestApp.StarReview;
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// Unit tests for the pure-logic helpers of the T7 interactive star-review tool (linked from TestApp): the
/// label store (T6-compatible JSON + toggle/merge), the review-queue selection, and the zoom/pan viewport math.
/// The WPF window/VM are not testable here and are exercised manually on real data.
/// </summary>
[TestFixture]
public class StarReviewTests {

    // ---- Label JSON shape MUST match T6's --labels reader EXACTLY ------------------------------------------

    // POCOs carrying the SAME [JsonProperty] names/casing as T6's private loader (OptimizationDiagnosticRunner:
    // RunLabelFile / LabelPosition / LabelPoint). Each label is now a BOX (x,y,w,h). The round-trip test below
    // deserializes a T7-written file THROUGH these — if T7 ever drifts from T6's shape, this test fails. This is the
    // contract that keeps `review` -> `optimize --labels` working.
    private sealed class T6LabelPoint {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("w")] public double? W { get; set; }
        [JsonProperty("h")] public double? H { get; set; }
    }

    private sealed class T6LabelPosition {
        [JsonProperty("focuserPosition")] public int FocuserPosition { get; set; }
        [JsonProperty("radiusPx")] public double? RadiusPx { get; set; }
        [JsonProperty("missed")] public List<T6LabelPoint> Missed { get; set; }
        [JsonProperty("shouldReject")] public List<T6LabelPoint> ShouldReject { get; set; }
        [JsonProperty("wronglyRejected")] public List<T6LabelPoint> WronglyRejected { get; set; }
    }

    private sealed class T6RunLabelFile {
        [JsonProperty("runId")] public string RunId { get; set; }
        [JsonProperty("radiusPx")] public double? RadiusPx { get; set; }
        [JsonProperty("positions")] public List<T6LabelPosition> Positions { get; set; }
    }

    [Test]
    public void WrittenJson_IsReadByT6LoaderShape_RoundTrip() {
        var labels = new StarReviewRunLabels {
            RunId = "attempt01",
            RadiusPx = 6.0,
            Positions = new List<StarReviewPositionLabels> {
                new StarReviewPositionLabels {
                    FocuserPosition = 5000,
                    RadiusPx = 6.0,
                    Missed = new List<StarReviewLabelBox> { new StarReviewLabelBox(123.4, 567.8, 12.0, 14.0) },
                    ShouldReject = new List<StarReviewLabelBox> { new StarReviewLabelBox(12.0, 34.0, 9.0, 9.0) },
                    WronglyRejected = new List<StarReviewLabelBox> { new StarReviewLabelBox(88.0, 90.0, 10.0, 8.0) }
                }
            }
        };

        var json = StarReviewLabelStore.Serialize(labels);

        // Deserialize through T6's exact attribute shape (this is what T6's loader does).
        var t6 = JsonConvert.DeserializeObject<T6RunLabelFile>(json);

        Assert.Multiple(() => {
            Assert.That(t6.RunId, Is.EqualTo("attempt01"));
            Assert.That(t6.RadiusPx, Is.EqualTo(6.0));
            Assert.That(t6.Positions, Has.Count.EqualTo(1));
            var p = t6.Positions[0];
            Assert.That(p.FocuserPosition, Is.EqualTo(5000));
            Assert.That(p.RadiusPx, Is.EqualTo(6.0));
            Assert.That(p.Missed, Has.Count.EqualTo(1));
            Assert.That(p.Missed[0].X, Is.EqualTo(123.4));
            Assert.That(p.Missed[0].Y, Is.EqualTo(567.8));
            Assert.That(p.Missed[0].W, Is.EqualTo(12.0));
            Assert.That(p.Missed[0].H, Is.EqualTo(14.0));
            Assert.That(p.ShouldReject, Has.Count.EqualTo(1));
            Assert.That(p.ShouldReject[0].X, Is.EqualTo(12.0));
            Assert.That(p.ShouldReject[0].Y, Is.EqualTo(34.0));
            Assert.That(p.ShouldReject[0].W, Is.EqualTo(9.0));
            Assert.That(p.ShouldReject[0].H, Is.EqualTo(9.0));
            Assert.That(p.WronglyRejected, Has.Count.EqualTo(1));
            Assert.That(p.WronglyRejected[0].X, Is.EqualTo(88.0));
            Assert.That(p.WronglyRejected[0].Y, Is.EqualTo(90.0));
            Assert.That(p.WronglyRejected[0].W, Is.EqualTo(10.0));
            Assert.That(p.WronglyRejected[0].H, Is.EqualTo(8.0));
        });
    }

    [Test]
    public void WrittenJson_UsesExactT6PropertyNames() {
        var labels = new StarReviewRunLabels {
            RunId = "r",
            RadiusPx = 6.0,
            Positions = new List<StarReviewPositionLabels> {
                new StarReviewPositionLabels {
                    FocuserPosition = 100,
                    Missed = new List<StarReviewLabelBox> { new StarReviewLabelBox(1, 2, 3, 4) },
                    ShouldReject = new List<StarReviewLabelBox> { new StarReviewLabelBox(3, 4, 5, 6) },
                    WronglyRejected = new List<StarReviewLabelBox> { new StarReviewLabelBox(5, 6, 7, 8) }
                }
            }
        };
        var json = StarReviewLabelStore.Serialize(labels);
        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"runId\""));
            Assert.That(json, Does.Contain("\"radiusPx\""));
            Assert.That(json, Does.Contain("\"positions\""));
            Assert.That(json, Does.Contain("\"focuserPosition\""));
            Assert.That(json, Does.Contain("\"missed\""));
            Assert.That(json, Does.Contain("\"shouldReject\""));
            Assert.That(json, Does.Contain("\"wronglyRejected\""));
            Assert.That(json, Does.Contain("\"x\""));
            Assert.That(json, Does.Contain("\"y\""));
            Assert.That(json, Does.Contain("\"w\""));
            Assert.That(json, Does.Contain("\"h\""));
        });
    }

    [Test]
    public void Load_LegacyPointOnlyFile_BackfillsDefaultBox() {
        // An older point-only label file (x,y, no w/h). Loading must widen each point to a 2·radiusPx box centered on
        // the point (so old files keep working under the new box-containment scoring).
        var dir = Path.Combine(Path.GetTempPath(), "hf-review-test-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(dir);
            var legacyJson =
                "{ \"runId\": \"legacy\", \"radiusPx\": 5.0, \"positions\": [ " +
                "{ \"focuserPosition\": 5000, \"missed\": [ { \"x\": 100.0, \"y\": 200.0 } ] } ] }";
            File.WriteAllText(Path.Combine(dir, "legacy.json"), legacyJson);

            var loaded = StarReviewLabelStore.Load(dir, "legacy", out var err);
            Assert.That(err, Is.Null);
            Assert.That(loaded.Positions, Has.Count.EqualTo(1));
            var b = loaded.Positions[0].Missed[0];
            Assert.Multiple(() => {
                Assert.That(b.HasSize, Is.True, "legacy point widened to a real box");
                Assert.That(b.W.Value, Is.EqualTo(10.0).Within(1e-12), "side = 2*radiusPx");
                Assert.That(b.H.Value, Is.EqualTo(10.0).Within(1e-12));
                Assert.That(b.X, Is.EqualTo(95.0).Within(1e-12), "centered on the original point => top-left = x - side/2");
                Assert.That(b.Y, Is.EqualTo(195.0).Within(1e-12));
                Assert.That(b.CenterX, Is.EqualTo(100.0).Within(1e-12));
                Assert.That(b.CenterY, Is.EqualTo(200.0).Within(1e-12));
            });
        } finally {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, true);
            }
        }
    }

    [Test]
    public void SaveThenLoad_MergesIncrementally() {
        var dir = Path.Combine(Path.GetTempPath(), "hf-review-test-" + Guid.NewGuid().ToString("N"));
        try {
            var labels = new StarReviewRunLabels { RunId = "attempt01", RadiusPx = 6.0 };
            var pos = StarReviewLabelStore.GetOrAddPosition(labels, 5000);
            StarReviewLabelStore.ToggleBox(pos.Missed, 10, 20, 8, 8);
            StarReviewLabelStore.Save(dir, labels);

            // Re-load (a fresh review session), add more, save, reload — prior labels must persist.
            var reloaded = StarReviewLabelStore.Load(dir, "attempt01", out var err);
            Assert.That(err, Is.Null);
            Assert.That(reloaded.Positions, Has.Count.EqualTo(1));
            Assert.That(reloaded.Positions[0].Missed, Has.Count.EqualTo(1));

            var pos2 = StarReviewLabelStore.GetOrAddPosition(reloaded, 5000);
            StarReviewLabelStore.ToggleBox(pos2.ShouldReject, 30, 40, 8, 8);
            StarReviewLabelStore.Save(dir, reloaded);

            var final = StarReviewLabelStore.Load(dir, "attempt01", out _);
            Assert.That(final.Positions[0].Missed, Has.Count.EqualTo(1));
            Assert.That(final.Positions[0].ShouldReject, Has.Count.EqualTo(1));
        } finally {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, true);
            }
        }
    }

    [Test]
    public void Load_MissingDir_ReturnsEmptyDefault() {
        var labels = StarReviewLabelStore.Load(@"X:\does\not\exist", "runX", out var err);
        Assert.Multiple(() => {
            Assert.That(err, Is.Null);
            Assert.That(labels.RunId, Is.EqualTo("runX"));
            Assert.That(labels.Positions, Is.Empty);
            Assert.That(labels.RadiusPx, Is.EqualTo(StarReviewLabelStore.DefaultRadiusPx));
        });
    }

    // ---- Toggle / merge -----------------------------------------------------------------------------------

    [Test]
    public void ToggleBox_AddsThenRemovesNearbyBox() {
        var boxes = new List<StarReviewLabelBox>();
        var added = StarReviewLabelStore.ToggleBox(boxes, 100, 100, 8, 8); // center (104,104)
        Assert.That(added, Is.True);
        Assert.That(boxes, Has.Count.EqualTo(1));

        // A second toggle whose CENTER is within tolerance of the existing box's center removes it (undo).
        var removed = StarReviewLabelStore.ToggleBox(boxes, 101, 101, 8, 8); // center (105,105) ~ within tol of (104,104)
        Assert.That(removed, Is.False);
        Assert.That(boxes, Is.Empty);
    }

    [Test]
    public void ToggleBox_FarClickAddsSecondBox() {
        var boxes = new List<StarReviewLabelBox>();
        StarReviewLabelStore.ToggleBox(boxes, 100, 100, 8, 8);
        StarReviewLabelStore.ToggleBox(boxes, 500, 500, 8, 8);
        Assert.That(boxes, Has.Count.EqualTo(2));
    }

    [Test]
    public void SmallestContainingBoxIndex_PrefersTightestBox() {
        var boxes = new List<StarReviewLabelBox> {
            new StarReviewLabelBox(0, 0, 100, 100),   // big box covering the click
            new StarReviewLabelBox(40, 40, 20, 20)    // tight box also covering it (smaller area)
        };
        var idx = StarReviewLabelStore.SmallestContainingBoxIndex(boxes, 50, 50);
        Assert.That(idx, Is.EqualTo(1));
        Assert.That(StarReviewLabelStore.SmallestContainingBoxIndex(boxes, 5000, 5000), Is.EqualTo(-1));
    }

    [Test]
    public void PruneEmptyPositions_DropsEmptyButKeepsLabeled() {
        var labels = new StarReviewRunLabels { RunId = "r" };
        var empty = StarReviewLabelStore.GetOrAddPosition(labels, 100);
        var kept = StarReviewLabelStore.GetOrAddPosition(labels, 200);
        StarReviewLabelStore.ToggleBox(kept.Missed, 1, 1, 4, 4);

        StarReviewLabelStore.PruneEmptyPositions(labels);
        Assert.That(labels.Positions, Has.Count.EqualTo(1));
        Assert.That(labels.Positions[0].FocuserPosition, Is.EqualTo(200));
    }

    [Test]
    public void PruneEmptyPositions_KeepsWronglyRejectedOnlyPosition() {
        var labels = new StarReviewRunLabels { RunId = "r" };
        var empty = StarReviewLabelStore.GetOrAddPosition(labels, 100);
        var wronglyOnly = StarReviewLabelStore.GetOrAddPosition(labels, 200);
        StarReviewLabelStore.ToggleBox(wronglyOnly.WronglyRejected, 7, 8, 4, 4);

        StarReviewLabelStore.PruneEmptyPositions(labels);
        Assert.That(labels.Positions, Has.Count.EqualTo(1));
        Assert.That(labels.Positions[0].FocuserPosition, Is.EqualTo(200));
        Assert.That(labels.Positions[0].WronglyRejected, Has.Count.EqualTo(1));
    }

    [Test]
    public void Counts_IncludesWronglyRejected() {
        var labels = new StarReviewRunLabels { RunId = "r" };
        var pos = StarReviewLabelStore.GetOrAddPosition(labels, 5000);
        StarReviewLabelStore.ToggleBox(pos.Missed, 1, 1, 4, 4);
        StarReviewLabelStore.ToggleBox(pos.Missed, 50, 50, 4, 4);
        StarReviewLabelStore.ToggleBox(pos.ShouldReject, 100, 100, 4, 4);
        StarReviewLabelStore.ToggleBox(pos.WronglyRejected, 200, 200, 4, 4);
        StarReviewLabelStore.ToggleBox(pos.WronglyRejected, 300, 300, 4, 4);
        StarReviewLabelStore.ToggleBox(pos.WronglyRejected, 400, 400, 4, 4);

        var (missed, shouldReject, wronglyRejected) = StarReviewLabelStore.Counts(labels);
        Assert.Multiple(() => {
            Assert.That(missed, Is.EqualTo(2));
            Assert.That(shouldReject, Is.EqualTo(1));
            Assert.That(wronglyRejected, Is.EqualTo(3));
        });
    }

    [Test]
    public void WronglyRejectedRoundTrip_ThroughLabelStore_Persists() {
        var dir = Path.Combine(Path.GetTempPath(), "hf-review-test-" + Guid.NewGuid().ToString("N"));
        try {
            var labels = new StarReviewRunLabels { RunId = "attempt01", RadiusPx = 6.0 };
            var pos = StarReviewLabelStore.GetOrAddPosition(labels, 5000);
            StarReviewLabelStore.ToggleBox(pos.WronglyRejected, 88, 90, 10, 8);
            StarReviewLabelStore.Save(dir, labels);

            var reloaded = StarReviewLabelStore.Load(dir, "attempt01", out var err);
            Assert.That(err, Is.Null);
            Assert.That(reloaded.Positions, Has.Count.EqualTo(1));
            Assert.That(reloaded.Positions[0].WronglyRejected, Has.Count.EqualTo(1));
            var b = reloaded.Positions[0].WronglyRejected[0];
            Assert.Multiple(() => {
                Assert.That(b.X, Is.EqualTo(88.0));
                Assert.That(b.Y, Is.EqualTo(90.0));
                Assert.That(b.W.Value, Is.EqualTo(10.0));
                Assert.That(b.H.Value, Is.EqualTo(8.0));
            });
        } finally {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, true);
            }
        }
    }

    // ---- Review queue -------------------------------------------------------------------------------------

    private static List<StarReviewFrame> Frames(params (string run, int pos, int accepted)[] specs) {
        return specs.Select(s => new StarReviewFrame {
            RunId = s.run, FocuserPosition = s.pos, FramePath = $"{s.run}_{s.pos}", AcceptedCount = s.accepted
        }).ToList();
    }

    [Test]
    public void Queue_Low_SelectsBelowNReview() {
        var frames = Frames(("a", 100, 3), ("a", 200, 8), ("a", 300, 20), ("a", 400, 7));
        StarReviewQueue.MarkExtremes(frames);
        var q = StarReviewQueue.Build(frames, StarReviewMode.Low);
        // < 8: 3 and 7 => positions 100 and 400.
        Assert.That(q.Select(f => f.FocuserPosition), Is.EquivalentTo(new[] { 100, 400 }));
    }

    [Test]
    public void Queue_All_SelectsEveryFrame() {
        var frames = Frames(("a", 100, 3), ("a", 200, 50), ("b", 300, 50));
        StarReviewQueue.MarkExtremes(frames);
        var q = StarReviewQueue.Build(frames, StarReviewMode.All);
        Assert.That(q, Has.Count.EqualTo(3));
    }

    [Test]
    public void Queue_Uncertain_AddsDefocusedExtremesToLowSet() {
        // All star-rich (>= NReview) so Low is empty; Uncertain must still pick each run's min/max position.
        var frames = Frames(("a", 100, 50), ("a", 200, 50), ("a", 300, 50));
        StarReviewQueue.MarkExtremes(frames);
        var low = StarReviewQueue.Build(frames, StarReviewMode.Low);
        var uncertain = StarReviewQueue.Build(frames, StarReviewMode.Uncertain);
        Assert.Multiple(() => {
            Assert.That(low, Is.Empty);
            Assert.That(uncertain.Select(f => f.FocuserPosition), Is.EquivalentTo(new[] { 100, 300 }));
        });
    }

    [Test]
    public void Queue_OrderedByRunThenPosition() {
        var frames = Frames(("b", 100, 1), ("a", 300, 1), ("a", 100, 1));
        StarReviewQueue.MarkExtremes(frames);
        var q = StarReviewQueue.Build(frames, StarReviewMode.All);
        Assert.That(q.Select(f => (f.RunId, f.FocuserPosition)),
            Is.EqualTo(new[] { ("a", 100), ("a", 300), ("b", 100) }).AsCollection);
    }

    [Test]
    public void ParseMode_DefaultsToLow_AndRejectsGarbage() {
        Assert.Multiple(() => {
            Assert.That(StarReviewQueue.ParseMode(null), Is.EqualTo(StarReviewMode.Low));
            Assert.That(StarReviewQueue.ParseMode("uncertain"), Is.EqualTo(StarReviewMode.Uncertain));
            Assert.That(StarReviewQueue.ParseMode("ALL"), Is.EqualTo(StarReviewMode.All));
            Assert.Throws<ArgumentException>(() => StarReviewQueue.ParseMode("bogus"));
        });
    }

    // ---- Viewport (screen <-> image with zoom/pan) --------------------------------------------------------

    [Test]
    public void Viewport_ScreenImageRoundTrip() {
        var vp = new StarReviewViewport(2.5, 30, -15);
        var (sx, sy) = vp.ImageToScreen(100, 200);
        var (ix, iy) = vp.ScreenToImage(sx, sy);
        Assert.Multiple(() => {
            Assert.That(ix, Is.EqualTo(100).Within(1e-9));
            Assert.That(iy, Is.EqualTo(200).Within(1e-9));
        });
    }

    [Test]
    public void Viewport_ScreenToImage_AppliesScaleAndOffset() {
        var vp = new StarReviewViewport(2.0, 10, 20);
        // image (5, 7) -> screen (5*2+10, 7*2+20) = (20, 34).
        var (ix, iy) = vp.ScreenToImage(20, 34);
        Assert.Multiple(() => {
            Assert.That(ix, Is.EqualTo(5).Within(1e-9));
            Assert.That(iy, Is.EqualTo(7).Within(1e-9));
        });
    }

    [Test]
    public void Viewport_ZoomAt_KeepsAnchorPixelFixed() {
        var vp = new StarReviewViewport(1.0, 0, 0);
        // The pixel currently under screen (300, 400) must remain under (300, 400) after zooming in.
        var (anchorImgX, anchorImgY) = vp.ScreenToImage(300, 400);
        vp.ZoomAt(3.0, 300, 400);
        var (sx, sy) = vp.ImageToScreen(anchorImgX, anchorImgY);
        Assert.Multiple(() => {
            Assert.That(vp.Scale, Is.EqualTo(3.0).Within(1e-9));
            Assert.That(sx, Is.EqualTo(300).Within(1e-6));
            Assert.That(sy, Is.EqualTo(400).Within(1e-6));
        });
    }

    [Test]
    public void Viewport_FitTo_CentersAndScalesDown() {
        var vp = new StarReviewViewport();
        // 1000x500 image into an 800x800 viewport: fit = min(0.8, 1.6) = 0.8; image drawn 800x400, centered.
        vp.FitTo(800, 800, 1000, 500);
        Assert.Multiple(() => {
            Assert.That(vp.Scale, Is.EqualTo(0.8).Within(1e-9));
            Assert.That(vp.OffsetX, Is.EqualTo(0).Within(1e-9));     // 800 - 1000*0.8 = 0
            Assert.That(vp.OffsetY, Is.EqualTo(200).Within(1e-9));   // (800 - 500*0.8)/2 = 200
        });
    }

    [Test]
    public void Viewport_ClampsScaleToBounds() {
        var vp = new StarReviewViewport();
        vp.Set(1000.0, 0, 0);
        Assert.That(vp.Scale, Is.EqualTo(StarReviewViewport.MaxScale));
        vp.Set(0.0001, 0, 0);
        Assert.That(vp.Scale, Is.EqualTo(StarReviewViewport.MinScale));
    }

    [Test]
    public void PanBy_TranslatesOffset() {
        var vp = new StarReviewViewport(1.0, 10, 10);
        vp.PanBy(5, -3);
        Assert.Multiple(() => {
            Assert.That(vp.OffsetX, Is.EqualTo(15));
            Assert.That(vp.OffsetY, Is.EqualTo(7));
        });
    }

    // ---- Combined hit-test + categorize (mode-less click routing) -----------------------------------------
    //
    // The view (StarReviewControl) decides click-vs-drag and the VM infers the label category from the SAME pure
    // helper StarReviewVM.HitTestCandidate(accepted, rejected, x, y). These tests pin its semantics directly: an
    // accepted hit categorizes as a should-reject (false positive), a rejected hit as a wrongly-rejected (recall),
    // overlap resolves to the smallest-area box, a click outside everything is None, and an equal-area tie prefers
    // the accepted box.

    [Test]
    public void HitTestCandidate_AcceptedHit_ReturnsAccepted() {
        var accepted = new[] { new Rect(0, 0, 20, 20) };
        var rejected = new[] { new Rect(100, 100, 20, 20) };
        var (cat, box) = StarReviewVM.HitTestCandidate(accepted, rejected, 10, 10);
        Assert.Multiple(() => {
            Assert.That(cat, Is.EqualTo(HitCategory.Accepted));
            Assert.That(box, Is.EqualTo(new Rect(0, 0, 20, 20)));
        });
    }

    [Test]
    public void HitTestCandidate_RejectedHit_ReturnsRejected() {
        var accepted = new[] { new Rect(0, 0, 20, 20) };
        var rejected = new[] { new Rect(100, 100, 20, 20) };
        var (cat, box) = StarReviewVM.HitTestCandidate(accepted, rejected, 110, 110);
        Assert.Multiple(() => {
            Assert.That(cat, Is.EqualTo(HitCategory.Rejected));
            Assert.That(box, Is.EqualTo(new Rect(100, 100, 20, 20)));
        });
    }

    [Test]
    public void HitTestCandidate_ClickOutsideAllBoxes_ReturnsNone() {
        var accepted = new[] { new Rect(0, 0, 20, 20) };
        var rejected = new[] { new Rect(100, 100, 20, 20) };
        var (cat, _) = StarReviewVM.HitTestCandidate(accepted, rejected, 500, 500);
        Assert.That(cat, Is.EqualTo(HitCategory.None));
    }

    [Test]
    public void HitTestCandidate_Overlap_SmallestAreaWinsAcrossSets() {
        // A big ACCEPTED box and a tight REJECTED box both contain the click. The smaller area wins regardless of
        // which set it came from, so the category follows the tightest candidate (here: Rejected).
        var accepted = new[] { new Rect(0, 0, 100, 100) };       // area 10000
        var rejected = new[] { new Rect(40, 40, 10, 10) };       // area 100, contains (45,45)
        var (cat, box) = StarReviewVM.HitTestCandidate(accepted, rejected, 45, 45);
        Assert.Multiple(() => {
            Assert.That(cat, Is.EqualTo(HitCategory.Rejected));
            Assert.That(box, Is.EqualTo(new Rect(40, 40, 10, 10)));
        });
    }

    [Test]
    public void HitTestCandidate_Overlap_SmallestAcceptedBeatsBiggerRejected() {
        var accepted = new[] { new Rect(40, 40, 10, 10) };       // area 100 (tight)
        var rejected = new[] { new Rect(0, 0, 100, 100) };       // area 10000 (big)
        var (cat, box) = StarReviewVM.HitTestCandidate(accepted, rejected, 45, 45);
        Assert.Multiple(() => {
            Assert.That(cat, Is.EqualTo(HitCategory.Accepted));
            Assert.That(box, Is.EqualTo(new Rect(40, 40, 10, 10)));
        });
    }

    [Test]
    public void HitTestCandidate_EqualAreaTie_PrefersAccepted() {
        // Identical boxes (same area) overlapping the click: the accepted set is scanned first and a rejected box
        // only wins on STRICTLY smaller area, so an equal-area tie resolves to Accepted (the should-reject path).
        var accepted = new[] { new Rect(0, 0, 20, 20) };
        var rejected = new[] { new Rect(0, 0, 20, 20) };
        var (cat, _) = StarReviewVM.HitTestCandidate(accepted, rejected, 10, 10);
        Assert.That(cat, Is.EqualTo(HitCategory.Accepted));
    }

    [Test]
    public void HitTestCandidate_NullCollections_AreNone() {
        var (cat, _) = StarReviewVM.HitTestCandidate(null, null, 10, 10);
        Assert.That(cat, Is.EqualTo(HitCategory.None));
    }
}
