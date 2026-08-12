# Wave 23 — execution plan

**Authority:** `docs/synthetic-af-bank-followups-wave23-design.md`. Where this file and the design disagree,
**the design wins**. Nothing here re-decides a rule; an unsatisfiable clause is a **finding**.

**Branch:** `ghilios/synthetic-af-bank-followups-wave23`, off `develop` @ `3d370ff`. Never push `develop`.
**Artifact root:** `/mnt/d/hf_w23` (`D:\hf_w23`). **966 GB free on D: at pre-registration time.**

**Everything under `/mnt/d/hf_w23` is written and self-tested already.** The pre-registration agent ran no
`TestApp.exe`, no `dotnet build`, no `dotnet test`. Self-test results at pre-registration time:

```
layout_w23.sh          w23_layout_self_test          PASS
gate_w23.sh            --self-test                   PASS   (and its anti-write guard was shown to FIRE, exit 1,
                                                             against a copy with a real `> …/G23_PASSED` appended)
v23_arm_w23.sh         --self-test                   PASS
gapprobe_w23.sh        --self-test                   PASS   (lumos and Panos both addressable in the bank)
score_v23_w23.py       --self-test                   24 of 24
score_p23_w23.py       --self-test                   21 of 21
f21_fingerprint_w23.py --self-test                   4 of 4   (and --write finds 4 of 4 on the real root)
score_v23_w23.py --gate /mnt/d/hf_w19/gate           exit 1, G23-P2a **0 of 8**   <- the FAIL end, REAL artifacts
score_v23_w23.py --gate /mnt/d/hf_w21/gate           exit 0, G23-P2a **8 of 8**   <- the PASS end, REAL artifacts
prov_w23.py --self-test /mnt/d/hf_w21/gate           refuses (that BuildId is on the prior list) — as designed
```

---

## 0. Before anything: the status sweep and the pre-registration commit

```bash
date -u +%H:%M:%SZ
tasklist.exe | grep -ciE "TestApp|NINA"
cd /home/ghilios/src/hocus-focus && git status --short && git log --oneline -1
```

**Step 0.1 — commit the pre-registration. NO MEASUREMENT BEFORE THIS COMMIT.**

```bash
cd /home/ghilios/src/hocus-focus
git add docs/synthetic-af-bank-followups-wave23-design.md plans/synthetic-af-bank-followups-wave23-plan.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(W23): pre-register the wave -- synth-validate step-size accuracy, the pinning audit, the gap probe"
```

> The drivers and scorers live under `D:\hf_w23` and are **not** in git, exactly as waves 11–21's were. Their
> content is fixed by this commit's description of them and by their own self-tests, both run **before** the
> arms. Cost: ~2 m.

---

## 1. Build ONE binary — the thirteenth (~1 m)

```bash
cd /home/ghilios/src/hocus-focus
git log --oneline -1                       # must be on ghilios/synthetic-af-bank-followups-wave23 @ 3d370ff (+ the pre-reg commit)
git status --short                          # must be CLEAN. If part2_w18.patch has leaked, STOP (design 3.1, G23-P1)
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w23\exe'
```

**Step 1.1 — provenance, as a FIRST-CLASS ARTIFACT. This closes the gap wave 18's results named** (*"no
wave-18 driver computes a binary hash at all; only truncated prefixes were logged"*):

```bash
{
  date -u +%FT%H:%M:%SZ
  echo "tree: $(git -C /home/ghilios/src/hocus-focus rev-parse HEAD)"
  echo "--- dll sha256 (HASH THE DLLS: TestApp.exe is the apphost and was byte-identical across w18 B1/B2) ---"
  sha256sum /mnt/d/hf_w23/exe/TestApp.dll /mnt/d/hf_w23/exe/NINA.Joko.Plugins.HocusFocus.dll
  echo "--- apphost, recorded so nobody mistakes it for a binary identity ---"
  sha256sum /mnt/d/hf_w23/exe/TestApp.exe
  echo "--- the shipped spec ---"
  sha256sum /mnt/d/hf_w23/exe/SynthBank/synthetic-bank-spec.json
  echo "--- the code delta this binary carries vs the previous merge (mandatory provenance) ---"
  git -C /home/ghilios/src/hocus-focus diff --name-only 2623771..3d370ff -- '*.cs'
} > /mnt/d/hf_w23/binary_provenance_w23.txt
cat /mnt/d/hf_w23/binary_provenance_w23.txt
```

**Step 1.2 — nothing changed after the build:**

```bash
BUILT=$(date -u -r /mnt/d/hf_w23/exe/NINA.Joko.Plugins.HocusFocus.dll +'%Y-%m-%d %H:%M:%S')
cd /home/ghilios/src/hocus-focus && find Joko.NINA.Plugins -name '*.cs' -newermt "$BUILT"     # MUST BE EMPTY
```

**Step 1.3 — the spec must have shipped with the build.** If `exe/SynthBank/synthetic-bank-spec.json` is
absent, every V23 cell would exit 2. Its sha256 must be `bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6`
(verified against the repo copy at pre-registration time). **A mismatch is a FINDING and is recorded, not an
abort** — a spec revision is exactly the drift this series exists to notice.

**Never rebuild `D:\hf_w23\exe` (F53(c)). This wave has exactly one binary and it is planned up front.**

---

## 2. The four BEFORE fingerprints (~2 m)

All four, or `gate_w23.sh` aborts. **A control written after the arm is not a control.**

```bash
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py  --write /mnt/d/hf_w23/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py   --write /mnt/d/hf_w23/bank_aux_fingerprint_BEFORE.json
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py   --write /mnt/d/hf_w23/w18_arm_fingerprint_BEFORE.json
python3 /mnt/d/hf_w23/f21_fingerprint_w23.py   --write /mnt/d/hf_w23/w21_f21_fingerprint_BEFORE.json
```

Expected counts: **42**, **59**, **48**, **4**. **A count that differs is a POPULATION failure and stops the
step** — it is a different fact from a content failure.

> Classes 1 and 3 are **also RULE P23's measurement populations** this wave. A preservation control whose
> population is also the measurement population is not ceremony: if the bank moves under the arms, P23 is
> measuring the arm.

---

## 3. RULE G23 — the gate (~42 m). A PARTIAL REPRODUCTION STOPS THE WAVE.

**Step 3.1 — self-tests FIRST. A self-test that runs after the thing it guards is not a guard** (wave 21 ran
one four datasets into its arm).

```bash
bash    /mnt/d/hf_w23/gate_w23.sh --self-test        ; echo "rc=$?"    # expect 0
python3 /mnt/d/hf_w23/score_v23_w23.py --self-test   ; echo "rc=$?"    # expect 0, 24 of 24
python3 /mnt/d/hf_w23/score_p23_w23.py --self-test   ; echo "rc=$?"    # expect 0, 21 of 21
```

**Read every exit code DIRECTLY, never through a pipe.** (`… | tail -3; echo $?` reports *tail's* status —
this happened during pre-registration and is recorded in §7.)

**Step 3.2 — run the gate, in the background, with an `until`-loop wait:**

```bash
nohup bash /mnt/d/hf_w23/gate_w23.sh > /mnt/d/hf_w23/gate_w23.log 2>&1 &
# then poll; DO NOT trust exit 0 on its own -- confirm a G23_START line AND a live TestApp
until grep -q 'G23_DONE' /mnt/d/hf_w23/gate_w23.log; do sleep 60; done; tail -20 /mnt/d/hf_w23/gate_w23.log
```

The driver itself asserts `G23-2` (8 landings), `G23-P1` (the seed reads 3 on 8 of 8 — **if it reads 4, STOP,
`part2_w18.patch` has leaked**) and prints `G23-P2`'s three counts.

**Step 3.3 — score it. Every command takes a WSL path.**

```bash
python3 /mnt/d/hf_w12/score_w12.py     /mnt/d/hf_w23/gate --rule G23        ; echo "rc=$?"
python3 /mnt/d/hf_w23/prov_w23.py      --self-test /mnt/d/hf_w23/gate       ; echo "rc=$?"
python3 /mnt/d/hf_w23/score_v23_w23.py --gate /mnt/d/hf_w23/gate --out /mnt/d/hf_w23/g23_p2p3.txt ; echo "rc=$?"
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w23/bank_landing_fingerprint_BEFORE.json ; echo "rc=$?"
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w23/bank_aux_fingerprint_BEFORE.json ; echo "rc=$?"
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --check /mnt/d/hf_w23/w18_arm_fingerprint_BEFORE.json ; echo "rc=$?"
python3 /mnt/d/hf_w23/f21_fingerprint_w23.py  --check /mnt/d/hf_w23/w21_f21_fingerprint_BEFORE.json ; echo "rc=$?"
```

**Step 3.4 — the interlock, BOTH DIRECTIONS, FAIL END FIRST (F75).** The marker is written by **the code that
computes the verdict**, never by hand.

```bash
# FAIL end, on REAL artifacts. Wave 19's gate reproduces K8 but its BuildId 763d1476 is on the prior list.
python3 /mnt/d/hf_w23/score_v23_w23.py --arm-gate /mnt/d/hf_w19/gate ; echo "rc=$?"   # expect NON-ZERO
test ! -f /mnt/d/hf_w23/G23_PASSED && echo "OK: the FAIL end left NO marker behind"    # assert it immediately

# PASS end. This is the ONLY thing that may create G23_PASSED.
python3 /mnt/d/hf_w23/score_v23_w23.py --arm-gate /mnt/d/hf_w23/gate ; echo "rc=$?"   # expect 0
cat /mnt/d/hf_w23/G23_PASSED
```

**NEVER hand-write `G23_PASSED`.** If `--arm-gate` refuses, **that refusal is the result** and the wave stops
at the gate. Record what refused and why.

**Interlock cost: ~6 m including all the fingerprint checks.**

---

## 4. RULE V23 — `synth-validate` across the bank (~35 m, timeboxed)

**Step 4.1 — self-test the arm driver BEFORE launching it.** It now has the built spec to check against, so
this run is strictly stronger than the pre-registration one (it verifies the 20 ids against the **shipped**
spec, not the repo copy):

```bash
bash /mnt/d/hf_w23/v23_arm_w23.sh --self-test ; echo "rc=$?"     # expect 0
```

**Step 4.2 — set the deadline and launch.** The default start-deadline is `23:30Z`; set it so the arm cannot
run past the point where scoring and the write-up no longer fit:

```bash
V23_DEADLINE_UTC=21:30 nohup bash /mnt/d/hf_w23/v23_arm_w23.sh > /mnt/d/hf_w23/v23_arm_w23.log 2>&1 &
until grep -q 'V23_DONE' /mnt/d/hf_w23/v23_arm_w23.log; do sleep 60; done
tail -30 /mnt/d/hf_w23/v23_arm_w23.log
```

**Watch for, and do NOT "fix" mid-arm:**

- `exit=1` lines are **expected and scoreable** — `synth-validate` exits 1 when a cell's `overallVerdict` is
  `Fail`, and those cells are the census. **The driver records rc as a manifest column and keeps the row.**
- `exit=2` is a usage/config refusal (a bad `--spec`, an `--out` inside the bank, an empty selection). If
  **every** cell exits 2, stop and fix the invocation — that is a driver defect, not a result.
- A cell that times out at 600 s gets **no manifest row** and is NOT-RUN and **named**. It never shrinks a
  denominator silently.

**Step 4.3 — score it:**

```bash
cat /mnt/d/hf_w23/V23_ARM_READY /mnt/d/hf_w23/v23_manifest.tsv | head -20
python3 /mnt/d/hf_w23/score_v23_w23.py --v23 /mnt/d/hf_w23/V23_ARM_READY --out /mnt/d/hf_w23/v23_score.txt
echo "rc=$?"
```

**If `V23-V5` returns `NOT-COMPARABLE` or fails, RULE V23 is `V-UNEVALUATED` and that is the finding.** Do not
re-run `D11` at `--max-rounds 2` to make it comparable — that would be repairing a rule with the measured value
in hand, which is the F14 / D20 fence.

**Interlocks named:** the arm ABORTS unless `/mnt/d/hf_w23/G23_PASSED` exists (§3.4); the scorer refuses unless
`V23_ARM_READY` exists **and carries `manifest=`**; the scorer reads every report path **out of the manifest**
and rebuilds none, with **no `os.walk` fallback**.

---

## 5. RULE P23 — the pinning audit (0 m TestApp, ~5 m)

```bash
python3 /mnt/d/hf_w23/score_p23_w23.py --self-test ; echo "rc=$?"   # again, before quoting anything
python3 /mnt/d/hf_w23/score_p23_w23.py --run --out /mnt/d/hf_w23/p23_score.txt ; echo "rc=$?"
```

**Expected population counts, asserted by the scorer: P1 = 40, P2 = 42, P3 = 8.** A short population is a
**POPULATION failure** and routes to `P-UNEVALUATED` for that population — it is never a shrunken denominator.

**P2 and P3 are labelled and CANNOT change P1's verdict.** P1 is the licensed population (R19's
`R-DETERMINISTIC`); P2's provenance is historical and heterogeneous.

**This step may run while nothing else does — it touches no `TestApp.exe`** — but it reads fingerprint classes
1 and 3, so run it **after** the AFTER-checks in §6 as well, and confirm both readings agree.

---

## 6. AFTER fingerprints (~2 m)

```bash
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py  --check /mnt/d/hf_w23/bank_landing_fingerprint_BEFORE.json ; echo "rc=$?"
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py   --check /mnt/d/hf_w23/bank_aux_fingerprint_BEFORE.json ; echo "rc=$?"
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py   --check /mnt/d/hf_w23/w18_arm_fingerprint_BEFORE.json ; echo "rc=$?"
python3 /mnt/d/hf_w23/f21_fingerprint_w23.py   --check /mnt/d/hf_w23/w21_f21_fingerprint_BEFORE.json ; echo "rc=$?"
```

> `synth-validate` is **spec-driven** and never opens a bank artifact, so classes 1 and 2 cannot move under
> RULE V23 even in principle. That is *why* the check is a control here and not a plea — it is the gate and the
> gap probe it is guarding against, not V23.

---

## 7. Item L — the two named real-bank gaps (~12 m, CONDITIONAL, drop D1)

**Only if the clock allows, and only after every pinned arm above has finished.**

```bash
bash /mnt/d/hf_w23/gapprobe_w23.sh --self-test ; echo "rc=$?"
nohup bash /mnt/d/hf_w23/gapprobe_w23.sh > /mnt/d/hf_w23/gapprobe_w23.log 2>&1 &
until grep -q 'L_DONE' /mnt/d/hf_w23/gapprobe_w23.log; do sleep 30; done
cat /mnt/d/hf_w23/gap_manifest.tsv
```

**The outcome is assigned BY HAND in the results doc**, per run, from the manifest and the quoted log:
`L-REPRODUCED` / `L-CHANGED` / `L-NOT-RUN`. **There is deliberately no scorer** — the recorded failure modes are
**prose** in the traps list, not fields, so a machine comparison would be a comparison against a string the
driver itself wrote. Naming that is the honest form; automating it would be theatre.

`Panos` is expected to be **UNEVALUATED BY NAME** (a degenerate σ fit is a known state, not a crash). Reporting
it as a failure would re-commit the register's own trap.

---

## 8. Analysis, suite, commit, push

**Step 8.1 — the analysis agent.** Its **first action** is:

```bash
ls -la --time-style=full-iso /mnt/d/hf_w23
date -u +%FT%H:%M:%SZ
```

**and the timestamp is PRINTED AT THE TOP of `docs/synthetic-af-bank-followups-wave23-results.md`.** Wave 19's
doc was falsified by an artifact written 33 seconds before its own commit. **Any row still open at that instant
is marked open in place, not reported as done.**

The agent **applies the pre-registered rules as written and must not re-decide one.** An unsatisfiable clause is
a finding. **RULE F14, RULE S16 and RULE D20 remain permanently fenced — do not harvest or re-score them.**

**Step 8.2 — the full suite, verified by COUNT.** Kill stragglers first (overlapping runs hold the test DLLs
and produce build failures that look like real errors — this cost three false alarms):

```bash
powershell.exe -NoProfile -Command "Get-Process testhost,vstest.console -EA SilentlyContinue | %{ \$_.Kill() }"
cd /home/ghilios/src/hocus-focus
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```

**Pre-wave baseline: 3958 by COUNT** (controller-measured; the pre-registration agent did **not** verify it and
the design says so). **This wave ships no C# change, so the count must be unchanged.** Verify by **COUNT**, never
by the tick (F37). If it is not 3958, **name the delta before anything is called green.**

*Note: the release build in §1 xcopies the plugin into NINA's folder; with F77's fix a failed copy now emits
`HF0001`. NINA must not be running.*

**Step 8.3 — commit and push:**

```bash
cd /home/ghilios/src/hocus-focus
git add docs/synthetic-af-bank-followups-wave23-results.md docs/followups.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(W23): results -- <VERDICTS>"
git push -u origin ghilios/synthetic-af-bank-followups-wave23
gh pr create --base develop --title "Wave 23 -- step-size accuracy, the pinning audit, and the gap census" --body "..."
```

Then verify CI **by COUNT read out of the log**, never by the tick.

---

## 9. The priced order, with the drop order

| # | step | instrument | est. | cumulative |
|---|---|---|---|---|
| 0 | pre-registration commit | git | 2 m | 0:02 |
| 1 | build the thirteenth binary + provenance + `-newermt` | `dotnet build` | 3 m | 0:05 |
| 2 | four BEFORE fingerprints | python | 2 m | 0:07 |
| 3 | **RULE G23** — 8 runs | `optimize --per-run --max-evals 250`, mixed | **42 m** | 0:49 |
| 3b | gate scoring + fingerprint checks + **both interlock ends** | python | 6 m | 0:55 |
| 4 | **RULE V23** — 40 cells | `synth-validate`, `--max-rounds 4` | **35 m** | 1:30 |
| 4b | V23 scoring | python | 3 m | 1:33 |
| 5 | **RULE P23** | python over landings on disk | 5 m | 1:38 |
| 6 | AFTER fingerprints | python | 2 m | 1:40 |
| 7 | **item L** (conditional) | `optimize --per-run` ×2 | **12 m** | 1:52 |
| 8 | analysis agent + results doc | — | 40 m | 2:32 |
| 8b | full suite by COUNT | `dotnet test` | 5 m | 2:37 |
| 8c | commit, push, PR, CI by COUNT | git / gh | 10 m | **2:47** |

**TestApp wall ≈ 1 h 29 m. Whole wave ≈ 2 h 47 m against a ~6 h ceiling.**

**Drop order, fixed in the design so it is not chosen by curiosity:**

| # | drop | saves | cost |
|---|---|---|---|
| D1 | item L (§7) | 12 m | two trap entries stay folklore for a tenth wave; priced |
| D2 | scenario `S0` from V23 | ~10 m | goal 2 measured in one direction only. **Recorded as a named hole** |
| D3 | V23 datasets from the **tail** of the pre-registered order (`D17` backwards) | ~1.5–2 m each | moves a denominator, never a threshold. Below 15 of 19 per scenario ⇒ `V-UNEVALUATED` by name. **`D11` is NEVER dropped** — it is the reproduction control |
| D4 | P23 populations P2 and P3 | ~2 m | P1 is licensed and carries the verdict alone |

**Nothing in the gate is droppable.**

---

## 10. Every interlock and guard, named in one place

| guard | who enforces it | what it refuses |
|---|---|---|
| `G23_PASSED` | **`score_v23_w23.py --arm-gate`**, on `rc == 0` and only then, carrying the BuildId; refuses to overwrite an existing marker | `v23_arm_w23.sh` ABORTS without it |
| the anti-write guard | `gate_w23.sh --self-test` greps **its own text** for a redirect into `G23_PASSED` | the gate driver writing the marker (F75). **Demonstrated to FIRE** at pre-registration |
| `V23_ARM_READY` | **`v23_arm_w23.sh`**, last, and only if `>= 15` scoreable rows exist **per scenario**; carries `manifest=`, `rows=`, `executed=` | `score_v23_w23.py --v23` refuses without it |
| the manifest | the driver **asserts** every report and log path exists **before** recording it | a scorer rebuilding a path. **No `os.walk` fallback, deliberately** |
| `EXECUTED_CELLS` | written **before the first run** | a pre-registered drop being mistaken for a short population |
| four BEFORE fingerprints | `gate_w23.sh` refuses to start without all four | a control written after the arm |
| one `TestApp.exe` / no NINA | every driver, `tasklist.exe | grep -c` in **refuse-to-guess** form | a concurrent arm (F55), and `Profile.Load` holding the profile |
| `$ARM` / `$GATE` / `$GAP` already populated | every driver | re-running an arm into scored output (F53(c)) |
| the clock guard | `v23_arm_w23.sh`, `$V23_DEADLINE_UTC` | a cell starting too late; it gets **no row** and is NAMED |
| `< /dev/null` | every `TestApp.exe` invocation inside a loop | TestApp eating the loop's stdin (wave 13's I2 scored 1 of 19 and "completed" in 25 s) |

---

## 11. Reporting cadence

Two or three lines per cron firing: **what is running, what was just started, what is next.** Begin every
firing with the §0 status sweep. **READ LOGS, NOT EXIT CODES** — a background job reporting `exit 0` has
repeatedly meant a driver aborted in one second. Confirm a `*_START` line **and** a live `TestApp` before
believing an arm is running. **If nothing is running and no agent is live, start the next step immediately.**
Never report "waiting".
