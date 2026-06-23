#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Review {

    [TestFixture]
    public class FrameReviewVMBaseTests {

        // Minimal concrete subclass over a fixed frame count to exercise the shared navigation/viewport logic.
        private sealed class TestReviewVM : FrameReviewVMBase<object> {
            public TestReviewVM(int frameCount) : base(frameCount) { }
            protected override void LoadFrame(int index) { }
            protected override System.Collections.Generic.IReadOnlyList<StarReviewLegendEntry> BuildLegend()
                => System.Array.Empty<StarReviewLegendEntry>();
        }

        [Test]
        public void Navigation_ClampsAtEnds_AndUpdatesCanExecute() {
            var vm = new TestReviewVM(frameCount: 3);
            Assert.Multiple(() => {
                Assert.That(vm.CurrentIndex, Is.EqualTo(0));
                Assert.That(vm.PrevCommand.CanExecute(null), Is.False, "Prev disabled at first frame");
                Assert.That(vm.NextCommand.CanExecute(null), Is.True);
                Assert.That(vm.PositionLabel, Is.EqualTo("1 / 3"));
            });

            vm.NextCommand.Execute(null);
            vm.NextCommand.Execute(null);
            Assert.Multiple(() => {
                Assert.That(vm.CurrentIndex, Is.EqualTo(2));
                Assert.That(vm.NextCommand.CanExecute(null), Is.False, "Next disabled at last frame");
                Assert.That(vm.PrevCommand.CanExecute(null), Is.True);
                Assert.That(vm.PositionLabel, Is.EqualTo("3 / 3"));
            });

            vm.NextCommand.Execute(null); // no-op past the end
            Assert.That(vm.CurrentIndex, Is.EqualTo(2));
        }

        // The marker stroke/text bindings scale inversely with zoom so overlays keep a constant on-screen size.
        // Exercise the real 1.5/Scale and 1.0/Scale math at a representative (non-floored) scale.
        [Test]
        public void InverseZoom_ScalesMarkersInverselyToZoom() {
            var vm = new TestReviewVM(frameCount: 1);
            vm.Viewport.Set(0.5, 0, 0);
            Assert.Multiple(() => {
                Assert.That(vm.MarkerStrokeThickness, Is.EqualTo(1.5 / 0.5).Within(1e-9)); // 3.0
                Assert.That(vm.MarkerTextScale, Is.EqualTo(1.0 / 0.5).Within(1e-9));        // 2.0
            });
        }

        // At extreme zoom-out the viewport floors Scale at MinScale, so the inverse-zoom bindings stay bounded
        // (markers don't blow up to infinity). Documents the combined viewport-clamp + binding behavior.
        [Test]
        public void InverseZoom_StaysBoundedAtMinScale() {
            var vm = new TestReviewVM(frameCount: 1);
            vm.Viewport.Set(StarReviewViewport.MinScale / 2.0, 0, 0); // viewport clamps Scale up to MinScale
            Assert.That(vm.MarkerStrokeThickness, Is.EqualTo(1.5 / StarReviewViewport.MinScale).Within(1e-9));
        }
    }
}
