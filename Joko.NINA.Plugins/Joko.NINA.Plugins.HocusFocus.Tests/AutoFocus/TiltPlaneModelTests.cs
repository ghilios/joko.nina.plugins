using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;
using System;
using System.Drawing;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class TiltPlaneModelTests {

        private static TiltPlaneModel BuildEqualCorners(double focuser, double micronsPerStep = 5.0) {
            return TiltPlaneModel.Create(
                imageSize: new Size(2000, 1000),
                fRatio: 5.0,
                focuserStepSizeMicrons: micronsPerStep,
                centerFocuser: focuser,
                topLeftFocuser: focuser,
                topRightFocuser: focuser,
                bottomLeftFocuser: focuser,
                bottomRightFocuser: focuser);
        }

        [Test]
        public void Create_AllCornersEqual_PlaneIsFlatWithZeroAdjustment() {
            const double focuser = 12345.0;
            var model = BuildEqualCorners(focuser);

            Assert.Multiple(() => {
                Assert.That(model.A, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(model.B, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(model.C, Is.EqualTo(focuser).Within(1e-9));
                Assert.That(model.MeanFocuserPosition, Is.EqualTo(focuser).Within(1e-9));
                Assert.That(model.TopLeft.AdjustmentRequiredSteps, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(model.TopRight.AdjustmentRequiredSteps, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(model.BottomLeft.AdjustmentRequiredSteps, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(model.BottomRight.AdjustmentRequiredSteps, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(model.TopLeft.AdjustmentRequiredMicrons, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(model.TopRight.AdjustmentRequiredMicrons, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(model.BottomLeft.AdjustmentRequiredMicrons, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(model.BottomRight.AdjustmentRequiredMicrons, Is.EqualTo(0.0).Within(1e-9));
            });
        }

        [Test]
        public void Center_AdjustmentValues_AreNaN() {
            var model = BuildEqualCorners(1000.0);
            Assert.Multiple(() => {
                Assert.That(double.IsNaN(model.Center.AdjustmentRequiredSteps), Is.True);
                Assert.That(double.IsNaN(model.Center.AdjustmentRequiredMicrons), Is.True);
            });
        }

        [Test]
        public void Create_TiltedAlongX_RecoversAFromCornerDelta() {
            // Right side higher than left by 100 steps (uniform top→bottom)
            var model = TiltPlaneModel.Create(
                imageSize: new Size(2000, 1000), fRatio: 5.0, focuserStepSizeMicrons: 5.0,
                centerFocuser: 1000,
                topLeftFocuser: 950, topRightFocuser: 1050,
                bottomLeftFocuser: 950, bottomRightFocuser: 1050);

            Assert.Multiple(() => {
                // X spans from -0.5 to 0.5 = 1.0; deltaY across X = 100 → A = 100
                Assert.That(model.A, Is.EqualTo(100.0).Within(1e-6));
                Assert.That(model.B, Is.EqualTo(0.0).Within(1e-6));
                Assert.That(model.C, Is.EqualTo(1000.0).Within(1e-6));
            });
        }

        [Test]
        public void Create_TiltedAlongY_RecoversBFromCornerDelta() {
            var model = TiltPlaneModel.Create(
                imageSize: new Size(2000, 1000), fRatio: 5.0, focuserStepSizeMicrons: 5.0,
                centerFocuser: 1000,
                topLeftFocuser: 950, topRightFocuser: 950,
                bottomLeftFocuser: 1050, bottomRightFocuser: 1050);

            Assert.Multiple(() => {
                Assert.That(model.A, Is.EqualTo(0.0).Within(1e-6));
                Assert.That(model.B, Is.EqualTo(100.0).Within(1e-6));
                Assert.That(model.C, Is.EqualTo(1000.0).Within(1e-6));
            });
        }

        [Test]
        public void Create_AdjustmentMicrons_EqualStepsTimesStepSize() {
            const double micronsPerStep = 7.5;
            var model = TiltPlaneModel.Create(
                imageSize: new Size(2000, 1000), fRatio: 5.0, focuserStepSizeMicrons: micronsPerStep,
                centerFocuser: 1000,
                topLeftFocuser: 950, topRightFocuser: 1050,
                bottomLeftFocuser: 950, bottomRightFocuser: 1050);

            Assert.Multiple(() => {
                Assert.That(model.TopRight.AdjustmentRequiredMicrons,
                    Is.EqualTo(model.TopRight.AdjustmentRequiredSteps * micronsPerStep).Within(1e-9));
                Assert.That(model.TopLeft.AdjustmentRequiredMicrons,
                    Is.EqualTo(model.TopLeft.AdjustmentRequiredSteps * micronsPerStep).Within(1e-9));
            });
        }

        [Test]
        public void EstimateFocusPosition_AtImageCenter_ReturnsC() {
            // Coherent corners: TL/BR symmetric, TR/BL symmetric → OLS plane goes exactly through the center
            const double tl = 950, tr = 1050, bl = 950, br = 1050;
            var model = TiltPlaneModel.Create(
                imageSize: new Size(2000, 1000), fRatio: 5.0, focuserStepSizeMicrons: 5.0,
                centerFocuser: 1000,
                topLeftFocuser: tl, topRightFocuser: tr,
                bottomLeftFocuser: bl, bottomRightFocuser: br);

            // Width=2000 → image center pixel x=1000 → modelX = 1000/2000 - 0.5 = 0
            // Height=1000 → image center pixel y=500 → modelY = 500/1000 - 0.5 = 0
            // So the plane evaluates to C = 1000 (the OLS intercept = mean of corners)
            Assert.That(model.EstimateFocusPosition(1000, 500), Is.EqualTo(1000.0).Within(1e-6));
        }

        [Test]
        public void EstimateFocusPosition_AtTopLeftPixel_MatchesTopLeftCornerInput() {
            // Pixel (0, 0) maps to model coords (-0.5, -0.5), which is exactly the OLS top-left input,
            // so for coherent corner data it should match the input value exactly.
            const double tl = 950, tr = 1050, bl = 950, br = 1050;
            var model = TiltPlaneModel.Create(
                imageSize: new Size(2000, 1000), fRatio: 5.0, focuserStepSizeMicrons: 5.0,
                centerFocuser: 1000,
                topLeftFocuser: tl, topRightFocuser: tr,
                bottomLeftFocuser: bl, bottomRightFocuser: br);

            Assert.That(model.EstimateFocusPosition(0, 0), Is.EqualTo(tl).Within(1e-6));
        }

        [Test]
        public void GetModelX_AtImageCenter_ReturnsApproximatelyZero() {
            var model = BuildEqualCorners(1000.0);
            // ImageSize.Width = 2000; Center pixel x=1000 → 1000/2000 - 0.5 = 0
            Assert.That(model.GetModelX(1000), Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void GetModelX_AtLeftEdge_IsMinusOneHalf() {
            var model = BuildEqualCorners(1000.0);
            Assert.That(model.GetModelX(0), Is.EqualTo(-0.5).Within(1e-9));
        }

        [Test]
        public void GetModelX_OutOfRange_Throws() {
            var model = BuildEqualCorners(1000.0);
            Assert.Multiple(() => {
                Assert.Throws<ArgumentException>(() => model.GetModelX(-1));
                Assert.Throws<ArgumentException>(() => model.GetModelX(2000));
            });
        }

        [Test]
        public void GetModelY_OutOfRange_Throws() {
            var model = BuildEqualCorners(1000.0);
            Assert.Multiple(() => {
                Assert.Throws<ArgumentException>(() => model.GetModelY(-1));
                Assert.Throws<ArgumentException>(() => model.GetModelY(1000));
            });
        }

        [Test]
        public void Constructor_NaNFRatio_DefaultsToFive() {
            var model = TiltPlaneModel.Create(
                imageSize: new Size(2000, 1000), fRatio: double.NaN, focuserStepSizeMicrons: 5.0,
                centerFocuser: 1000, topLeftFocuser: 1000, topRightFocuser: 1000,
                bottomLeftFocuser: 1000, bottomRightFocuser: 1000);
            Assert.That(model.FRatio, Is.EqualTo(5.0));
        }

        [Test]
        public void Constructor_ZeroWidth_Throws() {
            Assert.Throws<ArgumentException>(() => new TiltPlaneModel(
                imageSize: new Size(0, 1000), fRatio: 5.0,
                a: 0, b: 0, c: 1000, mean: 1000, focuserStepSizeMicrons: 5.0,
                centerPosition: 1000, topLeftPosition: 1000, topRightPosition: 1000,
                bottomLeftPosition: 1000, bottomRightPosition: 1000));
        }

        [Test]
        public void Constructor_ZeroHeight_Throws() {
            Assert.Throws<ArgumentException>(() => new TiltPlaneModel(
                imageSize: new Size(2000, 0), fRatio: 5.0,
                a: 0, b: 0, c: 1000, mean: 1000, focuserStepSizeMicrons: 5.0,
                centerPosition: 1000, topLeftPosition: 1000, topRightPosition: 1000,
                bottomLeftPosition: 1000, bottomRightPosition: 1000));
        }

        // Spec-first divergence flag: per the comprehensive-unit-tests plan, the
        // ctor at TiltModel.cs:40 reads `imageSize.Width == 0 || imageSize.Height <= 0`.
        // A negative width is asymmetric — the spec says positive dimensions, so the
        // negative-width case should also throw.
        [Test]
        public void Constructor_NegativeWidth_Throws_SPEC() {
            Assert.Throws<ArgumentException>(() => new TiltPlaneModel(
                imageSize: new Size(-1, 1000), fRatio: 5.0,
                a: 0, b: 0, c: 1000, mean: 1000, focuserStepSizeMicrons: 5.0,
                centerPosition: 1000, topLeftPosition: 1000, topRightPosition: 1000,
                bottomLeftPosition: 1000, bottomRightPosition: 1000));
        }

        [Test]
        public void Constructor_NegativeHeight_Throws() {
            Assert.Throws<ArgumentException>(() => new TiltPlaneModel(
                imageSize: new Size(2000, -1), fRatio: 5.0,
                a: 0, b: 0, c: 1000, mean: 1000, focuserStepSizeMicrons: 5.0,
                centerPosition: 1000, topLeftPosition: 1000, topRightPosition: 1000,
                bottomLeftPosition: 1000, bottomRightPosition: 1000));
        }

        [Test]
        public void Create_FromAutoFocusResult_UsesActualCornerRegionCenters() {
            // Plane z = C + A*xn + B*yn over normalized [-0.5, 0.5]; corner regions centered at ±1/3.
            const double A = 300.0, B = -120.0, C = 11200.0;
            double At(double xn, double yn) => C + A * xn + B * yn;

            var imageSize = new System.Drawing.Size(9576, 6388);
            var result = new AutoFocusResult() {
                Succeeded = true,
                ImageSize = imageSize,
                RegionResults = new[] {
                    RegionResult(0, new RatioRect(0.0, 0.0, 1.0, 1.0), At(0, 0)),
                    RegionResult(1, new RatioRect(1/3d, 1/3d, 1/3d, 1/3d), At(0, 0)),
                    RegionResult(2, new RatioRect(0.0, 0.0, 1/3d, 1/3d), At(-1/3d, -1/3d)),   // TL
                    RegionResult(3, new RatioRect(2/3d, 0.0, 1/3d, 1/3d), At(+1/3d, -1/3d)),  // TR
                    RegionResult(4, new RatioRect(0.0, 2/3d, 1/3d, 1/3d), At(-1/3d, +1/3d)),  // BL
                    RegionResult(5, new RatioRect(2/3d, 2/3d, 1/3d, 1/3d), At(+1/3d, +1/3d)), // BR
                }
            };

            var model = TiltPlaneModel.Create(result, fRatio: 6.3, focuserStepSizeMicrons: 0.269);

            Assert.Multiple(() => {
                Assert.That(model.A, Is.EqualTo(A).Within(1e-9));  // old code returns 200.0 (A * 2/3)
                Assert.That(model.B, Is.EqualTo(B).Within(1e-9));
                Assert.That(model.C, Is.EqualTo(C).Within(1e-9));
            });
        }

        private static AutoFocusRegionResult RegionResult(int index, RatioRect boundary, double focusPosition) {
            return new AutoFocusRegionResult() {
                RegionIndex = index,
                Region = new StarDetectionRegion(boundary),
                EstimatedFinalFocuserPosition = focusPosition,
                EstimatedFinalHFR = 2.0,
                Fittings = new AutoFocusFitting()
            };
        }
    }
}
