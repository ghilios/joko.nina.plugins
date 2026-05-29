using NINA.Joko.Plugins.HocusFocus.Inspection;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection;

[TestFixture]
public class SensorModelAberrationResultTests {

    private const double MaxChi = 5.0;       // good/marginal boundary = 0.6 * 5 = 3.0
    private const double MinRSquared = 0.05;

    [Test]
    public void BuildFitQualityResults_ProducesBothRowsWithExpectedNames() {
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.8, reducedChiSquared: 1.0), MaxChi, MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Name, Is.EqualTo("Model Fit (R²)"));
            Assert.That(reducedChiSquared.Name, Is.EqualTo("Reduced χ²"));
            Assert.That(rSquared.Value, Does.Contain("R²"));
            Assert.That(reducedChiSquared.Value, Does.Not.Contain("R²"));
        });
    }

    [Test]
    public void BuildFitQualityResults_FlatSensorGoodChiSquaredLowRSquared_BothAcceptable() {
        // A near-flat sensor: low R² but χ² well within the good band → both rows acceptable.
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.02, reducedChiSquared: 1.0), MaxChi, MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Acceptable, Is.True, "Low R² alone must not fail the R² row while χ² is good");
            Assert.That(reducedChiSquared.Acceptable, Is.True);
        });
    }

    [Test]
    public void BuildFitQualityResults_LowRSquaredElevatedChiSquared_OnlyRSquaredRowFails() {
        // Low R² AND elevated χ² (>= 0.6*max=3.0 but < max=5.0): the R² row now contributes to rejection,
        // while the χ² row is still within the acceptable cap (marginal).
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.02, reducedChiSquared: 3.5), MaxChi, MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Acceptable, Is.False);
            Assert.That(reducedChiSquared.Acceptable, Is.True);
        });
    }

    [Test]
    public void BuildFitQualityResults_AdequateRSquaredElevatedChiSquared_BothAcceptable() {
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.5, reducedChiSquared: 3.5), MaxChi, MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Acceptable, Is.True);
            Assert.That(reducedChiSquared.Acceptable, Is.True);
        });
    }

    [Test]
    public void BuildFitQualityResults_ChiSquaredAboveMax_OnlyChiSquaredRowFails_WhenRSquaredAdequate() {
        // χ² above the cap → χ² row rejected. R² is adequate, so it does not contribute → R² row acceptable.
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.8, reducedChiSquared: 6.0), MaxChi, MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Acceptable, Is.True);
            Assert.That(reducedChiSquared.Acceptable, Is.False);
        });
    }

    [Test]
    public void BuildFitQualityResults_ChiSquaredAboveMaxAndLowRSquared_BothRowsFail() {
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.02, reducedChiSquared: 6.0), MaxChi, MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Acceptable, Is.False);
            Assert.That(reducedChiSquared.Acceptable, Is.False);
        });
    }

    private static SensorParaboloidModel BuildModel(double goodness, double reducedChiSquared) {
        var model = new SensorParaboloidModel(x0: 0, y0: 0, z0: 30000, gx: 0.001, gy: 0.001, k: 1e-6);
        typeof(SensorParaboloidModel).GetProperty(nameof(SensorParaboloidModel.GoodnessOfFit)).SetValue(model, goodness);
        typeof(SensorParaboloidModel).GetProperty(nameof(SensorParaboloidModel.ReducedChiSquared)).SetValue(model, reducedChiSquared);
        return model;
    }
}
