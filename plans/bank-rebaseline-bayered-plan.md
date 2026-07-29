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

- [ ] `snr_ref` over the new exports → candidates.
- [ ] Montage + **LLM QA** per the skill. This is the long pole and is rate-limited; budget accordingly and do
      not parallelise past the documented limits.
- [ ] `build_goldens` → new per-image `*.golden.json`.
- [ ] **Keep the old goldens** (rename, don't delete) until Task 4 has compared old vs new. They are the only
      record of what the previous conclusion rested on.

## Task 3 — Full `bank-verify` re-baseline

- [ ] Invoke the **`running-af-bank-validation`** skill for the pipeline, and heed its warnings: the STA/pumped
      dispatcher requirement, watching `bank_verify_progress.log` rather than stdout (WSL block-buffers), and
      closing NINA first.
- [ ] Optimizer A/B prepass, then `bank-verify` with `--commit $(git rev-parse --short HEAD)`.
- [ ] **Validate the harness before trusting the results:** `cwhite_2026` is the dry-run anchor and must
      reproduce its known config-B numbers (P=0.848, recall@≥12=0.181, sensor R²=0.9933, 7/9 aligned). It is
      **mono**, so Phase 1 must not have moved it. If it moved, the mono byte-identity guarantee is broken and
      that is a Phase 1 bug — stop and report.

## Task 4 — Re-check the `bobp_m101` conclusion

This is the point of Phase 2.

- [ ] `docs/bobp-m101-recall-investigation-results.md` reported **recall@SNR≥12 = 0.126** and attributed 94% of
      misses to two knobs (`LowSensitivity` gate 1302, star-clip Degenerate guard 762). That analysis drove
      `docs/optimizer-sensitivity-pinning-design.md` and the `Wtie` 1e-3 → 0.02 change shipped in PR #111.
- [ ] Re-measure recall/precision on the new goldens with the fixed harness. Report old vs new side by side.
- [ ] **Answer explicitly:** does the star-shedding characterisation still hold? Was the recall deficit real, or
      an artifact of scoring a luminance detector against a mosaic reference? Does `Wtie = 0.02` still look like
      the right call?
- [ ] Whatever the answer, **write it up** — append a dated section to
      `docs/bobp-m101-recall-investigation-results.md` and annotate
      `docs/optimizer-sensitivity-pinning-design.md`. If the conclusion still holds, say so with the new numbers;
      that is as valuable as an overturn and stops this being re-litigated.
- [ ] **Do not change `Wtie` or any objective constant in this branch.** If the evidence no longer supports it,
      that is a separate, spec'd decision — report it, don't act on it.

## Verification

- [ ] `cwhite_2026` anchor reproduces (harness integrity).
- [ ] Mono runs unchanged vs `verification_20260624T142723Z.md`.
- [ ] The three bayered runs have new goldens and new bank-verify numbers.
- [ ] The `bobp_m101` question is answered in writing, either way.
