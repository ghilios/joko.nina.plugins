# Waves 22+ — continuation prompt

Run this after `/clear`. Waves 17, 18, 19 and 20 have executed; **wave 21 executed last and was the final wave
of that run** (hard stop 12:00Z, no new arm after it). Where this file states a wave-21 outcome it says
**pre-registered; read wave 21's results for the outcome** — and where
`docs/synthetic-af-bank-followups-wave21-design.md` exists, **the design is the authority over this file.**
**You are the controller, you delegate context-heavy work to agents, and you do not stop for approval.**

---

## 0. STANDING AUTHORISATION — unchanged

1. **Waves MAY ship user-visible product-behaviour changes** when the wave's own ship rule, fixed and committed
   BEFORE the data, is satisfied. If the rule is not met, the change does not ship and is recorded as a costed
   recommendation. **Ship unconditionally where the ship is not what the rule decides** — wave 20 shipped D1/D2/D3
   and lost a verdict, and the ship was right.
2. **~6 hours of compute per wave.** A wave that wants more must cut scope or split across two and say which.
3. **ONE branch, `ghilios/synthetic-af-bank-followups-wave13`, PR #191.** Never push `develop`. **Wave 21 closed
   the previous run, so wave 22's FIRST decision is whether PR #191 is still the right vessel** or whether it
   merges and a fresh branch opens. Decide it in writing before any measurement; do not drift into a thirteenth
   section by default.
4. **Proceed without asking.** A blocker is a result: record what was tried, what failed, what unblocking costs,
   and move on.

**Stop early** if the gate fails and cannot be explained, or if two consecutive waves produce no finding worth a
register entry. **Read §2 before committing to a wave: this run's register may now be close to dry.** The
remaining backlog is one ~2 m comment fix, a ~20 m test, a ~30 m UI session, two ~8–53 m arms and a rejected
list. **If that is honestly the whole of it, say so and stop** — the next wave owes an explicit sentence on
whether it has a finding worth an entry, and "no" is an acceptable answer.

---

## 1. WHERE THINGS STAND after waves 17–21

| | state |
|---|---|
| **the gate** | Eight values, **reproduced on TWELVE binaries** (waves 11–21), bit-identical to sixteen digits every time. It has never moved. Wave 21's `G21` PASSED on BuildId `58b3b0828eaa494499feec0b1098822f`, with `P1a/P1b/P2a–e/P3a` all **8 of 8**. §5 |
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
| **F15** | 42 of 42 bank landings byte-identical for **eight consecutive waves**. Wave 21's other three controls also passed: **59 of 59** aux files, **48 of 48** prior-wave arm landings across three roots, **10 of 10** wave-20 logs — all byte-identical, **0 could-not-look** |
| **the suite** | **3922**, verified by COUNT. **Everything below 3922 in older docs is stale.** Verify by count, never the tick ([F37](followups.md)) |

**Artifacts:** `D:\hf_w17\` … `D:\hf_w21\`. Reusable: `score_w12.py` (the gate), `prov_w<N>.py` (free controls,
self-testing), `convert_landing_w15.py` (landing → harness settings, via the production Accept path),
`bank_fingerprint_w15.py`, `aux_fingerprint_w17.py`, `arm_fingerprint_w19.py`, `log_fingerprint_w20.py`,
`score_r19_w19.py` (the 33-key landing differ), `score_d20_w20.py` (the `optimize/detected` reader),
`layout_w21.sh` (the shared path layout) and `b21_manifest.tsv` (the driver→scorer handoff, §4).

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

## 2. THE BACKLOG, priced — cheapest first

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
| **F21** | **NO.** **Now priced — the deliverable was delivered.** Nothing further is owed unless a *rule* over `synth-validate` is wanted, and one is now affordable | **45–52 s** per dataset×scenario×2 rounds |

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

**Added by waves 18–21, each paid for:**

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
- Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"`. Keep logs **ASCII**: a Unicode
  character in a redirected log arrives as the single byte `0x1A` on this machine's console code page.
- `D17_cdk14_oiii5` finds zero stars at short exposures; `lumos` exits rc=3; `astrodet` **the DATASET** is
  frameless (F14); `Panos` has a degenerate σ fit and must be UNEVALUATED **by name**.
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

**The `BuildId` novelty list — ELEVEN recorded ids, and wave 21's is the twelfth:**

```
5cb7e474 (w11)  103d61c4 (w12)  62334f10 (w13)  084e3485 (w14)  df3a867d (w15)  10bc1b47 (w16)
932a1366 (w17)  e745c958 (w18 B1)  7a3a03ba (w18 B2)  763d1476 (w19)  1a4dd85a (w20)
```

Wave 14's `exe_floor` never recorded one (`af-fit` writes no landing), so it cannot be listed. **Keep dll sha256s
in a separate, labelled table** and have the scorer fail loudly if a `BuildId` ever matches a known dll hash — an
earlier draft of this charter listed a `TestApp.dll` **sha256** as a `BuildId`, [F66](followups.md)'s exact shape
committed into the document that warns about it.

**The four fingerprint classes, and what each is FOR:**

| class | population | script | role |
|---|---|---|---|
| 1 | 42 bank landings | `bank_fingerprint_w15.py` | **preservation** — F15's fix, seven clean waves |
| 2 | 59 aux (`harness_settings.json` ×39 + `synthetic_meta.json` ×20) | `aux_fingerprint_w17.py` | **preservation** |
| 3 | 48 prior-wave arms (`hf_w18/seedA0` 20, `seedA1` 20, `hf_w18/gate` 8) | `arm_fingerprint_w19.py` | **preservation** — the out-of-sample population must stay unspent |
| 4 | **the previous wave's logs** | `log_fingerprint_w20.py` | **evidence** — wave 20's class 4 was wave 19's 28 logs because every denominator in its satisfiability analysis was counted on them; **wave 21's class 4 is wave 20's own logs**, for the same reason |

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
8. **You run the full suite**, verified by COUNT. Baseline **3838 after wave 20**; **read wave 21's results for
   the current number**. Name every added test.
9. **You commit and push**, append the wave's section to the PR (§0.3), and verify CI by COUNT read out of the log.

**Commit with the privacy email:**
```
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
```
Build: `dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w<N>\exe'`
Tests: `dotnet.exe test "$(wslpath -w <abs>/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`
Banks: `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = **truth**),
`D:\Autofocus Bank` (19 runs).
