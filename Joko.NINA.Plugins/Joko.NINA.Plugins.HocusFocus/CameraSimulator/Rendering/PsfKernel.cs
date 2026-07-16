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

        /// <summary>High-fidelity <c>|Cv2.Dft(pupil·phase)|²</c> path (adds diffraction rings). Phase 3b seam; not implemented.</summary>
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

        internal PsfKernel(
            int phasesPerAxis,
            int radius,
            double sigmaPixels,
            double innerRadiusPixels,
            double outerRadiusPixels,
            double measuredHfrPixels,
            double analyticHfrPixels,
            float[][] phases,
            double[] radialLut,
            double radialStepPixels) {
            PhasesPerAxis = phasesPerAxis;
            Radius = radius;
            Size = 2 * radius + 1;
            SigmaPixels = sigmaPixels;
            InnerRadiusPixels = innerRadiusPixels;
            OuterRadiusPixels = outerRadiusPixels;
            MeasuredHfrPixels = measuredHfrPixels;
            AnalyticHfrPixels = analyticHfrPixels;
            this.phases = phases;
            this.radialLut = radialLut;
            this.radialStepPixels = radialStepPixels;
        }

        /// <summary>Number of sub-pixel phases per axis, S (there are S² phase kernels).</summary>
        public int PhasesPerAxis { get; }

        /// <summary>Kernel support radius R, in pixels. Each phase kernel is (2R+1)×(2R+1).</summary>
        public int Radius { get; }

        /// <summary>Edge length of each phase kernel, 2R+1.</summary>
        public int Size { get; }

        /// <summary>The combined in-focus σ (px) this PSF was built from.</summary>
        public double SigmaPixels { get; }

        /// <summary>Geometric donut inner radius r_in (px). 0 with no obstruction or at focus.</summary>
        public double InnerRadiusPixels { get; }

        /// <summary>Geometric donut outer radius r_out (px). 0 at focus.</summary>
        public double OuterRadiusPixels { get; }

        /// <summary>Flux-weighted mean radius (HFR, px) measured on the oversampled raster of this kernel.</summary>
        public double MeasuredHfrPixels { get; }

        /// <summary>Rice-distribution closed-form HFR (px) of the annulus⊛Gaussian model — the validation target.</summary>
        public double AnalyticHfrPixels { get; }

        /// <summary>
        /// The normalized (Σ=1) phase kernel for sub-pixel phase (phaseX, phaseY), row-major, Size×Size.
        /// <b>The returned array is this kernel's shared internal buffer — treat it as read-only and never
        /// mutate it.</b> Phase 4 caches and shares <see cref="PsfKernel"/> instances across many stars, so an
        /// in-place edit would corrupt every subsequent stamp.
        /// </summary>
        public float[] GetPhaseKernel(int phaseX, int phaseY) {
            if (phaseX < 0 || phaseX >= PhasesPerAxis) throw new ArgumentOutOfRangeException(nameof(phaseX));
            if (phaseY < 0 || phaseY >= PhasesPerAxis) throw new ArgumentOutOfRangeException(nameof(phaseY));
            return phases[phaseY * PhasesPerAxis + phaseX];
        }

        /// <summary>
        /// The continuous radial intensity profile I(r) of this PSF (px argument), by linear interpolation of
        /// the LUT it was built from. Returns 0 beyond the rasterized support. Exposed for donut-geometry tests.
        /// </summary>
        public double RadialIntensity(double radiusPixels) => InterpolateLut(radialLut, radialStepPixels, radiusPixels);

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
