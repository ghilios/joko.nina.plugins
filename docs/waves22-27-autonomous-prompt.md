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

## 1. STOP CONDITION

**Stop at `12:00Z` on 2026-08-13** (extended by the owner from `15:00Z` on 2026-08-12). **Waves 22-25 have
executed; wave 25 finished its AFTER arm at `05:03:49Z`**, so roughly **seven hours** remain — and the owner's
final results table has first claim on them (§7).
Do not start a new wave or a new arm after that. Finish the step in flight, push, and write a final summary. A
cron fires every 30 minutes; each firing must begin with the status sweep in §2 and report in two or three lines.

**Stop EARLY and say so if** the gate fails and cannot be explained, or **two consecutive waves produce no
finding worth a register entry**. §2 of the handoff says the register may now be close to dry. **"There is
nothing left worth a wave" is an acceptable and welcome answer** — say it plainly rather than manufacturing work.

**The dry-wave stop is NOT armed.** Wave 23 opened [F79](followups.md) and produced the F26 scope correction;
wave 24 opened **[F80](followups.md)**, verified F79, corrected F34's `D01` attribution, and closed F21's
costing; **wave 25 SHIPPED a product fix ([F81](followups.md)), CLOSED [F34](followups.md), opened
[F82](followups.md), and extended F80 with two demonstrated gaps in its own new checker.** Three consecutive
productive waves, so the counter is at zero.

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

**The pre-flight is ~15 m and it is BLOCKING.** Wave 25 built it (`/mnt/d/hf_w25/verify_derivation_w25.py`) and
it returned `V-DRIFT` **21 findings** on the wave's own instruments before any measurement — including a `sed`
**ordering** bug in the plan that left 10 of 15 sibling references pointing at a nonexistent file, and a
surviving `G24_START` that would have hung the controller's own waiter. **Derive the next one from wave 25's, not
wave 24's**, and know its two known limits: **`\b` cannot see `_`** (the pattern must accept an optional
`_suffix` and an optional `_prefix`), and its `PREV` is a **single wave**, so a label that skips a generation is
invisible to it. See [F80](followups.md). Run it **before the gate**, never after.

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
- **AN IDENTIFIER BOUNDARY IS NOT A WORD BOUNDARY** ([F80](followups.md), wave 25). `_` is a word character, so
  `\b` cannot see it — and **every marker, function and path in this series is underscore-joined**. Wave 25's own
  pre-flight had this bug **twice**, on opposite affixes: it missed `G24_START` (found only because another clause
  happened to fire on the same line) and then `w24_layout_self_test` (passing `V-CLEAN` a file that could not
  run). **A checker that finds a defect by accident has not checked for it.** And check the predecessor's
  **interface** survived, not just its spelling: no renaming scheme can repoint a call site whose function no
  longer exists under any name.
- **A NEGATIVE ASSERTION ABOUT THE PREVIOUS WAVE IS ITSELF PREVIOUS-WAVE DRIFT** ([F80](followups.md), wave 25).
  A guard asserting a path does *not* contain `hf_w24` embeds `hf_w24`. Assert **positively** that it contains
  this wave's root — stronger, and it needs no reference to the past at all.
- **Price from the same instrument AND the same scenario** ([F21](followups.md), wave 24). Every wave-24 step
  priced from a measured rate landed within 5 %; every step priced by *deriving* from a never-run scenario came
  in at ~0.4×. **When a scenario has never been run, give a BAND and say it is a derivation.** A conservative
  derived price is the cheaper error — wave 24's over-reserve is what bought both of its dropped items.

## 7. WHAT IS OPEN

Read `docs/waves22+-handoff-prompt.md` §2 for the priced backlog. Summary of what is genuinely open:

| item | note |
|---|---|
| **the owner's FINAL RESULTS TABLE** | **The only deliverable asked for by name**: precision / recall / optimization time / score / sigma / exposure vs optimal / binning vs optimal / `BrightnessSensitivity`, **per dataset**, from a `golden eval` arm. **Schedule it FIRST.** Wave 26 competes with it for the remaining hours |
| **the wave-25 result** | **DONE and shipped.** `V-CLEAN` / `G-PASS` / **`W-UNEXERCISED`** / **`N-PRESERVED`** / **`M-CORRECTED`**. P4 = [F81](followups.md) (product), P5 closed [F34](followups.md). `docs/synthetic-af-bank-followups-wave25-results.md` |
| **`N25-E`** | **~4 m of scoring, 0 TestApp — the cheapest open question in the series.** Dropped on wave 25's clock. Does wave 25's B14 BEFORE arm reproduce wave 23's S1 numbers? Either the series **recovers** the `13 of 17` baseline or retires it **with evidence**. Inputs on disk and fingerprinted (38 of 38 byte-identical) |
| **[F82](followups.md)** | **The floor is not sticky across rounds.** `D01`'s half-width fell 12.0 → 9.0 because `SearchSpan` is measured over the **fitted** points and a widened sweep on a star-poor field loses its outer frames. Two candidate fixes; **pre-register which one before looking**. ~45 m code + ~10 m 3-cell re-run + the gate. **Do NOT re-run a full paired arm** — `W-UNEXERCISED` means 13 blind cells move by exactly zero |
| **the `A4` truth-model gap** | ~20 m. The harness computes the cap boundary from the **requested** sweep, the product from the **fitted** span; they disagree on `D01` r1 by 2×. **Fix the assertion, not the product**, and never in an arm that also carries a product change |
| **P23's flat-direction arm** | ~30 m. **Goal 3 has had TWO consecutive waves of nothing.** The only item that changes that |
| **the S1 re-measurement** | **DISCHARGED AS AN ARM** by wave 25 (paired, 18 of 20 cells per side) — **but not as a published number.** No goal-2 accuracy rate may be quoted until `N25-E` says whether P2 moved S1 |
| **the wide end of [F25](followups.md)** | **UNMEASURED, not untriggered.** No wave-25 clause touches it; `D05`/S2 is published and unusable blind |
| **`lumos`'s zero-star frame** | ~10 m of `af-fit`/`review`. `rc=3` is now reproduced **and explained** (hard-floor FAIL, one frame yields 0 accepted stars); this settles whether it is an unusable run or a gate question. Wave 25 declared it dropped **in advance** |
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
