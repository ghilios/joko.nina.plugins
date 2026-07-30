# Follow-ups register

Findings that surfaced while doing something else, were deliberately **not** acted on at the time, and would
otherwise be lost. Each entry says what was observed, the evidence, why it matters, and the suggested next step.

**Conventions.** Keep entries short and falsifiable — quote the measurement, not an impression. When one is picked
up, write its spec in `docs/<topic>-design.md` and its plan in `plans/<topic>-plan.md`, then mark the entry
**Done** with the commit or PR that closed it rather than deleting it, so the reasoning stays traceable.

Status: **Open** · **In progress** · **Done** · **Won't fix**

---

## Detector / optimizer behaviour

### F1 — The donut heuristic misses small donuts
**Status:** Open · found 2026-07-30 during the bank donut audit

`BankVerification.Decide` flags a run donut-aware only when
`donutPeakFracMax >= 1.0 AND (extremeFrameMedianHFR >= 9.0 OR extremeDonutBBoxMedianPx >= 24)`. The second clause
is calibrated for **large** donuts and vetoes small ones.

**Evidence.** `FlyData` (frac 10.36, extremeHFR 5.0, bbox 13 → vetoed) shows unmistakable annuli — a bright ring
with a dark centre in nearly every one of the 36 largest stars at its most-defocused frame. Meanwhile `toml999`
(frac **47.17**) and `bobp_m101` (frac **39.00**) are pure filled disks, so `frac` alone predicts nothing: on
saturated runs a flat-topped clipped core mimics the donut signature, which is exactly what the veto was written
to suppress, and there it works correctly.

**Why it matters.** Annularity is a geometric ratio (the central-obstruction shadow), not an absolute size, so a
fast short-focal-length rig produces a genuine ring only ~10–15 px across. A vetoed run is scored with donut
detection off *and* its golden is built without `snr_ref --donut`, and per-pixel SNR under-counts donuts — so the
reference itself is incomplete, not merely the detector config.

**Next step.** Re-calibrate the heavy-defocus clause on a size-independent annularity measure (e.g. central-dip
depth relative to ring flux) rather than HFR/bbox thresholds. It changes the verdict for every run, so it needs
its own spec and a re-audit of the bank. Do **not** simply lower the thresholds — that would re-admit the
saturated-core false positives the veto exists to block.

### F2 — `cwhite_2026`'s donut status is ambiguous
**Status:** Open · needs a human eye

The validation anchor is donut-off because its `frac` is 0.67, below the 1.0 threshold. Its most-defocused stars
show **irregular, partial** central darkening at low SNR — not the clean symmetric rings of `mufti` / `Panos` /
`FlyData`, but not clean filled disks either. It is also the run the veto is named after
("cwhite-style over-flag avoided"), so its appearance informed the original calibration.

**Why it matters.** It is the harness anchor; if its donut status is wrong, the anchor is measured on the wrong
config. Bundle this with F1's re-audit rather than deciding it in isolation.

### F3 — `Wtie` does not prevent star-shedding in general
**Status:** Open · documentation/characterisation

`docs/optimizer-sensitivity-pinning-design.md` frames `Wtie = 0.02` as stopping the optimizer landing on the
star-shedding corner. Measured bank-wide with `Wtie` present (`ec6a1b4`), A/B configs still shed heavily:
`mccomiskey` A → recall@≥12 **0.079** vs C0's 0.871; `timmer` B → **0.055**; `CWhiteFocus` A at sens 50 → 0.327.

This is *correct by construction* — `Wtie` only arbitrates genuine plateaus, and these were real σ improvements —
but the doc should say it prevents shedding chosen **on a σ-wiggle**, not shedding generally.

### F4 — The objective has no sensor-model term
**Status:** Open · potentially the most consequential entry here

J scores curve tightness and star count, with no term for the downstream sensor/tilt fit. So it can pick an
operating point that tightens focus while destroying tilt measurement.

**Evidence.** `mccomiskey` config A: σ_focus 0.460 and J 0.9940, but sensor stars collapse 3,618 → **43**, sensor
R² 0.944 → **0.526**, alignment 9/9 → 7/9. Config B reaches the same σ_focus (0.441) at J 0.9950 while keeping
937 sensor stars and R² 0.954 — **A is dominated by B on every axis**, and J could not tell them apart (Δ 0.001).

**Next step.** Consider a sensor-fit guard or term in the objective, at least for the inspection variant.

### F5 — Donut detection halves σ_focus on a run with **no** donuts
**Status:** Open · unexplained

On `mccomiskey` (max HFR 2.01", no donuts — see the F1 audit), enabling the donut master takes σ_focus
**3.263 → 1.648**, a ~49% improvement, measured by one-at-a-time ablation holding sensitivity and star-clip fixed.

**Why it matters.** If the donut path's morphological processing improves HFR consistency generally rather than
only on rings, that is a free win available to every run — or a sign that something in the non-donut path is
worse than it needs to be. Worth understanding before anyone "optimises" the donut path away.

### F6 — Sensitivity and star-clip act only in combination
**Status:** Open · explains the documented plateau

One-at-a-time ablation on `mccomiskey` σ_focus (adaptive on, donut on, NC 4):

| | `clip 2` | `clip 10` |
|---|---|---|
| **`sens 10`** | 1.648 | 2.960 |
| **`sens 33.3`** | 1.503 | **0.441** |

At clip 2 sensitivity barely matters; at clip 10 it is decisive. Raising clip *hurts* at default sensitivity and
*helps* strongly at high sensitivity. Neither knob does anything useful alone — the objective surface is a
**diagonal valley**, not two independent axes, which is a concrete mechanism for the plateau
`docs/optimizer-sensitivity-pinning-design.md` describes and a hint that a coordinate-wise search is the wrong
shape for this space.

### F7 — Adaptive binarization is not uniformly good for the AF fit
**Status:** Open · first observation under a correct C0

Now that C0 runs as-shipped (`2c91e1b`), enabling `LocallyAdaptiveBinarization` improves σ_focus on some runs and
degrades it on others: `CWhiteFocus` 3.000 → 2.502 and `mufti` 6.917 → 5.894, but `caboose` 1.398 → 4.196 and
`uneven` 14.107 → 20.123. Recall is essentially unchanged (median Δ −0.0003).

The feature was AF-bank-gated on other criteria; this is the first look at it with C0 measuring the shipped
config. Not necessarily wrong — but it should not be assumed uniformly beneficial.

### F8 — Optimizer landings are not reproducible across invocations
**Status:** Open

`bobp_m101` landed at three different points on the same frames and the same corrected pipeline:
`sens 0 / clip 0.25`, `sens 0 / clip 6.875` (prepass A), `sens 30.1 / clip 3.44` (prepass B). Consistent with the
F6 diagonal valley, but it means **a single landing is not evidence** about which corner the optimizer prefers,
and any claim resting on one `optimize` run should be treated as anecdote.

---

## Harness / tooling

### F9 — `bank-verify` cannot pin C0's detector knobs
**Status:** Open

C0 consumes `BuildDefaultStarDetectorParams()` verbatim with no `--sensitivity` / `--star-clip` override. When a
shipped default changes (as in `59d5e59`, sensitivity 2.0 → 10.0), old and new reports are no longer comparable
and there is **no way to reproduce the old configuration** without a scratch build. That is what made the
2026-06-24 anchor comparison untestable as written.

**Next step.** Add explicit overrides mirroring `golden eval`'s, so a historical config can be re-measured.

### F10 — Golden bbox is not a valid donut proxy
**Status:** Done (recorded so it is not re-attempted)

Using the golden star bbox to detect donuts fails: `Panos` is a genuine donut run but its most-defocused golden
frame measures a median bbox of only 12 px, because the SNR reference detects **fragments** of a thin ring rather
than the ring. This is the documented under-counting of defocused donuts in `.claude/docs/golden-star-set.md`.
Use the heuristic's own donut statistics, or render the pixels and classify them.

---

## Bank data quality

### F11 — Precision is a lower bound on runs whose faint tier was budget-truncated → **re-run these with more montages**
**Status:** Open · the largest known data-quality gap in the bank

`build_goldens` auto-confirms the SNR≥12 tier and LLM-QAs only a bounded slice of the uncertain tier, capped by
`golden_prep --budget-montages`. On deep fields that slice is a few percent, so real faint detections HocusFocus
finds are scored as **false positives**. Precision on those runs is a weak lower bound, not a measurement.

**recall@SNR≥12 is unaffected everywhere** — the high tier is auto-confirmed in full — and remains the trustworthy
headline. Only precision (and recall@all) is compromised.

**Measured coverage** (rendered ÷ actual uncertain candidates):

| run | frames | rendered | uncertain | coverage | montages | budget used |
|---|---|---|---|---|---|---|
| `bobp_m101` | 10 | 2,303 | 2,303 | **100%** | 67 | 20/frame |
| `bobp` | 9 | 1,870 | 1,870 | **100%** | 56 | 20/frame |
| `vsn07` | 7 | 1,362 | 1,362 | **100%** | 42 | 20/frame |
| `FlyData` | 9 | 6,480 | 17,215 | **37.6%** | 180 | 20/frame |
| `timmer` | 9 | 6,480 | 117,107 | **5.5%** | 180 | 20/frame |
| `SorenVance` | 6 | 4,320 | 130,932 | **3.3%** | 120 | 20/frame (golden invalid — see F16) |
| `lumos` | 23 | 16,560 | 564,813 | **2.9%** | 460 | 20/frame (golden invalid — see F16) |

**Every pre-existing run in the bank is worse still** — all were built at the skill-default 4 montages/frame,
which hard-caps the faint tier at 144 cells/frame. Their goldens show exactly that fingerprint:

| run | faint stars/frame | cap |
|---|---|---|
| `mccomiskey` | 139 | 144 |
| `uneven` | 133 | 144 |
| `muggsie` | 129 | 144 |
| `toml999` | 124 | 144 |
| `FlyData` | 102 | 144 |

On `mccomiskey` that is ~2% of its ~6,900 uncertain candidates per frame and **nothing at all below SNR 8**, which
is why its C0@nc2 "6,871 false positives" at precision 0.8325 is an artifact rather than a finding.

**Next step — re-run the deep runs with a much larger `--budget-montages`.** Cost scales linearly with coverage:
the 236-montage QA pass took 27 min at chunk=4, so ~7 min per 60 montages.

| target | montages (`timmer`) | QA time | coverage |
|---|---|---|---|
| current (20/frame) | 180 | ~20 min | 5.5% |
| 60/frame | 540 | ~65 min | 17% |
| 200/frame | 1,800 | ~3.5 hr | 55% |
| full | 3,253 | ~6 hr | 100% |

Full coverage on every deep run is ~6 hr each and probably not worth it. Suggested: **60–200 montages/frame for
any run whose precision is quoted in a report**, prioritised as `mccomiskey` (its precision has already been cited
and is the most misleading), then `timmer`, `SorenVance`, `lumos`, then the remaining default-budget runs.

Independently of any re-run, **record the coverage fraction in the golden sidecar** so a consumer can tell whether
a precision figure is a measurement or a bound. Today nothing in `<frame>.golden.json` says how much of the
uncertain tier was examined, which is why this went unnoticed for so long.

**The coverage-recording half of this is done** (schema v2, `docs/golden-tier-plausibility-design.md` §4.5):
goldens now carry per-tier `examined`/`total`, and candidates the budget never reached are `unresolved` and
excluded from **both** denominators instead of being counted as false positives. That removes the artifact at
its source for any regenerated run — the "6,871 false positives" shape cannot recur. The remaining work here is
purely the larger `--budget-montages` re-runs on the runs still carrying v1 sidecars.

### F16 — The SNR≥12 auto-confirm gate inverts on heavily-defocused runs
**Status:** Fixed — `docs/golden-tier-plausibility-design.md` / `plans/golden-tier-plausibility-plan.md`

The donut matched filter shreds each ring into many tiny high-SNR fragments, which then pass `build_goldens`'
SNR≥12 **auto-confirm** gate without QA. The gate assumes a high-SNR candidate is a real star — sound for the
per-pixel-SNR path it was validated on, **not** for the matched-filter path.

**Evidence.** Golden star widths, donut runs old vs newly generated:

| run | mode | stars | median W | p90 W | **≤4 px** |
|---|---|---|---|---|---|
| `lumos` | donut, new | 60,348 | 3 px | 4 px | **91%** |
| `SorenVance` | donut, new | 28,935 | 3 px | 36 px | **67%** |
| `FlyData` | donut, new | 5,462 | 11 px | 22 px | 26% ✅ |
| `mufti` | donut, existing | 5,612 | 18 px | 36 px | 1% |
| `Panos` | donut, existing | 7,178 | 12 px | 36 px | 11% |
| `LinwoodFocus` | donut, existing | 1,640 | 20 px | 36 px | 19% |

`lumos`'s donuts measure ~21 px and `SorenVance`'s ~26 px per the donut heuristic, so a golden whose stars are
median **3 px** is fragments, not stars. The scored consequence: HocusFocus finds 8–813 stars/frame (plausible for
these fields) against a reference claiming ~4,600/frame, giving recall@≥12 of **0.036** (`SorenVance`) and
**0.013** (`lumos`) — and **config B (donut-on) is no better than C0**, which is the tell. A real donut-detection
gap would show B recovering; it doesn't, because the reference is wrong rather than the detector blind.

**ROOT CAUSE (investigated 2026-07-30) — the tiering, not the matched filter, and NOT bad data.**
`snr_ref`'s SNR is a **per-pixel/peak** measure, so it scales inversely with how far a star's flux is spread. A
2 px noise spike concentrates its signal and scores high; a 36 px defocused donut spreads the same flux over
~1,000 px and scores low. `build_goldens` then auto-confirms everything at SNR≥12 **without QA**, assuming high
SNR implies real. On a heavily-defocused run that assumption **inverts** — the gate keeps the noise and discards
the stars:

| run | extreme HFR | auto-confirmed (SNR≥12) | discarded (SNR<12) | corr(bbox, SNR) |
|---|---|---|---|---|
| `lumos` | 10.7 | n=2,268, median **2 px** | n=24,499, median **36 px** | **−0.495** |
| `SorenVance` | 7.5 | n=4,506, median 3 px | n=21,601, median 12 px | −0.082 |
| `FlyData` | 5.0 | n=544, median 6 px | n=1,991, median 28 px | −0.054 |

Severity tracks defocus exactly, which is the confirming signature. `FlyData` is mild enough that the ordering
survives, which is why it produced a good golden.

**The `--donut` matched filter is not at fault** — it *found* the donuts; they sit in the candidate list at 36 px
(13,170 of them ≥30 px on one `lumos` frame). What fails is the SNR tiering applied afterwards.

**The data is good.** Cropping `lumos`'s large low-SNR candidates shows genuine faint diffuse defocused discs at
SNR 15–44. These are legitimate low-SNR, heavily-defocused runs — exactly what a validation bank should contain.
**Do not delete them.**

**Designed 2026-07-30 → `docs/golden-tier-plausibility-design.md`** (plan: `plans/golden-tier-plausibility-plan.md`).

**The integrated-SNR next step proposed here was measured and does not work** — on `lumos@209735` it keeps 2,445
candidates at 88.0% ≤4 px versus peak SNR's 2,390 at 88.1%, i.e. marginally worse, and on `FlyData@889` it takes
≤4 px from 16.7% to 29.0%. A 3 px spike at 12σ peak has integrated significance ≈20; a 36 px donut at 0.25σ/px
has ≈8. Integrated SNR is the *correct* significance ordering and still ranks the spike higher. No
significance-based statistic can fix this — the gate's error is using significance as a proxy for "is a star".

The design instead keeps the `snr >= 12` tier definition (so `recall@SNR≥12` keeps its meaning), adds a
frame-relative **star-plausibility** measure (candidate size ÷ the frame's own star scale) that reorders the QA
worklist, drops auto-confirm entirely on `--donut` runs, and adds an **unresolved** state excluded from both
recall and precision denominators.

Two findings from that work belong here regardless of when it lands:

- **`LinwoodFocus` is also contaminated** (per-frame high/QA tier width ratio **0.71**: high tier median 13 px vs
  QA tier 28 px). Its historical `recall@SNR≥12` measures the wrong star population and cannot be rescued by
  rescoring. `Panos` is inconclusive; `mufti` and `FlyData` are clean.
- **Not hot pixels, and σ is not mis-estimated.** Zero auto-confirmed `lumos` sites recur in ≥11 of 23 frames
  (68.3% appear in exactly one), and block-MAD σ agrees with an adjacent-difference estimate (99.33 vs 103.79) at
  unit z-width. Do not re-investigate either.

Until the fix lands, `lumos` and `SorenVance` cannot be scored: their goldens are quarantined at
`_prior_reports/{SorenVance,lumos}_BAD_donut_overdetect_20260730/` (recoverable) and both runs are back to NaN.
`verification_20260730T141918Z`'s rows for them must be disregarded.

### F12 — `mccomiskey` is a low-SNR run
**Status:** Open · data-quality note, no action needed

7.0 s subs, Green filter, 0.2885 "/px. Median candidate SNR **11.3** at best focus and **8.1** at the wings; only
47% and 24% of candidates clear SNR 12. Best-focus HFR 3.43 px is inside the detector's calibrated 2–4 px band, so
this is **not** an oversampling problem and binning is not the remedy (2×2 would push HFR to ~1.7 px, below the
band, breaking every pixel-unit knob). Longer exposure is the only lever. Worth knowing when its numbers look
noisy.

### F13 — `SorenVance` was the one bayered run never scored
**Status:** Blocked by F16

Bayered, 6 frames, and had no goldens or linear exports at all, so it produced NaN in every report. Linear exports
now exist and the donut heuristic flags it (frac 12.00, bbox 26 px), but its generated golden proved invalid — see
F16 — so it remains unscored. `lumos` (23 frames, mono, donut-aware) is in the same position. `vsn07` (7 frames,
mono, no donut) succeeded and is now scored, at 100% uncertain coverage.

### F14 — `astrodet` is frameless
**Status:** Won't fix

Holds artifacts but zero `Focuser*` frames, so run discovery skips it and no golden can be built. Documented so it
is not repeatedly investigated.

---

## Process

### F15 — `optimize --per-run` overwrites each run's stored settings
**Status:** Open

The prepass writes `optimized_settings.json` back into the run folder as well as `--out`. That destroyed
`bobp_m101`'s historical "row (a)" settings (`sens 50 / clip 9.5`) mid-investigation, leaving only the two knobs
quoted in the write-up, so the baseline had to be reconstructed rather than reproduced.

**Next step.** Consider writing only to `--out` unless a flag opts into updating the run folder, or snapshot the
previous file alongside it.
