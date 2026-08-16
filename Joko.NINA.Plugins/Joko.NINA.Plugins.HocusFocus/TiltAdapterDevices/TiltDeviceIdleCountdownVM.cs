#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices {

    /// <summary>
    /// Renders the tilt adapter's idle auto-disconnect countdown as an inline banner, replacing the modal that
    /// used to ask whether to disconnect.
    ///
    /// <para>This VM only DISPLAYS. <see cref="TiltDeviceConnectionService"/> owns the deadline and fires the
    /// disconnect from its own timer, so hardware behavior never depends on the UI thread being responsive or on
    /// any particular panel being alive. The per-second tick here exists purely so the number moves; it reads the
    /// remaining time from the service, which computes it from the same clock the fire check uses, so the display
    /// and the disconnect cannot disagree.</para>
    ///
    /// <para>One instance is shared by every panel that shows tilt-device state, so whichever panel the user is
    /// actually looking at shows the same countdown from one source of truth.</para>
    /// </summary>
    public class TiltDeviceIdleCountdownVM : BaseINPC, IDisposable {
        private readonly TiltDeviceConnectionService service;
        private readonly IApplicationDispatcher applicationDispatcher;
        private DispatcherTimer timer;
        private bool disposed;

        public TiltDeviceIdleCountdownVM(TiltDeviceConnectionService service, IApplicationDispatcher applicationDispatcher = null) {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.applicationDispatcher = applicationDispatcher;
            StayConnectedCommand = new RelayCommand(() => this.service.KeepConnectedResetIdle());
            this.service.PropertyChanged += Service_PropertyChanged;
        }

        /// <summary>Whether the banner is shown at all.</summary>
        public bool IsVisible => service.IdleDisconnectPending;

        /// <summary>
        /// True once the countdown has run out and the disconnect is in flight. The service fires on its own 5 s
        /// tick, so the disconnect actually happens 0–5 s after this reads zero — showing "Disconnecting…" rather
        /// than a number keeps the banner from claiming an instant it cannot promise.
        /// </summary>
        public bool IsDisconnecting => IsVisible && service.IdleDisconnectRemaining <= TimeSpan.Zero;

        public string HeadlineText {
            get {
                if (!IsVisible) {
                    return string.Empty;
                }
                if (IsDisconnecting) {
                    return "Tilt adapter idle — disconnecting…";
                }
                var remaining = service.IdleDisconnectRemaining;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Tilt adapter idle for {0:0} minutes — disconnecting in {1}",
                    TiltDeviceConnectionService.IdleTimeout.TotalMinutes,
                    FormatRemaining(remaining));
            }
        }

        public string DetailText => "Moving the adapter or starting a run keeps it connected.";

        public bool CanStayConnected => IsVisible && !IsDisconnecting;

        public ICommand StayConnectedCommand { get; }

        internal static string FormatRemaining(TimeSpan remaining) {
            if (remaining < TimeSpan.Zero) {
                remaining = TimeSpan.Zero;
            }
            // Round UP so the banner never shows 0:00 while there is still time left on the clock.
            var totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
            return string.Format(CultureInfo.CurrentCulture, "{0}:{1:D2}", totalSeconds / 60, totalSeconds % 60);
        }

        private void Service_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(TiltDeviceConnectionService.IdleDisconnectPending)) {
                return;
            }
            // Arrives on the service's timer thread. Post rather than block: the non-blocking dispatch is the
            // house rule for anything reachable from that thread, and a command requery off the UI thread throws.
            PostToUiThread(() => {
                if (service.IdleDisconnectPending) {
                    StartTimer();
                } else {
                    StopTimer();
                }
                RaiseAll();
            });
        }

        private void StartTimer() {
            if (disposed) {
                return;
            }
            if (timer == null) {
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (_, __) => RaiseAll();
            }
            timer.Start();
        }

        private void StopTimer() => timer?.Stop();

        private void RaiseAll() {
            RaisePropertyChanged(nameof(IsVisible));
            RaisePropertyChanged(nameof(IsDisconnecting));
            RaisePropertyChanged(nameof(HeadlineText));
            RaisePropertyChanged(nameof(CanStayConnected));
        }

        private void PostToUiThread(Action action) {
            if (applicationDispatcher == null) {
                action();
                return;
            }
            applicationDispatcher.PostSynchronizationContext(action);
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            service.PropertyChanged -= Service_PropertyChanged;
            StopTimer();
            timer = null;
        }
    }
}
