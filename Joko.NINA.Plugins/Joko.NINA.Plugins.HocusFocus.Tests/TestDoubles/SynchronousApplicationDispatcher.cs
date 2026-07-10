using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles {

    internal sealed class SynchronousApplicationDispatcher : IApplicationDispatcher {

        public void DispatchSynchronizationContext(Action action) => action();

        public T DispatchSynchronizationContext<T>(Func<T> func) => func();

        public void PostSynchronizationContext(Action action) => action();

        public T GetResource<T>(string name, T fallback) => fallback;
    }
}
