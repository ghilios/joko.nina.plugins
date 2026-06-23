using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection {

    [TestFixture]
    public class FrameReviewRetentionDecisionTests {

        [Test]
        public void Requested_TrueOnlyWhenReviewEnabledAndResolvedScmEnabled() {
            Assert.Multiple(() => {
                Assert.That(InspectorVM.IsFrameReviewRequested(frameReviewEnabled: true, resolvedSensorCurveModelEnabled: true), Is.True);
                Assert.That(InspectorVM.IsFrameReviewRequested(frameReviewEnabled: true, resolvedSensorCurveModelEnabled: false), Is.False);
                Assert.That(InspectorVM.IsFrameReviewRequested(frameReviewEnabled: false, resolvedSensorCurveModelEnabled: true), Is.False);
                Assert.That(InspectorVM.IsFrameReviewRequested(frameReviewEnabled: false, resolvedSensorCurveModelEnabled: false), Is.False);
            });
        }
    }
}
