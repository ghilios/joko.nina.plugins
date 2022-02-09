#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Enum;
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

        public static HocusFocusReport GenerateReport(
            IProfileService profileService,
            ICollection<ScatterErrorPoint> FocusPoints,
            double initialFocusPosition,
            double initialHFR,
            double finalHFR,
            DataPoint focusPoint,
            ReportAutoFocusPoint lastFocusPoint,
            TrendlineFitting trendlineFitting,
            QuadraticFitting quadraticFitting,
            HyperbolicFitting hyperbolicFitting,
            GaussianFitting gaussianFitting,
            double temperature,
            string filter,
            TimeSpan duration) {
            var report = new HocusFocusReport() {
                Filter = filter,
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
                Method = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod.ToString(),
                Fitting = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR ? profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting.ToString() : "GAUSSIAN",
                MeasurePoints = FocusPoints.Select(x => new FocusPoint() { Position = x.X, Value = x.Y, Error = x.ErrorY }),
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
                Duration = duration
            };

            return report;
        }
    }
}