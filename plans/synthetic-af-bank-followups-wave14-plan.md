# Synthetic AF bank — followups wave 14 (plan)

Design: [`docs/synthetic-af-bank-followups-wave14-design.md`](../docs/synthetic-af-bank-followups-wave14-design.md).
**RULE G14 and RULE F14 (V1–V4, C1–C5, the verdict) are fixed in the design and this plan does not restate them
loosely — it executes them.** This file is committed **before any wave-14 measurement**, so the
pre-registration is in git history before the data.

Artifacts root: `D:\hf_w14\` (WSL `/mnt/d/hf_w14/`). **Nothing in `D:\hf_w13\`, `D:\hf_w12\`, `D:\hf_w11\`,
`D:\hf_w10\` or `D:\hf_w7\` is rebuilt, overwritten or deleted** ([F53](../docs/followups.md)(c)) — wave 13's
39 `af_fit_summary.txt` files are Stage A's **only** input and are read-only for the whole wave.

> **THE ONE HARD CONSTRAINT.** Only one `TestApp.exe` may run at a time and a subagent's background processes
> die with its session, so **the controller runs every measurement arm**. Every step below marked **[CTRL]** is
> the controller's; **[AGENT]** steps are reads, writes, scorers and code.

---

## Step 0 — before this file is committed  **[CTRL]**

- [ ] Branch `ghilios/synthetic-af-bank-followups-wave13` is current; PR **#191** is the only PR.
- [ ] `query session` recorded (expected: session 1 `ghili` = **`Disc`**) — this is item 4's whole result.
- [ ] Item 1's code change made and verified: `AutoFocusOptions.cs:62` and `:81` both `1` → `0`; three
      assertions shown to FAIL against the pre-change source (`Defaults_AreLoadedFromAccessor`,
      `ResetDefaults_RestoresDocumentedDefaults`,
      `FitInputs_FallBackToTheDOCUMENTEDCodeDefaults_WhenTheFileDoesNotCarryThem`), plus one new invariant test
      verified to fail against a mutant whose two code-default sites disagree.
- [ ] `D:\hf_w13\snapshot_profiles_w13.py`'s `DEFAULTS` updated for the new code default (the `*` marker means
      "key absent ⇒ this code default"). **This is the one wave-13 artifact that is edited**, because leaving it
      would make it silently describe the wrong product; the edit is recorded in the results doc.

## Step 1 — item 2's code change, BEFORE the build  **[AGENT: code]**

One binary serves the whole wave (F53(c)), so `madFloor` must be in it. **Harness-only, off by default.**

1. `MathUtility.RejectionTest` (`Utility/MathUtility.cs:149`) gains
   `double madFloor = 0.0, double roundOneScale = 0.0, double roundOneFloorFactor = 0.0`.
   **The floor is applied AFTER the existing degenerate-scale guards**, never before:
   ```
   scale = mad;
   if (scale <= 0 || IsNaN(scale)) scale = stdDev(errors);
   if (scale <= 0 || IsNaN(scale)) return null;      // unchanged, still first
   var floor = Math.Max(madFloor, roundOneFloorFactor * roundOneScale);
   if (floor > 0.0) scale = Math.Max(scale, floor);   // family A and family B, one line
   ```
   `roundOneScale` is the caller's record of round 1's effective scale (0 on round 1 itself, so **family B
   leaves round 1 bit-identical by construction**). `RejectionTest` must also **expose the effective scale it
   used**, so the caller can record it — an `out double effectiveScale` or an equivalent.
2. Thread it through `AlglibHyperbolicFitting.SelectBestModel` (~line 305) → `FitWithOutlierRejection`'s
   rejection call (~line 506). `SelectBestModel` keeps its current signature as the default overload;
   the floor arrives on a new optional overload so **no production call site changes at all**.
3. `af-fit` gains `--mad-floor <f>` (family A) and `--round1-floor <α>` (family B), both defaulting to `0.0`,
   and prints them plus the **per-round effective scale at full precision** into `af_fit_summary.txt`.
   `AfFitDiagnosticRunner`'s own replicated Grubbs internals must apply the same floor, or the printed `z`
   stops agreeing with `RejectionTest`'s verdict — which is the file's own cross-check.
4. **No `*Options` class changes, no persisted option, no `Resources/OptionsDataTemplates.xaml` control.**
   If a future wave ships this to users, that project invariant applies then.
5. Tests, each **shown to fail against the pre-change source before it is called a test**:
   - `RejectionTest_AtTheDefaultFloor_IsBitIdenticalToTheUnflooredOverload` — same points, same verdict, same
     effective scale, `Is.EqualTo` not `Within`;
   - `RejectionTest_TheFloorIsAppliedAFTERTheDegenerateFallback` — a zero-MAD input still takes the stdDev
     branch, and the floor never makes the scale **smaller** than it would have been (this is the monotonicity
     the whole of Stage A rests on);
   - `RejectionTest_AFloorCanOnlySuppress_NeverRedirect` — over a battery of point sets, the floored argmax
     equals the unfloored argmax;
   - `RejectionTest_RoundOneFloor_LeavesRoundOneUntouched`;
   - `SelectBestModel_ProductionOverload_PassesNoFloor` — the production path is pinned to 0 by a test, not by
     inspection.
6. **Full suite by COUNT** before the build. Branch baseline **3755** (not `develop`'s 3744); every delta named.

## Step 2 — commit the pre-registration  **[CTRL]**

Commit the design, this plan and the drivers, with a message stating **no wave-14 measurement has run yet**.
**Nothing below starts before this commit exists.**

## Step 3 — build the ONE binary  **[CTRL]**

```
dotnet.exe build "$(wslpath -w …/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w14\exe'
```

Record `TestApp.dll` + `NINA.Joko.Plugins.HocusFocus.dll` sha256, and confirm
`strings … | grep AtrousWaveletFast` **hits** ⇒ `DetectorVersion` 2. **Never rebuilt for the rest of the wave.**
Snapshot the profile set to `D:\hf_w14\profiles_before_w14.txt` **before** any arm.

> **NINA must not be running during any arm.** `Profile.Load` holds the `.profile` open and a
> `--profile-id`-pinned arm throws *"No active NINA profile could be loaded"*. `tasklist.exe | grep -i nina`
> before starting.

---

## Step 4 — RULE G14, the gate. **A miss STOPS THE WAVE.**  **[CTRL]**

```
bash /mnt/d/hf_w14/gate_w14.sh            # ~42 m, sequential, machine quiet
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w14/gate --rule G14 | tee /mnt/d/hf_w14/gate_score.txt
python3 /mnt/d/hf_w14/prov_w14.py         /mnt/d/hf_w14/gate     | tee /mnt/d/hf_w14/prov_score.txt
```

- **A WSL path, never `D:\hf_w14\gate`.** A Windows path yields eight `UNEVALUATED` and a FAIL that looks
  exactly like a broken arm (wave 13 §1.1).
- `score_w12.py` is reused **unmodified**; `--rule` is a free label there and is not validated against a list.
- `prov_w14.py` carries what `score_w12.py` cannot: the `BuildId` must differ from **wave 13's `62334f10…`**,
  wave 12's `103d61c4…` and wave 11's `5cb7e474…`; `DetectorVersion` 2 ×8; `ProfileId` `astrodet` ×8;
  `FitInputs` one distinct value; `ConcurrencyCheck` `exclusive` **read across the arm**.
- **PASS** = 8 of 8 to 6 dp. Bit-identity to sixteen digits is reported separately as the stronger observation.
- **FAIL** = any miss ⇒ **STOP.** Item 1's change reached somewhere it should not have, and that is the wave's
  finding. Do not proceed to item 2 in a moved coordinate system.

> **RULE G14 is a WEAK control for item 2 and the results doc must say so**: the gate runs at
> `MaxOutlierRejections` = 0, where `RejectionTest` is never called. The real control for item 2 is Step 6's
> `f = 0.00` rung.

---

## Step 5 — Stage A, free, from wave 13's artifacts  **[AGENT: analysis]** *(may run while Step 4 runs — it touches no `TestApp`)*

```
python3 /mnt/d/hf_w14/stageA_w14.py /mnt/d/hf_w13 --out /mnt/d/hf_w14/stageA
```

Inputs (read-only): `/mnt/d/hf_w13/affit_syn/*/af_fit_summary.txt`,
`/mnt/d/hf_w13/affit_real/*/af_fit_summary.txt`, the matching `af_fit_points.csv`, and
`/mnt/d/hf_w13/affit_syn_score.txt` for truth-scored `e`.

Outputs, all under `/mnt/d/hf_w14/stageA/`:

| file | content |
|---|---|
| `rounds.tsv` | one row per round × rung: recovered scale interval, `max z`, limit, **margin `max z / limit`**, state (**INERT / RESOLVED / UNRESOLVABLE_FROM_ARTIFACT / INCONSISTENT**) |
| `predict.tsv` | per run × rung: **SUPPRESSED / INDETERMINATE / N/A**, and the predicted production row |
| `sem_audit.tsv` | per actual rejection: `r`, `N*`, **`s = |r|·√N*`**, the run's `ρ = σ₃/σ₀` |
| `stageA_report.txt` | the reproduction gate (72 of 72), the state counts, and the three tables above summarised |

**The gate first, before any other field is read: 72 of 72 rounds reproduced at `f = 0`, 0 INCONSISTENT.**
Below 72 ⇒ **Stage A is UNEVALUATED** and the report says so instead of printing numbers.

**Self-test, and it must be run and shown:** perturb one parsed `z` so a firing round falls below its limit and
require the reproduction gate to FAIL. *A comparator that cannot fail is not a comparator.*

**`predict.tsv` is committed before Step 6 runs.** That is what makes Stage A a prediction rather than a
post-hoc narration.

---

## Step 6 — Stage B, the ladder. Six rungs, sequential.  **[CTRL]**

```
bash /mnt/d/hf_w14/affit_w14.sh A 0.00     # THE CONTROL RUNG -- never dropped
bash /mnt/d/hf_w14/affit_w14.sh A 0.25
bash /mnt/d/hf_w14/affit_w14.sh A 0.50
bash /mnt/d/hf_w14/affit_w14.sh A 1.00
bash /mnt/d/hf_w14/affit_w14.sh B 0.50
bash /mnt/d/hf_w14/affit_w14.sh B 1.00
```

Each rung is 20 synthetic + 19 real ⇒ ~11 m (wave 13: 3 m 46 s + 7 m). **~66 m total.**

After **every** rung, before anything is scored:

```
bash /mnt/d/hf_w14/verify_affit_w14.sh /mnt/d/hf_w14/affit_A0.00/syn  20
bash /mnt/d/hf_w14/verify_affit_w14.sh /mnt/d/hf_w14/affit_A0.00/real 19
```

- **The population SIZE is its own assertion.** One bank path contains a **SPACE**
  (`timmer/5 AutoFocus_…/attempt01`) and `TestApp.exe` inherits the read loop's stdin — wave 13's I2 "completed"
  in 25 s having scored **1 of 19** runs, with every row it produced perfectly valid.
- The `Settings: D:\hf_w11\pinned_settings_w11.json` line is the positive control that the pinned detector
  took; its absence means that run used saved params and **is not comparable**.
- A short population ⇒ the rung is **UNEVALUATED**, never "fewer runs fired".

**V1 immediately after the first rung:**

```
python3 /mnt/d/hf_w14/score_affit_w14.py --control /mnt/d/hf_w14/affit_A0.00 --w13 /mnt/d/hf_w13
```

**All 39 budget tables must reproduce wave 13's exactly** — winner model, `#rej`, rejected positions, σ_focus,
redχ², R² and `minPos` at all four budgets. **A miss ⇒ Stage B is UNEVALUATED**, the remaining rungs are not
run, and the miss is investigated: the tree differs from `f9f2074` by F15 and item 1, neither of which can
reach this table, so a miss means something else moved. *This — not RULE G14 — is the proof that `madFloor` is
inert at its default.*

**Drop order if time runs out:** `A 0.50` first, then `B 0.50`. **`A 0.00` is never dropped.** Family B is
never dropped before family A's rungs — dropping the family the design predicts will fail removes the
falsification, not the cost.

---

## Step 7 — score Stage B and apply RULE F14  **[AGENT: analysis]**

```
python3 /mnt/d/hf_w14/score_affit_w14.py --root /mnt/d/hf_w14 --w13 /mnt/d/hf_w13 \
        --stagea /mnt/d/hf_w14/stageA --out /mnt/d/hf_w14/f14_score.txt
```

In this order, and the validity gates print **before** any decision clause:

1. **V1** control rung reproduces wave 13 — 39 of 39.
2. **V2** population by COUNT — 39 per rung.
3. **V3** Stage A's gate — 72 of 72, 0 INCONSISTENT. UNRESOLVABLE counts reported, not hidden.
4. **V4** every Stage B production row matches one of that run's wave-13 rows at budget ≤ B. **A row matching
   none refutes the prefix lemma ⇒ Stage A is RETRACTED IN FULL** and Stage B is reported as raw numbers with
   no counterfactual framework.
5. **C1** `max ρ(run, f) ≤ 1.10` over the 13 budget-3 firing runs, **with R² and reduced χ² reported beside
   it**. **`Panos` is UNEVALUATED BY NAME** — its budget-3 σ_focus is `NaN`, and Newtonsoft writes NaN as the
   **string** `"NaN"`; use `score_excess_w12.py`'s `num()` shape.
   **`ρ` is a CONTAINMENT measure, never an accuracy factor** — σ_focus inflates mechanically as `n − p`
   shrinks (`docs/wave14-f62-audit.md`). Any sentence in the results doc quoting 12.7× or 45.2× as accuracy is
   wrong, and the audit's weakening of the F61 `sChi` support must appear **beside** item 1's citation of it,
   not in a footnote.
6. **C2** `N_fire(B, f)` for `B ∈ {1,2,3}` × every rung, as a table.
7. **C3** `caboose` at budget 3 — CONTAINS-SELECTIVELY / CONTAINS-BLUNTLY / DOES NOT CONTAIN.
8. **C4** median and max `|Δe|` over the 20 synthetic datasets, veto only at ≥ 0.10 step.
9. **C5** blast radius at budget 1, per bank.
10. **Stage A vs Stage B agreement**, round by round. Agreement is expected on every **unflagged** round; a
    disagreement there is investigated in the design's stated order, **not** resolved by picking a winner.
11. **The verdict**, exactly as RULE F14 writes it, including the three RECOMMEND-AGAINST/NO-VERDICT branches.
    **The analysis agent must not re-decide a rule** — if a clause turns out unsatisfiable, it says so as a
    finding.

**The scorer must refuse to compute anything it cannot look at**, with a state distinct from "no difference"
and distinct from "the product declined" — wave 12's scorer was wrong twice by conflating exactly those.

---

## Step 8 — items 3, 4, 5  **[AGENT: analysis]**

- **Item 3 (F62 audit):** already running in its own agent with criteria fixed at spawn. The results doc
  records those criteria **verbatim** and then its findings. Nothing here re-scopes it.
- **Item 4 (A1–A9):** **one line.** `query session` says session 1 is `Disc` ⇒ no composited desktop ⇒ a WPF
  client area cannot be captured. Not attemptable; price stays ~30 m on a **connected** session.
- **Item 5 (F57(d)):** **CLOSE as NOT CONSTRUCTIBLE**, with the design's three reasons — the arm cannot be
  pinned (`strings D:\hf_w10\exe*\TestApp.dll | grep -c -- '--profile-id'` = **0** on both builds, the flag
  postdates them), wave 10's landings are VOID so both halves cost ~84 m, and two of the register's three
  bounds are near-vacuous for the question. Update F57(d), and archive or delete `D:\hf_w10\exe_v1wav`.

---

## Step 9 — write it up  **[AGENT: analysis]**

`docs/synthetic-af-bank-followups-wave14-results.md`, with:

- the provenance banner (build sha256s, `BuildId`, md5 of the pinned file, profile snapshot);
- **item 1 described as an OWNER OVERRIDE of wave 13's RULE M13**, in those words, with the four supporting
  facts labelled as support and F63's 6-of-8 landing movement as the user-visible consequence;
- RULE G14's verdict, with bit-identity reported separately;
- RULE F14 clause by clause, validity gates first, **ties and UNEVALUATED counts reported before wins**;
- **§ on what was NOT run and what it would cost** (design's list, re-priced against actuals);
- **§5.3-shaped budget table: estimated vs actual, every arm.**

New/updated register entries in `docs/followups.md`: **F45** (with F45(b)'s verdict and the SEM-unit finding),
**F57(d)** closed, **F62** and **F63** as the audit and item 1 touch them, and a new entry for whatever item 2
finds. Every finding gets an entry.

## Step 10 — close the wave  **[CTRL]**

1. **Full suite verified by COUNT, not by tick** (F37). Branch baseline **3755**; every added test named.
   Nothing piped to `tail` — it masks the exit code. The known-flaky `SendAsync_WritesOnABackgroundThread` is
   not chased on a full-suite run.
2. Commit with the privacy email:
   ```
   GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
     git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
   ```
3. Push to `ghilios/synthetic-af-bank-followups-wave13`; append a wave-14 section to **PR #191**'s body.
   **Never push `develop`. Do not open a new PR.**
4. **CI verified by test COUNT read out of the log**, not by its green tick.

---

## Artifact map

| path | what |
|---|---|
| `D:\hf_w14\exe\` | the one binary (item 1 + `madFloor`), never rebuilt |
| `D:\hf_w14\gate\<run>\` | RULE G14's eight landings |
| `D:\hf_w14\gate_score.txt`, `prov_score.txt` | the gate's verdict and its free controls |
| `D:\hf_w14\stageA\` | `rounds.tsv`, `predict.tsv`, `sem_audit.tsv`, `stageA_report.txt` |
| `D:\hf_w14\affit_<family><rung>\{syn,real}\<run>\` | Stage B, one directory per rung |
| `D:\hf_w14\f14_score.txt` | RULE F14 applied |
| `D:\hf_w14\profiles_before_w14.txt` | the profile set, before any arm |
| `D:\hf_w14\*.sh`, `*.py` | the drivers and scorers, LF line endings |

---

## Traps, kept where they will be read

- **Pass the scorers WSL paths** (`/mnt/d/…`), never Windows paths.
- **Silent truncation exits 0 two ways**: a bank path with a SPACE, and `TestApp.exe` eating the read loop's
  stdin without `< /dev/null`. **Assert the population size.**
- **"Could not look" needs its own state at every level, and the guard comes BEFORE any field read.**
- **Never run NINA during a pinned arm.**
- **Pin `--settings` AND `--profile-id` on every arm.** No fan-out; a pinned arm cannot fan out, and fan-out is
  1.33× not 4× (F60).
- **Newtonsoft writes NaN/Infinity as the STRINGS `"NaN"`/`"Infinity"`** — `Panos` is exactly this case.
- `D17_cdk14_oiii5` finds zero stars at short exposures; `lumos` exits rc=3 reproducibly; `astrodet` **the
  DATASET** is frameless (F14); `Panos` has a degenerate σ fit.
- **Never rebuild an arm's directory mid-wave** (F53(c)).
- **`ConcurrencyCheck == exclusive` on a single landing proves nothing.** Read it across the arm.
- **The fire rate is not one number, and neither is the blow-up ratio** — `caboose` is 12.7× through
  `bank-verify` and 45.2× through `af-fit`.
- **A control that cannot fail is not a control**, and "identical" is the easiest way for one to hide.
- **Fix the rule before the data, on the bar the predecessors faced** — and **check it is satisfiable**.
- **Say what was not run, and price it.**
- Wave 5's φ table is invalid on three axes — never quote it.
