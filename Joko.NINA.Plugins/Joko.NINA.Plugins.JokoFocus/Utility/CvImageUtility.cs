using Joko.NINA.Plugins.JokoFocus.StarDetection;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using OpenCvSharp;
using System;
using System.Linq;

namespace Joko.NINA.Plugins.JokoFocus.Utility {
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
    }

    public static class CvImageUtility {
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
            Mat result = null;
            try {
                var props = image.RawImageData.Properties;
                if (image is IDebayeredImage) {
                    var debayeredImage = (IDebayeredImage)image;
                    if (!debayeredImage.SaveLumChannel) {
                        throw new ArgumentException("No Luminance channel saved");
                    }

                    result = ToOpenCVMat(debayeredImage.DebayeredData.Lum, bpp: props.BitDepth, width: props.Width, height: props.Height);
                } else {
                    result = ToOpenCVMat(image.RawImageData.Data.FlatArray, bpp: props.BitDepth, width: props.Width, height: props.Height);
                }
                return result;
            } catch (Exception) {
                result?.Dispose();
                throw;
            }
        }

        public static CvImageStatistics CalculateStatistics(Mat image, Rect? rect = null, CvImageStatisticsFlags flags = CvImageStatisticsFlags.All) {
            if (image.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }

            float[] data;
            if (rect == null) {
                image.GetArray<float>(out data);
            } else {
                unsafe {
                    var imageData = (float*)image.DataPointer;
                    var height = rect.Value.Height;
                    var width = rect.Value.Width;
                    var startX = rect.Value.X;
                    var startY = rect.Value.Y;
                    data = new float[height * width];
                    var dataSizeBytes = data.Length * sizeof(float);
                    var dataRowSizeBytes = width * sizeof(float);
                    fixed (float* t = data) {
                        for (int row = 0; row < rect.Value.Height; ++row) {
                            System.Buffer.MemoryCopy(imageData + ((row + startY) * image.Width) + startX, t + (row * width), dataSizeBytes, dataRowSizeBytes);
                            dataSizeBytes -= dataRowSizeBytes;
                        }
                    }
                }
            }

            var result = new CvImageStatistics();
            if (flags.HasFlag(CvImageStatisticsFlags.MAD) || flags.HasFlag(CvImageStatisticsFlags.Median)) {
                // Median
                Array.Sort(data);
                var medianPosition = data.Length >> 1;
                result.Median = data[medianPosition];

                // MAD
                if (flags.HasFlag(CvImageStatisticsFlags.MAD)) {
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
                }
            }

            if (flags.HasFlag(CvImageStatisticsFlags.Mean) || flags.HasFlag(CvImageStatisticsFlags.StdDev)) {
                // Mean
                double pixelTotal = 0d;
                for (int i = 0; i < data.Length; ++i) {
                    pixelTotal += data[i];
                }
                result.Mean = pixelTotal / data.Length;

                // Variance
                if (flags.HasFlag(CvImageStatisticsFlags.StdDev)) {
                    double sse = 0d;
                    for (int i = 0; i < data.Length; ++i) {
                        var error = data[i] - result.Mean;
                        sse += error * error;
                    }
                    double variance = sse / (data.Length - 1);
                    result.StdDev = Math.Sqrt(variance);
                }
            }

            return result;
        }

        public static CvImageStatistics CalculateStatistics_Histogram(Mat image, bool useLogHistogram = true, Rect? rect = null, CvImageStatisticsFlags flags = CvImageStatisticsFlags.All) {
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
            }

            var numPixels = height * width;
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

        public static Mat ClipInPlace(Mat src, float min = 0.0f, float max = 1.0f) {
            unsafe {
                var srcData = (float*)src.DataPointer;
                var numPixels = src.Rows * src.Cols;
                for (int i = 0; i < numPixels; ++i) {
                    var value = srcData[i];
                    if (value < min) {
                        value = min;
                    }
                    if (value > max) {
                        value = max;
                    }
                    srcData[i] = value;
                }
                return src;
            }
        }

        public static Mat RescaleInPlace(Mat src, float min = 0.0f, float max = 1.0f) {
            unsafe {
                var srcData = (float*)src.DataPointer;
                var numPixels = src.Rows * src.Cols;
                float dataMin = float.MaxValue, dataMax = 0.0f;
                for (int i = 0; i < numPixels; ++i) {
                    var value = srcData[i];
                    dataMax = value > dataMax ? value : dataMax;
                    dataMin = value < dataMin ? value : dataMin;
                }
                var scalingDenominator = dataMax - dataMin;
                if (scalingDenominator > 0) {
                    for (int i = 0; i < numPixels; ++i) {
                        var pixel = srcData[i];
                        srcData[i] = (pixel - dataMin) / scalingDenominator;
                    }
                }
                return src;
            }
        }

        public static Mat SubtractInPlace(Mat lhs, Mat rhs, float min = 0.0f, float max = 1.0f) {
            if (lhs.Size() != rhs.Size() || lhs.Type() != rhs.Type()) {
                throw new ArgumentException("Matrix size and type must be the same to subtract in place");
            }
            if (lhs.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }

            unsafe {
                var lhsData = (float*)lhs.DataPointer;
                var rhsData = (float*)rhs.DataPointer;
                var numPixels = lhs.Rows * lhs.Cols;
                for (int i = 0; i < numPixels; ++i) {
                    var value = *lhsData - *(rhsData++);
                    if (value < min) {
                        value = min;
                    }
                    if (value > max) {
                        value = max;
                    }
                    *(lhsData++) = value;
                }
            }
            return lhs;
        }

        public static Mat[] ComputeAtrousB3SplineDyadicWaveletLayers(Mat src, int numLayers) {
            var layers = new Mat[numLayers + 1];
            var previousLayer = src;
            Mat convolved = null;
            for (int i = 0; i < numLayers; ++i) {
                using (var scalingFilter = GetB3SplineFilter(i)) {
                    convolved = new Mat();
                    Cv2.SepFilter2D(previousLayer, convolved, MatType.CV_32F, scalingFilter, scalingFilter, borderType: BorderTypes.Reflect);
                    layers[i] = ClipInPlace(previousLayer.Subtract(convolved).ToMat());
                    previousLayer = convolved;
                }
            }
            layers[numLayers] = convolved;
            return layers;
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
            var p00 = (double)image.Get<float>(y0, x0);
            var p01 = (double)image.Get<float>(y0, x1);
            var p10 = (double)image.Get<float>(y1, x0);
            var p11 = (double)image.Get<float>(y1, x1);
            var interpolatedX0 = p00 + xRatio * (p01 - p00);
            var interpolatedX1 = p10 + xRatio * (p11 - p10);
            return interpolatedX0 + yRatio * (interpolatedX1 - interpolatedX0);
        }

        public static double KappaSigmaNoiseEstimate(Mat image, out long numBackgroundPixels, double clippingMultipler = 3.0d, double allowedError = 0.001, int maxIterations = 10) {
            // NOTE: This algorithm could be sped up by building a log histogram. Consider this if performance becomes problematic
            if (image.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }

            unsafe {
                var imageData = (float*)image.DataPointer;
                int numPixels = image.Rows * image.Cols;
                var threshold = float.MaxValue;
                var lastSigma = 1.0d;
                var backgroundPixels = 0L;
                int numIterations = 0;
                while (numIterations < maxIterations) {
                    ++numIterations;
                    var total = 0.0d;
                    backgroundPixels = 0L;
                    for (int i = 0; i < numPixels; ++i) {
                        var pixel = (double)imageData[i];
                        if (pixel < threshold && pixel > 0.0f) {
                            total += pixel;
                            ++backgroundPixels;
                        }
                    }

                    var mean = total / backgroundPixels;
                    var mrs = 0.0d;
                    for (int i = 0; i < numPixels; ++i) {
                        var pixel = (double)imageData[i];
                        if (pixel < threshold && pixel > 0.0f) {
                            var error = pixel - mean;
                            mrs += error * error;
                        }
                    }

                    var variance = mrs / (backgroundPixels - 1);
                    var sigma = Math.Sqrt(variance);
                    if (numIterations > 1) {
                        // NOTE: PixInsight treats this as an absolute difference rather than a percent change. That makes it faster, but less accurate
                        var sigmaConvergenceError = Math.Abs(sigma - lastSigma);
                        if (sigmaConvergenceError <= allowedError) {
                            lastSigma = sigma;
                            break;
                        }
                    }
                    threshold = (float)(mean + clippingMultipler * sigma);
                    lastSigma = sigma;
                }

                numBackgroundPixels = backgroundPixels;
                return lastSigma;
            }
        }

        public static void Binarize(Mat src, Mat dst, double threshold) {
            if (src.Size() != dst.Size() || src.Type() != dst.Type()) {
                dst.Create(src.Size(), src.Type());
            }

            unsafe {
                var srcData = (float*)src.DataPointer;
                var dstData = (float*)dst.DataPointer;
                var numPixels = src.Rows * src.Cols;
                for (int i = 0; i < numPixels; ++i) {
                    dstData[i] = (double)srcData[i] >= threshold ? 1.0f : 0.0f;
                }
            }
        }

        public static System.Drawing.Rectangle ToDrawingRectangle(this OpenCvSharp.Rect openCvRect) {
            return new System.Drawing.Rectangle(x: openCvRect.Left, y: openCvRect.Top, width: openCvRect.Width, height: openCvRect.Height);
        }

        public static Accord.Point ToAccordPoint(this Point2d point) {
            return new Accord.Point(x: (float)point.X, y: (float)point.Y);
        }

        public static DetectedStar ToDetectedStar(this Star star) {
            return new DetectedStar() {
                HFR = star.HFR,
                Position = star.Center.ToAccordPoint(),
                AverageBrightness = star.MeanBrightness,
                Background = star.Background,
                BoundingBox = star.StarBoundingBox.ToDrawingRectangle()
            };
        }

        public static System.Drawing.Color ToDrawingColor(this System.Windows.Media.Color color) {
            return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        public static System.Drawing.FontFamily ToDrawingFontFamily(this System.Windows.Media.FontFamily fontFamily) {
            return new System.Drawing.FontFamily(fontFamily.FamilyNames.Select(fn => fn.Value).First());
        }
    }
}
