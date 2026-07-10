using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles {

    /// <summary>
    /// Runs dispatched actions inline (like <see cref="SynchronousApplicationDispatcher"/>) but counts how many times
    /// <c>DispatchSynchronizationContext</c> was invoked, so tests can assert that a code path actually marshals UI work
    /// through the dispatcher instead of touching commands/collections directly on the calling thread.
    /// <see cref="PostCount"/> tracks the non-blocking variant separately, so tests can pin which paths must never
    /// block the calling thread on the UI thread.
    /// </summary>
    internal sealed class RecordingApplicationDispatcher : IApplicationDispatcher {

        public int DispatchCount { get; private set; }

        public int PostCount { get; private set; }

        public void DispatchSynchronizationContext(Action action) {
            DispatchCount++;
            action();
        }

        public T DispatchSynchronizationContext<T>(Func<T> func) {
            DispatchCount++;
            return func();
        }

        public void PostSynchronizationContext(Action action) {
            PostCount++;
            action();
        }

        public T GetResource<T>(string name, T fallback) => fallback;
    }
}
