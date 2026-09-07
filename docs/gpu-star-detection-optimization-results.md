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
4. **End-to-end `optimize --gpu` lands at 1.4–2.7×** (muggsie 2.7×, CWhite 1.85×, Panos 1.76×, timmer
   1.37×) — short of the ≥3× go/no-go bar. With EARLY compute effectively free, the wall is set by the
   CPU flood-fill, the LATE stage, and fixed harness overhead; two further GPU tunings (chain −40%,
   pool 2→4) each moved end-to-end by ~0, proving the residue is CPU-shaped. §5 lists what would clear
   3× (flood-fill acceleration first) and the ship-regardless CPU win (I1 parallel histogram, 33×,
   bit-identical).

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

| Run | CPU wall | GPU v1 wall | GPU v2 wall | E2E speedup | fallbacks |
|---|---|---|---|---|---|
| muggsie | 66.6 s | 24.3 s | 24.4 s | **2.7×**\* | 0 |
| Panos (labeled) | 104.3 s | 59.7 s | 59.2 s | **1.76×** | 0 |
| CWhiteFocus 61 MP | 515.3 s | 278.8 s | 279.9 s | **1.85×** | 0 |
| timmer (bayered) | 559.6 s | 408.7 s | 407.7 s | **1.37×** | 0 |

\* muggsie's GPU arm took a different (equally valid) search path — see equivalence below — landing with
far fewer early rebuilds, so its wall benefits from both the GPU and the shorter path.

### Round 2 — after the candidate-collection rework (§6)

Same protocol, after the run-index walker (bit-identical, now the default for BOTH arms) and with the
opt-in CCL collector as a third arm:

| Run | CPU (fast walker) | GPU | GPU+CCL | best J: legacy vs CCL arm |
|---|---|---|---|---|
| muggsie | 63.9 s / J 0.997195 | 24.7 s (**2.59×**) | 24.7 s | 0.997195 → **0.997252** (CPU+CCL: 28.6 s, 18 builds vs 207) |
| Panos (labeled) | 100.6 s / 0.8556 | 53.2 s (**1.89×**) | 79.1 s | 0.8556 → **0.919816** |
| CWhiteFocus 61 MP | 497.1 s / 0.996068 | 252.0 s (**1.97×**) | 258.9 s | 0.996068 → **0.997037** |
| timmer (bayered) | 553.7 s / 0.997785 | 435.2 s (1.27×) | **373.3 s (1.48×)** | 0.997785 → **0.997819** |

Aggregate over the 4 runs: 1215 s CPU → 765 s GPU (**1.59×**) → 736 s GPU+CCL (**1.65×, with higher J on
every run**). All arms: zero fallbacks.

**The v1→v2 invariance is the second-most-important measurement of the spike:** cutting the GPU chain
another 40% (301→214 ms at 61 MP) moved end-to-end wall by ~0. The `--gpu` arm is **no longer bound by
the GPU-able compute at all** — the residual wall is the CPU-side work: the LATE per-star stage, the
sequential flood-fill + binarize/dilation tail of each build, and fixed harness overhead (run loading,
fit, summary/annotation detections). Attribution below.

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

### Where the GPU arm's wall actually goes (CWhite, TRACE attribution)

Stage-time totals across the GPU arm's 344 builds + 2270 late gates (278 s wall):

| Component | stage-time | mean/build | note |
|---|---|---|---|
| GpuEarlySpan (incl. queueing on the GPU pool) | 457 s | 1.33 s | **0.21 s isolated** — the rest is 9 concurrent frame builds queueing on 2 pooled chains |
| CollectStarCandidates (CPU, sequential per frame) | 379 s | 1.10 s | the largest true single residual |
| Binarize/dilate tail (CPU) | 74 s | 0.22 s | |
| LATE gates (CPU, already parallel) | 823 s | 0.36 s/gate | ~30–40% of wall |

Two conclusions: (a) further GPU-chain optimization is pointless for this workload — v2's 40% chain cut
moved E2E by ~0; (b) the residual is exactly the deliberately-out-of-scope CPU work: flood-fill + late
stage + fixed harness overhead. A pool-depth probe (2 → 4 chains) confirmed it: **no E2E change** (Panos
60.7 vs 59.2 s, CWhite 288 vs 280 s) — the apparent GPU queueing overlaps CPU work that bounds the burst
anyway. Every GPU-side lever is exhausted; the wall is CPU-shaped.

## 5. Verdict

**Hypothesis: partially proven — decisively at the component level, short of the bar end-to-end.**

Proven:
- CUDA on this class of hardware runs the detector's EARLY compute **12–17× faster** than the current CPU
  implementation (and ~8× vs best-available CPU), with **search outcomes identical**: all four A/B runs
  landed byte-identical `optimized_settings.json`, ΔJ ≤ 1e-5, zero binarized flips at the pixel level,
  zero runtime fallbacks. The "GPU search, CPU confirm" production shape is validated trivially — the
  landed settings ARE the CPU arm's settings.
- ILGPU 1.5.3 makes this deployable-in-principle: NuGet-only, driver-JIT, no CUDA toolkit, works on
  Blackwell today (with the XMath/LibDevice avoidance noted in §1).

Not met:
- The **≥3× end-to-end** go/no-go bar on heavy runs. Measured: muggsie 2.7×, CWhite 1.85×, Panos 1.76×,
  timmer 1.37×. Amdahl is fully in charge: with the EARLY compute effectively free, `optimize` wall is
  set by the CPU flood-fill (~1.1 s/build at 61 MP), the LATE per-star stage, and fixed harness overhead
  — all deliberately out of the spike's scope. Both post-hoc GPU tunings (chain −40%, pool 2→4) moved
  end-to-end by ~0, which is the empirical proof of that statement.

### What would clear 3×, if pursued

1. ~~**`CollectStarCandidates`**~~ — **DONE, see §6** (run-index walker, bit-identical, 5.7× on the
   stage; lifted CWhite's GPU arm 1.85× → 1.97× and Panos 1.76× → 1.89×). The remaining stage cost
   (~0.14 s/build) is no longer a first-order term.
2. **Fixed overhead** (run loading, seed/summary/annotation detections, fit) — untouched by the spike,
   and now a visible slice of the GPU arms' walls.
3. **LATE stage on GPU** — the largest remaining block on every run; irregular/branchy, high effort.
4. **Different workload**: `bank-verify`-style batch evaluation (fixed params, every frame cold) matches
   the GPU's strength far better than the optimizer's cached search; expect near the chain's 12–17×
   there for the detection portion.

### Ship-regardless items

- **CPU I1 parallel histogram**: 33×, bit-identical, EXACT median, ~2 h of work — benefits every user
  (wizard + live AF) with no GPU anywhere. Recommend promoting into `CvImageUtility` behind the existing
  test suite.
- The `IEarlyPipelineAccelerator` seam is production-inert (null hook ⇒ CPU span verbatim) and can stay.

### Caveats

- All numbers are Debug-configuration (matching every historical harness figure) on a PCIe **Gen3** host;
  Release + Gen4/5 would improve both arms (transfers are ~60% of the isolated GPU chain).
- Bayered runs keep their CPU CFA/debayer per early build (params-dependent, out of scope) — that alone
  caps timmer-class runs near 1.4–1.9×.
- The spike used the pinned settings' `NoiseReductionRadius=4` shape (two K-σ estimates per build); other
  profiles shift stage shares but not the conclusions.

## 6. Candidate-collection rework (follow-up to §5.1) — the flood-fill lever, pulled

Two collectors now sit behind a shared, parallel per-row run-length index (`StructureRunIndex`), so
candidate collection never raster-scans (or mutates) the full-frame map again; the saturated-pixel count
in `EvaluateGlobalMetrics` is parallelized too (order-independent ⇒ bit-identical).

1. **Run-index walker — bit-identical** (the spike-era default; §7 later made CCL the v3 default, keeping
   this walker reachable via `--legacy-collector`). A literal translation of the legacy
   zero-the-bbox scan: same seeds, same growth (including the same-row gap-jump and downward-only quirks),
   same point ORDER (eccentricity sums are order-sensitive), with the bbox zeroing replaced by interval
   subtraction. The legacy implementation is retained purely as the equivalence oracle for
   `CandidateCollectionTests` (random + adversarial maps: donut, bbox-shadowing, gap-jump, edges, full-lit).
   Single frame at 61 MP: `CollectStarCandidates` **0.81 s → 0.141 s (5.7×)**.
2. **8-connected-component collector — deliberately NOT bit-identical.** At the time of the spike this was
   opt-in (`StarDetectorParams.UseConnectedComponentCollection` default OFF; a TestApp `--ccl` flag);
   superseded by §7, which promoted it to the DEFAULT (`StarDetectorVersion` 3) — the `--ccl` flag is gone
   and the legacy walker is now the opt-in via `--legacy-collector`. True components via run-based
   union-find: no bbox shadowing (a star overlapping an earlier candidate's bounding box survives), whole
   components collected (connected donut rings arrive unified), no same-row gap-jump merging. 0.080 s at
   61 MP. At fixed params on the CWhite frame it detects **4457 vs 4379 stars (+1.8%)** — the surviving
   shadowed neighbors.

**The CCL surprise is quality, not speed** (round-2 table above): the CCL arm landed a **higher J on all
four runs**, most dramatically on the one run with human-labeled ground truth (Panos: 0.8556 → 0.9198,
an improvement ~50× larger than anything the eval-budget work argued about — the labeled objective's
recall/precision term directly credits the stars the legacy walker was destroying). On timmer it was also
1.17× faster than the GPU arm (the changed J landscape converged with fewer early rebuilds), and on
muggsie the CPU+CCL arm beat every other arm's J while converging 2.2× faster than legacy CPU. (The
spike-era caveat that sensor modeling kept the toggle OFF is superseded by §7: with CCL as the v3 default,
sensor modeling and autofocus use it too — an intended behavior change under the `StarDetectorVersion`
discipline.)

**Recommended follow-up for the CCL mode** (the promotion half happened — §7 shipped CCL as the v3
default): validate that the higher J is real detection quality — run `bank-verify` / `golden eval`
recall/precision with the CCL default across the bank (the golden sets are detector-independent, so they
arbitrate honestly).

## 7. Production promotion (user-directed follow-on; plan: `plans/gpu-production-integration-plan.md`)

Everything above was promoted from spike to plugin:

- **CCL is the DEFAULT** (`StarDetectorVersion` 2 → 3). The pre-v3 walker stays reachable
  (`--legacy-collector`) and remains the oracle for the bit-identity tests. Suite fallout was re-pinned
  deliberately: the golden small-field signature gains one star (a surviving shadowed neighbor), the
  donut/relaxation premise tests pin the legacy collector whose fragmentation they guard, and the
  MeasuredSensitivity synthetic field's noise drops σ 0.02 → 0.01 — at 0.02 it sat exactly on the 5σ
  contamination boundary that v3's TIGHT bounding box (13×13 vs the walker's gap-jump-inflated 14×13,
  which shifts the background annulus one pixel) flips. That razor-edge case is the entire class of
  CCL-vs-legacy measurement difference observed.
- **`GpuAccelerationEnabled` option** (default ON; machine-local — never imported or per-filter; edited on
  the optimization wizard's START page, its only consumer). It gates ONLY the optimization wizard/harness, which stamp
  `StarDetectorParams.AllowGpuAcceleration` (an EARLY cache-key param, so contexts never cross backends)
  after `GpuAccelerationPolicy` approves: CUDA device initializes, frames ≥ 2 MP, VRAM ≥ working set +
  1 GB slack. `GpuEarlyPipeline` latches itself off after 3 per-build failures (one warning). Autofocus,
  sensor modeling, and single-frame detection never consult any of it.
- **GPU code lives in the plugin** (`Gpu/`), ILGPU 1.5.3 ships in the deploy xcopy; TestApp's `bench-gpu`
  consumes the same classes. Harness: `--gpu` / `--no-gpu` force; default follows the settings option.
- **Debayer optimization** = `PreparedSourceCache`, the parity spec's sanctioned per-(frame,
  hotpixel-params) prepared-source cache, stamped on optimization params only (wizard + harness; disposed
  with the run / cleared between `--per-run` iterations). Bayered optimize runs stop re-doing the ~1.2 s
  CFA+debayer on every early rebuild; a cache hit returns a clone of identical pixels, so detection is
  byte-identical. GPU debayer was evaluated and NOT built: the cache removes the recompute entirely,
  which beats accelerating it.
- Verified: suite 4431/4431; option-driven muggsie run reproduces the round-2 GPU+CCL arm bit-for-bit
  (24.3 s, bestJ 0.997202, 0 fallbacks).

### Production-default stack vs where the day started

Same protocol (pinned, sequential, 250 evals). "Before" = the original round-1 CPU arms (legacy collector,
no cache, no GPU); "after" = the new zero-flag default (CCL + prepared-source cache + option-driven GPU):

| Run | before | after | wall | J |
|---|---|---|---|---|
| muggsie | 66.6 s / 0.997195 | 24.3 s / 0.997202 | **2.7×** | better |
| Panos (labeled) | 104.3 s / 0.8556 | ~79 s / **0.9198** | 1.3× | **+0.064** (the search spends its win chasing the far better optimum) |
| CWhiteFocus 61 MP | 515.3 s / 0.996068 | 258.9 s / 0.997037 | **2.0×** | better |
| timmer (bayered) | 559.6 s / 0.997785 | **285.7 s** / 0.997819 | **2.0×** | better |

The prepared-source cache alone is worth 1.31–1.36× on bayered runs (timmer GPU arm 373 → 286 s, CPU arm
554 → 408 s; identical builds/reuses/J — exact reuse, zero behavior change). Every "after" arm: zero GPU
fallbacks; J equal-or-better on all four runs.
