using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class ApplicationDispatcherTests {

        // F13: when no WPF Application/Dispatcher is available (headless / early startup / test host), the cached
        // dispatcher is null. DispatchSynchronizationContext must fall back to inline invocation rather than
        // dereferencing null and throwing, on the calling (e.g. device-broadcast) thread.
        [Test]
        public void DispatchSynchronizationContext_Action_RunsInlineWhenNoDispatcherAvailable() {
            var sut = new ApplicationDispatcher();
            var ran = false;

            Assert.DoesNotThrow(() => sut.DispatchSynchronizationContext(() => ran = true));
            Assert.That(ran, Is.True);
        }

        [Test]
        public void DispatchSynchronizationContext_Func_RunsInlineWhenNoDispatcherAvailable() {
            var sut = new ApplicationDispatcher();

            int result = 0;
            Assert.DoesNotThrow(() => result = sut.DispatchSynchronizationContext(() => 42));
            Assert.That(result, Is.EqualTo(42));
        }
    }
}
