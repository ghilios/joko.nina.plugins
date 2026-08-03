using NUnit.Framework;
using TestApp.SynthBank;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Bank {

    /// <summary>
    /// The fourth harness-calibration bug found on the synthetic AF bank, and the third in this family:
    /// <c>synth-validate</c> reported <c>converged: true</c> for a round that applied nothing, at ANY step,
    /// because a degenerate fit makes <c>StepSizeRecommender</c> hold the current step — indistinguishable at the
    /// call site from it agreeing. <c>D05_tec140_1000mm</c> S2 reported convergence at step 140 against a
    /// behavioral step of 35, in the same JSON object as assertion A3 failing that number against [21, 56].
    /// </summary>
    [TestFixture]
    public class ConvergenceBandTests {

        private const double DefaultTolerance = 0.4;

        [Test]
        public void HalfWidth_IsTheFractionalTolerance_OnAnOrdinaryStep() {
            Assert.That(ConvergenceBand.HalfWidth(35.0, DefaultTolerance), Is.EqualTo(14.0).Within(1e-12));
        }

        [Test]
        public void HalfWidth_FloorsAtHalfAStep_SoTinyStepsStayExpressible() {
            // Without the floor a behavioral step of 1 would demand |step - 1| <= 0.4, which the integer step
            // grid cannot express as anything but exactness.
            Assert.That(ConvergenceBand.HalfWidth(1.0, DefaultTolerance), Is.EqualTo(0.5).Within(1e-12));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(0.0)]
        [TestCase(-35.0)]
        public void HalfWidth_IsUndefined_WhenTheBehavioralStepNeverConverged(double stepBehavioral) {
            Assert.That(ConvergenceBand.HalfWidth(stepBehavioral, DefaultTolerance), Is.NaN);
        }

        [Test]
        public void Contains_RejectsTheD05Case_TheBugThisFixIsFor() {
            // The measured failure, pinned: final step 140, behavioral 35. This used to be reported as
            // convergence while A3 failed the identical number.
            var band = ConvergenceBand.HalfWidth(35.0, DefaultTolerance);
            Assert.That(ConvergenceBand.Contains(140, 35.0, band), Is.False,
                "a step 4x the behavioral fixed point is not convergence, whatever the loop stopped for");
        }

        [Test]
        public void Contains_AcceptsAStepInsideTheBand() {
            var band = ConvergenceBand.HalfWidth(35.0, DefaultTolerance); // 14 => [21, 49]
            Assert.Multiple(() => {
                Assert.That(ConvergenceBand.Contains(35, 35.0, band), Is.True);
                Assert.That(ConvergenceBand.Contains(21, 35.0, band), Is.True, "inclusive at the lower edge");
                Assert.That(ConvergenceBand.Contains(49, 35.0, band), Is.True, "inclusive at the upper edge");
                Assert.That(ConvergenceBand.Contains(20, 35.0, band), Is.False);
                Assert.That(ConvergenceBand.Contains(50, 35.0, band), Is.False);
            });
        }

        [Test]
        public void Contains_IsFalse_WhenTheBandIsUndefined() {
            // An unknown band is not a passing one. Contains is only ever asked in order to make a positive
            // claim, so it must never answer yes without evidence.
            Assert.Multiple(() => {
                Assert.That(ConvergenceBand.Contains(35, double.NaN, double.NaN), Is.False);
                Assert.That(ConvergenceBand.Contains(35, 35.0, double.NaN), Is.False);
            });
        }

        [Test]
        public void Band_IsAStrictSubsetOfAssertionA3sBand_SoTheTwoCannotContradict() {
            // A3 asserts [0.6, 1.6] x step_behavioral. This band is [0.6, 1.4] x at the default tolerance —
            // same lower edge, tighter upper edge. That containment is what guarantees a run reported as
            // converged is never one A3 rejects, which is the contradiction D05 exhibited.
            const double behavioral = 35.0;
            var band = ConvergenceBand.HalfWidth(behavioral, DefaultTolerance);
            var a3Lo = 0.6 * behavioral;
            var a3Hi = 1.6 * behavioral;
            for (var step = 1; step <= 200; step++) {
                if (ConvergenceBand.Contains(step, behavioral, band)) {
                    Assert.That(step, Is.InRange(a3Lo, a3Hi),
                        $"step {step} is inside the convergence band but outside A3's terminal band");
                }
            }
        }
    }
}
