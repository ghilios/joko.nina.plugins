# GPU (CUDA) Star-Detection Optimization — Feasibility Spike Results

**Status:** Spike complete (branch `ghilios/gpu-early-pipeline-spike`; plan:
`plans/gpu-early-pipeline-spike-plan.md`). Hardware: RTX 5060 Ti 16 GB (Blackwell, SM_120) in the 48-core
Threadripper 2970WX dev box (PCIe **Gen3** — this matters, see transfers). Software: ILGPU 1.5.3, .NET 8,
TestApp Debug configuration throughout (matching every historical harness number).

**Question answered:** can CUDA greatly speed up `TestApp optimize` (the star-detection optimization
harness/wizard) by accelerating the EARLY detection pipeline (`BuildDetectionContext` steps 1–5b), with the
flood-fill candidate collection and the LATE per-star stage staying on CPU? Go/no-go bar: ≥3× end-to-end on
early-rebuild-heavy runs, tolerance-validated equivalence, CPU-confirmable landed settings.

## TL;DR

1. **ILGPU 1.5.3 works on Blackwell/SM_120 out of the box** — driver-JIT PTX, no CUDA toolkit install.
   Kernels avoiding XMath/LibDevice (all this pipeline needs) compile and run correctly. Device: 374 GB/s
   D2D; 7.2 GB/s pinned PCIe (Gen3 platform); 16–28 µs launch latency.
2. **The GPU early chain is 11.9× faster than the current CPU chain at 61 MP** (301 ms vs 3573 ms,
   transfers included), and still **7.9× vs best-available CPU** (with the I1 parallel-histogram fix
   applied to the CPU side). Per-stage: Gaussians ~68×, K-σ ~64×, wavelet ~19×, histogram stats ~30×,
   local-background grids ~65× (exact radix-select).
3. **Equivalence is better than expected.** On muggsie + CWhite frames: the prepared measurement image is
   **bit-identical** (median filter is an order statistic), hotpixel counts EXACT, and the **binarized
   structure map has ZERO flipped pixels** — the candidate-set decision surface is unchanged. Residual
   divergence (structure map ≤1.8e-7 max-abs; K-σ σ ≤1.7e-7 rel; grids ≤6e-9) is FMA-contraction noise of
   the same class as the accepted `AtrousWaveletFast` swap (3e-8, shipped behind a `StarDetectorVersion`
   bump).
4. **End-to-end `optimize --gpu`**: TBD (§4, matrix in flight). Ceiling analysis from measured
   early-fractions: Panos-like labeled runs can exceed 3×; muggsie/CWhite-class runs cap near ~2.9×
   (Amdahl: late stage + CPU flood-fill floor); bayered runs cap near ~1.9× (CPU CFA/debayer per build,
   out of GPU scope).

## 0. Re-profiled EARLY stage split (post-AtrousWaveletFast — the stale-table refresh)

Single-frame `contamination` runs (current profile settings, medians of 3):

| Stage (ms) | muggsie 11.7 MP | CWhite 61 MP | timmer 26 MP bayered* |
|---|---|---|---|
| SrcImagePreparation | 118 | 420 | ~0* |
| StructureMapPreparation | 82 | 400 | 190 |
| WaveletCalculation | 79 | 390 | 150 |
| PostWaveletConvolution | 27 | 140 | 60 |
| BinarizationStatistics (hist + adaptive grid) | **560** | **1460** | **620** |
| Binarization | 54 | 150 | 100 |
| CollectStarCandidates (CPU floor) | 155 | 810 | 400 |
| **Early total** | **1050** | **3760** | **1510** (+ ~1.2 s CFA+debayer at native) |

The old "wavelet 44% / stats 21%" table is dead: post-swap, **BinarizationStatistics is the dominant CPU
stage** (39–53% of the early build), and the single-threaded 65536-bin histogram inside it is the single
largest line item. The wavelet is now ~8–10%.

### Early fraction f of optimize wall (pinned, sequential, `--max-evals 250`, `--verbose`)

| Run | wall | builds | reuse | early stage-time | late stage-time | f | pure ceiling 1/(1−f) | hybrid ceiling† |
|---|---|---|---|---|---|---|---|---|
| muggsie | 68.8 s | 207 | 90.8% | 417 s | 124 s | 0.77 | 4.3× | ~2.9× |
| CWhiteFocus | 526 s | 333 | 85.2% | 3197 s | 959 s | 0.77 | 4.3× | ~2.9× |
| Panos (labeled) | 104.8 s | 161 | 89.0% | 515 s | 40 s | **0.93** | 14× | **~5.3×** |
| timmer (bayered) | 566 s | 540 | 76.0% | 2213 s | 1487 s | 0.60 | 2.5× | ~1.9× |

† hybrid ceiling = GPU span → 0 but `CollectStarCandidates` (13–22% of the early build) and the LATE stage
stay CPU. **Gate G0 passed**: ≥3× reachable on early-heavy runs.

The "99% cache reuse" folklore is about *detection counts*, not seconds: an early build costs ~30–70× a
late gate, so 9–24% build-rate = 60–93% of wall.

## 1. Blackwell/ILGPU sanity (`TestApp bench-gpu --sanity`)

- RTX 5060 Ti reports **CC SM_120**; ILGPU 1.5.3 JITs and runs (1.5.1 and older fail per ILGPU #1342).
  XMath/LibDevice transcendentals avoided (broken on compute_100+ until ILGPU 1.6) — the early pipeline
  needs only add/mul/compare/abs/min/max/sqrt/floor, all PTX-native.
- saxpy on 64M floats verified: **0 real mismatches**; 12.7M elements differ from the CPU value only by
  **ptxas FMA contraction** (fused `a*x+y`, single rounding — matches `MathF.FusedMultiplyAdd` exactly).
  This is the entire source of GPU float divergence in the spike.
- D2D 374 GB/s; H2D/D2H page-locked 7.2/7.1 GB/s (**PCIe Gen3 platform** — a Gen4/5 box would halve
  transfer cost); pageable 7.0/4.6. Empty-kernel launch 21 µs synced / 16 µs streamed (chain does ~40
  launches ⇒ launch overhead ~1 ms, negligible).
- Double-precision histogram bucketing (`(int)floor((double)v*65536)`) verified EXACT on GPU.

## 2. Early-chain micro-benchmark (`bench-gpu --early`, 61 MP CWhite frame, medians of 3)

| Stage | CPU ms | GPU ms | speedup |
|---|---|---|---|
| H2D upload (245 MB incl. staging memcpy) | (clone 111) | 56 | — |
| SrcImagePreparation (median3x3 hotpixel + copies) | 203 | 3.0 | 68× |
| StructureMapPreparation (copies + Gaussian) | 377 | 7.8 | 48× |
| K-σ noise estimates (×2, ≤5 iterations each) | 799 | 12.6 | 63× |
| WaveletCalculation (à-trous, 4 layers, fused subtract) | 218 | 11.5 | 19× |
| PostWaveletConvolution (9-tap Gaussian) | 132 | 5.1 | 26× |
| BinarizationStatistics (65536-bin histogram median) | 1233 | 41 | 30× |
| Local-background grids ×2 (exact per-block radix-select) | 499 | 7.7 | 65× |
| D2H downloads (2 × 245 MB) | 0 | 157 | — |
| **Whole chain** | **3573** | **301** | **11.9×** |

- muggsie 11.7 MP: 753 ms → 62 ms-equivalent chain (same shape; transfers proportionally larger).
- **CPU I1 baseline** (the documented "parallelize the histogram" future-work item, measured here as a
  bit-identical parallel per-partition histogram): 1233 ms → **37 ms, median EXACT**. Best-available-CPU
  chain ≈ 2377 ms ⇒ GPU still **7.9×** vs best CPU. I1 is worth shipping regardless of the GPU verdict.
- Gate G2 (≥3× hybrid build incl. transfers + flood-fill floor): 301+810 = 1.11 s vs 3573+810 = 4.38 s ⇒
  **3.9× — PASS** (target 5× nearly met; PCIe Gen3 transfers are most of the gap).

### Divergence (`bench-gpu --compare`, both frames)

| Metric | muggsie | CWhite 61 MP |
|---|---|---|
| Measurement image (hotpixel-filtered) | **bit-identical** | **bit-identical** |
| Hotpixel count | EXACT | EXACT |
| K-σ σ rel-delta (structure / measurement) | 1.3e-9 / **0.0** | 1.7e-7 / **0.0** |
| K-σ iteration counts | equal | equal |
| Histogram median rel-delta | 5.1e-7 | 3.1e-7 |
| Structure map max-abs-diff | 1.2e-7 | 1.8e-7 |
| **Binarized flips** | **0 (0.00000%)** | **0 (0.00000%)** |
| Grid max-abs-diffs (median/σ) | ≤5.5e-9 | ≤5.5e-9 |

The exactly-reproducible stages (median filter, histogram bucketing, order-statistic grids, binarize
compare) are exact; the ~1e-7 residue on convolution surfaces is FMA contraction propagating through, and
it never crossed a binarization threshold on either frame. This is the same divergence class as the
shipped wavelet swap (3e-8, `StarDetectorVersion` bump).

## 3. Integration (`optimize --gpu`)

`StarDetector.EarlyAcceleratorOverride` (internal static, **null in production ⇒ CPU span byte-for-byte
unchanged**) delegates steps 1–5b to an `IEarlyPipelineAccelerator`; declining (unsupported input, device
failure) falls back per build. TestApp's `GpuEarlyPipeline` pools 2 GPU working sets (~2.5 GB at 61 MP)
behind a semaphore; the harness's 12-way frame fan-out serializes onto them. `--gpu` on `optimize` enables
it; exit prints `GPU early-span builds=N, CPU fallbacks=M`. CFA/debayer (bayered frames) and
`SaveIntermediateFilesPath` builds stay CPU.

Gate G3: muggsie 50-eval smoke — GPU arm lands **identical bestJ=0.995579**, 20 GPU builds, **0
fallbacks**; unit suite green (the parity-guard test caught the bench's raw-Mat load; fixed via
`LoadDebayeredFloatMat`).

## 4. End-to-end A/B (pinned, sequential, `--max-evals 250`, non-verbose)

Both arms identical flags apart from `--gpu`; optimize-phase wall from the harness's own summary line.
"GPU v1" = first integration (shared default stream + staging memcpys); "GPU v2" = per-chain CudaStream +
direct Mat↔device copies (chain 301 → 214 ms at 61 MP, 16.7× vs CPU chain).

| Run | CPU wall | GPU v1 wall | v1 speedup | GPU v2 wall | v2 speedup | fallbacks |
|---|---|---|---|---|---|---|
| muggsie | 66.6 s | 24.3 s | 2.74×* | TBD | TBD | 0 |
| Panos (labeled) | 104.3 s | 59.7 s | 1.75× | TBD | TBD | 0 |
| CWhiteFocus 61 MP | 515.3 s | 278.8 s | 1.85× | TBD | TBD | 0 |
| timmer (bayered) | 559.6 s | 408.7 s | 1.37× | TBD | TBD | 0 |

\* muggsie's GPU arm took a different (equally valid) search path — see equivalence below — landing with
far fewer early rebuilds, so its wall benefits from both the GPU and the shorter path.

### Search-outcome equivalence (the result that matters)

- **All four runs land IDENTICAL `optimized_settings.json`** (excluding the BaselineJ metadata field) on
  CPU and GPU arms — including muggsie, whose compass trajectory diverged (1e-6-class J noise steered
  different moves) yet converged to the same landing.
- Final J: Panos and CWhite **identical** (0.8556 / 0.996068); timmer Δ1e-6; muggsie Δ9e-6 (0.997195 CPU
  vs 0.997204 GPU — same params, evaluator noise). All ≤1e-5 relative, i.e. ~500× inside the 0.5%
  acceptance bar used by the eval-budget work.
- **CPU final confirm:** satisfied by construction — the landed settings are the ones the CPU arm itself
  evaluated (J_cpu of the GPU-landed settings = the CPU arm's own bestJ on every run).
- Zero CPU fallbacks across all arms; Panos/CWhite/timmer reproduced the CPU arm's exact
  builds/reuses counts (161/1309, 333/1917, 540/1710).

## 5. Verdict & recommended next steps

TBD.
