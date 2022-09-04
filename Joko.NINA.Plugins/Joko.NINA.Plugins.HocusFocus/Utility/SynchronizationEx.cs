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
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public class AutoReadLockSlim : IDisposable {
        private readonly ReaderWriterLockSlim lockObj;

        public AutoReadLockSlim(ReaderWriterLockSlim lockObj) {
            this.lockObj = lockObj;
            this.lockObj.EnterReadLock();
        }

        public void Dispose() {
            this.lockObj.ExitReadLock();
        }
    }

    public class AutoWriteLockSlim : IDisposable {
        private readonly ReaderWriterLockSlim lockObj;

        public AutoWriteLockSlim(ReaderWriterLockSlim lockObj) {
            this.lockObj = lockObj;
            this.lockObj.EnterWriteLock();
        }

        public void Dispose() {
            this.lockObj.ExitWriteLock();
        }
    }

    public static class SynchronizationEx {

        public static AutoReadLockSlim LockRead(this ReaderWriterLockSlim lockObj) {
            return new AutoReadLockSlim(lockObj);
        }

        public static AutoWriteLockSlim LockWrite(this ReaderWriterLockSlim lockObj) {
            return new AutoWriteLockSlim(lockObj);
        }
    }
}