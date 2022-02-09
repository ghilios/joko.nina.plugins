#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class PSFModel {

        public PSFModel(StarDetectorPSFFitType psfType, double sigmaX, double sigmaY, double fwhmX, double fwhmY, double thetaRadians, double rSquared, double pixelScale) {
            this.PSFType = psfType;
            this.SigmaX = sigmaX;
            this.SigmaY = sigmaY;
            this.Sigma = Math.Sqrt(sigmaX * sigmaY);
            this.FWHMx = fwhmX;
            this.FWHMy = fwhmY;
            this.ThetaRadians = thetaRadians;
            var a = Math.Max(fwhmX, fwhmY);
            var b = Math.Min(fwhmX, fwhmY);
            this.Eccentricity = Math.Sqrt(1 - b * b / (a * a));
            this.FWHMPixels = Math.Sqrt(fwhmX * fwhmY);
            this.FWHMArcsecs = this.FWHMPixels * pixelScale;
            this.RSquared = rSquared;
        }

        public StarDetectorPSFFitType PSFType { get; private set; }
        public double SigmaX { get; private set; }
        public double SigmaY { get; private set; }
        public double Sigma { get; private set; }
        public double FWHMx { get; private set; }
        public double FWHMy { get; private set; }
        public double ThetaRadians { get; private set; }
        public double FWHMPixels { get; private set; }
        public double FWHMArcsecs { get; private set; }
        public double Eccentricity { get; private set; }
        public double RSquared { get; private set; }

        public override string ToString() {
            return $"{{{nameof(PSFType)}={PSFType.ToString()}, {nameof(SigmaX)}={SigmaX.ToString()}, {nameof(SigmaY)}={SigmaY.ToString()}, {nameof(Sigma)}={Sigma.ToString()}, {nameof(FWHMx)}={FWHMx.ToString()}, {nameof(FWHMy)}={FWHMy.ToString()}, {nameof(ThetaRadians)}={ThetaRadians.ToString()}, {nameof(FWHMPixels)}={FWHMPixels.ToString()}, {nameof(FWHMArcsecs)}={FWHMArcsecs.ToString()}, {nameof(Eccentricity)}={Eccentricity.ToString()}, {nameof(RSquared)}={RSquared.ToString()}}}";
        }
    }
}