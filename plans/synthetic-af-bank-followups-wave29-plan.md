# Wave 29 — implementation plan

**Spec:** `docs/synthetic-af-bank-followups-wave29-design.md`. Read it first; this file only executes it.
**The controller runs every measurement.** A subagent's background processes die with its session and its tool
timeouts cap at 10 minutes.

**Order is load-bearing, and Step 1 → Step 2 is the whole of [F95](../docs/followups.md)'s remedy.** Every
instrument is written **before** the blocking pre-flight runs, so the pre-flight has a non-empty population.
Wave 28 ran its pre-flight at the moment its plan said to — which was before the six instruments it guards
existed — and certified a root of one file. **A blocking pre-flight must run BEFORE the measurement and AFTER
the instruments.** No previous plan in this series has said so.

**Step 4 (`W29-S`) and Step 5 (`W29-R`) must both be scored before anything is written about F82's fix.**
Nothing in this wave ships (design §9); if that changes for any reason, Step 8's reversal conditions fire and
the wave owes the suite and the 42 m gate.

---

## Step 0 — vessel, ~10 m

```bash
gh pr view 196 --json state,mergeable --jq '"\(.state) \(.mergeable)"'      # expect: OPEN MERGEABLE
cd /home/ghilios/src/hocus-focus
git checkout ghilios/synthetic-af-bank-followups-wave27 && git pull
git checkout -b ghilios/synthetic-af-bank-followups-wave29
mkdir -p /mnt/d/hf_w29
date -u +%FT%H:%M:%SZ > /mnt/d/hf_w29/T0.txt
```

**A fresh branch and a fresh PR. Do not add a third section to #196** — design §0; wave 28 §13 deviation 2
already measured what that costs.

Commit the design and this plan **before any measurement**:

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "W29 pre-registration: F82's pre-registered fix has no carrier in the product, and its reversal condition has never been run"
```

**Gate:** `git log --oneline -1` shows the pre-registration commit before Step 4 runs anything.

---

## Step 1 — write EVERY instrument, ~60 m. Nothing runs yet

Five files under `/mnt/d/hf_w29/`, **LF endings**, all derived from `/mnt/d/hf_w28/`:

| file | derived from | role |
|---|---|---|
| `verify_derivation_w29.py` | `verify_derivation_w28.py` (**not** a `.bak`) | `RULE V29` |
| `fp_w29.sh` | `fp_w28.sh` | `W29-FP`, one `sweep()` called twice |
| `score_w29s.py` | `score_w28s.py` | `RULE W29-S` |
| `score_w29r.py` | new, but reusing `score_w28n.py`'s manifest + refusal skeleton | `RULE W29-R` |
| `w29l_arm.sh` + `score_w29l.py` | `w28n_arm_w28.sh` + `score_w28n.py` | `RULE W29-L` |

Standing requirements for all of them (design §12):

1. **`--out` is mandatory and the scorer REFUSES without it.** A stdout redirect puts the evidence in a
   job-scoped temp directory that is deleted without ceremony.
2. **`--rule` is asserted against the file's own declared rule name** ([F90](../docs/followups.md)) — a hard
   refusal, not a warning. Wave 28's scorers caught the controller within a minute with this.
3. **Every ordinal and count in printed output is COMPUTED**, never typed (`V29-C` will check).
4. **Populations asserted** inside the output file. Paths reach a scorer through a **manifest**, never a
   template ([F74](../docs/followups.md)); both drivers source **one** layout function.
5. **Each scorer re-derives its own outcome-space enumeration in code** and prints
   `regions enumerated: N   uncovered: 0`, against design §10's tables. A mismatch prints `<RULE>-TREE-GAP`,
   enumerates the uncovered regions, prints what design §10 would have said, and **does not repair and
   re-score** ([F94](../docs/followups.md)).
6. **Every clause's FAIL end is reachable and demonstrated on real files**, and each demonstration asserts
   **which clause fired**, not merely that something did.

### 1a. `verify_derivation_w29.py` — the three repairs to carry, and the one thing to MEASURE

1. `sed` the wave id forward with **shape-matched** token patterns, both affixes, `_` treated as a word
   character. **Then grep the result for `V28`/`w28` residue** — wave 28's own controller `sed`
   (`s/\bV27\b/V28/g`) failed to rewrite `V27_OK` because `_` is a word character, and the checker caught its
   author. Expect the same class here with `V28_OK`.
2. **Carry [F87](../docs/followups.md)'s `historical_ranges` repair and prove it did not regress.**
   `in_historical_block()` must compute ranges top-down by **bracket depth**, require the opener line to
   actually open a bracket, and exclude the **definition line of `HISTORICAL_BLOCKS`** by name
   (`is_definition`). The regression test is direct: the checker must report findings on its own file when
   its own file names `w28`, and must **not** report a token inside a declared historical block.
3. **Self-test clause `[6]` carried forward, both ends**: a real historical block is exempt · the file after
   it is not · the definition line opens nothing.
4. **Per-clause fixture pinning `[5b]` carried forward — and the fixtures are MEASURED, not inherited.**
   Wave 28 established that a root pinned for its `-B` findings goes silent one wave later. Run first:

   ```bash
   for R in /mnt/d/hf_w24 /mnt/d/hf_w26 /mnt/d/hf_w27 /mnt/d/hf_w28; do
     python3 /mnt/d/hf_w29/verify_derivation_w29.py "$R" --out "/mnt/d/hf_w29/vdrift_probe_$(basename $R).txt"
   done
   ```

   **Pin `-B` to a root that still refuses and `-C` to a root that still refuses**, from those counts.
   `/mnt/d/hf_w27` was wave 28's `-B` fixture and `PREV` is now 28 — **assume nothing, read the four probe
   files.** Keep every expired root pinned and **asserted silent**, so the expiry is demonstrated rather than
   described. **A clause with no live fixture is a FAIL**, not a pass.

**Nothing in Step 1 is run for its verdict.** The probe runs above are fixture selection and their outputs are
kept.

---

## Step 2 — `RULE V29`, BLOCKING, over the POPULATED root, ~20 m

**This step runs only after Step 1 has written all five instruments.** That ordering is the deliverable.

```bash
ls -la --time-style=full-iso /mnt/d/hf_w29/        # capture: the population must be > 1 file
python3 /mnt/d/hf_w29/verify_derivation_w29.py --self-test --out /mnt/d/hf_w29/vdrift_selftest_w29.txt
python3 /mnt/d/hf_w29/verify_derivation_w29.py /mnt/d/hf_w29 --out /mnt/d/hf_w29/vdrift_w29.txt
```

**Gates — READ THE OUTPUT, NOT THE EXIT CODE:**

- `vdrift_selftest_w29.txt` ends `>>> SELF-TEST PASS`, with clause groups `[1] [2] [3] [4] [5] [5b] [6]` all
  present and `[6]` showing **both** ends;
- each pinned fixture **refuses on its own clause**, count printed;
- the expired root is present and **asserted silent**;
- `vdrift_w29.txt` verdict is `V-CLEAN`, **`files checked` is ≥ 5**, and its banner reads `RULE V29` — not
  `V28`. **A `V-CLEAN` from a checker printing the previous wave's rule name is [F87](../docs/followups.md);
  a `V-CLEAN` over a root of one file is [F95](../docs/followups.md). Both block the wave.**

**Nothing downstream runs until this passes.**

---

## Step 3 — `W29-FP` fingerprint, BEFORE, ~8 m

Read-only for the whole wave: `/mnt/d/hf_w25/{after,before,table18,exe}`, `/mnt/d/hf_w28/nc`,
`/mnt/d/SyntheticAutofocusBank`.

**[F91](../docs/followups.md): ONE `sweep <root> <outfile>` function, called twice.** No second `find`.

```bash
find "$ROOT" -type f -print0 | sort -z | xargs -0 sha256sum
```

- `-print0 | sort -z | xargs -0` — `/mnt/d/Autofocus Bank` contains a space and plain `xargs` splits it.
- **Assert `ROOTPOPULATION` per root and `POPULATION <n>` overall inside the file.** It is the only thing that
  catches a false `VIOLATED`.
- Write the artifact **outside** every tree it sweeps (wave 28 §7.10).
- **Run `fp_w29.sh --self-test` and KEEP ITS OUTPUT** — wave 28's self-test left no artifact (§7.7), and so
  did its verdict line. Both are two minutes and both are captured here: `fp_selftest_w29.txt`.

**Gate:** `fp_BEFORE.txt` exists with its asserted counts printed, and `fp_selftest_w29.txt` contains
`SELFTEST W29FP`.

---

## Step 4 — `RULE W29-S` (source), ~25 m, zero compute

```bash
python3 /mnt/d/hf_w29/score_w29s.py --rule W29-S --out /mnt/d/hf_w29/w29s_score.txt
```

Reads four files at `HEAD` and **records each sha256 in the output** (design §4). Refuses if any is absent.

| clause | what the scorer does |
|---|---|
| `S-a` | Locate `ComputeStepBehavioral` **by declaration, not first textual occurrence** — wave 28 §7.9's real bug, where the first occurrence was a call site and the "body" contained zero of the thing being counted. **An overloaded declaration REFUSES rather than picking a body.** Count `StepSizeRecommender.Recommend(` sites in the body; assert exactly 1; print the count seen and the whole-file count (**2**, at `:792` and `:1262`) so a reader can see the scoping is doing work. Then assert `ComputeStepBehavioral` is the only writer of the value serialized as `terminal.stepBehavioral` |
| `S-b` | Enumerate **every** `Recommend(` site under `Joko.NINA.Plugins.HocusFocus/`, print the count and the list. For each, assert its enclosing round loop reloads previously-captured runs (`LoadRunStampedAsync`) rather than capturing a sweep. Then scan `OptimizedStarDetectionSettings` and `OptimizationSummary` for any member matching `(?i)halfwidth` and assert **zero** |
| `S-c` | Assert `SweepDetectability` declares a member carrying requested focuser positions, and that it is reachable inside `Recommend` |

**String literals are blanked length-preservingly before any pattern is counted**, so a decoy in a comment or
literal cannot inflate a count and offsets stay aligned (wave 28 §7.9).

**Self-test, on real files, both ends per clause:** `S-a`'s zero-site end against `StepSizeRecommender.cs`;
its multi-site end against the whole of `SynthValidateRunner.cs` (2 sites); `S-b`'s carrier end against
`StepSizeRecommendation`, which **does** declare `HalfWidth`; `S-c`'s absent end against the same type.

**Gate:** `w29s_score.txt` ends `>>> RULE W29-S = <verdict>` with `regions enumerated: 8   uncovered: 0`,
and `w29s_selftest.txt` ends `SELFTEST W29S <n> of <n>`.

---

## Step 5 — `RULE W29-R` (the verdict on wave 26's escape clause), ~25 m, zero compute

```bash
python3 /mnt/d/hf_w29/score_w29r.py --rule W29-R \
   --root /mnt/d/hf_w25/after --out /mnt/d/hf_w29/w29r_score.txt
```

**Before any statistic, in this order:**

1. Assert `MaxHalfWidthSampledHalfSpanMultiple == 1.5` **in source at both trees** — B15's tree hash read out
   of `/mnt/d/hf_w25/binary_provenance_w25.txt` (line `tree:`), **never assumed from a previous HEAD** — and
   at `HEAD`. A mismatch is `R-UNSATISFIABLE`; the inversion is only valid if the constant that produced the
   artifacts is the one the scorer divides by.
2. Assert the addressable population: **18** cells, and assert the gap against the 20 datasets is **exactly**
   `D05_tec140_1000mm` and `D19_cygnus_deep_shed` ([F74](../docs/followups.md); wave 24's 40-vs-38).
3. Assert every round read is `wasCapped || wasBandFloored` and `wasDetectBounded == false`. **Any round that
   is not is a could-not-look**: named, counted, and excluded — never silently included.
4. Assert the field copies read are the **per-round** `stepRecommendation` and `bootstrap`, exactly one
   `stepRecommendation` per round index, and that **no value is read from `terminal`**
   ([F68](../docs/followups.md) part 5).

Then, per cell, per transition `r → r+1`:

```
impliedSearchSpan(r) = stepRecommendation.halfWidth / 0.75
requested(r)         = 2 * bootstrap.offsetSteps * bootstrap.stepSize
qualifies(r)         = requested(r+1) > requested(r)  AND  impliedSearchSpan(r+1) < impliedSearchSpan(r)
```

Cell qualifies if **any** transition does. Print `k`, `c` (over 18) **and** `c_blind` (over the 15 excluding
`D01`/`D02`/`D03`), plus the names of every qualifying cell. **The verdict is taken on `c >= 2`, as wave 26
wrote it** — not on a rate this wave invented.

**Reachability of both branches, demonstrated before the population is read:** the scorer's self-test asserts
that the identity reproduces `D01`'s published rounds (implied 16 → 12 against requested 16 → 24) **and** that
`D02`'s published rounds (implied 16 → 24) do **not** qualify — one end each, on real published numbers.

**Gate:** `w29r_score.txt` ends `>>> RULE W29-R = <verdict>` with `regions enumerated: 4   uncovered: 0`, and
the `R-0`-false / `R-1`-true region is printed as asserted-empty.

**Do not write a line of C# on either verdict.** Design §5.4 and §9.

---

## Step 6 — `RULE W29-L` (the one arm), ~55 m, **~25 m compute**

**No build.** Binary `/mnt/d/hf_w25/exe`; assert `TestApp.dll` sha256 `2fb0c8fd…` against
`/mnt/d/hf_w25/binary_provenance_w25.txt` before the first cell.

`w29l_arm.sh` — derived from `w28n_arm_w28.sh`, keeping its structure:

- copies the settings tree per cell **without `-p`** and `touch`es every copied file
  ([F89](../docs/followups.md));
- edits the named fields **only**, and its own self-test proves the edit is surgical (*exactly N files changed
  and they are the settings files*; an unedited tree fingerprints identically twice);
- writes per-cell fingerprints to `/mnt/d/hf_w29/l/fp/`, **outside** every tree they sweep
  (wave 28 §7.10 — a fingerprint written inside the tree it sweeps makes the AFTER population one larger);
- pins `--settings` **and** `--profile-id`;
- gives `TestApp.exe` `< /dev/null`;
- writes `w29l_manifest.tsv` (cell, settings path, out path, exit, seconds) **last**, and the
  `W29L_MANIFEST_READY` marker is written **by the driver, on success only** ([F75](../docs/followups.md)) —
  never by a controller `printf`;
- the scorer reads paths **only** out of the manifest.

Five cells, `NoiseClippingMultiplier = 1.0` throughout (design §6): `L0` baseline · `L1` `MinimumStarBoundingBoxSize`
6 → 3 · `L2` `MaxDistortion` 0.5 → 0.9 · `L3` `Sensitivity` one notch · `L4` all three.

**Budget from the measured rate, not from instinct:** wave 28 measured `D01` at `NC = 1.0` at **179 s** for a
9-frame cell — 3.9× the previous published maximum, because lowering the threshold multiplies the candidate
count. `L1`/`L2`/`L4` open gates *further*, so they may exceed it. **Reserve 25 m and check the manifest's
per-cell seconds against 179 s; report the range in the results.**

```bash
python3 /mnt/d/hf_w29/score_w29l.py --rule W29-L \
   --manifest /mnt/d/hf_w29/w29l_manifest.tsv --out /mnt/d/hf_w29/w29l_score.txt
```

The scorer anchors on the **`FALSE-NEGATIVE ATTRIBUTION, HIGH TIER ONLY`** header and asserts it consumed
exactly one block — the same line appears twice per report and the aggregate copy appears **first**, so a
first-match reader takes the wrong one (wave 28 self-test [3]).

**`L-0` is the control and it can fail:** `L0` must reproduce wave 28's published `D01` `NC = 1.0` high-tier
attribution block **field for field**. If it does not, every downstream number is uninterpretable and the
verdict is `L-UNEVALUATED`.

**Gate:** `w29l_score.txt` ends `>>> RULE W29-L = <verdict>` with `regions enumerated: 8   uncovered: 0`, the
exclusive and joint shares printed, and the `L-1 ∧ L-2` region printed as asserted-empty.

**No branch reads `L3` alone, and nothing this wave writes may cite `L3` as evidence for or against a
sensitivity bound** ([F84](../docs/followups.md), `RULE S16` fence).

---

## Step 7 — [F83](../docs/followups.md)'s `P-D08` at `n = 3`, ~2 m

*"The cheapest decision-moving measurement left in the register."* **An item under two minutes is never
dropped** (charter §4). Run it, capture it with `--out`, record it whether it moves anything or not.

---

## Step 8 — fingerprint AFTER, the reversal check, and the CLOSING `V29`, ~12 m

```bash
bash /mnt/d/hf_w29/fp_w29.sh after                 # same sweep() expression, second call
git status --short && git diff --stat              # the reversal check
python3 /mnt/d/hf_w29/verify_derivation_w29.py /mnt/d/hf_w29 --out /mnt/d/hf_w29/vdrift_w29_FINAL.txt
```

**The reversal check, explicitly (design §8):** if **any** `.cs` file changed for any reason, or the
`/mnt/d/hf_w25/exe` dll hashes do not match `binary_provenance_w25.txt`, or the arm proved non-deterministic —
then **the unit suite is owed by COUNT** ([F37](../docs/followups.md), baseline **3997**, not the charter's
stale 3973) **and the 42 m `optimize` gate becomes owed** and the wave stops until both are run.

**The closing `V29` run is not optional at any budget.** It is [F95](../docs/followups.md)'s entire remedy:
the first run proves the checker works, the closing run proves the wave is clean, and this series has been
conflating them. **Keep `vdrift_w29.txt` beside `vdrift_w29_FINAL.txt`** so both states are on the record.
**Capture the `W29-FP` verdict LINE to a file** — wave 28's existed only in a terminal.

**Gate:** `W29-FP = PRESERVED`, populations asserted equal, and `vdrift_w29_FINAL.txt` reads `V-CLEAN` over a
root of every instrument the wave wrote.

---

## Step 9 — record, ~75 m

**First action of the results document is `ls -la --time-style=full-iso /mnt/d/hf_w29/`, with the timestamp
PRINTED at the top.** Any row still open at that instant is marked open **in place**, not reported as done.
Wave 19's document was falsified by an artifact written 33 seconds before its own commit.

1. `docs/synthetic-af-bank-followups-wave29-results.md` — every verdict quoted from an artifact and cited;
   the pre-registered rules **applied as written**; an unsatisfiable or under-specified clause reported as a
   **finding**, not repaired and not re-scored. Estimate **and** actual per step
   ([F21](../docs/followups.md)). What was not run, and its price.
2. `docs/followups.md` — the entries design §14 names: **F96** (conditional on `W29-S`), **F97**, **F98**, and
   the amendments to **F82** (in place: the *"truth of 9"* is a product-derived fixed point; name which
   caller; add the third candidate), **F81**, **F94**, **F95**, **F21**.
3. `docs/synthetic-af-bank-results-table.md` — wave 28's owed `K` column from `truthModel`
   (`max(hfrMin, hfrMinEffective)`) and the note that `D01`/`D02`/`D03` are the only three floored by the
   generator. **No 2–4 px band claim below the band** — `W28-B` is `B-SPLIT`.
4. `docs/waves30+-…` handoff, **or** design §11's cut 4: fold it into the results file's §12 and say so.

---

## Step 10 — close, ~20 m

```bash
git add -A && GIT_COMMITTER_NAME="George Hilios" \
  GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "W29 results: ..."
git push -u origin ghilios/synthetic-af-bank-followups-wave29
gh pr create --base develop --head ghilios/synthetic-af-bank-followups-wave29 --title "Wave 29: ..." --body "..."
```

**Never push `develop`.** Verify CI by **COUNT read out of the log**, never by the tick. Delete the status
cron.

---

## Standing constraints (apply to every step)

- **Only one `TestApp.exe` at a time.** Never run NINA during the arm. No fan-out.
- **Never rebuild any directory mid-wave** — every instrument is written at Step 1.
- **Pass scorers WSL paths** (`/mnt/d/...`). A Windows path yields `UNEVALUATED` and looks like a failed arm.
- **Read the output, not the exit code.** A background job reporting `exit 0` has repeatedly meant a driver
  aborted in one second; a `*_START` line alone is not evidence.
- **A `--self-test` dispatcher in a SOURCED file sees the parent's `$1`** — wave 26's fired inside its
  caller's `source` line, terminated it, and printed a clean PASS.
- Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"`. Every `table18` log carries **CRLF**;
  strip `\r` before any comparison. Wave roots at or before 23 carry a `0xE5` — read old logs on bytes.
- Commit with the privacy email, both author and committer.

## Stop conditions

- `RULE V29` not `V-CLEAN`, or its population ≤ 1 file → **stop**; nothing downstream runs.
- `W29-FP` not `PRESERVED` → **stop**; a read-only root was written.
- Any `.cs` file changed → Step 8's reversal fires: suite by COUNT **and** the 42 m gate, or stop.
- Two consecutive waves producing no finding worth a register entry → **stop and say so.** *"There is nothing
  left worth a wave"* is an acceptable and welcome answer.
