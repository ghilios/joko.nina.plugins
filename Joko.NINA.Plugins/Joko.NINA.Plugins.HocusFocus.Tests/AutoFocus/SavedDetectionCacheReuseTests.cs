#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    /// <summary>
    /// Unit tests for the reuse-side decision helper of the replay detection-result cache
    /// (<see cref="AutoFocusEngine.TryLoadValidCachedDetection"/>). This is the safety-critical gate: it must
    /// return a saved result ONLY when the detector version AND the params-derived cache key both match the
    /// current run, and must treat every other case (missing file, version mismatch, param mismatch, corrupt
    /// file) as a miss so the caller falls back to a full detection. A wrong "hit" would feed a stale/incorrect
    /// measurement into both the AF curve and the sensor model.
    /// </summary>
    [TestFixture]
    public class SavedDetectionCacheReuseTests {

        private const int ImageNumber = 3;
        private const int FrameNumber = 1;
        private const int RegionIndex = 2;

        private static PSFModel BuildPsf() {
            return new PSFModel(
                psfType: StarDetectorPSFFitType.Moffat_40,
                offsetX: 0.5, offsetY: -0.25,
                peak: 1234.5, background: 12.0,
                sigmaX: 2.0, sigmaY: 3.0,
                fwhmX: 4.0, fwhmY: 6.0,
                thetaRadians: 0.123,
                rSquared: 0.987,
                pixelScale: 1.25,
                reducedChiSquared: 1.05,
                beta: 4.0);
        }

        // Builds a result whose CacheKey/DetectorVersion are stamped from the given params, exactly as the live
        // Detect path does, so a faithfully-saved file validates against those same params.
        private static HocusFocusStarDetectionResult BuildResult(StarDetectorParams detectorParams) {
            var stars = new List<DetectedStar> {
                new HocusFocusDetectedStar {
                    HFR = 2.5,
                    Position = new Accord.Point(100.5f, 200.25f),
                    AverageBrightness = 5000.0,
                    MaxBrightness = 12000.0,
                    Background = 50.0,
                    BoundingBox = new Rectangle(95, 195, 12, 12),
                    PSF = BuildPsf(),
                    NormalisedBrightness = 0.42f,
                    OriginalPosition = new Accord.Point(100.5f, 200.25f)
                }
            };

            return new HocusFocusStarDetectionResult {
                AverageHFR = 2.8,
                HFRStdDev = 0.3,
                DetectedStars = stars.Count,
                StarList = stars,
                DetectorParams = detectorParams,
                Region = detectorParams.Region,
                Metrics = new StarDetectorMetrics(),
                FocuserPosition = 12345,
                PixelScale = 1.25,
                DetectorVersion = StarDetector.StarDetectorVersion,
                CacheKey = StarDetector.ComputeCacheKey(detectorParams)
            };
        }

        // Writes the cached JSON to the canonical per-region filename inside the source folder.
        private static void WriteCache(string folder, HocusFocusStarDetectionResult result, int imageNumber = ImageNumber, int frameNumber = FrameNumber, int regionIndex = RegionIndex) {
            var path = Path.Combine(folder, AutoFocusEngine.BuildStarDetectionResultFileName(imageNumber, frameNumber, regionIndex));
            File.WriteAllText(path, StarDetectionResultCacheSerializer.Serialize(result));
        }

        [Test]
        public void VersionAndKeyMatch_ReturnsTrueWithRoundTrippedResult() {
            using var tmp = new TempDir();
            var currentParams = new StarDetectorParams { Sensitivity = 0.5 };
            WriteCache(tmp.Path, BuildResult(currentParams));

            var hit = AutoFocusEngine.TryLoadValidCachedDetection(
                tmp.Path, ImageNumber, FrameNumber, RegionIndex, currentParams, out var cached);

            Assert.Multiple(() => {
                Assert.That(hit, Is.True);
                Assert.That(cached, Is.Not.Null);
                Assert.That(cached, Is.InstanceOf<HocusFocusStarDetectionResult>());
                // Round-tripped measurement feeds the AF curve; star list (HocusFocusDetectedStar) feeds the sensor model.
                Assert.That(cached.AverageHFR, Is.EqualTo(2.8).Within(1e-9));
                Assert.That(cached.HFRStdDev, Is.EqualTo(0.3).Within(1e-9));
                Assert.That(cached.StarList, Has.Count.EqualTo(1));
                Assert.That(cached.StarList[0], Is.InstanceOf<HocusFocusDetectedStar>());
                Assert.That(cached.CacheKey, Is.EqualTo(StarDetector.ComputeCacheKey(currentParams)));
                Assert.That(cached.DetectorVersion, Is.EqualTo(StarDetector.StarDetectorVersion));
            });
        }

        [Test]
        public void MissingFile_ReturnsFalse() {
            using var tmp = new TempDir();
            var currentParams = new StarDetectorParams { Sensitivity = 0.5 };
            // No file written.

            var hit = AutoFocusEngine.TryLoadValidCachedDetection(
                tmp.Path, ImageNumber, FrameNumber, RegionIndex, currentParams, out var cached);

            Assert.Multiple(() => {
                Assert.That(hit, Is.False);
                Assert.That(cached, Is.Null);
            });
        }

        [Test]
        public void DetectorVersionMismatch_ReturnsFalse() {
            using var tmp = new TempDir();
            var currentParams = new StarDetectorParams { Sensitivity = 0.5 };
            var result = BuildResult(currentParams);
            // Simulate a result produced by an older detector version. The CacheKey is left matching so this test
            // isolates the version check specifically.
            result.DetectorVersion = StarDetector.StarDetectorVersion - 1;
            WriteCache(tmp.Path, result);

            var hit = AutoFocusEngine.TryLoadValidCachedDetection(
                tmp.Path, ImageNumber, FrameNumber, RegionIndex, currentParams, out var cached);

            Assert.Multiple(() => {
                Assert.That(hit, Is.False);
                Assert.That(cached, Is.Null);
            });
        }

        [Test]
        public void CacheKeyMismatch_DifferentParams_ReturnsFalse() {
            using var tmp = new TempDir();
            // Save was produced with one set of params...
            var savedParams = new StarDetectorParams { Sensitivity = 0.5 };
            WriteCache(tmp.Path, BuildResult(savedParams));

            // ...but the current run uses different detection params (different Sensitivity → different key).
            var currentParams = new StarDetectorParams { Sensitivity = 0.9 };
            Assert.That(StarDetector.ComputeCacheKey(currentParams), Is.Not.EqualTo(StarDetector.ComputeCacheKey(savedParams)),
                "Test precondition: the two param sets must produce different cache keys");

            var hit = AutoFocusEngine.TryLoadValidCachedDetection(
                tmp.Path, ImageNumber, FrameNumber, RegionIndex, currentParams, out var cached);

            Assert.Multiple(() => {
                Assert.That(hit, Is.False);
                Assert.That(cached, Is.Null);
            });
        }

        [Test]
        public void CacheKeyMismatch_DifferentRegion_ReturnsFalse() {
            using var tmp = new TempDir();
            // Region is folded into the cache key, so a different region must invalidate reuse.
            var savedParams = new StarDetectorParams { Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.5)) };
            WriteCache(tmp.Path, BuildResult(savedParams));

            var currentParams = new StarDetectorParams { Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.8)) };
            Assert.That(StarDetector.ComputeCacheKey(currentParams), Is.Not.EqualTo(StarDetector.ComputeCacheKey(savedParams)),
                "Test precondition: the two regions must produce different cache keys");

            var hit = AutoFocusEngine.TryLoadValidCachedDetection(
                tmp.Path, ImageNumber, FrameNumber, RegionIndex, currentParams, out var cached);

            Assert.Multiple(() => {
                Assert.That(hit, Is.False);
                Assert.That(cached, Is.Null);
            });
        }

        [Test]
        public void CorruptFile_ReturnsFalse() {
            using var tmp = new TempDir();
            var currentParams = new StarDetectorParams { Sensitivity = 0.5 };
            var path = Path.Combine(tmp.Path, AutoFocusEngine.BuildStarDetectionResultFileName(ImageNumber, FrameNumber, RegionIndex));
            File.WriteAllText(path, "{ this is not valid json ]]]");

            var hit = AutoFocusEngine.TryLoadValidCachedDetection(
                tmp.Path, ImageNumber, FrameNumber, RegionIndex, currentParams, out var cached);

            Assert.Multiple(() => {
                Assert.That(hit, Is.False);
                Assert.That(cached, Is.Null);
            });
        }

        [Test]
        public void UsesOriginalImageAndFrameNumbersForFileName() {
            using var tmp = new TempDir();
            var currentParams = new StarDetectorParams { Sensitivity = 0.5 };
            // File saved under the ORIGINAL image/frame numbers from the saved filename.
            WriteCache(tmp.Path, BuildResult(currentParams), imageNumber: 7, frameNumber: 4, regionIndex: 1);

            // Looking it up with the original numbers hits...
            var hit = AutoFocusEngine.TryLoadValidCachedDetection(
                tmp.Path, 7, 4, 1, currentParams, out var cached);
            // ...but a different (e.g. replay-reassigned) image number misses, proving the original numbers drive the name.
            var missWrongImage = AutoFocusEngine.TryLoadValidCachedDetection(
                tmp.Path, 99, 4, 1, currentParams, out var cachedMiss);

            Assert.Multiple(() => {
                Assert.That(hit, Is.True);
                Assert.That(cached, Is.Not.Null);
                Assert.That(missWrongImage, Is.False);
                Assert.That(cachedMiss, Is.Null);
            });
        }

        private sealed class TempDir : IDisposable {
            public string Path { get; }

            public TempDir() {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hf-cache-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose() {
                try { Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
            }
        }
    }
}
