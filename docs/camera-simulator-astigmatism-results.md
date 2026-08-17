# Camera Simulator — Astigmatic PSF: Performance Results

**Provenance.** AMD Ryzen Threadripper 2970WX (24 cores / 48 threads), 62 GB RAM, Windows via WSL interop.
**Release** build, workstation GC. ASTAP G18 (`.290`) catalog at `C:\Program Files\astap`, limiting mag ~18.
Harness: `TestApp bench-simrender` (see [`.claude/docs/testapp-cli.md`](../.claude/docs/testapp-cli.md)).
Design: [`camera-simulator-astigmatism-design.md`](camera-simulator-astigmatism-design.md). Raw rows:
[`camera-simulator-astigmatism-results.csv`](camera-simulator-astigmatism-results.csv).

Every number here is measured. The two figures that are *not* — the historical 13.1 s Debug render and the
`PoissonToGaussianThreshold` cost table — are quoted from
[`plans/camera-simulator-realism-and-performance-plan.md`](../plans/camera-simulator-realism-and-performance-plan.md)
and labelled where used.

## TL;DR

1. **A clean, well-corrected rig pays nothing.** With no tilt and no backfocus error the astigmatic render
   is within noise of the isotropic one across every field and defocus tested — worst 1.03×. The elliptical
   path is never entered, because the quantized defocus levels coincide and the kernel collapses to the
   circular one. **SHIPPED.**
2. **The dense 61 MP worst case is 1.39× and 880 ms.** On the headline field — 34,909 stars on a QHY600 at
   350 steps of defocus with tilt and backfocus injected — the shipped default renders in 880 ms against 634
   ms isotropic. That is 6 % of a 15 s AF exposure and 18 % of a 5 s one, so it stays entirely hidden behind
   the prefetch. **SHIPPED**, with the ratio gate missed and the absolute gate passed by 3.4× — see
   [Gates](#gates).
3. **Three optimizations took it from 1.58× to 1.33×**, all fidelity-free: building the distinct kernels in
   parallel, pooling the fine-grid buffers off the large-object heap, and writing the separable convolution
   tap-outer so it vectorizes. Kernel generation went from 3.5–6× the circular cost to 1.0–1.5×, at a
   measured scaling exponent of 1.96 against the ideal 2. **MEASURED, all three kept.**
4. **The cache budget was over-counting by 4× and has been replaced by an exact count.** Bounding the kernel
   count by (defocus cells × astigmatism cells × orientation bins) predicted 863 MB where the real cache held
   29 MB, and coarsened the defocus quantum on that basis — degrading every frame's fidelity to fit a cache
   that was never going to be allocated. **FIXED before measurement.**
5. **A perfect corrector is still elliptical wherever the spacing is wrong**, and the matrix shows it: at
   `on-zero` with 60 µm of backfocus error the dense-wide field builds 121 kernels against the isotropic
   arm's 7. That is the Seidel-fixed induced split, not a residual, and it is the single biggest behavioural
   difference from the model this replaced. On the clean `A0` config `on-zero` collapses to `off` exactly —
   1 kernel, both arms, all four fields. **VERIFIED.**

## Method

### Why the phases are instrumented rather than differenced

`NoiseGenerator.DevelopRange` branches at `PoissonToGaussianThreshold = 40` electrons: below it a Poisson
draw costs ~λ uniform samples, at or above it a fixed four. The repo's own measured table has development
swinging ~10× with λ — 12 s at λ = 14.6, **37 s at λ = 39**, 3.8 s at λ = 60. Stars change λ. So a "dense
frame minus starless frame" difference books a development delta as PSF cost, which is precisely the
quantity being isolated.

Instead, `StarFieldCompositor` reports its own phases through an internal `RenderPhaseTimings` sink, passed
by a fourth `internal` `Render` overload. Every timing call site is guarded, so a production render never
reads the clock, and `Render_TimingsOverload_IsByteIdenticalAndReportsThePhases` pins that the instrumented
and uninstrumented frames are identical.

The headline field develops at **11.9 e⁻/px**, comfortably below the Poisson cliff, so development is the
expensive branch throughout — the honest, slower side to measure against.

### Rig and fields

All four fields use the QHY600 / IMX455 (9576 × 6388, 61.2 MP, 3.76 µm), L filter, gain 100, 5 s exposure,
2.5″ seeing, sky 20.5 mag/arcsec², focuser step 3.336 µm, limiting mag 17.

| field | pointing | optics | on-frame stars | σ_min | purpose |
|---|---|---|---|---|---|
| `dense-wide` | γ Cygni | 530 mm f/5 | **34,909** | 0.79 px | the headline dense field |
| `dense` | γ Cygni | 1000 mm f/7.1 | 8,464 | 1.44 px | same pointing as the `D19_cygnus_deep_shed` bank row |
| `sparse` | North Galactic Pole | 1000 mm f/7.1 | 990 | 1.44 px | 8.5× density contrast, all else identical |
| `dense-oversampled` | γ Cygni | 2000 mm f/8 | 1,670 | 2.78 px | R ≈ 34 px instead of 30 — the R² stress |

**The dense field had to move, and the plan's target was unreachable as written.** It called for ≥ 25,000
on-frame stars at 1000 mm and mag 16. At 1000 mm a 61 MP frame covers 2.8 sq deg, and the installed G18
catalog bottoms out near mag 18 — so even at the catalog's faint limit the 1000 mm field yields 14,237
on-frame stars, and at mag 16 only 4,935. A 530 mm f/5 widefield on the same sky (an FSQ-106-class
astrograph, a common QHY600 pairing) covers 3.6× the area and clears the target at mag 17.

### Arms

| arm | meaning |
|---|---|
| `off` | astigmatism disabled — today's isotropic renderer |
| `on-zero` | enabled with corner residual 0 — a **perfectly corrected** optic |
| `on` | corner residual 15 µm, the shipped default |
| `on-strong` | corner residual 40 µm, a badly astigmatic corrector |

`on-zero` is **not** a "feature off" arm and it is worth being precise about why. A perfect corrector still
gets an induced split from mis-spacing — exactly half the curvature the same mis-spacing induces, by Seidel's
3:1 rule — so this arm is elliptical wherever the backfocus error is nonzero, and on `A1`/`A2` it measures
the *floor* of what a well-corrected rig costs rather than zero. On the clean `A0` config it is a genuine
verification arm: with no residual and no spacing error the coefficient is literally `0.0`, every star's two
quantized levels coincide, and the census matches `off` exactly.

The plan expected `on-zero` to isolate a fixed "new code path" overhead. It cannot, and that is a finding
rather than a gap: there is no code-path cost separate from ellipticity. The elliptical generator is reached
only when a star's two quantized defocus levels differ, so a frame with no astigmatism runs the identical
code it always did.

Aberration configs: `A0` clean (no tilt, no backfocus), `A1` backfocus 60 µm only, `A2` tilt 40 µm at 30° +
backfocus 60 µm. Defocus offsets 0 / 150 / 350 steps. 5 timed iterations per cell after 1 warmup, with a
full GC between iterations; medians reported. 144 cells, 8m01s wall.

## 1. Kernel generation — the ladder

`--kernel-ladder` times `PsfKernelGenerator` alone across a radius ladder, with no catalog and no frame. It
is the cheapest early warning that the convolution scales badly, and it is where the optimization work was
steered from.

| target R | circular ms | elliptical ms | ratio | bytes | elliptical local exponent |
|---|---|---|---|---|---|
| 8 | 3.28 | 0.71 | 0.22× | 22,592 | — |
| 15 | 3.55 | 2.90 | 0.82× | 65,600 | 2.23 |
| 30 | 4.89 | 12.71 | 2.60× | 242,240 | 2.13 |
| 60 | 12.05 | 12.35 | 1.03× | 942,624 | −0.04 |
| 120 | 39.66 | 47.02 | 1.19× | 3,728,208 | 1.93 |
| 240 | 126.92 | 187.71 | 1.48× | 14,828,992 | 2.00 |

**Asymptotic exponent 1.96** over the top three rungs, against the 2.0 a separable convolution should hold
and the 4.0 a direct 2-D convolution would show. The separable path is behaving.

Local exponents are reported per rung rather than as one fit across the ladder because the small radii are
dominated by fixed cost — the radial LUT has a 512-entry floor — so a single slope reads far below the true
asymptotic scaling and would mask a bad convolution at large R. The R = 30 and R = 60 rungs bracket a change
in how many rim sub-samples the ellipse needs and are noisy against each other; the trend either side is
clean.

**Before the optimizations the same ladder read 1.97× / 3.51× / 4.84× / 6.00× at R = 30…240.** Ratios above
1.5 are gone.

## 2. Kernel-cache cardinality and memory

The number that actually grows when the cache key gains axes. With aberrations off the cache holds **one**
kernel; the astigmatic key is (tangential level, sagittal level, orientation bin).

Orientation bins resolved to **14–22** at the shipped residual and never approached the cap of 64, so the
kernel count is driven by realized level pairs, not by angular quantization. Peak cache **129.4 MB** against
the 384 MB budget, so the coarsening ladder never fired at the shipped default — every cell rendered at full
fidelity. Even the `on-strong` stress arm peaks at 238 MB, still inside it. (See
[the budget corrections](#three-budget-bugs-found-after-the-matrix-was-recorded) for how that budget was
arrived at.)

| field | cell | kernels (on) | bins | cache MB | kernels (on-strong) |
|---|---|---|---|---|---|
| dense-wide | 350 / A2 | 409 | 22 | 129.4 | 743 |
| dense-wide | 150 / A2 | 408 | 22 | 34.7 | 732 |
| dense | 350 / A2 | 189 | 15 | 42.3 | 350 |
| sparse | 350 / A2 | 168 | 15 | 37.7 | 298 |
| dense-oversampled | 350 / A2 | 140 | 14 | 40.7 | 249 |

**The exact count replaced an upper bound.** The bound — defocus cells × astigmatism cells × orientation
bins — predicted 863 MB for the dense-wide 350/A2 cell where the cache really holds 129 MB, because Δ and A
are both smooth functions of field position and so are nowhere near independent. Coarsening on that estimate
doubled the defocus quantum and halved the bins on a frame that fitted comfortably. Since assigning keys is
pure arithmetic and builds nothing, the budget loop now measures the cache it would really allocate and
retries only if that exceeds the budget.

## 3. Whole-frame timing

Per-phase medians, ms. `on/off` is the ratio that G2 gates.

#### dense-wide — 34,909 stars, 61 MP, the headline field

| defocus | aberr | off | on | on/off | strong | strong/off | kernels | bins | cache MB | kernelGen (on) |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | A0 | 535 | 537 | 1.00× | 534 | 1.00× | 1 | 1 | 0.0 | 0 ms (0%) |
| 0 | A1 | 535 | 559 | 1.04× | 580 | 1.08× | 156 | 22 | 2.6 | 6 ms (1%) |
| 0 | A2 | 535 | 635 | 1.19× | 635 | 1.19× | 383 | 22 | 6.6 | 70 ms (11%) |
| 150 | A0 | 568 | 551 | 0.97× | 554 | 0.98× | 1 | 1 | 0.1 | 3 ms (1%) |
| 150 | A1 | 559 | 659 | 1.18× | 702 | 1.25× | 167 | 22 | 14.4 | 76 ms (12%) |
| 150 | A2 | 554 | 714 | 1.29× | 772 | 1.39× | 408 | 22 | 34.7 | 121 ms (17%) |
| 350 | A0 | 600 | 599 | 1.00× | 612 | 1.02× | 1 | 1 | 0.3 | 6 ms (1%) |
| 350 | A1 | 615 | 750 | 1.22× | 840 | 1.37× | 169 | 22 | 53.1 | 119 ms (16%) |
| **350** | **A2** | **634** | **880** | **1.39×** | 1043 | 1.65× | 409 | 22 | 129.4 | 250 ms (28%) |

Headline cell phase breakdown:

| phase | off | on |
|---|---|---|
| catalog query | 86 | 82 |
| stamp-job build | 104 | 345 |
| …of which kernel generation | 7 | 250 |
| stamping | 86 | 78 |
| development | 451 | 430 |
| **total** | **634** [622–680] | **880** [856–1076] |

Development is unchanged, as it must be — the astigmatism model never touches it. So is stamping, within
noise. The entire delta is kernel generation.

#### dense — 8,464 stars, 1000 mm

| defocus | aberr | off | on | on/off | strong/off | kernels | cache MB |
|---|---|---|---|---|---|---|---|
| 0 | A2 | 389 | 404 | 1.04× | 1.05× | 183 | 5.1 |
| 150 | A2 | 394 | 441 | 1.12× | 1.27× | 190 | 15.2 |
| 350 | A2 | 400 | 475 | 1.19× | 1.36× | 189 | 42.3 |

#### sparse — 990 stars, identical optics to `dense`

| defocus | aberr | off | on | on/off | strong/off | kernels | cache MB |
|---|---|---|---|---|---|---|---|
| 0 | A2 | 339 | 342 | 1.01× | 1.00× | 150 | 4.1 |
| 150 | A2 | 334 | 363 | 1.09× | 1.20× | 168 | 13.5 |
| 350 | A2 | 341 | 407 | 1.19× | 1.42× | 168 | 37.7 |

#### dense-oversampled — 1,670 stars, 2000 mm f/8

| defocus | aberr | off | on | on/off | strong/off | kernels | cache MB |
|---|---|---|---|---|---|---|---|
| 0 | A2 | 355 | 385 | 1.09× | 1.14× | 134 | 9.1 |
| 150 | A2 | 358 | 412 | 1.15× | 1.24× | 144 | 19.6 |
| 350 | A2 | 368 | 475 | 1.29× | 1.47× | 140 | 40.7 |

## 4. Scaling controls

**Star count.** `dense` and `sparse` differ only in how many stars the pointing has — 8,464 against 990,
8.5×. Catalog query scales with it (48.6 ms vs 2.3 ms) and stamping scales with it (24.4 ms vs 13.8 ms);
**kernel generation does not** (80.2 ms vs 65.6 ms at 350/A2, for 189 vs 168 kernels). That is the expected
shape: kernels are a property of the aberration field, not of how many stars sample it. It also means a
*sparse* field is the harsher relative case — fewer stars to amortize the same kernel set over — and here
the two land at an identical 1.19×, because development dominates both.

**Kernel radius.** `dense-oversampled` doubles σ_min (2.78 px vs 1.44 px) and so the support radius, but its
kernel count is *lower* (140 vs 189) because the coarser plate scale spreads the same defocus over fewer
quantized levels. Cache bytes land similar (41 MB vs 42 MB): the R² per kernel is offset by having fewer of
them. Its ratio is nonetheless the second-worst in the matrix (1.29×), because each of those kernels costs
R² to build.

## 5. Under live contention

The realistic case: NINA prefetches the next frame at `StartExposure` while the previous autofocus point is
still being detected, and both loops route through the same shared CPU governor. `--with-detection` runs a
real `StarDetector.Detect` loop alongside every timed render.

| arm | total ms | kernelGen |
|---|---|---|
| off | 985 | 37 ms |
| on | 1125 | 341 ms |

**Ratio 1.14×** — *better* than the 1.39× measured idle, because contention slows the field-independent
development phase for both arms and so dilutes the difference. The absolute worst case under contention is
1.13 s.

## 6. Gates

| | gate | bar | measured | verdict |
|---|---|---|---|---|
| G1 | `on/off`, clean A0 cells | ≤ 1.10× | **1.03×** (on), 1.02× (strong) | **PASS** |
| G2 | `on/off`, all other cells | ≤ 1.25× | **1.39×** (on), 1.65× (strong) | **FAIL** |
| G3 | worst absolute cell | ≤ 3.0 s and ≤ exposure | **880 ms** (on), 1043 ms (strong) | **PASS** |
| G4 | kernel-generation share | ≤ 10 % | **28 %** (on), 39 % (strong) | **FAIL** |
| G5 | distinct kernels per frame | ≤ 512 | **409** (on), 743 (strong) | **PASS** at the default, **FAIL** on the stress arm |
| G6 | peak kernel cache | ≤ 256 MB | **129 MB** (on), 238 MB (strong) | **PASS** |
| G7 | `on/off` under contention | ≤ 1.40× | **1.14×** | **PASS** |

### On the three that missed

These bars were written in the plan before any measurement existed. Two of them turned out to be the wrong
instrument, and saying so is more useful than moving them quietly.

**G2 (1.39× against 1.25×) is a real miss, and the bar is the right *kind* of bar** — a ratio is what ports
across hardware. It is missed on four cells, all at 150–350 steps of defocus with tilt and backfocus both
injected. The reasoning behind the number, though, was about *absolute* time: the render is prefetched at
`StartExposure`, so its cost is invisible while render ≤ exposure. At 880 ms against a 5 s exposure there is
5.7× of headroom, and under contention the ratio falls to 1.14×. A machine would have to be roughly 5×
slower than this one before the feature became visible — and at that point the 634 ms *baseline* render is
already marginal, so the astigmatism model is not what breaks it. Recorded as accepted, not as passed.

**G4 (28 % against 10 %) measures the wrong thing.** It was written as "the only phase that is pure added
overhead", to catch kernel generation running away. But the share is a ratio against a total that is
*dominated by development* — so making development faster would fail this gate while making the render
strictly better. The 250 ms of kernel generation it flags is the same 250 ms G2 and G3 already price
correctly. Superseded by G2/G3; not carried forward.

**G5 (743 against 512) fires only on `on-strong`,** a 40 µm corner residual chosen to be several times what
a decent flattener shows. At the shipped default the worst is 409. The count was a proxy for memory, and
memory is measured directly by G6 at 129 MB of a 256 MB bar — with the in-code budget enforcing it exactly
rather than approximately. Keep G6; treat G5 as informational.

**Why every ratio moved up slightly versus the model this replaced.** The earlier local-spacing model made
the split proportional to a term that was near-zero over most of a well-spaced sensor, so it built fewer
elliptical kernels for the same aberration config. The current model splits the surface wherever there is
*any* curvature or residual, which is more kernels and — the point of the change — actually correct. The
cost of that correctness is the ~4 % the headline ratio moved, from 1.33× to 1.39×.

## 7. Mitigations — measured, not proposed

Each was implemented and measured on the dense-wide 350/A2 cell. Every one is fidelity-free: none changes a
rendered pixel at fixed inputs.

| # | change | total | on/off | kernelGen |
|---|---|---|---|---|
| 0 | first working implementation | 1151 ms | 1.58× | 418 ms |
| 1 | + pool the fine-grid buffers (`ArrayPool`) | 953 ms | 1.48× | 327 ms |
| 2 | + vectorize the separable convolution | 845 ms | 1.23× | 210 ms |
| 3 | final, with the exact cache budget | 848 ms | 1.33× | 216 ms |
| 4 | model corrected to the Seidel + residual form | 880 ms | 1.39× | 250 ms |

*(Rows 2 and 3 differ in ratio, not in absolute cost: the `off` baseline in row 2's run measured 685 ms
against 639 ms in the final matrix, which is run-to-run variance on the isotropic arm. Row 4 is the shipped
model — see [Gates](#on-the-three-that-missed) for why it costs slightly more than row 3 and why that is
the right trade.)*

1. **Parallel kernel building** was mandatory from the start, not an optimization applied later: 409 kernels
   at ~14 ms each is 5.7 s serial, against a render that runs in well under one. Split `BuildStampJobs` into
   key assignment / parallel build over the distinct keys / job assembly. Deterministic — the key list is in
   deterministic star order, each kernel is a pure function of its key, and every write lands in its own
   pre-indexed slot.
2. **Pooling the fine-grid buffers.** Each is ~360 KB, past the 85 KB large-object-heap threshold, so a frame
   building a few hundred kernels churned hundreds of megabytes through a heap that is neither compacted nor
   cheap. `ArrayPool<float>` took kernel generation from 418 ms to 327 ms.
3. **Vectorizing the convolution.** Written tap-outer rather than tap-inner, so a fixed tap is a straight
   shifted multiply-accumulate over x that vectorizes with no gather. 327 ms → 210 ms. Each output still
   accumulates its taps in the same order whatever the vector width, so the result does not depend on which
   SIMD path the CPU offers — which matters, because `Render` is contractually pure and its byte-for-byte
   determinism is asserted.
4. **Cheap interior/exterior cell classification** before the sub-sampled rim ramp. Sound in principle — only
   cells straddling a rim need sub-sampling — but worth only ~10 ms here, because the adaptive sub-sample
   count is already 1 for most kernels. Kept: it is what keeps a near-line focus from paying 64 evaluations
   on every cell.

### Three budget bugs found after the matrix was recorded

All surfaced from a user report of large backfocus errors rendering with no eccentricity at all. None
changes the numbers above — the headline cell re-measures at 409 kernels and 129.4 MB throughout — but all
three were the budget failing to do what it claimed.

1. **The fallback did not bound anything.** When even maximum coarsening would not fit, the code dropped
   astigmatism *and reverted to the fine defocus quantum*. Dropping astigmatism is not a way to fit a byte
   budget: the dominant term is (number of defocus levels × kernel size) and astigmatism controls neither. The
   10 mm case built **822 kernels at 3.2 GB in 5.4 s** on the code path whose purpose was to prevent exactly
   that. The ladder is now two passes — coarsen with astigmatism, then coarsen again without — keeping the
   coarsened quantum either way: **52 kernels, 201 MB, 1.0 s**. When even circular donuts at maximum
   coarsening do not fit, it renders and says so rather than claiming a budget it did not honour. The
   isotropic path was exposed to this too and always had been; the phase instrumentation is what made it
   visible.
2. **The estimate counted only the phase bank.** The two profile LUTs have a 512-entry floor, so a frame made
   of thousands of *small* kernels pays ~8 KB each for them. A 500 µm backfocus error measured **131.9 MB
   against a 128 MB budget** — over, while reporting itself as fitting. The LUTs are now counted.
3. **The budget itself was too small, and turned the feature off exactly when it would have been most
   visible.** Kernel size grows as R², so at a large injected backfocus error the kernels are individually
   enormous — 7 MB at R = 108 — and 128 MB admitted only about eighteen of them, fewer than the distinct
   (Δ, A) pairs a smoothly curved field needs. A 5 mm corner-curvature error asked for 331 MB, was refused,
   and rendered **circular donuts**: a user dialling the error up to see the effect better saw it vanish.
   Raised to 384 MB, which covers the model's useful envelope (astigmatism now survives to a 5 mm error on a
   full-frame f/7 rig and gives up at 10 mm, where it would need 3 GB and the stars are 450 px across).
   Bytes turn out to be a good proxy for generation time as well — roughly 2 ms per MB — so the same ceiling
   bounds the render cost at about 770 ms of kernel generation in the worst case.

   A related correctness fix went in alongside: coarsening was allowed to collapse the orientation to a
   **single bin**, which does not render a coarse ellipse field but renders *every* ellipse at the same
   position angle — worse than no astigmatism, because it looks like a real pattern and is not one. Bins
   now have a floor of four while astigmatism is on, so such an attempt fails the budget and falls through
   to honest circular donuts instead.

### Levers evaluated and rejected

- **Fewer kernel-build threads.** Tested at 48 / 24 / 12 / 6: kernel generation measured 275 / 356 / 364 /
  591 ms. Full parallelism is strictly best, so the shared governor's default is already right. The residual
  gap to ideal scaling (409 kernels × 14 ms ÷ 48 ≈ 120 ms against 250 ms measured) is imperfect scaling, not
  oversubscription.
- **Coarser orientation binning.** Would halve the kernel count, but the bins resolve to 14–22 — nowhere near
  the cap — so the count is driven by realized defocus level pairs, and thinning the angular budget would
  cost fidelity for a fraction of the win it appeared to promise.
- **A cross-frame kernel memo.** A 9-point AF sweep rebuilds the same kernels nine times, so a process-level
  memo keyed on the full model identity (σ, N, p, ε, S, cache key) would remove most of the cost of a sweep.
  Not implemented: it changes timing only, never pixels, but it is a lifetime-and-invalidation problem that
  belongs in its own change rather than bolted onto this one. **Recorded as the highest-value follow-up.**

## 8. What shipped

The model ships enabled, with a 15 µm corner astigmatism and a 50 µm backfocus error default. On a clean,
well-corrected rig it is free; on a mis-spaced or tilted one it costs 39 % of a render that takes 0.88 s,
hidden entirely behind an exposure. The cache budget, the coarsening ladder, and the circular-donut fallback
are in place and were never triggered at the shipped default — they exist for configurations past anything a
real corrector produces.

## 9. Reproducing

```bash
# Release, run from the repo root via Windows dotnet.exe over WSL interop.
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Release --nologo
DLL=$(wslpath -w Joko.NINA.Plugins/TestApp/bin/Release/net8.0-windows7.0/TestApp.dll)

dotnet.exe "$DLL" bench-simrender --kernel-ladder --iters 5              # seconds, no catalog needed
dotnet.exe "$DLL" bench-simrender --census --field all                   # star and kernel counts only
dotnet.exe "$DLL" bench-simrender --field all --iters 5 --csv out.csv    # the full matrix, ~8 min
dotnet.exe "$DLL" bench-simrender --field dense-wide --aberr A2 \
    --defocus-steps 350 --arms off,on --iters 5 --with-detection         # the contention cell
```
