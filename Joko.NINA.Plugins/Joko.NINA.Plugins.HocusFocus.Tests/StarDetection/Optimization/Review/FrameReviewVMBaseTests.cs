using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization.Review {

    [TestFixture]
    public class FrameReviewVMBaseTests {

        // Minimal concrete subclass: records LoadFrame calls so jumps are observable without image/UI work.
        private sealed class TestReviewVM : FrameReviewVMBase<int> {
            public int LoadedIndex { get; private set; } = -1;
            public int LoadCount { get; private set; }
            public TestReviewVM(int frameCount) : base(frameCount) { }
            protected override void LoadFrame(int index) { LoadedIndex = index; LoadCount++; }
            protected override IReadOnlyList<StarReviewLegendEntry> BuildLegend() => new List<StarReviewLegendEntry>();
        }

        [Test]
        public void SelectedFrameNumber_Get_IsOneBasedCurrentIndex() {
            var vm = new TestReviewVM(5);
            Assert.That(vm.SelectedFrameNumber, Is.EqualTo(1));   // CurrentIndex defaults to 0
        }

        [Test]
        public void SelectedFrameNumber_Set_JumpsAndLoadsThatFrame() {
            var vm = new TestReviewVM(5);
            vm.SelectedFrameNumber = 3;                            // 1-based -> index 2
            Assert.Multiple(() => {
                Assert.That(vm.CurrentIndex, Is.EqualTo(2));
                Assert.That(vm.LoadedIndex, Is.EqualTo(2));
                Assert.That(vm.SelectedFrameNumber, Is.EqualTo(3));
            });
        }

        [TestCase(0)]    // index -1
        [TestCase(6)]    // index 5 == FrameCount (out of range)
        [TestCase(99)]
        public void SelectedFrameNumber_Set_OutOfRange_IsIgnored(int oneBasedValue) {
            var vm = new TestReviewVM(5);
            vm.SelectedFrameNumber = 3;                            // move to index 2 first
            var loadsBefore = vm.LoadCount;
            vm.SelectedFrameNumber = oneBasedValue;
            Assert.Multiple(() => {
                Assert.That(vm.CurrentIndex, Is.EqualTo(2));        // unchanged
                Assert.That(vm.LoadCount, Is.EqualTo(loadsBefore)); // no reload
            });
        }

        [Test]
        public void SelectedFrameNumber_Set_SameValue_DoesNotReload() {
            var vm = new TestReviewVM(5);
            vm.SelectedFrameNumber = 3;
            var loadsBefore = vm.LoadCount;
            vm.SelectedFrameNumber = 3;                            // no-op
            Assert.That(vm.LoadCount, Is.EqualTo(loadsBefore));
        }

        [Test]
        public void NextCommand_RaisesSelectedFrameNumber_AndUpdatesValue() {
            var vm = new TestReviewVM(5);
            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.NextCommand.Execute(null);   // 0 -> 1

            Assert.Multiple(() => {
                Assert.That(vm.SelectedFrameNumber, Is.EqualTo(2));
                Assert.That(raised, Does.Contain(nameof(vm.SelectedFrameNumber)));
            });
        }
    }
}
