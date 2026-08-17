using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Export/Import of the per-filter auto-focus SWEEP GEOMETRY through the settings file, over the production
    /// wiring (real options, real store, real edit binder — no substituted store), because the bug this covers was
    /// exactly a gap between those parts: the geometry is stored per filter and shown on the settings page, but the
    /// file the Export button writes did not contain it, so it silently did not survive the trip to the imaging
    /// machine.
    /// </summary>
    [TestFixture]
    public class StarDetectionSettingsIOSweepGeometryTests {

        private sealed class Harness {
            public IProfileService ProfileService;
            public StarDetectionOptions Buffer;
            public PerFilterStarDetectionStore Store;
            public PerFilterEditBinder Binder;
            public string CurrentFilterName = "Ha";

            /// <summary>The geometry target the IO layer is handed: null models per-filter mode being off.</summary>
            public PerFilterEditBinder GeometryTarget => Store.Enabled ? Binder : null;
        }

        private static Harness Build(bool enabled = true, int profileStepSize = 100, int profileOffsetSteps = 4) {
            var filters = new ObserveAllCollection<FilterInfo>() {
                new FilterInfo() { Name = "L" },
                new FilterInfo() { Name = "Ha" }
            };
            var profile = Substitute.For<IProfile>();
            profile.FilterWheelSettings.FilterWheelFilters.Returns(filters);
            profile.FocuserSettings.AutoFocusStepSize.Returns(profileStepSize);
            profile.FocuserSettings.AutoFocusInitialOffsetSteps.Returns(profileOffsetSteps);
            var profileService = Substitute.For<IProfileService>();
            profileService.ActiveProfile.Returns(profile);

            var h = new Harness() { ProfileService = profileService };
            // Production construction order: options -> store -> binder.
            h.Buffer = new StarDetectionOptions(profileService, new InMemoryPluginOptionsAccessor());
            h.Store = new PerFilterStarDetectionStore(
                profileService, new InMemoryPluginOptionsAccessor(), () => StarDetectionSettingsSnapshot.FromOptions(h.Buffer));
            h.Binder = new PerFilterEditBinder(h.Store, h.Buffer, profileService, () => h.CurrentFilterName);
            h.Store.Enabled = enabled;
            return h;
        }

        // A file on disk, so the import path under test is the same one the Import button drives.
        private static string WriteExportFile(StarDetectionSettingsExport export) {
            var path = Path.Combine(Path.GetTempPath(), "HFExportIO_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, export.Serialize());
            return path;
        }

        private static StarDetectionSettingsExport ExportWith(
                double brightnessSensitivity, PerFilterSweepGeometry geometry, string filterName = "Ha") {
            var source = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            source.UseAdvanced = true;
            source.BrightnessSensitivity = brightnessSensitivity;
            return StarDetectionSettingsExport.FromOptions(source, filterName, geometry);
        }

        // --- Export -----------------------------------------------------------------------------------------

        [Test]
        public void BuildExport_PerFilterOn_CarriesTheEditedFiltersGeometryAndName() {
            var h = Build();
            h.Binder.SweepStepSizeOverride = 25;
            h.Binder.SweepOffsetStepsOverride = 8;

            var export = StarDetectionSettingsIO.BuildExport(h.Buffer, h.GeometryTarget);

            Assert.Multiple(() => {
                Assert.That(export.FilterName, Is.EqualTo("Ha"));
                Assert.That(export.SweepGeometry, Is.Not.Null);
                Assert.That(export.SweepGeometry.StepSize, Is.EqualTo(25));
                Assert.That(export.SweepGeometry.InitialOffsetSteps, Is.EqualTo(8));
            });
        }

        [Test]
        public void BuildExport_PerFilterOn_PartialOverride_CarriesTheOneThatIsSet() {
            var h = Build();
            h.Binder.SweepStepSizeOverride = 25;

            var export = StarDetectionSettingsIO.BuildExport(h.Buffer, h.GeometryTarget);

            Assert.Multiple(() => {
                Assert.That(export.SweepGeometry.StepSize, Is.EqualTo(25));
                Assert.That(export.SweepGeometry.InitialOffsetSteps, Is.EqualTo(PerFilterSweepGeometry.Inherit),
                    "the two fields resolve independently");
            });
        }

        [Test]
        public void BuildExport_PerFilterOn_NoOverride_StillRecordsThatTheFilterInherits() {
            var h = Build();

            var export = StarDetectionSettingsIO.BuildExport(h.Buffer, h.GeometryTarget);

            Assert.Multiple(() => {
                Assert.That(export.SweepGeometry, Is.Not.Null);
                Assert.That(export.SweepGeometry.IsUnset, Is.True);
            });
        }

        [Test]
        public void BuildExport_PerFilterOff_CarriesNoGeometryAndNoFilterName() {
            var h = Build(enabled: false);
            Assume.That(h.GeometryTarget, Is.Null);

            var export = StarDetectionSettingsIO.BuildExport(h.Buffer, h.GeometryTarget);

            Assert.Multiple(() => {
                Assert.That(export.SweepGeometry, Is.Null);
                Assert.That(export.FilterName, Is.Null);
                // The detection settings themselves are exported exactly as before.
                Assert.That(export.StarDetection, Is.Not.Null);
            });
        }

        // --- Import -----------------------------------------------------------------------------------------

        [Test]
        public async Task ImportAsync_AppliesTheFilesGeometryToTheEditedFilter() {
            var h = Build();
            var path = WriteExportFile(ExportWith(8.4, new PerFilterSweepGeometry() { StepSize = 25, InitialOffsetSteps = 8 }));
            try {
                var applied = await StarDetectionSettingsIO.ImportAsync(
                    h.Buffer, path, (rows, summary) => Task.FromResult(true), h.GeometryTarget);

                var stored = h.Store.GetSweepGeometry("Ha");
                Assert.Multiple(() => {
                    Assert.That(applied, Is.True);
                    Assert.That(stored.StepSize, Is.EqualTo(25));
                    Assert.That(stored.InitialOffsetSteps, Is.EqualTo(8));
                    // The binder's bound properties follow, so the settings page shows the imported numbers.
                    Assert.That(h.Binder.SweepStepSizeOverride, Is.EqualTo(25));
                    Assert.That(h.Binder.SweepOffsetStepsOverride, Is.EqualTo(8));
                    // And the detection settings still import.
                    Assert.That(h.Buffer.BrightnessSensitivity, Is.EqualTo(8.4));
                    // Only the edited filter is touched.
                    Assert.That(h.Store.GetSweepGeometry("L").IsUnset, Is.True);
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_ShowsTheGeometryChangeInTheConfirmation() {
            var h = Build(profileStepSize: 100, profileOffsetSteps: 4);
            var path = WriteExportFile(ExportWith(8.4, new PerFilterSweepGeometry() { StepSize = 25, InitialOffsetSteps = 8 }));
            try {
                IReadOnlyList<StarDetectionSettingDiffRow> shownRows = null;
                await StarDetectionSettingsIO.ImportAsync(h.Buffer, path, (rows, summary) => {
                    shownRows = rows;
                    return Task.FromResult(false);
                }, h.GeometryTarget);

                var byName = shownRows.ToDictionary(r => r.Name);
                Assert.Multiple(() => {
                    Assert.That(byName.ContainsKey("Auto-Focus Step Size"), Is.True);
                    Assert.That(byName["Auto-Focus Step Size"].CurrentValue, Is.EqualTo("inherit (100)"));
                    Assert.That(byName["Auto-Focus Step Size"].NewValue, Is.EqualTo("25"));
                    Assert.That(byName.ContainsKey("Auto-Focus Offset Steps"), Is.True);
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_OnCancel_LeavesTheStoredGeometryUntouched() {
            var h = Build();
            h.Binder.SweepStepSizeOverride = 30;
            var path = WriteExportFile(ExportWith(8.4, new PerFilterSweepGeometry() { StepSize = 25, InitialOffsetSteps = 8 }));
            try {
                var applied = await StarDetectionSettingsIO.ImportAsync(
                    h.Buffer, path, (rows, summary) => Task.FromResult(false), h.GeometryTarget);

                Assert.Multiple(() => {
                    Assert.That(applied, Is.False);
                    Assert.That(h.Store.GetSweepGeometry("Ha").StepSize, Is.EqualTo(30));
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_GeometryIsTheOnlyChange_StillConfirmsAndApplies() {
            // The detection settings match, so the pre-existing "nothing to change" early-out would have skipped the
            // dialog entirely and dropped the one thing the file did carry.
            var h = Build();
            var export = StarDetectionSettingsExport.FromOptions(
                h.Buffer, "Ha", new PerFilterSweepGeometry() { StepSize = 25, InitialOffsetSteps = 8 });
            var path = WriteExportFile(export);
            try {
                var confirmCalled = false;
                var applied = await StarDetectionSettingsIO.ImportAsync(h.Buffer, path, (rows, summary) => {
                    confirmCalled = true;
                    return Task.FromResult(true);
                }, h.GeometryTarget);

                Assert.Multiple(() => {
                    Assert.That(confirmCalled, Is.True);
                    Assert.That(applied, Is.True);
                    Assert.That(h.Store.GetSweepGeometry("Ha").StepSize, Is.EqualTo(25));
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_NothingDiffers_SkipsTheDialog() {
            var h = Build();
            h.Binder.SweepStepSizeOverride = 25;
            var export = StarDetectionSettingsExport.FromOptions(
                h.Buffer, "Ha", new PerFilterSweepGeometry() { StepSize = 25 });
            var path = WriteExportFile(export);
            try {
                var confirmCalled = false;
                var applied = await StarDetectionSettingsIO.ImportAsync(h.Buffer, path, (rows, summary) => {
                    confirmCalled = true;
                    return Task.FromResult(true);
                }, h.GeometryTarget);

                Assert.Multiple(() => {
                    Assert.That(confirmCalled, Is.False);
                    Assert.That(applied, Is.False);
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_InheritGeometryInTheFile_ClearsAStoredOverride() {
            var h = Build();
            h.Binder.SweepStepSizeOverride = 30;
            h.Binder.SweepOffsetStepsOverride = 6;
            var path = WriteExportFile(ExportWith(8.4, PerFilterSweepGeometry.Unset()));
            try {
                await StarDetectionSettingsIO.ImportAsync(
                    h.Buffer, path, (rows, summary) => Task.FromResult(true), h.GeometryTarget);

                Assert.Multiple(() => {
                    Assert.That(h.Store.GetSweepGeometry("Ha").IsUnset, Is.True);
                    Assert.That(h.Binder.SweepStepSizeOverride, Is.EqualTo(PerFilterSweepGeometry.Inherit));
                    Assert.That(h.Binder.EffectiveSweepStepSize, Is.EqualTo(100), "back to the profile value");
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_FileWithNoGeometryNode_LeavesTheStoredOverrideAlone() {
            // Every export written before this feature, and every export written with per-filter mode off. Silence
            // in the file is not an instruction to clear the filter's override.
            var h = Build();
            h.Binder.SweepStepSizeOverride = 30;
            var path = WriteExportFile(ExportWith(8.4, geometry: null, filterName: null));
            try {
                var applied = await StarDetectionSettingsIO.ImportAsync(
                    h.Buffer, path, (rows, summary) => Task.FromResult(true), h.GeometryTarget);

                Assert.Multiple(() => {
                    Assert.That(applied, Is.True);
                    Assert.That(h.Buffer.BrightnessSensitivity, Is.EqualTo(8.4));
                    Assert.That(h.Store.GetSweepGeometry("Ha").StepSize, Is.EqualTo(30));
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_UnusableGeometryInTheFile_IsCoercedToInherit() {
            // A hand-edited file must not be able to put a 0 step size in front of the auto-focus engine.
            var h = Build();
            var export = ExportWith(8.4, PerFilterSweepGeometry.Unset());
            export.SweepGeometry = new PerFilterSweepGeometry() { StepSize = 0, InitialOffsetSteps = 0 };
            var path = WriteExportFile(export);
            try {
                await StarDetectionSettingsIO.ImportAsync(
                    h.Buffer, path, (rows, summary) => Task.FromResult(true), h.GeometryTarget);

                Assert.That(h.Store.GetSweepGeometry("Ha").IsUnset, Is.True);
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_PerFilterOff_ImportsDetectionAndSaysTheGeometryWasNotApplied() {
            // With the feature off there is no per-filter set to write a sweep into, and geometry never wrote to the
            // profile. Dropping it is correct — dropping it silently is not.
            var h = Build(enabled: false);
            var path = WriteExportFile(ExportWith(8.4, new PerFilterSweepGeometry() { StepSize = 25, InitialOffsetSteps = 8 }));
            try {
                string shownSummary = null;
                IReadOnlyList<StarDetectionSettingDiffRow> shownRows = null;
                var applied = await StarDetectionSettingsIO.ImportAsync(h.Buffer, path, (rows, summary) => {
                    shownRows = rows;
                    shownSummary = summary;
                    return Task.FromResult(true);
                }, h.GeometryTarget);

                Assert.Multiple(() => {
                    Assert.That(applied, Is.True);
                    Assert.That(h.Buffer.BrightnessSensitivity, Is.EqualTo(8.4));
                    Assert.That(shownRows.Any(r => r.Name.StartsWith("Auto-Focus")), Is.False,
                        "no geometry rows: there is nothing they could be applied to");
                    Assert.That(shownSummary, Does.Contain("sweep geometry"));
                    Assert.That(h.Store.GetSweepGeometry("Ha").IsUnset, Is.True);
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_PerFilterOffAndTheFileInherits_SaysNothingAboutGeometry() {
            // Nothing was dropped, so there is nothing to report.
            var h = Build(enabled: false);
            var path = WriteExportFile(ExportWith(8.4, PerFilterSweepGeometry.Unset()));
            try {
                string shownSummary = null;
                await StarDetectionSettingsIO.ImportAsync(h.Buffer, path, (rows, summary) => {
                    shownSummary = summary;
                    return Task.FromResult(true);
                }, h.GeometryTarget);

                Assert.That(shownSummary, Does.Not.Contain("sweep geometry"));
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public async Task ImportAsync_CorruptFile_IsRejectedWithoutTouchingAnything() {
            var h = Build();
            h.Binder.SweepStepSizeOverride = 30;
            var path = Path.Combine(Path.GetTempPath(), "HFExportIO_" + Guid.NewGuid().ToString("N") + ".json");
            try {
                File.WriteAllText(path, "{ not valid json ");

                var confirmCalled = false;
                var applied = await StarDetectionSettingsIO.ImportAsync(h.Buffer, path, (rows, summary) => {
                    confirmCalled = true;
                    return Task.FromResult(true);
                }, h.GeometryTarget);

                Assert.Multiple(() => {
                    Assert.That(applied, Is.False);
                    Assert.That(confirmCalled, Is.False);
                    Assert.That(h.Store.GetSweepGeometry("Ha").StepSize, Is.EqualTo(30));
                });
            } finally {
                File.Delete(path);
            }
        }

        // --- Copy from filter, over the same shared helpers -------------------------------------------------

        [Test]
        public async Task CopyFromFilter_CarriesTheSourceFiltersGeometry() {
            var h = Build();
            h.Store.SetSweepGeometry("L", new PerFilterSweepGeometry() { StepSize = 55, InitialOffsetSteps = 9 });

            await StarDetectionSettingsIO.CopyFromFilterAsync(
                "L", h.Store, h.Buffer, (rows, summary) => Task.FromResult(true), h.GeometryTarget);

            var stored = h.Store.GetSweepGeometry("Ha");
            Assert.Multiple(() => {
                Assert.That(stored.StepSize, Is.EqualTo(55));
                Assert.That(stored.InitialOffsetSteps, Is.EqualTo(9));
                Assert.That(h.Store.GetSweepGeometry("L").StepSize, Is.EqualTo(55), "the source is not modified");
            });
        }
    }
}
