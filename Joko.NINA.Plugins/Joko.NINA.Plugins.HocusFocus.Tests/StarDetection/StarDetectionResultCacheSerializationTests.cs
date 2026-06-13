#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;
using System.Collections.Generic;
using System.Drawing;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Verifies the save-side prerequisite for the replay detection-result cache: the per-region
    /// _star_detection_result.json round-trips faithfully (polymorphic HocusFocusDetectedStar + PSF preserved),
    /// and the DetectorVersion / params-region CacheKey are stamped and stable.
    /// </summary>
    [TestFixture]
    public class StarDetectionResultCacheSerializationTests {

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

        private static HocusFocusStarDetectionResult BuildResult() {
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
                    OriginalPosition = new Accord.Point(100.5f, 200.25f),
                    StarContaminationSuspected = true
                },
                new HocusFocusDetectedStar {
                    HFR = 3.1,
                    Position = new Accord.Point(300.0f, 400.0f),
                    AverageBrightness = 8000.0,
                    MaxBrightness = 16000.0,
                    Background = 60.0,
                    BoundingBox = new Rectangle(290, 390, 20, 20),
                    PSF = null, // a star without a fitted PSF must also survive
                    NormalisedBrightness = 0.77f
                }
            };

            var detectorParams = new StarDetectorParams {
                Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.5))
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

        [Test]
        public void RoundTrip_PreservesConcreteResultType() {
            var json = StarDetectionResultCacheSerializer.Serialize(BuildResult());
            var restored = StarDetectionResultCacheSerializer.Deserialize(json);

            Assert.That(restored, Is.InstanceOf<HocusFocusStarDetectionResult>());
        }

        [Test]
        public void RoundTrip_PreservesHocusFocusDetectedStarSubtype() {
            var json = StarDetectionResultCacheSerializer.Serialize(BuildResult());
            var restored = StarDetectionResultCacheSerializer.Deserialize(json);

            Assert.That(restored.StarList, Has.Count.EqualTo(2));
            foreach (var star in restored.StarList) {
                // SensorModel does exactly this cast; it must not throw.
                Assert.That(star, Is.InstanceOf<HocusFocusDetectedStar>());
            }
        }

        [Test]
        public void RoundTrip_PreservesBaseDetectionFields() {
            var json = StarDetectionResultCacheSerializer.Serialize(BuildResult());
            var restored = StarDetectionResultCacheSerializer.Deserialize(json);
            var star = (HocusFocusDetectedStar)restored.StarList[0];

            Assert.Multiple(() => {
                Assert.That(star.HFR, Is.EqualTo(2.5).Within(1e-9));
                Assert.That(star.AverageBrightness, Is.EqualTo(5000.0).Within(1e-9));
                Assert.That(star.MaxBrightness, Is.EqualTo(12000.0).Within(1e-9));
                Assert.That(star.Background, Is.EqualTo(50.0).Within(1e-9));
                Assert.That((double)star.Position.X, Is.EqualTo(100.5).Within(1e-4));
                Assert.That((double)star.Position.Y, Is.EqualTo(200.25).Within(1e-4));
                Assert.That(star.BoundingBox.X, Is.EqualTo(95));
                Assert.That(star.BoundingBox.Y, Is.EqualTo(195));
                Assert.That(star.BoundingBox.Width, Is.EqualTo(12));
                Assert.That(star.BoundingBox.Height, Is.EqualTo(12));
                Assert.That(star.StarContaminationSuspected, Is.True);
                Assert.That(star.NormalisedBrightness, Is.EqualTo(0.42f).Within(1e-6f));
            });
        }

        [Test]
        public void RoundTrip_PreservesPsf() {
            var json = StarDetectionResultCacheSerializer.Serialize(BuildResult());
            var restored = StarDetectionResultCacheSerializer.Deserialize(json);
            var star = (HocusFocusDetectedStar)restored.StarList[0];

            Assert.That(star.PSF, Is.Not.Null);
            Assert.Multiple(() => {
                Assert.That(star.PSF.PSFType, Is.EqualTo(StarDetectorPSFFitType.Moffat_40));
                Assert.That(star.PSF.OffsetX, Is.EqualTo(0.5).Within(1e-9));
                Assert.That(star.PSF.OffsetY, Is.EqualTo(-0.25).Within(1e-9));
                Assert.That(star.PSF.Peak, Is.EqualTo(1234.5).Within(1e-9));
                Assert.That(star.PSF.Background, Is.EqualTo(12.0).Within(1e-9));
                Assert.That(star.PSF.SigmaX, Is.EqualTo(2.0).Within(1e-9));
                Assert.That(star.PSF.SigmaY, Is.EqualTo(3.0).Within(1e-9));
                Assert.That(star.PSF.FWHMx, Is.EqualTo(4.0).Within(1e-9));
                Assert.That(star.PSF.FWHMy, Is.EqualTo(6.0).Within(1e-9));
                Assert.That(star.PSF.ThetaRadians, Is.EqualTo(0.123).Within(1e-9));
                Assert.That(star.PSF.RSquared, Is.EqualTo(0.987).Within(1e-9));
                Assert.That(star.PSF.Beta, Is.EqualTo(4.0).Within(1e-9));
                // Derived members are recomputed by the ctor from the round-tripped inputs.
                Assert.That(star.PSF.Sigma, Is.EqualTo(System.Math.Sqrt(2.0 * 3.0)).Within(1e-9));
                Assert.That(star.PSF.FWHMPixels, Is.EqualTo(System.Math.Sqrt(4.0 * 6.0)).Within(1e-9));
                Assert.That(star.PSF.FWHMArcsecs, Is.EqualTo(System.Math.Sqrt(4.0 * 6.0) * 1.25).Within(1e-9));
            });
        }

        [Test]
        public void RoundTrip_PreservesNullPsf() {
            var json = StarDetectionResultCacheSerializer.Serialize(BuildResult());
            var restored = StarDetectionResultCacheSerializer.Deserialize(json);
            var star = (HocusFocusDetectedStar)restored.StarList[1];

            Assert.That(star.PSF, Is.Null);
        }

        [Test]
        public void RoundTrip_PreservesDetectorVersionAndCacheKey() {
            var original = BuildResult();
            var json = StarDetectionResultCacheSerializer.Serialize(original);
            var restored = StarDetectionResultCacheSerializer.Deserialize(json);

            Assert.Multiple(() => {
                Assert.That(restored.DetectorVersion, Is.EqualTo(StarDetector.StarDetectorVersion));
                Assert.That(restored.DetectorVersion, Is.EqualTo(original.DetectorVersion));
                Assert.That(restored.CacheKey, Is.EqualTo(original.CacheKey));
                Assert.That(restored.CacheKey, Is.Not.Null.And.Not.Empty);
            });
        }

        [Test]
        public void CacheKey_IsDeterministicForEqualParams() {
            var a = new StarDetectorParams { Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.5)) };
            var b = new StarDetectorParams { Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.5)) };

            Assert.That(StarDetector.ComputeCacheKey(a), Is.EqualTo(StarDetector.ComputeCacheKey(b)));
        }

        [Test]
        public void CacheKey_ChangesWhenADetectionParamChanges() {
            var baseline = new StarDetectorParams();
            var changed = new StarDetectorParams { Sensitivity = baseline.Sensitivity + 1.0 };

            Assert.That(StarDetector.ComputeCacheKey(changed), Is.Not.EqualTo(StarDetector.ComputeCacheKey(baseline)));
        }

        [Test]
        public void CacheKey_ChangesWhenRegionChanges() {
            var full = new StarDetectorParams { Region = StarDetectionRegion.Full };
            var cropped = new StarDetectorParams { Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.5)) };

            Assert.That(StarDetector.ComputeCacheKey(cropped), Is.Not.EqualTo(StarDetector.ComputeCacheKey(full)));
        }

        [Test]
        public void CacheKey_DependsOnDetectorVersion() {
            // The version is folded into the hashed input, so the canonical string the key hashes must contain
            // the current version token. (A real bump changes StarDetectorVersion and thus every key.)
            var p = new StarDetectorParams();
            var key = StarDetector.ComputeCacheKey(p);

            // Recompute SHA-256 over the documented canonical form and confirm it matches, proving the version
            // is part of the hashed input.
            var canonical = $"v{StarDetector.StarDetectorVersion}|{p.ToCanonicalCacheString()}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical));
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            foreach (var x in hash) {
                sb.Append(x.ToString("x2"));
            }

            Assert.That(key, Is.EqualTo(sb.ToString()));
        }

        // --- Canonical-key correctness: the 6 output-affecting params previously omitted from ToString() ---
        // Each must invalidate the cache when toggled away from its default, or a future reuse path would serve
        // stale results. Defaults (per StarDetectorParams): RejectContaminatedStars=true,
        // HfrTauPolicy=GateOnly, HotpixelThresholdingEnabled=true, HotpixelThreshold=0.001,
        // StarMeasurementNoiseReductionEnabled=false, PSFPixelIntegration=false.

        [Test]
        public void CacheKey_ChangesWhenRejectContaminatedStarsChanges() {
            var baseline = new StarDetectorParams();
            var changed = new StarDetectorParams { RejectContaminatedStars = !baseline.RejectContaminatedStars };

            Assert.That(StarDetector.ComputeCacheKey(changed), Is.Not.EqualTo(StarDetector.ComputeCacheKey(baseline)));
        }

        [Test]
        public void CacheKey_ChangesWhenHfrTauPolicyChanges() {
            var baseline = new StarDetectorParams { HfrTauPolicy = TauClipPolicy.GateOnly };
            var changed = new StarDetectorParams { HfrTauPolicy = TauClipPolicy.SubtractTau };

            Assert.That(StarDetector.ComputeCacheKey(changed), Is.Not.EqualTo(StarDetector.ComputeCacheKey(baseline)));
        }

        [Test]
        public void CacheKey_ChangesWhenHotpixelThresholdingEnabledChanges() {
            var baseline = new StarDetectorParams();
            var changed = new StarDetectorParams { HotpixelThresholdingEnabled = !baseline.HotpixelThresholdingEnabled };

            Assert.That(StarDetector.ComputeCacheKey(changed), Is.Not.EqualTo(StarDetector.ComputeCacheKey(baseline)));
        }

        [Test]
        public void CacheKey_ChangesWhenHotpixelThresholdChanges() {
            var baseline = new StarDetectorParams();
            var changed = new StarDetectorParams { HotpixelThreshold = baseline.HotpixelThreshold + 0.01 };

            Assert.That(StarDetector.ComputeCacheKey(changed), Is.Not.EqualTo(StarDetector.ComputeCacheKey(baseline)));
        }

        [Test]
        public void CacheKey_ChangesWhenStarMeasurementNoiseReductionEnabledChanges() {
            var baseline = new StarDetectorParams();
            var changed = new StarDetectorParams { StarMeasurementNoiseReductionEnabled = !baseline.StarMeasurementNoiseReductionEnabled };

            Assert.That(StarDetector.ComputeCacheKey(changed), Is.Not.EqualTo(StarDetector.ComputeCacheKey(baseline)));
        }

        [Test]
        public void CacheKey_ChangesWhenPSFPixelIntegrationChanges() {
            var baseline = new StarDetectorParams();
            var changed = new StarDetectorParams { PSFPixelIntegration = !baseline.PSFPixelIntegration };

            Assert.That(StarDetector.ComputeCacheKey(changed), Is.Not.EqualTo(StarDetector.ComputeCacheKey(baseline)));
        }

        // --- Denylist: perf-/diagnostics-only params must NOT change the key (no output effect ⇒ no miss). ---

        [Test]
        public void CacheKey_UnchangedWhenMaxStarEvaluationParallelismChanges() {
            var baseline = new StarDetectorParams();
            var changed = new StarDetectorParams { MaxStarEvaluationParallelism = baseline.MaxStarEvaluationParallelism + 4 };

            Assert.That(StarDetector.ComputeCacheKey(changed), Is.EqualTo(StarDetector.ComputeCacheKey(baseline)));
        }

        [Test]
        public void CacheKey_UnchangedWhenCollectContaminationDiagnosticsChanges() {
            var baseline = new StarDetectorParams();
            var changed = new StarDetectorParams { CollectContaminationDiagnostics = !baseline.CollectContaminationDiagnostics };

            Assert.That(StarDetector.ComputeCacheKey(changed), Is.EqualTo(StarDetector.ComputeCacheKey(baseline)));
        }

        // --- Region geometry: changing it changes the key; identical params ⇒ identical key. ---

        [Test]
        public void CacheKey_ChangesWhenRegionGeometryChanges() {
            var a = new StarDetectorParams { Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.5)) };
            var b = new StarDetectorParams { Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.6)) };

            Assert.That(StarDetector.ComputeCacheKey(b), Is.Not.EqualTo(StarDetector.ComputeCacheKey(a)));
        }

        // --- Culture invariance: the key for params with fractional doubles must be identical under de-DE
        // (which formats 0.5 as "0,5") and the invariant culture. ---

        [Test]
        public void CacheKey_IsCultureInvariant() {
            // Sensitivity=0.5 is the canary: under de-DE a bare ToString() would render "0,5" and diverge.
            string MakeKey() => StarDetector.ComputeCacheKey(new StarDetectorParams {
                Sensitivity = 0.5,
                HotpixelThreshold = 0.001,
                PixelScale = 1.25,
                Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.5))
            });

            var invariantKey = MakeKey();

            var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            try {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var deKey = MakeKey();
                Assert.That(deKey, Is.EqualTo(invariantKey));
            } finally {
                System.Globalization.CultureInfo.CurrentCulture = originalCulture;
            }
        }
    }
}
