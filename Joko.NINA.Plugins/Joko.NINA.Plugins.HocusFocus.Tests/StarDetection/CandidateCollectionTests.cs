#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

/// <summary>
/// Guards the candidate-collection rework:
/// - the run-index walker (production default) must be BIT-IDENTICAL to the legacy zero-the-bbox scan —
///   same candidates, same bounds, same point lists in the same order (eccentricity sums are
///   order-sensitive), on random and adversarial maps alike;
/// - the opt-in 8-connected-component collector must be deterministic, partition exactly the lit pixels,
///   and exhibit its documented behavior differences (no bbox shadowing);
/// - the parallel saturated-pixel count must equal the serial count.
/// </summary>
[TestFixture]
public class CandidateCollectionTests {

    private static Mat MatFromBits(bool[,] lit) {
        int height = lit.GetLength(0);
        int width = lit.GetLength(1);
        var mat = new Mat(height, width, MatType.CV_32F, Scalar.All(0));
        unsafe {
            var p = (float*)mat.DataPointer;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    p[y * width + x] = lit[y, x] ? 1.0f : 0.0f;
                }
            }
        }
        return mat;
    }

    private static List<StarDetector.StarCandidateRegion> RunLegacy(bool[,] lit) {
        using var mat = MatFromBits(lit);
        var detector = new StarDetector(new AlglibAPI());
        return detector.CollectStarCandidatesLegacy(mat, new StarDetectorMetrics(), CancellationToken.None);
    }

    private static List<StarDetector.StarCandidateRegion> RunWalker(bool[,] lit) {
        using var mat = MatFromBits(lit);
        var index = StructureRunIndex.Build(mat, 0.001f);
        return StarDetector.CollectStarCandidatesWalker(index, new StarDetectorMetrics(), CancellationToken.None);
    }

    private static List<StarDetector.StarCandidateRegion> RunConnected(bool[,] lit) {
        using var mat = MatFromBits(lit);
        var index = StructureRunIndex.Build(mat, 0.001f);
        return StarDetector.CollectStarCandidatesConnected(index, new StarDetectorMetrics(), CancellationToken.None);
    }

    private static void AssertIdentical(List<StarDetector.StarCandidateRegion> expected, List<StarDetector.StarCandidateRegion> actual, string context) {
        Assert.That(actual.Count, Is.EqualTo(expected.Count), $"{context}: candidate count");
        for (int i = 0; i < expected.Count; i++) {
            Assert.That(actual[i].Bounds, Is.EqualTo(expected[i].Bounds), $"{context}: bounds of candidate {i}");
            Assert.That(actual[i].Points, Is.EqualTo(expected[i].Points), $"{context}: point list (incl. order) of candidate {i}");
        }
    }

    private static bool[,] RandomMap(int height, int width, double density, int seed, bool smooth) {
        var rng = new Random(seed);
        var lit = new bool[height, width];
        if (!smooth) {
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    lit[y, x] = rng.NextDouble() < density;
                }
            }
            return lit;
        }
        // Blobby variant: sprinkle seeds, then dilate a few times so realistic multi-pixel clusters with
        // concavities/overlapping extents form.
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                lit[y, x] = rng.NextDouble() < density / 6;
            }
        }
        for (int pass = 0; pass < 2; pass++) {
            var next = (bool[,])lit.Clone();
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    if (lit[y, x]) continue;
                    for (int dy = -1; dy <= 1 && !next[y, x]; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            int ny = y + dy, nx = x + dx;
                            if (ny >= 0 && ny < height && nx >= 0 && nx < width && lit[ny, nx] && rng.NextDouble() < 0.6) {
                                next[y, x] = true;
                                break;
                            }
                        }
                    }
                }
            }
            lit = next;
        }
        return lit;
    }

    [Test]
    public void Walker_MatchesLegacy_OnRandomMaps() {
        foreach (var (density, smooth) in new[] { (0.005, false), (0.03, false), (0.15, false), (0.02, true), (0.08, true) }) {
            for (int seed = 1; seed <= 4; seed++) {
                var lit = RandomMap(193, 257, density, seed * 7919, smooth);
                AssertIdentical(RunLegacy(lit), RunWalker(lit), $"density={density} smooth={smooth} seed={seed}");
            }
        }
    }

    [Test]
    public void Walker_MatchesLegacy_OnAdversarialMaps() {
        var cases = new Dictionary<string, bool[,]>();

        // Donut: ring with a hole (the walker's seed-column probe lands in the hole on middle rows).
        var donut = new bool[32, 32];
        for (int y = 0; y < 32; y++) {
            for (int x = 0; x < 32; x++) {
                var r = Math.Sqrt((x - 16.0) * (x - 16.0) + (y - 16.0) * (y - 16.0));
                donut[y, x] = r >= 5 && r <= 9;
            }
        }
        cases["donut"] = donut;

        // Bbox shadowing: B's topmost pixel inside A's bounding box.
        var shadow = new bool[24, 40];
        for (int y = 2; y <= 8; y++) {
            for (int x = 4; x <= 10; x++) shadow[y, x] = true;    // A: 7x7 block
        }
        for (int y = 5; y <= 7; y++) {
            for (int x = 8; x <= 14; x++) shadow[y + 6, x] = true; // B: overlaps A's bbox rows/cols
        }
        cases["shadow"] = shadow;

        // Gap-jump: a row whose seed column is dark but has lit pixels within the bbox width.
        var gap = new bool[16, 24];
        for (int x = 3; x <= 12; x++) gap[3, x] = true;            // wide top row sets the bbox
        gap[4, 10] = true; gap[4, 11] = true;                      // next row: lit only far right of seed col
        cases["gap-jump"] = gap;

        // Edges: blobs touching every border and both extreme corners.
        var edges = new bool[20, 20];
        for (int i = 0; i < 4; i++) {
            edges[0, 6 + i] = true;             // top edge
            edges[19, 3 + i] = true;            // bottom edge (last row never seeds)
            edges[8 + i, 0] = true;             // left edge
            edges[12 + i, 19] = true;           // right edge (last column never seeds)
        }
        edges[0, 0] = true;
        edges[19, 19] = true;
        cases["edges"] = edges;

        // Full-lit map (stress: one enormous candidate + leftovers).
        var full = new bool[12, 12];
        for (int y = 0; y < 12; y++) for (int x = 0; x < 12; x++) full[y, x] = true;
        cases["full"] = full;

        // Vertical/horizontal 1px lines and isolated single pixels.
        var lines = new bool[16, 16];
        for (int y = 2; y <= 12; y++) lines[y, 5] = true;
        for (int x = 8; x <= 14; x++) lines[9, x] = true;
        lines[1, 14] = true;
        cases["lines"] = lines;

        foreach (var (name, lit) in cases) {
            AssertIdentical(RunLegacy(lit), RunWalker(lit), name);
        }
    }

    [Test]
    public void Connected_IsDeterministic_AndPartitionsLitPixels() {
        var lit = RandomMap(150, 200, 0.05, 12345, smooth: true);
        var first = RunConnected(lit);
        var second = RunConnected(lit);
        AssertIdentical(first, second, "determinism");

        // The components must partition the lit pixel set exactly: every lit pixel in exactly one candidate.
        var expected = new HashSet<(int X, int Y)>();
        for (int y = 0; y < 150; y++) {
            for (int x = 0; x < 200; x++) {
                if (lit[y, x]) expected.Add((x, y));
            }
        }
        var seen = new HashSet<(int X, int Y)>();
        foreach (var candidate in first) {
            foreach (var pt in candidate.Points) {
                Assert.That(seen.Add((pt.X, pt.Y)), Is.True, $"pixel ({pt.X},{pt.Y}) appears in two components");
            }
        }
        Assert.That(seen.SetEquals(expected), Is.True, "components must cover exactly the lit pixels");
    }

    [Test]
    public void Connected_SurvivesBboxShadowing_AndUnifiesDonuts() {
        // True shadowing needs a NON-CONVEX first candidate whose bbox exceeds its pixels: a diagonal
        // streak A (bbox rows 2-12 x cols 4-14) with a small disconnected star B at rows 6-7 x cols 13-14,
        // inside A's bbox but far from A's pixels. Legacy/walker zero A's whole bbox and erase B; the
        // component collector must keep both.
        var shadow = new bool[24, 40];
        for (int y = 2; y <= 12; y++) {
            shadow[y, y + 2] = true;
        }
        shadow[6, 13] = shadow[6, 14] = shadow[7, 13] = shadow[7, 14] = true;
        var walker = RunWalker(shadow);
        var connected = RunConnected(shadow);
        Assert.Multiple(() => {
            Assert.That(walker.Count, Is.EqualTo(1), "legacy semantics swallow the shadowed neighbor");
            Assert.That(connected.Count, Is.EqualTo(2), "CCL keeps both stars");
        });

        // A connected donut ring is ONE component with all ring pixels present.
        var donut = new bool[32, 32];
        int litCount = 0;
        for (int y = 0; y < 32; y++) {
            for (int x = 0; x < 32; x++) {
                var r = Math.Sqrt((x - 16.0) * (x - 16.0) + (y - 16.0) * (y - 16.0));
                donut[y, x] = r >= 5 && r <= 9;
                if (donut[y, x]) litCount++;
            }
        }
        var donutComponents = RunConnected(donut);
        Assert.Multiple(() => {
            Assert.That(donutComponents.Count, Is.EqualTo(1));
            Assert.That(donutComponents[0].Points.Count, Is.EqualTo(litCount), "whole ring collected");
        });
    }

    [Test]
    public void CountSaturatedPixels_MatchesSerialCount() {
        var rng = new Random(999);
        using var mat = new Mat(311, 421, MatType.CV_32F);
        long expected = 0;
        const double threshold = 0.8;
        unsafe {
            var p = (float*)mat.DataPointer;
            for (int i = 0; i < 311 * 421; i++) {
                p[i] = (float)rng.NextDouble();
                if (p[i] >= threshold) expected++;
            }
        }
        Assert.That(StarDetector.CountSaturatedPixels(mat, threshold), Is.EqualTo(expected));
    }
}
