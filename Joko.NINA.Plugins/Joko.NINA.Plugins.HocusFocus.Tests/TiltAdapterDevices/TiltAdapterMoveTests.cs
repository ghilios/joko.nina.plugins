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
public class TiltAdapterMoveTests {

    // The generator table from the design doc's "Core algorithm" section, copied exactly. This is the
    // correctness anchor for the whole feature: a sign error here means an EEPROM-persisted wrong-way
    // motor move on real hardware.
    private static readonly Dictionary<TiltMoveAxis, double[]> ExpectedUnitEffect = new Dictionary<TiltMoveAxis, double[]> {
        [TiltMoveAxis.DiagonalA] = new double[] { 1, 0, -1, 0 },
        [TiltMoveAxis.DiagonalB] = new double[] { 0, 1, 0, -1 },
        [TiltMoveAxis.EdgeVertical] = new double[] { 1, 1, -1, -1 },
        [TiltMoveAxis.EdgeHorizontal] = new double[] { 1, -1, -1, 1 },
        [TiltMoveAxis.Backfocus] = new double[] { 1, 1, 1, 1 },
    };

    private static IEnumerable<TiltMoveAxis> AllAxes => Enum.GetValues(typeof(TiltMoveAxis)).Cast<TiltMoveAxis>();

    [TestCaseSource(nameof(AllAxes))]
    public void UnitEffect_MatchesGeneratorTableForEveryAxis(TiltMoveAxis axis) {
        var effect = TiltAdapterMove.UnitEffect(axis);
        Assert.That(effect, Is.EqualTo(ExpectedUnitEffect[axis]));
    }

    [TestCaseSource(nameof(AllAxes))]
    public void PerCornerSteps_PositiveSteps_EqualsUnitEffectTimesSteps(TiltMoveAxis axis) {
        const int steps = 5;
        var move = new TiltAdapterMove(axis, steps, TiltMoveGroup.Tilt, "positive test move");
        var expected = ExpectedUnitEffect[axis].Select(e => e * steps).ToArray();
        Assert.That(move.PerCornerSteps, Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(AllAxes))]
    public void PerCornerSteps_NegativeSteps_EqualsUnitEffectTimesSteps(TiltMoveAxis axis) {
        const int steps = -3;
        var move = new TiltAdapterMove(axis, steps, TiltMoveGroup.Tilt, "negative test move");
        var expected = ExpectedUnitEffect[axis].Select(e => e * steps).ToArray();
        Assert.That(move.PerCornerSteps, Is.EqualTo(expected));
    }

    // Explicit worked examples from the plan/design, pinned verbatim as a regression lock in addition to
    // the generic per-axis loop above.
    [Test]
    public void DiagonalA_PlusFive_ProducesDocumentedExample() {
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 5, TiltMoveGroup.Tilt, "tr,5");
        Assert.That(move.PerCornerSteps, Is.EqualTo(new double[] { 5, 0, -5, 0 }));
    }

    [Test]
    public void EdgeVertical_MinusThree_ProducesDocumentedExample() {
        var move = new TiltAdapterMove(TiltMoveAxis.EdgeVertical, -3, TiltMoveGroup.Tilt, "bt,3");
        Assert.That(move.PerCornerSteps, Is.EqualTo(new double[] { -3, -3, 3, 3 }));
    }

    [Test]
    public void Backfocus_PlusTwo_ProducesDocumentedExample() {
        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 2, TiltMoveGroup.Backfocus, "bf,2");
        Assert.That(move.PerCornerSteps, Is.EqualTo(new double[] { 2, 2, 2, 2 }));
    }

    [Test]
    public void Constructor_StoresAxisStepsGroupAndDescription() {
        var move = new TiltAdapterMove(TiltMoveAxis.EdgeHorizontal, -7, TiltMoveGroup.Tilt, "rt,-7");
        Assert.Multiple(() => {
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.EdgeHorizontal));
            Assert.That(move.Steps, Is.EqualTo(-7));
            Assert.That(move.Group, Is.EqualTo(TiltMoveGroup.Tilt));
            Assert.That(move.Description, Is.EqualTo("rt,-7"));
        });
    }

    [Test]
    public void PerCornerSteps_AlwaysHasFourElements() {
        foreach (var axis in AllAxes) {
            var move = new TiltAdapterMove(axis, 1, TiltMoveGroup.Tilt, "length check");
            Assert.That(move.PerCornerSteps, Has.Count.EqualTo(4), $"axis {axis}");
        }
    }

    [Test]
    public void UnitEffect_UnknownAxis_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => TiltAdapterMove.UnitEffect((TiltMoveAxis)999));
    }

    // --- Immutability ---
    //
    // TiltAdapterMove has no array-valued constructor parameter (PerCornerSteps is always computed
    // internally from Axis/Steps), so "defensive copy" here means: the returned collections cannot be
    // mutated by the caller, even via a downcast to a mutable collection interface. This is the property
    // that actually matters for hardware safety — nothing external can corrupt the generator table or a
    // move's computed per-corner effect after construction.

    [Test]
    public void UnitEffect_ReturnedCollection_ThrowsOnMutationAttempt() {
        var effect = TiltAdapterMove.UnitEffect(TiltMoveAxis.DiagonalA);
        var asList = effect as IList<double>;
        Assert.That(asList, Is.Not.Null, "Expected a read-only wrapper implementing IList<double>.");
        Assert.Throws<NotSupportedException>(() => asList[0] = 999);
    }

    [Test]
    public void UnitEffect_CalledTwice_ReturnsTableWithSameValues() {
        var first = TiltAdapterMove.UnitEffect(TiltMoveAxis.EdgeVertical);
        var second = TiltAdapterMove.UnitEffect(TiltMoveAxis.EdgeVertical);
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void PerCornerSteps_ReturnedCollection_ThrowsOnMutationAttempt() {
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalB, 4, TiltMoveGroup.Tilt, "mutation test");
        var asList = move.PerCornerSteps as IList<double>;
        Assert.That(asList, Is.Not.Null, "Expected a read-only wrapper implementing IList<double>.");
        Assert.Throws<NotSupportedException>(() => asList[0] = 999);
    }

    [Test]
    public void PerCornerSteps_IsNotTheSameReferenceAsUnitEffectTable() {
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 1, TiltMoveGroup.Tilt, "reference test");
        Assert.That(move.PerCornerSteps, Is.Not.SameAs(TiltAdapterMove.UnitEffect(TiltMoveAxis.DiagonalA)));
    }
}

[TestFixture]
public class TiltAdapterMovePlanTests {

    private static TiltAdapterMove[] SampleMoves() => new[] {
        new TiltAdapterMove(TiltMoveAxis.DiagonalA, 5, TiltMoveGroup.Tilt, "tr,5"),
        new TiltAdapterMove(TiltMoveAxis.Backfocus, -2, TiltMoveGroup.Backfocus, "bf,-2"),
    };

    [Test]
    public void Constructor_StoresAllFieldsIntact() {
        var moves = SampleMoves();
        var residuals = new double[] { 0.1, -0.2, 0.3, -0.4 };

        var plan = new TiltAdapterMovePlan(moves, residuals, twistResidualSteps: 1.25, estimatedSeconds: 20.0);

        Assert.Multiple(() => {
            Assert.That(plan.Moves, Is.EqualTo(moves));
            Assert.That(plan.ResidualMicronsPerCorner, Is.EqualTo(residuals));
            Assert.That(plan.TwistResidualSteps, Is.EqualTo(1.25));
            Assert.That(plan.EstimatedSeconds, Is.EqualTo(20.0));
        });
    }

    [Test]
    public void Constructor_DefensivelyCopiesMoves_SourceArrayMutationDoesNotAffectPlan() {
        var moves = SampleMoves();
        var originalFirstMove = moves[0];
        var replacementMove = new TiltAdapterMove(TiltMoveAxis.DiagonalB, 9, TiltMoveGroup.Tilt, "tl,9");
        var residuals = new double[] { 0.1, 0.2, 0.3, 0.4 };

        var plan = new TiltAdapterMovePlan(moves, residuals, twistResidualSteps: 0, estimatedSeconds: 10);

        // Mutate the caller's arrays after construction.
        moves[0] = replacementMove;
        residuals[0] = 999.0;

        Assert.Multiple(() => {
            Assert.That(plan.Moves[0], Is.SameAs(originalFirstMove), "Plan's Moves must not reflect post-construction mutation of the source array.");
            Assert.That(plan.ResidualMicronsPerCorner[0], Is.EqualTo(0.1), "Plan's residuals must not reflect post-construction mutation of the source array.");
        });
    }

    [Test]
    public void Constructor_ReturnedCollections_AreNotTheSameReferenceAsSourceArrays() {
        var moves = SampleMoves();
        var residuals = new double[] { 0, 0, 0, 0 };

        var plan = new TiltAdapterMovePlan(moves, residuals, twistResidualSteps: 0, estimatedSeconds: 0);

        Assert.Multiple(() => {
            Assert.That(plan.Moves, Is.Not.SameAs(moves));
            Assert.That(plan.ResidualMicronsPerCorner, Is.Not.SameAs(residuals));
        });
    }

    [Test]
    public void Moves_ReturnedCollection_ThrowsOnMutationAttempt() {
        var plan = new TiltAdapterMovePlan(SampleMoves(), new double[] { 0, 0, 0, 0 }, 0, 0);
        var asList = plan.Moves as IList<TiltAdapterMove>;
        Assert.That(asList, Is.Not.Null, "Expected a read-only wrapper implementing IList<TiltAdapterMove>.");
        Assert.Throws<NotSupportedException>(() => asList[0] = null);
    }

    [Test]
    public void ResidualMicronsPerCorner_ReturnedCollection_ThrowsOnMutationAttempt() {
        var plan = new TiltAdapterMovePlan(SampleMoves(), new double[] { 0, 0, 0, 0 }, 0, 0);
        var asList = plan.ResidualMicronsPerCorner as IList<double>;
        Assert.That(asList, Is.Not.Null, "Expected a read-only wrapper implementing IList<double>.");
        Assert.Throws<NotSupportedException>(() => asList[0] = 999);
    }

    [Test]
    public void Constructor_NullCollections_ProduceEmptyReadOnlyCollections() {
        var plan = new TiltAdapterMovePlan(null, null, twistResidualSteps: 0, estimatedSeconds: 0);
        Assert.Multiple(() => {
            Assert.That(plan.Moves, Is.Empty);
            Assert.That(plan.ResidualMicronsPerCorner, Is.Empty);
        });
    }
}
