﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Threading;
using static alglib;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public interface IAlglibAPI {

        void minlmcreatevj(int m, double[] x, out minlmstate state);

        void minlmsetacctype(minlmstate state, int acctype);

        void minlmcreatev(int m, double[] x, double diffstep, out minlmstate state);

        void rbfcreate(int nx, int ny, out rbfmodel s);

        void minlmsetbc(minlmstate state, double[] bndl, double[] bndu);

        void minlmsetcond(minlmstate state, double epsx, int maxits);

        void minlmsetscale(minlmstate state, double[] s);

        void minlmoptguardgradient(minlmstate state, double teststep);

        void minlmoptimize(minlmstate state, ndimensional_fvec fvec, ndimensional_jac jac, ndimensional_rep rep, object obj);

        void minlmresults(minlmstate state, out double[] x, out minlmreport rep);

        void minlmoptguardresults(minlmstate state, out optguardreport rep);

        void deallocateimmediately<T>(ref T obj) where T : alglibobject;
    }

    public class AlglibAPI : IAlglibAPI {
        private readonly ReaderWriterLockSlim allocationLock;

        public AlglibAPI() {
            this.allocationLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }

        public void deallocateimmediately<T>(ref T obj) where T : alglibobject {
            using (this.allocationLock.LockWrite()) {
                alglib.deallocateimmediately(ref obj);
            }
        }

        public void minlmcreatev(int m, double[] x, double diffstep, out minlmstate state) {
            using (this.allocationLock.LockRead()) {
                alglib.minlmcreatev(m, x, diffstep, out state);
            }
        }

        public void minlmcreatevj(int m, double[] x, out minlmstate state) {
            using (this.allocationLock.LockRead()) {
                alglib.minlmcreatevj(m, x, out state);
            }
        }

        public void rbfcreate(int nx, int ny, out rbfmodel s) {
            using (this.allocationLock.LockRead()) {
                alglib.rbfcreate(nx, ny, out s);
            }
        }

        public void minlmoptguardgradient(minlmstate state, double teststep) {
            alglib.minlmoptguardgradient(state, teststep);
        }

        public void minlmoptguardresults(minlmstate state, out optguardreport rep) {
            alglib.minlmoptguardresults(state, out rep);
        }

        public void minlmoptimize(minlmstate state, ndimensional_fvec fvec, ndimensional_jac jac, ndimensional_rep rep, object obj) {
            alglib.minlmoptimize(state, fvec, jac, rep, obj);
        }

        public void minlmresults(minlmstate state, out double[] x, out minlmreport rep) {
            alglib.minlmresults(state, out x, out rep);
        }

        public void minlmsetacctype(minlmstate state, int acctype) {
            alglib.minlmsetacctype(state, acctype);
        }

        public void minlmsetbc(minlmstate state, double[] bndl, double[] bndu) {
            alglib.minlmsetbc(state, bndl, bndu);
        }

        public void minlmsetcond(minlmstate state, double epsx, int maxits) {
            alglib.minlmsetcond(state, epsx, maxits);
        }

        public void minlmsetscale(minlmstate state, double[] s) {
            alglib.minlmsetscale(state, s);
        }
    }
}