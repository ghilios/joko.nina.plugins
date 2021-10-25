using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Joko.NINA.Plugins.HocusFocus.Utility {
    public class MultiStopWatch : IDisposable {
        private Stopwatch stopWatch;
        private string memberName;
        private string filePath;
        private List<Entry> entries;

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

            var message = sb.ToString();
            Debug.Print($"Method: {memberName}; File: {filePath} - {message}");
            Logger.Trace(message, memberName, filePath);
        }

        void IDisposable.Dispose() {
            this.stopWatch.Stop();
            Log();
        }

        public void RecordEntry(string name) {
            this.entries.Add(new Entry() { Name = name, Elapsed = stopWatch.Elapsed });
        }

        public static MultiStopWatch Measure(
                [CallerMemberName] string memberName = "",
                [CallerFilePath] string filePath = "") {
            return new MultiStopWatch(memberName, filePath);
        }
    }
}
