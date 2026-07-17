#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// The <b>single source of truth</b> for how much a star blurs at a given defocus. Given the optics
    /// (aperture, focal length, obstruction), the sensor pixel pitch, the seeing, the observing wavelength,
    /// and the focuser step size, it produces the in-focus HFR floor and maps any defocus (in focuser steps
    /// or in microns of sensor displacement) to HFR, wavefront error W20, and geometric donut annulus radii.
    /// <see cref="PsfKernelGenerator"/> calls this so the rendered PSF and the reported HFR-vs-focuser V-curve
    /// stay consistent by construction.
    ///
    /// Formulas (design §Optics, spec §DefocusModel), with N = f/D, p = pixel µm, λ in µm:
    /// <list type="bullet">
    /// <item>FWHM_diff,px = (1.0290 − 0.5673·ε² + 0.3919·ε⁴)·λ_µm·N/p ; σ_diff = FWHM_diff,px / 2.3548</item>
    /// <item>σ_see = (seeing/2.3548)/(arcsec/px) ; σ_min = √(σ_diff²+σ_see²) ; HFR_min = 1.2533·σ_min [px]</item>
    /// <item>κ = k·(1+ε+ε²)/(3·N·p·(1+ε)) [px/step]</item>
    /// <item>HFR(steps) = √(HFR_min² + (κ·(steps−x0))²) ; range = √15·HFR_min/κ [steps]</item>
    /// <item>W20 = Δ/(8N²) [µm] (÷ λ for waves) ; r_out = |Δ|/(2N), r_in = ε·r_out [focal-plane µm]</item>
    /// </list>
    /// This model is deliberately independent of the aberration surface: <see cref="AberrationSurface"/>
    /// supplies the per-field-point defocus Δ, and this model turns that Δ into HFR / W20 / annulus radii.
    /// </summary>
    public sealed class DefocusModel {

        /// <summary>FWHM → σ for a Gaussian: 2·√(2·ln 2).</summary>
        private const double FwhmToSigma = 2.3548;

        /// <summary>σ → HFR (half-flux radius) for a Gaussian: √(π/2).</summary>
        private const double SigmaToHfr = 1.2533;

        private readonly double focuserStepSizeMicrons; // k
        private readonly double kappaPerMicron;         // κ / k  [px/µm]
        private readonly double wavelengthMicrons;      // λ (observing / filter central wavelength)

        /// <summary>Central obstruction fraction ε used by the diffraction, κ, and donut formulas (0 if disabled).</summary>
        public double CentralObstructionFraction { get; }

        /// <summary>Observing wavelength λ (filter central wavelength), in nm.</summary>
        public double WavelengthNm => wavelengthMicrons * 1000.0;

        /// <summary>Focal ratio N = f / D (dimensionless).</summary>
        public double FocalRatio { get; }

        /// <summary>Pixel pitch p, in µm.</summary>
        public double PixelSizeMicrons { get; }

        /// <summary>Plate scale (arcsec per pixel), <c>(p_µm/(1000·f_mm))·ArcsecPerRadian</c> — the same derivation as TanProjection.</summary>
        public double ArcsecPerPixel { get; }

        /// <summary>Optimal (best-focus) focuser step position x0.</summary>
        public int OptimalFocuserPosition { get; }

        /// <summary>Focuser step size k, in µm of sensor defocus per step.</summary>
        public double FocuserStepSizeMicrons => focuserStepSizeMicrons;

        /// <summary>Diffraction σ contribution, in px.</summary>
        public double SigmaDiffractionPixels { get; }

        /// <summary>Seeing σ contribution, in px.</summary>
        public double SigmaSeeingPixels { get; }

        /// <summary>Combined in-focus σ = √(σ_diff² + σ_see²), in px.</summary>
        public double SigmaMinPixels { get; }

        /// <summary>In-focus HFR floor = 1.2533·σ_min, in px.</summary>
        public double HfrMinPixels { get; }

        /// <summary>Defocus slope κ, in px per focuser step.</summary>
        public double KappaPixelsPerStep { get; }

        /// <summary>
        /// The computed "±range → 4× HFR" readout = √15·HFR_min/κ, in focuser steps. At this many steps from
        /// x0 the HFR reaches exactly 4× HFR_min (√(1+15) = 4). A derived readout, not an input.
        /// </summary>
        public double RangeReadoutSteps { get; }

        /// <summary>
        /// Plain-number constructor (unit-test friendly). The caller resolves the effective obstruction
        /// fraction (0 when the obstruction is disabled). Use the filter central wavelength for
        /// <paramref name="wavelengthNm"/> in production (the design's sanity vector uses 550 nm).
        /// </summary>
        /// <param name="apertureMillimeters">Clear aperture diameter D, in mm.</param>
        /// <param name="focalLengthMillimeters">Focal length f, in mm.</param>
        /// <param name="centralObstructionFraction">Central obstruction fraction ε in [0, 1); 0 if disabled.</param>
        /// <param name="pixelSizeMicrons">Pixel pitch p, in µm.</param>
        /// <param name="seeingArcsec">Atmospheric seeing FWHM, in arcsec.</param>
        /// <param name="wavelengthNm">Observing wavelength λ, in nm (filter central wavelength).</param>
        /// <param name="focuserStepSizeMicrons">Focuser step size k, in µm of sensor defocus per step.</param>
        /// <param name="optimalFocuserPosition">Best-focus focuser step position x0.</param>
        public DefocusModel(
            double apertureMillimeters,
            double focalLengthMillimeters,
            double centralObstructionFraction,
            double pixelSizeMicrons,
            double seeingArcsec,
            double wavelengthNm,
            double focuserStepSizeMicrons,
            int optimalFocuserPosition) {
            // `!(x > 0)` and not `x <= 0`: NaN fails BOTH `>` and `<=`, so the old form waved NaN through into
            // N = f/D and produced an all-NaN frame with no error anywhere. A fresh NINA profile stores NaN for
            // the telescope focal length and ratio, so this is reachable, not theoretical.
            if (!(apertureMillimeters > 0)) throw new ArgumentOutOfRangeException(nameof(apertureMillimeters), apertureMillimeters, "must be a positive number");
            if (!(focalLengthMillimeters > 0)) throw new ArgumentOutOfRangeException(nameof(focalLengthMillimeters), focalLengthMillimeters, "must be a positive number");
            if (!(centralObstructionFraction >= 0 && centralObstructionFraction < 1)) throw new ArgumentOutOfRangeException(nameof(centralObstructionFraction), centralObstructionFraction, "must be in [0, 1)");
            if (!(pixelSizeMicrons > 0)) throw new ArgumentOutOfRangeException(nameof(pixelSizeMicrons), pixelSizeMicrons, "must be a positive number");
            if (!(seeingArcsec >= 0)) throw new ArgumentOutOfRangeException(nameof(seeingArcsec), seeingArcsec, "must be a non-negative number");
            if (!(wavelengthNm > 0)) throw new ArgumentOutOfRangeException(nameof(wavelengthNm), wavelengthNm, "must be a positive number");
            if (!(focuserStepSizeMicrons > 0)) throw new ArgumentOutOfRangeException(nameof(focuserStepSizeMicrons), focuserStepSizeMicrons, "must be a positive number");

            var eps = centralObstructionFraction;
            var n = focalLengthMillimeters / apertureMillimeters;
            var lambdaMicrons = wavelengthNm / 1000.0;

            this.wavelengthMicrons = lambdaMicrons;
            CentralObstructionFraction = eps;
            FocalRatio = n;
            PixelSizeMicrons = pixelSizeMicrons;
            OptimalFocuserPosition = optimalFocuserPosition;
            this.focuserStepSizeMicrons = focuserStepSizeMicrons;

            // Diffraction: annular-Airy FWHM shrinks with obstruction. Angular FWHM = coeff·(λ/D); converting
            // to pixels (÷ plate scale p/f) gives coeff·λ·N/p with λ and p in the same length units.
            var diffractionCoefficient = 1.0290 - 0.5673 * eps * eps + 0.3919 * eps * eps * eps * eps;
            var fwhmDiffractionPixels = diffractionCoefficient * lambdaMicrons * n / pixelSizeMicrons;
            SigmaDiffractionPixels = fwhmDiffractionPixels / FwhmToSigma;

            // Same plate-scale derivation as TanProjection: radians/px · arcsec/radian, from one shared constant.
            ArcsecPerPixel = (pixelSizeMicrons / (1000.0 * focalLengthMillimeters)) * AstronomicalConstants.ArcsecPerRadian;
            SigmaSeeingPixels = (seeingArcsec / FwhmToSigma) / ArcsecPerPixel;

            SigmaMinPixels = Math.Sqrt(SigmaDiffractionPixels * SigmaDiffractionPixels + SigmaSeeingPixels * SigmaSeeingPixels);
            HfrMinPixels = SigmaToHfr * SigmaMinPixels;

            // Defocus slope, px per focuser step. κ/k is the px-per-micron slope reused by HfrAtDefocusMicrons.
            kappaPerMicron = (1.0 + eps + eps * eps) / (3.0 * n * pixelSizeMicrons * (1.0 + eps));
            KappaPixelsPerStep = focuserStepSizeMicrons * kappaPerMicron;

            RangeReadoutSteps = Math.Sqrt(15.0) * HfrMinPixels / KappaPixelsPerStep;
        }

        /// <summary>HFR (px) at a focuser step position: <c>√(HFR_min² + (κ·(steps−x0))²)</c>.</summary>
        public double HfrAtFocuserPosition(int steps) {
            var deltaSteps = steps - OptimalFocuserPosition;
            var defocusTerm = KappaPixelsPerStep * deltaSteps;
            return Math.Sqrt(HfrMinPixels * HfrMinPixels + defocusTerm * defocusTerm);
        }

        /// <summary>
        /// HFR (px) at a sensor defocus Δ (µm), where Δ = k·(steps − x0). Equivalent to
        /// <see cref="HfrAtFocuserPosition"/> via Δ/k, but continuous in Δ.
        /// </summary>
        public double HfrAtDefocusMicrons(double defocusMicrons) {
            var defocusTerm = kappaPerMicron * defocusMicrons;
            return Math.Sqrt(HfrMinPixels * HfrMinPixels + defocusTerm * defocusTerm);
        }

        /// <summary>Wavefront defocus W20 = Δ/(8N²), in µm, for a sensor defocus Δ (µm).</summary>
        public double W20Microns(double defocusMicrons) {
            return defocusMicrons / (8.0 * FocalRatio * FocalRatio);
        }

        /// <summary>
        /// Wavefront defocus W20 in waves (W20 µm ÷ λ µm) at the model's observing wavelength, for a sensor
        /// defocus Δ (µm). Used for PSF donut sizing.
        /// </summary>
        public double W20Waves(double defocusMicrons) {
            return W20Microns(defocusMicrons) / wavelengthMicrons;
        }

        /// <summary>Geometric donut outer radius r_out = |Δ|/(2N), in focal-plane µm, for a sensor defocus Δ (µm).</summary>
        public double OuterAnnulusRadiusMicrons(double defocusMicrons) {
            return Math.Abs(defocusMicrons) / (2.0 * FocalRatio);
        }

        /// <summary>Geometric donut inner radius r_in = ε·r_out, in focal-plane µm, for a sensor defocus Δ (µm).</summary>
        public double InnerAnnulusRadiusMicrons(double defocusMicrons) {
            return CentralObstructionFraction * OuterAnnulusRadiusMicrons(defocusMicrons);
        }

        /// <summary>Geometric donut outer radius r_out, in pixels (r_out µm ÷ p), for a sensor defocus Δ (µm).</summary>
        public double OuterAnnulusRadiusPixels(double defocusMicrons) {
            return OuterAnnulusRadiusMicrons(defocusMicrons) / PixelSizeMicrons;
        }

        /// <summary>Geometric donut inner radius r_in, in pixels (r_in µm ÷ p), for a sensor defocus Δ (µm).</summary>
        public double InnerAnnulusRadiusPixels(double defocusMicrons) {
            return InnerAnnulusRadiusMicrons(defocusMicrons) / PixelSizeMicrons;
        }

        /// <summary>
        /// Builds a model from a render request and its resolved sensor/filter. The obstruction fraction is 0
        /// when the obstruction is disabled; the observing wavelength is the filter central wavelength.
        /// </summary>
        public static DefocusModel FromRequest(RenderRequest request, SensorDefinition sensor, FilterDefinition filter) {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (sensor == null) throw new ArgumentNullException(nameof(sensor));
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var obstruction = request.CentralObstructionEnabled ? request.CentralObstructionFraction : 0.0;
            return new DefocusModel(
                apertureMillimeters: request.ApertureMillimeters,
                focalLengthMillimeters: request.FocalLengthMillimeters,
                centralObstructionFraction: obstruction,
                pixelSizeMicrons: sensor.PixelSizeMicrons,
                seeingArcsec: request.SeeingArcsec,
                wavelengthNm: filter.CentralWavelengthNm,
                focuserStepSizeMicrons: request.FocuserStepSizeMicrons,
                optimalFocuserPosition: request.OptimalFocuserPosition);
        }
    }
}
