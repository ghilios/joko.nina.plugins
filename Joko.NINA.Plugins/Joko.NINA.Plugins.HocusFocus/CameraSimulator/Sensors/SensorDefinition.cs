#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors {

    /// <summary>
    /// Immutable datasheet-derived description of a single Sony CMOS sensor, plus the physical laws that
    /// convert its stored anchors into gain-dependent electrons-per-ADU, digital saturation, read noise,
    /// and temperature-scaled dark current.
    ///
    /// All values come from the design's sensor reference table. QE, read-noise and dark tables are
    /// per-sensor and could be surfaced as editable data later.
    /// </summary>
    public sealed class SensorDefinition {

        /// <summary>Temperature interval (°C) over which dark current doubles for these Sony CMOS sensors.</summary>
        private const double DarkCurrentDoublingCelsius = 6.5;

        /// <summary>Slider units per decade for the ZWO-style gain law: g_e ∝ 10^(−g/200) (0.1 dB units).</summary>
        private const double GainLawDbUnitDivisor = 200.0;

        public SensorDefinition(
            SonySensorModel model,
            string sensorName,
            int width,
            int height,
            double pixelSizeMicrons,
            int bitDepth,
            double fullWellElectrons,
            QeCurve qeCurve,
            double readNoiseGain0Electrons,
            int highConversionGainThreshold,
            double readNoiseHcgElectrons,
            double readNoiseMinElectrons,
            int maxGain,
            double darkCurrentRefElectronsPerPixelPerSecond,
            double darkReferenceTemperatureCelsius) {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (pixelSizeMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSizeMicrons));
            if (bitDepth <= 0 || bitDepth > 16) throw new ArgumentOutOfRangeException(nameof(bitDepth));
            if (fullWellElectrons <= 0) throw new ArgumentOutOfRangeException(nameof(fullWellElectrons));
            if (qeCurve == null) throw new ArgumentNullException(nameof(qeCurve));
            if (highConversionGainThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(highConversionGainThreshold));
            if (maxGain <= highConversionGainThreshold) throw new ArgumentOutOfRangeException(nameof(maxGain));
            if (!(readNoiseMinElectrons < readNoiseHcgElectrons && readNoiseHcgElectrons < readNoiseGain0Electrons)) {
                throw new ArgumentException("Read noise must satisfy RN_min < RN_HCG < RN_gain0 for the monotone HCG-step model.");
            }

            Model = model;
            SensorName = sensorName ?? throw new ArgumentNullException(nameof(sensorName));
            Width = width;
            Height = height;
            PixelSizeMicrons = pixelSizeMicrons;
            BitDepth = bitDepth;
            FullWellElectrons = fullWellElectrons;
            QeCurve = qeCurve;
            ReadNoiseGain0Electrons = readNoiseGain0Electrons;
            HighConversionGainThreshold = highConversionGainThreshold;
            ReadNoiseHcgElectrons = readNoiseHcgElectrons;
            ReadNoiseMinElectrons = readNoiseMinElectrons;
            MaxGain = maxGain;
            DarkCurrentRefElectronsPerPixelPerSecond = darkCurrentRefElectronsPerPixelPerSecond;
            DarkReferenceTemperatureCelsius = darkReferenceTemperatureCelsius;
        }

        public SonySensorModel Model { get; }

        /// <summary>Friendly name, e.g. "IMX455".</summary>
        public string SensorName { get; }

        public int Width { get; }
        public int Height { get; }
        public double PixelSizeMicrons { get; }
        public int BitDepth { get; }
        public double FullWellElectrons { get; }
        public QeCurve QeCurve { get; }

        /// <summary>Read noise (e⁻) at gain 0 (low-conversion-gain floor).</summary>
        public double ReadNoiseGain0Electrons { get; }

        /// <summary>The 0.1 dB-unit gain at which dual conversion gain switches to the high-conversion-gain (HCG) mode.</summary>
        public int HighConversionGainThreshold { get; }

        /// <summary>Read noise (e⁻) just after the HCG step, at <see cref="HighConversionGainThreshold"/>.</summary>
        public double ReadNoiseHcgElectrons { get; }

        /// <summary>Minimum read noise (e⁻) at <see cref="MaxGain"/>.</summary>
        public double ReadNoiseMinElectrons { get; }

        /// <summary>Maximum usable 0.1 dB-unit gain for the slider.</summary>
        public int MaxGain { get; }

        /// <summary>Reference dark current (e⁻/px/s) at <see cref="DarkReferenceTemperatureCelsius"/>.</summary>
        public double DarkCurrentRefElectronsPerPixelPerSecond { get; }

        /// <summary>Sensor temperature (°C) at which <see cref="DarkCurrentRefElectronsPerPixelPerSecond"/> is quoted.</summary>
        public double DarkReferenceTemperatureCelsius { get; }

        /// <summary>The number of distinct digital levels, 2^bits.</summary>
        public double DigitalLevels => Math.Pow(2.0, BitDepth);

        /// <summary>The maximum digital number, 2^bits − 1.</summary>
        public int MaxAdu => (1 << BitDepth) - 1;

        /// <summary>
        /// Conversion gain in e⁻/ADU at the given 0.1 dB-unit gain (ZWO-style slider):
        /// <c>g_e(g) = (FullWell / 2^bits) · 10^(−g/200)</c>. At g=0 this equals the datasheet
        /// gain-0 e⁻/ADU (e.g. IMX455: 50000/65536 = 0.763).
        /// </summary>
        public double ElectronsPerAduAtGain(int gain) {
            return (FullWellElectrons / DigitalLevels) * Math.Pow(10.0, -gain / GainLawDbUnitDivisor);
        }

        /// <summary>
        /// Digital saturation in electrons: <c>N_sat(g) = min(FullWell, g_e(g)·(2^bits − 1))</c>.
        /// At high gain the digital clip is tighter than the analog well.
        /// </summary>
        public double DigitalSaturationElectronsAtGain(int gain) {
            return Math.Min(FullWellElectrons, ElectronsPerAduAtGain(gain) * MaxAdu);
        }

        /// <summary>
        /// Read noise (e⁻) at the given 0.1 dB-unit gain. Monotone-decreasing piecewise-linear model with a
        /// discontinuous step down at the HCG threshold (dual conversion gain):
        /// <list type="bullet">
        /// <item>Low-conversion-gain segment [0, threshold): linearly from RN(gain0) toward RN(HCG) across the
        /// full [0, maxGain] span — so it is still above RN(HCG) at the threshold, producing the step.</item>
        /// <item>At/above the threshold: linearly from RN(HCG) at the threshold to RN(min) at maxGain.</item>
        /// </list>
        /// The exact between-anchor shape is under-specified by the design; this monotone model reproduces the
        /// three datasheet points (gain0 / HCG@threshold / min) with the required HCG step.
        /// </summary>
        public double ReadNoiseElectronsAtGain(int gain) {
            var g = Math.Clamp(gain, 0, MaxGain);
            if (g < HighConversionGainThreshold) {
                var frac = (double)g / MaxGain;
                return ReadNoiseGain0Electrons + (ReadNoiseHcgElectrons - ReadNoiseGain0Electrons) * frac;
            } else {
                var frac = (double)(g - HighConversionGainThreshold) / (MaxGain - HighConversionGainThreshold);
                return ReadNoiseHcgElectrons + (ReadNoiseMinElectrons - ReadNoiseHcgElectrons) * frac;
            }
        }

        /// <summary>
        /// Dark current (e⁻/px/s) at the given sensor temperature, using the 6.5 °C-doubling law for these
        /// CMOS sensors: <c>I_dark(T) = I_ref · 2^((T − T_ref)/6.5)</c>.
        /// </summary>
        public double DarkElectronsPerPixelPerSecondAtTemperature(double celsius) {
            return DarkCurrentRefElectronsPerPixelPerSecond
                * Math.Pow(2.0, (celsius - DarkReferenceTemperatureCelsius) / DarkCurrentDoublingCelsius);
        }
    }
}
