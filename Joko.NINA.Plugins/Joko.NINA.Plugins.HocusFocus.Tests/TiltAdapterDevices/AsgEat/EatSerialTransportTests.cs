#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility.SerialCommunication;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices.AsgEat;

// These tests pin the CONTRACT of EatSerialTransport -- the port config, the read-loop's quiet-period vs
// overall-timeout race, exception propagation, and single-flight-command serialization -- NOT the (still
// unknown) ASG EAT wire format. Fixture strings used to fake device responses are arbitrary placeholders;
// none of this asserts anything about what the real device actually sends (that's EatResponsesTests' job,
// and even there the fixtures are marked ASSUMED/LIVE-CAPTURE, replaced with real captures in plan task T15).
[TestFixture]
public class EatSerialTransportTests {

    // NSubstitute gotcha (documented, not a bug in our code): once a member has been configured to throw,
    // calling `substitute.Member().Returns(...)` a SECOND time to reconfigure it actually re-invokes the
    // FIRST configured behavior (and throws) as a side effect of making that call. To let tests freely swap
    // ReadLine's behavior after OpenAsync's boot-banner drain has already run (which needs its own default
    // "no banner" behavior), ReadLine is configured on the substitute exactly ONCE, delegating to this
    // mutable indirection instead -- reassigning ReadLineBehavior.Next is a plain field write, not another
    // NSubstitute configuration call, so it can't trigger the gotcha.
    private sealed class ReadLineBehavior {
        public Func<string> Next = () => throw new TimeoutException();
    }

    private static (ISerialPortProvider Provider, ISerialPort Port, ReadLineBehavior ReadLine) MakeFakeProvider() {
        var provider = Substitute.For<ISerialPortProvider>();
        var port = Substitute.For<ISerialPort>();
        provider.GetSerialPort(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Parity>(), Arg.Any<int>(), Arg.Any<StopBits>(),
            Arg.Any<Handshake>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(port);

        // No boot banner by default -- every poll during OpenAsync's post-open settle drain times out
        // immediately so tests don't need to wait out the full settle window themselves. Tests that care
        // about SendAsync's own read behavior reassign readLine.Next AFTER OpenAsync completes.
        var readLine = new ReadLineBehavior();
        port.ReadLine().Returns(_ => readLine.Next());
        return (provider, port, readLine);
    }

    // --- OpenAsync: exact port config + explicit DTR. ---

    [Test]
    public async Task OpenAsync_CallsGetSerialPort_WithExactEatPortConfig() {
        var (provider, port, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);

        await transport.OpenAsync("COM7", CancellationToken.None);

        provider.Received(1).GetSerialPort(
            "COM7",
            9600,
            Parity.None,
            8,
            StopBits.One,
            Handshake.XOnXOff,
            Arg.Any<bool>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            // WriteTimeout must exceed the longest legitimate move (5-10 s, per the plan's device facts) --
            // assert it's generously beyond that, without pinning the exact LIVE-CAPTURE-tunable value.
            Arg.Is<int>(writeTimeoutMs => writeTimeoutMs > 10_000));
    }

    [Test]
    public async Task OpenAsync_OpensThePort_AndSetsDtrEnableExplicitly() {
        var (provider, port, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);

        await transport.OpenAsync("COM7", CancellationToken.None);

        port.Received(1).Open();
        // NSubstitute auto-implements interface property get/set, so reading it back after OpenAsync
        // confirms the transport set it explicitly (independent of whatever the fake provider itself did
        // with the constructor-style parameter). Compared against the named constant -- not a literal
        // `true` -- so a future T15 flip of the assumed default doesn't break this "DTR is set explicitly"
        // contract test.
        Assert.That(port.DtrEnable, Is.EqualTo(EatSerialTransport.DefaultDtrEnable));
    }

    [Test]
    public async Task OpenAsync_SetsIsOpenTrue() {
        var (provider, _, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);

        Assert.That(transport.IsOpen, Is.False);
        await transport.OpenAsync("COM7", CancellationToken.None);
        Assert.That(transport.IsOpen, Is.True);
    }

    [Test]
    public void Constructor_NullProvider_Throws() {
        Assert.Throws<ArgumentNullException>(() => new EatSerialTransport(null));
    }

    [Test]
    public void DefaultConstructor_DoesNotThrow() {
        // Uses the real production SerialPortProvider; we never call OpenAsync on it (no real hardware in
        // this test run), so this only confirms the production wiring constructs successfully.
        Assert.DoesNotThrow(() => new EatSerialTransport());
    }

    // --- OpenAsync: double-open guard + half-open-on-failure. ---

    [Test]
    public async Task OpenAsync_WhenAlreadyOpen_ThrowsAndDoesNotOverwriteTheExistingPort() {
        var (provider, port, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        await transport.OpenAsync("COM7", CancellationToken.None);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.OpenAsync("COM7", CancellationToken.None));

        // The double-open guard must reject BEFORE ever asking the provider for a second port -- the
        // original (still-open) port is left completely untouched.
        provider.Received(1).GetSerialPort(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Parity>(), Arg.Any<int>(), Arg.Any<StopBits>(),
            Arg.Any<Handshake>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
        Assert.That(transport.IsOpen, Is.True);
        port.DidNotReceive().Close();
    }

    [Test]
    public void OpenAsync_StepAfterPortOpenThrows_ClosesThePortAndLeavesTransportClosed() {
        var (provider, port, readLine) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        // Force the post-open boot-banner drain to fail with something other than the tolerated
        // TimeoutException -- e.g. the port reporting itself closed mid-drain.
        readLine.Next = () => throw new InvalidOperationException("port closed mid-drain");

        Assert.ThrowsAsync<SerialPortClosedException>(async () =>
            await transport.OpenAsync("COM7", CancellationToken.None));

        // Half-open on failure: the port must be closed and IsOpen must never report true for a failed open.
        Assert.That(transport.IsOpen, Is.False);
        port.Received(1).Close();
    }

    [Test]
    public void OpenAsync_StepAfterPortOpenThrows_AllowsARetryAfterward() {
        var (provider, port, readLine) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        readLine.Next = () => throw new InvalidOperationException("port closed mid-drain");

        Assert.ThrowsAsync<SerialPortClosedException>(async () =>
            await transport.OpenAsync("COM7", CancellationToken.None));

        // A failed open must not leave IsOpen == true (which would make every subsequent OpenAsync hit the
        // double-open guard forever) -- a fresh OpenAsync must be able to proceed normally.
        readLine.Next = () => throw new TimeoutException(); // no banner this time
        Assert.DoesNotThrowAsync(async () => await transport.OpenAsync("COM7", CancellationToken.None));
        Assert.That(transport.IsOpen, Is.True);
    }

    // --- Close: idempotent. ---

    [Test]
    public void Close_WhenNeverOpened_DoesNotThrow() {
        var transport = new EatSerialTransport(Substitute.For<ISerialPortProvider>());
        Assert.DoesNotThrow(() => transport.Close());
    }

    [Test]
    public async Task Close_ClosesThePort_AndIsIdempotent_AndClearsIsOpen() {
        var (provider, port, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        await transport.OpenAsync("COM7", CancellationToken.None);

        transport.Close();
        transport.Close(); // second call must not throw

        port.Received(1).Close();
        Assert.That(transport.IsOpen, Is.False);
    }

    // --- SendAsync: writes + collects lines; command not open guard. ---

    [Test]
    public void SendAsync_BeforeOpen_Throws() {
        var transport = new EatSerialTransport(Substitute.For<ISerialPortProvider>());
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.SendAsync("cp", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Test]
    public async Task SendAsync_WritesCommandPlusTerminator() {
        var (provider, port, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        await transport.OpenAsync("COM7", CancellationToken.None);

        await transport.SendAsync("cp", TimeSpan.FromMilliseconds(300), CancellationToken.None);

        port.Received(1).Write(Arg.Is<string>(s => s.StartsWith("cp", StringComparison.Ordinal) && s.Length > "cp".Length));
    }

    [Test]
    public async Task SendAsync_NoResponseWithinOverallTimeout_ReturnsTimedOutTrue_DoesNotThrow() {
        var (provider, port, readLine) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        await transport.OpenAsync("COM7", CancellationToken.None);
        readLine.Next = () => throw new TimeoutException(); // silent device, e.g. a move in progress

        EatRawExchange exchange = null;
        Assert.DoesNotThrowAsync(async () =>
            exchange = await transport.SendAsync("bf,10", TimeSpan.FromMilliseconds(300), CancellationToken.None));

        Assert.Multiple(() => {
            Assert.That(exchange, Is.Not.Null);
            Assert.That(exchange.TimedOut, Is.True);
            Assert.That(exchange.Lines, Is.Empty);
            Assert.That(exchange.Command, Is.EqualTo("bf,10"));
        });
    }

    [Test]
    public async Task SendAsync_CollectsLinesUntilQuietPeriod_ReturnsTimedOutFalse_AndReturnsEarly() {
        var (provider, port, readLine) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        await transport.OpenAsync("COM7", CancellationToken.None);

        var callCount = 0;
        readLine.Next = () => {
            callCount++;
            return callCount switch {
                1 => "OK",
                2 => "cp,100,200,300,400",
                _ => throw new TimeoutException()
            };
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // A generous overall budget -- the quiet-period race should end the exchange well before this
        // elapses, which the elapsed-time assertion below verifies.
        var exchange = await transport.SendAsync("cp", TimeSpan.FromSeconds(10), CancellationToken.None);
        sw.Stop();

        Assert.Multiple(() => {
            Assert.That(exchange.TimedOut, Is.False);
            Assert.That(exchange.Lines, Is.EqualTo(new[] { "OK", "cp,100,200,300,400" }));
            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)),
                "the quiet-period race should end the exchange well before the 10s overall budget elapses");
        });
    }

    [Test]
    public void SendAsync_PortClosedDuringRead_PropagatesSerialPortClosedException_NotSwallowed() {
        var (provider, port, readLine) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        transport.OpenAsync("COM7", CancellationToken.None).GetAwaiter().GetResult();
        readLine.Next = () => throw new InvalidOperationException("port closed");

        Assert.ThrowsAsync<SerialPortClosedException>(async () =>
            await transport.SendAsync("cp", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Test]
    public void SendAsync_PortClosedDuringWrite_PropagatesSerialPortClosedException_NotSwallowed() {
        var (provider, port, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        transport.OpenAsync("COM7", CancellationToken.None).GetAwaiter().GetResult();
        port.When(p => p.Write(Arg.Any<string>())).Do(_ => throw new InvalidOperationException("port closed"));

        Assert.ThrowsAsync<SerialPortClosedException>(async () =>
            await transport.SendAsync("cp", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Test]
    public void SendAsync_WriteTimesOut_PropagatesAsHardFailure_NotSwallowed() {
        var (provider, port, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        transport.OpenAsync("COM7", CancellationToken.None).GetAwaiter().GetResult();
        port.When(p => p.Write(Arg.Any<string>())).Do(_ => throw new TimeoutException("write timeout"));

        // A write timeout must propagate as a hard failure -- unlike a read timeout, it must NOT be
        // swallowed/converted into a tolerant EatRawExchange.
        Assert.ThrowsAsync<TimeoutException>(async () =>
            await transport.SendAsync("cp", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Test]
    public async Task SendAsync_WritesOnABackgroundThread_NotTheCallerThread() {
        var (provider, port, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        await transport.OpenAsync("COM7", CancellationToken.None);

        var callerThreadId = Thread.CurrentThread.ManagedThreadId;
        int? writeThreadId = null;
        port.When(p => p.Write(Arg.Any<string>())).Do(_ => writeThreadId = Thread.CurrentThread.ManagedThreadId);

        // A synchronous inline Write() could block the caller (e.g. the UI thread) for up to WriteTimeout
        // under an XON/XOFF stall -- it must be offloaded, mirroring the already-offloaded read.
        await transport.SendAsync("cp", TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.That(writeThreadId, Is.Not.Null);
        Assert.That(writeThreadId, Is.Not.EqualTo(callerThreadId));
    }

    [Test]
    public async Task SendAsync_ConcurrentCloseDuringRead_DoesNotThrowNullReferenceException() {
        var (provider, port, readLine) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        await transport.OpenAsync("COM7", CancellationToken.None);

        var readStarted = new ManualResetEventSlim(false);
        var releaseRead = new ManualResetEventSlim(false);
        var readCallCount = 0;
        readLine.Next = () => {
            // Only the FIRST poll blocks and then returns a line; every subsequent poll times out so the
            // quiet-period race ends the exchange after that one line instead of looping until the 5s
            // overall budget (which would otherwise return thousands of "OK" lines).
            if (Interlocked.Increment(ref readCallCount) == 1) {
                readStarted.Set();
                releaseRead.Wait(TimeSpan.FromSeconds(10));
                return "OK";
            }
            throw new TimeoutException();
        };

        var sendTask = transport.SendAsync("cp", TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.That(readStarted.Wait(TimeSpan.FromSeconds(5)), Is.True, "the read loop should have started");

        // Close the transport (nulling the serialPort field) WHILE the read above is still in flight.
        // SendAsync snapshotted the port into a local before dispatching the read, so the in-flight read
        // must complete cleanly against that snapshot instead of NRE-ing on the now-null field.
        transport.Close();
        releaseRead.Set();

        EatRawExchange exchange = null;
        Assert.DoesNotThrowAsync(async () => exchange = await sendTask);
        Assert.That(exchange.Lines, Is.EqualTo(new[] { "OK" }));
    }

    [Test]
    public async Task SendAsync_AlreadyCancelledToken_ThrowsWithoutWriting() {
        var (provider, port, _) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        await transport.OpenAsync("COM7", CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await transport.SendAsync("cp", TimeSpan.FromSeconds(1), cts.Token));
        port.DidNotReceive().Write(Arg.Any<string>());
    }

    // --- Single-flight: only one command in flight at a time. ---

    [Test]
    public async Task SendAsync_SerializesConcurrentCalls_SecondDoesNotWriteUntilFirstCompletes() {
        var (provider, port, readLine) = MakeFakeProvider();
        var transport = new EatSerialTransport(provider);
        await transport.OpenAsync("COM7", CancellationToken.None);

        var firstReadStarted = new ManualResetEventSlim(false);
        var releaseFirstRead = new ManualResetEventSlim(false);
        var writeOrder = new List<string>();
        var readCallCount = 0;

        port.When(p => p.Write(Arg.Any<string>())).Do(callInfo => {
            lock (writeOrder) {
                // The written string includes the transport's appended line terminator; strip it so this
                // test can assert on the plain command text (the terminator itself is a separate,
                // LIVE-CAPTURE-marked concern covered by SendAsync_WritesCommandPlusTerminator).
                writeOrder.Add(((string)callInfo[0]).TrimEnd('\r', '\n'));
            }
        });
        readLine.Next = () => {
            var count = Interlocked.Increment(ref readCallCount);
            if (count == 1) {
                firstReadStarted.Set();
                releaseFirstRead.Wait(TimeSpan.FromSeconds(10));
            }
            throw new TimeoutException();
        };

        var firstTask = transport.SendAsync("first", TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.That(firstReadStarted.Wait(TimeSpan.FromSeconds(5)), Is.True, "first command's read loop should have started");

        var secondTask = transport.SendAsync("second", TimeSpan.FromMilliseconds(200), CancellationToken.None);
        await Task.Delay(150);
        lock (writeOrder) {
            Assert.That(writeOrder, Does.Not.Contain("second"),
                "the second command must not be written while the first is still in flight (the semaphore should block it)");
        }

        releaseFirstRead.Set();
        await Task.WhenAll(firstTask, secondTask);

        lock (writeOrder) {
            Assert.That(writeOrder, Is.EqualTo(new[] { "first", "second" }));
        }
    }
}
