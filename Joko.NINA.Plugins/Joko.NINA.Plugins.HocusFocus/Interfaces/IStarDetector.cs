#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

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
        Gaussian
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

        public StarDetectionRegion(RatioRect outerBoundary) : this(outerBoundary, null) {
        }

        public StarDetectionRegion(RatioRect outerBoundary, RatioRect innerCropBoundary) {
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
        }

        public RatioRect OuterBoundary { get; private set; }
        public RatioRect InnerCropBoundary { get; private set; }

        public override string ToString() {
            return $"{{{nameof(OuterBoundary)}={OuterBoundary}, {nameof(InnerCropBoundary)}={InnerCropBoundary}}}";
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

    public class StarDetectorParams {
        public bool HotpixelFiltering { get; set; } = true;

        // If enabled, applies hotpixel filtering only if the difference exceeds a threshold. This is more computationally intensive, but limits blurring to more likely
        // hotpixel candidates
        public bool HotpixelThresholdingEnabled { get; set; } = true;

        // The threshold limit for Hotpixel thresholding. If the median value for a pixel's neighbors differs from the actual pixel value by this percent
        // of the max ADU, then the pixel is replaced
        public double HotpixelThreshold { get; set; } = 0.05;

        // If this is true, then the source image used for star measurement has the noise reduction settings applied to it. Otherwise, noise reduction is done only on the structure map
        public bool StarMeasurementNoiseReductionEnabled { get; set; } = true;

        // Half size in pixels of a Gaussian convolution filter used for noise reduction. This is useful for low-SNR images
        // Setting this value also implies hotpixel filtering is enabled, since otherwise we would blend the hot pixels into their neighbors
        public int NoiseReductionRadius { get; set; } = 2;

        // Number of noise standard deviations above the median to binarize the structure map containing star candidates. Increasing this is useful for noisy images to reduce
        // spurious detected stars in combination with light noise reduction
        public double NoiseClippingMultiplier { get; set; } = 4.0;

        // Number of noise standard deviations above the local background median to filter star candidate pixels out from star consideration and HFR analysis
        public double StarClippingMultiplier { get; set; } = 2.0;

        // Half size of a median box filter, used for hotpixel removal if HotpixelFiltering is enabled. Only 1 is supported for now, since OpenCV has native support for
        // a median box filter but not a general circular one
        public int HotpixelFilterRadius { get; set; } = 1;

        // Number of wavelet layers for structure detection
        public int StructureLayers { get; set; } = 4;

        // Size of the circle used to dilate the structure map
        public int StructureDilationSize { get; set; } = 3;

        // Number of times to perform dilation on the structure map
        public int StructureDilationCount { get; set; } = 0;

        // Sensitivity is the minimum value of a star's brightness (with the background n subtracted out) above the noise floor (s - b)/n. Smaller values increase sensitivity
        public double Sensitivity { get; set; } = 10.0;

        // Maximum ratio of median pixel value to the peak for a candidate pixel to be rejected. Large values are more tolerant of flat structures
        public double PeakResponse { get; set; } = 0.75;

        // Maximum distortion allowed in each star, which is the ratio of "area" (number of pixels) to the area of a perfect square bounding box. A perfect
        // circle has distortion PI/4 which is about 0.8. Smaller values are more distorted
        public double MaxDistortion { get; set; } = 0.5;

        // Size (as a ratio) of a centered rectangle within the star bounding box that the star center must be in. 1.0 covers the whole region, and 0.0 will fail every star
        public double StarCenterTolerance { get; set; } = 0.3;

        // The background is estimated by looking in an area around the star bounding box, increased on each side by this number of pixels
        public int BackgroundBoxExpansion { get; set; } = 3;

        // The minimum allowed size (length of either side) of a star candidate's bounding box. Increasing this can be helpful at very high focal lengths if too many small structures
        // are detected
        public int MinimumStarBoundingBoxSize { get; set; } = 5;

        // Minimum HFR for a star to be considered viable
        public double MinHFR { get; set; } = 1.5d;

        public StarDetectionRegion Region { get; set; } = StarDetectionRegion.Full;

        // Granularity to sample star bounding boxes when computing star measurements
        public float AnalysisSamplingSize { get; set; } = 1.0f;

        public bool StoreStructureMap { get; set; } = false;

        public string SaveIntermediateFilesPath { get; set; } = string.Empty;

        // If a star contains any pixels greater than this threshold, it is rejected due to being fully saturated
        public double SaturationThreshold { get; set; } = 0.99f;

        // Whether to model PSFs
        public bool ModelPSF { get; set; } = true;

        // What type of PSF fitting should be done
        public StarDetectorPSFFitType PSFFitType { get; set; } = StarDetectorPSFFitType.Moffat_40;

        // If this is true, use ILNumerics for PSF model fitting. This is always false at the moment, because alglib appears to perform more robustly
        public bool UseILNumerics { get; set; } = false;

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

        // Pixel scale of the image given for star detection
        public double PixelScale { get; set; } = 1.0d;

        public override string ToString() {
            return $"{{{nameof(HotpixelFiltering)}={HotpixelFiltering.ToString()}, {nameof(NoiseReductionRadius)}={NoiseReductionRadius.ToString()}, {nameof(NoiseClippingMultiplier)}={NoiseClippingMultiplier.ToString()}, {nameof(StarClippingMultiplier)}={StarClippingMultiplier.ToString()}, {nameof(HotpixelFilterRadius)}={HotpixelFilterRadius.ToString()}, {nameof(StructureLayers)}={StructureLayers.ToString()}, {nameof(StructureDilationSize)}={StructureDilationSize.ToString()}, {nameof(StructureDilationCount)}={StructureDilationCount.ToString()}, {nameof(Sensitivity)}={Sensitivity.ToString()}, {nameof(PeakResponse)}={PeakResponse.ToString()}, {nameof(MaxDistortion)}={MaxDistortion.ToString()}, {nameof(StarCenterTolerance)}={StarCenterTolerance.ToString()}, {nameof(BackgroundBoxExpansion)}={BackgroundBoxExpansion.ToString()}, {nameof(MinimumStarBoundingBoxSize)}={MinimumStarBoundingBoxSize.ToString()}, {nameof(MinHFR)}={MinHFR.ToString()}, {nameof(Region)}={Region}, {nameof(AnalysisSamplingSize)}={AnalysisSamplingSize.ToString()}, {nameof(StoreStructureMap)}={StoreStructureMap.ToString()}, {nameof(SaveIntermediateFilesPath)}={SaveIntermediateFilesPath}, {nameof(SaturationThreshold)}={SaturationThreshold.ToString()}, {nameof(ModelPSF)}={ModelPSF.ToString()}, {nameof(PSFFitType)}={PSFFitType.ToString()}, {nameof(UseILNumerics)}={UseILNumerics.ToString()}, {nameof(UsePSFAbsoluteDeviation)}={UsePSFAbsoluteDeviation.ToString()}, {nameof(PSFGoodnessOfFitThreshold)}={PSFGoodnessOfFitThreshold.ToString()}, {nameof(PSFResolution)}={PSFResolution.ToString()}, {nameof(PSFParallelPartitionSize)}={PSFParallelPartitionSize.ToString()}, {nameof(PixelScale)}={PixelScale.ToString()}}}";
        }
    }

    public class Star {
        public Point2d Center { get; set; }
        public Rect StarBoundingBox { get; set; }
        public double Background { get; set; }
        public double MeanBrightness { get; set; }
        public double PeakBrightness { get; set; }
        public double HFR { get; set; }
        public PSFModel PSF { get; set; }

        public override string ToString() {
            return $"{{{nameof(Center)}={Center.ToString()}, {nameof(StarBoundingBox)}={StarBoundingBox.ToString()}, {nameof(Background)}={Background.ToString()}, {nameof(MeanBrightness)}={MeanBrightness.ToString()}, {nameof(PeakBrightness)}={PeakBrightness.ToString()}, {nameof(HFR)}={HFR.ToString()}, {nameof(PSF)}={PSF}}}";
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
        public int TooLowHFR { get; set; } = 0;
        public int HFRAnalysisFailed { get; set; } = 0;
        public int PSFFitFailed { get; set; } = 0;
        public int OutsideROI { get; set; } = 0;

        public void AddROIOffset(int xOffset, int yOffset) {
            var allRectBounds = new List<List<Rect>>() {
                TooDistortedBounds,
                DegenerateBounds,
                SaturatedBounds,
                LowSensitivityBounds,
                NotCenteredBounds,
                TooFlatBounds
            };

            var offset = new Point(xOffset, yOffset);
            foreach (var rectBounds in allRectBounds) {
                var newRectBounds = rectBounds.Select(r => new Rect(r.Location + offset, r.Size)).ToList();
                rectBounds.Clear();
                rectBounds.AddRange(newRectBounds);
            }
        }
    }

    public class HocusFocusStarDetectorResult {
        public List<Star> DetectedStars { get; set; }
        public StarDetectorMetrics Metrics { get; set; }
        public DebugData DebugData { get; set; }
    }

    public interface IStarDetector {

        Task<HocusFocusStarDetectorResult> Detect(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token);
    }
}