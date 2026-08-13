# Wave 30 — implementation plan

**Executes `docs/synthetic-af-bank-followups-wave30-design.md`.** Nothing in this plan changes what the design
pre-registers. Where a step and the design disagree, **the design wins and the disagreement is a deviation to
be recorded**, not a silent fix.

**Hard stop `2026-08-14T00:45:17Z`.** Anchor `T0` with `date -u '+%Y-%m-%dT%H:%M:%SZ'` **and write it to
`/mnt/d/hf_w30/T0.txt`** — wave 29's plan named `T0.txt` and never wrote it, so its `RULE F21` table has no
in-band start stamp.

---

## 0. COMMAND-LINE CONVENTIONS — checked against the instruments, not invented here

Wave 29's controller deviation (ii) was that *"the plan's command lines were not checked against the
instruments the plan itself commissioned"*. **Step 1's exit gate is that every command line below is grepped
out of the instrument's own `usage:` line before any of them runs.**

The forms, inherited from `/mnt/d/hf_w29/`'s headers (verified at pre-registration):

```
bash  <driver>.sh   --self-test        --out <file>  [--rule <RULE>]
bash  <driver>.sh   --run              --out <file>  [--rule <RULE>]
bash  fp_w30.sh     [--self-test|before|after]  --out <file>  [--rule W30-FP]
python3 <scorer>.py --manifest <tsv>   --rule <RULE>  --out <file>
python3 <scorer>.py --self-test        --out <file>
python3 verify_derivation_w30.py <root>  --out <file>  [--rule V30]
```

- **`--out` is mandatory on every instrument and every instrument REFUSES without it.** No step in this plan
  uses a stdout redirect in place of `--out`.
- **`--rule` is asserted against the file's own declared rule name** ([F90](../docs/followups.md)) and is a
  hard refusal on mismatch. `verify_derivation_w29.py` declares `--rule` at its `argparse` line `:748`; `V30`
  keeps it.
- **All paths passed to scorers are WSL paths.** A Windows path yields `UNEVALUATED` and looks like a failed
  arm. The one exception is `TestApp.exe`'s own `--runs` / `--opt-results` / `--out`, which take Windows paths
  and are built by the arm driver, never typed here.
- Every `TestApp.exe` invocation gets `< /dev/null`. **One `TestApp.exe` at a time.**

---

## Step 0 — the vessel, and the pre-registration commit (12 m)

1. Confirm the current state before branching:
   ```
   git branch --show-current                       # expect ghilios/synthetic-af-bank-followups-wave29
   git log --oneline -1                            # expect 59e27ab (or later on that branch)
   git status --short                              # expect clean apart from the two wave-30 documents
   gh pr view 197 --json number,state,baseRefName,headRefName,title
   ```
2. **Branch and commit:**
   ```
   git checkout -b ghilios/synthetic-af-bank-followups-wave30
   git add docs/synthetic-af-bank-followups-wave30-design.md \
           plans/synthetic-af-bank-followups-wave30-plan.md
   GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
     git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
     -m "W30 pre-registration: which gate binds on D01, measured as a FLOW with an exact conservation identity"
   git push -u origin ghilios/synthetic-af-bank-followups-wave30
   ```
3. **Open the PR with the base that makes its diff one wave** (design §0, the finding):
   ```
   gh pr create --base ghilios/synthetic-af-bank-followups-wave29 \
     --title "Wave 30: which gate actually binds on D01 — the per-gate FLOW, and a conservation identity that can fail" \
     --body-file <a body naming the product question in one sentence, the two differing predictions, and what ships (nothing)>
   ```
4. **Verify the base took and the diff is one wave** — this is the step's gate:
   ```
   gh pr view <n> --json number,baseRefName,files | \
     python3 -c "import json,sys; d=json.load(sys.stdin); print(d['number'], d['baseRefName'], len(d['files']))"
   ```
   **Expect `<n> ghilios/synthetic-af-bank-followups-wave29 2`.** Any base of `develop`, or any file count
   above 2, means the vessel finding of design §0 has just repeated itself and **the step is not done**.
5. **Do NOT** edit #196's or #197's base. Design §0 recommends it to the owner and wave 30 does not do it.
6. **Never push `develop`.** Author **and** committer email `322725+ghilios@users.noreply.github.com`; if a
   commit slips through with `ghilios@gmail.com`, amend before pushing.

**Gate:** the PR exists, its base is the wave-29 branch, its diff is 2 files, `T0.txt` is written.

---

## Step 1 — write ALL SEVEN instruments, before any of them runs (60 m)

`mkdir -p /mnt/d/hf_w30`. **LF line endings, ASCII only.** Nothing under `/mnt/d/hf_w30/` is rebuilt after this
step ([F95](../docs/followups.md); design §14).

| # | file | derived from | must contain |
|---|---|---|---|
| 1 | `layout_w30.sh` | `/mnt/d/hf_w29/layout_w29.sh` | the `(root, predicate)` array (design §6) defined **once**; `w30_sweep` taking that array and emitting `ROOT` / `ROOTPOPULATION` / `POPULATION`; `w30_fp_population`; `w30_fp_changed`; the shared self-test counter (`W30_N_OK` / `W30_N_ALL`) so **every ordinal in a `SELFTEST` line is computed** |
| 2 | `fp_w30.sh` | `fp_w29.sh` | `before` / `after` / `--self-test`; both sides call `w30_sweep` with the **same** array ([F91](../docs/followups.md)); population size asserted **equal** before any hash is compared; `realpath` dedup for the D01 overlap between rows 1 and 2; written **outside** every tree it sweeps |
| 3 | `verify_derivation_w30.py` | `verify_derivation_w29.py` | design §7 — all three `historical_ranges` generations with self-test `[6]` asserting **all four ends**; the widened `PRINTS` selector; **per-clause populations with the `N = 0` refusal**; the 12-region tree with `A = None`; the **realized-tuple membership assertion** |
| 4 | `w30g_arm_w30.sh` | `w29l_arm_w29.sh` | design §4.3 — five cells, JSON keys **resolved not typed**, the four defocus preconditions asserted, the gate order extracted from source at **both** trees, dll sha256 asserted against `binary_provenance_w25.txt`, `cp` without `-p` + `touch`, per-cell BEFORE/AFTER fingerprints, the manifest, and the `READY` marker written **last** and only on full success |
| 5 | `score_w30g.py` | `score_w29l.py` | design §4.4 / §4.5 / §11 — the flow statistic, the conservation identity, the 36-region tree, the `Sensitivity` fence, the F22 caveat printed beside every recall number |
| 6 | `score_w30d.py` | new | design §5 — parses wave 29's §11 table out of the committed document, resolves each row, searches the declared set, the 6-region tree |
| 7 | `kcol_w30.py` | new | design §2.6 — `truthModel.max(hfrMin, hfrMinEffective)` per dataset from `/mnt/d/SyntheticAutofocusBank/<ds>/synthetic_meta.json`, emitting the `K` column rows |

**Three things the arm driver must get right, each of which cost a previous wave:**

- **The knob edits.** Seed tree: the same source wave 29's arm used. Edited keys **resolved** from the tree's
  own declared key set against a candidate list, refusing on zero or >1 match. Expected resolutions:
  `MaxDistortion`, `BrightnessSensitivity`, `MinStarBoundingBoxSize`. `EffectiveSensitivityGate` is **left
  alone** and each cell asserts its `key detector knobs:` echo shows the intended sensitivity (design §4.3).
- **The `Sensitivity` value is computed, never typed.** `landed − 4 × step`, with `step` read at run time from
  `OptimizerVariable.cs`'s own `Continuous(nameof(StarDetectorParams.Sensitivity), …)` declaration. Print the
  declaration line and the arithmetic.
- **The `FALSE-NEGATIVE ATTRIBUTION, HIGH TIER ONLY` block is anchored on its header**
  ([F68](../docs/followups.md) part 5) — the line appears twice per report and a first-match reader takes the
  wrong one. Wave 28's self-test proved this; wave 30's must too.

**Self-tests, all written now and all captured at their own steps:** `w30g_arm_w30.sh --self-test`,
`fp_w30.sh --self-test`, `score_w30g.py --self-test`, `score_w30d.py --self-test`,
`verify_derivation_w30.py --self-test`. **`score_w30g.py`'s self-test uses wave 29's `L0/L1` and `L0/L3` pairs
as live fixtures with known answers** — the two exact balances in design §4.2 — not synthetic mutants, and it
must demonstrate **both** ends: a conserving pair passes, and a deliberately perturbed copy of one FAILS.

**Gate for Step 1 (both halves, both blocking):**

1. **Every command line in §0 and in Steps 2–8 is grepped out of the corresponding instrument's own `usage:`
   line.** Print the grep output. A mismatch is fixed **in the plan**, not worked around at run time.
2. `wc -l` and `file` on all seven, confirming LF and ASCII; then `ls -la --time-style=full-iso /mnt/d/hf_w30/`
   captured, so the "last instrument written" timestamp exists for the `F21` table.

---

## Step 2 — `W30-FP` BEFORE. The first thing any instrument does (5 m)

**Nothing else may read a declared root before this completes** — not `V30`'s fixture probes, not the arm.

```
bash /mnt/d/hf_w30/fp_w30.sh --self-test --out /mnt/d/hf_w30/fp_selftest_w30.txt
bash /mnt/d/hf_w30/fp_w30.sh before --out /mnt/d/hf_w30/fp_BEFORE.txt --rule W30-FP
```

**Gate:** `fp_BEFORE.txt` exists, prints a `ROOTPOPULATION` line for each of the six rows in design §6, and a
`POPULATION` total. **Capture the wall time** — design §6 predicts ~40 s at wave 29's measured 61 MB/s and
prices it at 3 m; the measured number goes in the `F21` table either way.

**If a declared root is absent**, `w30_sweep` writes `COULD-NOT-LOOK <root> absent` and the root **leaves the
denominator** rather than scoring zero. Do not substitute another root.

---

## Step 3 — `RULE V30` pre-flight over the POPULATED root, and the fixture measurement (15 m)

```
python3 /mnt/d/hf_w30/verify_derivation_w30.py --self-test --out /mnt/d/hf_w30/vdrift_selftest_w30.txt

for R in /mnt/d/hf_w24 /mnt/d/hf_w26 /mnt/d/hf_w27 /mnt/d/hf_w28 /mnt/d/hf_w29; do
  python3 /mnt/d/hf_w30/verify_derivation_w30.py "$R" \
    --out "/mnt/d/hf_w30/vdrift_probe_$(basename "$R").txt" --rule V30
done

python3 /mnt/d/hf_w30/verify_derivation_w30.py /mnt/d/hf_w30 \
  --out /mnt/d/hf_w30/vdrift_w30.txt --rule V30
```

**Pin the per-clause fixtures FROM the five probes, never inherit them** ([F103](../docs/followups.md)).

**Design §7's forward prediction, to be scored here and reported whichever way it lands:**

| root | predicted `-B` | predicted role at wave 30 |
|---|---|---|
| `hf_w24` | 0 (`-C` = 2) | durable `-C` fixture, fourth wave |
| `hf_w26` | 0 | asserted silent |
| `hf_w27` | 0 | asserted silent |
| `hf_w28` | **0 — EXPIRES**, from 100 at wave 29 | asserted silent |
| `hf_w29` | **large, non-zero** | the new live `-B` fixture |

**A clause with no live fixture is a FAIL, not a pass.**

**Gate:** `vdrift_w30.txt` reports **`files checked: 7`** (all seven instruments — wave 29's plan said five and
its verifier found seven), `SELFTEST V30 <n> of <n>` with every clause group present, every clause's
`candidates: N` printed and non-zero where a population is declared, `regions enumerated: 12   uncovered: 0`,
the realized tuple printed and asserted a member, and **`>>> V-CLEAN`**.

**`V-DRIFT` blocks the wave.** Fix the instrument, re-run this step, and record it as a deviation. **Do not
proceed to Step 4 on a `V-DRIFT`.**

---

## Step 4 — `RULE W30-G`, the arm (25 m wall, ~15 m compute)

```
bash /mnt/d/hf_w30/w30g_arm_w30.sh --self-test --out /mnt/d/hf_w30/w30g_arm_selftest.txt
bash /mnt/d/hf_w30/w30g_arm_w30.sh --run       --out /mnt/d/hf_w30/w30g_arm.log --rule W30-G
```

The driver, in order, and it refuses at the first failure rather than continuing:

1. asserts the dll sha256 of `/mnt/d/hf_w25/exe` against `/mnt/d/hf_w25/binary_provenance_w25.txt`;
2. extracts the gate order from `StarDetector.cs` at `HEAD` **and** at the B15 tree `29665a62e4f8`
   (`git show 29665a62e4f8:…/StarDetector.cs`) and **refuses if they differ**;
3. asserts `DefocusAwareGates`, `DefocusAwareDonutDetection`, `DefocusAwareStructure` are all `false` and
   `DonutMaxStreakEccentricity` is `1.0` in the seed tree (design §4.1(b));
4. resolves each edited key and prints the resolution;
5. per cell: `cp` without `-p`, `touch`, edit, BEFORE fingerprint, run, AFTER fingerprint, assert exactly one
   file changed and every unnamed field unchanged, assert the `key detector knobs:` echo;
6. writes `w30g_manifest.tsv` and, **last and only on full success**, `W30G_MANIFEST_READY`.

**The five cells (design §4.3):**

| cell | `MaxDistortion` | `BrightnessSensitivity` | `MinStarBoundingBoxSize` |
|---|---|---|---|
| `M0` | 0.5 | 36.333… | 6 |
| `M1` | **0.3** | 36.333… | 6 |
| `M2` | 0.5 | **landed − 4 × step** | 6 |
| `M3` | **0.3** | **landed − 4 × step** | 6 |
| `M4` | **0.2** | 36.333… | 6 |

`NoiseClippingMultiplier` = **1.0** on all five.

**Gate:** all five `exit=0`; `W30G_MANIFEST_READY` present; the arm's wall time inside the pre-registered band
**725–1 600 s** (predicted ~950 s) — a time outside the band is **reported, not silently accepted**;
`SELFTEST W30G-ARM <n> of <n>`.

**If the arm exceeds ~30 m wall**, kill it, record it, and go to the cut list (§ Step 9's cut order). Do not
restart cells piecemeal — `M0` is the control and a partial arm has no licence for the imports.

---

## Step 5 — `RULE W30-G`, scoring (12 m)

```
python3 /mnt/d/hf_w30/score_w30g.py --self-test --out /mnt/d/hf_w30/w30g_selftest.txt
python3 /mnt/d/hf_w30/score_w30g.py --manifest /mnt/d/hf_w30/w30g_manifest.tsv \
  --rule W30-G --out /mnt/d/hf_w30/w30g_score.txt
```

The scorer reads **every** artifact path out of the manifest ([F74](../docs/followups.md)) and rebuilds none,
including the two wave-29 imports `/mnt/d/hf_w29/l/out/L{1,3}/attempt01/golden_eval.txt`.

It must print, in this order:

1. **the population lines** — cells scored, fields compared for the `M0`/`L0` control, gates ordered, single-
   knob cells checked — each as `candidates: N   findings: M`, **refusing on an unexpected `N = 0`**;
2. **`G-0`'s seven sub-assertions**, each with its own result;
3. **the conservation check per single-knob cell** — every upstream Δ, the release, every downstream Δ, the
   three terminals, and the balance as an integer equality;
4. **the capture matrix** `C_{g→h}` in full;
5. `Σ R_g`, `recapture`, `survive_g` per gate, `ΔTP(M3)`, and the **computed** material bar
   `0.01 × FN_total(M0)`;
6. the **realized clause tuple** and its membership assertion, then
   `regions enumerated: 36   uncovered: 0`;
7. the verdict, and the [F22](../docs/followups.md) caveat beside every recall number.

**The `Sensitivity` fence is enforced in code:** the scorer refuses to name `Sensitivity` in any verdict string
even if it carries the largest release, and prints the fence text ([F84](../docs/followups.md), `RULE S16`).

**Gate:** a verdict from design §11's table; `uncovered: 0`; the realized tuple asserted a member. **An
unsatisfiable clause is a FINDING and is not repaired.** A `G-TREE-GAP` is reported with what the design's tree
would have said, and **is not re-scored after a repair**.

---

## Step 6 — `RULE W30-D` and the two retrospective captures (15 m, ~2 m compute)

```
python3 /mnt/d/hf_w30/score_w30d.py --self-test --out /mnt/d/hf_w30/w30d_selftest.txt
python3 /mnt/d/hf_w30/score_w30d.py --manifest /mnt/d/hf_w30/w30d_manifest.tsv \
  --rule W30-D --out /mnt/d/hf_w30/w30d_score.txt
```

Then the **retrospective** capture of wave 29's three missing scorer self-tests — labelled as retrospective
everywhere they are quoted, because a self-test run now is weaker than the gate the wave-29 plan wrote:

```
python3 /mnt/d/hf_w29/score_w29s.py --self-test --out /mnt/d/hf_w30/w29s_selftest_RETRO.txt
python3 /mnt/d/hf_w29/score_w29r.py --self-test --out /mnt/d/hf_w30/w29r_selftest_RETRO.txt
python3 /mnt/d/hf_w29/score_w29l.py --self-test --out /mnt/d/hf_w30/w29l_selftest_RETRO.txt
```

**Write them under `/mnt/d/hf_w30/`, never under `/mnt/d/hf_w29/`** — wave 29's root is a declared read-only
root of this wave's fingerprint and writing into it would produce a self-inflicted `VIOLATED` at Step 8.

**Gate:** a `W30-D` verdict with `uncovered: 0`; three `RETRO` captures on disk, each with its own
`SELFTEST … <n> of <n>` line; `P-D08`'s row named in the `D-STALE` output with the artifact path
`/mnt/d/hf_w26/pd08/` and the commit `e42e2df` if the rule finds it, or a recorded disagreement with design
§2.1 if it does not.

---

## Step 7 — the results-table `K` column (12 m)

```
python3 /mnt/d/hf_w30/kcol_w30.py --out /mnt/d/hf_w30/kcol_w30.txt --rule W30-K
```

Reads `truthModel.hfrMin` and `truthModel.hfrMinEffective` from each
`/mnt/d/SyntheticAutofocusBank/<dataset>/synthetic_meta.json` (20 of 20 confirmed present at pre-registration,
key names only), emits `K = max(hfrMin, hfrMinEffective)` per row, and asserts **20 rows** before emitting any.

Then edit `docs/synthetic-af-bank-results-table.md`:

1. add the **`K` column**, values pasted from `kcol_w30.txt` and never retyped;
2. add the note that **`D01` / `D02` / `D03` are the only three floored by the generator**;
3. **remove any 2–4 px band claim below the band** (`W28-B` = `B-SPLIT`).

**Gate:** the three edits are present and the `K` values in the document match `kcol_w30.txt` byte for byte on
a `diff` of the extracted column.

**This is the first cut after `M4`** (design §13). If it is cut, §16's register entry must say **withdrawn**,
not "still owed" — it has now been carried by waves 28, 29 and 30.

---

## Step 8 — `W30-FP` AFTER, the closing `V30`, and the three reversal checks (8 m)

**Order matters and the reversal checks come first**, so that a C# change discovered here stops the wave before
it writes a clean-looking close.

```
git diff --name-only <base> HEAD -- '*.cs'
git ls-files --others --exclude-standard -- '*.cs'
```

**Union must be EMPTY.** If it is not, the 42 m `optimize` gate becomes owed and the unit suite runs by count
against baseline **3997** ([F37](../docs/followups.md)) — design §9. Also re-assert the arm binary's dll sha256
against `binary_provenance_w25.txt`, and confirm the arm's per-cell BEFORE/AFTER fingerprints showed exactly
one changed file per cell.

Then:

```
bash /mnt/d/hf_w30/fp_w30.sh after --out /mnt/d/hf_w30/fp_AFTER.txt --rule W30-FP
python3 /mnt/d/hf_w30/verify_derivation_w30.py /mnt/d/hf_w30 \
  --out /mnt/d/hf_w30/vdrift_w30_CLOSING.txt --rule V30
```

**Gate:** BEFORE and AFTER populations asserted **equal** before any hash is compared; a `PRESERVED` /
`VIOLATED` / `UNEVALUATED` verdict per design §11; the closing `V30` is **`V-CLEAN`** over the now-fully-
populated root. **Neither of these is cut at any budget.**

**Take the roll call here**, not at the end of the write-up:

```
date -u '+%Y-%m-%dT%H:%M:%SZ'; ls -la --time-style=full-iso /mnt/d/hf_w30/
```

**Any artifact whose mtime is within a few seconds of the roll call must be checked for being mid-write**
(`stat` it twice, two seconds apart) and **reported as open at the timestamp AND as a completed artifact** if
it closes afterwards. Wave 19's document was falsified by an artifact written 33 s before its own commit; wave
29's fix for that was to say both things, and it is now standing practice.

---

## Step 9 — results, register, handoff (80 m)

Write `docs/synthetic-af-bank-followups-wave30-results.md`. It must contain, and the order is the order a
reader needs:

1. **THE TIMESTAMP** — the roll call table from Step 8, with each row's state at that instant.
2. **§0 What this document is allowed to say** — every number quoted from a cited artifact; the design's rules
   **applied as written**; a rule that turned out unsatisfiable, under-specified or directionally wrong is a
   **finding**, not a repair; **everything is synthetic-only and `W30-G` is one dataset, `D01`**.
3. **The verdict table** — `W30-G`, `W30-D`, `V30`, `W30-FP`, each with its population and artifact.
4. **The headline**, whichever of design §4.6's two predictions survived, **naming which document predicted
   which** and stating plainly if both were wrong.
5. **The capture matrix in full**, and the conservation identity's result per cell.
6. **The [F22](../docs/followups.md) caveat, inline, before any recall number** — and no recall gain quoted as
   a product benefit anywhere in the document (design §2.7).
7. **What is still unknown about `D01`, said plainly**, in the shape wave 29 §3.3 used.
8. **Instrument findings** — populations that printed `0` and refused, `None` regions entered, tuple-membership
   assertions that fired.
9. **Controller deviations**, named, with what each cost.
10. **Under-specified in the plan**, per the instrument agent.
11. **[F21](../docs/followups.md): estimate vs actual per step**, plus the measured sweep constant for the
    **trimmed** 2.4 GB root set against wave 29's 41.8 GB / 61 MB/s.
12. **What was not run and what it costs.**
13. **Register entries** F104–F107 and the amendments, per design §16, **ready to paste** — and append them to
    `docs/followups.md` in the same commit.
14. **Wave 31, priced and goal-scored**, plus **the stop question answered** per design §17.
15. **Handoff**, folded here rather than written separately.

Commit with the privacy email, push, and **watch nothing further** — wave 30 ships no code, so there is no
release run to follow.

---

## THE CUT ORDER, decided before the clock is short

1. **`M4`** — fenced out of every verdict branch by construction, so cutting it changes no verdict and no
   register entry. −3 m compute, −3 m wall.
2. **Step 7, the `K` column** — −12 m, and if cut it is **withdrawn**, not re-listed a fourth time.
3. **The three retrospective self-test captures** in Step 6 — −6 m. `RULE W30-D` itself is **not** cut with
   them; it is the step's substance.
4. **Step 9's handoff document**, folded into the results file.

**NOT cut at any budget:**

- **the closing `V30`** (Step 8, ~2 m) — [F95](../docs/followups.md)'s remedy is both halves or neither;
- **the `W30-FP` BEFORE sweep** (Step 2, ~3 m) — it cannot be recovered retroactively, and wave 29 paid the
  full cost of learning that;
- **`RULE W30-D`** — design §2.1 measured the ledger already rotting.

**If the arm cannot run at all** (binary missing, provenance mismatch, seed tree absent), the wave does **not**
substitute a different binary or rebuild one. It records `G-UNEVALUATED`, keeps `W30-D`, `V30` and the `K`
column, and says in §12 exactly what the arm would have cost.

---

## STANDING CONSTRAINTS — every one of them earned

- **Never push `develop`.** Feature branch, PR, and for wave 30 a **PR base that is the wave-29 branch**.
- Author **and** committer email `322725+ghilios@users.noreply.github.com`.
- Specs in `docs/`, plans in `plans/` — both already written.
- **Pass scorers WSL paths.**
- **`--out` mandatory**; **`--rule` asserted** against the file's own declared name.
- Populations asserted; **ordinals and counts computed, never typed** — including the gate order, the axis step
  and the material bar.
- Paths through a **manifest**; BEFORE/AFTER from **one** expression called twice.
- `cp` without `-p`; `touch` after every copy or restore.
- `find … -print0 | sort -z | xargs -0` — `/mnt/d/Autofocus Bank` contains a space.
- **One `TestApp.exe` at a time**; `< /dev/null` on every invocation; **no directory rebuilt mid-wave.**
- **Read-only for the whole wave:** every root in design §6's table. Nothing wave 30 writes goes anywhere but
  `/mnt/d/hf_w30/` and the repository.
- **An unsatisfiable clause is a FINDING. Do not repair it.**
