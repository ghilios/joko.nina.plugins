#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestApp {

    internal class Program {
        private const string InputFilePath = @"C:\Users\ghili\Downloads\LIGHT_2022-01-08_20-40-16_H_-10.00_300.00s_0031.tif";
        private const string InputFilePath2 = @"C:\AutoFocusTestData\nik\L__2021-10-29_05-49-01__ASI2600MM_SNAPSHOT_G100_O30_2.00s_-4.80C.tif";
        private const string InputFilePath3 = @"C:\AP\Focus Points Original\12_Focuser_11250_HFR_1493.tif";
        private const string InputFilePath4 = @"C:\AP\Focus Points Original\5_Focuser_6000_HFR_0191.tif";
        private const string IntermediatePath = @"E:\StarDetectionTest\Intermediate";

        private static void Main(string[] args) {
            MainAsync(args).Wait();
        }

        private static async Task MainAsync(string[] args) {
            var starAnnotatorOptions = StaticStarAnnotatorOptions.CreateDefault();
            using (var t = new ResourcesTracker()) {
                var src = t.T(new Mat(InputFilePath, ImreadModes.Unchanged));
                var srcFloat = t.NewMat();
                ConvertToFloat(src, srcFloat);

                var srcStatistics = CvImageUtility.CalculateStatistics_Histogram(src);
                var srcLut = t.T(CvImageUtility.CreateMTFLookup(srcStatistics));
                var stretchedSrc = t.NewMat();
                CvImageUtility.ApplyLUT(src, srcLut, stretchedSrc);

                var detector = new StarDetector();
                var annotator = new HocusFocusStarAnnotator(starAnnotatorOptions, null);
                var starDetectionParams = new StarDetectionParams() { };
                var detectorParams = new StarDetectorParams() {
                    PSFFit = StarDetectorPSFFitType.Gaussian,
                    PSFGoodnessOfFitThreshold = 0.9
                    // SaveIntermediateFilesPath = IntermediatePath,
                    // CenterROICropRatio = 0.3
                };
                var detectorResult = await detector.Detect(srcFloat, detectorParams, null, CancellationToken.None);
                var detectionResult = new HocusFocusStarDetectionResult() {
                    StarList = detectorResult.DetectedStars.Select(s => s.ToDetectedStar()).ToList(),
                    DetectedStars = detectorResult.DetectedStars.Count,
                    DetectorParams = detectorParams,
                    Params = starDetectionParams,
                    Metrics = detectorResult.Metrics,
                    DebugData = detectorResult.DebugData,
                };

                var stretchedSourceBmpSrc = ToBitmapSource(stretchedSrc, PixelFormats.Gray16);
                _ = await annotator.GetAnnotatedImage(starDetectionParams, detectionResult, stretchedSourceBmpSrc);
                Console.WriteLine();
            }
        }

        private static BitmapSource ToBitmapSource(Mat src, PixelFormat pf) {
            int stride = (src.Width * pf.BitsPerPixel + 7) / 8;
            double dpi = 96;

            var dataSize = (long)src.DataEnd - (long)src.DataStart;
            var source = BitmapSource.Create(src.Width, src.Height, dpi, dpi, pf, null, src.DataStart, (int)dataSize, stride);
            source.Freeze();
            return source;
        }

        private static void ConvertToFloat(Mat src, Mat dst) {
            if (src.Size() != dst.Size() || dst.Type() != MatType.CV_32F) {
                dst.Create(src.Size(), MatType.CV_32F);
            }
            unsafe {
                var srcData = (ushort*)src.DataPointer;
                var dstData = (float*)dst.DataPointer;
                var numPixels = src.Rows * src.Cols;
                var maxShort = (float)ushort.MaxValue;
                for (int i = 0; i < numPixels; ++i) {
                    dstData[i] = (float)srcData[i] / maxShort;
                }
            }
        }

        public class StaticStarAnnotatorOptions : BaseINPC, IStarAnnotatorOptions {
            public bool ShowAnnotations { get; set; }
            public bool ShowAllStars { get; set; }
            public int MaxStars { get; set; }
            public bool ShowStarBounds { get; set; }
            public StarBoundsTypeEnum StarBoundsType { get; set; }
            public Color StarBoundsColor { get; set; }
            public ShowAnnotationTypeEnum ShowAnnotationType { get; set; }
            public Color AnnotationColor { get; set; }
            public FontFamily AnnotationFontFamily { get; set; }
            public float AnnotationFontSizePoints { get; set; }
            public bool ShowROI { get; set; }
            public Color ROIColor { get; set; }
            public bool ShowStarCenter { get; set; }
            public Color StarCenterColor { get; set; }
            public ShowStructureMapEnum ShowStructureMap { get; set; }
            public Color StructureMapColor { get; set; }
            public IStarDetectionOptions DetectorOptions { get; set; }
            public Color PSFFailedColor { get; set; }
            public Color TooFlatColor { get; set; }
            public Color SaturatedColor { get; set; }
            public Color LowSensitivityColor { get; set; }
            public Color NotCenteredColor { get; set; }
            public Color DegenerateColor { get; set; }
            public bool ShowDegenerate { get; set; }
            public bool ShowSaturated { get; set; }
            public bool ShowLowSensitivity { get; set; }
            public bool ShowNotCentered { get; set; }
            public bool ShowTooFlat { get; set; }
            public bool ShowPSFFailed { get; set; }
            public Color TooDistortedColor { get; set; }
            public bool ShowTooDistorted { get; set; }

            public static StaticStarAnnotatorOptions CreateDefault() {
                return new StaticStarAnnotatorOptions() {
                    ShowAnnotations = true,
                    ShowAllStars = true,
                    MaxStars = 200,
                    ShowStarBounds = true,
                    StarBoundsColor = Color.FromArgb(128, 255, 0, 0),
                    ShowAnnotationType = ShowAnnotationTypeEnum.FWHM,
                    AnnotationFontFamily = new FontFamily("Arial"),
                    AnnotationFontSizePoints = 18,
                    AnnotationColor = Color.FromArgb(255, 255, 255, 0),
                    StarBoundsType = StarBoundsTypeEnum.Box,
                    ShowROI = true,
                    ROIColor = Color.FromArgb(255, 255, 255, 0),
                    ShowStarCenter = true,
                    StarCenterColor = Color.FromArgb(128, 0, 0, 255),
                    ShowTooDistorted = false,
                    TooDistortedColor = Color.FromArgb(128, 255, 255, 0),
                    ShowDegenerate = false,
                    DegenerateColor = Color.FromArgb(128, 0, 255, 0),
                    ShowSaturated = false,
                    SaturatedColor = Color.FromArgb(128, 0, 255, 0),
                    ShowLowSensitivity = false,
                    LowSensitivityColor = Color.FromArgb(128, 0, 255, 0),
                    ShowNotCentered = false,
                    NotCenteredColor = Color.FromArgb(128, 0, 255, 255),
                    ShowTooFlat = false,
                    PSFFailedColor = Color.FromArgb(128, 255, 165, 0),
                    ShowPSFFailed = true,
                    TooFlatColor = Color.FromArgb(128, 0, 255, 0),
                    ShowStructureMap = ShowStructureMapEnum.None,
                    StructureMapColor = Color.FromArgb(128, 255, 0, 255)
                };
            }
        }
    }
}