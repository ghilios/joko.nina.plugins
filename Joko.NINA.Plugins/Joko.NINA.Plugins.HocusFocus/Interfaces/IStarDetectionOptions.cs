using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.HocusFocus.Interfaces {
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
