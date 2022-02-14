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
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel.AutoFocus;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public interface IAutoFocusEngineFactory {

        IAutoFocusEngine Create();
    }

    public class SavedAutoFocusAttempt {
        public int Attempt { get; set; }

        // public List<StarDetectionRegion> Regions { get; set; }
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
        public bool ValidateHfrImprovement { get; set; }
        public AFMethodEnum AutoFocusMethod { get; set; }
        public AFCurveFittingEnum AutoFocusCurveFitting { get; set; }
        public int AutoFocusInitialOffsetSteps { get; set; }
        public int AutoFocusStepSize { get; set; }
        public int AutoFocusNumberOfFramesPerPoint { get; set; }
        public int MaxConcurrent { get; set; }
        public bool Save { get; set; }
        public string SavePath { get; set; }
        public TimeSpan AutoFocusTimeout { get; set; }
        public double HFRImprovementThreshold { get; set; }
    }

    public interface IAutoFocusEngine {
        bool AutoFocusInProgress { get; }

        Task<AutoFocusResult> Run(AutoFocusEngineOptions options, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress);

        Task<AutoFocusResult> RunWithRegions(AutoFocusEngineOptions options, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress);

        Task<AutoFocusResult> Rerun(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress);

        AutoFocusEngineOptions GetOptions();

        event EventHandler<AutoFocusInitialHFRCalculatedEventArgs> InitialHFRCalculated;

        event EventHandler<AutoFocusIterationFailedEventArgs> IterationFailed;

        event EventHandler<AutoFocusIterationStartedEventArgs> IterationStarted;

        event EventHandler<AutoFocusStartedEventArgs> Started;

        event EventHandler<AutoFocusMeasurementPointCompletedEventArgs> MeasurementPointCompleted;

        event EventHandler<AutoFocusCompletedEventArgs> Completed;
    }

    public class AutoFocusFitting {
        public AFMethodEnum Method { get; set; }

        public AFCurveFittingEnum CurveFittingType { get; set; }

        public TrendlineFitting TrendlineFitting { get; set; } = new TrendlineFitting();

        public QuadraticFitting QuadraticFitting { get; set; } = null;

        public HyperbolicFitting HyperbolicFitting { get; set; } = null;

        public GaussianFitting GaussianFitting { get; set; } = null;

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
    }

    public class AutoFocusResult {
        public bool Succeeded { get; set; }
    }

    public class AutoFocusInitialHFRCalculatedEventArgs : EventArgs {
        public StarDetectionRegion Region { get; set; }
        public MeasureAndError InitialHFR { get; set; }
    }

    public class AutoFocusIterationFailedEventArgs : EventArgs {
        public int Iteration { get; set; }
    }

    public class AutoFocusIterationStartedEventArgs : EventArgs {
        public int Iteration { get; set; }
    }

    public class AutoFocusStartedEventArgs : EventArgs { }

    public class AutoFocusMeasurementPointCompletedEventArgs : EventArgs {
        public int FocuserPosition { get; set; }
        public int RegionIndex { get; set; }
        public StarDetectionRegion Region { get; set; }
        public MeasureAndError Measurement { get; set; }
        public AutoFocusFitting Fittings { get; set; }
    }

    public class AutoFocusRegionHFR {
        public StarDetectionRegion Region { get; set; }
        public double InitialHFR { get; set; }
        public double EstimatedFinalHFR { get; set; }
        public double? FinalHFR { get; set; }
        public double EstimatedFinalFocuserPosition { get; set; }
        public int FinalFocuserPosition { get; set; }
    }

    public class AutoFocusCompletedEventArgs : EventArgs {
        public int Iteration { get; set; }
        public int InitialFocusPosition { get; set; }
        public ImmutableList<AutoFocusRegionHFR> RegionHFRs { get; set; }
        public string Filter { get; set; }
        public double Temperature { get; set; }
        public TimeSpan Duration { get; set; }
        public string SaveFolder { get; set; }
    }
}