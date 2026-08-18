#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    [TestFixture]
    public class PsfKernelGeneratorTests {

        // Design sanity vector: D=100 mm, f=800 mm (N=8), ε=0.3, λ=550 nm, p=3.76 µm, seeing=2.5″.
        private static DefocusModel BuildSanityVector() {
            return new DefocusModel(
                apertureMillimeters: 100.0,
                focalLengthMillimeters: 800.0,
                centralObstructionFraction: 0.3,
                pixelSizeMicrons: 3.76,
                seeingArcsec: 2.5,
                wavelengthNm: 550.0,
                focuserStepSizeMicrons: 0.49,
                optimalFocuserPosition: 0);
        }

        [Test]
        public void AtFocus_KernelIsGaussian_MeasuredHfrEqualsHfrMin() {
            var model = BuildSanityVector();
            var kernel = PsfKernelGenerator.Generate(model, 0.0);
            Assert.Multiple(() => {
                Assert.That(kernel.OuterRadiusPixels, Is.EqualTo(0.0), "r_out is 0 at focus");
                Assert.That(kernel.MeasuredHfrPixels, Is.EqualTo(model.HfrMinPixels).Within(0.01 * model.HfrMinPixels),
                    "measured HFR at focus equals HFR_min");
                // Rice HFR at focus is exactly σ·√(π/2); HfrMinPixels uses the model's rounded 1.2533 constant,
                // so they agree only to that constant's ~1e-5 precision (our value is the more precise one).
                Assert.That(kernel.AnalyticHfrPixels, Is.EqualTo(model.HfrMinPixels).Within(1e-4 * model.HfrMinPixels),
                    "Rice HFR at focus equals HFR_min (to the model's constant precision)");
            });
        }

        [TestCase(100.0)]
        [TestCase(300.0)]
        [TestCase(600.0)]
        [TestCase(1200.0)]
        public void MeasuredHfr_MatchesDefocusModel(double defocusMicrons) {
            var model = BuildSanityVector();
            var kernel = PsfKernelGenerator.Generate(model, defocusMicrons);
            // DefocusModel.HfrAtDefocusMicrons is the quadrature hyperbola √(HFR_min² + (κΔ)²); the kernel HFR is
            // the exact annulus⊛Gaussian convolution (validated to <1% against the Rice form below). The two agree
            // to ~1% away from focus and worst-case ~2.4% in the r_out≈σ transition — within the design's ~3%
            // envelope for the hyperbola fit.
            var expected = model.HfrAtDefocusMicrons(defocusMicrons);
            Assert.That(kernel.MeasuredHfrPixels, Is.EqualTo(expected).Within(0.03 * expected),
                $"kernel HFR tracks DefocusModel HFR at Δ={defocusMicrons}µm");
        }

        [TestCase(0.0)]
        [TestCase(50.0)]
        [TestCase(150.0)]
        [TestCase(400.0)]
        [TestCase(900.0)]
        [TestCase(1800.0)]
        public void MeasuredHfr_MatchesRiceClosedForm(double defocusMicrons) {
            var model = BuildSanityVector();
            var kernel = PsfKernelGenerator.Generate(model, defocusMicrons);
            Assert.That(kernel.MeasuredHfrPixels, Is.EqualTo(kernel.AnalyticHfrPixels).Within(0.01 * kernel.AnalyticHfrPixels),
                $"measured HFR agrees with Rice closed form to <1% at Δ={defocusMicrons}µm");
        }

        [Test]
        public void LargeDefocus_DonutHoleRatioApproachesObstruction() {
            var model = BuildSanityVector();
            var eps = model.CentralObstructionFraction;
            var kernel = PsfKernelGenerator.Generate(model, 2000.0);

            // Measure the bright-annulus inner/outer half-max radii from the radial profile.
            var rMax = kernel.OuterRadiusPixels * 1.4;
            var step = 0.02;
            var peak = 0.0;
            for (var r = 0.0; r <= rMax; r += step) peak = Math.Max(peak, kernel.RadialIntensity(r));
            var half = 0.5 * peak;
            double inner = -1.0, outer = -1.0;
            for (var r = 0.0; r <= rMax; r += step) {
                if (kernel.RadialIntensity(r) >= half) {
                    if (inner < 0.0) inner = r;
                    outer = r;
                }
            }
            Assert.Multiple(() => {
                Assert.That(inner, Is.GreaterThan(0.0), "there is a central hole");
                Assert.That(inner / outer, Is.EqualTo(eps).Within(0.04), "inner/outer half-max ratio → ε");
            });
        }

        [Test]
        public void AllPhaseKernels_AreNormalizedToUnitSum() {
            var model = BuildSanityVector();
            var kernel = PsfKernelGenerator.Generate(model, 250.0);
            for (var b = 0; b < kernel.PhasesPerAxis; ++b) {
                for (var a = 0; a < kernel.PhasesPerAxis; ++a) {
                    var phase = kernel.GetPhaseKernel(a, b);
                    double sum = 0.0;
                    foreach (var v in phase) sum += v;
                    Assert.That(sum, Is.EqualTo(1.0).Within(1e-5), $"phase ({a},{b}) sums to 1");
                }
            }
        }

        [Test]
        public void FftMethod_IsNotImplemented() {
            var model = BuildSanityVector();
            Assert.Throws<NotSupportedException>(() => PsfKernelGenerator.Generate(model, 100.0, PsfKernelMethod.Fft));
        }

        // -----------------------------------------------------------------------------------------------
        // Astigmatic (elliptical) kernels.
        // -----------------------------------------------------------------------------------------------

        [TestCase(0.0, 0.0)]
        [TestCase(0.0, 0.7)]
        [TestCase(250.0, 0.0)]
        [TestCase(250.0, 0.7)]
        [TestCase(900.0, 1.9)]
        [TestCase(-600.0, -0.4)]
        public void Astigmatic_EqualAxes_ReturnsTheAnalyticKernelUnchanged(double defocusMicrons, double thetaRadians) {
            // The hard requirement: when the two semi-axes coincide -- astigmatism off, or a star on the optical
            // axis -- the astigmatic entry point must hand back bit-for-bit the kernel the circular path builds,
            // whatever the (then meaningless) position angle. Nothing may regress on that path.
            var model = BuildSanityVector();
            var circular = PsfKernelGenerator.Generate(model, defocusMicrons);
            var astigmatic = PsfKernelGenerator.GenerateAstigmatic(model, defocusMicrons, defocusMicrons, thetaRadians);

            Assert.Multiple(() => {
                Assert.That(astigmatic.Radius, Is.EqualTo(circular.Radius));
                Assert.That(astigmatic.MeasuredHfrPixels, Is.EqualTo(circular.MeasuredHfrPixels));
                Assert.That(astigmatic.AnalyticHfrPixels, Is.EqualTo(circular.AnalyticHfrPixels));
                Assert.That(astigmatic.OuterRadiusPixels, Is.EqualTo(circular.OuterRadiusPixels));
                Assert.That(astigmatic.IsAstigmatic, Is.False);
                Assert.That(astigmatic.PredictedEccentricity, Is.EqualTo(0.0));
            });

            for (var b = 0; b < circular.PhasesPerAxis; ++b) {
                for (var a = 0; a < circular.PhasesPerAxis; ++a) {
                    Assert.That(astigmatic.GetPhaseKernel(a, b), Is.EqualTo(circular.GetPhaseKernel(a, b)).AsCollection,
                        $"phase ({a},{b}) is element-wise identical");
                }
            }
        }

        [TestCase(50.0)]
        [TestCase(250.0)]
        [TestCase(900.0)]
        public void EllipticalRasterizer_ForcedWithEqualAxes_MatchesAnalyticPath(double defocusMicrons) {
            // The accuracy argument for the mask+blur rasterizer, made empirically: bypass the short-circuit and
            // rasterize a circle the elliptical way, then compare against the exact annulus⊛Gaussian radial
            // integral. Two independent algorithms, same answer.
            var model = BuildSanityVector();
            var sigma = model.SigmaMinPixels;
            var eps = model.CentralObstructionFraction;
            var a = model.OuterAnnulusRadiusPixels(defocusMicrons);

            var exact = PsfKernelGenerator.Generate(model, defocusMicrons);
            var viaMaskBlur = PsfKernelGenerator.GenerateElliptical(sigma, eps, a, a, 0.3);

            Assert.That(viaMaskBlur.Radius, Is.EqualTo(exact.Radius), "same support");
            Assert.That(viaMaskBlur.MeasuredHfrPixels, Is.EqualTo(exact.MeasuredHfrPixels).Within(0.005 * exact.MeasuredHfrPixels),
                "HFR agrees to 0.5%");

            var exactPhase = exact.GetPhaseKernel(0, 0);
            var maskPhase = viaMaskBlur.GetPhaseKernel(0, 0);
            var peak = exact.MaxPeak;
            var maxDeviation = 0.0;
            for (var i = 0; i < exactPhase.Length; ++i) {
                maxDeviation = Math.Max(maxDeviation, Math.Abs(exactPhase[i] - maskPhase[i]));
            }
            // 0.2% of peak. The intrinsic accuracy of mask+blur against the exact radial integral at S = 4
            // oversampling is ~0.15% of peak; an offline prototype of the same two algorithms measured 0.13%
            // across r_out = 2…40 px, so this bound is set from the method's real resolution, not from what
            // happened to pass.
            Assert.That(maxDeviation, Is.LessThan(2e-3 * peak),
                $"max sample deviation {maxDeviation:E3} against peak {peak:E3}");
        }

        [TestCase(100.0, 300.0)]
        [TestCase(0.0, 400.0)]
        [TestCase(-200.0, 200.0)]
        [TestCase(600.0, 900.0)]
        public void Astigmatic_MeasuredHfr_MatchesEllipticalRiceClosedForm(double tangentialDefocus, double sagittalDefocus) {
            var model = BuildSanityVector();
            var kernel = PsfKernelGenerator.GenerateAstigmatic(model, tangentialDefocus, sagittalDefocus, 0.6);
            Assert.That(kernel.MeasuredHfrPixels, Is.EqualTo(kernel.AnalyticHfrPixels).Within(0.01 * kernel.AnalyticHfrPixels),
                "rasterized HFR agrees with the elliptical Rice closed form to <1%");
        }

        [TestCase(0.0)]
        [TestCase(150.0)]
        [TestCase(600.0)]
        [TestCase(1500.0)]
        public void EllipticalRiceHfr_ReducesToRiceHfr_WhenAxesEqual(double defocusMicrons) {
            // The elliptical closed form is a 2-D integral that must collapse onto the 1-D one at equal axes.
            // If it does not, the analytic HFR stops being an independent check and starts being a second bug.
            var model = BuildSanityVector();
            var a = model.OuterAnnulusRadiusPixels(defocusMicrons);
            var circularRice = PsfKernelGenerator.Generate(model, defocusMicrons).AnalyticHfrPixels;
            var ellipticalRice = PsfKernelGenerator.EllipticalRiceHfr(
                model.SigmaMinPixels, model.CentralObstructionFraction, a, a);
            Assert.That(ellipticalRice, Is.EqualTo(circularRice).Within(1e-6 * Math.Max(circularRice, 1e-9)));
        }

        [TestCase(2.0, 12.0)]
        [TestCase(5.0, 15.0)]
        [TestCase(8.0, 12.0)]
        [TestCase(20.0, 6.0)]
        public void Astigmatic_SecondMoments_MatchPredictedEccentricity(double radialSemiAxis, double tangentialSemiAxis) {
            const double sigma = 1.44;
            const double eps = 0.3;
            var kernel = PsfKernelGenerator.GenerateElliptical(sigma, eps, radialSemiAxis, tangentialSemiAxis, 0.0);

            var (varX, varY, _) = SecondMoments(kernel);
            // With the position angle at 0 the radial axis is +x, so the moments map straight onto the closed form.
            var predictedVarX = sigma * sigma + radialSemiAxis * radialSemiAxis * (1.0 + eps * eps) / 4.0;
            var predictedVarY = sigma * sigma + tangentialSemiAxis * tangentialSemiAxis * (1.0 + eps * eps) / 4.0;

            Assert.Multiple(() => {
                Assert.That(varX, Is.EqualTo(predictedVarX).Within(0.02 * predictedVarX), "radial-axis variance");
                Assert.That(varY, Is.EqualTo(predictedVarY).Within(0.02 * predictedVarY), "tangential-axis variance");
                Assert.That(Eccentricity(varX, varY, 0.0), Is.EqualTo(kernel.PredictedEccentricity).Within(0.02),
                    "measured eccentricity matches the kernel's own closed-form prediction");
            });
        }

        [Test]
        public void Astigmatic_LineFocus_HasFiniteMinorWidth() {
            // The test the rejected "warp the circular profile" approach fails. At the astigmatic line focus one
            // semi-axis is zero, and a coordinate warp would scale σ along with it and predict an infinitely
            // thin line -- eccentricity 1. The truth is a line of seeing width, so the minor axis must come out
            // at σ and the eccentricity must stay below 1.
            const double sigma = 1.44;
            const double eps = 0.3;
            var kernel = PsfKernelGenerator.GenerateElliptical(sigma, eps, 0.0, 20.0, 0.0);

            var (varX, varY, _) = SecondMoments(kernel);
            Assert.Multiple(() => {
                Assert.That(Math.Sqrt(varX), Is.EqualTo(sigma).Within(0.05 * sigma), "the minor axis keeps its σ width");
                Assert.That(varY, Is.GreaterThan(10.0 * varX), "the major axis is far wider");
                // The warp approach would give exactly 1 here. The true value is 0.9906 for these numbers, so
                // the discriminator is that it stays strictly below 1 while the minor width stays at σ.
                Assert.That(kernel.PredictedEccentricity, Is.LessThan(0.995).And.GreaterThan(0.95));
                Assert.That(Eccentricity(varX, varY, 0.0), Is.LessThan(0.995));
            });
        }

        [TestCase(0.0)]
        [TestCase(0.4)]
        [TestCase(1.1)]
        public void Astigmatic_OrientationFollowsThePositionAngle(double theta) {
            // The major axis of the rendered kernel must actually point where the position angle says. This is
            // the only test that would catch a transposed rotation matrix, which is otherwise invisible in
            // every rotation-invariant statistic.
            var kernel = PsfKernelGenerator.GenerateElliptical(1.44, 0.3, 4.0, 16.0, theta);
            var (varX, varY, covXY) = SecondMoments(kernel);
            var measuredMajorAngle = 0.5 * Math.Atan2(2.0 * covXY, varX - varY);

            // The tangential axis is the major one here (16 > 4), and it sits at theta + 90°. Orientation is
            // mod π, so compare through the doubled angle.
            var expected = theta + Math.PI / 2.0;
            Assert.That(Math.Cos(2.0 * (measuredMajorAngle - expected)), Is.EqualTo(1.0).Within(0.02),
                $"major axis at {measuredMajorAngle:F3} rad, expected {expected:F3} (mod π)");
        }

        [Test]
        public void Astigmatic_RotationByPi_IsIdentical() {
            var a = PsfKernelGenerator.GenerateElliptical(1.44, 0.3, 4.0, 12.0, 0.35);
            var b = PsfKernelGenerator.GenerateElliptical(1.44, 0.3, 4.0, 12.0, 0.35 + Math.PI);
            Assert.That(b.GetPhaseKernel(0, 0), Is.EqualTo(a.GetPhaseKernel(0, 0)).Within(1e-7).AsCollection,
                "an ellipse is symmetric under a half turn");
        }

        [Test]
        public void Astigmatic_SwappingAxesAndRotating90Degrees_GivesTheSameKernel() {
            // (a_rad, a_tan) at θ and (a_tan, a_rad) at θ+90° describe the same ellipse. This is the geometry
            // behind the 90°-rotation property that keeps HFR(Δ) even about best focus.
            var a = PsfKernelGenerator.GenerateElliptical(1.44, 0.3, 4.0, 12.0, 0.0);
            var b = PsfKernelGenerator.GenerateElliptical(1.44, 0.3, 12.0, 4.0, Math.PI / 2.0);
            Assert.Multiple(() => {
                Assert.That(b.MeasuredHfrPixels, Is.EqualTo(a.MeasuredHfrPixels).Within(1e-9));
                Assert.That(b.GetPhaseKernel(0, 0), Is.EqualTo(a.GetPhaseKernel(0, 0)).Within(1e-7).AsCollection);
            });
        }

        [Test]
        public void Astigmatic_AllPhaseKernels_AreNormalizedToUnitSum() {
            var kernel = PsfKernelGenerator.GenerateElliptical(1.44, 0.3, 5.0, 14.0, 0.7);
            for (var b = 0; b < kernel.PhasesPerAxis; ++b) {
                for (var a = 0; a < kernel.PhasesPerAxis; ++a) {
                    double sum = 0.0;
                    foreach (var v in kernel.GetPhaseKernel(a, b)) sum += v;
                    Assert.That(sum, Is.EqualTo(1.0).Within(1e-5), $"phase ({a},{b}) sums to 1");
                }
            }
        }

        [Test]
        public void Astigmatic_DonutHole_AppearsOnBothPrincipalAxes() {
            // The elliptical analogue of LargeDefocus_DonutHoleRatioApproachesObstruction: the central
            // obstruction scales with the same affine map, so each principal axis shows a hole at ε of that
            // axis's own extent.
            const double eps = 0.3;
            const double aRad = 18.0, aTan = 30.0;
            var kernel = PsfKernelGenerator.GenerateElliptical(1.44, eps, aRad, aTan, 0.0);

            foreach (var (axis, semiAxis) in new[] {
                (PsfPrincipalAxis.Radial, aRad), (PsfPrincipalAxis.Tangential, aTan)
            }) {
                var (inner, outer) = HalfMaxExtent(r => kernel.AxisIntensity(r, axis), semiAxis * 1.4);
                Assert.That(inner, Is.GreaterThan(0.0), $"{axis} axis has a central hole");
                Assert.That(inner / outer, Is.EqualTo(eps).Within(0.06), $"{axis} inner/outer half-max ratio → ε");
            }
        }

        [Test]
        public void Astigmatic_RadialIntensity_RefusesToGuessAProfile() {
            var kernel = PsfKernelGenerator.GenerateElliptical(1.44, 0.3, 4.0, 12.0, 0.0);
            Assert.Throws<InvalidOperationException>(() => kernel.RadialIntensity(2.0));
        }

        [Test]
        public void Astigmatic_SupportRadiusExceedingTheCap_Throws() {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PsfKernelGenerator.GenerateElliptical(1.44, 0.3, 10.0, 5000.0, 0.0));
        }

        [Test]
        public void Astigmatic_FftMethod_IsNotImplemented() {
            var model = BuildSanityVector();
            Assert.Throws<NotSupportedException>(
                () => PsfKernelGenerator.GenerateAstigmatic(model, 100.0, 300.0, 0.0, PsfKernelMethod.Fft));
        }

        /// <summary>
        /// Flux-weighted second moments of the zero-phase raster about the kernel centre, in px², with the
        /// square pixel's own variance removed.
        ///
        /// <para>The phase kernel is the continuous PSF integrated over each pixel, so its second moment is the
        /// PSF's plus a uniform pixel's 1/12 px² per axis. Subtracting that is what lets these tests compare
        /// against the closed form for the PSF itself rather than for the PSF-as-sampled.</para>
        /// </summary>
        private static (double varX, double varY, double covXY) SecondMoments(PsfKernel kernel) {
            var (rawX, rawY, cov) = RawSecondMoments(kernel);
            return (rawX - 1.0 / 12.0, rawY - 1.0 / 12.0, cov);
        }

        private static (double varX, double varY, double covXY) RawSecondMoments(PsfKernel kernel) {
            var phase = kernel.GetPhaseKernel(0, 0);
            var size = kernel.Size;
            var centre = kernel.Radius;
            double sum = 0.0, sx = 0.0, sy = 0.0, sxx = 0.0, syy = 0.0, sxy = 0.0;
            for (var j = 0; j < size; ++j) {
                var dy = j - centre;
                for (var i = 0; i < size; ++i) {
                    var w = phase[j * size + i];
                    if (w == 0f) continue;
                    var dx = i - centre;
                    sum += w;
                    sx += w * dx;
                    sy += w * dy;
                    sxx += w * dx * dx;
                    syy += w * dy * dy;
                    sxy += w * dx * dy;
                }
            }
            var mx = sx / sum;
            var my = sy / sum;
            return (sxx / sum - mx * mx, syy / sum - my * my, sxy / sum - mx * my);
        }

        /// <summary>Eccentricity √(1 − λ_min/λ_max) of a 2×2 covariance.</summary>
        private static double Eccentricity(double varX, double varY, double covXY) {
            var mean = 0.5 * (varX + varY);
            var delta = Math.Sqrt(0.25 * (varX - varY) * (varX - varY) + covXY * covXY);
            var major = mean + delta;
            var minor = mean - delta;
            return Math.Sqrt(Math.Max(0.0, 1.0 - minor / major));
        }

        /// <summary>Inner and outer half-maximum radii of a 1-D profile, for donut-hole geometry.</summary>
        private static (double inner, double outer) HalfMaxExtent(Func<double, double> profile, double rMax) {
            const double step = 0.02;
            var peak = 0.0;
            for (var r = 0.0; r <= rMax; r += step) peak = Math.Max(peak, profile(r));
            var half = 0.5 * peak;
            double inner = -1.0, outer = -1.0;
            for (var r = 0.0; r <= rMax; r += step) {
                if (profile(r) >= half) {
                    if (inner < 0.0) inner = r;
                    outer = r;
                }
            }
            return (inner, outer);
        }
    }
}
