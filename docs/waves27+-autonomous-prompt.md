# Waves 27+ — autonomous run

> # THIS RUN IS COMPLETE, AND THE WAVE SERIES IS RECOMMENDED TO STOP. READ THIS BOX BEFORE ANYTHING BELOW.
>
> **Executed 2026-08-13: waves 27, 28, 29 and 30.** All four are on **ONE** branch,
> `ghilios/synthetic-af-bank-followups-wave27`, and **ONE** PR, **#196** — the owner instructed one PR, and
> earlier stacked PRs (#197, #199) were consolidated and closed. Results per wave are in
> `docs/synthetic-af-bank-followups-wave2<7,8,9>-results.md` and `…wave30-results.md`.
>
> **The recommendation to stop is a JUDGEMENT, not the charter's trigger.** §3's formal condition — two
> consecutive waves with no register-worthy finding — is **not met**; waves 29 and 30 both produced product
> findings. The judgement rests on wave 30 §12: **the synthetic bank has no unanswered product question that
> another arm can address**, which wave 30's own design predicted in advance and its measurement confirmed.
> Everything left is a **ship or a five-minute rescore**, and neither has a verdict to take.
>
> **§7 below is STALE and its opening item was FALSE.** It directed the run at *"re-measure precision against
> `*.truth.json`"*, which had **already shipped on 2026-08-13** (`aaf26e8`); the register contradicted itself
> and the owner's results table carried a **false banner**, now withdrawn. That correction is wave 27's
> headline. **Do not open on §7.**
>
> **What is actually left, none of it a wave** — full ledger in wave 30 results §10, with a search command per
> row so it can be checked rather than believed:
>
> | item | price | goal |
> |---|---|---|
> | Implement **F82's decided fix, candidate (3′)** — `docs/f82-fix-choice-decision.md` | 2 h 25 m – 3 h 10 m incl. a **42 m gate that is certainly owed** | 2 |
> | **First run its validity gate `V-0`** ([F110](followups.md)) — F82's evidence was taken with the detectability bound OFF | ~10 m, and it can **void** the decision | 2 |
> | Bound the **`MaxDistortion`** axis at ~π/4 and rename it — it is a **minimum** fill ratio ([F98](followups.md)) | ~20 m + 42 m gate | 3 |
> | Render new datasets at **1.4–19.4 ″/px** | ≥ 1 h | 1, 3 |
>
> **The last is the only route to a new blind population, and it has been open twelve waves.** Without it the
> honest position is that this bank is measured out.
>
> **Suite baseline is 3997**, verified by COUNT in CI. Everything below 3997 in this file is stale.

**Read this file, then `docs/waves22+-handoff-prompt.md`, then `docs/followups.md`. Those three are the
authority; this file only sets the run's boundaries.** Everything you need is on disk. Nothing depends on a
prior conversation — this prompt is written to survive a `/clear`.

---

## 0. YOUR FIRST TWO ACTIONS, IN THIS ORDER, BEFORE ANY MEASUREMENT

### 0.1 Anchor the clock to NOW, not to a timestamp written in this file

```bash
date -u +%FT%H:%M:%SZ            # this is T0
```

**The stop is `T0 + 12 hours`** unless the owner's invoking message names a different duration — if it does,
that wins, and you say so. **Compute the absolute stop time from `T0` and state it in your first reply.**

> **Why this is written this way.** The previous charter hard-coded `Stop at 15:00Z`. By the time the owner
> invoked it, `15:00Z` had already passed, and the controller had to re-anchor the whole run by judgement in its
> first minute. **A run boundary expressed as an absolute timestamp goes stale on the shelf.** Never copy a
> literal stop time into a successor prompt.

### 0.2 Create the recurring status cron YOURSELF, anchored to T0

**Do not assume a cron exists — the previous run's was deleted at its close.** Create one now with `CronCreate`:

- **schedule:** every 30 minutes on an **off-minute** (e.g. `13,43 * * * *`), never `0,30`.
- **prompt:** the §2 status sweep verbatim, plus the reporting rule in §8, plus **the absolute stop time you
  computed in 0.1** and the current wave's plan in two lines.
- **recurring:** true. Tell the owner it is session-only and auto-expires after 7 days.
- **At the close of the run, delete it** (`CronDelete`) so a finished session is not woken every half hour.

---

## 1. STANDING AUTHORISATION

1. **Proceed without asking.** A blocker is a result: record what was tried, what failed, what unblocking would
   cost, and move on.
2. **Waves MAY ship user-visible product changes** when the wave's own ship rule, fixed and committed BEFORE the
   data, is satisfied. If the rule is not met, the change does not ship and is recorded as a costed
   recommendation. **Ship unconditionally where the ship is not what the rule decides** — say which applies.
3. **~6 hours of compute per wave.** A wave that wants more must cut scope or split, and say which.
4. **Never push `develop`.** Specs go in `docs/`, plans in `plans/`. Commit with the privacy email (§4).

## 1a. THE VESSEL — decide it in writing before any measurement

**PR #195 (`ghilios/synthetic-af-bank-followups-wave23`) carries waves 23–26 and was OPEN at the end of that
run.** Check its state first:

```bash
gh pr view 195 --json state,mergeable --jq '"\(.state) \(.mergeable)"'
```

- **OPEN** → decide whether wave 27 continues on it or opens a fresh branch off it. It already carries four
  waves; do not drift into a fifth section by default.
- **MERGED** → `git checkout develop && git pull && git checkout -b ghilios/synthetic-af-bank-followups-wave27`.

**Record the decision in one line in the wave's design document.**

## 2. THE STATUS SWEEP — run at every cron firing, before anything else

```bash
date -u +%H:%M:%SZ
tasklist.exe | grep -ciE "TestApp|NINA"
cd /home/ghilios/src/hocus-focus && git status --short && git log --oneline -1
gh run list --branch $(git branch --show-current) --limit 2 \
  --json status,conclusion,headSha --jq '.[]|"\(.headSha[0:7]) \(.status) \(.conclusion // "-")"'
```

**READ LOGS, NOT EXIT CODES.** A background job reporting `exit 0` has repeatedly meant a driver aborted in one
second. Confirm a `*_START` line **and** a live `TestApp` before believing an arm is running. **A `*_START` line
alone is not evidence** — wave 24's driver printed one and had skipped all 22 cells.

**If nothing is running and no agent is live, start the next step immediately. Never report "waiting".**

## 3. STOP CONDITIONS

**Stop at the time computed in §0.1.** Do not start a new wave or a new arm after it. Finish the step in flight,
push, write the final summary, and delete the cron.

**Stop EARLY and say so if** the gate fails and cannot be explained, or **two consecutive waves produce no
finding worth a register entry**. **"There is nothing left worth a wave" is an acceptable and welcome answer** —
say it plainly rather than manufacturing work. The previous run ended by saying exactly that, and was right to.

## 4. HARD CONSTRAINTS

- **Only one `TestApp.exe` at a time.** Never run NINA during a pinned arm.
- **Only one `dotnet test` at a time.** Kill stragglers first:
  `powershell.exe -NoProfile -Command "Get-Process testhost,vstest.console -EA SilentlyContinue | %{ \$_.Kill() }"`
- **Never rebuild an arm's directory mid-wave.** Plan every binary up front. Pass scorers **WSL paths**
  (`/mnt/d/...`); a Windows path yields UNEVALUATED and looks like a failed arm.
- **Suite baseline: 3973.** Verify by **COUNT** out of the log, never by the tick ([F37](followups.md)).
  `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo` (~4 min, no
  `dotnet` in WSL — use Windows `dotnet.exe` via interop).
- **`dotnet test <sln>` does NOT build `TestApp`.** Build it separately or a compile break never surfaces.
- **Check the deployed binary before quoting any UI result.** The csproj PostBuild copy now warns by name
  (`HF0001`/`HF0002`, [F77](followups.md)), but **the DLL lock is taken at plugin LOAD, not at NINA start**, so
  an mtime/hash match can still mean a long-lived session is running older code. Restart NINA after a deploy.
- Commit with:
  `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com"` and
  `--author="George Hilios <322725+ghilios@users.noreply.github.com>"`.

## 5. THE WAVE ORDER

pre-registration committed → build the binaries → gate (score it **and** self-test its scorer in **both**
directions) → arms sequentially → analysis agent writes results + register → full suite by COUNT → commit, push,
update the PR → verify CI by COUNT out of the log → next wave's pre-registration.

**You are the controller. You delegate context-heavy work to agents and you run every measurement yourself** — a
subagent's background processes die with its session and its tool timeouts cap at 10 minutes. Spawn a
**pre-registration** agent, then a **code** agent if the wave ships, then an **analysis** agent. **Ask each to
say where your framing is wrong.** In the previous run every single agent overruled the controller on substance
and was right to; two of those corrections changed what the wave measured.

## 6. DISCIPLINE — the expensive lessons, including four this run paid for

**Inherited, still binding:** score a change on something the change cannot move · a control that cannot fail is
not a control · fix the rule before the data and check it is satisfiable · **an unsatisfiable clause is a
FINDING**, not something to repair and re-score · check BOTH branches of every clause are reachable · a clause
about the wave's own deliverable must have its **FAIL end measured on real artifacts** · pin `--settings` **and**
`--profile-id` on every arm · price the payoff in both directions and record estimate AND actual · say what you
did not run, and price it · **[F68](followups.md) has five parts**: population (artifact **and** field),
statistic, aggregation, the empty-set answer, and **which copy of the field is read**.

**Paid for by waves 23–26 — do not re-learn these:**

- **An identifier boundary is not a word boundary.** `\b` cannot see `_`, and every marker, function and path in
  this series is underscore-joined. A drift checker matching `\bG26\b` misses `G26_START`; one matching `_w26`
  misses `w26_gate_log_wsl`. **Both affixes, or the checker passes a file that cannot run.** ([F80](followups.md))
- **A scorer that accepts `--out` must be invoked with it, and should REFUSE without it.** A stdout redirect puts
  the evidence in a job-scoped temp directory that is deleted without ceremony. Wave 25 lost its scoring artifact
  twice; wave 26's scorer refuses, and caught the controller within a minute.
- **`xargs` splits on the space in `/mnt/d/Autofocus Bank`.** A fingerprint written that way recorded **20**
  landings instead of 42 and would have passed its own AFTER check. Use `-print0 | sort -z | xargs -0`, and
  **assert the population size** — that assertion is the only reason it was caught.
- **A `--self-test` dispatcher in a SOURCED file sees the parent's `$1`.** Wave 26's layout self-test fired
  inside its caller's `source` line, terminated it, and printed a clean PASS. **Read the output, not the exit
  code.**
- **Derived instruments keep their predecessor's prose until it stops describing them** ([F80](followups.md)).
  Carry `verify_derivation_w26.py` forward as a **blocking pre-flight**, widen it, and demonstrate the widening.
- **A wrong number can have more than one cause.** Fixing [F79](followups.md)'s `σ` revealed a second defect
  producing the identical `0 of 8`. Only a scorer-versus-driver comparison separated them.

**Traps:** Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"` · keep logs ASCII (guarded now
by `TestAppOutputAsciiTests`) · `TestApp.exe` eats a `while read` loop's stdin unless you redirect `< /dev/null`
· **hash the dlls, never the apphost** — it is byte-identical across eight distinct binaries ([F66](followups.md))
· a landing is **not** a harness settings file ([F71](followups.md)) · `D17_cdk14_oiii5` finds zero stars at short
exposures, `lumos` exits rc=3, `Panos`'s σ-fit claim belongs to `af-fit` and not to `optimize`.

## 7. WHAT IS OPEN — the full priced backlog is `docs/waves22+-handoff-prompt.md` §1c

**Start there.** Its item **1** is the one to open on:

> ~~**Re-measure precision against `*.truth.json`, not `*.golden.json`.** ~1–2 h, no product code, no binary, no
> gate … until a truth-based pass exists, **F23 and [F83](followups.md) are both undecidable** and the owner's
> results table's precision column is a lower bound.~~
>
> **STRUCK by wave 27, 2026-08-13 — IT WAS ALREADY DONE.** `TruthProtection` shipped **2026-08-03** in `aaf26e8`
> and is wired into **both** `GoldenEvalRunner.cs:301-307` and `BankVerifyRunner.cs:464-466`;
> [F31](followups.md) records it as `Done` **1,600 lines above** the [F84](followups.md) entry that asked for it.
> Wave 26's `91.3 %` re-derived false positives from the golden's `stars` list **alone**, consulting neither the
> golden's `unresolved` boxes nor `TruthProtection`: of its 150, **139 were already excluded** by the run that
> produced the owner's table (50 + 89) and `golden eval` reported **`FP = 11`**. **The precision column is not a
> lower bound; it is truth-corrected, and `1.000` is a ceiling.** F23 was re-measured at `afbank-verify/5` and is
> voided on that re-measurement, not undecidable.
>
> **What was actually open, and is what wave 27 ran:** `golden eval` emits **none** of `bank-verify`'s four
> truth disclosures (`scoringMode`/`protectedStars`, `precisionNull`, `truthViolations`, `scoredFraction`), so no
> reader of a `golden_eval.txt` can tell whether protection was applied or what chance alone would score — the
> mechanism that produced the misreading. See `docs/synthetic-af-bank-followups-wave27-design.md`.

Then, in order: the **F83 decision** (an owner's call, not an arm) · **`lumos`** (~10 m **+ a diagnosis of the
missing `af_fit_points.csv`**) · the **`A4` truth-model gap** (~20 m) · **[F82](followups.md)** (fix choice
pre-registered; **mandatory gate**) · **F67's residual** · **[F73](followups.md)'s code axis**.

**And the item nobody has ever worked:** **recall on WIDE fields** — `recall@high` **0.183** on the 40 mm rig
against 0.85–1.00 at long focal length, concentrated on exactly the three datasets whose step recommendations
also stalled. **The largest product gap in the owner's table.** It wants a design before it wants an arm.

**Fenced or rejected — do not re-attempt:** `RULE F14`, `RULE S16`, `RULE D20` are **permanent fences**.
[F70](followups.md)(b′) was rejected by the owner. [F59](followups.md)'s knob proposal is rejected on the merits
(its wave-11 exporter fix is real and shipped — they are different things sharing an entry id).
[F45](followups.md)(b) is behind the S16 fence. **A floor on the Sensitivity axis alone is dead** — the search
drives `StarClippingMultiplier` down alongside it, twice to that axis's own `0.25` floor ([F6](followups.md),
[F84](followups.md)).

## 8. THE OWNER'S THREE GOALS — score every candidate wave against these before pricing it

1. **optimization + autofocus works across a wide range of setups, while making improvements to bridge detected gaps**
2. **accuracy of step-size recommendations**
3. **appropriate exposure adjustments, and avoiding parameters pinned to extreme values (such as sensitivity at 0)**

**An item that scores zero on all three needs a stated reason to run at all.** F73's code axis scores zero and
was demoted for exactly that reason; it is still open and still scores zero.

## 9. REPORTING

**Two or three lines per cron firing: what is running, what you just started, what is next.** At the stop, a
final summary naming what shipped, what was measured, what was refuted, and what remains — with the suite count
verified by COUNT and the CI conclusion read out of the log. **Correct your own errors in the record, in place,
where the owner can see them**; the previous run did this five times and the record is better for it.
