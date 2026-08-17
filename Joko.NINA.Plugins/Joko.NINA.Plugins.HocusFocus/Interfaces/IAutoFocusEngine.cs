#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
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

    public class SavedAutoFocusAttempt {
        public int Attempt { get; set; }
        public List<SavedAutoFocusImage> SavedImages { get; set; }
        public int? StepSize { get; set; }
        public string FolderPath { get; set; }
    }

    public class SavedAutoFocusImage {
        public string Path { get; set; }
        public int ImageNumber { get; set; }
        public int FrameNumber { get; set; }
        public int FocuserPosition { get; set; }
        public int BitDepth { get; set; }
        public bool IsBayered { get; set; }

        /// <summary>
        /// The AF engine's saved-frame filename pattern. This is the ONLY place it is written down: the engine's
        /// own attempt loader, the wizard's Review loader, and the offline harness all parse through
        /// <see cref="TryParseFileName"/>, because <c>_BayeredN_</c> is what tells a loader whether a frame is a
        /// CFA mosaic — and a loader that gets that wrong detects on the wrong image.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex FileNameRegex = new System.Text.RegularExpressions.Regex(
            @"^(?<IMAGE_INDEX>\d+)_Frame(?<FRAME_NUMBER>\d+)_BitDepth(?<BITDEPTH>\d+)_Bayered(?<BAYERED>\d)_Focuser(?<FOCUSER>\d+)(_HFR(?<HFR>(\d+)(\.\d+)?))?$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Parses an AF-engine saved-frame path into its descriptor, or returns null when the name does not match
        /// the engine's pattern (an arbitrary image a diagnostic tool was pointed at, an annotation PNG, a result
        /// JSON). <see cref="Path"/> is set to <paramref name="path"/> verbatim; the extension is ignored.
        /// </summary>
        public static SavedAutoFocusImage TryParseFileName(string path) {
            if (string.IsNullOrEmpty(path)) {
                return null;
            }
            var match = FileNameRegex.Match(System.IO.Path.GetFileNameWithoutExtension(path) ?? string.Empty);
            if (!match.Success
                || !int.TryParse(match.Groups["IMAGE_INDEX"].Value, out var imageIndex)
                || !int.TryParse(match.Groups["FRAME_NUMBER"].Value, out var frameNumber)
                || !int.TryParse(match.Groups["FOCUSER"].Value, out var focuserPosition)
                || !int.TryParse(match.Groups["BITDEPTH"].Value, out var bitDepth)
                || !int.TryParse(match.Groups["BAYERED"].Value, out var isBayeredInt)) {
                return null;
            }
            return new SavedAutoFocusImage() {
                Path = path,
                ImageNumber = imageIndex,
                FrameNumber = frameNumber,
                FocuserPosition = focuserPosition,
                BitDepth = bitDepth,
                IsBayered = isBayeredInt != 0
            };
        }
    }

    /// <summary>
    /// Optional per-run override of where (and whether) an AutoFocus run's frames are saved, letting a caller
    /// (e.g. the Tilt Adapter Wizard) redirect saves into a specific per-step folder without touching the user's
    /// AutoFocusOptions. When applied, the engine still creates its usual timestamped attempt folder under
    /// <see cref="SavePath"/>; read it back from <see cref="AutoFocusResult.SaveFolder"/>.
    /// </summary>
    public class AutoFocusSaveOverride {
        public bool Save { get; set; }
        public string SavePath { get; set; }

        // When true, suppress the auxiliary artifacts a normal inspection run writes — the registered/annotated
        // alignment images and the star-detection intermediate files — so a saved calibration run keeps only the
        // raw frames needed for replay.
        public bool SuppressAuxiliaryFiles { get; set; }
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

        /// <summary>
        /// The filter name the two sweep-geometry values above were resolved for, or null when no per-filter
        /// lookup happened. Observability only — transient, never persisted, never in a replay snapshot. The
        /// engine compares it against the filter it actually ends up exposing through and warns on a mismatch.
        /// </summary>
        public string SweepGeometryFilterName { get; set; }
        public int FramesPerPoint { get; set; }
        public int MaxConcurrent { get; set; }
        public TimeSpan OverrideAutoFocusExposureTime { get; set; } = TimeSpan.Zero;
        public bool Save { get; set; }
        public string SavePath { get; set; }
        public TimeSpan AutoFocusTimeout { get; set; }
        public double HFRImprovementThreshold { get; set; }
        public int FocuserOffset { get; set; }
        public int MaxBlindStepsPerDirection { get; set; }
        public int MaxOutlierRejections { get; set; }
        public double OutlierRejectionConfidence { get; set; }
        public bool WeightedHyperbolicFitEnabled { get; set; }
        public HyperbolicFitModel HyperbolicFitModel { get; set; }
        public FitRejectionCriterion FitRejectionCriterion { get; set; }
        public double ReducedChiSquaredRejectionThreshold { get; set; }
        public bool PreserveExposures { get; set; }

        // When true, PSFs are modeled during this auto-focus run (auto-focus normally skips PSF fitting for speed).
        // Set by the manual AF "Review Frames" feature — only when frame review is requested AND PSF modeling is
        // enabled in the star-detection options — so the review can show PSF-derived per-star properties. Modeling
        // PSFs only adds per-star PSF data; it does not change the detected stars or HFR, so the AF curve is unaffected.
        public bool ModelPSF { get; set; }

        // When true, a saving run writes ONLY the raw exposure frames — the per-region annotated TIFFs and
        // star-detection result JSONs are skipped. Used by the Tilt Adapter Wizard so a saved calibration run is
        // a compact, replayable set of raw frames with no auxiliary artifacts.
        public bool SaveExposuresOnly { get; set; }

        // When replaying a saved auto-focus run, reuse the per-region star-detection JSON saved alongside each
        // exposure instead of re-running detection — but ONLY when the saved result's detector version and
        // params (region included) still match the current run. OFF by default and intentionally not exposed in
        // the options UI: replay's normal purpose is to re-detect, possibly with new params, so opting into reuse
        // is a programmatic/future-facing choice. With this false (always, for live AF) the engine runs detection
        // exactly as before — byte-identical behavior. Any cache miss/mismatch/read error silently falls back to
        // detection, so a stale or unreadable cache can never produce a wrong measurement.
        public bool ReuseSavedDetection { get; set; } = false;

        // When true and an imaging filter is supplied, the run exposes through EXACTLY that filter: the engine
        // moves the wheel to it and skips the designated-AF-filter substitution that UseFilterWheelOffsets
        // normally applies in SetAutofocusFilter. Set by the Star Detection Optimizer Wizard's target-filter
        // sweep (per-filter star detection). Default false = existing behavior, byte-identical.
        public bool UseExactImagingFilter { get; set; } = false;

        // When non-null, the engine builds star-detector params from THIS options snapshot (via
        // HocusFocusStarDetection.BuildStarDetectorParams) instead of the live detector's injected options — letting
        // a saved run replay with its capture-time detection settings WITHOUT mutating HocusFocusPlugin
        // .StarDetectionOptions. Null = use the detector's current options (live capture and "use current settings"
        // replay). Transient/per-call; never persisted.
        public IStarDetectionOptions StarDetectionOptionsOverride { get; set; } = null;

        // Internal-only test seam / safety valve for the always-on symmetric-window exclusion at the final fit
        // (points outside minimum ± (offsetSteps+1)*stepSize are excluded from the fit but still surfaced). Hard-
        // wired true in GetOptions; NOT a persisted IAutoFocusOptions property and NOT exposed in the UI. Tests
        // construct options with it false to assert the byte-identical no-window control.
        public bool SymmetricFocusWindowEnabled { get; set; } = true;
    }

    public interface IAutoFocusEngine {
        bool AutoFocusInProgress { get; }

        Task<AutoFocusResult> Run(AutoFocusEngineOptions options, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress);

        Task<AutoFocusResult> RunWithRegions(AutoFocusEngineOptions options, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress);

        /// <summary>
        /// Captures a fixed, non-convergent focuser sweep centered on the CURRENT focuser position (assumed to be
        /// rough focus), saving every frame to disk regardless of whether any stars are detected, then restores the
        /// focuser. Unlike <see cref="Run"/> this performs no trend-walk, no initial-HFR gate, and no curve-fit
        /// validation, so it succeeds where the current star-detection settings cannot yet build a focus curve. The
        /// returned <see cref="AutoFocusResult.SaveFolder"/> loads back through <c>LoadSavedAutoFocusAttempt</c> just
        /// like a saved run, so the Star Detection Optimizer can search for settings that DO build a good curve.
        /// </summary>
        Task<AutoFocusResult> CaptureFixedSweepAsync(AutoFocusEngineOptions options, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress);

        Task<AutoFocusResult> Rerun(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress);

        Task<AutoFocusResult> RerunWithRegions(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress);

        AutoFocusEngineOptions GetOptions(SavedAutoFocusAttempt savedAttempt = null, FilterInfo imagingFilter = null, bool useExactImagingFilter = false);

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

    public class AutoFocusFitting {
        public AFMethodEnum Method { get; set; }

        public AFCurveFittingEnum CurveFittingType { get; set; }

        public TrendlineFitting TrendlineFitting { get; set; } = new TrendlineFitting();

        public QuadraticFitting QuadraticFitting { get; set; } = null;

        public HyperbolicFitting HyperbolicFitting { get; set; } = null;

        public GaussianFitting GaussianFitting { get; set; } = null;

        /// <summary>
        /// The concrete hyperbolic model used for this run: the fixed option model for a non-Hybrid run, or — when
        /// the option is <see cref="HyperbolicFitModel.Hybrid"/> — the model selected at finalization by
        /// <see cref="StarDetection.AlglibHyperbolicFitting.SelectBestModel"/>. Null only for non-hyperbolic runs,
        /// so downstream consumers (report, UI) can tell "no hyperbolic fit" from a real model.
        /// </summary>
        public HyperbolicFitModel? SelectedHyperbolicFitModel { get; set; } = null;

        public void Reset() {
            TrendlineFitting = new TrendlineFitting();
            QuadraticFitting = null;
            HyperbolicFitting = null;
            GaussianFitting = null;
            SelectedHyperbolicFitModel = null;
        }

        public AutoFocusFitting Clone() {
            return new AutoFocusFitting() {
                Method = Method,
                CurveFittingType = CurveFittingType,
                TrendlineFitting = TrendlineFitting,
                QuadraticFitting = QuadraticFitting,
                HyperbolicFitting = HyperbolicFitting,
                GaussianFitting = GaussianFitting,
                SelectedHyperbolicFitModel = SelectedHyperbolicFitModel
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

    public class AutoFocusRegionPoint {
        public int FocuserPosition { get; set; }
        public MeasureAndError Measurement { get; set; }
    }

    public class AutoFocusRegionResult {
        public int RegionIndex { get; set; }
        public StarDetectionRegion Region { get; set; }
        public AutoFocusFitting Fittings { get; set; }
        public double EstimatedFinalFocuserPosition { get; set; }
        public double EstimatedFinalHFR { get; set; }
        public AutoFocusRegionPoint[] RejectedPoints { get; set; }
        public AutoFocusRegionPoint[] WindowExcludedPoints { get; set; }
    }

    public class AutoFocusResult {
        public bool Succeeded { get; set; }
        public int InitialFocuserPosition { get; set; }
        public int StepSize { get; set; }

        public DrawingSize ImageSize { get; set; }
        public AutoFocusRegionResult[] RegionResults { get; set; }
        public String SaveFolder { get; set; }
    }

    public class AutoFocusInitialHFRCalculatedEventArgs : EventArgs {
        public StarDetectionRegion Region { get; set; }
        public MeasureAndError InitialHFR { get; set; }
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
        public AutoFocusRegionPoint[] RejectedPoints { get; set; }
        public AutoFocusRegionPoint[] WindowExcludedPoints { get; set; }
    }

    public class AutoFocusSubMeasurementPointCompletedEventArgs : EventArgs {
        public int FocuserPosition { get; set; }
        public int RegionIndex { get; set; }
        public StarDetectionRegion Region { get; set; }
        public StarDetectionResult StarDetectionResult { get; set; }
        public IRenderedImage Image { get; set; }
    }

    public class AutoFocusRegionHFR {
        public StarDetectionRegion Region { get; set; }
        public double? InitialHFR { get; set; }
        public double EstimatedFinalHFR { get; set; }
        public double? FinalHFR { get; set; }
        public double EstimatedFinalFocuserPosition { get; set; }
        public int FinalFocuserPosition { get; set; }
        public AutoFocusFitting Fittings { get; set; }
        public AutoFocusRegionPoint[] RejectedPoints { get; set; }
        public AutoFocusRegionPoint[] WindowExcludedPoints { get; set; }
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