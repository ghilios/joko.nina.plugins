#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public class StarField {

        public StarField(int index, int focuserPosition, StarDetectionResult starDetectionResult) {
            this.RegistrationIndex = index;
            this.FocuserPosition = focuserPosition;
            this.StarDetectionResult = starDetectionResult;
        }

        public int RegistrationIndex { get; private set; }
        public int FocuserPosition { get; private set; }
        public StarDetectionResult StarDetectionResult { get; private set; }

        public override string ToString() {
            return $"{{{nameof(RegistrationIndex)}={RegistrationIndex.ToString()}, {nameof(FocuserPosition)}={FocuserPosition.ToString()}, {nameof(StarDetectionResult)}={StarDetectionResult}}}";
        }
    }

    public class MatchedStar {

        public MatchedStar(StarField starField, HocusFocusDetectedStar star) {
            this.StarField = starField;
            this.Star = star;
        }

        public StarField StarField { get; set; }
        public HocusFocusDetectedStar Star { get; set; }

        public override string ToString() {
            return $"{{{nameof(StarField)}={StarField}, {nameof(Star)}={Star}}}";
        }
    }

    public class RegisteredStar {

        public RegisteredStar(int index, MatchedStar matchedStar) {
            this.Index = index;
            this.RegistrationX = matchedStar.Star.Position.X;
            this.RegistrationY = matchedStar.Star.Position.Y;
            this.MatchedStars = new List<MatchedStar>();
            this.MatchedStars.Add(matchedStar);
        }

        public int Index { get; private set; }
        public double RegistrationX { get; private set; }
        public double RegistrationY { get; private set; }
        public List<MatchedStar> MatchedStars { get; private set; }

        public override string ToString() {
            return $"{{{nameof(Index)}={Index.ToString()}, {nameof(RegistrationX)}={RegistrationX.ToString()}, {nameof(RegistrationY)}={RegistrationY.ToString()}, {nameof(MatchedStars)}={MatchedStars.Count}}}";
        }
    }

    public interface IStarRegistry : IReadOnlyList<RegisteredStar> {
        float SearchRadiusPixels { get; }
        int StarFieldCount { get; }

        StarField GetStarField(int starFieldIndex);

        void AddStarField(int focuserPosition, StarDetectionResult starDetectionResult);
    }
}