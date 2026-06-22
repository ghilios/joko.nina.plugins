#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay {

    /// <summary>
    /// View model for the modal shown when replaying a saved AutoFocus run that has a <c>metadata.json</c>. Presents
    /// the three choices (use current settings / use the run's capture-time settings in memory / update the profile to
    /// the capture-time settings) plus cancel, and exposes the pick as an awaitable <see cref="Choice"/> so the
    /// (possibly background-thread) replay can block on the user's decision. Mirrors the
    /// <see cref="NINA.Core.Utility.WindowService"/> + <c>RequestClose</c> pattern used by the other plugin dialogs.
    /// </summary>
    public sealed class ReplaySettingsPromptVM : BaseINPC, IDisposable {
        private readonly TaskCompletionSource<ReplaySettingsChoice> tcs =
            new TaskCompletionSource<ReplaySettingsChoice>(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler RequestClose;

        public string CaptureSummary { get; }

        public ICommand UseCurrentCommand { get; }
        public ICommand UseCaptureInMemoryCommand { get; }
        public ICommand UpdateProfileCommand { get; }
        public ICommand CloseCommand { get; }

        /// <summary>Completes when the user picks an option (or cancels / closes the window).</summary>
        public Task<ReplaySettingsChoice> Choice => tcs.Task;

        public ReplaySettingsPromptVM(AutoFocusReplayMetadata metadata) {
            CaptureSummary = BuildSummary(metadata);
            UseCurrentCommand = new RelayCommand(() => Pick(ReplaySettingsChoice.UseCurrentSettings));
            UseCaptureInMemoryCommand = new RelayCommand(() => Pick(ReplaySettingsChoice.UseCaptureTimeSettingsInMemory));
            UpdateProfileCommand = new RelayCommand(() => Pick(ReplaySettingsChoice.UpdateProfileToCaptureTime));
            CloseCommand = new RelayCommand(() => Pick(ReplaySettingsChoice.Cancel));
        }

        private void Pick(ReplaySettingsChoice choice) {
            tcs.TrySetResult(choice);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private static string BuildSummary(AutoFocusReplayMetadata m) {
            if (m == null) {
                return string.Empty;
            }
            var when = m.CreatedAtUtc == default(DateTime) ? "unknown time" : m.CreatedAtUtc.ToLocalTime().ToString("g");
            var method = m.AutoFocus?.AutoFocusMethod.ToString() ?? "?";
            var regionCount = m.Regions?.Regions?.Count ?? 0;
            var regionText = regionCount == 1 ? "1 region" : $"{regionCount} regions";
            return $"Captured {when}  ·  method {method}  ·  {regionText}";
        }

        public void Dispose() {
            // Window dismissed without an explicit pick (e.g. the X button) ⇒ treat as Cancel. TrySetResult is a no-op
            // if a button already set the result.
            tcs.TrySetResult(ReplaySettingsChoice.Cancel);
        }
    }
}
