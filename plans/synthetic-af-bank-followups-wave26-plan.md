# Wave 26 -- plan (execution)

Design (the authority): [`docs/synthetic-af-bank-followups-wave26-design.md`](../docs/synthetic-af-bank-followups-wave26-design.md).
Where this plan and the design disagree, **the design wins**; record the disagreement in
`/mnt/d/hf_w26/CONTROLLER_DEVIATIONS.md` as it happens, not afterwards.

**Vessel:** `ghilios/synthetic-af-bank-followups-wave23`, PR **#195** (OPEN at `06:29Z`, HEAD `7e98915`).
**Binary:** `/mnt/d/hf_w25/exe` -- B15, `BuildId d79dae73582e4a6c95e5c81f1f143ffc`, already `G25`-certified.
**Nothing is built. No C# changes. No sixteenth gate** (design sec 5, and the three conditions that reverse it).
**Suite baseline: 3973**, verified by COUNT out of the log, never by the tick ([F37](../docs/followups.md)).

`T0` is the moment the pre-registration commit lands. All wall-clock triggers below are UTC and absolute.

---

## 0. Artifacts already written and already self-tested (no work owed)

Written and exercised by the pre-registration agent. **No `TestApp.exe`, no `dotnet build`, no `dotnet test` was
run to produce any of them.**

| file | state |
|---|---|
| `docs/synthetic-af-bank-followups-wave26-design.md` | this wave's design |
| `plans/synthetic-af-bank-followups-wave26-plan.md` | this file |
| `/mnt/d/hf_w26/verify_derivation_w26.py` | carried forward from wave 25's, **widened for the third affix shape**, FAIL end **pinned**. `--self-test` **PASS** (5 sections). `/mnt/d/hf_w26` scores **`V-CLEAN`** |
| `/mnt/d/hf_w26/layout_w26.sh` | **written fresh, not derived** (design sec 4.4 and wave 25's D8). `--self-test` **PASS** |
| `/mnt/d/hf_w26/prep_w26.py` | population resolver + perturbed-vector builder. `--self-test` **PASS** (5 sections, both directions) |
| `/mnt/d/hf_w26/q26_arm_w26.sh` | the six-phase arm driver. `--self-test` **PASS** |
| `/mnt/d/hf_w26/score_q26_w26.py` | `RULE Q26` + `RULE L26`, `--out` **MANDATORY**, marker written on `rc == 0` only. `--self-test` **PASS** (6 sections; every verdict end reachable on its own fixture) |

**The empty directories already under `/mnt/d/hf_w26/`** (`settings/`, `a_fixed/`, `b_search/`, `c_golden/`,
`l_lumos/`, `fingerprints/`) are `layout_w26.sh --self-test`'s doing. **No binary ran.** This is wave 25's D3 in
advance, recorded so a later reader does not mistake them for a partial arm.

---

## 1. Pre-flight (~10 m, `T0`)

Run in order. **Any failure is BLOCKING and no measurement starts.**

```bash
cd /home/ghilios/src/hocus-focus
gh pr view 195 --json state --jq .state                     # must be OPEN; if MERGED, design sec 0's contingency
git log --oneline -1 && git status --short

# RULE V26 -- BLOCKING.  Both directions, then this wave's root.
python3 /mnt/d/hf_w26/verify_derivation_w26.py --self-test  # expect: SELF-TEST PASS, rc=0
python3 /mnt/d/hf_w26/verify_derivation_w26.py /mnt/d/hf_w24 | tail -6   # expect: V-DRIFT   (the FAIL end)
python3 /mnt/d/hf_w26/verify_derivation_w26.py /mnt/d/hf_w26 | tail -6   # expect: V-CLEAN   (the PASS end)

bash    /mnt/d/hf_w26/layout_w26.sh --self-test
bash    /mnt/d/hf_w26/q26_arm_w26.sh --self-test
python3 /mnt/d/hf_w26/prep_w26.py --self-test
python3 /mnt/d/hf_w26/score_q26_w26.py --self-test
```

**If `V26` returns `V-DRIFT` on `/mnt/d/hf_w26`, FIX AND RE-RUN.** Do not proceed; do not add an `ALLOW` entry
without a written reason on its own line. If any `--self-test` fails, the instrument is broken and this wave is
not measuring anything.

**Commit the pre-registration now**, before the first artifact exists, so the fence is a fact:

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(W26): pre-register the wave -- price the sensitivity pin instead of nudging it"
```

---

## 2. Controls BEFORE any arm (~4 m, `T0+10m`)

**A control written after the arm is not a control** -- wave 25's gate refused to start twice for exactly this and
both refusals were right.

```bash
# fingerprint class 3: wave 18's 40 landings, this wave's read-only population
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py --write /mnt/d/hf_w26/fingerprints/w18_arm_BEFORE.json \
  2>/dev/null || \
  find /mnt/d/hf_w18/seedA0 /mnt/d/hf_w18/seedA1 -name 'optimized_settings.json' -o -name 'aggregate_summary.json' \
    | sort | xargs sha256sum > /mnt/d/hf_w26/fingerprints/w18_arm_BEFORE.txt

# fingerprint class 1: the 42 bank landings, twelve waves byte-identical
find "/mnt/d/Autofocus Bank" /mnt/d/SyntheticAutofocusBank -name 'optimized_settings.json' \
  | sort | xargs sha256sum > /mnt/d/hf_w26/fingerprints/bank_landing_BEFORE.txt
wc -l /mnt/d/hf_w26/fingerprints/*BEFORE*
```

**Then `Q26-V0`**, which is the control that replaces the gate:

```bash
bash /mnt/d/hf_w26/q26_arm_w26.sh --phase v
cat /mnt/d/hf_w26/q26_v0.txt
```

Expect the recorded `BuildId d79dae73...`, `G25_PASSED present: yes`, and the FAIL-end line
*"the comparator DISTINGUISHES B15 from B14"*. **A hash mismatch or a BuildId mismatch STOPS THE WAVE** and a full
sixteenth gate becomes mandatory (design sec 5).

---

## 3. Resolve the population and build the vectors (~2 m, `T0+14m`)

```bash
python3 /mnt/d/hf_w26/prep_w26.py --out /mnt/d/hf_w26 --manifest /mnt/d/hf_w26/w26_prep_manifest.tsv \
  2>&1 | tee /mnt/d/hf_w26/prep_w26.txt
cat /mnt/d/hf_w26/W26_PREP_READY
```

**Read the output, not the exit code.** Three things must be true and each is printed:

- `seedA0` and `seedA1` each report **20 of 20** landings readable -- a short root is a POPULATION failure
  (`Q26-V1`), not a smaller denominator;
- the at-floor count is **8** -- `RULE P23` published it. **A different number is a FINDING, not a repair.** Record
  it in `CONTROLLER_DEVIATIONS.md`, name the landings, and proceed: the rule is scored over what is there, with the
  denominator printed;
- `settings vectors written: 32` (8 x 4). Anything less and `W26_PREP_READY` is not written, and phases `a`/`b`/`c`
  will refuse. **That refusal is the result.**

---

## 4. Phase `a` -- the fixed-vector 2x2 (band 5-20 m, reserve 20 m, `T0+16m`)

```bash
nohup bash /mnt/d/hf_w26/q26_arm_w26.sh --phase a > /mnt/d/hf_w26/w26_a.log 2>&1 &
```

**Confirm it is alive before believing it started:**

```bash
grep -c Q26_A_START /mnt/d/hf_w26/w26_a.log; tasklist.exe | grep -ci TestApp
```

A `START` line alone is not evidence -- wave 24's arm printed one and skipped all 22 cells. Wait for
`/mnt/d/hf_w26/Q26_A_DONE` and read `rows=`; the driver refuses below 8 rows and writes no marker.

**This is the step whose price is a derivation** (design sec 12). If it is not finished by `T0+50m`, note the actual
rate in `CONTROLLER_DEVIATIONS.md` -- a measured rate for `optimize --max-evals 1` is itself worth recording,
because this series has never had one.

---

## 5. Phase `b` -- the paired re-search (band 4-12 m, reserve 12 m)

```bash
bash /mnt/d/hf_w26/q26_arm_w26.sh --phase b 2>&1 | tee /mnt/d/hf_w26/w26_b.log
cat /mnt/d/hf_w26/Q26_B_DONE
```

Four datasets, two sides each, `--max-evals 250`, the invocation **verbatim from wave 18's own
`Provenance.CommandLine`** plus `--sensitivity-floor 10.0` on the constrained side and nothing else. The driver
refuses below 4 rows.

**Do not run this concurrently with anything.** One `TestApp.exe` at a time.

---

## 6. Phase `c` -- precision, and PREDICTION P-D08 (~2 m)

```bash
bash /mnt/d/hf_w26/q26_arm_w26.sh --phase c 2>&1 | tee /mnt/d/hf_w26/w26_c.log
cat /mnt/d/hf_w26/Q26_C_DONE
```

The `base` variant reproduces the owner's published table invocation exactly and is the reproduction control
(`Q26-C0`). The `sens` variant adds `--sensitivity 10.0` and nothing else.

**Design sec 7.4's PREDICTION P-D08 is fixed and directional**: `D08`'s precision **rises** above 0.966, the other
three stay at 1.000, `recall@all` **falls** on all four, `REJECTED:LowSensitivity` rises. Report it either way.

---

## 7. Phase `l` -- `lumos` (~10 m, goal 1)

```bash
bash /mnt/d/hf_w26/q26_arm_w26.sh --phase l 2>&1 | tee /mnt/d/hf_w26/w26_l.log
cat /mnt/d/hf_w26/L26_DONE
```

Two branches, both publishable: `L-PARAMETER-VECTOR` (zero rows with `Stars == 0` at the shipped defaults -- the
landing's gates emptied the frame) or `L-FRAME` (the frame yields nothing at any settings, and the run is closed as
unusable).

---

## 8. Score, and score BEFORE starting the extension (~6 m)

```bash
python3 /mnt/d/hf_w26/score_q26_w26.py --root /mnt/d/hf_w26 --out /mnt/d/hf_w26/q26_score.txt
echo "SCORE_EXIT=$?"; ls -l /mnt/d/hf_w26/q26_score.txt; cat /mnt/d/hf_w26/Q26_PASSED 2>/dev/null
```

**`--out` is mandatory and the scorer refuses without it.** Wave 25 lost this artifact twice to a stdout redirect
that was never taken and had to reconstruct every number from a transcript. The file is written by the scorer; do
not pipe and hope.

**`Q26_PASSED` is written by the scorer on `rc == 0` only.** Never write it by hand. If the scorer refuses, that
refusal is the result and it is what the results document reports.

**Score here, before phase `d`**, so the primary verdict exists on disk before the wave spends 96 minutes on a
labelled extension.

---

## 9. Phase `d` -- the labelled extension (~96 m, CLOCK-GATED)

**GO/NO-GO: start only if step 8 completed by `09:00Z`.** Otherwise D1 fires and the wave skips it.

```bash
nohup bash /mnt/d/hf_w26/q26_arm_w26.sh --phase d > /mnt/d/hf_w26/w26_d.log 2>&1 &
```

The driver self-truncates at `10:10Z` (override with `Q26_D_DEADLINE=HH:MM`) and **NAMES every dataset it did not
reach**. Its dataset order is fixed in advance as ascending wave-18 optimize seconds -- a published quantity,
independent of anything this wave measures -- so a clock stop truncates at a point the outcome did not choose.

**Re-score after it lands** (the scorer is idempotent and rewrites `--out`):

```bash
python3 /mnt/d/hf_w26/score_q26_w26.py --root /mnt/d/hf_w26 --out /mnt/d/hf_w26/q26_score.txt
```

`Q26-D` is reported in its own labelled table and is in **no** denominator of `RULE Q26`'s verdict.

---

## 10. Close out (~45 m; start the analysis agent as soon as step 8's artifact exists)

1. **Analysis agent.** Give it: the design, this plan, `/mnt/d/hf_w26/q26_score.txt`, every `w26_*.log`,
   `/mnt/d/hf_w26/CONTROLLER_DEVIATIONS.md`, and design sec 11's may/must-not list. It writes
   `docs/synthetic-af-bank-followups-wave26-results.md` and the register updates in design sec 13.
2. **The suite, by COUNT:**
   ```bash
   dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
     > /mnt/d/hf_w26/suite.log 2>&1; echo "SUITE_EXIT=$?"
   grep -E 'Failed:|Passed:|Total:' /mnt/d/hf_w26/suite.log | tail -3
   ```
   Expect **`Failed: 0, Passed: 3973, Total: 3973`**. No C# changed, so a different count is an environment
   problem, not this wave's -- but read it out of the log, never off the tick. Kill stragglers first:
   `powershell.exe -NoProfile -Command "Get-Process testhost,vstest.console -EA SilentlyContinue | %{ \$_.Kill() }"`
3. **Re-fingerprint and diff** the two BEFORE files from step 2. Any difference is a **finding** -- this wave writes
   nowhere near either population and passes `--update-run-folder` nowhere.
4. **Commit and push** with the privacy email (CLAUDE.md), update PR #195's body, then verify CI **by COUNT out of
   the log**, not by the conclusion field alone.
5. **Write the hand-off**: update `docs/waves22+-handoff-prompt.md` sec 1c -- strike `N25-E` (discharged, design sec 1.3),
   record `RULE Q26`'s verdict, and record design sec 14's pre-registered F82 choice.

---

## 11. Drop ladder and the clock checkpoints

| | item | trigger | cost |
|---|---|---|---|
| **D1** | phase `d` | step 8 not complete by **09:00Z** | a labelled extension in no denominator. The verdict is untouched |
| **D2** | phase `l` (`lumos`) | step 8 not complete by **09:40Z** | goal 1 gets nothing; `L26` returns unrun and is named as such |
| **D3** | phase `c` | scoring not started by **10:00Z** | PREDICTION P-D08 is reported as **untested**, never as unsupported |
| **HARD STOP** | phases `a` and `b` | not finished by **10:00Z** | kill; score what exists. `Q-UNEVALUATED` is a result and the wave publishes it |

**Nothing in the ladder shrinks a blind denominator.** Q1 and Q2 are untouched by every rung.

---

## 12. Standing discipline, grepped for before the wave starts

- **READ LOGS, NOT EXIT CODES.** Confirm a `*_START` line **and** a live `TestApp.exe` before believing an arm is
  running. A background job reporting `exit 0` has repeatedly meant a driver aborted in one second.
- **One `TestApp.exe` at a time. One `dotnet test` at a time. Never run NINA during a phase.**
- **Never rebuild an arm's directory mid-wave.** Pass scorers WSL paths. Assert population sizes.
- **No marker is written by a human** ([F75](../docs/followups.md)). `W26_PREP_READY`, `Q26_A_DONE`, `Q26_B_DONE`,
  `Q26_C_DONE`, `Q26_D_DONE` and `L26_DONE` are written by their drivers **last**, only on the pre-registered
  minimum row count; `Q26_PASSED` is written by the scorer on `rc == 0` only. **Grep this plan too** -- a step a
  person performs is still a writer, and it is the weakest kind.
- **Every scorer that accepts `--out` is invoked with it.** `score_q26_w26.py` enforces it by refusing; the two
  fingerprint steps redirect explicitly and the redirect target is checked with `wc -l`.
- **Apply pre-registered rules as written. An unsatisfiable clause is a FINDING**, not something to repair and
  re-score. `RULE F14`, `RULE S16` and `RULE D20` are permanently fenced.
- **Any ordinal or count in a driver's output must be COMPUTED, never typed** ([F80](../docs/followups.md)), and
  **an identifier boundary is not a word boundary** -- `RULE V26` now covers the prefix, suffix, rule-letter and
  lowercase-filename shapes, and demonstrates all four.
- **[F53](../docs/followups.md)(c): a code change must not land during a wave's measurements.** This wave ships no
  code, so the rule is trivially satisfied -- and any temptation to fix something mid-wave forfeits design sec 5's
  argument for skipping the gate.
