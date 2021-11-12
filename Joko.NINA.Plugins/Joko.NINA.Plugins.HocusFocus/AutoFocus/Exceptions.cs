﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace Joko.NINA.Plugins.HocusFocus.AutoFocus {

    public class TooManyFailedMeasurementsException : Exception {
        public int NumFailures { get; private set; }

        public TooManyFailedMeasurementsException(int numFailures) : base("Too many failed measurements") {
            this.NumFailures = numFailures;
        }
    }

    public class InitialHFRFailedException : Exception {

        public InitialHFRFailedException() : base("Calculating initial HFR failed") {
        }
    }
}