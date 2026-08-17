using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    /// <summary>
    /// Behavioural unit tests for the <see cref="StarFieldCompositor"/>: correct frame geometry,
    /// deterministic (race-free) parallel stamping, flux deposited at the projected star position, and the
    /// starless/dark fallbacks that must never fail an exposure.
    /// </summary>
    [TestFixture]
    public class StarFieldCompositorTests {
        private static readonly int Width = SyntheticCameraTestScene.SensorDef.Width;
        private static readonly int Height = SyntheticCameraTestScene.SensorDef.Height;

        [Test]
        public void Render_ReturnsRowMajorFrameOfSensorSize() {
            var reader = new FakeCatalogReader(Array.Empty<CatalogStar>());
            var compositor = new StarFieldCompositor(reader);
            var pixels = compositor.Render(SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition), CancellationToken.None);
            Assert.That(pixels, Is.Not.Null);
            Assert.That(pixels.Length, Is.EqualTo(Width * Height));
        }

        [Test]
        public void Render_MissingDatabase_RendersStarlessFrameNeverThrows() {
            // A reader whose Query throws (missing folder / no DB) must NOT fail the exposure: the compositor logs
            // and renders a starless frame. That frame must be byte-identical to a reader that simply returns no
            // stars (both take the empty-jobs path → same background + same seeded noise).
            var throwing = new FakeCatalogReader(() => throw new DirectoryNotFoundException("no astap here"));
            var empty = new FakeCatalogReader(Array.Empty<CatalogStar>());

            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition);
            var starlessFromError = new StarFieldCompositor(throwing).Render(request, CancellationToken.None);
            var starlessFromEmpty = new StarFieldCompositor(empty).Render(request, CancellationToken.None);

            Assert.That(starlessFromError.Length, Is.EqualTo(Width * Height));
            Assert.That(starlessFromError, Is.EqualTo(starlessFromEmpty).AsCollection);
        }

        [Test]
        public void Render_ResolvesCatalogReaderFromRequestPath_NotFromConstructionTime() {
            // Regression: the reader was latched at construction, so the request's AstapCatalogPath snapshot was
            // ignored — a changed catalog path did nothing until the camera was reconnected, and the
            // "no ASTAP database found at '<path>'" warning could name a path that was not the one being read.
            // The factory must be handed each request's own path.
            var pathsSeen = new List<string>();
            var compositor = new StarFieldCompositor(path => {
                pathsSeen.Add(path);
                // No database at either path: the missing-DB path must still render starless, never throw.
                return new FakeCatalogReader(() => throw new DirectoryNotFoundException($"no astap at '{path}'"));
            });

            var first = compositor.Render(
                SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition, astapCatalogPath: @"D:\astap-old"),
                CancellationToken.None);
            var second = compositor.Render(
                SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition, astapCatalogPath: @"D:\astap-new"),
                CancellationToken.None);

            Assert.Multiple(() => {
                Assert.That(pathsSeen, Is.EqualTo(new[] { @"D:\astap-old", @"D:\astap-new" }).AsCollection,
                    "each render resolves its reader from that request's catalog-path snapshot");
                Assert.That(first.Length, Is.EqualTo(Width * Height), "a missing database still yields a starless frame");
                Assert.That(second.Length, Is.EqualTo(Width * Height), "a missing database still yields a starless frame");
            });
        }

        [Test]
        public void Render_IsDeterministicForFixedSeed() {
            // Determinism proves the parallel row-stripe stamp is race-free: any data race on the shared
            // accumulator would perturb pixels between runs.
            var projection = SyntheticCameraTestScene.Projection();
            var stars = new List<CatalogStar> {
                SyntheticCameraTestScene.StarAtPixel(projection, 700, 700, 10.5),
                SyntheticCameraTestScene.StarAtPixel(projection, 2300, 800, 11.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 1504, 1504, 10.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 900, 2200, 11.5),
                SyntheticCameraTestScene.StarAtPixel(projection, 2200, 2300, 10.8),
            };
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 30);

            var a = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(request, CancellationToken.None);
            var b = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(request, CancellationToken.None);
            Assert.That(a, Is.EqualTo(b).AsCollection);
        }

        [Test]
        public void Render_IsPure_SameRequestRendersIdenticallyOnAnyThread() {
            // The camera prefetches the render at StartExposure rather than at DownloadExposure. That is only
            // safe because Render is a pure function of its request: same request => same pixels, no matter when,
            // or on which thread, it runs. Stars are injected so this exercises the parallel stamp, not just the
            // starless background.
            var projection = SyntheticCameraTestScene.Projection();
            var stars = new List<CatalogStar> {
                SyntheticCameraTestScene.StarAtPixel(projection, 700, 700, 10.5),
                SyntheticCameraTestScene.StarAtPixel(projection, 1504, 1504, 10.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 2200, 2300, 10.8),
            };
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 30);
            var compositor = new StarFieldCompositor(new FakeCatalogReader(stars));

            var inline = compositor.Render(request, CancellationToken.None);
            var offThread = Task.Run(() => compositor.Render(request, CancellationToken.None)).GetAwaiter().GetResult();

            Assert.That(offThread, Is.EqualTo(inline).AsCollection);
        }

        [Test]
        public void Render_TimingsOverload_IsByteIdenticalAndReportsThePhases() {
            // The internal timings overload carries the same contract the truthSink overload does: passing it
            // must not change a single pixel. Every instrumentation call site is guarded on a non-null sink, so
            // a production render never even reads the clock — but "guarded" is a claim, and this is the test
            // that keeps it true. Stars are injected so the kernel-cache counters have something to count.
            var projection = SyntheticCameraTestScene.Projection();
            var stars = new List<CatalogStar> {
                SyntheticCameraTestScene.StarAtPixel(projection, 700, 700, 10.5),
                SyntheticCameraTestScene.StarAtPixel(projection, 1504, 1504, 10.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 2200, 2300, 10.8),
            };
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 30);
            var compositor = new StarFieldCompositor(new FakeCatalogReader(stars));

            var withoutTimings = compositor.Render(request, CancellationToken.None);
            var timings = new RenderPhaseTimings();
            var withTimings = compositor.Render(request, null, timings, CancellationToken.None);

            Assert.That(withTimings, Is.EqualTo(withoutTimings).AsCollection, "instrumentation must not change any pixel");
            Assert.Multiple(() => {
                Assert.That(timings.StarsQueried, Is.EqualTo(stars.Count));
                Assert.That(timings.StampJobs, Is.EqualTo(stars.Count));
                // Aberrations are off in the test scene, so every star shares one quantized defocus level and
                // therefore one kernel. That "1" is the reference point the astigmatic cache key is measured
                // against.
                Assert.That(timings.DistinctKernels, Is.EqualTo(1));
                Assert.That(timings.KernelCacheBytes, Is.GreaterThan(0));
                Assert.That(timings.MaxKernelRadius, Is.GreaterThan(0));
                Assert.That(timings.DevelopMs, Is.GreaterThan(0.0));
                Assert.That(timings.KernelGenerateMs, Is.GreaterThan(0.0));
                Assert.That(timings.KernelGenerateMs, Is.LessThanOrEqualTo(timings.StampJobBuildMs),
                    "kernel generation is a subset of the job build");
            });
        }

        [Test]
        public void Render_DepositsFluxAtProjectedStarPosition() {
            var projection = SyntheticCameraTestScene.Projection();
            const double px = 1900, py = 1100;
            var star = SyntheticCameraTestScene.StarAtPixel(projection, px, py, 9.5); // bright, unsaturated
            Assert.That(projection.TryProject(star.Coordinates.RADegrees, star.Coordinates.Dec, out var sx, out var sy), Is.True);

            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 30);
            var pixels = new StarFieldCompositor(new FakeCatalogReader(new[] { star })).Render(request, CancellationToken.None);

            // A window around the projected star should hold far more signal than an equal window of empty sky.
            // Compare background-SUBTRACTED signal (the bias pedestal floods both windows equally). A mag 9.5 star
            // deposits ~80k ADU of flux, virtually all inside a 25×25 window at this focus.
            var starSum = WindowSum(pixels, Width, (int)Math.Round(sx), (int)Math.Round(sy), 12);
            var skySum = WindowSum(pixels, Width, 200, 200, 12);
            Assert.That(starSum - skySum, Is.GreaterThan(40000), $"star window {starSum} vs empty window {skySum}: star flux should dominate");

            // The brightest pixel in the star window should sit within a couple px of the projected centre.
            var (bx, by) = WindowArgMax(pixels, Width, (int)Math.Round(sx), (int)Math.Round(sy), 12);
            Assert.That(Math.Abs(bx - sx), Is.LessThan(3.0));
            Assert.That(Math.Abs(by - sy), Is.LessThan(3.0));
        }

        [Test]
        public void Render_FocalLengthZero_RendersDarkFrameNeverThrows() {
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition) with {
                FocalLengthMillimeters = 0.0
            };
            var pixels = new StarFieldCompositor(new FakeCatalogReader(Array.Empty<CatalogStar>())).Render(request, CancellationToken.None);
            Assert.That(pixels.Length, Is.EqualTo(Width * Height));
            // Bias pedestal present, no runaway values (bias + dark + read noise only, 14-bit sensor).
            Assert.That(pixels[0], Is.GreaterThan(0));
        }

        [Test]
        public void Render_CancellationRequested_Throws() {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var compositor = new StarFieldCompositor(new FakeCatalogReader(Array.Empty<CatalogStar>()));
            Assert.Throws<OperationCanceledException>(
                () => compositor.Render(SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition), cts.Token));
        }

        // -----------------------------------------------------------------------------------------------
        // Astigmatism: the elliptical kernel path through the compositor.
        // -----------------------------------------------------------------------------------------------

        private static List<CatalogStar> AstigmatismScene() {
            var projection = SyntheticCameraTestScene.Projection();
            return new List<CatalogStar> {
                SyntheticCameraTestScene.StarAtPixel(projection, 300, 1504, 10.2),
                SyntheticCameraTestScene.StarAtPixel(projection, 2700, 1504, 10.2),
                SyntheticCameraTestScene.StarAtPixel(projection, 1504, 300, 10.4),
                SyntheticCameraTestScene.StarAtPixel(projection, 1504, 2700, 10.4),
                SyntheticCameraTestScene.StarAtPixel(projection, 1504, 1504, 10.0),
                SyntheticCameraTestScene.StarAtPixel(projection, 700, 700, 10.6),
                SyntheticCameraTestScene.StarAtPixel(projection, 2300, 2300, 10.6),
            };
        }

        [Test]
        public void Render_AstigmatismDisabled_IsByteIdenticalToBefore() {
            // The toggle ships enabled, so this is the guard that it cannot move a frame it was not asked to.
            // Two ways to get a zero split -- the toggle off, and a perfectly corrected optic at design spacing
            // -- and both must collapse every star onto the quantized level it always had, so the compositor
            // takes the circular generator and the frames agree to the byte rather than merely closely.
            var stars = AstigmatismScene();
            var steps = SyntheticCameraTestScene.OptimalFocuserPosition + 120;
            var withoutFeature = SyntheticCameraTestScene.Request(steps,
                aberrationsEnabled: true, tiltAngleDegrees: 30.0, tiltAmountMicrons: 90.0, backfocusErrorMicrons: 40.0);
            var featureOffWithResidual = withoutFeature with { CornerAstigmatismMicrons = 15.0 };

            var perfectOpticOff = SyntheticCameraTestScene.Request(steps,
                aberrationsEnabled: true, tiltAngleDegrees: 30.0, tiltAmountMicrons: 90.0, backfocusErrorMicrons: 0.0);
            var perfectOpticOn = perfectOpticOff with { AstigmatismEnabled = true, CornerAstigmatismMicrons = 0.0 };

            var a = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(withoutFeature, CancellationToken.None);
            var b = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(featureOffWithResidual, CancellationToken.None);
            var c = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(perfectOpticOff, CancellationToken.None);
            var d = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(perfectOpticOn, CancellationToken.None);
            Assert.Multiple(() => {
                Assert.That(b, Is.EqualTo(a).AsCollection, "the toggle off ignores the residual entirely");
                Assert.That(d, Is.EqualTo(c).AsCollection, "a perfect optic at design spacing has nothing to split");
            });
        }

        [Test]
        public void Render_AstigmatismChangesPixels_WhenTheOpticHasAResidual() {
            // The complement of the byte-identity guard: with a real residual the frame must actually differ,
            // or the feature is wired up but inert.
            var stars = AstigmatismScene();
            var steps = SyntheticCameraTestScene.OptimalFocuserPosition + 120;
            var off = SyntheticCameraTestScene.Request(steps,
                aberrationsEnabled: true, tiltAngleDegrees: 30.0, tiltAmountMicrons: 90.0, backfocusErrorMicrons: 40.0);
            var on = off with { AstigmatismEnabled = true, CornerAstigmatismMicrons = 15.0 };

            var a = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(off, CancellationToken.None);
            var b = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(on, CancellationToken.None);
            Assert.That(b, Is.Not.EqualTo(a).AsCollection);
        }

        [Test]
        public void Render_WithAstigmatism_IsDeterministicAndPure() {
            // The kernels are now built on a Parallel.For over the distinct cache keys. Determinism is what
            // proves each one lands in its own slot and is a pure function of its key -- and purity is the
            // contract the camera relies on when it prefetches the render at StartExposure.
            var stars = AstigmatismScene();
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 120,
                aberrationsEnabled: true, tiltAngleDegrees: 30.0, tiltAmountMicrons: 90.0, backfocusErrorMicrons: 40.0,
                astigmatismEnabled: true, cornerAstigmatismMicrons: 15.0);
            var compositor = new StarFieldCompositor(new FakeCatalogReader(stars));

            var inline = compositor.Render(request, CancellationToken.None);
            var again = compositor.Render(request, CancellationToken.None);
            var offThread = Task.Run(() => compositor.Render(request, CancellationToken.None)).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(again, Is.EqualTo(inline).AsCollection, "same request, same pixels");
                Assert.That(offThread, Is.EqualTo(inline).AsCollection, "and on any thread");
            });
        }

        [Test]
        public void Render_KernelCacheCardinality_StaysBounded() {
            // Pure counting, no clock: the wall-clock benchmark lives in TestApp, but cardinality and bytes are
            // what actually regress if someone halves a quantum, and they are deterministic enough to gate on.
            var stars = AstigmatismScene();
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 350,
                aberrationsEnabled: true, tiltAngleDegrees: 30.0, tiltAmountMicrons: 200.0, backfocusErrorMicrons: 120.0,
                astigmatismEnabled: true, cornerAstigmatismMicrons: 40.0);
            var timings = new RenderPhaseTimings();
            new StarFieldCompositor(new FakeCatalogReader(stars)).Render(request, null, timings, CancellationToken.None);

            Assert.Multiple(() => {
                Assert.That(timings.DistinctKernels, Is.LessThanOrEqualTo(stars.Count),
                    "no more kernels than stars -- each star needs at most one");
                Assert.That(timings.DistinctKernels, Is.GreaterThan(1), "an aggressive field really is multi-kernel");
                Assert.That(timings.KernelCacheBytes, Is.LessThan(384L * 1024 * 1024), "inside the cache budget");
            });
        }

        [Test]
        public void Render_TruthSink_CarriesTheAstigmaticShape() {
            var stars = AstigmatismScene();
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 120,
                aberrationsEnabled: true, tiltAngleDegrees: 0.0, tiltAmountMicrons: 120.0, backfocusErrorMicrons: 40.0,
                astigmatismEnabled: true, cornerAstigmatismMicrons: 15.0);
            var truth = new List<StarTruth>();
            new StarFieldCompositor(new FakeCatalogReader(stars)).Render(request, truth, CancellationToken.None);

            Assert.That(truth, Is.Not.Empty);
            var elongated = 0;
            foreach (var t in truth) {
                Assert.That(t.OuterRadiusPixels,
                    Is.EqualTo(Math.Max(t.OuterRadiusRadialPixels, t.OuterRadiusTangentialPixels)).Within(1e-12),
                    "OuterRadiusPixels is the enclosing extent, which is what golden box sizing depends on");
                Assert.That(t.QuantizedDefocusMicrons,
                    Is.EqualTo(0.5 * (t.QuantizedTangentialDefocusMicrons + t.QuantizedSagittalDefocusMicrons)).Within(1e-9),
                    "the reported defocus stays the mean of the pair");
                if (t.OuterRadiusRadialPixels != t.OuterRadiusTangentialPixels) {
                    ++elongated;
                    Assert.That(t.PredictedEccentricity, Is.GreaterThan(0.0));
                    Assert.That(t.OrientationBin, Is.GreaterThanOrEqualTo(0));
                }
            }
            Assert.That(elongated, Is.GreaterThan(0), "an off-axis star in this field must render elliptical");
        }

        [Test]
        public void Render_WingSpillStarAtAnAstigmaticCorner_IsStillStamped() {
            // The projection margin is sized from |Δ| + |A|, since the larger semi-axis is what actually spills
            // onto the sensor. Sizing it from |Δ| alone would silently drop this star.
            var projection = SyntheticCameraTestScene.Projection();
            var offFrame = new List<CatalogStar> {
                SyntheticCameraTestScene.StarAtPixel(projection, -14, -14, 8.5)
            };
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + 350,
                aberrationsEnabled: true, tiltAngleDegrees: 225.0, tiltAmountMicrons: 200.0, backfocusErrorMicrons: 120.0,
                astigmatismEnabled: true, cornerAstigmatismMicrons: 40.0);

            var truth = new List<StarTruth>();
            new StarFieldCompositor(new FakeCatalogReader(offFrame)).Render(request, truth, CancellationToken.None);
            Assert.That(truth, Has.Count.EqualTo(1), "an off-frame star whose donut reaches the sensor is still stamped");
        }

        private static long WindowSum(ushort[] pixels, int width, int cx, int cy, int radius) {
            long sum = 0;
            var height = pixels.Length / width;
            for (var y = Math.Max(0, cy - radius); y <= Math.Min(height - 1, cy + radius); ++y) {
                for (var x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); ++x) {
                    sum += pixels[y * width + x];
                }
            }
            return sum;
        }

        private static (int x, int y) WindowArgMax(ushort[] pixels, int width, int cx, int cy, int radius) {
            var height = pixels.Length / width;
            ushort best = 0;
            int bx = cx, by = cy;
            for (var y = Math.Max(0, cy - radius); y <= Math.Min(height - 1, cy + radius); ++y) {
                for (var x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); ++x) {
                    var v = pixels[y * width + x];
                    if (v > best) { best = v; bx = x; by = y; }
                }
            }
            return (bx, by);
        }
    }
}
