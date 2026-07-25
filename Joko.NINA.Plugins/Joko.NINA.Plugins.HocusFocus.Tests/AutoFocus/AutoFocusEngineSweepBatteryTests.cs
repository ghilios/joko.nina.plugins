#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus.Harness;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    /// <summary>
    /// Tier-A deterministic AutoFocus sweep battery. Unlike <c>AutoFocusEngineTests</c> (which only drives the engine to
    /// failure through all-mocked equipment) and the VM tests (which stub <c>engine.Run</c> wholesale), this fixture
    /// drives a full, SUCCESSFUL <see cref="AutoFocusEngine.Run"/> in-process: a state-backed focuser that actually
    /// moves (<see cref="MovingFocuserMediator"/>), an imaging mediator that stamps each frame with its capture-time
    /// position (<see cref="StampedImagingMediator"/> + <see cref="SweepFrameLedger"/>), and scripted detection
    /// (<see cref="ScriptedStarDetection"/>) that turns a <see cref="DegradationScenario"/> into HFR/σ/star-count per
    /// frame — no pixels rendered, fully deterministic.
    ///
    /// This file ships the harness INFRA plus the full deterministic scenario battery (C0/C1, A1–A3, B1–B3, F1/F2,
    /// AB1), built on <see cref="BuildHarness"/> and the three observables it exposes: the final position, the focuser
    /// <see cref="MovingFocuserMediator.MoveHistory"/>, and the window-excluded-vs-rejected partition on the region
    /// result. Every scenario below was hand-traced against NINA core's <c>TrendlineFitting</c> semantics (minimum =
    /// min(Y+ErrorY) over the X-ordered valid points; a point joins the left/right trend only when it lies on that
    /// side of the minimum AND <c>Y &gt; Minimum.Y + 0.1</c>), so the expected MoveHistory shapes in the comments are
    /// exact, not aspirational. Two engine facts the assertions deliberately accommodate:
    /// <list type="bullet">
    ///   <item>Zero-HFR (starless) points flow into <c>lastValidFocusPoints</c>, so far failure points legitimately
    ///   appear in <c>WindowExcludedPoints</c> with <c>Measure == 0</c>. Disjointness from <c>RejectedPoints</c> still
    ///   holds because each refit rebuilds RejectedPoints from the kept subset only.</item>
    ///   <item>The Grubbs test can numerically flag a point even on exact-hyperbola data (near-zero residual σ), so
    ///   "RejectedPoints is empty" is only asserted where <c>MaxOutlierRejections = 0</c> pins it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class AutoFocusEngineSweepBatteryTests {

        // Shared geometry for the battery scenarios (C1 keeps its own local constants + the default DefocusModel
        // baseline to prove the harness works with the simulator's real optics; everything below uses the steeper
        // analytic hyperbola from SteepScenario so the trend-walk is exactly hand-traceable).
        private const int Focus = 10000;
        private const int StepSize = 5;
        private const int OffsetSteps = 5;

        [SetUp]
        public void ResetStaticGuardBefore() {
            // AutoFocusInProgress is a process-wide static; reset it so each test starts from a known state regardless of
            // execution order (mirrors AutoFocusEngineTests).
            AutoFocusEngine.ResetAutoFocusInProgressForTests();
        }

        [TearDown]
        public void ResetStaticGuardAfter() {
            AutoFocusEngine.ResetAutoFocusInProgressForTests();
        }

        // ------------------------------------------------------------------------------------------------------------
        // C1 — Start FAR, clean symmetric curve. Proves the harness drives a full successful sweep end-to-end.
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task C1_StartFarFromFocus_CleanCurve_BootstrapsAndConvergesNearFocus() {
            const int focus = 10000;
            const int stepSize = 5;
            const int offsetSteps = 5;

            var scenario = DegradationScenario.CleanSymmetric(focus, stepSize);
            var options = DefaultSweepOptions(scenario, offsetSteps);
            // C1 tests the base bootstrap (+ the always-on symmetric window). Behavior B's reversal is exercised by the
            // cutoff scenarios; a clean far start must simply march to focus, so keep the directional cap OFF here.
            options.MaxBlindStepsPerDirection = 0;

            // Start 40 steps ABOVE focus (the seeds sit on the high side, so the walk naturally descends toward focus).
            var startPosition = focus + 40 * stepSize;
            var harness = BuildHarness(scenario, options, startPosition, focuserMin: focus - 5000, focuserMax: focus + 5000);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var result = await harness.Engine.Run(options, imagingFilter: null, token: cts.Token, progress: null);

            Assert.That(result, Is.Not.Null, "Run() must produce a result");
            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True, "a clean far-from-focus sweep must succeed");
                Assert.That(result.RegionResults, Has.Length.EqualTo(1), "region==null path yields a single full-frame region");

                var region = result.RegionResults[0];
                Assert.That(region.EstimatedFinalFocuserPosition, Is.EqualTo(focus).Within(2.0 * stepSize),
                    "the fitted vertex must land within a couple steps of true focus");

                // The three observables the rest of the battery asserts on — exercised here to prove they are populated:
                //   (1) final position  -> region.EstimatedFinalFocuserPosition (checked above)
                //   (2) reversal trace  -> the focuser was actually driven
                Assert.That(harness.Focuser.MoveHistory, Is.Not.Empty, "the focuser must have been driven through the sweep");
                //   (3) window-excluded vs rejected partition. The sweep reaches ~40 steps out, well beyond the
                //       ±(offsetSteps+0.5)*stepSize window, so Behavior A must have excluded those far points...
                Assert.That(region.WindowExcludedPoints, Is.Not.Null.And.Not.Empty,
                    "far points outside the symmetric window must be surfaced as window-excluded (Behavior A ran)");
                //       ...and window-exclusion is disjoint from Grubbs rejection by construction.
                var excludedPositions = region.WindowExcludedPoints.Select(p => p.FocuserPosition).ToHashSet();
                var rejectedPositions = (region.RejectedPoints ?? Array.Empty<AutoFocusRegionPoint>()).Select(p => p.FocuserPosition);
                Assert.That(rejectedPositions.Any(excludedPositions.Contains), Is.False,
                    "a point is never both window-excluded and Grubbs-rejected");
            });

            // The focuser was ultimately commanded to (approximately) the calculated focus point.
            Assert.That(harness.Focuser.Position, Is.EqualTo(focus).Within(2.0 * stepSize),
                "the focuser ends at the calculated focus point");
        }

        // ------------------------------------------------------------------------------------------------------------
        // C0 — Inert control. A well-centered clean sweep must be byte-identical whether the knobs are on-but-inert
        // (window enabled + cap 8, neither ever triggered) or hard-off (window disabled + cap 0). MoveHistory equality
        // is the strongest cheap byte-identity proxy: identical sampling, identical final-validation move.
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task C0_WellCentered_KnobsOnButInert_ByteIdenticalToKnobsOff() {
            // Expected walk (both runs): seeds +5s..+1s, L1 at focus, L2 at -1s (first left-trend point -> break),
            // queue-left -2s..-5s, final-validation move to 10000. Nothing is far, nothing fails, no cap pressure.
            var onScenario = SteepScenario();
            var onOptions = DefaultSweepOptions(onScenario, OffsetSteps);
            onOptions.SymmetricFocusWindowEnabled = true;
            onOptions.MaxBlindStepsPerDirection = 8;
            var onHarness = BuildHarness(onScenario, onOptions, Focus, Focus - 5000, Focus + 5000);
            var onResult = await RunSweep(onHarness, onOptions);

            var offScenario = SteepScenario();
            var offOptions = DefaultSweepOptions(offScenario, OffsetSteps);
            offOptions.SymmetricFocusWindowEnabled = false;
            offOptions.MaxBlindStepsPerDirection = 0;
            var offHarness = BuildHarness(offScenario, offOptions, Focus, Focus - 5000, Focus + 5000);
            var offResult = await RunSweep(offHarness, offOptions);

            Assert.Multiple(() => {
                Assert.That(onResult.Succeeded, Is.True, "on-but-inert run succeeds");
                Assert.That(offResult.Succeeded, Is.True, "knobs-off run succeeds");
                Assert.That(onHarness.Focuser.MoveHistory.ToArray(), Is.EqualTo(offHarness.Focuser.MoveHistory.ToArray()).AsCollection,
                    "identical focuser trajectories - the knobs must not perturb sampling or the final move");
                Assert.That(onResult.RegionResults[0].EstimatedFinalFocuserPosition,
                    Is.EqualTo(offResult.RegionResults[0].EstimatedFinalFocuserPosition),
                    "identical (bit-for-bit) fitted vertex - the window pass on an all-in-window curve is a pure no-op");
                Assert.That(onResult.RegionResults[0].WindowExcludedPoints, Is.Empty, "nothing excluded when on-but-inert");
                Assert.That(offResult.RegionResults[0].WindowExcludedPoints, Is.Empty, "nothing excluded when disabled");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // A1 — Lopsided valid far points. Start 20 steps above focus with clean detection: the descent samples a heavy
        // right arm (+25s..-5s => 31 valid points). Behavior A must surface everything outside vertex ± 6 steps as
        // WindowExcluded (never Rejected) and the windowed refit must land on true focus. The flag-off control proves
        // the exclusion comes from Behavior A alone and that sampling is untouched (identical MoveHistory).
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task A1_LopsidedFarStart_FarPointsWindowExcluded_NotRejected_FitUsesWindowOnly() {
            var start = Focus + 20 * StepSize; // 10100

            var scenario = SteepScenario();
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 0; // isolate Behavior A
            var harness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            var region = result.RegionResults[0];
            var excluded = PositionsOf(region.WindowExcludedPoints);
            var rejected = PositionsOf(region.RejectedPoints);
            // Every sampled valid point from +7 steps outward is unambiguously outside vertex ± 6*stepSize (the ± 6s
            // boundary points sit exactly ON the strict boundary, so they are deliberately left out of the assertion).
            var farPositions = PositionRange(Focus + 7 * StepSize, Focus + 25 * StepSize);

            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True, "a lopsided clean sweep succeeds");
                Assert.That(region.EstimatedFinalFocuserPosition, Is.EqualTo(Focus).Within(StepSize),
                    "the windowed refit lands on true focus");
                Assert.That(excluded, Is.SupersetOf(farPositions), "every far valid point is window-excluded");
                Assert.That(excluded.Where(p => Math.Abs(p - Focus) <= 5 * StepSize), Is.Empty,
                    "no core point (within +/-5 steps of focus) is ever window-excluded");
                Assert.That(farPositions.Intersect(rejected), Is.Empty,
                    "far points are categorized as window-excluded, never as Grubbs-rejected");
                Assert.That(excluded.Intersect(rejected), Is.Empty, "window-excluded and rejected stay disjoint");
            });

            // Flag-off control: identical sampling (windowing is finalize-only), no exclusions, same vertex.
            var controlScenario = SteepScenario();
            var controlOptions = DefaultSweepOptions(controlScenario, OffsetSteps);
            controlOptions.MaxBlindStepsPerDirection = 0;
            controlOptions.SymmetricFocusWindowEnabled = false;
            var controlHarness = BuildHarness(controlScenario, controlOptions, start, Focus - 5000, Focus + 5000);
            var controlResult = await RunSweep(controlHarness, controlOptions);

            Assert.Multiple(() => {
                Assert.That(controlResult.Succeeded, Is.True, "control run succeeds");
                Assert.That(controlResult.RegionResults[0].WindowExcludedPoints, Is.Empty,
                    "with the internal flag off, nothing is window-excluded");
                Assert.That(controlHarness.Focuser.MoveHistory.ToArray(), Is.EqualTo(harness.Focuser.MoveHistory.ToArray()).AsCollection,
                    "windowing is finalize-only: the sampled trajectory is identical with the flag on or off");
                Assert.That(controlResult.RegionResults[0].EstimatedFinalFocuserPosition, Is.EqualTo(Focus).Within(StepSize),
                    "on exact-hyperbola data the unwindowed fit finds the same vertex (the pure-data control)");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // A2 — All points in-window. A well-centered sweep samples -5s..+5s only; every point is strictly inside
        // vertex ± 6 steps, so WindowExcludedPoints must be EMPTY and the fit unaffected.
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task A2_AllPointsInWindow_WindowExcludedEmpty() {
            var scenario = SteepScenario();
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 0;
            var harness = BuildHarness(scenario, options, Focus, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            var region = result.RegionResults[0];
            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(region.WindowExcludedPoints, Is.Empty, "an all-in-window sweep excludes nothing");
                Assert.That(region.EstimatedFinalFocuserPosition, Is.EqualTo(Focus).Within(1.0),
                    "noise-free exact-hyperbola data recovers the vertex almost exactly");
                Assert.That(harness.Focuser.MoveHistory.Min(), Is.EqualTo(Focus - 5 * StepSize), "walk floor is -5 steps");
                Assert.That(harness.Focuser.MoveHistory.Max(), Is.EqualTo(Focus + 5 * StepSize), "walk ceiling is +5 steps");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // A3 — Start INSIDE a far low-HFR spurious plateau (the detector "sees" false stars in noise at and beyond the
        // start). Expected engine behavior, hand-traced:
        //   * initial HFR = 1.1 (spurious, nonzero => the initial gate passes);
        //   * seeds -13s/-12s are spurious, -11s..-9s are true; the flat plateau can never produce a left-trend point
        //     (all within TrendlineFitting's fixed 0.1 threshold of the minimum), so the walk marches LEFT deeper into
        //     the plateau until the directional cap (8 step-outs) fires -> Behavior B's step-out reversal;
        //   * the forced-right walk then descends the true curve through focus and terminates normally;
        //   * finalization: with Grubbs disabled (MaxOutlierRejections=0) the WINDOW alone must exclude the whole
        //     spurious cluster and the refit must land on true focus - the "false minimum does not hijack the center"
        //     guarantee (window centered on the fitted vertex, not the lowest raw point: the plateau IS the lowest band).
        // NOTE (engine contract, discovered by tracing): without Behavior B this walk never terminates - a flat low
        // plateau at the advancing edge can never yield the trend point the stop condition needs. A3 therefore runs
        // with the cap ON; Behavior A alone cannot rescue a walk that never completes.
        // FalseSigma is 0.4 (vs SigmaBase 0.1) so the weighted provisional fit is anchored by the true V; the false
        // plateau still wins every "lowest raw point" comparison (1.1 vs everything except the 1.0 vertex).
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task A3_StartInsideFarSpuriousPlateau_WindowExcludesFalseMinimum_FinalIsTrueFocus() {
            var start = Focus - 14 * StepSize; // 9930, inside the plateau (onset 12 steps, left side)
            var scenario = SteepScenario(s => {
                s.Spurious = new SpuriousSpec(OnsetSteps: 12, Side: FocusSide.Left, FalseHfr: 1.1, FalseStarCount: 10, FalseSigma: 0.4);
            });
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 8;   // required for termination (see note above)
            options.MaxOutlierRejections = 0;        // isolate Behavior A: no Grubbs help against the false cluster
            var harness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            var region = result.RegionResults[0];
            var excluded = PositionsOf(region.WindowExcludedPoints);
            var history = harness.Focuser.MoveHistory.ToList();
            // Sampled spurious plateau points: seeds -13s/-12s plus the 8 capped left step-outs (-14s..-21s).
            var spuriousPositions = PositionRange(Focus - 21 * StepSize, Focus - 12 * StepSize);

            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True, "the run recovers despite starting inside the false plateau");
                Assert.That(region.EstimatedFinalFocuserPosition, Is.EqualTo(Focus).Within(StepSize),
                    "the final vertex is TRUE focus, not the (lower-HFR) spurious plateau");
                Assert.That(excluded, Is.SupersetOf(spuriousPositions),
                    "every sampled spurious point is excluded by the symmetric window");
                Assert.That(excluded.Where(p => Math.Abs(p - Focus) <= 3 * StepSize), Is.Empty,
                    "the true core of the curve is never excluded");
                Assert.That(PositionsOf(region.RejectedPoints), Is.Empty,
                    "MaxOutlierRejections=0 and no starless points: the window alone did all the excluding");

                // Behavior B's step-out reversal is what terminated the plateau march: exactly 8 capped left
                // step-outs (floor = start + step - 8*step), then a return to the start, then the rightward rescue.
                Assert.That(history.Min(), Is.EqualTo(start - 7 * StepSize), "left march stopped at exactly the cap");
                var reversalIndex = history.LastIndexOf(start);
                Assert.That(reversalIndex, Is.GreaterThan(history.IndexOf(history.Min())),
                    "the return-to-start (reversal) comes after the capped plateau march");
                Assert.That(history.Max(), Is.EqualTo(Focus + 5 * StepSize), "the rescue walk tops out at the +5s queue fill");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // B1 — One-direction cutoff: the seed arm (always placed on the high side) is entirely starless because the
        // right side cuts off at exactly the start distance. All 5 seeds fail => the failure budget trips on the very
        // first walk iteration => Behavior B returns to the start and forces the LEFT direction, which descends
        // through true focus and converges. The cap-off control proves the reversal is what rescued the run: without
        // it the same geometry throws TooManyFailedMeasurements and the sweep fails without ever exploring below the
        // start.
        // Expected MoveHistory (capped run): [+5s..+1s seeds] [start = reversal return] [start = L1] [descend ... -1s]
        // [queue-left -2s..-5s] [final-validation ~focus].
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task B1_SeedsStarless_ReversalRescuesLeftward_ControlWithoutCapFails() {
            var start = Focus + 10 * StepSize; // 10050; right side starless beyond exactly 10 steps
            var scenario = SteepScenario(s => s.RightCutoffSteps = 10);
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 8;
            var harness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            var region = result.RegionResults[0];
            var history = harness.Focuser.MoveHistory.ToList();
            var excluded = PositionsOf(region.WindowExcludedPoints);

            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True, "the reversal rescues a start whose entire seed arm is starless");
                Assert.That(region.EstimatedFinalFocuserPosition, Is.EqualTo(Focus).Within(StepSize));

                // The dead (starless) direction was abandoned, not extended: nothing beyond the seed ceiling.
                Assert.That(history.Max(), Is.EqualTo(start + OffsetSteps * StepSize),
                    "the capped direction is never probed beyond the seeds");
                // Seeds first, then the reversal's return-to-start move.
                Assert.That(history.Take(OffsetSteps), Is.EqualTo(new[] { start + 5 * StepSize, start + 4 * StepSize, start + 3 * StepSize, start + 2 * StepSize, start + StepSize }).AsCollection,
                    "the five seed moves descend from start+5s to start+1s");
                Assert.That(history[OffsetSteps], Is.EqualTo(start), "the sixth move is the reversal's return to the start");
                Assert.That(history.Min(), Is.EqualTo(Focus - 5 * StepSize), "the rescue walk fills the left arm to -5 steps");

                // Partition: far valid points (+7s..+10s) window-excluded; the starless seed positions also land in
                // WindowExcluded (with Measure 0) because zero-HFR points remain in the fit-input list. Disjointness holds.
                Assert.That(excluded, Is.SupersetOf(PositionRange(Focus + 7 * StepSize, Focus + 10 * StepSize)));
                Assert.That(excluded.Intersect(PositionsOf(region.RejectedPoints)), Is.Empty);
            });

            // Control: same geometry, cap disabled => TooManyFailedMeasurements aborts the sweep; the engine never
            // explores below the start (min move = the failure restore to the start position itself).
            var controlScenario = SteepScenario(s => s.RightCutoffSteps = 10);
            var controlOptions = DefaultSweepOptions(controlScenario, OffsetSteps);
            controlOptions.MaxBlindStepsPerDirection = 0;
            var controlHarness = BuildHarness(controlScenario, controlOptions, start, Focus - 5000, Focus + 5000);
            var controlResult = await RunSweep(controlHarness, controlOptions);

            Assert.Multiple(() => {
                Assert.That(controlResult, Is.Not.Null, "the control fails gracefully, not catastrophically");
                Assert.That(controlResult.Succeeded, Is.False, "without the cap the starless seed arm kills the sweep");
                Assert.That(controlHarness.Focuser.MoveHistory.Min(), Is.GreaterThanOrEqualTo(start),
                    "without the reversal the engine never explores the good (left) side");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // B2 — Focus lies BEYOND the focuser travel limit (focus 10000, limit at 10140). A PRODUCTIVE descent (the
        // lowest measured HFR keeps improving as the walk nears focus) is NOT counted against the directional cap, so
        // the walk is not reversed mid-descent — it uses the full available travel trying to reach focus and stops at
        // the HARDWARE travel limit, failing GRACEFULLY there (a handled "focuser reached its limit", not a crash).
        // The cap bounds NON-productive excursions (wrong-way / starless — see B1/B3); a genuinely unreachable focus is
        // bounded by the travel limit, not by an arbitrary step cap.
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task B2_ProductiveDescentBoundedByTravelLimit_NotByCap_FailsGracefully() {
            var start = Focus + 40 * StepSize;       // 10200
            var focuserMin = start - 12 * StepSize;  // 10140 — focus (10000) sits below this, i.e. unreachable
            var focuserMax = Focus + 100 * StepSize;

            var scenario = SteepScenario(s => s.RightCutoffSteps = 45);
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 8;
            var harness = BuildHarness(scenario, options, start, focuserMin, focuserMax);
            var result = await RunSweep(harness, options);

            var history = harness.Focuser.MoveHistory.ToList();
            var seedFloor = start + StepSize; // 10205, the lowest seed position
            Assert.Multiple(() => {
                Assert.That(result, Is.Not.Null, "reaching the travel limit is a graceful failure, not a crash");
                Assert.That(result.Succeeded, Is.False, "focus is beyond the focuser travel limit — genuinely unreachable");
                // The productive descent is NOT cut off at the cap (8 step-outs): it walks well past that, down to the
                // travel limit, ultimately requesting a position at/below focuserMin (which trips the engine's limit guard).
                Assert.That(history.Where(p => p < seedFloor).Distinct().Count(), Is.GreaterThan(8),
                    "a productive descent is bounded by the travel limit, not the step cap");
                Assert.That(history.Min(), Is.LessThanOrEqualTo(focuserMin),
                    "the descent reaches the focuser travel limit (the real bound)");
                Assert.That(history.Max(), Is.EqualTo(start + OffsetSteps * StepSize),
                    "no reversal excursion above the seed ceiling — a productive descent never reverses");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // B3 — Mirror of B1: the LEFT side is starless just below the start, so the natural left walk piles up 5
        // failures => failure-budget reversal => the forced RIGHT walk completes the bracket and the sweep converges.
        // Expected MoveHistory: [seeds +3s..-1s] [L1 -2s] [L2..L6 -3s..-7s all starless] [reversal return to start]
        // [R +4s,+5s => break] [queue-left -8s..-10s (starless fill; failedLeft widens the target)] [final ~focus].
        // Note the queue-left dip AFTER the reversal — the engine deliberately back-fills the left arm target; the
        // reversal evidence is the post-return exploration of NEW HIGHS beyond the seed ceiling.
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task B3_LeftSideStarless_ReversalRescuesRightward_ControlWithoutCapFails() {
            var start = Focus - 2 * StepSize; // 9990; left side starless beyond exactly 2 steps
            var scenario = SteepScenario(s => s.LeftCutoffSteps = 2);
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 8;
            var harness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            var region = result.RegionResults[0];
            var history = harness.Focuser.MoveHistory.ToList();
            var seedCeiling = Focus + 3 * StepSize; // 10015, highest seed position

            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True, "the reversal rescues a start pinned against a left cutoff");
                Assert.That(region.EstimatedFinalFocuserPosition, Is.EqualTo(Focus).Within(StepSize));

                // The failing left excursion reached -7s before the failure budget tripped.
                Assert.That(history, Does.Contain(Focus - 7 * StepSize), "the left walk probed into the starless zone");
                var reversalIndex = history.LastIndexOf(start);
                Assert.That(reversalIndex, Is.GreaterThan(history.IndexOf(Focus - 7 * StepSize)),
                    "the return-to-start comes after the failed left excursion");
                Assert.That(history.Skip(reversalIndex + 1).Any(p => p > seedCeiling), Is.True,
                    "after the reversal the OTHER direction is explored beyond the seed ceiling (+4s/+5s)");

                // Every real (nonzero-HFR) point sits inside the window here; only far starless fill points are
                // window-excluded (they carry Measure == 0).
                Assert.That(region.WindowExcludedPoints.Where(p => p.Measurement.Measure > 0.0), Is.Empty,
                    "no valid star point is window-excluded in this mildly lopsided sweep");
                Assert.That(PositionsOf(region.WindowExcludedPoints).Intersect(PositionsOf(region.RejectedPoints)), Is.Empty);
            });

            // Control: cap off => the 5 left failures throw and the sweep dies without ever exploring right of the seeds.
            var controlScenario = SteepScenario(s => s.LeftCutoffSteps = 2);
            var controlOptions = DefaultSweepOptions(controlScenario, OffsetSteps);
            controlOptions.MaxBlindStepsPerDirection = 0;
            var controlHarness = BuildHarness(controlScenario, controlOptions, start, Focus - 5000, Focus + 5000);
            var controlResult = await RunSweep(controlHarness, controlOptions);

            Assert.Multiple(() => {
                Assert.That(controlResult.Succeeded, Is.False, "without the cap the left-cutoff geometry kills the sweep");
                Assert.That(controlHarness.Focuser.MoveHistory.Max(), Is.EqualTo(seedCeiling),
                    "without the reversal the engine never explores beyond the seed ceiling");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // F1 — Truly starless everywhere. Engine contract (observed, not the naive expectation): the initial-HFR gate
        // is RACY by design — OnNextAttempt() clears AnalysisTasks after the initial capture was queued, so
        // StartBlindFocusPoints' InitialHFR==0 check may or may not see the completed measurement. The run therefore
        // fails through one of two graceful paths: (a) InitialHFRFailedException before any seed move, or (b) the
        // seed/walk failure budget (with the cap on: seeds fail -> one reversal -> the other arm fails ->
        // TooManyFailedMeasurements). Both paths share the guarantees asserted here: a failed (non-null) result, no
        // fitted vertex, a BOUNDED excursion, the focuser restored, and the process-wide guard released.
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task F1_StarlessEverywhere_FailsGracefully_BoundedExcursion_GuardReleased() {
            var start = Focus + 40 * StepSize;
            var scenario = SteepScenario(s => {
                s.LeftCutoffSteps = 0;  // starless at any distance from focus on the left...
                s.RightCutoffSteps = 0; // ...and on the right => starless everywhere the sweep can look
            });
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 8;
            var harness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            Assert.That(result, Is.Not.Null, "a starless field fails gracefully, it does not crash Run()");
            var region = result.RegionResults[0];
            var history = harness.Focuser.MoveHistory.ToList();
            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(double.IsNaN(region.EstimatedFinalFocuserPosition), Is.True, "no vertex was ever fitted");
                Assert.That(region.WindowExcludedPoints, Is.Empty, "finalization was never reached");
                Assert.That(region.RejectedPoints, Is.Empty, "no fit ever ran (never 3 valid points)");
                // Path (a): only the restore move. Path (b): seeds (up to +6 steps), one reversal, and a left arm
                // bounded by the fresh failure budget (offsetSteps zero-HFR points past the start). Never a runaway.
                Assert.That(history.Max(), Is.LessThanOrEqualTo(start + (OffsetSteps + 1) * StepSize),
                    "no probing beyond the seed arm");
                Assert.That(history.Min(), Is.GreaterThanOrEqualTo(start - (OffsetSteps + 3) * StepSize),
                    "the starless walk is bounded by the failure budget - no runaway toward a focuser limit");
                Assert.That(history.Last(), Is.EqualTo(start), "the focuser is restored to the start");
                Assert.That(harness.Focuser.Position, Is.EqualTo(start), "the focuser ends where it began");
            });

            // Guard-release proof: an immediate second run must be admitted (a leaked in-progress guard returns null).
            var secondHarness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var secondResult = await RunSweep(secondHarness, options);
            Assert.That(secondResult, Is.Not.Null, "the in-progress guard was released by the failed run");
            Assert.That(secondResult.Succeeded, Is.False);
        }

        // ------------------------------------------------------------------------------------------------------------
        // F2a — Starless only far, stars near, cap comfortably larger than the start distance. The descent tolerates
        // the 3 starless outer seeds, reaches focus well inside the cap, and converges with no reversal — the far
        // starless zones are only ever touched by the seed arm.
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task F2a_StarlessOnlyFar_AmpleCap_ConvergesWithoutReversal() {
            var start = Focus + 10 * StepSize; // 10050, inside the starred band (stars within +/-12 steps of focus)
            var scenario = SteepScenario(s => {
                s.LeftCutoffSteps = 12;
                s.RightCutoffSteps = 12;
            });
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 20; // ample: the descent needs ~12 steps to reach and cross focus
            var harness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            var region = result.RegionResults[0];
            var excluded = PositionsOf(region.WindowExcludedPoints);
            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True, "3 starless outer seeds do not sink a sweep that can reach focus");
                Assert.That(region.EstimatedFinalFocuserPosition, Is.EqualTo(Focus).Within(StepSize));
                Assert.That(harness.Focuser.MoveHistory.Max(), Is.EqualTo(start + OffsetSteps * StepSize),
                    "the far starless side is only ever probed by the seeds — no reversal, no far excursion");
                Assert.That(harness.Focuser.MoveHistory.Min(), Is.EqualTo(Focus - 5 * StepSize));
                Assert.That(excluded, Is.SupersetOf(PositionRange(Focus + 7 * StepSize, Focus + 12 * StepSize)),
                    "the far valid arm (+7s..+12s) is window-excluded from the final fit");
                Assert.That(excluded.Intersect(PositionsOf(region.RejectedPoints)), Is.Empty);
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // F2b — Same geometry as F2a but with a MODEST cap (8) SMALLER than the start-to-focus distance. This is the
        // regression guard for the reported bug: a productive descent (the lowest measured HFR keeps improving as the
        // walk nears focus) must NOT be counted against the directional cap, so a far-but-recoverable start is not
        // abandoned just because it needs more than `cap` steps to reach focus. The walk reaches focus and converges,
        // exactly like F2a — the cap only bounds NON-productive excursions (see the B-scenarios).
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task F2b_StarlessOnlyFar_ModestCap_ProductiveDescentNotCapped_Converges() {
            var start = Focus + 10 * StepSize;
            var scenario = SteepScenario(s => {
                s.LeftCutoffSteps = 12;
                s.RightCutoffSteps = 12;
            });
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 8; // smaller than the ~10 steps to focus — but a productive descent isn't capped
            var harness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            var history = harness.Focuser.MoveHistory.ToList();
            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True,
                    "a productive descent toward a reachable focus is not cut off by a modest cap");
                Assert.That(result.RegionResults[0].EstimatedFinalFocuserPosition, Is.EqualTo(Focus).Within(StepSize));
                Assert.That(history.Max(), Is.EqualTo(start + OffsetSteps * StepSize),
                    "no reversal: the far starless side is only ever probed by the seeds");
                Assert.That(history.Min(), Is.EqualTo(Focus - 5 * StepSize), "the descent reaches and brackets focus");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // AB1 — Combined worst case: a far low-HFR spurious plateau (+12s..+16s at HFR 1.1, star-count 10) capped by a
        // starless cutoff beyond +16s, start on the clean side at +10s, production-like options (Grubbs enabled,
        // cap 8). Hand-traced composition:
        //   * the spurious seed cluster fakes a minimum whose left trend fills from TRUE points => the walk turns
        //     right INTO the plateau, hits the cutoff, piles up 5 failures => Behavior B reverses (failure budget);
        //   * the forced-left walk descends through true focus and terminates normally;
        //   * finalization: the false cluster (the lowest raw band on the curve!) is neutralized — window exclusion
        //     (plus at most MaxOutlierRejections Grubbs picks on the provisional passes, which each refit re-derives)
        //     leaves a clean core fit whose vertex is TRUE focus.
        // The spurious cluster reports an inflated σ (0.4 vs the baseline 0.1) — the realistic signature of stars
        // imagined in noise. The equal-σ variant of the same geometry is AB2 below: there the engine's real behavior
        // is a fail-SAFE (the poisoned fit fails validation), never a wrong focus position.
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task AB1_FarFalseMinimumPlusCutoff_ReversalAndWindowRecoverTrueFocus() {
            var start = Focus + 10 * StepSize; // 10050
            var scenario = SteepScenario(s => {
                s.Spurious = new SpuriousSpec(OnsetSteps: 12, Side: FocusSide.Right, FalseHfr: 1.1, FalseStarCount: 10, FalseSigma: 0.4);
                s.RightCutoffSteps = 16;
            });
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 8;
            var harness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            var region = result.RegionResults[0];
            var excluded = PositionsOf(region.WindowExcludedPoints);
            var rejected = PositionsOf(region.RejectedPoints);
            var history = harness.Focuser.MoveHistory.ToList();
            // Sampled spurious plateau: seeds +12s..+15s plus the one right step-out to +16s.
            var spuriousPositions = PositionRange(Focus + 12 * StepSize, Focus + 16 * StepSize);

            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.True, "the combined worst case still converges");
                Assert.That(region.EstimatedFinalFocuserPosition, Is.EqualTo(Focus).Within(StepSize),
                    "the final vertex is TRUE focus — the low-HFR false cluster does not capture the fit");

                // Every sampled spurious point was kept OUT of the final fit, and the two exclusion categories
                // remain disjoint (window-excluded points are never re-labeled as rejected, or vice versa).
                Assert.That(spuriousPositions.Except(excluded.Union(rejected)), Is.Empty,
                    "every spurious point is neutralized via window-exclusion (or a Grubbs pick on a provisional pass)");
                Assert.That(excluded.Intersect(rejected), Is.Empty);
                Assert.That(excluded.Where(p => Math.Abs(p - Focus) <= 3 * StepSize), Is.Empty,
                    "the true core is never excluded");

                // Behavior B's failure-budget reversal: the rightward probe dies at the cutoff (+17s..+21s all
                // starless), the engine returns to the start, and the rescue happens leftward through focus.
                Assert.That(history.Max(), Is.EqualTo(Focus + 21 * StepSize),
                    "the rightward march is bounded by the failure budget beyond the cutoff");
                var reversalIndex = history.LastIndexOf(start);
                Assert.That(reversalIndex, Is.GreaterThan(history.IndexOf(history.Max())),
                    "the return-to-start comes after the failed rightward march");
                Assert.That(history.Min(), Is.EqualTo(Focus - 5 * StepSize), "the rescue fills the left arm to -5 steps");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // AB2 — AB1's geometry with an EQUAL-σ false plateau (σ 0.1, indistinguishable from real measurements). This
        // is beyond what the finalization window can repair: the weighted hyperbolic fit itself is captured/broken by
        // the plateau (alglib fails to produce a usable model around it), so finalization cannot fit a vertex and the
        // run FAILS — the engine's real, asserted contract here is fail-SAFE: it never reports a focus position from
        // the poisoned curve, and it restores the focuser. (Discovered while tuning: with equal σ the provisional fit
        // is dragged toward the plateau, the windowed refit around that hijacked center has no left arm, and the
        // hyperbolic solve fails => FitQuality failure. A wrong ANSWER never escapes.)
        // ------------------------------------------------------------------------------------------------------------
        [Test]
        public async Task AB2_EqualSigmaFalsePlateau_FailsSafe_NeverReportsFalseFocus() {
            var start = Focus + 10 * StepSize;
            var scenario = SteepScenario(s => {
                s.Spurious = new SpuriousSpec(OnsetSteps: 12, Side: FocusSide.Right, FalseHfr: 1.1, FalseStarCount: 10, FalseSigma: 0.1);
                s.RightCutoffSteps = 16;
            });
            var options = DefaultSweepOptions(scenario, OffsetSteps);
            options.MaxBlindStepsPerDirection = 8;
            var harness = BuildHarness(scenario, options, start, Focus - 5000, Focus + 5000);
            var result = await RunSweep(harness, options);

            Assert.Multiple(() => {
                Assert.That(result.Succeeded, Is.False,
                    "an equal-σ false plateau breaks the fit and the run fails validation — fail-safe, not fail-wrong");
                Assert.That(double.IsNaN(result.RegionResults[0].EstimatedFinalFocuserPosition), Is.True,
                    "no vertex is ever reported from the poisoned curve");
                Assert.That(harness.Focuser.Position, Is.EqualTo(start),
                    "the focuser is restored to the start — it never settles in the false-minimum band");
            });
        }

        // ------------------------------------------------------------------------------------------------------------
        // Reusable harness construction — the battery builds every scenario on top of these helpers.
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A steep analytic hyperbola baseline: HFR(pos) = sqrt(1 + (0.12·(pos − focus))²), i.e. a 1.0 px floor rising
        /// 0.6 px per 5-unit step. Chosen so EVERY point one or more steps off the vertex clears NINA core
        /// <c>TrendlineFitting</c>'s fixed <c>Minimum.Y + 0.1</c> trend threshold (HFR at ±1 step = 1.166 vs threshold
        /// 1.1) — with the simulator's default optics the near-focus CFZ is flat, trend membership goes fuzzy, and the
        /// walk over-samples unpredictably. With this curve (and zero noise) every scenario's walk, and therefore its
        /// MoveHistory, is exactly derivable by hand. It is still a pure hyperbola, so a noise-free HYPERBOLIC fit
        /// recovers the vertex exactly.
        /// </summary>
        private static DegradationScenario SteepScenario(Action<DegradationScenario> configure = null) {
            var scenario = new DegradationScenario(Focus, StepSize) {
                BaselineHfr = pos => Math.Sqrt(1.0 + Math.Pow(0.12 * (pos - Focus), 2.0))
            };
            configure?.Invoke(scenario);
            return scenario;
        }

        private static async Task<AutoFocusResult> RunSweep(SweepHarness harness, AutoFocusEngineOptions options) {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var result = await harness.Engine.Run(options, imagingFilter: null, token: cts.Token, progress: null);
            Assert.That(result, Is.Not.Null, "Run() must always produce a result (a null means the in-progress guard rejected the run)");
            return result;
        }

        private static int[] PositionsOf(AutoFocusRegionPoint[] points) {
            return (points ?? Array.Empty<AutoFocusRegionPoint>()).Select(p => p.FocuserPosition).OrderBy(p => p).ToArray();
        }

        /// <summary>Every focuser grid position from <paramref name="lowInclusive"/> to <paramref name="highInclusive"/> in <see cref="StepSize"/> increments.</summary>
        private static int[] PositionRange(int lowInclusive, int highInclusive) {
            return Enumerable.Range(0, (highInclusive - lowInclusive) / StepSize + 1).Select(i => lowInclusive + i * StepSize).ToArray();
        }

        internal sealed class SweepHarness {
            public AutoFocusEngine Engine { get; init; }
            public MovingFocuserMediator Focuser { get; init; }
            public ScriptedStarDetection Detection { get; init; }
            public SweepFrameLedger Ledger { get; init; }
            public DegradationScenario Scenario { get; init; }
        }

        /// <summary>
        /// A fully-specified <see cref="AutoFocusEngineOptions"/> tuned for deterministic in-process sweeps:
        /// single-threaded (MaxConcurrent=1), one frame per point, STARHFR + HYPERBOLIC, no saving, a generous timeout,
        /// HFR-improvement validation on, and the always-on symmetric window enabled. Behavior B (the directional cap)
        /// is left OFF by default; scenarios that exercise the reversal set <see cref="AutoFocusEngineOptions.MaxBlindStepsPerDirection"/>.
        /// </summary>
        internal static AutoFocusEngineOptions DefaultSweepOptions(DegradationScenario scenario, int offsetSteps) {
            return new AutoFocusEngineOptions {
                DebayerImage = false,
                NumberOfAFStars = 0,
                TotalNumberOfAttempts = 1,
                ValidateHfrImprovement = true,
                AutoFocusMethod = AFMethodEnum.STARHFR,
                AutoFocusCurveFitting = AFCurveFittingEnum.HYPERBOLIC,
                AutoFocusInitialOffsetSteps = offsetSteps,
                AutoFocusStepSize = scenario.StepSize,
                FramesPerPoint = 1,
                MaxConcurrent = 1,
                Save = false,
                SavePath = null,
                AutoFocusTimeout = TimeSpan.FromMinutes(5),
                HFRImprovementThreshold = 0.1,
                FocuserOffset = 0,
                MaxBlindStepsPerDirection = 0,
                MaxOutlierRejections = 3,
                OutlierRejectionConfidence = 0.95,
                WeightedHyperbolicFitEnabled = true,
                HyperbolicFitModel = HyperbolicFitModel.Symmetric,
                FitRejectionCriterion = FitRejectionCriterion.RSquared,
                ReducedChiSquaredRejectionThreshold = 0.0,
                SymmetricFocusWindowEnabled = true
            };
        }

        /// <summary>
        /// Wires an <see cref="AutoFocusEngine"/> for a deterministic sweep: a <see cref="MovingFocuserMediator"/> at
        /// <paramref name="startPosition"/> clamped to <c>[<paramref name="focuserMin"/>, <paramref name="focuserMax"/>]</c>,
        /// a <see cref="StampedImagingMediator"/> + <see cref="SweepFrameLedger"/> feeding a
        /// <see cref="ScriptedStarDetection"/> for <paramref name="scenario"/>, and a profile whose focuser settings match
        /// <paramref name="options"/> (STARHFR + HYPERBOLIC — both the DTO and the profile must agree, since finalization
        /// reads the profile). Everything else is a bare NSubstitute.
        /// </summary>
        internal static SweepHarness BuildHarness(DegradationScenario scenario, AutoFocusEngineOptions options, int startPosition, int focuserMin, int focuserMax) {
            var focuser = new MovingFocuserMediator(startPosition, focuserMin, focuserMax);
            var ledger = new SweepFrameLedger();
            var imaging = new StampedImagingMediator(focuser, ledger);
            var detection = new ScriptedStarDetection(ledger, scenario);

            var starDetectionSelector = Substitute.For<IPluggableBehaviorSelector<IStarDetection>>();
            starDetectionSelector.GetBehavior().Returns(detection);
            starDetectionSelector.SelectedBehavior.Returns(detection);

            // The engine reads cameraMediator.GetInfo() in IsSubSampleEnabled/GetSubSampleRectangle; a bare substitute
            // returns null there. A connected camera that CANNOT sub-sample keeps the full-frame region==null path.
            var cameraMediator = Substitute.For<ICameraMediator>();
            cameraMediator.GetInfo().Returns(new CameraInfo { Connected = true, CanSubSample = false });

            var profileService = Substitute.For<IProfileService>();
            // Finalization (ValidateCalculatedFocusPosition) reads the fitting/method off the PROFILE, so it must agree
            // with the DTO. R²/χ² thresholds default to 0 ⇒ no fit-quality rejection of a clean fit.
            profileService.ActiveProfile.FocuserSettings.AutoFocusMethod.Returns(options.AutoFocusMethod);
            profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting.Returns(options.AutoFocusCurveFitting);

            var engine = new AutoFocusEngine(
                profileService: profileService,
                cameraMediator: cameraMediator,
                filterWheelMediator: Substitute.For<IFilterWheelMediator>(),
                focuserMediator: focuser,
                guiderMediator: Substitute.For<IGuiderMediator>(),
                imagingMediator: imaging,
                imageDataFactory: Substitute.For<IImageDataFactory>(),
                starDetectionSelector: starDetectionSelector,
                starAnnotatorSelector: Substitute.For<IPluggableBehaviorSelector<IStarAnnotator>>(),
                autoFocusOptions: Substitute.For<IAutoFocusOptions>(),
                starAnnotatorOptions: Substitute.For<IStarAnnotatorOptions>(),
                alglibAPI: new AlglibAPI());

            return new SweepHarness {
                Engine = engine,
                Focuser = focuser,
                Detection = detection,
                Ledger = ledger,
                Scenario = scenario
            };
        }
    }
}
