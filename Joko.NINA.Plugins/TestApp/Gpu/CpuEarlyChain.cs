#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Gpu;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TestApp.Gpu {

    /// <summary>
    /// CPU oracle for the EARLY span, calling the exact production functions in the exact
    /// BuildDetectionContextInternal order (StarDetector.cs:520-652) so bench-gpu compares the GPU chain
    /// against precisely what the detector computes. Sequential where the detector overlaps the K-σ tasks
    /// (same values, slightly different wall attribution — reported as its own stage).
    /// </summary>
    public static class CpuEarlyChain {
        private const float AdaptiveBinarizationSigmaFloor = 1e-6f; // mirrors StarDetector's private const

        public static GpuEarlyResult Run(Mat srcImage, GpuEarlyParams p, bool hotpixelAlreadyApplied, bool collectTimings) {
            var result = new GpuEarlyResult { StageMs = collectTimings ? new Dictionary<string, double>() : null };
            var sw = Stopwatch.StartNew();
            void Record(string stage) {
                if (result.StageMs != null) {
                    result.StageMs[stage] = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                }
            }

            var meas = srcImage.Clone();
            Record("H2D"); // clone stands in for the upload the GPU pays

            long hotpixelCount = 0;
            bool measurementMutated = false;
            bool hotpixelFilteringApplied = hotpixelAlreadyApplied;
            if (p.HotpixelFiltering || (p.NoiseReductionRadius > 0 && p.StarMeasurementNoiseReductionEnabled)) {
                if (!hotpixelAlreadyApplied) {
                    hotpixelCount = ApplyHotpixelFilter(meas, p);
                    measurementMutated = true;
                }
                hotpixelFilteringApplied = true;
            }
            bool noiseReductionApplied = false;
            if (p.NoiseReductionRadius > 0 && p.StarMeasurementNoiseReductionEnabled) {
                CvImageUtility.ConvolveGaussian(meas, meas, p.NoiseReductionRadius * 2 + 1);
                noiseReductionApplied = true;
                measurementMutated = true;
            }
            Record("SrcImagePreparation");

            var noiseReduced = new Mat();
            if (hotpixelFilteringApplied || noiseReductionApplied || p.NoiseReductionRadius <= 0) {
                meas.CopyTo(noiseReduced);
            } else {
                meas.CopyTo(noiseReduced);
                hotpixelCount = ApplyHotpixelFilter(noiseReduced, p);
            }
            if (p.NoiseReductionRadius > 0 && !noiseReductionApplied) {
                CvImageUtility.ConvolveGaussian(noiseReduced, noiseReduced, p.NoiseReductionRadius * 2 + 1);
            }
            var structure = new Mat();
            noiseReduced.CopyTo(structure);
            Record("StructureMapPreparation");

            result.StructureNoise = CvImageUtility.KappaSigmaNoiseEstimate(noiseReduced, clippingMultipler: p.NoiseClippingMultiplier);
            var measurementImageDiffers = p.NoiseReductionRadius > 0 && !noiseReductionApplied;
            result.MeasurementNoise = measurementImageDiffers
                ? CvImageUtility.KappaSigmaNoiseEstimate(meas, clippingMultipler: p.NoiseClippingMultiplier)
                : result.StructureNoise;
            Record("KSigma");

            AtrousWaveletFast.ComputeResidualAndSubtractInPlace(structure, p.EffectiveStructureLayers);
            Record("WaveletCalculation");

            CvImageUtility.ConvolveGaussian(structure, structure, p.StructureLayers * 2 + 1);
            Record("PostWaveletConvolution");

            var stats = CvImageUtility.CalculateStatistics_Histogram(structure, useLogHistogram: false, flags: CvImageStatisticsFlags.Median);
            result.StructureMapMedian = stats.Median;
            Record("BinarizationStatistics");
            Record("D2H"); // no transfer on CPU; keeps the stage tables aligned

            if (p.LocallyAdaptiveBinarization) {
                result.SigmaGrid = CvImageUtility.ComputeLocalBackgroundGrid(noiseReduced, Math.Max(1, p.AdaptiveNoiseBlockSize), AdaptiveBinarizationSigmaFloor);
                result.AdaptiveMedianGrid = CvImageUtility.ComputeLocalBackgroundGrid(structure, Math.Max(1, p.AdaptiveNoiseBlockSize), AdaptiveBinarizationSigmaFloor);
            }
            Record("CpuGrids");

            result.StructureMap = structure;
            result.MeasurementImage = measurementMutated ? meas : null;
            if (!measurementMutated) {
                meas.Dispose();
            }
            noiseReduced.Dispose();
            result.HotpixelCount = hotpixelCount;
            return result;
        }

        private static long ApplyHotpixelFilter(Mat img, GpuEarlyParams p) {
            if (p.HotpixelThresholdingEnabled) {
                return HotpixelFiltering.HotpixelFilterWithThresholding(img, p.HotpixelThreshold);
            }
            HotpixelFiltering.HotpixelFilter(img);
            return 0L;
        }
    }
}
