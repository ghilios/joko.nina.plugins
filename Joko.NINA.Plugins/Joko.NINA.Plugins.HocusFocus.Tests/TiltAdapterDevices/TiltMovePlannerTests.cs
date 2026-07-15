#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices;

[TestFixture]
public class TiltMovePlannerTests {

    // Builds a per-screw target vector s = (s1, s2, s3, s4) from the orthogonal components
    // (a, b, f, t) via the design doc's reconstruction formulas: s1=a+f+t, s2=b+f-t, s3=-a+f+t,
    // s4=-b+f-t. Used throughout to construct precise, hand-verifiable test inputs rather than
    // reverse-engineering s by hand for every case.
    private static double[] BuildS(double a, double b, double f, double t) =>
        new[] { a + f + t, b + f - t, -a + f + t, -b + f - t };

    private static readonly Random Rng = new Random(20260714);

    // --- Decompose recovery ---

    [Test]
    public void Decompose_PureAntisymmetricTilt_RecoversExactAAndB_ZeroFAndT() {
        var s = new double[] { 6.0, -3.5, -6.0, 3.5 }; // (a, -a-pattern)=(a,b,-a,-b) with a=6, b=-3.5

        var (a, b, f, t) = TiltMovePlanner.Decompose(s);

        Assert.Multiple(() => {
            Assert.That(a, Is.EqualTo(6.0).Within(1e-12));
            Assert.That(b, Is.EqualTo(-3.5).Within(1e-12));
            Assert.That(f, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(t, Is.EqualTo(0.0).Within(1e-12));
        });
    }

    [Test]
    public void Decompose_Uniform_RecoversFOnly() {
        var s = new double[] { 7.25, 7.25, 7.25, 7.25 };

        var (a, b, f, t) = TiltMovePlanner.Decompose(s);

        Assert.Multiple(() => {
            Assert.That(f, Is.EqualTo(7.25).Within(1e-12));
            Assert.That(a, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(b, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(t, Is.EqualTo(0.0).Within(1e-12));
        });
    }

    [Test]
    public void Decompose_BuildSRoundTrips_ForGeneralComponents() {
        var s = BuildS(a: 2.5, b: -4.25, f: 3.0, t: 0.75);

        var (a, b, f, t) = TiltMovePlanner.Decompose(s);

        Assert.Multiple(() => {
            Assert.That(a, Is.EqualTo(2.5).Within(1e-12));
            Assert.That(b, Is.EqualTo(-4.25).Within(1e-12));
            Assert.That(f, Is.EqualTo(3.0).Within(1e-12));
            Assert.That(t, Is.EqualTo(0.75).Within(1e-12));
        });
    }

    [Test]
    public void Decompose_WrongLength_Throws() {
        Assert.Throws<ArgumentException>(() => TiltMovePlanner.Decompose(new double[] { 1, 2, 3 }));
    }

    [Test]
    public void Decompose_Null_Throws() {
        Assert.Throws<ArgumentException>(() => TiltMovePlanner.Decompose(null));
    }

    // --- RoundToStep ---

    [TestCase(4.6, 5)]
    [TestCase(5.4, 5)]
    [TestCase(-4.6, -5)]
    [TestCase(2.5, 3)]   // ties away from zero, not banker's rounding
    [TestCase(-2.5, -3)]
    [TestCase(0.49, 0)]
    [TestCase(-0.49, 0)]
    public void RoundToStep_MatchesAwayFromZeroConvention(double value, int expected) {
        Assert.That(TiltMovePlanner.RoundToStep(value), Is.EqualTo(expected));
    }

    // --- Single-edge merge vs two-diagonal general case ---

    [Test]
    public void Plan_RaEqualsRb_ProducesOneEdgeVerticalMove() {
        // a=4.6, b=5.4 -> ra=5, rb=5 (equal, nonzero) -> ONE EdgeVertical(5) move, not two diagonals.
        var s = BuildS(a: 4.6, b: 5.4, f: 0, t: 0);

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0);

        Assert.That(plan.Moves, Has.Count.EqualTo(1));
        var move = plan.Moves[0];
        Assert.Multiple(() => {
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.EdgeVertical));
            Assert.That(move.Steps, Is.EqualTo(5));
            Assert.That(move.Group, Is.EqualTo(TiltMoveGroup.Tilt));
        });
    }

    [Test]
    public void Plan_RaEqualsNegativeRb_ProducesOneEdgeHorizontalMove() {
        // a=5, b=-5 -> ra=5, rb=-5 -> ONE EdgeHorizontal(5) move.
        var s = BuildS(a: 5, b: -5, f: 0, t: 0);

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0);

        Assert.That(plan.Moves, Has.Count.EqualTo(1));
        var move = plan.Moves[0];
        Assert.Multiple(() => {
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.EdgeHorizontal));
            Assert.That(move.Steps, Is.EqualTo(5));
            Assert.That(move.Group, Is.EqualTo(TiltMoveGroup.Tilt));
        });
    }

    [Test]
    public void Plan_UnequalNonzeroComponents_ProducesTwoDiagonals_NotForcedIntoOneEdge() {
        // a=3.4, b=2.4 -> ra=3, rb=2 -> TWO diagonals (never force a common magnitude to save a move).
        var s = BuildS(a: 3.4, b: 2.4, f: 0, t: 0);

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0);

        Assert.That(plan.Moves, Has.Count.EqualTo(2));
        Assert.That(plan.Moves.Select(m => m.Axis), Is.EquivalentTo(new[] { TiltMoveAxis.DiagonalA, TiltMoveAxis.DiagonalB }));
        var diagA = plan.Moves.Single(m => m.Axis == TiltMoveAxis.DiagonalA);
        var diagB = plan.Moves.Single(m => m.Axis == TiltMoveAxis.DiagonalB);
        Assert.Multiple(() => {
            Assert.That(diagA.Steps, Is.EqualTo(3));
            Assert.That(diagB.Steps, Is.EqualTo(2));
            Assert.That(diagA.Group, Is.EqualTo(TiltMoveGroup.Tilt));
            Assert.That(diagB.Group, Is.EqualTo(TiltMoveGroup.Tilt));
        });
    }

    [Test]
    public void Plan_OnlyOneComponentNonzero_ProducesSingleDiagonal() {
        var sOnlyA = BuildS(a: 4, b: 0, f: 0, t: 0);
        var sOnlyB = BuildS(a: 0, b: -6, f: 0, t: 0);

        var planA = TiltMovePlanner.Plan(sOnlyA, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0);
        var planB = TiltMovePlanner.Plan(sOnlyB, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0);

        Assert.Multiple(() => {
            Assert.That(planA.Moves, Has.Count.EqualTo(1));
            Assert.That(planA.Moves[0].Axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
            Assert.That(planA.Moves[0].Steps, Is.EqualTo(4));

            Assert.That(planB.Moves, Has.Count.EqualTo(1));
            Assert.That(planB.Moves[0].Axis, Is.EqualTo(TiltMoveAxis.DiagonalB));
            Assert.That(planB.Moves[0].Steps, Is.EqualTo(-6));
        });
    }

    // --- Total move count bound (property-style over random inputs) ---

    [Test]
    public void Plan_TotalMoveCount_NeverExceedsThree_ForRandomInputs() {
        for (int i = 0; i < 500; ++i) {
            var s = RandomS(range: 50.0);
            var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.8);
            Assert.That(plan.Moves.Count, Is.LessThanOrEqualTo(3), $"Iteration {i}: s=[{string.Join(",", s)}]");
        }
    }

    // --- Sub-step targets -> zero moves ---

    [Test]
    public void Plan_AllComponentsUnderHalfStep_ProducesZeroMoves() {
        var s = BuildS(a: 0.4, b: -0.3, f: 0.2, t: 0.1);

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0);

        Assert.That(plan.Moves, Is.Empty);
    }

    // --- Off-center-curvature leakage regression (command-space split guarantee) ---

    [Test]
    public void Plan_OffCenterCurvatureLeakage_AsymmetricPartLandsInTiltGroup_MeanCapturedByBackfocus() {
        // Nonzero mean (backfocus) AND an asymmetric linear part (as off-center curvature would
        // produce): a=2 (leaked linear term), b=0, f=10 (mean/backfocus), t=0.
        var s = BuildS(a: 2, b: 0, f: 10, t: 0); // s = (12, 10, 8, 10)

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0);

        Assert.That(plan.Moves, Has.Count.EqualTo(2));
        var tiltMove = plan.Moves.Single(m => m.Group == TiltMoveGroup.Tilt);
        var bfMove = plan.Moves.Single(m => m.Group == TiltMoveGroup.Backfocus);
        Assert.Multiple(() => {
            Assert.That(tiltMove.Axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
            Assert.That(tiltMove.Steps, Is.EqualTo(2), "The asymmetric leakage term must land in the tilt group, not be dropped.");
            Assert.That(bfMove.Axis, Is.EqualTo(TiltMoveAxis.Backfocus));
            Assert.That(bfMove.Steps, Is.EqualTo(10), "The mean must be captured by the backfocus group.");
        });
    }

    // --- Residual math ---

    [Test]
    public void Plan_ResidualMicronsPerCorner_MatchesAppliedMinusTargetTimesUnit_WorkedExample() {
        // a=4.6, b=5.4, f=0, t=0 -> ra=5, rb=5 -> EdgeVertical(5) -> applied=(5,5,-5,-5).
        var s = BuildS(a: 4.6, b: 5.4, f: 0, t: 0);
        const double unitMicrons = 1.8;

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: unitMicrons);

        var applied = new double[] { 5, 5, -5, -5 };
        var expectedResidual = new double[4];
        for (int i = 0; i < 4; ++i) {
            expectedResidual[i] = (applied[i] - s[i]) * unitMicrons;
        }

        Assert.That(plan.ResidualMicronsPerCorner, Is.EqualTo(expectedResidual).Within(1e-9));
    }

    [Test]
    public void Plan_TwistOnlyInput_ProducesZeroMoves_ResidualReflectsUnreachableTwist_TwistResidualCarriesT() {
        const double t = 0.7;
        var s = new double[] { t, -t, t, -t }; // pure twist, a=b=f=0

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.8);

        Assert.Multiple(() => {
            Assert.That(plan.Moves, Is.Empty, "Twist is never planned; a pure-twist target produces zero moves.");
            Assert.That(plan.TwistResidualSteps, Is.EqualTo(t).Within(1e-12));
            for (int i = 0; i < 4; ++i) {
                Assert.That(plan.ResidualMicronsPerCorner[i], Is.EqualTo((0.0 - s[i]) * 1.8).Within(1e-9), $"corner {i}");
            }
        });
    }

    // --- Twist reported, never planned ---

    [Test]
    public void Plan_NonzeroTwist_NeverProducesAMoveThatChangesT_AcrossGroupToggles() {
        var s = BuildS(a: 3, b: -2, f: 4, t: 1.3);
        var (_, _, _, expectedT) = TiltMovePlanner.Decompose(s);

        var allGroups = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0);
        var tiltOnly = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: false, unitMicrons: 1.0);
        var bfOnly = TiltMovePlanner.Plan(s, includeTilt: false, includeBackfocus: true, unitMicrons: 1.0);
        var neither = TiltMovePlanner.Plan(s, includeTilt: false, includeBackfocus: false, unitMicrons: 1.0);

        // No move axis (DiagonalA/B, EdgeVertical/Horizontal, Backfocus) has any component of t (all
        // generators are combinations of a, b, f only — see TiltAdapterMove's unit-effect table), so
        // TwistResidualSteps must be the same raw t regardless of which groups were applied.
        Assert.Multiple(() => {
            Assert.That(allGroups.TwistResidualSteps, Is.EqualTo(expectedT).Within(1e-12));
            Assert.That(tiltOnly.TwistResidualSteps, Is.EqualTo(expectedT).Within(1e-12));
            Assert.That(bfOnly.TwistResidualSteps, Is.EqualTo(expectedT).Within(1e-12));
            Assert.That(neither.TwistResidualSteps, Is.EqualTo(expectedT).Within(1e-12));
        });
    }

    // --- Group toggles ---

    [Test]
    public void Plan_IncludeBackfocusFalse_DropsBfMove_ResidualReflectsUnappliedBackfocus() {
        var s = BuildS(a: 3, b: 0, f: 5, t: 0); // s = (8, 5, 2, 5)

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: false, unitMicrons: 1.0);

        Assert.That(plan.Moves.Any(m => m.Group == TiltMoveGroup.Backfocus), Is.False);
        Assert.That(plan.Moves, Has.Count.EqualTo(1));
        Assert.That(plan.Moves[0].Axis, Is.EqualTo(TiltMoveAxis.DiagonalA));

        // applied=(3,0,-3,0); residual = applied - s = (-5,-5,-5,-5) -- the missing backfocus of 5 on every corner.
        var expected = new double[] { -5, -5, -5, -5 };
        Assert.That(plan.ResidualMicronsPerCorner, Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void Plan_IncludeTiltFalse_DropsDiagonalAndEdgeMoves_ResidualReflectsUnappliedTilt() {
        var s = BuildS(a: 3, b: 0, f: 5, t: 0); // s = (8, 5, 2, 5)

        var plan = TiltMovePlanner.Plan(s, includeTilt: false, includeBackfocus: true, unitMicrons: 1.0);

        Assert.That(plan.Moves.Any(m => m.Group == TiltMoveGroup.Tilt), Is.False);
        Assert.That(plan.Moves, Has.Count.EqualTo(1));
        Assert.That(plan.Moves[0].Axis, Is.EqualTo(TiltMoveAxis.Backfocus));

        // applied=(5,5,5,5); residual = applied - s = (-3,0,3,0) -- the missing DiagonalA of 3.
        var expected = new double[] { -3, 0, 3, 0 };
        Assert.That(plan.ResidualMicronsPerCorner, Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void Plan_BothGroupsFalse_ProducesZeroMoves_ResidualEqualsNegativeTargetTimesUnit() {
        var s = BuildS(a: 3, b: -2, f: 5, t: 0.4);
        const double unitMicrons = 1.8;

        var plan = TiltMovePlanner.Plan(s, includeTilt: false, includeBackfocus: false, unitMicrons: unitMicrons);

        Assert.That(plan.Moves, Is.Empty);
        for (int i = 0; i < 4; ++i) {
            Assert.That(plan.ResidualMicronsPerCorner[i], Is.EqualTo(-s[i] * unitMicrons).Within(1e-9), $"corner {i}");
        }
    }

    // --- Cap-splitting ---

    [Test]
    public void SplitStepsForCap_450With200Cap_ProducesThreeChunksSummingCorrectly() {
        var chunks = TiltMovePlanner.SplitStepsForCap(450, 200);

        Assert.That(chunks, Is.EqualTo(new[] { 200, 200, 50 }));
        Assert.That(chunks.Sum(), Is.EqualTo(450));
    }

    [Test]
    public void SplitStepsForCap_NegativeMagnitude_PreservesSignInEveryChunk() {
        var chunks = TiltMovePlanner.SplitStepsForCap(-450, 200);

        Assert.That(chunks, Is.EqualTo(new[] { -200, -200, -50 }));
        Assert.That(chunks.Sum(), Is.EqualTo(-450));
    }

    [Test]
    public void SplitStepsForCap_WithinCap_ReturnsSingleUnsplitValue() {
        var chunks = TiltMovePlanner.SplitStepsForCap(150, 200);
        Assert.That(chunks, Is.EqualTo(new[] { 150 }));
    }

    [Test]
    public void SplitStepsForCap_ZeroOrNegativeCap_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => TiltMovePlanner.SplitStepsForCap(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TiltMovePlanner.SplitStepsForCap(100, -5));
    }

    [Test]
    public void Plan_MoveExceedingCap_SplitsIntoMultipleSameAxisMoves_AfterMinimalDecomposition() {
        // a=450, b=0, f=0 -> minimal decomposition is ONE DiagonalA(450) move; cap-splitting then
        // splits that single move into ceil(450/200)=3 same-axis moves: 200, 200, 50.
        var s = BuildS(a: 450, b: 0, f: 0, t: 0);

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0, maxStepsPerCommand: 200);

        Assert.That(plan.Moves, Has.Count.EqualTo(3));
        Assert.That(plan.Moves.Select(m => m.Axis), Is.All.EqualTo(TiltMoveAxis.DiagonalA));
        Assert.That(plan.Moves.Select(m => m.Steps), Is.EqualTo(new[] { 200, 200, 50 }));
        Assert.That(plan.Moves.Sum(m => m.Steps), Is.EqualTo(450));
        Assert.That(plan.Moves.Select(m => m.Group), Is.All.EqualTo(TiltMoveGroup.Tilt));
    }

    [Test]
    public void Plan_CapSplitting_DoesNotChangeAppliedTotalOrResidual() {
        var s = BuildS(a: 450, b: 0, f: 0, t: 0);
        const double unitMicrons = 1.8;

        var uncapped = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: unitMicrons);
        var capped = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: unitMicrons, maxStepsPerCommand: 200);

        Assert.That(capped.ResidualMicronsPerCorner, Is.EqualTo(uncapped.ResidualMicronsPerCorner).Within(1e-9));
    }

    [Test]
    public void Plan_MaxStepsPerCommandLessThanOne_Throws() {
        var s = BuildS(a: 1, b: 1, f: 1, t: 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => TiltMovePlanner.Plan(s, true, true, 1.0, maxStepsPerCommand: 0));
    }

    // --- EstimatedSeconds ---

    [Test]
    public void Plan_EstimatedSeconds_EqualsMoveCountTimesPerMoveSeconds() {
        var s = BuildS(a: 450, b: 0, f: 3, t: 0);

        var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0, maxStepsPerCommand: 200, perMoveSeconds: 12.5);

        Assert.That(plan.EstimatedSeconds, Is.EqualTo(plan.Moves.Count * 12.5).Within(1e-9));
    }

    // --- Validation ---

    [Test]
    public void Plan_WrongLengthInput_Throws() {
        Assert.Throws<ArgumentException>(() => TiltMovePlanner.Plan(new double[] { 1, 2, 3 }, true, true, 1.0));
    }

    // --- Property-style random check: applied == reconstruction from rounded (ra,rb,rf); residual == (applied-s)*unit ---

    [Test]
    public void Plan_RandomInputs_AppliedMatchesRoundedReconstruction_AndResidualIsConsistent() {
        for (int i = 0; i < 500; ++i) {
            var s = RandomS(range: 30.0);
            const double unitMicrons = 1.8;

            var plan = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: unitMicrons);

            var (a, b, f, _) = TiltMovePlanner.Decompose(s);
            int ra = TiltMovePlanner.RoundToStep(a);
            int rb = TiltMovePlanner.RoundToStep(b);
            int rf = TiltMovePlanner.RoundToStep(f);

            // Reconstruction from rounded components via the same a/b/f -> corner formula as BuildS
            // (with t=0, since twist is never applied): applied1=ra+rf, applied2=rb+rf, applied3=-ra+rf, applied4=-rb+rf.
            var expectedApplied = new double[] { ra + rf, rb + rf, -ra + rf, -rb + rf };

            var actualApplied = new double[4];
            foreach (var move in plan.Moves) {
                for (int c = 0; c < 4; ++c) {
                    actualApplied[c] += move.PerCornerSteps[c];
                }
            }

            Assert.That(actualApplied, Is.EqualTo(expectedApplied).Within(1e-9), $"Iteration {i}: s=[{string.Join(",", s)}]");

            for (int c = 0; c < 4; ++c) {
                double expectedResidual = (expectedApplied[c] - s[c]) * unitMicrons;
                Assert.That(plan.ResidualMicronsPerCorner[c], Is.EqualTo(expectedResidual).Within(1e-9), $"Iteration {i}, corner {c}");
            }
        }
    }

    private static double[] RandomS(double range) {
        return new[] {
            (Rng.NextDouble() * 2 - 1) * range,
            (Rng.NextDouble() * 2 - 1) * range,
            (Rng.NextDouble() * 2 - 1) * range,
            (Rng.NextDouble() * 2 - 1) * range,
        };
    }
}

[TestFixture]
public class FourCornerCoupledPlannerTests {

    [Test]
    public void Plan_DelegatesToTiltMovePlanner_ProducesIdenticalResultForSameInputs() {
        var s = new double[] { 8, 5, 2, 5 };
        var planner = new FourCornerCoupledPlanner();

        var viaInstance = planner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.8, maxStepsPerCommand: 100, perMoveSeconds: 8);
        var viaStatic = TiltMovePlanner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.8, maxStepsPerCommand: 100, perMoveSeconds: 8);

        Assert.Multiple(() => {
            Assert.That(viaInstance.Moves.Select(m => (m.Axis, m.Steps, m.Group)),
                Is.EqualTo(viaStatic.Moves.Select(m => (m.Axis, m.Steps, m.Group))));
            Assert.That(viaInstance.ResidualMicronsPerCorner, Is.EqualTo(viaStatic.ResidualMicronsPerCorner));
            Assert.That(viaInstance.TwistResidualSteps, Is.EqualTo(viaStatic.TwistResidualSteps));
            Assert.That(viaInstance.EstimatedSeconds, Is.EqualTo(viaStatic.EstimatedSeconds));
        });
    }

    [Test]
    public void Plan_UsesDefaultParameters_WhenNotSpecified() {
        var s = new double[] { 1, 1, 1, 1 }; // pure backfocus of 1
        var planner = new FourCornerCoupledPlanner();

        var plan = planner.Plan(s, includeTilt: true, includeBackfocus: true, unitMicrons: 1.0);

        Assert.That(plan.Moves, Has.Count.EqualTo(1));
        Assert.That(plan.Moves[0].Axis, Is.EqualTo(TiltMoveAxis.Backfocus));
        Assert.That(plan.Moves[0].Steps, Is.EqualTo(1));
        Assert.That(plan.EstimatedSeconds, Is.EqualTo(10)); // default perMoveSeconds
    }

    [Test]
    public void IsAssignableToInterface() {
        ITiltMovePlanner planner = new FourCornerCoupledPlanner();
        Assert.That(planner, Is.Not.Null);
    }
}
