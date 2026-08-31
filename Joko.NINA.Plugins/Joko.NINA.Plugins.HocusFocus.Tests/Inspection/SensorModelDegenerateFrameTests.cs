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
using System.Linq;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection {

    /// <summary>
    /// Frames that detect no stars (or too few to normalise brightness against) are a NORMAL outcome of an
    /// AutoFocus sweep — the extreme-defocus endpoints and any frame lost to cloud — and the AutoFocus curve
    /// fit already tolerates them by discarding the point. The sensor model must tolerate them too.
    ///
    /// <para>The regression this guards: <c>RegisterStarsAndFit</c> normalised each frame's brightness with an
    /// unguarded <c>StarList.Max()</c>/<c>Min()</c>. A single starless frame threw
    /// "Sequence contains no elements" and killed the whole aberration inspection AFTER a successful AF run,
    /// and a single-star frame divided by a zero brightness range, producing a NaN
    /// <see cref="HocusFocusDetectedStar.NormalisedBrightness"/> that silently made those stars unmatchable in
    /// the KdTree brightness filter.</para>
    /// </summary>
    [TestFixture]
    public class SensorModelDegenerateFrameTests {
        private const int ImageWidth = 1000;
        private const int ImageHeight = 1000;
        private const double CenterX = ImageWidth / 2.0;
        private const double CenterY = ImageHeight / 2.0;
        private const double FocuserSizeMicrons = 5.0;
        private const double PixelSize = 3.76;
        private const int StepSize = 2000;
        private const double FinalFocusPosition = 30000.0;

        private static readonly int[] FocuserOffsets = { -8000, -6000, -4000, -2000, 0, 2000, 4000, 6000, 8000 };

        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        [Test]
        public void RegisterStarsAndFit_OneFrameDetectedNoStars_SkipsItAndStillFits() {
            var frames = BuildFrames();
            // The far-defocus endpoint of the sweep comes back empty, exactly as the detector reports it.
            ReplaceStarList(frames[0], new List<DetectedStar>());

            var reports = new List<string>();
            var (model, _) = Run(frames, reports);

            Assert.That(model, Is.Not.Null, "A starless frame must not prevent the remaining frames from fitting");
            Assert.That(model.StarsInModel, Is.GreaterThanOrEqualTo(9));
            Assert.That(reports, Has.Some.Contains("1 frame(s) had no detected stars"),
                "The skipped frame should be surfaced in the registration report");
            // A starless frame forms no triangles, so RANSAC must record it as unaligned rather than
            // registering it on a transform estimated from nothing.
            Assert.That(frames[0].HasBeenAligned, Is.False);
        }

        [Test]
        public void RegisterStarsAndFit_MultipleFramesDetectedNoStars_SkipsThemAndStillFits() {
            var frames = BuildFrames();
            ReplaceStarList(frames[0], new List<DetectedStar>());
            ReplaceStarList(frames[frames.Count - 1], new List<DetectedStar>());

            var reports = new List<string>();
            var (model, _) = Run(frames, reports);

            Assert.That(model, Is.Not.Null);
            Assert.That(reports, Has.Some.Contains("2 frame(s) had no detected stars"));
        }

        [Test]
        public void RegisterStarsAndFit_FrameWithSingleStar_AssignsFiniteNormalisedBrightness() {
            var frames = BuildFrames();
            var singleStarFrame = frames[0];
            ReplaceStarList(singleStarFrame, singleStarFrame.StarDetectionResult.StarList.Take(1).ToList());

            var (model, _) = Run(frames, new List<string>());

            Assert.That(model, Is.Not.Null);
            var lone = (HocusFocusDetectedStar)singleStarFrame.StarDetectionResult.StarList.Single();
            Assert.That(float.IsNaN(lone.NormalisedBrightness), Is.False,
                "A single-star frame has no brightness range; the normalised value must not be NaN");
            Assert.That(lone.NormalisedBrightness, Is.EqualTo(0.5f));
        }

        [Test]
        public void RegisterStarsAndFit_FrameWithUniformBrightness_AssignsFiniteNormalisedBrightness() {
            var frames = BuildFrames();
            var uniformFrame = frames[0];
            foreach (var star in uniformFrame.StarDetectionResult.StarList.Cast<HocusFocusDetectedStar>()) {
                star.AverageBrightness = 1000.0;
            }

            var (model, _) = Run(frames, new List<string>());

            Assert.That(model, Is.Not.Null);
            Assert.That(uniformFrame.StarDetectionResult.StarList.Cast<HocusFocusDetectedStar>(),
                Has.All.Matches<HocusFocusDetectedStar>(s => !float.IsNaN(s.NormalisedBrightness)),
                "A frame whose stars are all equally bright has no brightness range to normalise against");
        }

        [Test]
        public void RegisterStarsAndFit_NormalisedBrightnessUnchangedForHealthyFrames() {
            // The degenerate-frame handling must not perturb the normal path: a frame with a brightness range
            // still maps its faintest star to 0 and its brightest to 1.
            var frames = BuildFrames();
            Run(frames, new List<string>());

            var healthy = frames[4].StarDetectionResult.StarList.Cast<HocusFocusDetectedStar>().ToList();
            Assert.Multiple(() => {
                Assert.That(healthy.Min(s => s.NormalisedBrightness), Is.EqualTo(0.0f).Within(1e-6f));
                Assert.That(healthy.Max(s => s.NormalisedBrightness), Is.EqualTo(1.0f).Within(1e-6f));
            });
        }

        [Test]
        public void RegisterStarsAndFit_EveryFrameDetectedNoStars_ThrowsDescriptiveError() {
            var frames = BuildFrames();
            foreach (var frame in frames) {
                ReplaceStarList(frame, new List<DetectedStar>());
            }

            // Without the guard the reference-frame index stays -1 and the run dies on an index-out-of-range
            // deep inside alignment/matching, which says nothing about what actually went wrong.
            var ex = Assert.Catch(() => Run(frames, new List<string>()));
            Assert.That(ex.Message, Does.Contain("had any detected stars"));
        }

        private (SensorParaboloidModel, RegistrationAndFitResult) Run(List<SensorDetectedStars> frames, List<string> reports) {
            var sensorModel = new SensorModel(
                Substitute.For<IProfileService>(),
                BuildInspectorOptions(),
                new FakeAutoFocusOptions(),
                alglibAPI) {
                RegistrationReportSink = reports.Add
            };

            return sensorModel.RegisterStarsAndFit(
                frames,
                new Size(ImageWidth, ImageHeight),
                FocuserSizeMicrons,
                FinalFocusPosition,
                PixelSize,
                progress: new Progress<ApplicationStatus>(),
                stepSize: StepSize,
                ct: CancellationToken.None);
        }

        private static void ReplaceStarList(SensorDetectedStars frame, List<DetectedStar> starList) {
            frame.StarDetectionResult.StarList = starList;
        }

        private static FakeInspectorOptions BuildInspectorOptions() {
            return new FakeInspectorOptions {
                // Exercise the alignment path, which is where a starless frame would otherwise fail to form
                // triangles — it must degrade to "frame did not align" rather than throwing.
                UseRANSAC = true,
                RejectBadBrightnessMatches = false,
                RejectBadlyFittingMatches = false,
                StartingBrightnessDiff = -1,
                MaxStarsPerRegion = -1,
                FixedSensorCenter = true
            };
        }

        // Mirrors SensorModelRepeatabilityTests' synthetic run: a 5x5 grid of stars swept through focus across
        // 9 focuser positions, with a tilted + mildly curved best-focus surface.
        private static List<SensorDetectedStars> BuildFrames() {
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
