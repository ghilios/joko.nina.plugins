using NINA.Joko.Plugins.HocusFocus.Inspection;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection;

[TestFixture]
public class SensorAberrationCalculatorTests {

    private const double MaxChi = SensorAberrationCalculator.DefaultAcceptableReducedChiSquared;   // 5.0
    private const double MinRSquared = SensorAberrationCalculator.DefaultAcceptableRSquaredMin;     // 0.05

    [Test]
    public void IsReducedChiSquaredAcceptable_NullFit_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(null, MaxChi), Is.False);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_NearOne_IsTrue() {
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(1.0), MaxChi), Is.True);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_SmallValue_IsTrue() {
        // Residuals smaller than the declared σ (reduced χ² well below 1) is still a good fit.
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(0.1), MaxChi), Is.True);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_LargeValue_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(50.0), MaxChi), Is.False);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_NaN_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(double.NaN), MaxChi), Is.False);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_Infinity_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(double.PositiveInfinity), MaxChi), Is.False);
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_RespectsConfiguredMax() {
        // A χ² of 8 is rejected at the default cap of 5 but accepted when the cap is raised to 10.
        Assert.Multiple(() => {
            Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(8.0), MaxChi), Is.False);
            Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(WithReducedChiSquared(8.0), 10.0), Is.True);
        });
    }

    [Test]
    public void IsReducedChiSquaredAcceptable_IsIndependentOfGoodnessOfFit() {
        // Magnitude independence: a near-flat sensor (very low R²) with residuals consistent with the
        // measurement errors (reduced χ² ≈ 1) is accepted, where an R² target would have rejected it.
        var nearlyFlatButConsistent = WithReducedChiSquared(1.0);
        SetGoodness(nearlyFlatButConsistent, 0.02);
        Assert.That(SensorAberrationCalculator.IsReducedChiSquaredAcceptable(nearlyFlatButConsistent, MaxChi), Is.True);
    }

    // --- IsModelAcceptable: combined R²/χ² rejection rule (truth table) ---

    [Test]
    public void IsModelAcceptable_NullFit_IsFalse() {
        Assert.That(SensorAberrationCalculator.IsModelAcceptable(null, MaxChi, MinRSquared), Is.False);
    }

    [Test]
    public void IsModelAcceptable_GoodChiSquaredLowRSquared_Accepts() {
        // Flat sensor: χ² ≈ 1 (well below the 0.6·max=3.0 good band) with very low R² → accept.
        Assert.That(SensorAberrationCalculator.IsModelAcceptable(WithFit(reducedChiSquared: 1.0, goodness: 0.02), MaxChi, MinRSquared), Is.True);
    }

    [Test]
    public void IsModelAcceptable_MarginalChiSquaredLowRSquared_Rejects() {
        // χ² ≥ 0.6·max=3.0 AND R² below min → the low R² now matters → reject.
        Assert.That(SensorAberrationCalculator.IsModelAcceptable(WithFit(reducedChiSquared: 3.5, goodness: 0.02), MaxChi, MinRSquared), Is.False);
    }

    [Test]
    public void IsModelAcceptable_MarginalChiSquaredAdequateRSquared_Accepts() {
        // Same elevated χ² but R² above min → accept.
        Assert.That(SensorAberrationCalculator.IsModelAcceptable(WithFit(reducedChiSquared: 3.5, goodness: 0.5), MaxChi, MinRSquared), Is.True);
    }

    [Test]
    public void IsModelAcceptable_ChiSquaredAboveMax_RejectsRegardlessOfRSquared() {
        // Above the cap, a high R² cannot rescue the fit (acceptance uses χ² <= max, so values strictly
        // above the cap are rejected even when R² is excellent).
        Assert.Multiple(() => {
            Assert.That(SensorAberrationCalculator.IsModelAcceptable(WithFit(reducedChiSquared: 6.0, goodness: 0.99), MaxChi, MinRSquared), Is.False);
            Assert.That(SensorAberrationCalculator.IsModelAcceptable(WithFit(reducedChiSquared: 5.0001, goodness: 0.99), MaxChi, MinRSquared), Is.False);
        });
    }

    [Test]
    public void IsModelAcceptable_NaNChiSquared_Rejects() {
        Assert.That(SensorAberrationCalculator.IsModelAcceptable(WithFit(reducedChiSquared: double.NaN, goodness: 0.99), MaxChi, MinRSquared), Is.False);
    }

    [Test]
    public void IsModelAcceptable_InfinityChiSquared_Rejects() {
        Assert.That(SensorAberrationCalculator.IsModelAcceptable(WithFit(reducedChiSquared: double.PositiveInfinity, goodness: 0.99), MaxChi, MinRSquared), Is.False);
    }

    // --- ClassifyReducedChiSquared: display-band boundaries (good band = 0.6·max = 3.0) ---

    [Test]
    public void ClassifyReducedChiSquared_JustBelowGoodBand_IsGood() {
        Assert.That(SensorAberrationCalculator.ClassifyReducedChiSquared(2.99, MaxChi), Is.EqualTo(SensorAberrationCalculator.FitQualityBand.Good));
    }

    [Test]
    public void ClassifyReducedChiSquared_AtGoodBand_IsMarginal() {
        Assert.That(SensorAberrationCalculator.ClassifyReducedChiSquared(3.0, MaxChi), Is.EqualTo(SensorAberrationCalculator.FitQualityBand.Marginal));
    }

    [Test]
    public void ClassifyReducedChiSquared_JustBelowMax_IsMarginal() {
        Assert.That(SensorAberrationCalculator.ClassifyReducedChiSquared(4.99, MaxChi), Is.EqualTo(SensorAberrationCalculator.FitQualityBand.Marginal));
    }

    [Test]
    public void ClassifyReducedChiSquared_AtMax_IsRejected() {
        Assert.That(SensorAberrationCalculator.ClassifyReducedChiSquared(5.0, MaxChi), Is.EqualTo(SensorAberrationCalculator.FitQualityBand.Rejected));
    }

    [Test]
    public void ClassifyReducedChiSquared_NaN_IsRejected() {
        Assert.That(SensorAberrationCalculator.ClassifyReducedChiSquared(double.NaN, MaxChi), Is.EqualTo(SensorAberrationCalculator.FitQualityBand.Rejected));
    }

    [Test]
    public void ClassifyReducedChiSquared_Infinity_IsRejected() {
        Assert.That(SensorAberrationCalculator.ClassifyReducedChiSquared(double.PositiveInfinity, MaxChi), Is.EqualTo(SensorAberrationCalculator.FitQualityBand.Rejected));
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

    private static SensorParaboloidModel WithFit(double reducedChiSquared, double goodness) {
        var model = WithReducedChiSquared(reducedChiSquared);
        SetGoodness(model, goodness);
        return model;
    }
}
