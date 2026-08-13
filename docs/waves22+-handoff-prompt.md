# Waves 22+ — continuation prompt

Run this after `/clear`. Waves 17–21 executed in an earlier run; **waves 22, 23, 24, 25 and 26 have since
executed**, and **wave 26 was the LAST wave of that run, which has ENDED.**
**Where a wave's own design document exists, THE DESIGN IS THE AUTHORITY OVER THIS FILE**, and where this file
states an outcome it names the results document that carries it.
**You are the controller, you delegate context-heavy work to agents, and you do not stop for approval.**

> **START HERE — the three-line state, current as of wave 26 (`2026-08-13T07:12Z`).**
>
> | | |
> |---|---|
> | **vessel** | `ghilios/synthetic-af-bank-followups-wave23`, **PR #195**, carrying waves 23, 24, 25 and 26. Re-decide the vessel in writing before every wave; check `gh pr view 195 --json state` before committing |
> | **the gate** | **FIFTEEN binaries**, bit-identical at sixteen digits every time. Wave 25's `G25` PASSED on BuildId `d79dae73582e4a6c95e5c81f1f143ffc`, **novel against fourteen**. **Wave 26 built NO binary and SKIPPED the gate deliberately** (design §5's four conditions), replacing it with three sharper controls — see §1c |
> | **the suite** | **3973**, verified by COUNT (`Failed: 0, Passed: 3973, Total: 3973`, `SUITE_EXIT=0`, wave 25). Everything below it is stale: 3922, 3933, 3958, 3960, **3966**. Wave 26 changed no C# |
> | **the deliverable** | **DONE.** The owner's final results table shipped at `7e98915` — all 20 datasets, precision / recall / optimization time / score / sigma / exposure vs optimal / binning vs optimal / `BrightnessSensitivity` (effective gate). `docs/synthetic-af-bank-results-table.md` |
> | **next** | **Read §1c's "STATE OF THE REGISTER" first.** The run is over; every remaining item needs a binary, and one item needs ~2 minutes and no binary |

> **WAVE 26 IN FIVE LINES.** **Nothing shipped and nothing was built** — B15 was reused and the gate deliberately
> skipped, replaced by `Q26-V0` (hash identity, **FAIL end demonstrated on B14**), `Q26-V5` (determinism) and
> **`Q26-V3`, which tested what no gate ever has: B15 reproduces wave 18's `FinalJ` on 8 of 8 landings produced
> fifteen binaries earlier, exactly.** **`RULE Q26` = `Q-PIN-COSTED`**: the flat-direction hypothesis is
> **REFUTED** (`A-RESPONSIVE`, 0 flat of 8) and forbidding the extreme costs `J` on **4 of 4** paired
> re-searches. **But the headline cost zero compute: `J` is composed WITHOUT a precision term when a run is
> unlabelled (`OptimizationObjective.cs:484-490`), and this bank runs unlabelled** — so a sensitivity pin buys
> stars for free, which is the mechanism behind `RULE P23`'s 22-of-24 driven pins. That is **[F83](followups.md)**.
> `RULE L26` (`lumos`) is **could-not-look**: `af-fit` emitted no CSV and the instrument refused rather than
> guessing.

> **WAVE 25 IN FIVE LINES.** **P4 SHIPPED** (`StepSizeRecommender` had a ceiling with no floor —
> [F81](followups.md)) under `G-PASS` + `N-PRESERVED`; **P5 shipped** unconditionally and **CLOSED
> [F34](followups.md)** (`M-CORRECTED`, FAIL end measured on wave 24's published arm: 25 cells, 1 contradiction,
> `D01` by name). **`RULE W25` = `W-UNEXERCISED`** — 0 of 13 blind S1 cells engaged the floor because all 13
> already converged, so the blind arm bought **do-no-harm evidence, not efficacy evidence**. Efficacy showed on
> **`G25-P3c`: 3 of 8 gate landings WIDENED, 0 NARROWED, one of them `mccomiskey`, a REAL-bank dataset**. The one
> control that missed its bar, `D01`, is diagnosed to a line and is now [F82](followups.md).

---

## 0. STANDING AUTHORISATION — unchanged

1. **Waves MAY ship user-visible product-behaviour changes** when the wave's own ship rule, fixed and committed
   BEFORE the data, is satisfied. If the rule is not met, the change does not ship and is recorded as a costed
   recommendation. **Ship unconditionally where the ship is not what the rule decides** — wave 20 shipped D1/D2/D3
   and lost a verdict, and the ship was right.
2. **~6 hours of compute per wave.** A wave that wants more must cut scope or split across two and say which.
3. **ONE branch, `ghilios/synthetic-af-bank-followups-wave23`, PR #195 (OPEN).** Never push `develop`. PR #191
   merged as `d2f3400`; wave 23 cut the current branch fresh from `develop @ 3d370ff`, and **wave 24 continued on
   it deliberately** because its binary had to carry `e7a5ee6` ([F79](followups.md)'s fix). **Every wave's FIRST
   decision, in writing, before any measurement, is whether PR #195 is still the right vessel** or whether it has
   merged and a fresh branch opens. Waves 23 and 24 each recorded the reasoning; do not drift into a third
   section by default.
4. **Proceed without asking.** A blocker is a result: record what was tried, what failed, what unblocking costs,
   and move on.

**Stop early** if the gate fails and cannot be explained, or if two consecutive waves produce no finding worth a
register entry. The next wave owes an explicit sentence on whether it has a finding worth an entry, and "no" is
an acceptable answer.

**The dry-wave counter is at ZERO and the register is NOT dry — but it is now THIN, and the reason is specific.**
The paragraph that used to stand here said the backlog was down to *"one ~2 m comment fix, a ~20 m test, a ~30 m
UI session, two ~8–53 m arms and a rejected list."* **Five waves have since produced [F79](followups.md),
[F80](followups.md) (three times), the F26 scope correction, the F34 `D01` correction and then F34's CLOSURE,
F21's finished costing, a shipped product fix ([F81](followups.md)), a newly diagnosed product defect
([F82](followups.md)) and — wave 26 — [F83](followups.md), a structural defect in the objective every conclusion
in this series is denominated in.** **What makes the backlog thin is not a shortage of questions; it is that
every remaining one needs a BINARY**, and the last wave deliberately built none. §1c's **STATE OF THE REGISTER**
is the honest inventory.

---

## 1. WHERE THINGS STAND after waves 17–25

| | state |
|---|---|
| **the gate** | Eight values, **reproduced on FIFTEEN binaries** (waves 11–25), bit-identical to sixteen digits every time. It has never moved. Wave 25's `G25` PASSED on BuildId `d79dae73582e4a6c95e5c81f1f143ffc`, **novel against fourteen**, with `G25-1/2/P1/P2a/P2e/P3a` all **8 of 8** and `P4-ASCII-CLEAN`. Wave 24's `G24` PASSED on `dd6ca32ef7f941c2a54753398cc2cf6b`. §5 |
| **[F81](followups.md)** — NEW in wave 25, **SHIPPED** | **`MaxHalfWidthSampledHalfSpanMultiple` was a ceiling with no floor**, and the `half-width-unresolved` exit returned before the ceiling was consulted at all. Both fixed in shipping plugin code (P4), one-sided **by construction** (a `Math.Max` against an existing ceiling), guarded by a `double.IsFinite` could-not-look check that is [F79](followups.md)'s mirror. `WasBandFloored` is the new observable and it is **not** an overload of `WasCapped` — the two are mutually exclusive by construction, which is what makes the do-no-harm clause provable from the code |
| **[F83](followups.md)** — NEW in wave 26, **OPEN — and it is the one to read first** | **`J` carries NO precision term on an unlabelled run.** `JRun` takes the `else` branch at `OptimizationObjective.cs:484-490` when recall and precision are absent, and **the whole synthetic bank runs unlabelled** (`Labels: (none — unlabeled)`). `SStars` counts stars, not correct stars; the one false-positive term that exists (`SMarginalSnr`) ships at strength **0**. **So driving `BrightnessSensitivity` to its floor buys stars for FREE — that is the mechanism behind `RULE P23`'s 22-of-24 driven pins**, and every *reading* of an optimize result on this bank ("the optimizer chose this because it is better") means better in a score with no false-positive cost. **Zero compute, source-derived.** The entry also carries `Q26-B`'s measured price for forbidding the pin, the 8-of-8 provably-inert reading, and `PREDICTION P-D08` confirmed at `n = 1` with three caveats larger than the effect |
| **[F82](followups.md)** — NEW in wave 25, **OPEN, and its FIX CHOICE IS NOW PRE-REGISTERED** | **The floor is not sticky across rounds.** `D01`'s half-width went **12.0 → 9.0** between rounds because `SearchSpan` is measured over the **fitted** points, and the round-1 sweep — requested **wider** (16 → 24 units) — **lost its outer frames**, so the cap recomputed a lower bound than the floor had established. Diagnosed to a line, two candidate fixes, **and it does NOT need a full paired arm to score** (see §1c) |
| **[F34](followups.md)** | **CLOSED (wave 25).** The stall message now reads `DegenerateReason` and branches; `RULE M25` = `M-CORRECTED` with the FAIL end measured on wave 24's real published arm (**25 cells, exactly 1 contradiction, `D01` by name**) and 0 on the AFTER arm |
| **[F79](followups.md)** — NEW in wave 23, **VERIFIED in wave 24** | A single non-ASCII byte (`0xE5`, a CP437 `σ`) made `grep` class a whole log binary and report **zero** matches for strings elsewhere in it — **failing closed**, latent since wave 11, in 8 of 8 gate logs of twelve wave roots. **Fixed in wave 23** (120 lines / 20 files, guarded by `TestAppOutputAsciiTests`); **`G24-P4` measured 0 non-ASCII bytes in 8 of 8** on the first gate built after it, with the FAIL end demonstrated first on wave 23's real gate. **AND the fix exposed a second defect it was masking** — the same driver line still prints `0 of 8` because it counts **lines** where a block is a `BEGIN`/`END` **pair**. Both defects emit the identical wrong number |
| **[F80](followups.md)** — NEW in wave 24, **PRE-FLIGHT SHIPPED in wave 25** | **Instruments derived by `sed` keep their predecessor's prose.** Eight drifts across five instruments in wave 24, one **load-bearing**, **every one passing its own `--self-test`**. Wave 25 turned the remedy into a **blocking** pre-flight (`verify_derivation_w25.py`, `RULE V25`) and it earned its keep immediately: `V-DRIFT` **21 findings** on the wave's own instruments, including a **`sed` ORDERING bug in the plan** that left 10 of 15 sibling references pointing at a nonexistent file, and a surviving `G24_START` that would have hung the controller's own waiter. **AND THE CHECKER HAD TWO GAPS OF ITS OWN, SAME ROOT CAUSE: `\b` CANNOT SEE `_`** — it missed the marker **suffix** form (`G24_START`, found only because another clause fired on the same line) and then the **prefix** form (`w24_gate_log_wsl`), passing `V-CLEAN` a file that could not self-test. Both closed and **demonstrated**. Its `PREV` is a **single wave** and drift is not: a **w23** label survived two generations |
| **[F26](followups.md)** | **Scope CORRECTED (wave 23 §13): the livelock is in `TestApp/SynthValidateRunner.cs`, the HARNESS, not in the shipping wizard. NINA never loads it.** Wave 24 shipped the guard — a **revisit** rule, not the entry's own counter, which never fires on the oscillating form. On `D08`/S1 it meets **F26's own pre-registered bar** (`roundsUsed` 4→**3**, step `21`→**62**, `converged` false→**true**). **But RULE B24 is `B-UNEXERCISED`: 0 of 7 blind S4 cells ever revisited a factor**, so the livelock is **conditional and rarer than the entry implies** |
| **[F25](followups.md) / [F34](followups.md)** | **The owed directional gate is now implementable and the obvious version of it is WRONG.** Wave 24's P1 (shipping plugin code) added `DegenerateReason` and **measures `SampledHfrRange` on the degenerate path**, where it had been `NaN` — the gate's decision variable did not exist on the branch it must decide. The census: **`D03`/S1 is degenerate at R² = 0.9835**, `D02`/S1 at −0.2741, **the wide end is EMPTY (0 of 7 S2 cells)**, and **`D01`/S1 is NOT degenerate at all** (`halfWidth` 6.0877, R² ≈ 1.0) yet stalls at 4.5× too narrow. **F34's grouping of D01/D02/D03 as three degenerate stalls is corrected.** §1c |
| **F67** | **CLOSED by intervention (wave 17).** The cause is **`DetectionBinning`** — **neither** of wave 16's two candidates. `C17-A` **63 of 63 = 1.0000** against a status quo of **0.000 of 63**. Nothing further is owed on the entry itself; the residual is a *different* question, §2 |
| **F73 / RULE R19** | **`R-DETERMINISTIC`** (wave 19): 20 of 20 datasets, **660 of 660** landing keys, `R19-A` minus `R19-B` = zero. **But it is a NULL ARM** — the two build trees were C#-identical (`git diff --name-only … '*.cs'` empty), so it measures the **noise** floor and **not** whether a code change can move a landing at fixed `J`. The code axis was never run |
| **F69** | **(b) and (c) SHIPPED (wave 20)**, measured on the eleventh binary: `optimize/detected` on 8 of 8 gate logs and 1 of 1 probe log, 55/55 fields; the no-op notice once on the probe, zero times on the gate. **(a) shipped in wave 21 — verify in that wave's results** |
| **F70** | Manual **fixed at BOTH sites** (wave 20, `2678ecd`). **(a) escalated to the owner as not decidable by measurement.** **(b′): the partition is now DERIVED and pinned by behaviour (wave 21)** — `ResetDefaultsImpl` assigns **53** public properties, **exactly 20 preset-owned, 33 live, 0 unaccounted**; 52 of the 53 are probed by test, `UseAdvanced` is the one mechanical could-not-look. **The 20 literals were deliberately NOT deleted** — `ResetDefaultsImpl:405-407` records a prior decision to keep them as documentation, and like (a) both states pass every test, so it is an **owner's call**, now a two-minute edit |
| **F74** | New (wave 20): a driver and a scorer rebuilt the same artifact path from a template and cost RULE D20 its verdict. **Fixed BY CONSTRUCTION in wave 21** — see the discipline in §4 |
| **RULE D20** | **`D-UNEVALUATED`, permanently.** Do not convert the diagnostic to a verdict — see §3b |
| **F72 / item C** | **A1, A2, A4, A6 CONFIRMED as rendered pixels (wave 21)**, A1/A2 checked against the report JSON on disk. NINA loaded cleanly a **second** independent time. A3/A9 upgraded from the placeholder to a real recommendation. **A5 and A7 are BLOCKED on [F76](followups.md); A8 was not exercised** (this run's step was `55 → 53`, not capped). The wizard's entry point is **Plugins → Hocus Focus → Star Detector → "Optimize Star Detection"**, NOT the Imaging dock |
| **F76** | **FIXED and CONFIRMED IN THE FIELD (2026-08-11/12).** The optimizer wizard now fits `min(content, work area)` in BOTH directions, re-fits per step, centres horizontally on a new width, and holds still during a run. Took **eight rounds and five distinct causes**; every one was named by a log line and none by reading source. **Instrumentation has been removed** — if it regresses, re-add `Logger.Info` tracing FIRST rather than reasoning. See [F76](followups.md) |
| **F21** | **PRICED at last (wave 21, item P): 45–52 s.** `exit=0` by name, `roundsUsed=2`, and a second round **did** occur. The working invocation is in §2. An unregistered rule-free follow-on at `--max-rounds 5` stopped at `roundsUsed=2` again with an **identical** trajectory, so the convergence is a **real stop, not `maxRounds` running out**. **Seed sensitivity is UNMEASURED** — the per-round seeds are *derived* from spec+scenario (identical in both runs: `[-25215000, 1539450862]`), so the repeat is ordinary determinism and says nothing about seeds |
| **RULE B21** | **`B-DEMONSTRATED`** (wave 21), **6 of 6** on V1/V2/V3/A/B/D over the six factor-2 datasets `D20` never touched (`D10, D17, D09, D08, D15, D14`). `optimize/detected` **is** the post-mutation bundle: binning 1 → 2, `PixelScale` `NaN` → finite and equal to the console at its own precision, difference set from `optimize/baseline` **exactly** `{DetectionBinning, PixelScale}` over 55 fields. `B-REFUTED` was reachable and did not occur. **This is how D20's question was answered WITHOUT harvesting D20** — a new population, not a re-score |
| **F75** | New (wave 21): the wave held **two standards for its two interlocks**. `B21_ARM_READY` is driver-written, last, only on `>= 4` rows, under an explicit *"never hand-write the marker"*; `G21_PASSED` — the one the arm blocks on — was a controller `printf` named only in the plan. **An interlock whose writer is a human is a note, not an interlock.** One-line repair named in the entry |
| **F45(b)** | **REJECTED ON TIME by wave 21** (~3–4 h against a 3 h 25 m wave). Remains behind the RULE S16 fence, §3a |
| **F59** | **REJECTED ON THE MERITS** (wave 20's measurement, wave 21 concurring). §2 carries the corrected costing — do not re-cost it at "~1 h" |
| **F15** | 42 of 42 bank landings byte-identical for **twelve consecutive waves**. Wave 25's other three controls also passed: **59 of 59** aux files, **48 of 48** prior-wave arm landings across three roots, **38 of 38** wave-23 `synth_validate_report.json` files — all byte-identical, **0 could-not-look**, output KEPT (`fp_*_AFTER.txt`). **Wave 25's gate REFUSED TO START TWICE (`exit 3`) because two of the four BEFORE fingerprints had not been written yet — both refusals were correct.** *A control written after the arm is not a control*, and an abort that names the missing control is the guard working |
| **the suite** | **3973**, verified by COUNT in wave 25 (`Failed: 0, Passed: 3973, Total: 3973`, `3 m 42 s`, `SUITE_EXIT=0`) — the 3966 baseline plus wave 25's 7 new tests, every one named, four shown red by named mutants and three labelled companions. **Everything below 3973 in older docs is stale (3922, 3933, 3958, 3960, 3966).** Verify by count, never the tick ([F37](followups.md)) |
| **the two named real-bank gaps** | **`lumos` MEASURED at last** (wave 24, after ten waves as folklore): `rc=3` **reproduces**, and it is a **hard-floor FAIL** — `bestJ = currentJ = 0`, no improvement, *"at least one frame has < 3 stars under optimized params (min observed = 0)"*. **`Panos` is `L-NOT-COMPARABLE`**: `optimize` returns **exit=0, hard-floor PASS, `bestJ = 0.935582`** — but the recorded *"degenerate σ fit"* is an **`af-fit`** property, so this probe cannot settle it. §2. **Wave 26's `RULE L26` tried to close the `lumos` half and returned COULD-NOT-LOOK** — `af-fit` produced no `af_fit_points.csv`, so the driver wrote 0 rows and refused rather than branching to `L-FRAME` off an absent measurement. The next attempt owes a diagnosis of the missing CSV first |

**Artifacts:** `D:\hf_w17\` … `D:\hf_w26\`. **Wave 26 adds a reusable set that needs no binary:**
`verify_derivation_w26.py` (the pre-flight, **shape-matched**, FAIL end **pinned** to `/mnt/d/hf_w24`),
`prep_w26.py` (resolves the at-floor population from each landing's own `Provenance.CommandLine` and writes
perturbed vectors through the production Accept path), `q26_arm_w26.sh` (six-phase driver) and
`score_q26_w26.py` (**`--out` MANDATORY** — it refuses without one, which is wave 25's lost-artifact defect fixed
structurally rather than remembered). Also reusable: `score_w12.py` (the gate), `prov_w<N>.py` (free controls,
self-testing), `convert_landing_w15.py` (landing → harness settings, via the production Accept path),
`bank_fingerprint_w15.py`, `aux_fingerprint_w17.py`, `arm_fingerprint_w19.py`, `log_fingerprint_w20.py`,
`score_r19_w19.py` (the 33-key landing differ), `score_d20_w20.py` (the `optimize/detected` reader),
`layout_w24.sh` (the shared path layout), `b24_arm_w24.sh` (the four-phase `synth-validate` arm driver, two
binaries, manifest handoff), `score_b24_w24.py` (B24/R24/D24, self-test **21 of 21**),
`score_g24_w24.py` (the gate + the interlock writer, self-test **24 of 24**),
`ascii_census_w24.py` ([F79](followups.md)'s byte census), `v23_fingerprint_w24.py` (fingerprint class 4) and
`gapprobe_w24.sh` (the `lumos`/`Panos` probe).

**Derive the next wave's copies with `sed` if you must — but run [F80](followups.md)'s `--verify-derivation`
pre-flight before the gate, and START FROM `/mnt/d/hf_w25/verify_derivation_w25.py`, not from wave 24's.**
Wave 24 shipped eight prose drifts and one broken module reference this way; wave 25's pre-flight caught 21 more
on its own instruments before any measurement, **and needed two fixes of its own** — the token pattern must
accept an optional `_suffix` AND an optional `_prefix`, because `\b` cannot see `_`. **Check the predecessor's
INTERFACE survived, not just its spelling**: wave 25's `layout_w25.sh` exposed a different API from wave 24's, so
no renaming scheme could have repointed three call sites that no longer existed under any name.

---

## 1b. WHAT CHANGED AFTER WAVE 21 (product work, not a wave)

Between wave 21 and now, the owner directed a run of UI fixes. These shipped on the same branch and are **not**
wave output — no pre-registration, no rules, just requested product changes with tests:

| commit | change |
|---|---|
| `e605c89` | the optimizer wizard defaults to **Live Auto-Focus** (was Saved). Flipping it broke 13 tests **and hung the fixture**, because dozens of tests call `StartAsync` without naming a source and Live waits on a camera. Fixed at the shared `NewVM` helper, which now pins `Replay`; three tests pass `sourceMode: null` to observe the product default |
| `4be89de` … `4d762af` | **F76**, eight rounds — see the register |
| `11e9633` | `ClampWindowToWorkArea` attached to the two standalone review windows, **clamp only**. Growing is the opt-in `FitContent`, OFF by default: those windows host an image viewport whose ScrollViewer extent is the IMAGE |
| `bdb61c8` | `Suspended` attached property (wizard binds `ShowProgress`) so the window does not resize on every status line during a run, and fits **once** on release |

**F70(b′) was REJECTED by the owner** and is closed — do not re-propose deleting the 20 preset-owned literals.
The Simple-mode preset system owns the whole detector configuration; the "20 owned / 33 live" split is a
snapshot of what currently *varies*, not a design boundary. The tests that pin it were renamed
`DerivationAssignedProperties_*` for exactly that reason.

---

## 1c. THE STATE OF THE REGISTER, at the end of the run

**Wave 25 executed the stall bridge and it shipped.** `P4` = [F81](followups.md) (product), `P5` = the harness
message that **closed** [F34](followups.md). Verdicts: `RULE V25` **`V-CLEAN`** (after two blocking runs that
were both right), `RULE G25` **`G-PASS`**, `RULE W25` **`W-UNEXERCISED`**, `RULE N25` **`N-PRESERVED`**,
`RULE M25` **`M-CORRECTED`**. Full account: `docs/synthetic-af-bank-followups-wave25-results.md`.

**Wave 26 priced the sensitivity pin and shipped nothing.** `RULE V26` **`V-CLEAN`** (after a `V-DRIFT` that found
a **third** derivation-drift shape); the gate **deliberately skipped** and replaced by `Q26-V0`/`V3`/`V5`;
`RULE Q26` **`Q-PIN-COSTED`**; `RULE L26` **could-not-look**. Full account:
`docs/synthetic-af-bank-followups-wave26-results.md`.

> ### THREE THINGS THE CHARTER SAID THAT ARE NO LONGER TRUE — CORRECTED
>
> 1. **`N25-E` is DISCHARGED, not open.** It was listed here as priority 1 and *"the cheapest open question in
>    the series"*. **It ran** — artifact `/mnt/d/hf_w25/n25e_score.txt`, published in wave 25 results §16.1:
>    *"P2 moved 1 of 18 paired S1 cells. Wave 23's `13 of 17` is formally RETIRED as uncomparable."* Struck.
> 2. **`P23`'s flat-direction arm is DISCHARGED**, by a strictly stronger instrument than the one it registered
>    (F6's 2×2 **plus** a paired re-search under `--sensitivity-floor`). See [F83](followups.md). Struck.
> 3. **The owner's final results table is DELIVERED** (`7e98915`, all 20 datasets). It is no longer "next".

**What is genuinely open, priced, cheapest first:**

| # | item | price | goal | needs a binary? | why |
|---|---|---|---|---|---|
| **1** | **`PREDICTION P-D08` at `n = 3`** — score `seedA1/D01_ultrawide_40mm` and `seedA1/D16_esprit550_ha3` at `--sensitivity 10.0` | **~2 m, 2 `golden eval` cells** | **3** | **NO** | **The highest decision-value-per-minute item left.** Both are at-floor landings with sub-1.5 combined gates (0.2234, 0.1969) and `StarClippingMultiplier` **pinned at its own 0.25 floor**, and neither has ever been scored for precision at a raised sensitivity. If `D08`'s +0.034 replicates, [F83](followups.md)'s lead becomes a finding; if not, option (b) loses its only measured support. **Score against `*.truth.json` as well as the golden**, or [F31](followups.md)'s caveat applies to the replication too |
| **2** | **the [F83](followups.md) DECISION** — (a) raise `DefaultSensitivityLower`, (b) a precision term on unlabelled runs, or (c) label the bank | **a decision, then ~42 m of baseline** | **3** | for (a)/(b) | The measurement is done and the three options are laid out with their evidence and prices. **What is missing is an owner's call**, not an arm. (a) is the option the evidence is most hostile to — F23 already measured a hard floor as worse than doing nothing |
| **3** | **`lumos`'s zero-star frame** (`RULE L26`) | ~10 m **+ a diagnosis** | 1 | yes | Wave 26 returned **could-not-look**: `af-fit` produced no `af_fit_points.csv`. **The next attempt owes a diagnosis of the missing CSV before it re-runs the same command** |
| **4** | **the `A4` truth-model gap** | ~20 m | 2 | yes (rebuilds TestApp) | The harness computes the cap boundary from the **requested** sweep, the product from the **fitted** span; they disagree on `D01` r1 by 2×, and `A4` compares against `WasCapped` alone so it now flags floored rounds. **Fix the assertion, not the product**, and keep it out of any arm carrying a product change |
| **5** | **[F82](followups.md)** — the floor-not-sticky fix | ~45 m code + tests **+ ~10 m 3-cell re-run + ~42 m MANDATORY gate** | 2 | yes | **The fix choice is now PRE-REGISTERED: (1), the monotone floor** (wave 26 design §14), with the condition that would reverse it — if a later wave measures `SearchSpan` over `bestFit.Inputs` shrinking on **more than a single cell** while the requested sweep widens, (2) becomes correct. `n = 1` is not enough to move a bound four published rounds depend on |
| **6** | **F67's residual** | ~30 m + a rule | — | yes | Pairs with nothing now. Needs the [F62](followups.md) no-`BestJ`-across-factors landmine pre-registered around |
| **7** | **[F73](followups.md)'s code axis** | ~53 m + a second binary | — | yes | Wave 19's arm was a **NULL** arm (C#-identical trees). The real question — can an inert-believed code change move a landing at fixed `J`? — has never been run |

**Blocked or fenced. Do not re-attempt:** `RULE F14` (§3), `RULE S16` (§3a) and `RULE D20` (§3b) are **permanent
fences**. [F70](followups.md)(b′) was **rejected by the owner**. [F45](followups.md)(b) is behind the S16 fence
with `N*` unreachable in `AutoFocusEngine`. [F59](followups.md) is **rejected on the merits**. The wide end of
[F25](followups.md) is **unmeasured, not untriggered**, and remains so.

**Is another wave worth it?** For the ~6-hour shape this charter assumes: **no** — what is left is one decision,
one fix with a mandatory gate, and two probes, and **item 1 is the only one that does not need a binary.** For a
**~30-minute** session: **yes, exactly one** — item 1, which settles whether the run's sharpest lead is real.

> ### DO NOT RE-RUN A FULL PAIRED 20-CELL S1 ARM TO CHASE `D01`
>
> **`W-UNEXERCISED` is the measurement that makes it unnecessary.** Wave 25's 13 paired blind S1 cells moved by
> **exactly zero** across a real product change — converged 13/13 both arms, in-band 13/13 both arms, distance
> `SAME 13` by exact `repr()`. A second full arm buys a second `SAME 13` for ~2 h 40 m. The only cells that can
> move are the three published controls plus whatever the 8-run `optimize` gate moves.

> ### WHAT WAVES 25 AND 26 MEASURED THAT CONSTRAINS THE NEXT ONE
>
> - **The pathology is RARE on this bank at S1 — and only there.** 0 of 13 blind cells; **3 of 8** `optimize`
>   gate landings, including `mccomiskey`, a **real-bank** dataset. A rate from one population does not transfer
>   to the other.
> - **The wide end of [F25](followups.md) is UNMEASURED, not untriggered.** No clause in wave 25 or 26 touches
>   it, and `D05`/S2 remains published and unusable blind.
> - **`13 of 17` is FORMALLY RETIRED, with evidence** — `N25-E` ran and P2 moved **1 of 18** paired S1 cells
>   (wave 25 results §16.1). It is uncomparable, and **no goal-2 accuracy rate may be quoted from it.** Nothing
>   has replaced it.
> - **`terminal.stepBehavioral` is not arm-invariant on `D01`** (8.0 → 9.0 across arms, identical on the other
>   17 cells). Any rule scoring against it must assert cross-arm equality per cell first and name failures.
> - **`D05_tec140_1000mm` and `D19_cygnus_deep_shed` time out at `timeout 600` on EVERY S1 arm** — four in a row
>   now. Structural, not flaky. Raising the timeout changes the instrument and breaks comparability.
> - **An identifier boundary is not a word boundary.** `\b` cannot see `_`, and every marker, function and path
>   in this series is underscore-joined. [F80](followups.md) now carries **three** demonstrated gaps — prefix,
>   suffix, and the **rule-letter enumeration** that missed `W25_BEFORE_READY`, wave 25's own interlock marker.
>   **An alternation enumerating the values a field has TAKEN is a hardcoded list wearing a regex.**
> - **`J` HAS NO PRECISION TERM ON THIS BANK** ([F83](followups.md)). Any clause that reads a `BestJ`, a
>   `BaselineJ` or a `dJ` is reading a score with **no false-positive cost in it**, because the runs are
>   unlabelled and `JRun` takes the `else` branch. This is not a caveat to add later — it changes what a `J`
>   comparison is evidence *of*, and it applies to every wave from 5 onward.
> - **A `J` from wave 18 and a `J` from wave 26 are the SAME QUANTITY.** `Q26-V3`: B15 reproduces wave 18's
>   `FinalJ` on **8 of 8** landings produced on B1/B2, exactly, at all sixteen digits. **This is the first
>   cross-binary objective comparison in the series** and it is what licenses quoting an old `J` as a reference
>   scale. No gate clause has ever tested it.
> - **A gate can be honestly SKIPPED, and wave 26 records the four conditions** (nothing built; the predecessor
>   gate already answered what a re-gate asks; a re-gate does not reach the wave's actual risk; no clause in the
>   verdict tree reads a gate landing) **and the three that reverse it** (any C# changes; a hash/BuildId
>   mismatch; the arm's own instrument is non-deterministic). **It is only honest with a replacement control that
>   is shown to REFUSE** — `Q26-V0`'s FAIL end was measured on B14, a real different published binary.
> - **A checker's known-bad input expires the moment it works.** Wave 25's `V25` used *"the previous wave's
>   root"*, then repaired that root. Wave 26 had to **pin** the known-bad root in source. *A regression fixture
>   that is "whatever came before" has a shelf life of one wave.*

---

## 2. THE BACKLOG, priced — cheapest first

**§1c supersedes this table.** What follows is the older, still-valid priced backlog; read it for
items §1c does not name, and for the costings that have been corrected in place.

Pick by **decision value per hour**. Prefer questions that can be **refuted**. Prefer instruments **already
printed**. **State per item whether the gate REACHES its code** — the gate is a control on the binary's search,
so documentation, scorers and tests are in **no** binary and a wave that lists the gate beside them is claiming
a control it does not have. Name the check that *does* reach them instead.

| candidate | why now, and does the gate REACH it? | cost |
|---|---|---|
| **F69(a)** — only if wave 21 somehow did not land it | **NO** (an XML doc comment). The reaching check is wave 21's new NUnit test | **~2 m** |
| **F70(b′)** — deleting the 20 literals | **NO — and the instrument is already built.** The membership is pinned by `StarDetectionOptionsTests.DerivationOwnedProperties_* / NotDerivationOwnedProperties_* / PresetOwnedPartition_*`, killed by mutants **M-P1** and **M-P2** (each fails on exactly the property that moved). Deletion is guarded by `ResetDefaults_EqualsFreshConstruction_*` (four entry states, every property; red against **M-R1**). **What is missing is not safety, it is a decision** | **~5 m**, once the owner decides |
| the **joint-path** behaviour of the `optimize/detected` dump | **NO** — every arm in this series is `--per-run`. Settled in source: `ApplyRunDetectionBinningIfRequested` has exactly one call site (`OptimizationDiagnosticRunner.cs:675`, pinned by a test) | **~5 m**, and **nothing in the register depends on it** |
| the **out-of-sample pass over wave 18's 40 arm landings** | **NO** — it is `af-fit` over landings already paid for. The population is **provably unspent** (48 of 48 byte-identical, three waves running) | **~8 m** |
| **F67's residual thread** (not F67, which is closed) | partly — needs `af-fit` at **both** binning factors. The seven factor-2 datasets' published landings were produced at factor 2 while thirteen waves of `af-fit` evidence about them was produced at factor 1. **No `BestJ` may be quoted across the two factors** ([F62](followups.md)) | **~30 m** of `af-fit` + a rule |
| **[F76](followups.md)** | **DONE — remove from the backlog.** Fixed and field-confirmed | — |
| **F72 / item C** — A5, A7, A8 as rendered pixels | **NO.** A5/A7 are **UNBLOCKED** now that F76 is fixed: `Continue optimizing`, `Accept` and `Close` all render. A8 still needs a saved run whose recommended step is actually **capped**; reaching the summary is not enough | **~20 m** |
| **F73's honest missing arm — the CODE axis** | it *is* the arm: same 20 datasets, a binary differing by a change **believed inert on the search**, all **33** keys diffed. Wave 19's `git diff --name-only <old> <new> -- '*.cs'` line is now mandatory provenance for any two-binary rule | **~53 m** |
| **F59** | **NO, and the cost is not "~1 h".** `DerivePresetSettings()` assigns four of the five (`MaxDistortion`, `StarCenterTolerance`, `HotpixelThreshold`, `Sensitivity`), so under the pinned file's `UseAdvanced=False` they are **overwritten on load** and repairing the exporter changes the detector by exactly nothing for them. The fifth, `SaturationThreshold`, is **not** preset-owned and **would** bind — making this a **coordinate-system move owing a fresh 42 m baseline**, and that is the whole price | **42 m baseline** + re-derivation |
| **F45(b)** production plumbing | **NO.** Fenced, §3a. `N*` is not reachable in `AutoFocusEngine` (`:901` drops the count into `MeasureAndError`, a NuGet struct of two doubles) | **~3–4 h** + tests |
| **F21** | **NO. Costing FINISHED in wave 24 — and the 45–52 s figure below must NOT be used to price an arm.** It was measured on `D11`, a factor-2 38 MB dataset at `--max-rounds 2`, and pricing a factor-1 arm from it under-reserved wave 23 by **3.5×**. Use the **per-scenario** rates: **S0 136 s/cell, S1 234 s/cell** (means over 19–20 cells, from `run.log` mtime deltas). **For a scenario never run, the price is a DERIVATION — give a band; wave 24's derived S4/S2 prices came in at ~0.4×** | **S0 136 s · S1 234 s** per cell |

**F21 — the working invocation, measured. Copy it; do not re-derive it:**

```bash
timeout 1200 <exe>/TestApp.exe synth-validate \
  --spec 'D:\hf_w21\exe\SynthBank\synthetic-bank-spec.json' --out '<out>' \
  --datasets D11_rc10_585_afbin2 --scenarios S1 --max-rounds 2 \
  --settings 'D:\hf_w11\pinned_settings_w11.json' --profile-id 'ce3f3e63-8fd3-4b72-a0ca-d90db9441382'
```

**Read the report with the RIGHT field names** — wave 21's probe printed four `None`s because it guessed them,
and every one of them existed under another name (F74's family, in the reader):

| the obvious guess | what the report actually calls it |
|---|---|
| `dataset`, `scenario` | **`datasetId`**, **`scenarioId`** |
| per-round `HalfWidth`, `step` | **nested**: `rounds[N].stepRecommendation.{halfWidth, stepSize}` — a flat key scan finds nothing |
| assertion `name`, `passed` | **`id`**, **`verdict`**, `detail` |

Round records also carry `bootstrap`, `fit`, `exposureRecommendation`, `binningRecommendation` and `applied`;
the scenario-level `terminal` block carries `converged`, `roundsUsed`, `stoppedReason`, `stepToleranceBand`,
`stepTheory`, `stepBehavioral`, `finalStepSize`, `expectedStepSize` and `overallVerdict`. On `D11`/`S1` the
trajectory is `14 → 24 → 41` with `wasCapped=True` both rounds at a pinned `1.714…` growth ratio, stopping on
`converged (step 41 within the 22 tolerance band of step_behavioral 55)`.

**Everything below was established at zero compute BEFORE the probe ran, and still holds:** the spec
**ships with the build** at `<exe>\SynthBank\synthetic-bank-spec.json`, sha256
`bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6`. **Caveat, not a blocker:** the bank's own
`synthetic_meta.json` records `generator.specSha256 = 621455a1…`, so **the shipped spec is a LATER revision than
the one that rendered the bank.** Scenario ids are **S0–S6** (`SynthValidationScenarios.cs`); **S1** (step ×0.25,
"multi-round convergence") is the one that **guarantees a second round** — S0 converges in one. `synth-validate`
accepts `--settings` (via `HarnessSettingsStore.Resolve(args, …)`) and `--profile-id`, so it **can be pinned like
every other arm**. **No clause may be written over it until a rate exists** ([F68](followups.md)).

---

## 3a. RULE S16 RETURNED NO RECOMMENDATION, AND THE REASON IS NOT THE DATA

Wave 16 measured the SEM criterion and **it works**: `S16-A(a)` is exact — **0 of 2** rejections on the one run
with ρ > 2 survive at `V1.00` — and `S16-D` at `V0.07` reached **CONTAINS-SELECTIVELY**, the branch expected to
be foreclosed. **It returned NO RECOMMENDATION because `S16-A(b)` could not pass:** the clause demanded **≥ 30
of 33** benign rejections surviving, the production consensus contains only **12**, and all **12 of 12**
survived. The bar was an absolute count against a denominator borrowed from a different instrument.

**Do not simply restate the bar as a rate and re-score wave 16's rungs.** A wave that wants a recommendation out
of the SEM criterion must (1) fix the corrected clause **on the consensus population, as a rate, naming its
aggregation** ([F68](followups.md)), and (2) apply it to a population measured **after** the rule is fixed.
**And correct the record while you are there:** wave 14's published *"32 of 33 rejections at s ≥ 1"* counted
phantoms; on the consensus it is **12 of 12 benign / 0 of 2 damaging**.

---

## 3. RULE F14 IS CLOSED. DO NOT HARVEST IT.

Wave 14's RULE F14 returned **NO VERDICT** because its V4 gate tested a stronger claim than the design proved.
Wave 15 built **V4′**, which tests the proved property, and **it passes** — 585 of 585 triples, 0 violations, and
the corrected diagnostic visibly points at family A, `f = 0.50`.

**It is still NO VERDICT, permanently.** Three reasons: (1) the rule's own guard — *"never resolved by preferring
whichever rung agrees with something else"* — cannot bind a reader who has already seen the published
diagnostics; (2) **V4 was not the rule's only defect** — the branch built to consume the wave's best measurement
is unreachable, and a rule that cannot consume its own strongest evidence should not issue verdicts; (3) a wave
that repairs a failed gate and immediately harvests a verdict teaches the next wave to do the same.
**Instead:** write a NEW rule with a branch table that CAN consume the SEM result, and apply V4′
**prospectively** to a population measured after the rule is fixed.

---

## 3b. RULE D20 IS `D-UNEVALUATED`, PERMANENTLY. DO NOT HARVEST IT EITHER.

Wave 20's RULE D20 returned **`D-UNEVALUATED`** naming `D20-V1`: the probe ran, `TestApp.exe` exited 0 in 16 s
and the log contains the whole result, but **the driver wrote `<probe>/probe.log` and the scorer read
`<probe>/<DS>/run.log`**, so the driver aborted its own post-check and never wrote `D20_PROBE_READY`. The
controller copied the log byte-identically and the scorer **still refused, correctly** ([F74](followups.md)).

**It is `D-UNEVALUATED` permanently, and the next wave must not convert it.** Three reasons, in this order:

1. **The measurement is already published as a labelled diagnostic** in wave 20's results §3.2, so **no re-run of
   the same probe on the same dataset can be blind.** This is the F14 fence's reason (1) verbatim.
2. **The rule has a SECOND, substantive defect a path fix does not touch.** `D20-B`'s `5e-7` tolerance is
   **finer than its own instrument's resolution**: the console value is `G6` — six **significant** digits, not
   six decimals — at `OptimizationDiagnosticRunner.cs:1938`. Wave 21 measured at pre-registration time that
   **`D20-B` FAILS on 4 of the 8 wave-20 gate logs** (`CWhiteFocus`, `D18_m24_deep_shed`,
   `D20_m24_bright_control`, `muggsie` — **exactly the four whose `PixelScale >= 1`**) **on a program that is
   CORRECT**. Repairing that threshold now would be repairing a rule **with the measured value in hand**, which
   is exactly what the F14 fence forbids.
3. A wave that repairs a failed gate and immediately harvests its verdict teaches the next wave to do the same.

**What wave 21 did instead** (pre-registered; read wave 21's results for the outcome): it wrote a NEW rule,
**RULE B21**, with the corrected tolerance, and applied it **PROSPECTIVELY** to a population D20 never measured —
the **six factor-2 synthetic datasets that are not D12**: `D08_c11_2800mm`, `D09_c14_3800mm`,
`D10_rc16_3250mm_sparse`, `D14_cdk14_2563mm_e47`, `D15_cdk20_3454mm_e47`, `D17_cdk14_oiii5`.
**Whatever B21 returns, D20 stays unevaluated and the diagnostic is never promoted to a verdict by hand.**

---

## 4. THE STANDING DISCIPLINE

The inherited ones still hold: **score a change on something the change cannot move**; **a control that cannot
fail is not a control**; **ask where a knob REACHES, not only whether it matters**; **intervention beats
correlation**; **fix the rule before the data, on the bar the predecessors faced, and check it is satisfiable**;
**the cheapest instrument is the one already printed**; **price the payoff in both directions and record
estimate AND actual**; **say what you did not run, and price it**; **pin `--settings` AND `--profile-id` on every
arm**; **a gate never shown to PASS is not a gate, and the demonstration must assert the mutation happened**;
**check that BOTH branches of a clause are reachable**; **refuting an argument is not refuting its conclusion**;
**a control demonstrated on one dataset is a control demonstrated on one dataset**.

**Added by waves 18–24, each paid for:**

- **[F68](followups.md) is FIVE parts now**, not four: **population** (artifact **and field**), **statistic**,
  **aggregation**, **the empty-set answer**, and **which copy of the field is read and whether it is the live
  one**. Wave 18 lost a verdict to the fifth part — the conversion control read the dead `Options` copy of
  `NoiseReductionRadius`. **Prefer rates with named denominators over absolute counts.**
- **A satisfiability analysis must check the PRECISION OF THE INSTRUMENT a clause reads, not only the values the
  clause can reach** (`D20-B`, §3b) — **and it must check that the clause's population is ADDRESSABLE**
  ([F74](followups.md)). Wave 20 verified the easier half of both.
- **A clause about a wave's own deliverable must have its FAIL end measured on real artifacts.** `G20-P2` was
  **0 of 8 on wave 19's gate logs and 8 of 8 on wave 20's, same scorer, same minute.** Wave 19 reported the same
  count with no threshold on it and its ten thresholded clauses passed with the code absent. *A number that is
  reported but not thresholded is a number the wave has agreed in advance not to act on.*
- **A driver and a scorer must not independently reconstruct the same artifact path; the handoff artifact carries
  the path, and the reader parses it.** Wave 21 makes this structural: the arm driver writes
  `/mnt/d/hf_w21/b21_manifest.tsv` (dataset, log path, landing path, resolved factor per row) and the scorer
  reads paths **only** out of it; both drivers source **one shared layout function**
  (`/mnt/d/hf_w21/layout_w21.sh`) — F74(b). *A fallback rooted at the same wrong parent is not a second chance.*
- **An interlock whose writer is a HUMAN is not an interlock — it is a note** ([F75](followups.md)). Wave 21
  held two standards at once: `B21_ARM_READY` was written by the driver, last, only on `>= 4` rows, under an
  explicit *"never hand-write the marker"* — while `G21_PASSED`, **the marker the arm actually blocks on**, was
  a controller `printf` named only in the plan. Nothing mechanically coupled it to the check it attested, so it
  could have been written before the FAIL-end demonstration and nothing would have caught it. **The writer must
  be the code that computes the verdict**: a scorer that already knows `rc` and the BuildId should emit the
  marker on `rc == 0` and only then — which also makes the FAIL end *unable* to leave one behind, a
  two-directional demonstration of the interlock for free. **And when a marker seems to have no writer, grep the
  PLAN too, not just `*.sh` and `*.py`** — wave 21's controller called it "a reader and no writer" and was
  wrong; the writer was a human step at plan line 173.
- **A self-test that runs AFTER the thing it guards is not a guard.** Wave 21 launched the B21 arm and ran
  `b21_arm_w21.sh --self-test` while it was four datasets deep. It passed and it was read-only, so nothing was
  harmed — but had it failed, the arm was already running. Recorded as a deviation, not laundered into
  "the self-test passed."
- **A report and an exit status are two channels. Wire BOTH to the verdict, and self-test the message against the
  mechanism it describes.** Wave 20 shipped two counter-examples: `prov_w20.py` printed *"novel against NINE
  recorded ids"* while checking ten, and `score_d20_w20.py --gate` **exited 0 on a `G20-P2` FAIL**. Wave 21's
  `prov_w21.py` computes the word from `len(PRIOR_BUILD_IDS)`; its scorer reads `$?` **directly, not via a pipe**.
- **When a register entry names where a false statement lives, grep for the IDENTIFIER the false claim is about,
  read every hit, then leave a test that does it for you.** This **corrects wave 20's own Lessons #4** (*"grep
  for the sentence, not the address"*) — **F69(a)'s two copies were PARAPHRASES, not the same string, so a grep
  for the sentence would also have missed it.** Wave 21's mechanical form: every comment in the TestApp sources
  mentioning `--apply-run-detection-binning` must also mention `--no-run-detection-binning`, with a
  could-not-look guard that FAILS if the identifier disappears.
- **A treatment that fails to arrive turns a confounded experiment into a clean control — and you only get the
  benefit if you check what actually varied.** `git diff --name-only <old> <new> -- '*.cs'` is mandatory
  provenance for **any** wave whose rule compares two binaries. **A differing `BuildId` proves a rebuild
  happened; it has never proved the code differs** — and **an `exe` hash is not a binary identity, hash the
  dlls** (`TestApp.exe` is byte-identical across wave 18's B1/B2 *and* wave 20's eleventh binary).
- **An item under two minutes is never dropped.** Wave 19 finished at 1 h 35 m of a 6 h ceiling with a
  one-minute item undone, and it cost RULE C19 its verdict at commit time.
- **A WRONG NUMBER CAN HAVE MORE THAN ONE CAUSE, and fixing one cause does not validate the instrument**
  ([F79](followups.md), wave 24). Wave 23's `0 of 8` was the σ/`grep` defect; wave 24's **identical** `0 of 8`,
  on a binary carrying the σ fix, is a **line-vs-block counting bug the σ had been masking**. **Both emit the
  same string, so the output alone could never separate them — only the scorer-vs-driver comparison did.** Keep
  a scored clause and its convenience print computing the same statistic by different routes, compare them, and
  **never assume they compute the same statistic**: here one counted blocks and the other counted lines.
- **A control that cannot fail is not a control — and wave 24 shipped one INSIDE the guard of its own arm**
  (results §9.1). The self-test branch checking a clock comparison was `[ now > "23:59" ]`, a tautology, and it
  returned rc=0 immediately before the driver aborted in one second and skipped all 22 cells. **Demonstrate every
  guard in BOTH directions by byte-level mutation**, and remember that a `*_START` line without a live
  `TestApp.exe` is not a running arm.
- **Instruments derived by `sed` keep their predecessor's prose** ([F80](followups.md), wave 24). Eight drifts,
  five instruments, one wave, one load-bearing — **and all of them passed their own `--self-test`**, because a
  self-test checks behaviour and the drift is in the labels. Run the `--verify-derivation` pre-flight **before
  the gate**. **Any ordinal or count in a derived instrument's output must be COMPUTED, never typed** — and note
  that wave 20's fix for one instance of this did **not** fix the class.
- **Price from the same instrument AND the same scenario** ([F21](followups.md), wave 24). Every wave-24 step
  priced from a **measured** rate landed within 5 % (gate 0.98×, S0 arm 0.95×); every step priced by **deriving**
  from a never-run scenario came in at ~0.4×. **Give a derivation a BAND and say it is one.** A conservative
  derived price is the cheaper error — wave 24's over-reserve is what bought both of its dropped items.
- **A rule may have a PREDICTED answer, and that is a virtue, not a weakness** (RULE R24). Wave 24 predicted
  before the data that P2 was inert on S0, argued the mechanism, and then measured 20/20 on 17 terminal fields.
  **A rule with a predicted answer is a rule that can be wrong**, which is the only kind worth running.
- **Fence your OWN known noise source before the data, and make it reported-not-thresholded.** `R24-C` named the
  commit, the file and the line numbers of a prose change guaranteed to differ between the two binaries, and
  scored it as reported. Its one difference landed exactly there. **That is not loosening — the numbers and the
  verdicts stayed at 100 %.**

### Traps

- **Pass scorers WSL paths** (`/mnt/d/...`). A Windows path yields UNEVALUATED and looks like a failed arm.
- **F74's path trap:** never let a scorer rebuild an artifact path from a template. **Three layouts existed
  across two waves** and wave 20's scorer was written against the one appearing in neither of its own drivers.
- **A mutation harness that restores from VCS destroys uncommitted work under test.** Wave 20's first mutant
  reverted with `git checkout --` while the tree carried D1/D2 and `HEAD` did not, silently deleting the feature
  under measurement. **Keep a byte backup taken before each mutation, restore it in a `finally`, assert source
  sentinels after every mutant**, and abort on `WORKING TREE DAMAGED`.
- **The write-up's FIRST action is `ls -la --time-style=full-iso` on the artifact root, with the timestamp
  PRINTED at the top of the document.** Wave 19's doc was falsified by an artifact written **33 seconds before
  its own commit**. Any row still open at that instant is marked open in place, not reported as done.
- **Silent truncation exits 0**: one bank path contains a SPACE, and `TestApp.exe` eats a `while read` loop's
  stdin unless you redirect `< /dev/null`. **Assert the POPULATION SIZE.** And **"could not look" needs its own
  state, with the guard BEFORE any field read.**
- **`strings` is TWO instruments.** Type/member names live in `#Strings` as UTF-8; string literals in `#US` as
  UTF-16 — use `strings -el`. **`grep AtrousWaveletFast` returns 1 on every binary this project has produced.**
  Read the `DetectorVersion` **field**.
- **A landing is NOT a harness settings file.** It is flat; the harness reads `parsed["Options"]`. Use
  `convert_landing_w15.py` — and the converted file carries every knob **twice** ([F71](followups.md)).
  **`UseAdvanced=False`** on the pinned file means Simple-mode presets recompute the advanced knobs, so editing
  them there has no effect.
- **The fire rate is not one number** — `MaxOutlierRejections` fires at different rates through `optimize`,
  `af-fit` and `bank-verify`. Never quote a rate without its settings.
- **Never run NINA during a pinned arm.** Only one `TestApp.exe` at a time. No fan-out (1.33×, F60; a pinned arm
  cannot fan out at all). **Never rebuild an arm's directory mid-wave** ([F53](followups.md)(c)) — plan the
  binaries up front.
- Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"`. **Logs are ASCII again as of wave 24's
  binary** ([F79](followups.md) fixed, `G24-P4` verified 0 of 8) — but **every wave root at or before 23 still
  carries the `0xE5`**, so any clause reading an OLD log must still use `-a`, `awk` first, or Python on bytes. And
  a Unicode character that CP437 *cannot* represent still arrives as `0x1A`.
- `D17_cdk14_oiii5` finds zero stars at short exposures; `astrodet` **the DATASET** is frameless (F14).
- **`lumos` exits rc=3 — MEASURED, wave 24, and no longer folklore.** It is a **hard-floor FAIL**, not a crash:
  `bestJ = currentJ = 0` (no improvement, 88/250 evals) and *"at least one frame has < 3 stars under optimized
  params (min observed = 0)"*. Open: is the zero-star frame a frame property or a parameter-space one — ~10 m of
  `af-fit`/`review`.
- **`Panos` — read this before quoting it.** The recorded *"degenerate σ fit, UNEVALUATED **by name**"* is about
  the **σ / focus-curve fit in `af-fit`**. **Wave 24's `optimize --per-run` probe returned exit=0, hard-floor
  PASS, `bestJ = 0.935582`, 360 accepted stars on one frame** — which is `L-NOT-COMPARABLE`, **NOT** a refutation,
  because `optimize` does not compute σ_focus at all. `Panos` **is** addressable by `optimize`. The σ claim costs
  ~5 m of `af-fit` to settle and is the only half still folklore.
- **Price an arm from a representative population AND the same instrument** (§5). On CI, verify the **COUNT**,
  not the tick (F37).

---

## 5. THE GATE, unchanged

```
toml999 0.995784 | CWhiteFocus 0.996068 | uneven 0.996368 | muggsie 0.997195
mccomiskey 0.976746 | D18 0.999882 | D19 0.999487 | D20 0.999738
```

`--settings D:\hf_w11\pinned_settings_w11.json` (md5 `a67ffc06164c81613aef5c4f8324b9b8`) **and**
`--profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382` (astrodet), sequential, `--per-run --max-evals 250`, both
named in the script header, ~42 m. Score with `python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w<N>/gate --rule
G<N>` and `python3 /mnt/d/hf_w<N-1>/prov_w<N-1>.py --self-test /mnt/d/hf_w<N>/gate`. Free controls as **FIELDS**:
`BuildId`, `DetectorVersion`, `ProfileId`, `ConcurrencyCheck` read **across the arm**, `FitInputs`, `BaselineJ`.
Wave 5's φ table is invalid on three axes — never quote it.

**The `BuildId` novelty list — FOURTEEN recorded ids, and wave 25's is the FIFTEENTH (`d79dae73582e4a6c95e5c81f1f143ffc`). Wave 25's `prov_w25.py` COMPUTES the printed count from `len(PRIOR_BUILD_IDS)`; never type it ([F80](followups.md)):**

```
5cb7e474 (w11)  103d61c4 (w12)  62334f10 (w13)  084e3485 (w14)  df3a867d (w15)  10bc1b47 (w16)
932a1366 (w17)  e745c958 (w18 B1)  7a3a03ba (w18 B2)  763d1476 (w19)  1a4dd85a (w20)
58b3b082 (w21)  7927513b (w23)                        <- and dd6ca32e (w24) is the fourteenth
```

Wave 14's `exe_floor` never recorded one (`af-fit` writes no landing), so it cannot be listed; wave 22 built no
binary. **Keep dll sha256s in a separate, labelled table** and have the scorer fail loudly if a `BuildId` ever
matches a known dll hash — an earlier draft of this charter listed a `TestApp.dll` **sha256** as a `BuildId`,
[F66](followups.md)'s exact shape committed into the document that warns about it.

**The apphost is now a SEVENTH counter-example.** `TestApp.exe` sha256
`dd7103c28cc610e72671534cf23fb9a58b5a303a19d8779545cb7418d4ce6ff7` is **byte-identical across wave 18 B1, wave 18
B2, wave 20, wave 21, wave 23 and wave 24** — six binaries that are none of each other. **Hash the dlls. And
`git diff --name-only <B_old_tree>..<B_new_tree> -- '*.cs'` is mandatory provenance for any two-binary rule:
`BuildId` proves a rebuild happened, never that the code differs.**

> **Wave 24 got that diff's BASE wrong and it mattered.** It recorded `e7a5ee6..HEAD` (10 paths) when B13's tree
> is `839c38b`, so the correct delta is `839c38b..HEAD` = **27 `.cs` files**. The 21 it omitted are F79's ASCII
> sweep — **exactly the commit that causes the one prose difference `R24-C` reports**. A reader of the original
> artifact would have found a reported difference and no commit in the provenance capable of producing it.
> **Read the BEFORE binary's own provenance file for its tree hash; do not assume the previous wave's HEAD.**

**The four fingerprint classes, and what each is FOR:**

| class | population | script | role |
|---|---|---|---|
| 1 | 42 bank landings | `bank_fingerprint_w15.py` | **preservation** — F15's fix, **eleven** clean waves |
| 2 | 59 aux (`harness_settings.json` ×39 + `synthetic_meta.json` ×20) | `aux_fingerprint_w17.py` | **preservation** |
| 3 | 48 prior-wave arms (`hf_w18/seedA0` 20, `seedA1` 20, `hf_w18/gate` 8) | `arm_fingerprint_w19.py` | **preservation** — the out-of-sample population must stay unspent |
| 4 | **whatever THIS wave's denominators are counted on** | wave-specific | **evidence.** Wave 20's was wave 19's logs; wave 21's was wave 20's; **wave 24's was wave 23's 38 `synth_validate_report.json` files, because `R24-A`'s entire BEFORE side was those files.** *A file class that becomes a denominator is evidence, and a file class that becomes evidence must become a control in the same wave* |

**Two things wave 24 learned about class 4, both cheap and both mandatory now.** (i) **KEEP the AFTER check's
output in a file** — wave 23 ran four AFTER checks that left no artifact; wave 24's are in `fp_*_AFTER.txt`.
(ii) **Count the ADDRESSABLE population, not the directories.** Wave 24's plan said 40 wave-23 cells; **38**
reports exist, because two cells timed out at 600 s and produced none. Asserting 40 would have blocked RULE R24's
entire BEFORE side on an addressability error ([F74](followups.md)). A self-test branch now asserts `EXPECTED`
equals the addressable population **and** that the gap is exactly the named NOT-RUN cells.

**Budget — price from the same INSTRUMENT, not merely a representative population.**

| instrument | rate | caveat |
|---|---|---|
| `optimize --per-run --max-evals 250`, gate's mixed real+synthetic set | **~5.2 m/run** (gate ~42 m) | — |
| the same, all-synthetic | **~2.6 m/run** | measured **at factor 1** |
| the same, a **factor-2** dataset | **neither rate describes it.** Wave 20's probe came in at **16 s against a 3 m estimate**, because a factor-2 dataset detects on a **quarter of the pixels** | budgeting a factor-2 arm at the synthetic rate over-reserves by ~10× |
| `af-fit` | **a different order** — one detection per frame, then four budgets on those points: a **39-run rung is ~11 m** (wave 14 measured 11 m 06 s) | applying the `optimize` rate over-prices it by ~an order of magnitude |
| `synth-validate` | **NO measured rate.** Never priced from `optimize` or `af-fit` | the timebox **is** the deliverable |

---

## 6. HOW TO RUN A WAVE

**A subagent's background processes die with its session and tool timeouts cap at 10 minutes, so YOU run every
measurement arm.** Agents read, write, design and analyse. **Only one `TestApp.exe` at a time.**

1. Spawn a **PRE-REGISTRATION** agent. It writes `docs/…-wave<N>-design.md` and `plans/…-wave<N>-plan.md` with
   every rule, threshold and validity gate fixed, plus drivers under `D:\hf_w<N>\` (LF endings) that **source one
   shared layout function and hand paths to the scorer through a manifest** (§4). **Ask it to judge its own
   items' legitimacy and to say where your framing is wrong** — waves 14, 15 and 20 all had the controller
   overruled on substance, correctly.
2. **You commit the pre-registration before any measurement.**
3. **You build** into `D:\hf_w<N>\exe`; record **dll** sha256s + `BuildId`; run
   `find Joko.NINA.Plugins -name '*.cs' -newermt '<build mtime>'` and record that it is empty. Never rebuild
   mid-wave.
4. **You run the gate** and score it. A partial reproduction stops the wave.
5. **You run the arms**, sequentially, in background with an `until`-loop wait.
6. Spawn an **ANALYSIS** agent. It applies the pre-registered rules and **must not re-decide one** — an
   unsatisfiable rule is a finding. Its first action is the artifact listing, and it prints the timestamp.
7. Spawn a **CODE** agent if the wave ships anything. **Every new test must be shown to fail against the
   pre-change source** — or, for new API where a revert fails the build, against a **named mutant** — and you
   verify that claim yourself. The mutation harness keeps byte backups, not VCS reverts.
8. **You run the full suite**, verified by COUNT. Baseline **3973** (wave 25, `SUITE_EXIT=0`). Name every added
   test and the delta, and **name the delta before anything is called green**.
   **`dotnet test <sln>` does NOT build `TestApp`** — build it separately and read the build output.
9. **You commit and push**, append the wave's section to PR #195 (§0.3), and verify CI by COUNT read out of the log.

**Commit with the privacy email:**
```
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
```
Build: `dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w<N>\exe'`
Tests: `dotnet.exe test "$(wslpath -w <abs>/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`
Banks: `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = **truth**),
`D:\Autofocus Bank` (19 runs).
