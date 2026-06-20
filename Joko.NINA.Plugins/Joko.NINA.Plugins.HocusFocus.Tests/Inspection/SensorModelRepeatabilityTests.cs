#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection {

    /// <summary>
    /// The headline proof of the repeatability goal: running the entire registration + per-star
    /// hyperbolic + paraboloid fit pipeline twice, from scratch, on identical synthetic data must
    /// produce an identical model. Before the determinism work (seeded RANSAC sampling, stateless
    /// brightness search, wall-clock-free acceptance) this would diverge run-to-run. The synthetic
    /// data is generated fresh for each run because <see cref="SensorModel.RegisterStarsAndFit"/>
    /// mutates the input stars (brightness normalisation, alignment transforms), so sharing the list
    /// would not represent "running the unchanged process again".
    /// </summary>
    [TestFixture]
    public class SensorModelRepeatabilityTests {
        private const int ImageWidth = 1000;
        private const int ImageHeight = 1000;
        private const double CenterX = ImageWidth / 2.0;
        private const double CenterY = ImageHeight / 2.0;
        private const double FocuserSizeMicrons = 5.0;
        private const double PixelSize = 3.76;
        private const int StepSize = 2000;
        private const double FinalFocusPosition = 30000.0;

        // 9-position focuser sweep centred on FinalFocusPosition, comfortably bracketing every star's
        // best-focus position so each per-star hyperbola is well constrained.
        private static readonly int[] FocuserOffsets = { -8000, -6000, -4000, -2000, 0, 2000, 4000, 6000, 8000 };

        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        [Test]
        public void RegisterStarsAndFit_RunTwiceOnIdenticalData_ProducesIdenticalModel() {
            var first = RunPipeline();
            var second = RunPipeline();

            Assert.That(first, Is.Not.Null, "First run failed to produce a model");
            Assert.That(second, Is.Not.Null, "Second run failed to produce a model");
            Assert.That(first.StarsInModel, Is.GreaterThanOrEqualTo(9), "Too few stars entered the model to be a meaningful test");

            Assert.Multiple(() => {
                Assert.That(second.StarsInModel, Is.EqualTo(first.StarsInModel), "StarsInModel differs between runs");
                Assert.That(second.X0, Is.EqualTo(first.X0).Within(1e-9), "X0 differs between runs");
                Assert.That(second.Y0, Is.EqualTo(first.Y0).Within(1e-9), "Y0 differs between runs");
                Assert.That(second.Z0, Is.EqualTo(first.Z0).Within(1e-9), "Z0 differs between runs");
                Assert.That(second.Gx, Is.EqualTo(first.Gx).Within(1e-9), "Gx (tilt gradient) differs between runs");
                Assert.That(second.Gy, Is.EqualTo(first.Gy).Within(1e-9), "Gy (tilt gradient) differs between runs");
                Assert.That(second.K, Is.EqualTo(first.K).Within(1e-9), "K (curvature) differs between runs");
                Assert.That(second.GoodnessOfFit, Is.EqualTo(first.GoodnessOfFit).Within(1e-9), "GoodnessOfFit differs between runs");
                Assert.That(second.ReducedChiSquared, Is.EqualTo(first.ReducedChiSquared).Within(1e-9), "ReducedChiSquared differs between runs");
                Assert.That(second.RMSErrorMicrons, Is.EqualTo(first.RMSErrorMicrons).Within(1e-9), "RMSErrorMicrons differs between runs");
            });
        }

        private SensorParaboloidModel RunPipeline() {
            var sensorModel = new SensorModel(
                Substitute.For<IProfileService>(),
                BuildInspectorOptions(),
                new FakeAutoFocusOptions(),
                alglibAPI) {
                // Route report messages away from the UI-bound (dispatcher-backed) collection so the
                // core can run headless.
                RegistrationReportSink = _ => { }
            };

            var frames = BuildFrames();
            var (model, _) = sensorModel.RegisterStarsAndFit(
                frames,
                new Size(ImageWidth, ImageHeight),
                FocuserSizeMicrons,
                FinalFocusPosition,
                PixelSize,
                progress: new Progress<ApplicationStatus>(),
                stepSize: StepSize,
                ct: CancellationToken.None);
            return model;
        }

        private static FakeInspectorOptions BuildInspectorOptions() {
            return new FakeInspectorOptions {
                // Exercise the seeded-RANSAC alignment path (the original repeatability killer).
                UseRANSAC = true,
                // Keep the brightness search to a single deterministic pass; the determinism of the
                // search itself is covered separately. We are proving the fit pipeline is repeatable.
                RejectBadBrightnessMatches = false,
                RejectBadlyFittingMatches = false,
                StartingBrightnessDiff = -1,
                MaxStarsPerRegion = -1,
                FixedSensorCenter = true
            };
        }

        // Deterministically constructs a multi-frame synthetic AF run. Each call returns an independent
        // object graph so the second pipeline run starts from pristine, identical inputs.
        private static List<SensorDetectedStars> BuildFrames() {
            // 5x5 grid of stars with a fixed per-index jitter so triangles are non-degenerate (distinct
            // shapes) for RANSAC, while remaining identical across both runs.
            var starSpecs = new List<(double X, double Y, double BestFocus, double AvgBrightness)>();
            double[] grid = { 180, 330, 500, 670, 820 };
            int index = 0;
            foreach (var gx in grid) {
                foreach (var gy in grid) {
                    var jitterX = ((index * 37) % 11 - 5) * 1.3;
                    var jitterY = ((index * 53) % 11 - 5) * 1.3;
                    var x = gx + jitterX;
                    var y = gy + jitterY;
                    var dx = x - CenterX;
                    var dy = y - CenterY;
                    // True best-focus surface: tilt + mild curvature, in focuser steps. Gives each star a
                    // distinct hyperbola minimum so the paraboloid is non-degenerate.
                    var bestFocus = FinalFocusPosition + 0.9 * dx + 0.6 * dy + 0.0008 * (dx * dx + dy * dy);
                    var avgBrightness = 800.0 + 40.0 * index;
                    starSpecs.Add((x, y, bestFocus, avgBrightness));
                    ++index;
                }
            }

            const double minHfr = 1.5;
            const double slopePerStep = 1.5 / 3000.0;
            const double background = 80.0;

            var frames = new List<SensorDetectedStars>();
            foreach (var offset in FocuserOffsets) {
                var focuserPosition = FinalFocusPosition + offset;
                var starList = new List<DetectedStar>();
                foreach (var spec in starSpecs) {
                    var fromMin = focuserPosition - spec.BestFocus;
                    var hfr = Math.Sqrt(minHfr * minHfr + slopePerStep * slopePerStep * fromMin * fromMin);
                    starList.Add(new HocusFocusDetectedStar {
                        HFR = hfr,
                        NormalizedHFR = hfr,
                        Position = new Accord.Point((float)spec.X, (float)spec.Y),
                        AverageBrightness = spec.AvgBrightness,
                        MaxBrightness = spec.AvgBrightness * 2.0,
                        Background = background,
                        BoundingBox = new Rectangle((int)(spec.X - 5), (int)(spec.Y - 5), 10, 10)
                    });
                }

                var result = new HocusFocusStarDetectionResult {
                    StarList = starList,
                    ImageSize = new Size(ImageWidth, ImageHeight)
                };
                frames.Add(new SensorDetectedStars(focuserPosition, result, image: null));
            }
            return frames;
        }
    }
}
