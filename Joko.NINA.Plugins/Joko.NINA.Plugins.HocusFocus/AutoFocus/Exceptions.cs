using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
