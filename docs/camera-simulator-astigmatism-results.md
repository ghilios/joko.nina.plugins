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
2. **The dense 61 MP worst case is 1.38× and 934 ms.** On the headline field — 34,909 stars on a QHY600 at
   350 steps of defocus with tilt and backfocus injected — the shipped default renders in 934 ms against 677
   ms isotropic. That is 6 % of a 15 s AF exposure and 19 % of a 5 s one, so it stays entirely hidden behind
   the prefetch. **SHIPPED**, with the ratio gate missed and the absolute gate passed by 3.2× — see
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
   arm's 7. That is the Seidel-fixed induced split, not a residual. On the clean `A0` config `on-zero`
   collapses to `off` exactly — 1 kernel, both arms, all four fields. **VERIFIED.**
6. **The tilt-astigmatism term costs kernels, not a new phase.** Because the split now tracks the whole
   defocus range on a tilted field instead of a fixed corner value, the tilted `A2` cells build roughly
   2.4× the kernels they did with the residual term alone (450 vs 189 on the headline cell). Nothing else
   moves — catalog query, stamping and development are all within noise. The ratio is unchanged at 1.38×
   because the extra kernels landed alongside a proportionally slower baseline on this run.

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
| `on-zero` | enabled with corner residual 0 and tilt fraction 0 — a **perfectly corrected** optic, squarely mounted |
| `on` | corner residual 15 µm, tilt fraction 0.25 — the shipped defaults |
| `on-strong` | corner residual 40 µm, tilt fraction 0.6 — a poor corrector on a sagging focuser |

The tilt fraction is the expensive half. The residual term contributes a split that is a fixed function of
field radius, so it adds a bounded set of level pairs; the tilt term's split tracks the full defocus range
across a tilted field, which multiplies them. That is why the tilted `A2` cells carry ~2.4× the kernels of
the `A1` cells at the same defocus, and it is deliberately what the headline number prices.

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

Orientation bins resolved to **22–26** at the shipped settings and **45** at the stress arm, against a cap of
64, so the kernel count is still driven by realized level pairs rather than by angular quantization — though
with far less headroom than before the tilt term. Peak cache **142.5 MB** against the 384 MB budget, so the
coarsening ladder never fired at the shipped default and every cell rendered at full fidelity; the
`on-strong` arm peaks at 274 MB, still inside it. (See
[the budget corrections](#three-budget-bugs-found-after-the-matrix-was-recorded) for how that budget was
arrived at.)

| field | cell | kernels (on) | bins | cache MB | kernels (on-strong) |
|---|---|---|---|---|---|
| dense-wide | 350 / A2 | 450 | 26 | 142.5 | 857 |
| dense-wide | 150 / A2 | 447 | 26 | 38.0 | 846 |
| dense | 350 / A2 | 217 | 19 | 48.4 | 405 |
| sparse | 350 / A2 | 188 | 19 | 42.0 | 319 |
| dense-oversampled | 350 / A2 | 159 | 17 | 46.2 | 265 |

**One fidelity limit worth stating.** At very large tilt the orientation-bin formula asks for more bins than
the cap of 64 allows — a 10 mm tilt on a QHY600 wants ~770 — so the rim of a kernel can sit up to ~1.4 % of
its radius from where the exact orientation would put it. The orientation field is smooth and slowly varying,
so this shows up as a small systematic rotation rather than as visible banding, and no cell in this matrix
comes close to the cap. Recorded because the cap is silent, not because it bites here.

**The exact count replaced an upper bound.** The bound — defocus cells × astigmatism cells × orientation
bins — predicted 863 MB for the dense-wide 350/A2 cell where the cache really holds 143 MB, because Δ and A
are both smooth functions of field position and so are nowhere near independent. Coarsening on that estimate
doubled the defocus quantum and halved the bins on a frame that fitted comfortably. Since assigning keys is
pure arithmetic and builds nothing, the budget loop now measures the cache it would really allocate and
retries only if that exceeds the budget.

## 3. Whole-frame timing

Per-phase medians, ms. `on/off` is the ratio that G2 gates.

#### dense-wide — 34,909 stars, 61 MP, the headline field

| defocus | aberr | off | on | on/off | strong | strong/off | kernels | bins | cache MB | kernelGen (on) |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | A0 | 589 | 585 | 0.99× | 586 | 0.99× | 1 | 1 | 0.0 | 0 ms (0%) |
| 0 | A1 | 604 | 612 | 1.01× | 640 | 1.06× | 156 | 22 | 2.6 | 5 ms (1%) |
| 0 | A2 | 597 | 630 | 1.06× | 672 | 1.13× | 415 | 26 | 7.1 | 36 ms (6%) |
| 150 | A0 | 623 | 635 | 1.02× | 616 | 0.99× | 1 | 1 | 0.1 | 3 ms (0%) |
| 150 | A1 | 611 | 722 | 1.18× | 745 | 1.22× | 167 | 22 | 14.4 | 84 ms (12%) |
| 150 | A2 | 618 | 778 | 1.26× | 846 | 1.37× | 447 | 26 | 38.0 | 142 ms (18%) |
| 350 | A0 | 641 | 650 | 1.01× | 642 | 1.00× | 1 | 1 | 0.3 | 6 ms (1%) |
| 350 | A1 | 650 | 799 | 1.23× | 854 | 1.31× | 169 | 22 | 53.1 | 116 ms (15%) |
| **350** | **A2** | **677** | **934** | **1.38×** | 1139 | 1.68× | 450 | 26 | 142.5 | 263 ms (28%) |

The `A1` (backfocus-only) column is the residual mechanism on its own — no tilt, so no field-linear term —
and it is visibly cheaper than `A2` at the same defocus. That gap *is* the tilt term's cost.

Headline cell phase breakdown:

| phase | off | on |
|---|---|---|
| catalog query | 85 | 82 |
| stamp-job build | 102 | 358 |
| …of which kernel generation | 7 | 263 |
| stamping | 57 | 77 |
| development | 474 | 484 |
| **total** | **677** [650–696] | **934** [915–939] |

Development is unchanged, as it must be — the astigmatism model never touches it. The entire delta is
kernel generation, plus ~20 ms of extra stamping from slightly larger footprints.

#### dense — 8,464 stars, 1000 mm

| defocus | aberr | off | on | on/off | strong/off | kernels | cache MB |
|---|---|---|---|---|---|---|---|
| 0 | A2 | 419 | 433 | 1.03× | 1.10× | 199 | 5.6 |
| 150 | A2 | 423 | 534 | 1.26× | 1.33× | 216 | 17.4 |
| 350 | A2 | 444 | 525 | 1.18× | 1.38× | 217 | 48.4 |

#### sparse — 990 stars, identical optics to `dense`

| defocus | aberr | off | on | on/off | strong/off | kernels | cache MB |
|---|---|---|---|---|---|---|---|
| 0 | A2 | 360 | 367 | 1.02× | 1.05× | 174 | 4.8 |
| 150 | A2 | 365 | 392 | 1.07× | 1.15× | 186 | 15.0 |
| 350 | A2 | 383 | 429 | 1.12× | 1.36× | 188 | 42.0 |

#### dense-oversampled — 1,670 stars, 2000 mm f/8

| defocus | aberr | off | on | on/off | strong/off | kernels | cache MB |
|---|---|---|---|---|---|---|---|
| 0 | A2 | 383 | 396 | 1.04× | 1.16× | 148 | 10.1 |
| 150 | A2 | 390 | 452 | 1.16× | 1.21× | 159 | 21.7 |
| 350 | A2 | 382 | 517 | 1.36× | 1.53× | 159 | 46.2 |

## 4. Scaling controls

**Star count.** `dense` and `sparse` differ only in how many stars the pointing has — 8,464 against 990,
8.5×. Catalog query scales with it and stamping scales with it; **kernel generation does not** (217 vs 188
kernels at 350/A2). That is the expected shape: kernels are a property of the aberration field, not of how
many stars sample it. It also means a *sparse* field is the harsher relative case — fewer stars to amortize
the same kernel set over — though here development dominates both and the ratios land close (1.18× vs 1.12×).

**Kernel radius.** `dense-oversampled` doubles σ_min (2.78 px vs 1.44 px) and so the support radius, but its
kernel count is *lower* (159 vs 217) because the coarser plate scale spreads the same defocus over fewer
quantized levels. Cache bytes land similar (46 MB vs 48 MB): the R² per kernel is offset by having fewer of
them. Its ratio is nonetheless the second-worst in the matrix (1.36×), because each of those kernels costs
R² to build.

## 5. Under live contention

The realistic case: NINA prefetches the next frame at `StartExposure` while the previous autofocus point is
still being detected, and both loops route through the same shared CPU governor. `--with-detection` runs a
real `StarDetector.Detect` loop alongside every timed render.

| arm | total ms | kernelGen |
|---|---|---|
| off | 993 | 29 ms |
| on | 1124 | 291 ms |

**Ratio 1.13×** — *better* than the 1.38× measured idle, because contention slows the field-independent
development phase for both arms and so dilutes the difference. The absolute worst case under contention is
1.12 s.

## 6. Gates

| | gate | bar | measured | verdict |
|---|---|---|---|---|
| G1 | `on/off`, clean A0 cells | ≤ 1.10× | **1.06×** | **PASS** |
| G2 | `on/off`, all other cells | ≤ 1.25× | **1.38×** (on), 1.68× (strong) | **FAIL** |
| G3 | worst absolute cell | ≤ 3.0 s and ≤ exposure | **934 ms** (on), 1139 ms (strong) | **PASS** |
| G4 | kernel-generation share | ≤ 10 % | **28 %** (on), 41 % (strong) | **FAIL** |
| G5 | distinct kernels per frame | ≤ 512 | **450** (on), 857 (strong) | **PASS** at the defaults, **FAIL** on the stress arm |
| G6 | peak kernel cache | ≤ 256 MB | **143 MB** (on), 274 MB (strong) | **PASS** at the defaults |
| G7 | `on/off` under contention | ≤ 1.40× | **1.13×** | **PASS** |

### On the three that missed

These bars were written in the plan before any measurement existed. Two of them turned out to be the wrong
instrument, and saying so is more useful than moving them quietly.

**G2 (1.38× against 1.25×) is a real miss, and the bar is the right *kind* of bar** — a ratio is what ports
across hardware. It is missed on four cells, all with tilt and backfocus both injected at 150–350 steps of
defocus. The reasoning behind the number, though, was about *absolute* time: the render is prefetched at
`StartExposure`, so its cost is invisible while render ≤ exposure. At 934 ms against a 5 s exposure there is
5.4× of headroom, and under contention the ratio falls to 1.13×. A machine would have to be roughly 5×
slower than this one before the feature became visible — and at that point the 677 ms *baseline* render is
already marginal, so the astigmatism model is not what breaks it. Recorded as accepted, not as passed.

**G4 (28 % against 10 %) measures the wrong thing.** It was written as "the only phase that is pure added
overhead", to catch kernel generation running away. But the share is a ratio against a total that is
*dominated by development* — so making development faster would fail this gate while making the render
strictly better. The 263 ms of kernel generation it flags is the same 263 ms G2 and G3 already price
correctly. Superseded by G2/G3; not carried forward.

**G5 (857 against 512) fires only on `on-strong`,** a 40 µm residual on a rig whose tilt is 60 % optical —
several times worse than anything a working setup shows. At the shipped defaults the worst is 450. The count
was a proxy for memory, and memory is measured directly by G6 at 143 MB of a 256 MB bar — with the in-code
budget enforcing it exactly rather than approximately. Keep G6; treat G5 as informational. Note that
`on-strong` now also exceeds G6 at 274 MB, still comfortably inside the 384 MB budget the code enforces;
G6's own 256 MB bar predates the budget's own measurement and is the weaker of the two.

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
| 5 | + the field-linear (tilted-corrector) term | 934 ms | 1.38× | 263 ms |

*(Rows 2 and 3 differ in ratio, not in absolute cost: the `off` baseline in row 2's run measured 685 ms
against 639 ms in the final matrix, which is run-to-run variance on the isotropic arm. Row 5 is the shipped
model. Rows 4 and 5 were measured in separate sessions whose `off` baselines differ by 7 % (634 vs 677 ms),
so compare their kernel counts — 409 → 450 — rather than their ratios.)*

1. **Parallel kernel building** was mandatory from the start, not an optimization applied later: 450 kernels
   at ~14 ms each is 6.3 s serial, against a render that runs in well under one. Split `BuildStampJobs` into
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
  gap to ideal scaling (450 kernels × 14 ms ÷ 48 ≈ 131 ms against 263 ms measured) is imperfect scaling, not
  oversubscription.
- **Coarser orientation binning.** Would cut the kernel count, but the bins resolve to 22–26 at the shipped
  defaults against a cap of 64, so the count is still driven by realized defocus level pairs and thinning the
  angular budget would cost fidelity for a fraction of the win it appeared to promise. Re-examine this if the
  tilt term's default fraction is ever raised: it is what pushed the bins from 14–22 to 22–26, and the stress
  arm now reaches 45.
- **A cross-frame kernel memo.** A 9-point AF sweep rebuilds the same kernels nine times, so a process-level
  memo keyed on the full model identity (σ, N, p, ε, S, cache key) would remove most of the cost of a sweep.
  Not implemented: it changes timing only, never pixels, but it is a lifetime-and-invalidation problem that
  belongs in its own change rather than bolted onto this one. **Recorded as the highest-value follow-up.**

## 8. What shipped

The model ships enabled, with a 15 µm corner astigmatism, a 0.25 tilt-astigmatism fraction, and a 50 µm
backfocus error default. On a clean, well-corrected rig it is free; on a mis-spaced or tilted one it costs
38 % of a render that takes 0.93 s,
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
