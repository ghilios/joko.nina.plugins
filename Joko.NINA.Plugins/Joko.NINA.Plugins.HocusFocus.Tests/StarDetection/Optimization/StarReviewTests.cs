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
using NINA.Joko.Plugins.HocusFocus.Interfaces;
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

    // ---- Cleaner label filenames (starry-hopper item 16) ------------------------------------------------

    [Test]
    public void FileNameFor_PathLikeRunId_UsesLastTwoSegments() {
        Assert.Multiple(() => {
            Assert.That(StarReviewLabelStore.FileNameFor(@"E:\WorkshopData\Data\autofocus\sensitivity_example1\attempt01"),
                Is.EqualTo("sensitivity_example1_attempt01.json"));
            Assert.That(StarReviewLabelStore.FileNameFor("/home/me/run5/attempt02"),
                Is.EqualTo("run5_attempt02.json"));
        });
    }

    [Test]
    public void FileNameFor_SingleSegment_IsUnchanged() {
        // Back-compat: a bare runId (as the unit tests and the offline discovery use) keeps its simple name.
        Assert.That(StarReviewLabelStore.FileNameFor("attempt01"), Is.EqualTo("attempt01.json"));
    }

    [Test]
    public void FileNameFor_EmptyOrInvalid_FallsBackAndSanitizes() {
        Assert.Multiple(() => {
            Assert.That(StarReviewLabelStore.FileNameFor(null), Is.EqualTo("run.json"));
            Assert.That(StarReviewLabelStore.FileNameFor("   "), Is.EqualTo("run.json"));
            // Invalid filename characters in the derived stem are sanitized away (ordinal char scan — Does.Contain
            // is culture-aware and mis-handles control chars).
            var name = StarReviewLabelStore.FileNameFor(@"C:\a:b\c*d");
            Assert.That(name, Does.EndWith(".json"));
            Assert.That(name.IndexOfAny(Path.GetInvalidFileNameChars()), Is.LessThan(0),
                "no invalid filename characters survive sanitization");
        });
    }

    [Test]
    public void SaveThenLoad_PathLikeRunId_RoundTripsViaCleanFilename() {
        var dir = Path.Combine(Path.GetTempPath(), "hf-review-test-" + Guid.NewGuid().ToString("N"));
        try {
            var runId = @"E:\WorkshopData\sensitivity_example1\attempt01";
            var labels = new StarReviewRunLabels { RunId = runId, RadiusPx = 6.0 };
            var pos = StarReviewLabelStore.GetOrAddPosition(labels, 5000);
            StarReviewLabelStore.ToggleBox(pos.Missed, 10, 20, 8, 8);

            var savedPath = StarReviewLabelStore.Save(dir, labels);
            Assert.That(Path.GetFileName(savedPath), Is.EqualTo("sensitivity_example1_attempt01.json"),
                "the on-disk file uses the clean run-derived name");

            var reloaded = StarReviewLabelStore.Load(dir, runId, out var err);
            Assert.Multiple(() => {
                Assert.That(err, Is.Null);
                Assert.That(reloaded.Positions, Has.Count.EqualTo(1), "the clean filename round-trips through Load");
                Assert.That(reloaded.Positions[0].Missed, Has.Count.EqualTo(1));
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

    // ---- Accepted-star overlay: centroid crosshair hidden by default; the box stays the detector's bounds -----

    [Test]
    public void AcceptedOverlay_CentroidCrosshairsHiddenByDefault_BoxStillRendered() {
        // The centroid crosshair is a dev-only overlay (ShowCentroidMarkers, off by default) so the labeler isn't
        // cluttered. With it off, no centroid markers are populated, while the accepted box is still drawn at the
        // detector's StarBoundingBox (top-left + size).
        var box = new Rect(100, 100, 20, 20);
        var review = new FrameReview {
            RunId = "run1",
            FocuserPosition = 5000,
            FramePath = "frame.fits",
            ImageProvider = null, // no UI/image needed: markers populate synchronously in the ctor's LoadCurrent
            Accepted = new List<(double, double, double, Rect)> { (103.0, 117.0, 2.34, box) },
        };
        var labelsByRun = new Dictionary<string, StarReviewRunLabels> {
            { "run1", new StarReviewRunLabels { RunId = "run1" } }
        };

        var vm = new StarReviewVM(new[] { review }, labelsByRun, labelsDir: string.Empty);

        Assert.Multiple(() => {
            // Crosshair overlay hidden by default → no centroid markers.
            Assert.That(vm.CentroidMarkers, Is.Empty);
            // The accepted box is still the detector's StarBoundingBox (top-left + size), not recentered.
            Assert.That(vm.AcceptedMarkers, Has.Count.EqualTo(1));
            Assert.That(vm.AcceptedMarkers[0].X, Is.EqualTo(box.X));
            Assert.That(vm.AcceptedMarkers[0].Y, Is.EqualTo(box.Y));
            Assert.That(vm.AcceptedMarkers[0].Width, Is.EqualTo(box.Width));
            Assert.That(vm.AcceptedMarkers[0].Height, Is.EqualTo(box.Height));
        });
    }

    // The reported "box is off" bug: the green ACCEPTED box renders OFFSET from its star. The box Rectangle lives
    // in a Grid alongside the (wider) HFR TextBlock; with no explicit alignment the Rectangle defaults to Stretch,
    // which WPF resolves to CENTER for a fixed-size child — so when the HFR label is larger than a small box, the
    // Rectangle is displaced from the box's true top-left. Detection is fine (the accepted-star centering gate
    // guarantees Center is inside StarBoundingBox); this is purely a XAML layout bug. With no image loaded the
    // viewport stays Scale=1/Offset=0 (identity overlay transform), so the rendered Rectangle top-left, in
    // ViewportCanvas coords, must equal the box's image coords.
    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void AcceptedBox_RendersAtBoundingBoxOrigin_NotDisplacedByHfrLabel() {
        var box = new Rect(100, 100, 10, 10); // small box, smaller than the "3.56" HFR label's layout size
        var review = new FrameReview {
            RunId = "run1", FocuserPosition = 5000, FramePath = "f.fits",
            ImageProvider = null, // no image: viewport stays identity (Scale=1, Offset=0)
            Accepted = new List<(double, double, double, Rect)> { (104.0, 108.0, 3.56, box) },
        };
        var labelsByRun = new Dictionary<string, StarReviewRunLabels> { { "run1", new StarReviewRunLabels { RunId = "run1" } } };
        var vm = new StarReviewVM(new[] { review }, labelsByRun, string.Empty);

        var control = new StarReviewControl { DataContext = vm };
        control.Measure(new System.Windows.Size(1000, 800));
        control.Arrange(new System.Windows.Rect(0, 0, 1000, 800));
        control.UpdateLayout();

        var viewportCanvas = VisualDescendants<System.Windows.Controls.Canvas>(control)
            .FirstOrDefault(c => c.Name == "ViewportCanvas");
        Assert.That(viewportCanvas, Is.Not.Null, "ViewportCanvas not found in the visual tree");

        // The only 10x10 Rectangle is the accepted box (rejected/should-reject/missed lists are empty; legend
        // swatches are 20x12; the rubber-band DragRect is collapsed/zero-size).
        var rect = VisualDescendants<System.Windows.Shapes.Rectangle>(viewportCanvas)
            .FirstOrDefault(r => Math.Abs(r.Width - box.Width) < 0.01 && Math.Abs(r.Height - box.Height) < 0.01);
        Assert.That(rect, Is.Not.Null, "accepted box Rectangle not realized in the overlay");

        var topLeft = rect.TransformToAncestor(viewportCanvas).Transform(new System.Windows.Point(0, 0));
        Assert.Multiple(() => {
            Assert.That(topLeft.X, Is.EqualTo(box.X).Within(0.5),
                "accepted box X displaced from StarBoundingBox.X by the HFR-label Grid layout");
            Assert.That(topLeft.Y, Is.EqualTo(box.Y).Within(0.5),
                "accepted box Y displaced from StarBoundingBox.Y by the HFR-label Grid layout");
        });
    }

    private static IEnumerable<T> VisualDescendants<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject {
        if (root == null) {
            yield break;
        }
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++) {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) {
                yield return match;
            }
            foreach (var d in VisualDescendants<T>(child)) {
                yield return d;
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
    public void Viewport_WheelZoomOut_StopsAtStartingFit() {
        var vp = new StarReviewViewport();
        // 1000x500 image into 800x800: fit = 0.8, which becomes the zoom-out floor.
        vp.FitTo(800, 800, 1000, 500);
        Assert.That(vp.ZoomOutFloor, Is.EqualTo(0.8).Within(1e-9));

        // Wheel zoom-out far past the fit must clamp AT the fit (so the image never shrinks inside the viewport,
        // which would show black on all four edges).
        vp.ZoomAt(0.001, 400, 400);
        Assert.That(vp.Scale, Is.EqualTo(0.8).Within(1e-9), "zoom-out is clamped to the starting fit");

        // Zooming back in still works normally.
        vp.ZoomAt(10.0, 400, 400);
        Assert.That(vp.Scale, Is.EqualTo(8.0).Within(1e-9));
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

    // ---- Bounded pan (starry-hopper item 11) ------------------------------------------------------------

    [Test]
    public void ClampToBounds_LargerThanViewport_KeepsImageCoveringViewport() {
        // image 200x200 at scale 1 => content 200 > viewport 100 => offset must stay in [-100, 0].
        var vp = new StarReviewViewport(1.0, 0, 0);
        vp.PanBy(500, 500); // over-pan toward positive
        vp.ClampToBounds(100, 100, 200, 200);
        Assert.Multiple(() => {
            Assert.That(vp.OffsetX, Is.EqualTo(0).Within(1e-9));
            Assert.That(vp.OffsetY, Is.EqualTo(0).Within(1e-9));
        });

        vp.PanBy(-1000, -1000); // over-pan toward negative
        vp.ClampToBounds(100, 100, 200, 200);
        Assert.Multiple(() => {
            Assert.That(vp.OffsetX, Is.EqualTo(-100).Within(1e-9));
            Assert.That(vp.OffsetY, Is.EqualTo(-100).Within(1e-9));
        });
    }

    [Test]
    public void ClampToBounds_SmallerThanViewport_CentersAxis() {
        // content 100 < viewport 200 => the axis is centered at (200-100)/2 = 50, regardless of prior offset.
        var vp = new StarReviewViewport(1.0, 999, -999);
        vp.ClampToBounds(200, 200, 100, 100);
        Assert.Multiple(() => {
            Assert.That(vp.OffsetX, Is.EqualTo(50).Within(1e-9));
            Assert.That(vp.OffsetY, Is.EqualTo(50).Within(1e-9));
        });
    }

    [Test]
    public void ClampToBounds_NonPositiveDims_NoOp() {
        var vp = new StarReviewViewport(1.0, 33, 44);
        vp.ClampToBounds(0, 0, 100, 100);
        Assert.Multiple(() => {
            Assert.That(vp.OffsetX, Is.EqualTo(33).Within(1e-9));
            Assert.That(vp.OffsetY, Is.EqualTo(44).Within(1e-9));
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

    // ---- Shared FrameReviewBuilder.ExtractRejected (T8) ---------------------------------------------------
    //
    // The plugin-side, shared rejected-extraction the wizard AND the TestApp `review`/`diagnose-labels` tools all
    // consume (single source of truth). Pure: it flattens the per-reason rejection bounds into (reason, rect) pairs
    // in a FIXED reason order so the overlay colors line up across tools.

    [Test]
    public void ExtractRejected_NullMetrics_ReturnsEmpty() {
        Assert.That(FrameReviewBuilder.ExtractRejected(null), Is.Empty);
        Assert.That(FrameReviewBuilder.ExtractRejected(new HocusFocusStarDetectorResult { Metrics = null }), Is.Empty);
    }

    [Test]
    public void ExtractRejected_FlattensEachReasonInOrder() {
        var metrics = new StarDetectorMetrics();
        // The bounds lists are mutable (the int counts are derived); add one rect per reason.
        metrics.TooDistortedBounds.Add(new Rect(1, 1, 10, 10));
        metrics.DegenerateBounds.Add(new Rect(2, 2, 10, 10));
        metrics.SaturatedBounds.Add(new Rect(3, 3, 10, 10));
        metrics.LowSensitivityBounds.Add(new Rect(4, 4, 10, 10));
        metrics.NotCenteredBounds.Add(new Rect(5, 5, 10, 10));
        metrics.TooFlatBounds.Add(new Rect(6, 6, 10, 10));
        metrics.ContaminatedBounds.Add(new Rect(7, 7, 10, 10));
        var result = new HocusFocusStarDetectorResult { Metrics = metrics };

        var rejected = FrameReviewBuilder.ExtractRejected(result);

        Assert.That(rejected.Select(r => r.Reason),
            Is.EqualTo(new[] { "TooDistorted", "Degenerate", "Saturated", "LowSensitivity", "NotCentered", "TooFlat", "Contaminated" }).AsCollection);
        Assert.That(rejected[4].Bounds, Is.EqualTo(new Rect(5, 5, 10, 10)), "NotCentered carries its own rect");
    }

    [Test]
    public void ExtractRejected_PreservesMultiplePerReason() {
        var metrics = new StarDetectorMetrics();
        metrics.TooDistortedBounds.Add(new Rect(1, 1, 5, 5));
        metrics.TooDistortedBounds.Add(new Rect(2, 2, 5, 5));
        metrics.SaturatedBounds.Add(new Rect(9, 9, 5, 5));
        var result = new HocusFocusStarDetectorResult { Metrics = metrics };

        var rejected = FrameReviewBuilder.ExtractRejected(result);
        Assert.Multiple(() => {
            Assert.That(rejected.Count(r => r.Reason == "TooDistorted"), Is.EqualTo(2));
            Assert.That(rejected.Count(r => r.Reason == "Saturated"), Is.EqualTo(1));
            Assert.That(rejected, Has.Count.EqualTo(3));
        });
    }

    // ---- Legend (starry-hopper item 13) -----------------------------------------------------------------

    [Test]
    public void LegendEntries_CoverAcceptedRejectedAndUserLabels_WithPlainCaptions() {
        var queue = new List<FrameReview> { new FrameReview { RunId = "r", FocuserPosition = 1000 } };
        var vm = new StarReviewVM(queue, new Dictionary<string, StarReviewRunLabels>(StringComparer.Ordinal), string.Empty);

        var legend = vm.LegendEntries;
        Assert.Multiple(() => {
            // 1 accepted + 7 rejection reasons + 3 user-label types.
            Assert.That(legend, Has.Count.EqualTo(11));
            Assert.That(legend.Count(e => e.Dashed), Is.EqualTo(3), "the three user-label types are dashed");
            Assert.That(legend.Any(e => e.Caption == "Accepted star" && !e.Dashed), Is.True);
            Assert.That(legend.All(e => e.Brush != null), Is.True);
            // Plain-language captions — raw gate enum names must not leak through.
            Assert.That(legend.Any(e => e.Caption.Contains("TooDistorted")), Is.False);
            Assert.That(legend.Any(e => e.Caption == "Rejected: Too distorted"), Is.True);
        });
    }

    // ---- Undo/redo + right-click delete (starry-hopper items 9/17) --------------------------------------

    private static FrameReview FrameWithBoxes(string runId = "r", int pos = 1000) {
        var f = new FrameReview { RunId = runId, FocuserPosition = pos };
        f.Accepted.Add((110, 110, 2.0, new Rect(100, 100, 20, 20))); // accepted box
        f.Rejected.Add(("TooDistorted", new Rect(300, 300, 20, 20))); // rejected box
        return f;
    }

    private static StarReviewVM ReviewVM(out Dictionary<string, StarReviewRunLabels> labels, params FrameReview[] frames) {
        labels = new Dictionary<string, StarReviewRunLabels>(StringComparer.Ordinal);
        foreach (var rid in frames.Select(f => f.RunId).Distinct(StringComparer.Ordinal)) {
            labels[rid] = new StarReviewRunLabels { RunId = rid, RadiusPx = 6.0 };
        }
        return new StarReviewVM(frames, labels, string.Empty);
    }

    private static (int missed, int reject, int wrongly) Counts(Dictionary<string, StarReviewRunLabels> labels, string runId, int pos) {
        var p = labels[runId].Positions.FirstOrDefault(x => x.FocuserPosition == pos);
        return (p?.Missed?.Count ?? 0, p?.ShouldReject?.Count ?? 0, p?.WronglyRejected?.Count ?? 0);
    }

    // ---- Move/resize of drawn (missed) boxes ----------------------------------------------------------

    [Test]
    public void HitTestMissedBox_DetectsInsideEdgesAndCorners() {
        var vm = ReviewVM(out _, FrameWithBoxes());
        vm.AddMissedBox(200, 200, 40, 40); // box edges at x=200/240, y=200/240
        Assert.Multiple(() => {
            Assert.That(vm.HitTestMissedBox(220, 220, 5).Handle, Is.EqualTo(BoxHandle.Inside));
            Assert.That(vm.HitTestMissedBox(200, 220, 5).Handle, Is.EqualTo(BoxHandle.Left));
            Assert.That(vm.HitTestMissedBox(220, 240, 5).Handle, Is.EqualTo(BoxHandle.Bottom));
            Assert.That(vm.HitTestMissedBox(200, 200, 5).Handle, Is.EqualTo(BoxHandle.TopLeft));
            Assert.That(vm.HitTestMissedBox(240, 240, 5).Handle, Is.EqualTo(BoxHandle.BottomRight));
            Assert.That(vm.HitTestMissedBox(100, 100, 5).Index, Is.EqualTo(-1), "a point far from every box misses");
        });
    }

    [Test]
    public void MissedBoxEdit_Move_ShiftsBox_AndIsUndoable() {
        var vm = ReviewVM(out var labels, FrameWithBoxes());
        vm.AddMissedBox(200, 200, 40, 40);
        var (idx, handle) = vm.HitTestMissedBox(220, 220, 5); // Inside → move
        vm.BeginMissedBoxEdit(idx, handle, 220, 220);
        vm.UpdateMissedBoxEdit(230, 215); // drag +10, -5
        vm.EndMissedBoxEdit();
        var b = labels["r"].Positions[0].Missed[0];
        Assert.Multiple(() => {
            Assert.That(b.X, Is.EqualTo(210));
            Assert.That(b.Y, Is.EqualTo(195));
            Assert.That(b.W, Is.EqualTo(40));
            Assert.That(b.H, Is.EqualTo(40));
        });

        vm.UndoCommand.Execute(null);
        var u = labels["r"].Positions[0].Missed[0];
        Assert.Multiple(() => {
            Assert.That(u.X, Is.EqualTo(200), "undo restores the original position");
            Assert.That(u.Y, Is.EqualTo(200));
        });
    }

    [Test]
    public void MissedBoxEdit_ResizeBottomRight_KeepsTopLeftFixed() {
        var vm = ReviewVM(out var labels, FrameWithBoxes());
        vm.AddMissedBox(200, 200, 40, 40);
        var (idx, handle) = vm.HitTestMissedBox(240, 240, 5);
        Assert.That(handle, Is.EqualTo(BoxHandle.BottomRight));
        vm.BeginMissedBoxEdit(idx, handle, 240, 240);
        vm.UpdateMissedBoxEdit(260, 250); // drag the BR corner out
        vm.EndMissedBoxEdit();
        var b = labels["r"].Positions[0].Missed[0];
        Assert.Multiple(() => {
            Assert.That(b.X, Is.EqualTo(200), "the opposite (top-left) corner stays put");
            Assert.That(b.Y, Is.EqualTo(200));
            Assert.That(b.W, Is.EqualTo(60));
            Assert.That(b.H, Is.EqualTo(50));
        });
    }

    [Test]
    public void Undo_Redo_MissedBox() {
        var vm = ReviewVM(out var labels, FrameWithBoxes());
        vm.AddMissedBox(500, 500, 20, 20);
        Assert.That(Counts(labels, "r", 1000).missed, Is.EqualTo(1));

        Assert.That(vm.UndoCommand.CanExecute(null), Is.True);
        vm.UndoCommand.Execute(null);
        Assert.That(Counts(labels, "r", 1000).missed, Is.EqualTo(0), "undo removes the added missed box");

        Assert.That(vm.RedoCommand.CanExecute(null), Is.True);
        vm.RedoCommand.Execute(null);
        Assert.That(Counts(labels, "r", 1000).missed, Is.EqualTo(1), "redo re-adds it");
    }

    [Test]
    public void Undo_RestoresToggledLabel() {
        var vm = ReviewVM(out var labels, FrameWithBoxes());
        vm.ToggleLabelAt(110, 110); // inside the accepted box => should-reject
        Assert.That(Counts(labels, "r", 1000).reject, Is.EqualTo(1));
        vm.UndoCommand.Execute(null);
        Assert.That(Counts(labels, "r", 1000).reject, Is.EqualTo(0), "undo removes the should-reject label");
    }

    [Test]
    public void RemoveLabelAt_DeletesLabel_AndUndoRestoresIt() {
        var vm = ReviewVM(out var labels, FrameWithBoxes());
        vm.AddMissedBox(500, 500, 20, 20); // center (510,510)
        Assert.That(Counts(labels, "r", 1000).missed, Is.EqualTo(1));

        var removed = vm.RemoveLabelAt(510, 510);
        Assert.That(removed, Is.True);
        Assert.That(Counts(labels, "r", 1000).missed, Is.EqualTo(0), "right-click removed the label");

        vm.UndoCommand.Execute(null);
        Assert.That(Counts(labels, "r", 1000).missed, Is.EqualTo(1), "undo restores a right-click deletion");
    }

    [Test]
    public void RemoveLabelAt_OutsideEveryLabel_IsNoOp() {
        var vm = ReviewVM(out _, FrameWithBoxes());
        vm.AddMissedBox(500, 500, 20, 20);
        Assert.That(vm.RemoveLabelAt(10, 10), Is.False);
    }

    [Test]
    public void NewEdit_ClearsRedoStack() {
        var vm = ReviewVM(out _, FrameWithBoxes());
        vm.AddMissedBox(500, 500, 20, 20);
        vm.UndoCommand.Execute(null);
        Assert.That(vm.RedoCommand.CanExecute(null), Is.True, "precondition: a redo is available");

        vm.AddMissedBox(600, 600, 20, 20); // a fresh edit forks history
        Assert.That(vm.RedoCommand.CanExecute(null), Is.False, "a new edit clears the redo stack");
    }

    [Test]
    public void Undo_NavigatesToTheEditedFrame() {
        var vm = ReviewVM(out var labels, FrameWithBoxes("r", 1000), FrameWithBoxes("r", 2000));
        // Label on frame 0, move to frame 1, then undo — it must jump back to frame 0 so the change is visible.
        vm.AddMissedBox(500, 500, 20, 20);
        vm.NextCommand.Execute(null);
        Assert.That(vm.CurrentIndex, Is.EqualTo(1));

        vm.UndoCommand.Execute(null);
        Assert.Multiple(() => {
            Assert.That(vm.CurrentIndex, Is.EqualTo(0), "undo navigates back to the edited frame");
            Assert.That(Counts(labels, "r", 1000).missed, Is.EqualTo(0));
        });
    }
}
