using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// F3 decision evidence: compares τ policies for MeasureStar on synthetic shapes with known HFR ground
    /// truth under seeded noise. For each (policy, multiplier) cell it reports HFR bias (mean − truth) and
    /// noise (std across seeds). subtract@0.4σ is the status-quo effective behavior after the F4 recalibration;
    /// the gate-only candidates trade the radial-gradient bias against one-sided noise admission at large radii.
    /// Assertions are deliberately loose — the printed table is the product (captured into
    /// plans/sigma-consistency-f3-results.md for the decision gate).
    /// </summary>
    [TestFixture]
    public class TauPolicyExperimentTests {
        private const int Size = 81;
        private const double Cx = 40.0, Cy = 40.0;
        private const double NoiseSigmaTrue = 0.02; // honest σ of the measurement image, passed to MeasureStar
        private const int SeedBase = 9000;
        private const int Seeds = 10;

        private static readonly (string label, TauClipPolicy policy, double multiplier)[] Cells = {
            ("subtract@0.4σ", TauClipPolicy.SubtractTau, 0.4),
            ("gate@0.4σ", TauClipPolicy.GateOnly, 0.4),
            ("gate@1.0σ", TauClipPolicy.GateOnly, 1.0),
            ("gate@2.0σ", TauClipPolicy.GateOnly, 2.0),
        };

        private static Star NewStar() => new Star {
            Center = new Point2d(Cx, Cy),
            StarBoundingBox = new Rect(0, 0, Size, Size),
            Background = 0.0
        };

        private static (double bias, double std, int hitCount) MeasureCell(
                Func<Mat> imageFactory, double truth, TauClipPolicy policy, double multiplier) {
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams {
                AnalysisSamplingSize = 1.0f,
                StarClippingMultiplier = multiplier,
                HfrTauPolicy = policy
            };
            var hfrs = new List<double>();
            for (int s = 0; s < Seeds; ++s) {
                using var image = imageFactory();
                SyntheticDefocusedStarImage.AddGaussianNoise(image, NoiseSigmaTrue, SeedBase + s);
                var star = NewStar();
                if (detector.MeasureStar(image, star, p, NoiseSigmaTrue)) {
                    hfrs.Add(star.HFR);
                }
            }
            if (hfrs.Count < 2) {
                // Not enough measurements to compute stats — report NaN so the table row still prints.
                return (double.NaN, double.NaN, hfrs.Count);
            }
            double mean = hfrs.Average();
            double std = Math.Sqrt(hfrs.Sum(h => (h - mean) * (h - mean)) / (hfrs.Count - 1));
            return (mean - truth, std, hfrs.Count);
        }

        private static void RunMatrix(string shapeLabel, Func<Mat> imageFactory, double truth) {
            TestContext.WriteLine($"--- {shapeLabel} (truth HFR={truth:F3}, noise σ={NoiseSigmaTrue}) ---");
            TestContext.WriteLine($"{"cell",-16} {"bias",10} {"std",10} {"n",4}");
            foreach (var (label, policy, multiplier) in Cells) {
                var (bias, std, n) = MeasureCell(imageFactory, truth, policy, multiplier);
                if (double.IsNaN(bias)) {
                    TestContext.WriteLine($"{label,-16} {"n/a",10} {"n/a",10} {n,4}");
                } else {
                    TestContext.WriteLine($"{label,-16} {bias,10:F4} {std,10:F4} {n,4}");
                }
                Assert.Multiple(() => {
                    // Deliberately loose bounds — values outside them are still informative decision evidence.
                    // subtract@0.4σ on faint shapes can show biases of 5-12 (that is the F3 signal).
                    // gate@X.Xσ can drop measurable seeds when the gate clips the whole star (n/a rows).
                    if (!double.IsNaN(bias))
                        Assert.That(bias, Is.InRange(-20.0, 20.0), $"{shapeLabel}/{label}: bias should be bounded");
                    if (!double.IsNaN(std))
                        Assert.That(std, Is.LessThan(5.0), $"{shapeLabel}/{label}: seed noise should be bounded");
                });
            }
        }

        [Test]
        public void TauPolicyMatrix_BrightGaussian() {
            const double sigma = 5.0, peak = 1.0;
            RunMatrix("bright gaussian (peak 50σn)",
                () => SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, 0.0),
                HfrGroundTruth.Gaussian(sigma));
        }

        [Test]
        public void TauPolicyMatrix_FaintGaussian() {
            const double sigma = 5.0, peak = 0.16; // 8× noise σ — the faint-star regime AF cares about
            RunMatrix("faint gaussian (peak 8σn)",
                () => SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, 0.0),
                HfrGroundTruth.Gaussian(sigma));
        }

        [Test]
        public void TauPolicyMatrix_FaintDonut() {
            const double inner = 8.0, outer = 14.0, peak = 0.12; // 6× noise σ defocused annulus
            RunMatrix("faint donut (peak 6σn)",
                () => SyntheticDefocusedStarImage.CreateAnnulus(Size, Size, Cx, Cy, inner, outer, peak, 0.0),
                HfrGroundTruth.Annulus(inner, outer));
        }
    }
}
