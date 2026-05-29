using NINA.Joko.Plugins.HocusFocus.Inspection;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection;

[TestFixture]
public class SensorAberrationCalculatorTests {

    [Test]
    public void IsReducedChiSquaredAcceptable_NullFit_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(null), Is.False);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_NearOne_IsTrue() {
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(1.0)), Is.True);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_SmallValue_IsTrue() {
        // Residuals smaller than the declared σ (reduced χ² well below 1) is still a good fit.
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(0.1)), Is.True);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_LargeValue_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(50.0)), Is.False);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_NaN_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(double.NaN)), Is.False);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_IsIndependentOfGoodnessOfFit() {
        // Magnitude independence: a near-flat sensor (very low R²) with residuals consistent with the
        // measurement errors (reduced χ² ≈ 1) is accepted, where an R² target would have rejected it.
        var nearlyFlatButConsistent = WithReducedChiSquared(1.0);
        SetGoodness(nearlyFlatButConsistent, 0.02);
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(nearlyFlatButConsistent), Is.True);
    }

    [Test]
    public void IsFitTooGood_NullFit_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsFitTooGood(null), Is.False);
    }

    [Test]
    public void IsFitTooGood_LowGoodness_IsFalse() {
        var m = new SensorParaboloidModel();
        m.FromArray(new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 });
        // GoodnessOfFit defaults to 0; (1 - 0) = 1 which is not < 0.005
        Assert.That(SensorAberrationCalculator.IsFitTooGood(m), Is.False);
    }

    [Test]
    public void IsBetterFit_NullThis_IsFalse() {
        var other = new SensorParaboloidModel();
        Assert.That(SensorAberrationCalculator.IsBetterFit(null, other), Is.False);
    }

    [Test]
    public void IsBetterFit_NullOther_IsTrue() {
        var thisFit = new SensorParaboloidModel();
        Assert.That(SensorAberrationCalculator.IsBetterFit(thisFit, null), Is.True);
    }

    [Test]
    public void IsBetterFit_BothNull_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsBetterFit(null, null), Is.False);
    }

    [Test]
    public void IsBetterFit_OtherZeroGoodness_IsTrue() {
        var thisFit = new SensorParaboloidModel();
        var other = new SensorParaboloidModel();
        // Other has GoodnessOfFit = 0 (default), so this is "better"
        Assert.That(SensorAberrationCalculator.IsBetterFit(thisFit, other), Is.True);
    }

    [Test]
    public void IsBetterFit_HigherGoodness_IsTrue() {
        // We can't set GoodnessOfFit directly (private setter populated by EvaluateFit). Use Reflection.
        var thisFit = WithGoodness(0.85);
        var otherFit = WithGoodness(0.75);
        Assert.That(SensorAberrationCalculator.IsBetterFit(thisFit, otherFit), Is.True);
    }

    [Test]
    public void IsBetterFit_EqualGoodness_IsTrue() {
        var thisFit = WithGoodness(0.8);
        var otherFit = WithGoodness(0.8);
        Assert.That(SensorAberrationCalculator.IsBetterFit(thisFit, otherFit), Is.True);
    }

    [Test]
    public void IsBetterFit_LowerGoodness_IsFalse() {
        var thisFit = WithGoodness(0.6);
        var otherFit = WithGoodness(0.85);
        Assert.That(SensorAberrationCalculator.IsBetterFit(thisFit, otherFit), Is.False);
    }

    [Test]
    public void IsBetterFit_OverfitVsHealthy_PrefersHealthy() {
        var thisFit = WithGoodness(0.9999);   // overfit (1 - 0.9999 = 0.0001 < 0.005)
        var otherFit = WithGoodness(0.85);
        Assert.That(SensorAberrationCalculator.IsBetterFit(thisFit, otherFit), Is.False);
        Assert.That(SensorAberrationCalculator.IsBetterFit(otherFit, thisFit), Is.True);
    }

    [Test]
    public void IsBetterFit_BothOverfit_FallsThroughToFalse() {
        var thisFit = WithGoodness(0.9999);
        var otherFit = WithGoodness(0.99995);
        // thisFit is too good, so IsBetterFit returns false even though numerically the same.
        Assert.That(SensorAberrationCalculator.IsBetterFit(thisFit, otherFit), Is.False);
    }

    [Test]
    public void IsFitTooGood_AtBoundary_IsTrue() {
        var fit = WithGoodness(0.999);
        Assert.That(SensorAberrationCalculator.IsFitTooGood(fit), Is.True);
    }

    [Test]
    public void IsFitTooGood_JustBelowBoundary_IsFalse() {
        var fit = WithGoodness(0.99);   // 1 - 0.99 = 0.01 > 0.005
        Assert.That(SensorAberrationCalculator.IsFitTooGood(fit), Is.False);
    }

    [Test]
    public void CreateFullRegionSet_OneByOne_ReturnsSingleFullRegion() {
        var regions = SensorAberrationCalculator.CreateFullRegionSet(new System.Drawing.Size(100, 100), 1, 1);
        Assert.Multiple(() => {
            Assert.That(regions.Count, Is.EqualTo(1));
            Assert.That(regions[0].OuterBoundary.Width, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(regions[0].OuterBoundary.Height, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(regions[0].Index, Is.EqualTo(1));
        });
    }

    [Test]
    public void CreateFullRegionSet_TwoByTwo_TilesIntoFourQuadrants() {
        var regions = SensorAberrationCalculator.CreateFullRegionSet(new System.Drawing.Size(100, 100), 2, 2);
        Assert.That(regions.Count, Is.EqualTo(4));

        // Indices are 1..4 in row-major order
        Assert.That(regions.Select(r => r.Index), Is.EqualTo(new[] { 1, 2, 3, 4 }));

        // First (top-left) starts at (0, 0)
        Assert.Multiple(() => {
            Assert.That(regions[0].OuterBoundary.StartX, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(regions[0].OuterBoundary.StartY, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(regions[0].OuterBoundary.Width, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(regions[0].OuterBoundary.Height, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(regions[3].OuterBoundary.StartX, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(regions[3].OuterBoundary.StartY, Is.EqualTo(0.5).Within(1e-9));
        });
    }

    [Test]
    public void CreateFullRegionSet_3x4_Returns12Regions() {
        var regions = SensorAberrationCalculator.CreateFullRegionSet(new System.Drawing.Size(800, 600), rows: 3, cols: 4);
        Assert.That(regions.Count, Is.EqualTo(12));
        Assert.That(regions[regions.Count - 1].Index, Is.EqualTo(12));
    }

    [Test]
    public void CreateFullRegionSet_Coverage_FillsUnitSquare() {
        var regions = SensorAberrationCalculator.CreateFullRegionSet(new System.Drawing.Size(800, 600), 5, 5);
        // Sum of areas of all regions in ratio space should equal 1.0
        var totalArea = regions.Sum(r => r.OuterBoundary.Width * r.OuterBoundary.Height);
        Assert.That(totalArea, Is.EqualTo(1.0).Within(1e-9));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void CreateFullRegionSet_RejectsNonPositiveRows(int rows) {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SensorAberrationCalculator.CreateFullRegionSet(new System.Drawing.Size(100, 100), rows: rows, cols: 2));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void CreateFullRegionSet_RejectsNonPositiveCols(int cols) {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SensorAberrationCalculator.CreateFullRegionSet(new System.Drawing.Size(100, 100), rows: 2, cols: cols));
    }

    private static SensorParaboloidModel WithGoodness(double goodness) {
        var model = new SensorParaboloidModel();
        SetGoodness(model, goodness);
        return model;
    }

    private static void SetGoodness(SensorParaboloidModel model, double goodness) {
        typeof(SensorParaboloidModel).GetProperty(nameof(SensorParaboloidModel.GoodnessOfFit)).SetValue(model, goodness);
    }

    private static SensorParaboloidModel WithReducedChiSquared(double reducedChiSquared) {
        var model = new SensorParaboloidModel();
        typeof(SensorParaboloidModel).GetProperty(nameof(SensorParaboloidModel.ReducedChiSquared)).SetValue(model, reducedChiSquared);
        return model;
    }
}
