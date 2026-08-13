#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NUnit.Framework;
using System.Collections.Generic;
using TestApp.SynthBank;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Bank {

    /// <summary>
    /// W24 P2 — the binning-revisit deferral bound in <c>synth-validate</c>'s round loop.
    ///
    /// <para><b>This is an INSTRUMENT repair, not a product fix, and saying so is half the point.</b> The livelock
    /// lives in <c>TestApp/SynthValidateRunner.cs</c>, which ships in <c>TestApp.exe</c> and which NINA never
    /// loads. The wizard builds one summary carrying the recommended step size and the recommended detection
    /// binning together, from the same fit, in the same pass — no round loop, no deferral, no scheduler — so
    /// fixing this changes what the series' own measuring device reports, not what NINA does on the next
    /// auto-focus run.</para>
    ///
    /// <para><b>The defect it repairs.</b> The loop applies binning first and defers the exposure and step updates
    /// by a round. On a cell whose binning recommendation OSCILLATES (2, 1, 2, 1) every round is a binning round,
    /// so the step update never runs at all — four rounds of four, on a real cell, on an instrument whose headline
    /// number was a step-size convergence rate.</para>
    /// </summary>
    [TestFixture]
    public class SynthValidateRunnerBinningDeferralTests {

        /// <summary>
        /// The rule itself: a recommendation for a factor this scenario has ALREADY been at is applied without
        /// deferring, because the deferral's own justification — "changing binning invalidates the exposure
        /// measurement and the HFR-derived step geometry this round just took" — is empty for a factor that has
        /// already been measured.
        /// </summary>
        [Test]
        public void RevisitedBinningFactor_DoesNotDeferTheStep() {
            var visited = BinningRevisitPolicy.NewVisitedSet(2); // the scenario starts at factor 2
            var toOne = BinningRevisitPolicy.Decide(2, 1, visited);
            var backToTwo = BinningRevisitPolicy.Decide(1, 2, visited);

            Assert.Multiple(() => {
                Assert.That(toOne.StepDeferred, Is.True,
                    "the first visit to factor 1 has no prior measurement at that factor, so it defers as before");
                Assert.That(toOne.DeferralBoundReached, Is.False, "the bound did not engage on a first visit");

                Assert.That(backToTwo.BinningApplied, Is.True, "the binning change is still applied");
                Assert.That(backToTwo.NewFactor, Is.EqualTo(2));
                Assert.That(backToTwo.StepDeferred, Is.False,
                    "factor 2 has already been in effect this scenario, so there is nothing left for the deferral to protect");
                Assert.That(backToTwo.RunExposureAndStep, Is.True,
                    "the exposure/step block runs in the SAME round as the binning change");
                Assert.That(backToTwo.DeferralBoundReached, Is.True,
                    "P2's engagement marker: a fix that cannot report whether it engaged is not finished");
                Assert.That(backToTwo.Reason, Does.Contain("not deferring"));
                Assert.That(backToTwo.Reason, Does.Contain("already measured at factor 2"));
            });
        }

        /// <summary>
        /// P2's do-no-harm half, and the reason the rule is keyed on REVISITS rather than on a consecutive-deferral
        /// counter. A monotone walk 1 -> 2 -> 3 is two deferrals in a row and both are honest, because no
        /// measurement at the recommended factor exists yet; an <c>N = 2</c> counter would have cut the second one.
        ///
        /// <para><b>Companion test — it PASSES against the pre-change behaviour too, by design</b>, and is labelled
        /// as such: it pins the half of the rule that must not move.</para>
        /// </summary>
        [Test]
        public void FirstVisitToANewFactor_StillDefersTheStep() {
            var visited = BinningRevisitPolicy.NewVisitedSet(1);
            var toTwo = BinningRevisitPolicy.Decide(1, 2, visited);
            var toThree = BinningRevisitPolicy.Decide(2, 3, visited);
            var agrees = BinningRevisitPolicy.Decide(3, 3, visited);
            var noRecommendation = BinningRevisitPolicy.Decide(3, null, visited);

            Assert.Multiple(() => {
                Assert.That(toTwo.StepDeferred, Is.True, "no measurement at factor 2 existed yet");
                Assert.That(toTwo.DeferralBoundReached, Is.False);
                Assert.That(toTwo.Reason, Does.Contain("deferring exposure/step this round"));

                Assert.That(toThree.StepDeferred, Is.True, "nor at factor 3: two honest deferrals in a row");
                Assert.That(toThree.DeferralBoundReached, Is.False);

                Assert.That(agrees.BinningDiffers, Is.False, "a recommendation equal to the current factor changes nothing");
                Assert.That(agrees.BinningApplied, Is.False);
                Assert.That(agrees.StepDeferred, Is.False);
                Assert.That(agrees.RunExposureAndStep, Is.True);
                Assert.That(agrees.Reason, Is.Null);

                Assert.That(noRecommendation.BinningApplied, Is.False, "an ungated round recommends nothing and defers nothing");
                Assert.That(noRecommendation.RunExposureAndStep, Is.True);
                Assert.That(noRecommendation.NewFactor, Is.EqualTo(3), "and leaves the factor where it was");
            });
        }

        /// <summary>
        /// End to end on the reproduction's own shape: four rounds whose binning recommendation oscillates
        /// 2 -> 1 -> 2 -> 1. Pre-change this is four rounds of no-op — the step update never runs once.
        ///
        /// <para>Note what F26's own proposed next step would have done here. It is keyed on "the binning
        /// recommendation has not CHANGED THE APPLIED VALUE for N consecutive rounds"; on this trace the applied
        /// value changes every single round, so that counter never fires at all. The wording matches F26's
        /// original stuck-at-one evidence and not the oscillating form.</para>
        /// </summary>
        [Test]
        public void OscillatingRecommendation_AppliesTheStepWithinThreeRounds() {
            var recommendations = new int?[] { 1, 2, 1, 2 };
            var visited = BinningRevisitPolicy.NewVisitedSet(2);
            var factor = 2;
            var roundsThatRanTheStep = new List<int>();
            var roundsThatEngagedTheBound = new List<int>();

            for (var round = 0; round < recommendations.Length; round++) {
                var decision = BinningRevisitPolicy.Decide(factor, recommendations[round], visited);
                factor = decision.NewFactor;
                if (decision.RunExposureAndStep) {
                    roundsThatRanTheStep.Add(round);
                }
                if (decision.DeferralBoundReached) {
                    roundsThatEngagedTheBound.Add(round);
                }
            }

            Assert.Multiple(() => {
                Assert.That(roundsThatRanTheStep, Is.Not.Empty,
                    "pre-change the step block never ran in any of the four rounds, which is the livelock");
                Assert.That(roundsThatRanTheStep[0], Is.LessThanOrEqualTo(2),
                    "the cycle is detectable by round 1 (0-based), so the step must run within three rounds");
                Assert.That(roundsThatRanTheStep, Has.Count.GreaterThanOrEqualTo(2),
                    "once the cycle is known, every later round is a revisit and none of them defers");
                Assert.That(roundsThatEngagedTheBound, Is.EqualTo(roundsThatRanTheStep),
                    "every round that ran the step did so BECAUSE the bound engaged, and says so in the report");
                Assert.That(factor, Is.EqualTo(2), "the binning changes are still all applied");
            });
        }

        // ---- W25 P5 (F34) -- the stall message stops naming a cause it never checks -------------------------------

        /// <summary>
        /// <b>T4 — the message must READ the cause, not assert it.</b> The round loop printed, on every stall,
        /// "a no-op recommendation from a degenerate fit, not convergence". It never looked at
        /// <c>degenerateReason</c>. Wave 24 measured what that costs: <c>D01</c>/S1 stalled with
        /// <c>degenerateReason</c> null, a finite <c>halfWidth</c> of 6.0877 and <c>R^2 = 0.99999999999994</c> — as
        /// far from a degenerate fit as a fit gets — and the harness blamed one anyway. A wave-23 register entry
        /// then grouped that cell with two genuinely degenerate ones on the strength of the message. A wrong
        /// message became a wrong register entry.
        ///
        /// <para>The replacement quotes the number that actually explains the stall: the HFR dynamic range the
        /// sweep MEASURED, against the band the step is sized from. On the degenerate branch the old wording is
        /// kept and the exit token appended, so the reader learns WHICH exit rather than only that there was one.</para>
        ///
        /// <para><b>MUST FAIL pre-change.</b> Mutant: restore the unconditional string in
        /// <c>StallReason.Cause</c> — <c>return "a no-op recommendation from a degenerate fit, not convergence";</c>
        /// as the whole body.</para>
        /// </summary>
        [Test]
        public void StallMessage_NamesTheNonDegenerateCause_WhenDegenerateReasonIsNull() {
            // D01/S1's own numbers.
            var nonDegenerate = new StepRecommendationSnapshot {
                StepSize = 21, HalfWidth = 6.0877, DegenerateReason = null, SampledHfrRange = 1.4628
            };
            // D03/S1's: the exit that really is a could-not-look.
            var degenerate = new StepRecommendationSnapshot {
                StepSize = 21, HalfWidth = double.NaN, DegenerateReason = "half-width-unresolved", SampledHfrRange = 1.0799
            };
            // And a round that recorded no recommendation at all: neither cause may be assumed.
            var absent = StallReason.Cause(null);

            var message = StallReason.Describe(21, 8.4, 46.1, nonDegenerate);

            Assert.Multiple(() => {
                Assert.That(message, Does.Not.Contain("from a degenerate fit"),
                    "the cell it was measured on is NOT degenerate, and the message must stop saying it is");
                Assert.That(message, Does.Contain("NON-degenerate fit"), "it says what is actually true");
                Assert.That(message, Does.Contain("1.463"), "and quotes the number that explains it -- what the sweep MEASURED");
                Assert.That(message, Does.Contain("band 3x"), "against the band the step is sized from");
                Assert.That(message, Does.Contain("not convergence"), "the verdict itself is unchanged");

                // The band arithmetic is untouched: the message still says where it stopped and what it missed.
                Assert.That(message, Does.StartWith("stalled (round applied nothing, but step 21 is outside the "));
                Assert.That(message, Does.Contain("tolerance band of step_behavioral 46.1"));

                var degenerateMessage = StallReason.Cause(degenerate);
                Assert.That(degenerateMessage, Does.Contain("from a degenerate fit"), "today's wording, kept");
                Assert.That(degenerateMessage, Does.Contain("half-width-unresolved"), "with the exit token appended");
                Assert.That(degenerateMessage, Does.Not.Contain("NON-degenerate"));

                Assert.That(absent, Does.Contain("could not"), "no snapshot is a could-not-look, not a third cause");
                Assert.That(absent, Does.Not.Contain("from a degenerate fit"));
                Assert.That(absent, Does.Not.Contain("NON-degenerate fit"));

                // Never print a missing measurement at the reader.
                foreach (var text in new[] { message, degenerateMessage, absent }) {
                    Assert.That(text, Does.Not.Contain("NaN"));
                }
            });
        }
    }
}
