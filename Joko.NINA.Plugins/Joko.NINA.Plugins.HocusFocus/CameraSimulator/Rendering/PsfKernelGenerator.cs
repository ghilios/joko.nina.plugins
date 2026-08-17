#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Buffers;
using System.Numerics;

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

        /// <summary>Guard for the 0/0 at the exact centre of the elliptical quadric, where q and |∇q| both vanish.</summary>
        private const double QuadricOriginEpsilon = 1e-12;

        /// <summary>
        /// How many sub-samples per fine cell the elliptical rim ramp needs, as a multiple of the ratio of cell
        /// size to bright-wall thickness. 3 puts three sub-samples across the thinnest wall, which measured
        /// 0.3–0.4 % against the exact Rice form at a line focus (versus 3.1 % unsampled).
        /// </summary>
        private const double RimSubSampleFactor = 3.0;

        /// <summary>Cap on the rim sub-sampling factor per axis; 8 costs 64 cheap evaluations per fine cell.</summary>
        private const int MaxRimSubSamples = 8;

        /// <summary>
        /// How many fine cells of margin the cheap interior/exterior classification leaves around each rim
        /// before it stops trusting the first-order distance. A cell's half-diagonal is 0.707 cells, so 1.5
        /// leaves ample room for the linearization to be off without a cell being misclassified.
        /// </summary>
        private const double CellClassificationMarginCells = 1.5;

        /// <summary>
        /// Simpson intervals for the <b>angular</b> half of the elliptical Rice integral. Far fewer than the
        /// radial <see cref="SimpsonIntervals"/> because the integrand is a smooth √(a²cos²φ + b²sin²φ) over a
        /// quarter turn; the full count would quadruple the Bessel evaluations for no measurable accuracy.
        /// </summary>
        private const int EllipticalHfrAngularIntervals = 24;

        /// <summary>
        /// Hard cap on the kernel support radius (px). A (2R+1)²-per-phase kernel plus its S×-oversampled fine
        /// raster grows as R², so an out-of-range defocus must not be allowed to allocate unboundedly. At the
        /// cap the fine raster is ≈(2·513+1)·4 ≈ 4108 per axis (~135 MB transient) — already generous.
        /// <b>Precondition:</b> callers must quantize <c>defocusMicrons</c> to a bounded range so the geometric
        /// donut radius stays within this cap; exceeding it throws rather than risking an OOM.
        /// </summary>
        public const int MaxKernelRadius = 512;

        /// <summary>
        /// Entries in one profile LUT for a kernel of the given support radius — the same expression both
        /// rasterizer paths use. Exposed so the compositor's cache budget can account for the LUTs instead of
        /// counting only the phase bank: they have a 512-entry floor, so a frame made of thousands of small
        /// kernels pays for them noticeably.
        /// </summary>
        internal static int EstimateProfileLutEntries(int radiusPixels) {
            const int pad = 1;
            var lutMax = (radiusPixels + pad) * Math.Sqrt(2.0) + 1.0;
            return Math.Max(512, (int)Math.Ceiling(lutMax * 2.0 * DefaultPhasesPerAxis) + 1);
        }

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
        /// Builds the PSF for an <b>astigmatic</b> field point, from its tangential and sagittal defocuses and
        /// its field angle. The blur is an elliptical annulus whose <b>radial</b> semi-axis comes from
        /// <paramref name="tangentialDefocusMicrons"/> and whose <b>tangential</b> semi-axis comes from
        /// <paramref name="sagittalDefocusMicrons"/> — that crossing is the mechanism behind the 90° flip, and
        /// it falls out of the substitution rather than being imposed.
        ///
        /// <para><b>Equal semi-axes take the exact radial path, untouched.</b> That is not an optimization but
        /// the guarantee that nothing regresses when astigmatism is off or a star sits on the optical axis:
        /// the returned kernel is bit-for-bit the one <see cref="Generate"/> would have produced.</para>
        /// </summary>
        public static PsfKernel GenerateAstigmatic(
                DefocusModel model, double tangentialDefocusMicrons, double sagittalDefocusMicrons,
                double positionAngleRadians, PsfKernelMethod method = PsfKernelMethod.Analytic) {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (method == PsfKernelMethod.Fft) {
                throw new NotSupportedException(
                    "The FFT PSF path (|Cv2.Dft(pupil·phase)|²) is a reserved seam and is not implemented; use PsfKernelMethod.Analytic.");
            }
            var radialSemiAxis = model.OuterAnnulusRadiusPixels(tangentialDefocusMicrons);
            var tangentialSemiAxis = model.OuterAnnulusRadiusPixels(sagittalDefocusMicrons);
            if (radialSemiAxis == tangentialSemiAxis) {
                return Generate(model, tangentialDefocusMicrons);
            }
            return GenerateElliptical(
                model.SigmaMinPixels, model.CentralObstructionFraction,
                radialSemiAxis, tangentialSemiAxis, positionAngleRadians, DefaultPhasesPerAxis);
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
            return new PsfKernel(s, radius, rOut, rIn, measuredHfr, analyticHfr, phases, lut, radialStep);
        }

        /// <summary>
        /// Rasterizes the <b>elliptical</b> annulus⊛Gaussian: an antialiased coverage mask of the elliptical
        /// annulus on the same S×-oversampled grid the circular path uses, convolved with a separable circular
        /// Gaussian, then box-binned into the same S² sub-pixel phases.
        ///
        /// <para><b>Why not warp the circular profile.</b> Stretching the isotropic result anisotropically also
        /// stretches σ, giving <c>Gaussian(σ·a/r̄, σ·b/r̄)</c> edge softness. At the astigmatic line focus
        /// (a → 0) that predicts a zero-width line and eccentricity → 1, where the truth is a σ-wide line. The
        /// error is O(1) precisely in the near-focus corner regime this feature exists to model, so the warp is
        /// wrong where it matters most rather than slightly wrong everywhere.</para>
        ///
        /// <para><b>Why an antialiased mask.</b> A hard 0/1 mask sampled at h = 1/S folds rim content down to
        /// low frequency, where the Gaussian cannot remove it (~0.5 % ripple on a 5 px annulus). Coverage comes
        /// from the first-order signed distance to each rim, which brings the discretization error back to
        /// O(h²).</para>
        ///
        /// <para><b>Why managed convolution rather than <c>Cv2.SepFilter2D</c>.</b> OpenCV picks its SIMD path
        /// by runtime CPU feature, and <c>StarFieldCompositor.Render</c> is contractually a pure function whose
        /// byte-for-byte determinism is asserted (the camera prefetches renders on the strength of it). Managed
        /// float summation in a fixed order is portable-deterministic. Switching to OpenCV would be an
        /// all-or-nothing decision, since it changes bytes.</para>
        /// </summary>
        internal static PsfKernel GenerateElliptical(
                double sigmaPixels, double obstructionFraction,
                double radialSemiAxisPixels, double tangentialSemiAxisPixels,
                double positionAngleRadians, int phasesPerAxis = DefaultPhasesPerAxis) {
            if (sigmaPixels <= 0.0) throw new ArgumentOutOfRangeException(nameof(sigmaPixels));
            if (radialSemiAxisPixels < 0.0) throw new ArgumentOutOfRangeException(nameof(radialSemiAxisPixels));
            if (tangentialSemiAxisPixels < 0.0) throw new ArgumentOutOfRangeException(nameof(tangentialSemiAxisPixels));
            if (!(obstructionFraction >= 0.0 && obstructionFraction < 1.0)) throw new ArgumentOutOfRangeException(nameof(obstructionFraction));
            if (phasesPerAxis < 1) throw new ArgumentOutOfRangeException(nameof(phasesPerAxis));

            var s = phasesPerAxis;
            var h = 1.0 / s;
            var cos = Math.Cos(positionAngleRadians);
            var sin = Math.Sin(positionAngleRadians);
            var eps = obstructionFraction;

            // Support: the ellipse's own axis-aligned bounding half-extents plus the Gaussian tail. Tighter
            // than max(a,b)+5σ, and identical to it when the two axes are equal. Kept square so StarStamper,
            // PsfKernel.Size and the golden box math are untouched.
            var halfExtentX = Math.Sqrt(Square(radialSemiAxisPixels * cos) + Square(tangentialSemiAxisPixels * sin)) + SupportSigmaMargin * sigmaPixels;
            var halfExtentY = Math.Sqrt(Square(radialSemiAxisPixels * sin) + Square(tangentialSemiAxisPixels * cos)) + SupportSigmaMargin * sigmaPixels;
            var radius = Math.Max(1, (int)Math.Ceiling(Math.Max(halfExtentX, halfExtentY)));
            if (radius > MaxKernelRadius) {
                throw new ArgumentOutOfRangeException(nameof(radialSemiAxisPixels),
                    $"PSF support radius {radius}px (a_rad={radialSemiAxisPixels:F1}px, a_tan={tangentialSemiAxisPixels:F1}px, σ={sigmaPixels:F2}px) "
                    + $"exceeds MaxKernelRadius={MaxKernelRadius}px. Quantize the defocus to a bounded range before generating the kernel.");
            }

            const int pad = 1;
            var fineCount = (2 * (radius + pad) + 1) * s;
            var baseCoord = -(radius + pad) - 0.5;

            // Floor each semi-axis at one fine cell. A line focus has a literally zero semi-axis, which would
            // leave the rasterized mask empty and hand the normalized kernel over to rounding noise. One fine
            // cell is 0.25 px against a σ of order 1.4 px, so the substituted width adds h²/12 ≈ 0.005 px² of
            // variance — well under a percent of σ² — while guaranteeing the mask is actually sampled.
            var aR = Math.Max(radialSemiAxisPixels, h);
            var aT = Math.Max(tangentialSemiAxisPixels, h);
            var invAR = 1.0 / aR;
            var invAT = 1.0 / aT;
            var invAR2 = invAR * invAR;
            var invAT2 = invAT * invAT;

            var coords = new double[fineCount];
            for (var m = 0; m < fineCount; ++m) {
                coords[m] = baseCoord + (m + 0.5) * h;
            }

            // The rim ramp below is a first-order approximation: it assumes a cell crosses each rim once and
            // that the rim is locally straight. Both fail once the bright wall is thinner than a cell, which is
            // exactly what a near-line focus produces — measured against the exact Rice form, a single sample
            // per cell is 3.1 % high at the degenerate end while converging to 0.01 % for a round donut. So the
            // ramp is evaluated on a sub-grid whose spacing resolves the wall, and averaged. Round and thick
            // cases resolve to one sample and cost exactly what they did.
            var wallThickness = (1.0 - eps) * Math.Min(aR, aT);
            var subSamples = (int)Math.Clamp(Math.Ceiling(RimSubSampleFactor * h / Math.Max(wallThickness, 1e-9)), 1, MaxRimSubSamples);
            var subStep = h / subSamples;
            var subOffsets = new double[subSamples];
            for (var k = 0; k < subSamples; ++k) {
                subOffsets[k] = (k + 0.5) * subStep - 0.5 * h;
            }
            var subNormalization = 1.0 / (subSamples * subSamples);

            // --- pass 1: antialiased elliptical-annulus coverage mask, in the ellipse's own frame ---
            // The two fine-grid buffers are pooled. At a typical support they are ~360 KB each, which is past
            // the 85 KB large-object-heap threshold -- so a frame that builds a few hundred kernels would
            // otherwise churn hundreds of megabytes through the LOH, which is neither compacted nor cheap.
            var length = fineCount * fineCount;
            var pool = ArrayPool<float>.Shared;
            var mask = pool.Rent(length);
            var temp = pool.Rent(length);
            try {
            Array.Clear(mask, 0, length);
            int minX = fineCount, maxX = -1, minY = fineCount, maxY = -1;
            for (var my = 0; my < fineCount; ++my) {
                var cy = coords[my];
                var rowBase = my * fineCount;
                for (var mx = 0; mx < fineCount; ++mx) {
                    var cx = coords[mx];

                    // Cheap interior/exterior test at the cell centre. Only cells straddling a rim need the
                    // sub-sampled ramp, and for any real kernel that is a thin band around the perimeter --
                    // a couple of thousand cells out of ninety thousand. Without this the sub-sampling cost is
                    // paid on every cell and dominates kernel generation.
                    var classification = ClassifyCell(cx, cy, cos, sin, invAR, invAT, invAR2, invAT2, eps, h);
                    if (classification == CellClassification.Outside) {
                        continue;
                    }
                    if (classification == CellClassification.Inside) {
                        mask[rowBase + mx] = 1f;
                        if (mx < minX) minX = mx;
                        if (mx > maxX) maxX = mx;
                        if (my < minY) minY = my;
                        if (my > maxY) maxY = my;
                        continue;
                    }

                    var coverSum = 0.0;
                    for (var sy = 0; sy < subSamples; ++sy) {
                        var py = cy + subOffsets[sy];
                        for (var sx = 0; sx < subSamples; ++sx) {
                            var px = cx + subOffsets[sx];
                            var u = px * cos + py * sin;    // along the radial axis
                            var v = -px * sin + py * cos;   // along the tangential axis
                            var qx = u * invAR;
                            var qy = v * invAT;
                            var q = Math.Sqrt(qx * qx + qy * qy);

                            if (q <= QuadricOriginEpsilon) {
                                // Dead centre: inside the obstruction hole, or inside a filled ellipse if none.
                                if (eps <= 0.0) coverSum += 1.0;
                                continue;
                            }
                            // |∇q| = √((u/a_r²)² + (v/a_t²)²)/q, so the first-order distance to the q = c
                            // contour is (q − c)·q / √((u/a_r²)² + (v/a_t²)²). Coverage then ramps linearly
                            // across the rim — the product-of-two-ramps form StarStamper.AnnulusIntensity
                            // already uses for the circular case.
                            var gx = u * invAR2;
                            var gy = v * invAT2;
                            var g = Math.Sqrt(gx * gx + gy * gy);
                            if (g < QuadricOriginEpsilon) {
                                if (eps <= 0.0) coverSum += 1.0;
                                continue;
                            }
                            var scale = q / (g * subStep);
                            var cover = Clamp01(0.5 - (q - 1.0) * scale);
                            if (eps > 0.0 && cover > 0.0) {
                                cover *= Clamp01(0.5 + (q - eps) * scale);
                            }
                            coverSum += cover;
                        }
                    }

                    var averaged = coverSum * subNormalization;
                    if (averaged > 0.0) {
                        mask[rowBase + mx] = (float)averaged;
                        if (mx < minX) minX = mx;
                        if (mx > maxX) maxX = mx;
                        if (my < minY) minY = my;
                        if (my > maxY) maxY = my;
                    }
                }
            }

            // --- pass 2: separable Gaussian ---
            // Double-box correction: the coverage mask is already (ideal mask ⊛ box_h), and the phase box-bin
            // below applies a second box. Narrowing the taps by h²/12 lands the result on point samples of
            // (mask ⊛ Gaussian), which is exactly what the circular path produces and what the box-bin expects.
            // Without it the elliptical kernel comes out ~0.2 % softer than its circular counterpart.
            var sigmaTapsPixels = Math.Sqrt(Math.Max(sigmaPixels * sigmaPixels - h * h / 12.0, 0.25 * sigmaPixels * sigmaPixels));
            var sigmaTapsFine = sigmaTapsPixels * s;
            var tapRadius = Math.Max(1, (int)Math.Ceiling(SupportSigmaMargin * sigmaTapsFine));
            var taps = new float[2 * tapRadius + 1];
            var tapSum = 0.0;
            for (var k = -tapRadius; k <= tapRadius; ++k) {
                var t = Math.Exp(-(k * k) / (2.0 * sigmaTapsFine * sigmaTapsFine));
                taps[k + tapRadius] = (float)t;
                tapSum += t;
            }
            for (var k = 0; k < taps.Length; ++k) taps[k] = (float)(taps[k] / tapSum);

            if (maxX >= 0) {
                // Only the mask's bounding box dilated by the tap radius can be nonzero. For a thin ellipse in a
                // square support that is a small fraction of the grid.
                var x0 = Math.Max(0, minX - tapRadius);
                var x1 = Math.Min(fineCount - 1, maxX + tapRadius);
                var y0 = Math.Max(0, minY - tapRadius);
                var y1 = Math.Min(fineCount - 1, maxY + tapRadius);

                // Both passes are written tap-outer rather than tap-inner: for a fixed tap the destination
                // span is a straight shifted multiply-accumulate over x, which vectorizes without any gather.
                // Each output still accumulates its taps in the same order whatever the vector width, so the
                // result does not depend on which SIMD path the CPU offers -- which matters, because Render is
                // contractually a pure function whose byte-for-byte determinism is asserted.
                var width = x1 - x0 + 1;
                for (var y = minY; y <= maxY; ++y) {
                    var rowBase = y * fineCount;
                    Array.Clear(temp, rowBase + x0, width);
                    for (var k = -tapRadius; k <= tapRadius; ++k) {
                        var tap = taps[k + tapRadius];
                        var from = Math.Max(x0, minX - k);
                        var to = Math.Min(x1, maxX - k);
                        if (from > to) continue;
                        AccumulateScaled(temp, rowBase, mask, rowBase + k, from, to, tap);
                    }
                }
                // Column pass writes back into `mask`, so the two buffers are all this path ever needs.
                Array.Clear(mask, 0, length);
                for (var y = y0; y <= y1; ++y) {
                    var rowBase = y * fineCount;
                    var kFrom = Math.Max(-tapRadius, minY - y);
                    var kTo = Math.Min(tapRadius, maxY - y);
                    for (var k = kFrom; k <= kTo; ++k) {
                        AccumulateScaled(mask, rowBase, temp, (y + k) * fineCount, x0, x1, taps[k + tapRadius]);
                    }
                }
            }
            var fine = mask;

            // --- pass 3: measure, box-bin into phases, and sample the two principal-axis profiles ---
            double fluxSum = 0.0, radiusFluxSum = 0.0;
            for (var my = 0; my < fineCount; ++my) {
                var cy = coords[my];
                var rowBase = my * fineCount;
                for (var mx = 0; mx < fineCount; ++mx) {
                    var value = fine[rowBase + mx];
                    if (value == 0f) continue;
                    var cx = coords[mx];
                    fluxSum += value;
                    radiusFluxSum += value * Math.Sqrt(cx * cx + cy * cy);
                }
            }
            var measuredHfr = fluxSum > 0.0 ? radiusFluxSum / fluxSum : 0.0;

            // Box-bin, identical in structure to GenerateAnalytic's — the two paths must produce the same phase
            // semantics, so this loop deliberately mirrors that one rather than sharing a generic helper that
            // would force the circular path onto a different element type and shift its bytes.
            var size = 2 * radius + 1;
            var phases = new float[s * s][];
            for (var b = 0; b < s; ++b) {
                for (var a = 0; a < s; ++a) {
                    var kernel = new float[size * size];
                    double sum = 0.0;
                    for (var j = -radius; j <= radius; ++j) {
                        var kRow = (j + radius) * size;
                        for (var i = -radius; i <= radius; ++i) {
                            double acc = 0.0;
                            for (var p = 0; p < s; ++p) {
                                var my = s * (j + radius + pad) + p - b;
                                var fineRow = my * fineCount;
                                for (var q2 = 0; q2 < s; ++q2) {
                                    var mx = s * (i + radius + pad) + q2 - a;
                                    acc += fine[fineRow + mx];
                                }
                            }
                            kernel[kRow + (i + radius)] = (float)acc;
                            sum += acc;
                        }
                    }
                    if (sum > 0.0) {
                        var inv = 1.0 / sum;
                        for (var k = 0; k < kernel.Length; ++k) kernel[k] *= (float)inv;
                    }
                    phases[b * s + a] = kernel;
                }
            }

            var lutMax = (radius + pad) * Math.Sqrt(2.0) + 1.0;
            var lutCount = Math.Max(512, (int)Math.Ceiling(lutMax * 2.0 * s) + 1);
            var radialStep = lutMax / (lutCount - 1);
            var radialLut = new double[lutCount];
            var tangentialLut = new double[lutCount];
            for (var n = 0; n < lutCount; ++n) {
                var r = n * radialStep;
                radialLut[n] = SampleFine(fine, fineCount, baseCoord, h, r * cos, r * sin);
                tangentialLut[n] = SampleFine(fine, fineCount, baseCoord, h, -r * sin, r * cos);
            }

            var analyticHfr = EllipticalRiceHfr(sigmaPixels, eps, radialSemiAxisPixels, tangentialSemiAxisPixels);
            var varianceRadial = sigmaPixels * sigmaPixels + Square(radialSemiAxisPixels) * (1.0 + eps * eps) / 4.0;
            var varianceTangential = sigmaPixels * sigmaPixels + Square(tangentialSemiAxisPixels) * (1.0 + eps * eps) / 4.0;
            var eccentricity = Math.Sqrt(Math.Max(0.0,
                1.0 - Math.Min(varianceRadial, varianceTangential) / Math.Max(varianceRadial, varianceTangential)));

            return new PsfKernel(
                s, radius, radialSemiAxisPixels, tangentialSemiAxisPixels, positionAngleRadians,
                eps * Math.Max(radialSemiAxisPixels, tangentialSemiAxisPixels),
                measuredHfr, analyticHfr, eccentricity, phases, radialLut, tangentialLut, radialStep);
            } finally {
                pool.Return(mask);
                pool.Return(temp);
            }
        }

        /// <summary>
        /// Rice closed-form HFR of the <b>elliptical</b> annulus⊛Gaussian. The uniform pupil annulus maps
        /// through <c>M = R(θ)·diag(a_rad, a_tan)</c> with a constant Jacobian, so averaging the Rice mean
        /// radius over the pupil in normalized coordinates is exact:
        /// <code>
        /// HFR = (1/(π(1−ε²))) ∫₀^{2π} ∫_ε^1 RiceMean(ρ·√((a_rad·cosφ)² + (a_tan·sinφ)²), σ) ρ dρ dφ
        /// </code>
        /// The integrand has period π in φ and is symmetric about the axes, so only a quarter is evaluated.
        /// Reduces algebraically to <see cref="RiceHfr"/> when the semi-axes are equal, which keeps
        /// measured-vs-analytic HFR a genuinely independent check on the rasterizer.
        /// </summary>
        internal static double EllipticalRiceHfr(double sigmaPixels, double obstructionFraction, double radialSemiAxisPixels, double tangentialSemiAxisPixels) {
            var eps = obstructionFraction;
            var largest = Math.Max(radialSemiAxisPixels, tangentialSemiAxisPixels);
            if (largest <= GaussianLimitFraction * sigmaPixels) {
                return sigmaPixels * SigmaToHfr;
            }

            var rhoStep = (1.0 - eps) / SimpsonIntervals;
            var phiStep = (Math.PI / 2.0) / EllipticalHfrAngularIntervals;
            double outer = 0.0;
            for (var i = 0; i <= EllipticalHfrAngularIntervals; ++i) {
                var phi = i * phiStep;
                var cosPhi = Math.Cos(phi);
                var sinPhi = Math.Sin(phi);
                var scale = Math.Sqrt(Square(radialSemiAxisPixels * cosPhi) + Square(tangentialSemiAxisPixels * sinPhi));

                double inner = 0.0;
                for (var j = 0; j <= SimpsonIntervals; ++j) {
                    var rho = eps + j * rhoStep;
                    var f = rho * RiceMean(rho * scale, sigmaPixels);
                    var w = (j == 0 || j == SimpsonIntervals) ? 1.0 : (j % 2 == 1 ? 4.0 : 2.0);
                    inner += w * f;
                }
                inner *= rhoStep / 3.0;

                var wo = (i == 0 || i == EllipticalHfrAngularIntervals) ? 1.0 : (i % 2 == 1 ? 4.0 : 2.0);
                outer += wo * inner;
            }
            outer *= phiStep / 3.0;

            // ×4 for the quarter-plane symmetry, ÷ the pupil area π(1−ε²).
            return 4.0 * outer / (Math.PI * (1.0 - eps * eps));
        }

        /// <summary>
        /// <c>destination[destBase + x] += source[srcBase + x] * scale</c> for x in [from, to], vectorized.
        /// The scalar tail runs the identical arithmetic, so the vector width changes only how many elements
        /// are handled at once, never the value any one of them gets.
        /// </summary>
        private static void AccumulateScaled(float[] destination, int destBase, float[] source, int srcBase, int from, int to, float scale) {
            var x = from;
            if (Vector.IsHardwareAccelerated) {
                var vScale = new Vector<float>(scale);
                var lanes = Vector<float>.Count;
                for (; x <= to - lanes + 1; x += lanes) {
                    var accumulated = new Vector<float>(destination, destBase + x) + new Vector<float>(source, srcBase + x) * vScale;
                    accumulated.CopyTo(destination, destBase + x);
                }
            }
            for (; x <= to; ++x) {
                destination[destBase + x] += source[srcBase + x] * scale;
            }
        }

        /// <summary>Where a fine cell sits relative to the elliptical annulus.</summary>
        private enum CellClassification {

            /// <summary>Entirely outside the outer rim, or entirely inside the obstruction hole. Coverage 0.</summary>
            Outside,

            /// <summary>Entirely within the bright annulus. Coverage 1.</summary>
            Inside,

            /// <summary>Straddles a rim; needs the sub-sampled coverage ramp.</summary>
            Straddling
        }

        /// <summary>
        /// Classifies a fine cell from its centre alone. Uses the same first-order rim distance the ramp does,
        /// with a margin of <see cref="CellClassificationMarginCells"/> cells — comfortably more than a cell's
        /// half-diagonal (0.707 h), so the linearization has room to be wrong without misclassifying.
        /// </summary>
        private static CellClassification ClassifyCell(
                double cx, double cy, double cos, double sin,
                double invAR, double invAT, double invAR2, double invAT2, double eps, double h) {
            var u = cx * cos + cy * sin;
            var v = -cx * sin + cy * cos;
            var qx = u * invAR;
            var qy = v * invAT;
            var q = Math.Sqrt(qx * qx + qy * qy);
            if (q <= QuadricOriginEpsilon) {
                return eps > 0.0 ? CellClassification.Outside : CellClassification.Straddling;
            }
            var gx = u * invAR2;
            var gy = v * invAT2;
            var g = Math.Sqrt(gx * gx + gy * gy);
            if (g < QuadricOriginEpsilon) {
                return CellClassification.Straddling;
            }

            var margin = CellClassificationMarginCells * h;
            var distanceToOuterRim = (q - 1.0) * q / g;
            if (distanceToOuterRim > margin) {
                return CellClassification.Outside;
            }
            if (distanceToOuterRim > -margin) {
                return CellClassification.Straddling;
            }
            if (eps <= 0.0) {
                return CellClassification.Inside;
            }
            var distanceToInnerRim = (q - eps) * q / g;
            if (distanceToInnerRim < -margin) {
                return CellClassification.Outside;   // deep inside the obstruction hole
            }
            return distanceToInnerRim > margin ? CellClassification.Inside : CellClassification.Straddling;
        }

        /// <summary>Bilinear sample of the fine grid at a continuous coordinate; 0 outside the rasterized support.</summary>
        private static double SampleFine(float[] fine, int fineCount, double baseCoord, double h, double x, double y) {
            var fx = (x - baseCoord) / h - 0.5;
            var fy = (y - baseCoord) / h - 0.5;
            var ix = (int)Math.Floor(fx);
            var iy = (int)Math.Floor(fy);
            if (ix < 0 || iy < 0 || ix + 1 >= fineCount || iy + 1 >= fineCount) {
                return 0.0;
            }
            var tx = fx - ix;
            var ty = fy - iy;
            var row0 = iy * fineCount;
            var row1 = row0 + fineCount;
            return (1.0 - ty) * ((1.0 - tx) * fine[row0 + ix] + tx * fine[row0 + ix + 1])
                 + ty * ((1.0 - tx) * fine[row1 + ix] + tx * fine[row1 + ix + 1]);
        }

        private static double Square(double x) => x * x;

        private static double Clamp01(double x) => x <= 0.0 ? 0.0 : (x >= 1.0 ? 1.0 : x);

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
