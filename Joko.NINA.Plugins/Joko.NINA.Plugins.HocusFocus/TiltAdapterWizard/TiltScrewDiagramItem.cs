#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

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
