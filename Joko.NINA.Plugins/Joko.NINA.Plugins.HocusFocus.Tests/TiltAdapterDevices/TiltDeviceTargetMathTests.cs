using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices {

    /// <summary>
    /// Pins the device-motor -> wizard-screw conversion that both the manual Target Positions panel and the
    /// Inspector's return-to-a-past-run path depend on. The mapping is TR/TL/BL/BR = motors 1/2/4/3, so wizard
    /// index [0,1,2,3] reads device index [0,1,3,2] — the transposition in the back two entries is the whole
    /// reason this helper exists.
    /// </summary>
    [TestFixture]
    public class TiltDeviceTargetMathTests {

        [Test]
        public void DeltaPerScrew_MapsDeviceMotorOrderToWizardScrewOrder() {
            // Device order: motor1=10, motor2=20, motor3=30, motor4=40.
            var delta = TiltDeviceTargetMath.DeltaPerScrew(
                targetPerMotor: new[] { 10, 20, 30, 40 },
                currentPerMotor: new[] { 0, 0, 0, 0 });

            // Wizard order: TR(motor1)=10, TL(motor2)=20, BL(motor4)=40, BR(motor3)=30.
            Assert.That(delta, Is.EqualTo(new[] { 10.0, 20.0, 40.0, 30.0 }));
        }

        [Test]
        public void DeltaPerScrew_SubtractsCurrentFromTarget() {
            var delta = TiltDeviceTargetMath.DeltaPerScrew(
                targetPerMotor: new[] { 1000, 1000, 1000, 1000 },
                currentPerMotor: new[] { 990, 1010, 1000, 1030 });

            // TR: 1000-990=+10, TL: 1000-1010=-10, BL(motor4): 1000-1030=-30, BR(motor3): 1000-1000=0.
            Assert.That(delta, Is.EqualTo(new[] { 10.0, -10.0, -30.0, 0.0 }));
        }

        [Test]
        public void DeltaPerScrew_IdenticalPositions_IsAllZeros() {
            var delta = TiltDeviceTargetMath.DeltaPerScrew(new[] { 5, 6, 7, 8 }, new[] { 5, 6, 7, 8 });

            Assert.That(delta, Is.EqualTo(new[] { 0.0, 0.0, 0.0, 0.0 }));
        }

        [TestCase(null, new[] { 1, 2, 3, 4 }, TestName = "DeltaPerScrew_NullTarget_ReturnsNull")]
        [TestCase(new[] { 1, 2, 3, 4 }, null, TestName = "DeltaPerScrew_NullCurrent_ReturnsNull")]
        [TestCase(new[] { 1, 2, 3 }, new[] { 1, 2, 3, 4 }, TestName = "DeltaPerScrew_ShortTarget_ReturnsNull")]
        [TestCase(new[] { 1, 2, 3, 4 }, new[] { 1, 2, 3 }, TestName = "DeltaPerScrew_ShortCurrent_ReturnsNull")]
        [TestCase(new[] { 1, 2, 3, 4, 5 }, new[] { 1, 2, 3, 4 }, TestName = "DeltaPerScrew_LongTarget_ReturnsNull")]
        public void DeltaPerScrew_MalformedInput_ReturnsNull(int[] target, int[] current) {
            Assert.That(TiltDeviceTargetMath.DeltaPerScrew(target, current), Is.Null);
        }

        [Test]
        public void TwistSteps_PureTilt_IsZero() {
            // D1 = (1, 0, -1, 0): opposite corners moving oppositely is a rigid rotation about one axis.
            Assert.That(TiltDeviceTargetMath.TwistSteps(new[] { 1.0, 0.0, -1.0, 0.0 }), Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void TwistSteps_PurePiston_IsZero() {
            Assert.That(TiltDeviceTargetMath.TwistSteps(new[] { 7.0, 7.0, 7.0, 7.0 }), Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void TwistSteps_NonRigidTargets_ReportsTheUnreachableComponent() {
            // t = (s1 - s2 + s3 - s4) / 4 = (2 - 0 + 2 - 0) / 4.
            Assert.That(TiltDeviceTargetMath.TwistSteps(new[] { 2.0, 0.0, 2.0, 0.0 }), Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void TwistSteps_MalformedInput_ReturnsZero() {
            Assert.Multiple(() => {
                Assert.That(TiltDeviceTargetMath.TwistSteps(null), Is.EqualTo(0.0));
                Assert.That(TiltDeviceTargetMath.TwistSteps(new[] { 1.0, 2.0, 3.0 }), Is.EqualTo(0.0));
            });
        }
    }
}
