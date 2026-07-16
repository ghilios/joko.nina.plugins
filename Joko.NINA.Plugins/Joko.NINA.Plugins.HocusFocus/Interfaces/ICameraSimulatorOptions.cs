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

    /// <summary>
    /// The set of Sony CMOS sensors the synthetic camera can emulate. The chosen model fixes
    /// resolution, pixel size, bit depth, full well, QE curve, read-noise/gain behavior, and dark current.
    /// </summary>
    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum SonySensorModel {

        [Description("IMX455 (ASI6200MM / QHY600M)")]
        IMX455,

        [Description("IMX571 (ASI2600MM / QHY268M)")]
        IMX571,

        [Description("IMX533 (ASI533MM)")]
        IMX533,

        [Description("IMX294 (ASI294MM)")]
        IMX294
    }

    /// <summary>
    /// The filters the synthetic camera can place in front of the sensor: broadband L/R/G/B and
    /// narrowband Hα/OIII/SII at 3 nm and 5 nm. Wider bandpass passes more light → shorter exposure for
    /// equal signal.
    /// </summary>
    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum SimulatorFilter {

        [Description("L (Luminance)")]
        L,

        [Description("R (Red)")]
        R,

        [Description("G (Green)")]
        G,

        [Description("B (Blue)")]
        B,

        [Description("Hα 5nm")]
        Ha5,

        [Description("Hα 3nm")]
        Ha3,

        [Description("OIII 5nm")]
        OIII5,

        [Description("OIII 3nm")]
        OIII3,

        [Description("SII 5nm")]
        SII5,

        [Description("SII 3nm")]
        SII3
    }

    /// <summary>
    /// Persisted options for the synthetic star-field camera. Every property here also needs a control in
    /// <c>Resources/OptionsDataTemplates.xaml</c> (project invariant). Values are read into an immutable
    /// <c>RenderRequest</c> snapshot at exposure time.
    /// </summary>
    public interface ICameraSimulatorOptions : INotifyPropertyChanged {

        // Focus
        int OptimalFocuserPosition { get; set; }
        double FocuserStepSizeMicrons { get; set; }

        // Optics
        double ApertureMillimeters { get; set; }
        double FocalLengthMillimeters { get; set; }
        bool CentralObstructionEnabled { get; set; }
        double CentralObstructionFraction { get; set; }
        double OpticalThroughput { get; set; }

        // Sensor
        SonySensorModel SensorModel { get; set; }
        int Gain { get; set; }
        int BiasPedestalAdu { get; set; }
        double SensorTemperatureCelsius { get; set; }

        // Filter
        SimulatorFilter Filter { get; set; }

        // Sky
        double SkyBrightnessMagPerArcsec2 { get; set; }
        double SeeingArcsec { get; set; }

        // Catalog
        string AstapCatalogPath { get; set; }
        double LimitingMagnitude { get; set; }

        // Frame
        double RotationDegrees { get; set; }
        int NoiseSeed { get; set; }

        // Aberrations
        bool EnableAberrations { get; set; }
        double TiltAngleDegrees { get; set; }
        double TiltAmountMicrons { get; set; }
        double BackfocusErrorMicrons { get; set; }
        double OpticalAxisOffsetXMicrons { get; set; }
        double OpticalAxisOffsetYMicrons { get; set; }

        // Simulated tilt adapter. Deliberately separate from the user's real ITiltAdapterOptions: the inspector
        // guides from the REAL calibration, so the two must be comparable but must never implicitly overwrite
        // each other. The panel surfaces a coherence badge + explicit copy commands instead.
        int SimScrewCount { get; set; }                      // 3 or 4
        double SimScrew1AngleDegrees { get; set; }           // clockwise from top, image space
        double SimScrew2AngleDegrees { get; set; }
        double SimScrew3AngleDegrees { get; set; }
        double SimScrew4AngleDegrees { get; set; }           // double.NaN when 3-screw
        int SimScrewInwardCurvatureSign { get; set; }        // +1 / -1
        TiltAdjustmentType SimAdjustmentType { get; set; }
        double SimThreadPitchMicrons { get; set; }           // axial µm per full turn
        double SimStepperStepSizeMicrons { get; set; }       // axial µm per step
        double SimScrewRadiusMillimeters { get; set; }       // screw distance from sensor center
        bool ShowSimulatorTiltAdapterPanel { get; set; }

        void ResetDefaults();
    }
}
