# Synthetic AF bank — followups wave 6 (plan)

Wave 5: [`docs/synthetic-af-bank-followups-wave5-results.md`](../docs/synthetic-af-bank-followups-wave5-results.md) ·
Register: [`docs/followups.md`](../docs/followups.md) · Base: `develop` @ `00ebc83` (v4.0.0.12).

*The question this wave answers: **which of the numbers on the board are still true?** Wave 5 fixed the
wide-field catalog query but deliberately did not re-render the two rows it invalidated, so the bank currently
carries two datasets whose star counts, recall and positions are known-wrong — and F35's headline validation rests
on exactly those two. Everything else here is subordinate to that.*

## Ground rules (wave-5 lessons, applied as preconditions rather than repeated as advice)

| rule | why | where it binds |
|---|---|---|
| **`--settings D:\hf_w6\pinned_settings.json` on EVERY arm** | F42: each build dir bootstraps its own detector from the live profile | every `optimize` / `golden eval` / `bank-verify` invocation |
| **Read `BaselineJ` before reading any knob diff** | F41: it is search-independent, so two arms of one binary MUST agree on it | W1.5, W2 |
| **One binary per comparison; a prior wave's arm dir is not a control** | F41 | W1.5 uses `--no-min-hfr-seed` on ONE binary instead of building the pre-F35 commit |
| **Identical results across supposedly different configs ⇒ the instrument is wrong** | wave-5 `UseAdvanced=False` probes | W1.5 asserts old-frames ≠ new-frames counts before reading any J |
| **Check for a cheap instrument before scheduling an expensive one** | F24: `golden eval` already had every override | W1.6–W1.8 are all `golden eval`, no code |
| **Count only tests that DISCRIMINATE** | wave-4 lesson 2 | W3, W5 |
| **`optimize --per-run` rewrites settings INTO run folders** | F15 | W1.5 orders arms so the canonical arm lands last; snapshot first |

Build: `dotnet.exe build "$(wslpath -w …/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w6\exe'`
Tests: `dotnet.exe test "$(wslpath -w …/Joko.NINA.Plugins.sln)" -c Debug --nologo` (3628 passing at `c2ddb0a`).

---

## W1 — Re-baseline D01 and D02 (blocking; everything else is downstream)

**State.** F44's fix (`AstapCellGeometry.FindAreasCovering`) shipped in wave 5; `D01_ultrawide_40mm` (7.9× over the
one-cell cap) and `D02_rich_135mm` (2.4×) are still the OLD frames and are marked `suspect` in
`synthetic-bank-spec.json`. A scratch re-render of D01 measured truth stars 8107 → 19212 and sensor-grid occupancy
125/256 → 256/256.

**Design decision — the old frames are kept as an arm, not deleted.** `D:\hf_w6\oldframes\{D01,D02}` (done).
"What did the fix change" then becomes a two-arm measurement on ONE binary rather than a comparison against
wave-3 numbers produced by a different one (F41). Every W1 measurement below is run on both frame sets.

### W1.1 — Derived-parameter diff BEFORE rendering
`synth-bank --dry-run --datasets D01_ultrawide_40mm,D02_rich_135mm` and diff against the stored
`synthetic_meta.json`. The catalog query feeds `DeriveExpectedOptimal` — **exposure band, step size,
detectionBinning and the donut verdict are all derived from a render/query that just changed**, so the new frames
may differ by more than star count. Record every field that moves; a changed step size or exposure is a
*legitimate* re-baseline, but it must be stated, not discovered later.

### W1.2 — Re-render into the bank
`synth-bank --spec … --out "D:\SyntheticAutofocusBank" --datasets D01_ultrawide_40mm,D02_rich_135mm --overwrite`.
Note `synth-bank` does not delete the folder: the existing `harness_settings.json` / `optimized_settings.json` /
`hocusfocus_star_detection.json` survive the render. Snapshot them to `D:\hf_w6\snapshots\` first anyway (F15).

### W1.3 — The F44 table, completed
Truth-star count, x/y extent and 16×16 sensor-grid occupancy, old vs new, for **both** rows (wave 5 measured D01
only). This is the direct evidence that the two rows are now sound, and it is read from the `.truth.json`
sidecars — no detector run.

### W1.4 — Instrument: `optimize --no-min-hfr-seed`
`OptimizationDiagnosticRunner` applies `MinHfrSeed.Resolve` unconditionally (`:669`), so F35's control arm can only
be reproduced today by building the pre-F35 commit — which is exactly the cross-binary comparison F41 forbids. Add
a harness-only flag that skips the seed, mirroring how `--keep-floor` exposes F32. TestApp only; no plugin
behaviour changes. One discriminating test (the flag suppresses the floor; absent it, the floor is set).

### W1.5 — F35's headline, re-measured (the load-bearing arm)
`optimize --per-run`, one binary, `--settings` pinned, **arms:**

| arm | frames | seed | datasets |
|---|---|---|---|
| `old_seed` | `D:\hf_w6\oldframes` | F35 on | D01, D02 |
| `old_noseed` | `D:\hf_w6\oldframes` | `--no-min-hfr-seed` | D01, D02 |
| `new_noseed` | bank (re-rendered) | `--no-min-hfr-seed` | D01, D02 |
| `new_seed` **(canonical, runs LAST)** | bank (re-rendered) | F35 on | D01, D02 |
| `ctl_seed` / `ctl_noseed` | bank (unchanged rows) | on / off | D03, D05 |

12 runs, ~5–12 min each. **Gates, in order:**
1. `BaselineJ` agrees between `*_seed` and `*_noseed` on the SAME frames (F41 — no search produces it).
2. `old_*` reproduces wave 3's recorded `FinalJ` for D01/D02 to within the binary drift wave 3→6, or the drift is
   attributed before anything else is read.
3. D03/D05 land identically across `ctl_*` except where the seed legitimately fires (D03 does; D05 must not —
   D05 is the boring control that caught the wave-3 seed-leak bug).
4. **The F35 mechanism claim**: on the NEW frames, does `--no-min-hfr-seed` still give `FinalJ` exactly 0 on
   D01/D02, and does the seed still rescue it? The entry predicts yes (a gate-threshold result). Either answer is
   reportable; a *changed* answer is the more interesting one, since a fuller field means more wing stars and a
   better-constrained pre-search fit.

### W1.6 — Wave-5's F24 arm table, D01/D02 rows
5 arms (`masterOFF`, `shipping`, `boost0`, `close1`, `both`) × 2 datasets × 2 frame sets, `golden eval --params
default`, ~40 s each ⇒ ~13 min. Reuse `D:\hf_w5\f24_arms.sh` with the dataset list narrowed. The wave-5 *verdict*
turned on D06/D09/D14 and does not move; only the two rows are being replaced.

### W1.7 — F35's `MinHFR` sweep rows for D01/D02
`golden eval --params default --min-hfr M` for M ∈ {1.2, 0.9, 0.7, 0.5, 0.35, 0.25, 0.15, 0.1}, × 2 datasets × 2
frame sets ⇒ 32 evals, ~21 min. The claim under test is **"FP = 0 at every value"** and "the recall gain saturates
by 0.5" — both were measured on a spatially truncated field.

### W1.8 — F43's D01 per-frame count table
`golden eval --params default --min-hfr {1.2, 0.30, 0.10}` on D01, per-frame accepted stars, both frame sets. F43's
conclusion was independently confirmed on the reporter's real frames and does not depend on this; the D01 numbers
in the entry are labelled "indicative" and this makes them exact.

### W1.9 — Clear `suspect`, and only then
Remove the `suspect` field from both spec rows (`SynthBank/synthetic-bank-spec.json`), update the affected
`docs/followups.md` entries (F44, F35, F43, F24) **in place with the new numbers, keeping the old ones visible as
the superseded column**, and record the whole thing in
`docs/synthetic-af-bank-followups-wave6-results.md`. `synth-bank`'s suspect warning then stops firing, which is the
observable end-state of this item.

---

## W2 — F32: decide whether the confirmation arm is still the right experiment

**Do not schedule the ~6 h confirmation arm before this decision.** Read together, F32, F8 ("landings are not
reproducible across invocations") and F3 ("`Wtie` does not prevent star-shedding in general") describe one search,
not three defects — and wave 5's own arms found the constraint landing at a **higher** `J` than the unconstrained
search on 5 of 7 binding runs, which is impossible against a global optimum. The floor may be a workaround for a
stuck search rather than the feasibility bound it was designed as.

**The cheap discriminator, pre-registered.** If the unconstrained search is merely stuck, then *any* mechanism that
restarts it should recover most of the φ=0.75 gain **with no floor at all**. `--continue-rounds` already is that
mechanism: it re-seeds from the prior best with a FRESH curated set, resetting the pattern-search stride. So:

> **Arm R:** the 7 binding runs (`mccomiskey`, `uneven`, `toml999`, `CWhiteFocus`, `muggsie`, `D18`, `D19`) at
> `--continue-rounds 2`, **no keep floor**, same binary, `--settings` pinned. ~7 runs × ~10–15 min ≈ 1.5 h.
> `D20` (non-binding control) included as the 8th, where it must not move.

**Decision rule, fixed in advance:**

| outcome | reading | next |
|---|---|---|
| Arm R recovers ≥ 50% of the (off → φ=0.75) `J` gain on ≥ 4 of 7 | the corner is a **search defect**; the floor is treating a symptom | re-scope F32 → a search entry (restarts / Phase-A ordering / the strict `j > bestJ` freeze). File it; do NOT run the 6 h arm |
| Arm R recovers < 50% on ≥ 4 of 7 | restarting is not sufficient; the floor is buying something a restart cannot | keep F32's confirmation arm as designed — **φ = 0.50**, both banks, plus `bank-verify` so C2 is evaluated on real-bank RECALL rather than the keep-% proxy |
| mixed | report the split by run and take no adoption decision | — |

Also record, either way: keep% and σ_focus at each round, so the "greedy trap" claim is measured rather than
inferred. F8 predicts the rounds themselves will not be reproducible — that is a result, not a nuisance.

---

## W3 — F42: make the trap impossible, not merely documented

Three changes in `TestApp/HarnessSettingsStore.cs`:

1. **The bootstrap becomes LOUD.** `ResolveAt`'s create path prints an informational line today. Make it a
   `WARNING` on stderr + `Logger.Warning`, naming the profile and saying in one sentence that this run is not
   comparable to any earlier arm that used a different file. That single moment is when an arm silently stops
   being comparable to its predecessor.
2. **A fixed per-user default so a new build dir INHERITS instead of bootstrapping.** Resolution order becomes:
   `--settings` → an existing file next to the exe (back-compat: a deliberately placed file still wins) → the
   per-user file (`%LOCALAPPDATA%\HocusFocusHarness\harness_settings.json`) → bootstrap, **written to the per-user
   path**. The build-to-a-separate-`-o`-directory workflow then stops generating a fresh detector per arm, which is
   the mechanism F42 is about.
3. **Warn when a settings file has `UseAdvanced = False`.** `StarDetectionOptions.InitializeOptions` ends in
   `ConfigureSimpleSettings()`, which in Simple mode calls `DerivePresetSettings()` and **overwrites ~16 advanced
   knobs** (`NoiseClippingMultiplier`, `StarClippingMultiplier`, `StructureLayers`, `BrightnessSensitivity`,
   `MinHFR`, `MinStarBoundingBoxSize`, `MaxDistortion`, …) from the `Simple_*` presets. So editing those keys in a
   pinned file does nothing, silently — this is the wave-5 "four probe runs returned identical star counts"
   failure, generalized. Print the keys whose loaded value the options class overwrote, if that diff is cheap to
   take (snapshot the bag, construct, compare); otherwise warn unconditionally on `UseAdvanced=False`.
   *Checked for wave 5:* `D:\hf_w5\pinned_settings.json`'s advanced values coincide with the Typical presets, so
   **no wave-5 arm is invalidated** — the hazard is real but did not fire. Say so explicitly.

Tests in `Tests/Harness/HarnessSettingsStoreTests.cs`; each must fail when the behaviour it covers is reverted.

---

## W4 — The two smaller open items

**W4.1 — F43 replay mode (decide, do not drive-by fix).** Live now probes a lowered `MinHFR` ladder before
refusing; replay still gates on the user's CURRENT settings, locked deliberately by
`SeedGuard_ReplayMode_GatesOnBaseline`. The circularity argument applies identically; the counter-argument is that
a saved run exists *because* those settings could already focus. Write the decision and its reasoning into F43 —
including what breaks if replay is converted into a seed-gated path — and either extend the fix with a test that
discriminates, or record "won't fix, and why" so the question stops being re-opened.

**W4.2 — F39 (`detectionBinning = 2` has never run).** 7 datasets expect it; none has ever been scored at it, and
honouring it re-baselines the bank. **Pair with W1 or defer wholesale** — the one thing not to do is change it
after W1's numbers are published. Recommended: take F39's part (a) only (stop *writing* a derived-looking value
that was not derived — mark it `kept-from-base` in the file itself), and leave part (b) to its own wave with its
own arm.

---

## W5 — New entry: the blind walk extends one step past its own data (F45)

**Observed on a real 40 mm simulator run** (NINA log `20260805-214043`, report
`2026-08-05--21-47-43--90d513b9….json`), step size 15, `offsetSteps = 4`:

```
21:47:08 Enough left trend points (4) with an established minimum (25015) to queue remaining right focus points up to 25075
```

`TrendlineFitting.Minimum` is **not a fitted vertex** — `Calculate` sets it to `argmin(Y + ErrorY)` over the
*measured* points (NINA `TrendlineFitting.cs:94`). The engine's queue target is
`Minimum.X + (failedRightPoints + offsetSteps) · stepSize` (`AutoFocusEngine.cs:1485`). The run's own report gives:

| position | HFR | error | `Y + ErrorY` |
|---|---|---|---|
| **25000** | 0.6941 | 0.1736 | **0.8677 ← argmin** |
| 25015 | 0.7709 | 0.2459 | 1.0168 |

and `CalculatedFocusPoint = 24999.996`. Anchored at 25000 the target is 25060, which had already been sampled ⇒ no
further exposure; anchored at 25015 it is 25075 ⇒ **one extra full step, one extra exposure**, on every run that
hits this.

**The mechanism must be measured, not guessed, before the entry is written.** The fit runs on
`WeightRegularization.Regularize`d copies (σ floored at 0.2 × median — inert here, no σ is that small), and the
fitting loop applies **Grubbs rejection with `MaxOutlierRejections = 1`**, removing a point and recomputing the
trendline (`AutoFocusEngine.cs:132-179`). The leading hypothesis is that on the 10-point set present at the moment
of the decision (24925…25060, asymmetric — no 25075 yet) the vertex point 25000 was rejected, leaving 25015 as the
argmin; the final 11-point fit no longer rejects it, which is why the report and the engine disagree.

**Reproduction is offline and free:** feed the report's own 11 points (and the 10-point prefix) through
`TrendlineFitting` + `AlglibHyperbolicFitting` + `MathUtility.RejectionTest` in the test project and read which
point is rejected in each case. File F45 with whichever mechanism that shows — and per the wave rules, **flag it,
do not fix it inline**.

---

## W6 — Close-out

1. Full suite green (`dotnet.exe test`, 3628 + new). Per F37, a red check is checked against the native test-host
   crash and the test COUNT is read, not the badge; nothing merges on red.
2. `docs/synthetic-af-bank-followups-wave6-results.md` — headline table, what changed, what it cost, lessons.
3. `docs/followups.md` updated in place: F44 (suspect cleared / retained), F35, F43, F24, F32, F42, F39, new F45.
4. Branch `ghilios/synthetic-af-bank-followups-wave6`, PR against `develop`. Never push `develop`.

## Run-cost summary

| item | cost | blocking |
|---|---|---|
| W1.1–W1.3 re-render + truth diff | ~10 min | yes |
| W1.4 `--no-min-hfr-seed` + test | ~30 min | yes |
| W1.5 F35 arms (12 optimize runs) | ~1.5–2 h | yes |
| W1.6–W1.8 golden-eval arms (~60 evals) | ~45 min | yes |
| W2 arm R (8 optimize runs, 2 continue rounds each) | ~1.5–2 h | decision gate for F32 |
| W3 F42 hardening + tests | ~1 h | no |
| W4 decisions | ~30 min | no |
| W5 offline reproduction + entry | ~45 min | no |
