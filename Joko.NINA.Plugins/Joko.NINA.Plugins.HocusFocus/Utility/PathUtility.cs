#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public static class PathUtility {

        // https://stackoverflow.com/questions/275689/how-to-get-relative-path-from-absolute-path
        /// <summary>
        /// Creates a relative path from one file or folder to another.
        /// </summary>
        /// <param name="fromPath">Contains the directory that defines the start of the relative path.</param>
        /// <param name="toPath">Contains the path that defines the endpoint of the relative path.</param>
        /// <returns>The relative path from the start directory to the end path.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fromPath"/> or <paramref name="toPath"/> is <c>null</c>.</exception>
        /// <exception cref="UriFormatException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static string GetRelativePath(string fromPath, string toPath) {
            if (string.IsNullOrEmpty(fromPath)) {
                throw new ArgumentNullException("fromPath");
            }

            if (string.IsNullOrEmpty(toPath)) {
                throw new ArgumentNullException("toPath");
            }

            Uri fromUri = new Uri(AppendDirectorySeparatorChar(fromPath));
            Uri toUri = new Uri(AppendDirectorySeparatorChar(toPath));

            if (fromUri.Scheme != toUri.Scheme) {
                return toPath;
            }

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (string.Equals(toUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)) {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return relativePath;
        }

        /// <summary>
        /// The suffix appended to a target directory's name to form the default staging directory.
        /// </summary>
        public const string TempDirectorySuffix = ".hf-tmp";

        /// <summary>
        /// Writes text to <paramref name="path"/> atomically: the content is written to a temp file in a
        /// staging directory outside the target directory, which is then renamed into place, so a concurrent
        /// reader never observes a half-written file or an open write handle on the final path.
        /// </summary>
        /// <param name="path">The destination file.</param>
        /// <param name="contents">The content to write.</param>
        /// <param name="tempDirectory">
        /// Staging directory for the temp file. It MUST be on the same volume as <paramref name="path"/> for the
        /// rename to be atomic, and MUST NOT be the target directory itself (see remarks). Defaults to a sibling
        /// of the target directory named <c>&lt;targetDirName&gt;<see cref="TempDirectorySuffix"/></c>.
        /// </param>
        /// <remarks>
        /// Two separate hazards are in play, and both must be respected.
        /// <para>
        /// First: a plain <see cref="File.WriteAllText(string, string)"/> keeps a <see cref="FileAccess.Write"/>
        /// handle open on the destination. NINA core's <c>AutoFocusToolVM</c> watches the AutoFocus report
        /// directory and immediately <c>File.OpenText</c>s new reports with <see cref="FileShare.Read"/>; that
        /// share mode does not admit the still-open write access, so the read fails with a sharing violation
        /// ("being used by another process"). Staging the content in a temp file and renaming it into place means
        /// the final report never has an open write handle.
        /// </para>
        /// <para>
        /// Second: the staging file must live OUTSIDE the target directory. Windows reports an intra-directory
        /// rename as <c>FILE_ACTION_RENAMED_OLD_NAME</c>/<c>FILE_ACTION_RENAMED_NEW_NAME</c>, which .NET raises as
        /// <see cref="FileSystemWatcher.Renamed"/> — not <see cref="FileSystemWatcher.Created"/>. NINA's watcher
        /// subscribes only to Created and Deleted, so a same-directory rename publishes a report that NINA never
        /// notices until it restarts. Renaming in from another directory makes the destination observe
        /// <c>FILE_ACTION_ADDED</c>, which raises Created. Because the source and destination share a volume, the
        /// move remains a true atomic rename rather than a copy.
        /// </para>
        /// <para>
        /// Keeping the temp file out of the target directory also means a process crash between the write and the
        /// rename cannot strand a <c>.tmp</c> file where NINA's unfiltered
        /// <c>Directory.GetFiles(ReportDirectory)</c> would list it as a bogus chart.
        /// </para>
        /// </remarks>
        public static void WriteAllTextAtomic(string path, string contents, string tempDirectory = null) {
            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentNullException(nameof(path));
            }

            var fullPath = Path.GetFullPath(path);
            var targetDirectory = Path.GetDirectoryName(fullPath);
            var stagingDirectory = string.IsNullOrEmpty(tempDirectory)
                ? GetDefaultTempDirectory(targetDirectory)
                : tempDirectory;
            Directory.CreateDirectory(stagingDirectory);

            var tempPath = Path.Combine(
                stagingDirectory,
                Path.GetFileNameWithoutExtension(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                File.WriteAllText(tempPath, contents);
                // overwrite: a stale report from the same second (filenames are second-resolution) must not block it.
                File.Move(tempPath, fullPath, overwrite: true);
            } catch {
                // Best-effort cleanup so a failed rename doesn't strand a temp file; preserve the original error.
                try {
                    if (File.Exists(tempPath)) {
                        File.Delete(tempPath);
                    }
                } catch { }
                throw;
            }
        }

        /// <summary>
        /// A sibling of <paramref name="targetDirectory"/>, which is guaranteed to be on the same volume (same
        /// parent) and outside the target directory regardless of whether a watcher sets IncludeSubdirectories.
        /// Falls back to the target directory itself when it is a volume root and therefore has no sibling; in
        /// that degenerate case the write is still atomic but no Created event is raised.
        /// </summary>
        private static string GetDefaultTempDirectory(string targetDirectory) {
            var parent = Path.GetDirectoryName(targetDirectory);
            if (string.IsNullOrEmpty(parent)) {
                return targetDirectory;
            }

            return Path.Combine(parent, Path.GetFileName(targetDirectory) + TempDirectorySuffix);
        }

        private static string AppendDirectorySeparatorChar(string path) {
            // Append a slash only if the path is a directory and does not have a slash.
            if (!Path.HasExtension(path) &&
                !path.EndsWith(Path.DirectorySeparatorChar.ToString())) {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
        }
    }
}