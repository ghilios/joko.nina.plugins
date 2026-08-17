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

    /// <summary>Which algorithm <see cref="PsfKernelGenerator"/> uses to rasterize the PSF.</summary>
    public enum PsfKernelMethod {

        /// <summary>Analytic annulus⊛Gaussian radial LUT (default; HFR-exact, ~100× cheaper than the FFT path).</summary>
        Analytic,

        /// <summary>High-fidelity <c>|Cv2.Dft(pupil·phase)|²</c> path (adds diffraction rings). Reserved seam; not implemented.</summary>
        Fft
    }

    /// <summary>
    /// A rendered point-spread function for a single quantized defocus level: a bank of S×S sub-pixel-phase
    /// kernels (each a normalized Σ=1 raster whose PSF centre is offset by (a/S, b/S) from a pixel centre),
    /// plus the kernel's measured and analytic HFR. Produced by <see cref="PsfKernelGenerator"/> and consumed
    /// by <see cref="StarStamper.Stamp"/>, which picks the phase matching a star's sub-pixel position and adds
    /// <c>kernel·flux</c> into the electron accumulator.
    ///
    /// The phase kernels are indexed [phaseY, phaseX] with phaseX, phaseY ∈ [0, PhasesPerAxis). Every phase is
    /// (2·<see cref="Radius"/>+1)² floats, row-major, with the PSF centre pixel at index (Radius, Radius).
    /// </summary>
    public sealed class PsfKernel {
        private readonly float[][] phases; // [phaseY*S + phaseX] -> row-major Size*Size
        private readonly double[] radialLut;
        private readonly double radialStepPixels;

        private readonly double[] phasePeaks; // [phaseY*S + phaseX] -> max sample of that phase kernel

        internal PsfKernel(
            int phasesPerAxis,
            int radius,
            double outerRadiusPixels,
            double innerRadiusPixels,
            double measuredHfrPixels,
            double analyticHfrPixels,
            float[][] phases,
            double[] radialLut,
            double radialStepPixels) {
            PhasesPerAxis = phasesPerAxis;
            Radius = radius;
            Size = 2 * radius + 1;
            OuterRadiusPixels = outerRadiusPixels;
            InnerRadiusPixels = innerRadiusPixels;
            MeasuredHfrPixels = measuredHfrPixels;
            AnalyticHfrPixels = analyticHfrPixels;
            this.phases = phases;
            this.radialLut = radialLut;
            this.radialStepPixels = radialStepPixels;

            // Computed once, here, rather than lazily: the compositor caches and shares one PsfKernel across
            // every star at a given quantized defocus (parallel row-stripes read it from multiple threads
            // concurrently), so a lazy cache would need its own locking. Eager computation over the already-
            // built phase arrays is cheap (S² phases, each already materialized) next to generating them.
            var maxPeak = 0.0;
            phasePeaks = new double[phases.Length];
            for (var i = 0; i < phases.Length; ++i) {
                var peak = 0.0;
                var phase = phases[i];
                for (var k = 0; k < phase.Length; ++k) {
                    if (phase[k] > peak) peak = phase[k];
                }
                phasePeaks[i] = peak;
                if (peak > maxPeak) maxPeak = peak;
            }
            MaxPeak = maxPeak;
        }

        /// <summary>Number of sub-pixel phases per axis, S (there are S² phase kernels).</summary>
        public int PhasesPerAxis { get; }

        /// <summary>Kernel support radius R, in pixels. Each phase kernel is (2R+1)×(2R+1).</summary>
        public int Radius { get; }

        /// <summary>Edge length of each phase kernel, 2R+1.</summary>
        public int Size { get; }

        /// <summary>Geometric donut outer radius r_out (px). 0 at focus.</summary>
        public double OuterRadiusPixels { get; }

        /// <summary>
        /// Geometric donut inner radius r_in (px, r_in = ε·r_out). 0 at focus or with no central obstruction.
        /// Was computed inside <see cref="PsfKernelGenerator.Generate"/> and discarded; kept here too since
        /// the SNR math that assigns golden tiers needs it alongside <see cref="OuterRadiusPixels"/>.
        /// </summary>
        public double InnerRadiusPixels { get; }

        /// <summary>Flux-weighted mean radius (HFR, px) measured on the oversampled raster of this kernel.</summary>
        public double MeasuredHfrPixels { get; }

        /// <summary>Rice-distribution closed-form HFR (px) of the annulus⊛Gaussian model — the validation target.</summary>
        public double AnalyticHfrPixels { get; }

        /// <summary>
        /// The normalized (Σ=1) phase kernel for sub-pixel phase (phaseX, phaseY), row-major, Size×Size.
        /// <b>The returned array is this kernel's shared internal buffer — treat it as read-only and never
        /// mutate it.</b> The compositor caches and shares <see cref="PsfKernel"/> instances across many stars,
        /// so an in-place edit would corrupt every subsequent stamp.
        /// </summary>
        public float[] GetPhaseKernel(int phaseX, int phaseY) {
            if (phaseX < 0 || phaseX >= PhasesPerAxis) throw new ArgumentOutOfRangeException(nameof(phaseX));
            if (phaseY < 0 || phaseY >= PhasesPerAxis) throw new ArgumentOutOfRangeException(nameof(phaseY));
            return phases[phaseY * PhasesPerAxis + phaseX];
        }

        /// <summary>
        /// Peak sample of the phase kernel at (phaseX, phaseY) — the <b>fraction of total flux landing in the
        /// brightest pixel</b>, since each phase kernel is normalized to Σ=1. A star's peak electron count is
        /// <c>flux · PhasePeak(...)</c>; used for saturation checks and peak-pixel SNR, where the phase actually
        /// selected for a star's sub-pixel position matters (phases with the PSF centred on a pixel peak
        /// noticeably higher than ones with it split across a pixel boundary).
        /// </summary>
        public double PhasePeak(int phaseX, int phaseY) {
            if (phaseX < 0 || phaseX >= PhasesPerAxis) throw new ArgumentOutOfRangeException(nameof(phaseX));
            if (phaseY < 0 || phaseY >= PhasesPerAxis) throw new ArgumentOutOfRangeException(nameof(phaseY));
            return phasePeaks[phaseY * PhasesPerAxis + phaseX];
        }

        /// <summary>
        /// The largest <see cref="PhasePeak"/> over every phase — the worst-case (most-concentrated) peak
        /// fraction for this kernel, regardless of which sub-pixel phase a star lands on. Useful when a caller
        /// wants a conservative saturation bound without knowing the star's exact phase.
        /// </summary>
        public double MaxPeak { get; }

        /// <summary>
        /// The continuous radial intensity profile I(r) of this PSF (px argument), by linear interpolation of
        /// the LUT it was built from. Returns 0 beyond the rasterized support. Exposed for donut-geometry tests.
        /// </summary>
        public double RadialIntensity(double radiusPixels) => InterpolateLut(radialLut, radialStepPixels, radiusPixels);

        /// <summary>
        /// Approximate heap cost of this kernel in bytes: the S² phase rasters plus the radial LUT. The phase
        /// bank dominates and grows as R², which is why a cache keyed on more than defocus alone needs a byte
        /// budget rather than a count budget. Reported per-render through <see cref="RenderPhaseTimings"/>.
        /// </summary>
        internal long ApproximateByteSize =>
            (long)PhasesPerAxis * PhasesPerAxis * Size * Size * sizeof(float)
            + (long)radialLut.Length * sizeof(double);

        /// <summary>Linear interpolation of a uniform-step radial LUT; 0 beyond the last entry, clamped at r ≤ 0.</summary>
        internal static double InterpolateLut(double[] lut, double stepPixels, double radiusPixels) {
            if (radiusPixels <= 0.0) return lut[0];
            var fractional = radiusPixels / stepPixels;
            var lower = (int)Math.Floor(fractional);
            if (lower >= lut.Length - 1) {
                return lower >= lut.Length ? 0.0 : lut[lut.Length - 1];
            }
            var frac = fractional - lower;
            return lut[lower] * (1.0 - frac) + lut[lower + 1] * frac;
        }
    }
}
