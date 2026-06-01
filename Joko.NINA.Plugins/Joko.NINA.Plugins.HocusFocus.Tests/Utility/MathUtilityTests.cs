using System;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot;
using OxyPlot.Series;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class MathUtilityTests {

    [Test]
    public void MedianMAD_ScalesOddLengthMedianAbsoluteDeviation() {
        var (median, mad) = new[] { 1.0, 2.0, 3.0 }.MedianMAD();

        Assert.Multiple(() => {
            Assert.That(median, Is.EqualTo(2.0));
            Assert.That(mad, Is.EqualTo(1.483).Within(1e-12));
        });
    }

    [Test]
    public void MedianMAD_ScalesEvenLengthMedianAbsoluteDeviation() {
        var (median, mad) = new[] { 1.0, 2.0, 4.0, 100.0 }.MedianMAD();

        Assert.Multiple(() => {
            Assert.That(median, Is.EqualTo(3.0));
            Assert.That(mad, Is.EqualTo(2.2245).Within(1e-12));
        });
    }

    [Test]
    public void MedianMAD_EmptyInput_ReturnsNaNPair() {
        var (median, mad) = Array.Empty<double>().MedianMAD();
        Assert.Multiple(() => {
            Assert.That(double.IsNaN(median), Is.True);
            Assert.That(double.IsNaN(mad), Is.True);
        });
    }

    [Test]
    public void MedianMAD_SingleValue_HasZeroDispersion() {
        var (median, mad) = new[] { 42.0 }.MedianMAD();
        Assert.Multiple(() => {
            Assert.That(median, Is.EqualTo(42.0));
            Assert.That(mad, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void MeanVar_ComputesSampleMeanAndVariance() {
        var (mean, variance) = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 }.MeanVar();
        // Classic example: mean = 5, sample variance (N-1) = 4.571428...
        Assert.Multiple(() => {
            Assert.That(mean, Is.EqualTo(5.0).Within(1e-12));
            Assert.That(variance, Is.EqualTo(32.0 / 7.0).Within(1e-12));
        });
    }

    [Test]
    public void RadiansToDegrees_PiEqualsOneEighty() {
        Assert.That(MathUtility.RadiansToDegrees(Math.PI), Is.EqualTo(180.0).Within(1e-12));
    }

    [Test]
    public void RadiansToDegrees_HalfPiEqualsNinety() {
        Assert.That(MathUtility.RadiansToDegrees(Math.PI / 2.0), Is.EqualTo(90.0).Within(1e-12));
    }

    [Test]
    public void ArcsecPerPixel_TypicalSetup_MatchesExpectedPlateScale() {
        // Plate scale formula (in arcsec/px): 206.265 * (pixelSize_um / focalLength_mm)
        // For 3.76 um pixels at 600 mm focal length => ~1.292 arcsec/px
        var arcsec = MathUtility.ArcsecPerPixel(pixelSize: 3.76, focalLength: 600.0);
        Assert.That(arcsec, Is.EqualTo(1.2926).Within(1e-3));
    }

    [Test]
    public void DotProduct_KnownVectors_ComputesSum() {
        var x = new float[] { 1f, 2f, 3f };
        var y = new float[] { 4f, 5f, 6f };
        Assert.That(MathUtility.DotProduct(x, y), Is.EqualTo(32.0f).Within(1e-6f));
    }

    [Test]
    public void DotProduct_LengthMismatch_Throws() {
        Assert.That(
            () => MathUtility.DotProduct(new float[] { 1f }, new float[] { 1f, 2f }),
            Throws.ArgumentException);
    }

    [Test]
    public void SumOfSquaresOfDifferences_KnownValues_ComputesSum() {
        var x = new float[] { 1f, 2f, 3f };
        var y = new float[] { 4f, 4f, 6f };
        // diffs: 3, 2, 3 -> 9 + 4 + 9 = 22
        Assert.That(MathUtility.SumOfSquaresOfDifferences(x, y), Is.EqualTo(22.0f).Within(1e-6f));
    }

    [Test]
    public void SumOfSquaresOfDifferences_LengthMismatch_Throws() {
        Assert.That(
            () => MathUtility.SumOfSquaresOfDifferences(new float[] { 1f }, new float[] { 1f, 2f }),
            Throws.ArgumentException);
    }

    [Test]
    public void Swap_DistinctIndices_ExchangesValues() {
        var arr = new[] { 10, 20, 30 };
        arr.Swap(0, 2);
        Assert.That(arr, Is.EqualTo(new[] { 30, 20, 10 }));
    }

    [Test]
    public void Swap_SameIndex_NoOp() {
        var arr = new[] { 1, 2, 3 };
        arr.Swap(1, 1);
        Assert.That(arr, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void NthOrderStatisticFloat_ReturnsKthSmallest() {
        var arr = new[] { 7f, 1f, 3f, 9f, 2f, 5f };
        // Pre-sorted: 1, 2, 3, 5, 7, 9
        Assert.Multiple(() => {
            Assert.That(arr.NthOrderStatisticFloat(0), Is.EqualTo(1f));
            // After the previous call the array is partially partitioned; reset.
            var arr2 = new[] { 7f, 1f, 3f, 9f, 2f, 5f };
            Assert.That(arr2.NthOrderStatisticFloat(2), Is.EqualTo(3f));
            var arr3 = new[] { 7f, 1f, 3f, 9f, 2f, 5f };
            Assert.That(arr3.NthOrderStatisticFloat(5), Is.EqualTo(9f));
        });
    }

    [Test]
    public void MedianFloat_OddLength_ReturnsMiddleElement() {
        var arr = new[] { 5f, 1f, 9f };
        Assert.That(arr.MedianFloat(), Is.EqualTo(5f));
    }

    [Test]
    public void MedianFloat_EvenLength_ReturnsLowerMiddleElement() {
        // Implementation defines median for even length as the (N-1)/2 ordered element,
        // which is the lower-middle value (no averaging of the two middles).
        var arr = new[] { 4f, 1f, 3f, 2f };
        // sorted: 1, 2, 3, 4 -> (4-1)/2 = 1 -> arr[1] == 2
        Assert.That(arr.MedianFloat(), Is.EqualTo(2f));
    }

    [Test]
    public void CalcSquaredDistance_KnownPoints_ReturnsSquaredDistance() {
        var p1 = new Point2D(0, 0);
        var p2 = new Point2D(3, 4);
        Assert.That(MathUtility.CalcSquaredDistance(p1, p2), Is.EqualTo(25.0).Within(1e-12));
    }

    [Test]
    public void CalcSquaredDistance_SamePoint_ReturnsZero() {
        var p = new Point2D(7, 11);
        Assert.That(MathUtility.CalcSquaredDistance(p, p), Is.EqualTo(0.0));
    }

    [Test]
    public void RejectionTest_NoOutliersInLinearFit_ReturnsNull() {
        var fitting = (Func<double, double>)(x => 2.0 * x + 1.0);
        var points = new System.Collections.Generic.List<ScatterErrorPoint> {
            new ScatterErrorPoint(0, 1.0,  0, 0),
            new ScatterErrorPoint(1, 3.0,  0, 0),
            new ScatterErrorPoint(2, 5.0,  0, 0),
            new ScatterErrorPoint(3, 7.0,  0, 0),
            new ScatterErrorPoint(4, 9.0,  0, 0),
            new ScatterErrorPoint(5, 11.0, 0, 0),
        };

        Assert.That(MathUtility.RejectionTest(points, fitting, confidence: 0.95), Is.Null);
    }

    [Test]
    public void RejectionTest_TooFewPoints_ReturnsNull() {
        var fitting = (Func<double, double>)(x => x);
        var points = new System.Collections.Generic.List<ScatterErrorPoint> {
            new ScatterErrorPoint(0, 0, 0, 0),
            new ScatterErrorPoint(1, 100, 0, 0),
            new ScatterErrorPoint(2, 2, 0, 0),
        };

        // <=3 points => no rejection per implementation
        Assert.That(MathUtility.RejectionTest(points, fitting, confidence: 0.95), Is.Null);
    }

    [Test]
    public void RejectionTest_ObviousOutlier_ReturnsThatPoint() {
        var fitting = (Func<double, double>)(x => x);
        var points = new System.Collections.Generic.List<ScatterErrorPoint> {
            new ScatterErrorPoint(0, 0,    0, 0),
            new ScatterErrorPoint(1, 1,    0, 0),
            new ScatterErrorPoint(2, 2,    0, 0),
            new ScatterErrorPoint(3, 3,    0, 0),
            new ScatterErrorPoint(4, 4,    0, 0),
            new ScatterErrorPoint(5, 100,  0, 0), // gross outlier
            new ScatterErrorPoint(6, 6,    0, 0),
            new ScatterErrorPoint(7, 7,    0, 0),
        };

        var rejected = MathUtility.RejectionTest(points, fitting, confidence: 0.95);
        Assert.That(rejected, Is.Not.Null);
        Assert.That(rejected.X, Is.EqualTo(5));
        Assert.That(rejected.Y, Is.EqualTo(100));
    }

    [Test]
    public void RejectionTest_NoisyClusterPlusOutlier_FlagsTheTrueOutlier() {
        // Inliers carry small, deterministic noise (so the MAD is non-zero and the robust path is
        // exercised) and sit on a model that is biased low by ~10 (a non-zero residual offset). A single
        // gross outlier sits far above the cluster. The robust median/MAD test must flag the gross
        // outlier itself, not one of the noisy inliers.
        var fitting = (Func<double, double>)(x => x); // model
        var rng = new System.Random(7);
        var points = new System.Collections.Generic.List<ScatterErrorPoint>();
        for (int x = 0; x < 14; ++x) {
            var noise = (rng.NextDouble() - 0.5) * 1.0; // ±0.5
            points.Add(new ScatterErrorPoint(x, x + 10.0 + noise, 0, 1.0)); // residual ≈ +10
        }
        points.Add(new ScatterErrorPoint(20, 20 + 60.0, 0, 1.0)); // gross outlier, residual ≈ +60

        var rejected = MathUtility.RejectionTest(points, fitting, confidence: 0.95);
        Assert.That(rejected, Is.Not.Null);
        Assert.That(rejected.X, Is.EqualTo(20));
    }

    [Test]
    public void RejectionTest_WeightedResiduals_RespectsPerPointUncertainty() {
        // One point sits far off the curve. Unweighted, it is rejected. But if it carries a large
        // measurement uncertainty (tiny weight), the weighted test must NOT reject it.
        var fitting = (Func<double, double>)(x => x);
        var rng = new System.Random(11);
        var points = new System.Collections.Generic.List<ScatterErrorPoint>();
        for (int x = 0; x < 12; ++x) {
            var noise = (rng.NextDouble() - 0.5) * 0.5;
            points.Add(new ScatterErrorPoint(x, x + noise, 0, 1.0));
        }
        const int farX = 6;
        points.Add(new ScatterErrorPoint(farX, farX + 25.0, 0, 1.0)); // far from curve

        // Unweighted: the far point is an obvious outlier.
        var unweighted = MathUtility.RejectionTest(points, fitting, confidence: 0.95);
        // Weighted so the far point has a tiny weight (huge uncertainty) and the rest weight 1.
        Func<double, double> weights = x => x == farX ? 0.01 : 1.0;
        var weighted = MathUtility.RejectionTest(points, fitting, confidence: 0.95, weights: weights);

        Assert.Multiple(() => {
            Assert.That(unweighted, Is.Not.Null);
            Assert.That(unweighted.X, Is.EqualTo(farX));
            Assert.That(weighted, Is.Null, "A far point with large uncertainty should not be rejected when weighted");
        });
    }
}
