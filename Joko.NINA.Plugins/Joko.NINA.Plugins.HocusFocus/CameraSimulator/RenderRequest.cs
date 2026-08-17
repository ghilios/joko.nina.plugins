#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.Interfaces;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator {

    /// <summary>
    /// Immutable snapshot of everything the compositor needs to render one exposure. Built once in
    /// <c>HocusFocusSimulatorCamera.StartExposure</c> from the live focuser/telescope state and the resolved
    /// option values, so nothing can change under the renderer while it runs (and so the render is a pure
    /// function of this record). The NINA profile is only <em>read</em> to build this — never mutated.
    /// </summary>
    public sealed record RenderRequest {

        // --- Focuser (drives defocus) ---

        /// <summary>Whether a focuser was connected at exposure start. When false the render is rejected.</summary>
        public bool FocuserConnected { get; init; }

        /// <summary>Live focuser step position at exposure start.</summary>
        public int FocuserPosition { get; init; }

        // --- Telescope / pointing ---

        /// <summary>Whether a telescope/mount was connected at exposure start. When false the render is rejected.</summary>
        public bool TelescopeConnected { get; init; }

        /// <summary>J2000 right ascension of the pointing, in degrees.</summary>
        public double RaDegreesJ2000 { get; init; }

        /// <summary>J2000 declination of the pointing, in degrees.</summary>
        public double DecDegreesJ2000 { get; init; }

        // --- Optics ---

        public double ApertureMillimeters { get; init; }

        /// <summary>Resolved focal length in mm (the option override, or the profile's telescope focal length when the option is 0).</summary>
        public double FocalLengthMillimeters { get; init; }

        public bool CentralObstructionEnabled { get; init; }
        public double CentralObstructionFraction { get; init; }
        public double OpticalThroughput { get; init; }

        // --- Sensor / gain ---

        public SonySensorModel SensorModel { get; init; }
        public int Gain { get; init; }
        public int BiasPedestalAdu { get; init; }
        public double SensorTemperatureCelsius { get; init; }

        // --- Filter ---

        public SimulatorFilter Filter { get; init; }

        // --- Sky / seeing ---

        public double SkyBrightnessMagPerArcsec2 { get; init; }
        public double SeeingArcsec { get; init; }

        // --- Focus model ---

        public int OptimalFocuserPosition { get; init; }
        public double FocuserStepSizeMicrons { get; init; }

        // --- Catalog ---

        public string AstapCatalogPath { get; init; }
        public double LimitingMagnitude { get; init; }

        // --- Frame ---

        public double RotationDegrees { get; init; }
        public int NoiseSeed { get; init; }

        // --- Aberrations ---

        public bool AberrationsEnabled { get; init; }
        public double TiltAngleDegrees { get; init; }
        public double TiltAmountMicrons { get; init; }
        public double BackfocusErrorMicrons { get; init; }
        public double OpticalAxisOffsetXMicrons { get; init; }
        public double OpticalAxisOffsetYMicrons { get; init; }

        /// <summary>
        /// Whether the best-focus surface splits into tangential and sagittal surfaces, giving stars an
        /// elliptical (eccentric) blur. Paired with a magnitude the way
        /// <see cref="CentralObstructionEnabled"/> is paired with <see cref="CentralObstructionFraction"/>.
        /// </summary>
        public bool AstigmatismEnabled { get; init; }

        /// <summary>
        /// Axial spacing error magnitude e_c (µm) — how far the sensor sits from the corrector's design
        /// spacing. Defaults to the "not entered" sentinel, in which case it is inferred from the backfocus
        /// error; the sign is inherited from that error, not carried here.
        /// </summary>
        public double BackfocusSpacingErrorMicrons { get; init; } = AberrationSurface.UnsetSpacingErrorMicrons;

        /// <summary>ρ = c_a/c_m, the astigmatism-to-curvature ratio. Non-negative.</summary>
        public double AstigmatismRatio { get; init; }

        // --- Exposure ---

        /// <summary>Exposure length in seconds, from the <c>CaptureSequence.ExposureTime</c>.</summary>
        public double ExposureSeconds { get; init; }
    }
}
