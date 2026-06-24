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
using System.IO;
using System.Linq;

namespace TestApp {

    /// <summary>
    /// Step 1 of the AF-bank verification: idempotent, dry-run-first cleanup of stale per-config clutter in each
    /// discovered run folder (star_detection_result JSONs, diagnostic PNGs, stray optimize/eval/contamination
    /// outputs, optimized_settings handoffs). KEEPS the AF frames, autofocus_report_Region*.json, labels/, our
    /// *.golden.json sidecars, run_meta.json and *.linear.fits exports. Classification is the pure-logic
    /// <see cref="BankCleanup.Classify"/> (unit-tested); this runner just enumerates and lists/deletes.
    /// Default lists only; <c>--apply</c> deletes (logging every removal). Read-only on the NINA profile.
    /// </summary>
    public static class BankCleanRunner {

        public static void Run(string[] args) {
            var runs = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runs) || !Directory.Exists(runs)) {
                Console.Error.WriteLine("Usage: TestApp bank-clean --runs <bank-root> [--apply]");
                Environment.ExitCode = 2;
                return;
            }
            var apply = DiagnosticUtil.HasFlag(args, "--apply");
            var discovery = OptimizationRunDiscovery.Discover(runs);
            if (discovery.Runs.Count == 0) {
                Console.WriteLine($"No runs discovered under {runs}.");
                return;
            }
            Console.WriteLine($"bank-clean: {discovery.Runs.Count} run(s) under {runs}  ({(apply ? "APPLY — deleting" : "DRY-RUN — listing only")})");

            long totalDelete = 0, totalBytes = 0;
            foreach (var run in discovery.Runs) {
                var runFolder = Path.GetDirectoryName(run.Frames[0].Path);
                if (runFolder == null || !Directory.Exists(runFolder)) {
                    continue;
                }
                var toDelete = new List<(string path, string reason, long bytes)>();
                foreach (var file in Directory.EnumerateFiles(runFolder, "*", SearchOption.AllDirectories)) {
                    var rel = Path.GetRelativePath(runFolder, file).Replace(Path.DirectorySeparatorChar, '/');
                    var decision = BankCleanup.Classify(rel);
                    if (!decision.Keep) {
                        long bytes = 0;
                        try { bytes = new FileInfo(file).Length; } catch { /* best effort */ }
                        toDelete.Add((file, decision.Reason, bytes));
                    }
                }
                if (toDelete.Count == 0) {
                    Console.WriteLine($"  [{run.RunId}] clean (nothing stale)");
                    continue;
                }
                Console.WriteLine($"  [{run.RunId}] {toDelete.Count} stale file(s):");
                foreach (var (path, reason, bytes) in toDelete) {
                    var rel = Path.GetRelativePath(runFolder, path).Replace(Path.DirectorySeparatorChar, '/');
                    totalDelete++;
                    totalBytes += bytes;
                    if (apply) {
                        try {
                            File.Delete(path);
                            Console.WriteLine($"      DELETED {rel}  ({reason}, {bytes} B)");
                        } catch (Exception ex) {
                            Console.WriteLine($"      FAILED  {rel}  ({ex.GetType().Name}: {ex.Message})");
                        }
                    } else {
                        Console.WriteLine($"      would delete {rel}  ({reason}, {bytes} B)");
                    }
                }
            }
            Console.WriteLine($"bank-clean: {(apply ? "deleted" : "would delete")} {totalDelete} file(s), {totalBytes / 1024.0:F1} KiB.");
        }
    }
}
