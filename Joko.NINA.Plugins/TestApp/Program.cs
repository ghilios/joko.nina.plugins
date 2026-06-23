#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using KdTree;
using KdTree.Math;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
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

        private static float DotProduct(float[] x, float[] y) {
            if (x.Length != y.Length) {
                throw new ArgumentException($"x length ({x.Length}) must be equal to y length ({y.Length})");
            }
            float ssd = 0.0f;
            for (int i = 0; i < x.Length; ++i) {
                ssd += x[i] * y[i];
            }
            return ssd;
        }

        [STAThread]
        private static async Task Main(string[] args) {
            // Headless fit-quality mode: `TestApp fit-quality [--dir|--zip ...]`. Operates purely on saved AF
            // report JSONs (no profile, no images).
            if (args.Length > 0 && args[0].Equals("fit-quality", StringComparison.OrdinalIgnoreCase)) {
                FitQualityRunner.Run(args);
                return;
            }

            // Headless focus-sweep diagnostic mode: `TestApp focus-sweep --af-run <dir> ...`
            if (args.Length > 0 && args[0].Equals("focus-sweep", StringComparison.OrdinalIgnoreCase)) {
                await FocusSweepDiagnosticRunner.Run(args);
                return;
            }

            // Headless AF fit + outlier-rejection diagnostic mode: `TestApp af-fit --af-run <dir> ...`
            if (args.Length > 0 && args[0].Equals("af-fit", StringComparison.OrdinalIgnoreCase)) {
                await AfFitDiagnosticRunner.Run(args);
                return;
            }

            // Headless star-detection optimizer harness: `TestApp optimize --runs <dir> ...`
            if (args.Length > 0 && args[0].Equals("optimize", StringComparison.OrdinalIgnoreCase)) {
                await OptimizationDiagnosticRunner.Run(args);
                return;
            }

            // Headless label-classification diagnostic: `TestApp diagnose-labels --runs <dir> --labels <dir> ...`.
            // Classifies each human-labeled review box (missed/shouldReject/wronglyRejected) against a fresh
            // detection: ACCEPTED / REJECTED:<reason> / NO CANDIDATE (structure gap).
            if (args.Length > 0 && args[0].Equals("diagnose-labels", StringComparison.OrdinalIgnoreCase)) {
                DiagnoseLabelsRunner.Run(args);
                return;
            }

            // Headless label-driven gate RECOMMENDER: `TestApp recommend --runs <dir> --labels <dir> ...`. Runs the
            // analyzer + recommender the in-wizard "Optimize with feedback" uses, printing the per-gate breakdown
            // and the recommended threshold changes (precision-bounded) without launching NINA.
            if (args.Length > 0 && args[0].Equals("recommend", StringComparison.OrdinalIgnoreCase)) {
                RecommendRunner.Run(args);
                return;
            }

            // Interactive two-pass star-review labeling tool: `TestApp review --runs <dir> ...`. The runner does its
            // async loading/detection on this thread, then constructs and shows the WPF window on a dedicated STA
            // thread it creates internally — so it is correct regardless of this thread's apartment (an async Main
            // can resume off the [STAThread] main thread after an await, which would otherwise crash window ctor).
            if (args.Length > 0 && args[0].Equals("review", StringComparison.OrdinalIgnoreCase)) {
                StarReview.StarReviewRunner.Run(args);
                return;
            }

            // Minimal CSV-driven annotator: `TestApp annotate --image <frame> --stars <csv> ...` (or --runs).
            // Overlays an external star list on the real plugin MTF stretch; also exports plain stretched PNGs.
            if (args.Length > 0 && args[0].Equals("annotate", StringComparison.OrdinalIgnoreCase)) {
                await AnnotateRunner.Run(args);
                return;
            }

            // AF-bank verification orchestrators (Steps 1-5 of plans/autofocus-bank-verification-plan.md):
            //   bank-clean    — idempotent dry-run-first cleanup of stale per-config clutter in each run folder.
            //   export-linear — write linear mono-FITS sidecars (NINA loader: XISF + debayer) so the golden
            //                   reference detector can read every run, not just the mono-FITS ones.
            //   bank-donut-meta — per-run donut-aware decision -> run_meta.json (refined heuristic).
            //   bank-verify   — per run x config {C0 as-default, A optimized, B optimized+donut} metrics ->
            //                   timestamped verification_<UTC>.{json,md} at the bank root.
            if (args.Length > 0 && args[0].Equals("bank-clean", StringComparison.OrdinalIgnoreCase)) {
                BankCleanRunner.Run(args);
                return;
            }
            if (args.Length > 0 && args[0].Equals("export-linear", StringComparison.OrdinalIgnoreCase)) {
                await ExportLinearRunner.Run(args);
                return;
            }
            if (args.Length > 0 && args[0].Equals("bank-donut-meta", StringComparison.OrdinalIgnoreCase)) {
                await BankDonutMetaRunner.Run(args);
                return;
            }
            if (args.Length > 0 && args[0].Equals("bank-verify", StringComparison.OrdinalIgnoreCase)) {
                await BankVerifyRunner.Run(args);
                return;
            }

            // Headless aberration-inspector alignment reproducer: `TestApp inspect-align --runs <folder> ...`.
            // Drives the real SensorModel.RegisterStarsAndFit RANSAC alignment and reports the reference frame,
            // per-frame triangle counts, frames aligned, and every registration warning.
            if (args.Length > 0 && args[0].Equals("inspect-align", StringComparison.OrdinalIgnoreCase)) {
                await InspectAlignRunner.Run(args);
                return;
            }

            // Headless tilt-calibration validator: `TestApp tilt --dataset <folder> ...`. Reproduces the Tilt
            // Adapter Wizard's calibration over a bank of saved AF runs and reports computed screw angles +
            // recovered hardware against ground-truth metadata. Optionally runs (and persists) star-detection
            // optimization first.
            if (args.Length > 0 && args[0].Equals("tilt", StringComparison.OrdinalIgnoreCase)) {
                await TiltCalibrationRunner.Run(args);
                return;
            }

            // Golden-set audit tools: `TestApp golden tiles --runs <dir> ...` renders MTF-stretched tiles for
            // visual golden-set authoring (no detection); `TestApp golden eval --runs <dir> ...` runs the real
            // detector and scores precision/recall against the visual golden.json. See .claude/docs/golden-star-set.md.
            if (args.Length > 0 && args[0].Equals("golden", StringComparison.OrdinalIgnoreCase)) {
                var sub = args.Length > 1 ? args[1] : "";
                if (sub.Equals("tiles", StringComparison.OrdinalIgnoreCase)) {
                    await GoldenRunner.Run(args);
                } else if (sub.Equals("eval", StringComparison.OrdinalIgnoreCase)) {
                    await GoldenEvalRunner.Run(args);
                } else {
                    Console.Error.WriteLine("Usage: TestApp golden tiles|eval --runs <dir> ...");
                    Environment.ExitCode = 2;
                }
                return;
            }

            // Headless contamination diagnostic mode: `TestApp contamination --image <path> ...` (or any
            // invocation that passes --image). Otherwise fall through to the existing WPF GUI.
            bool diagnosticMode = args.Length > 0 &&
                (args[0].Equals("contamination", StringComparison.OrdinalIgnoreCase)
                 || args.Any(a => a.Equals("--image", StringComparison.OrdinalIgnoreCase)));
            if (diagnosticMode) {
                await ContaminationDiagnosticRunner.Run(args);
                return;
            }

            // No diagnostic args: preserve the original WPF window behavior (App.OnStartup shows MainWindow).
            App.Main();
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

        // Delegates to the shared plugin helper so the Mat->BitmapSource conversion lives in one place (the same
        // helper the review UI/wizard use). Kept here for TestApp's existing GUI/annotation path callers.
        public static BitmapSource ToBitmapSource(Mat src, PixelFormat pf) => StarReviewImaging.ToBitmapSource(src, pf);

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
            public bool ShowContaminated { get; set; }
            public Color ContaminatedColor { get; set; }

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
                    ShowContaminated = false,
                    ContaminatedColor = Color.FromArgb(128, 255, 0, 255),
                    ShowStructureMap = ShowStructureMapEnum.None,
                    StructureMapColor = Color.FromArgb(128, 255, 0, 255)
                };
            }
        }
    }
}
