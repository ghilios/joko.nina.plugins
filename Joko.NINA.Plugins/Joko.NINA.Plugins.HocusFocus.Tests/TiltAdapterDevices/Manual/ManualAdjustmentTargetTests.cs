using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual;
using NUnit.Framework;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices.Manual;

/// <summary>
/// Pins the manual pad's nine cells to the exact per-screw effect and wire command each one produces.
///
/// <para>This is the anchor test for the manual adjustment feature, in the same spirit as
/// <c>TiltAdapterMoveTests</c>' unit-effect assertions. A sign error in <see cref="ManualAdjustmentTarget.All"/>
/// is not a rendering bug: it is a motor driven the wrong way, persisted to the device's EEPROM, with no undo
/// available in the plugin. Every cell is asserted in BOTH directions, and the expectations below are written
/// out longhand from the physical corner names rather than derived from the table under test.</para>
/// </summary>
[TestFixture]
public class ManualAdjustmentTargetTests {

    // Expected per-screw effect for direction "+", amount 1, in WIZARD screw order (s1=TR, s2=TL, s3=BL, s4=BR).
    private static readonly object[] PadExpectations = {
        //           key,      axis,                        s1(TR) s2(TL) s3(BL) s4(BR)
        new object[] { "TR", TiltMoveAxis.DiagonalA, new double[] { 1, 0, -1, 0 } },
        new object[] { "BL", TiltMoveAxis.DiagonalA, new double[] { -1, 0, 1, 0 } },
        new object[] { "TL", TiltMoveAxis.DiagonalB, new double[] { 0, 1, 0, -1 } },
        new object[] { "BR", TiltMoveAxis.DiagonalB, new double[] { 0, -1, 0, 1 } },
        new object[] { "Top", TiltMoveAxis.EdgeVertical, new double[] { 1, 1, -1, -1 } },
        new object[] { "Bottom", TiltMoveAxis.EdgeVertical, new double[] { -1, -1, 1, 1 } },
        new object[] { "Right", TiltMoveAxis.EdgeHorizontal, new double[] { 1, -1, -1, 1 } },
        new object[] { "Left", TiltMoveAxis.EdgeHorizontal, new double[] { -1, 1, 1, -1 } },
        new object[] { "All", TiltMoveAxis.Backfocus, new double[] { 1, 1, 1, 1 } },
    };

    [TestCaseSource(nameof(PadExpectations))]
    public void PerScrewEffect_PositiveDirection_MatchesTheDeclaredCorners(string key, TiltMoveAxis expectedAxis, double[] expectedUnitEffect) {
        var target = ManualAdjustmentTarget.ByKey(key);
        var effect = target.PerScrewEffect(positiveDirection: true, amount: 20);

        Assert.Multiple(() => {
            Assert.That(target.Axis, Is.EqualTo(expectedAxis));
            Assert.That(effect.ToArray(), Is.EqualTo(expectedUnitEffect.Select(v => v * 20).ToArray()));
        });
    }

    [TestCaseSource(nameof(PadExpectations))]
    public void PerScrewEffect_NegativeDirection_IsTheExactNegation(string key, TiltMoveAxis expectedAxis, double[] expectedUnitEffect) {
        var target = ManualAdjustmentTarget.ByKey(key);
        var effect = target.PerScrewEffect(positiveDirection: false, amount: 20);

        Assert.Multiple(() => {
            Assert.That(target.Axis, Is.EqualTo(expectedAxis));
            Assert.That(effect.ToArray(), Is.EqualTo(expectedUnitEffect.Select(v => v * -20).ToArray()));
        });
    }

    /// <summary>
    /// The wire command must come from EatCommands, never be assembled from the pad label: the default sign
    /// encoding signs the ARGUMENT rather than switching to the opposite mnemonic, so "BL +20" really goes out
    /// as <c>tr,-20</c>. A chip built from the label would name a command the device never receives.
    /// </summary>
    [TestCase("TR", true, "tr,20")]
    [TestCase("TR", false, "tr,-20")]
    [TestCase("BL", true, "tr,-20")]
    [TestCase("BL", false, "tr,20")]
    [TestCase("TL", true, "tl,20")]
    [TestCase("BR", true, "tl,-20")]
    [TestCase("Top", true, "tp,20")]
    [TestCase("Bottom", true, "tp,-20")]
    [TestCase("Right", true, "rt,20")]
    [TestCase("Left", true, "rt,-20")]
    [TestCase("All", true, "bf,20")]
    [TestCase("All", false, "bf,-20")]
    public void BuildMove_ProducesTheWireCommandTheDeviceWillActuallyReceive(string key, bool positive, string expectedWire) {
        var move = ManualAdjustmentTarget.ByKey(key).BuildMove(positive, 20);

        Assert.That(EatCommands.Format(move), Is.EqualTo(expectedWire));
    }

    [Test]
    public void BuildMove_PerCornerStepsAlwaysAgreeWithPerScrewEffect() {
        foreach (var target in ManualAdjustmentTarget.All) {
            foreach (bool positive in new[] { true, false }) {
                var move = target.BuildMove(positive, 7);
                var effect = target.PerScrewEffect(positive, 7);
                Assert.That(move.PerCornerSteps.ToArray(), Is.EqualTo(effect.ToArray()), $"{target.Key} ({(positive ? "+" : "-")})");
            }
        }
    }

    [Test]
    public void PadIsTheFullNineCellGrid_RowMajor() {
        Assert.Multiple(() => {
            Assert.That(ManualAdjustmentTarget.All.Count, Is.EqualTo(9));
            Assert.That(ManualAdjustmentTarget.All.Select(t => t.Key),
                Is.EqualTo(new[] { "TL", "Top", "TR", "Left", "All", "Right", "BL", "Bottom", "BR" }));
            // Row-major: the declaration order must match the 3x3 layout the ItemsControl renders.
            for (int i = 0; i < 9; ++i) {
                var t = ManualAdjustmentTarget.All[i];
                Assert.That(t.PadRow, Is.EqualTo(i / 3), $"{t.Key} row");
                Assert.That(t.PadColumn, Is.EqualTo(i % 3), $"{t.Key} column");
            }
        });
    }

    [Test]
    public void BackfocusIsTheOnlyBackfocusGroupedCell() {
        foreach (var target in ManualAdjustmentTarget.All) {
            var move = target.BuildMove(positiveDirection: true, amount: 5);
            var expected = target.Key == "All" ? TiltMoveGroup.Backfocus : TiltMoveGroup.Tilt;
            Assert.That(move.Group, Is.EqualTo(expected), target.Key);
        }
    }

    [Test]
    public void DescribeMove_NamesBothCouplesCornersWithTheirMotorNumbers() {
        var move = ManualAdjustmentTarget.ByKey("TR").BuildMove(positiveDirection: true, amount: 20);

        // Display order is TL, TR, BL, BR, so the moving pair reads TR then BL.
        Assert.That(move.Description, Is.EqualTo("TR (Motor 1) +20 · BL (Motor 4) −20 steps"));
    }

    [Test]
    public void DescribeMove_BackfocusSaysAllFourMoveTogether() {
        var move = ManualAdjustmentTarget.ByKey("All").BuildMove(positiveDirection: false, amount: 50);

        Assert.That(move.Description, Is.EqualTo("All four motors −50 steps together"));
    }

    // ------------------------------------------------------------------------------------------------------
    // The three index spaces. Cross-checked against EatWizardMapping (the wizard's own corner labels) and
    // EatTiltMotionController's device motor order, so this table cannot drift from either.
    // ------------------------------------------------------------------------------------------------------

    [TestCase(1, "TR", 1)]
    [TestCase(2, "TL", 2)]
    [TestCase(3, "BL", 4)]
    [TestCase(4, "BR", 3)]
    public void CornerTable_MapsWizardScrewToLabelAndDeviceMotor(int wizardScrew, string expectedLabel, int expectedMotor) {
        var corner = TiltAdapterCorner.ForWizardScrew(wizardScrew);

        Assert.Multiple(() => {
            Assert.That(corner.Label, Is.EqualTo(expectedLabel));
            Assert.That(corner.DeviceMotorNumber, Is.EqualTo(expectedMotor));
            Assert.That(corner.WizardScrewNumber, Is.EqualTo(wizardScrew));
            // The wizard's own mapping is the authority for the label.
            Assert.That(EatWizardMapping.CornerLabelForWizardScrew(wizardScrew), Is.EqualTo(expectedLabel));
        });
    }

    [Test]
    public void CornerTable_DeviceMotorOrderMatchesTheControllerPermutation() {
        // PermuteWizardToDeviceMotorOrder is [w0, w1, w3, w2]; applying it to a vector tagged by wizard index
        // must land each corner at its declared device motor slot.
        var wizardTagged = new double[] { 1, 2, 3, 4 }; // value == wizard screw number
        var deviceOrder = EatTiltMotionController.PermuteWizardToDeviceMotorOrder(wizardTagged);

        for (int deviceIndex = 0; deviceIndex < 4; ++deviceIndex) {
            var corner = TiltAdapterCorner.InDeviceMotorOrder[deviceIndex];
            Assert.That(deviceOrder[deviceIndex], Is.EqualTo((double)corner.WizardScrewNumber),
                $"device motor {deviceIndex + 1} ({corner.Label})");
        }
    }

    [Test]
    public void CornerTable_DisplayOrderIsTheTwoByTwoReadingOrder() {
        Assert.That(TiltAdapterCorner.InDisplayOrder.Select(c => c.Label), Is.EqualTo(new[] { "TL", "TR", "BL", "BR" }));
    }
}
