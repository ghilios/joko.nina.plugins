# GPU (CUDA) Star-Detection Optimization Feasibility Spike

## Context

The dev machine now has an NVIDIA RTX 5060 Ti (Blackwell, 16 GB VRAM, CUDA 13.4 driver). Verify whether GPU
acceleration can greatly speed up **star detection optimization** — the `TestApp optimize` harness driving
`StarDetectionOptimizer` over saved autofocus runs. Intended usage shape: *transfer a saved AF run to a GPU
machine and optimize there* (this hardware won't be on typical imaging rigs). **Feasibility only** — no
user-facing knobs; prove it in TestApp before touching plugin behavior. Findings land in a results doc
whether positive or negative.

**Why plausible despite prior negative verdicts:** repo docs rejected OpenCL/CUDA before, but on end-user
deployment grounds (removed by this usage shape) and "memory-bandwidth-bound" grounds that argue against
wider CPU SIMD, not GPU — the 5060 Ti has ~448 GB/s VRAM vs ~80–100 GB/s system DRAM, and frames stay
VRAM-resident. At default budget (250 evals), EARLY context rebuilds dominate wall on heavy runs (early
build ≈1.65 s/frame at 61 MP vs ~53 ms late gate; bank of 16 runs ≈ 4085 s; CWhite 61 MP ≈ 545 s/run).
The EARLY pipeline (median filter, Gaussians, à-trous wavelet, K-σ, histogram stats, local grids, binarize)
is GPU-shaped; the histogram stats scan is single-threaded today.

**Honest ceiling:** per-run E2E ≈ `1/((1−f) + f/s)` with f = early fraction of wall, s = per-build speedup.
Estimated s ≈ 3–6× (GPU chain ~0.25–0.5 s vs 1.65 s at 61 MP — floor set by the CPU-only
`CollectStarCandidates` flood-fill and transfers). With assumed f = 0.5–0.75 at budget 250, expected E2E is
**1.6–2.7× on heavy runs, ~1.0–1.1× on late-dominated runs** — the ≥3× bar needs f ≥ 0.75 and s ≥ 5.
Step 0 replaces assumptions with measurements **before any kernel is written**, and can kill the spike cheap.

## Decisions (user-confirmed)

1. **Equivalence**: tolerance-validated, not bit-identical. Quantify divergence (structure-map max-abs-diff,
   binarized flip count, candidate-set symmetric difference, per-star HFR deltas, J + landed-params drift).
   Production shape: **GPU-accelerated search, CPU final confirm of landed settings** (precedent: the
   wavelet swap shipped 3e-8 diffs behind a `StarDetectorVersion` bump).
2. **Workload**: `TestApp optimize` on bank runs (bank-verify batch noted as even more GPU-shaped; not in scope).
3. **Go/no-go**: ≥3× end-to-end optimize wall on early-rebuild-heavy runs, else write up the negative result.
4. **CPU-baseline honesty**: also implement the documented cheap CPU lever ("I1": parallelize the
   single-threaded 65536-bin `CalculateStatistics_Histogram`) — bit-identical, shippable regardless — so the
   verdict is "GPU vs best-available CPU".

## Verified facts (exploration + plan agents, cross-checked against code)

- Early/late split at `StarDetector.cs:406/:433`; the GPU-able span is **:520 (noise reduction) → :646
  (BinarizationStatistics + adaptive grids) + the K-σ awaits (:648–652)**. Everything from
  `StructureDilation` (:663) onward — dilation, binarize, inner-crop, donut-close,
  `CollectStarCandidates` (:718, inherently serial) — stays on existing CPU code. LATE stage stays CPU.
- `LocallyAdaptiveBinarization` default **true** → the two `ComputeLocalBackgroundGrid` calls are in scope
  (sigmaGrid on the noise-reduced image inside the K-σ task at :570–573; adaptiveMedianGrid on the blurred
  structure map at :642–645).
- Exactly-reproducible on GPU (order statistics / double-precision bucketing): histogram median, local
  grids (median/MAD), median3x3, binarize compare. Genuine fp32 divergence confined to Gaussians, wavelet
  passes, K-σ reductions (~1e-7 relative; CPU itself is SIMD-order-dependent — `--cv-threads` exists for
  that reason).
- The `MultiStopWatch` "Binarization" bucket hides the K-σ await stall (awaits at :648–652 land between the
  `BinarizationStatistics` and `Binarization` RecordEntry calls) — Step 0 must account for this.
- **ILGPU 1.5.3** (NuGet) is the minimum for Blackwell: ≤1.5.1 PTX-JIT-fails on RTX 50 (issue #1342, fixed
  in 1.5.3); LibDevice/XMath transcendentals broken on compute_100+ until unreleased 1.6.0 — **avoid XMath**
  (pipeline needs only add/mul/compare/abs/sqrt). Fallback: **ComputeSharp 3.x** (D3D12, no sm_120 dependency).
- TestApp is a Windows exe → Windows CUDA driver (WSL CUDA never involved). Build/run via `cmd.exe /c` interop.
- `InternalsVisibleTo("TestApp")` exists; bench-subcommand pattern: `BenchWaveletRunner.cs` / `bench-simrender`.
- Bank data at `D:\Autofocus Bank` (22 runs; muggsie small mono, CWhiteFocus 61 MP, Panos labeled, timmer bayered).

## Steps

Branch: `ghilios/gpu-early-pipeline-spike` off `develop`. First action on the branch: commit this plan as
`plans/gpu-early-pipeline-spike-plan.md` (repo convention; specs/results go in `docs/`).

### Step 0 — Re-profile EARLY post-AtrousWaveletFast (~0.5 day) — no kernels until this passes

The 44%/21%/22% stage table is stale (pre-wavelet-swap). Refresh it and pin f.

- Build: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"`.
- Per-stage timings via `TestApp.exe contamination --image <frame> --out C:\temp\gpu-spike\profile\<name>`
  (one full Detect with TRACE; no optimizer state) on 3 frames: muggsie (small mono), CWhiteFocus (61 MP),
  timmer (bayered); 3 reps, medians, from the `MultiStopWatch` lines in the newest NINA log
  (`/mnt/c/Users/ghili/AppData/Local/NINA/Logs`). If the "Binarization" bucket is large, add 2 throwaway
  `RecordEntry` calls around the K-σ awaits (spike-only diagnostic). Record `CollectStarCandidates` ms — it
  is the GPU-build floor. Capture K-σ `NumIterations` from the trace lines.
- Pin f on the 4 Step-4 runs: one pinned sequential optimize each
  (`optimize --runs "D:\Autofocus Bank\<run>" --per-run --max-evals 250 --settings <pinned> --profile-id
  <guid> --verbose --out ...`; snapshot one pinned `harness_settings.json` at spike start and reuse it for
  every arm — both pins, every time, per `.claude/docs/testapp-cli.md`).
  f ≈ ContextBuilds × median(early-build wall) / total wall; cross-check `SecondsPerExpensiveEvaluation`.
- **Gate G0:** refreshed stage table + measured f. If `1/(1−f) < 3` on ALL heavy runs, the user's ≥3× bar is
  unreachable — stop and report (recommending the CPU I1 item + possibly the bank-verify workload instead).

### Step 1 — GPU stack sanity on Blackwell (~0.5–1 day)

ILGPU **1.5.3** added to **TestApp.csproj only**. New `TestApp/Gpu/GpuDevice.cs` (lazy `Context.Create(b =>
b.Cuda())` singleton, device banner, typed "GPU unavailable" result, ProcessExit disposal) +
`TestApp/Gpu/BenchGpuRunner.cs` + a `bench-gpu` dispatch case in `Program.cs` (mirror `bench-wavelet`).

`bench-gpu --sanity` (Release build, like other benches): device banner (expect CC 12.0); saxpy on 64 M
floats verified vs CPU; one micro-kernel per math primitive used (abs/min/max/sqrt/double-multiply-floor —
the LibDevice-avoidance check); D2D bandwidth on 256 MB; H2D/D2H pageable + page-locked on 245 MB; empty
kernel-launch latency.

- **Gate G1:** saxpy correct, D2D ≥ 300 GB/s, pinned H2D/D2H ≥ 10 GB/s, no PTX JIT errors. On failure: try
  ILGPU 1.6-preview, else switch to ComputeSharp and re-run the same runner.

### Step 2 — Standalone EARLY-pipeline micro-benchmark (~3–4 days)

`TestApp/Gpu/GpuEarlyKernels.cs`: median3x3 (+ fused hotpixel-threshold variant matching
`HotpixelFiltering.cs` semantics, BORDER_REPLICATE); separable Gaussian (same taps as OpenCV
`getGaussianKernel`, reflect border); à-trous B3 5-tap dilated separable per layer + fused
residual-subtract-clamp (port `AtrousWaveletFast` tap/border logic); K-σ clipped mean/σ two-stage reduction
with fp64 accumulators, ≤5 iterations with host convergence check; 65536-bin histogram (shared-mem block
hists + atomic merge; double-precision bucketing ⇒ bit-exact median); local-background grid exact
median/MAD via bucketed selection (**decision point**: if Step 0 shows grids <5% of build, keep them on CPU
on downloaded intermediates and skip the kernel); threshold/binarize. NOT built: dilation/donut-close
kernels (default-off params; such builds fall back to CPU).

`bench-gpu --early --image <frame> --iters 5` (per-stage GPU vs in-process CPU oracle calls),
`--resident-repeats 10` (upload once, run chain N×, download outputs each time — the optimizer's shape),
`--compare` (divergence: pre-binarize max-abs-diff, binarized flip count, K-σ/median deltas — grids and
histogram median expected exactly 0). Also implement + measure the **CPU I1 baseline** (parallel per-row
histograms, bit-identical) here.

- **Gate G2:** at 61 MP, GPU whole-early-chain incl. transfers + measured `CollectStarCandidates` CPU floor
  ≥ **3×** vs the Step-0 CPU build (target 5×); binarized flips < 0.01% of pixels at seed params; σ
  rel-delta < 1e-5. At 2–3×: judgment call against measured f. Below 2×: write the negative doc, stop.

### Step 3 — Integrate behind the seam (~2–3 days)

Seam is **inside `BuildDetectionContextInternal`**, not at `ISplitFrameDetector` (don't duplicate the
ownership logic). Plugin side (internal, inert when unset):

- `StarDetection/EarlyPipelineAccelerator.cs`: `internal interface IEarlyPipelineAccelerator {
  bool TryRunEarlySpan(Mat srcImage, StarDetectorParams p, int effectiveStructureLayers,
  bool hotpixelAlreadyApplied, out EarlySpanOutput output); }` where `EarlySpanOutput` = post-blur
  structure-map Mat + median, structure/measurement K-σ results, sigma/adaptive-median grids,
  hotpixel count; `true` ⇒ srcImage mutated in place (hotpixel+NR applied); `false` ⇒ existing CPU span
  runs unchanged (per-build graceful fallback; counter incremented). Returns false for
  `SaveIntermediateFilesPath != ""`, `StoreStructureMap`, or unsupported combos.
- `StarDetector`: `internal static IEarlyPipelineAccelerator EarlyAcceleratorOverride;` consulted after
  ROI/binning (:507), replacing :520–:652; CPU code from :663 onward untouched.

TestApp side: `Gpu/GpuEarlyPipeline.cs : IEarlyPipelineAccelerator` composed from Step-2 kernels; `--gpu`
flag on `optimize` sets the override before runs load, prints device banner + fallback summary; GPU init
failure ⇒ warn + CPU run. Design decisions: **upload prepared srcImage per build** (10–20 ms vs ≥1.65 s —
skip cache-invalidation complexity; revisit only if transfers >15% of GPU build); pool **2** GPU working
sets (~2.5 GB at 61 MP) behind `SemaphoreSlim(2)` with one `CudaStream` each (frame fan-out up to 12
serializes on it — acceptable); outputs returned as ordinary CPU Mats so context ownership is untouched;
CPU-final-confirm runs as a separate process without `--gpu` (no in-process backend mixing).

- **Gate G3:** `optimize --runs "D:\Autofocus Bank\muggsie" --gpu --max-evals 50` completes, star counts/J
  within a few percent of the CPU arm, zero unexplained fallbacks, and the full unit suite green
  (`cmd.exe /c "dotnet test ..."`, timeout 600000 — hook is null in tests so bit-identity tests must pass
  untouched; known-flaky: `SendAsync_WritesOnABackgroundThread`).

### Step 4 — End-to-end measurement + equivalence report (~1.5–2 days)

Runs: muggsie, CWhiteFocus, Panos (`--labels`), timmer (bayered; CFA/debayer deliberately CPU — say so in
the doc rather than letting it look like a GPU failure). Protocol: sequential, both arms pinned with the
same `--settings` + `--profile-id`, `--max-evals 250`, 2 repeats, same build config. Collect: wall,
ContextBuilds/Reuses, cheap/expensive per-eval seconds, finalJ, trajectory overlay, landed-params diff,
fallback count. Divergence: `bench-gpu --compare` at seed AND at each arm's landed params on every frame;
candidate-set symmetric difference; matched per-star HFR deltas via `GateAndMeasure` against both contexts.
**CPU final confirm:** fresh process without `--gpu` evaluating the GPU arm's landed settings
(`--settings` shim or a small `bench-gpu --confirm` mode) → accept if J_cpu(landed_gpu) within 0.5% of
J_cpu(landed_cpu) (the bar the budget work used).

**Deliverable:** `docs/gpu-star-detection-optimization-results.md` — refreshed stage table, sanity numbers,
per-kernel table, E2E table (wall/J/drift/divergence per run, including the CPU-I1 baseline column),
CPU-confirm outcome, ship/no-ship recommendation with production-integration sketch — positive or negative.

## Risks

1. ILGPU/Blackwell: 1.5.3 minimum; XMath/LibDevice broken until 1.6.0 (avoid); scattered CUDA-13 PTX-JIT
   reports — Step 1 retires all three cheaply; ComputeSharp is the fallback.
2. fp32 flips at threshold boundaries → different candidate sets: quantified (flip counts, symmetric
   diffs), not assumed; framed against the accepted 3e-8 CPU wavelet-swap precedent.
3. GPU semaphore serializing 12-way frame fan-out while LATE still wants the CPU — watch idle-wait in arm B
   (`FrameParallelismOverride` is settable in-memory if contention shows).
4. Measurement discipline: Release for benches, warmup reps, both pins on every optimize arm, no fan-out.
5. `dotnet test`/builds deploy the plugin into NINA's folder (PostBuild xcopy) — close NINA when building.

## Effort: ~8–10 working days total, with kill-gates at G0 (free), G1 (~1 day in), G2 (~5 days in)

## Critical files

- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` (:433–756 span; hook site)
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/CvImageUtility.cs` (K-σ :571, histogram :314,
  grids :662, Gaussian — the CPU oracles kernels must match) and `Utility/AtrousWaveletFast.cs` (:50/:86)
- `Joko.NINA.Plugins/TestApp/Program.cs`, `TestApp/BenchWaveletRunner.cs` (pattern),
  `TestApp/OptimizationDiagnosticRunner.cs` (wiring + instrumentation)
- New: `TestApp/Gpu/{GpuDevice,BenchGpuRunner,GpuEarlyKernels,GpuEarlyPipeline}.cs`,
  `StarDetection/EarlyPipelineAccelerator.cs`

## Verification

- Gates G0–G3 above; final E2E table vs the ≥3×-on-heavy-runs bar; CPU-confirm ΔJ ≤ 0.5%.
- Full unit suite at milestones + final gate (Windows dotnet.exe, timeout 600000).
- Everything measured lands in `docs/gpu-star-detection-optimization-results.md` on the spike branch;
  commits with the privacy email; no push to develop.
