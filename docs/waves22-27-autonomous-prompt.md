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
4. **ONE branch: `ghilios/synthetic-af-bank-followups-wave13`, PR #191.** Never push `develop`.
   **Wave 22's FIRST decision, in writing, before any measurement: is PR #191 still the right vessel, or does it
   merge and a fresh branch open?** It already carries waves 13–21 plus a run of owner-directed UI work. Do not
   drift into a fourteenth section by default.

## 1. STOP CONDITION

**Stop at `15:00Z` on 2026-08-12.** Do not start a new wave or a new arm after that. Finish the step in flight,
push, and write a final summary. A cron fires every 30 minutes; each firing must begin with the status sweep in
§2 and report in two or three lines.

**Stop EARLY and say so if** the gate fails and cannot be explained, or **two consecutive waves produce no
finding worth a register entry**. §2 of the handoff says the register may now be close to dry. **"There is
nothing left worth a wave" is an acceptable and welcome answer** — say it plainly rather than manufacturing work.

## 2. THE STATUS SWEEP — run this at every cron firing, before anything else

```bash
date -u +%H:%M:%SZ
tasklist.exe | grep -ciE "TestApp|NINA"
cd /home/ghilios/src/hocus-focus && git status --short && git log --oneline -1
gh run list --branch ghilios/synthetic-af-bank-followups-wave13 --limit 2 \
  --json status,conclusion,headSha --jq '.[]|"\(.headSha[0:7]) \(.status) \(.conclusion // "-")"'
```

**READ LOGS, NOT EXIT CODES.** A background job reporting `exit 0` has repeatedly meant a driver aborted in one
second. Confirm a `*_START` line **and** a live `TestApp` before believing an arm is running.

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
- **Suite baseline: 3922.** Verify by **COUNT** out of the CI log, never by the tick ([F37](followups.md)).
  Local: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`
  (no `dotnet` in WSL; use Windows `dotnet.exe` via interop, ~4 min).

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

pre-registration committed → build ONE binary → gate (score it **and** self-test its scorer in **both**
directions) → arms sequentially → analysis agent writes results + register → full suite by COUNT → commit, push,
update PR #191 body → verify CI by COUNT out of the log → next wave's pre-registration.

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

## 7. WHAT IS OPEN

Read `docs/waves22+-handoff-prompt.md` §2 for the priced backlog. Summary of what is genuinely open:

| item | note |
|---|---|
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
