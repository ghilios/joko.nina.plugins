#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Image.Interfaces;
using NINA.WPF.Base.ViewModel.AutoFocus;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Core.Enum;
using NINA.Core.Locale;
using Accord.Imaging;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    [Flags]
    public enum CvImageStatisticsFlags {
        None = 0,
        Median = 1,
        MAD = 2,
        Mean = 4,
        StdDev = 8,
        All = Median | MAD | Mean | StdDev
    }

    public class CvImageStatistics {
        public double Median { get; set; }
        public double MAD { get; set; }

        public double Mean { get; set; }

        public double StdDev { get; set; }

        public override string ToString() {
            return $"{{{nameof(Median)}={Median.ToString()}, {nameof(MAD)}={MAD.ToString()}, {nameof(Mean)}={Mean.ToString()}, {nameof(StdDev)}={StdDev.ToString()}}}";
        }
    }

    public static class CvImageUtility {
        private static Random PRNG = new Random();

        public static Mat ToOpenCVMat(ushort[] imageArray, int bpp, int width, int height) {
            var data = new Mat(new Size(width, height), MatType.CV_32F);
            var scalingRatio = (float)(1 << bpp);
            unsafe {
                var numPixels = width * height;
                var dest = (float*)data.DataPointer;
                fixed (ushort* src = imageArray) {
                    var srcPtr = src;
                    for (int i = 0; i < numPixels; ++i) {
                        *(dest++) = *(srcPtr++) / scalingRatio;
                    }
                }
            }
            return data;
        }

        public static Mat ConvolveGaussian(Mat src, Mat dst, int kernelSize, double sigma = -1.0d) {
            if (kernelSize < 3 || kernelSize % 2 == 0) {
                throw new ArgumentException("kernelSize must be a positive odd number greater than 1", "kernelSize");
            }
            if (src.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }
            if (src.Size() != dst.Size() || src.Type() != dst.Type()) {
                dst.Create(src.Size(), src.Type());
            }

            if (sigma <= 0.0) {
                sigma = 0.159758 * kernelSize;
            }

            using (var normalizedKernel = Cv2.GetGaussianKernel(kernelSize, sigma, ktype: MatType.CV_64F)) {
                Cv2.SepFilter2D(src, dst, src.Type(), normalizedKernel, normalizedKernel, borderType: BorderTypes.Reflect);
            }
            return dst;
        }

        public static Mat ToOpenCVMat(IRenderedImage image) {
            if ((image as IDebayeredImage)?.SaveLumChannel == true) {
                var props = image.RawImageData.Properties;
                var debayeredImage = (IDebayeredImage)image;
                return ToOpenCVMat(debayeredImage.DebayeredData.Lum, bpp: props.BitDepth, width: props.Width, height: props.Height);
            } else {
                return ToOpenCVMat(image.RawImageData);
            }
        }

        public static Mat ToOpenCVMat(IImageData imageData) {
            var props = imageData.Properties;
            return ToOpenCVMat(imageData.Data.FlatArray, bpp: props.BitDepth, width: props.Width, height: props.Height);
        }

        public static CvImageStatistics CalculateStatistics(Mat image, Rect? rect = null, CvImageStatisticsFlags flags = CvImageStatisticsFlags.All) {
            if (image.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }

            var result = new CvImageStatistics();
            var maybeRoi = rect != null ? image.SubMat(rect.Value) : image;
            if (flags.HasFlag(CvImageStatisticsFlags.MAD) || flags.HasFlag(CvImageStatisticsFlags.Median)) {
                maybeRoi.GetArray<float>(out var data);
                if (flags.HasFlag(CvImageStatisticsFlags.MAD)) {
                    // Median
                    Array.Sort(data);
                    var medianPosition = data.Length >> 1;
                    if (flags.HasFlag(CvImageStatisticsFlags.Median)) {
                        result.Median = data[medianPosition];
                    }

                    // MAD
                    int upIndex = medianPosition;
                    int downIndex = medianPosition - 1;
                    int currentCount = 0;
                    while (true) {
                        var upDistance = upIndex < data.Length ? Math.Abs(data[upIndex] - result.Median) : double.MaxValue;
                        var downDistance = downIndex >= 0 ? Math.Abs(data[downIndex] - result.Median) : double.MaxValue;
                        int chosenIndex;
                        if (upDistance <= downDistance) {
                            chosenIndex = upIndex++;
                        } else {
                            chosenIndex = downIndex--;
                        }

                        if (currentCount++ == medianPosition) {
                            result.MAD = Math.Abs(data[chosenIndex] - result.Median);
                            break;
                        }
                    }
                } else if (flags.HasFlag(CvImageStatisticsFlags.Median)) {
                    // Since we're only getting the median, we can use quick select
                    result.Median = MathUtility.MedianFloat(data, PRNG);
                }
            }

            if (flags.HasFlag(CvImageStatisticsFlags.StdDev)) {
                Cv2.MeanStdDev(maybeRoi, out var meanScalar, out var stddevScalar);
                result.StdDev = stddevScalar.ToDouble();
                if (flags.HasFlag(CvImageStatisticsFlags.Mean)) {
                    result.Mean = meanScalar.ToDouble();
                }
            } else if (flags.HasFlag(CvImageStatisticsFlags.Mean)) {
                var meanScalar = Cv2.Mean(maybeRoi);
                result.Mean = meanScalar.ToDouble();
            }

            return result;
        }

        private static CvImageStatistics CalculateStatistics_Histogram_UInt16(Mat image) {
            var histogram = new uint[ushort.MaxValue + 1];
            unsafe {
                var dataPtr = (ushort*)image.DataPointer;
                for (var i = 0; i < image.Rows * image.Cols; ++i) {
                    histogram[dataPtr[i]] += 1;
                }
            }

            var numPixels = image.Rows * image.Cols;

            // Median
            var targetMedianCount = numPixels / 2.0d;
            uint currentCount = 0;
            double median = -1.0d;
            for (uint i = 0; i <= ushort.MaxValue; ++i) {
                currentCount += histogram[i];
                if (currentCount > targetMedianCount) {
                    median = i;
                    break;
                } else if (currentCount == targetMedianCount) {
                    for (uint j = i + 1; i <= ushort.MaxValue; ++i) {
                        if (histogram[j] > 0) {
                            median = (i + j) / 2.0d;
                            break;
                        }
                    }
                    break;
                }
            }

            // MAD
            currentCount = 0;
            int upIndex = (int)Math.Ceiling(median);
            int downIndex = (int)Math.Floor(median);
            double beforeMedian = -1;
            double mad;
            while (true) {
                while (upIndex <= ushort.MaxValue && histogram[upIndex] == 0) {
                    ++upIndex;
                }
                while (downIndex >= 0 && histogram[downIndex] == 0) {
                    --downIndex;
                }

                var upDistance = upIndex <= ushort.MaxValue ? Math.Abs(upIndex - median) : double.MaxValue;
                var downDistance = downIndex >= 0 ? Math.Abs(downIndex - median) : double.MaxValue;
                int chosenIndex;
                if (upDistance <= downDistance) {
                    chosenIndex = upIndex;
                    currentCount += histogram[upIndex++];
                } else {
                    chosenIndex = downIndex;
                    currentCount += histogram[downIndex--];
                }

                if (currentCount == targetMedianCount) {
                    beforeMedian = Math.Abs(chosenIndex - median);
                } else if (currentCount > targetMedianCount) {
                    if (beforeMedian >= 0) {
                        mad = (beforeMedian + Math.Abs(chosenIndex - median)) / 2.0d;
                    } else {
                        mad = Math.Abs(chosenIndex - median);
                    }
                    break;
                }
            }

            // Mean
            ulong pixelTotal = 0L;
            for (uint i = 0; i <= ushort.MaxValue; ++i) {
                pixelTotal += histogram[i] * i;
            }
            double mean = pixelTotal / (double)numPixels;

            // Variance
            double sse = 0d;
            for (uint i = 0; i <= ushort.MaxValue; ++i) {
                var error = i - mean;
                sse += error * error;
            }
            double variance = sse / (numPixels - 1);
            double stdDev = Math.Sqrt(variance);

            return new CvImageStatistics() { Median = median, MAD = mad, Mean = mean, StdDev = stdDev };
        }

        public struct Ranged {

            public Ranged(double start, double end) {
                Start = start;
                End = end;
            }

            public double Start;
            public double End;
        }

        public static CvImageStatistics CalculateStatistics_Histogram(
            Mat image,
            bool useLogHistogram = true,
            Ranged? valueRange = null,
            Rect? rect = null,
            CvImageStatisticsFlags flags = CvImageStatisticsFlags.All) {
            if (image.Type() == MatType.CV_16U) {
                return CalculateStatistics_Histogram_UInt16(image);
            }
            if (image.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }

            var result = new CvImageStatistics();
            int numBuckets = 1 << 16;
            var histogram = new uint[numBuckets];
            var bucketLowerBounds = new double[numBuckets];
            double firstLogValue = Math.Log(1.0 / numBuckets / 2.0); // Pick a starting lower bound close enough to 0 to be a useful precision
            double logBucketSize = (0.0 - firstLogValue) / numBuckets; // Last bucket is ln(1) = 0

            if (useLogHistogram) {
                bucketLowerBounds[0] = 0;
                double nextLogValue = firstLogValue;
                for (int i = 1; i < numBuckets; ++i) {
                    bucketLowerBounds[i] = Math.Exp(nextLogValue);
                    nextLogValue += logBucketSize;
                }
            } else {
                for (int i = 0; i < numBuckets; ++i) {
                    bucketLowerBounds[i] = (double)i / numBuckets;
                }
            }

            long numPixels = 0L;
            var height = rect.HasValue ? rect.Value.Height : image.Height;
            var width = rect.HasValue ? rect.Value.Width : image.Width;
            unsafe {
                var imageData = (float*)image.DataPointer;
                var dataRowSizeBytes = width * sizeof(float);
                var data = (float*)image.DataPointer;
                var rowStride = image.Width - width;
                var startX = rect.HasValue ? rect.Value.X : 0;
                var startY = rect.HasValue ? rect.Value.Y : 0;
                var p = data + startY * image.Width + startX;
                if (valueRange.HasValue) {
                    var valueRangeValue = valueRange.Value;
                    for (int row = 0; row < height; ++row) {
                        for (int col = 0; col < width; ++col) {
                            var pixelValue = (double)*(p++);
                            if (pixelValue < valueRangeValue.Start || pixelValue >= valueRangeValue.End) {
                                continue;
                            }
                            int bucketIndex;
                            if (useLogHistogram) {
                                var logValue = Math.Log(pixelValue);
                                bucketIndex = (int)Math.Ceiling((logValue - firstLogValue) / logBucketSize);
                            } else {
                                bucketIndex = (int)Math.Floor(pixelValue * numBuckets);
                            }
                            if (bucketIndex < 0) bucketIndex = 0;
                            if (bucketIndex >= numBuckets) bucketIndex = numBuckets - 1;
                            ++histogram[bucketIndex];
                            ++numPixels;
                        }
                        p += rowStride;
                    }
                } else {
                    // Keep the hot path for computing statistics without bounds fast
                    for (int row = 0; row < height; ++row) {
                        for (int col = 0; col < width; ++col) {
                            var pixelValue = (double)*(p++);
                            int bucketIndex;
                            if (useLogHistogram) {
                                var logValue = Math.Log(pixelValue);
                                bucketIndex = (int)Math.Ceiling((logValue - firstLogValue) / logBucketSize);
                            } else {
                                bucketIndex = (int)Math.Floor(pixelValue * numBuckets);
                            }
                            if (bucketIndex < 0) bucketIndex = 0;
                            if (bucketIndex >= numBuckets) bucketIndex = numBuckets - 1;
                            ++histogram[bucketIndex];
                        }
                        p += rowStride;
                    }
                    numPixels = height * width;
                }
            }

            if (flags.HasFlag(CvImageStatisticsFlags.MAD) || flags.HasFlag(CvImageStatisticsFlags.Median)) {
                var targetMedianCount = numPixels / 2.0d;
                uint currentCount = 0;
                int medianPosition = -1;
                for (int i = 0; i <= numBuckets; ++i) {
                    currentCount += histogram[i];
                    // Ignore the case where we land directly in the middle of an even count array. This is already an approximation anyways, so it's not worth the complexity
                    if (currentCount >= targetMedianCount) {
                        // Interpolate within the bucket containing the median
                        var interpolationRatio = (currentCount - targetMedianCount) / histogram[i];
                        // Handle the case where the median is within the last bucket
                        var nextBucket = i < (numBuckets - 1) ? bucketLowerBounds[i + 1] : 1.0d;
                        var thisBucket = bucketLowerBounds[i];
                        result.Median = thisBucket + (nextBucket - thisBucket) * interpolationRatio;
                        medianPosition = i;
                        break;
                    }
                }

                // MAD
                if (flags.HasFlag(CvImageStatisticsFlags.MAD)) {
                    int upIndex = medianPosition;
                    int downIndex = medianPosition - 1;
                    currentCount = 0;
                    while (true) {
                        var upDistance = upIndex < numBuckets ? Math.Abs(bucketLowerBounds[upIndex] - result.Median) : double.MaxValue;
                        var downDistance = downIndex >= 0 ? Math.Abs(bucketLowerBounds[downIndex] - result.Median) : double.MaxValue;
                        int chosenIndex;
                        if (upDistance <= downDistance) {
                            chosenIndex = upIndex++;
                        } else {
                            chosenIndex = downIndex--;
                        }

                        currentCount += histogram[chosenIndex];
                        if (currentCount >= targetMedianCount) {
                            result.MAD = Math.Abs(bucketLowerBounds[chosenIndex] - result.Median);
                            break;
                        }
                    }
                }
            }

            if (flags.HasFlag(CvImageStatisticsFlags.Mean) || flags.HasFlag(CvImageStatisticsFlags.StdDev)) {
                // Mean
                double pixelTotal = 0d;
                for (int i = 0; i < numBuckets; ++i) {
                    pixelTotal += histogram[i] * bucketLowerBounds[i];
                }
                result.Mean = pixelTotal / numPixels;

                // Variance
                if (flags.HasFlag(CvImageStatisticsFlags.StdDev)) {
                    double sse = 0d;
                    for (int i = 0; i < numBuckets; ++i) {
                        var error = bucketLowerBounds[i] - result.Mean;
                        sse += histogram[i] * (error * error);
                    }
                    double variance = sse / (numPixels - 1);
                    result.StdDev = Math.Sqrt(variance);
                }
            }
            return result;
        }

        public static Mat GetB3SplineFilter(int dyadicLayer) {
            /*
             * Cubic Spline coefficients
             *   1.0/256, 1.0/64, 3.0/128, 1.0/64, 1.0/256,
             *   1.0/64,  1.0/16, 3.0/32,  1.0/16, 1.0/64,
             *   3.0/128, 3.0/32, 9.0/64,  3.0/32, 3.0/128,
             *   1.0/64,  1.0/16, 3.0/32,  1.0/16, 1.0/64,
             *   1.0/256, 1.0/64, 3.0/128, 1.0/64, 1.0/256
             *
             * Separated 1D filter
             *   0.0625,  0.25,   0.375,   0.25,   0.0625
             */

            int size = (1 << (dyadicLayer + 2)) + 1;
            var bicubicWavelet2D = new Mat(new Size(1, size), MatType.CV_32F, 0.0d);
            // Each successive layer is downsampled 2x. Rather than copy the matrix to convolve it, we can pad
            // the separated filter with zeroes
            unsafe {
                var data = (float*)bicubicWavelet2D.DataPointer;
                data[0] = data[size - 1] = 0.0625f;
                data[1 << dyadicLayer] = data[size - (1 << dyadicLayer) - 1] = 0.25f;
                data[size >> 1] = 0.375f;
            }
            return bicubicWavelet2D;
        }

        public static Mat ClampInPlace(Mat src, float min = 0.0f, float max = 1.0f) {
            Cv2.Max(src, min, src);
            Cv2.Min(src, max, src);
            return src;
        }

        public static Mat Rescale(Mat src, float min = 0.0f, float max = 1.0f) {
            Cv2.MinMaxIdx(src, out double dataMin, out double dataMax);
            var scalingDenominator = dataMax - dataMin;
            return src.Subtract(dataMin).Divide(scalingDenominator).ToMat();
        }

        public static Mat SubtractInPlace(Mat lhs, Mat rhs, float min = 0.0f, float max = 1.0f) {
            if (lhs.Size() != rhs.Size() || lhs.Type() != rhs.Type()) {
                throw new ArgumentException("Matrix size and type must be the same to subtract in place");
            }
            if (lhs.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }

            Cv2.Subtract(lhs, rhs, lhs);
            return ClampInPlace(lhs, min, max);
        }

        public static Mat ComputeResidualAtrousB3SplineDyadicWaveletLayer(Mat src, int numLayers) {
            var previousLayer = src;
            Mat tempMat = new Mat();
            for (int i = 0; i < numLayers; ++i) {
                using (var scalingFilter = GetB3SplineFilter(i)) {
                    Cv2.SepFilter2D(previousLayer, tempMat, MatType.CV_32F, scalingFilter, scalingFilter, borderType: BorderTypes.Reflect);
                    previousLayer = tempMat;
                }
            }
            return previousLayer;
        }

        public static double BilinearSamplePixelValue(Mat image, double y, double x) {
            if (image.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }

            var y0 = (int)Math.Floor(y);
            var y1 = Math.Min(image.Height - 1, y0 + 1);
            var x0 = (int)Math.Floor(x);
            var x1 = Math.Min(image.Width - 1, x0 + 1);
            double yRatio = y - y0;
            double xRatio = x - x0;
            unsafe {
                var data = (float*)image.DataPointer;
                var width = image.Width;
                var p00 = (double)data[y0 * width + x0];
                var p01 = (double)data[y0 * width + x1];
                var p10 = (double)data[y1 * width + x0];
                var p11 = (double)data[y1 * width + x1];
                var interpolatedX0 = p00 + xRatio * (p01 - p00);
                var interpolatedX1 = p10 + xRatio * (p11 - p10);
                return interpolatedX0 + yRatio * (interpolatedX1 - interpolatedX0);
            }
        }

        public class KappaSigmaNoiseEstimateResult {
            public double Sigma { get; set; }
            public double BackgroundMean { get; set; }
            public int NumIterations { get; set; }
        }

        public static KappaSigmaNoiseEstimateResult KappaSigmaNoiseEstimate(Mat image, double clippingMultipler = 3.0d, double allowedError = 0.00001, int maxIterations = 5) {
            // NOTE: This algorithm could be sped up by building a log histogram. Consider this if performance becomes problematic
            if (image.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }

            using (var backgroundMaskMat = new Mat()) {
                int numPixels = image.Rows * image.Cols;
                var threshold = float.MaxValue;
                var lastSigma = 1.0d;
                var lastBackgroundMean = 1.0d;
                int numIterations = 0;

                while (numIterations < maxIterations) {
                    if (numIterations > 0) {
                        Cv2.InRange(image, float.Epsilon, threshold - float.Epsilon, backgroundMaskMat);
                    }

                    Cv2.MeanStdDev(image, out var meanScalar, out var sigmaScalar, numIterations > 0 ? backgroundMaskMat : null);
                    var sigma = sigmaScalar.ToDouble();
                    var mean = meanScalar.ToDouble();
                    if (++numIterations > 1) {
                        // NOTE: PixInsight treats this as an absolute difference rather than a percent change. That makes it faster, but less accurate
                        var sigmaConvergenceError = Math.Abs(sigma - lastSigma);
                        if (sigmaConvergenceError <= allowedError) {
                            lastSigma = sigma;
                            break;
                        }
                    }
                    threshold = (float)(mean + clippingMultipler * sigma);
                    lastSigma = sigma;
                    lastBackgroundMean = mean;
                }

                return new KappaSigmaNoiseEstimateResult() {
                    Sigma = lastSigma,
                    BackgroundMean = lastBackgroundMean,
                    NumIterations = numIterations
                };
            }
        }

        public static void Binarize(Mat src, Mat dst, double threshold) {
            if (src.Size() != dst.Size() || src.Type() != dst.Type()) {
                dst.Create(src.Size(), src.Type());
            }

            Cv2.Threshold(src, dst, threshold, 1.0d, ThresholdTypes.Binary);
        }

        public static System.Drawing.Rectangle ToDrawingRectangle(this OpenCvSharp.Rect openCvRect) {
            return new System.Drawing.Rectangle(x: openCvRect.Left, y: openCvRect.Top, width: openCvRect.Width, height: openCvRect.Height);
        }

        public static Accord.Point ToAccordPoint(this Point2d point) {
            return new Accord.Point(x: (float)point.X, y: (float)point.Y);
        }

        public static Star AddOffset(this Star star, int xOffset, int yOffset) {
            return new Star() {
                Center = star.Center.Add(new Point2d(xOffset, yOffset)),
                StarBoundingBox = star.StarBoundingBox.Add(new Point(xOffset, yOffset)),
                Background = star.Background,
                MeanBrightness = star.MeanBrightness,
                HFR = star.HFR,
                PSF = star.PSF
            };
        }

        public static System.Drawing.Color ToDrawingColor(this System.Windows.Media.Color color) {
            return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        public static System.Drawing.Color SetAlpha(this System.Drawing.Color color, byte a) {
            return System.Drawing.Color.FromArgb(a, color.R, color.G, color.B);
        }

        public static System.Drawing.FontFamily ToDrawingFontFamily(this System.Windows.Media.FontFamily fontFamily) {
            return new System.Drawing.FontFamily(fontFamily.FamilyNames.Select(fn => fn.Value).First());
        }

        public static System.Drawing.Color Blend(this System.Drawing.Color color1, System.Drawing.Color color2) {
            if (color2.A == byte.MaxValue) {
                return color2;
            }

            var r1 = (int)color1.R * color1.A / byte.MaxValue;
            var g1 = (int)color1.G * color1.A / byte.MaxValue;
            var b1 = (int)color1.B * color1.A / byte.MaxValue;
            var a2 = color2.A;
            var a1 = byte.MaxValue - a2;
            var blendR = (byte)((r1 * a1 + (int)color2.R * a2) / byte.MaxValue);
            var blendG = (byte)((g1 * a1 + (int)color2.G * a2) / byte.MaxValue);
            var blendB = (byte)((b1 * a1 + (int)color2.B * a2) / byte.MaxValue);
            var blendA = Math.Max(color1.A, color2.A);
            return System.Drawing.Color.FromArgb(blendA, blendR, blendG, blendB);
        }

        public static void BlendPixel(this System.Drawing.Bitmap bmp, int x, int y, System.Drawing.Color color) {
            var currentColor = bmp.GetPixel(x, y);
            var blendedColor = currentColor.Blend(color);
            bmp.SetPixel(x, y, blendedColor);
        }

        public static MeasureAndError AverageMeasurement(this List<MeasureAndError> measurement) {
            int total = 0;
            double sum = 0.0d;
            double sumVariance = 0.0d;
            bool invalidStdDev = false;
            foreach (var measure in measurement) {
                if (measure.Measure > 0) {
                    sum += measure.Measure;
                    ++total;

                    if (measure.Stdev <= 0.0 || double.IsNaN(measure.Stdev)) {
                        invalidStdDev = true;
                    }
                    if (!invalidStdDev) {
                        sumVariance += measure.Stdev * measure.Stdev;
                    }
                }
            }

            if (total == 0) {
                return new MeasureAndError() {
                    Measure = 0.0,
                    Stdev = double.NaN
                };
            } else {
                return new MeasureAndError() {
                    Measure = sum / total,
                    Stdev = Math.Sqrt(sumVariance / total)
                };
            }
        }

        public static void ApplyLUT(Mat src, Mat lut, Mat dst) {
            if (dst.Size() != src.Size() || dst.Type() != src.Type()) {
                dst.Create(src.Size(), src.Type());
            }

            unsafe {
                var srcData = (ushort*)src.DataPointer;
                var dstData = (ushort*)dst.DataPointer;
                var lutData = (ushort*)lut.DataPointer;
                var numPixels = src.Rows * src.Cols;
                for (int i = 0; i < numPixels; ++i) {
                    dstData[i] = lutData[srcData[i]];
                }
            }
        }

        private static double NormalizeUShort(double val, int bitDepth) {
            return val / (double)(1 << bitDepth);
        }

        private static ushort DenormalizeUShort(double val) {
            return (ushort)(val * ushort.MaxValue);
        }

        private static double MidtonesTransferFunction(double midToneBalance, double x) {
            if (x > 0) {
                if (x < 1) {
                    return (midToneBalance - 1) * x / ((2 * midToneBalance - 1) * x - midToneBalance);
                }
                return 1;
            }
            return 0;
        }

        public static Mat CreateMTFLookup(CvImageStatistics statistics, double shadowsClipping = -2.8, double targetHistogramMedianPercent = 0.2) {
            var normalizedMedian = NormalizeUShort(statistics.Median, 16);
            var normalizedMAD = NormalizeUShort(statistics.MAD, 16);

            var scaleFactor = 1.4826; // see https://en.wikipedia.org/wiki/Median_absolute_deviation

            double shadows = normalizedMedian + shadowsClipping * normalizedMAD * scaleFactor;
            double midtones = MidtonesTransferFunction(targetHistogramMedianPercent, normalizedMedian - shadows);
            double highlights = 1;

            var lut = new Mat(1, ushort.MaxValue, MatType.CV_16U);
            unsafe {
                var lutData = (ushort*)lut.DataPointer;
                for (int i = 0; i < ushort.MaxValue; i++) {
                    double value = NormalizeUShort(i, 16);
                    lutData[i] = DenormalizeUShort(MidtonesTransferFunction(midtones, 1 - highlights + value - shadows));
                }
            }

            return lut;
        }
    }
}