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
using System.Text;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// Text preparation for NINA's <c>MyMessageBox</c>. Its message <c>TextBlock</c> does not wrap, so the
    /// window simply grows to fit the longest line: a single long sentence (e.g. a device error quoting the
    /// failed command and its recovery advice) stretches the modal past the screen edge and clips its own text
    /// at BOTH ends, leaving the user unable to read the thing they are being asked to answer.
    ///
    /// <para>Hard-wrapping the message before it is handed to the dialog is the fix that is actually in this
    /// plugin's control. Wrap at the call site of every message box whose text is not a short, fixed
    /// string.</para>
    /// </summary>
    public static class DialogText {

        /// <summary>Conservative column count: comfortably readable, and narrow enough that the resulting modal fits a 1080p screen at typical DPI.</summary>
        public const int DefaultWrapColumns = 90;

        /// <summary>
        /// Hard-wraps <paramref name="text"/> so no line exceeds <paramref name="columns"/> characters, without
        /// reflowing the author's own structure: existing line breaks (e.g. the blank line between a failure
        /// description and the question that follows it) are preserved exactly, and only over-long lines are
        /// broken — at whitespace where possible. A single word longer than the limit (a path, a URL) is left
        /// intact on its own line rather than split, since breaking it would corrupt what it names.
        /// </summary>
        public static string Wrap(string text, int columns = DefaultWrapColumns) {
            if (string.IsNullOrEmpty(text) || columns <= 0) {
                return text;
            }

            var result = new StringBuilder(text.Length + 32);
            // Split on \n and keep any \r as part of the line so CRLF input round-trips unchanged.
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; ++i) {
                if (i > 0) {
                    result.Append('\n');
                }
                var line = lines[i];
                bool endsWithCr = line.EndsWith("\r", StringComparison.Ordinal);
                if (endsWithCr) {
                    line = line.Substring(0, line.Length - 1);
                }
                AppendWrappedLine(result, line, columns);
                if (endsWithCr) {
                    result.Append('\r');
                }
            }
            return result.ToString();
        }

        private static void AppendWrappedLine(StringBuilder result, string line, int columns) {
            if (line.Length <= columns) {
                result.Append(line);
                return;
            }

            foreach (var segment in WrapSegments(line, columns)) {
                result.Append(segment);
            }
        }

        private static IEnumerable<string> WrapSegments(string line, int columns) {
            int lineStart = 0;
            bool first = true;
            while (lineStart < line.Length) {
                int remaining = line.Length - lineStart;
                if (remaining <= columns) {
                    yield return (first ? string.Empty : "\n") + line.Substring(lineStart);
                    yield break;
                }

                // Break at the last space that fits; if there is none, the word itself is longer than the
                // limit, so take it whole (up to the next space) rather than splitting it mid-token.
                int breakAt = line.LastIndexOf(' ', lineStart + columns, columns);
                if (breakAt <= lineStart) {
                    breakAt = line.IndexOf(' ', lineStart);
                    if (breakAt < 0) {
                        yield return (first ? string.Empty : "\n") + line.Substring(lineStart);
                        yield break;
                    }
                }

                yield return (first ? string.Empty : "\n") + line.Substring(lineStart, breakAt - lineStart);
                first = false;
                lineStart = breakAt + 1; // Consume the space the break happened at.
            }
        }
    }
}
