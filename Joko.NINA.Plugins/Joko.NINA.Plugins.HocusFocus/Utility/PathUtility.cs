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
        /// Writes text to <paramref name="path"/> atomically: the content is written to a sibling temp file which
        /// is then renamed into place, so a concurrent reader never observes a half-written file or an open write
        /// handle on the final path.
        /// </summary>
        /// <remarks>
        /// A plain <see cref="File.WriteAllText(string, string)"/> keeps a <see cref="FileAccess.Write"/> handle
        /// open on the destination while serializing. NINA core's <c>AutoFocusToolVM.LoadChart</c> watches the
        /// AutoFocus report directory and immediately <c>File.OpenText</c>s new reports with
        /// <see cref="FileShare.Read"/>; that share mode does not admit the still-open write access, so the read
        /// fails with a sharing violation ("being used by another process"). Writing a temp file and renaming means
        /// the final report never has an open write handle, eliminating the race. The temp file carries a
        /// non-<c>.json</c> extension so a directory watcher filtering on the real extension does not wake on it.
        /// </remarks>
        public static void WriteAllTextAtomic(string path, string contents) {
            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentNullException(nameof(path));
            }

            var directory = Path.GetDirectoryName(path);
            // The temp file MUST live in the same directory (volume) as the target so File.Move is a true atomic
            // rename rather than a copy+delete.
            var tempPath = Path.Combine(directory ?? string.Empty, Path.GetFileNameWithoutExtension(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                File.WriteAllText(tempPath, contents);
                // overwrite: a stale report from the same second (filenames are second-resolution) must not block it.
                File.Move(tempPath, path, overwrite: true);
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