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
    /// The shared photon → electron radiometry stage. Turns a star magnitude, the sky surface brightness,
    /// and the sensor dark current into <b>electrons</b>, using the same collecting-area / plate-scale
    /// geometry so every mean (and, later, its variance) is computed from one set of constants.
    ///
    /// All formulas are V-referenced, gray-star approximation (design §Radiometry):
    /// <list type="bullet">
    /// <item><c>Ne_star  = F0 · 10^(−0.4·m) · A · Δλ · QE(λc) · T · t</c>          [e⁻ total for the star]</item>
    /// <item><c>Ne_sky   = F0 · 10^(−0.4·μ_sky) · A · Δλ · QE(λc) · T · t · Ω_px</c> [e⁻/px]</item>
    /// <item><c>Ne_dark  = I_dark(T_sensor) · t</c>                                 [e⁻/px]</item>
    /// </list>
    /// with net collecting area <c>A = (π/4)·D²·(1 − ε²)</c> cm² (<b>D in cm</b>), plate scale
    /// <c>arcsec/px = (p/(1000·f))·ArcsecPerRadian</c> (p = pixel µm, f = focal length mm; the same
    /// derivation TanProjection uses) and pixel solid angle <c>Ω_px = (arcsec/px)²</c>.
    /// </summary>
    public sealed class RadiometryCalculator {

        /// <summary>Magnitude-0 photon flux density, V-referenced (Bessell 1998): ph·s⁻¹·cm⁻²·nm⁻¹.</summary>
        public const double ZeroMagnitudeFluxPhotons = 1.00e4;

        private readonly double filterBandwidthNm;
        private readonly double quantumEfficiencyAtCenter;
        private readonly double opticalThroughput;
        private readonly double exposureSeconds;
        private readonly double skyBrightnessMagPerArcsec2;
        private readonly double darkElectronsPerPixelPerSecond;

        /// <summary>Net light-collecting area <c>A = (π/4)·D²·(1 − ε²)</c> in cm² (D in cm, ε = obstruction fraction).</summary>
        public double ApertureAreaCm2 { get; }

        /// <summary>Plate scale (arcsec per pixel), <c>(p_µm/(1000·f_mm))·ArcsecPerRadian</c> — the same derivation as TanProjection.</summary>
        public double ArcsecPerPixel { get; }

        /// <summary>Pixel solid angle <c>Ω_px = (arcsec/px)²</c> in arcsec².</summary>
        public double PixelSolidAngleArcsec2 { get; }

        /// <summary>
        /// Plain-number constructor (unit-test friendly). The caller resolves the effective obstruction
        /// fraction (0 when the obstruction is disabled) and the sensor QE at the filter center.
        /// </summary>
        /// <param name="apertureMillimeters">Clear aperture diameter D, in mm (converted internally to cm).</param>
        /// <param name="centralObstructionFraction">Central obstruction fraction ε in [0, 1); 0 if disabled.</param>
        /// <param name="focalLengthMillimeters">Focal length f, in mm.</param>
        /// <param name="pixelSizeMicrons">Pixel pitch p, in µm.</param>
        /// <param name="filterBandwidthNm">Filter bandwidth Δλ, in nm.</param>
        /// <param name="quantumEfficiencyAtCenter">Sensor QE at the filter center λc (0..1).</param>
        /// <param name="opticalThroughput">Optical throughput T (0..1).</param>
        /// <param name="exposureSeconds">Exposure length t, in seconds.</param>
        /// <param name="skyBrightnessMagPerArcsec2">Sky surface brightness μ_sky, in mag/arcsec².</param>
        /// <param name="darkElectronsPerPixelPerSecond">Temperature-resolved dark current I_dark(T_sensor), in e⁻/px/s.</param>
        public RadiometryCalculator(
            double apertureMillimeters,
            double centralObstructionFraction,
            double focalLengthMillimeters,
            double pixelSizeMicrons,
            double filterBandwidthNm,
            double quantumEfficiencyAtCenter,
            double opticalThroughput,
            double exposureSeconds,
            double skyBrightnessMagPerArcsec2,
            double darkElectronsPerPixelPerSecond) {
            // `!(x > 0)` and not `x <= 0`: NaN fails BOTH `>` and `<=`, so the old form waved NaN through and
            // produced an all-NaN frame with no error anywhere. A fresh NINA profile stores NaN for the telescope
            // focal length and ratio, so this is reachable, not theoretical.
            if (!(apertureMillimeters > 0)) throw new ArgumentOutOfRangeException(nameof(apertureMillimeters), apertureMillimeters, "must be a positive number");
            if (!(centralObstructionFraction >= 0 && centralObstructionFraction < 1)) throw new ArgumentOutOfRangeException(nameof(centralObstructionFraction), centralObstructionFraction, "must be in [0, 1)");
            if (!(focalLengthMillimeters > 0)) throw new ArgumentOutOfRangeException(nameof(focalLengthMillimeters), focalLengthMillimeters, "must be a positive number");
            if (!(pixelSizeMicrons > 0)) throw new ArgumentOutOfRangeException(nameof(pixelSizeMicrons), pixelSizeMicrons, "must be a positive number");
            if (!(filterBandwidthNm > 0)) throw new ArgumentOutOfRangeException(nameof(filterBandwidthNm), filterBandwidthNm, "must be a positive number");
            if (!(quantumEfficiencyAtCenter > 0)) throw new ArgumentOutOfRangeException(nameof(quantumEfficiencyAtCenter), quantumEfficiencyAtCenter, "must be a positive number");
            if (!(opticalThroughput > 0)) throw new ArgumentOutOfRangeException(nameof(opticalThroughput), opticalThroughput, "must be a positive number");
            if (!(exposureSeconds >= 0)) throw new ArgumentOutOfRangeException(nameof(exposureSeconds), exposureSeconds, "must be a non-negative number");
            if (!(darkElectronsPerPixelPerSecond >= 0)) throw new ArgumentOutOfRangeException(nameof(darkElectronsPerPixelPerSecond), darkElectronsPerPixelPerSecond, "must be a non-negative number");

            this.filterBandwidthNm = filterBandwidthNm;
            this.quantumEfficiencyAtCenter = quantumEfficiencyAtCenter;
            this.opticalThroughput = opticalThroughput;
            this.exposureSeconds = exposureSeconds;
            this.skyBrightnessMagPerArcsec2 = skyBrightnessMagPerArcsec2;
            this.darkElectronsPerPixelPerSecond = darkElectronsPerPixelPerSecond;

            var apertureCm = apertureMillimeters / 10.0;
            var eps = centralObstructionFraction;
            ApertureAreaCm2 = (Math.PI / 4.0) * apertureCm * apertureCm * (1.0 - eps * eps);
            // Same plate-scale derivation as TanProjection: radians/px · arcsec/radian, from one shared constant.
            ArcsecPerPixel = (pixelSizeMicrons / (1000.0 * focalLengthMillimeters)) * AstronomicalConstants.ArcsecPerRadian;
            PixelSolidAngleArcsec2 = ArcsecPerPixel * ArcsecPerPixel;
        }

        /// <summary>
        /// Total electrons collected from a star of the given V-magnitude over the whole PSF (before it is
        /// distributed across the normalized kernel): <c>F0 · 10^(−0.4·m) · A · Δλ · QE(λc) · T · t</c>.
        /// </summary>
        public double StarElectrons(double magnitude) {
            return ZeroMagnitudeFluxPhotons * Math.Pow(10.0, -0.4 * magnitude)
                * ApertureAreaCm2 * filterBandwidthNm * quantumEfficiencyAtCenter * opticalThroughput * exposureSeconds;
        }

        /// <summary>
        /// Sky-background electrons per pixel: <c>F0 · 10^(−0.4·μ_sky) · A · Δλ · QE(λc) · T · t · Ω_px</c>.
        /// The narrowband ∝ Δλ falloff (long NB subs tolerate dark sky) falls out of the Δλ factor.
        /// </summary>
        public double SkyElectronsPerPixel() {
            return ZeroMagnitudeFluxPhotons * Math.Pow(10.0, -0.4 * skyBrightnessMagPerArcsec2)
                * ApertureAreaCm2 * filterBandwidthNm * quantumEfficiencyAtCenter * opticalThroughput * exposureSeconds
                * PixelSolidAngleArcsec2;
        }

        /// <summary>Dark-current electrons per pixel: <c>I_dark(T_sensor) · t</c>.</summary>
        public double DarkElectronsPerPixel() {
            return darkElectronsPerPixelPerSecond * exposureSeconds;
        }

        /// <summary>
        /// Builds a calculator from a render request and its resolved sensor/filter. The obstruction fraction
        /// is 0 when the obstruction is disabled; QE is sampled from the sensor's curve at the filter center;
        /// dark current is temperature-resolved from the sensor at the request's sensor temperature.
        /// </summary>
        public static RadiometryCalculator FromRequest(RenderRequest request, SensorDefinition sensor, FilterDefinition filter) {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (sensor == null) throw new ArgumentNullException(nameof(sensor));
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var obstruction = request.CentralObstructionEnabled ? request.CentralObstructionFraction : 0.0;
            var qeAtCenter = sensor.QeCurve.EvaluateAt(filter.CentralWavelengthNm);
            var darkPerSecond = sensor.DarkElectronsPerPixelPerSecondAtTemperature(request.SensorTemperatureCelsius);

            return new RadiometryCalculator(
                apertureMillimeters: request.ApertureMillimeters,
                centralObstructionFraction: obstruction,
                focalLengthMillimeters: request.FocalLengthMillimeters,
                pixelSizeMicrons: sensor.PixelSizeMicrons,
                filterBandwidthNm: filter.BandwidthNm,
                quantumEfficiencyAtCenter: qeAtCenter,
                opticalThroughput: request.OpticalThroughput,
                exposureSeconds: request.ExposureSeconds,
                skyBrightnessMagPerArcsec2: request.SkyBrightnessMagPerArcsec2,
                darkElectronsPerPixelPerSecond: darkPerSecond);
        }
    }
}
