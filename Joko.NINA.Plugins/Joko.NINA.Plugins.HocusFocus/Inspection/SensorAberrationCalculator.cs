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
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Inspection {

    /// <summary>
    /// Pure helpers used by SensorModel for fit comparison, region tiling, and time-based
    /// goodness-of-fit targets. Kept dependency-free so they can be unit tested directly.
    /// </summary>
    public static class SensorAberrationCalculator {

        /// <summary>
        /// Default upper bound on reduced χ² for a paraboloid fit to be accepted (and the brightness-tolerance
        /// search to stop). Reduced χ² ≈ 1 means the model fits the measured best-focus positions to
        /// within their standard errors; values far above 1 indicate a poor fit. Unlike an R² target,
        /// this is independent of the overall tilt/curvature magnitude, so a near-flat but well-measured
        /// sensor is accepted rather than rejected for "low R²". The bound is generous to tolerate the
        /// approximate per-star uncertainties, and is user-configurable per rig via the inspector options.
        /// </summary>
        public const double DefaultAcceptableReducedChiSquared = 5.0;

        /// <summary>
        /// Default lower bound on R² (variance explained) below which the fit is considered statistically
        /// questionable — but only enough to reject when the reduced χ² is also elevated (see
        /// <see cref="IsModelAcceptable"/>). A near-flat sensor legitimately scores a low R², so R² alone
        /// never rejects a model.
        /// </summary>
        public const double DefaultAcceptableRSquaredMin = 0.05;

        /// <summary>
        /// Fraction of the acceptable reduced-χ² cap that separates a "good" fit from a "marginal" one.
        /// At the default cap of 5.0 this puts the good/marginal boundary at 3.0.
        /// </summary>
        private const double GoodReducedChiSquaredFraction = 0.6;

        /// <summary>
        /// Display band for a fit's reduced χ² relative to the configured acceptable cap.
        /// </summary>
        public enum FitQualityBand {
            Good,
            Marginal,
            Rejected
        }

        /// <summary>
        /// True if the fit's reduced χ² is finite and within the acceptable bound. A fit with very small
        /// reduced χ² (residuals smaller than the declared σ) is still acceptable — only large values,
        /// indicating the model cannot explain the data within its uncertainties, are rejected.
        /// </summary>
        public static bool IsReducedChiSquaredAcceptable(SensorParaboloidModel fit, double maxReducedChiSquared) {
            if (fit == null) {
                return false;
            }
            var reducedChiSquared = fit.ReducedChiSquared;
            return !double.IsNaN(reducedChiSquared) && !double.IsInfinity(reducedChiSquared)
                && reducedChiSquared <= maxReducedChiSquared;
        }

        /// <summary>
        /// Combined acceptance rule that keeps the magnitude-independence benefit of reduced χ² while
        /// letting a low R² matter when the fit is also statistically questionable. Reject if
        /// χ² ≥ <paramref name="maxReducedChiSquared"/>, OR if R² &lt; <paramref name="minRSquared"/> AND
        /// χ² ≥ 0.6·max. A flat but well-measured sensor (good χ², low R²) is still accepted.
        /// </summary>
        public static bool IsModelAcceptable(SensorParaboloidModel fit, double maxReducedChiSquared, double minRSquared) {
            if (fit == null) {
                return false;
            }
            if (!IsReducedChiSquaredAcceptable(fit, maxReducedChiSquared)) {
                return false;   // χ² rejected outright
            }
            var goodBand = GoodReducedChiSquaredFraction * maxReducedChiSquared;
            var chiElevated = fit.ReducedChiSquared >= goodBand;
            if (fit.GoodnessOfFit < minRSquared && chiElevated) {
                return false;   // low R² only contributes when χ² is also elevated
            }
            return true;
        }

        /// <summary>
        /// Classify a fit's reduced χ² into a display band relative to the configured acceptable cap.
        /// Good below 0.6·max, Marginal between 0.6·max and max, Rejected at/above max (or non-finite).
        /// </summary>
        public static FitQualityBand ClassifyReducedChiSquared(double reducedChiSquared, double maxReducedChiSquared) {
            if (double.IsNaN(reducedChiSquared) || double.IsInfinity(reducedChiSquared) || reducedChiSquared >= maxReducedChiSquared) {
                return FitQualityBand.Rejected;
            }
            if (reducedChiSquared < GoodReducedChiSquaredFraction * maxReducedChiSquared) {
                return FitQualityBand.Good;
            }
            return FitQualityBand.Marginal;
        }

        /// <summary>
        /// A fit with goodness-of-fit indistinguishable from 1.0 is treated as overfit /
        /// suspicious — usually means too few points contributed to the fit.
        /// </summary>
        public static bool IsFitTooGood(SensorParaboloidModel fit) {
            if (fit == null) {
                return false;
            }
            return 1 - fit.GoodnessOfFit < 0.005;
        }

        /// <summary>
        /// True if <paramref name="thisFit"/> is a strictly better fit than <paramref name="otherFit"/>:
        /// non-null, non-zero R², not overfit, and at least as good as the alternative.
        /// </summary>
        public static bool IsBetterFit(SensorParaboloidModel thisFit, SensorParaboloidModel otherFit) {
            if (thisFit == null) {
                return false;
            }
            if (otherFit == null) {
                return true;
            }

            if (otherFit.GoodnessOfFit == 0) {
                return true;
            }
            if (thisFit.GoodnessOfFit == 0) {
                return false;
            }

            if (IsFitTooGood(thisFit)) {
                return false;
            }
            if (IsFitTooGood(otherFit)) {
                return true;
            }
            return thisFit.GoodnessOfFit >= otherFit.GoodnessOfFit;
        }

        /// <summary>
        /// Tile the unit ratio square into <paramref name="rows"/> × <paramref name="cols"/> equal
        /// regions. Each region's <see cref="StarDetectionRegion.Index"/> increments in row-major
        /// order starting at 1.
        /// </summary>
        public static List<StarDetectionRegion> CreateFullRegionSet(System.Drawing.Size imageSize, int rows, int cols) {
            if (rows <= 0) {
                throw new ArgumentOutOfRangeException(nameof(rows), "rows must be positive");
            }
            if (cols <= 0) {
                throw new ArgumentOutOfRangeException(nameof(cols), "cols must be positive");
            }
            var xPart = 1.0d / cols;
            var yPart = 1.0d / rows;
            var regions = new List<StarDetectionRegion>();
            int index = 0;
            for (int r = 0; r < rows; r++) {
                for (int c = 0; c < cols; c++) {
                    regions.Add(new StarDetectionRegion(new RatioRect(c * xPart, r * yPart, xPart, yPart), ++index));
                }
            }
            return regions;
        }
    }
}
