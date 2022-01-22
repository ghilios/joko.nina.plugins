#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Converters;
using System.ComponentModel;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public enum StarBoundsTypeEnum {
        Ellipse,
        Box
    }

    public enum ShowStructureMapEnum {
        None,
        Original,
        Dilated
    }

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum ShowAnnotationTypeEnum {

        [Description("None")]
        None,

        [Description("HFR")]
        HFR,

        [Description("FWHM")]
        FWHM,

        [Description("Eccentricity")]
        Eccentricity,

        [Description("PSF Rotation")]
        PSFTheta
    }

    public interface IStarAnnotatorOptions : INotifyPropertyChanged {
        bool ShowAnnotations { get; set; }
        bool ShowAllStars { get; set; }
        int MaxStars { get; set; }
        bool ShowStarBounds { get; set; }
        StarBoundsTypeEnum StarBoundsType { get; set; }
        Color StarBoundsColor { get; set; }
        ShowAnnotationTypeEnum ShowAnnotationType { get; set; }
        Color AnnotationColor { get; set; }
        FontFamily AnnotationFontFamily { get; set; }
        float AnnotationFontSizePoints { get; set; }
        bool ShowROI { get; set; }
        Color ROIColor { get; set; }
        bool ShowStarCenter { get; set; }
        Color StarCenterColor { get; set; }
        ShowStructureMapEnum ShowStructureMap { get; set; }
        Color StructureMapColor { get; set; }
        IStarDetectionOptions DetectorOptions { get; set; }
        Color TooFlatColor { get; set; }
        Color SaturatedColor { get; set; }
        Color LowSensitivityColor { get; set; }
        Color NotCenteredColor { get; set; }
        Color DegenerateColor { get; set; }
        Color TooDistortedColor { get; set; }
        bool ShowTooDistorted { get; set; }
        bool ShowDegenerate { get; set; }
        bool ShowSaturated { get; set; }
        bool ShowLowSensitivity { get; set; }
        bool ShowNotCentered { get; set; }
        bool ShowTooFlat { get; set; }
    }
}