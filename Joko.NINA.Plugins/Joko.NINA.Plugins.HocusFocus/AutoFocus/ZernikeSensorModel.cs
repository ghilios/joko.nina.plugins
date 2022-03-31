#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class ZernikeSensorModel {
        private readonly double radius;
        private readonly double theta;

        public ZernikeSensorModel(double radius, double theta) {
            this.radius = radius;
            this.theta = theta;
        }

        private static readonly double SQRT_3 = Math.Sqrt(3.0);
        private static readonly double SQRT_6 = Math.Sqrt(6.0);
        private static readonly double SQRT_8 = Math.Sqrt(8.0);

        public static double Evaluate(double radius, double theta) {
            var R = radius;
            var R2 = radius * radius;
            var R3 = R2 * radius;
            var sinT = Math.Sin(theta);
            var sin2T = Math.Sin(2.0 * theta);
            var sin3T = Math.Sin(3.0 * theta);
            var cosT = Math.Cos(theta);
            var cos2T = Math.Cos(2.0 * theta);
            var cos3T = Math.Cos(3.0 * theta);
            var z0 = 1.0;
            var z1 = 2.0 * R * sinT;
            var z2 = 2.0 * R * cosT;
            var z3 = SQRT_6 * R2 * sin2T;
            var z4 = SQRT_3 * (2.0 * R2 - 1);
            var z5 = SQRT_6 * R2 * cos2T;
            var z6 = SQRT_8 * R3 * sin3T;
            var z7 = SQRT_8 * (2.0 * R3 - 2.0 * R) * sinT;
            var z8 = SQRT_8 * (2.0 * R3 - 2.0 * R) * cosT;
            var z9 = SQRT_8 * R3 * cos3T;
            return z0 + z1 + z2 + z3 + z4 + z5 + z6 + z7 + z8 + z9;
        }
    }
}