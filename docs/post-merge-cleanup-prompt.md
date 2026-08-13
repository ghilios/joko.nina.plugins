# Clearing the last of the synthetic-AF-bank work — a SHIP run, not a wave

**Run this after PR #196 merges.** Everything you need is on disk; nothing depends on a prior conversation.
Read this file, then `docs/synthetic-af-bank-followups-wave30-results.md` §10 (the checkable ledger), then the
register entries this file names. Those are the authority.

---

## 0. WHAT THIS IS, AND WHAT IT IS NOT

**The wave series is STOPPED.** Waves 27–30 closed the last product question the synthetic bank can answer with
another arm, and wave 30 §12 recommended stopping — a judgement, not the charter's trigger, and the reasoning is
there.

**So: no wave apparatus.** No `RULE X`, no verdict trees, no per-clause populations, no derivation pre-flight,
no BEFORE/AFTER fingerprints, no blindness ledger. Those exist to take a **verdict** from a **measurement**, and
**not one item below has a verdict to take.** Every item is a ship or a rescore. Building the apparatus around
them would cost more than the items and would produce the fifth consecutive wave whose findings are mostly about
its own instruments — which is exactly what stopping was meant to end.

**What you DO keep**, because it is cheap and has repeatedly paid:

- **Verify a claim before acting on it.** Every item below carries a **search command**; run it first. This run's
  predecessor lost its first hour to a backlog item that had shipped ten days earlier ([F85](followups.md)), and
  a later wave listed a debt paid seven hours before it opened ([F106](followups.md)).
- **Read logs, not exit codes.** Confirm a `*_START` line **and** a live `TestApp` before believing an arm runs.
  Piping to `tail` masks the exit code.
- **Verify the suite by COUNT out of the log, never by the tick** ([F37](followups.md)).
- **Never `git checkout --` to undo a mutation.** Byte backup, restore in a `finally`, **`touch` after every
  restore** or MSBuild skips the recompile and the next run silently re-measures the mutant ([F89](followups.md)).

---

## 1. YOUR FIRST FOUR ACTIONS

```bash
date -u +%FT%H:%M:%SZ                      # T0. State the absolute stop in your first reply.
gh pr view 196 --json state --jq .state    # must be MERGED. If not, STOP and say so.
git checkout develop && git pull
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```

**The stop is `T0 + <the duration the invoking message names>`; if it names none, use 6 hours.** Compute the
absolute time and state it. **Never copy a literal stop time out of a prompt** — a run boundary written as a
timestamp goes stale on the shelf.

**Re-establish the suite baseline from that run, by COUNT.** It was **4039** on the wave-27 branch at merge time
(`develop @ ec06bec` was 4015 + this run's 24). **Do not assume 4039 survives the merge** — other PRs land. The
number you measure is the baseline; every count below is relative to it.

**Branch: `ghilios/af-bank-cleanup`, ONE branch and ONE PR for everything below.** The owner asked for one PR and
was right; a previous run split the work three ways and had to consolidate. Rebase onto `develop` and
force-push (`--force-with-lease`) at each item boundary, and re-check whether `develop` moved rather than
waiting to be asked.

---

## 2. THE WORK, IN THIS ORDER — the order is load-bearing twice

### Item 1 — `V-0`, the validity gate that can void item 2 (~10 m, no code)

**Run this before writing any F82 code.** [F110](followups.md): `--step-detect-bound` is **opt-in** in the
harness (`TestApp/SynthValidateRunner.cs:783-793`) and `D01`'s published rounds carry `maxUsefulHalfSpan: NaN`,
while the **product supplies detectability unconditionally**
(`StarDetectionOptimizerWizardVM.cs:4073-4079`) and applies it **after** the floor. **So every number in F82 was
measured in a configuration the product does not run.**

- **Check:** `grep -c 'maxUsefulHalfSpan.*NaN' /mnt/d/hf_w25/after/D01_ultrawide_40mm__S1/synth_validate_report.json`
- **Do:** re-run `D01`/S1 **with** `--step-detect-bound`, on `/mnt/d/hf_w25/exe` (B15 — no build).
- **The question:** does the detect bound already clamp `maxHalfWidth` **below** the requested-span floor?
- **If YES, item 2 is VOID.** Say so, record it, and skip to item 3. That is a real outcome, not a failure.
- **If NO,** item 2 proceeds and you have its precondition in writing.

### Item 2 — implement F82's decided fix, candidate (3′) (2 h 25 m – 3 h 10 m incl. the gate)

`docs/f82-fix-choice-decision.md` is the decision and the argument. **Do not re-litigate the choice**; it was
made with `W29-S`/`W29-R` in hand and it retired wave 26's fix (1) as **refuted** — at `D01`'s own numbers
fix (1) gives `12.0 / 3.5 → step 3`, the step that stalled, changing **zero** recommended steps anywhere.

- **Check it is still owed:** `grep -n 'PointsPerSide' Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs`
- **The verification is pre-registered in §7 of the decision document. Use it; do not invent one.** In
  particular: **a fix must not be scored on the cells that motivated it** — `D01`/`D02`/`D03` are burned. The
  legitimate bar is `V-2`: the 15 non-burned cells' 36 rounds **36 of 36 bit-identical**, and `stepBehavioral`
  **18 of 18** identical across arms.
- **`V-1` has a predicted answer, computed before any code exists:** `halfWidth == 18.0`, step **3 → 5**. A
  rule with a predicted answer is one that can be wrong, which is the only kind worth running.
- **The 42 m gate is CERTAINLY owed** — `Q27-V1`'s FAIL end was *run* against a diff whose two files are
  exactly this fix's (`/mnt/d/hf_w27/q27_v1.txt`). Predicted `G-PASS`, 8 of 8 bit-identical. Score with
  `python3 /mnt/d/hf_w12/score_w12.py <root>/gate --rule G<N>`.

### Item 3 — the `ACCEPTED-elsewhere` residual (~15 m, no code)

**This gates the bottom half of item 4, so it comes first.** Wave 30 measured that **23–25 % of the distortion
gate's acceptances were `ACCEPTED-elsewhere`** — accepted, but matched to a *different* golden star than the one
they were released from. That is not a false positive (precision is **1.000, FP = 0** at `MaxDistortion` 0.5 /
0.3 / 0.2 — see `/mnt/d/hf_w30/row7_precision_rescore.txt`), but it means the release→acceptance attribution is
fuzzier than a clean mapping, and **nobody has resolved it.**

- **Check:** `grep -n 'ACCEPTED-elsewhere' /mnt/d/hf_w30/g/out/M{1,3,4}/attempt01/golden_eval.txt`
- **The question:** are those acceptances real stars matched to a neighbour (harmless, a matching artifact), or
  the detector merging/mis-centring under a relaxed distortion gate (not harmless)?
- The per-star detail is already on disk in `false_negatives_f<focuser>.csv` and `detected_f<focuser>.csv`.
  **Zero TestApp minutes if you use them.**

### Item 4 — bound and rename the `MaxDistortion` axis (~20 m + 42 m gate)

[F98](followups.md): it is a **MINIMUM fill ratio despite its name** — `StarDetector.cs:1804` rejects when
`fillRatio < effectiveMaxDistortion`, and a perfect disk's ceiling is ~π/4 ≈ 0.79. **So the top ~21 % of a
searchable axis (`OptimizerVariable.cs`, range 0.1–1.0) returns zero detections for round stars**, and wave 29
demonstrated the collapse by "relaxing" it to 0.9 and killing two arm cells before they ran.

- **Check:** `grep -n 'fillRatio' Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs`
- **Top bound: supported now.** Bound the axis at ~π/4 and rename it so the name states the direction.
- **Bottom of the axis: only after item 3 resolves.** Do not touch it before.
- **Renaming a persisted knob is a migration.** If it is user-visible it owes a control in
  `Resources/OptionsDataTemplates.xaml` per `CLAUDE.md` — check, and say which applies.

### Item 5 — the `A4` truth-model gap (20 m + 42 m gate) — only if items 1–4 leave room

The harness computes the cap boundary from the **requested** sweep and the product from the **fitted** span;
they disagree on `D01` r1 by 2×, and `A4` compares against `WasCapped` alone so it now flags floored rounds.
**Fix the assertion, not the product**, and keep it out of any commit carrying a product change.
[F96](followups.md) makes it more urgent, not less: `stepBehavioral` is `StepSizeRecommender.Recommend`
iterated to a **fixed point**, so the harness scores the recommender against a fixed point of itself.

---

## 3. EXPLICITLY NOT IN SCOPE

- **Rendering datasets at 1.4–19.4 ″/px** (≥ 1 h). It is the **only** route to a blind wide-field dataset and
  has been open twelve waves — but it is new data, not cleanup, and it deserves its own decision from the owner.
- **Anything on the `Sensitivity` axis.** [F84](followups.md) killed floors there — the search drives
  `StarClippingMultiplier` down alongside it ([F6](followups.md)), and [F23](followups.md) measured a hard floor
  as worse than doing nothing.
- **A `NoiseClippingMultiplier` default change.** [F93](followups.md) says plainly that `N-RECOVERS` does not
  license it: the optimizer *chose* the landed values under an objective that charges nothing for a missed
  detection ([F83](followups.md)). It owes its own decision and a fresh baseline.
- **The four withdrawn ledger rows** (wave 30 §10 rows 3–6). They were withdrawn **with reasons** after being
  carried between two and four waves. **Do not re-list them.** One of them — wave 29's BEFORE fingerprint — is
  unrecoverable by construction: a BEFORE taken now is an AFTER.

---

## 4. HARD CONSTRAINTS

- **Never push `develop`.** Commit with the privacy email as **author and committer**:
  `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com"` and
  `--author="George Hilios <322725+ghilios@users.noreply.github.com>"`.
- **Every new test must be shown RED against a named mutant**, and you must *show* the red output, not assert
  it. Byte backup, `finally` restore, **`touch` after restore**, sha256-verify, abort on damage.
- **One `TestApp.exe` at a time; one `dotnet test` at a time.** Kill stragglers first.
- **`dotnet test <sln>` does NOT build `TestApp`** — build it separately or a compile break never surfaces.
- **Hash the dlls, never the apphost** — `TestApp.exe` is byte-identical across seven distinct binaries
  ([F66](followups.md)). **`git diff --name-only <tree>` omits UNTRACKED files** — union it with
  `git ls-files --others --exclude-standard` ([F88](followups.md)).
- **Pass scorers WSL paths** (`/mnt/d/...`); a Windows path yields UNEVALUATED and looks like a failed arm.
- **The gate is owed by anything touching plugin code.** Decide it the way `Q27-V1` does — intersect the C#
  diff (unioned with untracked) against the reachable set, **reading the BEFORE binary's tree hash out of its
  own provenance file**, never assuming the previous HEAD. Empty intersection ⇒ discharged, and **demonstrate
  the FAIL end** on a diff known to reach it.

---

## 5. STOP CONDITIONS, AND THE HONEST ENDING

**Stop at the computed time.** Do not start an item after it; finish what is in flight, push, summarise.

**Stop EARLY and say so if** item 1 voids item 2 and items 3–5 are done, or if the gate fails and cannot be
explained. **"Everything left is done, and the rest is out of scope" is the expected ending, not a failure.**

**This run should END the synthetic-AF-bank thread.** When it closes, the honest position is: the bank is
measured out, the remaining product questions need **new data**, and the next decision is the owner's — render
the 1.4–19.4 ″/px datasets, or stop.

---

## 6. REPORTING

Report what you **ran**, what it **returned**, and what you **changed** — with the suite verified by COUNT and
the CI conclusion read out of the log. **Correct your own errors in the record, in place, where the owner can
see them.** The previous run did this six times and the record is better for it.
