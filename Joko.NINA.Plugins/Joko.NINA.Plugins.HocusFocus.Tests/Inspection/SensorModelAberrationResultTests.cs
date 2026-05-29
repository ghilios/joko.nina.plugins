using NINA.Joko.Plugins.HocusFocus.Inspection;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection;

[TestFixture]
public class SensorModelAberrationResultTests {

    private const double MinRSquared = 0.05;   // reduced χ² cap is the fixed SensorAberrationCalculator constant (5.0)

    [Test]
    public void BuildFitQualityResults_ProducesBothRowsWithExpectedNames() {
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.8, reducedChiSquared: 1.0), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Name, Is.EqualTo("Model Fit (R²)"));
            Assert.That(reducedChiSquared.Name, Is.EqualTo("Reduced χ²"));
            Assert.That(rSquared.Value, Does.Contain("R²"));
            Assert.That(reducedChiSquared.Value, Does.Not.Contain("R²"));
        });
    }

    [Test]
    public void BuildFitQualityResults_FlatSensorGoodChiSquaredLowRSquared_BothAcceptable() {
        // A near-flat sensor: low R² but χ² within the cap → model accepted → both rows acceptable.
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.02, reducedChiSquared: 1.0), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Acceptable, Is.True);
            Assert.That(reducedChiSquared.Acceptable, Is.True);
        });
    }

    [Test]
    public void BuildFitQualityResults_AdequateRSquaredHighChiSquared_BothAcceptable() {
        // The user's case: a well-fit model (adequate R²) with a huge reduced χ². χ² only matters when R²
        // is very low, so the model is accepted and neither row is flagged.
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.61, reducedChiSquared: 11427.48), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Acceptable, Is.True);
            Assert.That(reducedChiSquared.Acceptable, Is.True);
        });
    }

    [Test]
    public void BuildFitQualityResults_LowRSquaredHighChiSquared_BothRowsFail() {
        // Very low R² AND χ² above the cap → model rejected → both rows flagged.
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.02, reducedChiSquared: 50.0), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Acceptable, Is.False);
            Assert.That(reducedChiSquared.Acceptable, Is.False);
        });
    }

    [Test]
    public void BuildFitQualityResults_AdequateRSquaredGoodChiSquared_BothAcceptable() {
        var (rSquared, reducedChiSquared) = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.8, reducedChiSquared: 1.0), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(rSquared.Acceptable, Is.True);
            Assert.That(reducedChiSquared.Acceptable, Is.True);
        });
    }

    private static SensorParaboloidModel BuildModel(double goodness, double reducedChiSquared) {
        var model = new SensorParaboloidModel(x0: 0, y0: 0, z0: 30000, gx: 0.001, gy: 0.001, k: 1e-6);
        typeof(SensorParaboloidModel).GetProperty(nameof(SensorParaboloidModel.GoodnessOfFit)).SetValue(model, goodness);
        typeof(SensorParaboloidModel).GetProperty(nameof(SensorParaboloidModel.ReducedChiSquared)).SetValue(model, reducedChiSquared);
        return model;
    }
}
