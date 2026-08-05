# Synthetic AF bank — followups wave 5

Wave 4: [`docs/synthetic-af-bank-followups-wave4-results.md`](../docs/synthetic-af-bank-followups-wave4-results.md).
Register: [`docs/followups.md`](../docs/followups.md).
Branch: `ghilios/synthetic-af-bank-followups-wave5`. Base: `develop` (wave 4 merged as PR #173).

*The question: F32 says nothing bounds what `J` may spend. Bound it — as an acceptance constraint, scored on both
banks, with the constraint's own inertness proved rather than assumed.*

---

## What this wave is and is not

**Is:** a feasibility constraint on the optimizer's accept step (F32), the F24 master-default arms that share its
validation path, one ~4-minute verification (F26), and two handoff decisions already taken.

**Is not:** a new objective term, a rescale of `J`, or a retune of `Wtie`. All three are ruled out on evidence:

- **A rescale is provably a no-op.** A monotone transform preserves the argmax, so every landing would be
  identical. It buys human legibility and not one different decision.
- **`Wtie` has already lost this fight.** `OptimizationObjective.cs:60-68` records it being raised 1e-3 → 0.02
  *specifically* to out-vote the star-shedding corner, calibrated against a plateau σ-wiggle of ≲4e-3. The real
  shedders' σ gains are far larger, and F32 measures it being out-voted anyway.
- **The optimizer is not mis-scoring.** σ_focus improves on **10 of 10** real shedders (median ratio **0.320**,
  a 68% reduction) and R² on 9 of 10. It is genuinely buying a better fit with the stars it discards. The defect
  is that nothing bounds the *price*.

So the fix changes the feasible set, never the score.

### There is no cheap instrument for F32, and saying so up front is part of the plan

Wave 4's lesson 5 applies verbatim. `golden eval --sensitivity <s>` (~40 s) measures whether the detector **can**
shed at a *forced* gate. F32 is about whether the optimizer **chooses** to, under a constraint, when it is free to
pick the gate. Only an `optimize` pass answers that. The 40 s instrument is used in this wave for F24 and F26,
where the question really is "what does this parameter do at fixed settings" — and nowhere for F32.

---

## Task 1 — F32: bound the trade

### 1.1 The constraint

A candidate θ is **feasible** iff, for every run `r` in the set being optimized:

```
keep_r(θ) = Σ_f FrameStarCounts_r,f(θ) / Σ_f FrameStarCounts_r,f(seed)   ≥   MinDetectionKeepFraction
```

with `keep_r := 1.0` when the seed's total for that run is 0 (undefined ratio ⇒ exempt; this must not re-gate the
F20/F35 escape, where the seed can legitimately detect nothing).

Four properties this is chosen for:

1. **`J` is untouched.** `BaselineJ`, `FinalJ`, `SeedJ`, `BestJ` keep their current numeric meaning, so every
   landing stays comparable to every prior arm. This is the whole reason it is a rejection and not a multiplier.
2. **The seed is always feasible** (keep = 1.0 by construction), so the feasible set is non-empty and the
   never-regress floor is preserved. Worst case the search returns the seed.
3. **The baseline is the run's own seed**, and in the headless path the seed is
   `BuildDefaultStarDetectorParams()` (`OptimizationDiagnosticRunner.cs:242`) — i.e. exactly the **C0** of every
   `C0 → A` table in F32/F33. The constraint is stated in the same units the defect was measured in.
4. **Min over runs, not the pooled total.** Pooling lets one dense run's gains mask another run being gutted.
   For the headless one-run-per-call case the two are identical, so nothing in the bank arms is affected by the
   choice; it only bites in the wizard's N>1 blend, where it is the safer reading.

### 1.2 The seam

`SearchContext.EvalJ` (`StarDetectionOptimizer.cs:272`) is the single point every `bestJ`/`FinalJ` flows through,
and `RunEvaluationMetrics.FrameStarCounts` (`OptimizationObjective.cs:283`) is already in hand there on a cache
miss. Three accept sites consume its result:

| site | line | compare |
|---|---|---|
| `CoarseGrid` | `:356` | `j > bestJ` |
| `CompassStage` | `:533` | `j > improvedJ` |
| `RevertNeutralAxes` | `:423` | `j >= bestJ` |

The rejection goes **ahead of** each compare: `if (eval.Feasible && eval.J > bestJ)`.

`RevertNeutralAxes` is included deliberately even though pulling back toward the seed usually *raises* the count:
"usually" is not "always", and a uniform rule at all three sites is one invariant instead of three arguments.

### 1.3 Implementation

**T1.1 — `OptimizerSettings.MinDetectionKeepFraction` (`double?`, default `null`).** Null ⇒ every caller
bit-identical, exactly the `MinHfrSeedFloor` pattern at `:56`. `synth-validate` and `tilt` stay untouched by
construction.

**T1.2 — `EvalJ` returns a small readonly struct** `CandidateEvaluation { double J; bool Feasible; double
KeepFraction; }` instead of `double`, memoized alongside the existing `memo`/`memoSigma` dictionaries. Five call
sites update. Keeping one evaluation method preserves "the single point everything flows through".

**T1.3 — the seed establishes the baseline explicitly.** `EvalJ(theta, isSeed: true)` at `:154` records the
per-run seed totals and returns `Feasible = true` unconditionally. An optional parameter rather than "the first
call is the seed by construction" — implicit call-order coupling is precisely the shape of the wave-3 seed leak.

**T1.4 — close the `--continue-rounds` ratchet.** `OptimizeAsync` is called repeatedly with `seedN =
result.BestParams` (`OptimizationDiagnosticRunner.cs:~675`, mirroring the wizard's Continue button). A cap
relative to *each call's own* seed compounds: at φ = 0.5, two continue rounds permit 12.5% of the original —
the constraint paid in installments. So:

- `OptimizationResult` gains `SeedRunDetectionTotals`.
- `OptimizerSettings` gains `DetectionKeepBaselineTotals` (optional); when set it overrides the seed-derived
  baseline.
- The two multi-pass callers (TestApp `--continue-rounds`, the wizard's Continue) set it once from the **first**
  pass's result and hold it for every later round.

A regression test asserts a 3-round chain cannot fall below φ of the *original* seed.

**T1.5 — reporting.** `OptimizationResult` gains `LandingKeepFraction` (min over runs) and
`CandidatesRejectedByKeepFloor`. Surfaced in `optimize`'s console, `optimize_summary.txt`, and
`aggregate_summary.*`. `optimized_settings.json` gains `MinDetectionKeepFraction` (the floor in force) and
`LandingDetectionKeepFraction` (what was measured), **written only when the floor is non-null**.

No `SchemaVersion` bump: both fields are additive, no existing number changes meaning, and an absent field
already reads correctly as "no floor was in force". This follows F33's `EffectiveSensitivityGate` precedent and
directly answers F39's complaint — a file that records a setting the run did not use is worse than one that
records nothing.

**T1.6 — `TestApp optimize --keep-floor <f>`.** Arm selection by flag on one binary, per the F23 pattern.

**T1.7 — tests, counted by what they DISCRIMINATE.** Wave 4's lesson 2: the first F38 coverage was five tests of
which exactly one failed on revert. **Measured** by neutralizing the feasibility test at all three accept sites
and re-running: **8 of 12 fail, 4 pass either way.** The four are kept and labelled as guards, not counted:

| test | discriminates? |
|---|---|
| `Floor_RejectsALandingThatShedsBelowIt` | **yes** |
| `Floor_IsMinOverRuns_NotThePooledTotal` | **yes** |
| `Floor_DoesNotRatchetAcrossContinueRounds` | **yes** |
| `Floor_SeedIsAlwaysFeasible` | **yes** |
| `Floor_LandingAlwaysSatisfiesTheFloor` (×4 floors) | **yes** ×4 |
| `NoFloor_IsBitIdenticalToTheUnconstrainedSearch` | no — the inertness guard that lets one binary be its own control arm |
| `Floor_AcceptsTheSameCandidatesWhenLoweredBeneathThem` | no — a floor beneath everything visited is inert by construction |
| `Floor_DoesNotAlterJ` | no — guards "never a multiplier", which the revert does not break |
| `Floor_ExemptsARunWhoseSeedDetectedNothing` | no — an unconstrained search also moves off a zero-star seed |

The fixture itself is the load-bearing part: raising `Sensitivity` both tightens σ and sheds stars, so the
**unconstrained** search lands at keep **0.10** — the defect reproduced in miniature. Without that precondition
(asserted in the tests) a passing floor test would prove nothing.

### 1.4 The arms

**Pre-register everything below before the first run starts.**

**Populations.** Per floor φ, a run is **BINDING** iff its unconstrained keep% < φ, else **INERT**. Unconstrained
keep% comes from files already on disk (`hf_f23/H_real_A`, `hf_w4/optA`) — free, no detector run.

Known keep% (wave 1/3/4): `mccomiskey` 1.2%, `CWhiteFocus` 29.6%, `standard_example1` 29.4%, `uneven` 32.8%,
`toml999` 33.7%, `D18` 48.8%, `D19` 59.2%, `bobp` 85.3%, `D20` 94.8%.

**Screening subset (8 runs).** Real shedders `toml999`, `CWhiteFocus`, `uneven`, `muggsie`, `mccomiskey`;
synthetic `D18_m24_deep_shed`, `D19_cygnus_deep_shed`, `D20_m24_bright_control`. D20 is the control that wave 4
built and validated; it must stay INERT at every floor below 0.94.

**Floors:** φ ∈ {0.30, 0.50, 0.75}, plus a feature-OFF arm on the same 8 runs with the same binary.

**Acceptance criteria, fixed now:**

| # | criterion | scope |
|---|---|---|
| **C0** | feature-OFF landings **bit-identical** to the **PARENT COMMIT's binary** (`c97e1a3`, built to `D:\hf_w5\exe_base`) | toml999 + D20. Hard. A miss stops the wave — the plumbing is not inert |
| **C1** | landing keep% ≥ φ | every BINDING run. Exact; a violation is a bug, not a result |
| **C2** | median Δrecall@≥12 vs C0 improves from −0.243 to **≥ −0.10** | real BINDING runs |
| **C3** | median σ_focus(landing)/σ_focus(seed) **≤ 0.60** (unconstrained: 0.320) | real BINDING runs |
| **C4** | precision **≥ 0.99** | every synthetic dataset, exact metric |
| **C5** | Δrecall vs the unconstrained landing ≥ −0.01 **and** σ ratio ≤ 1.05 | D20 + INERT real runs |
| **C6** | ≤ 25% of real runs collapse to `ImprovedOverSeed = false` | real bank |

**Chosen floor = the SMALLEST φ meeting C1–C6** — minimum intervention that achieves the goal, since a larger
floor recovers more recall but gives back more σ. If no floor clears, ship nothing and record why.

**C5 is not a bit-identity claim, on purpose.** A landing can be feasible while a candidate *visited on the way*
was not; rejecting that candidate legitimately changes the trajectory. So INERT runs are allowed to move, and the
number that moved is **counted and reported** rather than waved through.

> **C0 was written wrong the first time, and the arm caught it in 12 minutes.** The original criterion compared
> the feature-OFF landing to `hf_f23/H_real_A` — the WAVE-1 control arm. It failed on `toml999` across nine
> knobs, which reads as a regression and is not one: **`BaselineJ` differs too (0.99784 → 0.98348)**, and
> `BaselineJ` is the *current settings*' score, computed with no search at all. A changed `BaselineJ` can only
> mean the objective or the detector changed — which they did, five times, across waves 2–4 (`5115885`,
> `c2db33e`, `7b5a695`, `238623d`, plus PR #174).
>
> **A prior wave's arm is not a control for this wave's binary.** The only valid inertness baseline is the
> immediate parent commit, and the only valid baseline for each φ arm is this binary's own feature-OFF arm — which
> the design already contains. Generalized as [F41](../docs/followups.md).

**Confirmation arm.** At the chosen floor only: full synthetic bank (20) + full real bank (19).

**The discriminating re-run F32 asks for.** Re-run F33's acceptance test on D18/D19/D20 with the constraint
reverted; it must reproduce trade rates −53.4 / −40.4 / **+44.9** and keep% 48.8 / 59.2 / 94.8. This is what
proves the arm measures the constraint and not incidental drift. It is covered by the feature-OFF screening arm,
which contains exactly those three datasets.

### 1.5 Adoption

Turning the floor on by default is a **separate commit after the arms**, not part of T1.1–T1.7. If adopted, the
wizard also needs one line of user-facing copy for the case where the constraint bound the landing ("held to ≥X%
of the stars your current settings detect") — otherwise a user sees a smaller improvement with no visible cause.

---

## Task 2 — F24: the master's two silent defaults

### 2.1 What the entry actually asks for, and the constraint on answering it

F24's remaining step reads "default the master's +2 structure boost and 5 px morph-close to neutral on runs the
donut heuristic did not flag."

**There is no runtime donut heuristic to gate on.** `DonutHeuristic.Decide` lives in
`TestApp/BankVerification.cs:159` — it is a bank-audit tool, not a detector signal, and the plugin has no
per-run donut flag at detection time. So the literal phrasing is not implementable, and the implementable
version is: **stop the master silently applying two mechanisms the user never asked for**, leaving both reachable
through axes that already exist.

| mechanism | today | neutral form | already an axis? |
|---|---|---|---|
| +2 structure boost | `StarDetector.cs:613` applies `DonutDefaultStructureLayerBoost = 2` when the master is ON and `DefocusAwareStructure` is OFF | delete that branch; `StructureLayerBoost` governs when `DefocusAwareStructure` is on | yes — `DefocusAwareStructure` + `StructureLayerBoost` |
| 5 px morph-close | `StarDetector.cs:699` runs it when master ON and `DonutMorphCloseSize > 1`; the default **is** 5 (`IStarDetector.cs:407`, `StarDetectionOptions.cs:282,364`, `HocusFocusStarDetection.cs:444`) | default 5 → 1 | yes — `DonutMorphCloseSize` is a curated optimizer axis |

### 2.2 The keep floor does NOT dissolve this — check before assuming it does

Tempting shortcut: "with Task 1 shipped, the search is forced to neutralize anything that sheds." It is not. The
seed has the master **OFF**, and the master's own cost is −0.028/−0.039 recall (wave 3, at default shared params)
— a candidate flipping the master ON stays comfortably feasible at any φ ≤ 0.9. Task 1 bounds catastrophic
shedding; it does not price a 3% one. The two tasks are independent and both are needed.

### 2.3 The arm is nearly free, because the flags already exist

`golden eval` already supports every override this needs — `--defocus-donut`, `--defocus-structure`
`--structure-layer-boost 0`, `--donut-morph-close 1` (`GoldenEvalRunner.cs:461-479`). **No detector code changes
to run the arm.** Four arms × 20 datasets × ~40 s ≈ 55 minutes total, with exact precision.

| arm | flags (all with `--params default --defocus-donut`) |
|---|---|
| shipping | — |
| boost-neutral | `--defocus-structure --structure-layer-boost 0` |
| close-neutral | `--donut-morph-close 1` |
| both-neutral | both of the above |

Wave 3 ran exactly this on D16/D04 and recovered master-OFF recall **exactly** — the same star counts, not
merely the same ratio. What is unmeasured is (i) the cost on the genuine-donut datasets, (ii) the σ_focus/`J`
effect, and (iii) the real bank.

**Then the expensive half, on donut datasets only:** `optimize --per-run --donut` over the synthetic ε>0 rows
(D08, D09, D11, D12, D14, D15, D17) and the real donut runs (`Panos`, `mufti`, `LinwoodFocus`, `FlyData`),
shipping vs both-neutral.

**Pre-registered — the change ships only if all four hold:**

1. Recall Δ ≥ **−0.005** on **every** synthetic dataset, the genuine-donut rows included.
2. Precision ≥ **0.99** everywhere (exact metric).
3. Median σ_focus ratio (both-neutral / shipping) ≤ **1.10** on the donut datasets. F24 measured donut detection
   improving D13's fit **eightfold**; if the boost and the close are what buy that, neutralizing them is refused.
4. Real-bank donut runs: no σ_focus or `J` regression beyond the same 1.10. **Recall is not scored on the real
   bank** — per F11 real-data precision/recall is only a lower bound, so the real half is a σ/`J`/count check.

Note the two neutralizations are scored **separately as well as together**, because they have opposite dataset
dominance (boost dominates D16, close dominates D04) and one may pass while the other fails.

If it ships, `DonutMorphCloseSize`'s default change is user-visible: it needs an
`Resources/OptionsDataTemplates.xaml` check (the control exists — only the default moves) and a line in the
manual.

---

## Task 3 — F26's unverified clause (~4 minutes)

F36 records exactly one clause across F1–F8/F18/F21/F25/F26 that still rests on the `/3` metric: F26's *"D12 S6
converges … even though it fails its own precision gates."* Those gates are F23 arm (a)'s (precision ≥ 0.90),
which arm (a) failed on **D09, D12, D15** at `/3`.

The landings are on disk (`D:\hf_f23\a_A\<DS>_attempt01\optimized_settings.json`, verified present), and
`golden eval` applies the `/5` `TruthProtection` repair (`GoldenEvalRunner.cs:273-279`). So:

```
TestApp golden eval --runs "D:\SyntheticAutofocusBank\<DS>" --params optimized \
    --opt-results "D:\hf_f23\a_A\<DS>_attempt01" --match centroid --match-radius 12 --out <dir>
```

for `DS ∈ {D09_c14_3800mm, D12_c14_585_afbin2, D15_cdk20_3454mm_e47}`, **and the same three against the control
arm `H_A`** so the result is an A/B and not an absolute. Six evals, ~4 minutes.

`ResolveSnapshot` takes `--opt-results` as a **direct folder** containing `optimized_settings.json`, not a bank
root — hence the `_attempt01` suffix in the path above.

Expected: the earlier truth-scored table already reads arm (a) D12 = **1.000**, so the clause most likely
**refutes**. Record the outcome either way in F26 and close F36's open clause. This does not reopen F23, which is
"won't fix as written" on F31's grounds, not on this clause.

---

## Task 4 — the two decisions (both taken)

**4a — backfill: convert in place, no re-run.** New `TestApp bank-export-settings --runs <bank-root>` reads each
run folder's existing `optimized_settings.json` and writes `hocusfocus_star_detection.json` beside it, through
the **same** DTO→options mapping the optimize path uses. No optimizer runs, so F15 is never touched and no
landing changes. A test asserts every existing bank landing round-trips with no axis dropped (the reflected-name
mapping already has a no-axis-unmapped test; this extends it to the on-disk corpus).

**Sequencing matters:** run this **before** any Task-1 arm, or the 8 screening runs' folder settings will have
been rewritten by `optimize --per-run` and the backfill will capture wave-5 landings instead of their current
ones.

Do **not** rename to `optimized_settings.json` — `golden eval --params optimized`, `bank-verify`, `review` and
`inspect-align` match that name exactly and would deserialize an envelope as a bare
`OptimizedStarDetectionSettings`, binding every curated knob to its CLR default (Sensitivity 0, MinHFR 0,
StructureLayers 0) and scoring a completely different detector with no error and no warning.

**4b — wizard pickup: an explicit button, never automatic.** An "Import settings from this run" action the user
presses, showing the existing diff before anything changes. "Current" keeps meaning the live profile, so
`baselineJ` and the headline improvement percentage keep the meaning they have today. Auto-pickup was rejected:
it silently redefines "Current", which changes what the headline percentage measures with no visible cause — the
wave-3 seed-leak shape one level out.

---

## Order of work

| # | step | cost | blocks |
|---|---|---|---|
| 1 | Task 3 — F26 verify at `/5` | ~4 min | nothing |
| 2 | Task 4a — `bank-export-settings` + backfill both banks | ~1 h + seconds | **must precede any arm** |
| 3 | Task 2 — the four `golden eval` arms, 20 datasets | ~55 min | nothing (no code) |
| 4 | Task 1 — T1.1–T1.7 code + tests | — | the arms |
| 5 | Task 1 — screening arms (3 floors + feature-OFF × 8 runs) | ~5.7 h detached | the floor choice |
| 6 | Task 2 — `optimize --donut` arms on the donut datasets | ~2.5 h detached | the F24 verdict |
| 7 | Task 1 — confirmation arm at the chosen floor, both banks | ~6 h detached | adoption |
| 8 | Task 4b — the explicit import button | ~2 h | nothing |
| 9 | Adoption commits, results doc, followups, PR | — | — |

Steps 5–7 total ~14 h of detached compute. Run with progress files; build to a separate `-o` directory because
the exe is file-locked while running.

## Discipline carried forward

- **Read the thing before scheduling a run against it.** Wave 4's `Bin2` scare was a complete, plausible
  explanation of F33 that would have justified re-baselining both banks; one grep for who reads the field killed
  it. Applied here already: `DonutHeuristic` is TestApp-only, which is why Task 2 is restated rather than
  implemented as F24 words it.
- **Count tests that discriminate.** Every test in T1.7 is verified by reverting the fix.
- **An entry's stated blocker is a claim.** F24's "the donut heuristic did not flag" and F26's precision clause
  are both checked against the code and the metric rather than inherited.
- **A landing that moves a knob is not evidence of shedding** — what separates D18/D19 from D20 is the cost.
- **F15:** `optimize --per-run` rewrites settings INTO run folders. Run only the datasets under test, read the
  `--out` copies, and snapshot the 8 screening runs' folder settings before step 5. F15 is **not** fixed inline:
  changing where settings are written mid-wave would silently change what `--params optimized` reads for every
  subsequent scoring run.
- **F37:** on a red CI check, suspect the native test-host crash before a regression, and verify the test
  **count**, not just the badge. Never merge on red. Baseline: **3416 passing** on `develop` @ `757ef90`.
- Specs go in `docs/` (`-design.md`), plans in `plans/` (`-plan.md`), findings get FLAGGED in `docs/followups.md`
  rather than fixed inline. Never push to `develop`; commit with `322725+ghilios@users.noreply.github.com`.

## Reproduce

```
# Task 3 — the one unverified clause, exact precision (~40 s each)
TestApp golden eval --runs "D:\SyntheticAutofocusBank\<DS>" --params optimized \
    --opt-results "D:\hf_f23\{a_A|H_A}\<DS>_attempt01" --match centroid --match-radius 12 --out <dir>

# Task 2 — master-default arms, no code changes needed (~40 s each)
TestApp golden eval --runs "D:\SyntheticAutofocusBank\<DS>" --params default --defocus-donut \
    [--defocus-structure --structure-layer-boost 0] [--donut-morph-close 1] --match centroid --out <dir>

# Task 1 — screening arm, ONE floor, the 8 subset runs only (F15)
TestApp optimize --per-run --keep-floor 0.50 --runs "<run>" --out "D:\hf_w5\phi050\<run>" --max-evals 250

# Build (exe is file-locked while running)
dotnet.exe build "$(wslpath -w <abs>/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w5\exe'
# Tests
dotnet.exe test "$(wslpath -w <abs>/Joko.NINA.Plugins.sln)" -c Debug --nologo
```
