#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public interface IApplicationDispatcher {

        void DispatchSynchronizationContext(Action action);

        T DispatchSynchronizationContext<T>(Func<T> func);

        /// <summary>
        /// Queues <paramref name="action"/> onto the UI thread and returns immediately, without waiting for it to run.
        /// Use this instead of <see cref="DispatchSynchronizationContext(Action)"/> on any path the UI thread may
        /// transitively wait on — notably NINA's device-info broadcasts, which run on a DeviceUpdateTimer that
        /// application shutdown awaits while the UI thread is blocked and not pumping.
        /// </summary>
        void PostSynchronizationContext(Action action);

        T GetResource<T>(string name, T fallback);
    }
}