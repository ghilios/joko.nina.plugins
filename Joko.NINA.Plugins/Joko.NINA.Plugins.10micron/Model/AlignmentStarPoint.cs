#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using Joko.NINA.Plugins.TenMicron.Utility;
using NINA.Astrometry;
using System;

namespace Joko.NINA.Plugins.TenMicron.Model {

    public class AlignmentStarPoint {
        public AlignmentStarInfo AlignmentStarInfo { get; private set; }
        public double ErrorArcsec { get; private set; }
        public double ErrorPointRadius { get; private set; }
        public Angle RightAscension { get; private set; }
        public Angle Declination { get; private set; }
        public double Altitude { get; private set; }
        public double InvertedAltitude => 90.0 - Altitude;
        public double Azimuth { get; private set; }

        private AlignmentStarPoint() {
        }

        public static AlignmentStarPoint FromAlignmentStarInfo(
            AlignmentStarInfo alignmentStarInfo,
            double modelMaxErrorArcsec,
            Angle latitude,
            Angle longitude,
            double siteElevation) {
            var now = DateTime.Now;
            var lst = AstroUtil.GetLocalSiderealTime(now, longitude.Degree);
            var alignmentStarRA = Angle.ByHours(AstroUtil.EuclidianModulus(lst - alignmentStarInfo.LocalHour.ToAngle().Hours, 24));
            var alignmentStarDec = alignmentStarInfo.Declination.ToAngle();

            var coordinates = new Coordinates(ra: alignmentStarRA, dec: alignmentStarDec, epoch: Epoch.JNOW, referenceDate: now, dateTime: new ConstantDateTime(now));
            var topocentricCoordinates = coordinates.Transform(latitude: latitude, longitude: longitude, elevation: siteElevation);
            var errorRatio = (double)alignmentStarInfo.ErrorArcseconds / modelMaxErrorArcsec;
            return new AlignmentStarPoint() {
                AlignmentStarInfo = alignmentStarInfo,
                Altitude = topocentricCoordinates.Altitude.Degree,
                RightAscension = alignmentStarRA,
                Declination = alignmentStarDec,
                Azimuth = topocentricCoordinates.Azimuth.Degree,
                ErrorArcsec = (double)alignmentStarInfo.ErrorArcseconds,
                ErrorPointRadius = 5.0 * Math.Max(1.0, 1.5 * errorRatio)
            };
        }
    }
}