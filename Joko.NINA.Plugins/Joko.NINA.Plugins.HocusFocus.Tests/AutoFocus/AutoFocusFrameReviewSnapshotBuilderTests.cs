using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class AutoFocusFrameReviewSnapshotBuilderTests {

        // ---- helpers ------------------------------------------------------------------------------------

        private static BitmapSource MakeBitmap(int w = 2, int h = 2) {
            var stride = w;
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, new byte[stride * h], stride);
            bmp.Freeze();
            return bmp;
        }

        private static IRenderedImage MakeImage(int w = 2, int h = 2) {
            var img = Substitute.For<IRenderedImage>();
            img.Image.Returns(MakeBitmap(w, h));
            return img;
        }

        private static PSFModel MakePsf(
            double offsetX = 0.3, double offsetY = -0.4,
            double peak = 1000, double background = 50,
            double fwhmX = 4.0, double fwhmY = 2.0,
            double thetaRadians = Math.PI / 4, double pixelScale = 1.5, double beta = 4.0) {
            return new PSFModel(StarDetectorPSFFitType.Moffat_40, offsetX, offsetY, peak, background,
                sigmaX: fwhmX / 2.0, sigmaY: fwhmY / 2.0, fwhmX: fwhmX, fwhmY: fwhmY,
                thetaRadians: thetaRadians, rSquared: 0.9, pixelScale: pixelScale, reducedChiSquared: 1.0, beta: beta);
        }

        private static HocusFocusDetectedStar MakeStar(
            double hfr = 2.0, double posX = 10, double posY = 20, double background = 12.5,
            int boxX = 8, int boxY = 18, int boxW = 5, int boxH = 7,
            PSFModel psf = null) {
            return new HocusFocusDetectedStar {
                HFR = hfr,
                Position = new Accord.Point((float)posX, (float)posY),
                Background = background,
                BoundingBox = new System.Drawing.Rectangle(boxX, boxY, boxW, boxH),
                PSF = psf,
            };
        }

        private static (double, HocusFocusStarDetectionResult, IRenderedImage) Frame(
            double focuserPosition,
            IEnumerable<HocusFocusDetectedStar> stars,
            IRenderedImage image,
            StarDetectorMetrics metrics = null,
            StarDetectorParams detectorParams = null) {
            var result = new HocusFocusStarDetectionResult {
                StarList = stars?.Cast<DetectedStar>().ToList(),
                Metrics = metrics,
                DetectorParams = detectorParams,
            };
            return (focuserPosition, result, image);
        }

        // ---- tests --------------------------------------------------------------------------------------

        [Test]
        public void Build_OrdersFramesByFocuserPosition_StableForEqualPositions() {
            var f300 = Frame(300, new[] { MakeStar() }, MakeImage());
            var f100 = Frame(100, new[] { MakeStar() }, MakeImage());
            var f200a = Frame(200, new[] { MakeStar(posX: 1) }, MakeImage());
            var f200b = Frame(200, new[] { MakeStar(posX: 2) }, MakeImage());

            var snapshot = AutoFocusFrameReviewSnapshotBuilder.Build(new[] { f300, f100, f200a, f200b });

            Assert.Multiple(() => {
                Assert.That(snapshot.Frames.Select(f => f.FocuserPosition), Is.EqualTo(new[] { 100.0, 200.0, 200.0, 300.0 }));
                // Original capture index preserved, and the two 200s keep their capture order (stable sort).
                Assert.That(snapshot.Frames.Select(f => f.FrameIndex), Is.EqualTo(new[] { 1, 2, 3, 0 }));
                Assert.That(snapshot.Frames[1].Stars.Single().CenterX, Is.EqualTo(1)); // f200a before f200b
                Assert.That(snapshot.Frames[2].Stars.Single().CenterX, Is.EqualTo(2));
            });
        }

        [Test]
        public void Build_DropsFramesWithNullImageOrNullInnerImage() {
            var nullImage = Frame(100, new[] { MakeStar() }, null);
            var nullInner = Substitute.For<IRenderedImage>();
            nullInner.Image.Returns((BitmapSource)null);
            var nullInnerFrame = Frame(200, new[] { MakeStar() }, nullInner);
            var good = Frame(300, new[] { MakeStar() }, MakeImage());

            var snapshot = AutoFocusFrameReviewSnapshotBuilder.Build(new[] { nullImage, nullInnerFrame, good });

            Assert.Multiple(() => {
                Assert.That(snapshot.Frames.Count, Is.EqualTo(1));
                Assert.That(snapshot.Frames.Single().FocuserPosition, Is.EqualTo(300.0));
                Assert.That(snapshot.Frames.Single().FrameIndex, Is.EqualTo(2));
            });
        }

        [Test]
        public void Build_PopulatesStarGeometryAndCount() {
            var frame = Frame(100, new[] {
                MakeStar(hfr: 3.5, posX: 12, posY: 24, boxX: 9, boxY: 21, boxW: 6, boxH: 8),
                MakeStar(posX: 50),
            }, MakeImage());

            var snapshot = AutoFocusFrameReviewSnapshotBuilder.Build(new[] { frame });
            var f = snapshot.Frames.Single();
            var s = f.Stars.First();

            Assert.Multiple(() => {
                Assert.That(f.DetectedStarCount, Is.EqualTo(2));
                Assert.That(s.CenterX, Is.EqualTo(12)); // Position, NOT the box top-left
                Assert.That(s.CenterY, Is.EqualTo(24));
                Assert.That(s.BoxX, Is.EqualTo(9));
                Assert.That(s.BoxY, Is.EqualTo(21));
                Assert.That(s.BoxWidth, Is.EqualTo(6));
                Assert.That(s.BoxHeight, Is.EqualTo(8));
                Assert.That(s.Hfr, Is.EqualTo(3.5));
            });
        }

        [Test]
        public void Build_StarWithoutPsf_HasNoPsfAndNaNDerivedFields() {
            var frame = Frame(100, new[] { MakeStar(psf: null, background: 7.0) }, MakeImage());

            var s = AutoFocusFrameReviewSnapshotBuilder.Build(new[] { frame }).Frames.Single().Stars.Single();

            Assert.Multiple(() => {
                Assert.That(s.HasPsf, Is.False);
                Assert.That(s.Background, Is.EqualTo(7.0)); // star background is NOT PSF-gated
                Assert.That(s.FwhmArcsec, Is.NaN);
                Assert.That(s.FwhmPixels, Is.NaN);
                Assert.That(s.FwhmX, Is.NaN);
                Assert.That(s.FwhmY, Is.NaN);
                Assert.That(s.Eccentricity, Is.NaN);
                Assert.That(s.PsfThetaDegrees, Is.NaN);
                Assert.That(s.PsfBackground, Is.NaN);
                Assert.That(s.PsfPeak, Is.NaN);
                Assert.That(s.MoffatBeta, Is.NaN);
            });
        }

        [Test]
        public void Build_StarWithPsf_PopulatesPsfFieldsAndThetaInDegrees() {
            var psf = MakePsf(offsetX: 0.3, offsetY: -0.4, peak: 1234, background: 56,
                fwhmX: 4.0, fwhmY: 2.0, thetaRadians: Math.PI / 2, pixelScale: 1.5, beta: 4.0);
            var frame = Frame(100, new[] { MakeStar(psf: psf) }, MakeImage());

            var s = AutoFocusFrameReviewSnapshotBuilder.Build(new[] { frame }).Frames.Single().Stars.Single();

            Assert.Multiple(() => {
                Assert.That(s.HasPsf, Is.True);
                Assert.That(s.PsfOffsetX, Is.EqualTo(0.3).Within(1e-9));
                Assert.That(s.PsfOffsetY, Is.EqualTo(-0.4).Within(1e-9));
                Assert.That(s.FwhmX, Is.EqualTo(4.0).Within(1e-9));
                Assert.That(s.FwhmY, Is.EqualTo(2.0).Within(1e-9));
                Assert.That(s.FwhmPixels, Is.EqualTo(Math.Sqrt(8.0)).Within(1e-9));
                Assert.That(s.FwhmArcsec, Is.EqualTo(Math.Sqrt(8.0) * 1.5).Within(1e-9));
                Assert.That(s.PsfPeak, Is.EqualTo(1234).Within(1e-9));
                Assert.That(s.PsfBackground, Is.EqualTo(56).Within(1e-9));
                Assert.That(s.MoffatBeta, Is.EqualTo(4.0).Within(1e-9));
                Assert.That(s.PsfThetaDegrees, Is.EqualTo(90.0).Within(1e-6)); // π/2 rad → 90°
            });
        }

        [Test]
        public void Build_MapsAllSevenRejectionReasonRects() {
            var metrics = new StarDetectorMetrics();
            metrics.TooDistortedBounds.Add(new OpenCvSharp.Rect(1, 2, 3, 4));
            metrics.DegenerateBounds.Add(new OpenCvSharp.Rect(5, 6, 7, 8));
            metrics.SaturatedBounds.Add(new OpenCvSharp.Rect(9, 10, 11, 12));
            metrics.LowSensitivityBounds.Add(new OpenCvSharp.Rect(13, 14, 15, 16));
            metrics.NotCenteredBounds.Add(new OpenCvSharp.Rect(17, 18, 19, 20));
            metrics.TooFlatBounds.Add(new OpenCvSharp.Rect(21, 22, 23, 24));
            metrics.ContaminatedBounds.Add(new OpenCvSharp.Rect(25, 26, 27, 28));
            var frame = Frame(100, new[] { MakeStar() }, MakeImage(), metrics: metrics);

            var f = AutoFocusFrameReviewSnapshotBuilder.Build(new[] { frame }).Frames.Single();

            Assert.Multiple(() => {
                Assert.That(f.TooDistorted.Single(), Is.EqualTo(new System.Windows.Rect(1, 2, 3, 4)));
                Assert.That(f.Degenerate.Single(), Is.EqualTo(new System.Windows.Rect(5, 6, 7, 8)));
                Assert.That(f.Saturated.Single(), Is.EqualTo(new System.Windows.Rect(9, 10, 11, 12)));
                Assert.That(f.LowSensitivity.Single(), Is.EqualTo(new System.Windows.Rect(13, 14, 15, 16)));
                Assert.That(f.NotCentered.Single(), Is.EqualTo(new System.Windows.Rect(17, 18, 19, 20)));
                Assert.That(f.TooFlat.Single(), Is.EqualTo(new System.Windows.Rect(21, 22, 23, 24)));
                Assert.That(f.Contaminated.Single(), Is.EqualTo(new System.Windows.Rect(25, 26, 27, 28)));
            });
        }

        [Test]
        public void Build_NullMetrics_AllReasonRectListsEmptyNotNull() {
            var frame = Frame(100, new[] { MakeStar() }, MakeImage(), metrics: null);

            var f = AutoFocusFrameReviewSnapshotBuilder.Build(new[] { frame }).Frames.Single();

            Assert.Multiple(() => {
                Assert.That(f.TooDistorted, Is.Empty);
                Assert.That(f.Degenerate, Is.Empty);
                Assert.That(f.Saturated, Is.Empty);
                Assert.That(f.LowSensitivity, Is.Empty);
                Assert.That(f.NotCentered, Is.Empty);
                Assert.That(f.TooFlat, Is.Empty);
                Assert.That(f.Contaminated, Is.Empty);
                Assert.That(f.RoiRects, Is.Empty);
            });
        }

        [Test]
        public void Build_RoiRects_EmptyForFullOrNullRegion() {
            var nullParams = Frame(100, new[] { MakeStar() }, MakeImage(), detectorParams: null);
            var fullRegion = Frame(200, new[] { MakeStar() }, MakeImage(),
                detectorParams: new StarDetectorParams { Region = StarDetectionRegion.Full });

            var snapshot = AutoFocusFrameReviewSnapshotBuilder.Build(new[] { nullParams, fullRegion });

            Assert.Multiple(() => {
                Assert.That(snapshot.Frames[0].RoiRects, Is.Empty);
                Assert.That(snapshot.Frames[1].RoiRects, Is.Empty);
            });
        }

        [Test]
        public void Build_RoiRects_ScalesOuterAndInnerToImagePixels() {
            // 100x80 image; inner crop must be entirely contained within the outer boundary → two rects scaled to pixels.
            var region = new StarDetectionRegion(new RatioRect(0.1, 0.1, 0.8, 0.8), new RatioRect(0.2, 0.25, 0.4, 0.3));
            var frame = Frame(100, new[] { MakeStar() }, MakeImage(100, 80),
                detectorParams: new StarDetectorParams { Region = region });

            var roi = AutoFocusFrameReviewSnapshotBuilder.Build(new[] { frame }).Frames.Single().RoiRects;

            Assert.Multiple(() => {
                Assert.That(roi.Count, Is.EqualTo(2));
                Assert.That(roi[0], Is.EqualTo(new System.Windows.Rect(10, 8, 80, 64)));    // outer × (100,80)
                Assert.That(roi[1], Is.EqualTo(new System.Windows.Rect(20, 20, 40, 24)));   // inner × (100,80)
            });
        }

        [Test]
        public void Build_EmptyInput_ProducesEmptySnapshot() {
            Assert.That(AutoFocusFrameReviewSnapshotBuilder.Build(Array.Empty<(double, HocusFocusStarDetectionResult, IRenderedImage)>()).Frames, Is.Empty);
        }
    }
}
