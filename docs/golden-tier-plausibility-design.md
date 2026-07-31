# Golden tier plausibility — fixing the SNR≥12 auto-confirm inversion (F16)

**Status:** design, approved 2026-07-30. Implementation plan: `plans/golden-tier-plausibility-plan.md`.
**Supersedes the "next step" recorded in `docs/followups.md` F16** — integrated SNR was measured and does not work.

## 1. The problem

`build_goldens.py` auto-confirms every SNR reference candidate at `snr >= 12` without LLM QA, on the assumption
that a high-SNR candidate is a real star. On heavily-defocused runs that assumption inverts: the gate keeps
compact noise spikes and discards the real donuts. Two runs (`lumos`, `SorenVance`) produced goldens so wrong they
had to be quarantined, and both are unscoreable today.

The original F16 evidence stands. What follows is what the prototyping added or corrected.

### 1.1 The reference is matched-filter dominated, and the filter fires at noise level

| run | total candidates | connected-component | matched filter | MF median response (`donut_k` = 6.0) |
|---|---|---|---|---|
| `lumos` | 617,828 / 23 frames | 107,012 | **510,816** (83%) | **6.58** |
| `SorenVance` | 158,744 / 6 frames | 61,887 | 96,857 (61%) | 7.75 |
| `FlyData` | 20,726 / 9 frames | 7,637 | 13,089 (63%) | 6.73 |

On `lumos` that is ~22,200 matched-filter detections per frame with the bulk sitting on the threshold. Real
donuts are the ~170/frame above response 12.

### 1.2 `lumos`'s compact path has no focus signature at all

Per-frame connected-component counts across the whole 23-point sweep are flat — 4,470 / 4,463 / 4,564 / … /
4,621, with `snr >= 12` pinned near 2,100 and **median area exactly 3 px on every frame**. A real star field
cannot do that; near-focus frames must yield more compact high-SNR components than the wings. Healthy runs show
exactly that curve:

| run | candidate count across sweep | peak at |
|---|---|---|
| `FlyData` | 623 → **1,168** → 714 | best focus (foc 889) |
| `vsn07` | 143 → **1,004** → 193 | best focus (foc 9893) |
| `lumos` | 4,470 → 4,601 → 4,621 (flat) | — none — |

**Flatness of the reference candidate count against focuser position is the single cheapest tell that a golden
is unsound**, and it is the basis of the validator in §4.1.

### 1.3 Ruled out: hot pixels, and a mis-estimated σ

Both were plausible and both are wrong, so neither should be re-investigated.

*Not fixed-pattern.* Keyed to 2 px cells, **zero** auto-confirmed `lumos` sites recur in ≥11 of 23 frames, and
68.3% appear in exactly one frame. `SorenVance`: 1.4% present in all 6 frames, 79.0% in exactly one. These are
transient per-frame events, not hot or telegraph pixels with a fixed position.

*σ is sound.* The block-MAD estimate matches an independent adjacent-pixel-difference estimate, and the
normalised residual has unit width on every frame tested:

| frame | σ (block MAD) | σ (adjacent diff) | z-width |
|---|---|---|---|
| `lumos@124056` | 99.33 | 103.79 | 1.00 |
| `FlyData@889` | 8.90 | 9.44 | 1.00 |
| `SorenVance@28895` | 2.97 | 1.05 | 1.00 |

`SorenVance` is the one case where the two disagree, and in the direction explained by debayer interpolation
correlating adjacent pixels (lag-2 recovers to 2.10). The block-MAD value is the honest one. So the candidates
really are ≥12σ peaks — the measurement is right and the interpretation is wrong.

*They are structureless.* Ratio of annulus flux to core flux, over auto-confirmed candidates:

| frame | ring/core median | fraction with no wings (<0.1) |
|---|---|---|
| `lumos@209735` | **+0.15** | **43.5%** |
| `FlyData@889` | +0.80 | 2.0% |

## 2. What was ruled out: integrated SNR

F16 proposed tiering on integrated SNR (flux ÷ noise over the candidate's own footprint) so a large faint donut
and a small bright star rank comparably. **Measured on real components, it does not work and is marginally worse
than what we have:**

| `lumos@209735` | kept at ≥12 | ≤4 px | corr(box, metric) |
|---|---|---|---|
| peak SNR (current) | 2,390 | 88.1% | −0.448 |
| integrated SNR | 2,445 | **88.0%** | **−0.463** |

| `FlyData@889` | kept at ≥12 | ≤4 px |
|---|---|---|
| peak SNR (current) | 1,048 | 16.7% |
| integrated SNR | 1,238 | **29.0%** |

Raising the integrated threshold does not recover the donuts until ≥40, where only 22 candidates per frame
survive — not a usable reference.

**Why it cannot work.** A 3 px spike at 12σ peak has integrated significance ≈ 12·√3 ≈ 20. A 36 px donut spread
at 0.25σ/px has ≈ 8. Integrated SNR is the *statistically correct* ranking and it still puts the spike on top,
because the spike genuinely is the more significant detection. **No purely significance-based statistic can fix
this.** The gate's error is using significance as a proxy for "is a star". Only morphology, scale consistency, or
a human/LLM look can reject a significant non-star.

This is the central claim of the design and everything below follows from it.

## 3. Decisions

| decision | choice |
|---|---|
| tier definition | **Unchanged.** "high" remains `snr >= 12`, so `recall@SNR≥12` keeps its exact meaning. |
| auto-confirm, non-donut runs | Narrowed by a **star-plausibility gate** — significance is necessary but no longer sufficient. |
| auto-confirm, `--donut` runs | **Removed.** The high tier is QA'd like any other tier. |
| gate placement | **Priority ordering only.** Nothing is excluded; the gate reorders the QA worklist so the montage budget lands on plausible candidates first. |
| unresolved candidates | **Third state**, excluded from both recall and precision denominators. |
| rebuild scope | **All six donut-aware runs**: `lumos`, `SorenVance`, `FlyData`, `LinwoodFocus`, `mufti`, `Panos`. |

Priority ordering rather than a pre-filter matters because it means the gate never permanently loses a real
donut it mis-scores — a mis-scored candidate is demoted in the queue, not deleted, and whatever the budget did
not reach is recorded as unresolved rather than silently decided.

## 4. Architecture

### 4.1 `tools/golden/golden_health.py` (new) — validate an existing golden, with no re-read

Reads `<frame>.golden.json` only: no FITS, no `snr_*.json`, no LLM. This is what makes "audit before rebuild"
possible and gives every already-built golden a trust verdict.

Signals, per run:

- **Size collapse** — fraction of the high tier with `max(w,h) <= 4 px`.
- **Tier–size inversion** — per-frame median width of the high tier ÷ that of the QA-confirmed tier, then the
  median across frames. A sound tiering gives ≥1 (brighter stars really are bigger); the F16 pathology gives <1.
- **Focus response** — dynamic range of the high-tier count and of the median high-tier width across the sweep.

Measured over the bank as it stood before the rebuild:

| run | ≤4 px in high tier | per-frame width ratio | verdict |
|---|---|---|---|
| `lumos` (quarantined) | **91%** | 1.00 | collapsed |
| `SorenVance` (quarantined) | **66%** | 1.00 | collapsed |
| `LinwoodFocus` | 9% | **0.71** | **inverted** — high tier 13 px vs QA tier 28 px |
| `Panos` | 9% | 1.00 | flat; MF radius quantization makes this inconclusive |
| `mufti` | 0% | 1.40 | clean |
| `FlyData` | 10% | 2.00 | clean |
| all 11 others | ≤34% | ≥1.0 | clean |

`LinwoodFocus` is the find: its real donuts sit in the QA tier while smaller things were auto-confirmed into
"high". Its historical `recall@SNR≥12` measures the wrong star population and is not salvageable by rescoring.

The validator is not a substitute for the fix — it is the instrument that says which goldens the fix must touch,
and the regression gate afterwards.

### 4.2 `tools/golden/snr_ref.py` — emit what it already computes

Per candidate, add:

- `flux` — already computed as `tot` at line 101 and discarded. Required by the scale estimator.
- `src` — `"cc"` | `"mf"`, so the two detection paths are distinguishable.
- `snrKind` — `"peak"` | `"matched"`. The `snr` field currently carries two incomparable quantities under one
  name: peak/σ for connected components, disk-integrated response for the matched filter. Naming them is a
  prerequisite for any correct downstream logic.

No behavioural change; existing thresholds keep their meaning.

### 4.3 `tools/golden/golden_prep.py` — frame scale, plausibility, priority worklist

**Frame star-scale `S`** = median `max(bw,bh)` of the top `max(20, 0.5%)` candidates by flux, **over the CC+MF
union**. Both the union and the flux ranking are load-bearing:

| frame | CC-only | CC+MF union | plain median (all cands) |
|---|---|---|---|
| `lumos@209735` | **3.0 px** — collapses to the spike size | **36.0 px** ✅ | 36.0 |
| `vsn07@9893` | 10.0 ✅ | 10.0 ✅ | **3.0** ✗ |
| `FlyData@889` | 13.0 ✅ | 13.0 ✅ | **20.0** ✗ |

On `lumos` nearly every real star is found only by the matched filter, so a CC-only scale collapses to 3 px and
the gate silently becomes a no-op that still reports success. The plain median fails in the other direction on
clean runs. Flux-weighted median was also tried and agrees within 3 px everywhere; top-N is marginally more
stable and is what the design specifies.

**Guard (required).** If `S` lands at the minimum component size, abort with a diagnostic rather than proceed —
that is the CC-only trap above, and it is also what happens if a deep run is prepped without `--donut`.

**Plausibility score** = `max(bw,bh) / S`, used for ordering, and as a gate at `>= 0.3` where a gate is needed.
Threshold validation:

| frame | S | keep at `box >= 0.3·S` | ≤4 px after |
|---|---|---|---|
| `lumos@209735` (broken) | 36 px | **10.9%** | **0.0%** |
| `FlyData@889` | 13 px | 93.9% | 11.3% |
| `FlyData@1264` | 20 px | 98.2% | 0.0% |
| `vsn07@9893` (clean, at focus) | 10 px | 87.8% | 54.9% |
| `vsn07@10058` (clean, wing) | 26 px | 97.0% | 0.0% |

It strips 89% of `lumos`'s high tier — all of it compact spikes — while preserving 88–98% on clean runs.

**A fixed morphology threshold was rejected.** Effective area `flux/peak` separates the two populations in
principle (`lumos` p50 = 1.7, `vsn07` at-focus p50 = 3.9) but the distributions overlap badly: a hard
`Aeff >= 4` would reject **52% of real near-focus stars** on clean `vsn07`. Any plausibility measure must be
relative to the frame's own scale, never an absolute pixel or concentration cut.

**Behaviour by mode:**

- non-`--donut`: auto-confirm requires `snr >= 12` **and** `box >= 0.3·S`. On `vsn07` this moves ~12% of the high
  tier from auto-confirm into QA.
- `--donut`: no auto-confirm at all. The whole candidate list, high tier included, enters the QA worklist.
- worklist ordered by plausibility descending, then SNR descending.

### 4.4 `tools/golden/build_qa_worklist.py` — the prefix invariant breaks

`build_qa_worklist.py:40` encodes each frame's worklist base as `b = fr['high']`, relying on the auto-confirmed
high tier being a contiguous SNR-descending prefix of `snr_<foc>.json`. Both changes above break that: with
auto-confirm off there is no prefix to skip, and plausibility ordering is not SNR ordering. The bridge must carry
explicit global indices per montage instead of a base offset. This is a correctness bug the moment either change
lands, not a cleanup.

### 4.5 Sidecar schema v2 — three-state resolution and coverage

Each candidate resolves to **confirmed**, **rejected**, or **unresolved**. Golden `stars[]` holds confirmed only.
Added to the frame sidecar:

- `unresolved[]` — boxes never examined, in the same box format as `stars[]`.
- `coverage` — per tier, `examined` / `total`, so a consumer can tell whether a precision figure is a measurement
  or a bound. This also closes the recording half of **F11**, independently of any re-run.
- `qaVersion` / `qaVotes` — see §6.

**A missing `unresolved` array reads as empty**, so every existing v1 golden scores exactly as it does today.
Backward compatibility is by construction, not by care.

### 4.6 `TestApp golden eval` — honour the unresolved set

Exclude unresolved regions from both the recall and the precision denominator: an HF detection landing on an
unresolved box is neither a true positive nor a false positive, and an unresolved box is not a missed star.
`GoldenStarSet.cs` already carries `schemaVersion` and an optional-field precedent (`coveredTiles`) to extend,
and has test coverage in `Golden/GoldenStarSetTests.cs`.

## 5. What survives, what is superseded

| runs | effect |
|---|---|
| 13 non-donut runs with goldens | **Untouched.** v1 sidecars, empty unresolved set, identical scores. Fully comparable to history. |
| `lumos`, `SorenVance` | Already quarantined and scoring NaN — nothing to lose. |
| `LinwoodFocus` | **Superseded.** Tiering inverted; past `recall@SNR≥12` measured the wrong population. |
| `Panos`, `mufti`, `FlyData` | **Superseded on rebuild.** Validator says `mufti`/`FlyData` are sound and `Panos` inconclusive, but the unresolved denominator shifts their numbers, so old figures must not be mixed with new. |

`recall@SNR≥12` keeps its definition throughout — the tier is still `snr >= 12`; only *membership* changes from
auto-confirmed to QA-resolved. Cross-run comparison stays valid within each group, and any report mixing a
rebuilt run with a v1 run must say so.

The six donut-aware runs were identified from matched-filter radius quantization in their stored goldens (box
widths land on 12/20/28/36 px = 2r for r ∈ {6,10,14,18}); confirm at rebuild.

## 6. QA reproducibility — a consequence that needs an answer

Three bank folders (`sensitivity_example1/2`, `standard_example2`) turned out to be independent golden
generations over the same frames rather than copies, which yields a free measurement:

| pair | high tier (auto-confirmed) | QA-confirmed tier | count disagreement |
|---|---|---|---|
| `LinwoodFocus` / `sensitivity_example2` | Jaccard **1.0000** | **0.6202** | 11.3% |
| `CWhiteFocus` / `standard_example2` | Jaccard **1.0000** | 0.8880 | 5.9% |
| `fmeschia_Focus` / `sensitivity_example1` | Jaccard **1.0000** | 0.9697 | 4.3% |

Auto-confirm is bit-for-bit reproducible. The LLM QA pass is not, and the **worst pair is the deeply-defocused
run at 0.62** — precisely the regime donut runs occupy. Moving the high tier into QA therefore trades a
deterministic-but-wrong reference for a correct-but-stochastic one, and `recall@SNR≥12` on donut runs stops being
reproducible run to run.

**Answer:** QA the high tier with **3 independent votes per candidate, confirmed on a 2-of-3 majority**, and
record `qaVotes` plus a `qaVersion` (model + prompt revision) in the sidecar. Majority voting over three
Bernoulli votes lifts a per-vote agreement of ~0.8 to ~0.9 at the candidate level, and the recorded version makes
a reproducibility regression attributable rather than mysterious. Cost is 3× on the high tier only; the uncertain
tier stays single-vote, as its budget truncation already dominates its error.

The three source folders were deleted at the user's request (they double-counted in validation sweeps). Their
non-regenerable content — 27 goldens, both `settings.txt` notes, and hand-curated review labels — is archived at
`_prior_reports/deleted_example_runs_20260730/`.

## 7. Cost

Measured QA rate: 236 montages in 27 min at chunk 4 → 8.74 montages/min, 36 cells each → ~314 candidates/min.

**Cost is `budget-montages × frames`, not the high-tier size.** An earlier version of this table sized each run
by its high tier, which is wrong: with auto-confirm off, the *entire* candidate list enters the worklist, and
what actually bounds the work is the per-frame montage cap. `LinwoodFocus` measured 540 montages at
`--budget-montages 60`, against the 24 the old table predicted from its 865-star high tier — a 20× error.

| run | frames | montages @20 | QA @1 vote | montages @60 | QA @1 vote | candidates/frame |
|---|---|---|---|---|---|---|
| `lumos` | 23 | 460 | ~53 min | 1,380 | ~158 min | ~26,000 |
| `LinwoodFocus` | 9 | 180 | ~21 min | 540 | ~62 min | ~5,000–7,300 |
| `FlyData` | 9 | 180 | ~21 min | 540 | ~62 min | ~2,300 |
| `Panos` | 7 | 140 | ~16 min | 420 | ~48 min | — |
| `mufti` | 7 | 140 | ~16 min | 420 | ~48 min | — |
| `SorenVance` | 6 | 120 | ~14 min | 360 | ~41 min | ~26,000 |
| **total** | **61** | **1,220** | **~2.3 hr** | **3,660** | **~7.0 hr** | |

Multiply by the vote count. Plus ~2 hr of `snr_ref` across the six runs.

**Plausibility ordering is what makes a small budget defensible.** The queue spends itself on the largest,
most star-like candidates first, so the first N montages carry far more tier-assignment information than a
random N would — which was not true under the old SNR ordering, where the queue led with pixel-scale spikes.
Raising the budget mostly buys coverage on the shallow runs; `lumos` and `SorenVance` have ~26,000 candidates
per frame and stay budget-bound at any affordable setting.

**Chosen for the initial rebuild: 20 montages/frame, 1 vote** (~2.3 hr total). Single-vote QA is not
reproducible — see §6 — so every sidecar records `qaVotes: 1`, making the limitation visible to any consumer
rather than silent. Re-running at higher vote counts later is purely additive.

## 8. Risks and limitations

- **`donut_radii` is hardcoded `(6,10,14,18)`**, capping any measured scale at 36 px. `lumos` and `mufti` both sit
  *at* that cap, so their true donuts may exceed what the reference can represent, and `S` is then a lower bound.
  Out of scope here; filed as a follow-up.
- **`0.3·S` is calibrated on five frames from three runs.** It should be re-validated per run at rebuild, and the
  validator's own metrics act as the check.
- **The scale estimator depends on the matched filter finding the donuts.** The §4.3 guard converts that from a
  silent failure into a loud one, but a run where the MF genuinely misses every donut cannot be prepped correctly
  and must fail rather than emit a golden.
- **Priority ordering does not create budget.** On `lumos` the uncertain tier is 564,813 candidates; ordering
  decides what gets examined first, not how much. Coverage recording (§4.5) is what keeps the resulting precision
  figure honest.
- `corr(bbox, snr)` was considered as the headline health metric and rejected: it is **+0.29** on clean `vsn07`
  and −0.05 on `FlyData`, so "≈0" is not the sound-tiering signature — brighter stars genuinely are bigger. It
  also cannot be computed from a stored golden, which carries no SNR. The §4.1 metrics replace it.

## 9. Acceptance criteria

1. `golden_health.py` flags `lumos`, `SorenVance` and `LinwoodFocus` as unsound and passes the 13 untouched runs,
   from stored sidecars alone.
2. Rebuilt `FlyData` retains ≥85% of its currently-confirmed high tier — the fix must not be a recall regression
   on a run that was already sound. `vsn07` is not in the rebuild scope but *is* affected by the narrowed
   auto-confirm, so it is prepped into scratch and diffed against its shipped sidecar without replacing it;
   same ≥85% bar.
3. Rebuilt `lumos` and `SorenVance` produce goldens with <10% of the high tier at ≤4 px and a per-frame width
   ratio ≥1.0, and both become scoreable.
4. Every v1 sidecar scores bit-identically before and after the `golden eval` change.
5. The scale guard fires on a `--donut`-less prep of `lumos` rather than emitting a golden.
6. `dotnet test` passes.

## 9a. What execution changed (2026-07-30)

Three things were wrong in this design and were found only by rendering real output. Recorded here so the
reasoning is not re-derived.

**The cost model in §7 was wrong by ~20×** — corrected in place. Cost is `budget-montages × frames`, not
high-tier size.

**Plausibility had to saturate, and the two paths had to be interleaved.** `box / scale` grows without limit, so
36 px matched-filter responses outranked the real 13 px stars on near-focus frames; rendering the queue showed
**719 of 720 crops were noise**. Capping at 1.0 fixes the ranking (and cannot affect the auto-confirm gate, since
everything it clips was already far above 0.3). Interleaving is needed because the matched filter emits ~10× more
candidates and otherwise takes the whole budget once defocus puts both paths at plausibility 1.0. Plausibility
must remain the *primary* key — plain round-robin lets a 0.083 spike jump a 1.0 donut.

**`donut_k` = 6.0 was inside the noise.** Median response 6.41 against a 6.0 threshold; confirmed donuts min 7.48
/ median 14.46. At 8.0 the pool drops 94% for ~7% of real donuts (a *lower* bound — the confirmed sample came
from the 6.0 pool, so it is blind to donuts never proposed). This mattered more than the tiering work for yield:
`LinwoodFocus` went from 116 to **716** confirmations at identical QA cost.

**The tier assignment still mixed two quantities.** §4.2 named `snrKind` but `tier()` kept bucketing the raw
`snr`, which meant peak/σ for connected components and disk-integrated response for the matched filter. On
`Panos` that put 36 px donuts in the medium tier and smaller components in the high tier — `TIER-INVERSION` on a
freshly rebuilt golden. Matched-filter candidates now measure a real peak. **The lesson generalises: naming the
two scales was not enough; every consumer of `snr` had to be checked.**

**Coverage bookkeeping had a silent-overstatement bug.** `persist_qa` marked the whole `qaorder` examined
regardless of how many montages completed, which would have inflated the one field schema v2 exists to report
honestly. The workflow now returns a completed-montage count.

## 10. Out of scope

- Scaling `donut_radii` to the run's defocus (follow-up).
- The F11 uncertain-tier re-runs at larger `--budget-montages`. This design adds the coverage *recording* F11
  asked for; the re-runs remain a separate, orthogonal cost decision.
- Any change to the HocusFocus detector itself. This is entirely about the reference the detector is scored
  against.
