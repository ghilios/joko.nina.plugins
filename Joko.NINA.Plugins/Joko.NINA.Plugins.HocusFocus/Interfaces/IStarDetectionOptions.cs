#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public interface IStarDetectionOptions {
        bool HotpixelFiltering { get; set; }
        bool UseAutoFocusCrop { get; set; }
        int NoiseReductionRadius { get; set; }
        double NoiseClippingMultiplier { get; set; }
        double StarClippingMultiplier { get; set; }
        int StructureLayers { get; set; }
        double BrightnessSensitivity { get; set; }
        double StarPeakResponse { get; set; }
        double MaxDistortion { get; set; }
        double StarCenterTolerance { get; set; }
        int StarBackgroundBoxExpansion { get; set; }
        int MinStarBoundingBoxSize { get; set; }
        double MinHFR { get; set; }
        int StructureDilationSize { get; set; }
        int StructureDilationCount { get; set; }
        double PixelSampleSize { get; set; }
        bool DebugMode { get; set; }
    }
}