# Synthetic AF bank — followups wave 6

Plan: [`plans/synthetic-af-bank-followups-wave6-plan.md`](../plans/synthetic-af-bank-followups-wave6-plan.md).
Wave 5: [`docs/synthetic-af-bank-followups-wave5-results.md`](synthetic-af-bank-followups-wave5-results.md).
Register: [`docs/followups.md`](followups.md).

*The question: which of the numbers on the board are still true? Wave 5 fixed the wide-field catalog query and left
two rows of the bank marked known-wrong, with F35's headline validation resting on exactly those two. The
re-baseline says the headline survives — and that **one of the two rows was never wrong in the first place**.*

## Headline

| what | result |
|---|---|
| F44 — re-baseline `D01`/`D02` | **Done. `D02` was never affected** — its re-rendered frames are BIT-IDENTICAL. Only `D01` moved, 8107 → **19212** truth stars, occupancy 49% → **100%** |
| F35 — the headline validation | **Survives unchanged**: seed OFF → `FinalJ` exactly 0, seed ON → 0.9916, on a field with 2.4× the stars |
| The suspect criterion itself | **Over-predicted.** FOV ÷ cap is not the test; whether the ≤ 4 corner cells cover the field at that **declination** is |
| F41 in practice | Every wave-3 golden-eval number on the old frames reproduces **exactly** on the wave-6 binary, and `D02` bounds the wave-3→wave-6 drift at **5×10⁻⁵** |
| F42 — make the trap impossible | **Shipped**: per-user default path, loud bootstrap, and a `UseAdvanced=False` warning that names the knobs the file is lying about |
| F39 (a) | **Shipped** — `DetectionBinningSource` is a field now, not a sentence. Part (b) deliberately NOT paired with this wave |
| F43 replay | **Decided**: extend the rescue, but its premise-breaking evidence and a UI answer come first |
| F45 (new) | The Grubbs test **rejects the in-focus point** of a curve fitting at R² 0.9994, and the blind walk then buys an extra exposure |

## The re-baseline, and the row that never needed one

`synth-bank --overwrite` on both rows, then the truth sidecars read directly:

| row | in-focus truth stars | x extent | y extent | 16×16 occupancy | frames |
|---|---|---|---|---|---|
| `D01` before | 8107 | 431–5921 | **741–2965** | **125/256** | — |
| `D01` after | **19212** | −1–6249 | 0–4176 | **256/256** | changed |
| **`D02` before** | 3594 | −2–6247 | −1–4176 | 256/256 | — |
| **`D02` after** | 3594 | −2–6247 | −1–4176 | 256/256 | **BIT-IDENTICAL** |

`D02`'s golden set is the same set too — 3536 stars and 25 unresolved, differing only in enumeration order because
the corrected query walks cells differently. **So nothing derived from `D02` ever needed re-measuring.**

**Why the marking was wrong, and it is a reusable correction.** F44 flagged both rows on *diagonal FOV ÷ one-cell
cap* — `D01` 7.9×, `D02` 2.4×. That ratio is a proxy for the real question: **do the ≤ 4 corner cells `FindAreas`
returns cover the field?** Catalog cells are equal-area, so at `D02`'s **Dec +61.45°** a cell spans ≈ 1/cos(61.45)
≈ 2.1× more RA degrees than at the equator, and four of them covered an 11.95° field comfortably. A ratio rule
flags rows that are fine and keeps flagging them; the criterion has to be cell coverage **at the field's
declination**. `D01` at 7.9× is beyond rescue by any declination.

**The re-render moved exactly one thing.** A `--dry-run` diff against the stored `synthetic_meta.json` first: step
size (9 / 6), exposure (0.5 s, both at the clamp floor), `detectionBinning` (1), donut (false) and every sweep
position are **identical** before and after. The star field is the only variable, which is what makes every
comparison below a clean two-arm measurement rather than an interpretation.

## F35 — re-measured on one binary, because a prior wave's arm is not a control

The wave-3 control arm was "the pre-F35 commit", which is precisely the cross-binary comparison
[F41](followups.md) says is not a comparison. So the control is now produced by the binary under test:
**`optimize --no-min-hfr-seed`**, a harness-only flag with the same shape as `--keep-floor` — absent, the run is
bit-identical to before it existed.

| dataset | frames | fitted vertex | `FinalJ` seed OFF | `FinalJ` seed ON | landed `MinHFR` |
|---|---|---|---|---|---|
| `D01` | old (truncated) | 0.7621 | **0.000000** | 0.990358 | 0.100 |
| `D01` | **new (full field)** | 0.7712 | **0.000000** | **0.991551** | 0.100 |
| `D02` (bit-identical frames) | — | 0.7488 | **0.000000** | 0.996510 | 0.3625 |
| `D03` | — | 1.0154 | 0.995880 | 0.996543 | 0.300 |
| **`D05` control** | — | 1.7882 | **0.999706** | **0.999706** | 1.48125 |

- **The headline holds.** "`D01` and `D02` go from `FinalJ` exactly 0 to a real landing" is a gate-threshold
  result, and it behaves like one: 2.4× the stars, same answer.
- **`BaselineJ` is identical between the two arms of every dataset** (F41's free check), so the comparison is
  valid before any landing is read. `D05`'s `BaselineJ` of 0.999293 also matches wave 5's parent-commit
  measurement exactly — the same pinned settings file, the same detector.
- **`D05` is bit-identical seed-on vs seed-off.** The boring control that caught the wave-3 seed leak is still
  boring, which is the only way to know the seed is still declining where it should.
- **`D02` bounds the drift.** Its frames provably did not change, so `FinalJ` 0.99656 → 0.996510 is **pure
  wave-3→wave-6 binary drift: 5×10⁻⁵**. `D01`'s old-frames landing moved +1.8×10⁻⁴. So `D01`'s **+1.2×10⁻³** from
  the re-render is ~6× the drift and belongs to the frames.

## What the corrected field did to the recall tables

`golden eval` over `D01` on both frame sets — 28 evals, ~11 min, no code. **Every old-frames row reproduces the
wave-3/wave-5 numbers exactly to three decimals**, which is the F41 control doing its job across three waves.

**F35's "how low is safe" sweep** (recall@high; FP = **0** in all 18 configurations):

| `MinHFR` | 1.2 | 0.9 | 0.7 | 0.5 | 0.35 | 0.30 | 0.25 | 0.15 | 0.1 |
|---|---|---|---|---|---|---|---|---|---|
| D01 old | 0.129 | 0.152 | 0.160 | 0.164 | 0.164 | 0.165 | 0.165 | 0.165 | 0.165 |
| **D01 new** | 0.127 | 0.150 | 0.157 | 0.161 | 0.162 | 0.162 | 0.162 | 0.162 | 0.162 |
| vertex-frame stars, new | **0** | 0 | 4 | 6 | 7 | 7 | 7 | 7 | 7 |

Recall is fractionally *lower* on the corrected field, and that is the correct direction: the golden grew with the
field and the stars the fix restored are predominantly faint edge-of-frame ones, so TP rises 8253 → **19039**
while the ratio does not. The conclusions — zero false positives at any gate, saturation by 0.5 — are unchanged.

**F43's per-frame counts**, the table that says why this rig class needs the rescue at all:

| `MinHFR` | 5964 | 5973 | 5982 | 5991 | **6000 (focus)** | 6009 | 6018 | 6027 | 6036 |
|---|---|---|---|---|---|---|---|---|---|
| 1.2 (default) | 1859 | 3758 | 4157 | 13 | **0** | 11 | 4182 | 3761 | 1867 |
| 0.30 | 1859 | 3758 | 5784 | 476 | **7** | 438 | 5786 | 3761 | 1867 |
| 0.10 | 1859 | 3758 | 5784 | 478 | **7** | 443 | 5786 | 3761 | 1867 |

Thousands of stars in the wings, **zero at focus at the default gate**, and lowering it buys seven — against
`NHard = 3`. The rescue is exactly as thin on a correct field as it looked on a truncated one.

**F24's five arms on `D01`**: master still hurts (masterOFF − shipping = +0.014 old, +0.013 new), `both` still
recovers master-OFF recall exactly, precision still 1.000 / FP 0 on every arm. The wave-5 verdict turned on
D06/D09/D14, none of which was ever suspect.

## F42 — the trap, closed three ways

1. **The default path now inherits.** `DefaultPath()` = an existing file beside the exe (so an arm directory that
   already has one is untouched) → otherwise `%LOCALAPPDATA%\HocusFocusHarness\harness_settings.json`. The
   build-to-a-fresh-`-o`-directory workflow stops minting a new detector per arm, which was the mechanism.
2. **The bootstrap is loud** — stderr `WARNING` + `Logger.Warning`, naming the profile and saying the run is not
   comparable to earlier arms. That moment is exactly when an arm stops being comparable.
3. **A `UseAdvanced=False` settings file warns and names what it is lying about.**
   `StarDetectionOptions.InitializeOptions` ends in `ConfigureSimpleSettings()` → `DerivePresetSettings()`, which
   **overwrites ~16 advanced knobs** from the three `Simple_*` presets. Editing `MinHFR` or
   `NoiseClippingMultiplier` in a pinned file therefore does nothing, silently — the wave-5 probe set where four
   different configurations returned identical star counts. The overridden keys are **measured** (hand the file's
   bag to a throwaway options instance and diff it) rather than hard-coded, so the list cannot drift.

**Wave 5 is not invalidated by (3), and this was checked rather than assumed:** `pinned_settings.json`'s advanced
values coincide with the Typical presets, so the diff is empty. And no wave-5 comparison is touched by (1) —
those arms passed `--settings` explicitly, which bypasses `DefaultPath()`.

## F39 — half fixed, half deliberately deferred

Part (a): `ResolveForRun` now writes `DetectionBinningSource` (`derived-from-in-focus-hfr` / `kept-from-base`) as
a **field**. Seventeen bank files said "kept from base" in a prose `DerivedNotes` sentence while presenting
`"DetectionBinning": "Bin2"` beside sixteen genuinely exported values; a reader diffs fields, not sentences.

Part (b) — actually running the seven `detectionBinning = 2` datasets at their expected factor — is **not** paired
with this wave's re-baseline, on purpose. The entire value of this pass is that exactly one variable moved. Part
(b) would move a second one on seven other datasets in the same wave and the two effects could not be separated.

## F43 — replay decided

Replay gates on the user's CURRENT settings because "a saved run exists because those settings could already
focus". **Three populations falsify that premise, all of them real today:** failed AF runs (`KeepFramesForReview`
saves them), runs captured before the user changed settings, and runs never captured by this profile at all — the
live app's own `LastSelectedLoadPath` pointed at `D01`, a dataset yielding **zero** in-focus stars at the default
gate. So replay refuses a case it demonstrably receives.

**Decision: extend the ladder to replay, baseline gate first**, so the premise case is untouched and only the
refusal path changes. **Not shipped in wave 6**, because a replay whose baseline cannot fit has `BaselineJ = 0`
— measured, every `D01`/`D02` arm printed `Current settings J: 0` — and the wizard's headline is a percentage
*against* that baseline. The change needs a decision about what the summary says when there is no baseline to
improve on, which is UI work with its own review. Replacing a confusing refusal with a confusing result is not
progress.

## F45 — the blind walk extends one step past its own data

From a real 40 mm simulator run: the engine queued its last sweep point from an "established minimum" of **25015**
while the run's own report shows `argmin(HFR + error)` at **25000** and the hyperbola vertex at 24999.996 — one
extra step, one extra exposure.

**Reproduced offline from the report's own eleven points**, not inferred. `TrendlineFitting.Minimum` is computed
on the **post-Grubbs-rejection** set, and the rejected point is the **in-focus one**:

| set | rejected | resulting `Minimum.X` |
|---|---|---|
| the 10 points present at the decision | **25000** | **25015** |
| the final 11 points | **25000** | 25015 |
| the same 10 points, rejection disabled | — | **25000** |

And the rejection is the more serious half: on a fit with **R² = 0.9994**, the vertex point's residual is 0.0345 px
against its own measurement error of **0.174 px** — consistent with the curve to a fifth of its own error bar — and
it is discarded because `RejectionTest` scales by the **MAD of the weighted residuals**, which collapses precisely
when the fit is good. Full numbers, and the three separable fixes (none taken), in [F45](followups.md).

## Lessons

**1. A suspect marking is a hypothesis, and it has to be measured like one.** `D02` was flagged by a ratio,
carried the warning for a whole wave, and was never affected — its frames came back bit-identical. The cheapest
step of the entire re-baseline (diff the frames) would have removed it from the work list on day one.

**2. Keep the control that cannot move.** `D02`'s bit-identical frames turned it from a row to re-measure into the
instrument that bounded wave-3→wave-6 drift at 5×10⁻⁵ — which is the only reason `D01`'s +1.2×10⁻³ can be
attributed to the frames rather than to the binary. The most useful dataset this wave was the one where nothing
happened, for the second wave running (`D05` did the same job for the seed).

**3. If a control arm can only be built from another commit, build the flag instead.** F35 had no way to produce
its own control on one binary, so `--no-min-hfr-seed` is the fix — and it cost twenty minutes against a
cross-binary comparison that F41 already proved meaningless.

**4. Change one thing.** The `--dry-run` diff *before* rendering is what makes this wave's numbers readable: step
size, exposure, binning and donut verdict are provably identical, so every difference belongs to the star field.
It is also why F39 part (b) was refused — it would have moved a second variable in the same wave.

**5. A warning that names the specific keys beats a warning that names the hazard.** The `UseAdvanced=False` check
diffs the file's own bag against what the options class does with it, so it reports *"`MinHFR 0.1→1.2`"* rather
than *"advanced knobs may be overridden"* — and it cannot drift when the presets change.

## Verification

- Full suite green; the new work adds 7 harness tests of which **4 discriminate** (confirmed by neutralizing all
  three F42/F39 behaviours at once and re-running: exactly those 4 fail). The other 3 are labelled guards.
- `--no-min-hfr-seed` has no unit test — `OptimizationDiagnosticRunner` is not source-linked into the test project
  — and its discriminating evidence is the arm itself: on `D01` the flag yields `FinalJ` exactly 0 where its
  absence yields 0.9916.
- Per [F37](followups.md), a red CI check is checked against the native test-host crash before being read as a
  regression, and nothing merges on red.

## Reproduce

```
# W1 -- re-render, then diff the truth sidecars
TestApp synth-bank --spec <spec> --out "D:\SyntheticAutofocusBank" \
    --datasets D01_ultrawide_40mm,D02_rich_135mm --dry-run   # derived-parameter diff FIRST
TestApp synth-bank ... --overwrite

# F35 -- one binary, both frame sets, control via the new flag, --settings pinned (F42)
D:\hf_w6\f35_arms.sh          # old frames preserved at D:\hf_w6\oldframes

# F24 / MinHFR sweep / F43 per-frame -- golden eval only, no code
D:\hf_w6\golden_arms.sh

# F32 arm R -- restarts instead of a floor
D:\hf_w6\f32_armR.sh
```
