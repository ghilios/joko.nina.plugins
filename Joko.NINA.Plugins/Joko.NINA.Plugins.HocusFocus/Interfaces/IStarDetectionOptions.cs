using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.HocusFocus.Interfaces {
    public interface IStarDetectionOptions {
        bool HotpixelFiltering { get; set; }
        int NoiseReductionRadius { get; set; }
        double NoiseClippingMultiplier { get; set; }
        int StructureLayers { get; set; }
        double BrightnessSensitivity { get; set; }
        double StarPeakResponse { get; set; }
        double MaxDistortion { get; set; }
        double BarycenterStretchMultiplier { get; set; }
        int StarBackgroundBoxExpansion { get; set; }
        int MinStarBoundingBoxSize { get; set; }
        double MinHFR { get; set; }
        int StructureDilationSize { get; set; }
        int StructureDilationCount { get; set; }
    }
}
