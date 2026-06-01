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
        /// Upper bound on reduced χ² used as a secondary rejection criterion (see <see cref="IsModelAcceptable"/>).
        /// Reduced χ² ≈ 1 means the model fits the measured best-focus positions to within their standard
        /// errors. Because the per-star σ is approximate and does not capture model error, reduced χ²
        /// routinely runs far above 1 even for good fits and its absolute scale is rig-dependent — so a high
        /// reduced χ² is only treated as a failure when the R² is also very low. This cap is therefore a
        /// fixed sanity bound rather than a tunable option.
        /// </summary>
        public const double AcceptableReducedChiSquared = 5.0;

        /// <summary>
        /// Default lower bound on R² (variance explained) below which the fit is considered statistically
        /// questionable. Only when R² falls below this AND the reduced χ² is unacceptable is the model
        /// rejected; a near-flat sensor legitimately scores a low R², so R² alone never rejects a model.
        /// </summary>
        public const double DefaultAcceptableRSquaredMin = 0.05;

        /// <summary>
        /// Minimum R² required for an individual star's curve fit to be trusted and included in the
        /// sensor model. A star whose hyperbolic fit explains less variance than this is discarded
        /// rather than contributing a noisy best-focus position to the paraboloid surface fit.
        /// </summary>
        public const double PerStarAcceptableRSquared = 0.90;

        /// <summary>
        /// True if the fit's reduced χ² is finite and within the acceptable bound. A fit with very small
        /// reduced χ² (residuals smaller than the declared σ) is still acceptable — only large values,
        /// indicating the model cannot explain the data within its uncertainties, are rejected.
        /// </summary>
        public static bool IsReducedChiSquaredAcceptable(SensorParaboloidModel fit) {
            if (fit == null) {
                return false;
            }
            var reducedChiSquared = fit.ReducedChiSquared;
            return !double.IsNaN(reducedChiSquared) && !double.IsInfinity(reducedChiSquared)
                && reducedChiSquared <= AcceptableReducedChiSquared;
        }

        /// <summary>
        /// Acceptance rule: the reduced χ² is only a rejection criterion when the R² is very low. A model is
        /// rejected only when R² &lt; <paramref name="minRSquared"/> AND its reduced χ² is unacceptable
        /// (above the fixed <see cref="AcceptableReducedChiSquared"/> cap, or non-finite). A well-fit model
        /// with an adequate R² is accepted even when its reduced χ² is very large, and a near-flat sensor
        /// (low R², good χ²) is likewise accepted.
        /// </summary>
        public static bool IsModelAcceptable(SensorParaboloidModel fit, double minRSquared) {
            if (fit == null) {
                return false;
            }
            if (fit.GoodnessOfFit < minRSquared && !IsReducedChiSquaredAcceptable(fit)) {
                return false;
            }
            return true;
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
