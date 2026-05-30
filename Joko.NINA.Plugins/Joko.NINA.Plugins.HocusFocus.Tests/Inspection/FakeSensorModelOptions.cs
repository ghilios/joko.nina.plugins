using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection {

    internal sealed class FakeInspectorOptions : IInspectorOptions {
        public event PropertyChangedEventHandler PropertyChanged;

        public int StepCount { get; set; }
        public int StepSize { get; set; }
        public int FramesPerPoint { get; set; }
        public int TimeoutSeconds { get; set; }
        public int NumRegionsWide { get; set; } = 7;
        public double SimpleExposureSeconds { get; set; }
        public double DetailedAnalysisExposureSeconds { get; set; }
        public bool LoopingExposureAnalysisEnabled { get; set; }
        public double MicronsPerFocuserStep { get; set; }
        public bool EccentricityColorMapEnabled { get; set; }
        public bool MouseOnChartsEnabled { get; set; }
        public bool SensorCurveModelEnabled { get; set; }
        public bool ShowSensorModel { get; set; }
        public double SensorROI { get; set; } = 1.0;
        public double CornersROI { get; set; } = 1.0;
        public bool InterpolationEnabled { get; set; }
        public InterpolationAlgoEnum InterpolationAlgo { get; set; } = InterpolationAlgoEnum.MultiQuadric;
        public InterpolationAmountEnum InterpolationAmount { get; set; } = InterpolationAmountEnum.Medium;
        public bool FixedSensorCenter { get; set; } = true;
        public bool UseRANSAC { get; set; }
        public bool UseAffineAlignment { get; set; }
        public bool AstigmaticCurvatureEnabled { get; set; }
        public bool RejectBadBrightnessMatches { get; set; }
        public bool RejectBadlyFittingMatches { get; set; }
        public double PreviousRunBrightnessDiff { get; set; } = 0.1;
        public double StartingBrightnessDiff { get; set; } = -1;
        public bool SaveImagesOnReruns { get; set; }
        public bool SaveAlignmentImages { get; set; }
        public int MaxStarsPerRegion { get; set; } = -1;
        public double AcceptableRSquaredMin { get; set; } = 0.05;
    }

    internal sealed class FakeAutoFocusOptions : IAutoFocusOptions {
        public event PropertyChangedEventHandler PropertyChanged;

        public int MaxConcurrent { get; set; }
        public bool FastFocusModeEnabled { get; set; }
        public int FastStepSize { get; set; }
        public int FastOffsetSteps { get; set; }
        public int FastThreshold_Seconds { get; set; }
        public int FastThreshold_Celcius { get; set; }
        public int FastThreshold_FocuserPosition { get; set; }
        public bool ValidateHfrImprovement { get; set; }
        public double HFRImprovementThreshold { get; set; }
        public int AutoFocusTimeoutSeconds { get; set; }
        public string SavePath { get; set; }
        public bool Save { get; set; }
        public string LastSelectedLoadPath { get; set; }
        public int FocuserOffset { get; set; }
        public int MaxOutlierRejections { get; set; }
        public double OutlierRejectionConfidence { get; set; } = 0.95;
        public bool UnevenHyperbolicFitEnabled { get; set; }
        public bool WeightedHyperbolicFitEnabled { get; set; }
        public HyperbolicFitModel HyperbolicFitModel { get; set; }
    }
}
