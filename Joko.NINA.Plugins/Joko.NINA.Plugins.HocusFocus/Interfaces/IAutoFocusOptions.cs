using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.HocusFocus.Interfaces {
    public interface IAutoFocusOptions {
        int MaxConcurrent { get; set; }
        bool FastFocusModeEnabled { get; set; }
        int FastStepSize { get; set; }
        int FastOffsetSteps { get; set; }
        int FastThreshold_Seconds { get; set; }
        int FastThreshold_Celcius { get; set; }
        int FastThreshold_FocuserPosition { get; set; }
        bool ValidateHfrImprovement { get; set; }
        double HFRImprovementThreshold { get; set; }
        int AutoFocusTimeoutSeconds { get; set; }
    }
}
