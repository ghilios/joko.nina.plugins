#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Profile;
using OpenCvSharp;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Logger = NINA.Core.Utility.Logger;

namespace TestApp {

    /// <summary>
    /// Writes a linear, mono, 16-bit FITS sidecar (<c>&lt;frame&gt;.linear.fits</c>) for every AF sweep frame in
    /// every discovered run, using NINA's own loader (<see cref="DiagnosticUtil.LoadDebayeredFloatMat"/>) — which
    /// reads BOTH FITS and XISF and debayers Bayered frames to luminance. This lets the detector-independent golden
    /// reference (<c>tools/golden/snr_ref.py</c>, which only parses mono FITS) cover the bank's XISF and Bayered
    /// runs, not just the mono-FITS ones. "Linear" means no MTF/auto-stretch — it does NOT mean no debayer: a
    /// reference computed on a Bayer mosaic while HocusFocus detects on luminance scores every mosaic-only find as
    /// an HF recall gap HF could never close.
    ///
    /// <para><b>Deliberately NOT CFA hotpixel filtered</b>, unlike the detector's own OSC path. The reference's
    /// value is having blind spots that differ from the detector's, and hot-pixel rejection is already assigned to
    /// the LLM montage QA step (<c>.claude/docs/golden-star-set.md</c>). So the export shares the detector's
    /// DEBAYER but not its filtering, which is exactly what <c>LoadDebayeredFloatMat</c> is.</para>
    ///
    /// <para>Idempotent: skips frames whose sidecar already exists unless <c>--overwrite</c>. Read-only on the
    /// source frames. Regenerate the sidecars for the four bayered bank runs — they were exported from the
    /// mosaic before this was fixed.</para>
    /// </summary>
    public static class ExportLinearRunner {

        public static async Task Run(string[] args) {
            var runs = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runs) || !Directory.Exists(runs)) {
                Console.Error.WriteLine("Usage: TestApp export-linear --runs <bank-root> [--profile-id <guid>] [--overwrite]");
                Environment.ExitCode = 2;
                return;
            }
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var overwrite = DiagnosticUtil.HasFlag(args, "--overwrite");
            Logger.SetLogLevel(LogLevelEnum.INFO);
            if (Application.Current == null) {
                new Application();
            }
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            if (profileService.ActiveProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id.");
            }

            var discovery = OptimizationRunDiscovery.Discover(runs);
            Console.WriteLine($"export-linear: {discovery.Runs.Count} run(s) under {runs}  ({(overwrite ? "overwrite" : "skip-existing")})");
            int written = 0, skipped = 0, failed = 0;
            foreach (var run in discovery.Runs) {
                Console.WriteLine($"  [{run.RunId}] {run.Frames.Count} frame(s)");
                foreach (var frame in run.Frames.OrderBy(f => f.FocuserPosition)) {
                    var outPath = frame.Path + ".linear.fits";
                    if (!overwrite && File.Exists(outPath)) {
                        skipped++;
                        continue;
                    }
                    try {
                        using var mat = await DiagnosticUtil.LoadDebayeredFloatMat(frame.Path, profileService);
                        WriteMonoFits16(outPath, mat);
                        written++;
                        Console.WriteLine($"      Focuser {frame.FocuserPosition}: {mat.Width}x{mat.Height} -> {Path.GetFileName(outPath)}");
                    } catch (Exception ex) {
                        failed++;
                        Console.WriteLine($"      Focuser {frame.FocuserPosition}: FAILED ({ex.GetType().Name}: {ex.Message})");
                    }
                }
            }
            Console.WriteLine($"export-linear: wrote {written}, skipped {skipped} existing, {failed} failed.");
        }

        /// <summary>Writes a minimal standard FITS (BITPIX=16, BZERO=32768 unsigned convention, big-endian data)
        /// from a CV_32F [0,1] Mat scaled to [0,65535] — exactly the layout tools/golden/snr_ref.py parses
        /// (reads '>i2', applies BSCALE*value+BZERO). Header + data are zero-padded to 2880-byte FITS blocks.</summary>
        private static void WriteMonoFits16(string path, Mat mat32f) {
            int w = mat32f.Width, h = mat32f.Height;
            // [0,1] float -> [0,65535] ushort -> store as int16 with BZERO=32768 (val - 32768), big-endian.
            var bytes = new byte[w * h * 2];
            unsafe {
                var src = (float*)mat32f.DataPointer;
                long n = (long)w * h;
                for (long i = 0; i < n; ++i) {
                    var v = src[i];
                    int u = (int)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 65535f);
                    short s = (short)(u - 32768);          // unsigned->signed via BZERO offset
                    bytes[i * 2] = (byte)((s >> 8) & 0xFF); // big-endian (FITS)
                    bytes[i * 2 + 1] = (byte)(s & 0xFF);
                }
            }
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            WriteHeader(fs, w, h);
            fs.Write(bytes, 0, bytes.Length);
            PadToBlock(fs);
        }

        private static void WriteHeader(FileStream fs, int w, int h) {
            var sb = new System.Text.StringBuilder();
            void Card(string s) => sb.Append(s.PadRight(80).Substring(0, 80));
            Card("SIMPLE  =                    T / HocusFocus linear export");
            Card("BITPIX  =                   16");
            Card("NAXIS   =                    2");
            Card($"NAXIS1  = {w,20}");
            Card($"NAXIS2  = {h,20}");
            Card("BZERO   =                32768");
            Card("BSCALE  =                    1");
            Card("END");
            var header = sb.ToString();
            int pad = (2880 - (header.Length % 2880)) % 2880;
            header += new string(' ', pad);
            var hb = System.Text.Encoding.ASCII.GetBytes(header);
            fs.Write(hb, 0, hb.Length);
        }

        private static void PadToBlock(FileStream fs) {
            int rem = (int)(fs.Position % 2880);
            if (rem != 0) {
                fs.Write(new byte[2880 - rem], 0, 2880 - rem);
            }
        }
    }
}
