#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.WPF.Base.Utility.AutoFocus;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public abstract class AlglibHyperbolicFitting : HyperbolicFitting {
        public double[][] Inputs { get; protected set; }
        public double[] Weights { get; protected set; }
        public double[] Outputs { get; protected set; }
        public int StepSize { get; protected set; }
        public bool OptGuardEnabled { get; set; } = false;

        /// <summary>
        /// Standard error of the fitted minimum's focuser position (parameter x0), propagated from the
        /// fit covariance. <see cref="double.NaN"/> when not computed (e.g. too few points, or a fitting
        /// variant that does not estimate it). Used downstream to weight the paraboloid fit by 1/σ².
        /// </summary>
        public double MinimumStdError { get; protected set; } = double.NaN;

        public abstract bool Solve();
    }
}