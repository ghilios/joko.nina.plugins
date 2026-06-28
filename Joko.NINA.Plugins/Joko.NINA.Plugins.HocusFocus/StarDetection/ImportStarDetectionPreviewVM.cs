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
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// View model for the modal shown before applying an imported star-detection settings file. Presents the table of
    /// settings that will change (Setting / Current / Imported) and exposes the user's decision as an awaitable
    /// <see cref="Result"/> (true = apply, false = cancel). Mirrors the
    /// <see cref="NINA.Core.Utility.WindowService"/> + <c>RequestClose</c> pattern used by the other plugin dialogs.
    /// </summary>
    public sealed class ImportStarDetectionPreviewVM : BaseINPC, IDisposable {
        private readonly TaskCompletionSource<bool> tcs =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler RequestClose;

        public ImportStarDetectionPreviewVM(IReadOnlyList<StarDetectionSettingDiffRow> rows, string sourceSummary = null) {
            Rows = rows ?? Array.Empty<StarDetectionSettingDiffRow>();
            HeaderText = Rows.Count == 1
                ? "1 setting will change. Applying replaces your current star-detection settings on this profile."
                : $"{Rows.Count} settings will change. Applying replaces your current star-detection settings on this profile.";
            SourceSummary = sourceSummary;
            ApplyCommand = new RelayCommand(() => Finish(true));
            CancelCommand = new RelayCommand(() => Finish(false));
        }

        public IReadOnlyList<StarDetectionSettingDiffRow> Rows { get; }

        public string HeaderText { get; }

        /// <summary>Provenance line for the imported file (e.g. when it was exported and by which plugin version).</summary>
        public string SourceSummary { get; }

        public bool HasSourceSummary => !string.IsNullOrEmpty(SourceSummary);

        public ICommand ApplyCommand { get; }

        public ICommand CancelCommand { get; }

        /// <summary>Completes when the user clicks Apply (true) or Cancel / closes the window (false).</summary>
        public Task<bool> Result => tcs.Task;

        private void Finish(bool apply) {
            tcs.TrySetResult(apply);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() {
            // Window dismissed without an explicit choice (e.g. the X button) ⇒ treat as Cancel. TrySetResult is a
            // no-op if a button already set the result.
            tcs.TrySetResult(false);
        }
    }
}
