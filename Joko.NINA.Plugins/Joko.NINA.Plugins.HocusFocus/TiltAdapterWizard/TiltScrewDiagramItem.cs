#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

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
    }
}
