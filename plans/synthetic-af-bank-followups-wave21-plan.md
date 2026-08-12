# Synthetic AF bank — followups wave 21 (execution plan)

Pre-registration: [`docs/synthetic-af-bank-followups-wave21-design.md`](../docs/synthetic-af-bank-followups-wave21-design.md).
**The design is the authority on every threshold. This file is the order of operations and the commands.**

> **HARD STOP `12:00Z`. No arm starts after that.** Drop order is design §10 and it is repeated in step 9 below.
> **Commit the pre-registration BEFORE any measurement.** Then: code → suite → ONE binary → fingerprints →
> gate → arms → score → analysis → suite → commit → push → CI.

**Everything under `/mnt/d/hf_w21/` already exists and every self-test has already passed** (design §5.2).
No driver needs to be written during execution.

| file | what | self-test |
|---|---|---|
| `/mnt/d/hf_w21/layout_w21.sh` | the ONE layout declaration, sourced by both drivers (F74(b)) | via the two drivers |
| `/mnt/d/hf_w21/gate_w21.sh` | RULE G21 | PASS |
| `/mnt/d/hf_w21/b21_arm_w21.sh` | RULE B21's six-dataset arm; writes `b21_manifest.tsv` + `B21_ARM_READY` | PASS (population re-verified against the bank) |
| `/mnt/d/hf_w21/f21_probe_w21.sh` | item P, rule-free, `timeout 1200` | PASS |
| `/mnt/d/hf_w21/score_b21_w21.py` | `G21-P1/P2/P3` + RULE B21, manifest-addressed, non-zero exit on FAIL | **20 of 20** |
| `/mnt/d/hf_w21/prov_w21.py` | free controls; **eleven** recorded `BuildId`s, count derived | PASS / FAIL / AGREE |
| `/mnt/d/hf_w21/log_fingerprint_w21.py` | fingerprint class 4 (wave-20's 10 logs) | **7 of 7** |

---

## Step 0 — commit the pre-registration (target `09:20Z`, 2 m)

```bash
cd /home/ghilios/src/hocus-focus
git add docs/synthetic-af-bank-followups-wave21-design.md \
        plans/synthetic-af-bank-followups-wave21-plan.md \
        docs/waves22+-handoff-prompt.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(W21): pre-register the wave -- RULE D20 is FENCED, not re-run, and the tolerance defect is measured"
git rev-parse --short HEAD    # record this in the results doc
```

**Record the commit hash and its UTC time. The design must be committed before the binary exists.**

---

## Step 1 — item A: F69(a), the doc and the test (target `09:22Z → 09:45Z`, 20 m)

Spawn a **CODE** agent. Two edits, both in one commit, and **the controller verifies the RED claim itself**.

1. `Joko.NINA.Plugins/TestApp/OptimizationDiagnosticRunner.cs:591-594` — replace the second sentence of the
   `<summary>` with:

   > *Runs by DEFAULT (F39(b), wave 8); the opt-OUT is `--no-run-detection-binning`.
   > `--apply-run-detection-binning` is an accepted no-op, retained for wave 7's scripts (F69(c)).*

   **Do not touch anything else in that file.** `ParamsDumpTests` pins the call-site count and the dump order;
   a stray edit turns three tests red.

2. `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Harness/ParamsDumpTests.cs` — add
   `ApplyRunDetectionBinning_EveryCommentNamingTheOptInFlag_AlsoNamesTheOptOut`, using the existing
   `RunnerSource` helper at `:464`. Design §3.2 is the specification. It must:
   - enumerate every **comment block** (a maximal run of adjacent `//` / `///` lines) in every `*.cs` under
     `TestApp/` containing `--apply-run-detection-binning`;
   - assert the population size **`>= 2`** and print it — **zero occurrences FAILS** with *"the identifier this
     test is about is gone; it can no longer see its subject"*;
   - assert each such block also contains `--no-run-detection-binning`, naming the file and line of any that
     does not.

**Verify the mutant yourself. Do not accept the agent's word.**

```bash
cd /home/ghilios/src/hocus-focus
cp Joko.NINA.Plugins/TestApp/OptimizationDiagnosticRunner.cs /tmp/odr.bak     # BYTE backup, not `git checkout`
python3 - <<'PY'
p='Joko.NINA.Plugins/TestApp/OptimizationDiagnosticRunner.cs'
s=open(p,encoding='utf-8').read()
new="Runs by DEFAULT (F39(b), wave 8); the opt-OUT is <c>--no-run-detection-binning</c>."
old="No-op unless <c>--apply-run-detection-binning</c> was passed"
assert new in s, "the fix is not in the tree; nothing to mutate"
s=s.replace(new, old)          # THE MUTANT: put the false sentence back
open(p,'w',encoding='utf-8',newline='\n').write(s)
PY
grep -c "No-op unless" Joko.NINA.Plugins/TestApp/OptimizationDiagnosticRunner.cs   # ASSERT the mutation took: must be >= 1
dotnet.exe test "$(wslpath -w "$PWD/Joko.NINA.Plugins/Joko.NINA.Plugins.sln")" -c Debug --nologo \
  --filter "FullyQualifiedName~EveryCommentNamingTheOptInFlag"          # MUST be red
cp /tmp/odr.bak Joko.NINA.Plugins/TestApp/OptimizationDiagnosticRunner.cs         # restore from the BACKUP
grep -c "No-op unless" Joko.NINA.Plugins/TestApp/OptimizationDiagnosticRunner.cs  # must be 0
```

> **Restore what you SAVED, not what the repository remembers.** Wave 20's mutation harness used
> `git checkout -- <file>` while the tree carried uncommitted work, and the first mutant silently deleted the
> feature under test.

**Drop trigger:** if the test is not green by **`09:50Z`**, ship the doc fix alone, record the test as owed in
the results doc and the wave-22 handoff, and move to step 2. The doc fix is the item; the test is the durable
half.

---

## Step 2 — the suite, verified by COUNT (target `09:45Z`, 4 m)

```bash
dotnet.exe test "$(wslpath -w "$PWD/Joko.NINA.Plugins/Joko.NINA.Plugins.sln")" -c Debug --nologo
```

Baseline **3838**. Expect **3839** (one new test). **Read the count, never the tick** ([F37](../docs/followups.md)).
Do not pipe to `tail` — that masks the exit code. If `SendAsync_WritesOnABackgroundThread` fails, it is the
known flake; re-run that fixture alone and record it.

**Commit item A now, before the build**, so the results doc is written against a clean tree:

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit -am --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "fix(W21): F69(a) -- the XML doc said the opposite of the default, and a test now greps the IDENTIFIER"
```

---

## Step 3 — build ONE binary, the twelfth (target `09:50Z`, 2 m)

```bash
dotnet.exe build "$(wslpath -w "$PWD/Joko.NINA.Plugins/TestApp/TestApp.csproj")" -c Release -o 'D:\hf_w21\exe'
sha256sum /mnt/d/hf_w21/exe/TestApp.dll /mnt/d/hf_w21/exe/NINA.Joko.Plugins.HocusFocus.dll \
          /mnt/d/hf_w21/exe/TestApp.exe /mnt/d/hf_w21/exe/SynthBank/synthetic-bank-spec.json
# PROVE the binary is the tree (do not assume it):
find Joko.NINA.Plugins -name '*.cs' -newermt "$(date -u -r /mnt/d/hf_w21/exe/TestApp.dll '+%Y-%m-%d %H:%M:%SZ')"
```

**Hash the DLLs, not the exe** — wave 18's apphost is byte-identical across two different binaries
([F66](../docs/followups.md)). `find … -newermt` must print **nothing**. **Never rebuild mid-wave**
([F53](../docs/followups.md)(c)).

---

## Step 4 — four fingerprint classes, BEFORE the gate (target `09:53Z`, 2 m)

```bash
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write /mnt/d/hf_w21/bank_landing_fingerprint_BEFORE.json  # 42
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --write /mnt/d/hf_w21/bank_aux_fingerprint_BEFORE.json      # 59
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --write /mnt/d/hf_w21/w18_arm_fingerprint_BEFORE.json       # 48
python3 /mnt/d/hf_w21/log_fingerprint_w21.py  --write /mnt/d/hf_w21/w20_log_fingerprint_BEFORE.json       # 10
```

`gate_w21.sh` **aborts** if any of the four is missing. Classes 2 and 4 are **evidence**, not ceremony
(design §9).

---

## Step 5 — RULE G21, the gate, the stopping gate (start `09:55Z`, 42 m)

```bash
bash /mnt/d/hf_w21/gate_w21.sh --self-test          # again, on the day
nohup bash /mnt/d/hf_w21/gate_w21.sh > /mnt/d/hf_w21/gate_w21.log 2>&1 &
```

Run it **in the background from the controller session** and wait with an `until`-loop — a subagent's
background processes die with its session. The driver refuses to start if a `TestApp.exe` or NINA is running,
if the fingerprints are absent, or if the gate root is already populated.

The driver asserts `G21-2`, `G21-P2a/b/e` and `G21-P1a/b` itself and **exits non-zero** rather than handing on
a bad arm. Then:

```bash
python3 /mnt/d/hf_w12/score_w12.py     /mnt/d/hf_w21/gate --rule G21
python3 /mnt/d/hf_w21/prov_w21.py      --self-test /mnt/d/hf_w21/gate      # 3 directions; THEN quote it
python3 /mnt/d/hf_w21/score_b21_w21.py --self-test                          # 20 of 20
python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w19/gate            # the FAIL end, on real artifacts
echo "w19 exit=$?"                                                          # MUST be 1. Not through a pipe.
python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w21/gate --out /mnt/d/hf_w21/g21_p2p3.txt
echo "w21 exit=$?"                                                          # MUST be 0
```

**A partial reproduction stops the wave.** If `G21-1` is 8 of 8, write the marker the arm requires:

```bash
printf 'G21 PASS %s  BuildId=%s\n' "$(date -u +%FT%H:%M:%SZ)" "<the BuildId>" > /mnt/d/hf_w21/G21_PASSED
```

**If `G21-P3b` does not return exactly `{CWhiteFocus, D18_m24_deep_shed, D20_m24_bright_control, muggsie}`,
RULE B21 is `B-UNEVALUATED` by its own branch table** — the scorer applies that automatically. Do not
re-decide it.

---

## Step 6 — RULE B21, the six-dataset arm (start `10:50Z`, 15–25 m)

```bash
bash /mnt/d/hf_w21/b21_arm_w21.sh --self-test
B21_DEADLINE_UTC=11:15 nohup bash /mnt/d/hf_w21/b21_arm_w21.sh > /mnt/d/hf_w21/b21_arm_w21.log 2>&1 &
```

Datasets run **cheapest first** (`D10, D17, D09, D08, D15, D14`), each under `timeout 600`, and **no dataset
starts after `B21_DEADLINE_UTC`**. If the gate over-ran past `10:45Z`, set `B21_DEADLINE_UTC=11:05`.

The driver writes a manifest row **only after asserting** that the log and the landing exist, and writes
`B21_ARM_READY` **last** and only if `>= 4` rows exist. **Never hand-write the marker.** If the driver refuses,
that refusal is the result — record it and move on (wave 20's lesson: *the right response to a refusing
interlock is to fix the handoff, not to satisfy the interlock by hand* — and the handoff is already fixed).

```bash
python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w21/gate \
        --arm /mnt/d/hf_w21/B21_ARM_READY --out /mnt/d/hf_w21/b21_score.txt
echo "exit=$?"     # read $? directly, NOT through a pipe
cat /mnt/d/hf_w21/b21_manifest.tsv
```

---

## Step 7 — item P, the F21 price probe (start `11:20Z`, ≤ 20 m) — **FIRST IN THE DROP ORDER**

```bash
bash /mnt/d/hf_w21/f21_probe_w21.sh --self-test
nohup bash /mnt/d/hf_w21/f21_probe_w21.sh > /mnt/d/hf_w21/f21_chain.log 2>&1 &
```

**Do not start it after `11:20Z`.** Rule-free: the deliverables are the wall time, the exit code **by name**,
the round count, `HalfWidth`/step per round, and **whether a second round occurred at all**. Exit 124 means
*"> 1200 s, UNMEASURED"* and that is a legitimate answer. **Do not write a clause over it.**

---

## Step 8 — controls, analysis, write-up (from `11:25Z`)

```bash
ls -la --time-style=full-iso /mnt/d/hf_w21/     # THE WRITE-UP'S FIRST ACTION. Print this timestamp in the doc.
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w21/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w21/bank_aux_fingerprint_BEFORE.json
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --check /mnt/d/hf_w21/w18_arm_fingerprint_BEFORE.json
python3 /mnt/d/hf_w21/log_fingerprint_w21.py  --check /mnt/d/hf_w21/w20_log_fingerprint_BEFORE.json
```

Spawn an **ANALYSIS** agent to write `docs/synthetic-af-bank-followups-wave21-results.md`. Its instructions:

- **The first line of the document is the artifact-root timestamp above.** Wave 19's results doc was falsified
  by an artifact created 33 seconds before its own commit.
- **Apply the pre-registered rules; do not re-decide one.** An unsatisfiable rule is a finding.
- **RULE D20 is not re-scored, not re-run, and its diagnostic is not promoted.** Design §1.4 is the fence and
  it goes into the register verbatim.
- Every claim names the artifact it came from. Anything still open at the document's timestamp is marked
  **OPEN in place**, never reported as a completed drop.
- Register entries owed: **F69(a) CLOSED** (with the corrected "grep the identifier" lesson, which **overrides
  wave 20's Lessons #4**); **F74 fixed by construction**, with the pre-registration's own F74 instance
  (the 6-of-8 landing glob) recorded as the reinforcement; **F68** gains *check the instrument's precision* and
  *check the population is addressable*; **RULE D20 fenced**; **F21** gains the working invocation; **F59**
  re-affirmed as rejected on wave 20's measurement with the 42 m price named.

---

## Step 9 — the drop order, restated as decisions with clock times

| by | if not done | do this |
|---|---|---|
| `09:50Z` | item A's test is not green | ship the doc fix alone; record the test as owed |
| `10:45Z` | the gate has not finished | set `B21_DEADLINE_UTC=11:05`; the arm runs 4 datasets |
| `11:00Z` | the gate has not finished | **drop item B entirely.** Ship item A, the F74 fix, the instrument repairs, the D20 fence and the handoff — all complete before the gate started |
| `11:20Z` | item P has not started | **drop item P.** The `--spec` path, the scenario choice and the pinning are already recorded at zero compute |
| `11:35Z` | the results doc is not started | write measurements only; cite the design for thresholds |
| **never dropped** | — | the gate, the suite, the four fingerprint classes, the commit, the push |

---

## Step 10 — commit, push, PR, CI (from `11:35Z`, 25 m)

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(W21): RULE G21 PASS, RULE B21 <verdict>, RULE D20 fenced permanently"
git push origin ghilios/synthetic-af-bank-followups-wave13
gh pr view 191 --json body -q .body > /tmp/pr191.md   # append the wave-21 section, findings first
```

Verify CI **by COUNT read out of the log**, not by the tick. Watch the run to completion.

**Do not open a new PR. Do not push `develop`.**

---

## Traps that have bitten this series, in the order they bite

- **WSL paths to scorers**, Windows paths to `TestApp.exe`. A Windows path under WSL yields UNEVALUATED and
  looks exactly like a failed arm. Every driver here aborts on the wrong kind.
- **`TestApp.exe` eats a `while read` loop's stdin** unless you redirect `< /dev/null`. All three drivers do.
- **Read exit codes from `$?` directly, never through a pipe.** Piping to `tail` reports `tail`'s status.
- **`grep -c` prints 0 *and* exits 1**; `$(… || echo 0)` then yields `"0\n0"` and the numeric test errors out
  reporting SAFE. All three drivers use the refuse-to-guess form.
- **Only one `TestApp.exe` at a time; never run NINA during a pinned arm; no fan-out.**
- **Never rebuild an arm's directory mid-wave** ([F53](../docs/followups.md)(c)).
- **A landing is not a harness settings file**; `D17_cdk14_oiii5` finds zero stars at short exposures;
  `Panos` must be UNEVALUATED by name; Newtonsoft writes NaN as the **string** `"NaN"`.
- **Assert the POPULATION SIZE** everywhere. A check that finds 3 files and reports "3 of 3 identical" is the
  failure the whole control apparatus exists to prevent.
