#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILNumerics;
using ILNumerics.Drawing;
using ILNumerics.Drawing.Plotting;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using static ILNumerics.Globals;
using static ILNumerics.ILMath;
using DashStyle = ILNumerics.Drawing.DashStyle;
using DrawingColor = System.Drawing.Color;
using FormsApplication = System.Windows.Forms.Application;

namespace TestApp {

    internal class Program {
        private const string InputFilePath = @"C:\Users\ghili\Downloads\LIGHT_2022-01-08_20-40-16_H_-10.00_300.00s_0031.tif";
        private const string InputFilePath2 = @"C:\AutoFocusTestData\nik\L__2021-10-29_05-49-01__ASI2600MM_SNAPSHOT_G100_O30_2.00s_-4.80C.tif";
        private const string InputFilePath3 = @"C:\AP\Focus Points Original\12_Focuser_11250_HFR_1493.tif";
        private const string InputFilePath4 = @"C:\AP\Focus Points Original\5_Focuser_6000_HFR_0191.tif";
        private const string IntermediatePath = @"E:\StarDetectionTest\Intermediate";

        [STAThread]
        private static void Main(string[] args) {
            /*
            var folder = @"E:\AutoFocusSaves\AutoFocus_20221102_223808\attempt01";
            var files = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly);

            var attemptFolder = new DirectoryInfo(folder);
            var attemptRegex = new Regex(@"^attempt(?<ATTEMPT>\d+)$", RegexOptions.Compiled);
            var attemptMatch = attemptRegex.Match(attemptFolder.Name);
            int attemptNumber = -1;
            if (attemptMatch.Success) {
                attemptNumber = int.Parse(attemptMatch.Groups["ATTEMPT"].Value);
            }

            var imageFileRegex = new Regex(@"^(?<IMAGE_INDEX>\d+)_Focuser(?<FOCUSER>\d+)_HFR(?<HFR>(\d+)(\.\d+)?)$", RegexOptions.Compiled);
            var allFiles = attemptFolder.GetFiles();
            foreach (var file in allFiles) {
                var fileNameNoExtension = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                var match = imageFileRegex.Match(fileNameNoExtension);
                if (match.Success) {
                    var focuser = int.Parse(match.Groups["FOCUSER"].Value);
                    var hfr = double.Parse(match.Groups["HFR"].Value);
                    Console.WriteLine($"Found image file: {file.Name}, Focuser: {focuser}, HFR: {hfr}");
                }
            }
            */

            var a = new App();
            // a.StartupUri = new Uri("App.xaml", System.UriKind.Relative);
            a.Run();

            /*
            FormsApplication.EnableVisualStyles();
            FormsApplication.SetCompatibleTextRenderingDefault(false);
            var form = new ILForm();

            Array<float> angles = linspace<float>(0, (float)pi * 2f, 6);
            Array<float> pos = zeros<float>(3, 6);
            pos["0;:"] = sin(angles);
            pos["1;:"] = cos(angles);

            var scene = new Scene();
            // get terrain data, convert to single precision
            Array<float> A = tosingle(SpecialData.terrain[r(120, end), r(0, 310)]);
            scene.Add(
              // create plot cube
              new PlotCube(twoDMode: true) {
	            // create contour plot
	            new ContourPlot(A, create3D: false,
                    levels: new List<ContourLevel> {
			            // configure individual contour levels
			            new ContourLevel() { Text = "Coast", Value = 5, LineWidth = 3, LabelColor = DrawingColor.Azure },
                        new ContourLevel() { Text = "Plateau", Value = 1000, LineWidth = 3},
                        new ContourLevel() { Text = "Basis 1", Value = 1500, LineWidth = 3, LineStyle = DashStyle.PointDash },
                        new ContourLevel() { Text = "High", Value = 3000, LineWidth = 3, LineColor = 0 },
                        new ContourLevel() { Text = @"\fontname{Colonna MT}\bf\fontsize{+4}Rescue", Value = 4200, LineWidth = 3,
                                                          LineStyle = DashStyle.Dotted },
                        new ContourLevel() { Text = "Peak", Value = 5000, LineWidth = 3},
                    }),
	            // add surface with the same data
	            new Surface(A) {
	  	            // disable wireframe
		            Wireframe = { Visible = false },
                    UseLighting = true,
                    Children = {
                      new Legend { Location = new PointF(1f,.1f) },
                      new Colorbar {
                          Location = new PointF(1,.4f),
                          Anchor = new PointF(1,0)
                      }
                    }
            });

            scene.First<PlotCube>().AspectRatioMode = AspectRatioMode.StretchToFill;
            form.panel1.Scene = scene;
            Application.Run(form);
            */

            // Console.WriteLine();

            // MainAsync(args).Wait();
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
                    ModelPSF = true,
                    PSFFitType = StarDetectorPSFFitType.Moffat_40,
                    PSFParallelPartitionSize = 0,
                    PSFResolution = 10,
                    PSFGoodnessOfFitThreshold = 0.9,
                    PixelScale = 1.1d,
                    // SaveIntermediateFilesPath = IntermediatePath,
                    Region = new StarDetectionRegion(RatioRect.FromCenterROI(0.3))
                };
                var detectorResult = await detector.Detect(srcFloat, detectorParams, null, CancellationToken.None);
                var detectionResult = new HocusFocusStarDetectionResult() {
                    StarList = detectorResult.DetectedStars.Select(s => HocusFocusStarDetection.ToDetectedStar(s)).ToList(),
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

        public static BitmapSource ToBitmapSource(Mat src, PixelFormat pf) {
            int stride = (src.Width * pf.BitsPerPixel + 7) / 8;
            double dpi = 96;

            var dataSize = (long)src.DataEnd - (long)src.DataStart;
            var source = BitmapSource.Create(src.Width, src.Height, dpi, dpi, pf, null, src.DataStart, (int)dataSize, stride);
            source.Freeze();
            return source;
        }

        public static void ConvertToFloat(Mat src, Mat dst) {
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
                    TooFlatColor = Color.FromArgb(128, 0, 255, 0),
                    ShowStructureMap = ShowStructureMapEnum.None,
                    StructureMapColor = Color.FromArgb(128, 255, 0, 255)
                };
            }
        }
    }
}