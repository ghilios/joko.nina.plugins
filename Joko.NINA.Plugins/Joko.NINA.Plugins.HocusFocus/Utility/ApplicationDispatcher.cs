#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public class ApplicationDispatcher : IApplicationDispatcher {

        private readonly SynchronizationContext synchronizationContext = Application.Current?.Dispatcher != null
            ? new DispatcherSynchronizationContext(Application.Current.Dispatcher)
            : null;

        public void DispatchSynchronizationContext(Action action) {
            if (SynchronizationContext.Current == synchronizationContext) {
                action();
            } else {
                synchronizationContext.Send(_ => action(), null);
            }
        }

        public T DispatchSynchronizationContext<T>(Func<T> func) {
            if (SynchronizationContext.Current == synchronizationContext) {
                return func();
            } else {
                var result = default(T);
                synchronizationContext.Send(_ => result = func(), null);
                return result;
            }
        }

        public T GetResource<T>(string name, T fallback) {
            var resource = DispatchSynchronizationContext(() => Application.Current.TryFindResource(name));
            if (resource is T) {
                return (T)resource;
            } else {
                return fallback;
            }
        }
    }
}