#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NSubstitute;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices.AsgEat;

/// <summary>
/// MANUAL, HARDWARE-IN-THE-LOOP integration test for the real ASG EAT (firmware 7.1.0) over serial. It is
/// <see cref="ExplicitAttribute"/> and category "HardwareIntegration", so it NEVER runs in the normal unit
/// suite (`dotnet test` with no filter skips it); run it deliberately with, e.g.:
///   dotnet test --filter "FullyQualifiedName~EatHardwareIntegrationTests"
///
/// It drives the FULL production stack -- <see cref="EatTiltMotionController"/> over the real
/// <see cref="EatSerialTransport"/> -- against the physical device on <see cref="Port"/>. It connects, reads
/// the current positions, then exercises every move mnemonic this plugin depends on (each of the five axes,
/// once forward and once reversed), asserting the device's reported per-motor positions after every command,
/// and finally asserts the device ends exactly where it started. Each move persists to the device's EEPROM,
/// so a clean pass leaves the hardware unchanged.
///
/// <para>Progress is written LIVE (one flushed line per step) to <see cref="ProgressLogPath"/> as well as to
/// NUnit's <see cref="TestContext.Progress"/>, so an observer can tail the file while the test runs.</para>
///
/// <para>MACHINE-SPECIFIC by design (per the manual-test request): the COM port and the progress-log path are
/// hardcoded below for the current bench setup. Adjust them for a different machine.</para>
/// </summary>
[TestFixture]
[Explicit("Requires a real ASG EAT physically connected on COM7. Moves real motors and writes device EEPROM.")]
[Category("HardwareIntegration")]
public class EatHardwareIntegrationTests {

    // --- MACHINE-SPECIFIC constants (manual test) ---------------------------------------------------------
    private const string Port = "COM7";
    // Windows path; tail it from WSL at /mnt/c/Users/ghili/eat-probe/integration-progress.log while running.
    private const string ProgressLogPath = @"C:\Users\ghili\eat-probe\integration-progress.log";

    // Small, reversible probe magnitude (<= 10 steps, per the capture constraints).
    private const int N = 5;

    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task FullMnemonicRoundTrip_OnRealDevice_ReturnsToStartingPositions() {
        ResetProgressLog();
        Progress($"=== ASG EAT hardware integration test — port {Port}, step size {N} ===");

        // Generous limits: the device's counters sit around 600, and this test only nudges them by +/-N; we
        // do not want the software soft-limits to interfere with a protocol/wire validation run.
        var options = Substitute.For<ITiltAdapterOptions>();
        options.TiltDeviceMaxStepsPerCommand.Returns(1000);
        options.TiltDeviceMaxExcursionSteps.Returns(1_000_000);
        options.TiltDeviceSettleSeconds.Returns(0.0);
        // TiltDeviceShadowPositions is left to NSubstitute's auto-backing (starts null -> shadow unseeded;
        // ConnectAsync's 'cp' seeds it from the real device).

        var controller = new EatTiltMotionController(options);
        var moveProgress = new ActionProgress(s => Progress($"    device: {s}"));

        try {
            Progress($"Connecting (opens {Port}, resets the Arduino, drains the boot banner, sends 'cp')...");
            await controller.ConnectAsync(Port, None);
            Assert.That(controller.Connected, Is.True, "controller should report Connected after ConnectAsync");
            Assert.That(controller.AbsolutePositionsKnown, Is.True,
                "the 'cp' response at connect should have parsed, seeding known absolute positions");

            var start = await ReadPositionsAsync(controller, "start");

            // Each axis, once forward (+N) and once reversed (-N). The forward move uses the axis's positive
            // mnemonic (tr/tl/tp/rt/bf); the reverse uses the SAME mnemonic with a negative signed argument
            // (SignedArgument encoding, confirmed on hardware) -- so the opposite mnemonics (bl/br/bt/lt),
            // which this plugin never emits, are intentionally NOT exercised.
            // Expected per-motor deltas are in DEVICE motor order [TR, TL, BR, BL] and are an INDEPENDENT
            // oracle (hardcoded from the confirmed physical corner effects), not derived from the code
            // under test.
            var axes = new[] {
                (Axis: TiltMoveAxis.DiagonalA,      Name: "DiagonalA",      Delta: new[] { +N, 0, 0, -N }),
                (Axis: TiltMoveAxis.DiagonalB,      Name: "DiagonalB",      Delta: new[] { 0, +N, -N, 0 }),
                (Axis: TiltMoveAxis.EdgeVertical,   Name: "EdgeVertical",   Delta: new[] { +N, +N, -N, -N }),
                (Axis: TiltMoveAxis.EdgeHorizontal, Name: "EdgeHorizontal", Delta: new[] { +N, -N, +N, -N }),
                (Axis: TiltMoveAxis.Backfocus,      Name: "Backfocus",      Delta: new[] { +N, +N, +N, +N }),
            };

            foreach (var (axis, name, delta) in axes) {
                var group = axis == TiltMoveAxis.Backfocus ? TiltMoveGroup.Backfocus : TiltMoveGroup.Tilt;

                var before = await ReadPositionsAsync(controller, $"{name}: before");

                var forward = new TiltAdapterMove(axis, +N, group, $"{name} +{N}");
                Progress($"{name}: sending +{N}  (wire '{EatCommands.Format(forward)}')");
                await controller.ExecuteMoveAsync(forward, moveProgress, None);
                var after = await ReadPositionsAsync(controller, $"{name}: after +{N}");
                var expected = Add(before, delta);
                Progress($"    expected [{Join(expected)}]  (device order TR,TL,BR,BL)");
                Assert.That(after, Is.EqualTo(expected),
                    $"{name} +{N}: device positions after the forward move [TR,TL,BR,BL]");

                var reverse = new TiltAdapterMove(axis, -N, group, $"{name} -{N}");
                Progress($"{name}: reversing -{N}  (wire '{EatCommands.Format(reverse)}')");
                await controller.ExecuteMoveAsync(reverse, moveProgress, None);
                var back = await ReadPositionsAsync(controller, $"{name}: after -{N}");
                Assert.That(back, Is.EqualTo(before),
                    $"{name} -{N}: reversing must restore the pre-move positions");
            }

            var end = await ReadPositionsAsync(controller, "end");
            Assert.That(end, Is.EqualTo(start),
                "the device must end at exactly the positions it started at (net-zero across the whole run)");
            Progress($"=== PASS — all five axes exercised; positions restored: [{Join(start)}] -> [{Join(end)}] ===");
        } finally {
            try {
                await controller.DisconnectAsync(None);
                Progress("Disconnected.");
            } catch (Exception ex) {
                Progress($"Disconnect error (ignored): {ex.Message}");
            }
        }
    }

    private static async Task<int[]> ReadPositionsAsync(EatTiltMotionController controller, string label) {
        var positions = await controller.QueryPositionsAsync(None);
        Assert.That(positions.Known, Is.True, $"'cp' at '{label}' should have returned known positions");
        var arr = positions.PerMotorSteps.ToArray();
        Progress($"    {label}: [{Join(arr)}]  (device order TR,TL,BR,BL)");
        return arr;
    }

    private static int[] Add(int[] a, int[] b) => a.Zip(b, (x, y) => x + y).ToArray();

    private static string Join(int[] a) => string.Join(", ", a);

    private static void ResetProgressLog() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(ProgressLogPath));
            File.WriteAllText(ProgressLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] --- run start ---{Environment.NewLine}");
        } catch { /* best-effort */ }
    }

    private static void Progress(string message) {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        TestContext.Progress.WriteLine(line);
        try {
            File.AppendAllText(ProgressLogPath, line + Environment.NewLine); // open+close flushes for live tailing
        } catch { /* best-effort */ }
    }

    /// <summary>Synchronous <see cref="IProgress{T}"/> so device progress lines are logged in order, inline.</summary>
    private sealed class ActionProgress : IProgress<string> {
        private readonly Action<string> report;
        public ActionProgress(Action<string> report) => this.report = report;
        public void Report(string value) => report(value);
    }
}
