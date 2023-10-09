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
using KdTree;
using KdTree.Math;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.WPF.Base.Utility.AutoFocus;
using OpenCvSharp;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public class SavedStars {
            public int FocuserPosition { get; set; }
            public List<HocusFocusDetectedStar> StarList { get; set; }
        }

        public class DetectedStarIndex {

            public DetectedStarIndex(int index, HocusFocusDetectedStar star) {
                this.Index = index;
                this.DetectedStar = star;
            }

            public int Index { get; private set; }
            public HocusFocusDetectedStar DetectedStar { get; private set; }
        }

        public class MatchingPair {
            public int SourceIndex { get; set; }
            public int GlobalIndex { get; set; }
        }

        public class MatchedStar {
            public int FocuserPosition { get; set; }
            public HocusFocusDetectedStar Star { get; set; }

            public override string ToString() {
                return $"{{{nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(Star)}={Star}}}";
            }
        }

        public class MatchedStars {
            public double RegistrationX { get; set; } = double.NaN;
            public double RegistrationY { get; set; } = double.NaN;
            public double? FitFocuserPosition { get; set; }
            public double? FitHFR { get; set; }
            public double? FitRSquared { get; set; }
            public string FitExpression { get; set; } = null;
            public List<MatchedStar> StarsUsedInCalculation { get; set; }

            public override string ToString() {
                return $"{{{nameof(RegistrationX)}={RegistrationX.ToString()}, {nameof(RegistrationY)}={RegistrationY.ToString()}, {nameof(FitFocuserPosition)}={FitFocuserPosition.ToString()}, {nameof(FitHFR)}={FitHFR.ToString()}, {nameof(FitRSquared)}={FitRSquared.ToString()}, {nameof(FitExpression)}={FitExpression}, {nameof(StarsUsedInCalculation)}={StarsUsedInCalculation}}}";
            }
        }

        [STAThread]
        private static async Task Main(string[] args) {
            /*
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await MainAsync(args);
            stopwatch.Stop();
            Console.WriteLine($"Elapsed: {stopwatch.Elapsed}");
            */

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

            /*
            var a = new App();
            a.Run();
            */

            var alglibAPI = new AlglibAPI();
            using (var stopwatch = MultiStopWatch.Measure()) {
                var path = @"E:\TiltSavedAF";
                var allDetectedStars = await Task.WhenAll(Directory.GetFiles(path, "*_result.json").Select(async filePath => {
                    using (var reader = File.OpenText(filePath)) {
                        var text = await reader.ReadToEndAsync();
                        var savedStars = JsonConvert.DeserializeObject<SavedStars>(text);
                        return savedStars;
                    }
                }));
                stopwatch.RecordEntry("parse");

                var allDetectedStarTrees = allDetectedStars.Select(result => {
                    var tree = new KdTree<float, DetectedStarIndex>(2, new FloatMath(), AddDuplicateBehavior.Error);
                    foreach (var (star, starIndex) in result.StarList.Select((star, starIndex) => (star, starIndex))) {
                        tree.Add(new[] { star.Position.X, star.Position.Y }, new DetectedStarIndex(starIndex, star));
                    }
                    return tree;
                }).ToArray();
                stopwatch.RecordEntry("build trees");

                float searchRadius = 50;
                var globalRegistry = new KdTree<float, DetectedStarIndex>(2, new FloatMath(), AddDuplicateBehavior.Error);
                var starIndexMap = Enumerable.Range(0, allDetectedStars.Length).Select(i => new Dictionary<int, int>()).ToArray();
                foreach (var starNode in allDetectedStarTrees[0]) {
                    var nextIndex = globalRegistry.Count;
                    globalRegistry.Add(starNode.Point, new DetectedStarIndex(nextIndex, starNode.Value.DetectedStar));
                    starIndexMap[0].Add(starNode.Value.Index, nextIndex);
                }

                for (int i = 1; i < allDetectedStars.Length; ++i) {
                    var nextStarList = allDetectedStars[i].StarList;
                    var nextStarTree = allDetectedStarTrees[i];
                    var nextStarIndexMap = starIndexMap[i];
                    var matchedGlobalStars = new bool[globalRegistry.Count];
                    var matchedSourceStars = new bool[nextStarTree.Count];
                    var queue = new KdTree.PriorityQueue<MatchingPair, float>(new FloatMath());
                    foreach (var (starNode, starNodeIndex) in nextStarTree.Select((starNode, starNodeIndex) => (starNode, starNodeIndex))) {
                        var sourceStar = starNode.Value.DetectedStar;
                        var sourcePoint = starNode.Point;
                        var sourceIndex = starNode.Value.Index;
                        var globalNeighbors = globalRegistry.RadialSearch(sourcePoint, searchRadius);
                        foreach (var globalNeighbor in globalNeighbors) {
                            var globalNeighborIndex = globalNeighbor.Value.Index;
                            var distance = DotProduct(globalNeighbor.Point, sourcePoint);
                            queue.Enqueue(new MatchingPair() { SourceIndex = sourceIndex, GlobalIndex = globalNeighborIndex }, distance);
                        }
                    }

                    while (queue.Count > 0) {
                        var nextCandidate = queue.Dequeue();
                        if (matchedGlobalStars[nextCandidate.GlobalIndex] || matchedSourceStars[nextCandidate.SourceIndex]) {
                            continue;
                        }

                        nextStarIndexMap.Add(nextCandidate.SourceIndex, nextCandidate.GlobalIndex);
                        matchedGlobalStars[nextCandidate.GlobalIndex] = true;
                        matchedSourceStars[nextCandidate.SourceIndex] = true;
                    }

                    for (int j = 0; j < matchedSourceStars.Length; ++j) {
                        if (matchedSourceStars[j]) {
                            continue;
                        }

                        // Now we've found a star that didn't match in the global registry. Add it to the registry for future matches
                        var star = nextStarList[j];
                        var nextGlobalIndex = globalRegistry.Count;
                        globalRegistry.Add(new[] { star.Position.X, star.Position.Y }, new DetectedStarIndex(nextGlobalIndex, star));
                        nextStarIndexMap.Add(j, nextGlobalIndex);
                    }
                }

                var allMatchedStars = new MatchedStars[globalRegistry.Count];
                foreach (var globalNode in globalRegistry) {
                    var matchedStars = new MatchedStars() {
                        StarsUsedInCalculation = new List<MatchedStar>(),
                        RegistrationX = globalNode.Value.DetectedStar.Position.X,
                        RegistrationY = globalNode.Value.DetectedStar.Position.Y
                    };
                    allMatchedStars[globalNode.Value.Index] = matchedStars;
                }
                for (int i = 0; i < starIndexMap.Length; ++i) {
                    var nextStarIndexMap = starIndexMap[i];
                    var focuserPosition = allDetectedStars[i].FocuserPosition;
                    var detectedStars = allDetectedStars[i].StarList;
                    foreach (var nextKvp in nextStarIndexMap) {
                        var sourceIndex = nextKvp.Key;
                        var globalIndex = nextKvp.Value;
                        var sourceStar = detectedStars[sourceIndex];
                        var matchedStar = new MatchedStar() {
                            FocuserPosition = focuserPosition,
                            Star = sourceStar
                        };
                        allMatchedStars[globalIndex].StarsUsedInCalculation.Add(matchedStar);
                    }
                }
                stopwatch.RecordEntry("registration");

                allMatchedStars.AsParallel().ForAll(matchedStars => {
                    if (matchedStars.StarsUsedInCalculation.Count < 5) {
                        return;
                    }

                    try {
                        var points = matchedStars.StarsUsedInCalculation.Select(s => new ScatterErrorPoint(s.FocuserPosition, s.Star.HFR, 0.0d, 0.0d)).ToList();

                        var fitting2 = HyperbolicFittingAlglib.Create(alglibAPI, points, true);
                        var solveResult = fitting2.Solve();
                        if (!solveResult) {
                            throw new Exception("WTF");
                        }

                        matchedStars.FitFocuserPosition = fitting2.Minimum.X;
                        matchedStars.FitHFR = fitting2.Minimum.Y;
                        matchedStars.FitRSquared = fitting2.RSquared;
                        matchedStars.FitExpression = fitting2.Expression;
                    } catch (Exception e) {
                        Console.WriteLine($"Failed to calculate hyperbolic at ({matchedStars.RegistrationX}, {matchedStars.RegistrationY}). Error={e.Message}");
                    }
                });
                stopwatch.RecordEntry("fits");

                allMatchedStars = allMatchedStars.OrderBy(s => s.RegistrationY).ThenBy(s => s.RegistrationX).ToArray();
                Console.WriteLine($"Fit all stars. {allMatchedStars.Length}");

                var sensorModelDataPoints = allMatchedStars
                    .Where(s => s.FitFocuserPosition.HasValue)
                    .Select(s => new SensorParaboloidDataPoint(s.RegistrationX, s.RegistrationY, s.FitFocuserPosition.Value, s.FitRSquared.Value))
                    .ToList();

                var (medianFocusPosition, _) = allMatchedStars.Where(s => s.FitFocuserPosition.HasValue).Select(s => s.FitFocuserPosition.Value).MedianMAD();
                var sensorModelSolver = new SensorParaboloidSolver(dataPoints: sensorModelDataPoints, sensorSizeMicronsX: 9576 * 3.76, sensorSizeMicronsY: 6388 * 3.76, inFocusMicrons: medianFocusPosition, fixedSensorCenter: true);
                var nlSolver = new NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel>(alglibAPI);
                nlSolver.OptGuardEnabled = true;
                var solution = nlSolver.SolveWinsorizedResiduals(sensorModelSolver);
                var goodnessOfFit = nlSolver.GoodnessOfFit(sensorModelSolver, solution);
                var rmsError = nlSolver.RMSError(sensorModelSolver, solution);
                Console.WriteLine($"Solved fit: {solution}. RMS = {rmsError:0.0000}, GoF = {goodnessOfFit:0.0000}");
                stopwatch.RecordEntry("model sensor NL");

                var solution2 = nlSolver.SolveIRLS(sensorModelSolver);
                var goodnessOfFit2 = nlSolver.GoodnessOfFit(sensorModelSolver, solution2);
                var rmsError2 = nlSolver.RMSError(sensorModelSolver, solution2);
                Console.WriteLine($"Solved IRLS fit: {solution2}. RMS = {rmsError2:0.0000}, GoF = {goodnessOfFit2:0.0000}");
                stopwatch.RecordEntry("model sensor IRLS");

                Console.WriteLine(stopwatch.GenerateString());
            }

            /*
            Console.WriteLine($"{allMatchedStars.Length} registered stars");
            var resultTargetPath = @"E:\TiltSavedAF\fits_per_star.json";
            File.WriteAllText(resultTargetPath, JsonConvert.SerializeObject(allMatchedStars, Formatting.Indented));
            Console.WriteLine();
            */
        }

        private static float DotProduct(float[] x, float[] y) {
            if (x.Length != y.Length) {
                throw new ArgumentException($"x length ({x.Length}) must be equal to y length ({y.Length})");
            }
            float ssd = 0.0f;
            for (int i = 0; i < x.Length; ++i) {
                var diff = y[i] - x[i];
                ssd += diff * diff;
            }
            return ssd;
        }

        private static async Task MainAsync(string[] args) {
            var starAnnotatorOptions = StaticStarAnnotatorOptions.CreateDefault();
            var alglibAPI = new AlglibAPI();
            using (var t = new ResourcesTracker()) {
                var src = t.T(new Mat(InputFilePath, ImreadModes.Unchanged));
                var srcFloat = t.NewMat();
                ConvertToFloat(src, srcFloat);

                var srcStatistics = CvImageUtility.CalculateStatistics_Histogram(src);
                var srcLut = t.T(CvImageUtility.CreateMTFLookup(srcStatistics));
                var stretchedSrc = t.NewMat();
                CvImageUtility.ApplyLUT(src, srcLut, stretchedSrc);

                var detector = new StarDetector(alglibAPI);
                var annotator = new HocusFocusStarAnnotator(starAnnotatorOptions, null);
                var starDetectionParams = new StarDetectionParams() { };
                var detectorParams = new StarDetectorParams() {
                    PSFFitType = StarDetectorPSFFitType.Gaussian,
                    PSFParallelPartitionSize = 0,
                    UsePSFAbsoluteDeviation = false
                    //Region = new StarDetectionRegion(RatioRect.Full, RatioRect.FromCenterROI(0.5))
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