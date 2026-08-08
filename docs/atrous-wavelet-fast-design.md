# Fast à trous wavelet for structure detection — design & experiment results

## Problem

`WaveletCalculation` — the structure-removal à trous B3-spline residual (`StarDetector` step 4) — is the
single most expensive stage of star detection: **~44% of a whole `Detect`** on a large sensor (2.1–2.5 s of a
4.8–6.0 s frame; `docs/star-detection-optimizer-performance-design.md`), and its cost **doubles with every
added StructureLayer** (F52 measured 27/46/117/254/526 s for layers 4→8 on a 9-frame run). Every production
AF frame pays it, and every EARLY-context rebuild in the optimizer pays it again. The speedup idea was
already on file as I1 ("early-stage internal parallelism") in `docs/star-detection-optimizer-speedup-results.md`.

## Root cause

`CvImageUtility.ComputeResidualAtrousB3SplineDyadicWaveletLayer` implements the à trous cascade by building,
for layer *i*, a **dense zero-padded 1-D kernel** of length `2^(i+2)+1` (5, 9, 17, 33, 65, 129, 257, 513 …)
and calling `Cv2.SepFilter2D` with it. Only **5 taps are ever non-zero** (the B3-spline weights
`0.0625, 0.25, 0.375, 0.25, 0.0625` at offsets `0, ±2^i, ±2^(i+1)`); OpenCV convolves all the zeros. Per-pixel
multiply-accumulates for a 6-layer cascade: `2×(5+9+17+33+65+129) = 516` versus the **60** the à trous
structure actually needs — and OpenCV 4.6's `SepFilter2D` runs single-threaded here on top of that.

## Approach chosen: sparse 5-tap dilated separable convolution (`AtrousWaveletFast`)

`Utility/AtrousWaveletFast.cs` applies exactly the 5 non-zero taps per direction per layer:

- **Constant work per layer** (~10 MACs/pixel/pass) regardless of dilation, so per-layer cost stops doubling.
- **Two-pass separable** (horizontal then vertical) per layer with two ping-pong buffers, mirroring
  `SepFilter2D`'s row-then-column float pipeline.
- **SIMD** via `System.Numerics.Vector<float>` (256-bit on AVX2 hardware) for interiors; scalar borders.
- **Multithreaded** with `Parallel.ForEach` over row ranges through the repo's `ParallelExecution` shared
  scheduler (bounded to ProcessorCount alongside the two concurrent K-σ noise-estimate tasks), with the
  standard parallelism-knob and CancellationToken plumbing.
- **BORDER_REFLECT parity**: multi-bounce reflection matches `Cv2.BorderInterpolate(..., BorderTypes.Reflect)`
  exactly (unit-tested for every length/offset combination the taps can produce).
- **Deterministic**: every output pixel is computed independently with a fixed operation order shared by the
  scalar and vector paths, so results are independent of thread count and SIMD width.
- **Fused final subtract** (`ComputeResidualAndSubtractInPlace`): the production step is
  `residual → SubtractInPlace(structureMap, residual)` (subtract + clamp = 3 more full-image passes). The
  fused form writes `clamp(src − residual, 0, 1)` directly in the last vertical pass — no residual Mat, no
  re-read. Used when debug intermediates are off; bit-identical to compute-then-subtract (unit-tested).

### Numerical status vs legacy

The tap-summation **order** differs from OpenCV's dense kernel loop, so results agree to float rounding but
are **not bit-identical**: measured max abs diff ≤ ~1e-6 on [0,1] images across all tested size/layer
combinations (tolerance 5e-6 asserted in tests; an indexing/border bug would show up orders of magnitude
above that). Rounding-level structure-map differences can in principle flip a near-threshold binarization
pixel, so the swap is gated:

- `StarDetectorParams.FastAtrousWavelets` (**default OFF** while under validation) — production detection is
  bit-identical with the flag off.
- The flag is **EARLY-cache-keyed** (`StarDetector.EarlyCacheKeyProperties`) so optimizer contexts are never
  reused across implementations.
- End-to-end A/B on the deterministic equivalence field: **identical detection signatures** (stars, HFRs,
  metrics, all rejection bounds) between legacy and fast paths. Star *measurement* reads the source image —
  the wavelet only shapes candidate formation — so signature equality is the expected outcome, not luck.

## Alternatives considered

- **Single equivalent Gaussian blur** (the L-layer B3 cascade ≈ one Gaussian with
  σ ≈ 1.05·√((4^L−1)/3)): rejected — a real algorithm change (different kernel shape, meaningfully different
  residual), would move detection results, invalidating all calibrated settings for a speedup no better than
  the sparse path.
- **Polyphase/decimated pyramid** (compute per-coset with the base 5-tap kernel via strided sub-images):
  mathematically identical, but OpenCV cannot view strided cosets without copies; the copy traffic costs more
  than the sparse pass it replaces.
- **OpenCL/UMat**: deployment variability (GPU drivers on end-user NINA machines) for a step that becomes
  memory-bound anyway once the tap count is fixed.

## Benchmark results

Machine: 24 logical cores, AVX2 (`Vector<float>.Count=8`), workstation GC, Release build, median of 3
iterations after warmup (`TestApp bench-wavelet --detect`). `fast` = residual only; `fused` = residual +
subtract + clamp (the production step; compare against `legacy + SubtractInPlace`). `fast-1T` =
`parallelismKnob: 1`, isolating the sparsity+SIMD gain from threading.

**Mat-level residual, 6248×4176 (26.1 MP):**

| StructureLayers | legacy (SepFilter2D) | fast | speedup | fast-1T | fused | maxAbsDiff |
|---|---|---|---|---|---|---|
| 4 | 130 ms | 52 ms | **2.5×** | 82 ms | 76 ms | 3.0e-8 |
| 6 | 781 ms | 81 ms | **9.7×** | 111 ms | 81 ms | 1.5e-8 |
| 8 | 4267 ms | 107 ms | **40×** | 154 ms | 105 ms | 1.5e-8 |

**Mat-level residual, 3008×3008 (9.0 MP):** layers 4: 43→19 ms (2.3×); layers 6: 206→28 ms (7.5×);
layers 8: 1297→37 ms (35.6×). Same ≤3e-8 max abs diff.

Legacy cost doubles per layer (the F52 curve); the fast path is nearly flat (52→81→107 ms for 4→6→8
layers). Even single-threaded, the sparse SIMD path beats legacy at every layer count, so the win does not
depend on core count. Both passes stream at memory bandwidth once the tap count is fixed, which is why 24
threads only add ~1.5× over one thread — and why `fused` ≈ `fast` despite doing the subtract for free.

**Detect-level A/B, 26.1 MP, ~2400-star synthetic grid** (ModelPSF off, NoiseClippingMultiplier 4.0):

| StructureLayers | legacy detect | fast detect | speedup | star results |
|---|---|---|---|---|
| 4 | 661 ms | 648 ms | 1.02× | identical (Δcenter = 0.0) |
| 6 | 1272 ms | 670 ms | **1.90×** | identical (Δcenter = 0.0) |
| 8 | 4718 ms | 738 ms | **6.4×** | identical (Δcenter = 0.0) |

Two things to read out of the detect table:

- **Whole-detect time becomes nearly layer-independent** (648/670/738 ms): the wavelet stops being the
  dominant knob, so the F52 "~2× wall per StructureLayer" guidance (and the optimizer's incentive to avoid
  high-layer probes) no longer describes the flag-on pipeline.
- At the **default 4 layers on this 24-core machine** the end-to-end gain is small because the wavelet
  already overlaps the two concurrent K-σ noise-estimate tasks; the machines that motivated this work (the
  bank machine measured 2.1–2.5 s of wavelet in a 4.8–6.0 s detect) have far less headroom, and donut mode
  (effective 6 layers) and optimizer EARLY rebuilds (up to 8+boost layers) hit the steep part of the legacy
  curve where the measured gains are 1.9–6.4× end-to-end.

## Validation

- `AtrousWaveletFastTests` (15 tests): numeric equivalence vs legacy across sizes/layers incl. tiny
  multi-bounce borders and odd (non-SIMD-multiple) widths; exact `Reflect` parity with OpenCV; source
  not modified; determinism across runs and parallelism; fused-subtract bit-identity; NaN-pixel clamp
  parity between SIMD lanes and scalar tail; end-to-end detection signature parity; EARLY-key
  classification.
- Full unit test suite: 3688 tests passed, 0 failures (re-run green after the review fixes below).
- Adversarial multi-agent review (3 dimensions → per-finding verification): 6 findings raised, 5 confirmed
  and fixed. The two substantive ones:
  - **Cancellation race (critical, fixed):** the first cut passed the detection CancellationToken into the
    wavelet's `Parallel.ForEach`, creating a NEW cancellation point inside the window where the two K-σ
    noise-estimate tasks still read `srcImage`/`noiseReducedImage` — an OCE unwind would dispose those Mats
    under the running tasks (native use-after-free). Fixed by not passing the token there: like the legacy
    path, the ~100 ms window is non-cancellable and the token is honored at the pre-existing points after
    the task awaits. Any future cancellation point added between the task launches and their awaits must
    first join those tasks.
  - **NaN clamp divergence (minor, fixed):** the fused subtract's scalar tail used `Math.Min/Max` (NaN
    propagates) while the SIMD lanes and the legacy `Cv2.Max/Min` clamp map NaN → 0, making output depend
    on column position and vector width for NaN inputs (reachable only via raw float `Detect(Mat)` inputs,
    e.g. TestApp replay of a float FITS). Scalar tail now uses the comparison-based clamp.
- **Not yet done (needs the machine with `D:\Autofocus Bank`):** `bank-verify` regression across the 22-run
  bank comparing flag OFF vs ON (recall/precision/J deltas expected ≈ 0, same criterion as past
  rounding-level changes). Recommended before flipping the default ON.

## Rollout recommendation

1. Land flag-gated (default OFF) — zero production impact, available to the bench harness and tests.
2. Run the AF-bank validation (flag ON vs OFF) on the bank machine; expect no metric movement beyond
   run-to-run noise.
3. Flip the default ON (or remove the legacy path + flag outright — no persisted option was added, so no
   Options UI is involved) and update the F52 "~2× per layer" wizard guidance, which stops being true:
   with the sparse path, per-layer cost is flat, so `DefocusAwareStructure` boosts and higher
   `StructureLayers` stop being the optimizer's dominant wall-clock knob.

## Follow-up opportunities (measured, not yet implemented)

- `BinarizationStatistics` (~21% of detect) and the K-σ noise estimates become the new critical path once
  the wavelet shrinks; the K-σ tasks already overlap the wavelet, so end-to-end gains are smaller than the
  44% share suggests. A log-histogram K-σ (noted in `CvImageUtility.KappaSigmaNoiseEstimate`) is the next
  candidate.
- `PostWaveletConvolution` (~3%) still uses a dense `SepFilter2D` Gaussian; the same pass infrastructure
  could absorb it if it ever matters.
