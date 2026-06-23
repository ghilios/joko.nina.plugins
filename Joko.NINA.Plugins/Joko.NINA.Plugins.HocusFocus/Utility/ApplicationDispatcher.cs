#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Windows;
using System.Windows.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public class ApplicationDispatcher : IApplicationDispatcher {

        // Capture the WPF dispatcher directly. CheckAccess() lets us detect the UI thread reliably (a self-built
        // DispatcherSynchronizationContext never reference-equals the one WPF installs, so the old fast path never
        // fired — F12). The handle is null in headless/early-startup/test hosts, in which case we invoke inline (F13).
        private readonly Dispatcher dispatcher = Application.Current?.Dispatcher;

        public void DispatchSynchronizationContext(Action action) {
            // No dispatcher (headless/test host) or already on the UI thread: run inline (F12 fast path, F13 fallback).
            if (dispatcher == null || dispatcher.CheckAccess()) {
                action();
                return;
            }

            // The dispatcher is shutting down / has shut down: it will no longer pump, so Send would throw
            // InvalidOperationException on the calling thread. Drop the UI-bound work rather than crash (F14).
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) {
                return;
            }

            try {
                dispatcher.Invoke(action);
            } catch (OperationCanceledException) {
                // Dispatcher shut down between the check above and the Invoke; nothing to do.
            } catch (InvalidOperationException) {
                // Same race: "The Dispatcher has been shut down" surfaced as InvalidOperationException.
            }
        }

        public T DispatchSynchronizationContext<T>(Func<T> func) {
            if (dispatcher == null || dispatcher.CheckAccess()) {
                return func();
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) {
                return default;
            }

            try {
                return dispatcher.Invoke(func);
            } catch (OperationCanceledException) {
                return default;
            } catch (InvalidOperationException) {
                return default;
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