﻿#region "copyright"

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
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public interface IAutoFocusEngineFactory {

        IAutoFocusEngine Create();
    }

    public interface IAutoFocusEngine {
        bool AutoFocusInProgress { get; }

        Task<AutoFocusResult> Run(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress);

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
        public MeasureAndError Measurement { get; set; }
        public AutoFocusFitting Fittings { get; set; }
    }

    public class AutoFocusCompletedEventArgs : EventArgs {
        public int Iteration { get; set; }
        public int InitialFocusPosition { get; set; }
        public int FinalFocuserPosition { get; set; }
        public double InitialHFR { get; set; }
        public double FinalHFR { get; set; }
        public string Filter { get; set; }
        public double Temperature { get; set; }
        public TimeSpan Duration { get; set; }
        public string SaveFolder { get; set; }
    }
}