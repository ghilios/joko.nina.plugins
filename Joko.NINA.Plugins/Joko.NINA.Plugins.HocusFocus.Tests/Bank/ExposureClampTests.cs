#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NUnit.Framework;
using TestApp.SynthBank;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Bank {

    /// <summary>
    /// F19(b) — the synthetic bank reports a clamped exposure identically to a freely-solved one, so a value that
    /// carries no information reads as a measurement. <c>D02_rich_135mm</c> is the case: the solve asks for far
    /// less than the 0.5 s floor, the reported band collapses to [0.5, 0.5], and wave 7's arm E then measured that
    /// dataset gaining 44% of its σ_focus at 8× that "derived" exposure.
    /// </summary>
    [TestFixture]
    public class ExposureClampTests {

        // The bank's own bounds, so the cases below are the real ones rather than convenient round numbers.
        private const double Min = 0.5;
        private const double Max = 30.0;

        [Test]
        public void Classify_NamesTheFloorWhenTheSolveAsksForLessThanTheMinimum() {
            // D02's shape: the arithmetic wants ~5e-5 s (S_now measured at 991.8 against a target of 10), and the
            // bank reports 0.5 s. DISCRIMINATING: before this, the harness reported 0.5 s with no way to tell it
            // apart from a dataset whose solve genuinely landed at 0.5 s.
            Assert.That(ExposureClamp.Classify(0.00005, Min, Max), Is.EqualTo(ExposureClamp.Floor));
            Assert.That(ExposureClamp.Saturated(ExposureClamp.Classify(0.00005, Min, Max)), Is.True);
        }

        [Test]
        public void Classify_NamesTheCeilingWhenTheSolveAsksForMoreThanTheMaximum() {
            // D10_rc16_3250mm_sparse's shape: derived 30.0 s with a band of [30.0, 30.0].
            Assert.That(ExposureClamp.Classify(112.0, Min, Max), Is.EqualTo(ExposureClamp.Ceiling));
        }

        [Test]
        public void Classify_ASolveInsideTheBandIsNotSaturated() {
            Assert.Multiple(() => {
                Assert.That(ExposureClamp.Classify(1.5, Min, Max), Is.EqualTo(ExposureClamp.None));
                Assert.That(ExposureClamp.Saturated(ExposureClamp.None), Is.False);
            });
        }

        [Test]
        public void Classify_ASolveLandingExactlyOnABoundIsNOTSaturated() {
            // The reason this classifies the RAW value rather than comparing the clamped result to the bounds: a
            // solve that legitimately asks for exactly the minimum got the number it asked for, and reporting it as
            // "saturated" would put a dataset on the re-check work list that has nothing wrong with it.
            // DISCRIMINATING against the obvious `clamped == Min ? floor : ...` implementation, which cannot tell
            // this case from the D02 case at all.
            Assert.Multiple(() => {
                Assert.That(ExposureClamp.Classify(Min, Min, Max), Is.EqualTo(ExposureClamp.None));
                Assert.That(ExposureClamp.Classify(Max, Min, Max), Is.EqualTo(ExposureClamp.None));
            });
        }

        [Test]
        public void Classify_AnUnsolvableConfigurationIsNotDerivedRatherThanClamped() {
            // "Unmeasurable" is not "saturated" -- the same distinction F18's W_detect guard draws at the other end
            // of the harness. A NaN raw must not be reported as having hit the floor just because NaN comparisons
            // are false; it must be its own verdict.
            Assert.Multiple(() => {
                Assert.That(ExposureClamp.Classify(double.NaN, Min, Max), Is.EqualTo(ExposureClamp.NotDerived));
                Assert.That(ExposureClamp.Classify(double.PositiveInfinity, Min, Max), Is.EqualTo(ExposureClamp.NotDerived));
                Assert.That(ExposureClamp.Saturated(ExposureClamp.NotDerived), Is.False);
            });
        }
    }
}
