#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Builds the analytic <b>annulus⊛Gaussian</b> PSF for a given defocus and rasterizes it into a bank of
    /// S×S sub-pixel-phase kernels (see <see cref="PsfKernel"/>). The radial profile (design §Optics) is
    /// <code>
    /// I(r) = 1/(π(r_out²−r_in²)σ²) · ∫_{r_in}^{r_out} R·e^(−(r−R)²/(2σ²))·e^(−rR/σ²)·I₀(rR/σ²) dR
    /// </code>
    /// evaluated into a radial LUT (Simpson), rasterized on an S×-oversampled grid over the support
    /// radius ≈ ceil(r_out + 5σ), then box-binned into S² phase kernels, each normalized to Σ=1. σ, r_out and
    /// r_in all come from <see cref="DefocusModel"/> so the rendered HFR tracks the reported V-curve by
    /// construction. At focus (r_out ≈ r_in ≈ 0) the profile reduces cleanly to a pure Gaussian of σ.
    ///
    /// The kernel's HFR is measured (flux-weighted mean radius of the raster) and, independently, computed in
    /// closed form via the Rice distribution — <c>E[r|R]=σ√(π/2)·e^(−t/2)[(1+t)I₀(t/2)+t·I₁(t/2)]</c>,
    /// <c>t=R²/(2σ²)</c>, <c>HFR=∫R·E[r|R]dR / ((r_out²−r_in²)/2)</c> — so tests can cross-check the raster.
    /// </summary>
    public static class PsfKernelGenerator {

        /// <summary>Default sub-pixel phases per axis, S. 16 phases total; quantizes centres to 1/8 px.</summary>
        public const int DefaultPhasesPerAxis = 4;

        /// <summary>Gaussian σ→HFR factor √(π/2): the flux-weighted mean radius of a 2-D Gaussian.</summary>
        private const double SigmaToHfr = 1.2533141373155003;

        /// <summary>Number of tail σ included in the kernel support beyond the geometric donut edge.</summary>
        private const double SupportSigmaMargin = 5.0;

        /// <summary>Simpson intervals for the radial and Rice integrals (must be even).</summary>
        private const int SimpsonIntervals = 100;

        /// <summary>Below this annulus outer radius (relative to σ) the PSF is treated as a pure Gaussian.</summary>
        private const double GaussianLimitFraction = 1e-6;

        /// <summary>
        /// Hard cap on the kernel support radius (px). A (2R+1)²-per-phase kernel plus its S×-oversampled fine
        /// raster grows as R², so an out-of-range defocus must not be allowed to allocate unboundedly. At the
        /// cap the fine raster is ≈(2·513+1)·4 ≈ 4108 per axis (~135 MB transient) — already generous.
        /// <b>Precondition:</b> callers must quantize <c>defocusMicrons</c> to a bounded range so the geometric
        /// donut radius stays within this cap; exceeding it throws rather than risking an OOM.
        /// </summary>
        public const int MaxKernelRadius = 512;

        /// <summary>Builds the PSF for a defocus Δ (µm) using the sizing from <paramref name="model"/>.</summary>
        public static PsfKernel Generate(DefocusModel model, double defocusMicrons, PsfKernelMethod method = PsfKernelMethod.Analytic) {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (method == PsfKernelMethod.Fft) {
                throw new NotSupportedException(
                    "The FFT PSF path (|Cv2.Dft(pupil·phase)|²) is a reserved seam and is not implemented; use PsfKernelMethod.Analytic.");
            }
            var sigma = model.SigmaMinPixels;
            var rOut = model.OuterAnnulusRadiusPixels(defocusMicrons);
            var rIn = model.InnerAnnulusRadiusPixels(defocusMicrons);
            return GenerateAnalytic(sigma, rIn, rOut, DefaultPhasesPerAxis);
        }

        /// <summary>
        /// Builds the PSF directly from the σ, r_in, r_out (px) that <see cref="Generate"/> resolved from the
        /// model. <paramref name="innerRadiusPixels"/> must satisfy 0 ≤ r_in ≤ r_out.
        /// </summary>
        private static PsfKernel GenerateAnalytic(double sigmaPixels, double innerRadiusPixels, double outerRadiusPixels, int phasesPerAxis = DefaultPhasesPerAxis) {
            if (sigmaPixels <= 0.0) throw new ArgumentOutOfRangeException(nameof(sigmaPixels));
            if (outerRadiusPixels < 0.0) throw new ArgumentOutOfRangeException(nameof(outerRadiusPixels));
            if (innerRadiusPixels < 0.0 || innerRadiusPixels > outerRadiusPixels) throw new ArgumentOutOfRangeException(nameof(innerRadiusPixels));
            if (phasesPerAxis < 1) throw new ArgumentOutOfRangeException(nameof(phasesPerAxis));

            var sigma = sigmaPixels;
            var rOut = outerRadiusPixels;
            var rIn = innerRadiusPixels;
            var s = phasesPerAxis;
            var isGaussian = rOut <= GaussianLimitFraction * sigma;

            // Kernel support: geometric outer edge plus a Gaussian tail margin. Odd size, centred.
            var radius = Math.Max(1, (int)Math.Ceiling(rOut + SupportSigmaMargin * sigma));
            if (radius > MaxKernelRadius) {
                throw new ArgumentOutOfRangeException(nameof(outerRadiusPixels),
                    $"PSF support radius {radius}px (r_out={rOut:F1}px, σ={sigma:F2}px) exceeds MaxKernelRadius={MaxKernelRadius}px. " +
                    "Quantize the defocus to a bounded range before generating the kernel.");
            }
            const int pad = 1; // fine-grid pad so every phase's shifted box-bin stays in range

            // Radial LUT covering the fine-grid diagonal; step ≤ 1/(2S) so it is at least as fine as the raster.
            var lutMax = (radius + pad) * Math.Sqrt(2.0) + 1.0;
            var lutCount = Math.Max(512, (int)Math.Ceiling(lutMax * 2.0 * s) + 1);
            var radialStep = lutMax / (lutCount - 1);
            var lut = new double[lutCount];
            for (var n = 0; n < lutCount; ++n) {
                var r = n * radialStep;
                lut[n] = isGaussian ? Math.Exp(-r * r / (2.0 * sigma * sigma)) : RadialProfile(r, sigma, rIn, rOut);
            }

            // Oversampled rasterization of the whole (padded) support, centred on the origin.
            var h = 1.0 / s;
            var fineCount = (2 * (radius + pad) + 1) * s;
            var baseCoord = -(radius + pad) - 0.5;
            var fine = new double[fineCount * fineCount];
            var coords = new double[fineCount];
            for (var m = 0; m < fineCount; ++m) {
                coords[m] = baseCoord + (m + 0.5) * h;
            }
            double fluxSum = 0.0, radiusFluxSum = 0.0;
            for (var my = 0; my < fineCount; ++my) {
                var cy = coords[my];
                var rowBase = my * fineCount;
                for (var mx = 0; mx < fineCount; ++mx) {
                    var cx = coords[mx];
                    var r = Math.Sqrt(cx * cx + cy * cy);
                    var value = PsfKernel.InterpolateLut(lut, radialStep, r);
                    fine[rowBase + mx] = value;
                    fluxSum += value;
                    radiusFluxSum += value * r;
                }
            }
            var measuredHfr = fluxSum > 0.0 ? radiusFluxSum / fluxSum : 0.0;

            // Box-bin the single fine raster into S² phase kernels (each a shifted stride), normalize Σ=1.
            var size = 2 * radius + 1;
            var phases = new float[s * s][];
            for (var b = 0; b < s; ++b) {
                for (var a = 0; a < s; ++a) {
                    var kernel = new float[size * size];
                    double sum = 0.0;
                    for (var j = -radius; j <= radius; ++j) {
                        var jj = j + radius;
                        var kRow = jj * size;
                        for (var i = -radius; i <= radius; ++i) {
                            double acc = 0.0;
                            for (var p = 0; p < s; ++p) {
                                var my = s * (j + radius + pad) + p - b;
                                var fineRow = my * fineCount;
                                for (var q = 0; q < s; ++q) {
                                    var mx = s * (i + radius + pad) + q - a;
                                    acc += fine[fineRow + mx];
                                }
                            }
                            var v = acc; // (1/S²) factor cancels in the per-kernel normalization below
                            kernel[kRow + (i + radius)] = (float)v;
                            sum += v;
                        }
                    }
                    if (sum > 0.0) {
                        var inv = 1.0 / sum;
                        for (var k = 0; k < kernel.Length; ++k) kernel[k] *= (float)inv;
                    }
                    phases[b * s + a] = kernel;
                }
            }

            var analyticHfr = isGaussian ? sigma * SigmaToHfr : RiceHfr(sigma, rIn, rOut);
            return new PsfKernel(s, radius, rOut, measuredHfr, analyticHfr, phases, lut, radialStep);
        }

        /// <summary>
        /// Rice closed-form HFR of the annulus⊛Gaussian: the flux-weighted average of the Rice mean radius
        /// E[r|R] over the uniform annulus. Computed independently of the raster and surfaced as
        /// <see cref="PsfKernel.AnalyticHfrPixels"/>, so tests can cross-check it against the rasterized
        /// <see cref="PsfKernel.MeasuredHfrPixels"/>.
        /// </summary>
        private static double RiceHfr(double sigmaPixels, double innerRadiusPixels, double outerRadiusPixels) {
            if (outerRadiusPixels - innerRadiusPixels <= GaussianLimitFraction * sigmaPixels) {
                return sigmaPixels * SigmaToHfr;
            }
            // ∫_{r_in}^{r_out} R·E[r|R] dR via Simpson.
            var a = innerRadiusPixels;
            var b = outerRadiusPixels;
            var step = (b - a) / SimpsonIntervals;
            double sum = 0.0;
            for (var k = 0; k <= SimpsonIntervals; ++k) {
                var r = a + k * step;
                var f = r * RiceMean(r, sigmaPixels);
                var w = (k == 0 || k == SimpsonIntervals) ? 1.0 : (k % 2 == 1 ? 4.0 : 2.0);
                sum += w * f;
            }
            var integral = sum * step / 3.0;
            return integral / ((b * b - a * a) / 2.0);
        }

        /// <summary>Rice-distribution mean radius E[r|R] for a 2-D Gaussian of width σ centred at radius R.</summary>
        private static double RiceMean(double r, double sigma) {
            var t = r * r / (2.0 * sigma * sigma);
            var half = t / 2.0;
            return sigma * SigmaToHfr * ((1.0 + t) * ModifiedBessel.ScaledI0(half) + t * ModifiedBessel.ScaledI1(half));
        }

        /// <summary>Annulus⊛Gaussian radial intensity at radius r (px). Constant prefactor kept for fidelity.</summary>
        private static double RadialProfile(double r, double sigma, double rIn, double rOut) {
            var inv2Sigma2 = 1.0 / (2.0 * sigma * sigma);
            var invSigma2 = 1.0 / (sigma * sigma);
            var step = (rOut - rIn) / SimpsonIntervals;
            double sum = 0.0;
            for (var k = 0; k <= SimpsonIntervals; ++k) {
                var bigR = rIn + k * step;
                var diff = r - bigR;
                // R·e^(−(r−R)²/2σ²)·[e^(−rR/σ²)·I₀(rR/σ²)] — the bracket is the scaled Bessel (overflow-safe).
                var f = bigR * Math.Exp(-diff * diff * inv2Sigma2) * ModifiedBessel.ScaledI0(r * bigR * invSigma2);
                var w = (k == 0 || k == SimpsonIntervals) ? 1.0 : (k % 2 == 1 ? 4.0 : 2.0);
                sum += w * f;
            }
            var integral = sum * step / 3.0;
            return integral / (Math.PI * (rOut * rOut - rIn * rIn) * sigma * sigma);
        }
    }
}
