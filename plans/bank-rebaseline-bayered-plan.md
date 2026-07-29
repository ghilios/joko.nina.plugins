# Bank Re-baseline for Bayered Runs — Implementation Plan (Phase 2)

**Goal:** rebuild the validation artifacts the Phase 1 parity fix invalidated, then re-check the `bobp_m101`
recall conclusion that motivated PR #111 — so `ghilios/exposure-recommendation` can be validated against a
harness whose numbers mean what they say.

**Depends on:** `ghilios/headless-detection-parity` (Phase 1, complete). Branch:
`ghilios/bank-rebaseline-bayered`, off Phase 1. Rebase both onto `develop` in order once the exposure PR merges.

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

---

## Task 1 — Re-export linear frames (mechanical, no LLM)

- [ ] Invoke the **`generating-af-golden-data`** skill for the exact pipeline and its gotchas (rate limits,
      XISF/Bayer handling) — do not improvise the commands.
- [ ] Re-run `TestApp export-linear` for `bobp`, `bobp_m101`, `timmer`. Phase 1 fixed this runner to debayer, so
      the new exports are luminance where the old were mosaic.
- [ ] **Verify the fix took effect** before proceeding: the new `.linear.fits` must differ from the old for these
      bayered runs. If they are identical, the export fix did not apply and everything downstream is wasted —
      stop and report.

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
