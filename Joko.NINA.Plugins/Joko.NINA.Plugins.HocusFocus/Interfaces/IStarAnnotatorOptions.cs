using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Joko.NINA.Plugins.HocusFocus.Interfaces {
    public enum StarBoundsTypeEnum {
        Ellipse,
        Box
    }

    public interface IStarAnnotatorOptions {
        bool ShowAllStars { get; set; }
        int MaxStars { get; set; }
        bool ShowStarBounds { get; set; }
        StarBoundsTypeEnum StarBoundsType { get; set; }
        Color StarBoundsColor { get; set; }
        bool ShowHFR { get; set; }
        Color HFRColor { get; set; }
        FontFamily TextFontFamily { get; set; }
        float TextFontSizePoints { get; set; }
        bool ShowROI { get; set; }
        Color ROIColor { get; set; }
        bool ShowStarCenter { get; set; }
        Color StarCenterColor { get; set; }
    }
}
