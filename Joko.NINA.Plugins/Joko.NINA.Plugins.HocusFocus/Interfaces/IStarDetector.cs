#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Core.Model;
using NINA.Image.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.ComponentModel;
using NINA.Joko.Plugins.HocusFocus.Converters;
using System.Drawing;
using Point = OpenCvSharp.Point;
using NINA.Image.ImageAnalysis;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum StarDetectorPSFFitType {

        [Description("Moffat 4.0")]
        Moffat_40,

        [Description("Gaussian")]
        Gaussian,

        [Description("Moffat 2.5")]
        Moffat_25,

        [Description("Moffat 1.5")]
        Moffat_15,

        [Description("Moffat (β fittable)")]
        MoffatFittable
    }

    public class RatioRect {
        public static readonly RatioRect Full = new RatioRect(0.0d, 0.0d, 1.0d, 1.0d);

        public RatioRect(double startX, double startY, double width, double height) {
            if (startX < 0.0d || startX >= 1.0d) {
                throw new ArgumentException($"StartX must be a ratio, between [0, 1)", "startX");
            }
            if (startY < 0.0d || startY >= 1.0d) {
                throw new ArgumentException($"StartY must be a ratio, between [0, 1)", "startY");
            }
            if (height <= 0.0d) {
                throw new ArgumentException($"Height must be a positive ratio", "height");
            }
            if (width <= 0.0d) {
                throw new ArgumentException($"Width must be a positive ratio", "width");
            }
            this.StartX = startX;
            this.StartY = startY;
            this.Width = Math.Min(width, 1.0d - startX);
            this.Height = Math.Min(height, 1.0d - startY);
        }

        public double StartX { get; private set; }
        public double StartY { get; private set; }
        public double Height { get; private set; }
        public double Width { get; private set; }

        public double EndExclusiveX() {
            return StartX + Width;
        }

        public double EndExclusiveY() {
            return StartY + Height;
        }

        public bool Contains(RatioRect inner) {
            if (StartX > inner.StartX || StartY > inner.StartY) {
                return false;
            }
            if (EndExclusiveX() < inner.EndExclusiveX() || EndExclusiveY() < inner.EndExclusiveY()) {
                return false;
            }
            return true;
        }

        public bool Contains(Rectangle inner, System.Drawing.Size fullSize) {
            return Contains(FromRectangle(inner, fullSize));
        }

        public bool IsFull() {
            return Width >= 1.0d && Height >= 1.0d;
        }

        public override string ToString() {
            return $"{{{nameof(StartX)}={StartX.ToString()}, {nameof(StartY)}={StartY.ToString()}, {nameof(Height)}={Height.ToString()}, {nameof(Width)}={Width.ToString()}}}";
        }

        // Culture-invariant canonical form used by StarDetectorParams.ToCanonicalCacheString(). Captures the
        // full geometry (StartX/StartY/Width/Height) formatted with InvariantCulture so the cache key is stable
        // across locales. Distinct from ToString(), which is for human/debug display and is not locale-safe.
        public string ToCanonicalString() {
            return $"{{StartX={StartX.ToString(CultureInfo.InvariantCulture)},StartY={StartY.ToString(CultureInfo.InvariantCulture)},Width={Width.ToString(CultureInfo.InvariantCulture)},Height={Height.ToString(CultureInfo.InvariantCulture)}}}";
        }

        public static RatioRect FromCenterROI(double roi) {
            return new RatioRect(
                (1.0d - roi) / 2.0,
                (1.0d - roi) / 2.0,
                roi,
                roi);
        }

        public static RatioRect FromRectangle(Rectangle rect, System.Drawing.Size fullSize) {
            return new RatioRect(
                rect.X / (double)fullSize.Width,
                rect.Y / (double)fullSize.Height,
                rect.Width / (double)fullSize.Width,
                rect.Height / (double)fullSize.Height);
        }

        public Rectangle ToRectangle(System.Drawing.Size fullSize) {
            return new Rectangle(
                x: (int)Math.Round(StartX * fullSize.Width),
                y: (int)Math.Round(StartY * fullSize.Height),
                width: (int)Math.Round(Width * fullSize.Width),
                height: (int)Math.Round(Height * fullSize.Height));
        }

        public Rect2d ToRect2D(System.Drawing.Size fullSize) {
            return new Rect2d(
                x: (int)Math.Round(StartX * fullSize.Width),
                y: (int)Math.Round(StartY * fullSize.Height),
                width: (int)Math.Round(Width * fullSize.Width),
                height: (int)Math.Round(Height * fullSize.Height));
        }

        public System.Windows.Int32Rect ToInt32Rect(System.Drawing.Size fullSize) {
            return new System.Windows.Int32Rect(
                x: (int)Math.Round(StartX * fullSize.Width),
                y: (int)Math.Round(StartY * fullSize.Height),
                width: (int)Math.Round(Width * fullSize.Width),
                height: (int)Math.Round(Height * fullSize.Height));
        }
    }

    public class StarDetectionRegion {
        public static readonly StarDetectionRegion Full = new StarDetectionRegion(RatioRect.Full);

        public StarDetectionRegion(RatioRect outerBoundary, int index = 0) : this(outerBoundary, null, index) {
        }

        // Marked so Newtonsoft can deserialize this type (it has two parameterized constructors and otherwise cannot
        // choose one). Used by AutoFocus replay metadata.json round-tripping; param names match the property names.
        [Newtonsoft.Json.JsonConstructor]
        public StarDetectionRegion(RatioRect outerBoundary, RatioRect innerCropBoundary, int index = 0) {
            if (outerBoundary == null) {
                throw new ArgumentException("outerBoundary cannot be null", "outerBoundary");
            }
            if (innerCropBoundary != null) {
                if (!outerBoundary.Contains(innerCropBoundary)) {
                    throw new ArgumentException($"Inner crop boundary ({innerCropBoundary}) must be entirely contained within outer crop boundary ({outerBoundary})", "innerCropBoundary");
                }
            }

            this.OuterBoundary = outerBoundary;
            this.InnerCropBoundary = innerCropBoundary;
            this.Index = index;
        }

        public int Index { get; set; }
        public RatioRect OuterBoundary { get; private set; }
        public RatioRect InnerCropBoundary { get; private set; }

        public override string ToString() {
            return $"{{{nameof(OuterBoundary)}={OuterBoundary}, {nameof(InnerCropBoundary)}={InnerCropBoundary}}}";
        }

        // Culture-invariant canonical form used by StarDetectorParams.ToCanonicalCacheString(). Captures Index
        // plus both boundaries' full invariant geometry. Changing the region (outer/inner boundary or index)
        // changes the detected-star results, so it must change the cache key.
        public string ToCanonicalString() {
            var inner = InnerCropBoundary == null ? "null" : InnerCropBoundary.ToCanonicalString();
            return $"{{Index={Index.ToString(CultureInfo.InvariantCulture)},Outer={OuterBoundary.ToCanonicalString()},Inner={inner}}}";
        }

        public bool IsFull() {
            return InnerCropBoundary == null && OuterBoundary.IsFull();
        }

        public static StarDetectionRegion FromStarDetectionParams(StarDetectionParams p) {
            var starDetectionRegion = StarDetectionRegion.Full;
            if (p.UseROI && p.InnerCropRatio < 1.0 && p.OuterCropRatio > 0.0) {
                var outerCropRatio = p.OuterCropRatio >= 1.0 ? p.InnerCropRatio : p.OuterCropRatio;
                var outerRegion = RatioRect.FromCenterROI(outerCropRatio);
                RatioRect innerCropRegion = null;
                if (p.OuterCropRatio < 1.0) {
                    innerCropRegion = RatioRect.FromCenterROI(p.InnerCropRatio);
                }
                starDetectionRegion = new StarDetectionRegion(outerRegion, innerCropRegion);
            }
            return starDetectionRegion;
        }
    }

    // How the τ clip (StarClippingMultiplier × measurement-image σ) is applied inside MeasureStar's flux sum.
    // SubtractTau subtracts τ from every surviving pixel (legacy soft-threshold; biases HFR low on stars with a
    // radial gradient — accuracy analysis F3 — but suppresses one-sided noise at large radii more aggressively).
    // GateOnly uses τ purely as an inclusion gate, matching the convention of the iterative centroid and the
    // star-parameter computation. The production default is GateOnly at τ=2.0σ, chosen empirically — see
    // docs/sigma-consistency-f3-results.md.
    public enum TauClipPolicy {
        SubtractTau,
        GateOnly
    }

    public class StarDetectorParams {
        public bool HotpixelFiltering { get; set; } = true;

        // If enabled, applies hotpixel filtering only if the difference exceeds a threshold. This is more computationally intensive, but limits blurring to more likely
        // hotpixel candidates
        public bool HotpixelThresholdingEnabled { get; set; } = true;

        // The threshold limit for Hotpixel thresholding. If the median value for a pixel's neighbors differs from the actual pixel value by this percent
        // of the max ADU, then the pixel is replaced
        public double HotpixelThreshold { get; set; } = 0.001;

        // If this is true, then the source image used for star measurement has the noise reduction settings applied to it. Otherwise, noise reduction is done only on the structure map
        // Default mirrors the StarDetectionOptions default so the class-default bundle stays self-consistent (F4): sharp measurement + honest σ + the compensated 0.4/2.0 knob defaults below.
        public bool StarMeasurementNoiseReductionEnabled { get; set; } = false;

        // Half size in pixels of a Gaussian convolution filter used for noise reduction. This is useful for low-SNR images
        // Setting this value also implies hotpixel filtering is enabled, since otherwise we would blend the hot pixels into their neighbors
        public int NoiseReductionRadius { get; set; } = 3;

        // Number of noise standard deviations above the median to binarize the structure map containing star candidates. Increasing this is useful for noisy images to reduce
        // spurious detected stars in combination with light noise reduction. Default lowered 4.0→2.0 per the
        // golden-set recall audit: at 4σ ~79% of real stars never formed a candidate; 2σ ~triples recall@SNR≥12
        // at stable best-focus and only modest HFR scatter. See docs/star-detection-golden-audit-cwhite-results.md.
        public double NoiseClippingMultiplier { get; set; } = 2.0;

        // Spatially-adaptive structure-map binarization (opt-in, default OFF for bit-identical detection). When OFF
        // the structure map is binarized against the legacy SINGLE global scalar threshold
        // (global-median + NoiseClippingMultiplier · global-σ), so candidate formation is byte-for-byte legacy.
        // When ON the scalar is replaced by a spatially-varying threshold surface
        // (local-median(x,y) + NoiseClippingMultiplier · local-σ(x,y)) computed from robust per-block statistics on
        // a coarse grid and bilinearly upsampled — making the SAME NoiseClippingMultiplier locally fair (low where
        // the background is clean, high where it is noisy). local-σ is sampled on the same noise-reduced source the
        // global σ uses (F4 σ-consistency) and local-median on the structure map actually thresholded; with constant
        // grids this reduces exactly to the global formula. EARLY param (it changes candidate formation): listed in
        // StarDetector.EarlyCacheKeyProperties. See docs/adaptive-noiseclip-design.md.
        // Default ON: AF-bank validated (recall@SNR≥12 + precision both improved at NC=2, no AF/donut regression —
        // see docs/af-bank-noiseclip-sweep-results.md). Retained as a user/regression off-switch.
        public bool LocallyAdaptiveBinarization { get; set; } = true;

        // EARLY adaptive-binarization knob (only when LocallyAdaptiveBinarization is true). Side length (px) of the
        // square blocks the robust local median/σ surface is estimated on (matches tools/golden/snr_ref.py coarse_bg,
        // grid 128). Larger ⇒ smoother surface (less local adaptivity, cheaper); smaller ⇒ more adaptive but noisier
        // and risks tracking real extended structure into the background. Sane range 64–256. EARLY param (changes
        // candidate formation): listed in StarDetector.EarlyCacheKeyProperties. Ignored entirely when the flag is OFF.
        public int AdaptiveNoiseBlockSize { get; set; } = 128;

        // Number of measurement-image noise standard deviations above the local background median to filter star
        // candidate pixels out from star consideration and HFR analysis. σ is measured on the image actually
        // sampled (F4) and the level + gate-only policy were chosen empirically — see
        // docs/sigma-consistency-f3-results.md (was 2.0 against a smoothed σ before F4; the empirically chosen
        // honest level is also 2.0).
        public double StarClippingMultiplier { get; set; } = 2.0;

        // See TauClipPolicy. Applies only inside MeasureStar; the centroid and star-parameter clip sites are
        // gate-only by construction.
        public TauClipPolicy HfrTauPolicy { get; set; } = TauClipPolicy.GateOnly;

        public double ContaminationSensitivity { get; set; } = 5.0;

        // When true (default), stars flagged as contaminated by the gradient-robust test are rejected
        // outright (quality gate) rather than merely flagged, keeping HFR/PSF statistics clean of
        // one-sided contaminants. When false, contaminated stars are kept and only flagged.
        public bool RejectContaminatedStars { get; set; } = true;

        // Diagnostics opt-in. When true, the detector records a per-star ContaminationDiagnosticRecord for
        // every accepted star (see HocusFocusStarDetectorResult.ContaminationDiagnostics). Off by default so
        // production NINA runs incur zero extra allocations or math. Excluded from ToString().
        public bool CollectContaminationDiagnostics { get; set; } = false;

        // Diagnostics opt-in. When true, the detector records a RejectedCandidateRecord for every candidate that a
        // late gate rejects — the gate name PLUS the scalar it compared against its threshold (sensitivity ratio,
        // fill-ratio, HFR, etc.) — into HocusFocusStarDetectorResult.RejectedCandidates. This is what the
        // label-driven gate recommender inverts ("how far must threshold X move to recover these labeled stars").
        // Off by default ⇒ the bag is null and not one allocation/branch-with-work runs on the production path, so
        // detection stays bit-identical. Excluded from the cache key (output-neutral side channel).
        public bool CollectRejectedCandidateDiagnostics { get; set; } = false;

        // Logging suppression opt-in (output-neutral). When true, the detector skips the per-region
        // "Average HFR / Detected Stars" INFO summary in HocusFocusStarDetection.BuildStarDetectionResult. The
        // optimizer wizard sets this on its seed/baseline params (RunEvaluationLoader) so a single optimization —
        // which re-runs detection thousands of times — does not flood the NINA log with ~10k+ INFO lines; normal
        // autofocus leaves it false and keeps the summary. Affects logging only (no detected-star value or
        // accept/reject decision changes), so it is excluded from the cache key below.
        public bool SuppressInfoLogging { get; set; } = false;

        // Half size of a median box filter, used for hotpixel removal if HotpixelFiltering is enabled. Only 1 is supported for now, since OpenCV has native support for
        // a median box filter but not a general circular one
        public int HotpixelFilterRadius { get; set; } = 1;

        // Number of wavelet layers for structure detection
        public int StructureLayers { get; set; } = 4;

        // Defocus-aware structure detection (opt-in, default OFF). When true, the à-trous wavelet residual that is
        // subtracted to remove large-scale structure is computed at StructureLayers + StructureLayerBoost layers,
        // so the residual is COARSER and large/donut (heavily defocused) stars survive the subtraction and form
        // candidates instead of being erased. The post-wavelet blur stays keyed to StructureLayers. When OFF the
        // effective layer count is exactly StructureLayers, so candidate formation is bit-identical. EARLY param
        // (changes candidate formation): listed in StarDetector.EarlyCacheKeyProperties.
        public bool DefocusAwareStructure { get; set; } = false;

        // Extra wavelet layers added to StructureLayers for the residual-subtraction step when
        // DefocusAwareStructure is on. 0 ⇒ no change even if the flag is on. Ignored entirely when the flag is OFF.
        public int StructureLayerBoost { get; set; } = 0;

        // Size of the circle used to dilate the structure map
        public int StructureDilationSize { get; set; } = 3;

        // Number of times to perform dilation on the structure map
        public int StructureDilationCount { get; set; } = 0;

        // Sensitivity is the minimum value of a star's brightness (with the background n subtracted out) above the
        // noise floor (s - b)/n, with n measured on the image actually sampled (F4). Smaller values increase
        // sensitivity. The default compensates for the removed σ understatement (~4× for white noise at the
        // default radius; hotpixel filtering compresses the ratio, and the ×0.2 constant is arbitrated by the
        // real-data before/after sweep) — was 10.0 against a smoothed σ
        public double Sensitivity { get; set; } = 2.0;

        // Maximum ratio of median pixel value to the peak for a candidate pixel to be rejected. Large values are more tolerant of flat structures
        public double PeakResponse { get; set; } = 0.75;

        // Maximum distortion allowed in each star, which is the ratio of "area" (number of pixels) to the area of a perfect square bounding box. A perfect
        // circle has distortion PI/4 which is about 0.8. Smaller values are more distorted
        public double MaxDistortion { get; set; } = 0.5;

        // Opt-in (default OFF for bit-identical detection). When true, the TooDistorted gate's effective
        // fill-ratio threshold is relaxed for LARGE candidates (proxy for large defocus): at large defocus a
        // star becomes a donut/annulus (central-obstruction shadow) → large bbox, low fill-ratio → today's
        // strict MaxDistortion wrongly rejects it as TooDistorted. The effective threshold is
        //   MaxDistortion * clamp(DefocusDistortionSizeReference / candidateSize, DefocusDistortionMinFactor, 1.0)
        // where candidateSize = max(bbox.Width, bbox.Height) (the same d the gate already uses). Candidates at or
        // below the size reference keep the strict MaxDistortion (factor = 1.0); larger candidates get a more
        // permissive threshold toward the MinFactor floor. See ComputeEffectiveMaxDistortion. LATE-gate param:
        // it changes only the late TooDistorted decision, so it is excluded from the early cache key.
        public bool DefocusAwareDistortion { get; set; } = false;

        // The candidate bbox max-dimension (px) at/below which the strict MaxDistortion threshold applies (the
        // factor is exactly 1.0). Above it the effective threshold relaxes. Only consulted when
        // DefocusAwareDistortion is true. Default 30 px tuned on the Panos wide-range AF run (Focuser 44396
        // donuts): at 30 the most-defocused donuts (≈36–49 px bbox) clear the distortion gate while the closest
        // in-sweep frame's accepted count stays sane (no junk flood); lowering it toward 20 recovers the last
        // couple of smaller donuts but inflates moderately-defocused frames.
        public double DefocusDistortionSizeReference { get; set; } = 30.0;

        // The floor multiplier on MaxDistortion for very large candidates (the most permissive the gate ever
        // becomes). Only consulted when DefocusAwareDistortion is true. Must be in (0, 1].
        public double DefocusDistortionMinFactor { get; set; } = 0.25;

        // Opt-in (default OFF for bit-identical detection). Companion to DefocusAwareDistortion. When true, the
        // NotCentered gate's effective StarCenterTolerance is RELAXED (the centered acceptance sub-box grows) for
        // LARGE candidates (proxy for large defocus): a defocused donut's hollow ring makes its intensity-weighted
        // centroid wobble away from the bbox center, so today's strict StarCenterTolerance wrongly rejects it as
        // NotCentered even after the distortion gate admits it. The effective tolerance is
        //   StarCenterTolerance * clamp(candidateSize / DefocusDistortionSizeReference, 1.0, DefocusCenteringToleranceFactor)
        // clamped to <= 1.0 (the max valid tolerance, where the sub-box covers the whole bbox). candidateSize =
        // max(bbox.Width, bbox.Height) and the SAME DefocusDistortionSizeReference defocus proxy is reused.
        // Candidates at or below the size reference keep the strict tolerance (factor = 1.0); larger candidates get
        // a larger (more permissive) tolerance toward DefocusCenteringToleranceFactor. See
        // ComputeEffectiveStarCenterTolerance. LATE-gate param: it changes only the late NotCentered decision, so
        // it is excluded from the early cache key.
        public bool DefocusAwareCentering { get; set; } = false;

        // The max multiplier applied to StarCenterTolerance for very large candidates (the most permissive the
        // NotCentered gate ever becomes). Only consulted when DefocusAwareCentering is true. Must be >= 1.0
        // (>1 relaxes; 1.0 is a no-op). The resulting effective tolerance is additionally clamped to <= 1.0 so it
        // never exceeds the whole bbox. Default 2.0 tuned on the Panos wide-range AF run: doubling the centering
        // sub-box recovers the ring-unstable donut centroids the distortion gate admits, without ballooning
        // near-focus accepted/NotCentered counts (near-focus candidates stay <= the size reference, so factor 1.0).
        public double DefocusCenteringToleranceFactor { get; set; } = 2.0;

        // MASTER flag for defocus-aware DONUT recovery + diffraction-spike/bloom suppression (opt-in, default OFF
        // for bit-identical detection). Gated by the StarDetectionOptions.DefocusAwareDonutDetection profile option,
        // which (when OFF) ALSO forces DefocusAwareDistortion/Centering/Structure off in BuildStarDetectorParams.
        // When OFF, none of the donut code paths run, so detection is byte-for-byte legacy. EARLY param (it gates
        // the early morph-close): listed in StarDetector.EarlyCacheKeyProperties.
        public bool DefocusAwareDonutDetection { get; set; } = false;

        // EARLY donut-recovery knob (only when DefocusAwareDonutDetection is true). Ellipse kernel DIAMETER (px) for
        // the morphological CLOSE of the binarized structure map applied before candidate collection, reconnecting
        // fragmented donut-ring arcs into one candidate (fixes TooSmall fragmentation). <= 1 ⇒ no close. EARLY param
        // (changes candidate formation): listed in StarDetector.EarlyCacheKeyProperties.
        public int DonutMorphCloseSize { get; set; } = 5;

        // LATE donut-recovery knob (only when DefocusAwareDonutDetection is true). Minimum enclosed-hole area (as a
        // fraction of the candidate bbox area) for a candidate to be treated as an annular donut whose hole pixel
        // COUNT is added to the fill-ratio used by the TooDistorted gate (so a hollow ring is judged like a filled
        // disk). DETECTION-ONLY: the measured starPoints are NOT mutated, so flux/HFR/centroid stay byte-identical.
        // See StarDetector.CountEnclosedHole. LATE param (excluded from the early cache key).
        public double DonutMinAnnularityHoleFraction { get; set; } = 0.15;

        // LATE spike-suppression knob (only when DefocusAwareDonutDetection is true). Reject a candidate as a
        // diffraction spike / satellite trail when its point-cloud second-moment eccentricity >= this value. A
        // perfect line has eccentricity → 1.0, so 1.0 (the default) DISABLES the gate; lower it (e.g. 0.95) to
        // enable. LATE param (excluded from the early cache key).
        public double DonutMaxStreakEccentricity { get; set; } = 1.0;

        // LATE spike-suppression knob (only when DefocusAwareDonutDetection is true). Reject candidates whose
        // centroid lies within this many px of a saturated source (peak >= SaturationThreshold), suppressing
        // bloom/halo fragments around a bright saturated star. 0 ⇒ OFF. LATE param (excluded from the early cache key).
        public double DonutSaturationBloomRadius { get; set; } = 0.0;

        // Size (as a ratio) of a centered rectangle within the star bounding box that the star center must be in. 1.0 covers the whole region, and 0.0 will fail every star
        public double StarCenterTolerance { get; set; } = 0.3;

        // The background is estimated by looking in an area around the star bounding box, increased on each side by this number of pixels
        public int BackgroundBoxExpansion { get; set; } = 3;

        // The minimum allowed size (length of either side) of a star candidate's bounding box. Increasing this can be helpful at very high focal lengths if too many small structures
        // are detected
        public int MinimumStarBoundingBoxSize { get; set; } = 5;

        // Minimum HFR for a star to be considered viable. The old 1.5 floor was calibrated against faint-star
        // HFRs inflated ~1.24× by one-sided noise rectification at the legacy soft-threshold τ; with the honest
        // gate-only τ (F3) faint stars measure at/below truth, so the floor is scaled down accordingly —
        // see docs/sigma-consistency-f3-results.md (follow-up).
        public double MinHFR { get; set; } = 1.2d;

        public StarDetectionRegion Region { get; set; } = StarDetectionRegion.Full;

        // Granularity to sample star bounding boxes when computing star measurements
        public float AnalysisSamplingSize { get; set; } = 1.0f;

        public bool StoreStructureMap { get; set; } = false;

        public string SaveIntermediateFilesPath { get; set; } = string.Empty;

        // If a star contains any pixels greater than this threshold, it is rejected due to being fully saturated
        public double SaturationThreshold { get; set; } = 0.99f;

        // When true, partially-saturated stars (Background + PeakBrightness >= SaturationThreshold) are excluded from
        // the per-frame HFR aggregation (AverageHFR / HFRStdDev) — their flat saturated cores bias HFR high — as long
        // as enough unsaturated stars remain. They stay detected/counted; only the curve point is cleaned.
        public bool ExcludeSaturatedStarsFromHFR { get; set; } = true;

        // Whether to model PSFs
        public bool ModelPSF { get; set; } = true;

        // What type of PSF fitting should be done
        public StarDetectorPSFFitType PSFFitType { get; set; } = StarDetectorPSFFitType.Moffat_40;

        // If this is true, fits PSFs to minimize absolute deviation. This takes a bit more time computationally, but is more robust to noise
        // and outlier pixels
        public bool UsePSFAbsoluteDeviation { get; set; } = false;

        // If PSF modeling is enabled, any R^2 values below this threshold will be rejected
        public double PSFGoodnessOfFitThreshold { get; set; } = 0.9;

        // The number of pixels of the width of a nominal square to sample star bounding boxes for the purposes of PSF model fitting
        public int PSFResolution { get; set; } = 10;

        // Enables parallel processing of PSF modeling by partitioning the detected stars into batches of this size
        // Set <= 0 to disable parallelism
        public int PSFParallelPartitionSize { get; set; } = 100;

        // When true, the model value for each pixel is the integral of the PSF over the pixel area
        // [i-0.5, i+0.5] x [j-0.5, j+0.5] instead of the point sample at (i,j).
        // Reduces bias for undersampled rigs (FWHM ≈ 1.5px). Default: false.
        public bool PSFPixelIntegration { get; set; } = false;

        // Pixel scale of the image given for star detection. INCLUDES DetectionBinning, so arcsec-valued outputs
        // (PSF.FWHMArcsecs) stay physical while the pixel-valued ones are measured in binned pixels.
        public double PixelScale { get; set; } = 1.0d;

        // The integer factor star detection software-bins the frame by before analyzing it (1 = off), layered on
        // by HocusFocusStarDetection.ApplyDetectionImageContext from the effective options. The whole detection pipeline
        // then runs in binned pixels, so every pixel-unit knob above stays in its calibrated range; the detector
        // scales every pixel-space OUTPUT back to source pixels before returning
        // (StarDetector.ScaleResultToSourcePixels). EARLY param — it changes candidate formation, so it is in
        // EarlyCacheKeyProperties.
        public int DetectionBinning { get; set; } = 1;

        // How per-star HFR is aggregated (and whether an extra HFR-outlier rejection pass runs). Carried on the params
        // bundle — rather than read from the live options at the detect site — so a replay that supplies a capture-time
        // options override reproduces the original run's aggregation/outlier behavior. Affects detection output, so it
        // is included in the cache key by ToCanonicalCacheString (not denylisted).
        public MeasurementAverageEnum MeasurementAverage { get; set; } = MeasurementAverageEnum.Median;

        // Controls inner parallelism for per-star evaluation. 0 or negative = auto (Environment.ProcessorCount);
        // 1 = sequential kill-switch; >1 = that many (clamped by ParallelExecution governor).
        // This is an internal knob — it is not exposed in the options UI and is not persisted.
        public int MaxStarEvaluationParallelism { get; set; } = 0;

        /// <summary>
        /// Shallow copy of this parameter bundle. Used by the star-detection optimizer (T2) to materialize
        /// candidate params from a seed without mutating the seed. <see cref="Region"/> is a shared reference,
        /// which is safe because the optimizer never mutates the region — it only tunes the scalar knobs.
        /// </summary>
        public StarDetectorParams Clone() => (StarDetectorParams)this.MemberwiseClone();

        public override string ToString() {
            return $"{{{nameof(HotpixelFiltering)}={HotpixelFiltering.ToString()}, {nameof(NoiseReductionRadius)}={NoiseReductionRadius.ToString()}, {nameof(NoiseClippingMultiplier)}={NoiseClippingMultiplier.ToString()}, {nameof(StarClippingMultiplier)}={StarClippingMultiplier.ToString()}, {nameof(HotpixelFilterRadius)}={HotpixelFilterRadius.ToString()}, {nameof(StructureLayers)}={StructureLayers.ToString()}, {nameof(StructureDilationSize)}={StructureDilationSize.ToString()}, {nameof(StructureDilationCount)}={StructureDilationCount.ToString()}, {nameof(Sensitivity)}={Sensitivity.ToString()}, {nameof(PeakResponse)}={PeakResponse.ToString()}, {nameof(MaxDistortion)}={MaxDistortion.ToString()}, {nameof(DefocusAwareDistortion)}={DefocusAwareDistortion.ToString()}, {nameof(DefocusDistortionSizeReference)}={DefocusDistortionSizeReference.ToString()}, {nameof(DefocusDistortionMinFactor)}={DefocusDistortionMinFactor.ToString()}, {nameof(DefocusAwareCentering)}={DefocusAwareCentering.ToString()}, {nameof(DefocusCenteringToleranceFactor)}={DefocusCenteringToleranceFactor.ToString()}, {nameof(StarCenterTolerance)}={StarCenterTolerance.ToString()}, {nameof(BackgroundBoxExpansion)}={BackgroundBoxExpansion.ToString()}, {nameof(MinimumStarBoundingBoxSize)}={MinimumStarBoundingBoxSize.ToString()}, {nameof(MinHFR)}={MinHFR.ToString()}, {nameof(Region)}={Region}, {nameof(AnalysisSamplingSize)}={AnalysisSamplingSize.ToString()}, {nameof(StoreStructureMap)}={StoreStructureMap.ToString()}, {nameof(SaveIntermediateFilesPath)}={SaveIntermediateFilesPath}, {nameof(SaturationThreshold)}={SaturationThreshold.ToString()}, {nameof(ModelPSF)}={ModelPSF.ToString()}, {nameof(PSFFitType)}={PSFFitType.ToString()}, {nameof(UsePSFAbsoluteDeviation)}={UsePSFAbsoluteDeviation.ToString()}, {nameof(PSFGoodnessOfFitThreshold)}={PSFGoodnessOfFitThreshold.ToString()}, {nameof(PSFResolution)}={PSFResolution.ToString()}, {nameof(PSFParallelPartitionSize)}={PSFParallelPartitionSize.ToString()}, {nameof(PixelScale)}={PixelScale.ToString()}, {nameof(DetectionBinning)}={DetectionBinning.ToString()}, {nameof(ContaminationSensitivity)}={ContaminationSensitivity.ToString()}, {nameof(MaxStarEvaluationParallelism)}={MaxStarEvaluationParallelism.ToString()}}}";
        }

        // Properties intentionally EXCLUDED from the detection-result cache key (ToCanonicalCacheString). The
        // SAFE failure mode of this key is "spurious cache miss", never "stale reuse" — so a property is listed
        // here ONLY when it provably does not change which stars are detected or their measured values. When in
        // doubt, do NOT add it here (the default is to include it). Each entry documents why it is output-neutral.
        private static readonly HashSet<string> CacheKeyExcludedProperties = new HashSet<string>(StringComparer.Ordinal) {
            // Perf only: controls inner per-star evaluation parallelism. Not exposed in the UI, not persisted,
            // and the detected-star results are identical regardless of its value.
            nameof(MaxStarEvaluationParallelism),

            // Diagnostics only: when true the detector fills the side-channel
            // HocusFocusStarDetectorResult.ContaminationDiagnostics list; the production contamination decision
            // and every detected-star value are unaffected (already excluded from ToString() for the same reason).
            nameof(CollectContaminationDiagnostics),

            // Diagnostics only: when true the detector fills the side-channel
            // HocusFocusStarDetectorResult.RejectedCandidates list. The accept/reject decision and every
            // detected-star value are unaffected — the records are emitted at the same gate sites that already
            // run, after the reject decision is made — so this is output-neutral.
            nameof(CollectRejectedCandidateDiagnostics),

            // Logging only: when true the per-region "Average HFR / Detected Stars" INFO summary in
            // BuildStarDetectionResult is skipped. No detected-star value or accept/reject decision depends on it,
            // so it is output-neutral and must not change which detections the optimizer's cache treats as equal.
            nameof(SuppressInfoLogging),

            // Debug/intermediate-output only: stashes the structure map into DebugData for inspection. Does not
            // change which stars are detected or any measured value.
            nameof(StoreStructureMap),

            // Save-path/side-effect only: if non-empty, intermediate images/text are written to this directory.
            // The detected-star results do not depend on it.
            nameof(SaveIntermediateFilesPath)
        };

        /// <summary>
        /// Builds a deterministic, culture-invariant canonical string of all detection-output-affecting
        /// parameters, for use as the input to <see cref="StarDetection.StarDetector.ComputeCacheKey"/>. Public
        /// instance properties are enumerated via reflection and emitted as <c>Name=Value|</c> sorted by name
        /// (deterministic order), EXCEPT those in <see cref="CacheKeyExcludedProperties"/> (an explicit,
        /// commented denylist of provably output-neutral fields). Every value is formatted with
        /// <see cref="CultureInfo.InvariantCulture"/> (enums by name; the <see cref="Region"/> via its
        /// invariant <c>ToCanonicalString()</c>) so the key is identical across locales.
        ///
        /// Design intent: the SAFE failure mode is a spurious cache miss, never stale reuse. Therefore the
        /// default is to INCLUDE a property; only the denylisted, provably non-output fields are dropped. A
        /// property whose getter throws or returns an awkward value is captured defensively rather than crashing.
        /// </summary>
        public string ToCanonicalCacheString() {
            var properties = GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(prop => prop.CanRead && prop.GetIndexParameters().Length == 0)
                .Where(prop => !CacheKeyExcludedProperties.Contains(prop.Name))
                .OrderBy(prop => prop.Name, StringComparer.Ordinal);

            var sb = new StringBuilder();
            foreach (var prop in properties) {
                sb.Append(prop.Name);
                sb.Append('=');
                sb.Append(FormatCacheValue(GetPropertyValueSafe(prop)));
                sb.Append('|');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Like <see cref="ToCanonicalCacheString()"/> but emits ONLY the properties named in
        /// <paramref name="includedProperties"/> (an explicit ALLOW-list, sorted by name), each formatted with
        /// <see cref="CultureInfo.InvariantCulture"/>. Used by
        /// <see cref="StarDetection.StarDetector.ComputeEarlyCacheKey"/> to key a reusable early-stage detection
        /// context on just the early-affecting params. Because it is an allow-list, a property that is not listed
        /// is simply absent from the string (its value never contributes), which is exactly what makes a cache
        /// keyed on this safe to reuse across changes to the omitted (late-only) params.
        /// </summary>
        public string ToCanonicalCacheString(ISet<string> includedProperties) {
            if (includedProperties == null) {
                throw new ArgumentNullException(nameof(includedProperties));
            }

            var properties = GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(prop => prop.CanRead && prop.GetIndexParameters().Length == 0)
                .Where(prop => includedProperties.Contains(prop.Name))
                .OrderBy(prop => prop.Name, StringComparer.Ordinal);

            var sb = new StringBuilder();
            foreach (var prop in properties) {
                sb.Append(prop.Name);
                sb.Append('=');
                sb.Append(FormatCacheValue(GetPropertyValueSafe(prop)));
                sb.Append('|');
            }
            return sb.ToString();
        }

        private object GetPropertyValueSafe(PropertyInfo prop) {
            try {
                return prop.GetValue(this);
            } catch (Exception ex) {
                // A misbehaving getter must never break the cache key (or the build). Fold the exception type
                // into the key so two different failures still produce stable, distinguishable strings.
                return $"<err:{ex.GetType().Name}>";
            }
        }

        private static string FormatCacheValue(object value) {
            switch (value) {
                case null:
                    return "null";
                case StarDetectionRegion region:
                    return region.ToCanonicalString();
                case RatioRect rect:
                    return rect.ToCanonicalString();
                case Enum e:
                    // Enum name (not the underlying number) so reordering enum members can't silently collide.
                    return e.ToString();
                case bool b:
                    return b ? "true" : "false";
                case IFormattable formattable:
                    // Covers all numerics (double/float/int/long/...) with locale-independent formatting.
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            }
        }
    }

    /// <summary>
    /// A fitted local background plane B(x, y) = B0 + B1·(x − OriginX) + B2·(y − OriginY), estimated robustly
    /// from a star's background annulus. Used as the local background everywhere a scalar median was used
    /// (centroid, flux, HFR, PSF) so a one-sided gradient (galaxy/nebula) does not bias measurements. When a
    /// usable gradient fit is unavailable, <see cref="Flat"/> yields a constant plane equal to the median.
    /// </summary>
    public sealed class LocalBackgroundPlane {
        public double OriginX { get; }
        public double OriginY { get; }
        public double B0 { get; }   // background at the origin
        public double B1 { get; }   // d(background)/dx
        public double B2 { get; }   // d(background)/dy
        public bool IsFlat { get; } // true when constructed from a scalar median (no usable gradient fit)

        public LocalBackgroundPlane(double originX, double originY, double b0, double b1, double b2, bool isFlat) {
            OriginX = originX; OriginY = originY; B0 = b0; B1 = b1; B2 = b2; IsFlat = isFlat;
        }

        public static LocalBackgroundPlane Flat(double originX, double originY, double value)
            => new LocalBackgroundPlane(originX, originY, value, 0.0, 0.0, isFlat: true);

        public double ValueAt(double x, double y) => B0 + B1 * (x - OriginX) + B2 * (y - OriginY);
    }

    public class Star {
        public Point2d Center { get; set; }
        public Rect StarBoundingBox { get; set; }
        public double Background { get; set; }
        public LocalBackgroundPlane BackgroundPlane { get; set; }
        public double MeanBrightness { get; set; }
        public double PeakBrightness { get; set; }
        public double HFR { get; set; }
        public PSFModel PSF { get; set; }

        /// <summary>
        /// Set to true when the gradient-robust contamination test detects a one-sided positive residual excess
        /// in the background annulus (a neighbor star, hot column, or other localized source), after removing
        /// the smooth local gradient. When <see cref="StarDetectorParams.RejectContaminatedStars"/> is enabled
        /// the star is rejected outright; otherwise it is kept and merely flagged for downstream diagnostics.
        /// </summary>
        public bool StarContaminationSuspected { get; set; }

        /// <summary>
        /// INFORMATIONAL ONLY (never affects an accept/reject decision). Set to true when this accepted star
        /// passed a defocus-RELAXED gate (distortion and/or centering) but would have FAILED the verbatim STRICT
        /// gate (<see cref="StarDetectorParams.MaxDistortion"/> / <see cref="StarDetectorParams.StarCenterTolerance"/>).
        /// When the defocus-aware gates are OFF the effective threshold equals the strict threshold, so this flag
        /// is never set and detection stays bit-identical. Used by the optimizer to discourage cranking the
        /// defocus relaxation into junk (near-focus relaxation-admitted stars are large/low-fill blobs).
        /// </summary>
        public bool RelaxationAdmitted { get; set; }

        /// <summary>
        /// INFORMATIONAL ONLY (never affects an accept/reject decision, never enters the optimizer objective). The
        /// exact scalar the Sensitivity gate compared this star against: <c>NormalizedBrightness / σ</c>, or — when
        /// <see cref="StarDetectorParams.DefocusAwareDonutDetection"/> admits the integrated-flux path for an
        /// extended candidate — the larger of that and the donut integrated-flux SNR (whichever the gate actually
        /// used). These are two DIFFERENT statistics with different scalings (a per-pixel peak-vs-noise ratio vs. a
        /// matched-filter <c>TotalFlux / (σ√N)</c>) — a caller cannot tell which one a given value is, so treat
        /// entries as belonging to a single scalar "the gate's verdict", not something to pool or threshold as one
        /// physical quantity.
        /// <para>
        /// Carried through detection binning
        /// (<see cref="NINA.Joko.Plugins.HocusFocus.Utility.CvImageUtility.ScaleToSourcePixels(Star, int)"/>) and an
        /// ROI offset (<see cref="NINA.Joko.Plugins.HocusFocus.Utility.CvImageUtility.AddOffset"/>) WITHOUT rescaling
        /// — not because binning "preserves the level" (it does not: the σ denominator is measured on the
        /// already-binned image, so it shrinks under binning — by up to ~<c>DetectionBinning</c> for uncorrelated
        /// noise, measurably less in practice, since the default <c>HotpixelFiltering</c> runs ABOVE the bin at
        /// native resolution and correlates the noise, giving ~1.3x at 2x on synthetic white noise rather than
        /// ~2x; the numerator moves too, just less — the peak term of <c>NormalizedBrightness</c> also shrinks
        /// slightly when a sharp star is block-averaged), but because this is the gate's own comparison pair with
        /// <see cref="StarDetectorParams.Sensitivity"/> (a binned-space param compared BEFORE the rescale) — both
        /// must stay in the same space, exactly like <c>RejectedCandidateRecord.MeasuredValue</c>. The DIRECTION is
        /// what matters, not a specific ratio: values are NOT comparable across different
        /// <see cref="StarDetectorParams.DetectionBinning"/> factors.
        /// </para>
        /// <see cref="double.NaN"/> at every legacy construction site that doesn't set it. Feeds a later
        /// exposure-time recommendation when the optimizer floors the Sensitivity gate.
        /// </summary>
        public double MeasuredSensitivity { get; set; } = double.NaN;

        public override string ToString() {
            return $"{{{nameof(Center)}={Center.ToString()}, {nameof(StarBoundingBox)}={StarBoundingBox.ToString()}, {nameof(Background)}={Background.ToString()}, {nameof(MeanBrightness)}={MeanBrightness.ToString()}, {nameof(PeakBrightness)}={PeakBrightness.ToString()}, {nameof(HFR)}={HFR.ToString()}, {nameof(PSF)}={PSF}, {nameof(StarContaminationSuspected)}={StarContaminationSuspected}, {nameof(RelaxationAdmitted)}={RelaxationAdmitted}, {nameof(MeasuredSensitivity)}={MeasuredSensitivity.ToString()}}}";
        }
    }

    public class StarDetectorMetrics {
        public int StructureCandidates { get; set; } = 0;
        public int TotalDetected { get; set; } = 0;
        public int TooSmall { get; set; } = 0;
        public int OnBorder { get; set; } = 0;
        public int TooDistorted { get => TooDistortedBounds.Count; set => throw new NotSupportedException("Can't set TooDistorted directly"); }
        public List<Rect> TooDistortedBounds { get; private set; } = new List<Rect>();
        public int Degenerate { get => DegenerateBounds.Count; set => throw new NotSupportedException("Can't set Degenerate directly"); }
        public List<Rect> DegenerateBounds { get; private set; } = new List<Rect>();
        public int Saturated { get => SaturatedBounds.Count; set => throw new NotSupportedException("Can't set Saturated directly"); }
        public List<Rect> SaturatedBounds { get; private set; } = new List<Rect>();
        public int LowSensitivity { get => LowSensitivityBounds.Count; set => throw new NotSupportedException("Can't set LowSensitivity directly"); }
        public List<Rect> LowSensitivityBounds { get; private set; } = new List<Rect>();
        public int NotCentered { get => NotCenteredBounds.Count; set => throw new NotSupportedException("Can't set NotCentered directly"); }
        public List<Rect> NotCenteredBounds { get; private set; } = new List<Rect>();
        public int TooFlat { get => TooFlatBounds.Count; set => throw new NotSupportedException("Can't set TooFlat directly"); }
        public List<Rect> TooFlatBounds { get; private set; } = new List<Rect>();
        // Defocus-aware spike suppression (only populated when DefocusAwareDonutDetection is on): candidates
        // rejected as diffraction spikes / satellite trails by the eccentricity gate, and bloom/halo fragments
        // rejected within DonutSaturationBloomRadius of a saturated source. Both empty when the feature is off.
        public int TooElongated { get => TooElongatedBounds.Count; set => throw new NotSupportedException("Can't set TooElongated directly"); }
        public List<Rect> TooElongatedBounds { get; private set; } = new List<Rect>();
        public int BloomSuppressed { get => BloomSuppressedBounds.Count; set => throw new NotSupportedException("Can't set BloomSuppressed directly"); }
        public List<Rect> BloomSuppressedBounds { get; private set; } = new List<Rect>();
        public int TooLowHFR { get; set; } = 0;
        public int HFRAnalysisFailed { get; set; } = 0;

        // Backing field so PSFFitFailed can be incremented atomically from the parallel PSF-fit partitions
        // (see IncrementPsfFitFailed). The public getter/setter behavior is preserved for existing
        // readers/writers (e.g. TestApp's FocusSweepDiagnosticRunner) and for Merge.
        private int psfFitFailed = 0;

        public int PSFFitFailed { get => psfFitFailed; set => psfFitFailed = value; }

        /// <summary>
        /// Atomically increments <see cref="PSFFitFailed"/>. Used by ModelPSF, which runs across multiple
        /// parallel partitions, so a plain <c>++</c> would race.
        /// </summary>
        public void IncrementPsfFitFailed() => System.Threading.Interlocked.Increment(ref psfFitFailed);

        public int ContaminationSuspected { get => ContaminatedBounds.Count; set => throw new NotSupportedException("Can't set ContaminationSuspected directly"); }
        public List<Rect> ContaminatedBounds { get; private set; } = new List<Rect>();

        /// <summary>
        /// INFORMATIONAL count of ACCEPTED stars whose <see cref="Star.RelaxationAdmitted"/> flag is set — i.e.
        /// stars that survived a defocus-RELAXED gate but would have failed the strict gate. Tallied on the main
        /// thread from the assembled accepted-star list (it never flows through <see cref="Merge"/>, since the
        /// per-thread metrics instances never touch it). Zero whenever the defocus-aware gates are OFF, so it
        /// does NOT affect detection bit-identity. The optimizer consumes it as a precision/false-positive signal.
        /// </summary>
        public int RelaxationAdmittedCount { get; set; } = 0;
        public int OutsideROI { get; set; } = 0;
        public long SaturatedPixelCount { get; set; } = 0L;
        public long HotpixelCount { get; set; } = 0L;

        /// <summary>
        /// Folds <paramref name="other"/> into this instance: SUMs all scalar counters and CONCATENATEs every
        /// <c>*Bounds</c> list. Used to combine the per-thread metrics produced by the parallel star-evaluation
        /// stage back into the run's main metrics. Counters that the parallel stage never touches (e.g.
        /// <see cref="StructureCandidates"/>, <see cref="HotpixelCount"/>, <see cref="SaturatedPixelCount"/>,
        /// <see cref="TotalDetected"/>, <see cref="OutsideROI"/>) start at 0 on a fresh thread-local instance,
        /// so the fold is additive and does not double-count values already present on the main metrics.
        /// </summary>
        /// <summary>
        /// Returns all seven <c>*Bounds</c> lists in a fixed, canonical order. Every method that needs to
        /// iterate over all bounds lists (Merge, SortBounds, AddROIOffset) uses this helper so that adding an
        /// eighth bounds list in the future requires only a single edit here.
        /// </summary>
        private List<List<Rect>> AllBoundsLists() => new List<List<Rect>> {
            TooDistortedBounds,
            DegenerateBounds,
            SaturatedBounds,
            LowSensitivityBounds,
            NotCenteredBounds,
            TooFlatBounds,
            TooElongatedBounds,
            BloomSuppressedBounds,
            ContaminatedBounds
        };

        public void Merge(StarDetectorMetrics other) {
            if (other == null) {
                return;
            }

            StructureCandidates += other.StructureCandidates;
            TotalDetected += other.TotalDetected;
            TooSmall += other.TooSmall;
            OnBorder += other.OnBorder;
            TooLowHFR += other.TooLowHFR;
            HFRAnalysisFailed += other.HFRAnalysisFailed;
            PSFFitFailed += other.PSFFitFailed;
            OutsideROI += other.OutsideROI;
            SaturatedPixelCount += other.SaturatedPixelCount;
            HotpixelCount += other.HotpixelCount;

            var thisBounds = AllBoundsLists();
            var otherBounds = other.AllBoundsLists();
            for (int i = 0; i < thisBounds.Count; i++) {
                thisBounds[i].AddRange(otherBounds[i]);
            }
        }

        /// <summary>
        /// Sorts every <c>*Bounds</c> list in place by (Y, then X) of the rect's top-left corner. Called after
        /// merging the parallel star-evaluation results so that production output is independent of thread
        /// scheduling order. (The Task-1 equivalence Signature already sorts bounds the same way.)
        /// </summary>
        public void SortBounds() {
            foreach (var rectBounds in AllBoundsLists()) {
                rectBounds.Sort((a, b) => {
                    int cmp = a.Y.CompareTo(b.Y);
                    return cmp != 0 ? cmp : a.X.CompareTo(b.X);
                });
            }
        }

        public void AddROIOffset(int xOffset, int yOffset) {
            var offset = new Point(xOffset, yOffset);
            foreach (var rectBounds in AllBoundsLists()) {
                var newRectBounds = rectBounds.Select(r => new Rect(r.Location + offset, r.Size)).ToList();
                rectBounds.Clear();
                rectBounds.AddRange(newRectBounds);
            }
        }

        /// <summary>
        /// Scales every <c>*Bounds</c> rectangle from binned pixels back to source pixels. Called once, after
        /// <see cref="AddROIOffset"/>, when <see cref="StarDetectorParams.DetectionBinning"/> is above 1 — the
        /// rejected-candidate boxes are drawn over the full-resolution frame by the annotator and matched against
        /// full-resolution review labels, so they must live in the same space as the accepted stars.
        /// </summary>
        public void ScaleBounds(int factor) {
            if (factor <= 1) {
                return;
            }
            foreach (var rectBounds in AllBoundsLists()) {
                var scaled = rectBounds.Select(r => new Rect(r.X * factor, r.Y * factor, r.Width * factor, r.Height * factor)).ToList();
                rectBounds.Clear();
                rectBounds.AddRange(scaled);
            }
        }
    }

    /// <summary>
    /// Per-star diagnostics for the gradient-robust contamination test, captured only when
    /// <see cref="StarDetectorParams.CollectContaminationDiagnostics"/> is enabled. One record is produced for
    /// every accepted star (1:1 with <see cref="HocusFocusStarDetectorResult.DetectedStars"/>), so the
    /// contamination decision can be reproduced and re-tuned offline. The test fits a robust plane to the
    /// annulus pixels (removing the local gradient), then flags a one-sided POSITIVE residual excess in any
    /// octant (a contaminant only adds light), which ignores both smooth gradients and edge-clip deficits.
    /// </summary>
    public sealed class ContaminationDiagnosticRecord {
        public double CenterX { get; set; }            // image/pixel coords (ROI offset applied, like DetectedStars)
        public double CenterY { get; set; }
        public double NoiseSigma { get; set; }         // fallback sigma (used if the residual sigma is degenerate)
        public double Sensitivity { get; set; }
        public int MinSectorPixels { get; set; }       // = 8
        public bool ContaminationSuspected { get; set; } // production decision
        public double Hfr { get; set; }
        public double Background { get; set; }          // local background plane value at the star center

        // Gradient-robust test details.
        public double GradientSlope { get; set; } = double.NaN;        // |fitted plane gradient| in counts/pixel
        public double LocalSigmaResidual { get; set; } = double.NaN;   // robust sigma of plane-subtracted residuals
        public double[] SectorResidualMedian { get; set; }            // length 8, plane-subtracted
        public int[] SectorResidualCount { get; set; }                // length 8
        public double MaxSectorResidualOverSE { get; set; } = double.NaN; // test statistic (max over sectors)
        public int ResidualTrippingSector { get; set; } = -1;          // first sector that tripped, else -1
    }

    /// <summary>
    /// Canonical gate names used by <see cref="RejectedCandidateRecord.Gate"/>. Centralized so the detector, the
    /// label analyzer, and the recommender all agree on the spelling (these strings are matched, not enum-typed,
    /// because they also flow through the human-readable diagnostics text and the review overlay legend).
    /// </summary>
    public static class RejectionGate {
        public const string TooSmall = "TooSmall";
        public const string OnBorder = "OnBorder";
        public const string TooDistorted = "TooDistorted";
        public const string Degenerate = "Degenerate";
        public const string LowSensitivity = "LowSensitivity";
        public const string NotCentered = "NotCentered";
        public const string TooFlat = "TooFlat";
        public const string TooElongated = "TooElongated";
        public const string BloomSuppressed = "BloomSuppressed";
        public const string HFRAnalysisFailed = "HFRAnalysisFailed";
        public const string TooLowHFR = "TooLowHFR";
        public const string Contaminated = "Contaminated";
    }

    /// <summary>
    /// Per-rejected-candidate diagnostics, captured only when
    /// <see cref="StarDetectorParams.CollectRejectedCandidateDiagnostics"/> is enabled. One record is produced for
    /// EVERY candidate a late gate rejects (unlike the metrics <c>*Bounds</c> lists, which only exist for seven of
    /// the gates), so the label-driven recommender can attribute a labeled "wrongly-rejected" star to the exact
    /// gate that killed it and invert that gate's threshold. <see cref="MeasuredValue"/> is the scalar the gate
    /// compared against <see cref="ThresholdValue"/>; both are <see cref="double.NaN"/> for the non-scalar gates
    /// (OnBorder/Degenerate/HFRAnalysisFailed), which can only be flagged, not threshold-recovered.
    /// </summary>
    public sealed class RejectedCandidateRecord {
        public Rect Bounds { get; set; }                       // candidate bbox (ROI offset applied, like *Bounds)
        public string Gate { get; set; }                       // one of RejectionGate.*
        public double MeasuredValue { get; set; } = double.NaN; // the per-candidate scalar the gate compared
        public double ThresholdValue { get; set; } = double.NaN;// the effective threshold it compared against
        public double CandidateSize { get; set; }              // max(W,H) — the defocus/size proxy
        public double CenterX { get; set; }                    // candidate centroid or bbox center (ROI applied)
        public double CenterY { get; set; }

        // Half-flux radius of the rejected candidate, when measurable (NaN for candidates rejected before a
        // centroid/parameters exist — TooSmall/OnBorder/TooDistorted/Degenerate/HFRAnalysisFailed). For the gates
        // that reject AFTER ComputeStarParameters (LowSensitivity/NotCentered/TooFlat) the detector measures HFR
        // on-demand (diagnostics path only); for TooLowHFR/Contaminated the already-measured HFR is carried.
        public double Hfr { get; set; } = double.NaN;
    }

    public class HocusFocusStarDetectorResult {
        public List<Star> DetectedStars { get; set; }
        public StarDetectorMetrics Metrics { get; set; }
        public DebugData DebugData { get; set; }

        // The software binning factor detection ran at (1 = none). Informational: every geometry in this result is
        // already back in SOURCE pixels. Useful for logs and for anything that wants to report what was used.
        public int DetectionBinning { get; set; } = 1;

        // Noise σ estimated on the noise-reduced structure-map source; drives only the binarize threshold.
        public double StructureNoiseSigma { get; set; }

        // Noise σ estimated on the image actually sampled for star measurement; drives the sensitivity gate,
        // clip margins, MeasureStar τ, the PSF noise floor, and the contamination fallback. Equal to
        // StructureNoiseSigma when the two images are identical (no noise reduction, or measurement noise
        // reduction enabled).
        public double MeasurementNoiseSigma { get; set; }

        // Populated only when StarDetectorParams.CollectContaminationDiagnostics is true; otherwise null.
        public List<ContaminationDiagnosticRecord> ContaminationDiagnostics { get; set; } = null;

        // Populated only when StarDetectorParams.CollectRejectedCandidateDiagnostics is true; otherwise null.
        // One entry per rejected candidate (ROI offset applied, sorted by (Y, X) for determinism).
        public List<RejectedCandidateRecord> RejectedCandidates { get; set; } = null;
    }

    public interface IStarDetector {

        Task<HocusFocusStarDetectorResult> Detect(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token);

        /// <summary>
        /// EARLY phase: builds the reusable early-stage detection context for an image. Pair with
        /// <see cref="GateAndMeasure"/>. Used by the optimizer to cache + reuse the expensive early stage across
        /// candidate evaluations that change only late-stage params. The returned context owns its image; dispose it.
        /// </summary>
        Task<StarDetection.StarDetector.DetectionContext> BuildDetectionContext(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token);

        /// <summary>
        /// LATE phase: gates + measures a context built by <see cref="BuildDetectionContext"/>, producing the same
        /// result the monolithic <see cref="Detect"/> would for the full param bundle.
        /// </summary>
        HocusFocusStarDetectorResult GateAndMeasure(StarDetection.StarDetector.DetectionContext context, StarDetectorParams p, CancellationToken token);

        /// <summary>Early cache key over only the early-affecting params; see
        /// <see cref="StarDetection.StarDetector.ComputeEarlyCacheKey"/>.</summary>
        string ComputeEarlyCacheKey(StarDetectorParams p);
    }
}
