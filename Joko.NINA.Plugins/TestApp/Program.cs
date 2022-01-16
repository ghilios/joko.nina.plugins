﻿#region "copyright"

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
        private const string InputFilePath = @"C:\Users\ghili\Downloads\2021-10-17_21-22-58_Ha_-10.00_3.00s_0000.tif";
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
                    SaveIntermediateFilesPath = IntermediatePath,
                    StructureDilationCount = 0
                };
                var detectorResult = await detector.Detect(srcFloat, detectorParams, null, CancellationToken.None);
                var detectionResult = new HocusFocusStarDetectionResult() {
                    StarList = detectorResult.DetectedStars.Select(s => s.ToDetectedStar()).ToList(),
                    DetectedStars = detectorResult.DetectedStars.Count,
                    DetectorParams = detectorParams,
                    Params = starDetectionParams
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
            public bool ShowHFR { get; set; }
            public Color HFRColor { get; set; }
            public FontFamily TextFontFamily { get; set; }
            public float TextFontSizePoints { get; set; }
            public bool ShowROI { get; set; }
            public Color ROIColor { get; set; }
            public bool ShowStarCenter { get; set; }
            public Color StarCenterColor { get; set; }
            public ShowStructureMapEnum ShowStructureMap { get; set; }
            public Color StructureMapColor { get; set; }
            public IStarDetectionOptions DetectorOptions { get; set; }
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

            public static StaticStarAnnotatorOptions CreateDefault() {
                return new StaticStarAnnotatorOptions() {
                    ShowAnnotations = true,
                    ShowAllStars = true,
                    MaxStars = 200,
                    ShowStarBounds = true,
                    StarBoundsColor = Color.FromArgb(128, 255, 0, 0),
                    ShowHFR = true,
                    TextFontFamily = new FontFamily("Arial"),
                    TextFontSizePoints = 18,
                    HFRColor = Color.FromArgb(255, 255, 255, 0),
                    StarBoundsType = StarBoundsTypeEnum.Box,
                    ShowROI = true,
                    ROIColor = Color.FromArgb(255, 255, 255, 0),
                    ShowStarCenter = true,
                    StarCenterColor = Color.FromArgb(128, 0, 0, 255),
                    ShowDegenerate = false,
                    DegenerateColor = Color.FromArgb(128, 0, 255, 0),
                    ShowSaturated = false,
                    SaturatedColor = Color.FromArgb(128, 0, 255, 0),
                    ShowLowSensitivity = false,
                    LowSensitivityColor = Color.FromArgb(128, 0, 255, 0),
                    ShowNotCentered = false,
                    NotCenteredColor = Color.FromArgb(128, 0, 255, 0),
                    ShowTooFlat = false,
                    TooFlatColor = Color.FromArgb(128, 0, 255, 0),
                    ShowStructureMap = ShowStructureMapEnum.None,
                    StructureMapColor = Color.FromArgb(128, 255, 0, 255)
                };
            }
        }
    }
}