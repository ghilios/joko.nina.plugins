# Synthetic AF bank — followups wave 5

Plan: [`plans/synthetic-af-bank-followups-wave5-plan.md`](../plans/synthetic-af-bank-followups-wave5-plan.md).
Wave 4: [`docs/synthetic-af-bank-followups-wave4-results.md`](synthetic-af-bank-followups-wave4-results.md).
Register: [`docs/followups.md`](followups.md).

*The question: bound what `J` may spend. All four items answered. Two of the answers are "no", the biggest one
inverts the premise it was built on, and the two most expensive findings are about measurement rather than about
the detector.*

## Headline

| what | result |
|---|---|
| F32 — bound the trade | **Shipped default OFF, proven inert — and the arms overturn the entry's own framing:** on 5 of 7 binding runs the constraint IMPROVES `J` while keeping 2–4× more stars |
| F24 — the master's two silent defaults | **Answered: do NOT neutralize them.** The wave-3 conclusion came from 2 datasets and fails on 20 |
| F26 — the one clause left on the `/3` metric | **Verified and REFUTED** in four minutes. Arm (a) scores precision **1.000** where it was recorded at 0.451–0.653 |
| Backfill decision | **Shipped** — 42 landings converted in place, no re-run, F15 never touched |
| Wizard pickup decision | **Shipped** — explicit button, never automatic |
| F40 (new) | The wave-4 settings handoff **had never once been written to disk** |
| F41 (new) | A prior wave's control arm is **not a control** for a later wave's binary. `BaselineJ` is the free tell |
| F42 (new) | **Every build directory silently gets its own detector settings** — and the run instructions require a new one per arm |

## F32 — the constraint, and why it is a constraint

F32 reads as "the optimizer trades enormous recall for numerically trivial gains". Arithmetically true; the
misreading points at the wrong fix, and wave 4 had already established why:

- **The optimizer is not mis-scoring.** σ_focus improves on **10 of 10** real shedders (median ratio **0.320**)
  and R² on 9 of 10. It is genuinely buying a better fit with the stars it discards.
- **The trade is 5.7:1 by construction.** `SStars` hard-clamps at `nMin ≥ 8` / `nMedian ≥ 20` — a *starvation
  detector*, not a star-count reward — so the only unsaturating star term is the tie-breaker's
  `0.6·nMean/(nMean+20)` at `Wtie = 0.02`, worth at most **0.012 of `J`** across the entire range from zero
  stars to infinite stars, while a 10% σ tightening buys **+0.0037**.

So **nothing bounds what `J` may SPEND**, which makes the fix a feasibility constraint. The two alternatives were
ruled out on evidence rather than taste: **rescaling `J` is provably a no-op** (a monotone transform preserves
the argmax, so every landing would be identical), and **`Wtie` has already lost this fight** — it was raised
1e-3 → 0.02 *specifically* to out-vote this corner, and F32 measures it being out-voted anyway.

### What shipped

A candidate keeping less than `MinDetectionKeepFraction` of the **seed's** accepted stars (min over runs) is
rejected **ahead of** the `j > bestJ` compare, at all three accept sites (`CoarseGrid`, `CompassStage`,
`RevertNeutralAxes`). `J` is never multiplied or re-anchored, so every landing stays numerically comparable to
every prior arm. The seed is feasible by construction, so the feasible set is never empty and the never-regress
floor survives.

Three details that are not decoration:

- **Min over runs, not the pooled sum.** Pooling lets one dense run's gains mask another being gutted. Identical
  for the headless one-run-per-call case, so no bank measurement turns on it; it only bites in the wizard's N>1
  blend, where min is the safer reading.
- **A run whose seed detected nothing is EXEMPT.** The ratio is undefined, and rejecting on it would re-gate
  exactly the F20/F35 population — a rig whose seed legitimately detects nothing is the case the `MinHFR` seeding
  exists to rescue, and it must be free to climb from zero.
- **The multi-pass ratchet is closed.** Both multi-pass callers re-invoke the optimizer with
  `seed = the previous pass's best` (TestApp `--continue-rounds`, the wizard's Continue button). Measured against
  each round's *own* seed, a floor of 0.5 permits 0.25 after two rounds and 0.125 after three — the constraint
  paid in installments. Round 0's seed totals are pinned instead.

### Verification

**12 tests, of which 8 fail** when the feasibility test is neutralized at all three accept sites; the other four
are labelled guards and **not counted** (wave-4 lesson 2). The fixture is the load-bearing part: raising
`Sensitivity` both tightens σ and sheds stars, so the *unconstrained* search lands at keep **0.10** — the defect
reproduced in miniature. A floor test that passes without that precondition proves nothing, so the precondition
is asserted.

**Inertness measured, not assumed.** The parent commit (`c97e1a3`) was built to its own directory and run against
the wave-5 binary with no floor, **settings pinned to one file**, on `D05_tec140_1000mm`:

| binary | currentJ → bestJ | evals | landing |
|---|---|---|---|
| `c97e1a3` | 0.999293 → 0.999693 | 234 | — |
| wave 5, no floor | 0.999293 → 0.999693 | 234 | **bit-identical** |

### The screening arms, and the result that overturns this entry's own framing

32 optimizations: φ ∈ {0.30, 0.50, 0.75} plus a feature-OFF control, over 5 real shedders and D18/D19/D20, one
binary, `--settings` pinned (F42). **`BaselineJ` is identical across all four arms of every run** — the F41 tell,
confirming the comparison is valid before any knob is read.

**The control arm's own keep fractions**, now recorded on every landing, and they reproduce wave 4 independently
on a different binary (D18 48.8% → 0.511, D19 59.2% → 0.600, D20 94.8% → 0.948):

| run | keep, unconstrained | binds at 0.75 | 0.50 | 0.30 |
|---|---|---|---|---|
| `mccomiskey` | **0.095** | ✓ | ✓ | ✓ |
| `uneven` | 0.353 | ✓ | ✓ | — |
| `toml999` | 0.397 | ✓ | ✓ | — |
| `CWhiteFocus` | 0.429 | ✓ | ✓ | — |
| `D18_m24_deep_shed` | 0.511 | ✓ | — | — |
| `D19_cygnus_deep_shed` | 0.600 | ✓ | — | — |
| `muggsie` | 0.612 | ✓ | — | — |
| **`D20` (control)** | **0.948** | — | — | — |

**THE HEADLINE: on most binding runs the constraint IMPROVES `J` while keeping 2–4× more stars.** At φ = 0.75:

| run | keep off → φ=0.75 | Δ`J` | σ_focus vs unconstrained |
|---|---|---|---|
| `CWhiteFocus` | 0.429 → **1.819** | **+0.00139** | **0.156×** |
| `uneven` | 0.353 → 0.937 | +0.00165 | 0.785× |
| `D19_cygnus_deep_shed` | 0.600 → 1.276 | +0.00024 | 0.364× |
| `toml999` | 0.397 → **1.163** | +0.00054 | 0.959× |
| `D18_m24_deep_shed` | 0.511 → **1.819** | +0.00009 | 1.337× |
| `muggsie` | 0.612 → 0.805 | −0.00234 | 1.676× |
| `mccomiskey` | 0.095 → 0.771 | −0.03097 | 2.812× |

**5 of 7 binding runs land at a HIGHER `J` than the unconstrained search found**, with more stars and (on 4 of 5)
a tighter fit. `CWhiteFocus` is the clearest: σ_focus 1.578 → **0.246** while keeping 1.819× the seed's stars
instead of 0.429×.

**A constrained maximum cannot exceed an unconstrained GLOBAL maximum.** So this is not the constraint being
free — it is proof that **the unconstrained search was not finding the global optimum**. The shedding corner is
substantially a **greedy trap**, not the rational purchase F32 and wave 4 concluded it was. The mechanism is the
one `RevertNeutralAxes` already documents, one level up: Phase A grids Sensitivity × StarClip with Sensitivity
as the OUTER loop, the winning StarClip is first discovered in a shedding row, and strict `j > bestJ` freezes it
there. The floor prunes that branch early and the search explores a genuinely better region.

**This corrects wave 4 and F32's own text.** "The optimizer is behaving rationally — it is genuinely buying a
much better fit with the stars it discards" is true *relative to the baseline* and false as a claim about the
best available trade. On 5 of 7 runs it could have had both.

### Inertness, measured twelve times rather than argued

Across the 12 (run, floor) pairs where the floor does **not** bind, **9 land bit-identical** to the control —
including **D20 at all three floors**, exactly as the wave-4 control was designed to behave:

| pair | result |
|---|---|
| D20 × {0.75, 0.50, 0.30} | **bit-identical** ×3 |
| D19 × {0.50, 0.30} | **bit-identical** ×2 |
| muggsie × {0.50, 0.30} | **bit-identical** ×2 |
| toml999, uneven × {0.30} | **bit-identical** ×2 |
| CWhiteFocus × {0.30}, D18 × {0.50, 0.30} | changed (see below) |

The three that changed are **the pre-registered C5 caveat happening**, not a failure: a landing can be feasible
while a candidate *visited on the way* was not, and rejecting that candidate legitimately changes the trajectory.
Two of the three (D18) changed for the **better** (`J` +0.000076, recall@high 0.607 → **0.945**); one
(`CWhiteFocus`) is −0.0001 of `J`. Counted and reported rather than waved through, which is why C5 was written
as a bound rather than as bit-identity.

### Exact recall on the synthetic arms — precision 1.000, FP 0, everywhere

| dataset | arm | recall@high | recall@all | TP | FP | precision |
|---|---|---|---|---|---|---|
| `D18_m24_deep_shed` | off | 0.607 | 0.168 | 13936 | 0 | 1.000 |
| | φ=0.75 | 0.622 | **0.402** | **33319** | 0 | 1.000 |
| | **φ=0.50** | **0.945** | 0.346 | 28676 | 0 | 1.000 |
| `D19_cygnus_deep_shed` | off | 0.968 | 0.433 | 5492 | 0 | 1.000 |
| | φ=0.75 | **0.984** | **0.864** | **10963** | 0 | 1.000 |
| **`D20` (control)** | **all four arms** | **0.960** | **0.905** | **9073** | 0 | 1.000 |

**D20 is identical to the last detection at every floor** — the control earning its keep for the second wave
running. **C4 passes decisively**: not one false positive appears at any floor, so every star the constraint wins
back is a real one.

Note φ=0.75 **overshoots on D18**: its effective gate collapses to 0.234, which admits masses of faint stars
(TP 33319) while *losing* high-tier ones relative to φ=0.50 (recall@high 0.622 vs 0.945). A floor set too high
can push the search into the opposite corner — the Sensitivity-floor pathology F23/F33 describe, reached from
the other side.

### Verdict against the pre-registered criteria

| # | criterion | result |
|---|---|---|
| C0 | inert vs the parent commit | **PASS** — bit-identical, settings pinned |
| C1 | landing keep ≥ φ on every binding run | **PASS** at all three floors |
| C2 | efficacy on real binding runs | **not evaluable as written** — needs real-bank recall (`bank-verify`), not run. Keep% is the proxy: median 0.397 → 1.163 at φ=0.75 |
| C4 | precision ≥ 0.99, synthetic | **PASS** — 1.000, FP 0, every arm |
| C5 | control and inert runs do not degrade | **PASS** — D20 identical ×3; 9/12 inert pairs bit-identical; the 3 exceptions are +, +, −0.0001 |
| C6 | ≤ 25% of real runs collapse to no improvement | **PASS** — none did |

**Recommended floor: φ = 0.50**, by the pre-registered rule (*the smallest φ meeting the criteria*). φ = 0.30
does not address the defect — only `mccomiskey` binds and it pays. φ = 0.50 improves `J` on 3 of 4 binding runs,
leaves all four inert runs untouched (3 of 4 bit-identical), and produces the single largest measured recall
gain (D18 recall@high 0.607 → **0.945**) without φ=0.75's gate collapse.

**Adoption still needs the confirmation arm** — both full banks at φ = 0.50, plus `bank-verify` for real-bank
recall so C2 can be evaluated as written rather than by proxy. The constraint ships **default OFF** until then,
so nothing in this wave depends on that verdict.

## F24 — answered, and the answer is no

The entry's remaining step asks whether to default the master's `+2` structure boost and 5 px morph-close to
neutral. **Two things had to be corrected before it could even be attempted.**

**The literal phrasing is not implementable.** F24 says "on runs the donut heuristic did not flag", but
`DonutHeuristic.Decide` lives in `TestApp/BankVerification.cs:159` — it is a **bank-audit tool, not a detector
signal**, and the plugin has no per-run donut flag at detection time. Read before building.

**And the arm needed no code at all.** `golden eval` already exposes every override
(`GoldenEvalRunner.cs:461-479`), so five arms × 20 datasets cost ~55 minutes with exact precision, against a
planned `optimize` campaign.

### Wave 3 reproduces exactly — and does not generalize

D16 masterOFF 0.821 → shipping 0.793, `boost0` restores 0.821. D04 0.762 → 0.723, `close1` restores 0.758.
Neutralizing **both** recovers master-OFF recall on **19 of 20** datasets. Precision is **1.000 on every arm and
every dataset**. The mechanism claim is confirmed on the whole bank.

**But the two mechanisms are a rig-dependent trade, not a uniform cost:**

| direction | datasets | Δrecall (masterOFF − shipping) |
|---|---|---|
| master **HURTS** recall | 12 — D01–D05, D07, D10, D13, D16, D18, D19, D20 | +0.007 … **+0.067** |
| master **HELPS** recall | **3 — D06, D09, D14** | −0.005 … **−0.067** |
| no effect | 5 — D08, D11, D12, D15, D17 | 0.000 |

On all three where it helps, `boost0` gives the gain back **exactly** — so the `+2` structure boost is what buys
it, and the morph-close is inert there. And those three are `D06_sparse_1000mm`, `D09_c14_3800mm`,
`D14_cdk14_2563mm_e47`: long focal length and sparse, i.e. **large defocused stars**, precisely what a coarser
wavelet residual exists to preserve. The story reads correctly in both directions — the boost saves big
defocused stars from the subtraction and erases small-star structure.

**The pre-registered criterion FAILS**: both-neutral costs **−0.067 on D09**, −0.024 on D06, −0.005 on D14. A
blanket default change takes recall from exactly the rigs the feature was built for.

**So the fix is not a default but a condition** — on star size. The optimizer can already reach the neutral point
(`DefocusAwareStructure = true, boost = 0`); it never goes there because `J` does not pay for recall, which is
F32. Note the keep floor does **not** dissolve this: the seed has the master OFF and the master's own cost is
≤ 0.067, so flipping it on stays feasible at any floor ≤ 0.9. The two entries are independent.

## F26 — the last clause resting on the broken metric, refuted in four minutes

[F36](followups.md) identified exactly one clause across F1–F8/F18/F21/F25/F26 still resting on the `/3` metric:
F26's *"D12 S6 converges … even though it **fails its own precision gates**."* Those gates are F23 arm (a)'s
(precision ≥ 0.90), which arm (a) failed on D09, D12 and D15.

The landings were already on disk and `golden eval` applies the `/5` `TruthProtection` repair, so this cost six
evals:

| dataset | control `H_A` | **arm (a)** | arm (a) as recorded at `/3` |
|---|---|---|---|
| `D09_c14_3800mm` | 0.958 | **1.000** | 0.451 |
| `D12_c14_585_afbin2` | 1.000 | **1.000** | 0.653 |
| `D15_cdk20_3454mm_e47` | 0.968 | **1.000** | 0.531 |

**Arm (a) clears the gate on all three and beats the control on two.** The gate failure was entirely an artifact.
What the term actually cost is **recall** — 0.983 → 0.932, 0.942 → 0.900, 0.965 → 0.917 — the same inversion F31
found: both mechanisms were suppressing *real detections*, not junk. F23 remains "won't fix as written" on F31's
grounds; this clause is now closed rather than unverified.

## The two decisions

**Backfill: convert in place, no re-run.** `TestApp bank-export-settings --runs <bank-root> [--apply]` reads each
folder's existing `optimized_settings.json` and writes the envelope beside it through the same DTO→options
mapping the optimize path uses. **42 landings (20 synthetic + 22 real), 0 failed**, every one round-trip verified
through `DiffKnobs` *before* being written. The re-run route would have tripped F15 on 39 folders at once.

**Wizard pickup: an explicit button, never automatic.** The wizard's baseline is the live profile — `baselineJ`
and the headline improvement percentage are both measured against it. Auto-pickup would silently redefine
"Current", so the same displayed percentage would mean something different with nothing on screen to say so.

## F40 — a feature can be shipped, tested, and never have run

A filesystem scan of `D:\` and the user profile before the backfill found **zero `hocusfocus_star_detection.json`
files anywhere**. Not a writer bug — wave 4's arms, including the `optA` acceptance run, executed *before* the
handoff was committed, and no `optimize` pass has run since. Wave 4's own last line says so correctly ("the
envelope appears on the next `optimize` pass"), and reads much weaker than its headline "**Shipped.** Every
landing is now stored in a form the app can import and replay with".

Unit tests prove the mapping; they do not prove a file arrives on disk. The distance between "the code that
writes it is correct" and "it has been written" is exactly one arm nobody ran. **The backfill is the format's
first end-to-end exercise outside unit tests.**

## F41 and F42 — the same hazard from two directions, and they cost the most this wave

Wave 5's acceptance criterion C0 was "the feature-OFF landing must be bit-identical to `hf_f23/H_real_A`". On
`toml999` it failed across **nine knobs** — Sensitivity 33.3 → 16.7, StarClip 3.5 → 6.875, MaxDistortion
0.10 → 0.45. That reads as a serious regression in the change under test.

**It is not one, and the same output says so.** `BaselineJ` differed too, **0.99784 → 0.98348**. `BaselineJ` is
the *current settings*' score — **no search produces it**, so a search-side change cannot move it. A changed
`BaselineJ` can only mean the objective or the detector changed, and between wave 1 and wave 5 they changed at
least five times. That single number is a free, decisive check, and it should be read **before** any knob diff.

Chasing the residue found the deeper one. `HarnessSettingsStore.DefaultPath()` is
`Path.Combine(AppContext.BaseDirectory, "harness_settings.json")` — **the file sits next to the exe** — and it is
**bootstrapped from the live NINA profile when absent**. The AF-bank run instructions require building each arm
to a separate `-o` directory, because the exe is file-locked while running. So **every new build directory
bootstraps a fresh settings file from whatever the profile held at that moment.** Measured across three wave-5
build directories:

| option | `exe2` | `exe_base` |
|---|---|---|
| `LocallyAdaptiveBinarization` | True | **False** |
| `ModelPSF` | True | **False** |
| `UseOptimizedSettings` | False | **True** |
| `PixelSizeMicrons` / `FocalLengthMm` | 3.8 / **NaN** | 3.76 / 688.0 |

`UseOptimizedSettings = True` alone changes what the BASELINE is. The store exists precisely to stop profile
state leaking into runs — its own comment says "two runs of the same data minutes apart were seeded from
different telescopes" — and the bootstrap path reintroduces it while the build workflow guarantees it fires.

**Pinning `--settings` to one file made the two binaries agree exactly** (the inertness table above). An arm set
run from one build directory stays internally valid; every cross-directory comparison was not.

## Lessons

**0. A constraint that makes the objective GO UP is not a constraint — it is a bug report about the search.**
The whole wave was designed around bounding a trade. On 5 of 7 binding runs there was no trade to bound: the
unconstrained search was simply stuck, and pruning the shedding branch let it find a better optimum with more
stars. The arm was built to measure a cost and measured a defect instead, which only became visible because `J`
was left untouched and therefore stayed comparable.

**1. `BaselineJ` is a free control, and it should be checked before any knob diff.** It is search-independent, so
two arms of the same binary must agree on it. When they do not, the binaries differ and no comparison between
them means anything. This one number separates "my change broke something" from "these are different detectors"
in one glance, and would have saved the whole F41 detour.

**2. The measurement apparatus deserves the same suspicion as the code.** The two most expensive findings this
wave (F41, F42) were both about whether a comparison was valid, not about the detector. A control that is not a
control produces a confident, specific, wrong answer — nine knobs' worth.

**3. Read the thing before building against it — twice more.** `DonutHeuristic` is a TestApp audit tool, so
F24's "runs the donut heuristic did not flag" was not implementable as written; and `golden eval` already had
every override F24's arms needed, turning a planned `optimize` campaign into 55 minutes.

**4. Two datasets are not a bank.** Wave 3's F24 conclusion held perfectly on the two datasets it was measured on
and reversed sign on three others. The generalization was the untested part, not the mechanism.

**5. A gated diagnostic is missing exactly where it decides something.** The keep fraction was first written only
when a floor was in force, which left the *control* arm — the one run whose keep fraction says whether a floor
would have bound at all — unable to report it. Report a measurement whenever it is measurable; gate the
*setting*, not the *observation*.

**6. Count the tests that discriminate.** 12 written, 8 fail on revert, and the four that do not are labelled as
guards rather than quietly counted.

## Verification

- New/changed tests all pass; the F32 constraint's 8 discriminating tests were confirmed by neutralizing the
  feasibility test at all three accept sites and restoring.
- Inertness vs the parent commit measured directly, settings pinned: **bit-identical**.
- Backfill: 42/42 landings round-trip verified before writing, 0 failed.
- Per [F37](followups.md), a red CI check is checked against the native test-host crash before being read as a
  regression, and nothing merges on red.

## Reproduce

```
# F26 — the one unverified clause (~40 s each, 6 arms)
TestApp golden eval --runs "D:\SyntheticAutofocusBank\<DS>" --params optimized \
    --opt-results "D:\hf_f23\{a_A|H_A}\<DS>_attempt01" --match centroid --match-radius 12 --out <dir>

# F24 — five arms over all 20 datasets, no code change (~55 min total)
D:\hf_w5\f24_arms.sh          # analysed by D:\hf_w5\analyze_f24.py

# F32 — screening arms, settings PINNED (F42) so every arm shares one detector
D:\hf_w5\f32_arms.sh          # analysed by D:\hf_w5\analyze_f32.py

# Backfill (dry-run first)
TestApp bank-export-settings --runs "D:\SyntheticAutofocusBank" [--apply]
```
