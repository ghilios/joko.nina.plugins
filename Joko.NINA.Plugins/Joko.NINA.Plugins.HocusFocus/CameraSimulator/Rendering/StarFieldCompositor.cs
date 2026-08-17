#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// The render pipeline. Turns an immutable <see cref="RenderRequest"/> into a row-major
    /// <c>ushort[width*height]</c> 16-bit frame:
    /// <list type="number">
    /// <item>resolve the sensor/filter and build the radiometry, defocus, aberration-surface, and TAN-projection models;</item>
    /// <item>resolve the catalog reader for the request's catalog path and query it for the sensor's diagonal field of view;</item>
    /// <item>project each star, look up its <b>local</b> defocus Δ from the aberration surface, quantize Δ, and
    /// build (once, cached) the analytic PSF kernel for that level;</item>
    /// <item>stamp <c>kernel·flux</c> into a shared electron accumulator using a disjoint horizontal row-stripe
    /// partition (race-free — see below), add sky + dark, and develop to ADU with Poisson + read noise.</item>
    /// </list>
    ///
    /// <para><b>Catalog robustness.</b> A missing/absent ASTAP database, a pointing outside the catalog's
    /// coverage, a per-cell decode error, or simply zero stars never fails the exposure: the compositor logs a
    /// descriptive warning and renders a <b>starless</b> frame (sky + dark + noise only). The focuser/mount
    /// disconnected checks are the camera's hard errors, not the compositor's.</para>
    ///
    /// <para><b>Catalog path.</b> The reader is resolved <b>per exposure</b> from the request's
    /// <see cref="RenderRequest.AstapCatalogPath"/> snapshot rather than latched at construction, so editing the
    /// option takes effect on the very next exposure (no camera reconnect) and the missing-database warning always
    /// names the path that was actually read.</para>
    ///
    /// <para><b>Concurrency.</b> <see cref="StarStamper.Stamp"/>'s accumulator write is a non-atomic <c>+=</c>
    /// and defocused donuts overlap, so stamping cannot be parallelized over stars into one shared buffer. This
    /// compositor instead partitions the frame into horizontal <b>row-stripes</b>: each stripe owns a disjoint
    /// block of output rows and, running in parallel, stamps every star whose kernel footprint intersects those
    /// rows into <b>only its own rows</b> of the one shared accumulator (a star spanning two stripes is stamped
    /// by both, each clipping to its rows). Because the stripes write disjoint rows, the shared-array
    /// <c>+=</c> never races. Development then runs in parallel too, via <see cref="FrameDeveloper"/>: one
    /// seeded <see cref="NoiseGenerator"/> per stripe over a disjoint index range, so the noise stays
    /// deterministic for a given seed.</para>
    ///
    /// <para>Note the two partitions are <b>not</b> the same kind of thing and must not be harmonized. This
    /// stamping partition is a pure execution detail — stripes only decide <i>who writes which rows</i>, never
    /// what value lands there — so <see cref="StripeCount"/> is free to track
    /// <see cref="Environment.ProcessorCount"/>. <see cref="FrameDeveloper.StripeCount"/> is a fixed constant
    /// because each of its stripes draws from its own RNG stream, which makes that partition part of the frame's
    /// identity.</para>
    /// </summary>
    public class StarFieldCompositor : IStarFieldCompositor {

        /// <summary>
        /// Defocus quantization resolution, expressed as the change in the geometric donut <b>outer radius</b>
        /// (px) between adjacent quantized levels. One PSF kernel is built (and cached) per distinct level, so a
        /// finer value builds more kernels but tracks the HFR-vs-focuser curve more smoothly. 0.25 px keeps the
        /// per-star HFR error well under the detector's own bias while collapsing the smooth aberration surface
        /// onto a small kernel set (adjacent field points share a level).
        /// </summary>
        private const double DonutRadiusQuantumPixels = 0.25;

        /// <summary>Floor on the quantization step (µm) so a fast optic (small N) cannot produce a degenerate sub-µm quantum.</summary>
        private const double MinDefocusQuantumMicrons = 0.5;

        /// <summary>
        /// Kernel-support cap used when clamping the quantized defocus. Kept a little below
        /// <see cref="PsfKernelGenerator.MaxKernelRadius"/> so <c>ceil(r_out + 5σ)</c> stays within the hard cap
        /// even after the σ tail is added. A physically-absurd defocus (far past any real focuser travel) is
        /// clamped to the largest kernel rather than throwing.
        /// </summary>
        private const int MaxSafeKernelRadius = 500;

        /// <summary>Fractional margin added to the diagonal FOV so stars just outside the exact frame edge are still queried.</summary>
        private const double FovMarginFactor = 1.05;

        /// <summary>Target rows per stripe used to size the row-stripe partition (bounds boundary re-stamping).</summary>
        private const int TargetRowsPerStripe = 128;

        /// <summary>
        /// How far (px) the quantized orientation is allowed to displace an ellipse's rim. Deliberately the
        /// same number as <see cref="DonutRadiusQuantumPixels"/>, so the angular and radial quantization errors
        /// are budgeted alike rather than one silently dominating.
        /// </summary>
        private const double OrientationRimQuantumPixels = 0.25;

        /// <summary>
        /// Ceiling on orientation bins. At 64 the position angle is quantized to ±1.4°, comfortably under the
        /// scatter of the detector's own fitted θ, so finer bins would only cost cache.
        /// </summary>
        private const int MaxOrientationBins = 64;

        /// <summary>
        /// Byte budget for one render's PSF kernel cache. The 3-D key is the first thing in this pipeline that
        /// can allocate unboundedly — with one quantized defocus the level count was bounded by the field's own
        /// Δ spread and never needed a guard. One kernel is <c>S²·(2R+1)²·4</c> bytes: 223 KB at R = 29 and
        /// 817 KB at R = 56, on top of a 244 MB accumulator and a 122 MB output for a 61 MP frame.
        /// </summary>
        private const long MaxKernelCacheBytes = 128L * 1024 * 1024;

        /// <summary>Largest factor by which the defocus quantum may be coarsened (and the bins thinned) to fit the budget.</summary>
        private const int MaxCoarseningFactor = 16;

        /// <summary>
        /// Belt-and-braces cap on distinct kernels per render. The up-front budget is the real control; this
        /// catches an estimate that turned out optimistic, by collapsing further stars onto the circular kernel
        /// for their mean defocus — a bounded, well-defined degradation rather than an unbounded allocation.
        /// </summary>
        private const int HardKernelCount = 4096;

        private readonly Func<string, IAstapCatalogReader> catalogReaderFactory;

        /// <param name="catalogReaderFactory">Resolves the ASTAP catalog reader for a catalog directory. Invoked
        /// once per <see cref="Render"/> with that exposure's <see cref="RenderRequest.AstapCatalogPath"/>, so the
        /// reader always reads the path the user currently has configured. The camera passes
        /// <c>path => new AstapCatalogReader(path)</c>; tests inject a fake.</param>
        public StarFieldCompositor(Func<string, IAstapCatalogReader> catalogReaderFactory) {
            this.catalogReaderFactory = catalogReaderFactory ?? throw new ArgumentNullException(nameof(catalogReaderFactory));
        }

        /// <summary>
        /// Convenience overload that binds one reader for every catalog path — for callers (and tests) that
        /// already hold the reader they want and do not care about the request's path.
        /// </summary>
        /// <param name="catalogReader">The ASTAP catalog reader to use regardless of the request's catalog path.</param>
        public StarFieldCompositor(IAstapCatalogReader catalogReader)
            : this(FixedReaderFactory(catalogReader)) {
        }

        /// <summary>Wraps a fixed reader as a path-ignoring factory, validating it eagerly (not at render time).</summary>
        private static Func<string, IAstapCatalogReader> FixedReaderFactory(IAstapCatalogReader catalogReader) {
            if (catalogReader == null) throw new ArgumentNullException(nameof(catalogReader));
            return _ => catalogReader;
        }

        private readonly struct StampJob {
            public StampJob(double cx, double cy, PsfKernel kernel, double flux) {
                Cx = cx;
                Cy = cy;
                Kernel = kernel;
                Flux = flux;
            }

            public double Cx { get; }
            public double Cy { get; }
            public PsfKernel Kernel { get; }
            public double Flux { get; }
        }

        /// <summary>
        /// The PSF kernel cache key. With astigmatism the blur is an ellipse, so one quantized defocus no
        /// longer identifies a kernel: the tangential and sagittal defocuses set the two semi-axes and the
        /// orientation bin sets the position angle.
        ///
        /// <para><b>Isotropy is decided from the quantized levels, never from the raw Δ.</b> Branching on
        /// "the semi-axes are nearly equal" while keying on levels would let two stars share a key and want
        /// different kernels — the cache would stop being a function of its key. With the level rule, equal
        /// |levels| means <see cref="IsotropicOrientationBin"/> and the existing circular generator. And the
        /// collapse lands on exactly today's level: if <c>round((Δ−A)/q) == round((Δ+A)/q) == L</c> then
        /// <c>|Δ − Lq| ≤ q/2</c>, so <c>L == round(Δ/q)</c>. Since A is literally 0.0 when astigmatism is off,
        /// "astigmatism off renders byte-identically" is a proof rather than a hope.</para>
        /// </summary>
        private readonly record struct PsfKernelKey(long LevelT, long LevelS, int OrientationBin);

        /// <summary>Orientation bin meaning "the semi-axes are equal, so this kernel is circular".</summary>
        private const int IsotropicOrientationBin = -1;

        /// <summary>
        /// Everything about the aberration surface that has to be known <b>before</b> the star loop: the
        /// projection margin, the orientation quantization, and the kernel-cache budget all depend on the
        /// field's extremes. Deciding them up front is what keeps fidelity uniform across one frame — a
        /// mid-loop adjustment would render one corner at a different quality from another.
        /// </summary>
        private readonly struct FieldSurvey {
            public FieldSurvey(double maxAbsDefocus, double maxAbsSplit, double defocusRange, double splitRange, double maxCombined) {
                MaxAbsDefocusMicrons = maxAbsDefocus;
                MaxAbsSplitMicrons = maxAbsSplit;
                DefocusRangeMicrons = defocusRange;
                SplitRangeMicrons = splitRange;
                MaxCombinedMicrons = maxCombined;
            }

            /// <summary>Largest |Δ| over the sample points.</summary>
            public double MaxAbsDefocusMicrons { get; }

            /// <summary>Largest |A| over the sample points.</summary>
            public double MaxAbsSplitMicrons { get; }

            /// <summary>Peak-to-peak spread of Δ over the sample points.</summary>
            public double DefocusRangeMicrons { get; }

            /// <summary>Peak-to-peak spread of A over the sample points.</summary>
            public double SplitRangeMicrons { get; }

            /// <summary>Largest |Δ| + |A|, i.e. the largest <c>max(|Δ_T|, |Δ_S|)</c> and so the largest semi-axis.</summary>
            public double MaxCombinedMicrons { get; }
        }

        /// <summary>A star that survived projection, before its kernel-cache key is assigned.</summary>
        private readonly struct ProjectedStar {
            public ProjectedStar(double cx, double cy, int px, int py, double flux, double raDegrees, double decDegrees, double magnitude) {
                Cx = cx; Cy = cy; Px = px; Py = py; Flux = flux;
                RaDegrees = raDegrees; DecDegrees = decDegrees; Magnitude = magnitude;
            }

            public double Cx { get; }
            public double Cy { get; }
            public int Px { get; }
            public int Py { get; }
            public double Flux { get; }
            public double RaDegrees { get; }
            public double DecDegrees { get; }
            public double Magnitude { get; }
        }

        /// <summary>A projected star, resolved down to its kernel-cache slot. Pass 1 of the three-pass build.</summary>
        private readonly struct StarPlacement {
            public StarPlacement(double cx, double cy, double flux, int kernelIndex,
                    double defocusMicrons, double splitMicrons, double quantizedTangential, double quantizedSagittal,
                    int orientationBin, double raDegrees, double decDegrees, double magnitude) {
                Cx = cx; Cy = cy; Flux = flux; KernelIndex = kernelIndex;
                DefocusMicrons = defocusMicrons; SplitMicrons = splitMicrons;
                QuantizedTangentialMicrons = quantizedTangential; QuantizedSagittalMicrons = quantizedSagittal;
                OrientationBin = orientationBin;
                RaDegrees = raDegrees; DecDegrees = decDegrees; Magnitude = magnitude;
            }

            public double Cx { get; }
            public double Cy { get; }
            public double Flux { get; }
            public int KernelIndex { get; }
            public double DefocusMicrons { get; }
            public double SplitMicrons { get; }
            public double QuantizedTangentialMicrons { get; }
            public double QuantizedSagittalMicrons { get; }
            public int OrientationBin { get; }
            public double RaDegrees { get; }
            public double DecDegrees { get; }
            public double Magnitude { get; }
        }

        /// <inheritdoc/>
        public ushort[] Render(RenderRequest request, CancellationToken token) => Render(request, null, null, token);

        /// <summary>
        /// Renders exactly what <see cref="Render(RenderRequest, CancellationToken)"/> does, and — when
        /// <paramref name="truthSink"/> is non-null — appends one <see cref="StarTruth"/> per accepted star to
        /// it as a side effect. This is the synthetic-bank generator's seam onto ground truth the compositor
        /// already computes internally (position, flux, quantized defocus, kernel HFR) and would otherwise
        /// discard; it exists as a second overload rather than a new interface member so
        /// <see cref="IStarFieldCompositor"/> — the camera's MEF-composed seam — is untouched.
        /// <b>Passing a null sink must render byte-identically to the single-argument overload</b>: this method
        /// skips every truth computation entirely when <paramref name="truthSink"/> is null, so the two calls
        /// run the identical stamp/development pipeline with no extra allocation on the hot path.
        /// </summary>
        public ushort[] Render(RenderRequest request, ICollection<StarTruth> truthSink, CancellationToken token)
            => Render(request, truthSink, null, token);

        /// <summary>
        /// The real render, with an optional <see cref="RenderPhaseTimings"/> sink for the
        /// <c>bench-simrender</c> harness. It is a second sink rather than a new interface member for the same
        /// reason <paramref name="truthSink"/> is — <see cref="IStarFieldCompositor"/>, the camera's
        /// MEF-composed seam, stays untouched — and carries the same contract: <b>passing a null
        /// <paramref name="timings"/> must render byte-identically to the public overloads</b>. Every timing
        /// call site is guarded, so a production render never even reads the clock. See
        /// <see cref="RenderPhaseTimings"/> for why the phases are instrumented here instead of being
        /// recovered by differencing two whole renders.
        /// </summary>
        internal ushort[] Render(
                RenderRequest request, ICollection<StarTruth> truthSink, RenderPhaseTimings timings, CancellationToken token) {
            if (request == null) throw new ArgumentNullException(nameof(request));
            token.ThrowIfCancellationRequested();

            var sensor = SensorRegistry.Get(request.SensorModel);
            var filter = FilterRegistry.Get(request.Filter);
            int width = sensor.Width;
            int height = sensor.Height;

            // The radiometry/defocus/aberration models and the TAN projection all require a positive focal length
            // (plate scale). A focal length of 0 is a misconfiguration — the camera resolves it from the profile —
            // but rather than crash the exposure we render a dark frame (bias + dark + read noise, no sky/stars).
            // No truth either way: a dark frame has no stars to have truth about.
            double sky = 0.0, dark = 0.0;
            List<StampJob> jobs;
            if (request.FocalLengthMillimeters > 0.0) {
                var radiometry = RadiometryCalculator.FromRequest(request, sensor, filter);
                var defocusModel = DefocusModel.FromRequest(request, sensor, filter);
                var aberration = AberrationSurface.FromRequest(request, sensor);
                sky = radiometry.SkyElectronsPerPixel();
                dark = radiometry.DarkElectronsPerPixel();
                var jobBuildStart = timings != null ? Stopwatch.GetTimestamp() : 0L;
                jobs = BuildStampJobs(request, sensor, radiometry, defocusModel, aberration, width, height, truthSink, timings, token);
                if (timings != null) {
                    timings.StampJobBuildMs = RenderPhaseTimings.ElapsedMs(jobBuildStart);
                    timings.StampJobs = jobs.Count;
                }
            } else {
                Logger.Warning(
                    $"Synthetic camera: focal length is {request.FocalLengthMillimeters} mm (must be > 0). " +
                    "Rendering a dark frame (bias + dark + noise only, no sky or stars).");
                dark = sensor.DarkElectronsPerPixelPerSecondAtTemperature(request.SensorTemperatureCelsius) * Math.Max(0.0, request.ExposureSeconds);
                jobs = new List<StampJob>();
            }

            var accumulator = new float[width * height];

            // Stamp every job into the shared accumulator via the disjoint row-stripe partition, then fold in the
            // uniform sky + dark background. Both are per-stripe writes to disjoint rows, so the shared-array
            // accumulation never races.
            var background = (float)(sky + dark);
            var stampStart = timings != null ? Stopwatch.GetTimestamp() : 0L;
            StampAndAddBackground(accumulator, width, height, jobs, background, token);
            if (timings != null) {
                timings.StampMs = RenderPhaseTimings.ElapsedMs(stampStart);
            }

            token.ThrowIfCancellationRequested();

            // Development is ~90% of a 61 MP render, so it runs in parallel — deterministically, via a fixed
            // stripe partition with one seeded generator per stripe. See FrameDeveloper.StripeCount.
            var developStart = timings != null ? Stopwatch.GetTimestamp() : 0L;
            var frame = FrameDeveloper.DevelopToAdu(
                accumulator, sensor, request.Gain, request.BiasPedestalAdu, request.NoiseSeed, token);
            if (timings != null) {
                timings.DevelopMs = RenderPhaseTimings.ElapsedMs(developStart);
            }
            return frame;
        }

        /// <summary>
        /// Projects the catalog stars and turns each on-frame (or wing-spilling) star into a <see cref="StampJob"/>:
        /// its sub-pixel centre, the cached PSF kernel for its quantized local defocus, and its total flux (e⁻).
        /// When <paramref name="truthSink"/> is non-null, appends one <see cref="StarTruth"/> per accepted job to
        /// it — same loop, same acceptance decision, so a wing-spill star that gets a <see cref="StampJob"/> also
        /// gets truth (that is exactly the case a recall audit cares about: its donut lands on the sensor even
        /// though its centre does not). When <paramref name="truthSink"/> is null this method does no extra work
        /// or allocation beyond what it always did.
        /// </summary>
        private List<StampJob> BuildStampJobs(
                RenderRequest request, SensorDefinition sensor, RadiometryCalculator radiometry,
                DefocusModel defocusModel, AberrationSurface aberration, int width, int height,
                ICollection<StarTruth> truthSink, RenderPhaseTimings timings, CancellationToken token) {

            var projection = new TanProjection(
                request.RaDegreesJ2000, request.DecDegreesJ2000,
                request.FocalLengthMillimeters, sensor.PixelSizeMicrons, request.RotationDegrees, width, height);

            var fovDeg = DiagonalFovDegrees(width, height, sensor.PixelSizeMicrons, request.FocalLengthMillimeters) * FovMarginFactor;

            var quantumMicrons = DefocusQuantumMicrons(defocusModel);
            var maxAbsMicrons = MaxAbsDefocusMicrons(defocusModel);

            // Everything that must be settled before the star loop, from a handful of field extrema.
            var field = SurveyField(request, sensor, aberration);
            var astigmatic = aberration.AstigmatismCoefficient != 0.0;
            var orientationBins = astigmatic ? OrientationBinCount(field, defocusModel) : 1;

            // PSF margin: the worst-case kernel radius over the field for this focuser position. Stars whose
            // centres fall up to this far off-frame still spill their donut onto the sensor.
            var psfMargin = WorstCaseKernelRadius(defocusModel, field, quantumMicrons, maxAbsMicrons);

            var catalogStart = timings != null ? Stopwatch.GetTimestamp() : 0L;
            var stars = QueryCatalogStars(request, fovDeg);
            if (timings != null) {
                timings.CatalogQueryMs = RenderPhaseTimings.ElapsedMs(catalogStart);
                timings.StarsQueried = stars.Count;
            }

            // --- pass 1a: project every star once ---
            // Projection is the expensive part of the star loop, so it is done once and the cheap key
            // assignment (pass 1b) can be repeated while the cache budget is being resolved.
            var projected = new List<ProjectedStar>(stars.Count);
            var processed = 0;
            foreach (var star in stars) {
                if ((++processed & 0x3FFF) == 0) {
                    token.ThrowIfCancellationRequested();
                }

                var coordinates = star.Coordinates;
                if (coordinates == null) {
                    continue;
                }
                if (coordinates.Epoch != Epoch.J2000) {
                    coordinates = coordinates.Transform(Epoch.J2000);
                }

                if (!projection.TryProject(coordinates.RADegrees, coordinates.Dec, out var x, out var y, psfMargin)) {
                    continue;
                }

                // Flux first: a star that contributes nothing must not register a kernel nobody stamps with.
                var flux = radiometry.StarElectrons(star.Magnitude);
                if (flux <= 0.0) {
                    continue;
                }

                projected.Add(new ProjectedStar(
                    x, y, (int)Math.Round(x), (int)Math.Round(y), flux,
                    coordinates.RADegrees, coordinates.Dec, star.Magnitude));
            }

            // --- pass 1b: assign kernel-cache keys, resolving the cache budget by exact count ---
            // The keys are computed from arithmetic alone -- no kernel is built here -- so the budget can be
            // checked against what the cache would ACTUALLY hold rather than against an upper bound. That
            // matters: bounding the count by (Δ cells x A cells x orientation bins) over-counts by several
            // times, because Δ and A are both smooth functions of field position and so are far from
            // independent. Coarsening on that estimate would degrade every frame's fidelity to fit a cache
            // that was never going to be allocated.
            // The ladder is two passes, not one: coarsen with astigmatism, and only if the largest coarsening
            // still does not fit, coarsen again with astigmatism dropped. Dropping it is NOT itself a way to
            // fit the budget — the dominant term is (number of Δ levels × kernel size), and astigmatism
            // controls neither — so a fallback that reverted to the fine quantum would announce that it had
            // bounded the cache while allocating gigabytes. That is exactly what a 10 mm backfocus error did:
            // 822 kernels at 3.2 GB, on a render that claimed to have given up to stay inside 128 MB.
            List<PsfKernelKey> keys = null;
            List<StarPlacement> placements = null;
            var resolvedQuantum = quantumMicrons;
            var resolvedBins = orientationBins;
            var resolvedAstigmatic = astigmatic;
            var fitted = false;
            long lastBytes = 0;

            foreach (var attemptAstigmatic in astigmatic ? new[] { true, false } : new[] { false }) {
                for (var coarsening = 1; coarsening <= MaxCoarseningFactor; coarsening *= 2) {
                    var attemptQuantum = quantumMicrons * coarsening;
                    var attemptBins = attemptAstigmatic ? Math.Max(1, orientationBins / coarsening) : 1;

                    (placements, keys) = AssignKernelKeys(
                        projected, aberration, request.FocuserPosition, attemptQuantum, maxAbsMicrons,
                        attemptBins, attemptAstigmatic);
                    lastBytes = EstimateKernelCacheBytes(keys, defocusModel, attemptQuantum);
                    resolvedQuantum = attemptQuantum;
                    resolvedBins = attemptBins;
                    resolvedAstigmatic = attemptAstigmatic;

                    if (lastBytes <= MaxKernelCacheBytes) {
                        // Only when something was actually traded away. The ordinary case -- first attempt,
                        // full fidelity -- says nothing, or every isotropic render would log a line about a
                        // budget it never came close to.
                        if (coarsening > 1) {
                            Logger.Info(
                                $"Synthetic camera: the PSF cache needed coarsening to fit the {MaxKernelCacheBytes / 1048576} MB "
                                + $"budget — defocus quantum {attemptQuantum:F1} µm, {attemptBins} orientation bins, "
                                + $"astigmatism {(attemptAstigmatic ? "on" : "off")}, {keys.Count} kernels ({lastBytes / 1048576.0:F0} MB).");
                        }
                        fitted = true;
                        break;
                    }
                }
                if (fitted) {
                    break;
                }
                if (attemptAstigmatic) {
                    Logger.Warning(
                        $"Synthetic camera: the astigmatic PSF cache still needs {lastBytes / 1048576.0:F0} MB at the maximum "
                        + $"{MaxCoarseningFactor}× coarsening, over the {MaxKernelCacheBytes / 1048576} MB budget "
                        + $"(Δ spread {field.DefocusRangeMicrons:F0} µm, astigmatism spread {field.SplitRangeMicrons:F0} µm). "
                        + "Rendering this frame with circular donuts — reduce the injected tilt, backfocus error, or "
                        + "astigmatism ratio to get the elliptical model back.");
                }
            }

            if (!fitted) {
                // Even circular donuts at the coarsest quantum do not fit. The kernels are simply enormous —
                // a defocus this far out of range makes each one hundreds of pixels across — so there is
                // nothing left to trade. Render it and say so, rather than pretending a budget was honoured.
                Logger.Warning(
                    $"Synthetic camera: the PSF cache needs {lastBytes / 1048576.0:F0} MB even with circular donuts at the "
                    + $"maximum {MaxCoarseningFactor}× coarsening ({keys.Count} kernels up to "
                    + $"{KernelRadiusPixels(defocusModel, field.MaxCombinedMicrons)} px radius). The injected defocus spread of "
                    + $"{field.DefocusRangeMicrons:F0} µm is far past any real focuser travel; reduce the backfocus error or tilt.");
            }

            quantumMicrons = resolvedQuantum;
            orientationBins = resolvedBins;
            astigmatic = resolvedAstigmatic;

            // --- pass 2: build the distinct kernels in parallel ---
            // Deterministic: the key list order is the deterministic star order, each kernel is a pure function
            // of its key, and every write lands in its own pre-indexed slot. Routed through the shared CPU
            // governor for the same reason the stamp and development loops are -- a render is kicked off at
            // StartExposure and runs alongside the star detection of the previous autofocus point.
            var kernels = new PsfKernel[keys.Count];
            var kernelStart = timings != null ? Stopwatch.GetTimestamp() : 0L;
            var kernelOptions = ParallelExecution.CreateOptions(0, token);
            Parallel.For(0, keys.Count, kernelOptions, i => {
                var key = keys[i];
                kernels[i] = key.OrientationBin == IsotropicOrientationBin
                    ? PsfKernelGenerator.Generate(defocusModel, key.LevelT * quantumMicrons)
                    : PsfKernelGenerator.GenerateAstigmatic(
                        defocusModel, key.LevelT * quantumMicrons, key.LevelS * quantumMicrons,
                        OrientationBinAngle(key.OrientationBin, orientationBins));
            });
            if (timings != null) {
                timings.KernelGenerateMs = RenderPhaseTimings.ElapsedMs(kernelStart);
            }

            // --- pass 3: attach kernels to jobs, and emit truth ---
            var jobs = new List<StampJob>(placements.Count);
            foreach (var placement in placements) {
                var kernel = kernels[placement.KernelIndex];
                jobs.Add(new StampJob(placement.Cx, placement.Cy, kernel, placement.Flux));

                if (truthSink != null) {
                    // Same phase selection Stamp itself will make for this exact (x, y) and kernel — see
                    // StarStamper.SelectPhase's doc for why this can never disagree with the actual stamp.
                    StarStamper.SelectPhase(placement.Cx, placement.Cy, kernel.PhasesPerAxis, out var phaseX, out var phaseY);
                    truthSink.Add(new StarTruth {
                        CxPixels = placement.Cx,
                        CyPixels = placement.Cy,
                        RaDegrees = placement.RaDegrees,
                        DecDegrees = placement.DecDegrees,
                        MagnitudeV = placement.Magnitude,
                        FluxElectrons = placement.Flux,
                        LocalDefocusMicrons = placement.DefocusMicrons,
                        QuantizedDefocusMicrons = 0.5 * (placement.QuantizedTangentialMicrons + placement.QuantizedSagittalMicrons),
                        AstigmatismSplitMicrons = placement.SplitMicrons,
                        QuantizedTangentialDefocusMicrons = placement.QuantizedTangentialMicrons,
                        QuantizedSagittalDefocusMicrons = placement.QuantizedSagittalMicrons,
                        OrientationBin = placement.OrientationBin,
                        AnalyticHfrPixels = kernel.AnalyticHfrPixels,
                        MeasuredHfrPixels = kernel.MeasuredHfrPixels,
                        OuterRadiusPixels = kernel.OuterRadiusPixels,
                        InnerRadiusPixels = kernel.InnerRadiusPixels,
                        OuterRadiusRadialPixels = kernel.OuterRadiusRadialPixels,
                        OuterRadiusTangentialPixels = kernel.OuterRadiusTangentialPixels,
                        PositionAngleRadians = kernel.PositionAngleRadians,
                        PredictedEccentricity = kernel.PredictedEccentricity,
                        KernelSupportRadiusPixels = kernel.Radius,
                        PhaseX = phaseX,
                        PhaseY = phaseY,
                        KernelPeakFraction = kernel.PhasePeak(phaseX, phaseY)
                    });
                }
            }

            if (timings != null) {
                timings.DistinctKernels = kernels.Length;
                timings.OrientationBins = orientationBins;
                timings.DefocusQuantumMicrons = quantumMicrons;
                long cacheBytes = 0;
                var maxRadius = 0;
                foreach (var cached in kernels) {
                    cacheBytes += cached.ApproximateByteSize;
                    if (cached.Radius > maxRadius) maxRadius = cached.Radius;
                }
                timings.KernelCacheBytes = cacheBytes;
                timings.MaxKernelRadius = maxRadius;
            }
            return jobs;
        }

        /// <summary>
        /// Samples the aberration surface at the field's extremes: the four corners, the sensor centre, and the
        /// interior stationary point of the plane-plus-paraboloid.
        ///
        /// <para>That last point matters and used to be missing. <c>z = Gx·x' + Gy·y' + K·r'²</c> is stationary
        /// at <c>x' = −Gx/(2K), y' = −Gy/(2K)</c>, which coincides with the sensor centre only when the
        /// gradients and the optical-axis offset all vanish. With a large axis offset the true extremum sits
        /// away from both, so a corners-plus-centre sample undersizes the projection margin and silently drops
        /// wing-spilling stars.</para>
        /// </summary>
        private static FieldSurvey SurveyField(RenderRequest request, SensorDefinition sensor, AberrationSurface aberration) {
            int w = sensor.Width, h = sensor.Height;
            var samples = new List<(int px, int py)>(6) {
                (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w / 2, h / 2)
            };
            if (aberration.K != 0.0) {
                var stationaryX = aberration.X0 - aberration.Gx / (2.0 * aberration.K);
                var stationaryY = aberration.Y0 - aberration.Gy / (2.0 * aberration.K);
                var px = (int)Math.Round(stationaryX / sensor.PixelSizeMicrons + w / 2.0);
                var py = (int)Math.Round(stationaryY / sensor.PixelSizeMicrons + h / 2.0);
                samples.Add((Math.Clamp(px, 0, w - 1), Math.Clamp(py, 0, h - 1)));
            }

            double maxAbsDefocus = 0.0, maxAbsSplit = 0.0, maxCombined = 0.0;
            double minDefocus = double.MaxValue, maxDefocus = double.MinValue;
            double minSplit = double.MaxValue, maxSplit = double.MinValue;
            foreach (var (px, py) in samples) {
                var defocus = aberration.LocalDefocusMicrons(px, py, request.FocuserPosition);
                var split = aberration.AstigmatismSplitMicrons(px, py);
                maxAbsDefocus = Math.Max(maxAbsDefocus, Math.Abs(defocus));
                maxAbsSplit = Math.Max(maxAbsSplit, Math.Abs(split));
                // max(|Δ−A|, |Δ+A|) == |Δ| + |A|, so this is the largest semi-axis anywhere in the field.
                maxCombined = Math.Max(maxCombined, Math.Abs(defocus) + Math.Abs(split));
                minDefocus = Math.Min(minDefocus, defocus);
                maxDefocus = Math.Max(maxDefocus, defocus);
                minSplit = Math.Min(minSplit, split);
                maxSplit = Math.Max(maxSplit, split);
            }
            return new FieldSurvey(maxAbsDefocus, maxAbsSplit, maxDefocus - minDefocus, maxSplit - minSplit, maxCombined);
        }

        /// <summary>
        /// How finely the ellipse orientation must be quantized, from how elliptical the field actually gets.
        ///
        /// <para>Rotating an ellipse by δ displaces its rim by at most <c>√2·δ·(a − b)</c>. Generating each
        /// kernel at its <b>bin centre</b> bounds <c>δ ≤ π/(2n)</c> — orientation is mod π for an ellipse,
        /// which halves the bin count for free — so holding the rim error to
        /// <see cref="OrientationRimQuantumPixels"/> needs <c>n ≥ π·s_max/(√2·ε)</c>. The axis difference is
        /// <c>|a_rad − a_tan| = min(|Δ|, |A|)/(N·p)</c>, bounded here by the field's separate maxima.</para>
        ///
        /// <para>As the field becomes round <c>s_max → 0</c> and this returns 1 — and in that regime every star
        /// also collapses to equal levels, so the frame degenerates to exactly today's kernel set.</para>
        /// </summary>
        private static int OrientationBinCount(in FieldSurvey field, DefocusModel model) {
            var separationPixels = Math.Min(field.MaxAbsDefocusMicrons, field.MaxAbsSplitMicrons)
                                 / (model.FocalRatio * model.PixelSizeMicrons);
            if (!(separationPixels > 0.0)) {
                return 1;
            }
            var bins = (int)Math.Ceiling(Math.PI * separationPixels / (Math.Sqrt(2.0) * OrientationRimQuantumPixels));
            return Math.Clamp(bins, 1, MaxOrientationBins);
        }

        /// <summary>The orientation bin for a field angle. Orientation is mod π: an ellipse is unchanged by a half turn.</summary>
        private static int OrientationBin(double thetaRadians, int bins) {
            if (bins <= 1) {
                return 0;
            }
            var wrapped = thetaRadians - Math.PI * Math.Floor(thetaRadians / Math.PI);
            return Math.Clamp((int)(wrapped / Math.PI * bins), 0, bins - 1);
        }

        /// <summary>The centre angle of an orientation bin — where its one shared kernel is generated.</summary>
        private static double OrientationBinAngle(int bin, int bins) => (bin + 0.5) * Math.PI / bins;

        /// <summary>
        /// Assigns every projected star its kernel-cache key, and returns the placements alongside the distinct
        /// keys in first-seen (i.e. deterministic star) order. Pure arithmetic — no kernel is built here, which
        /// is what lets the caller try a quantization, measure the cache it would really produce, and try again.
        /// </summary>
        private static (List<StarPlacement> placements, List<PsfKernelKey> keys) AssignKernelKeys(
                List<ProjectedStar> projected, AberrationSurface aberration, int focuserSteps,
                double quantumMicrons, double maxAbsMicrons, int orientationBins, bool astigmatic) {

            var placements = new List<StarPlacement>(projected.Count);
            var keys = new List<PsfKernelKey>();
            var keyIndices = new Dictionary<PsfKernelKey, int>();

            foreach (var star in projected) {
                double defocusT, defocusS, theta, split;
                if (astigmatic) {
                    aberration.AstigmaticDefocusMicrons(star.Px, star.Py, focuserSteps, out defocusT, out defocusS, out theta);
                    split = 0.5 * (defocusS - defocusT);
                } else {
                    defocusT = defocusS = aberration.LocalDefocusMicrons(star.Px, star.Py, focuserSteps);
                    theta = 0.0;
                    split = 0.0;
                }

                var levelT = QuantizeLevel(defocusT, quantumMicrons, maxAbsMicrons);
                var levelS = QuantizeLevel(defocusS, quantumMicrons, maxAbsMicrons);
                // Equal |levels| means equal semi-axes -- including the sensor sitting midway between the two
                // focal surfaces, where the blur is the round circle of least confusion.
                var bin = Math.Abs(levelT) == Math.Abs(levelS)
                    ? IsotropicOrientationBin
                    : OrientationBin(theta, orientationBins);
                var key = new PsfKernelKey(levelT, levelS, bin);

                if (!keyIndices.TryGetValue(key, out var kernelIndex)) {
                    if (keys.Count >= HardKernelCount) {
                        // Collapse onto the circular kernel for this star's mean defocus: bounded (there are at
                        // most as many of those as there are Δ levels), well defined, and it renders the star
                        // round at the right size rather than at an arbitrary neighbour's shape.
                        var meanLevel = QuantizeLevel(0.5 * (defocusT + defocusS), quantumMicrons, maxAbsMicrons);
                        key = new PsfKernelKey(meanLevel, meanLevel, IsotropicOrientationBin);
                    }
                    if (!keyIndices.TryGetValue(key, out kernelIndex)) {
                        kernelIndex = keys.Count;
                        keys.Add(key);
                        keyIndices[key] = kernelIndex;
                    }
                }

                placements.Add(new StarPlacement(
                    star.Cx, star.Cy, star.Flux, kernelIndex, 0.5 * (defocusT + defocusS), split,
                    levelT * quantumMicrons, levelS * quantumMicrons, key.OrientationBin,
                    star.RaDegrees, star.DecDegrees, star.Magnitude));
            }
            return (placements, keys);
        }

        /// <summary>
        /// Bytes the cache will hold for a resolved key set — summed per key from the support radius that key
        /// implies, so it is what the render is actually about to allocate rather than a bound on it.
        /// </summary>
        private static long EstimateKernelCacheBytes(List<PsfKernelKey> keys, DefocusModel model, double quantumMicrons) {
            var phases = (long)PsfKernelGenerator.DefaultPhasesPerAxis * PsfKernelGenerator.DefaultPhasesPerAxis;
            long total = 0;
            foreach (var key in keys) {
                var largestDefocus = Math.Max(Math.Abs(key.LevelT), Math.Abs(key.LevelS)) * quantumMicrons;
                var edge = 2L * KernelRadiusPixels(model, largestDefocus) + 1;
                total += phases * edge * edge * sizeof(float);
            }
            return total;
        }

        /// <summary>Kernel support radius (px) for a defocus, clamped to the safe cap.</summary>
        private static int KernelRadiusPixels(DefocusModel model, double defocusMicrons) {
            var radius = (int)Math.Ceiling(model.OuterAnnulusRadiusPixels(defocusMicrons) + 5.0 * model.SigmaMinPixels);
            return Math.Clamp(radius, 1, MaxSafeKernelRadius);
        }

        /// <summary>
        /// Resolves the reader for this exposure's catalog path and queries it for the pointing, returning an
        /// empty list (never throwing) on any failure. A missing/absent database is logged distinctly from a
        /// pointing that simply has no catalog stars, so the user can tell "install an ASTAP database" apart from
        /// "this patch of sky is empty".
        /// </summary>
        private List<CatalogStar> QueryCatalogStars(RenderRequest request, double fovDeg) {
            try {
                var stars = new List<CatalogStar>();
                // Resolved here, from the request's path snapshot, so an options edit lands on the next exposure —
                // and inside the try, so a factory that rejects the path is the same starless-frame warning as a
                // path that simply has no database, rather than a failed exposure.
                var catalogReader = catalogReaderFactory(request.AstapCatalogPath);
                // Query() validates the folder/database eagerly; per-cell decode errors surface during enumeration,
                // so the foreach is inside the try too.
                foreach (var star in catalogReader.Query(request.RaDegreesJ2000, request.DecDegreesJ2000, fovDeg, request.LimitingMagnitude)) {
                    stars.Add(star);
                }
                if (stars.Count == 0) {
                    Logger.Warning(
                        $"Synthetic camera: the pointing RA={request.RaDegreesJ2000:F4}°, Dec={request.DecDegreesJ2000:F4}° has no catalog stars " +
                        $"down to magnitude {request.LimitingMagnitude:F1} within a {fovDeg:F2}° field. Rendering a starless frame.");
                }
                return stars;
            } catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException) {
                Logger.Warning(
                    $"Synthetic camera: no ASTAP database found at '{request.AstapCatalogPath}' ({ex.Message}). " +
                    "Rendering a starless frame (sky + dark + noise only). Install an ASTAP star database and point the catalog path at it to render stars.");
                return new List<CatalogStar>();
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Warning(
                    $"Synthetic camera: the catalog query for RA={request.RaDegreesJ2000:F4}°, Dec={request.DecDegreesJ2000:F4}° " +
                    $"failed ({ex.GetType().Name}: {ex.Message}). Rendering a starless frame.");
                return new List<CatalogStar>();
            }
        }

        /// <summary>
        /// Stamps all jobs into <paramref name="accumulator"/> and adds the uniform background, both via a
        /// disjoint horizontal row-stripe partition so the shared-array writes never race.
        /// </summary>
        private static void StampAndAddBackground(
                float[] accumulator, int width, int height, IReadOnlyList<StampJob> jobs, float background, CancellationToken token) {

            var stripeCount = StripeCount(height);
            // Routed through the shared CPU governor for the same reason FrameDeveloper is: a render is kicked
            // off at StartExposure and runs alongside the star detection of the previous autofocus point, so an
            // ungoverned loop here would stack ProcessorCount threads on top of detection's. Passing stripeCount
            // as the knob keeps the degree exactly what it was (it is already <= ProcessorCount), so this changes
            // only which scheduler runs the loop. Value-neutral: each stripe writes just its own rows, so every
            // pixel still sees its jobs applied in j-ascending order with the background last, whatever the
            // scheduler or thread count does.
            var options = ParallelExecution.CreateOptions(stripeCount, token);
            Parallel.For(0, stripeCount, options, stripe => {
                var rowStart = (int)((long)stripe * height / stripeCount);
                var rowEnd = (int)((long)(stripe + 1) * height / stripeCount);
                if (rowStart >= rowEnd) {
                    return;
                }
                options.CancellationToken.ThrowIfCancellationRequested();

                for (var j = 0; j < jobs.Count; ++j) {
                    var job = jobs[j];
                    // Vertical footprint of this star's kernel; skip jobs that do not touch this stripe.
                    var y0 = (int)Math.Floor(job.Cy);
                    var top = y0 - job.Kernel.Radius - 1;
                    var bottom = y0 + job.Kernel.Radius + 1;
                    if (bottom < rowStart || top >= rowEnd) {
                        continue;
                    }
                    // The row clip is what makes the parallel stamp race-free: this stripe writes only its own
                    // [rowStart, rowEnd) rows of the shared accumulator, disjoint from every other stripe.
                    StarStamper.Stamp(accumulator, width, height, job.Cx, job.Cy, job.Kernel, job.Flux, rowStart, rowEnd);
                }

                if (background != 0f) {
                    var from = rowStart * width;
                    var to = rowEnd * width;
                    for (var i = from; i < to; ++i) {
                        accumulator[i] += background;
                    }
                }
            });
        }

        /// <summary>Angular diagonal field of view (degrees) for a sensor at a focal length.</summary>
        internal static double DiagonalFovDegrees(int width, int height, double pixelSizeMicrons, double focalLengthMm) {
            var diagonalPixels = Math.Sqrt((double)width * width + (double)height * height);
            var diagonalMm = 0.5 * diagonalPixels * (pixelSizeMicrons / 1000.0);
            return 2.0 * Math.Atan(diagonalMm / focalLengthMm) * 180.0 / Math.PI;
        }

        /// <summary>Defocus quantization step (µm) sized for <see cref="DonutRadiusQuantumPixels"/> of donut-radius resolution.</summary>
        private static double DefocusQuantumMicrons(DefocusModel model) {
            // r_out(px) = |Δ| / (2·N·p); a q-px change in r_out ⇒ a (q·2·N·p) µm change in Δ.
            var quantum = DonutRadiusQuantumPixels * 2.0 * model.FocalRatio * model.PixelSizeMicrons;
            return Math.Max(MinDefocusQuantumMicrons, quantum);
        }

        /// <summary>
        /// Largest |Δ| (µm) whose kernel support stays within <see cref="MaxSafeKernelRadius"/>. Quantized defocus
        /// is clamped to this so a runaway field point can never allocate an unbounded kernel.
        ///
        /// This is the <b>single source of truth for the kernel-cap guard</b>: both this renderer's own
        /// <see cref="QuantizeLevel"/> clamp and any external caller that needs to know "how far past focus can
        /// this model still render" — e.g. a synthetic-bank generator picking a defocus sweep range without
        /// risking a <see cref="PsfKernelGenerator.MaxKernelRadius"/> throw — must go through this one method,
        /// so the render-time clamp and an offline check of it can never drift apart. Lifted from <c>private</c>
        /// for exactly that reason.
        /// </summary>
        internal static double MaxAbsDefocusMicrons(DefocusModel model) {
            // radius ≈ ceil(r_out + 5σ) ≤ MaxSafeKernelRadius ⇒ r_out ≤ MaxSafeKernelRadius − 5σ − 1 (px).
            var maxOuterRadiusPixels = MaxSafeKernelRadius - 5.0 * model.SigmaMinPixels - 1.0;
            if (maxOuterRadiusPixels <= 0.0) {
                return 0.0;
            }
            return maxOuterRadiusPixels * 2.0 * model.FocalRatio * model.PixelSizeMicrons;
        }

        /// <summary>Quantizes a defocus Δ (µm) to an integer level after clamping to the safe range.</summary>
        private static long QuantizeLevel(double deltaMicrons, double quantumMicrons, double maxAbsMicrons) {
            var clamped = Math.Clamp(deltaMicrons, -maxAbsMicrons, maxAbsMicrons);
            return (long)Math.Round(clamped / quantumMicrons);
        }

        /// <summary>
        /// The worst-case kernel support radius (px) over the field for this focuser position. Used as the
        /// projection PSF margin so wing-spilling corner stars are not dropped.
        ///
        /// <para>Sized from <c>|Δ| + |A|</c>, not from <c>|Δ|</c>: the two semi-axes are <c>|Δ − A|</c> and
        /// <c>|Δ + A|</c>, so the larger one — the one that sets the support — is <c>|Δ| + |A|</c>. With
        /// astigmatism off, A is 0 and this is exactly what it always was.</para>
        /// </summary>
        private static int WorstCaseKernelRadius(DefocusModel model, in FieldSurvey field, double quantumMicrons, double maxAbsMicrons) {
            var level = QuantizeLevel(field.MaxCombinedMicrons, quantumMicrons, maxAbsMicrons);
            return KernelRadiusPixels(model, level * quantumMicrons);
        }

        /// <summary>Number of row-stripes for the parallel stamp, bounded by the CPU count and the frame height.</summary>
        private static int StripeCount(int height) {
            var byHeight = Math.Max(1, height / TargetRowsPerStripe);
            return Math.Clamp(Environment.ProcessorCount, 1, byHeight);
        }
    }
}
