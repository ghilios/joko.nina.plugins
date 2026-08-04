#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices.AsgEat;

[TestFixture]
public class EatWizardMappingTests {

    // Expected per-corner vectors below are HARD-CODED literals derived independently from the wizard's
    // StepInstructionsText wording (TiltAdapterWizardVM.cs) -- never computed from MoveForStep on either
    // side of an assertion. A sign error here is exactly the class of bug the plan calls out as the
    // highest-consequence failure mode (EEPROM-persisted wrong-way motor moves), so this test intentionally
    // does not trust the implementation to check itself.
    //
    // N = 150 is the EAT preset's default CalibrationAppliedAmount; N = 30 is a second, unrelated value used
    // to make sure the mapping isn't accidentally hard-coded to 150 anywhere.

    [TestCase(150)]
    [TestCase(30)]
    public void MoveForStep_Baseline_ReturnsNull(int n) {
        // "Ensure all screws are at their starting position, then click Run Measurement." -- measurement
        // only, no move.
        Assert.That(EatWizardMapping.MoveForStep(WizardStep.Baseline, n), Is.Null);
    }

    [TestCase(150)]
    [TestCase(30)]
    public void MoveForStep_AllInward_MatchesWizardInstruction(int n) {
        // "Apply +N steps to EVERY motor, then click Run Measurement." -> all four wizard screws +N.
        var expected = new double[] { n, n, n, n };
        var move = EatWizardMapping.MoveForStep(WizardStep.AllInward, n);
        Assert.Multiple(() => {
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.Backfocus));
            Assert.That(move.Steps, Is.EqualTo(n));
            Assert.That(move.Group, Is.EqualTo(TiltMoveGroup.Backfocus));
            Assert.That(move.PerCornerSteps, Is.EqualTo(expected));
        });
    }

    [TestCase(150)]
    [TestCase(30)]
    public void MoveForStep_ReBaseline1_MatchesWizardInstruction(int n) {
        // "Apply -N steps to every motor, returning to the baseline position, then click Run Measurement."
        var expected = new double[] { -n, -n, -n, -n };
        var move = EatWizardMapping.MoveForStep(WizardStep.ReBaseline1, n);
        Assert.Multiple(() => {
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.Backfocus));
            Assert.That(move.Steps, Is.EqualTo(-n));
            Assert.That(move.Group, Is.EqualTo(TiltMoveGroup.Backfocus));
            Assert.That(move.PerCornerSteps, Is.EqualTo(expected));
        });
    }

    [TestCase(150)]
    [TestCase(30)]
    public void MoveForStep_Screw1_MatchesWizardInstruction(int n) {
        // "Apply +N steps to motor 1 and -N steps to motor 3, then click Run Measurement." -> wizard-screw1
        // +N, wizard-screw3 -N (wizard indices 1..4 at array positions [0..3]).
        var expected = new double[] { n, 0, -n, 0 };
        var move = EatWizardMapping.MoveForStep(WizardStep.Screw1, n);
        Assert.Multiple(() => {
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
            Assert.That(move.Steps, Is.EqualTo(n));
            Assert.That(move.Group, Is.EqualTo(TiltMoveGroup.Tilt));
            Assert.That(move.PerCornerSteps, Is.EqualTo(expected));
        });
    }

    [TestCase(150)]
    [TestCase(30)]
    public void MoveForStep_ReBaseline2_MatchesWizardInstruction(int n) {
        // "Apply -N steps to motor 1 and +N steps to motor 3, returning to the baseline position, then
        // click Run Measurement."
        var expected = new double[] { -n, 0, n, 0 };
        var move = EatWizardMapping.MoveForStep(WizardStep.ReBaseline2, n);
        Assert.Multiple(() => {
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
            Assert.That(move.Steps, Is.EqualTo(-n));
            Assert.That(move.Group, Is.EqualTo(TiltMoveGroup.Tilt));
            Assert.That(move.PerCornerSteps, Is.EqualTo(expected));
        });
    }

    [TestCase(150)]
    [TestCase(30)]
    public void MoveForStep_Screw2_MatchesWizardInstruction(int n) {
        // "Apply +N steps to motor 2 and -N steps to motor 4, then click Run Measurement." -> wizard-screw2
        // +N, wizard-screw4 -N.
        var expected = new double[] { 0, n, 0, -n };
        var move = EatWizardMapping.MoveForStep(WizardStep.Screw2, n);
        Assert.Multiple(() => {
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.DiagonalB));
            Assert.That(move.Steps, Is.EqualTo(n));
            Assert.That(move.Group, Is.EqualTo(TiltMoveGroup.Tilt));
            Assert.That(move.PerCornerSteps, Is.EqualTo(expected));
        });
    }

    [TestCase(150)]
    [TestCase(30)]
    public void MoveForStep_Complete_MatchesWizardInstruction(int n) {
        // "Return all motors to their original position." -- the restore move after Screw2; must be exactly
        // the move that returns (0,+N,0,-N) back to (0,0,0,0).
        var expected = new double[] { 0, -n, 0, n };
        var move = EatWizardMapping.MoveForStep(WizardStep.Complete, n);
        Assert.Multiple(() => {
            Assert.That(move.Axis, Is.EqualTo(TiltMoveAxis.DiagonalB));
            Assert.That(move.Steps, Is.EqualTo(-n));
            Assert.That(move.Group, Is.EqualTo(TiltMoveGroup.Tilt));
            Assert.That(move.PerCornerSteps, Is.EqualTo(expected));
        });
    }

    [Test]
    public void MoveForStep_UnknownStep_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => EatWizardMapping.MoveForStep((WizardStep)999, 150));
    }

    // --- Inverse relationships: the "Re-Baseline"/"Complete" steps are literally the recovery moves for the
    // preceding perturbation step. ---

    [TestCase(150)]
    [TestCase(30)]
    public void ReBaseline1_IsNegationOfAllInward(int n) {
        var allInward = EatWizardMapping.MoveForStep(WizardStep.AllInward, n);
        var reBaseline1 = EatWizardMapping.MoveForStep(WizardStep.ReBaseline1, n);
        var negated = allInward.PerCornerSteps.Select(v => -v).ToArray();
        Assert.That(reBaseline1.PerCornerSteps, Is.EqualTo(negated));
    }

    [TestCase(150)]
    [TestCase(30)]
    public void ReBaseline2_IsNegationOfScrew1(int n) {
        var screw1 = EatWizardMapping.MoveForStep(WizardStep.Screw1, n);
        var reBaseline2 = EatWizardMapping.MoveForStep(WizardStep.ReBaseline2, n);
        var negated = screw1.PerCornerSteps.Select(v => -v).ToArray();
        Assert.That(reBaseline2.PerCornerSteps, Is.EqualTo(negated));
    }

    [TestCase(150)]
    [TestCase(30)]
    public void Complete_IsNegationOfScrew2(int n) {
        var screw2 = EatWizardMapping.MoveForStep(WizardStep.Screw2, n);
        var complete = EatWizardMapping.MoveForStep(WizardStep.Complete, n);
        var negated = screw2.PerCornerSteps.Select(v => -v).ToArray();
        Assert.That(complete.PerCornerSteps, Is.EqualTo(negated));
    }

    // --- Full-sequence-returns-to-baseline: the cumulative device position after the executed 6-step
    // sequence (Baseline itself moves nothing) must be exactly (0,0,0,0). If this fails, the mapping is
    // wrong and must not ship -- see the plan's mandatory sequence trace. ---

    [TestCase(150)]
    [TestCase(30)]
    public void FullSequence_SumsToZeroPerCorner(int n) {
        var executedSteps = new[] {
            WizardStep.AllInward,
            WizardStep.ReBaseline1,
            WizardStep.Screw1,
            WizardStep.ReBaseline2,
            WizardStep.Screw2,
            WizardStep.Complete,
        };

        var total = new double[] { 0, 0, 0, 0 };
        foreach (var step in executedSteps) {
            var move = EatWizardMapping.MoveForStep(step, n);
            for (int i = 0; i < 4; i++) {
                total[i] += move.PerCornerSteps[i];
            }
        }

        Assert.That(total, Is.EqualTo(new double[] { 0, 0, 0, 0 }));
    }

    // --- Optional measured final re-baseline (Task 6): ReBaseline3 substitutes for Complete's restore move ---

    [TestCase(150)]
    public void FullSequence_WithFinalRebaseline_SumsToZeroPerCorner(int n) {
        var executedSteps = new[] { WizardStep.AllInward, WizardStep.ReBaseline1, WizardStep.Screw1,
                                    WizardStep.ReBaseline2, WizardStep.Screw2, WizardStep.ReBaseline3,
                                    WizardStep.Complete };
        var total = new double[] { 0, 0, 0, 0 };
        foreach (var step in executedSteps) {
            var move = EatWizardMapping.MoveForStep(step, n, measuredFinalRebaseline: true);
            if (move == null) continue;   // Complete is a no-move when ReBaseline3 already restored
            for (int i = 0; i < 4; i++) { total[i] += move.PerCornerSteps[i]; }
        }
        Assert.That(total, Is.EqualTo(new double[] { 0, 0, 0, 0 }));
    }

    [Test]
    public void ReBaseline3_IsDiagonalBRestore_AndCompleteBecomesNoMove() {
        var rb3 = EatWizardMapping.MoveForStep(WizardStep.ReBaseline3, 150, measuredFinalRebaseline: true);
        Assert.Multiple(() => {
            Assert.That(rb3.Axis, Is.EqualTo(TiltMoveAxis.DiagonalB));
            Assert.That(rb3.Steps, Is.EqualTo(-150));
            Assert.That(EatWizardMapping.MoveForStep(WizardStep.Complete, 150, measuredFinalRebaseline: true), Is.Null);
            Assert.That(EatWizardMapping.MoveForStep(WizardStep.Complete, 150, measuredFinalRebaseline: false).Steps, Is.EqualTo(-150));
        });
    }

    // --- InverseMove ---

    [Test]
    public void InverseMove_NegatesSteps_PreservesAxisAndGroup() {
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 42, TiltMoveGroup.Tilt, "forward move");
        var inverse = EatWizardMapping.InverseMove(move);
        Assert.Multiple(() => {
            Assert.That(inverse.Axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
            Assert.That(inverse.Steps, Is.EqualTo(-42));
            Assert.That(inverse.Group, Is.EqualTo(TiltMoveGroup.Tilt));
        });
    }

    [Test]
    public void InverseMove_OfNegativeSteps_ProducesPositiveSteps() {
        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, -17, TiltMoveGroup.Backfocus, "backward move");
        var inverse = EatWizardMapping.InverseMove(move);
        Assert.That(inverse.Steps, Is.EqualTo(17));
    }

    [Test]
    public void InverseMove_PerCornerSteps_IsNegationOfOriginal() {
        var move = new TiltAdapterMove(TiltMoveAxis.EdgeVertical, 6, TiltMoveGroup.Tilt, "edge move");
        var inverse = EatWizardMapping.InverseMove(move);
        var negated = move.PerCornerSteps.Select(v => -v).ToArray();
        Assert.That(inverse.PerCornerSteps, Is.EqualTo(negated));
    }

    [Test]
    public void InverseMove_Null_ReturnsNull() {
        Assert.That(EatWizardMapping.InverseMove(null), Is.Null);
    }

    // --- CornerLabelForWizardScrew ---

    [TestCase(1, "TR")]
    [TestCase(2, "TL")]
    [TestCase(3, "BL")]
    [TestCase(4, "BR")]
    public void CornerLabelForWizardScrew_ReturnsExpectedLabel(int wizardScrew, string expected) {
        Assert.That(EatWizardMapping.CornerLabelForWizardScrew(wizardScrew), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(5)]
    [TestCase(-1)]
    public void CornerLabelForWizardScrew_OutOfRange_Throws(int wizardScrew) {
        Assert.Throws<ArgumentOutOfRangeException>(() => EatWizardMapping.CornerLabelForWizardScrew(wizardScrew));
    }
}
