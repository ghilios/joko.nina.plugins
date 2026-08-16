using NINA.Joko.Plugins.HocusFocus;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class StarDetectionCopyFromFilterTests {

        private static StarDetectionOptions NewOptions() =>
            new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

        private static StarDetectionSettingsSnapshot AdvancedSnapshot(double brightnessSensitivity) {
            var source = NewOptions();
            source.UseAdvanced = true;
            source.BrightnessSensitivity = brightnessSensitivity;
            source.DebugMode = true;
            source.PSFParallelPartitionSize = 999;
            return StarDetectionSettingsSnapshot.FromOptions(source);
        }

        [Test]
        public async Task CopyFromFilter_OnApply_LoadsSourceSnapshotIntoBuffer() {
            // Snapshot built into a local first: constructing it via NewOptions() subscribes to a *different*
            // NSubstitute mock's event (IProfileService.ProfileChanged), which would otherwise clobber
            // NSubstitute's thread-global "last call" tracking if inlined into the .Returns(...) argument.
            var sourceSnapshot = AdvancedSnapshot(8.4);
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            store.GetOrSeedSnapshot("Ha").Returns(sourceSnapshot);

            var buffer = NewOptions();
            buffer.UseAdvanced = true;
            buffer.BrightnessSensitivity = 2.0;
            buffer.DebugMode = false;
            buffer.PSFParallelPartitionSize = 50;

            IReadOnlyList<StarDetectionSettingDiffRow> shownRows = null;
            string shownSummary = null;
            await StarDetectionSettingsIO.CopyFromFilterAsync("Ha", store, buffer, (rows, summary) => {
                shownRows = rows;
                shownSummary = summary;
                return Task.FromResult(true);
            });

            Assert.Multiple(() => {
                Assert.That(shownRows, Is.Not.Empty);
                Assert.That(shownSummary, Does.Contain("Ha"));
                Assert.That(buffer.BrightnessSensitivity, Is.EqualTo(8.4));
                // Machine-local knobs never transfer between filters (ApplyImportedSnapshot semantics).
                Assert.That(buffer.DebugMode, Is.False);
                Assert.That(buffer.PSFParallelPartitionSize, Is.EqualTo(50));
            });
        }

        [Test]
        public async Task CopyFromFilter_OnCancel_LeavesBufferUntouched() {
            // See CopyFromFilter_OnApply_LoadsSourceSnapshotIntoBuffer for why the snapshot is hoisted to a local.
            var sourceSnapshot = AdvancedSnapshot(8.4);
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            store.GetOrSeedSnapshot("Ha").Returns(sourceSnapshot);

            var buffer = NewOptions();
            buffer.UseAdvanced = true;
            buffer.BrightnessSensitivity = 2.0;

            await StarDetectionSettingsIO.CopyFromFilterAsync("Ha", store, buffer, (rows, summary) => Task.FromResult(false));

            Assert.That(buffer.BrightnessSensitivity, Is.EqualTo(2.0));
        }

        [Test]
        public async Task CopyFromFilter_IdenticalSettings_SkipsDialog() {
            var buffer = NewOptions();
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            store.GetOrSeedSnapshot("Ha").Returns(StarDetectionSettingsSnapshot.FromOptions(buffer));

            var confirmCalled = false;
            await StarDetectionSettingsIO.CopyFromFilterAsync("Ha", store, buffer, (rows, summary) => {
                confirmCalled = true;
                return Task.FromResult(true);
            });

            Assert.That(confirmCalled, Is.False);
        }

        [Test]
        public async Task CopyFromFilter_EmptySourceName_IsANoOp() {
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            var buffer = NewOptions();

            var confirmCalled = false;
            await StarDetectionSettingsIO.CopyFromFilterAsync("", store, buffer, (rows, summary) => {
                confirmCalled = true;
                return Task.FromResult(true);
            });

            Assert.That(confirmCalled, Is.False);
            store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
        }

        // --- Sweep geometry travels with the copy -------------------------------------------------------------
        //
        // Deliberately built by a SEPARATE diff helper rather than through ImportableSettings: sweep geometry is
        // not an IStarDetectionOptions property, so classifying it as importable would make a star-detection
        // export file rewrite a focuser sweep on another rig, and classifying it as machine-local would be untrue.

        [Test]
        public void BuildSweepGeometryDiff_BothFieldsDiffer_ProducesTwoLabelledRows() {
            var rows = StarDetectionSettingsDiff.BuildSweepGeometryDiff(
                currentStepSize: PerFilterSweepGeometry.Inherit,
                currentOffsetSteps: 4,
                incoming: new PerFilterSweepGeometry { StepSize = 25, InitialOffsetSteps = 8 },
                profileStepSize: 100,
                profileOffsetSteps: 4);

            Assert.Multiple(() => {
                Assert.That(rows, Has.Count.EqualTo(2));
                Assert.That(rows[0].Name, Is.EqualTo("Auto-Focus Step Size"));
                Assert.That(rows[0].CurrentValue, Is.EqualTo("inherit (100)"), "an unset value shows what it resolves to");
                Assert.That(rows[0].NewValue, Is.EqualTo("25"));
                Assert.That(rows[1].Name, Is.EqualTo("Auto-Focus Offset Steps"));
                Assert.That(rows[1].NewValue, Is.EqualTo("8"));
            });
        }

        [Test]
        public void BuildSweepGeometryDiff_IdenticalGeometry_ProducesNoRows() {
            var rows = StarDetectionSettingsDiff.BuildSweepGeometryDiff(
                currentStepSize: 25,
                currentOffsetSteps: 8,
                incoming: new PerFilterSweepGeometry { StepSize = 25, InitialOffsetSteps = 8 },
                profileStepSize: 100,
                profileOffsetSteps: 4);

            Assert.That(rows, Is.Empty);
        }

        [Test]
        public void BuildSweepGeometryDiff_IncomingInheritsWhereCurrentOverrides_ShowsTheProfileFallback() {
            var rows = StarDetectionSettingsDiff.BuildSweepGeometryDiff(
                currentStepSize: 25,
                currentOffsetSteps: PerFilterSweepGeometry.Inherit,
                incoming: PerFilterSweepGeometry.Unset(),
                profileStepSize: 100,
                profileOffsetSteps: 4);

            Assert.Multiple(() => {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(rows[0].CurrentValue, Is.EqualTo("25"));
                Assert.That(rows[0].NewValue, Is.EqualTo("inherit (100)"));
            });
        }

        // Dropping an explicit override in favour of inheriting is a real change to what is STORED, even when the
        // two happen to resolve to the same number today -- a later edit in Options -> Focuser would move one and
        // not the other. So it is shown rather than silently collapsed.
        [Test]
        public void BuildSweepGeometryDiff_ExplicitValueReplacedByInheritOfTheSameNumber_IsStillShown() {
            var rows = StarDetectionSettingsDiff.BuildSweepGeometryDiff(
                currentStepSize: PerFilterSweepGeometry.Inherit,
                currentOffsetSteps: 4,
                incoming: PerFilterSweepGeometry.Unset(),
                profileStepSize: 100,
                profileOffsetSteps: 4);

            Assert.Multiple(() => {
                Assert.That(rows, Has.Count.EqualTo(1));
                Assert.That(rows[0].Name, Is.EqualTo("Auto-Focus Offset Steps"));
                Assert.That(rows[0].CurrentValue, Is.EqualTo("4"));
                Assert.That(rows[0].NewValue, Is.EqualTo("inherit (4)"));
            });
        }

        [Test]
        public void BuildSweepGeometryDiff_NullIncoming_IsTreatedAsUnset() {
            var rows = StarDetectionSettingsDiff.BuildSweepGeometryDiff(
                currentStepSize: PerFilterSweepGeometry.Inherit,
                currentOffsetSteps: PerFilterSweepGeometry.Inherit,
                incoming: null,
                profileStepSize: 100,
                profileOffsetSteps: 4);

            Assert.That(rows, Is.Empty);
        }

        [Test]
        public void PerFilterBindingSurface_IdenticalOnBothHosts() {
            // The shared HocusFocus_StarDetection_Options template binds these DataContext-relative paths. Both
            // hosts — the plugin options page (DataContext = HocusFocusPlugin) and the Imaging dockable
            // (DataContext = StarDetectionOptionsVM) — must expose identically-named instance properties for the
            // same XAML to resolve on either.
            var bindingRoots = new[] { "PerFilterStore", "PerFilterEditBinder", "CopyStarDetectionFromFilterCommand" };
            Assert.Multiple(() => {
                foreach (var name in bindingRoots) {
                    Assert.That(typeof(HocusFocusPlugin).GetProperty(name), Is.Not.Null, $"HocusFocusPlugin.{name}");
                    Assert.That(typeof(StarDetectionOptionsVM).GetProperty(name), Is.Not.Null, $"StarDetectionOptionsVM.{name}");
                }
            });
        }
    }
}
