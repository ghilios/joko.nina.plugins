# Wave 28 — implementation plan

**Spec:** `docs/synthetic-af-bank-followups-wave28-design.md`. Read it first; this file only executes it.
**Controller runs every measurement.** Subagents' background processes die with their session and their tool
timeouts cap at 10 minutes.

**Order is load-bearing.** Steps 0–2 are blocking pre-flight. Step 5 (`W28-B`, the verdict) must be scored
**before** step 7 decides whether ship (2) is owed. Step 3's product change ships **unconditionally** and its
rule is fixed in the design before any data — do not renegotiate it after step 5.

---

## Step 0 — vessel, ~5 m

```bash
gh pr view 196 --json state,mergeable --jq '"\(.state) \(.mergeable)"'      # expect: OPEN MERGEABLE
cd /home/ghilios/src/hocus-focus
git checkout ghilios/synthetic-af-bank-followups-wave27 && git pull
git checkout -b ghilios/synthetic-af-bank-followups-wave28
mkdir -p /mnt/d/hf_w28
```

Commit the design and this plan **before any measurement**, with the privacy email:

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "W28 pre-registration: item 8 is the LOWER edge of the detector's 2-4 px calibrated band"
```

**Gate:** `git log --oneline -1` shows the pre-registration commit before step 4 runs anything.

---

## Step 1 — `verify_derivation_w28.py`, BLOCKING, ~30 m

Derive from `/mnt/d/hf_w27/verify_derivation_w27.py` (**not** the `.bak` — that is the pre-[F87](../docs/followups.md) copy).

1. `sed` the wave id forward; **shape-matched** token patterns, both affixes, `_` treated as a word character.
2. **Carry [F87](../docs/followups.md)'s repair forward and prove it did not regress.** `in_historical_block()` must not
   treat the **definition line of `HISTORICAL_BLOCKS`** as opening a historical literal. The regression test
   is direct: the checker must report findings **on its own file** when its own file names `w27`, and must
   **not** report a wave-27 token inside a declared historical block.
3. **Self-test clause `[6]` carried forward**: a real historical block is exempt · the file after it is not ·
   the definition line opens nothing. Both ends.
4. **Both FAIL fixtures pinned and kept:** `/mnt/d/hf_w24` **and** `/mnt/d/hf_w26`. Neither is "whatever came
   before" ([F80](../docs/followups.md)).

```bash
python3 /mnt/d/hf_w28/verify_derivation_w28.py --self-test --out /mnt/d/hf_w28/vdrift_selftest_w28.txt
python3 /mnt/d/hf_w28/verify_derivation_w28.py --root /mnt/d/hf_w28 --out /mnt/d/hf_w28/vdrift_w28.txt
```

**Gates — READ THE OUTPUT, NOT THE EXIT CODE:**
- `vdrift_selftest_w28.txt` ends with `SELFTEST V28 <n> of <n>`, `n` equal on both sides, and clause `[6]`
  present with both ends;
- both pinned fixtures **refuse** with a non-zero finding count, each printed;
- `vdrift_w28.txt` verdict is `V-CLEAN` **and** its banner reads `RULE V28`, not `V27`. **A `V-CLEAN` from a
  checker printing the previous wave's rule name is exactly [F87](../docs/followups.md) and blocks the wave.**

**Nothing downstream runs until this passes.**

---

## Step 2 — read-only fingerprint, BEFORE, ~10 m

`/mnt/d/hf_w25/table18/` and `/mnt/d/SyntheticAutofocusBank/` are read-only for the whole wave.

**[F91](../docs/followups.md): ONE expression, called twice.** Write `fp_w28.sh` exposing a single
`sweep <root> <outfile>` function used for both BEFORE and AFTER. No second `find`.

```bash
find "$ROOT" -type f -print0 | sort -z | xargs -0 sha256sum
```

- `-print0 | sort -z | xargs -0` — `/mnt/d/Autofocus Bank` contains a space and plain `xargs` splits it.
- **Assert the population size inside the file** (`POPULATION <n>`); it is the only thing that catches a
  false `VIOLATED`.

**Gate:** `fp_BEFORE.txt` exists with its asserted count printed. AFTER is step 8.

---

## Step 3 — ship (1): the false in-band statement, ~60 m + suite

**Unconditional (design §9). Product code, no measurement.**

File: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/DetectionBinningResolver.cs`

Two defects, one cause — the recommendation is one-sided because `Clamp` floors at 1:

1. `DescribeRecommendationDetail`'s else-branch (`:100-104`) asserts *"{0:0.0} px is already in that range"*
   for **any** `recommended == 1`, including 0.7 px. Split the branch: **in band** keeps today's wording;
   **below band** says the measurement is below the range the detector is calibrated for, that binning
   cannot help (it only divides), and points at the axis that does — `MinHFR`
   (cf. [F35](../docs/followups.md), [F43](../docs/followups.md): the only axis that rescues an under-sampled rig).
2. `ShouldShowRecommendation` (`:120-121`) returns **false** below the band, so the row is hidden from
   exactly the users who need it. It must return **true** when the measurement is below the band.

Add a named boundary constant rather than a literal — the band's edges are already implied by
`TargetHfrPixels`; make the lower edge explicit and reference it from both call sites so they cannot drift.

**Tests** (`Joko.NINA.Plugins.HocusFocus.Tests/`), **three cases so the below-band case cannot pass by
accident**:

| case | HFR | expect |
|---|---|---|
| below band | 0.7 | detail does **not** contain "already in that range"; `ShouldShowRecommendation(1, 0.7)` is **true** |
| in band | 3.0 | detail **does** say in-range; `ShouldShowRecommendation(1, 3.0)` is **false** |
| above band | 6.0 | `RecommendFromHfr` is 2; detail describes the division |

**Do NOT change `RecommendFromHfr`.** It is the single source of truth cited by the options page, the wizard
and the documentation; changing what it returns changes detection behaviour on upgrade. **This step changes
what the user is TOLD, and nothing else.** State that in the commit message.

**Gate:** suite green at **≥ 3973 + 3**, verified **by COUNT out of the log** ([F37](../docs/followups.md)), never by the
tick. Kill stragglers first; `dotnet test <sln>` does **not** build `TestApp` — build it separately.

```bash
powershell.exe -NoProfile -Command "Get-Process testhost,vstest.console -EA SilentlyContinue | %{ \$_.Kill() }"
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo 2>&1 | tee /mnt/d/hf_w28/suite_AFTER_ship1.log
```

---

## Step 4 — `RULE W28-S` (source), ~30 m, zero compute

`score_w28s.py --out <file>`, **refusing without `--out`**. Reads `StarDetector.cs` at HEAD, records its
**sha256** in the output, and asserts:

| clause | assertion |
|---|---|
| `S-a` | the `ConvolveGaussian(structureMap, structureMap, …)` call's third argument is literally `p.StructureLayers * 2 + 1` |
| `S-b` | no `StarDetectorParams` member controls the post-wavelet blur width independently |
| `S-c` | `EffectiveStructureLayers(p)` reaches the residual (`:611`/`:619`/`:622`) and **not** the blur |

**Count assertion (F68 part 5):** the scorer asserts it matched **exactly one** `ConvolveGaussian` call site
in `BuildDetectionContextInternal` and **refuses on any other count** — a second copy must not be silently
read past.

**Self-test, both ends, captured:** run each clause against a constructed fixture where it is false. Print
`SELFTEST W28S <n> of <n>` to the output file.

**Gate:** `w28s_score.txt` prints a verdict in `{S-WELDED, S-SEPARATE, S-UNEVALUATED}` and the file sha256.
A missing file or line is `S-UNEVALUATED`, **never a pass**.

> **This is a finding, not a ship gate** (design §4/§9). It holds at the commit read during pre-registration;
> a conjunct known-true at pre-registration must not be dressed as a gate.

---

## Step 5 — `RULE W28-B` (the verdict), ~75 m, ~1 min compute

**The blind half. Score it before step 7.**

`score_w28b.py --manifest <tsv> --rule W28-B --out <file>`:

- takes its **rule label as an argument** and asserts it against the manifest ([F90](../docs/followups.md));
- **refuses without `--out`**;
- reads paths **through the manifest**, never a template.

**Manifest** (`w28b_manifest.tsv`), built from one expression: 20 rows of
`dataset · log path · synthetic_meta path · class`. `class` is **computed** from
`K = max(hfrMin, hfrMinEffective)`, never typed.

**Population, computed not typed:** `POP = set(all 20) − EXCLUDED`, `EXCLUDED = {D01, D02, D03}` as a named
constant. Assert `len(POP) == 17` and print it.

**Parsing gates, before any row is read:**
- the `PER-FRAME (sorted by focuser)` header line matches exactly
  `focuser  dsteps  golden  acc   TP   FP   FN   prec   recall   f1`; refuse otherwise;
- **the scorer refuses to read any `OVERALL` or `ATTRIBUTION` line from the 17** — that is the blindness
  boundary of design §6 and it is enforced in code, not by intention;
- exactly 9 frame rows per dataset, asserted.

**Statistics** (design §5.2): `Δrecall` from the `recall` column (**ALL-TIER** — the log carries
`recall@high` only in `OVERALL`, and the results must never call `Δrecall` a high-tier statement);
`Δacc` from the `acc` column (**detector-only, no golden**).

**Clauses:** `B-1` sign agreement ≥ 13 of 14 · `B-2` prediction correct ≥ 12 of 14 with ≥ 1 per class ·
`B-3` **all 4 LOW have `Δacc < 0`**.

**Verdict tree — check total coverage at pre-registration and print `B-TREE-GAP` for any uncovered region**
(wave 27 §8.1). Domain: `B-BANDPASS` · `B-REFUTED` · `B-SPLIT` · `B-UNDERPOWERED` · `B-TREE-GAP`.

**EMPTY:** a dataset with no `PER-FRAME` block, < 3 frames, or no `truthModel.perFrame` is `CNL-NOFRAMES`,
leaves the denominator, prints as `k of 14` with **k computed**; `k < 10` ⇒ `B-UNDERPOWERED`, never a pass.

**Self-test, captured, both ends** — including at minimum: a fixture whose two routes disagree returns
`B-SPLIT`; a fixture with one LOW dataset positive returns `B-REFUTED`; a fixture missing a `PER-FRAME`
block returns `CNL-NOFRAMES` and a **computed** `k`; a fixture with a renamed header **refuses**. Print
`SELFTEST W28B <n> of <n>`.

**Gate:** `w28b_score.txt` carries the verdict, the computed populations (`17`, `14`, `k of 14`), the three
MID datasets excluded **by name**, and both routes per dataset.

---

## Step 6 — `RULE W28-N` (the arm), ~60 m, **5–8 min compute**

**No binary.** Build the arm tree by copying seedA0's optimized settings and editing **one field**.

```bash
mkdir -p /mnt/d/hf_w28/nc
# for each of D01,D02,D03 x NC in {landed,2.0,1.0}: copy the opt-results subtree, edit
# optimized_settings.json's NoiseClippingMultiplier only, and record the before/after sha256 of each file.
```

**[F89](../docs/followups.md): `cp -p` restores the pre-edit mtime.** `touch` every copied file after the copy; keep the
sha256 check, which guards content and says nothing about mtime.

Invocation mirrors the published `table18` one, differing **only** in `--opt-results` and a pinned
`--profile-id`; state the pin's demonstrated inertness **with its `n`**, never assumed. Redirect
`TestApp.exe`'s stdin: `< /dev/null`.

Run **sequentially** — one `TestApp.exe` at a time, no NINA. Confirm a live `TestApp` and a `*_START` line
before believing the arm is running; **a `*_START` line alone is not evidence**.

`score_w28n.py --manifest <tsv> --rule W28-N --out <file>`:

| clause | field | bar |
|---|---|---|
| `N-1` | `NoiseClip=` on the `key detector knobs:` line | intended value on **9 of 9** |
| `N-2` | `NO CANDIDATE (structure gap)` **inside the `HIGH TIER ONLY` block** | ≥ 25 % fall at `NC=1.0` on **3 of 3** |
| `N-3` | `precision` from `OVERALL` | ≥ 0.90 on **3 of 3** at `NC=1.0` |

**[F68](../docs/followups.md) part 5 — which copy:** the `NO CANDIDATE (structure gap)` line appears **twice** per log, once
in the aggregate block and once in `HIGH TIER ONLY` (`GoldenEvalRunner.cs:763` vs `:774`). **The aggregate
copy is the wrong one.** Anchor on the block header and assert exactly one line consumed.

**`N-1`'s FAIL end runs on a real cell:** one extra `golden eval` from an **unedited** copy, which must echo
the landed `NoiseClip`. A control that cannot fail is not a control.

**Verdict:** `N-RECOVERS` · `N-REFUTED` (`N-2` fails — §2.2's amplitude account is wrong, and the results say
so plainly) · `N-COSTED` (`N-2` holds, `N-3` fails) · `N-UNDERPOWERED` (`k < 3`). Tree checked for total
coverage.

**Estimate to record against actual ([F21](../docs/followups.md)):** 9 cells at the measured `golden eval` rate of
**6–46 s per 9-frame cell** (full 20-dataset arm 349 s) ⇒ **5–8 min**. The charter's §5 budget table has no
`golden eval` row; **record the actual and add one.**

---

## Step 7 — ship (2) decision, ~30 m if owed

**Read `w28b_score.txt` first.**

- `B-BANDPASS` ⇒ surface the below-band condition outside the wizard. The machinery exists —
  `HasUndersampledStars` (`StarDetectionOptimizerWizardVM.cs:237-238`), `MinHfrSeed.IsBelowGate`
  (`MinHfrSeed.cs:113-120`) — and is wizard-only. Re-run the suite by COUNT.
- **anything else ⇒ it does not ship**, and is recorded as a costed recommendation with its price. Do not
  renegotiate the rule after seeing the number.

---

## Step 8 — AFTER fingerprint, ~10 m

Call **the same `sweep` function** from step 2. Assert `BEFORE == AFTER` population size **before comparing
any hash** — that assertion is the only reason wave 27 caught a false `VIOLATED`
([F91](../docs/followups.md)).

**Gate:** `PRESERVED`, with both counts printed. A difference forces `UNEVALUATED` on any rule reading those
roots.

---

## Step 9 — record, ~45 m

1. `docs/synthetic-af-bank-followups-wave28-results.md` — verdicts, both estimate and actual, what was
   **not** run and its price, controller deviations, and a blindness statement matching design §6.
2. `docs/followups.md` — register entries. **Owed regardless of verdict:**
   - the **welded cutoff** (`StarDetector.cs:632`) and that it confounds
     [F43](../docs/followups.md)'s "fewer `StructureLayers` is worse";
   - the **one-sided band** (§7): a calibrated range enforced above and not below, with the false in-band
     string, and ship (1);
   - **item 8 was substantially worked** by F20/F35/F43/F44/F22 — amend those entries to cross-reference each
     other, since their being scattered is why the backlog called it untouched;
   - amend [F62](../docs/followups.md) — `RULE D27` and item 8 are **one band-pass phenomenon**, not two findings.
3. `docs/synthetic-af-bank-results-table.md` — add the **in-focus kernel HFR (px)** column from
   `truthModel`, and the note that `D01`/`D02`/`D03` are the **only three floored by the generator**
   (`hfrMin < hfrMinEffective`). It reorders the recall column into a monotone story and costs nothing.
4. `docs/waves29+-…` handoff — re-price the backlog. **[F82](../docs/followups.md) (`D01`'s step recommendation stalled at 3
   against a truth of 9) is better-evidenced than item 8 was and scores goal 2**; name it item 1.

---

## Step 10 — close, ~20 m

```bash
git status --short   # nothing untracked left behind -- F88: git diff does NOT list untracked files
```

Commit with the privacy email, push, open the PR, and **verify CI by COUNT out of the log**, not by the
conclusion field alone.

---

## Standing constraints (apply to every step)

- Instruments in `/mnt/d/hf_w28/`, **LF endings**.
- Every scorer taking `--out` **refuses without it**; `--self-test` is **never dispatched from a sourced
  file**; every self-test prints `SELFTEST <RULE> <n> of <n>` **into a file**.
- **Populations asserted. Ordinals computed, never typed. Paths through a manifest.**
- `-print0 | sort -z | xargs -0`.
- Scorers take **WSL paths** (`/mnt/d/...`); a Windows path yields UNEVALUATED and looks like a failed arm.
- **Never rebuild an arm's directory mid-wave.** Wave 28 needs **one** build (step 3) and it is not an arm
  binary — no `golden eval` cell runs against it.
- Logs ASCII. Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"`.
- **Read logs, not exit codes.**
- Never push `develop`.

## Stop conditions

- Steps 0–2 are blocking. If step 1 cannot be made to pass, **that is the wave's finding** — record it and
  stop; a derivation checker that cannot certify itself is [F87](../docs/followups.md) recurring.
- If `W28-B` returns `B-TREE-GAP`, **the rule is incomplete: report it, do not repair and re-score.**
  ([F14](../docs/followups.md) / `RULE D20` fence.)
- If the arm overruns, **cut step 6 first.** Steps 3, 4 and 5 carry the wave: (3) ships unconditionally,
  (4) is free, (5) is the verdict.
