#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Converters;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum InterpolationAlgoEnum {

        [Description("Hierarchical")]
        Hierarchical,

        [Description("Thin Plate Spline")]
        ThinPlateSpline,

        [Description("Multi Quadric")]
        MultiQuadric,

        [Description("Bi-Harmonic")]
        BiHarmonic
    }

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum InterpolationAmountEnum {

        [Description("Small")]
        Small,

        [Description("Medium")]
        Medium,

        [Description("Large")]
        Large
    }

    public interface IInspectorOptions : INotifyPropertyChanged {
        int StepCount { get; set; }
        int StepSize { get; set; }
        int SignalAmplification { get; set; }
        bool CenterFocuserBeforeRun { get; set; }
        int FramesPerPoint { get; set; }
        int TimeoutSeconds { get; set; }
        int NumRegionsWide { get; set; }
        double SimpleExposureSeconds { get; set; }
        double DetailedAnalysisExposureSeconds { get; set; }
        bool LoopingExposureAnalysisEnabled { get; set; }
        /// <summary>
        /// User OVERRIDE for the focuser step size, in µm of focuser travel per step. Non-positive (the −1
        /// sentinel) means "not overridden" — read <see cref="EffectiveMicronsPerFocuserStep"/> for the value
        /// anything actually computes with, which falls back to
        /// <see cref="DriverMicronsPerFocuserStep"/>. Persisted per profile.
        /// </summary>
        double MicronsPerFocuserStep { get; set; }

        /// <summary>
        /// The focuser step size the connected driver reports (ASCOM <c>Focuser.StepSize</c>, surfaced by NINA
        /// as <c>FocuserInfo.StepSize</c>), in µm/step.
        ///
        /// <para>NOT AN OPTION and NEVER PERSISTED — it is live device state, re-read on every connect.
        /// Persisting it would create a second copy that goes stale against a swapped focuser, which is the
        /// exact failure the camera simulator's own step size was collapsed into
        /// <see cref="MicronsPerFocuserStep"/> to eliminate.</para>
        ///
        /// <para>Written by exactly one place — <c>InspectorVM.UpdateDeviceInfo(FocuserInfo)</c>, the plugin's
        /// registered focuser consumer. The setter accepts ONLY finite, positive values and silently ignores
        /// everything else, which is what makes this sticky: a disconnect reports 0, that write is dropped,
        /// and the last known value stands. So a focuser disconnecting mid-session cannot rescale an in-flight
        /// analysis. Cleared on profile change (a profile swap means a different rig).</para>
        ///
        /// <para>Accepted cost of stickiness: swapping to a focuser that reports nothing leaves the previous
        /// focuser's value in place for the rest of the session.</para>
        /// </summary>
        double DriverMicronsPerFocuserStep { get; set; }

        /// <summary>
        /// The focuser step size (µm/step) every consumer should compute with:
        /// <see cref="MicronsPerFocuserStep"/> when overridden, else <see cref="DriverMicronsPerFocuserStep"/>
        /// when the driver reports a usable one, else −1 ("unknown", the same graceful degradation as before —
        /// micron readouts go NaN and the rows that need them drop out).
        ///
        /// <para>Callers replaying a saved run layer their own captured step size on top of this; see
        /// <c>InspectorVM.SensorModelFocuserSizeOverrideMicrons</c>.</para>
        /// </summary>
        double EffectiveMicronsPerFocuserStep { get; }

        /// <summary>
        /// True when an override is set, the driver reports a usable value, and the two disagree by more than
        /// <c>InspectorOptions.FocuserStepSizeMismatchFraction</c> relative to the driver's.
        ///
        /// <para>Advisory only: it changes no value and blocks nothing, and the override still wins. Its
        /// purpose is that ASCOM <c>StepSize</c> is optional and a minority of drivers report a *plausible*
        /// wrong number — commonly 1, meaning "one step per step" rather than one micron per step — which is
        /// invisible without a second opinion.</para>
        /// </summary>
        bool HasFocuserStepSizeMismatch { get; }

        /// <summary>
        /// The focuser's direction convention <c>k</c>: false (default) = standard, increasing focuser
        /// position moves the camera AWAY from the objective (<c>sign(k) = +1</c>); true = reversed
        /// (<c>sign(k) = −1</c>).
        ///
        /// DISPLAY-ONLY BY CONTRACT. It is consumed exclusively by the direction captions, the 3D/contour
        /// "Telescope/Sensor" labels, the motion arrows, and the mechanical wording of the tilt guidance and
        /// wizard — the sites enumerated in docs/focuser-direction-convention-design.md §3, and nothing else.
        /// It must NEVER be passed into the TiltAdapterWizard math layer (TiltCalibrationCalculator,
        /// TiltScrewGeometry's computing functions, TiltScrewTargets), the Automatic Adjustment planner's
        /// target computation, or the camera simulator: the correction math needs only the measured σ, out of
        /// which the focuser convention provably cancels (§1(c)).
        ///
        /// The guarantee this buys: a wrong <c>k</c> produces wrong LABELS and never wrong MOTION — a bounded,
        /// visible failure instead of a silent inverted one. Two bounded exceptions are sanctioned and must
        /// not be widened: the wizard's direction combo, which writes only the ASSUMED σ (§2.3), and
        /// TiltScrewGeometry.PhysicalToStoredAngle via Manual Calibration Entry (§7.4).
        ///
        /// It lives here rather than on ITiltAdapterOptions deliberately — it is a focuser/rig property like
        /// <see cref="MicronsPerFocuserStep"/>, and keeping it away from σ is what stops the two from being
        /// fused into one opaque bit again (§2.2).
        /// </summary>
        bool FocuserIncreasesTowardObjective { get; set; }
        bool EccentricityColorMapEnabled { get; set; }
        bool MouseOnChartsEnabled { get; set; }
        bool SensorCurveModelEnabled { get; set; }
        bool ShowSensorModel { get; set; }
        double SensorROI { get; set; }
        double CornersROI { get; set; }
        bool InterpolationEnabled { get; }
        InterpolationAlgoEnum InterpolationAlgo { get; set; }
        InterpolationAmountEnum InterpolationAmount { get; set; }
        bool FixedSensorCenter { get; set; }
        bool UseRANSAC { get; set; }
        bool UseAffineAlignment { get; set; }
        bool AstigmaticCurvatureEnabled { get; set; }
        bool RejectBadBrightnessMatches { get; set; }
        bool RejectBadlyFittingMatches { get; set; }
        double PreviousRunBrightnessDiff { get; set; }
        double StartingBrightnessDiff { get; set; }
        bool SaveImagesOnReruns { get; set; }
        bool SaveAlignmentImages { get; set; }
        bool FrameReviewEnabled { get; set; }
        int MaxStarsPerRegion { get; set; }
        double AcceptableRSquaredMin { get; set; }
    }
}