#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class TiltAdapterGuidanceVM {
        public int ScrewCount { get; set; }
        public bool HasFourScrews => ScrewCount == 4;
        public bool HasTiltGuidance { get; set; }
        public bool HasBackfocusRow { get; set; }

        public string Screw1TiltArrow { get; set; } = "—";
        public string Screw2TiltArrow { get; set; } = "—";
        public string Screw3TiltArrow { get; set; } = "—";
        public string Screw4TiltArrow { get; set; } = "—";

        public string Screw1BackfocusArrow { get; set; } = "—";
        public string Screw2BackfocusArrow { get; set; } = "—";
        public string Screw3BackfocusArrow { get; set; } = "—";
        public string Screw4BackfocusArrow { get; set; } = "—";
    }
}
