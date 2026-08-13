# Waves 22+ — 12-hour autonomous run

**Read this file, then `docs/waves22+-handoff-prompt.md`, then `docs/followups.md`. Those three are the
authority; this file only sets the run's boundaries.** Everything you need is on disk — nothing depends on a
prior conversation.

---

## 0. STANDING AUTHORISATION

1. **Proceed without asking.** A blocker is a result: record what was tried, what failed, what unblocking would
   cost, and move on.
2. **Waves MAY ship user-visible product changes** when the wave's own ship rule, fixed and committed BEFORE the
   data, is satisfied. If the rule is not met, the change does not ship and is recorded as a costed recommendation.
3. **~6 hours of compute per wave.** A wave that wants more must cut scope or split, and say which.
4. **ONE branch: `ghilios/synthetic-af-bank-followups-wave23`, PR #195 (OPEN).** Never push `develop`.
   It carries waves 23, 24 and 25. **Every wave's FIRST decision, in writing, before any measurement: is PR #195
   still the right vessel, or has it merged and a fresh branch opens?** Check `gh pr view 195 --json state`
   immediately before committing the pre-registration and take that branch instead if it merged. Do not drift
   into a third section by default — waves 23 and 24 each decided this explicitly and recorded the reasoning.

## 1. STOP CONDITION — **THIS RUN HAS ENDED**

**The run stopped after wave 26**, which was declared the last wave **in writing before it ran**
(`docs/synthetic-af-bank-followups-wave26-design.md` §1.7) rather than on the clock. The reason is specific and
it is not "the register is dry": **every remaining item needs a binary**, wave 26 deliberately built none, and
none of them fits between wave 26's landing and the owner's `12:00Z` stop.

**Waves 22–26 executed.** Verdicts, in order: wave 23 `G-PASS` / `V-GAPPED` / **`P-SENSITIVITY-PINNED`**; wave 24
`G-PASS` / `R-PRESERVED` / `B-UNEXERCISED` / `D-REPORTED`; wave 25 `V-CLEAN` / `G-PASS` / `W-UNEXERCISED` /
`N-PRESERVED` / `M-CORRECTED`; wave 26 `V-CLEAN` / **`Q-PIN-COSTED`** / `L26` **could-not-look**.

**If you are reading this to decide whether to start wave 27: read `docs/waves22+-handoff-prompt.md` §1c, "THE
STATE OF THE REGISTER", first.** It carries the corrected inventory — three items the charter listed as open are
**discharged**, and exactly **one** open item needs no binary and costs ~2 minutes.

**The dry-wave stop was NEVER armed, and the counter finished at zero.** Wave 23 opened [F79](followups.md) and
produced the F26 scope correction; wave 24 opened **[F80](followups.md)**, verified F79, corrected F34's `D01`
attribution, and closed F21's costing; **wave 25 SHIPPED a product fix ([F81](followups.md)), CLOSED
[F34](followups.md), opened [F82](followups.md), and extended F80 with two demonstrated gaps in its own new
checker**; **wave 26 opened [F83](followups.md)** — `J` is composed with **no precision term** when a run is
unlabelled, which is the mechanism behind `RULE P23`'s 22-of-24 driven pins — **and extended F80 with a third
gap plus a control that exited its caller while printing PASS.** Four consecutive productive waves.

**Had the run continued, the stop condition would still apply**: stop if the gate fails and cannot be explained,
or if two consecutive waves produce no finding worth a register entry. **"There is nothing left worth a wave" is
an acceptable and welcome answer** — say it plainly rather than manufacturing work.

## 2. THE STATUS SWEEP — run this at every cron firing, before anything else

```bash
date -u +%H:%M:%SZ
tasklist.exe | grep -ciE "TestApp|NINA"
cd /home/ghilios/src/hocus-focus && git status --short && git log --oneline -1
gh run list --branch ghilios/synthetic-af-bank-followups-wave23 --limit 2 \
  --json status,conclusion,headSha --jq '.[]|"\(.headSha[0:7]) \(.status) \(.conclusion // "-")"'
```

**READ LOGS, NOT EXIT CODES.** A background job reporting `exit 0` has repeatedly meant a driver aborted in one
second. Confirm a `*_START` line **and** a live `TestApp` before believing an arm is running.

> **Wave 24 hit this exactly, and the `*_START` line was present.** `b24_arm_w24.sh` aborted in one second on
> first launch, skipping all 22 cells, because it compared wall-clock times as **strings** (`"21:01" > "03:00"`
> is lexically true) against a deadline that was **tomorrow**. The `START` line alone would have been believed;
> only *"no live `TestApp.exe`"* caught it. **And the self-test branch guarding that comparison was
> `[ now > "23:59" ]` — a tautology that could never fire, which returned rc=0 immediately before the failed
> launch. A control that cannot fail is not a control.** Wave 24 results §9.1.

**If nothing is running and no agent is live, start the next step immediately.** Never report "waiting".

## 3. HARD CONSTRAINTS

- **Only one `TestApp.exe` at a time.** Never run NINA during a pinned arm.
- **Only one `dotnet test` at a time.** Overlapping runs hold the test DLLs and produce build failures that look
  like real errors — this cost three false alarms. Kill stragglers first:
  `powershell.exe -NoProfile -Command "Get-Process testhost,vstest.console -EA SilentlyContinue | %{ \$_.Kill() }"`
- **Never rebuild an arm's directory mid-wave.** Pass scorers WSL paths (`/mnt/d/...`). Assert population sizes.
- **NINA may be launched** for UI checks — the owner has granted this — but **never while a pinned arm runs**, and
  see F77 below before trusting any UI result.
- Commit with:
  `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com"` and
  `--author="George Hilios <322725+ghilios@users.noreply.github.com>"`.
- **Suite baseline: 3973**, verified by COUNT in wave 25 (`Failed: 0, Passed: 3973, Total: 3973`, `SUITE_EXIT=0`).
  Everything below it in older docs is stale (3922, 3933, 3958, 3960, **3966** all are).
  Verify by **COUNT** out of the log, never by the tick ([F37](followups.md)).
  Local: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`
  (no `dotnet` in WSL; use Windows `dotnet.exe` via interop, ~4 min).
- **`dotnet test <sln>` does NOT build `TestApp`.** A compile break in any runner never surfaces in the suite.
  Build it separately and read the build output.

## 4. F77 — CHECK THE DEPLOYED BINARY BEFORE QUOTING ANY UI RESULT

The csproj PostBuild xcopies the plugin into NINA's folder **and fails silently while NINA holds the DLL**. The
build still reports success. This produced **two field tests against a stale binary**, one of which looked like
a passing confirmation of code that was never loaded.

```bash
DST="/mnt/c/Users/ghili/AppData/Local/NINA/Plugins/3.0.0/Hocus Focus/NINA.Joko.Plugins.HocusFocus.dll"
SRC=Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/bin/Debug/net8.0-windows7.0/NINA.Joko.Plugins.HocusFocus.dll
date -u -r "$DST" +%H:%M:%SZ; sha256sum "$SRC" "$DST" | cut -c1-24    # must MATCH
strings "$DST" | grep -c <SymbolUnderTest>                             # unanchored: `^…$` lied once
```

NINA's log filename encodes start time and pid — `<yyyyMMdd>-<HHmmss>-<version>.<pid>-*.log` — so compare it
against the DLL's mtime to prove which binary a session loaded.

## 5. THE WAVE ORDER

pre-registration committed → **`--verify-derivation` pre-flight ([F80](followups.md))** → build ONE binary →
gate (score it **and** self-test its scorer in **both** directions) → arms sequentially → analysis agent writes
results + register → full suite by COUNT → commit, push, update PR #195 body → verify CI by COUNT out of the log
→ next wave's pre-registration.

**The pre-flight is ~15 m and it is BLOCKING.** Wave 25 built it and it returned `V-DRIFT` **21 findings** on the
wave's own instruments before any measurement — including a `sed` **ordering** bug in the plan that left 10 of 15
sibling references pointing at a nonexistent file, and a surviving `G24_START` that would have hung the
controller's own waiter. Wave 26 widened it again (`V-DRIFT`, 8 findings, then `V-CLEAN`).

**Derive the next one from `/mnt/d/hf_w26/verify_derivation_w26.py`**, which matches previous-wave tokens by
**SHAPE** rather than by an enumeration of the rule letters that happen to have been used — the wave-25 pattern
missed **`W25_BEFORE_READY`**, wave 25's own arm interlock marker, and four score files. Its remaining known
limit: **its `PREV` is a single wave**, so a label that skips a generation is invisible to it. Its FAIL end is
**pinned to `/mnt/d/hf_w24`** in source, because *a regression fixture that is "whatever came before" has a shelf
life of one wave* — wave 25 repaired its own root and would have left wave 26 without a known-bad input. See
[F80](followups.md). Run it **before the gate**, never after.

**A gate MAY be skipped, and wave 26 records how to do it honestly.** Four conditions, all four required:
nothing is built; the predecessor gate already answered what a re-gate asks; **a re-gate does not reach the
wave's actual risk**; and no clause in the verdict tree reads a gate landing. Three triggers reverse it and make
a full gate mandatory: any C# changes, a hash or BuildId mismatch, or the arm's own instrument proving
non-deterministic. **The replacement control must be SHOWN TO REFUSE on a real different input** — wave 26's
`Q26-V0` demonstrated its FAIL end on B14's published binary, so its accepting result is not vacuous. See
`docs/synthetic-af-bank-followups-wave26-design.md` §1.4 and §5.

## 6. DISCIPLINE — the expensive lessons, in one place

- **Apply pre-registered rules as written. An unsatisfiable clause is a FINDING**, not something to repair and
  re-score. **RULE F14 (§3), RULE S16 (§3a) and RULE D20 (§3b) are permanently fenced — do not harvest them.**
  Wave 21 shows the honest alternative: RULE B21 answered D20's question on a *new* population.
- **A gate never shown to PASS is not a gate**, and a clause about the wave's own deliverable must have its FAIL
  end measured on real artifacts. Check **both** branches are reachable, and assert the mutation happened.
- **[F68](followups.md) has five parts**: population (artifact **and** field), statistic, aggregation, the
  empty-set answer, and **which copy of the field is read and whether it is the live one**.
- **A driver and its scorer must not both rebuild a path** ([F74](followups.md)) — the handoff artifact carries
  the path and the reader parses it out. No `os.walk` fallback.
- **An interlock whose writer is a human is a note, not an interlock** ([F75](followups.md)). When a marker seems
  to have no writer, grep the **plan** too, not just `*.sh`/`*.py`.
- **INSTRUMENT FIRST when the mechanism is not observable** ([F76](followups.md)). That entry was diagnosed wrong
  **five times** from reading source; twenty lines of `Logger.Info` settled it, and each wrong fix disabled the
  mechanism the next step needed. **A fix that cannot report whether it engaged is not finished.** In every round
  the finding was the log's *silence* — a missing line, not a wrong number.
- **A single red CI run that names infrastructure** ("machine slowness") **is not evidence your new test is at
  fault.** Check whether a later commit with the same test passed before weakening the suite.
- **`strings` is two instruments**: type/member names in `#Strings` (UTF-8), string literals in `#US` (UTF-16,
  `strings -el`). Read the `DetectorVersion` **field**, never `grep AtrousWaveletFast`.
- **A WRONG NUMBER CAN HAVE MORE THAN ONE CAUSE, and fixing one does not validate the instrument**
  ([F79](followups.md), wave 24). Wave 23's `0 of 8` was a `grep`/σ defect; wave 24's *identical* `0 of 8`, on a
  binary carrying the σ fix, is a **line-vs-block counting bug** the σ had been masking. **The only reason it was
  caught is that a scorer and a driver compute the same clause by different routes and were compared** — keep
  that comparison, and never assume the two compute the same statistic.
- **Instruments derived by `sed` keep their predecessor's prose** ([F80](followups.md), waves 24 + 25). Eight
  drifts in five instruments in wave 24 and 21 more in wave 25, **every instrument passing its own `--self-test`
  with the drift present**, because a self-test checks behaviour and the drift is in the labels. **Any ordinal or
  count in a derived instrument's output must be COMPUTED, never typed.**
- **KNOW WHAT YOUR SCORE IS MADE OF BEFORE YOU COMPARE TWO OF THEM** ([F83](followups.md), wave 26). `J` on the
  synthetic bank is composed **without** the `Wl · sLabel` term, because the runs are unlabelled and `JRun` takes
  the `else` branch at `OptimizationObjective.cs:484-490`. **`SStars` counts stars, not correct stars**, and the
  one false-positive term that exists ships at strength 0. Every `BestJ` / `BaselineJ` / `dJ` in this series is
  denominated in a score with no false-positive cost — which was **five minutes of reading source**, and it
  explains a published finding (`RULE P23`'s 22-of-24 driven pins) that had no mechanism for three waves.
  *Read the objective's composition before pricing anything against it.*
- **A CONTROL THAT EXITS ITS CALLER WHILE PRINTING PASS** ([F80](followups.md), wave 26). A **sourced** bash file
  sees the **parent's** `$1`, so a trailing `--self-test` dispatcher fired during `source`, ran the layout's own
  self-test, and `exit`ed the arm driver inside its own `source` line — printing a clean pass and running none of
  the arm's checks. Guard every dispatcher with `[ "${BASH_SOURCE[0]}" = "$0" ]`. **This is wave 24's
  `[ now > "23:59" ]` in a new costume, and it was caught the same way: by reading the OUTPUT of a self-test
  rather than its exit code.** Two more from the same wave: a guard that greps its own file **reported the defect
  it is made of**, and `xargs` split `/mnt/d/Autofocus Bank` on its space and produced **20 fingerprints instead
  of 42 while every command exited 0** — caught only by the population assertion. **Assert the population size
  of every control, including the ones that obviously worked.**
- **AN IDENTIFIER BOUNDARY IS NOT A WORD BOUNDARY** ([F80](followups.md), waves 25 + 26). `_` is a word character, so
  `\b` cannot see it — and **every marker, function and path in this series is underscore-joined**. Wave 25's own
  pre-flight had this bug **twice**, on opposite affixes: it missed `G24_START` (found only because another clause
  happened to fire on the same line) and then `w24_layout_self_test` (passing `V-CLEAN` a file that could not
  run). **A checker that finds a defect by accident has not checked for it.** And check the predecessor's
  **interface** survived, not just its spelling: no renaming scheme can repoint a call site whose function no
  longer exists under any name. **Wave 26 found the THIRD shape and it was in the fix itself**: the pattern's
  uppercase arm **enumerated the rule letters `[GBRD]`** and gave `\bW%d\b` no suffix arm, so it missed
  **`W25_BEFORE_READY`** — wave 25's own interlock marker, the exact load-bearing class — and four score files.
  **An alternation that enumerates the values a field has TAKEN is a hardcoded list wearing a regex**, and it
  belongs under the same rule as "any ordinal or count must be COMPUTED, never typed". Match by **shape**.
- **EVERY SCORER THAT ACCEPTS `--out` MUST REFUSE WITHOUT ONE.** Wave 25 lost its central scorer's output file
  twice to a `tee` that lived in the plan and was never executed, and had to re-derive every number from the
  arms' own reports. Wave 26's `score_q26_w26.py` **refuses to run without `--out`**, so the artifact is written
  by the code that computes the verdict rather than by a redirect a controller has to remember — the same move
  [F75](followups.md) requires for interlocks, applied to a scorer's own evidence. *Do not pipe and hope.*
- **A NEGATIVE ASSERTION ABOUT THE PREVIOUS WAVE IS ITSELF PREVIOUS-WAVE DRIFT** ([F80](followups.md), wave 25).
  A guard asserting a path does *not* contain `hf_w24` embeds `hf_w24`. Assert **positively** that it contains
  this wave's root — stronger, and it needs no reference to the past at all.
- **Price from the same instrument AND the same scenario** ([F21](followups.md), wave 24). Every wave-24 step
  priced from a measured rate landed within 5 %; every step priced by *deriving* from a never-run scenario came
  in at ~0.4×. **When a scenario has never been run, give a BAND and say it is a derivation.** A conservative
  derived price is the cheaper error — wave 24's over-reserve is what bought both of its dropped items.

## 7. WHAT IS OPEN, at the end of the run

**`docs/waves22+-handoff-prompt.md` §1c ("THE STATE OF THE REGISTER") is the authority and supersedes this
table.** Read it before acting on anything here.

**Three items this file previously listed as open are DISCHARGED:** `N25-E` (it ran — P2 moved **1 of 18**
paired S1 cells, wave 25 §16.1), **P23's flat-direction arm** (wave 26 ran a strictly stronger instrument —
[F83](followups.md)), and **the owner's final results table** (delivered at `7e98915`, all 20 datasets).

| item | note |
|---|---|
| **`PREDICTION P-D08` at `n = 3`** | **~2 m, two `golden eval` cells, NO BINARY — the only open item that needs no build, and the highest decision-value-per-minute item left.** `seedA1/D01_ultrawide_40mm` (gate 0.2234) and `seedA1/D16_esprit550_ha3` (gate 0.1969) are at-floor landings with `StarClippingMultiplier` **pinned at its own 0.25 floor**, never scored for precision at `--sensitivity 10.0`. Settles whether [F83](followups.md)'s lead is real. **Score against `*.truth.json` as well as the golden** — [F31](followups.md) |
| **the [F83](followups.md) DECISION** | **`J` has NO precision term on an unlabelled run** (`OptimizationObjective.cs:484-490`), so a sensitivity pin buys stars for free. Three options — raise `DefaultSensitivityLower`, add a precision term, or label the bank — each with its evidence and price in the entry. **What is missing is an owner's call, not an arm.** Any of (a)/(b) owes a fresh **42 m** baseline |
| **the wave-26 result** | **DONE.** `V-CLEAN` / **`Q-PIN-COSTED`** / `L26` could-not-look. **Nothing shipped, nothing built, the gate deliberately skipped** under four pre-registered conditions with a replacement control shown to REFUSE on B14. `docs/synthetic-af-bank-followups-wave26-results.md` |
| **the wave-25 result** | **DONE and shipped.** `V-CLEAN` / `G-PASS` / **`W-UNEXERCISED`** / **`N-PRESERVED`** / **`M-CORRECTED`**. P4 = [F81](followups.md) (product), P5 closed [F34](followups.md). `docs/synthetic-af-bank-followups-wave25-results.md` |
| **[F82](followups.md)** | **The floor is not sticky across rounds.** `D01`'s half-width fell 12.0 → 9.0 because `SearchSpan` is measured over the **fitted** points and a widened sweep on a star-poor field loses its outer frames. **The fix choice is now PRE-REGISTERED — (1), the monotone floor** (wave 26 design §14) — with the condition that would reverse it. ~45 m code + ~10 m 3-cell re-run + a **mandatory** gate. **Do NOT re-run a full paired arm** — `W-UNEXERCISED` means 13 blind cells move by exactly zero |
| **the `A4` truth-model gap** | ~20 m. The harness computes the cap boundary from the **requested** sweep, the product from the **fitted** span; they disagree on `D01` r1 by 2×. **Fix the assertion, not the product**, and never in an arm that also carries a product change |
| **the S1 re-measurement** | **DISCHARGED AS AN ARM** by wave 25 and **RETIRED AS A NUMBER** by `N25-E`. `13 of 17` is formally uncomparable, with evidence. **No goal-2 accuracy rate exists**, and none may be quoted |
| **the wide end of [F25](followups.md)** | **UNMEASURED, not untriggered.** No wave-25 or wave-26 clause touches it; `D05`/S2 is published and unusable blind |
| **`lumos`'s zero-star frame** | ~10 m **+ a diagnosis**. Wave 26's `RULE L26` returned **could-not-look**: `af-fit` produced no `af_fit_points.csv`, so the driver wrote 0 rows and refused rather than branching to `L-FRAME` off an absent measurement. **The next attempt owes an explanation of the missing CSV before it re-runs the same command** |
| **`Panos`'s σ fit** | ~5 m of `af-fit`. **The recorded "degenerate σ fit" belongs to `af-fit`, NOT `optimize`** — wave 24's `optimize` probe returned exit=0, hard-floor PASS, `bestJ=0.935582`, which is `L-NOT-COMPARABLE` and **not** a refutation. This is the only half still folklore |
| **F77** | the silent deploy failure above — make the copy fail loudly or warn by name. ~20 m |
| **`SystemParameters.WorkArea`** | reports the **primary** monitor. `ClampWindowToWorkArea`'s Win32 half already resolves per-monitor via `MonitorFromWindow`; the WPF half does not. Wrong rect if a window opens on a secondary display |
| **F72 / A5, A7, A8** | A5/A7 unblocked now the wizard footer renders. A8 needs a run whose recommended step is **capped** |
| **F70(b′)** | **REJECTED by the owner — do not re-propose.** The preset system owns the whole configuration |
| **F70(a)** | the optimizer seed literal `NoiseReductionRadius = 3` at `HocusFocusStarDetection.cs:428` — **not** overwritten, and it **does** take effect. Owner's call, not decidable by measurement |
| **F73's code axis** | wave 19's arm was a NULL arm (C#-identical trees). The real question — can an inert-believed code change move a landing at fixed `J`? — was never run. ~53 m |
| **F45(b)** | behind the S16 fence; `N*` unreachable in `AutoFocusEngine` (`:901` drops the count into `MeasureAndError`, a NuGet struct of two doubles). ~3–4 h |

## 8. REPORTING

Two or three lines per cron firing: **what is running, what you just started, what is next.** At the stop, a
final summary naming what shipped, what was measured, what was refuted, and what remains — with the suite count
verified by COUNT and the CI conclusion read from the log.
