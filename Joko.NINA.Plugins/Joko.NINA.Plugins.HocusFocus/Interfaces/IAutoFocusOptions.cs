using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.HocusFocus.Interfaces {
    public enum AutoFocusModeEnum {
        AutoFocusMode_Blind,
        AutoFocusMode_Fast,
        AutoFocusMode_TwoPass,
        AutoFocusMode_Auto
    }

    public interface IAutoFocusOptions {
        int MaxConcurrent { get; set; }
        AutoFocusModeEnum AutoFocusMode { get; set; }
        int FastStepSize { get; set; }
        int FastOffsetSteps { get; set; }
        int FastThreshold_Seconds { get; set; }
        int FastThreshold_Celcius { get; set; }
        int FastThreshold_FocuserPosition { get; set; }
    }
}
