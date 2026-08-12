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
using System.IO;

namespace TestApp {

    /// <summary>
    /// F15's policy, in one place and out of the WPF-bound runner so it can actually be tested: does
    /// <c>optimize</c> write its landing back into each run's OWN source folder, and what happens to whatever
    /// was already there?
    ///
    /// <para><b>The defect.</b> For thirteen waves <c>optimize --per-run</c> wrote
    /// <c>optimized_settings.json</c> into every discovered run's source folder as well as into <c>--out</c>,
    /// with no way to suppress it. It destroyed <c>bobp_m101</c>'s historical <c>sens 50 / clip 9.5</c> row
    /// mid-investigation, silently re-baselined BOTH banks in wave 3, and is the last thing forcing whole
    /// passes to be serialized against one another — two passes over the same bank collide IN THE BANK, however
    /// carefully their <c>--out</c> directories are kept apart.</para>
    ///
    /// <para><b>Why the capability is kept rather than deleted.</b> <c>bank-verify --opt-a/--opt-b</c> and
    /// <c>golden eval --params optimized</c> read the RUN FOLDER copy by default, and <c>review --runs
    /// &lt;same&gt;</c> auto-discovers it. Deleting the write would break those; making it opt-in turns a
    /// silent side effect into something a command line SAYS.</para>
    /// </summary>
    internal static class LandingWriteback {

        /// <summary>The opt-in flag. One definition, so the runner and its tests cannot disagree about the
        /// spelling — a flag that is tested under one name and shipped under another is a test of nothing.</summary>
        internal const string UpdateRunFolderFlag = "--update-run-folder";

        /// <summary>The one-generation backup <see cref="SnapshotExistingLanding"/> leaves behind. Deliberately
        /// NOT <c>optimized_settings.json</c> — every bank reader matches that name EXACTLY, so a backup sharing
        /// it would be read back as a landing.</summary>
        internal const string DisplacedLandingFileName = "optimized_settings.displaced.json";

        /// <summary>True only when the caller explicitly asked for the run-folder write. The default is "leave
        /// the bank exactly as you found it".</summary>
        internal static bool ShouldUpdateRunFolder(string[] args) {
            if (args == null) {
                return false;
            }
            foreach (var a in args) {
                if (string.Equals(a, UpdateRunFolderFlag, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Preserves a run folder's existing <c>optimized_settings.json</c> before it is overwritten — but only
        /// if nothing has been preserved there yet.
        ///
        /// <para><b>The backup keeps the OLDEST displaced landing, not the most recent one, and that is the
        /// point.</b> The file worth keeping is the one nobody can reproduce: a bank run's historical settings,
        /// written by a session whose command line no longer exists. Every landing written since is reproducible
        /// from a recorded command and survives in its own <c>--out</c> directory. A rolling backup would lose
        /// the irreplaceable file on the second pass and keep a reproducible one in its place.</para>
        ///
        /// <para>Returns the backup path when one was created; null when the target did not exist, when a backup
        /// was already present, or when the copy failed. All three are different facts and the caller reports
        /// which — an absent write has to be VISIBLE, because the whole defect was that it was not.</para>
        /// </summary>
        internal static string SnapshotExistingLanding(string landingPath) {
            try {
                if (string.IsNullOrEmpty(landingPath) || !File.Exists(landingPath)) {
                    return null;
                }
                var dir = Path.GetDirectoryName(landingPath);
                var backup = Path.Combine(dir ?? string.Empty, DisplacedLandingFileName);
                if (File.Exists(backup)) {
                    return null;   // the oldest displaced landing is already preserved; do not roll it forward
                }
                File.Copy(landingPath, backup);
                return backup;
            } catch (Exception ex) {
                // A backup failure must never cost the caller its landing — and must never pass unsaid either.
                Console.Error.WriteLine($"  WARNING: could not preserve the existing landing at {landingPath}: {ex.Message}");
                Logger.Error(ex, $"Failed to snapshot existing landing at {landingPath}");
                return null;
            }
        }
    }
}
