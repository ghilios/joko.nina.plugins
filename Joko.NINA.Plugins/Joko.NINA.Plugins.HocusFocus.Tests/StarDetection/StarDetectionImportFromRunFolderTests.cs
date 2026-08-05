#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Importing the settings handoff a previous optimize pass left beside a run's frames — the EXPLICIT,
    /// user-pressed alternative to auto-pickup. Auto-pickup was rejected because the wizard's baseline is the
    /// live profile: <c>baselineJ</c> and the headline improvement percentage are both measured against it, so a
    /// run folder silently becoming "Current" would change what that percentage means with nothing on screen to
    /// say so.
    /// </summary>
    [TestFixture]
    public class StarDetectionImportFromRunFolderTests {

        private string tempRoot;

        [SetUp]
        public void SetUp() {
            tempRoot = Path.Combine(Path.GetTempPath(), "hf_runfolder_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown() {
            try {
                if (Directory.Exists(tempRoot)) {
                    Directory.Delete(tempRoot, recursive: true);
                }
            } catch { /* best effort */ }
        }

        private static StarDetectionOptions NewOptions() =>
            new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

        private static void WriteHandoff(string folder, double sensitivity) {
            Directory.CreateDirectory(folder);
            var landing = new OptimizedStarDetectionSettings {
                BrightnessSensitivity = sensitivity,
                StarClippingMultiplier = 2.0,
                MinHFR = 0.7,
                StructureLayers = 6
            };
            var export = StarDetectionSettingsExport.FromOptimizedLanding(NewOptions(), landing);
            File.WriteAllText(
                Path.Combine(folder, StarDetectionSettingsIO.RunFolderSettingsFileName), export.Serialize());
        }

        [Test]
        public async Task Import_OnApply_LoadsTheRunFoldersLanding() {
            var runFolder = Path.Combine(tempRoot, "attempt01");
            WriteHandoff(runFolder, sensitivity: 21.5);

            var options = NewOptions();
            options.BrightnessSensitivity = 2.0;

            IReadOnlyList<StarDetectionSettingDiffRow> shownRows = null;
            await StarDetectionSettingsIO.ImportFromRunFolderAsync(runFolder, options, (rows, _) => {
                shownRows = rows;
                return Task.FromResult(true);
            });

            Assert.Multiple(() => {
                Assert.That(shownRows, Is.Not.Empty, "the diff must be shown before anything is applied");
                Assert.That(options.BrightnessSensitivity, Is.EqualTo(21.5));
            });
        }

        /// <summary>The property the whole "explicit button" decision rests on: declining changes nothing.</summary>
        [Test]
        public async Task Import_OnCancel_LeavesCurrentSettingsUntouched() {
            var runFolder = Path.Combine(tempRoot, "attempt01");
            WriteHandoff(runFolder, sensitivity: 21.5);

            var options = NewOptions();
            options.BrightnessSensitivity = 2.0;

            await StarDetectionSettingsIO.ImportFromRunFolderAsync(runFolder, options, (_, __) => Task.FromResult(false));

            Assert.That(options.BrightnessSensitivity, Is.EqualTo(2.0));
        }

        /// <summary>A run that was never optimized has no handoff. That must be a quiet no-op, and above all it
        /// must never prompt — a confirmation dialog for a file that does not exist is worse than silence.</summary>
        [Test]
        public async Task Import_WithNoHandoffPresent_NeverPrompts() {
            var runFolder = Path.Combine(tempRoot, "attempt01");
            Directory.CreateDirectory(runFolder);

            var options = NewOptions();
            options.BrightnessSensitivity = 2.0;
            var prompted = false;

            await StarDetectionSettingsIO.ImportFromRunFolderAsync(runFolder, options, (_, __) => {
                prompted = true;
                return Task.FromResult(true);
            });

            Assert.Multiple(() => {
                Assert.That(prompted, Is.False);
                Assert.That(options.BrightnessSensitivity, Is.EqualTo(2.0));
            });
        }

        /// <summary>Frame folder first, then the run root: an <c>optimize --per-run</c> pass writes beside the
        /// frames and a joint pass writes at the root, mirroring the AF-replay metadata convention.</summary>
        [Test]
        public void Resolve_PrefersTheFrameFolderOverTheRunRoot() {
            var runFolder = Path.Combine(tempRoot, "attempt01");
            WriteHandoff(runFolder, sensitivity: 21.5);
            WriteHandoff(tempRoot, sensitivity: 9.9);

            var resolved = StarDetectionSettingsIO.ResolveRunFolderSettings(runFolder);

            Assert.That(Path.GetDirectoryName(resolved), Is.EqualTo(runFolder));
        }

        [Test]
        public void Resolve_FallsBackToTheRunRoot() {
            var runFolder = Path.Combine(tempRoot, "attempt01");
            Directory.CreateDirectory(runFolder);
            WriteHandoff(tempRoot, sensitivity: 9.9);

            var resolved = StarDetectionSettingsIO.ResolveRunFolderSettings(runFolder);

            Assert.That(Path.GetDirectoryName(resolved), Is.EqualTo(tempRoot));
        }

        /// <summary>A corrupt or wrong-type file must not prompt and must not apply. The envelope's own
        /// <c>fileType</c> discriminator is what separates it from an AF-replay <c>metadata.json</c>, which has
        /// the same <c>schemaVersion</c> + <c>starDetection</c> shape and would otherwise deserialize cleanly.</summary>
        [Test]
        public async Task Import_WithAWrongTypeFile_NeverPromptsAndChangesNothing() {
            var runFolder = Path.Combine(tempRoot, "attempt01");
            Directory.CreateDirectory(runFolder);
            File.WriteAllText(
                Path.Combine(runFolder, StarDetectionSettingsIO.RunFolderSettingsFileName),
                "{\"schemaVersion\":1,\"starDetection\":{\"brightnessSensitivity\":99.0}}");

            var options = NewOptions();
            options.BrightnessSensitivity = 2.0;
            var prompted = false;

            await StarDetectionSettingsIO.ImportFromRunFolderAsync(runFolder, options, (_, __) => {
                prompted = true;
                return Task.FromResult(true);
            });

            Assert.Multiple(() => {
                Assert.That(prompted, Is.False, "a file lacking the fileType discriminator must be rejected");
                Assert.That(options.BrightnessSensitivity, Is.EqualTo(2.0));
            });
        }
    }
}
