# Bank Re-baseline for Bayered Runs — Implementation Plan (Phase 2)

**Goal:** rebuild the validation artifacts the Phase 1 parity fix invalidated, then re-check the `bobp_m101`
recall conclusion that motivated PR #111 — so `ghilios/exposure-recommendation` can be validated against a
harness whose numbers mean what they say.

**Depends on:** `ghilios/headless-detection-parity` (Phase 1, complete). Branch:
`ghilios/bank-rebaseline-bayered`, off Phase 1, which is itself off `develop` — both were re-parented off
`ghilios/exposure-recommendation` so this re-baseline can run, and Phase 1 can merge, before PR #159 does.

**Spec:** `docs/headless-detection-parity-design.md` §Consequences for stored artifacts.

## Why everything below is stale

Phase 1 changed what the headless detector *sees* for bayered runs — from a raw Bayer mosaic to
CFA-hotpixel-filtered debayered luminance, chosen per-params inside `Detect`. On `bobp` that moved the optimizer
from `Sensitivity 0` / 52 stars to `Sensitivity 10` / 27 stars. Every stored number derived from the old
representation is therefore measuring a different image than the app uses.

Phase 1 also fixed `ExportLinearRunner`, which feeds `tools/golden/snr_ref.py`. The goldens were built from a
**mosaic** reference while HocusFocus detects on luminance — so mosaic-only reference finds were scored as HF
recall gaps HF could never close. **This is the mechanism that makes the `bobp_m101` audit suspect**, not just its
absolute numbers.

## Scope

| Run | Goldens | Linear exports | Action |
|---|---|---|---|
| `bobp` | 9 | 9 | regenerate both |
| `bobp_m101` | 10 | 10 | regenerate both — **the run under re-check** |
| `timmer` | 9 | 9 | regenerate both |
| `SorenVance` | 0 | 0 | none exist; out of scope unless we want it scored |

18 mono runs are unaffected and must **not** be regenerated — their artifacts are still valid, and re-running them
would burn hours and risk churn.

**Sequencing decision:** do **`bobp_m101` first, alone**, then stop and reassess. It is the run the conclusion
under re-check rests on, so it answers the actual question for a third of the rate-limited LLM budget. If its
conclusion moves, `bobp` and `timmer` become more urgent; if it holds, they may not be urgent at all. Do not start
the other two without checking back.

---

## Task 1 — Re-export linear frames (mechanical, no LLM)

- [x] Invoke the **`generating-af-golden-data`** skill for the exact pipeline and its gotchas (rate limits,
      XISF/Bayer handling) — do not improvise the commands.
- [x] Re-run `TestApp export-linear` for `bobp_m101` (`--profile-id b10b1d6d…` / Default, `--overwrite`).
      `bobp` and `timmer` deferred per the sequencing decision above.
- [x] **Verify the fix took effect** before proceeding: the new `.linear.fits` must differ from the old for these
      bayered runs. If they are identical, the export fix did not apply and everything downstream is wasted —
      stop and report.

### Task 1 result — `bobp_m101`, 2026-07-29 (gate PASSED)

All 10 sidecars changed (10/10 distinct MD5s; byte size identical at 47,088,000 — 5936×3966 either way, so
**size is not a valid gate**, only content is). Old exports preserved at
`D:\Autofocus Bank\_prior_reports\bobp_m101_linear_mosaic_prephase1\`.

The old files are demonstrably a **Bayer mosaic**: their four CFA sub-lattice medians are four distinct values
(330 / 353 / 354 / 338 ADU, spread 24–25) and mean |horizontal-neighbour Δ| is 23.7–24.2 ADU. The new files'
sub-lattice medians are equal to within 1 ADU and the neighbour Δ falls to 3.5 ADU. 97.6% of pixels changed;
median |Δ| 11 ADU, p99 49 ADU, peaks ~41k ADU at saturated cores.

**The defect's mechanism is not the one this plan and `docs/headless-detection-parity-design.md` assert.** The
checkerboard dominated `snr_ref`'s block-MAD noise estimate, so the reference's σ was **3.0–3.25× too high**
(17.8–19.3 ADU vs 5.9). Both the 5σ detection threshold and the SNR tiering scaled with it, so the mosaic
reference was ~3× **less** sensitive:

| `snr_ref` (k=5, no donut), 10 frames | old (mosaic) | new (luminance) |
|---|---|---|
| median σ | 17.8–19.3 ADU | 5.93 ADU |
| candidates, all tiers | 2,427 | 4,410 |
| high tier, SNR ≥ 12 | 1,365 | 2,107 |

Cross-matched at the `golden eval` radius (12 px): **1,365 / 1,365 (100%)** of old high-tier candidates are
reproduced by the new reference, all of them still in the new high tier. There are **zero mosaic-only finds** on
this run — the old reference was a strict *subset*, not a set polluted with mosaic artifacts. Conversely 209 of
the new 2,107 high-tier stars match no old candidate at any tier, and a further 533 were present but tiered
medium/low. The stored goldens' high tiers equal the old counts exactly (27/44/93/246/450/305/100/54/29/17 =
1,365), confirming the golden high tier is a direct function of this export.

**Consequence for Task 4:** the `recall@SNR≥12 = 0.126` denominator grows ~54% (1,365 → ~2,107) and the added
stars are *fainter* than any the old reference could see. If HF's misses skew faint, the corrected recall goes
**down**, not up. Do not enter Task 4 assuming the deficit was a scoring artifact.

## Task 2 — Regenerate the reference and the goldens

> **START HERE** in a fresh session. Task 1 is complete (see its result above). Be on
> `ghilios/bank-rebaseline-bayered`; the working tree should be clean. **Invoke the
> `generating-af-golden-data` skill first** for the exact pipeline and its gotchas — do not improvise the
> commands. Scope is `bobp_m101` **alone** (`bobp_m101/AutoFocus_20260626_225408/attempt01`), per the
> sequencing decision above.

**What the QA step must report, because it decides Task 4's answer.** The new reference has ~54% more
candidates and the additions are *fainter* than anything the old reference could resolve. So:

- **How many candidates does the LLM QA confirm vs reject, and at which SNR tiers?** If it rejects most of the
  newly-visible faint ones, the effective golden barely changes and Task 4's answer is "the old conclusion
  stands". If it confirms them, the recall denominator genuinely grows and the deficit likely deepens.
- **Any systematic rejection pattern.** If the faint additions are mostly noise, say so plainly — that is a real
  finding, not a failure of the method.
- Per-frame golden counts, old vs new. The old high-tier per-frame counts were
  27/44/93/246/450/305/100/54/29/17 (= 1,365).

If rate limits make full QA impractical, report how far you got rather than degrading the method. A partial,
honest golden is more useful than a fast, unreliable one.

- [x] `snr_ref` over the new exports → candidates.
- [x] Montage + **LLM QA** per the skill. This is the long pole and is rate-limited; budget accordingly and do
      not parallelise past the documented limits.
- [x] `build_goldens` → new per-image `*.golden.json`.
- [x] **Keep the old goldens** (rename, don't delete) until Task 4 has compared old vs new. They are the only
      record of what the previous conclusion rested on.

### Task 2 result — `bobp_m101`, 2026-07-29

`bank-donut-meta --refresh` re-confirmed `donutAware=false` on the new representation (donut peak fraction rose
11.5 → 39.0, but extreme-frame HFR 6.9 < 9.0 and donut bbox 14 px < 24 px keep it below the gate), so `snr_ref`
ran without `--donut` as before. Ran with `--budget-montages 20` (not the usual 4) so the **entire** uncertain
tier was QA'd — the old run's uncertain tier was effectively fully covered, and a smaller budget would have
truncated the new golden's faint tier and made the old-vs-new comparison unfair. 67 montages, chunk=4, **0**
rate-limit failures.

| | old (mosaic) | new (luminance) |
|---|---|---|
| candidates | 2,427 | 4,410 |
| high tier, auto-confirmed (SNR≥12) | 1,365 | **2,107** (+54%) |
| uncertain candidates | 1,062 | 2,303 |
| uncertain QA-confirmed | 1,010 (**95.1%**) | 972 (**42.2%**) |
| **golden total** | **2,375** | **3,079** |

**The QA rejects most of the newly-visible faint tier, and the rejection pattern is defocus, not SNR.** Confirm
rate by frame: 5682 (best focus) **1.7%**, 5658 12.0%, 5706 28.5%, then 47.9–69.1% across the wings. At best
focus every real star is already above SNR 12 so the uncertain tier there is genuinely noise; at defocus real
light spreads out and drops below SNR 12. By SNR tier alone the rate is non-monotone (47.9% at 10–12, 33.0% at
8–10, 40.1% at 6.5–8, 52.3% at 5–6.5) purely as a composition artifact of that split.

Artifacts archived at `D:\Autofocus Bank\_prior_reports\bobp_m101_goldens_luminance_phase2\` (goldens, `snr_*`,
`qa_*`, manifest, worklist); old mosaic goldens preserved at `…\bobp_m101_goldens_mosaic_prephase1\`.

Incidental fix: `tools/golden/qa_workflow.js` was CRLF, which the Workflow tool rejects as control characters, so
the documented QA step could not launch. `.gitattributes` now pins `tools/golden/*.js` to LF (commit `dc7cfed`).

## Task 3 — Full `bank-verify` re-baseline

- [x] Invoke the **`running-af-bank-validation`** skill for the pipeline, and heed its warnings: the STA/pumped
      dispatcher requirement, watching `bank_verify_progress.log` rather than stdout (WSL block-buffers), and
      closing NINA first.
- [~] Optimizer A/B prepass, then `bank-verify` with `--commit $(git rev-parse --short HEAD)`.
      **C0-only sweep done; A/B not run** — see below.
- [x] **Validate the harness before trusting the results:** `cwhite_2026` is the dry-run anchor and must
      reproduce its known config-B numbers (P=0.848, recall@≥12=0.181, sensor R²=0.9933, 7/9 aligned). It is
      **mono**, so Phase 1 must not have moved it. If it moved, the mono byte-identity guarantee is broken and
      that is a Phase 1 bug — stop and report.

### Task 3 result — 2026-07-29 (anchor gate FAILED, but **not** a Phase 1 bug — needs a decision)

`bank-verify --nc-sweep 2,3,4 --match-radius 12 --commit dc7cfed` over the whole bank, **C0 only**, ~65 min →
`verification_20260729T145515Z.{json,md}`, 22 runs (the bank has grown from 17: `SorenVance`, `lumos` and `vsn07`
have no goldens and score NaN; `bobp` and `bobp_m101` are new to the report).

**A/B were not run.** The `opt_A`/`opt_B` prepass directories no longer exist, and rebuilding them is
`optimize --per-run` twice over the bank — the skill budgets 1.5–3 hr *each*. The C0 rows are the headline recall
answer and need no prepass, so the sweep was run C0-only. **Consequence:** the plan's anchor is a *config-B*
number and is therefore not reproducible without that prepass.

**Every run moved, mono and bayered alike — and the cause is not Phase 1.** The C0 "as-default" configuration
itself changed between the two reports, and both reports record it:

| | baseline `b331479` (2026-06-24) | new `dc7cfed` (2026-07-29) |
|---|---|---|
| C0 sensitivity | **2.0** | **10.0** |
| C0 noiseClipDefault | 2.0 | 2.0 |

That is commit **`59d5e59` "Revert Simple-mode BrightnessSensitivity/NoiseClipping to v3 values (interim)"**
(2026-07-11), which is in HEAD but **not** in `b331479`. It edits `BuildDefaultStarDetectorParams()`, which
`BankVerifyRunner.BaseDefault()` consumes verbatim. Sensitivity 2 → 10 sheds stars, so recall falls and precision
rises **bank-wide**: e.g. `cwhite_2026` C0@nc2 recall@≥12 0.4594 → 0.2658 with precision 0.8105 → 0.8504 and
`sStars` 106 → 59; `CWhiteFocus` 0.8697 → 0.8072 with `sStars` 3933 → 1802. Goldens are byte-identical on all 17
baselined runs (verified: `goldenStars` and `goldenSNRge12` unchanged for every one), so the golden set is not
the variable.

**So the plan's verification item "mono runs unchanged vs `verification_20260624T142723Z.md`" is not testable as
written** — the configuration under comparison moved for reasons unrelated to this branch. The anchor's movement
is *explained*, not *unexplained*, and it is **not** evidence that Phase 1's mono byte-identity guarantee broke.
Supporting evidence that the harness itself is sound: the dataset copies still produce identical recall
(`CWhiteFocus`≡`standard_example2` 0.807227, `fmeschia`≡`sensitivity_example1` 0.750000,
`LinwoodFocus`≡`sensitivity_example2` 0.091743 — precision differs only because their golden sets differ
slightly), and the full unit suite is **3066/3066 green**.

`bank-verify` has **no `--sensitivity` override** (C0 takes `BuildDefaultStarDetectorParams()` verbatim), so an
apples-to-apples mono anchor needs one of:

1. a scratch build pinning C0 sensitivity to 2.0, re-run on `cwhite_2026` alone (~5 min) — cheapest true test of
   the mono guarantee;
2. a re-run at the pre-Phase-1 commit for a direct A/B (slower, but tests Phase 1 end-to-end);
3. accepting `verification_20260729T145515Z` as the **new** baseline and recording the discontinuity, on the
   grounds that sensitivity 10 is what ships today.

**This is a decision for the user, not an assumption to make.** Nothing downstream of it was assumed.

New-run rows worth recording (C0@nc2): `bobp_m101` golden 3079 / high 2107 → recall@≥12 **0.6450**, precision
**0.9873**; `bobp` golden 1705 / high 1039 → recall@≥12 0.6670, precision 0.9262 — but **`bobp`'s golden is still
the stale mosaic one**, so that row is not re-baselined and must not be read as valid. Same for `timmer`.

## Task 4 — Re-check the `bobp_m101` conclusion

This is the point of Phase 2.

**ANSWERED 2026-07-29 — the deficit was real; correcting the reference deepens it.** Full write-up in
`docs/bobp-m101-recall-investigation-results.md` § *Re-check after the headless-detection parity fix*;
`docs/optimizer-sensitivity-pinning-design.md` annotated. Commit `bbde567`.

Holding the detector and params fixed and varying **only** the golden, the true positives are **identical (247)**
— the detector finds exactly the same stars and only the denominator moves, 1365 → 2107, so recall@SNR≥12 falls
**0.181 → 0.117**. The star-shedding characterisation survives (`LowSensitivity` 1236 + `Degenerate` 595 still
dominate) but the "94% from two knobs" figure weakens to **~65%**: the corrected reference adds fainter stars
that fail earlier (structure gap 446, `TooSmall` 311). Precision *rises* with the corrected golden across all
three param sets (0.809 → 0.965 at the star-rich corner, FPs 330 → 60), because much of what the mosaic reference
scored as HF false positives were real stars it was too insensitive to see. **`Wtie = 0.02` still looks right —
more so**: the star-rich corner now dominates the shedding corner on both axes. No objective constant changed.

- [x] `docs/bobp-m101-recall-investigation-results.md` reported **recall@SNR≥12 = 0.126** and attributed 94% of
      misses to two knobs (`LowSensitivity` gate 1302, star-clip Degenerate guard 762). That analysis drove
      `docs/optimizer-sensitivity-pinning-design.md` and the `Wtie` 1e-3 → 0.02 change shipped in PR #111.
- [x] Re-measure recall/precision on the new goldens with the fixed harness. Report old vs new side by side.
- [x] **Answer explicitly:** does the star-shedding characterisation still hold? Was the recall deficit real, or
      an artifact of scoring a luminance detector against a mosaic reference? Does `Wtie = 0.02` still look like
      the right call?
- [x] Whatever the answer, **write it up** — append a dated section to
      `docs/bobp-m101-recall-investigation-results.md` and annotate
      `docs/optimizer-sensitivity-pinning-design.md`. If the conclusion still holds, say so with the new numbers;
      that is as valuable as an overturn and stops this being re-litigated.
- [x] **Do not change `Wtie` or any objective constant in this branch.** If the evidence no longer supports it,
      that is a separate, spec'd decision — report it, don't act on it.

## Verification

- [~] `cwhite_2026` anchor: **moved, and explained** — the C0 as-default sensitivity changed 2.0 -> 10.0 via
      `59d5e59` (2026-07-11), not via Phase 1. Not testable as written; see Task 3 result.
- [~] Mono runs unchanged vs `verification_20260624T142723Z.md`: **all moved**, bank-wide, from the same
      defaults change. Goldens byte-identical throughout; determinism check passes; suite 3066/3066.
- [~] Bayered runs: `bobp_m101` has new goldens **and** new bank-verify numbers. `bobp` and `timmer` have
      bank-verify rows but **still-stale mosaic goldens** (deferred by the sequencing decision). `SorenVance`
      has no goldens at all.
- [x] The `bobp_m101` question is answered in writing, either way.
