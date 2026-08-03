#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using TestApp;
using TestApp.SynthBank;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Golden {

    /// <summary>
    /// G8b — golden-policy edge cases for <see cref="GoldenFromTruth"/>, constructed directly against
    /// <see cref="StarTruth"/> records rather than through a render (no <c>StarFieldCompositor</c> involved).
    ///
    /// <para><b>How the numbers are made exact.</b> Every test uses <see cref="Noise"/>'s default noise model:
    /// <c>sky = dark = 0</c>, <c>readNoise = 1</c>, <c>binning = 1</c>, so
    /// <see cref="GoldenFromTruth.BackgroundSigmaElectrons"/> = 1·√(0+0+1²) = 1 exactly. Every test star also uses
    /// <c>KernelPeakFraction = 1.0</c> (all flux in one binned pixel), so at binning 1
    /// <c>PeakFractionBinned = min(1, 1·1²) = 1</c> and therefore <c>peakSnr = FluxElectrons / 1 = FluxElectrons</c>
    /// — a test star's <see cref="StarTruth.FluxElectrons"/> IS its peak SNR, with no arithmetic to get wrong.</para>
    /// </summary>
    [TestFixture]
    public class GoldenFromTruthTests {

        private static GoldenFrameNoiseModel Noise(
                int binning = 1, int width = 300, int height = 300,
                double readNoise = 1.0, double saturation = 1.0e9, double sigmaMinPixels = 0.5) {
            return new GoldenFrameNoiseModel {
                SkyElectronsPerNativePixel = 0.0,
                DarkElectronsPerNativePixel = 0.0,
                ReadNoiseElectrons = readNoise,
                DigitalSaturationElectrons = saturation,
                Binning = binning,
                FrameWidthNative = width,
                FrameHeightNative = height,
                SigmaMinPixels = sigmaMinPixels
            };
        }

        /// <summary>A star whose peak SNR is exactly <paramref name="snr"/> under <see cref="Noise"/>'s defaults
        /// (see class remarks), at native pixel <paramref name="x"/>/<paramref name="y"/>.</summary>
        private static StarTruth Star(double x, double y, double snr, double hfr = 3.0, double outerRadius = 0.0) {
            return new StarTruth {
                CxPixels = x,
                CyPixels = y,
                FluxElectrons = snr,
                MeasuredHfrPixels = hfr,
                OuterRadiusPixels = outerRadius,
                KernelPeakFraction = 1.0
            };
        }

        private static GoldenTierThresholds Defaults() => new GoldenTierThresholds();

        // ── Tier cuts (20 / 10 / 5 / 3.5) ─────────────────────────────────────────────────────────────────────

        [TestCase(20.0, GoldenConfidence.High)]
        [TestCase(19.999, GoldenConfidence.Medium)]
        [TestCase(10.0, GoldenConfidence.Medium)]
        [TestCase(9.999, GoldenConfidence.Low)]
        [TestCase(5.0, GoldenConfidence.Low)]
        [TestCase(4.999, SyntheticTier.Unresolved)]
        [TestCase(3.5, SyntheticTier.Unresolved)]
        [TestCase(3.499, SyntheticTier.Omitted)]
        public void TierCut_AssignsExactBoundary(double snr, string expectedTier) {
            var truth = new List<StarTruth> { Star(100, 100, snr) };
            var result = GoldenFromTruth.Build(truth, Noise(), Defaults(), "frame.fits", 5000);

            Assert.That(result.Dispositions[0].Tier, Is.EqualTo(expectedTier));
            Assert.That(result.Dispositions[0].PeakSnr, Is.EqualTo(snr).Within(1e-9));

            // The bucket the tier claims to be in must actually match where the box (or lack of one) landed.
            switch (expectedTier) {
                case GoldenConfidence.High:
                case GoldenConfidence.Medium:
                case GoldenConfidence.Low:
                    Assert.That(result.Frame.Stars, Has.Count.EqualTo(1));
                    Assert.That(result.Frame.Stars[0].Confidence, Is.EqualTo(expectedTier));
                    Assert.That(result.Frame.Unresolved, Is.Null.Or.Empty);
                    break;
                case SyntheticTier.Unresolved:
                    Assert.That(result.Frame.Stars, Is.Empty);
                    Assert.That(result.Frame.Unresolved, Has.Count.EqualTo(1));
                    break;
                case SyntheticTier.Omitted:
                    Assert.That(result.Frame.Stars, Is.Empty);
                    Assert.That(result.Frame.Unresolved, Is.Null.Or.Empty);
                    break;
            }
        }

        // ── Merge boundary (2.0 x HFR_pair) ───────────────────────────────────────────────────────────────────

        [Test]
        public void Merge_JustInsideTwoTimesHfrPair_FoldsIntoOneComponent() {
            // Equal HFR (=10) and equal flux => hfrPair = 10 exactly; merge threshold separation = 2.0*10 = 20.
            var truth = new List<StarTruth> {
                Star(100, 100, snr: 15, hfr: 10),
                Star(100 + 19.9, 100, snr: 15, hfr: 10) // separation 19.9 < 20 -- just inside.
            };
            var result = GoldenFromTruth.Build(truth, Noise(), Defaults(), "frame.fits", 5000);

            Assert.That(result.Dispositions.Count(d => d.Tier == SyntheticTier.MergedInto), Is.EqualTo(1),
                "exactly one star should have folded into the other's component");
            Assert.That(result.Frame.Stars.Count + (result.Frame.Unresolved?.Count ?? 0), Is.EqualTo(1),
                "a merged pair produces exactly one box");
        }

        [Test]
        public void Merge_JustOutsideTwoTimesHfrPair_StaysTwoComponents() {
            var truth = new List<StarTruth> {
                Star(100, 100, snr: 15, hfr: 10),
                Star(100 + 20.1, 100, snr: 15, hfr: 10) // separation 20.1 > 20 -- just outside.
            };
            var result = GoldenFromTruth.Build(truth, Noise(), Defaults(), "frame.fits", 5000);

            Assert.That(result.Dispositions.Count(d => d.Tier == SyntheticTier.MergedInto), Is.EqualTo(0),
                "neither star should have merged into the other");
            Assert.That(result.Frame.Stars.Count + (result.Frame.Unresolved?.Count ?? 0), Is.EqualTo(2),
                "two independent (or blend-interacting) components produce two boxes");
        }

        // ── Blend boundary (2.0 - 4.0 x HFR_pair), flux ratio < dominance => both demoted ────────────────────

        [Test]
        public void Blend_JustInsideFourTimesHfrPair_DemotesBothToUnresolved() {
            // hfrPair = 10 (equal HFR/flux); blend upper edge separation = 4.0*10 = 40.
            var truth = new List<StarTruth> {
                Star(100, 100, snr: 15, hfr: 10),          // SNR 15 -> medium in isolation.
                Star(100 + 39.9, 100, snr: 15, hfr: 10)    // separation 39.9 < 40 -- just inside the blend band.
            };
            var result = GoldenFromTruth.Build(truth, Noise(), Defaults(), "frame.fits", 5000);

            Assert.Multiple(() => {
                Assert.That(result.Dispositions[0].Tier, Is.EqualTo(SyntheticTier.Unresolved), "demoted by the blend interaction");
                Assert.That(result.Dispositions[1].Tier, Is.EqualTo(SyntheticTier.Unresolved), "demoted by the blend interaction");
                Assert.That(result.Dispositions[0].Reason, Does.Contain("blend/dominance"));
            });
        }

        [Test]
        public void Blend_JustOutsideFourTimesHfrPair_NoInteraction_KeepsOwnTier() {
            var truth = new List<StarTruth> {
                Star(100, 100, snr: 15, hfr: 10),
                Star(100 + 40.1, 100, snr: 15, hfr: 10) // separation 40.1 > 40 -- just outside, independent.
            };
            var result = GoldenFromTruth.Build(truth, Noise(), Defaults(), "frame.fits", 5000);

            Assert.Multiple(() => {
                Assert.That(result.Dispositions[0].Tier, Is.EqualTo(GoldenConfidence.Medium), "SNR 15 -> medium, undemoted");
                Assert.That(result.Dispositions[1].Tier, Is.EqualTo(GoldenConfidence.Medium), "SNR 15 -> medium, undemoted");
            });
        }

        // ── Dominance flux ratio (10x) within the blend band ──────────────────────────────────────────────────

        [Test]
        public void Dominance_FluxRatioJustUnder10_BothDemoted() {
            // Separation 30 sits inside [20,40) -- the blend/dominance band for hfrPair=10. Ratio 100/10.101 = 9.9.
            var truth = new List<StarTruth> {
                Star(100, 100, snr: 100, hfr: 10),               // bright: SNR 100 -> high in isolation.
                Star(130, 100, snr: 100.0 / 9.9, hfr: 10)        // faint: SNR ~10.10 -> medium in isolation.
            };
            var result = GoldenFromTruth.Build(truth, Noise(), Defaults(), "frame.fits", 5000);

            Assert.Multiple(() => {
                Assert.That(result.Dispositions[0].Tier, Is.EqualTo(SyntheticTier.Unresolved), "ratio 9.9 < 10 -> BOTH demoted (blend)");
                Assert.That(result.Dispositions[1].Tier, Is.EqualTo(SyntheticTier.Unresolved), "ratio 9.9 < 10 -> BOTH demoted (blend)");
            });
        }

        [Test]
        public void Dominance_FluxRatioJustOver10_OnlyFaintDemoted() {
            // Ratio 100/9.901 = 10.1 -- at/above DominanceFluxRatio, the bright star is spared.
            var truth = new List<StarTruth> {
                Star(100, 100, snr: 100, hfr: 10),                // bright: SNR 100 -> high.
                Star(130, 100, snr: 100.0 / 10.1, hfr: 10)        // faint: SNR ~9.90 -> medium in isolation.
            };
            var result = GoldenFromTruth.Build(truth, Noise(), Defaults(), "frame.fits", 5000);

            Assert.Multiple(() => {
                Assert.That(result.Dispositions[0].Tier, Is.EqualTo(GoldenConfidence.High), "bright star keeps its tier -- dominance, not blend");
                Assert.That(result.Dispositions[1].Tier, Is.EqualTo(SyntheticTier.Unresolved), "faint star alone is demoted");
            });
        }

        // ── Edge-clipped box -> unresolved ────────────────────────────────────────────────────────────────────

        [Test]
        public void EdgeClippedBox_DemotesToUnresolved() {
            // HFR=10 -> box half-width 2*10=20, box spans [x-20,x+20]. At x=5 in a 60x60 frame the box is clipped
            // on the low edge; SNR=50 would otherwise be a clean "high" (unclipped) box.
            var noise = Noise(width: 60, height: 60);
            var truth = new List<StarTruth> { Star(5, 5, snr: 50, hfr: 10) };
            var result = GoldenFromTruth.Build(truth, noise, Defaults(), "frame.fits", 5000);

            Assert.Multiple(() => {
                Assert.That(result.Dispositions[0].Tier, Is.EqualTo(SyntheticTier.Unresolved));
                Assert.That(result.Frame.Stars, Is.Empty);
                Assert.That(result.Frame.Unresolved, Has.Count.EqualTo(1));
                var box = result.Frame.Unresolved[0];
                Assert.That(box.X, Is.GreaterThanOrEqualTo(0.0), "clipped box must not extend past the frame's left edge");
                Assert.That(box.Y, Is.GreaterThanOrEqualTo(0.0), "clipped box must not extend past the frame's top edge");
                Assert.That(result.Dispositions[0].Reason, Does.Contain("clipped"));
            });
        }

        // ── Saturation -> kept + high + flagged ───────────────────────────────────────────────────────────────

        [Test]
        public void SaturatedStar_ForcedHighAndFlagged_EvenWhenOwnSnrIsOnlyMedium() {
            // saturationCeiling = 0.98 * 15 = 14.7; peakElectrons(=FluxElectrons, see class remarks) = 15 >= 14.7
            // -> saturated. SNR 15 alone would be "medium" (10<=15<20); saturation must override that to "high".
            var noise = Noise(saturation: 15.0);
            var truth = new List<StarTruth> { Star(100, 100, snr: 15, hfr: 3) };
            var result = GoldenFromTruth.Build(truth, noise, Defaults(), "frame.fits", 5000);

            Assert.Multiple(() => {
                Assert.That(result.Dispositions[0].Saturated, Is.True);
                Assert.That(result.Dispositions[0].Tier, Is.EqualTo(GoldenConfidence.High), "saturation forces high regardless of raw SNR tier");
                Assert.That(result.Frame.Stars, Has.Count.EqualTo(1));
                Assert.That(result.Frame.Stars[0].Confidence, Is.EqualTo(GoldenConfidence.High));
                Assert.That(result.Dispositions[0].Reason, Does.Contain("saturation"));
            });
        }

        // ── Binned coordinate transform ───────────────────────────────────────────────────────────────────────

        [TestCase(0.0, 1, 0.0)]
        [TestCase(99.5, 2, 49.5)]     // (99.5+0.5)/2 - 0.5 = 50 - 0.5 = 49.5
        [TestCase(3.0, 1, 3.0)]       // binning 1 is the identity transform.
        [TestCase(15.0, 4, 3.375)]    // (15+0.5)/4 - 0.5 = 3.875 - 0.5 = 3.375
        public void ToBinnedCoordinate_MatchesClosedForm(double native, int binning, double expectedBinned) {
            Assert.That(GoldenFromTruth.ToBinnedCoordinate(native, binning), Is.EqualTo(expectedBinned).Within(1e-9));
        }

        [Test]
        public void Build_TransformsCenterAndHfrByBinning_EndToEnd() {
            const int binning = 2;
            var noise = Noise(binning: binning, width: 400, height: 400, readNoise: 1.0);
            // At binning 2: PeakFractionBinned = min(1, 1.0*2*2) = 1 (still capped at 1) and
            // BackgroundSigmaElectrons = 2*sqrt(1) = 2, so peakSnr = FluxElectrons/2. Use FluxElectrons=200 -> SNR 100 (high).
            var truth = new List<StarTruth> { Star(x: 99.5, y: 199.5, snr: 200, hfr: 20) };
            var result = GoldenFromTruth.Build(truth, noise, Defaults(), "frame.fits", 5000);

            Assert.Multiple(() => {
                Assert.That(result.Dispositions[0].BinnedCenterX, Is.EqualTo(GoldenFromTruth.ToBinnedCoordinate(99.5, binning)).Within(1e-9));
                Assert.That(result.Dispositions[0].BinnedCenterY, Is.EqualTo(GoldenFromTruth.ToBinnedCoordinate(199.5, binning)).Within(1e-9));
                Assert.That(result.Dispositions[0].BinnedHfrPixels, Is.EqualTo(20.0 / binning).Within(1e-9), "HFR divides by binning too");
                Assert.That(result.Dispositions[0].PeakSnr, Is.EqualTo(100.0).Within(1e-9));
                Assert.That(result.Dispositions[0].Tier, Is.EqualTo(GoldenConfidence.High));
            });
        }

        // ── Component-centroid blend/dominance discovery (three-body case) ───────────────────────────────────
        //
        // Regression coverage for the bug where Phase 4 derived its blend/dominance candidates from the
        // star-level candidate list built for merges, instead of running its own search over component
        // centroids. A merged component's flux-weighted centroid can sit closer to a third star than either of
        // its individual members did, so a component pair can land inside the blend/dominance band even when
        // NEITHER constituent star-pair ever qualified as a blend candidate on its own. All three stars below
        // use HFR = 1 px, and the defaults are Merge = 2.0x, Blend = 4.0x, Dominance flux ratio = 10x:
        //   a1 = (149.1, 153.95), a2 = (150.9, 153.95) -- separation 1.8 < 2.0 -> merge into component A,
        //     flux-weighted centroid (150.0, 153.95) (equal flux -> exact midpoint).
        //   b = (150.0, 150.0) -- dist(a1,b) = dist(a2,b) = sqrt(0.9^2 + 3.95^2) ~= 4.051 >= 4.0, so NEITHER
        //     star-level pair is ever a blend candidate.
        //   But dist(A.centroid, b) = 3.95 and hfrPair(A,b) = 1 (both HFRs are 1), so 2.0 <= 3.95 < 4.0 --
        //     squarely inside the blend/dominance band at the COMPONENT level.
        // Component A's combined flux (100 = 50+50) is 20x b's flux (5), clearing DominanceFluxRatio (10), so
        // the expected outcome is dominance: A keeps its "high" tier as ONE box, b is demoted to unresolved --
        // NOT two independent "stars" boxes ~4 px apart for what is optically one blended trio.

        [Test]
        public void ComponentCentroidBlendDiscovery_ThirdStarInsideBand_DemotesThirdStarInsteadOfASecondBox() {
            var truth = new List<StarTruth> {
                Star(149.1, 153.95, snr: 50, hfr: 1),  // a1 -- merges with a2 (separation 1.8 < 2.0*1)
                Star(150.9, 153.95, snr: 50, hfr: 1),  // a2
                Star(150.0, 150.0, snr: 5, hfr: 1)     // b  -- 20x fainter than the merged component (100 vs 5)
            };
            var result = GoldenFromTruth.Build(truth, Noise(), Defaults(), "frame.fits", 5000);

            Assert.Multiple(() => {
                // The pre-fix bug produced Frame.Stars.Count == 2 here (the merged pair AND b, independently),
                // because Phase 4 never discovered ANY candidate for this component/third-star pair.
                Assert.That(result.Frame.Stars, Has.Count.EqualTo(1),
                    "the merged pair must be the ONLY stars box -- b must not survive as an independent second box");
                Assert.That(result.Frame.Stars[0].Confidence, Is.EqualTo(GoldenConfidence.High));
                Assert.That(result.Frame.Unresolved, Has.Count.EqualTo(1),
                    "b must be demoted to unresolved by the (newly-discovered) component-level dominance interaction");

                Assert.That(result.Dispositions[2].Tier, Is.EqualTo(SyntheticTier.Unresolved),
                    "b (truth index 2) must be demoted, not left as an independent star");
                Assert.That(result.Dispositions[2].Reason, Does.Contain("blend/dominance"));

                // Exactly one of a1/a2 folded into the other's component (merged-into); the other is the
                // component's representative and carries the combined "high" tier.
                Assert.That(result.Dispositions.Count(d => d.Tier == SyntheticTier.MergedInto), Is.EqualTo(1));
                Assert.That(result.Dispositions.Count(d => d.Tier == GoldenConfidence.High), Is.EqualTo(1));
            });
        }

        [Test]
        public void ComponentCentroidBlendDiscovery_ThirdStarJustOutsideBand_StaysTwoIndependentStars() {
            // Same merged pair as above, but b moved to (150.0, 149.85): dist(A.centroid, b) = 4.10, just
            // outside the blend band (>= 4.0*hfrPair=4.0). Confirms the fix's component-centroid search does
            // not over-reach -- a genuinely independent third star must not be pulled into a demotion it does
            // not qualify for.
            var truth = new List<StarTruth> {
                Star(149.1, 153.95, snr: 50, hfr: 1),  // a1 -- merges with a2 (separation 1.8 < 2.0*1)
                Star(150.9, 153.95, snr: 50, hfr: 1),  // a2
                Star(150.0, 149.85, snr: 5, hfr: 1)    // b  -- component-centroid distance 4.10, just outside the band
            };
            var result = GoldenFromTruth.Build(truth, Noise(), Defaults(), "frame.fits", 5000);

            Assert.Multiple(() => {
                Assert.That(result.Frame.Stars, Has.Count.EqualTo(2),
                    "the merged pair and b are both independent at this separation -- no interaction should be found");
                Assert.That(result.Frame.Unresolved, Is.Null.Or.Empty);
                Assert.That(result.Dispositions[2].Tier, Is.EqualTo(GoldenConfidence.Low), "b keeps its own tier (SNR 5 -> low), undemoted");
            });
        }
    }
}
