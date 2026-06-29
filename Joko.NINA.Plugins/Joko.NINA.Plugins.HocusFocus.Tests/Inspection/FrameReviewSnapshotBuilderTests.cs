using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NSubstitute;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection {

    [TestFixture]
    public class FrameReviewSnapshotBuilderTests {

        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        // ---- helpers ------------------------------------------------------------------------------------

        private static BitmapSource MakeBitmap() {
            var bmp = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Gray8, null, new byte[] { 0, 0, 0, 0 }, 2);
            bmp.Freeze();
            return bmp;
        }

        private static IRenderedImage MakeImage() {
            var img = Substitute.For<IRenderedImage>();
            img.Image.Returns(MakeBitmap());
            return img;
        }

        private static HocusFocusDetectedStar MakeStar(
            double hfr = 2.0,
            double posX = 10, double posY = 20,
            double? origX = null, double? origY = null,
            int boxX = 8, int boxY = 18, int boxW = 5, int boxH = 7,
            int? origBoxX = null, int? origBoxY = null, int? origBoxW = null, int? origBoxH = null) {
            return new HocusFocusDetectedStar {
                HFR = hfr,
                Position = new Accord.Point((float)posX, (float)posY),
                OriginalPosition = new Accord.Point((float)(origX ?? posX), (float)(origY ?? posY)),
                BoundingBox = new System.Drawing.Rectangle(boxX, boxY, boxW, boxH),
                OriginalBoundingBox = new System.Drawing.Rectangle(origBoxX ?? boxX, origBoxY ?? boxY, origBoxW ?? boxW, origBoxH ?? boxH),
            };
        }

        private static SensorDetectedStars MakeFrame(
            double focuserPosition,
            IEnumerable<HocusFocusDetectedStar> stars,
            IRenderedImage image,
            Matrix3x2 alignment = null,
            bool hasBeenAligned = false) {
            var result = new HocusFocusStarDetectionResult {
                StarList = stars.Cast<DetectedStar>().ToList(),
            };
            return new SensorDetectedStars(focuserPosition, result, image) {
                AlignmentTransform = alignment,
                HasBeenAligned = hasBeenAligned,
            };
        }

        private static SensorModel.RegisteredStar MakeRegistered(params (HocusFocusDetectedStar star, int imageIndex)[] matches) {
            return MakeFittedRegistered(null, matches);
        }

        private static SensorModel.RegisteredStar MakeFittedRegistered(AlglibHyperbolicFitting fit, params (HocusFocusDetectedStar star, int imageIndex)[] matches) {
            var rs = new SensorModel.RegisteredStar { Fitting = fit };
            foreach (var (star, imageIndex) in matches) {
                rs.MatchedStars.Add(new SensorModel.MatchedStar { Star = star, ImageIndex = imageIndex, FocuserPosition = imageIndex });
            }
            return rs;
        }

        // A genuine, solved symmetric-hyperbola fit whose best focus is approximately x0.
        private AlglibHyperbolicFitting MakeFit(double x0) {
            var points = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: x0, y0: 0.0, a: 2.0, b: 80.0, xStart: x0 - 400, xStep: 20, count: 41, errorY: 1.0);
            var fit = HyperbolicFittingAlglib.Create(alglibAPI, points, useWeights: false);
            fit.HuberIrlsEnabled = false;
            Assert.That(fit.Solve(), Is.True);
            return fit;
        }

        // ---- tests --------------------------------------------------------------------------------------

        [Test]
        public void Build_MapsSamePhysicalStarToSameRegistrationIdAcrossFrames() {
            var a0 = MakeStar(posX: 10, posY: 10);
            var a1 = MakeStar(posX: 11, posY: 10);
            var b0 = MakeStar(posX: 50, posY: 50);

            var frame0 = MakeFrame(100, new[] { a0, b0 }, MakeImage());
            var frame1 = MakeFrame(200, new[] { a1 }, MakeImage());

            var registered = new[] {
                MakeRegistered((a0, 0), (a1, 1)),
                MakeRegistered((b0, 0)),
            };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame0, frame1 }, registered, referenceImageIndex: 0, ransacEnabled: true);

            var f0 = snapshot.Frames.Single(f => f.ImageIndex == 0);
            var f1 = snapshot.Frames.Single(f => f.ImageIndex == 1);
            var a0Star = f0.Stars.Single(s => s.CenterX == 10);
            var b0Star = f0.Stars.Single(s => s.CenterX == 50);
            var a1Star = f1.Stars.Single();

            Assert.Multiple(() => {
                Assert.That(a0Star.RegistrationId, Is.EqualTo(0));
                Assert.That(a1Star.RegistrationId, Is.EqualTo(0));
                Assert.That(b0Star.RegistrationId, Is.EqualTo(1));
            });
        }

        [Test]
        public void Build_UnmatchedStar_HasNullRegistrationIdAndUnmatchedState() {
            var matched = MakeStar(posX: 10);
            var unmatched = MakeStar(posX: 99);
            var frame = MakeFrame(100, new[] { matched, unmatched }, MakeImage());
            var registered = new[] { MakeRegistered((matched, 0)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            var unmatchedStar = snapshot.Frames.Single().Stars.Single(s => s.CenterX == 99);
            Assert.Multiple(() => {
                Assert.That(unmatchedStar.RegistrationId, Is.Null);
                Assert.That(unmatchedStar.RegistrationState, Is.EqualTo(FrameReviewRegistrationState.Unmatched));
                Assert.That(unmatchedStar.FocusOffsetFromMean, Is.Null);
            });
        }

        [Test]
        public void Build_RegistrationState_MapsUnmatchedNoFitAndWithFit() {
            var unmatched = MakeStar(posX: 10);
            var noFit = MakeStar(posX: 20);
            var withFit = MakeStar(posX: 30);
            var frame = MakeFrame(100, new[] { unmatched, noFit, withFit }, MakeImage());
            var registered = new[] {
                MakeRegistered((noFit, 0)),                 // matched, no fit
                MakeFittedRegistered(MakeFit(5000), (withFit, 0)), // matched, with fit
            };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, registered, referenceImageIndex: 0, ransacEnabled: true);
            var stars = snapshot.Frames.Single().Stars;

            Assert.Multiple(() => {
                Assert.That(stars.Single(s => s.CenterX == 10).RegistrationState, Is.EqualTo(FrameReviewRegistrationState.Unmatched));
                Assert.That(stars.Single(s => s.CenterX == 20).RegistrationState, Is.EqualTo(FrameReviewRegistrationState.MatchedNoFit));
                Assert.That(stars.Single(s => s.CenterX == 30).RegistrationState, Is.EqualTo(FrameReviewRegistrationState.MatchedWithFit));
                // No-fit star has no focus offset; with-fit star does.
                Assert.That(stars.Single(s => s.CenterX == 20).FocusOffsetFromMean, Is.Null);
                Assert.That(stars.Single(s => s.CenterX == 30).FocusOffsetFromMean, Is.Not.Null);
            });
        }

        [Test]
        public void Build_FocusOffsetFromMean_IsSignedAndSumsToZero() {
            var starLow = MakeStar(posX: 10);
            var starHigh = MakeStar(posX: 20);
            var frame = MakeFrame(100, new[] { starLow, starHigh }, MakeImage());
            var registered = new[] {
                MakeFittedRegistered(MakeFit(5000), (starLow, 0)),  // lower best-focus
                MakeFittedRegistered(MakeFit(5080), (starHigh, 0)), // higher best-focus
            };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, registered, referenceImageIndex: 0, ransacEnabled: true);
            var stars = snapshot.Frames.Single().Stars;
            var low = stars.Single(s => s.CenterX == 10).FocusOffsetFromMean.Value;
            var high = stars.Single(s => s.CenterX == 20).FocusOffsetFromMean.Value;

            Assert.Multiple(() => {
                Assert.That(low, Is.LessThan(0.0));            // below the field mean
                Assert.That(high, Is.GreaterThan(0.0));         // above the field mean
                Assert.That(low + high, Is.EqualTo(0.0).Within(1e-6)); // two stars → offsets are equal and opposite
            });
        }

        [Test]
        public void Build_FocusOffsetFromMean_SingleFittedStarIsZero() {
            var star = MakeStar(posX: 10);
            var frame = MakeFrame(100, new[] { star }, MakeImage());
            var registered = new[] { MakeFittedRegistered(MakeFit(5000), (star, 0)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            Assert.That(snapshot.Frames.Single().Stars.Single().FocusOffsetFromMean.Value, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void Build_FocusCurves_PopulatedForAllMatchedStars_FitWhenAvailable() {
            var withFit = MakeStar(posX: 30);
            var noFit = MakeStar(posX: 20);
            var frame = MakeFrame(100, new[] { noFit, withFit }, MakeImage());
            var fit = MakeFit(5000);
            var registered = new[] {
                MakeRegistered((noFit, 0)),
                MakeFittedRegistered(fit, (withFit, 0), (withFit, 1)),
            };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            Assert.Multiple(() => {
                // The registered-but-unfitted star now gets a points-only curve (no Fit) so its cross-frame points
                // can be shown on hover.
                Assert.That(snapshot.FocusCurvesByRegistrationId.ContainsKey(0), Is.True);
                var noFitCurve = snapshot.FocusCurvesByRegistrationId[0];
                Assert.That(noFitCurve.Fit, Is.Null);
                Assert.That(noFitCurve.Points.Count, Is.EqualTo(1));
                Assert.That(noFitCurve.OffsetFromMean, Is.Null);

                Assert.That(snapshot.FocusCurvesByRegistrationId.ContainsKey(1), Is.True);
                var curve = snapshot.FocusCurvesByRegistrationId[1];
                Assert.That(curve.Fit, Is.SameAs(fit));
                Assert.That(curve.Points.Count, Is.EqualTo(2)); // one per matched star
                Assert.That(curve.RSquared, Is.EqualTo(fit.RSquared));
                Assert.That(curve.BestFocus, Is.EqualTo(fit.Minimum.X));
            });
        }

        [Test]
        public void Build_FocusCurve_CarriesRejectedPointsForFittedStar_ProjectedToZeroError() {
            var star = MakeStar(posX: 30);
            var frame = MakeFrame(100, new[] { star }, MakeImage());
            var fitted = MakeFittedRegistered(MakeFit(5000), (star, 0), (star, 1));
            // The fit rejected one detection (carries a nonzero weight/error from the fit's input).
            fitted.RejectedPoints = new[] { new ScatterErrorPoint(1.0, 7.5, 0.0, 0.3) };
            var registered = new[] { fitted };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            var curve = snapshot.FocusCurvesByRegistrationId[0];
            Assert.Multiple(() => {
                Assert.That(curve.RejectedPoints.Count, Is.EqualTo(1));
                Assert.That(curve.RejectedPoints[0].X, Is.EqualTo(1.0)); // focuser position carried through
                Assert.That(curve.RejectedPoints[0].Y, Is.EqualTo(7.5)); // HFR carried through
                Assert.That(curve.RejectedPoints[0].ErrorY, Is.EqualTo(0.0)); // projected to zero error like display points
            });
        }

        [Test]
        public void Build_FocusCurve_NoRejectedPoints_IsEmpty() {
            var star = MakeStar(posX: 30);
            var frame = MakeFrame(100, new[] { star }, MakeImage());
            var registered = new[] { MakeFittedRegistered(MakeFit(5000), (star, 0), (star, 1)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            Assert.That(snapshot.FocusCurvesByRegistrationId[0].RejectedPoints, Is.Empty);
        }

        [Test]
        public void Build_FocusCurve_UnfittedStar_HasNoRejectedPoints() {
            var star = MakeStar(posX: 20);
            var frame = MakeFrame(100, new[] { star }, MakeImage());
            // A registered-but-unfitted star: even if rejected points were somehow set, the builder only surfaces them
            // for fitted stars (a fitless star had no fit to reject from).
            var noFit = MakeRegistered((star, 0));
            noFit.RejectedPoints = new[] { new ScatterErrorPoint(0.0, 5.0, 0.0, 0.0) };
            var registered = new[] { noFit };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            var curve = snapshot.FocusCurvesByRegistrationId[0];
            Assert.Multiple(() => {
                Assert.That(curve.Fit, Is.Null);
                Assert.That(curve.RejectedPoints, Is.Empty);
            });
        }

        [Test]
        public void Build_RansacOff_NoRegistrationLineEvenWhenPositionMoved() {
            var star = MakeStar(posX: 30, posY: 30, origX: 10, origY: 10);
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var movedFrame = MakeFrame(200, new[] { star }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, movedFrame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: false);

            Assert.Multiple(() => {
                Assert.That(snapshot.RansacEnabled, Is.False);
                Assert.That(snapshot.Frames.SelectMany(f => f.Stars).Any(s => s.HasRegistrationLine), Is.False);
                Assert.That(snapshot.Frames.All(f => f.TransformText == ""), Is.True);
            });
        }

        [Test]
        public void Build_ReferenceFrame_IsFlaggedWithNoLineAndEmptyTransform() {
            var star = MakeStar(posX: 10, posY: 10);
            var refFrame = MakeFrame(100, new[] { star }, MakeImage());
            var otherStar = MakeStar(posX: 33, posY: 33, origX: 10, origY: 10);
            var otherFrame = MakeFrame(200, new[] { otherStar }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);
            var registered = new[] { MakeRegistered((star, 0), (otherStar, 1)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, otherFrame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            var reference = snapshot.Frames.Single(f => f.ImageIndex == 0);
            Assert.Multiple(() => {
                Assert.That(reference.IsReference, Is.True);
                Assert.That(reference.TransformText, Is.EqualTo(""));
                Assert.That(reference.Stars.Any(s => s.HasRegistrationLine), Is.False);
                Assert.That(snapshot.Frames.Single(f => f.ImageIndex == 1).IsReference, Is.False);
            });
        }

        [Test]
        public void Build_NonReferenceAlignedFrame_HasLineFromRawCenterToAlignedTarget() {
            var star = MakeStar(posX: 33, posY: 44, origX: 10, origY: 12);
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var movedFrame = MakeFrame(200, new[] { star }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);
            var registered = new[] { MakeRegistered((star, 1)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, movedFrame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            var moved = snapshot.Frames.Single(f => f.ImageIndex == 1).Stars.Single();
            Assert.Multiple(() => {
                Assert.That(moved.HasRegistrationLine, Is.True);
                Assert.That(moved.CenterX, Is.EqualTo(10)); // raw detected center
                Assert.That(moved.CenterY, Is.EqualTo(12));
                Assert.That(moved.TargetX, Is.EqualTo(33)); // aligned position = registration target
                Assert.That(moved.TargetY, Is.EqualTo(44));
            });
        }

        [Test]
        public void Build_NonReferenceFrame_StarNotMoved_HasNoLine() {
            var star = MakeStar(posX: 10, posY: 10);
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var frame = MakeFrame(200, new[] { star }, MakeImage(), hasBeenAligned: false);
            var registered = new[] { MakeRegistered((star, 1)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, frame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            Assert.That(snapshot.Frames.Single(f => f.ImageIndex == 1).Stars.Single().HasRegistrationLine, Is.False);
        }

        [Test]
        public void Build_NonReferenceAlignedFrame_TransformTextUsesDegreeSymbol() {
            var transform = new Matrix3x2(1, 0, 0, 1, 5, 7);
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var movedFrame = MakeFrame(200, new[] { MakeStar(posX: 15, origX: 10) }, MakeImage(), alignment: transform, hasBeenAligned: true);

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, movedFrame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);

            var moved = snapshot.Frames.Single(f => f.ImageIndex == 1);
            Assert.Multiple(() => {
                Assert.That(moved.TransformText, Is.EqualTo(FrameReviewTransformFormatter.Format(transform)));
                Assert.That(moved.TransformText, Does.Contain("°"));
                Assert.That(moved.TransformText, Does.Not.Contain("deg"));
            });
        }

        [Test]
        public void Build_DropsFramesWithNullImageOrNullImageImage() {
            var nullImageFrame = MakeFrame(100, new[] { MakeStar() }, null);
            var nullInnerImage = Substitute.For<IRenderedImage>();
            nullInnerImage.Image.Returns((BitmapSource)null);
            var nullInnerFrame = MakeFrame(200, new[] { MakeStar() }, nullInnerImage);
            var goodFrame = MakeFrame(300, new[] { MakeStar() }, MakeImage());

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { nullImageFrame, nullInnerFrame, goodFrame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);

            Assert.Multiple(() => {
                Assert.That(snapshot.Frames.Count, Is.EqualTo(1));
                Assert.That(snapshot.Frames.Single().ImageIndex, Is.EqualTo(2));
            });
        }

        [Test]
        public void Build_OrdersSurvivingFramesByFocuserPositionButKeepsOriginalImageIndex() {
            var f0 = MakeFrame(300, new[] { MakeStar() }, MakeImage());
            var f1 = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var f2 = MakeFrame(200, new[] { MakeStar() }, MakeImage());

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { f0, f1, f2 }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);

            Assert.Multiple(() => {
                Assert.That(snapshot.Frames.Select(f => f.FocuserPosition), Is.EqualTo(new[] { 100.0, 200.0, 300.0 }));
                Assert.That(snapshot.Frames.Select(f => f.ImageIndex), Is.EqualTo(new[] { 1, 2, 0 }));
            });
        }

        [Test]
        public void Build_SnapshotIsImmutableAfterSourceStarMutation() {
            var star = MakeStar(hfr: 2.0, posX: 10, posY: 20, boxX: 8, boxY: 18, boxW: 5, boxH: 7);
            var frame = MakeFrame(100, new[] { star }, MakeImage());

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);
            var captured = snapshot.Frames.Single().Stars.Single();

            star.Position = new Accord.Point(999, 888);
            star.HFR = 99;
            star.BoundingBox = new System.Drawing.Rectangle(1, 2, 3, 4);
            star.OriginalPosition = new Accord.Point(777, 666);
            star.OriginalBoundingBox = new System.Drawing.Rectangle(11, 12, 13, 14);

            Assert.Multiple(() => {
                Assert.That(captured.CenterX, Is.EqualTo(10));
                Assert.That(captured.CenterY, Is.EqualTo(20));
                Assert.That(captured.Hfr, Is.EqualTo(2.0));
                Assert.That(captured.BoxX, Is.EqualTo(8));
                Assert.That(captured.BoxWidth, Is.EqualTo(5));
            });
        }

        [Test]
        public void Build_UsesRawCoordinates_NotAlignedBoxOrCenter() {
            // Position/BoundingBox are the (aligned) values; OriginalPosition/OriginalBoundingBox are the raw values.
            var star = MakeStar(hfr: 3.5,
                posX: 12.0, posY: 24.0, origX: 11.0, origY: 23.0,
                boxX: 50, boxY: 60, boxW: 6, boxH: 8,
                origBoxX: 9, origBoxY: 21, origBoxW: 6, origBoxH: 8);
            var frame = MakeFrame(100, new[] { star }, MakeImage());

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);
            var s = snapshot.Frames.Single().Stars.Single();

            Assert.Multiple(() => {
                Assert.That(s.CenterX, Is.EqualTo(11.0)); // raw center, NOT aligned 12.0
                Assert.That(s.CenterY, Is.EqualTo(23.0));
                Assert.That(s.BoxX, Is.EqualTo(9));        // raw box top-left, NOT aligned 50
                Assert.That(s.BoxY, Is.EqualTo(21));
                Assert.That(s.BoxWidth, Is.EqualTo(6));
                Assert.That(s.BoxHeight, Is.EqualTo(8));
                Assert.That(s.TargetX, Is.EqualTo(12.0));  // aligned position = target
                Assert.That(s.TargetY, Is.EqualTo(24.0));
                Assert.That(s.Hfr, Is.EqualTo(3.5));
            });
        }

        [Test]
        public void Build_DetectedStarCount_EqualsAcceptedStarCount() {
            var frame = MakeFrame(100, new[] { MakeStar(posX: 1), MakeStar(posX: 2), MakeStar(posX: 3) }, MakeImage());

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);

            var f = snapshot.Frames.Single();
            Assert.Multiple(() => {
                Assert.That(f.DetectedStarCount, Is.EqualTo(3));
                Assert.That(f.Stars.Count, Is.EqualTo(3));
            });
        }

        [Test]
        public void Build_CarriesRansacFlagAndReferenceIndex() {
            var frame = MakeFrame(100, new[] { MakeStar() }, MakeImage());

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 5, ransacEnabled: true);

            Assert.Multiple(() => {
                Assert.That(snapshot.RansacEnabled, Is.True);
                Assert.That(snapshot.ReferenceImageIndex, Is.EqualTo(5));
            });
        }

        [Test]
        public void Build_EmptyInput_ProducesEmptySnapshot() {
            var snapshot = FrameReviewSnapshotBuilder.Build(new SensorDetectedStars[0], new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);
            Assert.Multiple(() => {
                Assert.That(snapshot.Frames, Is.Empty);
                Assert.That(snapshot.FocusCurvesByRegistrationId, Is.Empty);
            });
        }

        // F35: the registration line is gated on a tolerance (|Δx|+|Δy| > 1e-6), not exact inequality, so a nonzero
        // but sub-epsilon alignment residual no longer draws a visually meaningless ~zero-length line. The aligned
        // position flows in as targetX (= posX) and the raw position as centerX (= origX) with no intervening
        // transform, so a known delta between those star fields equals |targetX-centerX|+|targetY-centerY| directly.
        // Small-magnitude coordinates are used because a float cannot represent a nonzero delta below ~6.1e-5 at
        // coordinate magnitude ~1000.

        // Delta 3e-7 (representable, > 0, < 1e-6): old "!= 0" code drew this; the tolerance suppresses it.
        [Test]
        public void Build_NonReferenceAlignedFrame_SubEpsilonMovement_HasNoLine() {
            float origin = 0f;
            float aligned = 3e-7f; // representable, nonzero, |Δ| ≈ 3e-7 < 1e-6
            var star = MakeStar(posX: aligned, posY: origin, origX: origin, origY: origin);
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var movedFrame = MakeFrame(200, new[] { star }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);
            var registered = new[] { MakeRegistered((star, 1)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, movedFrame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            Assert.That(snapshot.Frames.Single(f => f.ImageIndex == 1).Stars.Single().HasRegistrationLine, Is.False);
        }

        // Delta 2e-6 (just above the epsilon): the line is drawn.
        [Test]
        public void Build_NonReferenceAlignedFrame_AboveEpsilonMovement_HasLine() {
            float origin = 0f;
            float aligned = 2e-6f; // |Δ| ≈ 2e-6 > 1e-6
            var star = MakeStar(posX: aligned, posY: origin, origX: origin, origY: origin);
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var movedFrame = MakeFrame(200, new[] { star }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);
            var registered = new[] { MakeRegistered((star, 1)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, movedFrame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            Assert.That(snapshot.Frames.Single(f => f.ImageIndex == 1).Stars.Single().HasRegistrationLine, Is.True);
        }

        // F37: when the reference frame's image was dropped, a surviving aligned frame must NOT be rendered as if it
        // were registered against a present reference. The surviving star is genuinely moved and carries a transform,
        // so the OLD code (no referenceSurvives guard) would draw a registration line and show transform text; the
        // fix suppresses both because the reference the alignment is relative to is no longer visible.
        [Test]
        public void Build_DroppedReferenceImage_SuppressesLinesAndTransform() {
            var refStar = MakeStar(posX: 10, posY: 10);
            var droppedRefFrame = MakeFrame(100, new[] { refStar }, null); // reference image not retained -> dropped
            var otherStar = MakeStar(posX: 33, posY: 44, origX: 10, origY: 12); // aligned away from raw -> would draw a line
            var otherFrame = MakeFrame(200, new[] { otherStar }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);
            var registered = new[] { MakeRegistered((refStar, 0), (otherStar, 1)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { droppedRefFrame, otherFrame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            Assert.Multiple(() => {
                Assert.That(snapshot.Frames.Count, Is.EqualTo(1));
                Assert.That(snapshot.Frames.Single().Stars.Any(s => s.HasRegistrationLine), Is.False);
                Assert.That(snapshot.Frames.Single().TransformText, Is.EqualTo(""));
            });
        }
    }
}
