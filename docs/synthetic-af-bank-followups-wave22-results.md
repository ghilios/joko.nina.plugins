# Wave 22 — results

**Date:** 2026-08-12 · **Budget:** ~2 h of a nominal ~6 h wave (the run's stop condition is `15:00Z`, and the
window opened at `12:58Z`). **A full wave was not startable.** This is a scoped single-item wave, and that is
recorded here rather than padded out.

---

## 1. The vessel decision (§0.4 of the autonomous prompt), in writing

The prompt required, before any measurement: *is PR #191 still the right vessel, or does it merge and a fresh
branch open?*

**Resolved by owner action, not by me.** The owner directed the merge during this session. **PR #191 merged to
`develop` at `12:50:41Z` as `d2f3400`**, carrying waves 13–21. `develop` was pulled to that commit. So the answer
is the second branch of the question: **PR #191 is closed as a vessel and a fresh branch opens.**

**One branch: `ghilios/replay-region-reports`**, carrying two commits — the owner-requested replay report fix
(which pre-dates the wave) and this wave's F77. They were briefly split onto two branches and the owner
consolidated them: **a second branch buys nothing here and costs another ~4-minute full-suite run**, since the
suite that validated the pair had already been run against a tree containing both. Do **not** re-open a
fourteenth section on the old branch.

## 2. F77 — CLOSED

Full write-up in [`followups.md` §F77](followups.md). The short version, because the method is the point:

**The mechanism was measured, not read.** F76 cost five wrong diagnoses from reading source (§6 of the prompt),
so F77 was probed first. The PostBuild `Exec` is a **ten-command batch**; the plugin-DLL `xcopy` is the **first**
and `Microsoft.CodeAnalysis*.dll` is the **last**. A batch exits with its *last* command's code. A throwaway
probe batch confirmed it directly: a failing `xcopy` sets `errorlevel=4`, a later `echo` runs, the batch exits
**0**. MSBuild never had a failure to report. That is the whole defect.

**The fix** is an `if errorlevel 1 echo <proj> : warning HF0001: …` immediately after the DLL copy (and `HF0002`
after the pdb), in MSBuild's canonical diagnostic format so they count as real warnings. The build still
*succeeds*, so an open NINA does not break `dotnet test` — the common case.

**Both branches were reached on real artifacts** (§6: *a gate never shown to PASS is not a gate*):

| branch | condition | HF0001 | deployed DLL |
|---|---|---|---|
| FAIL | NINA pid 86368 holding the DLL | **fires**, `3 Warning(s)`, build succeeded | SRC `c18987a…` ≠ DST `7dae5fb…`, DST frozen at `12:53:10Z` |
| PASS | NINA closed | silent, `0` HF warnings | hashes **match** `c18987a…`, DST `13:03:27Z` |

The lock was proven rather than assumed: `Get-Process 86368 | Modules` listed the deployed DLL, and
`[IO.File]::Open(dst,'Open','Write','None')` threw *"being used by another process"*.

**The strongest evidence is the silence.** In the FAIL build `HF0002` did **not** fire, and the pdb hashes
matched — NINA locks the DLL but not the pdb. The instrument discriminates **per file**, not per "is NINA
running". A blanket warning would have fired on both and taught nothing. (§6: *in every round the finding was
the log's silence*.)

## 3. The finding worth the register entry

Beyond closing F77, the probe **refined the §4 check the prompt itself prescribes**:

**The lock is acquired at plugin LOAD, not at NINA start.** At `12:53:10Z` the copy **succeeded** although NINA
had been running since `08:58:23Z` — the plugin had not been loaded yet, and the file was locked only afterwards.

Two consequences:

1. *"NINA is running"* is **neither necessary nor sufficient** for the deploy to fail.
2. Worse, and this is the part that survives F77's fix: **the mtime/hash check can PASS while a long-lived
   session still runs the older code in memory.** HF0001 closes the *on-disk* hole. The *in-memory* hole is
   closed only by restarting NINA after a deploy — comparing the DLL's mtime against the NINA log's start time
   (`<yyyyMMdd>-<HHmmss>-<version>.<pid>-*.log`) remains the only proof of which binary a session loaded.

This session is itself an instance: the NINA that was running had started four hours before the DLL it was
tested against was written.

## 4. Gate / suite

Full suite by **COUNT** out of the log: **3933 passed, 0 failed, 0 skipped** — `develop`'s post-merge **3922**
plus the **11** new replay-report tests on the sibling branch. The csproj change cannot move the count and did
not. Verified by count, never by the tick ([F37](followups.md)).

## 5. What remains open, and why no second item was started

**F77 is closed.** The `SystemParameters.WorkArea` item is now registered as **[F78](followups.md)** —
**blocked on hardware, not on understanding.** The fix is ~25 m and its shape is known (a `WorkAreaFor(Window)`
helper via `MonitorFromWindow`, replacing five reads), but this machine has **exactly one display**
(`\\.\DISPLAY1`, `3440x1440`, work `{0,0,3440,1392}`). On one monitor the change is **provably inert and cannot
be exercised in either direction**. Shipping it would add a fix that cannot report whether it engaged — the exact
failure F76 and F77 each cost a run to learn. It is recorded with both a verification plan and a fallback
(unit-test the *selection* only) rather than shipped blind.

Still open otherwise: F72/A5/A7/A8 (need NINA UI sessions); F70(a), an owner's call not decidable by measurement;
F73's code axis (~53 m); F45(b) (~3–4 h, behind the S16 fence). **F70(b′) is rejected by the owner — do not
re-propose.**

**No second wave was started.** The window opened at `12:58Z` against a `15:00Z` stop, and §0.3 prices a wave at
~6 h. None of the remaining items is a disciplined wave in ~1.5 h: F73's code axis needs the full
pre-registration → gate → arms order, the UI items need NINA sessions, and F78 is hardware-blocked. Per §1 this
is said plainly rather than padded: **wave 22 is a scoped one-item wave that produced a real finding, and there
is no second item worth starting inside this window.**
