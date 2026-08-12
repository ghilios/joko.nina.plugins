# Waves 14–21 — autonomous continuation prompt

Run this after `/clear`. It replaces the single-wave handoff pattern: **you are the controller for up to eight
waves, you delegate the context-heavy work to agents, and you do not stop for approval.**

---

## 0. STANDING AUTHORISATION — read this before deciding anything

The user has authorised, in advance:

1. **Waves MAY ship user-visible product-behaviour changes** — defaults, gates, fit rules — **when the wave's own
   ship rule, fixed and committed BEFORE the data, is satisfied.** Document the rule and the evidence. If the
   rule is not met, the change does not ship and is recorded as a costed recommendation.
2. **~6 hours of compute per wave.** That is one large population arm plus a gate plus cheap instruments. A wave
   that wants more must cut scope or split across two waves and say which.
3. **ONE branch, ONE PR, for all of it: `ghilios/synthetic-af-bank-followups-wave13`, PR #191.** Every wave
   commits and pushes there. Never push `develop`. Do not open new PRs.
4. **Proceed without asking.** If you are blocked, record the blocker precisely (what was tried, what failed,
   what it would cost to unblock), move to the next item, and carry it in the handoff. "Blocked" is a result;
   stalling is not. Only use `AskUserQuestion` if proceeding under *any* assumption would destroy data or ship
   something harmful.

**Stop early** if the register runs dry, if the gate fails and cannot be explained, or if two consecutive waves
produce no finding worth a register entry. Say so and stop; eight is a ceiling, not a quota.

---

## 1. FIRST, THE ONE PRODUCT CHANGE THE USER ASKED FOR

**Make `MaxOutlierRejections` default to 0.** Code default only:
`AutoFocusOptions.cs:62`'s `GetValueInt32(nameof(MaxOutlierRejections), 1)` → `0`, and
`InitializeOptions`'s `MaxOutlierRejections = 1` → `0`. **Profiles that explicitly set 1 keep 1** — this changes
the fallback, not other people's stored choices.

**Document it honestly, because it overrides a pre-registration.** Wave 13's RULE M13 said the default changes
only if D1 dominates; D1 did not fire, and the pre-registered outcome was NO CHANGE. This ships anyway, as a
**product-coherence decision by the owner**, and the results doc must say exactly that. The supporting evidence
is real and should be cited, but must not be dressed up as the rule having been met:

- `MOR`=1 won on **0 of 4** synthetic datasets where the rejection fires, scored against the generator's truth;
- the sensor fit prefers 0 on **10 of 10** like-for-like `sChi` runs (F61/wave 13);
- the harness has measured waves 5–12 at 0, so this makes the product match eight waves of measurement;
- at budget 3 the cascade takes `caboose` (R² = 0.99987) to **12.7× worse** σ_focus (F45/wave 13), so the
  direction of risk is entirely on the high side.

### The pre-registered control that makes this cheap to verify

> **RULE G14 must still reproduce the eight gate values BIT-IDENTICALLY after the default changes.** The pinned
> file `D:\hf_w11\pinned_settings_w11.json` sets `MaxOutlierRejections` **explicitly**, so the code default is
> never consulted on a pinned arm. **If the gate moves, the change reached somewhere it should not have** — that
> is a finding, not a nuisance, and it stops the wave.

Also expect to update: the code-default assertions in `HarnessFitInputsTests`, and `DEFAULTS` in
`D:\hf_w13\snapshot_profiles_w13.py` (the `*` marker means "key absent ⇒ this code default"). Both are pinning
the old value on purpose; changing them is part of the change, not a workaround.

**Then say what it does to users**: F63 measured that the optimizer's landing moves on **6 of 8** runs between
these two values, so this changes what the wizard recommends. Every profile with the key absent silently moves.
That belongs in the results doc and in the PR body.

---

## 2. HOW TO RUN A WAVE — the orchestration, and one hard constraint

**THE HARD CONSTRAINT: a subagent's background processes are killed when its session ends, and tool timeouts cap
at 10 minutes. So YOU — the controller — run every measurement arm.** Subagents read, write, design and analyse;
they never launch a multi-hour arm. Also: **only one `TestApp.exe` at a time** (the drivers abort otherwise), so
arms are strictly sequential and no two agents may measure at once.

Per wave, in order:

1. **Spawn a PRE-REGISTRATION agent** (`general-purpose`). Give it: the register, the previous wave's results
   doc, this wave's chosen items. It returns and commits `docs/synthetic-af-bank-followups-wave<N>-design.md`
   and `plans/synthetic-af-bank-followups-wave<N>-plan.md` with **every rule and threshold fixed**, plus the
   exact driver scripts under `D:\hf_w<N>\`. **You commit these before any measurement runs.**
2. **You build the binary** into `D:\hf_w<N>\exe` and record its sha256 + `BuildId`. Never rebuild it mid-wave.
3. **You run the gate** and score it. **A partial reproduction is a failure and stops the wave.**
4. **You run the arms**, sequentially, in background with an `until`-loop wait. Enforce the ~6h ceiling; if an
   arm would blow it, cut the population and record which runs were dropped and why.
5. **Spawn an ANALYSIS agent.** Give it the artifact paths. It runs the scorers, applies the pre-registered
   rules, and writes `docs/synthetic-af-bank-followups-wave<N>-results.md` plus the `docs/followups.md` entries.
   **It must not re-decide a rule** — if a rule turns out unsatisfiable, it says so as a finding.
6. **Spawn a CODE agent** if the wave ships anything, with tests. **Every new test must be shown to fail against
   the pre-change source before it is called a test.**
7. **You run the full suite**, verified by COUNT. **The baseline is the previous wave's count on this branch**
   (wave 13 = 3755), not `develop`'s. Name every added test.
8. **You commit and push to the shared branch**, and append a section to PR #191's body for this wave. Retitle
   the PR when the scope outgrows its title.

---

## 3. THE BACKLOG, in priority order — but choose by the rule below, not by this list alone

**The rule:** pick the item with the largest *decision value per hour* — a question whose answer would change
what ships, what the register believes, or what the next wave does. Prefer a question that can be **refuted**.
Prefer an instrument that is **already printed**. If two items tie, take the one that has been carried longest.

| # | candidate | why now | rough cost |
|---|---|---|---|
| **F63** | extend D5 to the 39-run population | **the only clause that fired in wave 13**, and it is bounded to 8 runs. If 6-of-8 holds at population scale, every landing waves 5–12 published is a non-default artifact | ~6 h (both arms, `--max-evals 250`) — **or redesign it cheaper first** |
| **F45(b)** | a MAD floor tied to each point's own measured σ | now the **only live F45 sub-question**; it is what would let the budget be raised safely, and wave 13 showed the budget is currently the only thing containing the cascade. Instrument is known and costs **4 minutes** for 20 datasets | cheap to measure, expensive to ship (changes every AF fit) |
| **F62 audit** | find every place the register scores a point-set-changing knob on a within-fit statistic | wave 13 proved σ_focus/`J`/R² are anti-informative for that class of change; other conclusions may rest on it | ~1 h, mostly reading |
| **F59** | re-pin the five knobs at real values | the pinned file does not describe the detector; deliberate coordinate-system move, needs a fresh gate baseline | ~1 h + re-derivation |
| **item 2** | the nine UI changes A1–A9 | **run `query session` FIRST.** If `Disc`, it is not attemptable — one line, move on. If `Active`, ~30 min and the procedure in wave 13 §3 is correct | 0 or ~30 m |
| **F57(d)** | the unused `exe_v1wav` bisect | wave 13 recommends **closing** it with three bounds. Close it or run it once (~42 m) and close it either way | ~42 m or 0 |
| F18/F21/F25/F26 | step-size and sweep-width family | untouched for many waves; F21's half-width instability is the load-bearing one | unpriced |

---

## 4. THE STANDING DISCIPLINE — every wave inherits this

- **Score a change on something the change cannot move.** σ_focus, `J` and R² are computed BY the fit under
  test; when what changed is *which points are fitted*, they measure their own denominator (**F62**). The
  synthetic bank's `renderRequest.OptimalFocuserPosition` is a free out-of-sample arbiter.
- **A control that cannot fail is not a control — and "nothing moved" needs one MORE than "something moved".**
  Wave 13's clean 0-of-19 null was equally consistent with a disconnected knob; an 11-minute arm at a larger
  budget converted it into a measured fact and produced the wave's sharpest result.
- **Ask where a knob REACHES, not only whether it matters.** Four clauses said "inert"; the fifth asked whether
  the *recommendation* moves and found 6 of 8.
- **Intervention beats correlation.** Five profiles agreeing named a suspect; one integer in one file convicted
  it and recovered both historical numbers.
- **Fix the rule before the data, on the bar the predecessors faced.** A stricter bar is a moved goalpost.
  **And check the rule is satisfiable**: wave 13's D1 clause (a) needed ≥15 of 20 wins, which 16 ties made
  unreachable in principle. The verdict did not depend on it, but do not inherit that shape — **a bar the tie
  structure forecloses is a badly-formed bar, not a passed test.**
- **The cheapest instrument is the one already printed and ignored.** `af-fit`'s budget table existed for seven
  waves before wave 13 read it, and it answered the wave's primary question in 3 m 46 s.
- **Price the payoff you are buying, in both directions**, and write down the estimate *and* the actual.
- **Say what you did not run, and price it.**
- **Pin `--settings` AND `--profile-id` on every arm** (a deliberate fan-out must argue for itself).

### Traps

- **Pass the scorers WSL paths** (`/mnt/d/...`), never Windows paths. A Windows path yields UNEVALUATED, which
  is correct behaviour and looks exactly like a failed arm.
- **Silent truncation exits 0, two different ways here**: one bank path contains a SPACE
  (`timmer/5 AutoFocus_.../attempt01`), and `TestApp.exe` inherits a `while read` loop's stdin and eats the rest
  of the list unless you redirect `< /dev/null`. **Assert the POPULATION SIZE** —
  `D:\hf_w13\verify_affit_w13.sh` takes an expected count for exactly this.
- **"Could not look" needs its own state at every level, and the guard must come BEFORE any field read.**
- **The fire rate is not one number** — `MaxOutlierRejections` fires on 8/39 through `optimize`, 7/39 through
  `af-fit`, 0/19 through `bank-verify`'s `C0@nc4`, and not on the same runs. Never quote a rate without the
  settings that produced it.
- **Never run NINA during a pinned arm** — `Profile.Load` holds the `.profile` open and the arm throws.
- **`ConcurrencyCheck == exclusive` on a single landing proves nothing.** Read it across the arm.
- **Do not budget fan-out at 4×.** It is 1.33× (F60), and a pinned arm cannot fan out at all.
- Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"`.
- `D17_cdk14_oiii5` finds zero stars at short exposures — a starvation extreme, useless as a measurement.
- `lumos` exits rc=3 reproducibly; `astrodet` the DATASET is frameless (F14); `Panos` has a degenerate σ fit.
- Never rebuild an arm's directory mid-wave (F53(c)).
- On CI, verify the **COUNT**, not the tick (F37). An absent check is more dangerous than a red one.

---

## 5. THE GATE, unchanged across waves

Eight values, now reproduced across **four** binaries and two settings files, bit-identically:

```
toml999 0.995784 | CWhiteFocus 0.996068 | uneven 0.996368 | muggsie 0.997195
mccomiskey 0.976746 | D18 0.999882 | D19 0.999487 | D20 0.999738
```

`--settings D:\hf_w11\pinned_settings_w11.json` (md5 `a67ffc06`) **and**
`--profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382` (astrodet), both named in the script header, sequential,
`--per-run --max-evals 250`. Score with `python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w<N>/gate --rule G<N>`.
Free controls as FIELDS: `BuildId` (must differ), `DetectorVersion`, `ProfileId`, `ConcurrencyCheck`,
`FitInputs`, `BaselineJ`. Wave 5's φ table is invalid on three axes — never quote it.

---

## 6. FINISHING

After the last wave: make sure PR #191's body describes the **whole** series (one section per wave, findings
first), the title reflects the scope, CI is verified by COUNT read out of the log, and
`docs/followups.md` has an entry for every finding. Write a final `docs/waves22+-handoff-prompt.md` if anything
is left, and report to the user: what shipped, what was refuted, what is still open with prices.

**Commit with the privacy email:**
```
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
```

Build: `dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w<N>\exe'`
Tests: `dotnet.exe test "$(wslpath -w <abs>/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`
Banks: `D:\SyntheticAutofocusBank` (20 datasets, each with `renderRequest.OptimalFocuserPosition` = **truth**),
`D:\Autofocus Bank` (19 runs). Wave-13 artifacts and reusable scorers: `D:\hf_w13\`.
