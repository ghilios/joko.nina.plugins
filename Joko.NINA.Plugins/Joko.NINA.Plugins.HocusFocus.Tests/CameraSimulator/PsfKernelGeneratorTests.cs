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
    }
}
