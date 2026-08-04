# Synthetic AF bank — followups wave 3

Plan: [`plans/synthetic-af-bank-followups-wave3-plan.md`](../plans/synthetic-af-bank-followups-wave3-plan.md).
Wave 2: [`docs/synthetic-af-bank-followups-wave2-results.md`](synthetic-af-bank-followups-wave2-results.md).
Register: [`docs/followups.md`](followups.md).

*The question: ship F20's real fix (F35), re-score F24 against recall, and measure the objective's dynamic range
on both banks before anyone proposes a new term for it.*

## Headline

| what | result |
|---|---|
| F35 — seed `MinHFR` out of the cold-start plateau | **Done.** D01/D02 `FinalJ=0` → **0.9902 / 0.9966**; 14/17 synthetic and **19/19 real** landings bit-identical to control |
| …and the validation run caught a bug in it | The seed leaked across runs through a shared params object. **The D05 control is what exposed it** |
| …and the real bank corrected F20 | F20's four `BaselineJ=0` real runs are **a different defect** — the seed fires on none of them |
| F35 step 3 — "widen the low end of the axis" | **Won't fix.** The 0.25 grid does not exist; the axis already resolves to 0.03125 |
| F24 restated — score the nine donut axes against Δrecall | **Not attributable to the search** — it moved none of the nine; the master's own defaults cost the recall |
| F32 — measure `J`'s dynamic range | **Done.** Real bank trades **17.3 points of recall per unit `J`** at the median; worst case 3018 |
| F33 — report the effective gate | **Done.** `mccomiskey` is 9.81, not the 7.5 on record — and it is not the only misread landing |
| F36 (new) — do F1–F8/F18/F21/F25/F26 rest on the broken metric? | **Audited: no.** One clause in one entry, not eleven entries |

## Three premises that did not survive contact with the code

Wave 2's lesson was that a deterministic pipeline reproduces a systematic error perfectly. Wave 3's is narrower
and cheaper: **read the thing before scheduling a run against it.** Three of this wave's inputs were wrong, and
all three were settled by reading code or a landing file — no detector run, no bank pass.

**1. The trigger statistic is not where F35 says it is.** `BestFit.Minimum.Y` is "already read by the wizard"
only *after* the search (`StarDetectionOptimizerWizardVM.cs:3115`). The one seam all five optimizer callers
share sees `RunEvaluationMetrics`, which carries the vertex **X only** (`BestFocusPosition`) and has no
`Minimum.Y` field at all. Had the fix been written where the entry pointed, it would not have compiled; had it
been written at the engine seam with an invented statistic, it would have been the circularity F35 exists to
remove, one level down.

**2. `Continuous` has no grid, so F35 step 3 is not a fix.** `OptimizerVariable.Quantize` is *identity + clamp*
for `Continuous`; `InitialStep` is the **initial pattern-search stride**, halved on every non-improving sweep
down to `InitialStep × StepFloorFraction` = **0.03125**. The "0.45 → 0.20 is a 2.25× jump" framing describes the
first descent stride only. And it is moot in both directions on the rigs that motivated it: `MinHFR` is absent
from Phase A (`CoarseGrid` covers `Sensitivity × StarClippingMultiplier`), so it moves only through
strictly-improving Phase-B moves — which on D01/D02's flat `J` never happen **at any stride**. Retuning the
stride would perturb every rig that *does* have a gradient while doing nothing for the ones the entry is about.

**3. F24's nine axes never moved.** Reading the two config-B landings — one `json.tool` invocation, no run —
shows the search left **all nine at their defaults** on D16, and moved only `DefocusDistortionSizeReference` by
4% (30 → 28.75) on D04. A per-axis ablation would have produced nine null results and attributed nothing.

The trap inside the trap: *default* is not the same as *neutral*. `DonutMorphCloseSize` defaults to **5** and is
neutral at **1**, so an arm that "leaves the axes alone" is still running a 5 px morphological close — which
turned out to be 90% of the cost on D04. See §F24.

## F35 — the seed

**The split that makes it implementable: the fit TRIGGERS, geometry SIZES.**

The trigger is `BestFit.Minimum.Y <= MinHFR` from a fit taken *before* the search, computed by the two callers
that already hold one (the wizard's seed guard; TestApp `optimize`'s `perRunBaseline`) and passed to the engine
as `OptimizerSettings.MinHfrSeedFloor`. The engine applies the clamp once, ahead of θ0, so the seeded value
flows into the seed evaluation, the never-regress floor, and `RevertNeutralAxes`. `synth-validate` and `tilt`
fit no curve before optimizing, do not opt in, and stay bit-identical.

**The size is a constant — 0.30 px in binned detection pixels — and it must not come from a measurement.** Both
available statistics read high, in the direction that would make a fit-derived seed useless: the wing vertex
over-predicts 2.3× (0.548 vs 0.238 on D01) and the survivor medians are **left-censored at `MinHFR` itself**.
The run that shipped confirms the bias in the live path: D01's pre-search fit read **0.762 px** against a truth
vertex of 0.238 — 3.2× high — and the seed worked anyway, because the fit only had to clear 1.2, not measure
0.238. A rule like `0.8 × predicted` would have given 0.61 and gated the true vertex completely.

`MinHFR` needs no arcsec/px conversion: from the software-binning stage the whole pipeline runs in binned pixels
and every pixel-unit param "stays in the range it was calibrated for regardless of the rig"
(`StarDetector.cs:483-486`). What bounds the seed is sampling — HFR cannot fall far below pixel quantization.

Pre-check on the vertex frame (`golden eval --min-hfr 0.30`, D01): `TooLowHFR` falls **1877 → 10**, the vertex
frame carries **5 stars** where it carried 0 — clearing the objective's `NHard` = 3 — and precision stays
**1.000** on every frame. Every other gate is untouched, which is F35's own point: `TooLowHFR` was 1877 of
~38500 missed stars, and the W class's recall is lost in candidate *formation* (`NO CANDIDATE` 18800,
`TooSmall` 6398). **Recall does not recover and was never the criterion.**

### The validation run caught a bug in the fix

The first bank pass looked like a clean sweep: 17/17 hard-floor PASS, exit 0 where every prior pass exited 3.
**It was wrong, and the control is what said so.**

`D05_tec140_1000mm` landed at `MinHFR` **0.300** — the seed constant — against 1.450 in the wave-1 arm. D05's
truth vertex is 1.804 px, comfortably above the 1.2 gate, so its trigger must never fire. Its entire purpose in
F35 is to be the rig that proves an unaffected rig stays unaffected. Fourteen of seventeen datasets landed at
exactly 0.300, while the log printed the "seeding" line exactly **once**.

That pair of facts is the diagnosis. `OptimizeAsync` was writing `seed.MinHFR` **in place**, and callers reuse
one `StarDetectorParams`: TestApp `optimize --per-run` builds its `RunDetectionContext` once
(`OptimizationDiagnosticRunner.cs:306`) *outside* the per-dataset loop (`:426`), and the wizard passes a live
reference to `runs[0].Seed`. So D01 — first alphabetically, and the one rig whose fit genuinely triggered —
permanently re-gated the other sixteen. Each subsequent run then saw a seed *already* at the floor, so
`Resolve` correctly returned null and printed nothing. **One real firing, sixteen silent ones, and a result that
depended on dataset order.**

Fixed by cloning the seed rather than mutating the caller's. The regression test asserts a second, non-triggering
run starts from the original gate; it was confirmed to fail without the fix, because a regression test that
passes either way is worth nothing. The first version of the unit test had itself asserted
`seed.MinHFR == SeedFloor` — it codified the bug and passed.

**The lesson is narrow and repeatable: the control dataset earned its place.** D05 exists in F35 purely to be
boring, and a "boring" reading is the only thing that distinguished a working fix from one that had silently
re-gated the entire bank. Every headline number in the first pass was defensible on its own; only the row that
was supposed to *not* move revealed the defect.

### Results, on the re-run with the leak fixed

**Synthetic bank — 17/17, `EXIT=0` where the wave-1 arm exited 3 (hard-floor failure).**

The seed fired on exactly **three** datasets, each off its own wing fit, and they are exactly the three
undersampled rigs F20 and F35 name:

| dataset | fitted vertex | seed? | `MinHFR` new / control | `FinalJ` new / control |
|---|---|---|---|---|
| `D01_ultrawide_40mm` | 0.762 px | **YES** | 0.100 / 1.200 | **0.99018 / 0.00000** |
| `D02_rich_135mm` | 0.749 px | **YES** | 0.550 / 1.200 | **0.99656 / 0.00000** |
| `D03_redcat_250mm` | 1.015 px | **YES** | 0.300 / 0.450 | 0.99549 / 0.99486 |
| the other 14 | — | no | **identical** | **identical to 5 dp** |

- **2 runs rescued**: D01 and D02 go from `FinalJ` exactly 0 to a real landing. That is F20's whole defect.
- **14 of 17 landings are bit-identical to the wave-1 control**, `MinHFR` and `FinalJ` alike — including
  `D05_tec140_1000mm` at 1.450, the control, and `D12`/`D16`/`D17` whose landings sit *above* the default gate.
- **0 control violations**: no dataset that did not trigger landed at the seed constant.

The 14/17 row is the strongest safety statement available: on every rig the change was not designed for, it is
a **no-op**, not a small perturbation.

Two things the run says that the register did not:

**D17's `BaselineJ = 0` is a different defect.** It reads 0 like D01/D02, but its fit vertex is above the gate,
the seed correctly declines, and its landing is unchanged at `MinHFR` 1.294 / `FinalJ` 0.97768 — it was already
escaping on its own. A `BaselineJ` of 0 is not by itself evidence of the MinHFR collapse.

**The trigger's censoring bias is visible and survivable.** D01's pre-search fit read 0.762 px against a truth
vertex of 0.238 (3.2× high) and D02's read 0.749. Both still cleared 1.2, which is the entire reason a
*trigger-only* use of the fit works where a *sizing* use would not: `0.8 × predicted` would have given 0.61 and
0.60 and re-gated both rigs completely.

**Real bank — 19/19 landings bit-identical to the control, and the seed fired on ZERO runs.**

`MinHFR` and `FinalJ` match the wave-1 arm to five decimal places on every one of the nineteen runs. Zero
rescued, zero violations, and the one hard-floor failure (`lumos`) is the same one the control produces.

That is the strongest possible transfer result in the safe direction — **exactly inert, not "within noise"** —
and it carries a correction that only a real-bank pass could produce.

**F20's real-bank population is not the defect F35 fixes.** F20 says `BaselineJ = 0` on `Panos` and
`LinwoodFocus` is "the same silent null result D01/D02 produce synthetically, on real frames from real rigs."
Measured, it is not. All four real runs with `BaselineJ = 0` — `Panos`, `LinwoodFocus`, `SorenVance`, `lumos` —
have a fit vertex **above** the 1.2 gate, so the trigger correctly declines and their landings are unchanged.
Their `J = 0` comes from having almost no stars at all (`lumos` and `SorenVance` detect **zero** at C0; `Panos`
33 and `LinwoodFocus` 13 across nine frames), not from stars being gated out for measuring too small.

**Two defects were being read as one.** "The objective collapses to exactly zero" has at least two causes: the
`MinHFR` gate emptying the curve's core (synthetic D01/D02, now fixed) and a frame that genuinely has no
detectable signal (real `lumos`/`SorenVance`). They present identically in the log — `currentJ=0 bestJ=0
hard-floor FAIL` — and only the first is a detector-configuration problem. F35 addresses the first and is
provably inert on the second.

So the honest summary of the fix's reach: **real, measured, and with an empty real-bank population on this
bank.** The undersampled short-focal-length rigs it targets are exactly what the synthetic bank was built to
contain and what the real bank happens not to have. That is an argument for the synthetic bank's value, not
against the fix — but it does mean the user-facing benefit is unproven on real frames until such a rig appears
in the bank, and it should not be claimed as proven.

## F24 restated — the nine axes are not the mechanism; the master toggle's defaults are

The task was to score the nine donut-gated axes against Δrecall on `D16_esprit550_ha3` and `D04_esprit_550mm`,
two unobstructed 550 mm refractors where the donut relaxations should be inert and are not (−0.147 and −0.113
under config B). **Reading the two landings first changed the experiment**: the search moved none of the nine.
D16 landed every one at its default; D04 moved only `DefocusDistortionSizeReference` 30 → 28.75. A per-axis
ablation of what the *search* chose would have returned nine nulls and attributed nothing.

What it left behind is not inert, though: `DonutMorphCloseSize`'s default of 5 is **not** its neutral value of 1.
So the question is not "which axis did the optimizer move" but "what does the master switch on when nobody
touches anything" — and the arms below are built to answer that.

So the arms hold every parameter at defaults and vary only what the **master toggle** turns on. `golden eval`,
~40 s per arm, precision **1.000** and FP **0** on every one:

| arm | D16 recall@high | D04 recall@high |
|---|---|---|
| A1 — master OFF | **0.821** (151/184) | **0.762** (16853/22109) |
| A2 — master ON | 0.793 (−0.028) | 0.723 (−0.039) |
| A3 — master ON, structure boost 0 | **0.821** (recovers all) | 0.732 (recovers 23%) |
| A4 — master ON, size-ref 28.75 | 0.793 (**bit-identical to A2**) | 0.723 (**bit-identical to A2**) |
| A5 — master ON, morph-close 1 | 0.799 (recovers 21%) | 0.758 (recovers 90%) |
| **A6 — master ON, boost 0 + morph-close 1** | **0.821 (151/184)** | **0.762 (16853/22109)** |

**A6 recovers the master-OFF recall exactly — the same star counts, not merely the same ratio — on both
datasets.** So two mechanisms account for the master's entire recall cost here, and nothing else in it does:

1. **The silent +2 structure boost.** With the master ON and `DefocusAwareStructure` OFF, `StarDetector.cs:610-615`
   applies `DonutDefaultStructureLayerBoost = 2` anyway, taking `StructureLayers` 4 → 6. More wavelet layers
   subtracted from the residual erases more small-star structure — precisely what a well-sampled refractor loses.
   Dominant on D16 (100% of the cost).
2. **The 5 px morph-close.** `DonutMorphCloseSize` **defaults to 5 and is neutral at 1**, so `--params default`
   plus the master runs a morphological close that merges and reshapes compact stars. Dominant on D04 (90%).

The one axis the search *did* move is provably inert: A4 is bit-identical to A2 on both datasets.

**Two caveats, stated because the numbers invite a wrong comparison.** These arms run at default shared params
(NC 4.0), so −0.028 / −0.039 is the master's own cost, **not** config B's −0.147 / −0.113, which is measured at
NC 2 against B's whole landing. The master residue is roughly a fifth to a third of the observed config-B loss;
the remainder is that B's search landed different values on the *shared* axes (D16 `MinHFR` 0.7, `PeakResponse`
0.7; D04 `StarClip` 0.5, `MinHFR` 0.95).

**And the closing link is F32.** Both mechanisms are reachable by the search — `DonutMorphCloseSize` is a curated
axis, and `StructureLayerBoost` becomes settable once `DefocusAwareStructure` flips on. The optimizer simply
never neutralizes them, because `J` does not pay for recall. F24's loss is not a missing knob; it is
[F32](followups.md#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)
measured on two specific rigs.

**Next step (not taken here):** consider making the master's structure boost and morph-close default to their
neutral values on runs the donut heuristic did not flag, or fold recall into what the objective pays for. Both
are objective/behaviour changes, so per F33 they must be scored on **both** banks.

## F32 — the objective's dynamic range, measured on both banks

All inputs were already on disk; `D:\hf_w3\f32_dynrange.py` reads them with no detector run. Recall comes from
the `/5` synthetic verify and the `/3` real one — valid because `recallHigh` derives only from `match.Pairs`
(`BankVerifyRunner.cs:488`) and the `/5` repair touched only the false-positive list. The real bank's
**precision** from that run is void and is not read anywhere here.

| arm | median `BaselineJ` | median Δ`J` | median Δrecall@≥12 | **median trade rate** |
|---|---|---|---|---|
| synthetic A | 0.952 | +0.0101 | **0.000** | **0.00** |
| synthetic B | 0.992 | +0.0021 | −0.014 | **−1.01** |
| **real A** | 0.977 | +0.0126 | **−0.243** | **−17.32** |

Trade rate = Δrecall@≥12 per unit Δ`J`; negative means recall was given up to buy `J`.

The median reproduces F32's headline exactly (−0.243). Two things it did not say:

**The worst cases are far starker as a rate than as a pair of numbers.** `toml999` gives up **3018 points of
recall per unit of `J`**; `uneven` −134, `CWhiteFocus` −116, `muggsie` −94. "Two ten-thousandths for 46 points"
undersells it — at `BaselineJ` 0.998 the fourth decimal is not a rounding error, it is the entire remaining
scale.

**The two banks disagree in SIGN, not just magnitude.** Config A costs the synthetic bank **no recall at all**
(median trade rate 0.00) while costing the real bank a quarter of it. So the synthetic bank cannot measure this
defect — and F33's "never the synthetic alone" rule applies to a proposed *rescaling* exactly as much as to a
new term. **Any candidate objective change must be shown to move the real-bank trade rate, and the synthetic
bank cannot tell you whether it did.**

## F33 — the effective gate, and how many landings it reclassifies

`max(Sensitivity, PeakResponse × effective StarClip)` now ships as `StarDetector.EffectiveSensitivityGate`,
reported wherever a landing's Sensitivity is quoted.

**The register's own arithmetic was wrong, in the way the register warns about.** F33 computed `mccomiskey` as
`0.75 × 10 = 7.5` using the *default* `PeakResponse`; that landing's own `StarPeakResponse` is **0.98**, so its
effective gate is **9.81**. Both inputs are searched axes — the exact reason the entry gives for not hard-coding
1.5, applied one level down.

**And it is not one odd run.** Landings that read as "Sensitivity at the floor" but are not:

| bank / arm | run | Sensitivity | effective gate | detections C0 → A |
|---|---|---|---|---|
| real A | `mccomiskey` | 0.0 | **9.81** | 3606 → 43 |
| real A | `caboose` | 0.0 | **5.05** | 18 → 22 |
| synthetic A | `D11_rc10_585_afbin2` | 0.0 | **2.36** | 217 → 406 |
| synthetic A | `D12_c14_585_afbin2` | 0.0 | **2.10** | 186 → 475 |

So **2 of the 6** synthetic "Sensitivity 0.0" landings F23 cites are not floor landings (D09/D10/D15/D17 are
genuine, all below the 1.5 inert bound). Reported, not acted on: `SensitivityIsAtFloor` still drives UI
visibility and whether `Recommend` runs at all, and changing that is a behaviour change rather than the
reporting change F33 asks for.

The `afbank-verify` schema is deliberately **not** bumped — the new `effectiveSensitivity` is additive and
derived, no number changes, and the /4 and /5 bumps were for changes that made numbers non-comparable.

## F36 — the audit that replaced a re-measurement pass

"F1–F8, F18, F21, F25, F26 were measured on the landings F32/F33 reinterpret and may need what F23/F24 got" was
an inference, not a check. Checked: **no entry's conclusion depends on the repaired precision metric.** They rest
on σ_focus, recall, R², or landed parameter values, and `recallHigh` never went through the repaired path.

The actual exposure is **one clause in one entry** — F26's "D12 S6 converges even though it fails its own
precision gates", where those gates were the wave-1 `/3` ones. The convergence result stands; that half does not.

Two entries are instead *strengthened* by F33's correction: **F8**'s three "non-reproducible" landings are
27× apart by effective gate (0.19 / 5.2 / 30.1), not merely different corners; and **F3**'s `mccomiskey`
star-shedding example now has its mechanism named — the clip escape route, not a floored Sensitivity.

## What shipped

| change | user-visible? |
|---|---|
| F35 — `MinHfrSeed` + `OptimizerSettings.MinHfrSeedFloor`, applied ahead of θ0 | **yes on an undersampled rig** — but no such rig exists in the real bank, so the benefit is measured only synthetically |
| F35 — the seed must not leak across runs (clone, don't mutate the caller's seed) | no (correctness of the above) |
| F33 — `StarDetector.EffectiveSensitivityGate`, reported at every landing-Sensitivity site | no (diagnostics) |
| F32 / F36 / F24 — measurements and register corrections | no (docs) |

**Deliberately not shipped:** F35 step 3 (the axis stride — the premise is refuted, and retuning it would perturb
every rig that *does* have a gradient); any objective change (F32 shows the two banks disagree in sign, so
nothing can be accepted on the synthetic bank alone); and any change to `SensitivityIsAtFloor` semantics.

## Verification

- Full suite **3371/3371, two consecutive clean runs**. One earlier run showed a single failure while two bank
  passes were saturating the CPU; its name was not captured and it did not recur in four subsequent full runs.
  Recorded as unidentified-intermittent rather than attributed to the known-flaky EAT transport test.
- The seed-leak regression test was confirmed to **fail without the fix**. The first version of the unit test
  asserted the buggy behaviour and passed — which is why the fail-without-fix check exists.
- Both banks completed: synthetic 17/17 (`EXIT=0`, where the control arm exited 3), real 19/19 with one
  hard-floor failure (`lumos`) that the control also produces.

## Reproduce

```
# F35 — the seed, both banks (~55 min each, run detached; build to a separate -o dir, the exe is locked while running)
TestApp optimize --per-run --runs "D:\SyntheticAutofocusBank" --out <dir> --max-evals 250
TestApp optimize --per-run --runs "D:\Autofocus Bank"        --out <dir> --max-evals 250

# F35 — the cheap pre-check (~40 s), which is how the vertex-frame star count was confirmed before spending an hour
TestApp golden eval --runs "D:\SyntheticAutofocusBank\D01_ultrawide_40mm" --params default \
    --min-hfr 0.30 --match centroid --out <dir>

# F24 — the arm set (D:\hf_w3\run_f24_arms.sh)
# F32/F33 — D:\hf_w3\f32_dynrange.py, reads files already on disk
```
