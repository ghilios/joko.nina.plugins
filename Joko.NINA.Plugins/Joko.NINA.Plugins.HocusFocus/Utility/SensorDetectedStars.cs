#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public class SensorDetectedStars {

        public SensorDetectedStars(int focuserPosition, HocusFocusStarDetectionResult starDetectionResult) {
            this.FocuserPosition = focuserPosition;
            this.StarDetectionResult = starDetectionResult;
        }

        public int FocuserPosition { get; private set; }
        public HocusFocusStarDetectionResult StarDetectionResult { get; private set; }

        public override string ToString() {
            return $"{{{nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(StarDetectionResult)}={StarDetectionResult}}}";
        }
    }
}