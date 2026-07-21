using NINA.Joko.Plugins.HocusFocus;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
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
