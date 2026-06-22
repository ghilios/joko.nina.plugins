using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection {

    [TestFixture]
    public class FrameReviewSnapshotBuilderTests {

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
            int boxX = 8, int boxY = 18, int boxW = 5, int boxH = 7) {
            return new HocusFocusDetectedStar {
                HFR = hfr,
                Position = new Accord.Point((float)posX, (float)posY),
                OriginalPosition = new Accord.Point((float)(origX ?? posX), (float)(origY ?? posY)),
                BoundingBox = new System.Drawing.Rectangle(boxX, boxY, boxW, boxH),
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
            var rs = new SensorModel.RegisteredStar();
            foreach (var (star, imageIndex) in matches) {
                rs.MatchedStars.Add(new SensorModel.MatchedStar { Star = star, ImageIndex = imageIndex, FocuserPosition = imageIndex });
            }
            return rs;
        }

        // ---- tests --------------------------------------------------------------------------------------

        [Test]
        public void Build_MapsSamePhysicalStarToSameRegistrationIdAcrossFrames() {
            var a0 = MakeStar(posX: 10, posY: 10); // physical star A in frame 0
            var a1 = MakeStar(posX: 11, posY: 10); // physical star A in frame 1
            var b0 = MakeStar(posX: 50, posY: 50); // physical star B in frame 0

            var frame0 = MakeFrame(100, new[] { a0, b0 }, MakeImage());
            var frame1 = MakeFrame(200, new[] { a1 }, MakeImage());

            // registeredStars[0] = physical star A (matched in frames 0 and 1); [1] = physical star B (frame 0 only)
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
                Assert.That(a1Star.RegistrationId, Is.EqualTo(0)); // same physical star → same id across frames
                Assert.That(b0Star.RegistrationId, Is.EqualTo(1));
            });
        }

        [Test]
        public void Build_UnmatchedStar_HasNullRegistrationId() {
            var matched = MakeStar(posX: 10);
            var unmatched = MakeStar(posX: 99);
            var frame = MakeFrame(100, new[] { matched, unmatched }, MakeImage());
            var registered = new[] { MakeRegistered((matched, 0)) };

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, registered, referenceImageIndex: 0, ransacEnabled: true);

            var unmatchedStar = snapshot.Frames.Single().Stars.Single(s => s.CenterX == 99);
            Assert.That(unmatchedStar.RegistrationId, Is.Null);
        }

        [Test]
        public void Build_RansacOff_NoArrowsEvenWhenPositionMoved() {
            // Position differs from OriginalPosition, alignment set — but RANSAC off → no arrows, empty transform.
            var star = MakeStar(posX: 30, posY: 30, origX: 10, origY: 10);
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var movedFrame = MakeFrame(200, new[] { star }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, movedFrame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: false);

            Assert.Multiple(() => {
                Assert.That(snapshot.RansacEnabled, Is.False);
                Assert.That(snapshot.Frames.SelectMany(f => f.Stars).Any(s => s.HasArrow), Is.False);
                Assert.That(snapshot.Frames.All(f => f.TransformText == ""), Is.True);
            });
        }

        [Test]
        public void Build_ReferenceFrame_IsFlaggedWithNoArrowsAndEmptyTransform() {
            var star = MakeStar(posX: 10, posY: 10); // reference frame: Position == OriginalPosition
            var refFrame = MakeFrame(100, new[] { star }, MakeImage());
            var otherFrame = MakeFrame(200, new[] { MakeStar(posX: 33, posY: 33, origX: 10, origY: 10) }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, otherFrame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);

            var reference = snapshot.Frames.Single(f => f.ImageIndex == 0);
            Assert.Multiple(() => {
                Assert.That(reference.IsReference, Is.True);
                Assert.That(reference.TransformText, Is.EqualTo(""));
                Assert.That(reference.Stars.Any(s => s.HasArrow), Is.False);
                Assert.That(snapshot.Frames.Single(f => f.ImageIndex == 1).IsReference, Is.False);
            });
        }

        [Test]
        public void Build_NonReferenceAlignedFrame_HasArrowFromOriginalToPosition() {
            var star = MakeStar(posX: 33, posY: 44, origX: 10, origY: 12);
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var movedFrame = MakeFrame(200, new[] { star }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, movedFrame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);

            var moved = snapshot.Frames.Single(f => f.ImageIndex == 1).Stars.Single();
            Assert.Multiple(() => {
                Assert.That(moved.HasArrow, Is.True);
                Assert.That(moved.OriginalX, Is.EqualTo(10));
                Assert.That(moved.OriginalY, Is.EqualTo(12));
                Assert.That(moved.CenterX, Is.EqualTo(33));
                Assert.That(moved.CenterY, Is.EqualTo(44));
            });
        }

        [Test]
        public void Build_NonReferenceFrame_StarNotMoved_HasNoArrow() {
            var star = MakeStar(posX: 10, posY: 10); // Position == OriginalPosition (failed/identity alignment)
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var frame = MakeFrame(200, new[] { star }, MakeImage(), hasBeenAligned: false);

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, frame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);

            Assert.That(snapshot.Frames.Single(f => f.ImageIndex == 1).Stars.Single().HasArrow, Is.False);
        }

        [Test]
        public void Build_NonReferenceAlignedFrame_TransformTextFromAlignment() {
            var transform = new Matrix3x2(1, 0, 0, 1, 5, 7);
            var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
            var movedFrame = MakeFrame(200, new[] { MakeStar(posX: 15, origX: 10) }, MakeImage(), alignment: transform, hasBeenAligned: true);

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, movedFrame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);

            var moved = snapshot.Frames.Single(f => f.ImageIndex == 1);
            Assert.Multiple(() => {
                Assert.That(moved.TransformText, Is.EqualTo(transform.ToFullString()));
                Assert.That(moved.TransformText, Is.Not.Empty);
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
                Assert.That(snapshot.Frames.Single().ImageIndex, Is.EqualTo(2)); // original index preserved
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

            // Mutate the source star the way the registration/alignment pass would, after the snapshot is built.
            star.Position = new Accord.Point(999, 888);
            star.HFR = 99;
            star.BoundingBox = new System.Drawing.Rectangle(1, 2, 3, 4);

            Assert.Multiple(() => {
                Assert.That(captured.CenterX, Is.EqualTo(10));
                Assert.That(captured.CenterY, Is.EqualTo(20));
                Assert.That(captured.Hfr, Is.EqualTo(2.0));
                Assert.That(captured.BoxX, Is.EqualTo(8));
                Assert.That(captured.BoxWidth, Is.EqualTo(5));
            });
        }

        [Test]
        public void Build_CopiesStarPrimitivesAndBoxTopLeft() {
            var star = MakeStar(hfr: 3.5, posX: 12.0, posY: 24.0, origX: 11.0, origY: 23.0, boxX: 9, boxY: 21, boxW: 6, boxH: 8);
            var frame = MakeFrame(100, new[] { star }, MakeImage());

            var snapshot = FrameReviewSnapshotBuilder.Build(new[] { frame }, new SensorModel.RegisteredStar[0], referenceImageIndex: 0, ransacEnabled: true);
            var s = snapshot.Frames.Single().Stars.Single();

            Assert.Multiple(() => {
                Assert.That(s.CenterX, Is.EqualTo(12.0));
                Assert.That(s.CenterY, Is.EqualTo(24.0));
                Assert.That(s.OriginalX, Is.EqualTo(11.0));
                Assert.That(s.OriginalY, Is.EqualTo(23.0));
                Assert.That(s.Hfr, Is.EqualTo(3.5));
                Assert.That(s.BoxX, Is.EqualTo(9));
                Assert.That(s.BoxY, Is.EqualTo(21));
                Assert.That(s.BoxWidth, Is.EqualTo(6));
                Assert.That(s.BoxHeight, Is.EqualTo(8));
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
            Assert.That(snapshot.Frames, Is.Empty);
        }
    }
}
