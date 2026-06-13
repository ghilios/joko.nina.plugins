using NINA.Joko.Plugins.HocusFocus.Utility;
using static alglib;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles {

    /// <summary>
    /// Delegates to a real <see cref="AlglibAPI"/> but corrupts the Nth minlmresults call (1-based) —
    /// NaN parameters and a negative termination type — to simulate a mid-IRLS optimizer failure.
    /// </summary>
    internal sealed class FailNthSolveAlglibAPI : IAlglibAPI {
        private readonly IAlglibAPI inner = new AlglibAPI();
        private readonly int failOnCall;
        private int resultsCalls;

        public FailNthSolveAlglibAPI(int failOnCall) {
            this.failOnCall = failOnCall;
        }

        public void minlmresults(minlmstate state, out double[] x, out minlmreport rep) {
            inner.minlmresults(state, out x, out rep);
            if (++resultsCalls == failOnCall) {
                for (int i = 0; i < x.Length; ++i) {
                    x[i] = double.NaN;
                }
                rep.terminationtype = -3; // any negative type (except -5) makes SolveOnce return false
            }
        }

        public void minlmcreatevj(int m, double[] x, out minlmstate state) => inner.minlmcreatevj(m, x, out state);

        public void minlmsetacctype(minlmstate state, int acctype) => inner.minlmsetacctype(state, acctype);

        public void minlmcreatev(int m, double[] x, double diffstep, out minlmstate state) => inner.minlmcreatev(m, x, diffstep, out state);

        public void rbfcreate(int nx, int ny, out rbfmodel s) => inner.rbfcreate(nx, ny, out s);

        public void minlmsetbc(minlmstate state, double[] bndl, double[] bndu) => inner.minlmsetbc(state, bndl, bndu);

        public void minlmsetcond(minlmstate state, double epsx, int maxits) => inner.minlmsetcond(state, epsx, maxits);

        public void minlmsetscale(minlmstate state, double[] s) => inner.minlmsetscale(state, s);

        public void minlmoptguardgradient(minlmstate state, double teststep) => inner.minlmoptguardgradient(state, teststep);

        public void minlmoptimize(minlmstate state, ndimensional_fvec fvec, ndimensional_jac jac, ndimensional_rep rep, object obj) => inner.minlmoptimize(state, fvec, jac, rep, obj);

        public void minlmoptguardresults(minlmstate state, out optguardreport rep) => inner.minlmoptguardresults(state, out rep);

        public void deallocateimmediately<T>(ref T obj) where T : alglibobject => inner.deallocateimmediately(ref obj);
    }
}
