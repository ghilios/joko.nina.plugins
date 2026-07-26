#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles {

    /// <summary>
    /// A concrete, state-backed <see cref="IFocuserMediator"/> for the deterministic AutoFocus sweep harness. Unlike a
    /// bare NSubstitute focuser, this actually MOVES: <see cref="MoveFocuser(int, CancellationToken)"/> clamps the
    /// requested target to <c>[Min, Max]</c>, updates <see cref="Position"/>, and returns the (clamped) landing
    /// position — so the engine's "Focuser reached its limit" guard (which compares the returned position against the
    /// requested one) fires exactly when the travel range is exceeded. Every REQUESTED target (pre-clamp) is appended to
    /// <see cref="MoveHistory"/>, which is how Behavior B's reversal is asserted: a cap-out shows up as a return toward
    /// the start followed by moves in the opposite direction. Only the three members the engine actually calls —
    /// <see cref="GetInfo"/>, <see cref="MoveFocuser(int, CancellationToken)"/>, and <see cref="ToggleTempComp(bool)"/> —
    /// carry real behavior; the rest of the interface throws or no-ops.
    /// </summary>
    internal sealed class MovingFocuserMediator : IFocuserMediator {
        private readonly List<int> moveHistory = new List<int>();

        public MovingFocuserMediator(int start, int min = int.MinValue / 2, int max = int.MaxValue / 2) {
            if (min > max) {
                throw new ArgumentException($"min ({min}) must be <= max ({max})");
            }
            this.Min = min;
            this.Max = max;
            this.Position = Math.Min(max, Math.Max(min, start));
        }

        /// <summary>Current focuser position (already clamped into <c>[Min, Max]</c>).</summary>
        public int Position { get; private set; }

        /// <summary>Lower travel limit. A move below it clamps here, which trips the engine's limit guard.</summary>
        public int Min { get; }

        /// <summary>Upper travel limit. A move above it clamps here, which trips the engine's limit guard.</summary>
        public int Max { get; }

        /// <summary>Temperature reported by <see cref="GetInfo"/> (deterministic, never read for anything else here).</summary>
        public double Temperature { get; set; } = 20.0;

        /// <summary>
        /// Every REQUESTED move target, in call order (pre-clamp). Behavior B's one-shot reversal is asserted against
        /// this: the sweep caps out going one way, returns toward the start (initialFocusPosition), then explores the
        /// other direction.
        /// </summary>
        public IReadOnlyList<int> MoveHistory => moveHistory;

        public FocuserInfo GetInfo() => new FocuserInfo {
            Connected = true,
            Position = Position,
            Temperature = Temperature,
            IsMoving = false,
            TempComp = false,
            TempCompAvailable = false
        };

        public Task<int> MoveFocuser(int position, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            moveHistory.Add(position);
            Position = Math.Min(Max, Math.Max(Min, position));
            return Task.FromResult(Position);
        }

        // ToggleTempComp is gated behind TempCompAvailable (false here), so the engine never calls it — but keep it a
        // safe no-op rather than a throw in case a future caller does.
        public void ToggleTempComp(bool tempComp) { }

        // ---- Unreached on the region==null Run() path: fail loudly if the harness ever grows to touch them. --------

        public Task<int> MoveFocuserRelative(int position, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> MoveFocuserByTemperatureRelative(double temperature, double slope, CancellationToken ct) => throw new NotSupportedException();

        public void BroadcastSuccessfulAutoFocusRun(AutoFocusInfo info) { }

        public void BroadcastNewAutoFocusPoint(DataPoint dataPoint) { }

        public void BroadcastUserFocused(FocuserInfo info) { }

        public void BroadcastAutoFocusRunStarting() { }

        public void RegisterHandler(IFocuserVM handler) { }

        public void RegisterConsumer(IFocuserConsumer consumer) { }

        public void RemoveConsumer(IFocuserConsumer consumer) { }

        public Task<IList<string>> Rescan() => throw new NotSupportedException();

        public Task<bool> Connect() => throw new NotSupportedException();

        public Task Disconnect() => throw new NotSupportedException();

        public void Broadcast(FocuserInfo deviceInfo) { }

        public string Action(string actionName, string actionParameters) => throw new NotSupportedException();

        public string SendCommandString(string command, bool raw = true) => throw new NotSupportedException();

        public bool SendCommandBool(string command, bool raw = true) => throw new NotSupportedException();

        public void SendCommandBlind(string command, bool raw = true) => throw new NotSupportedException();

        public IDevice GetDevice() => throw new NotSupportedException();

        public event Func<object, EventArgs, Task> Connected { add { } remove { } }

        public event Func<object, EventArgs, Task> Disconnected { add { } remove { } }
    }
}
