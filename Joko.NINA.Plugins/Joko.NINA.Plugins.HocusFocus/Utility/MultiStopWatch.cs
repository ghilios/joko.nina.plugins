#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public class MultiStopWatch : IDisposable {
        private readonly Stopwatch stopWatch;
        private readonly string memberName;
        private readonly string filePath;
        private readonly List<Entry> entries;
        private readonly object lockObj = new object();

        private class Entry {
            public string Name;
            public TimeSpan Elapsed;
        }

        private MultiStopWatch(string memberName, string filePath) {
            this.memberName = memberName;
            this.filePath = filePath;
            this.entries = new List<Entry>();
            this.stopWatch = Stopwatch.StartNew();
        }

        private void Log() {
            var message = GenerateString();
            Debug.Print($"Method: {memberName}; File: {filePath} - {message}");
            Logger.Trace(message, memberName, filePath);
        }

        public string GenerateString() {
            lock (lockObj) {
                var sb = new StringBuilder();
                var totalElapsed = this.stopWatch.Elapsed;
                var previousElapsed = TimeSpan.Zero;
                var first = true;
                foreach (var entry in entries) {
                    var elapsed = entry.Elapsed - previousElapsed;
                    previousElapsed = entry.Elapsed;
                    if (!first) {
                        sb.Append(", ");
                    }
                    sb.Append($"{entry.Name}: {elapsed}");
                    first = false;
                }
                sb.Append($"; Elapsed: {totalElapsed}");

                return sb.ToString();
            }
        }

        void IDisposable.Dispose() {
            lock (lockObj) {
                this.stopWatch.Stop();
                Log();
            }
        }

        public void RecordEntry(string name) {
            lock (lockObj) {
                this.entries.Add(new Entry() { Name = name, Elapsed = stopWatch.Elapsed });
            }
        }

        public static MultiStopWatch Measure(
                [CallerMemberName] string memberName = "",
                [CallerFilePath] string filePath = "") {
            return new MultiStopWatch(memberName, filePath);
        }
    }
}