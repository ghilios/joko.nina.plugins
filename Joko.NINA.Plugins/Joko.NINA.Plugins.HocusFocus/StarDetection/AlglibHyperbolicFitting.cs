﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

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

        public abstract bool Solve();
    }
}