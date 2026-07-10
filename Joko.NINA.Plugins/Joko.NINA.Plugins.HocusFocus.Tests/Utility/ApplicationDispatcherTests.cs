using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class ApplicationDispatcherTests {

        /// <summary>
        /// A real WPF <see cref="Dispatcher"/> on a live thread that is deliberately NOT pumping. This is the exact
        /// state NINA's UI thread is in during shutdown: <c>ApplicationDeviceConnectionVM.Shutdown()</c> blocks it
        /// inside <c>Nito.AsyncEx.AsyncContext.Run</c>, which pumps its own queue and never the WPF dispatcher. The
        /// dispatcher is alive and has NOT begun shutting down, so <c>HasShutdownStarted</c> is still false.
        /// </summary>
        private sealed class NonPumpingDispatcherThread : IDisposable {
            private readonly Thread thread;
            private readonly ManualResetEventSlim created = new ManualResetEventSlim();
            private readonly ManualResetEventSlim pump = new ManualResetEventSlim();
            private bool disposed;

            public NonPumpingDispatcherThread() {
                thread = new Thread(() => {
                    Dispatcher = Dispatcher.CurrentDispatcher;
                    created.Set();
                    pump.Wait();
                    Dispatcher.Run();
                });
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                created.Wait();
            }

            public Dispatcher Dispatcher { get; private set; }

            public int ThreadId => thread.ManagedThreadId;

            /// <summary>Lets the dispatcher start pumping, draining whatever was queued while it was blocked.</summary>
            public void StartPumping() => pump.Set();

            public void Dispose() {
                if (disposed) return;
                disposed = true;
                StartPumping();
                Dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                thread.Join(TimeSpan.FromSeconds(10));
                created.Dispose();
                pump.Dispose();
            }
        }

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

        [Test]
        public void PostSynchronizationContext_RunsInlineWhenNoDispatcherAvailable() {
            var sut = new ApplicationDispatcher();
            var ran = false;

            Assert.DoesNotThrow(() => sut.PostSynchronizationContext(() => ran = true));
            Assert.That(ran, Is.True);
        }

        // Characterizes the mechanics of the shutdown deadlock: the blocking variant waits for the dispatcher to
        // pump. Any code path that a shutdown-blocked UI thread transitively waits on must therefore never use it.
        [Test]
        public void DispatchSynchronizationContext_BlocksWhileDispatcherIsNotPumping() {
            using var ui = new NonPumpingDispatcherThread();
            var sut = new ApplicationDispatcher(ui.Dispatcher);

            var blocked = Task.Run(() => sut.DispatchSynchronizationContext(() => { }));

            Assert.That(blocked.Wait(TimeSpan.FromMilliseconds(500)), Is.False,
                "DispatchSynchronizationContext is a blocking Invoke and cannot complete while the dispatcher is idle");

            ui.StartPumping();
            Assert.That(blocked.Wait(TimeSpan.FromSeconds(10)), Is.True,
                "once the dispatcher pumps, the blocked Invoke drains");
        }

        // Regression (NINA hang on exit): FocuserVM/CameraVM's DeviceUpdateTimer broadcasts device info on a
        // threadpool thread, and ApplicationDeviceConnectionVM.Shutdown() awaits that timer to stop while the UI
        // thread is blocked and not pumping. A consumer that marshals with a blocking Invoke never returns, so the
        // timer never stops and shutdown deadlocks. Posting breaks the cycle.
        [Test]
        public void PostSynchronizationContext_DoesNotBlockWhileDispatcherIsNotPumping() {
            using var ui = new NonPumpingDispatcherThread();
            var sut = new ApplicationDispatcher(ui.Dispatcher);

            var posted = Task.Run(() => sut.PostSynchronizationContext(() => { }));

            Assert.That(posted.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "PostSynchronizationContext must return without waiting for the dispatcher to pump");
        }

        [Test]
        public void PostSynchronizationContext_RunsActionOnDispatcherThreadOncePumping() {
            using var ui = new NonPumpingDispatcherThread();
            var sut = new ApplicationDispatcher(ui.Dispatcher);
            var ran = new ManualResetEventSlim();
            var ranOnThreadId = 0;

            sut.PostSynchronizationContext(() => {
                ranOnThreadId = Environment.CurrentManagedThreadId;
                ran.Set();
            });

            Assert.That(ran.IsSet, Is.False, "the posted action must not run until the dispatcher pumps");

            ui.StartPumping();

            Assert.That(ran.Wait(TimeSpan.FromSeconds(10)), Is.True, "the posted action should run once pumping");
            Assert.That(ranOnThreadId, Is.EqualTo(ui.ThreadId), "the action must run on the dispatcher's own thread");
        }

        [Test]
        public void PostSynchronizationContext_RunsInlineWhenAlreadyOnDispatcherThread() {
            using var ui = new NonPumpingDispatcherThread();
            var sut = new ApplicationDispatcher(ui.Dispatcher);
            ui.StartPumping();

            var ranBeforePostReturned = false;
            ui.Dispatcher.Invoke(() => {
                var ran = false;
                sut.PostSynchronizationContext(() => ran = true);
                ranBeforePostReturned = ran;
            });

            Assert.That(ranBeforePostReturned, Is.True,
                "on the dispatcher thread the action should run inline rather than being queued behind pending work");
        }

        // F14, post variant: BeginInvoke on a shut-down dispatcher throws InvalidOperationException on the calling
        // (device-broadcast) thread. Drop the UI-bound work instead of crashing the broadcast.
        [Test]
        public void PostSynchronizationContext_DropsWorkWhenDispatcherHasShutDown() {
            var ui = new NonPumpingDispatcherThread();
            var sut = new ApplicationDispatcher(ui.Dispatcher);
            ui.Dispose();

            var ran = false;
            Assert.DoesNotThrow(() => sut.PostSynchronizationContext(() => ran = true));
            Assert.That(ran, Is.False, "work bound for a dead dispatcher is dropped, not run on the calling thread");
        }
    }
}
