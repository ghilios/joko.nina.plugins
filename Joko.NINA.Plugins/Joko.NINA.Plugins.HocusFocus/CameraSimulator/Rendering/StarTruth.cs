#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Everything <see cref="StarFieldCompositor"/> knows, by construction, about one star it stamped into a
    /// rendered frame — the seam a synthetic-bank generator uses to build detector-independent ground truth
    /// (position, flux, HFR) without re-deriving any of it from the rasterized pixels. <see cref="StarFieldCompositor.BuildStampJobs"/>
    /// already computes every one of these quantities per star and previously discarded all but the four a
    /// <c>StampJob</c> needs (centre, kernel, flux); this record is that same per-star state, kept.
    ///
    /// One <see cref="StarTruth"/> is appended per <b>accepted</b> stamp job — including off-frame wing-spill
    /// stars whose centre falls outside the sensor but whose donut still spills onto it, since those are exactly
    /// the ones a recall audit needs to know about. Nothing is appended for a star the compositor rejected
    /// (off-frame even with the PSF margin, or non-positive flux).
    /// </summary>
    public sealed record StarTruth {

        /// <summary>
        /// Sub-pixel, full-frame, native (pre-binning) X coordinate — exactly the <c>x</c> that
        /// <see cref="TanProjection.TryProject"/> returned for this star. May fall outside [0, width) for an
        /// accepted wing-spill star.
        /// </summary>
        public double CxPixels { get; init; }

        /// <summary>
        /// Sub-pixel, full-frame, native (pre-binning) Y coordinate — exactly the <c>y</c> that
        /// <see cref="TanProjection.TryProject"/> returned for this star. May fall outside [0, height) for an
        /// accepted wing-spill star.
        /// </summary>
        public double CyPixels { get; init; }

        /// <summary>J2000 right ascension actually projected for this star, in degrees.</summary>
        public double RaDegrees { get; init; }

        /// <summary>J2000 declination actually projected for this star, in degrees.</summary>
        public double DecDegrees { get; init; }

        /// <summary>Catalog magnitude (<c>CatalogStar.Magnitude</c>), treated as V in the gray-star radiometry model.</summary>
        public double MagnitudeV { get; init; }

        /// <summary>
        /// Total electrons this star deposits over its whole PSF — <see cref="RadiometryCalculator.StarElectrons"/>
        /// at <see cref="MagnitudeV"/>. The kernel distributes this across pixels; nothing else scales it, so a
        /// consumer can reconstruct the star's total signal without integrating the rendered pixels.
        /// </summary>
        public double FluxElectrons { get; init; }

        /// <summary>
        /// Raw local defocus Δ (µm) from <see cref="AberrationSurface.LocalDefocusMicrons"/> at this star's
        /// projected pixel and the request's focuser position, <b>before</b> quantization. This is the "true"
        /// optical defocus; <see cref="QuantizedDefocusMicrons"/> is what the cached kernel actually rendered.
        /// </summary>
        public double LocalDefocusMicrons { get; init; }

        /// <summary>
        /// The defocus the cached kernel this star used was actually built from — <c>level · quantumMicrons</c>
        /// from <see cref="StarFieldCompositor.BuildStampJobs"/>'s quantization. Feed <b>this</b> (not
        /// <see cref="LocalDefocusMicrons"/>) into <see cref="DefocusModel.HfrAtDefocusMicrons"/> when
        /// cross-checking <see cref="MeasuredHfrPixels"/>: the render is exact against the quantized level, not
        /// the continuous Δ that level was rounded from.
        /// </summary>
        public double QuantizedDefocusMicrons { get; init; }

        /// <summary>Rice closed-form HFR (px) of this star's kernel — <see cref="PsfKernel.AnalyticHfrPixels"/>.</summary>
        public double AnalyticHfrPixels { get; init; }

        /// <summary>Flux-weighted HFR (px) measured on this star's kernel raster — <see cref="PsfKernel.MeasuredHfrPixels"/>.</summary>
        public double MeasuredHfrPixels { get; init; }

        /// <summary>Geometric donut outer radius (px) of this star's kernel — <see cref="PsfKernel.OuterRadiusPixels"/>.</summary>
        public double OuterRadiusPixels { get; init; }

        /// <summary>Geometric donut inner radius (px) of this star's kernel — <see cref="PsfKernel.InnerRadiusPixels"/>.</summary>
        public double InnerRadiusPixels { get; init; }

        /// <summary>
        /// Kernel support radius (px) of this star's kernel — <see cref="PsfKernel.Radius"/>. The stamped
        /// footprint is <c>(2·KernelSupportRadiusPixels + 1)²</c> pixels centred on the star.
        /// </summary>
        public int KernelSupportRadiusPixels { get; init; }

        /// <summary>
        /// Sub-pixel phase index along X, in [0, <see cref="PsfKernel.PhasesPerAxis"/>), that
        /// <see cref="StarStamper.Stamp"/> selects for this star — computed by
        /// <see cref="StarStamper.SelectPhase"/>, the same routine <see cref="StarStamper.Stamp"/> itself calls,
        /// so this can never disagree with which phase kernel actually got stamped.
        /// </summary>
        public int PhaseX { get; init; }

        /// <summary>Sub-pixel phase index along Y — see <see cref="PhaseX"/>.</summary>
        public int PhaseY { get; init; }

        /// <summary>
        /// <c>kernel.PhasePeak(PhaseX, PhaseY)</c> — the max sample of the <b>actual phase kernel this star
        /// was stamped with</b> (not <see cref="PsfKernel.MaxPeak"/>, which is the worst case over every phase
        /// and can overstate a star that landed on a less-concentrated phase). Since every phase kernel is
        /// normalized to Σ=1, this is the fraction of total flux landing in the brightest pixel, so a consumer
        /// computes this star's peak electron count as <c>FluxElectrons * KernelPeakFraction</c> — the input
        /// the SNR math needs for saturation checks and golden-tier assignment.
        /// </summary>
        public double KernelPeakFraction { get; init; }
    }
}
