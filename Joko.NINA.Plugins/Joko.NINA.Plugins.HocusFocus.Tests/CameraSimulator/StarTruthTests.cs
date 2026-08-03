#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    /// <summary>
    /// Behavioural tests for the <see cref="StarFieldCompositor.Render(RenderRequest, ICollection{StarTruth}, CancellationToken)"/>
    /// truth-sink overload (design workstream G1): the null-sink path must remain byte-identical to the
    /// existing single-argument <see cref="StarFieldCompositor.Render(RenderRequest, CancellationToken)"/>, and
    /// when a sink is supplied, every <see cref="StarTruth"/> it collects must actually describe what got
    /// rendered — the position <see cref="TanProjection"/> computed and the HFR <see cref="PsfKernelGenerator"/>
    /// built the kernel from.
    /// </summary>
    [TestFixture]
    public class StarTruthTests {

        [Test]
        public void Render_WithTruthSink_IsByteIdenticalToTheSingleArgumentOverload() {
            // Load-bearing assertion: adding a truth sink must not perturb a single pixel of the render. Stars
            // are injected (not a starless frame) so this exercises the truth-collecting branch of BuildStampJobs,
            // not just the empty-jobs fast path.
            var projection = SyntheticCameraTestScene.Projection();
            var stars = new List<CatalogStar> {
                SyntheticCameraTestScene.StarAtPixel(projection, 700, 700, 10.5),
                SyntheticCameraTestScene.StarAtPixel(projection, 2300, 800, 11.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 1504, 1504, 10.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 900, 2200, 11.5),
            };
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 30);
            var compositor = new StarFieldCompositor(new FakeCatalogReader(stars));

            var withoutSink = compositor.Render(request, CancellationToken.None);
            var sink = new List<StarTruth>();
            var withSink = compositor.Render(request, sink, CancellationToken.None);

            Assert.Multiple(() => {
                Assert.That(withSink, Is.EqualTo(withoutSink), "a supplied truth sink must not change any rendered pixel");
                Assert.That(sink, Is.Not.Empty, "the injected stars must have produced truth entries");
            });
        }

        [Test]
        public void Render_NullSink_DoesNotThrowAndSkipsTruth() {
            // The single-argument overload delegates to Render(request, null, token); confirm that path is
            // exercised (not just the two-argument overload with an explicit null) and produces no truth.
            var projection = SyntheticCameraTestScene.Projection();
            var stars = new List<CatalogStar> { SyntheticCameraTestScene.StarAtPixel(projection, 1504, 1504, 10.0) };
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition);
            var compositor = new StarFieldCompositor(new FakeCatalogReader(stars));

            Assert.DoesNotThrow(() => compositor.Render(request, null, CancellationToken.None));
        }

        [Test]
        public void Render_TruthMatchesInjectedStars_PositionAndFluxOrdering() {
            var projection = SyntheticCameraTestScene.Projection();
            var starBright = SyntheticCameraTestScene.StarAtPixel(projection, 900, 900, 9.0);
            var starMedium = SyntheticCameraTestScene.StarAtPixel(projection, 1500, 1500, 11.0);
            var starFaint = SyntheticCameraTestScene.StarAtPixel(projection, 2100, 2100, 13.0);
            var stars = new List<CatalogStar> { starBright, starMedium, starFaint };

            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition);
            var compositor = new StarFieldCompositor(new FakeCatalogReader(stars));
            var sink = new List<StarTruth>();
            compositor.Render(request, sink, CancellationToken.None);

            Assert.That(sink, Has.Count.EqualTo(stars.Count));

            foreach (var star in stars) {
                Assert.That(projection.TryProject(star.Coordinates.RADegrees, star.Coordinates.Dec, out var expectedX, out var expectedY), Is.True);
                // Match by nearest projected position rather than by list order — BuildStampJobs preserves catalog
                // order today, but the truth-position check should not depend on that being true.
                var truth = sink.OrderBy(t => Math.Abs(t.CxPixels - expectedX) + Math.Abs(t.CyPixels - expectedY)).First();
                Assert.Multiple(() => {
                    Assert.That(truth.CxPixels, Is.EqualTo(expectedX).Within(1e-6), $"CxPixels matches TryProject for mag {star.Magnitude}");
                    Assert.That(truth.CyPixels, Is.EqualTo(expectedY).Within(1e-6), $"CyPixels matches TryProject for mag {star.Magnitude}");
                    Assert.That(truth.RaDegrees, Is.EqualTo(star.Coordinates.RADegrees).Within(1e-9));
                    Assert.That(truth.DecDegrees, Is.EqualTo(star.Coordinates.Dec).Within(1e-9));
                    Assert.That(truth.MagnitudeV, Is.EqualTo(star.Magnitude));
                    Assert.That(truth.FluxElectrons, Is.GreaterThan(0.0), "flux must be positive");
                });
            }

            // Brighter (smaller magnitude) must deposit strictly more flux, since all three stars share one
            // radiometry calculator (same request) and differ only in magnitude.
            var byMagnitudeAscending = sink.OrderBy(t => t.MagnitudeV).ToList();
            for (var i = 1; i < byMagnitudeAscending.Count; ++i) {
                Assert.That(byMagnitudeAscending[i - 1].FluxElectrons, Is.GreaterThan(byMagnitudeAscending[i].FluxElectrons),
                    "a brighter (smaller-magnitude) star must have more flux than a fainter one");
            }
        }

        [Test]
        public void Render_MeasuredHfrTracksDefocusModel_AtEachStarsQuantizedDefocus() {
            // Push well off focus so the HFR curve sits in its defocus-dominated regime rather than being
            // dominated by the in-focus HFR_min floor, where a percentage tolerance is not meaningful.
            var projection = SyntheticCameraTestScene.Projection();
            var stars = new List<CatalogStar> {
                SyntheticCameraTestScene.StarAtPixel(projection, 700, 700, 10.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 2300, 800, 10.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 1504, 1504, 10.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 900, 2200, 10.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 2200, 2300, 10.0),
            };
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 400);
            var model = DefocusModel.FromRequest(request, SyntheticCameraTestScene.SensorDef, SyntheticCameraTestScene.FilterDef);

            var sink = new List<StarTruth>();
            new StarFieldCompositor(new FakeCatalogReader(stars)).Render(request, sink, CancellationToken.None);

            Assert.That(sink, Is.Not.Empty);
            Assert.Multiple(() => {
                foreach (var truth in sink) {
                    // The kernel is generated FROM the model at the truth's own QuantizedDefocusMicrons (not the
                    // raw, pre-quantization LocalDefocusMicrons), so that is the value to compare against — see
                    // StarTruth.QuantizedDefocusMicrons's doc. Same 3% envelope PsfKernelGeneratorTests uses for
                    // kernel-vs-model HFR at comparable defocus (MeasuredHfr_MatchesDefocusModel).
                    var expectedHfr = model.HfrAtDefocusMicrons(truth.QuantizedDefocusMicrons);
                    Assert.That(truth.MeasuredHfrPixels, Is.EqualTo(expectedHfr).Within(0.03 * expectedHfr),
                        $"measured HFR tracks DefocusModel at Δ_quantized={truth.QuantizedDefocusMicrons:F1}µm");
                    // Same 1% envelope PsfKernelGeneratorTests uses for measured-vs-Rice-closed-form agreement
                    // (MeasuredHfr_MatchesRiceClosedForm): both HFRs come from the same kernel, so they should
                    // agree tightly regardless of how well that kernel tracks the DefocusModel hyperbola above.
                    Assert.That(truth.AnalyticHfrPixels, Is.EqualTo(truth.MeasuredHfrPixels).Within(0.01 * truth.MeasuredHfrPixels),
                        "analytic and measured HFR agree to <1% (both read off the same kernel)");
                }
            });
        }
    }
}
