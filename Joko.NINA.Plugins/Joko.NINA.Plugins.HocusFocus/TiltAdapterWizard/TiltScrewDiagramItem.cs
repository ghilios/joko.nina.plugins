#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// Geometry of the shared HF_TiltScrewDiagram canvas (TiltAdapterWizard/DataTemplates.xaml). The canvas is
    /// 2 × <see cref="Center"/> on a side and every fixed coordinate in it — crosshair, sensor rectangle, 0°
    /// chevron — is expressed relative to that same centre, so every VM that plots
    /// <see cref="TiltScrewDiagramItem"/>s into it must use THESE constants. Both the wizard and the camera
    /// simulator's adapter panel render this template; a consumer with its own copy silently drifts off the
    /// crosshair when the canvas is resized, which is exactly how the simulator's diagram broke when the canvas
    /// grew for screw names. Changing these means changing the template to match.
    /// </summary>
    public static class TiltScrewDiagramGeometry {
        public const double Center = 130.0;
        public const double ScrewRadius = 75.0;
        public const double ScrewCircleRadius = 12.0;
        public const double LabelWidth = 64.0;
        public const double LabelHeight = 14.0;
    }

    public class TiltScrewDiagramItem {
        public double X { get; set; }           // Canvas.Left
        public double Y { get; set; }           // Canvas.Top
        public int Number { get; set; }         // 1..4
        public double AngleDegrees { get; set; }

        // The screw's name, drawn just outside its circle. The circle itself keeps the wizard number: it is
        // 24px across, and a name has to stay readable at any length. Placement is computed in
        // RebuildDiagram (above the circle in the top half of the canvas, below it in the bottom half) so
        // labels never land on the sensor rectangle or on each other.
        public string Label { get; set; } = string.Empty;

        public double LabelX { get; set; }      // Canvas.Left of the label block
        public double LabelY { get; set; }      // Canvas.Top of the label block
    }
}
