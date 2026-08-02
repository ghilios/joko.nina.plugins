#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TestApp;

namespace TestApp.SynthBank {

    /// <summary>
    /// Named string labels a <see cref="SyntheticStarDisposition.Tier"/> can hold beyond the three ordinary
    /// confidence tiers (<see cref="GoldenConfidence.High"/>/<see cref="GoldenConfidence.Medium"/>/
    /// <see cref="GoldenConfidence.Low"/>, which are reused verbatim so a tiered disposition's <c>Tier</c> is
    /// literally the same string that lands in <see cref="GoldenStarBox.Confidence"/>).
    /// </summary>
    public static class SyntheticTier {

        /// <summary>The star (or merged component) is real but was deliberately excluded from both the
        /// required-find and false-positive sets — see <see cref="GoldenFrame.Unresolved"/>.</summary>
        public const string Unresolved = "unresolved";

        /// <summary>The star was dropped entirely: it produces no box anywhere in <see cref="GoldenFrame"/>,
        /// so a detector reporting something at its location should be scored as a false positive.</summary>
        public const string Omitted = "omitted";

        /// <summary>The star is a non-representative member of a merged component; its own light was folded
        /// into another star's box (<see cref="SyntheticStarDisposition.MergedIntoTruthIndex"/>) rather than
        /// producing a box of its own.</summary>
        public const string MergedInto = "merged-into";
    }

    /// <summary>
    /// Every threshold <see cref="GoldenFromTruth"/> needs to turn peak SNR and pairwise geometry into golden
    /// tiers, spec-configurable per the design ("G5. Golden policy") so a driver can probe sensitivity without
    /// editing code. Defaults match the design doc; every value has a documented reason, not just a number.
    /// </summary>
    public sealed record GoldenTierThresholds {

        /// <summary>
        /// Peak-pixel SNR at/above which a star (or merged component) is tier <see cref="GoldenConfidence.High"/>.
        /// Deliberately <b>2×</b> the detector's own default gate (<c>BrightnessSensitivity</c> /
        /// <c>ExposureRecommender.TargetSensitivity</c> = 10, <c>StarDetectionOptions.cs</c> /
        /// <c>ExposureRecommender.cs</c>). At 2× the gate threshold, a star the detector misses under its
        /// DEFAULT configuration is a genuine recall defect rather than a borderline call — that is what makes
        /// "recall@high ≈ 1.0" a legitimate expectation of a correct detector, not merely a hope.
        /// </summary>
        public double HighSnr { get; init; } = 20.0;

        /// <summary>
        /// Peak-pixel SNR at/above which a star is tier <see cref="GoldenConfidence.Medium"/> (below
        /// <see cref="HighSnr"/>). Set at the detector's own default gate (~10) — the boundary between "the
        /// detector's default settings should find this" and "only a more sensitive configuration would".
        /// </summary>
        public double MediumSnr { get; init; } = 10.0;

        /// <summary>
        /// Peak-pixel SNR at/above which a star is tier <see cref="GoldenConfidence.Low"/> (below
        /// <see cref="MediumSnr"/>). Half the detector's default gate: stars here are within reach of a
        /// permissive configuration, but a default-sensitivity run legitimately misses many of them, so they
        /// are counted as real stars without being held against recall@high or recall@medium.
        /// </summary>
        public double LowSnr { get; init; } = 5.0;

        /// <summary>
        /// Peak-pixel SNR floor: at/above this (but below <see cref="LowSnr"/>) a star is real but
        /// <see cref="SyntheticTier.Unresolved"/> — neither a required find nor a false positive. Below it the
        /// star is <see cref="SyntheticTier.Omitted"/> entirely. This idealized noise model has no matched
        /// filter and no local background estimate, so below ~3.5σ its own peak-pixel test cannot vouch for
        /// visibility with any confidence; scoring a detection there as a false positive would be punishing a
        /// detector for something the reference itself cannot certify either way.
        /// </summary>
        public double UnresolvedSnr { get; init; } = 3.5;

        /// <summary>
        /// Two stars whose centre separation is below this many × their flux-weighted mean HFR are folded into
        /// ONE merged box (union-find), rather than scored as two independent finds. At this separation their
        /// point-spread functions overlap enough that even an ideal detector centroiding the combined light
        /// would report a single blob — charging a detector a false negative AND a false positive for exactly
        /// that behavior is what this rule exists to prevent.
        /// </summary>
        public double MergeSeparationHfrMultiple { get; init; } = 2.0;

        /// <summary>
        /// The upper edge of the "blend" interaction band (paired with <see cref="MergeSeparationHfrMultiple"/>
        /// as the lower edge): inside <c>[Merge, Blend) × HFR_pair</c> two stars' PSFs still overlap enough that
        /// reporting one blob or two is equally defensible. At/above this separation the two point-spread
        /// functions' wings no longer meaningfully overlap, so a detector reporting two independent stars is
        /// unambiguously correct and no interaction rule applies.
        /// </summary>
        public double BlendSeparationHfrMultiple { get; init; } = 4.0;

        /// <summary>
        /// Flux ratio (bright/faint) at/above which a blend-range pair (see <see cref="BlendSeparationHfrMultiple"/>)
        /// is resolved as "dominance" (bright star keeps its tier, faint one is demoted) rather than "blend"
        /// (both demoted to <see cref="SyntheticTier.Unresolved"/>). 10× is ~2.5 magnitudes: below it the two
        /// stars are close enough in brightness that a detector reporting either count is defensible; at/above
        /// it the faint star's contribution to a combined centroid or flux measurement is within noise of
        /// undetectable on its own, so it reads as the bright star's wing rather than a real count ambiguity.
        /// </summary>
        public double DominanceFluxRatio { get; init; } = 10.0;

        /// <summary>
        /// Floor on the box half-width (px), applied after the HFR- and annulus-derived half-widths. Without
        /// it, a heavily oversampled in-focus point source (HFR well under 1 px) would get a box only 1-2 px
        /// wide — too small for IoU or even a coordinate-tolerant centroid match to be meaningful.
        /// </summary>
        public double MinBoxHalfWidthPixels { get; init; } = 4.0;

        /// <summary>
        /// Fraction of the sensor's digital saturation (<see cref="GoldenFrameNoiseModel.DigitalSaturationElectrons"/>)
        /// at/above which a star's peak electrons count as saturated. Just under the ADC's literal 100%
        /// ceiling: a star whose modeled peak sits a couple of ADU short of the exact digital limit is already
        /// visually clipped in practice (quantization plus the deterministic-vs-actual-pixel rounding this
        /// policy cannot see), so 0.98 catches those cores instead of only the mathematically exact ones.
        /// </summary>
        public double SaturationFraction { get; init; } = 0.98;
    }

    /// <summary>
    /// The pieces of the noise/geometry model that <see cref="StarTruth"/> does not itself carry, but the peak-SNR
    /// math (and the binning transform ahead of it) needs. Everything here is a single frame-wide scalar or
    /// integer — none of it varies per star — because the render's sky/dark/read-noise model is spatially
    /// uniform at the fidelity this policy operates at.
    /// </summary>
    public sealed record GoldenFrameNoiseModel {

        /// <summary>Sky background, in electrons per NATIVE (pre-binning) pixel, already integrated over the
        /// whole exposure (i.e. a rate × exposure time, not a rate).</summary>
        public double SkyElectronsPerNativePixel { get; init; }

        /// <summary>Dark current, in electrons per NATIVE (pre-binning) pixel, already integrated over the
        /// whole exposure.</summary>
        public double DarkElectronsPerNativePixel { get; init; }

        /// <summary>Read noise, in electrons RMS, per NATIVE (pre-binning) pixel (paid once per native pixel
        /// that is summed into a binned pixel — see <see cref="GoldenFromTruth.BackgroundSigmaElectrons"/>).</summary>
        public double ReadNoiseElectrons { get; init; }

        /// <summary>The sensor's digital saturation, in electrons, at the capture gain — <c>N_sat(g)</c> from
        /// <c>SensorDefinition.DigitalSaturationElectronsAtGain</c>. Compared against a star's PEAK electrons
        /// (not its total flux) to decide <see cref="SyntheticStarDisposition.Saturated"/>.</summary>
        public double DigitalSaturationElectrons { get; init; }

        /// <summary>Capture binning factor b (≥ 1). Every native-pixel quantity (positions, HFR, radii, σ_min)
        /// is transformed into captured/binned pixels by this factor before anything downstream runs.</summary>
        public int Binning { get; init; }

        /// <summary>Full native (pre-binning) frame width, px — matches
        /// <c>StarFieldCompositor</c>'s rendered <c>width</c> / <c>HocusFocusSimulatorCamera.BinFrame</c>'s
        /// input width.</summary>
        public int FrameWidthNative { get; init; }

        /// <summary>Full native (pre-binning) frame height, px — see <see cref="FrameWidthNative"/>.</summary>
        public int FrameHeightNative { get; init; }

        /// <summary>In-focus combined σ (px, NATIVE/pre-binning) — <c>DefocusModel.SigmaMinPixels</c>. Used as
        /// the box-geometry margin term (<c>3·σ_min</c>) so a box comfortably covers an in-focus PSF's wings
        /// even when its HFR alone would size a tighter box.</summary>
        public double SigmaMinPixels { get; init; }
    }

    /// <summary>
    /// One input star's full disposition after the golden policy runs: where it ended up (a binned centre/HFR,
    /// which may be its own or — if merged — its component's), how confident the policy is in it, and WHY. This
    /// is the "truth sidecar" that makes a false negative in a scored run debuggable: which star, how bright, and
    /// which specific rule (SNR floor, merge, blend, dominance, saturation, frame-edge clipping) produced the
    /// outcome.
    /// </summary>
    public sealed record SyntheticStarDisposition {

        /// <summary>Index of <see cref="Star"/> in the input truth list passed to <see cref="GoldenFromTruth.Build"/>
        /// — a stable identity used by <see cref="MergedIntoTruthIndex"/> to cross-reference within the same call.</summary>
        public int TruthIndex { get; init; }

        /// <summary>The input truth record this disposition describes.</summary>
        public StarTruth Star { get; init; }

        /// <summary>
        /// Binned (captured-pixel) centre X. For the REPRESENTATIVE star of a merged component (see
        /// <see cref="Tier"/> / <see cref="MergedIntoTruthIndex"/>) this is the component's flux-weighted
        /// combined centroid, not this star's own position — that combined value is what the emitted
        /// <see cref="GoldenStarBox"/> is actually centred on. For a non-representative merged-away star, and
        /// for every standalone star, this is the star's own binned centre.
        /// </summary>
        public double BinnedCenterX { get; init; }

        /// <summary>Binned (captured-pixel) centre Y — see <see cref="BinnedCenterX"/>.</summary>
        public double BinnedCenterY { get; init; }

        /// <summary>Binned HFR (px) — the component's combined (flux-weighted) HFR for a representative,
        /// this star's own for everyone else. See <see cref="BinnedCenterX"/> for the same representative/member
        /// distinction.</summary>
        public double BinnedHfrPixels { get; init; }

        /// <summary>Peak-pixel SNR — the component's combined SNR for a representative, this star's own
        /// (pre-merge) SNR for everyone else. See <see cref="BinnedCenterX"/>.</summary>
        public double PeakSnr { get; init; }

        /// <summary>
        /// <see cref="GoldenConfidence.High"/> / <see cref="GoldenConfidence.Medium"/> / <see cref="GoldenConfidence.Low"/>
        /// when this star (or, for a representative, its component) landed in <see cref="GoldenFrame.Stars"/>;
        /// <see cref="SyntheticTier.Unresolved"/> when it landed in <see cref="GoldenFrame.Unresolved"/>;
        /// <see cref="SyntheticTier.Omitted"/> when it was dropped entirely; <see cref="SyntheticTier.MergedInto"/>
        /// for a non-representative member of a merged component (its light contributed to another star's box,
        /// but it produced none of its own).
        /// </summary>
        public string Tier { get; init; }

        /// <summary>
        /// When <see cref="Tier"/> is <see cref="SyntheticTier.MergedInto"/>, the <see cref="TruthIndex"/> of the
        /// representative (brightest-flux, ties broken by lowest <see cref="TruthIndex"/>) member of this star's
        /// merged component — the star whose box actually carries the combined light. Null otherwise.
        /// </summary>
        public int? MergedIntoTruthIndex { get; init; }

        /// <summary>
        /// True when THIS star's own peak electrons crossed <see cref="GoldenTierThresholds.SaturationFraction"/>
        /// of <see cref="GoldenFrameNoiseModel.DigitalSaturationElectrons"/> (individually — a representative
        /// also picks this up if ANY member of its component saturated, or if the component's combined peak
        /// does). <b>This is the only place saturation is recorded</b> — <see cref="GoldenStarBox"/> has no
        /// "saturated" field, deliberately: a clipped core has no meaningful HFR, so a consumer wanting an
        /// "HFR-assessable" subset of the golden filters dispositions by <c>!Saturated</c> rather than reading
        /// anything off the emitted box.
        /// </summary>
        public bool Saturated { get; init; }

        /// <summary>Human-readable explanation of which rule produced <see cref="Tier"/> and why — the numbers
        /// that drove the decision (SNR vs. the relevant threshold, separation vs. the relevant HFR multiple,
        /// flux ratio, box-vs-frame geometry), not just the rule's name.</summary>
        public string Reason { get; init; }
    }

    /// <summary>The output of <see cref="GoldenFromTruth.Build"/>: the golden sidecar itself plus the per-star
    /// disposition list that makes it debuggable.</summary>
    public sealed class GoldenFromTruthResult {

        /// <summary>The assembled golden frame — <see cref="GoldenFrame.SchemaVersion"/> 2,
        /// <see cref="GoldenFrame.Method"/> <c>"synthetic"</c>, ready for
        /// <see cref="GoldenStarSetStore.SaveForImage"/>.</summary>
        public GoldenFrame Frame { get; init; }

        /// <summary>One entry per star in the input truth list, in input order (so index <c>i</c> here
        /// describes <c>truth[i]</c> — also recorded redundantly as <see cref="SyntheticStarDisposition.TruthIndex"/>
        /// for consumers that only keep a subset).</summary>
        public IReadOnlyList<SyntheticStarDisposition> Dispositions { get; init; }
    }

    /// <summary>
    /// The golden policy — turns exact per-star render truth (<see cref="StarTruth"/>, native/pre-binning
    /// pixels) into a detector-independent golden star set (<see cref="GoldenFrame"/>, captured/binned pixels)
    /// via a pure, deterministic pipeline. Pure by design (no filesystem, no OpenCV, no NINA image types) so it
    /// is source-linkable into the Tests project exactly like <see cref="GoldenStarSet"/> already is.
    ///
    /// <para><b>Pipeline</b> (see design doc "G5. Golden policy — the substantive design" for the full spec this
    /// implements):</para>
    /// <list type="number">
    /// <item>Transform every star from native to binned (captured) pixels: <c>c_binned = (c+0.5)/bin − 0.5</c>;
    /// HFR/outer-radius/σ_min all ÷ bin.</item>
    /// <item>Compute each star's own peak-pixel SNR and, from it, an individual tier.</item>
    /// <item>Find close pairs with a spatial grid (not O(n²) — see <see cref="FindCandidatePairs"/>), union-find
    /// stars within the MERGE separation into one component per component, then evaluate BLEND/DOMINANCE
    /// interactions between the resulting components, not the raw stars — <see cref="Build"/>'s "Phase 4" is
    /// where that precedence (merge first, then blend/dominance between components) is enforced; see the
    /// design-decisions paragraph below for why.</item>
    /// <item>Size and place a box per surviving component; clip against the frame, demoting/dropping per the
    /// edge rules.</item>
    /// <item>Apply saturation as a final "unmissable" override, then assemble the <see cref="GoldenFrame"/> and
    /// the per-star <see cref="SyntheticStarDisposition"/> sidecar.</item>
    /// </list>
    ///
    /// <para><b>Design decisions the source spec leaves open</b> (documented here rather than silently picked —
    /// see the G5 task report for the full list): (1) a merged component's combined peak fraction is the
    /// FLUX-WEIGHTED MEAN of its members' individual peak fractions, not their sum — deliberately the more
    /// conservative (lower) SNR estimate for two PSFs whose peaks coincide, since golden generation should never
    /// OVER-claim detectability. (2) Component-pair separations that land back under the MERGE threshold (after
    /// flux-weighted centroiding pulls two independently-formed components close together) are left alone —
    /// merge components are a single deterministic pass over the raw stars, not an iterated fixed point, so this
    /// residual case is intentionally not a second merge. (3) A component whose own combined SNR already sits
    /// below <see cref="GoldenTierThresholds.UnresolvedSnr"/> never participates in blend/dominance interactions
    /// at all (neither as the demoted party nor as the one doing the demoting) — the spec states "do not promote
    /// an invisible star into unresolved" for the dominance case; this extends that symmetrically so an
    /// already-invisible neighbor cannot demote a visible star either.</para>
    /// </summary>
    public static class GoldenFromTruth {

        private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

        /// <summary>Native → binned (captured-pixel) coordinate transform: <c>(c + 0.5)/bin − 0.5</c>. Applies
        /// identically to X and Y. At <c>bin == 1</c> this is the identity.</summary>
        public static double ToBinnedCoordinate(double nativeCoordinate, int binning) {
            if (binning < 1) throw new ArgumentOutOfRangeException(nameof(binning), binning, "must be >= 1");
            return (nativeCoordinate + 0.5) / binning - 0.5;
        }

        /// <summary>
        /// Fraction of a star's total flux landing in one BINNED pixel: <c>min(1, kernelPeakFraction·bin²)</c>.
        /// The cap matters both ways — for an undersampled PSF (already concentrated pre-binning) binning can
        /// only ever concentrate flux further toward 1, never past it; for an oversampled donut, the ×bin² scale
        /// keeps the model honest as bin grows without ever claiming more than 100% of the flux in one pixel.
        /// </summary>
        public static double PeakFractionBinned(double kernelPeakFractionNative, int binning) {
            if (binning < 1) throw new ArgumentOutOfRangeException(nameof(binning), binning, "must be >= 1");
            return Math.Min(1.0, kernelPeakFractionNative * binning * binning);
        }

        /// <summary>Peak electrons in the brightest BINNED pixel: <c>flux · PeakFractionBinned(...)</c>.</summary>
        public static double PeakElectrons(double fluxElectrons, double kernelPeakFractionNative, int binning) =>
            fluxElectrons * PeakFractionBinned(kernelPeakFractionNative, binning);

        /// <summary>
        /// Background (sky + dark + read) noise σ, in electrons, for ONE BINNED pixel:
        /// <c>bin · √(sky_native + dark_native + readNoise²)</c>. The leading <c>bin ×</c> (rather than
        /// <c>bin² ×</c> inside the root) is because binning SUMS b² independent native pixels: shot variance
        /// (sky+dark, each Poisson) and read-noise variance both scale by b², so the summed σ scales by
        /// <c>√(b²·x) = b·√x</c>. This is a single frame-wide scalar — the render's noise model has no spatial
        /// structure at the fidelity this policy needs.
        /// </summary>
        public static double BackgroundSigmaElectrons(GoldenFrameNoiseModel noise) {
            if (noise == null) throw new ArgumentNullException(nameof(noise));
            if (noise.Binning < 1) throw new ArgumentOutOfRangeException(nameof(noise), noise.Binning, "Binning must be >= 1");
            var variancePerNative = noise.SkyElectronsPerNativePixel + noise.DarkElectronsPerNativePixel
                + noise.ReadNoiseElectrons * noise.ReadNoiseElectrons;
            return noise.Binning * Math.Sqrt(Math.Max(0.0, variancePerNative));
        }

        /// <summary>Peak-pixel SNR for one star in isolation: <c>PeakElectrons(...) / backgroundSigmaElectrons</c>.
        /// A merged component's COMBINED SNR is not this — see <see cref="GoldenFromTruth.Build"/>, which
        /// recomputes it from the component's summed flux and flux-weighted peak fraction.</summary>
        public static double PeakSnr(double fluxElectrons, double kernelPeakFractionNative, int binning, double backgroundSigmaElectrons) {
            if (!(backgroundSigmaElectrons > 0.0)) return double.PositiveInfinity;
            return PeakElectrons(fluxElectrons, kernelPeakFractionNative, binning) / backgroundSigmaElectrons;
        }

        /// <summary>
        /// Confidence tier for a peak SNR: <see cref="GoldenConfidence.High"/> at/above
        /// <see cref="GoldenTierThresholds.HighSnr"/>, <see cref="GoldenConfidence.Medium"/> down to
        /// <see cref="GoldenTierThresholds.MediumSnr"/>, <see cref="GoldenConfidence.Low"/> down to
        /// <see cref="GoldenTierThresholds.LowSnr"/>, or <c>null</c> below that (the caller decides between
        /// <see cref="SyntheticTier.Unresolved"/> and <see cref="SyntheticTier.Omitted"/> using
        /// <see cref="GoldenTierThresholds.UnresolvedSnr"/>, which this method does not consult).
        /// </summary>
        public static string TierLabelForSnr(double peakSnr, GoldenTierThresholds thresholds) {
            if (thresholds == null) throw new ArgumentNullException(nameof(thresholds));
            if (peakSnr >= thresholds.HighSnr) return GoldenConfidence.High;
            if (peakSnr >= thresholds.MediumSnr) return GoldenConfidence.Medium;
            if (peakSnr >= thresholds.LowSnr) return GoldenConfidence.Low;
            return null;
        }

        /// <summary>
        /// Box half-width (px, binned): <c>max(2·HFR, outerRadius + 3·σ_min, MinBoxHalfWidthPixels)</c>. The
        /// first term covers a point source's flux profile, the second a donut's full annulus, the third floors
        /// a heavily-oversampled in-focus source at a usable multi-pixel size. Actual box <c>w = h =
        /// 2·⌈half⌉</c> — computed by the caller, since the ceiling+doubling only matters once per component.
        /// </summary>
        public static double BoxHalfWidthPixels(double hfrBinnedPixels, double outerRadiusBinnedPixels, double sigmaMinBinnedPixels, GoldenTierThresholds thresholds) {
            if (thresholds == null) throw new ArgumentNullException(nameof(thresholds));
            var byHfr = 2.0 * hfrBinnedPixels;
            var byAnnulus = outerRadiusBinnedPixels + 3.0 * sigmaMinBinnedPixels;
            return Math.Max(Math.Max(byHfr, byAnnulus), thresholds.MinBoxHalfWidthPixels);
        }

        private static double FluxWeightedMean(double fluxA, double valueA, double fluxB, double valueB) {
            var total = fluxA + fluxB;
            return total > 0.0 ? (fluxA * valueA + fluxB * valueB) / total : (valueA + valueB) / 2.0;
        }

        /// <summary>
        /// One merge component: a group of input stars folded into a single box because they are too close
        /// together to be independently resolved. Every "combined" quantity is the FLUX-WEIGHTED MEAN across
        /// members (not a sum, except for <see cref="CombinedFlux"/> itself), consistent with how a real
        /// detector's flux-weighted centroid would land on overlapping light.
        /// </summary>
        private sealed class Component {
            public List<int> Members;
            public int RepresentativeIndex;
            public double CombinedFlux;
            public double CenterX;
            public double CenterY;
            public double HfrPixels;
            public double OuterRadiusPixels;
            public double PeakElectrons;
            public double CombinedSnr;
            public bool AnySaturated;
        }

        /// <summary>Minimal union-find (path compression + union by size) over star indices, used only to
        /// resolve the MERGE relation into components. Not exposed — an implementation detail of <see cref="Build"/>.</summary>
        private sealed class UnionFind {
            private readonly int[] parent;
            private readonly int[] size;

            public UnionFind(int n) {
                parent = new int[n];
                size = new int[n];
                for (var i = 0; i < n; ++i) { parent[i] = i; size[i] = 1; }
            }

            public int Find(int x) {
                while (parent[x] != x) {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            public void Union(int a, int b) {
                var ra = Find(a);
                var rb = Find(b);
                if (ra == rb) return;
                if (size[ra] < size[rb]) { (ra, rb) = (rb, ra); }
                parent[rb] = ra;
                size[ra] += size[rb];
            }
        }

        /// <summary>
        /// Candidate index pairs (i &lt; j, GLOBAL indices into <paramref name="x"/>/<paramref name="y"/>) whose
        /// Euclidean distance is at most <paramref name="maxReach"/> — a SUPERSET of the pairs the caller
        /// actually cares about (the caller re-checks the exact, pair-specific threshold), found via a uniform
        /// spatial grid rather than an O(n²) scan.
        ///
        /// <para><b>Complexity.</b> Cell size = <paramref name="maxReach"/>, so two points within that distance
        /// can only land in the same cell or one of its 8 neighbors (standard uniform-grid property: a distance
        /// ≤ cell width can cross at most one cell boundary per axis) — every candidate pair is found by scanning
        /// each occupied cell's 3×3 neighborhood. Memory is O(n) (only occupied cells are stored). Time is
        /// O(n·k) where k is the average number of stars within <paramref name="maxReach"/> of a given star —
        /// effectively linear for the star fields this generator produces, since <paramref name="maxReach"/> is
        /// a small multiple of the frame's own largest HFR (single-digit to low-double-digit px) against
        /// multi-thousand-px frames. The only way this degrades toward O(n²) is a field where a large fraction
        /// of all stars sit within one <paramref name="maxReach"/> of each other simultaneously, which would
        /// mean the frame is already saturated with overlapping stars everywhere — not a case any of the bank's
        /// datasets are built to produce.</para>
        /// </summary>
        private static List<(int A, int B)> FindCandidatePairs(IReadOnlyList<double> x, IReadOnlyList<double> y, double maxReach) {
            var result = new List<(int, int)>();
            var n = x.Count;
            if (n < 2 || !(maxReach > 0.0)) {
                return result;
            }

            var cells = new Dictionary<(long Cx, long Cy), List<int>>();
            for (var i = 0; i < n; ++i) {
                var key = (Cx: (long)Math.Floor(x[i] / maxReach), Cy: (long)Math.Floor(y[i] / maxReach));
                if (!cells.TryGetValue(key, out var list)) {
                    list = new List<int>();
                    cells[key] = list;
                }
                list.Add(i);
            }

            var maxReachSq = maxReach * maxReach;
            foreach (var entry in cells) {
                var here = entry.Value;
                for (var dcx = -1; dcx <= 1; ++dcx) {
                    for (var dcy = -1; dcy <= 1; ++dcy) {
                        if (!cells.TryGetValue((entry.Key.Cx + dcx, entry.Key.Cy + dcy), out var neighbor)) {
                            continue;
                        }
                        foreach (var i in here) {
                            foreach (var j in neighbor) {
                                if (j <= i) {
                                    continue; // global-index ordering both skips self-pairs and de-duplicates
                                              // each unordered pair across both traversal directions.
                                }
                                var dx = x[i] - x[j];
                                var dy = y[i] - y[j];
                                if (dx * dx + dy * dy <= maxReachSq) {
                                    result.Add((i, j));
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Runs the full golden policy over one frame's render truth. See the class remarks for the pipeline
        /// and the design decisions made where the source spec left the exact mechanics open.
        /// </summary>
        /// <param name="truth">Per-star render truth, native (pre-binning) pixels — one entry per star the
        /// compositor accepted, including off-frame wing-spill stars.</param>
        /// <param name="noise">The noise/geometry inputs <see cref="StarTruth"/> does not itself carry.</param>
        /// <param name="thresholds">Tier and interaction thresholds; pass <c>new GoldenTierThresholds()</c> for
        /// the design defaults.</param>
        /// <param name="imageFile">Provenance recorded on the output <see cref="GoldenFrame.ImageFile"/>.</param>
        /// <param name="focuserPosition">Recorded on the output <see cref="GoldenFrame.FocuserPosition"/>.</param>
        public static GoldenFromTruthResult Build(
                IReadOnlyList<StarTruth> truth,
                GoldenFrameNoiseModel noise,
                GoldenTierThresholds thresholds,
                string imageFile,
                int focuserPosition) {
            if (truth == null) throw new ArgumentNullException(nameof(truth));
            if (noise == null) throw new ArgumentNullException(nameof(noise));
            if (thresholds == null) throw new ArgumentNullException(nameof(thresholds));
            if (noise.Binning < 1) throw new ArgumentOutOfRangeException(nameof(noise), noise.Binning, "Binning must be >= 1");
            if (noise.FrameWidthNative <= 0 || noise.FrameHeightNative <= 0) {
                throw new ArgumentOutOfRangeException(nameof(noise), "FrameWidthNative/FrameHeightNative must be positive");
            }

            var n = truth.Count;
            var bin = noise.Binning;
            var backgroundSigma = BackgroundSigmaElectrons(noise);
            var saturationCeiling = thresholds.SaturationFraction * noise.DigitalSaturationElectrons;
            var sigmaMinBinned = noise.SigmaMinPixels / bin;
            var binnedWidth = noise.FrameWidthNative / bin;   // integer division — matches HocusFocusSimulatorCamera.BinFrame
            var binnedHeight = noise.FrameHeightNative / bin; // (drops a trailing partial row/column, same as the real binner).

            // ---- Phase 1: per-star native -> binned transform, individual peak SNR, individual saturation. ----
            var cx = new double[n];
            var cy = new double[n];
            var hfr = new double[n];
            var outerR = new double[n];
            var peakFrac = new double[n];
            var peakE = new double[n];
            var snr = new double[n];
            var sat = new bool[n];
            for (var i = 0; i < n; ++i) {
                var t = truth[i];
                cx[i] = ToBinnedCoordinate(t.CxPixels, bin);
                cy[i] = ToBinnedCoordinate(t.CyPixels, bin);
                // MeasuredHfrPixels (not AnalyticHfrPixels): the flux-weighted HFR actually measured on this
                // star's rasterized kernel is what a centroid-based detector would report, so it is the more
                // faithful stand-in for "what a detector sees" than the closed-form analytic value.
                hfr[i] = t.MeasuredHfrPixels / bin;
                outerR[i] = t.OuterRadiusPixels / bin;
                peakFrac[i] = PeakFractionBinned(t.KernelPeakFraction, bin);
                peakE[i] = t.FluxElectrons * peakFrac[i];
                snr[i] = backgroundSigma > 0.0 ? peakE[i] / backgroundSigma : double.PositiveInfinity;
                sat[i] = peakE[i] >= saturationCeiling;
            }

            // ---- Phase 2: candidate pairs (spatial grid) -> union-find merges + remembered blend candidates. ----
            var maxHfr = 0.0;
            for (var i = 0; i < n; ++i) { if (hfr[i] > maxHfr) maxHfr = hfr[i]; }
            // Bound by the BLEND multiple (the wider of the two interaction radii): a pair's own HFR_pair can
            // never exceed the frame's largest single-star HFR, so this one search radius safely finds every
            // pair that could qualify for EITHER the merge or the blend/dominance test below.
            var maxReach = thresholds.BlendSeparationHfrMultiple * maxHfr;
            var candidatePairs = FindCandidatePairs(cx, cy, maxReach);

            var uf = new UnionFind(n);
            var blendCandidates = new List<(int A, int B)>();
            foreach (var (i, j) in candidatePairs) {
                var hfrPair = FluxWeightedMean(truth[i].FluxElectrons, hfr[i], truth[j].FluxElectrons, hfr[j]);
                var dx = cx[i] - cx[j];
                var dy = cy[i] - cy[j];
                var s = Math.Sqrt(dx * dx + dy * dy);
                if (s < thresholds.MergeSeparationHfrMultiple * hfrPair) {
                    uf.Union(i, j);
                } else if (s < thresholds.BlendSeparationHfrMultiple * hfrPair) {
                    blendCandidates.Add((i, j));
                }
                // else: this SPECIFIC pair's own HFR_pair put it outside the blend band even though the
                // (coarser, global-max-based) grid search radius admitted it as a candidate -- independent.
            }

            // ---- Phase 3: build components from the union-find partition. ----
            var membersByRoot = new Dictionary<int, List<int>>();
            for (var i = 0; i < n; ++i) {
                var r = uf.Find(i);
                if (!membersByRoot.TryGetValue(r, out var list)) {
                    list = new List<int>();
                    membersByRoot[r] = list;
                }
                list.Add(i); // ascending TruthIndex order, since i runs 0..n-1.
            }

            var componentOfStar = new int[n];
            var components = new Dictionary<int, Component>();
            foreach (var kv in membersByRoot) {
                var members = kv.Value;
                foreach (var m in members) { componentOfStar[m] = kv.Key; }

                double fluxSum = 0, cxSum = 0, cySum = 0, hfrSum = 0, outerSum = 0, fracSum = 0;
                var anySat = false;
                var repIndex = members[0];
                var repFlux = truth[repIndex].FluxElectrons;
                foreach (var m in members) {
                    var f = truth[m].FluxElectrons;
                    fluxSum += f;
                    cxSum += f * cx[m];
                    cySum += f * cy[m];
                    hfrSum += f * hfr[m];
                    outerSum += f * outerR[m];
                    fracSum += f * peakFrac[m];
                    anySat |= sat[m];
                    if (f > repFlux) { repFlux = f; repIndex = m; } // first (lowest-index) max wins ties.
                }

                var combCx = fluxSum > 0.0 ? cxSum / fluxSum : members.Average(m => cx[m]);
                var combCy = fluxSum > 0.0 ? cySum / fluxSum : members.Average(m => cy[m]);
                var combHfr = fluxSum > 0.0 ? hfrSum / fluxSum : members.Average(m => hfr[m]);
                var combOuter = fluxSum > 0.0 ? outerSum / fluxSum : members.Average(m => outerR[m]);
                // Combined peak fraction is the flux-weighted MEAN of the members' own peak fractions, not
                // their sum. This deliberately under- rather than over-estimates the true combined peak for
                // two PSFs whose brightest pixels coincide (real overlapping light would sum there, pushing the
                // true peak above this estimate) -- a golden generator should never over-claim detectability by
                // constructing an SNR neither the model nor a real detector could actually observe.
                var combFrac = fluxSum > 0.0 ? fracSum / fluxSum : members.Average(m => peakFrac[m]);
                var combPeakElectrons = fluxSum * combFrac;
                var combSnr = backgroundSigma > 0.0 ? combPeakElectrons / backgroundSigma : double.PositiveInfinity;

                components[kv.Key] = new Component {
                    Members = members,
                    RepresentativeIndex = repIndex,
                    CombinedFlux = fluxSum,
                    CenterX = combCx,
                    CenterY = combCy,
                    HfrPixels = combHfr,
                    OuterRadiusPixels = combOuter,
                    PeakElectrons = combPeakElectrons,
                    CombinedSnr = combSnr,
                    // Beyond the literal per-star saturation check (Phase 1): also flag a component whose
                    // COMBINED peak crosses the ceiling, even when no single member does alone. This is an
                    // approximation in the same conservative direction as combFrac above, not an exact
                    // pixel-level re-render of the merged light.
                    AnySaturated = anySat || combPeakElectrons >= saturationCeiling
                };
            }

            // ---- Phase 4: resolve blend/dominance between COMPONENTS (not raw stars) -- see class remarks. ----
            var demoted = new HashSet<int>();
            var evaluatedComponentPairs = new HashSet<(int, int)>();
            foreach (var (i, j) in blendCandidates) {
                var ri = componentOfStar[i];
                var rj = componentOfStar[j];
                if (ri == rj) {
                    continue; // already one component via a different merge chain -- one box, no interaction with itself.
                }
                var key = ri < rj ? (ri, rj) : (rj, ri);
                if (!evaluatedComponentPairs.Add(key)) {
                    continue; // this component pair was already evaluated via another member-star pair.
                }

                var a = components[key.Item1];
                var b = components[key.Item2];
                var hfrPair = FluxWeightedMean(a.CombinedFlux, a.HfrPixels, b.CombinedFlux, b.HfrPixels);
                var dx = a.CenterX - b.CenterX;
                var dy = a.CenterY - b.CenterY;
                var s = Math.Sqrt(dx * dx + dy * dy);

                if (s < thresholds.MergeSeparationHfrMultiple * hfrPair) {
                    // Residual case: after flux-weighted centroiding, two independently-formed components landed
                    // back inside the merge radius. Deliberately NOT re-merged -- see class remarks point (2).
                    continue;
                }
                if (s >= thresholds.BlendSeparationHfrMultiple * hfrPair) {
                    continue; // independent at the component level.
                }

                // A component already below the visibility floor never participates, in either role -- see
                // class remarks point (3).
                if (a.CombinedSnr < thresholds.UnresolvedSnr || b.CombinedSnr < thresholds.UnresolvedSnr) {
                    continue;
                }

                var ratio = Math.Max(a.CombinedFlux, b.CombinedFlux) / Math.Min(a.CombinedFlux, b.CombinedFlux);
                if (ratio < thresholds.DominanceFluxRatio) {
                    demoted.Add(key.Item1);
                    demoted.Add(key.Item2);
                } else {
                    demoted.Add(a.CombinedFlux <= b.CombinedFlux ? key.Item1 : key.Item2);
                }
            }

            // ---- Phase 5: final bucket per component (SNR floor -> tier/demotion -> box geometry/clipping -> saturation override), then assemble. ----
            var dispositions = new SyntheticStarDisposition[n];
            var starBoxes = new List<(int Rep, GoldenStarBox Box)>();
            var unresolvedBoxes = new List<(int Rep, GoldenStarBox Box)>();
            var coverageCounts = new Dictionary<string, int> {
                [GoldenConfidence.High] = 0,
                [GoldenConfidence.Medium] = 0,
                [GoldenConfidence.Low] = 0
            };

            foreach (var componentEntry in components) {
                var root = componentEntry.Key;
                var comp = componentEntry.Value;
                var members = comp.Members;

                string bucket; // "stars" | "unresolved" | "omitted"
                string reason;
                GoldenStarBox box = null;
                string nativeTier = null;

                if (!comp.AnySaturated && comp.CombinedSnr < thresholds.UnresolvedSnr) {
                    bucket = "omitted";
                    reason = $"peak SNR {F(comp.CombinedSnr)} < unresolvedSnr {F(thresholds.UnresolvedSnr)}"
                        + (members.Count > 1 ? $" (combined over {members.Count} merged stars)" : "")
                        + " -> omitted; a detection here should count as a false positive";
                } else {
                    nativeTier = comp.AnySaturated ? GoldenConfidence.High : TierLabelForSnr(comp.CombinedSnr, thresholds);
                    var isDemoted = !comp.AnySaturated && demoted.Contains(root);

                    var half = BoxHalfWidthPixels(comp.HfrPixels, comp.OuterRadiusPixels, sigmaMinBinned, thresholds);
                    var w = 2.0 * Math.Ceiling(half);
                    var h = w;
                    var rawX = comp.CenterX - w / 2.0;
                    var rawY = comp.CenterY - h / 2.0;
                    var ix0 = Math.Max(rawX, 0.0);
                    var iy0 = Math.Max(rawY, 0.0);
                    var ix1 = Math.Min(rawX + w, binnedWidth);
                    var iy1 = Math.Min(rawY + h, binnedHeight);
                    var empty = ix1 <= ix0 || iy1 <= iy0;
                    var clipped = !empty && (ix0 > rawX || iy0 > rawY || ix1 < rawX + w || iy1 < rawY + h);

                    if (empty) {
                        bucket = "omitted";
                        reason = $"box [{F(rawX)},{F(rawY)},{F(w)},{F(h)}] does not intersect the {binnedWidth}x{binnedHeight} frame -> omitted (dropped entirely)";
                    } else {
                        box = new GoldenStarBox {
                            X = clipped ? ix0 : rawX,
                            Y = clipped ? iy0 : rawY,
                            W = clipped ? ix1 - ix0 : w,
                            H = clipped ? iy1 - iy0 : h,
                            Confidence = nativeTier
                        };
                        if (clipped) {
                            bucket = "unresolved";
                            reason = $"box clipped by frame edge (raw [{F(rawX)},{F(rawY)},{F(w)},{F(h)}] vs frame {binnedWidth}x{binnedHeight}) -> unresolved"
                                + (nativeTier != null ? $" (would have been {nativeTier})" : "");
                        } else if (nativeTier == null || isDemoted) {
                            bucket = "unresolved";
                            reason = isDemoted
                                ? $"peak SNR {F(comp.CombinedSnr)} (tier {nativeTier}) demoted by a blend/dominance interaction with a neighboring component -> unresolved"
                                : $"peak SNR {F(comp.CombinedSnr)} in [{F(thresholds.UnresolvedSnr)},{F(thresholds.LowSnr)}) -> unresolved (neither a required find nor a false positive)";
                        } else {
                            bucket = "stars";
                            reason = comp.AnySaturated
                                ? $"peak electrons {F(comp.PeakElectrons)} >= {F(thresholds.SaturationFraction)}x{F(noise.DigitalSaturationElectrons)} digital saturation -> forced tier high (unmissable)"
                                : $"peak SNR {F(comp.CombinedSnr)} -> tier {nativeTier}";
                        }
                    }

                    if (nativeTier != null) {
                        coverageCounts[nativeTier]++;
                    }
                }

                if (bucket == "stars") {
                    starBoxes.Add((comp.RepresentativeIndex, box));
                } else if (bucket == "unresolved") {
                    unresolvedBoxes.Add((comp.RepresentativeIndex, box));
                }

                var repTier = bucket == "stars" ? nativeTier : bucket == "unresolved" ? SyntheticTier.Unresolved : SyntheticTier.Omitted;
                foreach (var m in members) {
                    if (m == comp.RepresentativeIndex) {
                        dispositions[m] = new SyntheticStarDisposition {
                            TruthIndex = m,
                            Star = truth[m],
                            BinnedCenterX = comp.CenterX,
                            BinnedCenterY = comp.CenterY,
                            BinnedHfrPixels = comp.HfrPixels,
                            PeakSnr = comp.CombinedSnr,
                            Tier = repTier,
                            MergedIntoTruthIndex = null,
                            Saturated = comp.AnySaturated,
                            Reason = (members.Count > 1 ? $"representative of a {members.Count}-star merged component; " : "") + reason
                        };
                    } else {
                        var dx = cx[m] - comp.CenterX;
                        var dy = cy[m] - comp.CenterY;
                        var distToCentroid = Math.Sqrt(dx * dx + dy * dy);
                        dispositions[m] = new SyntheticStarDisposition {
                            TruthIndex = m,
                            Star = truth[m],
                            BinnedCenterX = cx[m],
                            BinnedCenterY = cy[m],
                            BinnedHfrPixels = hfr[m],
                            PeakSnr = snr[m],
                            Tier = SyntheticTier.MergedInto,
                            MergedIntoTruthIndex = comp.RepresentativeIndex,
                            Saturated = sat[m],
                            Reason = $"merged into truth#{comp.RepresentativeIndex} (own peak SNR {F(snr[m])}, "
                                + $"{members.Count}-star component centred at ({F(comp.CenterX)},{F(comp.CenterY)}), "
                                + $"own distance to component centroid {F(distToCentroid)}px)"
                        };
                    }
                }
            }

            var coverage = new Dictionary<string, GoldenTierCoverage> {
                [GoldenConfidence.High] = new GoldenTierCoverage { Examined = coverageCounts[GoldenConfidence.High], Total = coverageCounts[GoldenConfidence.High] },
                [GoldenConfidence.Medium] = new GoldenTierCoverage { Examined = coverageCounts[GoldenConfidence.Medium], Total = coverageCounts[GoldenConfidence.Medium] },
                [GoldenConfidence.Low] = new GoldenTierCoverage { Examined = coverageCounts[GoldenConfidence.Low], Total = coverageCounts[GoldenConfidence.Low] }
            };

            var frame = new GoldenFrame {
                ImageFile = imageFile,
                FocuserPosition = focuserPosition,
                SchemaVersion = 2,
                Method = "synthetic",
                Stars = starBoxes.OrderBy(t => t.Rep).Select(t => t.Box).ToList(),
                Unresolved = unresolvedBoxes.OrderBy(t => t.Rep).Select(t => t.Box).ToList(),
                Coverage = coverage
            };

            return new GoldenFromTruthResult { Frame = frame, Dispositions = dispositions };
        }
    }
}
