using NINA.Joko.Plugins.HocusFocus.Inspection;
using NUnit.Framework;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection;

[TestFixture]
public class SensorModelAberrationResultTests {

    private const double MinRSquared = 0.05;   // reduced χ² cap is the fixed SensorAberrationCalculator constant (5.0)

    [Test]
    public void BuildFitQualityResults_AdequateRSquared_ShowsOnlyRSquaredRow() {
        // Reduced χ² is irrelevant while R² is adequate, so its row is omitted from the table.
        var results = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.8, reducedChiSquared: 1.0), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(results.Select(r => r.Name), Is.EqualTo(new[] { "Model Fit (R²)" }));
            Assert.That(results[0].Acceptable, Is.True);
            Assert.That(results[0].Value, Does.Contain("R²"));
        });
    }

    [Test]
    public void BuildFitQualityResults_AdequateRSquaredHighChiSquared_HidesReducedChiSquaredRow() {
        // The user's case: adequate R² with a huge reduced χ² → χ² row hidden, model accepted.
        var results = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.61, reducedChiSquared: 11427.48), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(results.Any(r => r.Name == "Reduced χ²"), Is.False);
            Assert.That(results.Single().Acceptable, Is.True);
        });
    }

    [Test]
    public void BuildFitQualityResults_LowRSquaredGoodChiSquared_ShowsBothRowsAcceptable() {
        // Low R²: the reduced χ² row appears (it now matters); good χ² keeps the model accepted.
        var results = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.02, reducedChiSquared: 1.0), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(results.Select(r => r.Name), Is.EqualTo(new[] { "Model Fit (R²)", "Reduced χ²" }));
            Assert.That(results.All(r => r.Acceptable), Is.True);
        });
    }

    [Test]
    public void BuildFitQualityResults_LowRSquaredHighChiSquared_ShowsBothRowsRejected() {
        // Low R² AND χ² above the cap → model rejected → both rows shown and flagged.
        var results = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: 0.02, reducedChiSquared: 50.0), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(results.Select(r => r.Name), Is.EqualTo(new[] { "Model Fit (R²)", "Reduced χ²" }));
            Assert.That(results.All(r => !r.Acceptable), Is.True);
            Assert.That(results.Single(r => r.Name == "Reduced χ²").Value, Does.Not.Contain("R²"));
        });
    }

    [Test]
    public void BuildFitQualityResults_ReducedChiSquaredRowAppearsExactlyAtThreshold() {
        // R² exactly at the min is NOT "< min", so the χ² row stays hidden; just below it appears.
        var atThreshold = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: MinRSquared, reducedChiSquared: 1.0), MinRSquared);
        var belowThreshold = SensorModelAberrationResult.BuildFitQualityResults(BuildModel(goodness: MinRSquared - 0.001, reducedChiSquared: 1.0), MinRSquared);
        Assert.Multiple(() => {
            Assert.That(atThreshold.Any(r => r.Name == "Reduced χ²"), Is.False);
            Assert.That(belowThreshold.Any(r => r.Name == "Reduced χ²"), Is.True);
        });
    }

    private static SensorParaboloidModel BuildModel(double goodness, double reducedChiSquared) {
        var model = new SensorParaboloidModel(x0: 0, y0: 0, z0: 30000, gx: 0.001, gy: 0.001, k: 1e-6);
        typeof(SensorParaboloidModel).GetProperty(nameof(SensorParaboloidModel.GoodnessOfFit)).SetValue(model, goodness);
        typeof(SensorParaboloidModel).GetProperty(nameof(SensorParaboloidModel.ReducedChiSquared)).SetValue(model, reducedChiSquared);
        return model;
    }
}
