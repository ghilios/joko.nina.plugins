#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Profile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestApp {

    /// <summary>
    /// Backfills the NINA-importable settings handoff into bank run folders that already hold a landing:
    /// <c>TestApp bank-export-settings --runs &lt;bank-root&gt; [--apply]</c>.
    ///
    /// <para><b>Why this exists rather than a re-run.</b> Every <c>optimize --per-run</c> pass writes
    /// <c>hocusfocus_star_detection.json</c> beside its landing, but folders optimized before that shipped have
    /// only the bare <c>optimized_settings.json</c> — which nothing in the plugin can read. The obvious route to
    /// fixing that is to re-optimize the bank, and it is the wrong one: per F15 <c>optimize --per-run</c> rewrites
    /// each run's stored settings, so re-running 39 folders to regenerate a derived file would re-baseline both
    /// banks as a side effect and cost hours. This converts what is already on disk, changes no landing, and runs
    /// in seconds.</para>
    ///
    /// <para><b>The filename is load-bearing.</b> It must never be <c>optimized_settings.json</c> —
    /// <c>golden eval --params optimized</c>, <c>bank-verify</c>, <c>review</c> and <c>inspect-align</c> match that
    /// name exactly and deserialize it as a bare <see cref="OptimizedStarDetectionSettings"/>. Handing them an
    /// envelope would bind every curated knob to its CLR default (Sensitivity 0, MinHFR 0, StructureLayers 0) and
    /// score a completely different detector with no error and no warning. Nor <c>metadata.json</c>, which is the
    /// AF-replay capture record the real bank genuinely has.</para>
    ///
    /// <para>Dry-run by default (lists what it would write); <c>--apply</c> writes. Existing handoffs are left
    /// alone unless <c>--overwrite</c> is given, so re-running after a fresh optimize pass cannot clobber a
    /// newer envelope with one rebuilt from an older landing.</para>
    /// </summary>
    public static class BankExportSettingsRunner {

        public static void Run(string[] args) {
            var runs = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runs) || !Directory.Exists(runs)) {
                Console.Error.WriteLine("Usage: TestApp bank-export-settings --runs <bank-root> [--apply] [--overwrite]");
                Environment.ExitCode = 2;
                return;
            }
            var apply = DiagnosticUtil.HasFlag(args, "--apply");
            var overwrite = DiagnosticUtil.HasFlag(args, "--overwrite");

            // The envelope's BASE is the harness's own star-detection options, exactly as the optimize path uses
            // (OptimizationDiagnosticRunner passes ctx.StarDetectionOptions): the landing supplies the curated
            // knobs, the base supplies every axis the optimizer does not search. A different base would produce an
            // envelope that imports a different detector than the one the landing describes.
            Logger.SetLogLevel(LogLevelEnum.INFO);
            var profileService = new ProfileService();
            profileService.TryLoad(DiagnosticUtil.GetArg(args, "--profile-id") ?? string.Empty);
            var activeProfile = profileService.ActiveProfile
                ?? throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id.");
            var harnessSettings = HarnessSettingsStore.Resolve(args, profileService, activeProfile);
            var starDetectionOptions = new StarDetectionOptions(profileService, harnessSettings.Accessor);

            // Enumerate by FILE, not by OptimizationRunDiscovery: a landing can sit in an --out directory that
            // holds no frames at all, and those are exactly the copies F15 says to prefer reading.
            var landings = Directory
                .EnumerateFiles(runs, "optimized_settings.json", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Console.WriteLine($"bank-export-settings: {landings.Count} landing(s) under {runs}  " +
                $"({(apply ? "APPLY — writing" : "DRY-RUN — listing only")}{(overwrite ? ", overwriting existing" : "")})");

            int written = 0, skipped = 0, failed = 0;
            var failures = new List<string>();
            foreach (var landingPath in landings) {
                var folder = Path.GetDirectoryName(landingPath);
                if (string.IsNullOrEmpty(folder)) {
                    continue;
                }
                var targetPath = Path.Combine(folder, OptimizationDiagnosticRunner.SettingsHandoffFileName);
                if (File.Exists(targetPath) && !overwrite) {
                    skipped++;
                    continue;
                }
                try {
                    var landing = JsonConvert.DeserializeObject<OptimizedStarDetectionSettings>(File.ReadAllText(landingPath));
                    if (landing == null) {
                        throw new InvalidOperationException("deserialized to null");
                    }
                    var export = StarDetectionSettingsExport.FromOptimizedLanding(starDetectionOptions, landing);

                    // Prove the round trip BEFORE writing: re-read what we are about to emit and confirm the
                    // curated axes survived. The DTO→options mapping is by reflected name, so a renamed or newly
                    // added axis fails here rather than silently importing a detector that differs from the
                    // landing — the same class of silent-substitution the filename constraint above guards.
                    var verify = StarDetectionSettingsExport.Deserialize(export.Serialize());
                    verify.Validate();
                    var mismatches = landing.DiffKnobs(verify.StarDetection);
                    if (mismatches.Count > 0) {
                        throw new InvalidOperationException("round-trip lost " + string.Join(", ", mismatches));
                    }

                    if (apply) {
                        File.WriteAllText(targetPath, export.Serialize());
                    }
                    written++;
                    Console.WriteLine($"  {(apply ? "wrote" : "would write")} {targetPath}");
                } catch (Exception ex) {
                    failed++;
                    failures.Add($"{landingPath}: {ex.Message}");
                    Console.Error.WriteLine($"  FAILED {landingPath}: {ex.Message}");
                }
            }

            Console.WriteLine($"bank-export-settings: {written} {(apply ? "written" : "to write")}, {skipped} already present, {failed} failed");
            if (failed > 0) {
                // A silent partial backfill is the failure mode worth being loud about: the point of the file is
                // that a folder can be replayed from, and a folder missing one is indistinguishable from a folder
                // that was never optimized.
                Environment.ExitCode = 1;
            }
        }
    }
}
