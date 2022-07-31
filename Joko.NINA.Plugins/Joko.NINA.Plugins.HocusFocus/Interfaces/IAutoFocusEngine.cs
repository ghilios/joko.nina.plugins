#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel.AutoFocus;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using DrawingSize = System.Drawing.Size;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public interface IAutoFocusEngineFactory {

        IAutoFocusEngine Create();
    }

    public enum AutoFocusType {
        Default,
        Registered
    }

    public class SavedAutoFocusAttempt {
        public int Attempt { get; set; }
        public List<SavedAutoFocusImage> SavedImages { get; set; }
    }

    public class SavedAutoFocusImage {
        public string Path { get; set; }
        public int ImageNumber { get; set; }
        public int FrameNumber { get; set; }
        public int FocuserPosition { get; set; }
        public int BitDepth { get; set; }
        public bool IsBayered { get; set; }
    }

    public class AutoFocusEngineOptions {
        public bool DebayerImage { get; set; }
        public int NumberOfAFStars { get; set; }
        public int TotalNumberOfAttempts { get; set; }
        public bool ValidateHfrImprovement { get; set; }
        public AFMethodEnum AutoFocusMethod { get; set; }
        public AFCurveFittingEnum AutoFocusCurveFitting { get; set; }
        public int AutoFocusInitialOffsetSteps { get; set; }
        public int AutoFocusStepSize { get; set; }
        public int FramesPerPoint { get; set; }
        public int MaxConcurrent { get; set; }
        public TimeSpan OverrideAutoFocusExposureTime { get; set; } = TimeSpan.Zero;
        public bool Save { get; set; }
        public string SavePath { get; set; }
        public TimeSpan AutoFocusTimeout { get; set; }
        public double HFRImprovementThreshold { get; set; }
        public int FocuserOffset { get; set; }
        public bool AllowHyperbolaRotation { get; set; }
        public bool RegisterStars { get; set; }
    }

    public interface IAutoFocusEngine {
        bool AutoFocusInProgress { get; }

        Task<AutoFocusResult> Run(AutoFocusEngineOptions options, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress);

        Task<AutoFocusResult> RunWithRegions(AutoFocusEngineOptions options, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress);

        Task<AutoFocusResult> Rerun(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress);

        Task<AutoFocusResult> RerunWithRegions(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress);

        AutoFocusEngineOptions GetOptions();

        Task<FilterInfo> SetAutofocusFilter(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress);

        SavedAutoFocusAttempt LoadSavedAutoFocusAttempt(string path);

        SavedAutoFocusAttempt LoadSavedFinalAttempt(string path);

        event EventHandler<AutoFocusInitialHFRCalculatedEventArgs> InitialHFRCalculated;

        event EventHandler<AutoFocusFailedEventArgs> IterationFailed;

        event EventHandler<AutoFocusIterationStartedEventArgs> IterationStarted;

        event EventHandler<AutoFocusStartedEventArgs> Started;

        event EventHandler<AutoFocusMeasurementPointCompletedEventArgs> MeasurementPointCompleted;

        event EventHandler<AutoFocusSubMeasurementPointCompletedEventArgs> SubMeasurementPointCompleted;

        event EventHandler<AutoFocusCompletedEventArgs> Completed;

        event EventHandler<AutoFocusFailedEventArgs> Failed;
    }

    public class AutoFocusFitting : BaseINPC {
        private AFMethodEnum method;

        public AFMethodEnum Method {
            get => method;
            set {
                method = value;
                RaisePropertyChanged();
            }
        }

        private AFCurveFittingEnum curveFittingType;

        public AFCurveFittingEnum CurveFittingType {
            get => curveFittingType;
            set {
                curveFittingType = value;
                RaisePropertyChanged();
            }
        }

        private TrendlineFitting trendlineFitting = new TrendlineFitting();

        public TrendlineFitting TrendlineFitting {
            get => trendlineFitting;
            set {
                trendlineFitting = value;
                RaisePropertyChanged();
            }
        }

        private QuadraticFitting quadraticFitting;

        public QuadraticFitting QuadraticFitting {
            get => quadraticFitting;
            set {
                quadraticFitting = value;
                RaisePropertyChanged();
            }
        }

        private HyperbolicFitting hyperbolicFitting;

        public HyperbolicFitting HyperbolicFitting {
            get => hyperbolicFitting;
            set {
                hyperbolicFitting = value;
                RaisePropertyChanged();
            }
        }

        private GaussianFitting gaussianFitting;

        public GaussianFitting GaussianFitting {
            get => gaussianFitting;
            set {
                gaussianFitting = value;
                RaisePropertyChanged();
            }
        }

        public void Reset() {
            TrendlineFitting = new TrendlineFitting();
            QuadraticFitting = null;
            HyperbolicFitting = null;
            GaussianFitting = null;
        }

        public AutoFocusFitting Clone() {
            return new AutoFocusFitting() {
                Method = Method,
                CurveFittingType = CurveFittingType,
                TrendlineFitting = TrendlineFitting,
                QuadraticFitting = QuadraticFitting,
                HyperbolicFitting = HyperbolicFitting,
                GaussianFitting = GaussianFitting
            };
        }

        public double GetRSquared() {
            if (Method == AFMethodEnum.CONTRASTDETECTION) {
                return double.NaN; // Gaussian doesn't expose R^2, nor does it really make sense
            }
            if (CurveFittingType == AFCurveFittingEnum.PARABOLIC || CurveFittingType == AFCurveFittingEnum.TRENDPARABOLIC) {
                return QuadraticFitting?.RSquared ?? double.NaN;
            } else if (CurveFittingType == AFCurveFittingEnum.HYPERBOLIC || CurveFittingType == AFCurveFittingEnum.TRENDHYPERBOLIC) {
                return HyperbolicFitting?.RSquared ?? double.NaN;
            }
            return double.NaN;
        }
    }

    public class AutoFocusRegionResult {
        public int RegionIndex { get; set; }
        public StarDetectionRegion Region { get; set; }
        public AutoFocusFitting Fittings { get; set; }
        public double EstimatedFinalFocuserPosition { get; set; }
        public double EstimatedFinalHFR { get; set; }
    }

    public class AutoFocusResult {
        public bool Succeeded { get; set; }
        public int InitialFocuserPosition { get; set; }

        public DrawingSize ImageSize { get; set; }
        public AutoFocusRegionResult[] RegionResults { get; set; }
    }

    public class AutoFocusInitialHFRCalculatedEventArgs : EventArgs {
        public StarDetectionRegion Region { get; set; }
        public MeasureAndError InitialHFR { get; set; }
    }

    public class AutoFocusIterationStartedEventArgs : EventArgs {
        public int Iteration { get; set; }
    }

    public class AutoFocusStartedEventArgs : EventArgs { }

    public class MeasurementsFittings {
        public Dictionary<int, MeasureAndError> MeasurementsByFocuserPosition { get; set; }
        public AutoFocusFitting Fittings { get; set; }
    }

    public class AutoFocusMeasurementPointCompletedEventArgs : EventArgs {
        public int FocuserPosition { get; set; }
        public int RegionIndex { get; set; }
        public StarDetectionRegion Region { get; set; }
        public MeasureAndError Measurement { get; set; }
        public AutoFocusFitting Fittings { get; set; }
        public MeasurementsFittings RegisteredMeasurements { get; set; }
    }

    public class AutoFocusSubMeasurementPointCompletedEventArgs : EventArgs {
        public int FocuserPosition { get; set; }
        public int RegionIndex { get; set; }
        public StarDetectionRegion Region { get; set; }
        public StarDetectionResult StarDetectionResult { get; set; }
    }

    public class AutoFocusRegionHFR {
        public StarDetectionRegion Region { get; set; }
        public double? InitialHFR { get; set; }
        public double EstimatedFinalHFR { get; set; }
        public double? FinalHFR { get; set; }
        public double EstimatedFinalFocuserPosition { get; set; }
        public int FinalFocuserPosition { get; set; }
        public AutoFocusFitting Fittings { get; set; }
    }

    public class AutoFocusFinishedEventArgsBase : EventArgs {
        public int Iteration { get; set; }
        public int InitialFocusPosition { get; set; }
        public ImmutableList<AutoFocusRegionHFR> RegionHFRs { get; set; }
        public string Filter { get; set; }
        public double Temperature { get; set; }
        public DrawingSize ImageSize { get; set; }
        public TimeSpan Duration { get; set; }
        public string SaveFolder { get; set; }
    }

    public class AutoFocusCompletedEventArgs : AutoFocusFinishedEventArgsBase {
    }

    public class AutoFocusFailedEventArgs : AutoFocusFinishedEventArgsBase {
    }
}