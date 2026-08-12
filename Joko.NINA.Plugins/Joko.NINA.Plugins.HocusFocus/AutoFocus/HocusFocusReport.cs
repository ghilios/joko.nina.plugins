#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Utility.AutoFocus;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class HocusFocusReport : AutoFocusReport {

        [JsonProperty]
        public double FinalHFR { get; set; } = 0.0d;

        /// <summary>Standard error of the hyperbolic best-focus position, in focuser steps (NaN if unavailable).</summary>
        [JsonProperty]
        public double HyperbolicMinimumStdError { get; set; } = double.NaN;

        /// <summary>Reduced χ² of the hyperbolic fit (NaN if unavailable). See the AF panel tooltip for the weighted-fit caveat.</summary>
        [JsonProperty]
        public double HyperbolicReducedChiSquared { get; set; } = double.NaN;

        /// <summary>Leave-one-out best-focus stability, in focuser steps (NaN if unavailable / not computed).</summary>
        [JsonProperty]
        public double HyperbolicLeaveOneOutStdError { get; set; } = double.NaN;

        /// <summary>
        /// Fewest / most accepted stars found at any point the curve was actually fitted on (rejected and
        /// symmetric-window-excluded points excluded). <c>-1</c> means "not recorded" — an older report, a report
        /// written by another auto-focuser, or a contrast-detection run that counted no stars at all. It is
        /// deliberately NOT 0, which would be indistinguishable from a sweep that genuinely found nothing.
        /// </summary>
        [JsonProperty]
        public int AcceptedStarCountMin { get; set; } = -1;

        /// <inheritdoc cref="AcceptedStarCountMin"/>
        [JsonProperty]
        public int AcceptedStarCountMax { get; set; } = -1;

        /// <summary>
        /// The concrete hyperbolic model used for this run: the fixed model for a non-Hybrid run, or the model the
        /// Hybrid (Best Fit) option resolved to. Null only for non-hyperbolic runs and on older reports. Distinct
        /// from <see cref="HocusFocusAutoFocusOptions"/>.HyperbolicFitModel, which records the option as configured
        /// (e.g. "Hybrid").
        /// </summary>
        [JsonProperty]
        public HyperbolicFitModel? HyperbolicFitModelChosen { get; set; } = null;

        [JsonProperty]
        public StarDetectionRegion Region { get; set; } = StarDetectionRegion.Full;

        [JsonProperty]
        public IStarDetectionOptions HocusFocusStarDetectionOptions { get; set; } = null;

        [JsonProperty]
        public IAutoFocusOptions HocusFocusAutoFocusOptions { get; set; } = null;

        [JsonProperty]
        public IFocuserSettings FocuserOptions { get; set; } = null;

        public static HocusFocusReport GenerateReport(
            IProfileService profileService,
            IStarDetection starDetector,
            ICollection<ScatterErrorPoint> focusPoints,
            double initialFocusPosition,
            double initialHFR,
            double finalHFR,
            DataPoint focusPoint,
            AutoFocusFitting fittings,
            ReportAutoFocusPoint lastFocusPoint,
            double temperature,
            string filter,
            StarDetectionRegion region,
            IStarDetectionOptions hocusFocusStarDetectionOptions,
            IAutoFocusOptions hocusFocusAutoFocusOptions,
            TimeSpan duration,
            int acceptedStarCountMin = -1,
            int acceptedStarCountMax = -1) {
            var trendlineFitting = fittings.TrendlineFitting;
            var quadraticFitting = fittings.QuadraticFitting;
            var hyperbolicFitting = fittings.HyperbolicFitting;
            var alglibHyperbolicFitting = hyperbolicFitting as AlglibHyperbolicFitting;
            var gaussianFitting = fittings.GaussianFitting;
            var report = new HocusFocusReport() {
                Filter = filter,
                AutoFocuserName = "Hocus Focus",
                StarDetectorName = starDetector.Name,
                Timestamp = DateTime.Now,
                Temperature = temperature,
                InitialFocusPoint = new FocusPoint() {
                    Position = initialFocusPosition,
                    Value = initialHFR
                },
                CalculatedFocusPoint = new FocusPoint() {
                    Position = focusPoint.X,
                    Value = focusPoint.Y
                },
                PreviousFocusPoint = new FocusPoint() {
                    Position = lastFocusPoint?.Focuspoint.X ?? double.NaN,
                    Value = lastFocusPoint?.Focuspoint.Y ?? double.NaN
                },
                FinalHFR = finalHFR,
                HyperbolicMinimumStdError = alglibHyperbolicFitting?.MinimumStdError ?? double.NaN,
                HyperbolicReducedChiSquared = alglibHyperbolicFitting?.ReducedChiSquared ?? double.NaN,
                HyperbolicLeaveOneOutStdError = alglibHyperbolicFitting?.LeaveOneOutStdError ?? double.NaN,
                HyperbolicFitModelChosen = fittings.SelectedHyperbolicFitModel,
                AcceptedStarCountMin = acceptedStarCountMin,
                AcceptedStarCountMax = acceptedStarCountMax,
                Method = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod.ToString(),
                Fitting = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR ? profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting.ToString() : "GAUSSIAN",
                MeasurePoints = focusPoints.Select(x => new FocusPoint() { Position = x.X, Value = x.Y, Error = x.ErrorY }),
                Intersections = new Intersections() {
                    TrendLineIntersection = trendlineFitting != null ? new FocusPoint() { Position = trendlineFitting.Intersection.X, Value = trendlineFitting.Intersection.Y } : null,
                    GaussianMaximum = gaussianFitting != null ? new FocusPoint() { Position = gaussianFitting.Maximum.X, Value = gaussianFitting.Maximum.Y } : null,
                    HyperbolicMinimum = hyperbolicFitting != null ? new FocusPoint() { Position = hyperbolicFitting.Minimum.X, Value = hyperbolicFitting.Minimum.Y } : null,
                    QuadraticMinimum = quadraticFitting != null ? new FocusPoint() { Position = quadraticFitting.Minimum.X, Value = quadraticFitting.Minimum.Y } : null
                },
                Fittings = new Fittings() {
                    Gaussian = gaussianFitting?.Expression ?? "",
                    Hyperbolic = hyperbolicFitting?.Expression ?? "",
                    Quadratic = quadraticFitting?.Expression ?? "",
                    LeftTrend = trendlineFitting?.LeftExpression ?? "",
                    RightTrend = trendlineFitting?.RightExpression ?? ""
                },
                RSquares = new RSquares() {
                    Hyperbolic = hyperbolicFitting?.RSquared ?? double.NaN,
                    Quadratic = quadraticFitting?.RSquared ?? double.NaN,
                    LeftTrend = trendlineFitting?.LeftTrend?.RSquared ?? double.NaN,
                    RightTrend = trendlineFitting?.RightTrend?.RSquared ?? double.NaN
                },
                BacklashCompensation = new BacklashCompensation() {
                    BacklashCompensationModel = profileService.ActiveProfile.FocuserSettings.BacklashCompensationModel.ToString(),
                    BacklashIN = profileService.ActiveProfile.FocuserSettings.BacklashIn,
                    BacklashOUT = profileService.ActiveProfile.FocuserSettings.BacklashOut,
                },
                Region = region,
                Duration = duration,
                FocuserOptions = profileService.ActiveProfile.FocuserSettings,
                HocusFocusStarDetectionOptions = starDetector is IHocusFocusStarDetection ? hocusFocusStarDetectionOptions : null,
                HocusFocusAutoFocusOptions = hocusFocusAutoFocusOptions
            };

            return report;
        }
    }
}